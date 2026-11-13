using System;
using Godot;

namespace Slice.Player;

/// <summary>
/// The helmet's skin: one texture on the helmet sphere the suit already has, no extra meshes. The
/// cream shell everywhere, and on its front, where the shut visor covers it, the open helmet's
/// face opening painted in: a rubber seal, the quilted interior padding, and the sleeper's face
/// inside it (a 96x72 skin drawn by code: brown and wrinkled, deep sockets, huge sickly
/// yellow-green eyes with a pinhole pupil, raw nostrils, a thin frown, grain). The visor lifting
/// away is what shows it. Built once on first use and shared by every figure.
/// </summary>
public static class SleeperFace
{
    const int W = 96, H = 72;
    /// <summary>The helmet texture: an equirectangular wrap of Godot's SphereMesh (u = 0.5 straight ahead, v down), and its pixels per radian.</summary>
    const int TW = 456, TH = 228;
    const float PerRad = TW / Mathf.Tau;
    /// <summary>
    /// The face window's and the opening's half-widths and half-heights on the sphere, radians, as
    /// rounded rectangles (superellipses). The opening and its seal stay inside the cap the shut
    /// visor covers (47 degrees round straight ahead), so nothing shows while it is shut.
    /// </summary>
    const float FaceA = 0.62f, FaceB = 0.46f, OpenA = 0.72f, OpenB = 0.64f, Seal = 0.06f, Round = 3f;

    static readonly Color Shell = new("ece3c8");

    /// <summary>The helmet sphere's material: the plate's look on the shell, matte skin and padding in the opening.</summary>
    public static StandardMaterial3D Helmet => HelmetMaterial.Value;

    static readonly Lazy<StandardMaterial3D> HelmetMaterial = new(() =>
    {
        (Image albedo, Image rough, Image metal) = DrawHelmet();
        static ImageTexture Tex(Image i) { i.GenerateMipmaps(); return ImageTexture.CreateFromImage(i); }
        return new StandardMaterial3D
        {
            AlbedoTexture = Tex(albedo),
            Roughness = 1f, RoughnessTexture = Tex(rough),
            Metallic = 0.2f, MetallicTexture = Tex(metal),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
        };
    });

    static float Super(float ax, float ay, float a, float b) =>
        MathF.Pow(MathF.Pow(MathF.Abs(ax / a), Round) + MathF.Pow(MathF.Abs(ay / b), Round), 1f / Round);

    static (Image, Image, Image) DrawHelmet()
    {
        Image face = Draw(7);
        var rnd = new Random(11);
        byte[] col = new byte[TW * TH * 3], rough = new byte[TW * TH], metal = new byte[TW * TH];
        int fx0 = TW / 2 - W / 2, fy0 = TH / 2 - H / 2;
        for (int y = 0; y < TH; y++)
            for (int x = 0; x < TW; x++)
            {
                float ax = (x + 0.5f - TW / 2f) / PerRad, ay = (y + 0.5f - TH / 2f) / PerRad;
                float eo = Super(ax, ay, OpenA, OpenB), ef = Super(ax, ay, FaceA, FaceB);
                Color c;
                float r = 0.9f, m = 0f;
                if (eo > 1f + Seal) { c = Shell; r = 0.45f; m = 1f; }
                else if (eo > 1f)
                {
                    // The rubber seal round the opening, a thin highlight on its outer lip.
                    c = new Color("17181f").Lerp(new Color("3c3e4c"), Mathf.SmoothStep(0.65f, 1f, (eo - 1f) / Seal));
                    r = 0.7f;
                }
                else if (ef <= 1f)
                {
                    // The face, set back in the opening: darker towards its edge, most under the brow of the helmet.
                    c = face.GetPixel(Math.Clamp(x - fx0, 0, W - 1), Math.Clamp(y - fy0, 0, H - 1));
                    c *= 1f - Mathf.SmoothStep(0.78f, 1f, ef) * (ay < 0f ? 0.7f : 0.5f);
                }
                else c = Padding(ax, ay, rnd);
                int i = y * TW + x;
                col[i * 3] = (byte)Math.Clamp((int)(c.R * 255f), 0, 255);
                col[i * 3 + 1] = (byte)Math.Clamp((int)(c.G * 255f), 0, 255);
                col[i * 3 + 2] = (byte)Math.Clamp((int)(c.B * 255f), 0, 255);
                rough[i] = (byte)(r * 255f);
                metal[i] = (byte)(m * 255f);
            }
        return (Image.CreateFromData(TW, TH, false, Image.Format.Rgb8, col),
            Image.CreateFromData(TW, TH, false, Image.Format.L8, rough),
            Image.CreateFromData(TW, TH, false, Image.Format.L8, metal));
    }

