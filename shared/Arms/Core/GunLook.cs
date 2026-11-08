using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Arms;

public enum PartShape { Box, Cylinder, Sphere, Torus, Capsule }

/// <summary>How a part is finished: the worn held-item surface, a glowing emitter, or clear glass.</summary>
public enum Finish { Worn, Emitter }

/// <summary>What a part is for: the model is a list of these, so a look can be read back (tests) and lit by role (charge rings).</summary>
public enum Role { Receiver, Barrel, Muzzle, Emitter, Stock, Grip, Guard, Magazine, Sight, Vent, Fin, Weld, Ring, Tank, Sac, Eye, Rib, Brace }

/// <summary>
/// One primitive of a gun's model, in the gun's frame: the grip's top at the origin, the barrel
/// along -Z, up +Y. Box size is its extents; a cylinder is (top radius, height, bottom radius)
/// along its Y; a sphere (radius, height); a torus (inner, outer); a capsule (radius, height).
/// <see cref="Turn"/> is degrees (X, Y, Z).
/// </summary>
public readonly record struct LookPart(PartShape Shape, Vector3 At, Vector3 Size, Vector3 Turn, uint Colour, Finish Finish,
    float Wear, float Rough, float Metal, Role Role, int Index);

/// <summary>A gun's model as parts, where its muzzle is, and how long it is overall.</summary>
public sealed record GunModelSpec(IReadOnlyList<LookPart> Parts, Vector3 Muzzle, float Length, Band Band);

/// <summary>
/// Builds a gun's model from its row: the parts it has (receiver, barrel or emitter, stock, grip,
/// magazine or cell, sights, fins or vents) and its tech band set every shape and finish. Low-tech
/// is crude and welded (thick plate, weld beads, raw iron and dark wood tones, heavy wear);
/// mid-tech is utilitarian (squared gunmetal and polymer, rails, moderate wear); high-tech is sleek
/// (pale rounded shrouds, few seams, glowing emitters, little wear); living weapons are grown
/// (fleshy sacs, ribbing, chitin, a glow where something lives inside). Nothing is a copy of a real
/// or famous gun: every look is these rules applied to these parts. Engine-free; the slice turns
/// the parts into meshes with its held-item finish.
/// </summary>
public static class GunLook
{
    sealed class Palette
    {
        public uint Main, Dark, Trim, Glow;
        public float Wear, Rough, Metal;
    }

    static Palette PaletteOf(Band band, uint accent) => band switch
    {
        Band.Low => new Palette { Main = 0x4c4640, Dark = 0x2c2826, Trim = 0x6b4a2e, Glow = 0xff8a3a, Wear = 0.85f, Rough = 0.72f, Metal = 0.35f },
        Band.Mid => new Palette { Main = 0x56606a, Dark = 0x2e343a, Trim = accent, Glow = 0xffb347, Wear = 0.45f, Rough = 0.6f, Metal = 0.25f },
        Band.High => new Palette { Main = 0xd9dde2, Dark = 0x2a2f38, Trim = 0x8a95a3, Glow = accent, Wear = 0.12f, Rough = 0.35f, Metal = 0.2f },
        _ => new Palette { Main = 0x8a4c4a, Dark = 0x3a2626, Trim = 0x6f5a3a, Glow = accent, Wear = 0.9f, Rough = 0.82f, Metal = 0.02f },
    };

