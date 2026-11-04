using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Boarding;

/// <summary>
/// The first-person player: walk, sprint, jump and crouch, three weapons, the multi-tool's
/// interactions (hack, keycard, hijack, loot, salvage) and breaching charges.
///
/// Feel comes first. Look and shooting run every render frame, straight off the mouse
/// events, so a click fires on the very next frame. Movement runs on the physics tick and
/// the camera follows the body's interpolated position, so it stays smooth at any frame rate.
/// </summary>
public partial class Player : CharacterBody3D
{
    public Main Game;
    public Camera3D Camera;

    public PlayerHealth Health { get; private set; }
    public readonly List<Weapon> Weapons = new();
    public Weapon Current => Weapons[_weapon];
    public bool Crouched { get; private set; }
    public bool Sprinting { get; private set; }
    public bool ChannellingCore { get; private set; }
    public string Prompt { get; private set; } = "";
    public float ActionProgress { get; private set; } = -1f;
    public float DeadFor { get; private set; }

    public bool IsInsideShip => Game.Ship.IsInside(GlobalPosition + Vector3.Up * 0.5f);

    /// <summary>Capsule height right now (standing or crouched).</summary>
    public float CapsuleHeight => _capsule.Height;

    // Test overrides: when set, they replace the keyboard.
    public bool? TestCrouch;
    public bool? TestInteract;
    public Vector2? TestWalk;
    /// <summary>For soak tests that only care about navigation.</summary>
    public bool TestInvulnerable;

    const float MouseSensitivity = 0.0022f, Gravity = 12f, JumpSpeed = 4.8f;
    const float GroundAccel = 55f, AirAccel = 9f, CapsuleRadius = 0.35f;

    Tunables T => Game.Tunables;

    CapsuleShape3D _capsule;
    CollisionShape3D _shape;
    Viewmodel _viewmodel;
    float _yaw, _pitch, _eye;
    int _weapon;
    float _switchLeft;
    bool _triggerHeld, _triggerPressed, _aiming;
    float _sinceShot = 10f, _pendingRecovery;
    Vector2 _mouseFrame;
    float _stepDistance;
    float _shake, _kickPitch, _kickVel, _flinch;
    float _actionTime;
    Node3D _actionTarget;
    float _refusedFor;
    string _refused = "";

