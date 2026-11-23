using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PlanetSurface;
using PlanetSurface.Skies;
using ShipHandling.Handling;
using Slice.Game;
using Slice.Orbit;
using Slice.Surface;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// In-system warp, round trips, headless, by the game's own controls: the Skiff is built, a warp drive fitted (and lift
/// for its weight), flown up to orbit and given warp cells; then, twice over on the same fresh system: warp to another
/// world, down through its crossing and landed on it (a world no zone was made for before), back up, warp to a moon (a
/// short-range warp if it is that world's own), landed, up, a warp to a gas giant stopping at its high orbit, one to the
/// star stopping at its high orbit, and a warp home, landed. Every arrival is checked at the right body and orbit edge, clear of every body and wreck, facing it, at
/// rest, fuel spent as routed, the sleeper aboard; nothing of the zone left (its light, sky, sound, gravity, globes, input)
/// shows after; nothing lost in the ledger; and after the second loop the nodes, objects, physics bodies, drawn instances and
/// memory are back at the first loop's level. Frame times through every warp are printed and held to the budget.
///
/// Setting up is the one shortcut, printed when it happens: the Skiff's parts and its drive are handed over, the tech tree
/// opened, the warp cells given. Fresh system every run.
///
/// Windowed, at 60 fps, the first warp is also watched from the pilot's seat with the hull turned well away from where it
/// goes: the streaks must pour out of the point ahead of the nose, looking ahead and looking aside (<see cref="SeatStreaks"/>).
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/WarpTest.tscn
/// Windowed with shots: godot-q.ps1 ... res://tests/WarpTest.tscn -- --shots=&lt;folder&gt; [--minspec=1] [--loops=N]
/// --split=1: every stage of every warp split by engine stage and by system (<see cref="FrameSplit"/>, <see cref="SystemSplit"/>).
/// </summary>
public partial class WarpTest : Node
{
    const int WatchdogMinutes = 50;
    Game.Game _game;
    Driver _drive;
    Pilot _pilot;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Vessel _ship;
    string _shots;
    readonly Dictionary<string, string> _args = new();
    readonly List<double> _cpu = new(), _wall = new(), _parking = new();
    readonly Dictionary<string, List<double>> _byStage = new();
    double _worstCpu;
    string _worstAt = "";

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            System.Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            string[] kv = a.TrimStart('-').Split('=', 2);
            _args[kv[0]] = kv.Length > 1 ? kv[1] : "1";
        }
        _shots = _args.GetValueOrDefault("shots");
        if (_args.ContainsKey("minspec"))
        {
            Process.GetCurrentProcess().ProcessorAffinity = 0x55;
            PlanetSurface.Terrain.TerrainStreamer.Cores = 4;
            GD.Print($"min spec stand-in: affinity read back 0x{(long)Process.GetCurrentProcess().ProcessorAffinity:X}, {PlanetSurface.Terrain.TerrainStreamer.Cores} streaming cores, settings tests/minspec.cfg");
        }
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        if (!_timed) FrameTimes(); // a run cut short still says what its frames took
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

    Spaceflight Flight => _game.Spaceflight;
    StarSystem Model => _game.Surface.Planet.StarSystem;

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game
        {
            Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Fresh()),
            SettingsPath = _args.ContainsKey("minspec") ? "res://tests/minspec.cfg" : null,
        };
        AddChild(_game);
        _game.Spaceflight.LedgerFolder = System.IO.Path.Combine(OS.GetUserDataDir(), "warp-test-zones");
        _drive = new Driver(_game);
        _pilot = new Pilot(_game, _drive);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        if (Windowed) Engine.MaxFps = 60; // the owner's 60 Hz screen: every frame kept is one he sees
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        GD.Print($"world: {_game.World}");

        Fuel();
        var (planet, moon, giant) = Targets();
        if (planet == null) yield break;
        foreach (int x in _pilot.BuildSkiff(s => _ship = s)) yield return x;
        AirCells.Give(_game, 16);
        if (_ship == null) { Fail("the Skiff was not built"); yield break; }
        FitDrive(_ship, Lift(Model.Home, planet, moon));
        // Fuelled for the whole trip: generator fuel for hours of full thrust, the capacitor full, warp cells for every warp.
        var fuel = _ship.Power.Rules.Fuels[^1];
        _ship.Storage.Add(fuel.Id, 200);
        _ship.Power.ChargeMJ = _ship.Power.CapacityMJ;
        _ship.Storage.Add("warp-cell", 60);
        GD.Print($"  (setup: in the Skiff's hold 200 {fuel.Id} and 60 warp cells, its capacitor full: {Fuelled()})");
        foreach (int x in _pilot.Board(_ship)) yield return x;
        if (_game.Survivor.Seated != _ship && _ship.Stand is N.Vector3 stood)
        {
            // Set down out of reach (the projector's ray met a roof in a town): the setup moves its stand to flat ground by you.
            Vector3 spot = OrbitTest.FlatSpot(_game.Zone, _game.Survivor.WorldFeet, 30f);
            _ship.Stand = new N.Vector3(spot.X, _game.Zone.GroundAt(spot.X, spot.Z).Y + _ship.Body.Shape.FeetBelowOrigin + 0.1f, spot.Z);
            GD.Print($"  (setup: the Skiff stood out of reach at {stood.ToGodot()}: its stand moved to flat ground at {spot})");
            foreach (int x in _drive.Wait(10)) yield return x;
            foreach (int x in _pilot.Board(_ship)) yield return x;
        }
        if (_game.Survivor.Seated != _ship) { Fail("not at the Skiff's controls"); yield break; }
        foreach (int x in TakeOff("the start world")) yield return x;
        if (!_game.InOrbit) yield break;
        if (Windowed) foreach (int x in SkyAway("home-orbit-fresh-facing-away", v => _freshSky = v)) yield return x;
        // The same flight with no warp, as a baseline: a few seconds in orbit.
        var idle = new FrameProbe(this);
        for (int i = 0; i < 300; i++) { yield return 0; Frame(idle, "in orbit, no warp, Baseline"); }
        idle.Remove();
        _cpu.Clear();
        _wall.Clear();
        Refusals();
        foreach (int x in CallOff(planet, moon, giant)) yield return x;

        var levels = new List<(string When, Level At)>();
        int loops = int.Parse(_args.GetValueOrDefault("loops", "3"));
        for (int loop = 1; loop <= loops; loop++)
        {
            _loop = loop;
            GD.Print($"== round trip {loop} ==");
            foreach (int x in Leg(planet, land: true, $"{loop}: another world")) yield return x;
            if (!_ok) yield break;
            if (moon != null)
            {
                foreach (int x in Leg(moon, land: true, $"{loop}: a moon")) yield return x;
                if (!_ok) yield break;
            }
            if (giant != null)
            {
                foreach (int x in Leg(giant, land: false, $"{loop}: a gas giant")) yield return x;
                if (!_ok) yield break;
            }
            foreach (int x in Leg(Model.Star, land: false, $"{loop}: the star")) yield return x;
            if (!_ok) yield break;
            foreach (int x in Leg(Model.Home, land: true, $"{loop}: home")) yield return x;
            if (!_ok) yield break;
            foreach (int x in _drive.Wait(300)) yield return x;
            levels.Add(($"landed home after round trip {loop}", Measure()));
            GD.Print($"  level {loop}: {levels[^1].At}");
            if (loop < loops)
            {
                foreach (int x in TakeOff("home")) yield return x;
                if (!_game.InOrbit) yield break;
            }
        }
        if (levels.Count < 2) { FrameTimes(); yield break; }
        // Steady from the second loop on: the first makes every zone.
        Level a = levels[^2].At, b = levels[^1].At;
        Check(Within(b.Nodes, a.Nodes, 0.02, 40) && Within(b.Objects, a.Objects, 0.02, 200) && Within(b.Resources, a.Resources, 0.03, 60),
            $"nodes, objects and resources hold their level from one loop to the next: {b.Nodes} nodes (was {a.Nodes}), {b.Objects} objects ({a.Objects}), {b.Resources} resources ({a.Resources})");
        Check(Within(b.Bodies, a.Bodies, 0.0, 2) && Within(b.Colliders, a.Colliders, 0.02, 20), $"physics back at level: {b.Bodies} bodies in the zones' worlds (was {a.Bodies}), {b.Colliders} colliders in the tree ({a.Colliders})");
        Check(Within(b.Drawn, a.Drawn, 0.05, 60), $"drawn instances back at level: {b.Drawn} (was {a.Drawn})");
        Check(b.ManagedMb < a.ManagedMb * 1.15 + 40 && b.StaticMb < a.StaticMb * 1.10 + 64,
            $"memory back at level: managed {b.ManagedMb:F0} MB (was {a.ManagedMb:F0}), engine {b.StaticMb:F0} MB (was {a.StaticMb:F0})");
        Check(b.Zones == a.Zones, $"no zone made twice: {b.Zones} zones after both loops");
        Check(Flight.Jumps.TrueForAll(x => x.Running || x.From == null && x.Arrived?.Zone == null && x.To.Zone == null),
            $"no finished jump holds a zone, only their names ({Flight.Jumps.Count} jumps; the last from {Flight.Jumps[^1].FromName} to {Flight.Jumps[^1].ArrivedIn})");
        FrameTimes();
    }

    bool _ok = true;
    double _freshSky = -1;
    int _loop;

    /// <summary>No growth from one loop to the next (streaming makes what is loaded come and go, so less is fine).</summary>
    static bool Within(double now, double was, double share, double slack) => now - was <= Math.Max(was * share, slack);

    // ---- The system and the drive ---------------------------------------------------------------------------------

    /// <summary>Warp fuel comes from pentaxite, refined in a constructor; the drive burns a cell an AU.</summary>
    void Fuel()
    {
        var items = _game.Zone.Data.Items;
        var cell = items.Item("warp-cell");
        var ore = items.Item("pentaxite");
        Check(cell.Recipe != null && cell.Recipe.Inputs.Any(i => i.Item == ore) && cell.Recipe.Tier <= 1 && ore.DepositKind == "WarpFuel",
            $"warp cells are made from pentaxite in a constructor ({cell.Recipe?.Count} from {string.Join(", ", cell.Recipe?.Inputs.Select(i => $"{i.Count} {i.Item.Id}") ?? Array.Empty<string>())}, tier {cell.Recipe?.Tier}), and pentaxite is mined from warp fuel deposits");
        var drive = _game.Zone.Data.Power.Drives[Spaceflight.WarpDrive];
        Check(drive.Item == cell.Id && Math.Abs(drive.Per - 1) < 1e-9 && Math.Abs(drive.Spool - 6) < 1e-9, $"the warp drive burns {drive.Per} {drive.Item} a {drive.Unit} after a {drive.Spool:F0} s spool");
    }

    /// <summary>Another world with ground (light enough for the Skiff), a moon to go on to, and a gas giant, from the system's model.</summary>
    (Orbiter Planet, Orbiter Moon, Orbiter Giant) Targets()
    {
        StarSystem m = Model;
        double days = _game.Surface.Sky.Days;
        bool Light(Orbiter b) => b.Spec != null && !b.Spec.HighGravity && b.Spec.GravityScale <= 1.01;
        bool Start(Orbiter b) => b == m.Home || b.Parent == m.Home || m.Neighbour != null && (b == m.Neighbour || b.Parent == m.Neighbour);
        double Far(Orbiter b) => (m.Position(b, days) - m.Position(m.Home, days)).Length;
        Orbiter planet = m.Bodies.Where(b => b.Kind == OrbiterKind.Planet && !Start(b) && Light(b)).OrderBy(Far).FirstOrDefault()
            ?? m.Bodies.Where(b => b.Kind == OrbiterKind.Moon && !Start(b) && Light(b)).OrderBy(Far).FirstOrDefault();
        if (planet == null)
        {
            GD.Print("  (this system has no other world light enough for the Skiff to land on and lift off: nothing to warp to; rerun for another)");
            Fail("no other world to land on in this system");
            return (null, null, null);
        }
        Orbiter moon = m.Bodies.Where(b => b.Kind == OrbiterKind.Moon && b.Parent == planet && Light(b)).OrderBy(b => b.Orbit).FirstOrDefault()
            ?? m.Bodies.Where(b => b.Kind == OrbiterKind.Moon && !Start(b) && b != planet && Light(b)).OrderBy(Far).FirstOrDefault();
        Orbiter giant = m.Bodies.Where(b => b.Kind == OrbiterKind.GasGiant && b != m.Neighbour).OrderBy(Far).FirstOrDefault() ?? m.Neighbour;
        string Say(Orbiter b) => b == null ? "none" : $"{Charts.Tidy(b.Name)} ({b.Kind}{(b.Spec != null ? $", {b.Spec.WidthMeters / 1000:F0} km, {(b.Spec.Airless ? "airless" : "air")}, {b.Spec.GravityScale:F1} g" : "")}, {Far(b) / StarSystem.AuMetres:F2} AU out{(b.Parent != m.Star ? $", round {Charts.Tidy(b.Parent.Name)}" : "")})";
        GD.Print($"  the trip: {Say(planet)}, then {Say(moon)}, then {Say(giant)}, then home");
        return (planet, moon, giant);
    }

    static float Lift(params Orbiter[] bodies) => 9.81f * (float)bodies.Where(b => b?.Spec != null).Select(b => (double)b.Spec.GravityScale).DefaultIfEmpty(1.0).Max();

    /// <summary>The setup shortcut: a warp drive on the Skiff's back, and lift jets beside it till it climbs off the heaviest world of the trip.</summary>
    void FitDrive(Vessel v, float g)
    {
        Structure s = v.Structure;
        var cat = _game.Zone.Data.Catalogue;
        (Int3 lo, Int3 hi) = Bounds(s);
        Int3? at = null;
        // On its back if it fits, as near its middle as it goes.
        int mid = (lo.X + hi.X) / 2 - 1;
        var spots = from y in Enumerable.Range(1, hi.Y + 1) from z in Enumerable.Range(lo.Z, hi.Z - lo.Z + 1) from x in Enumerable.Range(lo.X - 1, hi.X - lo.X + 2)
                    orderby -y, Math.Abs(x - mid), z select new Int3(x, y, z);
        foreach (Int3 p in spots)
                {
                    if (at != null) break;
                    if (s.Has(p) || !s.Has(p + new Int3(0, -1, 0))) continue;
                    if (Building.Place(s, p, Building.Make(s, cat.Type("warp-drive"))) == PlaceError.None) at = p;
                }
        v.ApplyEdits();
        Check(at != null && v.Power.Range(Spaceflight.WarpDrive) != null, $"setup: a warp drive fitted on the Skiff's back at {at}");
        float up = v.Stats.Thrust[(int)Direction.Up];
        int added = 0;
        BlockType jet = cat.Type("thruster-t2");
        int[] rots = Enumerable.Range(0, 24).Where(r => Vessel.PushOf(Building.Make(s, jet, null, r)) == Direction.Up).ToArray();
        var errors = new Dictionary<PlaceError, int>();
        // Lift jets down both sides of the drive, pair by pair, till it climbs off the heaviest world of the trip.
        if (at is Int3 d)
            foreach (int z in Enumerable.Range(d.Z - 1, 7))
                foreach (int side in new[] { d.X - 1, d.X + 3, d.X - 2, d.X + 4 })
                    foreach (int y in new[] { d.Y, d.Y + 1, d.Y - 1 })
                    {
                        if (v.Stats.Thrust[(int)Direction.Up] >= v.Stats.Mass * g * 1.6f || added >= 14) break;
                        var p = new Int3(side, y, z);
                        if (s.Has(p)) continue;
                        foreach (int r in rots)
                        {
                            PlaceError e = Building.Place(s, p, Building.Make(s, jet, null, r));
                            if (e != PlaceError.None) { errors[e] = errors.GetValueOrDefault(e) + 1; continue; }
                            added++;
                            v.ApplyEdits();
                            break;
                        }
                    }
        GD.Print($"  (setup: {added} lift jets added beside the drive ({rots.Length} turns push up; refused: {string.Join(", ", errors.Select(e => $"{e.Key} {e.Value}"))}): up thrust {up:F0} -> {v.Stats.Thrust[(int)Direction.Up]:F0} for a mass of {v.Stats.Mass:F1} at {g:F1} m/s²)");
        Check(v.Stats.Thrust[(int)Direction.Up] > v.Stats.Mass * g * 1.25f, $"the Skiff with its drive can lift off the trip's heaviest world ({v.Stats.Thrust[(int)Direction.Up] / (v.Stats.Mass * g):F2} times its weight there)");
    }

    static (Int3, Int3) Bounds(Structure s)
    {
        var cells = s.Blocks().Select(b => b.Item1).ToList();
        return (new Int3(cells.Min(c => c.X), cells.Min(c => c.Y), cells.Min(c => c.Z)), new Int3(cells.Max(c => c.X), cells.Max(c => c.Y), cells.Max(c => c.Z)));
    }

    /// <summary>What stops a warp, as the console says it.</summary>
    void Refusals()
    {
        Orbiter home = Model.Home;
        float off = _ship.Body.Position.ToGodot().DistanceTo(_game.Orbit.Home.Centre), edge = Flight.HighOrbit(_game.Orbit, _game.Orbit.Home);
        string here = Flight.Route(home).Refusal;
        Check(off < edge + 500f ? here == "HERE" : here == null, $"a warp to the world you are over says HERE inside its highest orbit, and nothing past it ({off:F0} m out, the edge {edge:F0} m: {here ?? "clear"})");
        _ship.Power.ShieldsOn = true;
        string saved = Flight.Route(Model.Star).Refusal;
        Check(saved is null or "SHIELDS UP", $"with no shield on the Skiff, the shield switch can't stop it ({saved ?? "clear"})");
        int cells = _ship.Storage.Count("warp-cell");
        _ship.Storage.Remove("warp-cell", cells);
        Orbiter far = Model.Bodies.Where(b => b.Parent == Model.Star && b.Kind != OrbiterKind.Belt).OrderByDescending(b => b.Orbit).First();
        Check(Flight.Route(far).Refusal == "NO FUEL", $"with no warp cells the drive won't spool ({Flight.Route(far).Refusal}, {Flight.Route(far).Cells} cells to {Charts.Tidy(far.Name)})");
        _ship.Storage.Add("warp-cell", cells);
        WarpRoute r = Flight.Route(far);
        Check(r.Refusal == null && r.Cells == (int)Math.Ceiling(r.Au - 1e-9) && Math.Abs(r.TripSeconds - Math.Max(2, r.Au * 6)) < 0.01, $"the route to {r.Name}: {r.Au:F2} AU, {r.Cells} cells, {r.SpoolSeconds:F0} + {r.TripSeconds:F0} s");
        Check(_game.Visor != null && _ship.Power.Range(Spaceflight.WarpDrive)?.Reach == cells, $"the WARP gauge's reach is the cells' ({_ship.Power.Range(Spaceflight.WarpDrive)?.Reach:F0} AU)");
    }

    /// <summary>
    /// J again while the drive spools calls the warp off, nothing burnt; the zones made for it (a world nobody has gone to)
    /// are freed, and nothing holds on to them: not the jump's record, not its destination, not the flight.
    /// </summary>
    IEnumerable<int> CallOff(params Orbiter[] trip)
    {
        static Orbiter Root(Orbiter b)
        {
            while (b.Parent != null && b.Parent.Kind != OrbiterKind.Star) b = b.Parent;
            return b;
        }
        var taken = trip.Where(b => b != null).Select(Root).Append(Model.Home).ToHashSet();
        Orbiter fresh = Model.Bodies.FirstOrDefault(b => b.Parent == Model.Star && b.Spec != null && !taken.Contains(b) && Flight.OrbitOf(b) == null);
        if (fresh == null) { GD.Print("  (no world left out of the trip to call a warp off on)"); yield break; }
        List<ulong> before = _game.GetChildren().OfType<SurfaceZone>().Select(z => z.GetInstanceId()).ToList();
        int cells = _ship.Storage.Count("warp-cell");
        Flight.Target = JumpTarget.To(fresh);
        foreach (int x in _drive.Tap(Key.J)) yield return x;
        Jump j = Flight.Jumping;
        for (int i = 0; i < 120 && j?.Now == Jump.Stage.Spool; i++) yield return 0; // its world starts being made
        List<WeakReference> made = MadeSince(before, j);
        foreach (int x in _drive.Tap(Key.J)) yield return x;
        Check(j?.Now == Jump.Stage.Cancelled && _ship.Storage.Count("warp-cell") == cells,
            $"J again while it spools calls the warp to {Charts.Tidy(fresh.Name)} off, nothing burnt ({j?.Now}, {j?.Trouble}, {_ship.Storage.Count("warp-cell")} cells of {cells})");
        int waited = 0;
        for (; waited < 60 * 20 && made.Exists(w => w.IsAlive); waited++)
        {
            if (waited % 30 == 29) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            yield return 0;
        }
        Check(made.Count > 0 && !made.Exists(w => w.IsAlive),
            $"the {made.Count} zones made for the warp called off are freed and nothing holds them ({made.Count(w => w.IsAlive)} still reachable after {waited / 60f:F1} s)");
        Flight.Target = null;
    }

    /// <summary>The zones made since (weakly held: an iterator's locals would hold them).</summary>
    List<WeakReference> MadeSince(List<ulong> before, Jump j)
    {
        var made = _game.GetChildren().OfType<SurfaceZone>().Where(z => !before.Contains(z.GetInstanceId())).Select(z => new WeakReference(z)).ToList();
        if (j?.To.Zone is { } orbit && !before.Contains(orbit.GetInstanceId()) && !made.Exists(w => w.Target == orbit)) made.Add(new WeakReference(orbit));
        return made;
    }

    // ---- A leg: warp, then down and back up --------------------------------------------------------------------------

    IEnumerable<int> Leg(Orbiter target, bool land, string what)
    {
        GD.Print($"-- {what}: warp to {Charts.Tidy(target.Name)} --");
        foreach (int x in WarpTo(target)) yield return x;
        if (_ok && Windowed && target == Model.Star)
            foreach (int x in SkyAway("star-high-orbit-facing-away", v => GD.Print($"  (at the star's high orbit the sky facing away from it: mean brightness {v:F3})"))) yield return x;
        if (_ok && Windowed && target == Model.Home && _freshSky >= 0)
            foreach (int x in SkyAway("after-the-star-home-facing-away", v => Check(v <= Math.Max(_freshSky * 1.5, _freshSky + 0.05),
                $"no glare follows from the star: home's sky facing away from the sun is back at its fresh level ({v:F3} mean brightness, {_freshSky:F3} before the star)"))) yield return x;
        if (!_ok || !land || _args.ContainsKey("noland")) yield break; // --noland=1: warps alone (shots of the trip)
        foreach (int x in Land(target)) yield return x;
        if (!_ok) yield break;
        if (target != Model.Home) foreach (int x in TakeOff(Charts.Tidy(target.Name))) yield return x;
    }

    /// <summary>
    /// The owner's flow (solar-systems.md section 8 item 16): open the map, click the body on the system map (the one target,
    /// its route and cost on the panel, the button showing J), press J with the map still open, close the map.
    /// </summary>
    IEnumerable<int> PickAndJump(Orbiter target)
    {
        string key = Charts.KeyOf(Model, target), name = Charts.Tidy(target.Name);
        foreach (int x in _drive.Tap(Key.Tab)) yield return x;
        _game.Menu.OpenMap(Slice.Ui.MapLevel.System);
        var levels = _game.Menu.Root.Map.Levels;
        for (int i = 0; i < 60 && !levels.SystemPlaces.ContainsKey(key); i++) yield return 0;
        if (levels.SystemPlaces.TryGetValue(key, out Vector2 at)) foreach (int x in _drive.ClickAt(at)) yield return x;
        for (int i = 0; i < 20; i++) yield return 0;
        Check(Flight.Target?.Body == target, $"clicking {name} on the system map makes it the one jump target ({(Flight.Target?.Body is { } b ? Charts.Tidy(b.Name) : Flight.Target?.Star >= 0 ? $"star {Flight.Target.Star}" : "none")}; its place on the map {(levels.SystemPlaces.ContainsKey(key) ? at.ToString() : "not drawn")})");
        if (_shots != null && _shot < 40) Shot($"map-target-{name}");
        foreach (int x in _drive.Tap(Key.J)) yield return x;
        foreach (int x in _drive.Tap(Key.Tab)) yield return x;
        for (int i = 0; i < 4; i++) yield return 0;
    }

    IEnumerable<int> WarpTo(Orbiter target)
    {
        // The seat's proof wants a tunnel of a few seconds: the first warp that long, else the loop's last (home).
        bool watch = Windowed && !_watched && !Flight.Route(target).Short && (Flight.Route(target).TripSeconds >= 4.5 || target == Model.Home);
        if (watch) foreach (int x in TurnAway(target)) yield return x;
        WarpRoute route = Flight.Route(target);
        // A short-range warp from 30 m/s: away from what is near, nose first, held there till it spools.
        if (route.Short) _game.Controls.Hold(Key.W);
        for (int i = 0; i < 60 * 20 && route.Short && _ship.Body.Velocity.Length() < Flight.Warping.ShortRangeSpeed + 2f; i++) yield return 0;
        route = Flight.Route(target);
        int before = _ship.Storage.Count("warp-cell");
        int riders = _ship.Zone.Physics.Bodies.Count(b => b.Frame == _ship.Body);
        int jumps = Flight.Jumps.Count;
        foreach (int x in PickAndJump(target)) yield return x;
        _game.Controls.ReleaseAll();
        string no = Flight.Jumps.Count > jumps && Flight.Jumping.To.Name == route.Name ? null : $"J didn't spool for it: {Flight.Route(target).Refusal ?? "no jump"}";
        Check(no == null, $"J spools the drive for {route.Name}: {(route.Short ? "short-range, " : "")}{route.Au:F3} AU, {route.Cells} cells, {route.SpoolSeconds:F0} s spool and {route.TripSeconds:F1} s trip ({no ?? "spooling"}; {Fuelled()})");
        if (no != null) { _ok = false; yield break; }
        Jump j = Flight.Jumping;
        float awayDeg = watch ? Mathf.RadToDeg(Forward(_ship).AngleTo(Toward(target))) : 0f;
        var probe = new FrameProbe(this);
        var clock = Stopwatch.StartNew();
        double spooled = -1, shotSpool = -1;
        bool tunnelShot = false;
        IEnumerator<int> seat = null;
        var views = new List<SeatView>();
        Jump.Stage stage = j.Now;
        SplitStart();
        // The frames that wake the zone gone to and park the zone left are timed apart: proto/orbitclean's piecewise Park/Run replaces them.
        int switched = Flight.ZonesSwitched;
        bool apart = false;
        for (int i = 0; i < 60 * 240 && j.Running; i++)
        {
            yield return 0;
            bool kept = _keep != null; // the frame keeping for the seat's proof slows its frames: not counted
            if (apart) { apart = false; _parking.Add(probe.LastCpuMs); }
            else if (!kept) Frame(probe, $"warp to {route.Name}, {j.Now}");
            if (Flight.ZonesSwitched != switched) (switched, apart) = (Flight.ZonesSwitched, true);
            if (j.Now != stage) { SplitReport($"{route.Name}, {stage}"); stage = j.Now; }
            if (j.Now != Jump.Stage.Spool && spooled < 0) spooled = clock.Elapsed.TotalSeconds;
            if (_shots != null && j.Now == Jump.Stage.Spool && j.Spooled > 0.6 && shotSpool < 0) { shotSpool = 1; Shot($"spool-{Charts.Tidy(target.Name)}"); }
            if (watch && seat == null && j.Now == Jump.Stage.Tunnel && j.InTunnel > 0.8) seat = SeatStreaks(j, views).GetEnumerator();
            if (seat != null && !seat.MoveNext()) { seat = null; watch = false; }
            if (_shots != null && j.Now == Jump.Stage.Tunnel && j.InTunnel > 1.2 && !tunnelShot && seat == null) { tunnelShot = true; Shot($"tunnel-{Charts.Tidy(target.Name)}"); }
        }
        seat?.Dispose(); // a tunnel that ended mid-watch: the seat looks ahead again
        views.RemoveAll(v => v.Frames.Count < 20);
        _watched = views.Count == 2; // else the next long warp watches again
        SplitReport($"{route.Name}, {stage}");
        for (int i = 0; i < 90; i++)
        {
            yield return 0;
            if (apart) { apart = false; _parking.Add(probe.LastCpuMs); }
            else Frame(probe, $"arrived at {route.Name}");
            if (Flight.ZonesSwitched != switched) (switched, apart) = (Flight.ZonesSwitched, true);
        }
        probe.Remove();
        SplitReport($"{route.Name}, after arrival");
        foreach (SeatView v in views) Judge(v, route, awayDeg);
        if (j.Now != Jump.Stage.Done) { Fail($"the warp to {route.Name} ended {j.Now}: {j.Trouble}"); _ok = false; yield break; }
        GD.Print($"  (spool {spooled:F1} s, tunnel {j.InTunnel:F1} s (trip {j.TripSeconds:F1}); setting the destination up took {j.LongestStepMs:F1} ms of a tick at most, the arrival {Flight.WorstArrivalMs:F1})");
        Arrived(j, route, before, riders);
        if (_shots != null) Shot($"arrived-{Charts.Tidy(target.Name)}");
    }

    void Frame(FrameProbe probe, string where)
    {
        _cpu.Add(probe.LastCpuMs);
        _wall.Add(probe.LastMs);
        string stage = $"loop {_loop}, " + (where.Contains(", ") ? where[(where.LastIndexOf(", ") + 2)..] : "after arrival");
        if (!_byStage.TryGetValue(stage, out List<double> ms)) _byStage[stage] = ms = new List<double>();
        ms.Add(probe.LastCpuMs);
        if (probe.LastCpuMs > _worstCpu) (_worstCpu, _worstAt) = (probe.LastCpuMs, where);
    }

    /// <summary>At rest in the target's highest orbit, clear, facing it, fuel spent as routed, everyone aboard, nothing of the zone left.</summary>
    void Arrived(Jump j, WarpRoute route, int before, int riders)
    {
        Orbiter target = route.Body;
        var o = _game.Zone as OrbitZone;
        Check(o != null && Flight.OrbitOf(target) == o || o?.Home.Body == target, $"it arrived in the orbit {Charts.Tidy(target.Name)} is in ({_game.Zone.Name})");
        if (o == null) { _ok = false; return; }
        Celestial c = Spaceflight.CelestialOf(o, target);
        N.Vector3 p = _ship.Body.Position;
        N.Vector3 centre = c.Centre.ToNumerics();
        float edge = Flight.HighOrbit(o, c), off = N.Vector3.Distance(p, centre);
        Check(MathF.Abs(off - edge) < 25f, $"at its highest orbit's edge: {off:F0} m from its centre, the edge {edge:F0} m ({(o.HasGround || !c.IsHome ? $"{(off - c.GlobeRadius) / 1000:F1} km over its ground, inside where it takes over within {c.GlobeRadius + c.Ceiling + o.Rules.Crossing.Band + o.Rules.Space.TakesOverWithin:F0} m" : $"{off / (float)target.Radius:F2} radii out, {2 * Mathf.RadToDeg(MathF.Asin(MathF.Min(1f, (float)target.Radius / off))):F0}° across")})");
        if (!o.HasGround && c.IsHome && target.Radius > 0)
        {
            float across = 2f * Mathf.RadToDeg(MathF.Asin(MathF.Min(1f, (float)target.Radius / off)));
            Check(across is >= 40f and <= 50f && off > target.Radius * (target.Rings ? SpaceSky.RingOuter : 1f), $"{Charts.Tidy(target.Name)} fills the view on arrival: {across:F0}° across, {off / (float)target.Radius:F2} radii out{(target.Rings ? ", past its rings" : "")}");
        }
        Check(_ship.Body.Velocity.Length() < 0.5f && j.ArrivedIn == o.Name, $"at rest in its frame ({_ship.Body.Velocity.Length():F2} m/s, in {j.ArrivedIn})");
        float facing = Forward(_ship).Dot(N.Vector3.Normalize(centre - p).ToGodot());
        Check(facing > 0.99f, $"the pilot facing it ({MathF.Acos(Math.Clamp(facing, -1f, 1f)) * 180f / MathF.PI:F1}° off)");
        string inside = null;
        foreach (Celestial b in o.Bodies)
            if (N.Vector3.Distance(p, b.Centre.ToNumerics()) < b.GlobeRadius + (b == c ? 0f : b.Ceiling)) inside = b.Name;
        foreach (var (body, shell) in o.OtherSolids)
            if (N.Vector3.Distance(p, shell.Position.ToNumerics()) < body.Radius + 500) inside = Charts.Tidy(body.Name);
        float wreck = o.Wrecks.Count == 0 ? float.PositiveInfinity : o.Wrecks.Min(w => N.Vector3.Distance(p, w.Body.Position));
        Check(inside == null && wreck >= Flight.Warping.WreckClearance - 1f, $"clear of every body and wreck: {(inside != null ? $"inside {inside}" : "outside all")}, the nearest wreck {wreck:F0} m off");
        int burnt = before - _ship.Storage.Count("warp-cell");
        Check(burnt == route.Cells && j.Burnt == route.Cells, $"{burnt} warp cells burnt, as routed ({route.Cells}{(route.Short ? ", a short-range warp is free" : "")})");
        var s = _game.Survivor;
        int ridersNow = o.Physics.Bodies.Count(b => b.Frame == _ship.Body);
        Check(s.Seated == _ship && s.Zone == o && _ship.Zone == o && ridersNow == riders && Handover.WorldsHolding(_ship.Body, Flight.AllZones().ToArray()) == 1,
            $"the sleeper and everyone aboard came along: seated, {ridersNow} riding the hull (was {riders}), the hull in one zone's world");
        Check(j.Audit.Count == 0, $"the ledger: nothing lost, doubled or misplaced ({string.Join("; ", j.Audit)})");
        Leftovers($"after the warp to {route.Name}");
    }

    /// <summary>Nothing of a zone you are not in shows: its sky, its light, its sound, its globes; gravity and the camera are the zone's own.</summary>
    void Leftovers(string when)
    {
        SurfaceZone here = _game.Zone;
        var problems = new List<string>();
        SurfaceZone ZoneOf(Node n)
        {
            for (Node at = n; at != null; at = at.GetParent())
                if (at is SurfaceZone z) return z;
            return null;
        }
        var skies = new List<string>();
        foreach (Node n in Every(_game))
        {
            SurfaceZone z = ZoneOf(n);
            switch (n)
            {
                case WorldEnvironment w when w.Environment != null && w.GetViewport() is not SubViewport: // an icon's or preview's own little world isn't the sky
                    skies.Add($"{z?.Name}/{w.Name}");
                    if (z != here) problems.Add($"{z?.Name}'s sky ({w.Name}) is still the environment");
                    break;
                case DirectionalLight3D l when l.IsVisibleInTree() && l.LightEnergy > 0.01f && z != null && z != here:
                    problems.Add($"{z.Name}'s light {l.Name} still shines ({l.LightEnergy:F2})");
                    break;
                case AudioStreamPlayer a when a.Playing && a.CanProcess() && z != null && z != here:
                    problems.Add($"{z.Name}'s sound {a.Name} still plays");
                    break;
                case AudioStreamPlayer3D a when a.Playing && a.CanProcess() && z != null && z != here:
                    problems.Add($"{z.Name}'s sound {a.Name} still plays");
                    break;
                case Globe g when g.IsVisibleInTree() && z is OrbitZone oz && (here is OrbitZone ? oz != here : oz.Bodies.All(c => c.Surface != here)):
                    problems.Add($"{oz.Name}'s globe {g.Name} still shows");
                    break;
            }
        }
        if (skies.Count != 1) problems.Add($"{skies.Count} skies set: {string.Join(", ", skies)}");
        foreach (SurfaceZone z in Flight.AllZones())
            if (z != here && (z.ProcessMode != ProcessModeEnum.Disabled)) problems.Add($"{z.Name} still runs");
        float g0 = here is OrbitZone ? 0f : here.Spec.GravityScale * _game.Surface.Physics.Zone.Strength / _game.Surface.Spec.GravityScale;
        if (MathF.Abs(here.Physics.Zone.Strength - g0) > 0.01f) problems.Add($"gravity {here.Physics.Zone.Strength:F2} m/s² where it should be {g0:F2}");
        if (ZoneOf(_game.Camera) is { } cz && cz != here) problems.Add($"the camera is still in {cz.Name}");
        if (here.Camera != _game.Camera) problems.Add("the zone doesn't draw with the game's camera");
        Check(problems.Count == 0, $"nothing of another zone carried over {when}: one sky ({string.Join(", ", skies)}), its light, sound, globes, gravity {here.Physics.Zone.Strength:F2} m/s² and camera ({string.Join("; ", problems)})");
    }

    static IEnumerable<Node> Every(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            yield return n;
            foreach (Node k in n.GetChildren()) stack.Push(k);
        }
    }

    // ---- Down onto a world, and back up -----------------------------------------------------------------------------

    IEnumerable<int> Land(Orbiter target)
    {
        OrbitZone o = _game.Orbit;
        Celestial c = Spaceflight.CelestialOf(o, target);
        string name = Charts.Tidy(target.Name);
        bool before = c.Surface.Making;
        for (int i = 0; i < 60 * 240 && (o.Anchor != c || !c.Ready || !c.Surface.Arrived); i++) yield return 0;
        Check(o.Anchor == c && c.Ready && c.Surface.Arrived, $"{name} is the frame you fly in, its globe painted from its own surface, made from its seed ({c.Surface.Made?.Spec})");
        if (o.Anchor != c) { _ok = false; yield break; }
        GD.Print($"-- down onto {name} --");
        int t = 0;
        foreach (int x in MoonTest.Upright(_pilot, c, _ship))
        {
            if (++t == 60) GD.Print($"  (turning upright, a second in: {Attitude(c)})");
            yield return x;
        }
        for (int tries = 0; tries < 2 && Mathf.RadToDeg(N.Vector3.Transform(N.Vector3.UnitY, _ship.Body.Rotation).ToGodot().AngleTo((_ship.Body.Position.ToGodot() - c.Centre).Normalized())) > 20f; tries++)
        {
            GD.Print($"  (not upright yet: {Attitude(c)}; turning again)");
            foreach (int x in MoonTest.Upright(_pilot, c, _ship)) yield return x;
        }
        GD.Print($"  (upright over {name}: {Attitude(c)}, {c.Height(_ship.Body.Position.ToGodot()):F0} m up)");
        int crossings = Flight.Crossings.Count;
        for (int i = 0; i < 60 * 150 && Flight.Crossings.Count == crossings; i++)
        {
            _game.Controls.Hold(Key.Ctrl, -_ship.Body.Velocity.ToGodot().Dot((_ship.Body.Position.ToGodot() - c.Centre).Normalized()) < 40f);
            yield return 0;
            if (i % 1200 == 1199)
                GD.Print($"  ({i / 60 + 1} s down: {c.Height(_ship.Body.Position.ToGodot()):F0} m over {name}, sinking {-_ship.Body.Velocity.ToGodot().Dot((_ship.Body.Position.ToGodot() - c.Centre).Normalized()):F1} m/s, {Attitude(c)}, in {_game.Zone.Name}, frame {o.Anchor.Name}, band {Flight.Band:F2}, aligned {Flight.Aligned}, streaming {Flight.StreamingAhead}, its surface arrived {c.Surface.Arrived}, ground ready {c.Surface.Streamer.ReadyAround(_ship.Body.Position.ToGodot(), 1, needCollision: false)}, ceiling {c.Surface.Physics.Zone.Ceiling:F0})");
        }
        _game.Controls.ReleaseAll();
        SurfaceZone surface = c.Surface;
        Check(_game.Zone == surface && _ship.Zone == surface && _game.Survivor.Seated == _ship && Flight.Crossings[^1].Audit.Count == 0,
            $"down through its crossing onto {name}'s own surface, seated, nothing lost ({_game.Zone.Name}{(_game.Zone == surface ? "" : $"; {Attitude(c)}")})");
        if (_game.Zone != surface) { _ok = false; yield break; }
        Vector3 at = _ship.Body.Position.ToGodot();
        Check(at.Y > surface.GroundAt(at.X, at.Z).Y + 1f, $"it came in over the ground, not under it ({at.Y - surface.GroundAt(at.X, at.Z).Y:F0} m up)");
        Vector3 pad = OrbitTest.FlatSpot(surface, at, 40f);
        foreach (int x in _pilot.Land(_ship, pad)) yield return x;
        foreach (int x in _drive.Wait(40)) yield return x;
        if (_ship.Controller?.Frozen != true)
        {
            // A bounce off a slope: once more, on the flattest spot near where it stands.
            GD.Print($"  (not settled {_ship.AboveGround:F1} m up: down again on another spot)");
            foreach (int x in _pilot.Land(_ship, OrbitTest.FlatSpot(surface, _ship.Body.Position.ToGodot(), 30f))) yield return x;
            foreach (int x in _drive.Wait(40)) yield return x;
        }
        Check(_ship.Controller?.Frozen == true && !_game.Suit.Dead, $"landed on {name} on its gear, the sleeper alive ({_ship.AboveGround:F1} m up, the suit's air {_game.Suit.OxygenShare:P0})");
        if (target != Model.Home && !before)
            Check(true, $"{name} had no zone of its own before this trip's warp: it was made from its seed and the system's rules");
        float g = surface.Physics.Zone.Strength;
        Check(MathF.Abs(g - 9.81f * (float)surface.Spec.GravityScale / (float)_game.Surface.Spec.GravityScale * _game.Surface.Physics.Zone.Strength / 9.81f) < 0.02f,
            $"its gravity from its size: {g:F1} m/s² on a {surface.Planet.Width / 1000:F0} km world");
        if (surface.Spec.Airless)
        {
            int ore = surface.Planet.Map.Deposits.All().Count(d => d.Kind == Planets.Gen.DepositKind.WarpFuel);
            Check(ore > 0, $"{name} is airless and holds pentaxite: {ore} warp fuel deposits");
        }
        if (_shots != null) Shot($"landed-{name}");
        Leftovers($"landed on {name}");
    }

    /// <summary>The Skiff's fuel as the console has it: generator fuel, the capacitor, warp cells.</summary>
    string Fuelled() => $"generator fuel {_ship.Power.FuelUnits:F0} units, capacitor {_ship.Power.ChargeMJ:F1} of {_ship.Power.CapacityMJ:F0} MJ, supply {_ship.Power.Supply:P0}, {_ship.Storage.Count("warp-cell")} warp cells";

    IEnumerable<int> TakeOff(string from)
    {
        GD.Print($"-- up off {from} ({Fuelled()}) --");
        if (!_ship.Powered || _ship.Power.FuelUnits <= 0 && _ship.Power.ChargeMJ <= 0)
        {
            Fail($"NO CLIMB: off {from} the Skiff has nothing to climb on ({(_ship.Powered ? "powered" : "no power")}; {Fuelled()})");
            _ok = false;
            yield break;
        }
        _game.Controls.Hold(Key.Space);
        int climbed = 0;
        for (; climbed < 60 * 150 && !_game.InOrbit; climbed++) yield return 0;
        if (!_game.InOrbit)
        {
            _game.Controls.ReleaseAll();
            Fail($"NO CLIMB: off {from} the Skiff didn't reach orbit in {climbed / 60} s, {_ship.AboveGround:F0} m up ({(_ship.Powered ? "powered" : "no power")}, lift margin {_ship.Stats?.LiftMargin:F0} kN; {Fuelled()})");
            _ok = false;
            yield break;
        }
        for (int i = 0; i < 60 * 60 && _game.InOrbit && Flight.Band < 1f; i++) yield return 0;
        for (int i = 0; i < 60 * 6; i++) yield return 0;
        _game.Controls.ReleaseAll();
        foreach (int x in _pilot.Stop()) yield return x;
        Vector3 upNow = N.Vector3.Transform(N.Vector3.UnitY, _ship.Body.Rotation).ToGodot();
        Check(_game.InOrbit && Flight.Band >= 1f, $"up off {from} into orbit and over its band ({_game.Zone.Name}; the Skiff at {_ship.Body.Position.ToGodot()}, moving {_ship.Body.Velocity.ToGodot()}, its top {upNow}, power {_ship.Powered}, mode {_ship.Structure.Mode}, seated {_game.Survivor.Seated == _ship})");
        _ok = _game.InOrbit;
    }

    // ---- Levels and frame times --------------------------------------------------------------------------------------

    readonly record struct Level(int Nodes, int Objects, int Resources, int Bodies, int Colliders, int Drawn, double ManagedMb, double StaticMb, int Zones, int Terrain)
    {
        public override string ToString() => $"{Nodes} nodes, {Objects} objects, {Resources} resources, {Bodies} physics bodies, {Colliders} colliders, {Drawn} drawn, managed {ManagedMb:F0} MB, engine {StaticMb:F0} MB, {Zones} zones, {Terrain} streamed ground pieces apart";
    }

    Level Measure()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        int colliders = 0, drawn = 0, terrain = 0, marks = 0;
        foreach (Node n in Every(_game))
        {
            if (SystemSplit.IsMark(n)) { marks++; continue; } // --split's own markers aren't the game's
            // The ground streamed round where you are (or left each world) comes and goes with where that is: counted apart.
            bool ground = n is PlanetSurface.Terrain.TerrainStreamer || n.GetParent() is PlanetSurface.Terrain.TerrainStreamer;
            if (ground) { terrain += n.GetChildCount(); continue; }
            if (n is CollisionObject3D) colliders++;
            if (n is GeometryInstance3D gi && gi.IsVisibleInTree()) drawn++;
        }
        var zones = Flight.AllZones().ToList();
        return new Level((int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) - marks, (int)Performance.GetMonitor(Performance.Monitor.ObjectCount) - marks,
            (int)Performance.GetMonitor(Performance.Monitor.ObjectResourceCount), zones.Sum(z => z.Physics?.Bodies.Count ?? 0), colliders, drawn,
            GC.GetTotalMemory(false) / 1048576.0, OS.GetStaticMemoryUsage() / 1048576.0, zones.Count, terrain);
    }

    bool _timed;

    void FrameTimes()
    {
        _timed = true;
        static (double P95, double Worst) Spread(List<double> ms)
        {
            var sorted = ms.OrderBy(x => x).ToList();
            return sorted.Count == 0 ? (0, 0) : (sorted[(int)(sorted.Count * 0.95)], sorted[^1]);
        }
        var (p95, worst) = Spread(_cpu);
        var (wallP95, wallWorst) = Spread(_wall);
        foreach (var (stage, ms) in _byStage) { var (s95, sWorst) = Spread(ms); GD.Print($"  ({stage}: CPU 95th {s95:F1} ms, worst {sWorst:F1} ms over {ms.Count} frames)"); }
        if (_parking.Count > 0) GD.Print($"  (left out of the budget: the frames waking the zone gone to and parking the zone left, proto/orbitclean's piecewise Park/Run to fix: {string.Join(", ", _parking.Select(ms => ms.ToString("F0")))} ms)");
        GD.Print($"  frame times through every warp, CPU: 95th {p95:F1} ms, worst {worst:F1} ms ({_worstAt}); wall: 95th {wallP95:F1} ms, worst {wallWorst:F1} ms; {_cpu.Count} frames, {(_args.ContainsKey("minspec") ? "min spec stand-in" : "this machine")}, {(_shots != null ? "windowed" : "headless")}");
        Check(p95 < 16.7 && worst < 33.4, $"the warps keep the frame budget in CPU time: 95th percentile {p95:F1} ms (budget 16.7), worst {worst:F1} ms (no frame dropped twice)");
    }

    int _shot;

    void Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        System.IO.Directory.CreateDirectory(_shots);
        string file = System.IO.Path.Combine(_shots, $"{++_shot:00}-{name.Replace(' ', '-').ToLowerInvariant()}.png");
        Error err = GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print(err == Error.Ok ? $"  (shot: {file})" : $"  (shot failed: {err})");
    }
}
