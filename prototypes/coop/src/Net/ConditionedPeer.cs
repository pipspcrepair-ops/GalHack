using System;
using System.Collections.Generic;
using Godot;

namespace GalHack.Net;

/// <summary>Bad-network settings for tests. Latency is the round trip, split evenly both ways.</summary>
public sealed class NetConditions
{
    public float LatencyMs = 80f;
    public float JitterMs = 10f;
    public float LossPercent = 1.5f;
    public int Seed = 1;

    /// <summary>Parses "latency,jitter,loss", for example "80,10,1.5".</summary>
    public static NetConditions Parse(string text)
    {
        string[] parts = text.Split(',');
        var c = new NetConditions();
        if (parts.Length > 0 && float.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out float l)) c.LatencyMs = l;
        if (parts.Length > 1 && float.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out float j)) c.JitterMs = j;
        if (parts.Length > 2 && float.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out float p)) c.LossPercent = p;
        return c;
    }

    public override string ToString() => $"{LatencyMs:0} ms round trip, {JitterMs:0} ms jitter, {LossPercent:0.#}% loss";
}

/// <summary>
/// Wraps any MultiplayerPeer (ENet, Steam) and, when given <see cref="NetConditions"/>, makes
/// the network worse: every packet in either direction is delayed by half the round trip plus
/// jitter, and unreliable packets are lost at random. A "lost" reliable packet is not dropped
/// (that would break reliability); it arrives one round trip later, as a resend would, and
/// holds back everything behind it on its channel. Ordered channels never reorder.
///
/// It also counts payload bytes and packets both ways, which is how tests measure bandwidth.
/// Without conditions it only counts.
/// </summary>
public partial class ConditionedPeer : MultiplayerPeerExtension
{
    enum Kind { Packet, Disconnect }

    sealed class Item
    {
        public double Due;
        public long Order;
        public Kind Kind;
        public int Peer, Channel;
        public TransferModeEnum Mode;
        public byte[] Data;
    }

    public MultiplayerPeer Inner { get; }
    public NetConditions Conditions { get; }

    // Payload traffic, after the simulated loss.
    public long BytesIn { get; private set; }
    public long BytesOut { get; private set; }
    public long PacketsIn { get; private set; }
    public long PacketsOut { get; private set; }
    public long PacketsDropped { get; private set; }

    readonly List<Item> _outgoing = new(), _incoming = new();
    readonly Queue<Item> _ready = new();
    readonly Dictionary<(int peer, int channel, bool incoming), double> _streamDue = new();
    readonly Random _random;
    long _order;
    int _targetPeer, _transferChannel;
    TransferModeEnum _transferMode = TransferModeEnum.Reliable;
    bool _wasConnected;

    public ConditionedPeer() { } // Godot needs a parameterless constructor

    public ConditionedPeer(MultiplayerPeer inner, NetConditions conditions)
    {
        Inner = inner;
        Conditions = conditions;
        _random = new Random(conditions?.Seed ?? 1);
        inner.PeerConnected += OnInnerConnected;
        inner.PeerDisconnected += OnInnerDisconnected;
    }

    static double Now => Time.GetTicksUsec() / 1_000_000.0;

    // Disconnects seen during the inner poll; queued once that poll's packets are in.
    readonly List<int> _justDisconnected = new();
    readonly HashSet<int> _gone = new();

    readonly HashSet<int> _innerGone = new();

    void OnInnerConnected(long id)
    {
        _gone.Remove((int)id);
        _innerGone.Remove((int)id);
        EmitSignal(MultiplayerPeer.SignalName.PeerConnected, id);
    }

    void OnInnerDisconnected(long id)
    {
        _outgoing.RemoveAll(i => i.Peer == id);
        _justDisconnected.Add((int)id);
        _innerGone.Add((int)id); // until Godot hears about it, quietly drop what it sends there
    }

    /// <summary>Anything still "in flight" from a peer arrives first, then its disconnect.</summary>
    void QueueDisconnects()
    {
        foreach (int id in _justDisconnected)
        {
            double due = Now + OneWay();
            foreach (Item i in _incoming)
                if (i.Peer == id) due = Math.Max(due, i.Due);
            Add(_incoming, new Item { Due = due, Kind = Kind.Disconnect, Peer = id });
        }
        _justDisconnected.Clear();
    }

    double OneWay()
    {
        if (Conditions == null) return 0;
        return (Conditions.LatencyMs * 0.5 + _random.NextDouble() * Conditions.JitterMs) / 1000.0;
    }

    bool Lost() => Conditions != null && _random.NextDouble() * 100.0 < Conditions.LossPercent;

    /// <summary>When a packet sent now arrives, or null if it is lost.</summary>
    double? Schedule(int peer, int channel, TransferModeEnum mode, bool incoming)
    {
        double due = Now + OneWay();
        if (mode == TransferModeEnum.Reliable)
        {
            // Each loss costs a resend, about one round trip.
            while (Lost()) due += Conditions.LatencyMs / 1000.0 + OneWay();
        }
        else if (Lost())
        {
            PacketsDropped++;
            return null;
        }
        if (mode != TransferModeEnum.Unreliable)
        {
            // Ordered streams never overtake themselves.
            var key = (peer, channel, incoming);
            if (_streamDue.TryGetValue(key, out double last) && due < last) due = last;
            _streamDue[key] = due;
        }
        return due;
    }

    void Add(List<Item> list, Item item)
    {
        item.Order = _order++;
        int at = list.Count;
        while (at > 0 && (list[at - 1].Due > item.Due || (list[at - 1].Due == item.Due && list[at - 1].Order > item.Order))) at--;
        list.Insert(at, item);
    }

