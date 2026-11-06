using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GalHack.Items;
using GalHack.Structures;
using PoiGenerator.Gen;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using Int3 = GalHack.Structures.Int3;

namespace Slice.Orbit;

/// <summary>One wreck (or one piece of a wreck) to be set adrift in orbit: its blocks, where it floats, and what its hold carries.</summary>
public sealed class DerelictPlan
{
    public string Id;
    public string Name;
    /// <summary>The POI generator's site (poi-generator/data/orbit.txt) it comes from.</summary>
    public SpaceSiteDef Site;
    public PoiState State;
    public string Zone;
    public Structure Structure;
    public N.Vector3 Position;
    public N.Quaternion Rotation;
    /// <summary>What its container blocks hold (its hold, shared storage).</summary>
    public Bill Loot = new();
    /// <summary>Its way in from outside: a hull door, dead and shut, forced by hand (structure cells), if it has one.</summary>
    public Int3? Door;
}

/// <summary>
/// The start system's wrecks near the world, from the POI generator's data: its start-system
/// placement (<see cref="OrbitPlacement"/>: kinds, salvage grade, the "near" and "orbit" bands, a
/// bearing and a distance) brought in close, and each kind built as real blocks (building's
/// structure model, large grid) so it can be boarded, looted, salvaged block by block and claimed
/// with a core:
/// <list type="bullet">
/// <item>a derelict ship (wreck): a hull with a bridge, a crew deck and a hold behind bulkheads, a hull door amidships, a breach in its back, engines dead</item>
/// <item>a drifting hulk (hulk): a long liner broken in two at its spine, open where it parted</item>
/// <item>a broken station (broken, and any dead station or outpost: a start system holds no live faction): modules on truss tubes, a hull door, one module torn off and drifting</item>
/// <item>a wreck field (field): hull fragments and loose cargo boxes tumbling apart</item>
/// <item>an escape pod cluster (field): pods that launched and were never picked up</item>
/// </list>
/// None has a core or power: no field holds any air in their doors, so their insides are vacuum,
/// nothing holds you to a floor, and any of them can be claimed.
/// Everything is seeded from the system's seed, so the same world always has the same wrecks.
/// </summary>
public static class Derelicts
{
    public static List<DerelictPlan> Plan(PoiLibrary library, ulong systemSeed, GlobeMap globe, double atmosphereRadius,
        OrbitRules rules, Catalogue catalogue, ItemCatalogue items)
    {
        var plans = new List<DerelictPlan>();
        int site = 0;
        foreach (OrbitPoi poi in OrbitPlacement.Place(library, systemSeed, start: true))
        {
            if (poi.Zone is not ("near" or "orbit")) continue; // moons, belts and deep space are other zones
            site++;
            var rng = new Random(unchecked((int)(poi.Seed ^ (poi.Seed >> 32))));
            // The bearing is its longitude; its latitude from its seed, within the band.
            double latitude = (rng.NextDouble() * 2 - 1) * rules.Wrecks.MaxLatitude;
            double x = poi.Bearing / 360.0 * globe.Width, z = (90 - latitude) / 180 * globe.Height;
            float above = Altitude(poi, rules.Wrecks);
            N.Vector3 centre = globe.Direction(x, z) * (float)(atmosphereRadius + above);
            string id = $"site{site}-{poi.Site.Id.Replace('_', '-')}";
            List<DerelictPlan> pieces = poi.Site.Kind switch
            {
                "wreck" => Ship(rng, poi, id, catalogue),
                "hulk" => Hulk(rng, poi, id, catalogue),
                "field" when poi.Site.Id.Contains("pod") => Pods(rng, poi, id, catalogue),
                "field" => Field(rng, poi, id, catalogue),
                _ => Station(rng, poi, id, catalogue),
            };
            foreach (DerelictPlan p in pieces)
            {
                p.Site = poi.Site;
                p.State = poi.State;
                p.Zone = poi.Zone;
                // Pieces were laid out round the site's centre in its own frame: turn the whole site.
                N.Quaternion siteTurn = RandomTurn(rng);
                p.Position = centre + N.Vector3.Transform(p.Position, siteTurn);
                p.Rotation = N.Quaternion.Normalize(siteTurn * p.Rotation);
                Stock(p, rng, poi.Site.Salvage, rules, items);
                plans.Add(p);
            }
        }
        return plans;
    }

