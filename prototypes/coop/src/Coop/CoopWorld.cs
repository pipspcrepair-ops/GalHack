using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Net;
using Godot;

namespace Coop;

/// <summary>
/// The shared world of one co-op session, the same node at /root/Game/World on every machine.
///
/// The host owns the world: it simulates every ship and its cargo in every zone, and decides
/// interactions (the pilot seat, gravity, stunts, shoving crates) that clients request by RPC.
/// Each player's own movement is trusted: their client moves them and reports where they are,
/// ship-relative when aboard.
///
/// Replicated state lives in plain nodes (<see cref="NetShip"/>, <see cref="NetPlayer"/>) under
/// Net/, spawned by MultiplayerSpawners. Their synchronizers' zone filters mean a client only
/// ever receives the ships and players in its own zone. Clients load only their own zone, and
/// draw remote ships and players from interpolation buffers about 100 ms in the past.
/// </summary>
public partial class CoopWorld : Node, IPlayerActions
{
    const int SendEveryTicks = 2; // 30 Hz at 60 physics ticks
    const float PilotInputTimeout = 0.3f;
    const float MaxExtrapolation = 0.25f;

    static readonly Color[] Palette =
    {
        ShipLayout.Yellow, ShipLayout.Teal, ShipLayout.Red, new("7b2cbf"), new("2a9d8f"), new("f72585"), new("4cc9f0"), new("90be6d"),
    };

    public NetSession Session;
    public bool Dedicated;

    /// <summary>The local player arrived in the world (first spawn or after a reconnect).</summary>
    public event Action LocalPlayerSpawned;

    public Node PlayersRoot { get; private set; }
    public Node ShipsRoot { get; private set; }
    public int LocalZone { get; private set; }
    public Player LocalPlayer { get; private set; }

    /// <summary>Ships drawn or simulated here, by network ID. The host has all of them; a client, its zone's.</summary>
    public IReadOnlyDictionary<int, Ship> Ships => _ships;

    /// <summary>Other players drawn in your zone, by peer ID.</summary>
    public IReadOnlyDictionary<int, AvatarView> Avatars => _avatars;

    /// <summary>For each avatar drawn this tick: the sender-clock time drawn (s) and the snapshot used.</summary>
    public IReadOnlyDictionary<int, (double renderTime, PlayerSnap snap)> AvatarSamples => _avatarSamples;

    /// <summary>Client: for each ship drawn this tick, the host-clock time (s) its motion and its cargo were drawn at.</summary>
    public IReadOnlyDictionary<int, (double motion, double cargo)> ShipRenderTimes => _shipRenderTimes;

    MultiplayerSpawner _playerSpawner, _shipSpawner;
    readonly Dictionary<int, (double motion, double cargo)> _shipRenderTimes = new();
    Node _zonesRoot;
    TextureRect _screen;
    Camera3D _camera;
    bool _render;
    int _tick, _nextSlot;
    double _seatRequestAt;
    (int zone, int ship, Vector3 local)? _pendingSpawn;

    readonly Dictionary<int, ZoneWorld> _zones = new();
    readonly Dictionary<int, Ship> _ships = new();
    readonly List<Ship> _zoneShips = new();
    readonly Dictionary<int, AvatarView> _avatars = new();
    readonly Dictionary<int, (int ship, int mode)> _avatarFrames = new();
    readonly Dictionary<int, (double renderTime, PlayerSnap snap)> _avatarSamples = new();
    readonly Dictionary<int, string> _avatarDebug = new();

    /// <summary>For diagnosing a test: the last sample drawn for each avatar, as text.</summary>
    public IReadOnlyDictionary<int, string> AvatarDebug => _avatarDebug;
    readonly Dictionary<int, NetPlayer> _netPlayers = new();     // host: by peer
    readonly Dictionary<int, NetShip> _netShips = new();         // host: by ship ID
    readonly Dictionary<int, (Vector3 thrust, Vector3 turn, bool boost, double at)> _pilotInputs = new();
    readonly Dictionary<(int ship, int crate), (Vector3 impulse, double at)> _pushes = new();

