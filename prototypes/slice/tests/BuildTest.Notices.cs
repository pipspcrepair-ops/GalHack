using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Items;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>
/// The notices' slot: a box of the visor's layout that leaves nothing else room to overlap at every size and
/// scale, its rows one under another inside it; with a menu up, the menu header's top right corner, which no
/// page's text, widget or panel touches. A burst of level-ups and unlocks shows at most three at once; the
/// rest wait, each comes in, holds and fades, and the slot is empty again after.
/// </summary>
public partial class BuildTest
{
    /// <summary>The menu header's notices corner against what a menu panel laid out: nothing of the page's own on it.</summary>
    static List<string> SlotProblems(MenuPanel panel)
    {
        var problems = new List<string>();
        Rect2 slot = HudLayout.MenuNotices;
        foreach (var (text, rect, _) in panel.Gui.Texts)
            if (rect.Grow(-1f).Intersects(slot)) problems.Add($"\"{text}\" is on the notices' corner");
        foreach (var (id, rect) in panel.Gui.Rects)
            if (rect.Intersects(slot)) problems.Add($"widget {id} is on the notices' corner");
        foreach (Rect2 fill in panel.Gui.Fills)
            if (!fill.Encloses(slot) && fill.Intersects(slot)) problems.Add($"a panel {fill} is on the notices' corner");
        return problems;
    }

    /// <summary>The slot's geometry at every size and scale the layout check covers (no game needed).</summary>
    void NoticeSlotChecks()
    {
        var sizes = new[] { new Vector2(1280, 720), new Vector2(1600, 900), new Vector2(1920, 1080), new Vector2(2560, 1440), new Vector2(1024, 768), new Vector2(3440, 1440) };
        var bad = new List<string>();
        float narrowest = float.MaxValue;
        foreach (Vector2 size in sizes)
            foreach (float scale in new[] { 0.75f, 1f, 1.25f, 1.5f })
            {
                HudLayout l = HudLayout.Compute(size, scale);
                if (l.Problem(size) is string p) bad.Add(p);
                List<Rect2> rows = Notices.Rows(l.Notices);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (!l.Notices.Grow(0.5f).Encloses(rows[i])) bad.Add($"row {i} leaves the slot at {size} x{scale}");
                    for (int k = i + 1; k < rows.Count; k++)
                        if (rows[i].Intersects(rows[k])) bad.Add($"rows {i} and {k} overlap at {size} x{scale}");
                }
                Rect2 menu = HudLayout.MenuToScreen(HudLayout.MenuNotices, size);
                if (!new Rect2(Vector2.Zero, size).Encloses(menu)) bad.Add($"the menu's corner leaves the {size} window");
                narrowest = MathF.Min(narrowest, l.Notices.Size.X);
            }
        Check(bad.Count == 0 && HudLayout.MenuNotices.End.Y <= 74f && HudLayout.MenuNotices.End.X <= 1600f,
            $"the notices' slot: {sizes.Length * 4} sizes and scales, never on another box or off the window, {Notices.MostShown} rows inside it none on another (narrowest {narrowest:F0} px); with a menu up, inside the menu header's top right corner{(bad.Count == 0 ? "" : ": " + string.Join("; ", bad.Take(5)))}");
    }

    IEnumerable<int> NoticeChecks()
    {
        GD.Print("-- the notices' slot --");
        NoticeSlotChecks();
        Notices notices = _game.Notices;
        foreach (int x in _play.WaitFor(() => notices.Shown.Count == 0 && notices.Waiting == 0, 30)) yield return x;
        notices.ResetMost();
        Progress p = _game.Progress;
        int level = p.Level;
        // The burst: four level-ups at once (with the recipes they bring), two unlocks bought with the points, a claim
        // and a made order between them, so nothing of one kind folds into the one before it.
        var gained = new List<int>();
        void Levelled(int l, int _) => gained.Add(l);
        p.LevelledUp += Levelled;
        for (int i = 0; i < 4; i++)
        {
            var (into, span) = p.ToNext;
            p.Earn(Math.Max(1, (int)(span - into) + 1), "notices test");
            notices.Post(NoticeKind.Made, "MADE", $"{i + 1} Hull plate");
        }
        p.LevelledUp -= Levelled;
        int unlocked = 0;
        foreach (TechNode n in p.Nodes.Where(n => p.StateOf(n) == UnlockState.Affordable).Take(2).ToList())
            if (p.Unlock(n.Id)) { unlocked++; notices.Post(NoticeKind.Claimed, "CLAIMED", "Wreck"); }
        int posted = notices.Shown.Count + notices.Waiting;
        Check(gained.Count >= 4 && notices.Shown.Count == Notices.MostShown && notices.Waiting > 0,
            $"a burst of {gained.Count} level-ups (level {level} to {p.Level}), {unlocked} unlocks and their recipes, claims and orders: {notices.Shown.Count} show, {notices.Waiting} wait their turn");
        // Watch it play out, with the Tab menu up for part of it.
        int most = 0, frames = 0, seenFading = 0, maxFrames = 60 * 60;
        bool menuChecked = false, rowsOk = true, menuSlot = false;
        var viewport = new Vector2(1920, 1080);
        while ((notices.Shown.Count > 0 || notices.Waiting > 0) && frames < maxFrames)
        {
            yield return 0;
            frames++;
            most = Math.Max(most, notices.Shown.Count);
            foreach (var n in notices.Shown)
            {
                double a = notices.Alpha(n);
                if (a > 0.05 && a < 0.95 && _game.Stats.Seconds > 0) seenFading++;
            }
            List<Rect2> rows = Notices.Rows(notices.Slot(viewport));
            rowsOk &= notices.Shown.Count <= rows.Count;
            if (frames == 60 && !menuChecked)
            {
                _game.Menu.Open(MenuTab.You);
                foreach (int x in _drive.Wait(3)) yield return x;
                menuSlot = notices.Slot(viewport) == HudLayout.MenuToScreen(HudLayout.MenuNotices, viewport) && notices.Shown.Count > 0;
                _game.Menu.Close();
                menuChecked = true;
            }
        }
        double perWave = Notices.In + Notices.Hold + Notices.Fade;
        Check(most <= Notices.MostShown && notices.MostAtOnce <= Notices.MostShown && rowsOk && notices.Shown.Count == 0 && notices.Waiting == 0,
            $"all {posted} come and go in {frames / 60.0:F1} s ({perWave:F1} s each, {Notices.MostShown} at a time): never more than {Notices.MostShown} at once ({most}), each in a row of its own");
        Check(seenFading > 0, $"each comes in and fades rather than blinking off ({seenFading} frames with one part-way)");
        Check(menuSlot, "with the Tab menu up they keep going, in the menu header's top right corner");
    }
}
