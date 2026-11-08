using System;
using Godot;

namespace Wildlife;

public static partial class Brain
{
    /// <summary>
    /// A Husk out in the open. It keeps the bestiary's rules (it notices by the same senses, it
    /// winds up before every attack, it walks with others of its kind) but it is eager where the
    /// living are staggered: it goes for the living at once, and what one of them is after the rest
    /// come for too. It shambles, stalls and lurches like a puppet on a bad string, rushes the last
    /// few metres, brings its maggots up at short range now and then, and closes to seize the head
    /// or swing an arm. It makes almost no sound: a murmur through the worms.
    /// </summary>
    static void HuskMind(WildSim w, Creature c, ITarget near, float dist, float dt)
    {
        HuskRules r = w.Data.Husks;
        c.Stir = Mathf.MoveToward(c.Stir, Seizing.StirOf(w, c), dt * 2f);
        if (w.Time >= c.MurmurAt)
        {
            c.MurmurAt = w.Time + c.Rng.Range(5f, 12f);
            w.RaiseMurmur(c);
        }
        ITarget prey = Seizing.PreyFor(w, c, near);
        c.Quarry = prey;
        if (prey == null)
        {
            WanderAndGraze(w, c, 22f, dt);
            if (c.Desired.LengthSquared() > 0f) c.Desired *= 0.55f + 0.45f * Seizing.Gait(c, w.Time);
            return;
        }
        float d = WildSim.Flat(prey.Position - c.Position).Length();
        if (c.State != CState.Chase) c.SetState(CState.Chase);
        c.FacePoint = prey.Position;
        c.LookAt = prey.Position + Vector3.Up * 1.5f;
        // Maggots, now and then, from a few steps off.
        if (d > Spitting.Close && d <= r.VomitReach && w.Time >= c.VomitAt && Spitting.Can(c))
        {
            c.VomitAt = w.Time + r.VomitEvery * c.Rng.Range(0.7f, 1.3f);
            if (c.Rng.Chance(0.6f) && Attacks.Start(w, c, prey)) return;
        }
        float melee = c.Stats.AttackRange + Attacks.BodyRadius;
        if (d <= melee)
        {
            c.Desired = Vector3.Zero;
            Attacks.Start(w, c, prey);
            return;
        }
        // A puppet's going: it stalls and lurches by turns; close in, a lurch is a rush.
        float gait = Seizing.Gait(c, w.Time);
        float speed = d < 6f ? c.Stats.RunSpeed * (gait >= 1f ? 1.5f : gait > 0.1f ? 0.55f : 0.1f) : c.Stats.RunSpeed * gait;
        Steer(w, c, Senses.Aim(c, prey), speed);
    }
}
