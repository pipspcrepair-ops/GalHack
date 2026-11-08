using System;
using System.Collections.Generic;
using System.Numerics;
using static GalHack.Deco.DecoSlot;

namespace GalHack.Deco;

public static partial class DecoModels
{
    static void Farm(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["hay-bales"] = b =>
        {
            void Bale(float x0, float y0, float z0, float x1, float y1, float z1)
            {
                b.Box(x0, y0, z0, x1, y1, z1, Canvas).Rots();
                foreach (float t in new[] { 0.3f, 0.7f })
                {
                    float x = x0 + (x1 - x0) * t;
                    b.Box(x - 0.01f, y0, z0 - 0.005f, x + 0.01f, y1 + 0.005f, z1 + 0.005f, Wood).Ghost().Rots();
                }
            }
            Bale(-0.72f, 0, -0.24f, -0.02f, 0.42f, 0.24f);
            Bale(0.02f, 0, -0.24f, 0.72f, 0.42f, 0.24f);
            using (b.Turn(new Vector3(0, 0.42f, 0), 0, 14)) Bale(-0.36f, 0.42f, -0.22f, 0.34f, 0.84f, 0.22f);
            for (int i = 0; i < 5; i++)
            {
                float x = b.Rng.Range(-0.65f, 0.65f), z = b.Rng.Range(-0.45f, -0.3f);
                using (b.Turn(new Vector3(x, 0, z), 0, b.Rng.Range(0, 180))) b.Box(x - 0.08f, 0, z - 0.02f, x + 0.08f, 0.012f, z + 0.02f, Canvas).Ghost().Rots();
            }
        };

        r["feed-sacks"] = b =>
        {
            b.Ball(-0.2f, 0.12f, -0.2f, 0.26f, 0.12f, 0.18f, Canvas, 8).Soft();
            b.Ball(0.22f, 0.12f, -0.15f, 0.25f, 0.12f, 0.19f, Canvas, 8).Soft();
            b.Ball(0.0f, 0.12f, 0.24f, 0.28f, 0.12f, 0.2f, Canvas, 8).Soft();
            b.Ball(0.02f, 0.32f, 0.02f, 0.25f, 0.12f, 0.18f, Canvas, 8).Soft().Loose();
            b.Box(-0.08f, 0.38f, -0.05f, 0.1f, 0.44f, 0.08f, Trim).Ghost().Soft();
            b.Dome(-0.35f, 0, -0.38f, 0.12f, 0.025f, 0.08f, Soil, 6).Ghost().Rots();
        };

        r["trough"] = b =>
        {
            b.Taper(-0.92f, 0.1f, -0.28f, 0.92f, 0.46f, 0.28f, 1.04f, 1.3f, Metal);
            b.Box(-0.9f, 0.4f, -0.33f, 0.9f, 0.461f, 0.33f, Dark).Ghost();
            foreach (float x in new[] { -0.8f, 0.8f }) b.Box(x - 0.04f, 0, -0.3f, x + 0.04f, 0.12f, 0.3f, Metal);
            b.Box(-0.94f, 0.44f, -0.37f, 0.94f, 0.47f, -0.34f, Metal).Ghost();
            b.Box(-0.94f, 0.44f, 0.34f, 0.94f, 0.47f, 0.37f, Metal).Ghost();
        };

        r["fence-section"] = b =>
        {
            foreach (float x in new[] { -0.93f, 0.93f }) b.Box(x - 0.06f, 0, -0.06f, x + 0.06f, 1.35f, 0.06f, Wood);
            for (int i = 0; i < 3; i++)
            {
                float y = 0.3f + i * 0.4f;
                DecoPart rail = b.Box(-0.95f, y, -0.1f, 0.95f, y + 0.1f, -0.06f, Wood);
                if (i == 1) rail.Loose();
            }
            b.Beam(new Vector3(-0.86f, 0.35f, -0.1f), new Vector3(0.86f, 1.15f, -0.1f), 0.08f, 0.03f, Wood).Loose();
        };

        r["farm-gate"] = b =>
        {
            foreach (float x in new[] { -1.4f, 1.4f }) b.Box(x - 0.08f, 0, -0.08f, x + 0.08f, 1.4f, 0.08f, Wood);
            const float hx = -1.31f, hz = 0f;
            for (int i = 0; i < 5; i++)
            {
                float y = 0.2f + i * 0.24f;
                b.Box(-1.3f, y, -0.03f, 1.3f, y + 0.07f, 0.03f, Wood).Door(hx, hz, -75);
            }
            b.Box(-1.3f, 0.15f, -0.035f, -1.22f, 1.25f, 0.035f, Wood).Door(hx, hz, -75);
            b.Box(1.22f, 0.15f, -0.035f, 1.3f, 1.25f, 0.035f, Wood).Door(hx, hz, -75);
            b.Beam(new Vector3(-1.2f, 0.25f, 0), new Vector3(1.2f, 1.18f, 0), 0.07f, 0.05f, Wood).Door(hx, hz, -75).Loose();
            b.Box(1.28f, 0.95f, -0.05f, 1.36f, 1.0f, 0.05f, Metal).Ghost();
        };

        r["scarecrow"] = b =>
        {
            b.Box(-0.05f, 0, -0.05f, 0.05f, 1.92f, 0.05f, Wood);
            b.Box(-0.7f, 1.4f, -0.04f, 0.7f, 1.48f, 0.04f, Wood);
            b.Taper(-0.24f, 0.75f, -0.13f, 0.24f, 1.5f, 0.13f, 1.25f, 1f, Canvas).Soft();
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(s * 0.24f, 1.3f, -0.1f, s * 0.62f, 1.5f, 0.1f, Canvas).Soft();
                b.Box(s * 0.62f, 1.28f, -0.05f, s * 0.7f, 1.42f, 0.05f, Canvas).Rots().Ghost();
            }
            b.Ball(0, 1.68f, 0, 0.16f, 0.18f, 0.15f, Canvas, 8).Soft();
            foreach (float s in new[] { -0.06f, 0.06f }) b.Box(s - 0.025f, 1.7f, -0.152f, s + 0.025f, 1.73f, -0.14f, Dark).Ghost();
            b.Box(-0.07f, 1.6f, -0.16f, 0.07f, 1.615f, -0.145f, Dark).Ghost();
            b.Cyl(0, 1.8f, 0, 0.24f, 0.025f, Dark, 10).Soft();
            b.Cyl(0, 1.82f, 0, 0.13f, 0.17f, Dark, 8, 0.85f).Soft().Loose();
        };

