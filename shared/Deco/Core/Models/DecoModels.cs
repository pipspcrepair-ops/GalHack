using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Deco;

/// <summary>
/// Every deco piece's model builder, by piece id. Each set's builders live in their own file.
/// A builder draws the piece standing on y = 0, centred, front facing -Z, inside its footprint.
/// </summary>
public static partial class DecoModels
{
    static Dictionary<string, Action<DecoBuilder>> _all;

    public static IReadOnlyDictionary<string, Action<DecoBuilder>> All
    {
        get
        {
            if (_all != null) return _all;
            var r = new Dictionary<string, Action<DecoBuilder>>(StringComparer.Ordinal);
            Office(r); Canteen(r); Medical(r); Military(r); Industrial(r); Lab(r); Residential(r); Home(r); Resort(r);
            ThemePark(r); Cryo(r); Spaceport(r); Prison(r); Ship(r); Mine(r); Reaver(r); Civic(r); Alien(r); Precursor(r);
            Farm(r); Factory(r); Vehicles(r); Rail(r); Aviation(r);
            return _all = r;
        }
    }

    // ---- shared shapes ------------------------------------------------------------------------

    /// <summary>A swivel chair's five-star base on casters, with its gas column up to <paramref name="seatY"/>.</summary>
    static void StarBase(DecoBuilder b, float r, float seatY)
    {
        for (int i = 0; i < 5; i++)
        {
            float a = i * MathF.Tau / 5 + 0.3f;
            float x = MathF.Cos(a) * r, z = MathF.Sin(a) * r;
            b.Beam(0, 0.06f, 0, x, 0.04f, z, 0.04f, DecoSlot.Dark);
            b.Ball(x, 0.03f, z, 0.03f, DecoSlot.Rubber, 6).Ghost();
        }
        b.Cyl(0, 0.06f, 0, 0.03f, seatY - 0.06f, DecoSlot.Metal, 6);
    }

    /// <summary>
    /// A row of books on a shelf between x0 and x1, the faces of what bounds it (the case's sides,
    /// taller than any book), in assorted colours and heights. Most stand upright side by side. Now
    /// and then a run leans over from the last upright book (or from the side, when the shelf starts
    /// with it): each book rests against the one before, its foot on the shelf, at the angle at which
    /// it touches. Now and then a few lie flat in a stack. Across a gap nothing leans: it would lean on air.
    /// </summary>
    static void Books(DecoBuilder b, float x0, float x1, float y, float z0, float z1, float maxH)
    {
        DecoSlot[] covers = { DecoSlot.Trim, DecoSlot.Fabric, DecoSlot.Paper, DecoSlot.Alert, DecoSlot.Dark, DecoSlot.Wood };
        DecoSlot Cover() => covers[b.Rng.Int(covers.Length)];
        float x = x0;
        // What a leaning book would rest on: its face and how tall it stands. The side to begin with, then
        // the last upright book; nothing (0) after a gap or a stack.
        float holdX = x0, holdH = maxH * 10;
        while (x < x1 - 0.03f)
        {
            float w = b.Rng.Range(0.025f, 0.06f), h = maxH * b.Rng.Range(0.65f, 1f);
            if (x + w > x1) break;
            if (b.Rng.Chance(0.05f) && x + maxH + 0.02f < x1)
            {
                // A stack lying flat, each on the one below.
                int n = 2 + b.Rng.Int(3);
                float sy = y, len = maxH * b.Rng.Range(0.65f, 0.95f), x2 = x + 0.01f + len;
                for (int i = 0; i < n; i++)
                {
                    float t = b.Rng.Range(0.025f, 0.05f), l = len * b.Rng.Range(0.85f, 1f);
                    b.Box(x + 0.01f, sy, z0 + b.Rng.Range(0f, 0.03f), x + 0.01f + l, sy + t, z1 - 0.01f, Cover()).Soft().Ghost().As(DecoFinish.Paper);
                    sy += t;
                }
                x = x2 + 0.01f;
                holdH = 0;
                continue;
            }
            if (holdH > 0 && b.Rng.Chance(0.12f))
            {
                // A run leaning over: the first against what stands at x, the rest each against the one before.
                float gap = b.Rng.Range(0.04f, 0.1f);
                float angle = MathF.Asin(MathF.Min(gap / h, MathF.Sin(40 * DecoGeo.Deg)));
                // Its top corner touches a support that reaches that high; a shorter one it rests on by its top edge.
                if (holdH < h * MathF.Cos(angle)) angle = MathF.Atan2(gap, holdH);
                float foot = holdX + (holdH < h * MathF.Cos(angle) ? holdH * MathF.Tan(angle) : h * MathF.Sin(angle));
                int run = 1 + b.Rng.Int(3);
                for (int i = 0; i < run; i++)
                {
                    float bw = i == 0 ? w : b.Rng.Range(0.025f, 0.06f), bh = i == 0 ? h : MathF.Min(h, maxH * b.Rng.Range(0.65f, 1f));
                    if (foot + bw * MathF.Cos(angle) > x1) break;
                    // Pivot on its foot (the corner nearest the support), turned until it lies against the support.
                    using (b.Turn(new System.Numerics.Vector3(foot, y, 0), 0, 0, angle / DecoGeo.Deg))
                        b.Box(foot, y, z0 + 0.02f, foot + bw, y + bh, z1 - 0.02f, Cover()).Soft().Ghost().As(DecoFinish.Paper);
                    x = foot + bw * MathF.Cos(angle) + 0.004f;
                    // The next lies along this one's face: its foot where that face meets the shelf.
                    foot += bw / MathF.Cos(angle);
                }
                holdH = 0;
                continue;
            }
            b.Box(x, y, z0 + b.Rng.Range(0f, 0.04f), x + w, y + h, z1 - 0.01f, Cover()).Soft().Ghost().As(DecoFinish.Paper);
            holdX = x + w;
            holdH = h;
            x += w + 0.004f;
            if (b.Rng.Chance(0.08f)) { x += 0.08f; holdH = 0; }
        }
    }

