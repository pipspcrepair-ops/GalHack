using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static GalHack.Deco.DecoSlot;

namespace GalHack.Deco;

public static partial class DecoModels
{
    /// <summary>A railway wheel on an axle along X, its lowest point on the rail (y = 0). Steel: it never goes flat.</summary>
    static void RailWheel(DecoBuilder b, float x, float z, float r)
    {
        DecoPart w = b.Tube(x - 0.06f, r, z, x + 0.06f, r, z, r, Metal, 10);
        float lift = -DecoEmit.Vertices(w).Min(v => v.Y);
        w.M *= DecoGeo.Move(0, lift, 0);
    }

    /// <summary>A two-axle bogie centred at z, rails at x = ±0.72.</summary>
    static void Bogie(DecoBuilder b, float z, float r = 0.42f)
    {
        foreach (float dz in new[] { -0.85f, 0.85f })
        {
            foreach (float x in new[] { -0.72f, 0.72f }) RailWheel(b, x, z + dz, r);
            b.Tube(-0.8f, r, z + dz, 0.8f, r, z + dz, 0.07f, Dark, 6).Ghost();
        }
        foreach (float s in new[] { -1f, 1f })
        {
            b.Box(s * 0.84f, r - 0.12f, z - 1.25f, s * 0.98f, r + 0.22f, z + 1.25f, Dark);
            b.Box(s * 0.84f, r + 0.05f, z - 0.25f, s * 1.02f, r + 0.3f, z + 0.25f, Metal).Ghost();
        }
        b.Box(-0.84f, r + 0.1f, z - 0.3f, 0.84f, r + 0.35f, z + 0.3f, Dark);
    }

