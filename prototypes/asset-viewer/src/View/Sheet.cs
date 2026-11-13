using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AssetViewer.Providers;
using Godot;

namespace AssetViewer;

/// <summary>Where the tiles of a filmstrip go: columns are phases of the animation, rows are camera views. Pure arithmetic.</summary>
public sealed class SheetLayout
{
    public const int Header = 28, Gap = 4, MaxWidth = 2000;

    public readonly int Phases, Views, TileW, TileH;

    SheetLayout(int phases, int views, int w, int h) { Phases = phases; Views = views; TileW = w; TileH = h; }

    public int Width => Phases * (TileW + Gap) + Gap;
    public int Height => Header + Views * (TileH + Gap) + Gap;

    /// <summary>The tile size is shrunk, keeping its shape, until the whole sheet is no wider than <see cref="MaxWidth"/>, so a viewer never downscales it.</summary>
    public static SheetLayout Make(int phases, int views, int tileW, int tileH)
    {
        phases = Math.Max(1, phases);
        views = Math.Max(1, views);
        tileW = Math.Max(48, tileW);
        tileH = Math.Max(48, tileH);
        int room = MaxWidth - Gap * (phases + 1);
        if (phases * tileW > room)
        {
            float k = room / (float)(phases * tileW);
            tileW = Math.Max(16, (int)(tileW * k));
            tileH = Math.Max(16, (int)(tileH * k));
        }
        return new SheetLayout(phases, views, tileW, tileH);
    }

    /// <summary>Top-left pixel of the tile for a phase (column) in a view (row).</summary>
    public Vector2I Origin(int phase, int view) => new(Gap + phase * (TileW + Gap), Header + Gap + view * (TileH + Gap));

    /// <summary>
    /// The tick, counted from the start of the sheet's cycle, at which each phase is taken. A loop is one period, so the
    /// phases are evenly spaced and the next period's start is left out. A one-shot spans its whole length, first tick to last.
    /// </summary>
    public static int[] PhaseTicks(float cycleSeconds, bool loops, int phases, int ticksPerSecond = 60)
    {
        int total = Math.Max(1, (int)MathF.Round(cycleSeconds * ticksPerSecond));
        var ticks = new int[Math.Max(1, phases)];
        for (int i = 0; i < ticks.Length; i++)
        {
            float f = ticks.Length == 1 ? 0f : loops ? i / (float)ticks.Length : i / (float)(ticks.Length - 1);
            ticks[i] = Math.Clamp((int)MathF.Round(f * total), 0, total - 1);
        }
        return ticks;
    }
}

/// <summary>
/// Watches the feet that should be holding the ground. A foot's ground position is where it is drawn once the world's slide is taken
/// off; slide is how far a foot has moved from where it was when it last came down, while it is still meant to be planted.
/// </summary>
public sealed class SlideTracker
{
    Vector3[] _from = Array.Empty<Vector3>();
    bool[] _was = Array.Empty<bool>();
    float[] _now = Array.Empty<float>();
    float[] _worstEach = Array.Empty<float>();

    /// <summary>Slide of each foot at the last update, in metres; NaN while the foot is not planted.</summary>
    public IReadOnlyList<float> Now => _now;
    /// <summary>The most each foot slid at any update.</summary>
    public IReadOnlyList<float> WorstEach => _worstEach;
    public float Worst => _worstEach.Length == 0 ? 0f : _worstEach.Max();

    /// <param name="feet">Every foot, in a fixed order.</param>
    /// <param name="groundShift">How far the ground has been slid from the world (take it off to get ground positions).</param>
    public void Update(IReadOnlyList<FootMark> feet, Vector3 groundShift)
    {
        if (_now.Length != feet.Count)
        {
            _from = new Vector3[feet.Count];
            _was = new bool[feet.Count];
            _now = new float[feet.Count];
            _worstEach = new float[feet.Count];
        }
        for (int i = 0; i < feet.Count; i++)
        {
            Vector3 ground = feet[i].Drawn - groundShift;
            if (!feet[i].Planted) { _was[i] = false; _now[i] = float.NaN; continue; }
            if (!_was[i]) { _was[i] = true; _from[i] = ground; }
            _now[i] = ground.DistanceTo(_from[i]);
            _worstEach[i] = MathF.Max(_worstEach[i], _now[i]);
        }
    }
}

/// <summary>How long one cycle of an animation is. A stage that knows says so; otherwise the pose is watched until it comes back round.</summary>
public static class SheetCycle
{
    /// <summary>Used when a pose never repeats (a held stance, a brain's wandering): a few seconds is enough to see it breathe.</summary>
    public const float Fallback = 4f;
    const float Step = 1f / 30f;

