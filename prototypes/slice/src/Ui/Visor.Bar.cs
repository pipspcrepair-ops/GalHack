using System;
using System.IO;
using DiegeticUi.Theme;
using Godot;
using Slice.Game;
using Slice.Vessels;
using BarKind = Slice.Game.SliceRules.BarKind;

namespace Slice.Ui;

/// <summary>A slot of the structure's toolbar as the visor shows it: its label, its value (on or off, a gun's rounds), lit or not, and its icon.</summary>
public readonly record struct BarShown(string Label, string Value, bool On, HudIcon Icon, BarAct Act);

/// <summary>
/// Seated in a command chair, the toolbar along the bottom is the structure's (design: Visor HUD layout): a slot for each
/// function its devices bring, equipment from the left and core systems from the right, each with its icon, its label
/// and its state. Standing up, BAG comes back.
/// </summary>
public partial class Visor
{
    /// <summary>A kind's icon, from its data name.</summary>
    public static HudIcon IconOf(BarKind k) =>
        Enum.TryParse(k.Icon, true, out HudIcon icon) ? icon : throw new InvalidDataException($"structureBar kind '{k.Id}': no icon '{k.Icon}'");

    /// <summary>The structure's bar as shown, left to right (null: an empty slot).</summary>
    public BarShown?[] BarSlots(Vessel v)
    {
        BarSlot[] slots = v.Bar.Arranged();
        var shown = new BarShown?[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not BarSlot f) continue;
            bool on = v.Bar.IsOn(f);
            string value = f.Act switch
            {
                BarAct.Toggle => on ? "ON" : "OFF",
                BarAct.Select => $"{v.Bar.Rounds(f)}",
                _ => Game.ScanWait > 0 ? $"{Game.ScanWait:F0}s" : $"{Game.ScanRangeOf(v) / 1000f:0.#}KM", // a scanner's range, or its cooldown
            };
            shown[i] = new BarShown(f.Label, value, on, IconOf(f.Kind), f.Act);
        }
        return shown;
    }

    /// <summary>The structure's bar in the toolbar's place, its name at its left and its keys above it.</summary>
    void DrawBar(HudLayout layout, Vessel v)
    {
        float s = layout.Scale, cell = HudLayout.Cell * s, gap = HudLayout.CellGap * s;
        BarShown?[] slots = BarSlots(v);
        for (int i = 0; i < slots.Length; i++)
        {
            var box = new Rect2(layout.Toolbar.Position + new Vector2(i * (cell + gap), 0), new Vector2(cell, cell));
            _hud.Text(UiTheme.Mono, (int)(11 * s), box.Position + new Vector2(3, 1), $"{i + 1}", C.Dim);
            if (slots[i] is not BarShown b)
            {
                _hud.Block(box, new Color(C.Glass, 0.35f));
                continue;
            }
            Color tone = b.Act switch { BarAct.Select => C.Accent, BarAct.Run => C.Holo, _ => C.Good };
            bool picked = b.Act == BarAct.Select && b.On;
            _hud.Block(box, new Color(picked ? tone : C.Glass, picked ? 0.9f : 0.6f));
            if (b.On) _hud.Frame(box, new Color(tone, 0.95f), 2f * s);
            Color ink = picked ? C.Glass : b.On || b.Act == BarAct.Run ? tone : C.Dim;
            var glyph = new Rect2(box.Position + new Vector2((cell - 30 * s) / 2, 7 * s), new Vector2(30 * s, 30 * s));
            DrawGlyph(b.Icon, glyph, new Color(ink, b.On || b.Act == BarAct.Run ? 1f : 0.6f), s * 0.85f);
            _hud.TextIn(UiTheme.Label, (int)(9 * s), new Rect2(box.Position + new Vector2(1, cell - 14 * s), new Vector2(cell - 2, 13 * s)),
                HudLayout.Fit(UiTheme.Label, (int)(9 * s), b.Label, cell - 3), picked ? C.Glass : C.Text, HorizontalAlignment.Center);
            if (b.Value.Length > 0)
                _hud.TextIn(UiTheme.Mono, (int)(10 * s), new Rect2(box.Position + new Vector2(2, 1), new Vector2(cell - 4, 13 * s)), b.Value, picked ? C.Glass : ink, HorizontalAlignment.Right);
        }
        int nameSize = (int)(13 * s), hintSize = (int)(13 * s);
        _hud.TextIn(UiTheme.Display, nameSize, layout.BarName, HudLayout.Fit(UiTheme.Display, nameSize, v.Name.ToUpperInvariant(), layout.BarName.Size.X), C.Accent, HorizontalAlignment.Right);
        string keys = $"[{KeyBindings.Name(Game.Settings.Keys["slot-1"])}-{KeyBindings.Name(Game.Settings.Keys["slot-9"])}] SLOTS   [LMB] FIRE   {Bound("seat")} STAND UP";
        _hud.TextIn(UiTheme.Label, hintSize, layout.Hints, HudLayout.Fit(UiTheme.Label, hintSize, keys, layout.Hints.Size.X), C.Dim, HorizontalAlignment.Center);
    }
}
