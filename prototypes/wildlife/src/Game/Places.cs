using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// A place built of 1 m blocks for the showroom and the tests: a grid that starts solid and has
/// its rooms carved out, the rooms described for the population function, and the bits the
/// view draws (vent grilles, doors, lamps, a ramp for the player's feet).
/// </summary>
public sealed class BlockPlace
{
    public readonly Vector3I Size;
    readonly bool[] _solid;
    public readonly PoiPlan Plan = new();
    public readonly List<(Vector3 at, Vector3 normal)> Vents = new();
    public readonly List<(Vector3 at, Vector3 normal, Vector3 size)> Doors = new();
    public readonly List<(Vector3 at, Color color, float range)> Lamps = new();
    /// <summary>Slopes the player walks up and down between levels (from, to, width): the blocks under them are steps.</summary>
    public readonly List<(Vector3 from, Vector3 to, float width)> Ramps = new();
    /// <summary>Solid boxes of cargo and machinery inside rooms, for the view to colour.</summary>
    public readonly List<(Vector3I min, Vector3I max, Color color)> Props = new();
    public Vector3 Start;
    public Vector3 StartLook;
    public string Name = "";
    BlockSpace _space;

    public BlockPlace(Vector3I size)
    {
        Size = size;
        _solid = new bool[size.X * size.Y * size.Z];
        Array.Fill(_solid, true);
    }

    public bool Solid(int x, int y, int z) =>
        x < 0 || y < 0 || z < 0 || x >= Size.X || y >= Size.Y || z >= Size.Z || _solid[(y * Size.Z + z) * Size.X + x];

    public void Set(int x, int y, int z, bool solid)
    {
        if (x < 0 || y < 0 || z < 0 || x >= Size.X || y >= Size.Y || z >= Size.Z) return;
        _solid[(y * Size.Z + z) * Size.X + x] = solid;
        _space = null;
    }

    /// <summary>Opens a box of cells, min inclusive, max exclusive.</summary>
    public void Carve(Vector3I min, Vector3I max)
    {
        for (int y = min.Y; y < max.Y; y++)
        for (int z = min.Z; z < max.Z; z++)
        for (int x = min.X; x < max.X; x++) Set(x, y, z, false);
    }

    public void Fill(Vector3I min, Vector3I max, Color color)
    {
        for (int y = min.Y; y < max.Y; y++)
        for (int z = min.Z; z < max.Z; z++)
        for (int x = min.X; x < max.X; x++) Set(x, y, z, true);
        Props.Add((min, max, color));
    }

    public BlockSpace Space => _space ??= new BlockSpace(Solid, Vector3I.Zero, Size);

    /// <summary>Carves a room and describes it for the population function.</summary>
    public PoiRoom Room(RoomKind kind, Vector3I min, Vector3I max, int depth, bool dark)
    {
        Carve(min, max);
        var room = new PoiRoom { Index = Plan.Rooms.Count, Kind = kind, Min = min, Max = max, Depth = depth, Dark = dark };
        Plan.Rooms.Add(room);
        return room;
    }

    public static void Link(PoiRoom a, PoiRoom b)
    {
        a.Links.Add(b.Index);
        b.Links.Add(a.Index);
    }

    /// <summary>Steps down from one floor to another along +Z (or -Z), each 1 m high and <paramref name="run"/> long, with a slope over them for the player.</summary>
    public void Stairs(int x0, int x1, int z0, int fromFloor, int toFloor, int run, int headroom)
    {
        int steps = Math.Abs(fromFloor - toFloor);
        int dir = toFloor < fromFloor ? -1 : 1;
        for (int i = 0; i <= steps; i++)
        {
            int floor = fromFloor + dir * i;
            Carve(new Vector3I(x0, floor, z0 + i * run), new Vector3I(x1, floor + headroom + (dir < 0 ? i : 0), z0 + (i + 1) * run));
        }
        // The slope the player's feet follow, from the top step's front edge to the bottom one's.
        Ramps.Add((new Vector3((x0 + x1) * 0.5f, fromFloor, z0 + run * 0.5f), new Vector3((x0 + x1) * 0.5f, toFloor, z0 + run * (steps + 0.5f)), x1 - x0));
    }

