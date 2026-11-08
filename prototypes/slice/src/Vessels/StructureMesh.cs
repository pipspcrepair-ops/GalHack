using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using Structure = GalHack.Structures.Structure;

namespace Slice.Vessels;

/// <summary>
/// Draws a structure with building's own chunk mesher and materials (paint, patterns, stencils,
/// grime and glass), one mesh per 16³ chunk, rebuilt only for the chunks an edit dirtied, and
/// gives its light blocks real light. Presentation only: it never moves anything.
///
/// Damage redraws by way of <see cref="Queue"/>: chunks wait here and are rebuilt once a frame up
/// to a time budget, so a heavy crash spreads over a frame or two instead of hitching one.
/// </summary>
public partial class StructureMesh : Node3D
{
    const int MaxLights = 8;

    /// <summary>What damage may spend redrawing in one frame, ms. One chunk always goes, so the queue always drains.</summary>
    const double BudgetMs = 5.0;

    public Structure Structure { get; private set; }

    /// <summary>Runs first thing each frame: the vessel applies the frame's hits.</summary>
    public System.Action BeforeDraw;

    /// <summary>Whether the structure has power, asked a few times a second: its devices' lights and screens dim without it. Null: always powered.</summary>
    public System.Func<bool> PowerSource;

    /// <summary>Whether its devices show power now (the shader's per-chunk <c>powered</c>).</summary>
    public bool Powered { get; private set; } = true;

    /// <summary>Sparks, chips, marks and smoke on this structure.</summary>
    public DamageFx Fx { get; private set; }

    /// <summary>Chunks rebuilt since the start, and chunks waiting (tests).</summary>
    public int Rebuilt { get; private set; }
    public int Waiting => _waiting.Count;

    /// <summary>What damage cost in the last frame that had any, ms: applying its hits and redrawing (tests).</summary>
    public double LastFrameMs { get; private set; }

    readonly HashSet<Int3> _waiting = new();
    readonly Dictionary<Int3, MeshInstance3D> _chunks = new();
    readonly ChunkMeshData _scratch = new();
    readonly List<OmniLight3D> _lights = new();
    readonly List<LightSettings> _lightSettings = new();
    string _lightKey = "";
    readonly Slice.Surface.StructureSky _sky = new();
    int _litVersion = -1;
    double _sinceLit;

    /// <param name="deferred">Draw its chunks over the next frames, within the frame's budget (a whole structure set down at once), not all now.</param>
    public void Setup(Structure structure, bool deferred = false)
    {
        Structure = structure;
        foreach (MeshInstance3D m in _chunks.Values) m.QueueFree();
        _chunks.Clear();
        if (Fx == null)
        {
            Fx = new DamageFx { Name = "DamageFx", GroundMask = GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull };
            AddChild(Fx);
        }
        Fx.Setup(structure);
        _sky.Flood(structure);
        _litVersion = structure.Version;
        if (!deferred) Rebuild(structure.Chunks.Keys);
        else
        {
            _waiting.UnionWith(structure.Chunks.Keys);
            UpdateLights();
        }
    }

    /// <summary>Chunks to redraw soon: their blocks wore away or healed, and nothing else about them changed.</summary>
    public void Queue(IEnumerable<Int3> chunks) => _waiting.UnionWith(chunks);

