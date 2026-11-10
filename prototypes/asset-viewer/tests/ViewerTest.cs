using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace AssetViewer.Tests;

/// <summary>
/// Headless checks: every provider lists, every listed asset builds, the notes file round-trips, and
/// the viewer itself starts, selects, rerolls and saves a position without errors.
/// </summary>
public partial class ViewerTest : Node
{
    int _passes, _failures;

    bool Check(bool ok, string what)
    {
        if (ok) _passes++; else { _failures++; GD.PrintErr("  FAIL " + what); }
        return ok;
    }

    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception e) { _failures++; GD.PrintErr("  FAIL threw: " + e); }
        GD.Print($"\n{_passes} checks passed, {_failures} failed");
        GD.Print(_failures == 0 ? "ALL PASSED" : "SOME CHECKS FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async System.Threading.Tasks.Task CameraChecks(Viewer viewer)
    {
        GD.Print("== camera");
        ViewCamera cam = viewer.Cam;
        cam.PointerInView = () => true;
        viewer.Spin = false;
        cam.Frame(Vector3.Zero, 2f);
        float yaw = cam.Yaw, dist = cam.Distance;
        // Orbit: a left drag turns, the wheel zooms.
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(800, 400) });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(860, 400), Relative = new Vector2(60, 0) });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(860, 400) });
        await Frames(1);
        Check(!Mathf.IsEqualApprox(cam.Yaw, yaw), "a left drag turns the orbit");
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new Vector2(800, 400) });
        await Frames(1);
        Check(cam.Distance < dist, "the wheel zooms in");
        Vector3 target = cam.Target;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(800, 400) });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(850, 430), Relative = new Vector2(50, 30) });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = new Vector2(850, 430) });
        await Frames(1);
        Check(cam.Target.DistanceTo(target) > 0.01f, "a right drag pans");

        // Free camera: toggle, and W flies forward.
        viewer.SetFreeCamera(true);
        Check(cam.IsFree, "free camera on");
        cam.PlaceFree(new Vector3(0, 2, 10), Vector3.Zero);
        await Frames(2);
        Vector3 from = cam.Position;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = true });
        for (int i = 0; i < 20; i++) await Frames(1);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = false });
        await Frames(1);
        Check(cam.Position.DistanceTo(from) > 0.05f && cam.Position.Z < from.Z, "W flies the free camera forward");
        float freeYaw = cam.FreeYaw;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(800, 400) });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(830, 400), Relative = new Vector2(40, 0) });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = new Vector2(830, 400) });
        await Frames(2);
        Check(!Mathf.IsEqualApprox(cam.FreeYaw, freeYaw), "holding right mouse looks round in free mode");
        viewer.SetFreeCamera(false);
        Check(!cam.IsFree, "back to orbit");
        Check(cam.Distance > 0f && !float.IsNaN(cam.Position.X), "leaving free mode gives a sane orbit");
    }

    async System.Threading.Tasks.Task Run()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "asset-viewer-test-" + System.Environment.ProcessId);
        Directory.CreateDirectory(scratch);

        GD.Print("== registry");
        var registry = AssetRegistry.Discover();
        Check(registry.Failures.Count == 0, "providers listed cleanly: " + string.Join("; ", registry.Failures.Select(f => f.Provider + ": " + f.Error)));
        Check(registry.Entries.Count > 0, "there are assets");
        Check(registry.Entries.Select(e => e.Key).Distinct().Count() == registry.Entries.Count, "keys are unique");
        GD.Print($"  {registry.Entries.Count} assets in {registry.Categories.Count} categories");

        GD.Print("== every asset builds");
        int built = 0;
        foreach (AssetEntry e in registry.Entries)
        {
            var req = new BuildRequest { Seed = Viewer.DefaultSeed(e) };
            foreach (AssetOption o in e.Options) req.Options[o.Name] = o.Values.Length > 0 ? o.Values[Math.Clamp(o.Default, 0, o.Values.Length - 1)] : "";
            try
            {
                AssetInstance a = e.Build(req);
                if (Check(a != null, $"{e.Key} builds")) { built++; a.QueueFree(); }
            }
            catch (Exception ex) { Check(false, $"{e.Key} threw: {ex.GetBaseException().Message}"); }
        }
        GD.Print($"  {built} built");

        GD.Print("== notes file");
        var store = new NoteStore(scratch);
        var entry = registry.Entries[0];
        store.GetOrCreate(entry).Comment = "wrong colour, \"quoted\"";
        store.GetOrCreate(entry).Like = true;
        store.GetOrCreateAnimation(entry, "walk").Problem = true;
        store.GetOrCreateAnimation(entry, "walk").Comment = "foot slides";
        store.GetOrCreateAnimation(entry, "idle");   // empty: must not be written
        store.Touch(entry, 1234, new Dictionary<string, string> { ["State"] = "Burnt" });
        store.Bookmarks.Add(new Position { Name = "here", Asset = entry.Key, Seed = 7 });
        Check(store.Save(), "saves");
        Check(File.Exists(store.NotesPath) && File.Exists(store.PositionsPath), "both files exist");
        var again = new NoteStore(scratch);
        AssetNote n = again.Get(entry);
        Check(n != null && n.Comment == "wrong colour, \"quoted\"" && n.Like && !n.Problem, "comment and ticks survive");
        Check(n != null && n.Animations.ContainsKey("walk") && n.Animations["walk"].Problem && n.Animations["walk"].Comment == "foot slides", "animation note survives");
        Check(n != null && !n.Animations.ContainsKey("idle"), "empty animation note is not written");
        Check(n != null && n.Options.GetValueOrDefault("State") == "Burnt", "options kept");
        Check(again.Bookmarks.Count == 1 && again.Bookmarks[0].Seed == 7, "bookmark survives");
        Check(again.Report().Contains("foot slides"), "report carries the animation comment");
        string json = File.ReadAllText(store.NotesPath);
        Check(json.Contains("\"really_like\": true") && json.Contains("\"problem\": true"), "json uses readable names");

        GD.Print("== viewer");
        Viewer.NotesFolderOverride = Path.Combine(scratch, "viewer");
        var viewer = new Viewer();
        AddChild(viewer);
        await Frames(6);
        Check(viewer.Entry != null && viewer.Instance != null, "viewer shows something on start");
        var random = registry.Entries.FirstOrDefault(e => e.Randomisable);
        if (random != null)
        {
            viewer.Select(random, null, null, true);
            await Frames(4);
            ulong before = viewer.Seed;
            viewer.Randomise();
            await Frames(3);
            Check(viewer.Seed != before, "randomise changes the seed");
            Check(viewer.Instance != null, "randomise builds again");
        }
        var animated = registry.Entries.FirstOrDefault(e =>
        {
            try { var a = e.Build(new BuildRequest { Seed = 1 }); bool any = a.Animations.Count > 0; a.QueueFree(); return any; } catch (Exception) { return false; }
        });
        if (animated != null)
        {
            viewer.Select(animated, null, null, true);
            await Frames(4);
            Check(viewer.Animation != "", "an animated asset starts its first animation");
        }
        await CameraChecks(viewer);
        viewer.SavePosition("test spot");
        Position saved = viewer.Notes.Bookmarks.Last();
        Check(saved.Asset == viewer.Entry.Key, "position records the asset");
        viewer.Cam.Frame(new Vector3(1, 2, 3), 2f);
        viewer.SetFreeCamera(true);
        Check(viewer.ApplyPosition(saved), "position loads");
        Check(!viewer.Cam.IsFree, "position restores orbit mode");
        viewer.SaveNow(false);
        Check(File.Exists(Path.Combine(scratch, "viewer", NoteStore.PositionsFile)), "viewer wrote its positions");

        viewer.QueueFree();
        try { Directory.Delete(scratch, true); } catch (Exception) { }
    }
}
