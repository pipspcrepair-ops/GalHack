using System;
using Godot;
using Planets.Gen;

namespace Wildlife;

/// <summary>How hot a world is, as far as bodies and clothes care.</summary>
public enum ClimateBand { Frozen, Cold, Mild, Hot, Scorching }

/// <summary>
/// What a world is like to live on: the numbers bodies, coats, lungs and eyes answer to. Plain
/// data. Everything alive comes out of these and the seed; nothing asks which world it is.
/// </summary>
public sealed class WorldConditions
{
    public ulong Seed;
    public float Alienness;
    /// <summary>A Hollow world: the fungus has its wildlife (most of it is Hollows). How often worlds are, the world's own layer decides.</summary>
    public bool Bloom;
    /// <summary>How far in toward the galaxy's core, 0 on the rim to 1 at the core: the Husks spread outward from there. The rim when nothing says.</summary>
    public float Coreward;
    /// <summary>Mean surface temperature, C.</summary>
    public float TempC = 15f;
    /// <summary>Oxygen in the air, percent.</summary>
    public float Oxygen = 21f;
    /// <summary>No air to speak of: nothing that breathes lives out in it.</summary>
    public bool Vacuum;
    /// <summary>Background radiation, RAD (the suit warns above 3).</summary>
    public float Radiation;
    /// <summary>Fallout, a biolab agent, the Bloom's touch: how hard this world twists what lives on it, 0 to 1.</summary>
    public float Mutagen;
    /// <summary>The colour of the ground people stand on, roughly: what their clothes must read against.</summary>
    public Color Backdrop = new(0.32f, 0.38f, 0.22f);

    public ClimateBand Climate => TempC switch
    {
        < -8f => ClimateBand.Frozen,
        < 6f => ClimateBand.Cold,
        < 27f => ClimateBand.Mild,
        < 42f => ClimateBand.Hot,
        _ => ClimateBand.Scorching,
    };

    /// <summary>The air won't keep anyone alive for long: a mask outdoors, or a suit.</summary>
    public bool BadAir => Vacuum || Oxygen < 15f;

    /// <summary>The usual oxygen of an archetype (a game with a sky passes the sky's real value).</summary>
    static float OxygenOf(Archetype a) => a switch
    {
        Archetype.Lava => 8f, Archetype.Alien => 13f, Archetype.Arid => 18f, Archetype.Ice => 19f, _ => 21f,
    };

    static float RadiationOf(Archetype a) => a switch
    {
        Archetype.Lava => 1.5f, Archetype.Alien => 2f, Archetype.Arid => 0.5f, Archetype.Ice => 0.2f, _ => 0f,
    };

    /// <summary>A world's conditions from its spec; the same spec always gives the same world.</summary>
    public static WorldConditions Of(PlanetSpec spec, float alienness = 0.25f, bool bloom = false)
    {
        ArchetypeDef def = Archetypes.For(spec);
        var rng = Rng.For(spec.Seed, "conditions");
        var w = new WorldConditions
        {
            Seed = spec.Seed,
            Alienness = alienness,
            Bloom = bloom,
            TempC = Mathf.Lerp(-45f, 65f, def.BaseTemperature),
            Oxygen = OxygenOf(spec.Archetype),
            Radiation = RadiationOf(spec.Archetype),
            // Cratered rock with nothing but the sky above it holds no air.
            Vacuum = spec.Has(Modifier.Cratered) && spec.Archetype is Archetype.Arid or Archetype.Ice or Archetype.Lava,
        };
        if (w.Vacuum) w.Oxygen = 0f;
        w.Backdrop = w.Climate == ClimateBand.Frozen ? new Color(0.86f, 0.88f, 0.9f) : spec.Archetype switch
        {
            Archetype.Arid => new Color(0.74f, 0.6f, 0.4f), Archetype.Ice => new Color(0.8f, 0.83f, 0.86f), Archetype.Lava => new Color(0.18f, 0.15f, 0.14f), Archetype.Alien => new Color(0.3f, 0.32f, 0.38f), _ => new Color(0.32f, 0.38f, 0.22f),
        };
        // A few worlds are mutagenic, most of those alien; a high background count twists things a little anyway.
        float chance = spec.Archetype == Archetype.Alien ? 0.3f : 0.08f;
        w.Mutagen = rng.Chance(chance) ? rng.Range(0.35f, 1f) : 0f;
        w.Mutagen = MathF.Max(w.Mutagen, Math.Clamp((w.Radiation - 1f) * 0.2f, 0f, 0.3f));
        // (The fungus of a Hollow world takes animals; it twists nobody's body, so it adds no mutagen.)
        return w;
    }
}
