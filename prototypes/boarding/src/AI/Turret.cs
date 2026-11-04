using Godot;

namespace Boarding;

/// <summary>
/// The exterior turret: a twin-barrelled sponson on the hull. It is a crewed station, not a
/// drone: it only fights while its gunner sits at the gunner station inside the ship. Kill the
/// gunner (or wreck the station) and it falls silent. After a hijack it stops being hostile;
/// once the core is dead it has no power. It is also a component: salvage it whole.
///
/// It fires on the player outside the hull, always after a long red telegraph, and its hits
/// are heavy: they open weak and ramp up through the no-instant-death rules.
/// </summary>
public partial class Turret : Block
{
    public const int AttackerId = 1;

    public Vector3 Outward = Vector3.Left;
    public Guard Gunner;

    public int ShotsFired { get; private set; }
    public bool Telegraphing { get; private set; }

    public bool IsCrewed => Gunner != null && IsInstanceValid(Gunner) && Gunner.Seated
        && Ship.GunnerStation != null && !Ship.GunnerStation.Destroyed;
    public bool Hostile => !Destroyed && Ship.Hostile && Ship.CoreAlive && IsCrewed;

    enum Step { Idle, Telegraph, Burst, Cooldown }

    Node3D _yawNode, _pitchNode, _muzzleL, _muzzleR;
    MeshInstance3D _eye, _laser;
    StandardMaterial3D _laserMaterial;
    Step _step;
    float _timer, _yaw, _pitch, _baseYaw, _sweep;
    int _burstLeft;
    bool _left;

    Tunables T => Ship.Game.Tunables;

