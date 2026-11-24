using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Arms;
using GalHack.Physics.Engine;
using Godot;
using Slice.Arms;
using Slice.Game;
using Slice.Surface;
using Wildlife;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// Shots you can see and hear (owner, 15 Nov), on a fresh world every run, in numbers:
///
/// - Rows: every shot a hand weapon fires takes a quarter of a second or more over 30 m; vessel
///   guns are slower than real (about 600 m/s small craft, 250 to 350 capital); gunshots carry
///   loud and far; a bow twangs rather than bangs; vomiting is somewhat loud.
/// - A raider with a rifle shoots at the sleeper, who is looking at it: its rounds fly at the row's
///   speed, take as long as that says over 30 m, are drawn (body and hot head, a few pixels wide at
///   least) every frame they fly; each one flashes at the muzzle (on screen), kicks the gun in its
///   hands and is heard loud; one going past the sleeper's head cracks; a hit from behind marks the
///   visor's edge.
/// - The sleeper's own hits: a ring round the reticle and a thud heard at any distance, a kill
///   ringing differently; what is hit cries out (a person, an animal, a machine).
/// - An arrow twangs, flies slow, sticks where it lands with a thud, then fades and goes.
/// - A swing whooshes, hit or miss; one that lands thuds.
///
/// --shots=FOLDER (run windowed; --type=arid keeps the view open): a bolt in flight, the muzzle flash, the reticle ring and a kill's.
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/ShotFeelTest.tscn
/// </summary>
public partial class ShotFeelTest : Node
{
    const int WatchdogMinutes = 8;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();
    Game.Game _game;
    string _shots;

    // Every rendered frame: hostile rounds in flight, and whether they were drawn.
    readonly Dictionary<int, Flight> _flights = new();
    /// <summary>The kick since the last round: how far the muzzle went back along the barrel, rose, and went sideways (the most of each, m).</summary>
    float _kickPeak, _riseMost, _sideMost;
    bool _watchFrames;

    sealed class Flight
    {
        public int Slot;
        public WeaponDef Def;
        public N.Vector3 From, Last;
        public float At30 = -1f, Speed, Thinnest = 99f, Far;
        public int Frames, Drawn, Headed;
        public bool Done;
    }

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        _shots = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--shots="))?.Substring(8);
        ProcessPhysicsPriority = 100;
        ProcessPriority = 1000; // after the shots are drawn
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

    /// <summary>Each rendered frame, after the shots are placed: was every hostile round in flight drawn?</summary>
    public override void _Process(double delta)
    {
        if (!_watchFrames || _game?.Arms == null) return;
        ArmsHost arms = _game.Arms;
        Shot[] shots = arms.Pool.Shots;
        Vector3 eye = _game.Camera.GlobalPosition;
        foreach (Flight f in _flights.Values)
        {
            if (f.Done) continue;
            ref Shot s = ref shots[f.Slot];
            if (!s.Live || s.State != ShotState.Flying) { f.Done = true; continue; }
            f.Frames++;
            if (arms.Fx.Showing(f.Slot)) f.Drawn++;
            if (arms.Fx.HeadShowing(f.Slot)) f.Headed++;
            float far = s.Pos.ToGodot().DistanceTo(eye);
            if (far > 1f) f.Thinnest = MathF.Min(f.Thinnest, arms.Fx.WidthOf(f.Slot) / (far * arms.Fx.Pixel));
        }
        foreach (SimArms.Kit k in _kits)
            if (k.Model != null && GodotObject.IsInstanceValid(k.Model.Root) && k.Model.Root.GetParent() is Node3D hold && hold.IsInsideTree())
            {
                // Where the muzzle is against where it would be at rest, in the world: along the barrel, up, and to the side.
                Vector3 moved = k.Model.Muzzle.GlobalPosition - hold.GlobalTransform * k.Model.Muzzle.Position;
                Vector3 barrel = (hold.GlobalBasis * Vector3.Forward).Normalized();
                Vector3 side = barrel.Cross(Vector3.Up).Normalized();
                _kickPeak = MathF.Max(_kickPeak, -moved.Dot(barrel));
                _riseMost = MathF.Max(_riseMost, moved.Dot(side.Cross(barrel)));
                _sideMost = MathF.Max(_sideMost, MathF.Abs(moved.Dot(side)));
            }
    }

    readonly List<SimArms.Kit> _kits = new();

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    void Shoot(string name)
    {
        if (_shots == null) return;
        string file = System.IO.Path.Combine(_shots, name);
        GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print($"  (shot: {file})");
    }

    /// <summary>Seconds a shooter with a clear line may take to fire its first round: its wind-up and its aim.</summary>
    const float FirstShot = 1.5f;

    static readonly string[] Carried = { "rifle", "shotgun", "pistol", "musket", "crossbow", "bow", "laser_rifle", "energy_pistol" };

    IEnumerable<int> Scenario()
    {
        var game = _game = new Game.Game { Interactive = false, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Fresh("temperate")), SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        game.Zone.Weather.Bridge.Calm = true;
        game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        game.Zone.Sky.TimeOfDay = 0.4;
        Rows(game.Zone.Data.Arms, game.Zone.Data.Mounts);
        foreach (int x in OpenGround(game)) yield return x;
        foreach (int x in Shot(game, drive)) yield return x;
        foreach (string prop in new[] { "rifle", "pistol", "bow" }) foreach (int x in Aimed(game, drive, prop)) yield return x;
        if (_shots != null) foreach (int x in Pictures(game, drive)) yield return x;
        foreach (int x in Mounted(game, drive)) yield return x;
        foreach (int x in Hits(game, drive)) yield return x;
        foreach (int x in Arrow(game, drive)) yield return x;
        foreach (int x in Swings(game, drive)) yield return x;
        game.QueueFree();
        yield return 0;
    }

