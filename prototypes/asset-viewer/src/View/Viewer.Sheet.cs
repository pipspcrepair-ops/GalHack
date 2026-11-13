using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetViewer.Providers;
using Godot;

namespace AssetViewer;

/// <summary>
/// Filmstrips for a reviewer who can look at pictures but not at motion: one PNG with the phases of an animation across and the camera
/// views down, and a JSON beside it with the numbers. <c>--sheet[=N] --shot=out.png</c> makes one; <c>--sweep=DIR</c> makes one for every
/// animation of the chosen assets in one run. Stepping is by fixed ticks, never by the clock, so one seed always gives one sheet.
/// </summary>
public partial class Viewer
{
    /// <summary>What the command line asked of the sheet tool.</summary>
    public sealed class SheetArgs
    {
        public int Phases = 8;
        public string[] Views = { "front", "side" };
        public int TileW = 240, TileH = 300;
        public float? Seconds;
        public string Shot, Sweep, Category, Anim;
        public ulong? Seed;
        public Dictionary<string, string> Options = new();
        public int Limit;
        public float Timeout;
        /// <summary>--select was given: the sweep is of that one asset, as it is on screen.</summary>
        public bool Pinned;

        static string Value(string[] args, string name) => args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];

        static float? Number(string[] args, string name) =>
            float.TryParse(Value(args, name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f > 0f ? f : null;

        /// <summary>Null unless the command line asks for a sheet or a sweep.</summary>
        public static SheetArgs Parse(string[] args)
        {
            bool sheet = args.Any(a => a == "--sheet" || a.StartsWith("--sheet=", StringComparison.Ordinal));
            string sweep = Value(args, "--sweep");
            if (!sheet && sweep == null) return null;
            var r = new SheetArgs { Sweep = sweep, Shot = Value(args, "--shot"), Category = Value(args, "--category"), Anim = Value(args, "--anim"), Pinned = Value(args, "--select") != null };
            if (int.TryParse(Value(args, "--sheet"), out int n) && n > 0) r.Phases = n;
            if (Value(args, "--views") is string v) r.Views = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(ViewFrame.Known).Distinct().ToArray();
            if (r.Views.Length == 0) r.Views = new[] { "front", "side" };
            if (Value(args, "--tile") is string t && t.Split('x') is { Length: 2 } wh && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h)) { r.TileW = w; r.TileH = h; }
            r.Seconds = Number(args, "--sheet-seconds");
            r.Timeout = Number(args, "--timeout") ?? 0f;
            if (ulong.TryParse(Value(args, "--seed"), out ulong seed)) r.Seed = seed;
            if (int.TryParse(Value(args, "--limit"), out int limit)) r.Limit = limit;
            foreach (string o in args.Where(a => a.StartsWith("--option=", StringComparison.Ordinal)))
                if (o["--option=".Length..].Split('=', 2) is { Length: 2 } kv) r.Options[kv[0]] = kv[1];
            return r;
        }
    }

    long _sheetDeadlineMs;
    bool TimedOut => (long)Time.GetTicksMsec() > _sheetDeadlineMs;

