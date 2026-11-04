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
            DamageRules();
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
        Player.TakeNpcHit(999, 5f, false, Player.GlobalPosition + Vector3.Forward * 5f);
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

    void DamageRules()
    {
        Section("No instant death");
        Tunables t = Tunables.Load();
        var health = new PlayerHealth(t);
        float cap = t.PlayerHealth * t.NpcMaxHitFraction;
        float worst = 0f;
        for (int i = 0; i < 300; i++)
        {
            health.DebugSet(t.PlayerHealth);
            float raw = (float)GD.RandRange(1.0, 2000.0);
            worst = Mathf.Max(worst, health.TakeNpcHit((int)(GD.Randi() % 6), raw, GD.Randf() < 0.5f));
        }
        Check(worst <= cap + 0.001f, $"300 NPC hits up to 2000 raw: the biggest took {worst:F1} (cap {cap:F0})");

        var heavy = new PlayerHealth(t);
        var taken = new List<float>();
        for (int i = 0; i < 6; i++)
        {
            heavy.DebugSet(t.PlayerHealth);
            taken.Add(heavy.TakeNpcHit(Turret.AttackerId, t.TurretDamage, heavy: true));
        }
        Check(taken[0] < taken[4] && taken[1] < taken[4] && taken[0] <= t.TurretDamage * t.HeavyWarningScale + 0.01f,
            $"a heavy opens with weak warning hits, then ramps: {string.Join(", ", taken.Select(x => x.ToString("F1")))}");
        Check(taken.Zip(taken.Skip(1)).All(p => p.Second >= p.First - 0.001f), "and never ramps back down mid-fight");

        var low = new PlayerHealth(t);
        low.DebugSet(30f);
        low.TakeNpcHit(7, 20f, false);
        Check(low.InLastStand && Mathf.IsEqualApprox(low.Current, 10f), $"dropping below 25% starts the last stand ({low.Current:F0} hp)");
        float reduced = low.TakeNpcHit(7, 20f, false);
        Check(Mathf.IsEqualApprox(reduced, 20f * t.LastStandDamageScale), $"last stand reduces damage: 20 -> {reduced:F1}");
        low.TakeNpcHit(7, 35f, false);
        low.TakeNpcHit(8, 35f, false);
        Check(!low.Dead && low.Current >= 1f, $"and you can't be killed inside the grace window ({low.Current:F0} hp)");
        for (int i = 0; i < (t.LastStandSeconds + 0.1f) * 60; i++) low.Tick(1f / 60f);
        Check(!low.InLastStand, "the window is short");
        low.TakeNpcHit(7, 35f, false);
        Check(low.Dead, "after it, the fight is real again");
    }

    async Task TurretAndGunner()
    {
        Section("Crewed turret");
        await Fresh(frozen: false);
        Turret turret = Ship.Turret;
        Guard gunner = turret.Gunner;
        Check(gunner != null && gunner.Seated && turret.IsCrewed, "the gunner sits at his station and crews the turret");

        Player.DebugPlace(Ship.ToGlobal(new Vector3(-40f, 0.05f, -7f)));
        Player.LookAt2(turret.GlobalPosition);
        var hits = new List<float>();
        Player.Health.Damaged += d => hits.Add(d);
        bool telegraphed = false;
        int shotsAtTelegraph = -1;
        for (int i = 0; i < 60 * 7 && hits.Count == 0; i++)
        {
            await Ticks(1);
            if (turret.Telegraphing && shotsAtTelegraph < 0) { telegraphed = true; shotsAtTelegraph = turret.ShotsFired; }
        }
        Check(telegraphed && shotsAtTelegraph == 0, "it telegraphs with its laser before the first shot");
        Check(turret.ShotsFired > 0 && hits.Count > 0, $"it fires on the player outside ({turret.ShotsFired} shots, {hits.Count} hit)");
        if (hits.Count > 0)
            Check(hits[0] <= _main.Tunables.TurretDamage * _main.Tunables.HeavyWarningScale + 0.01f,
                $"its first hit is a reduced warning hit ({hits[0]:F1} of {_main.Tunables.TurretDamage})");

        gunner.TakeHit(9999f, gunner.GlobalPosition + Vector3.Up, Vector3.Forward, false);
        Check(gunner.Dead && !turret.IsCrewed && !turret.Hostile, "killing the gunner leaves the turret unmanned");
        int shots = turret.ShotsFired;
        await Seconds(3.5f);
        Check(turret.ShotsFired == shots, "and it stays silent");
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
