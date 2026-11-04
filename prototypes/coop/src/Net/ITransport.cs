using Godot;

namespace GalHack.Net;

/// <summary>
/// How peers reach each other. A transport only makes Godot MultiplayerPeers; everything above
/// it (RPCs, synchronizers, spawners, the session) is the same whichever one is used.
/// </summary>
public interface ITransport
{
    /// <summary>"ENet" or "Steam".</summary>
    string Name { get; }

    /// <summary>A short line for the HUD: where you are hosting or what you joined.</summary>
    string Describe { get; }

    /// <summary>A server peer, or null with a reason.</summary>
    MultiplayerPeer CreateHost(int maxClients, out string error);

    /// <summary>A client peer for an address (ENet "ip[:port]", Steam "lobby:ID" or a SteamID), or null with a reason.</summary>
    MultiplayerPeer CreateClient(string address, out string error);

    /// <summary>Called every frame while a session is up (Steam runs its callbacks here).</summary>
    void Poll();

    /// <summary>The session is over: leave lobbies and let go of sockets.</summary>
    void Shutdown();

    /// <summary>Bytes and packets on the wire since the last call, if the transport can tell.</summary>
    bool TryPopWireStats(out long bytesSent, out long bytesReceived, out long packetsSent, out long packetsReceived);
}

/// <summary>Plain UDP through ENet: LAN, direct IP, loopback tests and the dedicated server.</summary>
public sealed class EnetTransport : ITransport
{
    public const int DefaultPort = 24680;

    public int Port = DefaultPort;

    /// <summary>"*" for every interface; tests use 127.0.0.1 so nothing is exposed.</summary>
    public string BindAddress = "*";

    ENetMultiplayerPeer _peer;
    string _describe = "ENet";

    public string Name => "ENet";
    public string Describe => _describe;

    public MultiplayerPeer CreateHost(int maxClients, out string error)
    {
        _peer = new ENetMultiplayerPeer();
        _peer.SetBindIP(BindAddress);
        Error result = _peer.CreateServer(Port, maxClients);
        if (result != Error.Ok)
        {
            error = $"Couldn't host on UDP port {Port} ({result}). Is something else using it?";
            _peer = null;
            return null;
        }
        _peer.PeerConnected += id => ShortenTimeout((int)id);
        _describe = $"ENet, port {Port}";
        error = null;
        return _peer;
    }

    public MultiplayerPeer CreateClient(string address, out string error)
    {
        string host = address.Trim();
        int port = Port;
        int colon = host.LastIndexOf(':');
        if (colon > 0 && int.TryParse(host[(colon + 1)..], out int parsed))
        {
            port = parsed;
            host = host[..colon];
        }
        if (host.Length == 0) host = "127.0.0.1";

        _peer = new ENetMultiplayerPeer();
        Error result = _peer.CreateClient(host, port);
        if (result != Error.Ok)
        {
            error = $"Couldn't connect to {host}:{port} ({result}).";
            _peer = null;
            return null;
        }
        ShortenTimeout(1);
        _describe = $"ENet, {host}:{port}";
        error = null;
        return _peer;
    }

    /// <summary>Notice a vanished peer in seconds rather than ENet's default of up to 30.</summary>
    void ShortenTimeout(int peer)
    {
        ENetPacketPeer packetPeer = _peer?.GetPeer(peer);
        packetPeer?.SetTimeout(32, 3000, 8000);
    }

    public void Poll() { }

    public void Shutdown() => _peer = null;

    public bool TryPopWireStats(out long bytesSent, out long bytesReceived, out long packetsSent, out long packetsReceived)
    {
        bytesSent = bytesReceived = packetsSent = packetsReceived = 0;
        ENetConnection host = _peer?.Host;
        if (host == null) return false;
        bytesSent = (long)host.PopStatistic(ENetConnection.HostStatistic.SentData);
        bytesReceived = (long)host.PopStatistic(ENetConnection.HostStatistic.ReceivedData);
        packetsSent = (long)host.PopStatistic(ENetConnection.HostStatistic.SentPackets);
        packetsReceived = (long)host.PopStatistic(ENetConnection.HostStatistic.ReceivedPackets);
        return true;
    }
}
