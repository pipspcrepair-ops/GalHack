using Godot;

namespace Invasions.World;

/// <summary>
/// A base console with a big screen showing the defence net: warnings with the attacker, source,
/// bearing and countdown; the fight; or all quiet. Dark when the base has no power. Faces +Z.
/// </summary>
public partial class ConsoleScreen : Node3D
{
    Label3D _header, _body;
    MeshInstance3D _screen, _headerBar;
    bool _powered = true;
    float _blink;
    bool _alarm;

    static readonly Color ScreenOn = new("0c2430");
    static readonly Color ScreenOff = new("0a0b0d");

    public override void _Ready()
    {
        Material dark = Palette.Mat(Palette.Dark, 0.6f);
        Material cream = Palette.Mat(Palette.Cream, 0.6f);
        Palette.Box(this, new Vector3(0, 0.45f, 0.1f), new Vector3(1.9f, 0.9f, 1.0f), dark);              // desk
        Palette.Box(this, new Vector3(0, 0.93f, 0.25f), new Vector3(1.9f, 0.08f, 0.75f), Palette.Mat(Palette.Teal)); // desk top
        for (int i = 0; i < 6; i++)                                                                        // keys
            Palette.Box(this, new Vector3(-0.6f + i * 0.24f, 0.99f, 0.35f), new Vector3(0.16f, 0.05f, 0.12f),
                i % 3 == 0 ? Palette.Glow(Palette.Amber, 1.5f) : Palette.Mat(Palette.Cream));

        var frame = new Node3D { Position = new Vector3(0, 1.9f, -0.25f), Rotation = new Vector3(-0.12f, 0, 0) };
        AddChild(frame);
        Palette.Box(frame, Vector3.Zero, new Vector3(2.0f, 1.45f, 0.14f), cream);
        Palette.Box(frame, new Vector3(0, -0.8f, 0.0f), new Vector3(0.3f, 0.3f, 0.12f), dark);
        _screen = Palette.Box(frame, new Vector3(0, 0, 0.075f), new Vector3(1.84f, 1.3f, 0.02f), Palette.Glow(ScreenOn, 0.4f));
        _headerBar = Palette.Box(frame, new Vector3(0, 0.52f, 0.088f), new Vector3(1.84f, 0.22f, 0.01f), Palette.Glow(Palette.Amber, 1.2f));

        _header = new Label3D
        {
            Font = Palette.Font,
            FontSize = 38,
            PixelSize = 0.004f,
            Modulate = new Color("0c1418"),
            OutlineSize = 0,
            Position = new Vector3(0, 0.52f, 0.095f),
            Width = 440,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        frame.AddChild(_header);
        _body = new Label3D
        {
            Font = Palette.Font,
            FontSize = 36,
            PixelSize = 0.0034f,
            Modulate = Palette.Amber,
            OutlineSize = 0,
            Position = new Vector3(-0.86f, 0.36f, 0.09f),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 500,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LineSpacing = 4f,
        };
        frame.AddChild(_body);
        Show("HOMESTEAD DEFENCE NET", "ALL QUIET", false);
    }

    public override void _Process(double delta)
    {
        if (!_powered) return;
        _blink += (float)delta;
        bool hot = _alarm && Mathf.PosMod(_blink, 0.8f) < 0.4f;
        _headerBar.MaterialOverride = Palette.Glow(hot ? Palette.SirenRed : Palette.Amber, hot ? 2.5f : 1.2f);
    }

    /// <summary>Sets the screen. An alarm makes the header bar flash red.</summary>
    public void Show(string header, string body, bool alarm)
    {
        _header.Text = header;
        _body.Text = body;
        _alarm = alarm;
    }

    public string Text => _header.Text + "\n" + _body.Text;

    public void SetPowered(bool on)
    {
        _powered = on;
        _header.Visible = on;
        _body.Visible = on;
        _headerBar.Visible = on;
        _screen.MaterialOverride = on ? Palette.Glow(ScreenOn, 0.4f) : Palette.Mat(ScreenOff, 0.2f);
    }
}
