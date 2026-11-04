using Godot;

namespace Saves.Tests;

/// <summary>
/// Renders one view of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/saves res://tests/Screenshot.tscn -- --view=base --out=shot.png --save-dir=DIR [--new]
/// Views: base (the surface base), tunnel (the climb), orbit (the planet below), recovery (the
/// HUD after a chaos kill: run the game with --chaos-after=2 on the same --save-dir first).
/// </summary>
public partial class Screenshot : Node
{
    const int SetupTick = 3;

    Main _main;
    int _tick, _captureTick = 150;
    string _view = "base";
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

    // The re-entry view: climb to orbit, then dive back in over a chosen spot, and check where it lands.
    static readonly Vector2 ReentrySpot = new(300f, -200f);
    int _reentryStage;

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_tick == SetupTick) Setup();
        string zone = _main.Zones.Active?.ZoneId;
        if (_view == "orbit" && _tick > SetupTick && zone != GalHack.Records.ZoneIds.Orbit) _captureTick = _tick + 150;
        if (_view == "tunnel" && _tick == SetupTick + 50) _main.Hud.Tunnel.SetTime(1.3f);
        if (_view == "reentry" && _tick > SetupTick) Reentry(zone);
        if (_tick == _captureTick) Capture();
    }

    void Reentry(string zone)
    {
        Ship ship = _main.Ship;
        if (_reentryStage == 0 && zone == GalHack.Records.ZoneIds.Orbit && !ship.Frozen)
        {
            // In orbit: put the ship just above the atmosphere over the spot, diving.
            ship.Position = Places.OrbitPointAbove(ReentrySpot.X, ReentrySpot.Y, Places.OrbitEdgeAltitude + 5f);
            ship.Velocity = -Places.OrbitUp(ship.Position) * 40f;
            _reentryStage = 1;
        }
        else if (_reentryStage == 1 && zone == GalHack.Records.ZoneIds.Surface)
        {
            var landed = new Vector2(ship.Position.X, ship.Position.Z);
            GD.Print($"re-entered over ({ReentrySpot.X:F0}, {ReentrySpot.Y:F0}) and came out over ({landed.X:F1}, {landed.Y:F1}) at {ship.Position.Y:F0} m: {landed.DistanceTo(ReentrySpot):F1} m off");
            _reentryStage = 2;
            _captureTick = _tick + 40;
        }
        else if (_reentryStage < 2) _captureTick = _tick + 2;
    }

    void Setup()
    {
        Player player = _main.Player;
        Ship ship = _main.Ship;
        switch (_view)
        {
            case "base":
                // Standing on the ship's roof, looking back over the base.
                player.SpawnOutside(new Vector3(1.6f, 5.6f, 12.1f), 3.03f);
                player.DebugSetLook(3.03f, -0.4f);
                break;
            case "tunnel":
                player.DebugSit();
                ship.Position = new Vector3(0, 605f, 0);
                _captureTick = SetupTick + 55;
                break;
            case "reentry":
                player.DebugSit();
                ship.Position = new Vector3(0, 605f, 0);
                break;
            case "orbit":
                player.DebugSit();
                player.ThirdPerson = true;
                player.DebugSetLook(0.42f, -0.22f);
                ship.Position = new Vector3(0, 605f, 0);
                break;
            case "recovery":
                player.ThirdPerson = false;
                player.SpawnAboard(ship, new Vector3(2.6f, 0.05f, 3.5f), 2.55f);
                player.DebugSetLook(2.55f, -0.12f);
                _captureTick = 120;
                break;
        }
    }

    async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
