using System.Collections.Generic;
using System.Linq;
using Godot;
using TerrainDigging.Terrain;

namespace TerrainDigging;

/// <summary>
/// The survival tool's detector: a ping that sweeps outwards and marks nearby ore deposits
/// through the ground, with distance and size, for a few seconds.
/// </summary>
public partial class OreDetector : Node3D
{
    public const float Range = 26f;
    const float MarkerLife = 9f, WaveTime = 0.9f;

    public ChunkStore Store;

    public readonly record struct Deposit(Vector3 Center, int Cells, float Distance);

    readonly List<(Node3D node, ShaderMaterial material)> _markers = new();
    MeshInstance3D _wave;
    ShaderMaterial _waveMaterial;
    float _age = float.MaxValue;
    Vector3 _origin;

    public override void _Ready()
    {
        _waveMaterial = Shaders.Make(Shaders.Marker);
        _waveMaterial.SetShaderParameter("color", new Color(0.18f, 0.96f, 0.82f, 0.5f));
        _wave = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = _waveMaterial,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        };
        AddChild(_wave);
    }

    /// <summary>Finds ore around a point, groups it into deposits and marks the nearest ones.</summary>
    public List<Deposit> Ping(Vector3 origin)
    {
        _origin = origin;
        _age = 0f;
        foreach ((Node3D node, _) in _markers) node.QueueFree();
        _markers.Clear();

        var at = new System.Numerics.Vector3(origin.X, origin.Y, origin.Z);
        List<Int3> cells = Store.FindCells(CellMaterial.Ore, at, Range, 6000);

        // Group cells into 4 m buckets: one marker per deposit, not per cell.
        var deposits = cells
            .GroupBy(c => new Int3(Mathf.FloorToInt(c.X / 4f), Mathf.FloorToInt(c.Y / 4f), Mathf.FloorToInt(c.Z / 4f)))
            .Select(g =>
            {
                Vector3 sum = Vector3.Zero;
                foreach (Int3 c in g) sum += new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f);
                Vector3 center = sum / g.Count();
                return new Deposit(center, g.Count(), center.DistanceTo(origin));
            })
            .Where(d => d.Cells >= 3)
            .OrderBy(d => d.Distance)
            .Take(10)
            .ToList();

        foreach (Deposit d in deposits) AddMarker(d);
        return deposits;
    }

    void AddMarker(Deposit d)
    {
        var material = Shaders.Make(Shaders.Marker);
        material.SetShaderParameter("color", new Color(0.18f, 1f, 0.84f, 0.9f));
        float size = Mathf.Clamp(0.35f + d.Cells * 0.012f, 0.4f, 1.1f);
        var marker = new Node3D { Position = d.Center, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        marker.AddChild(new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(size, size * 1.4f, size) },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            RotationDegrees = new Vector3(180f, 0f, 0f),
        });
        var label = new Label3D
        {
            Text = $"{d.Distance:F0} m · {d.Cells}",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FixedSize = true,
            PixelSize = 0.0011f,
            FontSize = 28,
            OutlineSize = 8,
            Modulate = new Color(0.75f, 1f, 0.95f),
            Position = new Vector3(0f, size + 0.3f, 0f),
        };
        marker.AddChild(label);
        AddChild(marker);
        _markers.Add((marker, material));
    }

    public override void _Process(double delta)
    {
        if (_age == float.MaxValue) return;
        _age += (float)delta;

        float wave = _age / WaveTime;
        _wave.Visible = wave < 1f;
        if (_wave.Visible)
        {
            _wave.Position = _origin;
            _wave.Scale = Vector3.One * Mathf.Max(0.1f, Range * wave);
            _waveMaterial.SetShaderParameter("strength", 1f - wave);
        }

        // Markers light up as the wave passes them, then fade out together.
        float fade = Mathf.Clamp((MarkerLife - _age) / 1.5f, 0f, 1f);
        foreach ((Node3D node, ShaderMaterial material) in _markers)
        {
            float reached = Mathf.Clamp((_age - node.Position.DistanceTo(_origin) / Range * WaveTime) * 4f, 0f, 1f);
            float pulse = 0.75f + 0.25f * Mathf.Sin(_age * 6f);
            material.SetShaderParameter("strength", reached * fade * pulse);
            node.Visible = reached > 0f && fade > 0f;
            node.RotateY((float)delta * 1.5f);
        }
        if (_age > MarkerLife)
        {
            foreach ((Node3D node, _) in _markers) node.QueueFree();
            _markers.Clear();
            _age = float.MaxValue;
        }
    }
}
