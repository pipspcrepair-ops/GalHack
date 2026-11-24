#nullable enable
namespace PoiGenerator.Gen;

/// <summary>
/// The one door a person walks through, the same in every place and on the player's grid: one large block, 2 m
/// wide and <see cref="Tall"/> high (design 708: a door is a whole block, never one and a half), so a 1.8 m
/// person passes under its head. Over it and for <see cref="Approach"/> metres in front of it nothing (eaves,
/// roof slopes, porch roofs, gutters, brackets, balconies) comes lower than <see cref="Headroom"/>, the door's
/// head and a metre more: where a roof would, the roof is raised or trimmed there, never the door made smaller.
/// A place may still open grander ways where its style calls for them (double, barn and garage doors), but no
/// person's door is ever lower than this.
/// </summary>
public static class DoorSize
{
    public const int Wide = 2, Tall = 2, Headroom = Tall + 1, Approach = 2;
}
