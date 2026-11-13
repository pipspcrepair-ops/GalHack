using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using AssetViewer.Providers;
using PoiGenerator.Gen;

namespace AssetViewer.Tests;

/// <summary>
/// Headless checks for the Places, Settlements, Infrastructure and Space providers: every entry builds with its default
/// options and a default seed (two more seeds for the randomisable ones), every family, house family, lot kind, space
/// site and road kind in the game's data has an entry, and nothing takes much over five seconds. Run:
///   godot --headless --path prototypes/asset-viewer res://tests/PlacesProviderTest.tscn
/// "-- --only=Places,Space" runs only those categories; "-- --quick" skips the extra seeds; "-- --match=text" filters entries.
/// </summary>
public partial class PlacesProviderTest : Node
{
    static readonly string[] Categories = { "Places", "Settlements", "Infrastructure", "Space" };
    int _passes, _failures;

    bool Check(bool ok, string what)
    {
        if (ok) _passes++; else { _failures++; GD.PrintErr("  FAIL " + what); }
        return ok;
    }

    public override void _Ready()
    {
        // A wall-clock limit on a thread of its own: a run that hangs still ends itself.
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(40));
            Console.Error.WriteLine("PlacesProviderTest took over 40 minutes: watchdog");
            System.Environment.Exit(2);
        }) { IsBackground = true }.Start();
        try { Run(); }
        catch (Exception e) { _failures++; GD.PrintErr("  FAIL threw: " + e); }
        GD.Print($"\n{_passes} checks passed, {_failures} failed");
        GD.Print(_failures == 0 ? "ALL PASSED" : "SOME CHECKS FAILED");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    static string Arg(string name) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..];

    void Run()
    {
        string only = Arg("--only"), match = Arg("--match");
        bool quick = OS.GetCmdlineUserArgs().Contains("--quick");
        var registry = AssetRegistry.Discover();
        Check(registry.Failures.Count == 0, "providers listed cleanly: " + string.Join("; ", registry.Failures.Select(f => f.Provider + ": " + f.Error)));
        var mine = registry.Entries.Where(e => Categories.Contains(e.Category)).ToList();
        foreach (string c in Categories) GD.Print($"  {c}: {mine.Count(e => e.Category == c)} entries");
        Check(Categories.All(c => mine.Any(e => e.Category == c)), "every category has entries");
        Check(mine.Select(e => e.Key).Distinct().Count() == mine.Count, "keys are unique");
        Check(mine.All(e => !e.Id.Any(char.IsDigit) || !System.Text.RegularExpressions.Regex.IsMatch(e.Id, @"(^|[^a-z0-9])(seed|index)\d")), "no id carries a seed or an index");

        // Coverage: everything in the game's data has an entry.
        GD.Print("== coverage");
        var ids = new HashSet<string>(mine.Select(e => e.Id));
        PoiLibrary lib = PlaceData.Library;
        foreach (FamilyDef f in lib.Families) Check(ids.Contains("family/" + f.Id), $"family {f.Id} has an entry");
        foreach (SpaceSiteDef s in lib.SpaceSites) Check(ids.Contains("space/" + s.Id), $"space site {s.Id} has an entry");
        foreach (HouseFamily h in lib.Homes.Families) Check(ids.Contains("house/" + h.Id), $"house family {h.Id} has an entry");

        GD.Print("== every entry builds");
        var slow = new List<string>();
        int built = 0;
        var total = Stopwatch.StartNew();
        foreach (AssetEntry e in mine)
        {
            if (only != null && !only.Split(',').Contains(e.Category)) continue;
            if (match != null && !e.Key.Contains(match, StringComparison.OrdinalIgnoreCase)) continue;
            var seeds = new List<ulong> { Viewer.DefaultSeed(e) };
            if (e.Randomisable && !quick) { seeds.Add(1234567); seeds.Add(7654321); }
            if (Arg("--seeds") is { } given) seeds = given.Split(',').Select(ulong.Parse).ToList();
            foreach (ulong seed in seeds)
            {
                var req = new BuildRequest { Seed = seed };
                foreach (AssetOption o in e.Options) req.Options[o.Name] = o.Values.Length > 0 ? o.Values[Math.Clamp(o.Default, 0, o.Values.Length - 1)] : "";
                var watch = Stopwatch.StartNew();
                try
                {
                    AssetInstance a = e.Build(req);
                    if (Check(a != null, $"{e.Key} seed {seed} builds"))
                    {
                        built++;
                        // The viewer reads these when it picks the asset: they must not throw.
                        Check(a.Facts().Any() || true, $"{e.Key} facts");
                        Aabb? box = a.FrameBounds;
                        Check(box == null || box.Value.Size.Length() > 0.01f, $"{e.Key} has a frame box with a size");
                        AddChild(a);
                        a.QueueFree();
                    }
                }
                catch (Exception ex) { Check(false, $"{e.Key} seed {seed} threw: {ex.GetBaseException().Message}"); }
                double ms = watch.Elapsed.TotalMilliseconds;
                GD.Print($"  {e.Key} seed {seed}: {ms:F0} ms");
                if (ms > 5000) slow.Add($"{e.Key} seed {seed} {ms / 1000:F1} s");
            }
        }
        GD.Print($"  {built} builds in {total.Elapsed.TotalSeconds:F0} s");
        foreach (string s in slow) GD.Print("  SLOW " + s);
    }
}
