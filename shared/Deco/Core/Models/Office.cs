using System;
using System.Collections.Generic;
using System.Numerics;
using static GalHack.Deco.DecoSlot;

namespace GalHack.Deco;

public static partial class DecoModels
{
    static void Office(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["office-desk"] = b =>
        {
            b.Box(-0.74f, 0.71f, -0.42f, 0.74f, 0.76f, 0.46f, Wood);
            b.Box(0.28f, 0, -0.38f, 0.72f, 0.71f, 0.44f, Body);
            for (int i = 0; i < 3; i++)
            {
                float y = 0.06f + i * 0.215f;
                b.Box(0.3f, y, -0.41f, 0.7f, y + 0.19f, -0.38f, Body).Loose();
                b.Box(0.44f, y + 0.12f, -0.43f, 0.56f, y + 0.14f, -0.41f, Chrome).Ghost();
            }
            b.Box(-0.72f, 0, -0.38f, -0.66f, 0.71f, 0.44f, Body);
            b.Box(-0.66f, 0.22f, 0.4f, 0.28f, 0.68f, 0.44f, Body);
            Papers(b, -0.3f, 0.76f, -0.1f, 3, 0.08f);
            b.Cyl(-0.55f, 0.76f, 0.15f, 0.045f, 0.1f, Plastic, 6).Ghost();
        };

        r["office-chair"] = b =>
        {
            StarBase(b, 0.22f, 0.42f);
            b.Box(-0.22f, 0.42f, -0.21f, 0.22f, 0.5f, 0.2f, Fabric).Soft();
            b.Box(-0.03f, 0.44f, 0.15f, 0.03f, 0.62f, 0.2f, Metal);
            b.Box(-0.2f, 0.56f, 0.17f, 0.2f, 0.97f, 0.235f, Fabric).Soft().Loose();
            b.Box(-0.24f, 0.6f, -0.12f, -0.2f, 0.64f, 0.12f, Dark).Loose();
            b.Box(-0.24f, 0.5f, 0.02f, -0.2f, 0.6f, 0.06f, Dark);
            b.Box(0.2f, 0.6f, -0.12f, 0.24f, 0.64f, 0.12f, Dark).Loose();
            b.Box(0.2f, 0.5f, 0.02f, 0.24f, 0.6f, 0.06f, Dark);
        };

        r["desk-monitor"] = b =>
        {
            b.Box(-0.14f, 0, -0.1f, 0.14f, 0.03f, 0.12f, Dark);
            b.Box(-0.19f, 0.03f, -0.14f, 0.19f, 0.34f, 0.06f, Plastic);
            b.Box(-0.14f, 0.07f, 0.06f, 0.14f, 0.3f, 0.2f, Plastic).Loose();
            ScreenPanel(b, -0.16f, 0.07f, 0.16f, 0.31f, -0.15f, 0.012f, Dark);
            b.Box(0.1f, 0.035f, -0.155f, 0.14f, 0.05f, -0.14f, Signal).Lit().Ghost();
        };

        r["filing-cabinet"] = b =>
        {
            b.Box(-0.22f, 0, -0.3f, 0.22f, 1.3f, 0.3f, Body);
            b.Box(-0.23f, 1.3f, -0.31f, 0.23f, 1.33f, 0.31f, Dark);
            for (int i = 0; i < 4; i++)
            {
                float y = 0.04f + i * 0.315f;
                bool open = i == 2;
                float z0 = open ? -0.47f : -0.32f;
                if (open)
                {
                    b.Box(-0.19f, y + 0.04f, -0.46f, 0.19f, y + 0.24f, -0.3f, Dark).Ghost();
                    for (int k = 0; k < 5; k++) b.Box(-0.17f + k * 0.07f, y + 0.08f, -0.44f, -0.15f + k * 0.07f, y + 0.26f, -0.32f, Paper).Soft().Ghost();
                }
                b.Box(-0.2f, y, z0, 0.2f, y + 0.29f, z0 + 0.02f, Body).Loose();
                b.Box(-0.08f, y + 0.2f, z0 - 0.02f, 0.08f, y + 0.23f, z0, Chrome).Ghost();
                b.Box(-0.06f, y + 0.12f, z0 - 0.005f, 0.06f, y + 0.17f, z0, Paper).Ghost();
            }
        };

        r["water-cooler"] = b =>
        {
            b.Box(-0.16f, 0, -0.16f, 0.16f, 0.95f, 0.16f, Plastic);
            b.Box(-0.13f, 0.5f, -0.175f, 0.13f, 0.75f, -0.16f, Dark);
            b.Box(-0.09f, 0.66f, -0.2f, -0.04f, 0.71f, -0.17f, Trim).Loose();
            b.Box(0.04f, 0.66f, -0.2f, 0.09f, 0.71f, -0.17f, Alert).Loose();
            b.Box(-0.12f, 0.5f, -0.22f, 0.12f, 0.53f, -0.16f, Dark).Ghost();
            b.Cyl(0, 0.95f, 0, 0.1f, 0.04f, Dark, 8);
            b.Cyl(0, 0.99f, 0, 0.14f, 0.36f, Glass, 10).Pane();
            b.Cyl(0, 1.35f, 0, 0.14f, 0.05f, Glass, 10, 0.5f).Pane().Ghost();
            b.Cyl(0, 1.4f, 0, 0.05f, 0.03f, Trim, 6).Ghost();
        };

        r["bookshelf"] = b =>
        {
            b.Box(-0.48f, 0, -0.2f, -0.45f, 1.9f, 0.22f, Wood);
            b.Box(0.45f, 0, -0.2f, 0.48f, 1.9f, 0.22f, Wood);
            b.Box(-0.48f, 1.9f, -0.21f, 0.48f, 1.94f, 0.23f, Wood);
            b.Box(-0.45f, 0.05f, 0.2f, 0.45f, 1.9f, 0.22f, Dark);
            for (int i = 0; i < 5; i++)
            {
                float y = 0.04f + i * 0.38f;
                b.Box(-0.45f, y, -0.2f, 0.45f, y + 0.03f, 0.2f, Wood).Loose();
                if (i < 4) Books(b, -0.44f, 0.44f, y + 0.03f, -0.18f, 0.2f, 0.3f);
            }
        };

        r["office-partition"] = b =>
        {
            b.Box(-0.72f, 0.04f, -0.04f, 0.72f, 1.42f, 0.04f, Fabric).Soft();
            b.Box(-0.74f, 0, -0.05f, -0.7f, 1.45f, 0.05f, Metal);
            b.Box(0.7f, 0, -0.05f, 0.74f, 1.45f, 0.05f, Metal);
            b.Box(-0.74f, 1.42f, -0.05f, 0.74f, 1.46f, 0.05f, Metal).Loose();
            b.Box(-0.74f, 0, -0.22f, -0.68f, 0.04f, 0.22f, Dark);
            b.Box(0.68f, 0, -0.22f, 0.74f, 0.04f, 0.22f, Dark);
            for (int i = 0; i < 4; i++)
            {
                float x = -0.5f + i * 0.3f + b.Rng.Range(-0.05f, 0.05f), y = b.Rng.Range(0.75f, 1.15f);
                b.Box(x - 0.1f, y - 0.14f, -0.05f, x + 0.1f, y + 0.14f, -0.042f, Paper).Soft().Ghost();
            }
        };

        r["potted-plant"] = b =>
        {
            b.Cyl(0, 0, 0, 0.17f, 0.34f, Trim, 8, 1.2f);
            b.Cyl(0, 0.3f, 0, 0.19f, 0.03f, Soil, 8).Ghost();
            b.Tube(0, 0.3f, 0, 0.03f, 0.85f, 0.02f, 0.025f, Wood);
            Foliage(b, 0.02f, 1.05f, 0.01f, 0.2f, 5);
            Foliage(b, -0.04f, 0.8f, -0.02f, 0.16f, 3);
        };

        r["copier"] = b =>
        {
            b.Box(-0.42f, 0, -0.34f, 0.42f, 0.1f, 0.36f, Dark);
            b.Box(-0.42f, 0.1f, -0.34f, 0.42f, 0.86f, 0.36f, Plastic);
            b.Box(-0.38f, 0.86f, -0.3f, 0.38f, 0.88f, 0.32f, Glass).Pane();
            b.Box(-0.4f, 0.88f, -0.32f, 0.4f, 0.92f, 0.34f, Plastic).Loose();
            b.Wedge(-0.4f, 0.74f, -0.42f, 0.4f, -0.34f, 0.78f, 0.86f, Dark);
            b.Box(-0.3f, 0.8f, -0.425f, -0.1f, 0.84f, -0.4f, Screen).Screen();
            b.Box(0.1f, 0.8f, -0.425f, 0.14f, 0.83f, -0.4f, Signal).Lit().Ghost();
            b.Box(0.2f, 0.8f, -0.425f, 0.24f, 0.83f, -0.4f, Alert).Ghost();
            for (int i = 0; i < 3; i++) b.Box(-0.38f, 0.16f + i * 0.2f, -0.36f, 0.38f, 0.32f + i * 0.2f, -0.34f, Plastic).Loose();
            b.Box(0.42f, 0.5f, -0.2f, 0.48f, 0.53f, 0.15f, Plastic).Loose();
            b.Light(-0.2f, 0.82f, -0.5f, Screen);
        };

        r["waste-bin"] = b =>
        {
            b.Cyl(0, 0, 0, 0.15f, 0.38f, Dark, 8, 1.15f);
            b.Cyl(0, 0.36f, 0, 0.175f, 0.025f, Metal, 8).Loose();
            for (int i = 0; i < 3; i++)
                b.Ball(b.Rng.Range(-0.07f, 0.07f), 0.36f + i * 0.03f, b.Rng.Range(-0.07f, 0.07f), 0.05f, Paper, 5).Soft().Ghost();
        };
    }
}
