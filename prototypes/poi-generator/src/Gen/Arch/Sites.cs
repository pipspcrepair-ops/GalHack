#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Composes a whole site from its plan: buildings planned from their programs, and the yard
/// around them (runways, pads, tank farms, container stacks, fences, lattice towers, craters,
/// wrecked aircraft, a derelict rocket on its pad with the gantry still beside it). Everything
/// outside stands on open ground, so every building's way in is a walk across the yard.
/// </summary>
public static class Sites
{
    sealed class Ctx
    {
        public Canvas C = null!;
        public PoiLibrary Lib = null!;
        public FamilyDef Fam = null!;
        public Rarity Rarity;
        public Rng Rng = null!;
        public SiteDef Site = null!;
        public int Yard = -1;
        public int VaultModule = -1, DeepestModule = -1;
        public List<int> Landmarks = new();
    }

    public static void Compose(PoiLibrary lib, Canvas c, SiteDef site, FamilyDef fam, Rarity rarity, Rng rng)
    {
        var x = new Ctx { C = c, Lib = lib, Fam = fam, Rarity = rarity, Rng = rng, Site = site };
        c.X.InSpace = site.Space;
        // The approach: a patch of open ground at the site's way in.
        var approach = new List<Int3>();
        Int3 e = new(site.EntranceX, 0, site.EntranceZ);
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++) { approach.Add(e + new Int3(dx, 0, dz)); approach.Add(e + new Int3(dx, 1, dz)); }
        x.Yard = c.AddModule(ModuleKind.Outdoor, "approach", 0, approach);
        c.EntranceModule = x.Yard;
        c.EntranceCell = e;
        c.EntranceFacing = site.EntranceFacing;
        c.ExitCells = new List<Int3>();

