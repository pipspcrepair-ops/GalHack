using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace GalHack.Stars;

/// <summary>
/// How a sky stands against the catalogue. The star field hangs in the system's own frame, and the
/// system says how the world turns in that (<c>StarSystem.Orient</c>): all a sky needs from here is
/// the fixed tilt between that frame and the galaxy, and a way past the map's seam.
/// </summary>
public static class SkyFrame
{
    /// <summary>How a system's frame stands against the galaxy: seeded, so its band crosses its sky its own way.</summary>
    public static Quaternion Tilt(ulong seed)
    {
        var rng = new GalaxyRng(Mix.Of(seed, 0x54494C54));
        return rng.Turn();
    }

    /// <summary>How near the map's north and south edges, m, the sky starts making its way round.</summary>
    public const double SeamBand = 240;

    /// <summary>
    /// The colatitude a sky is seen from, radians, for a map row (<paramref name="z"/> of
    /// <paramref name="height"/>). The map wraps north to south but the globe's two poles are opposite
    /// each other, so the true sky would flip end over end at the edge. Near each edge the sky swings
    /// back toward the equator on its own longitude instead, a quarter turn each side, and both sides
    /// meet in the same sky: the same longitude, so night stays night. Away from the edges it is the
    /// true colatitude, and it is the same at a row whichever way you came.
    /// </summary>
    public static double SeamColatitude(double z, double height)
    {
        double north = Math.Min(z, height - z);
        double trueAt = north / height * Math.PI;
        double at = trueAt;
        if (north < SeamBand)
        {
            double t = north / SeamBand, edge = SeamBand / height * Math.PI;
            at = Math.PI / 2.0 + (edge - Math.PI / 2.0) * (t * t * (3.0 - 2.0 * t));
        }
        return z <= height / 2.0 ? at : Math.PI - at;
    }
}

/// <summary>
/// The sky seen from one place in the galaxy, ready for the shader, baked on a worker thread:
/// - every star's true direction and how bright it looks, from its light, its distance and the
///   dust in the way, listed by the cell of a cube grid over the sky it falls in, so a pixel looks
///   only at the few stars near it;
/// - a soft light from the catalogue's own stars, each spread a little: where they crowd too thickly
///   to tell apart it is all that shows, and it is the galaxy's band and core;
/// - the nebulae, marched from here, so they stand where they are, as big as they look from here,
///   and fill the sky from inside.
/// Directions are the catalogue's: <see cref="SkyFrame"/> turns the sky round them.
/// </summary>
public sealed class SkyView
{
    /// <summary>Cells along a cube face's edge; most stars a cell lists.</summary>
    public const int Grid = 192, MostInCell = 15;

    /// <summary>Texels across a face of the soft light.</summary>
    public const int GlowSize = 384;

    /// <summary>The width of the stars' data texture.</summary>
    public const int StarsWide = 1024;

    /// <summary>How bright a star is drawn, a log scale in 256 steps: must match GalaxyShader.</summary>
    public const float FaintestShown = 0.0015f, BrightestShown = 40f;

    /// <summary>The most any pixel's angle is expected to be, radians: how wide a star's cells are chosen for.</summary>
    const float WidestPixel = 0.002f;

    public readonly Vector3 Eye;
    public readonly int Here;

    /// <summary>The direction each star is seen in, the catalogue's frame.</summary>
    public readonly Vector3[] Direction;

    /// <summary>How bright each star is drawn (the shader's scale); 0: the star you are at.</summary>
    public readonly float[] Shown;

    /// <summary>Whether a star has its own point in the sky, not only a share of the soft light.</summary>
    public readonly bool[] Resolved;

    /// <summary>Four floats a star: its direction, and its colour and brightness packed in an integer.</summary>
    public float[] StarTexels { get; private set; }
    public int StarRows { get; private set; }

    /// <summary>6 × Grid cells wide, Grid tall: first list entry × 16 + how many.</summary>
    public float[] CellTexels { get; private set; }

