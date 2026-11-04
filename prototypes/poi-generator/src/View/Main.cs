using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// The POI viewer, built in code from a bare Main.tscn.
///
///   1  Aerial: fly over a planet patch where the placement engine has scattered places.
///      Landmarks show on the map from orbit, the rest as you get close.
///   2  Inspect: one place on its own, orbiting camera. F family, V layout, N new layout
///      seed, S state, R rarity.
///   3  Walk: first person through the place. E reads terminals (and caches, the core,
///      story props), L the shoulder lamp.
///   Tab shows the layout from above in Inspect and Walk.
/// </summary>
public partial class Main : Node3D
{
    public enum Mode { Aerial, Inspect, Walk }

    public Mode CurrentMode { get; private set; } = Mode.Aerial;
    public PoiLibrary Library { get; private set; }
    public PoiView Stage { get; private set; }
    public Player Player { get; private set; }
    public Camera3D Camera { get; private set; }
    public SkyRig Sky { get; private set; }
    public Overlay Map { get; private set; }

    // What the inspector shows.
    int _family, _variant;
    Rarity _rarity = Rarity.Common;
    PoiState _state = PoiState.Active;
    ulong _spawnSeed = 1, _customSeed;
    bool _useCustom;
    Biome _biome = Biome.Rock;

    // The aerial patch.
    const ulong PatchSeed = 4242;
    Node3D _aerialRoot, _stageRoot;
    PlanetMap _map;
    List<PlacedPoi> _placed = new();
    readonly List<(PlacedPoi poi, PoiView view, Label3D label)> _aerialViews = new();
    readonly MapReveal _reveal = new();
    Vector3 _flyPos = new(-60, 420, 780);
    float _flyYaw, _flyPitch = -0.55f;

    // Inspector camera.
    float _orbitYaw = 0.7f, _orbitPitch = -0.38f, _orbitDist = 1.4f, _orbitSpin = 1f;
    Vector2 _mouse;

    Label _hud, _keys, _prompt, _readerText, _readerTitle;
    PanelContainer _reader;
    PoiView.Interactable _reading;

