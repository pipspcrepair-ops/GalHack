using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>Meshes a place the way the game's worker does: every chunk's arrays, then its mesh and collision shape.</summary>
    static void MeshAll(PoiInstance p, BlockMaterials mats)
    {
        foreach (var c in Mesher.Build(p.Blocks, p.Layout.Occupied, new MeshOptions { RockMin = p.Blocks.Min, RockMax = p.Blocks.Max, RockBottom = p.Blocks.Min.Y }))
        {
            MeshBuild.ToMesh(c, mats.BySurface);
            if (c.Collision.Count > 0) MeshBuild.ToShape(c);
        }
    }

    /// <summary>
    /// What each type of place costs, the baseline the planner is held to: fresh places of every family laid out and
    /// spawned (a world does this for every place while it loads) and meshed (the worker does this as the sleeper comes
    /// near). Per type: the footprint, the median and worst of each, and the cost per hectare, so a bigger place of a
    /// type can be held to no worse than linear. Numbers only; the check is that every type was measured.
    /// </summary>
    void PlaceCost()
    {
        var mats = new BlockMaterials();
        var rand = new Random();
        var rows = new List<(string Id, double Area, List<double> Build, List<double> Mesh)>();
        bool warm = false;
        foreach (FamilyDef f in _lib.Families)
        {
            var build = new List<double>();
            var mesh = new List<double>();
            double area = 0;
            for (int i = 0; i < 3; i++)
            {
                TechLevel tech = f.Techs.Count > 0 ? WorldProfile.Parse(f.Techs[rand.Next(f.Techs.Count)]) : (TechLevel)rand.Next(3);
                ulong seed = (ulong)rand.NextInt64();
                var clock = Stopwatch.StartNew();
                PoiInstance p;
                try
                {
                    Layout l = PoiFactory.SpawnLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, seed, new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech), out _);
                    if (l == null) continue;
                    p = PoiFactory.Spawn(_lib, l, PoiState.Abandoned, seed);
                }
                catch (Exception) { continue; }
                double b = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                MeshAll(p, mats);
                double m = clock.Elapsed.TotalMilliseconds;
                if (!warm) { warm = true; i--; continue; }   // the first place pays for the JIT
                build.Add(b);
                mesh.Add(m);
                area = Math.Max(area, (p.Blocks.Size.X - 8.0) * (p.Blocks.Size.Z - 8.0));
            }
            if (build.Count > 0) rows.Add((f.Id, area, build, mesh));
        }
        double Median(List<double> x) => x.OrderBy(t => t).ElementAt(x.Count / 2);
        GD.Print("    type: footprint m² | build median/worst ms | mesh median/worst ms | ms per hectare (build+mesh, median)");
        foreach (var r in rows.OrderByDescending(r => r.Build.Max() + r.Mesh.Max()))
            GD.Print($"      {r.Id}: {r.Area:F0} | {Median(r.Build):F0}/{r.Build.Max():F0} | {Median(r.Mesh):F0}/{r.Mesh.Max():F0} | {(Median(r.Build) + Median(r.Mesh)) / Math.Max(0.01, r.Area / 10000):F0}");
        var all = rows.SelectMany(r => r.Build.Zip(r.Mesh, (b, m) => b + m)).ToList();
        GD.Print($"    every type: build+mesh median {Median(all):F0} ms, worst {all.Max():F0} ms; build median {Median(rows.SelectMany(r => r.Build).ToList()):F0} ms, mesh median {Median(rows.SelectMany(r => r.Mesh).ToList()):F0} ms");
        Check(rows.Count == _lib.Families.Count, $"every type of place measured: {rows.Count} of {_lib.Families.Count}");
    }
}
