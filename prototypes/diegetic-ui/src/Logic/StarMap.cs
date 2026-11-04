using System;
using System.Collections.Generic;

namespace DiegeticUi.Logic;

public enum BodyKind { Star, Rocky, Ocean, GasGiant, Station, Unknown }

/// <summary>A place on the map. Undiscovered bodies stay dark: you know something is there, not what.</summary>
public sealed class Body
{
    public string Name { get; init; }
    public BodyKind Kind { get; init; }
    /// <summary>Orbit radius in map units (the star system fits in about 1.2).</summary>
    public double Orbit { get; init; }
    public double PeriodSeconds { get; init; } = 60;
    public double Phase { get; init; }
    public double Size { get; init; }
    public uint Color { get; init; }     // 0xRRGGBB
    public bool Discovered { get; set; }
    public Hazard Hazards { get; init; }
    public string Note { get; init; } = "";

    /// <summary>Angle around the star at a time, radians.</summary>
    public double AngleAt(double t) => Orbit <= 0 ? 0 : Phase + t / PeriodSeconds * Math.Tau;

    public (double X, double Y) PositionAt(double t)
    {
        double a = AngleAt(t);
        return (Math.Cos(a) * Orbit, Math.Sin(a) * Orbit);
    }
}

/// <summary>The current star system, shared by the bridge holo-table and the visor mini-map.</summary>
public sealed class StarMap
{
    public string SystemName { get; }
    public IReadOnlyList<Body> Bodies { get; }
    public int Version { get; private set; }

    /// <summary>The body the player selected on the holo-table, or null. The visor marks it.</summary>
    public Body Selected { get; private set; }

    /// <summary>Where the ship is, as an orbit around the star.</summary>
    public Body Ship { get; }

    public StarMap(string systemName, IReadOnlyList<Body> bodies, Body ship)
    {
        SystemName = systemName;
        Bodies = bodies;
        Ship = ship;
    }

    public void Select(Body body)
    {
        if (Selected == body) return;
        Selected = body;
        Version++;
    }

    /// <summary>The prototype's system: a star, three known worlds, a depot and two dark contacts.</summary>
    public static StarMap Kessler()
    {
        var bodies = new List<Body>
        {
            new() { Name = "KESSLER", Kind = BodyKind.Star, Size = 0.11, Color = 0xFFD27A, Discovered = true, Note = "K-TYPE STAR" },
            new() { Name = "ASH", Kind = BodyKind.Rocky, Orbit = 0.30, PeriodSeconds = 40, Phase = 0.6, Size = 0.035,
                Color = 0xE8743B, Discovered = true, Hazards = Hazard.Heat | Hazard.Radiation, Note = "SCORCHED ROCK  -  420°C DAYSIDE" },
            new() { Name = "MERIDIAN", Kind = BodyKind.Ocean, Orbit = 0.52, PeriodSeconds = 75, Phase = 2.4, Size = 0.05,
                Color = 0x2EC4B6, Discovered = true, Hazards = Hazard.Cold, Note = "ICE SHELF OCEAN  -  YOUR CRASH SITE" },
            new() { Name = "KESSLER DEPOT", Kind = BodyKind.Station, Orbit = 0.62, PeriodSeconds = 90, Phase = 3.3, Size = 0.025,
                Color = 0xF4A100, Discovered = true, Note = "TRADER  -  BUYS ORE, SELLS PARTS" },
            new() { Name = "TALLOW", Kind = BodyKind.GasGiant, Orbit = 0.82, PeriodSeconds = 130, Phase = 4.6, Size = 0.085,
                Color = 0xD9A25F, Discovered = true, Hazards = Hazard.Toxic | Hazard.Radiation, Note = "RINGED GIANT  -  6 MOONS" },
            new() { Name = "CONTACT 5", Kind = BodyKind.Unknown, Orbit = 1.02, PeriodSeconds = 170, Phase = 1.2, Size = 0.045,
                Color = 0x6D597A, Discovered = false, Note = "UNSURVEYED  -  SEND A SCANNER" },
            new() { Name = "CONTACT 6", Kind = BodyKind.Unknown, Orbit = 1.2, PeriodSeconds = 220, Phase = 5.4, Size = 0.04,
                Color = 0x6D597A, Discovered = false, Note = "UNSURVEYED  -  FAINT SIGNAL" },
        };
        var ship = new Body { Name = "YOU", Kind = BodyKind.Station, Orbit = 0.55, PeriodSeconds = 75, Phase = 2.55, Size = 0.01 };
        return new StarMap("KESSLER SYSTEM", bodies, ship);
    }
}
