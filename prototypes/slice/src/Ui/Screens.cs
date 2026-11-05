using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Screens;
using DiegeticUi.Theme;
using GalHack.Items;
using GalHack.Items.Production;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using ShipHandling.Handling;
using Slice.Construction;
using Slice.Surface;
using Slice.Vessels;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>A page that redraws only when what it shows has changed (the diegetic UI's rule: idle screens cost nothing).</summary>
public abstract partial class SlicePage : Control, IScreenPage
{
    protected static Palette C => UiTheme.Colors;
    string _shown = "";

    /// <summary>A short text that changes whenever the page would look different.</summary>
    protected abstract string Signature();

    public bool Refresh()
    {
        string now = Signature();
        if (now == _shown) return false;
        _shown = now;
        QueueRedraw();
        return true;
    }
}

/// <summary>
/// The cockpit console: a screen in front of the pilot's seat (the diegetic UI's in-world
/// screen) with the vessel's four readable numbers (acceleration per direction, turn rate, top
/// speed, lift margin) and what it is doing right now. It follows whichever seat you are in.
/// </summary>
public partial class CockpitConsole : Node3D
{
    public Game.Game Game;
    public WorldScreen Screen { get; private set; }
    ConsolePage _page;
    Vessel _on;

    public override void _Ready()
    {
        Screen = new WorldScreen { Name = "Screen", Pixels = new Vector2I(960, 600), SizeMeters = new Vector2(0.48f, 0.30f), Pickable = false, MovesCamera = false, Bezel = 0.02f };
        AddChild(Screen);
        _page = new ConsolePage();
        Screen.SetContent(_page);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        Vessel seated = Game.Survivor?.Seated;
        if (seated != _on)
        {
            _on = seated;
            _page.Vessel = seated;
            if (seated != null)
            {
                Reparent(seated.Node, false);
                // Low in front of the seat, tilted back toward the pilot's eye.
                Vector3 seat = seated.Seat.Value.ToGodot();
                Transform = new Transform3D(new Basis(Vector3.Right, -0.55f), seat + new Vector3(0f, 0.62f, -0.62f));
            }
            Visible = seated != null;
            Screen.MarkDirty();
        }
    }

    /// <summary>The console's page.</summary>
    public sealed partial class ConsolePage : SlicePage
    {
        public Vessel Vessel;

        protected override string Signature()
        {
            if (Vessel?.Stats is not HandlingStats st) return Vessel == null ? "none" : "pod";
            VesselController c = Vessel.Controller;
            return $"{st.Mass:F1}|{st.LiftMargin:F0}|{Vessel.Body.Velocity.Length():F0}|{Vessel.Body.Position.Y:F0}|{c?.Gear?.Deployed}|{c?.Frozen}|{Vessel.Flight.State.Cruise}|{c?.ConsoleMessage}";
        }

        public override void _Draw()
        {
            float w = Size.X;
            this.Block(new Rect2(Vector2.Zero, Size), C.Glass);
            if (Vessel == null) return;
            HandlingStats st = Vessel.Stats;
            if (st == null || Vessel.Spec?.Thrusters.Count == 0)
            {
                this.Header(w, Vessel.Name.ToUpperInvariant(), "BEACON", C.Accent2);
                this.Text(UiTheme.Label, UiTheme.SizeLabel, new Vector2(30, 120), "Distress beacon transmitting.", C.Text);
                this.Text(UiTheme.Label, UiTheme.SizeLabel, new Vector2(30, 160), "No engines: this pod came down once.", C.Dim);
                this.Text(UiTheme.Label, UiTheme.SizeLabel, new Vector2(30, 200), "Locker: E at the yellow container.", C.Dim);
                return;
            }
            this.Header(w, Vessel.Name.ToUpperInvariant(), st.ClassLabel.ToUpperInvariant());
            float y = 112;
            void Row(string label, string value, Color colour)
            {
                this.Text(UiTheme.Label, UiTheme.SizeLabel, new Vector2(30, y), label, C.Dim);
                this.Text(UiTheme.Mono, 30, new Vector2(330, y - 2), value, colour);
                y += 46;
            }
            Row("ACCEL F / B", $"{st.Accel(Direction.Forward):F1} / {st.Accel(Direction.Back):F1} m/s²", C.Text);
            Row("ACCEL UP / SIDE", $"{st.Accel(Direction.Up):F1} / {st.Accel(Direction.Right):F1} m/s²", C.Text);
            Row("TURN", $"{st.TurnRate:F0} °/s", C.Text);
            Row("TOP SPEED", $"{st.TopSpeed:F0} m/s", C.Text);
            Row("LIFT MARGIN", $"{st.LiftMargin:+0;-0} kN ({st.SpareLift:F1} t spare)", st.CanLiftOff ? C.Good : C.Danger);
            Row("MASS", $"{st.Mass:F2} t", C.Text);
            y += 6;
            this.Block(new Rect2(0, y, w, 4), C.Rule);
            y += 18;
            VesselController c = Vessel.Controller;
            string state = c == null ? "" : c.Frozen ? "LANDED" : c.Gear?.Deployed == true ? "GEAR DOWN" : "FLYING";
            this.Text(UiTheme.Mono, 28, new Vector2(30, y), $"{Vessel.Body.Velocity.Length():F0} m/s   ALT {Vessel.Body.Position.Y:F0} m   {state}{(Vessel.Flight.State.Cruise ? "  CRUISE" : "")}", C.Text);
            string message = c?.ConsoleMessageAge < 4f ? c.ConsoleMessage : st.LiftWarning;
            if (!string.IsNullOrEmpty(message)) this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(30, y + 44), message, C.Warn);
        }
    }
}

