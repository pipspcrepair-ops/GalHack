using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GalHack.Net;

/// <summary>
/// The session layer: host, join, leave, reconnect, and a headless dedicated server mode, over
/// any <see cref="ITransport"/>. It knows nothing about the game.
///
/// It keeps the player list (names and pings, measured by the server once a second and shared
/// with everyone), says hello with a protocol version, tells clients when the host is ending the
/// session so they can leave gracefully, and tries to reconnect a client that loses the host
/// unexpectedly.
///
/// Godot's high-level multiplayer for this node's branch does the rest. Server relay is off: in
/// this game clients only ever talk to the host.
/// </summary>
public partial class NetSession : Node
{
    public enum Role { Offline, Host, Client, Dedicated }

    public const int ProtocolVersion = 1;

    public sealed class PeerInfo
    {
        public int Id;
        public string Name = "";
        public int PingMs = -1;
    }

    public Role CurrentRole { get; private set; }
    public bool IsServer => CurrentRole is Role.Host or Role.Dedicated;
    public bool IsOnline => CurrentRole != Role.Offline;

    /// <summary>Hosting, or (as a client) accepted by the host.</summary>
    public bool Connected { get; private set; }

    public ITransport Transport { get; private set; }

    /// <summary>The peer in use, wrapped so traffic is counted (and, in tests, made worse).</summary>
    public ConditionedPeer Peer { get; private set; }

    /// <summary>Simulated latency and loss for this process's traffic. Tests only.</summary>
    public NetConditions Conditions;

    public string LocalName = "Player";
    public int MaxPlayers = 8;
    public bool AutoReconnect = true;
    public int ReconnectAttempts = 3;
    public string Address { get; private set; }
    public string Status { get; private set; } = "";
    public int LocalId => IsServer ? 1 : IsOnline && Peer != null ? Multiplayer.GetUniqueId() : 0;
    public IReadOnlyDictionary<int, PeerInfo> Peers => _peers;

    /// <summary>The host is up, or this client was accepted (again, after a reconnect).</summary>
    public event Action Started;
    /// <summary>Server: a peer said hello and is now in the session.</summary>
    public event Action<int> PeerJoined;
    /// <summary>Server: a peer left or dropped.</summary>
    public event Action<int> PeerLeft;
    /// <summary>The player list or pings changed.</summary>
    public event Action RosterChanged;
    /// <summary>Client: the host vanished; reconnecting.</summary>
    public event Action<string> ConnectionLost;
    /// <summary>The session is over. The flag is true when it ended on purpose (you left, or the host ended it).</summary>
    public event Action<string, bool> Ended;

    readonly Dictionary<int, PeerInfo> _peers = new();
    readonly Dictionary<int, double> _pending = new(); // server: connected, no hello yet
    string _endingReason;
    double _retryAt, _closeAt, _nextPing;
    string _closeReason;
    int _attempt;
    bool _detaching;

