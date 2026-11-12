using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Places;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// The dark: places with rooms below ground on fresh worlds (a biohazard, a machine uprising, a
/// nuclear war, a civil war, a Hollow world and plain ones), held by nobody, by nobody at all, and
/// by nesters. No nest is built anywhere. Insects are many and passive, and climb. What comes at
/// you indoors stands between a child's height and a person's, and comes in many bodies (the
/// world's hunters, its twisted and changed, its machines, its Hollows), with now and then an armed
/// group; the same kinds outside keep their full size.
/// </summary>
public partial class OccupantTest
{
    void DarkPlaces()
    {
        GD.Print("-- the dark --");
        var deepFamilies = Applicable().Where(f => f.Tags.Contains("underground")).ToList();
        (string Fall, string Layer)[] worlds = { ("biohazard", null), ("machine_uprising", null), ("nuclear_war", null), ("civil_war", null), (null, "hollow_overrun"), (null, null), (null, null), ("slow_fade", null) };
        int places = 0, withFoes = 0, nests = 0, armed = 0, foes = 0, outOfRange = 0, insects = 0, insectsClimb = 0, insectsBite = 0, worldsVaried = 0;
        string tooTall = "";
        var crawl = new CrawlWatch(_data);
        var formsAll = new HashSet<string>();
        bool changedIn = false, machinesIn = false, hollowsIn = false;
        float outdoorTallest = 0f;
        int outdoorChanged = 0;
        foreach (var (fall, layer) in worlds)
        {
            ulong seed = Seed();
            var given = new WorldRoll.Given();
            if (fall != null) given.Set(LayerKind.Fall, fall);
            if (layer != null) given.Set(LayerKind.Flora, layer);
            WorldRoll roll = Roll(seed, "Temperate", given);
            WorldConditions w = Conditions(roll, seed);
            Slice.Surface.WildlifePatch.Story(w, roll, WorldProfile.Roll(seed, "Temperate", false), WorldReading.Of(roll));
            WorldLife life = Bestiary.Roll(_data, w);
            var outdoor = life.Fauna.Species.ToDictionary(s => s, s => s.BodyHeight);
            var deep = new PlaceFauna.Deep(life, null, 1f);
            var forms = new HashSet<string>();
            foreach (FamilyDef f in deepFamilies.OrderBy(_ => _rng.Next()).Take(6))
            {
                Layout layout = PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, Seed());
                foreach (OccupancyRoll occ in new[] { null, new OccupancyRoll(Occupant.Empty), new OccupancyRoll(Occupant.Nesters) })
                {
                    PoiInstance inst = PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, Seed(), null, occ);
                    PoiPopulation pop = PlaceFauna.Plan(new PlaceMemory(new PoiSite { Instance = inst, Family = f, Name = f.Id }), deep).Population;
                    nests += pop.Nests.Count;
                    if (pop.Silent) continue;
                    var aggressors = pop.Spawns.Where(s => !s.Species.Passive && s.Species.Role != DeepRole.Subsister).ToList();
                    foreach (PoiSpawn s in pop.Spawns.Where(s => s.Species.Passive))
                    {
                        insects++;
                        if (s.Species.Climbs || s.Species.Rig == RigKind.Crawler) insectsClimb++;
                        if (s.Species.Stats.Hunts) insectsBite++;
                    }
                    if (occ == null) { places++; if (aggressors.Count > 0) withFoes++; }
                    if (occ == null && crawl.Places < 8 && pop.Spawns.Any(s => s.Species.Passive)) crawl.Run(inst, pop);
                    if (aggressors.Any(s => s.Species.Figure is { Occupancy: Occupancy.Raider })) armed++;
                    foreach (PoiSpawn s in aggressors)
                    {
                        foes++;
                        float h = s.Species.Figure?.Height ?? s.Species.BodyHeight;
                        if (h < DeepFauna.IndoorMin - 0.06f || h > (s.Species.Figure != null ? 1.96f : DeepFauna.IndoorMax + 0.01f)) { outOfRange++; tooTall = $"{s.Species.CommonName} {h:F2} m"; }
                        forms.Add(s.Species.Figure != null ? (s.Species.Mechanical ? "machine on two legs" : "person") : $"{s.Species.Form}/{s.Species.Rig}");
                        changedIn |= s.Species.Id.Contains("-own-") && s.Species.Figure is { Occupancy: Occupancy.Feral };
                        machinesIn |= s.Species.Mechanical;
                        hollowsIn |= s.Species.Corrupted && s.Species.Figure == null;
                    }
                }
            }
            formsAll.UnionWith(forms);
            if (forms.Count >= 3) worldsVaried++;
            GD.Print($"    {fall ?? layer ?? "plain"}: the dark's foes come as {string.Join(", ", forms)}");
            foreach (var (s, h) in outdoor)
            {
                if (MathF.Abs(s.BodyHeight - h) > 1e-4f) outdoorChanged++;
                outdoorTallest = MathF.Max(outdoorTallest, s.BodyHeight);
            }
        }
        Check(nests == 0, $"no nest is built in any place ({nests} found)");
        Check(insects > 40 && insectsClimb == insects && insectsBite == 0, $"insects are many ({insects}), passive (none hunt) and climb walls and ceilings ({insectsClimb})");
        Check(crawl.Insects > 20 && crawl.Legged == 0 && crawl.Inside == 0 && crawl.Faces >= 2,
            $"insects crawl on their faces, never in them: {crawl.Insects} in {crawl.Places} places over a minute each, {crawl.Inside} body or segment points inside a wall, floor or ceiling (worst {crawl.Worst:F2} m), {crawl.Legged} on a body not made to crawl, on {crawl.Faces} kinds of face{crawl.Example}");
        Check(places > 20 && withFoes >= places * 0.75f, $"places nobody holds have something dangerous in their dark: {withFoes} of {places}");
        Check(outOfRange == 0, outOfRange == 0 ? $"every one of {foes} foes met indoors stands between a child's height and a person's" : $"{outOfRange} of {foes} foes indoors out of the 1.1-1.9 m range, e.g. {tooTall}");
        Check(worldsVaried >= worlds.Length - 2 && formsAll.Count >= 6, $"the dark's foes come in many bodies: {formsAll.Count} across the worlds ({string.Join(", ", formsAll.Take(10))}), three or more on {worldsVaried} of {worlds.Length} worlds");
        Check(changedIn && machinesIn && hollowsIn, $"the dark has the fall's changed ({changedIn}), machines where they rose ({machinesIn}) and Hollows where the growth is ({hollowsIn})");
        Check(armed > 0, $"now and then an armed group holds out in the dark ({armed} places)");
        Check(outdoorChanged == 0 && outdoorTallest > 2f, $"outside, the same kinds keep their own size ({outdoorChanged} changed; the tallest {outdoorTallest:F1} m)");
    }

    /// <summary>A place's insects let loose for a minute in its blocks: their bodies stay on the faces they crawl.</summary>
    sealed class CrawlWatch
    {
        readonly WildlifeData _data;
        public int Places, Insects, Inside, Legged, Faces;
        public float Worst = float.MinValue;
        public string Example = "";
        bool _floor, _wall, _ceiling;

        public CrawlWatch(WildlifeData data) => _data = data;

        public void Run(PoiInstance inst, PoiPopulation pop)
        {
            Places++;
            BlockGrid g = inst.Blocks;
            var terrain = new Wildlife.Terrain((x, z) => -50f, 64f);
            var sim = new WildSim(_data, terrain, new Wildlife.NavGrid(terrain.HalfSize, 3f));
            var space = new BlockSpace((x, y, z) => PlaceFauna.Solid(inst, x, y, z),
                new Vector3I(g.Min.X, g.Min.Y, g.Min.Z), new Vector3I(g.Min.X + g.Size.X, g.Min.Y + g.Size.Y, g.Min.Z + g.Size.Z));
            List<Creature> bugs = PoiFauna.Spawn(sim, pop, space).Where(c => c.Species.Passive).ToList();
            Insects += bugs.Count;
            Legged += bugs.Count(c => c.Rig is not CrawlRig);
            for (int step = 0; step < 1200; step++)
            {
                sim.Step(0.05f);
                if (step % 4 != 0) continue;
                foreach (Creature c in bugs)
                {
                    if (c.InWalls || c.Removed || c.Dead) continue;
                    var points = new List<Vector3> { Crawl.Center(c) };
                    if (c.Rig is CrawlRig rig) points.AddRange(rig.SegmentCenters);
                    foreach (Vector3 q in points)
                    {
                        // Inside a block, the density is the depth to the nearest open air.
                        float d = space.Density(q);
                        Worst = MathF.Max(Worst, d);
                        if (d > 0.02f && Inside++ == 0) Example = $"; e.g. {c.Species.CommonName} at {q}, {d:F2} m in, up {c.Facing.Y}";
                    }
                    if (c.Falling) continue;
                    float ny = c.Facing.Y.Y;
                    _floor |= ny > 0.8f;
                    _wall |= MathF.Abs(ny) < 0.35f;
                    _ceiling |= ny < -0.8f;
                }
            }
            Faces = (_floor ? 1 : 0) + (_wall ? 1 : 0) + (_ceiling ? 1 : 0);
        }
    }
}