    static void Rail(Dictionary<string, Action<DecoBuilder>> r)
    {
        r["diesel-loco"] = b =>
        {
            Bogie(b, -4.4f); Bogie(b, 4.4f);
            b.Box(-1.4f, 0.75f, -6.4f, 1.4f, 1.15f, 6.4f, Dark);
            b.Box(-1.15f, 0.35f, -2.0f, 1.15f, 0.75f, 2.0f, Dark);
            b.Box(-1.0f, 1.15f, -3.6f, 1.0f, 3.0f, 6.3f, Body);
            foreach (float s in new[] { -1f, 1f })
                for (int i = 0; i < 6; i++) b.Box(s * 1.0f - 0.01f, 1.8f, -2.6f + i * 1.4f, s * 1.0f + 0.01f, 2.7f, -1.8f + i * 1.4f, Dark).Ghost();
            foreach (float z in new[] { -2.0f, 4.5f }) { b.Cyl(0, 3.0f, z, 0.3f, 0.06f, Dark, 10).Ghost(); }
            b.Cyl(0.3f, 3.0f, 1.0f, 0.12f, 0.3f, Dark, 8);
            b.Box(-1.45f, 1.15f, -6.3f, 1.45f, 3.65f, -3.6f, Body);
            b.Box(-1.3f, 2.6f, -6.32f, 1.3f, 3.35f, -6.3f, Glass).Pane();
            foreach (float s in new[] { -1f, 1f }) b.Box(s * 1.45f - 0.01f, 2.6f, -5.6f, s * 1.45f + 0.01f, 3.3f, -4.6f, Glass).Pane();
            b.Box(-1.48f, 3.65f, -6.35f, 1.48f, 3.78f, -3.55f, Dark);
            b.Tube(0.4f, 3.78f, -5.0f, 0.4f, 3.95f, -5.0f, 0.06f, Metal).Ghost();
            b.Box(-1.46f, 1.2f, -4.3f, -1.44f, 3.4f, -3.7f, Body).Loose().Door(-1.45f, -4.3f, -80);
            for (int i = 0; i < 4; i++)
                using (b.Turn(new Vector3(-0.8f + i * 0.55f, 1.6f, -6.31f), 0, 0, 40))
                    b.Box(-0.95f + i * 0.55f, 1.2f, -6.33f, -0.75f + i * 0.55f, 2.0f, -6.31f, Trim).Ghost();
            foreach (float s in new[] { -1f, 1f }) b.Tube(s * 0.9f, 2.35f, -6.3f, s * 0.9f, 2.35f, -6.36f, 0.12f, Lamp).Lit();
            b.Box(-0.35f, 2.2f, -6.33f, 0.35f, 2.45f, -6.3f, Paper).Ghost();
            foreach (float s in new[] { -1f, 1f })
            {
                b.Tube(s * 1.35f, 1.95f, -3.55f, s * 1.35f, 1.95f, 6.3f, 0.025f, Metal).Ghost();
                for (int i = 0; i < 6; i++) b.Tube(s * 1.35f, 1.15f, -3.0f + i * 1.8f, s * 1.35f, 1.95f, -3.0f + i * 1.8f, 0.02f, Metal).Ghost();
            }
            foreach (float z in new[] { -6.48f, 6.4f }) b.Box(-0.2f, 0.75f, z, 0.2f, 1.05f, z + 0.08f, Dark).Ghost();
        };

        r["passenger-coach"] = b =>
        {
            Bogie(b, -5.5f); Bogie(b, 5.5f);
            b.Box(-1.45f, 0.95f, -7.45f, 1.45f, 3.4f, 7.45f, Body);
            b.Taper(-1.45f, 3.4f, -7.45f, 1.45f, 3.85f, 7.45f, 0.72f, 1f, Dark);
            b.Box(-1.46f, 1.95f, -6.4f, 1.46f, 2.75f, 6.4f, Glass).Pane();
            for (int i = 0; i <= 12; i++)
            {
                float z = -6.4f + i * 1.067f;
                foreach (float s in new[] { -1f, 1f }) b.Box(s > 0 ? 1.4f : -1.465f, 1.95f, z - 0.07f, s > 0 ? 1.465f : -1.4f, 2.75f, z + 0.07f, Body);
            }
            foreach (float s in new[] { -1f, 1f }) b.Box(s > 0 ? 1.45f : -1.46f, 1.6f, -7.45f, s > 0 ? 1.46f : -1.45f, 1.75f, 7.45f, Trim).Ghost();
            foreach (float z0 in new[] { -7.25f, 6.55f })
                foreach (float s in new[] { -1f, 1f })
                    b.Box(s > 0 ? 1.45f : -1.475f, 1.0f, z0, s > 0 ? 1.475f : -1.45f, 2.95f, z0 + 0.7f, Body).Loose().Door(s * 1.46f, z0, s * 75);
            for (int i = 0; i < 12; i++) Seats(b, 1.2f, 1.6f, 2.35f, -5.9f + i * 1.0f);
            foreach (float z in new[] { -7.5f, 7.45f }) b.Box(-0.6f, 1.0f, z, 0.6f, 3.2f, z + 0.05f, Dark);
        };

        r["box-wagon"] = b =>
        {
            Bogie(b, -4.2f); Bogie(b, 4.2f);
            b.Box(-1.3f, 0.75f, -5.95f, 1.3f, 1.0f, 5.95f, Dark);
            b.Box(-1.4f, 1.0f, -5.9f, 1.4f, 3.6f, 5.9f, Body);
            foreach (float s in new[] { -1f, 1f })
                for (int i = 0; i < 13; i++) b.Box(s > 0 ? 1.4f : -1.43f, 1.0f, -5.8f + i * 0.95f, s > 0 ? 1.43f : -1.4f, 3.55f, -5.72f + i * 0.95f, Dark).Ghost();
            foreach (float s in new[] { -1f, 1f }) b.Box(s > 0 ? 1.43f : -1.46f, 1.05f, -1.0f, s > 0 ? 1.46f : -1.43f, 3.35f, 1.0f, Body).Loose().Door(s * 1.44f, -1.0f, s * 80);
            b.Taper(-1.42f, 3.6f, -5.92f, 1.42f, 3.9f, 5.92f, 0.85f, 1f, Dark);
            foreach (float s in new[] { -1f, 1f }) b.Box(s > 0 ? 1.46f : -1.47f, 2.6f, 2.0f, s > 0 ? 1.47f : -1.46f, 3.1f, 4.0f, Paper).Ghost();
            Ring(b, new Vector3(0.6f, 3.0f, 5.95f), 0.25f, 8, 0.04f, Metal, 90);
        };

        r["tank-wagon"] = b =>
        {
            Bogie(b, -3.8f); Bogie(b, 3.8f);
            b.Box(-1.2f, 0.75f, -5.45f, 1.2f, 1.05f, 5.45f, Dark);
            foreach (float z in new[] { -2.5f, 2.5f }) b.Box(-1.1f, 1.0f, z - 0.25f, 1.1f, 1.35f, z + 0.25f, Dark);
            b.Barrel(-4.9f, 4.9f, 0, 1.05f, 1.3f, 1.3f, Metal, 14);
            foreach (float z in new[] { -4.9f, 4.9f }) b.Ball(0, 2.35f, z, 1.27f, 1.27f, 0.38f, Metal, 12);
            foreach (float z in new[] { -3.0f, 0f, 3.0f }) b.Barrel(z - 0.06f, z + 0.06f, 0, 1.03f, 1.32f, 1.32f, Dark, 14).Ghost();
            b.Cyl(0, 3.5f, 0, 0.4f, 0.35f, Metal, 10);
            b.Box(-0.8f, 3.6f, -0.8f, 0.8f, 3.65f, 0.8f, Metal).Ghost();
            foreach (float s in new[] { -1f, 1f }) b.Box(s > 0 ? 1.28f : -1.3f, 1.7f, 3.3f, s > 0 ? 1.3f : -1.28f, 2.3f, 3.9f, Hazard).Ghost();
            for (int i = 0; i < 7; i++) b.Tube(1.3f, 1.2f + i * 0.33f, 0.3f, 1.3f, 1.2f + i * 0.33f, -0.3f, 0.015f, Metal).Ghost();
        };

        r["flat-wagon"] = b =>
        {
            Bogie(b, -4.2f); Bogie(b, 4.2f);
            b.Box(-1.45f, 0.85f, -5.95f, 1.45f, 1.15f, 5.95f, Dark);
            foreach (float s in new[] { -1f, 1f }) for (int i = 0; i < 6; i++) b.Box(s * 1.42f - 0.04f, 1.15f, -5.5f + i * 2.2f, s * 1.42f + 0.04f, 1.6f, -5.4f + i * 2.2f, Metal);
            b.Box(-1.2f, 1.15f, -3.0f, 1.2f, 3.4f, 3.0f, Trim);
            foreach (float s in new[] { -1f, 1f }) for (int i = 0; i < 12; i++) b.Box(s > 0 ? 1.2f : -1.22f, 1.2f, -2.9f + i * 0.5f, s > 0 ? 1.22f : -1.2f, 3.35f, -2.82f + i * 0.5f, Dark).Ghost();
            b.Box(-1.18f, 1.2f, -3.02f, 1.18f, 3.35f, -3.0f, Trim).Loose().Door(-1.18f, -3.01f, -100);
            foreach (float x in new[] { -1.2f, 1.2f })
                foreach (float z in new[] { -3.0f, 3.0f })
                    b.Cable(new Vector3(x, 3.38f, z * 0.98f), new Vector3(x * 1.17f, 1.15f, z * 1.2f), 0.02f, Metal, 0.01f);
            b.Box(-1.1f, 1.15f, 3.6f, 0.2f, 1.9f, 5.5f, Wood).Loose();
            b.Box(0.35f, 1.15f, 3.8f, 1.2f, 1.6f, 5.2f, Wood);
            b.Box(-1.0f, 1.15f, -5.6f, 1.0f, 1.75f, -3.8f, Canvas).Soft();
        };

        r["log-wagon"] = b =>
        {
            Bogie(b, -3.0f, 0.38f); Bogie(b, 3.0f, 0.38f);
            b.Box(-1.2f, 0.7f, -4.45f, 1.2f, 0.95f, 4.45f, Dark);
            foreach (float z in new[] { -3.6f, -1.2f, 1.2f, 3.6f })
            {
                b.Box(-1.35f, 0.95f, z - 0.12f, 1.35f, 1.1f, z + 0.12f, Wood);
                foreach (float s in new[] { -1f, 1f }) b.Box(s * 1.3f - 0.06f, 1.1f, z - 0.06f, s * 1.3f + 0.06f, 2.85f, z + 0.06f, Metal);
                b.Cable(new Vector3(-1.24f, 2.7f, z), new Vector3(1.24f, 2.7f, z), 0.02f, Metal, 0.01f);
            }
            float[] layer = { 1.1f, 1.58f, 2.06f };
            int[] counts = { 4, 3, 2 };
            for (int l = 0; l < 3; l++)
                for (int i = 0; i < counts[l]; i++)
                {
                    float x = (i - (counts[l] - 1) / 2f) * 0.56f;
                    DecoPart log = b.Barrel(-4.3f + b.Rng.Range(0, 0.2f), 4.3f - b.Rng.Range(0, 0.2f), x, layer[l], 0.26f, 0.26f, Wood, 8);
                    b.Tube(x, layer[l] + 0.26f, -4.31f, x, layer[l] + 0.26f, -4.28f, 0.2f, Canvas, 8).Ghost();
                    if (l == 2 && i == 1) log.Loose();
                }
        };

        r["maglev-head"] = b =>
        {
            b.Box(-1.55f, 0, -6.9f, 1.55f, 0.55f, 6.95f, Dark);
            b.Box(-1.65f, 0.55f, -3.5f, 1.65f, 3.6f, 6.95f, Body);
            b.Wedge(-1.65f, 0.55f, -6.95f, 1.65f, -3.5f, 1.4f, 3.6f, Body);
            b.Hull(new[]
            {
                new Vector3(-0.9f, 2.28f, -5.6f), new Vector3(0.9f, 2.28f, -5.6f), new Vector3(-0.9f, 2.34f, -5.6f), new Vector3(0.9f, 2.34f, -5.6f),
                new Vector3(-1.3f, 3.3f, -4.0f), new Vector3(1.3f, 3.3f, -4.0f), new Vector3(-1.3f, 3.36f, -4.0f), new Vector3(1.3f, 3.36f, -4.0f),
            }, Glass).Pane();
            b.Taper(-1.65f, 3.6f, -3.5f, 1.65f, 3.95f, 6.95f, 0.8f, 1f, Body);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(s > 0 ? 1.64f : -1.66f, 2.0f, -3.0f, s > 0 ? 1.66f : -1.64f, 2.7f, 6.5f, Glass).Pane();
                b.Box(s > 0 ? 1.645f : -1.665f, 1.15f, -6.0f, s > 0 ? 1.665f : -1.645f, 1.25f, 6.9f, Screen).Lit();
                b.Box(s > 0 ? 1.65f : -1.675f, 0.65f, 4.6f, s > 0 ? 1.675f : -1.65f, 3.0f, 5.5f, Body).Loose().Door(s * 1.66f, 4.6f, s * 80);
            }
            foreach (float s in new[] { -1f, 1f }) b.Box(s * 0.9f - 0.25f, 0.8f, -6.96f, s * 0.9f + 0.25f, 0.95f, -6.9f, Lamp).Lit().Ghost();
            Seats(b, 1.3f, 1.6f, 2.4f, -2.5f, -1.0f, 0.5f, 2.0f, 3.5f, 5.0f);
            b.Light(0, 1.2f, -7.2f, Lamp);
        };