    /// <summary>The rows, offline: speeds you can see, gunshots that carry.</summary>
    void Rows(Armoury table, Armoury mounts)
    {
        GD.Print("-- the rows --");
        var hand = table.All.Where(d => d.Delivery is Delivery.Bolt or Delivery.Pellets or Delivery.Arc).ToList();
        string Speeds(IEnumerable<WeaponDef> rows) => string.Join(", ", rows.Select(d => $"{d.Id} {Fastest(d):F0} m/s, 30 m in {30f / Fastest(d):F2} s"));
        var fast = hand.Where(d => 30f / Fastest(d) < 0.25f).ToList();
        Check(fast.Count == 0, $"every hand weapon's shot takes a quarter of a second or more over 30 m: {Speeds(hand)}{(fast.Count > 0 ? $"; too fast: {string.Join(", ", fast.Select(d => d.Id))}" : "")}");
        var carried = Carried.Select(p => table.OfItem(Weapons.ItemOf(p))).ToList();
        Check(carried.All(d => d != null && hand.Contains(d)), $"people carry those same rows ({string.Join(", ", carried.Select(d => d?.Id))})");
        Check(mounts.All.Count > 0 && mounts.All.All(d => d.Speed is >= 250f and <= 650f) && mounts.All.All(d => GunVoice.Gunshot(d)),
            $"a structure's guns are slower than real and loud: {string.Join(", ", mounts.All.Select(d => $"{d.Id} {d.Speed:F0} m/s"))}");
        var guns = carried.Where(GunVoice.Gunshot).ToList();
        Check(guns.Count >= 6 && guns.All(d => GunSounds.ReportDbAt(d, 30f) >= 0f && GunSounds.ReportDbAt(d, 120f) >= -10f && GunSounds.ReportDbAt(d, 500f) > float.NegativeInfinity),
            $"every gunshot is loud and carries: {string.Join(", ", guns.Select(d => d.Id))} at {GunSounds.ReportDbAt(guns[0], 30f):F1} dB at 30 m, {GunSounds.ReportDbAt(guns[0], 120f):F1} at 120 m, heard to {GunSounds.LoudReach:F0} m");
        var strung = carried.Where(GunVoice.Strung).ToList();
        Check(strung.Count == 2 && strung.All(d => !GunVoice.Gunshot(d) && d.Stick != null), $"a bow and a crossbow twang and stick rather than bang ({string.Join(", ", strung.Select(d => d.Id))})");
        float ring = Slice.Ui.Visor.RingPixels(new Vector2(1920, 1080), false), kill = Slice.Ui.Visor.RingPixels(new Vector2(1920, 1080), true);
        Check(ring <= 10f && kill <= 12f && kill > ring, $"the hit ring is a small ring hugging the reticle, a kill's the same with a thin second ring just outside ({ring:F1} px and {kill:F1} px across the radius at 1080p)");
        Check(WildlifePatch.RetchDb >= 0f, $"vomiting is somewhat loud ({WildlifePatch.RetchDb:+0;-0} dB)");
    }

    /// <summary>The fastest a row's shot flies (a full charge for a charging gun).</summary>
    static float Fastest(WeaponDef d) => d.Speed + (d.Charge != null ? d.Charge.Stages * d.Charge.Speed : 0f);

    /// <summary>Somewhere open under the sky, and the patch's sim settled round it.</summary>
    IEnumerable<int> OpenGround(Game.Game game)
    {
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        Vector3 start = game.Survivor.WorldFeet;
        for (int tries = 0; tries < 24; tries++)
        {
            float turn = tries * 2.4f, far = 150f + tries * 30f;
            Vector3 open = start + new Vector3(MathF.Cos(turn), 0f, MathF.Sin(turn)) * far;
            if (game.Zone.Water.LiquidAt(open.X, open.Z, out _)) continue; // dry land
            game.Survivor.PlaceOutside((game.Zone.GroundAt(open.X, open.Z) + Vector3.Up * 0.3f).ToNumerics());
            for (int i = 0; i < 60; i++) { game.Suit.Vitals.Health = max; yield return 0; }
            if (!game.Sheltered()) break;
        }
        // A long move swaps the patch's sim: wait for the one round you to settle.
        WildSim sim = game.Zone.Wildlife.Sim;
        for (int i = 0, same = 0; i < 60 * 20 && same < 180; i++)
        {
            same = game.Zone.Wildlife.Sim == sim ? same + 1 : 0;
            sim = game.Zone.Wildlife.Sim;
            game.Suit.Vitals.Health = max;
            yield return 0;
        }
    }

    Species Raider(WildlifePatch patch, string prop)
    {
        Species s = patch.Life.People.Person((ulong)_rng.NextInt64(), PersonRole.Raider, Occupancy.Raider);
        s.Figure.Prop = prop;
        return s;
    }

    Vector3 Ground(Vector3 at) => _game.Zone.GroundAt(at.X, at.Z);

    /// <summary>Ground so far from the sleeper with a clear line from the eye to a body standing there (the first of a turn of bearings from the one given; else that one).</summary>
    Vector3 Clear(float far, float bearing)
    {
        for (int k = 0; k < 72; k++)
        {
            float a = bearing + k * MathF.Tau / 24f, reach = far * (1f - k / 24 * 0.25f); // round once, then a little nearer
            if (Open(reach, a, out Vector3 at)) return at;
        }
        return Ground(_game.Survivor.WorldFeet + new Vector3(MathF.Cos(bearing), 0f, MathF.Sin(bearing)) * far);
    }