    /// <summary>A vent grille: a 1 m opening a hand deep in a wall or ceiling, too small for anyone to follow.</summary>
    public void Vent(PoiRoom room, Vector3I cell, Vector3 normal)
    {
        Set(cell.X, cell.Y, cell.Z, false);
        // The grille is on the room's face of the cell; the creature waits behind it.
        Vector3 mouth = SpaceMath.Center(cell) + normal * 0.5f;
        Vents.Add((mouth, normal));
        room.Vents.Add((mouth, normal));
    }

    /// <summary>A doorway (2 wide, 3 high) between two rooms; the door shuts it.</summary>
    public void Door(PoiRoom into, Vector3I min, Vector3I max, Vector3 normal)
    {
        Carve(min, max);
        Vector3 at = new((min.X + max.X) * 0.5f, min.Y, (min.Z + max.Z) * 0.5f);
        Doors.Add((at, normal, max - min));
        into.Doors.Add((at, normal));
    }

    // ---- The showroom's places --------------------------------------------------------

    /// <summary>
    /// An abandoned Freeholder outpost dug into a hillside, three levels deep: an entrance hall
    /// with a lamp still burning, a long corridor (a creeper on its ceiling, a vent in its wall,
    /// a side store behind a door), a dark room with moss on the floor and something waiting in
    /// the far corner, stairs down to a lower hall, and a deep, fungus-choked room with a nest.
    /// </summary>
    public static BlockPlace Outpost(ulong seed = 41)
    {
        var p = new BlockPlace(new Vector3I(42, 14, 36)) { Name = "Abandoned outpost" };
        p.Plan.Seed = seed;
        p.Plan.Theme = PoiTheme.Industrial;
        p.Plan.State = PlaceState.Abandoned;
        PoiRoom hall = p.Room(RoomKind.Entrance, new Vector3I(2, 9, 2), new Vector3I(10, 13, 10), 0, false);
        PoiRoom corridor = p.Room(RoomKind.Corridor, new Vector3I(10, 9, 5), new Vector3I(28, 12, 7), 0, true);
        PoiRoom dark = p.Room(RoomKind.Room, new Vector3I(28, 9, 2), new Vector3I(37, 13, 12), 0, true);
        PoiRoom store = p.Room(RoomKind.Room, new Vector3I(20, 9, 8), new Vector3I(25, 12, 12), 0, true);
        Link(hall, corridor);
        Link(corridor, dark);
        Link(corridor, store);
        p.Door(store, new Vector3I(21, 9, 7), new Vector3I(23, 12, 8), Vector3.Back);
        p.Vent(corridor, new Vector3I(16, 11, 4), Vector3.Back);
        p.Vent(dark, new Vector3I(37, 11, 7), Vector3.Left);
        // Down to the lower hall: stairs from the dark room's back wall.
        p.Stairs(31, 34, 12, 9, 5, 2, 3);
        PoiRoom lower = p.Room(RoomKind.Hall, new Vector3I(24, 5, 22), new Vector3I(38, 9, 32), 1, true);
        Link(dark, lower);
        // Deeper: stairs west from the lower hall into the nest room.
        PoiRoom passage = p.Room(RoomKind.Corridor, new Vector3I(16, 5, 26), new Vector3I(24, 8, 28), 1, true);
        Link(lower, passage);
        p.StairsX(16, 12, 26, 28, 5, 2);
        PoiRoom nest = p.Room(RoomKind.Vault, new Vector3I(2, 1, 18), new Vector3I(12, 7, 34), 2, true);
        Link(passage, nest);
        p.Vent(nest, new Vector3I(8, 6, 34), Vector3.Forward);
        // Crates and machinery in the hall and the store; a dead console in the dark room.
        p.Fill(new Vector3I(3, 9, 7), new Vector3I(5, 10, 9), new Color("5d5a4e"));
        p.Fill(new Vector3I(23, 9, 10), new Vector3I(25, 11, 12), new Color("6a4c32"));
        p.Fill(new Vector3I(35, 9, 2), new Vector3I(37, 10, 4), new Color("3e4248"));
        p.Fill(new Vector3I(26, 5, 30), new Vector3I(28, 7, 32), new Color("4a4740"));
        p.Lamps.Add((new Vector3(6f, 12.5f, 6f), new Color("ffd59a"), 9f));
        p.Lamps.Add((new Vector3(36.5f, 12.4f, 11f), new Color("ff6a3a"), 3.5f)); // an emergency lamp, blinking
        p.Start = new Vector3(4.5f, 9f, 4f);
        p.StartLook = new Vector3(14f, 10.5f, 6f);
        return p;
    }

