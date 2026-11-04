using System.Linq;
using System.Threading.Tasks;
using Godot;
using Invasions.Combat;
using Invasions.Director;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Tests;

/// <summary>
/// Renders one moment of an invasion to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/invasions res://tests/Screenshot.tscn -- --view=warning --out=shot.png
/// Views: warning, arrival, firefight, offline, overview.
/// Long stretches of the fight are simulated faster than real time (same 1/60 s physics steps,
/// more of them per second) so a shot doesn't take minutes.
/// </summary>
public partial class Screenshot : Node
{
    Main _main;
    string _view = "warning";
    string _out = "shot.png";

    public override async void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        _main = new Main();
        AddChild(_main);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        await PhysicsFrames(3);

        switch (_view)
        {
            case "warning": await Warning(); break;
            case "arrival": await Arrival(); break;
            case "firefight": await Firefight(); break;
            case "offline": await Offline(); break;
            case "probe": await Probe(); break;
            default: await Overview(); break;
        }
        await Capture();
    }

    InvasionController C => _main.Controller;
    Player P => _main.Player;

    Vector3 Cell(int x, int z) => _main.Combat.CellCentre(_main.Model.Grid.FromLayout(x, z));

    async Task Warning()
    {
        // Inside the base by the main console as the first warning comes in.
        Vector3 console = Cell(14, 4);
        P.Teleport(console + new Vector3(-1.2f, 0.1f, 5.2f), 0f, 0f);
        C.InvadeNow();
        await Seconds(1.5f, 1f);
        P.LookAtPoint(console + new Vector3(0.4f, 1.6f, 0));
        await Seconds(0.5f, 1f);
    }

    async Task Arrival()
    {
        // Out past the walls as the first dropship sets down and the APCs roll in behind it.
        C.InvadeNow();
        C.SkipToArrival();
        C.SpawnPendingNow();
        P.Invulnerable = true;
        _main.Combat.TurretsEnabled = false;
        CrewedVehicle lead = null;
        for (int i = 0; i < 60; i++)
        {
            await Seconds(0.5f, 4f);
            // Wait for the ramp to be down and the first troops out.
            lead = C.World.Vehicles.OfType<CrewedVehicle>().FirstOrDefault(v => v.Phase == CrewedVehicle.Mode.Unload && v.TroopsAboard <= v.Plan.Troops.Count - 5);
            if (lead != null) break;
        }
        lead ??= C.World.Vehicles.OfType<CrewedVehicle>().First();
        Vector3 target = lead.GlobalPosition;
        Vector3 toBase = (C.World.BaseCentre - target) with { Y = 0 };
        toBase = toBase.Normalized();
        var side = new Vector3(-toBase.Z, 0, toBase.X);
        // Side on, so the open ramp and the troops coming off it show.
        P.Teleport(target + side * 17f + toBase * 15f + Vector3.Up * 0.1f, 0f, 0f);
        P.LookAtPoint(target + toBase * 2f + Vector3.Up * 2.5f);
        await Seconds(0.35f, 1f);
    }

    async Task Firefight()
    {
        // On top of the north wall as the assault reaches it.
        C.InvadeNow();
        C.SkipToArrival();
        C.SpawnPendingNow();
        P.Invulnerable = true;
        // About when the squad reaches the north wall (see the probe view's log).
        for (int i = 0; i < 60 && !C.World.Troops.OfType<PlaceholderSoldier>().Any(t => t.IsAlive && t.AtBreach); i++)
            await Seconds(2f, 6f);
        await Seconds(2.5f, 2f);
        PlaceholderSoldier breacher = C.World.Troops.OfType<PlaceholderSoldier>().FirstOrDefault(t => t.IsAlive && t.AtBreach)
                                      ?? C.World.Troops.OfType<PlaceholderSoldier>().FirstOrDefault(t => t.IsAlive && t.BreachTarget != null);
        Vector3 breach = breacher?.BreachTarget is Block b ? _main.Combat.CellCentre(b.Cell) : Centroid();
        P.Teleport(WallTopAbout(breach, 12f), 0f, 0f);
        P.LookAtPoint(breach + Vector3.Up * 0.5f);
        await Seconds(0.9f, 1f);
        P.LookAtPoint((breach + Centroid()) / 2 + Vector3.Up * 0.5f);
        await Seconds(0.1f, 1f);
    }

    Vector3 Centroid()
    {
        var alive = C.World.Troops.Where(t => t.IsAlive && !t.IsGone).Select(t => t.Body.GlobalPosition).ToList();
        return alive.Count == 0 ? Cell(9, -8) : alive.Aggregate(Vector3.Zero, (a, b) => a + b) / alive.Count;
    }

    /// <summary>The top of a standing outer wall block about <paramref name="distance"/> metres from a point.</summary>
    Vector3 WallTopAbout(Vector3 point, float distance)
    {
        BaseGrid grid = _main.Model.Grid;
        Block wall = grid.Alive.Where(b => b.Kind == BlockKind.Wall)
            .Where(b => { Cell l = grid.ToLayout(b.Cell); return l.X == 0 || l.Z == 0 || l.Z == 12 || l.X == 17; })
            .OrderBy(b => Mathf.Abs(_main.Combat.CellCentre(b.Cell).DistanceTo(point with { Y = 0 }) - distance)).First();
        return _main.Combat.CellCentre(wall.Cell) + Vector3.Up * ((float)_main.Model.Data.WallHeight + 0.15f);
    }

    async Task Offline()
    {
        C.InvadeNow();
        C.SkipToArrival();
        C.SpawnPendingNow();
        P.Invulnerable = true;
        await Seconds(40f, 6f);
        // The core falls; the base goes dark and the invaders pull out.
        Block core = _main.Model.Grid.Core;
        _main.Model.Damage(core, 1e6);
        await Seconds(7f, 2f);
        P.Teleport(Cell(4, 0) + Vector3.Up * ((float)_main.Model.Data.WallHeight + 0.15f), 0f, 0f);
        P.LookAtPoint(Cell(9, 6) + Vector3.Up * 1f);
        await Seconds(1.0f, 1f);
    }

    async Task Probe()
    {
        C.InvadeNow();
        C.SkipToArrival();
        C.SpawnPendingNow();
        P.Invulnerable = true;
        for (int i = 0; i < 34; i++)
        {
            await Seconds(5f, 8f);
            var troops = C.World.Troops.OfType<PlaceholderSoldier>().Where(t => t.IsAlive).ToList();
            var dead = _main.Model.Grid.Blocks.Where(b => !b.Alive).Select(b => $"{b.Kind}@{_main.Model.Grid.ToLayout(b.Cell)}");
            GD.Print($"t={5 * (i + 1)}s alive={troops.Count} breaching={troops.Count(t => t.BreachTarget != null)} inCoreRoom={troops.Count(t => t.InCoreRoom)} " +
                     $"core={(_main.Model.Grid.Core?.Health01 ?? 0):F2} phase={C.Current?.Phase} dead=[{string.Join(" ", dead)}]");
            foreach (var t in troops.Take(4)) GD.Print($"   {t.Role} at {_main.Model.Grid.ToLayout(_main.Combat.CellAt(t.GlobalPosition))} target={(t.BreachTarget == null ? "-" : t.BreachTarget.Kind.ToString())}");
        }
    }

    async Task Overview()
    {
        P.Teleport(new Vector3(-22f, 0.1f, 30f), 0f, 0f);
        P.LookAtPoint(new Vector3(0, 0, 0));
        await Seconds(1f, 1f);
    }

    async Task PhysicsFrames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>Runs game time forward at <paramref name="speed"/>x real time, keeping 1/60 s physics steps.</summary>
    async Task Seconds(float seconds, float speed)
    {
        Engine.TimeScale = speed;
        Engine.PhysicsTicksPerSecond = Mathf.RoundToInt(60 * speed);
        Engine.MaxPhysicsStepsPerFrame = Mathf.Max(8, Mathf.CeilToInt(speed * 2));
        await PhysicsFrames(Mathf.RoundToInt(seconds * 60));
        Engine.TimeScale = 1;
        Engine.PhysicsTicksPerSecond = 60;
    }

    async Task Capture()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
