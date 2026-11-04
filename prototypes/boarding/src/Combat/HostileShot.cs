using Godot;

namespace Boarding;

/// <summary>
/// The one way anything hostile shoots at the player: ask the shooter's <see cref="AimRamp"/>
/// where to aim (a guaranteed-miss warning shot, or a sample of the current cone), then launch
/// a projectile that travels at the weapon type's speed. Damage is passed through untouched.
/// </summary>
public static class HostileShot
{
    /// <summary>Half-width of the player's outline, for placing warning shots outside it.</summary>
    const float TargetRadius = 0.45f;

    public static Projectile Fire(Main game, CollisionObject3D shooter, Vector3 muzzle, AimRamp ramp,
        ShooterClass shooterClass, FireProfile weapon, float damage, Color color, float thickness)
    {
        Player player = game.Player;
        Vector3 center = player.GlobalPosition + Vector3.Up * (player.Crouched ? 0.6f : 1.05f);
        float baseline = game.Fire.BaselineSpread(shooterClass, TargetClass.OnFoot, weapon) * Conditions(game, player);
        Vector3 aim = ramp.AimPoint(muzzle, center, TargetRadius, baseline, game.Rng, out bool warning);
        ramp.Shot();

        var projectile = new Projectile
        {
            Game = game,
            Velocity = (aim - muzzle).Normalized() * weapon.Speed,
            Damage = damage,
            Warning = warning,
            Tracking = weapon.Tracking,
            Homing = warning ? null : player,
            Shooter = shooter.GetRid(),
            Color = color,
            Thickness = thickness,
            Life = 220f / weapon.Speed + 0.3f,
            Position = game.Effects.ToLocal(muzzle),
        };
        game.Effects.AddChild(projectile);
        game.OnHostileShot(projectile);
        return projectile;
    }

    /// <summary>Situations that widen the cone, from data: a sprinting or crouched target, darkness.</summary>
    static float Conditions(Main game, Player player)
    {
        HostileFire fire = game.Fire;
        float k = 1f;
        if (player.Sprinting) k *= fire.TargetSprinting;
        if (player.Crouched) k *= fire.TargetCrouched;
        if (game.Ship.Lights == LightMode.Blackout && player.IsInsideShip) k *= fire.LightsOut;
        return k;
    }
}
