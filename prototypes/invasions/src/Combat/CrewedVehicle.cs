using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Invasions.Director;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// A block-built, crewed invader vehicle: a driver or pilot plus gunners, each a visible figure with
/// its own hit box. The crew rules live in <see cref="VehicleCrew"/>: a gun only fires while its
/// gunner lives, and the vehicle only moves while its driver does. Subclasses supply the body and how
/// it moves (<see cref="GroundCarrier"/>s drive or walk, <see cref="Flyer"/>s fly, a
/// <see cref="Growth"/> erupts and spreads). Colours come from the faction's <see cref="Livery"/>.
///
/// PLACEHOLDER gunnery: bursts at the nearest visible player or turret, through fire control.
/// </summary>
public abstract partial class CrewedVehicle : Node3D, IInvaderVehicle
{
    public enum Mode { Approach, Unload, Support, Pickup, Depart, Stranded }

    public CombatWorld World;
    public VehiclePlan Plan { get; init; }
    public VehicleRoute Route { get; init; }
    public VehicleCrew Crew { get; private set; }
    public Mode Phase { get; protected set; } = Mode.Approach;

    public Node3D Body => this;
    public int TroopsAboard => _toUnload.Count + _extracted;
    public bool IsGone { get; private set; }
    public bool IsStranded => !Crew.CanMove;
    public IReadOnlyList<int> ShotsFired => _shots;
    public IEnumerable<IDamageable> CrewTargets => _crewBoxes.Where(b => b.IsAlive);
    public abstract Vector3 PickupPoint { get; }
    public abstract bool ReadyForPickup { get; }
    /// <summary>Metres per second, for tests and the HUD.</summary>
    public float Speed { get; protected set; }
    /// <summary>Troops it has set down so far.</summary>
    public int Unloaded { get; private set; }

    public event Action<IInvaderVehicle, string, Vector3> TroopDismounted;

    protected readonly Queue<string> _toUnload = new();
    protected readonly List<Gun> Guns = new();
    protected AnimatableBody3D Hull;
    protected Livery Livery;
    readonly List<HitBox> _crewBoxes = new();
    readonly Dictionary<int, Node3D> _figures = new();
    int[] _shots;
    int _extracted;
    float _unloadTimer = 0.6f;

    protected sealed class Gun
    {
        public int Index;
        public Node3D Yaw, Pitch;
        public Vector3 MuzzleLocal;
        public float RestYaw;
        public IDamageable Target;
        public float FireTimer, ScanTimer;
        public bool Barrel;
        public FireControl Fire;
    }

    /// <summary>Accuracy row for anyone shooting at this vehicle's crew.</summary>
    protected abstract TargetClass HullClass { get; }
    /// <summary>Width and length on the ground, for marking a hulk as an obstacle.</summary>
    protected abstract Vector2 Footprint { get; }
    /// <summary>Seconds between troops climbing out, unless the plan says otherwise.</summary>
    protected virtual float UnloadInterval => 0.45f;
    /// <summary>Whether troops still aboard a vehicle whose driver is dead can climb out now.</summary>
    protected virtual bool StrandedCanUnload => GlobalPosition.Y < 0.5f && Speed < 0.5f;
    bool _markedAsObstacle;

    const float GunRange = 170f, CrewHp = 80f;

    public override void _Ready()
    {
        Livery = World.LiveryFor(Plan.Faction);
        Crew = new VehicleCrew(Plan.Gunners, CrewHp);
        _shots = new int[Plan.Gunners];
        foreach (string role in Plan.Troops) _toUnload.Enqueue(role);
        Hull = new AnimatableBody3D { Name = "Hull", CollisionLayer = Layers.Vehicles, CollisionMask = 0, SyncToPhysics = false };
        AddChild(Hull);
        BuildBody();
    }

