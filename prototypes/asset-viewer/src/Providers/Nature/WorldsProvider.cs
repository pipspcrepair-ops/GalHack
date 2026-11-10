using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Planets.Gen;
using Planets.Look;
using Planets.Orbit;
using PlanetSurface.Skies;
using Archetype = Planets.Gen.Archetype;
using Rgb = Planets.Look.Rgb;

namespace AssetViewer.Providers.Nature;

/// <summary>
/// Worlds from space: the generator's globes (every world type, with its modifiers, and moons), gas giants with
/// their bands and rings as a system rolls them, stars by class and black holes in three sizes. Globes are the
/// generator's own OrbitView, wearing the colour map, clouds and relief of the same world the ground view streams.
/// </summary>
public sealed partial class WorldsProvider : IAssetProvider
{
    public string Category => "Worlds";
    public IEnumerable<(string Prototype, string Folder)> Mounts => new[] { ("diegetic-ui", "shaders") };

    static ulong SeedOf(BuildRequest r) => r.Seed == 0 ? 1UL : r.Seed;

    public IEnumerable<AssetEntry> Entries()
    {
        string[] mods = new[] { "Rolled" }.Concat(Enum.GetValues<Modifier>().Where(m => m != Modifier.None).Select(m => m.ToString())).ToArray();
        foreach (var t in NatureWorlds.Types())
        {
            var type = t;
            yield return new AssetEntry
            {
                Id = $"globe-{type.Archetype}-{type.Variant}", Group = "Planets", Name = type.Label, Randomisable = true,
                Options = new[] { new AssetOption("Size", "8 km", "16 km", "32 km", "64 km") { Default = 1 }, new AssetOption("Modifier", mods) },
                Build = r => Globe(r, type.Archetype, type.Variant, false),
            };
        }
        foreach (var t in NatureWorlds.Types().Where(t => PlanetSpec.Craters(t.Archetype)))
        {
            var type = t;
            yield return new AssetEntry
            {
                Id = $"moon-{type.Archetype}-{type.Variant}", Group = "Moons", Name = type.Label + " moon", Randomisable = true,
                Options = new[] { new AssetOption("Size", "8 km", "16 km") },
                Build = r => Globe(r, type.Archetype, type.Variant, true),
            };
        }
        yield return new AssetEntry { Id = "giant", Group = "Gas giants", Name = "Gas giant", Randomisable = true, Options = new[] { new AssetOption("Rings", "As rolled", "Ringed", "Bare") }, Build = Giant };
        foreach (StarClass c in Enum.GetValues<StarClass>())
        {
            StarClass cls = c;
            yield return new AssetEntry { Id = "star-" + cls, Group = "Stars", Name = $"{cls} star", Randomisable = true, Build = r => new StarView(Find(SeedOf(r), cls), cls.ToString()) };
        }
        foreach (string size in new[] { "Stellar", "Intermediate", "Supermassive" })
        {
            string s = size;
            yield return new AssetEntry { Id = "blackhole-" + s, Group = "Black holes", Name = s + " black hole", Build = _ => new HoleView(s) };
        }
    }

    static SkySystem Find(ulong seed, StarClass cls)
    {
        var types = NatureWorlds.Types().ToList();
        for (int k = 0; k < 900; k++)
        {
            var t = types[k % types.Count];
            SkySystem sky = SkySystem.For(NatureWorlds.SpecOf(t.Archetype, t.Variant, seed + (ulong)k * 7919));
            if (sky.Star == cls) return sky;
        }
        return SkySystem.For(NatureWorlds.SpecOf(Archetype.Temperate, 0, seed));
    }

    static AssetInstance Globe(BuildRequest r, Archetype a, int variant, bool moon)
    {
        int km = int.Parse(r.Get("Size", "16 km").Split(' ')[0]);
        PlanetSpec spec = PlanetSpec.Roll(SeedOf(r), a, variant, km);
        string mod = r.Get("Modifier", "Rolled");
        if (mod != "Rolled" && Enum.TryParse(mod, out Modifier m)) spec = spec with { Modifiers = m };
        if (moon) spec = spec with { Modifiers = spec.Modifiers | Modifier.Cratered };
        return new GlobeView(spec, moon);
    }

