using System;
using System.Collections.Generic;
using Godot;
using Steamworks;

namespace GalHack.Net;

/// <summary>
/// Steam as a transport: the host opens a friends-only lobby and a P2P listen socket; friends
/// join the lobby (from an invite, or from the list of friends who are hosting) and connect to
/// its owner through Steam's relays. No matchmaking, no public lobbies, no server browser.
///
/// Every lobby is tagged, because app 480 (Spacewar) is shared by every developer testing on it.
/// </summary>
public sealed class SteamTransport : ITransport
{
    public const string LobbyKey = "galhack";
    public const string LobbyValue = "coop-1";

    SteamworksSockets _sockets;
    SteamMultiplayerPeer _peer;
    CallResult<LobbyCreated_t> _created;
    CallResult<LobbyEnter_t> _entered;
    string _describe = "Steam";

    /// <summary>The lobby we host or joined, or 0.</summary>
    public ulong Lobby { get; private set; }

    public string Name => "Steam";
    public string Describe => _describe;

    SteamTransport() { }

    /// <summary>Starts Steam and makes a transport, or returns null with a reason.</summary>
    public static SteamTransport Start(out string error) =>
        SteamworksApi.TryInit(out error) ? new SteamTransport() : null;

    public MultiplayerPeer CreateHost(int maxClients, out string error)
    {
        _sockets = new SteamworksSockets();
        _peer = SteamMultiplayerPeer.CreateHost(_sockets);
        if (_peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
        {
            error = _peer.LastError;
            return null;
        }
        _created = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
        _created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxClients + 1));
        _describe = "Steam, creating a friends-only lobby...";
        error = null;
        return _peer;
    }

    void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
    {
        if (ioFailure || result.m_eResult != EResult.k_EResultOK)
        {
            _describe = $"Steam, no lobby ({result.m_eResult}); friends can't join";
            return;
        }
        Lobby = result.m_ulSteamIDLobby;
        var lobby = new CSteamID(Lobby);
        SteamMatchmaking.SetLobbyData(lobby, LobbyKey, LobbyValue);
        SteamMatchmaking.SetLobbyData(lobby, "host", _sockets.LocalId.ToString());
        SteamFriends.SetRichPresence("connect", $"+connect_lobby {Lobby}"); // "Join game" in the friends list
        _describe = "Steam, friends-only lobby (I to invite)";
    }

    /// <summary>Opens the Steam overlay's invite dialog for our lobby.</summary>
    public void InviteFriends()
    {
        if (Lobby != 0) SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(Lobby));
    }

    /// <summary>Joins "lobby:ID" (an invite or a friend's lobby), or a host's SteamID directly.</summary>
    public MultiplayerPeer CreateClient(string address, out string error)
    {
        _sockets = new SteamworksSockets();
        _peer = SteamMultiplayerPeer.CreateClient(_sockets);
        if (address.StartsWith("lobby:") && ulong.TryParse(address["lobby:".Length..], out ulong lobby))
        {
            _entered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            _entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobby)));
            _describe = "Steam, joining a friend's lobby...";
        }
        else if (ulong.TryParse(address, out ulong host))
        {
            _peer.ConnectTo(host);
            _describe = "Steam, direct to the host";
        }
        else
        {
            error = $"Not a Steam lobby or SteamID: {address}";
            return null;
        }
        error = null;
        return _peer;
    }

    void OnLobbyEntered(LobbyEnter_t result, bool ioFailure)
    {
        if (ioFailure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            _peer.Fail("Couldn't join the friend's lobby (it may have closed).");
            return;
        }
        Lobby = result.m_ulSteamIDLobby;
        var lobby = new CSteamID(Lobby);
        if (SteamMatchmaking.GetLobbyData(lobby, LobbyKey) != LobbyValue)
        {
            SteamMatchmaking.LeaveLobby(lobby);
            Lobby = 0;
            _peer.Fail("That lobby isn't a GalHack co-op session.");
            return;
        }
        CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
        _describe = "Steam, friend's lobby";
        _peer.ConnectTo(owner.m_SteamID);
    }

    /// <summary>Friends who are in a GalHack lobby right now: name and lobby ID.</summary>
    public static List<(string name, ulong lobby)> FriendLobbies()
    {
        var found = new List<(string, ulong)>();
        if (!SteamworksApi.Initialized) return found;
        AppId_t us = SteamUtils.GetAppID();
        int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        for (int i = 0; i < count; i++)
        {
            CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            if (!SteamFriends.GetFriendGamePlayed(friend, out FriendGameInfo_t game)) continue;
            if (game.m_gameID.AppID() != us || !game.m_steamIDLobby.IsValid()) continue;
            found.Add((SteamFriends.GetFriendPersonaName(friend), game.m_steamIDLobby.m_SteamID));
        }
        return found;
    }

    public void Poll() => SteamworksApi.RunCallbacks();

    public void Shutdown()
    {
        if (!SteamworksApi.Initialized) return;
        if (Lobby != 0) SteamMatchmaking.LeaveLobby(new CSteamID(Lobby));
        Lobby = 0;
        SteamFriends.ClearRichPresence();
        _sockets?.Dispose();
        _sockets = null;
    }

    public bool TryPopWireStats(out long bytesSent, out long bytesReceived, out long packetsSent, out long packetsReceived)
    {
        bytesSent = bytesReceived = packetsSent = packetsReceived = 0;
        return false;
    }
}
