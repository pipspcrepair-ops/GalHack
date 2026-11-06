using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Planets.Gen;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using Slice.Game;
using Slice.Items;
using Slice.Player;
using Slice.Surface;
using Slice.Start;
using Slice.Ui;
using DiegeticUi.Screens;
using Slice.Vessels;
using Wildlife;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The loop played the way a person plays it, headless: the pod comes down for real with the
/// castaway strapped in, and from then on only the game's own inputs are used (look, walk, keys,
/// the mouse on the Tab menu). No teleports, no grants, no skipped steps: every part is crafted,
/// every block paid for, every unlock earned. The one help is the marked TEST TIME-SCALE on
/// crafting, so an hour of fabricating fits in a run.
///
///   1  wake in the cryopod, take the bunker's locker, shoot its wall, climb out, 50 m away and back
///   2  the tool from the toolbar: gather ore, cut a plant
///   3  shoot the ground and a creature
///   4  loot the creature's body (drag, shift-click, split, take all), fill the toolbar by drag
///   5  craft the survival constructor in the suit fabricator, set it down
///   6  craft the base core in it
///   7  found a base, build onto it: a container, a generator, a small constructor, steel
///   8  earn level 5, unlock the small vessel, make its core and parts in the base's constructor
///   9  build a vessel, fly 500 m and land
///   10 every Tab menu tab, the head torch, and dying: your backpack, recovered from its marker
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/PlayerTest.tscn
/// </summary>
public partial class PlayerTest : Node
{
    /// <summary>TEST TIME-SCALE: crafting runs this many times faster (printed). Nothing else is sped up.</summary>
    const double CraftScale = 40;
    const int WatchdogMinutes = 25;

    Game.Game _game;
    Playthrough _play;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly List<RescueInfo> _rescues = new();
    readonly Dictionary<string, double> _gameTimeAt = new();
    Vessel _base, _vessel;
    string _said = "";
    float _topFall;
    Device _fab;
    Vector3 _pad;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = false, CraftTimeScale = CraftScale, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _play = new Playthrough(_game);
        _drive = _play.Drive;
        GD.Print($"-- TEST TIME-SCALE: crafting runs {CraftScale}x faster; everything else is real time --");
        while (_game.Phase != Phase.Playing) yield return 0;
        _game.Said += (text, _) => _said = text;
        _game.Zone.Physics.Rescued += r =>
        {
            _rescues.Add(r);
            if (r.Body == _game.Survivor.Body)
                GD.Print($"  (rescued the castaway at {_game.Stats.Seconds:F0} s: {r.From} -> {r.To}, inside {r.Inside}; aboard {_game.Survivor.Aboard?.Name}, seated {_game.Survivor.Seated?.Name})");
        };
        // Clear, still weather and calm creatures: the steps are what is tested, not the storms.
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        foreach (int x in _drive.UntilPlaying()) yield return x;

