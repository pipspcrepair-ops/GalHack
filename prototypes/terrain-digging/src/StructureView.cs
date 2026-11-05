using System.Collections.Generic;
using Godot;
using TerrainDigging.Terrain;

namespace TerrainDigging;

/// <summary>
/// Draws placed building blocks and gives them collision, and briefly highlights a block when
/// it is placed. Oriented blocks (a vessel's or a base's, at any heading) are drawn the same way.
/// </summary>
public partial class StructureView : Node3D
{
    readonly Dictionary<int, StaticBody3D> _blocks = new();
    readonly Dictionary<VolumeKey, StaticBody3D> _oriented = new();
    readonly List<(MeshInstance3D mesh, ShaderMaterial material, float age)> _flashes = new();

    public void Add(Structure s)
    {
        StaticBody3D body = Block($"Block {s.Id}", StructureVolume.Box(s.Box), Palette.Block);
        _blocks[s.Id] = body;
    }

    /// <summary>An oriented block (tests and screenshots): drawn and solid like any other.</summary>
    public void Add(VolumeKey key, StructureVolume v, Color color)
    {
        if (_oriented.Remove(key, out StaticBody3D old)) old.QueueFree();
        _oriented[key] = Block($"Block {key}", v, color);
    }

    public void Remove(VolumeKey key)
    {
        if (_oriented.Remove(key, out StaticBody3D old)) old.QueueFree();
    }

    /// <summary>The body standing for one of the store's blocks, or null.</summary>
    public StaticBody3D BodyOf(int structureId) => _blocks.TryGetValue(structureId, out StaticBody3D b) ? b : null;

    StaticBody3D Block(string name, StructureVolume v, Color color)
    {
        Vector3 size = new Vector3(v.Half.X, v.Half.Y, v.Half.Z) * 2f;
        var material = Shaders.Make(Shaders.Block);
        material.SetShaderParameter("body_color", color);
        material.SetShaderParameter("trim_color", Palette.BlockTrim);
        material.SetShaderParameter("stripe_color", Palette.BlockStripe);
        material.SetShaderParameter("half_size", size / 2f);

        var basis = new Basis(ToGodot(v.AxisX), ToGodot(v.AxisY), ToGodot(v.AxisZ));
        var body = new StaticBody3D
        {
            Name = name,
            CollisionLayer = Layers.Structure,
            CollisionMask = 0,
            Transform = new Transform3D(basis, ToGodot(v.Center)),
        };
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
        return body;
    }

    /// <summary>Replaces every block with the given set (after a load).</summary>
    public void Rebuild(IEnumerable<Structure> structures)
    {
        foreach (StaticBody3D b in _blocks.Values) b.QueueFree();
        _blocks.Clear();
        foreach (Structure s in structures) Add(s);
    }

    public int Count => _blocks.Count;

    /// <summary>Shows a box as a glowing outline for a moment (a block just placed).</summary>
    public void Flash(CellBox box, Color color)
    {
        var material = Shaders.Make(Shaders.Marker);
        material.SetShaderParameter("color", new Color(color, 0.35f));
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(box.Size.X, box.Size.Y, box.Size.Z) * 1.04f },
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
