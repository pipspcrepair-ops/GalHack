using Godot;
using Invasions.Combat;
using Invasions.Model;

namespace Invasions.World;

/// <summary>
/// A player base turret on its tower. With nobody manning it, its own turret AI picks the nearest
/// visible invader (soldier or vehicle crew), swings round and fires. A player can man it (F) to aim
/// and fire by hand. It needs the base powered: no core or no generator, no turret.
///
/// PLACEHOLDER gunnery, like the rest of combat here.
/// </summary>
public partial class Turret : Node3D
{
    public CombatWorld World;
    public BaseModel Model;
    public Block Block;

    public Player MannedBy { get; private set; }
    public int ShotsFired { get; private set; }
    public bool Powered => Block.Alive && Model.Powered;
    /// <summary>The turret AI is fighting on its own.</summary>
    public bool Autonomous => Powered && MannedBy == null && World.TurretsEnabled;

    /// <summary>Where a player manning the turret looks from.</summary>
    public Transform3D ViewTransform => _pitch.GlobalTransform.TranslatedLocal(new Vector3(0, 1.05f, 1.1f));

    public const float TowerHeight = 3.3f;
    const float Range = 135f, YawRate = 2.4f, PitchRate = 1.4f;
    FireControl _fireControl;

    Node3D _yaw, _pitch;
    MeshInstance3D _lamp;
    HitBox _head;
    IDamageable _target;
    float _scan, _fire;
    bool _barrel;
    Godot.Collections.Array<Rid> _ignore;

    public override void _Ready()
    {
        Material cream = Palette.Mat(Palette.Cream, 0.6f);
        Material dark = Palette.Mat(Palette.Dark, 0.6f);
        Material metal = Palette.Mat(Palette.Gunmetal, 0.35f, 0.6f);

        _yaw = new Node3D { Name = "Ring", Position = new Vector3(0, TowerHeight, 0) };
        AddChild(_yaw);
        Palette.Cylinder(_yaw, new Vector3(0, 0.1f, 0), 0.85f, 0.2f, dark, 16);
        _pitch = new Node3D { Name = "Cradle", Position = new Vector3(0, 0.75f, 0) };
        _yaw.AddChild(_pitch);
        Palette.Box(_pitch, Vector3.Zero, new Vector3(1.3f, 0.85f, 1.5f), cream);
        Palette.Box(_pitch, new Vector3(0, 0.12f, 0), new Vector3(1.32f, 0.16f, 1.52f), Palette.Mat(Palette.Orange));
        Palette.Box(_pitch, new Vector3(0, -0.3f, 0.2f), new Vector3(1.34f, 0.08f, 1.0f), Palette.Mat(Palette.Teal));
        Palette.Box(_pitch, new Vector3(0.3f, 0.05f, -1.35f), new Vector3(0.16f, 0.16f, 1.6f), metal);
        Palette.Box(_pitch, new Vector3(-0.3f, 0.05f, -1.35f), new Vector3(0.16f, 0.16f, 1.6f), metal);
        Palette.Box(_pitch, new Vector3(0.3f, 0.05f, -2.12f), new Vector3(0.24f, 0.24f, 0.18f), dark);
        Palette.Box(_pitch, new Vector3(-0.3f, 0.05f, -2.12f), new Vector3(0.24f, 0.24f, 0.18f), dark);
        Palette.Box(_pitch, new Vector3(0, 0.3f, -0.76f), new Vector3(0.36f, 0.2f, 0.04f), Palette.Glow(Palette.Teal, 3f)); // sensor eye
        _lamp = Palette.Box(_pitch, new Vector3(0, 0.5f, 0.5f), new Vector3(0.2f, 0.14f, 0.2f), Palette.Glow(Palette.Acid, 3f));

        _head = HitBox.Create(new BoxShape3D { Size = new Vector3(1.5f, 1.2f, 1.8f) }, new Vector3(0, 0.75f, 0),
            Layers.Blocks, Team.Defenders, () => Block.Alive, (amount, from, by) => Model.Damage(Block, amount));
        _yaw.AddChild(_head);
        World.TurretHeads.Add(_head);
        _fireControl = new FireControl(World.FireData, World.FireData.Weapon("base_cannon"));
        _ignore = new Godot.Collections.Array<Rid> { _head.GetRid() };
        if (GetParent()?.GetNodeOrNull<HitBox>("Body") is HitBox tower) _ignore.Add(tower.GetRid());
    }

