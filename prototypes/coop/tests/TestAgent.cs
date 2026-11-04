using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Net;
using Godot;
using Dict = Godot.Collections.Dictionary;

namespace Coop;

/// <summary>
/// Lives at /root/Game/TestAgent in every process of a multi-process test. On a client it
/// obeys commands from the host (walk, watch someone, toggle gravity, change zone, leave and
/// rejoin...) and answers with what it measured. On the host (or dedicated server) it directs
/// the scenario and checks the answers against the host's own truth.
///
/// Commands travel over the game's own connection, so they suffer the same simulated latency
/// and loss as everything else (they are reliable RPCs, so they still arrive).
/// </summary>
public partial class TestAgent : Node
{
    public CoopGame Game;
    public string TestName = "";
    public string Scenario = "";   // "listen" or "dedicated" on the director; empty on clients
    public string SimDescription = "";

    /// <summary>Where the "capture" command saves screenshots (windowed runs only).</summary>
    public string CaptureDir = "";

    public int Failures { get; private set; }
    bool IsDirector => Scenario.Length > 0;
    CoopWorld World => Game.World;

    const double Watchdog = 170;

    // Walking a circle here (left turns, centre 1.2 m to the left) stays clear of the crates and furniture.
    static readonly Vector3 WalkStart = new(2.7f, 0.05f, -7.5f);
    const float WalkRadius = 1.2f;
    readonly double _started = NetClock.Now;
    bool _expectLeave, _done;

    // ---- Client side: what is being measured --------------------------------------------------

    bool _walking;
    float _walkYaw, _walkTurn, _walkSpeed;
    readonly List<int> _truthTimes = new();
    readonly List<Vector3> _truthPositions = new();

    int _observePeer, _observeShip, _observeMissing, _observeFrames;
    float _observeMaxStep;
    Vector3? _observeLast;
    readonly List<double> _seenTimes = new();
    readonly List<Vector3> _seenPositions = new();

    (long bytesIn, long bytesOut, long packetsIn, long packetsOut, double at) _meter;
    long _wireIn, _wireOut, _wirePacketsIn, _wirePacketsOut;

    // ---- Director side ---------------------------------------------------------------------------

