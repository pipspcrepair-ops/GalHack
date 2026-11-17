using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Steep ground (OccupantTest --paths; owner: "what about sharp inclines or cliffs?", "wall climbers should be able to go
/// up and down those, but not a person or regular animal"): fresh ground of one of three forms, rolled anew each run (a
/// mesa with one ramp up its cliff band, a canyon with one ramp down and up its walls, steep ridged mountains), with
/// grazers, hunters and a band of people on it, and an intruder up on top or across the gap whom the hunters are set on.
/// Walkers never stand on a face steeper than their legs take (<see cref="Brain.GaitOf"/>) for more than about a second,
/// never go off an edge higher than they drop, and a hunter goes round a cliff band by the easy way, never up its face.
/// </summary>
public partial class OccupantTest
{
    sealed class SteepTally
    {
        public int Grounds, Walkers, Episodes, Falls, Hunters, Round, Waited;
        public float OnFace, Longest, WalkerSeconds;
        public string LongestAt = "", FellAt = "", HunterAt = "";
        public readonly List<string> Forms = new();
    }

    sealed class Footing
    {
        public float Face, Drop, PrevH;
        public Vector3 Prev;
        public bool Fell;
    }

    void SteepWalk(WorldLife life, PathTally t, SteepTally s, int world)
    {
        const float half = 90f;
        var rng = Wildlife.Rng.For((ulong)_rng.NextInt64(), "steep");
        string form = new[] { "mesa", "canyon", "mountains" }[world % 3]; // (each form every run, its own shape rolled fresh)
        Func<float, float, float> height;
        Vector3 top, foot;
        static float Smooth(float x) { x = Math.Clamp(x, 0f, 1f); return x * x * (3f - 2f * x); }
        // How much of a ramp a bearing is on: 1 down its middle, 0 a little off it.
        static float Ramp(float a, float at, float width) => Math.Clamp(1f - MathF.Abs(Geo.WrapAngle(a - at)) / width, 0f, 1f);
        switch (form)
        {
            case "mesa":
            {
                // A flat top ringed by a cliff a metre and a half wide, but for one ramp thirty metres long.
                float r = rng.Range(16f, 26f), h = rng.Range(8f, 14f), ramp = rng.Range(-MathF.PI, MathF.PI);
                var c = new Vector3(rng.Range(-12f, 12f), 0f, rng.Range(-12f, 12f));
                height = (x, z) =>
                {
                    float d = new Vector2(x - c.X, z - c.Z).Length(), a = MathF.Atan2(z - c.Z, x - c.X);
                    float wide = 1.5f + 28.5f * Ramp(a, ramp, 0.35f);
                    return h * Smooth((r + wide - d) / wide);
                };
                top = c;
                foot = c - new Vector3(MathF.Cos(ramp), 0f, MathF.Sin(ramp)) * (r + 12f);
                break;
            }
            case "canyon":
            {
                // A trench ten metres wide at the bottom with sheer walls, but for one ramp down and up its sides.
                float a = rng.Range(0f, MathF.PI), depth = rng.Range(10f, 14f), at = rng.Range(-30f, 30f);
                var along = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
                var across = new Vector3(-along.Z, 0f, along.X);
                height = (x, z) =>
                {
                    var p = new Vector3(x, 0f, z);
                    float e = MathF.Abs(p.Dot(across)), u = p.Dot(along);
                    float wide = 1.5f + 23.5f * Math.Clamp(1f - MathF.Abs(u - at) / 8f, 0f, 1f);
                    return -depth * (1f - Smooth((e - 5f) / wide));
                };
                top = across * 22f + along * (at + 30f);
                foot = -across * 22f + along * (at + 30f);
                break;
            }
            default:
            {
                // Ridged mountains: knife-edge ridges and steep flanks, valleys and passes between.
                float ox = rng.Range(-500f, 500f), oz = rng.Range(-500f, 500f), k = rng.Range(0.025f, 0.035f);
                height = (x, z) =>
                {
                    float n = MathF.Sin((x + ox) * k) * MathF.Cos((z + oz) * k * 0.8f) + 0.5f * MathF.Sin((x - z + ox) * k * 1.9f);
                    return 24f * (1f - MathF.Abs(n) / 1.5f);
                };
                top = new Vector3(rng.Range(-40f, 40f), 0f, rng.Range(-40f, 40f));
                foot = new Vector3(rng.Range(-40f, 40f), 0f, rng.Range(-40f, 40f));
                break;
            }
        }
        var terrain = new Wildlife.Terrain(height, half);
        var nav = new Wildlife.NavGrid(half, 3f);
        var sim = new WildSim(_data, terrain, nav);
        s.Grounds++;
        s.Forms.Add(form);
        var walkers = life.Fauna.Species.Where(k => k.Rig == RigKind.Legged && !k.Flies && !k.Mechanical && !k.Climbs && k.Domestic == DomesticKind.None && k.Body.Size < 2.5f).ToList();
        if (walkers.Count == 0) return;
        // On ground its legs stand on, near a point.
        Vector3 Near(Vector3 at, float spread)
        {
            for (int k = 0; k < 200; k++)
            {
                Vector3 p = terrain.Clamp(at + new Vector3(rng.Range(-spread, spread), 0f, rng.Range(-spread, spread)), 12f);
                if (!sim.Steep(p)) return p;
            }
            return at;
        }
        Species grazer = walkers[rng.Int(0, walkers.Count - 1)].Resized(1f);
        grazer.Behaviour = Behaviour.Grazer;
        Species hunter = walkers[rng.Int(0, walkers.Count - 1)].Resized(1.2f);
        hunter.Behaviour = Behaviour.Predator;
        hunter.Temperament = Temperament.Aggressive;
        hunter.Stats.Hunts = true;
        hunter.Stats.Damage = 1f;
        hunter.Stats.Retaliates = true;
        hunter.Stats.Perception = MathF.Max(hunter.Stats.Perception, 90f);
        Species folk = life.People.Person((ulong)_rng.NextInt64(), PersonRole.Scavenger, Occupancy.Friendly);
        sim.SpawnGroup(grazer, Near(Vector3.Zero, 40f), 6);
        sim.SpawnGroup(folk, Near(Vector3.Zero, 40f), 4);
        var hunters = new List<Creature> { sim.Spawn(hunter, Near(foot, 4f)), sim.Spawn(hunter, Near(foot, 4f)) };
        var you = new DummyTarget { Name = "intruder", Position = Near(top, 3f) };
        you.Position = you.Position with { Y = terrain.Height(you.Position.X, you.Position.Z) };
        sim.Targets.Add(you);
        var watch = new Dictionary<Creature, Watch>();
        var footing = new Dictionary<Creature, Footing>();
        var closest = hunters.ToDictionary(h => h, h => WildSim.Flat(h.Position - you.Position).Length());
        // The way round on 1 m cells for a walker's legs (45 degrees, a 1.5 m drop): how far a fleeing body is from what it fled.
        Func<int, int, int, int, bool> legs = (ax, az, bx, bz) =>
        {
            float rise = terrain.Height(bx + 0.5f, bz + 0.5f) - terrain.Height(ax + 0.5f, az + 0.5f), run = ax != bx && az != bz ? 1.414f : 1f;
            return MathF.Abs(rise) <= run || rise < 0f && -rise <= 1.5f;
        };
        int searches = nav.Searches;
        for (int step = 0; step < 60 * 30; step++)
        {
            // The hunters are set on the intruder; the grazers and the band are frightened off now and then.
            if (step % 150 == 0)
                foreach (Creature h in hunters)
                    if (!h.Dead && !h.Removed) { h.Noticed = you; h.LastSeen = you.Position; sim.Damage(h, 0.01f, you); }
            if (step % 210 == 100)
                foreach (Creature c in sim.Creatures)
                    if (!c.Dead && !c.Removed && !hunters.Contains(c) && WildSim.Flat(c.Position - you.Position).Length() < 40f) sim.Scare(c, you.Position, 5f);
            you.Health.Restore();
            Stepped(sim, t);
            Watched(sim, t, watch, you, form, (c, from) => FlatRoute((x, z) => false, c.Position, from, 80f, null, legs) ?? float.PositiveInfinity);
            Apart(sim, t, watch, form);
            foreach (Creature c in sim.Creatures)
            {
                if (c.Removed || c.Dead || c.Thrown) continue;
                NavGrid.Gait g = Brain.GaitOf(c.Species);
                if (g.Any) continue;
                if (!footing.TryGetValue(c, out Footing f)) footing[c] = f = new Footing { Prev = c.Position, PrevH = c.Position.Y };
                s.WalkerSeconds += PathDt;
                // On a face: the ground under it steeper than its legs take.
                Vector3 n = terrain.Normal(c.Position.X, c.Position.Z);
                bool face = new Vector2(n.X, n.Z).Length() > n.Y * g.Slope * 1.05f;
                if (face)
                {
                    f.Face += PathDt;
                    s.OnFace += PathDt;
                    if (f.Face > 1f && f.Face - PathDt <= 1f) s.Episodes++;
                    if (f.Face > s.Longest) { s.Longest = f.Face; s.LongestAt = $"; e.g. {form}: {c.Species.CommonName} ({(c.Species.Figure != null ? "person" : "animal")}, {c.State}) at {c.Position.X:F1},{c.Position.Y:F1},{c.Position.Z:F1}, slope {new Vector2(n.X, n.Z).Length() / n.Y:F2} against its {g.Slope:F2}"; }
                }
                else f.Face = 0f;
                // Off an edge: a run of steps down steeper than its legs take, adding up to more than it drops.
                float down = f.PrevH - c.Position.Y, run = WildSim.Flat(c.Position - f.Prev).Length();
                if (run > 0.6f) f.Drop = 0f; // (put back where it last went well: no fall)
                else if (down > 0f && down > g.Slope * run * 1.2f) f.Drop += down;
                else f.Drop = 0f;
                if (f.Drop > g.Drop && !f.Fell)
                {
                    f.Fell = true;
                    s.Falls++;
                    if (s.FellAt == "") s.FellAt = $"; e.g. {form}: {c.Species.CommonName} ({c.State}) {f.Drop:F1} m down at {c.Position.X:F1},{c.Position.Y:F1},{c.Position.Z:F1}, its drop {g.Drop:F1} m";
                }
                if (f.Drop == 0f) f.Fell = false;
                (f.Prev, f.PrevH) = (c.Position, c.Position.Y);
            }
            if (step % 150 == 149 && form != "mountains" && world < 2)
                foreach (Creature h in hunters)
                    GD.Print($"      HUNTER {form} {sim.Time:F0}s #{h.Id}: {h.State} {h.Phase}, {WildSim.Flat(h.Position - you.Position).Length():F1} m off, at {h.Position.X:F1},{h.Position.Y:F1},{h.Position.Z:F1}, wants {h.Desired.Length():F1}, way {h.PathIndex}/{h.Path.Count}{(h.PathWhole ? "" : " short")}, provoked {h.ProvokedBy != null && sim.Time - h.ProvokedAt < 12f}, noticed {h.Noticed?.Name}, herd {h.Herd != null}, pack {h.Pack != null}, rest {h.Rest != null}, beh {h.Species.Behaviour}, husk {h.Species.Husk}, stuck {h.StuckFor:F1}, next {(h.PathIndex < h.Path.Count ? h.Path[h.PathIndex] : h.Position)}, speed {WildSim.Flat(h.Velocity).Length():F1}, goal {h.PathGoal}");
            foreach (Creature h in hunters)
                if (!h.Removed) closest[h] = MathF.Min(closest[h], WildSim.Flat(h.Position - you.Position).Length());
        }
        s.Walkers += footing.Count;
        foreach (Creature h in hunters)
        {
            if (h.Removed || form == "mountains") continue; // (a chase across a cliff band: the mesa's and the canyon's, each with its one way round)
            s.Hunters++;
            // Round by the easy way to the intruder, or held at the edge of what it can't climb (never up the face).
            if (closest[h] < Attacks.Range(h) + 2.5f) s.Round++;
            else
            {
                s.Waited++;
                var probe = new List<Vector3>();
                bool way = nav.FindPath(h.Position, you.Position, probe, 4000, Brain.GaitOf(h.Species));
                if (s.HunterAt == "") s.HunterAt = $"; e.g. {form}: a hunter came no nearer than {closest[h]:F1} m, at {h.Position.X:F1},{h.Position.Y:F1},{h.Position.Z:F1} ({h.State}, noticed {h.Noticed?.Name ?? "none"}, wanting {h.Desired.Length():F1} m/s, its way {h.PathIndex}/{h.Path.Count}{(h.PathWhole ? "" : " short")}), the intruder at {you.Position.X:F1},{you.Position.Y:F1},{you.Position.Z:F1}; the grid has {(way ? $"a way of {probe.Count} points" : "no way")}";
            }
        }
        t.Places++;
        t.Searches += nav.Searches - searches;
        (t.Repaths, t.Unstuck) = (t.Repaths + sim.Repaths, t.Unstuck + sim.Unstuck);
    }

    /// <summary>
    /// Out of reach (owner: "what happens if the player moves to a position a chasing enemy can't reach? Does the enemy stop their
    /// pursuit and then give up?"): an intruder up on a sheer pillar no walker gets up, hunters set on it. The walkers go to the
    /// nearest spot they can get to, give up within their time (Stats.GiveUp) and leave; then it steps down beside the pillar and
    /// one comes back within its memory; a climber, if the world has one, gets up to it.
    /// </summary>
    void Ledge(WorldLife life)
    {
        const float half = 70f, r = 5f, h = 9f;
        var rng = Wildlife.Rng.For((ulong)_rng.NextInt64(), "ledge");
        var terrain = new Wildlife.Terrain((x, z) => h * Math.Clamp((r + 1.2f - MathF.Sqrt(x * x + z * z)) / 1.2f, 0f, 1f), half);
        var sim = new WildSim(_data, terrain, new Wildlife.NavGrid(half, 3f));
        Species Hunter(Species s)
        {
            s = s.Resized(1.1f);
            (s.Behaviour, s.Temperament, s.Stats.Hunts, s.Stats.Damage, s.Stats.Retaliates) = (Behaviour.Predator, Temperament.Aggressive, true, 1f, true);
            s.Stats.Perception = MathF.Max(s.Stats.Perception, 90f);
            s.Stats.GiveUp = 15f * 1.3f; // (an aggressive predator's, by the data)
            return s;
        }
        var walkers = life.Fauna.Species.Where(k => k.Rig == RigKind.Legged && !k.Flies && !k.Mechanical && !k.Climbs && k.Domestic == DomesticKind.None && k.Body.Size < 2.5f).ToList();
        if (walkers.Count == 0) return;
        // A wall climber: the world's own, or (a world without one) one of its walkers made a climber, so this is always looked at.
        Species climber = life.Fauna.Species.FirstOrDefault(k => k.Climbs && !k.Flies && !k.Mechanical && k.Domestic == DomesticKind.None);
        if (climber == null) (climber = walkers[rng.Int(0, walkers.Count - 1)].Resized(1f)).Climbs = true;
        var you = new DummyTarget { Name = "intruder", Position = new Vector3(0f, h, 0f) };
        sim.Targets.Add(you);
        var hunters = new List<Creature>();
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3f + rng.Range(-0.4f, 0.4f);
            hunters.Add(sim.Spawn(Hunter(walkers[rng.Int(0, walkers.Count - 1)]), new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * 22f));
        }
        Creature up = climber != null ? sim.Spawn(Hunter(climber), new Vector3(0f, 0f, -20f)) : null;
        var gaveAt = new Dictionary<Creature, float>();
        var gaveFrom = new Dictionary<Creature, float>();
        var after = new Dictionary<Creature, float>();
        float nearest = float.MaxValue, climbed = float.MaxValue, downAt = -1f;
        int back = 0;
        for (int step = 0; step < 110 * 30; step++)
        {
            // Shot at now and then all along (a hurt is no reason to keep after someone it can't get to).
            if (step % 150 == 0)
                foreach (Creature c in sim.Creatures) { c.Noticed = you; c.LastSeen = you.Position; sim.Damage(c, 0.01f, you); }
            sim.Step(PathDt);
            foreach (Creature c in hunters)
            {
                if (c.Removed || c.Dead) continue;
                float d = WildSim.Flat(c.Position - you.Position).Length();
                if (downAt < 0f) nearest = MathF.Min(nearest, d);
                if (!gaveAt.ContainsKey(c) && c.GaveUpOn == you) (gaveAt[c], gaveFrom[c]) = (sim.Time, d);
                if (gaveAt.TryGetValue(c, out float at) && downAt < 0f && sim.Time - at <= 10f) after[c] = d;
            }
            if (up != null && !up.Removed && downAt < 0f) climbed = MathF.Min(climbed, (up.Position - you.Position).Length());
            // Every walker given up a while (or a minute gone): the intruder steps down beside the pillar, within their memory.
            if (downAt < 0f && (gaveAt.Count == hunters.Count && sim.Time - gaveAt.Values.Max() > 10f || sim.Time > 60f))
            {
                downAt = sim.Time;
                you.Position = new Vector3(0f, 0f, r + 4f);
            }
            if (downAt >= 0f && back == 0)
                foreach (Creature c in hunters)
                    if (!c.Removed && !c.Dead && (c.State == CState.Chase && c.Noticed == you || WildSim.Flat(c.Position - you.Position).Length() < Attacks.Range(c) + 1.5f)) back++;
            you.Health.Restore();
        }
        int left = hunters.Count(c => gaveAt.ContainsKey(c) && after.TryGetValue(c, out float a) && a > gaveFrom[c] + 3f);
        string times = string.Join(", ", hunters.Select(c => gaveAt.TryGetValue(c, out float t) ? $"{t:F0} s ({c.Stats.GiveUp:F0} s its time, {gaveFrom[c]:F1} m off, {(after.TryGetValue(c, out float a) ? a : 0f):F1} m 10 s on)" : $"never ({c.State}, {c.Stats.GiveUp:F0} s its time, {WildSim.Flat(c.Position - you.Position).Length():F1} m off, way {c.PathIndex}/{c.Path.Count}{(c.PathWhole ? "" : " short")}, unreached {c.Unreached:F1} s)"));
        GD.Print($"    out of reach on a pillar {h:F0} m tall: walkers gave up at {times}; nearest any came {nearest:F1} m; {(up != null ? $"the climber came within {climbed:F1} m" : "no climber in this world")}");
        Check(gaveAt.Count == hunters.Count && hunters.All(c => gaveAt[c] <= c.Stats.GiveUp + 15f), $"out of reach: walkers set on someone up a pillar they can't climb give up within their time ({gaveAt.Count} of {hunters.Count})");
        Check(nearest < r + 6f && gaveFrom.Values.All(d => d < r + 9f), $"out of reach: they wait at the nearest spot they can get to till then (nearest {nearest:F1} m, the pillar {r:F0} m round)");
        Check(left >= hunters.Count - 1, $"out of reach: having given up, they leave ({left} of {hunters.Count} 3 m farther off within 10 s)");
        Check(back >= 1, $"out of reach: stepped down within their memory, one comes back for it ({back})");
        if (up != null) Check(climbed < Attacks.Range(up) + 2f, $"out of reach: a climber gets up to it ({climbed:F1} m at nearest)");
    }

    void SteepChecks(SteepTally s)
    {
        GD.Print($"    on steep ground ({string.Join(", ", s.Forms)}): {s.Walkers} walkers, {s.WalkerSeconds:F0} walker-seconds, {s.OnFace:F1} s of it on faces steeper than their legs take");
        Check(s.Episodes == 0 && s.Longest <= 1.2f, $"on steep ground: no walker stands on a face steeper than its legs take for more than a second ({s.Episodes} times over; longest {s.Longest:F1} s){s.LongestAt}");
        Check(s.Falls == 0, $"on steep ground: no walker goes off an edge higher than it drops ({s.Falls} falls){s.FellAt}");
        Check(s.Hunters >= 3 && s.Round >= s.Hunters * 0.8f, $"on steep ground: hunters set on an intruder up on top or across the gap get to it round the cliff band by the easy way ({s.Round} of {s.Hunters}; {s.Waited} held at the edge){s.HunterAt}");
    }
}
