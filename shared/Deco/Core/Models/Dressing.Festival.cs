using System;
using System.Collections.Generic;
using System.Numerics;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>The happiest day a world had, stopped: bunting, lanterns, banners, stalls, stands, a stage, floats, and what the crowd dropped.</summary>
public static partial class DecoDressing
{
    /// <summary>A span's two posts, by era, each standing on its own ground: their tops.</summary>
    static (Vector3 L, Vector3 R) SpanPosts(DecoBuilder b, DressArgs a, float h)
    {
        float half = a.Length / 2;
        Vector3 l = Post(b, a.Era, -half, a.LeftY, 0, h + R(b, -0.15f, 0.15f));
        Vector3 r = Post(b, a.Era, half, a.RightY, 0, h + R(b, -0.15f, 0.15f));
        return (l, r);
    }

    static float SpanHeight(DecoTech era) => era switch { DecoTech.Low => 4.6f, DecoTech.Mid => 5.2f, _ => 5.4f };

    static void Festival(Dictionary<string, Action<DecoBuilder, DressArgs>> r)
    {
        r["bunting"] = (b, a) =>
        {
            bool prayer = a.Style == "prayer";
            var (tl, tr) = SpanPosts(b, a, SpanHeight(a.Era));
            int lines = prayer ? 1 + (b.Rng.Chance(0.4f) ? 1 : 0) : 1 + (b.Rng.Chance(0.65f) ? 1 : 0);
            for (int k = 0; k < lines; k++)
            {
                var a0 = new Vector3(tl.X + 0.06f, tl.Y - 0.12f - k * 0.75f, 0);
                var a1 = new Vector3(tr.X - 0.06f, tr.Y - 0.12f - k * (prayer ? 0.75f : 0.2f), 0);
                bool broken = b.Rng.Chance(a.Wear * 0.4f);
                if (broken && b.Rng.Chance(0.5f)) (a0, a1) = (a1, a0);
                float slack = 0.04f + 0.035f * k + a.Wear * 0.03f, w = prayer ? 0.3f : 0.26f, len = prayer ? 0.36f : 0.32f;
                int offset = b.Rng.Int(4);
                Line(b, a0, a1, slack, 0.008f, prayer ? DecoSlot.Linen : DecoSlot.Dark, broken, prayer ? 0.38f : 0.36f,
                    (at, dir, i) =>
                    {
                        if (b.Rng.Chance(a.Wear * 0.3f)) return; // rotted off
                        Vector3 half = dir * (w / 2);
                        Panel(b, at - half, at + half, -Vector3.UnitY * len * R(b, 0.85f, 1.05f), Vector3.UnitZ, 0.008f, ClothOf(i + offset), prayer ? 1f : 0f);
                    },
                    (at, dir, i) =>
                    {
                        Vector3 half = dir * (w / 2), across = Vector3.Cross(Vector3.UnitY, dir) * len;
                        Panel(b, at - half + Vector3.UnitY * 0.006f, at + half + Vector3.UnitY * 0.006f, across, Vector3.UnitY, 0.006f, ClothOf(i + offset), prayer ? 1f : 0f);
                    });
            }
        };

        r["lanterns"] = (b, a) =>
        {
            var (tl, tr) = SpanPosts(b, a, SpanHeight(a.Era) - 0.2f);
            bool broken = b.Rng.Chance(a.Wear * 0.35f);
            var a0 = new Vector3(tl.X + 0.06f, tl.Y - 0.15f, 0);
            var a1 = new Vector3(tr.X - 0.06f, tr.Y - 0.15f, 0);
            float step = a.Era switch { DecoTech.Low => 0.95f, DecoTech.Mid => 0.5f, _ => 0.7f };
            Line(b, a0, a1, 0.06f + a.Wear * 0.03f, 0.008f, DecoSlot.Dark, broken, step,
                (at, dir, i) =>
                {
                    switch (a.Era)
                    {
                        case DecoTech.Low:
                            // A paper lantern on a short string, a dark cap top and bottom.
                            if (b.Rng.Chance(a.Wear * 0.35f)) return;
                            b.Tube(at, at - Vector3.UnitY * 0.12f, 0.006f, DecoSlot.Dark).Ghost();
                            Vector3 c = at - Vector3.UnitY * 0.33f;
                            b.Ball(c.X, c.Y, c.Z, 0.15f, 0.2f, 0.15f, ClothOf(i), 10).Soft().Ghost().As(DecoFinish.Paper);
                            b.Cyl(c.X, c.Y + 0.17f, c.Z, 0.07f, 0.04f, DecoSlot.Dark, 8).Ghost();
                            b.Cyl(c.X, c.Y - 0.21f, c.Z, 0.07f, 0.04f, DecoSlot.Dark, 8).Ghost();
                            break;
                        case DecoTech.Mid:
                            // Festoon bulbs on their sockets.
                            b.Cyl(at.X, at.Y - 0.07f, at.Z, 0.018f, 0.07f, DecoSlot.Dark, 6).Ghost();
                            if (!b.Rng.Chance(a.Wear * 0.4f)) b.Ball(at.X, at.Y - 0.11f, at.Z, 0.04f, 0.05f, 0.04f, DecoSlot.Lamp, 8).Lit().Ghost();
                            break;
                        default:
                            // Glow orbs threaded on the line, and the line itself a light ribbon.
                            b.Ball(at.X, at.Y - 0.06f, at.Z, 0.08f, DecoSlot.Lamp, 10).Lit().Ghost();
                            break;
                    }
                },
                (at, dir, i) =>
                {
                    if (a.Era == DecoTech.Low) b.Ball(at.X, 0.09f, at.Z, 0.16f, 0.09f, 0.14f, ClothOf(i), 8).Soft().Ghost().As(DecoFinish.Paper);
                    else b.Ball(at.X, 0.05f, at.Z, 0.05f, DecoSlot.Lamp, 6).Ghost();
                });
            if (a.Era == DecoTech.High && !broken) b.Cable(a0 - Vector3.UnitY * 0.02f, a1 - Vector3.UnitY * 0.02f, 0.012f, DecoSlot.Lamp, 0.06f + a.Wear * 0.03f);
        };

        r["banner"] = (b, a) =>
        {
            float h = a.Era == DecoTech.Low ? 4.2f : 4.8f;
            Vector3 top = Post(b, a.Era, 0, 0, 0, h, 1.2f);
            string emblem = a.Style.Length > 0 ? a.Style : Emblem(b);
            bool fallen = b.Rng.Chance(a.Wear * 0.35f);
            float bw = 0.85f, bh = 2.0f, z = -0.09f;
            switch (a.Era)
            {
                case DecoTech.Low: b.Box(-0.5f, top.Y - 0.12f, -0.12f, 0.5f, top.Y - 0.04f, -0.06f, DecoSlot.Wood).As(DecoFinish.Wood); break;
                case DecoTech.Mid:
                    // Two arms off the column, the banner held taut between them.
                    foreach (float y in new[] { top.Y - 0.25f, top.Y - 0.25f - bh }) b.Tube(0, y, -0.03f, 0, y, -0.13f, 0.02f, DecoSlot.Metal, 6);
                    b.Tube(-0.48f, top.Y - 0.25f, -0.11f, 0.48f, top.Y - 0.25f, -0.11f, 0.02f, DecoSlot.Metal, 6);
                    b.Tube(-0.48f, top.Y - 0.25f - bh, -0.11f, 0.48f, top.Y - 0.25f - bh, -0.11f, 0.02f, DecoSlot.Metal, 6);
                    break;
                default:
                    // A holo-banner: a rigid panel off the mast that glows while its cell holds.
                    b.Box(-0.03f, top.Y - 2.35f, -0.12f, 0.03f, top.Y - 0.2f, -0.06f, DecoSlot.Plastic);
                    b.Box(-0.45f, top.Y - 2.3f, -0.11f, 0.45f, top.Y - 0.25f, -0.1f, DecoSlot.Screen).Lit();
                    Symbol(b, emblem, 0, top.Y - 1.25f, -0.112f, 0.6f, DecoSlot.Linen);
                    b.Light(0, top.Y - 1.2f, -0.5f, DecoSlot.Screen);
                    return;
            }
            if (fallen)
            {
                // Torn from its ties: lying in a heap at the post's foot.
                Panel(b, new Vector3(-0.45f, 0.012f, -0.2f), new Vector3(0.45f, 0.012f, -0.2f), new Vector3(R(b, -0.2f, 0.2f), 0, -1.4f), Vector3.UnitY, 0.01f, DecoSlot.Fabric);
                return;
            }
            float y0 = top.Y - (a.Era == DecoTech.Low ? 0.12f : 0.25f);
            float tattered = a.Wear > 0.5f ? R(b, 0.6f, 0.95f) : 1f;
            Panel(b, new Vector3(-bw / 2, y0, z - 0.02f), new Vector3(bw / 2, y0, z - 0.02f), -Vector3.UnitY * bh * tattered, Vector3.UnitZ, 0.012f, DecoSlot.Fabric);
            // A band of the accent along its foot, and the emblem.
            if (tattered > 0.9f) Panel(b, new Vector3(-bw / 2, y0 - bh * 0.82f, z - 0.03f), new Vector3(bw / 2, y0 - bh * 0.82f, z - 0.03f), -Vector3.UnitY * bh * 0.12f, Vector3.UnitZ, 0.004f, DecoSlot.Trim);
            Symbol(b, emblem, 0, y0 - bh * 0.4f, z - 0.04f, 0.6f, DecoSlot.Linen);
        };

        r["banner_span"] = (b, a) =>
        {
            var (tl, tr) = SpanPosts(b, a, SpanHeight(a.Era) - 0.3f);
            string emblem = a.Style.Length > 0 ? a.Style : Emblem(b);
            var a0 = new Vector3(tl.X + 0.08f, tl.Y - 0.2f, 0);
            var a1 = new Vector3(tr.X - 0.08f, tr.Y - 0.2f, 0);
            if (b.Rng.Chance(a.Wear * 0.4f))
            {
                // One end let go: the cloth hangs down the other post in a long twist.
                b.Hang(a0, a1 - a0, 0.01f, DecoSlot.Dark, 1.5f);
                Panel(b, a0 + new Vector3(0.05f, 0, -0.02f), a0 + new Vector3(0.75f, 0, -0.02f), -Vector3.UnitY * (a0.Y - 0.05f), Vector3.UnitZ, 0.01f, DecoSlot.Fabric);
                return;
            }
            b.Cable(a0, a1, 0.01f, DecoSlot.Dark, 0.012f);
            var pts = Along(SpanOf(a0, a1, 0.012f), 0.5f, 0.6f);
            float depth = 0.85f;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                if (pts[i].At.X > a1.X - 0.6f) break;
                Panel(b, pts[i].At - Vector3.UnitY * 0.01f, pts[i + 1].At - Vector3.UnitY * 0.01f, -Vector3.UnitY * depth, Vector3.UnitZ, 0.012f, DecoSlot.Fabric);
            }
            if (pts.Count > 2)
            {
                Vector3 mid = pts[pts.Count / 2].At - Vector3.UnitY * (depth * 0.5f);
                Symbol(b, emblem, mid.X, mid.Y, -0.01f, 0.62f, DecoSlot.Trim);
                using (b.Turn(new Vector3(mid.X, mid.Y, 0), 0, 180)) Symbol(b, emblem, mid.X, mid.Y, -0.01f, 0.62f, DecoSlot.Trim);
            }
        };

