using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// A scavenger's run is a real trip (owner, 21 Nov: "we can't be having disappearing people like that"). On fresh worlds every
/// run (nothing pinned), a settlement whose people go on runs: one scavenger is followed by the sleeper the whole way at 10 to
/// 30 m (never undrawn in view, never a step longer than a run's in a tick, always on dry walkable ground once out on the land),
/// out to a real find, searching round it, and home through the way in with a bundle on their back. A second one is watched
/// going out and coming in only, the sleeper far off between (the place down meanwhile): it keeps walking its way, and is home
/// walking, never appearing in view. Nobody is removed except by death, and the far walking is cheap.
/// (Members new with the runs are read by name, so the same test runs on a build from before them, and fails there.)
///
/// -- --worlds=N: worlds to try for a settlement with scavengers and something in reach to pick over (default 6); --seed=N the first world's.
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/ScavTest.tscn
/// </summary>
public partial class ScavTest : Node
{
    const int WatchdogMinutes = 25;
    /// <summary>The most a body moves in a tick, m (9 m/s: StartleTest's), and within this of the sleeper nobody may pop in or out of sight, m.</summary>
    const float TickMove = 0.15f, Near = 40f;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
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

    /// <summary>Health kept up, and the weather kept clear (a downpour sends them home: not what is tested here).</summary>
    void TopUp()
    {
        _game.Suit.Vitals.Health = _max;
        if (++_tops % 300 == 0) _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
    }

    int _tops;

    /// <summary>Where shots go (--shots=dir; null: none), and a shot being taken.</summary>
    string _shots;
    bool _snapping;

    async void Snap(string file)
    {
        _snapping = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        GD.Print(image.SavePng(file) == Error.Ok ? $"saved {file}" : $"could not save {file}");
        _snapping = false;
    }

    // ---- What the runs added, read by name ---------------------------------------------------------

    static object Get(object o, string name) => o?.GetType().GetField(name)?.GetValue(o) ?? o?.GetType().GetProperty(name)?.GetValue(o);

