using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>
/// What covers an abandoned piece: when overgrown, green vines and moss or the Hollow Bloom's
/// fleshy growth; when buried, drifted sand or snow.
/// </summary>
public enum DecoCover { Vines, Bloom, Sand, Snow }

/// <summary>
/// Everything that decides a deco mesh. Pieces in one state share one look, so meshes cache well.
/// <paramref name="Doors"/> flings a piece's doors open on top of its state (a rusted car with its
/// doors open, left mid-evacuation).
/// </summary>
public readonly record struct DecoLook(string Piece, DecoState State, DecoFall Fall, bool Powered, DecoCover Cover, string Palette, bool Doors = false, DecoAge Age = DecoAge.Fresh, bool Exposed = false);

/// <summary>
/// How long since anyone used it. The collapse was a century or two ago: in an old-world place,
/// fast-rotting things (hay, food, crops) are gone or remnants, sturdy indoor furnishings survive
/// faded, dusty and worn, exposed timber has rotted or fallen in, metal left outside is rust and
/// vehicles are hulks. Fresh is a place in use now (a new farm, a faction base, a gang camp);
/// preserved is sealed, frozen or airless: near fresh, but dusty or frosted.
/// </summary>
public enum DecoAge { Fresh, OldWorld, Preserved }

/// <summary>
/// Makes deco meshes: runs a piece's builder, applies its state (broken or crushed, burnt,
/// overgrown, rusted with flat tyres, doors open and glass gone, half buried), topples it over
/// for a knocked-over look so it really rests on the floor, and emits flat-shaded triangles,
/// collision boxes and light spots. Plain C#; caches models, shapes and meshes.
/// </summary>
public sealed class DecoForge
{
    public DecoCatalogue Catalogue { get; }
    readonly Dictionary<string, DecoModel> _models = new(StringComparer.Ordinal);
    readonly Dictionary<DecoLook, DecoMeshData> _meshes = new();
    readonly Dictionary<(string, DecoState, DecoFall, bool, DecoAge, bool), Shape> _shapes = new();
    readonly Dictionary<string, DecoPalette> _palettes = new(StringComparer.Ordinal);

    public DecoForge(DecoCatalogue catalogue) => Catalogue = catalogue;

    static readonly DecoRgb Dust = DecoRgb.Hex("b4ab9c"), Mould = DecoRgb.Hex("2e3a26"), Weathered = DecoRgb.Hex("8a857a"), Bleached = DecoRgb.Hex("dcd8cc"), DryBrown = DecoRgb.Hex("7a6a48");
    static DecoRgb Stain(DecoPalette pal) => pal[DecoSlot.Grime].Scale(0.55f);

    public int CachedMeshes => _meshes.Count;
    public void ClearMeshes() => _meshes.Clear();

    /// <summary>A palette by id (from deco.json), or one registered with <see cref="AddPalette"/>.</summary>
    public DecoPalette Palette(string id)
    {
        if (!_palettes.TryGetValue(id, out DecoPalette p)) _palettes[id] = p = DecoPalette.From(Catalogue, id);
        return p;
    }

    /// <summary>Registers a palette made for a place (a style kit's tint) under its id.</summary>
    public void AddPalette(DecoPalette p) => _palettes[p.Id] = p;

    /// <summary>The piece as its builder made it (cached; clone before changing).</summary>
    public DecoModel Model(string id)
    {
        if (_models.TryGetValue(id, out DecoModel m)) return m;
        DecoPiece piece = Catalogue.Piece(id);
        if (!DecoModels.All.TryGetValue(id, out Action<DecoBuilder> build)) throw new KeyNotFoundException($"no model builder for deco piece '{id}'");
        var b = new DecoBuilder(piece.Width, piece.Height, piece.Depth, DecoHash.Fnv(id));
        build(b);
        _models[id] = b.Model;
        return b.Model;
    }

    public static bool HasBuilder(string id) => DecoModels.All.ContainsKey(id);

    // ---- shapes: what a state does to a piece's parts, and how it lies ---------------------------

    /// <summary>A piece's parts in one state and fall, before colouring: what meshes and footprints are made from.</summary>
    public sealed class Shape
    {
        public DecoModel Model;
        /// <summary>The topple: from standing to lying (identity when upright).</summary>
        public Matrix4x4 Settle = Matrix4x4.Identity;
        public Vector3 Min, Max, CentreOfMass;
        /// <summary>Screens that crack.</summary>
        public List<DecoPart> Cracked = new();
        /// <summary>Panes that shattered: shards lie on the floor beneath them.</summary>
        public List<Vector3> Shattered = new();
        /// <summary>Where something rotted away: a dark stain at that spot and height.</summary>
        public List<(Vector3 At, float Rx, float Rz)> Stains = new();
        public bool Stable = true;
        /// <summary>Buried: how far it sank, and the drift's reach beyond its footprint.</summary>
        public float Sink, Drift;
        /// <summary>Crushed: the height above which the piece is squashed, and by how much.</summary>
        public float CrushAt = float.MaxValue, CrushBy = 1f;
        /// <summary>Cables whose anchors were gone and that had to be re-hung or dropped (a builder's intact piece needs none).</summary>
        public int Repaired;
    }

    /// <summary>The parts of a piece in a state and fall, settled on the floor (cached).</summary>
    public Shape ShapeOf(string id, DecoState state, DecoFall fall, bool doors = false, DecoAge age = DecoAge.Fresh, bool exposed = false)
    {
        if (_shapes.TryGetValue((id, state, fall, doors, age, exposed), out Shape s)) return s;
        DecoPiece piece = Catalogue.Piece(id);
        DecoSet set = Catalogue.Set(piece.Set);
        var rng = new DecoRng(DecoHash.Combine(DecoHash.Fnv(id), (ulong)state * 7919UL));
        DecoModel model = Model(id).Clone();
        s = new Shape { Model = model };
        if (age == DecoAge.OldWorld) Weather(piece, model, s, exposed, new DecoRng(DecoHash.Combine(DecoHash.Fnv(id), exposed ? 77UL : 78UL)));
        switch (state)
        {
            case DecoState.Broken:
                Break(piece, model, s, rng, set.Crush);
                break;
            case DecoState.Burnt:
                Burn(model, rng);
                Shatter(model, s, 1f);
                FlatTyres(model, 0.45f);
                break;
            case DecoState.Rusted when piece.Outdoor:
                // A rusted hulk: tyres rotted off (it sits on its hubs), seats and trim gone, most glass gone.
                Hulk(model, s);
                break;
            case DecoState.Rusted:
                FlatTyres(model, 0.22f);
                if (rng.Chance(0.4f)) Shatter(model, s, 0.35f);
                break;
            case DecoState.Open:
                Shatter(model, s, 1f);
                break;
        }
        if (doors || state == DecoState.Open) OpenDoors(model, rng);
        s.Repaired = HangCables(model, state, rng);

        List<Vector3> verts = model.Parts.SelectMany(DecoEmit.Vertices).ToList();
        Vector3 com = DecoEmit.CentreOfMass(model.Parts);
        if (fall != DecoFall.None)
        {
            s.Settle = Topple(verts, com, fall, out bool stable);
            s.Stable = stable;
            Apply(model, s.Settle);
            verts = verts.Select(v => Vector3.Transform(v, s.Settle)).ToList();
            com = Vector3.Transform(com, s.Settle);
            s.Shattered = s.Shattered.Select(v => Vector3.Transform(v, s.Settle) with { Y = 0 }).ToList();
            s.Stains.Clear();
        }
        if (state == DecoState.Buried)
        {
            float h = verts.Max(v => v.Y);
            s.Sink = h * rng.Range(0.28f, 0.42f);
            s.Drift = MathF.Min(0.6f, 0.15f + h * 0.12f);
            Apply(model, DecoGeo.Move(0, -s.Sink, 0));
            verts = verts.Select(v => v with { Y = MathF.Max(0, v.Y - s.Sink) }).ToList();
            com.Y -= s.Sink;
        }
        s.CentreOfMass = com;
        s.Min = verts.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        s.Max = verts.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        if (s.Drift > 0)
        {
            s.Min -= new Vector3(s.Drift, 0, s.Drift);
            s.Max += new Vector3(s.Drift, 0, s.Drift);
        }
        _shapes[(id, state, fall, doors, age, exposed)] = s;
        return s;
    }