    /// <summary>A plain lit hall for showing creatures side by side: the mutation row.</summary>
    public static BlockPlace Gallery(string name = "A mutagenic world")
    {
        var p = new BlockPlace(new Vector3I(30, 12, 16)) { Name = name };
        p.Plan.Seed = 1;
        p.Room(RoomKind.Hall, new Vector3I(1, 1, 1), new Vector3I(29, 10, 15), 0, false);
        p.Lamps.Add((new Vector3(8f, 9.4f, 6f), new Color("ffe6c4"), 16f));
        p.Lamps.Add((new Vector3(21f, 9.4f, 6f), new Color("ffe6c4"), 16f));
        p.Lamps.Add((new Vector3(15f, 9.4f, 12f), new Color("c8d8ff"), 14f));
        p.Start = new Vector3(15f, 1f, 13.5f);
        p.StartLook = new Vector3(15f, 2f, 6f);
        return p;
    }

    /// <summary>Steps along X from x0 down to x1 (each 1 m lower) between z0 and z1.</summary>
    void StairsX(int x0, int x1, int z0, int z1, int fromFloor, int headroom)
    {
        int steps = Math.Abs(x0 - x1);
        for (int i = 0; i < steps; i++)
        {
            int floor = fromFloor - i;
            int x = x0 - i;
            Carve(new Vector3I(x - 1, floor, z0), new Vector3I(x, floor + headroom + i, z1));
        }
        Ramps.Add((new Vector3(x0 - 0.5f, fromFloor, (z0 + z1) * 0.5f), new Vector3(x1 - 0.5f, fromFloor - steps, (z0 + z1) * 0.5f), z1 - z0));
    }

    /// <summary>
    /// A crashed freighter's hold, level on the ground: ribs down the walls, cargo pods, a duct
    /// under the ceiling, and the engine room through a bulkhead at the back. Wreck nesters have
    /// built in the hold and the engine room.
    /// </summary>
    public static BlockPlace Hold(ulong seed = 7)
    {
        var p = new BlockPlace(new Vector3I(36, 12, 18)) { Name = "Derelict freighter" };
        p.Plan.Seed = seed;
        p.Plan.Theme = PoiTheme.Derelict;
        p.Plan.State = PlaceState.Abandoned;
        p.Plan.Derelict = true;
        PoiRoom airlock = p.Room(RoomKind.Entrance, new Vector3I(1, 2, 6), new Vector3I(5, 6, 12), 0, false);
        PoiRoom hold = p.Room(RoomKind.Hold, new Vector3I(5, 2, 2), new Vector3I(24, 9, 16), 1, true);
        PoiRoom engine = p.Room(RoomKind.EngineRoom, new Vector3I(26, 2, 4), new Vector3I(34, 8, 14), 1, true);
        p.Carve(new Vector3I(24, 2, 8), new Vector3I(26, 5, 10)); // the bulkhead hatch
        PoiRoom duct = p.Room(RoomKind.Duct, new Vector3I(7, 9, 8), new Vector3I(32, 10, 9), 1, true);
        Link(airlock, hold);
        Link(hold, engine);
        Link(hold, duct);
        // Ribs down both walls of the hold: pillars every 3 m.
        for (int x = 6; x < 24; x += 3)
        {
            p.Fill(new Vector3I(x, 2, 2), new Vector3I(x + 1, 9, 3), new Color("3a3c40"));
            p.Fill(new Vector3I(x, 2, 15), new Vector3I(x + 1, 9, 16), new Color("3a3c40"));
        }
        // Cargo pods, some burst.
        p.Fill(new Vector3I(8, 2, 4), new Vector3I(11, 4, 6), new Color("7a5a32"));
        p.Fill(new Vector3I(14, 2, 11), new Vector3I(17, 5, 14), new Color("5e6f74"));
        p.Fill(new Vector3I(19, 2, 4), new Vector3I(21, 3, 6), new Color("7a5a32"));
        p.Fill(new Vector3I(28, 2, 5), new Vector3I(32, 5, 7), new Color("464a52")); // engine block
        p.Fill(new Vector3I(28, 2, 11), new Vector3I(32, 5, 13), new Color("464a52"));
        // The duct opens into the hold and the engine room through grilles in its floor.
        p.Vent(hold, new Vector3I(12, 9, 8), Vector3.Down);
        p.Vent(engine, new Vector3I(30, 8, 8), Vector3.Down);
        p.Lamps.Add((new Vector3(3f, 5.5f, 9f), new Color("ffcf8a"), 6f));
        p.Lamps.Add((new Vector3(14f, 8.5f, 3.5f), new Color("9fc4c8"), 9f)); // a cold strip light still working
        p.Lamps.Add((new Vector3(30f, 7.5f, 9f), new Color("ff7a2a"), 4f)); // a reactor's dying glow
        p.Start = new Vector3(2.5f, 2f, 9f);
        p.StartLook = new Vector3(14f, 4f, 9f);
        return p;
    }
}

