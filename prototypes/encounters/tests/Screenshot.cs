using System.Linq;
using Encounters.Director;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace Encounters;

/// <summary>
/// Renders one view of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/encounters res://tests/Screenshot.tscn -- --view=orbit --out=shot.png
/// Views: orbit, skirmish, distress, overlay, and jump (presses J and checks the zone changed).
///
/// Each view stages its events with the director's debug spawn (which skips the spawn rules,
/// so the camera can be pointed straight at them), then lets the director run normally.
/// </summary>
public partial class Screenshot : Node
{
    const int SetupTick = 3;
    const int CaptureTick = 200;

    Main _main;
    Encounter _follow;
    int _tick;
    string _view = "orbit";
    string _out = "shot.png";

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        ProcessPhysicsPriority = 100;
        _main = new Main();
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_tick == SetupTick) Setup();
        if (_follow != null) FlyAlongside(_follow);
        if (_tick == CaptureTick) Capture();
    }

    void Setup()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        EncounterDirector d = _main.Director;
        switch (_view)
        {
            case "orbit":
            {
                // A convoy crossing in front of Halcyon Station, traders coming in to dock, and the giant below.
                _main.Craft.Place(new Vector3(-760, 170, -700), new Vector3(-650, 120, -2500), 60f);
                d.DebugSpawn("veyra-orbit", "convoy", N(-796, 270, -1233), N(2268, 270, -1046));
                d.DebugSpawn("veyra-orbit", "traders-dock", N(500, 260, -1600), N(0, 0, -2600));
                d.DebugSpawn("veyra-orbit", "freighter-pass", N(-2600, 380, -3600), N(2200, 300, -3300));
                FastForward(d, 6);
                break;
            }
            case "skirmish":
            {
                // A Concord patrol tangling with Carrion Reavers, close enough to see the tracers.
                // From the cockpit, so the skiff doesn't hide the fight.
                _main.Craft.Cockpit = true;
                _main.Craft.Place(new Vector3(-80, 70, 60), new Vector3(150, 10, -330), 10f);
                Encounter fight = d.DebugSpawn("veyra-orbit", "patrol-vs-reavers", N(110, 30, -280), N(0, 0, 0));
                // Run the fight on until someone has been shot down, so a wreck is smoking.
                for (int i = 0; i < 1200 && !fight.Ships.Any(s => s.Wrecked); i++) d.Tick();
                FastForward(d, 1);
                _follow = fight;
                break;
            }
            case "distress":
            {
                // Out in the Ashen Drift: a distress call a few kilometres off, Kessari miners at work on the rocks.
                _main.EnterZone("drift-deep");
                _main.Craft.Place(new Vector3(1000, 150, -2200), new Vector3(1030, 110, -3200), 30f);
                d.DebugSpawn("drift-deep", "kessari-miners", N(1700, 60, -2900), N(0, 0, 0));
                d.DebugSpawn("drift-deep", "distress-call", N(-1000, 390, -5640), N(0, 0, 0));
                FastForward(d, 30);
                break;
            }
            case "jump":
            {
                // Through the real input path: J fades out, swaps zone, fades back in.
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.J, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.J, Pressed = false });
                break;
            }
            case "overlay":
            {
                // A lived-in orbit: a few minutes of the director left to its own devices, a ship
                // claimed and a cargo pod dropped, then the Tab overlay and the M map.
                _main.Craft.Place(new Vector3(0, 40, 600), new Vector3(0, 0, -2600), 0f);
                FastForward(d, 150);
                Encounter freighter = d.DebugSpawn("veyra-orbit", "freighter-pass", N(-600, 60, -300), N(800, 0, -900));
                WorldEntity ship = freighter.Ships[0];
                d.SetPlayer(Main.LocalPlayer, "veyra-orbit", ship.Position + N(0, 80, 0), N(0, 0, -1));
                d.Touch(Main.LocalPlayer, ship.Id);
                d.RegisterPlayerOwned(Main.LocalPlayer, "veyra-orbit", EntityKind.PlayerCargo, "cargo-pod", "Cargo pod", N(-200, 20, 300), N(0, 0, -1));
                _main.Overlay.Visible = true;
                _main.Map.Visible = true;
                break;
            }
        }
    }

    /// <summary>Runs the director on by some seconds before the shot, with the player where the camera is.</summary>
    void FastForward(EncounterDirector d, float seconds)
    {
        for (int i = 0; i < seconds * d.Config.TickHz; i++)
        {
            d.SetPlayer(Main.LocalPlayer, _main.ZoneId, _main.Camera.GlobalPosition.ToNum(), (-_main.Camera.GlobalBasis.Z).ToNum());
            d.Tick();
        }
    }

    /// <summary>
    /// Keeps the camera just behind and above a raider chasing a corvette, so the pair and the
    /// tracers between them fill the frame. The camera is the player's view, so the director
    /// sees the player flying alongside the fight.
    /// </summary>
    void FlyAlongside(Encounter fight)
    {
        EntityView chaser = null, quarry = null;
        float best = float.MaxValue;
        foreach (WorldEntity raider in fight.Ships.Where(s => s.Live && s.Side == "b"))
            foreach (WorldEntity corvette in fight.Ships.Where(s => s.Live && s.Side == "a"))
            {
                float gap = NVector3.Distance(raider.Position, corvette.Position);
                if (gap >= best || !_main.Views.TryGetValue(raider.Id, out var r) || !_main.Views.TryGetValue(corvette.Id, out var c)) continue;
                (best, chaser, quarry) = (gap, r, c);
            }
        if (chaser == null) return;
        Vector3 ahead = (quarry.GlobalPosition - chaser.GlobalPosition).Normalized();
        Vector3 eye = chaser.GlobalPosition - ahead * 70f + chaser.GlobalBasis.Y * 22f + chaser.GlobalBasis.X * 18f;
        Vector3 look = chaser.GlobalPosition.Lerp(quarry.GlobalPosition, 0.45f);
        _main.Craft.CameraOverride = new Transform3D(Basis.LookingAt((look - eye).Normalized(), Vector3.Up), eye);
    }

    static NVector3 N(float x, float y, float z) => new(x, y, z);

    async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        if (_view == "jump" && (_main.ZoneId != "drift-deep" || _main.Director.Instance("veyra-orbit") != null))
        {
            GD.PrintErr($"jump failed: in {_main.ZoneId}");
            result = Error.Failed;
        }
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        ZoneInstance zi = _main.Director.Instance(_main.ZoneId);
        GD.Print($"  {_main.Views.Count} views, {zi?.Encounters.Count} events ({string.Join(", ", zi?.Encounters.Select(e => $"{e.Def.Id} {e.State}") ?? Enumerable.Empty<string>())}), {_main.Fx.BoltsInFlight} bolts in flight");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