    protected abstract void BuildBody();
    protected abstract void Move(float dt);
    /// <summary>Where the next troop climbs out, in world space.</summary>
    protected abstract Vector3 ExitPoint();
    protected virtual void OnUnloaded() { }
    protected virtual void OnStranded() { }
    /// <summary>Each time a troop climbs out (a bike hides the rider who got off).</summary>
    protected virtual void OnDismounted(int remaining) { }
    /// <summary>Gone from the area while departing. A growth sinks; everything else leaves by distance.</summary>
    protected virtual bool Departed() => Flat(GlobalPosition, Route.BaseCentre) > Route.GoneDistance;

    public override void _PhysicsProcess(double delta)
    {
        if (IsGone) return;
        float dt = (float)delta;
        if (Phase != Mode.Stranded && !Crew.CanMove)
        {
            Phase = Mode.Stranded;
            OnStranded();
        }

        Move(dt);

        // A stranded vehicle that has come to rest is a hulk: invaders on foot plan round it.
        if (Phase == Mode.Stranded && !_markedAsObstacle && GlobalPosition.Y < 0.5f && Speed < 0.3f)
        {
            _markedAsObstacle = true;
            World.RegisterObstacle(GlobalPosition with { Y = 0 }, new Vector3(Footprint.X, 3f, Footprint.Y), Rotation.Y);
        }

        // Troops climb out at the drop point; if the driver is killed on the way, they climb out of the
        // wreck once it has stopped and fight on foot rather than sit in it.
        bool unloading = Phase == Mode.Unload || (Phase == Mode.Stranded && StrandedCanUnload);
        if (unloading)
        {
            _unloadTimer -= dt;
            if (_unloadTimer <= 0f && _toUnload.Count > 0)
            {
                _unloadTimer = Plan.UnloadSeconds is double s ? (float)s : UnloadInterval;
                Unloaded++;
                TroopDismounted?.Invoke(this, _toUnload.Dequeue(), ExitPoint());
                OnDismounted(_toUnload.Count);
            }
            if (_toUnload.Count == 0 && Phase == Mode.Unload)
            {
                Phase = Mode.Support;
                OnUnloaded();
            }
        }

        for (int i = 0; i < Guns.Count; i++) GunStep(Guns[i], dt);

        if (Phase == Mode.Depart && Departed())
        {
            IsGone = true;
            QueueFree();
        }
    }

    // ---- Orders -----------------------------------------------------------------------

    public void AwaitPickup()
    {
        if (IsStranded || IsGone || Phase == Mode.Depart) return;
        Phase = Mode.Pickup;
    }

    public void Extract(IInvaderTroop troop) => _extracted++;

    public void Depart()
    {
        if (IsStranded || IsGone || Phase == Mode.Depart) return;
        Phase = Mode.Depart;
    }

    /// <summary>
    /// Everyone left aboard a stranded vehicle climbs out to walk home: the crew, and any troops it had
    /// already picked up again (who would otherwise sit in the wreck forever).
    /// </summary>
    public virtual IReadOnlyList<Vector3> BailOut()
    {
        var positions = new List<Vector3>();
        int n = 0;
        foreach (CrewSeat seat in Crew.Seats.Where(s => s.Alive))
        {
            positions.Add(BailPoint(n++));
            if (_figures.TryGetValue(seat.Index, out Node3D figure)) figure.Visible = false;
        }
        for (; _extracted > 0; _extracted--) positions.Add(BailPoint(n++));
        Crew.BailOut();
        return positions;
    }

    Vector3 BailPoint(int i)
    {
        float a = i * 1.7f;
        return GlobalPosition with { Y = 0.1f } + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (4.5f + i / 4 * 1.2f);
    }

    /// <summary>Everyone aboard dies with the vehicle: a growth whose heart is killed takes its gunners and its unborn with it.</summary>
    protected void LoseEveryoneAboard()
    {
        _extracted = 0;
        _toUnload.Clear();
        foreach (CrewSeat seat in Crew.Seats.Where(s => s.Alive).ToList())
        {
            Crew.Damage(seat.Index, seat.Hp);
            Slump(seat.Index);
        }
    }

    // ---- Guns -------------------------------------------------------------------------