/// <summary>
/// The wrist computer (Tab): a screen on the forearm with the pack (building materials,
/// organics, food, kit), the starter blueprint's cost against what you have, and the scanner's
/// nearest deposits. You can keep walking while it is up.
/// </summary>
public partial class Wrist : Node3D
{
    public Game.Game Game;
    public WorldScreen Screen { get; private set; }
    public bool Raised { get; private set; }
    float _raise;
    WristPage _page;

    public override void _Ready()
    {
        Screen = new WorldScreen { Name = "Screen", Pixels = new Vector2I(1024, 680), SizeMeters = new Vector2(0.30f, 0.20f), Pickable = false, MovesCamera = false, Bezel = 0.012f };
        AddChild(Screen);
        _page = new WristPage { Game = Game };
        Screen.SetContent(_page);
        Visible = false;
    }

    public void Toggle()
    {
        Raised = !Raised;
        Screen.MarkDirty();
    }

    public override void _Process(double delta)
    {
        _raise = Mathf.MoveToward(_raise, Raised ? 1f : 0f, (float)delta * 4f);
        Visible = _raise > 0.01f;
        float e = _raise * _raise * (3f - 2f * _raise);
        // Up from below the view to low in the left of it, its face turned to the eye.
        var at = new Vector3(-0.15f, -0.34f + 0.22f * e, -0.42f);
        Transform = new Transform3D(Basis.LookingAt(at.Normalized(), Vector3.Up), at);
    }

    public sealed partial class WristPage : SlicePage
    {
        public Game.Game Game;
        readonly DepositFinder _deposits = new();

        protected override string Signature()
        {
            if (Game?.Suit == null) return "";
            string near = string.Join(",", _deposits.Near(Game.Zone, Game.Survivor.WorldFeet).Select(d => $"{d.kind}{(int)(d.at - Game.Survivor.WorldFeet).Length() / 5}"));
            Job job = Game.Workshop.Current;
            return $"{Game.Pack.Version}|{Game.Pod.Cargo.Version}|{Game.Suit.Kit.Organics:F1}|{Game.Suit.Kit.Food.Count}|{Game.Builder.Remaining.Count}|{near}|{job?.Item.Id}{job?.Done}|{Game.Workshop.Version}";
        }

