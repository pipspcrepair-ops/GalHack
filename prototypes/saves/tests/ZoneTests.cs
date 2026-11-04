using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GalHack.Persistence;
using GalHack.Records;
using GalHack.Tests;
using Godot;
using Saves.Zones;

namespace Saves.Tests;

/// <summary>
/// Tests of the zone manager with real Godot worlds: the live-zone cap, the linger timer, saving
/// before unloading, the load audit, crossings (and crashes during them), landing where you
/// crossed, background preloading against the tunnel, and hidden zones still simulating.
/// The first groups run in _Ready on simulated time; the timing groups need real frames.
/// </summary>
public partial class ZoneTests : Node
{
    public event Action Finished;

    readonly TestLog _log;
    readonly string _root;
    readonly RecordSchemas _schemas = GameRecords.Schemas();

    public ZoneTests(TestLog log, string root)
    {
        _log = log;
        _root = root;
    }

    public ZoneTests() { }

    public override void _Ready()
    {
        void Group(string name, Action test)
        {
            try { test(); }
            catch (Exception e) { _log.Fail($"{name}: unexpected {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
        }
        Group("cap and linger", CapAndLinger);
        Group("unload and reload", UnloadAndReload);
        Group("crossing", Crossing);
        Group("crossing under crash", CrossingUnderCrash);
        Group("landing", Landing);
        _log.Section("Zones: preload and the tunnel (real frames)");
    }

    // ---- A small world to test with ------------------------------------------------------

    sealed class TestWorld
    {
        public SaveStore Store;
        public RecordTable Table;
        public ZoneManager Zones;
        public Places Places;
        public int Commits;
        public CommitResult LastCommit;
    }

    TestWorld MakeWorld(string dir, IZoneContent content = null, bool fresh = true)
    {
        var w = new TestWorld { Store = new SaveStore(dir, _schemas) };
        if (fresh)
        {
            w.Store.Create();
            w.Table = new RecordTable();
            StarterWorld.Create(w.Table);
            Commit(w, null);
        }
        else w.Table = w.Store.Load().Table;
        w.Places = new Places { Table = w.Table };
        w.Zones = new ZoneManager { Name = "Zones", Content = content ?? w.Places, Table = w.Table };
        w.Zones.CommitNow = (why, faults) =>
        {
            Commit(w, faults);
            return true;
        };
        w.Zones.SetProcess(false); // these tests drive time themselves
        AddChild(w.Zones);
        return w;
    }

    static void Commit(TestWorld w, FaultInjector faults)
    {
        CommitPlan plan = w.Table.PrepareCommit();
        w.LastCommit = w.Store.Commit(plan, faults);
        w.Table.CompleteCommit(plan);
        w.Commits++;
    }

    static void Dispose(TestWorld w)
    {
        if (w != null && IsInstanceValid(w.Zones)) w.Zones.Free();
    }

    string Dir(string name)
    {
        string path = Path.Combine(_root, name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    string Copy(string from, string name)
    {
        string to = Dir(name);
        foreach (string d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
        foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)));
        return to;
    }

    static bool Same(Dictionary<string, SaveRecord> a, Dictionary<string, SaveRecord> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out SaveRecord other) && other == kv.Value);

    /// <summary>A zone with no terrain, which spawns a plain node per record.</summary>
    sealed class PlainContent : IZoneContent
    {
        public object Generate(string zoneId)
        {
            Thread.Sleep(5);
            return null;
        }

        public IEnumerable<Action> Build(Zone zone, object generated) =>
            new Action[] { () => zone.Root.AddChild(new Node3D { Name = "Ground" }) };

        public void Spawn(Zone zone, SaveRecord record)
        {
            var node = new Node3D { Name = record.Id };
            zone.Root.AddChild(node);
            zone.Register(record.Id, node);
        }

        public void Capture(Zone zone) { }
    }

    /// <summary>The real content with a bug planted: it never spawns one type of record.</summary>
    sealed class ForgetfulContent : IZoneContent
    {
        public Places Real;
        public string Forget;
        public object Generate(string zoneId) => Real.Generate(zoneId);
        public IEnumerable<Action> Build(Zone zone, object generated) => Real.Build(zone, generated);
        public void Spawn(Zone zone, SaveRecord record)
        {
            if (record.Type != Forget) Real.Spawn(zone, record);
        }
        public void Capture(Zone zone) => Real.Capture(zone);
    }

    // ---- The live-zone cap and the linger timer -------------------------------------------

    void CapAndLinger()
    {
        _log.Section("Zones: the live-zone cap and the linger timer");
        var w = MakeWorld(Dir("cap"), new PlainContent());
        // A crate in each of six deep-space sites, to prove eviction saves and keeps them.
        for (int i = 0; i < 6; i++)
        {
            w.Table.Put(GameRecords.Make($"crate-site{i}", RecordTypes.Container, $"site/{i}", null, StarterWorld.Owner,
                new ContainerData { Label = "cache", Stacks = { StarterWorld.Stack(ItemKinds.Ore, 3) } }));
        }
        w.Table.Declare(18, 0);
        Commit(w, null);

        int most = 0;
        for (int i = 0; i < 6; i++)
        {
            string id = $"site/{i}";
            w.Zones.SetOccupied(new[] { id });
            Zone zone = w.Zones.LoadNow(id);
            w.Zones.SetActive(zone, new Vector2I(64, 64));
            for (int s = 0; s < 5; s++) w.Zones.Tick(1.0); // the player stays a few seconds
            most = Math.Max(most, w.Zones.Live.Count);
        }
        List<string> live = w.Zones.Live.Select(z => z.ZoneId).OrderBy(z => z, StringComparer.Ordinal).ToList();
        _log.Check(most == 4 && live.SequenceEqual(new[] { "site/2", "site/3", "site/4", "site/5" }),
            $"hopping through 6 zones, never more than {most} were live; the two empty the longest were unloaded to make room (live now: {string.Join(", ", live)})");

        LoadResult disk = new SaveStore(w.Store.Folder, _schemas).Load();
        _log.Check(disk.Ok && disk.Table.Contains("crate-site0") && disk.Table.Contains("crate-site1") && Same(disk.Table.Snapshot(), w.Table.Snapshot()),
            $"the evicted zones saved before they unloaded, and their crates are in the table and on disk ({w.Commits} commits)");

        // Linger: site/4 is empty now (the player is in site/5).
        Zone site4 = w.Zones.Get("site/4");
        for (int s = 0; s < 60; s++) w.Zones.Tick(1.0);
        bool stillThere = w.Zones.Get("site/4") == site4;
        // Hop back into site/4 for a moment: the same live zone, no reload, and its timer starts again.
        w.Zones.SetOccupied(new[] { "site/4" });
        w.Zones.SetActive(site4, new Vector2I(64, 64));
        w.Zones.Tick(1.0);
        bool reused = w.Zones.Get("site/4") == site4 && site4.LingerLeft == w.Zones.LingerSeconds;
        w.Zones.SetOccupied(new[] { "site/5" });
        w.Zones.SetActive(w.Zones.Get("site/5"), new Vector2I(64, 64));
        int before = w.Commits;
        for (int s = 0; s < 119; s++) w.Zones.Tick(1.0);
        bool lingered = w.Zones.Get("site/4") == site4;
        for (int s = 0; s < 2; s++) w.Zones.Tick(1.0);
        bool gone = w.Zones.Get("site/4") == null && w.Commits > before && w.Table.Contains("crate-site4");
        _log.Check(stillThere && reused, "hopping back into an empty zone within its linger window reuses the same live zone: no reload");
        _log.Check(lingered && gone,
            $"an empty zone stays live for {w.Zones.LingerSeconds:F0} s (still there at 119 s), then saves and unloads (gone at 121 s), its records kept");
        Dispose(w);
    }

    // ---- Unload and reload keep every record; the load audit --------------------------------

    void UnloadAndReload()
    {
        _log.Section("Zones: unload, reload and the load audit");
        var w = MakeWorld(Dir("unload"));
        Zone surface = w.Zones.LoadNow(ZoneIds.Surface);
        List<string> expected = w.Zones.ExpectedIn(ZoneIds.Surface).ToList();
        int aboard = w.Table.InZone(ZoneIds.Cabin(StarterWorld.ShipId)).Count();
        _log.Check(surface.AuditProblems.Count == 0 && expected.All(surface.Spawned.ContainsKey),
            $"the surface loads and every one of its {expected.Count} records came back, {aboard} of them aboard the ship");

        Dictionary<string, SaveRecord> before = w.Table.Snapshot();
        w.Zones.SetOccupied(Array.Empty<string>());
        w.Zones.SetActive(null, Vector2I.One);
        bool unloaded = w.Zones.Unload(surface);
        LoadResult disk = new SaveStore(w.Store.Folder, _schemas).Load();
        _log.Check(unloaded && w.Zones.Get(ZoneIds.Surface) == null && disk.Ok && Same(disk.Table.Snapshot(), before) && Same(w.Table.Snapshot(), before),
            "unloading the surface saves first and keeps every record, in memory and on disk");

        Zone again = w.Zones.LoadNow(ZoneIds.Surface);
        _log.Check(again.AuditProblems.Count == 0 && expected.All(again.Spawned.ContainsKey),
            $"loading it again brings all {expected.Count} back");
        Dispose(w);

        // A zone whose spawner misses something says so, and the records stay put.
        var forgetful = new ForgetfulContent { Forget = RecordTypes.Drop };
        var broken = MakeWorld(Dir("audit"), forgetful);
        forgetful.Real = broken.Places;
        Zone zone = broken.Zones.LoadNow(ZoneIds.Surface);
        int drops = broken.Table.All.Count(r => r.Type == RecordTypes.Drop);
        _log.Check(drops > 0 && zone.AuditProblems.Count == drops,
            $"a zone whose records don't all come back names them (\"{zone.AuditProblems.FirstOrDefault()}\") and drops none of them");
        Dispose(broken);

        // A zone whose background load throws is let go with a reason, in the background and in
        // LoadNow alike: nothing may sit at "loading" for ever, and a tunnel waiting on it turns back.
        var failing = MakeWorld(Dir("failing"), new FailingContent());
        int records = failing.Table.Count;
        Zone pending = failing.Zones.Preload(ZoneIds.Orbit);
        for (int i = 0; i < 200 && failing.Zones.Get(ZoneIds.Orbit) != null; i++)
        {
            Thread.Sleep(5);
            failing.Zones.Tick(0.016);
        }
        bool droppedInBackground = pending != null && failing.Zones.Get(ZoneIds.Orbit) == null;
        Zone now = failing.Zones.LoadNow(ZoneIds.Orbit);
        _log.Check(droppedInBackground && now == null && failing.Zones.Live.Count == 0 && failing.Table.Count == records
                   && failing.Zones.Trouble?.Contains("failed to load") == true,
            $"a zone whose load throws is dropped with a reason (\"{failing.Zones.Trouble}\"), never left loading, and no record is touched");
        Dispose(failing);
    }

    /// <summary>Content whose generation always throws, like a corrupt terrain file would.</summary>
    sealed class FailingContent : IZoneContent
    {
        public object Generate(string zoneId) => throw new System.IO.InvalidDataException("terrain data is damaged");
        public IEnumerable<Action> Build(Zone zone, object generated) => Array.Empty<Action>();
        public void Spawn(Zone zone, SaveRecord record) { }
        public void Capture(Zone zone) { }
    }

    // ---- Crossing ------------------------------------------------------------------------------

    void Crossing()
    {
        _log.Section("Zones: crossing");
        var w = MakeWorld(Dir("crossing"));
        Zone surface = w.Zones.LoadNow(ZoneIds.Surface);
        Zone orbit = w.Zones.LoadNow(ZoneIds.Orbit);
        w.Zones.SetActive(surface, new Vector2I(64, 64));
        _log.Check(surface.FindWorld3D() != orbit.FindWorld3D() && surface.RenderTargetUpdateMode == SubViewport.UpdateMode.Always
                   && orbit.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled,
            "each live zone is its own World3D, and only the one the host stands in is drawn");

        var ship = (Ship)surface.Spawned[StarterWorld.ShipId];
        SubViewport cabin = ship.CabinWorld;
        World3D cabinWorld = cabin.FindWorld3D();
        var passenger = (Node3D)surface.Spawned[StarterWorld.PlayerId];
        Vector3 passengerAt = passenger.Position;
        Node3D crate = ((RecordView)surface.Spawned[StarterWorld.CrateC]).Body;
        Vector3 crateAt = crate.Position;
        SaveRecord crateRecord = w.Table.Get(StarterWorld.CrateC), passengerRecord = w.Table.Get(StarterWorld.PlayerId);

        Vector3 arrival = Places.OrbitPointAbove(ship.Position.X, ship.Position.Z, Places.OrbitArrivalAltitude);
        bool crossed = w.Zones.Cross(StarterWorld.ShipId, ship, surface, orbit,
            () => WorldOps.Place(w.Table, StarterWorld.ShipId, ZoneIds.Orbit, null, Places.Round(arrival), rotation: new double[] { 0, 0, 0, 1 }),
            node => node.Position = arrival);

        LoadResult disk = new SaveStore(w.Store.Folder, _schemas).Load();
        _log.Check(crossed && ship.GetParent() == orbit.Root && !surface.Spawned.ContainsKey(StarterWorld.ShipId) && orbit.Spawned[StarterWorld.ShipId] == ship
                   && disk.Ok && disk.Table.Get(StarterWorld.ShipId).ZoneId == ZoneIds.Orbit,
            "the ship crossed to orbit: its record says orbit on disk, and its node left the surface's world for orbit's");
        _log.Check(w.LastCommit.Written == 1 && disk.Table.Get(StarterWorld.CrateC) == crateRecord && disk.Table.Get(StarterWorld.PlayerId) == passengerRecord,
            "the crossing wrote exactly one record: the passenger and the cargo aboard were not rewritten at all");
        _log.Check(ship.CabinWorld == cabin && cabin.FindWorld3D() == cabinWorld && cabinWorld != orbit.FindWorld3D() && passenger.GetParent() == ship && passenger.Position == passengerAt
                   && crate.Position == crateAt && orbit.Spawned.ContainsKey(StarterWorld.PlayerId) && orbit.Spawned.ContainsKey(StarterWorld.CrateC),
            "passengers and cargo rode through untouched: the same cabin world, the same nodes, the same positions");
        Dispose(w);
    }

    void CrossingUnderCrash()
    {
        _log.Section("Zones: crossing under crash");
        string pristine = Dir("cross-crash/pristine");
        var setup = MakeWorld(pristine);
        AuditCounts totals = AuditCounts.Of(setup.Table.All);
        Dispose(setup);

        // Trace the crossing's steps once.
        var trace = new List<string>();
        RunCrossing(Copy(pristine, "cross-crash/traced"), new FaultInjector { Trace = trace });
        int commitPoint = trace.IndexOf("manifest.json replaced: committed");

        int wrong = 0, stayed = 0, moved = 0;
        for (int i = 0; i < trace.Count; i++)
        {
            string dir = Copy(pristine, $"cross-crash/point-{i:D2}");
            bool crashed = false;
            try { RunCrossing(dir, new FaultInjector { CrashAt = i }); }
            catch (SimulatedCrash) { crashed = true; }

            // The restart: whatever is on disk.
            LoadResult r = new SaveStore(dir, _schemas).Load();
            string expect = i >= commitPoint ? ZoneIds.Orbit : ZoneIds.Surface;
            int places = r.Ok ? new[] { ZoneIds.Surface, ZoneIds.Orbit }.Count(z => r.Table.InZone(z).Any(x => x.Id == StarterWorld.ShipId)) : 0;
            List<string> broken = r.Ok ? Invariants.Check(r.Table.All, totals.Items, totals.Blocks) : r.Problems;
            int aboard = r.Ok ? r.Table.All.Count(x => x.ShipId == StarterWorld.ShipId) : 0;
            if (!crashed || !r.Ok || places != 1 || r.Table.Get(StarterWorld.ShipId).ZoneId != expect || broken.Count > 0 || aboard != 3)
            {
                wrong++;
                _log.Fail($"crossing crashed at point {i} ({trace[i]}): {(r.Ok ? $"in {places} zones; {string.Join("; ", broken)}" : r.Summary())}");
            }
            else if (expect == ZoneIds.Orbit) moved++;
            else stayed++;
        }
        _log.Check(wrong == 0 && trace.Count > 0,
            $"a crossing crashed at each of its {trace.Count} steps (the commit's, then leaving the old zone, then arriving in the new one): " +
            $"after a restart the ship was in exactly one zone every time ({stayed} times still on the surface, {moved} in orbit), with all 3 things aboard and every item");
    }

    void RunCrossing(string dir, FaultInjector faults)
    {
        var w = MakeWorld(dir, fresh: false);
        Ship ship = null;
        try
        {
            Zone surface = w.Zones.LoadNow(ZoneIds.Surface);
            Zone orbit = w.Zones.LoadNow(ZoneIds.Orbit);
            ship = (Ship)surface.Spawned[StarterWorld.ShipId];
            Vector3 arrival = Places.OrbitPointAbove(0, 0, Places.OrbitArrivalAltitude);
            w.Zones.Cross(StarterWorld.ShipId, ship, surface, orbit,
                () => WorldOps.Place(w.Table, StarterWorld.ShipId, ZoneIds.Orbit, null, Places.Round(arrival), rotation: new double[] { 0, 0, 0, 1 }),
                node => node.Position = arrival,
                faults);
        }
        finally
        {
            Dispose(w);
            // A crash between leaving the old zone and arriving in the new one leaves the node in neither.
            if (IsInstanceValid(ship) && !ship.IsInsideTree()) ship.Free();
        }
    }

    // ---- Landing where you crossed --------------------------------------------------------------

    void Landing()
    {
        _log.Section("Zones: landing where you crossed");
        float worst = 0;
        var rng = new Random(9);
        for (int i = 0; i < 200; i++)
        {
            var spot = new Vector2((float)rng.NextDouble() * 6000 - 3000, (float)rng.NextDouble() * 6000 - 3000);
            Vector3 up = Places.OrbitPointAbove(spot.X, spot.Y, Places.OrbitArrivalAltitude);
            Vector3 down = Places.OrbitPointAbove(spot.X, spot.Y, Places.OrbitEdgeAltitude); // crossing back in, lower
            worst = Mathf.Max(worst, Places.SurfacePointBelow(up).DistanceTo(spot));
            worst = Mathf.Max(worst, Places.SurfacePointBelow(down).DistanceTo(spot));
        }
        _log.Check(worst < 0.05f,
            $"climbing out over any of 200 spots and re-entering above it brings you down within {worst * 100:F1} cm of where you crossed");
        bool clear = Places.OrbitArrivalAltitude > Places.OrbitPreloadAltitude && Places.OrbitPreloadAltitude > Places.OrbitEdgeAltitude
                     && Places.SurfaceArrivalAltitude < Places.SurfaceEdgeAltitude && Places.SurfaceArrivalAltitude > Places.SurfacePreloadAltitude;
        _log.Check(clear,
            $"you arrive clear of the edge you just crossed (in orbit {Places.OrbitArrivalAltitude} m up, above a {Places.OrbitEdgeAltitude} m edge; on the surface {Places.SurfaceArrivalAltitude} m up, below a {Places.SurfaceEdgeAltitude} m edge), so you never bounce straight back");
    }

    // ---- Frame-driven: preload against the tunnel, and hidden zones simulating -------------------

    enum Stage { Start, Climbing, Tunnel, NextRun, Physics, Hands, Done, Finished }

    Stage _stage = Stage.Start;
    TestWorld _w;
    int _run;                       // 0: a normal PC, 1: a simulated slow one
    double _now, _tunnelAt, _longestFrame;
    ulong _lastFrameUsec;
    RigidBody3D _faller;
    int _physicsTicks;

    const double ClimbSeconds = 3.0; // from the preload altitude to the edge, at 60 m/s
    const double SlowPc = 6.0;

    // Every frame-driven stage runs inside a try: an exception fails the test and finishes the run,
    // instead of repeating every frame while the suite waits for a stage that will never end.
    public override void _Process(double delta) => Guarded(() => FrameStep(delta));

    public override void _PhysicsProcess(double delta) => Guarded(() => PhysicsStep(delta));

    void Guarded(Action step)
    {
        if (_stage == Stage.Finished) return;
        try { step(); }
        catch (Exception e)
        {
            _log.Fail($"zone tests, stage {_stage}: unexpected {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            _faller = null;
            _stage = Stage.Done;
        }
    }

    void FrameStep(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (_lastFrameUsec != 0 && _stage is Stage.Climbing or Stage.Tunnel)
            _longestFrame = Math.Max(_longestFrame, (now - _lastFrameUsec) / 1000.0);
        _lastFrameUsec = _stage is Stage.Start ? 0 : now;
        _now += delta;

        switch (_stage)
        {
            case Stage.Start:
                _w = MakeWorld(Dir($"preload-{_run}"));
                Zone surface = _w.Zones.LoadNow(ZoneIds.Surface);
                _w.Zones.SimulatedSlowness = _run == 1 ? SlowPc : 0;
                _w.Zones.SetActive(surface, new Vector2I(64, 64));
                _w.Zones.SetOccupied(new[] { ZoneIds.Surface });
                _w.Zones.SetProcess(true); // real frames from here on
                _longestFrame = 0;
                _lastFrameUsec = 0; // this frame did the set-up: only frames from here on count
                _now = 0;
                _w.Zones.Preload(ZoneIds.Orbit); // the ship has just climbed past the preload altitude
                _stage = Stage.Climbing;
                break;

            case Stage.Climbing:
                if (_now >= ClimbSeconds)
                {
                    _tunnelAt = _now; // the atmosphere's edge: the tunnel starts
                    _stage = Stage.Tunnel;
                }
                break;

            case Stage.Tunnel:
                Zone orbit = _w.Zones.Get(ZoneIds.Orbit);
                if (orbit == null || _now - _tunnelAt > 60)
                {
                    _log.Fail($"orbit never became ready ({_w.Zones.Trouble ?? "no reason given"})");
                    _stage = Stage.NextRun;
                    break;
                }
                if (_now - _tunnelAt < Tunnel.MinimumSeconds || !orbit.IsReady) break;
                double tunnel = _now - _tunnelAt;
                double readyAfter = orbit.ReadyAt - orbit.RequestedAt;
                string pc = _run == 0 ? "normal PC" : $"slow PC (+{SlowPc:F0} s per load)";
                _log.Info($"{pc}: orbit generated on a worker thread in {orbit.GenerateSeconds:F2} s, built in {orbit.BuildFrames} frames " +
                          $"(longest step {orbit.LongestBuildStepMs:F1} ms), ready {readyAfter:F2} s after preloading began; " +
                          $"climb {ClimbSeconds:F1} s + tunnel {tunnel:F2} s; longest frame {_longestFrame:F1} ms");
                if (_run == 0)
                {
                    _log.Check(readyAfter < ClimbSeconds + Tunnel.MinimumSeconds && tunnel < Tunnel.MinimumSeconds + 0.1,
                        $"preloading from the preload altitude, orbit was ready {readyAfter:F2} s in, before the tunnel's minimum {Tunnel.MinimumSeconds} s ran out: the tunnel did not have to wait");
                }
                else
                {
                    _log.Check(tunnel > Tunnel.MinimumSeconds + 0.3 && readyAfter <= ClimbSeconds + tunnel + 0.05,
                        $"on a slow PC the tunnel stretched to {tunnel:F2} s and ended as soon as orbit was ready");
                }
                _log.Check(_longestFrame < 100, $"no frame froze while orbit loaded in the background (longest {_longestFrame:F1} ms)");
                _stage = Stage.NextRun;
                break;

            case Stage.NextRun:
                Dispose(_w);
                _w = null;
                _run++;
                if (_run < 2) _stage = Stage.Start;
                else
                {
                    StartPhysicsProbe();
                    _stage = Stage.Physics;
                }
                break;

            case Stage.Done:
                Dispose(_w);
                _w = null;
                _stage = Stage.Finished;
                Finished?.Invoke();
                break;
        }
    }

    /// <summary>A crate dropped in a live zone that is not drawn still falls: hidden zones keep simulating.</summary>
    void StartPhysicsProbe()
    {
        _w = MakeWorld(Dir("physics"));
        Zone surface = _w.Zones.LoadNow(ZoneIds.Surface);
        Zone orbit = _w.Zones.LoadNow(ZoneIds.Orbit);
        _w.Zones.SetActive(surface, new Vector2I(64, 64));
        var floor = new StaticBody3D { Position = new Vector3(0, 2000, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(10, 1, 10) } });
        orbit.Root.AddChild(floor);
        _faller = new RigidBody3D { Position = new Vector3(0, 2005, 0) };
        _faller.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One } });
        orbit.Root.AddChild(_faller);
        _physicsTicks = 0;
    }

    void PhysicsStep(double delta)
    {
        if (_stage == Stage.Hands)
        {
            HandsStep(++_handsTick);
            return;
        }
        if (_stage != Stage.Physics || _faller == null) return;
        if (++_physicsTicks < 120) return;
        float y = _faller.Position.Y;
        _log.Check(Mathf.Abs(y - 2001f) < 0.1f,
            $"a crate in orbit, which is live but not drawn, fell and landed on a floor there (y {y:F2}): hidden zones keep simulating");
        _faller = null;
        StartHands();
        _stage = Stage.Hands;
    }

    // ---- Frame-driven: the player's hands, through the real look ray -----------------------------

    Player _player;
    Hands _hands;
    Zone _handsZone;
    int _handsTick;
    AuditCounts _handsStart;
    StructureData _baseBefore;
    string _said = "";

    void StartHands()
    {
        _log.Section("Zones: the player's hands (real look ray, real physics)");
        Dispose(_w);
        _w = MakeWorld(Dir("hands"));
        _player = new Player { Name = "Player", RecordId = StarterWorld.PlayerId };
        _w.Places.LocalPlayer = _player;
        _handsZone = _w.Zones.LoadNow(ZoneIds.Surface);
        _w.Zones.SetActive(_handsZone, new Vector2I(64, 64));
        _hands = new Hands(() => _w.Table, () => _handsZone, _w.Places, _player);
        _hands.Said += text => _said = text;
        _handsStart = AuditCounts.Of(_w.Table.All);
        _baseBefore = GameRecords.Read<StructureData>(_w.Table.Get(StarterWorld.BaseId));
        _handsTick = 0;
        StandAt(new Vector3(-2.5f, 0.05f, 16.5f), -0.45f); // off the ship, in front of the loose ore
    }

    /// <summary>Puts the player on the ground facing +Z (towards the base), looking down by <paramref name="pitch"/>.</summary>
    void StandAt(Vector3 at, float pitch)
    {
        _player.SpawnOutside(at, Mathf.Pi);
        _player.DebugSetLook(Mathf.Pi, pitch);
    }

    int Stack(string item) => Stacks.CountOf(WorldOps.StacksOf(_w.Table.Get(StarterWorld.PlayerId)), item);

    void HandsStep(int tick)
    {
        RecordTable t = _w.Table;
        switch (tick)
        {
            case 10:
                int ore = Stack(ItemKinds.Ore);
                _hands.Handle(Key.E);
                _log.Check(!t.Contains(StarterWorld.DropOre) && Stack(ItemKinds.Ore) == ore + 5 && !_handsZone.Spawned.ContainsKey(StarterWorld.DropOre),
                    $"E on a loose stack picks it up: the drop's record is gone and its 5 ore are in the pack (\"{_said}\")");
                StandAt(new Vector3(4.5f, 0.05f, 15.6f), -0.4f); // facing the supply crate
                break;
            case 20:
                _hands.Handle(Key.E);
                _log.Check(_hands.Carrying == StarterWorld.CrateB && t.Get(StarterWorld.CrateB).ZoneId == ZoneIds.Surface,
                    $"E on a crate picks it up to carry: its record follows the player, still in exactly one zone (\"{_said}\")");
                StandAt(new Vector3(8f, 0.05f, 15.6f), -0.6f);
                break;
            case 30:
                _hands.Handle(Key.E);
                double[] at = GameRecords.Read<ContainerData>(t.Get(StarterWorld.CrateB)).Position;
                _log.Check(_hands.Carrying == null && Math.Abs(at[0] - 8) < 0.5 && at[2] > 16 && _handsZone.Spawned.ContainsKey(StarterWorld.CrateB),
                    $"E again sets it down where the player looks ({at[0]:F1}, {at[1]:F1}, {at[2]:F1}), and it is drawn there");
                int plates = Stack(ItemKinds.Plate), drops = t.All.Count(r => r.Type == RecordTypes.Drop);
                _hands.Handle(Key.Z);
                _log.Check(Stack(ItemKinds.Plate) == plates - 5 && t.All.Count(r => r.Type == RecordTypes.Drop) == drops + 1
                           && AuditCounts.Of(t.All).Items == _handsStart.Items,
                    $"Z drops 5 plates as a new loose stack, and the item total doesn't change (\"{_said}\")");
                StandAt(new Vector3(-2.5f, 0.05f, 21.5f), 0f); // facing the base's wall
                break;
            case 40:
                int platesBefore = Stack(ItemKinds.Plate);
                _hands.Handle(Key.X);
                var after = GameRecords.Read<StructureData>(t.Get(StarterWorld.BaseId));
                _log.Check(after.Blocks.Count == _baseBefore.Blocks.Count - 1 && Stack(ItemKinds.Plate) == platesBefore + 1,
                    $"X takes the block under the crosshair off the base, and it goes back into the pack as a plate (\"{_said}\")");
                break;
            case 50:
                _hands.Handle(Key.B);
                var rebuilt = GameRecords.Read<StructureData>(t.Get(StarterWorld.BaseId));
                bool sameCells = rebuilt.Blocks.Select(b => (b[0], b[1], b[2])).ToHashSet().SetEquals(_baseBefore.Blocks.Select(b => (b[0], b[1], b[2])));
                _log.Check(rebuilt.Blocks.Count == _baseBefore.Blocks.Count && sameCells,
                    $"B builds a block into the gap from one plate: the base has all its cells again (\"{_said}\")");
                _hands.Handle(Key.M);
                var switched = GameRecords.Read<StructureData>(t.Get(StarterWorld.BaseId));
                _log.Check(switched.Mode == CoreModes.CapitalShip && switched.Blocks.Count == rebuilt.Blocks.Count,
                    $"M switches the base's core to capital-ship mode, every block kept (\"{_said}\")");
                break;
            case 60:
                _w.Places.Capture(_handsZone);
                Commit(_w, null);
                LoadResult disk = new SaveStore(_w.Store.Folder, _schemas).Load();
                AuditCounts found = disk.Ok ? disk.Found : null;
                _log.Check(disk.Ok && found.Items == _handsStart.Items && found.Blocks == _handsStart.Blocks
                           && Invariants.Check(disk.Table.All, _handsStart.Items, _handsStart.Blocks).Count == 0
                           && disk.Table.Get(StarterWorld.PlayerId).ZoneId == ZoneIds.Surface,
                    $"saved and reloaded: {found?.Items} items and {found?.Blocks} blocks, as before; the player's record moved off the ship with them");
                _stage = Stage.Done;
                break;
        }
    }
}
