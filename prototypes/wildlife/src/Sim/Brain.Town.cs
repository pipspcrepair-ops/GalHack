using System;
using Godot;

namespace Wildlife;

public static partial class Brain
{
    /// <summary>
    /// Keeps a wild creature out of settlements: whatever it wants, nothing of it may carry it
    /// further in, and inside a town's buffer it is pushed back out, sliding round the edge.
    /// A predator chasing someone stops at the edge of town: inside, they are safe.
    /// </summary>
    static void AvoidTowns(WildSim w, Creature c)
    {
        foreach (Settlement town in w.Settlements)
        {
            Vector3 from = WildSim.Flat(c.Position - town.Center);
            float d = from.Length(), edge = town.Radius + town.Buffer;
            if (d >= edge + 4f) continue;
            Vector3 outward = d > 0.01f ? from / d : c.Forward;
            float inward = -c.Desired.Dot(outward);
            if (inward > 0f) c.Desired += outward * inward;
            if (d < edge) c.Desired += outward * MathF.Max(c.Stats.WalkSpeed, 1f) * Math.Clamp((edge - d) / 4f, 0f, 1.5f);
            // A wander goal inside town is given up for one outside it.
            if (c.HasGoal && town.Contains(c.Goal, town.Buffer)) c.HasGoal = false;
        }
    }

    /// <summary>
    /// A settlement's own animals. They keep to their pen or field (or the town), never fight,
    /// and run from anything wild that comes near or anyone that hurts them, staying inside:
    /// - stock graze and wander their pen, and lie down together at night;
    /// - a working beast stands by its cart, shifting its weight, now and then a step or two;
    /// - a pet follows its owner at heel and lies down when they stop, or lies about near home.
    /// </summary>
    static void Domestic(WildSim w, Creature c, float dt)
    {
        Species s = c.Species;
        float radius = MathF.Max(1.5f, c.PenRadius);
        Vector3 pen = c.PenRadius > 0f ? c.PenCenter : c.Home;

        // Something wild close by (or someone who hurt it): away, but not out of the pen.
        Vector3? threat = null;
        if (c.ProvokedBy != null && w.Time - c.ProvokedAt < 10f) threat = c.ProvokedBy.Position;
        else
            foreach (Creature o in w.Creatures)
            {
                if (o.Removed || o.Dead || o.Species.Domestic != DomesticKind.None || o.Stats.Damage <= 0f) continue;
                if (WildSim.Flat(o.Position - c.Position).Length() < 10f) { threat = o.Position; break; }
            }
        if (threat is { } t)
        {
            c.SetState(CState.Flee);
            Vector3 away = WildSim.Flat(c.Position - t);
            away = away.LengthSquared() > 0.01f ? away.Normalized() : c.Forward;
            Vector3 goal = c.Position + away * 6f;
            Vector3 fromPen = WildSim.Flat(goal - pen);
            if (fromPen.Length() > radius * 0.9f) goal = pen + fromPen.Normalized() * radius * 0.9f;
            Steer(w, c, goal, s.Stats.RunSpeed);
            return;
        }

        // Out of its pen (shoved, or startled out): straight back.
        if (WildSim.Flat(c.Position - pen).Length() > radius)
        {
            c.SetState(CState.ReturnHome);
            Steer(w, c, pen, s.Stats.WalkSpeed);
            return;
        }

        // At night, stock and working beasts lie down where they are kept.
        if (w.Clock.IsNight && s.Domestic != DomesticKind.Pet)
        {
            Settle(c);
            return;
        }

        switch (s.Domestic)
        {
            case DomesticKind.Stock:
                PenWander(w, c, pen, radius, dt);
                break;
            case DomesticKind.Working:
                // Stood by its cart: a step or two now and then, otherwise still.
                if (WildSim.Flat(c.Position - c.Home).Length() > 1.2f) { c.SetState(CState.ReturnHome); Steer(w, c, c.Home, s.Stats.WalkSpeed * 0.6f); }
                else
                {
                    c.SetState(CState.Idle);
                    c.Desired = Vector3.Zero;
                    c.WantGraze = Mathf.PosMod(c.Age * 0.07f + c.Id * 0.3f, 1f) < 0.25f ? 1f : 0f;
                }
                break;
            default:
                Pet(w, c, pen, radius, dt);
                break;
        }
    }

    static void Settle(Creature c)
    {
        c.SetState(CState.Rest);
        c.Desired = Vector3.Zero;
        c.WantCrouch = 1f;
        c.LookAt = null;
    }

    static void PenWander(WildSim w, Creature c, Vector3 pen, float radius, float dt)
    {
        if (c.State == CState.Graze || c.State == CState.Idle)
        {
            c.Desired = Vector3.Zero;
            c.WantGraze = 1f;
            if (c.StateTime > 4f + c.Id % 5) c.SetState(CState.Wander);
            return;
        }
        if (c.State != CState.Wander) c.SetState(CState.Wander);
        if (!c.HasGoal)
        {
            float a = c.Rng.Range(0f, MathF.Tau), r = radius * MathF.Sqrt(c.Rng.Range(0f, 0.8f));
            c.Goal = pen + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            c.HasGoal = true;
        }
        if (WildSim.Flat(c.Goal - c.Position).Length() < 1.2f || c.StateTime > 20f)
        {
            c.SetState(CState.Graze);
            return;
        }
        Steer(w, c, c.Goal, c.Stats.WalkSpeed * 0.6f);
    }

    static void Pet(WildSim w, Creature c, Vector3 home, float radius, float dt)
    {
        ITarget owner = c.Owner;
        if (owner != null && WildSim.Flat(owner.Position - home).Length() < radius + 2f)
        {
            float d = WildSim.Flat(owner.Position - c.Position).Length();
            float heel = 1.4f + (c.Id % 3) * 0.5f;
            if (d > heel + 0.6f)
            {
                c.SetState(CState.Follow);
                // To heel: beside and a little behind its owner.
                Vector3 side = new Vector3(-owner.Forward.Z, 0f, owner.Forward.X) * (c.Id % 2 == 0 ? 1f : -1f);
                Vector3 spot = owner.Position - WildSim.Flat(owner.Forward).Normalized() * 0.6f + side * heel * 0.7f;
                Steer(w, c, spot, d > 5f ? c.Stats.RunSpeed : c.Stats.WalkSpeed);
                return;
            }
            // Its owner has stopped: it sits, then lies down.
            c.Desired = Vector3.Zero;
            c.FacePoint = owner.Position;
            if (c.State != CState.Rest && c.State != CState.Idle) c.SetState(CState.Idle);
            if (c.State == CState.Idle && c.StateTime > 3f) Settle(c);
            c.WantCrouch = c.State == CState.Rest ? 1f : 0.5f;
            return;
        }
        // Nobody to follow: lies about near home, now and then a little wander.
        if (c.State == CState.Rest && c.StateTime < 12f + c.Id % 7)
        {
            Settle(c);
            return;
        }
        PenWander(w, c, home, MathF.Min(radius, 6f), dt);
        if (c.State == CState.Graze && c.StateTime > 2f) Settle(c);
    }
}
