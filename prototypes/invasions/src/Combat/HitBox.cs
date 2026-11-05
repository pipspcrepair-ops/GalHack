using System;
using Godot;
using Invasions.Model;

namespace Invasions.Combat;

/// <summary>
/// A shootable collision shape that forwards hits to whatever owns it: a base block, a vehicle crew
/// seat, a turret head, an enemy base core. An AnimatableBody so it can ride on moving vehicles.
/// </summary>
public partial class HitBox : AnimatableBody3D, IDamageable
{
    public Team Team { get; set; }
    public TargetClass Class { get; set; } = TargetClass.Capital;
    public Func<bool> Alive;
    public Action<float, Vector3, Team> Damaged;
    /// <summary>Where to aim, relative to this body (the centre of the shape by default).</summary>
    public Vector3 AimOffset;

    public bool IsAlive => Alive?.Invoke() ?? true;
    public Vector3 AimPoint => GlobalTransform * AimOffset;

    public void TakeDamage(float amount, Vector3 from, Team by)
    {
        if (by == Team || !IsAlive) return; // nobody hurts their own side
        Damaged?.Invoke(amount, from, by);
    }

    public static HitBox Create(Shape3D shape, Vector3 position, uint layer, Team team, Func<bool> alive, Action<float, Vector3, Team> damaged)
    {
        var box = new HitBox
        {
            Team = team,
            Alive = alive,
            Damaged = damaged,
            CollisionLayer = layer,
            CollisionMask = 0,
            SyncToPhysics = false,
            Position = position,
        };
        box.AddChild(new CollisionShape3D { Shape = shape });
        return box;
    }
}