    public override void _Process(double delta)
    {
        AnimateLights();
        long start = Stopwatch.GetTimestamp();
        int rebuilt = Rebuilt, queued = _waiting.Count;
        BeforeDraw?.Invoke();
        // Blocks shot away let daylight in: redone a few times a second, not once a hit.
        if ((_sinceLit += delta) > 0.25)
        {
            Relight();
            if (PowerSource != null && PowerSource() != Powered) SetPowered(!Powered);
        }
        if (_waiting.Count > 0) DrainQueue();
        if (Rebuilt != rebuilt || queued > 0) LastFrameMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>Redraws waiting chunks, until the frame's budget is spent.</summary>
    public void DrainQueue(double budgetMs = BudgetMs)
    {
        long start = Stopwatch.GetTimestamp();
        int done = 0;
        var chunks = new List<Int3>(_waiting);
        foreach (Int3 c in chunks)
        {
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (done > 0 && ms > budgetMs) break;
            _waiting.Remove(c);
            Rebuild(c, colliders: false);
            done++;
        }
    }

    /// <summary>Its devices' lights and screens dim or light up together: one value per chunk, nothing rebuilt.</summary>
    void SetPowered(bool on)
    {
        Powered = on;
        foreach (MeshInstance3D m in _chunks.Values) m.SetInstanceShaderParameter("powered", on ? 1f : 0f);
    }

    /// <summary>If the structure changed, floods it again and queues the chunks whose light moved.</summary>
    void Relight()
    {
        _sinceLit = 0;
        if (Structure == null || Structure.Version == _litVersion) return;
        _litVersion = Structure.Version;
        _waiting.UnionWith(_sky.Flood(Structure));
    }

    public void Rebuild(IEnumerable<Int3> chunks)
    {
        Relight();
        foreach (Int3 c in chunks)
        {
            _waiting.Remove(c);
            Rebuild(c, colliders: false);
        }
        UpdateLights();
    }

    void Rebuild(Int3 chunk, bool colliders)
    {
        Rebuilt++;
        if (!Structure.Chunks.ContainsKey(chunk))
        {
            if (_chunks.Remove(chunk, out MeshInstance3D gone)) gone.QueueFree();
            _scratch.Clear();
            Fx?.Smoke(chunk, _scratch.Smoking);
            return;
        }
        if (!_chunks.TryGetValue(chunk, out MeshInstance3D mesh))
        {
            mesh = new MeshInstance3D { Name = $"Chunk{chunk.X}_{chunk.Y}_{chunk.Z}" };
            AddChild(mesh);
            if (!Powered) mesh.SetInstanceShaderParameter("powered", 0f);
            _chunks[chunk] = mesh;
        }
        ChunkMesher.Build(Structure, chunk, _scratch, colliders);
        _sky.Bake(_scratch.Opaque);
        mesh.Mesh = ChunkMesher.ToMesh(_scratch);
        Fx?.Smoke(chunk, _scratch.Smoking);
    }

    /// <summary>The real lights follow their glow: the same intensity and colour the shader draws, from the same clock.</summary>
    void AnimateLights()
    {
        if (_lights.Count == 0) return;
        double now = BlockMaterials.LampClock;
        for (int i = 0; i < _lights.Count && i < _lightSettings.Count; i++)
        {
            LightSettings s = _lightSettings[i];
            if (s.Anim == LightAnim.Steady) continue;
            _lights[i].LightEnergy = 1.4f * s.Intensity(now);
            _lights[i].LightColor = ChunkMesher.Rgb(s.ColorAt(now));
        }
    }

    void UpdateLights()
    {
        var lights = new List<(Int3 pos, Block block)>();
        foreach (var (p, b) in Structure.Blocks())
            if (Structure.TypeOf(b).IsLight && !b.Off && lights.Count < MaxLights) lights.Add((p, b));
        string key = string.Join(";", lights.ConvertAll(l => $"{l.pos}/{l.block.Rot}/{Structure.LightAt(l.pos).ToText()}"));
        if (key == _lightKey) return;
        _lightKey = key;
        foreach (OmniLight3D l in _lights) l.QueueFree();
        _lights.Clear();
        _lightSettings.Clear();
        float cell = Structure.Grid.CellSize;
        foreach (var (pos, block) in lights)
        {
            System.Numerics.Vector3 out_ = Orientations.Matrix(block.Rot).Apply(new System.Numerics.Vector3(0, 0, -0.6f));
            var light = new OmniLight3D
            {
                Position = (new Vector3(pos.X, pos.Y, pos.Z) + new Vector3(out_.X, out_.Y, out_.Z)) * cell,
                LightColor = ChunkMesher.Rgb(Structure.LightAt(pos).Color),
                LightEnergy = 1.4f,
                OmniRange = 4f,
                OmniAttenuation = 1.2f,
            };
            AddChild(light);
            _lights.Add(light);
            _lightSettings.Add(Structure.LightAt(pos));
        }
    }
}
