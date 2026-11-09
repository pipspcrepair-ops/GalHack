using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Arms;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Arms;
using Slice.Game;
using Slice.Items;
using Slice.Surface;
using Slice.Vessels;
using Wildlife;
using HitKind = GalHack.Arms.HitKind;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// The first twelve weapons, in the slice's real world (a fresh random one every run). Each is
/// fired at a creature, a block and a vessel, and its parts are checked doing what they say:
/// magazine and reload, fire modes, pellets and shell types, gravity arcs and bounces, the blast,
/// the backblast, the mine sticking where it lands (riding a vessel that moves, falling when its
/// block goes) and tripping in its radius, charge stages and pierce, the arc's jumps, the swarm
/// spreading and stinging, the worm sticking and ticking down, homing turning, the cone hitting
/// only inside its arc and scaring what it burns, and self-harm. Then 20 rounds and a swarm in
/// flight are timed against a budget, with nothing allocated in the shots' step. Models and
/// sounds are checked for every weapon. Quits itself (watchdog).
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/WeaponTest.tscn
/// </summary>
public partial class WeaponTest : Node
{
    const int WatchdogMinutes = 12;
    /// <summary>The shots' step for 20 rounds and a swarm in flight may take this long on average, ms.</summary>
    const double StepBudgetMs = 1.5;

    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly List<(HitKind Kind, object Thing, float Amount)> _dealt = new();

