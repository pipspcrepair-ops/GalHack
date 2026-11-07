#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Faction places on the player's 2 m blocks, each from its faction's character (docs/DESIGN.md, section 17
/// and the ships and structures table) and nothing hand-placed: Meridian builds clean massive blocks on a
/// long spine with corporate stripes; its freighters are huge hulls with oversized engines. The Vharn grow
/// segmented carapace domes on a sweeping asymmetric line, bone ribs and glowing slits. The Kessari repeat one
/// module in perfect symmetry round a lattice relay spire hung with status lights. Freeholders bolt faded
/// prefab modules together, and sink mines under a headframe. Every one wears its kit's colours, one to a
/// wall, and its faction's sigil (data/sigils.txt) where it reads from a distance.
/// </summary>
public static class FactionPlaces
{
    public static void Compose(Canvas c, PoiLibrary lib, Rng rng, LayoutHints? hints, TechLevel tech, string what)
    {
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Civic.Wear(p);
        SigilDef? sigil = lib.Sigils.GetValueOrDefault(SigilKey(c.Kit.Faction));
        switch (what)
        {
            case "lab": Lab(p, sigil); break;
            case "hull": Hull(p, sigil); break;
            case "hive": Hive(p, sigil); break;
            case "relay": Relay(p, sigil); break;
            case "homestead": Homestead(p, sigil); break;
            case "minehead": Minehead(p, sigil); break;
            default: throw new ArgumentException($"no faction plan '{what}'");
        }
        p.Finish();
    }

    static string SigilKey(Faction f) => f switch
    {
        Faction.Exchange => "ledger",
        _ => f.ToString().ToLowerInvariant(),
    };

    /// <summary>A kit's own paint for a symbol, the first colour only (a wall is one wall), or a fallback.</summary>
    static Stuff2 K(Plot p, char symbol, Stuff2 fallback) =>
        p.C.Kit.BySymbol(symbol) is { } e ? new Stuff2(e.Material, e.Colors[0]) : fallback;

    static Block Cube(Stuff2 s, Role r = Role.Wall) => Plot.Cube(s, r);

    static void Core(Plot p, int module, int u, int y, int v)
    {
        p.C.VaultModule = p.C.CoreModule = module;
        p.C.AddSlot(SlotKind.Core, p.Cell(u, y, v), module);
    }

    /// <summary>A storey's shell, its slab taken off and a gable of slopes on it (the verge in the wall's paint).</summary>
    static void Gabled(Plot p, int i0, int k0, int i1, int k1, int h, bool ridgeAlongI, Stuff2 wall, Stuff2 roof)
    {
        p.Shell(i0, k0, i1, k1, 0, h, wall, roof);
        p.Dig(i0, h, k0, i1, h, k1);
        p.Gable(i0, k0, i1, k1, h, ridgeAlongI, roof, wall, 1);
        p.Plinth(i0, k0, i1, k1, 0);
        p.Weathers(i0 - 2, 0, k0 - 2, i1 + 2, h + 12, k1 + 2, h);
    }

    static void Lamps(Plot p, int module, int i0, int k0, int i1, int k1, int y, int every = 5)
    {
        for (int i = i0 + 1; i <= i1 - 1; i += every)
            for (int k = k0 + 1; k <= k1 - 1; k += every) p.Lamp(2 * i + 1, y, 2 * k + 1, module, 1.2f, 11f);
    }

    // ---- Meridian: a research station --------------------------------------------------------------------

