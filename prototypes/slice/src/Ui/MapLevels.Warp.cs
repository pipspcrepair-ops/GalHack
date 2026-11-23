using System.Collections.Generic;
using Godot;
using PlanetSurface.Skies;
using Slice.Game;
using Slice.Orbit;

namespace Slice.Ui;

/// <summary>
/// The system map's jump panel (solar-systems.md section 8 item 16): the body clicked is the one target, its route drawn
/// from where you are, with its AU, the warp cells it burns and its seconds, or the drive's word for what stops it; the
/// button shows the jump key and does what it does; while it spools, its share, and the same button calls it off.
/// </summary>
public sealed partial class MapLevels
{
    /// <summary>The system map's key of the body you are at: the frame you fly in, or the world you are on.</summary>
    string HereKey(StarSystem model)
    {
        Spaceflight sf = _game.Spaceflight;
        Orbiter here = _game.InOrbit ? sf.Here?.Body : _game.Zone?.Body;
        return Charts.KeyOf(model, here ?? model.Home);
    }

    void WarpPanel(Gui g, Rect2 panel, Rect2 side, StarSystem model, Dictionary<string, Vector2> places)
    {
        Spaceflight sf = _game.Spaceflight;
        Vector2 at = side.Position + new Vector2(0, 404);
        var button = new Rect2(at + new Vector2(0, 48), new Vector2(150, 28));
        string key = OS.GetKeycodeString(_game.Settings?.Keys["jump"] ?? Key.J);
        Jump j = sf.Jumping;
        if (j?.Running == true)
        {
            g.Text($"{(j.Drive == Spaceflight.WarpDrive ? "WARP" : "FOLD")} · {j.To.Name.ToUpperInvariant()}", at, C.Accent, 15);
            g.Text(j.Now == Jump.Stage.Spool ? $"SPOOL {j.Spooled:P0}" : $"TUNNEL {j.InTunnel:F0} S", at + new Vector2(0, 24), C.Text, 14);
            if (j.Now == Jump.Stage.Spool && g.Button("map:jump", button, $"[{key}] CANCEL", C.Danger, size: 13)) sf.JumpKey();
            return;
        }
        if (sf.Target?.Body is not { } body) return;
        WarpRoute r = sf.Route(body);
        g.Text($"{(r.Short ? "SHORT WARP" : "WARP")} · {r.Name.ToUpperInvariant()}", at, C.Accent, 15);
        g.Text($"{r.Au:F2} AU · {r.Cells} CELLS · {r.Seconds:F0} S", at + new Vector2(0, 24), C.Text, 14);
        if (r.Refusal != null) g.Text(r.Refusal, at + new Vector2(0, 52), C.Danger, 14);
        else if (g.Button("map:jump", button, $"[{key}] WARP", C.Accent, size: 13) && sf.JumpKey() is { } no)
            _game.Say(no, Tones.Warning);
        if (g.Drawing && places.TryGetValue(HereKey(model) ?? "world", out Vector2 from) && places.TryGetValue(Charts.KeyOf(model, body), out Vector2 to) && panel.HasPoint(from) && panel.HasPoint(to))
            g.Canvas.DrawDashedLine(from, to, r.Refusal == null ? C.Accent : C.Dim, 2f, 8f);
    }
}
