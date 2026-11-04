using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// Renders one staged view of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/wildlife res://tests/Screenshot.tscn -- --view=herd --out=shot.png
/// Views: herd (an alien herd on the plains), predator (a weird predator mid-stalk), bloom (a
/// corrupted creature winding up to lunge), harvester (the hovercraft chewing through a grove),
/// temperate (a temperate scene for contrast). --hud keeps the debug HUD.
/// </summary>
public partial class Screenshot : Node
{
    const int SetupTick = 2;

    Main _main;
    int _tick, _captureTick = 240;
    string _view = "herd", _out = "shot.png", _worlds = "temperate,alien,bloom";
    float _time = -1f;
    string _pick = "";
    bool _hud, _captured;
    Func<(Vector3 eye, Vector3 at)> _aim;
    Action _each;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
            else if (arg == "--hud") _hud = true;
            else if (arg.StartsWith("--worlds=")) _worlds = arg["--worlds=".Length..];
            else if (arg.StartsWith("--pick=")) _pick = arg["--pick=".Length..];
            else if (arg.StartsWith("--time=")) _time = float.Parse(arg["--time=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        }
        ProcessPhysicsPriority = 100; // after Main has stepped the world
        _main = new Main();
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_tick == SetupTick) Setup();
        if (_tick > SetupTick)
        {
            _each?.Invoke();
            if (_aim != null)
            {
                var (eye, at) = _aim();
                _main.Camera.GlobalPosition = eye;
                _main.Camera.LookAt(at, Vector3.Up);
            }
        }
        if (_tick == _captureTick && !_captured) Capture();
    }

    void Setup()
    {
        Main m = _main;
        m.FreeCamera = true;
        m.Hud.StatusVisible = _hud;
        m.Player.Muzzle.Visible = false;
        Player p = m.Player;
        switch (_view)
        {
            case "herd":
            case "temperate":
            {
                World(_view == "herd" ? WorldKind.Alien : WorldKind.Temperate, _view == "herd" ? 0.33f : 0.42f);
                p.Teleport(Ground(new Vector3(150f, 0f, 150f))); // out of the way: a calm herd
                Species herd = m.Fauna.Species.First(s => s.Behaviour == Behaviour.Herd);
                Creature[] members = m.Sim.Creatures.Where(c => c.Species == herd && c.Herd != null).OrderBy(c => c.Position.Length()).Take(8).ToArray();
                Herd h = members[0].Herd;
                Vector3 side = new Vector3(0.75f, 0f, 1f).Normalized();
                float back = 3.5f + h.Spread * 0.6f + herd.Body.Size * 1.3f;
                _aim = () => (Ground(h.Center + side * back) + Vector3.Up * (0.8f + herd.Body.Size * 0.25f), h.Center + Vector3.Up * herd.Body.Size * 0.5f);
                _captureTick = 300;
                break;
            }
            case "predator":
            {
                World(WorldKind.Alien, 0.62f); // late afternoon, the sun low behind us
                // The weirdest walking predator if there is one: the stalking crouch reads best on legs.
                Species predator = m.Fauna.Species.Where(s => s.Behaviour == Behaviour.Predator)
                    .OrderByDescending(s => (s.Rig == RigKind.Legged ? 1f : 0f) + s.Weirdness).First();
                Vector3 toward = Turn(SunFlat(), 0.5f);
                Vector3 eye = Ground(new Vector3(-60f, 0f, 60f)) + Vector3.Up * (0.45f + predator.Body.Size * 0.3f);
                p.Teleport(Ground(eye));
                float dist = 3.5f + predator.Body.Size * 2.8f;
                Creature c = m.Sim.Spawn(predator, eye + toward * dist);
                c.Yaw = MathF.Atan2(toward.X, toward.Z); // facing back at us
                // Hold it in the stalk (this close it would otherwise break into a charge).
                _each = () => c.SetState(CState.Stalk);
                _aim = () => (eye, c.Rig.BoundsCenter + Vector3.Up * 0.15f);
                _captureTick = 90;
                break;
            }
            case "bloom":
            {
                World(WorldKind.Bloom, 0.7f);
                Species s = m.Fauna.Species.Where(x => x.Rig is RigKind.Legged or RigKind.Chain && x.Plan != BodyPlan.Giant)
                    .OrderByDescending(x => x.Weirdness).First();
                Vector3 toward = Turn(SunFlat(), -0.6f);
                Vector3 eye = Ground(new Vector3(70f, 0f, 70f)) + Vector3.Up * (0.7f + s.Body.Size * 0.3f);
                p.Teleport(Ground(eye));
                Creature c = m.Sim.Spawn(s, eye + toward * (3.2f + s.Body.Size * 1.3f));
                c.Yaw = MathF.Atan2(toward.X, toward.Z);
                int windupAt = _tick + 40;
                _each = () => { if (_tick == windupAt) Attacks.Start(m.Sim, c, p.Target); };
                _aim = () => (eye, c.Rig.BoundsCenter + Vector3.Up * 0.1f);
                _captureTick = windupAt + (int)(s.TelegraphWindup(m.Data.Combat) * 0.6f * 60f);
                break;
            }
            case "lineup":
                Lineup();
                break;
            case "sheet":
                Sheet();
                break;
            case "harvester":
            {
                World(WorldKind.Alien, 0.42f);
                // Come at the grove from outside, so the camera behind the vessel sees it bite in.
                Vector3 grove = m.Forest.Groves[0];
                Vector3 outward = -SunFlat();
                Plant edge = m.Forest.Field.Plants.Where(x => x.Kind == PlantKind.Tree && WildSim.Flat(x.Position - grove).Length() < 25f)
                    .OrderByDescending(x => (x.Position - grove).Dot(outward)).First();
                Vessel v = m.Vessel;
                v.Yaw = MathF.Atan2(outward.X, outward.Z); // forward is -outward: into the trees
                v.GlobalPosition = Ground(edge.Position + outward * 6.5f) + Vector3.Up * 0.85f;
                v.Driver = p.Target;
                v.AutoThrottle = 0.4f;
                p.Teleport(Ground(new Vector3(160f, 0f, -160f)));
                int cutting = 0;
                _each = () => { if (v.CuttingFor > 0f) cutting++; if (cutting == 16) _captureTick = _tick + 1; };
                _aim = () =>
                {
                    Vector3 f = v.Forward, r = new(-f.Z, 0f, f.X);
                    return (v.GlobalPosition + f * 1.5f + r * 8.5f + Vector3.Up * 3.2f, v.GlobalPosition + f * 3.2f + Vector3.Up * 1.4f);
                };
                _captureTick = 900;
                break;
            }
        }
    }

