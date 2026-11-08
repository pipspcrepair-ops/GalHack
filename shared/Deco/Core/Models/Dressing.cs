using System;
using System.Collections.Generic;
using System.Numerics;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>
/// What one piece of a world's dressing is drawn with: the style it takes (a wall mark's sign, a tent's
/// use), its era (timber, rope and oil; steel and sodium; composite and light strips), how worn it is
/// (0 fresh to 1 a long century), whether its lamps still burn, its seed, and for a span its length
/// and the ground's height under each end.
/// </summary>
public readonly record struct DressArgs(string Style, DecoTech Era, float Wear, bool Lit, ulong Seed, float Length = 6f, float LeftY = 0f, float RightY = 0f);

/// <summary>
/// The models a world's dressing is drawn from (data/world-dressing.txt in the POI generator says which a
/// world leaves where). Each is built in code from boxes, rods, balls and cables: it stands on y = 0 at its
/// middle, front facing -Z; a span runs along X from -Length/2 to +Length/2 with each end's ground at its
/// own height; a wall mark lies flat on the plane z = 0, drawn toward -Z, its foot at y = 0. Nothing
/// floats: every line hangs between two anchors or from one, every piece rests on the ground or on
/// another. Signs and marks are symbols only, never writing. Colours are slots: the world paints them.
/// </summary>
public static partial class DecoDressing
{
    static Dictionary<string, Action<DecoBuilder, DressArgs>> _all;

    public static IReadOnlyDictionary<string, Action<DecoBuilder, DressArgs>> All => _all ??= Make();

    static Dictionary<string, Action<DecoBuilder, DressArgs>> Make()
    {
        var r = new Dictionary<string, Action<DecoBuilder, DressArgs>>(StringComparer.Ordinal);
        Street(r); Festival(r); Sickness(r); War(r); Leaving(r); Faith(r); Marks(r); Stories(r);
        return r;
    }

    /// <summary>A dressing model, drawn: its parts, cables and lights (an unknown model draws nothing).</summary>
    public static DecoModel Build(string model, DressArgs a)
    {
        var b = new DecoBuilder(4f, 4f, 4f, DecoHash.Combine(a.Seed, DecoHash.Fnv(model)));
        if (All.TryGetValue(model, out var build)) build(b, a);
        return b.Model;
    }

    // ---- shared shapes ------------------------------------------------------------------------

    static float R(DecoBuilder b, float lo, float hi) => b.Rng.Range(lo, hi);

    /// <summary>A square rod from a to c.</summary>
    static DecoPart Rod(DecoBuilder b, Vector3 a, Vector3 c, float w, DecoSlot s) => b.Beam(a, c, w, w, s);

    /// <summary>A flat bar from a to c lying in a face whose normal is <paramref name="n"/>: <paramref name="w"/> wide in the face, <paramref name="t"/> thick.</summary>
    static DecoPart Flat(DecoBuilder b, Vector3 a, Vector3 c, Vector3 n, float w, float t, DecoSlot s)
    {
        Vector3 d = c - a;
        float len = d.Length();
        Vector3 y = len < 1e-6f ? Vector3.UnitY : d / len;
        Vector3 x = Vector3.Normalize(n - y * Vector3.Dot(n, y));
        Vector3 z = Vector3.Cross(x, y);
        return b.Hull(new[] { a - z * (w / 2) - x * (t / 2), a + z * (w / 2) - x * (t / 2), c - z * (w / 2) - x * (t / 2), c + z * (w / 2) - x * (t / 2),
            a - z * (w / 2) + x * (t / 2), a + z * (w / 2) + x * (t / 2), c - z * (w / 2) + x * (t / 2), c + z * (w / 2) + x * (t / 2) }, s);
    }

    /// <summary>The cloth colours a string of flags or a stack of goods cycles through.</summary>
    static readonly DecoSlot[] Cloths = { DecoSlot.Fabric, DecoSlot.Trim, DecoSlot.Signal, DecoSlot.Linen };

    static DecoSlot ClothOf(int i) => Cloths[((i % Cloths.Length) + Cloths.Length) % Cloths.Length];

