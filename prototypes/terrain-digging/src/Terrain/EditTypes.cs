using System.Collections.Generic;

namespace TerrainDigging.Terrain;

/// <summary>Who is removing terrain. The rules differ slightly for each.</summary>
public enum EditKind : byte
{
    /// <summary>A player's survival tool. Refused on bedrock and anchored ground.</summary>
    Tool,

    /// <summary>A crash or liftoff blast. Same rules as the tool, but a bigger, capped shape.</summary>
    Crater,

    /// <summary>A building block clearing its own volume. Only POI anchors and bedrock refuse it.</summary>
    Build,
}

/// <summary>Whether one cell can be removed, and if not, why.</summary>
public enum Verdict : byte
{
    Removable,
    AlreadyAir,
    Bedrock,
    Anchored,
    OutsideWorld,
}

public readonly record struct RemovedCell(Int3 Cell, CellMaterial Material);

/// <summary>A generated POI or a placed structure, protecting the ground in Box.</summary>
public readonly record struct AnchorZone(CellBox Box, bool IsPoi, int OwnerId);

/// <summary>A placed building block, as far as the terrain is concerned: a volume that anchors the ground.</summary>
public sealed record Structure(int Id, CellBox Box);

/// <summary>What one edit operation did. Removed is also the diff to send to other players.</summary>
public sealed class EditResult
{
    public readonly EditKind Kind;
    public readonly List<RemovedCell> Removed = new();
    public readonly HashSet<Int3> DirtyChunks = new();
    public int AlreadyAir, Bedrock, Anchored, Outside;

    public EditResult(EditKind kind) => Kind = kind;

    public int Refused => Bedrock + Anchored + Outside;

    /// <summary>The single most useful thing to tell the player about this edit.</summary>
    public Verdict Headline =>
        Removed.Count > 0 ? Verdict.Removable
        : Bedrock > 0 ? Verdict.Bedrock
        : Anchored > 0 ? Verdict.Anchored
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
