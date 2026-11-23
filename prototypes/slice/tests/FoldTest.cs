using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Stars;
using GalHack.Structures;
using Godot;
using PlanetSurface.Skies;
using Slice.Game;
using Slice.Items;
using Slice.Orbit;
using Slice.Surface;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The fold drive, through the real game on a fresh world and a fresh stretch of galaxy every run: a Flight structure
/// with a fold drive and fold crystal climbs to orbit, then folds home → A → B → home (A and B picked at random among
/// the stars in reach), and on to A again. Each arrival: the right star, its system the catalogue star's own (built
/// again from its seed, the same), at the star's high orbit facing it, the worlds drawn where the model has them, the
/// star left behind a point at its true bearing, fold crystal spent one a light year, the sleeper and the ship's hold
/// come along, nothing of the old system left (light, sky, gravity, weather, sound, the controls), the zone left freed
/// and the counts (nodes, objects, physics bodies, memory, textures) level. Back home, all that was left there is as it
/// was; back at A, what was left at A is put back.
///
/// The one shortcut is the setup, printed: the ship is set down whole, crystal in its hold, in Creative's free power.
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/FoldTest.tscn   (-- --minspec=1: 4 cores, lowest settings)
/// </summary>
public partial class FoldTest : Node
{
    const int WatchdogMinutes = 22;
    Game.Game _game;
    Driver _drive;
    Pilot _pilot;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();
    Vessel _ship;
    bool _minSpec;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            System.Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        _minSpec = OS.GetCmdlineUserArgs().Contains("--minspec=1");
        if (_minSpec)
        {
            // The minimum spec's stand-in: 4 cores held (read back below), workers sized for 4.
            Process.GetCurrentProcess().ProcessorAffinity = 0x55;
            PlanetSurface.Terrain.TerrainStreamer.Cores = 4;
        }
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        // Held: something in the run resets the process's cores (read back 0xffff once).
        if (_minSpec && (long)Process.GetCurrentProcess().ProcessorAffinity != 0x55) Process.GetCurrentProcess().ProcessorAffinity = 0x55;
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print($"frames through each fold{(_minSpec ? " (min-spec stand-in)" : "")}:\n  {string.Join("\n  ", _foldFrames)}");
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        SetProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    // ---- Frame times through a fold --------------------------------------------------------------

    /// <summary>Every frame's main thread in its stages, and any over budget written down whole: by stage, system, pipelines compiled and stalls.</summary>
    FrameSplit _split;
    SystemSplit _systems;

    void StartTiming()
    {
        if (_split == null)
        {
            Rid vp = GetViewport().GetViewportRid();
            RenderingServer.ViewportSetMeasureRenderTime(vp, true);
            StallWatch.StallMs = 20;
            _systems = new SystemSplit(_game);
            _split = new FrameSplit(GetTree().Root, vp) { HitchMs = 20, Systems = _systems, Doing = Stage };
        }
        _systems.Reset();
        _split.Reset();
    }

    /// <summary>The jump's stage, for a frame's note.</summary>
    string Stage()
    {
        Jump j = _game?.Spaceflight?.Jumping;
        return j == null ? "" : j.Running ? $"{j.Now} {(j.Now == Jump.Stage.Spool ? j.Spooled * 100 : j.InTunnel):F1}{(j.Now == Jump.Stage.Spool ? "%" : " s")}" : "after";
    }

    /// <summary>The frames since timing started: worst, 99th, median, how many over budget, and the worst few whole.</summary>
    string FrameReport()
    {
        var frames = _split.Frames.Select(f => f.Total).OrderBy(f => f).ToList();
        if (frames.Count == 0) return "no frames";
        string worst = string.Join("\n      ", _split.Hitches.OrderByDescending(h => double.Parse(h[..h.IndexOf(' ')], System.Globalization.CultureInfo.InvariantCulture)).Take(5));
        return $"{frames.Count} frames, worst {frames[^1]:F1} ms, 99th {frames[(int)(frames.Count * 0.99)]:F1} ms, median {frames[frames.Count / 2]:F1} ms, {frames.Count(f => f > 16.7)} over 16.7 ms, {frames.Count(f => f > 20)} over 20 ms, {frames.Count(f => f > 25)} over 25 ms{(worst.Length > 0 ? ":\n      " + worst : "")}";
    }

    /// <summary>Each fold's frames in a line, printed together at the end.</summary>
    readonly List<string> _foldFrames = new();

