using System;
using System.Collections.Generic;

namespace GalHack.Net;

/// <summary>
/// An in-process stand-in for Steam networking sockets, for tests: any number of endpoints
/// (pretend SteamIDs) that connect, accept, send and close like the real thing, with no Steam
/// anywhere. Tests can drop or hold back unreliable messages to check ordering and loss
/// handling, and every send is logged with its flags.
/// </summary>
public sealed class FakeSteamNetwork
{
    public readonly record struct SentMessage(ulong From, ulong To, SteamSend Flags, int Size);

    /// <summary>Every message sent, in order.</summary>
    public readonly List<SentMessage> Sent = new();

    /// <summary>When set, unreliable messages are held back until <see cref="ReleaseHeld"/>.</summary>
    public bool HoldUnreliable;

    /// <summary>When it returns true, that unreliable message is lost.</summary>
    public Func<byte[], bool> DropUnreliable;

    sealed class Connection
    {
        public uint Handle;
        public FakeSteamSockets Owner;
        public Connection Other;
        public SocketState State;
        public readonly Queue<byte[]> Inbox = new();
    }

    readonly Dictionary<ulong, FakeSteamSockets> _endpoints = new();
    readonly Dictionary<uint, Connection> _connections = new();
    readonly List<(Connection to, byte[] data)> _held = new();
    uint _nextHandle = 100;

    public FakeSteamSockets AddEndpoint(ulong steamId)
    {
        var endpoint = new FakeSteamSockets(this, steamId);
        _endpoints[steamId] = endpoint;
        return endpoint;
    }

    /// <summary>Delivers held unreliable messages, optionally in reverse order (as if reordered in flight).</summary>
    public void ReleaseHeld(bool reverse)
    {
        if (reverse) _held.Reverse();
        foreach (var (to, data) in _held)
            if (to.State == SocketState.Connected) to.Inbox.Enqueue(data);
        _held.Clear();
    }

    /// <summary>Simulates the network dropping a connection: both ends see a local problem.</summary>
    public void Sever(uint handle)
    {
        if (!_connections.TryGetValue(handle, out Connection c)) return;
        foreach (Connection end in new[] { c, c.Other })
        {
            if (end == null || end.State is SocketState.None or SocketState.ProblemDetectedLocally) continue;
            end.State = SocketState.ProblemDetectedLocally;
            end.Owner.Raise(new SocketEvent(end.Handle, SocketState.ProblemDetectedLocally, false, end.Other?.Owner.LocalId ?? 0, "Timed out"));
        }
    }

    internal uint Connect(FakeSteamSockets from, ulong remote)
    {
        var local = new Connection { Handle = _nextHandle++, Owner = from, State = SocketState.Connecting };
        _connections[local.Handle] = local;
        from.Raise(new SocketEvent(local.Handle, SocketState.Connecting, false, remote, ""));

        if (!_endpoints.TryGetValue(remote, out FakeSteamSockets target) || !target.Listening)
        {
            local.State = SocketState.ProblemDetectedLocally;
            from.Raise(new SocketEvent(local.Handle, SocketState.ProblemDetectedLocally, false, remote, "No one is listening at that SteamID"));
            return local.Handle;
        }
        var other = new Connection { Handle = _nextHandle++, Owner = target, State = SocketState.Connecting, Other = local };
        local.Other = other;
        _connections[other.Handle] = other;
        target.Raise(new SocketEvent(other.Handle, SocketState.Connecting, true, from.LocalId, ""));
        return local.Handle;
    }

    internal bool Accept(FakeSteamSockets by, uint handle)
    {
        if (!_connections.TryGetValue(handle, out Connection c) || c.Owner != by || c.State != SocketState.Connecting || c.Other == null)
            return false;
        foreach (Connection end in new[] { c, c.Other })
        {
            end.State = SocketState.Connected;
            end.Owner.Raise(new SocketEvent(end.Handle, SocketState.Connected, false, end.Other.Owner.LocalId, ""));
        }
        return true;
    }

    internal void Close(FakeSteamSockets by, uint handle, string debug)
    {
        if (!_connections.TryGetValue(handle, out Connection c) || c.Owner != by) return;
        c.State = SocketState.None;
        _connections.Remove(handle);
        Connection other = c.Other;
        if (other != null && other.State is SocketState.Connecting or SocketState.Connected)
        {
            other.State = SocketState.ClosedByPeer;
            other.Owner.Raise(new SocketEvent(other.Handle, SocketState.ClosedByPeer, false, by.LocalId, debug));
        }
    }

    internal bool Send(FakeSteamSockets by, uint handle, byte[] data, SteamSend flags)
    {
        if (!_connections.TryGetValue(handle, out Connection c) || c.Owner != by || c.State != SocketState.Connected) return false;
        if (data.Length > SteamMultiplayerPeer.MaxMessageSize) return false;
        Sent.Add(new SentMessage(by.LocalId, c.Other.Owner.LocalId, flags, data.Length));
        var copy = (byte[])data.Clone();
        bool reliable = (flags & SteamSend.Reliable) != 0;
        if (!reliable && DropUnreliable != null && DropUnreliable(copy)) return true;
        if (!reliable && HoldUnreliable) _held.Add((c.Other, copy));
        else c.Other.Inbox.Enqueue(copy);
        return true;
    }

    internal int Receive(FakeSteamSockets by, uint handle, List<byte[]> into, int max)
    {
        if (!_connections.TryGetValue(handle, out Connection c) || c.Owner != by) return 0;
        int n = 0;
        while (n < max && c.Inbox.Count > 0)
        {
            into.Add(c.Inbox.Dequeue());
            n++;
        }
        return n;
    }
}

/// <summary>One pretend Steam user on a <see cref="FakeSteamNetwork"/>.</summary>
public sealed class FakeSteamSockets : ISteamSockets
{
    readonly FakeSteamNetwork _network;
    readonly List<SocketEvent> _events = new();

    internal FakeSteamSockets(FakeSteamNetwork network, ulong id)
    {
        _network = network;
        LocalId = id;
    }

    public ulong LocalId { get; }
    public bool Listening { get; private set; }

    internal void Raise(SocketEvent e) => _events.Add(e);

    public bool Listen() => Listening = true;
    public void StopListening() => Listening = false;
    public uint Connect(ulong remote) => _network.Connect(this, remote);
    public bool Accept(uint connection) => _network.Accept(this, connection);
    public void Close(uint connection, int reason, string debug, bool linger) => _network.Close(this, connection, debug);
    public bool Send(uint connection, byte[] data, SteamSend flags) => _network.Send(this, connection, data, flags);
    public int Receive(uint connection, List<byte[]> into, int max) => _network.Receive(this, connection, into, max);
    public int Ping(uint connection) => 0;

    public void Poll(List<SocketEvent> events)
    {
        events.AddRange(_events);
        _events.Clear();
    }
}
