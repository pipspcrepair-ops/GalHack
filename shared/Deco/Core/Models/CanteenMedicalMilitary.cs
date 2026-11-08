using System;
using System.Collections.Generic;
using System.Numerics;
using static GalHack.Deco.DecoSlot;

namespace GalHack.Deco;

public static partial class DecoModels
{
    static void Canteen(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["canteen-table"] = b =>
        {
            b.Box(-0.98f, 0.71f, -0.45f, 0.98f, 0.75f, 0.45f, Plastic);
            b.Box(-0.99f, 0.69f, -0.46f, 0.99f, 0.71f, 0.46f, Trim);
            foreach (float x in new[] { -0.72f, 0.72f })
            {
                b.Box(x - 0.03f, 0, -0.03f, x + 0.03f, 0.69f, 0.03f, Metal);
                b.Box(x - 0.04f, 0, -0.38f, x + 0.04f, 0.04f, 0.38f, Metal);
                b.Box(x - 0.03f, 0.64f, -0.36f, x + 0.03f, 0.69f, 0.36f, Metal);
            }
            b.Box(-0.72f, 0.3f, -0.02f, 0.72f, 0.34f, 0.02f, Metal);
            b.Box(-0.05f, 0.75f, -0.04f, 0.05f, 0.87f, 0.04f, Chrome).Ghost();
            b.Cyl(0.1f, 0.75f, 0, 0.018f, 0.08f, Paper, 6).Ghost();
            b.Cyl(0.15f, 0.75f, 0, 0.018f, 0.08f, Dark, 6).Ghost();
        };

        r["canteen-chair"] = b =>
        {
            foreach (float x in new[] { -0.19f, 0.19f })
                foreach (float z in new[] { -0.18f, 0.18f })
                    b.Tube(x, 0, z, x, 0.44f, z, 0.014f, Metal);
            b.Box(-0.21f, 0.44f, -0.2f, 0.21f, 0.48f, 0.2f, Trim);
            using (b.Turn(new Vector3(0, 0.48f, 0.14f), 8))
            {
                b.Box(-0.2f, 0.56f, 0.12f, 0.2f, 0.88f, 0.16f, Trim).Loose();
                b.Box(-0.015f, 0.46f, 0.13f, 0.015f, 0.6f, 0.15f, Metal);
            }
        };

        r["canteen-bench"] = b =>
        {
            b.Box(-0.98f, 0.42f, -0.17f, 0.98f, 0.47f, 0.17f, Trim);
            foreach (float x in new[] { -0.8f, 0.8f })
            {
                b.Box(x - 0.025f, 0, -0.025f, x + 0.025f, 0.42f, 0.025f, Metal);
                b.Box(x - 0.03f, 0, -0.16f, x + 0.03f, 0.03f, 0.16f, Metal);
                b.Box(x - 0.025f, 0.38f, -0.15f, x + 0.025f, 0.42f, 0.15f, Metal);
            }
            b.Box(-0.8f, 0.15f, -0.015f, 0.8f, 0.18f, 0.015f, Metal);
        };

        r["vending-machine"] = b =>
        {
            b.Box(-0.46f, 0.06f, -0.38f, 0.46f, 1.9f, 0.45f, Trim);
            b.Box(-0.44f, 0, -0.36f, 0.44f, 0.06f, 0.43f, Dark);
            b.Box(-0.42f, 0.6f, -0.395f, 0.12f, 1.72f, -0.38f, Dark);
            for (int s = 0; s < 4; s++)
            {
                float y = 0.66f + s * 0.27f;
                b.Box(-0.4f, y, -0.38f, 0.1f, y + 0.015f, 0.2f, Metal).Ghost();
                for (int i = 0; i < 5; i++)
                {
                    if (b.Rng.Chance(0.25f)) continue; // a few already sold out
                    DecoSlot c = new DecoSlot[] { Fabric, Hazard, Alert, Plastic, Plant }[(i + s) % 5];
                    b.Box(-0.38f + i * 0.095f, y + 0.015f, -0.36f, -0.31f + i * 0.095f, y + 0.17f, -0.25f, c).Ghost();
                }
            }
            b.Box(-0.42f, 0.6f, -0.41f, 0.12f, 1.72f, -0.395f, Glass).Pane();
            b.Box(0.17f, 1.12f, -0.4f, 0.42f, 1.55f, -0.38f, Dark);
            b.Box(0.21f, 1.42f, -0.405f, 0.38f, 1.5f, -0.4f, Screen).Screen();
            Buttons(b, 0.2f, 1.16f, 0.39f, 1.38f, -0.4f, 3, 4, Plastic, Metal);
            b.Box(0.26f, 1.0f, -0.405f, 0.33f, 1.06f, -0.4f, Chrome).Ghost();
            b.Box(-0.38f, 0.22f, -0.41f, 0.08f, 0.45f, -0.38f, Dark).Loose();
            b.Box(-0.44f, 1.74f, -0.41f, 0.44f, 1.88f, -0.38f, Lamp).Lit();
            b.Light(0, 1.3f, -0.7f, Lamp);
        };

        r["serving-counter"] = b =>
        {
            b.Box(-0.98f, 0.08f, -0.32f, 0.98f, 0.86f, 0.45f, Body).As(DecoFinish.Ceramic);
            b.Box(-0.96f, 0, -0.3f, 0.96f, 0.08f, 0.43f, Dark);
            b.Box(-0.99f, 0.86f, -0.4f, 0.99f, 0.9f, 0.47f, Metal);
            b.Box(-0.99f, 0.76f, -0.5f, 0.99f, 0.79f, -0.36f, Chrome);
            foreach (float x in new[] { -0.9f, 0f, 0.9f }) b.Box(x - 0.02f, 0.6f, -0.45f, x + 0.02f, 0.78f, -0.32f, Metal).Ghost();
            for (int i = 0; i < 3; i++)
            {
                float x = -0.6f + i * 0.6f;
                b.Box(x - 0.25f, 0.86f, -0.15f, x + 0.25f, 0.905f, 0.3f, Dark).Ghost();
                b.Box(x - 0.23f, 0.88f, -0.13f, x + 0.23f, 0.92f, 0.28f, Metal).Ghost();
                if (i != 1) b.Box(x - 0.2f, 0.92f, -0.1f, x + 0.2f, 0.95f, 0.25f, Chrome).Loose().Ghost();
            }
            foreach (float x in new[] { -0.95f, 0.95f }) b.Box(x - 0.02f, 0.9f, -0.1f, x + 0.02f, 1.35f, -0.06f, Chrome);
            using (b.Turn(new Vector3(0, 1.32f, -0.08f), -25))
                b.Box(-0.93f, 1.05f, -0.1f, 0.93f, 1.32f, -0.09f, Glass).Pane();
            b.Box(-0.95f, 1.32f, -0.12f, 0.95f, 1.36f, -0.04f, Chrome);
            b.Box(-0.98f, 0.3f, -0.335f, 0.98f, 0.36f, -0.32f, Trim).Ghost();
        };

        r["food-tray"] = b =>
        {
            b.Box(-0.2f, 0, -0.14f, 0.2f, 0.022f, 0.14f, Trim);
            b.Box(-0.2f, 0.022f, -0.14f, 0.2f, 0.035f, -0.13f, Trim).Ghost();
            b.Box(-0.2f, 0.022f, 0.13f, 0.2f, 0.035f, 0.14f, Trim).Ghost();
            b.Cyl(-0.07f, 0.022f, 0, 0.085f, 0.014f, Plastic, 10).Ghost();
            b.Ball(-0.07f, 0.04f, 0, 0.05f, 0.025f, 0.045f, Fabric, 6).Ghost().Rots();
            b.Cyl(0.11f, 0.022f, -0.05f, 0.032f, 0.08f, Plastic, 8).Ghost();
            b.Box(0.07f, 0.022f, 0.04f, 0.17f, 0.04f, 0.1f, Paper).Ghost().Soft();
            b.Box(-0.18f, 0.024f, 0.09f, 0.0f, 0.03f, 0.105f, Chrome).Ghost();
        };

        r["tray-rack"] = b =>
        {
            foreach (float x in new[] { -0.45f, 0.45f })
                foreach (float z in new[] { -0.2f, 0.2f })
                {
                    b.Tube(x, 0.08f, z, x, 1.4f, z, 0.015f, Metal);
                    b.Ball(x, 0.04f, z, 0.04f, Rubber, 6).Ghost();
                }
            b.Box(-0.47f, 0.06f, -0.22f, 0.47f, 0.1f, 0.22f, Metal);
            b.Tube(-0.45f, 1.42f, -0.2f, -0.45f, 1.42f, 0.2f, 0.015f, Metal);
            b.Tube(0.45f, 1.42f, -0.2f, 0.45f, 1.42f, 0.2f, 0.015f, Metal);
            b.Tube(-0.45f, 1.42f, -0.2f, 0.45f, 1.42f, -0.2f, 0.018f, Metal);
            for (int i = 0; i < 9; i++)
            {
                float y = 0.2f + i * 0.13f;
                if (b.Rng.Chance(0.35f)) continue;
                b.Box(-0.42f, y, -0.18f, 0.42f, y + 0.02f, 0.18f, Trim).Loose().Ghost();
            }
        };

        r["drink-dispenser"] = b =>
        {
            b.Box(-0.2f, 0, -0.2f, 0.2f, 0.95f, 0.2f, Body);
            b.Box(-0.18f, 0.6f, -0.215f, 0.18f, 0.66f, -0.2f, Dark);
            b.Box(-0.16f, 0.62f, -0.24f, 0.16f, 0.64f, -0.1f, Metal).Ghost();
            foreach (float x in new[] { -0.09f, 0.09f })
            {
                b.Box(x - 0.08f, 0.95f, -0.15f, x + 0.08f, 1.38f, 0.15f, Glass).Pane();
                b.Box(x - 0.07f, 0.96f, -0.14f, x + 0.07f, x < 0 ? 1.25f : 1.05f, 0.14f, x < 0 ? Hazard : Alert).Ghost();
                b.Box(x - 0.02f, 0.86f, -0.24f, x + 0.02f, 0.92f, -0.2f, Dark).Ghost();
            }
            b.Box(-0.19f, 1.38f, -0.16f, 0.19f, 1.42f, 0.16f, Body);
            b.Box(-0.17f, 0.75f, -0.205f, 0.17f, 0.82f, -0.2f, Trim).Ghost();
            b.Box(0.12f, 0.88f, -0.205f, 0.16f, 0.91f, -0.2f, Signal).Lit().Ghost();
            b.Light(0, 1.1f, -0.4f, Signal);
        };

        r["cafe-table"] = b =>
        {
            b.Cyl(0, 0, 0, 0.26f, 0.035f, Metal, 8);
            b.Cyl(0, 0.035f, 0, 0.035f, 0.68f, Metal, 6);
            b.Cyl(0, 0.71f, 0, 0.44f, 0.03f, Plastic, 12);
            b.Cyl(0, 0.705f, 0, 0.445f, 0.012f, Trim, 12).Ghost();
            b.Cyl(0.12f, 0.74f, 0.08f, 0.04f, 0.09f, Glass, 6).Ghost().Pane();
            b.Cyl(-0.15f, 0.74f, -0.1f, 0.06f, 0.012f, Paper, 8).Ghost();
        };
    }