    /// <summary>
    /// A post from the ground at (x, y0, z) up <paramref name="h"/> metres, by era: a squared timber, a steel
    /// tube on a base plate, a tapered composite mast with a light strip. Returns its top.
    /// </summary>
    static Vector3 Post(DecoBuilder b, DecoTech era, float x, float y0, float z, float h, float thick = 1f)
    {
        switch (era)
        {
            case DecoTech.Low:
            {
                float t = 0.07f * thick;
                b.Box(x - t, y0 - 0.3f, z - t, x + t, y0 + h, z + t, DecoSlot.Wood).As(DecoFinish.Wood);
                break;
            }
            case DecoTech.Mid:
                b.Box(x - 0.11f * thick, y0 - 0.02f, z - 0.11f * thick, x + 0.11f * thick, y0 + 0.03f, z + 0.11f * thick, DecoSlot.Metal);
                b.Cyl(x, y0 - 0.2f, z, 0.045f * thick, h + 0.2f, DecoSlot.Metal, 8);
                b.Ball(x, y0 + h, z, 0.055f * thick, DecoSlot.Body, 6);
                break;
            default:
                b.Cyl(x, y0 - 0.02f, z, 0.13f * thick, 0.06f, DecoSlot.Plastic, 12);
                b.Cyl(x, y0 - 0.2f, z, 0.075f * thick, h + 0.2f, DecoSlot.Plastic, 10, 0.55f);
                // The strip down its front: dark when the cells are flat.
                b.Box(x - 0.012f, y0 + 0.4f, z - 0.07f * thick, x + 0.012f, y0 + h * 0.9f, z - 0.05f * thick, DecoSlot.Lamp).Lit().Ghost();
                break;
        }
        return new Vector3(x, y0 + h, z);
    }

    /// <summary>A thin cloth panel from the top edge a..c hanging <paramref name="down"/>, its face across <paramref name="n"/> (a flag, a pennant's square, a banner).</summary>
    static DecoPart Panel(DecoBuilder b, Vector3 a, Vector3 c, Vector3 down, Vector3 n, float t, DecoSlot s, float tipShare = 1f)
    {
        Vector3 h = n * (t * 0.5f);
        Vector3 ba = a + down, bc = c + down;
        if (tipShare < 1f)
        {
            // A pennant: the bottom edge pulled to a point at its middle.
            Vector3 mid = (a + c) * 0.5f + down;
            ba = Vector3.Lerp(mid, ba, tipShare);
            bc = Vector3.Lerp(mid, bc, tipShare);
        }
        return b.Hull(new[] { ba - h, bc - h, a - h, c - h, ba + h, bc + h, a + h, c + h }, s).Soft().Ghost().As(DecoFinish.Fabric);
    }

