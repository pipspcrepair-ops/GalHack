using System;
using Godot;

namespace Wildlife;

/// <summary>
/// After a death, and the goods people give up. A body is fought by no one: whoever was after the one
/// who died drops them and goes back to what it was doing. Then each by who they are: people nearby may
/// go through the dead one's things (the motions only: they take nothing), carrion eaters find the body
/// by themselves (<see cref="Carrion"/>), and a Husk that made the kill puts its worms in (<see cref="Infest"/>).
/// And someone held at gunpoint with goods on them tosses a bundle of them down to make you leave.
/// </summary>
public static partial class Brain
{
    /// <summary>How near the body a band must be to send someone over, m; how long they crouch over it and how long they stand looking round after, s; how long the walk may take.</summary>
    const float RummageNear = 45f, RummageCrouch = 5.5f, RummageLook = 3f, RummageWalk = 40f;
    /// <summary>How close to the bag they get down over it, m (its hands in it).</summary>
    const float RummageAt = 0.7f;
    /// <summary>How long a gun has to be on them before they offer, s; the offer's length (toss, a point at it, a point at the way out); how often anyone offers at most (once a day).</summary>
    const float OfferAfter = 0.7f, OfferSeconds = 3.4f, OfferEvery = 1200f;

    /// <summary>
    /// Someone has died (in this sim's frame, their body at <paramref name="body"/>): nothing goes on
    /// fighting them. Every creature drops them (its attack, its notice, its grudge, its quarry, its
    /// hold) and goes back to what it was doing; a band at odds with them settles. Of the bands near
    /// the body that aren't running, the boldest one in some goes over to rummage through it.
    /// </summary>
    public static void TargetDied(WildSim w, ITarget t, Vector3 body)
    {
        if (t == null) return;
        foreach (Creature c in w.Creatures)
        {
            if (c.Removed || c.Dead) continue;
            bool had = c.AttackTarget == t || c.Noticed == t || c.ProvokedBy == t || c.Quarry == t || c.Holding == t || c.TellTarget == t;
            if (had) Forget(w, c, t);
        }
        foreach (Pack p in w.Packs)
            if (p.Prey == t) EndHunt(w, p, 0f);
        foreach (Herd h in w.Herds)
        {
            if (h.Hunting == t) h.Hunting = null;
            if (h.Party is not { } b || Leader(h) is not { } lead) continue;
            if (b.Foe == t || b.Outsider == t)
            {
                b.Foe = null;
                b.Outsider = null;
                if (b.Mood != PartyMood.Fleeing) Settle(w, h, b, lead, PartyMood.Easy);
            }
            if (b.Mood == PartyMood.Fleeing || WildSim.Flat(h.Center - body).Length() > RummageNear || Rummager(h, b) is not { } who) continue;
            who.Rummage = body;
            who.RummageFor = RummageCrouch + RummageLook;
            who.SetState(CState.Follow);
            who.StateTime = 0f;
        }
    }

    /// <summary>Whether someone is a body to the ones about: dead, or a player down.</summary>
    public static bool Down(ITarget t) => t?.Health?.Downed ?? false;

    /// <summary>Who of a band goes over to a dead stranger's things: the boldest grown one, readier the rougher their trade and the worse the band took to the dead; often nobody.</summary>
    static Creature Rummager(Herd h, Party b)
    {
        Creature best = null;
        float most = 0.45f, lean = b.Leaning switch { Leaning.Hostile => 0.4f, Leaning.Wary => 0.15f, _ => 0f };
        foreach (Creature m in h.Members)
        {
            if (!m.Alive || m.Removed || m.Species.Figure is not { } f || f.Stage == LifeStage.Child) continue;
            float trade = f.Role switch
            {
                PersonRole.Raider or PersonRole.Scavenger => 1f,
                PersonRole.Drunk => 0.85f,
                PersonRole.Trader or PersonRole.Mechanic => 0.7f,
                PersonRole.Worker or PersonRole.Farmer => 0.6f,
                PersonRole.Medic => 0.5f,
                PersonRole.Guard or PersonRole.Lawman => 0.45f,
                _ => 0.3f,
            };
            float score = m.Nerve * trade + lean + m.Rng.Range(-0.1f, 0.1f);
            if (score > most) { most = score; best = m; }
        }
        return best;
    }

