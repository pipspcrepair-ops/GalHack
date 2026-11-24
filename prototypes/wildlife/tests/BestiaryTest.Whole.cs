using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>The owner (9 Nov 2026): a man in a suit was missing bits: a gap at the neck, the elbows and knees, no hands, the shins apart from the boots.</summary>
public partial class BestiaryTest
{
    /// <summary>
    /// Fresh people of every kind a world makes (its townsfolk in every dress, its own kinds and
    /// settlers, on worlds of every era and climate), standing, walking and crouched: every limb
    /// chain is closed (each drawn bone reaches the next, the head sits on the neck, the shin on
    /// the boot) and every forearm ends in a hand, at full detail and once the finest parts drop.
    /// </summary>
    void BodiesAreWhole()
    {
        var people = new List<Species>();
        var roles = Enum.GetValues<PersonRole>();
        string[] falls = _data.Signature.Falls.Keys.ToArray();
        for (int w = 0; w < 10; w++)
        {
            WorldConditions world = World(_fresh.Range(-30f, 50f), alien: _fresh.Range(0f, 1f));
            world.Now = _fresh.Int(0, 2);
            world.Before = _fresh.Int(world.Now, 2);
            if (falls.Length > 0) world.Fall = _fresh.Pick(falls);
            WorldLife life = Bestiary.Roll(_data, world);
            for (int n = 0; n < 16; n++) people.Add(life.People.Person(Seed(), roles[n % roles.Length], founders: life.People.FoundersOf(Seed())));
            foreach (var k in life.Signature.Kinds) people.AddRange(k.Species.Hosts?.Take(3) ?? Enumerable.Empty<Species>());
            if (life.Settlers?.Hosts is { } settlers) people.AddRange(settlers.Take(3));
        }
        // Every dress on someone, the suit among them.
        string[] dresses = { "suit", "hazard", "furs", "coat", "shirt", "light", "rags", "wraps" };
        for (int i = 0; i < people.Count; i += 5) people[i].Figure.Dress = dresses[i / 5 % dresses.Length];
        people = people.Where(s => s.Figure != null && !s.Mechanical).ToList();

        int open = 0, handless = 0, checkedPeople = 0;
        string openWhat = "", handWhat = "";
        var tally = new Dictionary<string, int>();
        foreach (Species s in people)
        {
            Figure f = s.Figure;
            var rig = new HumanRig(s);
            checkedPeople++;
            var pieces = rig.Layout.Pieces;
            int Find(string name) => pieces.FindIndex(p => p.Name == name);
            int body = Find("body"), head = Find("head");
            foreach (int mode in new[] { 0, 1, 2 })
            {
                rig.Drive(new Gesture { Feet = Vector3.Zero, Speed = mode == 1 ? 1.3f : 0f, Walked = mode == 1 ? 0.6f : 0f, Crouch = mode == 2 ? 1f : 0f, Clock = 0.7f });
                string who = $"{f.Folk?.Id ?? "human"} {f.Dress} {f.Height:F2} m ({string.Join(" ", f.Wear)})";
                void Gap(string joint, float gap, float allow)
                {
                    if (gap <= allow) return;
                    open++;
                    openWhat = $"{joint} {gap * 100f:F1} cm on a {who}";
                    tally[$"{joint} {f.Dress}"] = tally.GetValueOrDefault($"{joint} {f.Dress}") + 1;
                }
                for (int side = 0; side < 2; side++)
                {
                    // Each limb bone's drawn length reaches the next joint.
                    foreach (var (from, to) in new[] { ($"arm{side}u", $"arm{side}l"), ($"leg{side}u", $"leg{side}l"), ($"leg{side}l", $"leg{side}f") })
                    {
                        int a = Find(from), b = Find(to);
                        if (a < 0 || b < 0) { Gap(from + " missing", 1f, 0f); continue; }
                        Gap(from[..3] + (to.EndsWith("f") ? " ankle" : to.StartsWith("arm") ? " elbow" : " knee"), Reach(pieces[a], rig.Pose[a]).DistanceTo(rig.Pose[b].Origin), 0.025f);
                    }
                    // A hand at the end of each forearm.
                    int fore = Find($"arm{side}l");
                                        int palm = Find(HandMake.PieceName(rig.HandOf(side).shape, side));
                    bool hand = fore >= 0 && palm >= 0 && pieces[palm].Prims.Count >= 3 && rig.Pose[palm].Basis.Y.LengthSquared() > 0.5f && Reach(pieces[fore], rig.Pose[fore]).DistanceTo(rig.Pose[palm].Origin) < 0.15f * f.ForearmOf(side);
                    if (!hand) { handless++; handWhat = who; }
                }
                // The head on the neck: the neck reaches down into the top of the body.
                Piece hp = pieces[head];
                float neckLow = hp.Prims.Count == 0 ? 1f : hp.Prims.Min(p => p.Shape == Shape.Taper ? p.Local.Origin.Y : p.Local.Origin.Y - p.Size.Y * 0.5f);
                Gap("neck", neckLow + rig.Pose[head].Origin.DistanceTo(rig.Pose[body].Origin + rig.Pose[body].Basis.Y.Normalized() * f.Torso), 0.01f);
            }
        }
        // Carried into the next patch, a person (or an animal) is the same one, so the body built for it still fits:
        // a kind of several bodies once came back as another of them, its old meshes on the new rig.
        int carried = 0, changed = 0, before = 0;
        var source = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
        var into = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
        for (int w = 0; w < 6; w++)
        {
            WorldConditions world = World(_fresh.Range(-10f, 40f));
            world.Now = _fresh.Int(0, 2);
            if (falls.Length > 0) world.Fall = _fresh.Pick(falls);
            WorldLife life = Bestiary.Roll(_data, world);
            var kinds = life.Signature.Kinds.Select(k => k.Species).Concat(life.Moving.Where(s => s.Mate != null).Take(3)).Where(s => s.Hosts is { Count: > 1 } || s.Mate != null);
            foreach (Species kind in kinds)
                for (int i = 0; i < 4; i++)
                {
                    Creature c = source.Spawn(kind, Vector3.Zero);
                    Creature next = into.Spawn(c.Species, Vector3.Zero, null, null, asIs: true);
                    carried++;
                    if (!CreatureView.SameBody(c, next)) changed++;
                    if (!CreatureView.SameBody(c, into.Spawn(c.Species, Vector3.Zero))) before++; // the old way: picked afresh
                }
        }
        Check(carried > 20 && changed == 0, $"carried across a patch, every one of {carried} people and animals keeps its own body ({changed} came back as another; picked afresh, {before} would have)");
        foreach (var (k, v) in tally.OrderByDescending(t => t.Value).Take(10)) GD.Print($"        {v,4}  {k}");
        Check(checkedPeople > 150, $"{checkedPeople} fresh people of every kind and dress");
        Check(open == 0, $"every limb chain is closed: elbows, knees, ankles and neck meet ({open} open{(open > 0 ? ": " + openWhat : "")})");
        Check(handless == 0, $"every forearm ends in a hand ({handless} without{(handless > 0 ? ": " + handWhat : "")})");
    }

    /// <summary>Where a limb piece's own bone, drawn, ends: the far end of its longest taper.</summary>
    static Vector3 Reach(Piece piece, Transform3D pose)
    {
        Prim best = default;
        float longest = -1f;
        foreach (Prim p in piece.Prims)
            if (p.Shape == Shape.Taper && p.Size.Y > longest) { longest = p.Size.Y; best = p; }
        if (longest < 0f) return pose.Origin + Vector3.Up * 100f;
        // The taper's far end, less its rounding (a joint meets where the rounded end is).
        return pose * (best.Local * new Vector3(0f, best.Size.Y, 0f));
    }
}
