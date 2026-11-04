using Godot;

namespace Coop;

/// <summary>
/// The zones of the prototype. Every networked thing carries a zone ID, and a client only
/// loads (and only receives) its own zone. Each zone is a world of its own, so two zones can
/// use the same coordinates without ever touching.
/// </summary>
public static class Zones
{
    public const int Desert = 1;
    public const int Moon = 2;
    public static readonly int[] All = { Desert, Moon };

    public static string Name(int zone) => zone switch
    {
        Desert => "Desert surface",
        Moon => "Moon base",
        _ => "nowhere",
    };

    /// <summary>The debug transfer key cycles through the zones.</summary>
    public static int Next(int zone) => zone == Desert ? Moon : Desert;

    /// <summary>Where someone arriving in a zone stands, spread out a little by slot.</summary>
    public static Vector3 Arrival(int zone, int slot) =>
        (zone == Moon ? new Vector3(-12f, 0.05f, 30f) : new Vector3(-16f, 0.05f, 34f)) + new Vector3(slot % 4 * 1.5f, 0, 0);

    /// <summary>Which way arrivals face: toward the ships.</summary>
    public static float ArrivalYaw(int zone) => -0.42f;
}
