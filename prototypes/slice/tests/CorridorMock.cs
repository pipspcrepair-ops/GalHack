using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using PoiGenerator;
using PoiGenerator.Gen;
using Slice.Boot;
using Slice.Game;
using Wildlife;
using Int3 = PoiGenerator.Gen.Int3;
using Terrain = Wildlife.Terrain;
using NavGrid = Wildlife.NavGrid;
using Material = PoiGenerator.Gen.Material;
using Rng = PoiGenerator.Gen.Rng;

namespace Slice.Tests;

/// <summary>
/// A mockup, not a feature: one straight POI corridor with a T junction at the end, built from the
/// water works' real style kit (the concrete kit) in real POI blocks, lit by the kit's lamps and
/// the survivor's head torch, with a Hollow Prowler from the wildlife generator standing in it
/// 8 m off facing the camera. It is drawn at five corridor widths from the same spot, at the
/// game's first-person eye height (1.6 m) and field of view (75 degrees), so the sizes can be
/// compared before the generator's corridor rule (2 wide, 3 high) changes.
///
/// Run windowed (no --position), see CorridorMock.manual:
///   godot --path prototypes/slice res://tests/CorridorMock.tscn -- --out=DIR
/// Writes corridor-1x.png .. corridor-5x.png, corridors.png (the contact sheet), plan-1x.png ..
/// plan-5x.png and corridors-side.png (the creature's body in each, seen from above). --list prints
/// the candidate creatures and exits; --seed=N and --role=Prowler|Lurker pick one.
/// </summary>
public partial class CorridorMock : Node
{
    // (multiplier, width in cells, height in cells). Heights rise by a metre a step: a hall does not
    // need to be 15 m tall to be five times as roomy, and a taller ceiling carries its lamps higher.
    static readonly (int Mult, int Width, int Height)[] Sizes = { (1, 2, 3), (2, 4, 4), (3, 6, 5), (4, 8, 6), (5, 10, 7) };
    const float EyeHeight = 1.6f, Fov = 75f, CreatureDistance = 8f;
    const int MainEnd = -11, MainStart = 6; // the long corridor's cells along z

    string _out = "mock", _role = "Prowler";
    ulong _seed;
    bool _haveSeed;
    WildlifeData _data;
    Kit _kit;

