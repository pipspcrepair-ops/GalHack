using System;
using System.Runtime.CompilerServices;
using Godot;

namespace Wildlife;

/// <summary>
/// A procedural rig: turns a creature's motion and intentions into a world transform per
/// piece. There are only six (legged, chain, hopper, winged, gas bag, swarm); all the variety
/// comes from the parts and proportions fed into them, so animation stays reliable however
/// weird the species. Pure C#: the view copies <see cref="Pose"/> onto meshes, the tests
/// check it for NaNs, floating feet and runaway bodies.
/// </summary>
public abstract class Rig
{
    public readonly Species Species;
    public readonly RigLayout Layout;
    public readonly Transform3D[] Pose;

    /// <summary>Spheres (xyz centre, w radius) that bullets and blades test against.</summary>
    public readonly Vector4[] Hits = new Vector4[12];
    public int HitCount;
    public Vector3 BoundsCenter;
    public float BoundsRadius = 1f;

    /// <summary>Height of the body's centre above the ground under it, for the tests.</summary>
    public float BodyClearance;

    // Intentions from the brain, smoothed so poses never snap.
    protected float Crouch, Hidden, Graze, Rearing, Lunging, Bracing, Dead;
    protected float LookYaw;
    float _prevYaw;
    protected float YawRate;

    static readonly ConditionalWeakTable<Species, RigLayout> Layouts = new();

    protected Rig(Species species, Func<Species, RigLayout> build)
    {
        Species = species;
        Layout = Layouts.GetValue(species, s => build(s));
        Pose = new Transform3D[Layout.Pieces.Count];
        for (int i = 0; i < Pose.Length; i++) Pose[i] = Transform3D.Identity;
    }

    public static Rig Create(Species s, Creature c) => s.Rig switch
    {
        RigKind.Legged => new LeggedRig(s),
        RigKind.Chain => new ChainRig(s),
        RigKind.Hopper => new HopRig(s),
        RigKind.Winged => new WingedRig(s),
        RigKind.GasBag => new GasBagRig(s),
        _ => new SwarmRig(s, c),
    };

    /// <summary>Snap to a resting pose at the creature's spawn point (feet planted, chains laid out).</summary>
    public abstract void Reset(WildSim w, Creature c);

    public abstract void Update(WildSim w, Creature c, float dt);

    // ---- Feet, for the contact test ---------------------------------------------------

    public virtual int FootCount => 0;
    public virtual bool FootPlanted(int i) => false;
    public virtual Vector3 FootTarget(int i) => Vector3.Zero;
    public virtual Vector3 FootDrawn(int i) => Vector3.Zero;

    // ---- Shared helpers ---------------------------------------------------------------

    protected void SmoothIntents(Creature c, float dt)
    {
        Crouch = Geo.Smooth(Crouch, c.WantCrouch, 4f, dt);
        Hidden = Geo.Smooth(Hidden, c.WantHidden, c.WantHidden > Hidden ? 1.5f : 6f, dt); // erupt fast, settle slowly
        Graze = Geo.Smooth(Graze, c.WantGraze, 2.5f, dt);
        Rearing = Geo.Smooth(Rearing, c.Rear, 10f, dt);
        Lunging = Geo.Smooth(Lunging, c.Lunge, 14f, dt);
        Bracing = Geo.Smooth(Bracing, c.Brace, 6f, dt);
        Dead = Mathf.MoveToward(Dead, c.Dead ? 1f : 0f, dt * 1.4f);

        float yawDelta = Geo.WrapAngle(c.Yaw - _prevYaw);
        _prevYaw = c.Yaw;
        YawRate = Geo.Smooth(YawRate, dt > 0f ? yawDelta / dt : 0f, 5f, dt);

        float look = 0f;
        if (c.LookAt is { } at && !c.Dead)
        {
            Vector3 to = WildSim.Flat(at - c.Position);
            if (to.LengthSquared() > 0.01f) look = Math.Clamp(Geo.WrapAngle(MathF.Atan2(-to.X, -to.Z) - c.Yaw), -1.1f, 1.1f);
        }
        else look = MathF.Sin(c.Age * 0.31f + c.Id) * 0.3f; // idle glances
        LookYaw = Geo.Smooth(LookYaw, look, 3f, dt);
    }

    protected void ResetIntents(Creature c)
    {
        _prevYaw = c.Yaw;
        Hidden = c.WantHidden;
        Crouch = c.WantCrouch;
    }

    protected void SetHit(int i, Vector3 center, float radius)
    {
        Hits[i] = new Vector4(center.X, center.Y, center.Z, radius);
        if (i >= HitCount) HitCount = i + 1;
    }

    protected void SetBounds(Vector3 center, float radius)
    {
        BoundsCenter = center;
        BoundsRadius = radius;
    }

    /// <summary>A pose that hides a piece (scaled to nothing).</summary>
    protected static Transform3D Collapsed(Vector3 at) => new(Basis.Identity.Scaled(Vector3.One * 0.001f), at);
}
