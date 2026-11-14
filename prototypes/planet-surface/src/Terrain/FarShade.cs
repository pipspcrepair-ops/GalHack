using System;
using System.Collections.Generic;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using Planets.Look;
using Planets.Surface;
using PlanetSurface.Poi;
using PlanetSurface.World;

namespace PlanetSurface.Terrain;

/// <summary>
/// The far land's shade, past the sun's real-time shadows: a square of the land round the eye, and for each cell of it
/// how high the land, the woods and the places stand toward the sun, as the steepest rise seen from the cell's own
/// ground along the sun's way (a tangent). A shader shades a cell whose rise is above the sun's height, softly over a
/// degree or two either side, so ridges shadow their valleys and woods and places the ground behind them. The graphics
/// card works the square out (<see cref="Glsl"/>) from the GPU rings' coarsest texture (<see cref="ClipGround"/>, which
/// holds each point's ground and its top: the highest ground round it, and the woods and roofs over that), again
/// whenever the sun turns or the square moves a cell; nothing is built on the CPU or blended in. The CPU marches the
/// same way over the same ground (<see cref="Rises"/>) for tests and for what a point gets of the sun.
/// </summary>
public static class FarShade
{
    /// <summary>Cells each way.</summary>
    public const int N = 256;

    /// <summary>How far along the sun's way the rise is looked for, m.</summary>
    public const float Look = 4000f;

    /// <summary>The soft edge, as a tangent either side of the sun's: a shadow's penumbra and the cells' blur.</summary>
    public const float Soft = 0.025f;

    /// <summary>Where the far shade comes in and is full, as shares of the shadow distance; the real shadows fade out from the second (the light's fade start), so the two overlap and never leave a gap.</summary>
    public const float JoinFrom = 0.7f, JoinFull = 0.85f;

    /// <summary>How far the far shade is in at a distance from the eye (0 out, 1 in), as the shader has it.</summary>
    public static float Weight(float away, float reach) => Mathf.SmoothStep(reach * JoinFrom, reach * JoinFull, away);

    /// <summary>How much the real shadows hold at a distance from the eye (1 in, 0 faded out), as the light fades them.</summary>
    public static float RealWeight(float away, float reach) => 1f - Mathf.SmoothStep(reach * JoinFull, reach, away);

    /// <summary>How much of the sun a cell gets by its rise (0 shaded, 1 lit), for a sun this high (a tangent), as the shader has it.</summary>
    public static float Lit(float rise, float sunTangent) => 1f - Mathf.SmoothStep(-Soft, Soft, rise - sunTangent);

    /// <summary>Tree tops over the woods, by how wooded, m.</summary>
    const float Canopy = 14f;

    /// <summary>
    /// A point's ground as the shade has it (the true ground, or the water over it) and its top: the highest ground a quarter
    /// of a spacing round it (a ridge or a cliff narrower than a texel still shades what lies behind it), with the woods'
    /// height over it and any place's roof. <paramref name="roofs"/>: the places near (their footprints, canonical, and roofs).
    /// </summary>
    internal static (float Ground, float Top) Point(PlanetTerrainSource source, GroundPalette palette, IReadOnlyList<(double X0, double Z0, double X1, double Z1, float Roof)> roofs, double x, double z, float spacing)
    {
        double w = palette.Map.WidthMeters, h = palette.Map.HeightMeters;
        x = Mathx.WrapX(x, w);
        z = Mathx.Wrap(z, h);
        float g = source.SurfaceHeight(x, z);
        if (source.WaterSurfaceAt(x, z, out float sea)) g = MathF.Max(g, sea);
        float peak = g, q = spacing / 4f;
        for (int a = -1; a <= 1; a += 2)
        for (int b = -1; b <= 1; b += 2)
            peak = MathF.Max(peak, source.SurfaceHeight(x + a * q, z + b * q));
        float wood = 0f;
        if (palette.Flora != null)
            foreach (FloraRule r in palette.Flora.RulesOf(palette.BiomeAt(x, z)))
                if (FloraMeshes.IsLarge(r.Kind)) wood += r.Density;
        float top = peak + Canopy * Math.Clamp(wood / 6f, 0f, 1f);
        foreach (var (x0, z0, x1, z1, roof) in roofs)
            if (Mathx.WrapDelta(x - x0, w) >= 0 && Mathx.WrapDelta(x1 - x, w) >= 0 && Mathx.WrapDelta(z - z0, h) >= 0 && Mathx.WrapDelta(z1 - z, h) >= 0)
                top = MathF.Max(top, roof);
        return (g, top);
    }