    /// <summary>Metres above the atmosphere's top for a site: its band's range, nearer sites lower (orbit's band on a log scale).</summary>
    static float Altitude(OrbitPoi poi, OrbitRules.WreckRules r)
    {
        if (poi.Zone == "near")
        {
            float t = Math.Clamp((poi.RadiusKm - 200f) / 1800f, 0f, 1f);
            return r.Near[0] + (r.Near[1] - r.Near[0]) * t;
        }
        float u = Math.Clamp(MathF.Log(poi.RadiusKm / 1000f) / MathF.Log(30f), 0f, 1f);
        return r.Orbit[0] + (r.Orbit[1] - r.Orbit[0]) * u;
    }

    static N.Quaternion RandomTurn(Random rng)
    {
        // A uniformly random orientation (Shoemake).
        double u1 = rng.NextDouble(), u2 = rng.NextDouble() * Math.Tau, u3 = rng.NextDouble() * Math.Tau;
        double a = Math.Sqrt(1 - u1), b = Math.Sqrt(u1);
        return N.Quaternion.Normalize(new N.Quaternion((float)(a * Math.Sin(u2)), (float)(a * Math.Cos(u2)), (float)(b * Math.Sin(u3)), (float)(b * Math.Cos(u3))));
    }

    /// <summary>Fills a plan's hold: one roll of its grade's loot table for each container block.</summary>
    static void Stock(DerelictPlan p, Random rng, string grade, OrbitRules rules, ItemCatalogue items)
    {
        int containers = p.Structure.Blocks().Count(b => p.Structure.TypeOf(b.block).IsContainer);
        for (int i = 0; i < containers; i++)
            foreach (var (item, least, most) in rules.LootFor(grade))
            {
                if (!items.Has(item)) continue;
                int n = rng.Next(least, most + 1);
                if (n > 0) p.Loot.Add(item, n);
            }
    }

    // ---- Kinds ---------------------------------------------------------------------------------

    /// <summary>A derelict ship: a boxy hull, bridge forward, crew deck, hold aft behind bulkheads, a hull door amidships to starboard, a breach in the hold's roof.</summary>
    static List<DerelictPlan> Ship(Random rng, OrbitPoi poi, string id, Catalogue catalogue)
    {
        bool military = poi.Site.Salvage == "military";
        int w = 2 + rng.Next(0, 2), h = 2, l = 7 + rng.Next(0, 3);
        var b = new Cells(rng, military ? "4a4e45" : HullColour(rng));
        b.Box(-w - 1, 0, -l - 1, w + 1, h + 1, l + 1, "hull", hollow: true);
        // The bridge: windows across its front, a dead pilot's seat.
        for (int x = -w; x <= w; x++) b.Set(x, h, -l - 1, "glass");
        b.Set(0, 1, -l, "cockpit");
        // Bulkheads between bridge, crew deck and hold, each with an open door.
        foreach (int z in new[] { -l + 3, l - 5 })
        {
            b.Box(-w, 1, z, w, h, z, "wall");
            b.Set(0, 1, z, "door", open: true);
        }
        b.HullDoor(w + 1, 0);
        // The hold: cargo boxes along both walls.
        for (int z = l - 3; z <= l - 1; z++)
        {
            b.Set(-w, 1, z, "container", paint: "cargo");
            if (z != l - 2) b.Set(w, 1, z, "container", paint: "cargo");
        }
        // The engine room's dead machinery, and the engines out behind.
        b.Set(-w, 1, l - 4, "generator");
        b.Set(w, 1, l - 4, "fueltank");
        for (int x = -w + 1; x <= w - 1; x += 2) b.Set(x, 1, l + 2, "thruster", rot: "+y+z");
        // The breach: something tore the hold's roof open.
        b.Hole(0, h + 1, l - 2, 1);
        b.Scars(6);
        return new List<DerelictPlan> { b.Plan(id, poi.Site.Name, catalogue) };
    }