    static AssetInstance Giant(BuildRequest r)
    {
        var types = NatureWorlds.Types().ToList();
        string rings = r.Get("Rings", "As rolled");
        SkyBody pick = null;
        for (int k = 0; k < 900 && pick == null; k++)
        {
            var t = types[k % types.Count];
            foreach (SkyBody b in SkySystem.For(NatureWorlds.SpecOf(t.Archetype, t.Variant, SeedOf(r) + (ulong)k * 7919)).Bodies)
                if (b.Kind == BodyKind.GasGiant && (rings == "As rolled" || b.Rings == (rings == "Ringed"))) { pick = b; break; }
        }
        return new GiantView(pick);
    }
}

/// <summary>A planet globe: the generator's orbit view of a world.</summary>
public sealed partial class GlobeView : AssetInstance
{
    readonly PlanetSpec _spec;
    readonly OrbitView _orbit = new() { Name = "Orbit" };
    readonly WorldMap _map;
    readonly ArchetypeLook _look;
    readonly ColorMap _colors;
    readonly byte[] _clouds;
    readonly GlobeRelief.Data _relief;
    public override Aabb? FrameBounds => new Aabb(new Vector3(-1.3f, -1.3f, -1.3f), new Vector3(2.6f, 2.6f, 2.6f));
    public override bool Spins => false;

    public GlobeView(PlanetSpec spec, bool moon)
    {
        _spec = spec;
        Stage.OwnsEnvironment = true;
        Stage.Ground = false;
        Stage.MinDistanceShare = 0.3f;
        _map = PlanetGenerator.Generate(spec);
        _look = ArchetypeLook.For(spec);
        _colors = ColorMap.Build(_map, _look, 2048);
        _clouds = CloudMap.Build(spec, _look, 1024, out _);
        _relief = GlobeRelief.Build(_map, 360);
        AddChild(_orbit);
    }

    public override void _Ready()
    {
        _orbit.Camera.Current = false;
        _orbit.Show(_spec, _look, Tex(_colors.Width, _colors.Height, Image.Format.Rgba8, _colors.Albedo), Tex(_colors.Width, _colors.Height, Image.Format.Rgb8, _colors.Glow), Tex(1024, 512, Image.Format.Rgba8, _clouds));
        _orbit.ShowRelief(GlobeRelief.Mesh(_relief));
    }

    static ImageTexture Tex(int w, int h, Image.Format f, byte[] data) => ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, f, data));

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        yield return ("Type", Archetypes.Get(_spec.Archetype, _spec.Variant).Archetype + " " + Archetypes.Get(_spec.Archetype, _spec.Variant).VariantName);
        yield return ("Size", $"{_spec.WidthKm} x {_spec.HeightKm} km");
        yield return ("Modifiers", _spec.Modifiers == Modifier.None ? "none" : _spec.Modifiers.ToString());
        yield return ("Land", $"{_map.LandFraction * 100f:F0}%");
        yield return ("Rivers", _map.Rivers.Count.ToString());
        yield return ("Air", SkySystem.For(_spec).Air.ToString());
        yield return ("Ring", _look.Ring ? "yes" : "no");
    }
}

/// <summary>A gas giant: banded globe and, where the system rolled them, rings.</summary>
public sealed partial class GiantView : AssetInstance
{
    readonly SkyBody _body;

    public GiantView(SkyBody body)
    {
        _body = body;
        Stage.Ground = false;
        Stage.Background = "space";
        Color Light(Rgb c) => new(c.R, c.G, c.B);
        var sphere = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 96, Rings = 48 } };
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/gas_giant.gdshader") };
        if (body != null)
        {
            m.SetShaderParameter("light_band", Light(body.Color));
            m.SetShaderParameter("dark_band", Light(body.Band));
            m.SetShaderParameter("storm", Light(body.Band).Lerp(Colors.White, 0.35f));
        }
        sphere.MaterialOverride = m;
        sphere.RotationDegrees = new Vector3(0f, 0f, 14f);
        AddChild(sphere);
        if (body is { Rings: true })
        {
            var ring = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/planet_ring.gdshader") };
            ring.SetShaderParameter("tint", Light(body.RingColor));
            var mesh = new MeshInstance3D { Mesh = Ring(2.6f), MaterialOverride = ring, RotationDegrees = new Vector3(0f, 0f, 14f) };
            AddChild(mesh);
        }
    }

    static ArrayMesh Ring(float outer)
    {
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var idx = new List<int>();
        for (int s = 0; s <= 128; s++)
        {
            float a = s * Mathf.Tau / 128f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts.Add(d * outer * 0.6f); verts.Add(d * outer);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
            if (s == 0) continue;
            int b = (s - 1) * 2;
            idx.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        yield return ("Rings", _body is { Rings: true } ? "yes" : "no");
        if (_body != null) yield return ("Sky radius from home", $"{Mathf.RadToDeg(_body.Radius):F1} degrees");
    }
}