    bool IsServer => Session.IsServer;

    public override void _Ready()
    {
        ProcessPhysicsPriority = -20; // place remote ships before ships (-10) and players (0) move
        _render = DisplayServer.GetName() != "headless";

        var net = new Node { Name = "Net" };
        AddChild(net);
        PlayersRoot = new Node { Name = "Players" };
        ShipsRoot = new Node { Name = "Ships" };
        net.AddChild(PlayersRoot);
        net.AddChild(ShipsRoot);
        _playerSpawner = new MultiplayerSpawner { Name = "PlayerSpawner", SpawnPath = new NodePath("../Players"), SpawnFunction = Callable.From<Variant, Node>(SpawnEntity) };
        _shipSpawner = new MultiplayerSpawner { Name = "ShipSpawner", SpawnPath = new NodePath("../Ships"), SpawnFunction = Callable.From<Variant, Node>(SpawnEntity) };
        net.AddChild(_playerSpawner);
        net.AddChild(_shipSpawner);

        _zonesRoot = new Node { Name = "Zones" };
        AddChild(_zonesRoot);
        _screen = new TextureRect
        {
            Name = "Screen",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        _screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screen);
        _camera = new Camera3D { Name = "Camera", Fov = 80f, Near = 0.05f, Far = 8000f };

        AddChild(new LateTick { Name = "LateTick", Tick = LatePhysics });

        GetViewport().SizeChanged += OnResize;
        Session.PeerJoined += OnPeerJoined;
        Session.PeerLeft += OnPeerLeft;
        if (IsServer) StartServer();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= OnResize;
        Session.PeerJoined -= OnPeerJoined;
        Session.PeerLeft -= OnPeerLeft;
        if (IsInstanceValid(_camera) && _camera.GetParent() == null) _camera.Free(); // never used (dedicated, or left before spawning)
    }

    public void HandleInput(InputEvent e) => LocalPlayer?.HandleInput(e);

    // ---- Spawning replicated entities -----------------------------------------------------

    /// <summary>Runs on the host and, with the same data, on every client the entity is visible to.</summary>
    Node SpawnEntity(Variant data)
    {
        var d = data.AsGodotDictionary();
        Func<int, int> zoneOf = IsServer ? ZoneOfPeer : null;
        int id = d["id"].AsInt32(), zone = d["zone"].AsInt32();
        string name = d["name"].AsString();
        return d["kind"].AsString() == "ship"
            ? NetShip.Create(id, name, zone, zoneOf)
            : NetPlayer.Create(id, name, zone, zoneOf);
    }

    /// <summary>Host: which zone a peer is in (0 until it has a player), for the visibility filters.</summary>
    int ZoneOfPeer(int peer) => _netPlayers.TryGetValue(peer, out NetPlayer p) && IsInstanceValid(p) ? p.Zone : 0;

    static Godot.Collections.Dictionary SpawnData(string kind, int id, string name, int zone) =>
        new() { ["kind"] = kind, ["id"] = id, ["name"] = name, ["zone"] = zone };

    // ---- Host -----------------------------------------------------------------------------

    void StartServer()
    {
        foreach (int zone in Zones.All) LoadZone(zone);
        CreateShip(1, "ShipA", Zones.Desert, new Vector3(0, Ship.GroundClearance, 0));
        CreateShip(2, "ShipB", Zones.Desert, new Vector3(40, Ship.GroundClearance, 0));
        CreateShip(3, "ShipC", Zones.Moon, new Vector3(0, Ship.GroundClearance, 0));

        if (Dedicated)
        {
            GD.Print($"[server] dedicated server up, no local player. Zones: {string.Join(", ", Zones.All.Select(Zones.Name))}");
            return;
        }
        Vector3 local = SpawnSpot(_nextSlot++);
        SpawnNetPlayer(1, Session.LocalName, Zones.Desert, 1, local);
        SpawnLocalPlayer(Zones.Desert, 1, local);
    }