        r["farm-well"] = b =>
        {
            b.Cyl(0, 0, 0, 0.85f, 0.72f, Stone, 12);
            b.Cyl(0, 0.721f, 0, 0.66f, 0.004f, Dark, 12).Ghost();
            b.Cyl(0, 0.68f, 0, 0.88f, 0.06f, Stone, 12).Ghost();
            foreach (float x in new[] { -0.82f, 0.82f }) b.Box(x - 0.07f, 0.68f, -0.07f, x + 0.07f, 1.78f, 0.07f, Wood);
            foreach (float s in new[] { -1f, 1f })
                using (b.Turn(new Vector3(0, 1.78f, 0), s * 32))
                    b.Box(-0.98f, 1.76f, 0f, 0.98f, 1.81f, s * 0.6f, Wood).Loose();
            b.Tube(-0.82f, 1.32f, 0, 0.82f, 1.32f, 0, 0.06f, Wood, 6);
            b.Beam(new Vector3(0.88f, 1.32f, 0), new Vector3(0.95f, 1.2f, -0.12f), 0.04f, 0.04f, Metal).Ghost();
            b.Cyl(0, 0.78f, 0, 0.12f, 0.2f, Wood, 8);
            b.Cable(new Vector3(0, 1.3f, 0), new Vector3(0, 0.97f, 0), 0.012f, Canvas, 0f);
            b.Tube(-0.11f, 0.97f, 0, 0.11f, 0.97f, 0, 0.008f, Metal).Ghost();
        };

