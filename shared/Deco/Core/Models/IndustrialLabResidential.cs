using System;
using System.Collections.Generic;
using System.Numerics;
using static GalHack.Deco.DecoSlot;

namespace GalHack.Deco;

public static partial class DecoModels
{
    static void Industrial(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["pipe-run"] = b =>
        {
            (float y, float rad, DecoSlot s)[] pipes = { (0.24f, 0.09f, Metal), (0.55f, 0.07f, Trim), (0.82f, 0.05f, Hazard) };
            foreach (var (y, rad, s) in pipes)
            {
                b.Tube(-1f, y, 0.1f, 1f, y, 0.1f, rad, s, 8);
                foreach (float x in new[] { -0.55f, 0.55f }) b.Tube(x - 0.025f, y, 0.1f, x + 0.025f, y, 0.1f, rad + 0.025f, Dark, 8).Ghost();
            }
            foreach (float x in new[] { -0.82f, 0.82f })
            {
                b.Box(x - 0.03f, 0, 0.05f, x + 0.03f, 0.9f, 0.15f, Dark);
                b.Box(x - 0.08f, 0, 0.0f, x + 0.08f, 0.03f, 0.2f, Dark);
                b.Box(x - 0.05f, 0.0f, 0.2f, x + 0.05f, 0.95f, 0.25f, Metal);
            }
            Dial(b, 0.2f, 0.66f, 0.03f, 0.05f);
            b.Tube(0.2f, 0.55f, 0.1f, 0.2f, 0.62f, 0.05f, 0.012f, Metal).Ghost();
        };

        r["valve-assembly"] = b =>
        {
            b.Box(-0.2f, 0, -0.1f, 0.2f, 0.04f, 0.22f, Dark);
            b.Tube(0, 0.04f, 0.08f, 0, 1.46f, 0.08f, 0.08f, Metal, 8);
            b.Tube(-0.49f, 0.7f, 0.08f, 0.49f, 0.7f, 0.08f, 0.065f, Metal, 8);
            b.Box(-0.12f, 0.58f, -0.06f, 0.12f, 0.82f, 0.2f, Dark);
            b.Tube(0, 0.7f, -0.06f, 0, 0.7f, -0.15f, 0.02f, Metal).Ghost();
            Ring(b, new Vector3(0, 0.7f, -0.17f), 0.17f, 8, 0.025f, Alert, 90);
            b.Beam(-0.17f, 0.7f, -0.17f, 0.17f, 0.7f, -0.17f, 0.018f, Alert).Ghost();
            b.Beam(0, 0.53f, -0.17f, 0, 0.87f, -0.17f, 0.018f, Alert).Ghost();
            foreach (float y in new[] { 0.3f, 1.15f }) b.Tube(0, y - 0.02f, 0.08f, 0, y + 0.02f, 0.08f, 0.11f, Dark, 8).Ghost();
            Dial(b, 0.25f, 1.0f, 0.02f, 0.07f);
            b.Tube(0.08f, 1.0f, 0.08f, 0.25f, 1.0f, 0.04f, 0.012f, Metal).Ghost();
            b.Box(-0.4f, 0.9f, -0.0f, -0.12f, 0.95f, 0.01f, Hazard).Ghost();
        };

        r["control-panel"] = b =>
        {
            b.Box(-0.45f, 0, -0.04f, 0.45f, 1.45f, 0.24f, Body);
            b.Box(-0.44f, 0, -0.03f, 0.44f, 0.08f, 0.23f, Dark);
            b.Box(-0.45f, 0.82f, -0.24f, 0.45f, 0.88f, -0.04f, Dark);
            ScreenPanel(b, -0.36f, 1.05f, 0.05f, 1.36f, -0.05f, 0.02f, Dark);
            Dial(b, 0.2f, 1.26f, -0.045f, 0.075f);
            Dial(b, 0.2f, 1.07f, -0.045f, 0.06f);
            Buttons(b, -0.4f, 0.9f, 0.4f, 1.0f, -0.045f, 8, 1, Signal, Alert, Hazard, Signal);
            for (int i = 0; i < 6; i++) b.Box(-0.38f + i * 0.13f, 0.88f, -0.2f, -0.3f + i * 0.13f, 0.92f, -0.12f, i % 3 == 0 ? Alert : Plastic).Ghost();
            b.Tube(0.35f, 0.88f, -0.14f, 0.38f, 1.05f, -0.22f, 0.015f, Metal).Loose();
            b.Ball(0.38f, 1.06f, -0.22f, 0.035f, Alert, 6).Ghost();
            b.Box(-0.44f, 0.4f, -0.05f, 0.44f, 0.75f, -0.04f, Body).Loose();
            b.Box(-0.05f, 0.55f, -0.06f, 0.05f, 0.62f, -0.05f, Hazard).Ghost();
            b.Spark(-0.1f, 1.2f, -0.08f);
            b.Light(0, 1.1f, -0.4f, Screen);
        };

        r["oil-barrel"] = b =>
        {
            b.Cyl(0, 0, 0, 0.23f, 0.9f, Body, 10);
            foreach (float y in new[] { 0.02f, 0.3f, 0.6f, 0.86f }) b.Cyl(0, y, 0, 0.237f, 0.03f, Dark, 10).Ghost();
            b.Cyl(0.1f, 0.9f, 0.05f, 0.03f, 0.02f, Metal, 6).Ghost();
            b.Cyl(-0.1f, 0.9f, -0.05f, 0.02f, 0.02f, Metal, 6).Ghost();
            b.Box(-0.1f, 0.4f, -0.236f, 0.1f, 0.55f, -0.23f, Hazard).Ghost();
            for (int i = 0; i < 3; i++)
            {
                float a = b.Rng.Range(0, MathF.Tau);
                float x = MathF.Cos(a) * 0.232f, z = MathF.Sin(a) * 0.232f;
                b.Box(x - 0.015f, 0.05f, z - 0.015f, x + 0.015f, b.Rng.Range(0.3f, 0.7f), z + 0.015f, Rust).Ghost();
            }
        };

        r["workbench"] = b =>
        {
            b.Box(-0.98f, 0.83f, -0.45f, 0.98f, 0.9f, 0.45f, Wood);
            b.Legs(-0.95f, -0.42f, 0.95f, 0.42f, 0.83f, 0.06f, Metal);
            b.Box(-0.92f, 0.15f, -0.38f, 0.92f, 0.18f, 0.38f, Metal);
            b.Box(0.2f, 0.62f, -0.42f, 0.9f, 0.83f, 0.4f, Body);
            for (int i = 0; i < 2; i++) b.Box(0.24f + i * 0.33f, 0.66f, -0.43f, 0.53f + i * 0.33f, 0.8f, -0.42f, Body).Loose();
            b.Box(-0.88f, 0.9f, -0.48f, -0.62f, 1.0f, -0.36f, Dark);
            b.Box(-0.8f, 0.92f, -0.5f, -0.7f, 0.96f, -0.48f, Metal).Ghost();
            b.Box(0.35f, 0.9f, -0.05f, 0.75f, 0.99f, 0.15f, Alert).Loose();
            b.Box(0.5f, 0.99f, 0.03f, 0.6f, 1.0f, 0.07f, Dark).Ghost();
            b.Box(-0.3f, 0.9f, -0.2f, -0.05f, 0.915f, -0.17f, Wood).Ghost();
            b.Box(-0.08f, 0.9f, -0.23f, -0.03f, 0.94f, -0.14f, Metal).Ghost();
            b.Box(0.0f, 0.9f, 0.1f, 0.2f, 0.91f, 0.12f, Metal).Ghost();
            b.Cyl(-0.45f, 0.9f, 0.2f, 0.05f, 0.08f, Dark, 6).Ghost();
        };

        r["tool-rack"] = b =>
        {
            b.Box(-0.72f, 0, 0.18f, 0.72f, 1.4f, 0.24f, Wood);
            b.Box(-0.74f, 0, 0.16f, -0.7f, 1.42f, 0.25f, Metal);
            b.Box(0.7f, 0, 0.16f, 0.74f, 1.42f, 0.25f, Metal);
            b.Box(-0.74f, 1.4f, 0.16f, 0.74f, 1.44f, 0.25f, Metal);
            float[] xs = { -0.55f, -0.35f, -0.15f, 0.08f, 0.32f, 0.55f };
            for (int i = 0; i < xs.Length; i++)
            {
                float x = xs[i];
                float top = 1.25f - (i % 2) * 0.55f;
                if (i == 3)
                {
                    // Missing: only its painted outline on the shadow board is left.
                    b.Box(x - 0.012f, top - 0.38f, 0.172f, x + 0.012f, top, 0.18f, Paper).Ghost();
                    b.Box(x - 0.07f, top - 0.06f, 0.172f, x + 0.07f, top - 0.04f, 0.18f, Paper).Ghost();
                    continue;
                }
                b.Box(x - 0.01f, top - 0.02f, 0.12f, x + 0.01f, top, 0.18f, Metal).Ghost();
                switch (i % 3)
                {
                    case 0:
                        b.Box(x - 0.015f, top - 0.38f, 0.14f, x + 0.015f, top - 0.04f, 0.17f, Wood).Loose();
                        b.Box(x - 0.07f, top - 0.08f, 0.13f, x + 0.07f, top - 0.03f, 0.17f, Metal).Ghost();
                        break;
                    case 1:
                        b.Box(x - 0.018f, top - 0.34f, 0.15f, x + 0.018f, top - 0.04f, 0.17f, Metal).Loose();
                        b.Box(x - 0.04f, top - 0.38f, 0.15f, x + 0.04f, top - 0.32f, 0.17f, Metal).Ghost();
                        break;
                    default:
                        b.Box(x - 0.015f, top - 0.3f, 0.15f, x + 0.015f, top - 0.18f, 0.17f, Hazard).Ghost();
                        b.Box(x - 0.006f, top - 0.42f, 0.155f, x + 0.006f, top - 0.3f, 0.165f, Metal).Ghost();
                        break;
                }
            }
            b.Cyl(-0.45f, 0.15f, 0.08f, 0.1f, 0.06f, Dark, 8).Ghost();
            b.Box(0.3f, 0.05f, 0.1f, 0.65f, 0.12f, 0.17f, Wood).Ghost();
        };

        r["gas-cylinders"] = b =>
        {
            b.Box(-0.47f, 0, -0.18f, 0.47f, 0.05f, 0.22f, Dark);
            b.Box(-0.47f, 0.05f, 0.18f, 0.47f, 1.2f, 0.22f, Dark);
            b.Box(-0.47f, 0.95f, -0.18f, 0.47f, 0.99f, -0.16f, Metal).Loose();
            DecoSlot[] colours = { Alert, Plant, Trim };
            for (int i = 0; i < 3; i++)
            {
                float x = -0.3f + i * 0.3f;
                b.Cyl(x, 0.05f, 0.02f, 0.12f, 1.12f, colours[i], 10);
                b.Ball(x, 1.17f, 0.02f, 0.12f, 0.08f, 0.12f, colours[i], 8);
                b.Cyl(x, 1.22f, 0.02f, 0.04f, 0.1f, Metal, 6);
                b.Tube(x, 1.29f, 0.02f, x, 1.29f, -0.06f, 0.015f, Metal).Ghost();
            }
            b.Box(0.35f, 0.6f, -0.11f, 0.45f, 0.7f, -0.1f, Hazard).Ghost();
        };

        r["cargo-crate"] = b =>
        {
            b.Box(-0.48f, 0, -0.48f, 0.48f, 0.09f, 0.48f, Wood);
            foreach (float z in new[] { -0.35f, 0.0f, 0.35f }) b.Box(-0.48f, 0.0f, z - 0.03f, 0.48f, 0.06f, z + 0.03f, Dark).Ghost();
            b.Box(-0.46f, 0.09f, -0.46f, 0.46f, 0.9f, 0.46f, Body);
            foreach (float x in new[] { -0.46f, 0.42f })
                foreach (float z in new[] { -0.46f, 0.42f })
                    b.Box(x, 0.09f, z, x + 0.04f, 0.92f, z + 0.04f, Dark).Ghost();
            b.Box(-0.47f, 0.48f, -0.47f, 0.47f, 0.52f, 0.47f, Dark).Ghost();
            b.Box(-0.47f, 0.9f, -0.47f, 0.47f, 0.95f, 0.47f, Body).Loose();
            b.Box(-0.3f, 0.6f, -0.465f, 0.1f, 0.8f, -0.46f, Paper).Ghost();
            b.Box(0.15f, 0.6f, -0.465f, 0.35f, 0.75f, -0.46f, Trim).Ghost();
        };

        r["junction-box"] = b =>
        {
            b.Box(-0.2f, 0.1f, 0.05f, 0.2f, 0.8f, 0.25f, Metal);
            b.Box(-0.19f, 0.12f, 0.02f, 0.19f, 0.78f, 0.05f, Metal).Loose().Door(-0.19f, 0.035f, 100);
            b.Wedge(-0.08f, 0.55f, 0.015f, 0.08f, 0.02f, 0.68f, 0.68f, Hazard).Ghost();
            b.Box(0.1f, 0.3f, 0.01f, 0.14f, 0.34f, 0.02f, Signal).Lit().Ghost();
            b.Box(0.1f, 0.22f, 0.01f, 0.14f, 0.26f, 0.02f, Alert).Ghost();
            b.Tube(-0.1f, 0.8f, 0.15f, -0.1f, 0.94f, 0.15f, 0.03f, Dark).Ghost();
            b.Tube(-0.1f, 0.94f, 0.12f, -0.1f, 0.94f, 0.25f, 0.03f, Dark).Ghost();
            b.Tube(0.08f, 0.8f, 0.15f, 0.08f, 0.9f, 0.15f, 0.025f, Dark).Ghost();
            b.Tube(0.08f, 0.9f, 0.125f, 0.08f, 0.9f, 0.25f, 0.025f, Dark).Ghost();
            b.Tube(0, 0.04f, 0.15f, 0, 0.1f, 0.15f, 0.03f, Dark).Ghost();
            b.Tube(0, 0.04f, 0.12f, 0, 0.04f, 0.25f, 0.03f, Dark).Ghost();
            b.Spark(0, 0.45f, 0.0f);
            b.Light(0.12f, 0.3f, -0.2f, Signal);
        };

        // A flat panel light fixed to the ceiling: a frame on the ceiling with a glowing diffuser under it.
        r["ceiling-light"] = b =>
        {
            b.Box(-0.46f, 0.43f, -0.46f, 0.46f, 0.5f, 0.46f, Metal);
            b.Box(-0.44f, 0.05f, -0.44f, 0.44f, 0.43f, 0.44f, Body);
            b.Box(-0.38f, 0.0f, -0.38f, 0.38f, 0.06f, 0.38f, Lamp).Lit();
            b.Box(-0.4f, 0.04f, -0.01f, 0.4f, 0.07f, 0.01f, Metal).Ghost();
            b.Box(-0.01f, 0.04f, -0.4f, 0.01f, 0.07f, 0.4f, Metal).Ghost();
            b.Light(0, -0.15f, 0, Lamp);
        };

        // A long tube light on two brackets under the ceiling.
        r["strip-light"] = b =>
        {
            b.Box(-0.92f, 0.38f, -0.2f, 0.92f, 0.5f, 0.2f, Metal);
            b.Box(-0.9f, 0.08f, -0.17f, 0.9f, 0.38f, 0.17f, Body);
            b.Box(-0.86f, 0.0f, -0.13f, 0.86f, 0.09f, 0.13f, Lamp).Lit();
            b.Box(-0.94f, 0.04f, -0.2f, -0.9f, 0.44f, 0.2f, Dark).Ghost();
            b.Box(0.9f, 0.04f, -0.2f, 0.94f, 0.44f, 0.2f, Dark).Ghost();
            b.Light(-0.5f, -0.15f, 0, Lamp);
            b.Light(0.5f, -0.15f, 0, Lamp);
        };
    }

