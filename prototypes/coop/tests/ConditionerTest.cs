using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Net;
using Godot;
using Mode = Godot.MultiplayerPeer.TransferModeEnum;

namespace Coop;

/// <summary>
/// The network simulator, in real time, over the fake Steam layer: latency is about half the
/// round trip each way, unreliable packets are lost at about the set rate, and reliable ones
/// are never lost and never reordered, however much jitter there is.
/// </summary>
public static class ConditionerTest
{
    public static async Task<int> Run(NetTest test)
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

        var net = new FakeSteamNetwork();
        var host = SteamMultiplayerPeer.CreateHost(net.AddEndpoint(1));
        var conditions = new NetConditions { LatencyMs = 80, JitterMs = 10, LossPercent = 5, Seed = 11 };
        var client = new ConditionedPeer(SteamMultiplayerPeer.CreateClient(net.AddEndpoint(2), 1), conditions);
        for (int i = 0; i < 10 && client.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected; i++)
        {
            client.Poll();
            host.Poll();
        }

        await test.Frames(1);
        const int Reliable = 300, Unreliable = 1000;
        var sentAt = new Dictionary<int, double>();

        for (int i = 0; i < Unreliable; i++)
        {
            Send(client, 2, Mode.Unreliable, i);
            if (i < Reliable) Send(client, 0, Mode.Reliable, i);
            sentAt[i] = NetClock.Now;
        }

        var reliable = new List<int>();
        int unreliable = 0;
        var delays = new List<double>();
        // Poll in a busy loop rather than once a frame (or with Sleep, ~15 ms on Windows), so the measured delay is the simulator's.
        double end = NetClock.Now + 1.5;
        while (NetClock.Now < end)
        {
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
                    delays.Add((NetClock.Now - sentAt[value]) * 1000);
                }
            }
        }
        double lossPercent = 100.0 * (Unreliable - unreliable) / Unreliable;
        double median = delays.Count > 0 ? delays.OrderBy(d => d).ElementAt(delays.Count / 2) : 0;

        Check(reliable.SequenceEqual(Enumerable.Range(0, Reliable)),
            $"network simulator: {Reliable} reliable packets all arrive in order through 10 ms jitter and 5% loss");
        Check(lossPercent > 2.5 && lossPercent < 8,
            $"network simulator: {lossPercent:F1}% of {Unreliable} unreliable packets lost (set to 5%)");
        double earliest = delays.DefaultIfEmpty(0).Min();
        Check(earliest >= 39.5 && median < 70,
            $"network simulator: one-way delay {earliest:F0} ms at the earliest, median {median:F0} ms (80 ms round trip and 10 ms jitter: 40-50 ms each way, plus polling on a busy machine)");
        Check(client.BytesOut == (Reliable + Unreliable) * 4 && client.PacketsOut == Reliable + Unreliable,
            $"network simulator: counts traffic ({client.PacketsOut} packets, {client.BytesOut} bytes out)");
        client.Close();
        host.Close();
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
