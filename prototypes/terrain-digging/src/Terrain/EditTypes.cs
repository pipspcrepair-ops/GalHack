using System;
using System.Collections.Generic;

namespace TerrainDigging.Terrain;

/// <summary>Who is removing terrain. Every kind has the same rule: anything but bedrock.</summary>
public enum EditKind : byte
{
    /// <summary>A player's survival tool. Refused only on bedrock.</summary>
    Tool,

    /// <summary>A crash or liftoff blast. Same rule as the tool, but a bigger, capped shape.</summary>
    Crater,

    /// <summary>A structure clearing the cells inside its own volume. Bedrock refuses it.</summary>
    Build,
}

/// <summary>Whether one cell can be removed, and if not, why.</summary>
public enum Verdict : byte
{
    Removable,
    AlreadyAir,
    Bedrock,

    /// <summary>Never returned: ground isn't anchored any more (dig anywhere). Kept so old callers compile.</summary>
    [Obsolete("Ground under and around structures and POIs can be dug like any other (dig anywhere); this verdict is never returned.")]
    Anchored,

    OutsideWorld,
}

public readonly record struct RemovedCell(Int3 Cell, CellMaterial Material);

/// <summary>
/// A placed axis-aligned building block, as far as the terrain is concerned: a volume that
/// clears the ground inside it and that the ground around meets flush.
/// </summary>
public sealed record Structure(int Id, CellBox Box);

/// <summary>What one edit operation did. Removed is also the diff to send to other players.</summary>
public sealed class EditResult
{
    public readonly EditKind Kind;
    public readonly List<RemovedCell> Removed = new();

    /// <summary>Every chunk whose mesh changed: cells removed, and any structure volume registered.</summary>
    public readonly HashSet<Int3> DirtyChunks = new();

    public int AlreadyAir, Bedrock, Outside;

    /// <summary>Always 0: nothing is anchored any more (dig anywhere). Kept so old callers compile.</summary>
    [Obsolete("Ground is never anchored any more (dig anywhere); this count is always 0.")]
    public int Anchored;

    public EditResult(EditKind kind) => Kind = kind;

    public int Refused => Bedrock + Outside;

    /// <summary>The single most useful thing to tell the player about this edit.</summary>
    public Verdict Headline =>
        Removed.Count > 0 ? Verdict.Removable
        : Bedrock > 0 ? Verdict.Bedrock
        : Outside > 0 ? Verdict.OutsideWorld
        : Verdict.AlreadyAir;

    public EditLog ToDiff()
    {
        var log = new EditLog();
        foreach (RemovedCell r in Removed) log.Add(r.Cell);
        return log;
    }
}

public enum PlaceVerdict : byte
{
    Placed,
    InvalidSize,
    OutsideWorld,
    Overlaps,
    Bedrock,

    /// <summary>Never returned: POI ground takes blocks like any other. Kept so old callers compile.</summary>
    [Obsolete("POI ground isn't protected any more (dig anywhere); this verdict is never returned.")]
    AnchoredByPoi,
}

public sealed class PlaceResult
{
    public PlaceVerdict Verdict;
    public Structure Structure;
    public EditResult Cleared;
}

/// <summary>What applying a received or loaded diff did.</summary>
public sealed class ApplyReport
{
    public int Applied, AlreadyRemoved;

    /// <summary>Cells that can never be removed (bedrock, open air in the base terrain, outside the world).</summary>
    public readonly List<Int3> Invalid = new();

    public readonly HashSet<Int3> DirtyChunks = new();
}