    /// <summary>A drifting hulk: a long liner broken at its spine, the two halves drifting apart, each open where it parted.</summary>
    static List<DerelictPlan> Hulk(Random rng, OrbitPoi poi, string id, Catalogue catalogue)
    {
        int w = 3, h = 3, l = 13;
        string colour = HullColour(rng);
        var fore = new Cells(rng, colour);
        fore.Box(-w - 1, 0, -l - 1, w + 1, h + 1, 0, "hull", hollow: true);
        fore.Clear(-w, 1, 0, w, h, 0); // the break
        for (int x = -w; x <= w; x++) fore.Set(x, h, -l - 1, "glass");
        fore.Box(-w, 2, -l + 4, w, 2, -2, "deck");
        fore.Clear(-1, 2, -6, 1, 2, -5);
        fore.HullDoor(w + 1, -l + 6);
        foreach (int z in new[] { -l + 2, -8, -4 }) fore.Set(-w, 1, z, "container", paint: "cargo");
        fore.Scars(8);
        var aft = new Cells(rng, colour);
        aft.Box(-w - 1, 0, 0, w + 1, h + 1, l - 4, "hull", hollow: true);
        aft.Clear(-w, 1, 0, w, h, 0);
        for (int x = -w + 1; x <= w - 1; x += 2) aft.Set(x, 2, l - 3, "thruster", rot: "+y+z");
        foreach (int z in new[] { 3, 6 }) aft.Set(w, 1, z, "container", paint: "cargo");
        aft.Set(-w, 1, 5, "generator");
        aft.Scars(6);
        DerelictPlan a = fore.Plan(id + "-fore", poi.Site.Name + " (fore)", catalogue);
        DerelictPlan c = aft.Plan(id + "-aft", poi.Site.Name + " (aft)", catalogue);
        a.Position = new N.Vector3(0, 0, -6);
        c.Position = new N.Vector3(1.5f, -2f, 14f);
        c.Rotation = N.Quaternion.CreateFromYawPitchRoll(0.25f, 0.18f, 0.1f);
        return new List<DerelictPlan> { a, c };
    }

    /// <summary>A broken station: a hub with an airlock, modules out on truss tubes, one module torn off and drifting.</summary>
    static List<DerelictPlan> Station(Random rng, OrbitPoi poi, string id, Catalogue catalogue)
    {
        var hub = new Cells(rng, "d8d4c8", accent: "1b263b");
        hub.Box(-3, 0, -3, 3, 4, 3, "hull", hollow: true);
        hub.Box(-2, 2, -2, 2, 2, 2, "deck");
        hub.Clear(-1, 2, -1, 0, 2, 0);
        hub.HullDoor(3, 1);
        foreach (var (x, z) in new[] { (-2, -2), (2, -2), (-2, 2) }) hub.Set(x, 1, z, "container", paint: "cargo");
        // Truss tubes out along -z and +x, a module on the -z one.
        hub.Tube(0, 2, -4, 0, 0, -1, 5);
        hub.Set(0, 2, -3, "door", open: true);
        hub.Box(-2, 0, -13, 2, 4, -9, "hull", hollow: true);
        hub.Set(0, 2, -9, "door", open: true);
        hub.Set(-1, 1, -11, "container", paint: "cargo");
        hub.Set(1, 1, -12, "container", paint: "cargo");
        hub.Tube(4, 2, 0, 1, 0, 0, 3); // torn off at its far end: a way in from open space
        hub.Set(3, 2, 0, "door", open: true);
        hub.Scars(8);
        DerelictPlan main = hub.Plan(id, poi.Site.Name, catalogue);
        // The torn-off module, its stub of tube open to space.
        var torn = new Cells(rng, "d8d4c8", accent: "1b263b");
        torn.Box(-2, -2, -2, 2, 2, 2, "hull", hollow: true);
        torn.Tube(-5, 0, 0, 1, 0, 0, 3);
        torn.Clear(-2, 0, 0, -2, 0, 0);
        torn.Set(1, -1, 1, "container", paint: "cargo");
        torn.Scars(4);
        DerelictPlan off = torn.Plan(id + "-module", poi.Site.Name + " (module)", catalogue);
        off.Position = new N.Vector3(26f, 6f, 9f);
        off.Rotation = N.Quaternion.CreateFromYawPitchRoll(0.6f, 0.4f, -0.3f);
        return new List<DerelictPlan> { main, off };
    }

