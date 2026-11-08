#nullable enable
using System;

namespace PoiGenerator.Gen;

/// <summary>
/// What the rest of a world reads off its roll: the flora, the creatures, the paint on made things,
/// the sound, and the hooks the decay and the weather read. Every number is what the rolled values'
/// <c>bias</c> fields add up to (data/world-layers.txt), clamped, plus a few general rules over the
/// roll's own tables; nothing here names a value. Most run 0 to 1, where 0 is "nothing says so".
/// The same roll always reads the same. A world with no roll reads as <see cref="Neutral"/>.
/// </summary>
public sealed class WorldReading
{
    public static readonly WorldReading Neutral = new();

    // ---- Hooks: the decay and the weather read these ----

    /// <summary>How long ago the world fell, 0 (months) to 1 (about 170 years): wear, growth through ruins, faded paint.</summary>
    public float Age { get; private init; }
    /// <summary>How hard it fell, 0 (barely touched) to 1 (falling now, or a war that left nothing standing).</summary>
    public float Severity { get; private init; }
    /// <summary>How stormy its layers say the air is, 0 (dead calm) to 1 (a perpetual storm): an input for the weather, which decides.</summary>
    public float Storm { get; private init; }
    /// <summary>How dry its layers say it is, 0 to 1 (drought, bare climate, desert): an input for the weather and the ground.</summary>
    public float Dry { get; private init; }

    // ---- Flora ----

    /// <summary>How much stranger than its type the world's plants are, -0.2 to 0.6: added to the type's own range.</summary>
    public float Alien { get; private init; }
    public float Overgrown { get; private init; }
    public float Dead { get; private init; }
    /// <summary>Swollen, fused, wrongly coloured growth: the mutagenic world's plants.</summary>
    public float Swollen { get; private init; }
    public float Fungal { get; private init; }
    /// <summary>Burned off and coming back thin: blackened bark, low regrowth.</summary>
    public float Burnt { get; private init; }
    public float Sparse { get; private init; }

    // ---- Creatures ----

    /// <summary>Stock gone half wild in the fields: farmed worlds.</summary>
    public float Stock { get; private init; }
    /// <summary>Working beasts loose or still at work: low-tech living, caravans.</summary>
    public float Working { get; private init; }
    /// <summary>Pets and stock gone feral through the streets: fallen cities, places walked out of.</summary>
    public float Ferals { get; private init; }
    /// <summary>Mutated predators: biohazards, reactors, mutagen.</summary>
    public float Predators { get; private init; }
    public float Mutagen { get; private init; }
    /// <summary>How much wildlife the world carries against its type's usual: 0.15 (nearly nothing alive) to 1.3.</summary>
    public float Density { get; private init; } = 1f;

    // ---- Sound ----

    /// <summary>Emptiness: a dead world, the walked-out, the silenced.</summary>
    public float Desolate { get; private init; }
    /// <summary>Strife: war, front lines, warlords, far guns.</summary>
    public float Strife { get; private init; }
    /// <summary>Living warmth: a frontier getting on with it, a recovered world.</summary>
    public float Living { get; private init; }
    /// <summary>Ash in the air: a nuclear or ash fall.</summary>
    public float Ash { get; private init; }

    /// <summary>A Hollow world: overrun by the growth, rare.</summary>
    public bool HollowWorld { get; private init; }
    public bool Settled { get; private init; }
    /// <summary>The colours made things take, faded and dirtied by age and the fall; null where the roll gives no palette.</summary>
    public WorldPaint? Paint { get; private init; }

    static float U(float v) => Math.Clamp(v, 0f, 1f);

    public static WorldReading Of(WorldRoll? roll)
    {
        if (roll == null) return Neutral;
        float L(string k) => roll.Lean(k);
        bool settled = roll.Fact("settled") == "yes";
        // Farmland is farmland whichever value laid it out: stock grazes where the plan has fields.
        float fields = MathF.Min(0.3f, roll.RegionCount("farmland") * 0.06f);
        float age = U(L("age")), severity = U(L("severity")), ash = U(L("ash")), dead = U(L("dead"));
        float fade = U(0.12f + 0.45f * age + 0.25f * severity + L("fade"));
        float dirt = U(0.08f + 0.3f * age + 0.3f * severity + 0.3f * ash + L("dirt"));
        return new WorldReading
        {
            Age = age,
            Severity = severity,
            Storm = U(0.3f + L("storm")),
            Dry = U(L("dry")),
            Alien = Math.Clamp(L("alien") + 0.15f * MathF.Max(0f, roll.Depth), -0.2f, 0.6f),
            Overgrown = U(L("overgrown")),
            Dead = dead,
            Swollen = U(L("swollen")),
            Fungal = U(L("fungal")),
            Burnt = U(L("burnt")),
            Sparse = U(L("sparse")),
            Stock = settled ? U(L("stock") + fields) : 0f,
            Working = settled ? U(L("working")) : 0f,
            Ferals = settled ? U(L("ferals") * (0.5f + 0.5f * severity)) : 0f,
            Predators = U(L("predators")),
            Mutagen = U(L("mutagen")),
            Density = Math.Clamp(1f - L("barren") - 0.25f * dead, 0.15f, 1.3f),
            Desolate = U(L("desolate") + (settled ? 0f : 0.1f)),
            Strife = U(L("strife")),
            Living = U(L("living")),
            Ash = ash,
            HollowWorld = roll.HollowWorld,
            Settled = settled,
            Paint = roll.Paint.Count >= 5 ? new WorldPaint(roll.Paint[0], roll.Paint[1], roll.Paint[2], roll.Paint[3], roll.Paint[4], fade, dirt) : null,
        };
    }

    public override string ToString() =>
        $"age {Age:F2} severity {Severity:F2} storm {Storm:F2} dry {Dry:F2} | alien {Alien:+0.00;-0.00} over {Overgrown:F2} dead {Dead:F2} swollen {Swollen:F2} fungal {Fungal:F2} burnt {Burnt:F2} sparse {Sparse:F2}"
        + $" | stock {Stock:F2} working {Working:F2} ferals {Ferals:F2} predators {Predators:F2} mutagen {Mutagen:F2} density {Density:F2}"
        + $" | desolate {Desolate:F2} strife {Strife:F2} living {Living:F2} ash {Ash:F2}{(HollowWorld ? " | HOLLOW WORLD" : "")}";
}
