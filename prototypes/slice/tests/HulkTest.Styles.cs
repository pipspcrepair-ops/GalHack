using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Int3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

public partial class HulkTest
{
    static readonly string[] Styled = { "freeholders", "meridian", "exchange", "reavers", "kessari", "vharn" };

    /// <summary>
    /// Silhouette first (DESIGN, "Ships and structures by faction"; docs/ship-forms.md gate 3): every faction's hull is told
    /// from every other's by its outline alone. Hulls of each style on fresh seeds (any role and size), built with the
    /// livery off, each projected side on, from above and end on; each outline reduced to a handful of measures
    /// (proportions, how full and how ragged it is, how lopsided, how regularly ribbed, how sharp its bow, how many
    /// drives, what stands out past its body); then each hull is judged with nothing of it in the judging: the measures'
    /// scales, their weights (how well each parts the factions against how much it wanders within one) and the factions'
    /// averages all worked out without it, and it must be nearest its own faction's average. All 30 of 30, run after run.
    /// --dir writes the outlines.
    /// </summary>
    IEnumerable<int> Styles()
    {
        GD.Print("-- faction silhouettes on fresh seeds --");
        PoiLibrary lib = WorldChoice.Library;
        Catalogue cat = _game.Zone.Data.Catalogue;
        SiteDef site = lib.Sites.Values.First(s => s.Parts.Count > 0 && s.Parts[0].Arg("what") == "capital");
        string[] roles = { "freighter", "warship", "liner", "miner", "carrier" }, sizes = { "medium", "large", "huge" };
        const int each = 5;
        var samples = new List<(string Faction, float[] Measure, bool[,] Side, bool[,] Top)>();
        foreach (string faction in Styled)
            for (int i = 0; i < each; i++)
            {
                ulong seed = (ulong)Random.Shared.NextInt64();
                var spec = new CapitalHull.Spec(roles[Random.Shared.Next(roles.Length)], sizes[Random.Shared.Next(sizes.Length)], "long", faction);
                CapitalHull.HullPlan plan = CapitalHull.Plan(lib.Kits[site.Style], WorldProfile.Parse(site.Parts[0].Arg("tech", "mid")), spec, seed);
                Structure s = HullVessel.Build(plan, plan.Pieces[0], cat, PoiState.Abandoned, seed, intact: true, plain: true).Structure;
                var (side, top, front) = Outlines(s);
                samples.Add((faction, Measure(side, top, front, plan), side, top));
                yield return 0;
            }
        // Each hull judged by the rest alone: their spread puts every measure on one scale, their factions' spreads weigh
        // the measures (a measure that only follows role and size counts for little), their factions' averages are the targets.
        int k = samples[0].Measure.Length, right = 0;
        var wrong = new List<string>();
        for (int i = 0; i < samples.Count; i++)
        {
            var rest = samples.Where((_, j) => j != i).ToList();
            float[] mean = Enumerable.Range(0, k).Select(j => rest.Average(x => x.Measure[j])).ToArray();
            float[] sd = Enumerable.Range(0, k).Select(j => MathF.Sqrt(rest.Average(x => (x.Measure[j] - mean[j]) * (x.Measure[j] - mean[j]))) + 1e-4f).ToArray();
            float[] Z(float[] m) => m.Select((x, j) => (x - mean[j]) / sd[j]).ToArray();
            var zs = rest.Select(x => (x.Faction, Z: Z(x.Measure))).ToList();
            float[] weight = Enumerable.Range(0, k).Select(j =>
            {
                var byF = Styled.Select(f => zs.Where(z => z.Faction == f).Select(z => z.Z[j]).ToList()).ToList();
                float between = byF.Select(g => g.Average()).Select(m => m * m).Average();
                float within = byF.Average(g => g.Select(x => (x - g.Average()) * (x - g.Average())).Average()) + 0.05f;
                return between / within;
            }).ToArray();
            float[] me = Z(samples[i].Measure);
            string best = null;
            float bestD = float.MaxValue;
            foreach (string f in Styled)
            {
                var theirs = zs.Where(z => z.Faction == f).Select(z => z.Z).ToList();
                float d = Enumerable.Range(0, k).Sum(j => weight[j] * MathF.Pow(me[j] - theirs.Average(o => o[j]), 2));
                if (d < bestD) (bestD, best) = (d, f);
            }
            if (best == samples[i].Faction) right++;
            else wrong.Add($"{samples[i].Faction} read as {best}");
        }
        foreach (string f in Styled)
            GD.Print($"      {f}: {string.Join(" ", Enumerable.Range(0, k).Select(j => samples.Where(x => x.Faction == f).Average(x => x.Measure[j]).ToString("F2")))}");
        if (_args.TryGetValue("dir", out string dir)) SaveOutlines(samples.Select(x => (x.Faction, x.Side, x.Top)).ToList(), $"{dir}/silhouettes.png");
        Check(right == samples.Count, $"each faction told by its outline alone, livery off, each hull judged without it: {right} of {samples.Count} hulls nearest their own faction's outline{(wrong.Count > 0 ? " (" + string.Join(", ", wrong) + ")" : "")}");
    }

