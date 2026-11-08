using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Deco;
using GalHack.Items;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using PlanetSurface.World;
using PoiGenerator.Gen;
using Slice.Game;
using NV = System.Numerics.Vector3;

namespace Slice.Tests;

/// <summary>
/// Every world dressed by its layers (docs/DESIGN.md section 8). The data first: every lean names a real
/// value, every part a model, every theme leans to something. Then the models, by era: each stands on
/// the ground (nothing floats, nothing sunk), nothing is see-through, and a low world's and a high world's
/// draw differently. Then the worlds: for every theme, a fresh start world forced to it (its type, fall,
/// eras and everything else rolled), its dressing counted within 500 m of the start and in the nearest
/// town other than the start's own; every theme shows some of its own, and every piece takes its world's
/// era. Last, the same theme on a low world and a high one: two different looks. Nothing pinned.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/DressSweep.tscn
/// (-- --themes=a,b: only those; --out=FILE: the counts as CSV)
/// </summary>
public partial class DressSweep : Node
{
    const int WatchdogMinutes = 45;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();
    readonly Dictionary<string, string> _args = new();

    /// <summary>The street furniture every settled world has: not a theme's own.</summary>
    static readonly HashSet<string> Base = new() { "lamp", "awning", "shop_sign", "sign_post" };

