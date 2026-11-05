using System;
using System.Collections.Generic;
using System.Numerics;
using static GalHack.Deco.DecoSlot;

namespace GalHack.Deco;

public static partial class DecoModels
{
    // ---- car parts ------------------------------------------------------------------------------

    /// <summary>
    /// A glass greenhouse from the waist (y0) to the roof (y1): bottom from z bz0 to bz1, roof from
    /// tz0 to tz1, with corner pillars and a roof panel. The glass shatters; the pillars and roof stay.
    /// </summary>
    static void Greenhouse(DecoBuilder b, float y0, float bw, float bz0, float bz1, float y1, float tw, float tz0, float tz1, bool bPillar = true)
    {
        b.Frustum(y0, -bw + 0.02f, bw - 0.02f, bz0 + 0.02f, bz1 - 0.02f, y1, -tw + 0.02f, tw - 0.02f, tz0, tz1, Glass).Pane();
        foreach (float s in new[] { -1f, 1f })
        {
            b.Beam(new Vector3(s * bw, y0, bz0), new Vector3(s * tw, y1, tz0), 0.07f, 0.07f, Body);
            b.Beam(new Vector3(s * bw, y0, bz1), new Vector3(s * tw, y1, tz1), 0.09f, 0.09f, Body);
            if (bPillar) b.Beam(new Vector3(s * bw, y0, (bz0 + bz1) / 2 + 0.1f), new Vector3(s * tw, y1, (tz0 + tz1) / 2 + 0.1f), 0.07f, 0.07f, Body);
        }
        b.Box(-tw - 0.02f, y1, tz0 - 0.03f, tw + 0.02f, y1 + 0.06f, tz1 + 0.03f, Body);
    }

    /// <summary>A side door panel on both flanks from z0 to z1, hinged at its front edge and swinging out.</summary>
    static void Doors(DecoBuilder b, float x, float y0, float y1, float z0, float z1, DecoSlot s = Body)
    {
        b.Box(-x - 0.025f, y0, z0, -x + 0.005f, y1, z1, s).Loose().Door(-x - 0.01f, z0, -68);
        b.Box(x - 0.005f, y0, z0, x + 0.025f, y1, z1, s).Loose().Door(x + 0.01f, z0, 68);
        b.Box(-x - 0.03f, (y0 + y1) / 2 + 0.05f, z1 - 0.18f, -x - 0.025f, (y0 + y1) / 2 + 0.08f, z1 - 0.06f, Chrome).Ghost();
        b.Box(x + 0.025f, (y0 + y1) / 2 + 0.05f, z1 - 0.18f, x + 0.03f, (y0 + y1) / 2 + 0.08f, z1 - 0.06f, Chrome).Ghost();
    }

    /// <summary>Seat backs seen through the glass (rows across the car at each z).</summary>
    static void Seats(DecoBuilder b, float x, float y0, float y1, params float[] zs)
    {
        foreach (float z in zs)
        {
            b.Box(-x, y0, z, -0.06f, y1, z + 0.12f, Fabric).Soft().Ghost();
            b.Box(0.06f, y0, z, x, y1, z + 0.12f, Fabric).Soft().Ghost();
        }
    }

    /// <summary>Headlights on the front face (z) and tail lights on the back (zb).</summary>
    static void Lamps(DecoBuilder b, float x, float y, float z, float zb, float w = 0.22f, float h = 0.12f)
    {
        foreach (float s in new[] { -1f, 1f })
        {
            b.Box(s * x - w / 2, y, z - 0.012f, s * x + w / 2, y + h, z, Lamp).Lit().Ghost();
            b.Box(s * x - w / 2, y, zb, s * x + w / 2, y + h, zb + 0.012f, Alert).Ghost();
        }
    }

    static void Vehicles(Dictionary<string, Action<DecoBuilder>> r)
    {
        // ---- low tech ----
        r["farm-cart"] = b =>
        {
            foreach (float x in new[] { -0.78f, 0.78f })
            {
                Ring(b, new Vector3(x, 0.56f, 0.25f), 0.53f, 12, 0.06f, Wood, 0, 90);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * MathF.PI / 3;
                    b.Beam(new Vector3(x, 0.56f, 0.25f), new Vector3(x, 0.56f + MathF.Sin(a) * 0.5f, 0.25f + MathF.Cos(a) * 0.5f), 0.035f, 0.035f, Wood).Ghost();
                }
                b.Tube(x - 0.08f, 0.56f, 0.25f, x + 0.08f, 0.56f, 0.25f, 0.07f, Metal, 6);
            }
            b.Tube(-0.72f, 0.56f, 0.25f, 0.72f, 0.56f, 0.25f, 0.04f, Wood, 6);
            b.Box(-0.68f, 0.62f, -0.6f, 0.68f, 0.7f, 1.4f, Wood);
            foreach (float s in new[] { -1f, 1f }) b.Box(s * 0.62f, 0.7f, -0.6f, s * 0.68f, 1.0f, 1.4f, Wood).Loose();
            b.Box(-0.68f, 0.7f, 1.35f, 0.68f, 0.95f, 1.4f, Wood).Loose();
            b.Box(-0.68f, 0.7f, -0.6f, 0.68f, 1.0f, -0.55f, Wood);
            foreach (float s in new[] { -1f, 1f }) b.Beam(new Vector3(s * 0.42f, 0.64f, -0.55f), new Vector3(s * 0.32f, 0.04f, -1.44f), 0.07f, 0.06f, Wood);
            b.Ball(-0.25f, 0.85f, 0.4f, 0.3f, 0.15f, 0.2f, Canvas, 8).Soft();
            b.Ball(0.2f, 0.85f, 0.8f, 0.28f, 0.14f, 0.2f, Canvas, 8).Soft();
            b.Box(0.1f, 0.7f, -0.3f, 0.55f, 1.05f, 0.1f, Wood).Loose();
        };