/// <summary>
/// The voxel caves the showroom and the tests share: terrain-digging's seeded landscape, a
/// tunnel dug into its hillside with the survival tool (balls of rock 2 m round, side by side
/// and one above the other, as a player digs), and the roomiest chamber of its first generator cave.
/// </summary>
public static class CaveSite
{
    public const int Seed = 1977;

    public static TerrainDigging.Terrain.ChunkStore Store() => new(new TerrainDigging.Terrain.SimpleTerrainSource(Seed));

    /// <summary>Digs the tunnel (30 m along z from 40, 9 m under the lowest ground over it); returns its axis at z 46 and how many cells came out.</summary>
    public static (Vector3 axis, int removed, int ty) DigTunnel(TerrainDigging.Terrain.ChunkStore store)
    {
        var src = (TerrainDigging.Terrain.SimpleTerrainSource)store.Source;
        float y = float.MaxValue;
        for (int z = 40; z <= 72; z++) y = MathF.Min(y, src.SurfaceHeight(0f, z));
        int ty = (int)MathF.Floor(y - 9f);
        int removed = 0;
        for (int z = 40; z <= 70; z++)
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, 2), (-1, 2), (1, 2) })
            removed += store.Dig(new TerrainDigging.Terrain.Int3(dx, ty + dy, z), 2f).Removed.Count;
        return (new Vector3(0.5f, ty + 1.5f, 46f), removed, ty);
    }

    /// <summary>The roomiest spot in the generator's first cave: open floor, a ceiling 4 to 6 m up.</summary>
    public static (Vector3 floor, float height) Chamber(TerrainDigging.Terrain.ChunkStore store)
    {
        var src = (TerrainDigging.Terrain.SimpleTerrainSource)store.Source;
        Vector3 best = Vector3.Zero;
        float bestScore = -1f, bestH = 0f;
        for (int x = -90; x <= -30; x++)
        for (int z = -5; z <= 55; z++)
        {
            int top = (int)MathF.Floor(src.SurfaceHeight(x + 0.5f, z + 0.5f));
            for (int y = top - 18; y < top - 3; y++)
            {
                if (store.IsSolid(new TerrainDigging.Terrain.Int3(x, y, z)) || !store.IsSolid(new TerrainDigging.Terrain.Int3(x, y - 1, z))) continue;
                int h = 0;
                while (h < 7 && !store.IsSolid(new TerrainDigging.Terrain.Int3(x, y + h, z))) h++;
                if (h < 4 || h >= 7) continue;
                int wide = 0;
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                    if (!store.IsSolid(new TerrainDigging.Terrain.Int3(x + dx, y, z + dz)) && store.IsSolid(new TerrainDigging.Terrain.Int3(x + dx, y - 1, z + dz))) wide++;
                float score = wide + h * 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new Vector3(x + 0.5f, y, z + 0.5f);
                    bestH = h;
                }
            }
        }
        return (best, bestH);
    }
}