    public static GunModelSpec Build(WeaponDef def)
    {
        LookSpec l = def.Look;
        var parts = new List<LookPart>();
        Palette p = PaletteOf(l.Band, l.Accent);
        uint seed = Hash(def.Id);
        float cal = Math.Clamp(l.Calibre, 0f, 1f);

        if (def.Delivery == Delivery.Placed) return Puck(def, p, parts);
        if (l.Band == Band.Living) return Grown(def, l, p, parts, cal, seed);

        // ---- Receiver: the body everything hangs on ----
        float w = 0.034f + 0.05f * cal, h = 0.05f + 0.07f * cal, len = MathF.Max(0.12f, l.Body);
        float top = h * 0.5f + 0.012f;              // the receiver sits just above the hand
        float front = -len * 0.62f, back = len * 0.38f;
        Vector3 rc = new(0f, top, (front + back) * 0.5f);
        void Add(PartShape s, Vector3 at, Vector3 size, Vector3 turn, uint c, Role role, int index = 0, Finish f = Finish.Worn, float wear = -1f) =>
            parts.Add(new LookPart(s, at, size, turn, c, f, wear < 0f ? p.Wear : wear, p.Rough, role == Role.Barrel || role == Role.Muzzle ? p.Metal + 0.2f : p.Metal, role, index));

        switch (l.Band)
        {
            case Band.Low:
                // Thick plate boxes, a top plate a little off true, weld beads along the seams.
                Add(PartShape.Box, rc, new Vector3(w, h, len), Vector3.Zero, p.Main, Role.Receiver);
                Add(PartShape.Box, rc + new Vector3(0f, h * 0.5f + 0.004f, -len * 0.05f), new Vector3(w * 0.86f, 0.008f, len * 0.7f), new Vector3(0f, 1.5f, 0f), p.Dark, Role.Receiver, 1);
                for (int i = 0; i < 4 + (int)(len * 10f); i++)
                {
                    float z = front + (i + 0.5f) * len / (4 + (int)(len * 10f));
                    float side = (i & 1) == 0 ? 1f : -1f;
                    Add(PartShape.Cylinder, new Vector3(side * w * 0.5f, top + h * 0.48f, z), new Vector3(0.004f, 0.012f, 0.004f), new Vector3(0f, 0f, 90f), 0x5e554c, Role.Weld, i, wear: 1f);
                }
                break;
            case Band.Mid:
                Add(PartShape.Box, rc, new Vector3(w, h, len), Vector3.Zero, p.Main, Role.Receiver);
                // A polymer lower half and a side panel line.
                Add(PartShape.Box, rc + new Vector3(0f, -h * 0.28f, 0f), new Vector3(w * 1.04f, h * 0.44f, len * 0.96f), Vector3.Zero, p.Dark, Role.Receiver, 1);
                Add(PartShape.Box, rc + new Vector3(w * 0.52f, h * 0.1f, -len * 0.1f), new Vector3(0.004f, h * 0.3f, len * 0.5f), Vector3.Zero, p.Trim, Role.Receiver, 2);
                break;
            default:
                // A rounded shroud over a slim core, with a glowing seam down each side.
                Add(PartShape.Capsule, rc + new Vector3(0f, h * 0.1f, 0f), new Vector3(h * 0.55f, len, 0f), new Vector3(90f, 0f, 0f), p.Main, Role.Receiver);
                Add(PartShape.Box, rc + new Vector3(0f, -h * 0.22f, 0f), new Vector3(w * 0.9f, h * 0.5f, len * 0.8f), Vector3.Zero, p.Dark, Role.Receiver, 1);
                foreach (float side in new[] { -1f, 1f })
                    Add(PartShape.Box, rc + new Vector3(side * h * 0.5f, h * 0.1f, 0f), new Vector3(0.003f, 0.006f, len * 0.7f), Vector3.Zero, p.Glow, Role.Emitter, side > 0 ? 1 : 0, Finish.Emitter);
                break;
        }

        // ---- Barrel or emitter ----
        float br = 0.007f + 0.034f * cal * cal + 0.008f * cal;
        float bl = MathF.Max(0.04f, l.Barrel);
        float by = top + h * (l.Band == Band.High ? 0.1f : 0.15f);
        bool tube = cal >= 0.7f;                    // a launcher: the barrel is the body
        if (tube)
        {
            float tz = front - bl * 0.5f + len * 0.3f;
            Add(PartShape.Cylinder, new Vector3(0f, by, tz), new Vector3(br, bl + len * 0.6f, br), new Vector3(90f, 0f, 0f), l.Band == Band.High ? p.Main : p.Main, Role.Barrel);
            // Reinforcing bands round the tube.
            for (int i = 0; i < 3; i++)
                Add(PartShape.Torus, new Vector3(0f, by, tz + (i - 1) * bl * 0.38f), new Vector3(br * 0.98f, br * 1.12f, 0f), new Vector3(90f, 0f, 0f), p.Dark, Role.Ring, i);
        }
        else Add(PartShape.Cylinder, new Vector3(0f, by, front - bl * 0.5f), new Vector3(br, bl, br * 1.08f), new Vector3(90f, 0f, 0f), l.Band == Band.High ? p.Dark : 0x34363a, Role.Barrel);
        float muzzleZ = tube ? front - bl * 0.5f + len * 0.3f - (bl + len * 0.6f) * 0.5f : front - bl;

        // The muzzle, by band: a crude welded brake, a plain crown, or a lens ring.
        if (!tube)
        {
            if (l.Band == Band.Low)
            {
                Add(PartShape.Box, new Vector3(0f, by, muzzleZ + 0.015f), new Vector3(br * 2.6f, br * 2.2f, 0.03f), Vector3.Zero, p.Dark, Role.Muzzle);
                Add(PartShape.Box, new Vector3(0f, by, muzzleZ + 0.015f), new Vector3(br * 3.2f, br * 0.7f, 0.012f), Vector3.Zero, 0x5e554c, Role.Weld, 99, wear: 1f);
            }
            else if (l.Band == Band.Mid) Add(PartShape.Cylinder, new Vector3(0f, by, muzzleZ + 0.012f), new Vector3(br * 1.35f, 0.024f, br * 1.35f), new Vector3(90f, 0f, 0f), p.Dark, Role.Muzzle);
            else
            {
                Add(PartShape.Torus, new Vector3(0f, by, muzzleZ + 0.004f), new Vector3(br * 0.9f, br * 1.7f, 0f), new Vector3(90f, 0f, 0f), p.Dark, Role.Muzzle);
                Add(PartShape.Cylinder, new Vector3(0f, by, muzzleZ + 0.006f), new Vector3(br * 0.95f, 0.004f, br * 0.95f), new Vector3(90f, 0f, 0f), p.Glow, Role.Emitter, 2, Finish.Emitter);
            }
        }

        // ---- Emitters the parts call for ----
        switch (l.Emitter)
        {
            case "rings":
                // One ring per charge stage up the barrel: they light one by one as it charges.
                int n = def.Charge?.Stages ?? 4;
                for (int i = 0; i < n; i++)
                {
                    float z = front - bl * (0.18f + 0.62f * i / Math.Max(1, n - 1));
                    Add(PartShape.Torus, new Vector3(0f, by, z), new Vector3(br * 1.15f, br * 2.1f, 0f), new Vector3(90f, 0f, 0f), p.Glow, Role.Ring, i, Finish.Emitter);
                }
                break;
            case "prongs":
                // The fork: three prongs round the muzzle, each tipped with a node.
                for (int i = 0; i < 3; i++)
                {
                    float a = i * MathF.Tau / 3f + MathF.PI / 2f;
                    var off = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f) * (br * 2.6f);
                    Add(PartShape.Box, new Vector3(off.X, by + off.Y, muzzleZ - 0.03f), new Vector3(0.007f, 0.007f, 0.09f), new Vector3(-off.Y * 120f, off.X * 120f, 0f), p.Dark, Role.Emitter, 10 + i);
                    Add(PartShape.Sphere, new Vector3(off.X * 1.4f, by + off.Y * 1.4f, muzzleZ - 0.075f), new Vector3(0.008f, 0.016f, 0f), Vector3.Zero, p.Glow, Role.Emitter, 20 + i, Finish.Emitter);
                }
                break;
            case "pilot":
                // A small blue pilot light under the nozzle, always on.
                Add(PartShape.Cylinder, new Vector3(0f, by - br * 1.9f, muzzleZ + 0.02f), new Vector3(0.006f, 0.05f, 0.006f), new Vector3(90f, 0f, 0f), p.Dark, Role.Emitter, 30);
                Add(PartShape.Sphere, new Vector3(0f, by - br * 1.9f, muzzleZ - 0.006f), new Vector3(0.007f, 0.014f, 0f), Vector3.Zero, 0x4aa8ff, Role.Emitter, 31, Finish.Emitter);
                break;
            case "lens":
                Add(PartShape.Cylinder, new Vector3(0f, by, muzzleZ - 0.002f), new Vector3(br * 1.1f, 0.006f, br * 1.1f), new Vector3(90f, 0f, 0f), p.Glow, Role.Emitter, 40, Finish.Emitter);
                break;
        }