    /// <summary>A wreck field: hull fragments and loose cargo boxes, scattered and tumbling apart.</summary>
    static List<DerelictPlan> Field(Random rng, OrbitPoi poi, string id, Catalogue catalogue)
    {
        var pieces = new List<DerelictPlan>();
        int n = 5 + rng.Next(0, 4);
        string colour = HullColour(rng);
        for (int i = 0; i < n; i++)
        {
            var f = new Cells(rng, colour);
            int size = 6 + rng.Next(0, 22);
            f.Clump(size);
            if (i % 2 == 0) f.Set(0, 1, 0, "container", paint: "cargo");
            DerelictPlan p = f.Plan($"{id}-{i + 1}", $"{poi.Site.Name}: fragment {i + 1}", catalogue);
            double a = i * Math.Tau / n + rng.NextDouble() * 0.5, r = 25 + rng.NextDouble() * 70;
            p.Position = new N.Vector3((float)(Math.Cos(a) * r), (float)((rng.NextDouble() - 0.5) * 40), (float)(Math.Sin(a) * r));
            p.Rotation = RandomTurn(rng);
            pieces.Add(p);
        }
        return pieces;
    }

    /// <summary>An escape pod cluster: a ring of pods, each a seat, a locker and a hatch.</summary>
    static List<DerelictPlan> Pods(Random rng, OrbitPoi poi, string id, Catalogue catalogue)
    {
        var pods = new List<DerelictPlan>();
        int n = 4 + rng.Next(0, 3);
        for (int i = 0; i < n; i++)
        {
            var p = new Cells(rng, "e85d04", accent: "2b2d42");
            p.Box(-1, 0, -2, 1, 2, 2, "hull", hollow: true);
            p.Set(0, 1, -2, "door");
            p.Set(0, 1, 0, "cockpit");
            p.Set(0, 1, 1, "container", paint: "cargo");
            p.Scars(1);
            DerelictPlan plan = p.Plan($"{id}-{i + 1}", $"Escape pod {i + 1}", catalogue);
            plan.Door = new Int3(0, 1, -2);
            double a = i * Math.Tau / n;
            plan.Position = new N.Vector3((float)(Math.Cos(a) * 22), (float)((rng.NextDouble() - 0.5) * 8), (float)(Math.Sin(a) * 22));
            plan.Rotation = RandomTurn(rng);
            pods.Add(plan);
        }
        return pods;
    }

    static string HullColour(Random rng) => new[] { "8d99ae", "a3a380", "6c757d", "b08968", "7f8c8d" }[rng.Next(0, 5)];

    // ---- Building a layout as blueprint text ---------------------------------------------------

    /// <summary>A layout in progress: cells to block statements, written out as building's blueprint text and built by its own loader.</summary>
    sealed class Cells
    {
        readonly Random _rng;
        readonly SortedDictionary<(int y, int z, int x), string> _cells = new();
        readonly string _hull, _accent;
        Int3? _door;

        public Cells(Random rng, string hull, string accent = "2b2d42")
        {
            _rng = rng;
            _hull = hull;
            _accent = accent;
        }

        public void Set(int x, int y, int z, string type, string paint = null, string rot = null, bool open = false)
        {
            paint ??= type switch { "steel" or "hardened" => "hull", "glass" => null, _ => null };
            var line = new StringBuilder(type);
            if (rot != null) line.Append(" rot=").Append(rot);
            if (paint != null) line.Append(" paint=").Append(paint);
            if (open) line.Append(" open");
            _cells[(y, z, x)] = line.ToString();
        }

        public void Clear(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        _cells.Remove((y, z, x));
        }

        /// <summary>A box of a kind: "hull" (its shell; steel, hardened on the edges), "wall" or "deck" (inner steel).</summary>
        public void Box(int x0, int y0, int z0, int x1, int y1, int z1, string kind, bool hollow = false)
        {
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int edges = (x == x0 || x == x1 ? 1 : 0) + (y == y0 || y == y1 ? 1 : 0) + (z == z0 || z == z1 ? 1 : 0);
                        if (hollow && edges == 0) continue;
                        if (kind == "hull") Set(x, y, z, edges >= 2 ? "hardened" : "steel", edges >= 2 ? "trim" : "hull");
                        else Set(x, y, z, "steel", kind == "deck" ? "deck" : "wall");
                    }
        }

        /// <summary>A way in through a side wall: a hull door at floor height (dead, shut until it is forced).</summary>
        public void HullDoor(int wallX, int z)
        {
            Set(wallX, 1, z, "door", paint: "hazard");
            _door = new Int3(wallX, 1, z);
        }

