using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Production;
using GalHack.Terrain;
using Planets.Gen;
using PlanetSurface.Skies;
using Slice.Game;
using Slice.Items;
using TerrainDigging.Terrain;

namespace Slice.Tests;

/// <summary>
/// The drives against the owner's rule (22 Nov, docs/solar-systems.md section 8 item 7): "warp fuel and warp drive
/// materials are found on vacuum worlds; fold fuel and fold drive materials are found on high-gravity worlds". Each
/// drive's whole bill (the block, the constructor its parts need and fuel for a first jump) is walked down to ore and
/// checked against the deposits the worlds' maps really hold; then, on fresh systems, that ore is dug out of the fuel
/// moon's and a high-gravity world's own ground and the drives are made from it.
/// </summary>
public partial class SystemsTest
{
    /// <summary>Each drive's block and its line in the power rules (what it burns).</summary>
    static readonly (string Block, string Drive)[] Drives = { ("warp-drive", "warp-drive"), ("fold-drive-t1", "fold-drive"), ("fold-drive-t2", "fold-drive") };

    /// <summary>Fold crystal the proof makes for a first fold jump: ten light years at one a light year.</summary>
    const int FirstFoldCrystal = 10;

    /// <summary>A drive's whole bill: the block, the least constructor that makes everything in it, and fuel for a first jump.</summary>
    static Bill DriveBill(GameData data, string block, string drive, int fuel)
    {
        ItemCatalogue items = data.Items;
        int tier = items.Graph.TierNeeded(items.Item(block));
        MachineDef fab = items.Machines.Constructors.First(m => m.Tier == tier && m.BlockId != null);
        return Bill.Of((block, 1), (fab.BlockId, 1), (data.Power.Drives[drive].Item, fuel));
    }

    /// <summary>The deposit kinds a bill's ore comes from (dug stone is in any rock); a raw no deposit gives goes in <paramref name="none"/>.</summary>
    static HashSet<DepositKind> KindsOf(ItemCatalogue items, Bill bill, List<string> none)
    {
        var kinds = new HashSet<DepositKind>();
        foreach (string id in items.Graph.RawCost(bill).Keys)
        {
            ItemDef raw = items.Item(id);
            if (raw.Source == ItemSource.Dug) continue;
            if (Enum.TryParse(raw.DepositKind, out DepositKind kind)) kinds.Add(kind);
            else none.Add(id);
        }
        return kinds;
    }

    // ---- 11. Each drive's bill, down to ore, from its own worlds --------------------------------------------------------

    /// <summary>Against what the survey read off every mapped world's coarse map (no world is made again for it).</summary>
    void DriveBills(List<Ground> worlds)
    {
        GameData data = GameData.Shared;
        ItemCatalogue items = data.Items;
        var held = worlds.Select(w => w.Ore.Keys.ToHashSet()).ToList();
        HashSet<DepositKind> everywhere = held.Aggregate((a, b) => a.Intersect(b).ToHashSet());
        var none = new List<string>();
        var kinds = Drives.ToDictionary(d => d.Block, d => KindsOf(items, DriveBill(data, d.Block, d.Drive, 1), none));
        HashSet<DepositKind> warp = kinds["warp-drive"];
        string Names(IEnumerable<DepositKind> ks) => string.Join(", ", ks.Select(k => SliceItems.OreFor(items, k.ToString())?.Id ?? k.ToString()));
        Check(none.Count == 0, $"drives: every raw of every drive, its constructor and its fuel is mined from a deposit ({(none.Count == 0 ? "none is not" : string.Join(", ", none.Distinct()) + " no deposit gives")})");

        // The warp drive: all of it on every vacuum world, the light ones too, so it never needs a high-gravity world.
        int vacuum = 0, light = 0;
        var warpShort = new List<string>();
        for (int i = 0; i < worlds.Count; i++)
        {
            PlanetSpec spec = worlds[i].Body.Spec;
            if (!spec.Airless) continue;
            vacuum++;
            if (!spec.HighGravity) light++;
            warpShort.AddRange(warp.Where(k => !held[i].Contains(k)).Select(k => $"{spec} lacks {k}"));
        }
        Check(warpShort.Count == 0 && light > 0,
            $"warp drive: all {vacuum} vacuum worlds ({light} light ones) hold every ore of the drive, its constructor and its warp cells ({Names(warp.Except(everywhere))} beside what every world has), so it never needs a high-gravity world ({warpShort.Count} short{(warpShort.Count > 0 ? ": " + warpShort[0] : "")})");

        // The fold drives: their own ores (all but the warp coils', which come from the vacuum worlds) on every high-gravity world.
        int heavy = worlds.Count(w => w.Body.Spec.HighGravity);
        foreach (var (block, _) in Drives.Skip(1))
        {
            HashSet<DepositKind> own = kinds[block].Except(warp).ToHashSet();
            var distinct = own.Except(everywhere).ToList();
            int foldShort = Enumerable.Range(0, worlds.Count).Count(i => worlds[i].Body.Spec.HighGravity && !own.IsSubsetOf(held[i]));
            Check(distinct.Count > 0 && heavy > 0 && foldShort == 0,
                $"{block}: its own ores ({Names(distinct)} beside what every world has; its warp coils' from the vacuum worlds) lie on all {heavy} high-gravity worlds ({foldShort} short)");
        }

        // The map marks sit on the real deposits.
        var marked = new[] { (Mark: PlanetSurface.Skies.Deposit.WarpDrive, Kinds: warp), (Mark: PlanetSurface.Skies.Deposit.FoldDriveMaterials, Kinds: kinds["fold-drive-t1"].Except(warp).ToHashSet()) };
        int marks = 0, bare = 0;
        for (int i = 0; i < worlds.Count; i++)
            foreach (var (mark, ores) in marked.Where(m => worlds[i].Body.Deposits.Contains(m.Mark)))
            {
                marks++;
                if (!ores.IsSubsetOf(held[i])) bare++;
            }
        Check(marks > 0 && bare == 0, $"drives: each of the {marks} drive-material marks sits on a world whose ground holds them ({bare} do not)");

        // The start's promise, in amounts: the fuel moon holds the ore for the warp drive, its constructor and a first jump.
        Bill need = items.Graph.Expand(DriveBill(data, "warp-drive", "warp-drive", FirstJumpCells)).Leaves;
        int moons = 0;
        var thin = new List<string>();
        for (int i = 0; i < worlds.Count; i++)
        {
            if (worlds[i].Body != worlds[i].System.FuelMoon) continue;
            moons++;
            Dictionary<DepositKind, double> have = worlds[i].Ore;
            foreach (var (id, n) in need)
                if (Enum.TryParse(items.Item(id).DepositKind, out DepositKind k) && have.GetValueOrDefault(k) < n) thin.Add($"{worlds[i].Body.Spec} {have.GetValueOrDefault(k):F0} of {n} {id}");
        }
        Check(moons > 0 && thin.Count == 0, $"warp drive: all {moons} fuel moons hold the ore for it, its advanced constructor and {FirstJumpCells} warp cells ({need}) ({thin.Count} short{(thin.Count > 0 ? ": " + thin[0] : "")})");
    }