        public override void _Draw()
        {
            float w = Size.X;
            this.Block(new Rect2(Vector2.Zero, Size), C.Glass);
            if (Game?.Suit == null) return;
            ItemCatalogue items = Game.Zone.Data.Items;
            this.Header(w, "PACK", Game.Creative ? "CREATIVE" : "SURVIVAL", C.Accent, height: 70);
            float y = 86;
            var plan = Game.StarterPlan;
            Bill need = Game.Builder.RemainingCost(plan);
            this.Caption(new Vector2(28, y), $"THE {plan.Name.ToUpperInvariant()} STILL NEEDS (pack and pod together)");
            y += 40;
            int shown = 0;
            foreach (var (id, n) in need)
            {
                if (shown++ >= 7) break;
                int have = Game.Pack[id] + Game.Pod.Cargo[id];
                this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(28, y), items.Item(id).Name.ToUpperInvariant(), C.Text);
                this.Segments(new Rect2(300, y + 6, 300, 16), Math.Min(have, n), Math.Max(1, n), have >= n ? C.Good : C.Accent2, count: 12, gap: 4);
                this.Text(UiTheme.Mono, 20, new Vector2(620, y), $"{Math.Min(have, n),4} of {n}", have >= n ? C.Good : C.Dim);
                y += 30;
            }
            if (need.IsEmpty) { this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(28, y), "Nothing: it is built, or Creative builds free", C.Good); y += 30; }
            y += 8;
            Job job = Game.Workshop.Current;
            Bill missing = Game.Workshop.Missing;
            this.Caption(new Vector2(28, y), "POD CONSTRUCTOR");
            y += 40;
            string state = job != null ? $"Making {job.Item.Name.ToLowerInvariant()} {job.Done}/{job.Count}" : Game.Workshop.Busy ? "Waiting for materials" : "Idle: E at it to make the Skiff's parts";
            this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(28, y), state, job != null ? C.Good : C.Text);
            y += 28;
            if (!missing.IsEmpty) { this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(28, y), $"Short of {items.Describe(missing, 4)}", C.Warn); y += 28; }
            y += 6;
            this.Caption(new Vector2(28, y), "SCANNER: NEAREST DEPOSITS");
            y += 40;
            Vector3 feet = Game.Survivor.WorldFeet;
            foreach (var (kind, at) in _deposits.Near(Game.Zone, feet))
            {
                Vector3 to = at - feet;
                float bearing = Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2(to.X, -to.Z)), 360f);
                this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(28, y), $"{kind.ToUpperInvariant(),-8}", C.Holo);
                this.Text(UiTheme.Mono, 20, new Vector2(200, y), $"{new Vector2(to.X, to.Z).Length(),4:F0} m  bearing {bearing:000}°  depth {Math.Max(0, -to.Y):F0} m", C.Text);
                y += 28;
            }
        }
    }
}

/// <summary>
/// The pod's constructor screen, on the wall above it: what it is making, what it is short of,
/// and the pod's storage. Read-only; E at the constructor orders the starter vessel's parts.
/// </summary>
public partial class WorkshopScreen : Node3D
{
    public Game.Game Game;
    public WorldScreen Screen { get; private set; }

    public override void _Ready()
    {
        Screen = new WorldScreen { Name = "Screen", Pixels = new Vector2I(900, 560), SizeMeters = new Vector2(0.72f, 0.45f), Pickable = false, MovesCamera = false, Bezel = 0.02f };
        AddChild(Screen);
        Screen.SetContent(new WorkshopPage { Game = Game });
    }

    sealed partial class WorkshopPage : SlicePage
    {
        public Game.Game Game;

        protected override string Signature()
        {
            if (Game?.Workshop == null) return "";
            Job job = Game.Workshop.Current;
            return $"{Game.Workshop.Version}|{job?.Item.Id}|{job?.Done}|{(int)((job?.Progress ?? 0) * 10)}|{Game.Pod.Cargo.Version}";
        }

