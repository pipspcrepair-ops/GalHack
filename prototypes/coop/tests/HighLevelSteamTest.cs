using System;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Net;
using Godot;

namespace Coop;

/// <summary>
/// Godot's high-level multiplayer over <see cref="SteamMultiplayerPeer"/>, in one process: two
/// branches of the scene tree, each with its own SceneMultiplayer, play host and client. RPCs,
/// a MultiplayerSpawner and a MultiplayerSynchronizer with a zone filter all work exactly as
/// they do over ENet. NetTest runs it over the fake socket layer; the opt-in Steam smoke test
/// runs it over real Steam sockets (a loopback pair on this machine).
/// </summary>
public static class HighLevelSteamTest
{
    public static async Task<int> Run(Node owner, string over, Func<MultiplayerPeer> makeHost, Func<MultiplayerPeer> makeClient)
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

        SceneTree tree = owner.GetTree();
        int clientZone = 1, clientId = 0;
        Func<int, int> zoneOfPeer = peer => peer == clientId ? clientZone : 0;
        string label = $"high-level over the Steam peer ({over})";

        (Node root, MultiplayerSpawner spawner, Node items, HlProbe probe) Branch(string name)
        {
            var root = new Node { Name = name };
            var items = new Node { Name = "Items" };
            var spawner = new MultiplayerSpawner { Name = "Spawner", SpawnPath = new NodePath("../Items") };
            var probe = new HlProbe { Name = "Probe" };
            root.AddChild(items);
            root.AddChild(spawner);
            root.AddChild(probe);
            spawner.SpawnFunction = Callable.From<Variant, Node>(data =>
                HlItem.Create(data.AsInt32(), spawner.Multiplayer.IsServer() ? zoneOfPeer : null));
            tree.Root.AddChild(root);
            return (root, spawner, items, probe);
        }

        var host = Branch("SteamHost");
        var client = Branch("SteamClient");
        var hostMp = new SceneMultiplayer { ServerRelay = false };
        var clientMp = new SceneMultiplayer { ServerRelay = false };
        tree.SetMultiplayer(hostMp, host.root.GetPath());
        tree.SetMultiplayer(clientMp, client.root.GetPath());
        bool connected = false, hostSawLeave = false;
        clientMp.ConnectedToServer += () => connected = true;
        hostMp.PeerDisconnected += _ => hostSawLeave = true;
        hostMp.MultiplayerPeer = makeHost();
        clientMp.MultiplayerPeer = makeClient();

        string step = "connecting";
        try
        {
            await Until(owner, () => connected, 5);
            clientId = clientMp.GetUniqueId();
            Check(connected && hostMp.GetPeers().Contains(clientId), $"{label}: SceneMultiplayer connects (client is peer {clientId})");

            step = "spawning and syncing";
            var item = (HlItem)host.spawner.Spawn(1);
            item.Value = 42;
            await Until(owner, () => client.items.GetChildCount() == 1 && client.items.GetChild<HlItem>(0).Value == 42, 3);
            item.Value = 77;
            await Until(owner, () => client.items.GetChildCount() == 1 && client.items.GetChild<HlItem>(0).Value == 77, 3);
            Check(true, $"{label}: MultiplayerSpawner spawns on the client, MultiplayerSynchronizer streams updates");

            step = "sending RPCs";
            client.probe.RpcId(1, HlProbe.MethodName.Hit, 5);
            host.probe.RpcId(clientId, HlProbe.MethodName.Hit, 6);
            await Until(owner, () => host.probe.Received.Count == 1 && client.probe.Received.Count == 1, 3);
            Check(host.probe.Received.SequenceEqual(new[] { (clientId, 5) }) && client.probe.Received.SequenceEqual(new[] { (1, 6) }),
                $"{label}: RPCs both ways, with the right sender");

            step = "changing zone";
            clientZone = 2;
            await Until(owner, () => client.items.GetChildCount() == 0, 3);
            item.Value = 99;
            clientZone = 1;
            await Until(owner, () => client.items.GetChildCount() == 1 && client.items.GetChild<HlItem>(0).Value == 99, 3);
            Check(true, $"{label}: a zone visibility filter despawns the entity when the client leaves its zone and respawns it, current, on return");

            step = "leaving";
            clientMp.MultiplayerPeer.Close();
            await Until(owner, () => hostSawLeave, 5);
            Check(true, $"{label}: the host's SceneMultiplayer sees the client leave");
        }
        catch (TimeoutException e)
        {
            Check(false, $"{label}: {e.Message} while {step}");
        }
        finally
        {
            hostMp.MultiplayerPeer.Close();
            hostMp.MultiplayerPeer = new OfflineMultiplayerPeer();
            clientMp.MultiplayerPeer = new OfflineMultiplayerPeer();
            host.root.QueueFree();
            client.root.QueueFree();
        }
        return failures;
    }

    public static async Task Until(Node owner, Func<bool> done, double seconds)
    {
        double end = NetClock.RealNow + seconds;
        while (!done())
        {
            if (NetClock.RealNow > end) throw new TimeoutException("timed out");
            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