        r["farm-tractor"] = b =>
        {
            foreach (float s in new[] { -1f, 1f })
            {
                Wheel(b, s * 0.78f, 0.75f, 0.7f, 0.75f, 0.32f);
                Wheel(b, s * 0.56f, 0.38f, -1.15f, 0.38f, 0.16f);
                b.Box(s * 0.56f, 1.42f, -0.05f, s * 0.98f, 1.48f, 1.32f, Body);
                b.Box(s * 0.56f, 0.9f, -0.05f, s * 0.6f, 1.42f, 1.32f, Body).Ghost();
            }
            b.Box(-0.22f, 0.32f, -1.6f, 0.22f, 0.5f, 0.5f, Dark);
            b.Tube(-0.5f, 0.38f, -1.15f, 0.5f, 0.38f, -1.15f, 0.05f, Dark, 6);
            b.Box(-0.32f, 0.5f, -1.62f, 0.32f, 1.22f, 0.2f, Body);
            b.Box(-0.3f, 0.55f, -1.64f, 0.3f, 1.15f, -1.6f, Dark);
            for (int i = 0; i < 5; i++) b.Box(-0.33f, 0.62f + i * 0.1f, -1.4f + i * 0.0f, -0.325f, 0.66f + i * 0.1f, -0.6f, Dark).Ghost();
            b.Cyl(0.18f, 1.22f, -1.2f, 0.05f, 0.62f, Dark, 6);
            b.Cyl(0.18f, 1.84f, -1.2f, 0.065f, 0.05f, Dark, 6).Ghost();
            b.Box(-0.42f, 0.5f, 0.2f, 0.42f, 1.05f, 1.25f, Dark);
            b.Box(-0.24f, 1.18f, 0.7f, 0.24f, 1.28f, 1.05f, Fabric).Soft();
            b.Box(-0.24f, 1.28f, 1.0f, 0.24f, 1.6f, 1.08f, Fabric).Soft();
            b.Tube(0, 1.15f, 0.2f, 0, 1.55f, 0.42f, 0.025f, Dark, 6);
            Ring(b, new Vector3(0, 1.57f, 0.45f), 0.17f, 10, 0.03f, Dark, -60, 0);
            foreach (float s in new[] { -1f, 1f }) b.Tube(s * 0.24f, 1.0f, -1.63f, s * 0.24f, 1.0f, -1.68f, 0.06f, Lamp).Lit().Ghost();
            b.Box(-0.1f, 0.45f, 1.25f, 0.1f, 0.6f, 1.45f, Dark);
        };

        r["plough"] = b =>
        {
            b.Beam(new Vector3(-0.5f, 0.75f, -1.2f), new Vector3(0.6f, 0.72f, 1.1f), 0.12f, 0.1f, Metal);
            b.Beam(new Vector3(-0.8f, 0.72f, -0.4f), new Vector3(0.85f, 0.72f, -0.4f), 0.1f, 0.1f, Metal);
            for (int i = 0; i < 3; i++)
            {
                float x = -0.55f + i * 0.5f, z = -0.6f + i * 0.55f;
                b.Box(x - 0.03f, 0.25f, z - 0.03f, x + 0.03f, 0.74f, z + 0.03f, Metal);
                using (b.Turn(new Vector3(x, 0.15f, z), 0, 35, 0))
                {
                    b.Wedge(x - 0.03f, 0.04f, z - 0.35f, x + 0.03f, z + 0.25f, 0.08f, 0.45f, Metal);
                    using (b.Turn(new Vector3(x, 0.25f, z), 0, 0, -25)) b.Box(x, 0.14f, z - 0.15f, x + 0.25f, 0.42f, z + 0.25f, Rust);
                }
            }
            b.Beam(new Vector3(-0.45f, 0.72f, -1.12f), new Vector3(-0.45f, 0.06f, -1.17f), 0.08f, 0.08f, Metal);
            Wheel(b, 0.85f, 0.3f, 0.95f, 0.3f, 0.1f, Metal, Dark);
            b.Beam(new Vector3(0.6f, 0.72f, 1.0f), new Vector3(0.85f, 0.3f, 0.95f), 0.06f, 0.06f, Metal);
        };

        r["farm-trailer"] = b =>
        {
            foreach (float z in new[] { -1.3f, 1.3f })
            {
                foreach (float s in new[] { -1f, 1f }) Wheel(b, s * 0.98f, 0.4f, z, 0.4f, 0.22f);
                b.Tube(-0.9f, 0.4f, z, 0.9f, 0.4f, z, 0.05f, Dark, 6);
            }
            b.Box(-0.9f, 0.6f, -1.95f, 0.9f, 0.72f, 2.2f, Dark);
            b.Box(-1.1f, 0.72f, -1.95f, 1.1f, 0.82f, 2.2f, Wood);
            foreach (float z in new[] { -1.9f, 2.15f })
            {
                foreach (float x in new[] { -1.05f, -0.35f, 0.35f, 1.05f }) b.Box(x - 0.04f, 0.82f, z - 0.03f, x + 0.04f, 1.95f, z + 0.03f, Wood);
                foreach (float y in new[] { 1.2f, 1.6f, 1.92f }) b.Box(-1.1f, y, z - 0.03f, 1.1f, y + 0.05f, z + 0.03f, Wood).Loose();
            }
            b.Box(-1.0f, 0.82f, -1.8f, 1.0f, 1.55f, 2.05f, Canvas).Rots();
            b.Ball(0, 1.55f, 0.1f, 0.9f, 0.3f, 1.7f, Canvas, 8).Rots().Ghost();
            b.Beam(new Vector3(0, 0.65f, -1.95f), new Vector3(0, 0.08f, -2.2f), 0.1f, 0.1f, Dark);
            b.Box(-0.12f, 0, -2.25f, 0.12f, 0.06f, -2.12f, Dark).Ghost();
        };

        r["old-truck"] = b =>
        {
            foreach (float s in new[] { -1f, 1f })
            {
                Wheel(b, s * 0.78f, 0.45f, -1.7f, 0.45f, 0.22f);
                Wheel(b, s * 0.78f, 0.45f, 1.6f, 0.45f, 0.22f);
                b.Dome(s * 0.74f, 0.62f, -1.7f, 0.2f, 0.36f, 0.6f, Body, 10);
                b.Box(s * 0.5f, 0.55f, -1.15f, s * 0.95f, 0.6f, 0.4f, Dark);
                b.Box(s * 0.62f, 0.95f, 1.15f, s * 0.95f, 1.0f, 2.05f, Dark);
                b.Tube(s * 0.62f, 1.02f, -2.2f, s * 0.62f, 1.02f, -2.3f, 0.12f, Lamp).Lit();
            }
            b.Box(-0.6f, 0.45f, -2.7f, 0.6f, 0.62f, 2.65f, Dark);
            b.Box(-0.45f, 0.62f, -2.62f, 0.45f, 1.3f, -0.95f, Body);
            b.Box(-0.42f, 0.66f, -2.7f, 0.42f, 1.28f, -2.62f, Chrome);
            for (int i = 0; i < 6; i++) b.Box(-0.38f + i * 0.15f, 0.7f, -2.705f, -0.34f + i * 0.15f, 1.24f, -2.7f, Dark).Ghost();
            b.Box(-0.65f, 0.4f, -2.75f, 0.65f, 0.55f, -2.6f, Chrome);
            b.Box(-0.88f, 0.62f, -0.95f, 0.88f, 1.45f, 0.35f, Body);
            Doors(b, 0.88f, 0.68f, 1.42f, -0.9f, 0.1f);
            Greenhouse(b, 1.45f, 0.86f, -0.9f, 0.3f, 1.98f, 0.82f, -0.82f, 0.25f, bPillar: false);
            b.Dome(0, 2.03f, -0.28f, 0.84f, 0.16f, 0.58f, Body, 12);
            Seats(b, 0.7f, 1.3f, 1.7f, 0.15f);
            b.Box(-0.95f, 0.95f, 0.4f, 0.95f, 1.05f, 2.7f, Wood);
            foreach (float s in new[] { -1f, 1f })
            {
                for (int i = 0; i < 5; i++) b.Box(s * 0.92f - 0.03f, 1.05f, 0.5f + i * 0.5f, s * 0.92f + 0.03f, 1.6f, 0.56f + i * 0.5f, Wood);
                b.Box(s * 0.92f - 0.03f, 1.45f, 0.45f, s * 0.92f + 0.03f, 1.55f, 2.65f, Wood).Loose();
            }
            b.Cyl(-0.4f, 1.05f, 1.6f, 0.28f, 0.85f, Rust, 10);
            b.Cyl(0.3f, 1.05f, 1.9f, 0.28f, 0.85f, Rust, 10).Loose();
            b.Box(0.1f, 1.05f, 0.6f, 0.75f, 1.45f, 1.15f, Wood).Loose();
            b.Box(-0.3f, 0.75f, 2.65f, 0.3f, 0.85f, 2.7f, Paper).Ghost();
        };

