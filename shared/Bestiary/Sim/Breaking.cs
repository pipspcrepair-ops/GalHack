using System;
using Godot;

namespace Wildlife;

/// <summary>The kinds of thing that break blocks. A class of their own, never a trait any creature might roll.</summary>
public enum BreakerKind { None, Ram, Burrower, Corroder, Siege }

/// <summary>A block standing between a creature and what it wants, as the game sees it.</summary>
public struct BlockAt
{
    public Vector3 Point, Normal;
    /// <summary>Part of a player's base (a game setting may keep those safe).</summary>
    public bool PlayerBase;
    /// <summary>Whatever the game needs to find the block again.</summary>
    public object Key;
}

/// <summary>The game's blocks, for whatever batters them: what stands in the way, and a blow on it.</summary>
public interface IBlockWorld
{
    /// <summary>The first block on the way from one point to another (this sim's frame), if any.</summary>
    bool Blocking(Vector3 from, Vector3 to, out BlockAt block);
    /// <summary>A blow of this much on the block. The game's own damage rules decide what it does: a hard block shrugs off what a weak one can't.</summary>
    void Strike(in BlockAt block, float amount, BreakerKind kind);
}

/// <summary>
/// Block-breakers: rams that smash walls and doors, burrowers that come up under floors,
/// corrosive spitters that eat armour, siege beasts that batter bases. They're uncommon, and
/// generated like everything else. When what they're after is behind a block, they go at the
/// block: a wind-up that's heard and seen first (battering, dust falling from the cracks), then a
/// blow through the game's own block damage, so a scrap wall falls and combat steel holds.
/// </summary>
public static class Breaking
{
    /// <summary>How long the warning runs before the first blow, and between blows, s.</summary>
    public const float Warning = 1.6f, Between = 1.9f;
    /// <summary>How far past its reach it looks for a block in the way, m.</summary>
    public const float Reach = 2.2f, Interest = 16f;

    /// <summary>How hard one blow is: its own strength, made for the job.</summary>
    public static float Blow(Species s) => s.Stats.Damage * s.Breaker switch
    {
        BreakerKind.Siege => 3.2f, BreakerKind.Ram => 2.2f, BreakerKind.Burrower => 1.8f, BreakerKind.Corroder => 1.6f, _ => 0f,
    };

    /// <summary>One step of a breaker going at what's in its way. True while it is: the brain's steering is set aside.</summary>
    public static bool Step(WildSim w, Creature c, float dt)
    {
        if (c.Species.Breaker == BreakerKind.None || c.Dead || w.Blocks == null || c.Attacking) return Reset(c);
        ITarget t = c.Noticed ?? c.Target;
        if (t == null || !t.Attackable) return Reset(c);
        Vector3 from = c.Position + Vector3.Up * MathF.Min(1.2f, c.Species.BodyHeight * 0.6f);
        if (c.Species.Breaker == BreakerKind.Burrower) from = c.Position + Vector3.Down * 0.3f; // it comes at a floor from under it
        Vector3 to = t.Position + Vector3.Up * 1f;
        if ((to - from).Length() > Interest || !w.Blocks.Blocking(from, to, out BlockAt block)) return Reset(c);
        float d = WildSim.Flat(block.Point - c.Position).Length();
        if (d > c.Radius + Reach)
        {
            // Up to the block first, and only then the warning.
            c.BreakWind = 0f;
            c.Desired = WildSim.Flat(block.Point - c.Position).Normalized() * c.Stats.WalkSpeed;
            return true;
        }
        c.Desired = Vector3.Zero;
        c.FacePoint = block.Point;
        if (c.BreakWind <= 0f && c.BreakWind > -0.001f) Warn(w, c, block);
        c.BreakWind += dt;
        c.Brace = MathF.Min(1f, c.BreakWind / Warning);
        if (c.BreakWind < Warning) return true;
        c.BreakWind = Warning - Between;
        c.Lunge = 1f;
        if (block.PlayerBase && !w.BreakBases) return true; // the setting holds: it batters, nothing gives
        w.Blocks.Strike(block, Blow(c.Species), c.Species.Breaker);
        w.RaiseBroke(c, block);
        return true;
    }

    static void Warn(WildSim w, Creature c, BlockAt block)
    {
        w.RaiseBattering(c, block);
        w.RaiseTell(new TellEvent { Creature = c, Kind = TellKind.Dust, Position = block.Point + Vector3.Up * 1.4f, Below = block.Point, Time = w.Time, Duration = Warning });
    }

    static bool Reset(Creature c)
    {
        c.BreakWind = 0f;
        return false;
    }
}
