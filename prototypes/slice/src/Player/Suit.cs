using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Structures;
using GalHack.Terrain;
using Godot;
using PhysicsSandbox.Bridges;
using Planets.Gen;
using PlanetSurface.World;
using Slice.Game;
using Slice.Surface;
using Slice.Vessels;
using SurvivalVitals.Model;
using N = System.Numerics;

namespace Slice.Player;

/// <summary>
/// The survival suit: survival-vitals' model (food, oxygen, body temperature, radiation,
/// stamina, health, the replicator and the kit) worn on the generated planet. The planet, its
/// sky and its weather supply what the suit feels (<see cref="Exposure"/>); the survivor's
/// movement supplies how hard it is working. Every hit the physics module deals arrives here in
/// full; a fall can only hurt once the visor has flashed IMPACT.
/// </summary>
public sealed class Suit : IDamageSink
{
    public readonly SurvivalSim Sim;
    readonly SurfaceZone _zone;
    readonly SliceRules.ClimateRules _climate;

    public Vitals Vitals => Sim.Player.Vitals;
    public SurvivalVitals.Model.Inventory Kit => Sim.Player.Inventory;
    public TickReport Last => Sim.Last;
    public double HealthShare => Vitals.Health / Sim.Data.Vitals.Health.Max;
    public double FoodShare => Vitals.Food / Sim.Data.Vitals.Food.Max;
    public double OxygenShare => Sim.OxygenCapacity > 0 ? Vitals.Oxygen / Sim.OxygenCapacity : 0;
    public bool Dead => Sim.Player.Dead;

    /// <summary>
    /// In a sealed room aboard a structure: heated, with air to breathe. A command chair gives no
    /// air of its own: in the open, or in a leaking hull, the suit is all you have.
    /// </summary>
    public bool InSealedRoom;

    /// <summary>Somewhere that counts as a sealed room without being a structure's: the cryo bunker, heated and with air of its own.</summary>
    public Func<bool> Sheltered;

    /// <summary>Out of a storm's reach (the game's cover check, physics step only).</summary>
    public Func<bool> UnderCover;

    /// <summary>Lines for the visor (helmet sealing, bottles, hard landings, deaths).</summary>
    public event Action<string> Said;

    /// <summary>When each alert last came on, in survival time (tests check harm never comes first).</summary>
    public readonly Dictionary<Alert, double> WarnedAt = new();
    Alert _warning;

    /// <summary>Health lost, by cause, since the start (tests).</summary>
    public readonly Dictionary<DamageKind, double> Lost = new();

    float _miningLeft;

    public Suit(SurfaceZone zone, SurvivalData data, int seed)
    {
        _zone = zone;
        _climate = zone.Data.Rules.Climate;
        Sim = new SurvivalSim(data, seed) { Environment = Exposure };
    }

    // ---- What the suit feels -----------------------------------------------------------------

    /// <summary>
    /// The planet at a place and time, as survival's model wants it: the generator's temperature
    /// field spread over the climate's range, the weather's own swing on top, warmer by day and
    /// colder with height; the sky's air for oxygen; the world's background radiation.
    /// </summary>
    public Exposure Exposure(Pos p, double time)
    {
        if (InSealedRoom) return new Exposure(_climate.SealedRoomC, 21, 0, VolumeKind.Room, "Sealed room", double.PositiveInfinity);
        PlanetWorld planet = _zone.Planet;
        if (planet == null) return SurvivalVitals.Model.Exposure.Mild;
        double x = Mathx.WrapX(p.X, planet.Width), z = Mathx.Wrap(p.Z, planet.Height);
        float field = planet.Map.Sample(planet.Map.Temperature, x, z);
        double baseline = _climate.ColdestC + (_climate.HottestC - _climate.ColdestC) * field;
        // The weather model's temperature is its archetype's base plus the weather's own change;
        // only the change is added here (the base is already in the field).
        double weather = _zone.Weather.Sim == null ? 0 : _zone.Weather.Sim.Current.Temperature - _zone.Weather.Sim.Archetype.BaseTemperature;
        double day = _climate.DayNightSwing * Math.Clamp(_zone.Sky.ToSun.Y * 2f, -1f, 1f);
        double height = -_climate.LapsePerKm * Math.Max(0, p.Y) / 1000.0;
        string air = planet.Sky.Air.ToString();
        double oxygen = _climate.OxygenByAir.TryGetValue(air, out double o2) ? o2 : 21;
        if (p.Y > _zone.Physics.Zone.Ceiling) oxygen = 0;
        // Head under water: nothing to breathe, the suit on its own oxygen (or held breath).
        if (_zone.Water.Under(new Godot.Vector3((float)p.X, (float)p.Y + Survivor.EyeHeight, (float)p.Z), out _)) oxygen = 0;
        double radiation = _climate.RadiationByArchetype.TryGetValue(planet.Spec.Archetype.ToString(), out double rad) ? rad : 0;
        double temperature = baseline + weather + day + height;
        var rules = _zone.Data.Rules.ClimateNode;
        var here = new Godot.Vector3((float)p.X, (float)p.Y, (float)p.Z);
        foreach (Device d in _zone.Devices)
            if (d.Burning && d.GlobalPosition.DistanceSquaredTo(here) <= rules.Radius * rules.Radius)
            {
                temperature = Math.Clamp(temperature, rules.LowC, rules.HighC);
                break;
            }
        return new Exposure(temperature, oxygen, radiation, null, planet.Palette.BiomeAt(x, z).ToString(), LavaMetres(p));
    }

