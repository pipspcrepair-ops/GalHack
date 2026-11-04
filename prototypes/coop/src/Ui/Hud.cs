using System.Linq;
using System.Text;
using GalHack.Net;
using Godot;

namespace Coop;

/// <summary>A debug overlay: where you are, your ship, the session, and the player list with pings.</summary>
public partial class Hud : CanvasLayer
{
    public CoopGame Game;

    Label _left, _players;
    double _refresh;

    public override void _Ready()
    {
        Layer = 5;
        _left = MakeLabel(new Vector2(16f, 12f));
        _players = MakeLabel(Vector2.Zero);
        _players.HorizontalAlignment = HorizontalAlignment.Right;
        _players.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _players.GrowHorizontal = Control.GrowDirection.Begin;
        _players.Position = new Vector2(-16f, 12f);
        var crosshair = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0.8f),
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -2f, OffsetRight = 2f, OffsetTop = -2f, OffsetBottom = 2f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(crosshair);
    }

    Label MakeLabel(Vector2 position)
    {
        var label = new Label { Position = position, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 17);
        label.AddThemeColorOverride("font_color", new Color("ffe8c2"));
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
        AddChild(label);
        return label;
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.2;
        NetSession session = Game.Session;
        CoopWorld world = Game.World;
        Player player = world?.LocalPlayer;

        var text = new StringBuilder();
        string transport = session.Transport?.Describe ?? "";
        text.AppendLine(session.Connected
            ? $"{RoleName(session)}  ·  {transport}  ·  {Zones.Name(world?.LocalZone ?? 0)}"
            : session.Status);
        if (player != null)
        {
            Ship ship = player.Ship;
            text.AppendLine(player.CurrentMode switch
            {
                Player.Mode.Piloting => $"Piloting {ship?.Name}",
                Player.Mode.Aboard when ship != null => ship.GravityOn ? $"Aboard {ship.Name}, walking" : $"Aboard {ship.Name}, floating (core gravity off)",
                _ => $"Outside   jetpack {player.JetpackFuel * 100f:F0}%",
            });
            if (ship != null)
            {
                string flight = ship.StuntAutopilot ? "stunt autopilot" : ship.Piloted ? $"flown by {PilotName(session, ship.PilotPeer)}" : "autopilot holding course";
                text.AppendLine($"{ship.Name} {ship.Speed:F0} m/s   altitude {ship.GlobalPosition.Y:F0} m   {flight}");
            }
        }
        else if (session.Connected) text.AppendLine("Joining the world...");
        text.AppendLine();
        text.AppendLine("WASD move  ·  Space jump / up  ·  Ctrl crouch / down  ·  Shift sprint / boost  ·  F pilot seat  ·  Q/E roll");
        text.AppendLine("G core gravity  ·  T stunt autopilot  ·  V third person  ·  R back aboard  ·  F6 other zone (debug)");
        text.Append(Game.Settings.Transport is SteamTransport ? "I invite friends  ·  " : "");
        text.AppendLine($"F10 leave  ·  Esc free the mouse  ·  {Engine.GetFramesPerSecond()} fps");
        _left.Text = text.ToString();

        var list = new StringBuilder("Players\n");
        foreach (NetSession.PeerInfo info in session.Peers.Values.OrderBy(p => p.Id))
        {
            string ping = info.Id == 1 ? "host" : info.PingMs >= 0 ? $"{info.PingMs} ms" : "...";
            string you = info.Id == session.LocalId ? "  (you)" : "";
            list.AppendLine($"{info.Name}{you}   {ping}");
        }
        if (session.CurrentRole == NetSession.Role.Client && session.Peers.TryGetValue(session.LocalId, out var me) && me.PingMs >= 0)
            list.AppendLine($"\nyour ping {me.PingMs} ms");
        _players.Text = list.ToString();
    }

    static string RoleName(NetSession session) => session.CurrentRole switch
    {
        NetSession.Role.Host => "Hosting",
        NetSession.Role.Dedicated => "Dedicated server",
        _ => "Client",
    };

    static string PilotName(NetSession session, int peer) =>
        session.Peers.TryGetValue(peer, out var info) ? info.Name : $"peer {peer}";
}
