using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Arms;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Places;
using Slice.Surface;
using Wildlife;
using Int3 = PoiGenerator.Gen.Int3;

namespace Slice.Tests;

/// <summary>
/// The unseen attacker in the game (InsideTest --unseen; owner, 17 Nov: "invisible animals attacking me in a POI ... I think it
/// might occur when we're below the ground"). On fresh worlds (--worlds=N, 2 by default) a place that goes underground, held by
/// beasts, is walked round outside as you would come to it (the caves round you are looked for then), then walked into, a room
/// on each level in, the deepest four; then the same for the world's nearest other place, as rolled. Every blow on the sleeper, from any sim, is logged with whether (a) its body is built, shown and
/// posed within half a metre of where the sim has it, (b) a line through the world's collision is clear from its striking part
/// to the sleeper's middle (and, for a body living in a cave's or a place's space, from its middle to that part), and (c) the
/// shot sweep from the sleeper's eye meets it. None may fail; nothing of a cave's or a tunnel's sim is ever in a place's dug
/// rooms; and nothing shown within 14 m of the eye is drawn over a metre from where it is. --shots=dir: up to three pictures of
/// attackers below ground.
/// </summary>
public partial class InsideTest
{
    const int UnseenMinutes = 28;
    static readonly Occupant[] Beasts = { Occupant.Empty, Occupant.Nesters, Occupant.Prowlers };

    sealed class BlowLog
    {
        public int Blows, Below, Hidden, Through, Unshot, Shots;
        public readonly HashSet<Creature> Dug = new(), Misposed = new();
        public readonly List<string> Examples = new();
        public void Note(string what) { if (Examples.Count < 8) Examples.Add(what); }
    }

    IEnumerable<int> Unseen()
    {
        int worlds = int.TryParse(Arg("worlds"), out int n) ? n : 2;
        var deep = SurfaceZone.PoiCatalog.Library.Families.Where(f => f.Underground && f.Types.Count == 0 && !f.Space && !WorldProfile.Unbuilt(f) && f.StateWeights.Any(w => w > 0f)).Select(f => f.Id).ToList();
        GD.Print($"-- the unseen attacker: {worlds} fresh worlds, each with one of the {deep.Count} families that go underground --");
        string[] lands = { "arid", "temperate" };
        var log = new BlowLog();
        for (int w = 0; w < worlds; w++)
        {
            var stood = new Stood();
            foreach (int x in Stand(Arg("family") ?? deep[_rng.Next(deep.Count)], Beasts[_rng.Next(Beasts.Length)], lands[_rng.Next(lands.Length)], stood)) yield return x;
            if (stood.Place != null) foreach (int x in UnseenWalk(stood, log)) yield return x;
            // And the nearest other place of the world, whatever it is and whoever holds it, as rolled.
            Vector3 feet = stood.Game.Survivor.WorldFeet;
            PoiSite next = stood.Game.Zone.Planet.Pois.Where(p => p != stood.Site && p.Layout != null)
                .OrderBy(p => stood.Game.Zone.Planet.NearestCopy(new Vector3((float)p.Center.X, feet.Y, (float)p.Center.Z), feet).DistanceTo(feet)).FirstOrDefault();
            if (next != null) foreach (int x in Arrive(stood, next)) yield return x;
            if (next != null && stood.Place != null) foreach (int x in UnseenWalk(stood, log)) yield return x;
            stood.Game.QueueFree();
            for (int i = 0; i < 10; i++) yield return 0;
        }
        foreach (string e in log.Examples) GD.Print($"    e.g. {e}");
        Check(log.Blows > 0, $"blows on the sleeper logged: {log.Blows}, {log.Below} of them below ground");
        Check(log.Hidden + log.Through + log.Unshot == 0, $"every blow by something drawn where it is, through nothing solid, and that a shot can meet: of {log.Blows}, {log.Hidden} not (a), {log.Through} not (b), {log.Unshot} not (c)");
        Check(log.Dug.Count == 0, $"nothing of a cave's or a tunnel's sim in a place's dug rooms: {log.Dug.Count}");
        Check(log.Misposed.Count == 0, $"nothing shown within 14 m of the eye drawn over a metre from where it is: {log.Misposed.Count}");
    }

