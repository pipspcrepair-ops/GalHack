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
    /// <summary>
    /// Flora's drawings, by the layer each batch is (flora.gdshader's flora_layer): 0 large growth's
    /// near drawing, 1 its far drawing, 3 the distant drawing of a share of it, 4 small growth.
    /// </summary>
    public const int NearLayer = 0, FarLayer = 1, DistantLayer = 3, SmallLayer = 4;

    /// <summary>How far from the eye a layer's drawings can show at all, m (each plant's own reach is up to a tenth less).</summary>
    public static (float From, float To) FloraLayerReach(int layer) => layer switch
    {
        NearLayer => (0f, DetailReach.Near),
        SmallLayer => (0f, DetailReach.Small),
        FarLayer => (DetailReach.Near * 0.9f - DetailReach.NearBand, MathF.Max(DetailReach.Far, DetailReach.Thin)),
        _ => (DetailReach.Far * 0.9f - DetailReach.FarBand, DetailReach.Distant),
    };

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
            int layer = b.Layer, mesh = b.Plant * 64 + b.Variant + 1 + (layer == DistantLayer ? 1 << 20 : 0);
            for (int i = 0; i < b.Count; i++)
            {
                int o = i * 16;
                float lx = b.Buffer[o + 3], lz = b.Buffer[o + 11];
                float away = new Vector2(x0 + lx - eye.X, z0 + lz - eye.Z).Length();
                float shows = FloraShows(layer, b.Buffer[o + 15] >= 1.5f, away, x0 + lx, z0 + lz);
                if (shows <= 0f) continue;
                long id = FarBuilder.ScatterId(fx, fz, (int)(lx / FarBuilder.ScatterCell), (int)(lz / FarBuilder.ScatterCell));
                into[(id, mesh)] = (shows, height * b.Buffer[o + 5], away);
            }
        }
    }

    /// <summary>Every plant drawn and how much of it shows from an eye (frame coordinates), as the shader has it, by layer: tests measure flora's reach with it.</summary>
    public IEnumerable<(float Away, int Layer, float Shows)> FloraShown(Vector3 eye)
    {
        double w = World.Terrain.Width, h = World.Terrain.Height;
        foreach (FarChunk f in _far.Values)
        {
            if (f.Node == null || f.Flora == null) continue;
            float x0 = (float)NearestCopy(f.Fx * FarBuilder.Size, FarBuilder.Size, eye.X, w), z0 = (float)NearestCopy(f.Fz * FarBuilder.Size, FarBuilder.Size, eye.Z, h);
            foreach (FloraBatch b in f.Flora)
            {
                int layer = b.Layer;
                for (int i = 0; i < b.Count; i++)
                {
                    int o = i * 16;
                    float x = x0 + b.Buffer[o + 3], z = z0 + b.Buffer[o + 11];
                    float away = new Vector2(x - eye.X, z - eye.Z).Length();
                    yield return (away, layer, FloraShows(layer, b.Buffer[o + 15] >= 1.5f, away, x, z));
                }
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
    /// flora.gdshader has it: the near drawing its share, the far drawing the rest of what shows, and
    /// the distant drawing (a share of the large growth) what shows past the far drawing's reach.
    /// Small growth has its near drawing only; its thinning share goes at the large's near reach.
    /// </summary>
    public static float FloraShows(int layer, bool thin, float away, float x, float z)
    {
        float s = MathF.Sin(x * 12.9898f + z * 78.233f) * 43758.5453f;
        float r = s - MathF.Floor(s), own = 0.9f + 0.1f * r;
        float near = Shown(DetailReach.Near * own, DetailReach.NearBand, away);
        if (layer == NearLayer) return near;
        if (layer == SmallLayer) return thin ? near : Shown(DetailReach.Small * own, DetailReach.SmallBand, away);
        if (layer == FarLayer)
        {
            float most = thin ? Shown(DetailReach.Thin * own, DetailReach.ThinBand, away) : Shown(DetailReach.Far * own, DetailReach.FarBand, away);
            return MathF.Max(0f, most - near);
        }
        float far = Shown(DetailReach.Far * own, DetailReach.FarBand, away);
        return MathF.Max(0f, Shown(DetailReach.Distant * (0.95f + 0.05f * r), DetailReach.DistantBand, away) - far);
    }

    /// <summary>
    /// How much of the large growth's cover shows at a distance, all its drawings together, as
    /// flora.gdshader has it, for a plant of the thinning share or not: 1 near, then the distant
    /// drawings' share of it (a share of the plants, each a little bigger). It should only fall off in
    /// bands, never in a step.
    /// </summary>
    public static float LargeCover(bool thin, float away, float x, float z) =>
        FloraShows(NearLayer, thin, away, x, z) + FloraShows(FarLayer, thin, away, x, z)
        + FloraShows(DistantLayer, thin, away, x, z) * DetailReach.DistantShare * DetailReach.DistantGrow * DetailReach.DistantGrow;
}
