using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using PInt3 = PoiGenerator.Gen.Int3;

namespace PlanetSurface.Infra;

/// <summary>
/// The ground the people of each place made round it, as patches the terrain shader paints (<see cref="GroundPatch"/>):
/// the yard over its whole pad, an apron round each building, a forecourt or a loading apron at its way in, a car
/// park with painted bays on its roomiest side, and paths and drives from every other building's door to the way in.
/// What each is laid in follows the place and the world: concrete and tarmac where the world built that way, gravel,
/// trodden dirt and flags on a frontier world, a farm's dirt yard, nothing laid at all at an alien hive or a wreck.
/// A city's blocks get footpaths of flags to their kerbs (a grass verge in front of houses), crossovers at the lots'
/// gates and their yards paved by use; the roadside ruins and the start town's houses their own yards and paths.
/// The weather wears it all by the place's age (the shader cracks it, breaks slabs, lets weeds into the joints and
/// the land in from the edges). Every pad given patches is marked <see cref="TerrainStamp.Painted"/>, and any pad
/// with a made surface no plan here reached still gets a plain yard of it, so no place is left on bare grass.
/// </summary>
public static class MadeGrounds
{
    /// <summary>How far past its footprint a place's yard runs (most of the pad's flat apron, the rest is its crumbling edge).</summary>
    const float YardOut = 3f;

    /// <summary>Proof frames: plan none, so the places stand on their pads' mesh colours as they did before (--made=0).</summary>
    public static bool Off;

    public static List<GroundPatch> Plan(InfraNetwork net, IReadOnlyList<PoiSite> pois, IReadOnlyList<TerrainStamp> stamps, WorldProfile profile, double w, double h)
    {
        var patches = new List<GroundPatch>();
        if (Off) return patches;
        TechLevel tech = profile?.Before ?? TechLevel.Mid;
        foreach (PoiSite site in pois)
        {
            // A typed place's ground is its own (its runway, taxiways, apron, car park), laid from its sketch.
            if (site.Whole is { } whole)
            {
                if (whole.Anchor == site) patches.AddRange(whole.Grounds(whole.Stamp.Wear));
                continue;
            }
            if (site.Siting == "city" || site.Layout == null) continue;
            PInt3 face = site.Layout.EntranceFacing.Vec();
            Place(patches, site.Layout, site.Origin.X, site.Origin.Y, site.Origin.Z, site.Stamp, site.Family, tech,
                (site.Entrance.X + 0.5, site.Entrance.Z + 0.5), (face.X, face.Z));
        }
        if (net?.City is CityPlan city) City(patches, city, w, h);
        if (net != null)
            foreach (FlavourRuin ruin in net.Ruins)
            {
                if (ruin.Layout == null || ruin.Stamp == null) continue;
                PInt3 e = ruin.Layout.EntranceCell, face = ruin.Layout.EntranceFacing.Vec();
                Place(patches, ruin.Layout, ruin.OriginX, ruin.OriginY, ruin.OriginZ, ruin.Stamp, ruin.Family, tech,
                    (ruin.OriginX + e.X + 0.5, ruin.OriginZ + e.Z + 0.5), (face.X, face.Z));
            }
        // Whatever else was laid flat with a made surface (a station's way in, a yard of the roads side's): a plain yard of it.
        foreach (TerrainStamp t in stamps)
        {
            if (t.Shape != StampShape.Pad || t.Painted || t.Surface is StampSurface.Natural or StampSurface.Lawn) continue;
            patches.Add(Yard(t, CoverOf(t.Surface), t.Wear, 0));
            t.Painted = true;
        }
        return patches;
    }