        /// <summary>A square tube (3 by 3, a 1-cell passage) from a cell along an axis.</summary>
        public void Tube(int x, int y, int z, int dx, int dy, int dz, int length)
        {
            // The two axes across the tube.
            (int, int, int) p = dx != 0 ? (0, 1, 0) : (1, 0, 0), q = dz != 0 ? (0, 1, 0) : (0, 0, 1);
            if (dy != 0) (p, q) = ((1, 0, 0), (0, 0, 1));
            for (int i = 0; i < length; i++)
            {
                int cx = x + dx * i, cy = y + dy * i, cz = z + dz * i;
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b++)
                    {
                        int px = cx + p.Item1 * a + q.Item1 * b, py = cy + p.Item2 * a + q.Item2 * b, pz = cz + p.Item3 * a + q.Item3 * b;
                        if (a == 0 && b == 0) _cells.Remove((py, pz, px));
                        else Set(px, py, pz, "steel", "trim");
                    }
            }
        }

        /// <summary>A hole torn through: the cells within a radius of a point gone.</summary>
        public void Hole(int x, int y, int z, int r)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (dx * dx + dy * dy + dz * dz <= r * r + 1) _cells.Remove((y + dy, z + dz, x + dx));
        }

        /// <summary>Old damage: a few shell cells knocked out (never a door, a seat or a box).</summary>
        public void Scars(int count)
        {
            var shell = _cells.Where(c => c.Value.StartsWith("steel") && c.Value.Contains("paint=hull")).Select(c => c.Key).ToList();
            for (int i = 0; i < count && shell.Count > 0; i++)
            {
                int k = _rng.Next(0, shell.Count);
                _cells.Remove(shell[k]);
                shell.RemoveAt(k);
            }
        }

        /// <summary>A fragment: a random clump of plates and blocks grown from the origin.</summary>
        public void Clump(int size)
        {
            var at = new List<(int x, int y, int z)> { (0, 0, 0) };
            Set(0, 0, 0, "steel", "hull");
            while (_cells.Count < size)
            {
                var (x, y, z) = at[_rng.Next(0, at.Count)];
                int d = _rng.Next(0, 6);
                (x, y, z) = d switch { 0 => (x + 1, y, z), 1 => (x - 1, y, z), 2 => (x, y + 1, z), 3 => (x, y - 1, z), 4 => (x, y, z + 1), _ => (x, y, z - 1) };
                if (Math.Abs(y) > 1) continue;
                if (_cells.ContainsKey((y, z, x))) continue;
                Set(x, y, z, _rng.Next(0, 4) == 0 ? "hardened" : "steel", _rng.Next(0, 3) == 0 ? "trim" : "hull");
                at.Add((x, y, z));
            }
        }

        public DerelictPlan Plan(string id, string name, Catalogue catalogue)
        {
            var text = new StringBuilder();
            text.AppendLine("schema galhack.blueprint");
            text.AppendLine("version 1");
            text.AppendLine($"name \"{name}\"");
            text.AppendLine("grid large");
            text.AppendLine("mode capital");
            text.AppendLine("gravity off");
            text.AppendLine($"paint hull base={_hull} grime=3 texture=plates");
            text.AppendLine($"paint trim base={_accent} grime=3");
            text.AppendLine("paint wall base=4a4e69 grime=3");
            text.AppendLine("paint deck base=22223b grime=3 texture=ribbed");
            text.AppendLine("paint cargo base=e85d04 accent=2b2d42 pattern=stripes grime=3");
            text.AppendLine("paint hazard base=f4a100 accent=2b2d42 pattern=hazard grime=2");
            foreach (var ((y, z, x), what) in _cells)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"block {x} {y} {z} {what}"));
            BlueprintData data = Blueprint.Parse(text.ToString());
            // A wreck has no core (that is what makes it a wreck): every other rule holds.
            List<string> errors = Blueprint.Validate(data, catalogue).Where(e => !e.StartsWith("no core")).ToList();
            if (errors.Count > 0) throw new InvalidOperationException($"wreck '{name}': " + string.Join("; ", errors.Take(5)));
            Structure s = Blueprint.Build(data, catalogue);
            s.DirtyChunks.Clear();
            return new DerelictPlan { Id = id, Name = name, Structure = s, Rotation = N.Quaternion.Identity, Door = _door };
        }
    }
}