        public override void _Draw()
        {
            float w = Size.X;
            this.Block(new Rect2(Vector2.Zero, Size), C.Glass);
            Constructor fab = Game?.Workshop;
            if (fab == null) return;
            ItemCatalogue items = Game.Zone.Data.Items;
            this.Header(w, "CONSTRUCTOR", fab.Def.Tag, C.Accent2, height: 70);
            float y = 86;
            Job job = fab.Current;
            this.Text(UiTheme.Label, UiTheme.SizeLabel, new Vector2(24, y), job != null ? $"{job.Item.Name}  {job.Done}/{job.Count}" : fab.Busy ? "Waiting for materials" : "Idle", job != null ? C.Good : C.Text);
            y += 38;
            if (job != null) this.Segments(new Rect2(24, y, w - 48, 16), job.Progress, 1, C.Good, count: 20, gap: 3);
            y += 28;
            this.Caption(new Vector2(24, y), "ORDERS");
            y += 36;
            foreach (Job o in fab.Orders.Take(5))
            {
                this.Text(UiTheme.Mono, 20, new Vector2(24, y), $"{o.Item.Name,-18} {o.Done,3}/{o.Count}", C.Text);
                y += 26;
            }
            if (fab.SubParts.Count > 0) { this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(24, y), $"+ {fab.SubParts.Count} sub-part jobs it planned itself", C.Dim); y += 28; }
            Bill missing = fab.Missing;
            if (!missing.IsEmpty) { this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(24, y), $"Short of {items.Describe(missing, 3)}", C.Warn); y += 28; }
            y = Size.Y - 40;
            this.Text(UiTheme.Mono, 18, new Vector2(24, y), $"Lockers {fab.Storage.UsedSu:F0}/{fab.Storage.CapacitySu:F0} SU    E: make the Skiff's parts", C.Dim);
        }
    }
}

/// <summary>A text overlay of the numbers behind everything (F3), for testing and tuning.</summary>
public partial class DebugHud : CanvasLayer
{
    public Game.Game Game;
    Label _label;

    public override void _Ready()
    {
        Layer = 2;
        _label = new Label { Position = new Vector2(12, 120), Modulate = new Color(1, 1, 1, 0.9f) };
        _label.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (!Visible || Game?.Survivor == null) return;
        var z = Game.Zone;
        var b = Game.Survivor.Body;
        var lines = new List<string>
        {
            $"{Engine.GetFramesPerSecond()} fps   {z.Planet.Spec}   {z.Planet.Sky.Summary}",
            $"feet {b.WorldPosition.X:F1} {b.WorldPosition.Y:F1} {b.WorldPosition.Z:F1}   on floor {b.OnFloor}   frame {(b.Frame == null ? "world" : Game.Survivor.Aboard?.Name)}{(b.OnOuterHull ? " (hull)" : "")}",
            $"weather {z.Weather.Sim?.Dominant}   wind {z.Weather.Wind2D.Length():F1} m/s   time {z.Sky.TimeOfDay:F3}",
            $"exposure {Game.Suit.Last.Exposure}",
            $"terrain {z.Streamer.MeshedChunks} chunks, {z.Streamer.CollisionBodies} with collision, queue {z.Streamer.Pending}, main thread {z.Streamer.LastFrameMs:F2} ms",
            $"physics step {z.Host.LastStepMs:F2} ms, {z.Physics.Bodies.Count} bodies, creatures {z.Wildlife.Sim?.Creatures.Count}",
        };
        foreach (Vessel v in z.Vessels)
            lines.Add($"{v.Name}: {v.Structure.Count} blocks, {v.Body.Mass:F0} kg, {(v.Controller?.Frozen == true ? "landed" : "flying")} at {v.Body.Position.Y:F0} m, cargo {v.Cargo.UsedSu:F0} SU");
        _label.Text = string.Join("\n", lines);
    }
}