    /// <summary>The soft light on each cube face, RGB floats.</summary>
    public readonly float[][] Glow = new float[6][];

    SkyView(Galaxy g, Vector3 eye, int here)
    {
        Eye = eye;
        Here = here;
        int n = g.Stars.Length;
        Direction = new Vector3[n];
        Shown = new float[n];
        Resolved = new bool[n];
    }

    /// <summary>The sky from a star's place (its own light left out: there it is the sun).</summary>
    public static SkyView From(Galaxy g, int star) => Bake(g, g.Stars[star].Position, star);

    /// <summary>Bakes the sky at a point. It takes a moment: call it off the main thread.</summary>
    public static SkyView Bake(Galaxy g, Vector3 eye, int here = -1)
    {
        var view = new SkyView(g, eye, here);
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 6) };
        view.PlaceStars(g, options);
        view.ListStars(g);
        for (int f = 0; f < 6; f++) view.Glow[f] = new float[GlowSize * GlowSize * 3];
        view.Spread(g);
        for (int f = 0; f < 6; f++) Soften(view.Glow[f]);
        Parallel.For(0, 6, options, f => view.March(g, f));
        return view;
    }

    /// <summary>What light from a star looks like here, suns per square light year, on the drawn scale: compressed, as an eye or a long exposure does.</summary>
    public static float Tone(float flux) => MathF.Min(MathF.Pow(flux / 0.2f, 0.33f), 2.2f);

    /// <summary>The faintest a star within a jump is drawn: a star on the map can be found in the sky.</summary>
    public const float ChartFloor = 0.06f;

    /// <summary>How wide a star of this brightness is drawn, in pixels: must match GalaxyShader.</summary>
    public static float Spread(float shown) => 0.7f + 0.8f * MathF.Min(MathF.Sqrt(shown) * 0.5f, 1f);

    void PlaceStars(Galaxy g, ParallelOptions options)
    {
        Star[] stars = g.Stars;
        Parallel.For(0, stars.Length, options, i =>
        {
            if (i == Here) return;
            Vector3 to = stars[i].Position - Eye;
            float d = to.Length();
            if (d < 1e-3f) return;
            Direction[i] = to / d;
            float clear = g.Clear(Eye, stars[i].Position);
            if (stars[i].Region == Region.Core) clear = MathF.Max(clear, 0.5f); // their massed glow reads through any cloud
            float flux = stars[i].Light / (d * d) * clear;
            float shown = Tone(flux);
            if (d <= Galaxy.JumpT2) shown = MathF.Max(shown, ChartFloor);
            Shown[i] = shown;
        });
    }

    // ---- Stars ----------------------------------------------------------------------------------

    /// <summary>Which face, and where on it (each -1 to 1), a direction falls: the graphics API's own cube layout, so the shader's cubemap agrees.</summary>
    public static (int face, float s, float t) Face(Vector3 d)
    {
        float ax = MathF.Abs(d.X), ay = MathF.Abs(d.Y), az = MathF.Abs(d.Z);
        if (ax >= ay && ax >= az) return d.X > 0f ? (0, -d.Z / ax, -d.Y / ax) : (1, d.Z / ax, -d.Y / ax);
        if (ay >= az) return d.Y > 0f ? (2, d.X / ay, d.Z / ay) : (3, d.X / ay, -d.Z / ay);
        return d.Z > 0f ? (4, d.X / az, -d.Y / az) : (5, -d.X / az, -d.Y / az);
    }

    /// <summary>The direction through a point of a cube face.</summary>
    public static Vector3 Through(int face, float s, float t) => Vector3.Normalize(face switch
    {
        0 => new Vector3(1f, -t, -s),
        1 => new Vector3(-1f, -t, s),
        2 => new Vector3(s, 1f, t),
        3 => new Vector3(s, -1f, -t),
        4 => new Vector3(s, -t, 1f),
        _ => new Vector3(-s, -t, -1f),
    });

    public static int CellOf(Vector3 d)
    {
        var (f, s, t) = Face(d);
        int i = Math.Clamp((int)((s * 0.5f + 0.5f) * Grid), 0, Grid - 1), j = Math.Clamp((int)((t * 0.5f + 0.5f) * Grid), 0, Grid - 1);
        return f * Grid + i + j * Grid * 6;
    }

    /// <summary>The stars a cell lists (tests read what the shader will).</summary>
    public IEnumerable<int> Listed(int cell)
    {
        int code = (int)CellTexels[cell], at = code >> 4, count = code & 15;
        for (int i = 0; i < count; i++) yield return (int)_order[at + i];
    }

    int[] _order;

    /// <summary>Lists each star in every cell its point reaches, brightest first. A cell that fills leaves its faintest to the soft light.</summary>
    void ListStars(Galaxy g)
    {
        int n = g.Stars.Length;
        var byBright = new int[n];
        var keys = new float[n];
        for (int i = 0; i < n; i++)
        {
            byBright[i] = i;
            keys[i] = -Shown[i];
        }
        Array.Sort(keys, byBright);
        var counts = new int[Grid * Grid * 6];
        var touched = new List<(int star, int cell)>(n * 2);
        var mine = new List<int>(12);
        foreach (int i in byBright)
        {
            if (Shown[i] < FaintestShown) continue;
            Vector3 d = Direction[i];
            mine.Clear();
            mine.Add(CellOf(d));
            float reach = Spread(Shown[i]) * WidestPixel * 3f;
            Vector3 a = Vector3.Normalize(Vector3.Cross(d, MathF.Abs(d.Y) > 0.8f ? Vector3.UnitX : Vector3.UnitY)), b = Vector3.Cross(d, a);
            for (int k = 0; k < 16; k++)
            {
                float angle = k * MathF.Tau / 8f, r = k < 8 ? reach : reach * 0.55f;
                int c = CellOf(Vector3.Normalize(d + (a * MathF.Cos(angle) + b * MathF.Sin(angle)) * r));
                if (!mine.Contains(c)) mine.Add(c);
            }
            bool room = true;
            foreach (int c in mine) room &= counts[c] < MostInCell;
            if (!room) continue;
            foreach (int c in mine)
            {
                counts[c]++;
                touched.Add((i, c));
            }
            Resolved[i] = true;
        }
        var start = new int[counts.Length];
        int total = 0;
        for (int c = 0; c < counts.Length; c++)
        {
            start[c] = total;
            total += counts[c];
        }
        var fill = new int[counts.Length];
        _order = new int[total];
        StarRows = Math.Max(1, (total + StarsWide - 1) / StarsWide);
        StarTexels = new float[StarsWide * StarRows * 4];
        foreach (var (star, cell) in touched)
        {
            int at = start[cell] + fill[cell]++;
            _order[at] = star;
            Vector3 d = Direction[star];
            StarTexels[at * 4] = d.X;
            StarTexels[at * 4 + 1] = d.Y;
            StarTexels[at * 4 + 2] = d.Z;
            StarTexels[at * 4 + 3] = Pack(TintOf(g, star), Shown[star]);
        }
        CellTexels = new float[counts.Length];
        for (int c = 0; c < counts.Length; c++) CellTexels[c] = start[c] * 16 + counts[c];
    }

    /// <summary>A star's colour by kind, each a little its own: no two whites quite the same.</summary>
    public static Vector3 TintOf(Galaxy g, int star)
    {
        Star s = g.Stars[star];
        Vector3 c = Colour(s.Kind);
        ulong h = Mix.Of(g.Seed, 0x54494E54, star);
        float warm = Mix.Unit(h) - 0.5f;
        c += new Vector3(warm * 0.1f, 0f, -warm * 0.12f);
        return Vector3.Clamp(c, Vector3.Zero, Vector3.One);
    }

    public static Vector3 Colour(StarKind k) => k switch
    {
        StarKind.BrownDwarf => new Vector3(1f, 0.42f, 0.25f),
        StarKind.RedDwarf => new Vector3(1f, 0.62f, 0.42f),
        StarKind.OrangeDwarf => new Vector3(1f, 0.8f, 0.58f),
        StarKind.YellowDwarf => new Vector3(1f, 0.94f, 0.8f),
        StarKind.WhiteStar => new Vector3(0.93f, 0.95f, 1f),
        StarKind.BlueGiant => new Vector3(0.66f, 0.78f, 1f),
        StarKind.Protostar => new Vector3(1f, 0.66f, 0.5f),
        StarKind.RedGiant => new Vector3(1f, 0.55f, 0.3f),
        StarKind.WhiteDwarf => new Vector3(0.82f, 0.88f, 1f),
        StarKind.Pulsar => new Vector3(0.64f, 0.86f, 1f),
        StarKind.Magnetar => new Vector3(0.76f, 0.72f, 1f),
        _ => new Vector3(1f, 0.82f, 0.6f),
    };

    /// <summary>R5 G6 B5 and a brightness step, in an integer a float holds exactly.</summary>
    public static float Pack(Vector3 tint, float shown)
    {
        int r = (int)(tint.X * 31f + 0.5f), g = (int)(tint.Y * 63f + 0.5f), b = (int)(tint.Z * 31f + 0.5f);
        float step = MathF.Log(Math.Clamp(shown, FaintestShown, BrightestShown) / FaintestShown) / MathF.Log(BrightestShown / FaintestShown) * 255f;
        return (r << 19) | (g << 13) | (b << 8) | (int)(step + 0.5f);
    }

    // ---- Soft light -----------------------------------------------------------------------------

    /// <summary>A star's share of the soft light: all of it for a star without its own point, a little for the rest, none for the close ones (they would show as blurs).</summary>
    const float SoftGain = 1.1f, SoftShare = 0.45f, NearestSpread = 40f;

    /// <summary>
    /// The core's stars are the farthest and the most tightly packed: each is faint, and it is
    /// together that they show, as a real glowing mass you can steer by from anywhere. They count
    /// for more of the soft light than their share, and it spreads a little wider.
    /// </summary>
    const float CoreShare = 3.2f, CoreSpread = 1.5f;

    /// <summary>Scatters each star's light over a little patch of sky: a tight patch and a wide one, so crowded sky reads as a soft grained band.</summary>
    void Spread(Galaxy g)
    {
        const int Samples = 40;
        for (int i = 0; i < g.Stars.Length; i++)
        {
            if (i == Here || Shown[i] <= 0f) continue;
            float d = Vector3.Distance(g.Stars[i].Position, Eye);
            if (d < NearestSpread) continue;
            bool core = g.Stars[i].Region == Region.Core;
            // Per texel: a finer cube spreads the same light over more of them.
            float amount = MathF.Min(Shown[i], 0.45f) * SoftGain * (GlowSize / 256f) * (GlowSize / 256f) * (core ? CoreShare : Resolved[i] ? SoftShare : 1f) / Samples;
            Vector3 tint = Vector3.Lerp(TintOf(g, i), Vector3.One, 0.35f) * amount;
            Vector3 dir = Direction[i];
            Vector3 a = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.8f ? Vector3.UnitX : Vector3.UnitY)), b = Vector3.Cross(dir, a);
            var rng = new GalaxyRng(Mix.Of(g.Seed, 0x534F4654, i));
            for (int k = 0; k < Samples; k++)
            {
                // A pair of normal numbers, for a round soft patch; the second half reach further.
                float radius = MathF.Sqrt(-2f * MathF.Log(1f - rng.Float() * 0.999f)) * (k < Samples / 2 ? 0.02f : 0.06f) * (core ? CoreSpread : 1f), turn = rng.Range(0f, MathF.Tau);
                Deposit(Vector3.Normalize(dir + (a * MathF.Cos(turn) + b * MathF.Sin(turn)) * radius), tint);
            }
        }
    }

    /// <summary>A small blur over a face: the stars' soft light is a haze, with no grain of its own.</summary>
    static void Soften(float[] glow)
    {
        var from = (float[])glow.Clone();
        for (int y = 0; y < GlowSize; y++)
        for (int x = 0; x < GlowSize; x++)
        for (int c = 0; c < 3; c++)
        {
            float sum = 0f;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                sum += from[(Math.Clamp(x + dx, 0, GlowSize - 1) + Math.Clamp(y + dy, 0, GlowSize - 1) * GlowSize) * 3 + c];
            glow[(x + y * GlowSize) * 3 + c] = sum / 9f;
        }
    }

    void Deposit(Vector3 d, Vector3 light)
    {
        var (f, s, t) = Face(d);
        float x = (s * 0.5f + 0.5f) * GlowSize - 0.5f, y = (t * 0.5f + 0.5f) * GlowSize - 0.5f;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        void Put(int px, int py, float w)
        {
            int k = (Math.Clamp(px, 0, GlowSize - 1) + Math.Clamp(py, 0, GlowSize - 1) * GlowSize) * 3;
            Glow[f][k] += light.X * w;
            Glow[f][k + 1] += light.Y * w;
            Glow[f][k + 2] += light.Z * w;
        }
        Put(ix, iy, (1f - fx) * (1f - fy));
        Put(ix + 1, iy, fx * (1f - fy));
        Put(ix, iy + 1, (1f - fx) * fy);
        Put(ix + 1, iy + 1, fx * fy);
    }

    // ---- Nebulae --------------------------------------------------------------------------------

    /// <summary>Marches every texel of a face through the nebulae in its way, nearest first: what glows, less the dust in front of it, over the stars' soft light behind.</summary>
    void March(Galaxy g, int face)
    {
        float[] glow = Glow[face];
        var hits = new List<(float t0, float t1, Nebula n)>(8);
        const float Far = 2400f;
        for (int y = 0; y < GlowSize; y++)
        for (int x = 0; x < GlowSize; x++)
        {
            Vector3 dir = Through(face, (x + 0.5f) / GlowSize * 2f - 1f, (y + 0.5f) / GlowSize * 2f - 1f);
            hits.Clear();
            foreach (Nebula n in g.Nebulae)
                if (n.Crosses(Eye, dir, Far, out float t0, out float t1)) hits.Add((t0, t1, n));
            if (hits.Count == 0) continue;
            if (hits.Count > 1) hits.Sort((p, q) => p.t0.CompareTo(q.t0));
            Vector3 light = Vector3.Zero;
            float through = 1f;
            foreach (var (t0, t1, n) in hits)
            {
                const int Steps = 18;
                float dt = (t1 - t0) / Steps;
                // From inside, the way you look out through it, it is a veil over a night sky, not daylight.
                float veil = 0.3f + 0.7f * MathF.Min(t0 / n.Radius, 1f);
                for (int i = 0; i < Steps; i++)
                {
                    float t = t0 + dt * (i + 0.5f);
                    float d = n.Density(Eye + dir * t);
                    if (d <= 0f) continue;
                    // Thick gas shines far more than thin: the wisps stand out of the veil, and a long look through doesn't even it out.
                    light += n.ColourAt(Eye + dir * t, d) * (d * MathF.Sqrt(d) * 2f * n.Glow * dt * through * veil);
                    through *= MathF.Exp(-d * n.Dust * dt);
                }
            }
            // A long way through it adds up to a veil, not daylight: the sky from inside stays a night sky.
            float most = MathF.Max(light.X, MathF.Max(light.Y, light.Z));
            if (most > 1e-4f) light *= (1f - MathF.Exp(-most * 2.4f)) / (most * 2.4f);
            int k = (x + y * GlowSize) * 3;
            // Dust dims the massed light behind it and takes the stars themselves, but never all of it: the core must read from every system.
            float behind = MathF.Max(through, 0.4f);
            glow[k] = glow[k] * behind + light.X;
            glow[k + 1] = glow[k + 1] * behind + light.Y;
            glow[k + 2] = glow[k + 2] * behind + light.Z;
        }
    }
}
