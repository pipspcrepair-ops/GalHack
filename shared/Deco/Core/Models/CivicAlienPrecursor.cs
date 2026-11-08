using System;
using System.Collections.Generic;
using System.Numerics;
using static GalHack.Deco.DecoSlot;

namespace GalHack.Deco;

public static partial class DecoModels
{
    static void Civic(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["pew"] = b =>
        {
            foreach (float x in new[] { -0.95f, 0.95f })
            {
                b.Box(x - 0.035f, 0, -0.32f, x + 0.035f, 0.88f, 0.22f, Wood);
                b.Taper(x - 0.035f, 0.88f, -0.32f, x + 0.035f, 0.96f, 0.22f, 1f, 0.5f, Wood).Ghost();
            }
            b.Box(-0.92f, 0.42f, -0.3f, 0.92f, 0.47f, 0.15f, Wood);
            using (b.Turn(new Vector3(0, 0.47f, 0.14f), 8))
                b.Box(-0.92f, 0.47f, 0.12f, 0.92f, 0.95f, 0.18f, Wood).Loose();
            b.Box(-0.92f, 0.12f, 0.3f, 0.92f, 0.18f, 0.45f, Fabric).Soft();
            b.Box(-0.92f, 0.62f, 0.2f, 0.92f, 0.66f, 0.3f, Wood);
            for (int i = 0; i < 4; i++) b.Box(-0.7f + i * 0.45f, 0.66f, 0.22f, -0.62f + i * 0.45f, 0.78f, 0.28f, Fabric).Ghost().Soft();
            b.Box(-0.92f, 0.08f, -0.05f, 0.92f, 0.12f, 0.05f, Wood).Ghost();
        };

        r["lectern"] = b =>
        {
            b.Box(-0.22f, 0, -0.2f, 0.22f, 0.08f, 0.2f, Wood);
            b.Box(-0.12f, 0.08f, -0.1f, 0.12f, 1.0f, 0.12f, Wood);
            b.Wedge(-0.24f, 1.0f, -0.22f, 0.24f, 0.2f, 1.05f, 1.25f, Wood);
            using (b.Turn(new Vector3(0, 1.15f, 0), -24))
            {
                b.Box(-0.18f, 1.15f, -0.1f, 0.0f, 1.17f, 0.12f, Paper).Ghost().Soft();
                b.Box(0.0f, 1.15f, -0.1f, 0.18f, 1.17f, 0.12f, Paper).Ghost().Soft();
            }
            using (b.Turn(new Vector3(0, 1.05f, -0.21f), 0))
                b.Box(-0.16f, 0.62f, -0.235f, 0.16f, 1.04f, -0.215f, Fabric).Soft();
            b.Box(-0.16f, 0.62f, -0.24f, 0.16f, 0.66f, -0.23f, Gold).Ghost();
            b.Tube(0, 0.8f, -0.236f, 0, 0.8f, -0.245f, 0.05f, Gold, 8).Ghost();
        };

        r["robed-statue"] = b =>
        {
            b.Box(-0.45f, 0, -0.45f, 0.45f, 0.4f, 0.45f, Stone);
            b.Box(-0.48f, 0.36f, -0.48f, 0.48f, 0.42f, 0.48f, Stone).Ghost();
            b.Taper(-0.32f, 0.42f, -0.26f, 0.32f, 1.75f, 0.26f, 0.62f, 0.7f, Stone);
            for (int i = 0; i < 4; i++) b.Taper(-0.26f + i * 0.17f, 0.42f, -0.27f, -0.22f + i * 0.17f, 1.4f, -0.24f, 0.6f, 1f, Body).Ghost().As(DecoFinish.Stone);
            b.Ball(0, 1.72f, -0.05f, 0.22f, 0.12f, 0.18f, Stone, 8);
            b.Ball(0, 1.3f, -0.2f, 0.1f, 0.08f, 0.08f, Stone, 6).Ghost();
            DecoPart head = b.Ball(0, 1.95f, 0.02f, 0.2f, 0.25f, 0.2f, Stone, 8).Loose();
            b.Ball(0, 1.93f, -0.11f, 0.12f, 0.15f, 0.08f, Dark, 6).Ghost();
        };

        r["candle-stand"] = b =>
        {
            for (int i = 0; i < 3; i++)
            {
                float a = i * MathF.Tau / 3 + 0.5f;
                b.Beam(new Vector3(0, 0.25f, 0), new Vector3(MathF.Cos(a) * 0.2f, 0.02f, MathF.Sin(a) * 0.2f), 0.025f, 0.025f, Gold);
                b.Cyl(MathF.Cos(a) * 0.2f, 0, MathF.Sin(a) * 0.2f, 0.03f, 0.025f, Gold, 6).Ghost();
            }
            b.Tube(0, 0.2f, 0, 0, 1.1f, 0, 0.02f, Gold, 6);
            b.Cyl(0, 1.1f, 0, 0.22f, 0.03f, Gold, 10);
            for (int i = 0; i < 6; i++)
            {
                float a = i * MathF.Tau / 6, d = i == 0 ? 0 : 0.15f;
                float h = 0.06f + b.Rng.Range(0, 0.2f);
                float x = i == 0 ? 0 : MathF.Cos(a) * d, z = i == 0 ? 0 : MathF.Sin(a) * d;
                b.Cyl(x, 1.13f, z, 0.022f, h, Linen, 6).Soft();
                b.Cyl(x, 1.13f + h, z, 0.012f, 0.035f, Lamp, 4, 0.1f).Lit().Ghost();
                b.Ball(x + 0.018f, 1.13f + h * 0.5f, z, 0.012f, Linen, 4).Ghost();
            }
            b.Light(0, 1.35f, 0, Lamp);
        };

        r["altar-table"] = b =>
        {
            b.Box(-0.98f, 0, -0.45f, 0.98f, 0.1f, 0.47f, Stone);
            b.Box(-0.92f, 0.1f, -0.38f, 0.92f, 0.9f, 0.42f, Stone);
            b.Box(-0.98f, 0.9f, -0.45f, 0.98f, 0.98f, 0.47f, Stone);
            b.Box(-0.4f, 0.98f, -0.45f, 0.4f, 0.985f, 0.47f, Fabric).Soft();
            b.Box(-0.4f, 0.5f, -0.46f, 0.4f, 0.985f, -0.45f, Fabric).Soft();
            b.Box(-0.4f, 0.5f, -0.465f, 0.4f, 0.53f, -0.46f, Gold).Ghost();
            foreach (float x in new[] { -0.7f, 0.7f })
            {
                b.Cyl(x, 0.98f, 0.1f, 0.05f, 0.015f, Gold, 6).Ghost();
                b.Cyl(x, 0.98f, 0.1f, 0.015f, 0.015f, Gold, 6).Ghost();
            }
            b.Cyl(0, 0.985f, 0.15f, 0.1f, 0.01f, Gold, 10, 1.4f).Ghost();
        };

        r["notice-board"] = b =>
        {
            foreach (float x in new[] { -0.62f, 0.62f })
            {
                b.Box(x - 0.04f, 0, -0.04f, x + 0.04f, 1.9f, 0.04f, Wood);
                b.Box(x - 0.04f, 0, -0.25f, x + 0.04f, 0.05f, 0.25f, Wood);
            }
            b.Box(-0.68f, 0.8f, -0.03f, 0.68f, 1.82f, 0.03f, Wood);
            b.Box(-0.62f, 0.85f, -0.035f, 0.62f, 1.77f, -0.03f, Canvas);
            b.Taper(-0.72f, 1.82f, -0.12f, 0.72f, 1.98f, 0.12f, 1f, 0.2f, Wood);
            for (int i = 0; i < 9; i++)
            {
                float x = b.Rng.Range(-0.5f, 0.5f), y = b.Rng.Range(0.95f, 1.65f), w = b.Rng.Range(0.07f, 0.13f), h = b.Rng.Range(0.08f, 0.15f);
                using (b.Turn(new Vector3(x, y, 0), 0, 0, b.Rng.Range(-12, 12)))
                    b.Box(x - w, y - h, -0.042f - i * 0.0005f, x + w, y + h, -0.035f, Paper).Ghost().Soft();
            }
        };

        r["park-bench"] = b =>
        {
            foreach (float x in new[] { -0.65f, 0.65f })
            {
                b.Box(x - 0.03f, 0, -0.3f, x + 0.03f, 0.42f, -0.24f, Dark);
                b.Box(x - 0.03f, 0, 0.18f, x + 0.03f, 0.42f, 0.24f, Dark);
                b.Box(x - 0.03f, 0.38f, -0.3f, x + 0.03f, 0.42f, 0.24f, Dark);
                b.Beam(new Vector3(x, 0.42f, 0.22f), new Vector3(x, 0.88f, 0.34f), 0.05f, 0.04f, Dark);
                b.Box(x - 0.03f, 0.6f, -0.3f, x + 0.03f, 0.63f, 0.05f, Dark);
            }
            for (int i = 0; i < 4; i++) b.Box(-0.74f, 0.42f, -0.28f + i * 0.13f, 0.74f, 0.46f, -0.18f + i * 0.13f, Wood);
            using (b.Turn(new Vector3(0, 0.6f, 0.27f), 15))
                for (int i = 0; i < 3; i++)
                {
                    DecoPart slat = b.Box(-0.74f, 0.5f + i * 0.13f, 0.24f, 0.74f, 0.6f + i * 0.13f, 0.28f, Wood);
                    if (i == 1) slat.Loose();
                }
        };

        r["banner-stand"] = b =>
        {
            b.Box(-0.22f, 0, -0.03f, 0.22f, 0.05f, 0.03f, Dark);
            b.Box(-0.03f, 0, -0.22f, 0.03f, 0.05f, 0.22f, Dark);
            b.Tube(0, 0.05f, 0, 0, 2.35f, 0, 0.022f, Gold, 6);
            b.Tube(-0.24f, 2.28f, 0, 0.24f, 2.28f, 0, 0.015f, Gold, 6);
            b.Box(-0.22f, 1.1f, -0.012f, 0.22f, 2.26f, 0.012f, Fabric).Soft();
            b.Wedge(-0.22f, 0.95f, -0.012f, 0.0f, 0.012f, 1.1f, 1.1f, Fabric).Soft().Ghost();
            b.Wedge(0.0f, 0.95f, -0.012f, 0.22f, 0.012f, 1.1f, 1.1f, Fabric).Soft().Ghost();
            b.Cyl(0, 1.75f, -0.013f, 0.11f, 0.004f, Gold, 10).Ghost();
            b.Box(-0.22f, 2.18f, -0.014f, 0.22f, 2.22f, -0.012f, Gold).Ghost();
            b.Ball(0, 2.4f, 0, 0.045f, Gold, 6).Ghost();
        };

        r["column"] = b =>
        {
            b.Box(-0.48f, 0, -0.48f, 0.48f, 0.2f, 0.48f, Stone);
            b.Cyl(0, 0.2f, 0, 0.34f, 0.08f, Stone, 12).Ghost();
            b.Cyl(0, 0.2f, 0, 0.31f, 2.1f, Body, 12, 0.92f).As(DecoFinish.Stone);
            b.Taper(-0.36f, 2.3f, -0.36f, 0.36f, 2.5f, 0.36f, 1.33f, 1.33f, Stone);
        };
    }

