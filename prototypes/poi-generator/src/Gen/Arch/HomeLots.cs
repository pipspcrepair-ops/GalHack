#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>The ground round a house: its plot, fence and gate on a street, on a farm or on a city lot.</summary>
public static partial class Homes
{
    static HomeHints Loose(Rng rng) => new((int)(rng.NextU64() >> 33), (Climate)rng.Range(0, 4), rng.Float());

    /// <summary>A house of its street's culture on a roadside lot: set back behind its garden, its drive and path out to the lot's front, fenced.</summary>
    static void HouseLot(Lot l, Rng rng, LayoutHints? hints, string? family)
    {
        HomeHints home = hints?.Home ?? Loose(rng);
        var culture = HomeCulture.Of(HomeBook.Current, home, l.Tech);
        HouseStyle s = HouseStyle.Roll(culture, rng, family);
        const int Side = 6;
        int front = 10 + 2 * rng.Range(0, 2);
        Built b = Dwelling(l, l.F, s, Side, front);
        Grounds(l, l.F, b, s, b.U1 + 1 + Side, b.V1 + 8 + 2 * rng.Range(0, 2));
    }

    /// <summary>
    /// The plot round a house: a fence, a low wall, a hedge or a compound wall along the front and down the sides,
    /// gaps at the drive and the path, fallen in sections. The lot's gate is where the drive (or the path) meets its front.
    /// </summary>
    static void Grounds(Lot l, Frame f, Built b, HouseStyle s, int lotW, int lotD)
    {
        Canvas c = l.C;
        int gateU = b.DriveU0 is int d0 ? (d0 + b.DriveU1!.Value) / 2 : b.PathU;
        c.X.Gate = f.At(gateU, 0, -1);
        bool Gap(int u) => b.Paths.Append(b.PathU).Any(x => u >= x - 1 && u <= x + 2) || b.DriveU0 is int a && u >= a - 1 && u <= b.DriveU1!.Value + 1;
        int tall = s.Fence switch { "high" => 3, "hedge" => 2, "none" => 0, _ => 1 };
        if (tall == 0) return;
        Block Make(int u, int y, int v) => s.Fence switch
        {
            "rail" => new Block(BlockType.Rail, l.Tech == TechLevel.Low ? Material.Timber : Material.Steel, l.Tech == TechLevel.Low ? l.S.Door : new Rgb(0x5c, 0x60, 0x64), Role.Decor),
            "hedge" => new Block(BlockType.Cube, Material.Plant, new Rgb(0x48, 0x64, 0x34).Scale(0.9f + (Hash.Cell(new Int3(u, y, v), 0x4ED) % 20) / 100f), Role.Decor),
            "high" => new Block(BlockType.Cube, l.S.WallM == Material.Glass ? Material.Concrete : l.S.WallM, y == tall - 1 ? s.WallC.Scale(0.85f) : s.WallC, Role.Wall),
            _ => new Block(BlockType.Cube, l.S.BaseM, s.Wall is "brick" or "stone" ? s.WallC.Scale(0.9f) : l.S.Base, Role.Wall),
        };
        void Lay(int u, int v)
        {
            for (int y = 0; y < tall; y++)
            {
                Int3 p = f.At(u, y, v);
                if (c.Blocks.ContainsKey(p) || c.Occ.Contains(p) && y == 0) return;
                // A fence falls in 2 m sections; a wall mostly stands.
                if (Hash.Cell(new Int3(u / 2, 0, v / 2), 61 + (uint)lotW) % 100 < (s.Fence == "high" ? 8 : 24)) return;
                c.Set(p, Make(u, y, v));
            }
        }
        for (int u = 0; u < lotW; u++)
            if (!Gap(u)) Lay(u, 0);
        int back = s.Fence == "high" ? lotD - 1 : lotD * 2 / 3;
        for (int v = 1; v <= back; v++) { Lay(0, v); Lay(lotW - 1, v); }
        if (s.Fence == "high") for (int u = 1; u < lotW - 1; u++) Lay(u, lotD - 1);
    }

    /// <summary>A farm's house: a house of the farm's own people, no garage (the farm has its sheds), its door on the yard.</summary>
    static (int W, int D, int DoorU) House(Lot l, Frame f, string plan)
    {
        var culture = HomeCulture.Of(HomeBook.Current, Loose(l.R), l.Tech);
        string family = plan switch { "bungalow" => "bungalow", "cottage" => "cottage", "prefab" => "modern", _ => "suburban" };
        if (HomeBook.Current.Find(family) == null) family = HomeBook.Current.Families.FirstOrDefault(x => !x.Odd)?.Id ?? family;
        HouseStyle s = HouseStyle.Roll(culture, l.R, family);
        s.Garage = "none";
        s.Fence = "none";
        if (s.Plan is "court" or "row" or "tower" or "round" or "oval") s.Plan = "rect";
        Built b = Dwelling(l, f, s, 0, 4);
        return (b.U1 - b.U0 + 1, b.V1 - b.V0 + 1, b.DoorU);
    }

