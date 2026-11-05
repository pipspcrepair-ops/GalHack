using System;
using System.Collections.Generic;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Planets.Orbit;

/// <summary>
/// The planet from orbit: a slowly turning globe against deep space. Its surface is the
/// generator's own colour map, wrapped as an equirectangular projection, so every coastline
/// seen from here is one you can land on. The map itself is a torus (it wraps north-south too);
/// on the globe its top and bottom rows each pinch to a pole, which shows no seam.
/// </summary>
public partial class OrbitView : Node3D
{
    const float SpinSpeed = 0.05f;   // rad/s
    const float AxialTilt = 0.38f;   // rad
    static readonly Vector3 SunTravel = new Vector3(0.92f, -0.16f, -0.2f).Normalized(); // from the left: a dramatic terminator

    Node3D _tilt, _spin, _cloudSpin;
    MeshInstance3D _ring;
    ShaderMaterial _planet, _clouds, _atmosphere, _ringMaterial, _space;
    public Camera3D Camera { get; private set; }

    public override void _Ready()
    {
        _space = new ShaderMaterial { Shader = Shaders.Space };
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = _space, RadianceSize = Sky.RadianceSizeEnum.Size32 },
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.05f, 0.05f, 0.09f),
                AmbientLightEnergy = 1f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapWhite = 6f,
                GlowEnabled = true,
                GlowIntensity = 0.9f,
                GlowBloom = 0.08f,
                GlowHdrThreshold = 0.9f,
            },
        });

        var sun = new DirectionalLight3D { Name = "Sun", LightColor = new Color("fff1de"), LightEnergy = 1.7f };
        AddChild(sun);
        sun.Basis = Basis.LookingAt(SunTravel, Vector3.Up);

        Camera = new Camera3D { Name = "Camera", Fov = 36f, Position = new Vector3(0, 0.3f, 4.25f) };
        AddChild(Camera);
        Camera.LookAtFromPosition(Camera.Position, new Vector3(0.3f, 0.02f, 0), Vector3.Up);

        _tilt = new Node3D { Name = "Tilt", Rotation = new Vector3(0.12f, 0f, AxialTilt) };
        AddChild(_tilt);
        _spin = new Node3D { Name = "Spin" };
        _tilt.AddChild(_spin);
        _cloudSpin = new Node3D { Name = "CloudSpin" };
        _tilt.AddChild(_cloudSpin);

        _planet = new ShaderMaterial { Shader = Shaders.Planet };
        _planet.SetShaderParameter("sun_dir", -SunTravel);
        _spin.AddChild(Sphere(1f, 192, 96, _planet));

        _clouds = new ShaderMaterial { Shader = Shaders.Cloud };
        _cloudSpin.AddChild(Sphere(1.012f, 128, 64, _clouds));

        _atmosphere = new ShaderMaterial { Shader = Shaders.Atmosphere };
        _atmosphere.SetShaderParameter("sun_dir", -SunTravel);
        AddChild(Sphere(1.075f, 96, 48, _atmosphere));

        _ringMaterial = new ShaderMaterial { Shader = Shaders.Ring };
        _ring = new MeshInstance3D { Name = "Ring", Mesh = RingMesh(1.45f, 2.3f, 160), MaterialOverride = _ringMaterial, Visible = false };
        _tilt.AddChild(_ring);
    }

    public override void _Process(double delta)
    {
        _spin.RotateY((float)delta * SpinSpeed);
        _cloudSpin.RotateY((float)delta * SpinSpeed * 1.25f);
    }

    /// <summary>Shows a planet. Textures come from Main, built off the main thread.</summary>
    public void Show(PlanetSpec spec, ArchetypeLook look, Texture2D albedo, Texture2D glow, Texture2D clouds)
    {
        _planet.SetShaderParameter("albedo_map", albedo);
        _planet.SetShaderParameter("glow_map", glow);
        _planet.SetShaderParameter("day_glow", look.LiquidGlow > 0 ? 0.9f : 0.5f);
        _planet.SetShaderParameter("night_glow", look.GlowingNights ? 2.5f : 1.4f);
        _clouds.SetShaderParameter("cloud_map", clouds);
        _atmosphere.SetShaderParameter("color", C(look.Atmosphere));
        _space.SetShaderParameter("nebula", C(look.Nebula));
        _space.SetShaderParameter("nebula_b", C(look.Atmosphere * 0.6f));
        _ring.Visible = look.Ring;
        _ringMaterial.SetShaderParameter("color", C(look.RingColor));
    }

    /// <summary>Turns the globe so the given map x (metres east) faces the camera.</summary>
    public void Face(WorldMap map, double x)
    {
        var (now, _) = FacingPoint(map);
        double du = (x - now) / map.WidthMeters;
        // Spinning the globe by an angle turns what faces the camera back by the same angle.
        _spin.RotateY((float)(-du * Math.Tau));
        _cloudSpin.RotateY((float)(-du * Math.Tau));
    }

    /// <summary>The map point (metres) currently facing the camera: where Enter lands you.</summary>
    public (double x, double y) FacingPoint(WorldMap map)
    {
        Vector3 toCamera = (Camera.GlobalPosition - _spin.GlobalPosition).Normalized();
        Vector3 local = _spin.GlobalBasis.Inverse() * toCamera;
        // Godot's sphere puts u = 0 on +Z and runs towards +X; v = 0 is the north pole.
        double u = Math.Atan2(local.X, local.Z) / (2 * Math.PI);
        if (u < 0) u += 1;
        double v = Math.Acos(Math.Clamp(local.Y, -1f, 1f)) / Math.PI;
        return (u * map.WidthMeters, v * map.HeightMeters);
    }

    static MeshInstance3D Sphere(float radius, int segments, int rings, Material material) => new()
    {
        Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = segments, Rings = rings },
        MaterialOverride = material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>A flat annulus in the equatorial plane; UV.x runs from the inner to the outer edge.</summary>
    static ArrayMesh RingMesh(float inner, float outer, int segments)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        for (int s = 0; s <= segments; s++)
        {
            float a = s * Mathf.Tau / segments;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            verts.Add(d * inner);
            verts.Add(d * outer);
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            normals.Add(Vector3.Up);
            normals.Add(Vector3.Up);
            if (s == 0) continue;
            int b = (s - 1) * 2;
            indices.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    static Color C(Rgb c) => new(c.R, c.G, c.B);
}
