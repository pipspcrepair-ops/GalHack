using System;
using System.Collections.Generic;
using System.Numerics;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>A room's walls. The interior runs x from 0 (west) to Width (east) and z from 0 (north) to Depth (south).</summary>
public enum DecoWall { North, South, West, East }

/// <summary>A doorway: which wall, where along it (metres from the wall's low end, x for north/south, z for west/east, to the door's centre) and how wide.</summary>
public readonly record struct DecoDoor(DecoWall Wall, float Offset, float Width);

/// <summary>
/// A room (or an outdoor site) to dress: its size, doorways, theme set, the place's state and age,
/// and the seed. Sites (roads, rail lines, airfields) are rooms with no walls whose "doors" are
/// where the road or line runs out, and whose clear path must be wide enough to drive.
/// </summary>
public sealed class DecoRoom
{
    public float Width, Depth, Height = 3f;
    public List<DecoDoor> Doors = new();
    /// <summary>Patches of the floor nothing may stand on (a stair, a cache, a hole, a heap the years left): kept clear like a doorway.</summary>
    public List<DecoRect> Blocked = new();
    /// <summary>The theme set (deco.json) the room is furnished from.</summary>
    public string Set;
    /// <summary>The POI room id it came from (rooms.txt), if any, and its tags.</summary>
    public string RoomId;
    /// <summary>The named recipe (a mix of the set) it is furnished by, if any: set by <see cref="DecoPoi.Dress"/> from the room's id and the place.</summary>
    public string Recipe;
    public List<string> Tags = new();
    /// <summary>The POI's state: active, abandoned, infested or ruined.</summary>
    public string State = "abandoned";
    /// <summary>How long since use: the default follows the state (active places are fresh, others old-world).</summary>
    public DecoAge? Age;
    /// <summary>Open to the weather (outdoors, or a breached room): things rot and rust far faster.</summary>
    public bool Exposed;
    /// <summary>Only pieces at or below this tech level (null: any).</summary>
    public DecoTech? Tech;
    /// <summary>Clear width of the path that must join every doorway (1 m to walk, about 3 m to drive).</summary>
    public float Agent = 1f;
    /// <summary>An outdoor site: <see cref="Site"/> names its kind (jam, lot, crash...).</summary>
    public string Site;
    /// <summary>What half-buried things on a site are buried in (the biome's sand or snow); null lets the seed choose, once for the whole site.</summary>
    public DecoCover? Drift;
    /// <summary>A style kit's tint for the furniture (the walls' colours), or null for the set's own.</summary>
    public DecoRgb? KitBody, KitTrim;
    public ulong Seed = 1;

    public DecoAge AgeOrDefault => Age ?? (State == "active" ? DecoAge.Fresh : DecoAge.OldWorld);
    public bool Outdoor => Site != null;

    /// <summary>The middle of a doorway on its wall, and the inward direction.</summary>
    public (Vector2 At, Vector2 In) DoorPoint(DecoDoor d) => d.Wall switch
    {
        DecoWall.North => (new Vector2(d.Offset, 0), new Vector2(0, 1)),
        DecoWall.South => (new Vector2(d.Offset, Depth), new Vector2(0, -1)),
        DecoWall.West => (new Vector2(0, d.Offset), new Vector2(1, 0)),
        _ => (new Vector2(Width, d.Offset), new Vector2(-1, 0)),
    };

    /// <summary>The clear apron inside a doorway: as wide as the door plus a margin, <paramref name="deep"/> metres in.</summary>
    public DecoRect Apron(DecoDoor d, float deep = 1f, float margin = 0.15f)
    {
        var (at, inward) = DoorPoint(d);
        Vector2 along = new(inward.Y, -inward.X);
        Vector2 c = at + inward * (deep / 2);
        float half = d.Width / 2 + margin;
        return new DecoRect(c, along * half, inward * (deep / 2));
    }

    public override string ToString() => $"{RoomId ?? Site ?? Set} {Width:0.#}x{Depth:0.#} m, {Doors.Count} doors, {State}";
}

