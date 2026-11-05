using System;
using System.Collections.Generic;
using System.Numerics;
using static GalHack.Deco.DecoSlot;

namespace GalHack.Deco;

public static partial class DecoModels
{
    static void Resort(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["bar-counter"] = b =>
        {
            b.Box(-0.96f, 0, -0.3f, 0.96f, 0.08f, 0.42f, Dark);
            for (int i = 0; i < 3; i++)
            {
                float x0 = -0.98f + i * 0.653f;
                float bow = i == 1 ? -0.38f : -0.33f;
                b.Box(x0, 0.08f, bow, x0 + 0.653f, 1.0f, 0.3f, Wood);
            }
            b.Box(-0.98f, 0.45f, -0.385f, 0.98f, 0.52f, -0.33f, Trim).Ghost();
            b.Box(-0.95f, 0.1f, -0.39f, 0.95f, 0.13f, -0.37f, Lamp).Lit().Ghost();
            b.Box(-0.99f, 1.0f, -0.45f, 0.99f, 1.06f, 0.45f, Body);
            b.Tube(-0.95f, 0.2f, -0.44f, 0.95f, 0.2f, -0.44f, 0.022f, Chrome);
            b.Box(0.5f, 1.06f, 0.15f, 0.6f, 1.35f, 0.25f, Chrome);
            for (int i = 0; i < 3; i++) b.Box(0.47f + i * 0.04f, 1.25f, 0.08f, 0.49f + i * 0.04f, 1.32f, 0.15f, Dark).Ghost();
            for (int i = 0; i < 4; i++)
            {
                float x = -0.8f + i * 0.35f;
                b.Cyl(x, 1.06f, -0.2f + (i % 2) * 0.1f, 0.035f, 0.1f, Glass, 6).Pane().Ghost();
                if (i % 2 == 0) b.Cyl(x, 1.06f, -0.2f, 0.03f, 0.05f, Signal, 6).Lit().Ghost();
            }
            Cocktail(b, -0.45f, 1.16f, -0.1f);
            b.Light(0, 0.3f, -0.8f, Lamp);
        };

        r["bar-stool"] = b =>
        {
            b.Cyl(0, 0, 0, 0.2f, 0.03f, Chrome, 10);
            b.Cyl(0, 0.03f, 0, 0.025f, 0.7f, Chrome, 6);
            Ring(b, new Vector3(0, 0.3f, 0), 0.15f, 8, 0.015f, Chrome);
            b.Cyl(0, 0.72f, 0, 0.19f, 0.09f, Fabric, 10).Soft();
            b.Cyl(0, 0.72f, 0, 0.195f, 0.02f, Chrome, 10).Ghost();
        };

        r["pool-lounger"] = b =>
        {
            foreach (float x in new[] { -0.38f, 0.38f })
            {
                b.Box(x - 0.03f, 0.25f, -0.95f, x + 0.03f, 0.3f, 0.45f, Body);
                b.Box(x - 0.025f, 0, -0.85f, x + 0.025f, 0.25f, -0.8f, Body);
                b.Box(x - 0.025f, 0, 0.3f, x + 0.025f, 0.25f, 0.35f, Body);
                b.Tube(x, 0.08f, 0.45f, x, 0.08f, 0.45f, 0.08f, Rubber).Ghost();
            }
            for (int i = 0; i < 8; i++) b.Box(-0.37f, 0.28f, -0.92f + i * 0.17f, 0.37f, 0.31f, -0.8f + i * 0.17f, Wood);
            b.Box(-0.34f, 0.31f, -0.9f, 0.34f, 0.36f, 0.42f, Fabric).Soft();
            using (b.Turn(new Vector3(0, 0.3f, 0.42f), -50))
            {
                b.Box(-0.38f, 0.28f, 0.42f, 0.38f, 0.32f, 1.05f, Body);
                b.Box(-0.34f, 0.32f, 0.44f, 0.34f, 0.37f, 1.0f, Fabric).Soft();
            }
            b.Box(-0.3f, 0.36f, -0.5f, 0.3f, 0.4f, -0.2f, Linen).Soft().Ghost();
        };

        r["parasol"] = b =>
        {
            b.Cyl(0, 0, 0, 0.26f, 0.1f, Body, 10);
            b.Tube(0, 0.1f, 0, 0, 2.32f, 0, 0.026f, Wood);
            b.Cyl(0, 1.92f, 0, 0.97f, 0.42f, Canvas, 8, 0.04f).Soft();
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) * MathF.Tau / 8;
                b.Beam(new Vector3(0, 1.75f, 0), new Vector3(MathF.Cos(a) * 0.85f, 1.92f, MathF.Sin(a) * 0.85f), 0.015f, 0.015f, Wood).Ghost();
                float a2 = i * MathF.Tau / 8;
                b.Box(MathF.Cos(a2) * 0.9f - 0.08f, 1.84f, MathF.Sin(a2) * 0.9f - 0.08f, MathF.Cos(a2) * 0.9f + 0.08f, 1.92f, MathF.Sin(a2) * 0.9f + 0.08f, Trim).Ghost().Soft();
            }
            b.Ball(0, 2.38f, 0, 0.05f, Trim, 6).Ghost();
        };

        r["cocktail-table"] = b =>
        {
            b.Cyl(0, 0, 0, 0.28f, 0.035f, Chrome, 10);
            b.Cyl(0, 0.035f, 0, 0.035f, 0.98f, Chrome, 6);
            b.Cyl(0, 1.0f, 0, 0.34f, 0.04f, Body, 12);
            b.Cyl(0, 1.0f, 0, 0.345f, 0.015f, Trim, 12).Ghost();
            b.Cyl(0.12f, 1.04f, 0.05f, 0.035f, 0.11f, Glass, 6).Pane().Ghost();
            b.Cyl(-0.1f, 1.04f, -0.08f, 0.01f, 0.08f, Glass, 5).Ghost();
            b.Cyl(-0.1f, 1.12f, -0.08f, 0.0f, 0.06f, Glass, 6, 99f).Ghost();
            b.Cyl(-0.1f, 1.135f, -0.08f, 0.0f, 0.03f, Signal, 6, 99f).Lit().Ghost();
            b.Tube(-0.1f, 1.14f, -0.08f, -0.05f, 1.22f, -0.1f, 0.004f, Wood).Ghost();
            b.Cyl(-0.05f, 1.2f, -0.1f, 0.05f, 0.04f, Fabric, 6, 0.05f).Ghost();
        };

        r["back-bar"] = b =>
        {
            b.Box(-0.74f, 0, -0.2f, 0.74f, 0.9f, 0.24f, Wood);
            b.Box(-0.74f, 0.9f, -0.2f, -0.68f, 1.95f, 0.24f, Wood);
            b.Box(0.68f, 0.9f, -0.2f, 0.74f, 1.95f, 0.24f, Wood);
            b.Box(-0.74f, 1.95f, -0.22f, 0.74f, 2.0f, 0.24f, Trim);
            b.Box(-0.68f, 0.9f, 0.19f, 0.68f, 1.95f, 0.24f, Chrome);
            b.Box(-0.66f, 1.9f, -0.15f, 0.66f, 1.92f, 0.15f, Lamp).Lit().Ghost();
            foreach (float y in new[] { 1.2f, 1.55f })
            {
                b.Box(-0.68f, y, -0.15f, 0.68f, y + 0.02f, 0.19f, Glass).Pane();
                for (int i = 0; i < 8; i++)
                {
                    if (b.Rng.Chance(0.2f)) continue;
                    float x = -0.6f + i * 0.17f;
                    DecoSlot s = new DecoSlot[] { Glass, Trim, Fabric, Plant, Hazard, Wood }[(i * 3 + (int)(y * 10)) % 6];
                    float h = b.Rng.Range(0.18f, 0.28f);
                    b.Cyl(x, y + 0.02f, 0, 0.035f, h, s, 6).Ghost();
                    b.Cyl(x, y + 0.02f + h, 0, 0.012f, 0.06f, s, 5).Ghost();
                }
            }
            b.Box(-0.7f, 0.3f, -0.205f, 0.7f, 0.36f, -0.2f, Trim).Ghost();
            b.Light(0, 1.6f, -0.4f, Lamp);
        };

        r["neon-sign"] = b =>
        {
            b.Box(-0.72f, 0.05f, 0.18f, 0.72f, 0.95f, 0.24f, Dark);
            foreach (float x in new[] { -0.6f, 0.6f }) b.Box(x - 0.02f, 0.4f, 0.1f, x + 0.02f, 0.6f, 0.18f, Metal).Ghost();
            float z = 0.13f, w = 0.028f;
            // A cocktail glass and a wave, in neon tubes.
            Vector3 P(float x, float y) => new(x, y, z);
            b.Beam(P(-0.55f, 0.82f), P(-0.3f, 0.82f), w, w, Lamp).Lit();
            b.Beam(P(-0.55f, 0.82f), P(-0.425f, 0.55f), w, w, Lamp).Lit();
            b.Beam(P(-0.3f, 0.82f), P(-0.425f, 0.55f), w, w, Lamp).Lit();
            b.Beam(P(-0.425f, 0.55f), P(-0.425f, 0.25f), w, w, Lamp).Lit();
            b.Beam(P(-0.52f, 0.22f), P(-0.33f, 0.22f), w, w, Lamp).Lit();
            b.Ball(-0.36f, 0.86f, z, 0.04f, Screen, 6).Lit();
            float[] wave = { 0.45f, 0.62f, 0.45f, 0.28f, 0.45f, 0.62f };
            for (int i = 0; i + 1 < wave.Length; i++) b.Beam(P(-0.15f + i * 0.15f, wave[i]), P(-0.15f + (i + 1) * 0.15f, wave[i + 1]), w, w, Screen).Lit();
            b.Light(0, 0.5f, -0.4f, Lamp);
        };
    }

    /// <summary>A cocktail glass with a little umbrella (a frozen moment on a bar).</summary>
    static void Cocktail(DecoBuilder b, float x, float y, float z)
    {
        b.Cyl(x, y - 0.1f, z, 0.03f, 0.005f, Glass, 6).Ghost();
        b.Cyl(x, y - 0.1f, z, 0.006f, 0.06f, Glass, 5).Ghost();
        b.Cyl(x, y - 0.04f, z, 0.0f, 0.06f, Glass, 6, 99f).Ghost();
        b.Tube(x, y - 0.02f, z, x + 0.04f, y + 0.06f, z - 0.02f, 0.003f, Wood).Ghost();
        b.Cyl(x + 0.04f, y + 0.04f, z - 0.02f, 0.045f, 0.03f, Fabric, 6, 0.05f).Ghost();
    }

    static void ThemePark(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["ticket-booth"] = b =>
        {
            b.Box(-0.72f, 0, -0.72f, 0.72f, 1.0f, 0.72f, Trim);
            for (int i = 0; i < 4; i++) b.Box(-0.6f + i * 0.4f, 0.05f, -0.725f, -0.5f + i * 0.4f, 0.95f, -0.72f, Body).Ghost();
            b.Box(-0.74f, 0.98f, -0.74f, 0.74f, 1.04f, 0.74f, Body);
            b.Box(-0.72f, 1.04f, 0.6f, 0.72f, 2.0f, 0.72f, Trim);
            b.Box(-0.72f, 1.04f, -0.72f, -0.6f, 2.0f, 0.72f, Trim);
            b.Box(0.6f, 1.04f, -0.72f, 0.72f, 2.0f, 0.72f, Trim);
            b.Box(-0.6f, 1.06f, -0.71f, 0.6f, 1.86f, -0.69f, Glass).Pane();
            b.Box(-0.15f, 1.04f, -0.74f, 0.15f, 1.12f, -0.69f, Dark).Ghost();
            b.Box(-0.74f, 1.86f, -0.74f, 0.74f, 2.02f, -0.6f, Body);
            for (int i = 0; i < 7; i++) b.Ball(-0.6f + i * 0.2f, 1.94f, -0.715f, 0.03f, Lamp, 5).Lit().Ghost();
            b.Taper(-0.75f, 2.0f, -0.75f, 0.75f, 2.45f, 0.75f, 0.08f, 0.08f, Hazard);
            b.Ball(0, 2.44f, 0, 0.04f, Trim, 6).Ghost();
            b.Box(-0.5f, 1.3f, -0.6f, 0.5f, 1.33f, 0.3f, Body).Ghost();
            b.Light(0, 1.5f, -1.0f, Lamp);
        };

        r["coaster-car"] = b =>
        {
            b.Box(-0.4f, 0.05f, -0.8f, 0.4f, 0.25f, 0.85f, Dark);
            foreach (float x in new[] { -0.45f, 0.45f })
                foreach (float z in new[] { -0.6f, 0.65f }) Wheel(b, x, 0.11f, z, 0.11f, 0.08f, Metal, Dark);
            b.Box(-0.65f, 0.25f, -0.6f, 0.65f, 0.7f, 0.95f, Trim);
            b.Wedge(-0.65f, 0.25f, -0.98f, 0.65f, -0.6f, 0.5f, 0.85f, Trim);
            b.Box(-0.66f, 0.55f, -0.62f, 0.66f, 0.6f, 0.96f, Hazard).Ghost();
            foreach (float z in new[] { -0.15f, 0.55f })
            {
                b.Box(-0.58f, 0.45f, z - 0.25f, 0.58f, 0.55f, z + 0.15f, Fabric).Soft();
                b.Box(-0.6f, 0.55f, z + 0.12f, 0.6f, 1.25f, z + 0.25f, Fabric).Soft();
                b.Tube(-0.55f, 0.9f, z - 0.25f, 0.55f, 0.9f, z - 0.25f, 0.025f, Hazard).Loose();
                foreach (float x in new[] { -0.55f, 0.55f }) b.Tube(x, 0.7f, z - 0.05f, x, 0.9f, z - 0.25f, 0.02f, Hazard).Ghost();
            }
            foreach (float x in new[] { -0.4f, 0.4f }) b.Ball(x, 0.55f, -0.93f, 0.06f, Lamp, 6).Lit().Ghost();
            b.Box(-0.2f, 0.32f, -0.99f, 0.2f, 0.45f, -0.96f, Body).Ghost();
        };

        r["teacup-ride"] = b =>
        {
            b.Cyl(0, 0, 0, 0.72f, 0.08f, Body, 12);
            b.Cyl(0, 0.08f, 0, 0.58f, 0.68f, Fabric, 12, 1.18f);
            b.Cyl(0, 0.74f, 0, 0.7f, 0.06f, Hazard, 12);
            b.Cyl(0, 0.79f, 0, 0.62f, 0.012f, Dark, 12).Ghost();
            b.Cyl(0, 0.4f, 0, 0.05f, 0.45f, Metal, 6);
            b.Tube(0, 0.85f, -0.04f, 0, 0.85f, 0.04f, 0.13f, Metal, 8).Ghost();
            for (int i = 0; i < 6; i++)
            {
                float a = i * MathF.Tau / 6;
                b.Ball(MathF.Cos(a) * 0.6f, 0.45f, MathF.Sin(a) * 0.6f, 0.07f, Body, 6).Ghost();
            }
            Ring(b, new Vector3(0.66f, 0.45f, 0), 0.07f, 6, 0.035f, Hazard, 90, 0);
        };

        r["candy-stall"] = b =>
        {
            b.Box(-0.98f, 0, -0.3f, 0.98f, 1.0f, 0.45f, Body);
            for (int i = 0; i < 6; i++) b.Box(-0.9f + i * 0.33f, 0.05f, -0.305f, -0.75f + i * 0.33f, 0.95f, -0.3f, Trim).Ghost();
            b.Box(-0.99f, 1.0f, -0.42f, 0.99f, 1.05f, 0.47f, Wood);
            foreach (float x in new[] { -0.95f, 0.95f })
            {
                b.Box(x - 0.03f, 1.05f, -0.38f, x + 0.03f, 2.05f, -0.32f, Metal);
                b.Box(x - 0.03f, 1.05f, 0.4f, x + 0.03f, 2.4f, 0.46f, Metal);
            }
            for (int i = 0; i < 8; i++)
            {
                float x0 = -0.99f + i * 0.2475f;
                using (b.Turn(new Vector3(0, 2.4f, 0.45f), -30))
                    b.Box(x0, 2.37f, -0.62f, x0 + 0.2475f, 2.4f, 0.45f, i % 2 == 0 ? Trim : Canvas).Soft();
            }
            b.Box(-0.7f, 2.15f, 0.42f, 0.7f, 2.45f, 0.48f, Hazard);
            for (int i = 0; i < 5; i++) b.Ball(-0.56f + i * 0.28f, 2.3f, 0.41f, 0.035f, Lamp, 5).Lit().Ghost();
            for (int i = 0; i < 4; i++)
            {
                float x = -0.7f + i * 0.45f;
                b.Cyl(x, 1.05f, 0.05f, 0.1f, 0.25f, Glass, 8).Pane().Ghost();
                b.Ball(x, 1.13f, 0.05f, 0.08f, new DecoSlot[] { Trim, Hazard, Fabric, Plant }[i], 6).Ghost().Rots();
            }
            b.Cyl(0.6f, 1.05f, -0.25f, 0.0f, 0.18f, Paper, 6, 99f).Ghost();
            b.Ball(0.6f, 1.28f, -0.25f, 0.09f, Signal, 6).Lit().Ghost();
            b.Light(0, 1.8f, -0.6f, Lamp);
        };

        r["mascot-head"] = b =>
        {
            b.Cyl(0, 0, 0, 0.3f, 0.12f, Dark, 10);
            b.Ball(0, 0.68f, 0, 0.68f, 0.6f, 0.64f, Hazard, 12);
            foreach (float s in new[] { -1f, 1f })
            {
                using (b.Turn(new Vector3(s * 0.38f, 1.12f, 0), 0, 0, -s * 22))
                {
                    b.Cyl(s * 0.38f, 1.08f, 0.05f, 0.16f, 0.36f, Hazard, 6, 0.05f).Loose();
                    b.Cyl(s * 0.38f, 1.1f, -0.03f, 0.1f, 0.26f, Trim, 6, 0.05f).Ghost();
                }
            }
            // Big eyes; one looks somewhere else.
            b.Ball(-0.24f, 0.86f, -0.52f, 0.15f, 0.18f, 0.1f, Plastic, 8);
            b.Ball(0.24f, 0.86f, -0.52f, 0.15f, 0.18f, 0.1f, Plastic, 8);
            b.Ball(-0.22f, 0.84f, -0.6f, 0.07f, 0.08f, 0.04f, Dark, 6).Ghost();
            b.Ball(0.3f, 0.92f, -0.59f, 0.07f, 0.08f, 0.04f, Dark, 6).Ghost();
            b.Ball(0, 0.5f, -0.5f, 0.3f, 0.2f, 0.16f, Body, 8);
            b.Ball(0, 0.62f, -0.64f, 0.07f, 0.05f, 0.04f, Trim, 6).Ghost();
            for (int i = 0; i < 5; i++)
            {
                float a0 = -0.9f + i * 0.36f, a1 = a0 + 0.36f;
                b.Beam(new Vector3(MathF.Sin(a0) * 0.24f, 0.42f - MathF.Cos(a0) * 0.06f, -0.64f + MathF.Cos(a0) * 0.04f),
                       new Vector3(MathF.Sin(a1) * 0.24f, 0.42f - MathF.Cos(a1) * 0.06f, -0.64f + MathF.Cos(a1) * 0.04f), 0.025f, 0.02f, Dark).Ghost();
            }
        };

        r["park-sign"] = b =>
        {
            b.Box(-0.2f, 0, -0.15f, 0.2f, 0.06f, 0.15f, Dark);
            b.Tube(0, 0.06f, 0, 0, 2.35f, 0, 0.045f, Metal);
            (float y, float yaw, DecoSlot s, bool right)[] boards = { (2.0f, 8, Trim, true), (1.62f, -12, Fabric, false), (1.26f, 15, Hazard, true) };
            foreach (var (y, yaw, s, right) in boards)
                using (b.Turn(new Vector3(0, y, 0), 0, yaw))
                {
                    float x0 = right ? -0.25f : -0.36f, x1 = right ? 0.3f : 0.22f;
                    b.Box(x0, y - 0.1f, -0.03f, x1, y + 0.1f, 0.03f, s).Loose();
                    float tip = right ? x1 : x0;
                    b.Wedge(tip - 0.07f, y - 0.1f, -0.03f, tip + 0.07f, 0.03f, y + 0.1f, y + 0.1f, s).Ghost();
                    b.Box(x0 + 0.06f, y - 0.03f, -0.035f, x1 - 0.1f, y + 0.03f, -0.03f, Plastic).Ghost();
                }
            for (int i = 0; i < 4; i++) b.Ball(-0.2f + i * 0.17f, 2.12f, -0.03f, 0.025f, Lamp, 5).Lit().Ghost();
            b.Ball(0, 2.38f, 0, 0.07f, Trim, 6);
            b.Light(0, 2.0f, -0.4f, Lamp);
        };

        r["carousel-horse"] = b =>
        {
            b.Tube(0, 0, 0.05f, 0, 2.45f, 0.05f, 0.03f, Gold, 6);
            b.Ball(0, 1.1f, 0.05f, 0.14f, 0.19f, 0.32f, Body, 8);
            b.Beam(new Vector3(0, 1.2f, -0.18f), new Vector3(0, 1.52f, -0.28f), 0.11f, 0.11f, Body);
            b.Ball(0, 1.56f, -0.33f, 0.08f, 0.09f, 0.14f, Body, 8);
            b.Box(-0.01f, 1.25f, -0.32f, 0.01f, 1.6f, -0.12f, Fabric).Ghost();
            b.Beam(new Vector3(0, 1.0f, -0.15f), new Vector3(0, 0.78f, -0.42f), 0.05f, 0.05f, Body).Loose();
            b.Beam(new Vector3(0, 1.0f, 0.25f), new Vector3(0, 0.74f, 0.42f), 0.05f, 0.05f, Body);
            b.Beam(new Vector3(0, 1.0f, -0.1f), new Vector3(0, 0.82f, -0.3f), 0.05f, 0.05f, Body);
            b.Beam(new Vector3(0, 1.0f, 0.2f), new Vector3(0, 0.78f, 0.3f), 0.05f, 0.05f, Body);
            b.Box(-0.16f, 1.25f, -0.06f, 0.16f, 1.3f, 0.16f, Trim);
            b.Box(-0.17f, 1.08f, 0.0f, -0.15f, 1.25f, 0.1f, Trim).Ghost();
            b.Box(0.15f, 1.08f, 0.0f, 0.17f, 1.25f, 0.1f, Trim).Ghost();
            b.Cyl(0, 0, 0.05f, 0.08f, 0.05f, Gold, 6);
        };

        r["park-bin"] = b =>
        {
            b.Cyl(0, 0, 0, 0.2f, 0.82f, Trim, 10);
            foreach (float y in new[] { 0.15f, 0.42f, 0.69f }) b.Cyl(0, y, 0, 0.205f, 0.08f, Body, 10).Ghost();
            b.Cyl(0, 0.82f, 0, 0.21f, 0.12f, Hazard, 10, 0.6f).Loose();
            b.Box(-0.09f, 0.86f, -0.17f, 0.09f, 0.9f, -0.12f, Dark).Ghost();
        };

        r["queue-rail"] = b =>
        {
            foreach (float x in new[] { -0.88f, 0f, 0.88f })
            {
                b.Cyl(x, 0, 0, 0.1f, 0.03f, Metal, 8);
                b.Tube(x, 0.03f, 0, x, 0.92f, 0, 0.03f, Metal, 6);
                b.Ball(x, 0.94f, 0, 0.04f, Metal, 6).Ghost();
            }
            b.Tube(-0.88f, 0.88f, 0, 0.88f, 0.88f, 0, 0.025f, Trim).Loose();
            b.Tube(-0.88f, 0.5f, 0, 0.88f, 0.5f, 0, 0.022f, Hazard);
        };
    }

    static void Cryo(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["cryo-pod"] = b =>
        {
            b.Box(-0.45f, 0, -0.45f, 0.45f, 0.25f, 0.45f, Dark);
            b.Box(-0.42f, 0.25f, 0.05f, 0.42f, 2.2f, 0.45f, Body);
            b.Box(-0.45f, 0.25f, -0.36f, -0.36f, 2.2f, 0.45f, Body);
            b.Box(0.36f, 0.25f, -0.36f, 0.45f, 2.2f, 0.45f, Body);
            b.Box(-0.36f, 0.25f, -0.36f, 0.36f, 0.4f, 0.05f, Body);
            b.Taper(-0.45f, 2.2f, -0.45f, 0.45f, 2.45f, 0.45f, 0.6f, 0.6f, Body);
            b.Box(-0.36f, 2.1f, -0.4f, 0.36f, 2.2f, 0.05f, Body);
            b.Box(-0.33f, 0.4f, -0.0f, 0.33f, 2.08f, 0.05f, Lamp).Lit();
            b.Box(-0.28f, 0.45f, -0.04f, 0.28f, 1.95f, 0.0f, Dark).Ghost();
            b.Box(-0.36f, 0.4f, -0.38f, 0.36f, 2.1f, -0.35f, Glass).Pane().Door(new Vector3(-0.36f, 0, -0.38f), Vector3.UnitY, 70);
            foreach (float y in new[] { 0.4f, 2.04f }) b.Box(-0.36f, y, -0.39f, 0.36f, y + 0.06f, -0.37f, Frost).Ghost();
            b.Box(-0.36f, 0.4f, -0.39f, -0.3f, 2.1f, -0.37f, Frost).Ghost();
            b.Box(-0.44f, 1.3f, -0.37f, -0.4f, 1.6f, -0.3f, Screen).Screen();
            b.Box(0.4f, 1.4f, -0.37f, 0.44f, 1.45f, -0.3f, Signal).Lit().Ghost();
            b.Box(-0.46f, 0.25f, -0.38f, 0.46f, 0.28f, 0.46f, Trim).Ghost();
            b.Light(0, 1.3f, -0.6f, Lamp);
        };

        r["cryo-bed"] = b =>
        {
            b.Box(-0.45f, 0, -0.95f, 0.45f, 0.45f, 0.95f, Body);
            b.Box(-0.46f, 0.3f, -0.96f, 0.46f, 0.34f, 0.96f, Trim).Ghost();
            b.Box(-0.4f, 0.45f, -0.9f, 0.4f, 0.53f, 0.9f, Dark);
            b.Box(-0.4f, 0.53f, -0.9f, -0.36f, 0.56f, 0.9f, Lamp).Lit();
            b.Box(0.36f, 0.53f, -0.9f, 0.4f, 0.56f, 0.9f, Lamp).Lit();
            b.Box(-0.38f, 0.53f, -0.88f, 0.38f, 0.92f, 0.88f, Glass).Pane().Door(new Vector3(0, 0.92f, 0.88f), Vector3.UnitX, -60);
            b.Box(-0.38f, 0.88f, -0.88f, 0.38f, 0.93f, 0.88f, Frost).Ghost();
            b.Box(-0.2f, 0.3f, -0.96f, 0.2f, 0.42f, -0.95f, Screen).Screen();
            Buttons(b, -0.35f, 0.1f, 0.35f, 0.2f, -0.95f, 4, 1, Signal, Screen);
            b.Light(0, 0.8f, 0, Lamp);
        };

        r["coolant-tank"] = b =>
        {
            foreach (float x in new[] { -0.3f, 0.3f }) b.Box(x - 0.06f, 0, -0.38f, x + 0.06f, 0.2f, 0.38f, Dark);
            b.Cyl(0, 0.15f, 0, 0.42f, 1.55f, Trim, 12);
            b.Ball(0, 1.7f, 0, 0.42f, 0.18f, 0.42f, Trim, 12);
            foreach (float y in new[] { 0.35f, 1.0f, 1.55f }) b.Cyl(0, y, 0, 0.428f, 0.05f, Metal, 12).Ghost();
            Dial(b, 0, 1.25f, -0.41f, 0.08f);
            b.Box(-0.15f, 0.6f, -0.43f, 0.15f, 0.75f, -0.42f, Hazard).Ghost();
            b.Tube(0.1f, 1.8f, 0.1f, 0.1f, 1.95f, 0.1f, 0.05f, Metal);
            b.Cyl(0.1f, 1.93f, 0.1f, 0.07f, 0.05f, Dark, 8).Ghost();
            b.Tube(-0.15f, 1.8f, -0.05f, -0.15f, 1.95f, -0.05f, 0.03f, Metal).Ghost();
            for (int i = 0; i < 5; i++)
            {
                float a = b.Rng.Range(0, MathF.Tau), y = b.Rng.Range(0.3f, 1.5f);
                b.Box(MathF.Cos(a) * 0.4f - 0.06f, y, MathF.Sin(a) * 0.4f - 0.06f, MathF.Cos(a) * 0.4f + 0.06f, y + b.Rng.Range(0.1f, 0.3f), MathF.Sin(a) * 0.4f + 0.06f, Frost).Ghost();
            }
            b.Box(0.3f, 0.9f, -0.39f, 0.34f, 0.94f, -0.35f, Signal).Lit().Ghost();
            b.Light(0.3f, 0.9f, -0.6f, Signal);
        };

        r["cryo-console"] = b =>
        {
            b.Box(-0.3f, 0, -0.05f, 0.3f, 0.85f, 0.22f, Body);
            b.Wedge(-0.48f, 0.85f, -0.24f, 0.48f, 0.24f, 0.9f, 1.15f, Dark);
            ScreenPanel(b, -0.42f, 1.15f, 0.42f, 1.45f, 0.06f, 0.12f, Body);
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 2; j++)
                {
                    float x = -0.36f + i * 0.18f, t = 0.3f + j * 0.35f;
                    float z = -0.24f + t * 0.48f, y = 0.9f + t * 0.25f;
                    using (b.Turn(new Vector3(x, y, z), -27.5f))
                        b.Box(x - 0.05f, y, z - 0.04f, x + 0.05f, y + 0.012f, z + 0.04f, (i + j) % 3 == 0 ? Signal : Screen).Lit().Ghost();
                }
            b.Box(-0.31f, 0.4f, -0.06f, 0.31f, 0.46f, -0.05f, Trim).Ghost();
        };

        r["coolant-manifold"] = b =>
        {
            b.Tube(-0.75f, 0.72f, 0.08f, 0.75f, 0.72f, 0.08f, 0.08f, Trim, 8);
            foreach (float x in new[] { -0.5f, 0f, 0.5f })
            {
                b.Tube(x, 0, 0.08f, x, 0.72f, 0.08f, 0.055f, Metal, 8);
                b.Tube(x, 0, 0.08f, x, 0.04f, 0.08f, 0.1f, Dark, 8).Ghost();
                Ring(b, new Vector3(x, 0.4f, -0.04f), 0.08f, 6, 0.018f, Alert, 90);
                b.Tube(x, 0.4f, 0.04f, x, 0.4f, -0.04f, 0.012f, Metal).Ghost();
                b.Tube(x, 0.48f, 0.08f, x, 0.7f, 0.08f, 0.068f, Frost, 8).Ghost();
            }
            b.Tube(-0.6f, 0.72f, 0.08f, 0.6f, 0.72f, 0.08f, 0.09f, Frost, 8).Ghost();
            Dial(b, 0.25f, 0.9f, 0.05f, 0.06f);
            b.Tube(0.25f, 0.72f, 0.08f, 0.25f, 0.84f, 0.08f, 0.012f, Metal).Ghost();
            foreach (float x in new[] { -0.68f, 0.68f }) b.Box(x - 0.04f, 0.6f, 0.15f, x + 0.04f, 0.85f, 0.25f, Dark);
        };

        r["canister-cart"] = b =>
        {
            b.Box(-0.45f, 0.22f, -0.45f, 0.45f, 0.28f, 0.45f, Metal);
            foreach (float x in new[] { -0.38f, 0.38f }) foreach (float z in new[] { -0.38f, 0.38f }) { b.Tube(x, 0.08f, z, x, 0.22f, z, 0.015f, Metal).Ghost(); b.Ball(x, 0.05f, z, 0.05f, Rubber, 6).Ghost(); }
            b.Tube(-0.4f, 0.28f, 0.42f, -0.4f, 0.95f, 0.42f, 0.018f, Metal);
            b.Tube(0.4f, 0.28f, 0.42f, 0.4f, 0.95f, 0.42f, 0.018f, Metal);
            b.Tube(-0.4f, 0.95f, 0.42f, 0.4f, 0.95f, 0.42f, 0.02f, Metal);
            int n = 0;
            foreach (float x in new[] { -0.2f, 0.2f })
                foreach (float z in new[] { -0.2f, 0.2f })
                {
                    DecoPart can = b.Cyl(x, 0.28f, z, 0.15f, 0.5f, Body, 10);
                    if (n++ == 3) can.Loose();
                    b.Cyl(x, 0.78f, z, 0.07f, 0.06f, Trim, 8).Ghost();
                    b.Cyl(x, 0.7f, z, 0.155f, 0.06f, Frost, 10).Ghost();
                }
        };
    }

    static void Spaceport(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["seat-row"] = b =>
        {
            b.Box(-0.98f, 0.3f, -0.05f, 0.98f, 0.36f, 0.05f, Chrome);
            foreach (float x in new[] { -0.7f, 0.7f })
            {
                b.Box(x - 0.03f, 0, -0.03f, x + 0.03f, 0.3f, 0.03f, Chrome);
                b.Box(x - 0.04f, 0, -0.3f, x + 0.04f, 0.03f, 0.3f, Chrome);
            }
            for (int i = 0; i < 4; i++)
            {
                float x = -0.735f + i * 0.49f;
                b.Box(x - 0.22f, 0.4f, -0.32f, x + 0.22f, 0.46f, 0.2f, Fabric).Soft();
                using (b.Turn(new Vector3(x, 0.46f, 0.2f), 12))
                {
                    DecoPart back = b.Box(x - 0.22f, 0.48f, 0.18f, x + 0.22f, 0.88f, 0.24f, Fabric).Soft();
                    if (i == 2) back.Loose();
                }
                b.Box(x - 0.2f, 0.36f, -0.25f, x + 0.2f, 0.4f, 0.18f, Chrome).Ghost();
                if (i < 3) b.Box(x + 0.22f, 0.4f, -0.2f, x + 0.27f, 0.62f, 0.12f, Dark).Ghost();
            }
            Papers(b, 0.25f, 0.46f, -0.08f, 1, 0.02f);
        };

        r["departure-board"] = b =>
        {
            foreach (float x in new[] { -0.8f, 0.8f }) b.Box(x - 0.05f, 0, -0.05f, x + 0.05f, 1.55f, 0.15f, Dark);
            b.Box(-0.98f, 1.5f, -0.08f, 0.98f, 2.45f, 0.2f, Dark);
            b.Box(-0.95f, 2.28f, -0.1f, 0.95f, 2.42f, -0.08f, Trim);
            for (int i = 0; i < 4; i++) b.Box(-0.85f + i * 0.12f, 2.32f, -0.105f, -0.77f + i * 0.12f, 2.38f, -0.1f, Plastic).Ghost();
            b.Box(-0.92f, 1.55f, -0.1f, 0.92f, 2.25f, -0.085f, Screen).Screen();
            for (int row = 0; row < 7; row++)
            {
                float y = 2.18f - row * 0.095f;
                b.Box(-0.88f, y - 0.022f, -0.106f, -0.68f, y + 0.022f, -0.1f, Dark).Ghost();
                float len = b.Rng.Range(0.3f, 0.75f);
                b.Box(-0.62f, y - 0.022f, -0.106f, -0.62f + len, y + 0.022f, -0.1f, Dark).Ghost();
                b.Box(0.45f, y - 0.022f, -0.106f, 0.88f, y + 0.022f, -0.1f, row % 3 == 1 ? Alert : Dark).Ghost();
            }
            b.Tube(0, 2.45f, 0.05f, 0, 2.45f, 0.05f, 0.0f, Dark).Ghost();
            b.Box(-0.97f, 1.48f, -0.09f, 0.97f, 1.52f, 0.2f, Chrome);
            b.Light(0, 1.9f, -0.6f, Screen);
        };

        r["luggage-cart"] = b =>
        {
            b.Box(-0.4f, 0.12f, -0.65f, 0.4f, 0.18f, 0.6f, Chrome);
            foreach (float x in new[] { -0.36f, 0.36f }) foreach (float z in new[] { -0.55f, 0.5f }) Wheel(b, x, 0.07f, z, 0.07f, 0.05f);
            foreach (float x in new[] { -0.38f, 0.38f }) b.Tube(x, 0.18f, 0.6f, x, 0.95f, 0.66f, 0.018f, Chrome);
            b.Tube(-0.38f, 0.95f, 0.66f, 0.38f, 0.95f, 0.66f, 0.022f, Rubber);
            b.Box(-0.36f, 0.18f, -0.6f, 0.3f, 0.45f, -0.1f, Fabric).Soft();
            b.Box(-0.32f, 0.45f, -0.5f, 0.2f, 0.65f, -0.15f, Alert).Soft().Loose();
            b.Box(-0.2f, 0.18f, 0.0f, 0.35f, 0.55f, 0.45f, Trim).Soft();
            b.Tube(-0.25f, 0.72f, -0.45f, 0.15f, 0.72f, -0.2f, 0.09f, Canvas, 8).Soft().Loose();
            b.Box(-0.05f, 0.65f, -0.35f, 0.05f, 0.7f, -0.3f, Paper).Ghost();
        };

        r["info-kiosk"] = b =>
        {
            b.Box(-0.2f, 0, -0.15f, 0.2f, 1.1f, 0.2f, Body);
            b.Box(-0.205f, 0.85f, -0.155f, 0.205f, 0.92f, 0.205f, Trim).Ghost();
            using (b.Turn(new Vector3(0, 1.1f, 0), -20))
                ScreenPanel(b, -0.19f, 1.1f, 0.19f, 1.42f, -0.06f, 0.1f, Dark);
            b.Box(-0.1f, 0.3f, -0.155f, 0.1f, 0.5f, -0.15f, Trim).Ghost();
            b.Box(-0.02f, 0.34f, -0.16f, 0.02f, 0.46f, -0.155f, Plastic).Ghost();
        };

        r["checkin-desk"] = b =>
        {
            b.Box(-0.98f, 0, -0.32f, 0.98f, 0.95f, -0.2f, Body);
            b.Box(-0.98f, 0.95f, -0.42f, 0.98f, 1.0f, -0.18f, Plastic);
            b.Box(-0.98f, 0.55f, -0.33f, 0.98f, 0.62f, -0.32f, Trim).Ghost();
            b.Box(-0.98f, 0.7f, -0.2f, 0.6f, 0.75f, 0.45f, Body);
            b.Legs(-0.95f, -0.2f, 0.55f, 0.42f, 0.7f, 0.05f, Dark);
            b.Box(0.55f, 0, -0.48f, 0.98f, 0.32f, 0.2f, Dark);
            for (int i = 0; i < 5; i++) b.Tube(0.58f, 0.33f, -0.42f + i * 0.13f, 0.95f, 0.33f, -0.42f + i * 0.13f, 0.02f, Chrome).Ghost();
            b.Box(-0.4f, 0.75f, 0.1f, 0.0f, 0.98f, 0.18f, Dark);
            b.Box(-0.38f, 0.77f, 0.095f, -0.02f, 0.96f, 0.1f, Screen).Screen();
            b.Box(-0.5f, 0.75f, -0.1f, -0.2f, 0.77f, 0.05f, Dark).Ghost();
            b.Light(-0.2f, 0.9f, -0.2f, Screen);
        };

        r["suitcase"] = b =>
        {
            b.Box(-0.2f, 0.06f, -0.12f, 0.2f, 0.68f, 0.12f, Fabric).Soft();
            foreach (float x in new[] { -0.2f, 0.17f }) foreach (float y in new[] { 0.06f, 0.65f }) b.Box(x, y, -0.125f, x + 0.03f, y + 0.03f, 0.125f, Chrome).Ghost();
            foreach (float x in new[] { -0.14f, 0.14f }) b.Ball(x, 0.03f, 0.1f, 0.03f, Rubber, 6).Ghost();
            foreach (float x in new[] { -0.08f, 0.08f }) b.Tube(x, 0.68f, 0.1f, x, 0.92f, 0.1f, 0.01f, Chrome).Loose();
            b.Box(-0.1f, 0.91f, 0.08f, 0.1f, 0.94f, 0.12f, Rubber).Ghost();
            b.Box(0.2f, 0.4f, -0.02f, 0.22f, 0.5f, 0.06f, Paper).Ghost();
        };

        r["stanchion"] = b =>
        {
            foreach (float x in new[] { -0.6f, 0.6f })
            {
                b.Cyl(x, 0, 0, 0.15f, 0.04f, Chrome, 10);
                b.Cyl(x, 0.04f, 0, 0.03f, 0.88f, Chrome, 6);
                b.Ball(x, 0.94f, 0, 0.045f, Chrome, 6).Ghost();
            }
            b.Cable(new Vector3(-0.6f, 0.86f, 0), new Vector3(0.6f, 0.86f, 0), 0.025f, Fabric, 0.06f);
        };
    }
}
