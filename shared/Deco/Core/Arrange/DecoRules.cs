using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>
/// What a furnished room keeps to, from the owner's walk round the first rooms (15 Nov) and planning practice: nothing taller
/// than a sill in front of a window; a fire only at a chimney breast, at the stack's foot, never by a window; what the room is
/// arranged round on a solid wall (under a sill at most); a clear walk a metre wide (0.9 m and more) from the doors to every piece's use side with its
/// drawers and doors open; a room under 2.5 m across lined along one wall; furniture on no more than half the floor. The planner
/// keeps every rule as it sets each piece down; the interiors check counts them.
/// </summary>
public static class DecoRules
{
    /// <summary>The walk's walker, raster cells across (1 m), and how deep a piece's use side is looked for in front of it.</summary>
    public const int Walker = 4;
    const float UseDepth = 0.3f;
    /// <summary>A room narrower than this is lined along one wall.</summary>
    public const float Narrow = 2.5f;
    /// <summary>The most of a room's floor its furniture may stand on.</summary>
    public const float MostFloor = 0.5f;

    /// <summary>What is sat on: pushed in and out as it is used, it never stands in the walk.</summary>
    public static bool Seat(DecoPiece p) => p.Roles.Any(r => r is "chair" or "stool" or "seat" or "sofa" or "bench-seat" or "pew" or "seat-row" or "airline-seat" or "dining-chair");

    /// <summary>What has no side it is used from (a plant, a lamp, a bin, a pillar, a curtain, a rug).</summary>
    static bool Idle(DecoPiece p) => p.WalkOver || p.Roles.Any(r => r is "plant" or "planter" or "lamp" or "bin" or "pillar" or "statue" or "curtain" or "partition" or "rug" or "support" or "flag" or "candles" or "barrier");

    /// <summary>What stands on the floor in the way.</summary>
    static IEnumerable<DecoPlacement> Standing(DecoForge forge, IEnumerable<DecoPlacement> placements) => placements.Where(pl => pl.Support < 0 && DecoLayout.Blocks(forge, pl));

    /// <summary>A placement's footprint as it stands open: its drawers out, its doors wide.</summary>
    public static DecoRect Open(DecoForge forge, DecoPlacement pl)
    {
        var (min, max) = DecoLayout.LocalBounds(forge, forge.Catalogue.Piece(pl.Piece), pl.State, pl.Fall, true);
        return DecoLayout.RectOf(min, max, pl.X, pl.Z, pl.Yaw);
    }

    /// <summary>
    /// The pieces whose use side no one gets to: a walker a metre across, from a doorway (from any open floor where the room has
    /// none), to the strip in front of the piece as it stands open. Every piece stands open at once; seats stand aside.
    /// </summary>
    public static List<DecoPlacement> Unreached(DecoForge forge, DecoRoom room, IEnumerable<DecoPlacement> placements)
    {
        var standing = Standing(forge, placements).ToList();
        var r = new DecoRaster(room.Width, room.Depth, DecoPlanner.Cell);
        foreach (DecoRect b in room.Blocked) r.Add(b, 1);
        foreach (DecoPlacement pl in standing.Where(pl => !Seat(forge.Catalogue.Piece(pl.Piece)))) r.Add(Open(forge, pl), 1);
        bool[] fit = r.Fits(Walker);
        bool[] reach = r.Reach(room.Doors.Count > 0 ? room.Doors.SelectMany(d => r.DoorStarts(room, d, Walker)) : Enumerable.Range(0, fit.Length), Walker, fit);
        var touched = new bool[fit.Length];
        for (int c = 0; c < reach.Length; c++)
            if (reach[c])
                for (int dj = 0; dj < Walker; dj++)
                    for (int di = 0; di < Walker; di++) touched[(c / r.Nx + dj) * r.Nx + c % r.Nx + di] = true;
        bool Used(DecoPlacement pl)
        {
            // The strip just in front of it (its front faces local -z), out past its open drawers.
            DecoRect o = Open(forge, pl);
            Vector2 front = -Vector2.Normalize(o.V);
            var strip = new DecoRect(o.Centre + front * (o.V.Length() + UseDepth / 2), o.U, front * (UseDepth / 2));
            var (lo, hi) = strip.Bounds();
            for (int j = Math.Max(0, (int)(lo.Y / r.Cell)); j <= Math.Min(r.Nz - 1, (int)(hi.Y / r.Cell)); j++)
                for (int i = Math.Max(0, (int)(lo.X / r.Cell)); i <= Math.Min(r.Nx - 1, (int)(hi.X / r.Cell)); i++)
                    if (touched[j * r.Nx + i] && strip.Contains(new Vector2((i + 0.5f) * r.Cell, (j + 0.5f) * r.Cell))) return true;
            return false;
        }
        return standing.Where(pl => forge.Catalogue.Piece(pl.Piece) is var p && !Seat(p) && !Idle(p) && !Used(pl)).ToList();
    }