    /// <summary>
    /// The interior padding between the face and the seal: tuck-and-roll cushions running round the
    /// opening, dark grey-blue, puffed in the middle and creased at their seams, a stitch line beside
    /// each seam and a piped cord against the face; the top half in the helmet's shadow; grain.
    /// </summary>
    static Color Padding(float ax, float ay, Random rnd)
    {
        // Across the band: 0 at the face's edge, 1 at the opening's, found between the two outlines.
        float lo = 0f, hi = 1f;
        for (int k = 0; k < 14; k++)
        {
            float mid = (lo + hi) / 2f;
            if (Super(ax, ay, Mathf.Lerp(FaceA, OpenA, mid), Mathf.Lerp(FaceB, OpenB, mid)) > 1f) lo = mid; else hi = mid;
        }
        float across = (lo + hi) / 2f;
        const int Cushions = 16;
        float round = (MathF.Atan2(ay / OpenB, ax / OpenA) / Mathf.Tau + 1f) * Cushions, seg = round % 1f;
        float puff = MathF.Pow(MathF.Sin(MathF.PI * seg), 0.6f) * MathF.Pow(MathF.Sin(MathF.PI * Math.Clamp((across - 0.18f) / 0.82f, 0f, 1f)), 0.5f);
        Color c = new Color("16171f").Lerp(new Color("4a4e6a"), puff);
        if (across < 0.18f) c = new Color("23242f").Lerp(new Color("50546c"), MathF.Sin(MathF.PI * across / 0.18f)); // the piped cord
        else if (seg < 0.05f || seg > 0.95f) c = new Color("0b0c11");                                               // the crease
        else if ((seg is > 0.08f and < 0.12f) && (int)(across * 40f) % 2 == 0) c = new Color("7a7fa0");             // stitches
        c *= ay < 0f ? Mathf.Lerp(1f, 0.6f, Math.Clamp(-ay / OpenB, 0f, 1f)) : 1f;
        float g = (float)(rnd.NextDouble() - 0.5) * 0.08f + (rnd.NextDouble() < 0.03 ? -0.08f : 0f);
        return new Color(c.R + g, c.G + g, c.B + g);
    }

    // ---- the drawing: a small float canvas with the few brushes the face needs ----

    sealed class Canvas
    {
        readonly float[] _px = new float[W * H * 3];
        uint _s;
        public Canvas(uint seed) => _s = seed;
        public float Rnd() { _s = unchecked(_s * 1664525u + 1013904223u); return _s / 4294967296f; }

        void Over(int x, int y, Color c, float cov)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            float a = c.A * Math.Clamp(cov, 0f, 1f);
            int i = (y * W + x) * 3;
            _px[i] += (c.R * 255f - _px[i]) * a;
            _px[i + 1] += (c.G * 255f - _px[i + 1]) * a;
            _px[i + 2] += (c.B * 255f - _px[i + 2]) * a;
        }