    public override void _ExitTree() => World.TurretHeads.Remove(_head);

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _fire -= dt;
        _lamp.MaterialOverride = !Powered ? Palette.Mat(Palette.Maroon) : MannedBy != null ? Palette.Glow(Palette.Amber, 3f) : Palette.Glow(Palette.Acid, 3f);

        if (MannedBy != null)
        {
            if (!Powered) MannedBy.LeaveTurret();
            return;
        }
        if (!Autonomous)
        {
            // Unpowered: the barrels sag.
            _pitch.Rotation = new Vector3(Mathf.MoveToward(_pitch.Rotation.X, -0.25f, dt * 0.5f), 0, 0);
            return;
        }

        _scan -= dt;
        Vector3 muzzle = Muzzle();
        if (_scan <= 0f)
        {
            _scan = 0.3f;
            _target = World.NearestInvader(muzzle, Range, Layers.World | Layers.Blocks | Layers.Vehicles, _ignore);
            if (_target == null) _fireControl.LostSight();
        }
        if (!CombatWorld.Usable(_target)) _target = null;
        if (_target == null) return;

        Vector3 local = GlobalTransform.AffineInverse() * _target.AimPoint - _yaw.Position - _pitch.Position;
        float wantYaw = Mathf.Atan2(-local.X, -local.Z);
        float wantPitch = Mathf.Atan2(local.Y, new Vector2(local.X, local.Z).Length());
        _yaw.Rotation = new Vector3(0, Mathf.RotateToward(_yaw.Rotation.Y, wantYaw, YawRate * dt), 0);
        _pitch.Rotation = new Vector3(Mathf.RotateToward(_pitch.Rotation.X, Mathf.Clamp(wantPitch, -0.6f, 1.2f), PitchRate * dt), 0, 0);

        Vector3 toTarget = (_target.AimPoint - muzzle).Normalized();
        if (Forward.AngleTo(toTarget) < Mathf.DegToRad(3f) && _fire <= 0f)
        {
            _fire = (float)_fireControl.Weapon.Interval;
            _barrel = !_barrel;
            ShotsFired++;
            World.FireAt(_fireControl, Muzzle(), _target, Layers.DefenderShots, Team.Defenders, Palette.TracerSettler, _ignore, Range * 1.4f);
            Fx.Flash(Muzzle(), Palette.TracerSettler, 5f, 3f);
        }
    }

    Vector3 Forward => -_pitch.GlobalBasis.Z;
    Vector3 Muzzle() => _pitch.GlobalTransform * new Vector3(_barrel ? 0.3f : -0.3f, 0.05f, -2.2f);

    /// <summary>A shot aimed by a player at the controls: the weapon's hand-aimed spread, no fire control.</summary>
    void FireByHand()
    {
        if (_fire > 0f) return;
        _fire = (float)_fireControl.Weapon.Interval;
        _barrel = !_barrel;
        ShotsFired++;
        Vector3 muzzle = Muzzle();
        World.Fire(muzzle, muzzle + Forward * 100f, _fireControl.Weapon, (float)_fireControl.Weapon.AimedSpreadDegrees,
            Layers.DefenderShots, Team.Defenders, Palette.TracerSettler, _ignore, maxDistance: 500f);
        Fx.Flash(muzzle, Palette.TracerSettler, 5f, 3f);
    }

    // ---- Manned -----------------------------------------------------------------------

    public bool CanMan => Powered && MannedBy == null;

    public void Man(Player player) => MannedBy = player;
    public void Release() => MannedBy = null;

    /// <summary>A player at the controls turns the turret with the mouse.</summary>
    public void Aim(float yaw, float pitch)
    {
        _yaw.Rotation = new Vector3(0, yaw, 0);
        _pitch.Rotation = new Vector3(Mathf.Clamp(pitch, -0.6f, 1.2f), 0, 0);
    }

    public float Yaw => _yaw.Rotation.Y;
    public float Pitch => _pitch.Rotation.X;

    public void FireManual()
    {
        if (Powered) FireByHand();
    }
}
