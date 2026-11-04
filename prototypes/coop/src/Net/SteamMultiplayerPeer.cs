using System;
using System.Collections.Generic;
using Godot;

namespace GalHack.Net;

/// <summary>
/// A Godot MultiplayerPeer over Steam networking sockets (P2P through Steam's relays), so the
/// high-level multiplayer API (RPCs, MultiplayerSynchronizer, MultiplayerSpawner) works the
/// same over Steam as over ENet.
///
/// It talks only to <see cref="ISteamSockets"/>, never to Steam directly, which is what makes it
/// testable against a fake socket layer.
///
/// Topology is a star, like ENet: every client connects to the host, and the host relays.
/// Each Steam message carries a small header:
/// <code>
///   Data    [0][channel][transfer mode][sequence lo][sequence hi][payload...]
///   Hello   [1][proposed peer ID, int32]     client → host, once connected
///   Welcome [2][assigned peer ID, int32]     host → client; the client is now connected
/// </code>
/// Reliable packets use Steam's reliable messages, which arrive in order. Unreliable ones use
/// unreliable messages, and "unreliable ordered" ones also carry a per-channel sequence number
/// so a stale packet that arrives late is dropped, as ENet does.
/// </summary>
public partial class SteamMultiplayerPeer : MultiplayerPeerExtension
{
    /// <summary>Steam's largest message (k_cbMaxSteamNetworkingSocketsMessageSizeSend).</summary>
    public const int MaxMessageSize = 512 * 1024;
    const int DataHeader = 5;
    const byte MsgData = 0, MsgHello = 1, MsgWelcome = 2;

    sealed class Link
    {
        public uint Connection;
        public ulong Remote;
        public int PeerId;
        public bool Admitted, HelloSent, Closed;
        public readonly Dictionary<int, ushort> SendSequence = new();
        public readonly Dictionary<int, ushort> ReceiveSequence = new();
    }

    readonly record struct Packet(int Peer, int Channel, TransferModeEnum Mode, byte[] Data);

    readonly ISteamSockets _sockets;
    readonly bool _server;
    readonly Dictionary<uint, Link> _links = new();
    readonly Dictionary<int, Link> _peers = new();
    readonly Queue<Packet> _incoming = new();
    readonly List<SocketEvent> _events = new();
    readonly List<byte[]> _received = new();
    readonly List<Link> _scratch = new();
    readonly List<int> _pendingDisconnects = new();
    Link _hostLink;
    int _uniqueId;
    ConnectionStatus _status = ConnectionStatus.Disconnected;
    int _targetPeer, _transferChannel;
    TransferModeEnum _transferMode = TransferModeEnum.Reliable;
    bool _refuseNewConnections;

    /// <summary>Why the connection failed or ended, for the UI.</summary>
    public string LastError { get; private set; } = "";

    /// <summary>Client: the host's SteamID.</summary>
    public ulong HostSteamId => _hostLink?.Remote ?? 0;

    public SteamMultiplayerPeer() { } // Godot needs a parameterless constructor

    SteamMultiplayerPeer(ISteamSockets sockets, bool server)
    {
        _sockets = sockets;
        _server = server;
    }

    /// <summary>
    /// Hosts a session: listens for P2P connections. Check <c>GetConnectionStatus()</c>.
    /// Without <paramref name="listen"/> nobody can connect from outside; only in-process
    /// loopback connections arrive (for tests on one machine).
    /// </summary>
    public static SteamMultiplayerPeer CreateHost(ISteamSockets sockets, bool listen = true)
    {
        var peer = new SteamMultiplayerPeer(sockets, server: true);
        if (listen && !sockets.Listen())
        {
            peer.LastError = "Couldn't open a Steam listen socket.";
            return peer;
        }
        peer._uniqueId = 1;
        peer._status = ConnectionStatus.Connected;
        return peer;
    }

    /// <summary>
    /// Joins a session. With a host SteamID it starts connecting now; without one it waits in
    /// "connecting" until <see cref="ConnectTo"/> (for example, once a lobby says who hosts).
    /// </summary>
    public static SteamMultiplayerPeer CreateClient(ISteamSockets sockets, ulong host = 0)
    {
        var peer = new SteamMultiplayerPeer(sockets, server: false);
        peer._uniqueId = NewId(peer);
        peer._status = ConnectionStatus.Connecting;
        if (host != 0) peer.ConnectTo(host);
        return peer;
    }

    /// <summary>Client: connects to the host's SteamID.</summary>
    public bool ConnectTo(ulong host)
    {
        if (_server || _hostLink != null || _status != ConnectionStatus.Connecting) return false;
        uint connection = _sockets.Connect(host);
        if (connection == 0)
        {
            Fail("Couldn't start a Steam connection to the host.");
            return false;
        }
        _hostLink = new Link { Connection = connection, Remote = host, PeerId = 1 };
        _links[connection] = _hostLink;
        return true;
    }

