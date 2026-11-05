using System.Collections.Generic;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using Structure = GalHack.Structures.Structure;

namespace Slice.Vessels;

/// <summary>
/// Draws a structure with building's own chunk mesher and materials (paint, patterns, stencils,
/// grime and glass), one mesh per 16³ chunk, rebuilt only for the chunks an edit dirtied, and
/// gives its light blocks real light. Presentation only: it never moves anything.
/// </summary>
public partial class StructureMesh : Node3D
{
    const int MaxLights = 8;

    public Structure Structure { get; private set; }
    readonly Dictionary<Int3, MeshInstance3D> _chunks = new();
    readonly ChunkMeshData _scratch = new();
    readonly List<OmniLight3D> _lights = new();
    string _lightKey = "";

    public void Setup(Structure structure)
    {
        Structure = structure;
        foreach (MeshInstance3D m in _chunks.Values) m.QueueFree();
        _chunks.Clear();
        Rebuild(structure.Chunks.Keys);
    }

    public void Rebuild(IEnumerable<Int3> chunks)
    {
        foreach (Int3 c in chunks) Rebuild(c);
        UpdateLights();
    }

    void Rebuild(Int3 chunk)
    {
        if (!Structure.Chunks.ContainsKey(chunk))
        {
            if (_chunks.Remove(chunk, out MeshInstance3D gone)) gone.QueueFree();
            return;
        }
        if (!_chunks.TryGetValue(chunk, out MeshInstance3D mesh))
        {
            mesh = new MeshInstance3D { Name = $"Chunk{chunk.X}_{chunk.Y}_{chunk.Z}" };
            AddChild(mesh);
            _chunks[chunk] = mesh;
        }
        ChunkMesher.Build(Structure, chunk, _scratch);
        mesh.Mesh = ChunkMesher.ToMesh(_scratch);
    }

    void UpdateLights()
    {
        var lights = new List<(Int3 pos, Block block)>();
        foreach (var (p, b) in Structure.Blocks())
            if (Structure.TypeOf(b).IsLight && !b.Off && lights.Count < MaxLights) lights.Add((p, b));
        string key = string.Join(";", lights.ConvertAll(l => $"{l.pos}/{l.block.Rot}"));
        if (key == _lightKey) return;
        _lightKey = key;
        foreach (OmniLight3D l in _lights) l.QueueFree();
        _lights.Clear();
        float cell = Structure.Grid.CellSize;
        foreach (var (pos, block) in lights)
        {
            System.Numerics.Vector3 out_ = Orientations.Matrix(block.Rot).Apply(new System.Numerics.Vector3(0, 0, -0.6f));
            var light = new OmniLight3D
            {
                Position = (new Vector3(pos.X, pos.Y, pos.Z) + new Vector3(out_.X, out_.Y, out_.Z)) * cell,
                LightColor = ChunkMesher.Rgb(Structure.TypeOf(block).GlowColor),
                LightEnergy = 1.4f,
                OmniRange = 4f,
                OmniAttenuation = 1.2f,
            };
            AddChild(light);
            _lights.Add(light);
        }
    }
}