    /// <summary>
    /// Going through a dead one's things: there at a walk, down on its haunches over the body and its bag
    /// with its hands in it, then up again looking round to see who saw. Then back to the band. Nothing
    /// is taken. True while at it.
    /// </summary>
    static bool Rummaging(WildSim w, Creature c, float dt)
    {
        if (c.RummageFor <= 0f) return false;
        float d = WildSim.Flat(c.Rummage - c.Position).Length();
        if (c.RummageFor >= RummageCrouch + RummageLook && d > RummageAt)
        {
            // On the way: a walk, eyes on it.
            if (c.State != CState.Follow) c.SetState(CState.Follow);
            if (c.StateTime > RummageWalk) { c.RummageFor = 0f; return false; } // there's no way there
            Go(w, c, c.Rummage, c.Stats.WalkSpeed);
            c.LookAt = c.Rummage + Vector3.Up * 0.3f;
            return true;
        }
        c.RummageFor -= dt;
        c.SetState(CState.Idle);
        c.Desired = Vector3.Zero;
        if (c.RummageFor > RummageLook)
        {
            c.FacePoint = c.Rummage;
            c.LookAt = c.Rummage;
            c.WantCrouch = 1f;
            if (c.Act != BodyAct.Rummage)
            {
                Hold(w, c, BodyAct.Rummage, c.RummageFor - RummageLook);
                w.Rummages++;
            }
            return true;
        }
        // Up again: a look one way and the other.
        if (c.Act == BodyAct.Rummage) c.Act = BodyAct.None;
        float a = c.Yaw + MathF.Sin(c.RummageFor * 2.1f) * 1.3f;
        c.LookAt = c.Position + new Vector3(-MathF.Sin(a), 0f, -MathF.Cos(a)) * 8f + Vector3.Up * 1.5f;
        if (c.RummageFor <= 0f) c.RummageFor = 0f;
        return true;
    }

    /// <summary>Whether a person has goods to give up: grown, and not one who stands and fights for a living.</summary>
    static bool HasGoods(Figure f) => f.Stage != LifeStage.Child && f.Role is not (PersonRole.Guard or PersonRole.Lawman or PersonRole.Raider);

    /// <summary>
    /// A gun has been on this one a moment, hands up: with goods on them and none given today, they offer
    /// (body language: "Offer something to make you leave"). Starts it: the bundle is to land on the
    /// ground between them and whoever holds the gun, nearer the gun.
    /// </summary>
    static void Offer(WildSim w, Creature c, ITarget t)
    {
        if (c.OfferFor > 0f || c.StateTime < OfferAfter || w.Time - c.OfferedAt < OfferEvery || c.Species.Figure is not { } f || !HasGoods(f)) return;
        Vector3 to = WildSim.Flat(t.Position - c.Position);
        float d = to.Length();
        if (d < 2.6f) return; // no room between them
        // A couple of strides short of you, never more than most of the way, nor at their own feet.
        float k = Math.Clamp(1f - 2.5f / d, MathF.Max(0.45f, 1.2f / d), MathF.Max(MathF.Max(0.45f, 1.2f / d), MathF.Min(0.7f, 1f - 1.4f / d)));
        Vector3 at = c.Position + to * k;
        c.OfferAt = new Vector3(at.X, w.Terrain.Height(at.X, at.Z), at.Z);
        c.OfferFor = OfferSeconds;
        c.OfferedAt = w.Time;
        // Fear sizes the bundle: the faint-hearted, and anyone close to the muzzle, give more.
        float fear = Math.Clamp(0.25f + (1f - c.Nerve) * 0.5f + (1f - d / (AimClose * 1.5f)) * 0.35f, 0f, 1f);
        w.Offers++;
        w.RaiseOffered(c, t, c.OfferAt, fear);
    }

    /// <summary>The offer as it plays: the toss toward you, a point at what landed, a point at the way out (back the way you came). True while at it.</summary>
    static bool Offering(WildSim w, Creature c, ITarget t, float dt)
    {
        if (c.OfferFor <= 0f) return false;
        c.OfferFor -= dt;
        c.SetState(CState.Freeze);
        c.Desired = Vector3.Zero;
        Vector3 you = t?.Position ?? c.OfferAt;
        c.FacePoint = you;
        Vector3 away = WildSim.Flat(you - c.Position);
        away = away.LengthSquared() > 1e-4f ? away.Normalized() : c.Forward;
        float into = OfferSeconds - c.OfferFor;
        if (into < 0.7f) { c.LookAt = c.OfferAt + Vector3.Up * 0.4f; Hold(w, c, BodyAct.Present, 0.8f); }
        else if (into < 1.9f) { c.LookAt = c.OfferAt; Hold(w, c, BodyAct.Point, 0.4f); }
        else if (into < OfferSeconds - 0.2f) { c.LookAt = you + away * 14f + Vector3.Up * 1.4f; Hold(w, c, BodyAct.Point, 0.4f); }
        if (c.OfferFor <= 0f) c.OfferFor = 0f;
        return true;
    }
}