    protected override void BuildVisual()
    {
        _baseYaw = Mathf.Atan2(-Outward.X, -Outward.Z);
        var dark = Art.Flat(Palette.Dark, 0.5f, 0.5f);
        var mount = new Node3D { Rotation = new Vector3(0, _baseYaw, 0) };
        AddChild(mount);
        mount.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(1.8f, 1.8f, 0.5f)), Art.Stripes(Palette.Yellow, Palette.Dark, 0.3f), new Vector3(0, 0, 0.75f)));
        mount.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(1.0f, 0.5f, 1.0f)), dark, new Vector3(0, -0.6f, 0.2f)));

        _yawNode = new Node3D();
        AddChild(_yawNode);
        _yawNode.AddChild(Art.Mesh(new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.7f, Height = 0.3f }, dark, new Vector3(0, -0.3f, 0)));
        _pitchNode = new Node3D();
        _yawNode.AddChild(_pitchNode);
        _pitchNode.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(1.3f, 0.8f, 1.2f)), Art.Flat(Palette.Oxblood, 0.6f, 0.3f), new Vector3(0, 0.1f, 0)));
        _pitchNode.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(1.32f, 0.14f, 1.22f)), Art.Flat(Palette.Yellow, 0.5f), new Vector3(0, 0.3f, 0)));
        foreach (float x in new[] { -0.3f, 0.3f })
            _pitchNode.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.16f, 0.16f, 1.7f)), dark, new Vector3(x, 0.05f, -1.2f)));
        _muzzleL = new Node3D { Position = new Vector3(-0.3f, 0.05f, -2.1f) };
        _muzzleR = new Node3D { Position = new Vector3(0.3f, 0.05f, -2.1f) };
        _pitchNode.AddChild(_muzzleL);
        _pitchNode.AddChild(_muzzleR);
        _eye = Art.Mesh(Art.BoxMesh(new Vector3(0.3f, 0.14f, 0.06f)), Art.Glow(Palette.AlarmRed, 4f), new Vector3(0, 0.3f, -0.62f), false);
        _pitchNode.AddChild(_eye);

        _laserMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.2f, 0.1f, 0.7f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        _laser = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.04f, 1f) },
            MaterialOverride = _laserMaterial,
            TopLevel = true,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_laser);
        _yaw = _baseYaw;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Destroyed) return;
        float dt = (float)delta;
        Telegraphing = false;

        if (!Hostile)
        {
            // Silent: barrels droop, the eye goes dark (or teal once the ship is the player's).
            _step = Step.Idle;
            _laser.Visible = false;
            _pitch = Mathf.MoveToward(_pitch, -0.35f, dt * 0.5f);
            _eye.MaterialOverride = Ship.OwnerFaction == Faction.Player && !Ship.Rebooting ? Art.Glow(Palette.TealGlow, 2f) : Art.Flat(Palette.Dark);
            Pose();
            return;
        }
        _eye.MaterialOverride = Art.Glow(Palette.AlarmRed, 4f);

        Player player = Ship.Game.Player;
        Vector3 target = player.GlobalPosition + Vector3.Up * (player.Crouched ? 0.7f : 1.2f);
        bool engage = !player.Health.Dead && !player.IsInsideShip && !Ship.Game.PlayerHidden
            && GlobalPosition.DistanceTo(target) < T.TurretRange && InArc(target) && Clear(target);

        if (!engage)
        {
            _step = Step.Idle;
            _laser.Visible = false;
            _sweep += dt * 0.4f;
            Aim(_baseYaw + Mathf.Sin(_sweep) * 0.9f, 0f, dt);
            Pose();
            return;
        }

        // Track the player, then telegraph, then fire.
        Vector3 local = ToLocal(target);
        float wantYaw = Mathf.Atan2(-local.X, -local.Z);
        float wantPitch = Mathf.Atan2(local.Y, new Vector2(local.X, local.Z).Length());
        bool onTarget = Aim(wantYaw, wantPitch, dt);
        _timer -= dt;
        switch (_step)
        {
            case Step.Idle:
                if (onTarget)
                {
                    _step = Step.Telegraph;
                    _timer = T.TurretTelegraph;
                    Ship.Game.Alarm.ReportSighting(Ship.ToLocal(player.GlobalPosition));
                }
                break;
            case Step.Telegraph:
                Telegraphing = true;
                ShowLaser(target, 1f - _timer / T.TurretTelegraph);
                if (_timer <= 0f)
                {
                    _step = Step.Burst;
                    _burstLeft = T.TurretBurst;
                    _timer = 0f;
                }
                break;
            case Step.Burst:
                ShowLaser(target, 1f);
                if (_timer <= 0f)
                {
                    Fire(player, target);
                    _burstLeft--;
                    _timer = T.TurretBurstInterval;
                    if (_burstLeft <= 0)
                    {
                        _step = Step.Cooldown;
                        _timer = T.TurretCooldown;
                    }
                }
                break;
            case Step.Cooldown:
                _laser.Visible = false;
                if (_timer <= 0f) _step = Step.Idle;
                break;
        }
        Pose();
    }

    bool InArc(Vector3 target)
    {
        Vector3 to = ToLocal(target);
        return new Vector2(to.X, to.Z).Normalized().Dot(new Vector2(Outward.X, Outward.Z)) > -0.2f;
    }

    bool Clear(Vector3 target)
    {
        Vector3 from = _pitchNode.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(from, target, Layers.Sight, new Godot.Collections.Array<Rid> { GetRid() });
        return GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    bool Aim(float yaw, float pitch, float dt)
    {
        float turn = T.TurretTurnSpeed * dt;
        float dy = Mathf.AngleDifference(_yaw, yaw);
        _yaw += Mathf.Clamp(dy, -turn, turn);
        _pitch = Mathf.MoveToward(_pitch, Mathf.Clamp(pitch, -0.6f, 0.7f), turn);
        return Mathf.Abs(Mathf.AngleDifference(_yaw, yaw)) < 0.06f && Mathf.Abs(_pitch - pitch) < 0.1f;
    }

    void Pose()
    {
        _yawNode.Rotation = new Vector3(0, _yaw, 0);
        _pitchNode.Rotation = new Vector3(_pitch, 0, 0);
    }

    void ShowLaser(Vector3 target, float intensity)
    {
        Vector3 from = _eye.GlobalPosition;
        Basis basis = Effects.Facing(Vector3.Zero, target - from).Basis;
        basis.Z *= from.DistanceTo(target);
        _laser.GlobalTransform = new Transform3D(basis, (from + target) * 0.5f);
        _laser.Visible = true;
        _laserMaterial.AlbedoColor = new Color(1f, 0.2f, 0.1f, 0.2f + 0.7f * Mathf.Clamp(intensity, 0f, 1f));
    }

    void Fire(Player player, Vector3 target)
    {
        ShotsFired++;
        _left = !_left;
        Vector3 from = (_left ? _muzzleL : _muzzleR).GlobalPosition;
        if (GD.Randf() > T.TurretAccuracy)
            target += new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.3f, GD.Randf() - 0.5f) * 3f;
        var query = PhysicsRayQueryParameters3D.Create(from, from + (target - from).Normalized() * (T.TurretRange + 10f),
            Layers.World | Layers.Ship | Layers.Player, new Godot.Collections.Array<Rid> { GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        var effects = Ship.Game.Effects;
        effects.Flash(from, new Color("ff7b2a"), 1.1f);
        Vector3 end = hit.Count > 0 ? (Vector3)hit["position"] : from + (target - from).Normalized() * T.TurretRange;
        effects.Tracer(from, end, new Color("ff5a2a"), 0.12f, 0.12f);
        if (hit.Count == 0) return;
        if (hit["collider"].As<Node>() is Player p) p.TakeNpcHit(AttackerId, T.TurretDamage, heavy: true, from);
        else effects.Impact(end, (Vector3)hit["normal"], Surface.Rock, null, 2f);
    }
}
