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
    const int WatchdogMinutes = 20;
    /// <summary>Within this of the sleeper nobody may go out of sight or come into it, m.</summary>
    const float Near = 40f;
    /// <summary>
    /// The most a body may move in one physics tick, m: a run is under 0.1 m a tick; bodies pressed together in a crowd are parted
    /// by up to about two thirds of a metre at once (PersonalSpace, eased as drawn); anything more is a jump.
    /// </summary>
    const float TickMove = 0.75f;
    /// <summary>The same for a drawn body (a pose's own swing and a flinch on top), m.</summary>
    const float DrawnMove = 0.5f;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly System.Random _rng = new();
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
        for (int i = 0; i < rounds; i++)
            foreach (int x in Round(inside: i % 2 == 1)) yield return x;
    }

    /// <summary>One settlement: a shot among its people, then one of them killed by a rocket, each watched.</summary>
    IEnumerable<int> Round(bool inside)
    {
        // (A town with its people in its buildings as well as out in its streets: one under a roof, one in the open.)
        const string family = "survivor_town";
        PlanetSurface.Poi.PoiSiting.ForceFamily = family;
        WorldChoice world = WorldChoice.Fresh("temperate");
        _game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.42;
        _max = _game.Suit.Sim.Data.Vitals.Health.Max;
        GD.Print($"-- a settlement, {(inside ? "in a building" : "in the open")}, {world} --");
        foreach (int x in Startle(family, inside)) yield return x;
        _game.QueueFree();
        _game = null;
        yield return 0;
    }

    IEnumerable<int> Startle(string family, bool inside)
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
        for (int i = 0; i < 900 && town.People.Any(p => p.Looks == null || !p.Dead && p.View == null && !p.Indoors && !p.Away); i++) { TopUp(); yield return 0; }
        bool Out(Town.Person p) => !p.Dead && !p.Gone && !p.Indoors && !p.Away && p.Looks != null && p.View != null;
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
        Town.Person victim = town.People.Where(p => !p.Dead && !p.Gone && !p.Indoors && !p.Away && p.Looks != null && (p.Pos - you).Length() is > 4f and < 40f)
            .OrderByDescending(p => Weapons.Armed(p.Figure.Prop) ? 1 : 0)
            .ThenByDescending(p => town.People.Count(q => q != p && !q.Dead && !q.Indoors && (q.Pos - p.Pos).Length() < 8f))
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
    /// Ten seconds after a startle: every living person still there; none near you hidden or shown out of nowhere; no step
    /// in the sim or as drawn longer than a run's.
    /// </summary>
    IEnumerable<int> Watch(Town town, Place place, string what, Town.Person killed)
    {
        var people = town.People.Where(p => p.Looks != null && !p.Away).ToList();
        var was = people.ToDictionary(p => p, p => p.Pos);
        var drawn = people.ToDictionary(p => p, p => Drawn(p));
        var shown = people.ToDictionary(p => p, p => p.View is { Visible: true });
        var alive = people.Where(p => !p.Dead && !p.Gone).ToList();
        var inside = people.ToDictionary(p => p, p => p.Indoors);
        static string Flags(Town.Person p) => $"{(p.Fleeing ? "F" : "")}{(p.Indoors ? "I" : "")}{(p.Engaged ? "E" : "")}{(p.Asleep ? "S" : "")}{(p.Away ? "A" : "")}{(p.Sitting ? "s" : "")} spot {p.Spot} way {p.Step}/{p.Path?.Count ?? -1} stuck {p.StuckFor:F1} now {p.Now:F1}";
        var before = people.ToDictionary(p => p, Flags);
        int unstuck = town.Unstuck, unstuckNow = unstuck, setBacks = 0, fled = 0, removed = 0, hid = 0, popped = 0, jumps = 0, drawnJumps = 0;
        var notes = new List<string>();
        var kinds = new Dictionary<string, int>();
        void Note(string kind, string s) { if ((kinds[kind] = kinds.GetValueOrDefault(kind) + 1) <= 3) notes.Add(s); }
        for (int t = 0; t < 600; t++)
        {
            TopUp();
            int unstuckBefore = town.Unstuck;
            yield return 0;
            unstuckNow = unstuckBefore;
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
                // (One held on their way two seconds is put back where they last walked well, a few metres at most, eased as drawn:
                // Town.Headway's, counted apart.)
                if (step > TickMove * Wildlife.PersonView.PoseEvery(near) && town.Unstuck > unstuckNow) { setBacks++; unstuckNow = town.Unstuck; }
                else if (step > TickMove * Wildlife.PersonView.PoseEvery(near)) { jumps++; Note("jump", $"{p.Job} {step:F1} m in a tick in the sim, {was[p]} to {p.Pos} ({(p.Fleeing ? "fleeing" : "not fleeing")}{(p.Indoors ? ", indoors" : "")}{(p.Engaged ? ", fighting" : "")}{(p.Far ? ", far" : "")}{(p.Asleep ? ", asleep" : "")}{(p.Away ? ", away" : "")}, spot {p.Spot} refuge {p.Refuge}, way {p.Step}/{p.Path?.Count ?? -1}, {near:F0} m off, was {before[p]})"); }
                was[p] = p.Pos;
                bool vis = p.View is { Visible: true };
                if (near < Near && vis != shown[p])
                {
                    if (vis) { popped++; Note("popped", $"{p.Job} came into sight {near:F0} m off ({(p.Fleeing ? "fleeing" : "not fleeing")})"); }
                    else { hid++; Note("hid", $"{p.Job} went out of sight {near:F0} m off ({(p.Indoors ? "indoors" : "not indoors")}, {(p.Fleeing ? "fleeing" : "not fleeing")}{(p.Engaged ? ", fighting" : "")}{(p.Asleep ? ", asleep" : "")}{(p.Away ? ", away" : "")}, indoors a tick before {inside[p]}, pose away {p.PoseAway:F0}, in tree {p.View?.IsInsideTree()}, was {before[p]})"); }
                }
                shown[p] = vis;
                inside[p] = p.Indoors;
                before[p] = Flags(p);
                Vector3? d = Drawn(p);
                // (Drawn where you can see it: a body out of the view isn't posed till it comes back into it.)
                if (vis && d is { } now && drawn[p] is { } then && (now - then).Length() > DrawnMove && InView(now))
                {
                    drawnJumps++;
                    Note("drawn", $"{p.Job} drawn {(now - then).Length():F1} m on in a tick ({near:F0} m off)");
                }
                drawn[p] = d;
            }
        }
        int dead = alive.Count(p => p.Dead);
        GD.Print($"  {what}: {alive.Count} living before, {dead} died, {fled / 60} person-seconds fleeing, {town.Unstuck - unstuck} set back unstuck ({setBacks} seen as a step){(notes.Count > 0 ? "; " + string.Join("; ", notes) : "")}");
        Check(removed == 0 && alive.All(p => p.Dead || !p.Gone), $"after {what}, every living person is still there ({removed} removed)");
        Check(hid == 0 && popped == 0, $"after {what}, nobody within {Near} m went out of sight or came into it ({hid} hidden, {popped} appeared)");
        Check(jumps == 0, $"after {what}, nobody moved more than {TickMove} m in a tick in the sim ({jumps} jumps)");
        Check(drawnJumps == 0, $"after {what}, nobody was drawn more than {DrawnMove} m on in a tick ({drawnJumps} jumps)");
    }

    bool InView(Vector3 at)
    {
        Camera3D eye = GetViewport().GetCamera3D();
        return eye != null && !eye.IsPositionBehind(at) && new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size).HasPoint(eye.UnprojectPosition(at));
    }

    /// <summary>Where a body is drawn (world): the first piece of the pose as drawn, eased placement and all; null without one.</summary>
    static Vector3? Drawn(Town.Person p) =>
        p.View != null && GodotObject.IsInstanceValid(p.View) && p.View.Drawn is { Length: > 0 } pose ? p.View.GlobalTransform * pose[0].Origin : null;
}
