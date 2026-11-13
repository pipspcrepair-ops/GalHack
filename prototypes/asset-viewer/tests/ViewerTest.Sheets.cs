using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AssetViewer.Providers;
using Godot;

namespace AssetViewer.Tests;

/// <summary>The filmstrip tool, as far as it can be checked without a window: layout, slide, cycle lengths, the sidecars and the sweep. The pictures themselves are checked by looking at them.</summary>
public partial class ViewerTest
{
    void LayoutChecks()
    {
        GD.Print("== sheet layout");
        foreach (int phases in new[] { 1, 4, 8, 12, 20 })
        {
            SheetLayout l = SheetLayout.Make(phases, 3, 480, 600);
            Check(l.Width <= SheetLayout.MaxWidth, $"{phases} phases fit in {SheetLayout.MaxWidth} px (are {l.Width})");
            Check(l.Origin(phases - 1, 2).X + l.TileW <= l.Width && l.Origin(phases - 1, 2).Y + l.TileH <= l.Height, $"{phases} phases: the last tile is inside the sheet");
            Check(phases < 2 || l.Origin(1, 0).X - l.Origin(0, 0).X == l.TileW + SheetLayout.Gap, $"{phases} phases: tiles do not overlap");
        }
        SheetLayout small = SheetLayout.Make(4, 2, 200, 260);
        Check(small.TileW == 200 && small.TileH == 260, "a small sheet keeps the asked tile size");
        SheetLayout wide = SheetLayout.Make(16, 2, 300, 400);
        Check(Math.Abs(wide.TileW / (float)wide.TileH - 0.75f) < 0.03f, "a shrunk tile keeps its shape");

        int[] loop = SheetLayout.PhaseTicks(1f, true, 8);
        Check(loop.Length == 8 && loop[0] == 0 && loop[7] == 52 && loop.Zip(loop.Skip(1), (a, b) => b - a).All(d => d >= 7 && d <= 8), "a loop's phases are evenly spaced over one period and leave out its repeat");
        int[] once = SheetLayout.PhaseTicks(2f, false, 5);
        Check(once[0] == 0 && once[4] == 119 && once.Distinct().Count() == 5, "a one-shot's phases run from its first tick to its last");
        Check(SheetLayout.PhaseTicks(3f, true, 1).SequenceEqual(new[] { 0 }), "one phase is the start");

        var frame = ViewFrame.Fit("side", new Aabb(new Vector3(-0.5f, 0f, -1f), new Vector3(1f, 1.8f, 2f)), 0.8f);
        Vector2 tile = new(240f, 300f);
        Vector2 mid = frame.Project(frame.Centre, Vector3.Zero, 0f, tile);
        Check(mid.DistanceTo(tile * 0.5f) < 0.01f, "the middle of the motion projects to the middle of the tile");
        Vector2 head = frame.Project(new Vector3(0f, 1.8f, 0f), Vector3.Zero, 0f, tile), foot = frame.Project(Vector3.Zero, Vector3.Zero, 0f, tile);
        Check(head.Y < foot.Y && head.Y > 0f && foot.Y < tile.Y, "up is up in the side view and the whole height is in the tile");
        Vector2 nose = frame.Project(new Vector3(0f, 1f, -1f), Vector3.Zero, 0f, tile), tail = frame.Project(new Vector3(0f, 1f, 1f), Vector3.Zero, 0f, tile);
        Check(nose.X > tail.X, "the subject faces right in the side view");
        Vector2 follow = frame.Project(new Vector3(0f, 0.9f, 7f), new Vector3(0f, 0f, 7f), 0f, tile);
        Vector2 turned = frame.Project(new Basis(Vector3.Up, 1f) * new Vector3(0f, 1f, -1f), Vector3.Zero, 1f, tile);
        Check(turned.DistanceTo(nose) < 0.01f, "the views turn with the subject: its nose stays where it was in the tile");
        Check(follow.X.Equals(mid.X) || Math.Abs(follow.X - mid.X) < 0.01f, "the camera follows the subject along the ground");
        Check(ViewFrame.Names.All(n => ViewFrame.Fit(n, new Aabb(new Vector3(-1f, 0f, -1f), new Vector3(2f, 2f, 2f)), 1f).Size > 0f), "every view frames a box");
    }

