using System;
using System.Collections.Generic;
using Planets.Gen;

namespace PlanetSurface.Skies;

/// <summary>One mark on a body with no ground: a storm on a giant, a spot on a star. Its place is a latitude and a longitude (radians) and a size in the same unit.</summary>
public readonly record struct Mark(double Latitude, double Longitude, double Size);

/// <summary>
/// What a sun or a gas giant has in place of a surface: bands (a giant's), storms (its great one first)
/// and spots (a star's). They are painted on it from afar (<see cref="BodySurfaces"/>) and are all there is to
/// it: neither can be landed on, and each is approached the way Empyrion has it, an orbit zone with the body
/// out of reach in the sky. Rolled from the body's own seed, so everyone sees the same one.
/// </summary>
public sealed record Geography(float BandPhase, float BandCount, IReadOnlyList<Mark> Marks)
{
    /// <summary>The geography of a giant (bands, a great storm and a few lesser ones) or a star (spots); null for anything with ground.</summary>
    public static Geography Of(Orbiter b)
    {
        if (b.Kind is not (OrbiterKind.GasGiant or OrbiterKind.Star)) return null;
        var rng = new Rng(Hash.Of(b.Seed, 0x4F56));
        float phase = rng.NextFloat() * 6f, count = b.Kind == OrbiterKind.GasGiant ? 15f + rng.NextFloat() * 10f : 0f;
        var marks = new List<Mark>();
        // A giant's great storm is the first mark and the biggest; a star has only its spots.
        int n = b.Kind == OrbiterKind.GasGiant ? 1 + rng.Next(3) : 2 + rng.Next(5);
        for (int i = 0; i < n; i++)
        {
            double size = b.Kind == OrbiterKind.GasGiant ? (i == 0 ? 0.1 + 0.1 * rng.NextFloat() : 0.03 + 0.04 * rng.NextFloat()) : 0.04 + 0.06 * rng.NextFloat();
            marks.Add(new Mark((rng.NextFloat() - 0.5f) * 1.1, rng.NextFloat() * Math.Tau, size));
        }
        return new Geography(phase, count, marks);
    }
}
