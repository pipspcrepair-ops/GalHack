using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Plants;

/// <summary>The paints of one species. Tinted paints (alpha 1) multiply the instance's tint, so 0.8 is its plain colour.</summary>
internal sealed class Paints
{
    public Vector4 Top, Mid, Under, Bark, BarkLow, Petal, Heart, Stalk, Gill, Tip;
}

/// <summary>What a form tells the species about the body it built.</summary>
internal sealed class Anatomy
{
    public Vector3 StemTop = Vector3.UnitY;
    public float StemRadius = 0.3f, Foot = 0.4f;
}

/// <summary>
/// Turns a genome into triangles. Each form is a body plan, chunky and deliberate: a real stem into
/// the ground, a clear silhouette, and a part that says what it is. Stems start a metre under their
/// foot, so a trunk meets the ground whatever the slope. <paramref name="far"/> builds the same plant
/// with far fewer triangles for the distance.
/// </summary>
internal static class PlantForms
{
    const float Under = -1f;

    public static void Build(Plant p, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        Genome g = p.Genome;
        switch (p.Form)
        {
            case PlantForm.Spire: Spire(g, s, d, c, far, a, false); break;
            case PlantForm.Pagoda: Spire(g, s, d, c, far, a, true); break;
            case PlantForm.Spindle: Spindle(g, s, d, c, far, a); break;
            case PlantForm.Stacked: Stacked(g, s, d, c, far, a); break;
            case PlantForm.Domed: Domed(g, s, d, c, far, a); break;
            case PlantForm.Umbrella: Umbrella(g, s, d, c, far, a); break;
            case PlantForm.Weeping: Weeping(g, s, d, c, far, a); break;
            case PlantForm.CloudPads: CloudPads(g, s, d, c, far, a); break;
            case PlantForm.Bottle: Bottle(g, s, d, c, far, a); break;
            case PlantForm.Pole: Pole(g, s, d, c, far, a); break;
            case PlantForm.Palm: Palm(g, s, d, c, far, a); break;
            case PlantForm.FanPalm: FanPalm(g, s, d, c, far, a); break;
            case PlantForm.SpikeTree: SpikeTree(g, s, d, c, far, a); break;
            case PlantForm.Antler: Antler(g, s, d, c, far, a); break;
            case PlantForm.Lantern: Lantern(g, s, d, c, far, a); break;
            case PlantForm.Snag: Snag(g, s, d, c, far, a); break;
            case PlantForm.Capped: Capped(g, s, d, c, far, a); break;
            case PlantForm.Parasol: Parasol(g, s, d, c, far, a); break;
            case PlantForm.Trumpet: Trumpet(g, s, d, c, far, a); break;
            case PlantForm.Organ: Organ(g, s, d, c, far, a); break;
            case PlantForm.Candelabra: Candelabra(g, s, d, c, far, a); break;
            case PlantForm.Barrel: Barrel(g, s, d, c, far, a); break;
            case PlantForm.Whips: Whips(g, s, d, c, far, a); break;
            case PlantForm.Paddles: Paddles(g, s, d, c, far, a); break;
            case PlantForm.Bush: Bush(g, s, d, c, a); break;
            case PlantForm.Fern: Fern(g, s, d, c, a); break;
            case PlantForm.Thorn: Thorn(g, s, d, c, a); break;
            case PlantForm.Rosette: Rosette(g, s, d, c, far, a); break;
            case PlantForm.Shards: Shards(g, s, d, c, far, a); break;
            case PlantForm.Tussock: Tussock(g, s, d, c); break;
            case PlantForm.Flowers: Flowers(g, s, d, c); break;
            case PlantForm.Twigs: Twigs(g, s, d, c); break;
            case PlantForm.Reeds: Reeds(g, s, d, c); break;
            case PlantForm.Lichen: Lichen(g, s, d, c); break;
            case PlantForm.PinStalks: PinStalks(g, s, d, c); break;
            case PlantForm.GrainCrop: GrainCrop(g, s, d, c); break;
            case PlantForm.LeafCrop: LeafCrop(g, s, d, c); break;
            case PlantForm.VineCrop: VineCrop(g, s, d, c); break;
        }
        if (PlantAudit.Joined(p.Form)) s.Weld(d, c.Bark, MathF.Min(g.TrunkRadius * 0.5f, 0.3f), PlantAudit.Crowned(p.Form) ? a.StemTop : null);
    }

    static Vector3 Ring(float angle, float radius, float y = 0f) => new(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius);

    /// <summary>A trunk: flared into the ground, tapering, leaning and bending by the genome.</summary>
    static (Vector3[] Path, float[] Radii) Trunk(Genome g, Dice d, float height, float radius, float top, int nodes, Anatomy a)
    {
        var path = new Vector3[nodes + 2];
        var radii = new float[nodes + 2];
        Vector3 lean = new(MathF.Cos(g.LeanAngle), 0f, MathF.Sin(g.LeanAngle));
        path[0] = new Vector3(0f, Under, 0f);
        radii[0] = radius * g.Flare;
        for (int i = 0; i <= nodes; i++)
        {
            float t = i / (float)nodes, y = MathF.Max(0.1f, height * t);
            Vector3 wander = i > 0 ? new Vector3(d.Range(-1f, 1f), 0f, d.Range(-1f, 1f)) * height * 0.012f : Vector3.Zero;
            path[i + 1] = new Vector3(0f, y, 0f) + lean * height * (g.Lean * t + g.Bend * t * t) + wander;
            radii[i + 1] = i == 0 ? radius * g.Flare : radius + (top - radius) * MathF.Pow(t, 0.8f);
        }
        a.StemTop = path[^1];
        a.StemRadius = (radius + top) * 0.5f;
        a.Foot = MathF.Max(a.Foot, radius * MathF.Min(g.Flare, 1.5f));
        return (path, radii);
    }

    static Vector3 Along(Vector3[] path, float t)
    {
        float f = Math.Clamp(t, 0f, 1f) * (path.Length - 2);
        int i = Math.Min((int)f, path.Length - 3);
        return Vector3.Lerp(path[i + 1], path[i + 2], f - i);
    }

    /// <summary>A limb rising from a point to a point, with a kick in the middle.</summary>
    static void Limb(Sculptor s, Dice d, Vector3 from, Vector3 to, float radius, float tip, int sides, Vector4 low, Vector4 high)
    {
        Vector3 mid = Vector3.Lerp(from, to, 0.5f) + new Vector3(d.Range(-1f, 1f), d.Range(0f, 1.4f), d.Range(-1f, 1f)) * (to - from).Length() * 0.09f;
        s.Tube(new[] { from - Vector3.Normalize(to - from) * radius, mid, to }, new[] { radius, (radius + tip) * 0.5f, tip }, sides, low, high);
    }

    // ---- Needle and stacked forms -------------------------------------------------------------

