using System.Collections.Generic;
using Godot;

namespace Coop;

/// <summary>Receives RPCs for <see cref="HighLevelSteamTest"/>.</summary>
public partial class HlProbe : Node
{
    public readonly List<(int from, int value)> Received = new();

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void Hit(int value) => Received.Add((Multiplayer.GetRemoteSenderId(), value));
}