    static readonly float[] LavaRings = { 1f, 2f, 3f, 5f, 8f, 12f };
    bool _riding;

    /// <summary>
    /// How far the nearest lava is, metres: 0 standing in it, the ring it first shows on, or nowhere
    /// near. Someone in a seat or on a bike is carried over it, and only what lies around their
    /// own height counts: not the lava at the foot of a cliff you stand on.
    /// </summary>
    double LavaMetres(Pos p)
    {
        PlanetWater liquid = _zone.Water;
        if (liquid == null || !liquid.IsLava || _riding) return double.PositiveInfinity;
        float x = (float)p.X, y = (float)p.Y, z = (float)p.Z;
        if (liquid.LiquidAt(x, z, out float surface) && y <= surface + 0.6f) return 0;
        foreach (float r in LavaRings)
            for (int i = 0; i < 12; i++)
            {
                float a = i * MathF.Tau / 12f;
                if (liquid.LiquidAt(x + MathF.Cos(a) * r, z + MathF.Sin(a) * r, out surface) && surface > y - 6f) return r;
            }
        return double.PositiveInfinity;
    }

    Structure _roomsOf;
    int _roomsVersion = -1;
    readonly HashSet<Int3> _sealed = new();

    /// <summary>Is the survivor in a sealed room of the structure they sit in or stand aboard? (Building's room check, cached per structure version.)</summary>
    bool InRoom(Survivor survivor)
    {
        Vessel v = survivor.Seated ?? survivor.Aboard;
        if (v == null) return false;
        Structure s = v.Structure;
        if (s != _roomsOf || s.Version != _roomsVersion)
        {
            _roomsOf = s;
            _roomsVersion = s.Version;
            _sealed.Clear();
            foreach (Room room in Rooms.Detect(s).Sealed)
                foreach (Int3 cell in room.Cells) _sealed.Add(cell);
        }
        if (_sealed.Count == 0) return false;
        N.Vector3 chest = v.Body.ToLocal(survivor.Body.WorldPosition + survivor.Body.Up * 1f);
        if (survivor.Seated != null && v.Seat is N.Vector3 seat) chest = seat;
        return _sealed.Contains(StructureBridge.CellAt(s, chest));
    }

    // ---- Each tick ------------------------------------------------------------------------------

    /// <summary>Advances the suit by a tick of the survivor's work: walking, running, digging, falling.</summary>
    public TickReport Tick(float dt, Survivor survivor)
    {
        N.Vector3 at = survivor.Body.WorldPosition;
        Sim.Player.Position = new Pos(at.X, at.Y, at.Z);
        InSealedRoom = InRoom(survivor) || (Sheltered?.Invoke() ?? false);
        _riding = survivor.Seated != null || survivor.OnBike != null;
        _miningLeft -= dt;
        Activity activity = new(PaceOf(survivor), Mining: _miningLeft > 0f, FallSpeed: survivor.ImpactSpeed);
        double before = Vitals.Health;
        TickReport report = Sim.Tick(dt, activity);
        if (Vitals.Health < before) Count(DamageKindFor(report.Harm), before - Vitals.Health);
        Outdoors(dt, survivor, report);
        foreach (Alert a in Enum.GetValues<Alert>())
            if (a != Alert.None && (report.Warnings & a) != 0 && (_warning & a) == 0) WarnedAt[a] = Sim.Time;
        _warning = report.Warnings;
        // The model's own lines (helmet sealed, oxygen low, a hard landing) stay off the visor: the vitals show them.
        return report;
    }