        r["ore-tram"] = b =>
        {
            foreach (float z in new[] { -1.3f, -0.65f, 0.4f, 1.3f })
                foreach (float s in new[] { -1f, 1f }) Wheel(b, s * 0.5f, 0.22f, z, 0.22f, 0.1f, Metal, Dark);
            b.Box(-0.6f, 0.22f, -1.7f, 0.6f, 0.4f, 1.7f, Dark);
            b.Box(-0.62f, 0.4f, -1.7f, 0.62f, 1.05f, -0.3f, Hazard);
            b.Box(-0.4f, 1.05f, -1.15f, 0.4f, 1.15f, -0.4f, Dark);
            foreach (float x in new[] { -0.55f, 0.55f }) foreach (float z in new[] { -1.6f, -0.4f }) b.Box(x - 0.03f, 1.05f, z - 0.03f, x + 0.03f, 1.82f, z + 0.03f, Dark);
            b.Box(-0.62f, 1.82f, -1.68f, 0.62f, 1.88f, -0.32f, Dark);
            b.Box(-0.2f, 1.15f, -0.75f, 0.2f, 1.45f, -0.65f, Fabric).Soft();
            b.Tube(0, 0.75f, -1.7f, 0, 0.75f, -1.745f, 0.09f, Lamp).Lit();
            b.Box(-0.15f, 0.3f, -0.3f, 0.15f, 0.4f, 0.0f, Dark);
            b.Taper(-0.55f, 0.4f, 0.0f, 0.55f, 1.15f, 1.65f, 1.12f, 1.04f, Rust);
            for (int i = 0; i < 6; i++) b.Ball(b.Rng.Range(-0.4f, 0.4f), 1.1f, b.Rng.Range(0.2f, 1.45f), b.Rng.Range(0.08f, 0.14f), Stone, 5).Ghost();
        };

        // ---- mid tech ----
        r["hatchback"] = b =>
        {
            foreach (float s in new[] { -1f, 1f }) { Wheel(b, s * 0.74f, 0.31f, -1.25f, 0.31f, 0.2f); Wheel(b, s * 0.74f, 0.31f, 1.22f, 0.31f, 0.2f); }
            b.Box(-0.85f, 0.25f, -1.9f, 0.85f, 0.82f, 1.85f, Body);
            b.Box(-0.84f, 0.22f, -1.98f, 0.84f, 0.42f, -1.88f, Dark);
            b.Box(-0.84f, 0.22f, 1.82f, 0.84f, 0.42f, 1.95f, Dark);
            b.Wedge(-0.84f, 0.82f, -1.9f, 0.84f, -0.85f, 0.85f, 0.9f, Body);
            Greenhouse(b, 0.9f, 0.82f, -0.85f, 1.83f, 1.38f, 0.7f, -0.15f, 1.6f);
            Doors(b, 0.85f, 0.32f, 0.86f, -0.82f, 0.38f);
            Seats(b, 0.6f, 0.82f, 1.18f, 0.2f, 1.25f);
            Lamps(b, 0.62f, 0.58f, -1.9f, 1.85f);
            b.Box(-0.2f, 0.27f, -1.99f, 0.2f, 0.37f, -1.98f, Paper).Ghost();
            foreach (float s in new[] { -1f, 1f }) b.Box(s * 0.85f, 0.92f, -0.72f, s * 0.95f, 1.0f, -0.6f, Body).Ghost();
            b.Box(-0.55f, 1.44f, 0.1f, 0.5f, 1.5f, 1.2f, Canvas).Soft().Loose();
        };

        r["sedan"] = b =>
        {
            foreach (float s in new[] { -1f, 1f }) { Wheel(b, s * 0.76f, 0.32f, -1.55f, 0.32f, 0.2f); Wheel(b, s * 0.76f, 0.32f, 1.5f, 0.32f, 0.2f); }
            b.Box(-0.9f, 0.25f, -2.35f, 0.9f, 0.8f, 2.35f, Body);
            b.Box(-0.88f, 0.22f, -2.45f, 0.88f, 0.42f, -2.33f, Chrome);
            b.Box(-0.88f, 0.22f, 2.33f, 0.88f, 0.42f, 2.45f, Chrome);
            b.Wedge(-0.88f, 0.8f, -2.35f, 0.88f, -1.0f, 0.82f, 0.92f, Body);
            b.Wedge(-0.88f, 0.8f, 1.3f, 0.88f, 2.35f, 0.92f, 0.86f, Body);
            Greenhouse(b, 0.92f, 0.86f, -1.0f, 1.3f, 1.4f, 0.72f, -0.3f, 0.78f);
            Doors(b, 0.9f, 0.32f, 0.86f, -0.98f, 0.12f);
            Doors(b, 0.9f, 0.32f, 0.86f, 0.16f, 1.2f);
            Seats(b, 0.62f, 0.85f, 1.2f, 0.1f, 1.0f);
            Lamps(b, 0.65f, 0.58f, -2.35f, 2.35f, 0.3f, 0.1f);
            b.Box(-0.4f, 0.45f, -2.36f, 0.4f, 0.62f, -2.35f, Dark).Ghost();
            foreach (float s in new[] { -1f, 1f }) b.Box(s * 0.88f, 0.92f, -0.9f, s * 0.98f, 1.0f, -0.78f, Body).Ghost();
        };