        foreach (int x in WakeUp()) yield return x;
        foreach (int x in Gathering()) yield return x;
        foreach (int x in Hunting()) yield return x;
        foreach (int x in Inventory()) yield return x;
        foreach (int x in JetpackLiftOff()) yield return x;
        foreach (int x in SurvivalConstructor()) yield return x;
        foreach (int x in Base()) yield return x;
        foreach (int x in VesselCore()) yield return x;
        foreach (int x in BuildVessel()) yield return x;
        foreach (int x in Fly()) yield return x;
        foreach (int x in MenuAndTorch()) yield return x;
        foreach (int x in Death()) yield return x;
        Summary();
    }

    void Mark(string step) => _gameTimeAt[step] = _game.Stats.Seconds;

    // ==== 1. The pod ===============================================================================

    IEnumerable<int> WakeUp()
    {
        GD.Print($"-- 1. the bunker: wake, the locker, up the stairs and back ({_game.World}) --");
        foreach (int x in _play.WakeAndLeave(Check, shoot: true)) yield return x;
        Check(_rescues.All(r => r.Body != _game.Survivor.Body), "never rescued on the way");
        Mark("out");
    }

    // ==== 2 & 3. Gathering and hunting ============================================================

    /// <summary>What the whole loop needs from the ground, worked out from the recipes for what this run builds.</summary>
    Bill Plan()
    {
        ItemCatalogue items = _game.Zone.Data.Items;
        var need = new Bill();
        need.Add("survival-constructor", 1);
        need.Add("base-core", 1);
        need.Add("vessel-core", 1);
        foreach (var (block, small, n) in BaseBlocks.Concat(VesselBlocks)) need.Add(items.Blocks.For(block, small), n);
        return items.Graph.Expand(need).Leaves;
    }

    static readonly (string block, bool small, int n)[] BaseBlocks =
    {
        ("container", false, 1), ("generator", false, 1), ("smallfab", false, 1), ("steel", false, 2),
    };

    static readonly (string block, bool small, int n)[] VesselBlocks =
        Playthrough.VesselLayout.GroupBy(b => b.block).Select(g => (g.Key, true, g.Count())).ToArray();

    /// <summary>The toolbar slots the vessel's blocks go on.</summary>
    static readonly Dictionary<string, int> Slots = new()
    {
        ["chair"] = 4, ["generator"] = 5, ["fueltank"] = 6, ["gyro"] = 7, ["thruster"] = 8, ["steel"] = 1, ["gear"] = 2,
    };

    IEnumerable<int> Gathering()
    {
        GD.Print("-- 2. gathering with the tool from the toolbar --");
        Bill raw = Plan();
        GD.Print($"  the whole loop needs {_game.Zone.Data.Items.Describe(raw)} from the ground");
        foreach (int x in _drive.Tap(Key.Key1)) yield return x;
        Check(_game.Pack.Hand == HandKind.Tool, "1 puts the survival tool in hand");
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(_game.Hands.Visible, "it shows in the first-person hands");
        var kinds = new (DepositKind kind, string ore)[] { (DepositKind.Iron, "iron-ore"), (DepositKind.Copper, "copper-ore"), (DepositKind.Carbon, "carbon-ore"), (DepositKind.Silicon, "silicon-ore"), (DepositKind.Water, "ice") };
        int firstCells = -1;
        foreach (var (kind, ore) in kinds)
        {
            int want = raw[ore] + 6;
            if (want <= 6) continue;
            int cells = _game.Tool.OreCells, before = _game.Pack[ore];
            foreach (int x in _play.Gather(kind, want)) yield return x;
            GD.Print($"  {ore}: {_game.Pack[ore]}/{want} ({_game.Tool.OreCells - cells} cells), level {_game.Progress.Level}, {_game.Progress.Xp} XP");
            if (firstCells < 0)
            {
                firstCells = _game.Tool.OreCells - cells;
                Check(_game.Pack[ore] - before == firstCells * _game.Zone.Data.Rules.Materials.OrePerCell && firstCells > 0,
                    $"digging {kind} gives {_game.Zone.Data.Rules.Materials.OrePerCell} {ore} a cell: {firstCells} cells, +{_game.Pack[ore] - before}");
            }
        }
        var shortOf = raw.Where(kv => kv.Key != "stone" && _game.Pack[kv.Key] < kv.Value).ToList();
        Check(shortOf.Count == 0, $"the deposits near the pod hold the whole loop's ore: {string.Join(", ", raw.Select(kv => $"{_game.Pack[kv.Key]}/{kv.Value} {kv.Key}"))}");
        if (_game.Pack["stone"] < raw["stone"]) GD.Print($"  (stone {_game.Pack["stone"]}/{raw["stone"]}: more comes with the next digs)");
        Check(_game.Visor.Deposits.Near(_game.Zone, _game.Survivor.WorldFeet).All(d => !DepositFinder.Depleted(_game.Zone, KindOf(d.kind), d.at.X, d.at.Y, d.at.Z)),
            "a deposit dug out drops off the compass: every label shown still holds ore");

        int plants = _game.Tool.PlantsCut, biomass = _game.Pack["biomass"];
        foreach (int x in _play.CutPlant()) yield return x;
        Check(_game.Tool.PlantsCut > plants && _game.Pack["biomass"] > biomass, $"cutting a plant: +{_game.Pack["biomass"] - biomass} biomass");
        Mark("gathered");
    }

    static DepositKind KindOf(string label) => label == "ice" ? DepositKind.Water : Enum.Parse<DepositKind>(label, true);

    Creature _prey;

    IEnumerable<int> Hunting()
    {
        GD.Print("-- 3. shooting: the ground, then a creature --");
        // A patch of ground 5 m off that the eye can see (the real ground, dug holes and all).
        Vector3 ahead = _game.Zone.GroundAt(_game.Survivor.WorldFeet.X + 4f, _game.Survivor.WorldFeet.Z + 3f);
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        Vector3 eye = _game.Survivor.WorldEye();
        for (int k = 0; k < 12; k++)
        {
            float a = 0.64f + k * MathF.Tau / 12f;
            Vector3 p = _game.Survivor.WorldFeet + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * 4f;
            var down = space.IntersectRay(PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 6f, p + Vector3.Down * 6f, Layers.World));
            if (down.Count == 0) continue;
            Vector3 g = (Vector3)down["position"];
            if (eye.Y - g.Y < 0.7f) continue; // ground at eye level would only be grazed
            var seen = space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, g + (g - eye).Normalized() * 0.5f, Layers.World | Layers.Hull | Layers.Props));
            if (seen.Count > 0 && ((Vector3)seen["position"]).DistanceTo(g) < 0.3f) { ahead = g; break; }
        }
        int impacts = _game.Shots.Impacts;
        foreach (int x in _play.ShootAt(() => ahead)) yield return x;
        foreach (int x in _drive.Wait(20)) yield return x;
        Check(_game.Shots.Impacts > impacts && _game.Shots.LastImpact.Kind == ImpactKind.Ground && _game.Shots.LastImpact.At.DistanceTo(ahead) < 1.5f,
            $"a shot at the ground: a bolt and sparks and a scorch where it lands ({_game.Shots.LastImpact.At.DistanceTo(ahead):F2} m from the aim)");

        WildlifePatch w = _game.Zone.Wildlife;
        Vector3 feet = _game.Survivor.WorldFeet;
        var tried = new HashSet<Creature>();
        Creature NextPrey()
        {
            Vector3 from = _game.Survivor.WorldFeet;
            return w.Sim.Creatures.Where(c => !c.Dead && !c.Removed && !tried.Contains(c) && !c.Species.Flies && c.Species.Plan is BodyPlan.Walker or BodyPlan.Hopper)
                .OrderBy(c => w.FromPatch(c.Position).DistanceTo(from)).FirstOrDefault();
        }
        // One you can walk to, by the way you can walk: straight at it, a hunter can go down a cliff
        // and strand the rest of the test at the bottom. The nearest of all if there is no such one.
        var ground = _play.WalkableFrom(feet);
        List<Vector3> stalk = null;
        foreach (Creature c in w.Sim.Creatures.Where(c => !c.Dead && !c.Removed && !c.Species.Flies && c.Species.Plan is BodyPlan.Walker or BodyPlan.Hopper).OrderBy(c => w.FromPatch(c.Position).DistanceTo(feet)))
            if (_play.WayTo(ground, feet, w.FromPatch(c.Position), 2.5f) is List<Vector3> way) { _prey = c; stalk = way; break; }
        _prey ??= NextPrey();
        if (_prey == null) { Fail("no creature near to hunt"); yield break; }
        // Walking on brings a new wildlife patch, and the creatures in it are new objects: follow
        // the prey across, or the hunt goes after the old one's stale position in the new patch.
        Action<WildSim, Vector3, WildSim> carried = (old, oldOrigin, sim) =>
        {
            if (_prey.Dead) return;
            Vector3 was = _prey.Position + oldOrigin;
            _prey = sim.Creatures.Where(c => c.Species == _prey.Species && w.FromPatch(c.Position).DistanceTo(was) < 2f).FirstOrDefault() ?? _prey;
        };
        w.Swapped += carried;
        GD.Print($"  hunting a {_prey.Species.CommonName} {w.FromPatch(_prey.Position).DistanceTo(feet):F0} m away ({_prey.Species.Stats.MaxHealth:F0} health, worth {_prey.Species.Stats.Xp} XP)");
        float health = _prey.Health;
        int xp = _game.Progress.Xp;
        int shots = 0, stalls = 0;
        bool hit = false;
        float close = 25f;
        int flights = 0;
        float Above() => w.FromPatch(_prey.Position).Y - _game.Survivor.WorldFeet.Y;
        // Up over it on the jetpack, firing down whenever it is in range, as a player would.
        IEnumerable<int> FlyAtPrey()
        {
            GD.Print($"  (the {_prey.Species.CommonName} stands {Above():F0} m up a bank: up over it on the jetpack, shooting)");
            foreach (int x in _play.Tool(Slice.Player.ToolMode.Shoot)) yield return x;
            float was = _prey.Health;
            foreach (int x in _drive.HopToward(w.FromPatch(_prey.Rig.BoundsCenter), 14f, 2f,
                () =>
                {
                    if (_prey.Health >= was) return _prey.Dead;
                    was = _prey.Health;
                    hit |= _game.Shots.LastImpact.Kind == ImpactKind.Creature;
                    return _prey.Dead;
                },
                () => { _game.Controls.Primary = true; _game.Controls.PrimaryPressed = true; })) yield return x;
            _game.Controls.ReleaseAll();
        }
        GD.Print($"  (the way to it on foot: {(stalk == null ? "none found, straight at it" : $"{stalk.Count} steps over {ground.Count} cells of walkable ground")})");
        if (stalk != null)
            foreach (Vector3 step in stalk)
            {
                if (w.FromPatch(_prey.Position).DistanceTo(_game.Survivor.WorldFeet) < 8f) break;
                foreach (int x in _drive.WalkTo(step, 1.5f, 8f, sprint: false, dig: false)) yield return x;
            }
        for (int tries = 0; tries < 40 && !_prey.Dead; tries++)
        {
            Vector3 at = w.FromPatch(_prey.Position);
            float off = at.DistanceTo(_game.Survivor.WorldFeet);
            if (off > close)
            {
                foreach (int x in _drive.WalkTo(at, close * 0.6f, 20f)) yield return x;
                // A river or a cliff between you and it: a hunter picks another.
                stalls = w.FromPatch(_prey.Position).DistanceTo(_game.Survivor.WorldFeet) > off - 5f ? stalls + 1 : 0;
                // Up a bank too steep to stand on, where every step slides back: over it on the jetpack.
                if (stalls >= 2 && flights < 2 && Above() > 2.5f)
                {
                    flights++;
                    foreach (int x in FlyAtPrey()) yield return x;
                    stalls = 0;
                    continue;
                }
                if (stalls >= 2 && shots == 0 && NextPrey() is Creature next && tried.Add(_prey))
                {
                    next = NextPrey();
                    GD.Print($"  (no way to the {_prey.Species.CommonName}, {off:F0} m off: hunting a {next.Species.CommonName} instead)");
                    _prey = next;
                    health = _prey.Health;
                    stalls = 0;
                    close = 25f;
                }
                continue;
            }
            float before = _prey.Health;
            foreach (int x in _play.ShootAt(() => w.FromPatch(_prey.Rig.BoundsCenter))) yield return x;
            if (tries < 12) GD.Print($"  (shot {shots + 1}: prey at {w.FromPatch(_prey.Rig.BoundsCenter)}, health {_prey.Health:F0}, {w.FromPatch(_prey.Position).DistanceTo(_game.Survivor.WorldEye()):F1} m off; the bolt landed {_game.Shots.LastImpact.Kind} at {_game.Shots.LastImpact.At}; dead {_game.Suit.Dead}, hand {_game.Pack.Hand}, mode {_game.Tool.Mode}, menu {_game.Menu.IsOpen})");
            shots++;
            if (_prey.Health >= before) close = MathF.Max(2.5f, close * 0.6f); // something in the way (a bank, a crest): closer
            else hit |= _game.Shots.LastImpact.Kind == ImpactKind.Creature;
            // The crest it stands behind is its own slope, and you can't stand on that: from the air.
            if (!_prey.Dead && _prey.Health >= before && flights < 2 && Above() > 2.5f)
            {
                flights++;
                foreach (int x in FlyAtPrey()) yield return x;
            }
        }
        w.Swapped -= carried;
        Check(hit && (_prey.Dead || _prey.Health < health), $"shots hurt it ({health:F0} to {_prey.Health:F0}), each with a bolt, a spray where it lands and the visor's hit marker");
        Check(_prey.Dead && _game.Progress.Xp >= xp + _prey.Species.Stats.Xp, $"the {_prey.Species.CommonName} is brought down: +{_game.Progress.Xp - xp} XP");
        Mark("hunted");
    }

    // ==== 4. The inventory and the body ============================================================

    IEnumerable<int> Inventory()
    {
        GD.Print("-- 4. the body, and the inventory by hand --");
        Lootable body = _game.Zone.Loot.All.FirstOrDefault(l => l.Kind == LootKind.Corpse && l.Creature == _prey);
        if (body == null) { Fail("the creature left no body to loot"); yield break; }
        Check(body.Storage.Count("meat") > 0 && body.Storage.Count("hide") > 0, $"its body is a container: {_game.Zone.Data.Items.Describe(body.Storage.Contents)}");
        foreach (int x in _drive.WalkTo(body.Position, 1.6f, 30f)) yield return x;
        foreach (int x in _drive.LookAt(body.Position + Vector3.Up * 0.3f, 6)) yield return x;
        foreach (int x in _drive.Tap(Key.E)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        // On a slope too steep to stand on the walker slides off below it: up on the jetpack and E
        // from the air, as a player would.
        if (_game.Opened?.Loot != body)
            foreach (int x in _drive.HopToward(body.Position + Vector3.Up * 0.3f, Game.Game.UseReach - 0.5f, 1f, () => _game.Opened?.Loot == body, () => _game.Controls.Tap(Key.E))) yield return x;
        Check(_game.Opened?.Loot == body, "E on the body opens its loot beside the bag");
        // Drag the meat across, shift-click the hide, then take the rest.
        int meatSlot = body.Grid.IndexOf("meat"), free = Enumerable.Range(PlayerInventory.ToolbarSlots, _game.Pack.Grid.BagSlots).First(i => _game.Pack.Grid[i].IsEmpty);
        int meat = body.Storage.Count("meat");
        foreach (int x in _drive.DragWidget($"box:{meatSlot}", $"bag:{free}")) yield return x;
        Check(_game.Pack.Grid[free].Item == "meat" && _game.Pack["meat"] == meat && body.Storage.Count("meat") == 0, $"dragging the meat onto a bag slot moves the stack there ({meat})");
        int hide = body.Storage.Count("hide");
        foreach (int x in _drive.ClickWidget($"box:{body.Grid.IndexOf("hide")}", shift: true)) yield return x;
        Check(_game.Pack["hide"] == hide && body.Storage.Count("hide") == 0, "shift-click sends the hide across");
        foreach (int x in _drive.ClickWidget("box:take-all")) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(body.Storage.IsEmpty, "take all empties the body");

        // Split a stack, and fill the toolbar by dragging.
        int biomassSlot = _play.BagSlotOf("biomass");
        int stacks = _game.Pack.Grid.Slots.Count(s => s.Item == "biomass"), total = _game.Pack["biomass"];
        foreach (int x in _drive.RightClickWidget($"bag:{biomassSlot}")) yield return x;
        Check(_game.Pack.Grid.Slots.Count(s => s.Item == "biomass") == stacks + 1 && _game.Pack["biomass"] == total, $"right-click splits a stack in two ({total} biomass in {stacks + 1} stacks)");
        foreach (int x in _drive.DragWidget($"bag:{_play.BagSlotOf("patch-kit")}", "bar:7")) yield return x;
        Check(_game.Pack.Grid[7].Item == "patch-kit", "a patch kit dragged onto toolbar slot 8");
        foreach (int x in _play.CloseMenu()) yield return x;
        Check(!_game.Zone.Loot.All.Contains(body), "the emptied body is taken away once closed (never while it held anything)");
        Mark("looted");
    }

    // ==== 5 & 6. The survival constructor and the base core ======================================

    /// <summary>Up to the constructor and E on it: at its front, or round another side if the ground stands in the way of the look, as a player would.</summary>
    /// <summary>E on a fabricator opens the full panel: the Tab menu's CORE tab, picked on that machine.</summary>
    bool FabPanelOpen => _game.Menu.IsOpen && _game.Menu.Tab == MenuTab.Core && _game.Menu.Root.Core.Picked == _fab;

    IEnumerable<int> OpenFab()
    {
        foreach (var (x0, z0) in new[] { (0f, -1.4f), (1.6f, 0f), (-1.6f, 0f), (0f, 1.4f) })
        {
            Vector3 stand = _fab.GlobalTransform * new Vector3(x0, 0f, z0);
            foreach (int x in _play.WalkAround(_fab.GlobalPosition, stand, 2.6f)) yield return x;
            foreach (int x in _drive.WalkTo(stand, 0.4f, 15f)) yield return x;
            foreach (int x in _play.WaitFor(() => _game.Survivor.Body.OnFloor, 5)) yield return x;
            foreach (Vector3 aim in new[] { _fab.Front, _fab.GlobalTransform * new Vector3(0f, 1.1f, 0f) })
            {
                foreach (int x in _drive.LookAt(aim, 6)) yield return x;
                foreach (int x in _drive.Tap(Key.E)) yield return x;
                foreach (int x in _drive.Wait(2)) yield return x;
                if (FabPanelOpen) yield break;
            }
        }
    }

    IEnumerable<int> SurvivalConstructor()
    {
        GD.Print("-- 5. the suit fabricator makes a survival constructor; set it down --");
        foreach (int x in _play.OpenMenu(MenuTab.You)) yield return x;
        Check(_game.Menu.RectOf("tab:you") != null && _game.Menu.Root.You != null, "Tab opens the menu on YOU: bag, toolbar, paperdoll and the suit fabricator");
        foreach (int x in _play.Order("fab", "survival-constructor", 1)) yield return x;
        Check(_game.Fabricator.Orders.Any(j => j.Item.Id == "survival-constructor"), "ordered a survival constructor in the suit fabricator");
        foreach (int x in _play.CloseMenu()) yield return x;
        // It makes it while you walk to a flat spot near by (at about your own height: the hunt
        // may have ended at the foot of a cliff, with the pod up on top).
        Vector3 site = _play.FlatSpot(_game.Survivor.WorldFeet, 22f, within: 12f);
        foreach (int x in _drive.WalkTo(site, 5f, 60f)) yield return x;
        foreach (int x in _play.WaitFor(() => _game.Pack["survival-constructor"] > 0, 60)) yield return x;
        Check(_game.Pack["survival-constructor"] == 1, $"the fabricator finished it while the castaway walked ({_game.Stats.Seconds - _gameTimeAt.GetValueOrDefault("looted"):F0} s of play)");
        foreach (int x in _play.ToToolbar("survival-constructor", 2)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        Check(_game.Pack.Grid[2].Item == "survival-constructor", "dragged onto toolbar slot 3");
        Vector3 spot = _play.FlatSpot(_game.Survivor.WorldFeet, 4f, within: 6f);
        foreach (int x in _play.PlaceOnGround(2, spot)) yield return x;
        _fab = _game.Zone.Devices.FirstOrDefault();
        Check(_fab != null && _game.Pack["survival-constructor"] == 0, "3 and a click set it down on open ground");
        if (_fab == null) yield break;

        GD.Print("-- 6. the base core, made in the survival constructor --");
        // Up to its front, as you would to use it.
        foreach (int x in OpenFab()) yield return x;
        if (!FabPanelOpen)
        {
            bool seen = WorldRay.Cast(_game.Zone, _game.Survivor, 6f, Layers.World | Layers.Hull | Layers.Props, out LookHit look);
            GD.Print($"  (E at the constructor: the visor said \"{_said}\"; the look from {_game.Survivor.WorldEye()} {(seen ? $"meets {look.Collider} at {look.Distance:F1} m" : "meets nothing")}; the constructor's front {_fab.Front}, {_game.Survivor.WorldEye().DistanceTo(_fab.Front):F1} m off; opened {_game.Opened?.Title})");
            Vector3 f = _game.Survivor.WorldFeet;
            var q = PhysicsRayQueryParameters3D.Create(f + Vector3.Up * 0.3f, f + Vector3.Down * 3f, uint.MaxValue);
            var under = _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(q);
            GD.Print($"  (standing on {(under.Count > 0 ? ((Node)under["collider"].AsGodotObject()).GetPath().ToString() : "nothing")}, the ground {_game.Zone.GroundAt(f.X, f.Z).Y:F1} under feet at {f.Y:F1}, on floor {_game.Survivor.Body.OnFloor})");
        }
        Check(FabPanelOpen, "E on it opens the Tab menu's CORE panel on it: hopper, recipes and queue");
        foreach (int x in _drive.ClickWidget("storage:store-all")) yield return x;
        Check(_fab.Storage.Count("iron-ore") > 0 && _game.Pack["iron-ore"] == 0, $"STORE ALL loads the bag's ore into it ({_fab.Storage.UsedSu:F0} SU)");
        foreach (int x in _play.CloseMenu()) yield return x;
        // Its own screen, at eye height on its mast: step up to it, look at the base core's +1, click.
        foreach (int x in _drive.WalkTo(_fab.GlobalTransform * new Vector3(0f, 0f, -1.4f), 0.4f, 15f)) yield return x;
        _game.MachineScreens.Sync();
        MachineScreens.Mounted screen = _game.MachineScreens.Screens.TryGetValue((_fab, Int3.Zero), out var m) ? m : null;
        if (screen != null) foreach (int x in _play.ScreenClick(screen, "order:screen:base-core:1", "screen:down")) yield return x;
        bool onScreen = _fab.Constructor.Orders.Any(j => j.Item.Id == "base-core");
        Check(screen != null && onScreen && ScreenMount.AtEyeHeight(screen.Screen, _fab.GlobalPosition.Y, 0.15f, 15f),
            $"its own screen, at eye height: a cursor where you look, +1 on the base core clicked there");
        if (!onScreen)
        {
            foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
            foreach (int x in _play.Order("core", "base-core", 1)) yield return x;
            foreach (int x in _play.CloseMenu()) yield return x;
        }
        Check(_fab.Constructor.Orders.Any(j => j.Item.Id == "base-core") && _fab.Constructor.SubParts.Count > 0,
            $"a base core ordered; it planned {_fab.Constructor.SubParts.Count} lines of sub-parts by itself");
        foreach (int x in _play.WaitFor(() => _fab.Storage.Count("base-core") > 0, 120)) yield return x;
        Check(_fab.Storage.Count("base-core") == 1, $"the base core came out ({_game.Stats.Seconds - _gameTimeAt.GetValueOrDefault("looted"):F0} s of play since looting)");
        Mark("base core");
    }

    // ==== 7. The base ===============================================================================

    IEnumerable<int> Base()
    {
        GD.Print("-- 7. found a base and build it up --");
        // The base's blocks, crafted as items in the constructor's own lane. What's unlocked first;
        // the small constructor once level 2 and its node are in.
        foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
        Bill blocks = new();
        foreach (var (b, small, n) in BaseBlocks)
            if (b != "smallfab") blocks.Add(_play.BlockItem(b, small), n);
        foreach (int x in _play.MakeAll("core", _fab.Constructor, _fab.Storage, blocks, 240)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        Check(!_fab.Constructor.Busy && blocks.All(kv => _fab.Storage.Count(kv.Key) + _game.Pack[kv.Key] >= kv.Value),
            $"the survival constructor crafted the base's blocks as items: {_game.Zone.Data.Items.Describe(blocks, 5)}");
        // Unlock the small constructor (level 2) when the points allow, and craft it too.
        foreach (int x in _play.WaitFor(() => _game.Progress.Level >= 2, 30)) yield return x;
        Check(_game.Progress.Level >= 2, $"level {_game.Progress.Level} from playing ({_game.Progress.Xp} XP)");
        foreach (int x in _play.Unlock("smallfab@large")) yield return x;
        Check(_game.Progress.IsUnlocked("smallfab@large"), "the small constructor unlocked on the TECH tab");
        foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
        string smallfab = _play.BlockItem("smallfab", false);
        foreach (int x in _play.MakeAll("core", _fab.Constructor, _fab.Storage, Bill.Of((smallfab, 1)), 240)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        Check(_fab.Storage.Count(smallfab) == 1, "and the small constructor, a block item like the rest, came out of it");
        // Take what it made.
        // Up to its front, as you would to use it.
        foreach (int x in OpenFab()) yield return x;
        foreach (int x in _drive.ClickWidget("storage:open")) yield return x; // beside the bag, for the lot
        foreach (int x in _drive.ClickWidget("box:take-all")) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;

        foreach (int x in _play.ToToolbar("base-core", 3)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        Vector3 site = _play.FlatSpot(_fab.GlobalPosition, 14f, 1.2f, 0.5f);
        foreach (int x in _play.StandOff(site, _game.Survivor.WorldFeet, 6f)) yield return x;
        foreach (int x in _play.PlaceOnGround(3, site)) yield return x;
        _base = _game.Zone.Vessels.FirstOrDefault(v => v.IsBase && !v.IsRuin);
        Check(_base != null && _base.Structure.Mode == GalHack.Structures.CoreMode.Base && _base.Body.Motion == GalHack.Physics.Motion.Fixed,
            "the base core set down founds a base: large grid, base mode, a fixed body where it stands");
        if (_base == null) yield break;

        // The crafted stacks onto the toolbar, then onto the core.
        foreach (int x in _play.BlockToToolbar("container", false, 4)) yield return x;
        foreach (int x in _play.BlockToToolbar("generator", false, 5)) yield return x;
        foreach (int x in _play.BlockToToolbar("smallfab", false, 6)) yield return x;
        foreach (int x in _play.BlockToToolbar("steel", false, 8)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        Check(_game.Pack.Grid[4].Item == _play.BlockItem("container", false) && _game.Pack.Grid[6].Item == smallfab && _game.Pack.Grid[8].Count == 2, "block stacks dragged from the bag onto the toolbar");
        Vector3 core = _base.Body.Position.ToGodot();
        int placed = _game.Builder.Placed;
        var steps = new (int slot, Int3 on, Dir face)[]
        {
            (4, Int3.Zero, Dir.PosX), (5, Int3.Zero, Dir.NegX), (6, Int3.Zero, Dir.PosZ), (8, Int3.Zero, Dir.NegZ), (8, new Int3(0, 0, -1), Dir.NegZ),
        };
        foreach (var (slot, on, face) in steps)
        {
            Vector3 faceAt = _base.ToWorld(StructureBridge.CellCenter(_base.Structure, on) + face.Vec().ToVector3() * 1f);
            Vector3 outward = (faceAt - core) with { Y = 0f };
            foreach (int x in _play.WalkAround(core, faceAt + outward.Normalized() * 4f, 9f)) yield return x;
            foreach (int x in _play.PlaceBlock(slot, _base, on, face)) yield return x;
        }
        int built = _game.Builder.Placed - placed;
        Check(built == steps.Length, $"{built} large blocks built onto the base by looking and clicking, each using one from its stack");
        Check(_game.Pack.Grid[8].IsEmpty, "the steel stack is used up: its toolbar slot is empty, and an empty stack can't place");
        Check(_base.Machines.Count == 1 && _base.Powered && _base.Storage.CapacitySu > 0,
            $"the base has a constructor, power and storage ({_base.Storage.CapacitySu:F0} SU)");
        // Take one down with the tool's deconstruct mode: the block item comes back.
        string steel = _play.BlockItem("steel", false);
        int stock = _game.Pack[steel];
        foreach (int x in _play.Tool(ToolMode.Deconstruct)) yield return x;
        Vector3 far = _base.ToWorld(StructureBridge.CellCenter(_base.Structure, new Int3(0, 0, -2)) + new N.Vector3(0, 0, -1f));
        foreach (int x in _drive.LookAt(far, 6)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(!_base.Structure.Has(new Int3(0, 0, -2)) && _game.Pack[steel] == stock + 1, "the tool's deconstruct mode takes a block down and gives its item back to the bag");
        foreach (int x in _play.Tool(ToolMode.Gather)) yield return x;
        foreach (int x in _play.BlockToToolbar("steel", false, 8)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        foreach (int x in _play.PlaceBlock(8, _base, new Int3(0, 0, -1), Dir.NegZ)) yield return x;
        Check(_base.Structure.Has(new Int3(0, 0, -2)) && _game.Pack[steel] == stock, "and it goes back on");
        Mark("base");
    }

    // ==== 8. The small-vessel core ==================================================================

    IEnumerable<int> VesselCore()
    {
        GD.Print("-- 8. level 5, the small-vessel tech, and its core from the base's constructor --");
        // The vessel's parts first (they need no unlock): the base's own constructor makes them.
        Vector3 core = _base.Body.Position.ToGodot();
        foreach (int x in _drive.WalkTo(core, 6f, 30f)) yield return x;
        foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in Pick(_base)) yield return x;
        foreach (int x in _drive.ClickWidget("storage:store-raw")) yield return x;
        Check(_base.Storage.Count("iron-ore") > 0, $"STORE ORE, MATERIALS fills the base's storage ({_base.Storage.UsedSu:F0} SU)");
        Bill parts = new();
        foreach (var (b, small, n) in VesselBlocks) parts.Add(_game.Zone.Data.Items.Blocks.For(b, small), n);
        Bill blockParts = parts.Clone();
        // And what its core is made of, so making the core later leaves the blocks' parts whole.
        parts.Add(_game.Zone.Data.Items.Item("vessel-core").Recipe.InputsFor(1));
        GalHack.Items.Production.Constructor c = _base.Machines.Values.FirstOrDefault();
        if (c == null) { Fail("the base has no constructor"); yield break; }
        foreach (int x in _play.MakeAll("core", c, _base.Storage, parts, 300)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        Check(!c.Busy && c.Missing.IsEmpty, $"the base's small constructor made the vessel's parts: {_game.Zone.Data.Items.Describe(parts, 6)}");
        _ = blockParts;
        GD.Print($"  level {_game.Progress.Level}, {_game.Progress.Xp} XP, {_game.Progress.Points} points after {_game.Stats.Seconds / 60:F1} min of play");
        // Level 5 by playing: a deposit or two more to dig if it isn't there yet.
        for (int round = 0; round < 6 && _game.Progress.Level < 5; round++)
        {
            // Close to the base: what is left in the deposits already worked, then new ones near.
            DepositKind kind = new[] { DepositKind.Iron, DepositKind.Copper, DepositKind.Carbon }[round % 3];
            string ore = _game.Tool.OreOf(kind).Id;
            foreach (int x in _play.Gather(kind, _game.Pack[ore] + 60, 250f, retry: round < 3)) yield return x;
            GD.Print($"  (more digging: level {_game.Progress.Level}, {_game.Progress.Xp} XP)");
        }
        Check(_game.Progress.Level >= 5, $"level {_game.Progress.Level} earned by playing: {_game.Progress.Xp} XP in {_game.Stats.Seconds / 60:F1} min (test time-scale on crafting only)");
        Mark("level 5");
        foreach (string node in new[] { "vessel-core", "thruster@small", "gyro@small" })
        {
            foreach (int x in _play.Unlock(node)) yield return x;
            Check(_game.Progress.IsUnlocked(node), $"unlocked {_game.Progress.Node(node).Name} ({_game.Progress.Points} points left)");
        }
        for (int tries = 0; tries < 3 && _game.Survivor.WorldFeet.DistanceTo(core) > 12f; tries++)
            foreach (int x in _drive.WalkTo(core, 6f, 60f)) yield return x;
        Check(_game.Survivor.WorldFeet.DistanceTo(core) <= 12f, $"walked back to the base ({_game.Survivor.WorldFeet.DistanceTo(core):F0} m from its core)");
        foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in Pick(_base)) yield return x;
        foreach (int x in _play.Order("core", "vessel-core", 1)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        foreach (int x in _play.WaitFor(() => _base.Storage.Count("vessel-core") > 0, 120)) yield return x;
        Check(_base.Storage.Count("vessel-core") == 1, "the base's constructor made the small-vessel core");
        // The vessel's blocks, crafted as items from the parts already made, into the base's one storage.
        Bill vesselItems = new();
        foreach (var (b, small, n) in VesselBlocks) vesselItems.Add(_play.BlockItem(b, small), n);
        foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in Pick(_base)) yield return x;
        foreach (int x in _play.MakeAll("core", c, _base.Storage, vesselItems, 300)) yield return x;
        Check(!c.Busy && vesselItems.All(kv => _base.Storage.Count(kv.Key) >= kv.Value), $"the base's constructor crafted the vessel's blocks into the base's storage: {_game.Zone.Data.Items.Describe(vesselItems, 6)}");
        foreach (int x in Pick(_base)) yield return x;
        foreach (int x in _drive.ClickWidget("storage:open")) yield return x;
        foreach (int x in _drive.ClickWidget("box:take-all")) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        Check(_game.Pack["vessel-core"] == 1 && vesselItems.All(kv => _game.Pack[kv.Key] >= kv.Value), "its core and block stacks taken from the base's storage into the bag");
        Mark("vessel core");
    }

    IEnumerable<int> Pick(Vessel v)
    {
        List<object> near = _game.Menu.Root.Core.Near();
        int i = near.IndexOf(v);
        if (i >= 0) foreach (int x in _drive.ClickWidget($"core:pick:{i}")) yield return x;
    }

    // ==== 9. The vessel ===========================================================================

    IEnumerable<int> BuildVessel()
    {
        GD.Print("-- 9. build a vessel on its core --");
        foreach (int x in _play.ToToolbar("vessel-core", 3)) yield return x;
        foreach (var (block, slot) in Slots)
            foreach (int x in _play.BlockToToolbar(block, true, slot)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        Vector3 pad = _play.FlatSpot(_base.Body.Position.ToGodot(), 26f, 2.4f, 0.3f);
        GD.Print($"  (the vessel's pad {pad}; the base at {_base.Body.Position.ToGodot()}; the castaway at {_game.Survivor.WorldFeet})");
        foreach (int x in _play.StandOff(pad, _base.Body.Position.ToGodot(), 5f)) yield return x;
        foreach (int x in _play.PlaceOnGround(3, pad)) yield return x;
        _vessel = _game.Zone.Vessels.FirstOrDefault(v => !v.IsBase && !v.IsRuin && v.Structure.Grid.Size == GridSize.Small);
        if (_vessel == null) GD.Print($"  (no vessel: in hand {_game.Pack.Hand} {_game.Pack.InHand}, slot {_game.Pack.Selected}, says \"{_game.Builder.Verdict}\", vessel cores in the bag {_game.Pack["vessel-core"]}, {_game.Survivor.WorldFeet.DistanceTo(pad):F1} m from the pad)");
        Check(_vessel != null && _vessel.Controller.Frozen, "the small-vessel core set down starts a small vessel on its build stand");
        if (_vessel == null) yield break;
        Vessel v = _vessel;
        int ok = 0;
        foreach (int x in _play.BuildVessel(v, Slots, n => ok = n)) yield return x;
        HandlingStats st = v.Stats;
        GD.Print($"  {v.Name}: {v.Structure.Count} blocks, {st.Mass:F2} t, accel f/up {st.Accel(Direction.Forward):F1}/{st.Accel(Direction.Up):F1} m/s², turn {st.TurnRate:F0}°/s, lift margin {st.LiftMargin:F0} kN");
        int want = Playthrough.VesselLayout.Length;
        Check(ok == want && v.Seat != null && st.CanLiftOff && v.Controller.Gear?.Count == 3,
            $"the vessel built block by block from the toolbar: {ok} of {want} blocks, a seat, three legs, lift jets turned with R/T, and it can lift off ({st.LiftMargin:+0} kN)");
        Check(!InGround(v.Body), "built on its stand, it is never in the ground");
        Mark("vessel built");
    }

    bool InGround(Body b) => _game.Zone.Ground.IsInside(b, b.WorldPosition, b.Shape);

    IEnumerable<int> Fly()
    {
        Vessel v = _vessel;
        if (v == null) yield break;
        GD.Print("-- 9. fly it 500 m and land --");
        if (v.Seat == null) { Fail("the vessel has no seat to fly it from"); yield break; }
        foreach (int x in _drive.Board(v)) yield return x;
        Check(_game.Survivor.Seated == v, "F at its command chair");
        if (_game.Survivor.Seated != v) yield break;
        foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
        Check(_game.Menu.RectOf("core:power") != null, "the CORE tab shows the vessel: its four numbers and lift margin");
        Check(!_game.Suit.InSealedRoom, "a command chair in the open gives no air: only a sealed room does");
        foreach (int x in _play.CloseMenu()) yield return x;
        Vector3 start = v.Body.Position.ToGodot();
        _pad = start;
        Vector3 target = _play.FlatSpot(start, 560f, 0.5f, 0.3f);
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 120; i++) yield return 0;
        Check(!v.Controller.Frozen && v.Body.Position.Y > start.Y + 3f, $"Space lifts it off: {v.Body.Position.Y - start.Y:F1} m up after 2 s");
        float top = 0f;
        for (int t = 0; t < 60 * 120; t++)
        {
            Vector3 p = v.Body.Position.ToGodot(), to = target - p;
            float flat = new Vector2(to.X, to.Z).Length();
            if (flat < 30f) break;
            Steer(v, to, flat);
            // Height over the ground under it and over what it is flying at: at 70 m/s a hillside
            // arrives faster than a look straight down can tell, as a pilot looking ahead knows.
            float agl = v.AboveGround;
            for (float ahead = 1f; ahead <= 5f; ahead += 1f)
                agl = MathF.Min(agl, p.Y - _game.Zone.GroundAt(p.X + v.Body.Velocity.X * ahead, p.Z + v.Body.Velocity.Z * ahead).Y);
            _game.Controls.Hold(Key.Space, agl < 45f);
            _game.Controls.Hold(Key.Ctrl, agl > 80f);
            if (agl < 20f) _game.Controls.Hold(Key.W, false);
            top = MathF.Max(top, v.Body.Velocity.Length());
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        for (int t = 0; t < 60 * 90 && v.Controller is { Frozen: false }; t++)
        {
            Vector3 p = v.Body.Position.ToGodot(), to = target - p;
            Steer(v, to, new Vector2(to.X, to.Z).Length());
            _game.Controls.Hold(Key.W, false);
            float agl = v.AboveGround, vy = v.Body.Velocity.Y, wanted = agl > 20f ? -5f : agl > 5f ? -2.5f : -1.0f;
            if (t % 120 == 0) GD.Print($"  (landing: {agl:F1} m up, falling {-vy:F1} m/s, up {MathX.Up(v.Body.Rotation).Y:F2}, gear {v.Controller.Gear?.Deployed}, y {v.Body.Position.Y:F0})");
            _game.Controls.Hold(Key.Ctrl, vy > wanted + 0.4f);
            _game.Controls.Hold(Key.Space, vy < wanted - 0.4f);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(60)) yield return x;
        float flown = new Vector2(v.Body.Position.X - start.X, v.Body.Position.Z - start.Z).Length();
        Check(flown >= 500f, $"it flew {flown:F0} m, top speed {top:F0} m/s");
        Check(v.Controller.Frozen && v.Body.Damage == 0f, $"and landed on its gear, undamaged ({(v.Controller.Frozen ? "landed" : "still flying")}, {v.AboveGround:F1} m up, damage {v.Body.Damage:F2})");
        Mark("flown");
    }

    /// <summary>
    /// The flattest stretch of ground (24 m across) with nothing built on it, along the bunker's walkable
    /// ways, 20 to 120 m out, or round the places already walked to (the base, the fabricator, the pad).
    /// A fall that hits a slope of more than about 14 degrees only glances: the hit counts along the
    /// slope's normal, so a hillside world can leave nothing near the bunker that a fall kills on.
    /// </summary>
    Vector3 FlattestOut(Bunker b)
    {
        Vector3 best = b.Outside;
        float bestSpread = float.MaxValue;
        bool Clear(Vector3 p) => !b.Over(p, 8f) && _game.Zone.Devices.All(d => d.GlobalPosition.DistanceTo(p) > 8f)
            && _game.Zone.Vessels.All(v => v == _vessel || v.IsWreck || v.IsRuin || new Vector2(v.Body.Position.X - p.X, v.Body.Position.Z - p.Z).Length() > 24f);
        void Try(Vector3 p)
        {
            float h = _game.Zone.GroundAt(p.X, p.Z).Y, spread = 0f;
            if (h < 2f || !Clear(p)) return;
            for (int k = 0; k < 12; k++)
            {
                float a = k * MathF.Tau / 12f;
                foreach (float r in new[] { 4f, 8f, 12f })
                    spread = MathF.Max(spread, MathF.Abs(_game.Zone.GroundAt(p.X + MathF.Cos(a) * r, p.Z + MathF.Sin(a) * r).Y - h) / r);
            }
            if (spread < bestSpread) { bestSpread = spread; best = _game.Zone.GroundAt(p.X, p.Z); }
        }
        foreach (Vector3 way in b.Ways.Count > 0 ? b.Ways : new List<Vector3> { (b.Outside - b.StairTop).Normalized() })
            for (float d = 20f; d <= 120f; d += 10f) Try(b.Outside + way * d);
        var walked = new List<Vector3> { _pad };
        if (_base != null) walked.Add(_base.Body.Position.ToGodot());
        if (_fab != null) walked.Add(_fab.GlobalPosition);
        foreach (Vector3 place in walked)
            for (float d = 16f; d <= 64f; d += 8f)
                for (int k = 0; k < 16; k++)
                    Try(place + new Vector3(MathF.Cos(k * MathF.Tau / 16f), 0f, MathF.Sin(k * MathF.Tau / 16f)) * d);
        GD.Print($"  (the drop: over ground that rises at most {bestSpread:F2} m a metre, {best.DistanceTo(b.Outside):F0} m from the way out)");
        return best;
    }

    void Steer(Vessel v, Vector3 to, float flat)
    {
        N.Vector3 f = MathX.Forward(v.Body.Rotation);
        float heading = MathF.Atan2(-f.X, -f.Z), wanted = MathF.Atan2(-to.X, -to.Z);
        float err = Mathf.Wrap(wanted - heading, -MathF.PI, MathF.PI);
        _game.Controls.Look = new Vector2(Math.Clamp(-err * 12f, -4f, 4f), 0f);
        _game.Controls.Hold(Key.W, MathF.Abs(err) < 0.35f && flat > 60f);
    }

    // ==== 10. Menus, the torch, and death ============================================================

    IEnumerable<int> MenuAndTorch()
    {
        GD.Print("-- 10. every tab of the menu, and the head torch --");
        foreach (MenuTab tab in Enum.GetValues<MenuTab>())
        {
            foreach (int x in _play.OpenMenu(tab)) yield return x;
            Check(_game.Menu.Tab == tab && _game.Menu.Root.WidgetIds.Any(), $"the {tab.ToString().ToUpperInvariant()} tab opens by its own button");
        }
        foreach (int x in _play.CloseMenu()) yield return x;
        bool before = _game.Torch.Visible;
        foreach (int x in _drive.Tap(Key.L)) yield return x;
        Check(_game.Torch.Visible != before && _game.Torch.SpotRange > 20f, $"L toggles the head torch ({_game.Torch.SpotRange:F0} m, {_game.Torch.SpotAngle:F0}°)");
        foreach (int x in _drive.Tap(Key.L)) yield return x;
    }

    /// <summary>The jetpack up off the ground: rising fast is never a fall, so IMPACT stays off until the castaway comes down.</summary>
    IEnumerable<int> JetpackLiftOff()
    {
        GD.Print("-- the jetpack: IMPACT only when falling --");
        bool warnedRising = false;
        float top = 0f;
        // Full tanks first: the walk here may have burned them.
        foreach (int x in _play.WaitFor(() => _game.Survivor.Body.OnFloor && _game.Survivor.Legs.JetpackFuel >= _game.Survivor.Legs.JetpackFuelMax, 12)) yield return x;
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 && !_game.Suit.Dead; i++)
        {
            yield return 0;
            float up = _game.Survivor.Body.Velocity.Y;
            top = MathF.Max(top, up);
            if (up > 0.5f && (_game.Suit.Last.Warnings & SurvivalVitals.Model.Alert.Fall) != 0) warnedRising = true;
        }
        _game.Controls.Hold(Key.Space, false);
        foreach (int x in _play.WaitFor(() => _game.Survivor.Body.OnFloor, 10)) yield return x;
        Check(!warnedRising && top > 2f, $"lifting off on the jetpack (up to {top:F1} m/s) never flashes IMPACT");
    }

    IEnumerable<int> Death()
    {
        GD.Print("-- 10. dying, and the backpack recovered from its marker --");
        Vessel v = _vessel;
        if (v == null || _game.Survivor.Seated != v) yield break;
        // Up high (falls of a dozen metres are harmless; from 200 m nobody walks away), out of the seat, and a jump off the side.
        // Back over flat ground near home on the way up (a landing on a slope glances), so the walk
        // from the bunker to the backpack is a short one.
        // Over the flattest ground along a walkable way out of the bunker (a landing on a slope
        // glances off), so the walk from the bunker to the backpack is a short, sure one.
        Vector3 over = FlattestOut(_game.Bunker);
        for (int t = 0; t < 60 * 60; t++)
        {
            Vector3 to = over - v.Body.Position.ToGodot();
            float flat = new Vector2(to.X, to.Z).Length();
            N.Vector3 vel = v.Body.Velocity;
            float speed = new Vector2(vel.X, vel.Z).Length();
            if (v.AboveGround >= 220f && flat < 4f && speed < 3f) break;
            if (v.Body.Position.Y > _game.Zone.Physics.Zone.Ceiling - 500f) break;
            Steer(v, to, flat);
            // Right over it, not near it: forward while far and slow enough to stop, back thrust to brake.
            _game.Controls.Hold(Key.W, MathF.Abs(Mathf.Wrap(MathF.Atan2(-to.X, -to.Z) - MathF.Atan2(-MathX.Forward(v.Body.Rotation).X, -MathX.Forward(v.Body.Rotation).Z), -MathF.PI, MathF.PI)) < 0.35f && speed < flat * 0.5f);
            _game.Controls.Hold(Key.S, speed > flat * 0.6f + 1f);
            _game.Controls.Hold(Key.Space, v.AboveGround < 220f);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        // Hands off, it brakes to a hover (Ctrl's down jet if it still climbs).
        for (int t = 0; t < 60 * 15 && MathF.Abs(v.Body.Velocity.Y) > 2f; t++)
        {
            _game.Controls.Hold(Key.Ctrl, v.Body.Velocity.Y > 2f);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(30)) yield return x;
        Bill carried = _game.Pack.Storage.Contents;
        float high = v.AboveGround;
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(5)) yield return x;
        // Out of the seat on top of it: a slow step off the edge (a run-up would carry the fall
        // far off the flat ground below, and a landing on a slope glances).
        Vector3 off = v.ToWorld(new System.Numerics.Vector3(-8f, 0f, 0f));
        for (int t = 0; t < 120 && (_game.Survivor.Aboard == v || _game.Survivor.Body.Frame != null || _game.Survivor.Body.OnFloor); t++)
        {
            _drive.Face(new Vector3(off.X, _game.Survivor.WorldEye().Y, off.Z));
            _game.Controls.Hold(Key.W);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        _topFall = 0f;
        foreach (int x in _play.WaitFor(() => { _topFall = MathF.Max(_topFall, -_game.Survivor.Body.Velocity.Y); return _game.Suit.Dead; }, 30)) yield return x;
        // A suited body falls no faster than about 41 m/s, and boots and armour soften the
        // landing: a survivor on a sliver of health. A hop on the jetpack and the drop after it finish them.
        int falls = 1;
        for (int hop = 0; hop < 3 && !_game.Suit.Dead; hop++)
        {
            falls++;
            _game.Controls.Hold(Key.Space);
            for (int t = 0; t < 60 * 4; t++) yield return 0;
            _game.Controls.ReleaseAll();
            foreach (int x in _play.WaitFor(() => _game.Suit.Dead, 12)) yield return x;
        }
        if (!_game.Suit.Dead || _game.Backpack == null || !_game.Backpack.Storage.Contents.Equals(carried))
            GD.Print($"  (after the jump: health {_game.Suit.Vitals.Health:F0}, lost {string.Join(", ", _game.Suit.Lost.Select(kv => $"{kv.Value:F0} {kv.Key}"))}, top fall speed {_topFall:F1} m/s; dead {_game.Suit.Dead}, {_game.Survivor.WorldFeet.Y - _game.Zone.GroundAt(_game.Survivor.WorldFeet.X, _game.Survivor.WorldFeet.Z).Y:F0} m up, seated {_game.Survivor.Seated?.Name}, backpack {(_game.Backpack == null ? "none" : _game.Zone.Data.Items.Describe(_game.Backpack.Storage.Contents, 4))})");
        Check(_game.Suit.Dead && _game.Backpack != null && _game.Backpack.Storage.Contents.Equals(carried) && _game.Pack.Storage.IsEmpty,
            $"a fall from {high:F0} m onto flat ground{(falls > 1 ? $" and {falls - 1} more off the jetpack" : "")} kills the castaway: everything carried is in a backpack where they fell ({_game.Zone.Data.Items.Describe(carried, 4)})");
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.Tap(Key.R)) yield return x;
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(!_game.Suit.Dead && _game.Survivor.WorldFeet.DistanceTo(_game.Bunker.CryoFront) < 1.5f, "R: awake in the bunker, by the cryopod");
        Lootable pack = _game.Backpack;
        if (pack == null) yield break;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in _drive.WalkTo(pack.Position, 1.6f, 120f)) yield return x;
        if (_game.Survivor.WorldFeet.DistanceTo(pack.Position) > 2.2f) foreach (int x in _drive.WalkTo(pack.Position, 1.0f, 30f)) yield return x;
        foreach (int x in _play.WaitFor(() => _game.Survivor.Body.OnFloor, 5)) yield return x;
        foreach (int x in _drive.LookAt(pack.Position + Vector3.Up * 0.25f, 6)) yield return x;
        foreach (int x in _drive.Tap(Key.E)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        if (_game.Opened?.Loot != pack)
            GD.Print($"  (E at the backpack: \"{_said}\"; feet {_game.Survivor.WorldFeet}, the backpack at {pack.Position}, {_game.Survivor.WorldFeet.DistanceTo(pack.Position):F1} m; menu {_game.Menu.IsOpen})");
        foreach (int x in _drive.ClickWidget("box:take-all")) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        Check(_game.Pack.Storage.Contents.Equals(carried) && _game.Backpack == null, "walked to the backpack's marker: E and take all, everything back");
        Mark("recovered");
    }

    void Summary()
    {
        GD.Print("-- throughout --");
        Check(_rescues.All(r => r.Body != _game.Survivor.Body), $"the castaway never needed rescuing from the ground ({_rescues.Count} rescues of anything)");
        GD.Print($"  game time at each step (min): {string.Join(", ", _gameTimeAt.Select(kv => $"{kv.Key} {kv.Value / 60:F1}"))}");
        GD.Print($"  level {_game.Progress.Level}, {_game.Progress.Xp} XP; {_game.Stats.Crafted} items crafted, {_game.Builder.Placed} blocks placed, {_game.Tool.OreCells} ore cells dug, {_game.Stats.Kills} kills");
    }

    // ---- Reporting --------------------------------------------------------------------------

    void Check(bool ok, string what)
    {
        if (ok)
        {
            _passes++;
            GD.Print($"  ok    {what}");
        }
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.Print($"  FAIL  {what}");
    }
}