    static void Lab(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["lab-bench"] = b =>
        {
            b.Box(-0.98f, 0, -0.4f, 0.98f, 0.08f, 0.43f, Dark);
            b.Box(-0.98f, 0.08f, -0.42f, 0.98f, 0.86f, 0.45f, Body);
            for (int i = 0; i < 4; i++)
            {
                float x = -0.95f + i * 0.475f;
                b.Box(x + 0.02f, 0.12f, -0.43f, x + 0.455f, 0.82f, -0.42f, Body).Loose();
                b.Box(x + 0.2f, 0.7f, -0.44f, x + 0.27f, 0.72f, -0.43f, Chrome).Ghost();
            }
            b.Box(-0.99f, 0.86f, -0.45f, 0.99f, 0.9f, 0.47f, Dark).As(DecoFinish.Stone);
            b.Box(0.55f, 0.88f, -0.2f, 0.9f, 0.905f, 0.25f, Metal).Ghost();
            b.Tube(0.72f, 0.9f, 0.35f, 0.72f, 0.99f, 0.3f, 0.01f, Chrome).Ghost();
            for (int i = 0; i < 4; i++)
            {
                float x = -0.8f + i * 0.18f, z = b.Rng.Range(-0.2f, 0.25f);
                b.Cyl(x, 0.9f, z, 0.04f, 0.09f, Glass, 8).Pane().Ghost();
                b.Cyl(x, 0.9f, z, 0.034f, 0.045f, Liquid, 8).Ghost();
            }
            b.Cyl(-0.1f, 0.9f, 0.1f, 0.07f, 0.09f, Glass, 8, 0.3f).Pane().Ghost();
            Papers(b, 0.2f, 0.9f, -0.1f, 2, 0.05f);
        };

        r["fume-hood"] = b =>
        {
            b.Box(-0.74f, 0, -0.45f, 0.74f, 0.85f, 0.48f, Body);
            b.Box(-0.72f, 0, -0.43f, 0.72f, 0.08f, 0.46f, Dark);
            b.Box(-0.74f, 0.85f, -0.45f, -0.66f, 2.0f, 0.48f, Body);
            b.Box(0.66f, 0.85f, -0.45f, 0.74f, 2.0f, 0.48f, Body);
            b.Box(-0.66f, 0.85f, 0.4f, 0.66f, 2.0f, 0.48f, Body);
            b.Box(-0.74f, 2.0f, -0.45f, 0.74f, 2.3f, 0.48f, Body);
            b.Box(-0.2f, 2.3f, 0.0f, 0.2f, 2.5f, 0.4f, Metal);
            b.Box(-0.66f, 0.85f, -0.45f, 0.66f, 0.88f, 0.4f, Dark);
            b.Box(-0.66f, 1.22f, -0.43f, 0.66f, 1.25f, -0.4f, Trim);
            b.Box(-0.66f, 1.25f, -0.43f, 0.66f, 1.96f, -0.41f, Glass).Pane();
            b.Box(-0.6f, 1.96f, -0.3f, 0.6f, 1.99f, 0.35f, Lamp).Lit();
            b.Box(-0.74f, 2.05f, -0.46f, 0.74f, 2.25f, -0.45f, Trim).Ghost();
            for (int i = 0; i < 3; i++)
            {
                float x = -0.35f + i * 0.3f;
                b.Cyl(x, 0.88f, 0.1f, 0.05f, 0.18f, Glass, 8, 0.4f).Pane().Ghost();
                b.Cyl(x, 0.88f, 0.1f, 0.045f, 0.06f, Liquid, 8).Ghost();
            }
            b.Box(0.67f, 1.0f, -0.46f, 0.73f, 1.2f, -0.45f, Dark).Ghost();
            b.Box(0.68f, 1.12f, -0.465f, 0.72f, 1.16f, -0.46f, Signal).Lit().Ghost();
            b.Light(0, 1.5f, -0.1f, Lamp);
        };

        r["specimen-tank"] = b =>
        {
            b.Cyl(0, 0, 0, 0.46f, 0.3f, Metal, 12);
            b.Cyl(0, 0.3f, 0, 0.47f, 0.04f, Dark, 12).Ghost();
            b.Cyl(0, 0.34f, 0, 0.4f, 0.03f, Liquid, 12).Lit();
            b.Cyl(0, 0.34f, 0, 0.41f, 1.32f, Glass, 12).Pane();
            // Inside: a dark, curled shape nobody named. Coral-like; not a body.
            b.Tube(0, 0.37f, 0, 0.05f, 0.9f, 0.04f, 0.06f, Dark, 6);
            b.Tube(0.05f, 0.9f, 0.04f, -0.12f, 1.25f, 0.0f, 0.05f, Dark, 6);
            b.Tube(0.05f, 0.9f, 0.04f, 0.18f, 1.15f, -0.08f, 0.04f, Dark, 6);
            b.Ball(-0.12f, 1.28f, 0, 0.1f, 0.08f, 0.09f, Dark, 6);
            b.Ball(0.19f, 1.17f, -0.09f, 0.07f, Dark, 6);
            for (int i = 0; i < 5; i++) b.Ball(b.Rng.Range(-0.25f, 0.25f), b.Rng.Range(0.5f, 1.5f), b.Rng.Range(-0.25f, 0.25f), 0.025f, Liquid, 5).Lit().Ghost();
            b.Cyl(0, 1.66f, 0, 0.46f, 0.2f, Metal, 12);
            b.Cyl(0, 1.86f, 0, 0.3f, 0.06f, Dark, 8).Ghost();
            b.Cable(new Vector3(0.315f, 1.8f, 0.315f), new Vector3(0.315f, 0.25f, 0.315f), 0.03f, Dark, 0.02f, breakable: false);
            b.Cable(new Vector3(-0.315f, 1.8f, 0.315f), new Vector3(-0.315f, 0.25f, 0.315f), 0.03f, Dark, 0.02f);
            b.Box(-0.12f, 0.12f, -0.47f, 0.12f, 0.22f, -0.46f, Trim).Ghost();
            b.Light(0, 1.0f, -0.6f, Liquid);
        };

        r["centrifuge"] = b =>
        {
            b.Box(-0.4f, 0.04f, -0.4f, 0.4f, 0.75f, 0.4f, Body);
            b.Box(-0.38f, 0, -0.38f, 0.38f, 0.04f, 0.38f, Dark);
            b.Cyl(0, 0.75f, 0.05f, 0.33f, 0.12f, Body, 12, 0.85f).Loose();
            b.Cyl(0, 0.75f, 0.05f, 0.335f, 0.02f, Trim, 12).Ghost();
            b.Wedge(-0.38f, 0.5f, -0.48f, 0.38f, -0.4f, 0.62f, 0.75f, Dark);
            b.Box(-0.25f, 0.64f, -0.47f, 0.05f, 0.7f, -0.44f, Screen).Screen().Ghost();
            Buttons(b, 0.12f, 0.6f, 0.32f, 0.68f, -0.45f, 3, 1, Signal, Alert);
            b.Box(-0.41f, 0.3f, -0.41f, 0.41f, 0.34f, 0.41f, Trim).Ghost();
            b.Light(0, 0.7f, -0.6f, Signal);
        };

        r["microscope"] = b =>
        {
            b.Box(-0.12f, 0, -0.1f, 0.12f, 0.04f, 0.15f, Body);
            b.Beam(0, 0.04f, 0.11f, 0, 0.34f, 0.06f, 0.06f, Body);
            b.Box(-0.08f, 0.13f, -0.08f, 0.08f, 0.15f, 0.06f, Dark);
            b.Cyl(0, 0.19f, -0.02f, 0.04f, 0.05f, Metal, 6);
            b.Tube(0, 0.24f, -0.02f, 0, 0.36f, 0.05f, 0.026f, Body, 6);
            b.Tube(0, 0.36f, 0.05f, 0, 0.44f, 0.13f, 0.018f, Dark, 6);
            b.Tube(-0.06f, 0.2f, 0.09f, 0.06f, 0.2f, 0.09f, 0.025f, Metal, 6).Ghost();
            b.Box(-0.03f, 0.15f, -0.06f, 0.03f, 0.152f, -0.02f, Glass).Ghost();
        };

        r["lab-stool"] = b =>
        {
            b.Cyl(0, 0, 0, 0.21f, 0.04f, Dark, 8);
            b.Cyl(0, 0.04f, 0, 0.025f, 0.6f, Chrome, 6);
            Ring(b, new Vector3(0, 0.28f, 0), 0.16f, 8, 0.015f, Chrome);
            for (int i = 0; i < 4; i++) { float a = i * MathF.Tau / 4; b.Beam(0, 0.28f, 0, MathF.Cos(a) * 0.15f, 0.28f, MathF.Sin(a) * 0.15f, 0.012f, Chrome).Ghost(); }
            b.Cyl(0, 0.64f, 0, 0.18f, 0.06f, Trim, 10).Soft();
        };

        r["sample-fridge"] = b =>
        {
            b.Box(-0.47f, 0.05f, -0.41f, 0.47f, 1.95f, 0.45f, Body);
            b.Box(-0.44f, 0, -0.38f, 0.44f, 0.05f, 0.42f, Dark);
            b.Box(-0.42f, 0.2f, -0.415f, 0.36f, 1.82f, -0.41f, Dark);
            for (int s = 0; s < 5; s++)
            {
                float y = 0.3f + s * 0.3f;
                b.Box(-0.4f, y, -0.4f, 0.34f, y + 0.015f, 0.3f, Metal).Ghost();
                for (int i = 0; i < 4; i++)
                {
                    if (b.Rng.Chance(0.3f)) continue;
                    b.Box(-0.37f + i * 0.18f, y + 0.015f, -0.38f, -0.23f + i * 0.18f, y + 0.12f, -0.25f, new DecoSlot[] { Trim, Alert, Plant, Plastic }[(i + s) % 4]).Ghost();
                }
            }
            b.Box(-0.4f, 1.78f, -0.4f, 0.34f, 1.8f, 0.3f, Lamp).Lit().Ghost();
            b.Box(-0.44f, 0.15f, -0.44f, 0.38f, 1.85f, -0.415f, Glass).Pane().Door(-0.44f, -0.43f, 75);
            b.Box(0.39f, 0.6f, -0.45f, 0.43f, 1.4f, -0.41f, Chrome);
            b.Box(-0.3f, 1.87f, -0.42f, 0.0f, 1.92f, -0.41f, Screen).Screen();
            b.Box(0.15f, 1.86f, -0.42f, 0.3f, 1.93f, -0.41f, Hazard).Ghost();
            b.Light(0, 1.0f, -0.6f, Lamp);
        };

        r["chem-cart"] = b =>
        {
            foreach (float x in new[] { -0.44f, 0.44f })
                foreach (float z in new[] { -0.18f, 0.18f })
                {
                    b.Tube(x, 0.08f, z, x, 0.92f, z, 0.015f, Chrome);
                    b.Ball(x, 0.04f, z, 0.04f, Rubber, 6).Ghost();
                }
            b.Box(-0.46f, 0.3f, -0.2f, 0.46f, 0.33f, 0.2f, Body);
            b.Box(-0.46f, 0.78f, -0.2f, 0.46f, 0.81f, 0.2f, Body);
            b.Tube(0.44f, 0.92f, -0.18f, 0.44f, 0.92f, 0.18f, 0.016f, Chrome);
            for (int i = 0; i < 5; i++)
            {
                float x = -0.35f + i * 0.16f;
                b.Cyl(x, 0.81f, 0, 0.045f, b.Rng.Range(0.1f, 0.17f), Glass, 6, i % 2 == 0 ? 1f : 0.3f).Pane().Ghost();
                b.Cyl(x, 0.81f, 0, 0.04f, 0.05f, i % 2 == 0 ? Liquid : Alert, 6).Ghost();
            }
            for (int i = 0; i < 3; i++) b.Box(-0.35f + i * 0.25f, 0.33f, -0.12f, -0.18f + i * 0.25f, 0.5f, 0.12f, new DecoSlot[] { Plastic, Trim, Hazard }[i]).Ghost();
        };
    }