        r["pickup"] = b =>
        {
            foreach (float s in new[] { -1f, 1f }) { Wheel(b, s * 0.8f, 0.38f, -1.75f, 0.38f, 0.24f); Wheel(b, s * 0.8f, 0.38f, 1.6f, 0.38f, 0.24f); }
            b.Box(-0.95f, 0.35f, -2.65f, 0.95f, 1.0f, 0.38f, Body);
            b.Box(-0.95f, 0.32f, -2.73f, 0.95f, 0.52f, -2.62f, Chrome);
            b.Box(-0.6f, 0.6f, -2.66f, 0.6f, 0.95f, -2.65f, Dark).Ghost();
            b.Wedge(-0.93f, 1.0f, -2.65f, 0.93f, -1.0f, 1.04f, 1.12f, Body);
            Greenhouse(b, 1.12f, 0.9f, -1.0f, 0.36f, 1.78f, 0.8f, -0.5f, 0.3f, bPillar: false);
            Doors(b, 0.95f, 0.4f, 1.05f, -0.98f, 0.3f);
            Seats(b, 0.7f, 1.05f, 1.5f, 0.15f);
            b.Box(-0.92f, 0.62f, 0.42f, 0.92f, 0.7f, 2.62f, Dark);
            b.Box(-0.95f, 0.35f, 0.42f, 0.95f, 0.62f, 2.65f, Body);
            foreach (float s in new[] { -1f, 1f }) b.Box(s * 0.88f, 0.62f, 0.42f, s * 0.95f, 1.08f, 2.62f, Body);
            b.Box(-0.9f, 0.62f, 0.38f, 0.9f, 1.08f, 0.44f, Body);
            b.Box(-0.9f, 0.45f, 2.6f, 0.9f, 1.05f, 2.66f, Body).Door(new Vector3(0, 0.46f, 2.63f), Vector3.UnitX, 85);
            Lamps(b, 0.7f, 0.75f, -2.65f, 2.66f, 0.2f, 0.14f);
            // Packed to flee: a mattress, a chair and boxes in the bed.
            using (b.Turn(new Vector3(0, 0.7f, 2.3f), 30)) b.Box(-0.75f, 0.7f, 0.6f, 0.75f, 0.85f, 2.3f, Linen).Soft();
            b.Box(-0.7f, 0.7f, 1.7f, -0.2f, 1.1f, 2.4f, Paper).Soft().Loose();
            b.Box(0.15f, 0.7f, 1.9f, 0.75f, 1.0f, 2.5f, Fabric).Soft();
        };

        r["van"] = b =>
        {
            foreach (float s in new[] { -1f, 1f }) { Wheel(b, s * 0.78f, 0.34f, -1.65f, 0.34f, 0.22f); Wheel(b, s * 0.78f, 0.34f, 1.6f, 0.34f, 0.22f); }
            b.Box(-0.97f, 0.3f, -1.6f, 0.97f, 2.12f, 2.42f, Body);
            b.Box(-0.95f, 0.3f, -2.42f, 0.95f, 0.98f, -1.6f, Body);
            b.Box(-0.95f, 0.28f, -2.5f, 0.95f, 0.45f, -2.4f, Dark);
            b.Hull(new[]
            {
                new Vector3(-0.93f, 0.98f, -2.3f), new Vector3(0.93f, 0.98f, -2.3f), new Vector3(-0.92f, 2.08f, -1.68f), new Vector3(0.92f, 2.08f, -1.68f),
                new Vector3(-0.93f, 0.98f, -1.6f), new Vector3(0.93f, 0.98f, -1.6f), new Vector3(-0.92f, 2.08f, -1.6f), new Vector3(0.92f, 2.08f, -1.6f),
            }, Glass).Pane();
            foreach (float s in new[] { -1f, 1f }) b.Beam(new Vector3(s * 0.94f, 0.98f, -2.3f), new Vector3(s * 0.94f, 2.1f, -1.68f), 0.08f, 0.08f, Body);
            b.Box(-0.97f, 2.1f, -1.72f, 0.97f, 2.18f, 2.42f, Body);
            foreach (float s in new[] { -1f, 1f }) b.Box(s * 0.97f - 0.01f, 1.35f, -1.35f, s * 0.97f + 0.01f, 1.85f, 0.3f, Glass).Pane();
            b.Box(-0.99f, 0.36f, -2.25f, -0.97f, 1.95f, -1.62f, Body).Loose().Door(-0.98f, -2.25f, -65);
            b.Box(0.97f, 0.36f, -2.25f, 0.99f, 1.95f, -1.62f, Body).Loose().Door(0.98f, -2.25f, 65);
            b.Box(0.97f, 0.36f, -1.3f, 0.995f, 1.98f, 0.0f, Body).Door(0.985f, -1.3f, 75);
            foreach (float s in new[] { -1f, 1f }) b.Box(s > 0 ? 0f : -0.95f, 0.36f, 2.42f, s > 0 ? 0.95f : 0f, 2.05f, 2.45f, Body).Door(s * 0.95f, 2.44f, -s * 95);
            Seats(b, 0.7f, 1.0f, 1.5f, -1.3f);
            Lamps(b, 0.7f, 0.62f, -2.42f, 2.45f, 0.24f, 0.14f);
            b.Box(-0.8f, 2.18f, -1.4f, 0.8f, 2.22f, 1.8f, Metal).Ghost();
            b.Box(-0.7f, 2.22f, -1.2f, 0.1f, 2.48f, 0.3f, Canvas).Soft().Loose();
            b.Box(0.15f, 2.22f, -0.2f, 0.75f, 2.42f, 1.4f, Fabric).Soft();
            b.Box(-0.98f, 0.95f, -1.55f, -0.97f, 1.15f, 2.4f, Trim).Ghost();
            b.Box(0.97f, 0.95f, 0.05f, 0.98f, 1.15f, 2.4f, Trim).Ghost();
        };

