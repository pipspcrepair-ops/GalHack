using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Boarding;

/// <summary>
/// Headless tests for the boarding prototype. Run with:
///   godot --headless --path prototypes/boarding res://tests/BoardingTest.tscn
/// Each group builds a fresh world, drives the real systems (the player's interaction code,
/// real charges, real guards and turret) and checks the rules. Exits 0 if every check passes.
/// </summary>
public partial class BoardingTest : Node
{
    Main _main;
    int _failures, _checks;

    Ship Ship => _main.Ship;
    Player Player => _main.Player;
    ShipLayout Layout => _main.Layout;

    public override async void _Ready()
    {
        GD.Seed(20261104);
        try
        {
            await Pathfinding();
            await HackAndKeycard();
            await LoudBreach();
            await HijackChannel();
            await HijackStages();
            HostileFireRules();
            await GuardFire();
            await TurretAndGunner();
            await ComponentSalvage();
            await Crouching();
        }
        catch (Exception e)
        {
            Fail($"exception: {e}");
        }
        GD.Print(_failures == 0 ? $"ALL {_checks} CHECKS PASSED" : $"{_failures} OF {_checks} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- Groups ----------------------------------------------------------------------------

    async Task Pathfinding()
    {
        Section("A* pathfinding on the block grid");
        await Fresh(frozen: true);

        var from = new Vector2I(7, 8);   // gunnery
        var to = new Vector2I(20, 8);    // barracks, two walls and a corridor away
        List<Vector2I> path = Pathfinder.FindPath(from, to, c => Ship.Passable(c));
        Check(path != null, "finds a path from the gunnery to the barracks");
        if (path != null)
        {
            bool steps = path.Zip(path.Skip(1)).All(p => Math.Abs(p.First.X - p.Second.X) <= 1 && Math.Abs(p.First.Y - p.Second.Y) <= 1);
            bool clear = path.All(c => Ship.Passable(c));
            Check(steps && clear, $"every step is to a neighbouring open cell ({path.Count} cells)");
            Check(path.Contains(new Vector2I(11, 7)) && path.Contains(new Vector2I(14, 7)),
                "goes around the walls through both doors (11,7) and (14,7)");
        }

        var spine = new Vector2I(13, 13);
        Check(Ship.FindPath(Layout.CellCenter(spine), Layout.CellCenter(new Vector2I(16, 13))) == null,
            "the locked security door blocks every path into the antechamber");
        Check(Ship.FindPath(Layout.CellCenter(spine), Layout.CellCenter(new Vector2I(22, 13))) == null,
            "and into the core vault");
    }

    async Task HackAndKeycard()
    {
        Section("Security doors: hack and keycard");
        // Same world as the pathfinding group, continued.
        Door outer = Ship.DoorAt(new Vector2I(14, 13));
        Door vault = Ship.DoorAt(new Vector2I(19, 13));
        int noiseBefore = _main.Noise.Count;

        Stand(new Vector2I(13, 13), lookAt: outer);
        Player.TestInteract = true;
        await Seconds(1.5f);
        Check(outer.Locked, $"half a hack doesn't open it ({outer.HackProgress:F1} s of {_main.Tunables.HackSeconds} s)");
        await Seconds(1.8f);
        Player.TestInteract = false;
        Check(!outer.Locked, "holding E for ~3 s hacks the security door open");
        Check(_main.Alarm.Level == AlarmLevel.Unaware, $"the hack left the alarm at {_main.Alarm.Level}");
        Check(_main.Noise.Count == noiseBefore, "and made no noise");
        Check(Ship.FindPath(Layout.CellCenter(new Vector2I(13, 13)), Layout.CellCenter(new Vector2I(16, 13))) != null,
            "A* now paths through the opened door");
        Check(Ship.FindPath(Layout.CellCenter(new Vector2I(13, 13)), Layout.CellCenter(new Vector2I(22, 13))) == null,
            "but the vault door is still locked");

        _main.Inventory.Add(Inventory.Keycard);
        Stand(new Vector2I(18, 13), lookAt: vault);
        await Ticks(2);
        Player.TestInteract = true;
        float waited = 0f;
        while (vault.Locked && waited < 2f)
        {
            await Ticks(1);
            waited += 1f / Engine.PhysicsTicksPerSecond;
        }
        Player.TestInteract = false;
        Check(!vault.Locked, $"an officer's keycard opens the vault door in {waited:F2} s");
        Check(_main.Alarm.Level == AlarmLevel.Unaware && _main.Noise.Count == noiseBefore, "silently: no alarm, no noise");
        Check(Ship.FindPath(Layout.CellCenter(new Vector2I(13, 13)), Layout.CellCenter(new Vector2I(22, 13))) != null,
            "A* reaches the core once both doors are open");
    }

    async Task LoudBreach()
    {
        Section("Loud breach");
        await Fresh(frozen: false);
        _main.PlayerHidden = true;
        var cell = new Vector2I(26, 18);
        Block plate = Ship.Blocks[cell];
        Vector3 outward = Ship.GlobalBasis * Vector3.Right;
        Player.DebugPlace(Ship.ToGlobal(Layout.CellCenter(cell) + new Vector3(9f, 0.05f, 0f)));
        await Ticks(5);

        Vector3 breach = Ship.ToGlobal(Layout.CellCenter(cell));
        var before = _main.Guards.Where(g => !g.Dead && g.State != GuardState.Seated).ToDictionary(g => g, g => g.GlobalPosition.DistanceTo(breach));
        Check(_main.Alarm.Level == AlarmLevel.Unaware, "the ship starts unaware");

        Charge charge = Player.PlaceChargeOn(plate, plate.GlobalPosition + outward * 1f + Vector3.Up * 0.3f, outward);
        await Seconds(_main.Tunables.ChargeFuseSeconds + 0.2f);
        Check(charge.Detonated && plate.Destroyed, "the charge blew the hull plate out");
        Check(Ship.Passable(cell), "the gap is open floor now");
        Check(_main.Alarm.Level >= AlarmLevel.Alert, $"the loud breach raised the alarm to {(int)_main.Alarm.Level}");

        var heading = _main.Guards.Where(g => g.State is GuardState.Converge or GuardState.Investigate
            && g.PathEnd is Vector2I end && Math.Abs(end.X - cell.X) <= 3 && Math.Abs(end.Y - cell.Y) <= 3).ToList();
        Check(heading.Count >= 2, $"{heading.Count} guards are pathing to the breach");
        await Seconds(2f);
        int closer = before.Count(kv => !kv.Key.Dead && kv.Key.GlobalPosition.DistanceTo(breach) < kv.Value - 2f);
        Check(closer >= 2, $"{closer} guards actually moved toward it");
    }

    async Task HijackChannel()
    {
        Section("Hijack channel");
        await Fresh(frozen: true);
        foreach (Door door in Ship.Doors) door.Unlock();
        HijackSystem hijack = _main.Hijack;
        Stand(new Vector2I(22, 13), lookAt: Ship.Core);
        await Seconds(0.5f);
        Check(hijack.Progress == 0f && !hijack.Started, "no progress while nobody channels");

        Player.TestInteract = true;
        await Seconds(2f);
        Check(Player.ChannellingCore && Mathf.Abs(hijack.Progress - 2f) < 0.15f, $"holding E channels: {hijack.Progress:F2} s after 2 s");
        Check(_main.Alarm.Level == AlarmLevel.Lockdown, "starting a hijack puts the ship in lockdown");
        Check(Ship.Bulkheads.All(d => d.Locked), "and lockdown seals the bulkheads");

        float atHit = hijack.Progress;
        Player.TakeHit(5f, Player.GlobalPosition + Vector3.Forward * 5f);
        await Seconds(1f);
        Check(Mathf.Abs(hijack.Progress - atHit) < 0.02f, $"taking damage stalls it (still {hijack.Progress:F2} s a second later)");
        await Seconds(1f);
        Check(hijack.Progress > atHit + 0.3f, $"then it resumes ({hijack.Progress:F2} s)");

        Player.TestInteract = false;
        float atRelease = hijack.Progress;
        await Seconds(2f);
        float lost = atRelease - hijack.Progress;
        Check(lost > 0.5f && lost < 1.5f, $"it decays slowly while idle (lost {lost:F2} s in 2 s)");
    }

    async Task HijackStages()
    {
        Section("Hijack stages and their responses");
        // Same world, fast-forwarded: drive the channel directly, in big ticks within one frame.
        HijackSystem hijack = _main.Hijack;
        int guards = _main.Guards.Count;
        RunHijackUntil(() => hijack.CompletedStages >= 1);
        Check(_main.Guards.Count == guards + _main.Tunables.ReinforcementCount && _main.WavesSpawned == 1,
            $"stage 1: a reinforcement wave of {_main.Guards.Count - guards} arrives");
        Check(_main.Guards.Any(g => g.Type.Role == GuardRole.Enforcer), "with a heavy enforcer among them");
        Check(_main.Alarm.Level == AlarmLevel.Lockdown && !_main.Alarm.SelfDestruct.Active, "no self-destruct yet");

        foreach (Door door in Ship.Bulkheads) door.Unlock();   // the player had hacked them open again
        RunHijackUntil(() => hijack.CompletedStages >= 2);
        Check(Ship.Bulkheads.All(d => d.Sealed && d.Locked), "stage 2: the bulkheads slam shut and seal");
        Check(_main.Alarm.Level == AlarmLevel.LastStand, "stage 2 raises the alarm to 3 (last stand)");
        Check(_main.Alarm.SelfDestruct.Active && _main.Alarm.SelfDestruct.Remaining > 80f,
            $"and starts the self-destruct countdown ({_main.Alarm.SelfDestruct.Remaining:F0} s)");

        RunHijackUntil(() => hijack.Complete);
        await Ticks(3);
        Check(Ship.Lights == LightMode.Blackout, "stage 3: the power surge blacks out the lights");
        Check(Ship.OwnerFaction == Faction.Player, "completing the hijack flips ownership to the player");
        Check(!_main.Alarm.SelfDestruct.Active && _main.Alarm.SelfDestruct.Cancelled, "and cancels the self-destruct");
        Check(Ship.Rebooting && !Ship.Turret.Hostile, $"weapons and warp reboot ({Ship.RebootRemaining:F0} s); the turret stands down");
        Check(_main.Hud.StatusText.Contains("THE SHIP IS YOURS"), "the HUD says the ship is yours");
        Check(_main.Guards.Count(g => !g.Dead) > 0, "the remaining guards are still aboard to clear");
    }

    void RunHijackUntil(Func<bool> done)
    {
        for (int i = 0; i < 2000 && !done(); i++) _main.Hijack.Tick(0.05f, channelling: true);
    }

    void HostileFireRules()
    {
        Section("Hostile fire: unscaled damage, warning shots, accuracy ramp");
        Tunables t = Tunables.Load();
        HostileFire fire = HostileFire.Load();
        var rng = new Random(11);

        // Damage is never reduced, capped or floored.
        var health = new PlayerHealth(t);
        bool exact = true;
        for (int i = 0; i < 300; i++)
        {
            health.DebugSet(t.PlayerHealth);
            float raw = 0.5f + (float)rng.NextDouble() * 99f;
            exact &= Mathf.Abs(health.TakeDamage(raw) - raw) < 1e-3f && Mathf.Abs(health.Current - (t.PlayerHealth - raw)) < 1e-3f;
        }
        Check(exact, "300 hits of 0.5 to 99.5 damage each land in full");
        health.DebugSet(t.PlayerHealth);
        health.TakeDamage(t.TurretDamage);
        Check(health.Dead, $"one {t.TurretDamage:0}-damage capital shell kills a full-health player on foot");

        // The first N shots of every engagement are guaranteed misses that land close.
        int n = fire.WarningShots;
        bool allWarnings = true;
        float nearest = float.MaxValue, farthest = 0f;
        for (int e = 0; e < 500; e++)
        {
            var ramp = new AimRamp(fire);
            var target = new Vector3((float)rng.NextDouble() * 40f - 20f, 1f, (float)rng.NextDouble() * 40f - 20f);
            Vector3 muzzle = target + new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 0.6f, (float)rng.NextDouble() - 0.5f).Normalized() * (5f + (float)rng.NextDouble() * 80f);
            for (int s = 0; s < n; s++)
            {
                ramp.Observe(0.3f, true);   // slow fire: the shot count, not the clock, is what holds the warnings
                Vector3 aim = ramp.AimPoint(muzzle, target, 0.45f, 1.2f, rng, out bool warning);
                ramp.Shot();
                allWarnings &= warning;
                float outside = DistanceToLine(target, muzzle, aim) - 0.45f;
                nearest = Mathf.Min(nearest, outside);
                farthest = Mathf.Max(farthest, outside);
            }
        }
        Check(allWarnings, $"in 500 engagements the first {n} shots are always warnings");
        Check(nearest > 0.8f && farthest < fire.WarningMissMax + 0.01f,
            $"their line of fire passes {nearest:F2} to {farthest:F2} m outside the target: a sure miss, close enough to see");

        var fast = new AimRamp(fire);
        int fastWarnings = 0;
        for (int s = 0; s < 60; s++)
        {
            fast.Observe(1f / 60f, true);
            if (fast.Warning) fastWarnings++;
            fast.Shot();
        }
        int expected = Mathf.RoundToInt(fire.WarningSeconds * 60f);
        Check(Math.Abs(fastWarnings - expected) <= 1,
            $"a fast weapon's first {fire.WarningSeconds} s are all warnings too ({fastWarnings} of 60 rounds in 1 s)");

        // After the warnings, spread shrinks to the baseline over ramp_seconds.
        var r = new AimRamp(fire);
        float dt = 1f / 60f, clock = 0f, nextShot = 0f, settledAt = -1f, warnEnd = -1f, atOneSecond = 0f;
        for (int i = 0; i < 60 * 6; i++)
        {
            r.Observe(dt, true);
            clock += dt;
            if (clock >= nextShot) { r.Shot(); nextShot += 0.25f; }
            if (warnEnd < 0f && !r.Warning) warnEnd = clock;
            if (warnEnd >= 0f && atOneSecond == 0f && clock >= warnEnd + 1f) atOneSecond = r.Multiplier;
            if (settledAt < 0f && Mathf.IsEqualApprox(r.Multiplier, 1f)) settledAt = clock;
        }
        float rampTook = settledAt - warnEnd;
        Check(warnEnd > 0f && Mathf.Abs(rampTook - fire.RampSeconds) < 0.05f,
            $"accuracy reaches baseline {rampTook:F2} s after the warnings end (data: {fire.RampSeconds} s)");
        Check(atOneSecond > 1.05f && atOneSecond < fire.RampStart - 0.05f, $"halfway through, the cone is still {atOneSecond:F2}x baseline");

        float baseline = fire.BaselineSpread(ShooterClass.Infantry, TargetClass.OnFoot, fire.Weapon("rifle"));
        float widest = 0f;
        var origin = Vector3.Zero;
        var aimAt = new Vector3(0, 0, -20f);
        for (int i = 0; i < 2000; i++)
        {
            Vector3 p = r.AimPoint(origin, aimAt, 0.45f, baseline, rng, out _);
            widest = Mathf.Max(widest, Mathf.RadToDeg((p - origin).AngleTo(aimAt - origin)) * 2f);
        }
        Check(widest <= baseline + 0.01f && widest > baseline * 0.8f, $"settled rifle shots stay inside the {baseline:F1} deg baseline cone (widest {widest:F2})");

        Check(Mathf.IsEqualApprox(fire.BaselineSpread(ShooterClass.Capital, TargetClass.OnFoot, fire.Weapon("capital_cannon")), fire.Ratings["very_poor"])
              && Mathf.IsEqualApprox(fire.BaselineSpread(ShooterClass.Capital, TargetClass.Capital, fire.Weapon("capital_cannon")), fire.Ratings["very_good"]),
            "capital guns are very poor against infantry and very good against capitals (from the table)");
        Check(fire.Weapon("minigun").Spread > 1f && fire.Weapon("minigun").Rate > 1f && fire.Weapon("rail").Spread < 1f
              && fire.Weapon("rocket").Tracking > 0f && fire.Weapon("capital_cannon").Speed < fire.Weapon("small_craft_gun").Speed,
            "weapon types modify it: minigun sprays, rail is precise, rockets track, capital shells are slowest");

        // Breaking line of sight resets the ramp; a flicker shorter than the reset time doesn't.
        r.Observe(fire.LosResetSeconds * 0.5f, false);
        r.Observe(dt, true);
        Check(!r.Warning && Mathf.IsEqualApprox(r.Multiplier, 1f), "a brief flicker out of sight keeps the aim");
        r.Observe(fire.LosResetSeconds + 0.05f, false);
        Check(!r.Engaged && r.Shots == 0, "breaking line of sight ends the engagement");
        r.Observe(dt, true);
        Check(r.Warning && Mathf.IsEqualApprox(r.Multiplier, fire.RampStart), "and the next one opens with warning shots again");
    }

    static float DistanceToLine(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 d = (b - a).Normalized();
        Vector3 v = point - a;
        return (v - d * v.Dot(d)).Length();
    }

    async Task GuardFire()
    {
        Section("Guards shooting at the player");
        await Fresh(frozen: false);
        var shots = new List<Projectile>();
        _main.HostileShotFired += p => shots.Add(p);
        var damage = new List<float>();
        Player.Health.Damaged += d => damage.Add(d);

        // Walk into the barracks in full view.
        Player.DebugPlace(Ship.ToGlobal(Layout.CellCenter(new Vector2I(20, 7)) + Vector3.Up * 0.05f));
        Player.LookAt2(Ship.ToGlobal(Layout.CellCenter(new Vector2I(25, 7)) + Vector3.Up * 1.5f));
        for (int i = 0; i < 60 * 10 && damage.Count < 3; i++) await Ticks(1);
        await Seconds(0.3f);

        var byShooter = shots.GroupBy(p => p.Shooter).ToList();
        bool warningsFirst = byShooter.All(g => g.Take(_main.Fire.WarningShots).All(p => p.Warning && !p.HitPlayer));
        Check(shots.Count > 0 && warningsFirst,
            $"every guard's first {_main.Fire.WarningShots} shots are warnings that miss ({shots.Count} shots from {byShooter.Count} guards)");
        Check(shots.All(p => !p.Warning || !p.HitPlayer), "no warning shot ever hits");
        Check(shots.Take(3).All(p => p.Velocity.Length() < 500f && p.Velocity.Length() > 300f),
            $"rifle bolts travel at a visible {shots.FirstOrDefault()?.Velocity.Length():F0} m/s");
        float[] raws = { _main.Tunables.GuardDamage, _main.Tunables.OfficerDamage, _main.Tunables.EnforcerDamage };
        Check(damage.Count > 0 && damage.All(d => raws.Any(r => Mathf.Abs(r - d) < 1e-3f) || Player.Health.Dead),
            $"once the ramp tightens, hits land at full damage: {string.Join(", ", damage.Select(d => d.ToString("0.#")))}");
    }

    async Task TurretAndGunner()
    {
        Section("Crewed turret");
        await Fresh(frozen: false);
        Turret turret = Ship.Turret;
        Guard gunner = turret.Gunner;
        Check(gunner != null && gunner.Seated && turret.IsCrewed, "the gunner sits at his station and crews the turret");

        var shells = new List<Projectile>();
        _main.HostileShotFired += p => shells.Add(p);
        Player.DebugPlace(Ship.ToGlobal(new Vector3(-40f, 0.05f, -7f)));
        Player.LookAt2(turret.GlobalPosition);
        bool telegraphed = false;
        int shotsAtTelegraph = -1;
        for (int i = 0; i < 60 * 7 && shells.Count < _main.Fire.WarningShots; i++)
        {
            await Ticks(1);
            if (turret.Telegraphing && shotsAtTelegraph < 0) { telegraphed = true; shotsAtTelegraph = turret.ShotsFired; }
        }
        await Seconds(0.3f);   // let the shells arrive
        Check(telegraphed && shotsAtTelegraph == 0, "it telegraphs with its laser before the first shot");
        Check(shells.Count >= _main.Fire.WarningShots && shells.All(s => s.Warning && !s.HitPlayer) && !Player.Health.Dead,
            $"its opening {shells.Count} shells are warnings: the player is untouched");
        Check(shells.All(s => s.ClosestToPlayer > 0.9f && s.ClosestToPlayer < 4f),
            $"each one cracks past {shells.Min(s => s.ClosestToPlayer):F1} to {shells.Max(s => s.ClosestToPlayer):F1} m away");
        Check(Mathf.IsEqualApprox(shells[0].Velocity.Length(), _main.Fire.Weapon("capital_cannon").Speed),
            $"capital shells fly at {shells[0].Velocity.Length():F0} m/s: slow enough to see");
        Check(Mathf.IsEqualApprox(shells[0].Damage, _main.Tunables.TurretDamage), $"and carry the full {shells[0].Damage:0} damage");

        gunner.TakeHit(9999f, gunner.GlobalPosition + Vector3.Up, Vector3.Forward, false);
        Check(gunner.Dead && !turret.IsCrewed && !turret.Hostile, "killing the gunner leaves the turret unmanned");
        int fired = turret.ShotsFired;
        await Seconds(3.5f);
        Check(turret.ShotsFired == fired, "and it stays silent");
    }

    async Task ComponentSalvage()
    {
        Section("Salvaging components");
        await Fresh(frozen: true);
        _main.PlayerHidden = true;
        Block generator = Ship.Generator;
        Stand(new Vector2I(6, 11), lookAt: generator);
        Player.TestInteract = true;
        await Seconds(_main.Tunables.SalvageSeconds + 0.5f);
        Check(!generator.Destroyed && !_main.Inventory.Has("Vharn generator"), "removal is refused while the enemy core is alive");
        Check(!Salvage.CanRemove(Ship, Ship.Turret, out string reason), $"for the turret too: \"{reason}\"");
        Player.TestInteract = false;

        Ship.Core.Damage(1e6f, Ship.Core.GlobalPosition, Vector3.Up);
        await Ticks(2);
        Check(!Ship.CoreAlive && Ship.Derelict, "shooting the core dead leaves the ship derelict");

        Player.TestInteract = true;
        await Seconds(_main.Tunables.SalvageSeconds + 0.3f);
        Player.TestInteract = false;
        Check(generator.Destroyed && _main.Inventory.Has("Vharn generator"), "the generator comes out whole into the inventory");
        Check(Salvage.Remove(_main, Ship.Turret) && _main.Inventory.Has("Vharn turret"), "and so does the turret");
    }

    async Task Crouching()
    {
        Section("Crouching");
        await Fresh(frozen: true);
        _main.PlayerHidden = true;
        Vector3 open = Ship.ToGlobal(Layout.CellCenter(new Vector2I(-2, 19)));
        Player.DebugPlace(open);
        await Ticks(3);
        float walk = Player.NoiseRadius(false), sprint = Player.NoiseRadius(true);
        float standing = Player.CapsuleHeight;

        Player.TestCrouch = true;
        await Ticks(3);
        Check(Player.Crouched && Player.CapsuleHeight < standing, $"crouching shrinks the capsule: {standing:F2} m -> {Player.CapsuleHeight:F2} m");
        Check(Mathf.IsEqualApprox(Player.NoiseRadius(false), walk * 0.5f) && Mathf.IsEqualApprox(Player.NoiseRadius(true), sprint * 0.5f),
            $"and halves footstep noise: walk {walk:F0} m -> {Player.NoiseRadius(false):F0} m");

        int count = _main.Noise.Count;
        Player.TestWalk = new Vector2(0f, -1f);
        await Seconds(2f);
        Player.TestWalk = Vector2.Zero;
        Check(_main.Noise.Count > count && _main.Noise.Last is { Kind: NoiseKind.Footstep } n && Mathf.IsEqualApprox(n.Radius, walk * 0.5f),
            "crouch-walking really emits the quieter footsteps");
        float speed = new Vector2(Player.Velocity.X, Player.Velocity.Z).Length();

        Player.DebugPlace(Ship.ToGlobal(Layout.CellCenter(new Vector2I(3, 19)) + Vector3.Up * 0.05f));
        await Ticks(5);
        Player.TestCrouch = false;
        await Ticks(10);
        Check(Player.Crouched && !Player.CanStand(), "you can't stand up under the duct's low ceiling");
        Player.DebugPlace(open);
        await Ticks(10);
        Check(!Player.Crouched && Mathf.IsEqualApprox(Player.CapsuleHeight, standing), "and stand again once clear of it");
        _ = speed;
    }

    // ---- Helpers ---------------------------------------------------------------------------

    async Task Fresh(bool frozen)
    {
        if (_main != null)
        {
            // Let the old world be deleted (end of a process frame) before building the next one,
            // so two worlds never share the same space.
            _main.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Ticks(1);
        }
        _main = new Main { Name = "Main" };
        AddChild(_main);
        _main.FreezeGuards = frozen;
        _main.PlayerHidden = frozen;
        await Ticks(3);
    }

    /// <summary>Stands the player in a cell (ship-local), looking at a node's middle.</summary>
    void Stand(Vector2I cell, Node3D lookAt)
    {
        Player.DebugPlace(Ship.ToGlobal(Layout.CellCenter(cell) + Vector3.Up * 0.05f));
        Vector3 target = lookAt is Block b && b.Size.Y > 2f ? b.GlobalPosition : lookAt.GlobalPosition;
        Player.LookAt2(target);
    }

    async Task Ticks(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    Task Seconds(float s) => Ticks(Mathf.CeilToInt(s * Engine.PhysicsTicksPerSecond));

    static void Section(string name) => GD.Print($"-- {name}");

    void Check(bool ok, string what)
    {
        _checks++;
        if (ok) GD.Print($"PASS  {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