    /// <summary>Starts the animation and returns its cycle length in seconds and where the number came from: "scripted", "measured" or "default".</summary>
    public static (float Seconds, string Source) Of(AssetInstance stage, string animation)
    {
        stage.PlayAnimation(animation);
        if (stage.CycleSeconds > 0f) return (stage.CycleSeconds, "scripted");
        if (stage is not IStageProbe probe) return (Fallback, "default");
        probe.Advance(2f);
        var samples = new List<float[]>();
        for (int i = 0; i < 300; i++)
        {
            probe.Advance(Step);
            samples.Add(PoseOf(probe));
        }
        float period = Period(samples, Step);
        return period > 0f ? (period, "measured") : (Fallback, "default");
    }

    /// <summary>The subject's parts relative to where the subject is: each one's position and two axes. The same list every call while it keeps the same parts.</summary>
    public static float[] PoseOf(IStageProbe probe)
    {
        var v = new List<float>(256);
        Vector3 at = probe.Anchor;
        foreach ((Transform3D t, _) in Parts(probe.Subject))
        {
            Vector3 o = t.Origin - at, x = t.Basis.X * 0.5f, y = t.Basis.Y * 0.5f;
            v.AddRange(new[] { o.X, o.Y, o.Z, x.X, x.Y, x.Z, y.X, y.Y, y.Z });
        }
        return v.ToArray();
    }

    /// <summary>The box round the subject now, in world space: its meshes, and a hand's breadth round each bone of a skinned body (whose mesh box does not follow the pose). Null if empty.</summary>
    public static Aabb? BoundsOf(Node subject)
    {
        Aabb? total = null;
        foreach ((Transform3D t, Aabb local) in Parts(subject))
        {
            Aabb world = t * local;
            total = total.HasValue ? total.Value.Merge(world) : world;
        }
        return total;
    }

    const float BoneMargin = 0.15f;

    /// <summary>Everything placed in the subject: each visible plain mesh, and each bone of a skeleton, with the box that goes with it.</summary>
    static IEnumerable<(Transform3D At, Aabb Local)> Parts(Node n)
    {
        if (n == null || n is Node3D { Visible: false }) yield break;
        if (n is Skeleton3D sk)
        {
            for (int i = 0; i < sk.GetBoneCount(); i++)
                yield return (sk.GlobalTransform * sk.GetBoneGlobalPose(i), new Aabb(Vector3.One * -BoneMargin, Vector3.One * (2f * BoneMargin)));
            yield break;
        }
        if (n is MeshInstance3D m && m.Skin == null && m.GetAabb().Size.LengthSquared() > 0f) yield return (m.GlobalTransform, m.GetAabb());
        foreach (Node c in n.GetChildren())
            foreach (var part in Parts(c)) yield return part;
    }

    /// <summary>
    /// The shortest lag, in seconds, at which the series comes back to itself (0 if it never does): the mean squared distance between
    /// samples a lag apart leaves its starting trough, then falls into a deep one.
    /// </summary>
    public static float Period(IReadOnlyList<float[]> samples, float step, float minSeconds = 0.2f, float maxSeconds = 6f)
    {
        int n = samples.Count;
        int maxLag = Math.Min(n / 2, (int)(maxSeconds / step));
        if (maxLag < 4) return 0f;
        var d = new double[maxLag + 2];
        for (int lag = 1; lag <= maxLag + 1; lag++)
        {
            double sum = 0;
            int count = 0;
            for (int i = 0; i + lag < n; i++)
            {
                float[] a = samples[i], b = samples[i + lag];
                int m = Math.Min(a.Length, b.Length);
                for (int k = 0; k < m; k++) { double e = a[k] - b[k]; sum += e * e; }
                count++;
            }
            d[lag] = count == 0 ? 0 : sum / count;
        }
        double top = d.Skip(1).Max();
        if (top < 1e-9) return 0f;
        int l = 1;
        while (l <= maxLag && d[l] < 0.3 * top) l++;
        for (l = Math.Max(l, (int)(minSeconds / step)); l <= maxLag; l++)
        {
            if (d[l] >= 0.15 * top || d[l] > d[l - 1] || d[l] > d[l + 1]) continue;
            double a = d[l - 1], b = d[l], c = d[l + 1], den = a - 2 * b + c;
            double off = den > 1e-12 ? 0.5 * (a - c) / den : 0;
            return (float)((l + Math.Clamp(off, -0.5, 0.5)) * step);
        }
        return 0f;
    }
}