    void GunStep(Gun gun, float dt)
    {
        if (!Crew.CanFire(gun.Index))
        {
            // No gunner: the gun goes quiet and droops.
            gun.Pitch.Rotation = gun.Pitch.Rotation with { X = Mathf.MoveToward(gun.Pitch.Rotation.X, -0.35f, dt) };
            return;
        }

        gun.ScanTimer -= dt;
        gun.FireTimer -= dt;
        Vector3 muzzle = gun.Pitch.GlobalTransform * gun.MuzzleLocal;
        if (gun.ScanTimer <= 0f)
        {
            gun.ScanTimer = 0.6f;
            const uint sight = Layers.World | Layers.Blocks;
            gun.Target = Phase == Mode.Approach && Speed > 8f
                ? null
                : World.NearestPlayer(muzzle, GunRange, sight) ?? World.NearestTurret(muzzle, GunRange, sight);
            if (gun.Target == null) gun.Fire.LostSight();
        }
        if (!CombatWorld.Usable(gun.Target)) gun.Target = null;

        // Aim: turn the ring toward the target (or back to rest), then elevate.
        float wantYaw = gun.RestYaw, wantPitch = 0f;
        if (gun.Target != null)
        {
            Vector3 local = gun.Yaw.GetParentNode3D().GlobalTransform.AffineInverse() * gun.Target.AimPoint - gun.Yaw.Position;
            wantYaw = Mathf.Atan2(-local.X, -local.Z);
            wantPitch = Mathf.Atan2(local.Y, new Vector2(local.X, local.Z).Length());
        }
        gun.Yaw.Rotation = new Vector3(0, Mathf.RotateToward(gun.Yaw.Rotation.Y, wantYaw, 2.2f * dt), 0);
        gun.Pitch.Rotation = new Vector3(Mathf.RotateToward(gun.Pitch.Rotation.X, Mathf.Clamp(wantPitch, -0.5f, 0.9f), 1.6f * dt), 0, 0);

        if (gun.Target == null || gun.FireTimer > 0f) return;
        Vector3 forward = -gun.Pitch.GlobalBasis.Z;
        Vector3 toTarget = (gun.Target.AimPoint - muzzle).Normalized();
        if (forward.AngleTo(toTarget) > Mathf.DegToRad(6f)) return;

        if (!World.LineOfSight(muzzle, gun.Target, Layers.World | Layers.Blocks))
        {
            gun.Fire.LostSight(); // broke line of sight: the ramp starts over
            gun.Target = null;
            return;
        }
        gun.FireTimer = (float)gun.Fire.Weapon.Interval;
        gun.Barrel = !gun.Barrel;
        _shots[gun.Index]++;
        Vector3 from = muzzle + gun.Pitch.GlobalBasis.X * (gun.Barrel ? 0.12f : -0.12f);
        World.FireAt(gun.Fire, from, gun.Target, Layers.InvaderShots, Team.Invaders, Livery.Tracer, maxDistance: GunRange * 1.4f);
        if (_shots[gun.Index] % 3 == 0) Fx.Flash(from, Livery.Tracer, 4f, 2f);
    }

    // ---- Building ---------------------------------------------------------------------

