using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using PlanetSurface;
using Slice.Game;
using Slice.Items;
using Slice.Orbit;
using Slice.Surface;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Phase 2 played through, headless, by the game's own controls: the castaway builds the Skiff,
/// flies it straight up out of the atmosphere into orbit, flies to a derelict, steps out and
/// floats across, forces its dead hull door, goes in (dark, no gravity), takes what is in its hold,
/// salvages a block, claims it with a core on the way out, floats back to the Skiff, flies over
/// a chosen point on the globe, turns to a chosen heading, descends, and lands there. Every tick
/// it checks that the castaway and the Skiff are in exactly one zone; it checks the crossings'
/// mapping (the map point under you and your heading), the ledger, that everything on the surface
/// waited exactly as it was left, and the frame cost in orbit.
///
/// Setting up is the one shortcut, printed when it happens: the Skiff's parts and the Big Core
/// are handed over and the tech tree opened (the real-player test earns all of that). From there
/// on, only the game's inputs: no teleports, no grants.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/OrbitTest.tscn
/// </summary>
public partial class OrbitTest : Node
{
    const int WatchdogMinutes = 14;
    Game.Game _game;
    Driver _drive;
    Pilot _pilot;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Vessel _skiff;
    int _zoneViolations;
    string _firstViolation;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            System.Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100; // checks after the game has stepped
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try
        {
            more = _run.MoveNext();
            if (more) WatchZones();
        }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    void Fail(string what) => Check(false, what);

    /// <summary>
    /// Every tick: the castaway and the Skiff are each registered in exactly one zone's world, the
    /// zone the game says they are in, and the castaway's body node is under that zone (or in a
    /// cabin inside it). Never in both, never in neither.
    /// </summary>
    void WatchZones()
    {
        if (_game?.Survivor == null) return;
        WatchSky();
        SurfaceZone surface = _game.Surface, orbit = _game.Orbit;
        void Look(Body b, SurfaceZone said, string what)
        {
            int n = Handover.WorldsHolding(b, surface, orbit);
            bool ok = n == 1 && said.Physics.Bodies.Contains(b) && said.IsInsideTree() && said == _game.Zone;
            if (ok) return;
            _zoneViolations++;
            _firstViolation ??= $"{what}: in {n} worlds at {_clock.Elapsed.TotalSeconds:F1} s";
        }
        Look(_game.Survivor.Body, _game.Survivor.Zone, "the castaway");
        if (_skiff != null && (_game.Survivor.Seated == _skiff || _skiff.Zone == _game.Zone)) Look(_skiff.Body, _skiff.Zone, "the Skiff");
        Node n = _game.Survivor.Node;
        while (n != null && n is not SurfaceZone) n = n.GetParent();
        if (n != _game.Zone)
        {
            _zoneViolations++;
            _firstViolation ??= $"the castaway's node is under {n?.Name ?? "nothing"}, not the zone they are in, at {_clock.Elapsed.TotalSeconds:F1} s";
        }
    }

    /// <summary>
    /// This scenario boards a derelict ship (door, rooms, hold), and a start system may hold no
    /// ship among its fields, pods, hulks and stations (a pick of its weighted sites, none promised).
    /// So a run takes the next fresh world that has one; never a kept list. A seed given on the
    /// command line stays, and fails below if it has no ship.
    /// </summary>
    static WorldChoice WorldWithAShip()
    {
        string[] args = OS.GetCmdlineUserArgs();
        WorldChoice world = WorldChoice.FromArgs(args, WorldChoice.Tested);
        if (WorldChoice.FromArgs(args) != null) return world;
        static bool HasAShip(WorldChoice w) => PoiGenerator.Gen.OrbitPlacement.Place(SurfaceZone.PoiCatalog.Library, Spaceflight.SystemSeed(w.Spec()), start: true)
            .Any(p => p.Zone is "near" or "orbit" && p.Site.Kind == "wreck");
        for (int i = 0; i < 64 && !HasAShip(world); i++)
        {
            GD.Print($"  (no derelict ship in the system of {world}: on to a fresh world)");
            world = WorldChoice.Fresh();
        }
        return world;
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldWithAShip() };
        AddChild(_game);
        _game.Spaceflight.LedgerFolder = System.IO.Path.Combine(OS.GetUserDataDir(), "orbit-test-zones");
        _drive = new Driver(_game);
        _pilot = new Pilot(_game, _drive);
        _game.Said += (text, _) => _said = text;
        foreach (int x in _drive.UntilPlaying()) yield return x;
        SurfaceZone zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        GD.Print($"world: {_game.World}");