        r["stall"] = (b, a) =>
        {
            float w = R(b, 2.2f, 2.8f), d = 1.5f, front = 2.2f, back = 1.85f;
            bool collapsed = b.Rng.Chance(a.Wear * 0.45f);
            if (a.Era == DecoTech.High)
            {
                // A kiosk pod: a rounded counter, a canopy on two slim posts, a menu panel.
                b.Frustum(0, -w / 2, w / 2, -d / 2, d / 2, 1.05f, -w / 2 + 0.05f, w / 2 - 0.05f, -d / 2 + 0.05f, d / 2, DecoSlot.Plastic);
                b.Box(-w / 2, 1.05f, -d / 2 - 0.05f, w / 2, 1.1f, d / 2, DecoSlot.Body);
                b.Box(-w / 2 + 0.05f, 0.9f, -d / 2 - 0.01f, w / 2 - 0.05f, 0.94f, -d / 2 + 0.02f, DecoSlot.Lamp).Lit().Ghost();
                foreach (float x in new[] { -w / 2 + 0.15f, w / 2 - 0.15f }) b.Cyl(x, 1.1f, d / 2 - 0.15f, 0.04f, 1.25f, DecoSlot.Plastic, 8);
                if (collapsed) b.Wedge(-w / 2 - 0.1f, 1.1f, -d / 2 - 0.3f, w / 2 + 0.1f, d / 2, 1.16f, 2.4f, DecoSlot.Plastic);
                else b.Wedge(-w / 2 - 0.2f, 2.3f, -d / 2 - 0.5f, w / 2 + 0.2f, d / 2 + 0.05f, 2.36f, 2.48f, DecoSlot.Plastic);
                b.Box(-0.5f, 1.35f, d / 2 - 0.1f, 0.5f, 2.0f, d / 2 - 0.06f, DecoSlot.Screen).Lit();
                Symbol(b, "cup", 0, 1.67f, d / 2 - 0.1f, 0.45f, DecoSlot.Linen);
                b.Light(0, 2.1f, -0.2f);
                return;
            }
            DecoSlot frame = a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Metal;
            foreach (float x in new[] { -w / 2 + 0.05f, w / 2 - 0.05f })
            {
                if (a.Era == DecoTech.Low) { b.Box(x - 0.04f, 0, -d / 2 - 0.04f, x + 0.04f, front, -d / 2 + 0.04f, frame).As(DecoFinish.Wood); b.Box(x - 0.04f, 0, d / 2 - 0.04f, x + 0.04f, back, d / 2 + 0.04f, frame).As(DecoFinish.Wood); }
                else { b.Cyl(x, 0, -d / 2, 0.025f, front, frame, 6); b.Cyl(x, 0, d / 2, 0.025f, back, frame, 6); }
            }
            // The counter at the front, its board down to the ground.
            b.Box(-w / 2, 0.86f, -d / 2 - 0.05f, w / 2, 0.92f, -d / 2 + 0.55f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Body);
            b.Box(-w / 2 + 0.06f, 0.05f, -d / 2 - 0.03f, w / 2 - 0.06f, 0.86f, -d / 2, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Trim);
            // What it sold, gone hard on the counter.
            for (int i = 0, n = 3 + b.Rng.Int(4); i < n; i++)
            {
                float x = R(b, -w / 2 + 0.25f, w / 2 - 0.25f), z = R(b, -d / 2 + 0.1f, -d / 2 + 0.4f);
                switch (b.Rng.Int(3))
                {
                    case 0: b.Cyl(x, 0.92f, z, R(b, 0.08f, 0.13f), R(b, 0.06f, 0.12f), i % 2 == 0 ? DecoSlot.Linen : DecoSlot.Paper, 10).Rots(); break;
                    case 1: b.Cyl(x, 0.92f, z, 0.14f, 0.12f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Plastic, 10); break;
                    default: b.Ball(x, 0.97f, z, 0.07f, 0.05f, 0.05f, DecoSlot.Paper, 6).Rots(); break;
                }
            }
            // The canopy in stripes, front to back; fallen, it lies across the counter.
            int strips = Math.Max(5, (int)(w / 0.32f));
            for (int i = 0; i < strips; i++)
            {
                float x0 = -w / 2 + w * i / strips, x1 = -w / 2 + w * (i + 1) / strips;
                DecoSlot s = i % 2 == 0 ? DecoSlot.Fabric : DecoSlot.Linen;
                float yf = collapsed ? 0.95f : front + 0.05f, yb = back + 0.05f;
                if (collapsed && b.Rng.Chance(0.3f)) continue;
                b.Hull(new[] { new Vector3(x0, yf, -d / 2 - 0.25f), new Vector3(x1, yf, -d / 2 - 0.25f), new Vector3(x0, yf + 0.02f, -d / 2 - 0.25f), new Vector3(x1, yf + 0.02f, -d / 2 - 0.25f),
                    new Vector3(x0, yb, d / 2 + 0.1f), new Vector3(x1, yb, d / 2 + 0.1f), new Vector3(x0, yb + 0.02f, d / 2 + 0.1f), new Vector3(x1, yb + 0.02f, d / 2 + 0.1f) }, s).Soft().Ghost().As(DecoFinish.Fabric);
                if (!collapsed) Panel(b, new Vector3(x0, yf, -d / 2 - 0.26f), new Vector3(x1, yf, -d / 2 - 0.26f), -Vector3.UnitY * 0.2f, Vector3.UnitZ, 0.006f, s, 0.35f);
            }
            if (a.Era == DecoTech.Mid && !collapsed)
            {
                b.Tube(0, front - 0.05f, -0.2f, 0, front - 0.35f, -0.2f, 0.006f, DecoSlot.Dark).Ghost();
                b.Ball(0, front - 0.4f, -0.2f, 0.05f, DecoSlot.Lamp, 8).Lit().Ghost();
                b.Light(0, front - 0.5f, -0.2f);
            }
        };