    static void Alien(Dictionary<string, Action<DecoBuilder>> r)
    {
        // Chitin: the Vharn look. Hunched carapace, oxblood bands, bone, amber resin that glows.
        r["resin-altar"] = b =>
        {
            b.Dome(0, 0, 0, 0.72f, 0.62f, 0.48f, Chitin, 12);
            b.Dome(0, 0.25f, 0.05f, 0.6f, 0.35f, 0.38f, Trim, 12).Ghost();
            for (int i = 0; i < 3; i++) b.Ball(-0.45f + i * 0.45f, 0.42f, 0.2f, 0.2f, 0.2f, 0.22f, Body, 8).Ghost();
            b.Cyl(0, 0.6f, -0.05f, 0.32f, 0.12f, Body, 10);
            b.Cyl(0, 0.7f, -0.05f, 0.28f, 0.05f, Resin, 10).Lit();
            for (int i = 0; i < 3; i++)
            {
                float z = -0.25f + i * 0.2f;
                b.Beam(new Vector3(-0.55f, 0.45f, z), new Vector3(-0.15f, 0.97f, z), 0.05f, 0.05f, Bone);
                b.Beam(new Vector3(0.55f, 0.45f, z), new Vector3(0.15f, 0.97f, z), 0.05f, 0.05f, Bone);
                b.Beam(new Vector3(-0.15f, 0.97f, z), new Vector3(0.15f, 0.97f, z), 0.05f, 0.05f, Bone).Loose();
            }
            foreach (float s in new[] { -1f, 1f }) b.Tube(s * 0.6f, 0.1f, -0.35f, s * 0.68f, 0.45f, -0.48f, 0.05f, Bone, 5, 0f);
            b.Light(0, 0.9f, -0.1f, Resin);
        };

        r["rib-pillar"] = b =>
        {
            b.Dome(0, 0, 0, 0.45f, 0.3f, 0.45f, Chitin, 12);
            for (int i = 0; i < 7; i++)
            {
                float y = 0.25f + i * 0.3f, rr = 0.26f - i * 0.012f;
                b.Ball(0, y, 0, rr, 0.16f, rr, i % 2 == 0 ? Chitin : Trim, 8);
                if (i < 6) b.Cyl(0, y + 0.1f, 0, rr * 0.7f, 0.06f, Resin, 8).Lit().Ghost();
            }
            for (int k = 0; k < 4; k++)
            {
                float a = k * MathF.Tau / 4 + 0.4f;
                Vector3 P(float t, float rad) => new(MathF.Cos(a + t * 0.6f) * rad, t * 2.35f, MathF.Sin(a + t * 0.6f) * rad);
                b.Beam(P(0.02f, 0.42f), P(0.35f, 0.4f), 0.06f, 0.06f, Bone);
                b.Beam(P(0.35f, 0.4f), P(0.7f, 0.36f), 0.055f, 0.055f, Bone);
                b.Beam(P(0.7f, 0.36f), P(1.0f, 0.2f), 0.05f, 0.05f, Bone).Loose();
            }
            b.Cyl(0, 2.2f, 0, 0.15f, 0.28f, Bone, 6, 0f);
            b.Light(0, 1.2f, -0.4f, Resin);
        };

        r["brood-pod"] = b =>
        {
            for (int i = 0; i < 5; i++)
            {
                float a = i * MathF.Tau / 5;
                b.Beam(new Vector3(MathF.Cos(a) * 0.42f, 0.04f, MathF.Sin(a) * 0.42f), new Vector3(MathF.Cos(a) * 0.3f, 0.45f, MathF.Sin(a) * 0.3f), 0.07f, 0.07f, Chitin);
            }
            b.Dome(0, 0, 0, 0.34f, 0.3f, 0.34f, Chitin, 10);
            b.Ball(0, 0.75f, 0, 0.36f, 0.58f, 0.36f, Glass, 10).Pane();
            b.Ball(0, 0.68f, 0, 0.18f, 0.32f, 0.18f, Resin, 8).Lit();
            for (int i = 0; i < 4; i++)
            {
                float a = i * MathF.Tau / 4 + 0.3f;
                b.Beam(new Vector3(MathF.Cos(a) * 0.33f, 0.4f, MathF.Sin(a) * 0.33f), new Vector3(MathF.Cos(a) * 0.3f, 1.05f, MathF.Sin(a) * 0.3f), 0.025f, 0.025f, Trim).Ghost();
            }
            b.Light(0, 0.7f, -0.5f, Resin);
        };

        r["shell-totem"] = b =>
        {
            b.Dome(0, 0, 0, 0.42f, 0.18f, 0.42f, Chitin, 10);
            b.Tube(0, 0.1f, 0, 0, 1.6f, 0, 0.07f, Chitin, 6);
            float y = 0.25f;
            for (int i = 0; i < 4; i++)
            {
                float rr = 0.36f - i * 0.06f, h = 0.32f - i * 0.03f;
                using (b.Turn(new Vector3(0, y, 0), 0, i * 40))
                    b.Cyl(0, y, 0, rr, h, i % 2 == 0 ? Bone : Trim, 7, 0.3f);
                y += h * 0.95f;
            }
            for (int i = 0; i < 3; i++)
                using (b.Turn(new Vector3(0, y, 0), 0, 0, -30 + i * 30))
                    b.Box(-0.05f, y, -0.02f, 0.05f, y + 0.45f, 0.02f, Bone).Loose();
            b.Cable(new Vector3(-0.3f, 0.35f, 0.0f), new Vector3(0.3f, 0.35f, 0.0f), 0.012f, Trim, 0.25f);
        };

        // Lattice: the Kessari look. Graphite plinths, pale modular frames, cyan nodes (magenta when not working).
        r["lattice-obelisk"] = b =>
        {
            b.Box(-0.45f, 0, -0.45f, 0.45f, 0.3f, 0.45f, Body);
            b.Box(-0.4f, 0.3f, -0.4f, 0.4f, 0.36f, 0.4f, Dark);
            foreach (float x in new[] { -1f, 1f })
                foreach (float z in new[] { -1f, 1f })
                    b.Beam(new Vector3(x * 0.33f, 0.36f, z * 0.33f), new Vector3(x * 0.14f, 2.3f, z * 0.14f), 0.05f, 0.05f, Lattice);
            for (int i = 0; i < 4; i++)
            {
                float y = 0.75f + i * 0.42f, rr = 0.33f - (y - 0.36f) / 1.94f * 0.19f;
                Ring(b, new Vector3(0, y, 0), rr * 1.41f, 4, 0.035f, Lattice);
            }
            b.Cyl(0, 0.36f, 0, 0.1f, 1.94f, Node, 6).Lit();
            b.Taper(-0.16f, 2.28f, -0.16f, 0.16f, 2.5f, 0.16f, 0.1f, 0.1f, Body);
            b.Ball(0.0f, 1.9f, -0.17f, 0.04f, Node2, 6).Lit().Ghost();
            b.Light(0, 1.3f, -0.4f, Node);
        };

        r["resonance-harp"] = b =>
        {
            b.Box(-0.72f, 0, -0.18f, 0.72f, 0.18f, 0.2f, Body);
            b.Ball(0, 0.18f, 0, 0.5f, 0.14f, 0.18f, Dark, 8);
            b.Beam(new Vector3(-0.65f, 0.18f, 0), new Vector3(-0.55f, 1.95f, 0), 0.06f, 0.06f, Lattice);
            b.Beam(new Vector3(0.65f, 0.18f, 0), new Vector3(0.45f, 1.7f, 0), 0.06f, 0.06f, Lattice);
            b.Beam(new Vector3(-0.55f, 1.95f, 0), new Vector3(0.45f, 1.7f, 0), 0.06f, 0.06f, Lattice).Loose();
            for (int i = 0; i < 7; i++)
            {
                float x = -0.42f + i * 0.13f;
                float top = 1.93f - (x + 0.55f) / 1.0f * 0.25f;
                b.Tube(x, 0.3f, 0, x, top - 0.03f, 0, 0.007f, Node, 4).Lit().Ghost();
            }
            foreach (float x in new[] { -0.6f, 0.62f }) b.Ball(x, 0.25f, -0.19f, 0.03f, Node2, 6).Lit().Ghost();
            b.Light(0, 1.0f, -0.3f, Node);
        };

        r["node-plinth"] = b =>
        {
            b.Box(-0.48f, 0, -0.48f, 0.48f, 0.18f, 0.48f, Body);
            b.Box(-0.38f, 0.18f, -0.38f, 0.38f, 0.5f, 0.38f, Body);
            b.Box(-0.3f, 0.5f, -0.3f, 0.3f, 0.62f, 0.3f, Dark);
            foreach (float x in new[] { -1f, 1f }) foreach (float z in new[] { -1f, 1f })
            {
                b.Beam(new Vector3(x * 0.26f, 0.62f, z * 0.26f), new Vector3(x * 0.26f, 1.3f, z * 0.26f), 0.035f, 0.035f, Lattice);
                b.Ball(x * 0.4f, 0.52f, z * 0.4f, 0.035f, Node2, 6).Lit().Ghost();
            }
            Ring(b, new Vector3(0, 1.3f, 0), 0.37f, 4, 0.035f, Lattice);
            Ring(b, new Vector3(0, 0.95f, 0), 0.3f, 12, 0.025f, Lattice, 18, 10);
            b.Ball(0, 0.95f, 0, 0.12f, Node, 8).Lit();
            b.Light(0, 0.95f, 0, Node);
        };

        r["crystal-cradle"] = b =>
        {
            b.Cyl(0, 0, 0, 0.45f, 0.2f, Body, 8);
            for (int i = 0; i < 4; i++)
            {
                float a = i * MathF.Tau / 4 + 0.785f;
                Vector3 p0 = new(MathF.Cos(a) * 0.38f, 0.2f, MathF.Sin(a) * 0.38f), p1 = new(MathF.Cos(a) * 0.42f, 0.7f, MathF.Sin(a) * 0.42f), p2 = new(MathF.Cos(a) * 0.25f, 1.05f, MathF.Sin(a) * 0.25f);
                b.Beam(p0, p1, 0.05f, 0.05f, Lattice);
                b.Beam(p1, p2, 0.045f, 0.045f, Lattice);
            }
            (float x, float z, float h, float tilt)[] crystals = { (0, 0, 1.0f, 0), (0.15f, 0.08f, 0.6f, 18), (-0.14f, 0.1f, 0.7f, -15), (0.05f, -0.16f, 0.5f, 12) };
            foreach (var (x, z, h, tilt) in crystals)
                using (b.Turn(new Vector3(x, 0.2f, z), tilt * 0.6f, 0, tilt))
                {
                    // Grown from the cradle: set into it, its point grown from the shaft.
                    b.Cyl(x, 0.14f, z, 0.09f, h + 0.06f, Glass, 6).Pane();
                    b.Cyl(x, 0.18f + h, z, 0.09f, 0.17f, Glass, 6, 0f).Pane().Ghost();
                }
            b.Cyl(0, 0.25f, 0, 0.05f, 0.85f, Node, 6).Lit();
            b.Light(0, 0.8f, 0, Node);
        };

        // Bloom: the Hollow Bloom's growths. Fleshy, veined, glowing a sick green.
        r["bloom-pod"] = b =>
        {
            b.Dome(0, 0, 0, 0.48f, 0.25f, 0.46f, Body, 10);
            b.Ball(-0.12f, 0.5f, 0.05f, 0.3f, 0.45f, 0.3f, Flesh, 10);
            b.Ball(0.2f, 0.35f, -0.12f, 0.22f, 0.3f, 0.22f, Flesh, 8);
            b.Ball(0.12f, 0.95f, 0.1f, 0.18f, 0.25f, 0.18f, Flesh, 8).Loose();
            foreach (var (x, y, z) in new[] { (-0.2f, 0.8f, -0.25f), (0.3f, 0.45f, -0.3f), (0.15f, 1.15f, -0.05f), (-0.38f, 0.4f, -0.1f) })
                b.Ball(x, y, z, 0.07f, Node, 6).Lit().Ghost();
            for (int i = 0; i < 4; i++)
            {
                float a = i * MathF.Tau / 4 + 0.2f;
                b.Beam(new Vector3(MathF.Cos(a) * 0.1f, 0.2f, MathF.Sin(a) * 0.1f), new Vector3(MathF.Cos(a) * 0.3f, 0.95f, MathF.Sin(a) * 0.3f), 0.03f, 0.03f, Dark).Ghost();
            }
            b.Light(0, 0.7f, -0.4f, Node);
        };

        r["spore-chime"] = b =>
        {
            b.Dome(0, 0, 0, 0.4f, 0.15f, 0.4f, Body, 10);
            b.Beam(new Vector3(0, 0.1f, 0), new Vector3(0.1f, 0.8f, 0.05f), 0.12f, 0.12f, Bone);
            b.Beam(new Vector3(0.1f, 0.8f, 0.05f), new Vector3(-0.05f, 1.5f, 0.0f), 0.1f, 0.1f, Bone);
            b.Beam(new Vector3(-0.05f, 1.5f, 0.0f), new Vector3(0.0f, 1.75f, 0.0f), 0.08f, 0.08f, Bone);
            b.Ball(0, 1.78f, 0, 0.46f, 0.15f, 0.46f, Flesh, 10);
            b.Ball(0, 1.7f, 0, 0.4f, 0.06f, 0.4f, Dark, 10).Ghost();
            for (int i = 0; i < 7; i++)
            {
                float a = i * MathF.Tau / 7 + 0.2f, d = 0.18f + (i % 2) * 0.14f;
                float x = MathF.Cos(a) * d, z = MathF.Sin(a) * d, len = 0.25f + (i % 3) * 0.12f;
                b.Cable(new Vector3(x, 1.67f, z), new Vector3(x, 1.67f - len, z), 0.005f, Trim, 0f);
                b.Ball(x, 1.67f - len - 0.04f, z, 0.05f, Node, 6).Lit().Ghost();
            }
            b.Light(0, 1.3f, 0, Node);
        };
    }

