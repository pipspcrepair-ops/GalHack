using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Planets.Gen;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Items;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;
using SurvivalVitals.Model;
using N = System.Numerics;
using Survivor = Slice.Player.Survivor;
using GameNode = Slice.Game.Game;

namespace Slice.Orbit;

/// <summary>A crossing, as it happened (tests read these): where and how it went, in each zone's own frame.</summary>
public sealed record Crossing(bool Up, double MapX, double MapZ, double HeadingDegrees,
    N.Vector3 PositionBefore, N.Quaternion RotationBefore, N.Vector3 VelocityBefore,
    N.Vector3 PositionAfter, N.Quaternion RotationAfter, N.Vector3 VelocityAfter,
    N.Quaternion Frame, double Ms, IReadOnlyList<string> Audit);

/// <summary>
/// Climbing out of the atmosphere into orbit and coming back down, and what is different in space.
/// To the player the crossing is seamless: no loading screen, fade or effect, the destination made
/// ahead of them, their position, heading and velocity carried straight across.
///
/// <para><b>Up.</b> Once a climb passes part of the way to the atmosphere's top (the physics'
/// ceiling, where weight and air end), the orbit zone starts being made on worker threads, enters
/// the scene parked and is set up a piece a frame. At the top, what crosses (the vessel you fly
/// or ride, with you in it, or you alone) is recorded in orbit and committed off the main thread;
/// once that has landed it moves, in one tick, to the point over the globe that is the map point
/// under it, its height over sea level now its height over the globe's ground, its heading and
/// velocity turned into the globe's frame there.</para>
///
/// <para><b>Down.</b> The same in reverse, under the shell: the ground under you streams in ahead
/// of you on the way down, and the crossing waits until it's there. The map point under you is
/// where you come in, with the same heading (north stays north) and your velocity turned back into
/// the surface's frame. Landing: you come down where you crossed into the atmosphere; to land
/// somewhere, fly over it in orbit first (the design's rule).</para>
///
/// <para><b>The seam</b> is in <c>Spaceflight.Seam.cs</c>: the air thickens to the cap, the land hands over behind it,
/// and it thins to black space with the world curving away below.</para>
///
/// <para><b>In space</b> the suit breathes from its own oxygen (in vacuum the helmet seals and the
/// air runs down; a cockpit refills it), the survival tool's gather mode salvages a wreck's blocks
/// (about a third of each bill back), its core item claims a wreck with no core, and E forces a
/// wreck's dead doors. A vessel nobody is in waits at a zone's edge instead of crossing.</para>
///
/// A zone you aren't in stays in the scene, parked: disabled (which takes its colliders out of the
/// physics too) and hidden, exactly as it was left, so going back to it costs no reload.
/// </summary>
public partial class Spaceflight : Node
{
    public Game.Game Game;
    public OrbitRules Rules = OrbitRules.Shared;

    /// <summary>The orbit round the planet, once a climb out has started it.</summary>
    public OrbitZone Orbit { get; private set; }

    /// <summary>Where everything is, on disk (saves' persistence core), committed before each crossing.</summary>
    public ZoneLedger Ledger { get; private set; }

    /// <summary>What has been discovered, on every map level (shared by everyone in the world).</summary>
    public readonly Charts Charts = new();

    /// <summary>Every crossing so far, in order.</summary>
    public readonly List<Crossing> Crossings = new();

    /// <summary>Where the ledger's files go (tests point it at their own folder).</summary>
    public string LedgerFolder;

    /// <summary>The crossing waiting for its record to land (the vessel flies on meanwhile), or null.</summary>
    sealed record Pending(Vessel Carrier, bool Up);
    Pending _pending;


