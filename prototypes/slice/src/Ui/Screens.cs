using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Screens;
using DiegeticUi.Theme;
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
            string pack = string.Join(",", Game.Pack.Items.Select(kv => $"{kv.Key}{kv.Value}"));
            string near = string.Join(",", _deposits.Near(Game.Zone, Game.Survivor.WorldFeet).Select(d => $"{d.kind}{(int)(d.at - Game.Survivor.WorldFeet).Length() / 5}"));
            return $"{pack}|{Game.Suit.Kit.Organics:F1}|{Game.Suit.Kit.Food.Count}|{Game.Builder.Remaining.Count}|{near}";
        }

        public override void _Draw()
        {
            float w = Size.X;
            this.Block(new Rect2(Vector2.Zero, Size), C.Glass);
            if (Game?.Suit == null) return;
            this.Header(w, "PACK", Game.BuildMode == BuildMode.Creative ? "CREATIVE" : "SURVIVAL", C.Accent, height: 70);
            float y = 92;
            this.Caption(new Vector2(28, y), "MATERIALS (placeholder: iron, copper, silicon, carbon)");
            y += 44;
            var plan = Game.StarterPlan;
            IReadOnlyDictionary<string, int> need = Game.Builder.TotalCost(plan);
            foreach (string item in new[] { "iron", "copper", "silicon", "carbon" })
            {
                int have = Game.Pack[item];
                int needed = need.TryGetValue(item, out int n) ? n : 0;
                this.Text(UiTheme.Label, UiTheme.SizeLabel, new Vector2(28, y), item.ToUpperInvariant(), C.Text);
                this.Text(UiTheme.Mono, 26, new Vector2(200, y), $"{have,4}", have >= needed ? C.Good : C.Text);
                this.Segments(new Rect2(300, y + 8, 300, 18), Math.Min(have, needed), Math.Max(1, needed), have >= needed ? C.Good : C.Accent2, count: 12, gap: 4);
                this.Text(UiTheme.Mono, 20, new Vector2(620, y + 2), $"of {needed} for the {plan.Name}", C.Dim);
                y += 40;
            }
            y += 10;
            this.Caption(new Vector2(28, y), "SURVIVAL KIT");
            y += 44;
            var kit = Game.Suit.Kit;
            this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(28, y), $"Organics {kit.Organics:F1} kg   Food items {kit.Food.Count}   Bottles {kit.SmallBottles}+{kit.LargeBottles}   Medkits {kit.Medkits}   Replicator kits {kit.Kits.Count}", C.Text);
            y += 44;
            this.Caption(new Vector2(28, y), "SCANNER: NEAREST DEPOSITS");
            y += 44;
            Vector3 feet = Game.Survivor.WorldFeet;
            foreach (var (kind, at) in _deposits.Near(Game.Zone, feet))
            {
                Vector3 to = at - feet;
                float bearing = Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2(to.X, -to.Z)), 360f);
                this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(28, y), $"{kind.ToUpperInvariant(),-8}", C.Holo);
                this.Text(UiTheme.Mono, 22, new Vector2(200, y), $"{new Vector2(to.X, to.Z).Length(),4:F0} m  bearing {bearing:000}°  depth {Math.Max(0, -to.Y):F0} m", C.Text);
                y += 32;
            }
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
            lines.Add($"{v.Name}: {v.Structure.Count} blocks, {v.Body.Mass:F0} kg, {(v.Controller?.Frozen == true ? "landed" : "flying")} at {v.Body.Position.Y:F0} m, cargo {v.Cargo.Total}");
        _label.Text = string.Join("\n", lines);
    }
}