        // ---- Grip and guard ----
        uint gripColour = l.Band == Band.Low ? p.Trim : p.Dark;
        Add(PartShape.Box, new Vector3(0f, -0.045f, 0.012f), new Vector3(w * 0.8f, 0.1f, 0.04f), new Vector3(16f, 0f, 0f), gripColour, Role.Grip);
        Add(PartShape.Box, new Vector3(0f, -0.004f, -0.035f), new Vector3(0.006f, 0.008f, 0.05f), Vector3.Zero, p.Dark, Role.Guard);
        Add(PartShape.Box, new Vector3(0f, -0.022f, -0.058f), new Vector3(0.006f, 0.036f, 0.006f), Vector3.Zero, p.Dark, Role.Guard, 1);

        // ---- Magazine, cell, tank ----
        float magZ = front + len * 0.28f;
        switch (l.Mag)
        {
            case "box":
                float mh = 0.06f + 0.06f * cal;
                Add(PartShape.Box, new Vector3(0f, top - h * 0.5f - mh * 0.45f, magZ), new Vector3(w * 0.7f, mh, 0.032f + 0.03f * cal), new Vector3(-8f, 0f, 0f), p.Dark, Role.Magazine);
                break;
            case "tube":
                Add(PartShape.Cylinder, new Vector3(0f, by - br - 0.016f, front - bl * 0.42f), new Vector3(0.013f, bl * 0.8f, 0.013f), new Vector3(90f, 0f, 0f), p.Dark, Role.Magazine);
                // The pump grip on the tube.
                Add(PartShape.Box, new Vector3(0f, by - br - 0.016f, front - bl * 0.3f), new Vector3(0.036f, 0.034f, 0.09f), Vector3.Zero, gripColour, Role.Grip, 1);
                break;
            case "cell":
                Add(PartShape.Box, new Vector3(0f, top - h * 0.5f - 0.03f, magZ), new Vector3(w * 0.75f, 0.06f, 0.05f), Vector3.Zero, p.Dark, Role.Magazine);
                Add(PartShape.Box, new Vector3(w * 0.39f, top - h * 0.5f - 0.03f, magZ), new Vector3(0.003f, 0.04f, 0.03f), Vector3.Zero, p.Glow, Role.Emitter, 50, Finish.Emitter);
                break;
            case "tank":
                // Twin tanks under the body, joined by a hose to the nozzle.
                foreach (float side in new[] { -1f, 1f })
                    Add(PartShape.Capsule, new Vector3(side * 0.03f, top - h * 0.5f - 0.05f, back - len * 0.45f), new Vector3(0.027f, 0.2f, 0f), new Vector3(90f, 0f, 0f), 0x8a3a2a, Role.Tank, side > 0 ? 1 : 0);
                Add(PartShape.Cylinder, new Vector3(0f, top - h * 0.5f - 0.02f, front + 0.02f), new Vector3(0.006f, len * 0.5f, 0.006f), new Vector3(90f, 0f, 0f), p.Dark, Role.Tank, 2);
                break;
        }

