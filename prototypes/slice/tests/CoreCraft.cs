using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using Godot;
using Slice.Game;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>The structures the core tests and the core screenshots share: built from blocks in data, never scripted into a mode.</summary>
public static class CoreCraft
{
    /// <summary>A rotation that points a thruster's push straight up.</summary>
    public static int Lifting()
    {
        for (int r = 0; r < 24; r++)
            if (Orientations.ToWorld(r, Dir.NegZ) == Dir.PosY) return r;
        throw new InvalidOperationException("no rotation points a thruster up");
    }

    /// <summary>The Skiff: thrusters, a chair, gyro and legs, no hover plates. Its blueprint is old, so it loads in Flight Mode.</summary>
    public static Structure Skiff(GameData data) => data.Blueprint(data.Rules.Build.StarterBlueprint);

    /// <summary>Fits hover plates under the outermost cells of a craft's belly: four of them, spread out, one block each.</summary>
    public static int FitPlates(Structure s, Catalogue blocks)
    {
        BlockType plate = blocks.Type("hover");
        var (min, max) = s.Bounds();
        var spots = s.Blocks().Select(b => b.pos).Where(p => s.IsSolid(s.Get(p))).ToList();
        int done = 0;
        foreach (var (x, z) in new[] { (min.X, min.Z), (max.X, min.Z), (min.X, max.Z), (max.X, max.Z) })
        {
            // The lowest free cell under the block nearest that corner.
            foreach (Int3 p in spots.OrderBy(p => Math.Abs(p.X - x) + Math.Abs(p.Z - z)).ThenBy(p => p.Y))
            {
                Int3 under = p + new Int3(0, -1, 0);
                Block b = Building.Make(s, plate);
                if (Building.Check(s, under, b) != PlaceError.None) continue;
                Building.Place(s, under, b);
                done++;
                break;
            }
        }
        return done;
    }

    /// <summary>
    /// A small Big Core structure: a steel deck with its core, a generator, a chair, a gyro and
    /// enough thrusters pushing up to lift it, in Base Mode. With no thrusters when asked: it can't fly.
    /// </summary>
    public static Structure Ship(Catalogue blocks, bool thrusters, Func<Structure, bool> enough)
    {
        var s = new Structure(blocks) { Name = "Test ship", Mode = CoreMode.Base, GravityOn = true };
        Building.Place(s, Int3.Zero, Building.Make(s, blocks.CoreType));
        for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
                if (x != 0 || z != 0) Building.Place(s, new Int3(x, 0, z), Building.Make(s, blocks.Type("steel")));
        Building.Place(s, new Int3(0, 1, 0), Building.Make(s, blocks.Type("chair")));
        Building.Place(s, new Int3(1, 1, 0), Building.Make(s, blocks.Type("generator")));
        Building.Place(s, new Int3(-1, 1, 0), Building.Make(s, blocks.Type("gyro")));
        if (!thrusters) return s;
        int up = Lifting();
        var spots = new List<Int3>();
        for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
                if (z != 0) spots.Add(new Int3(x, 1, z));
        foreach (Int3 p in spots)
        {
            if (enough(s)) break;
            Building.Place(s, p, Building.Make(s, blocks.Type("thruster"), rot: up));
        }
        return s;
    }

    /// <summary>
    /// A large hover structure (a hover tank, a land carrier): a solid steel hull 7 by 11 cells,
    /// deepened a layer at a time until it weighs <paramref name="tonnes"/>, with a chair, a
    /// generator and a gyro on its deck, thrusters pushing forward and back, and hover plates spread
    /// under its belly (its four corners at least) until they lift half again its weight here. Set in Hover Mode.
    /// </summary>
    public static Structure HoverTank(Catalogue blocks, float tonnes, float gravity)
    {
        var s = new Structure(blocks) { Name = "Hover tank", Mode = CoreMode.Hover, GravityOn = true };
        Building.Place(s, Int3.Zero, Building.Make(s, blocks.CoreType));
        BlockType steel = blocks.Type("steel");
        for (int y = 0; y > -8 && (y == 0 || StructureStats.Compute(s).Mass < tonnes); y--)
        {
            // A block goes on only where it touches one already placed: sweep the layer until it is full.
            for (bool grew = true; grew;)
            {
                grew = false;
                for (int x = -3; x <= 3; x++)
                    for (int z = -5; z <= 5; z++)
                        grew |= !s.Has(new Int3(x, y, z)) && Building.Place(s, new Int3(x, y, z), Building.Make(s, steel)) == PlaceError.None;
            }
        }
        var (low, _) = s.Bounds();
        int belly = low.Y - 1;
        Building.Place(s, new Int3(0, 1, 0), Building.Make(s, blocks.Type("chair")));
        Building.Place(s, new Int3(1, 1, 0), Building.Make(s, blocks.Type("generator")));
        Building.Place(s, new Int3(-1, 1, 0), Building.Make(s, blocks.Type("gyro")));
        int back = Facing(Dir.PosZ);
        foreach (int x in new[] { -2, 2 })
        {
            Building.Place(s, new Int3(x, 1, -4), Building.Make(s, blocks.Type("thruster")));
            Building.Place(s, new Int3(x, 1, 4), Building.Make(s, blocks.Type("thruster"), rot: back));
        }
        BlockType plate = blocks.Type("hover");
        // The corners first, then the middles of the sides, then down the middle.
        var spots = new (int x, int z)[] { (-3, -5), (3, -5), (-3, 5), (3, 5), (-3, 0), (3, 0), (0, -5), (0, 5), (0, 0), (-3, -3), (3, -3), (-3, 3), (3, 3), (0, -3), (0, 3) };
        for (int i = 0; i < spots.Length; i++)
        {
            float lift = s.Blocks().Where(b => s.TypeOf(b.block).Function == "hover").Sum(b => s.TypeOf(b.block).Lift);
            if (i >= 4 && lift >= 1.5f * StructureStats.Compute(s).Mass * gravity) break;
            Building.Place(s, new Int3(spots[i].x, belly, spots[i].z), Building.Make(s, plate));
        }
        return s;
    }

    /// <summary>A rotation that points a thruster's push along <paramref name="push"/>.</summary>
    static int Facing(Dir push)
    {
        for (int r = 0; r < 24; r++)
            if (Orientations.ToWorld(r, Dir.NegZ) == push && Orientations.ToWorld(r, Dir.PosY) == Dir.PosY) return r;
        throw new InvalidOperationException($"no upright rotation pushes {push}");
    }

    /// <summary>The ground under a point, whatever has been built near it.</summary>
    public static float GroundY(Slice.Surface.SurfaceZone zone, Vector3 at) => zone.GroundAt(at.X, at.Z).Y;

    /// <summary>Every block of a structure, for checking that a switch rebuilt nothing.</summary>
    public static Dictionary<Int3, Block> Blocks(Structure s) => s.Blocks().ToDictionary(b => b.pos, b => b.block);

    public static bool SameBlocks(Structure s, Dictionary<Int3, Block> before) => s.BlockCount == before.Count && Changed(s, before) == 0;

    /// <summary>How many of the blocks before are gone or not as they were (worn, turned).</summary>
    public static int Changed(Structure s, Dictionary<Int3, Block> before) => before.Count(kv => !s.TryGet(kv.Key, out Block b) || !b.SameAs(kv.Value));
}
