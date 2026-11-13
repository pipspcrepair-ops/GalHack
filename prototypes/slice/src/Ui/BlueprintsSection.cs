using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics;
using GalHack.Structures;
using Godot;
using ShipHandling.Handling;
using Slice.Construction;
using Slice.Game;
using Slice.Items;
using Slice.Player;
using Slice.Vessels;
using Palette = DiegeticUi.Theme.Palette;
using Structure = GalHack.Structures.Structure;

namespace Slice.Ui;

/// <summary>
/// BLUEPRINTS: the library (the starter set under Large and Small, the player's own under Yours),
/// the selected blueprint on a turntable, its numbers, what it takes and which of its devices are
/// still locked, PLACE and SAVE. Blueprints live here and nowhere else: never on the toolbar.
///
/// PLACE in Creative sets the whole structure down at once on the nearest clear ground ahead (or
/// says why it can't); in Survival it starts the projector, which builds it block by block from
/// your bag, and only once its devices are unlocked. SAVE keeps the structure you sit in or look
/// at (one of yours) as a new blueprint under a name.
/// </summary>
public sealed class BlueprintsSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    BlueprintLibrary _library;
    BlueprintPreview _preview;
    string _selected;
    int _scroll;
    string _why;
    Vector2 _dragFrom;

    /// <summary>Why the last PLACE set nothing down, or null.</summary>
    public string Why => _why;

    /// <summary>The save dialog is up.</summary>
    public bool Saving { get; private set; }
    string _saveProblem;

    public const string NameBox = "blueprint-name";
    const float RowH = 38f;
    /// <summary>The bottom of the library's column, left clear for the alerts' row (the toolbar's line at the far left).</summary>
    const float AlertsRoom = 140f;

    public BlueprintsSection(Game.Game game) => _game = game;
    public Vector2 PreferredSize => new(1560, 790);

    /// <summary>The library, read when first wanted (tests point its saved folder elsewhere).</summary>
    public BlueprintLibrary Library => _library ??= new BlueprintLibrary(_game.Zone.Data, _game.Zone.Data.Rules.Planet.Gravity);

    public LibraryEntry Selected
    {
        get
        {
            var all = Library.All.ToList();
            return all.FirstOrDefault(e => e.File == _selected) ?? all.FirstOrDefault();
        }
    }

    public void Select(LibraryEntry e)
    {
        _selected = e?.File;
        _why = null;
    }

    /// <summary>What the tab shows changes with these.</summary>
    public string Signature => $"{_selected}|{Saving}|{_why}|{_saveProblem}|{_library?.Version}|{Busy}|{(_preview?.Dragging == true ? Time.GetTicksMsec() / 33 : 0)}";

    /// <summary>The structure SAVE would keep: the one you sit in, else the one you look at, else the one you stand in; yours only.</summary>
    public Vessel SaveTarget()
    {
        Survivor s = _game.Survivor;
        if (Owned(s.Seated)) return s.Seated;
        if (WorldRay.Cast(_game.Zone, s, 40f, Layers.World | Layers.Hull, out LookHit hit) && Owned(hit.Vessel)) return hit.Vessel;
        return Owned(s.Aboard) ? s.Aboard : null;
    }

    static bool Owned(Vessel v) => v != null && !v.IsWreck && !v.IsRuin && v.Structure.Core != null && v.Structure.Owner == Building.DefaultOwner;

    Vessel _target;

    public void Build(Gui g, Rect2 area)
    {
        if (!g.Drawing) _target = Saving ? _target : SaveTarget();
        LibraryEntry e = Selected;
        // Under the save dialog nothing else is lit or clickable.
        Vector2 mouse = g.Mouse;
        if (Saving) g.Mouse = new Vector2(-1e5f, -1e5f);
        List(g, new Rect2(area.Position, new Vector2(360, area.Size.Y)));
        if (e != null)
        {
            Preview(g, new Rect2(area.Position + new Vector2(380, 0), new Vector2(760, area.Size.Y)), e);
            Facts(g, new Rect2(area.Position + new Vector2(1160, 0), new Vector2(area.Size.X - 1160, area.Size.Y)), e);
        }
        g.Mouse = mouse;
        if (Saving) SaveDialog(g, area);
    }

    // ---- The library ---------------------------------------------------------------------------

    void List(Gui g, Rect2 r)
    {
        var list = new Rect2(r.Position, new Vector2(r.Size.X, r.Size.Y - 128 - AlertsRoom));
        var rows = new List<(string caption, LibraryEntry entry)>();
        void Group(string caption, IEnumerable<LibraryEntry> entries)
        {
            var some = entries.ToList();
            if (some.Count == 0) return;
            rows.Add((caption, null));
            foreach (LibraryEntry x in some) rows.Add((null, x));
        }
        Group("STARTERS", Library.Starters);
        Group("YOURS", Library.Yours);

        int fits = (int)(list.Size.Y / RowH);
        if (!g.Drawing && g.Wheel != 0 && g.Over(list)) _scroll -= g.Wheel;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, rows.Count - fits));
        var all = Library.All.ToList();
        LibraryEntry selected = Selected;
        float y = list.Position.Y;
        for (int i = _scroll; i < rows.Count && i < _scroll + fits; i++, y += RowH)
        {
            var (caption, entry) = rows[i];
            if (caption != null)
            {
                g.Caption(caption, new Vector2(list.Position.X, y + 8), list.Size.X);
                continue;
            }
            var row = new Rect2(list.Position.X, y + 2, list.Size.X, RowH - 4);
            int index = all.IndexOf(entry);
            if (g.Button($"bp:entry:{index}", row, "", C.Accent2, lit: entry == selected)) Select(entry);
            Color ink = entry == selected ? C.Glass : C.Text;
            float x = row.Position.X + 14;
            g.TextIn(entry.Name.ToUpperInvariant(), new Rect2(x, row.Position.Y, row.End.X - x - 120, row.Size.Y), ink, 16);
            g.TextIn(SizeText(entry), new Rect2(row.End.X - 128, row.Position.Y, 118, row.Size.Y), entry == selected ? C.Glass : C.Dim, 13, HorizontalAlignment.Right, UiTheme.Mono);
        }
        if (rows.Count > fits) g.TextIn($"{_scroll + 1}-{Math.Min(rows.Count, _scroll + fits)} / {rows.Count}", new Rect2(list.Position.X, list.End.Y - 2, list.Size.X, 18), C.Dim, 12, HorizontalAlignment.Right, UiTheme.Mono);

        // SAVE: what it would keep, and the button, above the alerts' row at the bottom left.
        float top = r.End.Y - 110 - AlertsRoom;
        g.Caption("SAVE", new Vector2(r.Position.X, top), r.Size.X);
        Vessel t = _target;
        g.Text("TARGET", new Vector2(r.Position.X, top + 36), C.Dim, 14);
        g.TextIn(t == null ? "-" : $"{t.Name.ToUpperInvariant()} · {ModeLabel(t.Structure.Mode)}",
            new Rect2(r.Position.X + 80, top + 30, r.Size.X - 80, 26), t == null ? C.Dim : C.Text, 15, HorizontalAlignment.Right, UiTheme.Mono);
        if (g.Button("bp:save", new Rect2(r.Position.X, top + 60, r.Size.X, 48), "SAVE", C.Accent, enabled: t != null, size: 20)) OpenSave();
    }

    static string SizeText(LibraryEntry e) => $"{e.Size.X:0.#}x{e.Size.Y:0.#}x{e.Size.Z:0.#} m";

    // ---- The turntable ---------------------------------------------------------------------------

    void Preview(Gui g, Rect2 r, LibraryEntry e)
    {
        string mode = ModeLabel(e);
        float w = DiegeticUi.Theme.Paint.TextWidth(UiTheme.Label, 15, mode) + 26;
        float x = r.End.X - w;
        var chip = new Rect2(x, r.Position.Y + 6, w, 30);
        g.Fill(chip, C.Accent2);
        g.TextIn(mode, chip, C.Glass, 15, HorizontalAlignment.Center);
        x -= 8;
        g.TextIn(HudLayout.Fit(UiTheme.Display, 28, e.Name.ToUpperInvariant(), x - r.Position.X - 12), new Rect2(r.Position.X, r.Position.Y, x - r.Position.X - 12, 40), C.Accent, 28, HorizontalAlignment.Left, UiTheme.Display);
        var view = new Rect2(r.Position.X, r.Position.Y + 48, r.Size.X, r.Size.Y - 48);
        g.Fill(view, g.Back(new Color(C.Panel, 0.55f)));
        g.Outline(view, new Color(C.Rule, 0.7f), 1.5f);
        if (!g.Drawing) Turn(g, view);
        else if (Picture(e, view) is Texture2D tex) g.Canvas.DrawTextureRect(tex, view, false);
        else g.TextIn("[ ]", view, C.Dim, 40, HorizontalAlignment.Center, UiTheme.Mono);
    }

    /// <summary>Dragging turns it, the wheel brings it nearer.</summary>
    void Turn(Gui g, Rect2 view)
    {
        _preview ??= NewPreview();
        if (_preview == null) return;
        if (g.Pressed && g.Over(view) && g.DragId == null)
        {
            _preview.Dragging = true;
            _dragFrom = g.Mouse;
        }
        if (_preview.Dragging)
        {
            if (!g.Held) _preview.Dragging = false;
            else
            {
                Vector2 d = g.Mouse - _dragFrom;
                _dragFrom = g.Mouse;
                _preview.Yaw -= d.X * 0.012f;
                _preview.Pitch += d.Y * 0.008f;
            }
        }
        if (g.Wheel != 0 && g.Over(view)) _preview.Zoom *= g.Wheel > 0 ? 0.88f : 1.14f;
    }

    BlueprintPreview NewPreview()
    {
        if (!BlueprintPreview.CanRender || _game.Menu == null) return null;
        var p = new BlueprintPreview { Name = "BlueprintPreview" };
        _game.Menu.AddChild(p);
        return p;
    }

    Texture2D Picture(LibraryEntry e, Rect2 view)
    {
        _preview ??= NewPreview();
        if (_preview == null) return null;
        float scale = _game.Menu.Root.Fit.Scale.X;
        return _preview.Picture(e.Plan, new Vector2I((int)(view.Size.X * scale), (int)(view.Size.Y * scale)));
    }

    // ---- Numbers, what it takes, PLACE -----------------------------------------------------------

    /// <summary>Base, Hover or Flight (the old Capital and Vessel modes are both Flight).</summary>
    public static string ModeLabel(LibraryEntry e) => ModeLabel(e.Plan.Mode);

    public static string ModeLabel(CoreMode mode) => mode switch
    {
        CoreMode.Base => "BASE",
        CoreMode.Hover => "HOVER",
        _ => "FLIGHT",
    };

    bool Creative => _game.Mode == GameMode.Creative;

    /// <summary>A whole placement is under way.</summary>
    bool Busy => _game.Builder.Placing is WholePlacement p && !p.Done;

    /// <summary>Its devices the tech tree hasn't opened yet (none in Creative).</summary>
    public List<string> Locked(LibraryEntry e)
    {
        return e.Families.Where(f => !e.Plan.Catalogue.Type(f.BlockId).IsArmor && !_game.Progress.BlockAllowed(f.BlockId))
            .Select(f => f.BlockId).ToList();
    }

    void Facts(Gui g, Rect2 r, LibraryEntry e)
    {
        g.Caption("NUMBERS", r.Position, r.Size.X);
        float y = r.Position.Y + 34;
        void Row(string label, string value, Color colour)
        {
            g.Text(label, new Vector2(r.Position.X, y + 2), C.Dim, 14);
            g.TextIn(value, new Rect2(r.Position.X + 150, y - 2, r.Size.X - 150, 24), colour, 16, HorizontalAlignment.Right, UiTheme.Mono);
            y += 26;
        }
        Row("SIZE", SizeText(e), C.Text);
        Row("BLOCKS", $"{e.Blocks:N0}", C.Text);
        Row("MASS", e.MassT >= 100 ? $"{e.MassT:N0} t" : $"{e.MassT:F2} t", C.Text);
        HandlingStats h = e.Handling;
        if (h != null)
        {
            float turn = MathF.Min(h.TurnRate, _game.Zone.Data.Rules.Build.MaxTurn);
            Row("ACCELERATION", $"{h.Accel(Direction.Forward):F1} m/s²", C.Text);
            Row("TURN RATE", $"{turn:F0} °/s", C.Text);
            Row("TOP SPEED", $"{h.TopSpeed:F0} m/s", C.Text);
            Row("LIFT MARGIN", $"{h.LiftMargin:+0;-0} kN", h.CanLiftOff ? C.Good : C.Danger);
        }

        // What it takes: each block item, how many, what you carry, and its lock.
        y += 10;
        Bill cost = BlueprintLibrary.Cost(_game.Zone.Data.Items, e.Plan);
        g.Caption("COST", new Vector2(r.Position.X, y), r.Size.X);
        g.TextIn(Creative ? "FREE" : $"{cost.Sum(kv => kv.Value):N0} BLOCKS", new Rect2(r.Position.X, y - 4, r.Size.X, 22), Creative ? C.Good : C.Dim, 13, HorizontalAlignment.Right, UiTheme.Mono);
        y += 34;
        List<string> locked = Creative ? new List<string>() : Locked(e);
        float bottom = r.End.Y - 96;
        int shown = 0, rowsFit = (int)((bottom - y) / 23f);
        foreach (var (blockId, count) in e.Families)
        {
            if (shown == rowsFit - 1 && e.Families.Count > rowsFit)
            {
                g.Text($"+{e.Families.Count - shown} MORE", new Vector2(r.Position.X + 30, y + 2), C.Dim, 13);
                break;
            }
            ItemDef item = _game.Zone.Data.Items.BlockItem(blockId);
            string name = item?.Name ?? e.Plan.Catalogue.Type(blockId).Name;
            bool isLocked = locked.Contains(blockId);
            if (item != null && ItemIcons.Shared?.Get(item) is Texture2D icon) g.Image(icon, new Rect2(r.Position.X, y + 1, 20, 20), Colors.White);
            if (item != null) g.SetTip(new Rect2(r.Position.X, y, r.Size.X, 22), () => Tooltips.For(_game, item, count));
            g.TextIn(name.ToUpperInvariant(), new Rect2(r.Position.X + 28, y, r.Size.X - 150, 22), isLocked ? C.Danger : C.Text, 13);
            int have = item == null ? 0 : _game.Pack[item.Id];
            string amount = Creative ? $"{count}" : $"{Math.Min(have, count)}/{count}";
            g.TextIn(isLocked ? "LOCKED" : amount, new Rect2(r.End.X - 120, y, 120, 22), isLocked ? C.Danger : Creative || have >= count ? C.Text : C.Warn, 13, HorizontalAlignment.Right, UiTheme.Mono);
            y += 23;
            shown++;
        }

        // PLACE.
        if (_why != null) g.TextIn(_why, new Rect2(r.Position.X, r.End.Y - 90, r.Size.X, 24), C.Warn, 14, HorizontalAlignment.Right);
        if (g.Button("bp:place", new Rect2(r.Position.X, r.End.Y - 60, r.Size.X, 58), Busy ? "PLACING" : "PLACE", C.Good, enabled: locked.Count == 0 && !Busy, size: 22)) Place(e);
    }

    /// <summary>Creative: the whole of it, set down over the next few ticks (the menu closes once it stands). Survival: the projector, block by block from your bag.</summary>
    public void Place(LibraryEntry e)
    {
        _why = null;
        if (Creative)
        {
            if (Busy) return;
            WholePlacement job = _game.Builder.PlaceWhole(e.Plan);
            job.Finished += j =>
            {
                if (j.Vessel != null) _game.Menu.Close();
                else _why = j.Why;
            };
            return;
        }
        if (Locked(e).Count > 0) return;
        _game.Builder.Project(e.Plan);
        _game.Menu.Close();
    }

    // ---- SAVE --------------------------------------------------------------------------------------

    void OpenSave()
    {
        if (_target == null) return;
        Saving = true;
        _saveProblem = null;
        ItemViews.Get(NameBox).Text = _target.Name;
        ItemViews.Focused = NameBox;
    }

    public void CloseSave()
    {
        Saving = false;
        if (ItemViews.Focused == NameBox) ItemViews.Focused = null;
    }

    /// <summary>Saves the target under the name typed. True if it was saved.</summary>
    public bool ConfirmSave()
    {
        if (_target == null) { _saveProblem = "NO STRUCTURE"; return false; }
        LibraryEntry saved = Library.Save(_target.Structure, ItemViews.Get(NameBox).Text, out string why);
        if (saved == null)
        {
            _saveProblem = why;
            return false;
        }
        CloseSave();
        Select(saved);
        _game.Notices.Post(NoticeKind.Saved, "SAVED", saved.Name);
        return true;
    }

    void SaveDialog(Gui g, Rect2 area)
    {
        g.Fill(area, new Color(C.Glass, 0.6f));
        var box = new Rect2(area.GetCenter() - new Vector2(340, 150), new Vector2(680, 300));
        g.Fill(box, new Color(C.Glass, 0.97f));
        g.Outline(box, C.Accent, 2f);
        g.Fill(new Rect2(box.Position, new Vector2(box.Size.X, 6)), C.Accent);
        var inner = box.Grow(-28);
        g.Text("SAVE AS BLUEPRINT", inner.Position + new Vector2(0, 4), C.Accent, 24, UiTheme.Display);
        Vessel t = _target;
        if (t != null)
        {
            g.Text("STRUCTURE", inner.Position + new Vector2(0, 56), C.Dim, 14);
            g.TextIn($"{t.Name.ToUpperInvariant()} · {ModeLabel(t.Structure.Mode)} · {t.Structure.Count:N0} BLOCKS",
                new Rect2(inner.Position.X + 130, inner.Position.Y + 50, inner.Size.X - 130, 26), C.Text, 15, HorizontalAlignment.Right, UiTheme.Mono);
        }
        g.Text("NAME", inner.Position + new Vector2(0, 104), C.Dim, 14);
        var field = new Rect2(inner.Position.X + 130, inner.Position.Y + 96, inner.Size.X - 130, 36);
        if (g.TextBox("bp:name", field, ItemViews.Get(NameBox).Text, "", ItemViews.Focused == NameBox)) ItemViews.Focused = NameBox;
        if (_saveProblem != null) g.TextIn(_saveProblem, new Rect2(field.Position.X, field.End.Y + 6, field.Size.X, 22), C.Warn, 14, HorizontalAlignment.Right);
        var buttons = new Rect2(inner.Position.X, inner.End.Y - 52, inner.Size.X, 52);
        if (g.Button("bp:save-confirm", new Rect2(buttons.Position, new Vector2(280, 52)), "SAVE", C.Good, size: 20)) ConfirmSave();
        if (g.Button("bp:save-cancel", new Rect2(buttons.End.X - 280, buttons.Position.Y, 280, 52), "CANCEL", C.Accent2, size: 20)) CloseSave();
    }
}