        // ---- Stock ----
        float sz = back;
        switch (l.Stock)
        {
            case "wire":
                foreach (float yy in new[] { top + h * 0.2f, top - h * 0.35f })
                    Add(PartShape.Cylinder, new Vector3(0f, yy, sz + 0.11f), new Vector3(0.005f, 0.22f, 0.005f), new Vector3(90f, 0f, 0f), p.Main, Role.Stock);
                Add(PartShape.Box, new Vector3(0f, top - h * 0.08f, sz + 0.22f), new Vector3(0.03f, h * 1.25f, 0.012f), Vector3.Zero, p.Main, Role.Stock, 1);
                break;
            case "solid":
                Add(PartShape.Box, new Vector3(0f, top - h * 0.12f, sz + 0.12f), new Vector3(w * 0.9f, h * 1.1f, 0.24f), new Vector3(-5f, 0f, 0f), l.Band == Band.Low ? p.Trim : p.Dark, Role.Stock);
                Add(PartShape.Box, new Vector3(0f, top - h * 0.2f, sz + 0.245f), new Vector3(w * 0.95f, h * 1.3f, 0.014f), Vector3.Zero, p.Dark, Role.Stock, 1);
                break;
            case "skeleton":
                Add(PartShape.Box, new Vector3(0f, top + h * 0.25f, sz + 0.1f), new Vector3(0.012f, 0.012f, 0.2f), Vector3.Zero, p.Main, Role.Stock);
                Add(PartShape.Box, new Vector3(0f, top - h * 0.35f, sz + 0.09f), new Vector3(0.012f, 0.012f, 0.18f), new Vector3(8f, 0f, 0f), p.Main, Role.Stock, 1);
                Add(PartShape.Box, new Vector3(0f, top - h * 0.05f, sz + 0.2f), new Vector3(0.028f, h * 1.3f, 0.016f), Vector3.Zero, p.Dark, Role.Stock, 2);
                break;
            case "brace":
                Add(PartShape.Box, new Vector3(0f, top - h * 0.1f, sz + 0.07f), new Vector3(w * 0.8f, h * 0.7f, 0.14f), Vector3.Zero, p.Dark, Role.Stock);
                break;
        }