    void StartSheetMode(SheetArgs a)
    {
        // As fast as it will go, and certain to end: the process gives up on its own a little after the time allowed.
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        Spin = false;
        float allowed = a.Timeout > 0f ? a.Timeout : a.Sweep != null ? 1500f : 240f;
        _sheetDeadlineMs = (long)Time.GetTicksMsec() + (long)(allowed * 1000f);
        GalHack.Testing.Backstop.Arm(3, allowed + 40.0);
        _ = RunSheetMode(a);
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async Task RunSheetMode(SheetArgs a)
    {
        int code = 0;
        try
        {
            await Frames(3);
            if (a.Sweep != null) code = await Sweep(a);
            else
            {
                if (string.IsNullOrEmpty(a.Shot)) throw new InvalidOperationException("--sheet needs --shot=out.png");
                string anim = Animation;
                if (!Instance.Animations.Contains(anim)) throw new InvalidOperationException($"no animation \"{anim}\"; it has: {string.Join(" | ", Instance.Animations)}");
                SheetReport r = await CaptureSheet(a, anim, a.Shot);
                GD.Print($"saved {a.Shot} ({r.CycleSeconds:0.00} s {r.CycleSource}, worst slide {r.WorstSlideM:0.000} m, {r.FramesWithProblems.Length} frames with problems)");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr("sheet failed: " + ex);
            code = 1;
        }
        GetTree().Quit(code);
    }

    // ---- The sweep --------------------------------------------------------------------------------

    static string Safe(string text) => System.Text.RegularExpressions.Regex.Replace(text, "[^A-Za-z0-9]+", "_").Trim('_');

    IEnumerable<AssetEntry> SweepEntries(SheetArgs a)
    {
        if (a.Pinned) return new[] { Entry };
        if (a.Category == null) throw new InvalidOperationException("--sweep needs --select=<asset> or --category=people|creatures");
        string name = a.Category.ToLowerInvariant() == "creatures" ? "Bestiary" : a.Category;
        var list = Registry.Entries.Where(e => string.Equals(e.Category, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0) throw new InvalidOperationException($"no category \"{a.Category}\" (have: {string.Join(", ", Registry.Categories)})");
        return a.Limit > 0 ? list.Take(a.Limit) : list;
    }

    /// <summary>Renders every animation of the chosen assets into DIR/&lt;asset&gt;/&lt;animation&gt;.png and .json, and writes DIR/index.json with the seeds. Returns the exit code.</summary>
    public async Task<int> Sweep(SheetArgs a)
    {
        Directory.CreateDirectory(a.Sweep);
        var assets = new List<object>();
        int sheets = 0, failures = 0;
        bool stopped = false;
        void WriteIndex() => File.WriteAllText(Path.Combine(a.Sweep, "index.json"), SheetReport.ToJson(new { seed_given = a.Seed.HasValue, sheets, failures, stopped_at_time_limit = stopped, assets }));
        foreach (AssetEntry e in SweepEntries(a))
        {
            if (TimedOut) { stopped = true; break; }
            // Fresh seeds every run unless one is given; the ones used are in index.json so any sheet can be made again.
            var (rolledSeed, rolled) = Roll(e);
            ulong seed = a.Seed ?? (e.Randomisable ? rolledSeed : DefaultSeed(e));
            var options = e.Randomisable && a.Seed == null ? rolled : new Dictionary<string, string>();
            if (a.Pinned) foreach (var kv in a.Options) options[kv.Key] = kv.Value;
            Select(e, seed, options, false);
            await Frames(3);
            string folder = Path.Combine(a.Sweep, Safe(e.Key));
            var anims = new List<object>();
            string[] names = (Instance?.Animations ?? Array.Empty<string>()).Where(n => a.Anim == null || n == a.Anim).ToArray();
            foreach (string anim in names)
            {
                if (TimedOut) { stopped = true; break; }
                string png = Path.Combine(folder, Safe(anim) + ".png");
                try
                {
                    SheetReport r = await CaptureSheet(a, anim, png);
                    sheets++;
                    anims.Add(new { animation = anim, png = Path.GetRelativePath(a.Sweep, png).Replace('\\', '/'), cycle_seconds = r.CycleSeconds, cycle_source = r.CycleSource, worst_slide_m = r.WorstSlideM, frames_with_problems = r.FramesWithProblems.Length });
                }
                catch (Exception ex)
                {
                    failures++;
                    GD.PrintErr($"{e.Key} / {anim}: {ex.GetBaseException().Message}");
                    anims.Add(new { animation = anim, error = ex.GetBaseException().Message });
                }
            }
            assets.Add(new { asset = e.Key, name = e.Name, seed = Seed, options = Options.ToDictionary(kv => kv.Key, kv => kv.Value), build_error = BuildError, animations = anims });
            WriteIndex();
            GD.Print($"swept {e.Key}: {anims.Count} animations");
            if (stopped) break;
        }
        WriteIndex();
        GD.Print($"sweep done: {sheets} sheets, {failures} failures{(stopped ? ", stopped at the time limit" : "")}");
        return stopped ? 3 : failures > 0 ? 1 : 0;
    }

    // ---- One sheet --------------------------------------------------------------------------------

    static bool Rendering => DisplayServer.GetName() != "headless";

    /// <summary>Plays the animation from the start and settles the stage so the first reading is of its first tick.</summary>
    static void Restart(AssetInstance stage, string anim)
    {
        stage.PlayAnimation(anim);
        ((IStageProbe)stage).Advance(0f);
    }

    async Task<SheetReport> CaptureSheet(SheetArgs a, string anim, string png)
    {
        AssetInstance inst = Instance;
        if (inst is not IStageProbe probe) throw new InvalidOperationException($"{Entry.Key} has no stage that can be stepped");
        Stage.Pivot.Rotation = Vector3.Zero;
        inst.SetSpeed(0f);
        (float cycle, string source) = SheetCycle.Of(inst, anim);
        if (a.Seconds is float over) { cycle = over; source = "override"; }
        bool loops = inst.CycleLoops;
        int cycleTicks = Math.Max(1, (int)MathF.Round(cycle * 60f));
        // A loop is settled first, for whole cycles, so a gait is in its stride and a script is at its own start.
        int warm = loops ? (int)MathF.Ceiling(2f / cycle) * cycleTicks : 0;
        var layout = SheetLayout.Make(a.Phases, a.Views.Length, a.TileW, a.TileH);
        const float dt = 1f / 60f;

        // First pass: the extent of the motion over the cycle, so every tile of a row is framed the same.
        Restart(inst, anim);
        Aabb? span = null;
        for (int t = 0; t < warm + cycleTicks; t++)
        {
            if (t > 0) probe.Advance(dt);
            if (t >= warm && (t - warm) % 4 == 0 && SheetCycle.BoundsOf(probe.Subject) is Aabb b)
            {
                Vector3 at = probe.Anchor;
                var rel = new Aabb(b.Position - new Vector3(at.X, 0f, at.Z), b.Size);
                span = span.HasValue ? span.Value.Merge(rel) : rel;
            }
        }
        Aabb extent = span ?? new Aabb(new Vector3(-1f, 0f, -1f), new Vector3(2f, 2f, 2f));
        float aspect = layout.TileW / (float)layout.TileH;
        ViewFrame[] frames = a.Views.Select(v => ViewFrame.Fit(v, extent, aspect)).ToArray();
        float reach = frames.Max(f => f.Size) * MathF.Max(1f, aspect) * 0.8f + 1f;

        var viewports = new SubViewport[frames.Length];
        var cameras = new Camera3D[frames.Length];
        if (Rendering)
            for (int v = 0; v < frames.Length; v++)
            {
                viewports[v] = new SubViewport { Size = new Vector2I(layout.TileW, layout.TileH), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Msaa3D = Viewport.Msaa.Msaa4X, GuiDisableInput = true };
                cameras[v] = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = frames[v].Size, Near = frames[v].Near, Far = frames[v].Far, Current = true };
                viewports[v].AddChild(cameras[v]);
                AddChild(viewports[v]);
            }
        var ground = new GroundMarks { Name = "SheetGround" };
        inst.AddChild(ground);

        // Second pass: the same cycle again, ticked one sixtieth of a second at a time; the feet are watched at every tick, the frames taken at their phases.
        var report = new SheetReport
        {
            Asset = Entry.Key, AssetName = Entry.Name, Seed = Seed, Options = Options.ToDictionary(kv => kv.Key, kv => kv.Value), Animation = anim,
            CycleSeconds = cycle, CycleSource = source, Loops = loops, Views = a.Views, Phases = layout.Phases, TileW = layout.TileW, TileH = layout.TileH,
        };
        var tracker = new SlideTracker();
        var feet = new List<FootMark>();
        int[] phaseTicks = SheetLayout.PhaseTicks(cycle, loops, layout.Phases);
        var tiles = new Texture2D[layout.Phases, layout.Views];
        var marks = new TileMarks[layout.Phases, layout.Views];
        var tile = new Vector2(layout.TileW, layout.TileH);
        Restart(inst, anim);
        int next = 0;
        try
        {
            // The first draw of a new viewport comes out without its shadows and sky: draw once and throw it away.
            if (Rendering)
            {
                for (int v = 0; v < frames.Length; v++) cameras[v].Transform = frames[v].Eye(probe.Anchor);
                await Draw(viewports);
            }
            for (int t = 0; t < warm + cycleTicks && next < phaseTicks.Length; t++)
            {
                if (t > 0) probe.Advance(dt);
                feet.Clear();
                probe.Feet(feet);
                tracker.Update(feet, probe.GroundShift ?? Vector3.Zero);
                while (next < phaseTicks.Length && t == warm + phaseTicks[next])
                {
                    int p = next++;
                    Vector3 at = probe.Anchor;
                    ground.Lay(probe.GroundShift, at.Z + extent.GetCenter().Z - reach, at.Z + extent.GetCenter().Z + reach, extent.Size.Length() * 0.03f);
                    float seconds = phaseTicks[p] * dt;
                    report.Frames.Add(new FrameRecord
                    {
                        Index = p, Seconds = seconds, Phase = seconds / cycle, Problems = probe.Problems(), PoseSignature = probe.PoseSignature(),
                        SlideM = tracker.Now.Select(s => float.IsNaN(s) ? (float?)null : s).ToArray(),
                        OffTargetM = feet.Select(f => f.Target.DistanceTo(f.Drawn)).ToArray(),
                    });
                    report.Feet = feet.Count;
                    if (!Rendering) continue;
                    for (int v = 0; v < frames.Length; v++) cameras[v].Transform = frames[v].Eye(at);
                    await Draw(viewports);
                    for (int v = 0; v < frames.Length; v++)
                    {
                        tiles[p, v] = ImageTexture.CreateFromImage(viewports[v].GetTexture().GetImage());
                        var m = new TileMarks { Label = $"{p + 1}/{layout.Phases}  {seconds:0.00} s", View = frames[v].Name };
                        foreach (FootMark f in feet) m.Feet.Add((frames[v].Project(f.Target, at, tile), frames[v].Project(f.Drawn, at, tile), f.Planted));
                        marks[p, v] = m;
                    }
                }
            }
        }
        finally
        {
            ground.QueueFree();
            foreach (SubViewport vp in viewports) vp?.QueueFree();
        }
        report.Summarise(tracker);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(png)));
        File.WriteAllText(Path.ChangeExtension(png, ".json"), report.ToJson());
        if (Rendering) await Compose(layout, tiles, marks, report, png);
        return report;
    }

