using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Terrain;
using Planets.Gen;
using PlanetSurface.World;
using PoiGenerator.Gen;
using NV = System.Numerics.Vector3;
using Hash = Planets.Gen.Hash;
using SInt3 = GalHack.Structures.Int3;

namespace PlanetSurface.Infra;

/// <summary>
/// The network's bridges, tunnel mouths, railways and maglev guideways as block structures (<see cref="InfraLayer.BlockPiece"/>),
/// each laid from its old template in 1 m blocks of the shapes it needs: a bridge as its spans (a deck between two supports,
/// parapet plates along it, girders under a steel or composite one), its piers (round columns on plinths under a cap beam, or
/// a timber trestle) and its abutments; a tunnel mouth's face, covered box and wing walls; a railway's track a stretch of
/// ballast, sleepers and rails at a time, its stations' platforms, shelters and buffers; a guideway's beam on its pylons.
/// </summary>
public partial class InfraLayer
{
    static NV Point(InfraRoute r, int k) => new((float)r.X[k], r.Y[k], (float)r.Z[k]);

    static NV Level(NV v)
    {
        v.Y = 0f;
        return v.LengthSquared() > 1e-8f ? NV.Normalize(v) : NV.UnitX;
    }

    /// <summary>How weathered a route's structures look (paint grime, 0 to 3).</summary>
    static int Grime(InfraRoute r) => r.Kept ? 1 : r.Wear > 0.5f ? 3 : 2;