        // ---- Sights ----
        float sy = top + h * 0.5f + 0.004f;
        if (tube) sy = by + br + 0.004f;
        switch (l.Sight)
        {
            case "post":
                Add(PartShape.Box, new Vector3(0f, sy + 0.008f, front + 0.012f), new Vector3(0.004f, 0.016f, 0.006f), Vector3.Zero, p.Dark, Role.Sight);
                Add(PartShape.Box, new Vector3(0f, sy + 0.006f, back - 0.01f), new Vector3(0.018f, 0.012f, 0.006f), Vector3.Zero, p.Dark, Role.Sight, 1);
                break;
            case "rail":
                Add(PartShape.Box, new Vector3(0f, sy + 0.004f, (front + back) * 0.5f), new Vector3(0.016f, 0.008f, len * 0.8f), Vector3.Zero, p.Dark, Role.Sight);
                Add(PartShape.Box, new Vector3(0f, sy + 0.016f, back - 0.03f), new Vector3(0.022f, 0.018f, 0.03f), Vector3.Zero, p.Dark, Role.Sight, 1);
                break;
            case "scope":
                Add(PartShape.Cylinder, new Vector3(0f, sy + 0.022f, (front + back) * 0.5f), new Vector3(0.014f, len * 0.55f, 0.014f), new Vector3(90f, 0f, 0f), p.Dark, Role.Sight);
                Add(PartShape.Cylinder, new Vector3(0f, sy + 0.022f, (front + back) * 0.5f - len * 0.28f), new Vector3(0.012f, 0.003f, 0.012f), new Vector3(90f, 0f, 0f), 0x3aa6ff, Role.Sight, 1, Finish.Emitter);
                break;
            case "sensor":
                Add(PartShape.Sphere, new Vector3(0f, sy + 0.008f, front + len * 0.2f), new Vector3(0.014f, 0.016f, 0f), Vector3.Zero, p.Glow, Role.Sight, 0, Finish.Emitter);
                Add(PartShape.Box, new Vector3(0f, sy + 0.002f, front + len * 0.2f), new Vector3(0.03f, 0.006f, 0.03f), Vector3.Zero, p.Dark, Role.Sight, 1);
                break;
            case "side":
                // A launcher's sight hangs off the left of the tube.
                Add(PartShape.Box, new Vector3(-br - 0.02f, by + br * 0.4f, front + 0.02f), new Vector3(0.03f, 0.04f, 0.05f), Vector3.Zero, p.Dark, Role.Sight);
                Add(PartShape.Box, new Vector3(-br - 0.02f, by + br * 0.4f + 0.012f, front - 0.004f), new Vector3(0.016f, 0.012f, 0.004f), Vector3.Zero, 0xff5a3a, Role.Sight, 1, Finish.Emitter);
                break;
        }