    /// <summary>A spine 88 m long of labs off one corridor, a square tower at its end with the sigil on it, a landing pad in front.</summary>
    static void Lab(Plot p, SigilDef? sigil)
    {
        const int I0 = 4, I1 = 47, K0 = 10, K1 = 17, H = 3, T1 = I1 + 9;
        Fab f = p.F;
        Stuff2 teal = K(p, 't', f.Accent), orange = K(p, 'k', f.Accent), slate = K(p, '%', f.Panel);
        p.Approach(25, 4, Dir.North);
        p.Pave(20, 1, 31, K0 - 1, f.Floor);
        p.Shell(I0, K0, I1, K1, 0, H, f.Wall, f.Roof);
        p.Plinth(I0, K0, I1, K1, 0);
        p.Weathers(I0 - 1, 0, K0 - 1, I1 + 1, H + 1, K1 + 1, H);
        // Corporate livery: a teal band and orange chevrons under the eaves, both long faces.
        foreach (int v in new[] { 2 * K0, 2 * K1 + 1 })
        {
            p.Repaint(2 * I0, 2 * H - 2, v, 2 * I1 + 1, 2 * H - 2, v, (_, _, _) => teal.C);
            p.Repaint(2 * I0, 2 * H - 1, v, 2 * I1 + 1, 2 * H - 1, v, (u, _, _) => ((u >> 1) & 3) < 2 ? orange.C : f.Wall.C);
        }
        for (int i = I0 + 2; i + 2 <= I1 - 2; i += 5)
        {
            if (i <= 25 && i + 2 >= 25) continue;
            p.Glazed(i, 1, K0, i + 2, K0);
            p.Glazed(i, 1, K1, i + 2, K1);
        }
        // Labs behind one long partition, the corridor along the front.
        p.Fill(I0 + 1, 0, 13, I1 - 1, H - 1, 13, Cube(slate));
        int corridor = p.Room(ModuleKind.Corridor, "corridor", 0, I0 + 1, 0, 11, I1 - 1, H - 1, 12);
        p.Door(25, 0, K0, true, corridor, p.Yard);
        for (int i = I0 + 3; i < I1; i += 8) p.Lamp(2 * i, 2 * H - 1, 2 * 11 + 1, corridor, 1.2f, 11f);
        int n = 0;
        for (int a = I0 + 1; a <= I1 - 6; a += 7, n++)
        {
            int b = Math.Min(a + 6, I1);
            if (b < I1) p.Fill(b, 0, 14, b, H - 1, K1 - 1, Cube(slate));
            int lab = p.Room(ModuleKind.Room, n % 3 == 2 ? "clean-room" : "lab", 0, a, 0, 14, b - 1, H - 1, K1 - 1);
            p.Door(a + 3, 0, 13, true, lab, corridor);
            for (int u = 2 * a + 1; u < 2 * b - 1; u += 2) p.Prop(u, 0, 2 * K1 - 1, n % 3 == 2 ? BlockType.Machine : BlockType.Bench, Material.Alloy, slate.C);
            p.Prop(2 * a + 1, 0, 2 * 14 + 2, BlockType.Shelf, Material.Alloy, slate.C);
            p.Terminal(2 * b - 2, 0, 2 * 14 + 1, lab);
            p.Lamp(2 * a + 6, 2 * H - 1, 2 * 15 + 1, lab, 1.4f, 11f);
            if (n % 2 == 0) p.Cache(2 * a + 3, 0, 2 * 15, lab);
        }
        // The tower: the station's landmark, a square shaft with the sigil high on its face and a mast on top.
        p.Shell(I1, 8, T1, 19, 0, 2, f.Wall, f.Roof);
        int server = p.Room(ModuleKind.Vault, "server-room", 0, I1 + 1, 0, 9, T1 - 1, 1, 18);
        p.Door(I1, 0, 12, false, corridor, server);
        for (int k = 10; k < 18; k += 2) p.Prop(2 * (T1 - 1) + 1, 0, 2 * k, BlockType.Machine, Material.Alloy, slate.C);
        p.Terminal(2 * (I1 + 2), 0, 2 * 17, server);
        p.Cache(2 * (I1 + 3), 0, 2 * 10, server);
        p.Lamp(2 * (I1 + 4) + 1, 3, 2 * 13 + 1, server, 1.6f, 12f);
        Core(p, server, 2 * (I1 + 6), 0, 2 * 15);
        const int Top = 13;
        for (int k = 8; k <= 19; k++)
            for (int i = I1; i <= T1; i++)
                if (i == I1 || i == T1 || k == 8 || k == 19) p.Fill(i, 3, k, i, Top, k, Cube(f.Wall));
        p.Fill(I1, Top + 1, 8, T1, Top + 1, 19, Cube(f.Roof, Role.Roof));
        p.Repaint(2 * I1, 2 * Top, 16, 2 * T1 + 1, 2 * Top + 1, 2 * 19 + 1, (_, _, _) => teal.C);
        foreach (int j in new[] { 3, 6, 9 }) p.Glazed(I1 + 2, j + 1, 19, T1 - 2, 19);
        p.Weathers(I1 - 1, 0, 7, T1 + 1, Top + 3, 20, Top + 1);
        if (sigil != null) SigilPaint.Paint(p, sigil, 2 * I1 + 1, 7, 2 * 8, true, true, false);
        for (int y = 2 * Top + 2; y < 2 * Top + 12; y++) p.Put(2 * (I1 + 4), y, 2 * 13, new Block(BlockType.Rib, Material.Alloy, slate.C, Role.Decor));
        p.Put(2 * (I1 + 4), 2 * Top + 12, 2 * 13, new Block(BlockType.Cube, Material.Lumen, orange.C, Role.Light));
        // The pad, ringed in hazard paint.
        p.Pave(6, 1, 15, 8, f.Floor);
        p.FloorStripes(12, 2, 31, 3);
        p.FloorStripes(12, 14, 31, 15);
        foreach (var (u, v) in new[] { (50, 12), (40, 6) }) p.Spawn(u, 0, v, p.Yard);
        p.Spawn(2 * 20, 0, 2 * 11, corridor);
    }

    // ---- Meridian: a freighter down ----------------------------------------------------------------------

