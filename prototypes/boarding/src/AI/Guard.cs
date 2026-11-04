using System.Collections.Generic;
using Godot;

namespace Boarding;

public enum GuardRole { Trooper, Officer, Gunner, Enforcer }

/// <summary>Per-role numbers and paint. Health and damage come from the tunables.</summary>
public sealed record GuardType(GuardRole Role, float Health, float Damage, bool Heavy, float Scale, float SpeedFactor, Color Armour, Color Accent)
{
    public static GuardType For(GuardRole role, Tunables t) => role switch
    {
        GuardRole.Officer => new(role, t.OfficerHealth, t.OfficerDamage, false, 1.0f, 1.0f, Palette.Dark, Palette.Yellow),
        GuardRole.Gunner => new(role, t.GunnerHealth, t.GuardDamage, false, 1.0f, 1.0f, Palette.Orange, Palette.Dark),
        GuardRole.Enforcer => new(role, t.EnforcerHealth, t.EnforcerDamage, true, 1.22f, 0.75f, new Color("3d1410"), Palette.AlarmRed),
        _ => new(role, t.GuardHealth, t.GuardDamage, false, 1.0f, 1.0f, Palette.Oxblood, Palette.Bone),
    };
}

/// <summary>
/// A Vharn crew member: a capsule body that walks the ship's grid, a blocky alien figure
/// that flinches, staggers, ducks behind crates and falls when killed, and a rifle that
/// always shows a red aim laser before it fires. The decisions live in GuardAI.cs.
///
/// Guards are children of the ship and keep their path in ship-local space.
/// </summary>
public partial class Guard : CharacterBody3D
{
    static int _nextId = 1000;

    public Main Game;
    public Ship Ship;
    public GuardType Type;
    public readonly int Id = _nextId++;

    public float Health { get; private set; }
    public bool Dead { get; private set; }
    public bool Crouching { get; private set; }
    public bool HasKeycard => Type.Role == GuardRole.Officer;
    public int ShotsFired { get; private set; }

    Tunables T => Game.Tunables;
    float Height => 1.9f * Type.Scale;
    float CrouchedHeight => 1.15f * Type.Scale;

    CapsuleShape3D _capsule;
    CollisionShape3D _shape;
    Node3D _visual, _hips, _torso, _legL, _legR, _muzzle;
    MeshInstance3D _visor, _laser;
    StandardMaterial3D _laserMaterial;
    float _walkPhase, _flinch, _flinchVel, _crouchBlend, _deathTilt, _hitFlash;
    readonly List<(float time, float damage)> _recentDamage = new();
    float _clock;

    public override void _Ready()
    {
        Health = Type.Health;
        _capsule = new CapsuleShape3D { Radius = 0.36f * Type.Scale, Height = Height };
        _shape = new CollisionShape3D { Shape = _capsule, Position = Vector3.Up * Height * 0.5f };
        AddChild(_shape);
        CollisionLayer = Layers.Guards;
        CollisionMask = Layers.World | Layers.Ship;
        FloorSnapLength = 0.3f;
        // Ship floors are not moving platforms here (the moving-cabin prototype owns that), so
        // never inherit a platform's velocity: a floor body being freed must not fling anyone.
        PlatformOnLeave = PlatformOnLeaveEnum.DoNothing;
        BuildFigure();
        ReadyBrain();
    }

    // ---- Being hit ----------------------------------------------------------------------

    /// <summary>True if a world point on this guard is a headshot.</summary>
    public bool IsHead(Vector3 point) => ToLocal(point).Y > _capsule.Height * 0.8f;

    /// <summary>Takes a hit. Returns true if it killed the guard.</summary>
    public bool TakeHit(float damage, Vector3 point, Vector3 direction, bool head)
    {
        if (Dead) return false;
        Health -= damage;
        _hitFlash = 0.08f;
        _flinchVel += head ? 14f : 8f;
        _recentDamage.Add((_clock, damage));
        float recent = 0f;
        foreach (var (time, d) in _recentDamage) if (_clock - time < 0.5f) recent += d;
        _recentDamage.RemoveAll(r => _clock - r.time > 0.5f);

        if (Health <= 0f)
        {
            Die();
            return true;
        }
        OnHurt(direction, recent >= T.GuardStaggerDamage);
        return false;
    }

    void Die()
    {
        Dead = true;
        Health = 0f;
        _laser.Visible = false;
        _visor.MaterialOverride = Art.Flat(Palette.Dark);
        // Corpses stay where they fall but no longer block anyone or stop bullets.
        CollisionLayer = 0;
        Velocity = Vector3.Zero;
        OnDied();
    }