    IEnumerable<int> UnseenWalk(Stood s, BlowLog log)
    {
        Game.Game game = s.Game;
        Driver drive = s.Drive;
        Place place = s.Place;
        WildlifePatch patch = game.Zone.Wildlife;
        PlanetSurface.World.PlanetWorld planet = game.Zone.Planet;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        PlaceFauna fauna = place.Memory.Fauna;
        string shots = Arg("shots");
        Creature subject = null;

        void Blow(AttackEvent e)
        {
            if (!e.Hit || e.Damage <= 0f) return;
            Creature c = e.Creature;
            log.Blows++;
            DeepSite site = patch.Sites.FirstOrDefault(d => d.Sim.Creatures.Contains(c));
            patch.SimOf(c, out Vector3 origin, out _);
            CreatureView view = site != null ? site.ViewOf(c) : patch.View.ViewOf(c);
            Vector3 feet = game.Survivor.WorldFeet, chest = feet + Vector3.Up * (game.Survivor.Crouched ? 0.6f : 0.95f), eye = game.Survivor.WorldEye();
            Vector3 body = origin + Interpenetration.Middle(c), part = origin + e.Part;
            bool below = feet.Y < game.Zone.GroundAt(feet.X, feet.Z).Y - 2.5f;
            if (below) log.Below++;
            string who = $"a {c.Species.CommonName} of the {site?.Name.ToString() ?? "patch"} {body.DistanceTo(chest):F1} m off, {(below ? "below ground" : "above")} in {s.Site.Family.Id}";
            float stale = view == null ? 0f : view.DrawnAt.DistanceTo(c.Position), off = PosedOff(c);
            bool drawn = view != null && view.IsVisibleInTree() && stale < 0.5f && off < 0.5f;
            bool clear = patch.SightClear(part, chest) && (c.Space == null || patch.SightClear(body, part));
            bool shot = Shootable(game, c, origin, eye, out string met);
            if (!drawn) log.Hidden++;
            if (!clear) log.Through++;
            if (!shot) log.Unshot++;
            string a = drawn ? "ok" : $"FAIL ({(view == null ? "no body built" : !view.IsVisibleInTree() ? "its body hidden" : $"drawn where it was {stale:F1} m back, posed {off:F1} m from it")})";
            GD.Print($"    blow {log.Blows}: {who}: (a) {a}, (b) {(clear ? "ok" : $"FAIL (struck from {part})")}, (c) {(shot ? "ok" : $"FAIL (a shot at it meets {met})")}");
            if (below && drawn && shot) subject = c;
        }

        // What lives in a cave or a tunnel never comes into a place's dug rooms: their walls and floors aren't in its rock.
        void Intruders()
        {
            foreach (DeepSite d in patch.Sites)
            {
                if (d.GetParent() is Place) continue; // a place's own (this one's, or one walked before)
                foreach (Creature c in d.Sim.Creatures)
                {
                    Vector3 at = d.World(Interpenetration.Middle(c));
                    if (c.Removed || c.Dead || !planet.Source.Dug(Mathf.FloorToInt(at.X), Mathf.FloorToInt(at.Y), Mathf.FloorToInt(at.Z)) || !log.Dug.Add(c)) continue;
                    log.Note($"a {c.Species.CommonName} of the {d.Name} in {s.Site.Family.Id}'s dug rooms at {at}");
                }
            }
        }

        // What is shown near you is drawn where it is (a rig posed on the wrong floor puts a body a storey or the hill away).
        void Posed()
        {
            Vector3 eye = game.Survivor.WorldEye();
            foreach (DeepSite d in patch.Sites)
                foreach (Creature c in d.Sim.Creatures)
                    if (!c.Removed && !c.Dead && c.Shown && d.World(c.Position).DistanceTo(eye) < 14f && PosedOff(c) > 1f && log.Misposed.Add(c))
                        log.Note($"a {c.Species.CommonName} ({c.Species.Rig} rig) of the {d.Name} drawn {PosedOff(c):F1} m from where it is in {s.Site.Family.Id}");
        }

        patch.Attacked += Blow;
        // Round it outside, looking in, as you would come to it.
        Vector3 door = game.Survivor.WorldFeet;
        PoiSite site0 = s.Site;
        Vector3 middle = planet.NearestCopy(new Vector3((float)site0.Center.X, door.Y, (float)site0.Center.Z), door);
        float radius = MathF.Max(site0.Stamp.MaxX - site0.Stamp.MinX, site0.Stamp.MaxZ - site0.Stamp.MinZ) * 0.5f + 8f;
        for (int k = 0; k < 4; k++)
        {
            Vector3 at = middle + new Vector3(MathF.Cos(k * MathF.PI / 2f), 0f, MathF.Sin(k * MathF.PI / 2f)) * radius;
            at.Y = game.Zone.GroundAt(at.X, at.Z).Y;
            foreach (int x in drive.WalkTo(at, 2f, 20f, sprint: true, dig: false, hop: true)) { game.Suit.Vitals.Health = max; yield return x; }
            for (int i = 0; i < 150; i++) { game.Suit.Vitals.Health = max; drive.Face(middle + Vector3.Down * 6f); yield return 0; }
            Intruders();
            Posed();
        }
        GD.Print($"  round it outside: {game.Caves.Found} caves filled so far, {patch.Sites.Count} sites standing");

        // In, the roomiest room on each level in (the deepest four, down or up), shallowest first: a walk, or a step there where the walk can't go.
        game.Survivor.PlaceOutside((door + Vector3.Up * 0.2f).ToNumerics());
        var walk = new PlaceWalk(game, drive);
        var stops = (fauna?.Rooms.Rooms ?? new List<PoiRoom>()).Where(r => r.Size.Y >= 2f && !r.Yard)
            .GroupBy(r => r.Level).OrderByDescending(g => g.Key).Take(4).Reverse().Select(g => g.OrderByDescending(r => r.Size.X * r.Size.Z).First()).ToList();
        var nav = place.Nav();
        bool torch = false;
        foreach (PoiRoom r in stops)
        {
            Int3 goal = new((int)r.FloorCenter.X, (int)MathF.Floor(r.Min.Y), (int)r.FloorCenter.Z);
            for (int k = 0; k < 9 && !nav.Standable(goal); k++) goal = new Int3(goal.X + (k % 3) - 1, goal.Y, goal.Z + (k / 3) - 1);
            bool ok = false;
            foreach (int x in walk.To(place, goal, v => ok = v)) { game.Suit.Vitals.Health = max; yield return x; }
            if (!ok) game.Survivor.PlaceOutside(place.ToWorld(new Vector3(goal.X + 0.5f, goal.Y + 0.1f, goal.Z + 0.5f)).ToNumerics());
            int before = log.Blows;
            for (int i = 0; i < 600; i++)
            {
                game.Suit.Vitals.Health = max;
                if (i % 30 == 0) { Intruders(); Posed(); }
                yield return 0;
            }
            GD.Print($"  {(ok ? "at" : "put in")} the {r.Purpose} {r.Kind} {r.Level} in, storey {r.Storey}: {log.Blows - before} blows; {patch.Sites.Count} sites standing, {game.Caves.Found} caves");
            // A picture of what came at you below ground, facing it, the torch on.
            if (shots != null && subject is { Removed: false, Dead: false } c && log.Shots < 3)
            {
                if (!torch) { torch = true; foreach (int x in drive.Tap(Key.L)) yield return x; }
                for (int i = 0; i < 30; i++) { game.Suit.Vitals.Health = max; drive.Face(patch.WorldOf(c) + Vector3.Up * 0.4f); yield return 0; }
                string file = System.IO.Path.Combine(shots, $"unseen-{s.Site.Family.Id}-{++log.Shots}.png");
                GetViewport().GetTexture().GetImage()?.SavePng(file);
                GD.Print($"  (shot: {file}, a {c.Species.CommonName}, storey {r.Storey})");
                subject = null;
            }
        }
        patch.Attacked -= Blow;
    }

