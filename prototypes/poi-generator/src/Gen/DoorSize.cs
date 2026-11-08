#nullable enable
namespace PoiGenerator.Gen;

/// <summary>
/// The one door a person walks through, the same in every place: 2 m wide and <see cref="Tall"/> high, a full
/// storey on the 1 m grid, so a 1.8 m person has 1.2 m over their head. Over it and for <see cref="Approach"/>
/// metres in front of it nothing (eaves, roof slopes, porch roofs, gutters, brackets, balconies) comes lower than
/// <see cref="Headroom"/>, the door's head and a metre more: where a roof would, the roof is raised or trimmed
/// there, never the door made smaller.
/// </summary>
public static class DoorSize
{
    public const int Wide = 2, Tall = 3, Headroom = Tall + 1, Approach = 2;
}