    void CreateShip(int id, string name, int zone, Vector3 position)
    {
        var ship = new Ship { Name = name, NetId = id, Position = position };
        _zones[zone].Root.AddChild(ship);
        _ships[id] = ship;
        var net = (NetShip)_shipSpawner.Spawn(SpawnData("ship", id, name, zone));
        _netShips[id] = net;
        WriteShip(ship, net, NetClock.Ms);
    }

    NetPlayer SpawnNetPlayer(int peer, string name, int zone, int shipId, Vector3 local)
    {
        var net = (NetPlayer)_playerSpawner.Spawn(SpawnData("player", peer, name, zone));
        _netPlayers[peer] = net;
        // Stamp 0: not a moment on anyone's clock. Clients skip it; the owner's own reports (on its clock) follow.
        net.Report(0, zone, new PlayerSnap { Mode = (int)Player.Mode.Aboard, ShipId = shipId, Pos = local }, buffer: false);
        return net;
    }

    static Vector3 SpawnSpot(int slot) => ShipLayout.SpawnLocal + new Vector3(-3f + slot % 5 * 1.5f, 0, slot / 5 * 1.2f);

    void OnPeerJoined(int peer)
    {
        if (!IsServer) return;
        string name = Session.Peers.TryGetValue(peer, out NetSession.PeerInfo info) ? info.Name : $"Player {peer}";
        Vector3 local = SpawnSpot(_nextSlot++);
        SpawnNetPlayer(peer, name, Zones.Desert, 1, local);
        RpcId(peer, MethodName.Welcome, Zones.Desert, 1, local);
        GD.Print($"[server] {name} joined (peer {peer})");
    }

    void OnPeerLeft(int peer)
    {
        if (!IsServer) return;
        foreach (Ship ship in _ships.Values.Where(s => s.PilotPeer == peer)) ReleaseSeat(ship);
        if (_netPlayers.Remove(peer, out NetPlayer net) && IsInstanceValid(net))
        {
            GD.Print($"[server] {net.PlayerName} left (peer {peer})");
            net.QueueFree(); // Godot despawns it everywhere
        }
        RemoveAvatar(peer);
    }

    // ---- Client ---------------------------------------------------------------------------

    /// <summary>The host accepted us: here is where we start.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Welcome(int zone, int shipId, Vector3 local)
    {
        _pendingSpawn = (zone, shipId, local);
        if (LocalZone != zone) SwitchClientZone(zone);
    }

    /// <summary>Client: loads only this zone, unloading the one we were in.</summary>
    void SwitchClientZone(int zone)
    {
        ZoneWorld old = _zones.GetValueOrDefault(LocalZone);
        LoadZone(zone);
        LocalZone = zone;
        if (LocalPlayer != null)
            LocalPlayer.EnterZone(_zones[zone].Root, _zoneShips, Zones.Arrival(zone, Session.LocalId % 4), Zones.ArrivalYaw(zone));
        if (_camera.GetParent() != _zones[zone].Root)
        {
            if (_camera.GetParent() == null) _zones[zone].Root.AddChild(_camera);
            else _camera.Reparent(_zones[zone].Root, false);
        }
        FreeShipViews();
        FreeAvatars();
        if (old != null)
        {
            _zones.Remove(old.Zone);
            _zonesRoot.RemoveChild(old);
            old.QueueFree();
        }
        Show(zone);
    }

    // ---- Zones and the screen ---------------------------------------------------------------

    void LoadZone(int zone)
    {
        if (_zones.ContainsKey(zone)) return;
        ZoneWorld world = ZoneWorld.Create(zone);
        _zonesRoot.AddChild(world);
        _zones[zone] = world;
    }

    void Show(int zone)
    {
        Vector2I size = (Vector2I)GetViewport().GetVisibleRect().Size;
        foreach (var (z, world) in _zones) world.SetShown(_render && z == zone, size);
        _screen.Texture = _render && _zones.TryGetValue(zone, out ZoneWorld shown) ? shown.GetTexture() : null;
        _camera.MakeCurrent();
    }

