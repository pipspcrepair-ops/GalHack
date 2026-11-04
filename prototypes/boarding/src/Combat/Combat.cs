using Godot;

namespace Boarding;

/// <summary>Resolving the player's shots against guards, blocks and the world.</summary>
public static class Combat
{
    /// <summary>
    /// One hitscan pellet. Returns whether it hit an enemy target (for the hit marker), whether
    /// that killed it, whether it was a headshot, and where it stopped (for the tracer).
    /// </summary>
    public static (bool hit, bool killed, bool head, Vector3 point) PlayerShot(Main game, Vector3 origin, Vector3 dir, WeaponDef def, Player player)
    {
        Vector3 end = origin + dir * def.Range;
        var query = PhysicsRayQueryParameters3D.Create(origin, end, Layers.Shootable, new Godot.Collections.Array<Rid> { player.GetRid() });
        var result = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0) return (false, false, false, end);

        var point = (Vector3)result["position"];
        var normal = (Vector3)result["normal"];
        Node collider = result["collider"].As<Node>();
        float damage = def.Damage * Falloff(def, origin.DistanceTo(point));

        switch (collider)
        {
            case Guard guard when !guard.Dead:
            {
                bool head = guard.IsHead(point);
                game.Effects.Impact(point, normal, Surface.Ichor, null);
                bool killed = guard.TakeHit(damage * (head ? def.Headshot : 1f), point, dir, head);
                return (true, killed, head, point);
            }
            case Block block:
            {
                // Effects first: the block may break and free itself.
                game.Effects.Impact(point, normal, block.Surface, block, def.Pellets > 1 ? 0.7f : 1f);
                bool enemyTarget = block is CoreBlock || block.Kind == BlockKind.Turret;
                bool broke = block.Damage(damage, point, normal);
                return (enemyTarget, enemyTarget && broke, false, point);
            }
            case Node3D body:
            {
                var surface = body.HasMeta("surface") ? (Surface)(int)body.GetMeta("surface") : Surface.Rock;
                game.Effects.Impact(point, normal, surface, body);
                return (false, false, false, point);
            }
        }
        return (false, false, false, point);
    }

    /// <summary>Shotgun pellets lose punch past close range; everything else holds its damage.</summary>
    static float Falloff(WeaponDef def, float distance)
    {
        if (def.Pellets <= 1) return 1f;
        return Mathf.Lerp(1f, 0.35f, Mathf.Clamp((distance - 10f) / 25f, 0f, 1f));
    }
}

/// <summary>
/// Taking components off a ship whole with the multi-tool. The rule: while the enemy core
/// is alive, every component is locked to it and removal is refused. Kill the core (the ship
/// goes derelict) or hijack it (the ship is yours) and intact components come away whole.
/// </summary>
public static class Salvage
{
    public static string ItemName(Block block) => block.Kind switch
    {
        BlockKind.Turret => "Vharn turret",
        BlockKind.Generator => "Vharn generator",
        _ => block.Kind.ToString(),
    };

    public static bool CanRemove(Ship ship, Block block, out string reason)
    {
        if (!block.IsComponent) { reason = "not a removable component"; return false; }
        if (block.Destroyed) { reason = "destroyed"; return false; }
        if (ship.CoreAlive && ship.Hostile) { reason = "the enemy core is alive and locks it in place"; return false; }
        reason = "";
        return true;
    }

    /// <summary>Moves the component into the player's inventory. Returns false if refused.</summary>
    public static bool Remove(Main game, Block block)
    {
        if (!CanRemove(game.Ship, block, out _)) return false;
        game.Inventory.Add(ItemName(block));
        block.Detach();
        return true;
    }
}
