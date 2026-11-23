using System;
using System.Collections.Generic;
using GalHack.Arms;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Terrain;
using Slice.Places;

namespace Slice.Arms;

/// <summary>
/// Weapons on the world (owner, 17 Nov 2026: "So they can damage anything, really"): anyone's shot or blast, by the weapon's own
/// numbers (a shot's damage times its blocks factor, a blast's damage times its own), harms what it meets through one path each:
/// a place's blocks, doors and dressing (<see cref="PoiPlaces.Struck"/>, <see cref="PoiPlaces.Blast"/>), a wreck
/// (<see cref="PoiPlaces.Work(SalvageSite, float)"/>), a plant's hit points (<see cref="Surface.SurfaceZone.WearPlant"/>, the
/// ones the Sleeper tool wears), and the ground, which a round only marks and a blast craters (<see cref="Surface.PlanetEdits.Blast"/>:
/// the dig's own edit, kept through streaming and saves; bedrock stays).
/// </summary>
public partial class ArmsHost
{
    /// <summary>Shots into places, wrecks and plants; craters blasted and plants a blast took (tests).</summary>
    public int PlaceHits, WreckHits, PlantHits, Craters, PlantsBlasted;

    /// <summary>The most one blast's harm to places, to plants and to the ground has taken, ms (tests).</summary>
    public static double PlacesMs, PlantsMs, CraterMs;

    /// <summary>Off, shots and blasts only mark the world (tests: a fight's cost without it).</summary>
    public static bool HarmsWorld = true;

    /// <summary>A shot into something solid that is no structure: a place, a wreck or a tree takes <paramref name="amount"/>; the ground only its mark.</summary>
    void HarmWorld(in ShotHit hit, Vector3 at, Vector3 n, Vector3 dir, float amount)
    {
        if (amount <= 0f || !HarmsWorld) return;
        if (hit.Thing is GodotObject collider && Game.Places is { } places)
        {
            if (places.Resolve(collider, at, n, out PoiPlaces.Hit h, hit.CellX)) { places.Struck(h, amount); PlaceHits++; }
            else if (places.SalvageOf(collider) is { } wreck) { places.Work(wreck, amount); WreckHits++; }
            else if (Zone.StandingOf(collider, at) is { } standing) { Zone.WearStanding(standing, amount * Table.Rules.PlantRound, dir with { Y = 0f }); PlantHits++; } // a pole or a post is shot through as a trunk is
            else if (Zone.Infra.UnderAt(collider, at, n) is { } lining) { Zone.Infra.WearUnder(lining, amount); PlaceHits++; } // the under-town's lining, block by block as a place's
            else if (Zone.InfraStruck(collider, at, n, out GalHack.Structures.Int3 cell) is { } block) { block.Hits.Shot(cell, at.ToNumerics(), n.ToNumerics(), amount, GalHack.Structures.DamageType.Kinetic); PlaceHits++; } // a bridge, a tunnel mouth, a seawall: a structure of blocks from now on
        }
        else if (hit.Thing is Rid body && Zone.TreeAt((body, hit.CellX), out long tree, out _) && Zone.Sighting(tree, at, out TerrainStreamer.PlantSighting plant))
        {
            Zone.WearPlant(plant, amount * Table.Rules.PlantRound, dir with { Y = 0f }, batch: true);
            PlantHits++;
        }
    }

    readonly HashSet<long> _blasted = new();

    /// <summary>
    /// A blast's harm to the world round it, falling off with distance as a structure's blocks take it: the places and wrecks in
    /// its reach, the plants (a heavy one fells or flattens what's near), and a crater in open ground sized by it (none where a
    /// place or a structure stands, which have their own ground).
    /// </summary>
    void BlastWorld(Vector3 p, Vector3 n, BlastPart blast, bool hulls)
    {
        float r = blast.Radius, damage = blast.Damage * blast.Blocks;
        if (damage <= 0f || r <= 0f || !HarmsWorld) return;
        PoiPlaces places = Game.Places;
        long t = System.Diagnostics.Stopwatch.GetTimestamp();
        places?.Blast(p, r, damage);
        PlacesMs = Math.Max(PlacesMs, System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        t = System.Diagnostics.Stopwatch.GetTimestamp();
        _blasted.Clear();
        foreach (TerrainStreamer.PlantSighting s in Zone.Streamer.FloraNear(p, r))
        {
            if (!_blasted.Add(s.Id)) continue;
            Vector3 trunk = s.Root + Vector3.Up * Mathf.Clamp(p.Y - s.Root.Y, 0f, 2f * s.Scale), away = s.Root - p;
            float d = trunk.DistanceTo(p);
            if (d >= r) continue;
            away.Y = 0f;
            Zone.WearPlant(s, damage * (1f - d / r * 0.75f), away.LengthSquared() > 1e-4f ? away : Vector3.Right, batch: true);
            if (Zone.Planet.Felled.Contains(s.Id)) PlantsBlasted++;
        }
        Zone.BlastStanding(p, r, damage);
        Zone.Infra.BlastUnder(p, r, damage);
        hulls |= Zone.BlastInfra(p, r, damage);
        PlantsMs = Math.Max(PlantsMs, System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        if (hulls || places?.PlaceAt(p) != null) return;
        t = System.Diagnostics.Stopwatch.GetTimestamp();
        float crater = Table.Rules.Crater * MathF.Cbrt(damage);
        if (Zone.Edits.Blast(p.ToNumerics(), crater).Removed.Count > 0) Craters++;
        CraterMs = Math.Max(CraterMs, System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds);
    }
}
