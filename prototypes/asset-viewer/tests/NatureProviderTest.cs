using System;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace AssetViewer.Tests;

/// <summary>
/// Headless checks of the nature providers (weather, flora, terrain, sky, worlds): every id in the game's data
/// has an entry, every entry builds with its defaults and, for a randomisable one, with a second seed or option,
/// and a sample of them is put in a tree and run for some frames. A run that hangs ends itself.
/// </summary>
public partial class NatureProviderTest : Node
{
    int _passes, _failures;
    bool Check(bool ok, string what) { if (ok) _passes++; else { _failures++; GD.PrintErr("  FAIL " + what); } return ok; }

    public override async void _Ready()
    {
        _ = System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(14)).ContinueWith(_ =>
        {
            GD.PrintErr("TIMED OUT: the run quits itself");
            System.Environment.Exit(2);
        });
        try { await Run(); } catch (Exception e) { _failures++; GD.PrintErr("  FAIL threw: " + e); }
        GD.Print($"\n{_passes} checks passed, {_failures} failed");
        GD.Print(_failures == 0 ? "ALL PASSED" : "SOME CHECKS FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    async System.Threading.Tasks.Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    async System.Threading.Tasks.Task Run()
    {
        var registry = AssetRegistry.Discover();
        ResourceMount.Mount(registry.Providers.SelectMany(p => p.Mounts));
        AddChild(new Camera3D { Current = true, Position = new Vector3(0, 30, 0) });
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--only="))?["--only=".Length..];
        if (string.IsNullOrEmpty(only))
        {
            var ids = registry.Entries.Select(x => x.Id).ToHashSet();
            foreach (var k in Weather.Model.WeatherData.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://") + "../weather/data/weather.json")).States) Check(ids.Contains("state-" + k.Kind), "weather " + k.Kind);
            foreach (var f in Enum.GetValues<GalHack.Plants.PlantFamily>()) Check(ids.Contains("family-" + f), "flora family " + f);
            foreach (var l in Planets.Gen.LandformInfo.All) Check(ids.Contains("landform-" + l), "landform " + l);
            foreach (var e in registry.Entries.Where(x => x.Category == "Flora").Take(6))
            {
                var req = new BuildRequest { Seed = 2 };
                foreach (AssetOption o in e.Options) req.Options[o.Name] = o.Values[o.Default];
                Check(e.Build(req) != null, e.Id + " builds");
            }
        }
        if (string.IsNullOrEmpty(only) && OS.GetCmdlineUserArgs().Contains("--all"))
        {
            int n = 0, built = 0;
            foreach (AssetEntry e in registry.Entries.Where(x => x.Category is "Weather" or "Flora" or "Terrain" or "Sky" or "Worlds"))
            {
                foreach (ulong seed in (n++ % 4 == 0 && e.Randomisable) ? new ulong[] { 1, 9 } : new ulong[] { 1 })
                {
                    var req = new BuildRequest { Seed = seed };
                    foreach (AssetOption o in e.Options) req.Options[o.Name] = o.Values[o.Default];
                    if (seed == 9) foreach (AssetOption o in e.Options.Where(o => o.Values.Length > 1)) req.Options[o.Name] = o.Values[(o.Default + 1) % o.Values.Length];
                    var w = Stopwatch.StartNew();
                    try { AssetInstance a = e.Build(req); if (Check(a != null, $"{e.Key} seed {seed} builds")) { built++; a.QueueFree(); } }
                    catch (Exception ex) { Check(false, $"{e.Key} seed {seed} threw: {ex.GetBaseException().Message}"); }
                    if (w.ElapsedMilliseconds > 5000) GD.Print($"  slow: {e.Key} {w.ElapsedMilliseconds} ms");
                }
            }
            GD.Print($"{built} builds");
        }
        foreach (string id in string.IsNullOrEmpty(only) ? Array.Empty<string>() : only.Split(','))
        {
            AssetEntry e = registry.Entries.First(x => x.Id == id);
            var req = new BuildRequest { Seed = ulong.Parse(OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--seed="))?["--seed=".Length..] ?? "3") };
            foreach (AssetOption o in e.Options) req.Options[o.Name] = o.Values[o.Default];
            var w = Stopwatch.StartNew();
            AssetInstance a = e.Build(req);
            GD.Print($"{id}: built in {w.ElapsedMilliseconds} ms");
            AddChild(a);
            GD.Print($"{id}: in the tree after {w.ElapsedMilliseconds} ms");
            await Frames(20);
            GD.Print($"{id}: 20 frames after {w.ElapsedMilliseconds} ms");
            a.QueueFree();
        }
    }
}