    static void Apply(DecoModel model, Matrix4x4 m)
    {
        foreach (DecoPart p in model.Parts) { p.M *= m; p.Hinge = Vector3.Transform(p.Hinge, m); p.HingeAxis = Vector3.TransformNormal(p.HingeAxis, m); }
        for (int i = 0; i < model.Lights.Count; i++) model.Lights[i] = model.Lights[i] with { At = Vector3.Transform(model.Lights[i].At, m) };
        if (model.Spark is Vector3 sp) model.Spark = Vector3.Transform(sp, m);
        foreach (DecoCable c in model.Cables) { c.A = Vector3.Transform(c.A, m); c.B = Vector3.Transform(c.B, m); }
        foreach (string k in model.Anchors.Keys.ToList()) model.Anchors[k] = Vector3.Transform(model.Anchors[k], m);
    }

    /// <summary>True if <paramref name="p"/> touches one of the parts (within <paramref name="tol"/> metres of its box).</summary>
    public static bool OnParts(Vector3 p, IEnumerable<DecoPart> parts, float tol = 0.025f)
    {
        foreach (DecoPart part in parts)
        {
            if (!Matrix4x4.Invert(part.M, out Matrix4x4 inv)) continue;
            Vector3 q = Vector3.Transform(p, inv);
            float lx = new Vector3(part.M.M11, part.M.M12, part.M.M13).Length(), ly = new Vector3(part.M.M21, part.M.M22, part.M.M23).Length(), lz = new Vector3(part.M.M31, part.M.M32, part.M.M33).Length();
            if (MathF.Abs(q.X) <= 0.5f + tol / MathF.Max(lx, 1e-4f) && MathF.Abs(q.Y) <= 0.5f + tol / MathF.Max(ly, 1e-4f) && MathF.Abs(q.Z) <= 0.5f + tol / MathF.Max(lz, 1e-4f)) return true;
        }
        return false;
    }

    /// <summary>
    /// Keeps every cable attached: a burnt rope is gone; a broken piece cuts some spans at one end,
    /// and that end drops; a span whose anchor part is missing hangs from the end still held; a cable
    /// with nothing left to hold it is removed. Returns how many had to be re-hung for lost anchors.
    /// </summary>
    static int HangCables(DecoModel m, DecoState state, DecoRng rng)
    {
        if (state == DecoState.Burnt) m.Cables.RemoveAll(c => c.Slot is DecoSlot.Canvas or DecoSlot.Fabric or DecoSlot.Linen or DecoSlot.Plant);
        if (state == DecoState.Broken)
            foreach (DecoCable c in m.Cables.Where(c => c.Spanned && c.Breakable))
                if (rng.Chance(0.6f)) Cut(c, keepA: rng.Chance(0.5f));
        int repaired = 0;
        foreach (DecoCable c in m.Cables.ToList())
        {
            bool a = OnParts(c.A, m.Parts), b = c.Spanned && OnParts(c.B, m.Parts);
            if (c.Spanned && a && b) continue;
            if (!c.Spanned && a) continue;
            repaired++;
            if (c.Spanned && b) Cut(c, keepA: false);
            else if (c.Spanned && a) Cut(c, keepA: true);
            else m.Cables.Remove(c);
        }
        return repaired;
    }

    static void Cut(DecoCable c, bool keepA)
    {
        if (!keepA) (c.A, c.B) = (c.B, c.A);
        c.Spanned = false;
        c.Tail = MathF.Max(0.1f, Vector3.Distance(c.A, c.B) * 0.5f);
    }

    /// <summary>Cables of a shape that aren't attached: an anchored end off the piece, or a hanging end not on the ground. Empty when all is well.</summary>
    public static List<string> LooseCables(Shape s)
    {
        var problems = new List<string>();
        for (int i = 0; i < s.Model.Cables.Count; i++)
        {
            DecoCable c = s.Model.Cables[i];
            if (!OnParts(c.A, s.Model.Parts)) problems.Add($"cable {i}: its anchor {c.A} touches nothing");
            if (c.Spanned && !OnParts(c.B, s.Model.Parts)) problems.Add($"cable {i}: its second anchor {c.B} touches nothing");
            if (!c.Spanned)
            {
                var path = DecoCables.Path(c);
                if (path[^1].Y > c.Radius + 0.01f) problems.Add($"cable {i}: its free end hangs in the air at {path[^1]}");
            }
        }
        return problems;
    }