    void OnResize() => Show(LocalZone);

    /// <summary>The debug transfer key: go to the other zone. Your client decides (movement is trusted).</summary>
    public void RequestZoneTransfer() => TransferLocalPlayer(Zones.Next(LocalZone));

    public void TransferLocalPlayer(int zone)
    {
        if (LocalPlayer == null || zone == LocalZone) return;
        if (LocalPlayer.CurrentMode == Player.Mode.Piloting) RequestSeat(LocalPlayer.Ship, false);
        if (IsServer)
        {
            LocalZone = zone;
            RebuildZoneShips();
            if (_camera.GetParent() != _zones[zone].Root) _camera.Reparent(_zones[zone].Root, false);
            LocalPlayer.EnterZone(_zones[zone].Root, _zoneShips, Zones.Arrival(zone, 0), Zones.ArrivalYaw(zone));
            FreeAvatars();
            Show(zone);
            if (_netPlayers.TryGetValue(1, out NetPlayer me)) me.Zone = zone;
        }
        else
        {
            SwitchClientZone(zone);
            SendPlayerState(); // tell the host now rather than on the next send
        }
        GD.Print($"[{Role}] moved to zone {zone} ({Zones.Name(zone)})");
    }

    void RebuildZoneShips()
    {
        _zoneShips.Clear();
        foreach (Ship ship in _ships.Values)
            if (ship.GetParent() == _zones.GetValueOrDefault(LocalZone)?.Root) _zoneShips.Add(ship);
    }

    // ---- The local player -----------------------------------------------------------------

    void SpawnLocalPlayer(int zone, int shipId, Vector3 local)
    {
        LocalZone = zone;
        ZoneWorld world = _zones[zone];
        if (_camera.GetParent() == null) world.Root.AddChild(_camera);
        RebuildZoneShips();
        LocalPlayer = new Player
        {
            Name = "LocalPlayer",
            MainWorld = world.Root,
            Camera = _camera,
            Ships = _zoneShips,
            Actions = this,
            BodyColor = ColorFor(Session.LocalId),
        };
        if (shipId != 0 && _ships.TryGetValue(shipId, out Ship ship)) LocalPlayer.SpawnAboard(ship, local);
        else LocalPlayer.SpawnOutside(Zones.Arrival(zone, 0), Zones.ArrivalYaw(zone));
        Show(zone);
        _pendingSpawn = null;
        LocalPlayerSpawned?.Invoke();
    }

    public static Color ColorFor(int peer) => Palette[peer == 1 ? 0 : 1 + (int)((uint)peer % (Palette.Length - 1))];

    // ---- IPlayerActions: requests to the host --------------------------------------------

    public void RequestSeat(Ship ship, bool sit)
    {
        if (ship == null) return;
        _seatRequestAt = NetClock.Now;
        if (IsServer) Seat(1, ship.NetId, sit);
        else RpcId(1, MethodName.SeatRequest, ship.NetId, sit);
    }

    public void RequestGravity(Ship ship)
    {
        if (ship == null) return;
        if (IsServer) ToggleGravity(ship.NetId);
        else RpcId(1, MethodName.GravityRequest, ship.NetId);
    }

