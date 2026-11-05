namespace GalHack.Physics;

/// <summary>
/// The one collision-layer table. Every prototype that adopts the shared module uses these
/// bits and masks and nothing else.
///
/// Reconciled from the prototypes: walkable-ship (World 1, ShipHull 2, Interior 4),
/// ship-handling (World 1, ShipHull 2, Water 4) and terrain-digging (Terrain 1, Structure 2,
/// Player 4, Props 8). World and Hull keep the values all three agreed on; Interior keeps
/// walkable-ship's 4 because cabin worlds are proven with it; Water, Player and Props move up
/// so no bit means two things. Interior only ever appears inside a cabin world, which is a
/// separate physics space, so it can never meet the others.
/// </summary>
public static class Layers
{
    /// <summary>Terrain, rocks, POIs: anything fixed to the planet.</summary>
    public const uint World = 1u << 0;

    /// <summary>A structure's outer hull: vessels, bases and wrecks. One body per structure, never one per block.</summary>
    public const uint Hull = 1u << 1;

    /// <summary>Inside a cabin world: the motionless cabin copy, its cargo and anyone aboard.</summary>
    public const uint Interior = 1u << 2;

    /// <summary>
    /// Water surfaces. Only hover probes see them: hover engines ride on water; everything else goes
    /// into it (water is a volume, see <see cref="IWaterField"/>).
    /// </summary>
    public const uint Water = 1u << 3;

    /// <summary>Players walking in the main world.</summary>
    public const uint Player = 1u << 4;

    /// <summary>Crates, creatures and loose cargo.</summary>
    public const uint Props = 1u << 5;

    // ---- Masks: what each kind of body collides with ------------------------------------

    /// <summary>
    /// Vessels sweep against the world and other hulls only. Players and props get out of a
    /// hull's way by themselves (their own move depenetrates them, or the engine pushes them),
    /// so a crate on the deck can never stop a ship lifting off.
    /// </summary>
    public const uint VesselMask = World | Hull;

    /// <summary>Wrecks are free rigid bodies: they land on the world, hulls and props.</summary>
    public const uint WreckMask = World | Hull | Props;

    public const uint PlayerMask = World | Hull | Props;
    public const uint PropMask = World | Hull | Player | Props;

    /// <summary>Hover engines see ground, hulls and water: they ride on a water surface.</summary>
    public const uint ProbeMask = World | Hull | Water;

    /// <summary>
    /// Landing legs stand on solid things only. Water is a volume, not a floor: a vessel let down
    /// onto a lake goes under, and its feet find the bed.
    /// </summary>
    public const uint GearMask = World | Hull;

    /// <summary>Everything solid in the main world.</summary>
    public const uint Solid = World | Hull;

    /// <summary>Inside a cabin world everything is on the one interior layer.</summary>
    public const uint CabinMask = Interior;
}