    static double Now => Time.GetTicksUsec() / 1_000_000.0;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        if (IsOnline) Close();
        Transport?.Shutdown();
    }

    // ---- Starting and stopping ------------------------------------------------------------

    /// <summary>Hosts a session. A dedicated server has no local player.</summary>
    public Error Host(ITransport transport, bool dedicated)
    {
        if (IsOnline) return Error.AlreadyInUse;
        MultiplayerPeer inner = transport.CreateHost(dedicated ? MaxPlayers : MaxPlayers - 1, out string error);
        if (inner == null)
        {
            SetStatus(error);
            return Error.CantCreate;
        }
        Transport = transport;
        CurrentRole = dedicated ? Role.Dedicated : Role.Host;
        _peers.Clear();
        if (!dedicated) _peers[1] = new PeerInfo { Id = 1, Name = LocalName, PingMs = 0 };
        Attach(inner);
        Connected = true;
        SetStatus(dedicated ? $"Dedicated server up ({transport.Describe})" : $"Hosting ({transport.Describe})");
        Started?.Invoke();
        RosterChanged?.Invoke();
        return Error.Ok;
    }

    /// <summary>Joins a session at <paramref name="address"/>.</summary>
    public Error Join(ITransport transport, string address)
    {
        if (IsOnline) return Error.AlreadyInUse;
        Transport = transport;
        Address = address;
        CurrentRole = Role.Client;
        _endingReason = null;
        _attempt = 0;
        return Connect();
    }

    /// <summary>Joins the last session again (after leaving it or losing it).</summary>
    public Error Rejoin() => Transport == null || Address == null ? Error.Unconfigured : Join(Transport, Address);

    /// <summary>
    /// Leaves the session on purpose. A host tells everyone first, so their games end gracefully
    /// instead of timing out.
    /// </summary>
    public void Leave(string reason = "You left the session.")
    {
        if (!IsOnline || _closeAt > 0) return;
        if (IsServer && Multiplayer.GetPeers().Length > 0)
        {
            Rpc(MethodName.Ending, reason);
            _closeAt = Now + 0.3; // long enough for the notice to go out
            _closeReason = reason;
            return;
        }
        Finish(reason, graceful: true);
    }

    Error Connect()
    {
        MultiplayerPeer inner = Transport.CreateClient(Address, out string error);
        if (inner == null)
        {
            Finish(error, graceful: false);
            return Error.CantConnect;
        }
        Attach(inner);
        SetStatus(_attempt > 0 ? $"Reconnecting ({_attempt}/{ReconnectAttempts})..." : "Connecting...");
        return Error.Ok;
    }

    void Attach(MultiplayerPeer inner)
    {
        Peer = new ConditionedPeer(inner, Conditions);
        if (Multiplayer is SceneMultiplayer scene) scene.ServerRelay = false;
        Multiplayer.MultiplayerPeer = Peer;
    }

    /// <summary>Drops the connection without telling anyone (Godot keeps any spawned nodes; the game cleans up).</summary>
    void Close()
    {
        _detaching = true;
        Peer?.FlushOutgoing();
        Peer?.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        _detaching = false;
        Peer = null;
        Connected = false;
        _pending.Clear();
    }

    void Finish(string reason, bool graceful)
    {
        if (CurrentRole == Role.Offline) return;
        Close();
        CurrentRole = Role.Offline;
        _peers.Clear();
        _retryAt = _closeAt = 0;
        SetStatus(reason);
        Ended?.Invoke(reason, graceful);
    }

    // ---- Every frame ----------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!IsOnline) return;
        Transport?.Poll();
        double now = Now;

        if (_closeAt > 0 && now >= _closeAt) Finish(_closeReason, graceful: true);
        if (_retryAt > 0 && now >= _retryAt)
        {
            _retryAt = 0;
            Connect();
        }
        if (IsServer && Connected && now >= _nextPing)
        {
            _nextPing = now + 1.0;
            long stamp = (long)Time.GetTicksUsec();
            foreach (int id in _peers.Keys.Where(id => id != 1).ToList()) RpcId(id, MethodName.PingRequest, stamp);
            BroadcastRoster();
            foreach (var (id, since) in _pending.ToList())
                if (now - since > 10) Kick(id, "Never said hello.");
        }
    }

    // ---- Godot multiplayer signals ---------------------------------------------------------

    void OnPeerConnected(long id)
    {
        if (IsServer) _pending[(int)id] = Now;
    }

    void OnPeerDisconnected(long id)
    {
        if (!IsServer) return;
        _pending.Remove((int)id);
        if (!_peers.Remove((int)id)) return;
        PeerLeft?.Invoke((int)id);
        BroadcastRoster();
    }

    void OnConnectedToServer()
    {
        SetStatus("Joining...");
        RpcId(1, MethodName.Hello, LocalName, ProtocolVersion);
    }

    // Godot emits these from inside its poll; swapping the peer there is unsafe, so wait a frame.
    void OnConnectionFailed() { if (!_detaching) Callable.From(ConnectionFailed).CallDeferred(); }
    void OnServerDisconnected() { if (!_detaching) Callable.From(ServerDisconnected).CallDeferred(); }

    void ConnectionFailed()
    {
        if (CurrentRole != Role.Client || Connected) return;
        if (_attempt > 0) ScheduleRetry();
        else Finish(DescribeFailure("Couldn't reach the host."), graceful: false);
    }

    void ServerDisconnected()
    {
        if (CurrentRole != Role.Client) return;
        if (_endingReason != null)
        {
            Finish(_endingReason, graceful: true);
            return;
        }
        string reason = DescribeFailure("Lost the connection to the host.");
        if (!AutoReconnect || !Connected)
        {
            Finish(reason, graceful: false);
            return;
        }
        Close();
        _peers.Clear();
        ConnectionLost?.Invoke(reason);
        _attempt = 0;
        ScheduleRetry();
    }

    void ScheduleRetry()
    {
        Close();
        if (++_attempt > ReconnectAttempts)
        {
            Finish("Lost the connection to the host, and couldn't reconnect.", graceful: false);
            return;
        }
        _retryAt = Now + Math.Pow(2, _attempt - 1); // 1, 2, 4 s
        SetStatus($"Connection lost. Reconnecting ({_attempt}/{ReconnectAttempts})...");
    }

    string DescribeFailure(string fallback) =>
        Peer?.Inner is SteamMultiplayerPeer steam && steam.LastError.Length > 0 ? steam.LastError : fallback;

    void Kick(int id, string reason)
    {
        _pending.Remove(id);
        RpcId(id, MethodName.Refused, reason);
        if (Multiplayer is SceneMultiplayer scene) scene.DisconnectPeer(id);
    }

    void BroadcastRoster()
    {
        int[] ids = _peers.Keys.OrderBy(i => i).ToArray();
        string[] names = ids.Select(i => _peers[i].Name).ToArray();
        int[] pings = ids.Select(i => _peers[i].PingMs).ToArray();
        if (Multiplayer.GetPeers().Length > 0) Rpc(MethodName.Roster, ids, names, pings);
        RosterChanged?.Invoke();
    }

    void SetStatus(string status)
    {
        Status = status ?? "";
    }

    // ---- RPCs -----------------------------------------------------------------------------

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Hello(string name, int version)
    {
        if (!IsServer) return;
        int id = Multiplayer.GetRemoteSenderId();
        _pending.Remove(id);
        if (version != ProtocolVersion)
        {
            Kick(id, $"Version mismatch: the host runs protocol {ProtocolVersion}, you run {version}.");
            return;
        }
        if (_peers.Count >= MaxPlayers)
        {
            Kick(id, "The session is full.");
            return;
        }
        name = string.IsNullOrWhiteSpace(name) ? $"Player {id}" : name.Trim()[..Math.Min(name.Trim().Length, 24)];
        _peers[id] = new PeerInfo { Id = id, Name = name };
        PeerJoined?.Invoke(id);
        BroadcastRoster();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Roster(int[] ids, string[] names, int[] pings)
    {
        _peers.Clear();
        for (int i = 0; i < ids.Length; i++)
            _peers[ids[i]] = new PeerInfo { Id = ids[i], Name = names[i], PingMs = pings[i] };
        if (!Connected && _peers.ContainsKey(Multiplayer.GetUniqueId()))
        {
            Connected = true;
            _attempt = 0;
            SetStatus($"Connected ({Transport.Describe})");
            Started?.Invoke();
        }
        RosterChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    void PingRequest(long stamp) => RpcId(1, MethodName.PingReply, stamp);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    void PingReply(long stamp)
    {
        if (!IsServer || !_peers.TryGetValue(Multiplayer.GetRemoteSenderId(), out PeerInfo info)) return;
        int rtt = (int)((Time.GetTicksUsec() - (ulong)stamp) / 1000);
        info.PingMs = info.PingMs < 0 ? rtt : (info.PingMs * 3 + rtt) / 4;
    }

    /// <summary>The host is ending the session on purpose: leave now, gracefully.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Ending(string reason)
    {
        _endingReason = $"The host ended the session ({reason}).";
        Callable.From(() => Finish(_endingReason, graceful: true)).CallDeferred();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Refused(string reason)
    {
        _endingReason = reason;
        Callable.From(() => Finish(reason, graceful: false)).CallDeferred();
    }
}
