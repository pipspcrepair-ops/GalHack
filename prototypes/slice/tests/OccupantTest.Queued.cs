using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Surface;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Hunting parties, ruin roamers, shanty towns and fish, on fresh worlds every run: every world
/// where people survive has hunting parties out in the wild, armed for its era; every world has
/// someone to pick through its ruins (people where they survive, its small pack animals anywhere);
/// shanty towns stand where survivors would gather, more on worlds that left many with nothing;
/// and fish swim in clean water, never in a tainted world's.
/// </summary>
public partial class OccupantTest
{
    void Queued()
    {
        GD.Print("-- hunters, ruin roamers, shanties, fish --");
        // Hunting parties.
        int peopled = 0, parties = 0, armedRight = 0, hosts = 0;
        foreach (TechLevel era in new[] { TechLevel.Low, TechLevel.Mid, TechLevel.High })
            for (int i = 0; i < 4; i++)
            {
                WorldLife life = Life(new[] { "slow_fade", "civil_war", "plague", "drought" }[i], now: era);
                peopled++;
                var party = life.Signature.Kinds.FirstOrDefault(k => k.Species.CommonName == "a hunting party");
                if (party.Species == null) continue;
                parties++;
                string weapon = _data.Signature.Armed[SignatureTables.Era((int)era)].Prop;
                foreach (Species h in party.Species.Hosts) { hosts++; if (h.Figure.Prop == weapon && !h.Stats.Hunts) armedRight++; }
            }
        Check(parties == peopled && armedRight == hosts, $"hunting parties on every world where people survive ({parties} of {peopled}), each hunter carrying the era's weapon and leaving you be ({armedRight} of {hosts})");

        // Ruin roamers.
        int worlds = 0, cast = 0, withPeople = 0, smallOk = 0, beasts = 0;
        foreach (bool lived in new[] { true, true, true, false, false, false })
        {
            WorldLife life = Life(lived ? "slow_fade" : "vanishing");
            if (!lived) { life.World.Peopled = false; life = Bestiary.Roll(_data, life.World); }
            worlds++;
            var roamers = life.RuinCast;
            if (roamers.Count > 0) cast++;
            if (roamers.Any(r => r.Species.Figure != null)) withPeople++;
            foreach (var (s, _) in roamers.Where(r => r.Species.Figure == null)) { beasts++; if (s.Body.Size <= 1.3f && s.GroupMin >= 3) smallOk++; }
        }
        Check(cast == worlds && withPeople == 3 && beasts > 0 && smallOk == beasts, $"every world has ruin roamers ({cast} of {worlds}): people where they survive ({withPeople} of 3), small pack animals anywhere ({smallOk} of {beasts} small, in groups)");

        // Shanty towns, on real planets.
        int Shanties(string fall, out int lived)
        {
            lived = 0;
            WorldForce.Current = new WorldForce { Fall = fall };
            var spec = new PlanetSpec(Seed(), Archetype.Temperate, 0, Modifier.None);
            WorldMap map = PlanetGenerator.Generate(spec);
            WorldProfile profile = WorldForce.Current.Apply(WorldProfile.Roll(spec.Seed, "Temperate", false) with { Inhabited = true });
            LandingSite landing = Landing.Find(map, map.WidthMeters * 0.5, map.HeightMeters * 0.5);
            List<PoiSite> sites = PoiSiting.Place(SurfaceZone.PoiCatalog, map, new PlanetTerrainSource(map), landing, profile: profile);
            WorldForce.Current = new WorldForce();
            var shacks = sites.Where(s => s.Planned?.Role == "shanty").ToList();
            lived = shacks.Count(s => s.Instance?.Townsfolk?.People.Count > 0);
            return shacks.Count;
        }
        int uprooted = Shanties("tide_of_refugees", out int livedA) + Shanties("great_migration", out int livedB);
        int plain = Shanties("supply_cutoff", out int livedC) + Shanties("slow_fade", out int livedD);
        Check(uprooted > plain && uprooted >= 3 && livedA + livedB + livedC + livedD >= (uprooted + plain) / 2,
            $"shanty towns where survivors gather: {uprooted} on two worlds of refugees, {plain} on two quieter ones; {livedA + livedB + livedC + livedD} of them lived in");

        // Fish.
        int cleanOk = 0, taintOk = 0;
        foreach (string fall in new[] { "slow_fade", "civil_war", "plague" })
        {
            WorldLife life = Life(fall);
            if (Schools.Clean(Archetype.Temperate, life.World, null)) cleanOk++;
        }
        foreach (string fall in new[] { "nuclear_war", "chemical_war", "industrial_collapse" })
        {
            ulong seed = Seed();
            WorldRoll roll = Roll(seed, "Temperate", new WorldRoll.Given().Set(LayerKind.Fall, fall));
            if (!Schools.Clean(Archetype.Temperate, Conditions(roll, seed), roll)) taintOk++;
        }
        bool lava = !Schools.Clean(Archetype.Lava, new WorldConditions(), null);
        var water = new PlanetTerrainSource(PlanetGenerator.Generate(new PlanetSpec(Seed(), Archetype.Temperate, 0, Modifier.None)));
        var r = Wildlife.Rng.For(Seed(), "fish-test");
        int placed = 0, inWater = 0;
        for (int i = 0; i < 4000 && placed < 40; i++)
        {
            var around = new Vector3((float)(r.Float() * water.Width), 0f, (float)(r.Float() * water.Height));
            if (Schools.Place(water, water.Width, water.Height, around, 10f, 60f, r) is not { } spot) continue;
            placed++;
            double wx = Mathx.WrapX(spot.At.X, water.Width), wz = Mathx.Wrap(spot.At.Z, water.Height);
            if (water.WaterSurfaceAt(wx, wz, out float surface) && spot.At.Y < surface && spot.At.Y > water.SurfaceHeight(wx, wz)) inWater++;
        }
        Check(cleanOk == 3 && taintOk == 3 && lava && placed >= 20 && inWater == placed,
            $"fish in clean water ({cleanOk} of 3 worlds), none in a tainted world's ({taintOk} of 3) or a lava sea ({lava}); {inWater} of {placed} schools put down are between the water's bed and its surface");
    }
}
