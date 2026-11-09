using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using Godot;
using Slice.Items;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>
/// Labels, values and icons only: every line the menus lay out (each Tab menu page, each escape menu
/// page and question, the start screen, the machines' in-world screens), every item's tooltip, the
/// visor's item and key lines and everything the suit said in the run is a label or a value, never a
/// sentence (<see cref="UiText"/>: more than three lowercase words in a row, or a full stop ending a phrase).
/// </summary>
public partial class BuildTest
{
    /// <summary>Every line seen on a page, by where it was seen.</summary>
    readonly List<(string Where, string Text)> _shownLines = new();

    /// <summary>Everything the suit said since the world began.</summary>
    readonly List<string> _said = new();

    /// <summary>Notes the lines a menu panel laid out in its last input pass.</summary>
    void NoteLines(string where, MenuPanel panel)
    {
        foreach (var (text, _, _) in panel.Gui.Texts) _shownLines.Add((where, text));
    }

    IEnumerable<int> WordChecks()
    {
        GD.Print("-- labels and values only: no sentences on screen --");
        // The start screen, laid out on its own.
        var layer = new CanvasLayer { Name = "WordsStart" };
        AddChild(layer);
        var start = new StartRoot { Name = "StartRoot" };
        layer.AddChild(start);
        Gui.Measure = true;
        start.Feed(new Vector2(-1e5f, -1e5f), false, false, false, false, false);
        NoteLines("start screen", start);
        int startLines = start.Gui.Texts.Count;
        layer.QueueFree();
        // The machines' in-world screens.
        int screens = 0;
        foreach (MachineScreens.Mounted m in _game.MachineScreens.Screens.Values)
        {
            m.Panel.Feed(new Vector2(-1e5f, -1e5f), false, false, false, false, false);
            NoteLines("an in-world screen", m.Panel);
            screens++;
        }
        // The Stats page's settings and credits, behind their buttons.
        _game.Menu.Open(MenuTab.Stats);
        foreach (int x in _drive.Wait(2)) yield return x;
        foreach (string page in new[] { "options:settings", "options:credits" })
        {
            foreach (int x in _drive.ClickWidget(page)) yield return x;
            foreach (int x in _drive.Wait(2)) yield return x;
            NoteLines($"menu, stats: {page}", _game.Menu.Root);
        }
        foreach (int x in _drive.ClickWidget("options:credits")) yield return x;
        _game.Menu.Close();
        Gui.Measure = false;
        yield return 0;
        // Every item's tooltip, as a stack of two.
        foreach (ItemDef item in Items.Items)
            foreach (var (text, _) in Tooltips.For(_game, item, 2)) _shownLines.Add(($"tooltip {item.Id}", text));
        // The visor's lines for what is in hand now, and the prompt.
        _shownLines.Add(("item line", _game.Visor.ItemLine()));
        _shownLines.Add(("key hints", _game.Visor.HintLine()));
        _shownLines.Add(("prompt", _game.UsePrompt()));
        foreach (string line in _said) _shownLines.Add(("the suit", line));

        var bad = new List<string>();
        var seen = new HashSet<string>();
        foreach (var (where, text) in _shownLines)
            if (text != null && seen.Add(text) && UiText.Problem(text) is string why) bad.Add($"{where}: \"{text}\" ({why})");
        foreach (string b in bad) GD.Print($"      SENTENCE  {b}");
        Check(bad.Count == 0 && startLines > 0,
            $"{seen.Count} distinct lines ({startLines} on the start screen, {screens} in-world screens, every page of the Tab and escape menus, {Items.Items.Count} tooltips, {_said.Count} suit lines) and none a sentence{(bad.Count == 0 ? "" : $": {bad.Count} found")}; allow-listed names: {string.Join(", ", UiText.Allowed)}");
    }
}