    void SlideChecks()
    {
        GD.Print("== slide and cycle");
        var tracker = new SlideTracker();
        FootMark Foot(float z, bool planted) => new(new Vector3(0f, 0f, z), new Vector3(0f, 0f, z), planted);
        tracker.Update(new[] { Foot(0f, true), Foot(0f, false) }, Vector3.Zero);
        tracker.Update(new[] { Foot(0.1f, true), Foot(0.5f, false) }, Vector3.Zero);
        Check(Math.Abs(tracker.Now[0] - 0.1f) < 1e-5f && float.IsNaN(tracker.Now[1]), "a planted foot that moves slides; a foot in the air does not count");
        tracker.Update(new[] { Foot(0.1f, false), Foot(0.5f, true) }, Vector3.Zero);
        tracker.Update(new[] { Foot(0.4f, true), Foot(0.5f, true) }, Vector3.Zero);
        Check(Math.Abs(tracker.Now[0]) < 1e-5f && Math.Abs(tracker.WorstEach[0] - 0.1f) < 1e-5f, "a foot coming down again starts afresh, and the worst is kept");
        var walker = new SlideTracker();
        for (int i = 0; i < 30; i++) walker.Update(new[] { Foot(i * 0.03f, true) }, new Vector3(0f, 0f, i * 0.03f));
        Check(walker.Worst < 1e-4f, "a foot that goes back with the ground does not slide");

        var sine = Enumerable.Range(0, 300).Select(i => new[] { MathF.Sin(i / 30f * MathF.Tau / 0.9f), MathF.Cos(i / 30f * MathF.Tau / 0.9f * 2f) }).ToArray();
        Check(Math.Abs(SheetCycle.Period(sine, 1f / 30f) - 0.9f) < 0.05f, "the period of a repeating pose is found");
        Check(SheetCycle.Period(Enumerable.Range(0, 300).Select(_ => new[] { 1f, 2f }).ToArray(), 1f / 30f) == 0f, "a pose that never moves has no period");
    }

    /// <summary>One of each kind of stage, so every animation of every one of them has a cycle length.</summary>
    IEnumerable<(string Name, AssetInstance Stage, Func<AssetInstance> Rebuild)> StagesToCheck(AssetRegistry registry)
    {
        AssetEntry Find(string key) => registry.Find(key) ?? throw new InvalidOperationException("no asset " + key);
        AssetEntry person = registry.Entries.First(e => e.Key.StartsWith("People|role/", StringComparison.Ordinal)), sleeper = Find("People|sleeper");
        yield return ("person", person.Build(new BuildRequest { Seed = 5 }), () => person.Build(new BuildRequest { Seed = 5 }));
        yield return ("sleeper", sleeper.Build(new BuildRequest { Seed = 5 }), () => sleeper.Build(new BuildRequest { Seed = 5 }));
        var seen = new HashSet<string>();
        foreach (AssetEntry e in registry.Entries.Where(e => e.Category == "Bestiary" && e.Randomisable))
        {
            AssetInstance stage;
            try { stage = e.Build(new BuildRequest { Seed = 11 }); } catch (Exception) { continue; }
            string kind = stage switch { CreatureStage c => "creature " + c.Species.Rig, SpaceStage => "space creature", _ => "" };
            if (kind == "" || !seen.Add(kind)) { stage.QueueFree(); continue; }
            AssetEntry entry = e;
            yield return (kind, stage, () => entry.Build(new BuildRequest { Seed = 11 }));
        }
    }