    bool Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
        return ok;
    }

    Spaceflight Flight => _game.Spaceflight;
    Galaxy Catalogue => Flight.Stars.Galaxy;

    IEnumerable<int> Scenario()
    {
        // The min-spec stand-in plays at the stand-in's own settings (tests/minspec.cfg: the lowest, 1080p, vsync), as PopRoute's.
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.Fresh(), SettingsPath = _minSpec ? "res://tests/minspec.cfg" : null };
        AddChild(_game);
        _game.Spaceflight.LedgerFolder = System.IO.Path.Combine(OS.GetUserDataDir(), "fold-test-zones");
        _drive = new Driver(_game);
        _pilot = new Pilot(_game, _drive);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        SurfaceZone zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        GD.Print($"world: {_game.World}{(_minSpec ? $"; min-spec stand-in: affinity 0x{(long)Process.GetCurrentProcess().ProcessorAffinity:x}, lowest settings" : "")}");
        if (_minSpec)
        {
            Check((long)Process.GetCurrentProcess().ProcessorAffinity == 0x55, "the stand-in holds 4 cores (affinity read back)");
        }
        _game.Mode = GameMode.Creative; // free power: the generators run on nothing, the drive still burns its crystal
        foreach (int x in SetDownShip()) yield return x;
        if (_ship == null) yield break;
        foreach (int x in ClimbOut()) yield return x;
        if (!_game.InOrbit) yield break;

        // What is left at home: the bunker, the ground's bags and boxes, a bag of our own by the bunker, the wrecks in orbit.
        Flight.Charts.ChosenStar = -1;
        var homeLeft = Left();
        int home = Flight.HomeStar;
        StartTiming(); // its markers in the scene before the counts are first taken
        foreach (int x in Settle()) yield return x;
        Levels baseline = Measure("in the start world's orbit, before any fold");

        float reach = (float)_ship.Power.JumpReach(Spaceflight.FoldDrive);
        List<int> nearHome = Catalogue.Neighbours(home, reach);
        if (!Check(nearHome.Count > 0, $"stars within the drive's reach of home ({reach:F0} LY): {nearHome.Count}")) yield break;
        // A and B at random, so that each leg is one jump: home to A, A to B, B back home.
        var homeSide = new HashSet<int>(nearHome);
        var legs = nearHome.OrderBy(_ => _rng.Next()).Select(s => (a: s, bs: Catalogue.Neighbours(s, reach).Where(o => o != home && homeSide.Contains(o)).ToList())).FirstOrDefault(p => p.bs.Count > 0);
        if (!Check(legs.bs != null, $"two stars in reach of home and of each other ({nearHome.Count} in reach of home)")) yield break;
        int a = legs.a, b = legs.bs[_rng.Next(legs.bs.Count)];
        GD.Print($"route: home {Catalogue.Name(home)} ({home}) → A {Catalogue.Name(a)} ({a}) → B {Catalogue.Name(b)} ({b}) → home → A");
        if (Shooting)
        {
            // The galaxy map with A picked: its route, LY, crystal and reach.
            Flight.Charts.ChosenStar = a;
            _game.Menu.OpenMap(Slice.Ui.MapLevel.Galaxy);
            foreach (int x in _drive.Wait(40)) yield return x;
            Shot("fold-1-map-route");
            _game.Menu.Close();
            foreach (int x in _drive.Wait(10)) yield return x;
        }

        foreach (int x in FoldTo(a)) yield return x;
        if (Flight.StarHere != a) yield break;
        if (Shooting)
        {
            // Nose to the star: its disc fills the middle of the view.
            foreach (int x in Face(N.Vector3.Normalize((_game.Orbit.Home.Centre - _ship.Body.Position.ToGodot()).ToNumerics()), 30)) yield return x;
            Image frame = GetViewport().GetTexture().GetImage();
            Color middle = frame.GetPixel(frame.GetWidth() / 2, frame.GetHeight() / 3);
            Check(middle.Luminance > 0.6f, $"the star fills the view ahead: {middle.Luminance:F2} bright above the console");
            Shot("fold-3-arrival-star");
            // The biggest of its worlds in the sky from here, where it is; then the star come from, picked, at its bearing.
            var biggest = _game.Orbit.Space.Drawn.OrderByDescending(d => d.Angular).First();
            GD.Print($"  (the worlds shot faces {biggest.Body.Name}, {2 * biggest.Angular * 180 / MathF.PI:F2}° across)");
            foreach (int x in Face(biggest.Direction.ToNumerics(), 30)) yield return x;
            Shot("fold-3-arrival-worlds");
            Flight.Charts.ChosenStar = home;
            foreach (int x in Face(Flight.LookToward(home).ToNumerics(), 30)) yield return x;
            Shot("fold-4-home-bearing");
            Flight.Charts.ChosenStar = -1;
        }
        // Something left at A: a crate set adrift beside the ship.
        Vector3 crateAt = _ship.Body.Position.ToGodot() + new Vector3(40f, 6f, 0f);
        _game.Orbit.Loot.Spill(Bill.Of(("iron-ore", 7), ("stone", 3)), crateAt);
        N.Vector3 crateOnStar = _game.Orbit.Home.ToGlobe(crateAt.ToNumerics()); // where it is in the star's own frame: the zone is laid out afresh each visit
        foreach (int x in Settle()) yield return x;
        Levels atA = Measure($"at {Catalogue.Name(a)}");

        foreach (int x in FoldTo(b)) yield return x;
        if (Flight.StarHere != b) yield break;
        foreach (int x in Settle()) yield return x;
        Levels atB = Measure($"at {Catalogue.Name(b)}");
        Level(atA, atB, "A to B");
        var crateRecord = Flight.Ledger.Table.All.FirstOrDefault(r => r.ZoneId.StartsWith(ZoneLedger.StarZoneOf(a) + "/") && r.Type == "container");
        Check(crateRecord != null && crateRecord.Items == 10, $"what was left at A waits in the ledger, in A's zone, with its place ({crateRecord?.Id ?? "no record"} in {crateRecord?.ZoneId})");

        foreach (int x in FoldTo(home)) yield return x;
        if (Flight.StarHere != home) yield break;
        foreach (int x in Settle()) yield return x;
        Levels atHome = Measure($"round home's star, {Catalogue.Name(home)}");
        Level(atB, atHome, "B to home's star");
        bool same = SameAsLeft(homeLeft, out string changed);
        Check(same, $"home waited as it was left: {homeLeft.Count} holders, the same items and blocks in the same zones{changed}");

        // The warp on to the start world: its high orbit, its zones waking as they were.
        GD.Print("-- the warp to the start world --");
        string refused = Flight.Warp(Flight.HomeSystem.Home);
        if (!Check(refused == null, $"from home's star the warp to the start world spools ({refused ?? "ok"})")) yield break;
        for (int i = 0; i < 60 * 120 && Flight.Jumping.Running; i++) yield return 0;
        foreach (int x in Settle()) yield return x;
        Check(_game.Orbit == Flight.FirstOrbit && _ship.Zone == _game.Orbit && _game.Survivor.Seated == _ship, "back in the start world's orbit, at the ship's controls");
        Check(OnlySky(_game.Orbit.Space.World) && _game.Orbit.Space.Sun.Visible, "the orbit's own sky and sun are the ones shown again");
        Check(SameAsLeft(homeLeft, out changed), $"and everything left at home is there{changed}");
        Levels back = Measure("back in the start world's orbit");
        Level(baseline, back, "home, A, B and home again");

        foreach (int x in FoldTo(a)) yield return x;
        if (Flight.StarHere != a) yield break;
        Lootable crate = _game.Orbit.Loot.All.FirstOrDefault(l => l.Storage.Contents["iron-ore"] == 7);
        float off = crate == null ? float.MaxValue : N.Vector3.Distance(_game.Orbit.Home.ToGlobe(crate.Position.ToNumerics()), crateOnStar);
        Check(crate != null && off < 0.5f && crate.Storage.Contents["stone"] == 3,
            $"back at A, the crate left there is put back where it was with what it held ({(crate == null ? "missing" : $"{off:F2} m off")})");
    }
    // ---- Setting up ------------------------------------------------------------------------------

    /// <summary>The Skiff with a fold drive T1 at its tail, set down whole beside the start, its hold holding fold crystal.</summary>
    IEnumerable<int> SetDownShip()
    {
        GameData data = _game.Zone.Data;
        // Six more lift jets along its sides: the drives weigh more than the Skiff was built to lift.
        string text = System.IO.File.ReadAllText(GameData.Own("data/blueprints/starter.gbp"))
            + string.Concat(new[] { -3, -1, 1 }.SelectMany(z => new[] { -3, 3 }.Select(x => $"\nblock {x} -1 {z} thruster-t1 rot=+z+y"))) + "\n";
        Structure s = Blueprint.Load(text, data.Catalogue, out List<string> errors);
        if (!Check(s != null && errors.Count == 0, $"the Skiff's blueprint loads ({string.Join("; ", errors)})")) yield break;
        data.Catalogue.TryGetType("fold-drive-t1", out BlockType drive);
        Block block = Building.Make(s, drive);
        bool fitted = false;
        foreach (int z in Enumerable.Range(3, 7))
        foreach (int y in new[] { 0, -1, 1 })
        foreach (int x in new[] { 0, -1, 1 })
            if (!fitted && Building.Check(s, new Int3(x, y, z), block) == PlaceError.None)
                fitted = Building.Place(s, new Int3(x, y, z), block) == PlaceError.None;
        if (!Check(fitted && Fit(s, "warp-drive"), "a fold drive T1 and a warp drive fit at the Skiff's tail")) yield break;
        Vector3 pad = new Playthrough(_game).FlatSpot(_game.Start, 26f, span: 12f);
        _ship = Vessel.Spawn(_game.Zone, s, pad.ToNumerics() + N.Vector3.UnitY * 2.5f, 0f, landed: true);
        _ship.Storage.Add("fold-crystal", 300);
        _ship.Storage.Add("warp-cell", 60);
        GD.Print($"  (setup: the Skiff with a fold drive, a warp drive and six more lift jets set down whole at {pad}, 300 fold crystal and 60 warp cells in its hold, Creative's free power)");
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>A block of a type fitted at the first free place behind the tail it attaches to.</summary>
    bool Fit(Structure s, string type)
    {
        _game.Zone.Data.Catalogue.TryGetType(type, out BlockType t);
        Block block = Building.Make(s, t);
        foreach (int z in Enumerable.Range(3, 12))
        foreach (int y in new[] { 0, -1, 1, 2 })
        foreach (int x in new[] { 0, -1, 1, -3, 3 })
            if (Building.Check(s, new Int3(x, y, z), block) == PlaceError.None && Building.Place(s, new Int3(x, y, z), block) == PlaceError.None) return true;
        return false;
    }

    IEnumerable<int> ClimbOut()
    {
        GD.Print("-- up to orbit --");
        foreach (int x in _pilot.Board(_ship)) yield return x;
        if (!Check(_game.Survivor.Seated == _ship, "at the ship's controls")) yield break;
        if (!Check(_ship.Stats?.LiftMargin > 0f, $"it can lift off this world: {_ship.Stats?.LiftMargin:F0} kN to spare")) yield break;
        _game.Controls.Hold(Key.Space);
        int ticks = 0;
        while (!_game.InOrbit && ticks++ < 60 * 150) yield return 0;
        _game.Controls.ReleaseAll();
        Check(_game.InOrbit, $"the climb crossed into orbit after {ticks / 60f:F0} s (lift margin {_ship.Stats?.LiftMargin:F0} kN)");
        // Clear of the band, where nothing of the surface is drawn.
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 30 && Flight.Band < 1f; i++) yield return 0;
        _game.Controls.ReleaseAll();
        foreach (int x in _pilot.Stop()) yield return x;
    }

    // ---- A fold ----------------------------------------------------------------------------------

    IEnumerable<int> FoldTo(int star)
    {
        int from = Flight.StarHere;
        SurfaceZone left = _game.Zone;
        bool leftAbroad = Flight.Away != null;
        var leftZones = leftAbroad ? Flight.Away.Zones.ToList() : new List<SurfaceZone>();
        float ly = Flight.LightYearsTo(star);
        int crystal = _ship.Storage.Count("fold-crystal");
        Bill hold = _ship.Storage.Contents.Clone();
        GD.Print($"-- fold: {Catalogue.Name(from)} → {Catalogue.Name(star)}, {ly:F1} LY --");
        _ship.Power.ShieldsOn = false;
        foreach (int x in _drive.Wait(2)) yield return x;
        Flight.Charts.ChosenStar = star;
        string fault = Flight.FoldFault(_ship, star);
        if (!Check(fault == null, $"the fold drive can make it: {fault ?? "IN REACH"} ({Flight.CrystalTo(star)} fold crystal, {_ship.Power.Range(Spaceflight.FoldDrive)?.Reach:F0} LY on the gauge)")) yield break;
        int folds = Flight.Folds.Count();
        StartTiming();
        string refused = Flight.Fold(star);
        if (!Check(refused == null, $"FOLD: the drive spools ({refused ?? $"{_ship.Power.SpoolLeft:F0} s"})")) yield break;
        int ticks = 0;
        bool tunnelShot = false;
        while (Flight.Folds.Count() == folds && Flight.Jumping.Running && ticks++ < 60 * 120)
        {
            if (Shooting && !tunnelShot && Flight.Jumping.Holding && Flight.Jumping.InTunnel > 1.0)
            {
                tunnelShot = true;
                Shot("fold-2-tunnel");
            }
            if (ticks % 20 == 0) _systems.MarkNew(); // the zones a fold makes are split out too, as they come
            yield return 0;
        }
        for (int i = 0; i < 120; i++) yield return 0; // a couple of seconds after, the arrival's settling in
        string frames = FrameReport();
        if (!Check(Flight.Folds.Count() > folds, $"the fold arrived after {ticks / 60f:F1} s ({Flight.Jumping.Now}{(Flight.Jumping.Trouble != null ? ", " + Flight.Jumping.Trouble : "")})")) yield break;
        Jump made = Flight.Folds.Last();
        GD.Print($"  frames through the fold: {frames}\n    {made.InTunnel:F1} s in the tunnel, longest setting-up tick {made.LongestStepMs:F1} ms ({made.LongestStep}), worst arrival tick {Flight.WorstArrivalMs:F1} ms");
        _foldFrames.Add($"{Catalogue.Name(from)} → {Catalogue.Name(star)}: {frames.Split(':')[0]}");
        OrbitZone here = _game.Orbit;
        StarSystem system = Flight.SystemHere;

        // The right star, and its own system.
        Check(Flight.StarHere == star && Flight.Stars.Here == star && here.Home.Body == system.Star && !here.HasGround && Flight.Charts.Visited.Contains(star),
            $"arrived round {Catalogue.Name(star)}: the space round its star, its sky and the charts agree");
        StarSystem catalogue = star == Flight.HomeStar ? Flight.HomeSystem : StarProfile.Of(Catalogue, star);
        Check(system.StarKey == Catalogue.SystemSeed(star) && Same(system, catalogue), $"its system is the catalogue star's own, built again from its seed the same: {Describe(system)}");
        Check(system.Bodies.Count(o => o.Kind is OrbiterKind.Planet or OrbiterKind.GasGiant or OrbiterKind.Moon) >= 8
              && system.Bodies.Any(o => o.Spec is { HighGravity: true } && Planets.Gen.DepositField.FuelKinds(o.Spec).Contains(Planets.Gen.DepositKind.Crystal)),
            "with at least 8 worlds and a high-gravity world holding fold fuel");

        // Where it arrives: the star's high orbit, facing it, its worlds where the model has them.
        Vector3 ship = _ship.Body.Position.ToGodot(), toStar = here.Home.Centre - ship;
        float high = Flight.HighOrbit(here, here.Home);
        float forward = MathX.Rotate(_ship.Body.Rotation, -N.Vector3.UnitZ).ToGodot().Dot(toStar.Normalized());
        Check(Math.Abs(toStar.Length() / high - 1f) < 0.02f && high > system.Star.Radius * 1.2 && forward > 0.99f,
            $"clear of the star at its high orbit, {toStar.Length() / 1000f:F0} km from its centre ({toStar.Length() / system.Star.Radius:F1} of its radii), nose to it ({forward:F3})");
        double worst = 0;
        string worstBody = "";
        double days = here.Days();
        D3 eyeGlobe = D(here.Home.ToGlobe(_game.Camera.GlobalPosition.ToNumerics()));
        foreach (var (body, direction, _) in here.Space.Drawn)
        {
            D3 d = here.Home.Seen.See(body, days, eyeGlobe, here.Home.Orient(days)).Direction;
            double angle = Angle(direction.ToNumerics(), N.Vector3.Transform(new N.Vector3((float)d.X, (float)d.Y, (float)d.Z), here.Home.Turn));
            if (angle > worst) (worst, worstBody) = (angle, body.Name);
        }
        Check(here.Space.Drawn.Any() && worst < 0.5, $"every world is drawn where the model has it this moment: {here.Space.Drawn.Count()} bodies, worst {worst:F3}° ({worstBody})");
        // The star itself: the sky's sun where the star is from the eye, its disc as big as it stands there.
        Vector3 eye = _game.Camera.GlobalPosition, sun = here.Home.Turn.ToBasis() * here.Space.ToSun;
        double sunOff = Angle(sun.ToNumerics(), (here.Home.Centre - eye).Normalized().ToNumerics());
        float radius = (float)((ShaderMaterial)here.Space.Environment.Sky.SkyMaterial).GetShaderParameter("sun_radius"), wantRadius = MathF.Asin((float)system.Star.Radius / here.Home.Centre.DistanceTo(eye));
        Check(sunOff < 0.5 && MathF.Abs(radius / wantRadius - 1f) < 0.01f, $"the sky's sun stands where the star is ({sunOff:F3}° off), its disc {2 * radius * 180 / MathF.PI:F1}° across as the star is from here");

        // The star left behind, a point in this sky at its true bearing: in the catalogue as baked from here, and as the sky's own uniforms draw it.
        N.Vector3 truth = N.Vector3.Normalize(Catalogue.Stars[from].Position - Catalogue.Stars[star].Position);
        double bearing = Angle(Flight.Stars.View.Direction[from], truth);
        var material = (ShaderMaterial)here.Space.Environment.Sky.SkyMaterial;
        Basis turn = (Basis)material.GetShaderParameter("star_turn"), sky = Basis.FromEuler(here.Space.Environment.SkyRotation);
        Vector3 drawn = sky * (turn.Inverse() * new Vector3(truth.X, truth.Y, truth.Z));
        double inWorld = Angle(Flight.LookToward(from).ToNumerics(), drawn.ToNumerics());
        Check(bearing < 0.01 && inWorld < 0.05 && Flight.Stars.View.Shown[from] > 0f,
            $"{Catalogue.Name(from)} is a point in this sky at its true bearing ({bearing:F4}° in the sky baked here, the map's look {inWorld:F3}° off the sky's own, brightness {Flight.Stars.View.Shown[from]:F3})");

        // Fold crystal, one a light year; the sleeper and the hold came too.
        int burnt = crystal - _ship.Storage.Count("fold-crystal");
        Check(burnt == (int)Math.Ceiling(ly - 1e-9) && made.Burnt == burnt, $"fold crystal spent one a light year: {burnt} for {ly:F1} LY");
        hold = hold.Minus(Bill.Of(("fold-crystal", burnt)));
        Check(_game.Survivor.Seated == _ship && _game.Survivor.Zone == here && _ship.Zone == here && Handover.WorldsHolding(_game.Survivor.Body, Zones().ToArray()) == 1
              && _ship.Storage.Contents.All(kv => hold[kv.Key] == kv.Value) && hold.All(kv => _ship.Storage.Count(kv.Key) == kv.Value),
            "the sleeper came along in the seat, in the one zone's world, and everything in the hold with them");
        Check(made.Audit.Count == 0, $"the ledger's audit of the fold is clean{(made.Audit.Count > 0 ? ": " + string.Join("; ", made.Audit) : "")}");

        // Nothing of the old system carries over.
        if (leftAbroad) Check(leftZones.All(z => !IsInstanceValid(z) || !z.IsInsideTree()), $"the system left is freed: its {leftZones.Count} zones out of the scene");
        else Check(left.ProcessMode == ProcessModeEnum.Disabled && !left.Visible && Flight.HomeParked == (star != Flight.HomeStar), "the start system waits parked: out of processing, hidden");
        Check(OnlySky(here.Space.World), "one sky: this star's own, every other environment off");
        var lights = Lit().Where(l => !here.IsAncestorOf(l)).Select(l => l.GetPath().ToString()).ToList();
        Check(lights.Count == 0, $"no light of the old system shines here{(lights.Count > 0 ? ": " + string.Join(", ", lights.Take(4)) : "")}");
        Check(here.Physics.Zone.Strength == 0f && (Vector4)RenderingServer.GlobalShaderParameterGet("weather_wind") == Vector4.Zero, "no gravity and no wind out here");
        Node tunnel = _game.GetNodeOrNull("Jump"); // the fold's own tunnel sound, fading out
        var sounds = Playing().Where(n => !here.IsAncestorOf(n) && !_game.Audio.IsAncestorOf(n) && !_ship.Node.IsAncestorOf(n) && tunnel?.IsAncestorOf(n) != true).Select(n => n.GetPath().ToString()).ToList();
        Check(sounds.Count == 0, $"no sound of the old system plays on{(sounds.Count > 0 ? ": " + string.Join(", ", sounds.Take(4)) : "")}");
        N.Vector3 v0 = _ship.Body.Velocity;
        _game.Controls.Hold(Key.W);
        foreach (int x in _drive.Wait(60)) yield return x;
        _game.Controls.ReleaseAll();
        float pushed = N.Vector3.Dot(_ship.Body.Velocity - v0, N.Vector3.Normalize((here.Home.Centre - _ship.Body.Position.ToGodot()).ToNumerics()));
        Check(pushed > 1f, $"the controls fly the ship here: a second of thrust, {pushed:F1} m/s toward the star");
        foreach (int x in _pilot.Stop()) yield return x;
    }

    static D3 D(N.Vector3 v) => new(v.X, v.Y, v.Z);

    // ---- Shots, when there is a window ---------------------------------------------------------

    static bool Shooting => DisplayServer.GetName() != "headless";

    /// <summary>The last frame drawn, to the worktree's scratchpad (looked at before they are sent).</summary>
    void Shot(string name)
    {
        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "scratchpad", "fold", "shots"));
        System.IO.Directory.CreateDirectory(dir);
        string file = System.IO.Path.Combine(dir, $"{name}{(_minSpec ? "-minspec" : "")}.png");
        Error err = GetViewport().GetTexture().GetImage().SavePng(file);
        if (_split != null) _split.Skip = true; // the frame that read the picture back and wrote it out is the test's, not the game's
        GD.Print(err == Error.Ok ? $"  shot {file}" : $"  could not save {file}: {err}");
    }

    /// <summary>The ship turned and held still, its nose along a direction (for a shot), for a few ticks to settle.</summary>
    IEnumerable<int> Face(N.Vector3 forward, int ticks)
    {
        Vector3 up = MathF.Abs(forward.Y) > 0.95f ? Vector3.Back : Vector3.Up;
        Quaternion q = Basis.LookingAt(forward.ToGodot(), up).GetRotationQuaternion();
        for (int i = 0; i < ticks; i++)
        {
            _ship.Body.Rotation = new N.Quaternion(q.X, q.Y, q.Z, q.W);
            _ship.Body.Velocity = N.Vector3.Zero;
            _ship.Body.AngularVelocity = N.Vector3.Zero;
            _ship.Binding.Teleport();
            yield return 0;
        }
    }
    // ---- What was left -----------------------------------------------------------------------

    /// <summary>Everything held at home but the sleeper and the ship (and what rides it): id, zone, items, blocks.</summary>
    Dictionary<string, (string Zone, int Items, int Blocks)> Left()
    {
        // A bag of our own by the bunker's way out.
        _game.Surface.Loot.Spill(Bill.Of(("stone", 5)), _game.Start + new Vector3(3f, 1f, 3f));
        return Home();
    }

    Dictionary<string, (string Zone, int Items, int Blocks)> Home() => Flight.Holders()
        .Where(h => h.Id != ZoneLedger.Sleeper && h.Ship == null && !Spaceflight.IsStarZone(h.Zone) && !h.Zone.StartsWith("cabin/") && h.Id != IdOfShip())
        .ToDictionary(h => h.Id, h => (h.Zone, h.Items.Total, h.Blocks));

    string IdOfShip() => Flight.Holders().FirstOrDefault(h => h.Type == "structure" && h.Name == _ship.Name && h.Blocks == _ship.Structure.Count).Id;

    bool SameAsLeft(Dictionary<string, (string Zone, int Items, int Blocks)> left, out string changed)
    {
        var now = Home();
        var diff = left.Where(kv => !now.TryGetValue(kv.Key, out var n) || n != kv.Value).Select(kv => $"{kv.Key} {kv.Value} now {(now.TryGetValue(kv.Key, out var n) ? n.ToString() : "gone")}")
            .Concat(now.Keys.Except(left.Keys).Select(k => $"{k} new")).ToList();
        changed = diff.Count == 0 ? "" : ": " + string.Join(", ", diff.Take(5));
        return diff.Count == 0;
    }

    // ---- Levels ------------------------------------------------------------------------------

    sealed record Levels(string Where, int Nodes, int StarNodes, int Objects, int Bodies, long Managed, long Static, long Textures, long Video);

    IEnumerable<int> Settle()
    {
        for (int i = 0; i < 30; i++) yield return 0;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (int i = 0; i < 10; i++) yield return 0;
    }

    Levels Measure(string where)
    {
        // The space round a star (any body with no ground): its own nodes go with its system's bodies, counted apart.
        int star = Zones().OfType<OrbitZone>().Where(o => !o.HasGround).Sum(o => o.FindChildren("*", "", true, false).Count + 1);
        var l = new Levels(where, (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), star, (int)Performance.GetMonitor(Performance.Monitor.ObjectCount),
            Zones().Sum(z => z.Physics?.Bodies.Count ?? 0), GC.GetTotalMemory(false), (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic),
            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed), (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed));
        GD.Print($"  levels {where}: {l.Nodes} nodes ({l.StarNodes} the star's zone), {l.Objects} objects, {l.Bodies} physics bodies, {l.Managed >> 20} MB managed, {l.Static >> 20} MB static, {l.Textures >> 20} MB textures, {l.Video >> 20} MB video");
        return l;
    }

    /// <summary>A swap leaves things level: what isn't the star zone's own counts the same; memory and textures within a little.</summary>
    void Level(Levels was, Levels now, string swap)
    {
        int nodes = (now.Nodes - now.StarNodes) - (was.Nodes - was.StarNodes);
        long managed = now.Managed - was.Managed, statics = now.Static - was.Static, textures = now.Textures - was.Textures;
        Check(Math.Abs(nodes) <= 40 && now.Bodies == was.Bodies && Math.Abs(managed) < 96L << 20 && Math.Abs(statics) < 96L << 20 && Math.Abs(textures) < 24L << 20,
            $"{swap}: level after the swap: {nodes:+0;-0;0} nodes outside the star's zone, bodies {was.Bodies} → {now.Bodies}, managed {managed / 1048576.0:+0.0;-0.0} MB, static {statics / 1048576.0:+0.0;-0.0} MB, textures {textures / 1048576.0:+0.0;-0.0} MB");
    }

    IEnumerable<SurfaceZone> Zones() => _game.GetChildren().OfType<SurfaceZone>().Where(z => IsInstanceValid(z) && z.IsInsideTree());

    /// <summary>The only environment shown is this one.</summary>
    bool OnlySky(WorldEnvironment mine) => _game.FindChildren("*", "WorldEnvironment", true, false).OfType<WorldEnvironment>()
        .Where(w => w.GetViewport() == GetViewport()).All(w => w == mine ? w.Environment != null : w.Environment == null);

    IEnumerable<DirectionalLight3D> Lit() => _game.FindChildren("*", "DirectionalLight3D", true, false).OfType<DirectionalLight3D>()
        .Where(l => l.GetViewport() == GetViewport() && l.IsVisibleInTree() && l.LightEnergy > 0f && l.SkyMode != DirectionalLight3D.SkyModeEnum.SkyOnly);

    IEnumerable<Node> Playing() => _game.FindChildren("*", "", true, false).Where(n => n is AudioStreamPlayer { Playing: true } or AudioStreamPlayer2D { Playing: true } or AudioStreamPlayer3D { Playing: true });

    // ---- The model ---------------------------------------------------------------------------

    /// <summary>Two builds of a system are the same: every body, its kind, orbit, size and world.</summary>
    static bool Same(StarSystem x, StarSystem y)
    {
        if (x.Bodies.Count != y.Bodies.Count || x.Luminosity != y.Luminosity) return false;
        for (int i = 0; i < x.Bodies.Count; i++)
        {
            Orbiter p = x.Bodies[i], q = y.Bodies[i];
            if (p.Kind != q.Kind || p.Orbit != q.Orbit || p.Radius != q.Radius || p.Seed != q.Seed || p.Spec?.Archetype != q.Spec?.Archetype || p.Spec?.WidthKm != q.Spec?.WidthKm) return false;
        }
        return true;
    }

    static string Describe(StarSystem s) =>
        $"L {s.Luminosity:0.###}, {s.Bodies.Count(b => b.Kind == OrbiterKind.Planet)} rocky, {s.Bodies.Count(b => b.Kind == OrbiterKind.GasGiant)} giants, {s.Bodies.Count(b => b.Kind == OrbiterKind.Moon)} moons, {s.Bodies.Count(b => b.Kind == OrbiterKind.Belt)} belts";

    static double Angle(N.Vector3 a, N.Vector3 b) => Math.Atan2(N.Vector3.Cross(a, b).Length(), N.Vector3.Dot(a, b)) * 180.0 / Math.PI;
}
