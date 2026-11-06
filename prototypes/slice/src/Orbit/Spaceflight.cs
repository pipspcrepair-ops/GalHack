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

/// <summary>A crossing, as it happened (tests read these).</summary>
public sealed record Crossing(bool Up, double MapX, double MapZ, double HeadingDegrees, Vector3 From, Vector3 To, double Ms, IReadOnlyList<string> Audit);

/// <summary>
/// Climbing out of the atmosphere into orbit and coming back down, and what is different in space.
///
/// <para><b>Up.</b> Once a climb passes part of the way to the atmosphere's top (the physics'
/// ceiling, where weight and air end), the orbit zone starts being made off the main thread. A
/// tunnel fades in over the last metres. At the top, what crosses (the vessel you fly or ride,
/// with you in it, or you alone) is recorded in orbit and committed, then moved, in one tick, to
/// the point over the globe that is the map point under it, at its height over the top of the
/// atmosphere, its heading and velocity turned into the globe's frame there. The surface stays in
/// memory, exactly as it was left.</para>
///
/// <para><b>Down.</b> Under the globe's atmosphere the same in reverse: the map point under you is
/// where you come in, just under the ceiling, with the same heading (north stays north) and your
/// velocity turned back into the surface's frame. Landing: you come down where you crossed into
/// the atmosphere; to land somewhere, fly over it in orbit first (the design's rule).</para>
///
/// <para><b>In space</b> the suit breathes from its own oxygen (in vacuum the helmet seals and the
/// air runs down; a cockpit refills it), the survival tool's gather mode salvages a wreck's blocks
/// (about a third of each bill back), its core item claims a wreck with no core, and E cycles a
/// wreck's doors and airlocks. A vessel nobody is in waits at a zone's edge instead of crossing.</para>
/// </summary>
public partial class Spaceflight : Node
{
    public Game.Game Game;
    public OrbitRules Rules = OrbitRules.Shared;

    /// <summary>The orbit round the planet, once a climb out has started it.</summary>
    public OrbitZone Orbit { get; private set; }

    /// <summary>Where everything is, on disk (saves' persistence core), committed before each crossing.</summary>
    public ZoneLedger Ledger { get; private set; }

    /// <summary>Every crossing so far, in order.</summary>
    public readonly List<Crossing> Crossings = new();

    /// <summary>How far the tunnel has faded in, 0 to 1.</summary>
    public float Tunnel => _tunnel.Amount;

    /// <summary>A test's crash at one of a crossing's commit points (the zone test's crash check).</summary>
    public GalHack.Persistence.FaultInjector Faults;

    /// <summary>Where the ledger's files go (tests point it at their own folder).</summary>
    public string LedgerFolder;

