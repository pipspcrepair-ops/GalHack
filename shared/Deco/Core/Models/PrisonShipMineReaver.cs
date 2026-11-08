using System;
using System.Collections.Generic;
using System.Numerics;
using static GalHack.Deco.DecoSlot;

namespace GalHack.Deco;

public static partial class DecoModels
{
    static void Prison(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["prison-bunk"] = b =>
        {
            b.Box(-0.98f, 0, 0.42f, 0.98f, 1.5f, 0.5f, Body);
            foreach (float y in new[] { 0.4f, 1.22f })
            {
                b.Box(-0.95f, y, -0.38f, 0.95f, y + 0.06f, 0.42f, Body);
                b.Box(-0.95f, y - 0.1f, -0.4f, 0.95f, y + 0.06f, -0.37f, Body);
                b.Box(-0.92f, y + 0.06f, -0.35f, 0.92f, y + 0.14f, 0.4f, Linen).Soft();
                b.Box(0.55f, y + 0.14f, -0.25f, 0.9f, y + 0.22f, 0.3f, Linen).Soft();
                b.Box(-0.9f, y + 0.14f, -0.32f, -0.4f, y + 0.2f, 0.38f, Fabric).Soft();
            }
            foreach (float x in new[] { -0.95f, 0.95f })
            {
                b.Box(x - 0.03f, 0, -0.4f, x + 0.03f, 0.4f, -0.34f, Metal);
                b.Cable(new Vector3(x * 0.98f, 1.27f, -0.38f), new Vector3(x * 0.98f, 1.48f, 0.44f), 0.012f, Metal, 0.01f);
            }
            b.Box(-0.7f, 0.85f, 0.41f, -0.4f, 1.05f, 0.42f, Paper).Ghost();
            for (int i = 0; i < 6; i++) b.Box(0.2f + i * 0.04f, 0.75f, 0.415f, 0.215f + i * 0.04f, 0.9f, 0.42f, Paper).Ghost();
        };

        r["cell-toilet"] = b =>
        {
            b.Box(-0.22f, 0, 0.2f, 0.22f, 1.0f, 0.48f, Chrome);
            b.Box(-0.18f, 0.88f, 0.08f, 0.18f, 0.98f, 0.2f, Chrome);
            b.Box(-0.14f, 0.95f, 0.1f, 0.14f, 0.981f, 0.19f, Dark).Ghost();
            b.Box(-0.03f, 0.75f, 0.195f, 0.03f, 0.8f, 0.2f, Dark).Ghost();
            b.Cyl(0, 0, -0.05f, 0.2f, 0.38f, Chrome, 10, 1.1f);
            b.Cyl(0, 0.38f, -0.05f, 0.22f, 0.02f, Dark, 10).Ghost();
            b.Box(-0.12f, 0, 0.0f, 0.12f, 0.36f, 0.2f, Chrome);
        };

        r["cell-bars"] = b =>
        {
            b.Box(-1f, 0, -0.06f, 1f, 0.1f, 0.06f, Metal);
            b.Box(-1f, 2.3f, -0.07f, 1f, 2.45f, 0.07f, Metal);
            b.Box(-1f, 1.05f, -0.04f, 1f, 1.12f, 0.04f, Metal);
            b.Box(0.02f, 0.1f, -0.05f, 0.08f, 2.3f, 0.05f, Metal);
            b.Box(0.08f, 0.95f, -0.08f, 0.22f, 1.25f, 0.05f, Dark);
            for (int i = 0; i < 13; i++)
            {
                float x = -0.92f + i * 0.153f;
                if (MathF.Abs(x - 0.05f) < 0.06f) continue;
                DecoPart bar = b.Tube(x, 0.1f, 0, x, 2.3f, 0, 0.02f, Metal, 6);
                if (i == 4 || i == 9) bar.Loose();
            }
            b.Box(-0.98f, 2.32f, -0.075f, -0.6f, 2.42f, -0.07f, Trim).Ghost();
        };

        r["guard-desk"] = b =>
        {
            b.Box(-0.74f, 0, -0.45f, 0.74f, 1.0f, -0.36f, Body);
            b.Box(-0.74f, 0.45f, -0.455f, 0.74f, 0.52f, -0.45f, Trim).Ghost();
            b.Box(-0.74f, 0.95f, -0.48f, 0.74f, 1.0f, -0.3f, Dark);
            b.Box(-0.74f, 0.74f, -0.36f, 0.74f, 0.78f, 0.45f, Dark);
            b.Box(-0.74f, 0, -0.36f, -0.68f, 0.74f, 0.45f, Body);
            b.Box(0.68f, 0, -0.36f, 0.74f, 0.74f, 0.45f, Body);
            foreach (float x in new[] { -0.35f, 0.2f })
            {
                b.Box(x - 0.18f, 0.78f, -0.3f, x + 0.18f, 0.98f, -0.15f, Dark);
                b.Box(x - 0.16f, 0.8f, -0.155f, x + 0.16f, 0.96f, -0.15f, Screen).Screen();
            }
            b.Box(0.45f, 0.78f, 0.0f, 0.6f, 0.85f, 0.15f, Dark).Ghost();
            b.Box(-0.2f, 0.78f, 0.1f, 0.1f, 0.8f, 0.3f, Paper).Ghost();
            b.Box(0.5f, 0.88f, -0.46f, 0.58f, 0.95f, -0.455f, Alert).Ghost();
        };

        r["steel-table"] = b =>
        {
            b.Box(-0.45f, 0.72f, -0.4f, 0.45f, 0.76f, 0.4f, Chrome);
            b.Cyl(0, 0, 0, 0.08f, 0.72f, Chrome, 6);
            b.Cyl(0, 0, 0, 0.2f, 0.04f, Dark, 8);
            foreach (var (x, z) in new[] { (0.58f, 0f), (-0.58f, 0f), (0f, 0.58f), (0f, -0.58f) })
            {
                b.Beam(new Vector3(0, 0.1f, 0), new Vector3(x, 0.1f, z), 0.05f, 0.05f, Chrome);
                b.Tube(x, 0.1f, z, x, 0.42f, z, 0.025f, Chrome, 6);
                b.Cyl(x, 0.42f, z, 0.15f, 0.04f, Chrome, 8);
            }
        };

        r["cctv-bank"] = b =>
        {
            b.Box(-0.74f, 0, -0.2f, 0.74f, 0.75f, 0.24f, Body);
            b.Box(-0.7f, 0.75f, -0.05f, 0.7f, 0.79f, 0.22f, Dark);
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    float x0 = -0.7f + i * 0.71f, y0 = 0.8f + j * 0.34f;
                    ScreenPanel(b, x0, y0, x0 + 0.68f, y0 + 0.32f, -0.04f, 0.24f, Dark);
                }
            b.Box(-0.3f, 0.75f, -0.18f, 0.2f, 0.77f, -0.08f, Dark).Ghost();
        };