    /// <summary>
    /// A city lot's house or row of terraced houses (<see cref="CityBlocks"/>): the city's culture and the lot's
    /// size choose it, set back from the lot line with its drive to the street. Returns where its drive and path
    /// cross the lot line, for the fence's gaps.
    /// </summary>
    public static (List<int> Paths, int? DriveU0, int? DriveU1) CityHouse(Plot p, CityLot lot, bool terrace)
    {
        int q = (int)p.Turn(Dir.North);
        var frame = new Frame(q, q is 1 or 2 ? -1 : 0, q is 2 or 3 ? -1 : 0);
        var l = new Lot { C = p.C, R = p.R, Tech = p.Tech, S = Stuff.For(p.Tech, p.R), F = frame };
        p.C.X.PlainDoor = l.S.Door;
        HomeHints home = lot.Home ?? new HomeHints(lot.Style * 7 + lot.W, Climate.Temperate, 0.5f);
        var culture = HomeCulture.Of(HomeBook.Current, home, p.Tech);
        int lotW = 2 * lot.W, lotD = 2 * lot.D;
        HouseStyle s;
        int maxW = lotW - 4;
        if (terrace)
        {
            s = HouseStyle.Roll(culture, p.R, "terrace");
            if (s.Plan != "row") { s.Plan = "row"; s.Roof = "gable"; }
            // As many houses as the lot holds, a cell of fence each side.
            while (s.UnitW > 8 && (maxW - 1) / s.UnitW < 2) s.UnitW -= 2;
            s.Units = Math.Max(1, (maxW - 1) / s.UnitW);
            s.D = Math.Min(s.D, lotD - 12);
            s.Storeys = Math.Clamp(lot.Storeys, 2, 3);
        }
        else
        {
            s = HouseStyle.Roll(culture, p.R);
            for (int i = 0; i < 12 && (s.Family.Id == "terrace" || s.Plan == "court" && lotW < 30 || s.Plan == "tower"); i++) s = HouseStyle.Roll(culture, p.R);
            if (s.Family.Id == "terrace" || s.Plan is "court" or "tower" or "row") { s.Plan = "rect"; s.Units = 1; }
            s.D = Math.Min(s.D, lotD - 14);
            // Fitted to the lot: a narrower house, a side wing folded back to an ell, the garage brought into the house or given up.
            if (s.Plan == "wing" && s.W + 8 > maxW) s.Plan = "ell";
            s.W = Math.Min(s.W, Ev(maxW));
            int beside = s.Garage switch { "attached" => s.Double ? 9 : 5, "carport" => s.Double ? 10 : 6, "detached" => (s.Double ? 10 : 6) + 2 * s.Over + 2, _ => 0 };
            if (beside > 0 && s.W + beside > maxW)
            {
                s.Double = false;
                beside = s.Garage switch { "attached" => 5, "carport" => 6, _ => 8 + 2 * s.Over };
                if (s.W + beside > maxW) s.Garage = s.W >= 12 && l.Tech != TechLevel.Low && s.Plan is "rect" or "ell" ? "front" : "none";
            }
        }
        int front = Math.Min(8 + 2 * p.R.Range(0, 2), lotD / 4);
        Built b = Dwelling(l, frame, s, 2, front, maxW);
        p.C.X.Gate = frame.At(b.DriveU0 is int d0 ? (d0 + b.DriveU1!.Value) / 2 : b.PathU, 0, 0);
        p.C.VaultModule = l.Loot;
        return (b.Paths.Append(b.PathU).Distinct().ToList(), b.DriveU0, b.DriveU1);
    }