    /// <summary>
    /// One of them set down in the open so far off, seen clear: the sim puts a body on ground it can
    /// walk, so ground it can't (a steep dune face) would send it off elsewhere; then another bearing.
    /// </summary>
    Creature Stand(Species s, float far, float bearing)
    {
        WildlifePatch patch = _game.Zone.Wildlife;
        Creature c = null;
        for (int tries = 0; tries < 8; tries++)
        {
            Vector3 spot = Clear(far, bearing + tries * 0.8f);
            c = patch.Sim.Spawn(s, patch.PatchLocal(spot));
            if (c.Position.DistanceTo(patch.PatchLocal(spot)) < 5f) break;
            patch.Sim.Kill(c, null);
        }
        c.Nerve = 1f; // (one with the nerve to fight you: hurt, a timid one runs)
        return c;
    }

    /// <summary>
    /// A point so far from a mark, level with it, with nothing solid between (the first going round
    /// from a heading, either way, no more than so far round; else the heading's own).
    /// </summary>
    Vector3 ClearFrom(Vector3 mark, Vector3 heading, float far, float most)
    {
        for (int k = 0; k <= 16; k++)
        {
            float a = (k + 1) / 2 * (k % 2 == 0 ? 1f : -1f) * MathF.Tau / 16f;
            if (MathF.Abs(a) > most) break;
            Vector3 p = mark + heading.Rotated(Vector3.Up, a) * far;
            if (!_game.Arms.Sweep(p.ToNumerics(), mark.ToNumerics(), null, ArmsHost.Hostile, 1f, out GalHack.Arms.ShotHit h) || h.Kind == HitKind.Player) return p;
        }
        return mark + heading * far;
    }

    /// <summary>Dry ground so far off on a bearing, seen clear from the eye low and high.</summary>
    bool Open(float far, float bearing, out Vector3 at)
    {
        Vector3 eye = _game.Survivor.WorldEye();
        at = Ground(_game.Survivor.WorldFeet + new Vector3(MathF.Cos(bearing), 0f, MathF.Sin(bearing)) * far);
        if (_game.Zone.Water.LiquidAt(at.X, at.Z, out _)) return false;
        foreach (float h in new[] { 0.3f, 0.8f, 1.4f })
            if (_game.Arms.Sweep(eye.ToNumerics(), (at + Vector3.Up * h).ToNumerics(), null, 0, 1f, out _)) return false;
        return true;
    }