        r["grandstand"] = (b, a) =>
        {
            float w = R(b, 7f, 8.5f);
            int rows = 4;
            const float rise = 0.45f, step = 0.9f;
            float z0 = -rows * step / 2;
            switch (a.Era)
            {
                case DecoTech.High:
                    // Stepped composite tiers, seat pods along them, a cantilevered canopy with a light strip.
                    for (int i = 0; i < rows; i++)
                    {
                        float y = rise * (i + 1), z = z0 + i * step;
                        b.Box(-w / 2, 0, z, w / 2, y, z + step, DecoSlot.Plastic);
                        for (float x = -w / 2 + 0.4f; x < w / 2 - 0.3f; x += 0.6f)
                            if (!b.Rng.Chance(a.Wear * 0.2f)) b.Frustum(y, x - 0.22f, x + 0.22f, z + 0.15f, z + 0.6f, y + 0.4f, x - 0.2f, x + 0.2f, z + 0.5f, z + 0.62f, DecoSlot.Body);
                    }
                    foreach (float x in new[] { -w / 2 + 0.2f, w / 2 - 0.2f })
                    {
                        b.Box(x - 0.1f, rise * rows, -z0 - 0.3f, x + 0.1f, rise * rows + 2.6f, -z0, DecoSlot.Plastic);
                        b.Beam(new Vector3(x, rise * rows + 2.6f, -z0 - 0.15f), new Vector3(x, rise * rows + 2.3f, z0 - 0.3f), 0.15f, 0.15f, DecoSlot.Plastic);
                    }
                    b.Box(-w / 2, rise * rows + 2.25f, z0 - 0.4f, w / 2, rise * rows + 2.4f, -z0, DecoSlot.Plastic);
                    b.Box(-w / 2, rise * rows + 2.2f, z0 - 0.42f, w / 2, rise * rows + 2.25f, z0 - 0.36f, DecoSlot.Lamp).Lit().Ghost();
                    b.Box(-w / 2, rise - 0.12f, z0 - 0.02f, w / 2, rise - 0.08f, z0, DecoSlot.Lamp).Lit().Ghost();
                    b.Light(0, rise * rows + 2f, z0);
                    break;
                default:
                {
                    // Seats and footboards stepping up: on timber trestles, or on a scaffold of tubes.
                    bool low = a.Era == DecoTech.Low;
                    DecoSlot frame = low ? DecoSlot.Wood : DecoSlot.Metal, seat = low ? DecoSlot.Wood : DecoSlot.Body;
                    for (int i = 0; i < rows; i++)
                    {
                        float y = rise * (i + 1), z = z0 + i * step;
                        for (float x = -w / 2 + 0.1f; x <= w / 2; x += w / 4)
                        {
                            float xx = MathF.Min(x, w / 2 - 0.1f);
                            if (low) { b.Box(xx - 0.05f, 0, z + 0.55f, xx + 0.05f, y, z + 0.65f, frame).As(DecoFinish.Wood); b.Box(xx - 0.04f, 0, z + 0.05f, xx + 0.04f, y - rise + 0.02f, z + 0.13f, frame).As(DecoFinish.Wood); }
                            else { b.Cyl(xx, 0, z + 0.6f, 0.025f, y + 0.02f, frame, 6); b.Cyl(xx, 0, z + 0.1f, 0.025f, y - rise + 0.04f, frame, 6); }
                        }
                        // The seat plank, then the footboard in front of it at the row below's height.
                        float gap = 0f;
                        for (float x = -w / 2; x < w / 2 - 0.01f; x += 2f)
                        {
                            float x1 = MathF.Min(x + 2f, w / 2);
                            if (b.Rng.Chance(a.Wear * 0.25f)) { gap++; continue; } // a plank gone
                            b.Box(x, y, z + 0.4f, x1, y + 0.05f, z + 0.75f, seat).As(low ? DecoFinish.Wood : DecoFinish.Paint);
                        }
                        b.Box(-w / 2, y - rise + 0.02f, z, w / 2, y - rise + 0.06f, z + 0.35f, frame).As(low ? DecoFinish.Wood : DecoFinish.Steel);
                        if (!low) b.Tube(-w / 2, y - 0.1f, z + 0.6f, w / 2, y - 0.1f, z + 0.6f, 0.02f, frame, 6);
                    }
                    // The rail along the top and the front, and bunting along the front.
                    float top = rise * rows;
                    foreach (float x in new[] { -w / 2, w / 2 })
                    {
                        if (low) b.Box(x - 0.04f, top, -z0 - 0.15f, x + 0.04f, top + 1f, -z0 - 0.07f, frame);
                        else b.Cyl(x, top, -z0 - 0.1f, 0.025f, 1f, frame, 6);
                        if (low) b.Box(x - 0.04f, 0, z0 - 0.3f, x + 0.04f, 1.1f, z0 - 0.22f, frame);
                        else b.Cyl(x, 0, z0 - 0.26f, 0.025f, 1.1f, frame, 6);
                    }
                    b.Box(-w / 2, top + 0.95f, -z0 - 0.15f, w / 2, top + 1f, -z0 - 0.07f, frame);
                    var f0 = new Vector3(-w / 2 + 0.05f, 1.05f, z0 - 0.26f);
                    var f1 = new Vector3(w / 2 - 0.05f, 1.05f, z0 - 0.26f);
                    Line(b, f0, f1, 0.03f, 0.007f, DecoSlot.Dark, b.Rng.Chance(a.Wear * 0.3f), 0.34f,
                        (at, dir, i) => { if (!b.Rng.Chance(a.Wear * 0.3f)) Panel(b, at - dir * 0.13f, at + dir * 0.13f, -Vector3.UnitY * 0.28f, Vector3.UnitZ, 0.006f, ClothOf(i), 0f); });
                    break;
                }
            }
            // What the crowd left on the seats.
            for (int i = 0, n = 2 + b.Rng.Int(4); i < n; i++)
            {
                int row = b.Rng.Int(rows);
                float y = rise * (row + 1) + (a.Era == DecoTech.High ? 0f : 0.05f), z = z0 + row * step + 0.55f, x = R(b, -w / 2 + 0.3f, w / 2 - 0.3f);
                if (a.Era == DecoTech.High) { y = rise * (row + 1); z = z0 + row * step + 0.1f; }
                if (b.Rng.Chance(0.5f)) b.Cyl(x, y, z, 0.04f, 0.1f, DecoSlot.Paper, 8).Ghost();
                else b.Box(x - 0.12f, y, z - 0.08f, x + 0.12f, y + 0.015f, z + 0.08f, ClothOf(i)).Soft().Ghost();
            }
        };