    /// <summary>How far its posed body (the rig's hit spheres, what is drawn and shot at) is from its middle where the sim has it, m.</summary>
    static float PosedOff(Creature c)
    {
        if (c.Rig is not { HitCount: > 0 } rig) return 0f;
        Vector3 mid = Interpenetration.Middle(c);
        float best = float.MaxValue;
        for (int i = 0; i < rig.HitCount; i++)
        {
            Vector4 h = rig.Hits[i];
            best = MathF.Min(best, MathF.Max(0f, new Vector3(h.X, h.Y, h.Z).DistanceTo(mid) - h.W));
        }
        return best;
    }

    /// <summary>Whether the shot sweep from the eye meets a creature (or another body in front of it), aimed at each of its hit spheres in turn (any will do); what it met if not.</summary>
    static bool Shootable(Game.Game game, Creature c, Vector3 origin, Vector3 eye, out string met)
    {
        met = "nothing";
        if (game.Arms == null) { met = "no arms"; return false; }
        int n = Math.Max(1, c.Rig?.HitCount ?? 0);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = origin + (c.Rig is { HitCount: > 0 } rig ? new Vector3(rig.Hits[i].X, rig.Hits[i].Y, rig.Hits[i].Z) : Interpenetration.Middle(c));
            Vector3 to = p + (p - eye).Normalized() * 0.5f;
            if (!game.Arms.Sweep(eye.ToNumerics(), to.ToNumerics(), Slice.Arms.ArmsHost.PlayerThing, 0, 0f, out ShotHit hit)) continue;
            if (hit.Thing == c || hit.Kind is HitKind.Creature or HitKind.Person) return true; // (another body in the way is no wall)
            met = $"{hit.Kind} {hit.Distance:F1} m out ({(p - eye).Length():F1} m to it)";
        }
        return false;
    }
}