        // ---- Vents and fins ----
        for (int i = 0; i < l.Vents; i++)
        {
            float z = front + len * (0.15f + 0.5f * i / Math.Max(1, l.Vents));
            foreach (float side in new[] { -1f, 1f })
                Add(PartShape.Box, new Vector3(side * (w * 0.5f + 0.002f), top + h * 0.12f, z), new Vector3(0.004f, h * 0.45f, 0.008f), Vector3.Zero, l.Band == Band.High ? p.Glow : 0x15171a,
                    Role.Vent, i, l.Band == Band.High ? Finish.Emitter : Finish.Worn);
        }
        for (int i = 0; i < l.Fins; i++)
        {
            float a = i * MathF.Tau / l.Fins;
            var off = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
            float r = tube ? br : br * 1.6f;
            Add(PartShape.Box, new Vector3(off.X * (r + 0.012f), by + off.Y * (r + 0.012f), (tube ? muzzleZ + bl + len * 0.6f - 0.06f : front - bl * 0.4f)),
                new Vector3(MathF.Abs(off.Y) > 0.5f ? 0.004f : 0.024f, MathF.Abs(off.Y) > 0.5f ? 0.024f : 0.004f, 0.06f), Vector3.Zero, p.Dark, Role.Fin, i);
        }
        float length = (back + (l.Stock == "none" ? 0f : 0.24f)) - muzzleZ;
        return new GunModelSpec(parts, new Vector3(0f, by, muzzleZ - (l.Emitter == "prongs" ? 0.08f : 0.005f)), length, l.Band);
    }

    /// <summary>A living weapon: a grown body of sacs and ribs on a chitin brace, with a mouth where the barrel would be.</summary>
    static GunModelSpec Grown(WeaponDef def, LookSpec l, Palette p, List<LookPart> parts, float cal, uint seed)
    {
        void Add(PartShape s, Vector3 at, Vector3 size, Vector3 turn, uint c, Role role, int index = 0, Finish f = Finish.Worn) =>
            parts.Add(new LookPart(s, at, size, turn, c, f, p.Wear, p.Rough, p.Metal, role, index));
        float len = MathF.Max(0.16f, l.Body), r = 0.03f + 0.05f * cal;
        float y = 0.045f + r * 0.6f;
        // The chitin brace along the forearm, and the grip it grows round.
        Add(PartShape.Box, new Vector3(0f, y - r * 0.9f, len * 0.15f), new Vector3(0.05f, 0.016f, len * 0.9f), new Vector3(-4f, 0f, 0f), p.Dark, Role.Brace);
        Add(PartShape.Capsule, new Vector3(0f, -0.04f, 0.01f), new Vector3(0.022f, 0.1f, 0f), new Vector3(16f, 0f, 0f), p.Dark, Role.Grip);
        // The body: a tube of flesh, ringed with ribs.
        bool pouch = l.Mag == "sac" && def.Swarm != null;
        if (pouch)
        {
            // A warm pouch on the arm, its skin pulsing over a glow.
            Add(PartShape.Sphere, new Vector3(0f, y + r * 0.2f, len * 0.05f), new Vector3(r * 1.5f, r * 2.6f, 0f), Vector3.Zero, p.Main, Role.Sac);
            Add(PartShape.Sphere, new Vector3(r * 0.6f, y + r * 0.6f, len * 0.05f), new Vector3(r * 0.6f, r * 1.1f, 0f), Vector3.Zero, p.Glow, Role.Sac, 1, Finish.Emitter);
            Add(PartShape.Sphere, new Vector3(-r * 0.7f, y + r * 0.3f, len * 0.15f), new Vector3(r * 0.45f, r * 0.8f, 0f), Vector3.Zero, p.Glow, Role.Sac, 2, Finish.Emitter);
            for (int i = 0; i < 4; i++)
                Add(PartShape.Torus, new Vector3(0f, y + r * 0.2f, len * 0.05f + (i - 1.5f) * r * 0.6f), new Vector3(r * 1.35f, r * 1.6f, 0f), new Vector3(90f, 0f, 0f), p.Trim, Role.Rib, i);
            // A short neck and the opening the hornets leave by.
            Add(PartShape.Cylinder, new Vector3(0f, y + r * 0.2f, -len * 0.3f), new Vector3(r * 0.45f, len * 0.35f, r * 0.7f), new Vector3(90f, 0f, 0f), p.Main, Role.Barrel);
            Add(PartShape.Torus, new Vector3(0f, y + r * 0.2f, -len * 0.48f), new Vector3(r * 0.3f, r * 0.62f, 0f), new Vector3(90f, 0f, 0f), p.Dark, Role.Muzzle);
            return new GunModelSpec(parts, new Vector3(0f, y + r * 0.2f, -len * 0.5f), len * 1.1f, Band.Living);
        }
        // A fat ribbed gullet with a bulb behind and a puckered mouth in front.
        Add(PartShape.Cylinder, new Vector3(0f, y, -len * 0.15f), new Vector3(r * 0.85f, len * 1.0f, r), new Vector3(90f, 0f, 0f), p.Main, Role.Barrel);
        int ribs = 5 + (int)(seed % 3);
        for (int i = 0; i < ribs; i++)
            Add(PartShape.Torus, new Vector3(0f, y, -len * 0.6f + i * len * 0.9f / ribs), new Vector3(r * 0.86f, r * 1.12f, 0f), new Vector3(90f, 0f, 0f), p.Trim, Role.Rib, i);
        Add(PartShape.Sphere, new Vector3(0f, y + r * 0.1f, len * 0.42f), new Vector3(r * 1.35f, r * 2.4f, 0f), Vector3.Zero, p.Main, Role.Sac);
        Add(PartShape.Sphere, new Vector3(0f, y + r * 0.9f, len * 0.42f), new Vector3(r * 0.5f, r * 0.8f, 0f), Vector3.Zero, p.Glow, Role.Sac, 1, Finish.Emitter);
        Add(PartShape.Torus, new Vector3(0f, y, -len * 0.66f), new Vector3(r * 0.42f, r * 0.95f, 0f), new Vector3(90f, 0f, 0f), p.Dark, Role.Muzzle);
        return new GunModelSpec(parts, new Vector3(0f, y, -len * 0.68f), len * 1.4f, Band.Living);
    }

    /// <summary>A placed weapon is the thing itself: a squat puck with a band and a red eye.</summary>
    static GunModelSpec Puck(WeaponDef def, Palette p, List<LookPart> parts)
    {
        void Add(PartShape s, Vector3 at, Vector3 size, Vector3 turn, uint c, Role role, int index = 0, Finish f = Finish.Worn) =>
            parts.Add(new LookPart(s, at, size, turn, c, f, p.Wear, p.Rough, p.Metal, role, index));
        Add(PartShape.Cylinder, new Vector3(0f, 0.02f, -0.06f), new Vector3(0.06f, 0.035f, 0.066f), Vector3.Zero, p.Main, Role.Receiver);
        Add(PartShape.Torus, new Vector3(0f, 0.02f, -0.06f), new Vector3(0.062f, 0.07f, 0f), Vector3.Zero, p.Dark, Role.Ring);
        Add(PartShape.Cylinder, new Vector3(0f, 0.04f, -0.06f), new Vector3(0.03f, 0.01f, 0.036f), Vector3.Zero, p.Dark, Role.Receiver, 1);
        Add(PartShape.Sphere, new Vector3(0f, 0.046f, -0.06f), new Vector3(0.011f, 0.012f, 0f), Vector3.Zero, 0xff2a1a, Role.Eye, 0, Finish.Emitter);
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.Tau / 4f;
            Add(PartShape.Box, new Vector3(MathF.Cos(a) * 0.05f, 0.002f, -0.06f + MathF.Sin(a) * 0.05f), new Vector3(0.014f, 0.006f, 0.014f), Vector3.Zero, p.Dark, Role.Fin, i);
        }
        return new GunModelSpec(parts, new Vector3(0f, 0.03f, -0.13f), 0.14f, def.Look.Band);
    }

    static uint Hash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h;
    }
}