        r["box-truck"] = b =>
        {
            foreach (float s in new[] { -1f, 1f })
            {
                Wheel(b, s * 1.0f, 0.48f, -2.5f, 0.48f, 0.28f);
                Wheel(b, s * 0.95f, 0.48f, 1.7f, 0.48f, 0.38f);
                Wheel(b, s * 0.95f, 0.48f, 2.75f, 0.48f, 0.38f);
            }
            b.Box(-0.9f, 0.45f, -3.6f, 0.9f, 0.72f, 3.65f, Dark);
            b.Box(-1.15f, 0.72f, -3.65f, 1.15f, 1.62f, -1.95f, Body);
            b.Box(-1.2f, 0.45f, -3.73f, 1.2f, 0.78f, -3.6f, Dark);
            b.Box(-0.8f, 0.9f, -3.66f, 0.8f, 1.5f, -3.65f, Dark).Ghost();
            Greenhouse(b, 1.62f, 1.12f, -3.55f, -1.95f, 2.55f, 1.08f, -3.2f, -1.98f, bPillar: false);
            Doors(b, 1.15f, 0.78f, 1.58f, -3.3f, -2.2f);
            Seats(b, 0.85f, 1.5f, 2.05f, -2.5f);
            Lamps(b, 0.85f, 0.95f, -3.65f, 3.66f, 0.3f, 0.16f);
            b.Box(-1.22f, 0.95f, -1.9f, 1.22f, 3.4f, 3.6f, Plastic);
            foreach (float s in new[] { -1f, 1f }) b.Box(s * 1.22f - 0.005f, 1.5f, -1.5f, s * 1.22f + 0.005f, 2.2f, 2.8f, Trim).Ghost();
            for (int i = 0; i < 5; i++) b.Box(-1.12f, 1.7f + i * 0.33f, 3.6f, 1.12f, 1.98f + i * 0.33f, 3.66f, Metal).Loose();
            b.Box(-1.1f, 1.0f, 3.5f, 1.1f, 1.7f, 3.55f, Dark).Ghost();
            foreach (float s in new[] { -1f, 1f }) b.Beam(new Vector3(s * 1.12f, 2.2f, -3.3f), new Vector3(s * 1.2f, 2.3f, -3.35f), 0.04f, 0.04f, Dark).Ghost();
        };

        r["tanker-truck"] = b =>
        {
            foreach (float s in new[] { -1f, 1f })
            {
                Wheel(b, s * 1.0f, 0.48f, -3.5f, 0.48f, 0.28f);
                Wheel(b, s * 0.95f, 0.48f, 2.6f, 0.48f, 0.38f);
                Wheel(b, s * 0.95f, 0.48f, 3.6f, 0.48f, 0.38f);
            }
            b.Box(-0.85f, 0.45f, -4.4f, 0.85f, 0.75f, 4.3f, Dark);
            b.Box(-1.15f, 0.75f, -4.45f, 1.15f, 1.62f, -2.85f, Body);
            b.Box(-1.2f, 0.45f, -4.5f, 1.2f, 0.78f, -4.4f, Chrome);
            Greenhouse(b, 1.62f, 1.12f, -4.35f, -2.85f, 2.55f, 1.08f, -4.0f, -2.88f, bPillar: false);
            Doors(b, 1.15f, 0.8f, 1.58f, -4.1f, -3.05f);
            Lamps(b, 0.85f, 0.95f, -4.45f, 4.38f, 0.3f, 0.16f);
            b.Barrel(-2.55f, 4.1f, 0, 0.8f, 1.15f, 1.15f, Chrome, 14);
            b.Ball(0, 1.95f, 4.1f, 1.12f, 1.12f, 0.28f, Chrome, 12);
            b.Ball(0, 1.95f, -2.55f, 1.1f, 1.1f, 0.22f, Chrome, 12);
            foreach (float z in new[] { -1.2f, 0.8f, 2.8f }) b.Barrel(z - 0.05f, z + 0.05f, 0, 0.78f, 1.17f, 1.17f, Dark, 14).Ghost();
            b.Box(-0.4f, 3.08f, -2.0f, 0.4f, 3.14f, 3.6f, Metal).Ghost();
            for (int i = 0; i < 4; i++) b.Cyl(0, 3.0f, -1.5f + i * 1.5f, 0.22f, 0.15f, Metal, 8).Ghost();
            foreach (float s in new[] { -1f, 1f })
                using (b.Turn(new Vector3(s * 1.1f, 1.9f, 1.0f), 0, 0, 0))
                    b.Box(s * 1.14f - 0.01f, 1.7f, 0.8f, s * 1.14f + 0.01f, 2.1f, 1.2f, Hazard).Ghost();
            b.Box(-0.25f, 1.6f, 4.39f, 0.25f, 2.0f, 4.4f, Hazard).Ghost();
            for (int i = 0; i < 6; i++) b.Tube(0.55f, 0.9f + i * 0.35f, 4.3f, 0.75f, 0.9f + i * 0.35f, 4.3f, 0.015f, Metal).Ghost();
        };

        r["city-bus"] = b =>
        {
            foreach (float s in new[] { -1f, 1f }) { Wheel(b, s * 1.02f, 0.5f, -3.7f, 0.5f, 0.3f); Wheel(b, s * 1.0f, 0.5f, 3.4f, 0.5f, 0.36f); }
            b.Box(-1.25f, 0.42f, -5.7f, 1.25f, 1.4f, 5.7f, Body);
            b.Box(-1.24f, 1.4f, -5.45f, 1.24f, 2.62f, 5.62f, Glass).Pane();
            for (int i = 0; i <= 10; i++)
            {
                float z = -5.45f + i * 1.107f;
                foreach (float s in new[] { -1f, 1f }) b.Box(s > 0 ? 1.19f : -1.25f, 1.4f, z - 0.06f, s > 0 ? 1.25f : -1.19f, 2.62f, z + 0.06f, Body);
            }
            b.Box(-1.25f, 2.62f, -5.7f, 1.25f, 3.1f, 5.7f, Body);
            b.Box(-0.8f, 3.1f, -1.0f, 0.8f, 3.3f, 1.2f, Plastic);
            b.Box(-1.2f, 1.0f, -5.72f, 1.2f, 2.55f, -5.7f, Glass).Pane();
            b.Box(-0.95f, 2.66f, -5.72f, 0.95f, 2.92f, -5.7f, Screen).Screen();
            b.Box(-1.25f, 0.35f, -5.75f, 1.25f, 0.62f, -5.65f, Dark);
            b.Box(-1.25f, 1.15f, -5.71f, 1.25f, 1.3f, 5.71f, Trim);
            foreach (float z0 in new[] { -5.35f, -0.4f })
            {
                b.Box(1.21f, 0.5f, z0, 1.245f, 2.55f, z0 + 0.5f, Glass).Pane().Door(1.23f, z0, 80);
                b.Box(1.21f, 0.5f, z0 + 0.5f, 1.245f, 2.55f, z0 + 1.0f, Glass).Pane().Door(1.23f, z0 + 1.0f, -80);
                b.Box(1.21f, 0.45f, z0, 1.25f, 0.5f, z0 + 1.0f, Dark).Ghost();
            }
            for (int i = 0; i < 10; i++) Seats(b, 1.1f, 1.2f, 1.75f, -4.2f + i * 0.9f);
            Lamps(b, 0.95f, 0.75f, -5.7f, 5.7f, 0.3f, 0.18f);
            b.Light(0, 2.8f, -6.0f, Screen);
        };