        r["stage"] = (b, a) =>
        {
            float w = R(b, 4.6f, 5.6f), d = 3.4f, deck = a.Era == DecoTech.Low ? 0.9f : 1.1f;
            string emblem = a.Style.Length > 0 ? a.Style : Emblem(b);
            // The deck on its legs, steps at one side.
            foreach (float x in new[] { -w / 2 + 0.1f, 0f, w / 2 - 0.1f })
                foreach (float z in new[] { -d / 2 + 0.1f, d / 2 - 0.1f })
                    b.Box(x - 0.06f, 0, z - 0.06f, x + 0.06f, deck, z + 0.06f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Dark);
            b.Box(-w / 2, deck - 0.08f, -d / 2, w / 2, deck, d / 2, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Dark).As(a.Era == DecoTech.Low ? DecoFinish.Wood : DecoFinish.Plastic);
            b.Box(-w / 2, 0.05f, -d / 2 - 0.02f, w / 2, deck - 0.08f, -d / 2, a.Era == DecoTech.Low ? DecoSlot.Fabric : DecoSlot.Dark).Soft();
            for (int s = 0; s < 3; s++) b.Box(w / 2, 0, -d / 2 + 0.3f + s * 0.0f, w / 2 + 0.3f * (3 - s), deck * (s + 1) / 4f, -d / 2 + 1.3f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Metal);
            if (a.Era == DecoTech.High) b.Box(-w / 2, deck - 0.05f, -d / 2 - 0.02f, w / 2, deck, -d / 2, DecoSlot.Lamp).Lit().Ghost();
            // The backdrop: a cloth with the emblem on a frame, a truss, or a holo-screen.
            float top = deck + 3.2f;
            switch (a.Era)
            {
                case DecoTech.Low:
                    foreach (float x in new[] { -w / 2 + 0.1f, w / 2 - 0.1f }) b.Box(x - 0.06f, deck, d / 2 - 0.15f, x + 0.06f, top, d / 2 - 0.03f, DecoSlot.Wood).As(DecoFinish.Wood);
                    b.Box(-w / 2 + 0.05f, top - 0.12f, d / 2 - 0.15f, w / 2 - 0.05f, top, d / 2 - 0.03f, DecoSlot.Wood).As(DecoFinish.Wood);
                    Panel(b, new Vector3(-w / 2 + 0.2f, top - 0.12f, d / 2 - 0.2f), new Vector3(w / 2 - 0.2f, top - 0.12f, d / 2 - 0.2f), -Vector3.UnitY * (top - deck - 0.3f) * (a.Wear > 0.6f ? 0.6f : 1f), Vector3.UnitZ, 0.01f, DecoSlot.Fabric);
                    Symbol(b, emblem, 0, deck + 1.9f, d / 2 - 0.21f, 1.3f, DecoSlot.Trim);
                    break;
                case DecoTech.Mid:
                    // A box truss over the deck with lamp cans on it, speaker stacks either side.
                    foreach (float x in new[] { -w / 2 + 0.15f, w / 2 - 0.15f })
                        foreach (float dx in new[] { -0.12f, 0.12f })
                            foreach (float dz in new[] { -0.12f, 0.12f }) b.Cyl(x + dx, deck, d / 2 - 0.3f + dz, 0.018f, top - deck, DecoSlot.Metal, 6);
                    foreach (float dy in new[] { -0.12f, 0.12f })
                        foreach (float dz in new[] { -0.12f, 0.12f }) b.Tube(-w / 2 + 0.15f, top - 0.15f + dy, d / 2 - 0.3f + dz, w / 2 - 0.15f, top - 0.15f + dy, d / 2 - 0.3f + dz, 0.018f, DecoSlot.Metal, 6);
                    for (float x = -w / 2 + 0.8f; x < w / 2 - 0.5f; x += 1.1f)
                    {
                        b.Tube(x, top - 0.3f, d / 2 - 0.3f, x, top - 0.55f, d / 2 - 0.3f, 0.12f, DecoSlot.Dark, 10);
                        b.Tube(x, top - 0.55f, d / 2 - 0.3f, x, top - 0.56f, d / 2 - 0.3f, 0.1f, DecoSlot.Lamp, 10).Lit().Ghost();
                    }
                    Panel(b, new Vector3(-w / 2 + 0.4f, top - 0.35f, d / 2 - 0.1f), new Vector3(w / 2 - 0.4f, top - 0.35f, d / 2 - 0.1f), -Vector3.UnitY * 2.2f, Vector3.UnitZ, 0.01f, DecoSlot.Fabric);
                    Symbol(b, emblem, 0, deck + 1.8f, d / 2 - 0.11f, 1.2f, DecoSlot.Trim);
                    foreach (float x in new[] { -w / 2 - 0.5f, w / 2 + 1.4f })
                        for (int k = 0; k < 2; k++)
                        {
                            float y0 = k * 0.95f;
                            b.Box(x - 0.4f, y0, -d / 2 + 0.4f, x + 0.4f, y0 + 0.92f, -d / 2 + 1.1f, DecoSlot.Dark);
                            b.Tube(x, y0 + 0.5f, -d / 2 + 0.4f, x, y0 + 0.5f, -d / 2 + 0.37f, 0.25f, DecoSlot.Rubber, 12).Ghost();
                        }
                    break;
                default:
                    b.Box(-w / 2 + 0.2f, deck, d / 2 - 0.3f, w / 2 - 0.2f, top, d / 2 - 0.1f, DecoSlot.Plastic);
                    b.Box(-w / 2 + 0.35f, deck + 0.3f, d / 2 - 0.32f, w / 2 - 0.35f, top - 0.2f, d / 2 - 0.3f, DecoSlot.Screen).Lit();
                    Symbol(b, emblem, 0, deck + 1.8f, d / 2 - 0.33f, 1.4f, DecoSlot.Linen);
                    foreach (float x in new[] { -w / 2 - 0.4f, w / 2 + 1.3f }) b.Cyl(x, 0, -d / 2 + 0.7f, 0.25f, 2.4f, DecoSlot.Plastic, 12, 0.6f);
                    b.Light(0, deck + 1.8f, d / 2 - 1.2f, DecoSlot.Screen);
                    break;
            }
            // What the band left on the deck: a drum, a stand.
            b.Cyl(-0.8f, deck, 0.4f, 0.3f, 0.3f, DecoSlot.Trim, 12);
            b.Cyl(-0.8f, deck + 0.3f, 0.4f, 0.29f, 0.01f, DecoSlot.Linen, 12).Ghost();
            b.Cyl(0.6f, deck, -0.6f, 0.12f, 0.02f, DecoSlot.Dark, 8);
            b.Cyl(0.6f, deck, -0.6f, 0.012f, 1.5f, DecoSlot.Metal, 6);
        };

