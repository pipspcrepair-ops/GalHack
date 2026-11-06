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
/// <para><b>The seam</b> is in <c>Spaceflight.Seam.cs</c>: over an overlap band above the ceiling
/// the surface stays on screen, and turns into the globe and space as you climb.</para>
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

    Vector3 _leftSurfaceAt;
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

    float Ceiling => Game.Surface.Physics.Zone.Ceiling;

    /// <summary>
    /// The planet is made: orbit starts just over its cloud tops. This is the physics' one altitude
    /// switch, so weight, the air and the handling model's space rules end where the crossing is.
    /// </summary>
    internal void PlanetBuilt()
    {
        SurfaceZone s = Game.Surface;
        float highest = s.Planet.Map.Elevation.Max();
        float altitude = Rules.OrbitAltitude(s.Planet.Sky.Air.ToString(), highest);
        s.Physics.Zone.Ceiling = altitude;
        s.Ground.Ceiling = altitude;
        GD.Print($"orbit starts at {altitude:F0} m on this world ({s.Planet.Sky.Air} air, highest ground {highest:F0} m)");
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
        if (g.Suit.Dead) return;
        if (++_chartTick % 30 == 0) Chart();
        // Afloat in a structure you right yourself with its decks, so a door in a tilted hull fits you.
        Survivor s = g.Survivor;
        if (s.Seated == null && s.Body.Frame != null && s.Legs.Floating) Riding.SetUp(s.Body, N.Vector3.UnitY);
        if (_pending != null)
        {
            if (!Ledger.Committing) FinishCrossing();
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

    /// <summary>A couple of times a second: what is near you comes onto the charts.</summary>
    void Chart()
    {
        GameNode g = Game;
        Vector3 at = g.Survivor.WorldFeet;
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
        var planet = g.Surface.Planet;
        double x = Mathx.WrapX(at.X, planet.Width), z = Mathx.Wrap(at.Z, planet.Height);
        Charts.LookAround(planet.Pois.Select((p, i) => ($"poi{i}", p.Name, (double)p.Origin.X, (double)p.Origin.Z)), x, z, planet.Width, planet.Height);
        foreach (var (kind, d) in _deposits.Near(g.Surface, at))
            Charts.Find($"deposit:{Mathf.RoundToInt(d.X)},{Mathf.RoundToInt(d.Z)}", kind, Mathx.WrapX(d.X, planet.Width), Mathx.Wrap(d.Z, planet.Height), "deposit");
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
        Orbit = new OrbitZone(s.Planet, s.Data, Rules, SystemSeed(s.Spec), Ceiling, coverage)
        {
            ProcessMode = ProcessModeEnum.Disabled, Visible = false,
        };
        Ledger = new ZoneLedger(LedgerFolder ?? System.IO.Path.Combine(OS.GetUserDataDir(), "zones", $"{Game.Surface.Spec.Seed}"));
    }

    /// <summary>The start system's seed: the world's own, so the same world always has the same wrecks round it.</summary>
    public static ulong SystemSeed(PlanetSpec spec) => PoiGenerator.Gen.Hash.Combine(spec.Seed, 0x0B17UL);

    // ---- Up ------------------------------------------------------------------------------------

    void WatchSurface()
    {
        Vessel carrier = Carrier();
        float y = carrier != null ? carrier.Body.Position.Y : Game.Survivor.WorldFeet.Y;
        if (y > Ceiling * Rules.Crossing.PreloadAt) StartOrbit();
        if (y > Ceiling && Orbit?.Ready == true) BeginCrossing(carrier, up: true);
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
        _leftSurfaceAt = new Vector3(p.X, p.Y, p.Z);
        to.Align(x, z, p.X, p.Z);
        _aligned = true;
        to.Arrive(from.Sky.ToSun);
        HoldSurfaceBehind();
        Wake(to);
        Handover.Move(from, to, s, carrier, p, b.Rotation, b.Velocity);
        g.EnterZone(to);
        from.ProcessMode = ProcessModeEnum.Disabled; // the surface waits, exactly as it was left (still on screen: the seam)
        _surfaceFar = g.Camera.Far;
        _surfaceAir ??= g.Suit.Sim.Environment;
        g.Suit.Sim.Environment = Vacuum;
        Crossings.Add(new Crossing(true, x, z, GlobeMap.HeadingDegrees(b.Rotation), p, b.Rotation, b.Velocity, b.Position, b.Rotation, b.Velocity, to.Turn, watch.Elapsed.TotalMilliseconds, Ledger.Audit(Holders(), totals)));
        Report();
        g.Say(carrier != null ? $"In space: the {carrier.Name} has left the atmosphere" : "In space", Tones.Good);
        if (g.Progress.Discover("orbit", 60, "reached orbit")) g.Say("The start system's wrecks are out here: fly close, step out, board one", Tones.Text);
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
            Vector3 centre = orbit.ToWorld(N.Vector3.Zero).ToGodot();
            bool inward = v.Dot((at - centre).Normalized()) < 0f;
            StreamAhead(inward && y < Ceiling + Rules.Crossing.Band * Rules.Crossing.StreamAhead, SurfacePoint(at, out _, out _));
            if (y < band && inward) AlignHere(at);
            return;
        }
        // In the band world coordinates are the surface's: what is under you is what you land on.
        StreamAhead(v.Dot((at - orbit.ToWorld(N.Vector3.Zero).ToGodot()).Normalized()) < 0f || y < Ceiling, at);
        if (y >= Ceiling - Rules.Crossing.Gap) return;
        if (Game.Surface.Streamer.ReadyAround(at, 1, needCollision: false))
        {
            BeginCrossing(carrier, up: false);
            return;
        }
        // The ground isn't there yet: the crossing waits at the edge until it is.
        Vector3 up = (at - orbit.ToWorld(N.Vector3.Zero).ToGodot()).Normalized();
        float inwards = v.Dot(up);
        if (inwards < 0f) b.Velocity -= (up * inwards).ToNumerics();
        (carrier?.Binding as BodyBinding ?? Game.Survivor.Binding).Teleport();
    }

    void Descend(Vessel carrier)
    {
        GameNode g = Game;
        var watch = Stopwatch.StartNew();
        OrbitZone from = Orbit;
        SurfaceZone to = g.Surface;
        Survivor s = g.Survivor;
        Body b = carrier?.Body ?? s.Body;
        N.Vector3 p = b.Position;
        double x = Mathx.WrapX(p.X, to.Planet.Width), z = Mathx.Wrap(p.Z, to.Planet.Height);
        var totals = ZoneLedger.Totals(Holders());

        ReleaseSurface();
        Wake(to);
        Handover.Move(from, to, s, carrier, p, b.Rotation, b.Velocity);
        g.EnterZone(to);
        Park(from); // orbit waits, wrecks as you left them
        _aligned = false;
        if (_surfaceAir != null) g.Suit.Sim.Environment = _surfaceAir;
        Crossings.Add(new Crossing(false, x, z, GlobeMap.HeadingDegrees(b.Rotation), p, b.Rotation, b.Velocity, b.Position, b.Rotation, b.Velocity, from.Turn, watch.Elapsed.TotalMilliseconds, Ledger.Audit(Holders(), totals)));
        Report();
        g.Say(carrier != null ? $"Into the atmosphere over {Where(x, z)}" : "Into the atmosphere", Tones.Good);
    }

    string Where(double x, double z)
    {
        double latitude = 90 - z / Game.Surface.Planet.Height * 180, longitude = x / Game.Surface.Planet.Width * 360;
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
        string id = carrier != null ? IdOf(carrier) : ZoneLedger.Castaway;
        if (Ledger.Begin(Holders(), id, up ? ZoneLedger.Orbit : ZoneLedger.Surface)) _pending = new Pending(carrier, up);
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
        Game.Say($"The crossing was refused ({Ledger.Trouble})", Tones.Warning);
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
        string castawayZone = aboard != null ? ZoneLedger.Cabin(IdOf(aboard)) : ZoneName(s.Zone);
        yield return new Holder(ZoneLedger.Castaway, "castaway", castawayZone, aboard != null ? IdOf(aboard) : null, "Castaway", g.Pack.Storage.Contents, 0);
        foreach (SurfaceZone zone in new SurfaceZone[] { g.Surface, Orbit })
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

    string ZoneName(SurfaceZone z) => z is OrbitZone ? ZoneLedger.Orbit : ZoneLedger.Surface;

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
            if (v == carrier || v.Flight == null || v.Controller?.Frozen != false) continue;
            if (g.Zone is OrbitZone orbit)
            {
                Vector3 p = v.Body.Position.ToGodot();
                if (orbit.AboveShell(p) > 1f) continue;
                Vector3 up = p.Normalized();
                v.Body.Position = (up * (orbit.ShellRadius + 1f)).ToNumerics();
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
    /// Space as the suit feels it: no air (the helmet seals, the suit's oxygen runs down), a sealed
    /// suit's slow chill, background radiation. Only a breathable room gives air: a sealed interior
    /// of a structure with a running core. A chair in an open hull is still in vacuum.
    /// </summary>
    Exposure Vacuum(Pos p, double time)
    {
        Survivor s = Game.Survivor;
        Vessel aboard = s.Aboard;
        bool room = aboard?.Structure.Core != null && aboard.Interior.Contains(s.Body.Position + s.Body.Up * 0.9f);
        return room
            ? new Exposure(20, 21, 0, VolumeKind.Room, aboard.Name, double.PositiveInfinity)
            : new Exposure(Rules.Space.VacuumC, 0, Rules.Space.Radiation, null, "Space", double.PositiveInfinity);
    }

    /// <summary>The visor's flight line in orbit: speed, height, the place under you.</summary>
    public string FlightLine(Vessel v)
    {
        Vector3 p = v.Body.Position.ToGodot();
        var (x, z) = Orbit.Under(p);
        string cruise = v.Flight.State.Cruise ? "   CRUISE" : "";
        return $"{v.Body.Velocity.Length():F0} m/s   ALT {Height(p):F0} m   OVER {Where(x, z)}{cruise}";
    }

    /// <summary>A direction in the local frame of the place under you (east, up, south), for the compass: north stays north in orbit.</summary>
    public Vector3 Local(Vector3 direction)
    {
        if (!Game.InOrbit) return direction;
        if (_aligned) return direction; // the world's axes are the local ones here
        var (x, z) = Orbit.Under(Game.Survivor.WorldFeet);
        N.Quaternion toGlobe = N.Quaternion.Conjugate(Orbit.Turn);
        return MathX.Unrotate(Orbit.Map.Frame(x, z), N.Vector3.Transform(direction.ToNumerics(), toGlobe)).ToGodot();
    }
}