    /// <summary>Has each viewport draw once, and waits until it has.</summary>
    async Task Draw(SubViewport[] viewports)
    {
        foreach (SubViewport vp in viewports) vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await Frames(1);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    /// <summary>Lays the tiles out with their labels and the foot marks on top, and saves the PNG.</summary>
    async Task Compose(SheetLayout layout, Texture2D[,] tiles, TileMarks[,] marks, SheetReport r, string png)
    {
        string title = $"{Entry.Category} / {r.AssetName}  |  {r.Animation}  |  seed {r.Seed}  |  cycle {r.CycleSeconds:0.00} s ({r.CycleSource}{(r.Loops ? "" : ", one-shot")})  |  ground ticks every 0.5 m  |  green: planted target, orange: in the air, ring: drawn foot, pink: target to drawn";
        var canvas = new SheetCanvas { Layout = layout, Title = title, Tiles = tiles, Marks = marks, Size = new Vector2(layout.Width, layout.Height) };
        var vp = new SubViewport { Size = new Vector2I(layout.Width, layout.Height), Disable3D = true };
        vp.AddChild(canvas);
        AddChild(vp);
        await Draw(new[] { vp });
        Error saved = vp.GetTexture().GetImage().SavePng(png);
        vp.QueueFree();
        if (saved != Error.Ok) throw new IOException($"could not save {png}: {saved}");
    }
}