        r["float"] = (b, a) =>
        {
            // Along +z: a parade float stopped in the road, its skirt, its great figure, its masts.
            float len = R(b, 6.5f, 8f), w = 2.6f, bed;
            bool lost = b.Rng.Chance(a.Wear * 0.45f);
            switch (a.Era)
            {
                case DecoTech.Low:
                {
                    // A wagon: spoked wheels, a timber bed, its draw-pole and yoke down on the road.
                    bed = 1.05f;
                    foreach (float z in new[] { -len / 2 + 1f, len / 2 - 1f })
                        foreach (float x in new[] { -w / 2 - 0.05f, w / 2 + 0.05f })
                        {
                            const float wr = 0.55f;
                            for (int i = 0; i < 12; i++)
                            {
                                float a0 = i * MathF.Tau / 12, a1 = (i + 1) * MathF.Tau / 12;
                                Rod(b, new Vector3(x, wr + MathF.Sin(a0) * wr, z + MathF.Cos(a0) * wr), new Vector3(x, wr + MathF.Sin(a1) * wr, z + MathF.Cos(a1) * wr), 0.07f, DecoSlot.Wood);
                            }
                            for (int i = 0; i < 4; i++) { float an = i * MathF.PI / 4; Rod(b, new Vector3(x, wr - MathF.Sin(an) * wr * 0.9f, z - MathF.Cos(an) * wr * 0.9f), new Vector3(x, wr + MathF.Sin(an) * wr * 0.9f, z + MathF.Cos(an) * wr * 0.9f), 0.035f, DecoSlot.Wood); }
                            b.Tube(x - 0.08f, wr, z, x + 0.08f, wr, z, 0.09f, DecoSlot.Dark, 8);
                        }
                    b.Box(-w / 2 + 0.1f, 0.5f, -len / 2 + 0.5f, w / 2 - 0.1f, bed - 0.12f, len / 2 - 0.5f, DecoSlot.Wood).As(DecoFinish.Wood);
                    Rod(b, new Vector3(0, 0.55f, -len / 2 + 0.3f), new Vector3(0, 0.05f, -len / 2 - 1.8f), 0.08f, DecoSlot.Wood);
                    b.Box(-0.7f, 0, -len / 2 - 1.95f, 0.7f, 0.1f, -len / 2 - 1.8f, DecoSlot.Wood);
                    break;
                }
                case DecoTech.Mid:
                {
                    // A lorry under it: the cab in front, tyres, the bed dressed over its chassis.
                    bed = 1.2f;
                    foreach (float z in new[] { -len / 2 + 0.9f, len / 2 - 1.4f, len / 2 - 0.5f })
                        foreach (float x in new[] { -w / 2 + 0.25f, w / 2 - 0.25f })
                        {
                            b.Tube(x - 0.15f, 0.48f, z, x + 0.15f, 0.48f, z, 0.48f, DecoSlot.Rubber, 14).Tyre();
                            b.Tube(x - 0.16f, 0.48f, z, x + 0.16f, 0.48f, z, 0.25f, DecoSlot.Metal, 10).Ghost();
                        }
                    b.Box(-w / 2 + 0.2f, 0.75f, -len / 2 + 0.2f, w / 2 - 0.2f, 1.0f, len / 2 - 0.2f, DecoSlot.Dark);
                    b.Box(-w / 2 + 0.1f, 0.9f, -len / 2, w / 2 - 0.1f, 2.6f, -len / 2 + 1.6f, DecoSlot.Body);
                    b.Box(-w / 2 + 0.2f, 2.0f, -len / 2 - 0.01f, w / 2 - 0.2f, 2.45f, -len / 2 + 0.02f, DecoSlot.Dark).Ghost();
                    break;
                }
                default:
                {
                    // A hover-float down on its pads: a long rounded body, a light strip along it.
                    bed = 1.0f;
                    foreach (float z in new[] { -len / 2 + 1f, len / 2 - 1f })
                        foreach (float x in new[] { -w / 2 + 0.4f, w / 2 - 0.4f })
                            b.Cyl(x, 0, z, 0.38f, 0.32f, DecoSlot.Dark, 12, 0.8f);
                    b.Barrel(-len / 2, len / 2, 0, 0.3f, w / 2, 0.38f, DecoSlot.Plastic, 16);
                    b.Box(-w / 2 - 0.01f, 0.62f, -len / 2 + 0.4f, w / 2 + 0.01f, 0.67f, len / 2 - 0.4f, DecoSlot.Lamp).Lit().Ghost();
                    break;
                }
            }
            // The bed: behind the cab on a lorry, the whole length otherwise.
            float z0 = a.Era == DecoTech.Mid ? -len / 2 + 1.7f : -len / 2 + 0.3f, z1 = a.Era == DecoTech.Mid ? len / 2 - 0.2f : len / 2 - 0.3f;
            b.Box(-w / 2, bed - 0.12f, z0, w / 2, bed, z1, a.Era == DecoTech.High ? DecoSlot.Plastic : DecoSlot.Wood);
            // The skirt round the bed, in stripes, to a hand off the road.
            if (a.Era != DecoTech.High)
            {
                int n = (int)((z1 - z0) / 0.4f);
                for (int i = 0; i < n; i++)
                {
                    if (b.Rng.Chance(a.Wear * 0.35f)) continue;
                    float za = z0 + (z1 - z0) * i / n, zb = z0 + (z1 - z0) * (i + 1) / n;
                    foreach (float x in new[] { -w / 2 - 0.02f, w / 2 + 0.02f })
                        Panel(b, new Vector3(x, bed, za), new Vector3(x, bed, zb), -Vector3.UnitY * (bed - 0.25f), Vector3.UnitX, 0.01f, i % 2 == 0 ? DecoSlot.Fabric : DecoSlot.Trim);
                }
            }
            // Its figure: a great head, a flower, an arch of rings, or the emblem on a disc.
            float cz = (z0 + z1) / 2;
            int kind = b.Rng.Int(4);
            Vector3 fall = new(w / 2 + 1.3f, 0, cz + R(b, -1f, 1f));
            float fy = lost ? 0f : bed;
            float fx = lost ? fall.X : 0f;
            switch (kind)
            {
                case 0:
                {
                    float hr = 1.05f, hy = lost ? hr * 0.9f : bed + 0.4f + hr;
                    if (!lost) b.Cyl(0, bed, cz, 0.35f, 0.5f, DecoSlot.Trim, 10);
                    b.Ball(fx, hy, cz, hr, hr * 1.05f, hr, DecoSlot.Body, 14);
                    foreach (float s in new[] { -0.38f, 0.38f }) b.Tube(fx + s, hy + 0.25f, cz - hr * 0.88f, fx + s, hy + 0.25f, cz - hr * 0.96f, 0.15f, DecoSlot.Dark, 10).Ghost();
                    b.Box(fx - 0.35f, hy - 0.45f, cz - hr * 0.86f, fx + 0.35f, hy - 0.33f, cz - hr * 0.8f, DecoSlot.Alert).Ghost();
                    if (!lost)
                        for (int i = 0; i < 5; i++) { float an = i * MathF.Tau / 5; b.Cyl(MathF.Cos(an) * 0.55f, hy + hr * 0.85f, cz + MathF.Sin(an) * 0.55f, 0.16f, 0.6f, DecoSlot.Gold, 6, 0f); }
                    break;
                }
                case 1:
                {
                    float sy = lost ? 0.3f : bed + 2.3f;
                    if (!lost) b.Cyl(0, bed, cz, 0.12f, 2.3f, DecoSlot.Plant, 8);
                    b.Ball(fx, sy, cz, 0.45f, DecoSlot.Signal, 10);
                    for (int i = 0; i < 8; i++)
                    {
                        float an = i * MathF.Tau / 8;
                        if (lost) b.Ball(fx + MathF.Cos(an) * 0.8f, 0.2f, cz + MathF.Sin(an) * 0.8f, 0.5f, 0.2f, 0.35f, ClothOf(i % 2 == 0 ? 0 : 1), 8);
                        else b.Ball(MathF.Cos(an) * 0.8f, sy + MathF.Sin(an) * 0.8f, cz - 0.05f, 0.5f, 0.35f, 0.18f, ClothOf(i % 2 == 0 ? 0 : 1), 8);
                    }
                    break;
                }
                case 2:
                    // Arches of hoops along the bed, flags on top.
                    for (int k = -1; k <= 1; k++)
                    {
                        float zz = cz + k * 1.2f;
                        for (int i = 0; i < 8; i++)
                        {
                            float a0 = i * MathF.PI / 8, a1 = (i + 1) * MathF.PI / 8;
                            Rod(b, new Vector3(MathF.Cos(a0) * 1.1f, bed + MathF.Sin(a0) * 1.6f, zz), new Vector3(MathF.Cos(a1) * 1.1f, bed + MathF.Sin(a1) * 1.6f, zz), 0.1f, k == 0 ? DecoSlot.Trim : DecoSlot.Fabric);
                        }
                        if (!lost) Panel(b, new Vector3(0, bed + 1.6f, zz), new Vector3(0, bed + 2.2f, zz), new Vector3(0, 0, 0.5f), Vector3.UnitX, 0.01f, ClothOf(k + 1));
                    }
                    break;
                default:
                {
                    float dy = lost ? 0.15f : bed + 1.7f;
                    if (lost) { using (b.Turn(new Vector3(fx, 0.15f, cz), 90, 0)) { b.Tube(fx, 0.15f, cz - 0.08f, fx, 0.15f, cz + 0.08f, 1.4f, DecoSlot.Body, 18); } }
                    else
                    {
                        b.Box(-0.1f, bed, cz - 0.1f, 0.1f, bed + 0.5f, cz + 0.1f, DecoSlot.Dark);
                        b.Tube(0, dy, cz - 0.08f, 0, dy, cz + 0.08f, 1.4f, DecoSlot.Body, 18);
                        Symbol(b, a.Style.Length > 0 ? a.Style : Emblem(b), 0, dy, cz - 0.08f, 2f, DecoSlot.Trim);
                        using (b.Turn(new Vector3(0, dy, cz), 0, 180)) Symbol(b, a.Style.Length > 0 ? a.Style : Emblem(b), 0, dy, cz - 0.08f, 2f, DecoSlot.Trim);
                    }
                    break;
                }
            }
            // Masts at the ends, bunting between them over the figure.
            var m0 = Post(b, a.Era, 0, bed, z0 + 0.15f, 3.4f, 0.8f);
            var m1 = Post(b, a.Era, 0, bed, z1 - 0.15f, 3.4f, 0.8f);
            Line(b, m0 - Vector3.UnitY * 0.1f, m1 - Vector3.UnitY * 0.1f, 0.05f, 0.007f, DecoSlot.Dark, b.Rng.Chance(a.Wear * 0.35f), 0.36f,
                (at, dir, i) => { if (!b.Rng.Chance(a.Wear * 0.3f)) Panel(b, at - dir * 0.13f, at + dir * 0.13f, -Vector3.UnitY * 0.3f, Vector3.UnitX, 0.006f, ClothOf(i), 0f); });
        };

        r["confetti"] = (b, a) =>
        {
            // Paper on the ground: squares of every colour, streamers, a few cups. Rotted thin on an old world.
            float spread = R(b, 0.8f, 1.6f);
            int n = (int)(R(b, 45, 80) * (1f - a.Wear * 0.5f));
            for (int i = 0; i < n; i++)
            {
                float x = R(b, -spread, spread), z = R(b, -spread, spread), s = R(b, 0.035f, 0.06f);
                if (x * x + z * z > spread * spread) continue;
                using (b.Turn(new Vector3(x, 0, z), 0, R(b, 0, 90)))
                    b.Box(x - s / 2, 0, z - s / 2, x + s / 2, 0.004f, z + s / 2, i % 5 == 4 ? DecoSlot.Paper : ClothOf(i)).Soft().Ghost().As(DecoFinish.Paper);
            }
            for (int k = 0, m = 2 + b.Rng.Int(4); k < m; k++)
            {
                var p = new Vector3(R(b, -spread, spread), 0.004f, R(b, -spread, spread));
                float an = R(b, 0, MathF.Tau);
                for (int j = 0; j < 5; j++)
                {
                    an += R(b, -0.9f, 0.9f);
                    var q = p + new Vector3(MathF.Cos(an), 0, MathF.Sin(an)) * 0.22f;
                    Flat(b, p, q, Vector3.UnitY, 0.03f, 0.003f, ClothOf(k + 1)).Soft().Ghost();
                    p = q;
                }
            }
            for (int k = 0, m = b.Rng.Int(3); k < m; k++)
            {
                float x = R(b, -spread, spread), z = R(b, -spread, spread), an = R(b, 0, MathF.Tau);
                b.Tube(new Vector3(x, 0.045f, z), new Vector3(x + MathF.Cos(an) * 0.1f, 0.045f, z + MathF.Sin(an) * 0.1f), 0.045f, DecoSlot.Paper, 8, 0.75f).Ghost();
            }
        };

