using System;

namespace Wildlife;

/// <summary>
/// What a creature fights with when it carries a weapon (a person's gun or club, a machine's
/// mounted gun). The sim has no weapons of its own: the game that runs it answers these, from the
/// same weapon rows the player uses. Null hooks leave every creature to its own body's attack.
/// </summary>
public static class Arming
{
    /// <summary>How far it can shoot at a target from where it stands now (it has a gun, rounds for it and a clear line), m; 0: it can't.</summary>
    public static Func<Creature, ITarget, float> Reach;

    /// <summary>It fires at the target now, from its own muzzle; false if the shot didn't go (no rounds, no line). Its own cadence: how long it waits after.</summary>
    public static Func<WildSim, Creature, ITarget, float> Fire;

    /// <summary>A blow's damage: its hand weapon's, or bare hands' for anyone carrying nothing; below 0, its body's own attack.</summary>
    public static Func<Creature, float> BlowDamage;

    /// <summary>Whether it would shoot at this target rather than close in (and how far from).</summary>
    public static float ShootReach(Creature c, ITarget t) => Reach == null || t == null ? 0f : Reach(c, t);

    /// <summary>Who stands with whom when one of them is set on: people and machines that aren't animals, by side (a friendly place's people with each other, everyone else together).</summary>
    public static bool Fights(Species s) => (s.Figure != null || s.Mechanical) && !s.Husk && s.Domestic == DomesticKind.None;

    public static bool Friendly(Species s) => s.Figure != null && !s.Mechanical && s.Figure.Occupancy == Occupancy.Friendly;

    /// <summary>How far a group hears one of its own set on, m.</summary>
    public const float RallyRadius = 32f;
}
