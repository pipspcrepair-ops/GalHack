using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Godot;
using Steamworks;

namespace GalHack.Net;

/// <summary>
/// Starting and stopping the Steam API (through Steamworks.NET). Nothing touches Steam until
/// <see cref="TryInit"/> is called, and automated tests set <see cref="Forbidden"/> so it can't be.
///
/// During development the app ID comes from steam_appid.txt next to the project (480, Valve's
/// "Spacewar" test app). steam_api64.dll is Valve's redistributable; it is looked for next to
/// the project, then next to the executable.
/// </summary>
public static class SteamworksApi
{
    /// <summary>Spacewar, Valve's shared test app.</summary>
    public const uint DevAppId = 480;

    /// <summary>Set by automated tests: any attempt to start Steam then throws.</summary>
    public static bool Forbidden;

    public static bool Initialized { get; private set; }

    /// <summary>A friend's invite was accepted in the Steam overlay (or "Join game" clicked): the lobby to join.</summary>
    public static event Action<ulong> LobbyJoinRequested;

    static bool _resolverSet;
    static ulong _lastCallbackFrame = ulong.MaxValue;
    static Callback<GameLobbyJoinRequested_t> _joinRequested;

    /// <summary>Starts Steam. Needs the Steam client running and logged in; never starts it.</summary>
    public static bool TryInit(out string error)
    {
        if (Forbidden) throw new InvalidOperationException("Steam is off limits in automated tests.");
        error = null;
        if (Initialized) return true;
        if (FindLibrary() == null)
        {
            error = "steam_api64.dll is missing. Run tools/steam-coop-test.ps1 -FetchSteamApi once.";
            return false;
        }
        InstallResolver();
        try
        {
            ESteamAPIInitResult result = SteamAPI.InitEx(out string message);
            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                error = result == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient
                    ? "Steam isn't running (or isn't logged in). Start Steam and try again."
                    : $"Steam didn't start: {message} ({result}).";
                return false;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            error = $"Couldn't load steam_api64.dll: {e.Message}";
            return false;
        }
        Initialized = true;
        _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => LobbyJoinRequested?.Invoke(r.m_steamIDLobby.m_SteamID));
        SteamNetworkingUtils.InitRelayNetworkAccess(); // warm up the relay network before anyone connects
        return true;
    }

    /// <summary>Runs Steam's callbacks, at most once a frame.</summary>
    public static void RunCallbacks()
    {
        if (!Initialized) return;
        ulong frame = Engine.GetProcessFrames();
        if (frame == _lastCallbackFrame) return;
        _lastCallbackFrame = frame;
        SteamAPI.RunCallbacks();
    }

    /// <summary>Stops Steam, so the account stops showing as in game.</summary>
    public static void Shutdown()
    {
        if (!Initialized) return;
        _joinRequested?.Dispose();
        _joinRequested = null;
        SteamAPI.Shutdown();
        Initialized = false;
    }

    public static ulong LocalSteamId => Initialized ? SteamUser.GetSteamID().m_SteamID : 0;
    public static string PersonaName => Initialized ? SteamFriends.GetPersonaName() : "";

    /// <summary>Where steam_api64.dll was found, or null.</summary>
    public static string FindLibrary()
    {
        foreach (string path in Candidates("steam_api64.dll"))
            if (File.Exists(path)) return path;
        return null;
    }

    static IEnumerable<string> Candidates(string file)
    {
        yield return Path.Combine(ProjectSettings.GlobalizePath("res://"), file);
        yield return Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", file);
    }

    /// <summary>Points Steamworks.NET's [DllImport("steam_api64")] at the copy we found.</summary>
    static void InstallResolver()
    {
        if (_resolverSet) return;
        _resolverSet = true;
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly, (name, assembly, searchPath) =>
            {
                if (!name.StartsWith("steam_api", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
                string file = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";
                foreach (string path in Candidates(file))
                    if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle)) return handle;
                return IntPtr.Zero;
            });
        }
        catch (InvalidOperationException)
        {
            // Already set (an editor hot reload); the earlier one still works.
        }
    }
}
