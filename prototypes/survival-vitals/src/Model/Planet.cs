using System;

namespace SurvivalVitals.Model;

/// <summary>
/// What the environment does to a body standing at one spot at one moment.
/// <paramref name="Volume"/> is set inside a breathable room or cockpit, where suit oxygen
/// refills. <paramref name="LavaMetres"/> is the distance to the nearest lava pool's edge
/// (zero or less means standing in it).
/// </summary>
public readonly record struct Exposure(
    double Temperature, double OxygenPercent, double Radiation, VolumeKind? Volume, string Place, double LavaMetres)
{
    /// <summary>Shirt-sleeve conditions: what tests use for "no hazard at all".</summary>
    public static Exposure Mild => new(20, 21, 0, null, "Mild", double.PositiveInfinity);
}

/// <summary>
/// The planet's climate as pure functions of place and time: hazard zones, breathable rooms,
/// faction land, the day and night swing and seeded weather. Nothing here has state, so any
/// spot can be asked about at any time, and the same seed always gives the same answers.
/// </summary>
public sealed class Planet
{
    public readonly PlanetData Data;
    public readonly int Seed;

    public Planet(PlanetData data, int seed)
    {
        Data = data;
        Seed = seed;
    }

    public double HourOfDay(double time) =>
        ((Data.StartHour + time / Data.DayLengthSeconds * 24.0) % 24.0 + 24.0) % 24.0;

    /// <summary>Warmest at 14:00, coldest at 02:00.</summary>
    public double DayNightOffset(double time) =>
        Data.DayNightSwing * Math.Sin(2 * Math.PI * (HourOfDay(time) - 8) / 24);

    public bool IsNight(double time)
    {
        double h = HourOfDay(time);
        return h < 6 || h >= 19;
    }

    /// <summary>
    /// Weather changes every interval. Each interval's weather is a hash of the seed and its
    /// index, so it never depends on how often anyone asked.
    /// </summary>
    public WeatherKind WeatherAt(double time)
    {
        var kinds = Data.Weather.Kinds;
        if (kinds.Count == 0 || Data.Weather.IntervalSeconds <= 0) return new WeatherKind { Name = "Clear" };
        long index = (long)Math.Floor(time / Data.Weather.IntervalSeconds);
        double total = 0;
        foreach (WeatherKind k in kinds) total += k.Weight;
        double pick = SeededHash.Unit(Seed, index, salt: 1) * total;
        foreach (WeatherKind k in kinds)
        {
            pick -= k.Weight;
            if (pick < 0) return k;
        }
        return kinds[^1];
    }

    public double OpenGroundTemperature(double time) =>
        Data.BaseTemperature + DayNightOffset(time) + WeatherAt(time).Temperature;

    /// <summary>1 inside a zone's radius, easing to 0 across its falloff.</summary>
    public static double ZoneWeight(Zone zone, Pos p)
    {
        double d = p.FlatDistanceTo(zone.Center);
        if (d <= zone.Radius) return 1;
        if (zone.Falloff <= 0 || d >= zone.Radius + zone.Falloff) return 0;
        double t = 1 - (d - zone.Radius) / zone.Falloff;
        return t * t * (3 - 2 * t);
    }

    public Volume VolumeAt(Pos p)
    {
        foreach (Volume v in Data.Volumes)
            if (v.Contains(p)) return v;
        return null;
    }

    public Territory TerritoryAt(Pos p)
    {
        foreach (Territory t in Data.Territories)
            if (t.Contains(p)) return t;
        return null;
    }

    public Exposure Sample(Pos p, double time)
    {
        // A breathable room or cockpit is sealed and climate-controlled: the outside can't reach in.
        if (VolumeAt(p) is Volume room)
            return new Exposure(room.Temperature, 21, 0, room.Kind, room.Name, double.PositiveInfinity);

        double temperature = OpenGroundTemperature(time);
        double oxygen = Data.OxygenPercent;
        double radiation = 0;
        double lava = double.PositiveInfinity;
        string place = "Open ground";
        double strongest = 0;

        foreach (Zone zone in Data.Zones)
        {
            if (zone.LavaRadius > 0)
                lava = Math.Min(lava, p.FlatDistanceTo(zone.Center) - zone.LavaRadius);

            double w = ZoneWeight(zone, p);
            if (w <= 0) continue;
            temperature += zone.Temperature * w;
            radiation += zone.Radiation * w;
            if (zone.OxygenPercent is double o2) oxygen += (o2 - oxygen) * w;
            if (w > strongest)
            {
                strongest = w;
                place = zone.Name;
            }
        }
        return new Exposure(temperature, oxygen, radiation, null, place, lava);
    }
}
