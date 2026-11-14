using System;

namespace Wildlife;

/// <summary>How a group takes to an outsider, as a whole.</summary>
public enum Stance { Welcoming, Bargaining, Guarded, Wary, Superstitious, Indifferent, Hostile }

/// <summary>What a stance comes down to, meeting an outsider: they carry on, they watch and warn, or they fight.</summary>
public enum Leaning { Friendly, Wary, Hostile }

/// <summary>
/// A group's temper toward outsiders: rolled per group from its tech and its world, never set per
/// kind of group. Low-tech and post-collapse groups lean superstitious, wary or hostile; mid-tech
/// guarded or bargaining; high-tech calm, scanning or indifferent. Tendencies only: a friendly
/// low-tech village and a hostile high-tech outpost both happen. Each person varies round it, and
/// the first to react sets the way the rest go.
/// </summary>
public sealed class Temper
{
    public float Wariness, Openness, Territorial, Escalation;
    public Stance Stance;

    /// <summary>
    /// Tech is "low", "mid" or "high"; collapse 0 (stable) to 1 (falling apart). A group's own makeup
    /// leans it on top (-1 to 1 each): <paramref name="lean"/> toward a fight (raiders) or away from
    /// one (a column with its children), <paramref name="caution"/> toward keeping you off.
    /// </summary>
    public static Temper Roll(string tech, float collapse, ulong seed, float lean = 0f, float caution = 0f)
    {
        var rng = Rng.For(seed, "temper");
        float t = tech switch { "low" => 0f, "high" => 1f, _ => 0.5f };
        var temper = new Temper
        {
            Wariness = Math.Clamp(rng.Range(0.1f, 0.9f) + 0.3f * (0.5f - t) + 0.35f * collapse + 0.3f * caution, 0f, 1f),
            Openness = Math.Clamp(rng.Range(0.1f, 0.9f) - 0.25f * collapse - 0.2f * caution, 0f, 1f),
            Territorial = rng.Range(0f, 1f),
            Escalation = Math.Clamp(rng.Range(0.1f, 0.9f) + 0.2f * collapse, 0f, 1f),
        };
        float fight = MathF.Max(lean, 0f), peace = MathF.Max(-lean, 0f);
        // The odds by tech and world, then the group's own roll among them.
        (Stance s, float w)[] odds =
        {
            (Stance.Welcoming, (0.5f + temper.Openness) * (1f - 0.9f * fight) * (1f - 0.5f * collapse)),
            (Stance.Bargaining, (0.3f + 1.2f * (1f - MathF.Abs(t - 0.5f) * 2f)) * (1f - 0.9f * fight) * (1f - 0.5f * collapse)),
            (Stance.Guarded, 0.4f + 0.6f * (1f - MathF.Abs(t - 0.5f) * 2f) + 0.3f * temper.Territorial),
            (Stance.Wary, 0.3f + temper.Wariness),
            (Stance.Superstitious, 1.4f * (1f - t) * (0.4f + temper.Wariness)),
            (Stance.Indifferent, 1.6f * t * (1f - temper.Wariness)),
            (Stance.Hostile, ((0.15f + 0.8f * collapse + 0.4f * temper.Territorial * temper.Escalation) * (1f + 3f * fight) + 4f * fight * fight) * (1f - peace)),
        };
        temper.Stance = rng.Weighted(odds, o => o.w).s;
        return temper;
    }

    /// <summary>One person's take: the group's, or now and then a step off it by their own nature.</summary>
    public Stance PersonStance(Rng rng) => rng.Chance(0.75f) ? Stance : (Stance)Math.Clamp((int)Stance + (rng.Chance(0.5f) ? 1 : -1), 0, 6);

    public static Leaning LeaningOf(Stance s) => s switch
    {
        Stance.Hostile => Leaning.Hostile,
        Stance.Guarded or Stance.Wary or Stance.Superstitious => Leaning.Wary,
        _ => Leaning.Friendly,
    };

    /// <summary>Standing (-100 to 100) below which they keep you at arm's length, above which the cold warm to you, and at which they turn on you.</summary>
    public const float StandingWary = -20f, StandingWarm = 40f, StandingHostile = -80f;

    /// <summary>
    /// How your standing with them moves a stance, the same for a town's people and a band met on
    /// the road: far enough down they turn on you, below the wary line they keep you off (a guard
    /// guarded, the rest wary), and among friends the cold warm.
    /// </summary>
    public static Stance ByStanding(Stance s, float standing, bool keeper)
    {
        if (standing <= StandingHostile) return Stance.Hostile;
        if (standing < StandingWary) return keeper ? Stance.Guarded : Stance.Wary;
        if (standing > StandingWarm && s is Stance.Wary or Stance.Guarded or Stance.Indifferent) return Stance.Welcoming;
        return s;
    }

    /// <summary>What a body does at an outsider coming up, from a stance and the job's own way (a guard warns, a trader shows wares, a raider taunts).</summary>
    public static BodyAct ActFor(Stance stance, BodyAct job) => stance switch
    {
        Stance.Welcoming => job is BodyAct.Present ? BodyAct.Present : job is BodyAct.Taunt ? BodyAct.Taunt : BodyAct.Greet,
        Stance.Bargaining => job == BodyAct.Present ? BodyAct.Present : BodyAct.Beckon,
        Stance.Guarded => job is BodyAct.Warn or BodyAct.Taunt ? job : BodyAct.None,
        Stance.Wary => BodyAct.WaveOff,
        Stance.Superstitious => BodyAct.Warn,
        Stance.Indifferent => BodyAct.None,
        _ => job == BodyAct.Taunt ? BodyAct.Taunt : job == BodyAct.Warn ? BodyAct.Warn : BodyAct.WaveOff,
    };
}
