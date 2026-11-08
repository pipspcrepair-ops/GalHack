using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Planets.Gen;
using PoiGenerator.Gen;
using PlanetSurface.Infra;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Wildlife;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace Slice.Tests;

/// <summary>
/// A war's craters: a war fall never makes a world cratered or airless (natural craters stay on barren,
/// airless bodies), and on fresh nuclear-fall worlds the blasts come down on what they aimed at: on or
/// near a target, almost never in open water, each crater in the ground with the dead land round it and
/// the places in its reach ruined by it. Fresh worlds every run.
/// </summary>
public partial class LayersTest
{
    /// <summary>A blast counts as on target this close to a target's edge, metres.</summary>
    const double OnTarget = 600;

    void WarFalls()
    {
        GD.Print("-- war falls and craters --");
        var war = _lib.Layers.Where(l => l.Kind == LayerKind.Fall && l.Group == "war").ToList();
        Check(war.Count > 5 && war.All(f => !f.Sets.Any(s => s.Fact == "air" && s.Value == "none")), $"no war fall ({war.Count}) takes the air away");
        Check(_lib.Strikes.Kinds.Count > 0 && _lib.Strikes.Kinds.All(k => k.Falls.All(id => war.Any(f => f.Id == id))) && _lib.Strikes.For("nuclear_war") != null,
            $"strikes come from war falls only: {string.Join(", ", _lib.Strikes.Kinds.SelectMany(k => k.Falls))}");
        // Fresh worlds of every type, a war forced on each: the type alone decides whether its rock is cratered and airless.
        int cratered = 0, wrong = 0, worlds = 0;
        var notes = new List<string>();
        foreach (Archetype a in Enum.GetValues<Archetype>())
            for (int i = 0; i < 60; i++)
            {
                ulong seed = Seed();
                string fall = war[_rng.Next(war.Count)].Id;
                PlanetSpec spec = PlanetSpec.Roll(seed, a, _rng.Next(PlanetSpec.VariantCount));
                WorldRoll roll = Roll(seed, a.ToString(), new WorldRoll.Given().Set(LayerKind.Fall, fall));
                worlds++;
                if (spec.Has(Modifier.Cratered)) cratered++;
                bool natural = PlanetSpec.Craters(a);
                // The fall never takes the air the body had (a lava world may have had none to begin with).
                bool tookAir = roll != null && roll.Fact("air") == "none" && roll.One(LayerKind.Air)?.Id != "none";
                bool bad = !natural && (spec.Has(Modifier.Cratered) || spec.Airless || PlanetFlora.Airless(spec) || WorldConditions.Of(spec).Vacuum || Planets.Gen.Archetypes.For(spec).CraterSpacing > 0f)
                           || tookAir;
                if (bad) { wrong++; notes.Add($"{a} {fall} seed {seed}: {spec.Modifiers}, air {roll?.One(LayerKind.Air)?.Id} to {roll?.Fact("air")}"); }
            }
        Check(wrong == 0 && cratered > 0, $"{worlds} fresh worlds with a war: none cratered or airless but barren rock ({cratered} cratered, all arid, ice or lava){(wrong > 0 ? "; " + string.Join("; ", notes.Take(3)) : "")}");
        // Natural craters only where they stay, even when asked for on a living world.
        PlanetSpec living = new(Seed(), Archetype.Temperate, 0, Modifier.Cratered);
        Check(Planets.Gen.Archetypes.For(living).CraterSpacing == 0f && !living.Airless && !WorldConditions.Of(living).Vacuum, "a temperate world told it's cratered keeps its air and grows no craters");
    }