    /// <summary>
    /// A house on a water tower: a concrete shaft of storeys, a flight up each switching sides, and on top a
    /// wider house of the street's own cladding and roof, out over the shaft on every side.
    /// </summary>
    static Built Tower(Lot l, Frame lot, HouseStyle s, int side, int front)
    {
        Rng r = l.R;
        l.S = StuffOf(s, l.Tech, r);
        int storeys = Math.Clamp(s.Storeys, 3, 4), tw = 8, top = 12 + 2 * r.Range(0, 2), off = (top - tw) / 2, margin = off + 2;
        Frame f = lot.Shift(side + margin, front + margin);
        var shaft = new Hut(l, f) { Storeys = storeys };
        var concrete = new Rgb(0x9c, 0x98, 0x90);
        shaft.Skin = (u, y, v) => new Block(BlockType.Cube, Material.Concrete, concrete.Scale((y % 4 == 3 ? 0.88f : 1f) * (1f + (Hash.Cell(new Int3(u, y, v), 3) % 6) / 100f)), Role.Wall);
        shaft.Wings.Add(new Wing(0, 0, tw - 1, tw - 1));
        var halls = new List<Room>();
        for (int st = 0; st < storeys; st++) halls.Add(shaft.Add(st == 0 ? "hall" : "storage", st, 1, 1, tw - 2, tw - 2, ModuleKind.Hall));
        shaft.Raise();
        var flights = new List<int>();
        for (int st = 0; st + 1 < storeys; st++) flights.Add(shaft.Stair(st % 2 == 0 ? tw - 2 : 1, 2, st));
        // The house on top.
        var house = new Hut(l, f) { Storeys = 1, Lift = shaft.Walk(storeys) };
        house.Skin = Cladding(s, house, l.S, l.Tech);
        house.Wings.Add(new Wing(-off, -off, tw - 1 + off, tw - 1 + off, r.Chance(0.5f)));
        int last = storeys - 1, col = last % 2 == 0 ? tw - 2 : 1;
        // Two rooms side by side, the wall between them clear of where the stair comes up.
        int split = col == 1 ? 4 : 3;
        Room mess = col == 1 ? house.Add("mess", 0, -off + 1, -off + 1, split - 1, tw - 2 + off, ModuleKind.Hall) : house.Add("mess", 0, split + 1, -off + 1, tw - 2 + off, tw - 2 + off, ModuleKind.Hall);
        Room kitchen = col == 1 ? house.Add("farm-kitchen", 0, split + 1, -off + 1, tw - 2 + off, tw - 2 + off) : house.Add("farm-kitchen", 0, -off + 1, -off + 1, split - 1, tw - 2 + off);
        house.Raise();
        // The last flight comes up through the house's floor, the room keeping clear of it.
        flights.Add(shaft.Stair(col, 2, last));
        for (int v = 2; v <= 5; v++) house.Mark(col, v, false, 0, 1);
        house.Reserve(col, 2, 4);
        house.Gap(split, tw - 2 + r.Range(0, off), false);
        shaft.Gap(tw / 2 - 1, 0, true);
        shaft.Modules();
        house.Modules();
        for (int st = 0; st + 1 < storeys; st++)
        {
            l.C.Link(halls[st].Module, flights[st]);
            l.C.Link(flights[st], halls[st + 1].Module);
            l.C.X.Levels.Add(new LevelInfo { Storey = st + 1, WalkY = shaft.Walk(st + 1), Name = $"tower {st + 2}" });
        }
        l.C.Link(halls[last].Module, flights[^1]);
        l.C.Link(flights[^1], mess.Module);
        l.C.X.Levels.Add(new LevelInfo { Storey = storeys, WalkY = house.Lift, Name = "house" });
        shaft.Windows(0.6f);
        house.Windows(0.9f);
        if (s.Shutters) house.Shutters(s.ShutterC);
        switch (s.Roof)
        {
            case "flat": house.FlatRoof(1, 1); break;
            case "gable": house.Prisms(house.Wings.Select(w => (w, "gable")).ToList(), Math.Min(s.Over, 2), new List<Hut> { house }); break;
            default: Hut.Hipped(new List<Hut> { house }, Math.Min(s.Over, 2), false); break;
        }
        // A rail round the edge of the house's floor where it stands out over the shaft.
        house.Furnish();
        house.Loot(kitchen);
        shaft.Box();
        house.Box();
        l.Approach(f, tw / 2 - 1, -3);
        shaft.FrontDoor(tw / 2 - 1, 0, halls[0]);
        l.C.Link(l.C.EntranceModule, halls[0].Module);
        house.Service(tw / 2 - 1, -off, null);
        for (int v = -(front + margin); v < 0; v++)
            for (int k = 0; k < 2; k++) l.Plate(f, tw / 2 - 1 + k, v, l.S.Base.Scale(1.15f), Role.Floor);
        l.C.X.Homes.Add(new HomeNote(s.Family.Id, $"tower/{storeys}/{top}/{s.Roof}/{s.Wall}", storeys + 1, s.Roof, "none", "", 0, 0, default, Dir.North, false));
        return new Built { U0 = side, U1 = side + 2 * margin + tw - 1, V0 = front, V1 = front + 2 * margin + tw - 1, DoorU = side + margin + tw / 2 - 1, DoorV = front + margin, PathU = side + margin + tw / 2 - 1 };
    }
}
