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
    const int WatchdogMinutes = 12;
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
        foreach (int x in ShakeSync()) yield return x;
        foreach (int x in NoBuildUp()) yield return x;
        if (_shots != null) foreach (int x in BlockPicture()) yield return x;
        _game.QueueFree();
        yield return 0;
    }

    void Picture(string name)
    {
        if (_shots == null) return;
        string file = System.IO.Path.Combine(_shots, name);
        GetViewport().GetTexture().GetImage().SavePng(file);
        // Every whirl the weather has going (a dust devil, a waterspout's thin rope, a tornado), where it stands from the eye.
        Camera3D cam = _game.Camera;
        var whirls = _game.Zone.Weather.Sim.Tornadoes.Select(t =>
        {
            var fx = _game.Zone.Weather.FunnelOf(t.Id);
            Vector3 at = fx?.GlobalPosition ?? cam.GlobalPosition;
            Vector3 to = at - cam.GlobalPosition;
            float bearing = (Mathf.RadToDeg(MathF.Atan2(to.X, -to.Z)) + 360f) % 360f;
            return $"a {t.Kind} {to.Length():F0} m off at bearing {bearing:F0}{(fx != null && cam.IsPositionInFrustum(at + Vector3.Up * 20f) ? ", in view" : "")}";
        });
        // And anything tall and thin in view (a mast, a cable, a rope): what it is and where it hangs from.
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        var thin = new List<string>();
        foreach (Node n in GetTree().Root.FindChildren("*", nameof(GeometryInstance3D), true, false))
        {
            if (n is not GeometryInstance3D g || !g.IsVisibleInTree()) continue;
            Aabb box = g.GlobalTransform * g.GetAabb();
            float wide = MathF.Max(box.Size.X, box.Size.Z);
            if (box.Size.Y < 12f || wide > box.Size.Y * 0.15f) continue;
            Vector3 mid = box.GetCenter();
            if (mid.DistanceTo(cam.GlobalPosition) > 60000f) continue; // in view or not: anything of the kind about, however far
            Vector2 at = cam.UnprojectPosition(mid) / screen;
            thin.Add($"{(cam.IsPositionInFrustum(mid) ? "IN VIEW " : "")}{g.GetPath()} ({g.GetType().Name}, {box.Size.Y:F0} m tall, {wide:F1} m wide, {mid.DistanceTo(cam.GlobalPosition):F0} m off, top {box.End.Y - _game.Zone.GroundAt(mid.X, mid.Z).Y:F0} m over the ground, at {at.X:F2} {at.Y:F2} of the frame)");
        }
        GD.Print($"  (picture: {file}; whirls: {string.Join("; ", whirls.DefaultIfEmpty("none"))}; tall and thin in view: {string.Join(" | ", thin.Take(12).DefaultIfEmpty("none"))})");
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
        Check(_game.Hands.PeakBuzz > 0.5f && _game.Hands.PeakKick == 0f && _game.Hands.PeakTilt == 0f, $"and the tool shakes in the hand ({_game.Hands.PeakBuzz:F2}), the view still");
        Check(Fx.SparksShowing == 0, "the lightning is gone a moment later");
        BoltLook();
    }

    /// <summary>
    /// Every bolt (the tool's, a storm's, an arc gun's) is drawn by one renderer: its band's brightness
    /// across the strip falls smoothly to nothing at the edge with no step anywhere outside the thin core,
    /// and each channel is one strip, edges shared at every joint, untwisted, closed to a point at both
    /// ends, never thinner than a few pixels far off.
    /// </summary>
    void BoltLook()
    {
        float edge = PlanetSurface.Fx.Bolts.Profile(1f), nearEdge = PlanetSurface.Fx.Bolts.Profile(0.97f), mid = PlanetSurface.Fx.Bolts.Profile(0f);
        float step = 0f;
        for (float d = 0.4f; d < 1f; d += 1f / 128f) step = MathF.Max(step, PlanetSurface.Fx.Bolts.Profile(d) - PlanetSurface.Fx.Bolts.Profile(d + 1f / 128f));
        float core = PlanetSurface.Fx.Bolts.Profile(PlanetSurface.Fx.Bolts.CoreShare * 2f);
        Check(mid >= 1f && core < 0.35f * mid && edge < 1e-4f && nearEdge < 1e-3f && step < 0.01f,
            $"a bolt's band: bright core ({mid:F2}), down to {core:F2} two core-widths out, then fading to nothing at its edge ({edge:F5}; {nearEdge:F5} just inside), no step over {step:F4} a 128th of the way");
        int bad = 0, channels = 0;
        var rng = new Random();
        Vector3 eye = _game.Survivor.WorldEye();
        foreach (float far in new[] { 3f, 40f, 300f })
            for (int k = 0; k < 10; k++)
            {
                Vector3 a = eye + new Vector3(far, 1f, 0.5f), b = a + new Vector3(rng.NextSingle() - 0.5f, -1f, rng.NextSingle() - 0.5f) * 6f;
                var path = new List<Vector3>();
                PlanetSurface.Fx.Bolts.Jag(path, a, b, 0.2f, 5, () => rng.NextSingle());
                var e = PlanetSurface.Fx.Bolts.Strip(new PlanetSurface.Fx.Bolts.Channel(path, 0.11f, 1f), eye, 1f);
                channels++;
                float widest = e.Max(x => x.left.DistanceTo(x.right));
                bool ends = e[0].left.DistanceTo(e[0].right) < 0.05f * widest && e[^1].left.DistanceTo(e[^1].right) < 0.05f * widest;
                bool untwisted = true;
                for (int i = 1; i < e.Length; i++) if ((e[i].right - e[i].left).Dot(e[i - 1].right - e[i - 1].left) < 0f) untwisted = false;
                bool seen = widest >= a.DistanceTo(eye) * 0.004f * 0.6f * 0.99f; // (the strip narrows toward its far end to 0.6)
                if (!ends || !untwisted || !seen || e.Length != path.Count) bad++;
            }
        Check(bad == 0, $"each bolt channel is one strip with an edge pair per joint, shared by the pieces either side: untwisted, closed at both ends, a few pixels wide at 3, 40 and 300 m ({channels} channels, {bad} not)");
    }

    // ---- Blocks: ours whole into the bag, someone else's damaged for a share ---------------------

    Vessel Row(string owner, bool core, float bearing, out S.Int3[] cells) => TestRows.Row(_game, owner, core, bearing, out cells);

    float _bearing = float.NaN;

    /// <summary>A fresh bearing for the next structure, well round from the last, so none stands in another's way.</summary>
    float NextBearing() => _bearing = float.IsNaN(_bearing) ? (float)_rng.NextDouble() * MathF.Tau : _bearing + 1.55f;

    Vector3 Centre(Vessel v, S.Int3 c) => TestRows.Centre(v, c);
    Vector3 FaceOf(Vessel v, S.Int3 c) => TestRows.FaceOf(_game, v, c);

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
            S.Int3 end = cells[3]; // the block stood on the first: up where the core can't be in front of it
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
        // The first blast at the row's end: whichever of its blocks it meets first (from where you stand that may be another) is the one followed.
        S.Structure ts = theirs.Structure;
        var hpBefore = ts.Blocks().ToDictionary(x => x.pos, x => x.block.Hp);
        var bag0 = _game.Pack.Storage.Contents.ToDictionary(kv => kv.Key, kv => kv.Value);
        int arcs = Fx.LightningArcs, extracted = Tool.Extracted;
        foreach (int x in Blast(FaceOf(theirs, row[3]))) yield return x;
        var hurt = ts.Blocks().Where(x => x.block.Hp < hpBefore.GetValueOrDefault(x.pos) - 0.01f).ToList();
        S.Int3 cell = hurt.Count > 0 ? hurt[0].pos : row[3];
        S.Block b = ts.Get(cell);
        float max = ts.StatsOf(b).MaxHp, hp = b.Hp;
        Bill bill = BuildingBridge.BillOf(items, ts, b);
        string blockItem = BuildingBridge.ItemOf(items, ts, b).Id;
        var before = bill.ToDictionary(kv => kv.Key, kv => bag0.GetValueOrDefault(kv.Key));
        int whole0 = bag0.GetValueOrDefault(blockItem);
        Check(!SleeperTool.Ours(theirs) && hurt.Count == 1 && Tool.Extracted == extracted + 1 && Fx.LightningArcs >= arcs + 3,
            $"a raider core's block takes damage instead ({max:F0} to {hp:F0} hit points), the lightning arcing over it ({Fx.LightningArcs - arcs} arcs)");
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
        Vessel v = TestRows.Row(_game, S.Building.DefaultOwner, true, NextBearing(), out S.Int3[] cells, off: 5f); // far enough that none of it stands at your feet
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
        var bag = bill.ToDictionary(kv => kv.Key, kv => _game.Pack[kv.Key]);
        int repaired = Tool.Repaired;
        // Held half a second: the hit points climb a little every frame, never a jump, and stop where they are when let go.
        float rate = _game.Zone.Data.Rules.Tool.Repair.Rate, last = s.Get(hurt).Hp, biggest = 0f;
        string readout0 = null, readout1 = null;
        int climbing = 0;
        foreach (int x in _drive.LookAt(FaceOf(v, hurt), 6)) yield return x;
        for (int i = 0; i < 30; i++)
        {
            _drive.Face(FaceOf(v, hurt));
            _game.Controls.Primary = true;
            yield return 0;
            float now = s.Get(hurt).Hp;
            if (now > last + 1e-3f) climbing++;
            biggest = MathF.Max(biggest, now - last);
            last = now;
            if (i == 5) readout0 = _game.Visor.BlockHealth();
            if (i == 25) readout1 = _game.Visor.BlockHealth();
        }
        _game.Controls.Primary = false;
        foreach (int x in _drive.Wait(20)) yield return x;
        float partway = s.Get(hurt).Hp;
        Check(climbing >= 20 && biggest <= rate / 60f * 1.5f + 0.01f && partway > max * 0.3f + 1f && partway < max - 1f && readout0 != readout1,
            $"held, its hit points climb a little a frame ({climbing} of 30 frames, at most {biggest:F2} a frame at {rate} a second), the readout counting up ({readout0} then {readout1}), and let go they stop partway ({partway:F0} of {max:F0})");
        foreach (int x in HoldRepair(FaceOf(v, hurt), 600)) yield return x;
        var paid = bill.ToDictionary(kv => kv.Key, kv => bag[kv.Key] - _game.Pack[kv.Key]);
        var whole0 = BuildingBridge.BillOf(items, s, s.Get(hurt));
        bool fair = whole0.All(kv => paid.GetValueOrDefault(kv.Key) <= Math.Ceiling(kv.Value * 0.7 + 1e-6) && paid.GetValueOrDefault(kv.Key) >= Math.Floor(kv.Value * 0.7 - 1e-6));
        Check(s.Get(hurt).Hp >= max - 0.01f && Tool.Repaired > repaired && fair,
            $"held on, it fills to {s.Get(hurt).Hp:F0} of {max:F0}, paid as it went, in proportion: {string.Join(", ", whole0.Select(kv => $"{paid.GetValueOrDefault(kv.Key)} of its {kv.Value} {kv.Key}"))} for the 70% it was missing (last: {Tool.LastMend})");

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
        Check(!s.Has(gone), "without its bill, repair can't restore it");
        foreach (var (id, n) in whole) _game.Pack.Add(id, n);
        // Within the cone's reach of where it stood, however the land raised the row.
        Vector3 spot = Centre(v, gone), feet = _game.Survivor.WorldFeet, away = new Vector3(feet.X - spot.X, 0f, feet.Z - spot.Z);
        away = away.LengthSquared() > 1e-4f ? away.Normalized() : Vector3.Right;
        Vector3 near = _game.Zone.GroundAt(spot.X + away.X * 3f, spot.Z + away.Z * 3f);
        _game.Survivor.PlaceOutside((near + Vector3.Up * 0.3f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        int restored = Tool.Restored;
        float first = -1f;
        foreach (int x in _drive.LookAt(Centre(v, gone), 6)) yield return x;
        for (int i = 0; i < 480 && !(s.Has(gone) && s.Get(gone).Hp >= s.StatsOf(s.Get(gone)).MaxHp - 0.01f); i++)
        {
            _drive.Face(Centre(v, gone));
            _game.Controls.Primary = true;
            yield return 0;
            if (first < 0f && s.Has(gone)) first = s.Get(gone).Hp;
        }
        _game.Controls.Primary = false;
        float full = s.Has(gone) ? s.StatsOf(s.Get(gone)).MaxHp : 1f;
        Check(s.Has(gone) && s.TypeOf(s.Get(gone)) == s.TypeOf(was) && first > 0f && first <= full * 0.2f && s.Get(gone).Hp >= full - 0.01f && Tool.Restored == restored + 1,
            $"with its bill in the bag, repair restores it where it stood: back at {first:F0} of {full:F0}, then filled whole (last: {Tool.LastMend})");

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
            // A damaged block in the cone with nothing to mend it: its hit points over the reticle stay low.
            s.SetHp(cells[1], max * 0.3f);
            v.ApplyEdits();
            foreach (var (id, _) in v.Hits.RepairBill(cells[1])) { _game.Pack.Remove(id, _game.Pack[id]); v.Storage.Remove(id, v.Storage.Count(id)); }
            foreach (int x in _drive.LookAt(FaceOf(v, cells[1]), 6)) yield return x;
            GD.Print($"  (the visor over the reticle: {_game.Visor.BlockHealth() ?? "nothing"})");
            for (int i = 0; i < 8; i++) { _game.Controls.Primary = true; yield return 0; }
            foreach (int x in Freeze("sleeper-repair.png")) yield return x;
            _game.Controls.Primary = false;
        }
        foreach (int x in _drive.Tap(Key.Q)) yield return x;
    }

    /// <summary>The picture of a blast on a raider core's block, on fresh ground with nothing else about.</summary>
    IEnumerable<int> BlockPicture()
    {
        foreach (int x in MoveOn()) yield return x;
        foreach (int x in _play.Tool(ToolMode.Gather)) yield return x;
        Vessel theirs = Row("raiders", true, NextBearing(), out S.Int3[] row);
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in _drive.LookAt(FaceOf(theirs, row[1]), 8)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in Freeze("sleeper-block.png")) yield return x;

        // Held fire for 3 s at the blast rate: the frame's brightness, frame by frame, never jumps from dark to full.
        foreach (int x in _drive.Wait(30)) yield return x;
        var means = new List<float>();
        int shots = Tool.Shots;
        for (int i = 0; i < 180; i++)
        {
            _drive.Face(FaceOf(theirs, row[1]));
            _game.Controls.Primary = true;
            yield return 0;
            Image frame = GetViewport().GetTexture().GetImage();
            frame.Resize(64, 36, Image.Interpolation.Bilinear);
            float sum = 0f;
            for (int y = 0; y < 36; y++)
                for (int x = 0; x < 64; x++) sum += frame.GetPixel(x, y).Luminance;
            means.Add(sum / (64 * 36));
        }
        _game.Controls.Primary = false;
        float jump = 0f, swing = means.Max() - means.Min();
        for (int i = 1; i < means.Count; i++) jump = MathF.Max(jump, means[i] - means[i - 1]);
        Check(Tool.Shots - shots >= 8 && jump <= 0.4f * swing + 0.002f,
            $"3 s of held fire ({Tool.Shots - shots} blasts at {Bolt.BoltRise + Bolt.BoltHold + Bolt.BoltFade:F3} s a bolt, its light {Bolt.FlashRise + Bolt.FlashFade:F2} s): the frame's brightness swings {swing:F4} and rises at most {jump:F4} from one frame to the next, never dark to full at once");
    }

    /// <summary>
    /// Held for 3 s each way (owner): digging, where pulses come every dig interval, and blasting, every
    /// shot interval: every bolt drawn shakes the tool on its own tick, no shake without a bolt, and each
    /// shake has died down before the next (quick pulses read as shakes, not a blur). Repair never shakes.
    /// </summary>
    IEnumerable<int> ShakeSync()
    {
        GD.Print("-- the tool shakes while fire is held, a jolt with every bolt --");
        foreach (int x in MoveOn()) yield return x;
        Hands hands = _game.Hands;
        Vector3 feet = _game.Survivor.WorldFeet, eye = _game.Survivor.WorldEye();
        foreach (var (label, at, mode) in new[]
        {
            ("digging", _game.Zone.GroundAt(feet.X + 2f, feet.Z + 1f), ToolMode.Gather),
            ("blasting", eye + new Vector3(0.4f, 1f, 0.3f).Normalized() * 20f, ToolMode.Gather),
            ("repairing", _game.Zone.GroundAt(feet.X + 2f, feet.Z + 1f), ToolMode.Repair),
        })
        {
            foreach (int x in _play.Tool(mode)) yield return x;
            foreach (int x in _drive.LookAt(at, 6)) yield return x;
            int bolts = Fx.LightningBolts, jolts = hands.ToolShakes, still = 0, apart = 0;
            for (int i = 0; i < 180; i++)
            {
                _drive.Face(at);
                _game.Controls.Primary = true;
                yield return 0;
                if (i >= 10 && hands.Buzz <= 0f) still++;
                if (Fx.LightningBolts - bolts != hands.ToolShakes - jolts) apart++; // a bolt without its jolt, or one without a bolt
            }
            _game.Controls.Primary = false;
            int settled = -1;
            for (int i = 0; i < 30; i++)
            {
                yield return 0;
                if (settled < 0 && hands.Buzz <= 0f) settled = i + 1;
            }
            int b = Fx.LightningBolts - bolts, k = hands.ToolShakes - jolts;
            float settle = _game.Zone.Data.Rules.Tool.Lightning.SettleSeconds;
            Check(mode == ToolMode.Repair ? b == 0 && k == 0 && still == 170 : b >= 8 && b == k && apart == 0 && still == 0 && settled > 0 && settled <= (int)MathF.Ceiling(settle * 60f) + 2,
                $"{label} for 3 s: {b} bolts and {k} jolts on their own ticks, the tool {(still == 0 ? "shaking every frame" : $"still for {still} frames")} while held, settled {settled} frames after letting go");
        }
        foreach (int x in _play.Tool(ToolMode.Gather)) yield return x;
    }

    // ---- Held a long while, nothing builds up ---------------------------------------------------------

    static long Objects() => (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
    static long Nodes() => (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
    static double Megabytes() => OS.GetStaticMemoryUsage() / 1048576.0;

    /// <summary>
    /// The trigger held for a long stretch each way a pulse can go (into the ground out of reach, digging, the
    /// repair cone on nothing): nodes, objects and memory level off rather than grow with every blast.
    /// </summary>
    IEnumerable<int> NoBuildUp()
    {
        GD.Print("-- held a long while: nothing builds up --");
        foreach (int x in MoveOn()) yield return x;
        foreach (int x in _play.Tool(ToolMode.Gather)) yield return x;
        Vector3 feet = _game.Survivor.WorldFeet, eye = _game.Survivor.WorldEye();
        foreach (var (label, at, repair) in new[]
        {
            ("blasting the ground beyond reach", _game.Zone.GroundAt(feet.X + 8.5f, feet.Z), false),
            ("digging", _game.Zone.GroundAt(feet.X + 2f, feet.Z + 1f), false),
            ("the repair cone on nothing", eye + Vector3.Up * 20f, true),
        })
        {
            if (repair != (Tool.Mode == ToolMode.Repair)) foreach (int x in _drive.Tap(Key.Q)) yield return x;
            foreach (int x in _drive.LookAt(at, 6)) yield return x;
            int shots = Tool.Shots;
            // Settle in for 32 s (scorch marks last 30), then measure 40 s more of it.
            foreach (int x in _drive.HoldPrimary(at, 1920)) yield return x;
            long nodes = Nodes(), objects = Objects();
            double mb = Megabytes();
            long most = nodes;
            for (int i = 0; i < 2400; i++)
            {
                _drive.Face(at);
                _game.Controls.Primary = true;
                yield return 0;
                if (i % 60 == 0) most = Math.Max(most, Nodes());
            }
            _game.Controls.Primary = false;
            foreach (int x in _drive.Wait(120)) yield return x;
            // (Objects counts what the garbage collector has yet to free, and ground worked or dug adds its own chunks
            // and debris, so the bar is well under a node a blast, and memory level.)
            Check(most - nodes < Math.Max(100, (Tool.Shots - shots) / 2) && Megabytes() - mb < 64,
                $"{label} for 40 s ({Tool.Shots - shots} blasts): nodes {nodes} to at most {most}, then {Nodes()}; objects {objects} to {Objects()}; memory {mb:F0} to {Megabytes():F0} MB");
        }
        if (Tool.Mode == ToolMode.Repair) foreach (int x in _drive.Tap(Key.Q)) yield return x;
    }

    // ---- Ore: as it was, with lightning -------------------------------------------------------------

    IEnumerable<int> Ore()
    {
        GD.Print("-- ore: the yield as it was, lightning to the ground --");
        DepositKind kind = DepositFinder.Kinds.First();
        string ore = Tool.OreOf(kind).Id;
        // Set down by the nearest deposit (the walk there is PlayerTest's to prove): this is about the tool.
        if (_play.NextDeposit(kind, 2000f) is Vector3 deposit)
        {
            Vector3 by = _game.Zone.GroundAt(deposit.X + 2f, deposit.Z + 2f);
            _game.Survivor.PlaceOutside((by + Vector3.Up * 0.3f).ToNumerics());
            foreach (int x in _drive.Wait(90)) yield return x;
        }
        int before = _game.Pack[ore], cells = Tool.OreCells, rock = Tool.TellOre, bolts = Fx.LightningBolts, arcs = Fx.LightningArcs;
        foreach (int x in _play.Gather(kind, before + 8, 60f)) yield return x;
        int dug = Tool.OreCells - cells, got = _game.Pack[ore] - before;
        float per = _game.Zone.Data.Rules.Materials.OrePerCell;
        // (Ore trickles in as cells chip, so a cell's share can come before the cell goes.)
        Check(got > 0 && got >= dug * per + (Tool.TellOre - rock) && got <= (dug + 4) * per + (Tool.TellOre - rock) + 1,
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