    /// <summary>Spans of a run each a whole number of metres, each starting where the last ended (they meet, never overlap), toward each cut point.</summary>
    static List<(NV Start, NV Way, int Cells)> Chain(IReadOnlyList<NV> cuts)
    {
        var spans = new List<(NV, NV, int)>();
        NV at = cuts[0];
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            NV d = cuts[i + 1] - at;
            float len = d.Length();
            NV way = len > 1e-3f ? d / len : Level(cuts[i + 1] - cuts[i]);
            int n = Math.Max(1, (int)MathF.Round(len));
            spans.Add((at, way, n));
            at += way * n;
        }
        return spans;
    }

    static void Rest(BlockPiece span, params BlockPiece[] supports)
    {
        span.Supports = supports;
        foreach (BlockPiece s in supports) s?.Carries.Add(span);
    }

    // ---- bridges ----------------------------------------------------------------------------------

    /// <summary>A bridge: an abutment at each end, a pier every fifth point (20 m) and the spans between them.</summary>
    static IEnumerable<Piece> BridgePieces(InfraStructure s)
    {
        InfraRoute r = s.Route;
        var cuts = new List<int> { s.First };
        for (int k = s.First + 5; k < s.Last; k += 5) cuts.Add(k);
        cuts.Add(s.Last);
        var chain = Chain(cuts.Select(k => Point(r, k)).ToList());
        var supports = new BlockPiece[cuts.Count];
        NV end = chain[^1].Start + chain[^1].Way * chain[^1].Cells;
        supports[0] = new Abutment { Structure = s, AtStart = true, Edge = Point(r, s.First), X = r.X[s.First], Z = r.Z[s.First] };
        supports[^1] = new Abutment { Structure = s, AtStart = false, Edge = end, X = end.X, Z = end.Z };
        for (int i = 1; i + 1 < cuts.Count; i++) supports[i] = new BridgePier { Structure = s, K = cuts[i], X = r.X[cuts[i]], Z = r.Z[cuts[i]] };
        foreach (BlockPiece p in supports) yield return p;
        for (int i = 0; i < chain.Count; i++)
        {
            var (start, way, n) = chain[i];
            var span = new BridgeSpan { Structure = s, From = cuts[i], Start = start, Way = way, Cells = n, Fallen = s.Fallen.Contains(cuts[i]), X = start.X, Z = start.Z };
            if (!span.Fallen) Rest(span, supports[i], supports[i + 1]);
            yield return span;
        }
    }

    /// <summary>What a bridge is built of: its deck, what's dark on it (girders, a trestle), its parapets.</summary>
    static (Laying.Stuff Deck, Laying.Stuff Dark, Laying.Stuff Rail) BridgeStuff(Laying l, InfraStructure s)
    {
        int g = Grime(s.Route);
        return s.Build switch
        {
            "timber" => (l.Of("base-block", Timber, g), l.Of("base-block", TimberDark, g), l.Of("base-block", TimberDark, g)),
            "steel" => (l.Of("concrete-plated", Concrete, g), l.Of("iron-plated", Steel, g), l.Of("iron-plated", Steel, g)),
            _ => (l.Of("concrete-plated", Composite, g), l.Of("concrete-plated", ConcreteDark, g), l.Of("concrete-plated", ConcreteDark, g)),
        };
    }

    /// <summary>How deep a bridge's deck is under its road: its slab, and the girders under a steel or composite one.</summary>
    static int DeckDepth(InfraStructure s) => s.Build == "timber" ? 1 : 2;

    /// <summary>
    /// A bridge's span between two supports: its deck slab, girders under it, a thin parapet plate standing along each edge (some
    /// gone on a neglected bridge), a railway's rails on it; or, fallen, its deck lying broken on the ground below.
    /// </summary>
    sealed class BridgeSpan : BlockPiece
    {
        public InfraStructure Structure;
        public int From, Cells;
        public NV Start, Way;
        public bool Fallen;
        protected override bool Carried => !Fallen;
        protected override bool Lying => Fallen;

        protected override void Lay(Laying l)
        {
            InfraRoute r = Structure.Route;
            int w = Math.Max(1, (int)MathF.Round(r.HalfWidth + 0.3f - 0.5f));
            var (deck, dark, rail) = BridgeStuff(l, Structure);
            ulong h0 = Hash.Of((ulong)r.Id * 7919UL + (ulong)From, 0xB41D);
            if (Fallen)
            {
                // In the valley, a slab stepped over the ground it came down on (each column's foot at or under the ground there,
                // up to its neighbours' so no step shows a gap through it), its edges broken here and there.
                NV flat = Level(Way);
                Turn = Frame(flat);
                Origin = new NV(Start.X, l.Ground(Start.X, Start.Z), Start.Z) + flat * 0.5f;
                var rows = new int[Cells + 2, 2 * w + 3];
                for (int i = -1; i <= Cells; i++)
                    for (int x = -w - 1; x <= w + 1; x++) rows[i + 1, x + w + 1] = Row(l, x, -i, 0f);
                for (int i = 0; i < Cells; i++)
                    for (int x = -w; x <= w; x++)
                    {
                        if (Math.Abs(x) == w && Hash.Of(h0, i * 64 + x + 32) % 3 == 0) continue;
                        int row = rows[i + 1, x + w + 1], top = row;
                        foreach (var (di, dx) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                            if (i + di >= 0 && i + di < Cells && Math.Abs(x + dx) <= w) top = Math.Max(top, rows[i + 1 + di, x + dx + w + 1] - 1);
                        l.Fill(x, x, row, top, -i, -i, deck);
                    }
                return;
            }
            Turn = Frame(Way);
            NV up = NV.Transform(NV.UnitY, Turn);
            Origin = Start + Way * 0.5f - up * 0.5f;
            int edge0 = Laying.Rot(new SInt3(1, 0, 0), new SInt3(0, 0, -1)), edge1 = Laying.Rot(new SInt3(-1, 0, 0), new SInt3(0, 0, -1));
            var steel = r.Kind == RouteKind.Rail ? l.Of("iron-plated", Rail, Grime(r)) : default;
            for (int i = 0; i < Cells; i++)
            {
                for (int x = -w; x <= w; x++) l.Put(x, 0, -i, deck);
                if (DeckDepth(Structure) > 1)
                    foreach (int x in new[] { -w + 1, w - 1 }) l.Put(x, -1, -i, dark);
                for (int s = -1; s <= 1; s += 2)
                    if (r.Kept || Hash.Of(h0, i * 2 + (s > 0 ? 1 : 0)) % 8 != 0) l.Put(s * w, 1, -i, rail, "plate", s > 0 ? edge1 : edge0);
                if (r.Kind == RouteKind.Rail)
                    foreach (int x in new[] { -1, 1 }) l.Put(x, 1, -i, steel, "plate");
            }
        }
    }

    /// <summary>
    /// A bridge's pier at a point: a cap beam under the deck on two round columns, each on a plinth sunk in the ground; on a
    /// timber bridge a trestle of two posts tied across. None where the deck is near the ground.
    /// </summary>
    sealed class BridgePier : BlockPiece
    {
        public InfraStructure Structure;
        public int K;

        protected override void Lay(Laying l)
        {
            InfraRoute r = Structure.Route;
            float capTop = r.Y[K] - DeckDepth(Structure), ground = l.Ground(r.X[K], r.Z[K]);
            if (capTop - ground <= 0.5f) return;
            Turn = Frame(Level(Point(r, Math.Min(r.Count - 1, K + 1)) - Point(r, Math.Max(0, K - 1))));
            Origin = new NV((float)r.X[K], capTop - 0.5f, (float)r.Z[K]);
            float half = r.HalfWidth + 0.3f;
            var (_, dark, _) = BridgeStuff(l, Structure);
            int cap;
            if (Structure.Build == "timber")
            {
                int c = Math.Max(1, (int)MathF.Round(half * 0.7f));
                foreach (int s in new[] { -c, c }) l.Fill(s, s, Row(l, s, 0, 1.5f), -1, 0, 0, dark);
                for (int y = -4; y > Math.Max(Row(l, -c, 0, 0f), Row(l, c, 0, 0f)) + 1; y -= 4) l.Fill(-c + 1, c - 1, y, y, 0, 0, dark);
                cap = c + 1;
                l.Fill(-cap, cap, 0, 0, 0, 0, dark);
            }
            else
            {
                int c = Math.Max(1, (int)MathF.Round(half * 0.5f)), g = Grime(r);
                Laying.Stuff col = l.Of("concrete-plated", Structure.Build == "steel" ? Concrete : Composite, g), plinth = l.Of("concrete-plated", ConcreteDark, g);
                foreach (int s in new[] { -c, c })
                {
                    // A plinth sunk in the ground (down to it under each of its cells), a round column from it up to the cap.
                    int foot = Row(l, s, 0, 1.5f);
                    for (int z = -1; z <= 1; z++)
                        for (int x = s - 1; x <= s + 1; x++) l.Fill(x, x, Math.Min(foot, Row(l, x, z, 1f)), Math.Min(foot + 1, -1), z, z, plinth);
                    for (int y = foot + 2; y <= -1; y++) l.Put(s, y, 0, col, "cylinder");
                }
                cap = Math.Max(c + 1, (int)MathF.Round(half * 0.95f - 0.5f));
                l.Fill(-cap, cap, 0, 0, 0, 0, col);
            }
            Cap = new HashSet<SInt3>(Enumerable.Range(-cap, 2 * cap + 1).Select(x => new SInt3(x, 0, 0)));
        }
    }

    /// <summary>A bridge's end: from its deck's edge back into the bank and down into the ground, with a seat under the span's end.</summary>
    sealed class Abutment : BlockPiece
    {
        public InfraStructure Structure;
        public bool AtStart;
        /// <summary>Where the deck ends on it (its road's top at the span's end).</summary>
        public NV Edge;

        protected override void Lay(Laying l)
        {
            InfraRoute r = Structure.Route;
            int k = AtStart ? Structure.First : Structure.Last, n = AtStart ? Math.Min(r.Count - 1, k + 1) : Math.Max(0, k - 1);
            NV into = Level(Point(r, n) - Point(r, k));
            Turn = Frame(into);
            // Its top a little under the road (the bank's road lies on it), its first cell wholly on the bank side.
            Origin = new NV(Edge.X, Edge.Y - 0.55f, Edge.Z) - into * 0.5f;
            int w = (int)MathF.Round(r.HalfWidth + 0.3f), seat = -DeckDepth(Structure);
            Laying.Stuff stone = l.Of("concrete-plated", Structure.Build == "timber" ? Stone : ConcreteDark, Grime(r));
            // Each column down into the ground under it: 2.5 m into the bank, wherever it falls away.
            for (int z = -1; z <= 2; z++)
                for (int x = -w; x <= w; x++) l.Fill(x, x, Math.Min(Row(l, x, z, 2.5f), seat - 1), z < 0 ? seat : 0, z, z, stone);
            Cap = new HashSet<SInt3>(Enumerable.Range(-w, 2 * w + 1).Select(x => new SInt3(x, seat, -1)));
        }
    }

    // ---- tunnels ------------------------------------------------------------------------------------

    /// <summary>
    /// A tunnel mouth: a face wall round the bore (piers either side, a lintel and a coping) set forward of the mouth in the cutting,
    /// a covered box back to the bore over the raw cut, and wing walls down the cutting's edges as high as the bank they hold.
    /// </summary>
    sealed class Portal : BlockPiece
    {
        public InfraStructure Structure;
        public bool AtStart;

        protected override void Lay(Laying l)
        {
            InfraRoute r = Structure.Route;
            int k = AtStart ? Structure.First : Structure.Last, outward = AtStart ? -1 : 1;
            int boxPoints = (int)Math.Ceiling(TerrainStamp.MouthBox / InfraNetwork.Spacing);
            int w = Math.Clamp(k + outward * boxPoints, 0, r.Count - 1);
            NV mouth = Point(r, k), wall = Point(r, w), flat = new(mouth.X - wall.X, 0, mouth.Z - wall.Z);
            if (flat.LengthSquared() < 1e-6f) return;
            float boxLength = flat.Length() + 3f;
            NV into = NV.Normalize(flat);
            float bore = r.HalfWidth + 1.2f, height = r.Kind == RouteKind.Rail ? 7f : 6.5f, capped = height + 2.4f;
            // Dressed stone, or timber framing on a low-tech track; cast concrete; composite panels.
            int g = Grime(r);
            Laying.Stuff col = r.Tech == TechLevel.Low ? (r.Kind == RouteKind.Track ? l.Of("base-block", PoleWood, g) : l.Of("concrete-plated", Stone, g))
                : l.Of("concrete-plated", r.Tech == TechLevel.Mid ? Concrete : Composite, g);
            Turn = Frame(into);
            // Cell rows from the road up: row 0 is the metre under the road.
            Origin = new NV(wall.X, wall.Y - 0.5f, wall.Z);
            int bc = (int)MathF.Ceiling(bore - 0.5f), top = (int)MathF.Ceiling(height);
            // The face: piers either side of the opening, the lintel over it, a coping a little proud.
            foreach (int s in new[] { -1, 1 })
                for (int x = bc + 1; x <= bc + 2; x++) l.Fill(s * x, s * x, Math.Min(-1, Row(l, s * x, 0, 1f)), top + 2, 0, 0, col);
            l.Fill(-bc, bc, top + 1, top + 2, 0, 0, col);
            l.Fill(-(bc + 3), bc + 3, top + 3, top + 3, 0, 1, col);
            // The covered box from the face back into the hill: walls and roof over the raw cut.
            int box = Math.Max(1, (int)MathF.Round(boxLength) - 1);
            for (int z = -1; z >= -box; z--)
            {
                foreach (int s in new[] { -1, 1 }) Fill(s * (bc + 1), s * (bc + 1), Math.Min(-1, Row(l, s * (bc + 1), z, 1f)), top, z, z);
                l.Fill(-(bc + 1), bc + 1, top + 1, top + 1, z, z, col);
            }
            // Wing walls down each side of the cutting, on its edge as it narrows, as high as the bank they hold.
            var back = System.Numerics.Quaternion.Inverse(Turn);
            for (int j = 0; j < 4; j++)
            {
                int p = w + outward * j, q = p + outward;
                if (p < 0 || q < 0 || p >= r.Count || q >= r.Count || r.Span[Math.Min(p, q)] != RoadSpan.Ground) break;
                NV pa = Point(r, p), pb = Point(r, q), along = new(pb.X - pa.X, 0, pb.Z - pa.Z);
                if (along.Length() < 1e-3f) break;
                NV dir = NV.Normalize(along), across = new(-dir.Z, 0, dir.X);
                float far = Math.Clamp((j + 0.5f) * InfraNetwork.Spacing / TerrainStamp.MouthFade, 0f, 1f);
                float edge = r.HalfWidth + (bore + TerrainStamp.MouthRoom - r.HalfWidth) * (1f - far) + 0.35f;
                foreach (int s in new[] { -1, 1 })
                {
                    NV mid = (pa + pb) * 0.5f + across * (s * (edge + 2.5f));
                    float tall = Math.Clamp(l.Ground(mid.X, mid.Z) - Math.Min(pa.Y, pb.Y) + 0.4f, 0.5f, capped);
                    int yTop = Math.Max(1, (int)MathF.Round(Math.Min(pa.Y, pb.Y) + tall - Origin.Y - 0.5f));
                    for (float t = 0f; t <= 1.01f; t += 0.25f)
                    {
                        // The wall's inner face stays off the road: its cell starts at or past the edge.
                        NV local = NV.Transform(NV.Lerp(pa, pb, t) + across * (s * (edge - 0.35f)) - Origin, back);
                        int x = Math.Sign(local.X) * (int)MathF.Ceiling(MathF.Abs(local.X) + 0.5f), z = (int)MathF.Round(local.Z);
                        if (z <= 0) continue; // the face and the box are there
                        l.Fill(x, x, Math.Min(-1, Row(l, x, z, 1f)), yTop, z, z, col);
                    }
                }
            }

            void Fill(int x0, int x1, int y0, int y1, int z0, int z1) => l.Fill(x0, x1, y0, y1, z0, z1, col);
        }
    }

    // ---- railways ----------------------------------------------------------------------------------

    /// <summary>A railway's track off its bridges, a run of up to five stretches at a time (none where a train stands on it, which brings its own).</summary>
    static IEnumerable<Piece> TrackPieces(InfraRoute r, HashSet<(int, int)> underTrains)
    {
        var run = new List<int>();
        IEnumerable<Piece> Flush()
        {
            if (run.Count == 0) yield break;
            var (start, way, n) = Chain(new[] { Point(r, run[0]), Point(r, run[^1] + 1) })[0];
            yield return new Track { Route = r, K = run[0], Start = start, Way = way, Cells = n, X = start.X, Z = start.Z };
            run.Clear();
        }
        for (int k = 0; k + 1 < r.Count; k++)
        {
            bool off = underTrains.Contains((r.Id, k)) || r.Span[k] == RoadSpan.Bridge;
            if (off || run.Count == 5 || run.Count > 0 && run[^1] != k - 1)
                foreach (Piece p in Flush()) yield return p;
            if (!off) run.Add(k);
        }
        foreach (Piece p in Flush()) yield return p;
    }

    /// <summary>A run of railway track: thin plates of ballast, sleepers across it every other metre and the two rails (a few sleepers gone on a long-neglected line).</summary>
    sealed class Track : BlockPiece
    {
        public InfraRoute Route;
        public int K, Cells;
        public NV Start, Way;
        protected override bool Lying => true;

        protected override void Lay(Laying l)
        {
            InfraRoute r = Route;
            Turn = Frame(Way);
            Origin = Start + Way * 0.5f + NV.Transform(NV.UnitY, Turn) * 0.5f;
            int g = Grime(r);
            Laying.Stuff ballast = l.Of("concrete-plated", Ballast, g), rail = l.Of("iron-plated", Rail, g),
                sleeper = r.Tech != TechLevel.Low ? l.Of("concrete-plated", SleeperConcrete, g) : l.Of("base-block", Sleeper, g);
            for (int i = 0; i < Cells; i++)
                for (int x = -2; x <= 2; x++)
                {
                    bool isRail = x is -1 or 1;
                    if (!isRail && r.Wear > 0.6f && Hash.Of((ulong)r.Id * 104729UL + (ulong)((K + i) * 8 + x + 2), 0x51EE) % 9 == 0) continue;
                    l.Put(x, 0, -i, isRail ? rail : i % 2 == 0 ? sleeper : ballast, "plate");
                }
        }
    }

    /// <summary>The end of a line: buffers past the end of the track, a platform beside it with a shelter's posts and roof. All of it stands in the ground.</summary>
    sealed class Station : BlockPiece
    {
        public InfraRoute Route;
        public bool AtStart;

        protected override void Lay(Laying l)
        {
            InfraRoute r = Route;
            int k = AtStart ? 0 : r.Count - 1, far = Math.Clamp(k + (AtStart ? 12 : -12), 0, r.Count - 1);
            NV a = Point(r, k), c = Point(r, far), along = new(c.X - a.X, 0, c.Z - a.Z);
            float length = along.Length();
            if (length < 1f) return;
            NV dir = along / length;
            int g = Grime(r);
            Laying.Stuff body = l.Of(r.Tech == TechLevel.Low ? "base-block" : "concrete-plated", r.Tech == TechLevel.Low ? Timber : r.Tech == TechLevel.Mid ? Concrete : Composite, g),
                dark = l.Of(r.Tech == TechLevel.Low ? "base-block" : "concrete-plated", r.Tech == TechLevel.Low ? TimberDark : ConcreteDark, g);
            float deck = (r.Y[k] + r.Y[far]) * 0.5f;
            Turn = Frame(dir);
            // Row 0 is the platform's top metre, its top 0.9 m over the rails.
            Origin = new NV(a.X, deck + 0.4f, a.Z) + dir * 0.5f;
            int px = (int)MathF.Round(r.HalfWidth + 2.2f), n = Math.Max(1, (int)MathF.Round(length));
            // Buffers across the end of the track, in the ground past it; the platform down into the ground under each of its columns.
            for (int x = -1; x <= 1; x++) l.Fill(x, x, Math.Min(-1, Row(l, x, 1, 1f)), 1, 1, 1, dark);
            for (int z = 0; z > -n; z--)
                for (int x = px - 2; x <= px + 2; x++) l.Fill(x, x, Math.Min(-1, Row(l, x, z, 1f)), 0, z, z, body);
            // The shelter: posts on the platform, and a thin roof on them.
            int m = n / 2;
            foreach (int along2 in new[] { -9, -3, 3, 9 })
            {
                int z = -m + along2;
                if (z > 0 || z < -(n - 1)) continue;
                foreach (int x in new[] { px - 1, px + 1 })
                    for (int y = 1; y <= 3; y++) l.Put(x, y, z, dark, "cylinder");
            }
            for (int z = Math.Min(0, -m + 10); z >= Math.Max(-(n - 1), -m - 10); z--)
                for (int x = px - 2; x <= px + 2; x++) l.Put(x, 4, z, body, "plate");
        }
    }

    /// <summary>A maglev's guideway: a beam on pylons every sixth point where it's raised, low over the ground where it isn't; each run between pylons chained to the last.</summary>
    static IEnumerable<Piece> GuidewayPieces(InfraRoute r)
    {
        var cuts = new List<int>();
        for (int k = 0; k < r.Count - 1; k += 6) cuts.Add(k);
        cuts.Add(r.Count - 1);
        if (cuts.Count < 2) yield break;
        float Lift(int k) => r.Span[Math.Min(k, r.Count - 2)] == RoadSpan.Bridge ? 7f : 0.8f;
        var chain = Chain(cuts.Select(k => Point(r, k) + NV.UnitY * Lift(k)).ToList());
        var pylons = cuts.Select(k => k < r.Count - 1 && Lift(k) > 1f ? new GuidewayPylon { Route = r, K = k, X = r.X[k], Z = r.Z[k] } : null).ToArray();
        foreach (GuidewayPylon p in pylons)
            if (p != null) yield return p;
        for (int i = 0; i < chain.Count; i++)
        {
            var (start, way, n) = chain[i];
            var beam = new Guideway { Route = r, Start = start, Way = way, Cells = n, X = start.X, Z = start.Z };
            if (pylons[i] != null || pylons[i + 1] != null) Rest(beam, pylons[i], pylons[i + 1]);
            yield return beam;
        }
    }

    /// <summary>A run of a maglev's guideway beam.</summary>
    sealed class Guideway : BlockPiece
    {
        public InfraRoute Route;
        public int Cells;
        public NV Start, Way;
        protected override bool Carried => Supports.Any(s => s != null);
        protected override bool Lying => !Carried;

        protected override void Lay(Laying l)
        {
            Turn = Frame(Way);
            Origin = Start + Way * 0.5f + NV.Transform(NV.UnitY, Turn) * 0.3f;
            Laying.Stuff beam = l.Of("concrete-plated", Composite, Grime(Route));
            l.Fill(-1, 1, -1, 0, 0, -(Cells - 1), beam);
        }
    }

    /// <summary>A guideway's pylon: a square column from the ground up under the beam.</summary>
    sealed class GuidewayPylon : BlockPiece
    {
        public InfraRoute Route;
        public int K;

        protected override void Lay(Laying l)
        {
            InfraRoute r = Route;
            float top = r.Y[K] + 7f - 1.2f, ground = l.Ground(r.X[K], r.Z[K]);
            if (top - ground < 0.5f) return;
            Turn = Frame(Level(Point(r, Math.Min(r.Count - 1, K + 1)) - Point(r, Math.Max(0, K - 1))));
            Origin = new NV((float)r.X[K], top - 0.5f, (float)r.Z[K]) + NV.Transform(new NV(-0.5f, 0f, -0.5f), Turn);
            Laying.Stuff pylon = l.Of("concrete-plated", Composite, Grime(r));
            for (int z = 0; z <= 1; z++)
                for (int x = 0; x <= 1; x++) l.Fill(x, x, Row(l, x, z, 1f), 0, z, z, pylon);
            Cap = new HashSet<SInt3> { new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(1, 0, 1) };
        }
    }
}
