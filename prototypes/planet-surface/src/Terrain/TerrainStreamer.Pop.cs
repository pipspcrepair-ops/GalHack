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
    /// <summary>Flora's drawings, by the layer each batch is (flora.gdshader's flora_layer): large growth and small growth.</summary>
    public const int LargeLayer = 0, SmallLayer = 1;

    /// <summary>How far from the eye a layer's drawings can show at all, m (each plant's own reach is a little less).</summary>
    public static float FloraLayerReach(int layer) => layer == SmallLayer ? DetailReach.Small : DetailReach.Large;

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
            int layer = b.Layer, mesh = b.Plant * 256 + b.Variant + 128;
            for (int i = 0; i < b.Count; i++)
            {
                int o = i * 16;
                float lx = b.Buffer[o + 3], lz = b.Buffer[o + 11];
                float away = eye.DistanceTo(new Vector3(x0 + lx, b.Buffer[o + 7], z0 + lz));
                float shows = FloraShows(layer, b.Buffer[o + 15] >= 1.5f, away, x0 + lx, z0 + lz);
                if (shows <= 0f) continue;
                long id = FarBuilder.ScatterId(fx, fz, (int)(lx / FarBuilder.ScatterCell), (int)(lz / FarBuilder.ScatterCell));
                into[(id, mesh)] = (shows, height * b.Buffer[o + 5], away);
            }
        }
    }

    /// <summary>
    /// Every plant drawn and how much of it shows from an eye (frame coordinates), as the shader has it, by layer, with
    /// where it roots, its height and its scatter cell (one plant's drawings share it): tests measure flora's reach and its pops with it.
    /// </summary>
    public IEnumerable<(float Away, int Layer, float Shows, Vector3 At, float Size, long Id)> FloraShown(Vector3 eye)
    {
        double w = World.Terrain.Width, h = World.Terrain.Height;
        foreach (FarChunk f in _far.Values)
        {
            if (f.Node == null || f.Flora == null) continue;
            float x0 = (float)NearestCopy(f.Fx * FarBuilder.Size, FarBuilder.Size, eye.X, w), z0 = (float)NearestCopy(f.Fz * FarBuilder.Size, FarBuilder.Size, eye.Z, h);
            foreach (FloraBatch b in f.Flora)
            {
                int layer = b.Layer;
                float height = MeshHeight(b);
                for (int i = 0; i < b.Count; i++)
                {
                    int o = i * 16;
                    float lx = b.Buffer[o + 3], lz = b.Buffer[o + 11], x = x0 + lx, z = z0 + lz;
                    var root = new Vector3(x, b.Buffer[o + 7], z);
                    float away = eye.DistanceTo(root);
                    long id = FarBuilder.ScatterId(f.Fx, f.Fz, (int)(lx / FarBuilder.ScatterCell), (int)(lz / FarBuilder.ScatterCell));
                    yield return (away, layer, FloraShows(layer, b.Buffer[o + 15] >= 1.5f, away, x, z), root, height * b.Buffer[o + 5], id);
                }
            }
        }
    }

    /// <summary>
    /// Plants that show by their own reach (as the shader has it) in batches the renderer leaves out by distance (its
    /// visibility range, measured from the eye to the batch's box centre): what vanishes a whole chunk's batch at a time. Tests count it.
    /// </summary>
    public int FloraCulledByRange(Vector3 eye)
    {
        int culled = 0;
        double w = World.Terrain.Width, h = World.Terrain.Height;
        foreach (FarChunk f in _far.Values)
        {
            if (f.Node == null || f.Flora == null) continue;
            float x0 = (float)NearestCopy(f.Fx * FarBuilder.Size, FarBuilder.Size, eye.X, w), z0 = (float)NearestCopy(f.Fz * FarBuilder.Size, FarBuilder.Size, eye.Z, h);
            int k = 0;
            foreach (Node n in f.Node.GetChildren())
            {
                if (n is not MultiMeshInstance3D m) continue;
                FloraBatch b = f.Flora[k++];
                float d = (m.GlobalTransform * m.Multimesh.GetAabb().GetCenter()).DistanceTo(eye);
                if (d >= m.VisibilityRangeBegin && (m.VisibilityRangeEnd <= 0f || d <= m.VisibilityRangeEnd)) continue;
                for (int i = 0; i < b.Count; i++)
                {
                    int o = i * 16;
                    float x = x0 + b.Buffer[o + 3], z = z0 + b.Buffer[o + 11];
                    if (FloraShows(b.Layer, b.Buffer[o + 15] >= 1.5f, eye.DistanceTo(new Vector3(x, b.Buffer[o + 7], z)), x, z) > 0.05f) culled++;
                }
            }
        }
        return culled;
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
    /// How much of a scattered plant shows at a distance from the eye (m, to its root), as flora.gdshader has it: a
    /// large plant the share of its pixels it keeps as it dissolves at the end of its reach, small growth the size it
    /// keeps as it shrinks into the ground (its thinning share at three fifths of the reach).
    /// </summary>
    public static float FloraShows(int layer, bool thin, float away, float x, float z)
    {
        float s = MathF.Sin(x * 12.9898f + z * 78.233f) * 43758.5453f;
        float r = s - MathF.Floor(s), k = thin ? 0.6f : 1f;
        return layer == SmallLayer
            ? Shown(DetailReach.Small * k * (0.7f + 0.3f * r), DetailReach.SmallBand * k, away)
            : Shown(DetailReach.Large * (0.95f + 0.05f * r), DetailReach.LargeBand, away);
    }
}
