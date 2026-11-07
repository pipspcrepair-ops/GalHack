using System;
using Godot;

namespace Wildlife;

/// <summary>What a Husk is doing with someone it has seized.</summary>
public sealed class SeizeEvent
{
    public Creature Husk;
    public ITarget Victim;
    public float Time;
    /// <summary>On letting go: whether the victim broke the hold (and threw it off).</summary>
    public bool Broken;
    /// <summary>On turning: the Husk the victim has become.</summary>
    public Creature Turned;
}

/// <summary>
/// A Husk's signature: it seizes a head and pulls it face to face so the worms can find a way in.
/// The seize is an attack like any other (a wind-up that is the telegraph, a commit, a lunge
/// judged where it lands); what happens once it holds depends on who it holds:
/// - someone the sim keeps (a person or an animal of the wildlife, a townsperson the game hands
///   it as an <see cref="IHost"/>) is held still; after <see cref="HuskRules.TurnSeconds"/> a
///   person has turned (a new Husk, the same person, stands where they stood) and an animal is
///   dead. Only people turn.
/// - the player: the sim holds them face to face and leaves the rest to the game (an open helmet
///   is a quick death, a sealed one a struggle), which ends it with <see cref="Break"/> or
///   <see cref="Finish"/>. The player is never infected: it is death or nothing.
/// It lets go of its own accord if the victim is gone, or after <see cref="HuskRules.MostHold"/>.
/// </summary>
public static class Seizing
{
    /// <summary>How close the faces come, m (feet to feet).</summary>
    public const float FaceToFace = 0.5f;

    /// <summary>
    /// Whether this Husk can seize a head at all: only one standing at least <see cref="HuskRules.SeizeHeight"/>.
    /// A short one (a child, a small build) never does, whoever it's after: it swings and retches instead.
    /// </summary>
    public static bool CanReach(WildSim w, Creature c) => c.Species.Figure is { } f && f.Height >= w.Data.Husks.SeizeHeight;

    public static void Begin(WildSim w, Creature c, ITarget t)
    {
        c.Phase = AttackPhase.Hold;
        c.PhaseTime = 0f;
        c.HeldFor = 0f;
        c.Holding = t;
        c.Lunge = 1f;
        c.Stir = 1f;
        if (t is CreatureTarget ct)
        {
            Creature v = ct.Creature;
            v.HeldBy = c;
            if (v.Attacking) Attacks.Cancel(v);
            v.Velocity = Vector3.Zero;
        }
        Pin(w, c, t);
        w.RaiseSeized(new SeizeEvent { Husk = c, Victim = t, Time = w.Time });
    }

    /// <summary>Each step of a hold: face to face, the worms at work, and for whoever the sim keeps, the turning.</summary>
    internal static void Hold(WildSim w, Creature c, float dt)
    {
        ITarget t = c.Holding;
        c.HeldFor += dt;
        c.Desired = Vector3.Zero;
        c.Lunge = 1f;
        c.Stir = 1f;
        HuskRules r = w.Data.Husks;
        bool gone = t == null || t is CreatureTarget { Creature: { } v } && (v.Dead || v.Removed) || t is not CreatureTarget && !t.Attackable;
        if (gone || c.Dead || c.HeldFor > r.MostHold)
        {
            Release(w, c, false);
            return;
        }
        Pin(w, c, t);
        if (t is CreatureTarget or IHost && c.HeldFor >= r.TurnSeconds) Turn(w, c, t);
    }

    /// <summary>Stands it face to face with the one it holds.</summary>
    static void Pin(WildSim w, Creature c, ITarget t)
    {
        Vector3 to = WildSim.Flat(t.Position - c.Position);
        Vector3 dir = to.LengthSquared() > 1e-4f ? to.Normalized() : c.Forward;
        Vector3 at = t.Position - dir * FaceToFace;
        c.Yaw = MathF.Atan2(-dir.X, -dir.Z);
        c.FacePoint = t.Position;
        c.LookAt = t.Position + Vector3.Up * 1.5f;
        c.Velocity = Vector3.Zero;
        if (c.Space != null)
        {
            if (WildSim.Flat(at - c.Position).Length() > 0.05f) Crawl.Place(w, c, at + Vector3.Up * 0.3f, null);
            return;
        }
        c.Position = new Vector3(at.X, w.Terrain.Height(at.X, at.Z), at.Z);
    }

    /// <summary>Someone the sim keeps has had the worms long enough: a person turns, an animal dies.</summary>
    static void Turn(WildSim w, Creature c, ITarget t)
    {
        Creature made = null;
        ulong seed = Rng.Mix(c.Species.Seed, (ulong)(w.Frame * 31 + c.Id));
        if (t is CreatureTarget ct)
        {
            Creature v = ct.Creature;
            if (Husks.CanTurn(v.Species))
            {
                Species s = w.TurnHost(v.Species, seed);
                made = v.Space != null ? w.SpawnIn(s, v.Space, v.Position) : w.Spawn(s, v.Position);
                made.Yaw = v.Yaw;
                v.HeldBy = null;
                v.Removed = true; // no body: they are standing there still
            }
            else
            {
                v.HeldBy = null;
                w.Damage(v, v.Health + 1f, c.AsTarget);
            }
        }
        else if (t is IHost h && h.Body is { } body && Husks.CanTurn(body))
        {
            Species s = w.TurnHost(body, seed);
            made = w.Spawn(s, h.Position);
            made.Yaw = MathF.Atan2(-h.Forward.X, -h.Forward.Z);
            h.Taken();
        }
        if (made != null)
        {
            // It stands a moment, as if listening, before it moves on its own.
            made.ThinkEvery = 1.5f;
            made.Stir = 1f;
            w.RaiseTurned(new SeizeEvent { Husk = c, Victim = t, Time = w.Time, Turned = made });
        }
        Release(w, c, false);
    }

