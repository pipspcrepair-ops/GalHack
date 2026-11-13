using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AssetViewer;

/// <summary>
/// The asset viewer: a browser of everything the game generates (creatures, places, blocks, weather...),
/// each one shown on a slow turntable with a camera you can orbit or fly, a Randomise button for
/// anything that can be rolled again, and a notes panel (a comment and two ticks per asset and per
/// animation) that is written to a JSON file as you go.
///
/// Command line (after "--"): --select=&lt;name or key&gt;, --seed=N, --option=Name=Value, --anim=name,
/// --shot=file.png (render and quit), --list (print every asset and quit), --fresh (ignore the saved position),
/// --notes=folder, --hide-panels, --free.
/// </summary>
public partial class Viewer : Node
{
    /// <summary>Tests point the notes at a scratch folder before the viewer starts.</summary>
    public static string NotesFolderOverride;

    public AssetRegistry Registry { get; private set; }
    public NoteStore Notes { get; private set; }
    public Stage Stage { get; private set; }
    public ViewCamera Cam { get; private set; }

    // What is on show.
    public AssetEntry Entry { get; private set; }
    public AssetInstance Instance => Stage?.Current;
    public ulong Seed { get; private set; }
    public readonly Dictionary<string, string> Options = new();
    public string Animation { get; private set; } = "";
    public string BuildError { get; private set; } = "";

    // View settings.
    public bool Spin = true;
    public float SpinSpeed = 0.35f;      // radians a second: a full turn in about 18 s
    public float AnimSpeed = 1f;
    bool _wireframe;

    int _frameIn = -1;                   // frames until the camera frames the new asset
    bool _frameWanted = true;
    float _saveIn = -1f;                 // seconds until the autosave
    float _selectIn = -1f;
    AssetEntry _selectEntry;
    string _shotPath;
    int _shotFrames;

    public override void _Ready()
    {
        RenderingServer.SetDebugGenerateWireframes(true);
        Registry = AssetRegistry.Discover();
        ResourceMount.Mount(Registry.Providers.SelectMany(p => p.Mounts));
        Notes = new NoteStore(NotesFolderOverride ?? NoteStore.DefaultFolder());

        string[] args = OS.GetCmdlineUserArgs();
        if (args.Contains("--list"))
        {
            foreach (AssetEntry e in Registry.Entries) GD.Print($"asset {e.Key} | {e.Group} | {e.Name}{(e.Randomisable ? " | random" : "")}");
            foreach (var f in Registry.Failures) GD.PrintErr($"provider {f.Provider}: {f.Error}");
            GD.Print($"{Registry.Entries.Count} assets in {Registry.Categories.Count} categories");
            GetTree().Quit(Registry.Failures.Count == 0 ? 0 : 1);
            return;
        }

        Stage = new Stage { Name = "Stage" };
        AddChild(Stage);
        Cam = new ViewCamera { Name = "Camera" };
        AddChild(Cam);
        Cam.KeysBlocked = TextHasFocus;
        Cam.ArrowsBlocked = () => TextHasFocus() || ListHasFocus();
        Cam.PointerInView = PointerInView;

        BuildUi();
        GetTree().AutoAcceptQuit = false;

        bool fresh = args.Contains("--fresh");
        AssetEntry first = null;
        string select = args.FirstOrDefault(a => a.StartsWith("--select=", StringComparison.Ordinal))?["--select=".Length..];
        if (select != null) first = FindByText(select);
        if (first != null)
        {
            ulong? seed = null;
            string s = args.FirstOrDefault(a => a.StartsWith("--seed=", StringComparison.Ordinal));
            if (s != null && ulong.TryParse(s["--seed=".Length..], out ulong sv)) seed = sv;
            var opts = new Dictionary<string, string>();
            foreach (string a in args.Where(a => a.StartsWith("--option=", StringComparison.Ordinal)))
            {
                string[] kv = a["--option=".Length..].Split('=', 2);
                if (kv.Length == 2) opts[kv[0]] = kv[1];
            }
            Select(first, seed, opts.Count > 0 ? opts : null, true);
            string anim = args.FirstOrDefault(a => a.StartsWith("--anim=", StringComparison.Ordinal))?["--anim=".Length..];
            if (anim != null) PlayAnimation(anim);
        }
        else if (!fresh && Notes.Last != null && Registry.Find(Notes.Last.Asset) != null) ApplyPosition(Notes.Last);
        else if (Registry.Entries.Count > 0) Select(Registry.Entries[0], null, null, true);
        else RefreshPanels();

        if (args.Contains("--demo") && Entry != null)
        {
            // For screenshots: some notes, so the ticks, marks and counts show.
            EditNote(n => { n.Comment = "Looks right, but the edges are too sharp."; n.Like = true; });
            if (Animation != "") EditAnimationNote(n => { n.Comment = "Stops dead at the top."; n.Problem = true; });
            RefreshPanels();
        }
        if (args.Contains("--free")) SetFreeCamera(true);
        if (args.Contains("--hide-panels")) SetPanelsVisible(false);
        string shot = args.FirstOrDefault(a => a.StartsWith("--shot=", StringComparison.Ordinal));
        SheetArgs sheet = SheetArgs.Parse(args);
        if (sheet != null) StartSheetMode(sheet);
        else if (shot != null) { _shotPath = shot["--shot=".Length..]; _shotFrames = 0; }
        if (Registry.Failures.Count > 0) Toast($"{Registry.Failures.Count} providers failed: {Registry.Failures[0].Provider}", 6f);
    }

