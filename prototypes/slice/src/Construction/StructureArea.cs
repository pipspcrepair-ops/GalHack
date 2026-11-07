using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;
using StructureClass = GalHack.Items.StructureClass;

namespace Slice.Construction;

/// <summary>
/// Structure classes and areas. A structure's class is its core and that core's mode (Empyrion's
/// four: a base or capital vessel on a Big Core, a small or hover vessel on a Small Core). Its area is its blocks'
/// bounding box grown by a margin for its class plus a share of its own size (data/slice.json
/// build.area): inside it you are linked to its one shared storage, and only there can you build
/// from that storage, open the CORE tab on it, or use its containers and constructors.
/// </summary>
public static class StructureArea
{
    /// <summary>The structure's class, from its core's mode.</summary>
    public static StructureClass ClassOf(Vessel v) => BuildingBridge.ClassOf(v.Structure);

    /// <summary>The core item that founds a structure and, taken down last, comes back: there is one for each grid.</summary>
    public static string CoreItem(Vessel v) => BuildingBridge.IsSmall(v.Structure) ? Slice.Items.PlayerInventory.SmallCore : Slice.Items.PlayerInventory.BigCore;

    /// <summary>How far past its blocks a structure's area reaches, m.</summary>
    public static float Margin(Vessel v, SliceRules.AreaRules rules)
    {
        var (min, max) = v.Structure.Bounds();
        float cell = v.Structure.Grid.CellSize;
        float half = MathF.Max(max.X - min.X, MathF.Max(max.Y - min.Y, max.Z - min.Z)) * cell * 0.5f + cell * 0.5f;
        return rules.Margin(ClassOf(v)) + rules.SizeShare * half;
    }

    /// <summary>Distance from a point in the world to the structure's blocks' bounding box (0 inside it), m.</summary>
    public static float Distance(Vessel v, Vector3 world)
    {
        var (min, max) = v.Structure.Bounds();
        float cell = v.Structure.Grid.CellSize;
        N.Vector3 lo = StructureBridge.CellCenter(v.Structure, min) - new N.Vector3(cell * 0.5f);
        N.Vector3 hi = StructureBridge.CellCenter(v.Structure, max) + new N.Vector3(cell * 0.5f);
        N.Vector3 p = v.Body.ToLocal(world.ToNumerics());
        N.Vector3 d = N.Vector3.Max(N.Vector3.Zero, N.Vector3.Max(lo - p, p - hi));
        return d.Length();
    }

    /// <summary>Is a point inside the structure's area?</summary>
    public static bool Contains(Vessel v, Vector3 world, SliceRules.AreaRules rules) =>
        v.Structure.Count > 0 && Distance(v, world) <= Margin(v, rules);

    /// <summary>Structures with a shared storage you could link to: built, cored, not wrecks or ruins.</summary>
    public static bool Linkable(Vessel v) => !v.IsWreck && !v.IsRuin && v.Structure.Core != null;

    /// <summary>Every linkable structure whose area holds a point, nearest first.</summary>
    public static List<Vessel> Around(SurfaceZone zone, Vector3 world, SliceRules.AreaRules rules) =>
        zone.Vessels.Where(v => Linkable(v) && Contains(v, world, rules)).OrderBy(v => Distance(v, world)).ToList();

    /// <summary>
    /// The structure you are linked to: the one you are building on, aboard or seated in when it
    /// counts (a vessel parked on a base: the one you build on wins), else the nearest whose area
    /// you are in; null outside every area.
    /// </summary>
    public static Vessel Linked(SurfaceZone zone, Vector3 world, SliceRules.AreaRules rules, params Vessel[] prefer)
    {
        foreach (Vessel p in prefer)
            if (p != null && Linkable(p) && Contains(p, world, rules)) return p;
        return Around(zone, world, rules).FirstOrDefault();
    }
}