    // ---- 12. Proof: dig the drives' ore where it lies and make them ------------------------------------------------------

    /// <summary>
    /// On fresh systems: an advanced constructor, the warp drive, warp cells for a first jump and the fold drive's warp
    /// coils, all from ore dug out of the fuel moon's own ground at the game's resolution; then the fold drive and fold
    /// crystal for a first jump from the system's smallest high-gravity world, the coils carried there. Dug as the sleeper's
    /// tool digs (an ore cell gives its body's ore, a rock cell stone) and made by the constructors' own queues. Each world
    /// is made, dug and let go in <see cref="Mine"/>, and collected before the next is made.
    /// </summary>
    void DriveProof(IEnumerable<StarSystem> systems)
    {
        ItemCatalogue items = GameData.Shared.Items;
        int coils = items.Blocks.For("fold-drive-t1")["warp-coil"];
        foreach (StarSystem s in systems)
        {
            Orbiter moon = s.FuelMoon, heavy = s.Bodies.Where(b => b.Spec is { HighGravity: true }).MinBy(b => b.Spec.WidthKm);
            var hold = new SharedStorage(items, "moon", 1e9);
            Bill want = items.Graph.Expand(Bill.Of(("fabricator-t3", 1), ("warp-drive", 1), ("warp-cell", FirstJumpCells), ("warp-coil", coils))).Leaves;
            string lacked = Mine(moon.Spec, want, hold, out Bill dug);
            bool made = lacked == null && Make(items, hold, "large", ("fabricator-t3", 1)) && Make(items, hold, "advanced", ("warp-drive", 1), ("warp-cell", FirstJumpCells), ("warp-coil", coils));
            Check(made, $"proof: dug out of {moon.Name} ({moon.Spec}): {dug}; made from it an advanced constructor, a warp drive, {FirstJumpCells} warp cells and the fold drive's {coils} warp coils{(lacked != null ? $" (short of {lacked})" : "")}");

            var there = new SharedStorage(items, "heavy", 1e9);
            there.TryAdd(Bill.Of(("warp-coil", coils)));
            want = items.Graph.Expand(items.Blocks.For("fold-drive-t1").Minus(Bill.Of(("warp-coil", coils))).Add("fold-crystal", FirstFoldCrystal)).Leaves;
            lacked = Mine(heavy.Spec, want, there, out dug);
            made = lacked == null && Make(items, there, "advanced", ("fold-drive-t1", 1), ("fold-crystal", FirstFoldCrystal));
            Check(made, $"proof: dug out of {heavy.Name} ({heavy.Spec}, {heavy.Spec.GravityScale} g): {dug}; made from it, with the coils from the moon, a fold drive and {FirstFoldCrystal} fold crystal{(lacked != null ? $" (short of {lacked})" : "")}");
        }
    }