        r["maglev-coach"] = b =>
        {
            b.Box(-1.55f, 0, -6.95f, 1.55f, 0.55f, 6.95f, Dark);
            b.Box(-1.65f, 0.55f, -6.95f, 1.65f, 3.6f, 6.95f, Body);
            b.Taper(-1.65f, 3.6f, -6.95f, 1.65f, 3.95f, 6.95f, 0.8f, 1f, Body);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(s > 0 ? 1.64f : -1.66f, 2.0f, -6.3f, s > 0 ? 1.66f : -1.64f, 2.7f, 6.3f, Glass).Pane();
                b.Box(s > 0 ? 1.645f : -1.665f, 1.15f, -6.95f, s > 0 ? 1.665f : -1.645f, 1.25f, 6.95f, Screen).Lit();
                foreach (float z0 in new[] { -6.0f, 5.1f })
                    b.Box(s > 0 ? 1.65f : -1.675f, 0.65f, z0, s > 0 ? 1.675f : -1.65f, 3.0f, z0 + 0.9f, Body).Loose().Door(s * 1.66f, z0, s * 80);
            }
            for (int i = 0; i < 10; i++) Seats(b, 1.3f, 1.6f, 2.4f, -5.0f + i * 1.05f);
            foreach (float z in new[] { -7.0f, 6.95f }) b.Box(-0.7f, 0.6f, z, 0.7f, 3.3f, z + 0.05f, Dark);
            b.Light(0, 0.3f, 0, Screen);
        };

        r["rail-track"] = b =>
        {
            b.Taper(-1.72f, 0, -2f, 1.72f, 0.12f, 2f, 0.82f, 1f, Stone);
            for (int i = 0; i < 7; i++)
            {
                float z = -1.8f + i * 0.6f;
                DecoPart s = b.Box(-1.3f, 0.12f, z - 0.11f, 1.3f, 0.22f, z + 0.11f, Wood);
                if (i == 3) s.Loose();
            }
            foreach (float x in new[] { -0.72f, 0.72f })
            {
                b.Box(x - 0.035f, 0.22f, -2f, x + 0.035f, 0.32f, 2f, Metal);
                b.Box(x - 0.07f, 0.22f, -2f, x + 0.07f, 0.24f, 2f, Metal).Ghost();
            }
        };

        r["maglev-guideway"] = b =>
        {
            b.Box(-1.2f, 0, -2f, 1.2f, 0.75f, 2f, Body);
            b.Box(-1.7f, 0.75f, -2f, 1.7f, 0.9f, 2f, Body);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(s * 1.62f - 0.06f, 0.4f, -2f, s * 1.62f + 0.06f, 0.75f, 2f, Metal);
                b.Box(s * 0.9f - 0.08f, 0.9f, -2f, s * 0.9f + 0.08f, 0.905f, 2f, Screen).Lit();
            }
            b.Box(-1.21f, 0.2f, -2f, 1.21f, 0.25f, 2f, Trim).Ghost();
            b.Light(0, 1.0f, 0, Screen);
        };

        r["buffer-stop"] = b =>
        {
            b.Taper(-1.72f, 0, -1f, 1.72f, 0.12f, 1f, 0.82f, 1f, Stone);
            foreach (float z in new[] { -0.7f, 0f, 0.7f }) b.Box(-1.3f, 0.12f, z - 0.11f, 1.3f, 0.22f, z + 0.11f, Wood);
            foreach (float x in new[] { -0.72f, 0.72f }) b.Box(x - 0.035f, 0.22f, -1f, x + 0.035f, 0.32f, 1f, Metal);
            foreach (float x in new[] { -0.72f, 0.72f })
            {
                b.Box(x - 0.1f, 0.32f, -0.55f, x + 0.1f, 1.35f, -0.35f, Body);
                b.Beam(new Vector3(x, 1.2f, -0.4f), new Vector3(x, 0.33f, 0.95f), 0.12f, 0.12f, Body);
            }
            b.Box(-1.5f, 1.0f, -0.6f, 1.5f, 1.35f, -0.3f, Body);
            for (int i = 0; i < 6; i++) b.Box(-1.4f + i * 0.5f, 1.02f, -0.605f, -1.15f + i * 0.5f, 1.33f, -0.6f, Hazard).Ghost();
            foreach (float x in new[] { -0.85f, 0.85f })
            {
                b.Tube(x, 1.18f, -0.6f, x, 1.18f, -0.9f, 0.1f, Dark, 8);
                b.Tube(x, 1.18f, -0.9f, x, 1.18f, -0.98f, 0.2f, Dark, 8);
            }
            b.Cyl(0, 1.35f, -0.45f, 0.08f, 0.2f, Signal, 8).Lit();
        };

        r["rail-signal"] = b =>
        {
            b.Box(-0.3f, 0, -0.3f, 0.3f, 0.25f, 0.3f, Stone);
            b.Tube(0, 0.25f, 0, 0, 4.1f, 0, 0.07f, Metal, 6);
            b.Box(-0.22f, 3.25f, -0.18f, 0.22f, 4.3f, 0.12f, Dark);
            DecoSlot[] lamps = { Signal, Hazard, Plant };
            for (int i = 0; i < 3; i++)
            {
                float y = 4.05f - i * 0.3f;
                DecoPart l = b.Tube(0, y, -0.18f, 0, y, -0.21f, 0.09f, lamps[i], 8);
                if (i == 0) l.Lit();
                b.Box(-0.11f, y + 0.08f, -0.32f, 0.11f, y + 0.1f, -0.18f, Dark).Ghost();
            }
            for (int i = 0; i < 10; i++) b.Tube(-0.12f, 0.5f + i * 0.28f, 0.14f, 0.12f, 0.5f + i * 0.28f, 0.14f, 0.012f, Metal).Ghost();
            foreach (float x in new[] { -0.12f, 0.12f }) b.Tube(x, 0.25f, 0.14f, x, 3.2f, 0.14f, 0.015f, Metal).Ghost();
            b.Box(0.1f, 0.25f, 0.1f, 0.3f, 0.65f, 0.28f, Dark);
            b.Cable(new Vector3(0.2f, 0.64f, 0.18f), new Vector3(0.1f, 3.27f, 0.05f), 0.02f, Dark, 0.03f);
            b.Light(0, 4.05f, -0.4f, Signal);
        };

        r["level-crossing"] = b =>
        {
            b.Box(-2.42f, 0, -0.16f, -2.14f, 1.45f, 0.16f, Metal);
            b.Box(-2.48f, 0.95f, -0.12f, -2.2f, 1.45f, 0.12f, Dark);
            b.Box(-2.2f, 1.05f, -0.05f, 2.45f, 1.16f, 0.05f, Body).Loose();
            for (int i = 0; i < 5; i++) b.Box(-1.8f + i * 0.85f, 1.04f, -0.055f, -1.4f + i * 0.85f, 1.17f, 0.055f, Alert).Ghost();
            b.Box(2.3f, 0, -0.06f, 2.42f, 1.04f, 0.06f, Metal);
            b.Tube(-1.85f, 0, 0, -1.85f, 2.8f, 0, 0.06f, Metal, 6);
            foreach (float a in new[] { 40f, -40f })
                using (b.Turn(new Vector3(-1.85f, 2.5f, -0.07f), 0, 0, a))
                    b.Box(-2.45f, 2.42f, -0.09f, -1.25f, 2.58f, -0.07f, Body);
            b.Box(-2.3f, 1.9f, -0.06f, -1.4f, 1.98f, 0.06f, Dark);
            foreach (float x in new[] { -2.2f, -1.5f })
            {
                b.Tube(x, 1.8f, -0.06f, x, 1.8f, -0.12f, 0.12f, Signal, 8).Lit();
                b.Cyl(x, 1.88f, -0.14f, 0.13f, 0.03f, Dark, 8).Ghost();
            }
            b.Light(-1.85f, 1.8f, -0.4f, Signal);
        };

        r["platform-edge"] = b =>
        {
            b.Box(-1.5f, 0, -2f, 1.5f, 0.92f, 2f, Plastic);
            b.Box(-1.5f, 0.92f, -2f, -1.15f, 1.0f, 2f, Stone);
            b.Box(-1.12f, 0.92f, -2f, -1.0f, 0.93f, 2f, Hazard).Ghost();
            for (int i = 0; i < 10; i++) b.Box(-0.95f, 0.92f, -1.9f + i * 0.4f, -0.75f, 0.935f, -1.75f + i * 0.4f, Hazard).Ghost();
            for (int i = 0; i < 3; i++) b.Box(-0.7f, 0.919f, -2f + i * 1.33f, 1.5f, 0.921f, -1.97f + i * 1.33f, Dark).Ghost();
            b.Box(-1.5f, 0.3f, -2f, -1.49f, 0.6f, 2f, Dark).Ghost();
        };

        r["station-canopy"] = b =>
        {
            foreach (float z in new[] { -1.6f, 1.6f })
            {
                b.Box(0.9f, 0, z - 0.1f, 1.1f, 3.6f, z + 0.1f, Metal);
                b.Beam(new Vector3(1.0f, 3.2f, z), new Vector3(-1.3f, 3.6f, z), 0.1f, 0.1f, Metal);
            }
            b.Box(-1.5f, 3.6f, -2f, 1.5f, 3.75f, 2f, Dark);
            b.Box(-1.3f, 3.75f, -1.8f, 0.7f, 3.77f, 1.8f, Glass).Pane();
            b.Box(-1.5f, 3.5f, -2f, -1.4f, 3.6f, 2f, Metal).Ghost();
            foreach (float z in new[] { -1.0f, 1.0f })
            {
                b.Cable(new Vector3(-0.6f, 3.6f, z), new Vector3(-0.6f, 3.1f, z), 0.012f, Metal, 0f);
                b.Cyl(-0.6f, 2.95f, z, 0.2f, 0.16f, Dark, 8, 0.4f);
                b.Ball(-0.6f, 2.95f, z, 0.08f, Lamp, 6).Lit();
                b.Light(-0.6f, 2.8f, z, Lamp);
            }
            foreach (float z in new[] { -0.55f, 0.55f }) b.Cable(new Vector3(0.1f, 3.6f, z), new Vector3(0.1f, 2.9f, z), 0.01f, Metal, 0f);
            b.Box(0.04f, 2.55f, -0.8f, 0.16f, 2.9f, 0.8f, Trim);
            for (int i = 0; i < 5; i++) b.Box(0.02f, 2.66f, -0.6f + i * 0.26f, 0.04f, 2.8f, -0.45f + i * 0.26f, Paper).Ghost();
        };
    }

    // ---- aviation ---------------------------------------------------------------------------------

    /// <summary>A row of cabin windows along both sides of a round fuselage (radius r, centre height cy).</summary>
    static void CabinWindows(DecoBuilder b, float cy, float r, float z0, float z1, float step = 0.5f)
    {
        float y = cy + 0.5f;
        float x = MathF.Sqrt(r * r - 0.25f) - 0.01f;
        for (float z = z0; z <= z1; z += step)
            foreach (float s in new[] { -1f, 1f })
                b.Box(s > 0 ? x : -x - 0.035f, y - 0.14f, z, s > 0 ? x + 0.035f : -x, y + 0.14f, z + 0.24f, Glass).Ghost();
        foreach (float s in new[] { -1f, 1f })
        {
            float xs = MathF.Sqrt(r * r - 0.01f);
            b.Box(s > 0 ? xs - 0.03f : -xs - 0.02f, cy + 0.05f, z0 - 0.2f, s > 0 ? xs + 0.02f : -xs + 0.03f, cy + 0.2f, z1 + 0.4f, Trim).Ghost();
        }
    }

    static void Aviation(Dictionary<string, Action<DecoBuilder>> r)
    {
        const float R = 1.9f;

        r["liner-fuselage"] = b =>
        {
            b.Barrel(-4f, 4f, 0, 0, R, R, Body, 16);
            CabinWindows(b, R, R, -3.6f, 3.4f);
            b.Box(-1.93f, 0.95f, 2.55f, -1.87f, 2.95f, 3.4f, Body).Loose().Door(-1.9f, 3.4f, 75);
            b.Box(-1.935f, 0.93f, 2.53f, -1.925f, 2.97f, 2.55f, Dark).Ghost();
            b.Box(-0.4f, 0.0f, -2.0f, 0.4f, 0.06f, 0.5f, Dark).Ghost();
            b.Box(-0.06f, 3.75f, 0.0f, 0.06f, 4.0f, 0.4f, Dark).Ghost();
        };

        r["liner-cabin-torn"] = b =>
        {
            const float cy = R + 0.04f;
            int n = 16;
            float w = 2 * R * MathF.Sin(MathF.PI / n) + 0.02f;
            for (int i = 0; i < n; i++)
            {
                float a = i * MathF.Tau / n - MathF.PI / 2;
                float x = MathF.Cos(a) * R, y = cy + MathF.Sin(a) * R;
                float deg = a * 180 / MathF.PI;
                // The upper left is torn away: staves end short, at ragged lengths, a few bent out.
                bool torn = i >= 8 && i <= 13;
                float z0 = -4f, z1 = 4f;
                if (torn) { z0 = -4f; z1 = -4f + b.Rng.Range(0.4f, 2.6f); }
                using (b.Turn(new Vector3(x, y, 0), 0, 0, deg + 90))
                {
                    b.Box(x - w / 2, y - 0.04f, z0, x + w / 2, y + 0.04f, z1, Body);
                    if (torn)
                    {
                        float z2 = 4f - b.Rng.Range(0.3f, 2.2f);
                        b.Box(x - w / 2, y - 0.04f, z2, x + w / 2, y + 0.04f, 4f, Body);
                    }
                }
            }
            b.Box(-1.75f, 0.85f, -4f, 1.75f, 0.95f, 4f, Fabric);
            for (int row = 0; row < 8; row++)
            {
                float z = -3.5f + row * 0.9f;
                foreach (float s in new[] { -1f, 1f })
                {
                    float xa = s * 0.35f, xb = s * 1.5f;
                    b.Box(MathF.Min(xa, xb), 0.95f, z, MathF.Max(xa, xb), 1.4f, z + 0.5f, Fabric).Soft();
                    b.Box(MathF.Min(xa, xb), 1.4f, z + 0.45f, MathF.Max(xa, xb), 2.05f, z + 0.58f, Fabric).Soft();
                }
            }
            b.Box(1.05f, 2.7f, -4f, 1.65f, 3.05f, 4f, Body);
            b.Box(1.0f, 2.62f, -4f, 1.05f, 2.7f, 4f, Dark).Ghost();
            CabinWindows(b, cy, R, -3.6f, 3.4f);
            // Wiring hangs from the torn edge down the outside to the ground.
            b.Hang(new Vector3(-1.34f, cy + 1.34f, -3.8f), new Vector3(-1, 0, 0.3f), 0.02f, Dark, 0.4f);
            b.Hang(new Vector3(-1.9f, cy, 3.8f), new Vector3(-1, 0, -0.2f), 0.02f, Alert, 0.3f);
        };

        r["liner-nose"] = b =>
        {
            b.Barrel(0.5f, 3.5f, 0, 0, R, R, Body, 16);
            b.Barrel(-3.4f, 0.5f, 0, 0, 0.55f, 0.55f, Body, 16, R / 0.55f, 0.4f);
            b.Hull(new[]
            {
                new Vector3(-0.5f, 2.42f, -2.6f), new Vector3(0.5f, 2.42f, -2.6f), new Vector3(-0.5f, 2.47f, -2.6f), new Vector3(0.5f, 2.47f, -2.6f),
                new Vector3(-0.78f, 2.78f, -1.9f), new Vector3(0.78f, 2.78f, -1.9f), new Vector3(-0.78f, 2.83f, -1.9f), new Vector3(0.78f, 2.83f, -1.9f),
            }, Glass).Pane();
            CabinWindows(b, R, R, 1.9f, 2.9f);
            b.Box(-1.93f, 0.95f, 0.8f, -1.87f, 2.95f, 1.65f, Body).Loose().Door(-1.9f, 0.8f, -75);
            b.Box(-0.6f, 0.35f, -2.2f, 0.6f, 0.45f, -1.0f, Dark).Ghost();
            b.Ball(0, 1.5f, -3.31f, 0.42f, 0.42f, 0.18f, Dark, 8).Ghost();
        };

        r["liner-tail"] = b =>
        {
            b.Barrel(-3.65f, 3.6f, 0, 0, R, R, Body, 16, 0.24f, 1.2f);
            b.Hull(new[]
            {
                new Vector3(-0.17f, 3.4f, -1.5f), new Vector3(0.17f, 3.4f, -1.5f), new Vector3(-0.08f, 7.8f, 1.8f), new Vector3(0.08f, 7.8f, 1.8f),
                new Vector3(-0.17f, 3.4f, 3.4f), new Vector3(0.17f, 3.4f, 3.4f), new Vector3(-0.08f, 7.8f, 3.9f), new Vector3(0.08f, 7.8f, 3.9f),
            }, Trim);
            b.Tube(-0.19f, 5.8f, 2.4f, 0.19f, 5.8f, 2.4f, 0.55f, Body, 10).Ghost();
            foreach (float s in new[] { -1f, 1f })
                b.Hull(new[]
                {
                    new Vector3(s > 0 ? 0.3f : -4.9f, s > 0 ? 2.9f : 3.3f, s > 0 ? 0.8f : 2.6f), new Vector3(s > 0 ? 4.9f : -0.3f, s > 0 ? 3.3f : 2.9f, s > 0 ? 2.6f : 0.8f),
                    new Vector3(s > 0 ? 0.3f : -4.9f, s > 0 ? 3.08f : 3.38f, s > 0 ? 0.8f : 2.6f), new Vector3(s > 0 ? 4.9f : -0.3f, s > 0 ? 3.38f : 3.08f, s > 0 ? 2.6f : 0.8f),
                    new Vector3(s > 0 ? 0.3f : -4.9f, s > 0 ? 2.9f : 3.3f, s > 0 ? 3.3f : 3.7f), new Vector3(s > 0 ? 4.9f : -0.3f, s > 0 ? 3.3f : 2.9f, s > 0 ? 3.7f : 3.3f),
                    new Vector3(s > 0 ? 0.3f : -4.9f, s > 0 ? 3.08f : 3.38f, s > 0 ? 3.3f : 3.7f), new Vector3(s > 0 ? 4.9f : -0.3f, s > 0 ? 3.38f : 3.08f, s > 0 ? 3.7f : 3.3f),
                }, Body);
            b.Cyl(0, 2.95f, 3.55f, 0.2f, 0.1f, Dark, 8).Ghost();
            CabinWindows(b, R, R, -3.6f, -2.2f);
        };

        r["liner-wing"] = b =>
        {
            using var lowered = b.Shift(0, -0.7f, 0);
            b.Box(-1.9f, 0.7f, -2.0f, 1.9f, 1.15f, 1.5f, Body);
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 P(float x, float y, float z) => new(s * x, y, z);
                Vector3[] inner =
                {
                    P(1.9f, 0.7f, -2.6f), P(9f, 1.05f, -0.2f), P(1.9f, 1.2f, -2.6f), P(9f, 1.38f, -0.2f),
                    P(1.9f, 0.7f, 2.0f), P(9f, 1.05f, 2.55f), P(1.9f, 1.2f, 2.0f), P(9f, 1.38f, 2.55f),
                };
                Vector3[] outer =
                {
                    P(9f, 1.05f, -0.2f), P(15.5f, 1.55f, 1.4f), P(9f, 1.38f, -0.2f), P(15.5f, 1.7f, 1.4f),
                    P(9f, 1.05f, 2.55f), P(15.5f, 1.55f, 3.2f), P(9f, 1.38f, 2.55f), P(15.5f, 1.7f, 3.2f),
                };
                if (s < 0) { inner = Mirror(inner); outer = Mirror(outer); }
                b.Hull(inner, Body);
                DecoPart o = b.Hull(outer, Body);
                if (s > 0) o.Loose();
                b.Box(s > 0 ? 3f : -14.5f, 1.2f, 2.3f, s > 0 ? 14.5f : -3f, 1.32f, 2.6f, Dark).Ghost();
                b.Frustum(1.6f, s > 0 ? 15.3f : -15.6f, s > 0 ? 15.6f : -15.3f, 1.5f, 3.2f, 2.0f, s > 0 ? 15.4f : -15.6f, s > 0 ? 15.6f : -15.4f, 2.6f, 3.3f, Trim);
            }
        };

        r["turbofan"] = b =>
        {
            b.Barrel(-2.6f, 2.0f, 0, 0, 1.15f, 1.15f, Body, 16, 0.8f, 0.15f);
            Ring(b, new Vector3(0, 1.15f, -2.6f), 1.08f, 16, 0.1f, Metal, 90);
            b.Tube(0, 1.15f, -2.3f, 0, 1.15f, -2.4f, 0.98f, Dark, 16).Ghost();
            b.Tube(0, 1.15f, -2.3f, 0, 1.15f, -2.62f, 0.3f, Metal, 10, 0.1f);
            b.Tube(0, 1.25f, 2.0f, 0, 1.25f, 2.7f, 0.58f, Dark, 10, 0.25f);
            b.Box(-0.15f, 2.2f, -1.0f, 0.15f, 3.0f, 1.6f, Body);
        };

        r["prop-engine"] = b =>
        {
            b.Barrel(-1.3f, 2.2f, 0, 1.25f, 0.7f, 0.7f, Body, 12, 0.7f);
            b.Tube(0, 1.95f, -1.3f, 0, 1.95f, -1.95f, 0.35f, Metal, 10, 0.1f);
            for (int i = 0; i < 4; i++)
            {
                float a = -MathF.PI / 2 + i * MathF.PI / 2;
                b.Beam(new Vector3(0, 1.95f, -1.55f), new Vector3(MathF.Cos(a) * 1.95f, 1.95f + MathF.Sin(a) * 1.95f, -1.55f), 0.28f, 0.06f, Dark).Loose();
            }
            b.Box(-0.15f, 2.5f, -0.6f, 0.15f, 4.0f, 1.6f, Body);
            b.Box(-0.4f, 1.3f, -0.4f, 0.4f, 1.45f, 0.6f, Dark).Ghost();
        };

        r["landing-gear"] = b =>
        {
            foreach (float x in new[] { -0.6f, 0.6f }) foreach (float z in new[] { -0.48f, 0.48f }) Wheel(b, x, 0.45f, z, 0.45f, 0.3f);
            b.Box(-0.45f, 0.38f, -0.6f, 0.45f, 0.52f, 0.6f, Metal);
            foreach (float z in new[] { -0.48f, 0.48f }) b.Tube(-0.75f, 0.45f, z, 0.75f, 0.45f, z, 0.06f, Metal, 6).Ghost();
            b.Tube(0, 0.5f, 0, 0, 2.95f, 0, 0.14f, Metal, 8);
            b.Beam(new Vector3(0, 0.6f, 0.15f), new Vector3(0, 1.1f, 0.35f), 0.06f, 0.06f, Dark);
            b.Beam(new Vector3(0, 1.1f, 0.35f), new Vector3(0, 1.6f, 0.15f), 0.06f, 0.06f, Dark);
            b.Box(0.2f, 1.9f, -0.6f, 0.25f, 2.95f, 0.6f, Body).Loose();
        };

        r["crash-debris"] = b =>
        {
            // Torn panels: most lie flat; one tipped up off an edge rests its raised end on a crumpled chunk.
            for (int i = 0; i < 9; i++)
            {
                float x = b.Rng.Range(-2.8f, 2.8f), z = b.Rng.Range(-2.8f, 2.8f), sx = b.Rng.Range(0.5f, 1.2f), sz = b.Rng.Range(0.4f, 0.9f);
                float tip = b.Rng.Chance(0.5f) ? b.Rng.Range(6f, 18f) : 0f;
                using (b.Turn(new Vector3(x, 0, z), 0, b.Rng.Range(0, 180)))
                {
                    using (b.Turn(new Vector3(x - sx / 2, 0, z), 0, 0, tip))
                        b.Box(x - sx / 2, 0, z - sz / 2, x + sx / 2, 0.06f, z + sz / 2, i % 3 == 0 ? Trim : Body);
                    if (tip > 0)
                    {
                        // The chunk under it: the panel's underside rests on its nearer top edge.
                        float d = sx - 0.14f, top = d * MathF.Sin(tip * DecoGeo.Deg) - 0.07f * MathF.Tan(tip * DecoGeo.Deg);
                        float cx = x - sx / 2 + d * MathF.Cos(tip * DecoGeo.Deg);
                        b.Box(cx - 0.07f, 0, z - sz * 0.3f, cx + 0.07f, top, z + sz * 0.3f, Dark);
                    }
                }
            }
            // Seats torn out in pairs, some upside down.
            for (int i = 0; i < 4; i++)
            {
                float x = b.Rng.Range(-2.8f, 2.8f), z = b.Rng.Range(-2.8f, 2.8f);
                using (b.Turn(new Vector3(x, 0, z), 0, b.Rng.Range(0, 180)))
                using (b.Turn(new Vector3(x, 0.45f, z), 0, 0, i % 2 == 0 ? 0 : 180))
                {
                    b.Box(x - 0.6f, 0.05f, z - 0.25f, x + 0.6f, 0.4f, z + 0.25f, Fabric).Soft();
                    b.Box(x - 0.6f, 0.4f, z + 0.18f, x + 0.6f, 0.85f, z + 0.3f, Fabric).Soft();
                }
            }
            for (int i = 0; i < 5; i++)
            {
                float x = b.Rng.Range(-3.2f, 3.2f), z = b.Rng.Range(-3.2f, 3.2f);
                using (b.Turn(new Vector3(x, 0, z), 0, b.Rng.Range(0, 180)))
                    b.Box(x - 0.3f, 0.0f, z - 0.12f, x + 0.3f, 0.45f, z + 0.12f, new[] { Fabric, Alert, Dark }[i % 3]).Soft();
            }
            b.Box(-0.4f, 0, 2.6f, 0.9f, 0.5f, 3.4f, Body);
            b.Hang(new Vector3(-0.35f, 0.48f, 2.9f), new Vector3(-1, 0, -0.5f), 0.025f, Dark, 0.8f);
            b.Box(1.8f, 0, -2.8f, 2.6f, 0.08f, -1.4f, Canvas).Soft();
        };

        r["crash-scar"] = b =>
        {
            for (int i = 0; i < 8; i++)
            {
                float z = -8.2f + i * 2.35f, w = 2.6f + b.Rng.Range(0, 0.6f);
                using (b.Turn(new Vector3(0, 0, z), 0, b.Rng.Range(-6, 6))) b.Box(-w, 0, z - 1.35f, w, 0.03f, z + 1.35f, Soil);
            }
            foreach (float s in new[] { -1f, 1f })
                for (int i = 0; i < 7; i++)
                {
                    float z = -8.3f + i * 2.75f + b.Rng.Range(-0.5f, 0.5f);
                    b.Dome(s * (3.1f + b.Rng.Range(-0.2f, 0.3f)), 0, z, 0.55f, b.Rng.Range(0.18f, 0.32f), 1.2f, Soil, 10);
                }
            for (int i = 0; i < 12; i++) b.Dome(b.Rng.Range(-3f, 3f), 0, b.Rng.Range(-9.5f, 9.5f), b.Rng.Range(0.08f, 0.2f), 0.06f, b.Rng.Range(0.08f, 0.2f), Stone, 6).Ghost();
        };

        r["boarding-stairs"] = b =>
        {
            foreach (float x in new[] { -0.85f, 0.85f }) foreach (float z in new[] { -1.7f, 2.3f }) Wheel(b, x, 0.35f, z, 0.35f, 0.2f);
            b.Box(-1.0f, 0.35f, -2.2f, 1.0f, 0.75f, 2.9f, Body);
            b.Box(-1.0f, 0.5f, -2.25f, 1.0f, 0.6f, 2.95f, Hazard).Ghost();
            foreach (float s in new[] { -1f, 1f })
            {
                b.Beam(new Vector3(s * 0.65f, 0.75f, 2.8f), new Vector3(s * 0.65f, 3.3f, -1.45f), 0.12f, 0.08f, Metal);
                b.Beam(new Vector3(s * 0.68f, 1.65f, 2.8f), new Vector3(s * 0.68f, 4.2f, -1.45f), 0.05f, 0.05f, Metal);
                for (int i = 0; i < 5; i++) b.Tube(s * 0.68f, 0.75f + i * 0.64f, 2.8f - i * 1.06f, s * 0.68f, 1.65f + i * 0.64f, 2.8f - i * 1.06f, 0.02f, Metal).Ghost();
                b.Tube(s * 0.72f, 3.35f, -1.45f, s * 0.72f, 4.25f, -1.45f, 0.025f, Metal);
                b.Tube(s * 0.72f, 3.35f, -2.9f, s * 0.72f, 4.8f, -2.9f, 0.04f, Metal);
                b.Tube(s * 0.72f, 4.25f, -1.45f, s * 0.72f, 4.25f, -2.9f, 0.025f, Metal);
            }
            for (int i = 0; i < 9; i++)
            {
                float t = (i + 0.5f) / 9f;
                float y = 0.75f + t * 2.55f, z = 2.8f - t * 4.25f;
                b.Box(-0.6f, y - 0.03f, z - 0.15f, 0.6f, y, z + 0.15f, Metal).Ghost();
            }
            b.Box(-0.75f, 3.25f, -2.95f, 0.75f, 3.35f, -1.4f, Metal);
            foreach (float x in new[] { -0.7f, 0.7f }) b.Box(x - 0.05f, 0.75f, -2.9f, x + 0.05f, 3.25f, -2.8f, Metal);
            foreach (float x in new[] { -0.72f, 0.72f }) b.Box(x - 0.03f, 3.35f, -1.55f, x + 0.03f, 4.8f, -1.5f, Metal).Ghost();
            b.Box(-0.8f, 4.8f, -2.95f, 0.8f, 4.9f, -1.45f, Body);
        };

        r["airbridge"] = b =>
        {
            b.Cyl(0, 0, 4.3f, 0.9f, 5.9f, Metal, 12);
            b.Box(-1.5f, 3.3f, -4.6f, 1.5f, 5.6f, 4.3f, Body);
            foreach (float s in new[] { -1f, 1f }) b.Box(s > 0 ? 1.49f : -1.51f, 4.1f, -4.3f, s > 0 ? 1.51f : -1.49f, 5.0f, 3.9f, Glass).Pane();
            b.Box(-1.55f, 5.6f, -4.65f, 1.55f, 5.7f, 4.3f, Dark);
            foreach (float x in new[] { -1.2f, 1.2f })
            {
                b.Box(x - 0.15f, 0.4f, -3.2f, x + 0.15f, 3.3f, -2.9f, Metal);
                Wheel(b, x, 0.4f, -3.05f, 0.4f, 0.3f);
            }
            b.Box(-1.2f, 0.75f, -3.15f, 1.2f, 0.95f, -2.95f, Metal).Ghost();
            b.Box(-1.75f, 3.15f, -5.4f, 1.75f, 5.75f, -4.6f, Body);
            b.Box(-1.6f, 3.25f, -5.5f, 1.6f, 5.65f, -5.4f, Canvas).Soft();
            b.Box(-1.0f, 3.3f, -5.45f, 1.0f, 3.35f, -4.6f, Dark).Ghost();
            b.Cable(new Vector3(0.6f, 4.5f, 4.3f), new Vector3(0.6f, 3.32f, 3.6f), 0.03f, Dark, 0.4f);
        };

        // ---- the high-tech skyliner: an elliptical lifting body with glowing window bands ----
        const float RX = 2.4f, RY = 1.85f;
        r["skyliner-body"] = b =>
        {
            b.Barrel(-4f, 4f, 0, 0, RX, RY, Body, 16);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(s > 0 ? 2.31f : -2.38f, 2.0f, -3.8f, s > 0 ? 2.38f : -2.31f, 2.38f, 3.8f, Screen).Lit();
                b.Box(s > 0 ? 2.2f : -2.4f, 1.15f, -4f, s > 0 ? 2.4f : -2.2f, 1.3f, 4f, Trim).Ghost();
            }
            b.Box(-2.42f, 0.85f, 1.8f, -2.32f, 2.9f, 2.7f, Body).Loose().Door(-2.37f, 2.7f, 70);
            b.Light(0, 2.2f, -2.6f, Screen);
        };

        r["skyliner-nose"] = b =>
        {
            b.Barrel(1.0f, 4f, 0, 0, RX, RY, Body, 16);
            b.Barrel(-3.9f, 1.0f, 0, 0, 0.6f, 0.46f, Body, 16, RX / 0.6f, 0.6f);
            b.Hull(new[]
            {
                new Vector3(-0.6f, 2.15f, -3.0f), new Vector3(0.6f, 2.15f, -3.0f), new Vector3(-0.6f, 2.2f, -3.0f), new Vector3(0.6f, 2.2f, -3.0f),
                new Vector3(-1.5f, 3.35f, -1.0f), new Vector3(1.5f, 3.35f, -1.0f), new Vector3(-1.5f, 3.4f, -1.0f), new Vector3(1.5f, 3.4f, -1.0f),
            }, Glass).Pane();
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(s > 0 ? 2.31f : -2.38f, 2.0f, 1.2f, s > 0 ? 2.38f : -2.31f, 2.38f, 3.8f, Screen).Lit();
                b.Box(s > 0 ? 2.2f : -2.4f, 1.15f, 1.0f, s > 0 ? 2.4f : -2.2f, 1.3f, 4f, Trim).Ghost();
            }
            b.Ball(0, 1.2f, -3.86f, 0.3f, 0.25f, 0.1f, Lamp, 8).Lit().Ghost();
            b.Light(0, 1.2f, -4.2f, Lamp);
        };

        r["skyliner-wing"] = b =>
        {
            using var lowered = b.Shift(0, -0.4f, 0);
            b.Box(-2.4f, 0.4f, -3.5f, 2.4f, 0.95f, 3.7f, Body);
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 P(float x, float y, float z) => new(s * x, y, z);
                Vector3[] wing =
                {
                    P(2.3f, 0.4f, -3.6f), P(13.5f, 0.9f, 2.4f), P(2.3f, 0.95f, -3.6f), P(13.5f, 1.05f, 2.4f),
                    P(2.3f, 0.4f, 3.8f), P(13.5f, 0.9f, 3.9f), P(2.3f, 0.95f, 3.8f), P(13.5f, 1.05f, 3.9f),
                };
                if (s < 0) wing = Mirror(wing);
                DecoPart w = b.Hull(wing, Body);
                if (s > 0) w.Loose();
                b.Beam(P(2.3f, 0.72f, -3.62f), P(13.5f, 0.99f, 2.36f), 0.08f, 0.08f, Screen).Lit();
                b.Frustum(1.0f, s > 0 ? 13.4f : -13.75f, s > 0 ? 13.75f : -13.4f, 2.2f, 3.9f, 1.5f, s > 0 ? 13.5f : -13.75f, s > 0 ? 13.75f : -13.5f, 3.1f, 3.95f, Trim);
            }
            b.Light(0, 1.2f, 0, Screen);
        };

        r["skyliner-tail"] = b =>
        {
            b.Barrel(-3.2f, 3.4f, 0, 0, RX, RY, Body, 16, 0.2f, 1.0f);
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 P(float x, float y, float z) => new(s * x, y, z);
                Vector3[] fin =
                {
                    P(0.4f, 3.1f, -1.0f), P(0.7f, 3.1f, -1.0f), P(2.9f, 6.8f, 1.8f), P(3.1f, 6.8f, 1.8f),
                    P(0.4f, 3.1f, 3.2f), P(0.7f, 3.1f, 3.2f), P(2.9f, 6.8f, 3.45f), P(3.1f, 6.8f, 3.45f),
                };
                if (s < 0) fin = Mirror(fin);
                b.Hull(fin, Trim);
                b.Beam(P(0.75f, 3.4f, 0.0f), P(2.85f, 6.6f, 2.0f), 0.06f, 0.06f, Screen).Lit().Ghost();
            }
            b.Tube(0, 2.95f, 3.3f, 0, 2.95f, 3.42f, 0.35f, Lamp, 10).Lit().Ghost();
        };

        // ---- the hangar floor: indoor pieces of the aviation set ----

        r["engine-stand"] = b =>
        {
            // A turbofan off its wing, on a wheeled cradle: two saddles under the nacelle, rails between them.
            foreach (float z in new[] { -1.1f, 1.1f })
            {
                b.Box(-0.85f, 0.2f, z - 0.08f, 0.85f, 0.32f, z + 0.08f, Hazard);
                foreach (float x in new[] { -0.72f, 0.72f }) Wheel(b, x, 0.12f, z, 0.12f, 0.08f);
                b.Beam(new Vector3(-0.55f, 0.32f, z), new Vector3(-0.28f, 0.78f, z), 0.08f, 0.08f, Dark);
                b.Beam(new Vector3(0.55f, 0.32f, z), new Vector3(0.28f, 0.78f, z), 0.08f, 0.08f, Dark);
            }
            foreach (float x in new[] { -0.62f, 0.62f }) b.Box(x - 0.05f, 0.22f, -1.02f, x + 0.05f, 0.3f, 1.02f, Dark);
            b.Barrel(-1.3f, 1.05f, 0, 0.62f, 0.68f, 0.68f, Body, 16, 0.85f);
            Ring(b, new Vector3(0, 1.3f, -1.3f), 0.63f, 16, 0.06f, Metal, 90);
            b.Tube(0, 1.3f, -1.18f, 0, 1.3f, -1.26f, 0.56f, Dark, 16).Ghost();
            b.Tube(0, 1.3f, -1.18f, 0, 1.3f, -1.42f, 0.16f, Metal, 10, 0.1f);
            b.Tube(0, 1.3f, 1.05f, 0, 1.3f, 1.45f, 0.38f, Dark, 10, 0.3f);
        };

        r["airline-seats"] = b =>
        {
            // A pair of airliner seats on one frame: cushions, tall backs with headrest covers, armrests between and either side.
            foreach (float x in new[] { -0.4f, 0.4f }) b.Box(x - 0.03f, 0, -0.25f, x + 0.03f, 0.38f, 0.25f, Metal);
            foreach (float x in new[] { -0.24f, 0.24f })
            {
                b.Box(x - 0.21f, 0.38f, -0.3f, x + 0.21f, 0.5f, 0.25f, Fabric).Soft();
                b.Box(x - 0.21f, 0.5f, 0.22f, x + 0.21f, 1.15f, 0.34f, Fabric).Soft();
                b.Box(x - 0.17f, 0.98f, 0.18f, x + 0.17f, 1.12f, 0.22f, Linen).Soft();
            }
            foreach (float x in new[] { -0.46f, 0f, 0.46f }) b.Box(x - 0.025f, 0.5f, -0.25f, x + 0.025f, 0.68f, 0.2f, Dark);
        };

        r["aircraft-tug"] = b =>
        {
            // A low tow tractor: a long flat body on four wheels, the driver's seat at the back, a hitch at the front.
            foreach (float s in new[] { -1f, 1f }) { Wheel(b, s * 0.55f, 0.25f, -0.72f, 0.25f, 0.2f); Wheel(b, s * 0.55f, 0.25f, 0.72f, 0.25f, 0.2f); }
            b.Box(-0.7f, 0.22f, -1.12f, 0.7f, 0.62f, 1.15f, Hazard);
            b.Box(-0.72f, 0.2f, -1.22f, 0.72f, 0.38f, -1.1f, Dark);
            b.Box(-0.12f, 0.24f, -1.25f, 0.12f, 0.32f, -1.2f, Metal);
            b.Box(-0.3f, 0.62f, 0.32f, 0.3f, 0.78f, 0.82f, Fabric).Soft();
            b.Box(-0.3f, 0.78f, 0.76f, 0.3f, 1.15f, 0.88f, Fabric).Soft();
            b.Tube(0, 0.62f, -0.08f, 0, 1.0f, 0.18f, 0.025f, Dark, 6);
            Ring(b, new Vector3(0, 1.02f, 0.2f), 0.16f, 8, 0.025f, Dark, -55, 0);
        };

        r["skyliner-engine"] = b =>
        {
            Ring(b, new Vector3(0, 1.92f, 0), 1.65f, 16, 0.55f, Body, 90);
            Ring(b, new Vector3(0, 1.93f, -0.32f), 1.32f, 16, 0.08f, Lamp, 90);
            b.Tube(0, 1.93f, -1.6f, 0, 1.93f, 1.8f, 0.45f, Dark, 10);
            b.Tube(0, 1.93f, 1.8f, 0, 1.93f, 2.1f, 0.3f, Lamp, 10).Lit().Ghost();
            for (int i = 0; i < 4; i++)
            {
                float a = i * MathF.PI / 2 + MathF.PI / 4;
                b.Beam(new Vector3(MathF.Cos(a) * 0.45f, 1.93f + MathF.Sin(a) * 0.45f, 0), new Vector3(MathF.Cos(a) * 1.4f, 1.93f + MathF.Sin(a) * 1.4f, 0), 0.1f, 0.3f, Dark);
            }
            b.Box(-0.18f, 3.55f, -0.8f, 0.18f, 4.0f, 1.2f, Body);
            b.Light(0, 1.93f, -1.2f, Lamp);
        };
    }

    /// <summary>Reorders an eight-corner hull built on the -x side so its corner bits still mean +x (the points are already mirrored).</summary>
    static Vector3[] Mirror(Vector3[] c)
    {
        var m = new Vector3[8];
        for (int i = 0; i < 8; i++) m[i ^ 1] = c[i];
        return m;
    }
}