    /// <summary>Lets go: the hold ended. Broken, the Husk is thrown back and comes again only after a stagger.</summary>
    public static void Release(WildSim w, Creature c, bool broken)
    {
        ITarget t = c.Holding;
        if (t is CreatureTarget { Creature: { } v } && v.HeldBy == c) v.HeldBy = null;
        c.Holding = null;
        c.HeldFor = 0f;
        c.Seizing = false;
        c.Lunge = 0f;
        c.Phase = AttackPhase.Recover;
        c.PhaseTime = 0f;
        HuskRules r = w.Data.Husks;
        c.PhaseLength = broken ? r.Stagger : w.Combat.AttackCooldown;
        if (broken && t != null)
        {
            Vector3 back = WildSim.Flat(c.Position - t.Position);
            back = back.LengthSquared() > 1e-4f ? back.Normalized() : -c.Forward;
            Vector3 to = c.Position + back * r.Shove;
            if (c.Space != null) Crawl.Place(w, c, to + Vector3.Up * 0.3f, null);
            else c.Position = new Vector3(to.X, w.Terrain.Height(to.X, to.Z), to.Z);
            c.Rear = 1f;
        }
        if (t != null) w.RaiseLetGo(new SeizeEvent { Husk = c, Victim = t, Time = w.Time, Broken = broken });
    }

    /// <summary>The player broke free (the game's struggle): the Husk is shoved off and staggers.</summary>
    public static void Break(WildSim w, Creature c)
    {
        if (c.Phase == AttackPhase.Hold) Release(w, c, true);
    }

    /// <summary>It's over for the one it held (the game's verdict on a player): it lets go.</summary>
    public static void Finish(WildSim w, Creature c)
    {
        if (c.Phase == AttackPhase.Hold) Release(w, c, false);
    }

    /// <summary>The Husk holding a target, if one is.</summary>
    public static Creature Holder(WildSim w, ITarget t)
    {
        foreach (Creature c in w.Creatures)
            if (c.Phase == AttackPhase.Hold && c.Holding == t && !c.Removed) return c;
        return null;
    }

    // ---- How a Husk goes about it --------------------------------------------------------

    /// <summary>
    /// The puppet's way of going, 0 (stopped dead) to 1 (a sudden rush): it shambles, stalls and
    /// lurches by turns, each Husk on its own beat.
    /// </summary>
    public static float Gait(Creature c, float time)
    {
        float s = MathF.Sin(time * 1.9f + c.Id * 1.3f) + 0.6f * MathF.Sin(time * 3.7f + c.Id * 0.7f);
        return s > 1.05f ? 1f : s > -0.35f ? 0.5f : 0.06f;
    }

    /// <summary>
    /// Who a Husk goes for: the living. Whoever it has noticed; anyone the sim knows of near enough
    /// to sense (players, townsfolk); any person of the wildlife near, an animal if nothing better;
    /// and, as a pack, whoever another Husk near it is after.
    /// </summary>
    public static ITarget PreyFor(WildSim w, Creature c, ITarget noticed)
    {
        HuskRules r = w.Data.Husks;
        ITarget best = null;
        float bestScore = float.MaxValue, sense = c.Stats.Perception * 0.6f;
        void Consider(ITarget t, float score)
        {
            if (score < bestScore) { bestScore = score; best = t; }
        }
        if (noticed != null && noticed.Attackable) Consider(noticed, WildSim.Flat(noticed.Position - c.Position).Length() * 0.8f);
        foreach (ITarget t in w.Targets)
        {
            if (!t.Attackable || w.InTown(t.Position)) continue;
            float d = WildSim.Flat(t.Position - c.Position).Length();
            if (d < sense) Consider(t, d);
        }
        foreach (Creature o in w.Creatures)
        {
            if (o == c || o.Removed || o.Dead || o.Space != c.Space || o.Species.Mechanical || o.Species.Rig == RigKind.Swarm || o.Species.Flies && !o.Landed || o.InWalls) continue;
            float d = WildSim.Flat(o.Position - c.Position).Length();
            if (o.Species.Husk)
            {
                // The pack: what one of them is after, they all come for.
                if (d < r.Gather && o.Quarry != null && o.Quarry.Attackable && o.Quarry != c.AsTarget) Consider(o.Quarry, WildSim.Flat(o.Quarry.Position - c.Position).Length() * 0.9f);
                continue;
            }
            if (d > sense || w.InTown(o.Position)) continue;
            Consider(o.AsTarget, o.Species.Figure != null ? d : d * 1.8f);
        }
        return best;
    }

    /// <summary>How stirred its worms are: by anything moving near it, most by what's nearest.</summary>
    public static float StirOf(WildSim w, Creature c)
    {
        float reach = w.Data.Husks.StirReach, near = reach;
        foreach (ITarget t in w.Targets) near = MathF.Min(near, (t.Position - c.Position).Length());
        foreach (Creature o in w.Creatures)
            if (o != c && !o.Removed && !o.Dead && !o.Species.Husk && o.Velocity.LengthSquared() > 0.04f) near = MathF.Min(near, (o.Position - c.Position).Length());
        return Math.Clamp(1f - near / reach, 0f, 1f);
    }
}
