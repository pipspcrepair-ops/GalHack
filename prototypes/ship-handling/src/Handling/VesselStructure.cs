using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ShipHandling.Handling;

/// <summary>The three handling classes. Each has its own top speeds in the tunables file.</summary>
public enum VesselClass { Hover, Small, Capital }

/// <summary>
/// A thruster. It pushes the ship along <see cref="Facing"/> (its exhaust points the other
/// way). Where it sits never matters to handling: placement adds no torque.
/// </summary>
public readonly record struct ThrusterSpec(string Size, Vector3 Position, Vector3 Facing, float Thrust, float Mass);

/// <summary>A gyro: its torque sets the turn rate.</summary>
public readonly record struct GyroSpec(string Size, Vector3 Position, float Torque, float Mass);

/// <summary>A hover engine: holds its ride height above ground or water, up to its lift.</summary>
public readonly record struct HoverEngineSpec(string Size, Vector3 Position, float Lift, float RideHeight, float Mass);

/// <summary>A landing leg. <see cref="Position"/> is where it hangs from the hull; the foot is Length below.</summary>
public readonly record struct GearSpec(string Size, Vector3 Position, float Length, float Mass);

/// <summary>What a block looks like. Only presentation cares; solid blocks also collide.</summary>
public enum BlockKind { Box, Prism, Glass, Glow }

/// <summary>
/// One block of the vessel's shape, in vessel-local metres (forward is -Z, up is +Y). Solid
/// blocks become the vessel's collision shape: one body per vessel, a few boxes in it.
/// </summary>
public readonly record struct HullBlock(Vector3 Center, Vector3 Size, string Color, bool Solid,
    BlockKind Kind = BlockKind.Box, Vector3 RotationDegrees = default);

/// <summary>
/// Everything handling needs to know about a vessel's build. A small interface on purpose: the
/// canonical block-and-component model can implement it later and nothing else has to change.
/// </summary>
public interface IVesselStructure
{
    string Name { get; }
    VesselClass Class { get; }
    float HullMass { get; }
    float CargoMass { get; }
    IReadOnlyList<ThrusterSpec> Thrusters { get; }
    IReadOnlyList<GyroSpec> Gyros { get; }
    IReadOnlyList<HoverEngineSpec> HoverEngines { get; }
    IReadOnlyList<GearSpec> LandingGear { get; }
    IReadOnlyList<HullBlock> Blocks { get; }

    /// <summary>Where the pilot's eyes are, vessel-local.</summary>
    Vector3 Cockpit { get; }

    /// <summary>Goes up whenever anything above changes, so cached numbers know to refresh.</summary>
    int Revision { get; }
}

/// <summary>
/// The prototype's stand-in for a real build: plain lists of components that the debug keys
/// can change (add or remove thrusters, add cargo, make it lopsided).
/// </summary>
public sealed class VesselLoadout : IVesselStructure
{
    public string Name { get; init; } = "Vessel";
    public VesselClass Class { get; init; }
    public float HullMass { get; set; }
    public float CargoMass { get; private set; }
    public Vector3 Cockpit { get; init; }
    public int Revision { get; private set; }

    public List<ThrusterSpec> ThrusterList { get; } = new();
    public List<GyroSpec> GyroList { get; } = new();
    public List<HoverEngineSpec> HoverList { get; } = new();
    public List<GearSpec> GearList { get; } = new();
    public List<HullBlock> BlockList { get; } = new();

    public IReadOnlyList<ThrusterSpec> Thrusters => ThrusterList;
    public IReadOnlyList<GyroSpec> Gyros => GyroList;
    public IReadOnlyList<HoverEngineSpec> HoverEngines => HoverList;
    public IReadOnlyList<GearSpec> LandingGear => GearList;
    public IReadOnlyList<HullBlock> Blocks => BlockList;

    /// <summary>True once <see cref="MakeLopsided"/> has piled every thruster onto one side.</summary>
    public bool Lopsided { get; private set; }

    public void AddCargo(float tonnes)
    {
        CargoMass = Mathf.Max(0f, CargoMass + tonnes);
        Revision++;
    }

    public void AddThruster(ThrusterSpec thruster)
    {
        ThrusterList.Add(thruster);
        Revision++;
    }

    /// <summary>Removes the last thruster pushing along <paramref name="facing"/>. False if there is none.</summary>
    public bool RemoveThruster(Vector3 facing)
    {
        int index = ThrusterList.FindLastIndex(t => t.Facing.Dot(facing) > 0.9f);
        if (index < 0) return false;
        ThrusterList.RemoveAt(index);
        Revision++;
        return true;
    }

    /// <summary>
    /// Bolts every thruster onto the left flank, as badly balanced as a build can get. Handling
    /// must not change at all: thruster placement never adds torque.
    /// </summary>
    public void MakeLopsided()
    {
        float flank = BlockList.Count > 0 ? BlockList.Min(b => b.Center.X - b.Size.X / 2f) : -2f;
        for (int i = 0; i < ThrusterList.Count; i++)
        {
            ThrusterSpec t = ThrusterList[i];
            float size = t.Size == "capital" ? 3f : t.Size == "large" ? 1.2f : 0.5f;
            ThrusterList[i] = t with { Position = new Vector3(flank - size, t.Position.Y, t.Position.Z) };
        }
        Lopsided = true;
        Revision++;
    }

    public VesselLoadout Clone()
    {
        var copy = new VesselLoadout { Name = Name, Class = Class, HullMass = HullMass, Cockpit = Cockpit };
        copy.CargoMass = CargoMass;
        copy.Lopsided = Lopsided;
        copy.ThrusterList.AddRange(ThrusterList);
        copy.GyroList.AddRange(GyroList);
        copy.HoverList.AddRange(HoverList);
        copy.GearList.AddRange(GearList);
        copy.BlockList.AddRange(BlockList);
        return copy;
    }
}
