using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Arms;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics.Engine;
using Godot;
using PhysicsSandbox.Bridges;
using Planets.Gen;
using Slice.Game;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;
using Wildlife;
using N = System.Numerics;
using S = GalHack.Structures;

namespace Slice.Tests;

/// <summary>
/// The Sleeper tool (owner, 15 Nov), on a fresh world every run, through the game's own keys:
///
/// - Two modes only, Q between them: attack/gather (blue) and repair (green).
/// - A blast is lightning: a bolt, a flash that lights the surroundings, a crackle and a thunderclap
///   from the shot pipeline's sound pool, and the tool kicks.
/// - A block on a core the sleeper owns, or on no core, comes back whole to the bag at once.
/// - A block on a core that isn't a sleeper's takes damage with arcs over it, and gives up a share of
///   its bill as it goes (blasted to nothing: about that share, never the block itself).
/// - A person in front: a short arc, a chip of damage, held stunned in their flinch for the data's
///   moment, then free, and not stunned again at once.
/// - Ore: the yield is what it was (every cell taken pays its whole share), with lightning to the ground.
/// - Repair: a green cone; a damaged block mended and a destroyed one restored when the bag holds what
///   they take, nothing without; harmless to a person and a block in it.
///
/// --shots=FOLDER (run windowed): blue lightning on a block, the arc to a person, the repair cone, lightning at ore.
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/SleeperToolTest.tscn
/// </summary>
public partial class SleeperToolTest : Node
{
    const int WatchdogMinutes = 9;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();
    Game.Game _game;
    Driver _drive;
    Playthrough _play;
    string _shots;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        _shots = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--shots="))?.Substring(8);
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
        if (_game?.Suit != null) _game.Suit.Vitals.Health = _game.Suit.Sim.Data.Vitals.Health.Max; // nothing out here ends the run
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    SleeperTool Tool => _game.Tool;
    ShotFx Fx => _game.Shots;
    SliceRules.LightningRules Bolt => _game.Zone.Data.Rules.Tool.Lightning;
    int Played(CombatSound k) => _game.Arms.Sounds.PlayedShared[(int)k];

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Fresh("temperate")) };
        AddChild(_game);
        _drive = new Driver(_game);
        _play = new Playthrough(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.4;
        foreach (int x in _play.MakeTool()) yield return x;
        Vector3 site = _play.FlatSpot(_game.Start, 40f, span: 12f);
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(site.X, site.Z) + Vector3.Up * 0.3f).ToNumerics());
        foreach (int x in _drive.Wait(90)) yield return x;
        foreach (int x in Modes()) yield return x;
        foreach (int x in Blocks()) yield return x;
        foreach (int x in Person()) yield return x;
        foreach (int x in Repair()) yield return x;
        foreach (int x in Ore()) yield return x;
        _game.QueueFree();
        yield return 0;
    }

    void Picture(string name)
    {
        if (_shots == null) return;
        string file = System.IO.Path.Combine(_shots, name);
        GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print($"  (picture: {file})");
    }

    /// <summary>For a picture: the next blast or cone held where it is (the effects and the hands stop), the picture taken, then everything runs on.</summary>
    IEnumerable<int> Freeze(string name)
    {
        if (_shots == null) yield break;
        Fx.SetProcess(false);
        _game.Hands.SetProcess(false);
        foreach (int x in _drive.Wait(4)) yield return x;
        Picture(name);
        Fx.SetProcess(true);
        _game.Hands.SetProcess(true);
    }

    // ---- Modes and the blast itself ------------------------------------------------------------

    IEnumerable<int> Modes()
    {
        GD.Print("-- two modes, and a blast of lightning --");
        foreach (int x in _play.Tool(ToolMode.Gather)) yield return x;
        Check(Enum.GetValues<ToolMode>().Length == 2 && Tool.Mode == ToolMode.Gather, $"the Sleeper tool has two modes ({string.Join(", ", Enum.GetValues<ToolMode>())}), attack/gather first");
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
        bool repair = Tool.Mode == ToolMode.Repair;
        string caption = _game.Visor.ItemLine();
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
        Check(repair && Tool.Mode == ToolMode.Gather && caption.Contains("REPAIR") && _game.Visor.ItemLine().Contains("ATTACK/GATHER"),
            $"Q switches to repair and back ({caption} / {_game.Visor.ItemLine()})");

        // Into the sky: nothing to meet, all lightning.
        Vector3 eye = _game.Survivor.WorldEye();
        foreach (int x in _drive.LookAt(eye + new Vector3(0.4f, 1f, 0.3f).Normalized() * 20f, 8)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        int shots = Tool.Shots, bolts = Fx.LightningBolts, flashes = Fx.LightningFlashes, crackle = Played(CombatSound.Crackle), thunder = Played(CombatSound.Thunder);
        _game.Hands.ResetPeaks();
        foreach (int x in _drive.Click()) yield return x;
        bool lit = Fx.SparksShowing > 0;
        foreach (int x in _drive.Wait(20)) yield return x;
        Check(Tool.Shots == shots + 1 && Fx.LightningBolts == bolts + 1 && Fx.LightningFlashes == flashes + 1 && lit,
            $"a click is one blast: a bolt of lightning and a flash that lights round it ({Fx.LightningBolts - bolts} bolt, {Fx.LightningFlashes - flashes} flash)");
        Check(Played(CombatSound.Crackle) == crackle + 1 && Played(CombatSound.Thunder) == thunder + 1, "with a crackle and a thunderclap, from the shot pipeline's sound pool");
        Check(_game.Hands.PeakKick > 0.5f && Tool.Kick.Recoil == Bolt.Kick, $"and the tool kicks ({_game.Hands.PeakKick:F2}, recoil {Tool.Kick.Recoil} from the data)");
        Check(Fx.SparksShowing == 0, "the lightning is gone a moment later");
    }

    // ---- Blocks: ours whole into the bag, someone else's damaged for a share ---------------------

    /// <summary>A few iron blocks off a core (or none), set down on the ground a few metres off.</summary>
    Vessel Row(string owner, bool core, float bearing, out S.Int3[] cells)
    {
        S.Catalogue cat = _game.Zone.Data.Catalogue;
        var s = new S.Structure(cat) { Name = $"Sleeper tool test {owner}" };
        S.BlockType bare = cat.Type("iron-plated");
        if (core) S.Building.Place(s, S.Int3.Zero, S.Building.Make(s, cat.CoreType), null, owner);
        // Three along from the core, and one stood on the first: the end and the top are leaves.
        cells = new[] { new S.Int3(1, 0, 0), new S.Int3(2, 0, 0), new S.Int3(3, 0, 0), new S.Int3(1, 1, 0) };
        foreach (S.Int3 c in cells) s.Set(c, S.Building.Make(s, bare));
        if (!core) s.Set(S.Int3.Zero, S.Building.Make(s, bare));
        s.DirtyChunks.Clear();
        Vector3 feet = _game.Survivor.WorldFeet;
        Vector3 spot = feet + new Vector3(MathF.Cos(bearing), 0f, MathF.Sin(bearing)) * 3.5f;
        Vector3 ground = _game.Zone.GroundAt(spot.X, spot.Z);
        N.Vector3 at = (ground + Vector3.Up * (s.Grid.CellSize * 0.5f + 0.3f)).ToNumerics();
        Vessel v = core ? Vessel.SpawnBase(_game.Zone, s, at, bearing) : Vessel.SpawnGroundWreck(_game.Zone, s, at, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -bearing));
        foreach (var (p, _) in s.Blocks()) v.ClearGround(p);
        return v;
    }

    float _bearing = float.NaN;

    /// <summary>A fresh bearing for the next structure, well round from the last, so none stands in another's way.</summary>
    float NextBearing() => _bearing = float.IsNaN(_bearing) ? (float)_rng.NextDouble() * MathF.Tau : _bearing + 1.55f;

    Vector3 Centre(Vessel v, S.Int3 c) => v.ToWorld(StructureBridge.CellCenter(v.Structure, c));

    /// <summary>The block's face toward the eye: where a blast at it lands.</summary>
    Vector3 FaceOf(Vessel v, S.Int3 c)
    {
        Vector3 centre = Centre(v, c), eye = _game.Survivor.WorldEye();
        return centre + (eye - centre).Normalized() * v.Structure.Grid.CellSize * 0.45f;
    }

    IEnumerable<int> Blast(Vector3 at, int clicks = 1)
    {
        foreach (int x in _drive.LookAt(at, 6)) yield return x;
        for (int i = 0; i < clicks; i++)
        {
            _drive.Face(at);
            foreach (int x in _drive.Click()) yield return x;
            foreach (int x in _drive.Wait((int)(_game.Zone.Data.Rules.Tool.ShotInterval * 60f) + 2)) yield return x;
        }
    }

    /// <summary>What the blast would meet along the look, and what the take-down is on (for a failure's line).</summary>
    string Aimed(Vessel v)
    {
        Vector3 eye = _game.Survivor.WorldEye(), dir = _game.Survivor.WorldLook();
        bool met = _game.Arms.Sweep(eye.ToNumerics(), (eye + dir * Bolt.Range).ToNumerics(), Slice.Arms.ArmsHost.PlayerThing, 0, 0f, out ShotHit hit);
        return $"the blast meets {(met ? hit.Kind.ToString() : "nothing")}{(met && hit.Thing == v ? " on it" : "")} at {hit.Distance:F1} m; the take-down on it {Tool.OnStructure && _game.Builder.TargetVessel == v} ({_game.Builder.Verdict})";
    }

    /// <summary>The cell of a structure the blast along the look meets first, if it meets that structure.</summary>
    S.Int3? Met(Vessel v)
    {
        Vector3 eye = _game.Survivor.WorldEye(), dir = _game.Survivor.WorldLook();
        return _game.Arms.Sweep(eye.ToNumerics(), (eye + dir * Bolt.Range).ToNumerics(), Slice.Arms.ArmsHost.PlayerThing, 0, 0f, out ShotHit hit)
               && hit.Kind == HitKind.Block && hit.Thing == v ? new S.Int3(hit.CellX, hit.CellY, hit.CellZ) : null;
    }

    /// <summary>To fresh open ground, well away from the structures set down so far.</summary>
    IEnumerable<int> MoveOn()
    {
        Vector3 site = _play.FlatSpot(_game.Survivor.WorldFeet, 25f, (float)_rng.NextDouble() * MathF.Tau, span: 10f);
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(site.X, site.Z) + Vector3.Up * 0.3f).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
    }

    IEnumerable<int> Blocks()
    {
        GD.Print("-- blocks: ours come back whole; someone else's give up a share --");
        ItemCatalogue items = _game.Zone.Data.Items;
        foreach (int x in _play.Tool(ToolMode.Gather)) yield return x;
        foreach (var (label, core) in new[] { ("ours", true), ("no core", false) })
        {
            Vessel v = Row(S.Building.DefaultOwner, core, NextBearing(), out S.Int3[] cells);
            foreach (int x in _drive.Wait(30)) yield return x;
            S.Int3 end = cells[2];
            string item = BuildingBridge.ItemOf(items, v.Structure, v.Structure.Get(end)).Id;
            int bag = _game.Pack[item], whole = Tool.TakenWhole, count = v.Structure.Count;
            foreach (int x in _drive.LookAt(FaceOf(v, end), 6)) yield return x;
            string aimed = Aimed(v);
            foreach (int x in Blast(FaceOf(v, end))) yield return x;
            Check(SleeperTool.Ours(v) && v.Structure.Count < count && _game.Pack[item] == bag + 1 && Tool.TakenWhole == whole + 1,
                $"{label}: one blast and the block is back in the bag, whole (+{_game.Pack[item] - bag} {items.Item(item).Name}; core {(v.Structure.Core != null ? v.Structure.Owner : "none")}; {aimed})");
        }

        // Someone else's core: damaged a share at a time, arcs over it, a share of its bill as it goes.
        Vessel theirs = Row("raiders", true, NextBearing(), out S.Int3[] row);
        foreach (int x in _drive.Wait(30)) yield return x;
        // The block a blast at the row's end meets first (from where you stand it may be another).
        foreach (int x in _drive.LookAt(FaceOf(theirs, row[2]), 6)) yield return x;
        S.Int3 cell = Met(theirs) ?? row[2];
        S.Structure ts = theirs.Structure;
        S.Block b = ts.Get(cell);
        float max = ts.StatsOf(b).MaxHp;
        string blockItem = BuildingBridge.ItemOf(items, ts, b).Id;
        Bill bill = BuildingBridge.BillOf(items, ts, b);
        var before = bill.ToDictionary(kv => kv.Key, kv => _game.Pack[kv.Key]);
        int whole0 = _game.Pack[blockItem], arcs = Fx.LightningArcs, extracted = Tool.Extracted;
        foreach (int x in Blast(FaceOf(theirs, cell))) yield return x;
        float hp = ts.Has(cell) ? ts.Get(cell).Hp : 0f;
        Check(!SleeperTool.Ours(theirs) && ts.Has(cell) && hp < max && Tool.Extracted == extracted + 1 && Fx.LightningArcs >= arcs + 3,
            $"a raider core's block takes damage instead ({max:F0} to {hp:F0} hit points), the lightning arcing over it ({Fx.LightningArcs - arcs} arcs)");
        if (_shots != null)
        {
            foreach (int x in _drive.Click()) yield return x;
            foreach (int x in Freeze("sleeper-block.png")) yield return x;
        }
        for (int i = 0; i < 40 && ts.Has(cell); i++) foreach (int x in Blast(FaceOf(theirs, cell))) yield return x;
        var got = bill.ToDictionary(kv => kv.Key, kv => _game.Pack[kv.Key] - before[kv.Key]);
        double share = Bolt.ExtractShare;
        bool fair = bill.All(kv => got[kv.Key] <= Math.Ceiling(kv.Value * share + 1e-6) && got[kv.Key] >= Math.Floor(kv.Value * share * 0.75 - 1e-6));
        Check(!ts.Has(cell) && got.Values.Sum() > 0 && fair && _game.Pack[blockItem] == whole0,
            $"blasted to nothing it gave up about {share:P0} of its bill ({string.Join(", ", bill.Select(kv => $"{got[kv.Key]} of {kv.Value} {kv.Key}"))}), never the block itself");
    }

    // ---- People: a chip and a stun -----------------------------------------------------------------

    /// <summary>One of the wild's people (a worker, nobody's enemy) set down on open ground in front of the sleeper, so far off.</summary>
    Creature Bystander(float far)
    {
        WildlifePatch patch = _game.Zone.Wildlife;
        Species s = patch.Life.People.Person((ulong)_rng.NextInt64(), PersonRole.Worker, Occupancy.Friendly);
        Vector3 feet = _game.Survivor.WorldFeet, look = _game.Survivor.WorldLook();
        look.Y = 0f;
        look = look.LengthSquared() < 1e-4f ? Vector3.Forward : look.Normalized();
        Vector3 spot = _game.Zone.GroundAt(feet.X + look.X * far, feet.Z + look.Z * far);
        return patch.Sim.Spawn(s, patch.PatchLocal(spot));
    }

    Vector3 Chest(Creature c) => _game.Zone.Wildlife.WorldOf(c) + Vector3.Up * c.Species.Figure.Height * 0.62f;

    IEnumerable<int> Person()
    {
        GD.Print("-- a person in front: zapped, chipped, stunned in their flinch --");
        foreach (int x in MoveOn()) yield return x;
        foreach (int x in _play.Tool(ToolMode.Gather)) yield return x;
        Creature c = Bystander(4f);
        foreach (int x in _drive.Wait(40)) yield return x;
        float health = c.Health;
        int zapped = Tool.Zapped, confirms = _game.Arms.Confirms;
        foreach (int x in _drive.LookAt(Chest(c), 6)) yield return x;
        _drive.Face(Chest(c));
        foreach (int x in _drive.Click()) yield return x;
        Vector3 held = _game.Zone.Wildlife.WorldOf(c);
        bool stunned = c.StunFor > 0f && c.Rig is HumanRig { HoldFlinch: > 0f };
        float moved = 0f, shown = 0f;
        for (float t = 0f; t < Bolt.StunSeconds - 0.1f; t += 1f / 60f)
        {
            moved = MathF.Max(moved, _game.Zone.Wildlife.WorldOf(c).DistanceTo(held));
            if (c.StunFor > 0f) shown = t;
            yield return 0;
        }
        Check(Tool.Zapped == zapped + 1 && c.Health < health && health - c.Health <= Bolt.PersonDamage + 0.01f && !c.Dead,
            $"the arc jumps to them: a chip of damage ({health:F0} to {c.Health:F0}, {Bolt.PersonDamage} in the data), and they live");
        Check(stunned && moved < 0.15f && shown >= Bolt.StunSeconds - 0.25f,
            $"and they're held stunned in their flinch for {Bolt.StunSeconds} s (moved {moved:F2} m, stunned for {shown:F2} s)");
        Check(_game.Arms.Confirms > confirms, "the hit is confirmed, as any of the sleeper's hits");
        foreach (int x in _drive.Wait(30)) yield return x;
        float again = c.Health;
        foreach (int x in _drive.LookAt(Chest(c), 4)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        Check(c.StunFor <= 0f && c.Health < again, $"the stun has worn off, and a blast straight after it chips again but doesn't stun again for {Bolt.RestunSeconds} s");
        if (_shots != null)
        {
            Creature p = Bystander(3.5f);
            foreach (int x in _drive.Wait(40)) yield return x;
            foreach (int x in _drive.LookAt(Chest(p), 6)) yield return x;
            foreach (int x in _drive.Click()) yield return x;
            foreach (int x in Freeze("sleeper-person.png")) yield return x;
        }
        _repairWitness = Bystander(3f);
    }

    Creature _repairWitness;

    // ---- Repair: the green cone ----------------------------------------------------------------------

    IEnumerable<int> HoldRepair(Vector3 at, int ticks)
    {
        foreach (int x in _drive.LookAt(at, 6)) yield return x;
        foreach (int x in _drive.HoldPrimary(at, ticks)) yield return x;
    }

    IEnumerable<int> Repair()
    {
        GD.Print("-- repair: mends and restores with the ingredients, harmless --");
        ItemCatalogue items = _game.Zone.Data.Items;
        foreach (int x in _play.Tool(ToolMode.Gather)) yield return x;
        Vessel v = Row(S.Building.DefaultOwner, true, NextBearing(), out S.Int3[] cells);
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
        S.Structure s = v.Structure;
        // The block the look lands on when aimed at the row's first (from where you stand that may be the core).
        foreach (int x in _drive.LookAt(FaceOf(v, cells[0]), 6)) yield return x;
        S.Int3 hurt = WorldRay.Cast(_game.Zone, _game.Survivor, _game.Zone.Data.Rules.Tool.Repair.Range, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull, out LookHit on) && on.Vessel == v ? on.Cell : cells[0], gone = cells[3];
        float max = s.StatsOf(s.Get(hurt)).MaxHp;
        s.SetHp(hurt, max * 0.3f);
        v.ApplyEdits();
        Bill bill = v.Hits.RepairBill(hurt);
        void Empty() { foreach (var (id, _) in bill) { _game.Pack.Remove(id, _game.Pack[id]); v.Storage.Remove(id, v.Storage.Count(id)); } }
        Empty();
        int shots = Tool.Shots, cones = Fx.Cones, mends = Played(CombatSound.Mend);
        bool cone = false;
        foreach (int x in _drive.LookAt(FaceOf(v, hurt), 6)) yield return x;
        for (int i = 0; i < 60; i++) { _drive.Face(FaceOf(v, hurt)); _game.Controls.Primary = true; yield return 0; cone |= Fx.ConeShowing; }
        _game.Controls.Primary = false;
        foreach (int x in _drive.Wait(12)) yield return x;
        Check(cone && Fx.Cones > cones && !Fx.ConeShowing && Played(CombatSound.Mend) > mends && Tool.Shots == shots,
            $"held, repair shows the green cone with its lighter sound ({Played(CombatSound.Mend) - mends} pulses), gone when let go, and no blast or kick");
        Check(s.Get(hurt).Hp <= max * 0.3f + 0.01f, $"without the ingredients ({items.Describe(bill, 3)}) nothing is mended");
        foreach (var (id, n) in bill) _game.Pack.Add(id, n);
        int repaired = Tool.Repaired;
        foreach (int x in HoldRepair(FaceOf(v, hurt), 120)) yield return x;
        bool onIt = WorldRay.Cast(_game.Zone, _game.Survivor, _game.Zone.Data.Rules.Tool.Repair.Range, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull, out LookHit look);
        Check(s.Get(hurt).Hp >= max - 0.01f && Tool.Repaired > repaired && bill.All(kv => _game.Pack[kv.Key] < kv.Value),
            $"with them in the bag it is mended to full ({s.Get(hurt).Hp:F0} of {max:F0}), paid from the bag (last pulse: {Tool.LastMend}; the look on {(onIt ? $"{look.Vessel?.Name ?? "ground"} {look.Cell} at {look.Distance:F1} m" : "nothing")})");

        // Destroyed: restored from what the bag holds, nothing without.
        S.Block was = s.Get(gone);
        string item = BuildingBridge.ItemOf(items, s, was).Id;
        Bill whole = BuildingBridge.BillOf(items, s, was);
        v.Hits.Shot(gone, Centre(v, gone).ToNumerics(), N.Vector3.UnitY, max * 20f, S.DamageType.Kinetic);
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(!s.Has(gone) && v.Hits.Fallen.ContainsKey(gone), "a block shot to nothing is remembered where it stood");
        _game.Pack.Remove(item, _game.Pack[item]);
        foreach (var (id, _) in whole) _game.Pack.Remove(id, _game.Pack[id]);
        foreach (int x in HoldRepair(Centre(v, gone), 60)) yield return x;
        Check(!s.Has(gone), "without its block or its bill, repair can't restore it");
        _game.Pack.Add(item, 1);
        int restored = Tool.Restored;
        foreach (int x in HoldRepair(Centre(v, gone), 60)) yield return x;
        Check(s.Has(gone) && s.TypeOf(s.Get(gone)) == s.TypeOf(was) && s.Get(gone).Hp >= s.StatsOf(s.Get(gone)).MaxHp - 0.01f && _game.Pack[item] == 0 && Tool.Restored == restored + 1,
            "with the block in the bag, repair restores it where it stood, whole");

        // Harmless: a person and someone else's block in the cone come to no harm.
        Vessel theirs = Row("raiders", true, NextBearing(), out S.Int3[] row);
        foreach (int x in _drive.Wait(30)) yield return x;
        float blockHp = theirs.Structure.Get(row[^1]).Hp;
        foreach (int x in HoldRepair(FaceOf(theirs, row[^1]), 90)) yield return x;
        Creature c = _repairWitness;
        float health = c?.Health ?? 0f;
        int zapped = Tool.Zapped;
        if (c != null) foreach (int x in HoldRepair(Chest(c), 90)) yield return x;
        Check(c != null && c.Health >= health && c.StunFor <= 0f && Tool.Zapped == zapped && theirs.Structure.Get(row[^1]).Hp >= blockHp,
            $"harmless: a person in the cone ({health:F0} to {c?.Health ?? 0f:F0} health, not stunned) and a raider core's block ({blockHp:F0} to {theirs.Structure.Get(row[^1]).Hp:F0}) untouched");
        if (_shots != null)
        {
            s.SetHp(cells[1], max * 0.3f);
            v.ApplyEdits();
            foreach (var (id, n) in v.Hits.RepairBill(cells[1])) _game.Pack.Add(id, n * 4);
            foreach (int x in _drive.LookAt(FaceOf(v, cells[1]), 6)) yield return x;
            for (int i = 0; i < 8; i++) { _game.Controls.Primary = true; yield return 0; }
            foreach (int x in Freeze("sleeper-repair.png")) yield return x;
            _game.Controls.Primary = false;
        }
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
    }

    // ---- Ore: as it was, with lightning -------------------------------------------------------------

    IEnumerable<int> Ore()
    {
        GD.Print("-- ore: the yield as it was, lightning to the ground --");
        DepositKind kind = DepositFinder.Kinds.First();
        string ore = Tool.OreOf(kind).Id;
        int before = _game.Pack[ore], cells = Tool.OreCells, rock = Tool.TellOre, bolts = Fx.LightningBolts, arcs = Fx.LightningArcs;
        foreach (int x in _play.Gather(kind, before + 8, 500f)) yield return x;
        int dug = Tool.OreCells - cells, got = _game.Pack[ore] - before;
        float per = _game.Zone.Data.Rules.Materials.OrePerCell;
        Check(dug > 0 && got >= dug * per + (Tool.TellOre - rock) && got <= (dug + 4) * per + (Tool.TellOre - rock) + 1,
            $"digging {kind} pays {per} {ore} a cell as before: {dug} cells and {Tool.TellOre - rock} off its rock, +{got}");
        Check(Fx.LightningBolts - bolts >= dug && Fx.LightningArcs - arcs >= dug,
            $"each pulse is a flash of lightning arcing to the ground being worked ({Fx.LightningBolts - bolts} bolts, {Fx.LightningArcs - arcs} arcs)");
        if (_shots != null && Tool.HasTarget)
        {
            Vector3 t = new(Tool.Target.X + 0.5f, Tool.Target.Y + 0.5f, Tool.Target.Z + 0.5f);
            foreach (int x in _drive.LookAt(t, 6)) yield return x;
            foreach (int x in _drive.Click()) yield return x;
            foreach (int x in Freeze("sleeper-ore.png")) yield return x;
        }
    }
}