    /// <summary>A raider shoots at the sleeper, who looks at it.</summary>
    IEnumerable<int> Shot(Game.Game game, Driver drive)
    {
        GD.Print("-- a raider shoots at the sleeper, who is looking at it --");
        WildlifePatch patch = game.Zone.Wildlife;
        WildSim sim = patch.Sim;
        ArmsHost arms = game.Arms;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        Vector3 feet = game.Survivor.WorldFeet;
        // Where it has a clear line to you from its own muzzle (a trunk or a rise between would send it closer first).
        Creature raider = null;
        for (int tries = 0; tries < 6; tries++)
        {
            raider = Stand(Raider(patch, "rifle"), 35f, tries * 1.05f);
            bool clear = false;
            for (int i = 0; i < 40 && !clear; i++) { game.Suit.Vitals.Health = max; yield return 0; clear = Arming.ShootReach(raider, patch.Player) > 0f; }
            if (clear) break;
            sim.Kill(raider, null);
        }
        sim.Damage(raider, 1f, patch.Player);
        Vector3 Head() => patch.WorldOf(raider) + Vector3.Up * 1.5f;
        int reports = arms.Sounds.Played[(int)VoiceKind.Report], flashes = game.Shots.Flashes, misses = arms.NearMisses, fired = 0;
        int onScreen = 0, flashShown = 0, loud = 0, kicked = 0, rounds = 0;
        float back = 0f, rise = 0f, sideways = 0f;
        float nearest = 999f;
        bool kickedThis = false;
        _watchFrames = true;
        // (Only its own rounds: anyone else out here who fires is someone else.)
        int Fired() => arms.People.KitOf(raider)?.Clip.Fired ?? 0;
        for (int i = 0; i < 60 * 30 && Fired() < 6; i++)
        {
            game.Suit.Vitals.Health = max;
            drive.Face(Head());
            SimArms.Kit kit = arms.People.KitOf(raider);
            if (kit != null && !_kits.Contains(kit)) _kits.Add(kit);
            // New hostile rounds from its rifle, and where they've got to.
            Shot[] shots = arms.Pool.Shots;
            for (int k = 0; k < shots.Length; k++)
            {
                ref Shot s = ref shots[k];
                if (!s.Live || s.Owner != ArmsHost.Hostile || s.State != ShotState.Flying || s.Visual) continue;
                if (!_flights.TryGetValue(s.Id, out Flight f))
                {
                    _flights[s.Id] = f = new Flight { Slot = k, Def = s.Def, From = s.Pos, Last = s.Pos };
                    continue;
                }
                float step = N.Vector3.Distance(s.Pos, f.Last) / (float)GetPhysicsProcessDeltaTime();
                f.Speed = MathF.Max(f.Speed, step);
                f.Last = s.Pos;
                f.Far = MathF.Max(f.Far, N.Vector3.Distance(s.Pos, f.From));
                if (f.At30 < 0f && f.Far >= 30f) f.At30 = s.Age;
            }
            if (Fired() > rounds)
            {
                rounds = Fired();
                Vector3 muzzle = kit != null ? arms.People.Muzzle(raider, kit, patch.Origin) : Head();
                if (game.Camera.IsPositionInFrustum(muzzle)) onScreen++;
                if (game.Shots.FlashesShowing > 0) flashShown++;
                if (arms.Sounds.LastLoud != null && arms.Sounds.LastLoud.GlobalPosition.DistanceTo(muzzle) < 0.5f) loud++;
                nearest = MathF.Min(nearest, muzzle.DistanceTo(game.Survivor.WorldEye()));
                _kickPeak = 0f;
                kickedThis = false;
            }
            if (rounds > 0 && _kickPeak > 0.02f && !kickedThis) { kicked++; kickedThis = true; }
            back = MathF.Max(back, _kickPeak);
            rise = MathF.Max(rise, _riseMost);
            sideways = MathF.Max(sideways, _sideMost);
            yield return 0;
        }
        for (int i = 0; i < 60 * 3; i++) { game.Suit.Vitals.Health = max; drive.Face(Head()); yield return 0; }
        _watchFrames = false;
        if (rounds == 0) GD.Print($"  (the raider: {raider.State}{(raider.Dead ? " dead" : "")}, {patch.WorldOf(raider).DistanceTo(game.Survivor.WorldFeet):F0} m off, its gun reaches {Arming.ShootReach(raider, patch.Player):F0} m, kit {arms.People.KitOf(raider)?.Clip.Def.Id ?? "none"}, you attackable {patch.Player.Attackable}, sheltered {game.Sheltered()}, the patch centred {patch.Origin.DistanceTo(game.Survivor.WorldFeet):F0} m off, its sim {(patch.Sim == sim ? "the same" : "swapped")})");
        Check(rounds >= 2, $"it fires at you ({rounds} rounds, from {nearest:F0} m)");
        if (rounds == 0) yield break;
        var flown = _flights.Values.Where(f => f.Def.Id == "line-rifle" && f.Far >= 10f).ToList(); // (not one that met something at the muzzle)
        var timed = flown.Where(f => f.At30 > 0f).ToList();
        WeaponDef rifle = game.Zone.Data.Arms.Get("line-rifle");
        Check(flown.Count > 0 && flown.All(f => MathF.Abs(f.Speed - rifle.Speed) < rifle.Speed * 0.1f), $"its rounds fly at the row's {rifle.Speed:F0} m/s ({string.Join(", ", flown.Take(6).Select(f => $"{f.Speed:F0}"))})");
        // (Over 30 m as timed in flight, and for any that met the ground sooner, at the speed it flew.)
        Check(flown.Count > 0 && timed.All(f => f.At30 >= 0.25f) && flown.All(f => 30f / f.Speed >= 0.25f), $"each takes a quarter of a second or more over 30 m ({string.Join(", ", flown.Take(6).Select(f => f.At30 > 0f ? $"{f.At30:F2} s" : $"{30f / f.Speed:F2} s at its speed"))})");
        Check(flown.Count > 0 && flown.All(f => f.Frames >= (int)(f.Far / rifle.Speed * 60f) - 1 && f.Drawn == f.Frames && f.Headed == f.Frames), $"drawn, with its hot head, every frame it flies ({string.Join(", ", flown.Take(6).Select(f => $"{f.Drawn}/{f.Headed}/{f.Frames}"))} drawn/headed/frames)");
        Check(flown.Count > 0 && flown.All(f => f.Thinnest >= ArmsFx.MinPixels * 0.95f), $"never thinner than {ArmsFx.MinPixels} pixels however far ({(flown.Count > 0 ? flown.Min(f => f.Thinnest) : 0f):F1} px at the thinnest)");
        Check(arms.Sounds.Played[(int)VoiceKind.Report] - reports >= rounds && loud >= rounds, $"every round is a loud report where it was fired ({loud} of {rounds} on the far-carrying players, {GunSounds.ReportDbAt(rifle, nearest):F1} dB at {nearest:F0} m)");
        Check(game.Shots.Flashes - flashes >= rounds && flashShown >= rounds && onScreen >= rounds, $"each flashes at its muzzle, on screen ({flashShown} flashes showing of {rounds}, {onScreen} on screen, sized {Slice.Player.ShotFx.SeenScale(nearest):F1}x for {nearest:F0} m)");
        Check(kicked >= rounds - 1, $"and kicks the rifle in its hands ({kicked} of {rounds} kicks over 2 cm)");
        Check(back > 0.03f && rise > 0f && rise < back * 2f && sideways < 0.005f, $"straight back along the barrel into the body, the muzzle rising a little, never sideways ({back * 100f:F1} cm back, {rise * 100f:F1} cm up, {sideways * 1000f:F1} mm sideways)");
        GD.Print($"  ({arms.NearMisses - misses} went past your head on their own, {arms.PlayerHits} hit you)");

        // One past your head: it cracks where it passes.
        int cracks = arms.Sounds.PlayedShared[(int)CombatSound.Crack];
        misses = arms.NearMisses;
        Vector3 eye = game.Survivor.WorldEye();
        Vector3 from = patch.WorldOf(raider) + Vector3.Up * 1.4f;
        Vector3 side = (eye - from).Cross(Vector3.Up).Normalized(), past = eye + side * 1.5f;
        Vector3 start = ClearFrom(past, WildSim.Flat(from - eye).Normalized(), 12f, MathF.PI); // (the ground between is the world's, not the test's)
        arms.FireFrom(rifle, start, past - start, 0f, raider);
        for (int i = 0; i < 60 && arms.NearMisses == misses; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        Check(arms.NearMisses > misses && arms.Sounds.PlayedShared[(int)CombatSound.Crack] > cracks, $"a round past your head cracks as it goes by ({arms.NearMisses - misses} heard within {ArmsHost.PassRadius} m)");

        // Shot from behind: the visor's edge marks the side.
        sim.Kill(raider, null);
        drive.Face(eye - (from - eye));
        yield return 0;
        int hits = arms.PlayerHits;
        // From behind, at the chest, from close enough that the ground between can't take it.
        Vector3 chest = game.Survivor.WorldFeet + Vector3.Up * 1.1f, away = new Vector3(from.X - eye.X, 0f, from.Z - eye.Z).Normalized();
        Vector3 behind = ClearFrom(chest, away, 6f, 1.1f);
        arms.FireFrom(rifle, behind, chest - behind, 0f, null);
        for (int i = 0; i < 60 && arms.PlayerHits == hits; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        Vector3 local = game.Camera.GlobalBasis.Inverse() * (behind - game.Camera.GlobalPosition);
        Check(arms.PlayerHits > hits && game.Visor.HurtShowing && local.Z > 0f, $"hit from behind, off screen, the visor's edge marks where it came from ({(arms.PlayerHits > hits ? "hit" : "missed")}, edge {(game.Visor.HurtShowing ? "showing" : "not showing")}, from {(local.Z > 0f ? "behind" : "ahead")})");
    }

    /// <summary>A structure's gun (fired as from the pilot's toolbar): its shot is drawn bright, with its hot head, a few pixels wide at least, every frame it flies.</summary>
    IEnumerable<int> Mounted(Game.Game game, Driver drive)
    {
        GD.Print("-- a structure's gun --");
        ArmsHost arms = game.Arms;
        WeaponDef gatling = game.Zone.Data.Mounts.All.First(d => d.Tracer.Body == GalHack.Arms.Body.Tracer);
        Vector3 eye = game.Survivor.WorldEye();
        // Up into open sky: the first way round with nothing in the way for a hundred metres.
        Vector3 dir = Vector3.Up;
        for (int k = 0; k < 12; k++)
        {
            float a = k * MathF.Tau / 12f;
            Vector3 d = (new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * 0.6f + Vector3.Up).Normalized();
            if (!arms.Sweep((eye + d * 3f).ToNumerics(), (eye + d * 100f).ToNumerics(), null, 0, 1f, out _)) { dir = d; break; }
        }
        drive.Face(eye + dir * 20f);
        yield return 0;
        int reports = arms.Sounds.Played[(int)VoiceKind.Report];
        arms.FireMounted(gatling, eye + dir * 3f, dir, Vector3.Zero, null);
        int slot = Array.FindIndex(arms.Pool.Shots, s => s.Live && s.Def == gatling);
        if (slot >= 0) _flights[arms.Pool.Shots[slot].Id] = new Flight { Slot = slot, Def = gatling };
        _watchFrames = true;
        for (int i = 0; i < 60 && slot >= 0 && arms.Pool.Shots[slot].Live && arms.Pool.Shots[slot].Def == gatling; i++) yield return 0;
        _watchFrames = false;
        Flight f = slot >= 0 ? _flights.Values.First(x => x.Def == gatling) : null;
        if (f == null || f.Frames < 10) GD.Print($"  (the gatling's shot met: {arms.Recent()})");
        Check(f != null && f.Frames >= 10 && f.Drawn == f.Frames && f.Headed == f.Frames && f.Thinnest >= ArmsFx.MinPixels * 0.95f && arms.Sounds.Played[(int)VoiceKind.Report] > reports && arms.Sounds.LastLoud != null,
            $"{gatling.Id}: drawn with its hot head every frame ({f?.Drawn}/{f?.Headed}/{f?.Frames}), never thinner than {f?.Thinnest:F1} px, its report loud");
    }

    /// <summary>
    /// Someone with a rifle, a pistol or a bow shooting at you: each round waits until the weapon is
    /// on you, so the barrel (or the arrow) points at you as it goes, a few degrees at most; the
    /// round leaves the muzzle along it; the body is turned to you.
    /// </summary>
    IEnumerable<int> Aimed(Game.Game game, Driver drive, string prop)
    {
        GD.Print($"-- a {prop} aimed at you --");
        WildlifePatch patch = game.Zone.Wildlife;
        ArmsHost arms = game.Arms;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        Creature raider = Stand(Raider(patch, prop), 18f, 2f);
        patch.Sim.Damage(raider, 1f, patch.Player);
        var offs = new List<float>();
        var barrels = new List<float>();
        var facing = new List<float>();
        int fired = 0;
        float wanted = -1f, first = -1f; // (when it first had you in reach on a clear line, and when its first round went)
        int dropped = arms.People.Dropped, inReach = 0;
        var states = new List<string>();
        for (int i = 0; i < 60 * 25 && offs.Count < 3 && !raider.Dead; i++)
        {
            game.Suit.Vitals.Health = max;
            drive.Face(patch.WorldOf(raider) + Vector3.Up * 1.4f);
            yield return 0;
            SimArms.Kit kit = arms.People.KitOf(raider);
            bool reach = Arming.ShootReach(raider, patch.Player) > 0f;
            if (reach) inReach++;
            if (wanted < 0f && reach) wanted = i / 60f;
            string st = $"{raider.State}/{raider.Phase}";
            if (first < 0f && (states.Count == 0 || states[^1] != st) && states.Count < 12) states.Add(st);
            if (first < 0f && kit?.Clip.Fired > 0) first = i / 60f;
            if (kit == null || kit.Clip.Fired <= fired || raider.Rig is not HumanRig hr) continue;
            fired = kit.Clip.Fired;
            if (kit.Target != patch.Player || hr.WeaponLine.LengthSquared() < 0.5f) continue;
            offs.Add(kit.LastOff);
            Vector3 mark = patch.PlayerWorld() + Vector3.Up * 1.15f;
            if (kit.Model != null && GodotObject.IsInstanceValid(kit.Model.Muzzle) && kit.Model.Muzzle.IsInsideTree())
            {
                Vector3 muzzle = kit.Model.Muzzle.GlobalPosition, barrel = -kit.Model.Muzzle.GlobalBasis.Z.Normalized();
                barrels.Add(Mathf.RadToDeg(barrel.AngleTo(mark - muzzle)));
            }
            Vector3 to = WildSim.Flat(patch.Player.Position - raider.Position);
            facing.Add(Mathf.RadToDeg(WildSim.Flat(hr.Facing).AngleTo(to)));
        }
        if (offs.Count == 0)
        {
            SimArms.Kit k0 = arms.People.KitOf(raider);
            GD.Print($"  (no round measured: {raider.State} {raider.Phase}{(raider.Dead ? " dead" : "")}, {WildSim.Flat(patch.Player.Position - raider.Position).Length():F0} m off, {(patch.WorldOf(raider).Y - patch.PlayerWorld().Y):F1} m above you, fired {k0?.Clip.Fired}, target {(k0?.Target == patch.Player ? "you" : k0?.Target == null ? "none" : "else")}, reach {Arming.ShootReach(raider, patch.Player):F0}, line {(raider.Rig is HumanRig r0 ? r0.WeaponLine.Length().ToString("F2") : "no rig")}, aim error {(raider.Rig is HumanRig r1 ? r1.AimError(patch.Player.Position + Vector3.Up * 1.15f).ToString("F1") : "-")}, bursts dropped {arms.People.Dropped} (last held {arms.People.LastHeldOff:F1} off); its band: {Band(raider)})");
        }
        patch.Sim.Kill(raider, null);
        string Degrees(List<float> a) => a.Count == 0 ? "none" : string.Join(", ", a.Select(v => $"{v:F1}"));
        Check(first >= 0f && wanted >= 0f && first - wanted <= FirstShot, $"with you in reach on a clear line, it fires within about a second ({(first < 0f ? "never fired" : $"{first - wanted:F2} s")}; reach first at {wanted:F1} s, in reach {inReach / 60f:F1} s, {arms.People.Dropped - dropped} bursts held off ({arms.People.LastHeldOff:F1} deg), before its first: {string.Join(" > ", states)})");
        Check(offs.Count >= 2 && offs.Max() <= HumanRig.OnMark + 0.5f, $"its {prop} is on you as each round goes: {Degrees(offs)} degrees off the line to you (at most {HumanRig.OnMark})");
        if (prop != "bow") Check(barrels.Count >= 2 && barrels.Max() <= HumanRig.OnMark + 1.5f, $"the round leaves the muzzle along the barrel: {Degrees(barrels)} degrees between the barrel and muzzle-to-you");
        GD.Print($"  (its chest turned {Degrees(facing)} degrees from you as it fired)");
    }

    /// <summary>What a band is about, in a line (tests' notes): its mood, whom it fights and deals with, and what it has seen.</summary>
    string Band(Creature c)
    {
        string Who(ITarget t) => t == null ? "none" : t == _game.Zone.Wildlife.Player ? "you" : t is CreatureTarget { Creature: var o } ? (o.Herd == c.Herd ? "its own" : o.Species.CommonName) : "else";
        return c.Herd?.Party is not { } b ? "none" : $"{c.Herd.Members.Count} strong, {b.Mood}, stance {b.Stance}, turned {b.Turned}, foe {Who(b.Foe)}, outsider {Who(b.Outsider)}, threat {Who(b.Threat)}, stranger {Who(b.Stranger)}, {b.Events} events";
    }

    /// <summary>(--shots) A raider close by on open ground, facing you, shooting at you: its muzzle flash, and one of its rounds coming at you, mid-flight.</summary>
    IEnumerable<int> Pictures(Game.Game game, Driver drive)
    {
        WildlifePatch patch = game.Zone.Wildlife;
        ArmsHost arms = game.Arms;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        Creature raider = Stand(Raider(patch, "rifle"), 12f, 1f);
        patch.Sim.Damage(raider, 1f, patch.Player);
        Vector3 Body() => patch.WorldOf(raider) + Vector3.Up * 1.2f;
        bool OnYou() => raider.Rig is HumanRig hr && hr.AimError(patch.Player.Position + Vector3.Up * 1.15f) < HumanRig.OnMark && arms.People.KitOf(raider)?.Target == patch.Player;
        // The tick after one of its rounds at you, its rifle on you, its flash still showing on screen; then one of its rounds half way to you.
        int fired = 0, flying = -1;
        bool flashed = false, bolt = false;
        for (int i = 0; i < 60 * 30 && !(flashed && bolt); i++)
        {
            game.Suit.Vitals.Health = max;
            drive.Face(Body());
            SimArms.Kit kit = arms.People.KitOf(raider);
            int now = kit?.Clip.Fired ?? 0;
            bool shot = now > fired && OnYou();
            fired = now;
            Vector3 muzzle = kit != null ? arms.People.Muzzle(raider, kit, patch.Origin) : Body();
            yield return 0;
            if (shot && !flashed && game.Shots.FlashesShowing > 0 && game.Camera.IsPositionInFrustum(Body())) { Shoot("muzzle-flash.png"); flashed = true; continue; }
            if (bolt) continue;
            Shot[] shots = arms.Pool.Shots;
            Vector3 eye = game.Survivor.WorldEye();
            for (int k = 0; k < shots.Length; k++)
            {
                if (!shots[k].Live || shots[k].Owner != ArmsHost.Hostile || shots[k].LastHit != raider) continue;
                float gone = shots[k].Pos.ToGodot().DistanceTo(muzzle) / MathF.Max(1f, muzzle.DistanceTo(eye));
                if (gone is > 0.35f and < 0.7f && OnYou()) { Shoot("bolt-in-flight.png"); bolt = true; break; }
            }
        }
        patch.Sim.Kill(raider, null);
    }

    /// <summary>The sleeper's own hits: the reticle's ring, the thud at any distance, the cry of what's hit.</summary>
    IEnumerable<int> Hits(Game.Game game, Driver drive)
    {
        GD.Print("-- the sleeper's hits --");
        WildlifePatch patch = game.Zone.Wildlife;
        WildSim sim = patch.Sim;
        ArmsHost arms = game.Arms;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        WeaponDef rifle = game.Zone.Data.Arms.Get("line-rifle");
        Vector3 feet = game.Survivor.WorldFeet;
        Species animal = patch.Fauna?.Species.Where(s => s.Figure == null && !s.Mechanical && s.Rig is RigKind.Legged or RigKind.Hopper && s.Body.Size is > 0.5f and < 3f).OrderByDescending(s => s.Body.Size).FirstOrDefault();
        var targets = new List<(string What, Creature C)>
        {
            ("a person", sim.Spawn(Raider(patch, "rifle"), patch.PatchLocal(Clear(28f, 0f)))),
            ("a machine", sim.Spawn(Robots.Roll(patch.Life.World, _rng.Next(1000), 1), patch.PatchLocal(Clear(40f, 2.1f)))),
        };
        if (animal != null) targets.Add(("an animal", sim.Spawn(animal, patch.PatchLocal(Clear(40f, 4.2f)))));
        else GD.Print("  (no ground animal of the right size on this world)");
        bool frozen = patch.Frozen;
        patch.Frozen = true;
        for (int i = 0; i < 30; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        bool ringShot = false;
        foreach (var (what, c) in targets)
        {
            // Where it stood once the sim took it (snapped to its walkable ground) the line may be blocked: then elsewhere, until the first thing along it is it.
            for (int tries = 0; tries < 6; tries++)
            {
                Vector3 eye0 = game.Survivor.WorldEye();
                Vector3 mid0 = arms.Where(c, out N.Vector3 m0) ? m0.ToGodot() : patch.WorldOf(c);
                if (arms.Sweep(eye0.ToNumerics(), (eye0 + (mid0 - eye0) * 1.05f).ToNumerics(), null, 0, 1f, out GalHack.Arms.ShotHit first) && first.Thing == c) break;
                c.Position = patch.PatchLocal(Clear(30f, 1.1f * (tries + 1)));
                for (int i = 0; i < 10; i++) { game.Suit.Vitals.Health = max; yield return 0; }
            }
            for (int kill = 0; kill < 2; kill++)
            {
                // At its middle, as the shots find it.
                Vector3 at = arms.Where(c, out N.Vector3 mid) ? mid.ToGodot() : patch.WorldOf(c) + Vector3.Up * c.Species.BodyHeight * 0.55f;
                drive.Face(at);
                yield return 0;
                Vector3 eye = game.Survivor.WorldEye();
                int confirms = arms.Confirms, kills = arms.Kills, cried = patch.Audio.Cried, flesh = arms.Sounds.PlayedShared[(int)CombatSound.Flesh];
                int thuds = arms.Sounds.PlayedShared[(int)(kill == 1 ? CombatSound.Kill : CombatSound.Confirm)];
                c.CriedAt = -9f;
                // (A small thing on the move can still be missed: up to three shots, aimed afresh.)
                for (int shot = 0; shot < 3 && arms.Confirms == confirms; shot++)
                {
                    if (arms.Where(c, out N.Vector3 now)) at = now.ToGodot();
                    // The sleeper's round, met on its way a few metres short (the ground between is the world's, not the test's).
                    Vector3 way = (at - eye).Normalized();
                    arms.Pool.Spawn(rifle, 0, (at - way * 3f).ToNumerics(), (way * rifle.Speed).ToNumerics(), kill == 1 ? 99999f : 1f);
                    for (int i = 0; i < 60 && arms.Confirms == confirms; i++) { game.Suit.Vitals.Health = max; yield return 0; }
                }
                bool rang, killRing;
                rang = game.Visor.RingShowing;
                killRing = game.Visor.RingKill;
                float far = eye.DistanceTo(at);
                if (kill == 0)
                {
                    if (arms.Confirms == confirms) GD.Print($"  (the shot at {what} met: {arms.Recent()})");
                    Check(arms.Confirms > confirms && rang && !killRing && arms.Sounds.PlayedShared[(int)CombatSound.Confirm] > thuds && arms.Sounds.PlayedShared[(int)CombatSound.Flesh] > flesh,
                        $"{what} hit at {far:F0} m: the ring round the reticle, the hit's slap and your thud ({(rang ? "ring" : "no ring")}, thud on a flat player: {arms.Sounds.ConfirmPlayer is AudioStreamPlayer})");
                    Check(patch.Audio.Cried > cried, $"{what} cries out ({c.Species.CommonName})");
                    if (!ringShot) { yield return 0; Shoot("reticle-ring.png"); ringShot = true; }
                }
                else
                {
                    Check(arms.Kills > kills && killRing && arms.Sounds.PlayedShared[(int)CombatSound.Kill] > thuds && c.Dead, $"{what} killed: the kill's own ring and thud ({(killRing ? "kill ring" : rang ? "plain ring" : "no ring")}, {(c.Dead ? "dead" : "alive")})");
                    if (what == "a person") { yield return 0; Shoot("reticle-kill.png"); }
                }
                for (int i = 0; i < 50; i++) { game.Suit.Vitals.Health = max; yield return 0; }
            }
        }
        patch.Frozen = frozen;
    }

    /// <summary>An arrow: a twang, slow, a thunk where it sticks, then it fades and is gone.</summary>
    IEnumerable<int> Arrow(Game.Game game, Driver drive)
    {
        GD.Print("-- an arrow --");
        ArmsHost arms = game.Arms;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        WeaponDef bow = game.Zone.Data.Arms.OfItem(Weapons.ItemOf("bow"));
        Vector3 eye = game.Survivor.WorldEye();
        Vector3 look = game.Survivor.WorldLook();
        Vector3 flat = new Vector3(look.X, 0f, look.Z).Normalized();
        Vector3 land = Ground(eye + flat * 14f);
        drive.Face(land);
        yield return 0;
        int twangs = arms.Sounds.Played[(int)VoiceKind.Report], impacts = arms.Sounds.Played[(int)VoiceKind.Impact], sticks = arms.Sticks;
        AudioStreamPlayer3D loud = arms.Sounds.LastLoud;
        Vector3 from = eye + flat * 1f + Vector3.Up * 0.2f;
        arms.FireFrom(bow, from, land - from, 0f, null);
        int slot = -1;
        Shot[] shots = arms.Pool.Shots;
        for (int k = 0; k < shots.Length; k++) if (shots[k].Live && shots[k].Def == bow) slot = k;
        Check(slot >= 0 && arms.Sounds.Played[(int)VoiceKind.Report] > twangs && arms.Sounds.LastLoud == loud, $"loosed with a twang, not a gunshot (at {bow.Speed:F0} m/s, 30 m in {30f / bow.Speed:F2} s)");
        if (slot < 0) yield break;
        int id = shots[slot].Id;
        for (int i = 0; i < 120 && shots[slot].Id == id && shots[slot].State == ShotState.Flying; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        bool stuck = shots[slot].Id == id && shots[slot].Live && shots[slot].State == ShotState.Stuck;
        Check(stuck && arms.Sticks > sticks && arms.Sounds.Played[(int)VoiceKind.Impact] > impacts, $"it sticks where it lands with a thunk ({(stuck ? $"{shots[slot].Pos.ToGodot().DistanceTo(land):F1} m from the aim" : "not stuck")})");
        if (!stuck) yield break;
        float stay = bow.Stick.Seconds, fadeMid = -1f, fadeLate = -1f;
        bool shown = true;
        for (int i = 0; i < (int)((stay + 1f) * 60f) && shots[slot].Id == id && shots[slot].Live; i++)
        {
            game.Suit.Vitals.Health = max;
            float held = shots[slot].Age - shots[slot].StuckAt;
            if (held > 1f && held < stay - 2f) { fadeMid = MathF.Max(fadeMid, arms.Fx.FadeOf(slot)); shown &= arms.Fx.Showing(slot); }
            if (held > stay - 0.3f) fadeLate = MathF.Max(fadeLate, arms.Fx.FadeOf(slot));
            yield return 0;
        }
        bool gone = shots[slot].Id != id || !shots[slot].Live;
        Check(shown && fadeMid == 0f && fadeLate > 0.6f && gone, $"it stands there {stay:F0} s, then fades and is gone (solid {(shown ? "and drawn" : "but not drawn")} mid-way, {fadeLate:P0} faded at the end, {(gone ? "gone" : "still there")})");
    }

    /// <summary>Swings: a whoosh each, hit or miss; one that lands thuds.</summary>
    IEnumerable<int> Swings(Game.Game game, Driver drive)
    {
        GD.Print("-- swings --");
        ArmsHost arms = game.Arms;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        WeaponDef club = game.Zone.Data.Arms.OfItem(Weapons.ItemOf("club"));
        Vector3 eye = game.Survivor.WorldEye(), look = game.Survivor.WorldLook();
        int whoosh = arms.Sounds.PlayedShared[(int)CombatSound.Whoosh], blow = arms.Sounds.PlayedShared[(int)CombatSound.Blow];
        arms.FireFrom(club, eye + Vector3.Up * 3f, Vector3.Up, 0f, null); // into thin air
        yield return 0;
        Check(arms.Sounds.PlayedShared[(int)CombatSound.Whoosh] > whoosh && arms.Sounds.PlayedShared[(int)CombatSound.Blow] == blow, "a swing that misses whooshes, and nothing thuds");
        whoosh = arms.Sounds.PlayedShared[(int)CombatSound.Whoosh];
        int hits = arms.PlayerHits;
        Vector3 chest = game.Survivor.WorldFeet + Vector3.Up * 1.1f;
        Vector3 from = ClearFrom(chest, new Vector3(look.X, 0f, look.Z).Normalized(), 1.2f, MathF.PI);
        arms.FireFrom(club, from, chest - from, 0f, null);
        game.Suit.Vitals.Health = max;
        yield return 0;
        Check(arms.Sounds.PlayedShared[(int)CombatSound.Whoosh] > whoosh && arms.Sounds.PlayedShared[(int)CombatSound.Blow] > blow && arms.PlayerHits > hits, $"one that lands whooshes and thuds ({club.Id}, {(arms.PlayerHits > hits ? "hit you" : "missed")})");
    }
}
