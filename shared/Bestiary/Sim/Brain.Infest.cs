using System;
using Godot;

namespace Wildlife;

public static partial class Brain
{
    /// <summary>How long a Husk's face is at the body's before it rises, s; between mouthfuls; how long it may take to get there; how far from the head it kneels, m.</summary>
    public const float InfestSeconds = 5f, Mouthful = 0.65f, InfestReach = 25f, KneelOff = 0.5f;

    /// <summary>
    /// A Husk killed a person: it isn't done with them. It goes to the body, kneels at the head and bends
    /// to the face with its hands on it, and heaves its worms up into it a mouthful at a time (each a gob
    /// of maggots dropped from its mouth into the face, retched up where you can hear it). Then it goes
    /// its way and the body rises: the same person as a Husk (<see cref="Husks.Turn"/>, as a hold turns
    /// the living), getting up off its back where it lay, and keeping round there. Anything that sets on
    /// the Husk breaks it off; so does losing the body.
    /// </summary>
    public static void Infest(WildSim w, Creature husk, Creature body)
    {
        if (husk.Dead || husk.Removed || husk == body || !Husks.CanTurn(body.Species) || body.Space != husk.Space) return;
        if (husk.Phase == AttackPhase.Hold) Seizing.Release(w, husk, false);
        if (husk.Attacking) Attacks.Cancel(husk);
        husk.Infests = body;
        husk.InfestFor = 0f;
        husk.NextMouthful = 0f;
        husk.SetState(CState.Follow);
        husk.StateTime = 0f;
    }

    /// <summary>A Husk at the body of someone it killed, as it goes: true while at it.</summary>
    static bool Infesting(WildSim w, Creature c, float dt)
    {
        Creature body = c.Infests;
        if (body.Removed || !body.Dead || c.ProvokedBy != null && w.Time - c.ProvokedAt < 2f || c.InfestFor <= 0f && c.StateTime > InfestReach)
        {
            c.Infests = null;
            return false;
        }
        Vector3 head = body.Rig != null && body.Rig.HitCount > 1 ? new Vector3(body.Rig.Hits[1].X, body.Rig.Hits[1].Y, body.Rig.Hits[1].Z) : body.Position;
        Vector3 side = WildSim.Flat(c.Position - head);
        side = side.LengthSquared() > 1e-4f ? side.Normalized() : -c.Forward;
        Vector3 spot = head + side * KneelOff;
        c.Stir = 1f;
        c.FacePoint = head;
        c.LookAt = head;
        if (c.InfestFor <= 0f && WildSim.Flat(spot - c.Position).Length() > 0.3f)
        {
            // Over to it, in its lurching way.
            if (c.State != CState.Follow) c.SetState(CState.Follow);
            Go(w, c, spot, c.Stats.WalkSpeed * (c.Space != null ? 1f : 0.6f + 0.6f * Seizing.Gait(c, w.Time)));
            return true;
        }
        // Down at the face, hands on it, heaving.
        c.SetState(CState.Idle);
        c.Desired = Vector3.Zero;
        c.WantCrouch = 1f;
        c.InfestFor += dt;
        if (w.Time >= c.NextMouthful)
        {
            c.NextMouthful = w.Time + Mouthful * c.Rng.Range(0.7f, 1.3f);
            Vector3 mouth = c.Rig != null && c.Rig.HitCount > 1 ? new Vector3(c.Rig.Hits[1].X, c.Rig.Hits[1].Y, c.Rig.Hits[1].Z) : c.Position + Vector3.Up * 0.9f;
            Spitting.Dribble(w, c, mouth, head);
        }
        if (c.InfestFor >= InfestSeconds) Rise(w, c, body);
        return true;
    }

    /// <summary>The body gets up a Husk: the same person, on its back where it lay and then up, keeping round there.</summary>
    static void Rise(WildSim w, Creature c, Creature body)
    {
        c.Infests = null;
        c.InfestFor = 0f;
        c.SetState(CState.Wander);
        Species s = w.TurnHost(body.Species, Rng.Mix(body.Species.Seed, (ulong)(w.Frame * 31 + body.Id)));
        Creature made = body.Space != null ? w.SpawnIn(s, body.Space, body.Position) : w.Spawn(s, body.Position);
        made.Yaw = body.Yaw;
        made.Home = body.Position;
        made.ThinkEvery = 1.5f;
        made.Stir = 1f;
        if (body.Space == null)
        {
            made.Downed = 2.4f;
            made.Tumble = new Quaternion(new Vector3(MathF.Cos(made.Yaw), 0f, -MathF.Sin(made.Yaw)), 1.45f);
        }
        body.Removed = true;
        w.RaiseTurned(new SeizeEvent { Husk = c, Victim = body.AsTarget, Time = w.Time, Turned = made });
    }
}