    /// <summary>Orders things at a constructor of a kind on a hold and runs its queue, sub-parts and all, till it is idle; true if all were made.</summary>
    static bool Make(ItemCatalogue items, SharedStorage hold, string machine, params (string Id, int Count)[] orders)
    {
        var fab = new Constructor(items, items.Machines.Get(machine), hold, new WorldRules());
        int[] before = orders.Select(o => hold.Count(o.Id)).ToArray();
        if (!orders.All(o => fab.Order(o.Id, o.Count).Ok)) return false;
        for (int t = 0; t < 20000 && fab.Busy; t++) fab.Tick(1.0);
        return orders.Select((o, i) => hold.Count(o.Id) - before[i] >= o.Count).All(ok => ok);
    }

    /// <summary>
    /// Digs a world's own ground (made at the game's resolution) for a bill of raw ore into a hold: each ore's deposits
    /// nearest the landing first, every cell of the ore body and the rock about it, as the sleeper's tool gives them.
    /// Null when it got it all, else what it fell short of.
    /// </summary>
    static string Mine(PlanetSpec spec, Bill want, SharedStorage hold, out Bill dug)
    {
        GameData data = GameData.Shared;
        int orePerCell = data.Rules.Materials.OrePerCell, stonePerCell = data.Rules.Materials.StonePerRockCell;
        GC.Collect(); // the last world dug is let go before this one is made
        WorldMap map = PlanetGenerator.Generate(spec);
        var ground = new PlanetTerrainSource(map);
        LandingSite site = Landing.Find(map, map.WidthMeters * 0.5, map.HeightMeters * 0.5);
        const int S = ChunkStore.Size;
        var density = new float[S * S * S];
        var material = new CellMaterial[S * S * S];
        var done = new HashSet<Int3>();
        var got = new Bill();
        double Far(Planets.Gen.Deposit d)
        {
            double dx = Mathx.WrapDelta(d.X - site.X, map.WidthMeters), dy = Mathx.WrapDelta(d.Y - site.Y, map.HeightMeters);
            return dx * dx + dy * dy;
        }
        int Floor(double v) => (int)Math.Floor(v / S);
        void Dig(Planets.Gen.Deposit d)
        {
            ground.OreCentre(d, out double x, out double y, out double z);
            float r = PlanetTerrainSource.OreRadius(d) * 1.2f + 1f;
            for (int cz = Floor(z - r); cz <= Floor(z + r); cz++)
            for (int cy = Floor(y - r); cy <= Floor(y + r); cy++)
            for (int cx = Floor(x - r); cx <= Floor(x + r); cx++)
            {
                var chunk = new Int3((cx % ground.ChunksX + ground.ChunksX) % ground.ChunksX, cy, (cz % ground.ChunksZ + ground.ChunksZ) % ground.ChunksZ);
                ground.GenerateChunk(chunk, density, material);
                for (int i = 0; i < material.Length; i++)
                {
                    var cell = new Int3(chunk.X * S + i % S, chunk.Y * S + i / (S * S), chunk.Z * S + i / S % S);
                    double dx = Mathx.WrapDelta(cell.X + 0.5 - x, ground.Width), dz = Mathx.WrapDelta(cell.Z + 0.5 - z, ground.Height);
                    if (Math.Abs(dx) > r || Math.Abs(dz) > r || Math.Abs(cell.Y + 0.5 - y) > r || !done.Add(cell)) continue;
                    if (material[i] == CellMaterial.Ore && SliceItems.OreFor(data.Items, ground.OreKindAt(cell).ToString()) is ItemDef ore) got.Add(ore.Id, orePerCell);
                    else if (material[i] is CellMaterial.Rock or CellMaterial.DeepRock) got.Add("stone", stonePerCell);
                }
            }
        }
        foreach (var (id, n) in want)
        {
            ItemDef raw = data.Items.Item(id);
            IEnumerable<Planets.Gen.Deposit> bodies = map.Deposits.All();
            if (raw.Source != ItemSource.Dug) bodies = Enum.TryParse(raw.DepositKind, out DepositKind kind) ? bodies.Where(d => d.Kind == kind) : Enumerable.Empty<Planets.Gen.Deposit>();
            foreach (Planets.Gen.Deposit d in bodies.OrderBy(Far).ToList())
            {
                if (got[id] >= n) break;
                Dig(d);
            }
        }
        hold.TryAdd(got);
        dug = got;
        var lacked = want.Where(kv => got[kv.Key] < kv.Value).Select(kv => $"{kv.Value - got[kv.Key]} {kv.Key}").ToList();
        return lacked.Count == 0 ? null : string.Join(", ", lacked);
    }
}
