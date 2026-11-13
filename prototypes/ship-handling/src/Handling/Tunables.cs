using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ShipHandling.Handling;

/// <summary>
/// Every handling number, loaded from data/handling.json. Plain classes so the file maps onto
/// them one to one; the comments in the file say what each number means.
/// </summary>
public sealed class Tunables
{
    public PlanetData Planet { get; set; } = new();
    public SpeedsData Speeds { get; set; } = new();
    public TurningData Turning { get; set; } = new();
    public AssistData Assist { get; set; } = new();
    public BoostData Boost { get; set; } = new();
    public HoverData Hover { get; set; } = new();
    public GearData Gear { get; set; } = new();
    public ContactData Contacts { get; set; } = new();
    public PartsData Parts { get; set; } = new();

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Parses the tunables file. The caller reads the file, so this stays engine-free.</summary>
    public static Tunables FromJson(string json) =>
        JsonSerializer.Deserialize<Tunables>(json, Options) ?? throw new InvalidOperationException("empty tunables file");

    /// <summary>The mass band a structure of this many tonnes is in: the first it is below, else the last.</summary>
    public BandData Band(float tonnes) => Speeds.Bands.Find(b => b.Below > 0f && tonnes < b.Below) ?? Speeds.Bands[^1];

    public sealed class PlanetData
    {
        public string Name { get; set; } = "Planet";
        public float Gravity { get; set; } = 9.8f;
        public float AtmosphereCeiling { get; set; } = 600f;
    }

    public sealed class SpeedsData
    {
        public SpeedData Hover { get; set; } = new();
        public List<BandData> Bands { get; set; } = new();
    }

    public class SpeedData
    {
        public float TopSpeedAir { get; set; }
        public float TopSpeedSpace { get; set; }
    }

    /// <summary>One mass band, tonnes: its top speeds and its turn cap (°/s).</summary>
    public sealed class BandData : SpeedData
    {
        public float Below { get; set; }
        public float MaxTurn { get; set; }
    }

    public sealed class TurningData
    {
        public float Floor { get; set; }
        public float RollFactor { get; set; }
        public float SpinUpTime { get; set; }
    }

    public sealed class AssistData
    {
        public float MinBrake { get; set; }
        public float EaseRate { get; set; }
        public float StopBelow { get; set; }
        public float CruiseRate { get; set; }
    }

    public sealed class BoostData
    {
        public float Multiplier { get; set; }
        public float Duration { get; set; }
        public float Cooldown { get; set; }
    }

    public sealed class HoverData
    {
        public float VerticalGain { get; set; }
        public float MaxRise { get; set; }
        public float MaxGrip { get; set; }
        public float GripBand { get; set; }
        public float MaxClimbRate { get; set; }
        public float LookAheadTime { get; set; }
        public float LookAheadMin { get; set; }
        public float LookAheadMax { get; set; }
        public float StepAllowance { get; set; }
        public float LevelRate { get; set; }
        public float MaxSlope { get; set; }
        public float JumpSpeed { get; set; }
        public float JumpCooldown { get; set; }
        public float ProbeDepth { get; set; }
    }

    public sealed class GearData
    {
        public float DeployAltitude { get; set; }
        public float DeploySpeed { get; set; }
        public float Hysteresis { get; set; }
        public float ExtendTime { get; set; }
        public float LandSpeed { get; set; }
        public float IdleBeforeLanding { get; set; }
        public float SettleTime { get; set; }
    }

    public sealed class ContactData
    {
        public float SafeSpeed { get; set; }
        public float DamageScale { get; set; }
        public float HullPointsPerTonne { get; set; }
        public float Restitution { get; set; }
        public float Scrape { get; set; }
        public float CraterSpeed { get; set; }
        public float CraterScale { get; set; }
        public float CraterMaxRadius { get; set; }
    }

    public sealed class PartsData
    {
        public Dictionary<string, ThrusterPart> Thrusters { get; set; } = new();
        public Dictionary<string, GyroPart> Gyros { get; set; } = new();
        public Dictionary<string, HoverPart> HoverEngines { get; set; } = new();
        public Dictionary<string, GearPart> LandingGear { get; set; } = new();
    }

    public sealed class ThrusterPart
    {
        public float Thrust { get; set; }
        public float Mass { get; set; }
    }

    public sealed class GyroPart
    {
        public float Torque { get; set; }
        public float Mass { get; set; }
    }

    public sealed class HoverPart
    {
        public float Lift { get; set; }
        public float RideHeight { get; set; }
        public float Mass { get; set; }
    }

    public sealed class GearPart
    {
        public float Length { get; set; }
        public float Mass { get; set; }
    }
}
