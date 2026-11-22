using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Godot;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Arms never flip (owner, 22 Nov: "twitchy arms, too, which is more an animation glitch where the arms rotate 180 in some
/// frames"). On fresh worlds every run (nothing pinned), people of every sort with every kind of thing in hand, Husks and walking
/// machines, each taken through a day's worth of moves a tick at a time: walking, running and turning, startled, panicking and
/// cowering, aiming round and switching targets, holding and carrying, sitting down and getting up, bumped and struck, striking,
/// and every gesture. Each arm bone (upper arm, forearm, hand) and what the hand holds is watched in the body's frame, as the rig
/// poses it and as it is drawn (through the pose blend): none turns more than 60° in a frame where a frame beside it turns under 30°.
///
/// -- --rounds=N worlds (default 2); --seeds=a:b:... rolls a run's worlds again (an A/B; never pinned); --shots=dir (windowed)
/// replays the first flip's body and saves it a few frames apart.
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/ArmTest.tscn
/// </summary>
public partial class ArmTest : Node
{
    const int WatchdogMinutes = 10;
    const float Dt = 1f / 60f, Pop = 60f, Calm = 30f;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly System.Random _rng = new();
    static readonly string[] Args = OS.GetCmdlineUserArgs();
    readonly ulong[] _seeds = Args.FirstOrDefault(a => a.StartsWith("--seeds=")) is { } s ? s[8..].Split(':').Select(ulong.Parse).ToArray() : Array.Empty<ulong>();
    readonly string _shots = Args.FirstOrDefault(a => a.StartsWith("--shots=")) is { } d ? d[8..] : null;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
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
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    // ---- What is measured ------------------------------------------------------------------------

    sealed class Tally
    {
        public int Frames, Pops;
        public float Worst;
        public readonly Dictionary<string, int> Why = new();
        public readonly List<string> Notes = new();
        public string Top => string.Join(", ", Why.OrderByDescending(k => k.Value).Take(30).Select(k => $"{k.Value} {k.Key}"));
    }

    /// <summary>The arm pieces of one body, frame to frame: each piece's turn in the body's frame the frame before last and last frame, degrees.</summary>
    sealed class Watch
    {
        readonly Quaternion[] _q;
        readonly Vector3[] _y;
        readonly (float Before, float Then, float Swing)[] _turn;
        readonly bool[] _has;
        public Watch(int n) => (_q, _y, _turn, _has) = (new Quaternion[n], new Vector3[n], new (float, float, float)[n], new bool[n]);

        /// <summary>
        /// One piece this frame (null: not shown); true when last frame was a flip: over <see cref="Pop"/> with a frame beside it under
        /// <see cref="Calm"/>. How far it turned then, and how far of that its length swung (the rest is a roll about it).
        /// </summary>
        public bool Sample(int k, Basis? rel, out float flip, out float swing)
        {
            (flip, swing) = (0f, 0f);
            if (rel is not { } b || b.X.LengthSquared() < 0.09f) { _has[k] = false; return false; }
            Quaternion q = b.GetRotationQuaternion().Normalized();
            Vector3 y = b.Y.Normalized();
            if (!_has[k]) { (_q[k], _y[k], _turn[k], _has[k]) = (q, y, (0f, 0f, 0f), true); return false; }
            float now = Mathf.RadToDeg(2f * MathF.Acos(MathF.Min(1f, MathF.Abs(q.Dot(_q[k]))))), swung = Mathf.RadToDeg(y.AngleTo(_y[k]));
            (float before, float then, swing) = _turn[k];
            (_q[k], _y[k], _turn[k]) = (q, y, (then, now, swung));
            flip = then;
            return then > Pop && MathF.Min(before, now) < Calm;
        }
    }

    /// <summary>The pieces watched: each arm's upper arm, forearm and hand, and what is held.</summary>
    static readonly string[] Names = { "left upper arm", "left forearm", "left hand", "right upper arm", "right forearm", "right hand", "held thing" };

    // ---- The rounds ------------------------------------------------------------------------------

    static readonly string[] Props = { "", "rifle", "pistol", "spear", "bow", "club", "machete", "shotgun", "cane", "umbrella", "hoe", "bottle" };

    readonly Tally _posed = new(), _drawn = new();
    double _poseMs;
    int _poses;
    (Species S, ulong Seed, int Frame)? _firstFlip;

    IEnumerable<int> Scenario()
    {
        int rounds = Args.FirstOrDefault(a => a.StartsWith("--rounds=")) is { } r ? int.Parse(r[9..]) : 2;
        WildlifeData data = DataFiles.LoadWildlife();
        for (int round = 0; round < rounds; round++)
        {
            ulong seed = _seeds.Length > round ? _seeds[round] : (ulong)_rng.NextInt64();
            var w = new WorldConditions { Seed = seed, Alienness = 0.25f, TempC = 15f, Coreward = round % 2 };
            WorldLife life = Bestiary.Roll(data, w);
            var pick = new Rng(seed ^ 0x5eedUL);
            var bodies = new List<Species>();
            PersonRole[] roles = { PersonRole.Scavenger, PersonRole.Guard, PersonRole.Elder, PersonRole.Child, PersonRole.Raider, PersonRole.Drunk, PersonRole.Farmer, PersonRole.Trader };
            for (int i = 0; i < Props.Length; i++)
            {
                Species s0 = life.People.Person(pick.NextU64(), roles[i % roles.Length], i % 3 == 2 ? Occupancy.Raider : Occupancy.Friendly);
                Species s = s0.Copy();
                s.Figure = s0.Figure.Clone();
                s.Figure.Prop = Props[i];
                bodies.Add(s);
            }
            bodies.Add(life.People.Person(pick.NextU64(), PersonRole.Scavenger, Occupancy.Feral, husk: true));
            bodies.Add(life.People.Person(pick.NextU64(), PersonRole.Worker, Occupancy.Feral, husk: true));
            for (int n = 0; n < 4; n++)
                if (Robots.Roll(w, n) is { Rig: RigKind.Humanoid } robot) { bodies.Add(robot); break; }
            GD.Print($"-- world {seed}: {bodies.Count} bodies ({string.Join(", ", bodies.Select(Who))}) --");
            for (int i = 0; i < bodies.Count; i++)
            {
                Run(bodies[i], seed + (ulong)i, null);
                yield return 0;
            }
        }
        GD.Print($"  (posing: {_poseMs * 1000.0 / Math.Max(1, _poses):F1} µs a pose over {_poses})");
        Report("as posed", _posed);
        Report("as drawn", _drawn);
        if (_shots != null && _firstFlip is { } f)
            foreach (int x in Shots(f.S, f.Seed, f.Frame)) yield return x;
    }

    static string Who(Species s) => s.Mechanical ? "machine" : s.Husk ? "husk" : $"{s.Figure.Role}{(s.Figure.Prop != "" ? " with a " + s.Figure.Prop : "")}";

    void Report(string what, Tally t)
    {
        GD.Print($"  {what}: {t.Frames} frames, {t.Pops} flips, worst {t.Worst:F0}° in a frame");
        if (t.Why.Count > 0) GD.Print($"  ({what}: flips while {t.Top})");
        foreach (string n in t.Notes) GD.Print($"    {n}");
        Check(t.Frames > 10000, $"{what}: arms watched ({t.Frames} frames)");
        Check(t.Pops == 0, $"{what}: no arm bone or held thing turns over {Pop:F0}° in a frame beside a frame under {Calm:F0}° ({t.Pops} frames, worst {t.Worst:F0}°)");
    }

    // ---- One body's day ----------------------------------------------------------------------------

    /// <summary>What a body is asked for, tick by tick: the town's own easing turns it into a gesture.</summary>
    sealed class Ask
    {
        public float Speed, Turn, Aim, Sit, Crouch, Work, Reach, Sip, Strike;
        public BodyAct Act;
        public Vector3? AimAt;
        public Vector3 ActAt;
        public int Style;
    }

    /// <summary>
    /// One body through the moves, a beat at a time (each beat's numbers rolled from <paramref name="seed"/>): watched as posed and
    /// as drawn. With <paramref name="frame"/> a frame callback (a replay for the shots), handed each frame's pose instead.
    /// </summary>
    void Run(Species s, ulong seed, Action<int, HumanRig, Transform3D[], Transform3D> frame)
    {
        var rig = new HumanRig(s);
        var r = new Rng(seed);
        var blend = new PoseBlend();
        var pieces = rig.Layout.Pieces;
        int[] bones = { pieces.FindIndex(p => p.Name == "arm0u"), pieces.FindIndex(p => p.Name == "arm0l"), -1, pieces.FindIndex(p => p.Name == "arm1u"), pieces.FindIndex(p => p.Name == "arm1l"), -1, pieces.FindIndex(p => p.Name == "prop") };
        var hands = new int[6];
        for (int i = 0; i < 2; i++)
            for (int k = 0; k < 3; k++) hands[i * 3 + k] = pieces.FindIndex(p => p.Name == HandMake.PieceName((HandShape)k, i));
        var posedW = new Watch(Names.Length);
        var drawnW = new Watch(Names.Length);
        Vector3 at = Vector3.Zero;
        float yaw = 0f, walked = 0f, clock = 0f, speed = 0f, act = 0f, aim = 0f, sit = 0f, crouch = 0f, work = 0f, reach = 0f, sip = 0f;
        BodyAct now = BodyAct.None;
        var shape = new HandShape[2];
        int tick = 0;
        string who = Who(s);
        foreach ((string beat, float seconds, Action<float, Ask> plan) in Beats(r, () => at, () => rig))
        {
            for (float t = 0f; t < seconds; t += Dt, tick++)
            {
                var a = new Ask();
                plan(t, a);
                // Eased as a settlement eases them (Town.PoseBody, Town's acts: a new act waits for the old to ease out).
                speed = Mathf.MoveToward(speed, a.Speed, 10f * Dt);
                yaw += a.Turn * Dt;
                at += new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw)) * speed * Dt;
                walked += speed * Dt;
                clock += Dt;
                if (now != a.Act)
                {
                    act = MathF.Max(0f, act - Dt * 3.5f * (a.Act == BodyAct.Startle ? 3f : 1f));
                    if (act <= 0f) now = a.Act;
                }
                else if (now != BodyAct.None) act = MathF.Min(1f, act + Dt * 3.5f * (now == BodyAct.Startle ? 3f : 1f));
                aim = Mathf.MoveToward(aim, a.Aim, 3f * Dt);
                sit = Mathf.MoveToward(sit, a.Sit, 2.5f * Dt);
                crouch = Mathf.MoveToward(crouch, a.Crouch, 2f * Dt);
                work = Mathf.MoveToward(work, a.Work, 2f * Dt);
                reach = Mathf.MoveToward(reach, a.Reach, 2f * Dt);
                sip = Mathf.MoveToward(sip, a.Sip, 2f * Dt);
                long from = Stopwatch.GetTimestamp();
                rig.Drive(new Gesture
                {
                    Feet = at, Yaw = yaw, Speed = speed, Walked = walked, Travels = true, Sit = sit, Work = work, Aim = aim,
                    AimAt = aim > 0.05f ? a.AimAt : null, Look = 0.6f * MathF.Sin(clock * 0.4f), Crouch = crouch, Reach = reach, Sip = sip, Strike = a.Strike,
                    Act = now, ActAmount = act, ActAt = a.ActAt, Clock = clock, Style = a.Style,
                });
                (_poseMs, _poses) = (_poseMs + Stopwatch.GetElapsedTime(from).TotalMilliseconds, _poses + 1);
                var root = new Transform3D(new Basis(Vector3.Up, yaw), at);
                int key = (int)a.Act | (a.Speed > 0.05f ? 1 : 0) << 8 | (a.Sit > 0f ? 1 : 0) << 9 | (a.Act == BodyAct.Panic ? 1 : 0) << 12 | (a.Aim > 0f ? 1 : 0) << 15;
                Transform3D[] drawn = blend.Frame(rig.Pose, root, key, 0f, s.Figure?.Height ?? 1.8f, clock);
                if (frame != null) { frame(tick, rig, drawn, root); continue; }
                Basis back = root.Basis.Inverse();
                for (int k = 0; k < Names.Length; k++)
                {
                    int arm = k < 3 ? 0 : 1;
                    bool hand = k % 3 == 2 && k < 6;
                    // (A hand that changes shape changes piece: the new one is watched from there.)
                    HandShape sh = rig.HandOf(arm).shape;
                    if (hand && sh != shape[arm]) drawnW.Sample(k, null, out _, out _);
                    if (hand) shape[arm] = sh;
                    int piece = hand ? hands[arm * 3 + (int)sh] : bones[k];
                    Basis? posed = hand ? back * rig.HandOf(arm).at.Basis : piece >= 0 ? back * rig.Pose[piece].Basis : null;
                    Basis? shown = piece >= 0 ? back * drawn[piece].Basis : null;
                    string Doing() => $"{who}, {beat}{(now != BodyAct.None ? $" ({now} {act:F2})" : "")}{(aim > 0.05f ? $" aim {aim:F2}" : "")}{(sit > 0f ? $" sit {sit:F2}" : "")}";
                    if (posedW.Sample(k, posed, out float f1, out float s1)) Flip(_posed, Names[k], beat, Doing(), f1, s1, s, seed, tick);
                    if (drawnW.Sample(k, shown, out float f2, out float s2)) Flip(_drawn, Names[k], beat, Doing(), f2, s2, s, seed, tick);
                    _posed.Worst = MathF.Max(_posed.Worst, f1);
                    _drawn.Worst = MathF.Max(_drawn.Worst, f2);
                }
                _posed.Frames++;
                _drawn.Frames++;
            }
        }
    }

    void Flip(Tally t, string piece, string beat, string doing, float deg, float swing, Species s, ulong seed, int tick)
    {
        t.Pops++;
        string key = $"{piece} {beat}";
        t.Why[key] = t.Why.GetValueOrDefault(key) + 1;
        if (t.Why[key] == 1 && t.Notes.Count < 40) t.Notes.Add($"{piece} turned {deg:F0}° in a frame ({swing:F0}° of its length, the rest a roll): {doing}, tick {tick - 1}");
        _firstFlip ??= (s, seed, tick - 1);
    }

    /// <summary>The beats of a body's day, each its name, its length (s) and what it asks for at each moment of it.</summary>
    static IEnumerable<(string, float, Action<float, Ask>)> Beats(Rng r, Func<Vector3> at, Func<HumanRig> rig)
    {
        Vector3 Around(float dist, float lo, float hi)
        {
            float a = r.Range(0f, MathF.Tau);
            return at() + new Vector3(MathF.Sin(a) * dist, r.Range(lo, hi), MathF.Cos(a) * dist);
        }
        float turn = r.Range(-1f, 1f);
        yield return ("standing", 1f, (t, a) => { });
        yield return ("walking", 2.5f, (t, a) => (a.Speed, a.Turn) = (1.3f, turn));
        float flipEvery = r.Range(0.4f, 0.8f);
        yield return ("running and turning", 3f, (t, a) => (a.Speed, a.Turn) = (4.5f, (int)(t / flipEvery) % 2 == 0 ? 3f : -3f));
        yield return ("turning on the spot", 1.5f, (t, a) => a.Turn = 4f);
        yield return ("startled", 2f, (t, a) => a.Act = BodyAct.Startle);
        float fleeTurn = r.Range(-1.5f, 1.5f);
        yield return ("panicking", 3f, (t, a) => (a.Act, a.Speed, a.Turn) = (BodyAct.Panic, 3.5f, fleeTurn));
        yield return ("cowering", 2.5f, (t, a) => a.Act = BodyAct.Cower);
        yield return ("hands up", 1.5f, (t, a) => a.Act = BodyAct.HandsUp);
        // Aiming at a target going right round the body, high and low, part of it walking.
        float a0 = r.Range(0f, MathF.Tau), up = r.Range(0.3f, 2.6f), spin = r.Range(1.2f, 2f) * (r.Chance(0.5f) ? 1f : -1f);
        yield return ("aiming round", 4.5f, (t, a) =>
        {
            float ang = a0 + spin * t;
            (a.Aim, a.Speed, a.AimAt) = (1f, t < 2f ? 0.8f : 0f, at() + new Vector3(MathF.Sin(ang) * 5f, up + 1.2f * MathF.Sin(t * 0.9f) - 0.6f, MathF.Cos(ang) * 5f));
        });
        Vector3 target = Around(5f, -0.5f, 3f);
        float next = 0f;
        yield return ("aiming, targets switched", 3f, (t, a) =>
        {
            if (t >= next) (target, next) = (Around(r.Range(2f, 9f), -1f, 3.5f), t + 0.35f);
            (a.Aim, a.AimAt) = (1f, target);
        });
        yield return ("holding, walking", 2.5f, (t, a) => (a.Speed, a.Turn) = (1.4f, -turn));
        BodyAct fright = r.Chance(0.5f) ? BodyAct.Panic : BodyAct.Cower;
        Vector3 threat = Around(6f, 0.5f, 1.8f);
        yield return ("raised while frightened", 3.5f, (t, a) => (a.Act, a.Aim, a.AimAt) = (fright, t > 1.2f ? 1f : 0f, threat));
        yield return ("sitting down", 1.5f, (t, a) => a.Sit = 1f);
        yield return ("sat", 1.5f, (t, a) => (a.Sit, a.Act) = (1f, t > 0.5f ? BodyAct.Shrug : BodyAct.None));
        yield return ("getting up", 1.5f, (t, a) => { });
        float bumpNext = 0f;
        yield return ("bumped", 3f, (t, a) =>
        {
            (a.Speed, a.Turn) = (1.2f, 0.5f);
            if (t < bumpNext) return;
            bumpNext = t + r.Range(0.2f, 0.5f);
            HumanRig h = rig();
            h.Struck(h.ChestPoint + new Vector3(r.Range(-0.4f, 0.4f), r.Range(-0.6f, 0.6f), r.Range(-0.4f, 0.4f)), new Vector3(r.Range(-1f, 1f), r.Range(-0.3f, 0.3f), r.Range(-1f, 1f)), r.Range(0f, 1f));
        });
        yield return ("striking", 3.6f, (t, a) =>
        {
            float p = t % 1.2f;
            a.Strike = p < 0.5f ? 0.85f * p / 0.5f : p < 0.6f ? 0.85f + 0.15f * (p - 0.5f) / 0.1f : MathF.Max(0f, 1f - (p - 0.6f) / 0.5f);
        });
        foreach (BodyAct g in new[] { BodyAct.WaveOff, BodyAct.Beckon, BodyAct.Point, BodyAct.Shrug, BodyAct.Warn, BodyAct.Celebrate, BodyAct.Grieve, BodyAct.Fidget, BodyAct.Taunt, BodyAct.Present, BodyAct.Greet, BodyAct.Play, BodyAct.Thanks, BodyAct.Rummage })
        {
            int style = r.Int(0, 2);
            Vector3 to = Around(r.Range(2f, 6f), 0.3f, 2.2f);
            yield return ($"{g}", 1.6f, (t, a) => (a.Act, a.Style, a.ActAt) = (g, style, to));
        }
        float p0 = r.Range(0f, MathF.Tau);
        yield return ("pointing round", 4f, (t, a) => (a.Act, a.ActAt) = (BodyAct.Point, at() + new Vector3(MathF.Sin(p0 + 1.6f * t) * 4f, 1.4f + MathF.Sin(t * 1.3f), MathF.Cos(p0 + 1.6f * t) * 4f)));
        yield return ("crouching", 2f, (t, a) => a.Crouch = 1f);
        yield return ("reaching", 2f, (t, a) => a.Reach = 1f);
        yield return ("working", 2.5f, (t, a) => a.Work = 1f);
        yield return ("drinking", 2f, (t, a) => a.Sip = 1f);
        yield return ("walking on", 2f, (t, a) => (a.Speed, a.Turn) = (1.3f, turn));
    }

    // ---- The shots ---------------------------------------------------------------------------------

    /// <summary>The first flipped body replayed and drawn (windowed only), a frame before its flip, at it and after.</summary>
    IEnumerable<int> Shots(Species s, ulong seed, int flip)
    {
        Directory.CreateDirectory(_shots);
        var poses = new Dictionary<int, (Transform3D[] Pose, Transform3D Root)>();
        HumanRig played = null;
        Run(s, seed, (tick, rig, drawn, root) =>
        {
            played = rig;
            if (tick >= flip - 2 && tick <= flip + 2) poses[tick] = ((Transform3D[])drawn.Clone(), root);
        });
        if (poses.Count == 0) yield break;
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.55f, 0.62f, 0.7f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.6f, 0.65f) } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, 30f, 0f), ShadowEnabled = true });
        var view = new PersonView();
        AddChild(view);
        view.Build(played);
        var cam = new Camera3D { Fov = 40f };
        AddChild(cam);
        var ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(40f, 40f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.4f, 0.28f) } };
        AddChild(ground);
        foreach (int tick in poses.Keys.OrderBy(k => k))
        {
            (Transform3D[] pose, Transform3D root) = poses[tick];
            Array.Copy(pose, played.Pose, pose.Length);
            view.Sync();
            ground.Position = root.Origin with { Y = root.Origin.Y - 0.01f };
            Vector3 chest = root.Origin + Vector3.Up * 1.2f, eye = chest + root.Basis * new Vector3(1.6f, 0.5f, -2.6f);
            cam.GlobalTransform = new Transform3D(Basis.LookingAt(chest - eye, Vector3.Up), eye);
            for (int k = 0; k < 3; k++) yield return 0;
            string file = Path.Combine(_shots, $"flip{tick - flip:+0;-0;0}.png");
            Error err = GetViewport().GetTexture().GetImage().SavePng(file);
            GD.Print(err == Error.Ok ? $"  (saved {file})" : $"  (could not save {file}: {err})");
        }
    }
}