        r["instruments"] = (b, a) =>
        {
            int n = 2 + b.Rng.Int(3);
            for (int i = 0; i < n; i++)
            {
                float x = R(b, -0.9f, 0.9f), z = R(b, -0.9f, 0.9f), yaw = R(b, 0, 360);
                using (b.Turn(new Vector3(x, 0, z), 0, yaw))
                    switch ((i + b.Rng.Int(4)) % 4)
                    {
                        case 0:
                            // A drum on its side.
                            if (a.Era == DecoTech.High)
                            {
                                b.Cyl(x, 0, z, 0.3f, 0.06f, DecoSlot.Plastic, 14);
                                b.Cyl(x, 0.06f, z, 0.24f, 0.005f, DecoSlot.Lamp, 14).Lit().Ghost();
                                break;
                            }
                            b.Tube(x - 0.17f, 0.27f, z, x + 0.17f, 0.27f, z, 0.27f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Trim, 14);
                            b.Tube(x - 0.175f, 0.27f, z, x + 0.175f, 0.27f, z, 0.25f, DecoSlot.Linen, 14).Ghost();
                            foreach (float s in new[] { -0.17f, 0.17f }) b.Tube(x + s - 0.015f, 0.27f, z, x + s + 0.015f, 0.27f, z, 0.285f, a.Era == DecoTech.Low ? DecoSlot.Bronze : DecoSlot.Chrome, 14).Ghost();
                            break;
                        case 1:
                            // A horn lying with its bell turned up a little.
                            DecoSlot brass = a.Era == DecoTech.High ? DecoSlot.Plastic : DecoSlot.Bronze;
                            b.Tube(x - 0.35f, 0.04f, z, x + 0.15f, 0.04f, z, 0.025f, brass, 8);
                            b.Tube(x + 0.15f, 0.06f, z, x + 0.45f, 0.13f, z, 0.03f, brass, 12, 4f);
                            if (a.Era == DecoTech.High) for (int k = 0; k < 3; k++) b.Box(x - 0.2f + k * 0.08f, 0.06f, z - 0.01f, x - 0.17f + k * 0.08f, 0.075f, z + 0.01f, DecoSlot.Lamp).Lit().Ghost();
                            break;
                        case 2:
                            // A cymbal, one edge on the ground.
                            using (b.Turn(new Vector3(x, 0, z), 12, 0)) b.Cyl(x, 0.02f, z, 0.22f, 0.01f, DecoSlot.Gold, 14, 0.2f);
                            break;
                        default:
                            if (a.Era == DecoTech.Low)
                            {
                                // A fiddle and its bow.
                                b.Box(x - 0.1f, 0, z - 0.18f, x + 0.1f, 0.06f, z + 0.18f, DecoSlot.Wood).As(DecoFinish.Wood);
                                b.Box(x - 0.02f, 0.02f, z + 0.18f, x + 0.02f, 0.045f, z + 0.42f, DecoSlot.Dark);
                                b.Box(x + 0.15f, 0, z - 0.3f, x + 0.17f, 0.012f, z + 0.3f, DecoSlot.Wood).Ghost();
                            }
                            else if (a.Era == DecoTech.Mid)
                            {
                                b.Box(x - 0.18f, 0, z - 0.5f, x + 0.18f, 0.12f, z + 0.5f, DecoSlot.Dark);
                                b.Box(x - 0.19f, 0.03f, z - 0.05f, x + 0.19f, 0.09f, z + 0.05f, DecoSlot.Chrome).Ghost();
                            }
                            else
                            {
                                b.Box(x - 0.15f, 0, z - 0.45f, x + 0.15f, 0.05f, z + 0.45f, DecoSlot.Plastic);
                                b.Box(x - 0.1f, 0.05f, z - 0.4f, x + 0.1f, 0.055f, z + 0.3f, DecoSlot.Screen).Lit().Ghost();
                            }
                            break;
                    }
            }
        };

        r["costumes"] = (b, a) =>
        {
            int n = 2 + b.Rng.Int(3);
            for (int i = 0; i < n; i++)
            {
                float x = R(b, -0.9f, 0.9f), z = R(b, -0.9f, 0.9f);
                using (b.Turn(new Vector3(x, 0, z), 0, R(b, 0, 360)))
                    switch (b.Rng.Int(5))
                    {
                        case 0:
                            // A tall hat, fallen on its side, its brim on the ground.
                            using (b.Turn(new Vector3(x, 0.17f, z), 0, 0, 80))
                            {
                                b.Cyl(x, 0.17f - 0.02f, z, 0.17f, 0.02f, ClothOf(i), 14);
                                b.Cyl(x, 0.17f, z, 0.1f, 0.3f, ClothOf(i + 1), 12, 0.9f);
                            }
                            break;
                        case 1:
                            // A mask: a pale face, dark eyes, a ribbon.
                            b.Cyl(x, 0, z, 0.11f, 0.03f, DecoSlot.Linen, 12, 0.85f);
                            foreach (float s in new[] { -0.04f, 0.04f }) b.Cyl(x + s, 0.03f, z - 0.02f, 0.022f, 0.004f, DecoSlot.Dark, 8).Ghost();
                            Flat(b, new Vector3(x + 0.1f, 0.004f, z), new Vector3(x + 0.4f, 0.004f, z + 0.12f), Vector3.UnitY, 0.03f, 0.004f, DecoSlot.Trim).Soft().Ghost();
                            if (a.Era == DecoTech.High) b.Box(x - 0.08f, 0.03f, z + 0.04f, x + 0.08f, 0.034f, z + 0.055f, DecoSlot.Lamp).Lit().Ghost();
                            break;
                        case 2:
                            // A heap of costume cloth.
                            b.Dome(x, 0, z, R(b, 0.3f, 0.45f), R(b, 0.1f, 0.18f), R(b, 0.25f, 0.4f), ClothOf(i), 10).Soft();
                            b.Dome(x + 0.15f, 0, z + 0.1f, 0.22f, 0.12f, 0.2f, ClothOf(i + 2), 8).Soft().Ghost();
                            break;
                        case 3:
                            // A cape spread where it fell.
                            b.Box(x - 0.45f, 0, z - 0.6f, x + 0.45f, 0.015f, z + 0.6f, ClothOf(i)).Soft().Ghost();
                            b.Box(x - 0.35f, 0.015f, z - 0.2f, x + 0.25f, 0.03f, z + 0.3f, ClothOf(i)).Soft().Ghost();
                            b.Box(x - 0.45f, 0.002f, z - 0.62f, x + 0.45f, 0.016f, z - 0.55f, DecoSlot.Gold).Ghost();
                            break;
                        default:
                            // A plumed cap: a crown and a fan of feathers.
                            b.Cyl(x, 0, z, 0.12f, 0.1f, ClothOf(i), 12);
                            for (int k = 0; k < 4; k++) Flat(b, new Vector3(x, 0.08f, z), new Vector3(x + 0.4f, 0.02f + k * 0.05f, z - 0.2f + k * 0.13f), Vector3.UnitY, 0.06f, 0.006f, DecoSlot.Signal).Soft().Ghost();
                            break;
                    }
            }
        };

