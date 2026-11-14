using System;
using System.Collections.Generic;
using System.Numerics;
using static GalHack.Deco.DecoSlot;

namespace GalHack.Deco;

public static partial class DecoModels
{
    /// <summary>
    /// What a home's kitchen, bathroom, dining table and hall are made of (residential set): worktop units, a sink, a
    /// cooker, a table and its chairs, a bath, a basin, a WC, a hearth, a sideboard and a coat stand. Fronts face -z,
    /// backs against the wall at +z, as every piece.
    /// </summary>
    static void Home(Dictionary<string, Action<DecoBuilder>> r)
    {
        // A base unit: plinth, carcass, two doors, a drawer line and the worktop over it.
        static void Unit(DecoBuilder b, bool worktop = true)
        {
            b.Box(-0.47f, 0, -0.24f, 0.47f, 0.1f, 0.3f, Dark);
            b.Box(-0.49f, 0.1f, -0.28f, 0.49f, 0.86f, 0.3f, Body);
            b.Box(-0.47f, 0.12f, -0.29f, -0.01f, 0.66f, -0.28f, Body).Loose().Door(-0.47f, -0.285f, 100);
            b.Box(0.01f, 0.12f, -0.29f, 0.47f, 0.66f, -0.28f, Body).Door(0.47f, -0.285f, -100);
            b.Box(-0.47f, 0.69f, -0.29f, 0.47f, 0.84f, -0.28f, Body);
            foreach (float x in new[] { -0.08f, 0.08f }) b.Box(x - 0.05f, 0.55f, -0.31f, x + 0.05f, 0.57f, -0.29f, Metal).Ghost();
            b.Box(-0.1f, 0.75f, -0.31f, 0.1f, 0.77f, -0.29f, Metal).Ghost();
            if (worktop) b.Box(-0.5f, 0.86f, -0.32f, 0.5f, 0.91f, 0.32f, Wood);
        }

        r["single-bed"] = b =>
        {
            // A narrow bed, its head to the wall: frame, mattress, blanket turned down, a pillow.
            b.Box(-0.46f, 0.1f, -0.98f, 0.46f, 0.3f, 0.92f, Wood);
            foreach (float x in new[] { -0.42f, 0.42f }) b.Box(x - 0.04f, 0, -0.95f, x + 0.04f, 0.1f, -0.87f, Wood).Ghost();
            b.Box(-0.43f, 0.3f, -0.94f, 0.43f, 0.48f, 0.9f, Linen).Soft();
            b.Box(-0.45f, 0.46f, -0.96f, 0.45f, 0.56f, 0.2f, Fabric).Soft();
            b.Box(-0.3f, 0.48f, 0.5f, 0.3f, 0.62f, 0.86f, Linen).Soft();
            b.Box(-0.47f, 0.1f, 0.92f, 0.47f, 0.9f, 1.0f, Wood);
        };

        r["kitchen-unit"] = b =>
        {
            Unit(b);
            b.Cyl(0.25f, 0.91f, 0.1f, 0.07f, 0.08f, Metal, 8, 0.85f).Loose().Ghost();
            b.Box(-0.38f, 0.91f, 0.0f, -0.12f, 0.93f, 0.26f, Wood).Loose().Ghost();
        };

        r["kitchen-sink"] = b =>
        {
            Unit(b, worktop: false);
            // The worktop round a basin let into it, a tap over the back.
            b.Box(-0.5f, 0.86f, -0.32f, -0.3f, 0.91f, 0.32f, Wood);
            b.Box(0.3f, 0.86f, -0.32f, 0.5f, 0.91f, 0.32f, Wood);
            b.Box(-0.3f, 0.86f, -0.32f, 0.3f, 0.91f, -0.22f, Wood);
            b.Box(-0.3f, 0.86f, 0.2f, 0.3f, 0.91f, 0.32f, Wood);
            b.Box(-0.3f, 0.7f, -0.22f, 0.3f, 0.72f, 0.2f, Chrome);
            foreach (float x in new[] { -0.3f, 0.29f }) b.Box(x, 0.72f, -0.22f, x + 0.01f, 0.86f, 0.2f, Chrome).Ghost();
            b.Tube(0, 0.91f, 0.26f, 0, 1.1f, 0.26f, 0.015f, Chrome);
            b.Tube(0, 1.1f, 0.26f, 0, 1.08f, 0.1f, 0.012f, Chrome).Ghost();
        };

        r["cooker"] = b =>
        {
            b.Box(-0.47f, 0, -0.27f, 0.47f, 0.06f, 0.3f, Dark);
            b.Box(-0.48f, 0.06f, -0.29f, 0.48f, 0.9f, 0.3f, Body).As(DecoFinish.Paint);
            // The oven door, its window and handle; the grill's slot over it.
            b.Box(-0.4f, 0.12f, -0.3f, 0.4f, 0.6f, -0.29f, Body).Loose().Door(new Vector3(0, 0.12f, -0.295f), Vector3.UnitX, 85);
            b.Box(-0.26f, 0.24f, -0.305f, 0.26f, 0.48f, -0.3f, Glass).Pane().Ghost();
            b.Box(-0.3f, 0.54f, -0.32f, 0.3f, 0.56f, -0.3f, Chrome).Ghost();
            b.Box(-0.4f, 0.66f, -0.3f, 0.4f, 0.8f, -0.29f, Dark);
            // The hob and its rings, the control panel at the back with its knobs.
            b.Box(-0.48f, 0.9f, -0.29f, 0.48f, 0.92f, 0.22f, Dark);
            foreach (float x in new[] { -0.22f, 0.22f })
                foreach (float z in new[] { -0.14f, 0.08f })
                    b.Cyl(x, 0.92f, z, 0.09f, 0.015f, Metal, 10).Ghost();
            b.Box(-0.48f, 0.9f, 0.22f, 0.48f, 1.0f, 0.3f, Body);
            for (int i = 0; i < 4; i++) b.Cyl(-0.3f + i * 0.2f, 0.95f, 0.21f, 0.025f, 0.02f, Chrome, 6).Ghost();
            b.Cyl(0.2f, 0.92f, 0.08f, 0.13f, 0.07f, Metal, 10, 0.9f).Loose().Ghost();
        };

        r["dining-table"] = b =>
        {
            b.Box(-0.82f, 0.72f, -0.46f, 0.82f, 0.76f, 0.46f, Wood);
            b.Box(-0.76f, 0.64f, -0.4f, 0.76f, 0.72f, 0.4f, Wood);
            b.Legs(-0.76f, -0.4f, 0.76f, 0.4f, 0.64f, 0.06f, Wood);
            b.Box(-0.2f, 0.76f, -0.12f, 0.2f, 0.762f, 0.12f, Linen).Soft().Loose().Ghost();
        };

        r["dining-chair"] = b =>
        {
            foreach (float x in new[] { -0.19f, 0.19f })
                foreach (float z in new[] { -0.18f, 0.18f })
                    b.Box(x - 0.02f, 0, z - 0.02f, x + 0.02f, 0.44f, z + 0.02f, Wood);
            b.Box(-0.21f, 0.44f, -0.21f, 0.21f, 0.48f, 0.21f, Wood);
            b.Box(-0.19f, 0.48f, -0.18f, 0.19f, 0.51f, 0.16f, Fabric).Soft();
            foreach (float x in new[] { -0.19f, 0.19f }) b.Box(x - 0.02f, 0.48f, 0.17f, x + 0.02f, 0.92f, 0.21f, Wood);
            for (int i = 0; i < 3; i++) b.Box(-0.17f, 0.6f + i * 0.11f, 0.18f, 0.17f, 0.66f + i * 0.11f, 0.2f, Wood).Loose();
        };

        r["bathtub"] = b =>
        {
            // A tub on four feet: its rim, the inside a step lower, the taps at the wall end.
            foreach (float x in new[] { -0.72f, 0.72f })
                foreach (float z in new[] { -0.28f, 0.28f })
                    b.Cyl(x, 0, z, 0.05f, 0.1f, Metal, 6).Ghost();
            b.Box(-0.86f, 0.08f, -0.38f, 0.86f, 0.22f, 0.38f, Linen).As(DecoFinish.Paint);
            b.Box(-0.86f, 0.22f, -0.38f, 0.86f, 0.58f, -0.3f, Linen).As(DecoFinish.Paint);
            b.Box(-0.86f, 0.22f, 0.3f, 0.86f, 0.58f, 0.38f, Linen).As(DecoFinish.Paint);
            b.Box(-0.86f, 0.22f, -0.3f, -0.76f, 0.58f, 0.3f, Linen).As(DecoFinish.Paint);
            b.Box(0.76f, 0.22f, -0.3f, 0.86f, 0.58f, 0.3f, Linen).As(DecoFinish.Paint);
            foreach (float z in new[] { -0.1f, 0.1f }) b.Tube(0.8f, 0.58f, z, 0.8f, 0.72f, z, 0.018f, Chrome).Ghost();
            b.Tube(0.8f, 0.72f, 0, 0.66f, 0.68f, 0, 0.015f, Chrome).Ghost();
        };

        r["washbasin"] = b =>
        {
            // A basin on its pedestal, its taps, and the mirror on the wall over it.
            b.Cyl(0, 0, 0.06f, 0.1f, 0.7f, Linen, 10, 0.8f).As(DecoFinish.Paint);
            b.Box(-0.28f, 0.7f, -0.2f, 0.28f, 0.84f, 0.22f, Linen).As(DecoFinish.Paint);
            b.Box(-0.22f, 0.8f, -0.15f, 0.22f, 0.841f, 0.12f, Chrome).Ghost();
            foreach (float x in new[] { -0.1f, 0.1f }) b.Tube(x, 0.84f, 0.17f, x, 0.93f, 0.17f, 0.012f, Chrome).Ghost();
            b.Box(-0.25f, 1.15f, 0.22f, 0.25f, 1.75f, 0.24f, Wood);
            b.Box(-0.22f, 1.18f, 0.215f, 0.22f, 1.72f, 0.22f, Glass).Pane().Ghost();
            b.Box(-0.24f, 1.05f, 0.14f, 0.24f, 1.07f, 0.24f, Linen).Loose().Ghost();
        };

        r["wc"] = b =>
        {
            b.Box(-0.2f, 0.42f, 0.28f, 0.2f, 0.8f, 0.46f, Linen).As(DecoFinish.Paint);
            b.Box(-0.21f, 0.8f, 0.27f, 0.21f, 0.83f, 0.47f, Linen).As(DecoFinish.Paint).Loose();
            b.Cyl(0, 0, -0.02f, 0.17f, 0.4f, Linen, 10, 1.15f).As(DecoFinish.Paint);
            b.Box(-0.12f, 0, 0.1f, 0.12f, 0.38f, 0.3f, Linen).As(DecoFinish.Paint);
            b.Cyl(0, 0.4f, -0.02f, 0.2f, 0.025f, Wood, 10).Loose();
        };

        r["fireplace"] = b =>
        {
            // The chimney breast's surround with the firebox in it, a mantel shelf over and a hearth stone before it.
            b.Box(-0.72f, 0, -0.25f, 0.72f, 0.05f, 0.25f, Stone);
            b.Box(-0.72f, 0.05f, 0.0f, -0.42f, 1.15f, 0.25f, Body).As(DecoFinish.Stone);
            b.Box(0.42f, 0.05f, 0.0f, 0.72f, 1.15f, 0.25f, Body).As(DecoFinish.Stone);
            b.Box(-0.42f, 0.8f, 0.0f, 0.42f, 1.15f, 0.25f, Body).As(DecoFinish.Stone);
            b.Box(-0.42f, 0.05f, 0.12f, 0.42f, 0.8f, 0.25f, Dark);
            b.Box(-0.3f, 0.05f, 0.0f, 0.3f, 0.12f, 0.12f, Grime).Ghost();
            b.Box(-0.74f, 1.15f, -0.08f, 0.74f, 1.22f, 0.25f, Wood);
            b.Box(-0.5f, 1.22f, 0.1f, -0.38f, 1.4f, 0.2f, Plastic).Loose().Ghost();
            b.Cyl(0.45f, 1.22f, 0.15f, 0.05f, 0.16f, Metal, 6).Loose().Ghost();
        };

        r["sideboard"] = b =>
        {
            b.Legs(-0.68f, -0.17f, 0.68f, 0.17f, 0.12f, 0.05f, Wood);
            b.Box(-0.72f, 0.12f, -0.2f, 0.72f, 0.82f, 0.22f, Wood);
            b.Box(-0.74f, 0.82f, -0.22f, 0.74f, 0.86f, 0.24f, Wood);
            for (int i = 0; i < 3; i++)
            {
                float x0 = -0.68f + i * 0.46f;
                b.Box(x0, 0.16f, -0.21f, x0 + 0.44f, 0.6f, -0.2f, Wood).Loose().Door(x0, -0.205f, 95);
                b.Box(x0 + 0.04f, 0.64f, -0.21f, x0 + 0.4f, 0.78f, -0.2f, Wood);
                b.Box(x0 + 0.18f, 0.7f, -0.225f, x0 + 0.26f, 0.72f, -0.21f, Metal).Ghost();
            }
            b.Cyl(-0.4f, 0.86f, 0.05f, 0.12f, 0.08f, Plastic, 10, 1.3f).Loose().Ghost();
            Papers(b, 0.35f, 0.86f, 0.0f, 2, 0.08f);
        };

        r["pendant-lamp"] = b =>
        {
            // A flex from a ceiling rose to a cloth shade over a bulb.
            b.Cyl(0, 0.96f, 0, 0.06f, 0.04f, Body, 8);
            b.Tube(0, 0.96f, 0, 0, 0.32f, 0, 0.006f, Dark);
            b.Cyl(0, 0.08f, 0, 0.2f, 0.24f, Fabric, 10, 0.55f).Soft().Lit();
            b.Ball(0, 0.12f, 0, 0.05f, Lamp, 6).Lit().Ghost();
            b.Light(0, 0.1f, 0, Lamp);
        };

        r["hanging-lamp"] = b =>
        {
            // An oil lamp on a chain from a hook: its brass font, the glass chimney round the flame, a smoke bell over it.
            b.Tube(0, 0.98f, 0, 0, 0.5f, 0, 0.008f, Metal);
            b.Cyl(0, 0.42f, 0, 0.12f, 0.08f, Bronze, 8, 0.4f);
            b.Cyl(0, 0.16f, 0, 0.06f, 0.26f, Glass, 8, 0.8f).Pane();
            b.Ball(0, 0.24f, 0, 0.025f, Lamp, 6).Lit().Ghost();
            b.Cyl(0, 0.04f, 0, 0.1f, 0.12f, Bronze, 8, 0.6f);
            b.Light(0, 0.24f, 0, Lamp);
        };

        r["coat-stand"] = b =>
        {
            foreach (float a in new[] { 0f, 90f, 180f, 270f })
            {
                Vector2 d = new(MathF.Cos(a * MathF.PI / 180), MathF.Sin(a * MathF.PI / 180));
                b.Tube(0, 0.05f, 0, d.X * 0.2f, 0.025f, d.Y * 0.2f, 0.02f, Wood);
                b.Tube(0, 1.7f, 0, d.X * 0.12f, 1.78f, d.Y * 0.12f, 0.012f, Wood).Ghost();
            }
            b.Tube(0, 0, 0, 0, 1.85f, 0, 0.025f, Wood);
            b.Box(-0.16f, 1.0f, -0.06f, 0.08f, 1.72f, 0.08f, Fabric).Soft().Loose();
            b.Box(0.05f, 1.35f, -0.04f, 0.19f, 1.72f, 0.06f, Trim).Soft().Loose();
        };
    }
}