    SurfaceZone Zone => _game.Zone;
    ArmsHost Arms => _game.Arms;
    WildlifePatch Wild => Zone.Wildlife;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true, Name = "WeaponTest watchdog" };
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
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (ok) _passes++;
        else _failures++;
    }

    // ---- Staging ----------------------------------------------------------------------------

    Vector3 Feet => _game.Survivor.WorldFeet;
    Vector3 Eye => _game.Survivor.WorldEye();

    /// <summary>A heading from the player along which the ground is open and near level out to a distance.</summary>
    Vector3 Bearing(float distance, float from = 0f, params Vector3[] avoid) => Bearing(distance, false, from, avoid);

    /// <summary>The same, and open behind as well when <paramref name="both"/> (a backblast needs room).</summary>
    Vector3 Bearing(float distance, bool both, float from, params Vector3[] avoid)
    {
        Vector3 best = Vector3.Forward;
        float bestScore = float.MaxValue;
        for (int i = 0; i < 24; i++)
        {
            float a = from + i * MathF.Tau / 24f;
            var d = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a));
            if (avoid.Any(v => v.Dot(d) > 0.5f)) continue; // 60 degrees clear of the others' headings
            float worst = 0f;
            for (float r = 2f; r <= distance + 4f; r += 2f)
            {
                Vector3 p = Feet + d * r;
                worst = MathF.Max(worst, MathF.Abs(Zone.GroundAt(p.X, p.Z).Y - Feet.Y));
            }
            Vector3 end = Zone.GroundAt(Feet.X + d.X * distance, Feet.Z + d.Z * distance) + Vector3.Up * 1.2f;
            bool blocked = Arms.Sweep(Eye.ToNumerics(), end.ToNumerics(), null, 0, 0f, out ShotHit h) && h.Kind is HitKind.Ground or HitKind.Block && h.Distance < Eye.DistanceTo(end) - 1f;
            float score = worst + (blocked ? 100f : 0f);
            if (both)
            {
                Vector3 back = Zone.GroundAt(Feet.X - d.X * 4f, Feet.Z - d.Z * 4f) + Vector3.Up * 1.2f;
                if (Arms.Sweep(Eye.ToNumerics(), back.ToNumerics(), null, 0, 0f, out ShotHit hb) && hb.Kind is HitKind.Ground or HitKind.Block) score += 100f;
            }
            if (score < bestScore) { bestScore = score; best = d; }
        }
        return best;
    }

    Species _kind;

    /// <summary>A sturdy test animal standing still at a point (wildlife frozen): plenty of health so every weapon's hits can be counted.</summary>
    Creature Animal(Vector3 at, float health = 4000f)
    {
        _kind ??= MakeKind();
        Creature c = Wild.Sim.Spawn(_kind, Wild.PatchLocal(Zone.GroundAt(at.X, at.Z)));
        c.Health = health;
        return c;
    }

    Species MakeKind()
    {
        List<Species> all = Wild.Fauna.Species;
        Species s = all.FirstOrDefault(x => x.Rig == RigKind.Legged && x.Domestic == DomesticKind.None && !x.Flies && x.Body.Size is > 0.8f and < 2.6f)
                    ?? all.FirstOrDefault(x => !x.Flies && x.Rig != RigKind.Swarm && x.Domestic == DomesticKind.None) ?? all[0];
        Species k = s.Copy();
        k.Stats.MaxHealth = 5000f;
        k.Behaviour = Behaviour.Grazer;
        k.Stats.Hunts = false;
        k.Stats.Retaliates = false;
        k.Stats.Damage = 0f;
        k.Stats.FleeRadius = 0f;
        k.Activity = Wildlife.Activity.Cathemeral;
        GD.Print($"      test animal: {k.CommonName} ({k.Rig}, size {k.Body.Size:F1})");
        return k;
    }

    /// <summary>Where to aim at a creature: the middle of its biggest hit sphere (its body, not the gap under it).</summary>
    Vector3 Centre(Creature c)
    {
        Wild.SimOf(c, out Vector3 origin, out _);
        int best = -1;
        for (int i = 0; i < c.Rig.HitCount; i++)
            if (best < 0 || c.Rig.Hits[i].W > c.Rig.Hits[best].W) best = i;
        if (best < 0) return c.Rig.BoundsCenter + origin;
        Vector4 s = c.Rig.Hits[best];
        return new Vector3(s.X, s.Y, s.Z) + origin;
    }

    void Gone(Creature c) => Wild.SimOf(c, out _, out _)?.Remove(c);

    Vessel Wall(Vector3 at, int width = 5, int height = 5)
    {
        var cat = Zone.Data.Catalogue;
        var s = new Structure(cat, cat.Grid(GridSize.Small)) { Name = $"TestWall{_walls++}" };
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                s.Set(new Int3(x, y, 0), Building.Make(s, cat.Type("steel")));
        s.DirtyChunks.Clear();
        Vector3 g = Zone.GroundAt(at.X, at.Z);
        return Vessel.SpawnBase(Zone, s, new N.Vector3(g.X, g.Y + 0.4f, g.Z), 0f);
    }

    int _walls;

    /// <summary>The middle of a structure's block nearest the eye: a point on its hull to shoot at.</summary>
    Vector3 HullPoint(Vessel v)
    {
        Vector3 best = v.Body.Position.ToGodot();
        float bestD = float.MaxValue;
        foreach (var (pos, _) in v.Structure.Blocks())
        {
            Vector3 p = v.ToWorld(StructureBridge.CellCenter(v.Structure, pos));
            float d = p.DistanceTo(Eye);
            if (d < bestD && p.Y > Zone.GroundAt(p.X, p.Z).Y + 0.4f) { bestD = d; best = p; }
        }
        return best;
    }

    /// <summary>A placed mine of a weapon held to a thing, or -1.</summary>
    int MineOn(WeaponDef d, object thing) => Array.FindIndex(Arms.Pool.Shots, s => s.Live && s.Def.Base.Id == d.Id && s.State == ShotState.Armed && s.Anchor.Thing == thing);

    /// <summary>Takes a test structure out of the world.</summary>
    void Remove(Vessel v)
    {
        if (!Zone.Vessels.Contains(v)) return;
        Zone.Physics.Unregister(v.Body);
        Zone.Physics.StuckFields.Remove(v.Field);
        Zone.Untrack(v);
        v.Node.QueueFree();
    }

    Vector3 WallMiddle(Vessel v) => v.ToWorld(StructureBridge.CellCenter(v.Structure, new Int3(2, 2, 0)));

    /// <summary>Puts a weapon in hand on slot 1 with plenty of its ammo, loaded.</summary>
    WeaponDef Arm(string id, int ammoItems = 20)
    {
        WeaponDef d = Arms.Table.Get(id);
        PlayerInventory pack = _game.Pack;
        int have = pack[d.Item];
        if (have < (d.Ammo == d.Item ? ammoItems : 1)) pack.Storage.Add(d.Item, (d.Ammo == d.Item ? ammoItems : 1) - have);
        // One stack of it, in slot 1 (any other stack of it folds back into the bag).
        for (int i = 1; i < pack.Grid.Count; i++)
            if (pack.Grid[i].Item == d.Item) { pack.Grid[i].Item = null; pack.Grid[i].Count = 0; }
        pack.Grid[0].Item = d.Item;
        pack.Grid[0].Count = Math.Min(pack[d.Item], Math.Max(1, Zone.Data.Items.Item(d.Item).StackSize));
        foreach (WeaponDef m in d.Modes)
            if (m.Ammo != d.Item && pack[m.Ammo] < ammoItems) pack.Storage.Add(m.Ammo, ammoItems - pack[m.Ammo]);
        pack.Grid.Reconcile();
        pack.Selected = 0;
        Gun gun = Arms.GunFor(d);
        if (gun.Loaded == 0) gun.LoadNow();
        return d;
    }

    Gun GunOf(WeaponDef d) => Arms.GunFor(d);

    /// <summary>Lead for a lobbed shot: how far above the target to aim so it falls onto it.</summary>
    Vector3 Lead(WeaponDef d, Vector3 target)
    {
        if (d.Gravity <= 0f) return target;
        float t = Eye.DistanceTo(target) / MathF.Max(1f, d.Speed);
        // A thrown thing also leaves the hand with a little lift (ArmsHost.Fire).
        float lift = d.Delivery == Delivery.Placed ? 2.5f * t : 0f;
        return target + Vector3.Up * MathF.Max(0f, 0.5f * Zone.Data.Rules.Planet.Gravity * d.Gravity * t * t - lift);
    }

    /// <summary>Aims at a point and pulls the trigger the way this weapon fires: a click, a short burst held, a full charge, a held cone.</summary>
    IEnumerable<int> Shoot(WeaponDef d, Vector3 at, int holdTicks = -1)
    {
        foreach (int x in _drive.LookAt(at, 8)) yield return x;
        int ticks = holdTicks >= 0 ? holdTicks : d.Trigger switch
        {
            Trigger.Auto => 8,
            Trigger.Charge => 100,
            Trigger.Hold => 30,
            _ => 1,
        };
        _game.Controls.PrimaryPressed = true;
        for (int i = 0; i < ticks; i++)
        {
            _drive.Face(at);
            _game.Controls.Primary = true;
            yield return 0;
        }
        _game.Controls.Primary = false;
        yield return 0;
    }

    string Tally() => $"fired {Arms.Fired}, hits {Arms.Hits}: ground {Arms.GroundHits}, blocks {Arms.BlockHits}, creatures {Arms.CreatureHits}";

    /// <summary>Where a creature is, for a failing check: its centre's bearing and distance from the eye along a heading, and its bounds.</summary>
    string Where(Creature c, Vector3 dir)
    {
        Vector3 to = Centre(c) - Eye;
        Wild.SimOf(c, out Vector3 o, out _);
        Vector3 rb = (c.Rig?.BoundsCenter ?? Vector3.Zero) + o - Eye;
        float deg(Vector3 v) => Mathf.RadToDeg(MathF.Acos(Math.Clamp(v.Normalized().Dot(dir.Normalized()), -1f, 1f)));
        return $"{to.Length():F1} m at {deg(to):F0} deg (rig bounds {rb.Length():F1} m at {deg(rb):F0} deg, r {c.Rig?.BoundsRadius:F2}, {c.Rig?.HitCount} spheres)";
    }

    float DealtTo(object thing) => _dealt.Where(h => h.Thing == thing).Sum(h => h.Amount);
    int HitsOn(object thing) => _dealt.Count(h => h.Thing == thing);

    /// <summary>Waits until the gun can fire again: not reloading, cooled down, pumped; loaded if it ran dry.</summary>
    IEnumerable<int> Ready(Gun gun)
    {
        for (int i = 0; i < 300 && (gun.Reloading || gun.Cooldown > 0f || gun.PumpLeft > 0f); i++) yield return 0;
        if (gun.Loaded == 0) gun.LoadNow();
        yield return 0;
    }

    IEnumerable<int> Until(Func<bool> done, int ticks)
    {
        for (int i = 0; i < ticks && !done(); i++) yield return 0;
    }

    // ---- The run ------------------------------------------------------------------------------

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        Zone.Weather.Bridge.Calm = true;
        Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        Zone.Sky.TimeOfDay = 0.4;
        Wild.Frozen = true;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        // Clear the near field of anything wild, so only the test's animals are hit or sought.
        foreach (Creature c in Wild.Sim.Creatures.ToList()) if (c.Position.DistanceTo(Wild.PatchLocal(Feet)) < 70f) Wild.Sim.Remove(c);
        Arms.Dealt += (k, t, a) => _dealt.Add((k, t, a));
        _game.Suit.Vitals.Health = _game.Suit.Sim.Data.Vitals.Health.Max;

        foreach (int x in Data()) yield return x;
        foreach (int x in Pistol()) yield return x;
        foreach (int x in Rifle()) yield return x;
        foreach (int x in Scattergun()) yield return x;
        foreach (int x in Launcher()) yield return x;
        foreach (int x in Rocket()) yield return x;
        foreach (int x in StageRifle()) yield return x;
        foreach (int x in ForkGun()) yield return x;
        foreach (int x in Hive()) yield return x;
        foreach (int x in Worms()) yield return x;
        foreach (int x in BendRounds()) yield return x;
        foreach (int x in Thrower()) yield return x;
        foreach (int x in Mine()) yield return x;
        foreach (int x in Targets()) yield return x;
        foreach (int x in FrameCost()) yield return x;
    }

    // ---- The data, the models and the sounds ---------------------------------------------------

    IEnumerable<int> Data()
    {
        Armoury table = Arms.Table;
        string[] twelve = { "SA2", "RF2", "SG2", "GL2", "RK3", "MN13", "AC3", "LT3", "LV2", "LV6", "SK1", "FL5" };
        Check(twelve.All(code => table.All.Any(w => w.Code == code)) && table.All.Count == 12, $"the twelve of section 8 are all rows: {string.Join(", ", table.All.Select(w => w.Code))}");
        var parts = table.All.SelectMany(w => w.Modes).SelectMany(w => w.Parts()).Distinct().OrderBy(p => p).ToList();
        string[] shared = { "arc", "bounce", "charge", "cone", "dot", "gravity", "homing", "pellets", "pierce", "placed", "stick", "swarm" };
        Check(shared.All(parts.Contains), $"the rows combine all twelve shared parts: {string.Join(" ", parts)}");
        var items = Zone.Data.Items;
        foreach (WeaponDef w in table.All)
        {
            var item = items.Item(w.Item);
            Check(item.Category == GalHack.Items.ItemCategory.Weapon && item.Recipe != null && item.Unlock?.Tab == "weapons" && w.Modes.All(m => items.TryGet(m.Ammo, out _)),
                $"{w.Code} {w.Name}: an item with a recipe ({item.Recipe?.Tier}), a weapons node at level {item.Unlock?.Level} for {item.Unlock?.Cost}, ammo {string.Join("/", w.Modes.Select(m => m.Ammo).Distinct())}");
        }
        var levels = new Dictionary<string, int>
        {
            ["tack-pistol"] = 2, ["pump-scattergun"] = 6, ["line-rifle"] = 7, ["pressure-thrower"] = 7, ["break-launcher"] = 8, ["shoulder-tube"] = 9,
            ["prox-mine"] = 10, ["hornet-hive"] = 11, ["stage-rifle"] = 14, ["wormspitter"] = 14, ["fork-gun"] = 16, ["bend-rounds"] = 16,
        };
        Check(levels.All(kv => items.Item(kv.Key).Unlock.Level == kv.Value && items.Item(kv.Key).Unlock.Cost == (kv.Value <= 4 ? 1 : kv.Value <= 10 ? 2 : 3)),
            "each sits at the pacing doc's level, at its level's cost (1 to level 4, 2 to 10, 3 to 16)");
        Check(items.Items.Where(i => i.IsAmmo && i.Unlock?.Parent != null && levels.ContainsKey(i.Unlock.Parent)).All(i => i.Unlock.Cost == 0),
            "a weapon's own new ammo costs nothing and opens with it");
        Check(table.All.Where(w => w.Look.Band == Band.Living).All(w => w.Ammo == "biomass"), "living weapons eat plain biomass");
        Check(table.Rules.SelfHarm && !table.Rules.FriendlyFire, "self-harm on, friendly fire off by default (a server setting)");

        // Models from parts: each band has its own look, and every gun has a grip and a muzzle.
        foreach (WeaponDef w in table.All)
        {
            GunModelSpec m = GunLook.Build(w);
            bool ok = m.Parts.Count >= 6 && m.Length > 0.05f && m.Parts.Any(p => p.Role == Role.Receiver || p.Role == Role.Sac) && (w.Delivery == Delivery.Placed || m.Parts.Any(p => p.Role == Role.Grip));
            ok &= w.Look.Band switch
            {
                Band.Low => m.Parts.Any(p => p.Role == Role.Weld) && m.Parts.Average(p => p.Wear) > 0.7f,
                Band.High => m.Parts.Any(p => p.Finish == Finish.Emitter) && m.Parts.Where(p => p.Finish == Finish.Worn).Average(p => p.Wear) < 0.2f,
                Band.Living => m.Parts.Any(p => p.Role == Role.Rib) && m.Parts.All(p => p.Metal < 0.1f),
                _ => m.Parts.Average(p => p.Wear) is > 0.3f and < 0.6f,
            };
            Check(ok, $"{w.Code}: a {w.Look.Band} model of {m.Parts.Count} parts, {m.Length:F2} m ({string.Join(" ", m.Parts.Select(p => p.Role).Distinct())})");
        }
        GunModel.Built built = GunModel.Build(table.Get("stage-rifle"));
        Check(built.Rings.Count == 4 && built.Muzzle != null, $"the stage rifle's model has a ring per charge stage ({built.Rings.Count}) and a muzzle");
        built.Root.Free();

        // Sounds from parts: every gun has its own, and they differ by calibre, energy and life.
        Check(Arms.Sounds.Clips >= 12 * 4 && table.All.All(w => Arms.Sounds.Has(w, VoiceKind.Report) && Arms.Sounds.Has(w, VoiceKind.Reload) && Arms.Sounds.Has(w, VoiceKind.Impact)),
            $"every weapon's report, action, reload and impact are synthesised at load ({Arms.Sounds.Clips} clips)");
        float[] pistol = GunVoice.Make(table.Get("tack-pistol"), VoiceKind.Report), tube = GunVoice.Make(table.Get("shoulder-tube"), VoiceKind.Report);
        float[] fork = GunVoice.Make(table.Get("fork-gun"), VoiceKind.Report), worm = GunVoice.Make(table.Get("wormspitter"), VoiceKind.Report);
        Check(tube.Length > pistol.Length * 1.5f, $"a big calibre booms longer: rocket {tube.Length / 22050f:F2} s, pistol {pistol.Length / 22050f:F2} s");
        Check(Brightness(fork) > Brightness(worm) * 1.3f && Brightness(pistol) > Brightness(worm), $"energy zaps brighter than a living gun's wet cough ({Brightness(fork):F2}, {Brightness(worm):F2})");
        yield return 0;
    }

    /// <summary>How bright a sound is: its mean sample-to-sample change against its level.</summary>
    static float Brightness(float[] s)
    {
        double diff = 0, level = 0;
        for (int i = 1; i < s.Length; i++) { diff += Math.Abs(s[i] - s[i - 1]); level += Math.Abs(s[i]); }
        return (float)(diff / Math.Max(1e-6, level));
    }

    // ---- SA2: magazine and reload ----------------------------------------------------------------

    IEnumerable<int> Pistol()
    {
        WeaponDef d = Arm("tack-pistol", 2);
        Gun gun = GunOf(d);
        int boxes = _game.Pack["pistol-rounds"];
        Check(gun.Loaded == d.Magazine && _game.Pack.Hand == HandKind.Weapon, $"the Tack Pistol is in hand and loaded ({gun.Loaded}/{d.Magazine}) from one box ({boxes} left)");
        Vector3 dir = Bearing(12f);
        Creature c = Animal(Feet + dir * 10f);
        foreach (int x in _drive.Wait(4)) yield return x;
        float before = c.Health;
        int hits = Arms.CreatureHits;
        for (int i = 0; i < d.Magazine; i++)
        {
            foreach (int x in Shoot(d, Centre(c))) yield return x;
            foreach (int x in _drive.Wait(12)) yield return x;
        }
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(Arms.CreatureHits - hits >= d.Magazine / 2 && c.Health < before, $"a magazine of bolts flies out at {d.Speed} m/s and lands: {Arms.CreatureHits - hits} hits, {before - c.Health:F0} damage");
        Check(gun.Loaded == 0 && gun.Reloading, "empty, it starts reloading by itself");
        foreach (int x in Until(() => !gun.Reloading, 200)) yield return x;
        Check(gun.Loaded == d.Magazine && _game.Pack["pistol-rounds"] == boxes && gun.Loose("pistol-rounds") == 30 - 2 * d.Magazine,
            $"after {d.Reload} s it is full again from the first box's loose rounds, no new box opened ({gun.Loose("pistol-rounds")} loose, {_game.Pack["pistol-rounds"]} boxes)");
        // Empty everything, then a pull clicks dry and nothing flies.
        _game.Pack.Remove("pistol-rounds", _game.Pack["pistol-rounds"]);
        while (gun.Loaded > 0 || gun.Loose("pistol-rounds") > 0)
        {
            foreach (int x in Shoot(d, Centre(c))) yield return x;
            foreach (int x in _drive.Wait(12)) yield return x;
            if (gun.Reloading) foreach (int x in Until(() => !gun.Reloading, 200)) yield return x;
        }
        int fired = Arms.Fired;
        int dry = Arms.Sounds.Played[(int)VoiceKind.Mechanism];
        foreach (int x in Shoot(d, Centre(c))) yield return x;
        Check(Arms.Fired == fired && !gun.Reloading && Arms.Sounds.Played[(int)VoiceKind.Mechanism] > dry, "with no ammo anywhere the trigger only clicks");
        Gone(c);
    }

    // ---- RF2: fire modes --------------------------------------------------------------------------

    IEnumerable<int> Rifle()
    {
        WeaponDef d = Arm("line-rifle");
        Gun gun = GunOf(d);
        Vector3 dir = Bearing(12f);
        Creature c = Animal(Feet + dir * 12f);
        foreach (int x in _drive.Wait(4)) yield return x;
        int fired = Arms.Fired;
        foreach (int x in Shoot(d, Centre(c), 30)) yield return x;
        int auto = Arms.Fired - fired;
        Check(gun.Def.Mode == "AUTO" && auto >= 4 && auto <= 6, $"AUTO: held half a second, {auto} rounds at {d.Rpm} rpm");
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        fired = Arms.Fired;
        foreach (int x in Shoot(d, Centre(c), 1)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(gun.Def.Mode == "BURST" && Arms.Fired - fired == 3, $"Q to BURST: one pull, {Arms.Fired - fired} rounds");
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        fired = Arms.Fired;
        foreach (int x in Shoot(d, Centre(c), 30)) yield return x;
        Check(gun.Def.Mode == "SINGLE" && Arms.Fired - fired == 1, $"Q to SINGLE: held, still one round ({Arms.Fired - fired})");
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(gun.Def.Mode == "AUTO" && DealtTo(c) > 0f, $"and back round to AUTO; the creature took {DealtTo(c):F0}");
        Gone(c);
    }

    // ---- SG2: pellets and shell types --------------------------------------------------------------

    IEnumerable<int> Scattergun()
    {
        WeaponDef d = Arm("pump-scattergun");
        Gun gun = GunOf(d);
        Vector3 dir = Bearing(10f);
        Creature c = Animal(Feet + dir * 7f);
        foreach (int x in _drive.Wait(4)) yield return x;
        foreach (int x in _drive.LookAt(Centre(c), 8)) yield return x;
        int live = Arms.Pool.Live;
        int hits = HitsOn(c), struck = Arms.Hits;
        _game.Controls.PrimaryPressed = true;
        _game.Controls.Primary = true;
        yield return 0;
        _game.Controls.Primary = false;
        yield return 0;
        // In flight, plus any that struck in their first tick (a close creature is reached at once).
        int pellets = Arms.Pool.Live - live + (Arms.Hits - struck);
        foreach (int x in _drive.Wait(20)) yield return x;
        Check(pellets == 8 && HitsOn(c) - hits >= 2, $"BUCKSHOT: one shell, {pellets} pellets in flight, {HitsOn(c) - hits} on the creature at 7 m");
        int fired = Arms.Fired;
        foreach (int x in Shoot(d, Centre(c))) yield return x;
        Check(Arms.Fired == fired && gun.PumpLeft > 0f || Arms.Fired == fired + 1, "a pump between shots: the trigger waits for the action");
        foreach (int x in _drive.Wait(60)) yield return x;
        int shells = _game.Pack["slug-shells"];
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
        Check(gun.Def.Mode == "SLUG" && gun.Reloading, "Q to SLUG: the buckshot goes back loose and slugs load");
        foreach (int x in Until(() => !gun.Reloading, 600)) yield return x;
        Check(gun.LoadedAmmo == "slug-shells" && gun.Loaded == d.Magazine && gun.Loose("buck-shells") > 0 && _game.Pack["slug-shells"] < shells,
            $"slugs loaded one at a time ({gun.Loaded}), the buckshot kept loose ({gun.Loose("buck-shells")})");
        live = Arms.Pool.Live;
        float before = DealtTo(c);
        foreach (int x in Shoot(d, Centre(c))) yield return x;
        int slugs = Arms.Pool.Live - live;
        foreach (int x in _drive.Wait(20)) yield return x;
        Check(slugs == 1 && DealtTo(c) - before >= 60f, $"a slug: one heavy round ({DealtTo(c) - before:F0} damage)");
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
        foreach (int x in Until(() => !gun.Reloading, 600)) yield return x;
        Gone(c);
    }

    // ---- GL2: gravity, bounce, blast; self-harm -----------------------------------------------------

    IEnumerable<int> Launcher()
    {
        WeaponDef d = Arm("break-launcher");
        Gun gun = GunOf(d);
        Vector3 dir = Bearing(24f);
        // A lob up at 70 degrees (above the trees): it climbs, tops out and falls, as gravity says.
        Vector3 up = (dir * MathF.Cos(1.22f) + Vector3.Up * MathF.Sin(1.22f)).Normalized();
        foreach (int x in Shoot(d, Eye + up * 30f)) yield return x;
        int slot = Array.FindIndex(Arms.Pool.Shots, s => s.Live && s.Def.Base.Id == d.Id);
        var heights = new List<float>();
        var vy = new List<float>();
        int id = slot >= 0 ? Arms.Pool.Shots[slot].Id : -1;
        float startY = slot >= 0 ? Arms.Pool.Shots[slot].Pos.Y : 0f;
        int blasts0 = Arms.Blasts;
        for (int i = 0; i < 480 && slot >= 0 && Arms.Pool.Shots[slot].Live && Arms.Pool.Shots[slot].Id == id; i++)
        {
            heights.Add(Arms.Pool.Shots[slot].Pos.Y);
            vy.Add(Arms.Pool.Shots[slot].Vel.Y);
            yield return 0;
        }
        float g = Zone.Data.Rules.Planet.Gravity;
        float accel = vy.Count > 20 ? (vy[^5] - vy[4]) / ((vy.Count - 9) / 60f) : 0f;
        Check(heights.Count > 30 && heights.Max() > startY + 2f && heights[^1] < heights.Max() - 1f && MathF.Abs(accel + g) < 0.4f,
            $"IMPACT grenade arcs: up {heights.DefaultIfEmpty(0f).Max() - startY:F1} m over {heights.Count / 60f:F1} s, falling at {-accel:F1} m/s² (gravity {g:F1}; {Arms.Recent()})");
        foreach (int x in Until(() => Arms.Blasts > blasts0, 120)) yield return x;
        Check(Arms.Blasts > blasts0, $"and bursts where it lands ({Arms.Blasts - blasts0} blasts)");
        foreach (int x in Until(() => !gun.Reloading, 200)) yield return x;

        // BOUNCE: skips off the ground and goes off on its fuse.
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
        foreach (int x in Until(() => !gun.Reloading, 200)) yield return x;
        int bounces = Arms.Bounces, blasts = Arms.Blasts;
        Vector3 down = (dir + Vector3.Down * 0.25f).Normalized();
        foreach (int x in Shoot(d, Eye + down * 12f)) yield return x;
        var sw = Stopwatch.StartNew();
        double fused = 0;
        for (int i = 0; i < 300 && Arms.Blasts == blasts; i++) { yield return 0; fused = i / 60.0; }
        Check(gun.Def.Mode == "BOUNCE" && Arms.Bounces > bounces && Arms.Blasts == blasts + 1 && fused > 2.0 && fused < 3.2,
            $"BOUNCE: it skips {Arms.Bounces - bounces} times and goes off on its fuse at {fused:F1} s");

        // Self-harm: a grenade at your own feet hurts you.
        foreach (int x in Until(() => !gun.Reloading, 200)) yield return x;
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
        foreach (int x in Until(() => !gun.Reloading, 200)) yield return x;
        double health = _game.Suit.Vitals.Health;
        int playerHits = Arms.PlayerHits;
        foreach (int x in Shoot(d, Feet + dir * 2.4f)) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;
        Check(Arms.PlayerHits > playerHits && _game.Suit.Vitals.Health < health, $"self-harm is on: your own grenade at 2.4 m hurts ({health - _game.Suit.Vitals.Health:F0} health)");
        _game.Suit.Vitals.Health = _game.Suit.Sim.Data.Vitals.Health.Max;
    }

    // ---- RK3: the rocket and its backblast ---------------------------------------------------------

    IEnumerable<int> Rocket()
    {
        WeaponDef d = Arm("shoulder-tube");
        Vector3 dir = Bearing(16f, true, 0f);
        Creature front = Animal(Feet + dir * 14f), behind = Animal(Feet - dir * 2.2f);
        foreach (int x in _drive.Wait(4)) yield return x;
        int blasts = Arms.Blasts;
        foreach (int x in Shoot(d, Centre(front))) yield return x;
        int slot = Array.FindIndex(Arms.Pool.Shots, s => s.Live && s.Def.Base.Id == d.Id);
        float speed = slot >= 0 ? Arms.Pool.Shots[slot].Vel.Length() : 0f;
        foreach (int x in Until(() => Arms.Blasts > blasts, 120)) yield return x;
        foreach (int x in _drive.Wait(4)) yield return x;
        Check(speed is > 40f and < 90f && Arms.Blasts > blasts && DealtTo(front) >= 100f, $"a slow rocket ({speed:F0} m/s) with a smoke trail bursts on the creature ({DealtTo(front):F0} damage)");
        Check(DealtTo(behind) > 0f, $"the backblast hurts what stands behind the tube ({DealtTo(behind):F0}; {Arms.Probe(_game.Arms.Muzzle(), Centre(behind))})");
        Gone(front);
        Gone(behind);
    }

    // ---- AC3: charge stages and pierce --------------------------------------------------------------

    IEnumerable<int> StageRifle()
    {
        WeaponDef d = Arm("stage-rifle");
        Gun gun = GunOf(d);
        Vector3 dir = Bearing(18f);
        Creature first = Animal(Feet + dir * 9f);
        foreach (int x in _drive.Wait(6)) yield return x;
        // The second stands on the line from the eye through the first, where that line meets its body's height.
        Vector3 c1 = Centre(first), line = (c1 - Eye).Normalized();
        float lift = c1.Y - Zone.GroundAt(c1.X, c1.Z).Y, bestOff = float.MaxValue, bestAt = 14f;
        for (float r = 12f; r <= 20f; r += 0.5f)
        {
            Vector3 p = Eye + line * (r / MathF.Max(0.2f, new Vector2(line.X, line.Z).Length()));
            float off = MathF.Abs(p.Y - (Zone.GroundAt(p.X, p.Z).Y + lift));
            if (off < bestOff) { bestOff = off; bestAt = r; }
        }
        Vector3 flat = new Vector3(line.X, 0f, line.Z).Normalized();
        Creature second = Animal(new Vector3(Eye.X, Feet.Y, Eye.Z) + flat * bestAt);
        foreach (int x in _drive.Wait(6)) yield return x;
        Vector3 aim = c1; // through the first, into the second behind it
        // A tap: the light shot stops in the first.
        string t0 = Tally();
        foreach (int x in Shoot(d, aim, 1)) yield return x;
        foreach (int x in _drive.Wait(40)) yield return x;
        Check(DealtTo(first) > 0f && DealtTo(second) == 0f, $"an uncharged slug stops in the first creature ({DealtTo(first):F0}) and doesn't reach the second ({t0} to {Tally()}; first {Where(first, aim - Eye)}, second {Where(second, aim - Eye)}; {Arms.Recent()})");
        // A full charge: four stages, each faster and through one more.
        foreach (int x in _drive.LookAt(aim, 6)) yield return x;
        _game.Controls.PrimaryPressed = true;
        int stages = Arms.Sounds.Played[(int)VoiceKind.Stage], peak = 0;
        for (int i = 0; i < 100; i++)
        {
            _drive.Face(aim);
            _game.Controls.Primary = true;
            yield return 0;
            peak = Math.Max(peak, gun.Stage);
        }
        bool glowing = _game.Hands.Gun != null && _game.Hands.Gun.Rings.Count == 4;
        _game.Controls.Primary = false;
        yield return 0;
        int slot = Array.FindIndex(Arms.Pool.Shots, s => s.Live && s.Def.Base.Id == d.Id);
        float speed = slot >= 0 ? Arms.Pool.Shots[slot].Vel.Length() : 0f;
        int pierce = slot >= 0 ? Arms.Pool.Shots[slot].PierceLeft : 0;
        Check(peak == 4 && Arms.Sounds.Played[(int)VoiceKind.Stage] - stages == 4 && glowing, $"held, it climbs four stages, a click each ({Arms.Sounds.Played[(int)VoiceKind.Stage] - stages}), the rings lighting up the barrel");
        Check(MathF.Abs(speed - (d.Speed + 4 * d.Charge.Speed)) < 1f && pierce == 4, $"a full charge flies at {speed:F0} m/s and pierces {pierce}");
        float a = DealtTo(first), b = DealtTo(second);
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(DealtTo(first) > a && DealtTo(second) > b, $"and goes through the first creature into the second ({DealtTo(first) - a:F0} and {DealtTo(second) - b:F0}; first {Where(first, aim - Eye)}, second {Where(second, aim - Eye)}; {Arms.Recent()})");
        Gone(first);
        Gone(second);
    }

    // ---- LT3: the arc --------------------------------------------------------------------------------

    IEnumerable<int> ForkGun()
    {
        WeaponDef d = Arm("fork-gun");
        Vector3 dir = Bearing(16f);
        Vector3 side = dir.Cross(Vector3.Up).Normalized();
        Creature a = Animal(Feet + dir * 12f), b = Animal(Feet + dir * 14f + side * 3f), c = Animal(Feet + dir * 15f - side * 3f), far = Animal(Feet + dir * 12f + side * 16f);
        foreach (int x in _drive.Wait(6)) yield return x;
        int jumps = Arms.ArcJumps;
        foreach (int x in Shoot(d, Centre(a))) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(DealtTo(a) > 0f && Arms.ArcJumps - jumps >= 2 && DealtTo(b) > 0f && DealtTo(c) > 0f, $"the bolt hits one ({DealtTo(a):F0}) and forks on: {Arms.ArcJumps - jumps} jumps, {DealtTo(b):F0} and {DealtTo(c):F0} to the two near it ({Tally()}; a to b: {Arms.Probe(Centre(a), Centre(b))})");
        Check(DealtTo(far) == 0f && Arms.Fx.Arcs > 0, $"the one 16 m off is out of reach; the lightning shows ({Arms.Fx.Arcs} arcs drawn)");
        foreach (Creature x in new[] { a, b, c, far }) Gone(x);
    }

    // ---- LV2: the swarm -------------------------------------------------------------------------------

    IEnumerable<int> Hive()
    {
        WeaponDef d = Arm("hornet-hive", 10);
        Gun gun = GunOf(d);
        Vector3 dir = Bearing(14f);
        int meat = _game.Pack["biomass"];
        // A pouch empties as the trigger is held.
        Vector3 up = (dir + Vector3.Up * 0.5f).Normalized();
        int fired = Arms.Fired;
        foreach (int x in Shoot(d, Eye + up * 20f, 75)) yield return x;
        Check(Arms.Fired - fired == d.Magazine, $"{Arms.Fired - fired} hornets out of a full pouch");
        // A volley let go at once into open air, nothing alive in reach: they spread out as they wander.
        foreach (Creature w in Wild.Sim.Creatures.ToList()) if (w.Position.DistanceTo(Wild.PatchLocal(Feet)) < 50f) Wild.Sim.Remove(w);
        // The people of a place or a hulk near by live in its own sim: they are alive in reach too.
        foreach (var site in Wild.Sites)
            if (site.Sim != null)
                foreach (Creature w in site.Sim.Creatures.ToList()) if ((w.Position + site.GlobalPosition).DistanceTo(Feet) < 50f) site.Sim.Remove(w);
        foreach (int x in _drive.Wait(30)) yield return x;
        var before = Arms.Pool.Shots.Where(s => s.Live && s.Def.Base.Id == d.Id).Select(s => s.Id).ToHashSet();
        Arms.Fire(d, 8);
        var hornets = Arms.Pool.Shots.Where(s => s.Live && s.Def.Base.Id == d.Id && !before.Contains(s.Id)).Select(s => s.Id).ToList();
        float Spread()
        {
            var at = Arms.Pool.Shots.Where(s => s.Live && hornets.Contains(s.Id)).Select(s => s.Pos.ToGodot()).ToList();
            if (at.Count < 2) return 0f;
            Vector3 mid = at.Aggregate(Vector3.Zero, (m, p) => m + p) / at.Count;
            return at.Average(p => p.DistanceTo(mid));
        }
        foreach (int x in _drive.Wait(6)) yield return x;
        float early = Spread();
        foreach (int x in _drive.Wait(90)) yield return x;
        float later = Spread();
        Check(hornets.Count == 8 && later > early + 1f, $"a swarm of {hornets.Count} let go together spreads as it wanders: {early:F1} m to {later:F1} m from its middle in 1.5 s");
        foreach (int x in _drive.Wait(60)) yield return x;
        // A living thing in reach: they find it and sting.
        Creature c = Animal(Feet + dir * 8f);
        foreach (int x in _drive.Wait(4)) yield return x;
        foreach (int x in Until(() => !gun.Reloading, 400)) yield return x;
        Check(_game.Pack["biomass"] < meat && gun.Loaded == d.Magazine, $"the pouch refills from biomass ({meat - _game.Pack["biomass"]} eaten)");
        int stings = HitsOn(c);
        foreach (int x in Shoot(d, Centre(c) + dir.Cross(Vector3.Up) * 2f, 75)) yield return x;
        foreach (int x in Until(() => HitsOn(c) - stings >= 5, 600)) yield return x;
        Check(HitsOn(c) - stings >= 5, $"aimed off to one side, they turn and sting it: {HitsOn(c) - stings} stings");
        foreach (int x in _drive.Wait(30)) yield return x;
        Gone(c);
    }

    // ---- LV6: stick and tick -------------------------------------------------------------------------

    IEnumerable<int> Worms()
    {
        WeaponDef d = Arm("wormspitter");
        Vector3 dir = Bearing(12f);
        Creature c = Animal(Feet + dir * 8f);
        foreach (int x in _drive.Wait(4)) yield return x;
        int sticks = Arms.Sticks;
        foreach (int x in Shoot(d, Lead(d, Centre(c)))) yield return x;
        foreach (int x in Until(() => Arms.Sticks > sticks, 120)) yield return x;
        int slot = Array.FindIndex(Arms.Pool.Shots, s => s.Live && s.Def.Base.Id == d.Id && s.State == ShotState.Stuck);
        Check(slot >= 0 && Arms.Pool.Shots[slot].Anchor.Thing == c, $"the worm sticks into the creature ({(slot >= 0 ? Arms.Pool.Shots[slot].Anchor.Kind : HitKind.Nothing)})");
        int hits = HitsOn(c);
        float hp = c.Health;
        for (int i = 0; i < 60 * 3; i++)
        {
            yield return 0;
            if (slot >= 0 && Arms.Pool.Shots[slot].Live && i % 60 == 30) Check(Arms.Pool.Shots[slot].Pos.ToGodot().DistanceTo(c.Rig.BoundsCenter + Wild.Origin) < c.Rig.BoundsRadius + 0.3f, "and rides it");
        }
        Check(HitsOn(c) - hits >= 2 && c.Health < hp, $"and keeps eating: {HitsOn(c) - hits} bites in 3 s, {hp - c.Health:F0} damage");
        foreach (int x in _drive.Wait(60 * 4)) yield return x;
        Check(slot < 0 || !Arms.Pool.Shots[slot].Live, $"after {d.Stick.Seconds} s it's done and gone");
        Gone(c);
    }

    // ---- SK1: homing --------------------------------------------------------------------------------

    IEnumerable<int> BendRounds()
    {
        Vector3 dir = Bearing(18f);
        Vector3 side = dir.Cross(Vector3.Up).Normalized();
        Creature c = Animal(Feet + dir * 16f);
        foreach (int x in _drive.Wait(4)) yield return x;
        Vector3 off = Centre(c) + side * 2.2f;
        // A plain round aimed off misses...
        WeaponDef plain = Arm("tack-pistol", 4);
        GunOf(plain).LoadNow();
        foreach (int x in Shoot(plain, off)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        float missed = DealtTo(c);
        // ...a bend round aimed off curves in.
        WeaponDef d = Arm("bend-rounds");
        foreach (int x in _drive.LookAt(off, 8)) yield return x;
        N.Vector3 v0 = (off - Arms.Muzzle()).ToNumerics(); // the way it leaves the muzzle: at the crosshair
        foreach (int x in Shoot(d, off, 1)) yield return x;
        int slot = Array.FindIndex(Arms.Pool.Shots, s => s.Live && s.Def.Base.Id == d.Id);
        float turned = 0f;
        for (int i = 0; i < 6; i++)
        {
            yield return 0;
            if (slot >= 0 && Arms.Pool.Shots[slot].Live) turned = MathF.Acos(Math.Clamp(N.Vector3.Dot(N.Vector3.Normalize(v0), N.Vector3.Normalize(Arms.Pool.Shots[slot].Vel)), -1f, 1f));
        }
        foreach (int x in _drive.Wait(40)) yield return x;
        Check(missed == 0f && turned > 0.03f && DealtTo(c) > 0f, $"[slot {slot}, {Tally()}] " + $"a plain round aimed 2.2 m off misses; a bend round turns {Mathf.RadToDeg(turned):F1} degrees in flight and hits ({DealtTo(c):F0})");
        Gone(c);
    }

    // ---- FL5: the cone ---------------------------------------------------------------------------------

    IEnumerable<int> Thrower()
    {
        WeaponDef d = Arm("pressure-thrower");
        Vector3 dir = Bearing(8f);
        Vector3 side = dir.Cross(Vector3.Up).Normalized();
        Creature inside = Animal(Feet + dir * 4f), outside = Animal(Feet + (dir * 0.5f + side * 0.87f) * 4f);
        foreach (int x in _drive.Wait(6)) yield return x;
        int cone = Arms.ConeHits;
        foreach (int x in Shoot(d, Centre(inside), 45)) yield return x;
        Check(DealtTo(inside) > 0f && DealtTo(outside) == 0f && Arms.ConeHits > cone, $"the flame hits only inside its arc: {DealtTo(inside):F0} to the one ahead, {DealtTo(outside):F0} to the one 60 degrees off (muzzle to it: {Arms.Probe(Arms.Muzzle(), Centre(inside))}, {Arms.Muzzle().DistanceTo(Centre(inside)):F1} m; look {_game.Survivor.WorldLook().Dot(dir):F2} along the heading; inside {Where(inside, _game.Survivor.WorldLook())}, outside {Where(outside, _game.Survivor.WorldLook())})");
        Check(Arms.Sounds.Played[(int)VoiceKind.Hold] > 0, "with its roar held while it burns");
        float burnt = DealtTo(inside);
        foreach (int x in _drive.Wait(60)) yield return x;
        Check(DealtTo(inside) > burnt && Arms.Effects.Left(inside, HarmTags.Fire) > 0f, $"the burn keeps ticking after the jet stops ({DealtTo(inside) - burnt:F0} more, {Arms.Effects.Left(inside, HarmTags.Fire):F1} s left)");
        Check(inside.ScaredUntil > Wild.Sim.Time && outside.ScaredUntil < Wild.Sim.Time, "and the burnt one is frightened; the other isn't");
        // Let it run.
        Wild.Frozen = false;
        Vector3 start = Centre(inside);
        foreach (int x in _drive.Wait(150)) yield return x;
        float fled = Centre(inside).DistanceTo(Feet) - start.DistanceTo(Feet);
        Check(inside.State == CState.Flee && fled > 1.5f, $"it runs from the flame: {fled:F1} m further off");
        Wild.Frozen = true;
        foreach (int x in _drive.Wait(60 * 3)) yield return x;
        Check(Arms.Effects.Left(inside, HarmTags.Fire) <= 0f, "the burn ends");
        Gone(inside);
        Gone(outside);
    }

    // ---- MN13: placed, stuck, tripped -------------------------------------------------------------------

    IEnumerable<int> Mine()
    {
        WeaponDef d = Arm("prox-mine", 6);
        int mines = _game.Pack[d.Item];
        Vector3 dir = Bearing(10f);
        Vector3 spot = Zone.GroundAt(Feet.X + dir.X * 5f, Feet.Z + dir.Z * 5f);
        int placed = Arms.Placements;
        foreach (int x in Shoot(d, spot + Vector3.Up * 0.8f)) yield return x;
        foreach (int x in Until(() => Arms.Placements > placed, 180)) yield return x;
        int slot = Array.FindIndex(Arms.Pool.Shots, s => s.Live && s.Def.Base.Id == d.Id && s.State == ShotState.Armed);
        Check(slot >= 0 && _game.Pack[d.Item] == mines - 1, $"thrown, it lands and holds to the ground ({_game.Pack[d.Item]} left)");
        if (slot < 0) yield break;
        Vector3 at = Arms.Pool.Shots[slot].Pos.ToGodot();
        Vector3 n = Arms.Pool.Shots[slot].Normal.ToGodot();
        bool under = Arms.Sweep((at + n * 0.5f).ToNumerics(), (at - n * 0.5f).ToNumerics(), null, 1, 1f, out ShotHit below);
        float gap = under ? (at + n * 0.5f).DistanceTo(below.Point.ToGodot()) - 0.5f : 9f;
        Check(under && MathF.Abs(gap) < 0.1f, $"it sits on the surface it landed on ({gap:F3} m off it): nothing floats");
        // Something walks up before it arms: nothing. After: it goes off.
        Creature c = Animal(at + dir * 1.2f);
        foreach (int x in _drive.Wait(60)) yield return x;
        Check(Arms.Pool.Shots[slot].Live && Arms.Trips == 0, "not yet armed: it waits");
        int trips = Arms.Trips;
        foreach (int x in Until(() => Arms.Trips > trips, 240)) yield return x;
        Check(Arms.Trips > trips && DealtTo(c) > 50f, $"armed after {d.Placed.Arm} s, it goes off with something {d.Placed.Radius} m away ({DealtTo(c):F0} damage)");
        Gone(c);

        // On a structure that moves, it moves with it.
        Structure s = Zone.Data.Blueprint("starter.gbp");
        s.Name = "MineCarrier";
        Vector3 side = Bearing(12f, 0f, dir);
        Vector3 g = Zone.GroundAt(Feet.X + side.X * 9f, Feet.Z + side.Z * 9f);
        Vessel v = Vessel.Spawn(Zone, s, new N.Vector3(g.X, g.Y + 3f, g.Z), 0f, landed: true);
        foreach (int x in _drive.Wait(90)) yield return x;
        placed = Arms.Placements;
        Vector3 aim = v.Body.Position.ToGodot();
        foreach (int x in Shoot(d, Lead(d, aim))) yield return x;
        foreach (int x in Until(() => Arms.Placements > placed, 180)) yield return x;
        slot = Array.FindIndex(Arms.Pool.Shots, sh => sh.Live && sh.Def.Base.Id == d.Id && sh.State == ShotState.Armed && sh.Anchor.Thing == v);
        Check(slot >= 0, "a mine thrown at a vessel holds to its hull");
        if (slot >= 0)
        {
            Vector3 before = Arms.Pool.Shots[slot].Pos.ToGodot();
            N.Vector3 lift = new(0f, 2.5f, 0f);
            v.Body.Position += lift;
            Zone.Host.BindingOf(v.Body)?.Teleport();
            foreach (int x in _drive.Wait(2)) yield return x;
            Vector3 after = Arms.Pool.Shots[slot].Pos.ToGodot();
            float drift = 0f;
            for (int i = 0; i < 90; i++)
            {
                yield return 0;
                if (!Arms.Pool.Shots[slot].Live) break;
                drift = MathF.Max(drift, (v.Body.ToParent(Arms.Pool.Shots[slot].Anchor.Local) - Arms.Pool.Shots[slot].Pos).Length());
            }
            Check(MathF.Abs(after.Y - before.Y - 2.5f) < 0.3f && drift < 0.02f && Arms.Pool.Shots[slot].Live,
                $"the vessel moves and the mine goes with it: up {after.Y - before.Y:F2} m, and as it settles it stays on its spot (off by {drift:F3} m at most), its own vessel never tripping it");
            Arms.Pool.Shots[slot].Live = false;
        }

        // Its block shot away: it drops to what is under it and holds there.
        Vector3 wd = Bearing(10f, 0f, dir, side);
        Vessel wall = Wall(Feet + wd * 6f);
        foreach (int x in _drive.Wait(10)) yield return x;
        placed = Arms.Placements;
        foreach (int x in Shoot(d, Lead(d, WallMiddle(wall)))) yield return x;
        foreach (int x in Until(() => Arms.Placements > placed, 180)) yield return x;
        slot = Array.FindIndex(Arms.Pool.Shots, sh => sh.Live && sh.Def.Base.Id == d.Id && sh.Anchor.Thing == wall);
        Check(slot >= 0, "a mine thrown at a wall holds to its block");
        if (slot >= 0)
        {
            Anchor an = Arms.Pool.Shots[slot].Anchor;
            var cell = new Int3(an.CellX, an.CellY, an.CellZ);
            N.Vector3 p = Arms.Pool.Shots[slot].Pos;
            wall.Hits.Shot(cell, p, Arms.Pool.Shots[slot].Normal, 1e6f, DamageType.Kinetic);
            int releases = Arms.Releases;
            placed = Arms.Placements;
            foreach (int x in Until(() => Arms.Releases > releases && Arms.Placements > placed, 300)) yield return x;
            Vector3 rest = Arms.Pool.Shots[slot].Pos.ToGodot();
            Anchor now = Arms.Pool.Shots[slot].Anchor;
            bool elsewhere = now.Kind != HitKind.Block || now.CellX != cell.X || now.CellY != cell.Y || now.CellZ != cell.Z;
            Check(!wall.Structure.Has(cell) && Arms.Releases > releases && Arms.Placements > placed && Arms.Pool.Shots[slot].Live && elsewhere && rest.Y <= p.Y + 0.01f,
                $"its block is shot away: it drops {p.Y - rest.Y:F2} m and holds to what it lands on ({now.Kind} {now.CellX},{now.CellY},{now.CellZ}, was {cell}; gone {!wall.Structure.Has(cell)}, released {Arms.Releases - releases}, placed again {Arms.Placements - placed}, live {Arms.Pool.Shots[slot].Live})");
            Arms.Pool.Shots[slot].Live = false;
        }
        Remove(v);
        Remove(wall);
    }


    // ---- Every weapon at a creature, a block and a vessel ---------------------------------------------

    IEnumerable<int> Targets()
    {
        Vector3 cd = Bearing(12f);
        Vector3 wd = Bearing(12f, 0f, cd);
        Vector3 vd = Bearing(18f, 0f, cd, wd);
        foreach (int x in _drive.Wait(10)) yield return x;
        GD.Print($"      targets: player at {Feet} aboard {_game.Survivor.Aboard?.Name ?? "nothing"}, hand {_game.Pack.Hand}, dead {_game.Suit.Dead}, menu {_game.Menu.IsOpen}");
        foreach (WeaponDef d in Arms.Table.All)
        {
            int fired0 = Arms.Fired;
            Arm(d.Id, 30);
            Gun gun = GunOf(d);
            // A fresh wall and vessel for each (a blast takes a small wall apart).
            Vessel wall = Wall(Feet + wd * 9f);
            Structure s = Zone.Data.Blueprint("starter.gbp");
            s.Name = $"Target {d.Code}";
            Vector3 g = Zone.GroundAt(Feet.X + vd.X * 16f, Feet.Z + vd.Z * 16f);
            Vessel ship = Vessel.Spawn(Zone, s, new N.Vector3(g.X, g.Y + 3f, g.Z), 0f, landed: true);
            foreach (int x in _drive.Wait(60)) yield return x;
            foreach (int x in Ready(gun)) yield return x;
            float near = d.Cone != null ? 4f : d.Swarm != null ? 7f : 9f;
            Creature c = Animal(Feet + cd * near);
            foreach (int x in _drive.Wait(4)) yield return x;

            // A creature.
            if (d.Placed != null)
            {
                // Thrown in front of it; once it is down, the animal steps up beside it.
                Vector3 ground = Zone.GroundAt(Centre(c).X - cd.X * 1.4f, Centre(c).Z - cd.Z * 1.4f);
                int placed0 = Arms.Placements;
                foreach (int x in Shoot(d, Lead(d, ground + Vector3.Up * 0.3f))) yield return x;
                foreach (int x in Until(() => Arms.Placements > placed0, 180)) yield return x;
                int m = Array.FindIndex(Arms.Pool.Shots, sh => sh.Live && sh.Def.Base.Id == d.Id && sh.State == ShotState.Armed);
                if (m >= 0)
                {
                    foreach (int x in _drive.Wait(60 * 3)) yield return x;
                    Vector3 mp = Arms.Pool.Shots[m].Pos.ToGodot() + cd * 1.2f;
                    c.Position = Wild.PatchLocal(Zone.GroundAt(mp.X, mp.Z));
                }
            }
            else foreach (int x in Shoot(d, Lead(d, Centre(c)))) yield return x;
            foreach (int x in Until(() => DealtTo(c) > 0f, 60 * 6)) yield return x;
            bool hitCreature = DealtTo(c) > 0f;
            foreach (int x in Ready(gun)) yield return x;

            // A block.
            int ground0 = Arms.GroundHits, block0 = Arms.BlockHits, creature0 = Arms.CreatureHits;
            float onWall = DealtTo(wall);
            float wallHp = wall.Structure.Blocks().Sum(b => b.block.Hp);
            Vector3 wm = WallMiddle(wall);
            Creature tripper = null;
            if (d.Placed != null)
            {
                foreach (int x in Shoot(d, Lead(d, wm))) yield return x;
                foreach (int x in _drive.Wait(60 * 4)) yield return x;
                int m = MineOn(d, wall);
                Vector3 mp = m >= 0 ? Arms.Pool.Shots[m].Pos.ToGodot() : wm;
                Vector3 toward = Feet - mp;
                toward.Y = 0f;
                tripper = Animal(mp + toward.Normalized() * 1.2f);
            }
            else foreach (int x in Shoot(d, Lead(d, wm))) yield return x;
            foreach (int x in Until(() => DealtTo(wall) > onWall, 60 * 5)) yield return x;
            foreach (int x in _drive.Wait(d.Stick != null ? 120 : 4)) yield return x;
            bool hitWall = DealtTo(wall) > onWall;
            float wallLoss = wallHp - wall.Structure.Blocks().Sum(b => b.block.Hp);
            if (tripper != null) Gone(tripper);
            foreach (int x in Ready(gun)) yield return x;

            // A vessel.
            float onShip = DealtTo(ship), shipDamage = ship.Body.Damage;
            Vector3 sm = HullPoint(ship);
            if (d.Placed != null)
            {
                foreach (int x in Shoot(d, Lead(d, sm))) yield return x;
                foreach (int x in _drive.Wait(60 * 4)) yield return x;
                // Something comes up to the parked vessel: the mine on it goes off.
                int m = MineOn(d, ship);
                Vector3 mp = m >= 0 ? Arms.Pool.Shots[m].Pos.ToGodot() : sm;
                Vector3 toward = Feet - mp;
                toward.Y = 0f;
                tripper = Animal(mp + toward.Normalized() * 1.2f);
            }
            else foreach (int x in Shoot(d, Lead(d, sm))) yield return x;
            foreach (int x in Until(() => DealtTo(ship) > onShip, 60 * 5)) yield return x;
            foreach (int x in _drive.Wait(6)) yield return x;
            bool hitShip = DealtTo(ship) > onShip;
            if (tripper != null) Gone(tripper);

            string where = $"ground {Arms.GroundHits - ground0}, blocks {Arms.BlockHits - block0}, creatures {Arms.CreatureHits - creature0}; {Arms.Fired - fired0} fired, hand {_game.Pack.Hand}";
            bool blocks = d.Blocks > 0f && (d.Swarm == null);
            string expect = blocks ? "harms" : "leaves alone";
            Check(hitCreature && hitWall == blocks && hitShip == blocks,
                $"{d.Code} {d.Name}: the creature takes {DealtTo(c):F0}; it {expect} blocks: the wall takes {DealtTo(wall) - onWall:F0} ({wallLoss:F0} after armour), the vessel {DealtTo(ship) - onShip:F0} (hull {ship.Body.Damage - shipDamage:F2}; hits on {where}){(hitCreature && hitWall == blocks && hitShip == blocks ? "" : $"; creature {Where(c, cd)}, wall {Where(c, WallMiddle(wall) - Eye)} off the wall's line, wall {Eye.DistanceTo(WallMiddle(wall)):F1} m; last struck: " + Arms.Recent())}");
            Gone(c);
            foreach (int x in Until(() => !gun.Reloading, 400)) yield return x;
            foreach (int x in _drive.Wait(20)) yield return x;
            Remove(wall);
            Remove(ship);
        }
        Check(Arms.Sounds.Played[(int)VoiceKind.Report] > 40 && Arms.Sounds.Played[(int)VoiceKind.Reload] > 5 && Arms.Sounds.Played[(int)VoiceKind.Impact] > 10,
            $"every shot was heard: {Arms.Sounds.Played[(int)VoiceKind.Report]} reports, {Arms.Sounds.Played[(int)VoiceKind.Reload]} reloads, {Arms.Sounds.Played[(int)VoiceKind.Impact]} impacts");
    }

    // ---- The frame budget ----------------------------------------------------------------------------

    IEnumerable<int> FrameCost()
    {
        Arms.Pool.Clear();
        foreach (int x in _drive.Wait(30)) yield return x;
        Vector3 dir = Bearing(10f);
        foreach (int x in _drive.LookAt(Eye + (dir + Vector3.Up * 1.2f).Normalized() * 40f, 8)) yield return x;
        WeaponDef rifle = Arm("line-rifle");
        WeaponDef hive = Arm("hornet-hive");
        // A first volley warms every path up (first calls set up statics); the second is timed.
        Arms.Fire(rifle, 20);
        Arms.Fire(hive, 8);
        foreach (int x in _drive.Wait(90)) yield return x;
        Arms.Pool.Clear();
        Arms.Fire(rifle, 20);
        int rounds = Arms.Pool.Live;
        Arms.Fire(hive, 8);
        int fired = Arms.Pool.Live;
        foreach (int x in _drive.Wait(4)) yield return x;
        int live = Arms.Pool.Live;
        double total = 0, worst = 0, eventWorst = 0;
        long bytes = 0;
        int ticks = 0, eventful = 0;
        var where = new List<string>();
        for (int i = 0; i < 40; i++)
        {
            int events = Arms.Hits + Arms.Bounces + Arms.Blasts + Arms.Sticks;
            yield return 0;
            if (Arms.Pool.Live < 25) break;
            total += Arms.LastStepMs;
            ticks++;
            // Hits and bounces make sparks and sounds (and may allocate); steady flight must not.
            if (Arms.Hits + Arms.Bounces + Arms.Blasts + Arms.Sticks != events) { eventful++; eventWorst = Math.Max(eventWorst, Arms.LastStepMs); continue; }
            worst = Math.Max(worst, Arms.LastStepMs);
            bytes += Arms.LastStepBytes;
            if (Arms.LastStepBytes > 0) where.Add($"tick {i}: {Arms.LastStepBytes} B");
        }
        if (where.Count > 0) GD.Print("      allocating ticks: " + string.Join(", ", where));
        {
            N.Vector3 e = Eye.ToNumerics();
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Arms.Seek(GalHack.Arms.Seek.Flesh, e, N.Vector3.UnitY, 30f, 0f, 0, out _);
            long seek = GC.GetAllocatedBytesForCurrentThread() - b0;
            b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Arms.Sweep(e, e + new N.Vector3(0.3f, 1f, 0.2f) * 5f, null, 0, 1f, out _);
            long sweep = GC.GetAllocatedBytesForCurrentThread() - b0;
            b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Arms.Effects.Tick(1f / 60f, Arms);
            long effects = GC.GetAllocatedBytesForCurrentThread() - b0;
            GD.Print($"      allocation probe: 100 seeks {seek} B, 100 sweeps {sweep} B, 100 effect ticks {effects} B");
            // One hornet dropped onto the ground, stepped by hand: what does each tick around its bounce allocate?
            Arms.Pool.Clear();
            WeaponDef hv = Arms.Table.Get("hornet-hive");
            Vector3 over = Zone.GroundAt(Feet.X + 3f, Feet.Z + 3f) + Vector3.Up * 1.5f;
            Arms.Pool.Spawn(hv, 0, over.ToNumerics(), new N.Vector3(0.5f, -13f, 0.3f), 6f);
            var log = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                int ev = Arms.Hits + Arms.Bounces;
                long b1 = GC.GetAllocatedBytesForCurrentThread();
                Arms.Pool.Step(1f / 60f, Arms);
                long used = GC.GetAllocatedBytesForCurrentThread() - b1;
                if (used > 0 || Arms.Hits + Arms.Bounces != ev) log.Add($"{i}:{used}B{(Arms.Hits + Arms.Bounces != ev ? "*" : "")}");
            }
            GD.Print($"      hand-stepped hornet (* a bounce or hit): {string.Join(" ", log)}");
            Arms.Pool.Clear();
        }
        double mean = ticks > 0 ? total / ticks : double.MaxValue;
        Check(live >= 28 && ticks >= 20, $"20 rounds and a swarm of 8 in flight ({live} shots; {rounds} rounds then {fired} with the swarm as fired), timed over {ticks} ticks ({Arms.Recent()})");
        Check(mean < StepBudgetMs && worst < StepBudgetMs * 4, $"the shots' step: {mean:F3} ms a tick on average, {worst:F3} ms at worst in steady flight, {eventWorst:F3} ms on a tick with a hit (budget {StepBudgetMs} ms)");
        Check(bytes == 0, $"and it allocates nothing while they fly ({bytes} bytes over {ticks - eventful} ticks of steady flight; {eventful} ticks had a hit or bounce)");
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(Arms.Fx.Drawn > 0 || Arms.Pool.Live == 0, "every live shot is drawn from the pool");
    }
}