    TunnelOverlay _tunnel;
    float _approach, _after, _hold;
    Vector3 _leftSurfaceAt;
    float _surfaceFar = 12000f;
    Func<Pos, double, Exposure> _surfaceAir;
    readonly Dictionary<object, string> _ids = new(ReferenceEqualityComparer.Instance);
    int _nextId;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 6; // after the game's own after-physics step (5): positions are final
        _tunnel = new TunnelOverlay { Name = "Tunnel" };
        AddChild(_tunnel);
    }

    /// <summary>A zone waiting out of the scene is freed with the game (it isn't anybody's child meanwhile).</summary>
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || Game == null) return;
        foreach (Node zone in new Node[] { Game.Surface, Orbit })
            if (zone != null && IsInstanceValid(zone) && !zone.IsInsideTree() && zone.GetParent() == null) zone.Free();
    }

    float Ceiling => Game.Surface.Physics.Zone.Ceiling;

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
        if (g.Phase != Phase.Playing || g.Survivor == null || g.Suit.Dead) return;
        float dt = (float)delta;
        if (g.InOrbit) WatchOrbit();
        else WatchSurface();
        HoldOthersAtTheEdge();
        if (_hold > 0f) _hold -= dt;
        else _after = MathF.Max(0f, _after - dt / Rules.Crossing.TunnelOut);
        _tunnel.Amount = MathF.Max(_approach, _after);
    }

    // ---- Up ------------------------------------------------------------------------------------

    void WatchSurface()
    {
        Vessel carrier = Carrier();
        Body b = carrier?.Body ?? Game.Survivor.Body;
        Vector3 at = carrier != null ? b.Position.ToGodot() : Game.Survivor.WorldFeet;
        float climb = (carrier != null ? b.Velocity : b.WorldVelocity).Y;
        if (Orbit == null && at.Y > Ceiling * Rules.Crossing.PreloadAt)
        {
            Orbit = new OrbitZone(Game.Surface.Planet, Game.Surface.Data, Rules, SystemSeed(Game.Surface.Spec));
            Game.Say("Leaving the atmosphere soon: orbit is coming into view", Tones.Text);
        }
        _approach = climb > 0f ? Mathf.Clamp(1f - (Ceiling - at.Y) / Rules.Crossing.TunnelFrom, 0f, 1f) : 0f;
        if (at.Y <= Ceiling) return;
        _approach = 1f; // over the top: the tunnel holds until orbit is ready
        if (Orbit != null && Orbit.Prepared) Ascend(carrier);
    }

    /// <summary>The start system's seed: the world's own, so the same world always has the same wrecks round it.</summary>
    public static ulong SystemSeed(PlanetSpec spec) => PoiGenerator.Gen.Hash.Combine(spec.Seed, 0x0B17UL);

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
        N.Quaternion frame = to.Map.Frame(x, z);
        float above = Rules.Crossing.ArriveAbove + MathF.Max(0f, p.Y - Ceiling);
        N.Vector3 position = to.Map.Direction(x, z) * (to.AtmosphereRadius + above);
        N.Quaternion rotation = N.Quaternion.Normalize(frame * b.Rotation);
        N.Vector3 velocity = N.Vector3.Transform(b.Velocity, frame);
        double heading = GlobeMap.HeadingDegrees(b.Rotation);
        if (!Record(carrier, ZoneLedger.Orbit)) return;

        _leftSurfaceAt = new Vector3(p.X, p.Y, p.Z);
        if (!to.IsInsideTree()) g.AddChild(to);
        to.Arrive(from.Sky.ToSun, new Basis(frame.ToGodot()));
        Handover.Move(from, to, s, carrier, position, rotation, velocity);
        g.EnterZone(to);
        g.RemoveChild(from); // the surface waits, exactly as it was left
        _surfaceFar = g.Camera.Far;
        g.Camera.Far = Rules.Space.CameraFar;
        _surfaceAir ??= g.Suit.Sim.Environment;
        g.Suit.Sim.Environment = Vacuum;
        Crossed(true, x, z, heading, new Vector3(p.X, p.Y, p.Z), position.ToGodot(), watch.Elapsed.TotalMilliseconds);
        g.Say(carrier != null ? $"In orbit: the {carrier.Name} is in space" : "In orbit", Tones.Good);
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
        float above = orbit.AboveAtmosphere(at);
        bool inward = v.Dot(at.Normalized()) < 0f;
        _approach = inward ? Mathf.Clamp(1f - above / Rules.Crossing.TunnelFrom, 0f, 1f) : 0f;
        if (above < 0f) Descend(carrier);
    }

    void Descend(Vessel carrier)
    {
        GameNode g = Game;
        var watch = Stopwatch.StartNew();
        OrbitZone from = Orbit;
        SurfaceZone to = g.Surface;
        Survivor s = g.Survivor;
        Body b = carrier?.Body ?? s.Body;
        var (place, local) = from.Map.ToSurface(b.Position, b.Rotation);
        N.Vector3 velocity = from.Map.VelocityToSurface(place.X, place.Z, b.Velocity);
        // The copy of that map point nearest where the surface was left, so everything there is
        // where it was relative to you (the surface's frame wraps with you, not with the map).
        float y = Ceiling - Rules.Crossing.ArriveBelow;
        Vector3 near = to.Planet.NearestCopy(new Vector3((float)place.X, y, (float)place.Z), _leftSurfaceAt);
        double heading = GlobeMap.HeadingDegrees(local);
        if (!Record(carrier, ZoneLedger.Surface)) return;

        g.AddChild(to);
        Handover.Move(from, to, s, carrier, near.ToNumerics(), local, velocity);
        g.EnterZone(to);
        g.RemoveChild(from); // orbit waits, wrecks as you left them
        g.Camera.Far = _surfaceFar;
        if (_surfaceAir != null) g.Suit.Sim.Environment = _surfaceAir;
        Crossed(false, place.X, place.Z, heading, b.Position.ToGodot(), near, watch.Elapsed.TotalMilliseconds);
        g.Say(carrier != null ? $"Into the atmosphere: the {carrier.Name} comes down over {Where(place.X, place.Z)}" : "Into the atmosphere", Tones.Good);
    }

    string Where(double x, double z)
    {
        double latitude = 90 - z / Game.Surface.Planet.Height * 180, longitude = x / Game.Surface.Planet.Width * 360;
        return $"{Math.Abs(latitude):F0}°{(latitude >= 0 ? "N" : "S")} {longitude:F0}°E";
    }

    // ---- The record, and the crossing's report -------------------------------------------------

    /// <summary>Commits where everything is, then the crosser's new zone. False (and nothing moves) if the disk refused.</summary>
    bool Record(Vessel carrier, string zone)
    {
        Ledger ??= new ZoneLedger(LedgerFolder ?? System.IO.Path.Combine(OS.GetUserDataDir(), "zones", $"{Game.Surface.Spec.Seed}"));
        string id = carrier != null ? IdOf(carrier) : ZoneLedger.Castaway;
        if (Ledger.Snapshot(Holders()) && Ledger.Cross(id, zone, Faults)) return true;
        Game.Say($"The crossing was refused ({Ledger.Trouble}): try again", Tones.Warning);
        GD.PrintErr($"crossing refused: {Ledger.Trouble}");
        _approach = 0f;
        return false;
    }

    void Crossed(bool up, double x, double z, double heading, Vector3 from, Vector3 to, double ms)
    {
        List<string> audit = Ledger.Audit(Holders());
        foreach (string problem in audit) GD.PrintErr($"crossing audit: {problem}");
        Crossings.Add(new Crossing(up, x, z, heading, from, to, ms, audit));
        _after = 1f;
        _hold = Rules.Crossing.TunnelHold;
        _approach = 0f;
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
                if (orbit.AboveAtmosphere(p) > 1f) continue;
                Vector3 up = p.Normalized();
                v.Body.Position = (up * (orbit.AtmosphereRadius + 1f)).ToNumerics();
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

    /// <summary>Space as the suit feels it: no air (the helmet seals, the suit's oxygen runs down), a sealed suit's slow chill, background radiation. A cockpit is sealed and aired.</summary>
    Exposure Vacuum(Pos p, double time)
    {
        if (Game.Suit.InCockpit) return Game.Suit.Exposure(p, time);
        return new Exposure(Rules.Space.VacuumC, 0, Rules.Space.Radiation, null, "Space", double.PositiveInfinity);
    }

    /// <summary>The visor's flight line in orbit: speed, height over the atmosphere, the place under you.</summary>
    public string FlightLine(Vessel v)
    {
        Vector3 p = v.Body.Position.ToGodot();
        var (x, z) = Orbit.Under(p);
        string cruise = v.Flight.State.Cruise ? "   CRUISE" : "";
        return $"{v.Body.Velocity.Length():F0} m/s   {Orbit.AboveAtmosphere(p):F0} m OVER THE AIR   OVER {Where(x, z)}{cruise}";
    }
}
