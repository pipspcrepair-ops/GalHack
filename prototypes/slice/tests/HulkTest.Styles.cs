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
    /// from every other's by its outline alone. Thirty hulls, five of each faction, on fresh seeds (any role and size),
    /// built with the livery off and projected side on, from above and end on; each outline reduced to its measures
    /// (proportions; how full, ragged and holed each view; how lopsided, how symmetric end on and about its axis; how
    /// steady its sides, how regularly ribbed, how sharp its bow, its ends against its middle; its drives, what stands
    /// past its body, its waists, crown and spikes). Each is judged by what knows nothing of it: a reference of 200 more
    /// forms of each faction on their own fresh seeds, which alone sets the measures' scales and teaches a model (softmax
    /// regression) what each faction's outlines are; every one of the thirty must come out its own faction's. All 30 of
    /// 30, run after run. --dir writes the thirty's outlines.
    /// </summary>
    IEnumerable<int> Styles()
    {
        GD.Print("-- faction silhouettes on fresh seeds --");
        PoiLibrary lib = WorldChoice.Library;
        Catalogue cat = Cat;
        SiteDef site = lib.Sites.Values.First(s => s.Parts.Count > 0 && s.Parts[0].Arg("what") == "capital");
        Kit kit = lib.Kits[site.Style];
        TechLevel tech = WorldProfile.Parse(site.Parts[0].Arg("tech", "mid"));
        string[] roles = { "freighter", "warship", "liner", "miner", "carrier" }, sizes = { "medium", "large", "huge" };
        CapitalHull.Spec Roll(string faction) => new(roles[Random.Shared.Next(roles.Length)], sizes[Random.Shared.Next(sizes.Length)], "long", faction);
        // The reference, on workers: forms only, their outlines the cells their shells take.
        const int teach = 200;
        var reference = System.Threading.Tasks.Task.Run(() =>
        {
            var jobs = Styled.SelectMany(f => Enumerable.Range(0, teach).Select(_ => (Faction: f, Spec: Roll(f), Seed: (ulong)Random.Shared.NextInt64()))).ToList();
            var rows = new (string Faction, float[] Measure)[jobs.Count];
            System.Threading.Tasks.Parallel.For(0, jobs.Count, n =>
            {
                CapitalHull.HullPlan plan = CapitalHull.Plan(kit, tech, jobs[n].Spec, jobs[n].Seed);
                var (side, top, front) = Outlines(plan.Form);
                rows[n] = (jobs[n].Faction, Measure(side, top, front, plan));
            });
            return rows.ToList();
        });
        // The thirty under test, built of blocks as the game builds them.
        const int each = 5;
        var samples = new List<(string Faction, float[] Measure, bool[,] Side, bool[,] Top, string Plan, float[] Form)>();
        foreach (string faction in Styled)
            for (int i = 0; i < each; i++)
            {
                ulong seed = (ulong)Random.Shared.NextInt64();
                CapitalHull.HullPlan plan = CapitalHull.Plan(kit, tech, Roll(faction), seed);
                Structure s = HullVessel.Build(plan, plan.Pieces[0], cat, PoiState.Abandoned, seed, intact: true, plain: true).Structure;
                var (side, top, front) = Outlines(s);
                var (fs, ft, ff) = Outlines(plan.Form);
                samples.Add((faction, Measure(side, top, front, plan), side, top, plan.Form.Plan, Measure(fs, ft, ff, plan)));
                yield return 0;
            }
        while (!reference.IsCompleted) yield return 0;
        var model = new Softmax(reference.Result, Styled);
        int right = 0;
        var wrong = new List<string>();
        foreach (var x in samples)
        {
            string best = model.Judge(x.Measure);
            if (best == x.Faction) right++;
            else wrong.Add($"a {x.Faction} {x.Plan} read as {best}");
        }
        GD.Print($"      the reference: {reference.Result.Count} forms, {model.Fits:P0} of them read as their own faction by the model they taught; the thirty's own forms read right {samples.Count(x => model.Judge(x.Form) == x.Faction)} of {samples.Count}");
        if (_args.TryGetValue("dir", out string dir)) SaveOutlines(samples.Select(x => (x.Faction, x.Side, x.Top)).ToList(), $"{dir}/silhouettes.png");
        Check(right == samples.Count, $"each faction told by its outline alone, livery off, each hull judged by a reference that never saw it: {right} of {samples.Count} hulls read as their own faction{(wrong.Count > 0 ? " (" + string.Join(", ", wrong) + ")" : "")}");
    }

    /// <summary>
    /// Softmax regression over the measures (sklearn's multinomial logistic regression with C = 3.3): the reference's
    /// spread sets each measure's scale, then batch gradient descent fits a weight per faction and measure.
    /// </summary>
    sealed class Softmax
    {
        readonly string[] _classes;
        readonly float[] _mean, _sd;
        readonly double[,] _w;
        /// <summary>The share of the reference the model reads right (how well it learnt them).</summary>
        public readonly float Fits;

        public Softmax(List<(string Faction, float[] Measure)> rows, string[] classes)
        {
            _classes = classes;
            int k = rows[0].Measure.Length, n = rows.Count, c = classes.Length;
            _mean = Enumerable.Range(0, k).Select(j => rows.Average(r => r.Measure[j])).ToArray();
            _sd = Enumerable.Range(0, k).Select(j => MathF.Sqrt(rows.Average(r => (r.Measure[j] - _mean[j]) * (r.Measure[j] - _mean[j]))) + 1e-6f).ToArray();
            var x = rows.Select(r => Scale(r.Measure)).ToArray();
            var y = rows.Select(r => Array.IndexOf(classes, r.Faction)).ToArray();
            _w = new double[c, k + 1];
            var grad = new double[c, k + 1];
            var p = new double[c];
            for (int step = 0; step < 8000; step++)
            {
                Array.Clear(grad);
                for (int i = 0; i < n; i++)
                {
                    Probabilities(x[i], p);
                    for (int a = 0; a < c; a++)
                    {
                        double e = p[a] - (y[i] == a ? 1 : 0);
                        for (int j = 0; j < k; j++) grad[a, j] += e * x[i][j];
                        grad[a, k] += e;
                    }
                }
                // (The L2 term on the weights, not the biases: 1/C, C = 3.3.)
                for (int a = 0; a < c; a++)
                    for (int j = 0; j <= k; j++) _w[a, j] -= 0.5 / n * (grad[a, j] + (j < k ? 0.3 * _w[a, j] : 0));
            }
            Fits = rows.Count(r => Judge(r.Measure) == r.Faction) / (float)n;
        }

        double[] Scale(float[] m) => m.Select((v, j) => (double)((v - _mean[j]) / _sd[j])).ToArray();

        void Probabilities(double[] x, double[] p)
        {
            int c = _classes.Length, k = x.Length;
            double top = double.MinValue;
            for (int a = 0; a < c; a++)
            {
                double z = _w[a, k];
                for (int j = 0; j < k; j++) z += _w[a, j] * x[j];
                p[a] = z;
                top = Math.Max(top, z);
            }
            double sum = 0;
            for (int a = 0; a < c; a++) sum += p[a] = Math.Exp(p[a] - top);
            for (int a = 0; a < c; a++) p[a] /= sum;
        }

        public string Judge(float[] m)
        {
            var p = new double[_classes.Length];
            Probabilities(Scale(m), p);
            return _classes[Array.IndexOf(p, p.Max())];
        }
    }

    /// <summary>A form's outlines: every cell its shell takes (any corner inside it), side on, from above and end on.</summary>
    static (bool[,] Side, bool[,] Top, bool[,] Front) Outlines(ShipForm form)
    {
        byte[] mask = ShipDecks.Masks(form, out int u0, out int nu, out int ny, out int nv);
        var side = new bool[nv, ny];
        var top = new bool[nv, nu];
        var front = new bool[nu, ny];
        for (int v = 0; v < nv; v++)
            for (int y = 0; y < ny; y++)
                for (int u = 0; u < nu; u++)
                    if (mask[u + nu * (y + ny * v)] != 0) side[v, y] = top[v, u] = front[u, y] = true;
        // Its pipes and struts too, runs of plating the builder lays along the form.
        foreach (var (from, axis, n, _) in form.Pipes)
            for (int k = 0; k < n; k++)
            {
                int u = from.X + (axis == 0 ? k : 0) - u0, y = from.Y + (axis == 1 ? k : 0), v = from.Z + (axis == 2 ? k : 0);
                if (u >= 0 && u < nu && y >= 0 && y < ny && v >= 0 && v < nv) side[v, y] = top[v, u] = front[u, y] = true;
            }
        return (Trim(side), Trim(top), Trim(front));
    }

    /// <summary>An outline cut to the box of what it holds (as a structure's own bounds are).</summary>
    static bool[,] Trim(bool[,] g)
    {
        int w = g.GetLength(0), h = g.GetLength(1), x0 = w, x1 = -1, y0 = h, y1 = -1;
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
                if (g[i, j]) (x0, x1, y0, y1) = (Math.Min(x0, i), Math.Max(x1, i), Math.Min(y0, j), Math.Max(y1, j));
        var t = new bool[x1 - x0 + 1, y1 - y0 + 1];
        for (int i = x0; i <= x1; i++)
            for (int j = y0; j <= y1; j++) t[i - x0, j - y0] = g[i, j];
        return t;
    }

    readonly Dictionary<string, string> _args = OS.GetCmdlineUserArgs().Where(a => a.StartsWith("--") && a.Contains('=')).ToDictionary(a => a[2..a.IndexOf('=')], a => a[(a.IndexOf('=') + 1)..]);

    /// <summary>A structure's outline side on (v by y), from above (v by u) and end on (u by y): every cell its plating takes (the hull itself; its devices, a thruster or a turret, are no part of its silhouette at range).</summary>
    static (bool[,] Side, bool[,] Top, bool[,] Front) Outlines(Structure s)
    {
        var plating = s.Blocks().Where(b => s.TypeOf(b.block).IsArmor).Select(b => b.pos).ToList();
        var lo = new Int3(plating.Min(p => p.X), plating.Min(p => p.Y), plating.Min(p => p.Z));
        var hi = new Int3(plating.Max(p => p.X), plating.Max(p => p.Y), plating.Max(p => p.Z));
        int nu = hi.X - lo.X + 1, ny = hi.Y - lo.Y + 1, nv = hi.Z - lo.Z + 1;
        var side = new bool[nv, ny];
        var top = new bool[nv, nu];
        var front = new bool[nu, ny];
        foreach (Int3 p in plating)
        {
            side[p.Z - lo.Z, p.Y - lo.Y] = true;
            top[p.Z - lo.Z, p.X - lo.X] = true;
            front[p.X - lo.X, p.Y - lo.Y] = true;
        }
        return (side, top, front);
    }

    /// <summary>An outline's measures: proportions; how full, how ragged, how holed and how straight-edged each view; how lopsided from above, how symmetric end on (mirrored and turned about its axis), how much stands past its body; how steady its sides run, how regularly its beam repeats down its length; its bow's sharpness, its ends against its middle; how many drives and how unlike in size; what stands out past its body; its bumps and waists, how its crown rises and falls; its spikes.</summary>
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
        // End on: how round it is, whether it is the same mirrored and turned a quarter or a sixth or an eighth of the way
        // round its axis (the Kessari's symmetry), how much of it stands past its body's radius.
        int fu = front.GetLength(0), fy = front.GetLength(1);
        var cells = new List<(int X, int Y)>();
        for (int x = 0; x < fu; x++)
            for (int y = 0; y < fy; y++)
                if (front[x, y]) cells.Add((x, y));
        float cx = (float)cells.Average(c => c.X), cy = (float)cells.Average(c => c.Y);
        bool At(float x, float y) => (int)MathF.Round(x) is int i && (int)MathF.Round(y) is int j && i >= 0 && j >= 0 && i < fu && j < fy && front[i, j];
        float Turned(float a) => cells.Count(c => At(cx + (c.X - cx) * MathF.Cos(a) - (c.Y - cy) * MathF.Sin(a), cy + (c.X - cx) * MathF.Sin(a) + (c.Y - cy) * MathF.Cos(a))) / (float)cells.Count;
        float mirror = cells.Count(c => At(2 * cx - c.X, c.Y)) / (float)cells.Count;
        float radius = MathF.Sqrt(cells.Count / MathF.PI), past = cells.Count(c => (c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy) > radius * radius * 1.4f) / (float)cells.Count;
        // Side on: how its crown rises and falls down its length; from above, its ends against its middle (a hammer or a dumbbell).
        float topMean = (float)tops.Average(), topSd = MathF.Sqrt((float)tops.Average(t => (t - topMean) * (t - topMean))) / ny;
        // Holes: empty cells a view's outline closes round (a truss's bays, a lattice), against its area; how much of its
        // length its beam and its height hold the same from one metre to the next (a slab's straight sides, not a curve's).
        static float Holes(bool[,] g)
        {
            int w = g.GetLength(0), h = g.GetLength(1), holes = 0, area = 0;
            var lo = new int[w];
            var hi = new int[w];
            for (int i = 0; i < w; i++)
            {
                lo[i] = -1;
                for (int j = 0; j < h; j++)
                    if (g[i, j]) { if (lo[i] < 0) lo[i] = j; hi[i] = j; area++; }
            }
            for (int j = 0; j < h; j++)
            {
                int first = -1, last = -1;
                for (int i = 0; i < w; i++)
                    if (g[i, j]) { if (first < 0) first = i; last = i; }
                for (int i = first + 1; i < last; i++)
                    if (!g[i, j] && lo[i] >= 0 && j > lo[i] && j < hi[i]) holes++;
            }
            return holes / (float)Math.Max(1, area);
        }
        // How much of a view's edge runs straight (a slab's flat faces, not a can's or a shell's curve or a truss's bars).
        static float Straight(bool[,] g)
        {
            int w = g.GetLength(0), h = g.GetLength(1), edge = 0, straight = 0;
            bool F(int i, int j) => i >= 0 && j >= 0 && i < w && j < h && g[i, j];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    if (!g[i, j]) continue;
                    foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        if (F(i + di, j + dj)) continue;
                        edge++;
                        // Straight: the cells either side along the edge are filled with the same side open.
                        int ai = dj, aj = di;
                        if (F(i + ai, j + aj) && F(i - ai, j - aj) && !F(i + ai + di, j + aj + dj) && !F(i - ai + di, j - aj + dj)) straight++;
                    }
                }
            return straight / (float)Math.Max(1, edge);
        }
        // How unlike its drives' bells are in size (a salvaged cluster, against a bank of one make).
        static float Spread(List<float> r) => r.Count < 2 ? 0f : MathF.Sqrt(r.Average(x => (x - r.Average()) * (x - r.Average()))) / r.Average();
        float steady = Enumerable.Range(1, nv - 1).Count(v => beam[v] == beam[v - 1] && height[v] == height[v - 1]) / (float)(nv - 1);
        int e = Math.Max(1, nv / 6);
        float ends = (beam.Take(e).Average() + beam.Skip(nv - e).Average()) / 2f, middle = beam.Skip(nv / 4).Take(nv / 2).Average();
        return new[]
        {
            MathF.Log(nv / (float)nu), MathF.Log(nv / (float)ny), MathF.Log(nu / (float)ny), Holes(side), Holes(top), steady, Straight(top), Straight(side), Straight(front),
            Fill(top), Ragged(top), Fill(side), Ragged(side), cells.Count / (float)(fu * fy),
            mismatch / (float)Math.Max(1, area), mirror, Turned(MathF.PI / 2), MathF.Max(Turned(MathF.PI / 3), Turned(MathF.PI / 4)), past,
            rep,
            beam.Take(Math.Max(1, nv / 12)).Average() / maxBeam, ends / MathF.Max(1f, middle),
            plan.Form.Drives.Count / 8f, Spread(plan.Form.Drives.Select(x => x.Radius).ToList()), Ragged(front),
            beyond / (float)Math.Max(1, area), over / (float)Math.Max(1, sideArea),
            bumps * 10f / nv, waist, topSd,
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
