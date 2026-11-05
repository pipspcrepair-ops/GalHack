using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Net;
using Godot;
using Mode = Godot.MultiplayerPeer.TransferModeEnum;

namespace Coop;

/// <summary>
/// The network simulator over the fake Steam layer, on a clock the test steps itself (1 ms at a
/// time), so the result is exact and the same however busy the machine is: latency is half the
/// round trip each way plus jitter, unreliable packets are lost at about the set rate, and
/// reliable ones are never lost and never reordered, however much jitter there is.
/// </summary>
public static class ConditionerTest
{
    public static int Run()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            if (ok) GD.Print($"PASS  {what}");
            else
            {
                failures++;
                GD.PrintErr($"FAIL  {what}");
            }
        }

        double now = 0; // the test's own clock, in seconds
        var net = new FakeSteamNetwork();
        var host = SteamMultiplayerPeer.CreateHost(net.AddEndpoint(1));
        var conditions = new NetConditions { LatencyMs = 80, JitterMs = 10, LossPercent = 5, Seed = 11 };
        var client = new ConditionedPeer(SteamMultiplayerPeer.CreateClient(net.AddEndpoint(2), 1), conditions) { Clock = () => now };
        for (int i = 0; i < 10 && client.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected; i++)
        {
            client.Poll();
            host.Poll();
        }

        const int Reliable = 300, Unreliable = 1000;
        for (int i = 0; i < Unreliable; i++)
        {
            Send(client, 2, Mode.Unreliable, i);
            if (i < Reliable) Send(client, 0, Mode.Reliable, i);
        }

        var reliable = new List<int>();
        int unreliable = 0;
        var delays = new List<double>();
        for (int step = 0; step <= 1500; step++)
        {
            now = step / 1000.0;
            client.Poll();
            host.Poll();
            while (host.GetAvailablePacketCount() > 0)
            {
                Mode mode = host.GetPacketMode();
                int value = BitConverter.ToInt32(host.GetPacket());
                if (mode == Mode.Reliable) reliable.Add(value);
                else
                {
                    unreliable++;
                    delays.Add(now * 1000); // everything was sent at 0
                }
            }
        }
        double lossPercent = 100.0 * (Unreliable - unreliable) / Unreliable;
        double earliest = delays.DefaultIfEmpty(0).Min(), latest = delays.DefaultIfEmpty(0).Max();
        double median = delays.Count > 0 ? delays.OrderBy(d => d).ElementAt(delays.Count / 2) : 0;

        Check(reliable.SequenceEqual(Enumerable.Range(0, Reliable)),
            $"network simulator: {Reliable} reliable packets all arrive, in order, through 10 ms jitter and 5% loss");
        Check(lossPercent > 2.5 && lossPercent < 8,
            $"network simulator: {lossPercent:F1}% of {Unreliable} unreliable packets lost (set to 5%)");
        Check(earliest >= 40 && latest <= 51 && median > 43 && median < 47,
            $"network simulator: one-way delay {earliest:F0}-{latest:F0} ms, median {median:F0} ms (80 ms round trip and 10 ms jitter: 40-50 ms each way)");
        Check(client.BytesOut == (Reliable + Unreliable) * 4 && client.PacketsOut == Reliable + Unreliable,
            $"network simulator: counts traffic ({client.PacketsOut} packets, {client.BytesOut} bytes out)");
        client.Close();
        host.Close();

        // Ordering: reliable packets stay in order on their channel, however the jitter falls.
        now = 0;
        var host2 = SteamMultiplayerPeer.CreateHost(net.AddEndpoint(3));
        var client2 = new ConditionedPeer(SteamMultiplayerPeer.CreateClient(net.AddEndpoint(4), 3),
            new NetConditions { LatencyMs = 80, JitterMs = 40, LossPercent = 10, Seed = 5 }) { Clock = () => now };
        for (int i = 0; i < 10; i++)
        {
            client2.Poll();
            host2.Poll();
        }
        var order = new List<int>();
        for (int step = 0; step <= 2000; step++)
        {
            now = step / 1000.0;
            if (step < 500) Send(client2, 0, Mode.Reliable, step); // one a millisecond for half a second
            client2.Poll();
            host2.Poll();
            while (host2.GetAvailablePacketCount() > 0) order.Add(BitConverter.ToInt32(host2.GetPacket()));
        }
        Check(order.SequenceEqual(Enumerable.Range(0, 500)),
            $"network simulator: 500 reliable packets stay in order through 40 ms jitter and 10% loss (resends) ({order.Count} arrived)");
        client2.Close();
        host2.Close();
        return failures;
    }

    static void Send(MultiplayerPeer peer, int channel, Mode mode, int value)
    {
        peer.SetTargetPeer(1);
        peer.TransferChannel = channel;
        peer.TransferMode = mode;
        peer.PutPacket(BitConverter.GetBytes(value));
    }
}
