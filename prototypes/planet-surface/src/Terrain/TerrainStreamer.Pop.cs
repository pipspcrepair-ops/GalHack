using System;
using System.Collections.Generic;
using Godot;
using PlanetSurface.Fx;

namespace PlanetSurface.Terrain;

/// <summary>
/// What a far chunk's new drawing changes on screen, for the pop-in log (<see cref="PopLog"/>): each
/// plant's drawings before and after (by its scatter cell and mesh), how much of each shows at the
/// distance it is (<see cref="FloraShows"/>, as the shader has it). Only while the log is on.
/// </summary>
public partial class TerrainStreamer
{
    /// <summary>Flora's reach, m (flora.gdshader's): small growth and the near drawings to the first, large growth's far drawings to the second.</summary>
    public const float FloraNearReach = 120f, FloraNearBand = 26f, FloraFarReach = 370f, FloraFarBand = 60f, FloraThinReach = 200f, FloraThinBand = 40f;

    readonly Dictionary<(int, int), float> _meshHeight = new();

    float MeshHeight(FloraBatch b)
    {
        if (_meshHeight.TryGetValue((b.Plant, b.Variant), out float h)) return h;
        Mesh m = PlantMesh(b.Plant, b.Variant) ?? _floraMeshes[b.Kind];
        return _meshHeight[(b.Plant, b.Variant)] = m.GetAabb().End.Y;
    }

    Vector3 Eye() => GetViewport()?.GetCamera3D()?.GlobalPosition ?? Focus;

    readonly Dictionary<(long, int), (float Shows, float Size, float Away)> _popBefore = new(), _popAfter = new();

    void Gather(Dictionary<(long, int), (float, float, float)> into, List<FloraBatch> flora, int fx, int fz, Vector3 eye)
    {
        into.Clear();
        if (flora == null) return;
        double w = World.Terrain.Width, h = World.Terrain.Height;
        float x0 = (float)NearestCopy(fx * FarBuilder.Size, FarBuilder.Size, eye.X, w), z0 = (float)NearestCopy(fz * FarBuilder.Size, FarBuilder.Size, eye.Z, h);
        foreach (FloraBatch b in flora)
        {
            float height = MeshHeight(b);
            int mesh = b.Plant * 64 + b.Variant + 1;
            for (int i = 0; i < b.Count; i++)
            {
                int o = i * 16;
                float lx = b.Buffer[o + 3], lz = b.Buffer[o + 11];
                float away = new Vector2(x0 + lx - eye.X, z0 + lz - eye.Z).Length();
                float shows = FloraShows(b.Far, b.Buffer[o + 15] >= 1.5f, away, x0 + lx, z0 + lz);
                if (shows <= 0f) continue;
                long id = FarBuilder.ScatterId(fx, fz, (int)(lx / FarBuilder.ScatterCell), (int)(lz / FarBuilder.ScatterCell));
                into[(id, mesh)] = (shows, height * b.Buffer[o + 5], away);
            }
        }
    }

    /// <summary>A far chunk's drawing is about to be replaced: what the eye sees change in it.</summary>
    void NotePops(FarChunk f, FarJob job)
    {
        if (!PopLog.On) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        Vector3 eye = Eye();
        Gather(_popBefore, f.Node != null ? f.Flora : null, f.Fx, f.Fz, eye);
        Gather(_popAfter, job.Flora, job.Fx, job.Fz, eye);
        // The new drawing dissolves in over the old (BeginSwap): a frame's share of each change shows at once.
        float step = 1f / (60f * FloraSwapSeconds);
        foreach (var (key, a) in _popAfter)
        {
            if (!_popBefore.TryGetValue(key, out var b)) PopLog.Note("flora", "appears", a.Away, a.Size, a.Shows * step);
            else PopLog.Note("flora", "shows more or less", a.Away, a.Size, MathF.Abs(a.Shows - b.Shows) * step);
        }
        foreach (var (key, b) in _popBefore)
            if (!_popAfter.ContainsKey(key)) PopLog.Note("flora", "goes", b.Away, b.Size, b.Shows * step);
        PopMs += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>This frame's time spent on the pop-in log, ms: left out of streaming's own.</summary>
    double PopMs;

    /// <summary>A far chunk goes from the drawing altogether (out of range).</summary>
    void NoteGone(FarChunk f)
    {
        if (!PopLog.On || f.Node == null) return;
        Gather(_popBefore, f.Flora, f.Fx, f.Fz, Eye());
        foreach (var (_, b) in _popBefore) PopLog.Note("flora", "goes", b.Away, b.Size, b.Shows);
    }

    static float Shown(float reach, float width, float away) => 1f - Mathf.SmoothStep(reach - width, reach, away);

    /// <summary>
    /// How much of one drawing of a scattered plant shows at a distance (m, flat, from the eye), as
    /// flora.gdshader has it: the near drawing its share, the far drawing the rest of what shows.
    /// </summary>
    public static float FloraShows(bool far, bool thin, float away, float x, float z)
    {
        float s = MathF.Sin(x * 12.9898f + z * 78.233f) * 43758.5453f;
        float own = 0.9f + 0.1f * (s - MathF.Floor(s));
        float near = Shown(FloraNearReach * own, FloraNearBand, away);
        if (!far) return near;
        float most = thin ? Shown(FloraThinReach * own, FloraThinBand, away) : Shown(FloraFarReach * own, FloraFarBand, away);
        return MathF.Max(0f, most - near);
    }
}
