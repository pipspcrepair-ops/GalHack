using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;

namespace GalHack.Net;

/// <summary>
/// The real <see cref="ISteamSockets"/>: Steam networking sockets through Steamworks.NET. P2P
/// connections go to a SteamID through Steam's relays (Steam Datagram Relay), so no ports are
/// opened and nobody's IP address is shared.
///
/// Steam reports connection changes through one global callback; each endpoint picks out its
/// own connections. Requires <see cref="SteamworksApi.TryInit"/> first.
/// </summary>
public sealed class SteamworksSockets : ISteamSockets, IDisposable
{
    static readonly List<SteamworksSockets> Endpoints = new();
    static Callback<SteamNetConnectionStatusChangedCallback_t> _statusChanged;

    readonly HashSet<uint> _connections = new();
    readonly HashSet<uint> _pairs = new();
    readonly Dictionary<uint, SocketState> _states = new();
    readonly List<SocketEvent> _events = new();
    readonly IntPtr[] _messages = new IntPtr[64];
    HSteamListenSocket _listen = HSteamListenSocket.Invalid;

    public SteamworksSockets()
    {
        if (!SteamworksApi.Initialized) throw new InvalidOperationException("Start Steam first (SteamworksApi.TryInit).");
        _statusChanged ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
        LocalId = SteamworksApi.LocalSteamId;
        Endpoints.Add(this);
    }

    public ulong LocalId { get; }

    public void Dispose()
    {
        foreach (uint c in _connections) SteamNetworkingSockets.CloseConnection(new HSteamNetConnection(c), 0, "Shutting down", false);
        _connections.Clear();
        StopListening();
        Endpoints.Remove(this);
    }

    public bool Listen()
    {
        if (_listen != HSteamListenSocket.Invalid) return true;
        _listen = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
        return _listen != HSteamListenSocket.Invalid;
    }

    public void StopListening()
    {
        if (_listen == HSteamListenSocket.Invalid) return;
        SteamNetworkingSockets.CloseListenSocket(_listen);
        _listen = HSteamListenSocket.Invalid;
    }

    public uint Connect(ulong remote)
    {
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID64(remote);
        HSteamNetConnection connection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
        if (connection == HSteamNetConnection.Invalid) return 0;
        _connections.Add(connection.m_HSteamNetConnection);
        return connection.m_HSteamNetConnection;
    }

    /// <summary>
    /// For tests on one machine: connects to <paramref name="host"/> (another endpoint in this
    /// process) with a real Steam socket pair over the local network loopback. Both ends see the
    /// same events a P2P connection would give.
    /// </summary>
    public uint ConnectLoopback(SteamworksSockets host)
    {
        var a = new SteamNetworkingIdentity();
        a.SetSteamID64(LocalId);
        var b = a;
        if (!SteamNetworkingSockets.CreateSocketPair(out HSteamNetConnection mine, out HSteamNetConnection theirs, true, ref a, ref b))
            return 0;
        uint client = mine.m_HSteamNetConnection, server = theirs.m_HSteamNetConnection;
        _connections.Add(client);
        _pairs.Add(client);
        host._connections.Add(server);
        host._pairs.Add(server);
        host.Report(server, SocketState.Connecting, incoming: true, LocalId, "");
        host.Report(server, SocketState.Connected, incoming: false, LocalId, "");
        Report(client, SocketState.Connecting, incoming: false, host.LocalId, "");
        Report(client, SocketState.Connected, incoming: false, host.LocalId, "");
        return client;
    }

    public bool Accept(uint connection)
    {
        if (_pairs.Contains(connection)) return true; // a socket pair is born connected
        return SteamNetworkingSockets.AcceptConnection(new HSteamNetConnection(connection)) == EResult.k_EResultOK;
    }

    public void Close(uint connection, int reason, string debug, bool linger)
    {
        SteamNetworkingSockets.CloseConnection(new HSteamNetConnection(connection), reason, debug, linger);
        _connections.Remove(connection);
        _pairs.Remove(connection);
        _states.Remove(connection);
    }

    public bool Send(uint connection, byte[] data, SteamSend flags)
    {
        GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            EResult result = SteamNetworkingSockets.SendMessageToConnection(
                new HSteamNetConnection(connection), pin.AddrOfPinnedObject(), (uint)data.Length, (int)flags, out _);
            return result == EResult.k_EResultOK;
        }
        finally
        {
            pin.Free();
        }
    }

    public int Receive(uint connection, List<byte[]> into, int max)
    {
        int count = SteamNetworkingSockets.ReceiveMessagesOnConnection(
            new HSteamNetConnection(connection), _messages, Math.Min(max, _messages.Length));
        for (int i = 0; i < count; i++)
        {
            SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(_messages[i]);
            var data = new byte[message.m_cbSize];
            Marshal.Copy(message.m_pData, data, 0, message.m_cbSize);
            into.Add(data);
            SteamNetworkingMessage_t.Release(_messages[i]);
        }
        return Math.Max(count, 0);
    }

    public void Poll(List<SocketEvent> events)
    {
        SteamworksApi.RunCallbacks();
        events.AddRange(_events);
        _events.Clear();
    }

    public int Ping(uint connection)
    {
        var status = new SteamNetConnectionRealTimeStatus_t();
        var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
        EResult result = SteamNetworkingSockets.GetConnectionRealTimeStatus(new HSteamNetConnection(connection), ref status, 0, ref lanes);
        return result == EResult.k_EResultOK ? status.m_nPing : -1;
    }

    static void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
    {
        uint connection = data.m_hConn.m_HSteamNetConnection;
        var state = (int)data.m_info.m_eState is >= 0 and <= 5 ? (SocketState)(int)data.m_info.m_eState : SocketState.None;
        foreach (SteamworksSockets endpoint in Endpoints)
        {
            bool incoming = endpoint._listen != HSteamListenSocket.Invalid
                && data.m_info.m_hListenSocket == endpoint._listen
                && state == SocketState.Connecting
                && !endpoint._connections.Contains(connection);
            if (incoming) endpoint._connections.Add(connection);
            if (!endpoint._connections.Contains(connection)) continue;
            endpoint.Report(connection, state, incoming, data.m_info.m_identityRemote.GetSteamID64(), data.m_info.m_szEndDebug);
        }
    }

    void Report(uint connection, SocketState state, bool incoming, ulong remote, string reason)
    {
        if (_states.TryGetValue(connection, out SocketState old) && old == state) return;
        _states[connection] = state;
        _events.Add(new SocketEvent(connection, state, incoming, remote, reason ?? ""));
    }
}