    readonly Dictionary<string, string> _args = OS.GetCmdlineUserArgs().Where(a => a.StartsWith("--") && a.Contains('=')).ToDictionary(a => a[2..a.IndexOf('=')], a => a[(a.IndexOf('=') + 1)..]);

    /// <summary>A structure's outline side on (v by y), from above (v by u) and end on (u by y): every cell anything stands in.</summary>
    static (bool[,] Side, bool[,] Top, bool[,] Front) Outlines(Structure s)
    {
        var (lo, hi) = s.Bounds();
        int nu = hi.X - lo.X + 1, ny = hi.Y - lo.Y + 1, nv = hi.Z - lo.Z + 1;
        var side = new bool[nv, ny];
        var top = new bool[nv, nu];
        var front = new bool[nu, ny];
        foreach (var (p, _) in s.Blocks())
        {
            side[p.Z - lo.Z, p.Y - lo.Y] = true;
            top[p.Z - lo.Z, p.X - lo.X] = true;
            front[p.X - lo.X, p.Y - lo.Y] = true;
        }
        return (side, top, front);
    }

    /// <summary>An outline's measures: proportions; how full and how ragged each view; how lopsided from above; how regularly its beam repeats down its length; its bow's sharpness; how many drives; the crown's and the spine's height.</summary>
    static float[] Measure(bool[,] side, bool[,] top, bool[,] front, CapitalHull.HullPlan plan)
    {
        int nv = side.GetLength(0), ny = side.GetLength(1), nu = top.GetLength(1);
        static float Fill(bool[,] g) { int n = 0; foreach (bool b in g) if (b) n++; return n / (float)g.Length; }
        static float Ragged(bool[,] g)
        {
            int a = 0, edge = 0, w = g.GetLength(0), h = g.GetLength(1);
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    if (!g[i, j]) continue;
                    a++;
                    if (i == 0 || j == 0 || i == w - 1 || j == h - 1 || !g[i - 1, j] || !g[i + 1, j] || !g[i, j - 1] || !g[i, j + 1]) edge++;
                }
            return edge / MathF.Sqrt(Math.Max(1, a));
        }
        // The beam down the length (from above), and the height down it (side on).
        float[] beam = Enumerable.Range(0, nv).Select(v => (float)Enumerable.Range(0, nu).Count(u => top[v, u])).ToArray();
        float[] height = Enumerable.Range(0, nv).Select(v => (float)Enumerable.Range(0, ny).Count(y => side[v, y])).ToArray();
        int mismatch = 0, area = 0;
        for (int v = 0; v < nv; v++)
            for (int u = 0; u < nu; u++)
            {
                if (top[v, u]) area++;
                if (top[v, u] != top[v, nu - 1 - u]) mismatch++;
            }
        // Repetition: the beam's best agreement with itself shifted 4 to 12 m (frames, segments), about its own trend.
        float[] d = beam.Select((b, i) => b - beam.Skip(Math.Max(0, i - 6)).Take(13).Average()).ToArray();
        float var0 = d.Sum(x => x * x) + 1e-3f, rep = 0f;
        for (int lag = 4; lag <= 12; lag++) rep = MathF.Max(rep, Enumerable.Range(0, nv - lag).Sum(i => d[i] * d[i + lag]) / var0);
        float maxBeam = beam.Max();
        // Beyond the body: what stands out past the hull's own beam from above (pods, modules, spikes, frames, fins), and
        // over its roof side on (a superstructure, a truss, pods, spikes).
        // (The body: its median beam from above and median height side on, the outline's own.)
        float half = beam.Where(b => b > 0).OrderBy(b => b).ElementAt(beam.Count(b => b > 0) / 2) / 2f;
        var tops = Enumerable.Range(0, nv).Select(v => Enumerable.Range(0, ny).LastOrDefault(y => side[v, y], -1)).Where(t => t >= 0).OrderBy(t => t).ToList();
        int beyond = 0, over = 0, sideArea = 0, roof = tops[tops.Count / 2];
        for (int v = 0; v < nv; v++)
        {
            for (int u = 0; u < nu; u++)
                if (top[v, u] && MathF.Abs(u - (nu - 1) / 2f) > half + 0.6f) beyond++;
            for (int y = 0; y < ny; y++)
            {
                if (!side[v, y]) continue;
                sideArea++;
                if (y > roof) over++;
            }
        }
        // How thin what stands out past the beam is: its share in runs of three metres or less down the length (a spike's, not a crate's).
        int thin = 0, stand = 0;
        for (int u = 0; u < nu; u++)
        {
            if (MathF.Abs(u - (nu - 1) / 2f) <= half + 0.6f) continue;
            for (int v = 0; v < nv;)
            {
                if (!top[v, u]) { v++; continue; }
                int run = 0;
                while (v < nv && top[v, u]) { run++; v++; }
                stand += run;
                if (run <= 3) thin += run;
            }
        }
        // Bumps: how often the beam jumps down the length; the deepest waist down the middle half.
        int bumps = Enumerable.Range(1, nv - 1).Count(v => MathF.Abs(beam[v] - beam[v - 1]) >= 2);
        float waist = Enumerable.Range(nv / 4, nv / 2).Min(v => beam[v]) / maxBeam;
        return new[]
        {
            nv / (float)nu, nv / (float)ny,
            Fill(top), Ragged(top),
            mismatch / (float)Math.Max(1, area),
            rep,
            beam.Take(Math.Max(1, nv / 12)).Average() / maxBeam,
            plan.Form.Drives.Count / 8f,
            beyond / (float)Math.Max(1, area), over / (float)Math.Max(1, sideArea),
            bumps * 10f / nv, waist,
            thin / (float)Math.Max(1, stand),
        };
    }

    /// <summary>The outlines, a row a faction: each hull side on over its view from above, dark on light.</summary>
    static void SaveOutlines(List<(string Faction, bool[,] Side, bool[,] Top)> all, string file)
    {
        int cell = 300, rows = Styled.Length, cols = all.Count / rows;
        var img = Image.CreateEmpty(cell * cols, cell * rows, false, Image.Format.Rgb8);
        img.Fill(new Color(0.94f, 0.93f, 0.9f));
        for (int i = 0; i < all.Count; i++)
        {
            int r = Array.IndexOf(Styled, all[i].Faction), c = all.Take(i).Count(x => x.Faction == all[i].Faction);
            var (side, top) = (all[i].Side, all[i].Top);
            int nv = side.GetLength(0), ny = side.GetLength(1), nu = top.GetLength(1);
            float k = (cell - 20f) / MathF.Max(nv, (ny + nu) * 1.6f);
            void Draw(bool[,] g, int h, int oy, Color col)
            {
                for (int v = 0; v < nv; v++)
                    for (int y = 0; y < h; y++)
                        if (g[v, y])
                            for (int px = (int)(v * k); px < (int)((v + 1) * k); px++)
                                for (int py = (int)(y * k); py < (int)((y + 1) * k); py++)
                                    img.SetPixel(c * cell + 10 + px, r * cell + oy - py, col);
            }
            Draw(side, ny, 10 + (int)(ny * k), new Color(0.12f, 0.12f, 0.14f));
            Draw(top, nu, 30 + (int)((ny + nu) * k), new Color(0.3f, 0.32f, 0.38f));
        }
        img.SavePng(file);
        GD.Print($"      outlines: {file}");
    }
}
