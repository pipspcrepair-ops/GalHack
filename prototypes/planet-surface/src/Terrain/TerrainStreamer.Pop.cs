using System;
using System.Collections.Generic;
using Godot;
using PlanetSurface.Fx;

namespace PlanetSurface.Terrain;

/// <summary>
/// What a far chunk's new drawing changes on screen, for the pop-in log (<see cref="PopLog"/>): each
/// plant drawn before and after, by its scatter cell, how much of it shows at the distance it is
/// (<see cref="FloraShows"/>, as the drawing has it), and whether its mesh changed. Only while the log is on.
/// </summary>
public partial class TerrainStreamer
{
    readonly Dictionary<(int, int), float> _meshHeight = new();

    float MeshHeight(FloraBatch b)
    {
        if (_meshHeight.TryGetValue((b.Plant, b.Variant), out float h)) return h;
        Mesh m = PlantMesh(b.Plant, b.Variant) ?? _floraMeshes[b.Kind];
        return _meshHeight[(b.Plant, b.Variant)] = m.GetAabb().End.Y;
    }

    Vector3 Eye() => GetViewport()?.GetCamera3D()?.GlobalPosition ?? Focus;

    readonly Dictionary<long, (float Shows, int Mesh, float Size, float Away)> _popBefore = new(), _popAfter = new();

    void Gather(Dictionary<long, (float, int, float, float)> into, List<FloraBatch> flora, int lod, int fx, int fz, Vector3 eye)
    {
        into.Clear();
        if (flora == null) return;
        double w = World.Terrain.Width, h = World.Terrain.Height;
        float x0 = (float)NearestCopy(fx * FarBuilder.Size, FarBuilder.Size, eye.X, w), z0 = (float)NearestCopy(fz * FarBuilder.Size, FarBuilder.Size, eye.Z, h);
        foreach (FloraBatch b in flora)
        {
            float height = MeshHeight(b);
            for (int i = 0; i < b.Count; i++)
            {
                int o = i * 16;
                float lx = b.Buffer[o + 3], lz = b.Buffer[o + 11];
                float away = new Vector2(x0 + lx - eye.X, z0 + lz - eye.Z).Length();
                float shows = FloraShows(lod, b, away, x0 + lx, z0 + lz);
                if (shows <= 0f) continue;
                long id = FarBuilder.ScatterId(fx, fz, (int)(lx / FarBuilder.ScatterCell), (int)(lz / FarBuilder.ScatterCell));
                int mesh = b.Plant * 64 + b.Variant + 1;
                float size = height * b.Buffer[o + 5];
                // Two in one cell (never, but a tree through a ruin could share): the more shown counts.
                if (into.TryGetValue(id, out var had) && had.Item1 >= shows) continue;
                into[id] = (shows, mesh, size, away);
            }
        }
    }

    /// <summary>A far chunk's drawing is about to be replaced: what the eye sees change in it.</summary>
    void NotePops(FarChunk f, FarJob job)
    {
        if (!PopLog.On) return;
        Vector3 eye = Eye();
        Gather(_popBefore, f.Node != null ? f.Flora : null, f.Lod, f.Fx, f.Fz, eye);
        Gather(_popAfter, job.Flora, job.Lod, job.Fx, job.Fz, eye);
        foreach (var (id, a) in _popAfter)
        {
            if (!_popBefore.TryGetValue(id, out var b)) PopLog.Note("flora", "appears", a.Away, a.Size, a.Shows);
            else if (b.Mesh != a.Mesh) PopLog.Note("flora", "mesh swap", a.Away, a.Size, MathF.Max(a.Shows, b.Shows));
            else PopLog.Note("flora", "fade step", a.Away, a.Size, MathF.Abs(a.Shows - b.Shows));
        }
        foreach (var (id, b) in _popBefore)
            if (!_popAfter.ContainsKey(id)) PopLog.Note("flora", "goes", b.Away, b.Size, b.Shows);
    }

    /// <summary>A far chunk goes from the drawing altogether (out of range).</summary>
    void NoteGone(FarChunk f)
    {
        if (!PopLog.On || f.Node == null) return;
        Gather(_popBefore, f.Flora, f.Lod, f.Fx, f.Fz, Eye());
        foreach (var (_, b) in _popBefore) PopLog.Note("flora", "goes", b.Away, b.Size, b.Shows);
    }

    /// <summary>
    /// How much of one scattered plant shows at a distance (m, flat, from the eye): as the drawing
    /// has it. Each level's multimesh draws whole out to its visibility range.
    /// </summary>
    static float FloraShows(int lod, FloraBatch b, float away, float x, float z) => away < (lod == 0 ? 520f : 760f) ? 1f : 0f;
}
