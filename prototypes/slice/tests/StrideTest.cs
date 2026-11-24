using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Strides follow the ground (owner, 22 Nov: "sometimes their legs flick between frames ... even worse when they get stuck on
/// something, or bump against someone or something, their legs go a million miles an hour"). On fresh worlds every run (nothing
/// pinned), the world's own walkers (its beasts and its people) in a walled pen in the sim, and a settlement's people as drawn:
/// some held where they stand as by a wall while they mean to walk on, some bumped into by another body, some stopped and set off
/// again, many hurried to a run. Watched every tick: no foot or knee pops about the body from one frame to the next (fast is fine, sudden is not), a planted foot
/// never slides over the ground (the stride keeps to the body's real speed), the stride rate never goes over a gait's natural
/// cadence, and a body held still stands or shuffles, its legs never walking on.
///
/// -- --rounds=N worlds (default 2); --seeds=a:b:... rolls a failing run's worlds again (an A/B; never pinned); --shots=dir
/// (windowed) saves a held walker and a bumped pair over their first second; --fps=N caps the frame rate (default 60) the posing is timed at.
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/StrideTest.tscn
/// </summary>
public partial class StrideTest : Node
{
    const int WatchdogMinutes = 20;
    const float Dt = 1f / 60f;
    IEnumerator<int> _run;
    int _failures, _passes, _round;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly System.Random _rng = new();
    readonly ulong[] _seeds = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--seeds=")) is { } s ? s[8..].Split(':').Select(ulong.Parse).ToArray() : Array.Empty<ulong>();
    readonly string _shots = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--shots=")) is { } d ? d[8..] : null;
    Game.Game _game;
    Driver _drive;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        ProcessPhysicsPriority = 100; // (after the towns' ticks: a held body is put back after its step)
        ProcessPriority = 100; // (and after their pose pass, to time it)
        if (_shots != null) Directory.CreateDirectory(_shots);
        _run = Scenario().GetEnumerator();
    }

    /// <summary>The town whose posing is timed, frame by frame (after its pose pass), and how many bodies it had built at the last frame.</summary>
    Town _timed;
    int _built;
    readonly List<float> _poses = new();
    /// <summary>Frames at a screen's rate (--fps=N, default 60: the slowest screen, a pose a frame), so posing is timed as a frame has it.</summary>
    readonly int _fps = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--fps=")) is { } f ? int.Parse(f[6..]) : 60;

    public override void _Process(double delta)
    {
        if (_timed == null) return;
        Engine.MaxFps = _fps; // (over the game's own frame pacer's cap)
        // (A frame a body was built in times its building, not its posing: left out.)
        int built = _timed.People.Count(p => p.View != null);
        if (built == _built) _poses.Add(_timed.LastPoseMs);
        _built = built;
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
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    // ---- What is measured ------------------------------------------------------------------------

    /// <summary>Everything watched of one kind of walker, over the run.</summary>
    sealed class Tally
    {
        public int Samples, Jumps, Windows, Slides, Spikes, Held, HeldWalking, Lunges;
        public float WorstJump, WorstSlide, WorstCadence, WorstHeld;
        /// <summary>The first few of each kind of fault, told.</summary>
        public readonly Dictionary<string, List<string>> Notes = new();
        public void Note(string kind, string s) { if (!Notes.TryGetValue(kind, out var l)) Notes[kind] = l = new List<string>(); if (l.Count < 3) l.Add(s); }
        public string Told(string kind) => Notes.TryGetValue(kind, out var l) ? "; e.g. " + string.Join("; ", l) : "";
        /// <summary>What they were doing at each jump and slide, counted.</summary>
        public readonly Dictionary<string, int> JumpWhy = new(), SlideWhy = new();
        public void Why(Dictionary<string, int> d, string s) => d[s] = d.GetValueOrDefault(s) + 1;
        public static string Top(Dictionary<string, int> d) => string.Join(", ", d.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Value} {k.Key}"));
    }

    /// <summary>
    /// One walker, sample by sample (a tick apart): its feet and knees about its body (a jump is more than a stride can carry them
    /// in a frame), its planted feet over the ground (a slide is the stride out of step with the ground),
    /// and over each second its steps against how far it really went.
    /// </summary>
    sealed class Gait
    {
        public readonly string Who;
        readonly int _legs;
        readonly float _legLen, _jump, _cap;
        readonly bool _human;
        Vector3[] _was = Array.Empty<Vector3>(), _plantedWas = Array.Empty<Vector3>();
        bool[] _plantedThen = Array.Empty<bool>();
        /// <summary>Each piece's move the frame before last and last frame, m (net of the body's own).</summary>
        (float Before, float Then)[] _moves = Array.Empty<(float, float)>();
        const float Pop = 0.5f;
        bool _has;
        float _win, _slide, _path, _yaw, _turned, _movedThen, _turnedThen, _lunge;
        /// <summary>How far apart fore and aft (of a leg) a stance is at most, and for how long a body standing may have them further, s.</summary>
        public const float Lunge = 0.35f, LungeFor = 0.6f;
        int _heldFor;
        int _slideN, _steps0;
        Vector3 _at0, _last;
        bool _wanted = true;

        public Gait(string who, int legs, float legLen, bool human)
        {
            (Who, _legs, _legLen, _human) = (who, Math.Max(1, legs), legLen, human);
            // (A stride's swing carries a foot about the body at under three times a person's run: 0.15 m a frame, more for a long leg.)
            _jump = human ? 0.15f : 0.15f + 0.08f * legLen;
            // A gait's natural cadence: a person's 2 strides a second (240 steps a minute, a sprint), a child's faster by its shorter
            // legs; a beast's step of a leg 5 a second.
            _cap = human ? 2f * MathF.Sqrt(0.9f / MathF.Max(0.3f, legLen)) : 5f;
        }

        public void Cut() => _has = false;

        /// <summary>
        /// One tick: the drawn feet and knees and body (world), the rig's own feet (world) and which are planted, the root and heading
        /// the rig posed at, its steps so far, and whether it means to walk (a held body that does should stand, not walk on).
        /// </summary>
        public void Sample(Tally t, Vector3[] drawn, Vector3 body, Vector3[] feet, bool[] planted, Vector3 root, float yaw, int steps, bool wants, Func<string> doing)
        {
            if (_was.Length != drawn.Length) { (_was, _moves) = (new Vector3[drawn.Length], new (float, float)[drawn.Length]); _has = false; }
            if (_plantedWas.Length != feet.Length) { _plantedWas = new Vector3[feet.Length]; _plantedThen = new bool[feet.Length]; _has = false; }
            var turn = new Basis(Vector3.Up, yaw).Inverse();
            if (!_has)
            {
                for (int i = 0; i < drawn.Length; i++) (_was[i], _moves[i]) = (turn * (drawn[i] - body), (0f, 0f));
                (_yaw, _turned) = (yaw, 0f);
                for (int i = 0; i < feet.Length; i++) (_plantedWas[i], _plantedThen[i]) = (feet[i], planted[i]);
                (_has, _win, _slide, _slideN, _steps0, _at0, _wanted, _path, _last) = (true, 0f, 0f, 0, steps, root, wants, 0f, root);
                return;
            }
            // (Set down somewhere else in a tick (a fall's landing, a clamber, a test's move): not a stride, begun again.)
            if (WildSim.Flat(root - _last).Length() > 0.5f) { _has = false; return; }
            t.Samples++;
            // Each piece about the body in the body's own frame, over and above what the body's move and turn carry a planted foot.
            // A pop is a frame's move far beyond the frames either side of it (a fast stride or strike is fast every frame of it),
            // or over half a metre at once; judged a frame late, with the frame after it in.
            float worst = 0f, moved = WildSim.Flat(root - _last).Length(), turned = MathF.Abs(Geo.WrapAngle(yaw - _yaw));
            int which = 0;
            for (int i = 0; i < drawn.Length; i++)
            {
                Vector3 rel = turn * (drawn[i] - body);
                float now = MathF.Max(0f, (rel - _was[i]).Length() - moved - turned * rel.Length()), then = _moves[i].Then;
                if (then > _jump && then > 2f * MathF.Max(now, _moves[i].Before) || then > Pop) if (then > worst) (worst, which) = (then, i);
                _moves[i] = (_moves[i].Then, now);
                _was[i] = rel;
            }
            (_yaw, _turned) = (yaw, _turned + turned);
            t.WorstJump = MathF.Max(t.WorstJump, worst);
            if (worst > 0f) { t.Jumps++; t.Why(t.JumpWhy, doing()); t.Note("jump", $"{Who}: {(which % 2 == 0 ? "a knee" : "a foot")} {(which / 2 < planted.Length && planted[which / 2] ? "down" : "lifted")} jumped {worst:F2} m about the body in a frame, the body going {_movedThen:F2} m and turning {_turnedThen:F2} rad ({doing()})"); }
            (_movedThen, _turnedThen) = (moved, turned);
            // A planted foot over the ground: its move (a foot pivoted round a body turning on the spot slides too).
            for (int i = 0; i < feet.Length; i++)
            {
                if (planted[i] && _plantedThen[i])
                {
                    _slide += WildSim.Flat(feet[i] - _plantedWas[i]).Length();
                    _slideN++;
                }
                (_plantedWas[i], _plantedThen[i]) = (feet[i], planted[i]);
            }
            // Standing, both feet down: no further apart fore and aft than a stance (a third of a leg) for more than 0.6 s.
            if (_human && feet.Length == 2 && planted[0] && planted[1] && moved < 0.005f && turned < 0.01f && MathF.Abs((turn * (feet[0] - feet[1])).Z) > Lunge * _legLen)
            {
                if ((_lunge += Dt) > LungeFor && _lunge - Dt <= LungeFor) { t.Lunges++; t.Note("lunge", $"{Who}: stood {LungeFor:F1} s with its feet {MathF.Abs((turn * (feet[0] - feet[1])).Z):F2} m apart fore and aft (a leg {_legLen:F2} m; {doing()})"); }
            }
            else _lunge = 0f;
            _wanted &= wants;
            _path += WildSim.Flat(root - _last).Length();
            _last = root;
            _win += Dt;
            if (_win < 1f) return;
            // Over the second: the stride rate (a leg's steps a second) against the body's real speed over the ground.
            t.Windows++;
            float speed = WildSim.Flat(root - _at0).Length() / _win;
            float cadence = (steps - _steps0) / (float)_legs / _win;
            float slide = _slideN > 0 ? _slide / (_slideN * Dt) : 0f;
            t.WorstCadence = MathF.Max(t.WorstCadence, cadence);
            t.WorstSlide = MathF.Max(t.WorstSlide, slide);
            // (A second's steps are counted whole: one landing more than its rate is the count's, not the legs'.)
            if (cadence > _cap + 1f / _legs / _win) { t.Spikes++; t.Note("spike", $"{Who}: {cadence:F1} steps a leg a second (natural at most {_cap:F1}) at {speed:F2} m/s"); }
            if (_human && slide > 0.6f) { t.Slides++; t.Why(t.SlideWhy, doing()); t.Note("slide", $"{Who}: planted feet slid {slide:F2} m/s over the ground ({speed:F2} m/s, {cadence:F1} strides a second)"); }
            // Held still while it meant to walk: standing, or a shuffle (a step or two a second), never a walk on (a person's walk is a stride a second).
            // (Turning where it stands, or jostled to and fro, is a move of its own, stepped round.)
            // (Its first second held is its stop: a step or two, and its feet brought together.)
            _heldFor = _wanted && _path / _win < 0.15f && _turned < 0.5f ? _heldFor + 1 : 0;
            if (_heldFor > 1)
            {
                t.Held++;
                t.WorstHeld = MathF.Max(t.WorstHeld, cadence);
                if (cadence > 1f) { t.HeldWalking++; t.Note("held", $"{Who}: held still ({speed:F2} m/s, turned {_turned:F2} rad, gone {_path:F2} m) its legs walked on at {cadence:F1} steps a leg a second ({doing()})"); }
            }
            (_win, _slide, _slideN, _steps0, _at0, _wanted, _path, _turned) = (0f, 0f, 0, steps, root, true, 0f, 0f);
        }
    }

    void Report(string what, Tally t, bool people = true)
    {
        GD.Print($"  {what}: {t.Samples} frames, {t.Windows} seconds watched; worst jump {t.WorstJump:F3} m a frame, worst slide {t.WorstSlide:F2} m/s, top cadence {t.WorstCadence:F2}, {t.Held} seconds held, top cadence held {t.WorstHeld:F2}");
        if (t.JumpWhy.Count + t.SlideWhy.Count > 0) GD.Print($"  ({what}: jumps while {Tally.Top(t.JumpWhy)}; slides while {Tally.Top(t.SlideWhy)})");
        Check(t.Samples > 600, $"{what}: walkers watched ({t.Samples} frames)");
        Check(t.Jumps == 0, $"{what}: no foot or knee pops about the body from one frame to the next ({t.Jumps} frames, worst {t.WorstJump:F2} m){t.Told("jump")}");
        Check(t.Slides == 0, $"{what}: planted feet keep to the ground: the stride follows the body's real speed ({t.Slides} seconds sliding, worst {t.WorstSlide:F2} m/s){t.Told("slide")}");
        Check(t.Spikes == 0, $"{what}: the stride rate never goes over a gait's natural cadence ({t.Spikes} seconds over, top {t.WorstCadence:F2}){t.Told("spike")}");
        if (people)
            Check(t.Lunges == 0, $"{what}: a body standing comes to a stance, its feet no further than {Gait.Lunge} of a leg apart fore and aft within {Gait.LungeFor} s ({t.Lunges} times){t.Told("lunge")}");
        Check(t.HeldWalking == 0, $"{what}: held still while meaning to walk on, a body stands or shuffles ({t.HeldWalking} of {t.Held} seconds walking on, top {t.WorstHeld:F2} steps a leg a second){t.Told("held")}");
    }

    /// <summary>A rig's leg pieces (knees and feet: each lower leg's top, each foot) and its body.</summary>
    static (int[] Legs, int Body) Pieces(Rig rig)
    {
        var pieces = rig.Layout.Pieces;
        var legs = new List<int>();
        for (int i = 0; i < pieces.Count; i++)
            if (pieces[i].Name.StartsWith("leg") && (pieces[i].Name.EndsWith("l") || pieces[i].Name.EndsWith("f"))) legs.Add(i);
        return (legs.ToArray(), pieces.FindIndex(p => p.Name == "body"));
    }

    // ---- The rounds ------------------------------------------------------------------------------

    IEnumerable<int> Scenario()
    {
        int rounds = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--rounds=")) is { } r ? int.Parse(r[9..]) : 2;
        var wild = new Tally();
        var folk = new Tally();
        var town = new Tally();
        for (int i = 0; i < rounds; i++)
            foreach (int x in Round(wild, folk, town)) yield return x;
        GD.Print($"  (posing a person walked steadily: {_poseUs / Math.Max(1, _posed):F2} µs a pose over {_posed})");
        Report("people walked steadily", _steady);
        Report("beasts in the sim", wild, false);
        Report("people in the sim", folk);
        Report("a settlement's people as drawn", town);
    }

    IEnumerable<int> Round(Tally wild, Tally folk, Tally drawn)
    {
        const string family = "survivor_town";
        PlanetSurface.Poi.PoiSiting.ForceFamily = family;
        ulong? seed = _seeds.Length > _round ? _seeds[_round] : null;
        _round++;
        WorldChoice world = WorldChoice.Fresh("temperate", seed);
        _game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.42;
        GD.Print($"-- {world} --");
        Steady(_game.Zone.Wildlife.Life, _steady);
        Pen(_game.Zone.Wildlife.Life, _game.Zone.Data.Wildlife, wild, folk);
        foreach (int x in Settlement(family, drawn)) yield return x;
        _game.QueueFree();
        _game = null;
        yield return 0;
    }

    readonly Tally _steady = new();
    /// <summary>A person's pose, timed (the rig alone, nothing drawn), µs all told and how many.</summary>
    double _poseUs;
    int _posed;

    /// <summary>
    /// The world's people (grown, old and young), each set off (at a sprinter's pick-up at most) and walked straight on for 3 s, from an amble to a fighter's
    /// run, then stopped dead: the gait itself, with nothing pushing them.
    /// </summary>
    void Steady(WorldLife life, Tally t)
    {
        var rng = new System.Random(_rng.Next());
        foreach (PersonRole role in new[] { PersonRole.Scavenger, PersonRole.Elder, PersonRole.Child, PersonRole.Raider, PersonRole.Drunk })
        {
            Species s = life.People.Person((ulong)rng.NextInt64(), role, Occupancy.Friendly);
            foreach (float speed in new[] { 0.6f, 1.3f, 2.4f, 3.4f, 5.4f })
            {
                var rig = new HumanRig(s);
                (int[] legs, int body) = Pieces(rig);
                var gait = new Gait($"{role} at {speed:F1} m/s", 2, s.Figure.LegLength, true);
                Vector3 at = Vector3.Zero;
                float clock = 0f;
                for (int k = 0; k < 4 * 60; k++)
                {
                    // (Off at a sprinter's pick-up, 10 m/s²; stopped dead for the last of it, as a held body is.)
                    float v = k < 3 * 60 ? MathF.Min(speed, 10f * Dt * (k + 1)) : 0f;
                    at += Vector3.Forward * v * Dt;
                    clock += Dt;
                    rig.Pace.Note(at, Vector3.Forward, Dt);
                    long from = Stopwatch.GetTimestamp();
                    rig.Drive(new Gesture { Feet = at, Speed = rig.Pace.Speed, Walked = rig.Pace.Walked, Clock = clock, Travels = true });
                    (_poseUs, _posed) = (_poseUs + Stopwatch.GetElapsedTime(from).TotalMilliseconds * 1000.0, _posed + 1);
                    var drawn = new Vector3[legs.Length];
                    for (int i = 0; i < legs.Length; i++) drawn[i] = rig.Pose[legs[i]].Origin;
                    var feet = new Vector3[2];
                    var planted = new bool[2];
                    for (int i = 0; i < 2; i++) (feet[i], planted[i]) = (rig.FootDrawn(i), rig.FootPlanted(i));
                    gait.Sample(t, drawn, rig.Pose[body].Origin, feet, planted, at, 0f, rig.Steps, false, () => $"{role} {v:F1} m/s");
                }
            }
        }
    }

    // ---- The sim: a walled pen ---------------------------------------------------------------------

    /// <summary>
    /// The world's own walkers (beasts that walk, its people) in a pen 12 m across with walls round it, an intruder walking about in
    /// it, for 30 s: every few seconds two held where they stand as they go (put back after each step, wanting on), one walked into
    /// by another, one stopped dead (stunned) and let go again.
    /// </summary>
    void Pen(WorldLife life, WildlifeData data, Tally wild, Tally folk)
    {
        var rng = new System.Random(_rng.Next());
        var sim = new WildSim(data, new Wildlife.Terrain((x, z) => 0f, 40f), new Wildlife.NavGrid(40f, 3f));
        sim.SolidAt = p => p.Y < 3f && (MathF.Abs(p.X) > 6f || MathF.Abs(p.Z) > 6f) && MathF.Abs(p.X) < 9f && MathF.Abs(p.Z) < 9f;
        Vector3 Inside() => new((float)rng.NextDouble() * 9f - 4.5f, 0f, (float)rng.NextDouble() * 9f - 4.5f);
        var beasts = life.Fauna.Species.Where(s => s.Rig == RigKind.Legged && !s.Flies && s.Body.Size < 2.5f).OrderBy(_ => rng.Next()).Take(3).ToList();
        foreach (Species s in beasts) sim.SpawnGroup(s.Resized(MathF.Min(s.Body.Size, 1.2f)), Inside(), 4);
        foreach (PersonRole role in new[] { PersonRole.Scavenger, PersonRole.Scavenger, PersonRole.Raider })
            sim.SpawnGroup(life.People.Person((ulong)rng.NextInt64(), role, role == PersonRole.Raider ? Occupancy.Raider : Occupancy.Friendly), Inside(), 2);
        var you = new DummyTarget { Name = "intruder", Position = Inside() };
        sim.Targets.Add(you);
        var walkers = sim.Creatures.Where(c => c.Rig is HumanRig or LeggedRig).ToList();
        var gaits = walkers.ToDictionary(c => c, c => new Gait($"{c.Species.CommonName}", c.Rig.FootCount, c.Species.Rig == RigKind.Humanoid ? c.Species.Figure.LegLength : c.Species.Body.LegLength, c.Rig is HumanRig));
        var parts = walkers.ToDictionary(c => c, c => Pieces(c.Rig));
        var held = new Dictionary<Creature, (Vector3 At, int Until)>();
        Vector3 goal = Inside();
        GD.Print($"  (the pen: {walkers.Count} walkers, {string.Join(", ", walkers.GroupBy(c => c.Species.CommonName).Select(g => $"{g.Count()} {g.Key}"))})");
        _penMs = 0.0;
        for (int step = 0; step < 30 * 60; step++)
        {
            // The intruder walks from point to point about the pen, and is never hurt (it is there to be fled from and gone for).
            Vector3 to = goal - you.Position;
            if (to.Length() < 0.5f) goal = Inside();
            else (you.Position, you.Forward) = (you.Position + to.Normalized() * 1.4f * Dt, to.Normalized());
            you.Health.Restore();
            var live = walkers.Where(c => !c.Dead && !c.Removed).ToList();
            if (live.Count > 2)
            {
                if (step % 180 == 0)
                    foreach (Creature c in live.Where(c => !held.ContainsKey(c)).OrderBy(_ => rng.Next()).Take(2)) held[c] = (c.Position, step + 120);
                if (step % 120 == 60)
                {
                    // Walked into: another body set down against it, a step ahead of it.
                    Creature a = live[rng.Next(live.Count)], b = live[rng.Next(live.Count)];
                    if (a != b && !held.ContainsKey(b))
                    {
                        b.Position = a.Position + a.Forward * (a.Species.Body.Width * 0.5f + 0.1f);
                        gaits[b].Cut(); // (set down there: a move of the test's, not a stride)
                    }
                }
                if (step % 150 == 75) live[rng.Next(live.Count)].StunFor = 0.5f;
            }
            long from = Stopwatch.GetTimestamp();
            sim.Step(Dt);
            _penMs += Stopwatch.GetElapsedTime(from).TotalMilliseconds;
            foreach (Creature c in walkers)
            {
                if (c.Dead || c.Removed || c.Rig == null) { gaits[c].Cut(); continue; }
                (int[] legs, int body) = parts[c];
                var at = new Vector3[legs.Length];
                for (int i = 0; i < legs.Length; i++) at[i] = c.Rig.Pose[legs[i]].Origin;
                var feet = new Vector3[c.Rig.FootCount];
                var planted = new bool[feet.Length];
                for (int i = 0; i < feet.Length; i++) (feet[i], planted[i]) = (c.Rig.FootDrawn(i), c.Rig.FootPlanted(i));
                bool wants = held.ContainsKey(c) || WildSim.Flat(c.Desired).Length() > 0.3f && c.StunFor <= 0f;
                gaits[c].Sample(c.Rig is HumanRig ? folk : wild, at, body >= 0 ? c.Rig.Pose[body].Origin : c.Position, feet, planted, c.Position, c.Yaw, c.Rig.Steps, wants,
                    () => $"{c.State}/{c.Phase}{(held.ContainsKey(c) ? " held" : "")}{(c.StunFor > 0f ? " stunned" : "")}{(c.Falling ? " falling" : "")} {WildSim.Flat(c.Velocity).Length():F1} m/s");
            }
            // Held: back where it was as it goes (a wall's say, after its step), meaning to go on all the while.
            foreach (Creature c in held.Keys.ToList())
            {
                if (step >= held[c].Until || c.Dead) { held.Remove(c); continue; }
                c.Position = held[c].At;
            }
        }
        GD.Print($"  (the pen's sim: {_penMs / (30 * 60):F3} ms a step for {walkers.Count} walkers)");
    }

    double _penMs;

    // ---- A settlement: its people as drawn -------------------------------------------------------------

    /// <summary>
    /// A settlement's people about their day round the sleeper for 30 s, as drawn, half of them hurried to a run: every second
    /// or so one held where they stand (as against a wall or a crowd) for 2.5 s, one walked into by another, one stopping to let
    /// someone by and setting off again.
    /// </summary>
    IEnumerable<int> Settlement(string family, Tally t)
    {
        var site = _game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == family && s.Instance?.Townsfolk != null);
        if (site == null) { GD.Print($"  (no {family} with people could be seated on this world)"); yield break; }
        if (site.Instance.Mood == Mood.Hostile) site.Instance.Mood = Mood.Wary;
        PoiGenerator.Gen.Int3 c0 = site.Instance.Townsfolk.Spots[0].Cell;
        Vector3 camp = _game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X + c0.X + 0.5f, site.Origin.Y + c0.Y, site.Origin.Z + c0.Z + 0.5f), _game.Survivor.WorldFeet);
        _game.Survivor.PlaceOutside((camp + Vector3.Up * 0.2f).ToNumerics());
        for (int i = 0; i < 3600 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
        Place place = _game.Places.PlaceOf(site);
        Town town = place?.Memory.Town;
        if (town == null) { Check(false, $"the {family} stands with its people"); yield break; }
        if (town.Memory.Mood == Mood.Hostile) town.Memory.Mood = Mood.Wary;
        for (int i = 0; i < 900 && town.People.Any(p => p.Looks == null || !p.Dead && p.View == null); i++) yield return 0;
        bool Up(Town.Person p) => !p.Dead && !p.Gone && p.Looks != null && p.Rig != null && p.View is { Visible: true } && p.Run == null && !p.Asleep;
        bool Going(Town.Person p) => Up(p) && p.Path != null && p.Step < p.Path.Count && !p.Sitting && p.Now > 0.4f;
        // The sleeper where most of them are about.
        var ups = town.People.Where(Up).ToList();
        Town.Person middle = ups.OrderByDescending(p => ups.Count(q => (q.Pos - p.Pos).Length() < 12f)).FirstOrDefault();
        if (middle == null) { Check(false, $"the {family} has people up and about ({town.People.Count})"); yield break; }
        Vector3 stand = middle.Pos;
        for (int k = 0; k < 16; k++)
        {
            float a = k * MathF.Tau / 16f;
            Vector3 at = middle.Pos + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * 6f;
            if (town.CanStand(at) && ups.All(q => (q.Pos - at).Length() > 1.5f)) { stand = at; break; }
        }
        _game.Survivor.PlaceOutside(town.World(stand + Vector3.Up * 0.15f).ToNumerics());
        foreach (Town.Person p in town.People.Where((_, i) => i % 2 == 0)) p.Speed = MathF.Max(p.Speed, 3f); // (hurrying: a run)
        GD.Print($"  ({town.People.Count} people, {ups.Count} up and about, {ups.Count(q => (q.Pos - stand).Length() < 20f)} within 20 m of the sleeper)");
        var gaits = new Dictionary<Town.Person, Gait>();
        var parts = new Dictionary<Town.Person, (int[] Legs, int Body)>();
        var held = new Dictionary<Town.Person, (Vector3 At, int Until)>();
        (_timed, _built) = (town, -1);
        _poses.Clear();
        int heldShot = -1, bumpShot = -1;
        Town.Person shotHeld = null, shotBumped = null;
        for (int tick = 0; tick < 30 * 60; tick++)
        {
            _drive.Face(town.World(middle.Pos + Vector3.Up * 1.2f));
            var going = town.People.Where(p => Going(p) && !held.ContainsKey(p) && (p.Pos - stand).Length() < 25f).ToList();
            if (tick % 75 == 0 && going.Count > 0)
            {
                Town.Person p = going[_rng.Next(going.Count)];
                held[p] = (p.Pos, tick + 150);
                if (shotHeld == null) (shotHeld, heldShot) = (p, tick + 3);
            }
            if (tick % 90 == 30 && going.Count > 1)
            {
                // Walked into: another of them set down a step ahead of one going on their way, the two parted by the crowd's rule.
                Town.Person a = going[_rng.Next(going.Count)];
                Town.Person b = town.People.Where(q => Up(q) && q != a && !held.ContainsKey(q) && !q.Sitting).OrderBy(q => (q.Pos - a.Pos).Length()).FirstOrDefault();
                Vector3 ahead = a.Pos + new Vector3(-MathF.Sin(a.Yaw), 0f, -MathF.Cos(a.Yaw)) * 0.45f;
                if (b != null && town.CanStand(ahead))
                {
                    gaits.GetValueOrDefault(b)?.Cut(); // (set down there: a move of the test's, not a stride)
                    b.Pos = ahead;
                    if (shotBumped == null) (shotBumped, bumpShot) = (a, tick + 4);
                }
            }
            if (tick % 60 == 45 && going.Count > 0) going[_rng.Next(going.Count)].Yield = 0.7f; // (letting someone by: a stop, and off again)
            yield return 0;
            foreach (Town.Person p in held.Keys.ToList())
            {
                if (tick >= held[p].Until || p.Dead || p.Gone) { held.Remove(p); continue; }
                p.Pos = p.Pos with { X = held[p].At.X, Z = held[p].At.Z };
            }
            foreach (Town.Person p in town.People)
            {
                // (Only a body posed since the last tick: one out of the view waits for it, as it stood, and is drawn on from there.)
                if (!Up(p) || p.PoseOwed > Dt * 1.5f || p.Sitting || p.Seated > 0f || p.Down > 0f || p.Slept > 0f || p.Swimming || p.View.Drawn is not { } pose || (town.World(p.Pos) - _game.Survivor.WorldFeet).Length() > 25f)
                {
                    gaits.GetValueOrDefault(p)?.Cut();
                    continue;
                }
                if (!gaits.TryGetValue(p, out Gait g)) (gaits[p], parts[p]) = (g = new Gait($"{p.Job}", 2, p.Figure.LegLength, true), Pieces(p.Rig));
                (int[] legs, int body) = parts[p];
                Transform3D view = p.View.GlobalTransform;
                var at = new Vector3[legs.Length];
                for (int i = 0; i < legs.Length; i++) at[i] = view * pose[legs[i]].Origin;
                var feet = new Vector3[2];
                var planted = new bool[2];
                for (int i = 0; i < 2; i++) (feet[i], planted[i]) = (p.Rig.FootDrawn(i), p.Rig.FootPlanted(i));
                bool wants = held.ContainsKey(p) || p.Path != null && p.Step < p.Path.Count && p.Yield <= 0f;
                g.Sample(t, at, view * pose[body].Origin, feet, planted, p.Pos, p.Yaw, p.Rig.Steps, wants,
                    () => $"{(held.ContainsKey(p) ? "held " : "")}{(p.Engaged ? "fighting " : "")}{(p.Fleeing ? "fleeing " : "")}{(p.Yield > 0f ? "giving way " : "")}{(p.Raised > 0.05f ? "raised " : "")}{(p.Strike > 0f ? "striking " : "")}{(p.StunFor > 0f ? "stunned " : "")}{(p.Act != BodyAct.None ? p.Act + " " : "")}{(p.Crouched > 0.05f ? "crouched " : "")}{p.Now:F1} m/s meant");
            }
            if (_shots != null)
            {
                // A few shots over the first second of each, the camera on them every frame between (as they settle).
                bool onHeld = shotHeld != null && tick - heldShot is >= 0 and <= Shots, onBumped = !onHeld && shotBumped != null && tick - bumpShot is >= 0 and <= Shots;
                if (onHeld || onBumped) Aim(town, onHeld ? shotHeld : shotBumped, tick == heldShot || tick == bumpShot);
                else if (_game.FixedCamera != null && _owed == 0) (_game.FixedCamera, _game.Visor.Visible) = (null, true);
                if (onHeld && (tick - heldShot) % 15 == 0) Capture(Path.Combine(_shots, $"held-{_round}-{tick - heldShot}.png"));
                if (onBumped && (tick - bumpShot) % 15 == 0) Capture(Path.Combine(_shots, $"bumped-{_round}-{tick - bumpShot}.png"));
            }
        }
        _timed = null;
        _poses.Sort();
        if (_poses.Count > 0) GD.Print($"  (posing the settlement's people at {_fps} fps: {_poses.Average():F3} ms a frame on average, {_poses[_poses.Count / 2]:F3} median, {_poses[_poses.Count * 95 / 100]:F3} at the 95th, over {_poses.Count} frames with no body built)");
    }

    /// <summary>Ticks a subject's shots run over (every 15th shot), the camera on them all the while.</summary>
    const int Shots = 45;

    /// <summary>The camera on one of them from off their side as the sequence began (the side clear of walls), at their knees (windowed only).</summary>
    void Aim(Town town, Town.Person who, bool first)
    {
        Vector3 at = town.World(who.Pos + Vector3.Up * 0.7f);
        if (first)
        {
            Vector3 side = new(MathF.Cos(who.Yaw), 0f, -MathF.Sin(who.Yaw));
            _side = town.CanStand(who.Pos + side * 2f) || !town.CanStand(who.Pos - side * 2f) ? side : -side;
        }
        Vector3 eye = at + _side * 2.6f + Vector3.Up * 0.5f;
        (_game.FixedCamera, _game.Visor.Visible) = (new Transform3D(Basis.LookingAt(at - eye, Vector3.Up), eye), false);
    }

    Vector3 _side;

    /// <summary>Shots asked for and not yet saved.</summary>
    int _owed;

    async void Capture(string file)
    {
        _owed++;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print(err == Error.Ok ? $"  (saved {file})" : $"  (could not save {file}: {err})");
        _owed--;
    }
}