        r["scarves"] = (b, a) =>
        {
            // A rail with the supporters' scarves over it, little flags stuck in the ground beside.
            float w = R(b, 2.2f, 3f), y = 1.05f;
            foreach (float x in new[] { -w / 2, w / 2 }) Post(b, a.Era, x, 0, 0, y + 0.05f, 0.7f);
            switch (a.Era)
            {
                case DecoTech.Low: b.Box(-w / 2, y - 0.05f, -0.04f, w / 2, y + 0.04f, 0.04f, DecoSlot.Wood).As(DecoFinish.Wood); break;
                case DecoTech.Mid: b.Tube(-w / 2, y, 0, w / 2, y, 0, 0.03f, DecoSlot.Metal, 8); break;
                default: b.Box(-w / 2, y - 0.04f, -0.04f, w / 2, y + 0.04f, 0.04f, DecoSlot.Plastic); b.Box(-w / 2, y - 0.045f, -0.045f, w / 2, y - 0.03f, 0.045f, DecoSlot.Lamp).Lit().Ghost(); break;
            }
            int n = 2 + b.Rng.Int(4);
            for (int i = 0; i < n; i++)
            {
                float x = -w / 2 + 0.3f + (w - 0.6f) * (i + 0.5f) / n + R(b, -0.1f, 0.1f);
                float l1 = R(b, 0.45f, 0.8f), l2 = R(b, 0.35f, 0.7f);
                for (int k = 0; k < 4; k++)
                {
                    DecoSlot s = k % 2 == 0 ? DecoSlot.Fabric : DecoSlot.Trim;
                    Panel(b, new Vector3(x - 0.08f, y + 0.04f - l1 * k / 4f, -0.05f), new Vector3(x + 0.08f, y + 0.04f - l1 * k / 4f, -0.05f), -Vector3.UnitY * l1 / 4f, Vector3.UnitZ, 0.012f, s);
                    Panel(b, new Vector3(x - 0.08f, y + 0.04f - l2 * k / 4f, 0.05f), new Vector3(x + 0.08f, y + 0.04f - l2 * k / 4f, 0.05f), -Vector3.UnitY * l2 / 4f, Vector3.UnitZ, 0.012f, s);
                }
                b.Box(x - 0.08f, y + 0.03f, -0.05f, x + 0.08f, y + 0.06f, 0.05f, DecoSlot.Fabric).Soft().Ghost();
            }
            for (int i = 0, m = 1 + b.Rng.Int(3); i < m; i++)
            {
                float x = R(b, -w / 2, w / 2), z = R(b, -0.9f, -0.4f), lean = R(b, -15, 15);
                using (b.Turn(new Vector3(x, 0, z), lean, 0, R(b, -10, 10)))
                {
                    b.Box(x - 0.01f, -0.1f, z - 0.01f, x + 0.01f, 0.7f, z + 0.01f, DecoSlot.Wood).Ghost();
                    Panel(b, new Vector3(x, 0.7f, z), new Vector3(x + 0.3f, 0.7f, z), -Vector3.UnitY * 0.2f, Vector3.UnitZ, 0.004f, i % 2 == 0 ? DecoSlot.Fabric : DecoSlot.Trim);
                }
            }
        };

        r["ticket_booth"] = (b, a) =>
        {
            float w = 1.5f, d = 1.4f, h = 2.4f;
            switch (a.Era)
            {
                case DecoTech.High:
                    b.Cyl(0, 0, 0, 0.8f, h, DecoSlot.Plastic, 16);
                    b.Dome(0, h, 0, 0.82f, 0.35f, 0.82f, DecoSlot.Body, 14);
                    b.Box(-0.5f, 1.1f, -0.82f, 0.5f, 1.8f, -0.7f, DecoSlot.Screen).Lit();
                    Symbol(b, "star", 0, 1.45f, -0.83f, 0.4f, DecoSlot.Linen);
                    b.Box(-0.55f, 0.95f, -0.95f, 0.55f, 1.0f, -0.75f, DecoSlot.Plastic);
                    b.Light(0, 1.5f, -1.2f, DecoSlot.Screen);
                    break;
                default:
                {
                    bool low = a.Era == DecoTech.Low;
                    DecoSlot wall = low ? DecoSlot.Wood : DecoSlot.Body;
                    // Walls round a hatch at the front; a roof over.
                    b.Box(-w / 2, 0, d / 2 - 0.06f, w / 2, h, d / 2, wall);
                    b.Box(-w / 2, 0, -d / 2, -w / 2 + 0.06f, h, d / 2, wall);
                    b.Box(w / 2 - 0.06f, 0, -d / 2, w / 2, h, d / 2, wall);
                    b.Box(-w / 2, 0, -d / 2, w / 2, 1.0f, -d / 2 + 0.06f, wall);
                    b.Box(-w / 2, 1.75f, -d / 2, w / 2, h, -d / 2 + 0.06f, wall);
                    b.Box(-w / 2 - 0.05f, 1.0f, -d / 2 - 0.2f, w / 2 + 0.05f, 1.05f, -d / 2 + 0.06f, low ? DecoSlot.Wood : DecoSlot.Metal);
                    if (low) b.Wedge(-w / 2 - 0.15f, h, -d / 2 - 0.25f, w / 2 + 0.15f, d / 2 + 0.15f, h + 0.1f, h + 0.5f, DecoSlot.Wood);
                    else
                    {
                        b.Box(-w / 2 - 0.05f, h, -d / 2 - 0.05f, w / 2 + 0.05f, h + 0.12f, d / 2 + 0.05f, DecoSlot.Metal);
                        for (int i = 0; i < 6; i++)
                            Panel(b, new Vector3(-w / 2 + w * i / 6f, 1.95f, -d / 2 - 0.02f), new Vector3(-w / 2 + w * (i + 1) / 6f, 1.95f, -d / 2 - 0.02f), new Vector3(0, -0.15f, -0.4f), Vector3.UnitY, 0.01f, i % 2 == 0 ? DecoSlot.Fabric : DecoSlot.Linen);
                    }
                    // The ticket roll on the counter, a star over the hatch.
                    b.Tube(0.3f, 1.12f, -d / 2 - 0.08f, 0.45f, 1.12f, -d / 2 - 0.08f, 0.07f, DecoSlot.Paper, 10).Rots();
                    Symbol(b, "star", 0, 2.1f, -d / 2 - 0.005f, 0.35f, DecoSlot.Trim);
                    break;
                }
            }
        };