    /// <summary>Gives up (for example, the lobby couldn't be joined). Godot then reports a failed connection.</summary>
    public void Fail(string reason)
    {
        LastError = reason;
        CloseAll(linger: false);
    }

    /// <summary>Round-trip time to a peer in ms, or -1.</summary>
    public int GetPing(int peer) => _peers.TryGetValue(peer, out Link link) ? _sockets.Ping(link.Connection) : -1;

    // ---- MultiplayerPeer ------------------------------------------------------------------

    public override void _Poll()
    {
        if (_sockets == null) return;

        foreach (int peer in _pendingDisconnects) EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, (long)peer);
        _pendingDisconnects.Clear();

        _events.Clear();
        _sockets.Poll(_events);
        foreach (SocketEvent e in _events) OnSocketEvent(e);

        _scratch.Clear();
        _scratch.AddRange(_links.Values);
        foreach (Link link in _scratch)
        {
            while (!link.Closed)
            {
                _received.Clear();
                if (_sockets.Receive(link.Connection, _received, 64) == 0) break;
                foreach (byte[] message in _received)
                {
                    if (link.Closed) break;
                    OnMessage(link, message);
                }
            }
        }
    }

    public override Error _PutPacketScript(byte[] buffer)
    {
        if (_status != ConnectionStatus.Connected) return Error.Unconfigured;
        if (buffer.Length > MaxMessageSize - DataHeader) return Error.InvalidParameter;

        if (_targetPeer > 0)
        {
            if (!_peers.TryGetValue(_targetPeer, out Link link)) return Error.InvalidParameter;
            return SendData(link, buffer) ? Error.Ok : Error.Failed;
        }
        foreach (Link link in _peers.Values)
            if (link.PeerId != -_targetPeer) SendData(link, buffer);
        return Error.Ok;
    }

    public override byte[] _GetPacketScript() => _incoming.Count > 0 ? _incoming.Dequeue().Data : Array.Empty<byte>();
    public override int _GetAvailablePacketCount() => _incoming.Count;
    public override int _GetMaxPacketSize() => MaxMessageSize - DataHeader;
    public override int _GetPacketPeer() => _incoming.Count > 0 ? _incoming.Peek().Peer : 0;
    public override int _GetPacketChannel() => _incoming.Count > 0 ? _incoming.Peek().Channel : 0;
    public override TransferModeEnum _GetPacketMode() => _incoming.Count > 0 ? _incoming.Peek().Mode : TransferModeEnum.Reliable;
    public override void _SetTransferChannel(int channel) => _transferChannel = channel;
    public override int _GetTransferChannel() => _transferChannel;
    public override void _SetTransferMode(TransferModeEnum mode) => _transferMode = mode;
    public override TransferModeEnum _GetTransferMode() => _transferMode;
    public override void _SetTargetPeer(int peer) => _targetPeer = peer;
    public override int _GetUniqueId() => _uniqueId;
    public override bool _IsServer() => _server;
    public override bool _IsServerRelaySupported() => true;
    public override ConnectionStatus _GetConnectionStatus() => _status;
    public override void _SetRefuseNewConnections(bool enable) => _refuseNewConnections = enable;
    public override bool _IsRefusingNewConnections() => _refuseNewConnections;

    public override void _Close() => CloseAll(linger: true);

    public override void _DisconnectPeer(int peer, bool force)
    {
        if (!_peers.TryGetValue(peer, out Link link)) return;
        _sockets.Close(link.Connection, 0, "Disconnected by the host", linger: !force);
        Forget(link);
        if (!force) _pendingDisconnects.Add(peer); // reported on the next poll, as ENet does
        if (!_server) _status = ConnectionStatus.Disconnected;
    }

    // ---- Steam side -----------------------------------------------------------------------

    void OnSocketEvent(SocketEvent e)
    {
        switch (e.State)
        {
            case SocketState.Connecting when e.Incoming:
                if (_links.ContainsKey(e.Connection)) return;
                if (!_server || _refuseNewConnections || _status != ConnectionStatus.Connected || !_sockets.Accept(e.Connection))
                {
                    _sockets.Close(e.Connection, 0, "Not accepting connections", linger: false);
                    return;
                }
                _links[e.Connection] = new Link { Connection = e.Connection, Remote = e.Remote };
                break;

            case SocketState.Connected:
                if (!_server && _links.TryGetValue(e.Connection, out Link host) && host == _hostLink && !host.HelloSent)
                {
                    host.HelloSent = true;
                    SendControl(host, MsgHello, _uniqueId);
                }
                break;

            case SocketState.ClosedByPeer:
            case SocketState.ProblemDetectedLocally:
                if (!_links.TryGetValue(e.Connection, out Link link)) return;
                _sockets.Close(e.Connection, 0, "Closed", linger: false);
                Drop(link, string.IsNullOrEmpty(e.Reason) ? "The connection closed." : e.Reason);
                break;
        }
    }

    void OnMessage(Link link, byte[] message)
    {
        if (message.Length == 0) return;
        switch (message[0])
        {
            case MsgHello when _server && !link.Admitted && message.Length >= 5:
            {
                int id = BitConverter.ToInt32(message, 1);
                while (id <= 1 || _peers.ContainsKey(id)) id = NewId(this); // a clash gets a fresh ID
                link.PeerId = id;
                link.Admitted = true;
                _peers[id] = link;
                SendControl(link, MsgWelcome, id);
                EmitSignal(MultiplayerPeer.SignalName.PeerConnected, (long)id);
                break;
            }

            case MsgWelcome when !_server && link == _hostLink && !link.Admitted && message.Length >= 5:
                _uniqueId = BitConverter.ToInt32(message, 1);
                link.Admitted = true;
                _peers[1] = link;
                _status = ConnectionStatus.Connected;
                EmitSignal(MultiplayerPeer.SignalName.PeerConnected, 1L);
                break;

            case MsgData when link.Admitted && message.Length >= DataHeader:
            {
                int channel = message[1];
                var mode = (TransferModeEnum)message[2];
                ushort sequence = (ushort)(message[3] | message[4] << 8);
                if (mode == TransferModeEnum.UnreliableOrdered)
                {
                    if (link.ReceiveSequence.TryGetValue(channel, out ushort last) && (short)(sequence - last) <= 0)
                        return; // older than one we already delivered
                    link.ReceiveSequence[channel] = sequence;
                }
                var payload = new byte[message.Length - DataHeader];
                Buffer.BlockCopy(message, DataHeader, payload, 0, payload.Length);
                _incoming.Enqueue(new Packet(link.PeerId, channel, mode, payload));
                break;
            }
        }
    }

    bool SendData(Link link, byte[] payload)
    {
        ushort sequence = 0;
        if (_transferMode == TransferModeEnum.UnreliableOrdered)
        {
            link.SendSequence.TryGetValue(_transferChannel, out sequence);
            link.SendSequence[_transferChannel] = ++sequence;
        }
        var message = new byte[payload.Length + DataHeader];
        message[0] = MsgData;
        message[1] = (byte)_transferChannel;
        message[2] = (byte)_transferMode;
        message[3] = (byte)sequence;
        message[4] = (byte)(sequence >> 8);
        Buffer.BlockCopy(payload, 0, message, DataHeader, payload.Length);
        return _sockets.Send(link.Connection, message, FlagsFor(_transferMode));
    }

    /// <summary>The Steam send flags for a Godot transfer mode. Nagle is off: Godot already batches.</summary>
    public static SteamSend FlagsFor(TransferModeEnum mode) =>
        (mode == TransferModeEnum.Reliable ? SteamSend.Reliable : SteamSend.Unreliable) | SteamSend.NoNagle;

    void SendControl(Link link, byte type, int value)
    {
        var message = new byte[5];
        message[0] = type;
        BitConverter.TryWriteBytes(message.AsSpan(1), value);
        _sockets.Send(link.Connection, message, SteamSend.Reliable | SteamSend.NoNagle);
    }

    /// <summary>A connection closed under us: tell Godot the peer left.</summary>
    void Drop(Link link, string reason)
    {
        bool wasAdmitted = link.Admitted;
        Forget(link);
        if (wasAdmitted) EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, (long)link.PeerId);
        if (!_server && link == _hostLink)
        {
            _hostLink = null;
            LastError = reason;
            _status = ConnectionStatus.Disconnected;
        }
    }

    void Forget(Link link)
    {
        link.Closed = true;
        _links.Remove(link.Connection);
        if (link.Admitted) _peers.Remove(link.PeerId);
    }

    void CloseAll(bool linger)
    {
        if (_sockets != null)
        {
            foreach (Link link in _links.Values)
            {
                link.Closed = true;
                _sockets.Close(link.Connection, 0, "Closed", linger);
            }
            if (_server) _sockets.StopListening();
        }
        _links.Clear();
        _peers.Clear();
        _incoming.Clear();
        _pendingDisconnects.Clear();
        _hostLink = null;
        _status = ConnectionStatus.Disconnected;
    }

    static int NewId(MultiplayerPeer peer)
    {
        int id;
        do id = (int)(peer.GenerateUniqueId() & 0x7fffffff);
        while (id <= 1);
        return id;
    }
}