    /// <summary>Small papers or a scatter of litter on a surface.</summary>
    static void Papers(DecoBuilder b, float cx, float y, float cz, int n, float spread)
    {
        for (int i = 0; i < n; i++)
        {
            float x = cx + b.Rng.Range(-spread, spread), z = cz + b.Rng.Range(-spread, spread);
            using (b.Turn(new System.Numerics.Vector3(x, y, z), 0, b.Rng.Range(-40, 40)))
                b.Box(x - 0.105f, y, z - 0.15f, x + 0.105f, y + 0.006f, z + 0.15f, DecoSlot.Paper).Soft().Ghost();
            y += 0.006f;
        }
    }

    /// <summary>A chunky screen panel: a housing with a glowing face on its -Z side and a few dark text lines.</summary>
    static void ScreenPanel(DecoBuilder b, float x0, float y0, float x1, float y1, float z, float depth, DecoSlot housing, bool lines = true)
    {
        b.Box(x0, y0, z, x1, y1, z + depth, housing);
        float m = MathF.Min(x1 - x0, y1 - y0) * 0.08f;
        b.Box(x0 + m, y0 + m, z - 0.006f, x1 - m, y1 - m, z, DecoSlot.Screen).Screen();
        if (!lines) return;
        int n = 3 + b.Rng.Int(2);
        for (int i = 0; i < n; i++)
        {
            float ly = y1 - m - (i + 1) * (y1 - y0 - 2 * m) / (n + 1);
            float lx = x0 + m * 2 + (x1 - x0 - 4 * m) * b.Rng.Range(0.35f, 0.9f);
            b.Box(x0 + m * 2, ly - 0.006f, z - 0.009f, lx, ly + 0.006f, z - 0.006f, DecoSlot.Dark).Ghost();
        }
        b.Light((x0 + x1) / 2, (y0 + y1) / 2, z - 0.15f, DecoSlot.Screen);
    }

