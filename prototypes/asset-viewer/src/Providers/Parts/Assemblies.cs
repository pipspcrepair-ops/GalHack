using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// Small assembled examples of the catalogue's blocks: a corridor, a room, a door set, an engine bay
/// and a machine hall, put together from blocks found by what they do (the first light, the first
/// door...) so they follow the catalogue. Placed with building's own rules of what faces what.
/// </summary>
public static class Assemblies
{
    public static readonly string[] Names = { "Corridor", "Room", "Door set", "Engine bay", "Machine hall", "Glass gallery" };

    public static IEnumerable<AssetEntry> Entries(Catalogue cat)
    {
        foreach (string name in Names)
        {
            string captured = name;
            yield return new AssetEntry
            {
                Id = "assembly." + PartsData.Slug(name), Group = "Assemblies", Name = name, Randomisable = true,
                Options = new[]
                {
                    new AssetOption("Grid", "Large", "Small") { Rollable = true },
                    new AssetOption("Armour", cat.Types.Skip(1).Where(t => t.IsArmor).Select(t => t.Name).ToArray()) { Rollable = true },
                    new AssetOption("Damage", "Intact", "Worn", "Wrecked") { Rollable = true },
                    new AssetOption("Paint", BlocksProvider.Colours) { Rollable = true },
                    new AssetOption("Roof", "Cut away", "Closed") { Rollable = true },
                },
                Build = r => Build(cat, captured, r),
            };
        }
    }

    sealed class Kit
    {
        public Structure S;
        public BlockType Wall, Glass;
        public BlockType Door, Light, Ladder, Chair, Container, Generator, Thruster, Fuel, Console;
        public ushort PaintSlot;
        public Random Rng;
        public bool Open;

        public void Put(int x, int y, int z, BlockType t, int rot = 0, string shape = null, BlockFlags flags = 0)
        {
            if (t == null) return;
            ShapeDef sh = t.IsArmor && shape != null ? S.Catalogue.Shapes.FirstOrDefault(q => q.Id == shape) : null;
            Block b = Building.Make(S, t, sh, rot);
            b.Flags = flags;
            if (PaintSlot != 0 && t.IsArmor) b.Paint = FacePaint.All(PaintSlot);
            S.Set(new Int3(x, y, z), b);
        }

        public void Wall3(int x, int y, int z, string shape = null, int rot = 0) => Put(x, y, z, Wall, rot, shape);

        /// <summary>The orientation whose local face points the given way in the structure.</summary>
        public static int Facing(Dir local, Dir world)
        {
            for (int o = 0; o < Orientations.Count; o++)
                if (Orientations.ToWorld(o, local) == world) return o;
            return 0;
        }
    }

    static BlockType First(Catalogue cat, Func<BlockType, bool> where) => cat.Types.Skip(1).FirstOrDefault(where);

    static AssetInstance Build(Catalogue cat, string name, BuildRequest r)
    {
        bool small = r.Get("Grid", "Large") == "Small";
        var s = new Structure(cat, cat.Grid(small ? GridSize.Small : GridSize.Large)) { Name = name };
        var kit = new Kit
        {
            S = s, Rng = new Random((int)(r.Seed & 0x7fffffff)), Open = r.Get("Roof", "Cut away") == "Cut away",
            Wall = cat.Types.Skip(1).FirstOrDefault(t => t.IsArmor && t.Name == r.Get("Armour")) ?? First(cat, t => t.IsArmor),
            Glass = First(cat, t => t.IsArmor && t.Transparent) ?? First(cat, t => t.IsArmor),
            Door = First(cat, t => t.IsDoor), Light = First(cat, t => t.IsLight), Ladder = First(cat, t => t.IsLadder),
            Chair = First(cat, t => t.IsCommandChair), Container = First(cat, t => t.IsContainer), Generator = First(cat, t => t.IsGenerator),
            Thruster = First(cat, t => t.IsThruster), Fuel = First(cat, t => t.Function == "fuel"),
            Console = First(cat, t => t.Function == "constructor"),
        };
        Paint paint = BlocksProvider.PaintOf(r, out bool painted);
        if (painted) kit.PaintSlot = s.Palette.Intern(paint, "viewer");
        switch (name)
        {
            case "Corridor": Corridor(kit); break;
            case "Room": Room(kit); break;
            case "Door set": DoorSet(kit); break;
            case "Engine bay": EngineBay(kit); break;
            case "Machine hall": MachineHall(kit, cat); break;
            default: Gallery(kit); break;
        }
        Wear(kit, r.Get("Damage", "Intact"));
        s.DirtyChunks.Clear();

        var asset = new StructureAsset(s);
        asset.FactsFn = () => Facts(s);
        Working.Add(asset);
        return asset;
    }