    static void Residential(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["sofa"] = b =>
        {
            b.Box(-0.98f, 0.06f, -0.42f, 0.98f, 0.2f, 0.45f, Wood);
            foreach (float x in new[] { -0.9f, 0.9f }) foreach (float z in new[] { -0.36f, 0.38f }) b.Box(x - 0.03f, 0, z - 0.03f, x + 0.03f, 0.06f, z + 0.03f, Wood).Ghost();
            for (int i = 0; i < 3; i++)
            {
                float x0 = -0.78f + i * 0.52f;
                float top = i == 1 ? 0.39f : 0.43f; // the middle cushion still sags where someone sat
                b.Box(x0, 0.2f, -0.4f, x0 + 0.5f, top, 0.22f, Fabric).Soft();
                b.Box(x0 + 0.02f, 0.43f, 0.2f, x0 + 0.48f, 0.82f, 0.36f, Fabric).Soft();
            }
            b.Box(-0.98f, 0.2f, 0.32f, 0.98f, 0.86f, 0.45f, Fabric).Soft();
            b.Box(-0.98f, 0.2f, -0.42f, -0.8f, 0.62f, 0.45f, Fabric).Soft();
            b.Box(0.8f, 0.2f, -0.42f, 0.98f, 0.62f, 0.45f, Fabric).Soft();
            using (b.Turn(new Vector3(-0.6f, 0.5f, 0.1f), -20, 25))
                b.Box(-0.75f, 0.42f, 0.0f, -0.45f, 0.68f, 0.12f, Trim).Soft().Loose();
        };

        r["armchair"] = b =>
        {
            b.Box(-0.45f, 0.06f, -0.4f, 0.45f, 0.2f, 0.45f, Wood);
            foreach (float x in new[] { -0.4f, 0.4f }) foreach (float z in new[] { -0.36f, 0.4f }) b.Box(x - 0.03f, 0, z - 0.03f, x + 0.03f, 0.06f, z + 0.03f, Wood).Ghost();
            b.Box(-0.3f, 0.2f, -0.38f, 0.3f, 0.43f, 0.25f, Fabric).Soft().As(DecoFinish.Leather);
            b.Box(-0.3f, 0.43f, 0.22f, 0.3f, 0.95f, 0.38f, Fabric).Soft().As(DecoFinish.Leather);
            b.Box(-0.45f, 0.2f, 0.32f, 0.45f, 0.98f, 0.45f, Fabric).Soft().As(DecoFinish.Leather);
            b.Box(-0.45f, 0.2f, -0.4f, -0.3f, 0.66f, 0.45f, Fabric).Soft().As(DecoFinish.Leather);
            b.Box(0.3f, 0.2f, -0.4f, 0.45f, 0.66f, 0.45f, Fabric).Soft().As(DecoFinish.Leather);
            b.Box(-0.44f, 0.66f, -0.38f, -0.31f, 0.69f, 0.0f, Wood).Loose().Ghost();
        };

        r["home-bed"] = b =>
        {
            b.Box(-0.74f, 0.1f, -0.98f, 0.74f, 0.32f, 0.92f, Wood);
            foreach (float x in new[] { -0.7f, 0.7f }) b.Box(x - 0.04f, 0, -0.95f, x + 0.04f, 0.1f, -0.87f, Wood).Ghost();
            b.Box(-0.7f, 0.32f, -0.94f, 0.7f, 0.52f, 0.9f, Linen).Soft();
            b.Box(-0.72f, 0.5f, -0.96f, 0.72f, 0.6f, 0.05f, Fabric).Soft();
            b.Box(-0.72f, 0.52f, 0.05f, 0.72f, 0.62f, 0.3f, Linen).Soft();
            using (b.Turn(new Vector3(0.35f, 0.52f, 0.05f), 0, 0, 0))
                b.Box(0.0f, 0.6f, 0.0f, 0.7f, 0.63f, 0.35f, Fabric).Soft().Ghost();
            foreach (float x in new[] { -0.35f, 0.35f }) b.Box(x - 0.3f, 0.52f, 0.55f, x + 0.3f, 0.66f, 0.86f, Linen).Soft();
            b.Box(-0.75f, 0.1f, 0.92f, 0.75f, 0.98f, 1.0f, Wood);
            b.Box(-0.68f, 0.45f, 0.915f, 0.68f, 0.9f, 0.925f, Trim).Ghost();
        };

        r["floor-lamp"] = b =>
        {
            b.Cyl(0, 0, 0, 0.17f, 0.04f, Dark, 8);
            b.Tube(0, 0.04f, 0, 0, 1.2f, 0, 0.015f, Metal);
            b.Cyl(0, 1.12f, 0, 0.22f, 0.3f, Lamp, 8, 0.6f).Lit().Loose().Soft();
            b.Ball(0, 1.2f, 0, 0.05f, Lamp, 6).Lit().Ghost();
            b.Hang(new Vector3(0.15f, 0.02f, 0), new Vector3(0, 0, 1), 0.008f, Dark, 0.06f);
            b.Light(0, 1.18f, 0, Lamp);
        };

        r["media-screen"] = b =>
        {
            b.Box(-0.72f, 0, -0.22f, 0.72f, 0.42f, 0.22f, Wood);
            b.Box(-0.7f, 0.04f, -0.225f, -0.01f, 0.38f, -0.22f, Wood).Loose();
            b.Box(0.01f, 0.04f, -0.225f, 0.7f, 0.38f, -0.22f, Wood);
            b.Box(-0.1f, 0.42f, -0.05f, 0.1f, 0.48f, 0.1f, Dark);
            b.Box(-0.03f, 0.48f, 0.0f, 0.03f, 0.56f, 0.06f, Dark);
            ScreenPanel(b, -0.62f, 0.56f, 0.62f, 1.3f, -0.06f, 0.14f, Dark);
            foreach (float x in new[] { -0.66f, 0.56f }) b.Box(x, 0.42f, -0.08f, x + 0.1f, 0.7f, 0.08f, Dark).Ghost();
            b.Box(0.3f, 0.42f, -0.18f, 0.42f, 0.44f, -0.12f, Dark).Ghost();
        };

        r["coffee-table"] = b =>
        {
            b.Box(-0.48f, 0.36f, -0.45f, 0.48f, 0.4f, 0.45f, Wood);
            b.Box(-0.38f, 0.395f, -0.35f, 0.38f, 0.402f, 0.35f, Glass).Pane().Ghost();
            b.Legs(-0.46f, -0.43f, 0.46f, 0.43f, 0.36f, 0.05f, Wood);
            b.Box(-0.42f, 0.1f, -0.38f, 0.42f, 0.12f, 0.38f, Wood).Loose();
            b.Box(-0.3f, 0.12f, -0.2f, 0.0f, 0.15f, 0.15f, Paper).Ghost();
            b.Cyl(0.25f, 0.4f, 0.1f, 0.04f, 0.09f, Plastic, 8).Ghost();
            Papers(b, -0.15f, 0.4f, -0.05f, 2, 0.06f);
        };

        r["bunk-bed"] = b =>
        {
            foreach (float x in new[] { -0.45f, 0.45f })
                foreach (float z in new[] { -0.95f, 0.95f })
                    b.Box(x - 0.03f, 0, z - 0.03f, x + 0.03f, 1.95f, z + 0.03f, Metal);
            foreach (float y in new[] { 0.3f, 1.25f })
            {
                b.Box(-0.45f, y, -0.95f, 0.45f, y + 0.06f, 0.95f, Metal);
                b.Box(-0.42f, y + 0.06f, -0.92f, 0.42f, y + 0.2f, 0.92f, Linen).Soft();
                b.Box(-0.3f, y + 0.2f, 0.6f, 0.3f, y + 0.28f, 0.88f, Linen).Soft();
                b.Box(-0.43f, y + 0.18f, -0.9f, 0.43f, y + 0.25f, 0.1f, Fabric).Soft();
            }
            b.Box(0.42f, 1.5f, -0.6f, 0.47f, 1.6f, 0.6f, Metal).Loose();
            foreach (float x in new[] { -0.45f, -0.2f }) b.Tube(x, 0, -0.98f, x, 1.6f, -0.98f, 0.014f, Metal);
            for (int i = 1; i < 6; i++) b.Tube(-0.45f, i * 0.28f, -0.98f, -0.2f, i * 0.28f, -0.98f, 0.012f, Metal).Ghost();
        };

        r["wardrobe"] = b =>
        {
            b.Box(-0.48f, 0.06f, -0.2f, 0.48f, 1.92f, 0.24f, Wood);
            b.Box(-0.5f, 1.92f, -0.23f, 0.5f, 1.98f, 0.25f, Trim);
            foreach (float x in new[] { -0.44f, 0.44f }) b.Box(x - 0.03f, 0, -0.18f, x + 0.03f, 0.06f, 0.22f, Wood).Ghost();
            b.Box(-0.46f, 0.1f, -0.225f, -0.01f, 1.88f, -0.2f, Wood).Loose().Door(-0.46f, -0.21f, 95);
            b.Box(0.01f, 0.1f, -0.225f, 0.46f, 1.88f, -0.2f, Wood).Door(0.46f, -0.21f, -95);
            b.Box(-0.06f, 0.9f, -0.235f, -0.03f, 1.1f, -0.225f, Metal).Ghost();
            b.Box(0.03f, 0.9f, -0.235f, 0.06f, 1.1f, -0.225f, Metal).Ghost();
            b.Box(-0.4f, 1.6f, -0.226f, -0.06f, 1.8f, -0.224f, Trim).Ghost();
            b.Box(0.06f, 1.6f, -0.226f, 0.4f, 1.8f, -0.224f, Trim).Ghost();
        };

        r["nightstand"] = b =>
        {
            b.Box(-0.22f, 0.05f, -0.2f, 0.22f, 0.45f, 0.2f, Wood);
            foreach (float x in new[] { -0.19f, 0.19f }) b.Box(x - 0.02f, 0, -0.17f, x + 0.02f, 0.05f, 0.17f, Wood).Ghost();
            b.Box(-0.2f, 0.3f, -0.21f, 0.2f, 0.42f, -0.2f, Wood).Loose();
            b.Box(-0.03f, 0.35f, -0.225f, 0.03f, 0.37f, -0.21f, Metal).Ghost();
            b.Box(0.05f, 0.45f, -0.05f, 0.17f, 0.5f, 0.05f, Dark).Ghost();
            b.Box(0.07f, 0.465f, -0.052f, 0.15f, 0.49f, -0.05f, Signal).Lit().Ghost();
            b.Box(-0.15f, 0.45f, -0.1f, 0.0f, 0.47f, 0.1f, Paper).Ghost();
        };

        r["floor-rug"] = b =>
        {
            b.Box(-0.95f, 0, -0.7f, 0.95f, 0.012f, 0.7f, Fabric).Soft();
            b.Box(-0.95f, 0, -0.7f, 0.95f, 0.014f, -0.6f, Trim).Soft().Ghost();
            b.Box(-0.95f, 0, 0.6f, 0.95f, 0.014f, 0.7f, Trim).Soft().Loose().Ghost();
            b.Box(-0.95f, 0, -0.6f, -0.85f, 0.014f, 0.6f, Trim).Soft().Ghost();
            b.Box(0.85f, 0, -0.6f, 0.95f, 0.014f, 0.6f, Trim).Soft().Ghost();
            for (int i = 0; i < 3; i++)
                using (b.Turn(new Vector3(-0.4f + i * 0.4f, 0, 0), 0, 45))
                    b.Box(-0.52f + i * 0.4f, 0, -0.12f, -0.28f + i * 0.4f, 0.016f, 0.12f, Linen).Soft().Ghost();
        };
    }
}
