namespace Boarding;

/// <summary>Physics layers.</summary>
public static class Layers
{
    /// <summary>The ground and rocks.</summary>
    public const uint World = 1u << 0;

    /// <summary>Ship blocks: hull, walls, doors, components. Each one is its own body with hit points.</summary>
    public const uint Ship = 1u << 1;

    /// <summary>The player's capsule.</summary>
    public const uint Player = 1u << 2;

    /// <summary>Guard capsules. Guards don't collide with each other, so a squad never jams in a door.</summary>
    public const uint Guards = 1u << 3;

    /// <summary>Broken-off chunks: they bounce off the world and ship, but never block anyone.</summary>
    public const uint Debris = 1u << 4;

    /// <summary>What bullets stop on.</summary>
    public const uint Shootable = World | Ship | Guards;

    /// <summary>What blocks line of sight (guards and the turret looking for the player).</summary>
    public const uint Sight = World | Ship;
}
