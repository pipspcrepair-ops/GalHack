using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Net;
using Godot;
using Steamworks;
using Mode = Godot.MultiplayerPeer.TransferModeEnum;

namespace Coop;

/// <summary>
/// OPT-IN real Steam smoke test, for one account on one machine. It uses the Steam client that
/// is already running and logged in, as app 480 (Spacewar); it never starts or logs in to
/// Steam, never contacts friends, never changes the profile or online status, and never
/// makes a public (or even friends-only) lobby. Run it by hand:
///   godot --headless --path prototypes/coop res://tests/SteamSmokeTest.tscn
///
/// Checks: SteamAPI init; relay network access and ping readiness; a PRIVATE lobby (create,
/// set and read metadata, leave); SteamMultiplayerPeer over real Steam sockets as a loopback
/// pair inside this process (packets, channels, ordering, and Godot's high-level multiplayer);
/// and a clean SteamAPI shutdown. Skips (exit 0) if Steam isn't running or logged in.
/// </summary>
public partial class SteamSmokeTest : Node
{
    const double Watchdog = 120;
    int _failures;
    bool _skipped, _done;
    readonly double _started = NetClock.Now;

    public override void _Ready()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Engine.MaxFps = 60;
        _ = Run();
    }

    public override void _Process(double delta)
    {
        if (_done || NetClock.Now - _started < Watchdog) return;
        Fail($"watchdog: still running after {Watchdog} s");
        End();
    }

    async Task Run()
    {
        GD.Print("== Steam smoke test (opt-in): app 480, the Steam account logged in on this machine, nothing sent to anyone ==");
        try
        {
            await Body();
        }
        catch (Exception e)
        {
            Fail($"stopped: {e.GetType().Name}: {e.Message}");
        }
        End();
    }

    void End()
    {
        if (_done) return;
        _done = true;
        if (SteamworksApi.Initialized)
        {
            SteamworksApi.Shutdown();
            Check(!SteamworksApi.Initialized, "SteamAPI shut down cleanly (the account no longer shows as in game)");
        }
        GD.Print(_skipped ? "SKIPPED" : _failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    async Task Body()
    {
        if (System.Diagnostics.Process.GetProcessesByName("steam").Length == 0)
        {
            Skip("Steam isn't running. Start Steam and log in yourself, then run this again.");
            return;
        }
        string library = SteamworksApi.FindLibrary();
        if (library == null)
        {
            Skip("steam_api64.dll is missing. Run tools/steam-coop-test.ps1 -FetchSteamApi first.");
            return;
        }
        string appIdFile = Path.Combine(Directory.GetCurrentDirectory(), "steam_appid.txt");
        Check(File.Exists(appIdFile) && File.ReadAllText(appIdFile).Trim() == "480", $"steam_appid.txt says 480 ({appIdFile})");

        if (!SteamworksApi.TryInit(out string error))
        {
            if (error.Contains("isn't running")) Skip(error);
            else Fail($"SteamAPI init: {error}");
            return;
        }
        Pass($"SteamAPI initialised (steam_api64.dll from {library})");
        if (!SteamUser.BLoggedOn())
        {
            Skip("Steam is running but not logged on.");
            return;
        }
        uint app = SteamUtils.GetAppID().m_AppId;
        Check(app == SteamworksApi.DevAppId, $"running as app {app} (Spacewar, Valve's shared test app)");

        await RelayNetwork();
        await PrivateLobby();
        await LoopbackPeer();

        using var host = new SteamworksSockets();
        using var client = new SteamworksSockets { LoopbackTo = host };
        _failures += await HighLevelSteamTest.Run(this, "real Steam sockets, loopback pair",
            () => SteamMultiplayerPeer.CreateHost(host, listen: false),
            () => SteamMultiplayerPeer.CreateClient(client, host.LocalId));
    }

    /// <summary>Steam Datagram Relay: access to the relay network, and our ping location measured.</summary>
    async Task RelayNetwork()
    {
        double start = NetClock.Now;
        ESteamNetworkingAvailability status;
        SteamNetworkingUtils.InitRelayNetworkAccess();
        do
        {
            await Frame();
            SteamworksApi.RunCallbacks();
            status = SteamNetworkingUtils.GetRelayNetworkStatus(out _);
        }
        while (status != ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current
               && status > ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Unknown
               && NetClock.Now - start < 30);
        double seconds = NetClock.Now - start;
        Check(status == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current,
            $"relay network access ready in {seconds:F1} s ({status})");

        float age = -1;
        while (NetClock.Now - start < 30)
        {
            age = SteamNetworkingUtils.GetLocalPingLocation(out _);
            if (age >= 0) break;
            await Frame();
            SteamworksApi.RunCallbacks();
        }
        int pops = SteamNetworkingUtils.GetPOPCount();
        Check(age >= 0 && pops > 0, $"ping location measured: P2P connections through the relays are ready ({pops} relay sites known)");
    }

    /// <summary>A PRIVATE lobby (invisible to friends and searches): create, tag, read back, leave.</summary>
    async Task PrivateLobby()
    {
        LobbyCreated_t? created = null;
        var call = CallResult<LobbyCreated_t>.Create((result, ioFailure) =>
            created = ioFailure ? new LobbyCreated_t { m_eResult = EResult.k_EResultIOFailure } : result);
        call.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, 1));
        double start = NetClock.Now;
        while (created == null && NetClock.Now - start < 20)
        {
            await Frame();
            SteamworksApi.RunCallbacks();
        }
        if (created is not { m_eResult: EResult.k_EResultOK } ok)
        {
            Fail($"private lobby: not created ({created?.m_eResult.ToString() ?? "no answer in 20 s"})");
            return;
        }
        var lobby = new CSteamID(ok.m_ulSteamIDLobby);
        try
        {
            bool set = SteamMatchmaking.SetLobbyData(lobby, SteamTransport.LobbyKey, "coop-smoke")
                       && SteamMatchmaking.SetLobbyData(lobby, "purpose", "automated smoke test");
            string tag = SteamMatchmaking.GetLobbyData(lobby, SteamTransport.LobbyKey);
            string purpose = SteamMatchmaking.GetLobbyData(lobby, "purpose");
            Check(set && tag == "coop-smoke" && purpose == "automated smoke test"
                  && SteamMatchmaking.GetNumLobbyMembers(lobby) == 1 && SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID(),
                $"private lobby created in {NetClock.Now - start:F1} s; metadata set and read back; we are its only member and its owner");
        }
        finally
        {
            SteamMatchmaking.LeaveLobby(lobby);
            Pass("left the private lobby (an empty lobby is deleted by Steam)");
        }
    }

    /// <summary>The raw peer over a real Steam socket pair: connect, channels and modes, ordering, ping.</summary>
    async Task LoopbackPeer()
    {
        using var hostSockets = new SteamworksSockets();
        using var clientSockets = new SteamworksSockets { LoopbackTo = hostSockets };
        var host = SteamMultiplayerPeer.CreateHost(hostSockets, listen: false);
        var client = SteamMultiplayerPeer.CreateClient(clientSockets, hostSockets.LocalId);
        var hostEvents = new List<long>();
        host.PeerConnected += id => hostEvents.Add(id);
        host.PeerDisconnected += id => hostEvents.Add(-id);

        double start = NetClock.Now;
        while (client.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected && NetClock.Now - start < 10)
        {
            await Frame();
            host.Poll();
            client.Poll();
        }
        int id = client.GetUniqueId();
        Check(client.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected && hostEvents.SequenceEqual(new long[] { id }),
            $"SteamMultiplayerPeer over a real Steam socket pair: connected in {(NetClock.Now - start) * 1000:F0} ms (peer {id})");

        for (int i = 0; i < 300; i++)
        {
            Send(client, 3, Mode.Reliable, i);
            Send(client, 5, Mode.UnreliableOrdered, i);
            Send(client, 7, Mode.Unreliable, i);
        }
        var reliable = new List<int>();
        var ordered = new List<int>();
        int unreliable = 0;
        bool mapped = true;
        start = NetClock.Now;
        while (reliable.Count < 300 && NetClock.Now - start < 5)
        {
            await Frame();
            client.Poll();
            host.Poll();
            while (host.GetAvailablePacketCount() > 0)
            {
                int channel = host.GetPacketChannel(), from = host.GetPacketPeer();
                Mode mode = host.GetPacketMode();
                int value = BitConverter.ToInt32(host.GetPacket());
                mapped &= from == id && (channel, mode) is (3, Mode.Reliable) or (5, Mode.UnreliableOrdered) or (7, Mode.Unreliable);
                if (mode == Mode.Reliable) reliable.Add(value);
                else if (mode == Mode.UnreliableOrdered) ordered.Add(value);
                else unreliable++;
            }
        }
        Check(reliable.SequenceEqual(Enumerable.Range(0, 300)), "real Steam sockets: 300 reliable packets arrive complete and in order");
        Check(ordered.Count > 0 && ordered.Zip(ordered.Skip(1), (a, b) => b > a).All(x => x),
            $"real Steam sockets: unreliable-ordered packets never arrive out of order ({ordered.Count} of 300 delivered)");
        Check(mapped && unreliable > 0, $"real Steam sockets: channels 3/5/7 and their transfer modes map through ({unreliable} of 300 plain unreliable delivered)");
        int ping = client.GetPing(1);
        Pass($"real Steam sockets: loopback round trip reported as {ping} ms");

        client.Close();
        start = NetClock.Now;
        while (!hostEvents.Contains(-id) && NetClock.Now - start < 5)
        {
            await Frame();
            host.Poll();
        }
        Check(hostEvents.Contains(-id), "real Steam sockets: the host sees the client close");
        host.Close();
    }

    static void Send(MultiplayerPeer peer, int channel, Mode mode, int value)
    {
        peer.SetTargetPeer(1);
        peer.TransferChannel = channel;
        peer.TransferMode = mode;
        peer.PutPacket(BitConverter.GetBytes(value));
    }

    async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    void Check(bool ok, string what)
    {
        if (ok) Pass(what);
        else Fail(what);
    }

    static void Pass(string what) => GD.Print($"PASS  {what}");

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }

    void Skip(string why)
    {
        _skipped = true;
        GD.Print($"SKIP  {why}");
    }
}