    static void Precursor(Dictionary<string, Action<DecoBuilder>> r)
    {
        // Older than anyone: black stone, jade glyph lines that still glow, bronze that never rusted.
        r["glyph-plinth"] = b =>
        {
            b.Box(-0.48f, 0, -0.48f, 0.48f, 0.3f, 0.48f, Stone);
            b.Box(-0.38f, 0.3f, -0.38f, 0.38f, 0.75f, 0.38f, Body);
            b.Box(-0.3f, 0.75f, -0.3f, 0.3f, 0.95f, 0.3f, Stone);
            Glyphs(b, -0.32f, 0.38f, 0.32f, 0.7f, -0.385f, 5);
            Glyphs(b, -0.4f, 0.08f, 0.4f, 0.24f, -0.485f, 4);
            b.Box(-0.39f, 0.55f, -0.39f, 0.39f, 0.58f, 0.39f, Bronze).Ghost();
            using (b.Turn(new Vector3(0, 1.3f, 0), 12, 30))
                b.Cyl(0, 1.1f, 0, 0.18f, 0.32f, Bronze, 3, 0f);
            b.Light(0, 1.0f, -0.5f, Node);
        };

        r["glyph-monolith"] = b =>
        {
            b.Box(-0.48f, 0, -0.24f, 0.48f, 0.12f, 0.24f, Stone);
            b.Taper(-0.42f, 0.12f, -0.18f, 0.42f, 2.48f, 0.18f, 0.72f, 0.8f, Body);
            for (int col = 0; col < 3; col++)
            {
                float x = -0.2f + col * 0.2f;
                for (int i = 0; i < 7; i++)
                {
                    float y = 0.4f + i * 0.27f, w = (0.42f - (y - 0.12f) / 2.36f * 0.12f);
                    if (MathF.Abs(x) + 0.05f > w) continue;
                    float len = b.Rng.Range(0.06f, 0.16f);
                    bool vert = b.Rng.Chance(0.5f);
                    float zf = -0.18f + (y - 0.12f) / 2.36f * 0.036f - 0.006f;
                    b.Box(x - (vert ? 0.012f : len / 2), y - (vert ? len / 2 : 0.012f), zf - 0.005f, x + (vert ? 0.012f : len / 2), y + (vert ? len / 2 : 0.012f), zf, Node).Lit().Ghost();
                }
            }
            b.Box(-0.4f, 1.2f, -0.185f, 0.4f, 1.26f, 0.185f, Bronze).Ghost();
            b.Light(0, 1.3f, -0.5f, Node);
        };

        r["bronze-brazier"] = b =>
        {
            for (int i = 0; i < 3; i++)
            {
                float a = i * MathF.Tau / 3 + 0.3f;
                b.Beam(new Vector3(MathF.Cos(a) * 0.38f, 0.04f, MathF.Sin(a) * 0.38f), new Vector3(MathF.Cos(a) * 0.2f, 0.9f, MathF.Sin(a) * 0.2f), 0.06f, 0.06f, Bronze);
                b.Cyl(MathF.Cos(a) * 0.39f, 0, MathF.Sin(a) * 0.39f, 0.06f, 0.05f, Bronze, 6).Ghost();
            }
            b.Cyl(0, 0.85f, 0, 0.36f, 0.3f, Bronze, 8, 1.3f);
            b.Cyl(0, 1.14f, 0, 0.44f, 0.02f, Gold, 8).Ghost();
            for (int i = 0; i < 4; i++)
            {
                float a = i * MathF.Tau / 4, d = i == 0 ? 0 : 0.18f;
                using (b.Turn(new Vector3(MathF.Cos(a) * d, 1.1f, MathF.Sin(a) * d), i == 0 ? 0 : 15, 0, i == 0 ? 0 : 12))
                {
                    b.Cyl(MathF.Cos(a) * d, 1.0f, MathF.Sin(a) * d, 0.07f, i == 0 ? 0.3f : 0.18f, Glass, 6).Pane();
                    b.Cyl(MathF.Cos(a) * d, 1.0f + (i == 0 ? 0.3f : 0.18f), MathF.Sin(a) * d, 0.07f, 0.1f, Glass, 6, 0f).Ghost();
                }
            }
            b.Ball(0, 1.12f, 0, 0.12f, Lamp, 6).Lit();
            b.Light(0, 1.3f, 0, Lamp);
        };

        r["warden-statue"] = b =>
        {
            b.Box(-0.42f, 0, -0.42f, 0.42f, 0.18f, 0.42f, Stone);
            b.Taper(-0.2f, 0.18f, -0.14f, 0.2f, 1.0f, 0.14f, 0.8f, 0.85f, Body);
            b.Taper(-0.26f, 1.0f, -0.16f, 0.26f, 1.75f, 0.16f, 1.25f, 1.1f, Body);
            b.Box(-0.3f, 1.72f, -0.18f, 0.3f, 1.8f, 0.18f, Bronze);
            foreach (float s in new[] { -1f, 1f })
                b.Beam(new Vector3(s * 0.33f, 1.72f, 0), new Vector3(s * 0.36f, 0.78f, -0.02f), 0.08f, 0.08f, Body);
            b.Tube(0, 1.8f, 0, 0, 1.9f, 0, 0.06f, Body, 6);
            b.Ball(0, 2.15f, 0, 0.14f, 0.28f, 0.15f, Body, 8).Loose();
            b.Box(-0.012f, 2.0f, -0.152f, 0.012f, 2.3f, -0.142f, Node).Lit().Ghost();
            Glyphs(b, -0.18f, 1.1f, 0.18f, 1.6f, -0.165f, 3);
            b.Light(0, 2.1f, -0.4f, Node);
        };

        r["reliquary"] = b =>
        {
            b.Box(-0.48f, 0, -0.48f, 0.48f, 0.2f, 0.48f, Stone);
            b.Box(-0.38f, 0.2f, -0.38f, 0.38f, 0.45f, 0.38f, Body);
            Glyphs(b, -0.32f, 0.24f, 0.32f, 0.42f, -0.385f, 4);
            foreach (float x in new[] { -1f, 1f }) foreach (float z in new[] { -1f, 1f })
                b.Beam(new Vector3(x * 0.32f, 0.45f, z * 0.32f), new Vector3(x * 0.18f, 0.95f, z * 0.18f), 0.04f, 0.04f, Bronze);
            Ring(b, new Vector3(0, 0.95f, 0), 0.25f, 4, 0.04f, Bronze);
            b.Cyl(0, 0.45f, 0, 0.11f, 0.32f, Glass, 6).Pane();
            b.Cyl(0, 0.77f, 0, 0.11f, 0.14f, Glass, 6, 0f).Ghost();
            b.Ball(0, 0.6f, 0, 0.06f, Lamp, 6).Lit().Ghost();
            b.Light(0, 0.7f, 0, Lamp);
        };

        r["stone-bench"] = b =>
        {
            foreach (float x in new[] { -0.7f, 0.7f }) b.Box(x - 0.18f, 0, -0.32f, x + 0.18f, 0.32f, 0.32f, Body);
            b.Box(-0.97f, 0.32f, -0.42f, 0.97f, 0.45f, 0.42f, Stone);
            b.Box(-0.9f, 0.36f, -0.425f, 0.9f, 0.38f, -0.42f, Node).Lit().Ghost();
            Glyphs(b, -0.85f, 0.06f, -0.55f, 0.26f, -0.325f, 2);
        };

        r["orrery"] = b =>
        {
            b.Box(-0.72f, 0, -0.72f, 0.72f, 0.18f, 0.72f, Stone);
            b.Box(-0.5f, 0.18f, -0.5f, 0.5f, 0.4f, 0.5f, Body);
            Glyphs(b, -0.42f, 0.22f, 0.42f, 0.36f, -0.505f, 4);
            b.Cyl(0, 0.4f, 0, 0.06f, 0.75f, Bronze, 6);
            b.Ball(0, 1.3f, 0, 0.17f, Glass, 8).Pane();
            b.Ball(0, 1.3f, 0, 0.1f, Lamp, 6).Lit();
            (float rad, float tx, float tz, float pa, DecoSlot planet)[] rings = { (0.38f, 20, 5, 0.5f, Gold), (0.53f, -12, 22, 2.4f, Node), (0.68f, 6, -15, 4.2f, Bronze) };
            foreach (var (rad, tx, tz, pa, planet) in rings)
            {
                Ring(b, new Vector3(0, 1.3f, 0), rad, 14, 0.025f, Bronze, tx, tz);
                var at = Vector3.Transform(new Vector3(MathF.Cos(pa) * rad, 0, MathF.Sin(pa) * rad), DecoGeo.RotX(tx) * DecoGeo.RotZ(tz)) + new Vector3(0, 1.3f, 0);
                DecoPart p = b.Ball(at.X, at.Y, at.Z, 0.055f, planet, 6);
                if (planet == Node) p.Lit();
            }
            b.Beam(new Vector3(0, 1.1f, 0), new Vector3(0, 1.13f, 0), 0.03f, 0.03f, Bronze).Ghost();
            b.Light(0, 1.3f, 0, Lamp);
        };

        r["glyph-lectern"] = b =>
        {
            b.Taper(-0.4f, 0, -0.2f, 0.4f, 0.95f, 0.2f, 0.6f, 0.7f, Body);
            b.Wedge(-0.45f, 0.95f, -0.24f, 0.45f, 0.24f, 1.05f, 1.35f, Stone);
            using (b.Turn(new Vector3(0, 1.2f, 0), -32))
            {
                b.Box(-0.3f, 1.2f, -0.15f, 0.3f, 1.215f, 0.15f, Bronze).Ghost();
                for (int i = 0; i < 4; i++) b.Box(-0.22f + i * 0.12f, 1.215f, -0.1f, -0.14f + i * 0.12f, 1.222f, 0.1f, Node).Lit().Ghost();
            }
            Glyphs(b, -0.2f, 0.2f, 0.2f, 0.8f, -0.155f, 3);
            b.Light(0, 1.4f, -0.3f, Node);
        };
    }

    /// <summary>Jade glyph lines inlaid in a face at z (facing -Z): short horizontal and vertical strokes.</summary>
    static void Glyphs(DecoBuilder b, float x0, float y0, float x1, float y1, float z, int n)
    {
        for (int i = 0; i < n * 2; i++)
        {
            float x = b.Rng.Range(x0 + 0.03f, x1 - 0.03f), y = b.Rng.Range(y0 + 0.03f, y1 - 0.03f);
            bool vert = b.Rng.Chance(0.5f);
            float len = MathF.Min(b.Rng.Range(0.05f, 0.14f), vert ? (y1 - y0) * 0.8f : (x1 - x0) * 0.8f);
            float hx = vert ? 0.01f : len / 2, hy = vert ? len / 2 : 0.01f;
            x = Math.Clamp(x, x0 + hx, x1 - hx);
            y = Math.Clamp(y, y0 + hy, y1 - hy);
            b.Box(x - hx, y - hy, z - 0.006f, x + hx, y + hy, z, DecoSlot.Node).Lit().Ghost();
        }
    }
}