    void Break(DecoPiece piece, DecoModel m, Shape s, DecoRng rng, bool crush)
    {
        var parts = m.Parts;
        // Screens go dark and crack; loose panes shatter away.
        foreach (DecoPart p in parts.Where(p => p.Is(PartFlags.Screen)).ToList()) s.Cracked.Add(p);
        Shatter(m, s, 1f);
        // Missing parts: one or two loose ones come off (all loose doors on a crushed vehicle); if none are loose, a small part goes.
        var loose = parts.Where(p => p.Is(PartFlags.Loose)).ToList();
        if (loose.Count > 0)
        {
            int n = crush ? Math.Max(1, loose.Count / 2) : Math.Min(loose.Count, 1 + rng.Int(2));
            rng.Shuffle(loose);
            foreach (DecoPart p in loose.Take(n)) parts.Remove(p);
        }
        else if (parts.Count > 4)
        {
            var small = parts.Where(p => !p.Is(PartFlags.Screen)).OrderBy(p => p.Volume).Take(Math.Max(1, parts.Count / 3)).ToList();
            parts.Remove(small[rng.Int(small.Count)]);
        }
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Vector3 v in parts.SelectMany(DecoEmit.Vertices)) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        float h = MathF.Max(0.2f, max.Y - min.Y);
        if (crush)
        {
            // Crushed: everything above the waist is pressed down, and the tyres burst.
            FlatTyres(m, 0.4f);
            s.CrushAt = min.Y + h * rng.Range(0.42f, 0.55f);
            s.CrushBy = rng.Range(0.35f, 0.55f);
            return;
        }
        // A bent frame: the whole piece leans, as far as its footprint allows.
        bool alongX = rng.Chance(0.5f);
        float slackLo = alongX ? min.X - (-piece.Width / 2) : min.Z - (-piece.Depth / 2);
        float slackHi = alongX ? piece.Width / 2 - max.X : piece.Depth / 2 - max.Z;
        float lean = rng.Chance(0.5f) ? MathF.Min(0.12f * h, slackHi) : -MathF.Min(0.12f * h, slackLo);
        if (MathF.Abs(lean) > 0.01f && piece.Mount != DecoMount.Ceiling)
        {
            float k = lean / h;
            Matrix4x4 shear = Matrix4x4.Identity;
            if (alongX) shear.M21 = k; else shear.M23 = k;
            Apply(m, shear);
        }
    }

    /// <summary>
    /// A century or two of neglect. Fast-rotting things (hay, food, crops) are gone or a remnant,
    /// with a stain where they lay. Sturdy soft things indoors survive worn and sagging; exposed to
    /// the weather they rot away. Exposed timber rots and falls in. Outdoor vehicles are hulks.
    /// </summary>
    static void Weather(DecoPiece piece, DecoModel m, Shape s, bool exposed, DecoRng rng)
    {
        foreach (DecoPart p in m.Parts.Where(p => p.Is(PartFlags.Perishable)).ToList())
        {
            if (rng.Chance(0.8f)) { m.Parts.Remove(p); Stain(s, p); }
            else Shrivel(p, 0.22f);
        }
        // What a piece is made of stays in some form: a suitcase keeps its bag, a lamp its shade, a sofa its seat. Only the trim goes.
        DecoPart main = m.Parts.Where(p => p.Is(PartFlags.Soft) && !p.Is(PartFlags.Perishable)).OrderByDescending(p => p.Volume).FirstOrDefault();
        foreach (DecoPart p in m.Parts.Where(p => p.Is(PartFlags.Soft) && !p.Is(PartFlags.Perishable)).ToList())
        {
            if (exposed)
            {
                if (p == main) continue;
                else if (rng.Chance(0.65f)) { m.Parts.Remove(p); Stain(s, p); }
                else Shrivel(p, 0.5f);
            }
            else if (rng.Chance(0.2f)) p.M = Matrix4x4.CreateScale(0.94f, 0.82f, 0.94f) * DecoGeo.Move(0, -0.09f, 0) * p.M;
        }
        if (!exposed) return;
        var wood = m.Parts.Where(p => p.Slot == DecoSlot.Wood).ToList();
        float woodShare = wood.Sum(p => p.Volume) / MathF.Max(1e-4f, m.Parts.Sum(p => p.Volume));
        foreach (DecoPart p in wood)
            if (rng.Chance(p.Is(PartFlags.Loose) ? 0.6f : 0.2f) && p.Volume < 0.25f * wood.Sum(w => w.Volume)) m.Parts.Remove(p);
        if (woodShare > 0.5f)
        {
            // Mostly timber: it sags and leans as it rots.
            var shear = Matrix4x4.Identity;
            shear.M21 = rng.Range(-0.08f, 0.08f);
            shear.M23 = rng.Range(-0.05f, 0.05f);
            Apply(m, shear);
        }
        if (piece.Outdoor) Hulk(m, s);
        else Shatter(m, s, 0.4f);
    }

    /// <summary>An outdoor vehicle gone to a hulk: tyres rotted off so it sits on its wheel rims, soft trim gone, most glass gone.</summary>
    static void Hulk(DecoModel m, Shape s)
    {
        if (m.Parts.Any(p => p.Is(PartFlags.Tyre)))
        {
            FlatTyres(m, 0.39f); // down onto its rims, which stand 0.6 of a wheel's height
            m.Parts.RemoveAll(p => p.Is(PartFlags.Tyre));
        }
        foreach (DecoPart p in m.Parts.Where(p => p.Is(PartFlags.Soft)).ToList()) { m.Parts.Remove(p); Stain(s, p); }
        Shatter(m, s, 0.8f);
    }

    /// <summary>A part shrinks down onto its base (a rotted bale, a rag where a cushion was).</summary>
    static void Shrivel(DecoPart p, float keep) => p.M = Matrix4x4.CreateScale(0.9f, keep, 0.9f) * DecoGeo.Move(0, -(1 - keep) * 0.5f, 0) * p.M;

    /// <summary>A dark stain left where something rotted away.</summary>
    static void Stain(Shape s, DecoPart p)
    {
        var v = DecoEmit.Vertices(p);
        Vector3 lo = v.Aggregate(Vector3.Min), hi = v.Aggregate(Vector3.Max);
        if (hi.X - lo.X < 0.08f || hi.Z - lo.Z < 0.08f) return;
        s.Stains.Add((new Vector3((lo.X + hi.X) / 2, lo.Y, (lo.Z + hi.Z) / 2), (hi.X - lo.X) * 0.45f, (hi.Z - lo.Z) * 0.45f));
    }

    /// <summary>Panes shatter out of their frames (a share of them); their shards fall below.</summary>
    static void Shatter(DecoModel m, Shape s, float share)
    {
        var panes = m.Parts.Where(p => p.Is(PartFlags.Pane) && !p.Is(PartFlags.Screen)).ToList();
        for (int i = 0; i < panes.Count; i++)
        {
            if (share < 1f && DecoHash.Unit((ulong)i * 31 + 7) > share) continue;
            m.Parts.Remove(panes[i]);
            s.Shattered.Add(panes[i].Centre with { Y = 0 });
        }
    }

    static void Burn(DecoModel m, DecoRng rng)
    {
        // Cloth, cushions, paper and leaves burn away, or shrivel to half.
        foreach (DecoPart p in m.Parts.Where(p => p.Is(PartFlags.Soft)).ToList())
        {
            if (rng.Chance(0.55f)) m.Parts.Remove(p);
            else p.M = Matrix4x4.CreateScale(0.88f, 0.45f, 0.88f) * DecoGeo.Move(0, -0.275f, 0) * p.M;
        }
    }

    /// <summary>
    /// Tyres go flat: each squashes down onto its rim by <paramref name="share"/> of its height and
    /// everything else settles by the same amount, so the piece still stands on its tyres.
    /// </summary>
    static void FlatTyres(DecoModel m, float share)
    {
        var tyres = m.Parts.Where(p => p.Is(PartFlags.Tyre)).ToList();
        if (tyres.Count == 0) return;
        float drop = float.MaxValue;
        foreach (DecoPart t in tyres)
        {
            var v = DecoEmit.Vertices(t);
            float lo = v.Min(x => x.Y), hi = v.Max(x => x.Y);
            drop = MathF.Min(drop, (hi - lo) * share * 0.5f);
        }
        foreach (DecoPart t in tyres)
        {
            var v = DecoEmit.Vertices(t);
            float lo = v.Min(x => x.Y), hi = v.Max(x => x.Y), height = hi - lo;
            float k = MathF.Max(0.2f, (height - drop) / height);
            t.M *= DecoGeo.Move(0, -lo, 0) * Matrix4x4.CreateScale(1.06f, k, 1.06f) * DecoGeo.Move(0, lo, 0);
        }
        var lower = DecoGeo.Move(0, -drop, 0);
        foreach (DecoPart p in m.Parts.Where(p => !p.Is(PartFlags.Tyre))) { p.M *= lower; p.Hinge = Vector3.Transform(p.Hinge, lower); }
        for (int i = 0; i < m.Lights.Count; i++) m.Lights[i] = m.Lights[i] with { At = Vector3.Transform(m.Lights[i].At, lower) };
    }

    /// <summary>Doors swing open on their hinges, each a little differently.</summary>
    static void OpenDoors(DecoModel m, DecoRng rng)
    {
        foreach (DecoPart p in m.Parts.Where(p => p.Is(PartFlags.Door)))
        {
            float angle = p.OpenAngle * rng.Range(0.55f, 1f);
            p.M *= DecoGeo.About(p.Hinge, Matrix4x4.CreateFromAxisAngle(p.HingeAxis, angle * DecoGeo.Deg));
        }
    }

    /// <summary>
    /// Tips a piece over the way it falls, then lets it settle: while its centre of mass isn't over
    /// what touches the floor, it rolls about the nearest supporting edge until another corner
    /// lands. The result rests on the floor: nothing floats and nothing sinks.
    /// </summary>
    public static Matrix4x4 Topple(List<Vector3> verts, Vector3 com, DecoFall fall, out bool stable)
    {
        Matrix4x4 t = fall switch
        {
            DecoFall.Back => DecoGeo.RotX(90),
            DecoFall.Front => DecoGeo.RotX(-90),
            DecoFall.Left => DecoGeo.RotZ(90),
            DecoFall.Right => DecoGeo.RotZ(-90),
            DecoFall.Over => DecoGeo.RotZ(180),
            _ => Matrix4x4.Identity,
        };
        Vector3 nudge = fall switch
        {
            DecoFall.Back => Vector3.UnitZ, DecoFall.Front => -Vector3.UnitZ, DecoFall.Left => -Vector3.UnitX, DecoFall.Right => Vector3.UnitX, _ => Vector3.UnitX,
        };
        var pts = verts.Select(v => Vector3.Transform(v, t)).ToList();
        Vector3 c = Vector3.Transform(com, t);
        void Ground()
        {
            float minY = pts.Min(p => p.Y);
            Matrix4x4 g = DecoGeo.Move(0, -minY, 0);
            t *= g;
            for (int i = 0; i < pts.Count; i++) pts[i] = Vector3.Transform(pts[i], g);
            c = Vector3.Transform(c, g);
        }
        Ground();
        stable = false;
        for (int iter = 0; iter < 160; iter++)
        {
            var contacts = pts.Where(p => p.Y < 0.002f).Select(p => new Vector2(p.X, p.Z)).ToList();
            List<Vector2> hull = DecoGeo.Hull(contacts);
            var cxz = new Vector2(c.X, c.Z);
            Vector2 q = DecoGeo.ClosestOnHull(hull, cxz, out bool inside, 0.006f);
            if (inside) { stable = true; break; }
            Vector2 d2 = cxz - q;
            if (d2.LengthSquared() < 1e-8f)
            {
                // Balanced on an edge: roll off it sideways (across the edge, not along it).
                Vector2 along = hull.Count >= 2 ? hull[^1] - hull[0] : Vector2.Zero;
                d2 = along.LengthSquared() > 1e-8f ? new Vector2(-along.Y, along.X) : new Vector2(nudge.X, nudge.Z);
                if (Vector2.Dot(d2, new Vector2(nudge.X, nudge.Z)) < 0) d2 = -d2;
            }
            Vector3 u = Vector3.Normalize(new Vector3(d2.X, 0, d2.Y));
            Vector3 axis = Vector3.Cross(Vector3.UnitY, u);
            var pivot = new Vector3(q.X, 0, q.Y);
            float best = float.MaxValue;
            foreach (Vector3 p in pts)
            {
                float s = Vector3.Dot(p - pivot, u), h = p.Y;
                if (s > 1e-4f && h > 1e-5f) best = MathF.Min(best, MathF.Atan2(h, s));
            }
            if (best == float.MaxValue || best < 1e-5f) best = 0.02f;
            Matrix4x4 r = DecoGeo.About(pivot, Matrix4x4.CreateFromAxisAngle(axis, best));
            t *= r;
            for (int i = 0; i < pts.Count; i++) pts[i] = Vector3.Transform(pts[i], r);
            c = Vector3.Transform(c, r);
            Ground();
        }
        return t;
    }

    // ---- meshes ------------------------------------------------------------------------------

    /// <summary>The finished mesh for a look (cached).</summary>
    public DecoMeshData Mesh(DecoLook look)
    {
        if (_meshes.TryGetValue(look, out DecoMeshData m)) return m;
        m = Build(look);
        _meshes[look] = m;
        return m;
    }

    public DecoMeshData Mesh(string piece, DecoState state = DecoState.Intact, DecoFall fall = DecoFall.None, bool powered = true, DecoCover cover = DecoCover.Vines, string palette = null, bool doors = false) =>
        Mesh(new DecoLook(piece, state, fall, powered, cover, palette ?? Catalogue.Piece(piece).Palette, doors));

    /// <summary>The cover a state uses when none is asked for: vines when overgrown, sand when buried.</summary>
    public static DecoCover CoverFor(DecoState state, DecoCover asked) => state == DecoState.Buried
        ? (asked is DecoCover.Sand or DecoCover.Snow ? asked : DecoCover.Sand)
        : (asked is DecoCover.Vines or DecoCover.Bloom ? asked : DecoCover.Vines);

    DecoMeshData Build(DecoLook look)
    {
        DecoPiece piece = Catalogue.Piece(look.Piece);
        DecoSet set = Catalogue.Set(piece.Set);
        DecoPalette pal = Palette(look.Palette);
        Shape shape = ShapeOf(look.Piece, look.State, look.Fall, look.Doors, look.Age, look.Exposed);
        DecoModel model = shape.Model;
        DecoCover cover = CoverFor(look.State, look.Cover);
        bool damaged = look.State is not (DecoState.Intact or DecoState.Knocked);
        bool powered = (look.Powered && !damaged) || set.SelfLit;
        bool selfLitDim = set.SelfLit && damaged;
        ulong salt = DecoHash.Combine(DecoHash.Fnv(look.Piece), (ulong)look.State + 1);
        var mesh = new DecoMeshData { SelfLit = set.SelfLit };
        var rng = new DecoRng(salt);

        // The box the piece must stay inside when it stands (its declared footprint).
        var boxMin = new Vector3(-piece.Width / 2, 0, -piece.Depth / 2);
        var boxMax = new Vector3(piece.Width / 2, piece.Height, piece.Depth / 2);
        bool upright = look.Fall == DecoFall.None;
        // Open doors and drifts reach past the footprint; everything else that stands stays inside it.
        bool clampBox = upright && look.State != DecoState.Intact && !look.Doors && look.State is not (DecoState.Open or DecoState.Buried);
        float dent = look.State switch { DecoState.Broken => 0.016f, DecoState.Burnt => 0.01f, DecoState.Rusted => 0.006f, _ => 0f };
        bool crush = shape.CrushAt < float.MaxValue;
        float crushAt = shape.CrushAt, crushBy = shape.CrushBy;
        Func<Vector3, Vector3> warp = null;
        if (dent > 0 || clampBox || crush || look.State == DecoState.Buried)
            warp = v =>
            {
                if (crush && v.Y > crushAt) v.Y = crushAt + (v.Y - crushAt) * crushBy;
                if (dent > 0)
                {
                    float jx = (DecoHash.At(v, salt) - 0.5f) * 2 * dent, jz = (DecoHash.At(v, salt + 1) - 0.5f) * 2 * dent;
                    float jy = v.Y < 0.01f ? 0 : (DecoHash.At(v, salt + 2) - 0.5f) * 2 * dent;
                    v += new Vector3(jx, jy, jz);
                }
                if (v.Y < 0) v.Y = 0;
                if (clampBox) v = Vector3.Clamp(v, boxMin, boxMax);
                return v;
            };

        DecoRgb ColorOf(DecoPart part, Vector3 at, Vector3 n, DecoSlot slot)
        {
            DecoRgb col = pal[slot];
            if (DecoPalette.Glows(slot))
            {
                if (part.Is(PartFlags.Screen) && (!powered || shape.Cracked.Contains(part))) col = pal[DecoSlot.Dead];
                else if (!powered) col = col.Scale(0.3f).Lerp(pal[DecoSlot.Dead], 0.4f);
                else if (selfLitDim) col = col.Scale(0.75f);
            }
            float noise = DecoHash.At(at, salt + 7);
            // Big patches (rust blooms, scorches) read from a distance: noise on a coarse lattice.
            float patch = DecoHash.At(new Vector3(MathF.Floor(at.X * 2.2f), MathF.Floor(at.Y * 2.2f), MathF.Floor(at.Z * 2.2f)), salt + 9);
            DecoSurface surface = DecoPalette.SurfaceOf(slot);
            switch (look.State)
            {
                case DecoState.Burnt:
                {
                    float k = 0.58f + 0.3f * noise + (n.Y > 0.5f ? 0.08f : 0f);
                    if (surface == DecoSurface.Metal) k -= 0.12f;
                    col = col.Lerp(pal[DecoSlot.Char], Math.Clamp(k, 0, 0.95f));
                    if (patch > 0.75f && surface != DecoSurface.Glass) col = col.Lerp(pal[DecoSlot.Rust], 0.35f);
                    break;
                }
                case DecoState.Overgrown:
                {
                    DecoRgb moss = cover == DecoCover.Bloom ? pal[DecoSlot.Bloom].Lerp(pal[DecoSlot.BloomVein], noise * 0.5f) : pal[DecoSlot.Moss].Lerp(pal[DecoSlot.Leaf], noise * 0.4f);
                    col = col.Scale(0.8f);
                    if (piece.Outdoor && slot is not (DecoSlot.Glass or DecoSlot.Rubber)) col = col.Lerp(pal[DecoSlot.Rust], 0.35f * patch);
                    if (n.Y > 0.55f) col = col.Lerp(moss, 0.5f + 0.4f * noise);
                    else if (at.Y < 0.3f) col = col.Lerp(moss, (0.3f - at.Y) / 0.3f * (0.3f + 0.4f * noise));
                    else if (noise > 0.72f) col = col.Lerp(moss, 0.35f);
                    break;
                }
                case DecoState.Rusted:
                case DecoState.Open when piece.Outdoor:
                case DecoState.Buried when piece.Outdoor:
                {
                    // Paint fades and rust blooms in patches, worst low down and on top; glass goes milky.
                    if (surface == DecoSurface.Glass) { col = col.Lerp(pal[DecoSlot.Grime], 0.55f); break; }
                    if (slot is DecoSlot.Rubber) { col = col.Scale(0.8f); break; }
                    if (slot is DecoSlot.Fabric or DecoSlot.Linen or DecoSlot.Canvas or DecoSlot.Paper) { col = col.Lerp(pal[DecoSlot.Grime], 0.45f); break; }
                    float r = 0.25f + 0.5f * patch + 0.2f * noise + (n.Y > 0.6f ? 0.15f : 0) + (at.Y < 0.6f ? 0.15f : 0);
                    col = col.Scale(0.85f).Lerp(pal[DecoSlot.Rust].Lerp(pal[DecoSlot.Grime], noise * 0.5f), Math.Clamp(r, 0, 0.9f));
                    break;
                }
                case DecoState.Broken:
                    col = col.Scale(0.85f + 0.12f * noise);
                    if (piece.Outdoor && patch > 0.6f && surface != DecoSurface.Glass) col = col.Lerp(pal[DecoSlot.Rust], 0.4f);
                    break;
                case DecoState.Open:
                case DecoState.Buried:
                    col = col.Scale(0.85f + 0.1f * noise);
                    break;
            }
            if (look.Age == DecoAge.OldWorld && look.State != DecoState.Burnt) col = Aged(col, slot, surface, n, noise, patch);
            else if (look.Age == DecoAge.Preserved)
            {
                // Sealed, frozen or airless: near as it was, under dust or frost.
                DecoRgb over = cover == DecoCover.Snow ? pal[DecoSlot.Frost] : Dust;
                col = col.Lerp(over, n.Y > 0.6f ? (cover == DecoCover.Snow ? 0.6f : 0.38f) : 0.1f + 0.08f * noise);
            }
            return col;
        }

        // A century or two of neglect: fabrics fade and spot with mould, timber greys, plastics go
        // pale and brittle, metal dulls (rusts outdoors), everything indoors wears a coat of dust.
        DecoRgb Aged(DecoRgb col, DecoSlot slot, DecoSurface surface, Vector3 n, float noise, float patch)
        {
            bool ex = look.Exposed;
            if (surface == DecoSurface.Glass) return col.Lerp(pal[DecoSlot.Grime], ex ? 0.6f : 0.4f);
            if (surface == DecoSurface.Glow) return col;
            switch (slot)
            {
                case DecoSlot.Fabric or DecoSlot.Linen or DecoSlot.Canvas or DecoSlot.Paper:
                {
                    float grey = col.Luma;
                    col = col.Lerp(new DecoRgb(grey, grey, grey), 0.45f).Lerp(Dust, 0.15f);
                    if (noise > (ex ? 0.68f : 0.86f)) col = col.Lerp(Mould, 0.6f);
                    break;
                }
                case DecoSlot.Wood:
                    col = ex ? col.Lerp(Weathered, 0.55f) : col.Scale(0.85f).Lerp(pal[DecoSlot.Grime], 0.15f);
                    if (ex && noise > 0.75f) col = col.Lerp(Mould, 0.5f);
                    break;
                case DecoSlot.Plastic:
                    col = col.Lerp(Bleached, ex ? 0.45f : 0.3f).Scale(0.95f);
                    break;
                case DecoSlot.Plant or DecoSlot.Moss or DecoSlot.Leaf:
                    col = col.Lerp(DryBrown, 0.75f);
                    break;
                case DecoSlot.Rubber:
                    col = col.Lerp(Bleached, 0.15f);
                    break;
                default:
                    if (ex || piece.Outdoor) col = col.Scale(0.85f).Lerp(pal[DecoSlot.Rust].Lerp(pal[DecoSlot.Grime], noise * 0.5f), Math.Clamp(0.2f + 0.55f * patch + 0.15f * noise, 0, 0.85f));
                    else
                    {
                        col = col.Scale(0.88f);
                        if (patch > 0.85f && surface == DecoSurface.Metal) col = col.Lerp(pal[DecoSlot.Rust], 0.3f);
                    }
                    break;
            }
            if (!ex && n.Y > 0.6f) col = col.Lerp(Dust, 0.3f);
            return col;
        }

        DecoSurface SurfaceFor(DecoPart part, DecoSlot slot)
        {
            DecoSurface s = DecoPalette.SurfaceOf(slot);
            if (s == DecoSurface.Glow && (!powered || (part.Is(PartFlags.Screen) && shape.Cracked.Contains(part)))) return DecoSurface.Matte;
            if (s == DecoSurface.Glass && look.State == DecoState.Burnt) return DecoSurface.Matte;
            if (s == DecoSurface.Metal && (look.State is DecoState.Rusted or DecoState.Burnt || (look.Age == DecoAge.OldWorld && (look.Exposed || piece.Outdoor)))) return DecoSurface.Matte;
            return s;
        }

        foreach (DecoPart part in model.Parts)
            DecoEmit.Part(mesh, part, (at, n, slot) => ColorOf(part, at, n, slot), slot => SurfaceFor(part, slot), warp);
        // Cables hang now, after any toppling: spans sag, cut ones drop to the floor.
        foreach (DecoCable c in model.Cables)
            foreach (DecoPart t in DecoCables.Tubes(DecoCables.Path(c), c.Radius, c.Slot))
                DecoEmit.Part(mesh, t, (at, n, slot) => ColorOf(t, at, n, slot), slot => SurfaceFor(t, slot), warp);

        foreach (DecoPart screen in shape.Cracked) Cracks(mesh, screen, pal, rng);
        mesh.RecomputeBounds();

        // What lies on the floor around it: inside the footprint when it stands, inside what it covers when it lies.
        Vector3 floorMin = upright ? boxMin : new Vector3(shape.Min.X, 0, shape.Min.Z);
        Vector3 floorMax = upright ? boxMax : new Vector3(shape.Max.X, 0, shape.Max.Z);
        if (look.State == DecoState.Buried) { floorMin = shape.Min with { Y = 0 }; floorMax = shape.Max with { Y = 0 }; }
        bool onFloor = piece.Mount is DecoMount.Floor or DecoMount.Top || !upright;
        var footprints = model.Parts.Select(p => DecoEmit.Vertices(p)).Select(v => (min: v.Aggregate(Vector3.Min), max: v.Aggregate(Vector3.Max))).Where(b => b.min.Y < 0.12f).ToList();
        bool Free(float x, float z, float r) => !footprints.Any(f => x + r > f.min.X && x - r < f.max.X && z + r > f.min.Z && z - r < f.max.Z);
        float scale = Math.Clamp(MathF.Sqrt(piece.Width * piece.Depth) / 1.2f, 1f, 4f);

        // The piece's own faces looking up near the floor: no mark is laid at their height where it would lie over one.
        var low = LowFaces(mesh);
        if (shape.Shattered.Count > 0 && onFloor)
            Shards(mesh, rng, shape.Shattered, floorMin, floorMax, pal[DecoSlot.Glass], low);
        foreach (var (at, rx, rz) in shape.Stains)
        {
            if (Clashes(low, at.X - rx, at.Z - rz, at.X + rx, at.Z + rz, at.Y + StainLift)) continue;
            low.Add((at.X - rx, at.Z - rz, at.X + rx, at.Z + rz, at.Y + StainLift));
            var ring = new List<Vector3>();
            for (int i = 0; i < 10; i++)
            {
                float a = i * MathF.Tau / 10, k = rng.Range(0.7f, 1f);
                ring.Add(new Vector3(at.X + MathF.Cos(a) * rx * k, at.Y + StainLift, at.Z + MathF.Sin(a) * rz * k));
            }
            FloorPoly(mesh, DecoSurface.Matte, at with { Y = at.Y + StainLift }, ring, Stain(pal));
        }
        if (look.Age == DecoAge.OldWorld && look.State is not (DecoState.Burnt or DecoState.Overgrown or DecoState.Rusted) && look.Exposed && onFloor)
            Tufts(mesh, rng, pal, floorMin, floorMax, Free, false, (int)(2 * scale), grassOnly: true);
        switch (look.State)
        {
            case DecoState.Broken when onFloor:
                Debris(mesh, rng, floorMin, floorMax, Free, pal[DecoSlot.Body].Lerp(pal[DecoSlot.Debris], 0.4f), (int)((3 + rng.Int(3)) * scale), low, size: scale);
                break;
            case DecoState.Burnt:
                // Under a piece still standing: one lying on the floor lies where its scorch would be.
                if (onFloor && upright) Scorch(mesh, rng, floorMin, floorMax, pal[DecoSlot.Char], low);
                if (onFloor) Debris(mesh, rng, floorMin, floorMax, Free, pal[DecoSlot.Ash], (int)((2 + rng.Int(2)) * scale), low, flat: true, size: scale);
                break;
            case DecoState.Overgrown:
                Overgrow(mesh, rng, cover, pal, floorMin, floorMax, onFloor, Free, clampBox ? boxMin : (Vector3?)null, clampBox ? boxMax : (Vector3?)null, scale);
                break;
            case DecoState.Rusted when onFloor:
                // Grass through the cracks round a long-abandoned piece.
                Tufts(mesh, rng, pal, floorMin, floorMax, Free, false, (int)(3 * scale), grassOnly: true);
                break;
            case DecoState.Buried:
                Drift(mesh, rng, cover, pal, shape);
                break;
        }
        mesh.RecomputeBounds();

        if (clampBox) mesh.Clamp(boxMin, boxMax);
        DecoEmit.Collision(mesh, model.Parts);
        mesh.CentreOfMass = shape.CentreOfMass;
        if (powered)
            foreach (DecoLightSpot l in model.Lights) mesh.Lights.Add((l.At, pal[l.Slot]));
        if (look.State == DecoState.Broken && piece.Anim == "spark" && model.Spark is Vector3 spark) mesh.Spark = spark;
        return mesh;
    }

    /// <summary>Two or three pale cracks across a dead screen, from one point of impact.</summary>
    static void Cracks(DecoMeshData mesh, DecoPart screen, DecoPalette pal, DecoRng rng)
    {
        Vector3 ex = new(screen.M.M11, screen.M.M12, screen.M.M13), ey = new(screen.M.M21, screen.M.M22, screen.M.M23), ez = new(screen.M.M31, screen.M.M32, screen.M.M33);
        float sx = ex.Length(), sy = ey.Length();
        if (sx < 0.05f || sy < 0.05f) return;
        Vector3 nx = ex / sx, ny = ey / sy, n = -Vector3.Normalize(ez);
        Vector3 face = Vector3.Transform(new Vector3(0, 0, -0.5f), screen.M) + n * 0.003f;
        Vector3 hit = face + nx * sx * rng.Range(-0.25f, 0.25f) + ny * sy * rng.Range(-0.25f, 0.25f);
        int count = 3 + rng.Int(2);
        for (int i = 0; i < count; i++)
        {
            float a = rng.Range(0, MathF.Tau);
            Vector3 dir = nx * MathF.Cos(a) + ny * MathF.Sin(a);
            float len = MathF.Min(sx, sy) * rng.Range(0.3f, 0.6f);
            Vector3 side = Vector3.Normalize(Vector3.Cross(n, dir)) * 0.006f;
            Vector3 a0 = hit - side, a1 = hit + side, b0 = hit + dir * len;
            mesh.AddTriangle(DecoSurface.Matte, a0, b0, a1, pal[DecoSlot.Crack]);
            mesh.AddTriangle(DecoSurface.Matte, a0, a1, b0, pal[DecoSlot.Crack]);
        }
    }

    /// <summary>A flat polygon on the floor, facing up.</summary>
    static void FloorPoly(DecoMeshData mesh, DecoSurface surface, Vector3 centre, IList<Vector3> ring, DecoRgb col)
    {
        for (int i = 0; i < ring.Count; i++)
        {
            Vector3 a = centre, b = ring[i], c = ring[(i + 1) % ring.Count];
            if (Vector3.Cross(b - a, c - a).Y < 0) (b, c) = (c, b);
            mesh.AddTriangle(surface, a, b, c, col);
        }
    }

    /// <summary>
    /// The heights of the flat marks a piece leaves on the floor, a step apart so that where they overlap
    /// none shares a plane with another (they would flicker): scorch lowest, then stains, then glass.
    /// </summary>
    const float ScorchLift = 0.004f, StainLift = 0.009f, ShardLift = 0.014f;

    /// <summary>Glass shards on the floor below where panes were, none lying over another.</summary>
    static void Shards(DecoMeshData mesh, DecoRng rng, List<Vector3> at, Vector3 min, Vector3 max, DecoRgb glass, List<(float X0, float Z0, float X1, float Z1, float Y)> low)
    {
        var laid = new List<(float X, float Z, float R)>();
        foreach (Vector3 p in at.Take(12))
            for (int i = 0; i < 3; i++)
            {
                float x = Math.Clamp(p.X + rng.Range(-0.3f, 0.3f), min.X + 0.05f, max.X - 0.05f), z = Math.Clamp(p.Z + rng.Range(-0.3f, 0.3f), min.Z + 0.05f, max.Z - 0.05f);
                float s = rng.Range(0.03f, 0.08f), a = rng.Range(0, MathF.Tau);
                if (Overlaps(laid, x, z, s) || Clashes(low, x - s, z - s, x + s, z + s, ShardLift)) continue;
                laid.Add((x, z, s));
                low.Add((x - s, z - s, x + s, z + s, ShardLift));
                var c = new Vector3(x, ShardLift, z);
                var ring = new[] { c + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * s, c + new Vector3(MathF.Cos(a + 2.2f), 0, MathF.Sin(a + 2.2f)) * s * 0.7f, c + new Vector3(MathF.Cos(a + 4f), 0, MathF.Sin(a + 4f)) * s * 0.9f };
                FloorPoly(mesh, DecoSurface.Matte, c, ring, glass.Scale(1.1f));
            }
    }

    /// <summary>The mesh's faces looking straight up within a few centimetres of the floor, as boxes seen from above with their height.</summary>
    static List<(float X0, float Z0, float X1, float Z1, float Y)> LowFaces(DecoMeshData mesh)
    {
        var low = new List<(float, float, float, float, float)>();
        foreach (DecoSurfaceData s in mesh.Surfaces)
            for (int t = 0; t + 2 < s.Positions.Count; t += 3)
            {
                Vector3 a = s.Positions[t], b = s.Positions[t + 1], c = s.Positions[t + 2];
                if (s.Normals[t].Y < 0.999f || a.Y > 0.03f) continue;
                low.Add((MathF.Min(a.X, MathF.Min(b.X, c.X)), MathF.Min(a.Z, MathF.Min(b.Z, c.Z)), MathF.Max(a.X, MathF.Max(b.X, c.X)), MathF.Max(a.Z, MathF.Max(b.Z, c.Z)), (a.Y + b.Y + c.Y) / 3f));
            }
        return low;
    }

    /// <summary>Whether a flat mark over a box at height y would lie within a few millimetres of one of those faces, over it (they would flicker).</summary>
    static bool Clashes(List<(float X0, float Z0, float X1, float Z1, float Y)> low, float x0, float z0, float x1, float z1, float y) =>
        low.Any(f => MathF.Abs(f.Y - y) < 0.0025f && x1 > f.X0 && x0 < f.X1 && z1 > f.Z0 && z0 < f.Z1);

    /// <summary>Whether a round mark at (x, z) of radius r would lie over one already laid.</summary>
    static bool Overlaps(List<(float X, float Z, float R)> laid, float x, float z, float r) =>
        laid.Any(l => (l.X - x) * (l.X - x) + (l.Z - z) * (l.Z - z) < (l.R + r) * (l.R + r));

    /// <summary>
    /// Broken bits on the floor: small battered fragments in the piece's colour (or flat ash), each lying
    /// on the floor clear of the others, never one inside or over another.
    /// </summary>
    static void Debris(DecoMeshData mesh, DecoRng rng, Vector3 min, Vector3 max, Func<float, float, float, bool> free, DecoRgb col, int count, List<(float X0, float Z0, float X1, float Z1, float Y)> low, bool flat = false, float size = 1f)
    {
        var laid = new List<(float X, float Z, float R)>();
        for (int i = 0, tries = 0; i < count && tries < 60; tries++)
        {
            float s = (flat ? rng.Range(0.08f, 0.18f) : rng.Range(0.05f, 0.12f)) * MathF.Sqrt(size);
            float x = rng.Range(min.X + s, max.X - s), z = rng.Range(min.Z + s, max.Z - s);
            // A fragment's longest reach from its middle (its scale is at most s by s by s, turned).
            float reach = s * 0.75f;
            if (max.X - min.X < 2 * s || max.Z - min.Z < 2 * s || !free(x, z, s * 0.6f) || Overlaps(laid, x, z, reach)) continue;
            // Its top off every mark and face already laid near the floor (a chunk's top is near level: it tips 12 degrees at most).
            float sy = flat ? s * 0.18f : s * rng.Range(0.3f, 0.6f), top = flat ? sy : s * 0.3f + sy / 2f;
            if (Clashes(low, x - reach, z - reach, x + reach, z + reach, top)) continue;
            laid.Add((x, z, reach));
            low.Add((x - reach, z - reach, x + reach, z + reach, top));
            i++;
            var part = new DecoPart
            {
                Kind = DecoPrim.Hex, Corners = DecoPart.UnitCube, Slot = DecoSlot.Debris,
                M = Matrix4x4.CreateScale(s, sy, s * rng.Range(0.5f, 1f)) * DecoGeo.RotZ(flat ? 0 : rng.Range(-12, 12)) * DecoGeo.RotY(rng.Range(0, 180)),
            };
            float half = flat ? s * 0.09f : s * 0.3f;
            part.M *= DecoGeo.Move(x, half, z);
            DecoEmit.Part(mesh, part, (at, n, _) => col.Scale(0.85f + 0.3f * DecoHash.At(at, 99)), _ => DecoSurface.Matte, v => v.Y < 0 ? v with { Y = 0 } : v);
        }
    }

    /// <summary>A scorch mark under a burnt piece.</summary>
    static void Scorch(DecoMeshData mesh, DecoRng rng, Vector3 min, Vector3 max, DecoRgb col, List<(float X0, float Z0, float X1, float Z1, float Y)> low)
    {
        // At the first of a few heights that no face of the piece over it shares.
        float lift = new[] { ScorchLift, ScorchLift * 0.5f, ScorchLift * 1.6f }.FirstOrDefault(y => !Clashes(low, min.X, min.Z, max.X, max.Z, y), -1f);
        if (lift < 0) return;
        low.Add((min.X, min.Z, max.X, max.Z, lift));
        var c = new Vector3((min.X + max.X) / 2, lift, (min.Z + max.Z) / 2);
        float rx = (max.X - min.X) / 2, rz = (max.Z - min.Z) / 2;
        var ring = new List<Vector3>();
        for (int i = 0; i < 14; i++)
        {
            float a = i * MathF.Tau / 14, k = rng.Range(0.72f, 1f);
            ring.Add(new Vector3(c.X + MathF.Cos(a) * rx * k, lift, c.Z + MathF.Sin(a) * rz * k));
        }
        FloorPoly(mesh, DecoSurface.Matte, c, ring, col);
    }

    /// <summary>A half-buried piece's drift: a low mound over its sunken base and dunes banked against one side.</summary>
    static void Drift(DecoMeshData mesh, DecoRng rng, DecoCover cover, DecoPalette pal, Shape shape)
    {
        DecoSlot slot = cover == DecoCover.Snow ? DecoSlot.Snow : DecoSlot.Sand;
        Vector3 min = shape.Min, max = shape.Max;
        var c = new Vector3((min.X + max.X) / 2, 0, (min.Z + max.Z) / 2);
        float rx = (max.X - min.X) / 2, rz = (max.Z - min.Z) / 2;
        float h = shape.Sink + 0.12f;
        var mounds = new List<DecoPart> { new() { Kind = DecoPrim.Sphere, Segments = 12, Slot = slot, M = Matrix4x4.CreateScale(rx * 2, h * 2, rz * 2) * DecoGeo.Move(c) } };
        // Dunes banked on the windward side, higher up the piece.
        bool alongX = rx > rz;
        float side = rng.Chance(0.5f) ? 1 : -1;
        int n = 3 + rng.Int(3);
        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n * 2 - 1;
            float along = t * (alongX ? rx : rz) * 0.75f, across = side * (alongX ? rz : rx) * rng.Range(0.35f, 0.6f);
            var at = alongX ? new Vector3(c.X + along, 0, c.Z + across) : new Vector3(c.X + across, 0, c.Z + along);
            float r = MathF.Min(rx, rz) * rng.Range(0.45f, 0.75f);
            mounds.Add(new DecoPart { Kind = DecoPrim.Sphere, Segments = 10, Slot = slot, M = Matrix4x4.CreateScale(r * 2.6f, (h + rng.Range(0.1f, 0.4f) * shape.Sink) * 2, r * 2) * DecoGeo.RotY(alongX ? 0 : 90) * DecoGeo.Move(at) });
        }
        foreach (DecoPart m in mounds)
            DecoEmit.Part(mesh, m, (p, nrm, s) => pal[s].Scale(0.9f + 0.12f * DecoHash.At(p, 41) + (nrm.Y > 0.7f ? 0.05f : -0.04f)), _ => DecoSurface.Matte,
                v => v.Y < 0 ? v with { Y = 0 } : Vector3.Clamp(v, min, max with { Y = 99 }));
    }

    /// <summary>Moss tufts and grass (or the Bloom's low fleshy mounds) round a piece's foot.</summary>
    static void Tufts(DecoMeshData mesh, DecoRng rng, DecoPalette pal, Vector3 floorMin, Vector3 floorMax, Func<float, float, float, bool> free, bool bloom, int tufts, bool grassOnly = false)
    {
        for (int i = 0, tries = 0; i < tufts && tries < 60; tries++)
        {
            float s = rng.Range(0.06f, 0.14f);
            if (floorMax.X - floorMin.X < 2 * s || floorMax.Z - floorMin.Z < 2 * s) break;
            float x = rng.Range(floorMin.X + s, floorMax.X - s), z = rng.Range(floorMin.Z + s, floorMax.Z - s);
            if (!free(x, z, s * 0.3f) && rng.Chance(0.7f)) continue;
            i++;
            if (!grassOnly)
            {
                var tuft = new DecoPart { Kind = DecoPrim.Sphere, Segments = 6, Slot = bloom ? DecoSlot.Bloom : DecoSlot.Moss, M = Matrix4x4.CreateScale(s * 2, s * 0.7f, s * 1.6f) * DecoGeo.RotY(rng.Range(0, 180)) * DecoGeo.Move(x, 0, z) };
                DecoEmit.Part(mesh, tuft, (p, _, slot) => pal[slot].Lerp(bloom ? pal[DecoSlot.BloomVein] : pal[DecoSlot.Leaf], DecoHash.At(p, 17) * 0.5f), slot => DecoPalette.SurfaceOf(slot) == DecoSurface.Flesh ? DecoSurface.Flesh : DecoSurface.Matte, v => v.Y < 0 ? v with { Y = 0 } : v);
            }
            if (bloom) continue;
            int blades = grassOnly ? 5 : 3;
            for (int g = 0; g < blades; g++)
            {
                float gx = x + rng.Range(-s, s), gz = z + rng.Range(-s, s), hgt = rng.Range(0.1f, grassOnly ? 0.32f : 0.2f), lean = rng.Range(-0.05f, 0.05f);
                var bot0 = new Vector3(gx - 0.014f, 0, gz); var bot1 = new Vector3(gx + 0.014f, 0, gz); var top = new Vector3(gx + lean, hgt, gz + lean);
                mesh.AddTriangle(DecoSurface.Leaf, bot0, bot1, top, pal[DecoSlot.Leaf].Lerp(pal[DecoSlot.Canvas], grassOnly ? 0.35f : 0).Scale(0.85f + 0.25f * DecoHash.At(top, 21)));
            }
        }
    }

    /// <summary>
    /// Vines feel their way up the piece (rays from outside find its surface) and over its top,
    /// with leaves; moss tufts and grass at its foot. In an infested place the growth is the
    /// Bloom's: thicker fleshy tendrils with nodules.
    /// </summary>
    static void Overgrow(DecoMeshData mesh, DecoRng rng, DecoCover cover, DecoPalette pal, Vector3 floorMin, Vector3 floorMax, bool onFloor,
        Func<float, float, float, bool> free, Vector3? clampMin, Vector3? clampMax, float scale)
    {
        var tris = mesh.AllTriangles().ToList();
        Vector3 min = mesh.Min, max = mesh.Max;
        Vector3 centre = (min + max) / 2;
        float reach = (max - min).Length() + 1f;
        bool bloom = cover == DecoCover.Bloom;
        DecoSlot stem = bloom ? DecoSlot.Bloom : DecoSlot.Vine;
        float r = (bloom ? 0.022f : 0.012f) * MathF.Sqrt(scale);
        Vector3 Clamp(Vector3 v) => clampMin is Vector3 lo && clampMax is Vector3 hi ? Vector3.Clamp(v, lo, hi) : v;

        (Vector3 at, Vector3 n)? Cast(Vector3 o, Vector3 d)
        {
            float best = float.MaxValue;
            Vector3 n = Vector3.Zero;
            foreach (var (a, b, c) in tris)
            {
                float t = DecoGeo.Ray(o, d, a, b, c);
                if (t > 0 && t < best) { best = t; n = Vector3.Normalize(Vector3.Cross(b - a, c - a)); }
            }
            if (best == float.MaxValue) return null;
            if (Vector3.Dot(n, d) > 0) n = -n;
            return (o + d * best, n);
        }

        var stems = new List<DecoPart>();
        var leaves = new List<(Vector3 at, Vector3 n, float size)>();
        int vines = (int)((3 + rng.Int(3)) * MathF.Min(scale, 2.5f));
        float height = max.Y - min.Y;
        float step = 0.11f * MathF.Max(1f, height / 1.5f);
        for (int v = 0; v < vines; v++)
        {
            float ang = (v + rng.Range(0.1f, 0.9f)) * MathF.Tau / vines;
            var path = new List<Vector3>();
            float limit = min.Y + height * rng.Range(0.6f, 1f);
            for (float y = min.Y + 0.04f; y <= limit; y += step)
            {
                float a = ang + MathF.Sin(y * 7f + v) * 0.25f;
                var dir = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
                var hit = Cast(new Vector3(centre.X, y, centre.Z) + dir * reach, -dir);
                if (hit == null) { if (path.Count > 2) break; continue; }
                path.Add(Clamp(hit.Value.at + hit.Value.n * (r + 0.006f)));
                if (rng.Chance(0.6f)) leaves.Add((hit.Value.at + hit.Value.n * 0.02f, hit.Value.n, rng.Range(0.05f, 0.09f) * MathF.Sqrt(scale)));
            }
            // Over the top: down-rays from above, walking inward.
            if (path.Count > 0 && path[^1].Y > max.Y - 0.25f * MathF.Max(1, height / 1.5f))
            {
                Vector3 last = path[^1];
                var inward = new Vector3(centre.X - last.X, 0, centre.Z - last.Z);
                if (inward.LengthSquared() > 1e-4f)
                {
                    inward = Vector3.Normalize(inward);
                    float span = new Vector2(max.X - min.X, max.Z - min.Z).Length() * rng.Range(0.2f, 0.45f);
                    for (float s = 0.1f; s < span; s += step)
                    {
                        Vector3 p = last + inward * s + new Vector3(-inward.Z, 0, inward.X) * MathF.Sin(s * 9f) * 0.05f;
                        var hit = Cast(new Vector3(p.X, max.Y + 1f, p.Z), -Vector3.UnitY);
                        if (hit == null) break;
                        path.Add(Clamp(hit.Value.at + hit.Value.n * (r + 0.006f)));
                        if (rng.Chance(0.7f)) leaves.Add((hit.Value.at + hit.Value.n * 0.02f, hit.Value.n, rng.Range(0.05f, 0.1f) * MathF.Sqrt(scale)));
                    }
                }
            }
            for (int i = 0; i + 1 < path.Count; i++)
                if (Vector3.Distance(path[i], path[i + 1]) < 0.5f * MathF.Max(1, height / 1.5f))
                    stems.Add(new DecoPart { Kind = DecoPrim.Cylinder, Segments = 4, Slot = stem, M = DecoGeo.Along(path[i], path[i + 1], r * 2, r * 2) });
        }
        foreach (DecoPart s in stems)
            DecoEmit.Part(mesh, s, (at, n, slot) => pal[slot].Scale(0.85f + 0.3f * DecoHash.At(at, 5)), slot => DecoPalette.SurfaceOf(slot), v => Clamp(v));
        var laid = new List<(Vector3 At, float Size)>();
        foreach (var (at, n, size) in leaves)
        {
            // No leaf lies over another: on a flat top they would share a plane and flicker.
            if (laid.Any(l => Vector3.Distance(l.At, Clamp(at)) < (l.Size + size) * 1.05f)) continue;
            laid.Add((Clamp(at), size));
            if (bloom)
            {
                var nod = new DecoPart { Kind = DecoPrim.Sphere, Segments = 6, Slot = DecoSlot.Bloom, M = Matrix4x4.CreateScale(size * 0.9f) * DecoGeo.Move(Clamp(at)) };
                DecoEmit.Part(mesh, nod, (p, _, slot) => pal[slot].Lerp(pal[DecoSlot.BloomVein], DecoHash.At(p, 3) * 0.5f), _ => DecoSurface.Flesh, v => Clamp(v));
                continue;
            }
            Vector3 t1 = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
            float tw = DecoHash.At(at, 11) * MathF.Tau;
            Vector3 t2 = Vector3.Cross(n, t1);
            Vector3 dir = t1 * MathF.Cos(tw) + t2 * MathF.Sin(tw), side = Vector3.Cross(n, dir);
            Vector3 a = Clamp(at), b = Clamp(at + dir * size + n * 0.01f), c = Clamp(at + dir * size * 0.5f + side * size * 0.35f), d = Clamp(at + dir * size * 0.5f - side * size * 0.35f);
            DecoRgb col = pal[DecoSlot.Leaf].Lerp(pal[DecoSlot.Moss], DecoHash.At(at, 13) * 0.6f);
            mesh.AddTriangle(DecoSurface.Leaf, a, c, b, col);
            mesh.AddTriangle(DecoSurface.Leaf, a, b, d, col);
        }
        if (onFloor) Tufts(mesh, rng, pal, floorMin, floorMax, free, bloom, (int)((4 + rng.Int(4)) * scale));
    }
}