        r["belongings"] = (b, a) =>
        {
            string style = a.Style;
            int n = 2 + b.Rng.Int(3);
            for (int i = 0; i < n; i++)
            {
                float x = R(b, -1f, 1f), z = R(b, -1f, 1f);
                using (b.Turn(new Vector3(x, 0, z), 0, R(b, 0, 360)))
                    switch (style)
                    {
                        case "masks":
                            if (a.Era == DecoTech.Low)
                            {
                                // A beaked mask of leather, its round eyes dark, and a hood.
                                b.Ball(x, 0.07f, z, 0.1f, 0.07f, 0.12f, DecoSlot.Canvas, 8);
                                b.Tube(x, 0.06f, z - 0.08f, x, 0.03f, z - 0.36f, 0.06f, DecoSlot.Canvas, 8, 0.15f);
                                foreach (float s in new[] { -0.045f, 0.045f }) b.Cyl(x + s, 0.12f, z - 0.04f, 0.03f, 0.02f, DecoSlot.Dark, 10).Ghost();
                                if (i == 0) b.Dome(x + 0.5f, 0, z, 0.35f, 0.1f, 0.3f, DecoSlot.Dark, 8).Soft();
                                break;
                            }
                            // A gas mask lying face up: the face, its eyes and its filter; a sleek respirator on a high world.
                            b.Ball(x, 0.06f, z, 0.11f, 0.06f, 0.13f, a.Era == DecoTech.High ? DecoSlot.Plastic : DecoSlot.Rubber, 10);
                            if (a.Era == DecoTech.High) b.Box(x - 0.09f, 0.1f, z - 0.06f, x + 0.09f, 0.115f, z - 0.03f, DecoSlot.Lamp).Lit().Ghost();
                            else foreach (float s in new[] { -0.045f, 0.045f }) b.Cyl(x + s, 0.1f, z - 0.03f, 0.035f, 0.025f, DecoSlot.Dark, 10).Ghost();
                            b.Tube(x, 0.06f, z + 0.1f, x, 0.06f, z + 0.2f, 0.05f, a.Era == DecoTech.High ? DecoSlot.Body : DecoSlot.Metal, 10);
                            if (i == 0) b.Box(x + 0.3f, 0, z - 0.2f, x + 0.7f, 0.25f, z + 0.1f, a.Era == DecoTech.High ? DecoSlot.Plastic : DecoSlot.Body);
                            break;
                        case "camp":
                            if (i == 0)
                            {
                                // A fire ring of stones and charred ends.
                                for (int k = 0; k < 9; k++) { float an = k * MathF.Tau / 9; b.Ball(x + MathF.Cos(an) * 0.35f, 0.05f, z + MathF.Sin(an) * 0.35f, 0.09f, 0.07f, 0.08f, DecoSlot.Stone, 6); }
                                b.Cyl(x, 0, z, 0.28f, 0.012f, DecoSlot.Ash, 10).Ghost();
                                for (int k = 0; k < 3; k++) Rod(b, new Vector3(x + R(b, -0.2f, 0.2f), 0.04f, z - 0.2f), new Vector3(x + R(b, -0.2f, 0.2f), 0.04f, z + 0.2f), 0.06f, DecoSlot.Char);
                                break;
                            }
                            // A bedroll: a wool blanket rolled and strapped, a sleeping bag, a thermal pod with its strip.
                            switch (a.Era)
                            {
                                case DecoTech.Low:
                                    b.Tube(x - 0.4f, 0.14f, z, x + 0.4f, 0.14f, z, 0.14f, i % 2 == 0 ? DecoSlot.Canvas : DecoSlot.Fabric, 10);
                                    foreach (float s in new[] { -0.2f, 0.2f }) b.Tube(x + s - 0.02f, 0.14f, z, x + s + 0.02f, 0.14f, z, 0.15f, DecoSlot.Dark, 10).Ghost();
                                    break;
                                case DecoTech.Mid:
                                    b.Box(x - 0.38f, 0, z - 0.95f, x + 0.38f, 0.08f, z + 0.95f, i % 2 == 0 ? DecoSlot.Signal : DecoSlot.Fabric).Soft();
                                    b.Tube(x - 0.38f, 0.12f, z - 0.85f, x + 0.38f, 0.12f, z - 0.85f, 0.12f, i % 2 == 0 ? DecoSlot.Signal : DecoSlot.Fabric, 10).Soft();
                                    break;
                                default:
                                    b.Frustum(0, x - 0.4f, x + 0.4f, z - 1f, z + 1f, 0.35f, x - 0.3f, x + 0.3f, z - 0.9f, z + 0.9f, DecoSlot.Plastic);
                                    b.Box(x - 0.41f, 0.15f, z - 1f, x + 0.41f, 0.18f, z + 1f, DecoSlot.Lamp).Lit().Ghost();
                                    break;
                            }
                            break;
                        case "books":
                            if (i == 0)
                            {
                                // A barrow, trolley or sled stacked with books.
                                float by = a.Era == DecoTech.High ? 0.25f : 0.5f;
                                if (a.Era == DecoTech.Low) { b.Tube(x - 0.55f, 0.35f, z, x + 0.55f, 0.35f, z, 0.35f, DecoSlot.Wood, 12); Rod(b, new Vector3(x, by, z + 0.4f), new Vector3(x, 0.03f, z + 1.3f), 0.05f, DecoSlot.Wood); }
                                else if (a.Era == DecoTech.Mid) { foreach (float sx in new[] { -0.3f, 0.3f }) foreach (float sz in new[] { -0.35f, 0.35f }) { b.Cyl(x + sx, 0, z + sz, 0.05f, 0.08f, DecoSlot.Rubber, 8); b.Cyl(x + sx, 0.08f, z + sz, 0.015f, by - 0.08f, DecoSlot.Metal, 6); } }
                                else foreach (float sx in new[] { -0.32f, 0.32f }) b.Box(x + sx - 0.04f, 0, z - 0.45f, x + sx + 0.04f, by, z + 0.45f, DecoSlot.Plastic);
                                b.Box(x - 0.4f, by, z - 0.5f, x + 0.4f, by + 0.05f, z + 0.5f, a.Era == DecoTech.Low ? DecoSlot.Wood : a.Era == DecoTech.Mid ? DecoSlot.Metal : DecoSlot.Plastic);
                                for (int k = 0; k < 6; k++) b.Box(x - 0.35f + (k % 3) * 0.24f, by + 0.05f, z - 0.4f + (k / 3) * 0.4f, x - 0.15f + (k % 3) * 0.24f, by + 0.05f + R(b, 0.15f, 0.35f), z - 0.1f + (k / 3) * 0.4f, ClothOf(k)).As(DecoFinish.Paper);
                                break;
                            }
                            for (int k = 0, m = 2 + b.Rng.Int(4); k < m; k++) b.Box(x - 0.12f, k * 0.04f, z - 0.17f, x + 0.12f, k * 0.04f + 0.035f, z + 0.17f, ClothOf(k + i)).Ghost().As(DecoFinish.Paper);
                            break;
                        default:
                            switch ((i + b.Rng.Int(4)) % 4)
                            {
                                case 0:
                                    // A seat, knocked over now and then.
                                    bool over = b.Rng.Chance(0.5f);
                                    using (b.Turn(new Vector3(x, 0, z + 0.2f), over ? -90 : 0, 0))
                                    {
                                        if (a.Era == DecoTech.Low) { b.Legs(x - 0.18f, z - 0.18f, x + 0.18f, z + 0.18f, 0.42f, 0.04f, DecoSlot.Wood); b.Box(x - 0.2f, 0.42f, z - 0.2f, x + 0.2f, 0.46f, z + 0.2f, DecoSlot.Wood).As(DecoFinish.Wood); }
                                        else if (a.Era == DecoTech.Mid)
                                        {
                                            Rod(b, new Vector3(x - 0.2f, 0, z - 0.2f), new Vector3(x - 0.2f, 0.85f, z + 0.18f), 0.02f, DecoSlot.Metal);
                                            Rod(b, new Vector3(x + 0.2f, 0, z - 0.2f), new Vector3(x + 0.2f, 0.85f, z + 0.18f), 0.02f, DecoSlot.Metal);
                                            Rod(b, new Vector3(x - 0.2f, 0, z + 0.2f), new Vector3(x - 0.2f, 0.45f, z - 0.15f), 0.02f, DecoSlot.Metal);
                                            Rod(b, new Vector3(x + 0.2f, 0, z + 0.2f), new Vector3(x + 0.2f, 0.45f, z - 0.15f), 0.02f, DecoSlot.Metal);
                                            b.Box(x - 0.21f, 0.43f, z - 0.2f, x + 0.21f, 0.46f, z + 0.15f, DecoSlot.Fabric).Soft();
                                            b.Box(x - 0.21f, 0.55f, z + 0.12f, x + 0.21f, 0.85f, z + 0.16f, DecoSlot.Fabric).Soft();
                                        }
                                        else { b.Cyl(x, 0, z, 0.2f, 0.05f, DecoSlot.Plastic, 12); b.Cyl(x, 0.05f, z, 0.04f, 0.35f, DecoSlot.Plastic, 8); b.Dome(x, 0.4f, z, 0.24f, 0.12f, 0.24f, DecoSlot.Body, 12); }
                                    }
                                    break;
                                case 1:
                                    // A bag with its strap.
                                    if (a.Era == DecoTech.Low) b.Barrel(z - 0.25f, z + 0.25f, x, 0, 0.16f, 0.13f, DecoSlot.Fabric, 10);
                                    else b.Box(x - 0.22f, 0, z - 0.1f, x + 0.22f, 0.3f, z + 0.1f, a.Era == DecoTech.Mid ? DecoSlot.Dark : DecoSlot.Plastic);
                                    b.Hang(new Vector3(x - 0.15f, 0.2f, z - 0.1f), new Vector3(-1, 0, -1), 0.012f, DecoSlot.Dark, 0.4f);
                                    break;
                                case 2:
                                    // A flask and a cup.
                                    b.Tube(x - 0.13f, 0.045f, z, x + 0.13f, 0.045f, z, 0.045f, a.Era == DecoTech.Low ? DecoSlot.Bronze : a.Era == DecoTech.Mid ? DecoSlot.Chrome : DecoSlot.Plastic, 10);
                                    b.Cyl(x + 0.25f, 0, z + 0.15f, 0.04f, 0.09f, DecoSlot.Linen, 8);
                                    break;
                                default:
                                    // A pram: wicker on wheels, a hooded pram, or a pod down on its pads.
                                    if (a.Era == DecoTech.High) { b.Cyl(x, 0, z, 0.25f, 0.08f, DecoSlot.Dark, 12); b.Dome(x, 0.08f, z, 0.3f, 0.45f, 0.4f, DecoSlot.Plastic, 12); break; }
                                    foreach (float sx in new[] { -0.25f, 0.25f })
                                        foreach (float sz in new[] { -0.3f, 0.3f }) b.Tube(x + sx - 0.02f, 0.16f, z + sz, x + sx + 0.02f, 0.16f, z + sz, 0.16f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Rubber, 10);
                                    b.Box(x - 0.23f, 0.3f, z - 0.4f, x + 0.23f, 0.65f, z + 0.4f, a.Era == DecoTech.Low ? DecoSlot.Canvas : DecoSlot.Body);
                                    if (a.Era == DecoTech.Mid) b.Dome(x, 0.65f, z + 0.25f, 0.23f, 0.3f, 0.2f, DecoSlot.Fabric, 10).Soft();
                                    break;
                            }
                            break;
                    }
            }
        };
    }
}
