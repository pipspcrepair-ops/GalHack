using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Planets.Gen;
using Slice.Game;
using Slice.Items;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Structure power, phase 1 (design section 13, owner 15 Nov): generators charge capacitors and every device draws from the
/// capacitors by what it is doing; idle draws nothing; the burn follows the load; generators take their fuel from core
/// storage, the least efficient first; with the capacitor empty and no fuel, everything stops. First on the model alone,
/// then on fresh random worlds: the starter Skiff, unpowered until fuel goes in its storage, climbs and hovers on it, its
/// thrusters drawing by throttle and its fuel burning as they draw; run dry in the air, it goes dark and falls; a hover
/// craft run dry sinks onto the ground. The jump fuels: gathered, refined, in core storage, a drive's reach read from it.
/// The start: the first core, generator, storage and fuel come before a climate node needs them.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/PowerTest.tscn [-Extra --pure]
/// </summary>
public partial class PowerTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    static System.Threading.Timer _watchdog;

    public override void _Ready()
    {
        _watchdog = new System.Threading.Timer(_ => System.Environment.Exit(3), null, TimeSpan.FromMinutes(12), System.Threading.Timeout.InfiniteTimeSpan);
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    bool Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
        return ok;
    }

    IEnumerable<int> Scenario()
    {
        GameData data = GameData.Shared;
        Model(data);
        Fuels(data);
        JumpFuels(data);
        Sun(data);
        Shield(data);
        Drives(data);
        Start(data);
        if (OS.GetCmdlineUserArgs().Contains("--pure")) yield break;

        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        GD.Print($"-- in the world: {_game.World} --");
        foreach (int x in Flight(data)) yield return x;
        foreach (int x in Hover(data)) yield return x;
        foreach (int x in Armed(data)) yield return x;
    }

    // ---- The model alone ------------------------------------------------------------------------------

    /// <summary>A structure from blueprint lines on the 1 m grid, a core at the origin.</summary>
    static Structure Build(GameData data, params string[] blocks)
    {
        string text = "schema galhack.blueprint\nversion 1\nname \"Power\"\ngrid 1m\nmode base\nblock 0 0 0 core\n" + string.Join("", blocks.Select(b => $"block {b}\n"));
        Structure s = Blueprint.Load(text, data.Catalogue, out List<string> errors);
        if (s == null || errors.Count > 0) throw new InvalidOperationException("the test structure: " + string.Join("; ", errors));
        return s;
    }

    /// <summary>Ticks the model for so many seconds at 60 a second, every device doing what <paramref name="doing"/> says.</summary>
    static void Run(StructurePower p, double seconds, Func<PowerState, Int3, Block, double> doing, bool on = true, bool free = false)
    {
        for (int i = 0; i < (int)Math.Round(seconds * 60); i++) p.Tick(1.0 / 60, on, free, doing);
    }

    static double Units(SharedStorage s, PowerRules r) => r.Fuels.Sum(f => (double)f.FuelUnits * s.Count(f.Id));

    void Model(GameData data)
    {
        GD.Print("-- the power model --");
        PowerRules rules = data.Power;
        // A core, a generator, a container, two thrusters pushing forward, a light and a constructor.
        Structure s = Build(data, "1 0 0 generator-t1", "-1 0 0 container-t1", "0 1 0 thruster-t1", "0 -1 0 thruster-t1", "0 0 -1 light", "0 0 1 fabricator-t1");
        var storage = new SharedStorage(data.Items, "test", BuildingBridge.StorageCapacity(s));
        var p = new StructurePower(rules, s, storage);
        double thrusters = s.Blocks().Where(b => s.TypeOf(b.block).IsThruster).Sum(b => -s.TypeOf(b.block).Power);
        double light = -data.Catalogue.Type("light").Power, fab = -data.Catalogue.Type("fabricator-t1").Power;
        double Idle(PowerState st, Int3 c, Block b) => 0;

        Check(p.CapacityMJ == rules.CoreMJ && rules.CoreMJ > 0, $"the core holds a capacitor of {p.CapacityMJ} MJ, and it starts empty ({p.ChargeMJ} MJ)");
        Run(p, 1, Idle);
        Check(p.Supply == 0 && p.ChargeMJ == 0 && p.OutputMW == 0, "no fuel in its storage and the capacitor empty: no power at all");

        storage.Add("biofuel", 10);
        Run(p, 20, Idle);
        double full = Units(storage, rules) + p.BurningUnits;
        Check(Math.Abs(p.ChargeMJ - p.CapacityMJ) < 1e-6 && p.Supply == 1 && p.DrawMW == 0,
            $"biofuel in its storage: the generator burns it from there and charges the capacitor full ({p.ChargeMJ:F1} MJ), nothing drawing ({storage.Count("biofuel")} biofuel left, {p.BurningUnits:F1} units in the one burning)");
        double filled = (100 - full) * rules.FuelUnitMJ;
        Check(Math.Abs(filled - p.CapacityMJ) < 1e-6, $"filling it took exactly its {p.CapacityMJ} MJ of fuel ({filled:F2} MJ)");
        Run(p, 60, Idle);
        Check(Math.Abs(Units(storage, rules) + p.BurningUnits - full) < 1e-9 && p.OutputMW == 0, "idle, a minute later it has burnt nothing more");

        // What a device draws follows what it is doing.
        Run(p, 1, (st, c, b) => st == PowerState.Throttle ? 0.5 : 0);
        Check(Math.Abs(p.DrawMW - 0.5 * thrusters) < 1e-9, $"thrusters at half throttle draw half their power ({p.DrawMW:F3} of {thrusters:F3} MW)");
        Run(p, 1, (st, c, b) => st == PowerState.Lit && !b.Off ? 1 : st == PowerState.Working ? 1 : 0);
        Check(Math.Abs(p.DrawMW - light - fab) < 1e-9, $"a light switched on and a constructor at work draw theirs ({p.DrawMW:F4} MW)");
        Run(p, 1, Idle);
        Check(p.DrawMW == 0, "the light off and the constructor idle: nothing");

        // The burn follows the load: at full throttle for a minute, the fuel gone is the energy drawn.
        double before = Units(storage, rules) + p.BurningUnits;
        Run(p, 60, (st, c, b) => st == PowerState.Throttle ? 1 : 0);
        double burnt = (before - Units(storage, rules) - p.BurningUnits) * rules.FuelUnitMJ, drawn = thrusters * 60;
        Check(Math.Abs(burnt - drawn) < 1e-6 && Math.Abs(p.ChargeMJ - p.CapacityMJ) < 1e-6,
            $"full throttle for a minute burns what it draws: {burnt:F2} MJ of fuel for {drawn:F2} MJ, the capacitor kept full");

        // More than the generators make: the capacitor carries the rest, then everything gets its share.
        p.AddLoad(p.GeneratorMW * 2);
        p.Tick(1.0 / 60, true, false, Idle);
        Check(p.Supply == 1 && p.ChargeMJ < p.CapacityMJ && Math.Abs(p.Usage - 2) < 1e-9, $"a load of twice the generator's {p.GeneratorMW:F2} MW: the capacitor makes up the difference ({p.ChargeMJ:F2} MJ left)");
        for (int i = 0; i < 60 * 60 && p.ChargeMJ > 0; i++) { p.AddLoad(p.GeneratorMW * 2); p.Tick(1.0 / 60, true, false, Idle); }
        p.AddLoad(p.GeneratorMW * 2);
        p.Tick(1.0 / 60, true, false, Idle);
        Check(Math.Abs(p.Supply - 0.5) < 1e-6 && p.ChargeMJ == 0, $"emptied, every device gets the share the generators can give ({p.Supply:P0})");

        // Run dry: the capacitor empties and nothing works.
        foreach (ItemDef f in rules.Fuels) storage.Remove(f.Id, storage.Count(f.Id));
        for (int i = 0; i < 60 * 600 && p.Supply > 0; i++) p.Tick(1.0 / 60, true, false, (st, c, b) => st == PowerState.Throttle ? 1 : 0);
        Check(p.Supply == 0 && p.ChargeMJ == 0 && p.FuelUnits < 1e-9 && p.SecondsLeft < 1e-6, $"out of fuel at full throttle, the fuel burning burns out, the capacitor empties and every device stops ({p.Supply:P0})");
        Run(p, 1, Idle);
        Check(p.Supply == 0, "and stays stopped, drawing or not");

        // Generators take the least efficient fuel first, the dense fuel last.
        storage.Add("fusion-cell", 1);
        storage.Add("energy-cell", 2);
        storage.Add("biofuel", 2);
        var order = new List<string>();
        for (int i = 0; i < 60 * 3600 && p.FuelUnits > 0; i++)
        {
            p.Tick(1.0 / 60, true, false, (st, c, b) => st == PowerState.Throttle ? 1 : 0);
            if (p.Burning != null && (order.Count == 0 || order[^1] != p.Burning)) order.Add(p.Burning);
        }
        Check(order.SequenceEqual(new[] { "biofuel", "energy-cell", "fusion-cell" }), $"they burn the least efficient fuel first: {string.Join(" then ", order)}");

        // Creative's rule: the generators run without burning.
        storage.Add("biofuel", 1);
        p.ChargeMJ = 0;
        p.BurningUnits = 0;
        Run(p, 30, (st, c, b) => st == PowerState.Throttle ? 1 : 0, free: true);
        Check(storage.Count("biofuel") == 1 && p.Supply == 1, "in Creative the generators make power and burn nothing");
        Run(p, 1, (st, c, b) => 1, on: false);
        Check(p.Supply == 0 && p.DrawMW == 0 && p.OutputMW == 0, "power switched off: nothing draws and nothing is made");

        // No generator: the core's capacitor never fills.
        Structure bare = Build(data, "-1 0 0 container-t1", "0 0 -1 light");
        var bareStore = new SharedStorage(data.Items, "bare", BuildingBridge.StorageCapacity(bare));
        bareStore.Add("biofuel", 5);
        var q = new StructurePower(rules, bare, bareStore);
        Run(q, 5, (st, c, b) => st == PowerState.Lit ? 1 : 0);
        Check(q.Supply == 0 && bareStore.Count("biofuel") == 5, "with no generator, fuel in storage gives no power");

        // Phase 2's devices: their costs are in the data, drawing nothing while idle.
        Check(rules.Draw.TryGetValue("turret", out var turret) && turret.Any(r => r.State == PowerState.Idle && r.Share == 0) && turret.Any(r => r.State == PowerState.Firing && r.Share == 1)
              && rules.Draw.TryGetValue("shield", out var shield) && shield.Any(r => r.State == PowerState.Recharging) && rules.ShieldFoldPerHp > 0,
            $"turrets (idle nothing, tracking, firing) and shields (up, recharging, {rules.ShieldFoldPerHp} fold crystal a hit point) have their draw in the data");
    }

    // ---- Generator fuel ---------------------------------------------------------------------------------

    void Fuels(GameData data)
    {
        GD.Print("-- generator fuel --");
        PowerRules rules = data.Power;
        foreach (ItemDef f in rules.Fuels)
            GD.Print($"    {f.Name,-22} {f.FuelUnits,4} units, {f.FuelUnits * rules.FuelUnitMJ,5:0} MJ, {f.VolumeSu:0.##} SU: {f.FuelUnits / f.VolumeSu:0.#} a SU; level {f.Unlock.Level}, {data.Items.Machines.TierName(f.Recipe.Tier)} constructor");
        var byTier = rules.Fuels.OrderBy(f => f.Unlock.Level).ThenBy(f => f.FuelUnits).ToList();
        Check(rules.Fuels.SequenceEqual(byTier), "the order generators burn them in is the tech tree's: the early fuel, the least efficient, first");
        Check(data.Catalogue.Types.All(t => t == null || t.Function is not ("fuel" or "warp-tank" or "fold-tank")) && !data.Catalogue.Types.Any(t => t?.Id.Contains("tank") == true),
            "no fuel tank, warp cell tank or fold crystal tank block is left: fuel lives in containers");
        Check(data.Catalogue.TryGetType("fuel-tank-t1", out BlockType old) && old.IsContainer && data.Items.Current("fuel-tank-t2") == "container-t1",
            "an old fuel tank in a blueprint or a bag reads as a container");
    }

    // ---- Warp and fold fuel -------------------------------------------------------------------------

    void JumpFuels(GameData data)
    {
        GD.Print("-- warp and fold fuel --");
        ItemCatalogue items = data.Items;
        ItemDef ore = items.Item("pentaxite"), cell = items.Item("warp-cell"), raw = items.Item("fold-crystal-raw"), fold = items.Item("fold-crystal");
        Check(ore.Source == ItemSource.Mined && ore.DepositKind == nameof(DepositKind.WarpFuel) && cell.Recipe.Inputs.All(i => i.Item == ore),
            $"warp fuel is gathered: {ore.Name} is mined from its deposits and refined into warp cells ({cell.Recipe.Count} a {ore.Name.ToLowerInvariant()})");
        Check(raw.Source == ItemSource.Mined && raw.DepositKind == nameof(DepositKind.Crystal) && fold.Recipe.Inputs.All(i => i.Item == raw),
            "fold fuel is gathered: raw fold crystal is mined from its deposits and refined into fold crystal");
        PlanetSpec Spec(Archetype a, Modifier m, int km = 16) => new((ulong)Random.Shared.NextInt64(), a, 0, m, km);
        Check(DepositField.FuelKinds(Spec(Archetype.Arid, Modifier.Cratered, 8)).Contains(DepositKind.WarpFuel) && DepositField.FuelKinds(Spec(Archetype.Ice, Modifier.None)).Contains(DepositKind.WarpFuel)
              && !DepositField.FuelKinds(Spec(Archetype.Temperate, Modifier.None)).Contains(DepositKind.WarpFuel),
            "pentaxite lies on airless bodies (the start's fuel moon is always one) and on icy worlds, not on a living world");
        Check(DepositField.FuelKinds(Spec(Archetype.Ice, Modifier.None, 32)).Contains(DepositKind.Crystal), "fold crystal lies on the dangerous high-gravity worlds");
        Check(data.Power.Drives["warp-drive"].Item == cell.Id && data.Power.Drives["fold-drive"].Item == fold.Id, "the warp drive burns warp cells and the fold drive fold crystal, three fuels apart");

        // A drive reads its reach from the fuel in core storage.
        Structure s = Build(data, "1 0 0 container-t1", "-3 0 0 warp-drive");
        var storage = new SharedStorage(data.Items, "drive", BuildingBridge.StorageCapacity(s));
        var p = new StructurePower(data.Power, s, storage);
        storage.Add(cell.Id, 12);
        storage.Add(fold.Id, 5);
        Check(p.Range("warp-drive") is { Reach: 12.0, Unit: "AU" } && p.Range("fold-drive") == null,
            $"a warp drive fitted, 12 warp cells in storage: fuel for {p.Range("warp-drive")?.Reach} AU; no fold drive fitted, no reach");
    }

    // ---- The start ----------------------------------------------------------------------------------------

    void Start(GameData data)
    {
        GD.Print("-- the start: power before the climate node needs it --");
        ItemCatalogue items = data.Items;
        var fresh = new Progress(data.Progress, items, data.Catalogue);
        string[] first = { "core", "generator-t1", "container-t1", "biofuel", "energy-cell", Device.ClimateItem };
        var late = first.Where(id => !fresh.ItemAllowed(items.Item(id)) || items.Item(id).Recipe.Tier > 0).ToList();
        Check(late.Count == 0, $"a sleeper at level 1 makes a core, a generator, a container, biofuel and a climate node at the survival constructor{(late.Count > 0 ? ": not " + string.Join(", ", late) : "")}");
        Check(items.Item("biofuel").Recipe.Inputs.All(i => i.Item.Source == ItemSource.Harvested), "and the first fuel from cutting plants");
        Check(data.Rules.ClimateNode.Power > 0 && !items.Recipes.Any(r => r.Output.Id == Device.ClimateItem && r.Inputs.Any(i => i.Item.FuelUnits > 0)),
            $"the climate node draws {data.Rules.ClimateNode.Power} MW of its structure's power and burns nothing");
    }

    // ---- In the world ------------------------------------------------------------------------------------

    /// <summary>A stock blueprint set down landed on flat ground near the start.</summary>
    Vessel SetDown(GameData data, string file, float distance)
    {
        Structure s = data.Blueprint(file);
        var (lo, hi) = s.Bounds();
        float span = 0.5f * new Vector2(hi.X - lo.X + 1, hi.Z - lo.Z + 1).Length();
        Vector3 spot = new Playthrough(_game).FlatSpot(_game.Survivor.WorldFeet, distance, span: MathF.Max(10f, span));
        Vessel v = Vessel.Spawn(_game.Zone, s, (spot + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        v.Settle();
        return v;
    }

    float Height(Vessel v) => v.Body.Position.Y - _game.Zone.GroundAt(v.Body.Position.X, v.Body.Position.Z).Y;

    /// <summary>The draw, averaged over some ticks.</summary>
    IEnumerable<int> Average(Vessel v, int ticks, Action<double> got)
    {
        double sum = 0;
        for (int i = 0; i < ticks; i++)
        {
            yield return 0;
            sum += v.Power.DrawMW;
        }
        got(sum / ticks);
    }

    IEnumerable<int> Flight(GameData data)
    {
        GD.Print("-- the Skiff on fuel in its storage --");
        Vessel v = SetDown(data, "starter.gbp", 40f);
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(!v.Powered && !v.View.Powered, $"{v.Name}: set down with nothing in its storage, no power and its lights dark");
        Controls c = _game.Controls;
        _game.Survivor.PlaceOutside((v.Body.Position + N.Vector3.UnitY * 4f).ToGodot().ToNumerics());
        bool sat = _game.Survivor.Sit(v);
        c.Hold(Key.Space);
        foreach (int x in _drive.Wait(90)) yield return x;
        c.Hold(Key.Space, false);
        Check(sat && v.Controller.Frozen && v.Controller.ConsoleMessage == "NO POWER", $"seated, Space does nothing: the console says {v.Controller.ConsoleMessage ?? "nothing"}");

        int cells = v.Storage.Add("energy-cell", 30);
        foreach (int x in _drive.Wait(20)) yield return x; // past the view's quarter-second look at its power
        Check(cells == 30 && v.Powered && v.View.Powered && v.Power.ChargeMJ > 0, $"30 energy cells in its storage: its generator charges the capacitor ({v.Power.ChargeMJ:F1} MJ) and its lights come on");
        foreach (int x in _drive.Wait(60 * 12)) yield return x; // the capacitor full
        double ground = Height(v), climbing = 0, hovering = 0, idle = v.Power.DrawMW; // seated on the ground: the chair and lights alone
        c.Hold(Key.Space);
        for (int t = 0; t < 900 && Height(v) < ground + 16f; t++) yield return 0;
        foreach (int x in Average(v, 30, d => climbing = d)) yield return x;
        c.Hold(Key.Space, false);
        for (int t = 0; t < 600 && MathF.Abs(v.Body.Velocity.Y) > 0.3f; t++) yield return 0;
        foreach (int x in _drive.Wait(60)) yield return x;
        double units = v.Power.FuelUnits, drawn = 0;
        float held = Height(v);
        for (int t = 0; t < 180; t++)
        {
            yield return 0;
            drawn += v.Power.DrawMW / 60;
        }
        hovering = drawn / 3;
        double burnt = (units - v.Power.FuelUnits) * data.Power.FuelUnitMJ;
        Check(Height(v) > ground + 12f && MathF.Abs(Height(v) - held) < 1f, $"it climbs off the ground and hovers hands off ({Height(v) - ground:F1} m up)");
        Check(climbing > hovering && hovering > 0, $"its thrusters draw by their throttle: {climbing:F3} MW climbing, {hovering:F3} MW holding it up");
        // Holding a hover is thrust too: the lift thrusters carry its weight, so they draw that share of their power.
        var stats = v.Flight.Stats;
        double lift = v.Structure.Blocks().Where(x => v.Structure.TypeOf(x.block).IsThruster && Vessel.PushOf(x.block) == ShipHandling.Handling.Direction.Up)
            .Sum(x => -v.Structure.TypeOf(x.block).Power);
        double weighs = lift * stats.Weight / stats.Thrust[(int)ShipHandling.Handling.Direction.Up];
        Check(Math.Abs(hovering - idle - weighs) < weighs * 0.15, $"hovering in place its lift thrusters draw about its weight's worth: {hovering - idle:F3} MW for {weighs:F3} ({stats.Weight / stats.Thrust[(int)ShipHandling.Handling.Direction.Up]:P0} of {lift:F3} MW of lift)");
        Check(Math.Abs(burnt - drawn) < drawn * 0.05 + 1e-6, $"its generator burns fuel from its storage as they draw: {burnt:F2} MJ of fuel for {drawn:F2} MJ drawn in 3 s ({v.Storage.Count("energy-cell")} cells left)");
        var gauges = Slice.Ui.StructureReadout.Of(v);
        Check(gauges.CapUse == v.Power.Usage && gauges.CapUse > 0 && gauges.FuelSeconds == v.Power.SecondsLeft && gauges.FuelSeconds > 60 && gauges.WarpAu == null && gauges.FoldLy == null,
            $"the chair's gauges read it: {gauges.CapUse:P0} of its generator's supply in use, {gauges.FuelSeconds / 60:F0} min of fuel, no drives");

        // Run dry in the air: the fuel taken out, the capacitor nearly empty.
        foreach (ItemDef f in data.Power.Fuels) v.Storage.Remove(f.Id, v.Storage.Count(f.Id));
        v.Power.BurningUnits = 0;
        v.Power.ChargeMJ = 0.5; // STAGING: run down to its last half megajoule, so the test needn't wait it out
        float top = Height(v);
        for (int t = 0; t < 600 && v.Powered; t++) yield return 0;
        bool dark = !v.Powered;
        foreach (int x in _drive.Wait(45)) yield return x;
        float fell = top - Height(v);
        Check(dark && !v.View.Powered && v.Power.ChargeMJ == 0, "its capacitor empty and no fuel: no power, its lights dark");
        Check(fell > 1.5f && v.Body.Velocity.Y < -3f && v.Flight.Throttle.All(th => th == 0f), $"it loses thrust and falls: {fell:F1} m in 0.75 s, {-v.Body.Velocity.Y:F1} m/s down, its thrusters idle");
        // Fuel back before it lands hard: the assist catches it.
        v.Storage.Add("energy-cell", 10);
        for (int t = 0; t < 300 && v.Body.Velocity.Y < -0.5f && !v.Controller.Frozen; t++) yield return 0;
        Check(v.Powered && (v.Body.Velocity.Y > -0.5f || v.Controller.Frozen), $"fuel in its storage again, its power comes back and the assist stops the fall ({v.Body.Velocity.Y:F1} m/s)");
        _game.Survivor.StandUp();
        _game.Zone.Remove(v); // taken away the moment the sleeper stands up (destroyed, or claimed)
        foreach (int x in _drive.Wait(5)) yield return x;
        Check(SetOut(), "the Skiff taken away just as the sleeper stands up: they are set out of it, their body their own");
    }

    /// <summary>The sleeper's body is whole and in the world, in no vessel's cabin, and the world goes on ticking.</summary>
    bool SetOut() => GodotObject.IsInstanceValid(_game.Survivor.Node) && _game.Survivor.Node.GetParent() == _game.Zone
        && _game.Survivor.Seated == null && _game.Survivor.Aboard == null;

    IEnumerable<int> Hover(GameData data)
    {
        GD.Print("-- a hover craft on fuel in its storage --");
        Vessel v = SetDown(data, "rover.gbp", 70f);
        if (!Check(v.Structure.Mode == CoreMode.Hover, $"{v.Name}: a hover craft")) yield break;
        v.Storage.Add("energy-cell", 30);
        _game.Survivor.PlaceOutside((v.Body.Position + N.Vector3.UnitY * 4f).ToGodot().ToNumerics());
        _game.Survivor.Sit(v);
        _game.Controls.Hold(Key.W);
        foreach (int x in _drive.Wait(30)) yield return x;
        _game.Controls.Hold(Key.W, false);
        foreach (int x in _drive.Wait(90)) yield return x;
        double plates = v.Structure.Blocks().Where(b => v.Structure.TypeOf(b.block).Function == "hover").Sum(b => -v.Structure.TypeOf(b.block).Power);
        float riding = v.AboveGround;
        Check(!v.Controller.Frozen && v.Power.DrawMW >= plates - 1e-9, $"riding its plates ({riding:F1} m up), they draw their {plates:F3} MW ({v.Power.DrawMW:F3} in all)");
        foreach (ItemDef f in data.Power.Fuels) v.Storage.Remove(f.Id, v.Storage.Count(f.Id));
        v.Power.BurningUnits = 0;
        v.Power.ChargeMJ = 0; // STAGING: run dry
        foreach (int x in _drive.Wait(120)) yield return x;
        Check(!v.Powered && v.AboveGround < riding - 0.3f, $"run dry, the plates let it down onto the ground ({riding:F1} to {v.AboveGround:F1} m)");
        _game.Zone.Remove(v); // taken away with the sleeper still seated in it
        foreach (int x in _drive.Wait(5)) yield return x;
        Check(SetOut(), "the rover taken away with the sleeper in its chair: they get up and are set out first, their body their own");
    }
}
