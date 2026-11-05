using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Net;
using Godot;
using Mode = Godot.MultiplayerPeer.TransferModeEnum;

namespace Coop;

/// <summary>
/// Synchronous unit tests: the Steam multiplayer peer against the fake socket layer, and the
/// interpolation buffer. No Steam anywhere.
/// </summary>
public static class UnitTests
{
    static int _failures;

    const ulong HostId = 76561190000000001, AId = 76561190000000002, BId = 76561190000000003,
        CId = 76561190000000004, DId = 76561190000000005, EId = 76561190000000006, FId = 76561190000000007;

    public static int Run()
    {
        _failures = 0;
        SteamPeer();
        Buffer();
        return _failures;
    }

    // ---- SteamMultiplayerPeer against FakeSteamNetwork ------------------------------------------------

    static void SteamPeer()
    {
        var net = new FakeSteamNetwork();
        var host = SteamMultiplayerPeer.CreateHost(net.AddEndpoint(HostId));
        var hostEvents = new List<long>();
        host.PeerConnected += id => hostEvents.Add(id);
        host.PeerDisconnected += id => hostEvents.Add(-id);

        var a = SteamMultiplayerPeer.CreateClient(net.AddEndpoint(AId), HostId);
        var b = SteamMultiplayerPeer.CreateClient(net.AddEndpoint(BId), HostId);
        var aEvents = new List<long>();
        a.PeerConnected += id => aEvents.Add(id);
        a.PeerDisconnected += id => aEvents.Add(-id);
        Check(host.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected && host.GetUniqueId() == 1
              && a.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connecting,
            "steam peer: the host is peer 1 and connected at once; a client starts out connecting");

        Pump(host, a, b);
        int aId = a.GetUniqueId(), bId = b.GetUniqueId();
        Check(a.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected && b.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected
              && aId > 1 && bId > 1 && aId != bId
              && hostEvents.OrderBy(x => x).SequenceEqual(new long[] { aId, bId }.OrderBy(x => x)) && aEvents.SequenceEqual(new long[] { 1 }),
            $"steam peer connect events: the host gets peer_connected for both clients ({aId}, {bId}); each client gets peer_connected(1)");

        // Channels and transfer modes survive the trip, and map to the right Steam send flags.
        int sent = net.Sent.Count;
        Send(a, 1, 3, Mode.Reliable, 1, 2, 3);
        Send(a, 1, 5, Mode.Unreliable, 4);
        Send(a, 1, 7, Mode.UnreliableOrdered, 5, 6);
        Pump(host);
        var got = Drain(host);
        Check(got.Count == 3
              && got[0].peer == aId && got[0].channel == 3 && got[0].mode == Mode.Reliable && got[0].data.SequenceEqual(new byte[] { 1, 2, 3 })
              && got[1].peer == aId && got[1].channel == 5 && got[1].mode == Mode.Unreliable && got[1].data.SequenceEqual(new byte[] { 4 })
              && got[2].peer == aId && got[2].channel == 7 && got[2].mode == Mode.UnreliableOrdered && got[2].data.SequenceEqual(new byte[] { 5, 6 }),
            "steam peer channel mapping: sender, channel (3, 5, 7), transfer mode and payload arrive intact");
        SteamSend[] flags = net.Sent.Skip(sent).Select(s => s.Flags).ToArray();
        Check(flags.Length == 3 && flags[0] == (SteamSend.Reliable | SteamSend.NoNagle)
              && flags[1] == SteamSend.NoNagle && flags[2] == SteamSend.NoNagle,
            "steam peer send flags: reliable → Steam reliable, unreliable and unreliable-ordered → Steam unreliable, Nagle off");

        // Ordering.
        for (int i = 0; i < 200; i++) Send(a, 1, 0, Mode.Reliable, BitConverter.GetBytes(i));
        Pump(host);
        Check(Drain(host).Select(p => BitConverter.ToInt32(p.data)).SequenceEqual(Enumerable.Range(0, 200)),
            "steam peer packet ordering: 200 reliable packets arrive complete and in order");

        net.HoldUnreliable = true;
        for (int i = 0; i < 50; i++) Send(a, 1, 2, Mode.UnreliableOrdered, (byte)i);
        net.HoldUnreliable = false;
        net.ReleaseHeld(reverse: true);
        Pump(host);
        got = Drain(host);
        Check(got.Count == 1 && got[0].data[0] == 49,
            $"steam peer packet ordering: 50 unreliable-ordered packets reordered in flight; only the newest is delivered, never an older one after it (got {got.Count})");
        for (int i = 50; i < 60; i++) Send(a, 1, 2, Mode.UnreliableOrdered, (byte)i);
        Send(a, 1, 9, Mode.UnreliableOrdered, 200); // another channel keeps its own sequence
        Pump(host);
        got = Drain(host);
        Check(got.Where(p => p.channel == 2).Select(p => (int)p.data[0]).SequenceEqual(Enumerable.Range(50, 10)) && got.Count(p => p.channel == 9) == 1,
            "steam peer packet ordering: in-order unreliable-ordered packets all arrive; each channel has its own sequence");

        net.HoldUnreliable = true;
        for (int i = 0; i < 20; i++) Send(a, 1, 4, Mode.Unreliable, (byte)i);
        net.HoldUnreliable = false;
        net.ReleaseHeld(reverse: true);
        Pump(host);
        got = Drain(host);
        Check(got.Select(p => (int)p.data[0]).SequenceEqual(Enumerable.Range(0, 20).Reverse()),
            "steam peer: plain unreliable packets are delivered even when reordered (all 20, in arrival order)");

        int n = 0;
        net.DropUnreliable = _ => ++n % 4 == 0;
        for (int i = 0; i < 100; i++)
        {
            Send(a, 1, 4, Mode.Unreliable, 1);
            Send(a, 1, 0, Mode.Reliable, 2);
        }
        net.DropUnreliable = null;
        Pump(host);
        got = Drain(host);
        Check(got.Count(p => p.mode == Mode.Unreliable) == 75 && got.Count(p => p.mode == Mode.Reliable) == 100,
            "steam peer: lost unreliable packets just vanish (75 of 100 arrive); reliable ones are unaffected (100 of 100)");

        // The host can target one client, everyone, or everyone but one.
        Send(host, aId, 1, Mode.Reliable, 7);
        Send(host, 0, 1, Mode.Reliable, 8);
        Send(host, -aId, 1, Mode.Reliable, 9);
        Pump(a, b);
        var toA = Drain(a);
        var toB = Drain(b);
        Check(toA.Select(p => (int)p.data[0]).SequenceEqual(new[] { 7, 8 }) && toB.Select(p => (int)p.data[0]).SequenceEqual(new[] { 8, 9 })
              && toA.All(p => p.peer == 1) && toB.All(p => p.peer == 1),
            "steam peer targeting: to one peer, to all, to all but one; clients see packets from peer 1");
        host.SetTargetPeer(123456);
        Check(host.PutPacket(new byte[] { 1 }) == Error.InvalidParameter, "steam peer: a packet to an unknown peer is refused");

        var big = new byte[200_000];
        new Random(5).NextBytes(big);
        Send(a, 1, 0, Mode.Reliable, big);
        Pump(host);
        got = Drain(host);
        Check(got.Count == 1 && got[0].data.SequenceEqual(big), "steam peer: a 200 KB reliable packet arrives intact");

        // Leaving, kicking, timing out, the host closing.
        b.Close();
        Pump(host, b);
        Check(hostEvents.Contains(-bId) && b.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected,
            "steam peer disconnect: a client closing → the host gets peer_disconnected for it");
        host.DisconnectPeer(aId);
        Pump(host, a);
        Check(a.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected && aEvents.Contains(-1) && hostEvents.Contains(-aId),
            "steam peer disconnect: the host dropping a client → the client sees the host go, the host reports the peer gone");

        var c = SteamMultiplayerPeer.CreateClient(net.AddEndpoint(CId), HostId);
        Pump(host, c);
        int cId = c.GetUniqueId();
        net.SeverAll(CId);
        Pump(host, c);
        Check(c.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected && hostEvents.Contains(-cId) && c.LastError.Length > 0,
            $"steam peer disconnect: a dropped connection is noticed on both ends (\"{c.LastError}\")");

        var d = SteamMultiplayerPeer.CreateClient(net.AddEndpoint(DId), HostId);
        var dEvents = new List<long>();
        d.PeerDisconnected += id => dEvents.Add(id);
        Pump(host, d);
        bool dConnected = d.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
        host.Close();
        Pump(d);
        Check(dConnected && d.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected && dEvents.SequenceEqual(new long[] { 1 })
              && host.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected,
            "steam peer disconnect: the host closing → its clients get peer_disconnected(1) and drop to disconnected");

        var e = SteamMultiplayerPeer.CreateClient(net.AddEndpoint(EId), 999);
        Pump(e);
        Check(e.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected && e.LastError.Length > 0,
            "steam peer: connecting to a SteamID that isn't hosting fails cleanly");

        var host2 = SteamMultiplayerPeer.CreateHost(net.AddEndpoint(HostId + 100));
        host2.RefuseNewConnections = true;
        var f = SteamMultiplayerPeer.CreateClient(net.AddEndpoint(FId), HostId + 100);
        Pump(host2, f);
        Check(f.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected, "steam peer: a host refusing new connections turns clients away");
    }