        int vaultCandidates = site.Parts.Count(p => p.Kind == "building" && p.Arg("vault") == "yes");
        foreach (var part in site.Parts)
        {
            switch (part.Kind)
            {
                case "building": BuildingPart(x, part, vaultCandidates == 0); break;
                case "runway": Strip(x, part, runway: true); break;
                case "road": Strip(x, part, runway: false); break;
                case "pad": Pad(x, part); break;
                case "tanks": Tanks(x, part); break;
                case "aircraft": Aircraft(x, part); break;
                case "containers": Containers(x, part); break;
                case "fence": Fence(x, part); break;
                case "tower": LatticeTower(x, part); break;
                case "crater": Crater(x, part); break;
                case "rocket": Rocket(x, part); break;
                case "poles": Poles(x, part); break;
                case "trench": Trench(x, part); break;
                case "vehicles": Vehicles(x, part); break;
                case "camp": Camp(x, part); break;
                case "stacks": Stacks(x, part); break;
                case "obelisks": Obelisks(x, part); break;
                case "hangar": Hangar(x, part); break;
                default: throw new ArgumentException($"site {site.Id}: unknown part '{part.Kind}'");
            }
        }
        c.VaultModule = x.VaultModule >= 0 ? x.VaultModule : x.DeepestModule >= 0 ? x.DeepestModule : x.Yard;
        // Levels for the whole site: the union of every building's storeys.
        c.X.Levels = c.X.Levels.GroupBy(l => l.Storey).Select(g => g.First()).OrderBy(l => l.Storey).ToList();
    }

    // ---- buildings --------------------------------------------------------------------------

    static void BuildingPart(Ctx x, SitePart part, bool anyMayHoldVault)
    {
        var spec = new BuildingSpec
        {
            Program = x.Lib.Programs[part.Arg("program")],
            X = part.X, Z = part.Z, Rot = part.Rot,
            W = part.Args.ContainsKey("w") ? part.Int("w") : null,
            D = part.Args.ContainsKey("d") ? part.Int("d") : null,
            Floors = part.Args.ContainsKey("floors") ? part.Int("floors") : null,
            Levels = part.Args.ContainsKey("levels") ? part.Int("levels") : null,
            Style = part.Args.ContainsKey("style") ? part.Arg("style") : null,
            HoldsVault = part.Arg("vault") == "yes" || (anyMayHoldVault && x.VaultModule < 0),
            MayHoldCore = part.Arg("core", "yes") == "yes",
            Name = part.Arg("name"),
        };
        var b = Building.Build(x.C, x.Lib, x.Fam, x.Rarity, spec, x.Rng.Fork("b" + part.X + "," + part.Z));
        x.C.Link(x.Yard, b.EntranceModule);
        if (spec.HoldsVault && b.VaultModule >= 0 && x.VaultModule < 0) x.VaultModule = b.VaultModule;
        if (b.DeepestModule >= 0 && (x.DeepestModule < 0 || x.C.Modules[b.DeepestModule].Min.Y < x.C.Modules[x.DeepestModule].Min.Y)) x.DeepestModule = b.DeepestModule;
        // The front step: open ground in front of the door, carved if the yard is paved.
        foreach (Int3 cell in b.ExitCells.Where(c => c.Y == 0))
            for (int d = 1; d <= 3; d++)
            {
                Int3 p = cell + b.EntranceFacing.Vec() * d;
                x.C.Remove(p);
                x.C.Remove(p + Int3.Up);
                x.C.Remove(p + Int3.Up * 2);
            }
    }

    // ---- ground works -------------------------------------------------------------------------

    static readonly Rgb Concrete = new(0x8a, 0x88, 0x82), Paint = new(0xe8, 0xe4, 0xd8), Hazard = new(0xe0, 0xa8, 0x00), Tar = new(0x3a, 0x3a, 0x3c);

    /// <summary>A strip of pavement in the ground: a cracked runway with markings, or a road.</summary>
    static void Strip(Ctx x, SitePart p, bool runway)
    {
        int len = p.Int("len", 100), width = p.Int("width", runway ? 14 : 6);
        bool alongX = p.Arg("axis", "x") == "x";
        float cracks = p.Num("cracks", 0.12f);
        for (int i = 0; i < len; i++)
            for (int j = 0; j < width; j++)
            {
                Int3 c = alongX ? new Int3(p.X + i, -1, p.Z + j) : new Int3(p.X + j, -1, p.Z + i);
                if (x.C.Blocks.ContainsKey(c)) continue;
                uint h = Hash.Cell(c, 44);
                bool crack = (h % 1000) / 1000f < cracks;
                if (crack)
                {
                    // Broken up: weeds growing through on bare ground.
                    if (h % 3 == 0) x.C.Blocks[c + Int3.Up] = new Block(BlockType.Plant, Material.Plant, new Rgb(0x5a, 0x6a, 0x30), Role.Debris);
                    continue;
                }
                Rgb col = (runway ? Concrete : Tar).Scale(0.9f + (h % 100) / 1000f);
                bool centre = j == width / 2 && i % 8 < 4;
                bool threshold = runway && (i < 4 || i >= len - 4) && j % 3 == 1;
                bool edge = runway && (j == 0 || j == width - 1);
                if (centre || threshold || edge) col = (centre || edge) && !runway ? Hazard : Paint;
                x.C.Set(c, new Block(BlockType.Cube, Material.Concrete, col, Role.Floor));
            }
        if (runway && p.Arg("lights") != "no")
            for (int i = 2; i < len; i += 10)
            {
                Int3 l = alongX ? new Int3(p.X + i, 0, p.Z - 1) : new Int3(p.X - 1, 0, p.Z + i);
                x.C.Blocks[l] = new Block(BlockType.Pillar, Material.Steel, new Rgb(0x50, 0x50, 0x50), Role.Decor);
            }
    }

    /// <summary>A concrete pad in the ground with a painted ring: landing and launch pads.</summary>
    static void Pad(Ctx x, SitePart p)
    {
        int r = p.Int("r", 10);
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d > r + 0.3f) continue;
                Int3 c = new(p.X + dx, -1, p.Z + dz);
                Rgb col = d > r - 1.2f || (d > r * 0.5f - 0.6f && d < r * 0.5f + 0.6f) ? Hazard : Concrete.Scale(0.85f + (Hash.Cell(c, 3) % 100) / 700f);
                x.C.Set(c, new Block(BlockType.Cube, Material.Concrete, col, Role.Floor));
            }
        x.C.X.Markers.Add(new Marker("landing-pad", new Int3(p.X, 0, p.Z)));
    }

    // ---- structures in the yard ---------------------------------------------------------------

    /// <summary>A cylinder of blocks standing on the ground (tanks, stacks, silos).</summary>
    static void Cylinder(Ctx x, int cx, int cz, float r, int y0, int h, Block b, bool hollow = false)
    {
        int ri = (int)MathF.Ceiling(r);
        for (int y = y0; y < y0 + h; y++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    float d = MathF.Sqrt(dx * dx + dz * dz);
                    if (d > r) continue;
                    if (hollow && d < r - 1f && y > y0 && y < y0 + h - 1) continue;
                    Int3 c = new(cx + dx, y, cz + dz);
                    var bb = b;
                    bb.Color = b.Color.Scale(0.92f + 0.12f * ((dx + dz + y) & 1));
                    x.C.Set(c, bb);
                }
    }

    /// <summary>Tank farm: round tanks with a ladder up each and rails round the top.</summary>
    static void Tanks(Ctx x, SitePart p)
    {
        int n = p.Int("count", 3), r = p.Int("r", 4), h = p.Int("h", 8), gap = p.Int("gap", 4);
        bool alongX = p.Arg("axis", "x") == "x";
        var steel = new Rgb(0xa8, 0xaa, 0xa4);
        for (int i = 0; i < n; i++)
        {
            int cx = p.X + (alongX ? i * (2 * r + gap) : 0), cz = p.Z + (alongX ? 0 : i * (2 * r + gap));
            var shell = new Block(BlockType.Cube, Material.Steel, i % 2 == 0 ? steel : steel.Lerp(new Rgb(0x8a, 0x5a, 0x3a), 0.4f), Role.Wall);
            Cylinder(x, cx, cz, r + 0.4f, 0, h, shell);
            // A hazard band.
            for (int dz = -r - 1; dz <= r + 1; dz++)
                for (int dx = -r - 1; dx <= r + 1; dx++)
                {
                    Int3 c = new(cx + dx, h / 2, cz + dz);
                    if (x.C.Get(c).Material == Material.Steel && MathF.Sqrt(dx * dx + dz * dz) > r - 0.6f)
                        x.C.Set(c, new Block(BlockType.Cube, Material.Steel, Hazard.Scale(0.85f), Role.Decor));
                }
            // Ladder up the south side, from the ground to the top.
            int lz = cz + r + 1;
            for (int y = 0; y < h; y++)
                x.C.Blocks[new Int3(cx, y, lz)] = new Block(BlockType.Ladder, Material.Steel, new Rgb(0x70, 0x70, 0x70), Role.Traverse, Dir.South);
            // Rails round the rim, open where the ladder arrives.
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    float d = MathF.Sqrt(dx * dx + dz * dz);
                    if (d > r + 0.4f || d < r - 0.6f) continue;
                    Int3 c = new(cx + dx, h, cz + dz);
                    if (Math.Abs(dx) <= 1 && dz > 0) continue;
                    x.C.Blocks[c] = new Block(BlockType.Rail, Material.Steel, new Rgb(0xd0, 0xa0, 0x20), Role.Decor);
                }
            var top = new List<Int3> { new(cx, h, cz), new(cx, h + 1, cz) };
            int m = x.C.AddModule(ModuleKind.Outdoor, "tank top", 0, top);
            x.C.Link(x.Yard, m);
        }
        x.C.X.Hazards.Add(new Hazard { Kind = "drop", Center = new Int3(p.X, h, p.Z), Radius = r, Strength = h, Warning = "No rails on the ladder side: a long drop." });
    }

    /// <summary>A crashed aircraft: a belly-down fuselage you can walk into, wings, one snapped off, a tail fin.</summary>
    static void Aircraft(Ctx x, SitePart p)
    {
        int len = p.Int("len", 18);
        bool alongX = p.Rot % 2 == 0;
        var hull = new Rgb(0xb8, 0xbc, 0xb4);
        var paint = x.Rng.Chance(0.5f) ? new Rgb(0x2a, 0x5a, 0x7a) : new Rgb(0x8a, 0x3a, 0x2a);
        Int3 W(int i, int y, int j) => alongX ? new Int3(p.X + i, y, p.Z + j) : new Int3(p.X + j, y, p.Z + i);
        var cabin = new List<Int3>();
        for (int i = 0; i < len; i++)
        {
            bool nose = i >= len - 3;
            for (int j = -2; j <= 2; j++)
                for (int y = -1; y <= 3; y++)
                {
                    Int3 c = W(i, y, j);
                    bool shell = y == -1 || y == 3 || Math.Abs(j) == 2;
                    bool open = i == 0 && y >= 0 && y <= 2 && Math.Abs(j) <= 1; // the tail broke away: the way in
                    if (nose && (Math.Abs(j) == 2 && y == 3)) continue;
                    if (shell && !open)
                    {
                        Rgb col = y == 1 && Math.Abs(j) == 2 && i % 3 == 1 ? new Rgb(0x30, 0x40, 0x48) : (y == 2 ? paint : hull);
                        if (nose && y >= 1) col = new Rgb(0x30, 0x40, 0x48);
                        Material mat = col == new Rgb(0x30, 0x40, 0x48) && y >= 1 ? Material.Glass : Material.Alloy;
                        var b = new Block(mat == Material.Glass ? BlockType.Pane : BlockType.Cube, mat, col.Scale(0.85f + 0.15f * ((i * 7 + y) % 3) / 2f), Role.Wall);
                        if (mat == Material.Glass) b = new Block(BlockType.Cube, Material.Alloy, col, Role.Window);
                        x.C.Set(c, b);
                    }
                    else if (!shell) { x.C.Clear(c); if (y == 0) { } cabin.Add(c); }
                }
        }
        // Wings at shoulder height, one torn off and lying beside the hull.
        for (int i = len / 3; i < len / 3 + 4; i++)
            for (int j = 3; j <= 8; j++)
            {
                x.C.Set(W(i, 1, j), new Block(BlockType.Cube, Material.Alloy, hull.Scale(0.9f), Role.Wall));
                Int3 g = W(i + 1, 0, -j - 2);
                if (j < 7) x.C.Set(g, new Block(BlockType.Cube, Material.Alloy, hull.Scale(0.8f).Lerp(Tar, 0.3f), Role.Debris));
            }
        // Tail fin, scorched.
        for (int y = 4; y <= 6; y++)
            for (int i = 1; i <= 4 - (y - 4); i++)
                x.C.Set(W(i, y, 0), new Block(BlockType.Cube, Material.Alloy, paint.Lerp(Tar, 0.4f), Role.Wall));
        // Scorch along the hull, and the furrow it ploughed.
        for (int i = -10; i < 0; i++)
            for (int j = -1; j <= 1; j++)
            {
                Int3 c = W(i, 0, j);
                if (!x.C.Blocks.ContainsKey(c)) x.C.Blocks[c] = new Block(BlockType.Scorch, Material.Ash, new Rgb(0x2a, 0x22, 0x1e), Role.Debris);
            }
        int m = x.C.AddModule(ModuleKind.Room, "aircraft cabin", 0, cabin.Where(c => c.Y >= 0).ToList());
        x.C.Link(x.Yard, m);
        var floor = cabin.Where(c => c.Y == 0).OrderBy(c => c.Manhattan(W(len - 4, 0, 0))).ToList();
        x.C.AddSlot(SlotKind.Loot, floor[0], m);
        x.C.Set(floor[3], new Block(BlockType.Remains, Material.Cloth, new Rgb(0x6a, 0x6a, 0x5a), Role.Story));
        x.C.X.Story.Add(new StoryProp("crash", floor[3], "The crew stayed with their aircraft."));
    }

    /// <summary>Shipping containers in rows, some stacked, some open with something left inside.</summary>
    static void Containers(Ctx x, SitePart p)
    {
        int rows = p.Int("rows", 3), cols = p.Int("cols", 4);
        var colours = new[] { new Rgb(0x8a, 0x3a, 0x2a), new Rgb(0x2a, 0x5a, 0x7a), new Rgb(0x6a, 0x7a, 0x3a), new Rgb(0xb0, 0x80, 0x30), new Rgb(0x5a, 0x5a, 0x60) };
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int ox = p.X + c * 9, oz = p.Z + r * 6;
                int high = x.Rng.Chance(0.45f) ? 2 : 1;
                for (int layer = 0; layer < high; layer++)
                {
                    var col = colours[(int)(Hash.Cell(new Int3(ox, layer, oz), 5) % colours.Length)];
                    bool open = layer == 0 && x.Rng.Chance(0.3f);
                    var inside = new List<Int3>();
                    for (int i = 0; i < 7; i++)
                        for (int j = 0; j < 4; j++)
                            for (int y = 0; y < 3; y++)
                            {
                                Int3 cell = new(ox + i, layer * 3 + y, oz + j);
                                bool shell = i == 0 || i == 6 || j == 0 || j == 3 || y == 2 || (y == 0 && layer > 0);
                                if (open && i == 0 && j is 1 or 2 && y < 2) shell = false;
                                if (!shell && layer == 0 && y < 2) { if (open) inside.Add(cell); else x.C.Set(cell, new Block(BlockType.Cube, Material.Rust, col.Scale(0.6f), Role.Wall)); continue; }
                                if (!shell) { x.C.Set(cell, new Block(BlockType.Cube, Material.Rust, col.Scale(0.6f), Role.Wall)); continue; }
                                x.C.Set(cell, new Block(BlockType.Cube, Material.Rust, col.Scale(0.85f + 0.15f * ((i + y) % 2)), Role.Wall));
                            }
                    if (open && inside.Count > 0)
                    {
                        foreach (Int3 cell in inside) x.C.Clear(cell);
                        int m = x.C.AddModule(ModuleKind.Room, "container", 0, inside);
                        x.C.Link(x.Yard, m);
                        x.C.AddSlot(SlotKind.Loot, inside.Where(q => q.Y == 0).OrderByDescending(q => q.X).First(), m);
                    }
                }
            }
    }

    /// <summary>A fence line of mesh panels on posts, with gaps (gates and breaches).</summary>
    static void Fence(Ctx x, SitePart p)
    {
        int len = p.Int("len", 40), h = p.Int("h", 2);
        bool alongX = p.Arg("axis", "x") == "x";
        int gapAt = p.Int("gate", len / 2);
        float breaches = p.Num("breaches", 0.08f);
        bool wall = p.Arg("type", "mesh") == "wall";
        for (int i = 0; i < len; i++)
        {
            if (Math.Abs(i - gapAt) <= 2) continue;
            Int3 b = alongX ? new Int3(p.X + i, 0, p.Z) : new Int3(p.X, 0, p.Z + i);
            if ((Hash.Cell(b, 61) % 1000) / 1000f < breaches) continue;
            for (int y = 0; y < h; y++)
            {
                Int3 c = b + Int3.Up * y;
                if (x.C.Blocks.ContainsKey(c)) continue;
                x.C.Blocks[c] = wall
                    ? new Block(BlockType.Cube, Material.Concrete, Concrete.Scale(0.8f + 0.1f * (y % 2)), Role.Wall)
                    : i % 4 == 0 ? new Block(BlockType.Pillar, Material.Steel, new Rgb(0x50, 0x52, 0x54), Role.Wall)
                    : new Block(BlockType.Lattice, Material.Steel, new Rgb(0x6a, 0x6e, 0x70), Role.Wall);
            }
        }
    }

    /// <summary>
    /// A lattice tower: four legs, a platform every few metres with a ladder between, and a crown
    /// (a winding wheel over a mine shaft, a lamp on a comms mast, a hut on a guard tower).
    /// </summary>
    static void LatticeTower(Ctx x, SitePart p)
    {
        int h = p.Int("h", 16), size = p.Int("size", 5), every = p.Int("every", 4);
        h = (h + every - 1) / every * every; // the top deck is a platform, so the crown has something to stand on
        string crown = p.Arg("crown", "lamp");
        var steel = new Rgb(0x5a, 0x4a, 0x40);
        int s = size - 1;
        var platforms = new List<Int3>();
        for (int y = 0; y < h; y++)
        {
            foreach (var (dx, dz) in new[] { (0, 0), (s, 0), (0, s), (s, s) })
                x.C.Set(new Int3(p.X + dx, y, p.Z + dz), new Block(BlockType.Lattice, Material.Steel, steel.Scale(0.9f + 0.1f * (y % 2)), Role.Wall));
            if (y % every == every - 1)
            {
                for (int dz = 0; dz <= s; dz++)
                    for (int dx = 0; dx <= s; dx++)
                    {
                        Int3 c = new(p.X + dx, y, p.Z + dz);
                        if (dx == 1 && dz == 1) continue; // the ladder's hatch
                        x.C.Set(c, new Block(BlockType.Lattice, Material.Steel, steel, Role.Floor));
                    }
                platforms.Add(new Int3(p.X + 2, y + 1, p.Z + 1));
                // Rails round the platform edge.
                for (int dz = 0; dz <= s; dz++)
                    for (int dx = 0; dx <= s; dx++)
                    {
                        bool edge = dx == 0 || dz == 0 || dx == s || dz == s;
                        bool corner = (dx == 0 || dx == s) && (dz == 0 || dz == s);
                        if (edge && !corner) x.C.Blocks[new Int3(p.X + dx, y + 1, p.Z + dz)] = new Block(BlockType.Rail, Material.Steel, new Rgb(0xb0, 0x30, 0x20), Role.Decor);
                    }
            }
        }
        // The ladder: up through every hatch.
        int top = platforms.Count > 0 ? platforms[^1].Y - 1 : h - 1;
        for (int y = 0; y <= top; y++)
            x.C.Blocks[new Int3(p.X + 1, y, p.Z + 1)] = new Block(BlockType.Ladder, Material.Steel, new Rgb(0x70, 0x70, 0x70), Role.Traverse, Dir.North);
        // The ladder's foot is on the ground, inside the legs: clear the way to it.
        Int3 crownAt = new(p.X + s - 1, h, p.Z + s - 1); // on the top deck, clear of the ladder hatch
        switch (crown)
        {
            case "wheel":
                // A winding wheel over the shaft, edge-on to the yard.
                for (int a = 0; a < 16; a++)
                {
                    float t = a / 16f * MathF.Tau;
                    Int3 c = crownAt + new Int3((int)MathF.Round(MathF.Cos(t) * 2.4f), 2 + (int)MathF.Round(MathF.Sin(t) * 2.4f), 0);
                    x.C.Set(c, new Block(BlockType.Cube, Material.Rust, new Rgb(0x7a, 0x4a, 0x30), Role.Decor));
                }
                for (int y = 0; y < 2; y++) x.C.Set(crownAt + Int3.Up * y, new Block(BlockType.Pillar, Material.Steel, steel, Role.Decor));
                break;
            case "hut":
                for (int dz = 0; dz <= s; dz++)
                    for (int dx = 0; dx <= s; dx++)
                    {
                        x.C.Set(new Int3(p.X + dx, h + 2, p.Z + dz), new Block(BlockType.Cube, Material.Scrap, new Rgb(0x6a, 0x5a, 0x48), Role.Roof));
                        if ((dx == 0 || dx == s) && (dz == 0 || dz == s))
                            for (int y = 0; y < 2; y++) x.C.Set(new Int3(p.X + dx, h + y, p.Z + dz), new Block(BlockType.Pillar, Material.Timber, new Rgb(0x6a, 0x4e, 0x36), Role.Wall));
                    }
                break;
            default:
                for (int y = 0; y < 5; y++) x.C.Set(crownAt + Int3.Up * y, new Block(BlockType.Pillar, Material.Steel, steel, Role.Decor));
                x.C.Set(crownAt + Int3.Up * 5, new Block(BlockType.Cube, Material.Lumen, new Rgb(0xff, 0x40, 0x30), Role.Light));
                x.C.X.Lights.Add(new LightSpot { Cell = crownAt + Int3.Up * 5, Color = new Rgb(0xff, 0x40, 0x30), Mode = LightMode.Beacon, Energy = 2f, Range = 16f });
                break;
        }
        foreach (Int3 pl in platforms)
        {
            int m = x.C.AddModule(ModuleKind.Outdoor, "tower platform", 0, new List<Int3> { pl, pl + Int3.Up });
            x.C.Link(x.Yard, m);
        }
        if (p.Arg("loot") == "yes" && platforms.Count > 0)
        {
            Int3 top2 = platforms[^1] + new Int3(1, 0, 1);
            x.C.AddSlot(SlotKind.Loot, top2, x.C.Modules.Count - 1);
        }
    }

    /// <summary>
    /// A blast crater: a bowl punched into the ground, lined with fused glass, a raised rim of
    /// thrown rubble, and a radiation pocket at the bottom, warned by signs on the rim.
    /// </summary>
    static void Crater(Ctx x, SitePart p)
    {
        // Gaps in the thrown rim along the four axes: where the ramped paths come up out of the bowl.
        static bool RimGap(float a)
        {
            for (int k = 0; k < 4; k++)
                if (MathF.Abs(MathF.IEEERemainder(a - k * MathF.PI / 2f, MathF.Tau)) < 0.16f) return true;
            return false;
        }
        float r = p.Num("r", 18f);
        int depth = p.Int("depth", 7);
        float rad = p.Num("radiation", 12f);
        int ri = (int)MathF.Ceiling(r + 4);
        var glass = new Rgb(0x3a, 0x5a, 0x48);
        var bowl = new List<Int3>();
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                float d = MathF.Sqrt(dx * dx + dz * dz);
                int cx = p.X + dx, cz = p.Z + dz;
                if (d <= r)
                {
                    // A smooth bowl in whole steps: the floor is a gentle terrace you can walk down.
                    float t = d / r;
                    int floorY = -1 - (int)MathF.Round(depth * (1f - t * t));
                    for (int y = floorY + 1; y <= -1; y++)
                    {
                        Int3 c = new(cx, y, cz);
                        x.C.Clear(c);
                        bowl.Add(c);
                    }
                    Int3 f = new(cx, floorY, cz);
                    var h = Hash.Cell(f, 77);
                    x.C.Set(f, new Block(BlockType.Cube, Material.Fused, glass.Scale(0.8f + (h % 100) / 400f).Lerp(new Rgb(0x60, 0x80, 0x50), (h % 7) / 14f), Role.Floor));
                }
                else if (d <= r + 4 && !RimGap(MathF.Atan2(dz, dx)))
                {
                    // The rim: thrown rubble, highest just outside the bowl.
                    float t = (d - r) / 4f;
                    int hgt = (int)MathF.Round(2f * (1f - t));
                    for (int y = 0; y < hgt; y++)
                        x.C.Set(new Int3(cx, y, cz), new Block(BlockType.Cube, Material.Rock, new Rgb(0x5a, 0x50, 0x48).Scale(0.85f + 0.1f * (y % 2)), Role.Debris));
                }
            }
        // Three ramped paths from the rim gaps straight down to the middle, along the axes.
        var axes = new List<(int ux, int uz)> { (1, 0), (0, -1), (-1, 0), (0, 1) };
        axes.RemoveAt(x.Rng.Range(0, 4));
        int FloorAt(int dx, int dz)
        {
            float d = MathF.Sqrt(dx * dx + dz * dz);
            if (d > r) return -1;
            float t = d / r;
            return -1 - (int)MathF.Round(depth * (1f - t * t));
        }
        foreach (var (ux, uz) in axes)
        {
            Dir outward = ux > 0 ? Dir.East : ux < 0 ? Dir.West : uz > 0 ? Dir.South : Dir.North;
            int lx = uz != 0 ? 1 : 0, lz = ux != 0 ? 1 : 0;
            for (int lane = 0; lane < 2; lane++)
                for (int t = 0; t <= ri; t++)
                {
                    int dx = ux * t + lx * lane, dz = uz * t + lz * lane;
                    int f0 = FloorAt(dx, dz), f1 = FloorAt(dx + ux, dz + uz);
                    if (f1 != f0 + 1) continue;
                    // The floor steps up one going outward: a fused ramp on this column rising toward it.
                    Int3 c = new(p.X + dx, f0 + 1, p.Z + dz);
                    x.C.Set(c, new Block(BlockType.Ramp, Material.Fused, glass.Scale(0.9f), Role.Floor, outward));
                }
        }
        var cells = bowl.Where(c => !x.C.Blocks.ContainsKey(c)).ToList();
        if (cells.Count > 0)
        {
            int m = x.C.AddModule(ModuleKind.Outdoor, "crater", -1, cells);
            x.C.Link(x.Yard, m);
            var bottom = cells.OrderBy(c => c.Y).ThenBy(c => Math.Abs(c.X - p.X) + Math.Abs(c.Z - p.Z)).First();
            if (p.Arg("loot") != "no") x.C.AddSlot(SlotKind.Loot, bottom, m);
            x.C.X.Story.Add(new StoryProp("blast crater", bottom, "Glass where the ground was. The middle still ticks on a counter."));
        }
        var hz = new Hazard { Kind = "radiation", Center = new Int3(p.X, -depth, p.Z), Radius = r * 0.6f, Strength = rad, Warning = "RADIATION: the crater floor is hot." };
        for (int a = 0; a < 6; a++)
        {
            float t = a / 6f * MathF.Tau;
            Int3 sign = new(p.X + (int)MathF.Round(MathF.Cos(t) * (r + 5)), 0, p.Z + (int)MathF.Round(MathF.Sin(t) * (r + 5)));
            if (x.C.Blocks.ContainsKey(sign)) continue;
            x.C.Blocks[sign] = new Block(BlockType.Pillar, Material.Steel, new Rgb(0x50, 0x50, 0x50), Role.Hazard);
            x.C.Blocks[sign + Int3.Up] = new Block(BlockType.Sign, Material.Steel, Hazard, Role.Hazard, Dir.North, 1);
            hz.Signs.Add(sign + Int3.Up);
        }
        x.C.X.Hazards.Add(hz);
        x.C.X.Sounds.Add(new SoundHook("geiger", hz.Center + Int3.Up, 0.8f, -1));
    }

    /// <summary>
    /// A derelict rocket still standing on its launch pad: a hull of decks joined by a ladder
    /// through every hatch, a hatch at the foot, fins and engine bells, and the service gantry
    /// beside it with a ladder and a walkway to an upper hatch.
    /// </summary>
    static void Rocket(Ctx x, SitePart p)
    {
        int decks = p.Int("decks", 6), r = p.Int("r", 4);
        int h = decks * 4 + 1;
        var hull = new Rgb(0xd8, 0xd6, 0xcc);
        var band = new Rgb(0x2a, 0x4a, 0x6a);
        var bell = new Rgb(0x3a, 0x34, 0x30);
        int baseY = 2; // the hull stands on its engine skirt
        // Engine skirt and bells: a ring of legs, open beneath so you can walk under the engines.
        for (int y = 0; y < baseY; y++)
            for (int a = 0; a < 8; a++)
            {
                float t = a / 8f * MathF.Tau;
                Int3 c = new(p.X + (int)MathF.Round(MathF.Cos(t) * (r - 0.5f)), y, p.Z + (int)MathF.Round(MathF.Sin(t) * (r - 0.5f)));
                x.C.Set(c, new Block(BlockType.Cube, Material.Steel, bell, Role.Wall));
            }
        // Hull: decks every four metres, a ladder through the middle.
        var deckCells = new List<List<Int3>>();
        for (int d = 0; d < decks; d++)
        {
            int y0 = baseY + d * 4; // the deck's floor slab
            var cells = new List<Int3>();
            for (int y = y0; y <= y0 + 4; y++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        float dist = MathF.Sqrt(dx * dx + dz * dz);
                        if (dist > r + 0.3f) continue;
                        Int3 c = new(p.X + dx, y, p.Z + dz);
                        bool wall = dist > r - 0.7f;
                        bool slab = y == y0 || y == y0 + 4;
                        if (wall || slab)
                        {
                            bool window = wall && y == y0 + 2 && (dx == 0 || dz == 0) && d >= decks - 2;
                            Rgb col = (d % 3 == 1 && wall) ? band : hull.Scale(0.92f + 0.08f * ((dx + dz + y) & 1));
                            x.C.Set(c, window ? new Block(BlockType.Cube, Material.Alloy, new Rgb(0x30, 0x40, 0x48), Role.Window) : new Block(BlockType.Cube, Material.Alloy, col, slab ? Role.Floor : Role.Wall));
                        }
                        else if (y > y0 && y < y0 + 4)
                        {
                            x.C.Clear(c);
                            if (y <= y0 + 3) cells.Add(c);
                        }
                    }
            deckCells.Add(cells);
        }
        // The nose cone.
        for (int k = 0; k < r + 2; k++)
        {
            float rr = r * (1f - k / (float)(r + 2));
            Cylinder(x, p.X, p.Z, rr, baseY + decks * 4 + 1 + k, 1, new Block(BlockType.Cube, Material.Alloy, hull, Role.Roof));
        }
        // Fins at the foot.
        // Three fins; the fourth was torn off when the access stair went up on the hatch side.
        foreach (var (fx, fz) in new[] { (1, 0), (0, 1), (0, -1) })
            for (int y = 0; y < 7; y++)
                for (int k = 1; k <= 3 - y / 3; k++)
                    x.C.Set(new Int3(p.X + fx * (r + k), y, p.Z + fz * (r + k)), new Block(BlockType.Cube, Material.Alloy, band, Role.Wall));
        // The ladder up the middle, through every deck's hatch.
        for (int y = baseY + 1; y < baseY + (decks - 1) * 4 + 1; y++)
        {
            Int3 c = new(p.X, y, p.Z);
            x.C.Set(c, new Block(BlockType.Ladder, Material.Steel, new Rgb(0x60, 0x60, 0x60), Role.Traverse, Dir.North));
        }
        // The way in: a hatch at the foot of the lowest deck, reached up a short stair of the pad.
        int doorY = baseY + 1;
        Int3 door = new(p.X - r, doorY, p.Z);
        var doorCells = new List<Int3>();
        for (int k = 0; k < 3; k++)
            for (int dz = 0; dz <= 1; dz++) doorCells.Add(new Int3(p.X - r, doorY + k, p.Z + dz));
        foreach (Int3 dc in doorCells) x.C.Clear(dc);
        // Steps up to the hatch from the pad: two stair blocks and their fill.
        for (int dz = 0; dz <= 1; dz++)
        {
            // Three steps up from the pad to a landing at the hatch sill (walk level 3), solid under each.
            for (int k = 0; k < 3; k++) x.C.Set(new Int3(p.X - r - 1, k, p.Z + dz), new Block(BlockType.Cube, Material.Steel, bell, Role.Floor));
            for (int k = 3; k <= 5; k++) x.C.Remove(new Int3(p.X - r - 1, k, p.Z + dz));
            for (int i = 0; i < 3; i++)
            {
                int sx = p.X - r - 4 + i;
                for (int k = 0; k < i; k++) x.C.Set(new Int3(sx, k, p.Z + dz), new Block(BlockType.Cube, Material.Steel, bell, Role.Floor));
                x.C.Set(new Int3(sx, i, p.Z + dz), new Block(BlockType.Stair, Material.Steel, bell.Scale(1.2f), Role.Traverse, Dir.East));
                for (int k = 1; k <= 3; k++) x.C.Remove(new Int3(sx, i + k, p.Z + dz));
            }
        }
        int prev = -1;
        for (int d = 0; d < decks; d++)
        {
            var cells = deckCells[d];
            if (cells.Count == 0) continue;
            string purpose = d == 0 ? "airlock deck" : d == decks - 1 ? "flight deck" : d == decks - 2 ? "crew deck" : "cargo deck";
            int m = x.C.AddModule(d == decks - 1 ? ModuleKind.Vault : ModuleKind.Room, purpose, 0, cells);
            if (prev >= 0) x.C.Link(prev, m);
            else
            {
                x.C.Link(x.Yard, m);
                x.C.Doorways.Add(new Doorway { Cells = doorCells, Facing = Dir.East, ModuleA = x.Yard, ModuleB = m });
            }
            prev = m;
            var floor = cells.Where(c => c.Y == baseY + d * 4 + 1 && Math.Abs(c.X - p.X) + Math.Abs(c.Z - p.Z) >= 2).OrderBy(c => Hash.Cell(c, (uint)d)).ToList();
            if (floor.Count > 2)
            {
                x.C.AddSlot(SlotKind.Loot, floor[0], m);
                x.C.AddSlot(SlotKind.Spawn, floor[1], m);
            }
            x.C.X.Lights.Add(new LightSpot { Cell = new Int3(p.X + 1, baseY + d * 4 + 3, p.Z + 1), Color = new Rgb(0xe6, 0xf2, 0xff), Mode = LightMode.Steady, Energy = 1f, Range = 7f, Module = m });
            if (d == decks - 1 && x.VaultModule < 0) x.VaultModule = m;
        }
        // The gantry: a lattice tower beside the hull, its top level with the flight deck's hatch.
        int gx = p.X + r + 4, gz = p.Z - 2;
        int gh = baseY + (decks - 1) * 4 + 1;
        var gantry = new SitePart { Kind = "tower", X = gx, Z = gz, Args = { ["h"] = (gh).ToString(), ["size"] = "5", ["every"] = "4", ["crown"] = "lamp" } };
        LatticeTower(x, gantry);
        x.C.X.Markers.Add(new Marker("derelict-ship", new Int3(p.X, baseY, p.Z), "restorable: fit a core"));
        x.C.X.Story.Add(new StoryProp("never launched", door, "Fuelled, crewed, and never cleared to go."));
        if (x.DeepestModule < 0) x.DeepestModule = prev;
    }

    static void Poles(Ctx x, SitePart p)
    {
        int n = p.Int("count", 4), gap = p.Int("gap", 12), h = p.Int("h", 7);
        bool alongX = p.Arg("axis", "x") == "x";
        for (int i = 0; i < n; i++)
        {
            Int3 b = alongX ? new Int3(p.X + i * gap, 0, p.Z) : new Int3(p.X, 0, p.Z + i * gap);
            for (int y = 0; y < h; y++) x.C.Blocks[b + Int3.Up * y] = new Block(BlockType.Pillar, Material.Steel, new Rgb(0x58, 0x5a, 0x5c), Role.Decor);
            bool lit = x.Rng.Chance(0.35f);
            x.C.Blocks[b + Int3.Up * h] = new Block(BlockType.Cube, lit ? Material.Lumen : Material.DeadLumen, lit ? new Rgb(0xff, 0xc0, 0x70) : new Rgb(0x40, 0x3a, 0x30), Role.Light);
            if (lit) x.C.X.Lights.Add(new LightSpot { Cell = b + Int3.Up * (h - 1), Color = new Rgb(0xff, 0xc0, 0x70), Mode = LightMode.Flicker, Energy = 1.6f, Range = 12f });
        }
    }

    /// <summary>A trench line: a channel two deep with ramps in and out, sandbags along its lip.</summary>
    static void Trench(Ctx x, SitePart p)
    {
        int len = p.Int("len", 30);
        bool alongX = p.Arg("axis", "x") == "x";
        Int3 W(int i, int y, int j) => alongX ? new Int3(p.X + i, y, p.Z + j) : new Int3(p.X + j, y, p.Z + i);
        Dir along = alongX ? Dir.East : Dir.South;
        var cells = new List<Int3>();
        for (int i = 0; i < len; i++)
            for (int j = 0; j < 2; j++)
            {
                for (int y = -2; y <= -1; y++)
                {
                    Int3 c = W(i, y, j);
                    x.C.Clear(c);
                    cells.Add(c);
                }
                x.C.Set(W(i, -3, j), new Block(BlockType.Cube, Material.Timber, new Rgb(0x5a, 0x44, 0x30), Role.Floor));
                Int3 bag = W(i, 0, j == 0 ? -1 : 2);
                if (i % 5 != 2) x.C.Blocks[bag] = new Block(BlockType.Barricade, Material.Cloth, new Rgb(0x8a, 0x7a, 0x58), Role.Wall, alongX ? Dir.North : Dir.East);
            }
        // Two-step ramps out at both ends: from the trench floor up to the ground.
        var soil = new Rgb(0x5a, 0x48, 0x34);
        foreach (var (i, outward, step) in new[] { (0, along.Opposite(), -1), (len - 1, along, 1) })
            for (int j = 0; j < 2; j++)
            {
                x.C.Set(W(i, -2, j), new Block(BlockType.Ramp, Material.Soil, soil, Role.Floor, outward));
                x.C.Set(W(i + step, -1, j), new Block(BlockType.Ramp, Material.Soil, soil, Role.Floor, outward));
                x.C.Set(W(i + step, -2, j), new Block(BlockType.Cube, Material.Soil, soil, Role.Floor));
                x.C.Set(W(i + step, -3, j), new Block(BlockType.Cube, Material.Soil, soil, Role.Floor));
            }
        var walk = cells.Where(c => c.Y == -2 && !x.C.Blocks.ContainsKey(c)).ToList();
        if (walk.Count == 0) return;
        int m = x.C.AddModule(ModuleKind.Outdoor, "trench", -1, cells.Where(c => !x.C.Blocks.ContainsKey(c)).ToList());
        x.C.Link(x.Yard, m);
        x.C.AddSlot(SlotKind.Loot, walk[walk.Count / 2], m);
    }

    /// <summary>Wrecked vehicles: boxy hulks with their cabs, rusting where they stopped.</summary>
    static void Vehicles(Ctx x, SitePart p)
    {
        int n = p.Int("count", 3), gap = p.Int("gap", 8);
        bool alongX = p.Arg("axis", "x") == "x";
        for (int i = 0; i < n; i++)
        {
            int ox = p.X + (alongX ? i * gap : 0), oz = p.Z + (alongX ? 0 : i * gap);
            var col = new[] { new Rgb(0x6a, 0x5a, 0x3a), new Rgb(0x4a, 0x5a, 0x4a), new Rgb(0x7a, 0x3a, 0x2a) }[i % 3].Lerp(new Rgb(0x5a, 0x40, 0x30), 0.3f);
            bool big = x.Rng.Chance(0.4f);
            int len = big ? 6 : 4, wid = 3, hgt = big ? 3 : 2;
            for (int a = 0; a < len; a++)
                for (int b = 0; b < wid; b++)
                    for (int y = 0; y < hgt; y++)
                    {
                        bool cab = a >= len - 2;
                        if (!cab && y == hgt - 1 && big && b == 1) continue;
                        if (y == hgt - 1 && cab && !big) continue;
                        Int3 c = alongX ? new Int3(ox + b, y, oz + a) : new Int3(ox + a, y, oz + b);
                        x.C.Set(c, new Block(BlockType.Cube, Material.Rust, col.Scale(0.8f + 0.2f * ((a + y) % 2)), Role.Debris));
                    }
        }
    }

    /// <summary>A camp: bedrolls, a burnt-out fire, crates, a barricade line. Someone lived here after.</summary>
    static void Camp(Ctx x, SitePart p)
    {
        Int3 fire = new(p.X, 0, p.Z);
        x.C.Blocks[fire] = new Block(BlockType.Ash, Material.Ash, new Rgb(0x30, 0x2a, 0x26), Role.Story);
        for (int a = 0; a < 5; a++)
        {
            float t = a / 5f * MathF.Tau;
            Int3 c = fire + new Int3((int)MathF.Round(MathF.Cos(t) * 3), 0, (int)MathF.Round(MathF.Sin(t) * 3));
            if (x.C.Blocks.ContainsKey(c)) continue;
            x.C.Blocks[c] = a % 2 == 0 ? new Block(BlockType.Scatter, Material.Cloth, new Rgb(0x6a, 0x5e, 0x48), Role.Story) : new Block(BlockType.Crate, Material.Scrap, new Rgb(0x6e, 0x5a, 0x40), Role.Prop);
        }
        var cells = new List<Int3> { fire + new Int3(1, 0, 1), fire + new Int3(1, 1, 1) };
        int m = x.C.AddModule(ModuleKind.Outdoor, "camp", 0, cells);
        x.C.Link(x.Yard, m);
        x.C.AddSlot(SlotKind.Loot, fire + new Int3(-2, 0, 2), m);
        x.C.X.Story.Add(new StoryProp("camp", fire, "A cold fire and bedrolls. Whoever camped here left in a hurry."));
        if (p.Arg("gang") == "yes")
        {
            x.C.X.Markers.Add(new Marker("gang-spawn", fire + new Int3(2, 0, -2), "raiders", 1));
            x.C.X.Markers.Add(new Marker("vehicle", fire + new Int3(-5, 0, 0), "buggy", 1));
        }
    }

    /// <summary>
    /// A hangar: a tall shed with a stepped gable roof and its great door rolled half open on the
    /// apron side, a mezzanine along the back wall up a ladder with the foreman's office on it, and
    /// whatever was being worked on when everyone left still on the floor.
    /// </summary>
    static void Hangar(Ctx x, SitePart p)
    {
        int w = p.Int("w", 26), d = p.Int("d", 18), h = p.Int("h", 8);
        var wall = new Rgb(0x8a, 0x8c, 0x86);
        var roof = new Rgb(0x6a, 0x5a, 0x4a);
        var concrete = new Rgb(0x5a, 0x58, 0x54);
        Int3 W(int i, int y, int j) => new(p.X + i, y, p.Z + j);   // i across the front (x), j back (z)
        int doorLo = w / 2 - 6, doorHi = w / 2 + 5, doorH = 6;
        var hall = new List<Int3>();
        for (int j = 0; j < d; j++)
            for (int i = 0; i < w; i++)
            {
                x.C.Set(W(i, -1, j), new Block(BlockType.Cube, Material.Concrete, concrete.Scale(0.9f + 0.1f * ((i + j) % 2)), Role.Floor));
                bool edge = i == 0 || i == w - 1 || j == 0 || j == d - 1;
                for (int y = 0; y < h; y++)
                {
                    Int3 c = W(i, y, j);
                    bool door = j == 0 && i >= doorLo && i <= doorHi && y < doorH;
                    if (edge && !door)
                    {
                        bool band = y == h - 2;
                        bool rib = i % 4 == 0 && j > 0;
                        x.C.Set(c, new Block(BlockType.Cube, Material.Steel, band ? new Rgb(0x6a, 0x7a, 0x5a) : rib ? wall.Scale(0.85f) : wall, Role.Wall));
                    }
                    else
                    {
                        x.C.Clear(c);
                        if (y < 3 && !edge) hall.Add(c);
                    }
                }
            }
        // The stepped gable along the hangar's length: each course steps in from both long sides.
        for (int k = 0; k <= d / 2; k++)
            for (int i = 0; i < w; i++)
            {
                int y = h + k;
                foreach (int j in new[] { k, d - 1 - k })
                    x.C.Set(W(i, y, j), new Block(BlockType.Cube, Material.Rust, roof.Scale(0.85f + 0.15f * (k % 2)), Role.Roof));
                for (int j = k + 1; j < d - 1 - k; j++)
                {
                    if (i == 0 || i == w - 1) x.C.Set(W(i, y, j), new Block(BlockType.Cube, Material.Steel, wall, Role.Wall));
                    else x.C.Clear(W(i, y, j));
                }
            }
        // The great door: one leaf rolled half open, hanging above the gap.
        for (int i = doorLo; i <= doorHi; i++)
            for (int y = doorH; y < h; y++) x.C.Set(W(i, y, 0), new Block(BlockType.Cube, Material.Steel, new Rgb(0x7a, 0x6a, 0x3a), Role.Wall));
        int floorModule = x.C.AddModule(ModuleKind.Hall, "hangar floor", 0, hall);
        x.C.Link(x.Yard, floorModule);
        // The mezzanine along the back wall, up a ladder, with the office on it.
        int mz = d - 4, my = 3;
        var mezz = new List<Int3>();
        for (int i = 1; i < w - 1; i++)
            for (int j = mz; j < d - 1; j++)
            {
                x.C.Set(W(i, my, j), new Block(BlockType.Cube, Material.Steel, new Rgb(0x50, 0x54, 0x58), Role.Floor));
                if (j == mz && i != 2) x.C.Blocks[W(i, my + 1, j)] = new Block(BlockType.Rail, Material.Steel, new Rgb(0xd0, 0xa0, 0x20), Role.Decor);
                else if (i >= 2 && i < w - 10) { mezz.Add(W(i, my + 1, j)); mezz.Add(W(i, my + 2, j)); }
            }
        // The ladder up the front of the mezzanine: its top rung at the walkway's floor, you step on at the gap in the rail.
        for (int y = 0; y <= my; y++) x.C.Blocks[W(2, y, mz - 1)] = new Block(BlockType.Ladder, Material.Steel, new Rgb(0x70, 0x70, 0x70), Role.Traverse, Dir.North);
        // The office: a box on the mezzanine's far end with a window over the floor.
        var office = new List<Int3>();
        int o0 = w - 9;
        for (int i = o0; i < w - 1; i++)
            for (int j = mz; j < d - 1; j++)
                for (int y = my + 1; y <= my + 3; y++)
                {
                    bool shell = i == o0 || j == mz || y == my + 3;
                    bool door = i == o0 && (j == mz + 1 || j == mz + 2) && y <= my + 2;
                    bool glass = j == mz && y == my + 2 && i > o0 && i < w - 2;
                    Int3 c = W(i, y, j);
                    if (door) { x.C.Clear(c); continue; }
                    if (glass) { x.C.Set(c, new Block(BlockType.Pane, Material.Glass, new Rgb(0x8a, 0xa4, 0xa8), Role.Window)); continue; }
                    if (shell) { x.C.Set(c, new Block(BlockType.Cube, Material.Steel, wall.Scale(0.9f), Role.Wall)); continue; }
                    x.C.Clear(c);
                    if (y <= my + 2) office.Add(c);
                }
        int mezzModule = x.C.AddModule(ModuleKind.Outdoor, "mezzanine", 0, mezz.Where(c => !x.C.Blocks.ContainsKey(c)).ToList());
        int officeModule = x.C.AddModule(ModuleKind.Room, "foreman's office", 0, office);
        x.C.Link(floorModule, mezzModule);
        x.C.Link(mezzModule, officeModule);
        var officeFloor = office.Where(c => c.Y == my + 1).OrderByDescending(c => c.X + c.Z).ToList();
        x.C.AddSlot(SlotKind.Loot, officeFloor[0], officeModule);
        // On the floor: what was being worked on, stopped mid-job.
        var floor = hall.Where(c => c.Y == 0 && c.Z - p.Z > 2 && c.Z - p.Z < mz - 2 && c.X - p.X > 3 && c.X - p.X < w - 3).OrderBy(c => Hash.Cell(c, 31)).ToList();
        foreach (var c in floor.Take(6)) x.C.Set(c, new Block(BlockType.Crate, Material.Scrap, new Rgb(0x6e, 0x5a, 0x40), Role.Prop));
        foreach (var c in floor.Skip(6).Take(2)) x.C.Set(c, new Block(BlockType.Machine, Material.Alloy, new Rgb(0x5a, 0x60, 0x66), Role.Prop));
        x.C.AddSlot(SlotKind.Loot, floor[8], floorModule);
        x.C.AddSlot(SlotKind.Spawn, floor[9], floorModule);
        x.C.AddSlot(SlotKind.Spawn, floor[10], floorModule);
        for (int i = 4; i < w - 4; i += 8)
            x.C.X.Lights.Add(new LightSpot { Cell = W(i, h - 2, d / 2), Color = new Rgb(0xff, 0xd0, 0x90), Mode = LightMode.Steady, Energy = 1.6f, Range = 14f, Module = floorModule });
        x.C.X.Story.Add(new StoryProp("hangar", W(w / 2, 0, d / 2), "The door rolled half open and stopped there. The tools are still out."));
    }

    /// <summary>Tall stacks: atmosphere processors, smelter chimneys, cooling towers.</summary>
    static void Stacks(Ctx x, SitePart p)
    {
        int n = p.Int("count", 2), r = p.Int("r", 3), h = p.Int("h", 30), gap = p.Int("gap", 6);
        bool cooling = p.Arg("type") == "cooling";
        var col = cooling ? new Rgb(0xb8, 0xb4, 0xa8) : new Rgb(0x6a, 0x64, 0x5e);
        for (int i = 0; i < n; i++)
        {
            int cx = p.X + i * (2 * r + gap), cz = p.Z;
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)h;
                float rr = cooling ? r * (1f - 0.35f * MathF.Sin(t * MathF.PI)) + 0.3f : r * (1f - 0.25f * t);
                Cylinder(x, cx, cz, rr, y, 1, new Block(BlockType.Cube, Material.Concrete, col.Scale(y % 6 == 5 ? 0.75f : 0.95f), Role.Wall), hollow: true);
            }
            // A lamp at the lip that once warned aircraft.
            Int3 lamp = new(cx + r - 1, h, cz);
            x.C.Set(lamp, new Block(BlockType.Cube, Material.Lumen, new Rgb(0xff, 0x40, 0x30), Role.Light));
            x.C.X.Lights.Add(new LightSpot { Cell = lamp, Color = new Rgb(0xff, 0x40, 0x30), Mode = LightMode.Beacon, Energy = 2f, Range = 18f });
        }
    }

    /// <summary>A field of tall stone obelisks with glyph bands: alien, old, and visible from far off.</summary>
    static void Obelisks(Ctx x, SitePart p)
    {
        int n = p.Int("count", 5), h = p.Int("h", 14), spread = p.Int("spread", 30);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n * MathF.Tau + x.Rng.Float(0f, 0.6f);
            float d = spread * (0.4f + 0.6f * x.Rng.Float());
            int ox = p.X + (int)(MathF.Cos(t) * d), oz = p.Z + (int)(MathF.Sin(t) * d);
            int hh = h + x.Rng.Between(-4, 6);
            for (int y = 0; y < hh; y++)
                for (int dz = 0; dz < 2; dz++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        bool glyph = y % 5 == 3;
                        x.C.Set(new Int3(ox + dx, y, oz + dz), new Block(BlockType.Cube, glyph ? Material.Glyph : Material.Basalt, glyph ? new Rgb(0x80, 0xff, 0xe0) : new Rgb(0x2a, 0x28, 0x30), Role.Decor));
                    }
            x.C.X.Lights.Add(new LightSpot { Cell = new Int3(ox, hh - 2, oz), Color = new Rgb(0x80, 0xff, 0xe0), Mode = LightMode.Pulse, Energy = 1.2f, Range = 10f });
        }
    }
}