    void CycleChecks(AssetRegistry registry)
    {
        GD.Print("== cycle length of every animation of every stage");
        int stages = 0, animations = 0;
        foreach ((string name, AssetInstance stage, Func<AssetInstance> rebuild) in StagesToCheck(registry))
        {
            AddChild(stage);
            stages++;
            if (!Check(stage is IStageProbe, $"{name} can be stepped")) continue;
            var probe = (IStageProbe)stage;
            // Stepping is deterministic: a second stage from the same seed, given the same calls, reads the same.
            string first = stage.Animations[0];
            stage.PlayAnimation(first); probe.Advance(1.5f);
            AssetInstance twin = rebuild();
            AddChild(twin);
            twin.PlayAnimation(first); ((IStageProbe)twin).Advance(1.5f);
            Check(probe.PoseSignature() == ((IStageProbe)twin).PoseSignature(), $"{name} / {first}: the same seed and ticks give the same pose");
            Check(name.Contains("Swarm") || SheetCycle.BoundsOf(probe.Subject) is Aabb box && box.Size.Length() > 0.05f, $"{name}: the subject can be measured");
            twin.QueueFree();
            foreach (string anim in stage.Animations)
            {
                (float cycle, string source) = SheetCycle.Of(stage, anim);
                animations++;
                Check(cycle > 0.1f && cycle <= 60f && source is "scripted" or "measured" or "default", $"{name} / {anim}: cycle {cycle:0.00} s ({source}) is a usable length");
            }
            var feet = new List<FootMark>();
            probe.Feet(feet);
            Check(feet.All(f => float.IsFinite(f.Drawn.X) && float.IsFinite(f.Drawn.Y) && float.IsFinite(f.Target.Z)), $"{name}: foot marks are finite");
            stage.QueueFree();
        }
        GD.Print($"  {animations} animations on {stages} kinds of stage");
        Check(stages >= 4, "several kinds of stage were checked");
    }

    async System.Threading.Tasks.Task SweepChecks(Viewer viewer, AssetRegistry registry, string scratch)
    {
        GD.Print("== sweep");
        AssetEntry sleeper = registry.Find("People|sleeper");
        viewer.Select(sleeper, 3, new Dictionary<string, string>(), true);
        await Frames(3);
        string dir = Path.Combine(scratch, "sweep");
        int code = await viewer.Sweep(new Viewer.SheetArgs { Sweep = dir, Pinned = true, Phases = 3, Views = new[] { "front", "side" }, Seed = 3 });
        string[] sheets = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != "index.json").ToArray();
        Check(code == 0, "the sweep ends well");
        Check(sheets.Length == sleeper.Build(new BuildRequest()).Animations.Count, $"the sweep writes one sidecar per animation ({sheets.Length})");
        using JsonDocument index = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "index.json")));
        JsonElement asset = index.RootElement.GetProperty("assets")[0];
        Check(asset.GetProperty("seed").GetUInt64() == 3 && index.RootElement.GetProperty("seed_given").GetBoolean(), "index.json records the seed");
        Check(asset.GetProperty("animations").GetArrayLength() == sheets.Length, "index.json lists every animation");
        string walk = sheets.First(f => Path.GetFileName(f) == "Walk.json");
        using JsonDocument sheet = JsonDocument.Parse(File.ReadAllText(walk));
        JsonElement root = sheet.RootElement;
        foreach (string key in new[] { "asset", "seed", "options", "animation", "cycle_seconds", "cycle_source", "frames", "worst_slide_m", "frames_with_problems", "views" })
            Check(root.TryGetProperty(key, out _), $"the sidecar has {key}");
        JsonElement frames = root.GetProperty("frames");
        Check(frames.GetArrayLength() == 3, "the sidecar has a record per phase");
        Check(frames[0].TryGetProperty("problems", out _) && frames[0].TryGetProperty("pose_signature", out _) && frames[0].GetProperty("slide_m").GetArrayLength() == root.GetProperty("feet").GetInt32(), "each frame has problems, pose signature and a slide per foot");
        Check(frames.EnumerateArray().Select(f => f.GetProperty("pose_signature").GetInt64()).Distinct().Count() == 3, "the phases of a walk are different poses");
        Check(root.GetProperty("cycle_seconds").GetSingle() > 0.3f, "a walk has a cycle");

        // Quits by itself: a second process runs a sweep and has to end with its files written.
        string childDir = Path.Combine(scratch, "child");
        var output = new Godot.Collections.Array();
        string[] args = { "--headless", "--audio-driver", "Dummy", "--path", ProjectSettings.GlobalizePath("res://"), "--", "--select=People|sleeper", "--sweep=" + childDir, "--anim=Walk", "--sheet=3", "--seed=3", "--fresh" };
        int exit = OS.Execute(OS.GetExecutablePath(), args, output, true);
        Check(exit == 0 && File.Exists(Path.Combine(childDir, "index.json")), $"a sweep run quits by itself with its files written (exit {exit})");
    }
}