    /// <summary>Points along a polyline every <paramref name="step"/> metres, with the way it runs there.</summary>
    static List<(Vector3 At, Vector3 Dir)> Along(List<Vector3> pts, float step, float start)
    {
        var o = new List<(Vector3, Vector3)>();
        float run = 0f, next = start;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Vector3 d = pts[i + 1] - pts[i];
            float len = d.Length();
            if (len < 1e-5f) continue;
            Vector3 u = d / len;
            while (next <= run + len)
            {
                o.Add((pts[i] + u * (next - run), u));
                next += step;
            }
            run += len;
        }
        return o;
    }

    /// <summary>The sag a span is drawn with (the same as the cable the emitter strings from it).</summary>
    static List<Vector3> SpanOf(Vector3 a, Vector3 c, float slack) =>
        DecoCables.Span(a, c, slack, Math.Clamp((int)(Vector3.Distance(a, c) / 0.15f), 4, 24));

    /// <summary>
    /// A line strung between two anchors with things hung from it every <paramref name="step"/> metres; broken
    /// (by wear), it hangs from the first anchor instead and its things lie along the ground where it fell.
    /// </summary>
    static void Line(DecoBuilder b, Vector3 a, Vector3 c, float slack, float r, DecoSlot slot, bool broken, float step, Action<Vector3, Vector3, int> hang, Action<Vector3, Vector3, int> lying = null)
    {
        if (!broken)
        {
            b.Cable(a, c, r, slot, slack);
            int i = 0;
            foreach (var (at, dir) in Along(SpanOf(a, c, slack), step, step * 0.6f)) hang(at - Vector3.UnitY * r, dir, i++);
            return;
        }
        // Snapped at the far end: down the near anchor to the ground and along it toward where it ran.
        Vector3 toward = c - a;
        toward.Y = 0;
        float reach = MathF.Min(toward.Length() * 0.6f, 4f);
        b.Hang(a, toward, r, slot, reach);
        if (lying == null) return;
        Vector3 u = toward.LengthSquared() > 1e-6f ? Vector3.Normalize(toward) : Vector3.UnitX;
        int n = (int)(reach / step);
        for (int k = 0; k < n; k++) lying(new Vector3(a.X, 0f, a.Z) + u * (step * (k + 0.8f)), u, k);
    }

    /// <summary>A symbol on a face at z (facing -Z), centred (cx, cy), <paramref name="size"/> across: a sign's only words.</summary>
    static void Symbol(DecoBuilder b, string kind, float cx, float cy, float z, float size, DecoSlot s)
    {
        float r = size * 0.5f, t = MathF.Max(0.006f, size * 0.02f);
        // A bar in the face's plane: its depth across the face (Along puts the first size there), its width in the plane.
        void Bar(float x0, float y0, float x1, float y1, float w) => b.Beam(new Vector3(cx + x0 * r, cy + y0 * r, z - t), new Vector3(cx + x1 * r, cy + y1 * r, z - t), t * 2, w * r, s).Ghost();
        void Disc(float x, float y, float rr) => b.Tube(cx + x * r, cy + y * r, z, cx + x * r, cy + y * r, z - t * 2, rr * r, s, 12).Ghost();
        void Ring(float rr, float w, int n = 14)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i * MathF.Tau / n, a1 = (i + 1) * MathF.Tau / n;
                Bar(MathF.Cos(a0) * rr, MathF.Sin(a0) * rr, MathF.Cos(a1) * rr, MathF.Sin(a1) * rr, w);
            }
        }
        switch (kind)
        {
            case "disc": Disc(0, 0, 0.8f); break;
            case "ring": Ring(0.75f, 0.18f); break;
            case "star":
                for (int i = 0; i < 5; i++) { float a = MathF.PI / 2 + i * MathF.Tau / 5; Bar(0, 0, MathF.Cos(a) * 0.9f, MathF.Sin(a) * 0.9f, 0.22f); }
                break;
            case "triangle": Bar(-0.8f, -0.6f, 0.8f, -0.6f, 0.16f); Bar(0.8f, -0.6f, 0f, 0.8f, 0.16f); Bar(0f, 0.8f, -0.8f, -0.6f, 0.16f); break;
            case "cross": Bar(-0.8f, 0, 0.8f, 0, 0.32f); Bar(0, -0.8f, 0, 0.8f, 0.32f); break;
            case "x": Bar(-0.8f, -0.8f, 0.8f, 0.8f, 0.2f); Bar(-0.8f, 0.8f, 0.8f, -0.8f, 0.2f); break;
            case "sun":
                Disc(0, 0, 0.42f);
                for (int i = 0; i < 8; i++) { float a = i * MathF.Tau / 8; Bar(MathF.Cos(a) * 0.55f, MathF.Sin(a) * 0.55f, MathF.Cos(a) * 0.95f, MathF.Sin(a) * 0.95f, 0.1f); }
                break;
            case "eye": Ring(0.8f, 0.1f); Disc(0, 0, 0.3f); Bar(-0.95f, 0, -0.75f, 0, 0.1f); Bar(0.75f, 0, 0.95f, 0, 0.1f); break;
            case "fist":
                // A raised fist: the forearm, the knuckles as four blocks, the thumb across.
                Bar(0, -0.95f, 0, -0.1f, 0.42f);
                for (int i = 0; i < 4; i++) Bar(-0.36f + i * 0.24f, 0.05f, -0.36f + i * 0.24f, 0.55f, 0.2f);
                Bar(-0.45f, 0f, 0.3f, 0f, 0.2f);
                break;
            case "stripes": for (int i = -1; i <= 1; i++) Bar(-0.9f, i * 0.5f, 0.9f, i * 0.5f, 0.2f); break;
            case "arrow": Bar(-0.9f, 0, 0.6f, 0, 0.2f); Bar(0.95f, 0, 0.45f, 0.45f, 0.18f); Bar(0.95f, 0, 0.45f, -0.45f, 0.18f); break;
            case "wave": for (int i = 0; i < 6; i++) Bar(-0.9f + i * 0.3f, (i % 2) * 0.3f - 0.15f, -0.6f + i * 0.3f, ((i + 1) % 2) * 0.3f - 0.15f, 0.14f); break;
            case "key": Ring(0.32f, 0.12f, 10); Bar(-0.25f, 0.25f, 0.85f, -0.85f, 0.12f); Bar(0.5f, -0.5f, 0.75f, -0.25f, 0.1f); break;
            case "cup": Bar(-0.5f, -0.6f, 0.5f, -0.6f, 0.15f); Bar(-0.55f, 0.6f, -0.45f, -0.6f, 0.15f); Bar(0.55f, 0.6f, 0.45f, -0.6f, 0.15f); Bar(0.55f, 0.3f, 0.85f, 0.1f, 0.12f); Bar(0.85f, 0.1f, 0.52f, -0.25f, 0.12f); break;
            case "wheel": Ring(0.8f, 0.12f); for (int i = 0; i < 3; i++) { float a = i * MathF.PI / 3; Bar(-MathF.Cos(a) * 0.75f, -MathF.Sin(a) * 0.75f, MathF.Cos(a) * 0.75f, MathF.Sin(a) * 0.75f, 0.08f); } break;
            case "spiral":
                for (int i = 0; i < 18; i++)
                {
                    float a0 = i * 0.55f, a1 = (i + 1) * 0.55f, r0 = 0.1f + i * 0.045f, r1 = 0.1f + (i + 1) * 0.045f;
                    Bar(MathF.Cos(a0) * r0, MathF.Sin(a0) * r0, MathF.Cos(a1) * r1, MathF.Sin(a1) * r1, 0.1f);
                }
                break;
            default: Disc(0, 0, 0.6f); break;
        }
    }

    static readonly string[] Emblems = { "disc", "ring", "star", "triangle", "sun", "eye", "stripes", "wave", "spiral", "wheel" };
    static string Emblem(DecoBuilder b) => Emblems[b.Rng.Int(Emblems.Length)];

    // ---- street furniture, by era ---------------------------------------------------------------

    static void Street(Dictionary<string, Action<DecoBuilder, DressArgs>> r)
    {
        r["lamp"] = (b, a) =>
        {
            bool gone = a.Wear > 0.55f && b.Rng.Chance(0.3f);
            switch (a.Era)
            {
                case DecoTech.Low:
                {
                    // A timber post on a stone foot, a bracket, and an oil lantern hanging from it.
                    b.Box(-0.2f, -0.2f, -0.2f, 0.2f, 0.25f, 0.2f, DecoSlot.Stone);
                    Vector3 top = Post(b, a.Era, 0, 0.25f, 0, 3.1f, 1.3f);
                    b.Box(-0.05f, top.Y - 0.12f, -0.75f, 0.05f, top.Y - 0.04f, 0.05f, DecoSlot.Wood).As(DecoFinish.Wood);
                    Rod(b, new Vector3(0, top.Y - 0.6f, -0.05f), new Vector3(0, top.Y - 0.1f, -0.5f), 0.04f, DecoSlot.Wood);
                    if (gone) break;
                    b.Tube(0, top.Y - 0.12f, -0.7f, 0, top.Y - 0.32f, -0.7f, 0.01f, DecoSlot.Dark).Ghost();
                    b.Box(-0.13f, top.Y - 0.72f, -0.83f, 0.13f, top.Y - 0.32f, -0.57f, DecoSlot.Dark);
                    b.Box(-0.09f, top.Y - 0.66f, -0.84f, 0.09f, top.Y - 0.38f, -0.56f, DecoSlot.Lamp).Lit();
                    b.Taper(-0.15f, top.Y - 0.34f, -0.85f, 0.15f, top.Y - 0.24f, -0.55f, 0.3f, 0.3f, DecoSlot.Dark);
                    b.Light(0, top.Y - 0.5f, -0.7f);
                    break;
                }
                case DecoTech.Mid:
                {
                    // A steel column and a curved arm out over the street to a sodium lantern.
                    b.Box(-0.13f, 0, -0.13f, 0.13f, 0.9f, 0.13f, DecoSlot.Metal);
                    Vector3 top = Post(b, a.Era, 0, 0, 0, 6.6f, 1.4f);
                    var p0 = new Vector3(0, top.Y - 0.3f, 0);
                    var p1 = new Vector3(0, top.Y + 0.15f, -0.6f);
                    var p2 = new Vector3(0, top.Y + 0.05f, -1.5f);
                    b.Tube(p0, p1, 0.04f, DecoSlot.Metal, 8);
                    b.Tube(p1, p2, 0.035f, DecoSlot.Metal, 8);
                    if (gone) break;
                    b.Frustum(top.Y - 0.12f, -0.16f, 0.16f, -2.05f, -1.45f, top.Y + 0.08f, -0.12f, 0.12f, -1.95f, -1.5f, DecoSlot.Dark);
                    b.Box(-0.12f, top.Y - 0.15f, -2f, 0.12f, top.Y - 0.11f, -1.5f, DecoSlot.Lamp).Lit();
                    b.Light(0, top.Y - 0.3f, -1.75f);
                    break;
                }
                default:
                {
                    // A slim mast with a light strip down it and a glowing ring at its head: and a holo-plate at eye height.
                    Vector3 top = Post(b, a.Era, 0, 0, 0, 5.2f, 1.3f);
                    if (gone) break;
                    for (int i = 0; i < 12; i++)
                    {
                        float a0 = i * MathF.Tau / 12, a1 = (i + 1) * MathF.Tau / 12;
                        b.Beam(new Vector3(MathF.Cos(a0) * 0.42f, top.Y - 0.05f, MathF.Sin(a0) * 0.42f - 0.2f), new Vector3(MathF.Cos(a1) * 0.42f, top.Y - 0.05f, MathF.Sin(a1) * 0.42f - 0.2f), 0.05f, 0.05f, DecoSlot.Lamp).Lit();
                    }
                    b.Beam(new Vector3(0, top.Y - 0.05f, -0.05f), new Vector3(0, top.Y - 0.05f, -0.62f), 0.03f, 0.03f, DecoSlot.Plastic);
                    b.Box(-0.2f, 1.5f, -0.1f, 0.2f, 1.85f, -0.08f, DecoSlot.Screen).Lit().Ghost();
                    Symbol(b, "arrow", 0, 1.675f, -0.1f, 0.26f, DecoSlot.Dark);
                    b.Light(0, top.Y - 0.3f, -0.2f);
                    break;
                }
            }
        };

        r["sign_post"] = (b, a) =>
        {
            bool blank = a.Style == "blank" || b.Rng.Chance(0.15f);
            string[] kinds = { "arrow", "triangle", "ring", "cross", "disc" };
            switch (a.Era)
            {
                case DecoTech.Low:
                {
                    // A fingerpost: carved boards pointing off, an arrow cut into each.
                    Vector3 top = Post(b, a.Era, 0, 0, 0, 2.6f, 1.2f);
                    int n = 1 + b.Rng.Int(3);
                    for (int i = 0; i < n; i++)
                    {
                        float y = top.Y - 0.3f - i * 0.38f, yaw = b.Rng.Range(-70, 250);
                        using (b.Turn(new Vector3(0, y, 0), 0, yaw))
                        {
                            b.Box(0.08f, y - 0.11f, -0.03f, 0.95f, y + 0.11f, 0.03f, DecoSlot.Wood).As(DecoFinish.Wood);
                            b.Hull(new[] { new Vector3(0.95f, y - 0.11f, -0.03f), new Vector3(1.15f, y, -0.03f), new Vector3(0.95f, y + 0.11f, -0.03f), new Vector3(1.15f, y, -0.03f),
                                new Vector3(0.95f, y - 0.11f, 0.03f), new Vector3(1.15f, y, 0.03f), new Vector3(0.95f, y + 0.11f, 0.03f), new Vector3(1.15f, y, 0.03f) }, DecoSlot.Wood);
                            if (!blank) Symbol(b, "arrow", 0.55f, y, -0.03f, 0.18f, DecoSlot.Dark);
                        }
                    }
                    break;
                }
                case DecoTech.Mid:
                {
                    // Enamel plates on a steel post: a disc, a triangle, an oblong.
                    Vector3 top = Post(b, a.Era, 0, 0, 0, 2.7f);
                    int n = 1 + b.Rng.Int(2);
                    for (int i = 0; i < n; i++)
                    {
                        float y = top.Y - 0.35f - i * 0.7f;
                        b.Tube(0, y, -0.05f, 0, y, -0.08f, 0.3f, i == 0 ? DecoSlot.Trim : DecoSlot.Body, 16);
                        b.Tube(0, y, -0.08f, 0, y, -0.085f, 0.24f, DecoSlot.Linen, 16).Ghost();
                        if (!blank) Symbol(b, kinds[b.Rng.Int(kinds.Length)], 0, y, -0.085f, 0.32f, DecoSlot.Dark);
                    }
                    break;
                }
                default:
                {
                    // A slim pylon with a holo-panel: a lit pictogram if the cells hold.
                    b.Frustum(0, -0.18f, 0.18f, -0.12f, 0.12f, 2.9f, -0.1f, 0.1f, -0.08f, 0.08f, DecoSlot.Plastic);
                    b.Box(-0.42f, 1.7f, -0.16f, 0.42f, 2.55f, -0.12f, DecoSlot.Screen).Lit();
                    if (!blank) Symbol(b, kinds[b.Rng.Int(kinds.Length)], 0, 2.12f, -0.16f, 0.5f, DecoSlot.Linen);
                    b.Light(0, 2.1f, -0.5f, DecoSlot.Screen);
                    break;
                }
            }
        };

        r["awning"] = (b, a) =>
        {
            // Over a shop's door, fixed to its wall (z = 0), reaching out over the pavement.
            float w = R(b, 2.2f, 3f), y = 2.35f, out_ = R(b, 0.9f, 1.3f);
            bool torn = a.Wear > 0.4f && b.Rng.Chance(0.6f);
            switch (a.Era)
            {
                case DecoTech.Low:
                    foreach (float x in new[] { -w / 2, w / 2 })
                    {
                        b.Box(x - 0.04f, y + 0.4f, -0.08f, x + 0.04f, y + 0.5f, 0, DecoSlot.Wood);
                        Rod(b, new Vector3(x, y + 0.45f, -0.04f), new Vector3(x, y, -out_), 0.05f, DecoSlot.Wood);
                        Rod(b, new Vector3(x, y - 0.3f, -0.02f), new Vector3(x, y, -out_ * 0.7f), 0.04f, DecoSlot.Wood);
                    }
                    break;
                case DecoTech.Mid:
                    b.Box(-w / 2, y + 0.42f, -0.18f, w / 2, y + 0.58f, 0, DecoSlot.Metal);
                    foreach (float x in new[] { -w / 2 + 0.1f, w / 2 - 0.1f }) Rod(b, new Vector3(x, y + 0.45f, -0.1f), new Vector3(x, y, -out_), 0.035f, DecoSlot.Metal);
                    break;
                default:
                    // A rigid canopy, a light strip along its edge.
                    b.Wedge(-w / 2, y, -out_, w / 2, 0, y + 0.08f, y + 0.25f, DecoSlot.Plastic);
                    b.Box(-w / 2, y - 0.03f, -out_ - 0.02f, w / 2, y + 0.03f, -out_ + 0.02f, DecoSlot.Lamp).Lit().Ghost();
                    b.Light(0, y - 0.2f, -out_ * 0.6f);
                    return;
            }
            // The cloth, striped, in strips along the slope; torn ones hang down from the frame.
            int strips = Math.Max(4, (int)(w / 0.3f));
            for (int i = 0; i < strips; i++)
            {
                float x0 = -w / 2 + w * i / strips, x1 = -w / 2 + w * (i + 1) / strips;
                DecoSlot s = i % 2 == 0 ? DecoSlot.Fabric : DecoSlot.Linen;
                if (torn && b.Rng.Chance(0.35f))
                {
                    b.Box(x0, y + 0.45f - R(b, 0.5f, 1.4f), -0.06f, x1, y + 0.45f, -0.04f, s).Soft().Ghost();
                    continue;
                }
                b.Hull(new[] { new Vector3(x0, y - 0.02f, -out_), new Vector3(x1, y - 0.02f, -out_), new Vector3(x0, y, -out_), new Vector3(x1, y, -out_),
                    new Vector3(x0, y + 0.43f, -0.02f), new Vector3(x1, y + 0.43f, -0.02f), new Vector3(x0, y + 0.45f, -0.02f), new Vector3(x1, y + 0.45f, -0.02f) }, s).Soft().Ghost().As(DecoFinish.Fabric);
                // The valance at the front.
                b.Hull(new[] { new Vector3(x0, y - 0.22f, -out_ - 0.01f), new Vector3(x1, y - 0.22f, -out_ - 0.01f), new Vector3(x0, y, -out_ - 0.01f), new Vector3(x1, y, -out_ - 0.01f),
                    new Vector3((x0 + x1) / 2, y - 0.22f, -out_ + 0.005f), new Vector3((x0 + x1) / 2, y - 0.22f, -out_ + 0.005f), new Vector3(x0, y, -out_ + 0.005f), new Vector3(x1, y, -out_ + 0.005f) }, s).Soft().Ghost();
            }
        };

        r["shop_sign"] = (b, a) =>
        {
            // A sign standing out from the wall beside the door: what the shop sold, as a picture.
            string[] goods = { "cup", "key", "wheel", "disc", "star", "cross" };
            string what = goods[b.Rng.Int(goods.Length)];
            float y = 2.9f;
            switch (a.Era)
            {
                case DecoTech.Low:
                    b.Box(-0.03f, y + 0.5f, -0.9f, 0.03f, y + 0.56f, 0, DecoSlot.Dark);
                    Rod(b, new Vector3(0, y + 0.1f, -0.02f), new Vector3(0, y + 0.53f, -0.5f), 0.03f, DecoSlot.Dark);
                    if (a.Wear > 0.6f && b.Rng.Chance(0.4f))
                    {
                        // One chain gone: the board hangs from the other by a corner.
                        b.Tube(0, y + 0.5f, -0.8f, 0, y + 0.2f, -0.8f, 0.008f, DecoSlot.Dark).Ghost();
                        using (b.Turn(new Vector3(0, y + 0.2f, -0.8f), 35, 0))
                            b.Box(-0.03f, y - 0.4f, -0.8f, 0.03f, y + 0.2f, -0.2f, DecoSlot.Wood).As(DecoFinish.Wood);
                        break;
                    }
                    foreach (float z in new[] { -0.8f, -0.25f }) b.Tube(0, y + 0.5f, z, 0, y + 0.3f, z, 0.008f, DecoSlot.Dark).Ghost();
                    b.Box(-0.03f, y - 0.3f, -0.85f, 0.03f, y + 0.3f, -0.2f, DecoSlot.Wood).As(DecoFinish.Wood);
                    using (b.Turn(new Vector3(0, y, -0.525f), 0, 90)) { Symbol(b, what, 0, y, -0.56f, 0.42f, DecoSlot.Trim); }
                    using (b.Turn(new Vector3(0, y, -0.525f), 0, -90)) { Symbol(b, what, 0, y, -0.56f, 0.42f, DecoSlot.Trim); }
                    break;
                case DecoTech.Mid:
                    b.Box(-0.1f, y - 0.35f, -0.85f, 0.1f, y + 0.35f, -0.08f, DecoSlot.Body);
                    b.Box(-0.03f, y - 0.05f, -0.08f, 0.03f, y + 0.05f, 0, DecoSlot.Metal);
                    using (b.Turn(new Vector3(0, y, -0.465f), 0, 90)) { Symbol(b, what, 0, y, -0.57f, 0.48f, DecoSlot.Linen); }
                    using (b.Turn(new Vector3(0, y, -0.465f), 0, -90)) { Symbol(b, what, 0, y, -0.57f, 0.48f, DecoSlot.Linen); }
                    break;
                default:
                    // A holo-sign: a thin frame and a glowing panel.
                    b.Box(-0.02f, y - 0.42f, -0.92f, 0.02f, y + 0.42f, -0.88f, DecoSlot.Plastic);
                    b.Box(-0.02f, y + 0.38f, -0.92f, 0.02f, y + 0.42f, 0, DecoSlot.Plastic);
                    b.Box(-0.015f, y - 0.36f, -0.86f, 0.015f, y + 0.36f, -0.14f, DecoSlot.Screen).Lit();
                    using (b.Turn(new Vector3(0, y, -0.5f), 0, 90)) { Symbol(b, what, 0, y, -0.518f, 0.5f, DecoSlot.Linen); }
                    using (b.Turn(new Vector3(0, y, -0.5f), 0, -90)) { Symbol(b, what, 0, y, -0.518f, 0.5f, DecoSlot.Linen); }
                    b.Light(0, y, -0.5f, DecoSlot.Screen);
                    break;
            }
        };
    }
}
