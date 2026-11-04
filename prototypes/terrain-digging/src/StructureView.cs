using System.Collections.Generic;
using Godot;
using TerrainDigging.Terrain;

namespace TerrainDigging;

/// <summary>
/// Draws placed building blocks and gives them collision, and briefly shows anchor zones
/// (the ground a structure or POI protects) when the player runs into one.
/// </summary>
public partial class StructureView : Node3D
{
    readonly Dictionary<int, StaticBody3D> _blocks = new();
    readonly List<(MeshInstance3D mesh, ShaderMaterial material, float age)> _flashes = new();

    public void Add(Structure s)
    {
        Vector3 size = new(s.Box.Size.X, s.Box.Size.Y, s.Box.Size.Z);
        var material = Shaders.Make(Shaders.Block);
        material.SetShaderParameter("body_color", Palette.Block);
        material.SetShaderParameter("trim_color", Palette.BlockTrim);
        material.SetShaderParameter("stripe_color", Palette.BlockStripe);
        material.SetShaderParameter("half_size", size / 2f);

        var body = new StaticBody3D
        {
            Name = $"Block {s.Id}",
            CollisionLayer = Layers.Structure,
            CollisionMask = 0,
            Position = ToGodot(s.Box.CenterPosition),
        };
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
        _blocks[s.Id] = body;
    }

    /// <summary>Replaces every block with the given set (after a load).</summary>
    public void Rebuild(IEnumerable<Structure> structures)
    {
        foreach (StaticBody3D b in _blocks.Values) b.QueueFree();
        _blocks.Clear();
        foreach (Structure s in structures) Add(s);
    }

    public int Count => _blocks.Count;

    /// <summary>Shows an anchored zone as a glowing box for a moment.</summary>
    public void FlashAnchor(CellBox box, Color color)
    {
        var material = Shaders.Make(Shaders.Marker);
        material.SetShaderParameter("color", new Color(color, 0.35f));
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(box.Size.X, box.Size.Y, box.Size.Z) },
            Position = ToGodot(box.CenterPosition),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mesh);
        _flashes.Add((mesh, material, 0f));
    }

    public override void _Process(double delta)
    {
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            (MeshInstance3D mesh, ShaderMaterial material, float age) = _flashes[i];
            age += (float)delta;
            if (age > 1.6f)
            {
                mesh.QueueFree();
                _flashes.RemoveAt(i);
                continue;
            }
            material.SetShaderParameter("strength", Mathf.Clamp(1.6f - age, 0f, 1f) * 0.5f);
            _flashes[i] = (mesh, material, age);
        }
    }

    static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