    static IEnumerable<(string, string)> Facts(Structure s)
    {
        StructureStats st = StructureStats.Compute(s);
        yield return ("Blocks", $"{st.Blocks} ({st.Components} devices)");
        yield return ("Grid", s.Grid.Size == GridSize.Small ? "Small, 0.5 m" : "Large, 2 m");
        yield return ("Mass", $"{st.BlockMass:0.##} t");
        var (min, max) = s.Bounds();
        float c = s.Grid.CellSize;
        yield return ("Size", $"{(max.X - min.X + 1) * c:0.#} x {(max.Y - min.Y + 1) * c:0.#} x {(max.Z - min.Z + 1) * c:0.#} m");
        yield return ("Class", StructureClasses.Name(s.Grid.Size == GridSize.Small ? StructureClass.Small : StructureClass.Base));
    }

    /// <summary>Wear: scuffs on some blocks, holes and missing panels on a wreck.</summary>
    static void Wear(Kit k, string damage)
    {
        if (damage == "Intact") return;
        Structure s = k.S;
        var blocks = s.Blocks().Where(x => s.TypeOf(x.block).IsArmor).ToList();
        foreach (var (pos, b0) in blocks)
        {
            Block b = b0;
            double roll = k.Rng.NextDouble();
            float max = s.MaxHp(b);
            if (damage == "Worn") b.Hp = roll < 0.35 ? max * (0.3f + 0.6f * (float)k.Rng.NextDouble()) : max;
            else if (roll < 0.14) { s.Clear(pos); continue; }
            else b.Hp = max * (float)(0.1 + 0.7 * k.Rng.NextDouble());
            s.Set(pos, b);
        }
    }

    // ---- The assemblies (cells; +Y up, -Z forward) ---------------------------------------------

    static void Corridor(Kit k)
    {
        const int len = 8;
        int down = Kit.Facing(Dir.NegZ, Dir.NegY), toLeft = Kit.Facing(Dir.NegZ, Dir.PosX), toRight = Kit.Facing(Dir.NegZ, Dir.NegX);
        for (int z = 0; z < len; z++)
        {
            k.Wall3(0, 0, z);
            bool lamp = z == 1 || z == 5;
            if (k.Open && lamp) { k.Put(-1, 1, z, k.Light, toLeft); k.Put(1, 1, z, k.Light, toRight); }
            else { k.Wall3(-1, 1, z); k.Wall3(1, 1, z); }
            if (k.Open) continue;
            if (lamp) k.Put(0, 2, z, k.Light, down); else k.Wall3(0, 2, z);
        }
        k.Put(0, 1, 3, k.Door);
        // The ends are left open: a ladder at one, a bulkhead of glass at the other.
        k.Put(0, 1, 0, k.Ladder);
        k.Put(0, 1, len - 1, k.Glass);
    }

    static void Room(Kit k)
    {
        int down = Kit.Facing(Dir.NegZ, Dir.NegY), toLeft = Kit.Facing(Dir.NegZ, Dir.PosX), toRight = Kit.Facing(Dir.NegZ, Dir.NegX);
        const int w = 5, d = 5, h = 2;
        for (int x = 0; x < w; x++)
            for (int z = 0; z < d; z++)
            {
                k.Wall3(x, 0, z);
                if (k.Open) continue;
                bool lamp = (x == 1 || x == 3) && z == 2;
                if (lamp) k.Put(x, h + 1, z, k.Light, down); else k.Wall3(x, h + 1, z);
            }
        for (int y = 1; y <= h; y++)
            for (int i = 0; i < w; i++)
            {
                if (!(y == 1 && i == 2)) k.Wall3(i, y, 0); // a doorway in the front wall
                if (!k.Open) k.Wall3(i, y, d - 1);
                if (i > 0 && i < d - 1) { k.Wall3(0, y, i); k.Wall3(w - 1, y, i); }
            }
        if (k.Open)
        {
            k.Put(0, h, 2, k.Light, toLeft);
            k.Put(w - 1, h, 2, k.Light, toRight);
        }
        k.Put(2, 1, 0, k.Door);
        k.Put(2, 1, 3, k.Chair);
        k.Put(1, 1, 3, k.Container);
        k.Put(3, 1, 3, k.Generator);
        k.Put(3, 1, 1, k.Fuel);
        // A window of glass in a side wall.
        if (k.Glass != null) k.Put(0, 1, 3, k.Glass);
    }