    public override void _Ready()
    {
        Library = GameData.Library;
        Sky = new SkyRig { Name = "Sky" };
        AddChild(Sky);
        Camera = new Camera3D { Name = "Camera", Fov = 72f, Near = 0.05f, Far = 9000f };
        AddChild(Camera);
        Camera.MakeCurrent();
        Sky.Follow = Camera;

        Player = new Player { Name = "Player", Camera = Camera };
        AddChild(Player);
        Camera.AddChild(Player.Lamp);
        Player.Lamp.Position = new Vector3(0.25f, -0.2f, 0f);

        _aerialRoot = new Node3D { Name = "Aerial" };
        _stageRoot = new Node3D { Name = "Stage" };
        AddChild(_aerialRoot);
        AddChild(_stageRoot);
        BuildHud();
        BuildAerial();
        _family = Library.Families.FindIndex(f => f.Id == "vharn_outpost");
        SetMode(Mode.Aerial);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // ---- modes ------------------------------------------------------------------------

    public void SetMode(Mode mode)
    {
        if (mode != Mode.Aerial && Stage == null) Rebuild();
        CurrentMode = mode;
        _aerialRoot.Visible = mode == Mode.Aerial;
        _stageRoot.Visible = mode != Mode.Aerial;
        Player.ProcessMode = mode == Mode.Walk ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        Player.Lamp.Visible = mode == Mode.Walk && _state != PoiState.Active;
        if (mode == Mode.Aerial)
        {
            Sky.Apply(Look.For(PoiState.Active) with { FogDensity = 0.00055f, SunEnergy = 1.35f }, 700f);
            Map.Visible = false;
        }
        else
        {
            Sky.Apply(Look.For(_state), 110f);
            if (mode == Mode.Walk)
            {
                var (pos, yaw) = Stage.EntranceStand();
                Player.Place(pos, yaw);
                Stage.Watcher = Player;
            }
            else Stage.Watcher = Camera;
        }
        CloseReader();
    }

    /// <summary>Shows one place in the inspector stage. Screenshots and the aerial view use this.</summary>
    public void Inspect(string familyId, int variant, Rarity rarity, PoiState state, ulong spawnSeed, ulong? customSeed = null, Biome? biome = null)
    {
        _family = Library.Families.FindIndex(f => f.Id == familyId);
        _variant = variant;
        _rarity = rarity;
        _state = state;
        _spawnSeed = spawnSeed;
        _useCustom = customSeed != null;
        _customSeed = customSeed ?? 0;
        _biome = biome ?? Library.Families[_family].Biomes[0];
        Rebuild();
        SetMode(Mode.Inspect);
    }

    void Rebuild()
    {
        foreach (Node n in _stageRoot.GetChildren()) n.QueueFree();
        var family = Library.Families[_family];
        var poi = PoiFactory.Generate(Library, family, _variant, _rarity, _state, _spawnSeed, _useCustom ? _customSeed : null);
        var ground = Look.ToColor(Look.Ground(_biome));
        Stage = PoiView.Build(poi, _biome, new PoiView.Options(), (x, z) => ground);
        _stageRoot.AddChild(Stage);

        // Ground all round, with the place's own plug of rock in the middle.
        var hole = new Rect2I(Stage.PlugMin, Stage.PlugMax - Stage.PlugMin + Vector2I.One);
        int half = 26 * PoiView.Tile;
        _stageRoot.AddChild(Terrain.Build(new Vector2I(-half, -half), new Vector2I(52, 52), (x, z) => ground, new List<Rect2I> { hole }, true, 4000f));
        _stageRoot.AddChild(Terrain.Mesas(poi.SpawnSeed ^ 77, Vector2.Zero, 170f, 1400f, 70, Look.ToColor(Look.Rock(_biome)).Lightened(0.15f), new()));
        Map.SetPlace(poi);
        if (CurrentMode != Mode.Aerial) Sky.Apply(Look.For(_state), 110f);
        Player.Lamp.Visible = CurrentMode == Mode.Walk && _state != PoiState.Active;
        if (CurrentMode == Mode.Walk)
        {
            var (pos, yaw) = Stage.EntranceStand();
            Player.Place(pos, yaw);
            Stage.Watcher = Player;
        }
        else Stage.Watcher = Camera;
    }

    // ---- the aerial patch -------------------------------------------------------------

    void BuildAerial()
    {
        _map = PlanetMap.Generate(PatchSeed, PlanetSize.Medium, "Patch");
        _placed = Placement.PlacePlanet(Library, _map, new SystemContext(), new PlayerKnowledge(), new Rng(PatchSeed), withRare: true);
        float size = _map.Width * _map.CellSize;
        Vector2 offset = new(-size / 2f, -size / 2f);
        var holes = new List<Rect2I>();
        var clear = new List<(Vector2, float)>();

        foreach (var p in _placed)
        {
            var poi = PoiFactory.Spawn(Library, PoiFactory.BuildLayout(Library, p.Family, p.Variant.Index, p.Rarity), p.State, p.SpawnSeed);
            var biome = _map.BiomeAt(p.CellX, p.CellY);
            var b = PoiView.ModuleBoundsOf(poi);
            Vector2 at = new Vector2(p.X, p.Z) + offset;
            int ox = Snap(at.X - (b.Position.X + b.Size.X / 2)), oz = Snap(at.Y - (b.Position.Z + b.Size.Z / 2));
            var view = PoiView.Build(poi, biome, new PoiView.Options(Collision: false, Detail: false, Lights: false, Figures: false), ColorAt, new Vector2I(ox, oz));
            view.Position = new Vector3(ox, 0, oz);
            _aerialRoot.AddChild(view);
            holes.Add(new Rect2I(view.PlugMin + new Vector2I(ox, oz), view.PlugMax - view.PlugMin + Vector2I.One));
            clear.Add((new Vector2(ox + b.Position.X + b.Size.X / 2, oz + b.Position.Z + b.Size.Z / 2), Mathf.Max(b.Size.X, b.Size.Z) + 30f));

            var label = new Label3D
            {
                Text = $"{p.Family.Name}\n{p.Variant.Name}\n{p.State} · {p.Rarity}" + (p.Reveal.Landmark ? " · landmark" : ""),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FixedSize = true,
                PixelSize = 0.0007f, FontSize = 28, OutlineSize = 10, Modulate = new Color("ffe8c2"),
                OutlineModulate = new Color(0, 0, 0, 0.8f), Visible = false,
                Position = new Vector3(b.Position.X + b.Size.X / 2, b.End.Y + 14f, b.Position.Z + b.Size.Z / 2),
            };
            view.AddChild(label);
            _aerialViews.Add((p, view, label));
        }

        Color ColorAt(float x, float z)
        {
            int cx = Mathf.Clamp((int)((x - offset.X) / _map.CellSize), 0, _map.Width - 1);
            int cz = Mathf.Clamp((int)((z - offset.Y) / _map.CellSize), 0, _map.Height - 1);
            return Look.ToColor(Look.Ground(_map.BiomeAt(cx, cz)));
        }
        int tiles = (int)(size / PoiView.Tile);
        var min = new Vector2I(Snap(offset.X), Snap(offset.Y));
        _aerialRoot.AddChild(Terrain.Build(min, new Vector2I(tiles, tiles), ColorAt, holes, false, 6000f));
        _aerialRoot.AddChild(Terrain.Mesas(PatchSeed, Vector2.Zero, 60f, size * 0.75f, 70, new Color("8a5238"), clear, 0.5f));
        _aerialRoot.AddChild(Deposits(offset, clear));
    }

    static int Snap(float v) => Mathf.FloorToInt(v / PoiView.Tile) * PoiView.Tile;

    /// <summary>Glowing ore spires on rich deposits, all in one MultiMesh.</summary>
    Node3D Deposits(Vector2 offset, List<(Vector2 at, float r)> clear)
    {
        var rng = new RandomNumberGenerator { Seed = PatchSeed + 9 };
        var transforms = new List<Transform3D>();
        for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++)
            {
                if (!_map.Deposit[_map.Index(x, y)] || rng.Randf() > 0.25f) continue;
                for (int k = 0; k < 2; k++)
                {
                    var at = new Vector2((x + rng.Randf()) * _map.CellSize, (y + rng.Randf()) * _map.CellSize) + offset;
                    if (clear.Any(c => at.DistanceTo(c.at) < c.r * 0.6f)) continue;
                    float h = rng.RandfRange(3f, 11f);
                    var axis = new Vector3(rng.RandfRange(-0.3f, 0.3f), 1f, rng.RandfRange(-0.3f, 0.3f)).Normalized();
                    var basis = new Basis(axis, rng.RandfRange(0.05f, 0.35f)).Scaled(new Vector3(rng.RandfRange(1.6f, 3f), h, rng.RandfRange(1.6f, 3f)));
                    transforms.Add(new Transform3D(basis, new Vector3(at.X, h * 0.45f, at.Y)));
                }
            }
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new BoxMesh(), InstanceCount = transforms.Count };
        for (int i = 0; i < transforms.Count; i++) mm.SetInstanceTransform(i, transforms[i]);
        return new MultiMeshInstance3D
        {
            Name = "Deposits", Multimesh = mm,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("3a8a7a"), EmissionEnabled = true, Emission = new Color("5ff0c8"), EmissionEnergyMultiplier = 0.7f, Roughness = 0.3f,
            },
        };
    }

    /// <summary>The place nearest where the aerial camera is looking.</summary>
    (PlacedPoi poi, PoiView view)? Aimed()
    {
        Vector3 fwd = -Camera.GlobalBasis.Z;
        Vector3 ground = fwd.Y < -0.05f ? Camera.GlobalPosition + fwd * (Camera.GlobalPosition.Y / -fwd.Y) : Camera.GlobalPosition;
        var best = _aerialViews.OrderBy(v => (v.view.GlobalPosition + v.view.ModuleBounds().GetCenter()).DistanceTo(ground)).FirstOrDefault();
        return best.view == null ? null : (best.poi, best.view);
    }

    // ---- input ------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m && Input.MouseMode == Input.MouseModeEnum.Captured) _mouse += m.Relative;
        if (CurrentMode == Mode.Walk) Player.HandleInput(e);
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Captured;
            if (mb.ButtonIndex == MouseButton.WheelUp) _orbitDist = Mathf.Max(0.35f, _orbitDist * 0.9f);
            if (mb.ButtonIndex == MouseButton.WheelDown) _orbitDist = Mathf.Min(4f, _orbitDist * 1.1f);
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.PhysicalKeycode)
        {
            case Key.Escape: Input.MouseMode = Input.MouseModeEnum.Visible; return;
            case Key.Key1: SetMode(Mode.Aerial); return;
            case Key.Key2: SetMode(Mode.Inspect); return;
            case Key.Key3: SetMode(Mode.Walk); return;
            case Key.Tab when CurrentMode != Mode.Aerial: Map.Visible = !Map.Visible; return;
        }

        if (CurrentMode == Mode.Aerial && key.PhysicalKeycode is Key.Enter or Key.KpEnter && Aimed() is { } aim)
        {
            var biome = _map.BiomeAt(aim.poi.CellX, aim.poi.CellY);
            Inspect(aim.poi.Family.Id, aim.poi.Variant.Index, aim.poi.Rarity, aim.poi.State, aim.poi.SpawnSeed, null, biome);
        }
        else if (CurrentMode == Mode.Inspect)
        {
            var families = Library.Families;
            switch (key.PhysicalKeycode)
            {
                case Key.F:
                    _family = (_family + 1) % families.Count;
                    _variant = 0;
                    _useCustom = false;
                    _biome = families[_family].Biomes[0];
                    Rebuild();
                    break;
                case Key.V: _variant = (_variant + 1) % families[_family].Variants.Count; _useCustom = false; Rebuild(); break;
                case Key.N: _useCustom = true; _customSeed = (ulong)GD.Randi() | ((ulong)GD.Randi() << 32); _spawnSeed++; Rebuild(); break;
                case Key.S: _state = (PoiState)(((int)_state + 1) % 4); Rebuild(); break;
                case Key.R: _rarity = (Rarity)(((int)_rarity + 1) % 3); Rebuild(); break;
            }
        }
        else if (CurrentMode == Mode.Walk)
        {
            if (key.PhysicalKeycode == Key.E)
            {
                if (_reader.Visible) CloseReader();
                else if (Stage.Nearest(Player.EyePosition, Player.LookDirection) is { } it) OpenReader(it);
            }
            else if (key.PhysicalKeycode == Key.L) Player.Lamp.Visible = !Player.Lamp.Visible;
        }
    }

    // ---- per frame --------------------------------------------------------------------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Vector2 mouse = _mouse;
        _mouse = Vector2.Zero;

        switch (CurrentMode)
        {
            case Mode.Aerial: Fly(dt, mouse); UpdateReveal(); break;
            case Mode.Inspect: Orbit(dt, mouse); break;
            case Mode.Walk:
                if (_reader.Visible && _reading != null && Stage.ToLocal(Player.EyePosition).DistanceTo(_reading.Position) > 3.5f) CloseReader();
                break;
        }
        UpdateHud();
        _hud.Visible = _keys.Visible = !Map.Visible && !_hudHidden;
        if (Map.Visible && CurrentMode == Mode.Walk)
        {
            Map.PlayerCell = Stage.ToLocal(Player.GlobalPosition);
            Map.PlayerYaw = Camera.GlobalRotation.Y;
        }
        else Map.PlayerCell = null;
    }

    void Fly(float dt, Vector2 mouse)
    {
        _flyYaw -= mouse.X * 0.0025f;
        _flyPitch = Mathf.Clamp(_flyPitch - mouse.Y * 0.0025f, -1.5f, 1.4f);
        var basis = new Basis(Vector3.Up, _flyYaw) * new Basis(Vector3.Right, _flyPitch);
        var move = new Vector3(Axis(Key.D, Key.A), Axis(Key.Space, Key.Ctrl), Axis(Key.S, Key.W));
        float speed = Input.IsPhysicalKeyPressed(Key.Shift) ? 260f : 70f;
        _flyPos += (basis * new Vector3(move.X, 0, move.Z) + Vector3.Up * move.Y) * speed * dt;
        _flyPos.Y = Mathf.Clamp(_flyPos.Y, 3f, 2400f);
        Camera.GlobalTransform = new Transform3D(basis, _flyPos);
    }

    /// <summary>Puts the aerial camera somewhere and points it. For screenshots.</summary>
    public void SetAerialCamera(Vector3 at, Vector3 lookAt)
    {
        _flyPos = at;
        Vector3 d = (lookAt - at).Normalized();
        _flyYaw = Mathf.Atan2(-d.X, -d.Z);
        _flyPitch = Mathf.Asin(d.Y);
        Fly(0f, Vector2.Zero);
    }

    void UpdateReveal()
    {
        float size = _map.Width * _map.CellSize;
        _reveal.Update(_placed, _flyPos.X + size / 2f, _flyPos.Z + size / 2f, _flyPos.Y);
        foreach (var (p, _, label) in _aerialViews) label.Visible = _reveal.IsRevealed(p);
    }

    /// <summary>The two places on the patch closest together: a good pair to frame from the air.</summary>
    public (Vector3 a, Vector3 b) ClosestPair()
    {
        Vector3 C(PoiView v) => v.GlobalPosition + v.ModuleBounds().GetCenter() with { Y = 0 };
        var best = (Vector3.Zero, Vector3.Zero);
        float bestD = float.MaxValue;
        for (int i = 0; i < _aerialViews.Count; i++)
            for (int j = i + 1; j < _aerialViews.Count; j++)
            {
                float d = C(_aerialViews[i].view).DistanceTo(C(_aerialViews[j].view));
                if (d < bestD) { bestD = d; best = (C(_aerialViews[i].view), C(_aerialViews[j].view)); }
            }
        return best;
    }

    /// <summary>The middle of all the places on the patch, at ground level.</summary>
    public Vector3 PatchCentroid()
    {
        var sum = Vector3.Zero;
        foreach (var (_, view, _) in _aerialViews) sum += view.GlobalPosition + view.ModuleBounds().GetCenter() with { Y = 0 };
        return _aerialViews.Count > 0 ? sum / _aerialViews.Count : Vector3.Zero;
    }

    /// <summary>Reveals every place on the aerial map, as if the player had been everywhere. For screenshots.</summary>
    public void RevealAll()
    {
        foreach (var p in _placed) _reveal.Revealed[p] = 0f;
    }

    void Orbit(float dt, Vector2 mouse)
    {
        _orbitYaw -= mouse.X * 0.004f;
        _orbitPitch = Mathf.Clamp(_orbitPitch - mouse.Y * 0.004f, -1.35f, 0.25f);
        if (mouse == Vector2.Zero) _orbitYaw += dt * 0.05f * _orbitSpin;
        _orbitYaw += Axis(Key.Left, Key.Right) * dt * 1.2f;
        _orbitPitch = Mathf.Clamp(_orbitPitch + Axis(Key.Up, Key.Down) * dt * 0.8f, -1.35f, 0.25f);
        PlaceOrbitCamera();
    }

    void PlaceOrbitCamera()
    {
        var b = Stage.SurfaceBounds();
        // Aim a third of the way up, so a headframe or spire stays in the picture.
        Vector3 target = Stage.ToGlobal(b.GetCenter() with { Y = b.Position.Y + Mathf.Clamp(b.Size.Y * 0.33f, 2.5f, 12f) });
        float radius = Mathf.Max(b.Size.X, b.Size.Z) * _orbitDist + 10f;
        var basis = new Basis(Vector3.Up, _orbitYaw) * new Basis(Vector3.Right, _orbitPitch);
        Vector3 at = target + basis * new Vector3(0, 0, radius);
        at.Y = Mathf.Max(at.Y, 1.2f);
        Camera.GlobalTransform = new Transform3D(Basis.Identity, at).LookingAt(target, Vector3.Up);
    }

    /// <summary>Sets the inspector camera: yaw and pitch in degrees, distance as a multiple of the place's size.</summary>
    public void SetOrbit(float yawDeg, float pitchDeg, float distance, bool spin = false)
    {
        _orbitYaw = Mathf.DegToRad(yawDeg);
        _orbitPitch = Mathf.DegToRad(pitchDeg);
        _orbitDist = distance;
        _orbitSpin = spin ? 1f : 0f;
        PlaceOrbitCamera();
    }

    /// <summary>The orbit yaw (degrees) that looks at the place from in front of its way in.</summary>
    public float EntranceYaw()
    {
        Vector3 o = Look.ToVector(Stage.Poi.Layout.EntranceFacing);
        return Mathf.RadToDeg(Mathf.Atan2(o.X, o.Z));
    }

    static float Axis(Key pos, Key neg) => (Input.IsPhysicalKeyPressed(pos) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(neg) ? 1f : 0f);

    // ---- HUD --------------------------------------------------------------------------

    void BuildHud()
    {
        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        _hud = MakeLabel(17, new Color("ffe8c2"));
        _hud.Position = new Vector2(18, 14);
        layer.AddChild(_hud);
        _keys = MakeLabel(15, new Color("e0d0b8"));
        _keys.AnchorTop = _keys.AnchorBottom = 1f;
        _keys.OffsetLeft = 18;
        _keys.OffsetTop = -64;
        layer.AddChild(_keys);
        _prompt = MakeLabel(18, new Color("7cffb0"));
        _prompt.AnchorLeft = _prompt.AnchorRight = 0.5f;
        _prompt.AnchorTop = _prompt.AnchorBottom = 0.62f;
        _prompt.OffsetLeft = -200;
        _prompt.OffsetRight = 200;
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        layer.AddChild(_prompt);

        var dot = new ColorRect
        {
            Color = new Color(1, 1, 1, 0.75f), AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -2, OffsetRight = 2, OffsetTop = -2, OffsetBottom = 2, Name = "Crosshair",
        };
        layer.AddChild(dot);

        // The terminal reader: green phosphor on near-black.
        _reader = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -380, OffsetRight = 380, OffsetTop = -170, OffsetBottom = 170, Visible = false,
        };
        _reader.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.05f, 0.03f, 0.93f), BorderColor = new Color("2f7a4a"),
            BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 16, ContentMarginBottom = 16,
        });
        var box = new VBoxContainer();
        _readerTitle = MakeLabel(15, new Color("4fd88a"));
        _readerText = MakeLabel(19, new Color("9cffc0"));
        _readerText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _readerText.CustomMinimumSize = new Vector2(700, 0);
        box.AddChild(_readerTitle);
        box.AddChild(_readerText);
        _reader.AddChild(box);
        layer.AddChild(_reader);

        Map = new Overlay { Visible = false, Name = "Overlay" };
        layer.AddChild(Map);
    }

    static Label MakeLabel(int size, Color color)
    {
        var l = new Label();
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        l.AddThemeConstantOverride("outline_size", 6);
        return l;
    }

    void OpenReader(PoiView.Interactable it)
    {
        _reading = it;
        _readerTitle.Text = it.Title;
        _readerText.Text = it.Text;
        _reader.Visible = true;
    }

    void CloseReader()
    {
        _reading = null;
        if (_reader != null) _reader.Visible = false;
    }

    /// <summary>Opens the nearest terminal's log in the reader. For screenshots.</summary>
    public void ReadNearestTerminal()
    {
        var eye = Stage.ToLocal(Player.EyePosition);
        var t = Stage.Interactables.Where(i => i.Title == "TERMINAL").OrderBy(i => i.Position.DistanceTo(eye)).FirstOrDefault();
        if (t != null) OpenReader(t);
    }

    /// <summary>Hides the HUD text, for clean screenshots.</summary>
    public void HideHud(bool hide) => _hudHidden = hide;
    bool _hudHidden;

    void UpdateHud()
    {
        string fps = $"{Engine.GetFramesPerSecond()} fps";
        switch (CurrentMode)
        {
            case Mode.Aerial:
            {
                int shown = _aerialViews.Count(v => _reveal.IsRevealed(v.poi));
                string height = _flyPos.Y >= MapReveal.OrbitAltitude ? "in orbit: landmarks only" : $"altitude {_flyPos.Y:F0} m";
                var aim = Aimed();
                _hud.Text = $"AERIAL  ·  planet patch, {_placed.Count} places placed by the engine\n" +
                            $"On the map: {shown} of {_placed.Count}  ({height}; others show as you get close)\n" +
                            (aim is { } a ? $"Nearest: {a.poi.Family.Name}, {a.poi.Variant.Name} ({a.poi.State}, {a.poi.Rarity})\n" : "") + fps;
                _keys.Text = "WASD fly  ·  Space / Ctrl up and down  ·  Shift fast  ·  mouse look\nEnter inspect the nearest place  ·  2 inspect  ·  3 walk  ·  Esc free the mouse";
                _prompt.Text = "";
                break;
            }
            default:
            {
                var poi = Stage.Poi;
                var l = poi.Layout;
                int defenders = poi.Enemies.Count(e => e.Defender);
                _hud.Text = $"{(CurrentMode == Mode.Inspect ? "INSPECT" : "WALK")}  ·  {l.Family.Name}  //  {l.Name}\n" +
                            $"{poi.State}  ·  {poi.Rarity}  ·  {l.Kit.Name}  ·  {l.Modules.Count} modules, {poi.Blocks.CountBlocks()} blocks\n" +
                            $"Loot {poi.LootValue} cr in {poi.Caches.Count} caches  ·  {poi.Enemies.Count} enemies ({defenders} on duty)  ·  " +
                            $"{poi.Terminals.Count} terminals  ·  {poi.Story.Count} story props\n" + fps;
                _keys.Text = CurrentMode == Mode.Inspect
                    ? "F family  ·  V layout  ·  N new layout seed  ·  S state  ·  R rarity  ·  mouse / arrows orbit  ·  wheel zoom\n1 aerial  ·  3 walk in  ·  Tab layout map  ·  Esc free the mouse"
                    : "WASD walk  ·  Shift sprint  ·  Space jump  ·  hold Ctrl crouch  ·  E read  ·  L lamp\n1 aerial  ·  2 inspect  ·  Tab layout map  ·  Esc free the mouse";
                _prompt.Text = CurrentMode == Mode.Walk && !_reader.Visible && Stage.Nearest(Player.EyePosition, Player.LookDirection) is { } it
                    ? $"E  read {it.Title.ToLowerInvariant()}" : "";
                break;
            }
        }
    }
}