    public override void _Ready()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--out=")) _out = a["--out=".Length..];
            else if (a.StartsWith("--seed=")) { _seed = ulong.Parse(a["--seed=".Length..]); _haveSeed = true; }
            else if (a.StartsWith("--role=")) _role = a["--role=".Length..];
        }
        Directory.CreateDirectory(_out);
        GetViewport().UseDebanding = false;
        Run();
    }

    async void Run()
    {
        try
        {
            ResourceMount.Mount();
            _data = WildlifeData.FromJson(File.ReadAllText(GameData.Proto("wildlife", "data", "wildlife.json")));
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--list") >= 0) { List(); Wildlife.Shutdown.Quit(this, 0); return; }
            var files = new List<(string, string)>();
            foreach (string p in Directory.EnumerateFiles(GameData.Proto("poi-generator", "data"), "*", SearchOption.AllDirectories))
                if (p.EndsWith(".kit") || p.EndsWith(".txt")) files.Add((Path.GetFileName(p), File.ReadAllText(p)));
            PoiLibrary lib = PoiLibrary.Load(files);
            _kit = lib.KitFor(lib.Family("water_works"));
            GD.Print($"kit {_kit.Id} (water_works); creature seed {Seed()}");

            Species species = Pick();
            Proportions b = species.Body;
            GD.Print($"creature {species.Name} the {species.CommonName}: form {species.Form}, plan {species.Plan}, rig {species.Rig}, legs {b.Legs}, "
                + $"length {b.Size:F2} m, body {b.Width:F2} x {b.Height:F2}, legs {b.LegLength:F2}, neck {b.NeckLength:F2}, head {b.HeadSize:F2}, tail {b.TailLength:F2}, corrupted {species.Corrupted}");

            var stats = new List<string>();
            foreach (var s in Sizes)
            {
                (Image view, Aabb box) = await Render(species, s, plan: false);
                view.SavePng(Path.Combine(_out, $"corridor-{s.Mult}x.png"));
                (Image plan, _) = await Render(species, s, plan: true);
                plan.SavePng(Path.Combine(_out, $"plan-{s.Mult}x.png"));
                stats.Add($"{s.Mult}x: {s.Width} m wide, {s.Height} m high; body box {box.Size.X:F2} wide x {box.Size.Y:F2} tall x {box.Size.Z:F2} long");
                GD.Print(stats[^1]);
                _boxes[s.Mult] = box;
            }
            await Sheets(species);
            Wildlife.Shutdown.Quit(this, 0);
        }
        catch (Exception e)
        {
            GD.PrintErr($"CorridorMock failed: {e}");
            Wildlife.Shutdown.Quit(this, 1);
        }
    }

    readonly Dictionary<int, Aabb> _boxes = new();

    // ---- The creature ------------------------------------------------------------------------

    ulong Seed() => _haveSeed ? _seed : 0;

    DeepRole RoleArg() => Enum.Parse<DeepRole>(_role);

    /// <summary>Hollow (Bloom-taken) industrial deep fauna over many planet seeds, as the game rolls it.</summary>
    IEnumerable<(ulong Seed, DeepRole Role, Species Sp)> Candidates()
    {
        for (ulong seed = 1; seed <= 120; seed++)
            foreach (DeepRole role in new[] { DeepRole.Prowler, DeepRole.Lurker })
                yield return (seed, role, new DeepFauna(_data, seed, 0.35f).For(role, PoiTheme.Industrial, 0, infested: true));
    }

    void List()
    {
        foreach (var (seed, role, sp) in Candidates().OrderByDescending(c => Score(c.Sp)).Take(25))
        {
            Proportions b = sp.Body;
            GD.Print($"seed {seed} {role} {sp.Name} {sp.CommonName} form={sp.Form} rig={sp.Rig} legs={b.Legs} len={b.Size:F2} w={b.Width:F2} h={b.Height:F2} legL={b.LegLength:F2} neck={b.NeckLength:F2} head={b.HeadSize:F2} ext={b.Extremity:F2} atk={sp.Attack.Style} hp={sp.Stats.MaxHealth:F0} run={sp.Stats.RunSpeed:F1} corrupted={sp.Corrupted} score={Score(sp):F2}");
        }
    }

    /// <summary>
    /// How well a species fits "as long as a horse, tall, long head and neck, charges": four legs,
    /// a horse's length (about 2.4 m) and a head standing well up (hip height + neck near 2 m or more),
    /// the longest neck and head, and a charging attack.
    /// </summary>
    static float Score(Species s)
    {
        Proportions b = s.Body;
        if (s.Rig != RigKind.Legged || b.Legs != 4) return -99f;
        float s1 = -MathF.Abs(b.Size - 2.4f);
        float s2 = MathF.Min(b.LegLength + b.Height + b.NeckLength, 3.2f);
        float s3 = (b.NeckLength + b.HeadSize) * 1.5f;
        float s4 = s.Attack.Style == AttackStyle.Lunge ? 0.5f : 0f;
        return s1 + s2 + s3 + s4;
    }

    Species Pick()
    {
        if (_haveSeed) return new DeepFauna(_data, _seed, 0.35f).For(RoleArg(), PoiTheme.Industrial, 0, infested: true);
        var best = Candidates().OrderByDescending(c => Score(c.Sp)).First();
        _seed = best.Seed;
        _haveSeed = true;
        _role = best.Role.ToString();
        GD.Print($"picked planet seed {best.Seed}, role {best.Role}");
        return best.Sp;
    }

    // ---- The corridor ------------------------------------------------------------------------

    /// <summary>The corridor as real blocks: width w, height h, a T at the end, lamps in the walls every seven metres.</summary>
    BlockGrid Corridor(int w, int h, bool roof, out List<Vector3> lamps)
    {
        lamps = new List<Vector3>();
        int half = w / 2, arm = 12;
        int crossLo = MainEnd - w + 1, crossHi = MainEnd; // the cross corridor, as wide as the main one
        var min = new Int3(-half - arm - 2, -2, crossLo - 2);
        var max = new Int3(half + arm + 1, h + 1, MainStart + 2);
        var g = new BlockGrid(min, max - min + new Int3(1, 1, 1));
        bool Air(Int3 p)
        {
            if (p.Y < 0 || p.Y >= h) return false;
            bool main = p.X >= -half && p.X < half && p.Z >= MainEnd && p.Z <= MainStart;
            bool cross = p.X >= -half - arm && p.X < half + arm && p.Z >= crossLo && p.Z <= crossHi;
            return main || cross;
        }
        for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
                for (int x = min.X; x <= max.X; x++)
                {
                    var p = new Int3(x, y, z);
                    if (Air(p)) continue;
                    bool near = Dirs.All.Any(d => Air(p + d.Vec()));
                    if (!near) continue;
                    char sym = y < 0 ? '_' : y >= h ? '=' : '#';
                    if (sym == '=' && !roof) continue;
                    g.Set(p, _kit.BySymbol(sym)!.Make(p));
                }
        // A lamp set in the wall beside the corridor, every seven metres, up near the ceiling.
        PaletteEntry lampBlock = _kit.FirstWithRole(PoiGenerator.Gen.Role.Light);
        for (int z = MainStart - 2; z >= MainEnd + 1; z -= 7)
        {
            var wallCell = new Int3(-half - 1, h - 1, z);
            g.Set(wallCell, lampBlock.Make(wallCell));
            lamps.Add(new Vector3(-half + 0.5f, h - 0.6f, z + 0.5f));
        }
        for (int z = MainStart - 5; z >= MainEnd + 1; z -= 7)
        {
            var wallCell = new Int3(half, h - 1, z);
            g.Set(wallCell, lampBlock.Make(wallCell));
            lamps.Add(new Vector3(half - 0.5f, h - 0.6f, z + 0.5f));
        }
        // Hollow Bloom: growths on the walls and strands hanging from the ceiling, as the infested state does it.
        var rng = new Rng(777);
        for (int n = 0; n < 0; n++)
        {
            int z = rng.Between(MainEnd + 1, -4);
            bool left = rng.Chance(0.5f);
            var c = new Int3(left ? -half : half - 1, rng.Between(0, Math.Min(2, h - 1)), z);
            if (!g.Get(c).IsAir) continue;
            g.Set(c, new Block(BlockType.Growth, Material.Bloom, rng.Chance(0.5f) ? StateTransforms.BloomFlesh : StateTransforms.BloomBruise, Role.Growth, Dir.North, (byte)rng.Range(0, 4)));
        }
        for (int n = 0; n < 6; n++)
        {
            var c = new Int3(rng.Between(-half, half - 1), h - 1, rng.Between(MainEnd + 1, -3));
            if (!g.Get(c).IsAir) continue;
            g.Set(c, new Block(BlockType.Tendril, Material.Bloom, rng.Chance(0.5f) ? StateTransforms.BloomFlesh : StateTransforms.BloomBruise, Role.Growth));
        }
        return g;
    }

    // ---- One render --------------------------------------------------------------------------

    Node3D _stage;

    async System.Threading.Tasks.Task<(Image, Aabb)> Render(Species species, (int Mult, int Width, int Height) size, bool plan)
    {
        _stage?.QueueFree();
        _stage = new Node3D { Name = $"Stage{size.Mult}" };
        AddChild(_stage);

        BlockGrid grid = Corridor(size.Width, size.Height, roof: !plan, out List<Vector3> lamps);
        var materials = new BlockMaterials(2.4f);
        var opt = new MeshOptions { ChunkSize = 16, Doors = false, Collision = false, Detail = true, Rock = false };
        foreach (ChunkMesh chunk in Mesher.Build(grid, new CellMask(grid.Min, grid.Size), opt))
            _stage.AddChild(new MeshInstance3D { Mesh = MeshBuild.ToMesh(chunk, materials.BySurface) });

        // The place's own lamps: what the generator makes (steady, energy 1.0, range 7), in the kit's light colour.
        Rgb lc = _kit.Light;
        foreach (Vector3 at in lamps)
            _stage.AddChild(new OmniLight3D { Position = at, LightColor = new Color(lc.R / 255f, lc.G / 255f, lc.B / 255f), LightEnergy = 1.0f, OmniRange = 7f, OmniAttenuation = 1.4f, ShadowEnabled = false });

        // Dark, like the inside of a place: the little ambient there is comes from the lamps' bounce.
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.02f, 0.02f, 0.025f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.30f, 0.27f, 0.25f),
            AmbientLightEnergy = plan ? 1.6f : 0.55f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        _stage.AddChild(new WorldEnvironment { Environment = env });

        // The creature: a real Hollow species, spawned in a sim over flat ground at the floor's height.
        var terrain = new Terrain((x, z) => 0f, 40f);
        var sim = new WildSim(_data, terrain, new NavGrid(terrain.HalfSize, 3f));
        Creature c = sim.Spawn(species, new Vector3(0, 0, -CreatureDistance));
        c.Yaw = Mathf.Pi; // forward is (-sin yaw, 0, -cos yaw): toward +z, the camera
        c.Position = new Vector3(0, 0, -CreatureDistance);
        for (int i = 0; i < 90; i++) c.Rig.Update(sim, c, 1f / 60f);
        var view = new CreatureView();
        _stage.AddChild(view);
        view.Build(c);
        view.Sync();

        if (plan)
        {
            Aabb pb = default;
            bool pf = true;
            foreach (Node n in view.GetChildren())
                if (n is MeshInstance3D m) { Aabb a = m.GlobalTransform * m.GetAabb(); pb = pf ? a : pb.Merge(a); pf = false; }
            float ring = TurnCircle(pb) / 2f;
            var im = new ImmediateMesh();
            im.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
            for (int i = 0; i <= 96; i++)
            {
                float t = i / 96f * Mathf.Tau;
                im.SurfaceAddVertex(new Vector3(Mathf.Sin(t) * ring, 1.2f, -CreatureDistance + Mathf.Cos(t) * ring));
            }
            im.SurfaceEnd();
            _stage.AddChild(new MeshInstance3D { Mesh = im, MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.2f, 1f, 0.3f) }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        var cam = new Camera3D { Name = "Camera", Near = 0.05f, Far = 200f, Current = true };
        if (plan)
        {
            cam.Projection = Camera3D.ProjectionType.Orthogonal;
            cam.Size = 14f;
            cam.Position = new Vector3(0, 30f, -7f);
            cam.RotationDegrees = new Vector3(-90f, 0, 0);
            var sun = new DirectionalLight3D { RotationDegrees = new Vector3(-90f, 0, 0), LightEnergy = 0.5f };
            _stage.AddChild(sun);
        }
        else
        {
            cam.Fov = Fov;
            cam.Position = new Vector3(0, EyeHeight, 0);
            cam.RotationDegrees = new Vector3(-2f, 0, 0); // looking straight down the corridor; the survivor's pitch rests near level
            var torch = new SpotLight3D
            {
                Position = new Vector3(0.12f, 0.05f, 0f), SpotRange = 45f, SpotAngle = 32f, LightEnergy = 5.5f,
                SpotAngleAttenuation = 0.8f, LightColor = new Color(1f, 0.95f, 0.85f), ShadowEnabled = true,
            };
            cam.AddChild(torch);
        }
        _stage.AddChild(cam);

        // The creature's real extent, from its drawn parts.
        Aabb box = default;
        bool first = true;
        foreach (Node n in view.GetChildren())
            if (n is MeshInstance3D mi)
            {
                Aabb a = mi.GlobalTransform * mi.GetAabb();
                box = first ? a : box.Merge(a);
                first = false;
            }

        await Frames(3);
        view.Sync();
        await Frames(6);
        Image img = await Grab();
        return (img, box);
    }

    async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    async System.Threading.Tasks.Task<Image> Grab()
    {
        await Frames(2);
        return GetViewport().GetTexture().GetImage();
    }

    // ---- The sheets --------------------------------------------------------------------------

    async System.Threading.Tasks.Task Sheets(Species sp)
    {
        _stage?.QueueFree();
        _stage = null;
        Proportions b = sp.Body;
        Aabb m1 = _boxes[1];
        string head = $"{sp.Name} ({sp.CommonName}; wildlife generator deep fauna, Prowler, {sp.Form}, {sp.Rig}): drawn {m1.Size.Z:F1} m long, {m1.Size.X:F1} m wide, {m1.Size.Y:F1} m tall";

        await Sheet("corridors.png", "corridor-{0}x.png",
            $"POI corridor widths: eye {EyeHeight} m, vertical field of view {Fov:0} degrees, creature stands {CreatureDistance:0} m off. Today's rule is 1x. {head}",
            s => $"{s.Mult}x{(s.Mult == 1 ? " (today)" : "")}: {s.Width} m wide x {s.Height} m high");

        await Sheet("corridors-side.png", "plan-{0}x.png",
            $"From above, same scale, 1 m grid cells are the wall blocks. The ring is the creature turning on the spot (its longest reach from the middle). {head}",
            s =>
            {
                Aabb a = _boxes[s.Mult];
                float len = Mathf.Max(a.Size.X, a.Size.Z);
                float circle = TurnCircle(a);
                string fit = s.Width >= circle ? "can turn round" : s.Width >= a.Size.X ? "fits, cannot turn round" : "does not fit";
                return $"{s.Mult}x: {s.Width} m wide x {s.Height} m high. Body {a.Size.X:F1} m across, {len:F1} m long. Turning ring {circle:F1} m: {fit}";
            });
    }

    /// <summary>The ring a body sweeps when it turns round on the spot about its middle: twice its farthest corner.</summary>
    static float TurnCircle(Aabb a)
    {
        float x = Mathf.Max(Mathf.Abs(a.Position.X), Mathf.Abs(a.End.X));
        float z = Mathf.Max(Mathf.Abs(a.Position.Z - (-CreatureDistance)), Mathf.Abs(a.End.Z - (-CreatureDistance)));
        return 2f * Mathf.Sqrt(x * x + z * z);
    }

    async System.Threading.Tasks.Task Sheet(string file, string pattern, string title, Func<(int Mult, int Width, int Height), string> label)
    {
        const int cw = 800, ch = 450, pad = 14, top = 110, cap = 56;
        int rows = 2, cols = 3;
        var size = new Vector2I(cols * (cw + pad) + pad, top + rows * (ch + cap + pad));
        var vp = new SubViewport { Size = size, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, TransparentBg = false, Disable3D = true };
        AddChild(vp);
        vp.AddChild(new ColorRect { Color = new Color(0.07f, 0.07f, 0.08f), Size = size });
        vp.AddChild(Text(title, new Vector2(pad, 12), cols * (cw + pad) - pad, 24, Colors.White));
        for (int i = 0; i < Sizes.Length; i++)
        {
            var s = Sizes[i];
            int col = i % cols, row = i / cols;
            var at = new Vector2(pad + col * (cw + pad), top + row * (ch + cap + pad));
            var img = Image.LoadFromFile(Path.Combine(_out, string.Format(pattern, s.Mult)));
            img.Resize(cw, ch, Image.Interpolation.Lanczos);
            vp.AddChild(new TextureRect { Texture = ImageTexture.CreateFromImage(img), Position = at, Size = new Vector2(cw, ch) });
            vp.AddChild(Text(label(s), at + new Vector2(0, ch + 6), cw, 22, new Color(1f, 0.85f, 0.45f)));
        }
        // The sixth cell: a key to the numbers.
        {
            int col = 2, row = 1;
            var at = new Vector2(pad + col * (cw + pad), top + row * (ch + cap + pad));
            vp.AddChild(Text(string.Join("\n", Sizes.Select(s => $"{s.Mult}x   {s.Width} m wide  x  {s.Height} m high")), at + new Vector2(20, 20), cw - 40, 30, Colors.White));
        }
        await Frames(4);
        Image sheet = vp.GetTexture().GetImage();
        sheet.SavePng(Path.Combine(_out, file));
        GD.Print($"saved {file} {sheet.GetWidth()}x{sheet.GetHeight()}");
        vp.QueueFree();
    }

    static Label Text(string text, Vector2 at, float width, int fontSize, Color color)
    {
        var l = new Label { Text = text, Position = at, Size = new Vector2(width, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}