        r["floor-mattress"] = b =>
        {
            b.Box(-0.42f, 0, -0.92f, 0.42f, 0.13f, 0.92f, Linen).Soft();
            for (int i = 0; i < 5; i++) b.Box(-0.425f, 0.03f + i * 0.02f, -0.92f, -0.42f, 0.04f + i * 0.02f, 0.92f, Trim).Ghost();
            b.Box(-0.3f, 0.13f, 0.55f, 0.3f, 0.21f, 0.85f, Linen).Soft();
            using (b.Turn(new Vector3(0, 0.13f, -0.2f), 0, 8))
                b.Box(-0.34f, 0.13f, -0.8f, 0.32f, 0.17f, 0.3f, Fabric).Soft();
        };
    }

    static void Ship(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["ship-console"] = b =>
        {
            b.Box(-0.4f, 0, -0.15f, 0.4f, 0.75f, 0.24f, Body);
            b.Box(-0.41f, 0.3f, -0.155f, 0.41f, 0.36f, -0.15f, Trim).Ghost();
            b.Wedge(-0.48f, 0.75f, -0.24f, 0.48f, 0.24f, 0.85f, 1.0f, Dark);
            ScreenPanel(b, -0.44f, 1.02f, 0.44f, 1.42f, 0.08f, 0.14f, Body);
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 2; j++)
                {
                    float x = -0.38f + i * 0.15f, t = 0.25f + j * 0.4f;
                    float z = -0.24f + t * 0.48f, y = 0.85f + t * 0.15f;
                    using (b.Turn(new Vector3(x, y, z), -17))
                        b.Box(x - 0.045f, y, z - 0.04f, x + 0.045f, y + 0.012f, z + 0.04f, (i + j) % 4 == 0 ? Signal : (i % 2 == 0 ? Screen : Trim)).Lit().Ghost();
                }
            foreach (float x in new[] { -0.46f, 0.46f }) b.Tube(x, 0.8f, -0.2f, x, 0.8f, 0.1f, 0.015f, Metal).Ghost();
        };

        r["crash-couch"] = b =>
        {
            b.Box(-0.3f, 0, -0.3f, 0.3f, 0.12f, 0.35f, Dark);
            b.Box(-0.15f, 0.12f, -0.15f, 0.15f, 0.4f, 0.25f, Metal);
            using (b.Turn(new Vector3(0, 0.45f, 0.1f), -10))
            {
                b.Box(-0.42f, 0.4f, -0.6f, 0.42f, 0.55f, 0.15f, Body);
                b.Box(-0.36f, 0.55f, -0.55f, 0.36f, 0.64f, 0.12f, Fabric).Soft();
                b.Box(-0.42f, 0.55f, -0.55f, -0.36f, 0.75f, 0.12f, Body);
                b.Box(0.36f, 0.55f, -0.55f, 0.42f, 0.75f, 0.12f, Body);
            }
            using (b.Turn(new Vector3(0, 0.5f, 0.15f), 32))
            {
                b.Box(-0.42f, 0.5f, 0.1f, 0.42f, 1.35f, 0.26f, Body);
                b.Box(-0.36f, 0.55f, 0.05f, 0.36f, 1.3f, 0.12f, Fabric).Soft();
                b.Box(-0.22f, 1.18f, 0.0f, 0.22f, 1.38f, 0.12f, Fabric).Soft();
                foreach (float x in new[] { -0.16f, 0.16f }) b.Box(x - 0.03f, 0.6f, 0.03f, x + 0.03f, 1.15f, 0.05f, Trim).Ghost().Soft();
            }
            using (b.Turn(new Vector3(0, 0.45f, -0.58f), 50))
                b.Box(-0.3f, 0.4f, -0.62f, 0.3f, 0.48f, -0.2f, Body);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(s * 0.42f - 0.04f, 0.72f, -0.45f, s * 0.42f + 0.04f, 0.78f, -0.05f, Dark);
                b.Tube(s * 0.42f, 0.78f, -0.38f, s * 0.42f, 0.9f, -0.4f, 0.015f, Dark).Ghost();
                b.Ball(s * 0.42f, 0.92f, -0.4f, 0.03f, Trim, 6).Ghost();
            }
        };

        r["ship-locker"] = b =>
        {
            b.Box(-0.23f, 0, -0.2f, 0.23f, 1.95f, 0.24f, Body);
            b.Box(-0.22f, 0.05f, -0.22f, 0.22f, 1.9f, -0.2f, Body).Loose().Door(-0.22f, -0.21f, 100);
            for (int i = 0; i < 4; i++) b.Box(-0.15f, 1.6f + i * 0.05f, -0.225f, 0.15f, 1.615f + i * 0.05f, -0.22f, Dark).Ghost();
            b.Box(0.13f, 0.85f, -0.235f, 0.17f, 1.05f, -0.22f, Metal).Ghost();
            b.Box(-0.12f, 1.35f, -0.225f, 0.12f, 1.42f, -0.22f, Paper).Ghost();
            b.Box(-0.235f, 0.2f, -0.21f, -0.225f, 1.8f, 0.23f, Trim).Ghost();
        };

        r["nav-table"] = b =>
        {
            b.Cyl(0, 0, 0, 0.45f, 0.85f, Body, 8);
            b.Cyl(0, 0.3f, 0, 0.455f, 0.05f, Trim, 8).Ghost();
            b.Cyl(0, 0.85f, 0, 0.72f, 0.08f, Dark, 8);
            b.Cyl(0, 0.93f, 0, 0.62f, 0.012f, Screen, 8).Screen();
            for (int i = 0; i < 7; i++)
            {
                float a = b.Rng.Range(0, MathF.Tau), d = b.Rng.Range(0.1f, 0.5f);
                b.Ball(MathF.Cos(a) * d, 0.955f + b.Rng.Range(0, 0.015f), MathF.Sin(a) * d, b.Rng.Range(0.012f, 0.025f), i == 0 ? Lamp : Signal, 5).Lit().Ghost();
            }
            Ring(b, new Vector3(0, 0.945f, 0), 0.35f, 12, 0.008f, Lamp, 0, 0, ghost: true);
        };

        r["netted-cargo"] = b =>
        {
            b.Box(-0.72f, 0, -0.45f, 0.0f, 0.62f, 0.45f, Body);
            b.Box(0.02f, 0, -0.45f, 0.72f, 0.5f, 0.2f, Trim);
            b.Box(0.05f, 0, 0.22f, 0.72f, 0.45f, 0.48f, Canvas);
            b.Box(-0.6f, 0.62f, -0.35f, 0.1f, 0.95f, 0.3f, Canvas).Soft();
            foreach (float x in new[] { -0.7f, 0.7f }) foreach (float z in new[] { -0.47f, 0.47f }) b.Box(x - 0.04f, 0, z - 0.02f, x + 0.04f, 0.04f, z + 0.02f, Metal).Ghost();
            // The net: straps from the floor rings, over the load.
            for (int i = 0; i < 4; i++)
            {
                float x = -0.6f + i * 0.4f;
                float top = x < 0.02f ? 0.62f : 0.5f;
                b.Cable(new Vector3(x, 0.02f, -0.46f), new Vector3(x, top, -0.46f), 0.012f, Canvas, 0.0f);
                b.Cable(new Vector3(x, top + 0.01f, -0.45f), new Vector3(x, top + 0.01f, x < 0.02f ? -0.36f : 0.15f), 0.012f, Canvas, 0.0f);
            }
            b.Cable(new Vector3(-0.71f, 0.5f, -0.46f), new Vector3(0.71f, 0.42f, -0.46f), 0.012f, Canvas, 0.02f);
        };

        r["bulkhead-lamp"] = b =>
        {
            b.Box(-0.18f, 0.1f, 0.12f, 0.18f, 0.4f, 0.25f, Dark);
            b.Box(-0.14f, 0.13f, 0.0f, 0.14f, 0.37f, 0.12f, Lamp).Lit();
            for (int i = 0; i < 4; i++) b.Box(-0.15f + i * 0.1f, 0.12f, -0.02f, -0.13f + i * 0.1f, 0.38f, 0.0f, Metal).Ghost();
            b.Box(-0.16f, 0.24f, -0.02f, 0.16f, 0.26f, 0.0f, Metal).Ghost();
            b.Light(0, 0.25f, -0.3f, Lamp);
        };

        r["galley-unit"] = b =>
        {
            b.Box(-0.48f, 0, -0.2f, 0.48f, 0.88f, 0.24f, Body);
            b.Box(-0.49f, 0.88f, -0.22f, 0.49f, 0.92f, 0.24f, Metal);
            b.Box(-0.48f, 1.15f, 0.0f, 0.48f, 1.48f, 0.24f, Body);
            b.Box(-0.4f, 0.92f, 0.0f, 0.0f, 1.12f, 0.24f, Dark);
            b.Box(-0.36f, 0.95f, -0.005f, -0.1f, 1.08f, 0.0f, Glass).Pane();
            b.Box(-0.08f, 1.0f, -0.005f, -0.03f, 1.04f, 0.0f, Screen).Screen();
            b.Box(-0.47f, 0.4f, -0.205f, 0.47f, 0.45f, -0.2f, Trim).Ghost();
            b.Cyl(0.2f, 0.92f, -0.05f, 0.04f, 0.09f, Plastic, 6).Ghost();
            b.Box(0.05f, 0.92f, -0.15f, 0.4f, 0.94f, 0.0f, Trim).Ghost();
            b.Light(-0.2f, 1.0f, -0.3f, Screen);
        };

        r["suit-rack"] = b =>
        {
            b.Box(-0.48f, 0, -0.2f, 0.48f, 0.08f, 0.24f, Dark);
            foreach (float x in new[] { -0.45f, 0.45f }) b.Box(x - 0.03f, 0.08f, 0.15f, x + 0.03f, 1.98f, 0.21f, Metal);
            b.Box(-0.45f, 1.88f, 0.12f, 0.45f, 1.94f, 0.21f, Metal);
            b.Box(-0.03f, 1.7f, 0.1f, 0.03f, 1.88f, 0.16f, Metal);
            // An empty suit, hung up and left.
            b.Box(-0.2f, 0.95f, -0.1f, 0.2f, 1.52f, 0.12f, Canvas).Soft();
            b.Box(-0.18f, 1.0f, 0.12f, 0.18f, 1.45f, 0.2f, Body);
            b.Ball(0, 1.66f, 0.0f, 0.15f, 0.16f, 0.15f, Plastic, 8).Loose();
            b.Box(-0.1f, 1.6f, -0.155f, 0.1f, 1.72f, -0.1f, Glass).Pane();
            foreach (float s in new[] { -1f, 1f })
            {
                b.Beam(new Vector3(s * 0.2f, 1.48f, 0), new Vector3(s * 0.26f, 0.98f, -0.02f), 0.09f, 0.09f, Canvas).Soft();
                b.Beam(new Vector3(s * 0.1f, 0.97f, 0), new Vector3(s * 0.11f, 0.24f, 0.0f), 0.11f, 0.11f, Canvas).Soft();
                b.Box(s * 0.11f - 0.07f, 0.09f, -0.12f, s * 0.11f + 0.07f, 0.24f, 0.08f, Dark);
            }
            b.Cable(new Vector3(0.12f, 1.3f, 0.19f), new Vector3(0.1f, 1.58f, 0.08f), 0.018f, Trim, 0.15f);
        };
    }

    /// <summary>A rough block of rock <paramref name="size"/> across, its base at <paramref name="y"/>: narrower at the top, every corner a little off.</summary>
    static void Lump(DecoBuilder b, float x, float y, float z, float size)
    {
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            bool top = (i & 2) != 0;
            float k = top ? b.Rng.Range(0.6f, 0.85f) : 1f;
            c[i] = new Vector3(x + ((i & 1) != 0 ? 0.5f : -0.5f) * size * k + b.Rng.Range(-0.05f, 0.05f) * size,
                y + (top ? size * b.Rng.Range(0.55f, 0.75f) : 0f),
                z + ((i & 4) != 0 ? 0.5f : -0.5f) * size * k + b.Rng.Range(-0.05f, 0.05f) * size);
        }
        b.Hull(c, Stone);
    }

    static void Mine(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["mine-cart"] = b =>
        {
            b.Box(-0.35f, 0.12f, -0.55f, 0.35f, 0.28f, 0.55f, Dark);
            foreach (float z in new[] { -0.4f, 0.4f }) foreach (float x in new[] { -0.39f, 0.39f }) Wheel(b, x, 0.14f, z, 0.14f, 0.06f, Metal, Dark);
            b.Taper(-0.4f, 0.28f, -0.6f, 0.4f, 0.9f, 0.6f, 1.15f, 1.15f, Rust);
            b.Box(-0.47f, 0.86f, -0.7f, 0.47f, 0.9f, 0.7f, Metal).Ghost();
            for (int i = 0; i < 7; i++) b.Ball(b.Rng.Range(-0.28f, 0.28f), 0.86f, b.Rng.Range(-0.45f, 0.45f), b.Rng.Range(0.06f, 0.1f), Stone, 5).Ghost();
            foreach (float z in new[] { -0.74f, 0.74f }) b.Box(-0.05f, 0.18f, z - 0.01f, 0.05f, 0.25f, z + 0.01f, Dark).Ghost();
        };

        r["rail-section"] = b =>
        {
            for (int i = 0; i < 5; i++)
            {
                float z = -0.8f + i * 0.4f;
                b.Box(-0.48f, 0, z - 0.08f, 0.48f, 0.07f, z + 0.08f, Wood);
            }
            foreach (float x in new[] { -0.28f, 0.28f })
            {
                b.Box(x - 0.025f, 0.07f, -1f, x + 0.025f, 0.14f, 1f, Metal);
                b.Box(x - 0.05f, 0.07f, -1f, x + 0.05f, 0.085f, 1f, Metal).Ghost();
            }
        };

        r["rock-drill"] = b =>
        {
            Vector3 head = new(0, 1.15f, 0.2f);
            foreach (var foot in new[] { new Vector3(-0.42f, 0.03f, 0.62f), new Vector3(0.42f, 0.03f, 0.62f), new Vector3(0, 0.03f, -0.35f) })
            {
                b.Beam(foot, head, 0.05f, 0.05f, Metal);
                b.Box(foot.X - 0.06f, 0, foot.Z - 0.06f, foot.X + 0.06f, 0.03f, foot.Z + 0.06f, Dark).Ghost();
            }
            b.Beam(new Vector3(0, 1.12f, 0.45f), new Vector3(0, 0.45f, -0.5f), 0.16f, 0.16f, Trim);
            b.Tube(0, 0.45f, -0.5f, 0, 0.18f, -0.72f, 0.03f, Metal, 6, 0.3f).Loose();
            b.Box(-0.14f, 1.12f, 0.35f, 0.14f, 1.38f, 0.65f, Dark);
            Dial(b, 0.15f, 1.3f, 0.34f, 0.05f);
            b.Hang(new Vector3(-0.1f, 1.14f, 0.64f), new Vector3(-1, 0, 0), 0.03f, Dark, 0.25f);
        };

        r["mine-lamp"] = b =>
        {
            b.Box(-0.06f, 0, -0.06f, 0.06f, 1.85f, 0.06f, Wood);
            b.Box(-0.22f, 0, -0.04f, 0.22f, 0.08f, 0.04f, Wood);
            b.Box(-0.04f, 0, -0.22f, 0.04f, 0.08f, 0.22f, Wood);
            b.Box(-0.03f, 1.76f, -0.03f, 0.22f, 1.82f, 0.03f, Metal);
            b.Cyl(0.15f, 1.62f, 0, 0.09f, 0.08f, Metal, 8, 0.5f);
            b.Ball(0.15f, 1.53f, 0, 0.06f, Lamp, 6).Lit();
            for (int i = 0; i < 4; i++)
            {
                float a = i * MathF.Tau / 4 + 0.4f;
                b.Tube(0.15f + MathF.Cos(a) * 0.075f, 1.43f, MathF.Sin(a) * 0.075f, 0.15f + MathF.Cos(a) * 0.075f, 1.63f, MathF.Sin(a) * 0.075f, 0.006f, Metal, 4).Ghost();
            }
            Ring(b, new Vector3(0.15f, 1.44f, 0), 0.075f, 6, 0.01f, Metal, 0, 0, ghost: true);
            // Its cable runs down the post and away along the floor.
            b.Cable(new Vector3(0.15f, 1.66f, 0), new Vector3(0.05f, 1.75f, 0.05f), 0.01f, Dark, 0.15f);
            b.Hang(new Vector3(0.06f, 1.7f, 0.065f), new Vector3(0, 0, 1), 0.01f, Dark, 0.12f);
            b.Light(0.15f, 1.5f, 0, Lamp);
        };

        r["timber-prop"] = b =>
        {
            foreach (float x in new[] { -0.78f, 0.78f })
            {
                b.Box(x - 0.12f, 0.05f, -0.12f, x + 0.12f, 2.28f, 0.12f, Wood);
                b.Box(x - 0.16f, 0, -0.16f, x + 0.16f, 0.05f, 0.16f, Stone);
                b.Box(x - 0.14f, 2.18f, -0.13f, x + 0.14f, 2.3f, 0.13f, Wood).Ghost();
            }
            b.Box(-1f, 2.3f, -0.14f, 1f, 2.5f, 0.14f, Wood);
            b.Beam(new Vector3(-0.68f, 1.6f, 0), new Vector3(-0.3f, 2.3f, 0), 0.1f, 0.08f, Wood).Loose();
            b.Beam(new Vector3(0.68f, 1.6f, 0), new Vector3(0.3f, 2.3f, 0), 0.1f, 0.08f, Wood);
            for (int i = 0; i < 3; i++) b.Box(-0.5f + i * 0.4f, 2.5f - 0.001f, -0.1f, -0.38f + i * 0.4f, 2.5f, 0.1f, Wood).Ghost();
        };

        r["blasting-crate"] = b =>
        {
            b.Box(-0.45f, 0, -0.22f, 0.45f, 0.34f, 0.22f, Wood);
            b.Box(-0.46f, 0.34f, -0.23f, 0.46f, 0.38f, 0.23f, Wood).Loose();
            b.Box(-0.455f, 0.12f, -0.225f, 0.455f, 0.2f, 0.225f, Alert).Ghost();
            b.Wedge(-0.08f, 0.21f, -0.235f, 0.08f, -0.225f, 0.33f, 0.33f, Hazard).Ghost();
            foreach (float x in new[] { -0.47f, 0.45f }) b.Cable(new Vector3(x + 0.01f, 0.27f, -0.08f), new Vector3(x + 0.01f, 0.27f, 0.08f), 0.012f, Canvas, 0.3f);
        };

        r["rock-pile"] = b =>
        {
            // Spoil from the face: big lumps at the foot, smaller ones heaped on them, each resting on what's under it.
            foreach (var (x, z, size) in new[] { (-0.35f, -0.3f, 0.5f), (0.3f, -0.25f, 0.45f), (0.05f, 0.35f, 0.55f), (-0.4f, 0.35f, 0.35f), (0.4f, 0.4f, 0.3f) })
                Lump(b, x, 0, z, size);
            Lump(b, -0.05f, 0.22f, 0, 0.45f);
            Lump(b, 0.08f, 0.45f, 0.04f, 0.3f);
        };

        r["cable-winch"] = b =>
        {
            b.Box(-0.45f, 0, -0.42f, 0.45f, 0.08f, 0.42f, Dark);
            foreach (float x in new[] { -0.38f, 0.3f }) b.Box(x - 0.04f, 0.08f, -0.3f, x + 0.04f, 0.75f, 0.3f, Trim);
            b.Tube(-0.34f, 0.45f, 0, 0.26f, 0.45f, 0, 0.22f, Metal, 10);
            b.Tube(-0.32f, 0.45f, 0, 0.24f, 0.45f, 0, 0.25f, Dark, 10);
            b.Box(0.34f, 0.1f, -0.2f, 0.45f, 0.5f, 0.2f, Trim);
            b.Tube(-0.42f, 0.45f, 0, -0.48f, 0.45f, 0, 0.04f, Metal).Ghost();
            b.Beam(new Vector3(-0.48f, 0.45f, 0), new Vector3(-0.48f, 0.3f, -0.15f), 0.03f, 0.03f, Metal).Ghost();
            // The cable pays out off the drum and lies along the floor.
            b.Hang(new Vector3(-0.1f, 0.42f, -0.24f), new Vector3(0, 0, -1), 0.02f, Dark, 0.15f);
        };
    }

    static void Reaver(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["scrap-throne"] = b =>
        {
            b.Box(-0.4f, 0, -0.4f, 0.4f, 0.42f, 0.35f, Metal);
            b.Box(-0.35f, 0.42f, -0.38f, 0.35f, 0.52f, 0.3f, Fabric).Soft();
            using (b.Turn(new Vector3(0, 0.5f, 0.3f), 4))
            {
                b.Box(-0.45f, 0.4f, 0.25f, 0.45f, 1.3f, 0.37f, Body);
                b.Box(-0.38f, 0.55f, 0.2f, 0.4f, 1.0f, 0.25f, Rust).Ghost();
                using (b.Turn(new Vector3(-0.2f, 1.1f, 0.31f), 0, 0, 12)) b.Box(-0.4f, 0.9f, 0.27f, 0.05f, 1.38f, 0.35f, Metal).Loose();
                b.Box(-0.3f, 0.75f, 0.19f, 0.3f, 0.85f, 0.2f, Trim).Ghost();
                for (int i = 0; i < 5; i++)
                    b.Tube(-0.36f + i * 0.18f, 1.3f, 0.31f, -0.36f + i * 0.18f, 1.3f + (i % 2 == 0 ? 0.16f : 0.1f), 0.31f, 0.025f, Metal, 4, 0f).Ghost();
            }
            foreach (float s in new[] { -1f, 1f })
            {
                b.Tube(s * 0.42f, 0.42f, -0.35f, s * 0.42f, 0.68f, -0.35f, 0.03f, Metal);
                b.Tube(s * 0.42f, 0.68f, -0.38f, s * 0.42f, 0.68f, 0.3f, 0.03f, Metal);
            }
            b.Tube(-0.08f, 0.3f, 0.36f, 0.08f, 0.3f, 0.36f, 0.11f, Rubber, 8).Ghost();
            b.Ball(0, 1.42f, 0.33f, 0.09f, 0.07f, 0.09f, Dark, 8).Loose();
            b.Box(-0.05f, 1.41f, 0.235f, 0.05f, 1.45f, 0.25f, Glass).Ghost();
        };

        r["drum-table"] = b =>
        {
            b.Cyl(0, 0, 0, 0.28f, 0.8f, Rust, 10);
            foreach (float y in new[] { 0.25f, 0.55f }) b.Cyl(0, y, 0, 0.285f, 0.03f, Dark, 10).Ghost();
            b.Box(-0.45f, 0.8f, -0.42f, 0.45f, 0.85f, 0.42f, Metal);
            b.Box(-0.44f, 0.84f, -0.3f, -0.2f, 0.85f, -0.05f, Trim).Ghost();
            for (int i = 0; i < 4; i++) using (b.Turn(new Vector3(0.1f, 0.85f, 0.1f), 0, i * 25)) b.Box(0.05f + i * 0.02f, 0.85f, 0.05f, 0.11f + i * 0.02f, 0.853f, 0.14f, Paper).Ghost();
            b.Cyl(-0.25f, 0.85f, 0.2f, 0.04f, 0.1f, Metal, 6).Ghost();
            b.Cyl(0.3f, 0.85f, -0.25f, 0.035f, 0.12f, Glass, 6).Ghost();
        };

        r["trophy-pole"] = b =>
        {
            b.Box(-0.18f, 0, -0.18f, 0.18f, 0.06f, 0.18f, Rust);
            b.Tube(0, 0.06f, 0, 0, 2.4f, 0, 0.035f, Metal, 6);
            b.Box(-0.22f, 2.0f, -0.02f, 0.22f, 2.05f, 0.02f, Metal);
            b.Ball(0, 2.25f, 0, 0.13f, 0.12f, 0.13f, Dark, 8).Loose();
            b.Box(-0.08f, 2.23f, -0.135f, 0.08f, 2.28f, -0.12f, Glass).Ghost();
            foreach (float s in new[] { -1f, 1f }) using (b.Turn(new Vector3(s * 0.1f, 2.32f, 0), 0, 0, -s * 35)) b.Cyl(s * 0.1f, 2.3f, 0, 0.035f, 0.15f, Bone, 5, 0f).Ghost();
            // Tags on chains, hanging straight from the crossbar.
            foreach (float x in new[] { -0.2f, 0.18f })
            {
                float len = x < 0 ? 0.35f : 0.25f;
                b.Cable(new Vector3(x, 2.01f, 0), new Vector3(x, 2.0f - len, 0), 0.006f, Metal, 0f);
                b.Box(x - 0.04f, 2.0f - len - 0.1f, -0.005f, x + 0.04f, 2.0f - len + 0.005f, 0.005f, Metal).Ghost();
            }
            using (b.Turn(new Vector3(0, 1.6f, 0), 0, 0, 6)) b.Box(0.03f, 1.25f, -0.01f, 0.19f, 1.85f, 0.01f, Trim).Soft();
            b.Box(-0.14f, 0.9f, -0.12f, 0.14f, 1.1f, 0.12f, Body).Loose();
            b.Box(-0.05f, 1.0f, -0.125f, 0.05f, 1.05f, -0.12f, Alert).Ghost();
        };

        r["scrap-barricade"] = b =>
        {
            foreach (float x in new[] { -0.55f, 0.55f })
            {
                b.Beam(new Vector3(x - 0.33f, 0.07f, -0.3f), new Vector3(x + 0.33f, 1.1f, 0.3f), 0.1f, 0.1f, Metal);
                b.Beam(new Vector3(x + 0.33f, 0.07f, -0.3f), new Vector3(x - 0.33f, 1.1f, 0.3f), 0.1f, 0.1f, Metal);
                b.Beam(new Vector3(x, 0.07f, 0.35f), new Vector3(x, 1.0f, -0.35f), 0.1f, 0.1f, Metal);
            }
            using (b.Turn(new Vector3(0, 0, 0.1f), -18)) b.Box(-0.95f, 0.06f, 0.0f, -0.05f, 1.35f, 0.05f, Body).Loose();
            using (b.Turn(new Vector3(0.45f, 0, 0.1f), -14, 0, 4)) b.Box(0.0f, 0.08f, 0.0f, 0.9f, 1.2f, 0.05f, Rust);
            for (int i = 0; i < 5; i++) b.Tube(-0.8f + i * 0.4f, 0.8f, 0.0f, -0.8f + i * 0.4f, 0.95f, -0.45f, 0.03f, Metal, 4, 0f).Ghost();
            b.Box(-0.9f, 0.5f, -0.1f, 0.9f, 0.58f, -0.08f, Trim).Ghost();
            b.Tube(0.55f, 0.25f, -0.35f, 0.55f, 0.25f, -0.05f, 0.25f / MathF.Cos(MathF.PI / 8), Rubber, 8);
        };

        r["burn-barrel"] = b =>
        {
            b.Cyl(0, 0, 0, 0.22f, 0.85f, Rust, 10);
            foreach (float y in new[] { 0.3f, 0.6f }) b.Cyl(0, y, 0, 0.226f, 0.03f, Dark, 10).Ghost();
            for (int i = 0; i < 6; i++)
            {
                float a = i * MathF.Tau / 6 + 0.2f, y = 0.4f + (i % 2) * 0.2f;
                b.Box(MathF.Cos(a) * 0.222f - 0.03f, y, MathF.Sin(a) * 0.222f - 0.03f, MathF.Cos(a) * 0.222f + 0.03f, y + 0.05f, MathF.Sin(a) * 0.222f + 0.03f, Lamp).Lit().Ghost();
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * MathF.Tau / 4;
                b.Cyl(MathF.Cos(a) * 0.08f, 0.82f, MathF.Sin(a) * 0.08f, 0.07f, 0.15f - i * 0.02f, Lamp, 5, 0f).Lit().Ghost();
            }
            b.Cyl(0, 0.82f, 0, 0.1f, 0.17f, Lamp, 5, 0f).Lit().Ghost();
            b.Light(0, 1.05f, 0, Lamp);
        };

        r["pallet-bunk"] = b =>
        {
            foreach (float y in new[] { 0f, 0.14f })
            {
                foreach (float x in new[] { -0.4f, 0f, 0.4f }) b.Box(x - 0.05f, y, -0.95f, x + 0.05f, y + 0.1f, 0.95f, Wood);
                for (int i = 0; i < 7; i++) b.Box(-0.48f, y + 0.1f, -0.92f + i * 0.3f, 0.48f, y + 0.13f, -0.8f + i * 0.3f, Wood);
            }
            b.Box(-0.44f, 0.27f, -0.9f, 0.44f, 0.4f, 0.9f, Linen).Soft();
            using (b.Turn(new Vector3(0, 0.4f, 0), 0, 8)) b.Box(-0.38f, 0.38f, -0.8f, 0.36f, 0.44f, 0.2f, Canvas).Soft();
            b.Box(-0.25f, 0.4f, 0.55f, 0.25f, 0.5f, 0.85f, Fabric).Soft();
            b.Cyl(0.38f, 0.27f, 0.85f, 0.04f, 0.1f, Metal, 6).Ghost();
        };

        r["trophy-board"] = b =>
        {
            b.Box(-0.72f, 0, 0.15f, 0.72f, 1.45f, 0.24f, Rust);
            b.Box(-0.6f, 0.1f, 0.14f, 0.3f, 0.5f, 0.15f, Body).Ghost();
            foreach (var (x, y) in new[] { (-0.45f, 1.1f), (0.0f, 1.15f), (0.45f, 1.05f) })
            {
                b.Ball(x, y, 0.06f, 0.13f, 0.12f, 0.1f, Dark, 8).Loose();
                b.Box(x - 0.08f, y - 0.03f, -0.045f, x + 0.08f, y + 0.03f, -0.03f, Glass).Ghost();
            }
            for (int i = 0; i < 4; i++) b.Cyl(-0.5f + i * 0.2f, 0.62f, 0.14f, 0.04f, 0.01f, Gold, 6).Ghost();
            for (int i = 0; i < 7; i++) b.Box(0.25f + i * 0.05f, 0.35f, 0.145f, 0.265f + i * 0.05f, 0.6f, 0.15f, Trim).Ghost();
            b.Beam(new Vector3(0.22f, 0.4f, 0.145f), new Vector3(0.6f, 0.55f, 0.145f), 0.015f, 0.005f, Trim).Ghost();
            b.Box(0.35f, 0.7f, 0.14f, 0.62f, 0.82f, 0.15f, Hazard).Ghost();
        };

        r["junk-shelf"] = b =>
        {
            foreach (float x in new[] { -0.45f, 0.45f }) b.Beam(new Vector3(x, 0, 0.1f), new Vector3(x * 0.95f, 1.95f, 0.12f), 0.06f, 0.06f, Metal);
            for (int s = 0; s < 4; s++)
            {
                float y = 0.1f + s * 0.48f;
                b.Box(-0.48f, y, -0.18f, 0.48f, y + 0.04f, 0.22f, s % 2 == 0 ? Wood : Body);
                for (int i = 0; i < 3; i++)
                {
                    float x = -0.35f + i * 0.33f + b.Rng.Range(-0.05f, 0.05f);
                    switch ((s + i) % 4)
                    {
                        case 0: b.Cyl(x, y + 0.04f, 0, 0.06f, 0.16f, Metal, 6).Ghost(); break;
                        case 1: b.Ball(x, y + 0.14f, 0, 0.1f, Dark, 6).Ghost(); break;
                        case 2: b.Box(x - 0.1f, y + 0.04f, -0.1f, x + 0.1f, y + 0.18f, 0.12f, Hazard).Ghost(); break;
                        default: b.Box(x - 0.08f, y + 0.04f, -0.12f, x + 0.08f, y + 0.24f, 0.08f, Rust).Ghost(); break;
                    }
                }
            }
            b.Cable(new Vector3(-0.3f, 1.54f, -0.18f), new Vector3(0.3f, 1.54f, -0.18f), 0.015f, Dark, 0.35f);
        };
    }
}