    /// <summary>The places that may stand over a square (canonical centre and half side, m): footprints and roofs.</summary>
    internal static List<(double X0, double Z0, double X1, double Z1, float Roof)> Roofs(IReadOnlyList<PoiSite> pois, double cx, double cz, double half, double w, double h)
    {
        var near = new List<(double, double, double, double, float)>();
        if (pois == null) return near;
        foreach (PoiSite s in pois)
        {
            if (s.Instance == null) continue;
            var lo = s.Footprint.Min;
            var hi = s.Footprint.Max;
            double mx = (lo.X + hi.X + 1) * 0.5, mz = (lo.Z + hi.Z + 1) * 0.5, rx = (hi.X - lo.X + 1) * 0.5, rz = (hi.Z - lo.Z + 1) * 0.5;
            if (Math.Abs(Mathx.WrapDelta(mx - cx, w)) > half + rx || Math.Abs(Mathx.WrapDelta(mz - cz, h)) > half + rz) continue;
            near.Add((lo.X, lo.Z, hi.X + 1, hi.Z + 1, hi.Y + 1));
        }
        return near;
    }

    /// <summary>A square of rises worked out on the CPU: its corner (x, z), side and rises, a cell at a time.</summary>
    public sealed record Square(float X0, float Z0, float Side, float[] Rise)
    {
        public float Cell => Side / N;

        /// <summary>How much of the sun a point gets by this (0 shaded, 1 lit), for a sun this high (a tangent), as the shader has it.</summary>
        public float Lit(float x, float z, float sunTangent)
        {
            int i = (int)MathF.Floor((x - X0) / Cell), j = (int)MathF.Floor((z - Z0) / Cell);
            return i < 0 || j < 0 || i >= N || j >= N ? 1f : FarShade.Lit(Rise[j * N + i], sunTangent);
        }
    }

    /// <summary>
    /// The steepest rise toward the sun from each cell's ground, along its way a cell at a time as far as it looks (past the
    /// square too, so a cell's rise never hangs on where the square stands: nothing crawls as the eye moves): the march
    /// the graphics card makes (<see cref="Glsl"/>), from a sampler of the ground and the tops at points of the square's frame.
    /// </summary>
    public static Square Rises(Func<float, float, (float Ground, float Top)> at, float x0, float z0, float side, Vector2 toward)
    {
        var rise = new float[N * N];
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
            rise[j * N + i] = Rise(at, x0, z0, side / N, toward, i, j);
        return new Square(x0, z0, side, rise);
    }

    /// <summary>One cell's rise (<see cref="Rises"/>).</summary>
    public static float Rise(Func<float, float, (float Ground, float Top)> at, float x0, float z0, float cell, Vector2 toward, int i, int j)
    {
        int steps = (int)MathF.Min(Look / cell, N * 1.5f);
        float g0 = at(x0 + (i + 0.5f) * cell, z0 + (j + 0.5f) * cell).Ground + 1f, best = -1f;
        for (int k = 1; k <= steps; k++)
        {
            float fi = i + toward.X * k, fj = j + toward.Y * k;
            best = MathF.Max(best, (at(x0 + (fi + 0.5f) * cell, z0 + (fj + 0.5f) * cell).Top - g0) / (k * cell));
        }
        return best;
    }
    /// <summary>
    /// The march on the graphics card: a cell a thread, its ground and the tops along the sun's way read from the coarsest
    /// ring's texture (ground, top), bilinear, wrapping round as the texture does.
    /// </summary>
    internal const string Glsl = @"#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
layout(set = 0, binding = 0) uniform sampler2DArray tops;
layout(r32f, set = 0, binding = 1) uniform restrict writeonly image2D rise;
layout(push_constant, std430) uniform Params {
	vec2 origin;  // the square's corner, the textures' coordinates
	vec2 toward;  // the sun's way, flat
	float cell;   // m
	float spacing;
	float texels;
	float level;
	float steps;
	float n;
	vec2 pad;
} p;

vec2 at(vec2 q) { return textureLod(tops, vec3((q / p.spacing + 0.5) / p.texels, p.level), 0.0).xy; }

void main() {
	ivec2 c = ivec2(gl_GlobalInvocationID.xy);
	float g0 = at(p.origin + (vec2(c) + 0.5) * p.cell).x + 1.0, best = -1.0;
	for (int k = 1; k <= int(p.steps); k++) {
		vec2 f = vec2(c) + p.toward * float(k);
		best = max(best, (at(p.origin + (f + 0.5) * p.cell).y - g0) / (float(k) * p.cell));
	}
	imageStore(rise, c, vec4(best));
}
";
}