        r["ambulance"] = b =>
        {
            foreach (float s in new[] { -1f, 1f }) { Wheel(b, s * 0.88f, 0.38f, -1.95f, 0.38f, 0.24f); Wheel(b, s * 0.85f, 0.38f, 1.75f, 0.38f, 0.3f); }
            b.Box(-1.05f, 0.4f, -2.9f, 1.05f, 1.15f, -1.2f, Body);
            b.Box(-1.05f, 0.36f, -2.98f, 1.05f, 0.55f, -2.88f, Dark);
            b.Wedge(-1.03f, 1.15f, -2.9f, 1.03f, -2.15f, 1.18f, 1.25f, Body);
            Greenhouse(b, 1.25f, 1.02f, -2.15f, -1.2f, 2.05f, 0.98f, -1.62f, -1.22f, bPillar: false);
            Doors(b, 1.05f, 0.45f, 1.2f, -2.1f, -1.3f);
            b.Box(-1.1f, 0.45f, -1.2f, 1.1f, 2.7f, 2.88f, Body);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(s * 1.1f - 0.005f, 1.0f, -1.2f, s * 1.1f + 0.005f, 1.25f, 2.88f, Trim).Ghost();
                b.Box(s * 1.1f - 0.008f, 1.6f, 0.35f, s * 1.1f + 0.008f, 2.3f, 0.55f, Trim).Ghost();
                b.Box(s * 1.1f - 0.008f, 1.85f, 0.1f, s * 1.1f + 0.008f, 2.05f, 0.8f, Trim).Ghost();
            }
            foreach (float s in new[] { -1f, 1f }) b.Box(s > 0 ? 0 : -1.0f, 0.5f, 2.88f, s > 0 ? 1.0f : 0, 2.55f, 2.92f, Body).Door(s * 1.0f, 2.9f, -s * 100);
            b.Box(-0.75f, 2.05f, -1.6f, 0.75f, 2.16f, -1.3f, Signal).Lit();
            b.Box(-0.4f, 2.7f, 2.4f, 0.4f, 2.78f, 2.7f, Signal).Lit().Ghost();
            Lamps(b, 0.75f, 0.7f, -2.9f, 2.92f, 0.24f, 0.14f);
            b.Light(0, 2.4f, -1.45f, Signal);
        };

        r["forklift"] = b =>
        {
            foreach (float s in new[] { -1f, 1f }) { Wheel(b, s * 0.5f, 0.27f, -0.6f, 0.27f, 0.2f); Wheel(b, s * 0.48f, 0.22f, 0.95f, 0.22f, 0.18f); }
            b.Box(-0.55f, 0.2f, -0.62f, 0.55f, 0.95f, 0.95f, Hazard);
            b.Box(-0.55f, 0.2f, 0.9f, 0.55f, 1.05f, 1.42f, Dark);
            foreach (float x in new[] { -0.5f, 0.5f })
            {
                b.Beam(new Vector3(x, 0.95f, -0.5f), new Vector3(x, 2.1f, -0.4f), 0.06f, 0.06f, Dark);
                b.Beam(new Vector3(x, 1.05f, 1.0f), new Vector3(x, 2.1f, 0.85f), 0.06f, 0.06f, Dark);
            }
            b.Box(-0.53f, 2.08f, -0.45f, 0.53f, 2.14f, 0.9f, Dark);
            b.Box(-0.25f, 0.95f, 0.35f, 0.25f, 1.05f, 0.75f, Fabric).Soft();
            b.Box(-0.25f, 1.05f, 0.7f, 0.25f, 1.45f, 0.8f, Fabric).Soft();
            b.Tube(0, 0.95f, -0.35f, 0, 1.3f, -0.15f, 0.025f, Dark, 6);
            Ring(b, new Vector3(0, 1.32f, -0.12f), 0.14f, 8, 0.025f, Dark, -55, 0);
            foreach (float x in new[] { -0.32f, 0.32f }) b.Box(x - 0.05f, 0.1f, -0.85f, x + 0.05f, 2.28f, -0.72f, Metal);
            b.Box(-0.4f, 0.5f, -0.9f, 0.4f, 1.1f, -0.85f, Dark);
            foreach (float x in new[] { -0.25f, 0.25f }) b.Box(x - 0.05f, 0.5f, -1.45f, x + 0.05f, 0.55f, -0.85f, Metal);
            // Frozen mid-lift: a pallet still on the forks.
            b.Box(-0.42f, 0.55f, -1.45f, 0.42f, 0.67f, -0.9f, Wood).Loose();
            b.Box(-0.38f, 0.67f, -1.4f, 0.38f, 1.1f, -0.95f, Paper).Soft().Loose();
            b.Cyl(0.35f, 2.14f, 0.6f, 0.05f, 0.08f, Hazard, 6).Ghost();
        };

        r["wheel-loader"] = b =>
        {
            foreach (float s in new[] { -1f, 1f }) { Wheel(b, s * 0.9f, 0.72f, -1.2f, 0.72f, 0.45f); Wheel(b, s * 0.9f, 0.72f, 1.5f, 0.72f, 0.45f); }
            b.Box(-0.65f, 0.55f, -1.9f, 0.65f, 1.35f, 0.35f, Body);
            b.Box(-0.4f, 0.6f, 0.3f, 0.4f, 1.1f, 0.55f, Dark);
            b.Box(-0.85f, 0.6f, 0.5f, 0.85f, 1.75f, 2.95f, Body);
            b.Box(-0.85f, 0.6f, 2.55f, 0.85f, 1.4f, 2.98f, Dark);
            foreach (float s in new[] { -1f, 1f }) b.Box(s * 0.6f, 1.4f, -1.55f, s * 1.18f, 1.48f, -0.8f, Body).Ghost();
            Greenhouse(b, 1.75f, 0.75f, 0.45f, 1.45f, 2.85f, 0.7f, 0.5f, 1.4f, bPillar: false);
            Seats(b, 0.45f, 1.8f, 2.3f, 1.1f);
            b.Cyl(0.5f, 1.75f, 2.4f, 0.07f, 0.85f, Dark, 6);
            foreach (float s in new[] { -1f, 1f }) b.Beam(new Vector3(s * 0.5f, 1.55f, -0.3f), new Vector3(s * 0.5f, 0.75f, -2.25f), 0.2f, 0.16f, Body);
            b.Beam(new Vector3(0, 1.2f, -1.0f), new Vector3(0, 0.95f, -2.2f), 0.14f, 0.14f, Dark);
            b.Wedge(-1.15f, 0.02f, -2.98f, 1.15f, -2.15f, 0.12f, 0.92f, Dark);
            b.Box(-1.18f, 0.0f, -3.0f, 1.18f, 0.05f, -2.85f, Metal).Ghost();
            for (int i = 0; i < 5; i++) b.Ball(-0.8f + i * 0.4f, 0.3f, -2.6f, 0.18f, Stone, 5).Ghost();
        };

        r["jersey-barrier"] = b =>
        {
            b.Box(-0.98f, 0, -0.31f, 0.98f, 0.1f, 0.31f, Stone);
            b.Taper(-0.98f, 0.1f, -0.3f, 0.98f, 0.82f, 0.3f, 1f, 0.38f, Stone);
            for (int i = 0; i < 4; i++) b.Box(-0.9f + i * 0.5f, 0.45f, -0.215f, -0.7f + i * 0.5f, 0.65f, -0.18f, i % 2 == 0 ? Hazard : Alert).Ghost();
            b.Box(-0.98f, 0.79f, -0.12f, 0.98f, 0.82f, 0.12f, Stone).Loose();
        };

        r["power-pole"] = b =>
        {
            b.Cyl(0, 0, 0, 0.15f, 8.8f, Wood, 8, 0.85f);
            b.Box(-0.95f, 8.2f, -0.08f, 0.95f, 8.36f, 0.08f, Wood);
            foreach (float x in new[] { -0.6f, 0.6f }) b.Beam(new Vector3(x, 8.2f, 0), new Vector3(0, 7.7f, 0), 0.05f, 0.05f, Wood).Ghost();
            string[] names = { "a", "b", "c" };
            float[] xs = { -0.8f, 0f, 0.8f };
            for (int i = 0; i < 3; i++)
            {
                b.Cyl(xs[i], 8.36f, i == 1 ? 0.12f : 0, 0.05f, 0.18f, Glass, 6);
                b.Anchor(names[i], xs[i], 8.53f, i == 1 ? 0.12f : 0);
            }
            b.Cyl(0.0f, 6.3f, 0.32f, 0.2f, 0.6f, Dark, 8);
            b.Box(-0.05f, 6.5f, 0.1f, 0.05f, 6.7f, 0.14f, Dark).Ghost();
            b.Cable(new Vector3(0, 6.85f, 0.32f), new Vector3(0, 8.4f, 0.13f), 0.015f, Dark, 0.02f);
            b.Beam(new Vector3(0, 7.1f, -0.1f), new Vector3(0, 7.25f, -0.82f), 0.07f, 0.07f, Metal);
            b.Box(-0.12f, 7.05f, -0.98f, 0.12f, 7.2f, -0.7f, Metal);
            b.Box(-0.1f, 7.02f, -0.95f, 0.1f, 7.05f, -0.73f, Lamp).Lit();
            for (int i = 0; i < 8; i++) b.Tube(-0.18f, 2.5f + i * 0.45f, 0, 0.18f, 2.5f + i * 0.45f, 0, 0.015f, Metal).Ghost();
            b.Light(0, 6.9f, -0.85f, Lamp);
        };

        // ---- high tech ----
        r["hover-skimmer"] = b =>
        {
            foreach (float x in new[] { -0.7f, 0.7f }) foreach (float z in new[] { -1.5f, 1.4f }) b.Box(x - 0.15f, 0, z - 0.2f, x + 0.15f, 0.2f, z + 0.2f, Dark);
            b.Frustum(0.2f, -0.95f, 0.95f, -1.6f, 2.15f, 0.72f, -0.92f, 0.92f, -1.2f, 2.15f, Body);
            b.Hull(new[]
            {
                new Vector3(-0.85f, 0.2f, -2.22f), new Vector3(0.85f, 0.2f, -2.22f), new Vector3(-0.92f, 0.72f, -1.2f), new Vector3(0.92f, 0.72f, -1.2f),
                new Vector3(-0.95f, 0.2f, -1.6f), new Vector3(0.95f, 0.2f, -1.6f), new Vector3(-0.92f, 0.72f, -1.2f), new Vector3(0.92f, 0.72f, -1.2f),
            }, Body);
            b.Dome(0, 0.72f, -0.15f, 0.72f, 0.52f, 1.25f, Glass, 12).Pane();
            Seats(b, 0.5f, 0.72f, 1.05f, -0.1f);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Barrel(1.6f, 2.24f, s * 0.6f, 0.3f, 0.26f, 0.26f, Dark, 10);
                b.Tube(s * 0.6f, 0.56f, 2.2f, s * 0.6f, 0.56f, 2.245f, 0.2f, Lamp, 10).Lit().Ghost();
                b.Box(s * 0.94f - 0.02f, 0.25f, -1.5f, s * 0.94f + 0.02f, 0.3f, 1.9f, Signal).Lit().Ghost();
                b.Wedge(s * 0.9f - 0.03f, 0.7f, 1.3f, s * 0.9f + 0.03f, 2.1f, 0.72f, 1.15f, Trim);
            }
            b.Light(0, 0.25f, 0, Signal);
        };

        r["cargo-hover"] = b =>
        {
            foreach (float x in new[] { -1.1f, 1.1f }) foreach (float z in new[] { -2.8f, 0.2f, 3.1f })
            {
                b.Cyl(x, 0, z, 0.3f, 0.3f, Dark, 10);
                b.Cyl(x, 0.02f, z, 0.32f, 0.04f, Signal, 10).Lit().Ghost();
            }
            b.Box(-1.35f, 0.3f, -3.6f, 1.35f, 0.6f, 3.9f, Dark);
            b.Frustum(0.6f, -1.35f, 1.35f, -3.95f, -1.9f, 2.5f, -1.25f, 1.25f, -3.1f, -1.9f, Body);
            b.Frustum(1.4f, -1.33f, 1.33f, -3.6f, -2.2f, 2.45f, -1.2f, 1.2f, -3.05f, -2.2f, Glass).Pane();
            Seats(b, 0.8f, 1.3f, 1.9f, -2.6f);
            b.Box(-1.4f, 0.6f, -1.8f, 1.4f, 2.9f, 3.9f, Plastic);
            for (int i = 0; i < 8; i++) foreach (float s in new[] { -1f, 1f }) b.Box(s * 1.4f - 0.02f, 0.65f, -1.6f + i * 0.7f, s * 1.4f + 0.02f, 2.85f, -1.5f + i * 0.7f, Dark).Ghost();
            b.Box(-1.41f, 2.3f, -1.8f, 1.41f, 2.45f, 3.9f, Trim).Ghost();
            b.Box(-1.2f, 0.7f, 3.9f, 1.2f, 2.8f, 3.95f, Metal).Loose().Door(new Vector3(0, 0.7f, 3.92f), Vector3.UnitX, 90);
            b.Light(0, 0.1f, 0, Signal);
        };

        r["sky-flyer"] = b =>
        {
            foreach (float s in new[] { -1f, 1f })
            {
                b.Barrel(-1.6f, 1.6f, s * 0.55f, 0, 0.05f, 0.05f, Dark, 6);
                b.Beam(new Vector3(s * 0.55f, 0.1f, -0.9f), new Vector3(s * 0.5f, 0.6f, -0.7f), 0.06f, 0.06f, Dark);
                b.Beam(new Vector3(s * 0.55f, 0.1f, 1.0f), new Vector3(s * 0.5f, 0.6f, 0.9f), 0.06f, 0.06f, Dark);
            }
            b.Box(-0.65f, 0.55f, -1.5f, 0.65f, 1.55f, 1.9f, Body);
            b.Ball(0, 1.05f, -1.5f, 0.65f, 0.5f, 0.95f, Body, 10);
            b.Dome(0, 1.5f, -0.9f, 0.55f, 0.55f, 1.05f, Glass, 12).Pane();
            Seats(b, 0.4f, 1.4f, 1.8f, -0.7f, 0.2f);
            b.Beam(new Vector3(0, 1.2f, 1.9f), new Vector3(0, 1.45f, 2.95f), 0.3f, 0.3f, Body);
            b.Frustum(1.45f, -0.04f, 0.04f, 2.2f, 2.95f, 2.45f, -0.03f, 0.03f, 2.65f, 2.98f, Trim);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(s * 0.65f, 1.0f, -0.1f, s * 1.15f, 1.15f, 0.9f, Body);
                Ring(b, new Vector3(s * 1.5f, 1.1f, 0.4f), 0.38f, 14, 0.1f, Body);
                b.Beam(new Vector3(s * 1.12f, 1.1f, 0.4f), new Vector3(s * 1.88f, 1.1f, 0.4f), 0.05f, 0.03f, Dark).Ghost();
                b.Beam(new Vector3(s * 1.5f, 1.1f, 0.02f), new Vector3(s * 1.5f, 1.1f, 0.78f), 0.05f, 0.03f, Dark).Ghost();
                b.Cyl(s * 1.5f, 1.02f, 0.4f, 0.08f, 0.15f, Dark, 6).Ghost();
            }
            b.Box(-0.3f, 0.6f, 1.88f, 0.3f, 0.9f, 1.92f, Lamp).Lit().Ghost();
        };

        r["agri-drone"] = b =>
        {
            b.Box(-0.3f, 0.35f, -0.4f, 0.3f, 0.65f, 0.4f, Body);
            b.Cyl(0, 0.65f, 0, 0.22f, 0.25f, Glass, 8).Pane();
            b.Cyl(0, 0.66f, 0, 0.19f, 0.12f, Plant, 8).Ghost();
            foreach (float x in new[] { -1f, 1f })
                foreach (float z in new[] { -1f, 1f })
                {
                    b.Beam(new Vector3(x * 0.25f, 0.55f, z * 0.3f), new Vector3(x * 0.68f, 0.6f, z * 0.68f), 0.06f, 0.05f, Dark);
                    Ring(b, new Vector3(x * 0.68f, 0.62f, z * 0.68f), 0.27f, 10, 0.04f, Body);
                    b.Beam(new Vector3(x * 0.68f - 0.22f, 0.65f, z * 0.68f), new Vector3(x * 0.68f + 0.22f, 0.65f, z * 0.68f), 0.06f, 0.012f, Dark).Ghost();
                }
            foreach (float x in new[] { -0.25f, 0.25f })
            {
                b.Beam(new Vector3(x, 0.36f, -0.3f), new Vector3(x * 1.3f, 0.02f, -0.35f), 0.03f, 0.03f, Dark);
                b.Beam(new Vector3(x, 0.36f, 0.3f), new Vector3(x * 1.3f, 0.02f, 0.35f), 0.03f, 0.03f, Dark);
                b.Box(x * 1.3f - 0.02f, 0, -0.45f, x * 1.3f + 0.02f, 0.03f, 0.45f, Dark);
            }
            b.Tube(-0.75f, 0.3f, 0.1f, 0.75f, 0.3f, 0.1f, 0.015f, Metal).Ghost();
            b.Box(0.2f, 0.6f, -0.405f, 0.26f, 0.63f, -0.4f, Signal).Lit().Ghost();
        };

        r["harvester"] = b =>
        {
            foreach (float x in new[] { -1.1f, 1.1f }) foreach (float z in new[] { -1.0f, 2.6f })
            {
                b.Cyl(x, 0, z, 0.35f, 0.35f, Dark, 10);
                b.Cyl(x, 0.02f, z, 0.37f, 0.04f, Signal, 10).Lit().Ghost();
            }
            b.Box(-1.3f, 0.35f, -1.6f, 1.3f, 2.2f, 3.45f, Body);
            b.Box(-1.25f, 2.2f, 0.8f, 1.25f, 3.3f, 3.3f, Body);
            b.Box(-1.27f, 2.9f, 0.85f, 1.27f, 3.0f, 3.25f, Trim).Ghost();
            b.Frustum(2.2f, -0.9f, 0.9f, -1.5f, 0.6f, 3.4f, -0.8f, 0.8f, -1.1f, 0.6f, Glass).Pane();
            b.Box(-0.9f, 3.4f, -1.15f, 0.9f, 3.48f, 0.65f, Body);
            Seats(b, 0.5f, 2.3f, 2.8f, 0.0f);
            b.Box(-1.48f, 0.3f, -3.45f, 1.48f, 0.9f, -1.6f, Dark);
            b.Tube(-1.45f, 1.05f, -2.9f, 1.45f, 1.05f, -2.9f, 0.08f, Metal, 6);
            for (int i = 0; i < 6; i++)
            {
                float a = i * MathF.PI / 3;
                b.Beam(new Vector3(-1.4f, 1.05f + MathF.Sin(a) * 0.45f, -2.9f + MathF.Cos(a) * 0.45f), new Vector3(1.4f, 1.05f + MathF.Sin(a) * 0.45f, -2.9f + MathF.Cos(a) * 0.45f), 0.05f, 0.05f, Trim).Ghost();
            }
            foreach (float s in new[] { -1f, 1f }) b.Box(s * 1.42f, 0.6f, -3.0f, s * 1.48f, 1.55f, -2.8f, Dark);
            b.Beam(new Vector3(1.25f, 2.8f, 2.9f), new Vector3(1.32f, 2.95f, -0.5f), 0.22f, 0.22f, Metal);
            b.Light(0, 0.2f, 1.0f, Signal);
        };
    }
}