    /// <summary>A footprint's along-wall span and its distance from a wall (offsets as a door's: x along north and south, z along west and east).</summary>
    static (float Lo, float Hi, float Off) Span(DecoRoom room, DecoRect r, DecoWall wall)
    {
        var (min, max) = r.Bounds();
        return wall switch
        {
            DecoWall.North => (min.X, max.X, min.Y),
            DecoWall.South => (min.X, max.X, room.Depth - max.Y),
            DecoWall.West => (min.Y, max.Y, min.X),
            _ => (min.Y, max.Y, room.Width - max.X),
        };
    }

    /// <summary>How far a footprint stands off a wall.</summary>
    public static float Off(DecoRoom room, DecoRect r, DecoWall wall) => Span(room, r, wall).Off;

    static readonly DecoWall[] Walls = { DecoWall.North, DecoWall.South, DecoWall.West, DecoWall.East };

    /// <summary>The wall a footprint stands against (within <paramref name="within"/> of it), the nearest first; null for one out in the room.</summary>
    public static DecoWall? Against(DecoRoom room, DecoRect r, float within = 0.15f) =>
        Walls.Select(w => (w, Span(room, r, w).Off)).Where(t => t.Off <= within).OrderBy(t => t.Off).Select(t => (DecoWall?)t.w).FirstOrDefault();

    static bool Overlaps(float lo, float hi, DecoDoor span, float margin) => lo < span.Offset + span.Width / 2 + margin && hi > span.Offset - span.Width / 2 - margin;

    /// <summary>
    /// Stands in front of a window, taller than its sill (within 0.6 m of its wall, across its width; a fire not even beside one).
    /// Low pieces (a sofa, a table, a desk) sit under a window; a shelf, a wardrobe, a cabinet, a fireplace never stands before one.
    /// </summary>
    public static bool BeforeWindow(DecoForge forge, DecoRoom room, DecoPlacement pl)
    {
        if (pl.Support >= 0 || forge.Catalogue.Piece(pl.Piece) is not { } p || p.WalkOver || p.Mount == DecoMount.Ceiling) return false;
        DecoRect r = DecoLayout.Footprint(forge, pl);
        float top = DecoLayout.Heights(forge, pl).Hi, beside = p.Has("hearth") ? 0.5f : 0f;
        return room.Windows.Any(w => Span(room, r, w.At.Wall) is var s && s.Off <= 0.6f && Overlaps(s.Lo, s.Hi, w.At, beside) && top > w.Sill + 0.05f);
    }

    /// <summary>A fire lit anywhere but at a chimney breast: against the breast's wall, within its width (a hand either side).</summary>
    public static bool OffFlue(DecoForge forge, DecoRoom room, DecoPlacement pl)
    {
        if (pl.Support >= 0 || !forge.Catalogue.Piece(pl.Piece).Has("hearth")) return false;
        DecoRect r = DecoLayout.Footprint(forge, pl);
        return !room.Flues.Any(f => Span(room, r, f.Wall) is var s && s.Off <= 0.3f && s.Lo >= f.Offset - f.Width / 2 - 0.5f && s.Hi <= f.Offset + f.Width / 2 + 0.5f);
    }

    /// <summary>
    /// Stands against a wall with a doorway in its width, or a window it rises above the sill of (<paramref name="top"/>: its
    /// height; a bed's head or a sofa's back may sit under a sill where a small room has no solid wall for it).
    /// </summary>
    public static bool OpeningBehind(DecoRoom room, DecoRect r, float top = float.MaxValue) =>
        Against(room, r, 0.3f) is DecoWall w && Span(room, r, w) is var s
        && room.Windows.Where(x => top > x.Sill + 0.05f).Select(x => x.At).Concat(room.Doors).Any(o => o.Wall == w && Overlaps(s.Lo, s.Hi, o, 0f));

    /// <summary>The share of a room's open floor its standing furniture covers.</summary>
    public static float Share(DecoForge forge, DecoRoom room, IEnumerable<DecoPlacement> placements) =>
        Standing(forge, placements).Sum(pl => DecoLayout.Footprint(forge, pl) is var r ? 4 * r.U.Length() * r.V.Length() : 0) / MathF.Max(1f, room.Width * room.Depth - room.Blocked.Count);

    /// <summary>The long wall a narrow room is lined along: the one with fewer doorways in it (null: the room is wide enough).</summary>
    public static DecoWall? Lining(DecoRoom room)
    {
        if (MathF.Min(room.Width, room.Depth) >= Narrow) return null;
        var (a, b) = room.Width >= room.Depth ? (DecoWall.North, DecoWall.South) : (DecoWall.West, DecoWall.East);
        return room.Doors.Count(d => d.Wall == b) < room.Doors.Count(d => d.Wall == a) ? b : a;
    }

    /// <summary>A narrow room with something standing off its one lined wall (down its middle or along the other).</summary>
    public static bool OffLining(DecoForge forge, DecoRoom room, IEnumerable<DecoPlacement> placements) =>
        Lining(room) is DecoWall w && Standing(forge, placements).Any(pl => Off(room, DecoLayout.Footprint(forge, pl), w) > 0.15f);
}