    /// <summary>A hull 120 m long on its belly, its back broken amidships, three engines as big as houses, holds, quarters, a bridge in the bow.</summary>
    static void Hull(Plot p, SigilDef? sigil)
    {
        const int I0 = 6, I1 = 65, K0 = 10, K1 = 21, H = 5, Gap0 = 34, Gap1 = 36;
        Fab f = p.F;
        Stuff2 plate = K(p, 'a', f.Wall), teal = K(p, 't', f.Accent), orange = K(p, 'k', f.Accent), slate = K(p, '%', f.Panel);
        Stuff2 dark = new(Material.Steel, slate.C.Scale(0.6f));
        p.Approach(20, 4, Dir.North);
        p.Shell(I0, K0, I1, K1, 0, H, plate, plate);
        p.Plinth(I0, K0, I1, K1, 0);
        p.Weathers(I0 - 1, 0, K0 - 1, I1 + 6, H + 6, K1 + 1, H);
        // Livery down both flanks: an orange band, a teal stripe over it, the hull number by the bow.
        foreach (int v in new[] { 2 * K0, 2 * K1 + 1 })
        {
            p.Repaint(2 * I0, 4, v, 2 * I1 + 1, 5, v, (_, _, _) => orange.C);
            p.Repaint(2 * I0, 6, v, 2 * I1 + 1, 6, v, (_, _, _) => teal.C);
        }
        string number = (10 + (int)(p.R.Float() * 89)).ToString();
        p.Stencil(number, 2 * (I0 + 10), 7, 2 * K0, true, dark.C, 1, true);
        if (sigil != null) SigilPaint.Paint(p, sigil, 2 * (I0 + 12), 1, 2 * K1 + 1, true, false, false, 2);
        // The engines: three nacelles off the stern, dark mouths.
        foreach (int k in new[] { K0 + 1, K0 + 5, K0 + 9 })
        {
            p.Fill(I1 + 1, 1, k, I1 + 5, 3, k + 2, Cube(slate));
            p.Fill(I1 + 5, 2, k + 1, I1 + 5, 2, k + 1, Cube(dark));
        }
        // A dorsal block over the holds, solid, the hull's spine.
        p.Fill(I1 - 14, H + 1, K0 + 3, I1 - 3, H + 3, K1 - 3, Cube(plate, Role.Roof));
        p.Repaint(2 * (I1 - 14), 2 * (H + 3), 2 * (K0 + 3), 2 * (I1 - 3) + 1, 2 * (H + 3) + 1, 2 * (K1 - 3) + 1, (_, _, _) => orange.C);
        // The broken back: the hull's top torn open amidships, ragged a block at a time.
        for (int i = Gap0; i <= Gap1; i++)
            for (int k = K0; k <= K1; k++)
            {
                int from = 2 + (int)(Hash.Cell(new Int3(i, 0, k), 41) % 3);
                p.Dig(i, from, k, i, H, k);
            }
        // Inside: a corridor down the spine, holds to port, quarters to starboard, the bridge forward, the engines aft.
        p.Fill(I0 + 9, 0, 14, I1 - 8, H - 1, 14, Cube(slate));
        p.Fill(I0 + 9, 0, 17, I1 - 8, H - 1, 17, Cube(slate));
        p.Dig(Gap0, 0, 14, Gap1, H - 1, 14);
        p.Dig(Gap0, 0, 17, Gap1, H - 1, 17);
        p.Fill(I0 + 8, 0, K0 + 1, I0 + 8, H - 1, K1 - 1, Cube(slate));
        p.Fill(I1 - 8, 0, K0 + 1, I1 - 8, H - 1, K1 - 1, Cube(slate));
        int bridge = p.Room(ModuleKind.Vault, "bridge", 0, I0 + 1, 0, K0 + 1, I0 + 7, H - 1, K1 - 1);
        int fore = p.Room(ModuleKind.Corridor, "corridor", 0, I0 + 9, 0, 15, Gap0 - 1, H - 1, 16);
        int breach = p.Room(ModuleKind.Hall, "breach", 0, Gap0, 0, K0 + 1, Gap1, 1, K1 - 1);
        int aft = p.Room(ModuleKind.Corridor, "corridor", 0, Gap1 + 1, 0, 15, I1 - 9, H - 1, 16);
        int holdA = p.Room(ModuleKind.Hall, "hold", 0, I0 + 9, 0, K0 + 1, Gap0 - 1, H - 1, 13);
        int holdB = p.Room(ModuleKind.Hall, "hold", 0, Gap1 + 1, 0, K0 + 1, I1 - 9, H - 1, 13);
        int quarters = p.Room(ModuleKind.Room, "crew-quarters", 0, I0 + 9, 0, 18, I0 + 18, H - 1, K1 - 1);
        int mess = p.Room(ModuleKind.Room, "mess", 0, I0 + 20, 0, 18, Gap0 - 1, H - 1, K1 - 1);
        int stores = p.Room(ModuleKind.Room, "stores", 0, Gap1 + 1, 0, 18, I1 - 9, H - 1, K1 - 1);
        int engines = p.Room(ModuleKind.Hall, "engine-room", 0, I1 - 7, 0, K0 + 1, I1 - 1, H - 1, K1 - 1);
        p.Fill(I0 + 19, 0, 18, I0 + 19, H - 1, K1 - 1, Cube(slate));
        p.Door(I0 + 8, 0, 15, false, bridge, fore);
        p.Door(I1 - 8, 0, 15, false, aft, engines);
        p.Mouth(18, 0, K0, 23, 3, K0, p.Yard, holdA);
        p.Mouth(20, 0, 14, 25, 2, 14, holdA, fore);
        p.Mouth(46, 0, 14, 51, 2, 14, holdB, aft);
        p.Door(I0 + 13, 0, 17, true, quarters, fore);
        p.Door(I0 + 26, 0, 17, true, mess, fore);
        p.Door(48, 0, 17, true, stores, aft);
        p.C.Link(fore, breach);
        p.C.Link(breach, aft);
        p.Glazed(I0, 2, K0 + 3, I0, K1 - 3);
        for (int i = I0 + 10; i < Gap0 - 1; i += 3)
            for (int h = 0; h < 2; h++) p.Prop(2 * i, h, 2 * (K0 + 1) + 1, BlockType.Crate, Material.Scrap, (h == 0 ? orange : teal).C.Scale(0.8f));
        for (int i = Gap1 + 2; i < I1 - 10; i += 3) p.Prop(2 * i, 0, 2 * (K0 + 2), BlockType.Crate, Material.Scrap, slate.C);
        for (int i = I0 + 10; i < I0 + 18; i += 2) p.Prop(2 * i, 0, 2 * K1 - 1, BlockType.Bunk, Material.Alloy, slate.C);
        for (int i = I0 + 21; i < Gap0 - 1; i += 2) p.Prop(2 * i, 0, 2 * 19 + 1, BlockType.Table, Material.Alloy, slate.C);
        for (int k = K0 + 2; k < K1 - 1; k += 3) p.Machine(I1 - 6, 0, k, I1 - 3, k + 1, 2, slate);
        p.Terminal(2 * (I0 + 2), 0, 2 * (K0 + 6), bridge);
        p.Cache(2 * (I0 + 4), 0, 2 * (K0 + 2), bridge);
        p.Cache(2 * (I0 + 15), 0, 2 * (K0 + 2), holdA);
        p.Cache(2 * (Gap1 + 5), 0, 2 * (K1 - 2), stores);
        foreach (var (m, i0, i1, k) in new[] { (fore, I0 + 9, Gap0 - 1, 15), (aft, Gap1 + 1, I1 - 9, 15), (holdA, I0 + 9, Gap0 - 1, 12), (holdB, Gap1 + 1, I1 - 9, 12) })
            for (int i = i0 + 2; i < i1; i += 7) p.Lamp(2 * i, 2 * H - 1, 2 * k + 1, m, 1.2f, 11f);
        p.Lamp(2 * (I0 + 4), 2 * H - 1, 2 * 15, bridge, 1.4f, 12f);
        p.Lamp(2 * (I1 - 4), 2 * H - 1, 2 * 15, engines, 1.4f, 12f);
        foreach (var (u, v) in new[] { (40, 8), (70, 6) }) p.Spawn(u, 0, v, p.Yard);
        p.Spawn(2 * 25, 0, 2 * 12, holdA);
        if (p.C.CoreModule < 0) p.C.VaultModule = bridge;
    }

