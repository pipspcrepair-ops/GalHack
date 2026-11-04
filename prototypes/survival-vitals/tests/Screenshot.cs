using Godot;
using SurvivalVitals.Model;

namespace SurvivalVitals;

/// <summary>
/// Renders one view of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/survival-vitals res://tests/Screenshot.tscn -- --view=frozen --out=shot.png
/// Views: frozen, replicator, shelter, backpack, death.
/// </summary>
public partial class Screenshot : Node
{
    const int SetupTick = 2;
    const int CaptureTick = 200;

    Main _main;
    int _tick;
    string _view = "frozen";
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
        if (_tick == CaptureTick) Capture();
    }

    void At(double hour) =>
        _main.Sim.AdvanceWorld(((hour - _main.Sim.Data.Planet.StartHour + 24) % 24) / 24 * _main.Sim.Data.Planet.DayLengthSeconds);

    void Setup()
    {
        SurvivalSim sim = _main.Sim;
        _main.ClearMessages();
        switch (_view)
        {
            case "frozen":
                // Some seconds into the valley in a light suit: body temperature has crossed the
                // warning line, so the visor flashes, but health has not started to drain.
                At(11.5);
                _main.DebugPlace(-58, -26, 52f, 4f);
                sim.Player.Gear.HelmetOn = true;
                sim.Player.Vitals.BodyTemperature = 35.95;
                _main.Hud.FreezeFlash = true;
                break;
            case "replicator":
                // Set down in the open by the crash site, then fed from: the prompt shows its keys.
                At(16.8);
                _main.DebugPlace(-9, 30, -32f, -16f);
                sim.Player.Vitals.Food = 260;
                Vector3 front = _main.Player.GlobalPosition + _main.Player.Forward * 3.1f;
                _main.Act(sim.DeployOrPack(Main.ToPos(front)));
                _main.Act(sim.Feed(sim.Replicators[^1]));
                sim.Player.Vitals.Food = 380;
                break;
            case "shelter":
                At(13);
                _main.DebugPlace(31.2, -3.4, 47f, -8f);
                _main.Act(sim.SetHome(sim.RespawnPoints.Find(p => p.Kind == RespawnKind.CloneChamber)));
                break;
            case "backpack":
                At(18.6);
                sim.Player.Position = Pos.Flat(-36, -2);
                sim.Kill();
                break;
            case "death":
                At(15);
                _main.DebugPlace(-25, -80, 180f, -8f);
                sim.Kill();
                break;
        }
    }

    async void Capture()
    {
        if (_view == "backpack")
        {
            // Respawn at the tent a few ticks before capture: the player turns to face the beam.
            _main.RespawnAt(RespawnChoice.NearestTent);
            for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