    /// <summary>Tiers of toothed skirts on a straight trunk; a pagoda's are flat and far apart.</summary>
    static void Spire(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a, bool pagoda)
    {
        float H = g.Height, R = H * g.Crown;
        int tiers = far ? Math.Max(2, g.Layers / 2 + 1) : g.Layers, sides = far ? 5 : 9;
        var (path, radii) = Trunk(g, d, H * 0.96f, g.TrunkRadius, g.TrunkRadius * 0.3f, far ? 2 : 3, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        float first = H * g.Stem, span = H - first;
        for (int i = 0; i < tiers; i++)
        {
            float t = i / (float)tiers;
            float y = first + span * MathF.Pow(t, 1.08f);
            float r = R * MathF.Pow(1f - t * (1f - g.Taper), 1.25f);
            float h = pagoda ? span / tiers * (0.42f + 0.25f * g.Flat) : span / tiers * (1.5f + g.Flat * 0.8f);
            Vector3 at = Along(path, (y - 0f) / (H * 0.96f)) with { Y = y };
            Vector4 hem = Vector4.Lerp(c.Mid, c.Top, t * 0.7f);
            s.Skirt(at, r, h, sides, far ? 0f : g.Jag, g.Droop * (pagoda ? 0.5f : 1f), d, Vector4.Lerp(c.Mid, c.Top, 0.4f + t * 0.6f), hem, c.Under);
        }
        s.Skirt(Along(path, 1f) with { Y = first + span * 0.93f }, R * g.Taper * 0.5f + 0.25f, span * 0.14f, far ? 4 : 5, 0f, 0f, d, c.Top, c.Top, c.Under);
    }

    /// <summary>A tall, narrow flame of billowing masses up a stem that shows at the foot.</summary>
    static void Spindle(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, R = H * g.Crown * 0.46f, first = H * MathF.Max(g.Stem * 0.5f, 0.1f);
        var (path, radii) = Trunk(g, d, H * 0.8f, g.TrunkRadius, g.TrunkRadius * 0.4f, far ? 2 : 3, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        int masses = far ? 2 : Math.Clamp(g.Layers, 3, 5);
        float span = H - first;
        for (int i = 0; i < masses; i++)
        {
            float t = (i + 0.5f) / masses;
            // Widest a third of the way up, narrowing to the tip.
            float r = R * (t < 0.34f ? 0.8f + 0.6f * t : 1.25f - 0.95f * t) * d.Range(0.9f, 1.1f) * (far ? 1.15f : 1f);
            float tall = span / masses * (far ? 0.72f : 0.86f);
            Vector3 at = Along(path, (first + span * t) / (H * 0.8f)) with { Y = first + span * t } + Ring(d.Range(0f, MathF.Tau), R * 0.14f);
            float shade = d.Range(0.86f, 1.04f);
            s.Lump(at, new Vector3(r, tall, r), d, c.Top * new Vector4(shade, shade, shade, 1f), c.Under, 0.24f, 0.8f);
        }
    }

    /// <summary>A central column threaded through lens-shaped layers, each turned a little off the last.</summary>
    static void Stacked(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, R = H * g.Crown * 0.8f;
        var (path, radii) = Trunk(g, d, H, g.TrunkRadius, g.TrunkRadius * 0.45f, far ? 2 : 3, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        int layers = far ? Math.Max(2, g.Layers / 2) : g.Layers;
        float first = H * g.Stem;
        for (int i = 0; i < layers; i++)
        {
            float t = (i + 0.5f) / layers;
            float y = first + (H - first) * t;
            float r = R * (1f - 0.75f * MathF.Pow(t, 1.5f)) * (1f + d.Range(-0.1f, 0.1f));
            float th = MathF.Max(0.25f, (H - first) / layers * (0.3f + 0.3f * g.Flat));
            Vector3 at = Along(path, y / H) with { Y = y } + Ring(d.Range(0f, MathF.Tau), r * 0.12f);
            (float, float)[] p = { (0.02f, -th * 0.35f), (r * 0.8f, -th * 0.4f), (r, -th * 0.1f), (r * 0.6f, th * 0.45f), (0.02f, th * 0.6f) };
            s.Lathe(at, Vector3.UnitY + Ring(d.Range(0f, MathF.Tau), 0.08f), p, far ? 5 : 7, b => b < 2 ? c.Under : b == 2 ? c.Mid : c.Top, 0f, 0.09f, d);
        }
    }

    // ---- Crowned trees ------------------------------------------------------------------------

    /// <summary>A trunk forked into limbs, each carrying a faceted mass of leaves: the broad, rounded crown.</summary>
    static void Domed(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, stemH = H * g.Stem, R = H * g.Crown;
        var (path, radii) = Trunk(g, d, stemH, g.TrunkRadius, g.TrunkRadius * 0.68f, far ? 1 : 2, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        Vector3 fork = a.StemTop;
        float crownH = H - stemH, y = stemH + crownH * 0.42f;
        int lobes = far ? 3 : g.Lobes;
        float phase = d.Range(0f, MathF.Tau);
        for (int i = 0; i < lobes; i++)
        {
            float angle = phase + i * MathF.Tau / lobes + d.Range(-0.3f, 0.3f);
            float out_ = R * d.Range(0.5f, 0.74f);
            Vector3 centre = new Vector3(fork.X, y + crownH * d.Range(-0.2f, 0.1f), fork.Z) + Ring(angle, out_);
            float r = R * (far ? 0.7f : d.Range(0.34f, 0.5f));
            if (i < g.Forks || far) Limb(s, d, fork, centre, g.TrunkRadius * 0.55f, g.TrunkRadius * 0.2f, far ? 3 : 4, c.Bark, c.Bark);
            float shade = d.Range(0.86f, 1.04f);
            s.Lump(centre, new Vector3(r, r * g.Flat, r) * d.Range(0.9f, 1.15f), d, c.Top * new Vector4(shade, shade, shade, 1f), c.Under, 0.26f + g.Jag * 0.16f);
        }
        // The upper billow, shouldering out of the lower one.
        int upper = far ? 1 : Math.Max(2, g.Lobes / 2);
        for (int i = 0; i < upper; i++)
        {
            float r = R * (far ? 0.72f : d.Range(0.36f, 0.5f));
            Vector3 centre = new Vector3(fork.X, H - r * g.Flat * 0.96f - d.Range(0f, 0.12f) * crownH, fork.Z) + Ring(phase + 0.9f + i * MathF.Tau / upper, far ? 0f : R * d.Range(0.14f, 0.34f));
            s.Lump(centre, new Vector3(r, r * g.Flat, r), d, c.Top, c.Mid, 0.26f);
        }
    }

    /// <summary>Limbs fanning up from a bare trunk to one or more wide, flat canopies.</summary>
    static void Umbrella(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, stemH = H * g.Stem, R = H * g.Crown * 1.25f;
        var (path, radii) = Trunk(g, d, stemH, g.TrunkRadius, g.TrunkRadius * 0.62f, far ? 1 : 2, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        Vector3 fork = a.StemTop;
        float th = MathF.Max(0.55f, (H - stemH) * (0.22f + 0.2f * g.Flat));
        float deckY = H - th;
        int n = far ? 3 : g.Forks + 1, pads = far ? 1 : g.Lobes > 4 ? 3 : g.Lobes > 3 ? 2 : 1;
        float phase = d.Range(0f, MathF.Tau);
        for (int i = 0; i < n; i++)
        {
            Vector3 to = new Vector3(fork.X, deckY - th * 0.1f, fork.Z) + Ring(phase + i * MathF.Tau / n, R * d.Range(0.45f, 0.68f));
            Limb(s, d, fork, to, g.TrunkRadius * 0.5f, g.TrunkRadius * 0.16f, far ? 3 : 4, c.Bark, c.Bark);
        }
        for (int i = 0; i < pads; i++)
        {
            float r = R * (pads == 1 ? 1f : i == 0 ? 0.82f : 0.6f);
            Vector3 at = new Vector3(fork.X, deckY + (pads == 1 ? 0f : d.Range(-0.15f, 0.25f) * th), fork.Z) + (i == 0 && pads > 1 ? Vector3.Zero : Ring(phase + i * 2.3f, pads == 1 ? 0f : R * 0.52f));
            (float, float)[] p = { (0.02f, th * 0.02f), (r * 0.76f, 0f), (r, th * 0.42f), (r * 0.6f, th * 0.9f), (0.02f, th * 1.02f) };
            s.Lathe(at, Vector3.UnitY, p, far ? 6 : 8, b => b < 2 ? c.Under : b == 2 ? c.Mid : c.Top, 0f, far ? 0.04f : 0.09f + g.Jag * 0.14f, d, 1f - g.Taper * 0.2f);
        }
    }

    /// <summary>A dome with long curtains of leaves hanging from its rim nearly to the ground.</summary>
    static void Weeping(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, stemH = H * g.Stem, R = H * g.Crown;
        float crown = H - stemH, centre = stemH + crown * 0.45f;
        // The trunk runs up into the dome (its top inside the lumps), so the crown never hangs above a bare stump.
        var (path, radii) = Trunk(g, d, stemH + crown * 0.34f, g.TrunkRadius, g.TrunkRadius * 0.55f, far ? 2 : 3, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        Vector3 top = a.StemTop;
        float phase = d.Range(0f, MathF.Tau);
        for (int i = 0; i < (far ? 0 : 3); i++)
        {
            float r = R * 0.6f;
            Vector3 lobe = new Vector3(top.X, centre + crown * d.Range(-0.05f, 0.14f), top.Z) + Ring(phase + i * MathF.Tau / 3f, R * 0.42f);
            // A limb from the trunk's top to the middle of each lobe.
            Limb(s, d, top, lobe, g.TrunkRadius * 0.5f, g.TrunkRadius * 0.2f, 4, c.Bark, c.Bark);
            s.Lump(lobe, new Vector3(r, crown * 0.42f, r), d, c.Top, c.Mid, 0.2f, 0.8f);
        }
        s.Lump(new Vector3(top.X, centre + crown * (far ? 0f : 0.2f), top.Z), new Vector3(R * (far ? 1f : 0.7f), crown * (far ? 0.55f : 0.42f), R * (far ? 1f : 0.7f)), d, c.Top, c.Mid, 0.16f, 0.7f);
        int strips = far ? 6 : g.Fronds;
        float width = MathF.Tau * R / strips * 0.86f; // gaps between them: curtains, not a wall
        Vector4 fade = Vector4.Lerp(c.Mid, c.Under, 0.55f);
        for (int i = 0; i < strips; i++)
        {
            float angle = phase + i * MathF.Tau / strips + d.Range(-0.08f, 0.08f);
            Vector3 dir = Ring(angle, 1f);
            Vector3 side = Ring(angle + MathF.PI / 2f, width * 0.5f);
            Vector3 hang = new Vector3(top.X, centre + crown * 0.16f, top.Z) + dir * R * 0.62f; // the curtain's head is inside the dome
            float fall = (hang.Y - H * (0.1f + g.Droop * 0.3f)) * d.Range(0.5f, 1f);
            Vector3 mid = hang + dir * R * 0.36f + Vector3.UnitY * -fall * 0.5f;
            Vector3 low = hang + dir * R * 0.34f + Vector3.UnitY * -fall;
            Vector3 outward = dir + Vector3.UnitY * 0.1f;
            s.Quad(hang - side, hang + side, mid + side * 0.92f, mid - side * 0.92f, outward, c.Mid, c.Mid, fade, fade);
            s.Quad(mid - side * 0.92f, mid + side * 0.92f, low + side * 0.4f, low - side * 0.4f, outward, fade, fade, c.Under, c.Under);
        }
    }

    /// <summary>A crooked trunk holding flat pads of foliage out on side limbs.</summary>
    static void CloudPads(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, R = H * g.Crown * 0.72f;
        var (path, radii) = Trunk(g, d, H * 0.86f, g.TrunkRadius, g.TrunkRadius * 0.4f, far ? 2 : 4, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        int pads = far ? 3 : g.Lobes + 1;
        float phase = d.Range(0f, MathF.Tau);
        for (int i = 0; i < pads; i++)
        {
            float t = i / (pads - 1f), y = H * (g.Stem + (0.82f - g.Stem) * t);
            Vector3 on = Along(path, y / (H * 0.86f));
            float r = R * (1f - 0.55f * t) * d.Range(0.85f, 1.1f);
            Vector3 pad = on + Ring(phase + i * 2.4f, i == pads - 1 ? 0f : R * (1.05f - 0.5f * t)) + Vector3.UnitY * R * 0.16f;
            if (i < pads - 1) Limb(s, d, on, pad, g.TrunkRadius * 0.34f, g.TrunkRadius * 0.14f, 3, c.Bark, c.Bark);
            s.Lump(pad, new Vector3(r, r * (0.3f + g.Flat * 0.2f), r), d, c.Top, c.Under, 0.14f, 0.45f);
        }
    }

    /// <summary>A swollen water-storing trunk and a small, thin crown.</summary>
    static void Bottle(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, stemH = H * MathF.Max(g.Stem, 0.55f), r = g.TrunkRadius * 2.2f;
        (float, float)[] p = far
            ? new[] { (r * 0.9f, Under), (r, stemH * 0.3f), (r * 0.45f, stemH * 0.9f), (r * 0.2f, stemH) }
            : new[] { (r * 0.95f, Under), (r * 1.02f, stemH * 0.14f), (r * 0.96f, stemH * 0.4f), (r * 0.66f, stemH * 0.66f), (r * 0.34f, stemH * 0.88f), (r * 0.2f, stemH) };
        int rows = p.Length;
        s.Lathe(Vector3.Zero, Vector3.UnitY, p, far ? 5 : g.Sides + 1, i => Vector4.Lerp(c.BarkLow, c.Bark, i / (rows - 1f)), far ? 0f : 0.04f, 0.03f, d);
        a.StemTop = new Vector3(0f, stemH, 0f);
        a.StemRadius = r * 0.72f;
        a.Foot = r * 0.9f;
        int n = far ? 3 : g.Forks + 2;
        float R = H * g.Crown, phase = d.Range(0f, MathF.Tau);
        for (int i = 0; i < n; i++)
        {
            Vector3 tip = a.StemTop + Ring(phase + i * MathF.Tau / n, R * d.Range(0.65f, 1f), (H - stemH) * d.Range(0.55f, 0.85f));
            Limb(s, d, a.StemTop, tip, r * 0.14f, r * 0.05f, 3, c.Bark, c.Bark);
            float pad = R * d.Range(0.34f, 0.46f);
            s.Lump(tip, new Vector3(pad, pad * 0.5f * (0.6f + g.Flat), pad), d, c.Top, c.Under, 0.2f);
        }
    }

    /// <summary>A tall scaled pole that forks only at the very top into hanging tufts.</summary>
    static void Pole(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, stemH = H * MathF.Max(g.Stem, 0.62f);
        var (path, radii) = Trunk(g, d, stemH, g.TrunkRadius, g.TrunkRadius * 0.7f, far ? 2 : 3, a);
        // Ring scars up the pole: bands of a lighter bark.
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        int forks = 2 + (g.Forks > 2 ? 1 : 0);
        float R = H * g.Crown * 0.8f, phase = d.Range(0f, MathF.Tau);
        Vector3 top = a.StemTop;
        for (int i = 0; i < forks; i++)
        {
            float angle = phase + i * MathF.Tau / forks;
            Vector3 mid = top + Ring(angle, R * 0.5f, (H - stemH) * 0.45f);
            Limb(s, d, top, mid, g.TrunkRadius * 0.55f, g.TrunkRadius * 0.34f, far ? 3 : 4, c.Bark, c.Bark);
            if (far)
            {
                s.Skirt(mid + Vector3.UnitY * -R * 0.05f, R * 0.74f, R * 0.7f, 5, 0f, 0.2f, d, c.Top, c.Mid, c.Under);
                continue;
            }
            for (int j = 0; j < 2; j++)
            {
                float spread = angle + (j == 0 ? -0.62f : 0.62f);
                Vector3 tip = mid + Ring(spread, R * 0.52f, (H - stemH) * 0.42f);
                Limb(s, d, mid, tip, g.TrunkRadius * 0.34f, g.TrunkRadius * 0.14f, 3, c.Bark, c.Bark);
                int leaves = Math.Max(5, g.Fronds / 2);
                for (int k = 0; k < leaves; k++)
                {
                    Vector3 dir = Ring(k * MathF.Tau / leaves + d.Range(-0.2f, 0.2f), 1f, 0.55f);
                    s.Blade(tip, dir, g.FrondLength * 0.62f, g.FrondWidth * 0.8f, 0.5f + g.Arch * 0.7f, 2, c.Mid, c.Under);
                }
            }
        }
    }

    // ---- Fronded forms ------------------------------------------------------------------------

    /// <summary>A slender ringed stem with a head of long, arching, folded fronds.</summary>
    static void Palm(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height;
        var (path, radii) = Trunk(g, d, H * 0.92f, g.TrunkRadius, g.TrunkRadius * 0.62f, far ? 2 : 4, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        Vector3 top = a.StemTop;
        int n = far ? 5 : g.Fronds;
        float phase = d.Range(0f, MathF.Tau);
        for (int i = 0; i < n; i++)
        {
            float angle = phase + i * 2.399f;
            Vector3 dir = Ring(angle, 1f, d.Range(0.25f, 0.85f) - g.Droop * 0.3f);
            float length = g.FrondLength * d.Range(0.82f, 1.1f);
            s.Blade(top, dir, length, g.FrondWidth * (far ? 1.5f : 1f), g.Arch, far ? 2 : 3, c.Top, c.Mid, far ? 0f : 0.45f, 0.3f);
        }
    }

    /// <summary>Great paddle leaves spread in one flat fan on a short stem.</summary>
    static void FanPalm(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, stemH = H * g.Stem * 0.7f;
        var (path, radii) = Trunk(g, d, stemH, g.TrunkRadius, g.TrunkRadius * 0.78f, far ? 1 : 2, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        Vector3 top = a.StemTop;
        int n = far ? 5 : g.Fronds;
        float facing = d.Range(0f, MathF.Tau), reach = H - stemH;
        Vector3 across = Ring(facing, 1f);
        for (int i = 0; i < n; i++)
        {
            float t = n > 1 ? i / (n - 1f) : 0.5f, tilt = (t - 0.5f) * 2.5f;
            Vector3 dir = Vector3.UnitY * MathF.Cos(tilt) + across * MathF.Sin(tilt) + Ring(facing + MathF.PI / 2f, d.Range(-0.08f, 0.08f));
            float length = reach * (1f - 0.22f * MathF.Abs(tilt) / 1.25f) * d.Range(0.92f, 1.04f);
            // A long bare stalk, then the paddle.
            Vector3 blade = top + dir * length * 0.42f;
            s.Tube(new[] { top - dir * 0.2f, blade }, new[] { g.TrunkRadius * 0.24f, g.TrunkRadius * 0.14f }, 3, c.Stalk, c.Stalk, false);
            s.Blade(blade, dir, length * 0.62f, g.FrondWidth * 2.1f * (far ? 1.3f : 1f), g.Arch * 0.2f, far ? 1 : 2, c.Mid, c.Top, far ? 0f : 0.2f, 0.42f);
        }
    }

    /// <summary>A forked woody stem, every tip a stiff burst of spikes.</summary>
    static void SpikeTree(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, stemH = H * g.Stem;
        var (path, radii) = Trunk(g, d, stemH, g.TrunkRadius, g.TrunkRadius * 0.7f, far ? 1 : 2, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        Vector3 fork = a.StemTop;
        int n = far ? 3 : g.Forks + 2;
        float R = H * g.Crown, phase = d.Range(0f, MathF.Tau);
        for (int i = 0; i < n; i++)
        {
            float angle = phase + i * MathF.Tau / n + d.Range(-0.25f, 0.25f);
            Vector3 elbow = fork + Ring(angle, R * d.Range(0.45f, 0.7f), (H - stemH) * d.Range(0.1f, 0.25f));
            Vector3 tip = elbow + new Vector3(d.Range(-0.2f, 0.2f), 1f, d.Range(-0.2f, 0.2f)) * (H - stemH) * d.Range(0.38f, 0.66f);
            s.Tube(new[] { fork, elbow, tip }, new[] { g.TrunkRadius * 0.55f, g.TrunkRadius * 0.42f, g.TrunkRadius * 0.3f }, far ? 3 : 4, c.Bark, c.Bark);
            int spikes = far ? 3 : 7;
            for (int k = 0; k < spikes; k++)
            {
                Vector3 dir = Ring(k * MathF.Tau / spikes + d.Range(-0.15f, 0.15f), k % 2 == 0 ? 1f : 0.55f, k % 2 == 0 ? 0.35f : 1f);
                s.Blade(tip, dir, g.FrondLength * 0.46f * d.Range(0.85f, 1.1f), g.FrondWidth * (far ? 1.4f : 0.6f), 0.04f, 1, c.Mid, c.Top, 0f, 0.25f);
            }
        }
    }

    // ---- Strange forms ------------------------------------------------------------------------

    /// <summary>A leafless, forking growth, like coral or antler, its tips swelling into bright knobs.</summary>
    static void Antler(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height;
        var (path, radii) = Trunk(g, d, H * g.Stem, g.TrunkRadius, g.TrunkRadius * 0.76f, 1, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.Bark, c.Mid);
        void Fork(Vector3 from, Vector3 dir, float length, float radius, int depth)
        {
            Vector3 to = from + Vector3.Normalize(dir) * length;
            s.Tube(new[] { from - Vector3.Normalize(dir) * radius * 0.5f, (from + to) * 0.5f + Ring(d.Range(0f, MathF.Tau), length * 0.06f), to }, new[] { radius, radius * 0.82f, radius * 0.66f }, far ? 3 : 4, c.Mid, c.Top);
            if (depth == 0)
            {
                if (!far) s.Lathe(to - Vector3.UnitY * radius, Vector3.UnitY, new[] { (0.01f, 0f), (radius * 2f, radius * 1.4f), (0.01f, radius * 3.6f) }, 3, _ => c.Tip);
                return;
            }
            int n = depth == (far ? 1 : 2) ? g.Forks + 1 : 2;
            float spin = d.Range(0f, MathF.Tau);
            for (int i = 0; i < n; i++)
                Fork(to, dir + Ring(spin + i * MathF.Tau / n, 0.7f + g.Spread * 0.6f, 0.15f), length * 0.72f, radius * 0.66f, depth - 1);
        }
        Fork(a.StemTop, Vector3.UnitY, H * (1f - g.Stem) * 0.46f, g.TrunkRadius * 0.74f, far ? 1 : 2);
    }

    /// <summary>Stems arching out from one base, each hanging a seed pod like a lantern.</summary>
    static void Lantern(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, R = H * g.Crown;
        var (path, radii) = Trunk(g, d, H * g.Stem * 0.7f, g.TrunkRadius, g.TrunkRadius * 0.7f, 1, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        int n = far ? 3 : g.Forks + 2;
        float phase = d.Range(0f, MathF.Tau);
        for (int i = 0; i < n; i++)
        {
            float angle = phase + i * MathF.Tau / n + d.Range(-0.2f, 0.2f), reach = R * d.Range(0.75f, 1.05f);
            Vector3 start = a.StemTop;
            float rise = (H - start.Y) * d.Range(0.86f, 1f);
            Vector3 p1 = start + Ring(angle, reach * 0.26f, rise * 0.72f), p2 = start + Ring(angle, reach * 0.6f, rise);
            Vector3 p3 = start + Ring(angle, reach * 0.92f, rise * 0.92f), end = start + Ring(angle, reach * 1.08f, rise * 0.68f);
            float st = MathF.Max(g.TrunkRadius * 0.62f, H * 0.014f);
            s.Tube(new[] { start, p1, p2, p3, end }, new[] { st, st * 0.84f, st * 0.66f, st * 0.5f, st * 0.34f }, 3, c.Bark, c.Mid);
            float pod = H * 0.042f * d.Range(0.9f, 1.3f);
            (float, float)[] p = far
                ? new[] { (0.02f, -pod * 2.5f), (pod, -pod * 1.2f), (0.02f, 0f) }
                : new[] { (0.02f, -pod * 2.8f), (pod * 0.82f, -pod * 2f), (pod, -pod * 1.15f), (pod * 0.5f, -pod * 0.3f), (0.02f, 0f) };
            int rows = p.Length;
            s.Lathe(end, Vector3.UnitY, p, 4, b => Vector4.Lerp(c.Tip, c.Mid, b / (rows - 1f)), far ? 0f : 0.07f, 0f, d);
        }
    }

    /// <summary>A dead, drowned trunk: snapped limbs and no leaves.</summary>
    static void Snag(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height * 0.72f;
        var (path, radii) = Trunk(g, d, H, g.TrunkRadius, g.TrunkRadius * 0.34f, far ? 2 : 3, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        int n = far ? 2 : g.Forks + 2;
        for (int i = 0; i < n; i++)
        {
            Vector3 on = Along(path, 0.42f + 0.52f * i / n);
            Vector3 tip = on + Ring(d.Range(0f, MathF.Tau), H * d.Range(0.14f, 0.28f), H * d.Range(0.05f, 0.2f));
            Limb(s, d, on, tip, g.TrunkRadius * 0.28f, g.TrunkRadius * 0.08f, 3, c.Bark, c.Bark);
        }
    }

    // ---- Fungal forms -------------------------------------------------------------------------

    /// <summary>A stalk under a turned cap: dome, cone, plate, bell or inverted funnel, its gills showing below.</summary>
    static void Cap(Sculptor s, Dice d, Paints c, Vector3 at, float r, float h, int shape, bool far, Vector3 tilt)
    {
        (float, float)[] p = shape switch
        {
            0 => new[] { (r * 0.2f, -h * 0.14f), (r * 0.8f, -h * 0.2f), (r, -h * 0.1f), (r * 0.92f, h * 0.34f), (r * 0.56f, h * 0.82f), (0.02f, h) },
            1 => new[] { (r * 0.2f, -h * 0.1f), (r * 0.82f, -h * 0.18f), (r, -h * 0.26f), (r * 0.56f, h * 0.42f), (r * 0.2f, h * 0.95f), (0.02f, h * 1.25f) },
            2 => new[] { (r * 0.2f, -h * 0.1f), (r * 0.8f, -h * 0.02f), (r, h * 0.04f), (r * 0.94f, h * 0.2f), (r * 0.4f, h * 0.36f), (0.02f, h * 0.4f) },
            3 => new[] { (r * 0.2f, h * 0.05f), (r * 0.74f, -h * 0.42f), (r, -h * 0.5f), (r * 0.74f, h * 0.3f), (r * 0.36f, h * 0.96f), (0.02f, h * 1.05f) },
            _ => new[] { (r * 0.18f, -h * 0.3f), (r * 0.7f, h * 0.1f), (r, h * 0.52f), (r * 0.82f, h * 0.56f), (r * 0.3f, h * 0.2f), (0.02f, h * 0.14f) },
        };
        if (far) p = new[] { p[0], p[2], p[3], p[5] };
        int rows = p.Length, gills = far ? 1 : 2;
        s.Lathe(at, Vector3.UnitY + tilt, p, far ? 6 : 8, b => b < gills ? c.Gill : Vector4.Lerp(c.Under, c.Top, (b - gills + 1f) / (rows - gills)), 0f, far ? 0f : 0.05f, d);
    }

    /// <summary>One or a few stalks, each under a great cap.</summary>
    static void Capped(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, R = H * g.Crown * 1.05f;
        int stems = far ? 1 : g.Clump, shape = g.Layers % 5;
        for (int i = 0; i < stems; i++)
        {
            float k = i == 0 ? 1f : d.Range(0.5f, 0.72f);
            float h = H * k, r = g.TrunkRadius * (i == 0 ? 1f : 0.62f);
            Vector3 at = i == 0 ? Vector3.Zero : Ring(d.Range(0f, MathF.Tau), R * (0.5f + 0.25f * k));
            var bits = new Anatomy();
            bool plain = far || i > 0;
            var (path, radii) = Trunk(g, d, h * 0.86f, r, r * 0.72f, plain ? 2 : 3, i == 0 ? a : bits);
            for (int n = 0; n < path.Length; n++) path[n] += at;
            s.Tube(path, radii, plain ? 4 : g.Sides, c.BarkLow, c.Bark);
            Vector3 top = path[^1];
            if (i == 0) a.StemTop = top;
            Cap(s, d, c, top, R * k, H * g.Flat * 0.28f * k, shape, plain, Ring(g.LeanAngle, g.Lean * 0.8f));
        }
    }

    /// <summary>A tall stalk with caps set up it one above another, shrinking toward the top.</summary>
    static void Parasol(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, R = H * g.Crown;
        var (path, radii) = Trunk(g, d, H * 0.94f, g.TrunkRadius, g.TrunkRadius * 0.55f, far ? 2 : 3, a);
        s.Tube(path, radii, far ? 4 : g.Sides, c.BarkLow, c.Bark);
        int n = far ? 2 : Math.Clamp(g.Layers, 2, 3);
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 1f : i / (n - 1f), y = H * (0.5f + 0.44f * t);
            float r = R * (1.15f - 0.75f * t);
            Cap(s, d, c, Along(path, y / (H * 0.94f)), r, r * (0.3f + g.Flat * 0.25f), 2 + i % 2, far, Ring(d.Range(0f, MathF.Tau), 0.05f));
        }
    }

    /// <summary>A single flaring trumpet, hollow at the top.</summary>
    static void Trumpet(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, R = H * g.Crown * 0.8f, r = g.TrunkRadius;
        int bells = far ? 1 : g.Clump;
        for (int i = 0; i < bells; i++)
        {
            float k = i == 0 ? 1f : d.Range(0.46f, 0.7f), h = H * k, w = R * k;
            Vector3 at = i == 0 ? Vector3.Zero : Ring(d.Range(0f, MathF.Tau), R * 0.8f * k);
            (float, float)[] p = far
                ? new[] { (r, Under), (r * 0.8f, h * 0.5f), (w, h), (w * 0.6f, h * 0.9f), (0.02f, h * 0.72f) }
                : new[] { (r * 1.1f, Under), (r, h * 0.2f), (r * 0.82f, h * 0.5f), (w * 0.42f, h * 0.76f), (w, h), (w * 0.92f, h * 1.01f), (w * 0.34f, h * 0.84f), (0.02f, h * 0.7f) };
            int rows = p.Length;
            s.Lathe(at, Vector3.UnitY + Ring(g.LeanAngle, g.Lean * 0.5f), p, far ? 5 : g.Sides + 2, b => b >= rows - 3 ? c.Gill : Vector4.Lerp(c.BarkLow, c.Top, b / (rows - 3f)), far ? 0f : g.Ribs, 0.02f, d);
        }
        a.StemTop = new Vector3(0f, H * 0.6f, 0f);
        a.StemRadius = r;
        a.Foot = r;
    }

    /// <summary>A cluster of upright tubes, open at the top, like organ pipes.</summary>
    static void Organ(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, r = g.TrunkRadius;
        int n = far ? 3 : Math.Max(3, g.Clump + 2);
        for (int i = 0; i < n; i++)
        {
            float k = i == 0 ? 1f : d.Range(0.42f, 0.86f), h = H * k, w = r * (i == 0 ? 1f : d.Range(0.6f, 0.88f));
            Vector3 at = i == 0 ? Vector3.Zero : Ring(i * 2.4f, r * 1.5f + d.Range(0f, 0.15f) * H * g.Crown);
            Vector3 tilt = Ring(i * 2.4f, i == 0 ? 0f : g.Spread * 0.16f, 1f);
            (float, float)[] p = far
                ? new[] { (w, Under), (w * 1.1f, h), (0.02f, h * 0.9f) }
                : new[] { (w * 0.92f, Under), (w, h * 0.5f), (w * 1.3f, h), (w * 0.86f, h * 0.99f), (0.02f, h * 0.78f) };
            int rows = p.Length;
            s.Lathe(at, tilt, p, far ? 4 : 5, b => b >= rows - 2 ? c.Gill : Vector4.Lerp(c.Under, c.Top, b / (rows - 2f)), 0f, 0.03f, d);
        }
        a.StemTop = new Vector3(0f, H * 0.7f, 0f);
        a.StemRadius = r;
        a.Foot = r * 1.1f;
    }

    // ---- Dry-country forms --------------------------------------------------------------------

    /// <summary>A ribbed column with arms that turn up at the elbow.</summary>
    static void Candelabra(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, r = g.TrunkRadius;
        Rib(s, d, Vector3.Zero, Vector3.UnitY, H, r, g, c, far);
        a.StemTop = new Vector3(0f, H, 0f);
        a.StemRadius = r;
        a.Foot = r;
        int arms = far ? Math.Min(2, g.Forks) : g.Forks;
        float phase = d.Range(0f, MathF.Tau);
        for (int i = 0; i < arms; i++)
        {
            float angle = phase + i * MathF.Tau / arms + d.Range(-0.3f, 0.3f), y = H * d.Range(0.32f, 0.62f);
            float out_ = r * d.Range(2.1f, 3.1f), rise = H * d.Range(0.24f, 0.42f), w = r * 0.62f;
            Vector3 from = new(0f, y, 0f), elbow = from + Ring(angle, out_, r * 0.4f), up = elbow + Vector3.UnitY * rise;
            s.Tube(new[] { from, Vector3.Lerp(from, elbow, 0.7f), elbow + Vector3.UnitY * w * 0.8f }, new[] { w, w, w }, far ? 4 : 5, c.Mid, c.Mid, false);
            Rib(s, d, elbow, Vector3.UnitY, rise, w, g, c, true);
        }
    }

    static void Rib(Sculptor s, Dice d, Vector3 at, Vector3 axis, float h, float r, Genome g, Paints c, bool far)
    {
        (float, float)[] p = far
            ? new[] { (r, Under), (r, h * 0.8f), (r * 0.6f, h), (0.02f, h * 1.04f) }
            : new[] { (r * 0.96f, Under), (r, h * 0.3f), (r, h * 0.82f), (r * 0.78f, h * 0.96f), (r * 0.3f, h * 1.03f), (0.02f, h * 1.04f) };
        int rows = p.Length;
        s.Lathe(at, axis, p, far ? 5 : Math.Max(6, g.Sides + 2), b => Vector4.Lerp(c.Under, c.Top, b / (rows - 1f)), far ? 0f : g.Ribs, 0f, d);
    }

    /// <summary>Squat ribbed globes, sitting close together, a few flowers on the crown.</summary>
    static void Barrel(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height * 0.55f, r = H * 0.5f;
        int n = far ? 1 : g.Clump;
        for (int i = 0; i < n; i++)
        {
            float k = i == 0 ? 1f : d.Range(0.5f, 0.75f), h = H * k, w = r * k * (0.9f + g.Flat * 0.25f);
            Vector3 at = i == 0 ? Vector3.Zero : Ring(d.Range(0f, MathF.Tau), r * 1.15f);
            (float, float)[] p = far
                ? new[] { (w * 0.7f, Under), (w, h * 0.4f), (w * 0.6f, h * 0.92f), (0.02f, h) }
                : new[] { (w * 0.8f, Under), (w * 0.94f, h * 0.14f), (w, h * 0.46f), (w * 0.8f, h * 0.8f), (w * 0.34f, h * 0.98f), (0.02f, h) };
            int rows = p.Length;
            s.Lathe(at, Vector3.UnitY, p, far ? 5 : 11, b => Vector4.Lerp(c.Under, c.Top, b / (rows - 1f)), far ? 0f : MathF.Max(g.Ribs, 0.1f), 0f, d);
            if (far || g.Bloom < 0.3f) continue;
            for (int f = 0; f < 3; f++)
                s.Blossom(at + Ring(f * 2.1f, w * 0.26f, h * 0.99f), Vector3.UnitY + Ring(f * 2.1f, 0.5f), r * 0.14f, c.Petal, c.Heart);
        }
        a.StemTop = new Vector3(0f, H * 0.9f, 0f);
        a.StemRadius = r * 0.9f;
        a.Foot = r * 0.85f;
    }

    /// <summary>Tall thin wands rising from one crown, flecked with leaves, red at the tips.</summary>
    static void Whips(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height;
        int n = far ? 5 : g.Fronds;
        for (int i = 0; i < n; i++)
        {
            float angle = i * 2.399f + d.Range(-0.2f, 0.2f), splay = d.Range(0.32f, 0.5f) + g.Spread * 0.24f, h = H * d.Range(0.72f, 1.04f);
            Vector3 mid = Ring(angle, h * splay * 0.34f, h * 0.55f), tip = Ring(angle, h * splay * (0.74f + g.Arch * 0.3f), h);
            float w = g.TrunkRadius * 0.26f;
            s.Tube(new[] { new Vector3(0f, Under, 0f), Ring(angle, w, 0.1f), mid, tip }, new[] { w, w, w * 0.68f, w * 0.12f }, 3, c.BarkLow, c.Bark, false);
            if (far) continue;
            for (float t = 0.3f; t < 0.9f; t += 0.2f)
            {
                Vector3 p = Vector3.Lerp(mid, tip, t);
                s.Leaf(p, Ring(angle + t * 9f, 1f, 0.5f), Vector3.UnitY, H * 0.09f, H * 0.03f, c.Mid, c.Top);
            }
            s.Leaf(tip, tip - mid, Vector3.UnitY, H * 0.09f, H * 0.03f, c.Tip, c.Tip);
        }
        a.StemTop = new Vector3(0f, H * 0.42f, 0f);
        a.StemRadius = g.TrunkRadius;
        a.Foot = g.TrunkRadius;
    }

    /// <summary>Flat oval pads growing edge to edge from a stout base.</summary>
    static void Paddles(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height, size = H * 0.25f;
        void Pad(Vector3 at, Vector3 dir, float k, int depth)
        {
            dir = Vector3.Normalize(dir);
            Vector3 centre = at + dir * size * k;
            float w = size * k * 0.72f, t = size * k * 0.14f;
            Vector3 flat = Vector3.Normalize(Vector3.Cross(dir, Ring(d.Range(0f, MathF.Tau), 1f, 0.2f)));
            (float, float)[] p = far
                ? new[] { (0.02f, -t), (w, 0f), (0.02f, t) }
                : new[] { (0.02f, -t), (w * 0.8f, -t * 0.7f), (w, 0f), (w * 0.8f, t * 0.7f), (0.02f, t) };
            int rows = p.Length;
            s.Lathe(centre, flat, p, far ? 5 : 6, b => Vector4.Lerp(c.Under, c.Top, b / (rows - 1f)), 0f, 0f, d, 1.35f);
            if (depth <= 0) return;
            int n = depth == 2 ? 2 : g.Forks > 2 ? 2 : 1;
            for (int i = 0; i < n; i++)
            {
                Vector3 side = Vector3.Normalize(Vector3.Cross(dir, flat)) * (i == 0 ? 0.62f : -0.62f);
                Pad(centre + dir * size * k * 0.72f + side * w * 0.7f, dir + side + Vector3.UnitY * 0.25f, k * d.Range(0.72f, 0.92f), depth - 1);
                if (far && depth < 2) break;
            }
        }
        s.Tube(new[] { new Vector3(0f, Under, 0f), new Vector3(0f, size * 0.5f, 0f) }, new[] { size * 0.24f, size * 0.2f }, 5, c.BarkLow, c.Bark);
        Pad(new Vector3(0f, size * 0.3f, 0f), Vector3.UnitY, 1f, far ? 1 : 2);
        a.StemTop = new Vector3(0f, H * 0.5f, 0f);
        a.StemRadius = size * 0.4f;
        a.Foot = size * 0.3f;
    }

    // ---- Shrubs -------------------------------------------------------------------------------

    /// <summary>
    /// A true bush: thin woody stems fan up out of one rooted base, bare near the ground so they show,
    /// and each carries sprays of broad pointed leaves, one spray over another, with air between them:
    /// a loose, jagged mass that is all tips, and nothing solid to mistake for a stone. Any bright colour
    /// is small flowers sitting on the sprays; the plant itself stays the world's green.
    /// </summary>
    static void Bush(Genome g, Sculptor s, Dice d, Paints c, Anatomy a)
    {
        float H = g.Height, R = H * g.Crown * 1.2f;
        int stems = g.Forks + 1;
        float phase = d.Range(0f, MathF.Tau);
        var sprays = new List<Vector3>();
        for (int i = 0; i < stems; i++)
        {
            float angle = phase + i * MathF.Tau / stems + d.Range(-0.25f, 0.25f);
            Vector3 mid = Ring(angle, R * 0.3f, H * d.Range(0.36f, 0.46f));
            Vector3 tip = Ring(angle, R * d.Range(0.48f, 0.72f), H * d.Range(0.62f, 0.8f));
            float w = H * 0.03f;
            s.Tube(new[] { new Vector3(0f, Under * 0.5f, 0f), mid, tip }, new[] { w * 1.3f, w * 0.8f, w * 0.3f }, 3, c.BarkLow, c.Bark, false);
            sprays.Add(tip);
            sprays.Add(Vector3.Lerp(mid, tip, 0.4f) + Ring(angle + 1.1f, R * 0.3f, H * 0.04f));
            sprays.Add(Vector3.Lerp(mid, tip, 0.7f) + Ring(angle - 1.2f, R * 0.28f, H * 0.12f));
        }
        sprays.Add(new Vector3(0f, H * 0.82f, 0f) + Ring(phase, R * 0.1f));
        var perches = new List<(Vector3 At, Vector3 Dir)>();
        foreach (Vector3 at in sprays)
        {
            // A wide lower whorl, drooping a little, and a tighter upper one turned between its leaves.
            float length = g.FrondLength * d.Range(0.95f, 1.25f), spin = d.Range(0f, MathF.Tau);
            int n = 5;
            for (int k = 0; k < n; k++)
            {
                Vector3 dir = Ring(spin + k * MathF.Tau / n + d.Range(-0.2f, 0.2f), 1f, d.Range(0.02f, 0.5f));
                s.Leaf(at - dir * length * 0.1f, dir, Vector3.UnitY, length, g.FrondWidth, c.Under, Vector4.Lerp(c.Mid, c.Top, d.Unit() * 0.6f));
            }
            for (int k = 0; k < n - 2; k++)
            {
                Vector3 dir = Ring(spin + (k + 0.5f) * MathF.Tau / (n - 2) + d.Range(-0.2f, 0.2f), 1f, d.Range(0.55f, 1.1f));
                Vector3 root = at + Vector3.UnitY * length * 0.1f;
                s.Leaf(root, dir, Vector3.UnitY, length * 0.8f, g.FrondWidth * 0.85f, c.Mid, c.Top);
                perches.Add((root + Vector3.Normalize(dir) * length * 0.45f, dir));
            }
        }
        if (g.Bloom > 0f)
        {
            int blooms = Math.Min(14, (int)MathF.Round(sprays.Count * (0.9f + g.Bloom)));
            for (int i = 0; i < blooms; i++)
            {
                // On a leaf, a third of a stride apart round the sprays, so they don't all sit on one.
                var (at, dir) = perches[i * 5 % perches.Count];
                s.Blossom(at, dir + Vector3.UnitY, H * 0.036f * d.Range(0.85f, 1.2f), c.Petal, c.Heart);
            }
        }
        a.Foot = 0.14f;
    }

    /// <summary>Arching fronds from a rooted crown, each a folded, narrow leaf.</summary>
    static void Fern(Genome g, Sculptor s, Dice d, Paints c, Anatomy a)
    {
        s.Tube(new[] { new Vector3(0f, Under * 0.5f, 0f), new Vector3(0f, g.Height * 0.14f, 0f) }, new[] { g.Height * 0.1f, g.Height * 0.06f }, 4, c.BarkLow, c.Bark);
        Vector3 crown = new(0f, g.Height * 0.12f, 0f);
        float phase = d.Range(0f, MathF.Tau);
        for (int i = 0; i < g.Fronds; i++)
        {
            Vector3 dir = Ring(phase + i * 2.399f, 1f, d.Range(0.75f, 1.6f));
            s.Blade(crown, dir, g.FrondLength * d.Range(0.8f, 1.1f), g.FrondWidth, g.Arch, 3, c.Mid, c.Top, 0.5f, 0.3f);
        }
        a.Foot = g.Height * 0.1f;
    }

    /// <summary>A dry, wiry bush: bare forking twigs and only a few small leaves.</summary>
    static void Thorn(Genome g, Sculptor s, Dice d, Paints c, Anatomy a)
    {
        float H = g.Height, R = H * g.Crown;
        int stems = g.Forks + 3;
        for (int i = 0; i < stems; i++)
        {
            float angle = i * 2.399f + d.Range(-0.3f, 0.3f);
            Vector3 mid = Ring(angle, R * 0.42f, H * d.Range(0.4f, 0.55f));
            Vector3 tip = Ring(angle + d.Range(-0.3f, 0.3f), R * d.Range(0.78f, 1.1f), H * d.Range(0.7f, 1f));
            float w = H * 0.03f;
            s.Tube(new[] { new Vector3(0f, Under * 0.5f, 0f), Ring(angle, H * 0.04f, 0.05f), mid, tip }, new[] { w, w, w * 0.6f, w * 0.15f }, 3, c.BarkLow, c.Bark, false);
            Vector3 fork = Vector3.Lerp(mid, tip, 0.4f);
            Vector3 side = fork + Ring(angle + 1.2f, R * 0.32f, H * 0.2f);
            s.Tube(new[] { fork, side }, new[] { w * 0.4f, w * 0.1f }, 3, c.Bark, c.Bark, false);
            for (int k = 0; k < 4; k++)
                s.Leaf(k % 2 == 0 ? tip : side, Ring(k * 1.9f + angle, 1f, d.Range(0f, 0.9f)), Vector3.UnitY, g.FrondLength * 0.6f, g.FrondWidth * 0.7f, c.Mid, c.Top);
        }
        a.Foot = 0.14f;
    }

    /// <summary>A rosette of stiff sword leaves, sometimes with a tall flower spike.</summary>
    static void Rosette(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        float H = g.Height;
        s.Tube(new[] { new Vector3(0f, Under * 0.5f, 0f), new Vector3(0f, H * 0.1f, 0f) }, new[] { H * 0.11f, H * 0.08f }, 5, c.BarkLow, c.Bark);
        float phase = d.Range(0f, MathF.Tau);
        for (int i = 0; i < g.Fronds; i++)
        {
            float t = i / (float)g.Fronds;
            Vector3 dir = Ring(phase + i * 2.399f, 1f - t * 0.75f, 0.55f + t * 2f);
            s.Blade(new Vector3(0f, 0.04f, 0f), dir, H * d.Range(0.85f, 1.1f), g.FrondWidth * 1.3f, 0.06f + g.Arch * 0.2f, 2, c.Under, c.Top, 0.35f, 0.3f);
        }
        if (g.Bloom > 0.3f)
        {
            float mast = H * 1.9f;
            s.Tube(new[] { Vector3.UnitY * 0.1f, new Vector3(0f, mast, 0f) }, new[] { H * 0.035f, H * 0.012f }, 3, c.Stalk, c.Stalk);
            for (int k = 0; k < 6; k++)
                s.Blossom(new Vector3(0f, mast * (0.68f + k * 0.06f), 0f) + Ring(k * 2.2f, H * 0.02f), Ring(k * 2.2f, 1f, 0.4f), H * 0.05f, c.Petal, c.Heart);
        }
        a.Foot = H * 0.11f;
    }

    // ---- Mineral growths ----------------------------------------------------------------------

    /// <summary>A cluster of leaning faceted shards.</summary>
    static void Shards(Genome g, Sculptor s, Dice d, Paints c, bool far, Anatomy a)
    {
        int n = far ? 2 : g.Clump + 1;
        float H = g.Height, r = g.TrunkRadius, phase = d.Range(0f, MathF.Tau);
        Vector3 main = Vector3.UnitY + Ring(g.LeanAngle, g.Lean);
        s.Shard(Vector3.Zero, main, r, H, far ? 4 : g.Sides, c.Under, c.Top);
        a.StemTop = Vector3.Normalize(main) * H * 0.8f;
        a.StemRadius = r * 0.9f;
        a.Foot = r;
        for (int i = 1; i < n; i++)
        {
            float angle = phase + i * 2.399f;
            s.Shard(Ring(angle, r * d.Range(1.1f, 1.8f)), Vector3.UnitY + Ring(angle, g.Spread), r * d.Range(0.45f, 0.7f), H * d.Range(0.36f, 0.68f), far ? 4 : g.Sides, c.Under, c.Mid);
        }
    }

    // ---- Ground cover -------------------------------------------------------------------------

    /// <summary>A tuft of wide blades, dark at the root and light at the tip, curving out from one spot.</summary>
    static void Tussock(Genome g, Sculptor s, Dice d, Paints c)
    {
        float spread = g.Spread;
        for (int i = 0; i < g.Fronds; i++)
        {
            Vector3 foot = Ring(d.Range(0f, MathF.Tau), spread * MathF.Sqrt(d.Unit()), -0.14f);
            Vector3 dir = Ring(d.Range(0f, MathF.Tau), g.Flare * d.Range(0.4f, 1f), 1f);
            s.Blade(foot, dir, g.Height * d.Range(0.68f, 1.1f), g.FrondWidth, g.Arch, 2, c.Under, c.Top, 0f, 0.14f);
        }
    }

    /// <summary>A little patch of leaves with small flowers on thin stalks above it.</summary>
    static void Flowers(Genome g, Sculptor s, Dice d, Paints c)
    {
        for (int i = 0; i < g.Fronds; i++)
        {
            Vector3 foot = Ring(d.Range(0f, MathF.Tau), g.Spread * MathF.Sqrt(d.Unit()), -0.1f);
            float h = g.Height * d.Range(0.7f, 1.05f);
            Vector3 top = foot + new Vector3(d.Range(-0.1f, 0.1f), h + 0.1f, d.Range(-0.1f, 0.1f));
            s.Tube(new[] { foot, top }, new[] { 0.011f, 0.008f }, 3, c.Under, c.Stalk, false);
            s.Blossom(top, Vector3.UnitY + Ring(d.Range(0f, MathF.Tau), 0.4f), g.FrondLength, c.Petal, c.Heart);
            s.Leaf(foot + Vector3.UnitY * (0.12f + h * 0.3f), Ring(i * 2.3f, 1f, 0.6f), Vector3.UnitY, g.Height * 0.6f, g.FrondWidth, c.Mid, c.Top);
            s.Leaf(foot + Vector3.UnitY * 0.12f, Ring(i * 2.3f + 2f, 1f, 0.3f), Vector3.UnitY, g.Height * 0.5f, g.FrondWidth, c.Under, c.Mid);
        }
    }

    /// <summary>Low dry scrub: a few wiry twigs from one point, tipped with small leaves.</summary>
    static void Twigs(Genome g, Sculptor s, Dice d, Paints c)
    {
        for (int i = 0; i < g.Fronds; i++)
        {
            float angle = i * 2.399f + d.Range(-0.4f, 0.4f);
            Vector3 tip = Ring(angle, g.Spread * d.Range(0.6f, 1f), g.Height * d.Range(0.6f, 1f));
            s.Tube(new[] { new Vector3(0f, -0.2f, 0f), tip * new Vector3(0.45f, 0.6f, 0.45f), tip }, new[] { 0.016f, 0.011f, 0.004f }, 3, c.BarkLow, c.Bark, false);
            s.Leaf(tip, Ring(angle, 1f, 0.5f), Vector3.UnitY, g.FrondLength, g.FrondWidth, c.Mid, c.Top);
            s.Leaf(tip * 0.75f, Ring(angle + 1.6f, 1f, 0.3f), Vector3.UnitY, g.FrondLength * 0.8f, g.FrondWidth, c.Under, c.Mid);
        }
    }

    /// <summary>Tall stiff stems at the water's edge, with seed heads.</summary>
    static void Reeds(Genome g, Sculptor s, Dice d, Paints c)
    {
        for (int i = 0; i < g.Fronds; i++)
        {
            Vector3 foot = Ring(d.Range(0f, MathF.Tau), g.Spread * MathF.Sqrt(d.Unit()), -0.5f);
            float h = g.Height * d.Range(0.75f, 1.08f);
            Vector3 top = foot + new Vector3(d.Range(-0.14f, 0.14f), h, d.Range(-0.14f, 0.14f));
            s.Blade(foot, top - foot, h, g.FrondWidth, 0f, 1, c.Under, c.Top, 0f, 0.1f);
            if (i % 3 == 0)
                s.Tube(new[] { Vector3.Lerp(foot, top, 0.8f), Vector3.Lerp(foot, top, 0.93f) }, new[] { g.FrondWidth * 0.42f, g.FrondWidth * 0.3f }, 3, c.BarkLow, c.Bark);
        }
    }

    /// <summary>Cold-country growth: low overlapping leaf scales hugging the ground, never a dome.</summary>
    static void Lichen(Genome g, Sculptor s, Dice d, Paints c)
    {
        for (int i = 0; i < g.Fronds; i++)
        {
            Vector3 at = Ring(d.Range(0f, MathF.Tau), g.Spread * MathF.Sqrt(d.Unit()), -0.08f);
            s.Leaf(at, Ring(d.Range(0f, MathF.Tau), 1f, d.Range(0.3f, 1.3f)), Vector3.UnitY, g.Height * d.Range(0.8f, 1.3f), g.FrondWidth, c.Under, c.Top);
        }
    }

    /// <summary>Thin stalks each ending in a small bright head: a fungal lawn.</summary>
    static void PinStalks(Genome g, Sculptor s, Dice d, Paints c)
    {
        for (int i = 0; i < g.Fronds; i++)
        {
            Vector3 foot = Ring(d.Range(0f, MathF.Tau), g.Spread * MathF.Sqrt(d.Unit()), -0.1f);
            float h = g.Height * d.Range(0.6f, 1.1f), r = g.FrondLength * d.Range(0.8f, 1.2f);
            Vector3 top = foot + new Vector3(d.Range(-0.06f, 0.06f), h + 0.1f, d.Range(-0.06f, 0.06f));
            s.Tube(new[] { foot, top }, new[] { g.FrondWidth, g.FrondWidth * 0.7f }, 3, c.Stalk, c.Stalk, false);
            s.Lathe(top, Vector3.UnitY, new[] { (0.01f, -r * 0.24f), (r, 0f), (0.01f, r * 0.62f) }, 3, b => b == 0 ? c.Gill : c.Top);
        }
    }

    // ---- Crops --------------------------------------------------------------------------------

    /// <summary>A stand of grain: stiff stalks with heavy heads.</summary>
    static void GrainCrop(Genome g, Sculptor s, Dice d, Paints c)
    {
        for (int i = 0; i < g.Fronds; i++)
        {
            Vector3 foot = Ring(d.Range(0f, MathF.Tau), g.Spread * MathF.Sqrt(d.Unit()), -0.2f);
            float h = g.Height * d.Range(0.88f, 1.06f);
            Vector3 top = foot + new Vector3(d.Range(-0.08f, 0.08f), h, d.Range(-0.08f, 0.08f));
            s.Blade(foot, top - foot, h, g.FrondWidth, 0f, 1, c.Under, c.Mid, 0f, 0.1f);
            s.Lathe(top, top - foot, new[] { (0.01f, -g.Height * 0.1f), (g.FrondWidth, 0f), (g.FrondWidth * 0.7f, g.Height * 0.16f), (0.01f, g.Height * 0.22f) }, 4, _ => c.Tip);
        }
    }

    /// <summary>A low head of broad, overlapping leaves.</summary>
    static void LeafCrop(Genome g, Sculptor s, Dice d, Paints c)
    {
        for (int i = 0; i < g.Fronds; i++)
        {
            float t = i / (float)g.Fronds;
            Vector3 dir = Ring(i * 2.399f, 1f - t * 0.6f, 0.5f + t * 1.6f);
            s.Blade(new Vector3(0f, -0.08f, 0f), dir, g.Height * (1.1f - 0.4f * t), g.FrondWidth, 0.25f, 2, c.Under, c.Top, 0.3f, 0.55f);
        }
    }

    /// <summary>A vine on a pole, leaves up its length and fruit hanging from it.</summary>
    static void VineCrop(Genome g, Sculptor s, Dice d, Paints c)
    {
        float H = g.Height;
        s.Tube(new[] { new Vector3(0f, -0.4f, 0f), new Vector3(0f, H, 0f) }, new[] { 0.03f, 0.02f }, 3, c.BarkLow, c.Bark);
        for (int i = 0; i < g.Fronds; i++)
        {
            float y = H * (0.15f + 0.8f * i / g.Fronds);
            Vector3 at = Ring(i * 2.399f, 0.03f, y);
            s.Leaf(at, Ring(i * 2.399f, 1f, 0.25f), Vector3.UnitY, g.FrondLength, g.FrondWidth, c.Under, c.Top);
            if (i % 3 == 1)
                s.Lump(at + Ring(i * 2.399f + 1f, 0.09f, -0.09f), new Vector3(0.07f, 0.09f, 0.07f), d, c.Tip, c.Tip, 0.08f, 1f);
        }
    }
}