    // ---- Shooting -----------------------------------------------------------------------

    public Vector3 MuzzlePosition => _muzzle.GlobalPosition;

    /// <summary>Shows or hides the aim laser toward a world point (the telegraph before a burst).</summary>
    protected void ShowLaser(bool on, Vector3 target, float intensity = 1f)
    {
        _laser.Visible = on;
        if (!on) return;
        Vector3 from = _muzzle.GlobalPosition;
        float length = from.DistanceTo(target);
        Basis basis = Effects.Facing(Vector3.Zero, target - from).Basis;
        basis.Z *= length;   // stretch the unit beam along its own axis
        _laser.GlobalTransform = new Transform3D(basis, (from + target) * 0.5f);
        _laserMaterial.AlbedoColor = new Color(1f, 0.15f, 0.1f, 0.25f + 0.55f * intensity);
    }

    /// <summary>
    /// One rifle shot at the player. Accuracy rolls a miss offset; the shot is a real ray, so
    /// cover the player hides behind takes the hit instead.
    /// </summary>
    protected void FireAt(Player player, float accuracy)
    {
        ShotsFired++;
        Vector3 from = _muzzle.GlobalPosition;
        Vector3 target = player.GlobalPosition + Vector3.Up * (player.Crouched ? 0.7f : 1.2f);
        if (GD.Randf() > accuracy)
        {
            // A miss that still cracks past close enough to read as dangerous.
            Vector3 side = (target - from).Cross(Vector3.Up).Normalized();
            target += side * (GD.Randf() < 0.5f ? -1f : 1f) * (0.6f + GD.Randf() * 0.8f) + Vector3.Up * (GD.Randf() - 0.3f) * 0.8f;
        }
        Vector3 dir = (target - from).Normalized();
        Vector3 end = from + dir * 120f;
        var query = PhysicsRayQueryParameters3D.Create(from, end, Layers.World | Layers.Ship | Layers.Player, new Godot.Collections.Array<Rid> { GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        Game.Effects.Flash(from, Palette.VharnGlow, 0.45f * Type.Scale);
        Game.Noise.Emit(from, T.NoiseGunfire, NoiseKind.Gunfire, fromPlayer: false);
        if (hit.Count == 0)
        {
            Game.Effects.Tracer(from, end, Palette.VharnGlow, 0.05f, 0.09f);
            return;
        }
        var point = (Vector3)hit["position"];
        Game.Effects.Tracer(from, point, Palette.VharnGlow, 0.05f, 0.09f);
        Node collider = hit["collider"].As<Node>();
        if (collider is Player p)
        {
            p.TakeNpcHit(Id, Type.Damage, Type.Heavy, from);
        }
        else if (collider is Block block)
        {
            Game.Effects.Impact(point, (Vector3)hit["normal"], block.Surface, block, 0.8f);
            block.Damage(Type.Damage * 0.5f, point, (Vector3)hit["normal"]);
        }
        else if (collider is Node3D other)
        {
            var surface = other.HasMeta("surface") ? (Surface)(int)other.GetMeta("surface") : Surface.Rock;
            Game.Effects.Impact(point, (Vector3)hit["normal"], surface, other, 0.8f);
        }
    }

    // ---- Body ----------------------------------------------------------------------------

    protected void SetCrouch(bool crouch)
    {
        if (Crouching == crouch) return;
        Crouching = crouch;
        float h = crouch ? CrouchedHeight : Height;
        _capsule.Height = h;
        _shape.Position = Vector3.Up * h * 0.5f;
    }

    /// <summary>Animates the figure: stride, crouch, flinch spring, hit flash, falling over.</summary>
    protected void AnimateFigure(float dt, float speed, float yaw)
    {
        _clock += dt;
        _visual.Rotation = new Vector3(0, Mathf.LerpAngle(_visual.Rotation.Y, yaw, Mathf.Min(1f, dt * 10f)), 0);

        _walkPhase += dt * speed * 3.2f;
        float swing = Mathf.Sin(_walkPhase) * Mathf.Clamp(speed / 3f, 0f, 1f) * 0.6f;
        _legL.Rotation = new Vector3(swing, 0, 0);
        _legR.Rotation = new Vector3(-swing, 0, 0);

        _crouchBlend = Mathf.MoveToward(_crouchBlend, Crouching ? 1f : 0f, dt * 6f);
        _hips.Position = new Vector3(0, 0.95f - 0.42f * _crouchBlend, 0);
        _legL.Scale = _legR.Scale = new Vector3(1, 1f - 0.45f * _crouchBlend, 1);

        _flinchVel += (-_flinch * 180f - _flinchVel * 14f) * dt;
        _flinch += _flinchVel * dt;
        _torso.Rotation = new Vector3(Mathf.DegToRad(-_flinch * 2.5f), 0, 0);

        _hitFlash = Mathf.Max(0f, _hitFlash - dt);
        _visual.Scale = Vector3.One * Type.Scale * (_hitFlash > 0f ? 1.04f : 1f);

        if (Dead)
        {
            _deathTilt = Mathf.MoveToward(_deathTilt, 1f, dt * 2.6f);
            _visual.Rotation = new Vector3(Mathf.DegToRad(88f * Mathf.Ease(_deathTilt, 0.4f)), _visual.Rotation.Y, 0);
            _visual.Position = new Vector3(0, 0.15f * _deathTilt, 0);
        }
    }

    void BuildFigure()
    {
        var armour = Art.Flat(Type.Armour, 0.6f, 0.25f);
        var accent = Art.Flat(Type.Accent, 0.55f, 0.2f);
        var under = Art.Flat(new Color("1d1b22"), 0.8f);

        _visual = new Node3D { Name = "Figure" };
        AddChild(_visual);
        _visual.Scale = Vector3.One * Type.Scale;

        _hips = new Node3D { Position = new Vector3(0, 0.95f, 0) };
        _visual.AddChild(_hips);
        _legL = Limb(_hips, new Vector3(-0.13f, 0, 0), new Vector3(0.17f, 0.92f, 0.2f), under);
        _legR = Limb(_hips, new Vector3(0.13f, 0, 0), new Vector3(0.17f, 0.92f, 0.2f), under);

        _torso = new Node3D();
        _hips.AddChild(_torso);
        _torso.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.48f, 0.58f, 0.3f)), armour, new Vector3(0, 0.33f, 0)));
        _torso.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.5f, 0.08f, 0.32f)), accent, new Vector3(0, 0.12f, 0)));
        foreach (float x in new[] { -0.33f, 0.33f })
            _torso.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.2f, 0.14f, 0.34f)), accent, new Vector3(x, 0.6f, 0)));

        // A long Vharn skull with a crest and a glowing visor slit.
        var head = new Node3D { Position = new Vector3(0, 0.78f, 0) };
        _torso.AddChild(head);
        head.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.24f, 0.3f, 0.32f)), armour, new Vector3(0, 0.02f, 0.02f)));
        head.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.05f, 0.22f, 0.4f)), accent, new Vector3(0, 0.22f, 0.08f)));
        _visor = Art.Mesh(Art.BoxMesh(new Vector3(0.2f, 0.05f, 0.02f)), Art.Glow(Palette.VharnGlow, 4f), new Vector3(0, 0.04f, -0.15f), false);
        head.AddChild(_visor);
        if (Type.Role == GuardRole.Officer)
            head.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.3f, 0.06f, 0.36f)), Art.Flat(Palette.Yellow, 0.4f, 0.5f), new Vector3(0, 0.17f, 0.02f)));

        // Arms hold the rifle out in front, pointing down -Z.
        _torso.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.13f, 0.13f, 0.42f)), under, new Vector3(0.24f, 0.38f, -0.16f)));
        _torso.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.13f, 0.13f, 0.42f)), under, new Vector3(-0.12f, 0.38f, -0.26f)));
        var gun = new Node3D { Position = new Vector3(0.08f, 0.42f, -0.35f) };
        _torso.AddChild(gun);
        gun.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.09f, 0.12f, 0.68f)), Art.Flat(Palette.Dark, 0.4f, 0.6f), Vector3.Zero));
        gun.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.095f, 0.03f, 0.4f)), Art.Glow(Palette.VharnGlow, 1.5f), new Vector3(0, 0.07f, -0.05f), false));
        _muzzle = new Node3D { Position = new Vector3(0, 0, -0.38f) };
        gun.AddChild(_muzzle);

        _laserMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.15f, 0.1f, 0.6f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        _laser = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.018f, 0.018f, 1f) },
            MaterialOverride = _laserMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            TopLevel = true,
            Visible = false,
        };
        AddChild(_laser);
    }

    static Node3D Limb(Node3D parent, Vector3 pivot, Vector3 size, Material material)
    {
        var limb = new Node3D { Position = pivot };
        parent.AddChild(limb);
        limb.AddChild(Art.Mesh(Art.BoxMesh(size), material, Vector3.Down * size.Y * 0.5f));
        return limb;
    }
}