    /// <summary>
    /// The bestiary: every species of two or three planets on clear ground, one column per
    /// planet, smallest at the front and biggest at the back, with a 1.8 m figure at the front
    /// of each column for scale. Creatures are frozen in place (idling, not wandering) and
    /// turned three-quarters to the camera. --worlds=temperate,alien,bloom picks the columns
    /// (left to right); "seed123:0.6" or "seed123:0.6:bloom" shows any planet.
    /// </summary>
    void Lineup()
    {
        Main m = _main;
        World(WorldKind.Temperate, _time >= 0f ? _time : 0.36f);
        m.Forest.Visible = false;
        m.Grass.Visible = false;
        m.Vessel.Visible = false;
        m.Planet.ShowRocks(false);
        m.Player.Teleport(Ground(new Vector3(200f, 0f, 200f)));
        m.Sim.Clear();
        m.View.Bind(m.Sim, null);
        m.Sim.Frozen = true;
        m.Sim.Targets.Clear();

        // Lay out in a frame that looks along the sunlight, so the sun is behind the camera.
        Vector3 fwd = Turn(SunFlat(), 0.35f), right = fwd.Cross(Vector3.Up).Normalized();
        Vector3 origin = new(0f, 0f, 0f);
        Vector3 At(float x, float depth) => origin + right * x + fwd * depth;
        // Head to the left and turned towards the camera: three-quarter view.
        Vector3 face = (-right * 0.75f - fwd * 0.65f).Normalized();
        float faceYaw = MathF.Atan2(-face.X, -face.Z);

        string[] columns = _worlds.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var planets = columns.Select(LineupPlanet).ToList();
        float colWidth = planets.Max(p => p.Item1.Species.OrderBy(Footprint).SkipLast(1).Select(Footprint).DefaultIfEmpty(2f).Max() * 2f + 3f);
        float maxDepth = 0f;
        for (int col = 0; col < planets.Count; col++)
        {
            (PlanetFauna fauna, string label) = planets[col];
            float cx = (col - (planets.Count - 1) * 0.5f) * colWidth;
            Figure(At(cx, -1.5f));
            var tag = new Label3D
            {
                Text = $"{label}\n{fauna.PlanetName}", FontSize = 96, PixelSize = 0.005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = new Color(1f, 0.95f, 0.85f), OutlineSize = 24, OutlineModulate = new Color(0f, 0f, 0f, 0.8f),
            };
            m.AddChild(tag);
            tag.GlobalPosition = Ground(At(cx, -1.5f)) + Vector3.Up * 2.5f;

            // Pairs side by side, each pair a step further back than the one before.
            var species = fauna.Species.OrderBy(Footprint).ToList();
            float depth = 0.5f;
            for (int i = 0; i < species.Count; i += 2)
            {
                float rowDepth = 0f;
                for (int k = i; k < Math.Min(i + 2, species.Count); k++)
                {
                    Species s = species[k];
                    float f = Footprint(s);
                    bool alone = i + 1 >= species.Count;
                    float x = cx + (alone ? 0f : (k == i ? -1f : 1f) * colWidth * 0.25f);
                    Creature c = m.Sim.Spawn(s, At(x, depth + f * 0.5f));
                    c.Yaw = faceYaw;
                    if (s.Rig == RigKind.Swarm) c.Altitude = s.Flies ? 1.2f : 0f;
                    else if (s.Flies) c.Altitude = s.Rig == RigKind.GasBag ? s.Body.Height * 0.9f + 1.2f : 1.4f + s.Body.Size * 0.3f;
                    c.Position = new Vector3(c.Position.X, m.Planet.Terrain.Height(c.Position.X, c.Position.Z) + (s.Flies ? c.Altitude : 0f), c.Position.Z);
                    c.Rig.Reset(m.Sim, c);
                    rowDepth = MathF.Max(rowDepth, f);
                }
                depth += rowDepth + 1f;
            }
            maxDepth = MathF.Max(maxDepth, depth);
        }

        // Frame the whole layout from in front and above.
        m.Camera.Fov = 45f;
        float halfWidth = planets.Count * colWidth * 0.5f + 1f;
        float hfov = 2f * MathF.Atan(MathF.Tan(Mathf.DegToRad(45f) * 0.5f) * 16f / 9f);
        float dist = MathF.Max(halfWidth / MathF.Tan(hfov * 0.5f), maxDepth * 0.9f) + 2f;
        Vector3 look = At(0f, maxDepth * 0.38f) + Vector3.Up * 0.6f;
        Vector3 eye = At(0f, maxDepth * 0.38f - dist * 0.9f) + Vector3.Up * (dist * 0.42f);
        _aim = () => (eye, look);
        _captureTick = 150;
    }