    static void Medical(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["hospital-bed"] = b =>
        {
            foreach (float x in new[] { -0.38f, 0.38f })
                foreach (float z in new[] { -0.85f, 0.85f })
                {
                    b.Box(x - 0.025f, 0.08f, z - 0.025f, x + 0.025f, 0.38f, z + 0.025f, Metal);
                    b.Ball(x, 0.05f, z, 0.05f, Rubber, 6).Ghost();
                }
            b.Box(-0.44f, 0.36f, -0.95f, 0.44f, 0.44f, 0.92f, Metal);
            b.Box(-0.42f, 0.44f, -0.92f, 0.42f, 0.6f, 0.9f, Linen).Soft();
            b.Box(-0.3f, 0.6f, 0.58f, 0.3f, 0.7f, 0.88f, Linen).Soft();
            b.Box(-0.43f, 0.58f, -0.9f, 0.43f, 0.64f, 0.1f, Fabric).Soft();
            b.Box(-0.47f, 0.3f, 0.92f, 0.47f, 0.98f, 0.99f, Body);
            b.Box(-0.47f, 0.82f, 0.915f, 0.47f, 0.88f, 0.925f, Trim).Ghost();
            b.Box(-0.47f, 0.3f, -0.99f, 0.47f, 0.76f, -0.93f, Body);
            b.Box(-0.15f, 0.55f, -1.0f, 0.15f, 0.72f, -0.99f, Paper).Ghost();
            foreach (float x in new[] { -0.48f, 0.45f })
                b.Box(x, 0.6f, -0.35f, x + 0.03f, 0.74f, 0.65f, Metal).Loose();
        };

        r["gurney"] = b =>
        {
            foreach (float x in new[] { -0.28f, 0.28f })
                foreach (float z in new[] { -0.8f, 0.8f })
                {
                    b.Tube(x, 0.1f, z, x, 0.62f, z * 0.9f, 0.02f, Metal);
                    b.Ball(x, 0.05f, z, 0.05f, Rubber, 6).Ghost();
                }
            b.Box(-0.3f, 0.22f, -0.75f, 0.3f, 0.25f, 0.75f, Metal);
            b.Box(-0.32f, 0.62f, -0.95f, 0.32f, 0.68f, 0.95f, Metal);
            b.Box(-0.3f, 0.68f, -0.93f, 0.3f, 0.76f, 0.93f, Fabric).Soft();
            using (b.Turn(new Vector3(0.3f, 0.76f, 0), 0, 0, -50))
                b.Box(0.1f, 0.76f, -0.6f, 0.36f, 0.775f, 0.3f, Linen).Soft().Ghost();
            b.Box(-0.3f, 0.76f, -0.25f, 0.3f, 0.79f, 0.4f, Linen).Soft();
            foreach (float x in new[] { -0.28f, 0.28f }) b.Tube(x, 0.68f, 0.95f, x, 0.95f, 0.98f, 0.015f, Metal);
            b.Tube(-0.28f, 0.95f, 0.98f, 0.28f, 0.95f, 0.98f, 0.015f, Metal);
        };

        r["iv-stand"] = b =>
        {
            for (int i = 0; i < 5; i++)
            {
                float a = i * MathF.Tau / 5;
                b.Beam(0, 0.06f, 0, MathF.Cos(a) * 0.2f, 0.03f, MathF.Sin(a) * 0.2f, 0.03f, Metal);
                b.Ball(MathF.Cos(a) * 0.2f, 0.03f, MathF.Sin(a) * 0.2f, 0.03f, Rubber, 6).Ghost();
            }
            b.Tube(0, 0.04f, 0, 0, 1.88f, 0, 0.013f, Chrome);
            b.Tube(-0.14f, 1.86f, 0, 0.14f, 1.86f, 0, 0.01f, Chrome);
            b.Box(-0.13f, 1.5f, -0.03f, -0.01f, 1.82f, 0.03f, Glass).Pane();
            b.Box(-0.12f, 1.52f, -0.02f, -0.02f, 1.62f, 0.02f, Liquid).Ghost();
            b.Cable(new Vector3(-0.07f, 1.51f, 0), new Vector3(-0.02f, 1.19f, -0.03f), 0.005f, Plastic, 0.12f);
            b.Box(-0.06f, 1.02f, -0.07f, 0.07f, 1.2f, 0.04f, Body).Loose();
            b.Box(-0.04f, 1.12f, -0.075f, 0.05f, 1.17f, -0.07f, Signal).Lit().Ghost();
        };

        r["med-cabinet"] = b =>
        {
            b.Box(-0.45f, 0, -0.05f, 0.45f, 1.36f, 0.25f, Body);
            b.Box(-0.46f, 1.36f, -0.07f, 0.46f, 1.49f, 0.25f, Body);
            b.Box(-0.08f, 1.4f, -0.08f, 0.08f, 1.45f, -0.07f, Alert).Ghost();
            b.Box(-0.025f, 1.37f, -0.08f, 0.025f, 1.48f, -0.07f, Alert).Ghost();
            for (int s = 0; s < 3; s++)
            {
                float y = 0.05f + s * 0.43f;
                b.Box(-0.42f, y, -0.04f, 0.42f, y + 0.02f, 0.22f, Metal).Ghost();
                for (int i = 0; i < 6; i++)
                {
                    if (b.Rng.Chance(0.3f)) continue;
                    DecoSlot c = new DecoSlot[] { Plastic, Alert, Trim, Paper }[(i + s) % 4];
                    b.Cyl(-0.34f + i * 0.13f, y + 0.02f, 0.06f, 0.035f, b.Rng.Range(0.1f, 0.2f), c, 6).Ghost();
                }
            }
            b.Box(-0.43f, 0.03f, -0.08f, -0.01f, 1.33f, -0.06f, Glass).Pane().Door(-0.43f, -0.07f, 70);
            b.Box(0.01f, 0.03f, -0.08f, 0.43f, 1.33f, -0.06f, Glass).Pane().Door(0.43f, -0.07f, -70);
            b.Box(-0.03f, 0.6f, -0.1f, -0.01f, 0.78f, -0.08f, Chrome).Ghost();
            b.Box(0.01f, 0.6f, -0.1f, 0.03f, 0.78f, -0.08f, Chrome).Ghost();
        };

        r["surgical-lamp"] = b =>
        {
            b.Cyl(0, 0.94f, 0, 0.13f, 0.06f, Body, 8);
            b.Tube(0, 0.95f, 0, 0.22f, 0.62f, 0.12f, 0.03f, Metal);
            b.Tube(0.22f, 0.62f, 0.12f, 0, 0.3f, 0, 0.03f, Metal);
            b.Ball(0.22f, 0.62f, 0.12f, 0.045f, Metal, 6).Ghost();
            b.Cyl(0, 0.08f, 0, 0.4f, 0.22f, Body, 12, 0.5f);
            b.Cyl(0, 0.06f, 0, 0.36f, 0.025f, Lamp, 12).Lit();
            for (int i = 0; i < 6; i++)
            {
                float a = i * MathF.Tau / 6;
                b.Ball(MathF.Cos(a) * 0.2f, 0.06f, MathF.Sin(a) * 0.2f, 0.05f, Lamp, 6).Lit().Ghost();
            }
            b.Cyl(0, 0, 0, 0.04f, 0.08f, Metal, 6).Ghost();
            b.Light(0, -0.1f, 0, Lamp);
        };

        r["privacy-curtain"] = b =>
        {
            foreach (float x in new[] { -0.96f, 0.96f })
            {
                b.Tube(x, 0, 0, x, 1.96f, 0, 0.018f, Metal);
                b.Box(x - 0.03f, 0, -0.22f, x + 0.03f, 0.03f, 0.22f, Metal);
            }
            b.Tube(-0.96f, 1.94f, 0, 0.96f, 1.94f, 0, 0.018f, Metal);
            for (int i = 0; i < 10; i++)
            {
                float x0 = -0.92f + i * 0.184f;
                float tilt = i % 2 == 0 ? 14 : -14;
                using (b.Turn(new Vector3(x0 + 0.092f, 1, 0), 0, tilt))
                {
                    DecoPart panel = b.Box(x0, 0.22f, -0.012f, x0 + 0.184f, 1.88f, 0.012f, Fabric).Soft();
                    if (i == 3 || i == 7) panel.Loose();
                }
                b.Cyl(x0 + 0.05f, 1.88f, 0, 0.02f, 0.06f, Metal, 6).Ghost();
            }
        };

        r["operating-table"] = b =>
        {
            b.Box(-0.36f, 0, -0.6f, 0.36f, 0.08f, 0.6f, Body);
            b.Box(-0.16f, 0.08f, -0.28f, 0.16f, 0.74f, 0.28f, Metal);
            b.Box(-0.3f, 0.74f, -0.95f, 0.3f, 0.84f, 0.95f, Body);
            b.Box(-0.28f, 0.84f, -0.93f, 0.28f, 0.9f, 0.93f, Fabric).Soft();
            b.Box(-0.16f, 0.88f, 0.75f, 0.16f, 0.93f, 0.95f, Fabric).Soft();
            b.Box(-0.49f, 0.8f, 0.15f, -0.3f, 0.84f, 0.38f, Body).Loose();
            b.Box(0.3f, 0.8f, 0.15f, 0.49f, 0.84f, 0.38f, Body).Loose();
            b.Box(-0.31f, 0.74f, -0.2f, 0.31f, 0.78f, 0.2f, Trim).Ghost();
            b.Box(-0.1f, 0.4f, -0.29f, 0.1f, 0.55f, -0.28f, Dark).Ghost();
        };

        r["vitals-monitor"] = b =>
        {
            for (int i = 0; i < 5; i++)
            {
                float a = i * MathF.Tau / 5 + 0.3f;
                b.Beam(0, 0.06f, 0, MathF.Cos(a) * 0.2f, 0.03f, MathF.Sin(a) * 0.2f, 0.03f, Dark);
                b.Ball(MathF.Cos(a) * 0.2f, 0.03f, MathF.Sin(a) * 0.2f, 0.03f, Rubber, 6).Ghost();
            }
            b.Tube(0, 0.05f, 0, 0, 1.05f, 0, 0.018f, Metal);
            b.Box(-0.13f, 0.62f, -0.08f, 0.13f, 0.7f, 0.08f, Metal).Ghost();
            b.Box(-0.22f, 1.02f, -0.1f, 0.22f, 1.38f, 0.12f, Body);
            b.Box(-0.19f, 1.08f, -0.106f, 0.19f, 1.34f, -0.1f, Screen).Screen();
            // The pulse trace, bright on the screen.
            float[] ys = { 1.2f, 1.2f, 1.26f, 1.13f, 1.22f, 1.2f, 1.2f };
            for (int i = 0; i + 1 < ys.Length; i++)
                b.Beam(new Vector3(-0.16f + i * 0.053f, ys[i], -0.11f), new Vector3(-0.16f + (i + 1) * 0.053f, ys[i + 1], -0.11f), 0.008f, 0.004f, Signal).Lit().Ghost();
            Buttons(b, -0.18f, 1.03f, 0.18f, 1.07f, -0.1f, 5, 1, Alert, Signal, Plastic);
            b.Light(0, 1.2f, -0.35f, Screen);
        };

        r["wheelchair"] = b =>
        {
            foreach (float x in new[] { -0.41f, 0.41f })
            {
                b.Tube(x - 0.025f, 0.31f, 0.06f, x + 0.025f, 0.31f, 0.06f, 0.3f, Rubber, 12).Tyre();
                b.Tube(x - 0.03f, 0.31f, 0.06f, x + 0.03f, 0.31f, 0.06f, 0.26f, Metal, 12).Ghost();
                b.Tube(x * 0.95f, 0.31f, 0.06f, x * 0.62f, 0.31f, 0.06f, 0.02f, Metal);
                b.Ball(x * 0.5f, 0.05f, -0.34f, 0.05f, Rubber, 6).Ghost();
                b.Tube(x * 0.6f, 0.45f, -0.28f, x * 0.5f, 0.08f, -0.34f, 0.015f, Metal);
                b.Box(x * 0.6f - 0.07f, 0.1f, -0.46f, x * 0.6f + 0.07f, 0.12f, -0.34f, Metal);
                b.Tube(x * 0.62f, 0.47f, 0.24f, x * 0.62f, 0.98f, 0.3f, 0.015f, Metal);
                b.Tube(x * 0.62f, 0.98f, 0.3f, x * 0.62f, 0.98f, 0.4f, 0.018f, Rubber);
                b.Box(x * 0.62f - 0.025f, 0.66f, -0.18f, x * 0.62f + 0.025f, 0.69f, 0.2f, Dark).Loose();
            }
            b.Box(-0.25f, 0.45f, -0.25f, 0.25f, 0.5f, 0.22f, Fabric).Soft();
            b.Box(-0.25f, 0.55f, 0.22f, 0.25f, 0.95f, 0.26f, Fabric).Soft().Loose();
        };
    }