    /// <summary>What lies over a point as the terrain shader finds it (tests and proof frames): each patch within 3 m, its distance past the outline and how far off its height the ground at <paramref name="y"/> is.</summary>
    public static IEnumerable<string> Probe(IReadOnlyList<GroundPatch> patches, double x, double z, float y, double w, double h)
    {
        foreach (GroundPatch p in patches)
        {
            double rx = Mathx.WrapDelta(x - p.X, w), rz = Mathx.WrapDelta(z - p.Z, h);
            double lu = rx * p.UX + rz * p.UZ, lv = -rx * p.UZ + rz * p.UX;
            float r = Math.Min(p.Round, Math.Min(p.HalfU, p.HalfV));
            double qx = Math.Abs(lu) - (p.HalfU - r), qz = Math.Abs(lv) - (p.HalfV - r);
            double d = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qz, 0) * Math.Max(qz, 0)) + Math.Min(Math.Max(qx, qz), 0) - r;
            if (d > 3) continue;
            yield return $"{p.Cover} {p.Pattern} layer {p.Layer} d {d:F1} off {y - p.Y:F1} give {p.Give:F1}";
        }
    }

    static MadeCover CoverOf(StampSurface s) => s switch
    {
        StampSurface.Concrete => MadeCover.Concrete,
        StampSurface.Tarmac => MadeCover.Tarmac,
        StampSurface.Gravel => MadeCover.Gravel,
        StampSurface.Lawn => MadeCover.Lawn,
        _ => MadeCover.Dirt,
    };

    /// <summary>A pad's whole footprint and most of its apron, laid in one surface, its corners rounded.</summary>
    static GroundPatch Yard(TerrainStamp t, MadeCover cover, float wear, int layer)
    {
        float a = Math.Min(t.Apron, YardOut);
        // A laid yard has square corners eased a little; a gravel or earth one wears round.
        bool soft = cover is MadeCover.Gravel or MadeCover.Dirt;
        float least = Math.Min(t.MaxX - t.MinX, t.MaxZ - t.MinZ) + 2 * a;
        var p = GroundPatch.Box(t.MinX - a, t.MinZ - a, t.MaxX + a, t.MaxZ + a, t.PadY, cover, cover is MadeCover.Concrete ? GroundPattern.Slabs : GroundPattern.Plain, wear, layer,
            round: soft ? Math.Min(18f, least * 0.3f) : Math.Min(6f, 2f + 0.06f * least));
        p.Slab = 6f;
        return p;
    }

    // ---- what kind of place it is -------------------------------------------------------------------

    static bool Any(FamilyDef f, params string[] tags) => tags.Any(f.Tags.Contains);
    static bool Wild(FamilyDef f) => Any(f, "alien", "wreck", "precursor", "hive") || f.Tier == PoiTier.Wreck;
    static bool Works(FamilyDef f) => Any(f, "industry", "working", "mine", "power", "fuel", "salvage", "ore", "machine", "utility", "water");
    static bool Rural(FamilyDef f) => Any(f, "farm", "farmstead", "settlement", "frontier", "shed", "gang", "clearing", "house");
    static bool Military(FamilyDef f) => Any(f, "military", "fort", "prison");

    // ---- a place: its yard, aprons, forecourt or loading apron, car park and paths ----------------------

    /// <summary>A building's box on the ground, world cells (max exclusive), and its doors (world metres, and which way out).</summary>
    sealed class Building
    {
        public int X0, Z0, X1, Z1;
        public readonly List<(double X, double Z, int OX, int OZ)> Doors = new();
        public double Cx => (X0 + X1) * 0.5;
        public double Cz => (Z0 + Z1) * 0.5;
        public double Gap(double x, double z) => Math.Max(Math.Max(X0 - x, x - X1), Math.Max(Z0 - z, z - Z1));
    }

    static void Place(List<GroundPatch> patches, Layout layout, int ox, int oy, int oz, TerrainStamp stamp, FamilyDef family, TechLevel tech,
        (double X, double Z) entrance, (int X, int Z) facing)
    {
        if (stamp == null || stamp.Shape != StampShape.Pad || Wild(family)) return;
        stamp.Painted = true;
        float wear = stamp.Wear, y = stamp.PadY;
        bool works = Works(family), rural = Rural(family) || tech == TechLevel.Low && !works, military = Military(family);
        MadeCover yard = stamp.Surface switch
        {
            StampSurface.Natural => rural ? MadeCover.Dirt : MadeCover.Gravel,
            _ => CoverOf(stamp.Surface),
        };
        // A house's garden is its own grass: no yard, only the paths and the apron.
        bool garden = family.Tags.Contains("house") && stamp.Surface == StampSurface.Natural;
        if (!garden) patches.Add(Yard(stamp, yard, wear, 0));

        var buildings = Buildings(layout, ox, oz);
        if (buildings.Count == 0) return;
        bool paved = yard is MadeCover.Concrete or MadeCover.Tarmac;
        // Round each building: a concrete apron on a world that poured it, gravel or flags on one that didn't, trodden ground on a farm.
        MadeCover apron = tech == TechLevel.Low ? (rural ? MadeCover.Dirt : MadeCover.Gravel)
            : rural ? MadeCover.Flags : paved || works || military ? MadeCover.Concrete : MadeCover.Flags;
        float apronW = apron == MadeCover.Dirt ? 1.5f : works ? 3f : 2f;
        foreach (Building b in buildings)
        {
            var p = GroundPatch.Box(b.X0 - apronW, b.Z0 - apronW, b.X1 + apronW, b.Z1 + apronW, y, apron, apron == MadeCover.Concrete ? GroundPattern.Slabs : GroundPattern.Plain, wear, 1, round: 1f);
            p.Slab = apron == MadeCover.Flags ? 0.9f : 3f;
            p.Kerb = tech == TechLevel.High && apron == MadeCover.Concrete;
            patches.Add(p);
        }

        // The way in: the building it opens from, the ground in front of it out to the yard's edge.
        Building main = buildings.OrderBy(b => b.Gap(entrance.X, entrance.Z)).First();
        float ya = Math.Min(stamp.Apron, YardOut);
        double edge(int dx, int dz) => dx > 0 ? stamp.MaxX + ya - main.X1 : dx < 0 ? main.X0 - (stamp.MinX - ya) : dz > 0 ? stamp.MaxZ + ya - main.Z1 : main.Z0 - (stamp.MinZ - ya);
        double Room(int dx, int dz)
        {
            // How far out from this face of the main building the ground is open (another building in the way stops it).
            double room = edge(dx, dz);
            foreach (Building o in buildings)
            {
                if (o == main) continue;
                bool across = dx != 0 ? o.Z1 > main.Z0 && o.Z0 < main.Z1 : o.X1 > main.X0 && o.X0 < main.X1;
                if (!across) continue;
                double gap = dx > 0 ? o.X0 - main.X1 : dx < 0 ? main.X0 - o.X1 : dz > 0 ? o.Z0 - main.Z1 : main.Z0 - o.Z1;
                if (gap >= 0) room = Math.Min(room, gap - 1);
            }
            return room;
        }
        bool served = tech != TechLevel.Low && !rural;
        int fx = Math.Sign(facing.X), fz = facing.X != 0 ? 0 : Math.Sign(facing.Z);
        if (fx != 0 || fz != 0)
        {
            double depth = Room(fx, fz) - apronW;
            if (depth >= 3)
            {
                bool loading = works && served && depth >= 7;
                MadeCover front = loading ? MadeCover.Concrete : served ? (paved ? MadeCover.Flags : MadeCover.Concrete) : rural ? MadeCover.Dirt : MadeCover.Gravel;
                if (front == MadeCover.Dirt && yard == MadeCover.Dirt) front = MadeCover.Gravel;
                patches.Add(Front(main, fx, fz, apronW, Math.Min(depth, loading ? 14 : 9), y, front, loading ? GroundPattern.Loading : front == MadeCover.Concrete ? GroundPattern.Slabs : GroundPattern.Plain, wear, 2));
            }
        }
        // A car park on the roomiest other side, where the world drove and the place had people coming to it.
        if (served && family.Tier != PoiTier.Camp)
        {
            var sides = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Where(s => s != (fx, fz)).Select(s => (s, room: Room(s.Item1, s.Item2) - apronW)).OrderByDescending(s => s.room).ToList();
            var (side, depth) = sides[0];
            double width = side.Item1 != 0 ? main.Z1 - main.Z0 : main.X1 - main.X0;
            if (depth >= 12 && width >= 12)
                patches.Add(Front(main, side.Item1, side.Item2, apronW + 1, Math.Min(depth - 1, 22), y, MadeCover.Tarmac, GroundPattern.Bays, wear, 2));
        }
        // From every other building's door to the way in: a drive at a works or a base, a path elsewhere, round its corner.
        MadeCover way = tech == TechLevel.Low || rural ? (yard == MadeCover.Dirt ? MadeCover.Gravel : MadeCover.Dirt) : works || military ? MadeCover.Concrete : MadeCover.Flags;
        float half = works || military ? 1.8f : rural ? 1.1f : 0.9f;
        (double X, double Z) hub = (entrance.X + fx * 2, entrance.Z + fz * 2);
        foreach (Building b in buildings)
        {
            if (b == main || b.Doors.Count == 0) continue;
            var (dx, dz, ox2, oz2) = b.Doors.OrderBy(d => Math.Abs(d.X - hub.X) + Math.Abs(d.Z - hub.Z)).First();
            (double X, double Z) step = (dx + ox2 * 2.5, dz + oz2 * 2.5);
            // Out of the door, then square across to the way in; the corner rounds where the two meet.
            (double X, double Z) corner = ox2 != 0 ? (hub.X, step.Z) : (step.X, hub.Z);
            if (buildings.Any(o => o.Gap(corner.X, corner.Z) < 0)) corner = ox2 != 0 ? (step.X, hub.Z) : (hub.X, step.Z);
            Path(patches, (dx, dz), step, half, y, way, wear);
            Path(patches, step, corner, half, y, way, wear);
            Path(patches, corner, hub, half, y, way, wear);
        }
    }

    static void Path(List<GroundPatch> patches, (double X, double Z) a, (double X, double Z) b, float half, float y, MadeCover cover, float wear)
    {
        if (Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z) < 0.5) return;
        var p = GroundPatch.Strip(a.X, a.Z, b.X, b.Z, half, y, cover, wear, 3);
        p.Pattern = cover == MadeCover.Concrete ? GroundPattern.Slabs : GroundPattern.Plain;
        p.Slab = cover == MadeCover.Concrete ? 3f : 0.9f;
        patches.Add(p);
    }

    /// <summary>A patch against one face of a building, from <paramref name="from"/> metres off it to <paramref name="to"/>, as wide as the face; its V axis points back at the building.</summary>
    static GroundPatch Front(Building b, int dx, int dz, double from, double to, float y, MadeCover cover, GroundPattern pattern, float wear, int layer)
    {
        double mid = (from + to) * 0.5;
        double cx = dx > 0 ? b.X1 + mid : dx < 0 ? b.X0 - mid : b.Cx;
        double cz = dz > 0 ? b.Z1 + mid : dz < 0 ? b.Z0 - mid : b.Cz;
        double width = dx != 0 ? b.Z1 - b.Z0 : b.X1 - b.X0;
        // U along the face; V = (-uz, ux) must point back at the building, against the way out (dx, dz).
        float ux = -dz, uz = dx;
        if (-uz * dx + ux * dz > 0) { ux = -ux; uz = -uz; }
        return new GroundPatch
        {
            X = cx, Z = cz, UX = ux, UZ = uz, HalfU = (float)Math.Min(width * 0.5 + 1, 30), HalfV = (float)(to - from) * 0.5f, Round = 1.5f, Y = y,
            Cover = cover, Pattern = pattern, Wear = wear, Layer = layer, Slab = cover == MadeCover.Flags ? 0.9f : 4f,
        };
    }

    /// <summary>
    /// The buildings of a layout on the ground: whatever stands solid at the foot of the place (walls, tanks, stacks of
    /// containers), grouped where it touches, each as its box in world cells, and the doors out of it onto the yard.
    /// Fences and lone props are left out (too thin, or too small).
    /// </summary>
    static List<Building> Buildings(Layout layout, int ox, int oz)
    {
        BlockGrid g = layout.Blocks;
        int w = g.Size.X, d = g.Size.Z;
        var solid = new bool[w * d];
        foreach (var (c, b) in g.Blocks())
        {
            if (c.Y < 0 || c.Y > 2 || !BlockInfo.Collides(b.Type) || b.Material == Material.Plant || b.Role is Role.Debris or Role.Floor) continue;
            solid[(c.Z - g.Min.Z) * w + (c.X - g.Min.X)] = true;
        }
        var seen = new bool[w * d];
        var result = new List<Building>();
        var queue = new Queue<int>();
        for (int start = 0; start < solid.Length; start++)
        {
            if (!solid[start] || seen[start]) continue;
            int count = 0, x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int k = queue.Dequeue(), x = k % w, z = k / w;
                count++;
                (x0, z0, x1, z1) = (Math.Min(x0, x), Math.Min(z0, z), Math.Max(x1, x), Math.Max(z1, z));
                // Two cells apart still one building (a gap where a wall fell, a doorway).
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)d) continue;
                    int n = nz * w + nx;
                    if (!solid[n] || seen[n]) continue;
                    seen[n] = true;
                    queue.Enqueue(n);
                }
            }
            int bw = x1 - x0 + 1, bd = z1 - z0 + 1;
            if (count < 12 || Math.Min(bw, bd) < 3 || count < 0.3 * bw * bd) continue;
            result.Add(new Building { X0 = ox + g.Min.X + x0, Z0 = oz + g.Min.Z + z0, X1 = ox + g.Min.X + x1 + 1, Z1 = oz + g.Min.Z + z1 + 1 });
        }
        // Doors onto the yard: the ground-floor doorways between an outdoor module and anything else.
        foreach (Doorway door in layout.Doorways)
        {
            if (door.Cells.Count == 0 || door.ModuleA < 0 || door.ModuleB < 0 || door.ModuleA >= layout.Modules.Count || door.ModuleB >= layout.Modules.Count) continue;
            bool a = layout.Modules[door.ModuleA].Kind == ModuleKind.Outdoor, b = layout.Modules[door.ModuleB].Kind == ModuleKind.Outdoor;
            if (a == b) continue;
            int lowest = door.Cells.Min(c => c.Y);
            if (lowest < -1 || lowest > 1) continue;
            double cx = ox + door.Cells.Average(c => c.X) + 0.5, cz = oz + door.Cells.Average(c => c.Z) + 0.5;
            PInt3 v = door.Facing.Vec();
            Building owner = result.OrderBy(r => r.Gap(cx, cz)).FirstOrDefault();
            if (owner == null || owner.Gap(cx, cz) > 3) continue;
            // Out is away from the building's middle.
            int sx = v.X, sz = v.Z;
            if (sx * (cx - owner.Cx) + sz * (cz - owner.Cz) < 0) { sx = -sx; sz = -sz; }
            owner.Doors.Add((cx, cz, sx, sz));
        }
        return result;
    }

    // ---- a city: footpaths to the kerbs, verges before the houses, crossovers, yards by use ---------------

    /// <summary>The deck of the city street nearest a point within 14 m (null where none runs).</summary>
    static float? DeckNear(CityPlan city, double x, double z, double w, double h)
    {
        float? deck = null;
        double best = 14;
        foreach (InfraRoute r in city.Streets)
            for (int i = 0; i < r.Count; i++)
            {
                double dx = Mathx.WrapDelta(r.X[i] - x, w), dz = Mathx.WrapDelta(r.Z[i] - z, h);
                if (Math.Abs(dx) > best || Math.Abs(dz) > best) continue;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d < best) { best = d; deck = r.Y[i]; }
            }
        return deck;
    }

    static void City(List<GroundPatch> patches, CityPlan city, double w, double h)
    {
        foreach (TerrainStamp t in city.Stamps)
        {
            // A block levelled as one: flags to the kerb all round (the streets' decks lie over their own half),
            // and its own ground by district inside the lot lines.
            string district = t.Name.StartsWith("city ") ? t.Name[5..] : "";
            float y = t.PadY;
            float kerbIn = (float)(CityPlan.Inset - CityPlan.Side) + 0.3f;
            var path = GroundPatch.Box(t.MinX - kerbIn, t.MinZ - kerbIn, t.MaxX + kerbIn, t.MaxZ + kerbIn, y, MadeCover.Flags, GroundPattern.Plain, t.Wear, 0, round: 4f);
            path.Slab = 0.9f;
            path.Kerb = true;
            path.Give = 3f;
            patches.Add(path);
            // Each side's footpath on its own street's level too, where a street runs along it: a block on a slope sits
            // level with the mean of its streets, and the flags must still reach the kerb of one higher or lower.
            foreach (var (sx, sz, x0, z0, x1, z1) in new (double, double, double, double, double, double)[]
            {
                ((t.MinX + t.MaxX) * 0.5, t.MinZ - CityPlan.Inset, t.MinX - kerbIn, t.MinZ - kerbIn, t.MaxX + kerbIn, t.MinZ + 0.5),
                ((t.MinX + t.MaxX) * 0.5, t.MaxZ + CityPlan.Inset, t.MinX - kerbIn, t.MaxZ - 0.5, t.MaxX + kerbIn, t.MaxZ + kerbIn),
                (t.MinX - CityPlan.Inset, (t.MinZ + t.MaxZ) * 0.5, t.MinX - kerbIn, t.MinZ - kerbIn, t.MinX + 0.5, t.MaxZ + kerbIn),
                (t.MaxX + CityPlan.Inset, (t.MinZ + t.MaxZ) * 0.5, t.MaxX - 0.5, t.MinZ - kerbIn, t.MaxX + kerbIn, t.MaxZ + kerbIn),
            })
            {
                if (DeckNear(city, sx, sz, w, h) is not float deck || Math.Abs(deck - y) < 1f) continue;
                var side = GroundPatch.Box(x0, z0, x1, z1, deck, MadeCover.Flags, GroundPattern.Plain, t.Wear, 0, round: 2f);
                side.Slab = 0.9f;
                side.Kerb = true;
                side.Give = 3f;
                patches.Add(side);
            }
            GroundPatch inner = district switch
            {
                "plaza" or "civic" => GroundPatch.Box(t.MinX, t.MinZ, t.MaxX, t.MaxZ, y, MadeCover.Flags, GroundPattern.Plain, t.Wear, 1, round: 2f),
                "works" or "station" => GroundPatch.Box(t.MinX, t.MinZ, t.MaxX, t.MaxZ, y, MadeCover.Concrete, GroundPattern.Slabs, t.Wear, 1, round: 2f),
                "school" or "university" or "park" => GroundPatch.Box(t.MinX, t.MinZ, t.MaxX, t.MaxZ, y, MadeCover.Lawn, GroundPattern.Plain, t.Wear, 1, round: 2f),
                _ => GroundPatch.Box(t.MinX, t.MinZ, t.MaxX, t.MaxZ, y, MadeCover.Concrete, GroundPattern.Slabs, t.Wear, 1, round: 2f),
            };
            inner.Slab = inner.Cover == MadeCover.Flags ? 2f : 5f;
            patches.Add(inner);
            t.Painted = true;
        }
        foreach (CityLotPlan lot in city.Lots)
        {
            if (lot.Site == null) continue;
            TerrainStamp s = lot.Site.Stamp;
            s.Painted = true;
            float y = lot.Pad, wear = s.Wear;
            // The lot's own ground where its building leaves it bare: grass round a house or a school, paving round the rest.
            MadeCover own = lot.Kind switch
            {
                "house" or "terrace" or "school" or "university" or "civic" => MadeCover.Lawn,
                "station" => MadeCover.Tarmac,
                _ => MadeCover.Concrete,
            };
            // (A lot paved like its block's own ground needs nothing of its own: the block's paving shows.)
            if (own != MadeCover.Concrete || lot.District == "homes")
                patches.Add(GroundPatch.Box(s.MinX, s.MinZ, s.MaxX, s.MaxZ, y, own, own == MadeCover.Concrete ? GroundPattern.Slabs : GroundPattern.Plain, wear, 2, round: 0.5f));
            bool alongX = lot.Front is Dir.North or Dir.South;
            int outward = lot.Front is Dir.North or Dir.West ? -1 : 1;
            var (fx0, fz0, fx1, fz1) = lot.Frontage;
            double line = alongX ? fz0 : fx0;
            double kerb = CityPlan.Inset - (lot.Street?.HalfWidth ?? CityPlan.Side);
            if (lot.District == "homes")
            {
                // In front of the houses: a footpath along the lot line, a grass verge to the kerb. Their ground steps with
                // the street, so each lot's frontage is its own.
                double a0 = alongX ? Math.Min(fx0, fx1) : Math.Min(fz0, fz1), a1 = a0 + (alongX ? lot.X1 - lot.X0 : lot.Z1 - lot.Z0);
                GroundPatch Band(double from, double to, MadeCover cover, int layer)
                {
                    double b0 = line + outward * from, b1 = line + outward * to;
                    return alongX ? GroundPatch.Box(a0, Math.Min(b0, b1), a1, Math.Max(b0, b1), y, cover, GroundPattern.Plain, wear, layer, round: 0.2f)
                        : GroundPatch.Box(Math.Min(b0, b1), a0, Math.Max(b0, b1), a1, y, cover, GroundPattern.Plain, wear, layer, round: 0.2f);
                }
                patches.Add(Band(0, kerb + 0.3, MadeCover.Lawn, 1));
                var foot = Band(0, 1.8, MadeCover.Flags, 2);
                foot.Slab = 0.9f;
                foot.Give = 3f;
                patches.Add(foot);
            }
            // The crossover: the drive (or the path) from the gate over the footpath and verge to the kerb.
            if (lot.DriveWidth > 0)
            {
                var (gx, gz) = lot.Gate;
                double kx = alongX ? gx : gx + outward * kerb, kz = alongX ? gz + outward * kerb : gz;
                double ix = alongX ? gx : gx - outward * 0.5, iz = alongX ? gz - outward * 0.5 : gz;
                bool drive = lot.DriveWidth >= 3f;
                var cross = GroundPatch.Strip(ix, iz, kx, kz, lot.DriveWidth * 0.5f, y, drive ? MadeCover.Concrete : MadeCover.Flags, wear, 3);
                cross.Round = 0.3f;
                cross.Slab = drive ? 3f : 0.9f;
                cross.Pattern = drive ? GroundPattern.Slabs : GroundPattern.Plain;
                cross.Give = 3f;
                patches.Add(cross);
            }
        }
    }
}
