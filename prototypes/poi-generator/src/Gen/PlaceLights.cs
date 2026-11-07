#nullable enable
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A place's lights set to its mood, in one call at the end of a spawn: dying places flicker, hostile
/// ones pulse red, lived-in ones glow warm. The one place light colour and animation are decided, so the
/// player's light API (colour and steady, pulse, flicker, strobe or colour fade) can take it over here.
/// </summary>
public static class PlaceLights
{
    static readonly Rgb Warm = new(0xff, 0xc8, 0x88), Alarm = new(0xe0, 0x2a, 0x1e);

    public enum Feel : byte { AsBuilt, Dying, Hostile, LivedIn }

    /// <summary>How a place feels, from its state, its mood and who is in it.</summary>
    public static Feel Of(PoiInstance inst) =>
        inst.Mood == Mood.Hostile ? Feel.Hostile
        : inst.Townsfolk is { People.Count: > 0 } || inst.Occupancy is { Main: Occupant.Squatters or Occupant.Keepers or Occupant.Cult } ? Feel.LivedIn
        : inst.State is PoiState.Abandoned or PoiState.Ruined ? Feel.Dying
        : Feel.AsBuilt;

    public static void Tune(PoiInstance inst)
    {
        Feel feel = Of(inst);
        foreach (LightSpot l in inst.Lights.Where(l => l.Mode is not (LightMode.Bloom or LightMode.Beacon or LightMode.Dead)))
            Set(l, feel);
    }

    /// <summary>One light to a feel. Alien glows and beacons keep their own.</summary>
    public static void Set(LightSpot l, Feel feel)
    {
        switch (feel)
        {
            case Feel.Dying: l.Mode = LightMode.Flicker; break;
            case Feel.Hostile: l.Mode = LightMode.Pulse; l.Color = Alarm; break;
            case Feel.LivedIn: l.Mode = LightMode.Steady; l.Color = l.Color.Lerp(Warm, 0.6f); break;
        }
    }
}
