namespace Boarding;

/// <summary>
/// Every number a designer might want to turn, loaded from data/tunables.txt. The defaults
/// here only matter if a key is missing from the file; the file is the source of truth.
/// Tests may change fields on their own instance before building a world.
/// </summary>
public sealed class Tunables
{
    public const string DefaultPath = "res://data/tunables.txt";

    // Hijack
    public float HijackTotalSeconds = 30f;
    public int HijackStages = 3;
    public float HijackDecayPerSecond = 0.5f;
    public float HijackDamagePause = 1.5f;
    public string[] HijackResponses = { "wave", "bulkhead", "surge" };
    public float HijackRange = 3f;
    public float RebootSeconds = 20f;
    public int ReinforcementCount = 3;

    // Alarm
    public float AlarmAlertCooldown = 45f;
    public float AlarmCombatSeconds = 30f;
    public float LastStandHullFraction = 0.25f;
    public int LastStandHijackStage = 2;
    public float SelfDestructSeconds = 90f;
    public float SelfDestructBlastRadius = 75f;

    // Doors, charges, interaction
    public float HackSeconds = 3f;
    public float KeycardSeconds = 0.4f;
    public float ChargeFuseSeconds = 2.5f;
    public float ChargeDamage = 2600f;
    public float ChargeSplash = 260f;
    public float ChargeSplashRadius = 3.2f;
    public float ChargeNoise = 60f;
    public int StartingCharges = 3;
    public float InteractRange = 3.2f;
    public float SalvageSeconds = 2f;
    public float LootSeconds = 1f;
    public float DoorOpenRadius = 2.4f;

    // Player
    public float PlayerHealth = 100f;
    public float RegenDelay = 5f;
    public float RegenPerSecond = 8f;
    public float WalkSpeed = 5f;
    public float SprintSpeed = 8.5f;
    public float CrouchSpeed = 2.4f;
    public float StandHeight = 1.8f;
    public float CrouchHeight = 1.1f;
    public float StandEye = 1.62f;
    public float CrouchEye = 0.98f;
    public float NoiseWalk = 6f;
    public float NoiseSprint = 16f;
    public float CrouchNoiseFactor = 0.5f;
    public float NoiseGunfire = 40f;

    // No instant death
    public float NpcMaxHitFraction = 0.35f;
    public int HeavyWarningHits = 2;
    public float HeavyWarningScale = 0.3f;
    public int HeavyRampHits = 2;
    public float AttackerMemorySeconds = 8f;
    public float LastStandHealthFraction = 0.25f;
    public float LastStandSeconds = 4f;
    public float LastStandDamageScale = 0.35f;

    // Guards
    public float GuardHealth = 100f;
    public float GuardDamage = 8f;
    public float OfficerHealth = 130f;
    public float OfficerDamage = 10f;
    public float GunnerHealth = 90f;
    public float EnforcerHealth = 260f;
    public float EnforcerDamage = 30f;
    public float GuardWalkSpeed = 2f;
    public float GuardRunSpeed = 4.2f;
    public float GuardSightRange = 24f;
    public float GuardFovDegrees = 120f;
    public float GuardHearing = 1f;
    public float GuardTelegraph = 0.55f;
    public int GuardBurst = 3;
    public float GuardBurstInterval = 0.11f;
    public float GuardBurstCooldown = 1.4f;
    public float GuardAccuracy = 0.55f;
    public float GuardStaggerDamage = 35f;

    // Turret
    public float TurretRange = 85f;
    public float TurretTelegraph = 1.3f;
    public float TurretDamage = 40f;
    public int TurretBurst = 3;
    public float TurretBurstInterval = 0.2f;
    public float TurretCooldown = 2.6f;
    public float TurretTurnSpeed = 1.6f;
    public float TurretAccuracy = 0.7f;

    // Block hit points
    public float HpHull = 500f;
    public float HpWeakHull = 180f;
    public float HpWall = 350f;
    public float HpArmor = 60000f;
    public float HpDoor = 220f;
    public float HpSecurityDoor = 2500f;
    public float HpCore = 1200f;
    public float HpTurret = 500f;
    public float HpGenerator = 400f;
    public float HpStation = 300f;
    public float HpCover = 160f;
    public float HpLoot = 400f;

    public float StageSeconds => HijackTotalSeconds / HijackStages;

    public static Tunables Load(string path = DefaultPath)
    {
        var tunables = new Tunables();
        DataFile.ApplyTo(tunables, DataFile.Load(path).Top);
        return tunables;
    }
}