        r["farm-tool-rack"] = b =>
        {
            foreach (float x in new[] { -0.7f, 0.7f }) b.Box(x - 0.04f, 0, 0.12f, x + 0.04f, 1.8f, 0.22f, Wood);
            b.Box(-0.74f, 1.5f, 0.1f, 0.74f, 1.58f, 0.22f, Wood);
            b.Box(-0.74f, 0.02f, 0.05f, 0.74f, 0.1f, 0.22f, Wood).Loose();
            float[] xs = { -0.5f, -0.2f, 0.1f, 0.42f };
            for (int i = 0; i < xs.Length; i++)
            {
                float x = xs[i];
                b.Beam(new Vector3(x, 0.12f, -0.08f), new Vector3(x + 0.05f, 1.62f, 0.08f), 0.035f, 0.035f, Wood);
                switch (i)
                {
                    case 0:
                        // A rake: its head on the handle's foot, the tines hung from the head.
                        b.Box(x - 0.08f, 0.1f, -0.1f, x + 0.08f, 0.13f, -0.07f, Metal).Ghost();
                        for (int t = 0; t < 3; t++) b.Beam(new Vector3(x - 0.06f + t * 0.06f, 0.12f, -0.09f), new Vector3(x - 0.06f + t * 0.06f, 0.0f + 0.01f, -0.18f), 0.012f, 0.012f, Metal).Ghost();
                        break;
                    case 2:
                        b.Box(x - 0.12f, 0.0f, -0.2f, x + 0.12f, 0.02f, -0.06f, Metal).Ghost();
                        break;
                    default:
                        using (b.Turn(new Vector3(x, 0.15f, -0.1f), 20)) b.Box(x - 0.11f, 0.0f, -0.12f, x + 0.11f, 0.3f, -0.1f, Metal).Ghost();
                        break;
                }
            }
        };

        r["milk-churns"] = b =>
        {
            int n = 0;
            foreach (var (x, z) in new[] { (-0.24f, -0.2f), (0.24f, -0.16f), (0.02f, 0.24f) })
            {
                b.Cyl(x, 0, z, 0.17f, 0.5f, Metal, 10);
                b.Cyl(x, 0.5f, z, 0.17f, 0.16f, Metal, 10, 0.55f);
                b.Cyl(x, 0.66f, z, 0.095f, 0.08f, Metal, 8);
                b.Cyl(x, 0.2f, z, 0.175f, 0.03f, Dark, 10).Ghost();
                DecoPart lid = b.Cyl(x, 0.74f, z, 0.11f, 0.05f, Metal, 8);
                if (n++ == 1) lid.Loose();
            }
        };

        r["produce-crates"] = b =>
        {
            void Crate(float x0, float y0, float z0, float x1, float y1, float z1)
            {
                b.Box(x0, y0, z0, x1, y0 + 0.03f, z1, Wood);
                foreach (float z in new[] { z0, z1 - 0.025f })
                    for (int i = 0; i < 3; i++) b.Box(x0, y0 + 0.05f + i * 0.11f, z, x1, y0 + 0.12f + i * 0.11f, z + 0.025f, Wood).Loose();
                foreach (float x in new[] { x0, x1 - 0.025f }) b.Box(x, y0, z0, x + 0.025f, y1, z1, Wood);
                for (int i = 0; i < 6; i++)
                    b.Ball(b.Rng.Range(x0 + 0.08f, x1 - 0.08f), y1 - 0.08f, b.Rng.Range(z0 + 0.08f, z1 - 0.08f), 0.07f, new[] { Alert, Plant, Hazard }[i % 3], 6).Ghost().Rots();
            }
            Crate(-0.72f, 0, -0.45f, -0.02f, 0.4f, 0.45f);
            Crate(0.02f, 0, -0.45f, 0.72f, 0.4f, 0.45f);
            Crate(-0.4f, 0.4f, -0.4f, 0.3f, 0.8f, 0.4f);
        };