    static void DoorSet(Kit k)
    {
        // A wall with three doorways: a pair of doors shut, a pair standing open, and a single door.
        for (int x = -3; x <= 3; x++)
            for (int y = 0; y < 3; y++)
                if (!(y < 2 && (x == 0 || x == -2 || x == 2))) k.Wall3(x, y, 0);
        k.Put(0, 0, 0, k.Door);
        k.Put(0, 1, 0, k.Door);
        k.Put(-2, 0, 0, k.Door, 0, null, BlockFlags.Open);
        k.Put(-2, 1, 0, k.Door, 0, null, BlockFlags.Open);
        k.Put(2, 0, 0, k.Door);
        k.Put(2, 1, 0, k.Glass);
        k.Put(1, 2, 1, k.Light, Kit.Facing(Dir.NegZ, Dir.NegY));
    }

    static void EngineBay(Kit k)
    {
        // A hull plate across the front, a generator and a tank behind it, and a bank of thrusters on the back.
        for (int x = -1; x <= 1; x++)
            for (int y = 0; y < 3; y++)
                k.Wall3(x, y, 0, "slope", Kit.Facing(Dir.NegZ, Dir.NegZ));
        k.Put(0, 1, 1, k.Generator);
        k.Put(-1, 1, 1, k.Fuel);
        k.Put(1, 1, 1, k.Container);
        // Exhaust leaves a thruster's back (+Z): the push is toward -Z, so they point as the hull does.
        for (int x = -1; x <= 1; x++)
            for (int y = 0; y < 3; y++)
                if (k.Thruster != null && (y == 1 || x == 0)) k.Put(x, y, 2, k.Thruster);
                else k.Wall3(x, y, 2);
    }

    static void MachineHall(Kit k, Catalogue cat)
    {
        ItemsOfMachines(k, cat, out var machines);
        for (int x = 0; x < 9; x++)
            for (int z = 0; z < 5; z++) k.Wall3(x, 0, z);
        int i = 0;
        foreach (BlockType m in machines)
        {
            int x = 1 + (i % 4) * 2, z = i < 4 ? 1 : 3;
            if (i >= 8) break;
            k.Put(x, 1, z, m, Kit.Facing(Dir.NegZ, Dir.PosZ));
            i++;
        }
        for (int x = 0; x < 9; x += 4) k.Put(x, 1, 4, k.Light, 0);
    }

    static void ItemsOfMachines(Kit k, Catalogue cat, out List<BlockType> machines)
    {
        var items = PartsData.Items;
        machines = cat.Types.Skip(1).Where(t => !t.IsArmor && items.Machines.ForBlock(t.Id) != null).ToList();
        if (machines.Count == 0) machines = cat.Types.Skip(1).Where(t => !t.IsArmor).Take(6).ToList();
    }

    static void Gallery(Kit k)
    {
        // A glass-roofed walk between two wings: the transparent block in a row of frames.
        int up = Kit.Facing(Dir.NegZ, Dir.PosY);
        for (int z = 0; z < 6; z++)
        {
            k.Wall3(0, 0, z);
            k.Wall3(-1, 1, z, z % 2 == 0 ? null : "half");
            k.Wall3(1, 1, z, z % 2 == 0 ? null : "half");
            k.Put(0, 2, z, k.Glass, up);
            k.Put(-1, 2, z, k.Glass, up, "slope");
            k.Put(1, 2, z, k.Glass, up, "slope");
        }
    }
}
