using DiegeticUi.Logic;
using DiegeticUi.Screens;
using DiegeticUi.Theme;
using Godot;

namespace DiegeticUi.Pages;

/// <summary>The small screen on the holo-table's rim: what the selected world is, and what it will do to you.</summary>
public partial class HoloInfoPage : Control, IScreenPage
{
    static Palette C => UiTheme.Colors;

    public const int Width = 640, Height = 360;

    readonly StarMap _map;
    int _seen = -1;

    public HoloInfoPage(StarMap map)
    {
        _map = map;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public bool Refresh()
    {
        if (_map.Version == _seen) return false;
        _seen = _map.Version;
        QueueRedraw();
        return true;
    }

    public override void _Draw()
    {
        this.Block(new Rect2(Vector2.Zero, Size), C.Glass);
        this.Block(new Rect2(0, 0, Width, 64), C.Good);
        this.TextIn(UiTheme.Display, 30, new Rect2(0, 0, Width, 64), "STAR MAP", C.Glass, padX: 24);
        this.TextIn(UiTheme.Label, 22, new Rect2(0, 0, Width, 64), _map.SystemName, C.Glass, HorizontalAlignment.Right, 24);

        Body body = _map.Selected;
        if (body == null)
        {
            this.Text(UiTheme.Label, 28, new Vector2(24, 110), "TOUCH A WORLD", C.Text);
            this.Text(UiTheme.Label, 28, new Vector2(24, 150), "TO SET A WAYPOINT", C.Text);
            this.Text(UiTheme.Label, 20, new Vector2(24, 230), "DARK RINGS ARE UNSURVEYED SPACE", C.Dim);
            return;
        }

        Color colour = body.Discovered ? Paint.Rgb(body.Color) : C.Dim;
        this.Block(new Rect2(24, 88, 14, 54), colour);
        this.Text(UiTheme.Label, 40, new Vector2(52, 88), body.Name, C.Text);
        string kind = body.Kind switch
        {
            BodyKind.Star => "STAR",
            BodyKind.Rocky => "ROCK WORLD",
            BodyKind.Ocean => "OCEAN WORLD",
            BodyKind.GasGiant => "GAS GIANT",
            BodyKind.Station => "STATION",
            _ => "UNKNOWN",
        };
        this.Text(UiTheme.Label, 22, new Vector2(24, 158), body.Orbit > 0 ? $"{kind}  -  ORBIT {body.Orbit:0.00} AU" : kind, colour);
        this.Text(UiTheme.Label, 20, new Vector2(24, 196), body.Note, C.Dim);

        this.Text(UiTheme.Label, 18, new Vector2(24, 250), "HAZARDS", C.Dim);
        (Hazard h, string label)[] lamps = { (Hazard.Cold, "COLD"), (Hazard.Heat, "HEAT"), (Hazard.Radiation, "RAD"), (Hazard.Toxic, "TOX"), (Hazard.Vacuum, "VAC") };
        for (int i = 0; i < lamps.Length; i++)
        {
            var r = new Rect2(24 + i * 120, 280, 110, 52);
            bool on = body.Discovered && body.Hazards.HasFlag(lamps[i].h);
            this.Block(r, on ? C.Danger : C.Panel);
            this.TextIn(UiTheme.Label, 22, r, body.Discovered ? lamps[i].label : "?", on ? C.Glass : C.Dim, HorizontalAlignment.Center);
        }
    }
}