    // ---- Vharn: a hive outpost ---------------------------------------------------------------------------

    /// <summary>Carapace domes of four sizes on a sweeping line, joined by roofed passages, bone ribs, glowing slits, a crest with the sigil over the gate.</summary>
    static void Hive(Plot p, SigilDef? sigil)
    {
        Fab f = p.F;
        Stuff2 band = K(p, '%', f.Panel), bone = K(p, 'o', f.Steel), spike = K(p, 's', bone), glow = K(p, 'w', f.Accent);
        var domes = new (double I, double K, double R, int H, string What)[]
        {
            (14, 22, 7, 3, "brood"), (28, 17, 6, 3, "barracks"), (44, 22, 9, 4, "hive-hall"), (59, 16, 5, 2, "sentinel"),
        };
        int gateI = (int)domes[0].I, gateK = (int)(domes[0].K - domes[0].R);
        p.Approach(gateI, gateK - 4, Dir.North);
        var rooms = new int[domes.Length];
        for (int d = 0; d < domes.Length; d++)
        {
            var (ci, ck, r, h, what) = domes[d];
            p.Drum(ci, ck, r, 0, h, f.Wall, j => j % 2 == 1 ? band : f.Wall);
            p.Cap(ci, ck, r, h + 1, f.Roof);
            p.Weathers((int)(ci - r) - 2, 0, (int)(ck - r) - 2, (int)(ci + r) + 2, h + 2 + (int)r, (int)(ck + r) + 2, h + 1);
            var cells = new List<(int, int, int)>();
            for (int k = (int)(ck - r); k <= (int)(ck + r); k++)
                for (int i = (int)(ci - r); i <= (int)(ci + r); i++)
                    if (Plot.In(ci, ck, r - 1.0, i, k))
                        for (int y = 0; y <= 2 * h + 1; y++)
                            for (int dk = 0; dk < 2; dk++)
                                for (int di = 0; di < 2; di++) cells.Add((2 * i + di, y, 2 * k + dk));
            rooms[d] = p.RoomOf(d == 2 ? ModuleKind.Vault : ModuleKind.Hall, what, 0, cells);
            foreach (var (u, _, v) in cells.Where(c => c.Item2 == 0)) p.Lay(u, 0, v, new Block(BlockType.Plate, f.Floor.M, f.Floor.C, Role.Floor));
            // Glowing slits in the walls, every few blocks round.
            int seg = 0;
            for (double a = 0; a < Math.Tau; a += 0.45, seg++)
            {
                if (seg % 2 != 0) continue;
                int i = (int)Math.Floor(ci + Math.Cos(a) * (r - 0.5)), k = (int)Math.Floor(ck + Math.Sin(a) * (r - 0.5));
                if (!Plot.In(ci, ck, r, i, k) || Plot.In(ci, ck, r - 1.0, i, k)) continue;
                for (int y = 2 * h - 2; y <= 2 * h - 1; y++)
                    for (int dk = 0; dk < 2; dk++)
                        for (int di = 0; di < 2; di++)
                            if (p.Get(2 * i + di, y, 2 * k + dk).Type == BlockType.Cube)
                                p.Put(2 * i + di, y, 2 * k + dk, new Block(BlockType.Cube, Material.Lumen, glow.C, Role.Light));
            }
            // Bone ribs standing proud round the dome, down to the ground.
            for (double a = 0.2; a < Math.Tau; a += 0.6)
            {
                int u = (int)Math.Floor(2 * ci + Math.Cos(a) * (2 * r + 0.6)), v = (int)Math.Floor(2 * ck + Math.Sin(a) * (2 * r + 0.6));
                if (p.Get(u, 0, v).IsAir) p.Post(u, 2 * h + 1, v, bone);
            }
            p.Lamp((int)(2 * ci), 2 * h + 1, (int)(2 * ck) + 1, rooms[d], 1.4f, 2.2f * (float)r);
        }
        // Roofed passages between the domes.
        for (int d = 0; d + 1 < domes.Length; d++)
        {
            var a = domes[d];
            var b = domes[d + 1];
            int kk = (int)Math.Round((a.K + b.K) / 2), i0 = (int)a.I, i1 = (int)b.I;
            for (int i = i0; i <= i1; i++)
            {
                bool inside = Plot.In(a.I, a.K, a.R - 1.0, i, kk) || Plot.In(b.I, b.K, b.R - 1.0, i, kk);
                if (inside) continue;
                p.Fill(i, 0, kk - 2, i, 2, kk - 2, Cube(f.Wall));
                p.Fill(i, 0, kk + 1, i, 2, kk + 1, Cube(f.Wall));
                p.Fill(i, 2, kk - 1, i, 2, kk, Cube(f.Roof, Role.Roof));
            }
            p.Mouth(i0, 0, kk - 1, i1, 1, kk, rooms[d], rooms[d + 1]);
            p.Room(ModuleKind.Hall, "passage", 0, i0, 0, kk - 1, i1, 1, kk);
        }
        // The gate through the first dome, mandible spikes either side, and the crest over it with the sigil.
        p.Mouth(gateI - 1, 0, gateK - 1, gateI, 1, gateK + 1, p.Yard, rooms[0]);
        foreach (int u in new[] { 2 * (gateI - 2), 2 * (gateI + 1) + 1 })
        {
            for (int y = 0; y < 6; y++) p.Put(u, y, 2 * (gateK - 1), new Block(BlockType.Rib, bone.M, bone.C, Role.Decor));
            p.Put(u, 6, 2 * (gateK - 1), new Block(BlockType.Spike, spike.M, spike.C, Role.Decor));
        }
        const int CrestI0 = 2, CrestI1 = 9, CrestK = 12, CrestTop = 9;
        p.Fill(CrestI0, 0, CrestK, CrestI1, CrestTop, CrestK, Cube(f.Wall));
        p.Weathers(CrestI0 - 1, 0, CrestK - 1, CrestI1 + 1, CrestTop + 2, CrestK + 1, CrestTop);
        for (int u = 2 * CrestI0; u <= 2 * CrestI1 + 1; u += 3) p.Put(u, 2 * CrestTop + 2, 2 * CrestK, new Block(BlockType.Spike, spike.M, spike.C, Role.Decor));
        if (sigil != null) SigilPaint.Paint(p, sigil, 2 * CrestI0, 2, 2 * CrestK, true, true, true);
        // What each dome holds.
        var (bi, bk, br, _, _) = domes[0];
        for (double a = 0; a < Math.Tau; a += 0.5)
            p.Prop((int)(2 * bi + Math.Cos(a) * (2 * br - 4)), 0, (int)(2 * bk + Math.Sin(a) * (2 * br - 4)), BlockType.Pod, Material.Resin, glow.C.Scale(0.8f));
        var (ri, rk, rr, _, _) = domes[1];
        for (double a = 0.3; a < Math.Tau; a += 0.7)
            p.Prop((int)(2 * ri + Math.Cos(a) * (2 * rr - 3)), 0, (int)(2 * rk + Math.Sin(a) * (2 * rr - 3)), BlockType.Bunk, Material.Resin, band.C);
        var (hi, hk, _, _, _) = domes[2];
        p.Terminal((int)(2 * hi) + 6, 0, (int)(2 * hk), rooms[2]);
        p.Cache((int)(2 * hi) - 6, 0, (int)(2 * hk) + 4, rooms[2]);
        p.Cache((int)(2 * hi) + 4, 0, (int)(2 * hk) - 8, rooms[2]);
        Core(p, rooms[2], (int)(2 * hi), 0, (int)(2 * hk) + 8);
        var (si, sk, _, _, _) = domes[3];
        p.Machine((int)si - 1, 0, (int)sk - 1, (int)si, (int)sk, 1, band);
        p.Spawn((int)(2 * bi) + 3, 0, (int)(2 * bk), rooms[0]);
        p.Spawn((int)(2 * ri), 0, (int)(2 * rk) + 3, rooms[1]);
        p.Spawn((int)(2 * hi) + 5, 0, (int)(2 * hk) + 5, rooms[2]);
    }