    readonly Dictionary<string, int> _ready = new();
    readonly Dictionary<int, TaskCompletionSource<Dict>> _waiting = new();
    int _nextRequest;
    readonly Dictionary<int, List<(int ms, Vector3 pos, Vector3[] crates)>> _history = new();

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100; // measure after everything has moved and been drawn
        Game.WorldCreated += () => World.LocalPlayerSpawned += Announce;
        Game.Ended += OnEnded;
        if (IsDirector) Callable.From(() => { _ = Direct(); }).CallDeferred();
    }

    public override void _Process(double delta)
    {
        if (!_done && NetClock.Now - _started > Watchdog)
        {
            Fail($"{TestName}: watchdog, still running after {Watchdog} s");
            Finish();
        }
    }

    void OnEnded(string reason, bool graceful)
    {
        if (_expectLeave)
        {
            _expectLeave = false;
            return;
        }
        if (IsDirector) return; // the director finishes in its own time
        GD.Print($"[{TestName}] ENDED graceful={graceful} ({reason})");
        if (!graceful) Failures++;
        Finish();
    }

    void Finish()
    {
        if (_done) return;
        _done = true;
        GetTree().Quit(Failures == 0 ? 0 : 1);
    }

    // ---- RPC plumbing -----------------------------------------------------------------------------

    void Announce()
    {
        if (!Game.Session.IsServer) RpcId(1, MethodName.ClientReady, TestName);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientReady(string name) => _ready[name] = Multiplayer.GetRemoteSenderId();

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    async void Command(int id, Dict args)
    {
        string op = args["op"].AsString();
        Dict result;
        try
        {
            result = await Execute(id, op, args);
        }
        catch (Exception e)
        {
            result = new Dict { ["error"] = $"{op}: {e.Message}" };
        }
        if (result != null && Game.Session.Connected) RpcId(1, MethodName.Reply, id, result);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Reply(int id, Dict result)
    {
        if (_waiting.Remove(id, out var waiter)) waiter.SetResult(result);
    }

    async Task<Dict> Ask(string name, string op, Dict args = null, double timeout = 15)
    {
        if (!_ready.TryGetValue(name, out int peer)) throw new InvalidOperationException($"{name} isn't connected");
        int id = ++_nextRequest;
        var waiter = new TaskCompletionSource<Dict>();
        _waiting[id] = waiter;
        args ??= new Dict();
        args["op"] = op;
        RpcId(peer, MethodName.Command, id, args);
        await Until(() => waiter.Task.IsCompleted, timeout, $"{name} to answer '{op}'");
        Dict result = waiter.Task.Result;
        if (result.ContainsKey("error")) throw new InvalidOperationException($"{name}: {result["error"]}");
        return result;
    }

    async Task<Dictionary<string, Dict>> AskAll(IEnumerable<string> names, string op, Dict args = null)
    {
        var tasks = names.ToDictionary(n => n, n => Ask(n, op, args?.Duplicate()));
        foreach (var task in tasks.Values) await task;
        return tasks.ToDictionary(t => t.Key, t => t.Value.Result);
    }

    async Task Until(Func<bool> done, double timeout, string what)
    {
        double end = NetClock.Now + timeout;
        while (!done())
        {
            if (NetClock.Now > end) throw new TimeoutException($"timed out waiting for {what}");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    async Task Seconds(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    // ---- Client: commands -------------------------------------------------------------------------

    async Task<Dict> Execute(int id, string op, Dict args)
    {
        Player player = World?.LocalPlayer;
        switch (op)
        {
            case "place":
            {
                Ship ship = World.Ships[args["ship"].AsInt32()];
                player.DebugPlaceAboard(ship, args["pos"].AsVector3(), Vector3.Zero);
                player.DebugSetLook(args.ContainsKey("yaw") ? args["yaw"].AsSingle() : 0f, 0f);
                return Ok();
            }
            case "walk":
                _walkSpeed = args["speed"].AsSingle();
                _walkTurn = _walkSpeed / args["radius"].AsSingle();
                _walkYaw = player.Yaw;
                _truthTimes.Clear();
                _truthPositions.Clear();
                _walking = true;
                return Ok();

            case "walk-stop":
                _walking = false;
                player.ScriptedMove = null;
                return new Dict
                {
                    ["t"] = _truthTimes.ToArray(), ["pos"] = _truthPositions.ToArray(),
                    ["max_step"] = _truthPositions.Zip(_truthPositions.Skip(1), (p, q) => p.DistanceTo(q)).DefaultIfEmpty(0f).Max(),
                };

            case "observe":
                _observePeer = args["peer"].AsInt32();
                _observeShip = args["ship"].AsInt32();
                _observeMissing = _observeFrames = 0;
                _observeMaxStep = 0;
                _observeLast = null;
                _seenTimes.Clear();
                _seenPositions.Clear();
                return Ok();

            case "observe-stop":
                _observePeer = 0;
                return new Dict
                {
                    ["t"] = _seenTimes.ToArray(), ["pos"] = _seenPositions.ToArray(),
                    ["max_step"] = _observeMaxStep, ["missing"] = _observeMissing, ["frames"] = _observeFrames,
                };

            case "meter-start":
                StartMeter();
                return Ok();

            case "meter-stop":
                return StopMeter();

            case "gravity":
                World.RequestGravity(World.Ships[args["ship"].AsInt32()]);
                return Ok();

            case "stunt":
                World.RequestStunt(World.Ships[args["ship"].AsInt32()], args["on"].AsBool());
                return Ok();

            case "sit":
                player.DebugPlaceAboard(World.Ships[args["ship"].AsInt32()], ShipLayout.SeatExitLocal, Vector3.Zero);
                await Seconds(0.2);
                Press(Key.F); // the real path: F asks the host for the seat
                await Seconds(1.0);
                return new Dict { ["mode"] = (int)player.CurrentMode };

            case "stand":
                Press(Key.F);
                await Seconds(0.5);
                return new Dict { ["mode"] = (int)player.CurrentMode };

            case "thrust":
                Hold(Key.W, true);
                await Seconds(args["seconds"].AsDouble());
                Hold(Key.W, false);
                return Ok();

            case "state":
                return State(args["ships"].AsInt32Array());

            case "entities":
                return Entities();

            case "zone":
                World.TransferLocalPlayer(args["zone"].AsInt32());
                return Ok();

            case "place-outside":
                player.DebugPlaceOutside(args["pos"].AsVector3());
                player.DebugSetLook(args["yaw"].AsSingle(), 0f);
                return Ok();

            case "look":
                player.ThirdPerson = args["third"].AsBool();
                player.DebugSetLook(args["yaw"].AsSingle(), args["pitch"].AsSingle());
                return Ok();

            case "capture":
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string path = System.IO.Path.Combine(CaptureDir, args["name"].AsString() + ".png");
                Error saved = GetViewport().GetTexture().GetImage().SavePng(path);
                GD.Print(saved == Error.Ok ? $"[{TestName}] saved {path}" : $"[{TestName}] could not save {path}: {saved}");
                return new Dict { ["ok"] = saved == Error.Ok };
            }

            case "rejoin":
                RpcId(1, MethodName.Reply, id, Ok()); // answer now; we are about to vanish
                _ = Rejoin(args["after"].AsDouble());
                return null;
        }
        throw new ArgumentException($"unknown command {op}");
    }

    async Task Rejoin(double after)
    {
        await Seconds(0.3);
        _expectLeave = true;
        GD.Print($"[{TestName}] leaving on purpose, rejoining in {after:F1} s");
        Game.Leave();
        await Seconds(after);
        _expectLeave = false;
        Error result = Game.Rejoin();
        GD.Print($"[{TestName}] rejoining: {result}");
    }

    static Dict Ok() => new() { ["ok"] = true };

    static void Press(Key key)
    {
        Hold(key, true);
        Hold(key, false);
    }

    static void Hold(Key key, bool pressed) =>
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed });

    Dict State(int[] ships)
    {
        var result = new Dict { ["mode"] = World.LocalPlayer != null ? (int)World.LocalPlayer.CurrentMode : -1 };
        foreach (int id in ships)
        {
            if (!World.Ships.TryGetValue(id, out Ship ship)) continue;
            World.ShipRenderTimes.TryGetValue(id, out var times);
            result[id.ToString()] = new Dict
            {
                ["pos"] = ship.GlobalPosition,
                ["gravity"] = ship.GravityOn,
                ["stunt"] = ship.StuntAutopilot,
                ["pilot"] = ship.PilotPeer,
                ["speed"] = ship.Speed,
                ["t"] = times.motion * 1000.0,
                ["cargo_t"] = times.cargo * 1000.0,
                ["crates"] = Enumerable.Range(0, ship.Crates.Count).Select(ship.CratePosition).ToArray(),
            };
        }
        return result;
    }

    Dict Entities() => new()
    {
        ["zone"] = World.LocalZone,
        ["ships"] = World.NetShips.Select(s => s.Id).ToArray(),
        ["ship_zones"] = World.NetShips.Select(s => s.Zone).ToArray(),
        ["players"] = World.NetPlayers.Select(p => p.PeerId).ToArray(),
        ["player_zones"] = World.NetPlayers.Select(p => p.Zone).ToArray(),
        ["avatars"] = World.Avatars.Keys.ToArray(),
        ["views"] = World.Ships.Keys.ToArray(),
    };

    void StartMeter()
    {
        ConditionedPeer peer = Game.Session.Peer;
        _meter = (peer.BytesIn, peer.BytesOut, peer.PacketsIn, peer.PacketsOut, NetClock.Now);
        Game.Session.Transport.TryPopWireStats(out _, out _, out _, out _);
    }

    Dict StopMeter()
    {
        ConditionedPeer peer = Game.Session.Peer;
        double seconds = NetClock.Now - _meter.at;
        Game.Session.Transport.TryPopWireStats(out _wireOut, out _wireIn, out _wirePacketsOut, out _wirePacketsIn);
        return new Dict
        {
            ["seconds"] = seconds,
            ["bytes_in"] = peer.BytesIn - _meter.bytesIn,
            ["bytes_out"] = peer.BytesOut - _meter.bytesOut,
            ["packets_in"] = peer.PacketsIn - _meter.packetsIn,
            ["packets_out"] = peer.PacketsOut - _meter.packetsOut,
            ["wire_in"] = _wireIn,
            ["wire_out"] = _wireOut,
            ["wire_packets_in"] = _wirePacketsIn,
            ["wire_packets_out"] = _wirePacketsOut,
        };
    }

    // ---- Every tick: measuring ----------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (World == null) return;
        if (_walking && World.LocalPlayer is { } player)
        {
            _walkYaw += _walkTurn * (float)delta;
            player.DebugSetLook(_walkYaw, 0f);
            player.ScriptedMove = new Vector2(0f, -_walkSpeed / 5.5f); // walk speed is 5.5 m/s at full stick
            _truthTimes.Add(NetClock.Ms);
            _truthPositions.Add(player.ShipLocalPosition);
        }
        if (_observePeer != 0) Observe();
        if (IsDirector) Record();
    }

    /// <summary>Where the watched player is drawn, in the ship's own frame, and how far it jumped since last tick.</summary>
    void Observe()
    {
        _observeFrames++;
        if (!World.Avatars.TryGetValue(_observePeer, out AvatarView avatar) || !avatar.Visible
            || !World.Ships.TryGetValue(_observeShip, out Ship ship)
            || !World.AvatarSamples.TryGetValue(_observePeer, out var sample))
        {
            _observeMissing++;
            _observeLast = null;
            return;
        }
        Vector3 local = ship.GlobalTransform.AffineInverse() * avatar.GlobalPosition;
        if (_observeLast is { } last)
        {
            float step = last.DistanceTo(local);
            _observeMaxStep = Mathf.Max(_observeMaxStep, step);
            if (step > 0.5f) GD.Print($"[{TestName}] JUMP {step:F2} m from {last} to {local}: {World.AvatarDebug.GetValueOrDefault(_observePeer)} / ship at {ship.GlobalPosition}");
        }
        _observeLast = local;
        _seenTimes.Add(sample.renderTime * 1000.0);
        _seenPositions.Add(local);
    }

    /// <summary>Director: the host's truth for every ship and its cargo, for the last 15 s.</summary>
    void Record()
    {
        int ms = NetClock.Ms;
        foreach (var (id, ship) in World.Ships)
        {
            if (!_history.TryGetValue(id, out var list)) _history[id] = list = new();
            list.Add((ms, ship.GlobalPosition, Enumerable.Range(0, ship.Crates.Count).Select(ship.CratePosition).ToArray()));
            if (list.Count > 900) list.RemoveAt(0);
        }
    }

    (Vector3 pos, Vector3[] crates) HistoryAt(int ship, double ms)
    {
        var list = _history[ship];
        for (int i = list.Count - 1; i > 0; i--)
        {
            if (list[i - 1].ms > ms) continue;
            var (t0, p0, c0) = list[i - 1];
            var (t1, p1, c1) = list[i];
            float f = t1 > t0 ? (float)Math.Clamp((ms - t0) / (t1 - t0), 0, 1) : 0f;
            return (p0.Lerp(p1, f), c0.Zip(c1, (a, b) => a.Lerp(b, f)).ToArray());
        }
        return (list[0].pos, list[0].crates);
    }

    // ---- Director: scenarios ------------------------------------------------------------------------

    async Task Direct()
    {
        try
        {
            if (Scenario == "dedicated") await DedicatedScenario();
            else if (Scenario == "shot") await ShotScenario();
            else await ListenScenario();
        }
        catch (Exception e)
        {
            Fail($"scenario stopped: {e.Message}");
        }
        GD.Print(Failures == 0 ? $"[{TestName}] SCENARIO PASSED" : $"[{TestName}] SCENARIO FAILED: {Failures} check(s)");
        Game.Leave(); // ends the session for everyone, gracefully
        await Seconds(1.0);
        Finish();
    }

    async Task ListenScenario()
    {
        string[] clients = { "A", "B", "C" };
        await Until(() => clients.All(_ready.ContainsKey), 60, "3 clients to join");
        Pass($"host + 3 clients joined over ENet loopback, clients on {SimDescription}");
        Ship shipA = World.Ships[1];

        // Everyone aboard ship A, then the host flies it hard: barrel rolls at 100 m/s.
        World.LocalPlayer.DebugPlaceAboard(shipA, new Vector3(3f, 0.05f, -6f), Vector3.Zero);
        await Ask("A", "place", new Dict { ["ship"] = 1, ["pos"] = WalkStart }); // a clear patch of deck, away from the cargo
        await Ask("B", "place", new Dict { ["ship"] = 1, ["pos"] = new Vector3(2.5f, 0.05f, 4f), ["yaw"] = 0.4f });
        await Ask("C", "place", new Dict { ["ship"] = 1, ["pos"] = new Vector3(-1f, 0.05f, 8f) });
        shipA.Position = new Vector3(0, 400, 0);
        shipA.StuntSpeed = 100f;
        shipA.StuntIntensity = 2f;
        World.RequestStunt(shipA, true);
        await Seconds(2.0);

        await AskAll(clients, "meter-start");
        await Ask("A", "walk", new Dict { ["radius"] = WalkRadius, ["speed"] = 1.5f });
        await Ask("B", "observe", new Dict { ["peer"] = _ready["A"], ["ship"] = 1 });
        await Seconds(12.0);
        var meters = await AskAll(clients, "meter-stop"); // before the test's own reports go up
        Dict truth = await Ask("A", "walk-stop");
        Dict seen = await Ask("B", "observe-stop");
        CheckPositionError("B watching A aboard ship A (100 m/s barrel rolls)", truth, seen, maxError: 0.12f, maxStep: 0.15f);
        PrintBandwidth("4 players in one zone, ship stunting", meters);

        // Gravity toggled by one client's RPC, seen everywhere.
        await Ask("C", "gravity", new Dict { ["ship"] = 1 });
        await Seconds(1.0);
        var states = await AskAll(clients, "state", new Dict { ["ships"] = new[] { 1 } });
        Check(!shipA.GravityOn && states.Values.All(s => !Ship1(s)["gravity"].AsBool()),
            $"gravity toggle from C's RPC applied on the host and all 3 clients (host {On(shipA.GravityOn)}, clients {string.Join("/", states.Values.Select(s => On(Ship1(s)["gravity"].AsBool())))})");

        // Cargo floats in zero-g on the host; every client draws it where the host had it.
        await Seconds(1.5);
        states = await AskAll(clients, "state", new Dict { ["ships"] = new[] { 1 } });
        CheckCargo("floating cargo", states, 1, 0.15f);
        World.RequestGravity(shipA);
        await Seconds(6.0);
        states = await AskAll(clients, "state", new Dict { ["ships"] = new[] { 1 } });
        CheckCargo("settled cargo", states, 1, 0.03f);

        // Stunt autopilot off by B's RPC.
        await Ask("B", "stunt", new Dict { ["ship"] = 1, ["on"] = false });
        await Seconds(0.8);
        states = await AskAll(clients, "state", new Dict { ["ships"] = new[] { 1 } });
        Check(!shipA.StuntAutopilot && states.Values.All(s => !Ship1(s)["stunt"].AsBool()), "stunt autopilot switched off by B's RPC, everywhere");

        // A asks for the pilot seat, flies, and stands up.
        shipA.Velocity = Vector3.Zero;
        shipA.LocalAngularVelocity = Vector3.Zero;
        Dict sat = await Ask("A", "sit", new Dict { ["ship"] = 1 });
        states = await AskAll(clients, "state", new Dict { ["ships"] = new[] { 1 } });
        int a = _ready["A"];
        Check(shipA.PilotPeer == a && sat["mode"].AsInt32() == (int)Player.Mode.Piloting && states.Values.All(s => Ship1(s)["pilot"].AsInt32() == a),
            "A sat in the pilot seat by RPC; the host and every client agree A is flying");
        await Ask("A", "thrust", new Dict { ["seconds"] = 1.5 });
        Check(shipA.Speed > 10f, $"A's flight inputs reach the host, which flies the ship ({shipA.Speed:F0} m/s after 1.5 s of thrust)");
        Dict stood = await Ask("A", "stand");
        await Seconds(0.5);
        Check(shipA.PilotPeer == 0 && stood["mode"].AsInt32() == (int)Player.Mode.Aboard, "A stood up; the seat is free again");

        // Zones: B goes to the moon. It must get nothing from the desert, and the desert nothing of B.
        await Ask("B", "zone", new Dict { ["zone"] = Zones.Moon });
        await Seconds(2.0);
        Dict bView = await Ask("B", "entities");
        int b = _ready["B"];
        Check(bView["ships"].AsInt32Array().SequenceEqual(new[] { 3 }) && bView["players"].AsInt32Array().Length == 0
              && bView["ship_zones"].AsInt32Array().All(z => z == Zones.Moon),
            $"B in zone 2 has only zone-2 entities (ships [{Ids(bView["ships"])}], players [{Ids(bView["players"])}])");
        var desert = await AskAll(new[] { "A", "C" }, "entities");
        Check(desert.Values.All(v => !v["ships"].AsInt32Array().Contains(3) && !v["players"].AsInt32Array().Contains(b) && !v["avatars"].AsInt32Array().Contains(b)),
            $"A and C in zone 1 have no zone-2 entities: no ship C, no B ({string.Join("; ", desert.Select(kv => $"{kv.Key}: ships [{Ids(kv.Value["ships"])}] players [{Ids(kv.Value["players"])}]"))})");
        await Ask("B", "zone", new Dict { ["zone"] = Zones.Desert });
        await Seconds(2.0);
        bView = await Ask("B", "entities");
        Check(bView["ships"].AsInt32Array().OrderBy(i => i).SequenceEqual(new[] { 1, 2 }) && bView["players"].AsInt32Array().Length == 3,
            $"B back in zone 1 gets zone 1 again (ships [{Ids(bView["ships"])}], players [{Ids(bView["players"])}])");

        // C leaves and rejoins; the world changes while it is away.
        int oldC = _ready["C"];
        _ready.Remove("C");
        await Ask2(oldC, "rejoin", new Dict { ["after"] = 2.0 });
        await Until(() => !World.NetPlayers.Any(p => p.PeerId == oldC), 10, "C's avatar to go");
        await Seconds(0.5);
        var others = await AskAll(new[] { "A", "B" }, "entities");
        Check(others.Values.All(v => !v["players"].AsInt32Array().Contains(oldC) && !v["avatars"].AsInt32Array().Contains(oldC)),
            "C left: its avatar is gone on the host, A and B");
        World.RequestGravity(shipA); // off again
        Ship shipB = World.Ships[2];
        shipB.Position = new Vector3(40f, 25f, -30f);
        shipB.Basis = new Basis(Vector3.Up, 0.7f);
        await Until(() => _ready.ContainsKey("C"), 30, "C to rejoin");
        await Seconds(2.5);
        Dict c = await Ask("C", "state", new Dict { ["ships"] = new[] { 1, 2 } });
        Dict cView = await Ask("C", "entities");
        Dict cA = (Dict)c["1"], cB = (Dict)c["2"];
        Vector3 aTruth = HistoryAt(1, cA["t"].AsDouble()).pos;
        float shipAError = cA["pos"].AsVector3().DistanceTo(aTruth), shipBError = cB["pos"].AsVector3().DistanceTo(shipB.GlobalPosition);
        Check(_ready["C"] != oldC && cA["gravity"].AsBool() == shipA.GravityOn && cB["gravity"].AsBool() == shipB.GravityOn
              && shipAError < 0.1f && shipBError < 0.05f && cView["players"].AsInt32Array().Length == 3,
            $"C rejoined as a new peer with a consistent world: gravity matches, ship A within {shipAError:F3} m, moved ship B within {shipBError:F3} m, sees 3 players");
        CheckCargo("C's cargo after rejoining", new Dictionary<string, Dict> { ["C"] = c }, 1, 0.15f);
        World.RequestGravity(shipA);
    }

    async Task DedicatedScenario()
    {
        string[] clients = { "D", "E" };
        await Until(() => clients.All(_ready.ContainsKey), 60, "2 clients to join");
        Check(World.LocalPlayer == null && World.NetPlayers.Count() == 2 && !World.NetPlayers.Any(p => p.PeerId == 1) && World.Avatars.Count == 0,
            "dedicated server: no local player, just the 2 clients' avatars, nothing drawn");
        Ship shipA = World.Ships[1];
        await Ask("D", "place", new Dict { ["ship"] = 1, ["pos"] = WalkStart });
        await Ask("E", "place", new Dict { ["ship"] = 1, ["pos"] = new Vector3(2.5f, 0.05f, 4f) });
        shipA.Position = new Vector3(0, 300, 0);
        shipA.StuntIntensity = 1.5f;
        World.SetStunt(1, true);
        await Seconds(1.5);
        await AskAll(clients, "meter-start");
        await Ask("D", "walk", new Dict { ["radius"] = WalkRadius, ["speed"] = 1.5f });
        await Ask("E", "observe", new Dict { ["peer"] = _ready["D"], ["ship"] = 1 });
        await Seconds(8.0);
        var meters = await AskAll(clients, "meter-stop");
        Dict truth = await Ask("D", "walk-stop");
        Dict seen = await Ask("E", "observe-stop");
        CheckPositionError("dedicated: E watching D aboard the stunting ship", truth, seen, maxError: 0.12f, maxStep: 0.15f);
        PrintBandwidth("2 players on a dedicated server", meters);

        await Ask("E", "gravity", new Dict { ["ship"] = 1 });
        await Seconds(1.0);
        var states = await AskAll(clients, "state", new Dict { ["ships"] = new[] { 1 } });
        Check(!shipA.GravityOn && states.Values.All(s => !Ship1(s)["gravity"].AsBool()), "dedicated: E's gravity RPC applied on the server and on D");
    }

    /// <summary>Screenshots: sets the scene for the windowed client "Kit", which captures what it sees.</summary>
    async Task ShotScenario()
    {
        await Until(() => _ready.ContainsKey("Ana") && _ready.ContainsKey("Kit"), 60, "Ana and Kit to join");
        Ship shipA = World.Ships[1];
        shipA.Position = new Vector3(0, 140, 0);
        shipA.StuntSpeed = 45f;
        shipA.StuntIntensity = 0.35f;
        World.RequestStunt(shipA, true);
        World.LocalPlayer.DebugPlaceAboard(shipA, new Vector3(-1.5f, 0.05f, 4.6f), Vector3.Zero);
        World.LocalPlayer.DebugSetLook(Mathf.Pi - 0.35f, 0f);
        await Ask("Ana", "place", new Dict { ["ship"] = 1, ["pos"] = new Vector3(1.7f, 0.05f, 5.6f), ["yaw"] = Mathf.Pi + 0.6f });
        await Ask("Kit", "place", new Dict { ["ship"] = 1, ["pos"] = new Vector3(0.3f, 0.05f, 8.3f), ["yaw"] = 0.05f });
        await Ask("Kit", "look", new Dict { ["yaw"] = 0.05f, ["pitch"] = -0.12f, ["third"] = true });
        await Seconds(4.0);
        await Ask("Kit", "capture", new Dict { ["name"] = "aboard" });

        // The moon zone: Kit and Ana go; the host and the desert ships stay behind.
        await Ask("Ana", "zone", new Dict { ["zone"] = Zones.Moon });
        await Ask("Kit", "zone", new Dict { ["zone"] = Zones.Moon });
        await Seconds(1.0);
        await Ask("Ana", "place-outside", new Dict { ["pos"] = new Vector3(-6.3f, 0.05f, 25.4f), ["yaw"] = 2.25f });
        await Ask("Kit", "place-outside", new Dict { ["pos"] = new Vector3(-12f, 0.05f, 30f), ["yaw"] = -0.55f });
        await Ask("Kit", "look", new Dict { ["yaw"] = -0.55f, ["pitch"] = 0.08f, ["third"] = true });
        await Seconds(2.0);
        await Ask("Kit", "capture", new Dict { ["name"] = "moon" });
    }

    /// <summary>Like Ask, but to a peer ID (for a client about to leave).</summary>
    async Task Ask2(int peer, string op, Dict args)
    {
        _ready["__leaving"] = peer;
        await Ask("__leaving", op, args);
        _ready.Remove("__leaving");
    }

    // ---- Director: checks ---------------------------------------------------------------------------

    void CheckPositionError(string what, Dict truth, Dict seen, float maxError, float maxStep)
    {
        int[] tt = truth["t"].AsInt32Array();
        Vector3[] tp = truth["pos"].AsVector3Array();
        double[] st = seen["t"].AsFloat64Array();
        Vector3[] sp = seen["pos"].AsVector3Array();
        var errors = new List<float>();
        for (int i = 0; i < st.Length; i++)
        {
            double t = st[i];
            if (tt.Length < 2 || t < tt[0] + 500 || t > tt[^1]) continue; // skip the first half second of walking
            int j = Array.FindIndex(tt, x => x >= t);
            if (j <= 0) continue;
            float f = tt[j] > tt[j - 1] ? (float)((t - tt[j - 1]) / (tt[j] - tt[j - 1])) : 0f;
            errors.Add(sp[i].DistanceTo(tp[j - 1].Lerp(tp[j], f)));
        }
        errors.Sort();
        float mean = errors.Count > 0 ? errors.Average() : float.NaN;
        float p95 = errors.Count > 0 ? errors[(int)(errors.Count * 0.95)] : float.NaN;
        float max = errors.Count > 0 ? errors[^1] : float.NaN;
        float step = seen["max_step"].AsSingle(), ownStep = truth["max_step"].AsSingle();
        int missing = seen["missing"].AsInt32(), frames = seen["frames"].AsInt32();
        GD.Print($"[{TestName}] position error ({what}): {errors.Count} samples, mean {mean * 100:F1} cm, p95 {p95 * 100:F1} cm, max {max * 100:F1} cm; "
                 + $"largest jump between frames {step * 100:F1} cm (the walker's own largest step {ownStep * 100:F1} cm); avatar missing {missing}/{frames} frames");
        Check(errors.Count > 300 && max < maxError && step < maxStep && missing == 0,
            $"{what}: drawn at the right ship-local spot (max error {max * 100:F1} cm < {maxError * 100:F0} cm), never popping (max {step * 100:F1} cm/frame < {maxStep * 100:F0} cm)");
    }

    void CheckCargo(string what, Dictionary<string, Dict> states, int ship, float tolerance)
    {
        float worst = 0f;
        var parts = new List<string>();
        foreach (var (name, state) in states)
        {
            var s = (Dict)state[ship.ToString()];
            Vector3[] crates = s["crates"].AsVector3Array();
            Vector3[] truth = HistoryAt(ship, s["cargo_t"].AsDouble()).crates;
            float error = crates.Zip(truth, (x, y) => x.DistanceTo(y)).Max();
            worst = Mathf.Max(worst, error);
            parts.Add($"{name} {error * 100:F1} cm");
        }
        Check(worst < tolerance, $"{what} matches the host on every client within {tolerance * 100:F0} cm ({string.Join(", ", parts)})");
    }

    void PrintBandwidth(string what, Dictionary<string, Dict> meters)
    {
        GD.Print($"[{TestName}] bandwidth per client, {what} (payload = Godot packets; wire = UDP payload incl. ENet headers):");
        foreach (var (name, m) in meters)
        {
            double s = m["seconds"].AsDouble();
            GD.Print($"[{TestName}]   {name}: down {Kb(m["bytes_in"], s)} KB/s payload / {Kb(m["wire_in"], s)} KB/s wire ({m["packets_in"].AsInt64() / s:F0} pkt/s), "
                     + $"up {Kb(m["bytes_out"], s)} KB/s payload / {Kb(m["wire_out"], s)} KB/s wire ({m["packets_out"].AsInt64() / s:F0} pkt/s)");
        }
        Pass($"bandwidth measured for {meters.Count} clients ({what})");
    }

    static string Kb(Variant bytes, double seconds) => (bytes.AsInt64() / 1024.0 / seconds).ToString("F1");
    static Dict Ship1(Dict state) => (Dict)state["1"];
    static string On(bool on) => on ? "on" : "off";
    static string Ids(Variant ids) => string.Join(",", ids.AsInt32Array().Select(i => i > 1000 ? "peer" : i.ToString()));

    void Check(bool ok, string what)
    {
        if (ok) Pass(what);
        else Fail(what);
    }

    void Pass(string what) => GD.Print($"PASS  {what}");

    void Fail(string what)
    {
        Failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
