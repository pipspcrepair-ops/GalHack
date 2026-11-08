using System;
using System.Collections.Generic;
using System.Numerics;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>How the rest of a world's stories show: sickness, war, leaving, faith, marks on the doors, and the small things of each.</summary>
public static partial class DecoDressing
{
    /// <summary>A flat ribbon along a polyline (tape, a belt), lying in the face whose normal is <paramref name="n"/>, its stripes alternating every <paramref name="stripe"/> metres.</summary>
    static void Ribbon(DecoBuilder b, List<Vector3> pts, Vector3 n, float w, DecoSlot s1, DecoSlot s2, float stripe = 0.25f)
    {
        float run = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            float len = Vector3.Distance(pts[i], pts[i + 1]);
            if (len < 1e-4f) continue;
            DecoSlot s = ((int)(run / stripe) % 2 == 0) ? s1 : s2;
            Vector3 dn = n;
            if (MathF.Abs(Vector3.Dot(Vector3.Normalize(pts[i + 1] - pts[i]), n)) > 0.95f) dn = Vector3.UnitY;
            Flat(b, pts[i], pts[i + 1], dn, w, 0.003f, s).Soft().Ghost();
            run += len;
        }
    }

    /// <summary>A short post by era for tape, rails and stakes: a stake, a striped post on a foot, a bollard with a lamp top. Returns its top.</summary>
    static Vector3 Stake(DecoBuilder b, DecoTech era, float x, float y0, float z, float h)
    {
        switch (era)
        {
            case DecoTech.Low:
                b.Box(x - 0.035f, y0 - 0.25f, z - 0.035f, x + 0.035f, y0 + h, z + 0.035f, DecoSlot.Wood).As(DecoFinish.Wood);
                break;
            case DecoTech.Mid:
                b.Cyl(x, y0 - 0.01f, z, 0.16f, 0.06f, DecoSlot.Rubber, 10);
                for (int i = 0; i < 4; i++) b.Cyl(x, y0 + 0.05f + i * (h - 0.05f) / 4f, z, 0.025f, (h - 0.05f) / 4f, i % 2 == 0 ? DecoSlot.Alert : DecoSlot.Linen, 8);
                break;
            default:
                b.Cyl(x, y0 - 0.05f, z, 0.06f, h + 0.05f, DecoSlot.Plastic, 10, 0.7f);
                b.Cyl(x, y0 + h, z, 0.045f, 0.06f, DecoSlot.Lamp, 10).Lit();
                break;
        }
        return new Vector3(x, y0 + h, z);
    }

    // ---- sickness ------------------------------------------------------------------------------

    static void Sickness(Dictionary<string, Action<DecoBuilder, DressArgs>> r)
    {
        r["tape"] = (b, a) =>
        {
            // Posts across the way (or along a front), tape between them; torn, it hangs from both and trails.
            float L = a.Length, half = L / 2;
            int spans = Math.Max(1, (int)MathF.Ceiling(L / 3.5f));
            var tops = new List<Vector3>();
            for (int i = 0; i <= spans; i++)
            {
                float x = -half + L * i / spans, y0 = i == 0 ? a.LeftY : i == spans ? a.RightY : a.LeftY + (a.RightY - a.LeftY) * i / spans;
                tops.Add(Stake(b, a.Era, x, y0, 0, 1.05f));
            }
            DecoSlot s1 = a.Style == "medical" ? DecoSlot.Alert : DecoSlot.Hazard, s2 = a.Style == "medical" ? DecoSlot.Linen : DecoSlot.Dark;
            for (int i = 0; i + 1 < tops.Count; i++)
            {
                Vector3 p = tops[i] + new Vector3(0.04f, -0.05f, 0), q = tops[i + 1] + new Vector3(-0.04f, -0.05f, 0);
                if (b.Rng.Chance(a.Wear * 0.5f))
                {
                    Vector3 way = q - p;
                    way.Y = 0;
                    Ribbon(b, DecoCables.Drop(p, 0f, way, MathF.Min(1.5f, way.Length() * 0.4f), 0.003f), Vector3.UnitZ, 0.05f, s1, s2);
                    Ribbon(b, DecoCables.Drop(q, 0f, -way, MathF.Min(1.2f, way.Length() * 0.3f), 0.003f), Vector3.UnitZ, 0.05f, s1, s2);
                    continue;
                }
                Ribbon(b, SpanOf(p, q, 0.02f + a.Wear * 0.03f), Vector3.UnitZ, 0.05f, s1, s2);
            }
        };

        r["barrier"] = (b, a) =>
        {
            float w = R(b, 2f, 2.6f);
            switch (a.Era)
            {
                case DecoTech.Low:
                    // A trestle: two splayed legs each end, a striped board across.
                    foreach (float x in new[] { -w / 2 + 0.15f, w / 2 - 0.15f })
                    {
                        Rod(b, new Vector3(x, 0, -0.3f), new Vector3(x, 0.95f, 0), 0.05f, DecoSlot.Wood);
                        Rod(b, new Vector3(x, 0, 0.3f), new Vector3(x, 0.95f, 0), 0.05f, DecoSlot.Wood);
                    }
                    for (int i = 0; i < 6; i++)
                        b.Box(-w / 2 + w * i / 6f, 0.82f, -0.05f, -w / 2 + w * (i + 1) / 6f, 1.02f, -0.02f, i % 2 == 0 ? DecoSlot.Alert : DecoSlot.Linen);
                    break;
                case DecoTech.Mid:
                    if (b.Rng.Chance(0.5f))
                    {
                        // Water-filled barriers, red and white by turns.
                        for (int i = 0; i < 2; i++)
                        {
                            float x0 = -w / 2 + w * i / 2f + 0.02f, x1 = -w / 2 + w * (i + 1) / 2f - 0.02f;
                            b.Frustum(0, x0, x1, -0.28f, 0.28f, 0.85f, x0 + 0.05f, x1 - 0.05f, -0.1f, 0.1f, i % 2 == 0 ? DecoSlot.Alert : DecoSlot.Linen);
                        }
                    }
                    else
                    {
                        // A crowd barrier: a steel frame of bars on splayed feet.
                        b.Tube(-w / 2, 1.05f, 0, w / 2, 1.05f, 0, 0.02f, DecoSlot.Metal, 6);
                        b.Tube(-w / 2, 0.2f, 0, w / 2, 0.2f, 0, 0.02f, DecoSlot.Metal, 6);
                        for (float x = -w / 2; x <= w / 2 + 0.01f; x += w / 12f) b.Cyl(x, 0.2f, 0, 0.012f, 0.85f, DecoSlot.Metal, 6);
                        foreach (float x in new[] { -w / 2, w / 2 }) { Rod(b, new Vector3(x, 0, -0.4f), new Vector3(x, 0.25f, 0), 0.03f, DecoSlot.Metal); Rod(b, new Vector3(x, 0, 0.4f), new Vector3(x, 0.25f, 0), 0.03f, DecoSlot.Metal); }
                    }
                    break;
                default:
                    // Bollards in a row with a holo-band between them.
                    for (int i = 0; i < 3; i++)
                    {
                        float x = -w / 2 + w * i / 2f;
                        b.Cyl(x, 0, 0, 0.13f, 0.95f, DecoSlot.Plastic, 12, 0.85f);
                        b.Cyl(x, 0.7f, 0, 0.125f, 0.05f, DecoSlot.Lamp, 12).Lit().Ghost();
                    }
                    b.Box(-w / 2, 0.6f, -0.01f, w / 2, 0.75f, 0.01f, DecoSlot.Screen).Lit().Ghost();
                    break;
            }
        };

        r["tent"] = (b, a) =>
        {
            bool medical = a.Style == "medical";
            float w = 2.8f, len = 3.6f;
            switch (a.Era)
            {
                case DecoTech.Low:
                {
                    // A ridge tent along z: two poles, the canvas down both sides, guy ropes to pegs.
                    float h = 2.2f;
                    var front = Post(b, DecoTech.Low, 0, 0, -len / 2, h, 0.7f);
                    var back = Post(b, DecoTech.Low, 0, 0, len / 2, h, 0.7f);
                    foreach (float s in new[] { -1f, 1f })
                        b.Hull(new[] { new Vector3(s * w / 2, 0.02f, -len / 2), new Vector3(s * w / 2 + s * 0.03f, 0.02f, -len / 2), new Vector3(0, h, -len / 2), new Vector3(s * 0.03f, h, -len / 2),
                            new Vector3(s * w / 2, 0.02f, len / 2), new Vector3(s * w / 2 + s * 0.03f, 0.02f, len / 2), new Vector3(0, h, len / 2), new Vector3(s * 0.03f, h, len / 2) }, DecoSlot.Canvas).As(DecoFinish.Fabric);
                    // The back closed, the front flaps tied open.
                    b.Hull(new[] { new Vector3(-w / 2, 0.02f, len / 2), new Vector3(w / 2, 0.02f, len / 2), new Vector3(0, h, len / 2), new Vector3(0, h, len / 2),
                        new Vector3(-w / 2, 0.02f, len / 2 + 0.02f), new Vector3(w / 2, 0.02f, len / 2 + 0.02f), new Vector3(0, h, len / 2 + 0.02f), new Vector3(0, h, len / 2 + 0.02f) }, DecoSlot.Canvas).As(DecoFinish.Fabric);
                    foreach (Vector3 t in new[] { front, back })
                    {
                        Vector3 peg = t with { Y = 0.05f, Z = t.Z + MathF.Sign(t.Z) * 1.3f };
                        b.Box(peg.X - 0.02f, -0.1f, peg.Z - 0.02f, peg.X + 0.02f, 0.08f, peg.Z + 0.02f, DecoSlot.Wood);
                        b.Cable(t - Vector3.UnitY * 0.05f, peg, 0.006f, DecoSlot.Linen, 0.01f);
                    }
                    break;
                }
                case DecoTech.Mid:
                {
                    // A frame tent: walls, a pitched roof, a door left open at the front.
                    float wall = 1.7f, ridge = 2.6f;
                    b.Box(-w / 2, 0, len / 2 - 0.03f, w / 2, wall, len / 2, DecoSlot.Canvas).As(DecoFinish.Fabric);
                    foreach (float s in new[] { -1f, 1f }) b.Box(s * w / 2 - 0.015f, 0, -len / 2, s * w / 2 + 0.015f, wall, len / 2, DecoSlot.Canvas).As(DecoFinish.Fabric);
                    b.Box(-w / 2, 0, -len / 2, -0.5f, wall, -len / 2 + 0.03f, DecoSlot.Canvas).As(DecoFinish.Fabric);
                    b.Box(0.5f, 0, -len / 2, w / 2, wall, -len / 2 + 0.03f, DecoSlot.Canvas).As(DecoFinish.Fabric);
                    b.Box(-0.5f, 2.0f, -len / 2, 0.5f, wall, -len / 2 + 0.03f, DecoSlot.Canvas);
                    foreach (float s in new[] { -1f, 1f })
                        b.Hull(new[] { new Vector3(s * (w / 2 + 0.1f), wall - 0.05f, -len / 2 - 0.1f), new Vector3(s * (w / 2 + 0.12f), wall - 0.05f, -len / 2 - 0.1f), new Vector3(0, ridge, -len / 2 - 0.1f), new Vector3(s * 0.02f, ridge, -len / 2 - 0.1f),
                            new Vector3(s * (w / 2 + 0.1f), wall - 0.05f, len / 2 + 0.1f), new Vector3(s * (w / 2 + 0.12f), wall - 0.05f, len / 2 + 0.1f), new Vector3(0, ridge, len / 2 + 0.1f), new Vector3(s * 0.02f, ridge, len / 2 + 0.1f) }, DecoSlot.Canvas).As(DecoFinish.Fabric);
                    foreach (float zz in new[] { -len / 2 - 0.1f, len / 2 + 0.1f })
                        b.Hull(new[] { new Vector3(-w / 2, wall - 0.05f, zz), new Vector3(w / 2, wall - 0.05f, zz), new Vector3(0, ridge, zz), new Vector3(0, ridge, zz),
                            new Vector3(-w / 2, wall - 0.05f, zz + 0.02f), new Vector3(w / 2, wall - 0.05f, zz + 0.02f), new Vector3(0, ridge, zz + 0.02f), new Vector3(0, ridge, zz + 0.02f) }, DecoSlot.Canvas);
                    if (medical)
                        foreach (float s in new[] { -1f, 1f })
                            using (b.Turn(new Vector3(s * (w / 2 + 0.016f), 1.05f, 0), 0, s * 90))
                            {
                                b.Box(s * (w / 2 + 0.016f) - 0.45f, 0.6f, -0.02f, s * (w / 2 + 0.016f) + 0.45f, 1.5f, -0.01f, DecoSlot.Linen).Ghost();
                                Symbol(b, "cross", s * (w / 2 + 0.016f), 1.05f, -0.02f, 0.7f, DecoSlot.Alert);
                            }
                    break;
                }
                default:
                {
                    // An inflatable shelter: a long dome, ribs over it, a door frame, a glowing panel.
                    b.Dome(0, 0, 0, w / 2 + 0.2f, 2.3f, len / 2 + 0.2f, DecoSlot.Plastic, 16);
                    for (int k = -1; k <= 1; k++)
                    {
                        float zz = k * len / 3, rz = MathF.Sqrt(MathF.Max(0.05f, 1 - (zz * zz) / ((len / 2 + 0.2f) * (len / 2 + 0.2f))));
                        for (int i = 0; i < 8; i++)
                        {
                            float a0 = i * MathF.PI / 8, a1 = (i + 1) * MathF.PI / 8;
                            Rod(b, new Vector3(MathF.Cos(a0) * (w / 2 + 0.24f) * rz, MathF.Sin(a0) * 2.34f * rz, zz), new Vector3(MathF.Cos(a1) * (w / 2 + 0.24f) * rz, MathF.Sin(a1) * 2.34f * rz, zz), 0.1f, DecoSlot.Body);
                        }
                    }
                    b.Box(-0.6f, 0, -len / 2 - 0.25f, 0.6f, 1.9f, -len / 2 + 0.3f, DecoSlot.Body);
                    b.Box(-0.45f, 0, -len / 2 - 0.27f, 0.45f, 1.8f, -len / 2 - 0.24f, DecoSlot.Dark).Ghost();
                    if (medical)
                    {
                        b.Box(-0.4f, 2.05f, -len / 2 - 0.05f, 0.4f, 2.35f, -len / 2 + 0.2f, DecoSlot.Screen).Lit();
                        Symbol(b, "cross", 0, 2.2f, -len / 2 - 0.06f, 0.26f, DecoSlot.Alert);
                    }
                    break;
                }
            }
            if (!medical)
            {
                // A camp's things at the door: a bedroll and a pot.
                b.Tube(-0.9f, 0.12f, -len / 2 - 0.9f, -0.2f, 0.12f, -len / 2 - 0.9f, 0.12f, DecoSlot.Fabric, 10);
                b.Cyl(0.7f, 0, -len / 2 - 0.8f, 0.15f, 0.2f, DecoSlot.Metal, 10);
                return;
            }
            // A field hospital: a flag with the cross, cots in a row at its side, a stretcher.
            Vector3 pole = Post(b, a.Era, -w / 2 - 0.6f, 0, -len / 2, 3.2f, 0.7f);
            Panel(b, pole + new Vector3(0.04f, -0.1f, 0), pole + new Vector3(0.9f, -0.12f, 0), -Vector3.UnitY * 0.6f, Vector3.UnitZ, 0.01f, DecoSlot.Linen);
            Symbol(b, "cross", pole.X + 0.47f, pole.Y - 0.41f, -0.006f, 0.42f, DecoSlot.Alert);
            using (b.Turn(new Vector3(pole.X + 0.47f, pole.Y - 0.41f, 0), 0, 180)) Symbol(b, "cross", pole.X + 0.47f, pole.Y - 0.41f, -0.006f, 0.42f, DecoSlot.Alert);
            for (int i = 0; i < 3; i++)
            {
                float x = w / 2 + 1f, z = -len / 2 + 0.5f + i * 1.1f;
                if (b.Rng.Chance(a.Wear * 0.3f)) continue;
                DecoSlot frame = a.Era == DecoTech.Low ? DecoSlot.Wood : a.Era == DecoTech.Mid ? DecoSlot.Metal : DecoSlot.Plastic;
                b.Legs(x - 0.95f, z - 0.33f, x + 0.95f, z + 0.33f, 0.42f, 0.04f, frame);
                b.Box(x - 0.95f, 0.38f, z - 0.33f, x + 0.95f, 0.44f, z + 0.33f, DecoSlot.Canvas).Soft();
                if (b.Rng.Chance(0.5f)) b.Dome(x + R(b, -0.3f, 0.3f), 0.44f, z, 0.6f, 0.12f, 0.3f, DecoSlot.Linen, 8).Soft();
            }
        };

        r["burn_pit"] = (b, a) =>
        {
            // A ring of turned earth round a bed of ash and charred bundles; a burnt drum at its edge.
            float rr = R(b, 1.6f, 2.2f);
            for (int i = 0; i < 16; i++)
            {
                float an = i * MathF.Tau / 16 + R(b, -0.1f, 0.1f);
                b.Dome(MathF.Cos(an) * rr, 0, MathF.Sin(an) * rr, R(b, 0.45f, 0.7f), R(b, 0.2f, 0.38f), R(b, 0.4f, 0.6f), i % 3 == 0 ? DecoSlot.Stone : DecoSlot.Soil, 8);
            }
            b.Cyl(0, 0, 0, rr - 0.1f, 0.03f, DecoSlot.Ash, 18).Ghost();
            for (int i = 0, n = 5 + b.Rng.Int(5); i < n; i++)
            {
                float x = R(b, -rr * 0.6f, rr * 0.6f), z = R(b, -rr * 0.6f, rr * 0.6f);
                b.Ball(x, 0.08f, z, R(b, 0.15f, 0.4f), R(b, 0.07f, 0.13f), R(b, 0.12f, 0.25f), DecoSlot.Char, 8);
            }
            for (int i = 0; i < 4; i++) Rod(b, new Vector3(R(b, -1f, 1f), 0.05f, R(b, -1f, 1f)), new Vector3(R(b, -1f, 1f), 0.05f, R(b, -1f, 1f)), 0.07f, DecoSlot.Char);
            b.Cyl(rr + 0.6f, 0, 0.3f, 0.29f, 0.88f, DecoSlot.Rust, 12);
            b.Cyl(rr + 0.6f, 0.88f, 0.3f, 0.27f, 0.02f, DecoSlot.Char, 12).Ghost();
        };
    }

    // ---- war -----------------------------------------------------------------------------------

    static void War(Dictionary<string, Action<DecoBuilder, DressArgs>> r)
    {
        r["sandbags"] = (b, a) =>
        {
            // A curved wall of sandbags, laid in staggered courses; a high world's blast modules behind.
            float len = R(b, 3.2f, 4.6f), bend = R(b, -0.4f, 0.6f);
            int courses = a.Era == DecoTech.High ? 3 : 4 + b.Rng.Int(2);
            Vector3 At(float t) => new(t * len - len / 2, 0, bend * (1 - 4 * (t - 0.5f) * (t - 0.5f)));
            for (int c = 0; c < courses; c++)
            {
                int n = (int)(len / 0.66f);
                for (int i = 0; i < n; i++)
                {
                    float t = (i + 0.5f + (c % 2) * 0.5f) / (n + 0.5f);
                    if (c == courses - 1 && b.Rng.Chance(0.15f + a.Wear * 0.2f)) continue;
                    Vector3 p = At(t), q = At(MathF.Min(1, t + 0.02f));
                    float yaw = MathF.Atan2(q.Z - p.Z, q.X - p.X) * -180f / MathF.PI;
                    using (b.Turn(new Vector3(p.X, 0, p.Z), 0, yaw + R(b, -6, 6)))
                        b.Ball(p.X, 0.12f + c * 0.22f, p.Z, 0.35f, 0.13f, 0.21f, i % 4 == 0 ? DecoSlot.Soil : DecoSlot.Canvas, 8).As(DecoFinish.Fabric);
                }
            }
            if (a.Era == DecoTech.High)
                for (int i = 0; i < 2; i++)
                {
                    float x = -len / 2 + 0.6f + i * (len - 1.2f);
                    b.Frustum(0, x - 0.55f, x + 0.55f, 0.35f, 0.95f, 1.4f, x - 0.45f, x + 0.45f, 0.5f, 0.8f, DecoSlot.Plastic);
                    b.Box(x - 0.56f, 1.0f, 0.34f, x + 0.56f, 1.1f, 0.36f, DecoSlot.Hazard).Ghost();
                }
            else if (a.Era == DecoTech.Low)
                for (int i = 0; i < 2; i++)
                {
                    float x = -len / 2 - 0.4f + i * (len + 0.8f);
                    b.Cyl(x, 0, 0.2f, 0.4f, 1.0f, DecoSlot.Wood, 10).As(DecoFinish.Wood);
                    b.Dome(x, 1.0f, 0.2f, 0.38f, 0.12f, 0.38f, DecoSlot.Soil, 8);
                }
        };

        r["barricade"] = (b, a) =>
        {
            // Whatever came to hand, heaped across one lane: the other kept clear.
            float w = R(b, 4f, 5f);
            int n = 6 + b.Rng.Int(4);
            var height = new float[10];
            for (int i = 0; i < n; i++)
            {
                int cell = b.Rng.Int(10);
                float x = -w / 2 + (cell + 0.5f) * w / 10f, z = R(b, -0.5f, 0.5f), y = height[cell];
                float h;
                switch ((b.Rng.Int(5) + (int)a.Era * 2) % 7)
                {
                    case 0:
                        h = 0.8f;
                        b.Cyl(x, y, z, 0.3f, h, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Rust, 12);
                        break;
                    case 1:
                        h = 0.55f;
                        using (b.Turn(new Vector3(x, y, z), 0, R(b, -30, 30))) b.Box(x - 0.45f, y, z - 0.3f, x + 0.45f, y + h, z + 0.3f, a.Era == DecoTech.High ? DecoSlot.Plastic : DecoSlot.Wood);
                        break;
                    case 2:
                        // Planks crossed over the heap.
                        h = 0.3f;
                        Rod(b, new Vector3(x - 1f, y + 0.05f, z - 0.3f), new Vector3(x + 1f, y + 0.9f, z + 0.2f), 0.09f, DecoSlot.Wood);
                        Rod(b, new Vector3(x + 1f, y + 0.05f, z - 0.2f), new Vector3(x - 0.8f, y + 0.8f, z + 0.3f), 0.09f, DecoSlot.Wood);
                        break;
                    case 3:
                        // Tyres in a stack.
                        h = 0.66f;
                        for (int k = 0; k < 3; k++) b.Cyl(x, y + k * 0.22f, z, 0.36f, 0.22f, DecoSlot.Rubber, 12);
                        break;
                    case 4:
                        h = 0.6f;
                        b.Box(x - 0.5f, y, z - 0.3f, x + 0.5f, y + h, z + 0.3f, DecoSlot.Stone);
                        break;
                    case 5:
                        // A door or a panel stood on its edge.
                        h = 0.2f;
                        using (b.Turn(new Vector3(x, y, z), R(b, -25, -10), R(b, -20, 20))) b.Box(x - 0.45f, y, z - 0.03f, x + 0.45f, y + 1.9f, z + 0.03f, a.Era == DecoTech.High ? DecoSlot.Plastic : DecoSlot.Body);
                        break;
                    default:
                        // A composite panel, its dead light strip along it.
                        h = 0.5f;
                        b.Box(x - 0.6f, y, z - 0.25f, x + 0.6f, y + h, z + 0.25f, DecoSlot.Plastic);
                        b.Box(x - 0.6f, y + 0.2f, z - 0.26f, x + 0.6f, y + 0.25f, z - 0.25f, DecoSlot.Lamp).Lit().Ghost();
                        break;
                }
                height[cell] = MathF.Min(1.6f, y + h);
            }
            if (a.Era == DecoTech.Low)
            {
                // An overturned cart at one end.
                using (b.Turn(new Vector3(w / 2, 0, 0), 0, 0, 90))
                {
                    b.Box(w / 2, 0, -0.6f, w / 2 + 0.9f, 1.4f, 0.6f, DecoSlot.Wood).As(DecoFinish.Wood);
                }
                b.Tube(w / 2 - 0.2f, 0.55f, -0.7f, w / 2 - 0.1f, 0.55f, -0.7f, 0.55f, DecoSlot.Wood, 12);
            }
        };

        r["hedgehogs"] = (b, a) =>
        {
            int n = 2 + b.Rng.Int(3);
            for (int k = 0; k < n; k++)
            {
                float x = (k - (n - 1) / 2f) * 1.7f, z = R(b, -0.3f, 0.3f);
                switch (a.Era)
                {
                    case DecoTech.Low:
                        // A log with sharpened stakes through it.
                        b.Tube(x - 0.75f, 0.45f, z, x + 0.75f, 0.45f, z, 0.12f, DecoSlot.Wood, 8);
                        for (int i = 0; i < 3; i++)
                        {
                            float sx = x - 0.5f + i * 0.5f;
                            Rod(b, new Vector3(sx, 0, z - 0.6f), new Vector3(sx, 0.95f, z + 0.6f), 0.06f, DecoSlot.Wood);
                            Rod(b, new Vector3(sx, 0, z + 0.6f), new Vector3(sx, 0.95f, z - 0.6f), 0.06f, DecoSlot.Wood);
                        }
                        break;
                    case DecoTech.Mid:
                        // A steel hedgehog: three beams through one middle, each with a foot on the ground.
                        for (int i = 0; i < 3; i++)
                        {
                            float an = i * MathF.Tau / 3 + R(b, -0.2f, 0.2f);
                            b.Beam(new Vector3(x + MathF.Cos(an) * 0.8f, 0, z + MathF.Sin(an) * 0.8f), new Vector3(x - MathF.Cos(an) * 0.8f, 1.25f, z - MathF.Sin(an) * 0.8f), 0.1f, 0.1f, a.Wear > 0.4f ? DecoSlot.Rust : DecoSlot.Metal);
                        }
                        break;
                    default:
                        // A tetrapod: four tapered legs from a middle.
                        Vector3 mid = new(x, 0.2f, z);
                        Vector3[] legs = { new(0, 1, 0), new(0.94f, -0.33f, 0), new(-0.47f, -0.33f, 0.82f), new(-0.47f, -0.33f, -0.82f) };
                        foreach (Vector3 l in legs) b.Tube(mid, mid + l * 0.65f, 0.16f, DecoSlot.Plastic, 8, 0.6f);
                        break;
                }
            }
        };

        r["wire"] = (b, a) =>
        {
            // A fence line along x with breaks in it: stakes, wire and coils, or emitter posts.
            float len = R(b, 8f, 12f);
            int posts = (int)(len / 2.5f) + 1;
            var tops = new List<Vector3>();
            for (int i = 0; i < posts; i++)
            {
                float x = -len / 2 + len * i / (posts - 1), lean = R(b, -8, 8);
                switch (a.Era)
                {
                    case DecoTech.Low:
                        // A stake fence: sharpened poles leaning out.
                        for (int k = 0; k < 3; k++)
                            using (b.Turn(new Vector3(x + k * 0.6f, 0, 0), -25 + lean, 0))
                                b.Cyl(x + k * 0.6f, -0.2f, 0, 0.06f, 1.7f, DecoSlot.Wood, 6, 0.1f);
                        break;
                    case DecoTech.Mid:
                        using (b.Turn(new Vector3(x, 0, 0), lean, 0)) b.Box(x - 0.03f, -0.3f, -0.03f, x + 0.03f, 1.3f, 0.03f, DecoSlot.Rust);
                        tops.Add(new Vector3(x, 1.2f, 0));
                        break;
                    default:
                        b.Cyl(x, -0.1f, 0, 0.05f, 1.6f, DecoSlot.Plastic, 8);
                        b.Cyl(x, 1.5f, 0, 0.06f, 0.08f, DecoSlot.Lamp, 8).Lit();
                        tops.Add(new Vector3(x, 1.4f, 0));
                        break;
                }
            }
            for (int i = 0; i + 1 < tops.Count; i++)
                for (int s = 0; s < 3; s++)
                {
                    Vector3 p = tops[i] - Vector3.UnitY * (s * 0.42f) + new Vector3(0.04f, 0, 0), q = tops[i + 1] - Vector3.UnitY * (s * 0.42f) - new Vector3(0.04f, 0, 0);
                    if (a.Era == DecoTech.High) { if (s < 2) Rod(b, p, q, 0.025f, DecoSlot.Plastic); continue; }
                    if (b.Rng.Chance(a.Wear * 0.35f)) b.Hang(p, q - p, 0.006f, DecoSlot.Rust, 0.8f);
                    else b.Cable(p, q, 0.006f, DecoSlot.Rust, 0.01f + a.Wear * 0.02f);
                }
            if (a.Era == DecoTech.Mid)
            {
                // A coil along the foot of it.
                for (float x = -len / 2; x < len / 2 - 0.3f; x += 0.3f)
                {
                    if (b.Rng.Chance(0.1f)) { x += 1f; continue; }
                    for (int i = 0; i < 10; i++)
                    {
                        float a0 = i * MathF.Tau / 10, a1 = (i + 1) * MathF.Tau / 10;
                        Rod(b, new Vector3(x + MathF.Sin(a0) * 0.06f, 0.42f + MathF.Sin(a0) * 0.4f, -0.6f + MathF.Cos(a0) * 0.4f), new Vector3(x + MathF.Sin(a1) * 0.06f, 0.42f + MathF.Sin(a1) * 0.4f, -0.6f + MathF.Cos(a1) * 0.4f), 0.012f, DecoSlot.Rust).Ghost();
                    }
                }
            }
        };

        r["two_flags"] = (b, a) =>
        {
            // Two poles, two different flags: whose ground this was, both times.
            string[] parts = a.Style.Split(':');
            string e1 = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : Emblem(b), e2 = parts.Length > 1 ? parts[1] : Emblem(b);
            for (int k = 0; k < 2; k++)
            {
                float x = k == 0 ? -1.5f : 1.5f;
                Vector3 top = Post(b, a.Era, x, 0, 0, a.Era == DecoTech.Low ? 4.6f : 5.4f, 1.1f);
                DecoSlot field = k == 0 ? DecoSlot.Trim : DecoSlot.Signal, mark = k == 0 ? DecoSlot.Linen : DecoSlot.Dark;
                if (b.Rng.Chance(a.Wear * 0.3f))
                {
                    // Shot through: what's left of it hangs down the pole.
                    Panel(b, top + new Vector3(0.05f, -0.1f, 0), top + new Vector3(0.45f, -0.15f, 0), -Vector3.UnitY * 0.9f, Vector3.UnitZ, 0.01f, field);
                    continue;
                }
                // Out from the pole in three panels, waving a little and drooping.
                float fw = 1.25f, fh = 0.8f, dir = k == 0 ? 1f : -1f;
                Vector3 p0 = top + new Vector3(dir * 0.05f, -0.08f, 0);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 c0 = p0 + new Vector3(dir * fw * i / 3f, -0.06f * i, (i % 2) * 0.08f);
                    Vector3 c1 = p0 + new Vector3(dir * fw * (i + 1) / 3f, -0.06f * (i + 1), ((i + 1) % 2) * 0.08f);
                    Panel(b, c0, c1, -Vector3.UnitY * fh, Vector3.UnitZ, 0.01f, field);
                }
                Vector3 mid = p0 + new Vector3(dir * fw * 0.5f, -0.09f - fh * 0.5f, 0.04f);
                Symbol(b, k == 0 ? e1 : e2, mid.X, mid.Y, mid.Z - 0.01f, 0.5f, mark);
                using (b.Turn(new Vector3(mid.X, mid.Y, mid.Z), 0, 180)) Symbol(b, k == 0 ? e1 : e2, mid.X, mid.Y, mid.Z - 0.01f, 0.5f, mark);
            }
        };

        r["burnt_car"] = (b, a) =>
        {
            // A vehicle burnt out where it stopped, along +z: a cart, a car on its rims, a hover car down on its pads.
            switch (a.Era)
            {
                case DecoTech.Low:
                    b.Box(-0.8f, 0.45f, -1.3f, 0.8f, 0.6f, 1.3f, DecoSlot.Char);
                    foreach (float s in new[] { -0.8f, 0.8f }) b.Box(s - 0.04f, 0.6f, -1.3f, s + 0.04f, 0.9f, 1.3f, b.Rng.Chance(0.5f) ? DecoSlot.Char : DecoSlot.Wood);
                    for (int i = 0; i < 12; i++)
                    {
                        float a0 = i * MathF.Tau / 12, a1 = (i + 1) * MathF.Tau / 12;
                        Rod(b, new Vector3(0.95f, 0.5f + MathF.Sin(a0) * 0.5f, MathF.Cos(a0) * 0.5f), new Vector3(0.95f, 0.5f + MathF.Sin(a1) * 0.5f, MathF.Cos(a1) * 0.5f), 0.05f, DecoSlot.Rust);
                    }
                    // The other wheel gone: that corner down on the ground.
                    b.Box(-0.95f, 0, -0.2f, -0.75f, 0.45f, 0.2f, DecoSlot.Char);
                    Rod(b, new Vector3(-0.4f, 0.5f, -1.3f), new Vector3(-0.5f, 0.02f, -2.8f), 0.07f, DecoSlot.Char);
                    Rod(b, new Vector3(0.4f, 0.5f, -1.3f), new Vector3(0.5f, 0.02f, -2.8f), 0.07f, DecoSlot.Char);
                    break;
                case DecoTech.Mid:
                {
                    float len = R(b, 4f, 4.6f);
                    foreach (float z in new[] { -len / 2 + 0.8f, len / 2 - 0.8f })
                        foreach (float x in new[] { -0.75f, 0.75f }) b.Tube(x - 0.1f, 0.24f, z, x + 0.1f, 0.24f, z, 0.24f, DecoSlot.Rust, 10);
                    b.Box(-0.85f, 0.3f, -len / 2, 0.85f, 0.95f, len / 2, DecoSlot.Char);
                    b.Box(-0.86f, 0.5f, -len / 2 + 0.2f, 0.86f, 0.7f, len / 2 - 0.2f, DecoSlot.Body).Ghost();
                    b.Frustum(0.95f, -0.8f, 0.8f, -0.7f, 1.2f, 1.45f, -0.7f, 0.7f, -0.4f, 0.8f, DecoSlot.Char);
                    foreach (float s in new[] { -1f, 1f }) b.Box(s * 0.76f - 0.03f, 1.0f, -0.55f, s * 0.76f + 0.03f, 1.38f, 1.05f, DecoSlot.Dark).Ghost();
                    b.Box(-0.6f, 1.0f, -0.72f, 0.6f, 1.38f, -0.66f, DecoSlot.Dark).Ghost();
                    // A door hanging open.
                    using (b.Turn(new Vector3(0.85f, 0, 0.9f), 0, -50)) b.Box(0.85f, 0.4f, -0.2f, 0.9f, 1.3f, 0.9f, DecoSlot.Char);
                    b.Box(-0.5f, 0.96f, len / 2 - 0.9f, 0.5f, 1.0f, len / 2 - 0.1f, DecoSlot.Ash).Ghost();
                    break;
                }
                default:
                {
                    float len = R(b, 4f, 4.8f);
                    foreach (float z in new[] { -len / 2 + 0.8f, len / 2 - 0.8f })
                        foreach (float x in new[] { -0.65f, 0.65f }) b.Cyl(x, 0, z, 0.32f, 0.22f, DecoSlot.Dark, 10, 0.8f);
                    b.Barrel(-len / 2, len / 2, 0, 0.18f, 0.95f, 0.45f, DecoSlot.Char, 16);
                    b.Dome(0, 0.95f, 0.2f, 0.75f, 0.45f, 1.3f, DecoSlot.Char, 12);
                    b.Box(-0.97f, 0.55f, -len / 2 + 0.6f, 0.97f, 0.6f, len / 2 - 0.6f, DecoSlot.Plastic).Ghost();
                    break;
                }
            }
            // What burned out of it, on the ground.
            for (int i = 0; i < 4; i++) b.Ball(R(b, -1.4f, 1.4f), 0.03f, R(b, -2.4f, 2.4f), R(b, 0.08f, 0.2f), 0.04f, R(b, 0.08f, 0.2f), DecoSlot.Ash, 6).Ghost();
        };

        r["shell_hole"] = (b, a) =>
        {
            // A crater: its thrown-up lip, the scorched bowl, clods and stones round it.
            float rr = R(b, 1.6f, 2.6f);
            for (int i = 0; i < 18; i++)
            {
                float an = i * MathF.Tau / 18 + R(b, -0.1f, 0.1f), d = rr * R(b, 0.9f, 1.1f);
                b.Dome(MathF.Cos(an) * d, -0.05f, MathF.Sin(an) * d, R(b, 0.5f, 0.8f), R(b, 0.18f, 0.35f), R(b, 0.35f, 0.55f), DecoSlot.Soil, 8);
            }
            b.Cyl(0, 0, 0, rr * 0.85f, 0.015f, DecoSlot.Char, 18).Ghost();
            b.Cyl(0, 0.015f, 0, rr * 0.5f, 0.01f, DecoSlot.Ash, 14).Ghost();
            for (int i = 0, n = 6 + b.Rng.Int(8); i < n; i++)
            {
                float an = R(b, 0, MathF.Tau), d = R(b, rr * 1.2f, rr * 2.2f), s = R(b, 0.08f, 0.25f);
                b.Ball(MathF.Cos(an) * d, s * 0.4f, MathF.Sin(an) * d, s, s * 0.6f, s * 0.8f, i % 2 == 0 ? DecoSlot.Soil : DecoSlot.Stone, 6).Ghost();
            }
        };
    }

    // ---- leaving -------------------------------------------------------------------------------

    static void Leaving(Dictionary<string, Action<DecoBuilder, DressArgs>> r)
    {
        r["luggage"] = (b, a) =>
        {
            // A heap of what people carried and left: a cart or trolley now and then, cases piled by it.
            bool cart = b.Rng.Chance(0.4f);
            if (cart)
                switch (a.Era)
                {
                    case DecoTech.Low:
                        // A handcart: two big wheels, a bed of boards, its shafts down on the road.
                        foreach (float x in new[] { -0.62f, 0.62f }) b.Tube(x - 0.04f, 0.45f, 0.6f, x + 0.04f, 0.45f, 0.6f, 0.45f, DecoSlot.Wood, 12);
                        b.Box(-0.55f, 0.55f, -0.2f, 0.55f, 0.62f, 1.4f, DecoSlot.Wood).As(DecoFinish.Wood);
                        foreach (float x in new[] { -0.45f, 0.45f }) Rod(b, new Vector3(x, 0.58f, -0.2f), new Vector3(x, 0.03f, -1.3f), 0.05f, DecoSlot.Wood);
                        for (int k = 0; k < 3; k++) b.Ball(-0.25f + k * 0.25f, 0.8f, 0.4f + k * 0.3f, 0.26f, 0.2f, 0.24f, ClothOf(k), 8).Soft();
                        break;
                    case DecoTech.Mid:
                        // A luggage trolley, tipped forward onto its nose.
                        using (b.Turn(new Vector3(0, 0, -0.5f), -18, 0))
                        {
                            b.Box(-0.4f, 0.12f, -0.5f, 0.4f, 0.16f, 0.6f, DecoSlot.Metal);
                            foreach (float x in new[] { -0.38f, 0.38f }) Rod(b, new Vector3(x, 0.16f, 0.6f), new Vector3(x, 1.05f, 0.7f), 0.03f, DecoSlot.Metal);
                            b.Box(-0.42f, 0.2f, -0.35f, 0.42f, 0.72f, 0.4f, ClothOf(2));
                        }
                        foreach (float x in new[] { -0.35f, 0.35f }) b.Cyl(x, 0, 0.45f, 0.1f, 0.06f, DecoSlot.Rubber, 10);
                        break;
                    default:
                        // A hover sled down on its skids, cases strapped on it.
                        foreach (float x in new[] { -0.4f, 0.4f }) b.Box(x - 0.05f, 0, -0.8f, x + 0.05f, 0.12f, 0.8f, DecoSlot.Dark);
                        b.Box(-0.5f, 0.12f, -0.85f, 0.5f, 0.2f, 0.85f, DecoSlot.Plastic);
                        b.Box(-0.51f, 0.14f, -0.86f, 0.51f, 0.17f, 0.86f, DecoSlot.Lamp).Lit().Ghost();
                        b.Frustum(0.2f, -0.4f, 0.4f, -0.6f, 0.5f, 0.75f, -0.35f, 0.35f, -0.55f, 0.45f, DecoSlot.Body);
                        break;
                }
            int n = 4 + b.Rng.Int(5);
            for (int i = 0; i < n; i++)
            {
                // Clear of the cart, where there is one: spilled in front of it.
                float x = R(b, -1.3f, 1.3f), z = cart ? R(b, -2.3f, -1.4f) : R(b, -1.3f, 1.3f);
                bool flat = b.Rng.Chance(0.5f);
                using (b.Turn(new Vector3(x, 0, z), 0, R(b, 0, 360)))
                    switch (a.Era)
                    {
                        case DecoTech.Low:
                            switch (b.Rng.Int(3))
                            {
                                case 0:
                                    // A trunk, banded, its lid up now and then.
                                    b.Box(x - 0.45f, 0, z - 0.28f, x + 0.45f, 0.42f, z + 0.28f, DecoSlot.Wood).As(DecoFinish.Wood);
                                    foreach (float s in new[] { -0.25f, 0.25f }) b.Box(x + s - 0.03f, -0.002f, z - 0.29f, x + s + 0.03f, 0.43f, z + 0.29f, DecoSlot.Dark).Ghost();
                                    break;
                                case 1:
                                    b.Barrel(z - 0.3f, z + 0.3f, x, 0, 0.2f, 0.16f, ClothOf(i), 10).As(DecoFinish.Fabric);
                                    b.Tube(x - 0.1f, 0.35f, z, x + 0.1f, 0.35f, z, 0.015f, DecoSlot.Dark).Ghost();
                                    break;
                                default:
                                    // A bundle tied in a cloth.
                                    b.Ball(x, 0.2f, z, 0.26f, 0.2f, 0.24f, ClothOf(i + 1), 8).Soft();
                                    b.Ball(x, 0.42f, z, 0.08f, 0.06f, 0.08f, ClothOf(i + 1), 6).Soft().Ghost();
                                    break;
                            }
                            break;
                        case DecoTech.Mid:
                            if (flat)
                            {
                                // A suitcase on its back, burst, clothes spilled.
                                b.Box(x - 0.35f, 0, z - 0.25f, x + 0.35f, 0.2f, z + 0.25f, ClothOf(i + 2));
                                b.Box(x - 0.6f, 0, z - 0.2f, x - 0.25f, 0.02f, z + 0.25f, ClothOf(i + 3)).Soft().Ghost();
                                b.Box(x + 0.3f, 0, z + 0.1f, x + 0.65f, 0.02f, z + 0.5f, DecoSlot.Linen).Soft().Ghost();
                            }
                            else
                            {
                                // A case stood on its end: handle, wheels.
                                b.Box(x - 0.25f, 0.04f, z - 0.12f, x + 0.25f, 0.72f, z + 0.12f, i % 3 == 0 ? DecoSlot.Dark : ClothOf(i + 2));
                                b.Box(x - 0.1f, 0.72f, z - 0.02f, x + 0.1f, 0.78f, z + 0.02f, DecoSlot.Dark).Ghost();
                                foreach (float s in new[] { -0.18f, 0.18f }) b.Cyl(x + s, 0, z + 0.08f, 0.04f, 0.04f, DecoSlot.Rubber, 6).Ghost();
                            }
                            break;
                        default:
                            // A hard-shell case, rounded, its light strip dark.
                            b.Frustum(0, x - 0.32f, x + 0.32f, z - 0.2f, z + 0.2f, 0.62f, x - 0.27f, x + 0.27f, z - 0.15f, z + 0.15f, i % 2 == 0 ? DecoSlot.Plastic : DecoSlot.Body);
                            b.Box(x - 0.33f, 0.3f, z - 0.205f, x + 0.33f, 0.33f, z + 0.205f, DecoSlot.Lamp).Lit().Ghost();
                            break;
                    }
            }
        };

        r["queue_rails"] = (b, a) =>
        {
            // A queue's way, back and forth: rope on posts, belts on stanchions, or light posts alone.
            float len = R(b, 3.2f, 4.2f);
            int lines = 4;
            for (int l = 0; l < lines; l++)
            {
                float z = -1.5f + l * 1f;
                var tops = new List<Vector3>();
                for (int i = 0; i < 3; i++)
                {
                    float x = -len / 2 + len * i / 2f;
                    // Every other line leaves a gap at alternate ends: the way turns there.
                    if ((l == 1 || l == 2) && i == (l == 1 ? 2 : 0)) { tops.Add(Vector3.Zero); continue; }
                    switch (a.Era)
                    {
                        case DecoTech.Low: tops.Add(Stake(b, DecoTech.Low, x, 0, z, 0.95f)); break;
                        case DecoTech.Mid:
                            b.Cyl(x, 0, z, 0.15f, 0.03f, DecoSlot.Chrome, 12);
                            b.Cyl(x, 0.03f, z, 0.025f, 0.92f, DecoSlot.Chrome, 8);
                            tops.Add(new Vector3(x, 0.92f, z));
                            break;
                        default:
                            b.Cyl(x, 0, z, 0.04f, 1f, DecoSlot.Plastic, 8);
                            b.Cyl(x, 1f, z, 0.05f, 0.06f, DecoSlot.Lamp, 8).Lit();
                            tops.Add(new Vector3(x, 0.95f, z));
                            break;
                    }
                }
                for (int i = 0; i + 1 < tops.Count; i++)
                {
                    if (tops[i] == Vector3.Zero || tops[i + 1] == Vector3.Zero) continue;
                    Vector3 p = tops[i] + new Vector3(0.04f, -0.05f, 0), q = tops[i + 1] - new Vector3(0.04f, 0.05f, 0);
                    if (a.Era == DecoTech.Low) b.Cable(p, q, 0.015f, DecoSlot.Fabric, 0.08f);
                    else if (a.Era == DecoTech.Mid)
                    {
                        if (b.Rng.Chance(a.Wear * 0.4f)) Ribbon(b, DecoCables.Drop(p, 0, q - p, 0.6f, 0.003f), Vector3.UnitZ, 0.05f, DecoSlot.Fabric, DecoSlot.Fabric);
                        else Ribbon(b, SpanOf(p, q, 0.03f), Vector3.UnitZ, 0.05f, DecoSlot.Fabric, DecoSlot.Fabric);
                    }
                    else b.Box(p.X, p.Y - 0.01f, p.Z - 0.005f, q.X, p.Y + 0.01f, p.Z + 0.005f, DecoSlot.Screen).Lit().Ghost();
                }
            }
        };

        r["crates"] = (b, a) =>
        {
            int n = 3 + b.Rng.Int(4);
            var spots = new List<(float X, float Z, float Top)>();
            for (int i = 0; i < n; i++)
            {
                float s = R(b, 0.45f, 0.65f);
                float x, z, y = 0;
                if (spots.Count > 0 && b.Rng.Chance(0.35f))
                {
                    var under = spots[b.Rng.Int(spots.Count)];
                    if (under.Top > 1.2f) continue;
                    (x, z, y) = (under.X, under.Z, under.Top);
                    spots.Remove(under);
                }
                else { x = R(b, -1.2f, 1.2f); z = R(b, -0.7f, 0.7f); }
                using (b.Turn(new Vector3(x, y, z), 0, R(b, -20, 20)))
                    switch (a.Era)
                    {
                        case DecoTech.Low:
                            if (b.Rng.Chance(0.35f) && y == 0) { b.Cyl(x, 0, z, 0.3f, 0.85f, DecoSlot.Wood, 12).As(DecoFinish.Wood); foreach (float h in new[] { 0.15f, 0.7f }) b.Cyl(x, h, z, 0.305f, 0.04f, DecoSlot.Dark, 12).Ghost(); s = 0.85f; break; }
                            b.Box(x - s / 2, y, z - s / 2, x + s / 2, y + s, z + s / 2, DecoSlot.Wood).As(DecoFinish.Wood);
                            Rod(b, new Vector3(x - s / 2, y + 0.04f, z - s / 2 - 0.005f), new Vector3(x + s / 2, y + s - 0.04f, z - s / 2 - 0.005f), 0.04f, DecoSlot.Wood).Ghost();
                            break;
                        case DecoTech.Mid:
                            if (b.Rng.Chance(0.4f)) { b.Box(x - s / 2, y, z - s / 2, x + s / 2, y + s * 0.8f, z + s / 2, DecoSlot.Paper).As(DecoFinish.Paper); s *= 0.8f; break; }
                            b.Box(x - s / 2, y, z - s * 0.35f, x + s / 2, y + s * 0.6f, z + s * 0.35f, i % 2 == 0 ? DecoSlot.Body : DecoSlot.Trim).As(DecoFinish.Plastic);
                            s *= 0.6f;
                            break;
                        default:
                            b.Box(x - s / 2, y, z - s * 0.4f, x + s / 2, y + s * 0.55f, z + s * 0.4f, DecoSlot.Plastic);
                            b.Box(x - s / 2 - 0.005f, y + s * 0.4f, z - s * 0.4f - 0.005f, x + s / 2 + 0.005f, y + s * 0.43f, z + s * 0.4f + 0.005f, DecoSlot.Lamp).Lit().Ghost();
                            s *= 0.55f;
                            break;
                    }
                spots.Add((x, z, y + s));
            }
        };
    }

    // ---- faith and remembrance -------------------------------------------------------------------

    static void Faith(Dictionary<string, Action<DecoBuilder, DressArgs>> r)
    {
        r["shrine"] = (b, a) =>
        {
            string emblem = a.Style.Length > 0 ? a.Style : Emblem(b);
            switch (a.Era)
            {
                case DecoTech.Low:
                    // A stone plinth with a cloth over it, a carved board on a post behind.
                    b.Box(-0.5f, 0, -0.3f, 0.5f, 0.7f, 0.3f, DecoSlot.Stone);
                    b.Box(-0.52f, 0.62f, -0.32f, 0.52f, 0.72f, 0.1f, DecoSlot.Fabric).Soft();
                    b.Box(-0.06f, 0, 0.3f, 0.06f, 1.9f, 0.42f, DecoSlot.Wood).As(DecoFinish.Wood);
                    b.Box(-0.38f, 1.2f, 0.26f, 0.38f, 1.85f, 0.3f, DecoSlot.Wood).As(DecoFinish.Wood);
                    Symbol(b, emblem, 0, 1.52f, 0.26f, 0.5f, DecoSlot.Trim);
                    break;
                case DecoTech.Mid:
                    // A box shrine on a post, roofed, the emblem inside it.
                    b.Box(-0.06f, 0, -0.06f, 0.06f, 1.1f, 0.06f, DecoSlot.Wood);
                    b.Box(-0.35f, 1.1f, -0.25f, 0.35f, 1.14f, 0.25f, DecoSlot.Body);
                    b.Box(-0.35f, 1.14f, 0.21f, 0.35f, 1.75f, 0.25f, DecoSlot.Body);
                    foreach (float s in new[] { -1f, 1f }) b.Box(s * 0.35f - 0.02f, 1.14f, -0.25f, s * 0.35f + 0.02f, 1.75f, 0.25f, DecoSlot.Body);
                    b.Wedge(-0.45f, 1.75f, -0.35f, 0.45f, 0.35f, 1.8f, 2.05f, DecoSlot.Trim);
                    Symbol(b, emblem, 0, 1.45f, 0.21f, 0.4f, DecoSlot.Gold);
                    b.Box(-0.6f, 0, -0.5f, 0.6f, 0.05f, -0.2f, DecoSlot.Stone);
                    break;
                default:
                    // An obelisk with a glowing emblem.
                    b.Frustum(0, -0.35f, 0.35f, -0.35f, 0.35f, 2.2f, -0.12f, 0.12f, -0.12f, 0.12f, DecoSlot.Plastic);
                    b.Box(-0.22f, 1.0f, -0.3f, 0.22f, 1.5f, -0.27f, DecoSlot.Screen).Lit();
                    Symbol(b, emblem, 0, 1.25f, -0.305f, 0.36f, DecoSlot.Linen);
                    b.Light(0, 1.25f, -0.6f, DecoSlot.Screen);
                    break;
            }
            // Candles and offerings at its foot.
            float baseY = a.Era == DecoTech.Low ? 0.72f : 0.05f, front = a.Era == DecoTech.Low ? -0.15f : -0.4f;
            for (int i = 0, n = 3 + b.Rng.Int(4); i < n; i++)
            {
                float x = R(b, -0.4f, 0.4f), z = front + R(b, -0.08f, 0.08f), h = R(b, 0.06f, 0.25f) * (1 - a.Wear * 0.6f);
                if (a.Era == DecoTech.High) { b.Cyl(x, baseY, z, 0.02f, h, DecoSlot.Lamp, 6).Lit(); continue; }
                b.Cyl(x, baseY, z, 0.025f, h, DecoSlot.Linen, 6);
                b.Ball(x, baseY + h + 0.02f, z, 0.012f, 0.025f, 0.012f, DecoSlot.Lamp, 4).Lit().Ghost();
            }
            b.Cyl(R(b, -0.3f, 0.3f), baseY, front - 0.12f, 0.08f, 0.04f, DecoSlot.Bronze, 10);
            for (int i = 0; i < 3; i++) b.Ball(R(b, -0.4f, 0.4f), baseY + 0.03f, front - 0.15f, 0.05f, 0.03f, 0.05f, i % 2 == 0 ? DecoSlot.Signal : DecoSlot.Plant, 6).Rots().Ghost();
        };

        r["memorial"] = (b, a) =>
        {
            // Rows of markers, a taller one at their head, wreaths on some.
            string emblem = a.Style.Length > 0 ? a.Style : Emblem(b);
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 4; col++)
                {
                    float x = (col - 1.5f) * 0.9f, z = row * 1.1f;
                    switch (a.Era)
                    {
                        case DecoTech.Low:
                            b.Box(x - 0.04f, -0.2f, z - 0.04f, x + 0.04f, 0.9f, z + 0.04f, DecoSlot.Wood).As(DecoFinish.Wood);
                            b.Box(x - 0.18f, 0.6f, z - 0.05f, x + 0.18f, 0.75f, z - 0.03f, DecoSlot.Wood).As(DecoFinish.Wood);
                            break;
                        case DecoTech.Mid:
                            b.Taper(x - 0.22f, -0.1f, z - 0.06f, x + 0.22f, 0.7f, z + 0.06f, 0.85f, 0.9f, DecoSlot.Stone);
                            break;
                        default:
                            b.Cyl(x, -0.05f, z, 0.07f, 1.05f, DecoSlot.Plastic, 8, 0.7f);
                            b.Cyl(x, 1.0f, z, 0.05f, 0.04f, DecoSlot.Lamp, 8).Lit();
                            break;
                    }
                    if (b.Rng.Chance(0.25f))
                    {
                        // A wreath leaning on its foot.
                        for (int i = 0; i < 8; i++)
                        {
                            float an = i * MathF.Tau / 8;
                            b.Ball(x + MathF.Cos(an) * 0.17f, 0.2f + MathF.Sin(an) * 0.17f, z - 0.12f, 0.06f, DecoSlot.Plant, 5).Rots().Ghost();
                        }
                        b.Ball(x, 0.05f, z - 0.12f, 0.05f, DecoSlot.Fabric, 5).Soft().Ghost();
                    }
                }
            b.Taper(-0.35f, 0, -1.4f, 0.35f, 1.8f, -1.15f, 0.7f, 0.8f, a.Era == DecoTech.High ? DecoSlot.Plastic : DecoSlot.Stone);
            Symbol(b, emblem, 0, 1.3f, -1.385f, 0.4f, a.Era == DecoTech.High ? DecoSlot.Lamp : DecoSlot.Bronze);
            for (int i = 0; i < 3; i++) b.Cyl(-0.3f + i * 0.3f, 0, -1.6f, 0.03f, R(b, 0.08f, 0.2f), DecoSlot.Linen, 6);
        };

        r["bell_post"] = (b, a) =>
        {
            float h = 3f;
            if (a.Era == DecoTech.High)
            {
                // An arch with a chime of tubes hung in it.
                b.Beam(new Vector3(-1f, 0, 0), new Vector3(-0.8f, h, 0), 0.14f, 0.14f, DecoSlot.Plastic);
                b.Beam(new Vector3(1f, 0, 0), new Vector3(0.8f, h, 0), 0.14f, 0.14f, DecoSlot.Plastic);
                b.Box(-0.9f, h - 0.05f, -0.08f, 0.9f, h + 0.1f, 0.08f, DecoSlot.Plastic);
                for (int i = 0; i < 5; i++) b.Tube(-0.5f + i * 0.25f, h - 0.15f, 0, -0.5f + i * 0.25f, h - 0.9f - i * 0.15f, 0, 0.035f, DecoSlot.Chrome, 8);
                b.Box(-0.9f, h + 0.1f, -0.085f, 0.9f, h + 0.13f, 0.085f, DecoSlot.Lamp).Lit().Ghost();
                return;
            }
            DecoSlot frame = a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Metal;
            foreach (float x in new[] { -0.8f, 0.8f }) b.Box(x - 0.08f, -0.3f, -0.08f, x + 0.08f, h, 0.08f, frame);
            b.Box(-1f, h, -0.1f, 1f, h + 0.16f, 0.1f, frame);
            b.Box(-0.12f, h - 0.12f, -0.06f, 0.12f, h, 0.06f, DecoSlot.Dark);
            // The bell, mouth down.
            b.Cyl(0, h - 0.75f, 0, 0.36f, 0.55f, a.Era == DecoTech.Low ? DecoSlot.Bronze : DecoSlot.Metal, 14, 0.6f);
            b.Dome(0, h - 0.2f, 0, 0.22f, 0.1f, 0.22f, a.Era == DecoTech.Low ? DecoSlot.Bronze : DecoSlot.Metal, 12);
            // Its rope down to the ground, unless the clapper's gone and nobody rings it.
            if (a.Style != "silent") b.Hang(new Vector3(0.3f, h - 0.1f, 0), new Vector3(0.3f, 0, -1), 0.012f, DecoSlot.Canvas, 0.6f);
        };

        r["cairn"] = (b, a) =>
        {
            float y = 0, s = R(b, 0.32f, 0.45f);
            int n = 4 + b.Rng.Int(4);
            for (int i = 0; i < n && s > 0.08f; i++)
            {
                float h = s * R(b, 0.5f, 0.7f);
                b.Ball(R(b, -0.04f, 0.04f), y + h, R(b, -0.04f, 0.04f), s, h, s * R(b, 0.8f, 1f), DecoSlot.Stone, 7);
                y += h * 1.8f;
                s *= R(b, 0.7f, 0.85f);
            }
            if (b.Rng.Chance(0.5f))
            {
                // A staff stood in the top with a strip of cloth tied on.
                b.Cyl(0, y - 0.15f, 0, 0.02f, 1f, DecoSlot.Wood, 6);
                Panel(b, new Vector3(0.02f, y + 0.8f, 0), new Vector3(0.1f, y + 0.8f, 0), -Vector3.UnitY * 0.45f, Vector3.UnitZ, 0.004f, ClothOf(b.Rng.Int(4)));
            }
        };
    }

    // ---- marks on a house's front: symbols only ------------------------------------------------------

    static void Marks(Dictionary<string, Action<DecoBuilder, DressArgs>> r)
    {
        r["mark"] = (b, a) =>
        {
            string[] style = a.Style.Split(':');
            string kind = style[0], emblem = style.Length > 1 ? style[1] : Emblem(b);
            const float z = -0.004f;
            void Stroke(float x0, float y0, float x1, float y1, float w, DecoSlot s) => Flat(b, new Vector3(x0, y0, z), new Vector3(x1, y1, z), Vector3.UnitZ, w, 0.006f, s).Ghost();
            switch (kind)
            {
                case "cross":
                {
                    // A painted cross, the paint run down from its ends; a ring round it now and then.
                    float c = 1.4f, s = R(b, 0.5f, 0.62f);
                    Stroke(-s, c - s, s, c + s, 0.1f, DecoSlot.Alert);
                    Stroke(-s, c + s, s, c - s, 0.1f, DecoSlot.Alert);
                    for (int i = 0; i < 4; i++) { float x = (i % 2 == 0 ? -s : s) * R(b, 0.6f, 1f); float y = c - s * R(b, 0.3f, 1f); Stroke(x, y, x, y - R(b, 0.08f, 0.3f), 0.025f, DecoSlot.Alert); }
                    if (b.Rng.Chance(0.4f)) Symbol(b, "ring", 0, c, z + 0.002f, s * 2.6f, DecoSlot.Alert);
                    break;
                }
                case "tally":
                {
                    DecoSlot s = b.Rng.Chance(0.5f) ? DecoSlot.Linen : DecoSlot.Dark;
                    int groups = 2 + b.Rng.Int(5);
                    for (int g = 0; g < groups; g++)
                    {
                        float x0 = -0.55f + (g % 4) * 0.3f, y0 = 1.25f - (g / 4) * 0.35f;
                        for (int k = 0; k < 4; k++) Stroke(x0 + k * 0.05f, y0, x0 + k * 0.05f + R(b, -0.01f, 0.01f), y0 + 0.25f, 0.02f, s);
                        if (g < groups - 1 || b.Rng.Chance(0.5f)) Stroke(x0 - 0.03f, y0 + 0.03f, x0 + 0.2f, y0 + 0.22f, 0.02f, s);
                    }
                    break;
                }
                case "plate":
                {
                    // A stamped plate: rivets, and rows of punched dots and dashes.
                    b.Box(-0.17f, 1.55f, -0.012f, 0.17f, 1.8f, 0, DecoSlot.Metal);
                    foreach (float x in new[] { -0.14f, 0.14f }) foreach (float y in new[] { 1.58f, 1.77f }) b.Tube(x, y, -0.012f, x, y, -0.018f, 0.008f, DecoSlot.Bronze, 6).Ghost();
                    for (int row = 0; row < 2; row++)
                        for (int k = 0; k < 6; k++)
                            if (b.Rng.Chance(0.75f)) b.Box(-0.11f + k * 0.04f, 1.63f + row * 0.07f, -0.016f, -0.11f + k * 0.04f + (b.Rng.Chance(0.5f) ? 0.025f : 0.01f), 1.66f + row * 0.07f, -0.012f, DecoSlot.Dark).Ghost();
                    break;
                }
                case "tag":
                {
                    // Tags on a nail and a loop of string: numbered by notches, not by numbers.
                    for (int k = 0, n = 1 + b.Rng.Int(2); k < n; k++)
                    {
                        float x = -0.1f + k * 0.18f, y = 1.5f - k * 0.1f;
                        b.Box(x - 0.008f, y - 0.008f, -0.03f, x + 0.008f, y + 0.008f, 0, DecoSlot.Dark).Ghost();
                        b.Tube(x, y, -0.02f, x, y - 0.1f, -0.02f, 0.003f, DecoSlot.Linen).Ghost();
                        b.Box(x - 0.04f, y - 0.22f, -0.024f, x + 0.04f, y - 0.1f, -0.016f, DecoSlot.Hazard).Ghost();
                        for (int c = 0; c < 3; c++) b.Box(x - 0.04f, y - 0.2f + c * 0.03f, -0.026f, x - 0.02f, y - 0.19f + c * 0.03f, -0.024f, DecoSlot.Dark).Ghost();
                    }
                    break;
                }
                case "chalk":
                {
                    // Travellers' signs: a circle, an arrow off it, a zigzag under.
                    DecoSlot s = DecoSlot.Linen;
                    Symbol(b, "ring", -0.25f, 1.3f, z, 0.3f, s);
                    Stroke(-0.08f, 1.3f, 0.35f, 1.3f, 0.025f, s);
                    Stroke(0.35f, 1.3f, 0.25f, 1.38f, 0.025f, s);
                    Stroke(0.35f, 1.3f, 0.25f, 1.22f, 0.025f, s);
                    for (int k = 0; k < 4; k++) Stroke(-0.4f + k * 0.18f, 1.0f + (k % 2) * 0.1f, -0.22f + k * 0.18f, 1.0f + ((k + 1) % 2) * 0.1f, 0.025f, s);
                    break;
                }
                case "fist": Symbol(b, "fist", 0, 1.5f, z, 0.9f, DecoSlot.Alert); Stroke(-0.5f, 0.95f, 0.5f, 0.95f, 0.08f, DecoSlot.Alert); break;
                case "names":
                {
                    // A slab of names worn to rows of marks.
                    b.Box(-0.55f, 0.9f, -0.04f, 0.55f, 1.8f, 0, DecoSlot.Stone);
                    for (int row = 0; row < 8; row++)
                        for (float x = -0.45f; x < 0.4f;)
                        {
                            float w = R(b, 0.06f, 0.22f);
                            b.Box(x, 1.0f + row * 0.095f, -0.044f, MathF.Min(0.45f, x + w), 1.03f + row * 0.095f, -0.04f, DecoSlot.Dark).Ghost();
                            x += w + 0.05f;
                        }
                    break;
                }
                case "waterline":
                {
                    float h = R(b, 0.7f, 1.8f);
                    b.Box(-0.9f, h - 0.12f, -0.006f, 0.9f, h + 0.05f, -0.002f, DecoSlot.Grime).Ghost();
                    b.Box(-0.9f, 0.05f, -0.004f, 0.9f, h - 0.12f, -0.001f, DecoSlot.Soil).Ghost();
                    for (int k = 0; k < 3; k++) { float y = h + 0.25f + k * 0.3f; Stroke(0.55f, y, 0.85f, y, 0.025f, DecoSlot.Dark); }
                    break;
                }
                case "glyph":
                    Symbol(b, b.Rng.Chance(0.5f) ? "spiral" : "eye", 0, 1.45f, z, 0.75f, DecoSlot.Signal);
                    for (int k = 0; k < 5; k++) b.Tube(-0.45f + k * 0.22f, 0.95f, z, -0.45f + k * 0.22f, 0.95f, z - 0.01f, 0.03f, DecoSlot.Signal, 8).Ghost();
                    break;
                case "mural":
                {
                    // A picture painted over a wall: a sky, a sun, hills, stripes of a field.
                    float w = R(b, 1.6f, 2.2f), y0 = 0.35f, h = R(b, 1.4f, 1.9f);
                    b.Box(-w / 2, y0, -0.005f, w / 2, y0 + h, -0.001f, DecoSlot.Signal).Ghost();
                    Symbol(b, "sun", w * R(b, -0.25f, 0.25f), y0 + h * 0.72f, -0.006f, h * 0.35f, DecoSlot.Trim);
                    for (int k = 0; k < 3; k++)
                    {
                        float cx = -w / 2 + w * (k + 0.5f) / 3f, hw = w * 0.28f, top = y0 + h * R(b, 0.35f, 0.55f);
                        b.Hull(new[] { new Vector3(cx - hw, y0, -0.009f), new Vector3(cx + hw, y0, -0.009f), new Vector3(cx, top, -0.009f), new Vector3(cx, top, -0.009f),
                            new Vector3(cx - hw, y0, -0.006f), new Vector3(cx + hw, y0, -0.006f), new Vector3(cx, top, -0.006f), new Vector3(cx, top, -0.006f) }, k % 2 == 0 ? DecoSlot.Fabric : DecoSlot.Body).Ghost();
                    }
                    for (int k = 0; k < 3; k++) b.Box(-w / 2, y0 + k * 0.08f, -0.011f, w / 2, y0 + k * 0.08f + 0.04f, -0.009f, k % 2 == 0 ? DecoSlot.Trim : DecoSlot.Linen).Ghost();
                    break;
                }
                case "zone":
                {
                    DecoSlot[] zone = { DecoSlot.Hazard, DecoSlot.Alert, DecoSlot.Signal, DecoSlot.Trim };
                    DecoSlot s = zone[(int)(DecoHash.Mix(a.Seed >> 7) % (ulong)zone.Length)];
                    b.Box(-0.7f, 1.0f, -0.005f, 0.7f, 1.22f, -0.001f, s).Ghost();
                    Symbol(b, "disc", 0, 1.7f, z, 0.45f, s);
                    break;
                }
                case "arrow":
                    using (b.Turn(new Vector3(0, 1.35f, 0), 0, 0, b.Rng.Chance(0.5f) ? 0 : 180)) Symbol(b, "arrow", 0, 1.35f, z, 0.8f, DecoSlot.Trim);
                    break;
                case "height":
                    // Children's heights up a door frame, each with its dot.
                    for (int k = 0, n = 3 + b.Rng.Int(5); k < n; k++)
                    {
                        float y = 0.75f + k * R(b, 0.06f, 0.12f);
                        Stroke(-0.12f, y, 0.06f, y, 0.015f, DecoSlot.Dark);
                        b.Tube(0.12f, y, z, 0.12f, y, z - 0.004f, 0.012f, DecoSlot.Dark, 6).Ghost();
                    }
                    break;
                case "notice":
                    // A notice pinned at eye height: blank but for the stamp.
                    b.Box(-0.21f, 1.35f, -0.006f, 0.21f, 1.9f, -0.002f, DecoSlot.Paper).Ghost().As(DecoFinish.Paper);
                    foreach (float x in new[] { -0.17f, 0.17f }) foreach (float y in new[] { 1.39f, 1.86f }) b.Tube(x, y, -0.006f, x, y, -0.012f, 0.008f, DecoSlot.Dark, 6).Ghost();
                    Symbol(b, "ring", 0.06f, 1.5f, -0.007f, 0.17f, DecoSlot.Alert);
                    for (int k = 0; k < 4; k++) b.Box(-0.15f, 1.62f + k * 0.06f, -0.008f, -0.15f + R(b, 0.12f, 0.28f), 1.635f + k * 0.06f, -0.006f, DecoSlot.Dark).Ghost();
                    break;
                case "gang":
                    Symbol(b, "x", 0, 1.45f, z, 1f, DecoSlot.Alert);
                    for (int k = 0; k < 8; k++) { float an = k * MathF.Tau / 8; Stroke(MathF.Cos(an) * 0.45f, 1.45f + MathF.Sin(an) * 0.45f, MathF.Cos(an) * 0.65f, 1.45f + MathF.Sin(an) * 0.65f, 0.04f, DecoSlot.Dark); }
                    break;
                default:
                    // A sigil: the world's emblem in a ring.
                    Symbol(b, "ring", 0, 1.5f, z, 0.95f, DecoSlot.Trim);
                    Symbol(b, emblem, 0, 1.5f, z - 0.003f, 0.6f, DecoSlot.Trim);
                    break;
            }
        };
    }

    // ---- the small things of a world's other stories ----------------------------------------------

    static void Stories(Dictionary<string, Action<DecoBuilder, DressArgs>> r)
    {
        r["survey_stakes"] = (b, a) =>
        {
            for (int i = 0; i < 5; i++)
            {
                float x = -4f + i * 2f + R(b, -0.2f, 0.2f), z = R(b, -0.15f, 0.15f);
                switch (a.Era)
                {
                    case DecoTech.Low:
                        b.Box(x - 0.025f, -0.3f, z - 0.025f, x + 0.025f, 0.9f, z + 0.025f, DecoSlot.Wood);
                        Panel(b, new Vector3(x + 0.025f, 0.88f, z), new Vector3(x + 0.1f, 0.88f, z), -Vector3.UnitY * 0.3f, Vector3.UnitZ, 0.004f, DecoSlot.Linen);
                        break;
                    case DecoTech.Mid:
                        b.Box(x - 0.02f, -0.3f, z - 0.02f, x + 0.02f, 1.1f, z + 0.02f, DecoSlot.Wood);
                        b.Box(x - 0.022f, 0.95f, z - 0.022f, x + 0.022f, 1.1f, z + 0.022f, DecoSlot.Alert).Ghost();
                        Panel(b, new Vector3(x + 0.02f, 1.05f, z), new Vector3(x + 0.05f, 1.05f, z), new Vector3(0.12f, -0.4f, 0), Vector3.UnitZ, 0.004f, DecoSlot.Hazard);
                        break;
                    default:
                        b.Cyl(x, -0.2f, z, 0.02f, 1.2f, DecoSlot.Plastic, 6);
                        b.Ball(x, 1.02f, z, 0.04f, DecoSlot.Lamp, 6).Lit();
                        break;
                }
            }
        };

        r["washing_line"] = (b, a) =>
        {
            float w = R(b, 3.6f, 5f);
            var p0 = Post(b, a.Era, -w / 2, 0, 0, 1.9f, 0.8f);
            var p1 = Post(b, a.Era, w / 2, 0, 0, 1.9f, 0.8f);
            foreach (Vector3 p in new[] { p0, p1 }) b.Box(p.X - 0.02f, p.Y - 0.08f, -0.35f, p.X + 0.02f, p.Y - 0.02f, 0.35f, a.Era == DecoTech.Low ? DecoSlot.Wood : a.Era == DecoTech.Mid ? DecoSlot.Metal : DecoSlot.Plastic);
            foreach (float z in new[] { -0.3f, 0.3f })
            {
                bool down = b.Rng.Chance(a.Wear * 0.4f);
                Line(b, new Vector3(p0.X + 0.03f, p0.Y - 0.06f, z), new Vector3(p1.X - 0.03f, p1.Y - 0.06f, z), 0.04f, 0.005f, DecoSlot.Linen, down, 0.55f,
                    (at, dir, i) =>
                    {
                        if (b.Rng.Chance(0.35f)) return;
                        float cw = R(b, 0.25f, 0.6f), ch = R(b, 0.3f, 0.75f);
                        Panel(b, at - dir * (cw / 2), at + dir * (cw / 2), -Vector3.UnitY * ch, Vector3.UnitZ, 0.01f, ClothOf(i + (z > 0 ? 2 : 0)));
                        b.Box(at.X - 0.01f, at.Y - 0.03f, at.Z - 0.012f, at.X + 0.01f, at.Y + 0.03f, at.Z + 0.012f, DecoSlot.Wood).Ghost();
                    },
                    (at, dir, i) => b.Box(at.X - 0.25f, 0, at.Z - 0.3f, at.X + 0.25f, 0.015f, at.Z + 0.3f, ClothOf(i)).Soft().Ghost());
            }
        };

        r["garden_table"] = (b, a) =>
        {
            float top;
            switch (a.Era)
            {
                case DecoTech.Low:
                    top = 0.76f;
                    b.Legs(-0.8f, -0.45f, 0.8f, 0.45f, top - 0.05f, 0.08f, DecoSlot.Wood);
                    b.Box(-0.85f, top - 0.05f, -0.5f, 0.85f, top, 0.5f, DecoSlot.Wood).As(DecoFinish.Wood);
                    b.Box(-0.9f, top, -0.55f, 0.9f, top + 0.006f, 0.55f, DecoSlot.Linen).Soft().Ghost();
                    foreach (float z in new[] { -0.85f, 0.85f }) { b.Legs(-0.75f, z - 0.12f, 0.75f, z + 0.12f, 0.42f, 0.06f, DecoSlot.Wood); b.Box(-0.8f, 0.42f, z - 0.14f, 0.8f, 0.46f, z + 0.14f, DecoSlot.Wood); }
                    break;
                case DecoTech.Mid:
                    top = 0.74f;
                    b.Cyl(0, 0, 0, 0.25f, 0.03f, DecoSlot.Metal, 10);
                    b.Cyl(0, 0.03f, 0, 0.03f, top - 0.06f, DecoSlot.Metal, 6);
                    b.Cyl(0, top - 0.03f, 0, 0.5f, 0.03f, DecoSlot.Body, 16);
                    b.Cyl(0, top, 0, 0.52f, 0.005f, DecoSlot.Linen, 16).Soft().Ghost();
                    break;
                default:
                    top = 0.72f;
                    b.Cyl(0, 0, 0, 0.18f, top - 0.04f, DecoSlot.Plastic, 12, 0.5f);
                    b.Cyl(0, top - 0.04f, 0, 0.55f, 0.04f, DecoSlot.Plastic, 18);
                    b.Cyl(0, top - 0.045f, 0, 0.555f, 0.01f, DecoSlot.Lamp, 18).Lit().Ghost();
                    break;
            }
            // The meal, left as it was: plates, cups, a jug.
            int places = a.Era == DecoTech.Low ? 4 : 3;
            for (int i = 0; i < places; i++)
            {
                float an = i * MathF.Tau / places + 0.3f, rr = a.Era == DecoTech.Low ? 0.3f : 0.32f;
                float x = MathF.Cos(an) * (a.Era == DecoTech.Low ? 0.55f : rr), z = MathF.Sin(an) * rr;
                b.Cyl(x, top + 0.006f, z, 0.11f, 0.015f, DecoSlot.Linen, 12).Ghost();
                b.Cyl(x + 0.12f, top + 0.006f, z + 0.08f, 0.035f, 0.08f, a.Era == DecoTech.High ? DecoSlot.Plastic : DecoSlot.Linen, 8).Ghost();
                if (a.Era == DecoTech.Low) continue;
                // Its chair, pushed back or over.
                float cx = MathF.Cos(an) * 0.85f, cz = MathF.Sin(an) * 0.85f;
                bool over = b.Rng.Chance(0.3f);
                using (b.Turn(new Vector3(cx, 0, cz), over ? 90 : 0, -an * 180 / MathF.PI + 90))
                {
                    if (a.Era == DecoTech.Mid)
                    {
                        b.Legs(cx - 0.2f, cz - 0.2f, cx + 0.2f, cz + 0.2f, 0.44f, 0.025f, DecoSlot.Metal, true);
                        b.Box(cx - 0.21f, 0.44f, cz - 0.21f, cx + 0.21f, 0.47f, cz + 0.21f, DecoSlot.Body);
                        b.Box(cx - 0.21f, 0.47f, cz + 0.18f, cx + 0.21f, 0.85f, cz + 0.21f, DecoSlot.Body);
                    }
                    else
                    {
                        b.Cyl(cx, 0, cz, 0.04f, 0.42f, DecoSlot.Plastic, 8);
                        b.Dome(cx, 0.42f, cz, 0.24f, 0.3f, 0.24f, DecoSlot.Body, 10);
                    }
                }
            }
            b.Cyl(0.05f, top + 0.006f, -0.05f, 0.06f, 0.2f, a.Era == DecoTech.High ? DecoSlot.Plastic : DecoSlot.Signal, 10, 0.8f);
        };

        r["dust_sheets"] = (b, a) =>
        {
            // Furniture under sheets: a settee, a chair, a lamp; plastic over them on a newer world.
            DecoSlot sheet = a.Era == DecoTech.Low ? DecoSlot.Linen : a.Era == DecoTech.Mid ? DecoSlot.Canvas : DecoSlot.Plastic;
            b.Frustum(0, -1f, 1f, -0.45f, 0.45f, 0.5f, -0.98f, 0.98f, -0.43f, 0.43f, sheet).Soft();
            b.Frustum(0.5f, -1f, 1f, 0.15f, 0.47f, 0.95f, -0.96f, 0.96f, 0.25f, 0.45f, sheet).Soft();
            b.Frustum(0, 1.4f, 2.1f, -0.35f, 0.35f, 0.9f, 1.45f, 2.05f, -0.3f, 0.32f, sheet).Soft();
            b.Cyl(-1.6f, 0, 0.1f, 0.25f, 1.5f, sheet, 10, 0.25f).Soft();
            // The sheets' hems, a little proud of the ground.
            b.Box(-1.02f, 0, -0.47f, 1.02f, 0.06f, 0.47f, sheet).Soft().Ghost();
        };

        r["soup_kitchen"] = (b, a) =>
        {
            DecoSlot frame = a.Era == DecoTech.Low ? DecoSlot.Wood : a.Era == DecoTech.Mid ? DecoSlot.Metal : DecoSlot.Plastic;
            foreach (float x in new[] { -1.1f, 1.1f })
            {
                b.Legs(x - 0.9f, -0.4f, x + 0.9f, 0.4f, 0.74f, 0.05f, frame);
                b.Box(x - 0.95f, 0.74f, -0.42f, x + 0.95f, 0.78f, 0.42f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Body);
                for (int k = 0; k < 4; k++) b.Cyl(x - 0.6f + k * 0.15f, 0.78f, 0.2f, 0.07f, 0.03f * (2 + k % 3), DecoSlot.Linen, 10);
            }
            // The pots on their fire, burner or heater.
            for (int k = 0; k < 2; k++)
            {
                float x = -0.6f + k * 1.2f;
                if (a.Era == DecoTech.Low)
                {
                    for (int i = 0; i < 6; i++) { float an = i * MathF.Tau / 6; b.Ball(x + MathF.Cos(an) * 0.32f, 0.06f, 1f + MathF.Sin(an) * 0.32f, 0.08f, DecoSlot.Stone, 5); }
                    b.Cyl(x, 0, 1f, 0.25f, 0.02f, DecoSlot.Char, 10).Ghost();
                    foreach (int i in new[] { 0, 1, 2 }) { float an = i * MathF.Tau / 3; Rod(b, new Vector3(x + MathF.Cos(an) * 0.4f, 0, 1f + MathF.Sin(an) * 0.4f), new Vector3(x, 0.9f, 1f), 0.03f, DecoSlot.Dark); }
                    b.Cyl(x, 0.3f, 1f, 0.24f, 0.32f, DecoSlot.Dark, 12);
                }
                else
                {
                    b.Box(x - 0.3f, 0, 0.75f, x + 0.3f, 0.5f, 1.25f, a.Era == DecoTech.Mid ? DecoSlot.Metal : DecoSlot.Plastic);
                    if (a.Era == DecoTech.High) b.Box(x - 0.31f, 0.4f, 0.74f, x + 0.31f, 0.43f, 1.26f, DecoSlot.Lamp).Lit().Ghost();
                    b.Cyl(x, 0.5f, 1f, 0.24f, 0.36f, DecoSlot.Metal, 12);
                }
            }
            b.Box(1.6f, 0, 0.8f, 2.1f, 0.4f, 1.2f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Body);
            for (int i = 0; i < 5; i++) b.Ball(1.7f + (i % 3) * 0.15f, 0.45f, 0.9f + (i / 3) * 0.15f, 0.07f, 0.05f, 0.06f, DecoSlot.Paper, 6).Rots().Ghost();
        };

        r["burn_barrel"] = (b, a) =>
        {
            switch (a.Era)
            {
                case DecoTech.Low:
                    // A brazier on its tripod.
                    for (int i = 0; i < 3; i++) { float an = i * MathF.Tau / 3; Rod(b, new Vector3(MathF.Cos(an) * 0.35f, 0, MathF.Sin(an) * 0.35f), new Vector3(MathF.Cos(an) * 0.2f, 0.7f, MathF.Sin(an) * 0.2f), 0.03f, DecoSlot.Dark); }
                    b.Cyl(0, 0.65f, 0, 0.22f, 0.25f, DecoSlot.Dark, 12, 1.5f);
                    b.Dome(0, 0.85f, 0, 0.28f, 0.08f, 0.28f, DecoSlot.Char, 10);
                    if (a.Lit) b.Dome(0, 0.9f, 0, 0.2f, 0.32f, 0.2f, DecoSlot.Lamp, 8).Lit().Ghost();
                    break;
                case DecoTech.Mid:
                    b.Cyl(0, 0, 0, 0.29f, 0.88f, DecoSlot.Rust, 14);
                    for (int i = 0; i < 6; i++) { float an = i * MathF.Tau / 6; b.Tube(MathF.Cos(an) * 0.29f, 0.25f, MathF.Sin(an) * 0.29f, MathF.Cos(an) * 0.3f, 0.25f, MathF.Sin(an) * 0.3f, 0.04f, DecoSlot.Char, 6).Ghost(); }
                    b.Cyl(0, 0.8f, 0, 0.27f, 0.06f, DecoSlot.Char, 12).Ghost();
                    b.Box(-0.32f, 0.88f, -0.32f, 0.32f, 0.9f, 0.32f, DecoSlot.Dark);
                    // Still burning where someone keeps it: flames over the grate.
                    if (a.Lit) b.Dome(0, 0.9f, 0, 0.22f, 0.4f, 0.22f, DecoSlot.Lamp, 8).Lit().Ghost();
                    break;
                default:
                    b.Cyl(0, 0, 0, 0.25f, 1.1f, DecoSlot.Plastic, 12, 0.4f);
                    b.Cyl(0, 0.45f, 0, 0.2f, 0.4f, DecoSlot.Lamp, 12, 0.5f).Lit();
                    break;
            }
            b.Ball(0.45f, 0.06f, 0.1f, 0.12f, 0.06f, 0.1f, DecoSlot.Char, 6).Ghost();
        };

        r["spikes"] = (b, a) =>
        {
            // A row of spikes leaning out, the gang's trophies on them, tyres heaped.
            for (int i = 0; i < 7; i++)
            {
                float x = -2.1f + i * 0.7f, lean = R(b, -35, -20);
                using (b.Turn(new Vector3(x, 0, 0), lean, R(b, -10, 10)))
                {
                    if (a.Era == DecoTech.High) b.Hull(new[] { new Vector3(x - 0.1f, -0.2f, -0.06f), new Vector3(x + 0.1f, -0.2f, -0.06f), new Vector3(x, 1.8f, 0), new Vector3(x, 1.8f, 0),
                        new Vector3(x - 0.1f, -0.2f, 0.06f), new Vector3(x + 0.1f, -0.2f, 0.06f), new Vector3(x, 1.8f, 0.01f), new Vector3(x, 1.8f, 0.01f) }, DecoSlot.Plastic);
                    else b.Cyl(x, -0.3f, 0, a.Era == DecoTech.Low ? 0.08f : 0.04f, R(b, 2.3f, 2.9f), a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Rust, 6, 0.05f);
                }
            }
            // A horned skull on the middle one's foot, of some beast.
            b.Ball(0, 0.15f, -0.35f, 0.14f, 0.12f, 0.17f, DecoSlot.Bone, 8);
            foreach (float s in new[] { -1f, 1f }) Rod(b, new Vector3(s * 0.1f, 0.2f, -0.3f), new Vector3(s * 0.38f, 0.45f, -0.2f), 0.03f, DecoSlot.Bone);
            if (a.Era != DecoTech.Low)
                foreach (float tx in new[] { 2.6f, -2.8f })
                    for (int k = 0, n = 3 + b.Rng.Int(3); k < n; k++) b.Cyl(tx + R(b, -0.05f, 0.05f), k * 0.22f, 0.3f, 0.36f, 0.22f, DecoSlot.Rubber, 12);
            if (a.Era == DecoTech.Mid)
            {
                // A panel off a car, its gang mark painted on.
                using (b.Turn(new Vector3(-2.2f, 0, 0.2f), -15, 20))
                {
                    b.Box(-2.7f, 0, 0.17f, -1.7f, 1.2f, 0.22f, DecoSlot.Body);
                    Symbol(b, "x", -2.2f, 0.65f, 0.17f, 0.7f, DecoSlot.Alert);
                }
            }
        };

        r["picket"] = (b, a) =>
        {
            // Chairs round a burning drum, placards stuck in the ground.
            string emblem = a.Style.Length > 0 ? a.Style : "fist";
            All["burn_barrel"](b, a);
            for (int i = 0; i < 3; i++)
            {
                float an = i * MathF.Tau / 3 + 0.4f, x = MathF.Cos(an) * 1f, z = MathF.Sin(an) * 1f;
                using (b.Turn(new Vector3(x, 0, z), 0, -an * 180 / MathF.PI - 90))
                {
                    b.Legs(x - 0.2f, z - 0.2f, x + 0.2f, z + 0.2f, 0.44f, 0.03f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Metal);
                    b.Box(x - 0.22f, 0.44f, z - 0.22f, x + 0.22f, 0.48f, z + 0.22f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Fabric);
                }
            }
            for (int i = 0; i < 2 + b.Rng.Int(2); i++)
            {
                float x = R(b, -1.6f, 1.6f), z = R(b, -1.6f, -1.1f);
                using (b.Turn(new Vector3(x, 0, z), R(b, -15, 15), R(b, -25, 25)))
                {
                    b.Box(x - 0.02f, -0.2f, z - 0.02f, x + 0.02f, 1.4f, z + 0.02f, DecoSlot.Wood);
                    b.Box(x - 0.32f, 1.0f, z - 0.03f, x + 0.32f, 1.5f, z - 0.02f, a.Era == DecoTech.High ? DecoSlot.Plastic : DecoSlot.Paper).As(DecoFinish.Paper);
                    Symbol(b, i % 2 == 0 ? emblem : "fist", x, 1.25f, z - 0.03f, 0.35f, DecoSlot.Alert);
                }
            }
        };

        r["beacon"] = (b, a) =>
        {
            switch (a.Era)
            {
                case DecoTech.Low:
                {
                    Vector3 top = Post(b, a.Era, 0, 0, 0, 5.5f, 1.8f);
                    for (int i = 0; i < 8; i++) { float an = i * MathF.Tau / 8; Rod(b, top + new Vector3(MathF.Cos(an) * 0.1f, 0, MathF.Sin(an) * 0.1f), top + new Vector3(MathF.Cos(an) * 0.32f, 0.45f, MathF.Sin(an) * 0.32f), 0.025f, DecoSlot.Dark); }
                    b.Dome(0, top.Y + 0.15f, 0, 0.25f, 0.18f, 0.25f, DecoSlot.Char, 10);
                    b.Ball(0, top.Y + 0.3f, 0, 0.18f, 0.12f, 0.18f, DecoSlot.Lamp, 8).Lit();
                    b.Light(0, top.Y + 0.5f, 0);
                    break;
                }
                case DecoTech.Mid:
                {
                    // A lattice mast, a lamp housing at its head.
                    const float h = 6.5f;
                    foreach (float sx in new[] { -0.25f, 0.25f }) foreach (float sz in new[] { -0.25f, 0.25f }) b.Beam(new Vector3(sx, -0.2f, sz), new Vector3(sx * 0.4f, h, sz * 0.4f), 0.04f, 0.04f, DecoSlot.Metal);
                    for (float y = 0.6f; y < h - 0.5f; y += 0.9f) { float k = 1f - 0.6f * y / h; Rod(b, new Vector3(-0.25f * k, y, -0.25f * k), new Vector3(0.25f * k, y + 0.9f, -0.25f * k), 0.025f, DecoSlot.Metal); Rod(b, new Vector3(-0.25f * k, y, 0.25f * k), new Vector3(0.25f * k, y + 0.9f, 0.25f * k), 0.025f, DecoSlot.Metal); }
                    b.Cyl(0, h, 0, 0.3f, 0.5f, DecoSlot.Dark, 12);
                    b.Cyl(0, h + 0.12f, 0, 0.31f, 0.26f, DecoSlot.Lamp, 12).Lit();
                    b.Dome(0, h + 0.5f, 0, 0.3f, 0.2f, 0.3f, DecoSlot.Body, 10);
                    b.Light(0, h + 0.25f, 0);
                    break;
                }
                default:
                    b.Cyl(0, -0.2f, 0, 0.18f, 7f, DecoSlot.Plastic, 10, 0.4f);
                    b.Ball(0, 7.05f, 0, 0.3f, DecoSlot.Lamp, 12).Lit();
                    for (int i = 0; i < 3; i++) b.Cyl(0, 1.2f + i * 1.8f, 0, 0.17f - i * 0.03f, 0.06f, DecoSlot.Lamp, 10).Lit().Ghost();
                    b.Light(0, 7f, 0);
                    break;
            }
        };

        r["telescope"] = (b, a) =>
        {
            // On its tripod, pointing at the ground; a chart of dots and lines on a board beside it.
            Vector3 head = new(0, 1.25f, 0);
            for (int i = 0; i < 3; i++) { float an = i * MathF.Tau / 3; Rod(b, new Vector3(MathF.Cos(an) * 0.5f, 0, MathF.Sin(an) * 0.5f), head, 0.03f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Metal); }
            Vector3 aim = Vector3.Normalize(new Vector3(0.3f, -0.55f, -1f));
            switch (a.Era)
            {
                case DecoTech.Low: b.Tube(head - aim * 0.5f, head + aim * 0.45f, 0.06f, DecoSlot.Bronze, 10, 0.8f); break;
                case DecoTech.Mid: b.Tube(head - aim * 0.5f, head + aim * 0.5f, 0.11f, DecoSlot.Linen, 12); b.Tube(head + new Vector3(0, 0.15f, 0), head + new Vector3(0, 0.15f, 0) + aim * 0.3f, 0.025f, DecoSlot.Dark, 6); break;
                default:
                    using (b.Turn(head, -40, 15)) b.Dome(head.X, head.Y, head.Z, 0.45f, 0.12f, 0.45f, DecoSlot.Plastic, 14);
                    b.Ball(head.X, head.Y, head.Z, 0.1f, DecoSlot.Lamp, 8).Lit();
                    break;
            }
            b.Box(0.6f, 0, 0.3f, 1.4f, 0.03f, 0.9f, DecoSlot.Wood);
            for (int i = 0; i < 9; i++) b.Cyl(R(b, 0.65f, 1.35f), 0.03f, R(b, 0.35f, 0.85f), 0.012f, 0.004f, DecoSlot.Linen, 6).Ghost();
            for (int i = 0; i < 3; i++) Flat(b, new Vector3(R(b, 0.7f, 1.3f), 0.034f, R(b, 0.4f, 0.8f)), new Vector3(R(b, 0.7f, 1.3f), 0.034f, R(b, 0.4f, 0.8f)), Vector3.UnitY, 0.008f, 0.002f, DecoSlot.Linen).Ghost();
        };

        r["scaffold"] = (b, a) =>
        {
            float w = 2f, d = 1.2f;
            int levels = 2 + b.Rng.Int(2);
            DecoSlot frame = a.Era == DecoTech.Low ? DecoSlot.Wood : a.Era == DecoTech.Mid ? DecoSlot.Metal : DecoSlot.Plastic;
            float r0 = a.Era == DecoTech.Low ? 0.05f : 0.025f, top = levels * 2f;
            foreach (float x in new[] { -w / 2, w / 2 }) foreach (float z in new[] { -d / 2, d / 2 }) b.Cyl(x, -0.1f, z, r0, top + 1f, frame, 6);
            for (int l = 1; l <= levels; l++)
            {
                float y = l * 2f;
                b.Box(-w / 2, y - 0.05f, -d / 2, w / 2, y, d / 2, a.Era == DecoTech.High ? DecoSlot.Plastic : DecoSlot.Wood).As(a.Era == DecoTech.High ? DecoFinish.Plastic : DecoFinish.Wood);
                b.Tube(-w / 2, y + 1f, -d / 2, w / 2, y + 1f, -d / 2, r0 * 0.8f, frame, 6);
                Rod(b, new Vector3(-w / 2, y - 2f, d / 2), new Vector3(w / 2, y, d / 2), r0 * 1.4f, frame);
                if (a.Era == DecoTech.High) b.Box(-w / 2, y - 0.06f, -d / 2 - 0.01f, w / 2, y - 0.03f, -d / 2, DecoSlot.Lamp).Lit().Ghost();
            }
            // A ladder up its side.
            foreach (float z in new[] { -0.2f, 0.2f }) b.Box(w / 2 + 0.08f, 0, z - 0.02f, w / 2 + 0.12f, top, z + 0.02f, frame);
            for (float y = 0.3f; y < top; y += 0.3f) b.Box(w / 2 + 0.08f, y, -0.2f, w / 2 + 0.12f, y + 0.03f, 0.2f, frame).Ghost();
        };

        r["mannequins"] = (b, a) =>
        {
            // Figures in a row, all facing one way: dummies, mannequins, white figures with an eye strip.
            int n = 3 + b.Rng.Int(3);
            bool same = a.Style == "same" || b.Rng.Chance(0.4f);
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) / 2f) * 0.85f;
                bool over = b.Rng.Chance(a.Wear * 0.3f);
                DecoSlot body = a.Era == DecoTech.Low ? DecoSlot.Canvas : a.Era == DecoTech.Mid ? (same ? DecoSlot.Body : ClothOf(i)) : DecoSlot.Plastic;
                using (b.Turn(new Vector3(x, 0, 0), over ? -88 : 0, 0))
                {
                    b.Cyl(x, 0, 0, 0.2f, 0.03f, DecoSlot.Dark, 10);
                    b.Cyl(x, 0.03f, 0, 0.025f, 0.9f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Metal, 6);
                    b.Frustum(0.9f, x - 0.17f, x + 0.17f, -0.1f, 0.1f, 1.5f, x - 0.21f, x + 0.21f, -0.12f, 0.12f, body);
                    b.Ball(x, 1.68f, 0, 0.12f, 0.14f, 0.12f, body, 10);
                    if (a.Era == DecoTech.Low) b.Box(x - 0.45f, 1.38f, -0.03f, x + 0.45f, 1.44f, 0.03f, DecoSlot.Wood);
                    else if (a.Era == DecoTech.Mid) { b.Box(x - 0.005f, 1.56f, -0.125f, x + 0.005f, 1.8f, 0.125f, DecoSlot.Hazard).Ghost(); b.Box(x - 0.125f, 1.67f, -0.125f, x + 0.125f, 1.69f, -0.1f, DecoSlot.Hazard).Ghost(); }
                    else b.Box(x - 0.09f, 1.69f, -0.125f, x + 0.09f, 1.72f, -0.11f, DecoSlot.Lamp).Lit().Ghost();
                }
            }
        };

        r["timber_frame"] = (b, a) =>
        {
            // The ribs of a ship that will never sail, set up on blocks: copied from something seen once.
            float len = R(b, 8f, 11f);
            foreach (float z in new[] { -len / 2 + 1f, 0f, len / 2 - 1f }) b.Box(-0.3f, 0, z - 0.3f, 0.3f, 0.45f, z + 0.3f, DecoSlot.Stone);
            b.Box(-0.12f, 0.45f, -len / 2, 0.12f, 0.7f, len / 2, DecoSlot.Wood).As(DecoFinish.Wood);
            Rod(b, new Vector3(0, 0.6f, -len / 2), new Vector3(0, 3f, -len / 2 - 0.9f), 0.18f, DecoSlot.Wood);
            Rod(b, new Vector3(0, 0.6f, len / 2), new Vector3(0, 2.6f, len / 2 + 0.4f), 0.18f, DecoSlot.Wood);
            for (float z = -len / 2 + 0.8f; z < len / 2 - 0.5f; z += 0.9f)
            {
                float k = 1f - MathF.Pow(z / (len / 2), 2) * 0.6f;
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 p = new(0, 0.62f, z);
                    for (int i = 0; i < 4; i++)
                    {
                        float an = (i + 1) * MathF.PI / 8;
                        Vector3 q = new(s * MathF.Sin(an) * 1.6f * k, 0.62f + (1 - MathF.Cos(an)) * 2.4f, z);
                        Rod(b, p, q, 0.11f, DecoSlot.Wood);
                        p = q;
                    }
                }
            }
            // A few strakes laid on, the rest never got to.
            for (int i = 0; i < 3; i++)
            {
                float y = 1.0f + i * 0.35f, x = 0.95f + i * 0.2f;
                b.Box(x, y, -len / 3, x + 0.06f, y + 0.25f, len / 3, DecoSlot.Wood).As(DecoFinish.Wood);
            }
            b.Cyl(0, 0.7f, -len / 6, 0.12f, 5.5f, DecoSlot.Wood, 8, 0.7f);
            b.Box(-1.2f, 4.6f, -len / 6 - 0.06f, 1.2f, 4.72f, -len / 6 + 0.06f, DecoSlot.Wood);
        };

        r["cones"] = (b, a) =>
        {
            int n = 3 + b.Rng.Int(4);
            for (int i = 0; i < n; i++)
            {
                float x = -1.8f + i * 0.75f, z = R(b, -0.2f, 0.2f);
                bool over = b.Rng.Chance(a.Wear * 0.4f);
                using (b.Turn(new Vector3(x, 0, z), over ? 70 : 0, R(b, 0, 360)))
                    switch (a.Era)
                    {
                        case DecoTech.Low:
                            b.Box(x - 0.03f, -0.2f, z - 0.03f, x + 0.03f, 0.9f, z + 0.03f, DecoSlot.Wood);
                            Panel(b, new Vector3(x + 0.03f, 0.88f, z), new Vector3(x + 0.25f, 0.88f, z), -Vector3.UnitY * 0.18f, Vector3.UnitZ, 0.005f, DecoSlot.Linen);
                            break;
                        case DecoTech.Mid:
                            b.Box(x - 0.18f, 0, z - 0.18f, x + 0.18f, 0.03f, z + 0.18f, DecoSlot.Dark);
                            b.Cyl(x, 0.03f, z, 0.14f, 0.68f, DecoSlot.Alert, 10, 0.12f);
                            b.Cyl(x, 0.33f, z, 0.1f, 0.1f, DecoSlot.Linen, 10, 0.75f).Ghost();
                            break;
                        default:
                            b.Cyl(x, 0, z, 0.12f, 0.8f, DecoSlot.Plastic, 10, 0.3f);
                            b.Cyl(x, 0.55f, z, 0.07f, 0.06f, DecoSlot.Lamp, 10).Lit().Ghost();
                            break;
                    }
            }
            // A barrow, a shovel leant on it, and the pile they swept.
            float bx = 2.4f;
            if (a.Era == DecoTech.High) { foreach (float s in new[] { -0.25f, 0.25f }) b.Box(bx + s - 0.04f, 0, -0.5f, bx + s + 0.04f, 0.15f, 0.5f, DecoSlot.Dark); b.Box(bx - 0.35f, 0.15f, -0.55f, bx + 0.35f, 0.55f, 0.55f, DecoSlot.Plastic); }
            else
            {
                b.Tube(bx - 0.05f, 0.18f, -0.6f, bx + 0.05f, 0.18f, -0.6f, 0.18f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Rubber, 10);
                b.Frustum(0.25f, bx - 0.25f, bx + 0.25f, -0.45f, 0.25f, 0.6f, bx - 0.35f, bx + 0.35f, -0.6f, 0.4f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Metal);
                foreach (float s in new[] { -0.25f, 0.25f }) { Rod(b, new Vector3(bx + s, 0.35f, 0.3f), new Vector3(bx + s * 1.2f, 0.55f, 1f), 0.03f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Metal); b.Box(bx + s - 0.02f, 0, 0.2f, bx + s + 0.02f, 0.3f, 0.24f, DecoSlot.Metal); }
            }
            Rod(b, new Vector3(bx + 0.5f, 0, 0.1f), new Vector3(bx + 0.3f, 1.2f, 0.25f), 0.03f, DecoSlot.Wood);
            b.Dome(bx + 1f, 0, 0.4f, 0.45f, 0.18f, 0.35f, DecoSlot.Soil, 8);
        };

        r["water_barrels"] = (b, a) =>
        {
            int n = 3 + b.Rng.Int(3);
            for (int i = 0; i < n; i++)
            {
                float x = -1f + (i % 3) * 0.7f, z = (i / 3) * 0.7f;
                switch (a.Era)
                {
                    case DecoTech.Low:
                        b.Cyl(x, 0, z, 0.3f, 0.8f, DecoSlot.Wood, 12, 0.95f).As(DecoFinish.Wood);
                        foreach (float h in new[] { 0.12f, 0.66f }) b.Cyl(x, h, z, 0.31f, 0.04f, DecoSlot.Dark, 12).Ghost();
                        break;
                    case DecoTech.Mid:
                        b.Cyl(x, 0, z, 0.29f, 0.88f, i % 2 == 0 ? DecoSlot.Signal : DecoSlot.Body, 14);
                        foreach (float h in new[] { 0.3f, 0.6f }) b.Cyl(x, h, z, 0.295f, 0.03f, DecoSlot.Dark, 14).Ghost();
                        break;
                    default:
                        b.Cyl(x, 0, z, 0.27f, 1.0f, DecoSlot.Plastic, 12);
                        b.Box(x - 0.03f, 0.1f, z - 0.275f, x + 0.03f, 0.9f, z - 0.265f, DecoSlot.Lamp).Lit().Ghost();
                        break;
                }
            }
            // A tap on a standpipe and a bucket under it; jerrycans on a mid world.
            b.Cyl(1.3f, -0.3f, -0.2f, 0.03f, 1.3f, DecoSlot.Metal, 6);
            b.Tube(1.3f, 0.95f, -0.2f, 1.3f, 0.95f, -0.4f, 0.02f, DecoSlot.Metal, 6);
            b.Cyl(1.3f, 0, -0.45f, 0.13f, 0.28f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Plastic, 10, 1.25f);
            if (a.Era == DecoTech.Mid) for (int k = 0; k < 2; k++) b.Box(-1.5f + k * 0.3f, 0, -0.8f, -1.3f + k * 0.3f, 0.45f, -0.5f, DecoSlot.Trim);
        };

        r["planters"] = (b, a) =>
        {
            float w = R(b, 0.9f, 1.4f), h = 0.5f;
            switch (a.Era)
            {
                case DecoTech.Low: b.Box(-w / 2, 0, -0.25f, w / 2, h, 0.25f, DecoSlot.Wood).As(DecoFinish.Wood); break;
                case DecoTech.Mid: b.Taper(-w / 2, 0, -0.28f, w / 2, h, 0.28f, 1.08f, 1.1f, b.Rng.Chance(0.5f) ? DecoSlot.Stone : DecoSlot.Body); break;
                default: b.Frustum(0, -w / 2 + 0.05f, w / 2 - 0.05f, -0.22f, 0.22f, h, -w / 2, w / 2, -0.27f, 0.27f, DecoSlot.Plastic); b.Box(-w / 2 + 0.03f, h - 0.08f, -0.275f, w / 2 - 0.03f, h - 0.05f, -0.265f, DecoSlot.Lamp).Lit().Ghost(); break;
            }
            b.Box(-w / 2 + 0.05f, h - 0.06f, -0.2f, w / 2 - 0.05f, h - 0.02f, 0.2f, DecoSlot.Soil).Ghost();
            for (int i = 0, n = (int)(w / 0.18f); i < n; i++)
            {
                float x = -w / 2 + 0.1f + (w - 0.2f) * i / Math.Max(1, n - 1), y = h + R(b, 0.05f, 0.18f);
                b.Ball(x, y, R(b, -0.1f, 0.1f), 0.12f, 0.1f, 0.12f, DecoSlot.Plant, 6).Rots().Ghost();
                if (b.Rng.Chance(0.6f)) b.Ball(x + R(b, -0.05f, 0.05f), y + 0.09f, R(b, -0.1f, 0.1f), 0.035f, i % 2 == 0 ? DecoSlot.Signal : DecoSlot.Trim, 5).Rots().Ghost();
            }
        };

        r["hitch_rail"] = (b, a) =>
        {
            switch (a.Era)
            {
                case DecoTech.Low:
                    // A hitching rail and a trough.
                    foreach (float x in new[] { -1.2f, 1.2f }) b.Box(x - 0.07f, -0.3f, -0.07f, x + 0.07f, 1.1f, 0.07f, DecoSlot.Wood).As(DecoFinish.Wood);
                    b.Box(-1.3f, 1.0f, -0.06f, 1.3f, 1.1f, 0.06f, DecoSlot.Wood).As(DecoFinish.Wood);
                    b.Box(-0.9f, 0, -0.9f, 0.9f, 0.5f, -0.4f, DecoSlot.Wood).As(DecoFinish.Wood);
                    b.Box(-0.85f, 0.4f, -0.85f, 0.85f, 0.42f, -0.45f, DecoSlot.Dark).Ghost();
                    break;
                case DecoTech.Mid:
                    // A cycle rack of steel hoops, a bicycle left in one.
                    for (int i = 0; i < 4; i++)
                    {
                        float x = -1.2f + i * 0.8f;
                        for (int k = 0; k < 6; k++)
                        {
                            float a0 = k * MathF.PI / 6, a1 = (k + 1) * MathF.PI / 6;
                            b.Tube(new Vector3(x, 0.45f + MathF.Sin(a0) * 0.4f - 0.4f + 0.4f, MathF.Cos(a0) * 0.35f), new Vector3(x, 0.45f + MathF.Sin(a1) * 0.4f, MathF.Cos(a1) * 0.35f), 0.025f, DecoSlot.Metal, 6);
                        }
                        foreach (float s in new[] { -0.35f, 0.35f }) b.Cyl(x, -0.1f, s, 0.025f, 0.55f, DecoSlot.Metal, 6);
                    }
                    if (b.Rng.Chance(0.6f))
                    {
                        float x = -0.8f + 0.12f;
                        foreach (float z in new[] { -0.5f, 0.5f })
                            for (int k = 0; k < 10; k++)
                            {
                                float a0 = k * MathF.Tau / 10, a1 = (k + 1) * MathF.Tau / 10;
                                Rod(b, new Vector3(x, 0.33f + MathF.Sin(a0) * 0.33f, z + MathF.Cos(a0) * 0.33f), new Vector3(x, 0.33f + MathF.Sin(a1) * 0.33f, z + MathF.Cos(a1) * 0.33f), 0.03f, DecoSlot.Rubber);
                            }
                        Rod(b, new Vector3(x, 0.33f, -0.5f), new Vector3(x, 0.75f, 0.1f), 0.03f, DecoSlot.Trim);
                        Rod(b, new Vector3(x, 0.33f, 0.5f), new Vector3(x, 0.75f, 0.1f), 0.03f, DecoSlot.Trim);
                        Rod(b, new Vector3(x, 0.75f, 0.1f), new Vector3(x, 0.8f, -0.45f), 0.03f, DecoSlot.Trim);
                    }
                    break;
                default:
                    // Charging posts, one cable hanging from each.
                    for (int i = 0; i < 3; i++)
                    {
                        float x = -1.2f + i * 1.2f;
                        b.Frustum(0, x - 0.15f, x + 0.15f, -0.1f, 0.1f, 1.3f, x - 0.1f, x + 0.1f, -0.07f, 0.07f, DecoSlot.Plastic);
                        b.Box(x - 0.08f, 0.9f, -0.085f, x + 0.08f, 1.15f, -0.075f, DecoSlot.Screen).Lit().Ghost();
                        b.Cyl(x, 1.3f, 0, 0.1f, 0.03f, DecoSlot.Lamp, 10).Lit().Ghost();
                        b.Hang(new Vector3(x + 0.1f, 0.8f, -0.05f), new Vector3(0.3f, 0, -1), 0.015f, DecoSlot.Dark, 0.5f);
                    }
                    break;
            }
        };

        r["fence"] = (b, a) =>
        {
            float len = R(b, 8f, 11f);
            int posts = (int)(len / 2.5f) + 1;
            var tops = new List<Vector3>();
            for (int i = 0; i < posts; i++)
            {
                float x = -len / 2 + len * i / (posts - 1);
                if (a.Era == DecoTech.High) { b.Cyl(x, -0.1f, 0, 0.05f, 1.3f, DecoSlot.Plastic, 8); tops.Add(new Vector3(x, 1.1f, 0)); continue; }
                b.Box(x - 0.06f, -0.3f, -0.06f, x + 0.06f, 1.25f, 0.06f, a.Era == DecoTech.Low ? DecoSlot.Wood : DecoSlot.Metal).As(a.Era == DecoTech.Low ? DecoFinish.Wood : DecoFinish.Steel);
                tops.Add(new Vector3(x, 1.1f, 0));
            }
            for (int i = 0; i + 1 < tops.Count; i++)
                for (int s = 0; s < (a.Era == DecoTech.Mid ? 3 : 2); s++)
                {
                    Vector3 p = tops[i] - Vector3.UnitY * (s * 0.4f), q = tops[i + 1] - Vector3.UnitY * (s * 0.4f);
                    bool broken = b.Rng.Chance(a.Wear * 0.3f);
                    switch (a.Era)
                    {
                        case DecoTech.Low:
                            // A rail down at one end where a post let go of it.
                            if (broken) Rod(b, p + new Vector3(0, 0, 0.08f), q with { Y = 0.05f, Z = 0.08f }, 0.08f, DecoSlot.Wood);
                            else b.Box(p.X, p.Y - 0.05f, 0.06f, q.X, p.Y + 0.05f, 0.12f, DecoSlot.Wood).As(DecoFinish.Wood);
                            break;
                        case DecoTech.Mid:
                            if (broken) b.Hang(p + new Vector3(0.06f, 0, 0), q - p, 0.006f, DecoSlot.Metal, 0.8f);
                            else b.Cable(p + new Vector3(0.06f, 0, 0), q - new Vector3(0.06f, 0, 0), 0.006f, DecoSlot.Metal, 0.015f);
                            break;
                        default:
                            if (!broken) { b.Box(p.X, p.Y - 0.03f, -0.03f, q.X, p.Y + 0.03f, 0.03f, DecoSlot.Plastic); if (s == 0) b.Box(p.X, p.Y + 0.03f, -0.031f, q.X, p.Y + 0.05f, 0.031f, DecoSlot.Lamp).Lit().Ghost(); }
                            break;
                    }
                }
        };
    }
}