    /// <summary>A block of the body. Solid blocks also go into the hull's collision.</summary>
    protected MeshInstance3D Block(Vector3 centre, Vector3 size, Material material, bool solid = true, Node3D parent = null)
    {
        MeshInstance3D mesh = Palette.Box(parent ?? this, centre, size, material);
        if (solid && parent == null)
            Hull.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = centre });
        return mesh;
    }

    /// <summary>A crew member: a figure the player can see and shoot, wired to its seat.</summary>
    protected Node3D CrewFigure(int seat, Node3D parent, Vector3 position)
    {
        var figure = new Node3D { Name = seat == 0 ? "Driver" : $"Gunner{seat}", Position = position };
        parent.AddChild(figure);
        BuildCrewLook(figure, seat);
        _figures[seat] = figure;

        HitBox box = HitBox.Create(new BoxShape3D { Size = new Vector3(0.62f, 0.95f, 0.5f) }, new Vector3(0, 0.4f, 0),
            Layers.InvaderHitboxes, Team.Invaders,
            () => Crew.Seat(seat).Alive,
            (amount, from, by) =>
            {
                if (Crew.Damage(seat, amount)) Slump(seat);
                else Fx.Spark(figure.GlobalPosition + Vector3.Up * 0.5f, Palette.SirenRed, 0.12f);
            });
        box.AimOffset = new Vector3(0, 0.15f, 0);
        box.Class = HullClass;
        figure.AddChild(box);
        _crewBoxes.Add(box);
        return figure;
    }

    /// <summary>What a crew member looks like: by default a seated figure in the faction's colours.</summary>
    protected virtual void BuildCrewLook(Node3D figure, int seat) => Looks.Crew(figure, Plan.Faction, Livery);

    protected virtual void Slump(int seat)
    {
        if (!_figures.TryGetValue(seat, out Node3D figure)) return;
        figure.Rotation = new Vector3(1.0f, 0.3f, 0);
        figure.Position += new Vector3(0, -0.3f, 0);
        Fx.Spark(figure.GlobalPosition + Vector3.Up * 0.5f, Palette.SirenRed, 0.3f);
    }

    protected Node3D Figure(int seat) => _figures.TryGetValue(seat, out Node3D f) ? f : null;

    /// <summary>A gun ring with twin barrels, a shield and its gunner.</summary>
    protected Gun AddGun(Vector3 mount, float restYaw)
    {
        int index = Guns.Count;
        var yaw = new Node3D { Name = $"GunRing{index}", Position = mount, Rotation = new Vector3(0, restYaw, 0) };
        AddChild(yaw);
        var pitch = new Node3D { Name = "Cradle", Position = new Vector3(0, 0.72f, -0.35f) };
        yaw.AddChild(pitch);
        BuildGunLook(yaw, pitch);
        CrewFigure(index + 1, yaw, new Vector3(0, 0.2f, 0.35f));
        var gun = new Gun
        {
            Index = index, Yaw = yaw, Pitch = pitch, MuzzleLocal = new Vector3(0, 0, -1.35f), RestYaw = restYaw,
            Fire = new FireControl(World.FireData, World.FireData.Weapon(Plan.Guns[index])),
        };
        gun.ScanTimer = 0.3f * index;
        Guns.Add(gun);
        return gun;
    }

    /// <summary>The ring, cradle, barrels and gun shield, in the faction's colours.</summary>
    protected virtual void BuildGunLook(Node3D yaw, Node3D pitch)
    {
        Palette.Cylinder(yaw, new Vector3(0, 0.08f, 0), 0.62f, 0.16f, Palette.Mat(Livery.Black, 0.6f), 14);
        Material metal = Palette.Mat(Palette.Gunmetal, 0.35f, 0.6f);
        Palette.Box(pitch, new Vector3(0, 0, 0), new Vector3(0.5f, 0.3f, 0.6f), metal);
        Palette.Box(pitch, new Vector3(0.12f, 0, -0.75f), new Vector3(0.08f, 0.08f, 1.1f), metal);
        Palette.Box(pitch, new Vector3(-0.12f, 0, -0.75f), new Vector3(0.08f, 0.08f, 1.1f), metal);
        Palette.Box(pitch, new Vector3(0, 0.05f, -0.42f), new Vector3(0.9f, 0.55f, 0.06f), Palette.Mat(Livery.Trim, 0.5f)); // shield
    }

    protected static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>Turns to face a flat direction at a limited rate.</summary>
    protected void TurnToward(Vector3 direction, float rate, float dt)
    {
        direction.Y = 0;
        if (direction.LengthSquared() < 0.001f) return;
        float want = Mathf.Atan2(-direction.X, -direction.Z);
        Rotation = new Vector3(Rotation.X, Mathf.RotateToward(Rotation.Y, want, rate * dt), Rotation.Z);
    }
}