        foreach (int x in BuildSkiff()) yield return x;
        if (_skiff == null) yield break;
        foreach (int x in ClimbOut()) yield return x;
        if (!_game.InOrbit) yield break;
        foreach (int x in ToTheDerelict()) yield return x;
        if (_wreck != null)
        {
            foreach (int x in Boarding()) yield return x;
            foreach (int x in BackToTheSkiff()) yield return x;
        }
        foreach (int x in Descent()) yield return x;
        foreach (int x in Persistence()) yield return x;
        foreach (int x in Handovers()) yield return x;
        Check(_zoneViolations == 0, $"every tick the castaway and the Skiff were in exactly one zone, the one the game says ({_zoneViolations} ticks wrong{(_firstViolation != null ? ": " + _firstViolation : "")})");
    }

    // ---- Setting up ----------------------------------------------------------------------------

    IEnumerable<int> BuildSkiff()
    {
        GD.Print("-- the Skiff, built by hand --");
        foreach (int x in new Playthrough(_game).MakeTool()) yield return x; // you wake empty-handed: the tool is made first, for what the boarding wants of it
        // SETUP SHORTCUT (printed): the Skiff's parts and the Big Core handed over, the tech tree
        // opened. The real-player test makes and earns all of it; this test is about orbit.
        foreach (var (item, n) in _game.Builder.TotalCost(_game.StarterPlan)) _game.Pack.Add(item, n);
        _game.Pack.Add("big-core", 1);
        _game.Progress.Creative = true;
        GD.Print($"  (setup: handed over the Skiff's parts and the Big Core, and opened the tech tree)");
        _game.Pack.Grid.SetBlock(3, PlayerInventory.BlueprintBlock);
        Vector3 pad = default;
        Vessel skiff = null;
        // As the staging does: a walk the ground blocks (a wall, a gully) leaves you too far off, so a few pads farther out are tried.
        for (int attempt = 0; attempt < 3 && skiff == null; attempt++)
        {
            pad = new Playthrough(_game).FlatSpot(_game.Start, 18f + attempt * 14f, span: 10f); // room for you to stand off it and see it
            Vector3 stand = pad + (pad - _game.Start).Normalized() * 7f;
            foreach (int x in _drive.WalkTo(new Vector3(stand.X, 0, stand.Z), 1.5f)) yield return x;
            if (_game.Pack.Selected != 3) foreach (int x in _drive.Tap(Key.Key4)) yield return x;
            foreach (int x in _drive.LookAt(pad, 6)) yield return x;
            // The ground's collision may still be coming in where you look: the ghost shows once it can go down.
            for (int i = 0; i < 300 && !_game.Builder.PlanShown; i++)
                foreach (int x in _drive.LookAt(pad, 1)) yield return x;
            foreach (int x in _drive.Click()) yield return x;
            skiff = _game.Builder.Underway;
            if (skiff == null) GD.Print($"  (the blueprint set nothing down at {pad}, feet {_game.Survivor.WorldFeet}: {_game.Builder.Verdict})");
        }
        if (skiff == null)
        {
            Fail($"the blueprint projector set nothing down (the tool says: {_game.Builder.Verdict}; shown {_game.Builder.PlanShown}, plan {_game.Builder.Plan != null}; pad {pad}, feet {_game.Survivor.WorldFeet}, slot {_game.Pack.Selected}, in hand {_game.Pack.InHand}, wish yaw {_game.Survivor.Yaw:F2})");
            yield break;
        }
        int stalls = 0;
        while (_game.Builder.Underway == skiff && stalls < 240)
        {
            int before = skiff.Structure.Count;
            _drive.Face(skiff.Body.Position.ToGodot());
            _game.Controls.Primary = true;
            yield return 0;
            stalls = skiff.Structure.Count > before ? 0 : stalls + 1;
        }
        _game.Controls.Primary = false;
        foreach (int x in _drive.Wait(5)) yield return x;
        Check(skiff.Structure.Count == _game.StarterPlan.Count, $"the Skiff is built block by block: {skiff.Structure.Count} of {_game.StarterPlan.Count}");
        _skiff = skiff;
    }

    // ---- Up ------------------------------------------------------------------------------------

    IEnumerable<int> ClimbOut()
    {
        GD.Print("-- straight up, out of the atmosphere --");
        Vessel v = _skiff;
        foreach (int x in _pilot.Board(v)) yield return x;
        Check(_game.Survivor.Seated == v, "F at the seat: at the Skiff's controls");
        HoldSkyForTheClimb();
        _left = _game.Spaceflight.Holders().ToList();
        float ceiling = _game.Surface.Physics.Zone.Ceiling;
        Vector3 start = v.Body.Position.ToGodot();
        double heading = GlobeMap.HeadingDegrees(v.Body.Rotation);
        _game.Controls.Hold(Key.Space);
        bool preloaded = false, sheetFromTheGround = true;
        int ticks = 0;
        float lastAir = 0f, airStep = 0f, highestFlying = 0f, shroudAtCap = 0f;
        Check(ceiling == 1060f, $"orbit starts at the cap on this world: {ceiling:F0} m (its highest ground {_game.Spaceflight.Highest:F0} m)");
        while (!_game.InOrbit && ticks++ < 60 * 150)
        {
            preloaded |= _game.Orbit != null && v.Body.Position.Y < ceiling;
            sheetFromTheGround &= _game.InOrbit || (_game.Orbit?.Ready == true && _game.Orbit.Globe.Visible && _game.Surface.SeaRim == _game.Surface.Streamer.HorizonRim);
            highestFlying = MathF.Max(highestFlying, v.Body.Position.Y);
            shroudAtCap = _game.Spaceflight.Shrouded;
            airStep = MathF.Max(airStep, MathF.Abs(_game.Spaceflight.AirGone - lastAir));
            lastAir = _game.Spaceflight.AirGone;
            yield return 0;
        }
        float airAtCrossing = lastAir;
        for (int i = 0; i < 10; i++)
        {
            airStep = MathF.Max(airStep, MathF.Abs(_game.Spaceflight.AirGone - lastAir));
            lastAir = _game.Spaceflight.AirGone;
            yield return 0;
        }
        Check(highestFlying <= 1060f + 0.01f && _game.Spaceflight.HighestInAir <= 1060f + 0.01f,
            $"nothing flew over the cap in the atmosphere: {highestFlying:F2} m at the most before the crossing, 1,060 m the limit");
        Check(_game.Spaceflight.TicksHeldAtCap == 0, $"the orbit was ready before the ship got there: it waited {_game.Spaceflight.TicksHeldAtCap} ticks at the cap");
        Check(shroudAtCap > 0.25f, $"the air's shroud was closed in at the cap: {shroudAtCap:P0} (this world's air: {_game.Surface.Planet.Sky.Air}, density {_game.Surface.Planet.Sky.Density:F2})");
        Check(_game.InOrbit && !_game.Surface.Streamer.Visible && !_game.Surface.Sea.Visible, "the land and the sea are gone from over the cap: the sheet is all the ground there is");
        Check(airAtCrossing is > 0.05f and < 0.5f && airStep < 0.02f,
            $"the atmosphere fades with height alone, through the crossing with no step: {airAtCrossing:P0} gone at the crossing, at most {airStep:P1} a tick");
        _game.Controls.ReleaseAll();
        Check(_game.InOrbit, $"the climb crossed into orbit after {ticks / 60f:F0} s ({_game.Spaceflight.Crossings.Count} crossing)");
        if (!_game.InOrbit) yield break;
        Check(preloaded, "orbit started loading below the atmosphere's top, before the crossing (the seam is hidden)");
        Check(sheetFromTheGround, "the far land stood behind the land all the way up, the sea ending with the land's own mesh: no ring of water past the land, and the sea never met the sky");
        Check(PlanetSurface.Terrain.TerrainStreamer.HorizonReach - 600f - 1200f >= 5800f, "the land's mesh thins away only where the sheet behind it is solid, however far off its centre you are");
        Crossing up = _game.Spaceflight.Crossings[0];
        OrbitZone orbit = _game.Orbit;
        GD.Print($"  crossed at map ({up.MapX:F0}, {up.MapZ:F0}) heading {up.HeadingDegrees:F1}°, in {up.Ms:F1} ms; orbit was made in {orbit.PrepareMs:F0} ms off the main thread");
        // Nothing about the Skiff changes at the crossing: the orbit is laid out over the point under it.
        Check(up.PositionBefore == up.PositionAfter && up.RotationBefore == up.RotationAfter && up.VelocityBefore == up.VelocityAfter,
            $"position, rotation and velocity carried straight across, to the last bit: at {up.PositionAfter}, {up.VelocityAfter.Length():F1} m/s");
        // And the globe agrees: the place under the Skiff there is the map point it left from.
        var (mx, mz) = orbit.Under(up.PositionAfter.ToGodot());
        double dx = Math.Abs(mx - up.MapX);
        dx = Math.Min(dx, orbit.Map.Width - dx);
        double off = Math.Sqrt(dx * dx + (mz - up.MapZ) * (mz - up.MapZ));
        Check(off < 2.0 && v.Zone == orbit && _game.Survivor.Seated == v,
            $"the globe has the Skiff over the map point it left from ({off:F2} m off), with the castaway still in its seat");
        N.Quaternion inGlobe = N.Quaternion.Normalize(N.Quaternion.Conjugate(orbit.Turn) * up.RotationAfter);
        var (_, local) = orbit.Map.ToSurface(orbit.ToGlobe(up.PositionAfter), inGlobe);
        double after = GlobeMap.HeadingDegrees(local);
        Check(HeadingError(after, heading) < 1.0, $"its heading over the globe is the heading it climbed with: {after:F1}° (was {heading:F1}°; north stays north)");
        Check(up.Audit.Count == 0, $"the crossing lost and doubled nothing: the ledger's totals and zones match the game's ({string.Join("; ", up.Audit)})");
        Check(_game.Spaceflight.Ledger.ZoneOf("castaway")?.StartsWith("cabin/") == true && _game.Spaceflight.Ledger.ZoneOf(IdOf(v)) == ZoneLedger.Orbit,
            $"on disk the Skiff is in orbit and the castaway in its cabin (crossing committed before anything moved: {_game.Spaceflight.Ledger.LastCommit?.Written} record written)");
        Check(_game.Surface.ProcessMode == ProcessModeEnum.Disabled && _game.Surface.Vessels.Contains(_game.Bunker.Ruin),
            "the surface waits, frozen, everything on it as it was left (the bunker among it)");
    }

    // ---- Out to a derelict -----------------------------------------------------------------------

    Vessel _wreck;
    Int3 _door;
    Vector3 Door => _wreck.ToWorld(StructureBridge.CellCenter(_wreck.Structure, _door));
    Vector3 Outward => MathX.Rotate(_wreck.Body.Rotation, N.Vector3.UnitX * Math.Sign(_door.X)).ToGodot();
    Vector3 CellAt(int x, int y, int z) => _wreck.ToWorld(StructureBridge.CellCenter(_wreck.Structure, new Int3(x, y, z)));
    /// <summary>Where the eye goes to bring the middle of the body to a cell: a suit floats at any tilt to a drifting hull, and only its middle fits a door's.</summary>
    Vector3 BodyAt(int x, int y, int z) => CellAt(x, y, z) + (_game.Survivor.WorldEye() - _game.Survivor.WorldFeet) * 0.5f;
    /// <summary>Half a metre up the hull's own axis: the suit's eye over a door's middle, so the legs clear its sill.</summary>
    Vector3 Lift => MathX.Rotate(_wreck.Body.Rotation, N.Vector3.UnitY * 0.5f).ToGodot();

    static readonly Int3[] Sides = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };

    /// <summary>
    /// The shortest way from a cell inside the hull, through its empty cells and its doors (open or
    /// shut), to a cell beside a cargo box; and that box. Never back out by the hull door taken in.
    /// </summary>
    (List<Int3> route, Int3 box) HoldRoute(Structure ship, Int3 from)
    {
        var (min, max) = ship.Bounds();
        bool Inside(Int3 c) => c.X >= min.X - 1 && c.X <= max.X + 1 && c.Y >= min.Y - 1 && c.Y <= max.Y + 1 && c.Z >= min.Z - 1 && c.Z <= max.Z + 1;
        bool Passable(Int3 c) => c != _door && (!ship.TryGet(c, out Block b) || ship.TypeOf(b).IsDoor);
        var came = new Dictionary<Int3, Int3> { [from] = from };
        var queue = new Queue<Int3>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            Int3 c = queue.Dequeue();
            foreach (Int3 d in Sides)
            {
                var n = new Int3(c.X + d.X, c.Y + d.Y, c.Z + d.Z);
                if (ship.TryGet(n, out Block nb) && ship.TypeOf(nb).IsContainer && !ship.TypeOf(nb).IsDoor && !(ship.TryGet(c, out Block cb) && ship.TypeOf(cb).IsDoor))
                {
                    var route = new List<Int3>();
                    for (Int3 at = c; at != from; at = came[at]) route.Add(at);
                    route.Add(from);
                    route.Reverse();
                    return (route, n);
                }
            }
            foreach (Int3 d in Sides)
            {
                var n = new Int3(c.X + d.X, c.Y + d.Y, c.Z + d.Z);
                if (!Inside(n) || came.ContainsKey(n) || !Passable(n)) continue;
                came[n] = c;
                queue.Enqueue(n);
            }
        }
        return (null, default);
    }

    /// <summary>Floats a route cell by cell (only where it turns, and either side of a door), forcing a shut door met on it by hand.</summary>
    IEnumerable<int> Along(Structure ship, List<Int3> route)
    {
        bool IsDoor(Int3 c) => ship.TryGet(c, out Block b) && ship.TypeOf(b).IsDoor;
        for (int i = 1; i < route.Count; i++)
        {
            Int3 c = route[i];
            bool last = i == route.Count - 1, turn = !last && (route[i + 1].X - c.X, route[i + 1].Y - c.Y, route[i + 1].Z - c.Z) != (c.X - route[i - 1].X, c.Y - route[i - 1].Y, c.Z - route[i - 1].Z);
            bool doorNext = !last && IsDoor(route[i + 1]);
            if (!(last || turn || doorNext || IsDoor(c) || IsDoor(route[i - 1]))) continue;
            foreach (int x in _pilot.FloatTo(() => BodyAt(c.X, c.Y, c.Z), 0.5f, 20f)) yield return x;
            if (doorNext && !ship.Get(route[i + 1]).Open)
            {
                Int3 d = route[i + 1];
                foreach (int x in _drive.LookAt(CellAt(d.X, d.Y, d.Z), 6)) yield return x;
                foreach (int x in _drive.Tap(Key.E)) yield return x;
            }
        }
        foreach (int x in _pilot.Stop()) yield return x;
    }

    IEnumerable<int> ToTheDerelict()
    {
        GD.Print("-- across space to a derelict --");
        OrbitZone orbit = _game.Orbit;
        Vessel v = _skiff;
        Check(orbit.Wrecks.Count >= 8, $"the start system has wrecks round the world: {orbit.Wrecks.Count} pieces from {orbit.Plans.Select(p => p.Site.Id).Distinct().Count()} kinds of place ({string.Join(", ", orbit.Plans.Select(p => p.Site.Id).Distinct())})");
        Check(orbit.Wrecks.All(w => w.IsDerelict && w.Structure.Core == null && w.Body.Core == null), "none of them has a core or any gravity");
        Vector3 from = v.Body.Position.ToGodot();
        _wreck = orbit.Wrecks.Where(w => w.Derelict.Site.Kind == "wreck" && w.Derelict.Door != null).OrderBy(w => w.Body.Position.ToGodot().DistanceTo(from)).FirstOrDefault();
        if (_wreck == null)
        {
            Fail("no derelict ship with a hull door in this system");
            yield break;
        }
        _door = _wreck.Derelict.Door.Value;
        GD.Print($"  flying to {_wreck.Name} ({_wreck.Derelict.Id}, {_wreck.Structure.Count} blocks, {from.DistanceTo(_wreck.Body.Position.ToGodot()):F0} m off)");
        double oxygen = _game.Suit.Vitals.Oxygen;
        foreach (Vector3 way in RoundTheGlobe(orbit, v.Body.Position.ToGodot(), Door + Outward * 11f))
            foreach (int x in _pilot.FlyTo(v, () => way, 200f, 120f)) yield return x;
        foreach (int x in _pilot.FlyTo(v, () => Door + Outward * 11f, 4f, 150f)) yield return x;
        float off = v.Body.Position.ToGodot().DistanceTo(Door + Outward * 11f);
        Check(off < 8f && v.Body.Velocity.Length() < 1.5f, $"the Skiff flew there and stopped {Door.DistanceTo(v.Body.Position.ToGodot()):F0} m off its door, {v.Body.Velocity.Length():F1} m/s");
        Check(v.Stats.TopSpeed > 100f, $"out here it flies by the space rules: top speed {v.Stats.TopSpeed:F0} m/s");
        Check(_game.Spaceflight.Band >= 1f && !_game.Surface.Visible && _game.Spaceflight.AirGone >= 1f, "the surface has gone from the view out here, whichever way it flew: the globe and space are all there is");
        Check(_game.Suit.Vitals.Oxygen < oxygen, $"an open hull is vacuum: the suit's oxygen runs down ({oxygen:F0} to {_game.Suit.Vitals.Oxygen:F0})");
    }

    /// <summary>Points to fly by when a straight line to a wreck would go through the globe (the far side of it): an arc over the limb at the height of the higher end. None if the line is clear.</summary>
    static List<Vector3> RoundTheGlobe(OrbitZone orbit, Vector3 from, Vector3 to)
    {
        N.Vector3 a = orbit.ToGlobe(from.ToNumerics()), b = orbit.ToGlobe(to.ToNumerics());
        float along = Math.Clamp(N.Vector3.Dot(-a, b - a) / (b - a).LengthSquared(), 0f, 1f);
        var ways = new List<Vector3>();
        if ((a + (b - a) * along).Length() > orbit.ShellRadius + 100f) return ways;
        float angle = MathF.Acos(Math.Clamp(N.Vector3.Dot(N.Vector3.Normalize(a), N.Vector3.Normalize(b)), -1f, 1f));
        float height = MathF.Max(MathF.Max(a.Length(), b.Length()), orbit.ShellRadius + 400f);
        int legs = (int)MathF.Ceiling(angle / (20f * MathF.PI / 180f)) + 1;
        N.Vector3 side = N.Vector3.Normalize(N.Vector3.Normalize(b) - N.Vector3.Normalize(a) * MathF.Cos(angle)), start = N.Vector3.Normalize(a);
        for (int k = 1; k < legs; k++)
        {
            float t = angle * k / legs;
            ways.Add(orbit.ToWorld((start * MathF.Cos(t) + side * MathF.Sin(t)) * height).ToGodot());
        }
        return ways;
    }

    IEnumerable<int> Boarding()
    {
        GD.Print("-- stepping out, across, and in --");
        Vessel v = _skiff;
        Structure ship = _wreck.Structure;
        int blocks = ship.Count;
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(20)) yield return x;
        Check(_game.Survivor.Seated == null && _game.Survivor.Body.Frame == v.Body && !_game.Survivor.Binding.Floating,
            "up from the seat, on the Skiff's deck: held by its own core's gravity");
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(8)) yield return x;
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(40)) yield return x;
        Check(_game.Survivor.Body.Frame == null && _game.Survivor.Binding.Floating, "off the deck and floating: mag boots off, nothing holds you");
        foreach (int x in _pilot.FloatTo(() => Door + Outward * 2.4f + Lift, 0.7f, 40f)) yield return x;
        Check(_game.Survivor.WorldEye().DistanceTo(Door) < 4f, $"floated across on the suit's thrusters to its hull door ({_game.Survivor.WorldEye().DistanceTo(Door):F1} m off)");
        bool shut = !ship.Get(_door).Open;
        foreach (int x in _drive.LookAt(Door, 6)) yield return x;
        Check(_game.UsePrompt() == "E  Force the door", $"the crosshair offers the door: '{_game.UsePrompt()}'");
        foreach (int x in _drive.Tap(Key.E)) yield return x;
        Check(shut && ship.Get(_door).Open, "E forces its dead door open by hand");
        int sx = Math.Sign(_door.X);
        // Lined up square on the opening first: a suit has a tenth of a metre to spare in a hull door, and thrusters only push it where it looks.
        foreach (int x in _pilot.FloatTo(() => BodyAt(_door.X + 2 * sx, _door.Y, _door.Z), 0.25f, 15f)) yield return x;
        foreach (int x in _pilot.FloatTo(() => BodyAt(_door.X - 2 * sx, _door.Y, _door.Z), 0.5f, 25f)) yield return x;
        Vector3 inside = _wreck.Body.ToLocal(_game.Survivor.WorldFeet.ToNumerics()).ToGodot();
        var (min, max) = ship.Bounds();
        Check(inside.X / 2f > min.X && inside.X / 2f < max.X && _game.Survivor.Binding.Floating && _game.Surface.Physics != null,
            $"in through the door and floating: no core, no gravity in a dead hull (at cell {inside / 2f}, hull x {min.X}..{max.X}, door {_door}, floating {_game.Survivor.Binding.Floating}, in space {_game.Surface.Physics != null})");
        foreach (int x in _drive.Tap(Key.L)) yield return x;
        Check(_game.Torch.Visible, "dark in there: the head torch on");

        // Through the hull to its nearest cargo box. A capital hull is many rooms on several decks, so
        // the way is found through its open cells and doors, forcing any shut door met on it.
        Int3 inside0 = new Int3(_door.X - 2 * sx, _door.Y, _door.Z);
        var (route, box) = HoldRoute(ship, inside0);
        if (route == null)
        {
            Fail($"no way through {_wreck.Name}'s open cells and doors from its hull door to a cargo box");
            yield break;
        }
        GD.Print($"  to the cargo box at {box}: {route.Count} cells from inside the door");
        foreach (int x in Along(ship, route)) yield return x;

        Vector3 BoxAt() => CellAt(box.X, box.Y, box.Z); // read live: a dead hull drifts
        foreach (int x in _pilot.FloatTo(BoxAt, 2.6f, 15f)) yield return x;
        foreach (int x in _pilot.Stop()) yield return x;
        foreach (int x in _drive.LookAt(BoxAt(), 8)) yield return x;
        // The suit floats at any tilt: if a corner is in the way of the look, in closer and look again.
        for (int k = 1; k <= 3 && string.IsNullOrEmpty(_game.UsePrompt()); k++)
        {
            foreach (int x in _pilot.FloatTo(BoxAt, 2.6f - 0.4f * k, 10f)) yield return x;
            foreach (int x in _pilot.Stop()) yield return x;
            foreach (int x in _drive.LookAt(BoxAt(), 8)) yield return x;
        }
        Bill hold = _wreck.Storage.Contents;
        string boxPrompt = _game.UsePrompt();
        foreach (int x in _drive.Tap(Key.E)) yield return x;
        foreach (int x in _drive.Wait(4)) yield return x;
        Check(_game.Opened?.Vessel == _wreck && !hold.IsEmpty, $"E on a cargo box ('{boxPrompt}', {_game.Survivor.WorldEye().DistanceTo(BoxAt()):F1} m off) opens the wreck's hold beside the bag: {_game.Zone.Data.Items.Describe(hold, 5)}");
        Bill before = _game.Pack.Storage.Contents;
        foreach (int x in _drive.ClickWidget("box:take-all")) yield return x;
        foreach (int x in _drive.Tap(Key.Tab)) yield return x;
        Check(_wreck.Storage.IsEmpty && hold.All(kv => _game.Pack[kv.Key] == before[kv.Key] + kv.Value), "TAKE ALL: everything in its hold is in the bag");

        // A block of the bulkhead, cut free with the survival tool.
        foreach (int x in _drive.Tap(Key.Key1)) yield return x;
        _game.Tool.Mode = Slice.Player.ToolMode.Gather;
        Vector3 eye = _game.Survivor.WorldEye();
        Int3 wall = ship.Blocks().Where(b => ship.TypeOf(b.block).Id == "steel").Select(b => b.pos)
            .OrderBy(p => CellAt(p.X, p.Y, p.Z).DistanceTo(eye)).First();
        Bill bag = _game.Pack.Storage.Contents;
        foreach (int x in _drive.HoldPrimary(CellAt(wall.X, wall.Y, wall.Z), 60 * 12, () => _game.Spaceflight.Salvaged > 0)) yield return x;
        foreach (int x in _drive.Wait(4)) yield return x;
        Bill got = _game.Spaceflight.SalvageTaken;
        Check(_game.Spaceflight.Salvaged == 1 && ship.Count == blocks - 1 && !got.IsEmpty && got.All(kv => _game.Pack[kv.Key] == bag[kv.Key] + kv.Value),
            $"held on a block, the survival tool cuts it free: {ship.Count} blocks left, {_game.Zone.Data.Items.Describe(got, 3)} in the bag (a third of its bill)");
        _salvagedTo = ship.Count;
        _lootedFrom = hold.Total;

        // Out by the way in, and claim it from outside with the Big Core.
        route.Reverse();
        foreach (int x in Along(ship, route)) yield return x;
        foreach (int x in _pilot.FloatTo(() => CellAt(_door.X - 2 * sx, 1, _door.Z), 1.0f, 25f)) yield return x;
        foreach (int x in _pilot.FloatTo(() => Door + Outward * 3f, 1.0f, 25f)) yield return x;
        foreach (int x in _pilot.Stop()) yield return x;
        Check(_game.Survivor.WorldEye().DistanceTo(Door) < 5f, "back out through its door");
        int coreSlot = _game.Pack.Grid.Slots.FindIndex(sl => sl.Item == "big-core");
        ItemGrid.Move(_game.Pack.Grid, coreSlot, _game.Pack.Grid, 4);
        foreach (int x in _drive.Tap(Key.Key5)) yield return x;
        Vector3 hull = CellAt(_door.X, 1, _door.Z + 2); // the flat of the hull beside the door
        foreach (int x in _drive.LookAt(hull, 8)) yield return x;
        bool claimed = false;
        _game.Spaceflight.Claimed += _ => claimed = true;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(4)) yield return x;
        Check(claimed && ship.Core != null && _wreck.Body.Core?.On == true && !_wreck.IsDerelict && _game.Pack["big-core"] == 0,
            $"the Big Core set into its hull claims it: it is yours, and its core holds anyone aboard to its floor (hand {_game.Pack.Hand}, bag {_game.Pack["big-core"]}, said: {_said})");
        foreach (int x in _drive.Tap(Key.Key1)) yield return x;
    }

    int _salvagedTo, _lootedFrom;
    string _said;

    IEnumerable<int> BackToTheSkiff()
    {
        GD.Print("-- back to the Skiff --");
        Vessel v = _skiff;
        Vector3 Seat() => v.ToWorld(v.Seat.Value);
        foreach (int x in _pilot.FloatTo(Seat, 2.3f, 40f)) yield return x;
        foreach (int x in _drive.LookAt(Seat(), 6)) yield return x;
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(5)) yield return x;
        _pilot.Seated();
        Check(_game.Survivor.Seated == v, $"floated back and into the seat (F); {_game.Suit.Vitals.Oxygen:F0} of {_game.Suit.Sim.OxygenCapacity:F0} oxygen left (the eye {_game.Survivor.WorldEye().DistanceTo(Seat()):F1} m from it, standing on {(_game.Survivor.Body.Frame == null ? "nothing" : _game.Survivor.Body.Frame == v.Body ? "the Skiff" : "another hull")}, floating {_game.Survivor.Binding.Floating}, moving {_game.Survivor.Body.WorldVelocity.Length():F1} m/s)");
    }

    // ---- Down ----------------------------------------------------------------------------------

    IEnumerable<int> Descent()
    {
        GD.Print("-- over a chosen point on the globe, and down --");
        Vessel v = _skiff;
        OrbitZone orbit = _game.Orbit;
        SurfaceZone surface = _game.Surface;
        Vector3 pad = FlatSpot(surface, _game.Start, 420f);
        double tx = Planets.Gen.Mathx.WrapX(pad.X, surface.Planet.Width), tz = Planets.Gen.Mathx.Wrap(pad.Z, surface.Planet.Height);
        const double heading = 45;
        float over = surface.Physics.Zone.Ceiling + orbit.Rules.Crossing.Band + 500f;
        Vector3 Above() => orbit.ToWorld(orbit.Map.Direction(tx, tz) * orbit.RadiusFor(over)).ToGodot();
        foreach (Vector3 way in RoundTheGlobe(orbit, v.Body.Position.ToGodot(), Above()))
            foreach (int x in _pilot.FlyTo(v, () => way, 200f, 120f)) yield return x;
        foreach (int x in _pilot.FlyTo(v, Above, 6f, 150f)) yield return x;
        Check(v.Body.Position.ToGodot().DistanceTo(Above()) < 12f, $"the Skiff flew to a point over the map's ({tx:F0}, {tz:F0}), {orbit.Under(v.Body.Position.ToGodot()).x:F0} by the globe");
        // Nose north-east over that point, top to the sky.
        N.Quaternion toWorld = N.Quaternion.Normalize(orbit.Turn * orbit.Map.Frame(tx, tz));
        Vector3 nose = N.Vector3.Transform(N.Vector3.Transform(-N.Vector3.UnitZ, GlobeChecks.Attitude(heading)), toWorld).ToGodot();
        Vector3 top = N.Vector3.Transform(N.Vector3.UnitY, toWorld).ToGodot();
        foreach (int x in _pilot.TurnTo(v, nose, top)) yield return x;
        var (lx, lz) = orbit.Under(v.Body.Position.ToGodot());
        // Straight down its own legs: Ctrl thrusts down.
        int crossings = _game.Spaceflight.Crossings.Count;
        bool streamed = false;
        var probe = new FrameProbe(this);
        _game.Controls.Hold(Key.Ctrl);
        for (int i = 0; i < 60 * 90 && _game.Spaceflight.Crossings.Count == crossings; i++)
        {
            streamed |= _game.Spaceflight.StreamingAhead;
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        Check(_game.Spaceflight.Crossings.Count == crossings + 1 && !_game.InOrbit, "down through the band it crosses back into the atmosphere");
        if (_game.InOrbit) yield break;
        Crossing down = _game.Spaceflight.Crossings[^1];
        GD.Print($"  crossed down at map ({down.MapX:F0}, {down.MapZ:F0}) heading {down.HeadingDegrees:F1}°, in {down.Ms:F1} ms");
        Check(streamed, "the ground under it streamed in ahead of the crossing");
        Check(down.PositionBefore == down.PositionAfter && down.RotationBefore == down.RotationAfter && down.VelocityBefore == down.VelocityAfter,
            $"position, rotation and velocity carried straight across, to the last bit: at {down.PositionAfter}, {down.VelocityAfter.Length():F1} m/s");
        double dx = Math.Abs(down.MapX - tx);
        dx = Math.Min(dx, surface.Planet.Width - dx);
        double offMap = Math.Sqrt(dx * dx + (down.MapZ - tz) * (down.MapZ - tz));
        Check(offMap < 60, $"it came in over the point it was over on the globe: {offMap:F1} m from the chosen map point");
        Check(HeadingError(down.HeadingDegrees, heading) < 3, $"heading kept: {down.HeadingDegrees:F1}° (north-east over the globe, north stays north)");
        Check(down.Audit.Count == 0, $"nothing lost or doubled on the way down ({string.Join("; ", down.Audit)})");
        Check(surface.ProcessMode == ProcessModeEnum.Inherit && orbit.ProcessMode == ProcessModeEnum.Disabled && !orbit.Visible && v.Zone == surface,
            "the surface is live again and orbit waits, parked");

        // Down on the gear, as a pilot would: the height under it sets the sink rate.
        int ticks = 0;
        float hurtBefore = v.Body.Damage; // what it took out there (a drifting wreck's knock) is not the landing's
        Vector3 target = surface.GroundAt(down.PositionAfter.X, down.PositionAfter.Z);
        while (!v.Controller.Frozen && ticks++ < 60 * 120)
        {
            float agl = v.AboveGround, vy = v.Body.Velocity.Y;
            float wanted = agl > 260f ? -60f : agl > 70f ? -20f : agl > 25f ? -8f : agl > 6f ? -3f : -1.2f;
            bool onGear = agl < 3f && MathF.Abs(vy) < 0.4f; // hands off, and it settles
            _game.Controls.Hold(Key.Ctrl, !onGear && vy > wanted + 0.5f);
            _game.Controls.Hold(Key.Space, !onGear && vy < wanted - 0.5f);
            _pilot.Creep(v, pad, agl > 3f);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(30)) yield return x;
        Vector3 end = v.Body.Position.ToGodot();
        double dxe = Math.Abs(Planets.Gen.Mathx.WrapX(end.X, surface.Planet.Width) - tx);
        dxe = Math.Min(dxe, surface.Planet.Width - dxe);
        double landed = Math.Sqrt(dxe * dxe + Math.Pow(Planets.Gen.Mathx.Wrap(end.Z, surface.Planet.Height) - tz, 2));
        double finalHeading = GlobeMap.HeadingDegrees(v.Body.Rotation);
        Check(v.Controller.Frozen && v.Body.Damage == hurtBefore, $"it landed on its gear, undamaged, {landed:F0} m from the chosen point (settled {v.Controller.Frozen}, damage {v.Body.Damage - hurtBefore:F2} on landing, {hurtBefore:F2} before it, {v.AboveGround:F1} m up, {v.Body.Velocity.Length():F1} m/s, ticks {ticks})");
        Check(landed < 150, $"the landing point is the point it came down over ({landed:F0} m off)");
        Check(HeadingError(finalHeading, heading) < 8, $"and its heading is the one it had over the globe: {finalHeading:F1}°");
        TheSkyCameBack();
    }

    // ---- Everything still there -----------------------------------------------------------------

    List<Holder> _left;

    IEnumerable<int> Persistence()
    {
        GD.Print("-- everything where it was --");
        if (_left == null) yield break;
        var now = _game.Spaceflight.Holders().ToList();
        var missing = new List<string>();
        foreach (Holder h in _left.Where(h => h.Zone == ZoneLedger.Surface && h.Id != "castaway"))
        {
            Holder back = now.FirstOrDefault(n => n.Id == h.Id);
            if (back.Id == null || back.Blocks != h.Blocks || back.Zone != h.Zone || !back.Items.Equals(h.Items)) missing.Add(h.Name);
        }
        Check(missing.Count == 0, $"every structure, device and container left on the surface is as it was: {_left.Count(h => h.Zone == ZoneLedger.Surface)} holders ({string.Join(", ", missing)})");
        Check(_game.Survivor.Seated == _skiff && _skiff.Structure.Count == _game.StarterPlan.Count && _skiff.Body.Damage == 0f, $"the Skiff came home whole and undamaged from the whole trip: {_skiff.Structure.Count} blocks, damage {_skiff.Body.Damage:F2}");
        if (_wreck != null)
            Check(_wreck.Structure.Count == _salvagedTo + 1 && _wreck.Storage.IsEmpty && _wreck.Structure.Core != null,
                $"orbit waits as it was left: the wreck salvaged ({_salvagedTo} blocks and the core), emptied ({_lootedFrom} items gone) and claimed");
    }

    // ---- The hand-over's frame cost --------------------------------------------------------------

    IEnumerable<int> Handovers()
    {
        GD.Print("-- the hand-over, three times up and down --");
        Vessel v = _skiff;
        float ceiling = _game.Surface.Physics.Zone.Ceiling;
        float gap = _game.Orbit.Rules.Crossing.Gap;
        var probe = new FrameProbe(this);
        var worst = new List<double>();
        _game.Spaceflight.WorstSeamMs = _game.Spaceflight.WorstSkyMs = 0;
        for (int round = 0; round < 3; round++)
        {
            _game.Controls.Hold(Key.Space);
            int crossings = _game.Spaceflight.Crossings.Count;
            for (int i = 0; i < 60 * 60 && v.Body.Position.Y < ceiling - 80f; i++) yield return 0;
            double top = 0;
            for (int i = 0; i < 60 * 20 && _game.Spaceflight.Crossings.Count == crossings; i++)
            {
                top = Math.Max(top, probe.LastMs);
                yield return 0;
            }
            for (int i = 0; i < 20; i++)
            {
                top = Math.Max(top, probe.LastMs);
                yield return 0;
            }
            _game.Controls.ReleaseAll();
            foreach (int x in _drive.Wait(30)) yield return x;
            _game.Controls.Hold(Key.Ctrl);
            crossings = _game.Spaceflight.Crossings.Count;
            for (int i = 0; i < 60 * 30 && _game.Spaceflight.Crossings.Count == crossings; i++)
            {
                top = Math.Max(top, probe.LastMs);
                yield return 0;
            }
            for (int i = 0; i < 20; i++)
            {
                top = Math.Max(top, probe.LastMs);
                yield return 0;
            }
            _game.Controls.ReleaseAll();
            worst.Add(top);
            foreach (int x in _drive.Wait(30)) yield return x;
        }
        probe.Remove();
        double best = worst.Min();
        GD.Print($"  (the seam and the skies took at most {_game.Spaceflight.WorstSeamMs:F2} ms of any frame across those crossings, the skies {_game.Spaceflight.WorstSkyMs:F2} ms of that: {_game.Spaceflight.WorstSeamParts})");
        Check(best < 33.4, $"no frame over 33 ms across a hand-over up and down, best of three: {best:F1} ms ({string.Join(", ", worst.Select(w => w.ToString("F1")))})");
        Check(_game.Spaceflight.Crossings.All(c => c.Audit.Count == 0 && c.PositionBefore == c.PositionAfter && c.VelocityBefore == c.VelocityAfter),
            $"all {_game.Spaceflight.Crossings.Count} crossings carried everything straight across");
    }

    string IdOf(Vessel v) => _game.Spaceflight.Holders().First(h => h.Name == v.Name && h.Type == "structure").Id;

    static double HeadingError(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    Vector3 FlatSpot(Vector3 near, float distance) => FlatSpot(_game.Zone, near, distance);

    static Vector3 FlatSpot(SurfaceZone zone, Vector3 near, float distance)
    {
        for (int i = 0; i < 64; i++)
        {
            float a = i * 0.61f, r = distance + (i / 16) * 6f;
            var p = new Vector3(near.X + MathF.Cos(a) * r, 0f, near.Z + MathF.Sin(a) * r);
            float h0 = zone.GroundAt(p.X, p.Z).Y, spread = 0f;
            for (int k = 0; k < 8; k++)
            {
                float b = k * MathF.Tau / 8f;
                spread = MathF.Max(spread, MathF.Abs(zone.GroundAt(p.X + MathF.Cos(b) * 4f, p.Z + MathF.Sin(b) * 4f).Y - h0));
            }
            if (h0 > 1f && spread < 0.35f) return zone.GroundAt(p.X, p.Z);
        }
        return zone.GroundAt(near.X + distance, near.Z);
    }
}