    public void RequestStunt(Ship ship, bool on)
    {
        if (ship == null) return;
        if (IsServer) SetStunt(ship.NetId, on);
        else RpcId(1, MethodName.StuntRequest, ship.NetId, on);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void SeatRequest(int shipId, bool sit)
    {
        if (IsServer) Seat(Multiplayer.GetRemoteSenderId(), shipId, sit);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void GravityRequest(int shipId)
    {
        if (IsServer) ToggleGravity(shipId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void StuntRequest(int shipId, bool on)
    {
        if (IsServer) SetStunt(shipId, on);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void CratePush(int shipId, int crate, Vector3 impulse)
    {
        if (IsServer && _ships.TryGetValue(shipId, out Ship ship)) ship.PushCrate(crate, impulse.LimitLength(5f));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void PilotInput(int shipId, Vector3 thrust, Vector3 turn, bool boost)
    {
        if (!IsServer || !_ships.TryGetValue(shipId, out Ship ship) || ship.PilotPeer != Multiplayer.GetRemoteSenderId()) return;
        _pilotInputs[shipId] = (thrust.Clamp(-Vector3.One, Vector3.One), turn.Clamp(-Vector3.One, Vector3.One), boost, NetClock.Now);
    }

    /// <summary>A player's own client reports where they are. Trusted: the host just takes it.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    void SubmitPlayer(int stamp, int zone, int mode, int shipId, Vector3 pos, float yaw, float pitch, bool crouch)
    {
        if (!IsServer || !_netPlayers.TryGetValue(Multiplayer.GetRemoteSenderId(), out NetPlayer net)) return;
        if (Array.IndexOf(Zones.All, zone) < 0) return;
        net.Report(stamp, zone, new PlayerSnap { Mode = mode, ShipId = shipId, Pos = pos, Yaw = yaw, Pitch = pitch, Crouch = crouch });
    }

    // ---- Host: deciding interactions --------------------------------------------------------

    void Seat(int peer, int shipId, bool sit)
    {
        if (!_ships.TryGetValue(shipId, out Ship ship)) return;
        if (sit && ship.PilotPeer == 0)
        {
            ship.PilotPeer = peer;
            ship.StuntAutopilot = false;
        }
        else if (!sit && ship.PilotPeer == peer) ReleaseSeat(ship);
    }

    void ReleaseSeat(Ship ship)
    {
        ship.PilotPeer = 0;
        ship.ThrustInput = ship.TurnInput = Vector3.Zero;
        ship.Boost = false;
        _pilotInputs.Remove(ship.NetId);
    }

    public void ToggleGravity(int shipId)
    {
        if (_ships.TryGetValue(shipId, out Ship ship)) ship.SetGravity(!ship.GravityOn);
    }

    public void SetStunt(int shipId, bool on)
    {
        if (_ships.TryGetValue(shipId, out Ship ship)) ship.StuntAutopilot = on;
    }

    // ---- Every tick -------------------------------------------------------------------------

    /// <summary>Early (priority -20): put network state into ships before anything moves.</summary>
    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        double now = NetClock.Now;
        if (IsServer) ApplyPilotInputs(now);
        else
        {
            UpdateShipViews(now);
            if (_pendingSpawn is { } spawn && LocalZone == spawn.zone && (spawn.ship == 0 || _ships.ContainsKey(spawn.ship)))
                SpawnLocalPlayer(spawn.zone, spawn.ship, spawn.local);
        }
        ReconcileSeat(now);
    }

    /// <summary>Late (priority 20): share what happened this tick and draw the other players.</summary>
    void LatePhysics()
    {
        double now = NetClock.Now;
        if (IsServer)
        {
            int ms = NetClock.Ms;
            foreach (var (id, ship) in _ships) WriteShip(ship, _netShips[id], ms);
            if (LocalPlayer != null && _netPlayers.TryGetValue(1, out NetPlayer me)) me.Report(ms, LocalZone, LocalSnap());
        }
        else if (LocalPlayer != null && _tick % SendEveryTicks == 0 && Session.Connected)
        {
            SendPlayerState();
            if (LocalPlayer.CurrentMode == Player.Mode.Piloting && LocalPlayer.Ship != null)
            {
                Ship ship = LocalPlayer.Ship;
                RpcId(1, MethodName.PilotInput, ship.NetId, ship.ThrustInput, ship.TurnInput, ship.Boost);
            }
            FlushPushes(now);
        }
        PlaceAvatars(now);
    }

    void ApplyPilotInputs(double now)
    {
        foreach (Ship ship in _ships.Values)
        {
            if (ship.PilotPeer is 0 or 1) continue; // nobody, or the host's own player (who sets them directly)
            if (_pilotInputs.TryGetValue(ship.NetId, out var input) && now - input.at < PilotInputTimeout)
            {
                ship.ThrustInput = input.thrust;
                ship.TurnInput = input.turn;
                ship.Boost = input.boost;
            }
            else
            {
                ship.ThrustInput = ship.TurnInput = Vector3.Zero;
                ship.Boost = false;
            }
        }
    }

    static void WriteShip(Ship ship, NetShip net, int ms)
    {
        Transform3D t = ship.GlobalTransform;
        net.Stamp = ms;
        net.Pos = t.Origin;
        net.Rot = t.Basis.GetRotationQuaternion();
        net.Vel = ship.Velocity;
        net.Spin = ship.LocalAngularVelocity;
        net.Pilot = ship.PilotPeer;
        net.Gravity = ship.GravityOn;
        net.Stunt = ship.StuntAutopilot;
        net.CargoStamp = ms;
        net.Cargo = ship.ReadCargo();
        net.CargoSync.ReplicationInterval = ship.CargoAwake ? NetShip.CargoAwakeInterval : NetShip.CargoAsleepInterval;
    }

    PlayerSnap LocalSnap()
    {
        Player p = LocalPlayer;
        bool aboard = p.CurrentMode != Player.Mode.Outside && p.Ship != null;
        return new PlayerSnap
        {
            Mode = (int)p.CurrentMode,
            ShipId = aboard ? p.Ship.NetId : 0,
            Pos = aboard ? (p.CurrentMode == Player.Mode.Piloting ? ShipLayout.SeatLocal : p.ShipLocalPosition) : p.GlobalPosition,
            Yaw = p.Yaw,
            Pitch = p.Pitch,
            Crouch = p.Crouching,
        };
    }

    void SendPlayerState()
    {
        if (LocalPlayer == null || !Session.Connected) return;
        PlayerSnap s = LocalSnap();
        RpcId(1, MethodName.SubmitPlayer, NetClock.Ms, LocalZone, s.Mode, s.ShipId, s.Pos, s.Yaw, s.Pitch, s.Crouch);
    }

    /// <summary>Client: crates shoved this tick go to the host, a few times a second at most.</summary>
    void OnCratePushed(Ship ship, int crate, Vector3 impulse)
    {
        var key = (ship.NetId, crate);
        _pushes[key] = _pushes.TryGetValue(key, out var p) ? (p.impulse + impulse, p.at) : (impulse, NetClock.Now);
    }

    void FlushPushes(double now)
    {
        foreach (var (key, push) in _pushes.ToList())
        {
            if (now - push.at < 0.1) continue;
            RpcId(1, MethodName.CratePush, key.ship, key.crate, push.impulse);
            _pushes.Remove(key);
        }
    }

    /// <summary>The pilot seat is the host's call: sit once it says yes, stand if it says someone else flies.</summary>
    void ReconcileSeat(double now)
    {
        Player p = LocalPlayer;
        if (p == null || p.Ship == null || !IsInstanceValid(p.Ship)) return;
        int pilot = PilotOf(p.Ship.NetId);
        int me = Session.LocalId;
        if (p.WantsSeat)
        {
            if (pilot == me) p.EnterSeat();
            else if (pilot != 0 || now - _seatRequestAt > 1.0) p.SeatDenied();
        }
        else if (p.CurrentMode == Player.Mode.Piloting && pilot != me && now - _seatRequestAt > 1.0)
        {
            p.LeaveSeat();
        }
    }

    int PilotOf(int shipId)
    {
        if (IsServer) return _ships.TryGetValue(shipId, out Ship s) ? s.PilotPeer : 0;
        foreach (Node child in ShipsRoot.GetChildren())
            if (child is NetShip net && net.Id == shipId) return net.Pilot;
        return 0;
    }

    // ---- Drawing remote ships ---------------------------------------------------------------

    /// <summary>Client: one ship copy per NetShip in our zone, placed from its buffer.</summary>
    void UpdateShipViews(double now)
    {
        var seen = new HashSet<int>();
        ZoneWorld zone = _zones.GetValueOrDefault(LocalZone);
        foreach (Node child in ShipsRoot.GetChildren())
        {
            if (child is not NetShip net || net.IsQueuedForDeletion() || zone == null || net.Zone != LocalZone) continue;
            seen.Add(net.Id);
            if (!_ships.TryGetValue(net.Id, out Ship ship))
            {
                ship = new Ship { Name = net.ShipName, NetId = net.Id, Remote = true };
                ship.CratePushed += OnCratePushed;
                zone.Root.AddChild(ship);
                _ships[net.Id] = ship;
                _zoneShips.Add(ship);
                ApplyMotion(ship, net, now);
                ship.ResetPhysicsInterpolation();
            }
            else ApplyMotion(ship, net, now);
            double motionTime = net.Motion.TrySample(now, out var drawn) ? drawn.RenderTime : 0;

            ship.SetGravity(net.Gravity);
            ship.StuntAutopilot = net.Stunt;
            ship.PilotPeer = net.Pilot;
            double cargoTime = 0;
            if (net.CargoBuffer.TrySample(now, out var cargo))
            {
                ship.ApplyCargo(cargo.A, cargo.B, Mathf.Min(cargo.Alpha, 1f)); // crates hold rather than guess
                cargoTime = cargo.RenderTime;
            }
            _shipRenderTimes[net.Id] = (motionTime, cargoTime);
        }
        foreach (int id in _ships.Keys.Where(id => !seen.Contains(id)).ToList()) FreeShipView(id);
    }

    static void ApplyMotion(Ship ship, NetShip net, double now)
    {
        if (!net.Motion.TrySample(now, out var s)) return;
        ShipSnap a = s.A, b = s.B;
        Vector3 pos;
        Quaternion rot;
        Vector3 vel;
        if (s.Extra > 0)
        {
            // Past the newest snapshot: coast on its motion for a moment, then hold.
            float e = (float)Math.Min(s.Extra, MaxExtrapolation);
            pos = b.Pos + b.Vel * e;
            float spin = b.Spin.Length();
            rot = spin > 1e-5f ? (b.Rot * new Quaternion(b.Spin / spin, spin * e)).Normalized() : b.Rot;
            vel = b.Vel;
        }
        else
        {
            float span = (float)(s.TimeB - s.TimeA);
            pos = span > 0 ? Hermite(a.Pos, a.Vel, b.Pos, b.Vel, s.Alpha, span) : a.Pos;
            rot = a.Rot.Normalized().Slerp(b.Rot.Normalized(), s.Alpha);
            vel = a.Vel.Lerp(b.Vel, s.Alpha);
        }
        ship.ApplyRemoteState(new Transform3D(new Basis(rot), pos), vel, s.Extra > 0 ? b.Spin : a.Spin.Lerp(b.Spin, s.Alpha));
    }

    /// <summary>Cubic Hermite: follows the curve a fast, turning ship really flew, not straight lines.</summary>
    static Vector3 Hermite(Vector3 p0, Vector3 v0, Vector3 p1, Vector3 v1, float t, float span)
    {
        float t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * span * v0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * span * v1;
    }

    void FreeShipView(int id)
    {
        _shipRenderTimes.Remove(id);
        if (!_ships.Remove(id, out Ship ship)) return;
        _zoneShips.Remove(ship);
        if (IsInstanceValid(ship)) ship.QueueFree();
    }

    void FreeShipViews()
    {
        if (IsServer) return;
        foreach (int id in _ships.Keys.ToList()) FreeShipView(id);
    }

    // ---- Drawing other players --------------------------------------------------------------

    void PlaceAvatars(double now)
    {
        _avatarSamples.Clear();
        ZoneWorld zone = _zones.GetValueOrDefault(LocalZone);
        var seen = new HashSet<int>();
        int me = Session.LocalId;
        foreach (Node child in PlayersRoot.GetChildren())
        {
            if (child is not NetPlayer net || net.IsQueuedForDeletion() || net.PeerId == me || zone == null || net.Zone != LocalZone) continue;
            if (!net.Buffer.TrySample(now, out var s)) continue;
            seen.Add(net.PeerId);
            if (!_avatars.TryGetValue(net.PeerId, out AvatarView avatar))
            {
                avatar = new AvatarView(ColorFor(net.PeerId), net.PlayerName) { Name = $"Avatar{net.PeerId}" };
                zone.Root.AddChild(avatar);
                _avatars[net.PeerId] = avatar;
            }

            PlayerSnap a = s.A, b = s.B;
            bool sameFrame = a.ShipId == b.ShipId && a.Mode == b.Mode;
            PlayerSnap use = sameFrame ? a : (s.Alpha >= 1f ? b : a);
            Vector3 feet = sameFrame ? a.Pos.Lerp(b.Pos, s.Alpha) : use.Pos;
            float yaw = sameFrame ? Mathf.LerpAngle(a.Yaw, b.Yaw, s.Alpha) : use.Yaw;
            float crouch = sameFrame ? Mathf.Lerp(a.Crouch ? 1f : 0f, b.Crouch ? 1f : 0f, Mathf.Min(s.Alpha, 1f)) : (use.Crouch ? 1f : 0f);

            Transform3D frame = Transform3D.Identity;
            if (use.ShipId != 0)
            {
                if (!_ships.TryGetValue(use.ShipId, out Ship ship) || !IsInstanceValid(ship))
                {
                    avatar.Visible = false;
                    continue;
                }
                frame = ship.GlobalTransform;
                if (use.Mode == (int)Player.Mode.Piloting) feet = ShipLayout.SeatLocal;
            }
            avatar.Place(frame, feet, yaw, use.Pitch, Mathf.Lerp(1f, Player.CrouchHeadHeight / Player.HeadHeight, crouch));
            avatar.Visible = true;

            // Jumping between frames (boarding, stepping off) is a teleport, not a slide.
            var frameKey = (use.ShipId, use.Mode == (int)Player.Mode.Outside ? 0 : 1);
            if (!_avatarFrames.TryGetValue(net.PeerId, out var last) || last != frameKey) avatar.ResetPhysicsInterpolation();
            _avatarFrames[net.PeerId] = frameKey;
            _avatarSamples[net.PeerId] = (s.RenderTime, use with { Pos = feet });
            _avatarDebug[net.PeerId] = $"t {s.RenderTime:F3} [{s.TimeA:F3} {s.TimeB:F3}] a {s.Alpha:F2} extra {s.Extra:F3} A {a.Pos} B {b.Pos} ship {use.ShipId} frame {frame.Origin}";
        }
        foreach (int peer in _avatars.Keys.Where(p => !seen.Contains(p)).ToList()) RemoveAvatar(peer);
    }

    void RemoveAvatar(int peer)
    {
        _avatarFrames.Remove(peer);
        if (_avatars.Remove(peer, out AvatarView avatar) && IsInstanceValid(avatar)) avatar.QueueFree();
    }

    void FreeAvatars()
    {
        foreach (int peer in _avatars.Keys.ToList()) RemoveAvatar(peer);
    }

    // ---- For the HUD and tests ----------------------------------------------------------------

    public string Role => Session.CurrentRole switch
    {
        NetSession.Role.Dedicated => "server",
        NetSession.Role.Host => "host",
        _ => "client",
    };

    /// <summary>The replicated state of a ship as this machine knows it, or null.</summary>
    public NetShip NetShipOf(int id) =>
        ShipsRoot.GetChildren().OfType<NetShip>().FirstOrDefault(s => s.Id == id && !s.IsQueuedForDeletion());

    public IEnumerable<NetPlayer> NetPlayers => PlayersRoot.GetChildren().OfType<NetPlayer>().Where(p => !p.IsQueuedForDeletion());

    public IEnumerable<NetShip> NetShips => ShipsRoot.GetChildren().OfType<NetShip>().Where(s => !s.IsQueuedForDeletion());

}