        public void Fill(Color c) { for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) Over(x, y, c, 1f); }

        /// <summary>A black wash whose strength runs down the image through (position, alpha) stops.</summary>
        public void Shade(float[] at, float[] alpha)
        {
            for (int y = 0; y < H; y++)
            {
                float t = (y + 0.5f) / H;
                int k = 1;
                while (k < at.Length - 1 && t > at[k]) k++;
                float a = Mathf.Lerp(alpha[k - 1], alpha[k], Math.Clamp((t - at[k - 1]) / (at[k] - at[k - 1]), 0f, 1f));
                for (int x = 0; x < W; x++) Over(x, y, new Color(0, 0, 0, a), 1f);
            }
        }

        /// <summary>A soft-edged ellipse; <paramref name="alphaAt"/> (optional) scales it by image row.</summary>
        public void Blob(float cx, float cy, float rx, float ry, Color c, Func<float, float> alphaAt = null)
        {
            float soft = MathF.Min(rx, ry);
            for (int y = (int)(cy - ry - 1); y <= cy + ry + 1; y++)
                for (int x = (int)(cx - rx - 1); x <= cx + rx + 1; x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    float cov = (1f - MathF.Sqrt(dx * dx + dy * dy)) * soft + 0.5f;
                    if (cov > 0f) Over(x, y, alphaAt == null ? c : new Color(c, c.A * alphaAt(y + 0.5f)), cov);
                }
        }

        public void Ring(float cx, float cy, float r, float w, Color c)
        {
            for (int y = (int)(cy - r - w); y <= cy + r + w; y++)
                for (int x = (int)(cx - r - w); x <= cx + r + w; x++)
                {
                    float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float cov = w / 2f - MathF.Abs(d - r) + 0.5f;
                    if (cov > 0f) Over(x, y, c, cov);
                }
        }

        /// <summary>A thick jagged line of eight wobbling segments, round-capped, painted once.</summary>
        public void Jag(float x0, float y0, float x1, float y1, float w, Color c, float j = 2.2f)
        {
            var pts = new Vector2[9];
            pts[0] = new Vector2(x0, y0);
            for (int i = 1; i <= 8; i++)
            {
                float t = i / 8f;
                float jx = (Rnd() - 0.5f) * j, jy = (Rnd() - 0.5f) * j;
                pts[i] = new Vector2(x0 + (x1 - x0) * t + jx, y0 + (y1 - y0) * t + jy);
            }
            int bx0 = (int)(MathF.Min(x0, x1) - w - j - 1), bx1 = (int)(MathF.Max(x0, x1) + w + j + 1);
            int by0 = (int)(MathF.Min(y0, y1) - w - j - 1), by1 = (int)(MathF.Max(y0, y1) + w + j + 1);
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float best = float.MaxValue;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector2 a = pts[i], ab = pts[i + 1] - a;
                        float t = Math.Clamp((p - a).Dot(ab) / MathF.Max(ab.LengthSquared(), 1e-6f), 0f, 1f);
                        best = MathF.Min(best, p.DistanceTo(a + ab * t));
                    }
                    float cov = w / 2f - best + 0.5f;
                    if (cov > 0f) Over(x, y, c, cov);
                }
        }

        public void Rect(int x0, int y0, int w, int h, Color c) { for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) Over(x, y, c, 1f); }

        /// <summary>Grain: every pixel nudged, one in fifty dark.</summary>
        public Image Grain()
        {
            var bytes = new byte[W * H * 4];
            for (int i = 0; i < W * H; i++)
            {
                float n = (Rnd() - 0.5f) * 22f, q = Rnd();
                if (q < 0.02f) n -= 55f;
                for (int k = 0; k < 3; k++) bytes[i * 4 + k] = (byte)Math.Clamp((int)MathF.Round(_px[i * 3 + k] + n), 0, 255);
                bytes[i * 4 + 3] = 255;
            }
            return Image.CreateFromData(W, H, false, Image.Format.Rgba8, bytes);
        }
    }

    static Color C(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    /// <summary>The face. The eyes come after every furrow, so nothing crosses them.</summary>
    static Image Draw(uint seed)
    {
        var c = new Canvas(seed);
        float[] eyeX = { 26f, 70f };
        c.Fill(new Color("6a4a2e"));
        c.Shade(new[] { 0f, 0.35f, 1f }, new[] { 0.45f, 0f, 0.35f });
        c.Blob(48, 40, 9, 26, C(160, 125, 85, 0.45f));
        c.Blob(16, 54, 13, 12, C(10, 4, 0, 0.5f)); c.Blob(80, 54, 13, 12, C(10, 4, 0, 0.5f));
        for (int i = 0; i < 60; i++)
        {
            float k = c.Rnd(), bx = c.Rnd() * W, by = c.Rnd() * H, rx = 3 + c.Rnd() * 10, ry = 2 + c.Rnd() * 6;
            c.Blob(bx, by, rx, ry, k < 0.4f ? C(30, 14, 5, 0.35f) : k < 0.7f ? C(170, 140, 90, 0.25f) : C(110, 110, 40, 0.3f));
        }
        foreach (float ex in eyeX) { c.Blob(ex, 34, 19, 16, C(8, 3, 0, 0.8f)); c.Blob(ex, 34, 15, 13, C(4, 1, 0, 0.9f)); }
        c.Jag(6, 19, 40, 21, 5, new Color("1a0c05"), 1.5f); c.Jag(56, 21, 90, 19, 5, new Color("1a0c05"), 1.5f);
        for (int i = 0; i < 3; i++) { float wob = (c.Rnd() - 0.5f) * 3; c.Jag(6, 3 + i * 5, 90, 3 + i * 5 + wob, 1.6f, new Color("160a03"), 2f); }
        c.Jag(45, 6, 44, 19, 2.2f, new Color("120702")); c.Jag(51, 6, 52, 19, 2.2f, new Color("120702"));
        // Furrows and bags first, so the eyes (drawn after) are never crossed.
        for (int ei = 0; ei < 2; ei++)
        {
            float cx = eyeX[ei], cy = 33f, R = 13f;
            for (int i = 0; i < 9; i++)
            {
                float a = MathF.PI * (0.15f + i * 0.1f), lx = R + 7 + c.Rnd() * 5, ly = R + 7 + c.Rnd() * 5;
                c.Jag(cx + MathF.Cos(a) * R, cy + MathF.Sin(a) * R + 1, cx + MathF.Cos(a) * lx, cy + MathF.Sin(a) * ly, 1.1f, new Color("120702"), 1.6f);
            }
            for (int i = 0; i < 4; i++) c.Jag(cx - 14, cy + R + 2 + i * 3, cx + 14, cy + R + 2 + i * 3 + 2, 1.5f - i * 0.2f, new Color("120702"));
        }
        c.Blob(43, 58, 2.6f, 4, new Color("020100")); c.Blob(53, 58, 2.6f, 4, new Color("020100"));
        for (int s = 0; s < 2; s++)
        {
            float f = s == 1 ? -1f : 1f, bx = s == 1 ? 66f : 30f;
            c.Jag(bx, 52, bx - f * 10, 70, 2.4f, new Color("120702")); c.Jag(bx - f * 6, 48, bx - f * 18, 62, 1.6f, new Color("120702")); c.Jag(bx + f * 8, 56, bx - f * 8, 72, 1.3f, new Color("120702"));
        }
        c.Jag(33, 67, 48, 64, 1.7f, new Color("080301")); c.Jag(48, 64, 63, 67, 1.7f, new Color("080301"));
        for (int i = 0; i < 6; i++) { float x = 4 + c.Rnd() * 88, y = 4 + c.Rnd() * 64, rx = 1 + c.Rnd() * 3, ry = 1 + c.Rnd() * 2; c.Blob(x, y, rx, ry, C(185, 170, 130, 0.5f)); }
        for (int i = 0; i < 3; i++) { float x = 4 + c.Rnd() * 88, y = 40 + c.Rnd() * 28, rx = 1 + c.Rnd() * 2, ry = 1 + c.Rnd() * 1.5f; c.Blob(x, y, rx, ry, C(160, 60, 48, 0.7f)); }
        // The eyes: huge, bulging out of the sockets.
        for (int ei = 0; ei < 2; ei++)
        {
            float cx = eyeX[ei], cy = 33f, R = 13f;
            c.Blob(cx, cy, R, R, new Color("a39a3c"));
            for (int i = 0; i < 14; i++)
            {
                float bx = cx + (c.Rnd() - 0.5f) * R * 1.5f, by = cy + (c.Rnd() - 0.5f) * R * 1.5f, rx = 1 + c.Rnd() * 3, ry = 1 + c.Rnd() * 3;
                c.Blob(bx, by, rx, ry, c.Rnd() < 0.5f ? C(70, 60, 10, 0.35f) : C(190, 180, 90, 0.25f));
            }
            float cyy = cy;
            c.Blob(cx, cy, R, R, C(10, 4, 0), y => { float t = (y - (cyy - R)) / (2 * R); return t < 0.5f ? Mathf.Lerp(0.75f, 0f, t * 2f) : Mathf.Lerp(0f, 0.3f, (t - 0.5f) * 2f); });
            for (int i = 0; i < 7; i++)
            {
                float a = (ei == 1 ? -0.4f : MathF.PI + 0.4f) + (c.Rnd() - 0.5f) * 2.4f, l = 0.55f + c.Rnd() * 0.2f;
                c.Jag(cx + MathF.Cos(a) * R, cy + MathF.Sin(a) * R, cx + MathF.Cos(a) * R * l, cy + MathF.Sin(a) * R * l, 0.6f, C(110, 30, 20, 0.6f), 1.6f);
            }
            c.Blob(cx, cy, 2, 2, new Color("030201"));
            c.Rect((int)cx - 5, (int)cy - 3, 2, 2, C(235, 235, 210, 0.85f));
            c.Ring(cx, cy, R, 1.8f, new Color("080301"));
        }
        return c.Grain();
    }
}