    static void Military(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["weapon-rack"] = b =>
        {
            b.Box(-0.72f, 0, 0.14f, 0.72f, 1.85f, 0.24f, Body);
            b.Box(-0.74f, 0, -0.22f, -0.68f, 1.85f, 0.24f, Body);
            b.Box(0.68f, 0, -0.22f, 0.74f, 1.85f, 0.24f, Body);
            b.Box(-0.68f, 0, -0.22f, 0.68f, 0.22f, 0.14f, Body);
            b.Box(-0.68f, 1.18f, -0.06f, 0.68f, 1.26f, 0.14f, Dark);
            b.Box(-0.68f, 1.62f, -0.2f, 0.68f, 1.66f, 0.14f, Dark);
            for (int i = 0; i < 6; i++)
            {
                float x = -0.55f + i * 0.22f;
                if (i == 2 || i == 4)
                {
                    // Taken: chalk outlines where these stood.
                    b.Box(x - 0.012f, 0.6f, 0.135f, x + 0.012f, 1.3f, 0.14f, Paper).Ghost();
                    continue;
                }
                DecoPart stock = b.Box(x - 0.03f, 0.22f, -0.07f, x + 0.03f, 0.52f, 0.05f, Wood);
                b.Box(x - 0.025f, 0.52f, -0.05f, x + 0.025f, 0.95f, 0.06f, Dark).Ghost();
                b.Box(x - 0.02f, 0.62f, -0.13f, x + 0.02f, 0.78f, -0.05f, Dark).Ghost();
                b.Tube(x, 0.95f, 0.02f, x, 1.4f, 0.04f, 0.012f, Metal, 5).Ghost();
                if (i == 5) stock.Loose();
            }
            b.Tube(-0.7f, 0.8f, -0.12f, 0.7f, 0.8f, -0.12f, 0.012f, Metal).Ghost();
            b.Box(0.5f, 1.68f, -0.15f, 0.65f, 1.82f, -0.05f, Canvas).Ghost();
        };

        r["ammo-crate"] = b =>
        {
            b.Box(-0.45f, 0.03f, -0.21f, 0.45f, 0.32f, 0.21f, Body);
            b.Box(-0.42f, 0, -0.19f, -0.32f, 0.03f, 0.19f, Dark);
            b.Box(0.32f, 0, -0.19f, 0.42f, 0.03f, 0.19f, Dark);
            b.Box(-0.46f, 0.32f, -0.22f, 0.46f, 0.38f, 0.22f, Body).Loose();
            foreach (float x in new[] { -0.3f, 0.3f }) b.Box(x - 0.03f, 0.25f, -0.225f, x + 0.03f, 0.35f, -0.21f, Metal).Ghost();
            b.Box(-0.26f, 0.1f, -0.215f, 0.22f, 0.2f, -0.21f, Trim).Ghost();
            foreach (float x in new[] { -0.47f, 0.45f }) b.Box(x, 0.16f, -0.08f, x + 0.02f, 0.2f, 0.08f, Dark).Ghost();
        };

        r["sandbag-wall"] = b =>
        {
            int layers = 5;
            for (int l = 0; l < layers; l++)
            {
                float y = 0.085f + l * 0.15f;
                float off = l % 2 == 0 ? 0 : 0.2f;
                float[] rows = l < 3 ? new[] { -0.2f, 0.2f } : new[] { 0f };
                foreach (float z in rows)
                    for (float x = -0.52f + off; x <= 0.55f; x += 0.4f)
                    {
                        DecoPart bag = b.Ball(x, y, z, 0.21f, 0.085f, 0.18f, Canvas, 8);
                        if (l == layers - 1) bag.Loose();
                    }
            }
        };

        r["army-cot"] = b =>
        {
            foreach (float x in new[] { -0.38f, 0.38f }) b.Tube(x, 0.38f, -0.95f, x, 0.38f, 0.95f, 0.02f, Metal);
            b.Box(-0.37f, 0.36f, -0.93f, 0.37f, 0.4f, 0.93f, Canvas).Soft();
            foreach (float z in new[] { -0.8f, 0f, 0.8f })
            {
                b.Beam(-0.38f, 0.38f, z, 0.36f, 0.02f, z, 0.025f, Metal);
                foreach (float x in new[] { -0.36f, 0.36f }) b.Box(x - 0.03f, 0, z - 0.025f, x + 0.03f, 0.02f, z + 0.025f, Rubber).Ghost();
                b.Beam(0.38f, 0.38f, z, -0.36f, 0.02f, z, 0.025f, Metal);
            }
            b.Tube(-0.3f, 0.43f, 0.75f, 0.3f, 0.43f, 0.75f, 0.065f, Canvas, 8).Soft();
            b.Box(-0.35f, 0.4f, -0.9f, 0.35f, 0.48f, -0.55f, Fabric).Soft();
        };

        r["map-table"] = b =>
        {
            b.Box(-0.94f, 0, -0.68f, 0.94f, 0.08f, 0.68f, Dark);
            b.Box(-0.9f, 0.08f, -0.64f, 0.9f, 0.82f, 0.64f, Body);
            b.Box(-0.98f, 0.82f, -0.73f, 0.98f, 0.88f, 0.73f, Dark);
            b.Box(-0.9f, 0.875f, -0.65f, 0.9f, 0.885f, 0.65f, Screen).Screen();
            for (int i = 0; i < 5; i++)
            {
                float x = b.Rng.Range(-0.7f, 0.6f), z = b.Rng.Range(-0.5f, 0.4f);
                using (b.Turn(new Vector3(x, 0.89f, z), 0, b.Rng.Range(0, 90)))
                    b.Box(x - b.Rng.Range(0.08f, 0.22f), 0.886f, z - b.Rng.Range(0.05f, 0.14f), x + b.Rng.Range(0.08f, 0.2f), 0.888f, z + b.Rng.Range(0.05f, 0.12f), Dark).Ghost();
            }
            for (int i = 0; i < 6; i++)
            {
                float x = b.Rng.Range(-0.8f, 0.8f), z = b.Rng.Range(-0.55f, 0.55f);
                b.Cyl(x, 0.885f, z, 0.012f, 0.07f, i < 4 ? Alert : Trim, 5).Ghost();
                b.Ball(x, 0.96f, z, 0.022f, i < 4 ? Alert : Trim, 5).Ghost();
            }
            b.Box(-0.97f, 0.4f, -0.69f, 0.97f, 0.46f, -0.68f, Trim).Ghost();
            b.Light(0, 1.2f, 0, Screen);
        };

        r["footlocker"] = b =>
        {
            b.Box(-0.45f, 0, -0.2f, 0.45f, 0.36f, 0.2f, Body);
            b.Taper(-0.46f, 0.36f, -0.21f, 0.46f, 0.44f, 0.21f, 0.97f, 0.85f, Body).Loose();
            foreach (float x in new[] { -0.44f, 0.4f })
                foreach (float z in new[] { -0.2f, 0.17f })
                    b.Box(x, 0.02f, z, x + 0.04f, 0.4f, z + 0.03f, Metal).Ghost();
            b.Box(-0.04f, 0.26f, -0.215f, 0.04f, 0.38f, -0.2f, Metal).Ghost();
            b.Box(-0.03f, 0.22f, -0.225f, 0.03f, 0.27f, -0.215f, Trim).Ghost();
            b.Box(0.15f, 0.12f, -0.205f, 0.38f, 0.2f, -0.2f, Paper).Ghost();
        };

        r["field-radio"] = b =>
        {
            b.Box(-0.2f, 0, -0.12f, 0.2f, 0.24f, 0.14f, Body);
            b.Box(-0.18f, 0.02f, -0.125f, 0.18f, 0.22f, -0.12f, Dark);
            for (int i = 0; i < 3; i++) b.Tube(-0.12f + i * 0.09f, 0.08f, -0.125f, -0.12f + i * 0.09f, 0.08f, -0.16f, 0.025f, Metal, 6).Ghost();
            b.Box(-0.15f, 0.15f, -0.13f, 0.05f, 0.19f, -0.125f, Screen).Screen();
            b.Box(0.1f, 0.16f, -0.13f, 0.13f, 0.19f, -0.125f, Signal).Lit().Ghost();
            // The whip aerial, set in its socket in the case.
            b.Tube(0.15f, 0.2f, 0.1f, 0.19f, 0.48f, 0.12f, 0.006f, Metal, 4).Ghost();
            b.Box(0.2f, 0.04f, -0.06f, 0.24f, 0.2f, 0.04f, Dark).Loose();
            b.Light(0, 0.2f, -0.3f, Signal);
        };

        r["gun-locker"] = b =>
        {
            b.Box(-0.47f, 0, -0.18f, 0.47f, 1.95f, 0.24f, Body);
            b.Box(-0.46f, 0.06f, -0.21f, -0.01f, 1.9f, -0.18f, Body).Loose().Door(-0.46f, -0.2f, 80);
            b.Box(0.01f, 0.06f, -0.21f, 0.46f, 1.9f, -0.18f, Body).Door(0.46f, -0.2f, -80);
            foreach (float x0 in new[] { -0.4f, 0.07f })
                for (int i = 0; i < 4; i++) b.Box(x0, 1.55f + i * 0.06f, -0.215f, x0 + 0.33f, 1.57f + i * 0.06f, -0.21f, Dark).Ghost();
            b.Box(-0.05f, 0.9f, -0.23f, -0.02f, 1.1f, -0.21f, Chrome).Ghost();
            b.Box(0.02f, 0.9f, -0.23f, 0.05f, 1.1f, -0.21f, Chrome).Ghost();
            b.Box(-0.3f, 1.25f, -0.215f, -0.12f, 1.35f, -0.21f, Trim).Ghost();
            b.Box(0.12f, 1.25f, -0.215f, 0.3f, 1.35f, -0.21f, Trim).Ghost();
        };
    }
}
