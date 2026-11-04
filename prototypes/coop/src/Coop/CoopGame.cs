using System;
using GalHack.Net;
using Godot;

namespace Coop;

/// <summary>
/// One play session, always at /root/Game: the network session, the shared world and the HUD.
/// The world is thrown away and rebuilt when a client reconnects, so a rejoin always starts
/// from the host's current state, never from leftovers.
/// </summary>
public partial class CoopGame : Node
{
    public sealed class Options
    {
        public string Name = "Player";
        public bool Host;
        public bool Dedicated;
        public ITransport Transport;
        public string Address;
        public NetConditions Conditions;
    }

    public Options Settings { get; private set; }
    public NetSession Session { get; private set; }
    public CoopWorld World { get; private set; }

    /// <summary>The session is over (reason, graceful).</summary>
    public event Action<string, bool> Ended;

    /// <summary>A fresh world was made (at the start, and again after a reconnect).</summary>
    public event Action WorldCreated;

    public static CoopGame Create(Options options) => new() { Name = "Game", Settings = options };

    public override void _Ready()
    {
        Session = new NetSession { Name = "Session", LocalName = Settings.Name, Conditions = Settings.Conditions };
        AddChild(Session);
        Session.ConnectionLost += reason =>
        {
            GD.Print($"[client] connection lost: {reason}");
            ReplaceWorld();
        };
        Session.Ended += OnEnded;
        if (!Settings.Dedicated && DisplayServer.GetName() != "headless") AddChild(new Hud { Name = "Hud", Game = this });
        Callable.From(Start).CallDeferred();
    }

    void Start()
    {
        if (Settings.Host || Settings.Dedicated)
        {
            if (Session.Host(Settings.Transport, Settings.Dedicated) != Error.Ok)
            {
                OnEnded(Session.Status, false);
                return;
            }
            ReplaceWorld();
        }
        else
        {
            ReplaceWorld(); // ready before the host's first message arrives
            Session.Join(Settings.Transport, Settings.Address);
        }
    }

    void ReplaceWorld()
    {
        DropWorld();
        World = new CoopWorld { Name = "World", Session = Session, Dedicated = Settings.Dedicated };
        AddChild(World);
        MoveChild(World, 1);
        WorldCreated?.Invoke();
    }

    void DropWorld()
    {
        if (World == null) return;
        RemoveChild(World); // out of the tree now, so the next one gets the same path
        World.QueueFree();
        World = null;
    }

    void OnEnded(string reason, bool graceful)
    {
        DropWorld();
        string role = Settings.Dedicated ? "server" : Settings.Host ? "host" : "client";
        GD.Print($"[{role}] SESSION ENDED graceful={graceful}: {reason}");
        Settings.Transport?.Shutdown();
        Ended?.Invoke(reason, graceful);
    }

    /// <summary>Leaves on purpose; a host ends the session for everyone, gracefully.</summary>
    public void Leave() => Session.Leave(Session.IsServer ? "the host left" : "You left the session.");

    /// <summary>Client: joins the same session again after leaving it, into a fresh world.</summary>
    public Error Rejoin()
    {
        ReplaceWorld();
        return Session.Rejoin();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.PhysicalKeycode == Key.F10)
            {
                Leave();
                return;
            }
            if (key.PhysicalKeycode == Key.I && Settings.Transport is SteamTransport steam)
            {
                steam.InviteFriends();
                return;
            }
        }
        World?.HandleInput(e);
    }
}