/// <summary>
/// One camera view of a filmstrip row: an orthographic camera looking at the middle of the whole motion's extent, the same in every tile
/// of the row, so poses can be compared frame to frame. Faces: the subject faces -Z in every stage.
/// </summary>
public sealed class ViewFrame
{
    public static readonly string[] Names = { "front", "side", "three", "top" };

    public string Name = "";
    /// <summary>From the subject towards the camera, and the camera's right and up.</summary>
    public Vector3 Toward, Right, Up;
    public float Size;
    /// <summary>The middle of the motion: across the ground relative to where the subject is, in height absolute.</summary>
    public Vector3 Centre;
    public float Aspect = 1f;

    public static bool Known(string name) => Names.Contains(name);

    /// <summary>Frames the box (X and Z relative to the subject, Y absolute), with a margin, to fit a tile of this shape.</summary>
    public static ViewFrame Fit(string name, Aabb box, float aspect)
    {
        (Vector3 toward, Vector3 hint) = name switch
        {
            "front" => (new Vector3(0f, 0f, -1f), Vector3.Up),
            "three" => (new Vector3(0.62f, 0.3f, -0.73f).Normalized(), Vector3.Up),
            "top" => (Vector3.Up, new Vector3(0f, 0f, -1f)),
            _ => (Vector3.Right, Vector3.Up),
        };
        Vector3 right = hint.Cross(toward).Normalized();
        Vector3 up = toward.Cross(right);
        Vector3 c = box.GetCenter();
        float hx = 0f, hy = 0f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = box.GetEndpoint(i) - c;
            hx = MathF.Max(hx, MathF.Abs(p.Dot(right)));
            hy = MathF.Max(hy, MathF.Abs(p.Dot(up)));
        }
        float size = MathF.Max(MathF.Max(hy * 2f, hx * 2f / aspect) * 1.12f, 0.1f);
        return new ViewFrame { Name = name, Toward = toward, Right = right, Up = up, Size = size, Centre = c, Aspect = aspect };
    }

    Vector3 CentreAt(Vector3 anchor) => new(anchor.X + Centre.X, Centre.Y, anchor.Z + Centre.Z);

    /// <summary>The camera's transform while the subject is at <paramref name="anchor"/>.</summary>
    public Transform3D Eye(Vector3 anchor) => new(new Basis(Right, Up, Toward), CentreAt(anchor) + Toward * (Size * 4f + 20f));

    public float Near => 0.1f;
    public float Far => Size * 8f + 60f;

    /// <summary>Where a point lands in a tile of the given pixel size.</summary>
    public Vector2 Project(Vector3 p, Vector3 anchor, Vector2 tile)
    {
        Vector3 v = p - CentreAt(anchor);
        return new Vector2((v.Dot(Right) / (Size * Aspect) + 0.5f) * tile.X, (0.5f - v.Dot(Up) / Size) * tile.Y);
    }
}

/// <summary>What one frame of a sheet says, for the reader who sees the numbers before the picture.</summary>
public sealed class FrameRecord
{
    public int Index;
    public float Seconds, Phase;
    public string Problems = "";
    public long PoseSignature;
    /// <summary>Per foot: metres slid from where it came down while planted, or null when the foot is in the air.</summary>
    public float?[] SlideM = Array.Empty<float?>();
    /// <summary>Per foot: metres between the rig's target and the drawn foot.</summary>
    public float[] OffTargetM = Array.Empty<float>();
}

/// <summary>The JSON that goes beside a sheet.</summary>
public sealed class SheetReport
{
    public string Asset = "", AssetName = "", Animation = "", CycleSource = "";
    public ulong Seed;
    public Dictionary<string, string> Options = new();
    public float CycleSeconds;
    public bool Loops;
    public string[] Views = Array.Empty<string>();
    public int Phases, TileW, TileH, Feet;
    public List<FrameRecord> Frames = new();
    /// <summary>The most any planted foot slid, over every tick of the cycle (not only the frames shown), and which.</summary>
    public float WorstSlideM;
    public int WorstSlideFoot = -1;
    public float[] WorstSlideEachM = Array.Empty<float>();
    public int[] FramesWithProblems = Array.Empty<int>();
    public string Error = "";

    public void Summarise(SlideTracker slide)
    {
        WorstSlideEachM = slide.WorstEach.ToArray();
        WorstSlideM = slide.Worst;
        WorstSlideFoot = WorstSlideEachM.Length == 0 ? -1 : Array.IndexOf(WorstSlideEachM, WorstSlideM);
        FramesWithProblems = Frames.Where(f => f.Problems != "").Select(f => f.Index).ToArray();
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static string ToJson(object any) => JsonSerializer.Serialize(any, Json);
}
