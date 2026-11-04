using GalHack.Net;
using Godot;

namespace Coop;

/// <summary>
/// The main scene. Shows the title screen, or starts straight away from the command line:
/// <code>
///   -- --host [--port=24680] [--name=Pip]       host over ENet (LAN, local testing)
///   -- --join=127.0.0.1[:24680] [--name=Ana]    join over ENet
///   -- --dedicated [--port=24680]               headless dedicated server, no local player
///   -- --steam-host                             host over Steam (friends-only lobby)
///   -- --steam-join=LOBBY_ID                     join a friend's Steam lobby
///   -- --sim=80,10,1.5                          simulate latency, jitter and loss (testing)
/// </code>
/// </summary>
public partial class Boot : Node
{
    CoopGame _game;
    TitleScreen _title;
    bool _quitting, _dedicated;

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless") Engine.MaxFps = 60; // a server needn't spin flat out
        GetTree().AutoAcceptQuit = false;
        SteamworksApi.LobbyJoinRequested += OnLobbyJoinRequested;

        Args args = Args.FromCommandLine();
        string name = args.Get("name", "Player");
        int port = args.GetInt("port", EnetTransport.DefaultPort);
        NetConditions sim = args.Has("sim") ? NetConditions.Parse(args.Get("sim", "80,10,1.5")) : null;
        var enet = new EnetTransport { Port = port, BindAddress = args.Get("bind", "*") };

        if (args.Has("dedicated"))
        {
            _dedicated = true;
            StartGame(new CoopGame.Options { Name = "server", Dedicated = true, Transport = enet });
        }
        else if (args.Has("host")) StartGame(new CoopGame.Options { Name = name, Host = true, Transport = enet, Conditions = sim });
        else if (args.Get("join") is { } address) StartGame(new CoopGame.Options { Name = name, Transport = enet, Address = address, Conditions = sim });
        else if (args.Has("steam-host")) StartSteam(name, host: true, 0);
        else if (ulong.TryParse(args.Get("steam-join"), out ulong lobby)) StartSteam(name, host: false, lobby);
        else ShowTitle("");
    }

    public override void _ExitTree()
    {
        SteamworksApi.LobbyJoinRequested -= OnLobbyJoinRequested;
        SteamworksApi.Shutdown();
    }

    void ShowTitle(string message)
    {
        _title = new TitleScreen { Name = "Title", Message = message };
        _title.HostSteam += name => StartSteam(name, host: true, 0);
        _title.JoinSteamLobby += (name, lobby) => StartSteam(name, host: false, lobby);
        _title.HostEnet += (name, port) => StartGame(new CoopGame.Options { Name = name, Host = true, Transport = new EnetTransport { Port = port } });
        _title.JoinEnet += (name, address) => StartGame(new CoopGame.Options { Name = name, Transport = new EnetTransport(), Address = address });
        AddChild(_title);
    }

    void StartSteam(string name, bool host, ulong lobby)
    {
        SteamTransport steam = SteamTransport.Start(out string error);
        if (steam == null)
        {
            if (_title == null) ShowTitle(error);
            else _title.SetStatus(error);
            return;
        }
        if (name == "Player" && SteamworksApi.PersonaName.Length > 0) name = SteamworksApi.PersonaName;
        StartGame(new CoopGame.Options { Name = name, Host = host, Transport = steam, Address = $"lobby:{lobby}" });
    }

    /// <summary>A Steam invite was accepted while the game was running.</summary>
    void OnLobbyJoinRequested(ulong lobby)
    {
        if (_game != null) return; // finish this session first
        StartSteam(SteamworksApi.PersonaName, host: false, lobby);
    }

    public void StartGame(CoopGame.Options options)
    {
        if (_game != null) return;
        if (_title != null)
        {
            _title.QueueFree();
            _title = null;
        }
        _game = CoopGame.Create(options);
        _game.Ended += OnGameEnded;
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, _game);
    }

    void OnGameEnded(string reason, bool graceful)
    {
        Node game = _game;
        _game = null;
        Callable.From(() =>
        {
            game.GetParent()?.RemoveChild(game);
            game.QueueFree();
            if (_quitting || _dedicated) Quit();
            else ShowTitle(reason);
        }).CallDeferred();
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest) return;
        if (_game != null && _game.Session.IsOnline)
        {
            _quitting = true;
            _game.Leave(); // a host tells everyone first; we quit once the session has ended
        }
        else Quit();
    }

    void Quit()
    {
        SteamworksApi.Shutdown();
        GetTree().Quit();
    }
}