        r["chicken-coop"] = b =>
        {
            foreach (float x in new[] { -0.55f, 0.15f }) foreach (float z in new[] { -0.35f, 0.4f }) b.Box(x - 0.04f, 0, z - 0.04f, x + 0.04f, 0.4f, z + 0.04f, Wood);
            b.Box(-0.62f, 0.4f, -0.42f, 0.22f, 0.95f, 0.48f, Body);
            b.Wedge(-0.66f, 0.95f, -0.5f, 0.26f, 0.55f, 1.12f, 1.38f, Wood).Loose();
            b.Box(-0.3f, 0.45f, -0.43f, -0.1f, 0.7f, -0.42f, Dark).Ghost();
            b.Beam(new Vector3(-0.2f, 0.45f, -0.45f), new Vector3(-0.2f, 0.02f, -0.72f), 0.2f, 0.03f, Wood).Loose();
            // The run: a wooden frame with wire mesh.
            float x0 = 0.24f, x1 = 0.96f;
            foreach (float x in new[] { x0, x1 }) foreach (float z in new[] { -0.7f, 0.7f }) b.Box(x - 0.025f, 0, z - 0.025f, x + 0.025f, 0.95f, z + 0.025f, Wood);
            b.Box(x0, 0.92f, -0.7f, x1, 0.95f, 0.7f, Wood).Ghost();
            for (int i = 0; i < 8; i++) b.Box(x0 + (x1 - x0) * i / 7f - 0.004f, 0.02f, -0.702f, x0 + (x1 - x0) * i / 7f + 0.004f, 0.92f, -0.698f, Metal).Ghost();
            for (int i = 0; i < 6; i++) b.Box(x0, 0.05f + i * 0.17f, -0.702f, x1, 0.058f + i * 0.17f, -0.698f, Metal).Ghost();
            for (int i = 0; i < 9; i++) b.Box(x1 - 0.002f, 0.02f, -0.7f + i * 0.175f - 0.004f, x1 + 0.006f, 0.92f, -0.7f + i * 0.175f + 0.004f, Metal).Ghost();
            b.Box(x0 + 0.1f, 0, -0.3f, x0 + 0.4f, 0.02f, 0.2f, Canvas).Ghost().Rots();
        };

        r["beehive"] = b =>
        {
            foreach (float x in new[] { -0.18f, 0.18f }) foreach (float z in new[] { -0.15f, 0.15f }) b.Box(x - 0.025f, 0, z - 0.025f, x + 0.025f, 0.3f, z + 0.025f, Wood);
            b.Box(-0.23f, 0.28f, -0.2f, 0.23f, 0.32f, 0.2f, Wood);
            for (int i = 0; i < 3; i++)
            {
                DecoPart box = b.Box(-0.21f, 0.32f + i * 0.18f, -0.18f, 0.21f, 0.49f + i * 0.18f, 0.18f, Trim);
                if (i == 2) box.Loose();
            }
            b.Box(-0.24f, 0.86f, -0.21f, 0.24f, 0.92f, 0.21f, Metal);
            b.Box(-0.1f, 0.33f, -0.185f, 0.1f, 0.35f, -0.18f, Dark).Ghost();
        };

