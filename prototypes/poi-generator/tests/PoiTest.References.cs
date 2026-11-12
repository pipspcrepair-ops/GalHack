using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>The reference ranges (data/references.txt): rooms' areas, corridors, storeys and least footprints.</summary>
    sealed class References
    {
        public readonly Dictionary<string, (float Min, float Max)> Rooms = new();
        public readonly Dictionary<string, (float Min, float Max)> Corridors = new();
        public readonly Dictionary<string, (float W, float D)> Footprints = new();
        public (float Min, float Max) Corridor = (2, 4);
        public (float Pitch, float Min, float Max) Storey = (4, 3, 4);

        public static References Load()
        {
            var r = new References();
            foreach (string raw in System.IO.File.ReadAllLines(ProjectSettings.GlobalizePath("res://data/references.txt")))
            {
                string line = raw.Split('#')[0].Trim();
                if (line.Length == 0) continue;
                string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                float F(int i) => float.Parse(p[i], System.Globalization.CultureInfo.InvariantCulture);
                switch (p[0])
                {
                    case "room": r.Rooms[p[1]] = (F(2), F(3)); break;
                    case "corridor": r.Corridor = (F(1), F(2)); break;
                    case "storey": r.Storey = (F(1), F(2), F(3)); break;
                    case "footprint": r.Footprints[p[1]] = (F(2), F(3)); break;
                    default:
                        if (p[0].StartsWith("corridor.")) r.Corridors[p[0]["corridor.".Length..]] = (F(1), F(2));
                        break;
                }
            }
            return r;
        }
    }

    /// <summary>
    /// Our places against the reference ranges (data/references.txt, drawn from planning practice and the two games'
    /// hand-built places as numbers only): on fresh places of every family, each room's floor area by what it is for,
    /// each corridor's clear width, and each family's footprint. Most rooms of a purpose (3 in 4) fall between 3/4 of
    /// the least area and twice the most (a 1 m grid with 1 m walls runs larger); most corridors (9 in 10) are as wide
    /// as the ranges say; and no family's median footprint is smaller than the games' own places of its kind.
    /// </summary>
    void ReferenceRanges()
    {
        References refs = References.Load();
        var rand = new Random();
        var areas = new Dictionary<string, List<int>>();
        var corridors = new List<(string Family, float Width)>();
        var footprints = new Dictionary<string, List<(int W, int D)>>();
        foreach (FamilyDef f in _lib.Families.Where(f => !f.Tags.Contains("city") && !f.Space))
        {
            int n = f.Tags.Contains("grand") ? 2 : 4;
            for (int i = 0; i < n; i++)
            {
                TechLevel tech = f.Techs.Count > 0 ? WorldProfile.Parse(f.Techs[rand.Next(f.Techs.Count)]) : (TechLevel)rand.Next(3);
                Layout layout;
                try { layout = PoiFactory.SpawnLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech), out _); }
                catch (Exception) { continue; }
                if (layout == null) continue;
                var nav = new NavGrid(layout.Blocks, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
                int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
                foreach (PlacedModule m in layout.Modules)
                {
                    var floor = m.Cells.Where(c => c.Y == m.Min.Y && nav.StandsOn(c)).ToList();
                    if (floor.Count == 0) continue;
                    x0 = Math.Min(x0, m.Min.X); x1 = Math.Max(x1, m.Max.X); z0 = Math.Min(z0, m.Min.Z); z1 = Math.Max(z1, m.Max.Z);
                    if (m.Kind == ModuleKind.Corridor)
                    {
                        // Its clear width: at each floor cell the shorter of its runs across x and z, the corridor's median
                        // (an L or a tee is as wide as its arms).
                        var set = floor.ToHashSet();
                        int Run(Int3 c, Int3 d) { int n = 1; for (Int3 q = c + d; set.Contains(q); q += d) n++; for (Int3 q = c - d; set.Contains(q); q -= d) n++; return n; }
                        var across = floor.Select(c => Math.Min(Run(c, new Int3(1, 0, 0)), Run(c, new Int3(0, 0, 1)))).OrderBy(w => w).ToList();
                        corridors.Add((f.Id, across[across.Count / 2]));
                    }
                    else if (refs.Rooms.ContainsKey(m.Purpose))
                    {
                        if (!areas.TryGetValue(m.Purpose, out var list)) areas[m.Purpose] = list = new List<int>();
                        list.Add(floor.Count);
                    }
                }
                if (x0 == int.MaxValue) continue;
                // Its footprint is all it built on the ground, yard, field and silo too (as a game's lot is), not just its rooms.
                foreach (var (c, _) in layout.Blocks.Blocks())
                {
                    if (c.Y < 0) continue;
                    x0 = Math.Min(x0, c.X); x1 = Math.Max(x1, c.X); z0 = Math.Min(z0, c.Z); z1 = Math.Max(z1, c.Z);
                }
                if (!footprints.TryGetValue(f.Id, out var fp)) footprints[f.Id] = fp = new List<(int, int)>();
                fp.Add((x1 - x0 + 1, z1 - z0 + 1));
            }
        }

        // Rooms by purpose.
        int purposes = 0, offPurposes = 0;
        foreach (var (id, list) in areas.OrderBy(k => k.Key))
        {
            var (min, max) = refs.Rooms[id];
            list.Sort();
            int inside = list.Count(a => a >= min * 0.75f && a <= max * 2f);
            bool ok = inside * 4 >= list.Count * 3;
            purposes++;
            if (!ok) offPurposes++;
            GD.Print($"    {(ok ? "  " : "! ")}{id,-14} {list.Count,4} rooms, area p10 {list[list.Count / 10]} median {list[list.Count / 2]} p90 {list[list.Count * 9 / 10]} m2 against {min:0}-{max:0} ({inside * 100 / list.Count}% in range)");
        }
        Check(offPurposes == 0, $"rooms are sized as their purpose is in the references: {purposes - offPurposes} of {purposes} purposes mostly in range");

        // Corridors.
        bool Wide((string Family, float Width) c)
        {
            var (lo, hi) = refs.Corridors.FirstOrDefault(k => c.Family.Contains(k.Key)).Value is var v && v != default ? v : refs.Corridor;
            return c.Width >= lo - 0.25f && c.Width <= hi + 0.25f;
        }
        int wide = corridors.Count(Wide);
        var widths = corridors.Select(c => c.Width).OrderBy(w => w).ToList();
        if (widths.Count > 0)
            GD.Print($"    corridors: {widths.Count}, width p10 {widths[widths.Count / 10]:0.0} median {widths[widths.Count / 2]:0.0} p90 {widths[widths.Count * 9 / 10]:0.0} m; too narrow or wide: {string.Join(", ", corridors.Where(c => !Wide(c)).GroupBy(c => c.Family).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key} {g.Count()}"))}");
        Check(wide * 10 >= corridors.Count * 9, $"corridors are as wide as the references say: {wide} of {corridors.Count}");

        // Footprints: never smaller than the games' own places of the kind.
        var small = new List<string>();
        foreach (var (id, list) in footprints)
        {
            FamilyDef f = _lib.Family(id);
            foreach (var (key, (w, d)) in refs.Footprints)
            {
                if (!(id.Contains(key) || f.Tags.Contains(key))) continue;
                var areasF = list.Select(x => x.W * x.D).OrderBy(a => a).ToList();
                int median = areasF[areasF.Count / 2];
                GD.Print($"    footprint {id}: median {median} m2 against at least {w * d:0} ({key})");
                if (median < w * d) small.Add($"{id} {median} m2 < {w * d:0}");
                break;
            }
        }
        Check(small.Count == 0, $"no place is smaller than the games' own of its kind{(small.Count > 0 ? ": " + string.Join(", ", small) : "")}");
    }
}