    AssetEntry FindByText(string text)
    {
        AssetEntry e = Registry.Find(text);
        if (e != null) return e;
        e = Registry.Entries.FirstOrDefault(x => string.Equals(x.Name, text, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Id, text, StringComparison.OrdinalIgnoreCase));
        return e ?? Registry.Entries.FirstOrDefault(x => x.Key.Contains(text, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    // ---- selecting and building -------------------------------------------------------------------

    /// <summary>Asks for an asset after a moment, so arrowing down a long list doesn't build every row it passes.</summary>
    public void SelectSoon(AssetEntry e)
    {
        _selectEntry = e;
        _selectIn = 0.12f;
    }

    public static ulong DefaultSeed(AssetEntry e)
    {
        ulong h = 1469598103934665603UL;
        foreach (char c in e.Key) { h ^= c; h *= 1099511628211UL; }
        return h % 1000000UL;
    }

    /// <summary>
    /// Shows an asset. Seed and options come from the arguments, else from the last note filed for
    /// the asset, else the defaults.
    /// </summary>
    public void Select(AssetEntry e, ulong? seed, Dictionary<string, string> options, bool frame)
    {
        _selectIn = -1f;
        Entry = e;
        AssetNote note = Notes.Get(e);
        Seed = seed ?? note?.Seed ?? DefaultSeed(e);
        Options.Clear();
        foreach (AssetOption o in e.Options)
        {
            string value = o.Values.Length > 0 ? o.Values[Mathf.Clamp(o.Default, 0, o.Values.Length - 1)] : "";
            if (options != null && options.TryGetValue(o.Name, out string v) && o.Values.Contains(v)) value = v;
            else if (options == null && note != null && note.Options.TryGetValue(o.Name, out string nv) && o.Values.Contains(nv)) value = nv;
            Options[o.Name] = value;
        }
        Animation = "";
        Rebuild(frame);
    }

    /// <summary>Builds the current asset again with the current seed and options.</summary>
    public void Rebuild(bool frame)
    {
        if (Entry == null) return;
        string keepAnimation = Animation;
        AssetInstance instance;
        BuildError = "";
        try
        {
            var request = new BuildRequest { Seed = Seed };
            foreach (var kv in Options) request.Options[kv.Key] = kv.Value;
            instance = Entry.Build(request);
            if (instance == null) throw new InvalidOperationException("the builder returned nothing");
        }
        catch (Exception ex)
        {
            BuildError = ex.GetBaseException().Message;
            GD.PrintErr($"building {Entry.Key} failed: {ex}");
            instance = new AssetInstance { Name = "Failed" };
            instance.Stage.Ground = true;
        }
        Stage.Present(instance);
        instance.SetSpeed(AnimSpeed);
        _frameWanted = frame || _frameWanted;
        _frameIn = 2;   // let _Ready run and streamed pieces appear before measuring
        // Keep the same animation across a re-roll when it still exists, else start the first.
        string[] anims = instance.Animations.ToArray();
        string start = anims.Contains(keepAnimation) ? keepAnimation : anims.Length > 0 ? anims[0] : "";
        Animation = start;
        if (start != "") instance.PlayAnimation(start);
        GetWindow().Title = $"Asset Viewer - {Entry.Category} / {Entry.Name}";
        RefreshPanels();
    }

    public void PlayAnimation(string name)
    {
        Animation = name ?? "";
        if (Instance != null && Animation != "") Instance.PlayAnimation(Animation);
        RefreshAnimationPanel();
    }

    public void StepAnimation(int dir)
    {
        string[] anims = Instance?.Animations.ToArray() ?? Array.Empty<string>();
        if (anims.Length == 0) return;
        int i = Array.IndexOf(anims, Animation);
        PlayAnimation(anims[(i + dir + anims.Length) % anims.Length]);
    }

    public void SetOption(string name, string value)
    {
        Options[name] = value;
        Rebuild(false);
        NoteContextChanged();
    }

    public void SetSeed(ulong seed)
    {
        Seed = seed;
        Rebuild(false);
        NoteContextChanged();
    }

    /// <summary>A fresh seed and a value for each of the asset's rollable options.</summary>
    public static (ulong Seed, Dictionary<string, string> Options) Roll(AssetEntry e)
    {
        var rolled = new Dictionary<string, string>();
        foreach (AssetOption o in e.Options.Where(o => o.Rollable && o.Values.Length > 1)) rolled[o.Name] = o.Values[Random.Shared.Next(o.Values.Length)];
        return ((ulong)Random.Shared.NextInt64(1, 1000000000), rolled);
    }

    /// <summary>Rolls a new seed (and any rollable options) and rebuilds.</summary>
    public void Randomise()
    {
        if (Entry == null || !Entry.Randomisable) { Toast("Not randomisable", 1.5f); return; }
        var (seed, rolled) = Roll(Entry);
        Seed = seed;
        foreach (var kv in rolled) Options[kv.Key] = kv.Value;
        Rebuild(false);
        NoteContextChanged();
    }

    /// <summary>If this asset already has notes, keep their seed and options in step with what is on screen.</summary>
    void NoteContextChanged()
    {
        if (Entry != null && Notes.Get(Entry) is { Empty: false }) { Notes.Touch(Entry, Seed, Options); ScheduleSave(); }
    }

    // ---- frame loop -------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Stage == null) return;
        if (_selectIn >= 0f && (_selectIn -= dt) < 0f && _selectEntry != null) { var e = _selectEntry; _selectEntry = null; Select(e, null, null, true); }

        if (_frameIn >= 0 && --_frameIn < 0) FrameNow();

        bool spins = Instance == null || Instance.Spins;
        if (Spin && spins) Stage.Pivot.RotateY(SpinSpeed * dt);
        Cam.Drift = Spin && !spins ? SpinSpeed * 0.6f : 0f;

        if (_saveIn >= 0f && (_saveIn -= dt) < 0f) SaveNow(false);
        UpdateToast(dt);
        UpdatePanelsEachFrame();

        if (_shotPath != null && ++_shotFrames > 40) TakeShot();
    }

    /// <summary>Measures the asset and puts the camera where the whole of it is in view.</summary>
    public void FrameNow()
    {
        if (Instance == null) return;
        Aabb box = MeasureBounds();
        Stage.FitGround(box);
        if (_frameWanted)
        {
            _frameWanted = false;
            float radius = box.Size.Length() * 0.5f;
            Cam.MinDistance = Mathf.Max(0.02f, radius * Instance.Stage.MinDistanceShare);
            Cam.Frame(box.GetCenter(), radius, FreeAreaAspect());
            if (Instance.Stage.StartInside)
            {
                Vector3 centre = box.GetCenter();
                Cam.PlaceFree(new Vector3(centre.X, box.Position.Y + 1.7f, centre.Z + radius * 0.25f), new Vector3(centre.X, box.Position.Y + 1.5f, centre.Z));
                SyncCameraMode();
            }
        }
    }

    /// <summary>Width over height of the part of the window between the panels, which is what the asset must fit in.</summary>
    float FreeAreaAspect()
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        float width = size.X - (_panelsVisible ? LeftWidth + RightWidth : 0f);
        return Mathf.Clamp(width / Mathf.Max(1f, size.Y), 0.3f, 3f);
    }

    public Aabb MeasureBounds()
    {
        if (Instance?.FrameBounds is Aabb given) return given;
        Aabb? total = null;
        Measure(Instance, ref total);
        if (total is not Aabb box || box.Size.LengthSquared() < 1e-6f) return new Aabb(new Vector3(-1f, 0f, -1f), new Vector3(2f, 2f, 2f));
        return box;
    }

    static void Measure(Node node, ref Aabb? total)
    {
        if (node is Node3D n3 && !n3.Visible) return;
        if (node is MeshInstance3D or MultiMeshInstance3D)
        {
            var vi = (VisualInstance3D)node;
            Aabb local = vi.GetAabb();
            if (local.Size.LengthSquared() > 0f && local.Size.Length() < 2e6f)
            {
                Aabb world = vi.GlobalTransform * local;
                total = total.HasValue ? total.Value.Merge(world) : world;
            }
        }
        foreach (Node child in node.GetChildren()) Measure(child, ref total);
    }

    void TakeShot()
    {
        string path = _shotPath;
        _shotPath = null;
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(path);
        GD.Print(result == Error.Ok ? $"saved {path}" : $"could not save {path}: {result}");
        SaveNow(false);
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }

    // ---- saving -----------------------------------------------------------------------------------

    public void ScheduleSave() => _saveIn = 1.2f;

    public void SaveNow(bool announce)
    {
        _saveIn = -1f;
        if (Entry != null) Notes.Last = CapturePosition("last");
        bool ok = Notes.Save();
        if (announce) Toast(ok ? $"Saved to {Notes.NotesPath}" : Notes.Problem, ok ? 3f : 6f);
        RefreshSaveLabel();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            if (Notes != null && Stage != null) SaveNow(false);
            GetTree().Quit();
        }
    }

    // ---- small helpers ----------------------------------------------------------------------------

    bool TextHasFocus() => GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;
    bool ListHasFocus() => GetViewport().GuiGetFocusOwner() is Tree or ItemList;

    public void SetWireframe(bool on)
    {
        _wireframe = on;
        GetViewport().DebugDraw = on ? Viewport.DebugDrawEnum.Wireframe : Viewport.DebugDrawEnum.Disabled;
    }

    public bool Wireframe => _wireframe;
}
