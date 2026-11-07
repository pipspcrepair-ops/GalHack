using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Where the Husks den, their worms gather into masses: fleshy, writhing clumps on the floors and
/// walls, worms reaching out from them at whatever comes near. One is a thing in the place (a
/// position on a surface, the way it faces out, its size) that the sim stirs and that can be
/// destroyed; <see cref="WormMassView"/> draws it from its seed, so no two are alike.
/// </summary>
public sealed class WormMass
{
    public Vector3 Position, Normal = Vector3.Up;
    /// <summary>Across, m.</summary>
    public float Size = 0.8f;
    public ulong Seed;
    public int Room;
    public float Health = -1f;
    public bool Dead;
    /// <summary>How stirred its worms are (0..1), and which way they reach (unit, sim frame).</summary>
    public float Stir;
    public Vector3 Reach;

    public float MaxHealth => 30f + 70f * Size;
    /// <summary>The middle of what can be hit.</summary>
    public Vector3 Middle => Position + Normal * Size * 0.25f;
}

public static class WormMasses
{
    /// <summary>Stirs every mass by what moves near it: the living within a few of its own sizes, nearest first.</summary>
    public static void Step(WildSim w, float dt)
    {
        foreach (WormMass m in w.Masses)
        {
            if (m.Health < 0f) m.Health = m.MaxHealth;
            if (m.Dead) { m.Stir = 0f; continue; }
            float reach = 2f + m.Size * 3f, best = reach;
            Vector3 to = Vector3.Zero;
            foreach (ITarget t in w.Targets)
            {
                if (!t.Attackable) continue;
                Vector3 d = t.Position + Vector3.Up - m.Middle;
                float len = d.Length();
                if (len < best) { best = len; to = d; }
            }
            foreach (Creature c in w.Creatures)
            {
                if (c.Removed || c.Dead || c.Species.Husk) continue;
                Vector3 d = Interpenetration.Middle(c) - m.Middle;
                float len = d.Length();
                if (len < best) { best = len; to = d; }
            }
            float want = best < reach ? 1f - best / reach : 0f;
            m.Stir = Mathf.MoveToward(m.Stir, want, dt * 1.5f);
            if (to != Vector3.Zero) m.Reach = m.Reach.Lerp(to.Normalized(), MathF.Min(1f, dt * 3f));
        }
    }

    /// <summary>The nearest living mass along a ray (sim frame), within reach.</summary>
    public static WormMass Raycast(WildSim w, Vector3 from, Vector3 dir, float reach, out float distance)
    {
        WormMass best = null;
        distance = reach;
        foreach (WormMass m in w.Masses)
        {
            if (m.Dead) continue;
            Vector3 c = m.Middle;
            float r = m.Size * 0.55f;
            float along = (c - from).Dot(dir);
            if (along < 0f || along > distance + r) continue;
            float off = (from + dir * along - c).Length();
            if (off > r) continue;
            float hit = along - MathF.Sqrt(MathF.Max(0f, r * r - off * off));
            if (hit < distance) { distance = MathF.Max(0f, hit); best = m; }
        }
        return best;
    }

    /// <summary>A mass takes a blow; enough of them and it is dead (the worms go limp and it sinks).</summary>
    public static void Damage(WildSim w, WormMass m, float amount)
    {
        if (m.Dead || amount <= 0f) return;
        if (m.Health < 0f) m.Health = m.MaxHealth;
        m.Health -= amount;
        m.Stir = 1f;
        if (m.Health <= 0f)
        {
            m.Dead = true;
            m.Health = 0f;
            w.RaiseMassDied(m);
        }
    }

    /// <summary>
    /// What a mass looks like, from its seed: a mound of fleshy lobes lying on its surface (+Y out
    /// of it), and worms rooted all over it, every one its own length, thickness and way.
    /// </summary>
    public static Piece Build(WormMass m)
    {
        var rng = Rng.For(m.Seed, "worm-mass");
        var p = new Piece("mass") { Shadow = true };
        float S = m.Size;
        Color flesh = new Color(0.55f, 0.34f, 0.32f).Lerp(new Color(0.72f, 0.58f, 0.5f), rng.Float());
        int lobes = rng.Int(5, 9);
        var tops = new List<(Vector3 at, float r)>();
        for (int i = 0; i < lobes; i++)
        {
            float a = rng.Range(0f, MathF.Tau), d = i == 0 ? 0f : rng.Range(0.12f, 0.38f) * S;
            float r = S * rng.Range(0.16f, 0.3f) * (i == 0 ? 1.4f : 1f);
            var at = new Vector3(MathF.Cos(a) * d, r * 0.35f, MathF.Sin(a) * d);
            p.Add(Shape.Sphere, new Vector3(r * 2f, r * 1.1f, r * 2f * rng.Range(0.8f, 1.2f)), at, flesh.Darkened(rng.Range(0f, 0.25f)), 0f, 0f, new Basis(Vector3.Up, a), 10, Mat.Membrane);
            tops.Add((at, r));
        }
        // A wet sheen of darker tissue in the folds.
        for (int i = 0; i < 3; i++)
            p.Add(Shape.Sphere, new Vector3(S * 0.5f, S * 0.08f, S * 0.4f), new Vector3(rng.Range(-0.25f, 0.25f) * S, 0.02f, rng.Range(-0.25f, 0.25f) * S), flesh.Darkened(0.45f), 0f, 0f, new Basis(Vector3.Up, rng.Range(0f, 3f)), 8, Mat.Membrane);
        int worms = rng.Int(24, 40);
        for (int i = 0; i < worms; i++)
        {
            var (at, r) = tops[rng.Int(0, tops.Count - 1)];
            var dir = new Vector3(rng.Range(-1f, 1f), rng.Range(0.2f, 1.2f), rng.Range(-1f, 1f)).Normalized();
            Vector3 root = at + dir * r * 0.8f;
            Color c = new Color(0.8f, 0.68f, 0.6f).Lerp(new Color(0.45f, 0.15f, 0.17f), rng.Range(0f, 1f));
            p.Worm(root, (dir + Vector3.Up * 0.4f).Normalized(), S * rng.Range(0.25f, 0.7f), S * rng.Range(0.018f, 0.032f), c, i, rng, 5, 0.12f, 0.45f);
        }
        return p;
    }
}
