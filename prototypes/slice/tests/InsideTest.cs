using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Wildlife;
using Int3 = PoiGenerator.Gen.Int3;

namespace Slice.Tests;

/// <summary>
/// A place walked into in the game (owner: "an ominous tower POI, and there were no spawns inside at all"): a fresh
/// world with one of a family seated near the start (--family, the Ominous Tower by default), held by an occupant
/// (--occ, else one drawn at random), the sleeper put at its way in and walked to a room on its storey and on each of
/// the next three up (or down), and at each stop the creatures and people spawned, stepped and drawn round them
/// counted. Every storey off the way in's has something on it (or is its keepers'), and nothing near or attacking
/// goes undrawn. --shots=dir: a picture inside, facing the nearest drawn threat or person.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes\slice -Scene res://tests/InsideTest.tscn
/// </summary>
public partial class InsideTest : Node
{
    const int WatchdogMinutes = 8;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
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

    static string Arg(string name) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith($"--{name}="))?[(name.Length + 3)..];

    /// <summary>Who may hold it when nobody says: people (each kind that lives in a place), nobody, beasts and machines.</summary>
    static readonly Occupant[] Holders = { Occupant.Squatters, Occupant.Scavengers, Occupant.Gang, Occupant.Cult, Occupant.Empty, Occupant.Prowlers, Occupant.Machines };

    IEnumerable<int> Scenario()
    {
        string family = Arg("family") ?? "ominous_tower";
        Occupant occ = Enum.TryParse(Arg("occ"), true, out Occupant o) ? o : Holders[_rng.Next(Holders.Length)];
        PoiSiting.ForceFamily = family;
        PoiSiting.ForceState = PoiState.Abandoned;
        PoiSiting.ForceOccupancy = new OccupancyRoll(occ);
        WorldChoice world = WorldChoice.Fresh("arid");
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        (PoiSiting.ForceFamily, PoiSiting.ForceState, PoiSiting.ForceOccupancy) = (null, null, null);
        game.Zone.Weather.Bridge.Calm = true;
        GD.Print($"-- {family} held by {occ}, {world} --");
        PoiSite site = game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == family);
        Check(site != null, $"a {family} is seated near the start");
        if (site == null) yield break;
        Vector3 Local(Int3 c) => game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X + c.X + 0.5f, site.Origin.Y + c.Y, site.Origin.Z + c.Z + 0.5f), game.Survivor.WorldFeet);
        game.Survivor.PlaceOutside((Local(site.Layout.EntranceCell) + Vector3.Up * 0.2f).ToNumerics());
        for (int i = 0; i < 3600 && game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
        Place place = game.Places.PlaceOf(site);
        Check(place is { Ready: true }, $"it stands as a structure round the sleeper at its way in");
        if (place is not { Ready: true }) yield break;
        PlaceFauna fauna = place.Memory.Fauna;
        Town town = place.Memory.Town;
        GD.Print($"  ({place.Inst.Layout.Modules.Count} modules, {fauna?.Rooms.Rooms.Count} rooms; population: {fauna?.Population?.Note ?? "none planned"}, {fauna?.Population?.Spawns.Count(PoiFauna.Threat)} threats planned, {fauna?.Population?.Deferred.Count} waiting; {town?.Data.People.Count ?? 0} people)");

        // A room on the way in's storey, then up (or down) the stairs a storey at a time: the nearest room by doors on each.
        var walk = new PlaceWalk(game, drive);
        var nav = place.Nav();
        List<PlacedModule> modules = place.Inst.Layout.Modules;
        int entry = modules[place.Inst.Layout.EntranceModule].Storey, way = modules.Max(m => m.Storey) > entry ? 1 : -1;
        Dictionary<int, int> hops = fauna?.Rooms is { } plan ? PoiFauna.Hops(plan) : new();
        var stops = (fauna?.Rooms.Rooms ?? new List<PoiRoom>()).Where(r => r.Kind is RoomKind.Room or RoomKind.Hall && hops.ContainsKey(r.Index) && (modules[r.Index].Storey - entry) * way >= 0)
            .GroupBy(r => modules[r.Index].Storey).OrderBy(g => Math.Abs(g.Key - entry)).Take(Arg("shots") != null ? 6 : 4).Select(g => g.OrderBy(r => hops[r.Index]).First()).ToList();
        // The storeys its keepers live on (their people's spots, their machines' posts) and one either side are theirs to keep clear.
        IEnumerable<int> posts = fauna?.Rooms.Holder == PlaceHolder.Machines ? place.Inst.Enemies.Select(e => e.Module) : place.Inst.Townsfolk?.Spots.Select(s => s.Module) ?? Enumerable.Empty<int>();
        var theirs = fauna?.Rooms.Holder is PlaceHolder.People or PlaceHolder.Machines ? posts.Where(i => i >= 0 && i < modules.Count).Select(i => modules[i].Storey).ToHashSet() : new HashSet<int>();
        // Nothing that comes at you is unseen: every blow on the sleeper is by something drawn.
        int blows = 0, unseen = 0;
        game.Zone.Wildlife.Attacked += e => { if (e.Hit) { blows++; if (!e.Creature.Shown) unseen++; } };
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        var best = (Seen: -1, Where: "");
        int bare = 0, hidden = 0, shot = 0;
        foreach (PoiRoom r in stops)
        {
            Int3 goal = new((int)r.FloorCenter.X, (int)MathF.Floor(r.Min.Y), (int)r.FloorCenter.Z);
            for (int k = 0; k < 9 && !nav.Standable(goal); k++) goal = new Int3(goal.X + (k % 3) - 1, goal.Y, goal.Z + (k / 3) - 1);
            bool ok = false;
            foreach (int x in walk.To(place, goal, v => ok = v)) { game.Suit.Vitals.Health = max; yield return x; }
            for (int i = 0; i < 90; i++) { game.Suit.Vitals.Health = max; yield return 0; }
            int storey = modules[r.Index].Storey - entry;
            var (line, there, seen, missed) = Count(game, place, r);
            GD.Print($"  {(ok ? "at" : "short of")} the {r.Purpose} on storey {storey} ({hops[r.Index]} doors in): {line}");
            if (storey != 0 && there == 0 && !theirs.Any(s => Math.Abs(s - modules[r.Index].Storey) <= 1)) bare++;
            hidden += missed;
            if (seen > best.Seen) best = (seen, $"{r.Purpose} on storey {storey}");
            // The picture: at the stop with the most drawn, facing the nearest in a clear line from the eye.
            if (Arg("shots") is { } dir && seen > shot && Nearest(place, r, place.FromWorld(game.Zone.Camera.GlobalPosition)) is Vector3 t)
            {
                shot = seen;
                foreach (int x in drive.LookAt(place.ToWorld(t + Vector3.Up * 0.7f), 40)) { game.Suit.Vitals.Health = max; yield return x; }
                string file = System.IO.Path.Combine(dir, $"inside-{family}-{occ}.png");
                GetViewport().GetTexture().GetImage().SavePng(file);
                GD.Print($"  (shot: {file}, storey {storey})");
            }
        }
        Check(stops.Count >= 2 && bare == 0, $"on each storey walked off the way in's ({stops.Count - 1}), something that comes at you or someone is on it within 30 m: {bare} bare; the most drawn at once {best.Seen}, in the {best.Where}");
        Check(hidden == 0 && unseen == 0, $"nothing near or attacking is left undrawn: {hidden} near you not drawn, {unseen} of {blows} blows by something not drawn");
        game.QueueFree();
        yield return 0;
    }

    /// <summary>
    /// What is round the sleeper: the place's creatures (all, within 30 m, stepped, drawn) and of the threats and people
    /// on the room's own storey, those drawn; and how many near or attacking are not drawn (DeepSite's distance).
    /// </summary>
    static (string Line, int There, int Seen, int Missed) Count(Game.Game game, Place place, PoiRoom room)
    {
        Vector3 feet = place.FromWorld(game.Survivor.WorldFeet), eye = place.FromWorld(game.Zone.Camera.GlobalPosition);
        bool OnStorey(Vector3 p) => p.Y > room.Min.Y - 1f && p.Y < room.Max.Y;
        var all = place.Memory.Fauna?.Sim?.Creatures.Where(c => !c.Removed && !c.Dead).ToList() ?? new List<Creature>();
        var near = all.Where(c => (c.Position - feet).Length() < 30f).ToList();
        var threats = near.Where(c => c.Species.Importance != Importance.Ambient && OnStorey(c.Position)).ToList();
        int missed = all.Count(c => Slice.Surface.DeepSite.MustDraw(c, WildSim.Flat(c.Position - eye).Length() + MathF.Abs(c.Position.Y - eye.Y) * 0.5f) && !c.Shown);
        var people = place.Memory.Town?.People.Where(p => !p.Dead && !p.Gone && (p.Pos - feet).Length() < 30f && OnStorey(p.Pos)).ToList() ?? new List<Town.Person>();
        int seen = threats.Count(c => c.Shown) + people.Count(p => p.View is { Visible: true });
        double step = place.Memory.Fauna?.Site?.LastStepMs ?? 0;
        return ($"{all.Count} creatures in the place, {near.Count} within 30 m, {near.Count(c => !c.Dormant)} stepped, {near.Count(c => c.Shown)} drawn; on this storey {threats.Count} threats ({threats.Count(c => c.Shown)} drawn) "
            + $"and {people.Count} people ({people.Count(p => p.View is { Visible: true })} drawn); {all.Count(c => !c.Dormant)} of the place's stepped, its sim step {step:F2} ms", threats.Count + people.Count, seen, missed);
    }

    /// <summary>The nearest drawn threat or person on a room's storey in a clear line from the eye, local; null if none is.</summary>
    static Vector3? Nearest(Place place, PoiRoom room, Vector3 eye)
    {
        bool OnStorey(Vector3 p) => p.Y > room.Min.Y - 1f && p.Y < room.Max.Y;
        BlockSpace space = PlaceFauna.SpaceOf(place.Memory);
        var points = (place.Memory.Fauna?.Sim?.Creatures.Where(c => c.Shown && !c.Removed && c.Species.Importance != Importance.Ambient).Select(c => c.Position) ?? Enumerable.Empty<Vector3>())
            .Concat(place.Memory.Town?.People.Where(p => p.View is { Visible: true }).Select(p => p.Pos) ?? Enumerable.Empty<Vector3>());
        return points.Where(p => OnStorey(p) && SpaceMath.Clear(space, eye, p + Vector3.Up * 0.8f, 0f)).OrderBy(p => (p - eye).Length()).Cast<Vector3?>().FirstOrDefault();
    }
}
