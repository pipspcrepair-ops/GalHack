using System;
using System.Collections.Generic;
using GalHack.Terrain;
using Godot;
using TerrainDigging.Terrain;
using PlanetWorld = PlanetSurface.World.PlanetWorld;

namespace CapitalBase.World;

/// <summary>
/// Ground crumbling away: every cell a lift-off cuts sheds a clod of its own colour that tumbles
/// down and fades. One multimesh, so a big cut costs one draw call. Presentation only: the cells
/// themselves are already gone from the terrain.
/// </summary>
public partial class Debris : Node3D
{
    const int MaxClods = 1500;

    sealed class Clod
    {
        public Vector3 Position, Velocity, Spin;
        public Basis Turn = Basis.Identity;
        public float Size, Age, Life;
        public Color Color;
    }

    readonly List<Clod> _clods = new();
    MultiMeshInstance3D _instance;
    MultiMesh _mesh;

    public int Live => _clods.Count;

    public override void _Ready()
    {
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One },
            InstanceCount = MaxClods,
            VisibleInstanceCount = 0,
        };
        _instance = new MultiMeshInstance3D
        {
            Multimesh = _mesh,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.95f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(_instance);
    }

    /// <summary>Sheds a clod from each removed cell (a deterministic spread, so screenshots repeat).</summary>
    public void Crumble(IReadOnlyList<RemovedCell> cells, PlanetWorld planet, float seconds)
    {
        if (!Visible) return;
        // Two clods a cell for a small cut, fewer for a big one, never more than the multimesh holds.
        float per = Math.Clamp((MaxClods - _clods.Count) / (float)Math.Max(1, cells.Count), 0f, 2f);
        float owed = 0f;
        foreach (RemovedCell r in cells)
        {
            owed += per;
            uint h = (uint)(r.Cell.X * 73856093 ^ r.Cell.Y * 19349663 ^ r.Cell.Z * 83492791);
            Color c = planet.Palette.Vertex(r.Material, r.Cell.X, r.Cell.Z, r.Cell.Y, out _);
            for (int n = 0; owed >= 1f && _clods.Count < MaxClods; n++, owed -= 1f)
            {
                uint seed = h + (uint)n * 2246822519u;
                float Rand(int k) => ((seed * (uint)(2654435761 + k * 40503)) >> 8) / 16777216f;
                _clods.Add(new Clod
                {
                    Position = new Vector3(r.Cell.X + Rand(1), r.Cell.Y + Rand(2), r.Cell.Z + Rand(3)),
                    Velocity = new Vector3((Rand(4) - 0.5f) * 2.4f, 0.6f + Rand(5) * 3.2f, (Rand(6) - 0.5f) * 2.4f),
                    Spin = new Vector3(Rand(7) - 0.5f, Rand(8) - 0.5f, Rand(9) - 0.5f) * 8f,
                    Size = 0.3f + 0.5f * Rand(10),
                    Life = seconds * (0.6f + 0.4f * Rand(11)),
                    Color = c,
                });
            }
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        int n = 0;
        for (int i = _clods.Count - 1; i >= 0; i--)
        {
            Clod c = _clods[i];
            c.Age += dt;
            if (c.Age >= c.Life)
            {
                _clods.RemoveAt(i);
                continue;
            }
            c.Velocity += Vector3.Down * 9.8f * dt;
            c.Position += c.Velocity * dt;
            if (c.Spin.LengthSquared() > 1e-6f) c.Turn = c.Turn.Rotated(c.Spin.Normalized(), c.Spin.Length() * dt).Orthonormalized();
        }
        foreach (Clod c in _clods)
        {
            float fade = 1f - Mathf.Clamp((c.Age - c.Life * 0.7f) / (c.Life * 0.3f), 0f, 1f);
            _mesh.SetInstanceTransform(n, new Transform3D(c.Turn.Scaled(Vector3.One * c.Size * fade), c.Position));
            _mesh.SetInstanceColor(n, c.Color);
            n++;
        }
        _mesh.VisibleInstanceCount = n;
    }
}