    public override void _Ready()
    {
        Health = new PlayerHealth(T);
        Health.Damaged += taken =>
        {
            if (taken <= 0f) return;
            Game.Hijack.NotifyDamage();
            Game.Hud?.DamageFlash(taken / Health.Max);
            _flinch += Mathf.Clamp(taken * 0.08f, 0.3f, 2.5f);
        };

        _capsule = new CapsuleShape3D { Radius = CapsuleRadius, Height = T.StandHeight };
        _shape = new CollisionShape3D { Shape = _capsule, Position = Vector3.Up * T.StandHeight * 0.5f };
        AddChild(_shape);
        CollisionLayer = Layers.Player;
        CollisionMask = Layers.World | Layers.Ship | Layers.Guards;
        FloorMaxAngle = Mathf.DegToRad(50f);
        FloorSnapLength = 0.3f;
        PlatformOnLeave = PlatformOnLeaveEnum.DoNothing;
        _eye = T.StandEye;

        foreach (WeaponDef def in Game.WeaponDefs) Weapons.Add(new Weapon(def));

        Camera.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _viewmodel = new Viewmodel { Name = "Viewmodel" };
        Camera.AddChild(_viewmodel);
        _viewmodel.Show(Current.Def.Id);

        if (!Game.Headless) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // ---- Input ---------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float sensitivity = MouseSensitivity * (_aiming ? 0.7f : 1f);
            _yaw -= motion.Relative.X * sensitivity;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * sensitivity, -1.5f, 1.5f);
            // Pulling down during a burst is the player fighting recoil: don't recover it twice.
            if (motion.Relative.Y > 0f) _pendingRecovery = Mathf.Max(0f, _pendingRecovery - Mathf.RadToDeg(motion.Relative.Y * sensitivity));
            _mouseFrame += motion.Relative;
        }
        else if (e is InputEventMouseButton button)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured)
            {
                if (button.Pressed) Input.MouseMode = Input.MouseModeEnum.Captured;
                return;
            }
            switch (button.ButtonIndex)
            {
                case MouseButton.Left:
                    _triggerHeld = button.Pressed;
                    if (button.Pressed) _triggerPressed = true;
                    break;
                case MouseButton.Right: _aiming = button.Pressed; break;
                case MouseButton.WheelUp when button.Pressed: SwitchTo((_weapon + Weapons.Count - 1) % Weapons.Count); break;
                case MouseButton.WheelDown when button.Pressed: SwitchTo((_weapon + 1) % Weapons.Count); break;
            }
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.Escape: Input.MouseMode = Input.MouseModeEnum.Visible; break;
                case Key.Key1: SwitchTo(0); break;
                case Key.Key2: SwitchTo(1); break;
                case Key.Key3: SwitchTo(2); break;
                case Key.R: Current.StartReload(); break;
                case Key.G: PlaceCharge(); break;
            }
        }
    }

    // ---- Per render frame: look, shoot, camera -------------------------------------------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach (Weapon w in Weapons) w.Tick(dt);
        _switchLeft = Mathf.Max(0f, _switchLeft - dt);
        _sinceShot += dt;

        if (!Health.Dead) Shoot(dt);
        _triggerPressed = false;

        // Recoil settles back part of the way once you stop firing.
        if (_sinceShot > 0.12f && _pendingRecovery > 0f)
        {
            float step = Mathf.Min(_pendingRecovery, Current.Def.RecoilRecovery * dt);
            _pendingRecovery -= step;
            _pitch -= Mathf.DegToRad(step);
        }

        PlaceCamera(dt);
        float speed01 = Mathf.Clamp(new Vector2(Velocity.X, Velocity.Z).Length() / T.SprintSpeed, 0f, 1f);
        _viewmodel.Tick(dt, IsOnFloor() ? speed01 : 0f, _mouseFrame, Current.Reloading || _switchLeft > 0f, WallDistance());
        _mouseFrame = Vector2.Zero;
    }

    void Shoot(float dt)
    {
        if (_switchLeft > 0f) return;
        Weapon w = Current;
        bool want = w.Def.Automatic ? _triggerHeld : _triggerPressed;
        if (want && w.Ammo == 0 && !w.Reloading)
        {
            w.StartReload();
            return;
        }
        int guard = 0;
        while (want && w.CanFire && guard++ < 4)
        {
            FireOnce(w);
            if (!w.Def.Automatic) break;
        }
    }

    void FireOnce(Weapon w)
    {
        WeaponDef def = w.Def;
        float side = w.Shoot();
        bool firstShot = _sinceShot > 0.35f;
        _sinceShot = 0f;

        Transform3D view = Camera.GlobalTransform;
        float speed01 = Mathf.Clamp(new Vector2(Velocity.X, Velocity.Z).Length() / T.WalkSpeed, 0f, 1f);
        float spread = Mathf.Lerp(def.Spread, def.SpreadMoving, speed01);
        if (Crouched) spread *= 0.7f;
        if (_aiming) spread *= 0.5f;
        if (firstShot && def.Pellets == 1) spread *= 0.4f;

        bool anyHit = false, anyKill = false, anyHead = false;
        Vector3 lastPoint = view.Origin - view.Basis.Z * def.Range;
        for (int i = 0; i < def.Pellets; i++)
        {
            Vector3 dir = Cone(-view.Basis.Z, view.Basis, spread);
            var (hit, killed, head, point) = Combat.PlayerShot(Game, view.Origin, dir, def, this);
            anyHit |= hit;
            anyKill |= killed;
            anyHead |= head;
            lastPoint = point;
            if (i < 3) Game.Effects.Tracer(_viewmodel.MuzzleGlobal, point, new Color("ffd27a"), def.Pellets > 1 ? 0.015f : 0.025f);
        }
        if (anyHit) Game.Hud?.HitMarker(anyHead, anyKill);

        // Recoil: the aim itself climbs (learnable, fixed pattern); the camera kick is extra and visual.
        _pitch = Mathf.Clamp(_pitch + Mathf.DegToRad(def.RecoilUp), -1.5f, 1.5f);
        _yaw -= Mathf.DegToRad(side);
        _pendingRecovery += def.RecoilUp * 0.6f;
        _kickVel += def.Kick * 14f;
        _viewmodel.Fired(def.Kick);

        Game.Noise.Emit(GlobalPosition + Vector3.Up, def.Noise, NoiseKind.Gunfire, fromPlayer: true);
    }

    /// <summary>A random direction inside a cone, biased toward the middle.</summary>
    static Vector3 Cone(Vector3 forward, Basis view, float degrees)
    {
        float r = Mathf.DegToRad(degrees) * Mathf.Sqrt(GD.Randf()) * 0.5f;
        float a = GD.Randf() * Mathf.Tau;
        Vector3 offset = view.X * Mathf.Cos(a) * Mathf.Tan(r) + view.Y * Mathf.Sin(a) * Mathf.Tan(r);
        return (forward + offset).Normalized();
    }

    void PlaceCamera(float dt)
    {
        _eye = Mathf.MoveToward(_eye, Crouched ? T.CrouchEye : T.StandEye, dt * 5f);

        // A stiff spring for the shot kick, plus decaying trauma shake for explosions.
        _kickVel += (-_kickPitch * 320f - _kickVel * 26f) * dt;
        _kickPitch += _kickVel * dt;
        _shake = Mathf.MoveToward(_shake, 0f, dt * 1.6f);
        _flinch = Mathf.MoveToward(_flinch, 0f, dt * 6f);
        float trauma = _shake * _shake;
        float t = Time.GetTicksMsec() * 0.001f;
        var shakeRot = new Vector3(Mathf.Sin(t * 47f), Mathf.Sin(t * 53f + 1f), Mathf.Sin(t * 41f + 2f)) * trauma * 0.03f;

        Vector3 feet = GetGlobalTransformInterpolated().Origin;
        Basis look = new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch + Mathf.DegToRad(_kickPitch * 0.12f + _flinch));
        look = look * Basis.FromEuler(shakeRot);
        Camera.GlobalTransform = new Transform3D(look, feet + Vector3.Up * _eye);
        Camera.Fov = Mathf.Lerp(Camera.Fov, _aiming ? 62f : 78f, Mathf.Min(1f, dt * 14f));
    }

    float WallDistance()
    {
        var query = PhysicsRayQueryParameters3D.Create(Camera.GlobalPosition, Camera.GlobalPosition - Camera.GlobalBasis.Z * 0.9f,
            Layers.World | Layers.Ship, new Godot.Collections.Array<Rid> { GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? Camera.GlobalPosition.DistanceTo((Vector3)hit["position"]) : 0.9f;
    }

    void SwitchTo(int index)
    {
        if (index == _weapon || index < 0 || index >= Weapons.Count) return;
        Current.ReloadLeft = 0f;   // switching cancels a reload
        _weapon = index;
        _switchLeft = 0.28f;
        _viewmodel.Show(Current.Def.Id);
    }

    public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

    // ---- Physics tick: movement, crouch, interaction ---------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Health.Tick(dt);
        if (Health.Dead)
        {
            DeadFor += dt;
            ChannellingCore = false;
            Velocity = new Vector3(0, Velocity.Y - Gravity * dt, 0);
            MoveAndSlide();
            if (DeadFor > 3f) Respawn();
            return;
        }

        UpdateCrouch();
        Move(dt);
        Interact(dt);
    }

    void UpdateCrouch()
    {
        bool want = TestCrouch ?? Input.IsPhysicalKeyPressed(Key.Ctrl);
        if (want && !Crouched) SetCrouched(true);
        else if (!want && Crouched && CanStand()) SetCrouched(false);
    }

    void SetCrouched(bool crouched)
    {
        Crouched = crouched;
        float height = crouched ? T.CrouchHeight : T.StandHeight;
        _capsule.Height = height;
        _shape.Position = Vector3.Up * height * 0.5f;
    }

    /// <summary>True if a standing capsule fits here: you can't stand up under a duct or a desk.</summary>
    public bool CanStand()
    {
        var shape = new CapsuleShape3D { Radius = CapsuleRadius - 0.03f, Height = T.StandHeight - 0.06f };
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = shape,
            Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up * (T.StandHeight * 0.5f + 0.04f)),
            CollisionMask = Layers.World | Layers.Ship,
            Exclude = new Godot.Collections.Array<Rid> { GetRid() },
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    /// <summary>How far your footsteps carry: sprinting is loud, crouching halves it.</summary>
    public float NoiseRadius(bool sprinting) =>
        (sprinting ? T.NoiseSprint : T.NoiseWalk) * (Crouched ? T.CrouchNoiseFactor : 1f);

    void Move(float dt)
    {
        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();
        if (!onFloor) v.Y -= Gravity * dt;

        Vector2 move = TestWalk ?? new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1f);
        Sprinting = !Crouched && !_aiming && move.Y < -0.1f && Input.IsPhysicalKeyPressed(Key.Shift) && _sinceShot > 0.3f;
        float speed = Crouched ? T.CrouchSpeed : Sprinting ? T.SprintSpeed : T.WalkSpeed;
        if (_aiming && !Crouched) speed *= 0.7f;
        Vector3 wish = new Basis(Vector3.Up, _yaw) * new Vector3(move.X, 0, move.Y) * speed;

        float accel = onFloor ? GroundAccel : AirAccel;
        v.X = Mathf.MoveToward(v.X, wish.X, accel * dt);
        v.Z = Mathf.MoveToward(v.Z, wish.Z, accel * dt);
        if (onFloor && !Crouched && Input.IsPhysicalKeyPressed(Key.Space)) v.Y = JumpSpeed;

        Velocity = v;
        MoveAndSlide();

        // Footsteps are noise guards can hear.
        float ground = new Vector2(Velocity.X, Velocity.Z).Length();
        if (onFloor && ground > 0.5f)
        {
            _stepDistance += ground * dt;
            float stride = Crouched ? 1.3f : Sprinting ? 2.6f : 2f;
            if (_stepDistance >= stride)
            {
                _stepDistance = 0f;
                Game.Noise.Emit(GlobalPosition, NoiseRadius(Sprinting), NoiseKind.Footstep, fromPlayer: true);
            }
        }
    }

    void Interact(float dt)
    {
        bool held = TestInteract ?? Input.IsPhysicalKeyPressed(Key.E);
        Node3D target = LookTarget(T.InteractRange);
        bool channel = false;
        float progress = -1f;
        string prompt = "";
        _refusedFor = Mathf.Max(0f, _refusedFor - dt);

        if (target != _actionTarget)
        {
            if (_actionTarget is Door oldDoor && IsInstanceValid(oldDoor)) oldDoor.ResetProgress();
            _actionTarget = target;
            _actionTime = 0f;
        }

        switch (target)
        {
            case Door door when door.Locked:
                bool key = Game.Inventory.Has(Inventory.Keycard);
                prompt = key ? "Hold E: swipe officer's keycard (silent)" : "Hold E: hack the lock (quiet)   G: breaching charge (loud)";
                if (held)
                {
                    bool opened = key ? door.Swipe(dt, T.KeycardSeconds) : door.Hack(dt, T.HackSeconds);
                    if (opened) Game.Hud?.Message(key ? "Keycard accepted. Door open." : "Lock hacked. Door open.");
                }
                progress = key ? door.KeyProgress / T.KeycardSeconds : door.HackProgress / T.HackSeconds;
                if (!held) door.ResetProgress();
                break;

            case CoreBlock core when Game.Ship.Hostile && !Game.Hijack.Complete:
                prompt = "Hold E: Hijack Tool on the core   (or shoot it to wreck the ship)";
                channel = held && core.GlobalPosition.DistanceTo(GlobalPosition + Vector3.Up) < T.HijackRange + 1f;
                progress = Game.Hijack.Progress / Game.Hijack.Total;
                break;

            case LootCache cache when !cache.Looted:
                prompt = "Hold E: crack the cache";
                if (held && Hold(dt, T.LootSeconds, out progress))
                {
                    foreach (var (item, count) in cache.Open()) Game.Inventory.Add(item, count);
                    Game.Hud?.Message("Looted: " + string.Join(", ", cache.Contents.Select(kv => $"{kv.Key} x{kv.Value}")));
                }
                break;

            case Block block when block.IsComponent && !block.Destroyed:
                string name = Salvage.ItemName(block);
                if (!Salvage.CanRemove(Game.Ship, block, out string reason))
                {
                    prompt = $"{name}: {reason}";
                    if (held && _refusedFor <= 0f)
                    {
                        _refused = reason;
                        _refusedFor = 2f;
                        Game.Hud?.Message($"Can't remove the {name.ToLower()}: {reason}");
                    }
                    break;
                }
                prompt = $"Hold E: remove the {name.ToLower()} whole (multi-tool)";
                if (held && Hold(dt, T.SalvageSeconds, out progress))
                {
                    Salvage.Remove(Game, block);
                    Game.Hud?.Message($"{name} removed into your inventory.");
                }
                break;
        }

        if (!held) _actionTime = 0f;
        ChannellingCore = channel;
        Prompt = prompt;
        ActionProgress = progress;
    }

    bool Hold(float dt, float seconds, out float progress)
    {
        _actionTime += dt;
        progress = _actionTime / seconds;
        if (_actionTime < seconds) return false;
        _actionTime = 0f;
        return true;
    }

    /// <summary>The block (or anything) under the crosshair within reach.</summary>
    Node3D LookTarget(float range)
    {
        Transform3D view = Camera.GlobalTransform;
        var query = PhysicsRayQueryParameters3D.Create(view.Origin, view.Origin - view.Basis.Z * range,
            Layers.World | Layers.Ship, new Godot.Collections.Array<Rid> { GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? hit["collider"].As<Node3D>() : null;
    }

    void PlaceCharge()
    {
        Transform3D view = Camera.GlobalTransform;
        var query = PhysicsRayQueryParameters3D.Create(view.Origin, view.Origin - view.Basis.Z * 3f,
            Layers.World | Layers.Ship, new Godot.Collections.Array<Rid> { GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].As<Node3D>() is not Block block || block.Destroyed) return;
        if (block.Kind is BlockKind.Armour or BlockKind.Core or BlockKind.Loot or BlockKind.DuctCap)
        {
            Game.Hud?.Message(block.Kind == BlockKind.Armour ? "Vault armour: a charge won't scratch it." : "A charge won't stick to that.");
            return;
        }
        if (!Game.Inventory.Take(Inventory.Charge))
        {
            Game.Hud?.Message("No breaching charges left.");
            return;
        }
        PlaceChargeOn(block, (Vector3)hit["position"], (Vector3)hit["normal"]);
    }

    /// <summary>Sticks a live charge to a block. Public for tests.</summary>
    public Charge PlaceChargeOn(Block block, Vector3 point, Vector3 normal)
    {
        var charge = new Charge { Game = Game, Target = block, Normal = normal };
        Game.Ship.AddChild(charge);
        charge.GlobalTransform = Effects.Facing(point + normal * 0.07f, normal);
        Game.Hud?.Message("Charge set. Get clear!");
        return charge;
    }

    // ---- Taking damage ---------------------------------------------------------------------

    /// <summary>A hostile hit. Damage lands in full: it is never scaled or capped.</summary>
    public float TakeHit(float damage, Vector3 from)
    {
        if (TestInvulnerable) return 0f;
        float taken = Health.TakeDamage(damage);
        if (taken > 0f) Game.Hud?.DamageFrom(from, Camera.GlobalTransform);
        return taken;
    }

    /// <summary>A hostile round cracked past close by: flinch, so a miss is felt as well as seen.</summary>
    public void NearMiss(Vector3 point)
    {
        _flinch += 0.6f;
        Shake(0.18f);
        Game.Hud?.NearMiss(point, Camera.GlobalTransform);
    }

    /// <summary>Your own charge, or the ship blowing up. Not hostile fire, but also never scaled.</summary>
    public float TakeEnvironmentDamage(float amount) => TestInvulnerable ? 0f : Health.TakeDamage(amount);

    public void Respawn()
    {
        DeadFor = 0f;
        Health.Reset();
        DebugPlace(Game.SpawnPosition);
        LookAt2(Game.SpawnFacing);
        Game.Hud?.Message("You went down. Back at the landing site; your gear is still on you.");
    }

    // ---- Test and screenshot hooks ----------------------------------------------------------

    public void DebugPlace(Vector3 feet)
    {
        GlobalPosition = feet;
        Velocity = Vector3.Zero;
        ResetPhysicsInterpolation();
    }

    public void DebugSetLook(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
        _pendingRecovery = 0f;
    }

    /// <summary>Aims the eye at a world point.</summary>
    public void LookAt2(Vector3 target)
    {
        Vector3 eye = GlobalPosition + Vector3.Up * _eye;
        Vector3 d = (target - eye).Normalized();
        _yaw = Mathf.Atan2(-d.X, -d.Z);
        _pitch = Mathf.Asin(Mathf.Clamp(d.Y, -1f, 1f));
        _pendingRecovery = 0f;
        Camera.GlobalTransform = new Transform3D(new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch), eye);
    }

    public void DebugSelectWeapon(int index)
    {
        SwitchTo(index);
        _switchLeft = 0f;
    }

    /// <summary>Fires the current weapon once, as if clicked. For screenshots and tests.</summary>
    public void DebugFire()
    {
        if (Current.Ammo == 0) Current.Ammo = Current.Def.Magazine;
        Current.Cooldown = 0f;
        FireOnce(Current);
    }

    static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(negative) ? 1f : 0f);
}
