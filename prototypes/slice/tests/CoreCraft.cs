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

    /// <summary>The Skiff: thrusters, a chair, gyro and legs, no hover plates. Its blueprint is old, so it loads in Vessel Mode.</summary>
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
        var s = new Structure(blocks, blocks.Grid(GridSize.Large)) { Name = "Test ship", Mode = CoreMode.Base, GravityOn = true };
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

    /// <summary>The ground under a point, whatever has been built near it.</summary>
    public static float GroundY(Slice.Surface.SurfaceZone zone, Vector3 at) => zone.GroundAt(at.X, at.Z).Y;

    /// <summary>Every block of a structure, for checking that a switch rebuilt nothing.</summary>
    public static Dictionary<Int3, Block> Blocks(Structure s) => s.Blocks().ToDictionary(b => b.pos, b => b.block);

    public static bool SameBlocks(Structure s, Dictionary<Int3, Block> before) =>
        s.Count == before.Count && before.All(kv => s.TryGet(kv.Key, out Block b) && b.SameAs(kv.Value));
}