    /// <summary>Models that are the world's own stuff whatever its era (stone, earth, ash, cloth on the ground).</summary>
    static readonly HashSet<string> Timeless = new() { "cairn", "shell_hole", "burn_pit", "confetti", "costumes", "mark", "timber_frame" };

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--") && eq > 2) _args[arg[2..eq]] = arg[(eq + 1)..];
        }
        try
        {
            PoiLibrary lib = WorldChoice.Library;
            Data(lib);
            Models();
            Worlds(lib);
            EraPair();
        }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
    }

    // ---- the table ----------------------------------------------------------------------------

    void Data(PoiLibrary lib)
    {
        DressBook book = lib.Dressing;
        var tags = new HashSet<string>(lib.Layers.SelectMany(l => l.Tags).Concat(lib.Layers.Where(l => l.Kind == LayerKind.Dress).Select(l => l.Id)));
        var groups = new HashSet<string>(lib.Layers.Where(l => l.Group != null).Select(l => l.Group));
        var bad = new List<string>();
        foreach (var (sel, _) in book.Leans)
        {
            if (sel == "all") continue;
            string[] p = sel.Split('.', 2);
            bool ok = p.Length == 2 && p[0] switch
            {
                "tag" => tags.Contains(p[1]),
                "group" => groups.Contains(p[1]),
                _ => Enum.TryParse(p[0], true, out LayerKind k) && lib.Layers.Any(l => l.Kind == k && l.Id == p[1]),
            };
            if (!ok) bad.Add(sel);
        }
        Check(bad.Count == 0, $"the dressing table's {book.Leans.Count} leans each name a real value{(bad.Count > 0 ? $" (not: {string.Join(", ", bad)})" : "")}");
        var noModel = book.Parts.Values.Where(p => !DecoDressing.All.ContainsKey(p.Model)).Select(p => p.Id).ToList();
        Check(noModel.Count == 0, $"its {book.Parts.Count} parts each have a model ({DecoDressing.All.Count} models){(noModel.Count > 0 ? $"; none for {string.Join(", ", noModel)}" : "")}");
        var themes = lib.Layers.Where(l => l.Kind == LayerKind.Theme && !l.Blank).Select(l => l.Id).ToList();
        var bare = themes.Where(t => !book.Leans.Any(l => l.Selector == "theme." + t && l.Leans.Count > 0)).ToList();
        Check(bare.Count == 0, $"every theme ({themes.Count}) leans to its own dressing{(bare.Count > 0 ? $"; not {string.Join(", ", bare)}" : "")}");
        var falls = lib.Layers.Where(l => l.Kind == LayerKind.Fall).ToList();
        var fallBare = falls.Where(f => !book.Leans.Any(l => l.Selector == "fall." + f.Id || f.Group != null && l.Selector == "group." + f.Group)).Select(f => f.Id).ToList();
        Check(fallBare.Count == 0, $"every fall ({falls.Count}) leans to some, its own or its group's{(fallBare.Count > 0 ? $"; not {string.Join(", ", fallBare)}" : "")}");
        var used = book.Leans.SelectMany(l => l.Leans.Keys).ToHashSet();
        var unused = book.Parts.Keys.Where(k => !used.Contains(k)).ToList();
        Check(unused.Count == 0, $"every part is leant to by something{(unused.Count > 0 ? $"; not {string.Join(", ", unused)}" : "")}");
    }

    // ---- the models, by era -------------------------------------------------------------------

    static readonly string[] MarkStyles = { "cross", "tally", "plate", "tag", "chalk", "fist", "names", "waterline", "glyph", "mural", "zone", "arrow", "height", "notice", "gang", "sigil:sun" };

    static IEnumerable<NV> Points(DecoModel m)
    {
        foreach (DecoPart p in m.Parts)
            foreach (NV v in DecoEmit.Vertices(p)) yield return v;
    }

    /// <summary>What a model is made of: its slots and how many parts of each.</summary>
    static string Signature(DecoModel m) => string.Join(",", m.Parts.GroupBy(p => p.Slot).OrderBy(g => g.Key).Select(g => $"{g.Key}{g.Count()}")) + $"|{m.Cables.Count}";

    void Models()
    {
        var floating = new List<string>();
        var glass = new List<string>();
        int differ = 0, eraModels = 0;
        var same = new List<string>();
        foreach (var (id, _) in DecoDressing.All)
        {
            string[] styles = id switch { "mark" => MarkStyles, "tent" => new[] { "medical", "camp" }, "belongings" => new[] { "", "masks", "camp", "books" }, "bunting" => new[] { "", "prayer" }, _ => new[] { "" } };
            bool wall = id is "mark" or "awning" or "shop_sign";
            var sig = new Dictionary<DecoTech, HashSet<string>>();
            foreach (DecoTech era in Enum.GetValues<DecoTech>())
            {
                sig[era] = new HashSet<string>();
                foreach (string style in styles)
                    for (int k = 0; k < 4; k++)
                    {
                        var args = new DressArgs(style, era, k * 0.3f, k % 2 == 0, (ulong)_rng.NextInt64(), 9f, 0f, 0f);
                        DecoModel m = DecoDressing.Build(id, args);
                        if (m.Parts.Count == 0) { floating.Add($"{id}/{style}/{era}: nothing drawn"); continue; }
                        sig[era].Add(Signature(m));
                        if (m.Parts.Any(p => p.Slot == DecoSlot.Glass || p.Is(PartFlags.Pane))) glass.Add($"{id}/{era}");
                        if (wall)
                        {
                            // On a wall: flat to its face or out from it, never behind it.
                            float back = Points(m).Max(v => v.Z);
                            if (back > 0.02f) floating.Add($"{id}/{style}/{era}: {back:F2} m behind its wall");
                            continue;
                        }
                        float lo = Points(m).Min(v => v.Y);
                        if (lo > 0.06f) floating.Add($"{id}/{style}/{era}: lowest point {lo:F2} m up");
                        if (lo < -0.5f) floating.Add($"{id}/{style}/{era}: {-lo:F2} m into the ground");
                    }
            }
            if (Timeless.Contains(id)) continue;
            eraModels++;
            if (!sig[DecoTech.Low].Overlaps(sig[DecoTech.High]) && !sig[DecoTech.Mid].Overlaps(sig[DecoTech.High]) && !sig[DecoTech.Low].Overlaps(sig[DecoTech.Mid])) differ++;
            else same.Add(id);
        }
        Check(floating.Count == 0, $"every dressing model stands on the ground or flat on its wall{(floating.Count > 0 ? $"; not: {string.Join("; ", floating.Take(12))}" : "")}");
        Check(glass.Count == 0, $"nothing in the dressing is see-through{(glass.Count > 0 ? $"; glass in {string.Join(", ", glass.Distinct())}" : "")}");
        GD.Print($"ERA models drawn three different ways (low, mid, high): {differ} of {eraModels}{(same.Count > 0 ? $"; alike somewhere: {string.Join(", ", same)}" : "")}");
        Check(differ >= eraModels * 0.9f, $"the dressing's models read their era: {differ} of {eraModels} differ low to mid to high");
    }

    // ---- the worlds ---------------------------------------------------------------------------

    sealed record Counted(string Theme, string World, string Era, int Start, int StartOwn, int Town, int TownOwn, string Top, double Ms);

    static double Dist(PlanetWorld w, double ax, double az, double bx, double bz) =>
        Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, w.Width), 2) + Math.Pow(Mathx.WrapDelta(az - bz, w.Height), 2));

    PlanetWorld Build(WorldForce force, string type = "random")
    {
        PlanetSurface.Poi.PoiSiting.StartWorld = true;
        WorldForce.Current = force;
        WorldChoice choice = WorldChoice.Fresh(type);
        PlanetWorld w = PlanetWorld.Build(choice.Spec(), Slice.Surface.SurfaceZone.PoiCatalog);
        WorldForce.Current = new WorldForce();
        GD.Print($"  world {choice}: {w.Layers}");
        return w;
    }

    /// <summary>Where a game starts on it (the start town's edge) and the nearest other town (a settled place, a city, a town's streets).</summary>
    static ((double X, double Z) Start, (double X, double Z)? Town) Spots(PlanetWorld w)
    {
        (double X, double Z) start = w.StartTown is StartTown t ? (t.EdgeX, t.EdgeZ) : (w.Landing.X, w.Landing.Y);
        (double, double)? own = w.StartTown is StartTown s ? (s.CenterX, s.CenterZ) : null;
        var towns = new List<(double X, double Z)>();
        foreach (var p in w.Pois)
            if (p.Tier is PoiTier.Town or PoiTier.City or PoiTier.Spaceport || p.Family.Tags.Contains("settlement")) towns.Add(p.Center);
        if (w.Infra.City is CityPlan c) towns.Add((c.CenterX, c.CenterZ));
        foreach (FlavourRuin f in w.Infra.Ruins.Where(f => f.Route != null && f.Family.Tags.Contains("house"))) towns.Add(f.Center);
        var others = towns.Where(t => own is not { } o || Dist(w, t.X, t.Z, o.Item1, o.Item2) > 400).OrderBy(t => Dist(w, t.X, t.Z, start.X, start.Z)).ToList();
        return (start, others.Count > 0 ? others[0] : null);
    }

    Counted Count(string theme, PlanetWorld w, double ms)
    {
        WorldDress d = w.Dress;
        var (start, town) = Spots(w);
        bool Near((double X, double Z) at, DressItem it, double r) => Dist(w, it.X, it.Z, at.X, at.Z) <= r;
        var nearStart = d.Items.Where(it => Near(start, it, 500)).ToList();
        var nearTown = town is { } tw ? d.Items.Where(it => Near(tw, it, 300)).ToList() : new List<DressItem>();
        string top = string.Join(" ", nearStart.Concat(nearTown).Where(it => !Base.Contains(it.Part.Id)).GroupBy(it => it.Part.Id).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key}:{g.Count()}"));
        return new Counted(theme, $"{w.Spec.Archetype} {w.Profile.Before}/{w.Profile.Now}", $"{d.Before}/{d.After}", nearStart.Count, nearStart.Count(it => !Base.Contains(it.Part.Id)),
            nearTown.Count, nearTown.Count(it => !Base.Contains(it.Part.Id)), top, ms);
    }

    void Worlds(PoiLibrary lib)
    {
        var themes = WorldForce.Values(lib, LayerKind.Theme);
        if (_args.TryGetValue("themes", out string only)) themes = themes.Where(only.Split(',').Contains).ToList();
        var rows = new List<Counted>();
        var wrongEra = new List<string>();
        var bare = new List<string>();
        GD.Print($"-- {themes.Count} themes, a fresh start world each --");
        foreach (string theme in themes)
        {
            var watch = Stopwatch.StartNew();
            PlanetWorld w = Build(new WorldForce { Theme = theme });
            double build = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            WorldDress d = w.Dress;
            Counted c = Count(theme, w, watch.Elapsed.TotalMilliseconds);
            rows.Add(c);
            var (st, _) = Spots(w);
            GD.Print($"  near the start: {string.Join(" ", d.Items.Where(it => Dist(w, it.X, it.Z, st.X, st.Z) <= 500).GroupBy(it => it.Part.Id).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}:{g.Count()}"))}");
            GD.Print($"SWEEP {theme,-22} {c.World,-26} start {c.StartOwn,4} own ({c.Start,4} all)  town {c.TownOwn,4} own ({c.Town,4} all)  [{c.Top}]  world {d.Items.Count} pieces, planned in {d.PlanMs:F0} ms (built in {build / 1000:F1} s)");
            if (c.StartOwn + c.TownOwn == 0) bare.Add(theme);
            DecoTech before = WorldDress.Era(w.Profile.Before), now = WorldDress.Era(w.Profile.Now);
            foreach (DressItem it in d.Items)
                if (it.Era != (it.Kept ? now : before)) { wrongEra.Add($"{theme}: {it.Part.Id} {it.Era}"); break; }
        }
        Check(bare.Count == 0, $"every theme shows its own dressing near the start or in the nearest town ({rows.Count - bare.Count} of {rows.Count}){(bare.Count > 0 ? $"; bare: {string.Join(", ", bare)}" : "")}");
        Check(wrongEra.Count == 0, $"every piece takes its world's era (the old one; the living's own where they keep it){(wrongEra.Count > 0 ? $"; not: {string.Join("; ", wrongEra.Take(6))}" : "")}");
        if (_args.TryGetValue("out", out string path))
        {
            var csv = new System.Text.StringBuilder("theme,world,era,start_all,start_own,town_all,town_own,top\n");
            foreach (Counted c in rows) csv.AppendLine($"{c.Theme},{c.World},{c.Era},{c.Start},{c.StartOwn},{c.Town},{c.TownOwn},{c.Top}");
            System.IO.File.WriteAllText(path, csv.ToString());
            GD.Print($"wrote {path}");
        }
    }

    /// <summary>The same theme on a low world and a high one: what stands in their streets is drawn two ways.</summary>
    void EraPair()
    {
        string theme = _args.GetValueOrDefault("pair", "parade_ground");
        var looks = new Dictionary<TechLevel, HashSet<string>>();
        var counts = new Dictionary<TechLevel, string>();
        foreach (var (old, now) in new[] { (TechLevel.Low, TechLevel.Low), (TechLevel.High, TechLevel.Mid) })
        {
            PlanetWorld w = Build(new WorldForce { Theme = theme, Before = old, Now = now });
            WorldDress d = w.Dress;
            var (start, _) = Spots(w);
            var near = d.Items.Where(it => Dist(w, it.X, it.Z, start.X, start.Z) <= 500).ToList();
            looks[old] = near.Select(it => Signature(DecoDressing.Build(it.Model, it.Args()))).ToHashSet();
            counts[old] = string.Join(" ", near.GroupBy(it => it.Era).Select(g => $"{g.Key}:{g.Count()}"));
            GD.Print($"ERA {theme} {old}/{now}: {near.Count} pieces near the start by era {counts[old]}");
        }
        int shared = looks[TechLevel.Low].Intersect(looks[TechLevel.High]).Count();
        int all = looks[TechLevel.Low].Union(looks[TechLevel.High]).Count();
        GD.Print($"ERA measure: {shared} of {all} distinct drawn pieces shared between the low and the high world ({(all == 0 ? 0 : 100f * shared / all):F0}%)");
        Check(all > 0 && shared <= all * 0.15f, $"a low world and a high one of the same theme look different: {shared} of {all} pieces drawn alike");
    }
}
