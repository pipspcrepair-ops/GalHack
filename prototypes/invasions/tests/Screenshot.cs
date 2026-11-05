using System.Collections.Generic;
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
/// Views: warning, arrival, firefight, offline, overview, and faction (with --faction=kessari|meridian|reavers|hollow|vharn).
/// Long stretches of the fight are simulated faster than real time (same 1/60 s physics steps,
/// more of them per second) so a shot doesn't take minutes.
/// </summary>
public partial class Screenshot : Node
{
    Main _main;
    string _view = "warning";
    string _out = "shot.png";
    string _faction = "kessari";

    public override async void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
            else if (arg.StartsWith("--faction=")) _faction = arg["--faction=".Length..];
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
            case "faction": await FactionInvasion(); break;
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

    // ---- One faction's invasion ----------------------------------------------------------

    /// <summary>
    /// The chosen faction invades: it is made hostile if it isn't, every other faction's base is knocked
    /// out, and the camera finds the moment that shows its troops, vehicles and tactics.
    /// </summary>
    async Task FactionInvasion()
    {
        InvasionDirector d = _main.Director;
        if (!d.Standings.Invades(_faction)) d.Wrong(_faction, 200, deliberate: true);
        foreach (EnemyBaseNode b in _main.EnemyBases.Where(b => b.Faction != _faction)) b.DestroyCore();
        await Seconds(0.1f, 1f);
        C.InvadeNow();
        C.SkipToArrival();
        C.SpawnPendingNow();
        P.Invulnerable = true;
        switch (_faction)
        {
            case "kessari": await KessariShot(); break;
            case "meridian": await MeridianShot(); break;
            case "reavers": await ReaversShot(); break;
            case "hollow": await HollowShot(); break;
            default: await Arrival(); break;
        }
    }

    IEnumerable<PlaceholderSoldier> Troops => C.World.Troops.OfType<PlaceholderSoldier>().Where(t => IsInstanceValid(t) && t.IsAlive);

    /// <summary>A strider has walked in and is setting down its frames and hovering cutters.</summary>
    async Task KessariShot()
    {
        _main.Combat.TurretsEnabled = false;
        Strider strider = null;
        for (int i = 0; i < 240; i++)
        {
            await Seconds(0.25f, 4f);
            strider = C.World.Vehicles.OfType<Strider>().FirstOrDefault(v => v.Unloaded >= 4);
            if (strider != null) break;
        }
        strider ??= C.World.Vehicles.OfType<Strider>().First();
        await Seconds(0.6f, 1f);
        Vector3 at = strider.GlobalPosition;
        Vector3 toBase = ((C.World.BaseCentre - at) with { Y = 0 }).Normalized();
        var side = new Vector3(-toBase.Z, 0, toBase.X);
        // Off its flank, between it and the base: the walker, and its troops heading for the walls.
        P.Teleport(at + side * 11f + toBase * 10f + Vector3.Up * 0.1f, 0f, 0f);
        P.LookAtPoint(at + toBase * 4f + Vector3.Up * 3.2f);
        await Seconds(0.2f, 1f);
    }

    /// <summary>Shielded officers stacked at the wall while the breaching team's charge blinks.</summary>
    async Task MeridianShot()
    {
        bool Ready() => _main.Combat.GetChildren().OfType<BreachCharge>().Any() && Troops.Any(t => t.Role != "breacher" && t.LongestStackUp > 1f);
        for (int i = 0; i < 480 && !Ready(); i++) await Seconds(0.25f, 6f);
        BreachCharge charge = _main.Combat.GetChildren().OfType<BreachCharge>().FirstOrDefault();
        Vector3 breach = charge?.GlobalPosition ?? Centroid();
        Vector3 outward = ((breach - C.World.BaseCentre) with { Y = 0 }).Normalized();
        var side = new Vector3(-outward.Z, 0, outward.X);
        // The turrets are firing at the stack; so the shields show in this one frame, each officer near
        // the breach also takes a one-point hit, as if a round had just landed.
        foreach (PlaceholderSoldier t in Troops.Where(t => t.GlobalPosition.DistanceTo(breach) < 9f)) t.TakeDamage(1f, breach, Team.Defenders);
        // On the ground outside, off to one side, looking at the charge on the wall and the team stacked beside it.
        P.Teleport(breach with { Y = 0.1f } + outward * 9f + side * 5f, 0f, 0f);
        P.LookAtPoint(breach with { Y = 1.2f } + outward * 2f);
        await Seconds(0.08f, 1f);
    }

    /// <summary>Hover bikes come in hard and close, a gunship behind them, raiders jumping off.</summary>
    async Task ReaversShot()
    {
        _main.Combat.TurretsEnabled = false;
        CrewedVehicle bike = null;
        for (int i = 0; i < 120; i++)
        {
            await Seconds(0.25f, 3f);
            bike = C.World.Vehicles.OfType<HoverBike>().FirstOrDefault(v => v.Phase == CrewedVehicle.Mode.Unload || v.Unloaded > 0);
            if (bike != null) break;
        }
        bike ??= C.World.Vehicles.OfType<CrewedVehicle>().First();
        await Seconds(0.5f, 1f);
        Vector3 target = bike.GlobalPosition;
        Vector3 toBase = ((C.World.BaseCentre - target) with { Y = 0 }).Normalized();
        var side = new Vector3(-toBase.Z, 0, toBase.X);
        // Between the bikes and the base, a little to the side, looking back out at the raid coming in.
        P.Teleport(target + toBase * 14f + side * 7f + Vector3.Up * 0.1f, 0f, 0f);
        P.LookAtPoint(target - toBase * 25f + Vector3.Up * 3f);
        await Seconds(0.3f, 1f);
    }

    /// <summary>The growth has broken the surface and the swarm is pouring out of its maw.</summary>
    async Task HollowShot()
    {
        _main.Combat.TurretsEnabled = false;
        Growth growth = C.World.Vehicles.OfType<Growth>().FirstOrDefault();
        for (int i = 0; i < 60 && growth != null && growth.Unloaded < 9; i++) await Seconds(0.25f, 2f);
        await Seconds(1.5f, 1f);
        Vector3 at = growth?.GlobalPosition ?? Centroid();
        Vector3 toBase = ((C.World.BaseCentre - at) with { Y = 0 }).Normalized();
        var side = new Vector3(-toBase.Z, 0, toBase.X);
        // In front of the maw and off to one side: the swarm running past toward the walls.
        P.Teleport(at + toBase * 19f + side * 11f + Vector3.Up * 0.1f, 0f, 0f);
        P.LookAtPoint(at + toBase * 5f + Vector3.Up * 1.8f);
        await Seconds(0.3f, 1f);
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
