using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Renders one view of the viewer to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/poi-generator res://tests/Screenshot.tscn -- --view=garrison_active --out=shot.png
/// Views: garrison_active, garrison_infested, garrison_interior, vault_ruined, mine_abandoned,
/// lab_infested, spire_active, aerial, overlay.
/// </summary>
public partial class Screenshot : Node
{
    const int SetupTick = 2;
    const int CaptureTick = 150;

    Main _main;
    int _tick;
    string _view = "garrison_active";
    string _out = "shot.png";

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        ProcessPhysicsPriority = 100;
        // Debanding dithers every pixel, which PNG can't compress; screenshots don't need it.
        GetViewport().UseDebanding = false;
        _main = new Main();
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_tick == SetupTick) Setup();
        if (_tick == CaptureTick) Capture();
    }

    void Setup()
    {
        var m = _main;
        // contact:<family,family,...>:<seeds> lays plans side by side; floors:<family>:<seed> shows one building storey by storey.
        if (_view.StartsWith("contact:") || _view.StartsWith("floors:"))
        {
            ContactSheet(_view);
            return;
        }
        // walk:<family>:<variant>:<rarity>:<state>:<seed>:<storey|deep|top>[:<along 0..1>] stands on the way to the
        // vault on that storey, looking on along it, with the head torch on.
        if (_view.StartsWith("walk:"))
        {
            string[] a = _view.Split(':');
            m.Inspect(a[1], int.Parse(a[2]), System.Enum.Parse<Rarity>(a[3], true), System.Enum.Parse<PoiState>(a[4], true), ulong.Parse(a[5]));
            m.SetMode(Main.Mode.Walk);
            m.Stage.AllKeys = true;
            WalkOnStorey(a[6], a.Length > 7 ? float.Parse(a[7], System.Globalization.CultureInfo.InvariantCulture) : 0.5f);
            return;
        }
        // ground:<family>:<variant>:<rarity>:<state>:<seed>:<distance>:<yaw from entrance>: an eye-level view from far off.
        if (_view.StartsWith("ground:"))
        {
            string[] a = _view.Split(':');
            m.Inspect(a[1], int.Parse(a[2]), System.Enum.Parse<Rarity>(a[3], true), System.Enum.Parse<PoiState>(a[4], true), ulong.Parse(a[5]));
            float dist = float.Parse(a[6], System.Globalization.CultureInfo.InvariantCulture), yaw = Mathf.DegToRad(m.EntranceYaw() + float.Parse(a[7], System.Globalization.CultureInfo.InvariantCulture));
            var b = m.Stage.SurfaceBounds();
            Vector3 c = m.Stage.ToGlobal(b.GetCenter() with { Y = 0 });
            Vector3 eye = c + new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * dist + Vector3.Up * 1.7f;
            m.Camera.GlobalTransform = new Transform3D(Basis.Identity, eye).LookingAt(c + Vector3.Up * Mathf.Min(b.Size.Y * 0.45f, dist * 0.25f), Vector3.Up);
            m.FreezeCamera = true;
            m.Sky.PlacePlanetInView(m.Camera, 24f, 16f);
            return;
        }
        // inspect:<family>:<variant>:<rarity>:<state>:<seed>:<yaw>:<pitch>:<distance> frames anything.
        if (_view.StartsWith("inspect:"))
        {
            string[] a = _view.Split(':');
            m.Inspect(a[1], int.Parse(a[2]), System.Enum.Parse<Rarity>(a[3], true), System.Enum.Parse<PoiState>(a[4], true), ulong.Parse(a[5]));
            Orbit(float.Parse(a[6]), float.Parse(a[7]), float.Parse(a[8]), 20f, 14f);
            return;
        }
        switch (_view)
        {
            case "garrison_active":
                m.Inspect("vharn_outpost", 0, Rarity.Uncommon, PoiState.Active, 5);
                Orbit(32f, -11f, 0.42f, -20f, 14f);
                break;
            case "garrison_infested":
                m.Inspect("vharn_outpost", 0, Rarity.Uncommon, PoiState.Infested, 5);
                Orbit(32f, -11f, 0.42f, -20f, 14f);
                break;
            case "garrison_interior":
                m.Inspect("vharn_outpost", 0, Rarity.Uncommon, PoiState.Infested, 5);
                m.SetMode(Main.Mode.Walk);
                WalkInto();
                break;
            case "vault_ruined":
                m.Inspect("precursor_vault", 0, Rarity.Rare, PoiState.Ruined, 3);
                Orbit(25f, -4f, 0.45f, 20f, 14f); // low, so the monoliths loom
                break;
            case "mine_abandoned":
                m.Inspect("abandoned_mine", 0, Rarity.Common, PoiState.Abandoned, 2);
                Orbit(30f, -4f, 0.9f, -24f, 20f);
                break;
            case "lab_infested":
                m.Inspect("research_lab", 1, Rarity.Uncommon, PoiState.Infested, 4);
                m.SetMode(Main.Mode.Walk);
                WalkInto();
                break;
            case "spire_active":
                m.Inspect("kessari_spire", 0, Rarity.Uncommon, PoiState.Active, 6);
                Orbit(40f, -8f, 0.7f, 25f, 20f);
                break;
            case "overlay":
                m.Inspect("vharn_outpost", 0, Rarity.Rare, PoiState.Active, 5);
                m.Map.Visible = true;
                break;
            case "aerial":
            {
                m.SetMode(Main.Mode.Aerial);
                m.RevealAll();
                // Look across the two closest places, from the side, low enough to see their shapes.
                var (a, b) = m.ClosestPair();
                Vector3 mid = (a + b) / 2f, across = (b - a).Normalized(), side = across.Cross(Vector3.Up).Normalized();
                float span = a.DistanceTo(b);
                m.SetAerialCamera(mid + side * (span * 0.9f + 50f) + Vector3.Up * (span * 0.45f + 30f), mid);
                m.Sky.PlacePlanetInView(m.Camera, 18f, 22f);
                break;
            }
        }
    }

    /// <summary>Orbits the inspector camera relative to the way in, and hangs the sky world in view.</summary>
    void Orbit(float yawFromEntrance, float pitch, float distance, float planetYaw, float planetElevation)
    {
        _main.SetOrbit(_main.EntranceYaw() + yawFromEntrance, pitch, distance);
        _main.Sky.PlacePlanetInView(_main.Camera, planetYaw, planetElevation);
    }

    /// <summary>Stands a little way inside the place, past the door, looking deeper in.</summary>
    void WalkInto()
    {
        var stage = _main.Stage;
        var poi = stage.Poi;
        // Pick a cell partway along the critical path, look along the path from there.
        var path = poi.CriticalPath;
        if (path.Count < 12) return;
        Int3 at = path[path.Count * 2 / 5], ahead = path[System.Math.Min(path.Count - 1, path.Count * 2 / 5 + 6)];
        var feet = stage.ToGlobal(new Vector3(at.X + 0.5f, at.Y + 0.05f, at.Z + 0.5f));
        var look = stage.ToGlobal(new Vector3(ahead.X + 0.5f, ahead.Y + 1.2f, ahead.Z + 0.5f)) - (feet + Vector3.Up * 1.6f);
        float yaw = Mathf.Atan2(-look.X, -look.Z);
        _main.Player.Place(feet, yaw);
        _main.Player.SetLook(yaw, Mathf.Clamp(Mathf.Asin(look.Normalized().Y), -0.4f, 0.3f));
    }

    /// <summary>Layout maps from above in a grid, each labelled with its family, seed, storey and plan.</summary>
    void ContactSheet(string view)
    {
        var lib = _main.Library;
        var tiles = new List<(Image Img, string Label)>();
        string[] a = view.Split(':');
        if (a[0] == "contact")
        {
            int n = int.Parse(a[2]);
            foreach (string fam in a[1].Split(','))
                for (int i = 0; i < n; i++)
                {
                    var f = lib.Family(fam);
                    ulong seed = 1000 + (ulong)i * 7919;
                    var l = PoiFactory.BuildLayout(lib, f, 0, Rarity.Uncommon, seed);
                    // The storey with the most rooms: the plan that says most about the place.
                    var storey = l.Extras.Levels.OrderByDescending(lv => l.Modules.Count(m => m.Storey == lv.Storey && m.Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault)).First();
                    tiles.Add((LayoutMap.Storey(l, storey.WalkY, 3), $"{fam}  seed {seed}\n{storey.Name}: {LayoutMap.PlanName(l, storey.Storey)}"));
                }
        }
        else
        {
            var f = lib.Family(a[1]);
            var l = PoiFactory.BuildLayout(lib, f, 0, Rarity.Uncommon, ulong.Parse(a[2]));
            foreach (var lv in l.Extras.Levels.Where(lv => l.Modules.Any(m => m.Storey == lv.Storey && m.Kind == ModuleKind.Corridor)))
                tiles.Add((LayoutMap.Storey(l, lv.WalkY, 3), $"{f.Id}  {lv.Name}\n{LayoutMap.PlanName(l, lv.Storey)}"));
        }
        var layer = new CanvasLayer { Layer = 20 };
        AddChild(layer);
        layer.AddChild(new ColorRect { Color = new Color(0.03f, 0.035f, 0.04f), AnchorRight = 1, AnchorBottom = 1 });
        int cols = a[0] == "contact" ? int.Parse(a[2]) : System.Math.Min(5, tiles.Count);
        var grid = new GridContainer { Columns = cols, Position = new Vector2(10, 10) };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 6);
        layer.AddChild(grid);
        var viewport = GetViewport().GetVisibleRect().Size;
        int rows = (tiles.Count + cols - 1) / cols;
        float cellW = (viewport.X - 20 - 8 * (cols - 1)) / cols, cellH = (viewport.Y - 20 - 6 * (rows - 1)) / rows - 34;
        foreach (var (img, label) in tiles)
        {
            var box = new VBoxContainer();
            var tex = new TextureRect { Texture = ImageTexture.CreateFromImage(img), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(cellW, cellH) };
            var text = new Label { Text = label };
            text.AddThemeFontSizeOverride("font_size", 12);
            text.AddThemeColorOverride("font_color", new Color("e8dcc8"));
            box.AddChild(tex);
            box.AddChild(text);
            grid.AddChild(box);
        }
        _main.HideHud(true);
    }

    /// <summary>Stands on the way to the vault on one storey, a fraction of the way along it, looking on.</summary>
    void WalkOnStorey(string storey, float along)
    {
        var stage = _main.Stage;
        var poi = stage.Poi;
        var levels = poi.Layout.Extras.Levels;
        int walkY = storey switch
        {
            "deep" => poi.CriticalPath.Min(c => c.Y),
            "top" => poi.CriticalPath.Max(c => c.Y),
            _ => levels.FirstOrDefault(l => l.Storey == int.Parse(storey))?.WalkY ?? 0,
        };
        var path = poi.CriticalPath;
        var on = Enumerable.Range(0, path.Count).Where(i => path[i].Y == walkY).ToList();
        if (on.Count == 0) on = Enumerable.Range(0, path.Count).ToList();
        int i0 = on[Mathf.Clamp((int)(on.Count * along), 0, on.Count - 1)];
        Int3 at = path[i0];
        Int3 ahead = path[System.Math.Min(path.Count - 1, i0 + 6)];
        if (ahead.Y != at.Y) ahead = path[System.Math.Max(0, i0 - 1)] == at ? at : at;
        var feet = stage.ToGlobal(new Vector3(at.X + 0.5f, at.Y + 0.05f, at.Z + 0.5f));
        var look = stage.ToGlobal(new Vector3(ahead.X + 0.5f, at.Y + 1.4f, ahead.Z + 0.5f)) - (feet + Vector3.Up * 1.6f);
        if (look.LengthSquared() < 0.01f) look = new Vector3(0, 0, -1);
        float yaw = Mathf.Atan2(-look.X, -look.Z);
        _main.Player.Place(feet, yaw);
        _main.Player.SetLook(yaw, Mathf.Clamp(Mathf.Asin(look.Normalized().Y), -0.3f, 0.2f));
        _main.Player.Lamp.Visible = true;
        _main.Player.ProcessMode = ProcessModeEnum.Disabled;
        _main.SettleInterior();
    }

    async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