    /// <summary>
    /// A contact sheet for checking the generator by eye: every species of the chosen planets,
    /// each in its own small view, framed three-quarters from the front at a size where faces,
    /// horns and feet can be judged. Same world, one camera per creature.
    /// </summary>
    void Sheet()
    {
        Main m = _main;
        World(WorldKind.Temperate, _time >= 0f ? _time : 0.36f);
        m.Forest.Visible = false;
        m.Grass.Visible = false;
        m.Vessel.Visible = false;
        m.Planet.ShowRocks(false);
        m.Hud.Visible = false;
        m.Player.Teleport(Ground(new Vector3(200f, 0f, 200f)));
        m.Sim.Clear();
        m.View.Bind(m.Sim, null);
        m.Sim.Frozen = true;
        m.Sim.Targets.Clear();
        Vector3 sun = SunFlat();

        var species = _worlds.Split(',', StringSplitOptions.RemoveEmptyEntries).SelectMany(w => LineupPlanet(w).Item1.Species).ToList();
        if (_pick != "") species = _pick.Split(',').Select(int.Parse).Select(i => species[i]).ToList();
        int cols = species.Count <= 1 ? 1 : species.Count <= 4 ? 2 : species.Count <= 9 ? 3 : 4, rows = (species.Count + cols - 1) / cols;
        var grid = new GridContainer { Columns = cols };
        grid.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddThemeConstantOverride("v_separation", 2);
        var layer = new CanvasLayer { Layer = 5 };
        layer.AddChild(grid);
        AddChild(layer);
        Vector2I cell = new(1600 / cols - 2, 900 / rows - 2);
        var cams = new List<(Camera3D cam, Creature c)>();
        for (int i = 0; i < species.Count; i++)
        {
            Species s = species[i];
            // Far apart, so no creature wanders into another's frame.
            Vector3 at = new(-120f + (i % 8) * 34f, 0f, -60f + (i / 8) * 40f);
            Creature c = m.Sim.Spawn(s, at);
            Vector3 face = Turn(-sun, 0.75f);
            c.Yaw = MathF.Atan2(-face.X, -face.Z);
            if (s.Rig == RigKind.Swarm) c.Altitude = s.Flies ? 1.2f : 0f;
            else if (s.Flies) c.Altitude = s.Rig == RigKind.GasBag ? s.Body.Height * 0.9f + 1.2f : 1.4f + s.Body.Size * 0.3f;
            c.Position = new Vector3(c.Position.X, m.Planet.Terrain.Height(c.Position.X, c.Position.Z) + (s.Flies ? c.Altitude : 0f), c.Position.Z);
            c.Rig.Reset(m.Sim, c);

            var box = new SubViewportContainer { Stretch = true, CustomMinimumSize = cell };
            var vp = new SubViewport { Size = cell, Msaa3D = Viewport.Msaa.Msaa4X };
            var cam = new Camera3D { Fov = 40f, Near = 0.05f, Far = 800f };
            vp.AddChild(cam);
            box.AddChild(vp);
            var label = new Label { Text = $"{s.Name}: {s.CommonName}, {s.Behaviour}{(s.Stats.Damage > 0f ? $", {s.Attack.Name}" : "")}  {s.Body.Size:F1} m", Position = new Vector2(6, 4) };
            label.AddThemeFontSizeOverride("font_size", 13);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 4);
            box.AddChild(label);
            grid.AddChild(box);
            cams.Add((cam, c));
        }
        _each = () =>
        {
            foreach (var (cam, c) in cams)
            {
                Species sp = c.Species;
                float r = sp.Rig switch
                {
                    RigKind.Winged => sp.Body.Size * 1.1f,
                    RigKind.Swarm => 2f,
                    RigKind.GasBag => MathF.Max(sp.Body.Width, sp.Body.Height) * 0.9f + sp.Body.TailLength * 0.25f,
                    _ => MathF.Max(sp.Body.Size * 0.62f, (sp.Body.LegLength + sp.Body.Height) * 0.55f),
                };
                Vector3 look = sp.Rig == RigKind.GasBag ? c.Position + Vector3.Down * sp.Body.TailLength * 0.25f : c.Rig.BoundsCenter;
                Vector3 dir = (c.Forward * 0.8f + c.Forward.Cross(Vector3.Up) * 0.6f).Normalized();
                cam.GlobalPosition = look + dir * r * 2.6f + Vector3.Up * r * 0.9f;
                cam.LookAt(look, Vector3.Up);
            }
        };
        _captureTick = 120;
    }

    /// <summary>The planets a lineup row can show: the game's three worlds, or any seed.</summary>
    (PlanetFauna, string) LineupPlanet(string name)
    {
        FaunaGenerator gen = _main.Generator;
        WorldStyle Style(WorldKind k) => WorldStyle.For(k);
        return name switch
        {
            "temperate" => (gen.Generate(Style(WorldKind.Temperate).FaunaSeed, Style(WorldKind.Temperate).Alienness, false), "Temperate"),
            "alien" => (gen.Generate(Style(WorldKind.Alien).FaunaSeed, Style(WorldKind.Alien).Alienness, false), "Alien"),
            "bloom" => (gen.Generate(Style(WorldKind.Bloom).FaunaSeed, Style(WorldKind.Bloom).Alienness, true), "Bloom-infested"),
            _ when name.StartsWith("seed") => LineupSeed(name),
            _ => throw new ArgumentException($"unknown lineup row {name}"),
        };
    }

    /// <summary>"seed123:0.6" or "seed123:0.6:bloom": any planet, for checking the generator by eye.</summary>
    (PlanetFauna, string) LineupSeed(string spec)
    {
        string[] parts = spec[4..].Split(':');
        ulong seed = ulong.Parse(parts[0]);
        float alien = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0.5f;
        bool bloom = parts.Length > 2 && parts[2] == "bloom";
        return (_main.Generator.Generate(seed, alien, bloom), $"seed {seed}, alien {alien:F1}{(bloom ? ", Bloom" : "")}");
    }

    /// <summary>How much room a species needs in the row.</summary>
    static float Footprint(Species s) => s.Rig switch
    {
        RigKind.Winged => s.Body.Size * 2.2f,
        RigKind.GasBag => MathF.Max(s.Body.Width, s.Body.Size * 0.6f) * 1.3f,
        RigKind.Swarm => 3f,
        RigKind.Chain => s.Body.Size * 0.6f,
        _ => MathF.Max(s.Body.Size * 0.9f, s.Body.Width + s.Body.LegLength),
    };

    /// <summary>A plain 1.8 m figure for scale.</summary>
    void Figure(Vector3 at)
    {
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.64f, 0.66f), Roughness = 0.8f };
        var body = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.22f, Height = 1.45f }, MaterialOverride = mat };
        var head = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.14f, Height = 0.28f }, MaterialOverride = mat };
        _main.AddChild(body);
        _main.AddChild(head);
        Vector3 g = Ground(at);
        body.GlobalPosition = g + Vector3.Up * 0.725f;
        head.GlobalPosition = g + Vector3.Up * 1.64f;
    }

    void World(WorldKind kind, float time)
    {
        _main.SwitchWorld(kind);
        _main.Sim.Clock.Time01 = time;
        _main.Planet.UpdateSky(_main.Sim.Clock); // so the sun direction is current for staging
    }

    /// <summary>The way the sunlight travels, flattened: look along it and the sun is behind you.</summary>
    Vector3 SunFlat() => WildSim.Flat(_main.Planet.SunDirection).Normalized();

    static Vector3 Turn(Vector3 v, float radians) => new Basis(Vector3.Up, radians) * v;

    Vector3 Ground(Vector3 p) => new(p.X, _main.Planet.Terrain.Height(p.X, p.Z), p.Z);

    async void Capture()
    {
        _captured = true;
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