    /// <summary>A ring of short beams (a hoop, a wheel rim, an orbit) around <paramref name="c"/>, tilted about X then Z.</summary>
    static void Ring(DecoBuilder b, System.Numerics.Vector3 c, float r, int n, float w, DecoSlot s, float tiltX = 0, float tiltZ = 0, bool ghost = false)
    {
        var m = DecoGeo.RotX(tiltX) * DecoGeo.RotZ(tiltZ);
        for (int i = 0; i < n; i++)
        {
            float a0 = i * MathF.Tau / n, a1 = (i + 1) * MathF.Tau / n;
            var p0 = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(MathF.Cos(a0) * r, 0, MathF.Sin(a0) * r), m) + c;
            var p1 = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(MathF.Cos(a1) * r, 0, MathF.Sin(a1) * r), m) + c;
            DecoPart part = b.Beam(p0, p1, w, w, s);
            if (ghost) part.Ghost();
        }
    }

    /// <summary>A wheel on an axle along X: a tyre, a round steel rim the full width of it (what's left when the tyre has gone), and a hubcap at each end.</summary>
    static void Wheel(DecoBuilder b, float x, float y, float z, float r, float width, DecoSlot tyre = DecoSlot.Rubber, DecoSlot hub = DecoSlot.Metal)
    {
        // The tyre's lowest corner sits exactly r below the axle, so a wheel at y = r touches the ground.
        DecoPart t = b.Tube(x - width / 2, y, z, x + width / 2, y, z, r, tyre, 14).Tyre();
        float lift = (y - r) - DecoEmit.Vertices(t).Min(v => v.Y);
        t.M *= DecoGeo.Move(0, lift, 0);
        DecoPart h = b.Tube(x - width / 2 - 0.003f, y, z, x + width / 2 + 0.003f, y, z, r * 0.6f, hub, 12).Ghost();
        h.M *= DecoGeo.Move(0, lift, 0);
        DecoPart cap = b.Tube(x - width / 2 - 0.006f, y, z, x + width / 2 + 0.006f, y, z, r * 0.22f, DecoSlot.Chrome, 8).Ghost();
        cap.M *= DecoGeo.Move(0, lift, 0);
    }

    /// <summary>A round gauge facing -Z: a pale face in a dark rim, with a needle.</summary>
    static void Dial(DecoBuilder b, float x, float y, float z, float r)
    {
        b.Tube(x, y, z + 0.02f, x, y, z - 0.01f, r, DecoSlot.Dark, 8).Ghost();
        b.Tube(x, y, z - 0.01f, x, y, z - 0.014f, r * 0.82f, DecoSlot.Paper, 8).Ghost();
        b.Beam(new System.Numerics.Vector3(x, y, z - 0.016f), new System.Numerics.Vector3(x + r * 0.55f, y + r * 0.4f, z - 0.016f), 0.008f, 0.004f, DecoSlot.Alert).Ghost();
    }

    /// <summary>Small lit buttons in a grid on a face at z (facing -Z).</summary>
    static void Buttons(DecoBuilder b, float x0, float y0, float x1, float y1, float z, int cols, int rows, params DecoSlot[] slots)
    {
        float sx = (x1 - x0) / cols, sy = (y1 - y0) / rows;
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                DecoSlot s = slots[(i * 7 + j * 3 + b.Rng.Int(2)) % slots.Length];
                float cx = x0 + sx * (i + 0.5f), cy = y0 + sy * (j + 0.5f);
                DecoPart p = b.Box(cx - sx * 0.3f, cy - sy * 0.3f, z - 0.012f, cx + sx * 0.3f, cy + sy * 0.3f, z, s).Ghost();
                if (DecoPalette.Glows(s)) p.Lit();
            }
    }

    /// <summary>A plant's leafy crown: a few low-poly balls of foliage around a point.</summary>
    static void Foliage(DecoBuilder b, float cx, float cy, float cz, float r, int n)
    {
        b.Ball(cx, cy, cz, r * 0.75f, r * 0.6f, r * 0.75f, DecoSlot.Plant, 6).Rots();
        for (int i = 0; i < n; i++)
        {
            float a = i * MathF.Tau / n + b.Rng.Range(0, 0.6f);
            float d = r * b.Rng.Range(0.45f, 0.65f);
            b.Ball(cx + MathF.Cos(a) * d, cy + b.Rng.Range(-r * 0.3f, r * 0.35f), cz + MathF.Sin(a) * d, r * 0.5f, r * 0.38f, r * 0.5f, DecoSlot.Plant, 6).Rots().Ghost();
        }
    }
}
