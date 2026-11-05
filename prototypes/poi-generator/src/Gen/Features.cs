#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// One storey of a place. Storey 0 is the ground floor (walk at y 0), positive storeys climb and
/// negative ones go down into the ground. Depth drives how wrong things get.
/// </summary>
public sealed class LevelInfo
{
    public int Storey;
    public int WalkY;
    public string Name = "";
    /// <summary>How many storeys below ground (0 at or above it).</summary>
    public int Depth => Storey < 0 ? -Storey : 0;
    /// <summary>0 = as built; 1 = as wrong as it gets. Lights fail, the Bloom thickens, the plan bends.</summary>
    public float Wrongness;
    /// <summary>Fog density for the view (exponential fog per metre).</summary>
    public float Fog;
    /// <summary>Danger tier for enemies and loot (0 = the place's base).</summary>
    public int Tier;
    public override string ToString() => $"{Name} (storey {Storey}, walk {WalkY}, wrong {Wrongness:F2})";
}

/// <summary>
/// A lift: a shaft through every storey it serves, with a car the view moves. Nav links its stops
/// directly; the shaft doors stay shut unless the car is there.
/// </summary>
public sealed class LiftShaft
{
    /// <summary>The shaft's floor cells at y 0 (only x and z matter).</summary>
    public List<Int3> Footprint = new();
    public List<int> StopWalkY = new();
    /// <summary>The doorway at each stop (indices into Layout.Doorways), in the same order as the stops.</summary>
    public List<int> StopDoors = new();
    public IEnumerable<Int3> StopCells(int stop) => Footprint.Select(c => c with { Y = StopWalkY[stop] });
}

/// <summary>A hidden room behind a breakable wall. Unreachable until the wall breaks.</summary>
public sealed class SecretRoom
{
    public int Module;
    public List<Int3> WallCells = new();
    /// <summary>The side you find it from: the room or corridor the breakable wall faces.</summary>
    public int FromModule;
    public Dir Facing;   // from the hidden room out through the wall
    /// <summary>What gives it away: cracks, a draft, scorch, drag marks, a mismatch in the plan.</summary>
    public List<string> Tells = new();
    public List<Int3> TellCells = new();
}

/// <summary>A doorway that needs a keycard of some clearance (or a hack or a breach, the boarding way).</summary>
public sealed class DoorLock
{
    public int Doorway;
    public string Clearance = "";
    public bool Blast;
}

/// <summary>Where a keycard lies: on remains, in a cache or on a desk.</summary>
public sealed class KeyCard
{
    public string Clearance = "";
    public Int3 Cell;
    public int Module;
    public string On = "";
}

/// <summary>
/// A warned hazard. Radiation pockets and unstable floors are always signposted (signs at their
/// edges, sound hooks, a visible tell) and never sit on the way to the vault.
/// </summary>
public sealed class Hazard
{
    public string Kind = "";      // radiation, unstable, cold, vacuum, drop, toxic
    public Int3 Center;
    public float Radius;
    public float Strength;        // radiation units, degrees, or metres of drop
    public List<Int3> Cells = new();
    public List<Int3> Signs = new();
    public string Warning = "";
}

/// <summary>
/// A collapse breach: the ground and the ceiling caved in over a room, an alternative way in from
/// the surface down a rubble slope.
/// </summary>
public sealed class Breach
{
    public int Module;
    /// <summary>Where you stand on the surface at the lip, before going down.</summary>
    public Int3 SurfaceCell;
    /// <summary>The cells opened to the sky (carved through the ground and the ceiling).</summary>
    public List<Int3> Cells = new();
    public bool Climbable = true;
}

public enum LightMode : byte { Steady, Flicker, Dead, Emergency, Bloom, Pulse, Beacon }

/// <summary>A light the place was built with. State and depth decide whether it still works.</summary>
public sealed class LightSpot
{
    public Int3 Cell;
    public Rgb Color;
    public LightMode Mode;
    public float Energy = 1.4f;
    public float Range = 8f;
    public int Module = -1;
    public int Storey;
}

/// <summary>A sound hook the view (or the slice) plays: drips, hums, distant knocks, wind.</summary>
public sealed record SoundHook(string Kind, Int3 Cell, float Volume, int Storey);

/// <summary>A tag for a later pass: spawn points, patrol routes, vehicles, zero-g, vacuum, water.</summary>
public sealed record Marker(string Kind, Int3 Cell, string Note = "", int Group = 0);

/// <summary>Design-time extras a layout carries beyond modules and doorways.</summary>
public sealed class LayoutExtras
{
    public List<LevelInfo> Levels = new();
    public List<LiftShaft> Lifts = new();
    public List<SecretRoom> Secrets = new();
    public List<DoorLock> Locks = new();
    public List<KeyCard> Keys = new();
    public List<Hazard> Hazards = new();
    public List<Breach> Breaches = new();
    public List<LightSpot> Lights = new();
    public List<SoundHook> Sounds = new();
    public List<Marker> Markers = new();
    /// <summary>Story the place shows (no text logs: these are what the props mean).</summary>
    public List<StoryProp> Story = new();
    /// <summary>Planner notes for tests: room purposes and whether their adjacency rules held.</summary>
    public List<RoomNote> Rooms = new();
    /// <summary>Stairwells, by storey: the cells each flight occupies, to check they line up.</summary>
    public List<(int Storey, Int3 Min, Int3 Max)> Stairwells = new();
    /// <summary>Corridor runs for the geometric check: every cell that is a corridor floor cell.</summary>
    public HashSet<Int3> CorridorCells = new();
    /// <summary>Each building's storey plans in short (footprint, circulation, corridor topology, rooms), for the sameness tests.</summary>
    public List<(int Building, int Storey, string Signature)> Plans = new();
    /// <summary>A layout the planner made wrong on purpose (a strange spawn).</summary>
    public bool Wrong;
    public bool InSpace;
    /// <summary>Under water (sunken places): everything below WaterY is flooded unless sealed.</summary>
    public int? WaterY;
}

/// <summary>What the planner decided about one room, for tests and the overlay.</summary>
public sealed class RoomNote
{
    public int Module;
    public string Purpose = "";
    public int Storey;
    public bool HasNearRule;
    public bool NearSatisfied;
    public bool AvoidBroken;
    public List<string> Neighbours = new();
}