    static void Send(MultiplayerPeer peer, int target, int channel, Mode mode, params byte[] data)
    {
        peer.SetTargetPeer(target);
        peer.TransferChannel = channel;
        peer.TransferMode = mode;
        peer.PutPacket(data);
    }

    static void Pump(params MultiplayerPeer[] peers)
    {
        for (int i = 0; i < 4; i++)
            foreach (MultiplayerPeer p in peers) p.Poll();
    }

    static List<(int peer, int channel, Mode mode, byte[] data)> Drain(MultiplayerPeer peer)
    {
        var list = new List<(int, int, Mode, byte[])>();
        while (peer.GetAvailablePacketCount() > 0)
        {
            int from = peer.GetPacketPeer(), channel = peer.GetPacketChannel();
            Mode mode = peer.GetPacketMode();
            list.Add((from, channel, mode, peer.GetPacket()));
        }
        return list;
    }

    // ---- SnapshotBuffer ---------------------------------------------------------------------------------

    /// <summary>
    /// A sender 1000 s ahead of us sends its own clock 30 times a second over 40 ms ± 10 ms with
    /// 5% loss. Drawn from the buffer, time must flow smoothly about 140 ms behind the sender.
    /// </summary>
    static void Buffer()
    {
        var buffer = new SnapshotBuffer<double>();
        var rng = new Random(3);
        var inFlight = new List<(double arrive, double sent)>();
        double nextSend = 0, lastRender = double.MinValue, worstError = 0, worstDelay = 0, minDelay = double.MaxValue;
        int backwards = 0, extrapolated = 0, samples = 0;
        for (double now = 0; now < 10; now += 1.0 / 60)
        {
            while (nextSend <= now)
            {
                if (rng.NextDouble() > 0.05) inFlight.Add((nextSend + 0.04 + rng.NextDouble() * 0.01, nextSend + 1000));
                nextSend += 1.0 / 30;
            }
            foreach (var p in inFlight.Where(p => p.arrive <= now).ToList())
            {
                buffer.Push(p.sent, p.sent, now);
                inFlight.Remove(p);
            }
            if (now < 1 || !buffer.TrySample(now, out var s)) continue;
            samples++;
            double value = s.A + (s.B - s.A) * s.Alpha;
            worstError = Math.Max(worstError, Math.Abs(value - s.RenderTime) * (s.Extra > 0 ? 0 : 1));
            if (s.Extra > 0) extrapolated++;
            double delay = now + 1000 - s.RenderTime;
            worstDelay = Math.Max(worstDelay, delay);
            minDelay = Math.Min(minDelay, delay);
            if (s.RenderTime < lastRender) backwards++;
            lastRender = s.RenderTime;
        }
        Check(worstError < 1e-6 && backwards == 0 && extrapolated < samples / 50 && minDelay > 0.13 && worstDelay < 0.16,
            $"interpolation buffer: draws {minDelay * 1000:F0}-{worstDelay * 1000:F0} ms behind the sender through 40±10 ms latency and 5% loss, never runs backwards, ran past the newest snapshot on {extrapolated} of {samples} frames");

        // A gap: the sender goes quiet for 300 ms (a stall on a busy machine), then carries on.
        // Drawing glides on along the last motion for up to 100 ms, then its clock waits for the
        // data; when the data comes, drawing carries on from where it was (no jump, never
        // backwards) and creeps back to the usual delay.
        var gap = new SnapshotBuffer<double>();
        double next = 0, shownInGap = 0, lastShown = double.NaN, worstStep = 0, delayAtEnd = 0;
        bool reversed = false;
        for (double now = 0; now < 6; now += 1.0 / 60)
        {
            for (; next <= now - 0.04; next += 1.0 / 30)
                if (next <= 1.0 + 1e-9 || next >= 1.3 - 1e-9) gap.Push(next, next, now);
            if (!gap.TrySample(now, out var s)) continue;
            double shown = s.A + (s.B - s.A) * s.Alpha;
            if (now > 1.0 && now < 1.34) shownInGap = Math.Max(shownInGap, shown);
            if (!double.IsNaN(lastShown))
            {
                worstStep = Math.Max(worstStep, shown - lastShown);
                reversed |= shown < lastShown - 1e-9;
            }
            lastShown = shown;
            delayAtEnd = now - s.RenderTime;
        }
        Check(Math.Abs(shownInGap - (1.0 + SnapshotBuffer<double>.MaxExtrapolation)) < 1e-6 && worstStep <= 1.06 / 60 && !reversed && delayAtEnd < 0.16,
            $"interpolation buffer: through a 300 ms stall it glides {SnapshotBuffer<double>.MaxExtrapolation * 1000:F0} ms, waits, then carries on with no jump (largest step {worstStep * 60:F2} frames of time) and is back to {delayAtEnd * 1000:F0} ms behind");
    }

    static void Check(bool ok, string what)
    {
        if (ok) GD.Print($"PASS  {what}");
        else
        {
            _failures++;
            GD.PrintErr($"FAIL  {what}");
        }
    }
}
