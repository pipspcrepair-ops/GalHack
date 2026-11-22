using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Arms;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Startled people stay where they are and stay there to see (owner, 18 and 20 Nov: bystanders "appeared next to where I blew
/// up the armed man in their panicked poses, and then phased out of existance"; "NPCs appear and then disappear from new
/// positions when startled"). On fresh worlds every run (nothing pinned), a settlement, among people in one of its buildings and
/// out in its streets: the sleeper stands among people, fires a shot beside them, then kills one of them with a rocket. Over the seconds
/// after each, every living person is still there, nobody near the sleeper goes out of sight or comes into it out of nowhere,
/// and nobody (as the sim has them or as drawn) moves further in a tick than a run allows.
///
/// -- --rounds=N: settlements (default 4: open, building, open, building).
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/StartleTest.tscn
/// </summary>
public partial class StartleTest : Node
{
    const int WatchdogMinutes = 25;
    /// <summary>Within this of the sleeper nobody may go out of sight or come into it, m.</summary>
    const float Near = 40f;
    /// <summary>
    /// The most a body may move in one physics tick, m (9 m/s): the fastest run (5.4 m/s, a fighter frightened) with a crowd's
    /// parting on top (3 m/s at most, Town.PartPace); anything faster is a jump.
    /// </summary>
    const float TickMove = 0.15f;
    /// <summary>The same for a drawn body, flat (a pose's own sway and a flinch on top), m.</summary>
    const float DrawnMove = 0.5f;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly System.Random _rng = new();
    /// <summary>--seeds=a:b:...: the rounds' worlds rolled again in order (an A/B of a failing run; never pinned), else fresh.</summary>
    readonly ulong[] _seeds = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--seeds=")) is { } s ? s[8..].Split(':').Select(ulong.Parse).ToArray() : System.Array.Empty<ulong>();
    int _round;
    Game.Game _game;
    Driver _drive;
    double _max;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
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
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    void TopUp() => _game.Suit.Vitals.Health = _max;

    IEnumerable<int> Scenario()
    {
        int rounds = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--rounds=")) is { } r ? int.Parse(r[9..]) : 4;
        int nights = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--nights=")) is { } n ? int.Parse(n[9..]) : 2;
        for (int i = 0; i < rounds; i++)
            foreach (int x in Round(inside: i % 2 == 1, night: false)) yield return x;
        for (int i = 0; i < nights; i++)
            foreach (int x in Round(inside: false, night: true)) yield return x;
        int runs = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--runs=")) is { } u ? int.Parse(u[7..]) : 1;
        for (int i = 0; i < runs; i++)
            foreach (int x in Round(inside: false, night: false, runs: true)) yield return x;
    }

    /// <summary>One settlement: a shot among its people, then one of them killed by a rocket, each watched; or a night there.</summary>
    IEnumerable<int> Round(bool inside, bool night, bool runs = false)
    {
        // (A town with its people in its buildings as well as out in its streets: one under a roof, one in the open.)
        const string family = "survivor_town";
        PlanetSurface.Poi.PoiSiting.ForceFamily = family;
        WorldChoice world = WorldChoice.Fresh("temperate", _seeds.Length > _round ? _seeds[_round] : null);
        _round++;
        _game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.42;
        _max = _game.Suit.Sim.Data.Vitals.Health.Max;
        if (_round == 1) WildFolkRunUpright(_game.Zone.Wildlife.Life);
        GD.Print($"-- a settlement, {(runs ? "the way in, watched" : night ? "a night by their homes" : inside ? "in a building" : "in the open")}, {world} --");
        foreach (int x in Startle(family, inside, night, runs)) yield return x;
        _game.QueueFree();
        _game = null;
        yield return 0;
    }

    IEnumerable<int> Startle(string family, bool inside, bool night, bool runs)
    {
        var site = _game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == family && s.Instance?.Townsfolk != null);
        if (site == null) { GD.Print($"  (no {family} with people could be seated on this world)"); yield break; }
        if (site.Instance.Mood == Mood.Hostile) site.Instance.Mood = Mood.Wary; // (people about their day, not at you)
        Int3 c0 = site.Instance.Townsfolk.Spots[0].Cell;
        Vector3 camp = _game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X + c0.X + 0.5f, site.Origin.Y + c0.Y, site.Origin.Z + c0.Z + 0.5f), _game.Survivor.WorldFeet);
        _game.Survivor.PlaceOutside((camp + Vector3.Up * 0.2f).ToNumerics());
        for (int i = 0; i < 3600 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) { TopUp(); yield return 0; }
        Place place = _game.Places.PlaceOf(site);
        Town town = place?.Memory.Town;
        if (town == null) { Check(false, $"the {family} stands with its people"); yield break; }
        if (town.Memory.Mood == Mood.Hostile) town.Memory.Mood = Mood.Wary;
        for (int i = 0; i < 900 && town.People.Any(p => p.Looks == null || !p.Dead && p.View == null); i++) { TopUp(); yield return 0; }
        if (night) { foreach (int x in Night(town, place)) yield return x; yield break; }
        if (runs) { foreach (int x in Runs(town, place)) yield return x; yield break; }
        bool Out(Town.Person p) => !p.Dead && !p.Gone && p.Looks != null && p.View is { Visible: true };
        // Among a few of them: the one with the most others close by (in their building, under its roof).
        var outs = town.People.Where(Out).ToList();
        Town.Person anchor = outs.Where(p => place.Roofed(town.World(p.Pos + Vector3.Up)) == inside)
            .OrderByDescending(p => outs.Count(q => q != p && (q.Pos - p.Pos).Length() < 10f)).FirstOrDefault()
            ?? outs.OrderByDescending(p => outs.Count(q => q != p && (q.Pos - p.Pos).Length() < 10f)).FirstOrDefault();
        if (anchor == null) { Check(false, $"the {family} has people out ({town.People.Count})"); yield break; }
        Vector3 stand = anchor.Pos;
        for (int k = 0; k < 16; k++)
        {
            float a = k * MathF.Tau / 16f, d = k < 8 ? 5f : 3f;
            Vector3 at = anchor.Pos + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * d;
            if (town.CanStand(at) && outs.All(q => (q.Pos - at).Length() > 1.2f)) { stand = at; break; }
        }
        _game.Survivor.PlaceOutside(town.World(stand + Vector3.Up * 0.15f).ToNumerics());
        for (int i = 0; i < 90; i++) { TopUp(); _drive.Face(town.World(anchor.Pos + Vector3.Up * 1.2f)); yield return 0; }
        GD.Print($"  ({town.People.Count} people, {town.People.Count(Out)} out, {outs.Count(q => (q.Pos - anchor.Pos).Length() < 10f)} within 10 m of where the sleeper stands, {(place.Roofed(_game.Survivor.WorldFeet + Vector3.Up) ? "under a roof" : "in the open")})");

        // A shot into the ground beside them.
        WeaponDef rifle = _game.Arms.Table.Get("line-rifle");
        Vector3 beside = town.World(anchor.Pos + (stand - anchor.Pos) * 0.5f);
        for (int i = 0; i < 20; i++) { TopUp(); _drive.Face(beside); yield return 0; }
        _game.Arms.Fire(rifle, 1);
        foreach (int x in Watch(town, place, "a shot beside them", null)) yield return x;

        // One of them (armed if any are) with others near blown up by a rocket.
        Vector3 you = place.FromWorld(_game.Survivor.WorldFeet);
        Town.Person victim = town.People.Where(p => Out(p) && (p.Pos - you).Length() is > 4f and < 40f)
            .OrderByDescending(p => Weapons.Armed(p.Figure.Prop) ? 1 : 0)
            .ThenByDescending(p => town.People.Count(q => q != p && Out(q) && (q.Pos - p.Pos).Length() < 8f))
            .ThenBy(p => (p.Pos - you).Length()).FirstOrDefault();
        if (victim == null) { GD.Print("  (nobody left out within 40 m to blow up)"); yield break; }
        WeaponDef rocket = WorldFire.Rocket(_game);
        for (int i = 0; i < 20; i++) { TopUp(); _drive.Face(town.World(victim.Pos + Vector3.Up * 1f)); yield return 0; }
        var (fired, blasts, hits) = (_game.Arms.Fired, _game.Arms.Blasts, _game.Arms.PersonHits);
        _game.Arms.Fire(rocket, 1);
        for (int i = 0; i < 180 && !victim.Dead; i++) { TopUp(); yield return 0; }
        if (!victim.Dead)
        {
            // (The rocket went wide: the same kill as its blast's, on them.)
            GD.Print($"  (the rocket missed ({rocket.Id}: {_game.Arms.Fired - fired} fired, {_game.Arms.Blasts - blasts} blasts, {_game.Arms.PersonHits - hits} people hit, they're {(victim.Dead ? "dead" : $"{victim.Health.Current:F0} health")}, {(town.World(victim.Pos) - _game.Survivor.WorldFeet).Length():F1} m off): the blast's kill dealt on them)");
            town.Hit(victim, town.World(victim.Pos + Vector3.Up), (town.World(victim.Pos) - _game.Survivor.WorldFeet).Normalized(), 1f, Slice.Game.Game.EndOf(rocket.Kill));
            town.Struck(victim, 9999f, DamageClass.Explosive, byPlayer: true);
        }
        foreach (int x in Watch(town, place, $"one blown up ({victim.Job}{(Weapons.Armed(victim.Figure.Prop) ? ", armed" : "")}, {(victim.Gone ? "nothing left" : "a body")})", victim)) yield return x;
    }

    /// <summary>
    /// The sleeper inside the way in, watching it, two minutes by day: those whose day is runs out of town go out by it and come
    /// back by it, and never vanish or appear in view (owner: nobody may).
    /// </summary>
    IEnumerable<int> Runs(Town town, Place place)
    {
        Int3 entrance = place.Memory.Inst.Layout.EntranceCell, f = place.Memory.Inst.Layout.EntranceFacing.Vec();
        int gate = Enumerable.Range(0, town.Spots.Count).OrderBy(i => town.Spots[i].Cell.Manhattan(entrance)).First();
        Vector3 at = town.SpotAt(gate), outward = new Vector3(f.X, 0f, f.Z).LengthSquared() > 0.1f ? new Vector3(f.X, 0f, f.Z).Normalized() : Vector3.Right, stand = at;
        for (float d = 6f; d >= 2f; d -= 1f)
            if (town.CanStand(at - outward * d)) { stand = at - outward * d; break; }
        var runners = town.People.Where(p => (p.Looks?.Figure.Routine?.Moves == "leave" || p.Job == Wildlife.PersonRole.Scavenger) && !p.Dead).ToList();
        foreach (Town.Person p in runners) p.Wait = 0f;
        _game.Survivor.PlaceOutside(town.World(stand + Vector3.Up * 0.15f).ToNumerics());
        GD.Print($"  ({runners.Count} of {town.People.Count} go on runs out of town ({runners.Count(p => p.Run != null)} out now; jobs here {string.Join(", ", town.People.GroupBy(p => p.Job).Select(g => $"{g.Count()} {g.Key}"))}); the sleeper {(stand - at).Length():F0} m inside the way in, watching it)");
        foreach (int x in Watch(town, place, "two minutes by the way in", null, 120, at)) yield return x;
        GD.Print($"  ({runners.Count(p => p.Run != null)} out of town at the end)");
    }

    /// <summary>The sleeper by the home spot most of them share, from evening through midnight to morning.</summary>
    IEnumerable<int> Night(Town town, Place place)
    {
        var homes = town.People.Where(p => p.Looks != null && p.Role != "guard").GroupBy(p => p.Home).OrderByDescending(g => g.Count()).ToList();
        if (homes.Count == 0) { Check(false, "the settlement has people with homes"); yield break; }
        Vector3 home = town.SpotAt(homes[0].Key), stand = home;
        for (int k = 0; k < 16; k++)
        {
            float a = k * MathF.Tau / 16f, d = k < 8 ? 4f : 2.5f;
            Vector3 at = home + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * d;
            if (town.CanStand(at)) { stand = at; break; }
        }
        _game.Survivor.PlaceOutside(town.World(stand + Vector3.Up * 0.15f).ToNumerics());
        GD.Print($"  ({town.People.Count} people, {homes[0].Count()} of them at home by the sleeper, {(place.Roofed(town.World(home + Vector3.Up)) ? "under a roof" : "in the open")})");
        foreach (var (time, name) in new[] { (0.82, "evening"), (0.02, "midnight"), (0.3, "morning") })
        {
            _game.Zone.Sky.TimeOfDay = time;
            foreach (int x in Watch(town, place, $"the {name}", null, 40, home)) yield return x;
        }
    }

    /// <summary>
    /// Some seconds after a startle (or into a time of night): every living person still there; none near you undrawn while in
    /// view or shown out of nowhere; everyone within the capsules' reach a body to bump into; no step in the sim or as drawn
    /// longer than a run's.
    /// </summary>
    IEnumerable<int> Watch(Town town, Place place, string what, Town.Person killed, int seconds = 10, Vector3? face = null)
    {
        var people = town.People.Where(p => p.Looks != null).ToList();
        var was = people.ToDictionary(p => p, p => p.Pos);
        var drawn = people.ToDictionary(p => p, p => Drawn(p));
        var placed = people.ToDictionary(p => p, p => p.View?.GlobalPosition ?? default);
        var shown = people.ToDictionary(p => p, p => p.View is { Visible: true });
        var alive = people.Where(p => !p.Dead && !p.Gone).ToList();
        static string Flags(Town.Person p) => $"{(p.Fleeing ? "F" : "")}{(p.Engaged ? "E" : "")}{(p.Asleep ? "S" : "")}{(p.Sitting ? "s" : "")} spot {p.Spot} way {p.Step}/{p.Path?.Count ?? -1} stuck {p.StuckFor:F1} now {p.Now:F1}";
        var before = people.ToDictionary(p => p, Flags);
        int settled = town.Settled, rerouted = town.Rerouted, fled = 0, removed = 0, hid = 0, popped = 0, jumps = 0, drawnJumps = 0, bare = 0, ran = 0, slumped = 0;
        var notes = new List<string>();
        var kinds = new Dictionary<string, int>();
        void Note(string kind, string s) { if ((kinds[kind] = kinds.GetValueOrDefault(kind) + 1) <= 3) notes.Add(s); }
        for (int t = 0; t < seconds * 60; t++)
        {
            TopUp();
            if (face is { } f) _drive.Face(town.World(f + Vector3.Up));
            yield return 0;
            Vector3 you = _game.Survivor.WorldFeet;
            foreach (Town.Person p in people)
            {
                if (p.Dead) { was[p] = p.Pos; continue; }
                if (p.Fleeing) fled++;
                float near = (town.World(p.Pos) - you).Length();
                if (p.Gone) { removed++; Note("gone", $"{p.Job} gone"); people.Remove(p); break; }
                float step = (p.Pos - was[p]).Length();
                // (Flat: a stair's step up is the stair's. A body far off is stepped every few ticks with the time between.)
                step = new Vector2(p.Pos.X - was[p].X, p.Pos.Z - was[p].Z).Length();
                // (Stepped as often as a body just beyond where they stand: at the edge of a ring, it may have been their last step's.)
                if (step > TickMove * Wildlife.PersonView.PoseEvery(near + 1f)) { jumps++; Note("jump", $"{p.Job} {step:F1} m in a tick in the sim, {was[p]} to {p.Pos} ({(p.Fleeing ? "fleeing" : "not fleeing")}{(p.Engaged ? ", fighting" : "")}{(p.Far ? ", far" : "")}{(p.Asleep ? ", asleep" : "")}, spot {p.Spot} refuge {p.Refuge}, way {p.Step}/{p.Path?.Count ?? -1}, {near:F0} m off, was {before[p]})"); }
                was[p] = p.Pos;
                bool vis = p.View is { Visible: true };
                if (near < Near && vis != shown[p] && InView(Drawn(p) ?? town.World(p.Pos + Vector3.Up)))
                {
                    // (A body first posed, dissolving in where they stand, is no pop.)
                    if (vis && drawn[p] != null) { popped++; Note("popped", $"{p.Job} came into sight {near:F0} m off ({(p.Fleeing ? "fleeing" : "not fleeing")}, was {before[p]})"); }
                    else if (!vis) { hid++; Note("hid", $"{p.Job} went out of sight {near:F0} m off ({(p.Fleeing ? "fleeing" : "not fleeing")}{(p.Engaged ? ", fighting" : "")}{(p.Asleep ? ", asleep" : "")}, was {before[p]})"); }
                }
                shown[p] = vis;
                // A body to bump into: everyone within the capsules' reach (one a Husk holds is its hold's).
                if (near < Town.SolidRange - 0.5f && !town.HasSolid(p) && !town.InHold(p))
                {
                    bare++;
                    Note("bare", $"{p.Job} {near:F0} m off with no capsule ({(p.Asleep ? "asleep" : p.Sitting ? "sitting" : "up")}, was {before[p]})");
                }
                before[p] = Flags(p);
                Vector3? d = Drawn(p);
                // (Flat, and in view before and after: a body out of the view isn't posed till it comes back into it. Not in the half
                // second after the sleeper is set down somewhere new: what was last drawn there is posed on in the first frames.)
                if (t >= 30 && vis && d is { } now && drawn[p] is { } then && new Vector2(now.X - then.X, now.Z - then.Z).Length() is var flat && flat > DrawnMove && InView(now) && InView(then))
                {
                    drawnJumps++;
                    Note("drawn", $"{p.Job} drawn {flat:F1} m on in a tick at {t / 60f:F1} s ({near:F0} m off, the view's own move {(p.View.GlobalPosition - placed[p]).Length():F1} m, eased {p.View.Easing().Apart:F2} m, the body piece {(now - p.View.GlobalPosition - (then - placed[p])).Length():F1} m within it, sim {town.World(p.Pos).DistanceTo(now):F1} m off it, act {p.Act} {p.ActAmount:F2}, raised {p.Raised:F2}, crouched {p.Crouched:F2}, down {p.Down:F2}, slept {p.Slept:F2}, seated {p.Seated:F2}, bent {p.Bent:F2}, hurt {p.Hurt:F2}, owed {p.PoseOwed:F2}, was {before[p]})");
                }
                drawn[p] = d;
                placed[p] = p.View?.GlobalPosition ?? default;
                // Fleeing faster than a walk (posed every tick, near): upright as a run, never a cower's slump or lean.
                if (p.Fleeing && p.Now > RunFrom && vis && near < 30f && p is { Crouched: < 0.05f, Bent: < 0.05f, Seated: < 0.05f, Down: < 0.05f, Engaged: false } && p.Rig != null)
                {
                    ran++;
                    if (Slumped(p.Figure, p.Rig, p.Pos.Y, p.Now) is { } how) { slumped++; Note("slump", $"{p.Job} fleeing at {p.Now:F1} m/s: {how}, act {p.Act} {p.ActAmount:F2}"); }
                }
            }
        }
        int dead = alive.Count(p => p.Dead);
        GD.Print($"  {what}: {alive.Count} living before, {dead} died, {fled / 60} person-seconds fleeing, held on their way: {town.Settled - settled} stopped short of a crowded spot, {town.Rerouted - rerouted} found another way{(notes.Count > 0 ? "; " + string.Join("; ", notes) : "")}");
        Check(removed == 0 && alive.All(p => p.Dead || !p.Gone), $"after {what}, every living person is still there ({removed} removed)");
        Check(hid == 0 && popped == 0, $"after {what}, nobody within {Near} m went out of sight or came into it in view ({hid} hidden, {popped} appeared)");
        Check(bare == 0, $"after {what}, everyone within {Town.SolidRange - 0.5f} m has a body to bump into ({bare} person-ticks without)");
        Check(jumps == 0, $"after {what}, nobody moved more than {TickMove} m in a tick in the sim ({jumps} jumps)");
        Check(drawnJumps == 0, $"after {what}, nobody was drawn more than {DrawnMove} m on in a tick ({drawnJumps} jumps)");
        Check(slumped == 0, $"after {what}, everyone fleeing faster than a walk runs upright, never crouched in a cower ({slumped} of {ran} person-ticks low or bent)");
    }

    /// <summary>Faster than this a person is past a walk, m/s.</summary>
    const float RunFrom = 1.6f;

    /// <summary>
    /// How a body is lower or more bent than a run at this speed (owner, 23 Nov: "Why do people crouch when they're running
    /// away?"): its torso tilted past a run's lean and the figure's stoop, or its hips sunk past a run's (a panicked run's wide,
    /// long stride takes them a fifth of the leg down already); null when it runs upright. A cower drops the hips a further
    /// fifth of the leg and bends the back over by more than half a radian.
    /// </summary>
    static string Slumped(Figure f, Rig rig, float feetY, float speed)
    {
        Transform3D body = rig.Pose[rig.Layout.Pieces.FindIndex(q => q.Name == "body")];
        float run = Math.Clamp((speed - 2f) / 1.4f, 0f, 1f);
        float low = f.LegLength * (0.99f - 0.022f - 0.07f * run - 0.3f) - (0.012f + 0.024f * run) * f.Height;
        float most = 0.05f + 0.26f * run + f.Stoop * 0.55f + 0.3f;
        float hip = body.Origin.Y - feetY, tilt = MathF.Acos(Math.Clamp(body.Basis.Y.Normalized().Y, -1f, 1f));
        return hip < low || tilt > most ? $"hips {hip:F2} m up (a run's {low:F2} at least), torso {tilt:F2} rad over (a run's {most:F2} at most)" : null;
    }

    /// <summary>
    /// Wild folk of this world (its own kinds, ruin roamers, settlers) frightened on open ground: one of each band badly hurt
    /// (it breaks off and runs), the band turned on you and the faint-hearted running from it. Each runner's body, as its rig
    /// poses it, is upright as a run whenever it moves faster than a walk.
    /// </summary>
    void WildFolkRunUpright(WorldLife life)
    {
        var kinds = life.Signature.Kinds.Select(k => k.Species).Concat(life.RuinCast.Select(r => r.Item1)).Append(life.Settlers)
            .Where(s => s is { Behaviour: Behaviour.Folk }).Distinct().Take(4).ToList();
        if (kinds.Count == 0) { GD.Print("  (no wild folk on this world)"); return; }
        WildlifeData data = DataFiles.LoadWildlife();
        int ran = 0, slumped = 0;
        var notes = new List<string>();
        foreach (Species s in kinds)
        {
            var sim = new WildSim(data, new Terrain((x, z) => 0f, 120f), new Wildlife.NavGrid(120f, 3f));
            var you = new DummyTarget { Position = new Vector3(0f, 0f, 10f), Health = new Health(1e9f), Forward = Vector3.Back };
            sim.Targets.Add(you);
            List<Creature> band = sim.SpawnGroup(s, Vector3.Zero, 4);
            var rigs = band.ToDictionary(c => c, c => new HumanRig(c.Species));
            foreach (Creature c in band) { c.Nerve = 0.1f; rigs[c].Reset(sim, c); }
            const float dt = 1f / 60f;
            for (int t = 0; t < 60 * 8; t++)
            {
                if (t == 60) { sim.Damage(band[0], band[0].Stats.MaxHealth * 0.75f, you); sim.Damage(band[^1], 1f, you); }
                sim.Step(dt);
                foreach (Creature c in band)
                {
                    if (!c.Alive || c.Removed) continue;
                    rigs[c].Update(sim, c, dt);
                    float speed = WildSim.Flat(c.Velocity).Length();
                    if (t < 120 || speed <= MathF.Max(RunFrom, c.Stats.WalkSpeed * 1.1f)) continue; // (the hits' own snap over first)
                    ran++;
                    if (Slumped(c.Species.Figure, rigs[c], c.Position.Y, speed) is { } how && ++slumped <= 3) notes.Add($"{s.CommonName} at {speed:F1} m/s ({c.State}): {how}, act {c.Act}");
                }
            }
        }
        GD.Print($"  wild folk frightened ({string.Join(", ", kinds.Select(k => k.CommonName))}): {ran} running ticks{(notes.Count > 0 ? "; " + string.Join("; ", notes) : "")}");
        Check(ran > 0 && slumped == 0, $"wild folk running from a fright run upright, never crouched in a cower ({slumped} of {ran} running ticks low or bent)");
    }

    bool InView(Vector3 at)
    {
        Camera3D eye = GetViewport().GetCamera3D();
        return eye != null && !eye.IsPositionBehind(at) && new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size).HasPoint(eye.UnprojectPosition(at));
    }

    /// <summary>Where a body is drawn (world): its torso as drawn, eased placement and all; null without one. (Not the first piece: a carried weapon's may come first, and stowed it stands anywhere.)</summary>
    static Vector3? Drawn(Town.Person p)
    {
        if (p.View == null || !GodotObject.IsInstanceValid(p.View) || p.View.Drawn is not { } pose || p.Rig == null) return null;
        var pieces = p.Rig.Layout.Pieces;
        for (int i = 0; i < pieces.Count && i < pose.Length; i++)
            if (pieces[i].Name == "body") return p.View.GlobalTransform * pose[i].Origin;
        return null;
    }
}