        r["greenhouse-bench"] = b =>
        {
            b.Legs(-0.95f, -0.42f, 0.95f, 0.42f, 0.75f, 0.04f, Metal);
            b.Box(-0.97f, 0.75f, -0.45f, 0.97f, 0.78f, 0.45f, Metal);
            for (int i = 0; i < 8; i++) b.Box(-0.95f + i * 0.24f, 0.78f, -0.44f, -0.75f + i * 0.24f, 0.8f, 0.44f, Wood);
            b.Box(-0.9f, 0.2f, -0.4f, 0.9f, 0.22f, 0.4f, Metal).Ghost();
            for (int t = 0; t < 2; t++)
            {
                float x0 = -0.85f + t * 0.9f;
                b.Box(x0, 0.8f, -0.32f, x0 + 0.7f, 0.86f, 0.3f, Dark).Ghost();
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 4; j++)
                        if (b.Rng.Chance(0.7f)) b.Cyl(x0 + 0.08f + i * 0.11f, 0.86f, -0.24f + j * 0.15f, 0.025f, b.Rng.Range(0.04f, 0.12f), Plant, 4, 0.2f).Ghost().Rots();
            }
            foreach (float x in new[] { -0.6f, -0.2f, 0.3f }) b.Cyl(x, 0.22f, 0.15f, 0.08f, 0.13f, Body, 8, 1.3f).Ghost();
            b.Cyl(0.6f, 0.22f, -0.15f, 0.08f, 0.16f, Metal, 8).Ghost();
        };

        r["hydro-rack"] = b =>
        {
            foreach (float x in new[] { -0.95f, 0.95f }) foreach (float z in new[] { -0.42f, 0.42f }) b.Box(x - 0.025f, 0, z - 0.025f, x + 0.025f, 2.4f, z + 0.025f, Metal);
            for (int s = 0; s < 3; s++)
            {
                float y = 0.15f + s * 0.78f;
                b.Box(-0.95f, y, -0.42f, 0.95f, y + 0.04f, 0.42f, Metal);
                b.Box(-0.9f, y + 0.04f, -0.36f, 0.9f, y + 0.14f, 0.36f, Trim);
                for (int i = 0; i < 7; i++)
                    for (int j = 0; j < 3; j++)
                        b.Ball(-0.78f + i * 0.26f, y + 0.2f, -0.24f + j * 0.24f, 0.08f, 0.07f, 0.08f, Plant, 5).Ghost().Rots();
                b.Box(-0.85f, y + 0.6f, -0.08f, 0.85f, y + 0.64f, 0.08f, Lamp).Lit();
                b.Light(0, y + 0.55f, 0, Lamp);
            }
            b.Tube(0.9f, 0.15f, 0.38f, 0.9f, 1.8f, 0.38f, 0.02f, Plastic, 6).Ghost();
            b.Box(0.6f, 0, 0.1f, 0.92f, 0.14f, 0.4f, Dark).Ghost();
        };
    }

    static void Factory(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["conveyor-belt"] = b =>
        {
            foreach (float z in new[] { -0.4f, 0.4f }) b.Box(-1f, 0.6f, z - 0.03f, 1f, 0.76f, z + 0.03f, Metal);
            b.Box(-1f, 0.7f, -0.36f, 1f, 0.745f, 0.36f, Rubber);
            foreach (float x in new[] { -0.95f, 0.95f }) b.Tube(x, 0.7f, -0.37f, x, 0.7f, 0.37f, 0.04f, Metal, 6).Ghost();
            foreach (float x in new[] { -0.75f, 0.75f })
            {
                foreach (float z in new[] { -0.36f, 0.36f }) b.Box(x - 0.03f, 0, z - 0.03f, x + 0.03f, 0.6f, z + 0.03f, Metal);
                b.Box(x - 0.03f, 0.25f, -0.36f, x + 0.03f, 0.29f, 0.36f, Metal).Ghost();
            }
            b.Box(-0.98f, 0.62f, -0.43f, 0.98f, 0.68f, -0.425f, Hazard).Ghost();
            // Left on the belt mid-run.
            b.Box(-0.7f, 0.745f, -0.18f, -0.38f, 0.98f, 0.16f, Body).Loose();
            b.Box(0.05f, 0.745f, -0.2f, 0.35f, 0.9f, 0.12f, Paper);
            b.Box(0.55f, 0.745f, -0.05f, 0.75f, 0.85f, 0.25f, Trim).Loose();
            b.Box(0.82f, 0.76f, 0.43f, 0.9f, 1.0f, 0.47f, Dark).Ghost();
            b.Box(0.83f, 0.92f, 0.42f, 0.89f, 0.97f, 0.43f, Signal).Lit().Ghost();
            b.Light(0.86f, 0.95f, 0.3f, Signal);
        };

        r["robot-arm"] = b =>
        {
            b.Cyl(0, 0, 0.3f, 0.46f, 0.25f, Dark, 10);
            for (int i = 0; i < 8; i++) b.Box(-0.44f + i * 0.11f, 0.08f, -0.168f, -0.39f + i * 0.11f, 0.17f, -0.155f, Hazard).Ghost();
            b.Cyl(0, 0.25f, 0.3f, 0.32f, 0.35f, Body, 10);
            b.Ball(0, 0.75f, 0.3f, 0.2f, Body, 8);
            Vector3 shoulder = new(0, 0.75f, 0.3f), elbow = new(0, 1.95f, -0.15f), wrist = new(0, 1.3f, -0.72f);
            b.Beam(shoulder, elbow, 0.22f, 0.2f, Body);
            b.Ball(elbow.X, elbow.Y, elbow.Z, 0.15f, Dark, 8);
            b.Beam(elbow, wrist, 0.15f, 0.14f, Body).Loose();
            b.Ball(wrist.X, wrist.Y, wrist.Z, 0.1f, Dark, 6);
            b.Tube(wrist, wrist + new Vector3(0, -0.15f, -0.08f), 0.06f, Metal, 6);
            foreach (float s in new[] { -1f, 1f }) b.Box(s * 0.04f - 0.02f, 0.98f, -0.9f, s * 0.04f + 0.02f, 1.13f, -0.8f, Metal).Ghost();
            b.Cable(new Vector3(0.12f, 0.8f, 0.42f), new Vector3(0.1f, 1.85f, -0.05f), 0.025f, Dark, 0.08f);
            b.Cable(new Vector3(0.1f, 1.85f, -0.05f), new Vector3(0.06f, 1.35f, -0.62f), 0.02f, Dark, 0.08f);
            b.Box(0.3f, 0.3f, -0.03f, 0.36f, 0.36f, 0.02f, Signal).Lit().Ghost();
            b.Spark(0, 1.12f, -0.85f);
            b.Light(0.33f, 0.4f, -0.2f, Signal);
        };

        r["gantry-crane"] = b =>
        {
            foreach (float x in new[] { -1.85f, 1.85f })
            {
                b.Box(x - 0.06f, 0, -0.85f, x + 0.06f, 0.08f, 0.85f, Dark);
                foreach (float z in new[] { -0.75f, 0.75f }) b.Beam(new Vector3(x, 0.06f, z), new Vector3(x, 2.36f, 0), 0.1f, 0.1f, Hazard);
                foreach (float z in new[] { -0.75f, 0.75f }) b.Ball(x, 0.05f, z * 1.08f, 0.05f, Rubber, 6).Ghost();
            }
            b.Box(-1.98f, 2.34f, -0.12f, 1.98f, 2.5f, 0.12f, Body);
            b.Box(0.2f, 2.18f, -0.18f, 0.6f, 2.34f, 0.18f, Dark);
            b.Box(0.28f, 2.02f, -0.1f, 0.52f, 2.18f, 0.1f, Hazard);
            // The hoist chain holds a crate just off the floor, slung from its hook.
            b.Cable(new Vector3(0.4f, 2.03f, 0), new Vector3(0.4f, 1.12f, 0), 0.018f, Metal, 0f, breakable: false);
            b.Tube(0.4f, 1.12f, 0, 0.4f, 1.0f, 0, 0.03f, Metal, 6, 0.4f).Ghost();
            b.Cable(new Vector3(0.4f, 1.02f, 0), new Vector3(0.1f, 0.88f, -0.3f), 0.012f, Metal, 0f);
            b.Cable(new Vector3(0.4f, 1.02f, 0), new Vector3(0.7f, 0.88f, 0.3f), 0.012f, Metal, 0f);
            b.Box(0.08f, 0.42f, -0.32f, 0.72f, 0.88f, 0.32f, Wood);
            b.Box(0.07f, 0.6f, -0.33f, 0.73f, 0.66f, 0.33f, Dark).Ghost();
            b.Box(-1.2f, 2.34f, -0.125f, -0.2f, 2.42f, -0.12f, Alert).Ghost();
        };

        r["assembly-station"] = b =>
        {
            b.Legs(-0.95f, -0.42f, 0.95f, 0.42f, 0.86f, 0.06f, Metal);
            b.Box(-0.98f, 0.86f, -0.45f, 0.98f, 0.9f, 0.45f, Metal);
            b.Box(-0.9f, 0.2f, -0.4f, 0.9f, 0.23f, 0.4f, Metal).Ghost();
            b.Box(-0.95f, 0.9f, 0.3f, 0.95f, 1.3f, 0.45f, Dark);
            for (int i = 0; i < 5; i++) b.Box(-0.88f + i * 0.36f, 1.0f, 0.24f, -0.6f + i * 0.36f, 1.12f, 0.3f, i % 2 == 0 ? Trim : Alert).Ghost();
            b.Box(-0.4f, 0.9f, -0.25f, 0.2f, 0.95f, 0.15f, Dark);
            b.Box(-0.3f, 0.95f, -0.18f, 0.1f, 1.2f, 0.08f, Body);
            b.Box(-0.29f, 0.97f, -0.185f, 0.0f, 1.15f, -0.18f, Dark).Loose();
            b.Cable(new Vector3(-0.2f, 1.1f, -0.185f), new Vector3(0.3f, 0.905f, -0.1f), 0.008f, Alert, 0.2f);
            b.Beam(new Vector3(0.7f, 1.3f, 0.38f), new Vector3(0.45f, 1.45f, -0.05f), 0.04f, 0.04f, Metal);
            b.Cyl(0.45f, 1.36f, -0.05f, 0.12f, 0.1f, Dark, 8, 0.6f);
            b.Cyl(0.45f, 1.35f, -0.05f, 0.1f, 0.012f, Lamp, 8).Lit().Ghost();
            ScreenPanel(b, 0.62f, 0.95f, 0.92f, 1.22f, 0.15f, 0.08f, Dark);
            b.Light(0.45f, 1.25f, -0.05f, Lamp);
        };

        r["pallet-racking"] = b =>
        {
            foreach (float x in new[] { -1.45f, 0f, 1.45f }) foreach (float z in new[] { -0.42f, 0.42f }) b.Box(x - 0.04f, 0, z - 0.04f, x + 0.04f, 2.45f, z + 0.04f, Trim);
            foreach (float y in new[] { 0.1f, 1.2f, 2.3f }) foreach (float z in new[] { -0.42f, 0.42f }) b.Box(-1.45f, y, z - 0.04f, 1.45f, y + 0.1f, z + 0.04f, Body);
            int k = 0;
            foreach (float y in new[] { 0.2f, 1.3f })
                foreach (float x in new[] { -0.72f, 0.72f })
                {
                    if (k++ == 2) continue;
                    b.Box(x - 0.62f, y, -0.45f, x + 0.62f, y + 0.12f, 0.45f, Wood);
                    b.Box(x - 0.58f, y + 0.12f, -0.42f, x + 0.55f, y + 0.12f + b.Rng.Range(0.5f, 0.85f), 0.42f, k % 2 == 0 ? Paper : Canvas).Soft();
                }
            b.Box(-1.4f, 2.4f, -0.4f, -0.2f, 2.45f, 0.4f, Wood).Loose();
        };

        r["pallet-stack"] = b =>
        {
            b.Box(-0.62f, 0, -0.52f, 0.62f, 0.04f, 0.52f, Wood);
            foreach (float x in new[] { -0.55f, 0f, 0.55f }) b.Box(x - 0.06f, 0.04f, -0.52f, x + 0.06f, 0.12f, 0.52f, Wood);
            b.Box(-0.62f, 0.12f, -0.52f, 0.62f, 0.15f, 0.52f, Wood);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 2; j++)
                    b.Box(-0.58f + j * 0.58f, 0.15f + i * 0.3f, -0.48f, -0.01f + j * 0.58f, 0.44f + i * 0.3f, 0.48f, Paper).Soft();
            b.Box(-0.6f, 0.16f, -0.5f, 0.6f, 1.06f, 0.5f, Glass).Pane();
            foreach (float x in new[] { -0.6f, 0.58f }) foreach (float z in new[] { -0.5f, 0.48f }) b.Box(x, 0.15f, z, x + 0.02f, 1.05f, z + 0.02f, Paper).Ghost();
        };

        r["control-desk"] = b =>
        {
            b.Box(-0.98f, 0, -0.1f, 0.98f, 0.75f, 0.45f, Body);
            b.Box(-0.97f, 0.05f, -0.105f, 0.97f, 0.1f, -0.1f, Dark).Ghost();
            b.Wedge(-0.98f, 0.75f, -0.45f, 0.98f, 0.2f, 0.82f, 0.98f, Dark);
            for (int i = 0; i < 9; i++)
                for (int j = 0; j < 2; j++)
                {
                    float x = -0.85f + i * 0.21f, t = 0.3f + j * 0.35f;
                    float z = -0.45f + t * 0.65f, y = 0.82f + t * 0.16f;
                    using (b.Turn(new Vector3(x, y, z), -14))
                        b.Box(x - 0.06f, y, z - 0.04f, x + 0.06f, y + 0.012f, z + 0.04f, (i + j) % 3 == 0 ? Signal : (i % 2 == 0 ? Screen : Alert)).Lit().Ghost();
                }
            ScreenPanel(b, -0.9f, 1.0f, -0.05f, 1.32f, 0.25f, 0.15f, Dark);
            ScreenPanel(b, 0.05f, 1.0f, 0.9f, 1.32f, 0.25f, 0.15f, Dark);
            // The window panel behind, looking out over the floor.
            b.Box(-0.98f, 1.4f, 0.42f, 0.98f, 1.46f, 0.48f, Metal);
            b.Box(-0.98f, 1.94f, 0.42f, 0.98f, 2.0f, 0.48f, Metal);
            foreach (float x in new[] { -0.98f, -0.03f, 0.92f }) b.Box(x, 1.46f, 0.42f, x + 0.06f, 1.94f, 0.48f, Metal);
            b.Box(-0.92f, 1.46f, 0.44f, 0.92f, 1.94f, 0.46f, Glass).Pane();
            b.Box(-0.98f, 0.75f, 0.42f, 0.98f, 1.4f, 0.48f, Body);
        };

        r["boiler"] = b =>
        {
            b.Box(-0.95f, 0, -0.72f, 0.95f, 0.15f, 0.72f, Dark);
            foreach (float z in new[] { -0.45f, 0.45f }) b.Box(-0.55f, 0.15f, z - 0.08f, 0.55f, 0.55f, z + 0.08f, Dark);
            b.Tube(0, 1.0f, -0.68f, 0, 1.0f, 0.68f, 0.6f, Rust, 12);
            foreach (float z in new[] { -0.4f, 0f, 0.4f }) b.Tube(0, 1.0f, z - 0.03f, 0, 1.0f, z + 0.03f, 0.62f, Dark, 12).Ghost();
            b.Tube(0, 1.0f, -0.7f, 0, 1.0f, -0.72f, 0.5f, Metal, 12).Ghost();
            b.Box(-0.25f, 0.6f, -0.74f, 0.25f, 0.95f, -0.69f, Dark).Loose().Door(-0.25f, -0.72f, -100);
            b.Box(-0.18f, 0.7f, -0.745f, 0.18f, 0.74f, -0.74f, Lamp).Lit().Ghost();
            Dial(b, -0.2f, 1.3f, -0.66f, 0.08f);
            Dial(b, 0.2f, 1.3f, -0.66f, 0.08f);
            b.Cyl(0, 1.55f, 0.3f, 0.15f, 0.9f, Dark, 8);
            b.Tube(0.4f, 1.4f, -0.2f, 0.85f, 1.4f, -0.2f, 0.07f, Metal, 6);
            b.Tube(0.85f, 1.4f, -0.2f, 0.85f, 0.15f, -0.2f, 0.07f, Metal, 6);
            b.Cyl(-0.3f, 1.55f, -0.1f, 0.05f, 0.18f, Hazard, 6).Ghost();
            b.Light(0, 0.8f, -1.0f, Lamp);
        };
    }
}