    public override void _Poll()
    {
        if (Inner.GetConnectionStatus() != ConnectionStatus.Disconnected) Inner.Poll();
        double now = Now;

        while (Inner.GetAvailablePacketCount() > 0)
        {
            int peer = Inner.GetPacketPeer(), channel = Inner.GetPacketChannel();
            TransferModeEnum mode = Inner.GetPacketMode();
            byte[] data = Inner.GetPacket();
            double? due = Schedule(peer, channel, mode, incoming: true);
            if (due == null) continue;
            Add(_incoming, new Item { Due = due.Value, Kind = Kind.Packet, Peer = peer, Channel = channel, Mode = mode, Data = data });
        }
        QueueDisconnects();

        while (_outgoing.Count > 0 && _outgoing[0].Due <= now)
        {
            Item item = _outgoing[0];
            _outgoing.RemoveAt(0);
            if (item.Peer > 0 && _innerGone.Contains(item.Peer)) continue;
            Inner.TransferChannel = item.Channel;
            Inner.TransferMode = item.Mode;
            Inner.SetTargetPeer(item.Peer);
            Inner.PutPacket(item.Data);
        }

        while (_incoming.Count > 0 && _incoming[0].Due <= now)
        {
            Item item = _incoming[0];
            if (item.Kind == Kind.Disconnect)
            {
                // Let Godot read that peer's last packets before it hears the peer has gone.
                bool pending = false;
                foreach (Item r in _ready) pending |= r.Peer == item.Peer;
                if (pending) break;
                _incoming.RemoveAt(0);
                _gone.Add(item.Peer);
                EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, (long)item.Peer);
                continue;
            }
            _incoming.RemoveAt(0);
            if (_gone.Contains(item.Peer)) continue;
            BytesIn += item.Data.Length;
            PacketsIn++;
            _ready.Enqueue(item);
        }

        if (Inner.GetConnectionStatus() == ConnectionStatus.Connected) _wasConnected = true;
    }

    public override Error _PutPacketScript(byte[] buffer)
    {
        if (Inner.GetConnectionStatus() != ConnectionStatus.Connected) return Error.Unconfigured;
        if (_targetPeer > 0 && _innerGone.Contains(_targetPeer)) return Error.Ok;
        BytesOut += buffer.Length;
        PacketsOut++;
        if (Conditions == null)
        {
            Inner.TransferChannel = _transferChannel;
            Inner.TransferMode = _transferMode;
            Inner.SetTargetPeer(_targetPeer);
            return Inner.PutPacket(buffer);
        }
        double? due = Schedule(_targetPeer, _transferChannel, _transferMode, incoming: false);
        if (due != null)
            Add(_outgoing, new Item { Due = due.Value, Kind = Kind.Packet, Peer = _targetPeer, Channel = _transferChannel, Mode = _transferMode, Data = buffer });
        return Error.Ok;
    }

    public override byte[] _GetPacketScript() => _ready.Count > 0 ? _ready.Dequeue().Data : Array.Empty<byte>();
    public override int _GetAvailablePacketCount() => _ready.Count;
    public override int _GetMaxPacketSize() => Inner is SteamMultiplayerPeer ? SteamMultiplayerPeer.MaxMessageSize - 8 : 1 << 24;
    public override int _GetPacketPeer() => _ready.Count > 0 ? _ready.Peek().Peer : 0;
    public override int _GetPacketChannel() => _ready.Count > 0 ? _ready.Peek().Channel : 0;
    public override TransferModeEnum _GetPacketMode() => _ready.Count > 0 ? _ready.Peek().Mode : TransferModeEnum.Reliable;
    public override void _SetTransferChannel(int channel) => _transferChannel = channel;
    public override int _GetTransferChannel() => _transferChannel;
    public override void _SetTransferMode(TransferModeEnum mode) => _transferMode = mode;
    public override TransferModeEnum _GetTransferMode() => _transferMode;
    public override void _SetTargetPeer(int peer) => _targetPeer = peer;
    public override int _GetUniqueId() => Inner.GetUniqueId();
    public override bool _IsServer() => Inner.GetUniqueId() == 1;
    public override bool _IsServerRelaySupported() => Inner.IsServerRelaySupported();
    public override void _SetRefuseNewConnections(bool enable) => Inner.RefuseNewConnections = enable;
    public override bool _IsRefusingNewConnections() => Inner.RefuseNewConnections;

    /// <summary>Stays "connected" until everything already in flight has been delivered.</summary>
    public override ConnectionStatus _GetConnectionStatus()
    {
        ConnectionStatus status = Inner.GetConnectionStatus();
        if (status == ConnectionStatus.Disconnected && _wasConnected && (_incoming.Count > 0 || _ready.Count > 0))
            return ConnectionStatus.Connected;
        return status;
    }

    public override void _Close()
    {
        _outgoing.Clear();
        _incoming.Clear();
        _ready.Clear();
        _justDisconnected.Clear();
        _gone.Clear();
        _innerGone.Clear();
        _wasConnected = false;
        Inner.Close();
    }

    public override void _DisconnectPeer(int peer, bool force)
    {
        _outgoing.RemoveAll(i => i.Peer == peer);
        Inner.DisconnectPeer(peer, force);
    }

    /// <summary>Flushes anything still waiting to go out (used just before closing).</summary>
    public void FlushOutgoing()
    {
        foreach (Item item in _outgoing)
        {
            Inner.TransferChannel = item.Channel;
            Inner.TransferMode = item.Mode;
            Inner.SetTargetPeer(item.Peer);
            Inner.PutPacket(item.Data);
        }
        _outgoing.Clear();
    }
}