    // ---- Kessari: a relay spire --------------------------------------------------------------------------

    /// <summary>Four identical modules round a core in perfect symmetry, joined by bridges, a lattice spire over the core hung with status lights, the sigil on every outward face.</summary>
    static void Relay(Plot p, SigilDef? sigil)
    {
        Fab f = p.F;
        Stuff2 lattice = K(p, 'l', f.Steel), panel = K(p, '%', f.Panel), cyan = K(p, '*', f.Accent), magenta = K(p, 'n', f.Accent), machine = K(p, 'm', f.Steel);
        const int C0 = 24, C1 = 35, MH = 5, CH = 6;
        p.Shell(C0, C0, C1, C1, 0, CH, f.Wall, f.Roof);
        p.Plinth(C0, C0, C1, C1, 0);
        p.Weathers(C0 - 1, 0, C0 - 1, C1 + 1, CH + 1, C1 + 1, CH);
        int core = p.Room(ModuleKind.Vault, "core-chamber", 0, C0 + 1, 0, C0 + 1, C1 - 1, CH - 1, C1 - 1);
        // The four modules: N, E, S, W, each the same box, the same rooms, the same lights.
        var modules = new (int I0, int K0, string What, Dir Out)[] { (25, 8, "workshop", Dir.North), (42, 25, "charging-bay", Dir.East), (25, 42, "stores", Dir.South), (8, 25, "relay-room", Dir.West) };
        var rooms = new int[4];
        for (int m = 0; m < 4; m++)
        {
            var (i0, k0, what, out_) = modules[m];
            int i1 = i0 + 9, k1 = k0 + 9;
            p.Shell(i0, k0, i1, k1, 0, MH, f.Wall, f.Roof);
            p.Plinth(i0, k0, i1, k1, 0);
            p.Weathers(i0 - 1, 0, k0 - 1, i1 + 1, MH + 1, k1 + 1, MH);
            rooms[m] = p.Room(ModuleKind.Hall, what, 0, i0 + 1, 0, k0 + 1, i1 - 1, MH - 1, k1 - 1);
            foreach (var (ci, ck) in new[] { (i0, k0), (i1, k0), (i0, k1), (i1, k1) }) p.Fill(ci, MH + 1, ck, ci, MH + 1, ck, new Block(BlockType.Cube, Material.Lumen, cyan.C, Role.Light));
            // Panel bands: the lattice's rhythm on the walls.
            for (int y = 2; y < 2 * MH; y += 4)
                p.Repaint(2 * i0, y, 2 * k0, 2 * i1 + 1, y, 2 * k1 + 1, (_, _, _) => panel.C);
            // The bridge to the core, and the sigil on the outward face.
            bool ns = out_ is Dir.North or Dir.South;
            if (ns)
            {
                int ka = out_ == Dir.North ? k1 : C1, kb = out_ == Dir.North ? C0 : k0;
                p.Shell(28, ka, 31, kb, 0, 2, f.Wall, f.Roof);
                int bridge = p.Room(ModuleKind.Corridor, "bridge", 0, 29, 0, ka + 1, 30, 1, kb - 1);
                p.Mouth(29, 0, ka, 30, 1, ka, out_ == Dir.North ? rooms[m] : core, bridge);
                p.Mouth(29, 0, kb, 30, 1, kb, bridge, out_ == Dir.North ? core : rooms[m]);
                if (sigil != null) SigilPaint.Paint(p, sigil, 2 * i0 + 6, 2, out_ == Dir.North ? 2 * k0 : 2 * k1 + 1, true, out_ == Dir.North, true, 2);
            }
            else
            {
                int ia = out_ == Dir.West ? i1 : C1, ib = out_ == Dir.West ? C0 : i0;
                p.Shell(ia, 28, ib, 31, 0, 2, f.Wall, f.Roof);
                int bridge = p.Room(ModuleKind.Corridor, "bridge", 0, ia + 1, 0, 29, ib - 1, 1, 30);
                p.Mouth(ia, 0, 29, ia, 1, 30, out_ == Dir.West ? rooms[m] : core, bridge);
                p.Mouth(ib, 0, 29, ib, 1, 30, bridge, out_ == Dir.West ? core : rooms[m]);
                if (sigil != null) SigilPaint.Paint(p, sigil, 2 * k0 + 6, 2, out_ == Dir.West ? 2 * i0 : 2 * i1 + 1, false, out_ == Dir.West, true, 2);
            }
            p.Lamp(2 * (i0 + 5), 2 * MH - 1, 2 * (k0 + 5), rooms[m], 1.6f, 13f);
        }
        p.Approach(26, 4, Dir.North);
        p.Door(26, 0, 8, true, rooms[0], p.Yard);
        // What's in them: the same machines in the same places, the stores and the relay's consoles.
        for (int m = 0; m < 4; m++)
        {
            var (i0, k0, what, _) = modules[m];
            for (int a = 0; a < 4; a++)
            {
                int u = 2 * (i0 + 2 + (a % 2) * 5), v = 2 * (k0 + 2 + (a / 2) * 5);
                p.Prop(u, 0, v, what switch { "stores" => BlockType.Crate, "relay-room" => BlockType.Terminal, "charging-bay" => BlockType.Locker, _ => BlockType.Machine }, machine.M, machine.C);
            }
        }
        p.Cache(2 * 27, 0, 2 * 45, rooms[2]);
        // The spire: four lattice legs off the core's roof, rings every four levels, a dish and a mast with its status light.
        const int Spire = 22;
        foreach (var (i, k) in new[] { (26, 26), (33, 26), (26, 33), (33, 33) }) p.Fill(i, CH + 1, k, i, Spire, k, new Block(BlockType.Lattice, lattice.M, lattice.C, Role.Wall));
        for (int j = CH + 4; j <= Spire; j += 4)
            for (int t = 26; t <= 33; t++)
            {
                foreach (var (i, k) in new[] { (t, 26), (t, 33), (26, t), (33, t) }) p.Fill(i, j, k, i, j, k, new Block(BlockType.Lattice, lattice.M, lattice.C, Role.Wall));
            }
        foreach (var (i, k) in new[] { (26, 26), (33, 26), (26, 33), (33, 33) }) p.Fill(i, Spire + 2, k, i, Spire + 2, k, new Block(BlockType.Cube, Material.Lumen, cyan.C, Role.Light));
        p.Fill(26, Spire + 1, 26, 33, Spire + 1, 33, Cube(f.Steel, Role.Roof));
        for (int y = 2 * Spire + 4; y < 2 * Spire + 16; y++) p.Put(59, y, 59, new Block(BlockType.Rib, lattice.M, lattice.C, Role.Decor));
        p.Put(59, 2 * Spire + 16, 59, new Block(BlockType.Cube, Material.Lumen, magenta.C, Role.Light));
        p.Weathers(25, CH, 25, 34, Spire + 8, 34, Spire);
        p.Terminal(2 * 29, 0, 2 * 29, core);
        p.Cache(2 * 31, 0, 2 * 26, core);
        p.Lamp(2 * 30, 2 * CH - 1, 2 * 30, core, 1.8f, 14f);
        Core(p, core, 2 * 27, 0, 2 * 32);
        p.Spawn(2 * 29, 0, 2 * 12, rooms[0]);
        p.Spawn(2 * 46, 0, 2 * 29, rooms[1]);
        p.Spawn(2 * 29, 0, 2 * 46, rooms[2]);
        p.Spawn(2 * 12, 0, 2 * 29, rooms[3]);
    }

