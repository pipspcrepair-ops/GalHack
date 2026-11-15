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
/// (--occ, else one drawn at random), the sleeper put at its way in and walked through the rooms nearest it, and
/// at each stop the creatures and people spawned, simulated and drawn round them counted. --shots=dir: a picture
/// inside, facing the nearest drawn.
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

        // The rooms nearest the way in, on its storey: walked one after another.
        var walk = new PlaceWalk(game, drive);
        var nav = place.Nav();
        Dictionary<int, int> hops = fauna?.Rooms is { } plan ? PoiFauna.Hops(plan) : new();
        var stops = (fauna?.Rooms.Rooms ?? new List<PoiRoom>()).Where(r => r.Kind is not (RoomKind.Corridor or RoomKind.Vent or RoomKind.Duct) && hops.ContainsKey(r.Index))
            .OrderBy(r => hops[r.Index]).Take(5).ToList();
        var best = (Drawn: -1, Threats: 0, Where: "");
        foreach (PoiRoom r in stops)
        {
            Int3 goal = new((int)r.FloorCenter.X, (int)MathF.Floor(r.Min.Y), (int)r.FloorCenter.Z);
            for (int k = 0; k < 9 && !nav.Standable(goal); k++) goal = new Int3(goal.X + (k % 3) - 1, goal.Y, goal.Z + (k / 3) - 1);
            bool ok = false;
            foreach (int x in walk.To(place, goal, v => ok = v)) yield return x;
            foreach (int x in drive.Wait(90)) yield return x;
            var (line, drawn, threats) = Count(game, place);
            GD.Print($"  {(ok ? "at" : "short of")} {r.Purpose} ({r.Kind}, {hops[r.Index]} doors in): {line}");
            if (drawn > best.Drawn) best = (drawn, threats, r.Purpose);
            if (Arg("shots") is { } dir && drawn > 0 && best.Where == r.Purpose)
            {
                Vector3 feet = place.FromWorld(game.Survivor.WorldFeet);
                Vector3? target = Nearest(place, feet);
                if (target is Vector3 t) foreach (int x in drive.LookAt(place.ToWorld(t + Vector3.Up * 0.6f), 30)) yield return x;
                string file = System.IO.Path.Combine(dir, $"inside-{family}-{occ}.png");
                GetViewport().GetTexture().GetImage().SavePng(file);
                GD.Print($"  (shot: {file})");
            }
        }
        Check(best.Drawn > 0, $"in the {stops.Count} rooms nearest the way in, someone or something is there to be seen: at most {best.Drawn} drawn ({best.Threats} that can hurt you), in the {best.Where}");
        game.QueueFree();
        yield return 0;
    }

    /// <summary>What is round the sleeper: the place's creatures (all, within 30 m, stepped, drawn, threats among the drawn) and its people.</summary>
    static (string Line, int Drawn, int Threats) Count(Game.Game game, Place place)
    {
        Vector3 feet = place.FromWorld(game.Survivor.WorldFeet);
        var all = place.Memory.Fauna?.Sim?.Creatures.Where(c => !c.Removed && !c.Dead).ToList() ?? new List<Creature>();
        var near = all.Where(c => (c.Position - feet).Length() < 30f).ToList();
        int stepped = near.Count(c => !c.Dormant), drawn = near.Count(c => c.Shown), threats = near.Count(c => c.Shown && c.Species.Importance != Importance.Ambient);
        var people = place.Memory.Town?.People.Where(p => !p.Dead && !p.Gone).ToList() ?? new List<Town.Person>();
        int nearPeople = people.Count(p => (p.Pos - feet).Length() < 30f), drawnPeople = people.Count(p => (p.Pos - feet).Length() < 30f && p.View is { Visible: true });
        return ($"{all.Count} creatures in the place, {near.Count} within 30 m, {stepped} stepped, {drawn} drawn ({threats} threats); {people.Count} people, {nearPeople} within 30 m, {drawnPeople} drawn", drawn + drawnPeople, threats + drawnPeople);
    }

    /// <summary>The nearest drawn creature or person, local.</summary>
    static Vector3? Nearest(Place place, Vector3 feet)
    {
        var points = (place.Memory.Fauna?.Sim?.Creatures.Where(c => c.Shown && !c.Removed).Select(c => c.Position) ?? Enumerable.Empty<Vector3>())
            .Concat(place.Memory.Town?.People.Where(p => p.View is { Visible: true }).Select(p => p.Pos) ?? Enumerable.Empty<Vector3>());
        return points.OrderBy(p => (p - feet).Length()).Cast<Vector3?>().FirstOrDefault();
    }
}
