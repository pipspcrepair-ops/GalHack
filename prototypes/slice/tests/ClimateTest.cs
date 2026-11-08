using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Game;
using Slice.Surface;
using Slice.Player;
using SurvivalVitals.Model;
using Weather.Model;
using Wildlife;
using WeatherKind = Weather.Model.WeatherKind;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The world pushing back, on a real world, through the game itself: swimming and going under.
///
/// Run: --headless --fixed-fps 60 --path prototypes/slice res://tests/ClimateTest.tscn
/// </summary>
public partial class ClimateTest : Node
{
    const int WatchdogMinutes = 15;
    Game.Game _game;
    Playthrough _play;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

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
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    SurfaceZone Zone => _game.Zone;
    Slice.Player.Survivor Me => _game.Survivor;

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = false, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _play = new Playthrough(_game);
        _drive = _play.Drive;
        while (_game.Phase != Phase.Playing) yield return 0;
        Zone.Weather.Bridge.Calm = true;
        Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Zone.Wildlife.Frozen = true;
        foreach (int x in _drive.UntilPlaying()) yield return x;
        foreach (int x in _play.WakeAndLeave((_, _) => { })) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in Sections()) yield return x;
    }

    IEnumerable<int> Sections()
    {
        Check(Zone.Weather.FallStopsOnContact, "rain, snow and dust die on what they hit: no cover check, a collision with whatever is there");
        foreach (int x in Rooms()) yield return x;
        foreach (int x in Swim()) yield return x;
        foreach (int x in Storms()) yield return x;
        foreach (int x in Lava()) yield return x;
        foreach (int x in Node()) yield return x;
        foreach (int x in Cells()) yield return x;
    }

    /// <summary>A shut box of steel on open ground, in a monsoon: nothing of the storm gets in, and it is all still out there.</summary>
    IEnumerable<int> Rooms()
    {
        foreach (int x in OnOpenGround(Me.WorldFeet)) yield return x;
        Weather.Model.WeatherSim sky = Zone.Weather.Sim;
        sky.Snap(WeatherKind.Monsoon);
        foreach (int x in _drive.Wait(60)) yield return x;
        Vector3 feet = Me.WorldFeet;
        Check(Zone.Weather.SkyAtEye > 0.9f && Zone.Weather.RainShown && Zone.Weather.SplashShown, $"in the open a monsoon falls and splashes around the eye (sky {Zone.Weather.SkyAtEye:F2})");
        Zone.Weather.StrikeAt(feet + new Vector3(12f, 0f, 0f));
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(Zone.Weather.StrikeLight > 1f, $"and a strike nearby lights the ground ({Zone.Weather.StrikeLight:F1})");

        var text = new System.Text.StringBuilder("schema galhack.blueprint\nversion 1\nname \"Shut\"\ngrid large\nmode base\ngravity off\n");
        for (int y = 0; y <= 4; y++)
        for (int z = -3; z <= 3; z++)
        for (int x = -3; x <= 3; x++)
            if (y == 0 || y == 4 || Math.Abs(x) == 3 || Math.Abs(z) == 3) text.Append($"block {x} {y} {z} steel\n");
        text.Append("block 2 1 2 core\n");
        var shut = Blueprint.Load(text.ToString(), Zone.Data.Catalogue, out List<string> errors);
        Check(shut != null, $"the room's blueprint loads {string.Join("; ", errors)}");
        Vector3 site = Zone.GroundAt(feet.X + 30f, feet.Z);
        Slice.Vessels.Vessel room = Slice.Vessels.Vessel.SpawnBase(Zone, shut, (site + Vector3.Up * 4f).ToNumerics(), 0f);
        Vector3 inside = room.ToWorld(new N.Vector3(0f, 3f, 0f));
        Me.PlaceOutside(inside.ToNumerics());
        Zone.Light.Touch();
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(120)) yield return x;
        Check(Zone.Light.EyeOpen < 0.05f, $"in a sealed room the eye sees no sky ({Zone.Light.EyeOpen:F3})");
        Check(Zone.Weather.SkyAtEye < 0.5f && !Zone.Weather.RainShown && !Zone.Weather.SplashShown, $"so no drop and no splash is drawn there (sky {Zone.Weather.SkyAtEye:F2})");
        Zone.Weather.StrikeAt(Me.WorldFeet + new Vector3(12f, 0f, 0f));
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(Zone.Weather.StrikeLight < 0.01f, $"and a strike right outside sheds no light in there ({Zone.Weather.StrikeLight:F2})");

        sky.Snap(WeatherKind.Blizzard);
        double health = _game.Suit.Vitals.Health;
        foreach (int x in _drive.Wait(180)) yield return x;
        Check(health - _game.Suit.Vitals.Health < 1, $"a blizzard outside takes nothing from anyone in there ({health - _game.Suit.Vitals.Health:F1} in 3 s)");
        sky.Snap(WeatherKind.Clear);
        Me.PlaceOutside((feet + Vector3.Up * 0.5f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
    }

    /// <summary>A storm is a place in the sky: its rain, strikes and funnels come from its cells, and its thunder takes its time.</summary>
    IEnumerable<int> Cells()
    {
        Weather.Model.WeatherSim sky = Zone.Weather.Sim;
        // Loose cells, the weather's own: a thunderstorm otherwise comes on a front (its deck and its
        // line of cells are checked in GlobeTest and the weather prototype), and a front left from the
        // storms before would hold its weather kilometres off.
        sky.FrontsOff = true;
        sky.ClearFront();
        sky.ClearCells();
        sky.Snap(WeatherKind.Thunderstorm);
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(sky.Cells.Count > 0 && sky.CellCover(Me.WorldFeet) > 0.2f, $"a storm that arrives at once has its first cell overhead ({sky.Cells.Count} in the sky)");
        StormCell over = sky.Cells[0];
        Vector3 outside = over.Position + Vector3.Right * (over.Radius + 1500f);
        Check(sky.RainAt(over.Position) > sky.Current.Rain * 0.95f && sky.RainAt(outside) < sky.Current.Rain * 0.25f,
            $"it rains from the cell: {sky.RainAt(over.Position):F2} under it, {sky.RainAt(outside):F2} a way off");

        // Moved well off: the strikes go there, every one under a cell.
        sky.ClearCells();
        StormCell off = sky.AddCell(Me.WorldFeet + new Vector3(1400f, 0f, 0f), 1f);
        int strikes = 0, underCell = 0;
        var seen = new HashSet<int>();
        for (int i = 0; i < 2400 && strikes < 4; i++)
        {
            foreach (Hazard h in sky.Hazards)
                if (h.Kind == HazardKind.Lightning && seen.Add(h.Id))
                {
                    strikes++;
                    foreach (StormCell c in sky.Cells)
                        if (new Vector2(h.Center.X - c.Position.X, h.Center.Z - c.Position.Z).Length() < c.Radius) { underCell++; break; }
                }
            yield return 0;
        }
        Check(strikes > 0 && underCell == strikes, $"lightning comes from the cells: {underCell} of {strikes} strikes under one");

        Tornado funnel = sky.SpawnTornado(off.Position, off.Position);
        Check(sky.Cells.Any(c => c.Funnel == funnel), "a funnel hangs from a cell");
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(sky.Cells.Any(c => c.Funnel == funnel && new Vector2(c.Position.X - funnel.Position.X, c.Position.Z - funnel.Position.Z).Length() < 1f), "and the cell stays over it");
        sky.ClearTornadoes();

        // The storm's own strikes and thunder gone first: this one is counted alone.
        sky.Snap(WeatherKind.Clear);
        for (int i = 0; i < 3600 && (Zone.Weather.ThunderComing > 0 || Zone.Weather.Hazards.Count > 0); i++) yield return 0;
        Vector3 eye = _game.Camera.GlobalPosition;
        int coming = Zone.Weather.ThunderComing;
        Zone.Weather.Thunder(eye + new Vector3(1000f, 0f, 0f));
        Check(Zone.Weather.ThunderComing == coming + 1 && MathF.Abs(SurfaceWeather.ThunderDelay(1000f) - 2.9f) < 0.1f, "a strike a kilometre off is seen at once, its thunder on its way");
        foreach (int x in _drive.Wait(120)) yield return x;
        bool still = Zone.Weather.ThunderComing >= coming + 1;
        foreach (int x in _drive.Wait(120)) yield return x;
        Check(still && Zone.Weather.ThunderComing <= coming, $"and arrives about three seconds later (still on its way at 2 s: {still}; {coming} before it, {Zone.Weather.ThunderComing} left at 4 s)");
        sky.Snap(WeatherKind.Clear);
        sky.ClearCells();
        sky.FrontsOff = false;
    }

    /// <summary>The climate node: bench-made, in the hand it sets down like a device, and only burning does it hold the air.</summary>
    IEnumerable<int> Node()
    {
        var items = Zone.Data.Items;
        Check(items.TryGet(Slice.Vessels.Device.ClimateItem, out _), "a climate node is an item, made at the survival constructor");
        _game.Pack.Add(Slice.Vessels.Device.ClimateItem, 1);
        _game.Pack.Grid.Reconcile();
        int slot = -1;
        for (int i = 0; i < Slice.Items.PlayerInventory.ToolbarSlots; i++)
            if (_game.Pack.Grid[i].Item == Slice.Vessels.Device.ClimateItem) slot = i;
        if (slot >= 0)
        {
            _game.Select(slot);
            Check(_game.Pack.Hand == Slice.Items.HandKind.Device, "held, it is set down like a device");
        }
        var rules = Zone.Data.Rules.ClimateNode;
        Vector3 at = Me.WorldFeet + Vector3.Right * 2f;
        Slice.Vessels.Device node = Slice.Vessels.Device.PlaceClimate(Zone, at, 0f);
        var here = new Pos(Me.WorldFeet.X, Me.WorldFeet.Y, Me.WorldFeet.Z);
        double raw = _game.Suit.Exposure(here, _game.Suit.Sim.Time).Temperature;
        (double low, double high) = (rules.LowC, rules.HighC);
        // STAGING: a band away from whatever the air is here, so holding it shows.
        rules.LowC = raw + 5;
        rules.HighC = raw + 9;
        foreach (int x in _drive.Wait(5)) yield return x;
        Check(!node.Burning && Math.Abs(_game.Suit.Exposure(here, _game.Suit.Sim.Time).Temperature - raw) < 0.5, "with nothing in its burner the node does nothing");
        node.Storage.Add("biomass", 1);
        foreach (int x in _drive.Wait(5)) yield return x;
        double held = _game.Suit.Exposure(here, _game.Suit.Sim.Time).Temperature;
        Check(node.Burning && held == raw + 5 && node.Storage.Count("biomass") == 0, $"fed, it burns the fuel and steadies the air: {raw:F1} to {held:F1} Â°C");
        var farOff = new Pos(here.X + rules.Radius + 3, here.Y, here.Z);
        Check(_game.Suit.Exposure(farOff, _game.Suit.Sim.Time).Temperature < held, "a few metres outside its radius the air is the world's own");
        node.Tick(rules.Fuels["biomass"] + 1);
        Check(!node.Burning, "the fuel burns out");
        (rules.LowC, rules.HighC) = (low, high);
    }

    /// <summary>Lava worlds only: pass --planet lava. The real distance reaches the visor, and beside it and in it health goes fast.</summary>
    IEnumerable<int> Lava()
    {
        if (!Zone.Water.IsLava)
        {
            GD.Print("  (no lava on this world: run with -- --planet lava to try it)");
            yield break;
        }
        Vector3 near = Me.WorldFeet, shore = default, inside = default;
        bool found = false;
        for (float r = 30f; r < 3000f && !found; r += 15f)
        for (int i = 0; i < 64 && !found; i++)
        {
            float a = i * MathF.Tau / 64f, x = near.X + MathF.Cos(a) * r, z = near.Z + MathF.Sin(a) * r;
            if (!Zone.Water.LiquidAt(x, z, out float surface)) continue;
            Vector3 dir = new Vector3(near.X - x, 0f, near.Z - z).Normalized();
            for (float out1 = 2f; out1 < 40f; out1 += 1f)
            {
                Vector3 p = new(x + dir.X * out1, 0f, z + dir.Z * out1);
                if (Zone.Water.LiquidAt(p.X, p.Z, out _)) continue;
                float g = Zone.GroundAt(p.X, p.Z).Y;
                if (g > surface + 3f) break;
                shore = new Vector3(p.X, g, p.Z);
                inside = new Vector3(x, surface, z);
                found = true;
                break;
            }
        }
        if (!found)
        {
            GD.Print("  (no lava shore within 3 km on this world: not tried)");
            yield break;
        }
        Exposure far = _game.Suit.Exposure(new Pos(near.X, near.Y, near.Z), _game.Suit.Sim.Time);
        Exposure at = _game.Suit.Exposure(new Pos(shore.X, shore.Y, shore.Z), _game.Suit.Sim.Time);
        Check(at.LavaMetres <= 3, $"at the shore the suit reads lava {at.LavaMetres:F0} m off ({(double.IsInfinity(far.LavaMetres) ? "none" : far.LavaMetres.ToString("F0") + " m")} where the castaway stood)");
        Check(_game.Suit.Exposure(new Pos(inside.X, inside.Y, inside.Z), _game.Suit.Sim.Time).LavaMetres == 0, "in it the distance is nothing");
        // STAGING: the castaway set down on the shore, then in it.
        Me.PlaceOutside((shore + Vector3.Up * 0.2f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        _game.Suit.Vitals.Health = _game.Suit.Sim.Data.Vitals.Health.Max;
        double before = _game.Suit.Vitals.Health;
        foreach (int x in _drive.Wait(90)) yield return x;
        Check(before - _game.Suit.Vitals.Health >= 8 && (_game.Suit.Last.Warnings & Alert.Lava) != 0, $"beside lava health drains fast: {before - _game.Suit.Vitals.Health:F0} in 1.5 s, the visor flashing");
        before = _game.Suit.Vitals.Health = _game.Suit.Sim.Data.Vitals.Health.Max;
        Me.PlaceOutside((inside + Vector3.Down * 0.3f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(before - _game.Suit.Vitals.Health >= 15 && (_game.Suit.Last.Harm & Alert.Lava) != 0, $"in lava it goes faster still: {before - _game.Suit.Vitals.Health:F0} in 0.5 s");
    }

    /// <summary>STAGING: this planet's water is declared ice for a moment: a temperate world's water stands in.</summary>
    IEnumerable<int> IceWater(Vector3 water)
    {
        var rules = Zone.Data.Rules.Climate;
        double held = rules.IceWaterC;
        rules.IceWaterC = 100;
        double before = _game.Suit.Vitals.Health = _game.Suit.Sim.Data.Vitals.Health.Max;
        foreach (int x in _drive.Wait(30)) yield return x;
        bool warned = (_game.Suit.Last.Warnings & Alert.Cold) != 0 && _game.Suit.Vitals.Health >= before;
        foreach (int x in _drive.Wait(240)) yield return x;
        rules.IceWaterC = held;
        Check(warned && before - _game.Suit.Vitals.Health >= 12, $"ice water: the visor warns first, then health goes fast: {before - _game.Suit.Vitals.Health:F0} in 4 s");
    }

    /// <summary>Back on dry open ground, whole and fed: each section starts as a castaway would.</summary>
    IEnumerable<int> OnOpenGround(Vector3 near)
    {
        Vector3 spot = Open(near);
        Me.PlaceOutside((spot + Vector3.Up * 0.3f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        _game.Suit.Vitals.Health = _game.Suit.Sim.Data.Vitals.Health.Max;
        _game.Suit.Vitals.Normalise();
    }

    Vector3 Open(Vector3 near)
    {
        for (float r = 0f; r < 400f; r += 12f)
        for (int i = 0; i < 16; i++)
        {
            Vector3 g = Zone.GroundAt(near.X + MathF.Cos(i * 0.39f) * r, near.Z + MathF.Sin(i * 0.39f) * r);
            if (g.Y > 1.5f && !Zone.Water.LiquidAt(g.X, g.Z, out _) && _game.Places?.PlaceAt(g) == null) return g;
        }
        return Zone.GroundAt(near.X, near.Z);
    }

    IEnumerable<int> Storms()
    {
        foreach (int x in OnOpenGround(Me.WorldFeet)) yield return x;
        Weather.Model.WeatherSim sky = Zone.Weather.Sim;
        double max = _game.Suit.Sim.Data.Vitals.Health.Max;

        // The weather itself, not the region's take on it: on warm low ground a blizzard falls as rain
        // and wind and freezes no one (RegionTest checks that rule).
        var land = sky.Land;
        sky.Land = null;
        foreach (var (kind, alert, name) in new[] { (WeatherKind.Blizzard, Alert.Cold, "blizzard"), (WeatherKind.FireStorm, Alert.Heat, "fire storm") })
        {
            _game.Suit.Vitals.Health = max;
            _game.Suit.Vitals.Normalise();
            sky.Snap(kind);
            double at = _game.Suit.Vitals.Health;
            foreach (int x in _drive.Wait(300)) yield return x;
            double lost = at - _game.Suit.Vitals.Health;
            Check(lost >= 15 && (_game.Suit.Last.Warnings & alert) != 0 && !_game.Suit.Dead,
                $"out in a {name} health drains fast: {lost:F0} in 5 s, the visor warning {alert}");
            sky.Snap(WeatherKind.Clear);
            foreach (int x in _drive.Wait(5)) yield return x;
        }
        sky.Land = land;

        // A hole dug deep enough: a shaft down through the ground, by the planet's own edits.
        Vector3 top = Me.WorldFeet;
        var center = TerrainDigging.Terrain.Int3.Floor((top + Vector3.Down * 2f).ToNumerics());
        for (int d = 0; d < 6; d++) Zone.Edits.Dig(new TerrainDigging.Terrain.Int3(center.X, center.Y - d, center.Z), 2.4f);
        // STAGING: set down in the shaft (whatever else stood on the spot stays where it stood).
        Me.PlaceOutside((top + Vector3.Down * 5f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(90)) yield return x;
        Vector3 bottom = Me.WorldFeet;
        Check(_game.Sheltered(), $"down a hole {Zone.GroundAt(top.X, top.Z).Y - bottom.Y:F1} m deep counts as shelter");
        _game.Suit.Vitals.Health = max;
        _game.Suit.Vitals.Normalise();
        sky.Snap(WeatherKind.Blizzard);
        foreach (int x in _drive.Wait(240)) yield return x;
        double kept = max - _game.Suit.Vitals.Health;
        Check(kept < 1 && !_game.Suit.Lost.ContainsKey(DamageKind.Debug), $"in the hole a blizzard takes nothing: {kept:F1} health in 4 s");

        // Wind can't reach down there, a hurricane or a funnel right on top.
        sky.Snap(WeatherKind.Hurricane);
        Zone.Weather.Bridge.Calm = false;
        Tornado funnel = sky.SpawnTornado(bottom, bottom);
        funnel.SkipFormation();
        foreach (int x in _drive.Wait(30)) yield return x;
        N.Vector3 still = Me.Body.WorldPosition;
        foreach (int x in _drive.Wait(150)) yield return x;
        float moved = (Me.Body.WorldPosition - still).Length();
        Check(moved < 0.3f, $"a hurricane and a funnel overhead don't touch anyone in shelter: moved {moved:F2} m in 2.5 s");
        sky.ClearTornadoes();
        Zone.Weather.Bridge.Calm = true;

        // A tunnel off the bottom of the shaft: with a gale over it, its mouth moans.
        var walls = new float[16];
        Array.Fill(walls, 3f);
        walls[4] = 25f;
        Check(WindHowl.FindGap(walls, out _) == 4 && WindHowl.FindGap(new float[16], out _) < 0 && WindHowl.FindGap(new[] { 28f, 28f, 28f, 28f, 28f, 28f, 28f, 28f, 28f, 28f, 28f, 28f, 28f, 28f, 28f, 28f }, out _) < 0,
            "an opening is a long line with walls both sides of it: not a closed room, not open ground");
        var floor = TerrainDigging.Terrain.Int3.Floor((bottom + Vector3.Up * 1.2f).ToNumerics());
        for (int k = 0; k < 19; k++) Zone.Edits.Dig(new TerrainDigging.Terrain.Int3(floor.X + k, floor.Y, floor.Z), 1.5f);
        Me.PlaceOutside((bottom + new Vector3(17.5f, 0.5f, 0f)).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(120)) yield return x;
        Check(_game.Howl.Opening != null && _game.Howl.Level > 0.1f, $"at the end of a tunnel in a gale the wind howls at its mouth: level {_game.Howl.Level:F2}");
        sky.Snap(WeatherKind.Monsoon);
        foreach (int x in _drive.Wait(60)) yield return x;
        Check(!Zone.Weather.RainShown && !Zone.Weather.SplashShown, $"and no rain reaches that far into a cave (eye open {Zone.Light.EyeOpen:F3})");
        sky.Snap(WeatherKind.Clear);
        foreach (int x in _drive.Wait(150)) yield return x;
        Check(_game.Howl.Level < 0.05f, "and the howl dies with the wind");
        foreach (int x in Ravage()) yield return x;
    }

    /// <summary>A funnel set down among trees and an animal: the trees go, for good, and it is thrown.</summary>
    IEnumerable<int> Ravage()
    {
        foreach (int x in OnOpenGround(Me.WorldFeet + new Vector3(30f, 0f, 30f))) yield return x;
        Weather.Model.WeatherSim sky = Zone.Weather.Sim;
        Vector3 stand = Me.WorldFeet;
        long tree = 0;
        Vector3 trunk = default;
        foreach (var (solid, offset) in Zone.Streamer.SolidsNear(stand, 90f))
        {
            if (!solid.Fellable || (solid.Center + offset).DistanceTo(stand) < 30f) continue;
            tree = solid.Id;
            trunk = solid.Center + offset;
            break;
        }
        if (tree == 0)
        {
            GD.Print("  (no tree within reach on this world: the tornado's trail is not tried)");
            yield break;
        }
        WildlifePatch patch = Zone.Wildlife;
        Species kind = patch.Fauna.Species.Find(s => s.Body.Size < 2f && s.Rig == RigKind.Legged) ?? patch.Fauna.Species[0];
        Creature beast = patch.Sim.Spawn(kind, patch.PatchLocal(trunk + Vector3.Right * 2f));
        Vector3 from = beast.Position;
        int before = Zone.Weather.TreesTorn;
        Tornado funnel = sky.SpawnTornado(trunk, trunk);
        funnel.SkipFormation();
        bool flew = false;
        float highest = 0f;
        for (int i = 0; i < 240; i++)
        {
            if (beast.Thrown) { flew = true; highest = MathF.Max(highest, beast.Position.Y - from.Y); }
            yield return 0;
        }
        sky.ClearTornadoes();
        Check(Zone.Planet.Felled.Contains(tree) && Zone.Weather.TreesTorn > before, $"the funnel tears trees up through the felled list: {Zone.Weather.TreesTorn - before} down around it");
        Check(flew && highest > 1.5f, $"and flings a {kind.CommonName}: {highest:F1} m up");
        beast.Removed = true;
    }

    /// <summary>The nearest deep water to the castaway, found in the generator's own columns; null on a dry world.</summary>
    public static Vector3? DeepWater(SurfaceZone zone, Vector3 near, float depth, float within)
    {
        for (float r = 40f; r <= within; r += 40f)
        for (int i = 0; i < 48; i++)
        {
            float a = i * MathF.Tau / 48f, x = near.X + MathF.Cos(a) * r, z = near.Z + MathF.Sin(a) * r;
            if (zone.Water.IsLava || !zone.Water.LiquidAt(x, z, out float surface, out float ground) || surface - ground < depth) continue;
            return new Vector3(x, surface, z);
        }
        return null;
    }

    IEnumerable<int> Swim()
    {
        if (DeepWater(Zone, Me.WorldFeet, 5f, 3000f) is not Vector3 water)
        {
            GD.Print("  (no deep water within 3 km on this world: going under is not tried)");
            yield break;
        }
        // STAGING: set down over the water (swimming is tested, not the walk there).
        Me.PlaceOutside((water + Vector3.Up * 2f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(120)) yield return x;
        bool floats = Me.Legs.Swimming && !Me.Body.Water.Underwater && Me.WorldEye().Y > water.Y;
        Check(floats, $"dropped in, the castaway floats and swims with the head out: swimming {Me.Legs.Swimming}, eye {Me.WorldEye().Y - water.Y:F2} m over the surface");
        Check(!Zone.Underwater.CameraUnder, "with the head out the view stays above the surface");

        double oxygen = _game.Suit.Vitals.Oxygen;
        _game.Controls.Hold(Key.Ctrl);
        for (int i = 0; i < 600 && !(Zone.Underwater.CameraUnder && Me.WorldEye().Y < water.Y - 1.5f); i++) yield return 0;
        foreach (int x in _drive.Wait(240)) yield return x;
        _game.Controls.ReleaseAll();
        Exposure air = _game.Suit.Last.Exposure;
        Zone.Weather.Sim.Snap(WeatherKind.Monsoon);
        foreach (int x in _drive.Wait(2)) yield return x;
        bool rainUnder = !Zone.Weather.RainShown && !Zone.Weather.SplashShown;
        Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Check(Me.Body.Water.Underwater && Zone.Underwater.CameraUnder, $"Ctrl dives: wholly under, {Zone.Underwater.Depth:F1} m down, and the view has gone under with it");
        Check(_game.Camera.Environment != null, "under the surface the camera sees the murk, not the sky");
        Check(air.OxygenPercent == 0 && _game.Suit.Sim.Player.Gear.HelmetOn && _game.Suit.Vitals.Oxygen < oxygen,
            $"nothing to breathe under water: the helmet sealed and the suit's oxygen falls ({oxygen:F1} to {_game.Suit.Vitals.Oxygen:F1})");
        for (int i = 0; i < 1200 && Me.Body.Water.Underwater; i++) yield return 0;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(rainUnder, "under the surface a monsoon overhead draws no drop");
        Check(!Me.Body.Water.Underwater && !Zone.Underwater.CameraUnder && _game.Camera.Environment == null, "let go, the body floats back up and the sky returns");

        foreach (int x in IceWater(water)) yield return x;

        // A land animal driven out over the water floats on it at the most, never sinks.
        WildlifePatch patch = Zone.Wildlife;
        patch.FinishFollowing();
        foreach (int x in _drive.Wait(5)) yield return x;
        Vector3 local = patch.PatchLocal(Me.WorldFeet);
        if (patch.Fauna.Species.Count > 0 && patch.Sim.Terrain.Inside(local, 20f))
        {
            Species s = patch.Fauna.Species[0];
            Creature c = patch.Sim.Spawn(s, local);
            float below = water.Y - patch.FromPatch(c.Position).Y;
            Check(below <= WildlifePatch.Draught + 0.05f, $"a land animal over deep water floats: its feet {below:F2} m under the surface");
            c.Removed = true;
        }
    }
}