    /// <summary>
    /// What the suit can't keep out, on top of survival's slow vitals, all warned on the visor as it
    /// comes on. A blizzard or a fire storm, for anyone left out in it.
    /// </summary>
    void Outdoors(float dt, Survivor survivor, TickReport report)
    {
        if (Dead || _zone.Weather.Sim is not { } weather) return;
        IceWater(dt, survivor, report);
        float exposure = weather.Here.Exposure; // the region's: a blizzard that falls as rain down here does not freeze you
        if (exposure < _climate.StormWarnFrom || InSealedRoom) return;
        if (_zone.Water?.Under(survivor.WorldEye(), out _) == true || (UnderCover?.Invoke() ?? false)) return;
        bool heat = weather.Data.State(weather.Dominant).ExposureLabel == "heat";
        Alert alert = heat ? Alert.Heat : Alert.Cold;
        report.Warnings |= alert;
        if (exposure < _climate.StormHarmFrom) return;
        report.Harm |= alert;
        report.Damage += Hit(_climate.StormHealthPerSecond * exposure * dt, heat ? DamageKind.Heat : DamageKind.Cold);
    }

    /// <summary>Ice water: the cold goes straight through the suit. The visor warns on going in, the health follows.</summary>
    void IceWater(float dt, Survivor survivor, TickReport report)
    {
        if (_riding || survivor.Body.Water.Fraction < 0.35f || report.Exposure.Temperature > _climate.IceWaterC)
        {
            _inIce = 0f;
            return;
        }
        _inIce += dt;
        report.Warnings |= Alert.Cold;
        if (_inIce < _climate.IceWaterWarnSeconds) return;
        report.Harm |= Alert.Cold;
        report.Damage += Hit(_climate.IceWaterHealthPerSecond * dt, DamageKind.Cold);
    }

    float _inIce;

    /// <summary>Working the tool: food drains faster for a moment (survival's mining rule).</summary>
    public void Worked() => _miningLeft = 0.6f;

    static Pace PaceOf(Survivor s)
    {
        if (s.Seated != null) return Pace.Idle;
        N.Vector3 v = s.Body.Velocity - s.Body.Up * N.Vector3.Dot(s.Body.Velocity, s.Body.Up);
        float speed = v.Length();
        if (speed < 0.5f) return Pace.Idle;
        if (s.Legs.Sprint && speed > s.Legs.WalkSpeed + 0.5f) return Pace.Sprint;
        return Pace.Walk;
    }

    static DamageKind DamageKindFor(Alert harm) =>
        (harm & Alert.Breath) != 0 ? DamageKind.Suffocation
        : (harm & Alert.Cold) != 0 ? DamageKind.Cold
        : (harm & Alert.Heat) != 0 ? DamageKind.Heat
        : (harm & Alert.Radiation) != 0 ? DamageKind.Radiation
        : (harm & Alert.Food) != 0 ? DamageKind.Starvation
        : (harm & Alert.Lava) != 0 ? DamageKind.Lava
        : DamageKind.Hit;

    void Count(DamageKind kind, double amount) => Lost[kind] = (Lost.TryGetValue(kind, out double had) ? had : 0) + amount;

    // ---- Damage --------------------------------------------------------------------------------

    /// <summary>
    /// The physics module's damage (the one formula, uncapped), as a share of the body's
    /// toughness, taken from health in full. Landing from a fall the visor flashed for is a fall
    /// (suit armor and boots soften it, survival's own rule); anything else is a hit.
    /// </summary>
    public float Take(Body body, float raw)
    {
        if (raw <= 0f || Dead) return 0f;
        bool fall = (Last.Warnings & Alert.Fall) != 0; // the visor was flashing IMPACT
        double health = raw / body.Integrity * Sim.Data.Vitals.Health.Max;
        double lost = Hit(health, fall ? DamageKind.Fall : DamageKind.Hit);
        return (float)(lost / Sim.Data.Vitals.Health.Max * body.Integrity);
    }

    /// <summary>Health lost to a hit of some kind (a creature's bite, a strike), armor applied.</summary>
    public double Hit(double amount, DamageKind kind)
    {
        if (Dead) return 0;
        double lost = Vitals.Damage(amount, kind, Sim.Mods);
        Count(kind, lost);
        if (Vitals.Dead)
        {
            int events = Sim.Events.Count;
            Sim.Tick(0, default); // lets the model notice: the backpack and the death message
            for (int i = events; i < Sim.Events.Count; i++) Said?.Invoke(Sim.Events[i]);
        }
        return lost;
    }

    public double TotalLost
    {
        get
        {
            double t = 0;
            foreach (double v in Lost.Values) t += v;
            return t;
        }
    }
}
