using System;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Poi;
using Planets.Surface;
using PlanetSurface.World;

namespace PlanetSurface.Terrain;

/// <summary>
/// The far land's shade, past the sun's real-time shadows: a square of the land round a point, and for
/// each cell of it how high the land, the woods and the places stand toward the sun, as the steepest
/// rise seen from the cell's own ground along the sun's way (a tangent). A shader shades a cell
/// whose rise is above the sun's height, softly over a degree or two either side, so ridges shadow
/// their valleys and woods and places the ground behind them. It holds for any height of the sun on
/// that way: only the sun's turn (or the square moving on) asks for a new one.
/// </summary>
public sealed class FarShade
{
    /// <summary>Cells each way.</summary>
    public const int N = 256;

    /// <summary>How far along the sun's way the rise is looked for, m.</summary>
    public const float Look = 4000f;

    /// <summary>The square's corner (frame coordinates, x and z) and side, m; the sun's way (flat, toward it) it was made for.</summary>
    public readonly float X0, Z0, Side;
    public readonly Vector2 Toward;
    public readonly float[] Rise;

    FarShade(float x0, float z0, float side, Vector2 toward, float[] rise)
    {
        X0 = x0;
        Z0 = z0;
        Side = side;
        Toward = toward;
        Rise = rise;
    }

    public float Cell => Side / N;

    /// <summary>The rise toward the sun at a point (frame coordinates), or null off the square.</summary>
    public float? RiseAt(float x, float z)
    {
        int i = (int)((x - X0) / Cell), j = (int)((z - Z0) / Cell);
        if (i < 0 || j < 0 || i >= N || j >= N) return null;
        return Rise[j * N + i];
    }

    /// <summary>How much of the sun a point gets by this (0 shaded, 1 lit), for a sun this many degrees up, as the shader has it.</summary>
    public float Lit(float x, float z, float sunTangent) => RiseAt(x, z) is float r ? 1f - Mathf.SmoothStep(-Soft, Soft, r - sunTangent) : 1f;

    /// <summary>The soft edge, as a tangent either side of the sun's: a shadow's penumbra and the cells' blur.</summary>
    public const float Soft = 0.025f;

    /// <summary>Where the far shade comes in and is full, as shares of the shadow distance; the real shadows fade out from the second (the light's fade start), so the two overlap and never leave a gap.</summary>
    public const float JoinFrom = 0.7f, JoinFull = 0.85f;

    /// <summary>How far the far shade is in at a distance from the eye (0 out, 1 in), as the shader has it.</summary>
    public static float Weight(float away, float reach) => Mathf.SmoothStep(reach * JoinFrom, reach * JoinFull, away);

    /// <summary>How much the real shadows hold at a distance from the eye (1 in, 0 faded out), as the light fades them.</summary>
    public static float RealWeight(float away, float reach) => 1f - Mathf.SmoothStep(reach * JoinFull, reach, away);

    /// <summary>Tree tops over the woods, by how wooded, m.</summary>
    const float Canopy = 14f;

    /// <summary>
    /// Works a square out (worker-safe: the generator's heights, flora rules and the places'
    /// footprints only). <paramref name="half"/>: half its side, m; <paramref name="toward"/>: the sun's flat way.
    /// </summary>
    public static FarShade Build(PlanetWorld world, float cx, float cz, float half, Vector2 toward)
    {
        float side = half * 2f, cell = side / N, x0 = cx - half, z0 = cz - half;
        double w = world.Terrain.Width, h = world.Terrain.Height;
        var ground = new float[N * N];
        var top = new float[N * N];
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            double x = Mathx.WrapX(x0 + (i + 0.5f) * cell, w), z = Mathx.Wrap(z0 + (j + 0.5f) * cell, h);
            float g = world.GroundHeight(x, z);
            if (world.Source.WaterSurfaceAt(x, z, out float sea)) g = MathF.Max(g, sea);
            ground[j * N + i] = g;
            // Woods stand their trees' height over the land.
            float wood = 0f;
            if (world.Flora != null)
                foreach (FloraRule r in world.Flora.RulesOf(world.Palette.BiomeAt(x, z)))
                    if (FloraMeshes.IsLarge(r.Kind)) wood += r.Density;
            // A cell stands as high as its highest ground, not just its middle's: a ridge or a cliff narrower than a cell still shades what lies behind it.
            float peak = g;
            for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
                peak = MathF.Max(peak, world.GroundHeight(x + a * cell / 4, z + b * cell / 4));
            top[j * N + i] = peak + Canopy * Math.Clamp(wood / 6f, 0f, 1f);
        }
        // Places stand their buildings' height.
        foreach (PoiSite s in world.Pois)
        {
            if (s.Layout == null) continue;
            var lo = s.Footprint.Min;
            var hi = s.Footprint.Max;
            float roof = hi.Y + 1;
            double mx = (lo.X + hi.X + 1) * 0.5, mz = (lo.Z + hi.Z + 1) * 0.5;
            float dx = (float)Mathx.WrapDelta(mx - cx, w), dz = (float)Mathx.WrapDelta(mz - cz, h);
            int i0 = (int)((cx + dx - (hi.X - lo.X + 1) * 0.5f - x0) / cell), i1 = (int)((cx + dx + (hi.X - lo.X + 1) * 0.5f - x0) / cell);
            int j0 = (int)((cz + dz - (hi.Z - lo.Z + 1) * 0.5f - z0) / cell), j1 = (int)((cz + dz + (hi.Z - lo.Z + 1) * 0.5f - z0) / cell);
            for (int j = Math.Max(0, j0); j <= Math.Min(N - 1, j1); j++)
            for (int i = Math.Max(0, i0); i <= Math.Min(N - 1, i1); i++)
                top[j * N + i] = MathF.Max(top[j * N + i], roof);
        }
        // The steepest rise toward the sun from each cell's ground, along its way, a cell at a time.
        var rise = new float[N * N];
        int steps = (int)MathF.Min(Look / cell, N * 1.5f);
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            float g0 = ground[j * N + i] + 1f, best = -1f;
            for (int k = 1; k <= steps; k++)
            {
                float fi = i + toward.X * k, fj = j + toward.Y * k;
                if (fi < 0f || fj < 0f || fi > N - 1 || fj > N - 1) break;
                float t = (top[(int)(fj + 0.5f) * N + (int)(fi + 0.5f)] - g0) / (k * cell);
                if (t > best) best = t;
            }
            rise[j * N + i] = best;
        }
        return new FarShade(x0, z0, side, toward, rise);
    }

    /// <summary>The rises as a texture (one float a cell), for the shaders.</summary>
    public ImageTexture Texture()
    {
        var bytes = new byte[N * N * 4];
        Buffer.BlockCopy(Rise, 0, bytes, 0, bytes.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(N, N, false, Image.Format.Rf, bytes));
    }
}
