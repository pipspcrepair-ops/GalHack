using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Slice.Surface;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Every body is drawn with real bones (the engine told "Basis ... must be normalized" thousands of times a run, and warned of
/// swarms placed outside a physics tick). On fresh worlds every run (nothing pinned), the world's armed people aiming as a body
/// far off is posed, then a settlement's people watched from among them and from 60, 200 and 500 m off, with the world's swarms
/// put down round the sleeper: every piece of every person as posed and drawn is finite with no axis gone to nothing, no swarm's
/// or school's multimesh is physics-interpolated, and the engine logs none of those errors.
///
/// -- --rounds=N worlds (default 2); --seeds=a:b:... rolls a failing run's worlds again (an A/B; never pinned).
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/BonesTest.tscn
/// </summary>
public partial class BonesTest : Node
{
    const int WatchdogMinutes = 20;
    IEnumerator<int> _run;
    int _failures, _passes, _round;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly System.Random _rng = new();
    readonly ulong[] _seeds = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--seeds=")) is { } s ? s[8..].Split(':').Select(ulong.Parse).ToArray() : Array.Empty<ulong>();
    Game.Game _game;
    Driver _drive;
    /// <summary>Bad poses of all looked at, interpolated multimeshes of all looked at, and the first few told.</summary>
    int _people, _posesSeen, _swarms, _flocksSeen, _aims, _offAim;
    readonly List<string> _told = new();

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        BonesWatch.Install();
        ProcessPriority = 100; // (after the towns' pose pass)
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
        SetProcess(false);
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    /// <summary>Every frame, once drawn: what is drawn of everyone and every swarm.</summary>
    public override void _Process(double delta)
    {
        if (_game?.Places == null || _game.Zone?.Wildlife?.Sim == null) return;
        foreach (Town town in _game.Places.Towns)
            foreach (Town.Person p in town.People)
            {
                if (p.View is not { Visible: true, Drawn: { } pose }) continue;
                _posesSeen++;
                for (int i = 0; i < pose.Length; i++)
                    if (!Sound(pose[i])) { _people++; Tell($"{p.Role} {town.People.IndexOf(p)} piece {i}: {pose[i]}"); break; }
            }
        if (++_frames % 30 == 0) Flocks(_game);
    }

    int _frames;

    /// <summary>
    /// The swarms' and fish's multimeshes: placed every drawn frame, so never physics-interpolated (the engine warns of each
    /// placement outside a physics tick, windowed; headless it is quiet, so it is looked at here).
    /// </summary>
    void Flocks(Node n)
    {
        if (n is MultiMeshInstance3D m && n.GetParent() is CreatureView or Schools)
        {
            _flocksSeen++;
            if (m.IsPhysicsInterpolated()) { _swarms++; Tell($"{n.GetParent().Name}'s multimesh is physics-interpolated"); }
        }
        foreach (Node c in n.GetChildren()) Flocks(c);
    }

    /// <summary>A piece a bone can take: finite, and none of its axes gone to nothing.</summary>
    static bool Sound(in Transform3D t) =>
        Geo.Finite(t) && t.Basis.X.LengthSquared() > 1e-12f && t.Basis.Y.LengthSquared() > 1e-12f && t.Basis.Z.LengthSquared() > 1e-12f;

    void Tell(string s)
    {
        if (_told.Count < 6) _told.Add(s);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    IEnumerable<int> Scenario()
    {
        int rounds = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--rounds=")) is { } r ? int.Parse(r[9..]) : 2;
        for (int i = 0; i < rounds; i++)
            foreach (int x in Round()) yield return x;
        Check(_posesSeen > 0 && _people == 0, $"every person's pose as drawn is a sound one ({_people} bad of {_posesSeen} drawn){(_told.Count > 0 ? "; e.g. " + string.Join("; ", _told) : "")}");
        Check(_aims > 0 && _offAim == 0, $"a gun held on a still mark is on it 2 s on, posed however seldom ({_offAim} of {_aims} off)");
        Check(_swarms == 0, $"no swarm's or school's multimesh is physics-interpolated ({_swarms} of {_flocksSeen} looked at)");
        Check(BonesWatch.Bases == 0, $"the engine was given no bone basis it can't turn into a rotation ({BonesWatch.Bases})");
        Check(BonesWatch.Infinite == 0, $"the engine was given nothing non-finite ({BonesWatch.Infinite})");
        Check(BonesWatch.Instances == 0, $"no multimesh placed outside a physics tick while interpolated ({BonesWatch.Instances})");
    }

    IEnumerable<int> Round()
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
        HeldAim(_game.Zone.Wildlife.Life);
        foreach (int x in Settlement(family)) yield return x;
        _game.QueueFree();
        _game = null;
        yield return 0;
    }

    /// <summary>
    /// A guard with each kind of gun raised, posed as a body far off is (a tenth of a second apart) and close to (a sixtieth), at a
    /// mark 20 m off in front that moves every 2 s: every piece sound every pose, and the gun on the mark (within 15°) by the time
    /// it moves on. (A swing stepped a tenth of a second at a time overshot further each step: the arms spun, then went to NaN.)
    /// </summary>
    void HeldAim(WorldLife life)
    {
        foreach (string prop in new[] { "rifle", "pistol", "bow" })
        foreach (float dt in new[] { 0.1f, 1f / 60f })
        {
            Species s = life.People.Person((ulong)_rng.NextInt64(), PersonRole.Guard, Occupancy.Friendly);
            s.Figure.Prop = prop; // (STAGING: whatever this world's era arms them with)
            var rig = new HumanRig(s);
            Vector3 mark = default;
            for (int k = 0; k * dt < 40f; k++)
            {
                float t = k * dt;
                bool moves = k % (int)MathF.Round(2f / dt) == 0;
                if (moves && k > 0)
                {
                    _aims++;
                    float off = rig.AimError(mark);
                    if (!(off < 15f)) { _offAim++; Tell($"a guard's {prop}, posed {dt:F3} s apart, {off:F0}° off its mark 2 s on, at {t:F0} s"); }
                }
                if (moves) mark = new Basis(Vector3.Up, _rng.NextSingle() * 2f - 1f) * Vector3.Forward * 20f + Vector3.Up * 1.2f;
                rig.Drive(new Gesture { Feet = Vector3.Zero, Clock = t, Aim = 1f, AimAt = mark });
                _posesSeen++;
                for (int i = 0; i < rig.Pose.Length; i++)
                    if (!Sound(rig.Pose[i])) { _people++; Tell($"a guard with a {prop} aiming, posed {dt:F3} s apart, at {t:F1} s, piece {i}: {rig.Pose[i]}"); break; }
            }
        }
    }

    /// <summary>The settlement's people from among them, then from further and further off; swarms put down round the sleeper at each.</summary>
    IEnumerable<int> Settlement(string family)
    {
        var site = _game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == family && s.Instance?.Townsfolk != null);
        if (site == null) { GD.Print($"  (no {family} with people could be seated on this world)"); yield break; }
        PoiGenerator.Gen.Int3 c0 = site.Instance.Townsfolk.Spots[0].Cell;
        Vector3 camp = _game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X + c0.X + 0.5f, site.Origin.Y + c0.Y, site.Origin.Z + c0.Z + 0.5f), _game.Survivor.WorldFeet);
        _game.Survivor.PlaceOutside((camp + Vector3.Up * 0.2f).ToNumerics());
        for (int i = 0; i < 3600 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
        Town town = _game.Places.PlaceOf(site)?.Memory.Town;
        if (town == null) { Check(false, $"the {family} stands with its people"); yield break; }
        town.Memory.Mood = Mood.Peaceful; // (people about their day, nobody minding you)
        for (int i = 0; i < 900 && town.People.Any(p => p.Looks == null || !p.Dead && p.View == null); i++) yield return 0;
        GD.Print($"  ({town.People.Count} people)");
        Vector3 home = town.World(town.SpotAt(0));
        float away = _rng.NextSingle() * MathF.Tau;
        foreach (float far in new[] { 0f, 60f, 200f, 500f })
        {
            Vector3 feet = _game.Zone.GroundAt(home.X + MathF.Sin(away) * far, home.Z + MathF.Cos(away) * far);
            if (far > 0f) _game.Survivor.PlaceOutside((feet + Vector3.Up * 0.3f).ToNumerics());
            for (int i = 0; i < 60; i++) { _drive.Face(home + Vector3.Up); yield return 0; }
            Swarms();
            for (int i = 0; i < 480; i++) { _drive.Face(home + Vector3.Up); yield return 0; }
        }
    }

    /// <summary>A few of the world's swarms, put down round the sleeper.</summary>
    void Swarms()
    {
        WildlifePatch patch = _game.Zone.Wildlife;
        var kinds = patch.Life?.Fauna.Species.Where(x => x.Rig == RigKind.Swarm).ToList();
        if (kinds == null || kinds.Count == 0) { GD.Print("  (no swarm on this world)"); return; }
        Vector3 feet = _game.Survivor.WorldFeet;
        foreach (Species s in kinds.Take(3))
        {
            float a = _rng.NextSingle() * MathF.Tau, r = 8f + 20f * _rng.NextSingle();
            Vector3 at = _game.Zone.GroundAt(feet.X + MathF.Sin(a) * r, feet.Z + MathF.Cos(a) * r) + Vector3.Up * (s.Flies ? 3f : 0.2f);
            patch.Sim.Spawn(s, patch.PatchLocal(at));
        }
    }
}

/// <summary>Listens to the engine's error log for bones and instances given what they can't take.</summary>
public partial class BonesWatch : Logger
{
    static BonesWatch _on;
    static int _bases, _infinite, _instances;

    /// <summary>Since <see cref="Install"/>: bases a bone couldn't turn into a rotation, non-finite values refused, and multimesh instance transforms refused.</summary>
    public static int Bases => Volatile.Read(ref _bases);
    public static int Infinite => Volatile.Read(ref _infinite);
    public static int Instances => Volatile.Read(ref _instances);

    public static void Install()
    {
        if (_on != null) return;
        _on = new BonesWatch();
        OS.AddLogger(_on);
    }

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        string all = $"{function} {code} {rationale}";
        if (all.Contains("must be normalized in order to be casted to a Quaternion")) Interlocked.Increment(ref _bases);
        if (all.Contains("is_finite()")) Interlocked.Increment(ref _infinite);
        if (all.Contains("multimesh_instance_set_transform") || all.Contains("MultiMesh interpolation is being triggered from outside physics process")) Interlocked.Increment(ref _instances);
    }
}
