using System;
using System.Collections.Generic;
using Godot;
using TerrainDigging.Terrain;

namespace TerrainDigging;

/// <summary>
/// Debug overlay (B): the chunk grid around the player, and a yellow flash on every chunk
/// whose mesh was just rebuilt, so you can watch a dig touch only the chunks it should.
/// </summary>
public partial class ChunkBorders : MeshInstance3D
{
    const int Reach = 2;
    const float FlashTime = 0.6f;

    public Func<Vector3> Center;

    readonly Dictionary<Int3, float> _flashes = new();
    ImmediateMesh _lines;
    StandardMaterial3D _gridMaterial, _flashMaterial;

    public override void _Ready()
    {
        _lines = new ImmediateMesh();
        Mesh = _lines;
        CastShadow = ShadowCastingSetting.Off;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _gridMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _flashMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
        };
        Visible = false;
    }

    public void Flash(Int3 chunk)
    {
        if (Visible) _flashes[chunk] = FlashTime;
    }

    public override void _Process(double delta)
    {
        _lines.ClearSurfaces();
        if (!Visible || Center == null)
        {
            _flashes.Clear();
            return;
        }

        const int S = ChunkStore.Size;
        Vector3 at = Center();
        var c = new Int3(Mathf.FloorToInt(at.X / S), Mathf.FloorToInt(at.Y / S), Mathf.FloorToInt(at.Z / S));

        _lines.SurfaceBegin(Mesh.PrimitiveType.Lines, _gridMaterial);
        var grid = new Color(0.35f, 0.95f, 1f, 0.55f);
        float y0 = (c.Y - 1) * S, y1 = (c.Y + 2) * S;
        for (int x = c.X - Reach; x <= c.X + Reach + 1; x++)
            for (int z = c.Z - Reach; z <= c.Z + Reach + 1; z++)
                Line(new Vector3(x * S, y0, z * S), new Vector3(x * S, y1, z * S), grid);
        for (int y = c.Y - 1; y <= c.Y + 2; y++)
            for (int i = c.X - Reach; i <= c.X + Reach + 1; i++)
            {
                Line(new Vector3(i * S, y * S, (c.Z - Reach) * S), new Vector3(i * S, y * S, (c.Z + Reach + 1) * S), grid);
                int j = i - c.X + c.Z;
                Line(new Vector3((c.X - Reach) * S, y * S, j * S), new Vector3((c.X + Reach + 1) * S, y * S, j * S), grid);
            }
        _lines.SurfaceEnd();

        foreach (Int3 chunk in new List<Int3>(_flashes.Keys))
        {
            _flashes[chunk] -= (float)delta;
            if (_flashes[chunk] <= 0f) _flashes.Remove(chunk);
        }
        if (_flashes.Count == 0) return;
        _lines.SurfaceBegin(Mesh.PrimitiveType.Lines, _flashMaterial);
        foreach ((Int3 chunk, float left) in _flashes)
            Box(chunk, new Color(1f, 0.85f, 0.2f, left / FlashTime));
        _lines.SurfaceEnd();
    }

    void Box(Int3 chunk, Color color)
    {
        const int S = ChunkStore.Size;
        const float inset = 0.06f;
        Vector3 lo = new Vector3(chunk.X, chunk.Y, chunk.Z) * S + Vector3.One * inset;
        Vector3 hi = lo + Vector3.One * (S - 2 * inset);
        for (int i = 0; i < 4; i++)
        {
            float x = (i & 1) == 0 ? lo.X : hi.X, z = (i & 2) == 0 ? lo.Z : hi.Z;
            Line(new Vector3(x, lo.Y, z), new Vector3(x, hi.Y, z), color);
            float y = (i & 1) == 0 ? lo.Y : hi.Y;
            float zz = (i & 2) == 0 ? lo.Z : hi.Z;
            Line(new Vector3(lo.X, y, zz), new Vector3(hi.X, y, zz), color);
            float xx = (i & 2) == 0 ? lo.X : hi.X;
            Line(new Vector3(xx, y, lo.Z), new Vector3(xx, y, hi.Z), color);
        }
    }

    void Line(Vector3 a, Vector3 b, Color color)
    {
        _lines.SurfaceSetColor(color);
        _lines.SurfaceAddVertex(a);
        _lines.SurfaceSetColor(color);
        _lines.SurfaceAddVertex(b);
    }
}
