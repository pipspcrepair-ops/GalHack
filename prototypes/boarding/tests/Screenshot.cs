using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Boarding;

/// <summary>
/// Renders one staged view of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/boarding res://tests/Screenshot.tscn -- --view=exterior --out=shot.png
/// Views: exterior, firefight, hijack, derelict.
/// </summary>
public partial class Screenshot : Node
{
    Main _main;
    string _view = "exterior";
    string _out = "shot.png";

    Ship Ship => _main.Ship;
    Player Player => _main.Player;
    ShipLayout Layout => _main.Layout;

    public override async void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        GD.Seed(1977);
        _main = new Main { Name = "Main" };
        AddChild(_main);
        await Ticks(2);
        Player.TestInvulnerable = true;

        switch (_view)
        {
            case "exterior": await Exterior(); break;
            case "firefight": await Firefight(); break;
            case "hijack": await Hijack(); break;
            case "derelict": await Derelict(); break;
            case "perf": await Perf(); break;
        }
        await Capture();
    }

    /// <summary>
    /// An establishing shot: the frigate's port flank, its turret laying a telegraph laser on the
    /// player out at the landing site. A separate camera, so the turret is seen side-on.
    /// </summary>
    async Task Exterior()
    {
        Player.DebugPlace(Ship.ToGlobal(new Vector3(-40f, 0.05f, 22f)));
        Player.LookAt2(Ship.Turret.GlobalPosition);
        var cam = new Camera3D { Fov = 62f, Far = 4000f };
        AddChild(cam);
        Vector3 eye = Ship.ToGlobal(new Vector3(-37f, 1.6f, -19f));
        cam.GlobalTransform = Effects.Facing(eye, Ship.ToGlobal(new Vector3(-21f, 4.2f, 2f)) - eye);
        cam.MakeCurrent();
        // Capture just after the first shell leaves the barrel: laser, flash and shell in flight.
        for (int i = 0; i < 60 * 8 && Ship.Turret.ShotsFired == 0; i++) await Ticks(1);
        await Ticks(2);
    }

    /// <summary>Not a picture: average frame time and draw calls at a few spots.</summary>
    async Task Perf()
    {
        _main.FreezeGuards = true;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);   // measure headroom, not the monitor
        var spots = new (string name, Vector3 at, Vector3 look)[]
        {
            ("outside", new Vector3(-40f, 0.05f, 22f), new Vector3(0f, 3f, 0f)),
            ("spine", Layout.CellCenter(new Vector2I(12, 20)), Layout.CellCenter(new Vector2I(12, 6)) + Vector3.Up * 1.5f),
            ("vault", Layout.CellCenter(new Vector2I(20, 13)), Layout.CellCenter(new Vector2I(24, 13)) + Vector3.Up * 1.5f),
        };
        foreach (var (name, at, look) in spots)
        {
            Player.DebugPlace(Ship.ToGlobal(at + Vector3.Up * 0.05f));
            Player.LookAt2(Ship.ToGlobal(look));
            await Seconds(1f);
            ulong start = Time.GetTicksUsec();
            int frames = 0;
            while (Time.GetTicksUsec() - start < 2_000_000)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                frames++;
            }
            double ms = (Time.GetTicksUsec() - start) / 1000.0 / frames;
            ulong draws = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            ulong objects = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
            GD.Print($"perf {name}: {ms:F2} ms/frame ({1000.0 / ms:F0} fps), {draws} draw calls, {objects} objects");
        }
    }

    /// <summary>Down the spine corridor, SMG hammering, Vharn troopers lasing back.</summary>
    async Task Firefight()
    {
        _main.Hud.Message("Gunfire! The crew converges.");
        Player.DebugPlace(Ship.ToGlobal(Layout.CellCenter(new Vector2I(12, 20)) + new Vector3(0.6f, 0.05f, 0f)));
        Player.LookAt2(Ship.ToGlobal(Layout.CellCenter(new Vector2I(13, 12)) + Vector3.Up * 1.3f));
        var spots = new[] { new Vector2I(12, 13), new Vector2I(13, 15), new Vector2I(13, 11) };
        var troopers = _main.Guards.Where(g => g.Type.Role == GuardRole.Trooper).Take(spots.Length).ToList();
        for (int i = 0; i < troopers.Count; i++)
        {
            troopers[i].Position = Layout.CellCenter(spots[i]) + Vector3.Up * 0.05f;
            troopers[i].ResetPhysicsInterpolation();
        }
        await Ticks(2);
        foreach (Guard g in troopers) g.DebugEngage();
        await Seconds(2.2f);
        // Hold the trigger for a moment so the recoil, tracers and sparks are mid-burst.
        Player.DebugSelectWeapon(0);
        for (int i = 0; i < 9; i++)
        {
            Player.DebugFire();
            await Ticks(4);
        }
        Player.DebugFire();
    }

    /// <summary>The core vault the moment the hijack lands: the surge has killed the lights.</summary>
    async Task Hijack()
    {
        foreach (Door door in Ship.Doors.ToList()) door.Unlock();
        Player.DebugPlace(Ship.ToGlobal(Layout.CellCenter(new Vector2I(20, 13)) + new Vector3(-0.5f, 0.05f, 0.6f)));
        Player.LookAt2(Ship.Core.GlobalPosition + Vector3.Up * 0.2f);
        Player.TestInteract = true;
        await Seconds(1.5f);
        for (int i = 0; i < 4000 && !_main.Hijack.Complete; i++) _main.Hijack.Tick(0.05f, true);
        await Seconds(0.35f);
    }

    /// <summary>Derelict: the core is dead, the ship dark, and the generator comes away whole.</summary>
    async Task Derelict()
    {
        _main.FreezeGuards = true;
        Ship.Core.Damage(1e6f, Ship.Core.GlobalPosition, Vector3.Up);
        Player.DebugPlace(Ship.ToGlobal(Layout.CellCenter(new Vector2I(6, 12)) + new Vector3(0.3f, 0.05f, 0.3f)));
        Player.LookAt2(Ship.Generator.GlobalPosition + Vector3.Up * 0.1f);
        Player.SetTorch(true);
        await Seconds(1.2f);
        Player.TestInteract = true;
        await Seconds(1.0f);
    }

    async Task Capture()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print($"fps at capture: {Engine.GetFramesPerSecond()}");
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }

    async Task Ticks(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    Task Seconds(float s) => Ticks(Mathf.CeilToInt(s * Engine.PhysicsTicksPerSecond));
}
