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
            else if (arg == "--day") Main.Daylight = true;
            else if (arg == "--bare") Main.Bare = true;
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
        // gallery:<sheet>:<sheets> and interiors render many tiles in one run and save a sheet of them.
        bool sheet = _view.StartsWith("gallery:") || _view == "interiors";
        if (_tick == SetupTick)
        {
            if (sheet) Gallery(_view);
            else Setup(_view);
        }
        if (_tick == CaptureTick && !sheet) Capture();
    }

    // ---- the family gallery --------------------------------------------------------------

    /// <summary>The interiors sheet: a representative room or corridor of a dozen families.</summary>
    static readonly (string View, string Label)[] Interiors =
    {
        ("room:hospital:0:Uncommon:Abandoned:7:1", "Hospital: a ward"),
        ("room:prison_block:0:Uncommon:Abandoned:7:any", "Prison Block: the biggest hall"),
        ("room:deep_bunker:0:Uncommon:Abandoned:7:-1", "Deep Bunker: level one"),
        ("room:research_base:0:Uncommon:Abandoned:7:deep", "Research Base: the sublevel"),
        ("room:abandoned_factory:1:Uncommon:Abandoned:7:any", "Abandoned Factory: the shop floor"),
        ("room:old_mine:0:Common:Abandoned:7:deep", "Old Mine: the deep workings"),
        ("room:ominous_tower:0:Uncommon:Abandoned:7:top", "Ominous Tower: the top floor"),
        ("room:abandoned_spaceport:0:Exotic:Abandoned:7:0", "Abandoned Spaceport: the terminal"),
        ("room:survivor_town:0:Uncommon:Active:7:0", "Survivor Town: a home"),
        ("room:alien_cathedral:0:Rare:Abandoned:7:any", "Alien Cathedral: the nave"),
        ("room:alien_hive_ruin:0:Rare:Infested:7:deep", "Alien Hive Ruin: the deep hive"),
        ("room:crashed_freighter:0:Common:Abandoned:7:any", "Crashed Freighter: the hold"),
    };

    /// <summary>
    /// Renders tiles one after another in this run, checks each place's geometry, and lays them out
    /// as one labelled sheet. gallery:k:n is sheet k of n of every family's exterior in daylight, from
    /// the same three-quarter view 60 to 120 m off; interiors is one interior each of a dozen families.
    /// </summary>
    async void Gallery(string view)
    {
        var lib = _main.Library;
        var tiles = new List<(Image Img, string Label)>();
        int stray = 0;
        string firstStray = "";
        if (view == "interiors")
        {
            foreach (var (v, label) in Interiors)
            {
                Setup(v);
                _main.HideHud(true);
                await Frames(2);
                // A soft fill light at the eye instead of the head torch's hard spot, so the room reads.
                _main.Player.Lamp.Visible = false;
                var fill = new OmniLight3D { LightColor = new Color("fff0dc"), LightEnergy = 1.1f, OmniRange = 26f, OmniAttenuation = 0.9f, ShadowEnabled = false };
                GetViewport().GetCamera3D()?.AddChild(fill);
                await Frames(100);
                tiles.Add((await Grab(), label));
                stray += Check(ref firstStray);
                fill.QueueFree();
            }
        }
        else
        {
            string[] a = view.Split(':');
            int k = int.Parse(a[1]), n = int.Parse(a[2]);
            var families = lib.Families.Where(f => !f.Space && !f.Tags.Contains("flavour")).ToList();
            int from = families.Count * (k - 1) / n, to = families.Count * k / n;
            for (int i = from; i < to; i++)
            {
                FamilyDef f = families[i];
                // The state it is most often found in, its first layout, its own rarity.
                int state = 0;
                for (int s = 1; s < f.StateWeights.Length; s++) if (f.StateWeights[s] > f.StateWeights[state]) state = s;
                _main.Inspect(f.Id, 0, f.Rarity, (PoiState)state, 7);
                _main.HideHud(true);
                _main.Sky.Apply(Look.Day, 220f);
                // The same three-quarter view of each: from 35 degrees off the way in, 18 degrees up,
                // far enough back that the whole place fits (60 to 120 m for most, more for the tallest).
                var b = _main.Stage.SurfaceBounds();
                float r = 0.5f * b.Size.Length();
                float dist = Mathf.Clamp(Mathf.Max(r * 1.8f, b.Size.Y * 1.7f), 34f, 320f);
                Vector3 target = _main.Stage.ToGlobal(b.GetCenter() with { Y = b.Position.Y + b.Size.Y * 0.38f });
                float yaw = Mathf.DegToRad(_main.EntranceYaw() + 35f), pitch = Mathf.DegToRad(18f);
                Vector3 eye = target + new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * dist;
                eye.Y = Mathf.Max(eye.Y, 2f);
                _main.Camera.GlobalTransform = new Transform3D(Basis.Identity, eye).LookingAt(target, Vector3.Up);
                _main.FreezeCamera = true;
                await Frames(70);
                string tech = f.Variants[0].Tech is { Length: > 0 } t ? t : f.Techs.Count > 0 ? string.Join("/", f.Techs) : "any";
                tiles.Add((await Grab(), $"{f.Name}  ·  {((PoiState)state).ToString().ToLowerInvariant()}  ·  tech {tech}"));
                stray += Check(ref firstStray);
            }
        }
        GD.Print($"geometry check over the sheet: {stray} stray{firstStray}");
        if (stray > 0)
        {
            GD.Print($"could not save {_out}: stray geometry in the places shown");
            GalHack.Testing.Backstop.Quit(this, 3);
            return;
        }
        Sheet(tiles, view == "interiors" ? "Interiors: one representative space each (a soft fill light at the eye in place of the head torch)" : $"POI families, sheet {view.Split(':')[1]} of {view.Split(':')[2]}: exteriors in daylight");
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GalHack.Testing.Backstop.Quit(this, result == Error.Ok ? 0 : 1);
    }

    int Check(ref string first)
    {
        StrayGeometry(this, out _, out int stray, out string f);
        if (stray > 0 && first.Length == 0) first = f;
        return stray;
    }

    async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    async System.Threading.Tasks.Task<Image> Grab()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image img = GetViewport().GetTexture().GetImage();
        img.Resize(640, 360, Image.Interpolation.Lanczos);
        return img;
    }

    /// <summary>Lays tiles out four to a row, each captioned, under a title, over the whole window.</summary>
    void Sheet(List<(Image Img, string Label)> tiles, string title)
    {
        foreach (Node n in _main.GetChildren()) if (n is Node3D n3) n3.Visible = false;
        _main.HideHud(true);
        var layer = new CanvasLayer { Layer = 30 };
        AddChild(layer);
        layer.AddChild(new ColorRect { Color = new Color(0.03f, 0.035f, 0.04f), AnchorRight = 1, AnchorBottom = 1 });
        var head = new Label { Text = title, Position = new Vector2(14, 8) };
        head.AddThemeFontSizeOverride("font_size", 20);
        head.AddThemeColorOverride("font_color", new Color("ffe8c2"));
        layer.AddChild(head);
        const int cols = 4;
        var grid = new GridContainer { Columns = cols, Position = new Vector2(10, 44) };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        layer.AddChild(grid);
        var viewport = GetViewport().GetVisibleRect().Size;
        float cellW = (viewport.X - 20 - 8 * (cols - 1)) / cols, cellH = cellW * 9f / 16f;
        foreach (var (img, label) in tiles)
        {
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 2);
            box.AddChild(new TextureRect { Texture = ImageTexture.CreateFromImage(img), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(cellW, cellH) });
            var text = new Label { Text = label, CustomMinimumSize = new Vector2(cellW, 0), ClipText = true };
            text.AddThemeFontSizeOverride("font_size", 16);
            text.AddThemeColorOverride("font_color", new Color("e8dcc8"));
            box.AddChild(text);
            grid.AddChild(box);
        }
    }

    void Setup(string view)
    {
        var m = _main;
        // contact:<family,family,...>:<seeds> lays plans side by side; floors:<family>:<seed> shows one building storey by storey.
        if (view.StartsWith("contact:") || view.StartsWith("floors:"))
        {
            ContactSheet(view);
            return;
        }
        // room:<family>:<variant>:<rarity>:<state>:<seed>:<storey|deep|top|any> stands at one end of the biggest
        // roofed room or hall on that storey and looks down its length.
        if (view.StartsWith("room:"))
        {
            string[] a = view.Split(':');
            m.Inspect(a[1], int.Parse(a[2]), System.Enum.Parse<Rarity>(a[3], true), System.Enum.Parse<PoiState>(a[4], true), ulong.Parse(a[5]));
            m.SetMode(Main.Mode.Walk);
            m.Stage.AllKeys = true;
            StandInRoom(a.Length > 6 ? a[6] : "any");
            return;
        }
        // walk:<family>:<variant>:<rarity>:<state>:<seed>:<storey|deep|top>[:<along 0..1>] stands on the way to the
        // vault on that storey, looking on along it, with the head torch on.
        if (view.StartsWith("walk:"))
        {
            string[] a = view.Split(':');
            m.Inspect(a[1], int.Parse(a[2]), System.Enum.Parse<Rarity>(a[3], true), System.Enum.Parse<PoiState>(a[4], true), ulong.Parse(a[5]));
            m.SetMode(Main.Mode.Walk);
            m.Stage.AllKeys = true;
            WalkOnStorey(a[6], a.Length > 7 ? float.Parse(a[7], System.Globalization.CultureInfo.InvariantCulture) : 0.5f);
            return;
        }
        // tell:<family>:<variant>:<rarity>:<state>:<seed> faces a hidden room's breakable wall from across the room;
        // breach:... stands at a collapse breach's lip looking down into it.
        if (view.StartsWith("tell:") || view.StartsWith("breach:"))
        {
            string[] a = view.Split(':');
            m.Inspect(a[1], int.Parse(a[2]), System.Enum.Parse<Rarity>(a[3], true), System.Enum.Parse<PoiState>(a[4], true), ulong.Parse(a[5]));
            m.SetMode(Main.Mode.Walk);
            m.Stage.AllKeys = true;
            LookAtFeature(a[0] == "tell");
            return;
        }
        // ground:<family>:<variant>:<rarity>:<state>:<seed>:<distance>:<yaw from entrance>: an eye-level view from far off.
        if (view.StartsWith("ground:"))
        {
            string[] a = view.Split(':');
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
        if (view.StartsWith("inspect:"))
        {
            string[] a = view.Split(':');
            m.Inspect(a[1], int.Parse(a[2]), System.Enum.Parse<Rarity>(a[3], true), System.Enum.Parse<PoiState>(a[4], true), ulong.Parse(a[5]));
            Orbit(float.Parse(a[6]), float.Parse(a[7]), float.Parse(a[8]), 20f, 14f);
            return;
        }
        switch (view)
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
        float cellW = (viewport.X - 20 - 8 * (cols - 1)) / cols, cellH = (viewport.Y - 20 - 6 * (rows - 1)) / rows - 46;
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

    /// <summary>Stands back from a hidden room's cracked wall (or at a breach's lip) and looks at it, head torch on.</summary>
    void LookAtFeature(bool tell)
    {
        var stage = _main.Stage;
        var poi = stage.Poi;
        var nav = poi.Nav();
        Int3 stand, target;
        if (tell)
        {
            var s = poi.Layout.Extras.Secrets.FirstOrDefault();
            if (s == null) return;
            int floor = s.WallCells.Min(c => c.Y);
            Int3 wall = s.WallCells.Where(c => c.Y == floor).OrderBy(c => c.X + c.Z).ElementAt(s.WallCells.Count(c => c.Y == floor) / 2);
            Int3 outward = s.Facing.Vec();
            stand = wall + outward;
            for (int k = 4; k >= 1; k--)
                if (nav.Standable(wall + outward * k)) { stand = wall + outward * k; break; }
            target = wall + Int3.Up;
        }
        else
        {
            var b = poi.Layout.Extras.Breaches.FirstOrDefault();
            if (b == null) return;
            stand = b.SurfaceCell;
            var low = b.Cells.OrderBy(c => c.Y).First();
            target = low;
        }
        var feet = stage.ToGlobal(new Vector3(stand.X + 0.5f, stand.Y + 0.05f, stand.Z + 0.5f));
        var look = stage.ToGlobal(new Vector3(target.X + 0.5f, target.Y + 0.5f, target.Z + 0.5f)) - (feet + Vector3.Up * 1.6f);
        float yaw = Mathf.Atan2(-look.X, -look.Z);
        _main.Player.Place(feet, yaw);
        _main.Player.SetLook(yaw, Mathf.Clamp(Mathf.Asin(look.Normalized().Y), -0.9f, 0.3f));
        _main.Player.Lamp.Visible = true;
        _main.Player.ProcessMode = ProcessModeEnum.Disabled;
        _main.SettleInterior();
    }

    /// <summary>Stands at one end of the biggest roofed room or hall on a storey and looks down its longer axis.</summary>
    void StandInRoom(string storey)
    {
        var stage = _main.Stage;
        var poi = stage.Poi;
        var nav = poi.Nav();
        var rooms = poi.Layout.Modules.Where(md => md.Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault).ToList();
        if (rooms.Count == 0) rooms = poi.Layout.Modules.Where(md => md.Kind is ModuleKind.Corridor or ModuleKind.Cave).ToList();
        if (storey == "deep") rooms = rooms.Where(md => md.Storey == rooms.Min(r => r.Storey)).ToList();
        else if (storey == "top") rooms = rooms.Where(md => md.Storey == rooms.Max(r => r.Storey)).ToList();
        else if (int.TryParse(storey, out int n) && rooms.Any(md => md.Storey == n)) rooms = rooms.Where(md => md.Storey == n).ToList();
        // Or a room by what it's for: market-hall, ward, filter-hall, main-tunnel.
        else if (poi.Layout.Modules.Any(md => md.Purpose == storey)) rooms = poi.Layout.Modules.Where(md => md.Purpose == storey).ToList();
        bool Roofed(PlacedModule md)
        {
            Int3 c = new((md.Min.X + md.Max.X) / 2, md.Min.Y + 1, (md.Min.Z + md.Max.Z) / 2);
            for (int y = 2; y <= md.Shape.H + 4; y++) if (!poi.Blocks.Get(c + new Int3(0, y, 0)).IsAir) return true;
            return false;
        }
        foreach (PlacedModule md in rooms.OrderByDescending(r => (Roofed(r) ? 1 : 0, r.Shape.W * r.Shape.D)))
        {
            bool alongX = md.Shape.W >= md.Shape.D;
            int lo = alongX ? md.Min.X : md.Min.Z, hi = alongX ? md.Max.X : md.Max.Z;
            int mid = alongX ? (md.Min.Z + md.Max.Z) / 2 : (md.Min.X + md.Max.X) / 2;
            for (int t = lo + 1; t <= (lo + hi) / 2; t++)
            for (int off = 0; off <= 2; off++)
            foreach (int side in new[] { mid + off, mid - off })
            for (int y = md.Min.Y; y <= md.Max.Y - 1; y++)
            {
                var c = alongX ? new Int3(t, y, side) : new Int3(side, y, t);
                if (!nav.Standable(c)) continue;
                // The view down the room must be open at eye height for most of its length.
                int open = 0;
                for (int u = 1; u <= hi - t; u++)
                {
                    var e = alongX ? new Int3(t + u, c.Y + 1, side) : new Int3(side, c.Y + 1, t + u);
                    if (!poi.Blocks.Get(e).IsAir || !poi.Blocks.Get(e + Int3.Up).IsAir) break;
                    open = u;
                }
                if (open < System.Math.Min(10, (hi - lo) * 2 / 3)) continue;
                var feet = stage.ToGlobal(new Vector3(c.X + 0.5f, c.Y + 0.05f, c.Z + 0.5f));
                var far = alongX ? new Vector3(hi + 0.5f, c.Y + 1.2f, mid + 0.5f) : new Vector3(mid + 0.5f, c.Y + 1.2f, hi + 0.5f);
                var look = stage.ToGlobal(far) - (feet + Vector3.Up * 1.6f);
                float yaw = Mathf.Atan2(-look.X, -look.Z);
                _main.Player.Place(feet, yaw);
                _main.Player.SetLook(yaw, Mathf.Clamp(Mathf.Asin(look.Normalized().Y), -0.25f, 0.15f));
                _main.Player.Lamp.Visible = true;
                _main.Player.ProcessMode = ProcessModeEnum.Disabled;
                _main.SettleInterior();
                return;
            }
        }
        WalkOnStorey(storey == "any" ? "0" : storey, 0.5f);
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
        int k0 = Mathf.Clamp((int)(on.Count * along), 0, on.Count - 1);
        // An interior shot stands indoors: on from the chosen point to the first cell under a roof.
        bool Roofed(Int3 c)
        {
            for (int y = 2; y <= 14; y++) if (!poi.Blocks.Get(c + new Int3(0, y, 0)).IsAir) return true;
            return false;
        }
        for (int k = k0; k < on.Count; k++) if (Roofed(path[on[k]])) { k0 = k; break; }
        int i0 = on[k0];
        Int3 at = path[i0];
        Int3 ahead = path[System.Math.Min(path.Count - 1, i0 + 6)];
        if (ahead.Y != at.Y) ahead = path[System.Math.Max(0, i0 - 1)] == at ? at : at;
        var feet = stage.ToGlobal(new Vector3(at.X + 0.5f, at.Y + 0.05f, at.Z + 0.5f));
        var look = stage.ToGlobal(new Vector3(ahead.X + 0.5f, at.Y + 1.4f, ahead.Z + 0.5f)) - (feet + Vector3.Up * 1.6f);
        if (look.LengthSquared() < 0.01f) look = new Vector3(0, 0, -1);
        float yaw = Mathf.Atan2(-look.X, -look.Z);
        // Look down the longest open line near the way on, not into the nearest wall: march at eye
        // height through the blocks in sixteen directions and keep the deepest, favouring the way on.
        float bestScore = float.MinValue, bestYaw = yaw;
        for (int d = 0; d < 16; d++)
        {
            float a = yaw + (d - 8) * Mathf.Pi / 8f;
            float open = 0f;
            var dir = new Vector3(-Mathf.Sin(a), 0, -Mathf.Cos(a));
            for (float t = 0.5f; t <= 30f; t += 0.5f)
            {
                var c = new Int3(Mathf.FloorToInt(at.X + 0.5f + dir.X * t), at.Y + 1, Mathf.FloorToInt(at.Z + 0.5f + dir.Z * t));
                if (!poi.Blocks.Get(c).IsAir) break;
                open = t;
            }
            float turn = Mathf.Abs(Mathf.Wrap(a - yaw, -Mathf.Pi, Mathf.Pi));
            float score = Mathf.Min(open, 24f) - turn * 2.5f;
            if (score > bestScore) { bestScore = score; bestYaw = a; }
        }
        yaw = bestYaw;
        look = new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));
        _main.Player.Place(feet, yaw);
        _main.Player.SetLook(yaw, Mathf.Clamp(Mathf.Asin(look.Normalized().Y), -0.3f, 0.2f));
        _main.Player.Lamp.Visible = true;
        _main.Player.ProcessMode = ProcessModeEnum.Disabled;
        _main.SettleInterior();
    }

    async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        // The places in the shot get PoiTest's Geometry check on the meshes actually drawn: a stroke
        // thrown out of its cell (a "wire" across the frame) refuses the shot rather than ship it.
        int places = StrayGeometry(this, out int triangles, out int stray, out string first);
        GD.Print($"geometry check: {triangles} triangles in {places} places, {stray} stray{first}");
        if (stray > 0)
        {
            GD.Print($"could not save {_out}: stray geometry in the places shown");
            GalHack.Testing.Backstop.Quit(this, 3);
            return;
        }
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GalHack.Testing.Backstop.Quit(this, result == Error.Ok ? 0 : 1);
    }

    /// <summary>
    /// Every mesh drawn for every place in the scene, in the place's own frame: each vertex finite
    /// and inside its block grid (a few metres of slack, the rock plug's tiles, and the rock skirt
    /// below), and no triangle edge longer than a chunk's diagonal. What PoiTest's Geometry check
    /// does for the generator's meshes, here for the exact places this shot shows.
    /// </summary>
    static int StrayGeometry(Node root, out int triangles, out int stray, out string first)
    {
        triangles = 0;
        stray = 0;
        first = "";
        int places = 0;
        const float slack = PoiView.Tile + 4f, maxEdge = 16f * 1.5f;
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            foreach (Node c in n.GetChildren()) stack.Push(c);
            if (n is not PoiView view || view.Poi == null) continue;
            places++;
            var g = view.Poi.Blocks;
            Vector3 lo = new(g.Min.X - slack, Mathf.Min(g.Min.Y, -3) - 64f, g.Min.Z - slack);
            Vector3 hi = new(g.Min.X + g.Size.X + slack, g.Min.Y + g.Size.Y + slack, g.Min.Z + g.Size.Z + slack);
            Transform3D toPlace = view.GlobalTransform.AffineInverse();
            var meshes = new Stack<Node>();
            meshes.Push(view);
            while (meshes.Count > 0)
            {
                Node m = meshes.Pop();
                foreach (Node c in m.GetChildren()) if (c is not PoiView) meshes.Push(c);
                if (m is not MeshInstance3D mi || mi.Mesh == null) continue;
                Transform3D t = toPlace * mi.GlobalTransform;
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                {
                    var arrays = mi.Mesh.SurfaceGetArrays(s);
                    Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    Variant raw = arrays[(int)Mesh.ArrayType.Index];
                    int[] idx = raw.VariantType == Variant.Type.Nil ? null : raw.AsInt32Array();
                    bool indexed = idx is { Length: > 0 };
                    int count = indexed ? idx.Length : v.Length;
                    for (int k = 0; k + 2 < count; k += 3)
                    {
                        triangles++;
                        bool bad = false;
                        for (int e = 0; e < 3 && !bad; e++)
                        {
                            Vector3 a = t * v[indexed ? idx[k + e] : k + e];
                            Vector3 b = t * v[indexed ? idx[k + (e + 1) % 3] : k + (e + 1) % 3];
                            bad |= !float.IsFinite(a.X) || !float.IsFinite(a.Y) || !float.IsFinite(a.Z)
                                   || a.X < lo.X || a.Y < lo.Y || a.Z < lo.Z || a.X > hi.X || a.Y > hi.Y || a.Z > hi.Z
                                   || a.DistanceSquaredTo(b) > maxEdge * maxEdge;
                            if (bad && first.Length == 0) first = $" (first: {view.Poi.Family.Id} {mi.Name} near {a.X:F1}, {a.Y:F1}, {a.Z:F1})";
                        }
                        if (bad) stray++;
                    }
                }
            }
        }
        return places;
    }
}