    // ---- Freeholders: a homestead ------------------------------------------------------------------------

    /// <summary>Prefab modules bolted together, each its own faded colour: the house, the workshop with the store, the greenhouse; a water tank, crop rows in a fence, a painted number.</summary>
    static void Homestead(Plot p, SigilDef? sigil)
    {
        Fab f = p.F;
        Stuff2 timber = K(p, '%', f.Wall), red = K(p, 'n', f.Accent), concrete = K(p, 'q', f.Plinth), glass = K(p, 'g', new Stuff2(Material.Glass, f.Glass)), roof = f.Roof;
        Stuff2 faded = new(Material.Concrete, new Rgb(0xd8, 0xcf, 0xb8));
        p.Approach(12, 5, Dir.North);
        // The house: two rooms under a gable.
        Gabled(p, 6, 10, 17, 17, 2, true, timber, roof);
        p.Fill(11, 0, 11, 11, 1, 16, Cube(timber));
        int living = p.Room(ModuleKind.Room, "living-room", 0, 12, 0, 11, 16, 1, 16);
        int kitchen = p.Room(ModuleKind.Room, "kitchen", 0, 7, 0, 11, 10, 1, 16);
        p.Door(13, 0, 10, true, living, p.Yard);
        p.Door(11, 0, 14, false, kitchen, living);
        p.Glazed(15, 1, 10, 16, 10);
        p.Glazed(7, 1, 10, 9, 10);
        if (sigil != null) SigilPaint.Paint(p, sigil, 2 * 10 + 4, 0, 2 * 6, false, true, false, 2);
        // The workshop, barn red, the store at its back.
        Gabled(p, 20, 10, 31, 19, 3, false, red, roof);
        p.Fill(21, 0, 16, 30, 2, 16, Cube(red));
        int shop = p.Room(ModuleKind.Hall, "workshop", 0, 21, 0, 11, 30, 2, 15);
        int store = p.Room(ModuleKind.Vault, "store", 0, 21, 0, 17, 30, 2, 18);
        p.Mouth(24, 0, 10, 27, 2, 10, p.Yard, shop);
        p.Door(25, 0, 16, true, store, shop);
        p.Stencil(((int)(p.R.Float() * 90) + 10).ToString(), 2 * 24, 7, 2 * 10, true, faded.C, 1, true);
        // The greenhouse: glass on a concrete sill.
        p.Shell(34, 10, 45, 15, 0, 2, concrete, glass);
        for (int i = 35; i <= 44; i++)
            foreach (int k in new[] { 10, 15 }) p.Fill(i, 1, k, i, 1, k, Cube(glass, Role.Window));
        int green = p.Room(ModuleKind.Hall, "greenhouse", 0, 35, 0, 11, 44, 1, 14);
        p.Weathers(33, 0, 9, 46, 4, 16, 2);
        for (int u = 2 * 35 + 1; u < 2 * 44; u += 2)
            foreach (int v in new[] { 23, 25, 27 }) p.Prop(u, 0, v, BlockType.Plant, Material.Plant, new Rgb(0x5a, 0x7a, 0x3a));
        // Bolted connectors between the modules.
        foreach (var (a0, a1, ma, mb) in new[] { (17, 20, living, shop), (31, 34, shop, green) })
        {
            p.Shell(a0, 12, a1, 15, 0, 2, concrete, roof);
            int link = p.Room(ModuleKind.Corridor, "connector", 0, a0 + 1, 0, 13, a1 - 1, 1, 14);
            p.Mouth(a0, 0, 13, a0, 1, 14, ma, link);
            p.Mouth(a1, 0, 13, a1, 1, 14, link, mb);
        }
        // Inside: bunks and a table, a bench and machines, sacks and crates in the store.
        p.Prop(2 * 13 + 1, 0, 2 * 16, BlockType.Bunk, Material.Timber, timber.C);
        p.Prop(2 * 15 + 1, 0, 2 * 16, BlockType.Bunk, Material.Timber, timber.C);
        p.Prop(2 * 14, 0, 2 * 13, BlockType.Table, Material.Timber, timber.C.Scale(0.8f));
        p.Prop(2 * 8, 0, 2 * 16, BlockType.Locker, Material.Steel, concrete.C);
        p.Prop(2 * 9 + 1, 0, 2 * 16 + 1, BlockType.Bench, Material.Timber, timber.C);
        for (int i = 22; i < 30; i += 3) p.Machine(i, 0, 13, i + 1, 14, 1, concrete);
        for (int i = 22; i < 30; i += 2) p.Prop(2 * i, 0, 2 * 18 + 1, BlockType.Crate, Material.Timber, timber.C);
        p.Cache(2 * 28, 0, 2 * 17, store);
        p.Cache(2 * 8, 0, 2 * 12, kitchen);
        Core(p, store, 2 * 23, 0, 2 * 17 + 1);
        p.Lamp(2 * 14, 3, 2 * 16 + 1, living, 1.2f, 10f);
        p.Lamp(2 * 8 + 1, 3, 2 * 16 + 1, kitchen, 1f, 9f);
        p.Lamp(2 * 22, 5, 2 * 11, shop, 1.4f, 12f);
        p.Lamp(2 * 23, 5, 2 * 17, store, 1f, 9f);
        // A water tank, and crop rows inside a fence.
        p.Drum(40, 21, 3, 0, 3, concrete);
        p.Disc(40, 21, 3, 4, Cube(roof, Role.Roof));
        p.Weathers(36, 0, 17, 44, 6, 25, 4);
        const int F0 = 4, F1 = 49, G0 = 26, G1 = 42;
        for (int k = 2 * G0; k <= 2 * G1 + 1; k++)
            for (int u = 2 * F0; u <= 2 * F1 + 1; u++)
            {
                bool edge = u == 2 * F0 || u == 2 * F1 + 1 || k == 2 * G0 || k == 2 * G1 + 1;
                if (edge && !(k == 2 * G0 && u >= 2 * 16 && u <= 2 * 17 + 1)) p.Lay(u, 0, k, new Block(BlockType.Rail, Material.Timber, timber.C.Scale(0.8f), Role.Decor));
                else if (!edge && k % 4 == 1) p.Lay(u, 0, k, new Block(BlockType.Plant, Material.Plant, new Rgb(0x7a, 0x84, 0x3a), Role.Decor));
            }
        p.Spawn(2 * 14, 0, 2 * 14, living);
        p.Spawn(2 * 26, 0, 2 * 13, shop);
        p.Spawn(2 * 16, 0, 2 * 22, p.Yard);
    }

