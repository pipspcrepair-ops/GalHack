using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.World;
using Slice.Game;
using Slice.Surface;
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

    /// <summary>Seated in a cockpit with its canopy shut: a sealed, heated volume.</summary>
    public bool InCockpit;

    /// <summary>Lines for the visor (helmet sealing, bottles, hard landings, deaths).</summary>
    public event Action<string> Said;

    /// <summary>When each alert was last on, in survival time (tests check harm never comes first).</summary>
    public readonly Dictionary<Alert, double> WarnedAt = new();

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
        if (InCockpit) return new Exposure(_climate.CockpitC, 21, 0, VolumeKind.Cockpit, "Cockpit", double.PositiveInfinity);
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
        double radiation = _climate.RadiationByArchetype.TryGetValue(planet.Spec.Archetype.ToString(), out double rad) ? rad : 0;
        return new Exposure(baseline + weather + day + height, oxygen, radiation, null, planet.Palette.BiomeAt(x, z).ToString(), double.PositiveInfinity);
    }

    // ---- Each tick ------------------------------------------------------------------------------

    /// <summary>Advances the suit by a tick of the survivor's work: walking, running, digging, falling.</summary>
    public TickReport Tick(float dt, Survivor survivor)
    {
        N.Vector3 at = survivor.Body.WorldPosition;
        Sim.Player.Position = new Pos(at.X, at.Y, at.Z);
        InCockpit = survivor.Seated != null;
        _miningLeft -= dt;
        Activity activity = new(PaceOf(survivor), Mining: _miningLeft > 0f, FallSpeed: survivor.ImpactSpeed);
        int events = Sim.Events.Count;
        double before = Vitals.Health;
        TickReport report = Sim.Tick(dt, activity);
        if (Vitals.Health < before) Count(DamageKindFor(report.Harm), before - Vitals.Health);
        foreach (Alert a in Enum.GetValues<Alert>())
            if (a != Alert.None && (report.Warnings & a) != 0) WarnedAt[a] = Sim.Time;
        for (int i = events; i < Sim.Events.Count; i++) Said?.Invoke(Sim.Events[i]);
        return report;
    }

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
        if (fall && lost > 0) Said?.Invoke($"Hard landing: -{lost:F0} health.");
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