/// <summary>An oriented rectangle on the floor: centre and two half-axis vectors.</summary>
public readonly record struct DecoRect(Vector2 Centre, Vector2 U, Vector2 V)
{
    public IEnumerable<Vector2> Corners()
    {
        yield return Centre - U - V; yield return Centre + U - V; yield return Centre + U + V; yield return Centre - U + V;
    }

    public DecoRect Shrink(float by)
    {
        float lu = U.Length(), lv = V.Length();
        return new DecoRect(Centre, lu > by ? U * ((lu - by) / lu) : U * 0.01f, lv > by ? V * ((lv - by) / lv) : V * 0.01f);
    }

    public (Vector2 Min, Vector2 Max) Bounds()
    {
        Vector2 e = new(MathF.Abs(U.X) + MathF.Abs(V.X), MathF.Abs(U.Y) + MathF.Abs(V.Y));
        return (Centre - e, Centre + e);
    }

    /// <summary>Separating-axis test for two oriented rectangles.</summary>
    public bool Overlaps(DecoRect o)
    {
        foreach (Vector2 axis in new[] { U, V, o.U, o.V })
        {
            if (axis.LengthSquared() < 1e-12f) continue;
            Vector2 n = Vector2.Normalize(axis);
            float ca = Vector2.Dot(Centre, n), ra = MathF.Abs(Vector2.Dot(U, n)) + MathF.Abs(Vector2.Dot(V, n));
            float cb = Vector2.Dot(o.Centre, n), rb = MathF.Abs(Vector2.Dot(o.U, n)) + MathF.Abs(Vector2.Dot(o.V, n));
            if (MathF.Abs(ca - cb) >= ra + rb) return false;
        }
        return true;
    }

    public bool Contains(Vector2 p)
    {
        Vector2 d = p - Centre;
        float lu = U.LengthSquared(), lv = V.LengthSquared();
        return (lu < 1e-12f || MathF.Abs(Vector2.Dot(d, U)) <= lu) && (lv < 1e-12f || MathF.Abs(Vector2.Dot(d, V)) <= lv);
    }
}

/// <summary>One piece placed in a room: what, in which state, and where (its footprint centre on the floor, its height and its yaw).</summary>
public sealed class DecoPlacement
{
    public string Piece;
    public DecoState State;
    public DecoFall Fall;
    public bool Doors;
    public bool Powered;
    public DecoCover Cover;
    public string Palette;
    public DecoAge Age;
    public bool Exposed;
    /// <summary>Its origin (the middle of its footprint as built) on the floor, in room metres; Y is its height off the floor.</summary>
    public float X, Y, Z;
    /// <summary>Degrees about +Y: 0 has its back to the south wall (front facing north), 90 its back to the east.</summary>
    public float Yaw;
    /// <summary>Which template use it belongs to (-1 for a lone piece), and the role it plays there.</summary>
    public int Group = -1;
    public string Role;
    /// <summary>The placement it stands on (a monitor on a desk), or -1.</summary>
    public int Support = -1;
    /// <summary>Parts of one machine (an airliner's sections, a train on its rails) may overlap each other.</summary>
    public int Assembly = -1;
    /// <summary>What the disorder pass did to it ("pushed back from the table", "dropped mid-aisle"...).</summary>
    public string Note;

    public DecoLook Look => new(Piece, State, Fall, Powered, Cover, Palette, Doors, Age, Exposed);
    public Matrix4x4 Transform => DecoGeo.YawMatrix(Yaw) * DecoGeo.Move(X, Y, Z);
    public DecoPlacement Clone() => (DecoPlacement)MemberwiseClone();
    public override string ToString() => $"{Piece} {State}{(Fall != DecoFall.None ? "/" + Fall : "")} at ({X:0.00}, {Y:0.00}, {Z:0.00}) yaw {Yaw:0}";
}

/// <summary>A wire strung between two placements' anchor points (or hanging from one when the other has fallen), in room coordinates.</summary>
public sealed class DecoPlanCable
{
    public int From, To = -1;
    public string FromAnchor, ToAnchor;
    public Vector3 A, B;
    public bool Spanned = true;
    public float Slack = 0.02f, Radius = 0.02f;

    public DecoCable AsCable() => new() { A = A, B = B, Spanned = Spanned, Slack = Slack, Radius = Radius, Tail = 1.5f, Slot = DecoSlot.Dark };
}

/// <summary>A dressed room: its placements, any wires between them, and what happened.</summary>
public sealed class DecoPlan
{
    public DecoRoom Room;
    public readonly List<DecoPlacement> Placements = new();
    public readonly List<DecoPlanCable> Cables = new();
    public readonly List<string> Notes = new();

    /// <summary>A text line per placement: two plans with the same seed print the same.</summary>
    public string Fingerprint() => string.Join("\n", Placements.ConvertAll(p => $"{p}|{p.Doors}|{p.Powered}|{p.Group}|{p.Support}|{p.Note}"))
        + "\n" + string.Join("\n", Cables.ConvertAll(c => $"{c.From}:{c.FromAnchor}-{c.To}:{c.ToAnchor} {c.Spanned}"));
}
