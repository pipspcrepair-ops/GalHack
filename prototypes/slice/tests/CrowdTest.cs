using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Slice.Surface;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Crowds keep a natural distance and nobody twitches (owner, 22 Nov: "NPCs have a tendency to clump. It's a problem with grazing
/// animals, too"; "npc's can get really twitchy when figuring out which way to path"). On fresh worlds every run (nothing pinned),
/// a settlement: a dozen of its people sent to one spot in its streets (a crowd), then all of them on into a building by the one
/// way (a shared route and a doorway, with others coming out the other way); two of them walking home over the land side by side;
/// and a herd put down grazing near it, then alarmed. Throughout: nobody stands in anybody (their real radii), the nearest
/// neighbour is about an arm's length off for people and looser for a grazing herd (tighter once alarmed), two on the one way
/// are never one body, and heading flicks (a turn of 35° one way and back within 2.5 s) stay rare in crowds and doorways.
///
/// -- --worlds=N (default 2); --seeds=a:b:... rolls a failing run's worlds again (an A/B; never pinned); --shots=DIR (windowed):
/// crowd.png and herd.png.
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/CrowdTest.tscn
/// </summary>
public partial class CrowdTest : Node
{
    const int WatchdogMinutes = 25;
    /// <summary>A flick: a turn at least this far (rad) one way and back again as far, within this long (s).</summary>
    const float FlickTurn = 0.6f, FlickSpan = 2.5f;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly System.Random _rng = new();
    readonly ulong[] _seeds = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--seeds=")) is { } s ? s[8..].Split(':').Select(ulong.Parse).ToArray() : Array.Empty<ulong>();
    readonly string _shots = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--shots=")) is { } d ? d[8..] : null;
    bool _snapping;
    Game.Game _game;
    Driver _drive;
    double _max;
    // Over all worlds.
    readonly Tally _crowd = new("in a crowd"), _doors = new("through a doorway"), _route = new("on a shared route"), _land = new("two walking home over the land");
    readonly List<float> _calm = new(), _alarmed = new();
    float _herdWorst, _herdR;
    string _herdWorstAt = "";
    int _herds;
    readonly List<float> _townMs = new(), _wildMs = new();

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        if (_shots != null) Directory.CreateDirectory(_shots);
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

    /// <summary>What was seen of people in one kind of moment: overlaps, nearest gaps, flicks and the time watched.</summary>
    sealed class Tally
    {
        public readonly string What;
        public int Overlaps, Samples, Flicks, Merged, Beside, Behind;
        public float Worst, Seconds;
        public readonly List<float> Gaps = new();
        public string WorstAt = "";
        public readonly List<string> Notes = new();
        public Tally(string what) => What = what;
        public float PerMinute => Seconds > 1f ? Flicks / (Seconds / 60f) : 0f;
        public float Quantile(float q) => Gaps.Count == 0 ? float.NaN : Gaps.OrderBy(g => g).ElementAt(Math.Min(Gaps.Count - 1, (int)(q * Gaps.Count)));
    }

    /// <summary>A body's turning, to find flicks: the swing under way (its sign, how far, since when) and the one before it.</summary>
    sealed class Swing
    {
        public float Yaw, Dir, Amount, Since, Prev, PrevSince, TurnedAt = -9f;
        public bool Counted, Seen;
        public string Note, PrevNote;
    }

    /// <summary>One tick of a body's facing: true when it completes a flick (a turn one way and straight back).</summary>
    static bool Flicked(Swing s, float yaw, float now)
    {
        if (!s.Seen) { (s.Seen, s.Yaw) = (true, yaw); return false; }
        float d = Mathf.AngleDifference(s.Yaw, yaw);
        s.Yaw = yaw;
        if (MathF.Abs(d) < 0.002f) return false;
        if (now - s.TurnedAt > 0.6f) (s.Prev, s.Dir, s.Amount) = (0f, 0f, 0f); // (a pause between: a turn, then another, not a flick)
        s.TurnedAt = now;
        float sign = MathF.Sign(d);
        if (sign != s.Dir) (s.Prev, s.PrevSince, s.Dir, s.Amount, s.Since, s.Counted) = (s.Amount, s.Since, sign, 0f, now, false);
        s.Amount += MathF.Abs(d);
        if (s.Counted || s.Amount < FlickTurn || s.Prev < FlickTurn || now - s.PrevSince > FlickSpan) return false;
        s.Counted = true;
        return true;
    }

    IEnumerable<int> Scenario()
    {
        int worlds = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--worlds=")) is { } w ? int.Parse(w[9..]) : 2;
        for (int i = 0; i < worlds && _clock.Elapsed.TotalMinutes < 18; i++)
            foreach (int x in World(i)) yield return x;
        Report();
    }

    IEnumerable<int> World(int round)
    {
        const string family = "survivor_town";
        PlanetSurface.Poi.PoiSiting.ForceFamily = family;
        WorldChoice world = WorldChoice.Fresh("temperate", _seeds.Length > round ? _seeds[round] : null);
        _game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.42;
        _max = _game.Suit.Sim.Data.Vitals.Health.Max;
        GD.Print($"-- a settlement, {world} --");
        foreach (int x in Settlement(family, round == 0)) yield return x;
        _game.QueueFree();
        _game = null;
        yield return 0;
    }

    static readonly MethodInfo GoToMethod = typeof(Town).GetMethod("GoTo", BindingFlags.NonPublic | BindingFlags.Instance);
    static void Send(Town town, Town.Person p, int spot, float wait)
    {
        GoToMethod.Invoke(town, new object[] { p, spot });
        p.Wait = wait;
    }

    static bool Awake(Town.Person p) => !p.Dead && !p.Gone && !p.Asleep && p.Looks != null && p.Run == null;

    IEnumerable<int> Settlement(string family, bool shots)
    {
        var site = _game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == family && s.Instance?.Townsfolk != null);
        if (site == null) { GD.Print($"  (no {family} with people could be seated on this world)"); yield break; }
        if (site.Instance.Mood == Mood.Hostile) site.Instance.Mood = Mood.Wary;
        Int3 c0 = site.Instance.Townsfolk.Spots[0].Cell;
        Vector3 camp = _game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X + c0.X + 0.5f, site.Origin.Y + c0.Y, site.Origin.Z + c0.Z + 0.5f), _game.Survivor.WorldFeet);
        _game.Survivor.PlaceOutside((camp + Vector3.Up * 0.2f).ToNumerics());
        for (int i = 0; i < 3600 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) { TopUp(); yield return 0; }
        Place place = _game.Places.PlaceOf(site);
        Town town = place?.Memory.Town;
        if (town == null) { Check(false, $"the {family} stands with its people"); yield break; }
        town.Memory.Mood = Mood.Peaceful; // (people about their day, nobody minding you)
        for (int i = 0; i < 900 && town.People.Any(p => p.Looks == null || !p.Dead && p.View == null); i++) { TopUp(); yield return 0; }

        // A spot in the open with room round it, and one under a roof: the crowd's two ends.
        bool Roofed(int k) => place.Roofed(town.World(town.SpotAt(k) + Vector3.Up));
        var open = Enumerable.Range(0, town.Spots.Count).Where(k => town.Spots[k].Tag is not ("sit" or "bed") && !Roofed(k)).ToList();
        var inside = Enumerable.Range(0, town.Spots.Count).Where(k => town.Spots[k].Tag is not ("sit" or "bed") && Roofed(k)).ToList();
        if (open.Count == 0 || inside.Count == 0) { GD.Print($"  (no spot {(open.Count == 0 ? "in the open" : "under a roof")} here)"); yield break; }
        int hub = open.OrderBy(k => town.People.Where(Awake).Sum(p => (p.Pos - town.SpotAt(k)).Length())).First();
        Vector3 at = town.SpotAt(hub);
        var crowd = town.People.Where(p => Awake(p) && p.Role != "guard" && !p.Sitting).OrderBy(p => (p.Pos - at).Length()).Take(12).ToList();
        int into = inside.OrderBy(k => (town.SpotAt(k) - at).Length()).First();
        // The sleeper a few metres off the hub, watching.
        Vector3 stand = at;
        for (int k = 0; k < 16; k++)
        {
            float a = k * MathF.Tau / 16f;
            Vector3 c = at + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * 7f;
            if (town.CanStand(c)) { stand = c; break; }
        }
        _game.Survivor.PlaceOutside(town.World(stand + Vector3.Up * 0.15f).ToNumerics());
        var doors = place.Memory.Inst.Layout.Doorways.SelectMany(d => d.Cells).Concat(place.Memory.Inst.Layout.ExitCells).Select(c => new Vector3(c.X + 0.5f, c.Y, c.Z + 0.5f)).ToList();
        GD.Print($"  ({town.People.Count} people; {crowd.Count} sent to a spot in the open, then on into a building {(town.SpotAt(into) - at).Length():F0} m off; {doors.Count} doorway cells)");

        foreach (Town.Person p in crowd) Send(town, p, hub, 60f);
        foreach (int x in Watch(town, crowd, 30, null, null, shots ? (at, "crowd.png") : default)) yield return x;
        // On into the building by the one way, and those inside out to the hub past them.
        var outs = town.People.Where(p => Awake(p) && !crowd.Contains(p) && p.Role != "guard" && !p.Sitting && Roofed(p.Spot)).Take(5).ToList();
        foreach (Town.Person p in crowd) Send(town, p, into, 30f);
        foreach (Town.Person p in outs) Send(town, p, hub, 30f);
        foreach (int x in Watch(town, crowd.Concat(outs).ToList(), 40, doors, into, default)) yield return x;

        foreach (int x in Land(town, place)) yield return x;
        foreach (int x in Grazing(town, shots)) yield return x;
    }

    /// <summary>
    /// Some seconds of people: each tick their overlaps, each second their nearest neighbour's gap, every tick their facing for flicks
    /// (near a doorway counted apart); those bound for <paramref name="route"/> together are walkers on a shared route.
    /// </summary>
    IEnumerable<int> Watch(Town town, List<Town.Person> who, int seconds, List<Vector3> doors, int? route, (Vector3 At, string Name) shot)
    {
        var swings = new Dictionary<Town.Person, Swing>();
        float dt = 1f / 60f;
        for (int t = 0; t < seconds * 60; t++)
        {
            TopUp();
            yield return 0;
            _townMs.Add(Ms(town, "LastApartMs"));
            float now = t * dt;
            var live = town.People.Where(p => Awake(p) && p.View is { Visible: true }).ToList();
            foreach (Town.Person p in who)
            {
                if (!live.Contains(p)) continue;
                bool nearDoor = doors != null && doors.Any(d => MathF.Abs(d.Y - p.Pos.Y) < 1.5f && new Vector2(d.X - p.Pos.X, d.Z - p.Pos.Z).Length() < 2f);
                Tally tally = nearDoor ? _doors : _crowd;
                if (!p.Sitting) tally.Seconds += dt;
                if (!swings.TryGetValue(p, out Swing s)) swings[p] = s = new Swing();
                if (!p.Sitting && Flicked(s, p.Yaw, now) && tally.Flicks++ < 8) tally.Notes.Add($"{s.PrevNote} then {s.Note} then {Note(p)}");
                if (s.Since == now || s.Note == null) (s.PrevNote, s.Note) = (s.Note, Note(p));
                // Bodies in each other, and the nearest one's gap once a second (standing or walking, not seated at a table).
                float nearest = float.MaxValue;
                Town.Person other = null;
                foreach (Town.Person o in live)
                {
                    if (o == p || MathF.Abs(o.Pos.Y - p.Pos.Y) > 1.5f) continue;
                    float gap = new Vector2(o.Pos.X - p.Pos.X, o.Pos.Z - p.Pos.Z).Length() - Town.RadiusOf(p) - Town.RadiusOf(o);
                    if (gap < nearest) (nearest, other) = (gap, o);
                }
                if (other == null) continue;
                Tally pair = route is int r && p.Spot == r && other.Spot == r && Walking(p) && Walking(other) && nearest < 2f ? _route : tally;
                bool counts = !p.Sitting && !other.Sitting && !p.Engaged && !other.Engaged;
                if (counts && nearest < -0.03f && (who.IndexOf(other) < 0 || who.IndexOf(p) < who.IndexOf(other)))
                {
                    pair.Overlaps++;
                    if (-nearest > pair.Worst) (pair.Worst, pair.WorstAt) = (-nearest, $"at {p.Pos.X:F1},{p.Pos.Z:F1}, {Note(p)} and {Note(other)}");
                }
                if (pair == _route && p.Now > 0.3f)
                {
                    // Side by side or one behind the other (by the line between them against the way they go), or one body.
                    Vector3 off = (other.Pos - p.Pos) with { Y = 0f };
                    if (off.Length() < MathF.Max(Town.RadiusOf(p), Town.RadiusOf(other))) pair.Merged++;
                    else if (MathF.Abs(off.Normalized().Dot(p.Forward)) > 0.7f) pair.Behind++;
                    else pair.Beside++;
                }
                if (t % 60 == 0 && counts && nearest < 3f) { pair.Gaps.Add(nearest); pair.Samples++; }
            }
            if (shot.Name != null && t == 20 * 60 && _shots != null) foreach (int x in Snap(town.World(shot.At), shot.Name)) yield return x;
        }
    }

    static bool Walking(Town.Person p) => p.Path != null && p.Step < p.Path.Count;

    bool Wet(Vector3 w) => _game.Zone.Planet.Source.WaterSurfaceAt(Planets.Gen.Mathx.WrapX(w.X, _game.Zone.Planet.Width), Planets.Gen.Mathx.Wrap(w.Z, _game.Zone.Planet.Height), out _);

    /// <summary>What someone is at, for a flick's note.</summary>
    static string Note(Town.Person p) => $"[{p.Job} {(Walking(p) ? $"walking {p.Step}/{p.Path.Count}" : "standing")} now {p.Now:F1} spin {Ms(p, "Spin"):F1} stuck {p.StuckFor:F1} yield {p.Yield:F1}{(p.Partner != null ? " partner" : "")}{(p.AnswerFor > 0f ? " answering" : "")} act {p.Act} spot {p.Spot} wait {p.Wait:F0}{(p.Fleeing ? " fleeing" : "")}{(p.Engaged ? " engaged" : "")}]";

    static float Ms(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o) is float f ? f : o.GetType().GetField(name)?.GetValue(o) is float g ? g : float.NaN;

    /// <summary>Two of them walking home over the land side by side from 40 m out (a run's way home, as the owner saw two scavengers come in).</summary>
    IEnumerable<int> Land(Town town, Place place)
    {
        Int3 entrance = place.Memory.Inst.Layout.EntranceCell, f = place.Memory.Inst.Layout.EntranceFacing.Vec();
        int gate = Enumerable.Range(0, town.Spots.Count).OrderBy(i => town.Spots[i].Cell.Manhattan(entrance)).First();
        Vector3 home = town.SpotAt(gate), outward = new Vector3(f.X, 0f, f.Z).LengthSquared() > 0.1f ? new Vector3(f.X, 0f, f.Z).Normalized() : Vector3.Right;
        var way = new List<Vector3>();
        for (float d = 40f; d >= 0f; d -= 2f)
        {
            Vector3 w = _game.Zone.GroundAt(town.World(home).X + outward.X * d, town.World(home).Z + outward.Z * d);
            way.Add(d == 0f ? home : town.Local(w));
        }
        var two = town.People.Where(p => Awake(p) && p.Role != "guard" && !p.Sitting && !p.Engaged).Take(2).ToList();
        if (two.Count < 2) { GD.Print("  (not two to walk home)"); yield break; }
        Vector3 side = new(-outward.Z, 0f, outward.X);
        for (int i = 0; i < 2; i++)
        {
            Town.Person p = two[i];
            p.Run = new ScavRun { Out = way, Way = way, Leg = 1, Round = new List<Vector3>(), Find = way[0], Home = true, Why = "test" };
            (p.Path, p.Step, p.Partner, p.Working, p.Wait, p.Now) = (null, 0, null, false, 0f, 0f);
            p.Pos = way[0] + side * (i == 0 ? -0.25f : 0.25f);
            p.Yaw = MathF.Atan2(outward.X, outward.Z); // (facing home)
        }
        _game.Survivor.PlaceOutside(town.World(way[way.Count / 2] + side * 6f + Vector3.Up * 0.3f).ToNumerics());
        GD.Print($"  (two set 0.5 m apart 40 m out, walking home side by side)");
        for (int t = 0; t < 30 * 60 && two.All(p => p.Run != null && !p.Dead); t++)
        {
            TopUp();
            yield return 0;
            float gap = new Vector2(two[0].Pos.X - two[1].Pos.X, two[0].Pos.Z - two[1].Pos.Z).Length() - Town.RadiusOf(two[0]) - Town.RadiusOf(two[1]);
            if (gap < -0.03f) { _land.Overlaps++; if (-gap > _land.Worst) (_land.Worst, _land.WorstAt) = (-gap, $"at {two[0].Pos.X:F1},{two[0].Pos.Z:F1}, leg {two[0].Run.Leg}"); }
            if (t % 30 == 0) _land.Gaps.Add(gap);
            Vector3 off = (two[1].Pos - two[0].Pos) with { Y = 0f };
            if (off.Length() < Town.RadiusOf(two[0])) _land.Merged++;
            else if (MathF.Abs(off.Normalized().Dot(two[0].Forward)) > 0.7f) _land.Behind++;
            else _land.Beside++;
            _land.Seconds += 1f / 60f;
        }
    }

    /// <summary>A herd of the world's put down grazing by the settlement (the sleeper out of its notice): its spacing grazing, then alarmed.</summary>
    IEnumerable<int> Grazing(Town town, bool shots)
    {
        WildlifePatch patch = _game.Zone.Wildlife;
        Species s = patch.Life?.Fauna.Species.Where(x => x.Behaviour == Behaviour.Herd && !x.Flies && !x.Corrupted && x.Rig != RigKind.Swarm).OrderBy(x => x.Activity is Wildlife.Activity.Diurnal or Wildlife.Activity.Cathemeral ? 0 : 1).FirstOrDefault();
        if (s == null) { GD.Print("  (no herd animal on this world)"); yield break; }
        // The sleeper out past the settlement first (its wildlife about them), then the herd put down 25 m off them.
        Vector3 home = town.World(town.SpotAt(0));
        float away = _rng.NextSingle() * MathF.Tau;
        Vector3 Out(float a, float r) => _game.Zone.GroundAt(home.X + MathF.Sin(a) * r, home.Z + MathF.Cos(a) * r);
        for (int k = 0; k < 24 && (Wet(Out(away, 110f)) || Wet(Out(away, 135f))); k++) away += MathF.Tau / 24f; // (dry land there and past it)
        Vector3 feet = Out(away, 110f);
        _game.Survivor.PlaceOutside((feet + Vector3.Up * 0.3f).ToNumerics());
        for (int i = 0; i < 180; i++) { TopUp(); yield return 0; }
        feet = _game.Survivor.WorldFeet;
        patch = _game.Zone.Wildlife;
        patch.Sim.Targets.Remove(patch.Player);
        List<Creature> herd = new();
        Vector3 mid = feet;
        for (int k = 0; k < 24 && herd.Count < 3; k++)
        {
            foreach (Creature c in herd) c.Removed = true;
            float a = away + (k % 2 == 0 ? 1 : -1) * (k / 2) * MathF.Tau / 12f; // (on out first, then round)
            mid = _game.Zone.GroundAt(feet.X + MathF.Sin(a) * (k < 12 ? 25f : 40f), feet.Z + MathF.Cos(a) * (k < 12 ? 25f : 40f));
            if (Wet(mid)) continue;
            herd = patch.Sim.SpawnGroup(s, patch.PatchLocal(mid), 9).Where(c => !c.Removed).ToList();
        }
        if (herd.Count < 3) { patch.Sim.Targets.Add(patch.Player); GD.Print($"  (no room for a herd of {s.CommonName} here)"); yield break; }
        foreach (Creature c in herd) c.Rest = null; // (STAGING: kept out of its bedding ground, about its day whatever the hour)
        _herds++;
        _herdR = herd[0].Radius;
        GD.Print($"  ({herd.Count} {s.CommonName} (radius {herd[0].Radius:F2} m) put down {mid.DistanceTo(feet):F0} m off, grazing, {feet.DistanceTo(home):F0} m out of the settlement)");
        Herd h = herd[0].Herd;
        for (int t = 0; t < 50 * 60; t++)
        {
            yield return 0;
            TopUp();
            _wildMs.Add(Ms(patch.Sim, "LastApartMs"));
            bool alarmed = t >= 35 * 60;
            if (alarmed && t % 120 == 0 && h != null) h.Alarm(h.Center + new Vector3(18f, 0f, 0f), patch.Sim.Time);
            if (t == 30 * 60 || t == 45 * 60) GD.Print($"    (the herd at {t / 60} s: {string.Join(", ", herd.Where(c => c.Alive && !c.Removed).GroupBy(c => c.State).Select(g => $"{g.Count()} {g.Key}"))}{(h != null ? $", {(h.Alarmed ? "alarmed" : "calm")}" : "")})");
            if (shots && _shots != null && t == 30 * 60) foreach (int x in Snap(patch.WorldOf(herd[0]) + (h != null ? h.Center - herd[0].Position : Vector3.Zero), "herd.png", 9f)) yield return x;
            if (t < 15 * 60 || t >= 35 * 60 && t < 38 * 60 || t % 30 != 0) continue; // (settled first; the alarm taken in first)
            var live = herd.Where(c => c.Alive && !c.Removed).ToList();
            foreach (Creature c in live)
            {
                Creature o = live.Where(x => x != c).OrderBy(x => WildSim.Flat(x.Position - c.Position).Length() - x.Radius).FirstOrDefault();
                if (o == null) continue;
                float nearest = WildSim.Flat(o.Position - c.Position).Length() - c.Radius - o.Radius;
                if (-nearest > _herdWorst) (_herdWorst, _herdWorstAt) = (-nearest, $"{c.State}{(c.Species.Adult != null ? " young" : "")} and {o.State}{(o.Species.Adult != null ? " young" : "")}, {(alarmed ? "alarmed" : "grazing")}");
                // (The young at their mothers' sides are not the herd's spacing.)
                if (c.Species.Adult == null && o.Species.Adult == null) (alarmed ? _alarmed : _calm).Add(nearest);
            }
        }
        patch.Sim.Targets.Add(patch.Player);
        foreach (Creature c in herd) c.Removed = true;
    }

    /// <summary>A frame of a point from a little way off and up (windowed only).</summary>
    IEnumerable<int> Snap(Vector3 at, string name, float back = 7f)
    {
        Vector3 feet = _game.Survivor.WorldFeet, to = new Vector3(at.X - feet.X, 0f, at.Z - feet.Z);
        to = to.LengthSquared() > 0.01f ? to.Normalized() : Vector3.Forward;
        Vector3 eye = at - to * back;
        eye.Y = MathF.Max(at.Y + 2.2f, _game.Zone.GroundAt(eye.X, eye.Z).Y + 2.2f);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(at + Vector3.Up * 0.6f - eye, Vector3.Up), eye);
        _game.Visor.Visible = false;
        yield return 0;
        yield return 0;
        _snapping = true;
        Capture(Path.Combine(_shots, name));
        while (_snapping) yield return 0;
        _game.FixedCamera = null;
        _game.Visor.Visible = true;
    }

    async void Capture(string file)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print(err == Error.Ok ? $"  (saved {file})" : $"  (could not save {file}: {err})");
        _snapping = false;
    }

    static float Median(List<float> v) => v.Count == 0 ? float.NaN : v.OrderBy(x => x).ElementAt(v.Count / 2);
    static float Share(List<float> v, Func<float, bool> f) => v.Count == 0 ? 0f : v.Count(f) / (float)v.Count;

    void Report()
    {
        GD.Print("-- over all worlds --");
        foreach (Tally t in new[] { _crowd, _doors, _route })
            GD.Print($"  ({t.What}: {t.Seconds / 60f:F1} person-minutes, {t.Flicks} flicks ({t.PerMinute:F2} a minute), {t.Overlaps} overlapping ticks (worst {t.Worst:F2} m{(t.WorstAt != "" ? ", " + t.WorstAt : "")}), nearest gap median {Median(t.Gaps):F2} m, 10% {t.Quantile(0.1f):F2} m over {t.Gaps.Count}{(t == _route ? $"; walking {t.Beside} side by side, {t.Behind} one behind, {t.Merged} as one" : "")})");
        foreach (Tally t in new[] { _crowd, _doors }) foreach (string n in t.Notes) GD.Print($"    (flick {t.What}: {n})");
        GD.Print($"  ({_land.What}: {_land.Seconds:F0} s, {_land.Overlaps} ticks in each other (worst {_land.Worst:F2} m{(_land.WorstAt != "" ? ", " + _land.WorstAt : "")}), gap median {Median(_land.Gaps):F2} m, least {(_land.Gaps.Count > 0 ? _land.Gaps.Min() : float.NaN):F2} m; {_land.Beside} ticks side by side, {_land.Behind} one behind, {_land.Merged} as one)");
        GD.Print($"  (herds: {_herds}, radius {_herdR:F2} m; nearest gap grazing median {Median(_calm):F2} m ({Share(_calm, g => g < 0.15f) * 100f:F0}% within 0.15 m), alarmed median {Median(_alarmed):F2} m; deepest overlap {_herdWorst:F2} m)");
        GD.Print($"  (cost: town personal space {Avg(_townMs):F3} ms a tick (99% under {Quantile(_townMs, 0.99f):F3}), wildlife {Avg(_wildMs):F3} ms (99% under {Quantile(_wildMs, 0.99f):F3}))");
        Check(_crowd.Seconds > 60f && _route.Samples + _route.Beside + _route.Behind > 0, $"crowds watched: {_crowd.Seconds / 60f:F1} person-minutes in the open, {_doors.Seconds / 60f:F1} by doorways, {_route.Beside + _route.Behind + _route.Merged} ticks of two on one way");
        foreach (Tally t in new[] { _crowd, _doors, _route })
            Check(t.Worst < 0.05f, $"nobody stands in anybody {t.What} ({t.Overlaps} ticks over 3 cm, worst {t.Worst:F2} m{(t.WorstAt != "" ? ": " + t.WorstAt : "")})");
        Check(_crowd.Gaps.Count > 10 && Median(_crowd.Gaps) >= 0.25f && Share(_crowd.Gaps, g => g < 0.1f) < 0.15f, $"a crowd keeps about an arm's length (nearest gap median {Median(_crowd.Gaps):F2} m, at least 0.25; {Share(_crowd.Gaps, g => g < 0.1f) * 100f:F0}% shoulder to shoulder, under 15%)");
        Check(Median(_crowd.Gaps) < 1.5f, $"and still stands as a crowd, not scattered (median {Median(_crowd.Gaps):F2} m, under 1.5)");
        Check(_route.Merged == 0, $"two on one way walk side by side or one behind the other, never as one body ({_route.Beside} side by side, {_route.Behind} behind, {_route.Merged} as one)");
        Check(_land.Seconds > 5f && _land.Worst < 0.05f && _land.Merged == 0, $"two walking home over the land are never in each other ({_land.Overlaps} ticks, worst {_land.Worst:F2} m, {_land.Merged} as one; {_land.Beside} beside, {_land.Behind} behind)");
        Check(_crowd.PerMinute < 2f, $"no twitching in a crowd: {_crowd.PerMinute:F2} heading flicks a person a minute (under 2; 12.8 before the fix)");
        Check(_doors.Seconds < 0.5f || _doors.PerMinute < 3f, $"nor in doorways: {_doors.PerMinute:F2} a person a minute (under 3, over {_doors.Seconds / 60f:F1} person-minutes; 8.6 before)");
        if (_herds == 0) { Check(false, "a herd watched grazing"); return; }
        Check(_herdWorst < 0.05f, $"no herd animal stands in another (deepest {_herdWorst:F2} m{(_herdWorstAt != "" ? ": " + _herdWorstAt : "")})");
        Check(Median(_calm) >= _herdR * 1.5f && Share(_calm, g => g < 0.15f) < 0.15f, $"a grazing herd spreads loosely (nearest gap median {Median(_calm):F2} m, at least 1.5 radii {_herdR * 1.5f:F2}; {Share(_calm, g => g < 0.15f) * 100f:F0}% touching, under 15%)");
        Check(Median(_alarmed) < Median(_calm), $"and bunches tighter alarmed (median {Median(_alarmed):F2} m against {Median(_calm):F2} grazing)");
        float added = Added();
        Check(float.IsNaN(added) || added < 0.1f, $"keeping a distance is cheap: {added:F3} ms more a tick for 100 bodies in a crowd than touching alone (a town all told {Avg(_townMs):F3} ms, wildlife {Avg(_wildMs):F3} ms)");
    }

    static float Avg(List<float> v) => v.Where(x => !float.IsNaN(x)).DefaultIfEmpty(float.NaN).Average();
    static float Quantile(List<float> v, float q) { var s = v.Where(x => !float.IsNaN(x)).OrderBy(x => x).ToList(); return s.Count == 0 ? float.NaN : s[Math.Min(s.Count - 1, (int)(q * s.Count))]; }

    /// <summary>What keeping a distance adds to keeping bodies apart, ms a tick: 100 bodies in a 12 m square, half walking, kept apart with and without it (NaN on a build without it).</summary>
    static float Added()
    {
        FieldInfo keep = typeof(PersonalSpace.Body).GetField("Keep");
        if (keep == null) return float.NaN;
        var rng = new System.Random();
        float Time(float k)
        {
            var space = new PersonalSpace();
            var bodies = Enumerable.Range(0, 100).Select(i => new Vector3(rng.NextSingle() * 12f, 0f, rng.NextSingle() * 12f)).ToList();
            long ticks = 0;
            for (int round = 0; round < 300; round++)
            {
                space.Bodies.Clear();
                for (int i = 0; i < bodies.Count; i++)
                {
                    object b = new PersonalSpace.Body { Pos = bodies[i], Radius = 0.27f, Give = 1f, Heading = i % 2 == 0 ? Vector3.Forward : Vector3.Zero };
                    keep.SetValue(b, k);
                    space.Bodies.Add((PersonalSpace.Body)b);
                }
                long from = Stopwatch.GetTimestamp();
                space.Resolve(1f / 60f, null, 4);
                if (round >= 50) ticks += Stopwatch.GetTimestamp() - from;
            }
            return (float)(ticks * 1000.0 / Stopwatch.Frequency / 250);
        }
        Time(0.3f);
        return Time(0.3f) - Time(0f);
    }
}