/// <summary>A star as a lit ball with a granulated surface and a corona, in the colour its class gives it.</summary>
public sealed partial class StarView : AssetInstance
{
    readonly SkySystem _sky;
    readonly string _class;

    public StarView(SkySystem sky, string cls)
    {
        _sky = sky;
        _class = cls;
        Stage.Ground = false;
        Stage.Background = "space";
        Rgb c = sky.StarColor;
        var shader = new Shader
        {
            Code = @"shader_type spatial;
render_mode unshaded;
uniform vec3 colour : source_color = vec3(1.0, 0.9, 0.6);
float h(vec3 p) { return fract(sin(dot(p, vec3(12.9898, 78.233, 45.164))) * 43758.5453); }
float n(vec3 p) { vec3 i = floor(p); vec3 f = fract(p); f = f * f * (3.0 - 2.0 * f);
  return mix(mix(mix(h(i), h(i + vec3(1,0,0)), f.x), mix(h(i + vec3(0,1,0)), h(i + vec3(1,1,0)), f.x), f.y),
             mix(mix(h(i + vec3(0,0,1)), h(i + vec3(1,0,1)), f.x), mix(h(i + vec3(0,1,1)), h(i + vec3(1,1,1)), f.x), f.y), f.z); }
varying vec3 obj;
void vertex() { obj = VERTEX; }
void fragment() {
  float g = n(obj * 6.0) * 0.6 + n(obj * 14.0) * 0.4;
  float rim = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 2.0);
  ALBEDO = colour * (1.0 + (g - 0.5) * 0.7 - rim * 0.35) * 2.2;
}",
        };
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("colour", new Color(c.R, c.G, c.B));
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 96, Rings = 48 }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        var corona = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = new Color(c.R, c.G, c.B, 0.07f), CullMode = BaseMaterial3D.CullModeEnum.Front, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.25f, Height = 2.5f }, MaterialOverride = corona, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        yield return ("Class", _class);
        yield return ("Energy", _sky.SunEnergy.ToString("F2"));
        yield return ("Colour", $"{_sky.StarColor.R:F2}, {_sky.StarColor.G:F2}, {_sky.StarColor.B:F2}");
    }
}

/// <summary>A black hole: the shadow, the bright ring at its edge and the accretion disc, in three sizes.</summary>
public sealed partial class HoleView : AssetInstance
{
    readonly string _size;

    public HoleView(string size)
    {
        _size = size;
        Stage.Ground = false;
        Stage.Background = "space";
        float scale = size switch { "Stellar" => 1f, "Intermediate" => 2.2f, _ => 4.5f };
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.62f * scale, Height = 1.24f * scale, RadialSegments = 64, Rings = 32 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Black, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        });
        var disc = new Shader
        {
            Code = @"shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never;
varying vec2 pos;
void vertex() { pos = VERTEX.xz; }
void fragment() {
  float r = length(pos);
  float t = clamp((r - 0.75) / 2.1, 0.0, 1.0);
  float a = smoothstep(0.0, 0.08, t) * (1.0 - smoothstep(0.7, 1.0, t));
  vec3 hot = mix(vec3(1.0, 0.95, 0.8), vec3(1.0, 0.35, 0.08), t);
  float swirl = 0.75 + 0.25 * sin(atan(pos.y, pos.x) * 5.0 + r * 9.0);
  ALBEDO = hot * a * swirl * 2.5;
}",
        };
        var ring = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(7f, 7f) * scale, SubdivideWidth = 2, SubdivideDepth = 2 }, MaterialOverride = new ShaderMaterial { Shader = disc }, RotationDegrees = new Vector3(0f, 0f, 14f) };
        ring.Scale = Vector3.One;
        AddChild(ring);
        var halo = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.8f, 0.5f), CullMode = BaseMaterial3D.CullModeEnum.Front,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        halo.AlbedoColor = new Color(1f, 0.8f, 0.5f, 0.35f);
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.7f * scale, Height = 1.4f * scale }, MaterialOverride = halo });
    }

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        yield return ("Size", _size);
        yield return ("Parts", "shadow, photon ring, accretion disc");
    }
}