    /// <summary>--seed=N: the first world's seed (a failure's world again, for an A/B; never pinned in a run).</summary>
    static ulong? Seeded(string name) =>
        OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith($"--{name}=")) is { } a && ulong.TryParse(a[(name.Length + 3)..], out ulong v) ? v : null;
    int _rounds;
    static void SetStatic(string name, float v) => typeof(Town).GetField(name)?.SetValue(null, v);
    /// <summary>Out of town on a run (or, before the runs, leaving for one or away on it).</summary>
    static bool OnRun(Town.Person p) => Get(p, "Run") != null || Get(p, "Leaving") is true || Get(p, "Away") is true;
    static int Searched(Town.Person p) => Get(Get(p, "Run"), "Searched") is int n ? n : 0;
    static bool Laden(Town.Person p) => Get(p, "Laden") is true;
    static bool Scavenger(Town.Person p) => !p.Dead && (p.Looks?.Figure.Routine?.Moves == "leave" || p.Job == PersonRole.Scavenger);

    IEnumerable<int> Scenario()
    {
        int worlds = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--worlds=")) is { } w ? int.Parse(w[9..]) : 6;
        _shots = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--shots="))?[8..];
        // (Finds near enough that two runs fit the test's time: the day allows a few hundred metres at most anyway.)
        SetStatic("RunNearest", 90f);
        SetStatic("RunFarthest", 260f);
        bool done = false;
        for (int i = 0; i < worlds && !done; i++)
            foreach (int x in Round(ok => done = ok)) yield return x;
        if (!done) Check(false, $"a settlement with scavengers on one of {worlds} fresh worlds");
    }

    IEnumerable<int> Round(Action<bool> found)
    {
        const string family = "survivor_town";
        PlanetSurface.Poi.PoiSiting.ForceFamily = family;
        WorldChoice world = WorldChoice.Fresh("temperate", _rounds++ == 0 ? Seeded("seed") : null);
        _game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.27;
        _max = _game.Suit.Sim.Data.Vitals.Health.Max;
        GD.Print($"-- a settlement's scavengers, {world} --");
        foreach (int x in Runs(family, found)) yield return x;
        _game.QueueFree();
        _game = null;
        yield return 0;
    }

    IEnumerable<int> Runs(string family, Action<bool> found)
    {
        // (One not held by raiders: their people are all raiders, and raiders do not go on runs.)
        var site = _game.Zone.Planet.Pois.Where(s => s.Family.Id == family).Take(4).Where(s => s.Instance?.Townsfolk != null).OrderBy(s => s.Instance.Mood == Mood.Hostile ? 1 : 0).FirstOrDefault();
        if (site == null) { GD.Print($"  (no {family} with people on this world)"); yield break; }
        if (site.Instance.Mood == Mood.Hostile) site.Instance.Mood = Mood.Wary;
        Int3 c0 = site.Instance.Townsfolk.Spots[0].Cell;
        Vector3 camp = _game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X + c0.X + 0.5f, site.Origin.Y + c0.Y, site.Origin.Z + c0.Z + 0.5f), _game.Survivor.WorldFeet);
        _game.Survivor.PlaceOutside((camp + Vector3.Up * 0.2f).ToNumerics());
        for (int i = 0; i < 3600 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) { TopUp(); yield return 0; }
        Place place = _game.Places.PlaceOf(site);
        Town town = place?.Memory.Town;
        if (town == null) { GD.Print("  (the settlement never stood with its people)"); yield break; }
        if (town.Memory.Mood == Mood.Hostile) town.Memory.Mood = Mood.Wary;
        for (int i = 0; i < 900 && town.People.Any(p => p.Looks == null || !p.Dead && p.View == null); i++) { TopUp(); yield return 0; }
        var runners = town.People.Where(Scavenger).ToList();
        if (runners.Count == 0) { GD.Print($"  (none of its {town.People.Count} go on runs: jobs {string.Join(", ", town.People.GroupBy(p => p.Job).Select(g => $"{g.Count()} {g.Key}"))})"); yield break; }
        int people = town.People.Count;
        Int3 entrance = place.Memory.Inst.Layout.EntranceCell, f = place.Memory.Inst.Layout.EntranceFacing.Vec();
        int gate = Enumerable.Range(0, town.Spots.Count).OrderBy(i => town.Spots[i].Cell.Manhattan(entrance)).First();
        Vector3 at = town.SpotAt(gate), outward = new Vector3(f.X, 0f, f.Z).LengthSquared() > 0.1f ? new Vector3(f.X, 0f, f.Z).Normalized() : Vector3.Right, stand = at;
        for (float d = 6f; d >= 2f; d -= 1f)
            if (town.CanStand(at - outward * d)) { stand = at - outward * d; break; }
        // (Test setup, before anything is watched: the scavengers to the way in, ready to go.)
        foreach (Town.Person p in runners) (p.Pos, p.Spot, p.Path, p.Wait) = (at, gate, null, 0f);
        _game.Survivor.PlaceOutside(town.World(stand + Vector3.Up * 0.15f).ToNumerics());
        GD.Print($"  ({runners.Count} of {people} go on runs; the sleeper {(stand - at).Length():F0} m inside the way in)");

        // One followed all the way (one sent home by something on the way, frightened before reaching a find, another).
        Town.Person first = null;
        var tried = new HashSet<Town.Person>();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            first = null;
            _game.Survivor.PlaceOutside(town.World(stand + Vector3.Up * 0.15f).ToNumerics());
            for (int i = 0; i < 150 * 60 && first == null && !(i > 900 && attempt == 0 && Get(town, "LastPlan") is string none && none.Contains(" 0 finds")); i++)
            {
                TopUp();
                _drive.Face(town.World(at + outward * 6f + Vector3.Up));
                yield return 0;
                first = runners.FirstOrDefault(p => OnRun(p) && !tried.Contains(p) && Get(Get(p, "Run"), "Home") is not true && new Vector2(p.Pos.X - at.X, p.Pos.Z - at.Z).Length() < 12f);
            }
            // (Nothing to pick over within a day's walk of this town: another world.)
            if (first == null && attempt == 0 && Get(town, "LastPlan") is string plan && plan.Contains(" 0 finds")) { GD.Print($"  (nothing in reach to go to: {plan})"); yield break; }
            if (first == null) { Check(false, $"a scavenger sets out at the way in within two and a half minutes ({runners.Count} waiting; {Get(town, "LastPlan")})"); yield break; }
            found(true);
            tried.Add(first);
            GD.Print($"  (one sets out: {Get(town, "LastPlan")})");
            _turnedBack = false;
            foreach (int x in Follow(town, first, at, attempt == 2)) yield return x;
            if (!_turnedBack) break;
        }

        // A second watched going and coming only, the sleeper far off between (another, if one is sent home before they're off).
        var watched = new HashSet<Town.Person> { first };
        for (int attempt = 0; attempt < 3; attempt++)
        {
            _game.Zone.Sky.TimeOfDay = 0.28;
            Town.Person second = null;
            _game.Survivor.PlaceOutside(town.World(stand + Vector3.Up * 0.15f).ToNumerics());
            foreach (Town.Person p in runners.Where(p => !watched.Contains(p) && !OnRun(p))) p.Wait = 0f;
            for (int i = 0; i < 150 * 60 && second == null; i++)
            {
                TopUp();
                _drive.Face(town.World(at + outward * 6f + Vector3.Up));
                yield return 0;
                // (One setting out now: others may be out already, from while the first was followed.)
                bool Going(Town.Person p) => OnRun(p) && Get(Get(p, "Run"), "Home") is not true && new Vector2(p.Pos.X - at.X, p.Pos.Z - at.Z).Length() < 12f;
                second = runners.Where(p => !watched.Contains(p)).FirstOrDefault(Going) ?? (runners.Count == 1 && Going(first) ? first : null);
            }
            if (second == null) { Check(false, "a second scavenger sets out at the way in within two and a half minutes"); yield break; }
            if (second == first) GD.Print("  (one scavenger here: the same one on a second run)");
            watched.Add(second);
            _turnedBack = false;
            foreach (int x in Unwatched(town, site, second, at, stand, outward, attempt == 2)) yield return x;
            if (!_turnedBack) break;
        }
        Check(town.People.Count == people && town.People.All(p => p.Dead || !p.Gone), $"nobody was removed but by death ({people} people, {town.People.Count} now, {town.People.Count(p => p.Dead)} dead, {town.People.Count(p => p.Gone && !p.Dead)} gone alive)");
    }

    /// <summary>The sleeper keeps 10 to 30 m from one of them from the way in, out, round the find and home again.</summary>
    bool _turnedBack;

    IEnumerable<int> Follow(Town town, Town.Person who, Vector3 gate, bool last)
    {
        var planet = _game.Zone.Planet;
        int undrawn = 0, jumps = 0, offGround = 0, ticks = 0, wet = 0;
        float farthest = 0f, worst = 0f, worstGround = 0f;
        bool searched = false, laden = false, home = false;
        Vector3 was = who.Pos;
        var notes = new List<string>();
        var took = new HashSet<string>();
        for (int t = 0; t < 14 * 60 * 60; t++)
        {
            TopUp();
            Vector3 w = town.World(who.Pos);
            Vector3 you = _game.Survivor.WorldFeet;
            Vector3 off = new Vector3(you.X - w.X, 0f, you.Z - w.Z);
            float d = off.Length();
            // Kept between 10 and 30 m, behind them as they go (carried along a tick at a time, on the ground).
            if (d > 26f || d < 12f)
            {
                Vector3 back = d > 0.5f ? off / d : -who.Forward;
                Vector3 to = w + back * 18f;
                to.Y = _game.Zone.GroundAt(to.X, to.Z).Y + 0.1f;
                Vector3 step = to - you;
                if (step.Length() > 0.4f) to = you + step.Normalized() * 0.4f + Vector3.Up * (_game.Zone.GroundAt(you.X, you.Z).Y + 0.1f - you.Y);
                _game.Survivor.PlaceOutside(to.ToNumerics(), glide: true);
            }
            // (Watching them, not aiming at them: a gun held on someone is a threat, and the look is the aim.)
            _drive.Face(Beside(w + Vector3.Up * 1.2f, 4f));
            yield return 0;
            ticks++;
            if (who.Dead) { GD.Print("  (the one followed died out there)"); break; }
            float flat = new Vector2(who.Pos.X - was.X, who.Pos.Z - was.Z).Length();
            float dist = (town.World(who.Pos) - _game.Survivor.WorldFeet).Length();
            if (flat > TickMove * PersonView.PoseEvery(dist + 1f)) { jumps++; if (notes.Count < 4) notes.Add($"a {flat:F2} m step at {t / 60f:F0} s"); }
            was = who.Pos;
            if (t > 60 && dist < PersonView.FarRange - 40f && who.View is not { Visible: true } && InView(town.World(who.Pos + Vector3.Up))) { undrawn++; if (notes.Count < 4) notes.Add($"undrawn {dist:F0} m off at {t / 60f:F0} s"); }
            // Out on the land (past the place's own ground), on its dry ground.
            float out_ = new Vector2(who.Pos.X - gate.X, who.Pos.Z - gate.Z).Length();
            farthest = MathF.Max(farthest, out_);
            Vector3 feet = town.World(who.Pos);
            if (!town.CanStand(who.Pos) && out_ > 15f)
            {
                float ground = planet.GroundHeight(Wrap(feet.X, planet.Width), Wrap(feet.Z, planet.Height));
                float gap = MathF.Abs(feet.Y - ground);
                worstGround = MathF.Max(worstGround, gap);
                if (gap > 0.6f && offGround++ == 0) notes.Add($"{gap:F1} m off the ground {out_:F0} m out at {t / 60f:F0} s ({(Get(Get(who, "Run"), "Road") is true ? "by road" : "over the land")})");
                if (planet.WaterSurfaceAt(feet) != null) wet++;
            }
            // Shots (--shots=dir, a windowed run): out on the land, searching, and on the way home with the bundle; a few metres off, from the side.
            string shot = _shots == null ? null : !took.Contains("out") && out_ > 60f && Searched(who) == 0 ? "out" : !took.Contains("search") && Get(Get(who, "Run"), "Searching") is >= 0 ? "search" : !took.Contains("home") && Laden(who) && out_ < farthest * 0.6f && out_ > 30f ? "home" : null;
            if (shot != null)
            {
                took.Add(shot);
                // (From the side as they go out, behind as they go home: the bundle is on their back.)
                Vector3 side = new Vector3(-who.Forward.Z, 0f, who.Forward.X) * (shot == "home" ? 2.5f : 5.5f) + who.Forward * (shot == "out" ? 3.5f : -4.5f);
                Vector3 at = town.World(who.Pos) + side;
                at.Y = _game.Zone.GroundAt(at.X, at.Z).Y + 0.1f;
                _game.Survivor.PlaceOutside(at.ToNumerics(), glide: true);
                for (int k = 0; k < 20; k++) { TopUp(); _drive.Face(Beside(town.World(who.Pos) + Vector3.Up * 1.0f, 1.6f)); yield return 0; }
                was = who.Pos;
                Snap(System.IO.Path.Combine(_shots, $"scav-{shot}.png"));
                while (_snapping) yield return 0;
                was = who.Pos;
            }
            if (t % 1800 == 0) { object r = Get(who, "Run"); GD.Print($"    {t / 60} s: {out_:F0} m out, homeward {Get(r, "Home")}, way {Get(r, "Leg")}/{(Get(r, "Way") as System.Collections.ICollection)?.Count}, searching {Get(r, "Searching")}, now {who.Now:F2}, speed {who.Speed:F1}, scared {who.ScaredFor:F1}, fighting {who.Engaged}, fleeing {who.Fleeing}, act {who.Act}"); }
            worst = MathF.Max(worst, flat);
            searched |= Searched(who) > 0;
            laden |= Laden(who);
            if (t > 120 && !OnRun(who) && out_ < 3f && searched) { home = true; break; }
            if (!last && !searched && Get(Get(who, "Run"), "Home") is true) { _turnedBack = true; GD.Print($"  (turned for home before the find, {out_:F0} m out, {Get(Get(who, "Run"), "Why")}: scared {who.ScaredFor:F1}, fleeing {who.Fleeing}, mood {who.Feel.Mood}, threat {who.Threat?.Species.Name ?? "none"}; another followed)"); yield break; }
        }
        GD.Print($"  followed {ticks / 60f:F0} s: {farthest:F0} m out at most, the longest step {worst:F2} m{(notes.Count > 0 ? "; " + string.Join("; ", notes) : "")}");
        Check(farthest > 80f, $"the one followed went out on the land ({farthest:F0} m from the way in at most)");
        Check(searched, "they reached a real find and searched round it");
        Check(home && laden, $"they came home through the way in carrying what they found (home {home}, a bundle on their back {laden})");
        Check(undrawn == 0, $"followed at 10-30 m, they were drawn the whole way in view ({undrawn} ticks undrawn)");
        Check(jumps == 0, $"no step longer than a run's in a tick ({jumps} jumps)");
        Check(offGround == 0 && wet == 0, $"always on dry ground out on the land ({offGround} ticks off it, worst {worstGround:F2} m; {wet} in water)");
    }

    static double Wrap(float v, double size) => ((v % size) + size) % size;

    /// <summary>A point this far to the side of another, square to the sleeper's way of looking at it.</summary>
    Vector3 Beside(Vector3 at, float by)
    {
        Vector3 to = at - _game.Survivor.WorldEye();
        Vector3 side = new Vector3(-to.Z, 0f, to.X);
        return side.LengthSquared() > 1e-4f ? at + side.Normalized() * by : at;
    }

    /// <summary>One watched as they go out (60 m), the sleeper far off while they are out (the place down), back by the way in before they are: they come home walking, and never appear in view.</summary>
    IEnumerable<int> Unwatched(Town town, PlanetSurface.Poi.PoiSite site, Town.Person who, Vector3 gate, Vector3 stand, Vector3 outward, bool last)
    {
        int popped = 0, jumps = 0;
        bool shown = who.View is { Visible: true };
        Vector3 was = who.Pos;
        void Watch(int t)
        {
            float flat = new Vector2(who.Pos.X - was.X, who.Pos.Z - was.Z).Length();
            float dist = (town.World(who.Pos) - _game.Survivor.WorldFeet).Length();
            if (flat > TickMove * PersonView.PoseEvery(dist + 1f)) jumps++;
            was = who.Pos;
            bool vis = who.View is { Visible: true };
            if (vis && !shown && dist < Near && InView(town.World(who.Pos + Vector3.Up))) popped++;
            shown = vis;
        }
        for (int t = 0; t < 120 * 60 && new Vector2(who.Pos.X - gate.X, who.Pos.Z - gate.Z).Length() < 60f && OnRun(who); t++)
        {
            TopUp();
            _drive.Face(town.World(who.Pos + Vector3.Up));
            yield return 0;
            Watch(t);
        }
        object going = Get(who, "Run");
        if (!last && (going == null || Get(going, "Home") is true)) { _turnedBack = true; GD.Print($"  (sent home before they were off: {Get(going, "Why") ?? "home"}; another watched)"); yield break; }
        // Far off: 1.5 km from the town, the other side of it from where they went.
        Vector3 w = town.World(who.Pos), g = town.World(gate);
        Vector3 away = new Vector3(g.X - w.X, 0f, g.Z - w.Z).Normalized() * 1500f + g;
        away.Y = _game.Zone.GroundAt(away.X, away.Z).Y + 0.3f;
        _game.Survivor.PlaceOutside(away.ToNumerics());
        Vector3 left = who.Pos;
        float moved = 0f;
        bool down = false;
        float worstMs = 0f, sumMs = 0f;
        int samples = 0;
        // Until they are on their way home and near it (or home already): the place down, their walk stepped as a point.
        for (int t = 0; t < 12 * 60 * 60; t++)
        {
            TopUp();
            yield return 0;
            down |= _game.Places.PlaceOf(site) == null;
            moved += new Vector2(who.Pos.X - left.X, who.Pos.Z - left.Z).Length();
            left = who.Pos;
            if (Get(town, "LastRunsMs") is float ms && OnRun(who) && t > 600) { worstMs = MathF.Max(worstMs, ms); sumMs += ms; samples++; }
            object run = Get(who, "Run");
            if (who.Dead || run == null && !OnRun(who) || Get(run, "Home") is true && new Vector2(who.Pos.X - gate.X, who.Pos.Z - gate.Z).Length() < 90f) break;
        }
        GD.Print($"  out of sight {(down ? "with the place down" : "with the place still up")}: {moved:F0} m along their way, {Searched(who)} spots searched (on a run {OnRun(who)}, homeward {Get(Get(who, "Run"), "Home")}, scared {who.ScaredFor:F1}, {new Vector2(who.Pos.X - gate.X, who.Pos.Z - gate.Z).Length():F0} m out); the walk of those out {(samples > 0 ? sumMs / samples : 0):F4} ms a tick on average, {worstMs:F4} ms at worst");
        Check(moved > 40f && down, $"far off, with the place down, they kept walking their way ({moved:F0} m, the place {(down ? "down" : "never down")})");
        Check(samples > 0 && worstMs < 0.1f, $"the far walking cost under 0.1 ms a tick ({worstMs:F4} ms at worst over {samples} ticks)");
        // Back inside the way in, before they are.
        _game.Survivor.PlaceOutside(town.World(stand + Vector3.Up * 0.15f).ToNumerics());
        shown = who.View is { Visible: true };
        was = who.Pos;
        bool home = false, walking = false;
        for (int t = 0; t < 6 * 60 * 60; t++)
        {
            TopUp();
            _drive.Face(town.World(who.Pos + Vector3.Up));
            yield return 0;
            if (_game.Places.PlaceOf(site) is not { Ready: true }) { was = who.Pos; shown = who.View is { Visible: true }; continue; }
            Watch(t);
            float out_ = new Vector2(who.Pos.X - gate.X, who.Pos.Z - gate.Z).Length();
            if (out_ < 6f && out_ > 1f && who.Now > 0.3f) walking = true;
            if (!OnRun(who) && out_ < 3f) { home = true; break; }
            if (who.Dead) break;
        }
        object r = Get(who, "Run");
        if (!home) GD.Print($"  (not home: {new Vector2(who.Pos.X - gate.X, who.Pos.Z - gate.Z).Length():F1} m from the way in, on a run {OnRun(who)}, homeward {Get(r, "Home")}, way {Get(r, "Leg")}/{(Get(r, "Way") as System.Collections.ICollection)?.Count}, searching {Get(r, "Searching")}, now {who.Now:F2}, the place {(_game.Places.PlaceOf(site) is { Ready: true } ? "up" : "down")}, drawn {who.View is { Visible: true }})");
        Check(home && walking, $"the one not watched came home walking through the way in (home {home}, walking in {walking}{(Laden(who) ? ", carrying what they found" : "")})");
        Check(popped == 0, $"they never appeared in view within {Near} m ({popped} times)");
        Check(jumps == 0, $"no step longer than a run's in a tick while watched ({jumps} jumps)");
    }

    bool InView(Vector3 at)
    {
        Camera3D eye = GetViewport().GetCamera3D();
        return eye != null && !eye.IsPositionBehind(at) && new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size).HasPoint(eye.UnprojectPosition(at));
    }
}
