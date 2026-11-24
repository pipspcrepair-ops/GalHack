using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Planets.Gen;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Places;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Places nobody marked (roadside houses, farmsteads, sheds, shops, fuel stops) on fresh worlds of every type: a few harmless
/// crawlers on their walls and ceilings where the world keeps insects, more in a ruin than in a lived-in one, none where it
/// doesn't (no air, frozen, sterile). They sit on a face, never in it or hanging in the air.
/// </summary>
public partial class OccupantTest
{
    void RoadsideCrawlers()
    {
        GD.Print("-- crawlers in places nobody marked --");
        var flavour = _lib.Families.Where(f => f.Id.StartsWith("flavour_", StringComparison.Ordinal)).ToList();
        int places = 0, keptPlaces = 0, keptWith = 0, crawlers = 0, forbidden = 0, forbiddenWorlds = 0, harmful = 0, ruinedN = 0, activeN = 0, floating = 0, settled = 0;
        float ruined = 0f, active = 0f;
        var crawl = new CrawlWatch(_data);
        var perWorld = new List<string>();
        var none = new List<string>();
        foreach (Archetype a in Enum.GetValues<Archetype>())
            for (int k = 0; k < 2; k++)
            {
                ulong seed = Seed();
                var spec = new PlanetSpec(seed, a, _rng.Next(0, Planets.Gen.Archetypes.Variants(a)), k == 1 && _rng.Next(2) == 0 ? Modifier.Cratered : Modifier.None);
                WorldRoll roll = Roll(seed, a.ToString());
                WorldConditions w = WorldConditions.Of(spec);
                WorldReading reading = WorldReading.Of(roll);
                w.Density = reading.Density;
                Slice.Surface.WildlifePatch.Story(w, roll, WorldProfile.Roll(seed, a.ToString(), false), reading);
                // As the game: an airless world has no creatures at all.
                PlaceFauna.Deep deep = w.Vacuum ? null : new PlaceFauna.Deep(Bestiary.Roll(_data, w), null, 1f);
                int here = 0, n = 0;
                foreach (FamilyDef f in flavour.OrderBy(_ => _rng.Next()).Take(4))
                    foreach (PoiState state in new[] { PoiState.Ruined, PoiState.Active })
                    {
                        Layout layout = PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, Seed());
                        PoiInstance inst = PoiFactory.Spawn(_lib, layout, state, Seed(), null);
                        var site = new PoiSite { Instance = inst, Family = f, Name = f.Id };
                        PlaceFauna planned = deep == null ? null : PlaceFauna.Plan(new PlaceMemory(site), deep);
                        PoiPopulation pop = planned?.Population;
                        List<PoiSpawn> bugs = pop?.Spawns.Where(s => s.Role == DeepRole.Crawler && s.Species.Passive).ToList() ?? new();
                        harmful += pop?.Spawns.Count(s => s.Role == DeepRole.Crawler && !s.Species.Passive) ?? 0;
                        places++;
                        n++;
                        here += bugs.Count;
                        if (state == PoiState.Ruined) { ruined += bugs.Count; ruinedN++; } else { active += bugs.Count; activeN++; }
                        if (!w.Insects) { forbidden += bugs.Count; continue; }
                        // A swarm world's insects defend themselves (none are harmless), and a warning post has no inside.
                        if (deep.Fauna.InsectsDefend || planned.Rooms.Rooms.Count == 0) continue;
                        keptPlaces++;
                        crawlers += bugs.Count;
                        if (bugs.Count > 0) keptWith++;
                        else none.Add($"{f.Id} ({planned.Rooms.Rooms.Count} rooms)");
                        if (bugs.Count > 0 && crawl.Places < 6) crawl.Run(inst, pop);
                        if (bugs.Count > 0 && settled < 40) floating += Floating(inst, planned, ref settled);
                    }
                if (!w.Insects) forbiddenWorlds++;
                perWorld.Add($"{a}{(w.Vacuum ? " airless" : "")}{(deep?.Fauna.InsectsDefend == true ? " swarm" : "")} {w.Climate} density {w.Density:F2}: {here} in {n} ({(w.Insects ? "insects" : "none allowed")})");
            }
        foreach (string s in perWorld) GD.Print("    " + s);
        GD.Print($"    {crawlers} crawlers in {keptPlaces} places where insects live; ruins {ruined / Math.Max(1, ruinedN):F1} a place, lived-in {active / Math.Max(1, activeN):F1}");
        Check(forbidden == 0, $"no crawlers where the world's climate forbids insects: {forbidden} on {forbiddenWorlds} such worlds");
        if (none.Count > 0) GD.Print($"    none in: {string.Join(", ", none.Distinct())}");
        Check(keptPlaces > 0 && keptWith >= keptPlaces * 0.9f && crawlers <= keptPlaces * 6, $"places nobody marked have a few crawlers where insects live: {keptWith} of {keptPlaces} places, {crawlers} in all");
        Check(ruined / Math.Max(1, ruinedN) > active / Math.Max(1, activeN), "a ruin has more crawlers than a lived-in place");
        Check(harmful == 0, $"every crawler is harmless ({harmful} that hunt)");
        Check(crawl.Insects > 10 && crawl.Inside == 0, $"crawlers crawl on their faces, never in them: {crawl.Insects} in {crawl.Places} places, {crawl.Inside} points inside a block (worst {crawl.Worst:F2} m){crawl.Example}");
        Check(settled > 10 && floating == 0, $"nothing hangs in the air: {floating} of {settled} crawlers off any face");
    }

    /// <summary>A place's crawlers put down in its blocks (and its furniture) on level ground, as the game does, and let settle: how many are off any face.</summary>
    int Floating(PoiInstance inst, PlaceFauna planned, ref int settled)
    {
        PoiPopulation pop = planned.Population;
        var terrain = new Wildlife.Terrain((x, z) => 0f, 64f);
        var sim = new WildSim(_data, terrain, new Wildlife.NavGrid(terrain.HalfSize, 3f));
        BlockSpace space = PlaceFauna.SpaceOf(new PlaceMemory(new PoiSite { Instance = inst, Family = inst.Family, Name = "" }));
        List<Creature> made = PoiFauna.Spawn(sim, pop, space).Where(c => c.Species.Passive && c.Species.Role == DeepRole.Crawler).ToList();
        PlaceFauna.CullUnreached(sim, PlaceFauna.Reach(inst, planned.Rooms));
        for (int i = 0; i < 60; i++) sim.Step(0.05f);
        int off = 0;
        foreach (Creature c in made)
        {
            if (c.Removed || c.Dead || c.InWalls) continue;
            settled++;
            if (c.Falling || space.Density(Crawl.Center(c)) < -(c.Clearance + 0.3f)) off++;
        }
        return off;
    }
}