    // ---- Freeholders: a mine -----------------------------------------------------------------------------

    /// <summary>A headframe over a capped shaft, the winding house with the way down, ore bins; below, a drift with stopes off it and a powder store at its end.</summary>
    static void Minehead(Plot p, SigilDef? sigil)
    {
        Fab f = p.F;
        Stuff2 timber = K(p, '%', f.Wall), red = K(p, 'n', f.Accent), concrete = K(p, 'q', f.Plinth), post = K(p, 'o', f.Steel), roof = f.Roof;
        Stuff2 ore = new(Material.Rock, new Rgb(0x8a, 0x6a, 0x3e));
        const int J = -6;
        p.Approach(13, 5, Dir.North);
        Gabled(p, 8, 10, 19, 17, 2, true, timber, roof);
        int house = p.Room(ModuleKind.Hall, "winding-house", 0, 9, 0, 11, 18, 1, 16);
        p.Door(13, 0, 10, true, house, p.Yard);
        p.Glazed(16, 1, 10, 17, 10);
        if (sigil != null) SigilPaint.Paint(p, sigil, 2 * 10 + 4, 0, 2 * 8, false, true, false, 2);
        p.Machine(9, 0, 11, 11, 12, 1, red);
        p.Lamp(2 * 13, 3, 2 * 11, house, 1.2f, 10f);
        // The headframe: timber legs on a concrete collar, braced, the sheave wheel on top.
        p.Fill(24, 0, 11, 27, 0, 14, Cube(concrete));
        foreach (var (i, k) in new[] { (24, 11), (27, 11), (24, 14), (27, 14) }) p.Fill(i, 1, k, i, 12, k, new Block(BlockType.Rib, post.M, post.C, Role.Wall));
        foreach (int j in new[] { 4, 8, 12 })
            for (int t = 24; t <= 27; t++)
                foreach (var (i, k) in new[] { (t, 11), (t, 14), (24, t - 13), (27, t - 13) })
                    if (k >= 11 && k <= 14) p.Fill(i, j, k, i, j, k, Cube(timber));
        // The sheave: a square frame stood on edge over the shaft.
        p.Fill(24, 13, 12, 27, 13, 13, Cube(red));
        p.Fill(24, 16, 12, 27, 16, 13, Cube(red));
        p.Fill(24, 14, 12, 24, 15, 13, Cube(red));
        p.Fill(27, 14, 12, 27, 15, 13, Cube(red));
        p.Weathers(23, 0, 10, 28, 18, 15, 12);
        // Ore bins on legs by the track.
        p.Machine(30, 0, 11, 33, 14, 3, concrete);
        p.Repaint(60, 4, 22, 67, 5, 29, (_, _, _) => red.C);
        // The way down: a flight from the winding house into the drift.
        p.Line(17, 13, Dir.West, J, -J, 2, concrete);
        int down = p.Flight(17, 13, Dir.West, J, -J, 2, "stair", -1);
        p.C.Link(house, down);
        p.Level(-1, J, "drift");
        int drift = p.Tunnel("drift", -1, 18, 12, 48, 15, J, 3, timber);
        p.C.Link(down, drift);
        for (int i = 20; i < 48; i += 6)
            foreach (int k in new[] { 12, 15 })
                for (int y = 2 * J; y < 2 * J + 6; y++) p.Put(2 * i, y, 2 * k + (k == 12 ? 0 : 1), new Block(BlockType.Rib, post.M, post.C, Role.Decor));
        for (int i = 21; i < 48; i += 7) p.Lamp(2 * i, 2 * J + 5, 2 * 13 + 1, drift, 1f, 11f);
        foreach (var (k0, k1, mouthK, at) in new[] { (4, 10, 11, 28), (17, 23, 16, 38) })
        {
            int stope = p.Tunnel("stope", -1, at - 3, k0, at + 3, k1, J, 3, timber);
            p.Mouth(at, J, mouthK, at + 1, J + 1, mouthK, drift, stope);
            // Ore in the stope's face: the rock a player can work.
            for (int i = at - 3; i <= at + 3; i++)
                foreach (int k in new[] { k0 - 1, k1 + 1 })
                    if (k != mouthK && Hash.Cell(new Int3(i, 0, k), 57) % 3 != 0) p.Fill(i, J, k, i, J + 1, k, Cube(ore, Role.Rock));
            p.Prop(2 * at, 2 * J, 2 * ((k0 + k1) / 2), BlockType.Crate, Material.Timber, timber.C);
            p.Lamp(2 * at, 2 * J + 5, 2 * ((k0 + k1) / 2), stope, 0.9f, 9f);
        }
        int powder = p.Tunnel("powder-store", -1, 50, 12, 54, 15, J, 2, concrete, ModuleKind.Vault);
        p.Door(49, J, 13, false, drift, powder);
        p.Cache(2 * 53, 2 * J, 2 * 14, powder);
        p.Terminal(2 * 51, 2 * J, 2 * 15 + 1, powder);
        p.Lamp(2 * 52, 2 * J + 3, 2 * 13, powder, 0.8f, 8f);
        Core(p, powder, 2 * 52, 2 * J, 2 * 12);
        p.Cache(2 * 30, 2 * J, 2 * 7, drift);
        p.Spawn(2 * 30, 2 * J, 2 * 13, drift);
        p.Spawn(2 * 14, 0, 2 * 13, house);
    }
}