    void StrikesOnTargets()
    {
        GD.Print("-- strikes on targets --");
        WorldForce before = WorldForce.Current;
        bool start = PlanetSurface.Poi.PoiSiting.StartWorld;
        PlanetSurface.Poi.PoiSiting.StartWorld = false;
        int strikes = 0, onTarget = 0, wet = 0, wetNaval = 0, bowls = 0, sunk = 0, dead = 0, ruined = 0, near = 0, built = 0;
        List<string> off = new(), unblasted = new();
        try
        {
            for (int tries = 0; built < 5 && tries < 12; tries++)
            {
                WorldForce.Current = new WorldForce { Fall = "nuclear_war", MidCollapse = false, Before = _rng.Next(2) == 0 ? TechLevel.Mid : TechLevel.High };
                PlanetSpec spec = PlanetSpec.Roll(Seed(), Archetype.Temperate, 0);
                PlanetWorld w = PlanetWorld.Build(spec, Slice.Surface.SurfaceZone.PoiCatalog);
                if (w.Roll == null) continue;
                built++;
                Check(!spec.Has(Modifier.Cratered) && Planets.Gen.Archetypes.For(spec).CraterSpacing == 0f && !PlanetFlora.Airless(spec), $"nuclear world {spec.Seed}: no natural craters, air kept");
                double W = w.Map.WidthMeters, H = w.Map.HeightMeters;
                double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, W), 2) + Math.Pow(Mathx.WrapDelta(az - bz, H), 2));
                // What could have been aimed at: the targets the war saw, and every town's heart.
                var targets = w.Infra.Targets.Select(t => (t.X, t.Z, (double)t.Reach)).ToList();
                targets.AddRange(w.Pois.Where(p => p.Planned?.Role == "hub").Select(p => (p.Center.X, p.Center.Z, Math.Max(p.Stamp.MaxX - p.Stamp.MinX, p.Stamp.MaxZ - p.Stamp.MinZ) * 0.5)));
                foreach (Strike s in w.Blasts)
                {
                    strikes++;
                    double edge = targets.Count == 0 ? double.MaxValue : targets.Min(t => Dist(t.X, t.Z, s.X, s.Z) - t.Item3);
                    if (edge <= OnTarget) onTarget++;
                    else off.Add($"{s} ({edge:F0} m from a target's edge)");
                    if (w.Map.Has(w.Map.CellAt(s.X, s.Z), CellFlags.Water)) { wet++; if (s.Naval) wetNaval++; }
                    if (w.Palette.Deadness(s.X, s.Z) >= 0.99f) dead++;
                    if (s.Bowl && !s.Site)
                    {
                        bowls++;
                        // The crater is in the ground: its floor lies well below its rim.
                        float floor = w.Source.SurfaceHeight(s.X, s.Z), rim = w.Source.SurfaceHeight(Mathx.WrapX(s.X + s.Crater, W), s.Z);
                        if (rim - floor > s.Depth * 0.6f) sunk++;
                    }
                    // The places in its flattened ring took it.
                    foreach (PoiSite p in w.Pois.Where(p => p.GroundZero == null && Dist(p.Center.X, p.Center.Z, s.X, s.Z) < s.Ruin * 0.7))
                    {
                        near++;
                        if (p.Instance.Blasted > 0f) ruined++;
                        else unblasted.Add($"{p.Name} {Dist(p.Center.X, p.Center.Z, s.X, s.Z):F0} m from {s}");
                    }
                }
                GD.Print($"(world {spec.Seed}: {w.Blasts.Count} blasts on {w.Infra.Targets.Count} targets: {string.Join("; ", w.Blasts.Take(4))})");
            }
        }
        finally
        {
            WorldForce.Current = before;
            PlanetSurface.Poi.PoiSiting.StartWorld = start;
        }
        Check(built >= 3 && strikes >= built * 3, $"{strikes} blasts over {built} fresh nuclear worlds");
        Check(strikes > 0 && onTarget >= strikes * 0.8, $"{onTarget} of {strikes} blasts within {OnTarget:F0} m of a target{(off.Count > 0 ? "; off: " + string.Join("; ", off.Take(3)) : "")}");
        Check(strikes > 0 && wet < strikes * 0.05 && wet == wetNaval, $"{wet} of {strikes} in open water, {wetNaval} of those at ships");
        Check(bowls > 0 && sunk == bowls, $"every ground burst's crater is in the ground: {sunk} of {bowls}");
        Check(dead == strikes, $"the land is dead round every blast: {dead} of {strikes}");
        Check(near == 0 || ruined == near, $"every place in a blast's flattened ring took it: {ruined} of {near}{(ruined < near ? "; " + string.Join("; ", unblasted.Take(2)) : "")}");
    }
}
