using System;
using System.Collections.Generic;

namespace GalHack.Net;

/// <summary>Send flags, with the same values as Steam's k_nSteamNetworkingSend_* constants.</summary>
[Flags]
public enum SteamSend
{
    Unreliable = 0,
    NoNagle = 1,
    NoDelay = 4,
    Reliable = 8,
}

/// <summary>A connection's state, mirroring ESteamNetworkingConnectionState.</summary>
public enum SocketState
{
    None = 0,
    Connecting = 1,
    FindingRoute = 2,
    Connected = 3,
    ClosedByPeer = 4,
    ProblemDetectedLocally = 5,
}

/// <summary>A connection changed state. <c>Incoming</c> is true for a new connection to our listen socket.</summary>
public readonly record struct SocketEvent(uint Connection, SocketState State, bool Incoming, ulong Remote, string Reason);

/// <summary>
/// The slice of Steam networking sockets (ISteamNetworkingSockets) that the multiplayer peer
/// needs: P2P connections to a SteamID through Steam's relays, reliable and unreliable
/// messages, and connection state changes.
///
/// <see cref="SteamworksSockets"/> is the real one. <see cref="FakeSteamNetwork"/> is an
/// in-process stand-in, so the peer is tested without ever touching Steam.
/// </summary>
public interface ISteamSockets
{
    /// <summary>Our own SteamID.</summary>
    ulong LocalId { get; }

    /// <summary>Starts accepting P2P connections (virtual port 0).</summary>
    bool Listen();

    void StopListening();

    /// <summary>Starts connecting to a SteamID. Returns the connection handle, or 0.</summary>
    uint Connect(ulong remote);

    bool Accept(uint connection);

    /// <summary>Closes a connection. With linger, reliable messages already queued are still delivered.</summary>
    void Close(uint connection, int reason, string debug, bool linger);

    /// <summary>Queues one message. False if Steam refused it (for example, the connection is gone).</summary>
    bool Send(uint connection, byte[] data, SteamSend flags);

    /// <summary>Appends up to <paramref name="max"/> received messages to <paramref name="into"/>.</summary>
    int Receive(uint connection, List<byte[]> into, int max);

    /// <summary>Pumps callbacks and appends this endpoint's connection state changes.</summary>
    void Poll(List<SocketEvent> events);

    /// <summary>Round-trip time in ms, or -1 if unknown.</summary>
    int Ping(uint connection);
}
