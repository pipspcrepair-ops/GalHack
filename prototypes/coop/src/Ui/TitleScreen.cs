using System;
using GalHack.Net;
using Godot;

namespace Coop;

/// <summary>
/// The title screen: host or join over Steam (friends only), or over ENet for LAN and local
/// testing. Nothing touches Steam until a Steam button is pressed.
/// </summary>
public partial class TitleScreen : Control
{
    public string Message = "";
    public string DefaultName = "Player";

    public event Action<string> HostSteam;              // name
    public event Action<string, ulong> JoinSteamLobby;  // name, lobby
    public event Action<string, int> HostEnet;          // name, port
    public event Action<string, string> JoinEnet;       // name, address

    LineEdit _name, _address;
    Label _status;
    VBoxContainer _friends;
    Control _friendsPanel;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = new Color("1d3557"), AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });
        var horizon = new ColorRect { Color = new Color("e76f51"), AnchorTop = 0.62f, AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(horizon);
        var glow = new ColorRect { Color = new Color("f4a261"), AnchorTop = 0.56f, AnchorRight = 1, AnchorBottom = 0.62f, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(glow);

        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.14f, 0.92f),
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 36, ContentMarginRight = 36, ContentMarginTop = 28, ContentMarginBottom = 28,
            BorderColor = new Color("f4a100"), BorderWidthTop = 3,
        });
        center.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(column);

        column.AddChild(Text("GALHACK", 54, new Color("f4a100")));
        column.AddChild(Text("Co-op prototype  ·  one group of friends, one ship", 18, new Color("e9dfc4")));

        var nameRow = new HBoxContainer();
        nameRow.AddChild(Text("Your name", 18, new Color("e9dfc4")));
        _name = new LineEdit { Text = DefaultName, CustomMinimumSize = new Vector2(260, 0), MaxLength = 24 };
        nameRow.AddChild(_name);
        column.AddChild(nameRow);

        column.AddChild(Text("Steam (friends only)", 16, new Color("0a9396")));
        var steamRow = new HBoxContainer();
        steamRow.AddThemeConstantOverride("separation", 12);
        steamRow.AddChild(Button("Host (Steam)", () => HostSteam?.Invoke(PlayerName)));
        steamRow.AddChild(Button("Join a friend (Steam)", ShowFriends));
        column.AddChild(steamRow);

        column.AddChild(Text("Local network / testing (ENet)", 16, new Color("0a9396")));
        var enetRow = new HBoxContainer();
        enetRow.AddThemeConstantOverride("separation", 12);
        enetRow.AddChild(Button("Host (LAN / ENet)", () => HostEnet?.Invoke(PlayerName, EnetTransport.DefaultPort)));
        _address = new LineEdit { Text = $"127.0.0.1:{EnetTransport.DefaultPort}", CustomMinimumSize = new Vector2(220, 0) };
        enetRow.AddChild(_address);
        enetRow.AddChild(Button("Join (ENet IP)", () => JoinEnet?.Invoke(PlayerName, _address.Text)));
        column.AddChild(enetRow);

        _friendsPanel = new VBoxContainer { Visible = false };
        _friendsPanel.AddChild(Text("Friends hosting GalHack right now (or accept an invite in the Steam overlay):", 15, new Color("e9dfc4")));
        _friends = new VBoxContainer();
        _friendsPanel.AddChild(_friends);
        _friendsPanel.AddChild(Button("Refresh", RefreshFriends));
        column.AddChild(_friendsPanel);

        _status = Text(Message, 16, new Color("ffb4a2"));
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(560, 0);
        column.AddChild(_status);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    string PlayerName => string.IsNullOrWhiteSpace(_name.Text) ? "Player" : _name.Text.Trim();

    public void SetStatus(string message)
    {
        Message = message;
        if (_status != null) _status.Text = message;
    }

    void ShowFriends()
    {
        if (!SteamworksApi.TryInit(out string error))
        {
            SetStatus(error);
            return;
        }
        if (_name.Text == DefaultName && SteamworksApi.PersonaName.Length > 0) _name.Text = SteamworksApi.PersonaName;
        _friendsPanel.Visible = true;
        RefreshFriends();
    }

    void RefreshFriends()
    {
        foreach (Node child in _friends.GetChildren()) child.QueueFree();
        var lobbies = SteamTransport.FriendLobbies();
        if (lobbies.Count == 0) _friends.AddChild(Text("Nobody yet. Ask a friend to press Host (Steam), then Refresh.", 15, new Color("8d99ae")));
        foreach (var (name, lobby) in lobbies)
            _friends.AddChild(Button($"Join {name}", () => JoinSteamLobby?.Invoke(PlayerName, lobby)));
    }

    static Label Text(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    static Button Button(string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(200, 40) };
        button.AddThemeFontSizeOverride("font_size", 17);
        button.Pressed += pressed;
        return button;
    }
}