    float _surfaceFar = 12000f;
    Func<Pos, double, Exposure> _surfaceAir;
    readonly Dictionary<object, string> _ids = new(ReferenceEqualityComparer.Instance);
    int _nextId;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 6; // after the game's own after-physics step (5): positions are final
        ProcessPriority = 100;      // after the zones and the game have placed everything this frame
    }

    /// <summary>A zone waiting out of the scene is freed with the game (a made orbit that never entered it).</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && Orbit != null && IsInstanceValid(Orbit) && !Orbit.IsInsideTree()) Orbit.Free();
    }

    /// <summary>The crossing height of the body you are near, on or over, m.</summary>
    float Ceiling => Below.Physics.Zone.Ceiling;

    /// <summary>The highest ground of the body you are near, on or over, m.</summary>
    public float Highest
    {
        get
        {
            SurfaceZone s = Below;
            if (_highest.TryGetValue(s, out float h)) return h;
            if (!s.Arrived) return 0f;
            return _highest[s] = s.Planet.Map.Elevation.Max();
        }
    }

    /// <summary>The highest anything you were in stood on the surface, m: the cap holds it (tests read this).</summary>
    public float HighestInAir { get; private set; }

    /// <summary>Ticks spent waiting at the cap for the orbit to be ready (tests read this).</summary>
    public int TicksHeldAtCap { get; private set; }

    /// <summary>
    /// The planet is made: orbit starts just over its cloud tops. This is the physics' one altitude
    /// switch, so weight, the air and the handling model's space rules end where the crossing is.
    /// </summary>
    internal void PlanetBuilt()
    {
        SurfaceZone s = Game.Surface;
        _highest[s] = s.Planet.Map.Elevation.Max();
        float altitude = Rules.OrbitAltitude(s.Planet.Sky.Air.ToString());
        s.Physics.Zone.Ceiling = altitude;
        s.Ground.Ceiling = altitude;
        GD.Print($"orbit starts at {altitude:F0} m on this world ({s.Planet.Sky.Air} air, highest ground {Highest:F0} m)");
    }

    /// <summary>What crosses with you: the vessel you sit in or ride in its cabin (null: you alone).</summary>
    Vessel Carrier()
    {
        Survivor s = Game.Survivor;
        if (s.Seated != null) return s.Seated;
        Vessel aboard = s.Aboard;
        return aboard != null && !aboard.IsDerelict && aboard.Flight != null ? aboard : null;
    }

    public override void _PhysicsProcess(double delta)
    {
        GameNode g = Game;
        if (g.Phase != Phase.Playing || g.Survivor == null) return;
        OrbitComing();
        WeatherAloft(delta);
        if (g.Suit.Dead) return;
        if (++_chartTick % 30 == 0) Chart();
        // Afloat in a structure you right yourself with its decks, so a door in a tilted hull fits you.
        Survivor s = g.Survivor;
        if (s.Seated == null && s.Body.Frame != null && s.Legs.Floating) Riding.SetUp(s.Body, N.Vector3.UnitY);
        JetpackInZeroG(s);
        if (_pending != null)
        {
            if (Ledger.Committing) HoldAtCap(Carrier());
            else FinishCrossing();
        }
        else if (g.InOrbit) WatchOrbit();
        else WatchSurface();
        HoldOthersAtTheEdge();
    }

    /// <summary>The orbit, once made, enters the scene parked and is set up a piece a frame.</summary>
    void OrbitComing()
    {
        if (Orbit == null) return;
        if (Orbit.IsInsideTree()) Orbit.BuildStep();
        else if (Orbit.Prepared) Game.AddChild(Orbit);
    }

    int _chartTick;
    readonly DepositFinder _deposits = new();

    /// <summary>The chart key of the place in view from the bunker's way out (found from the start, alongside the bunker), or null if the start has none in view.</summary>
    public string StartPlaceKey { get; private set; }

    /// <summary>Charts the place in view from the bunker's way out, for good and from the first moment. Does nothing twice.</summary>
    public void ChartStartPlace()
    {
        var planet = Game.Surface?.Planet;
        if (planet == null || Game.Bunker?.Poi is not { } seen || Game.Bunker.Landmark == null) return;
        for (int i = 0; i < planet.Pois.Count; i++)
            if (planet.Pois[i] == seen)
            {
                Charts.Find($"poi{i}", seen.Name, seen.Origin.X, seen.Origin.Z, "place");
                StartPlaceKey = $"poi{i}";
                return;
            }
    }

    /// <summary>A couple of times a second: what is near you comes onto the charts.</summary>
    void Chart()
    {
        GameNode g = Game;
        Vector3 at = g.Survivor.WorldFeet;
        // Where you are is charted, and with it everything a jump away.
        if (g.Surface.Sky.Stars is { Here: >= 0 } stars) Charts.Visited.Add(stars.Here);
        if (g.InOrbit)
        {
            OrbitZone o = Orbit;
            foreach (Vessel w in o.Wrecks)
            {
                Vector3 p = w.Body.Position.ToGodot();
                N.Vector3 q = o.ToGlobe(p.ToNumerics());
                Charts.SeeWreck(w.Derelict.Id, w.Derelict.Name, p.DistanceTo(at), q.Length() / (o.GlobeRadius * 4f), MathF.Atan2(q.Z, q.X), w.Derelict.Site.Kind);
            }
            return;
        }
        if (g.Zone != g.Surface) return; // the moon has no places of its own yet
        var planet = g.Surface.Planet;
        double x = Mathx.WrapX(at.X, planet.Width), z = Mathx.Wrap(at.Z, planet.Height);
        Charts.LookAround(planet.Pois.Select((p, i) => ($"poi{i}", p.Name, (double)p.Origin.X, (double)p.Origin.Z)), x, z, planet.Width, planet.Height);
        // The place in view from the bunker's way out is known from the first moment; every other place waits to be come near (or pinged).
        ChartStartPlace();
        // A deposit is charted once you have been near it (a ping only marks it for a while).
        float near = g.Surface.Data.Progress.Xp.DepositRadius;
        foreach (var (kind, d) in _deposits.Near(g.Surface, at))
            if (new Vector2(d.X - at.X, d.Z - at.Z).Length() <= near) Charts.Find($"deposit:{Mathf.RoundToInt(d.X)},{Mathf.RoundToInt(d.Z)}", kind, Mathx.WrapX(d.X, planet.Width), Mathx.Wrap(d.Z, planet.Height), "deposit");
    }

    /// <summary>
    /// The orbit from the first moment of a game (the loading screen calls this until it's there): its
    /// globe is the far land past the surface's own at every height, on foot too. True once it's ready.
    /// </summary>
    public bool LoadOrbit()
    {
        StartOrbit();
        OrbitComing();
        return Orbit.Ready;
    }

    /// <summary>Starts making the orbit now, if it isn't already.</summary>
    public void StartOrbit()
    {
        if (Orbit != null) return;
        // The globe's clouds are the sky's: the same coverage over the map, as it stands now.
        SurfaceZone s = Game.Surface;
        PlanetSurface.Skies.CloudCoverage clouds = s.Sky.Clouds;
        float cover = MathF.Max(s.Sky.Air.CloudCover, (float)Rules.Globe.CloudCoverAtLeast), w = (float)s.Planet.Width, h = (float)s.Planet.Height;
        Vector2 drift = s.Sky.CloudDrift, scale = s.Sky.CloudScale;
        Func<float, float, float> coverage = clouds == null ? null : (u, v) => clouds.Coverage(u * w, v * h, cover, drift, scale);
        Orbit = new OrbitZone(s.Planet, s.Data, Rules, s.Planet.StarSystem.StarKey, Ceiling, coverage)
        {
            ProcessMode = ProcessModeEnum.Disabled, Visible = false, Surfaces = s.Sky.Surfaces,
        };
        Ledger = new ZoneLedger(LedgerFolder ?? System.IO.Path.Combine(OS.GetUserDataDir(), "zones", $"{Game.Surface.Spec.Seed}"));
        Orbit.Home.Surface = s;
        Orbit.Days = () => Below.Sky.Days;
        LightBodies(s);
        MakeMoon();
    }

    /// <summary>A surface's sky lights the other bodies in it as space lights their globes up close: no pop as one hands over to the other.</summary>
    void LightBodies(SurfaceZone s) => s.Sky.LightBodies(Rules.Space.SunEnergy, new Color(Rules.Space.Ambient[0], Rules.Space.Ambient[1], Rules.Space.Ambient[2]));

    /// <summary>The start system's seed: the world's own, so the same world always has the same wrecks round it.</summary>
    public static ulong SystemSeed(PlanetSpec spec) => PoiGenerator.Gen.Hash.Combine(spec.Seed, 0x0B17UL);

    /// <summary>The jetpack firing in zero g: floating free and pushing in some direction (its hiss, the figure's pose).</summary>
    public bool EvaThrusting { get; private set; }

    /// <summary>
    /// In zero g the jetpack is how you move, with the keys it has on the ground: WASD along your
    /// look, Space up and Ctrl down, in any direction; sprint pushes harder. Let go and you drift.
    /// </summary>
    void JetpackInZeroG(Survivor s)
    {
        var legs = s.Legs;
        legs.ZeroGSpeed = legs.Sprint ? Rules.Space.JetpackSprint : Rules.Space.JetpackSpeed;
        legs.ZeroGAccel = Rules.Space.JetpackAccel;
        EvaThrusting = s.Seated == null && s.OnBike == null && legs.Floating && (legs.Move.LengthSquared() > 1e-4f || MathF.Abs(legs.Vertical) > 1e-3f);
    }

    // ---- Up ------------------------------------------------------------------------------------

    void WatchSurface()
    {
        Vessel carrier = Carrier();
        float y = carrier != null ? carrier.Body.Position.Y : Game.Survivor.WorldFeet.Y;
        if (y > Ceiling * Rules.Crossing.PreloadAt) StartOrbit();
        if (y < Ceiling)
        {
            HighestInAir = MathF.Max(HighestInAir, y);
            return;
        }
        // The cap: nothing stands in the atmosphere over it, ever. It crosses here.
        HoldAtCap(carrier);
        // Hover only works close to the ground: a craft in Hover Mode never crosses, however it got up here.
        if (carrier?.Structure.Mode == CoreMode.Hover) return;
        if (Orbit?.Ready == true) BeginCrossing(carrier, up: true);
        else TicksHeldAtCap++;
    }

    /// <summary>What you are in stands at the cap, moving on: it keeps its speed for the crossing, which carries it on over.</summary>
    void HoldAtCap(Vessel carrier)
    {
        Body b = carrier?.Body ?? Game.Survivor.Body;
        if (b.Position.Y > Ceiling)
        {
            b.Position = b.Position with { Y = Ceiling };
            (carrier?.Binding as BodyBinding ?? Game.Survivor.Binding).Teleport();
        }
        HighestInAir = MathF.Max(HighestInAir, b.Position.Y);
    }

    void Ascend(Vessel carrier)
    {
        GameNode g = Game;
        var watch = Stopwatch.StartNew();
        SurfaceZone from = g.Zone;
        OrbitZone to = Orbit;
        Survivor s = g.Survivor;
        Body b = carrier?.Body ?? s.Body;
        N.Vector3 p = b.Position;
        double x = Mathx.WrapX(p.X, from.Planet.Width), z = Mathx.Wrap(p.Z, from.Planet.Height);
        var totals = ZoneLedger.Totals(Holders());

        // The orbit is laid out over the point under you: nothing about you changes.
        Celestial body = Here;
        _leftAt[body] = new Vector3(p.X, p.Y, p.Z);
        to.Align(body, x, z, p.X, p.Z);
        _aligned = true;
        HoldSurfaceBehind();
        Wake(to);
        Handover.Move(from, to, s, carrier, p, b.Rotation, b.Velocity);
        g.EnterZone(to);
        from.ProcessMode = ProcessModeEnum.Disabled;
        GD.Print($"[lap disabled {watch.Elapsed.TotalMilliseconds:F1}]"); // the surface waits, exactly as it was left (still on screen: the seam)
        _surfaceFar = g.Camera.Far;
        if (from == g.Surface) _surfaceAir ??= g.Suit.Sim.Environment;
        g.Suit.Sim.Environment = Vacuum;
        g.Suit.Beyond = Space;
        Crossings.Add(new Crossing(true, x, z, GlobeMap.HeadingDegrees(b.Rotation), p, b.Rotation, b.Velocity, b.Position, b.Rotation, b.Velocity, to.Turn, watch.Elapsed.TotalMilliseconds, Ledger.Audit(Holders(), totals)));
        Report();
        g.Progress.Discover("orbit", 60, "reached orbit");
    }

    // ---- Down ----------------------------------------------------------------------------------

    void WatchOrbit()
    {
        OrbitZone orbit = Orbit;
        Vessel carrier = Carrier();
        Body b = carrier?.Body ?? Game.Survivor.Body;
        Vector3 at = carrier != null ? b.Position.ToGodot() : Game.Survivor.WorldFeet;
        Vector3 v = (carrier != null ? b.Velocity : b.WorldVelocity).ToGodot();
        float y = Height(at);
        float band = Ceiling + Rules.Crossing.Band;
        if (!_aligned)
        {
            // Well ahead of the band the ground under you starts streaming in; at the band the
            // orbit is laid out over it. Moving everything rigidly changes nothing you can see.
            Vector3 centre = Here.Centre;
            bool inward = v.Dot((at - centre).Normalized()) < 0f;
            StreamAhead(inward && y < Ceiling + Rules.Crossing.Band * Rules.Crossing.StreamAhead, SurfacePoint(at, out _, out _));
            if (y < band && inward) AlignHere(at);
            return;
        }
        // In the band world coordinates are the surface's: what is under you is what you land on.
        StreamAhead(v.Dot((at - Here.Centre).Normalized()) < 0f || y < Ceiling, at);
        if (y >= Ceiling - Rules.Crossing.Gap) return;
        if (Below.Arrived && Below.Streamer.ReadyAround(at, 1, needCollision: false))
        {
            BeginCrossing(carrier, up: false);
            return;
        }
        // The ground isn't there yet: the crossing waits at the edge until it is.
        Vector3 up = (at - Here.Centre).Normalized();
        float inwards = v.Dot(up);
        if (inwards < 0f) b.Velocity -= (up * inwards).ToNumerics();
        (carrier?.Binding as BodyBinding ?? Game.Survivor.Binding).Teleport();
    }

    void Descend(Vessel carrier)
    {
        GameNode g = Game;
        var watch = Stopwatch.StartNew();
        OrbitZone from = Orbit;
        SurfaceZone to = Below;
        Survivor s = g.Survivor;
        Body b = carrier?.Body ?? s.Body;
        N.Vector3 p = b.Position;
        double x = Mathx.WrapX(p.X, to.Planet.Width), z = Mathx.Wrap(p.Z, to.Planet.Height);
        var totals = ZoneLedger.Totals(Holders());

        ReleaseSurface();
        Wake(to);
        to.Use();
        Handover.Move(from, to, s, carrier, p, b.Rotation, b.Velocity);
        g.EnterZone(to);
        Park(from);
        GD.Print($"[lap park {watch.Elapsed.TotalMilliseconds:F1}]"); // orbit waits, wrecks as you left them
        _aligned = false;
        if (to == g.Surface)
        {
            if (_surfaceAir != null) g.Suit.Sim.Environment = _surfaceAir;
            g.Suit.Beyond = null;
        }
        else
        {
            // A world with no air is space at your feet: the suit's own oxygen, as up there.
            g.Suit.Sim.Environment = Vacuum;
            g.Suit.Beyond = to.Planet.Sky.Density < SurfaceWeather.NoAir ? Space : null;
            g.Progress.Discover("moon", 120, "landed on the moon");
        }
        Crossings.Add(new Crossing(false, x, z, GlobeMap.HeadingDegrees(b.Rotation), p, b.Rotation, b.Velocity, b.Position, b.Rotation, b.Velocity, from.Turn, watch.Elapsed.TotalMilliseconds, Ledger.Audit(Holders(), totals)));
        Report();
    }

    string Where(double x, double z)
    {
        double latitude = 90 - z / Here.Map.Height * 180, longitude = x / Here.Map.Width * 360;
        return $"{Math.Abs(latitude):F0}°{(latitude >= 0 ? "N" : "S")} {longitude:F0}°E";
    }

    static void Wake(SurfaceZone zone)
    {
        zone.ProcessMode = ProcessModeEnum.Inherit;
        zone.Visible = true;
    }

    static void Park(SurfaceZone zone)
    {
        zone.ProcessMode = ProcessModeEnum.Disabled;
        zone.Visible = false;
    }

    // ---- The record, and the crossing's report -------------------------------------------------

    /// <summary>Everything's record and the crosser's new zone start for the disk; the crossing happens once they've landed.</summary>
    void BeginCrossing(Vessel carrier, bool up)
    {
        string id = carrier != null ? IdOf(carrier) : ZoneLedger.Sleeper;
        if (Ledger.Begin(Holders(), id, up ? ZoneLedger.Orbit : ZoneName(Below))) _pending = new Pending(carrier, up);
        else Refused();
    }

    void FinishCrossing()
    {
        Pending p = _pending;
        _pending = null;
        if (!Ledger.Finish())
        {
            Refused();
            return;
        }
        if (p.Up) Ascend(p.Carrier);
        else Descend(p.Carrier);
    }

    void Refused()
    {
        Game.Say($"CROSSING REFUSED · {Ledger.Trouble}", Tones.Warning);
        GD.PrintErr($"crossing refused: {Ledger.Trouble}");
    }

    void Report()
    {
        foreach (string problem in Crossings[^1].Audit) GD.PrintErr($"crossing audit: {problem}");
    }

    /// <summary>Everything that holds items or blocks, in both zones, with where it is (the ledger's view).</summary>
    public IEnumerable<Holder> Holders()
    {
        GameNode g = Game;
        Survivor s = g.Survivor;
        Vessel aboard = s.Seated ?? (s.Body.Frame != null ? s.Aboard : null);
        string sleeperZone = aboard != null ? ZoneLedger.Cabin(IdOf(aboard)) : ZoneName(s.Zone);
        yield return new Holder(ZoneLedger.Sleeper, "sleeper", sleeperZone, aboard != null ? IdOf(aboard) : null, "Sleeper", g.Pack.Storage.Contents, 0);
        foreach (SurfaceZone zone in new SurfaceZone[] { g.Surface, Orbit, MoonSurface })
        {
            if (zone?.Physics == null) continue;
            string name = ZoneName(zone);
            foreach (Vessel v in zone.Vessels)
            {
                Bill items = v.Storage.Contents;
                foreach (var c in v.Machines.Values) Add(items, c.Tray);
                yield return new Holder(IdOf(v), "structure", name, null, v.Name, items, v.Structure.Count);
            }
            foreach (Lootable l in zone.Loot.All)
                yield return new Holder(IdOf(l), "container", name, null, l.Name, l.Storage.Contents, 0);
            foreach (Device d in zone.Devices)
            {
                Bill items = d.Storage.Contents;
                if (d.Constructor != null) Add(items, d.Constructor.Tray);
                yield return new Holder(IdOf(d), "device", name, null, d.Label, items, 0);
            }
        }
    }

    static void Add(Bill to, Bill from)
    {
        foreach (var (id, n) in from) to.Add(id, n);
    }

    string ZoneName(SurfaceZone z) => z is OrbitZone ? ZoneLedger.Orbit : z == Game.Surface ? ZoneLedger.Surface : ZoneLedger.Moon;

    /// <summary>A record id that stays with a thing for the game.</summary>
    string IdOf(object thing)
    {
        if (_ids.TryGetValue(thing, out string id)) return id;
        id = thing switch
        {
            Vessel v when v == Game.Bunker?.Ruin => "bunker",
            Vessel { Derelict: not null } v => ("wreck-" + v.Derelict.Id)[..Math.Min(64, 6 + v.Derelict.Id.Length)],
            Vessel => $"structure-{++_nextId}",
            Lootable => $"container-{++_nextId}",
            Device => $"device-{++_nextId}",
            _ => $"thing-{++_nextId}",
        };
        _ids[thing] = id;
        return id;
    }

    // ---- Edges ---------------------------------------------------------------------------------

    /// <summary>A vessel nobody is in (holding course on its autopilot) waits at its zone's edge rather than crossing alone.</summary>
    void HoldOthersAtTheEdge()
    {
        GameNode g = Game;
        Vessel carrier = Carrier();
        foreach (Vessel v in g.Zone.Vessels)
        {
            if (v == carrier && v.Structure.Mode != CoreMode.Hover || v.Flight == null || v.Controller?.Frozen != false) continue;
            if (g.Zone is OrbitZone orbit)
            {
                Vector3 p = v.Body.Position.ToGodot();
                Celestial near = orbit.OwnerOf(p);
                if (near.AboveShell(p) > 1f) continue;
                Vector3 up = (p - near.Centre).Normalized();
                v.Body.Position = (near.Centre + up * (near.ShellRadius + 1f)).ToNumerics();
                float inward = v.Body.Velocity.ToGodot().Dot(up);
                if (inward < 0f) v.Body.Velocity -= (up * inward).ToNumerics();
            }
            else if (v.Body.Position.Y > Ceiling - 1f)
            {
                v.Body.Position = v.Body.Position with { Y = Ceiling - 1f };
                if (v.Body.Velocity.Y > 0f) v.Body.Velocity = v.Body.Velocity with { Y = 0f };
            }
            else continue;
            v.Binding.Teleport();
        }
    }

    // ---- The suit in space ---------------------------------------------------------------------

    /// <summary>
    /// Space as the suit feels it: the suit's own rule for rooms (a sealed room's air follows its
    /// structure's power, as on the surface), with <see cref="Space"/> outside. A chair in an open
    /// hull is still in vacuum.
    /// </summary>
    Exposure Vacuum(Pos p, double time) => Game.Suit.Exposure(p, time);

    /// <summary>Space itself: no air (the helmet seals, the suit's oxygen runs down), a sealed suit's slow chill, background radiation.</summary>
    public Exposure Space(Pos p, double time) => new(Rules.Space.VacuumC, 0, Rules.Space.Radiation, null, "Space", double.PositiveInfinity);
}
