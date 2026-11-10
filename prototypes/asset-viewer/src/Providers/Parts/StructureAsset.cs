using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using Godot;
using Slice.Vessels;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// A <see cref="Structure"/> (one block, a room of blocks, a whole vessel) drawn by the slice's own
/// <see cref="StructureMesh"/>: building's chunk mesher and block shaders, the damage effects and the
/// real lights. Sits on the ground disc, with a ground collider so thrown chips land, and carries a
/// small set of named animations driven by the provider (hits, doors, lights, thrusters).
/// </summary>
public partial class StructureAsset : ModelAsset
{
    public Structure Structure { get; }
    public StructureMesh Mesh { get; }
    public bool Powered = true;

    readonly Node3D _holder = new() { Name = "Structure" };
    readonly bool _centre;
    Aabb _bounds;

    public sealed class Anim
    {
        public string Name;
        public Action Start;
        public Action<float> Tick;
        public Action Stop;
    }

    public StructureAsset(Structure structure, bool ground = true, bool centre = true)
    {
        _centre = centre;
        Structure = structure;
        AddChild(_holder);
        Mesh = new StructureMesh { Name = "Mesh", PowerSource = () => Powered };
        _holder.AddChild(Mesh);
        Mesh.Setup(structure);
        structure.DirtyChunks.Clear();
        Place();
        if (ground)
        {
            var body = new StaticBody3D { Name = "Ground", CollisionLayer = (uint)GalHack.Physics.Layers.World, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(400f, 1f, 400f) }, Position = new Vector3(0, -0.5f, 0) });
            AddChild(body);
        }
        Stage = new StageSpec { Ground = true };
        // The first animation is the one the viewer starts on: standing still, as the options say.
        AddAnimation("Still", null, null);
        _initial = structure.Blocks().ToList();
        foreach (var (pos, b) in _initial)
            if (structure.TypeOf(b).IsLight) _lamps[pos] = structure.LightAt(pos);
    }

    List<(Int3 pos, Block block)> _initial = new();
    readonly Dictionary<Int3, LightSettings> _lamps = new();

    /// <summary>Puts every block, flag, light and the power back as the asset was built (each animation starts from there).</summary>
    public void ResetToInitial()
    {
        var have = Structure.Blocks().Select(x => x.pos).ToList();
        var want = new HashSet<Int3>(_initial.Select(x => x.pos));
        foreach (Int3 p in have) if (!want.Contains(p)) Structure.Clear(p);
        foreach (var (pos, block) in _initial)
            if (!Structure.TryGet(pos, out Block cur) || !cur.SameAs(block)) Structure.Set(pos, block);
        foreach (var (pos, light) in _lamps)
            if (Structure.LightAt(pos) != light) Structure.SetLight(pos, light);
        Powered = true;
        Refresh();
    }

    /// <summary>Sits the structure's lowest face on the ground and its middle on the axis.</summary>
    void Place()
    {
        if (Structure.Count == 0) return;
        var (min, max) = Structure.Bounds();
        float cell = Structure.Grid.CellSize;
        var low = new Vector3(min.X - 0.5f, min.Y - 0.5f, min.Z - 0.5f) * cell;
        var high = new Vector3(max.X + 0.5f, max.Y + 0.5f, max.Z + 0.5f) * cell;
        Vector3 shift = _centre ? new(-(low.X + high.X) * 0.5f, -low.Y, -(low.Z + high.Z) * 0.5f) : Vector3.Zero;
        _holder.Position = shift;
        _bounds = new Aabb(low + shift, high - low);
    }

    public override Aabb? FrameBounds => _bounds.Size == Vector3.Zero ? null : _bounds;

    /// <summary>Where a cell's middle is, in this asset's frame.</summary>
    public Vector3 CellCentre(Int3 c) => _holder.Position + new Vector3(c.X, c.Y, c.Z) * Structure.Grid.CellSize;

    public Node3D Holder => _holder;

    /// <summary>Redraws whatever changed (a block's flags, hit points or paint), now.</summary>
    public void Refresh()
    {
        if (Structure.DirtyChunks.Count == 0) return;
        var dirty = Structure.DirtyChunks.ToArray();
        Structure.DirtyChunks.Clear();
        Mesh.Rebuild(dirty);
    }

    protected override void BeforeAnimation() => ResetToInitial();

    public override void _Process(double delta)
    {
        base._Process(delta);
        Refresh();
    }
}
