using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Logic;
using DiegeticUi.Screens;
using DiegeticUi.Theme;
using Godot;

namespace DiegeticUi.Pages;

/// <summary>
/// The constructor's own screen. Order on the left; on the right, two queues: the player's
/// orders above, and below them the sub-parts the constructor added by itself, each saying
/// which order it feeds. Raw materials nobody can make show in red at the bottom.
/// </summary>
public partial class ConstructorPage : Control, IScreenPage
{
    static Palette C => UiTheme.Colors;

    static readonly Recipe[] Menu = { Recipes.RoverWheel, Recipes.Motor, Recipes.HullPlate, Recipes.O2Canister, Recipes.MedKit };

    readonly ConstructorQueue _queue;
    readonly WarningTimeline _warnings;
    int _seenQueue = -1, _seenStorage = -1, _seenWarnings = -1;

    public IReadOnlyList<BlockButton> OrderButtons { get; }

    public ConstructorPage(ConstructorQueue queue, WarningTimeline warnings)
    {
        _queue = queue;
        _warnings = warnings;
        MouseFilter = MouseFilterEnum.Ignore;

        var buttons = new List<BlockButton>();
        for (int i = 0; i < Menu.Length; i++)
        {
            Recipe recipe = Menu[i];
            var button = new BlockButton
            {
                Text = recipe.Output.Name,
                Sub = $"{recipe.Output.MassKg:0.#} KG  {recipe.Output.VolumeL:0.#} L  {recipe.Seconds:0} S",
                Colour = C.Accent,
                Position = new Vector2(36, 198 + i * 98),
                Size = new Vector2(380, 86),
            };
            button.Pressed += () => _queue.Order(recipe.Output);
            AddChild(button);
            buttons.Add(button);
        }
        OrderButtons = buttons;
    }

    public bool Refresh()
    {
        if (_queue.Version == _seenQueue && _queue.Storage.Version == _seenStorage && _warnings.Version == _seenWarnings) return false;
        if (_queue.Storage.Version != _seenStorage && _queue.Missing.Count > 0) _queue.RecheckMissing();
        _seenQueue = _queue.Version;
        _seenStorage = _queue.Storage.Version;
        _seenWarnings = _warnings.Version;
        QueueRedraw();
        return true;
    }

    public override void _Draw()
    {
        this.Block(new Rect2(Vector2.Zero, Size), C.Glass);
        this.Header(Size.X, "CONSTRUCTOR", "FAB-2");

        string status = _queue.Missing.Count > 0 ? "WAITING ON RAW MATERIALS"
            : _queue.Busy ? "BUILDING" : "IDLE  -  PRESS AN ITEM TO ORDER IT";
        PageChrome.Strip(this, Size.X, _warnings, status, _queue.Missing.Count > 0 ? C.Danger : _queue.Busy ? C.Accent2 : C.Good);

        this.Caption(new Vector2(36, 150), "ORDER", C.Dim);
        this.Block(new Rect2(440, 160, 4, 540), C.Rule);

        // Your orders.
        this.Caption(new Vector2(470, 150), "YOUR ORDERS", C.Accent);
        float y = 198;
        if (_queue.Orders.Count == 0) this.Text(UiTheme.Label, UiTheme.SizeLabel, new Vector2(470, y + 8), "NOTHING ORDERED", C.Dim);
        foreach (Job job in _queue.Orders.Take(3))
        {
            Row(new Rect2(470, y, 774, 56), job, C.Accent);
            y += 64;
        }

        // What the constructor added by itself.
        y = 410;
        this.Caption(new Vector2(470, y - 12), "AUTO SUB-PARTS  -  ADDED FOR YOU", C.Good);
        y += 36;
        if (_queue.SubParts.Count == 0) this.Text(UiTheme.Label, UiTheme.SizeLabel, new Vector2(470, y + 8), "NONE NEEDED", C.Dim);
        foreach (Job job in _queue.SubParts.Take(3))
        {
            Row(new Rect2(500, y, 744, 56), job, C.Good);
            // A connector line from the queue rail: this part is for something above.
            this.Block(new Rect2(478, y - 8, 4, 36), C.Good.Darkened(0.4f));
            this.Block(new Rect2(478, y + 24, 22, 4), C.Good.Darkened(0.4f));
            y += 64;
        }
        if (_queue.SubParts.Count > 3)
            this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(500, y), $"+{_queue.SubParts.Count - 3} MORE", C.Dim);

        // Footer: what's missing, or where the parts come from.
        var foot = new Rect2(470, 650, 774, 52);
        if (_queue.Missing.Count > 0)
        {
            this.Block(foot, C.Danger);
            string need = string.Join("   ", _queue.Missing.Select(m => $"{m.Value} {Catalog.Get(m.Key).Name}"));
            this.TextIn(UiTheme.Label, UiTheme.SizeLabel, foot, "MISSING  " + need, C.Glass, padX: 20);
        }
        else
        {
            Inventory s = _queue.Storage;
            this.Block(foot, C.Panel);
            this.TextIn(UiTheme.Label, UiTheme.SizeSmall, foot, $"PARTS FROM: {s.Name}", C.Dim, padX: 20);
            this.TextIn(UiTheme.Mono, UiTheme.SizeSmall, foot, $"{s.TotalVolumeL:0} / {s.VolumeCapL:0} L", C.Text, HorizontalAlignment.Right, 20);
        }
    }

    void Row(Rect2 r, Job job, Color colour)
    {
        this.Block(r, C.Panel);
        this.Block(new Rect2(r.Position, new Vector2(10, r.Size.Y)), colour);
        if (job.Auto && job.Feeds != null)
        {
            // Two lines: the part, and which of your orders it is for.
            this.Text(UiTheme.Label, UiTheme.SizeLabel - 2, r.Position + new Vector2(26, 3), job.Item.Name, C.Text);
            this.Text(UiTheme.Label, 16, r.Position + new Vector2(26, 33), "FOR " + job.Feeds.Item.Name, C.Dim);
        }
        else this.TextIn(UiTheme.Label, UiTheme.SizeLabel, r, job.Item.Name, C.Text, padX: 26);
        this.TextIn(UiTheme.Mono, UiTheme.SizeLabel, new Rect2(r.Position.X + 260, r.Position.Y, 100, r.Size.Y),
            $"{job.Done}/{job.Count}", C.Text);

        var right = new Rect2(r.Position.X + 370, r.Position.Y, r.Size.X - 386, r.Size.Y);
        if (job.Running)
        {
            this.Segments(new Rect2(right.Position.X, right.Position.Y + 16, right.Size.X - 20, 24), job.Progress, 1, colour, 0, 12, 4);
        }
        else
        {
            string missing = job.Recipe.Inputs.Where(i => _queue.Missing.ContainsKey(i.Item.Id)).Select(i => i.Item.Name).FirstOrDefault();
            (string text, Color c) = missing != null ? ($"NEEDS {missing}", C.Danger)
                : _queue.CanStart(job) ? ("NEXT", C.Accent2)
                : job.Auto ? ("QUEUED", C.Dim) : ("WAITING FOR PARTS", C.Dim);
            this.TextIn(UiTheme.Label, UiTheme.SizeSmall + 2, right, text, c, HorizontalAlignment.Right, 16);
        }
    }
}
