namespace Invasions.World;

/// <summary>Physics layers. Shots pick what they can hit by mask; nobody shoots their own side.</summary>
public static class Layers
{
    /// <summary>The ground and rocks.</summary>
    public const uint World = 1u << 0;
    /// <summary>Base blocks, player and Vharn alike: walls, doors, containers, turrets, the core.</summary>
    public const uint Blocks = 1u << 1;
    public const uint Player = 1u << 2;
    /// <summary>Vharn foot soldiers.</summary>
    public const uint Invaders = 1u << 3;
    /// <summary>Vehicle crew and the garrison core: things only the defenders shoot.</summary>
    public const uint InvaderHitboxes = 1u << 4;
    /// <summary>NPC vehicle hulls. They stop the player and the player's bullets.</summary>
    public const uint Vehicles = 1u << 5;
    /// <summary>Spill crates.</summary>
    public const uint Loot = 1u << 6;

    public const uint DefenderShots = World | Blocks | Invaders | InvaderHitboxes | Vehicles;
    public const uint InvaderShots = World | Blocks | Player;
}
