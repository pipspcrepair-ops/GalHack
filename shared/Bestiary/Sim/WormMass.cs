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
    /// <summary>Who it was made of, if anyone: a hand, a foot or part of a face of theirs still shows (<see cref="WormMasses.Build"/>).</summary>
    public Species Host;
    public int Room;
    public float Health = -1f;
    public bool Dead;
    /// <summary>How stirred its worms are (0..1), and which way they reach (unit, sim frame).</summary>
    public float Stir;
    public Vector3 Reach;
    /// <summary>How eager its worms are, 0..1: nothing until something is close, then more the closer it comes; 1 while they have hold of it.</summary>
    public float Eager;
    /// <summary>Whoever its worms have hold of now, and who it has just let go of and until when (this sim's clock).</summary>
    public readonly List<Latch> Latches = new();
    public readonly Dictionary<object, float> Spared = new();

    public float MaxHealth => 30f + 70f * Size;
    /// <summary>The middle of what can be hit.</summary>
    public Vector3 Middle => Position + Normal * Size * 0.25f;
}

/// <summary>A mass's worms holding someone: a player (or anyone the game keeps) or a creature of the sim; where it last held them.</summary>
public sealed class Latch
{
    public ITarget Target;
    public Creature Creature;
    public Vector3 Last;
    public object Who => (object)Creature ?? Target;
}

public static class WormMasses
{
    /// <summary>
    /// Every mass a step. Its worms keep their own slow sway (the shader's) and stir at what moves
    /// near (the living within a few of its own sizes, nearest first). Within a couple of metres
    /// they turn eager, reaching further and faster toward it the closer it comes; within reach
    /// they take hold of legs and body (<see cref="Latch"/>): whoever they hold is slowed hard,
    /// hauled toward the mass and hurt a little at a time (<see cref="HuskRules.MassDamage"/>),
    /// never infected. They let go if the one held gets far enough off against the pull, or the
    /// mass is hurt (<see cref="Damage"/>), and then can't take that one again for a moment. A
    /// creature is moved here; a player is the game's to move (<see cref="Held"/>). Veins do nothing.
    /// </summary>
    public static void Step(WildSim w, float dt)
    {
        HuskRules r = w.Data.Husks;
        foreach (WormMass m in w.Masses)
        {
            if (m.Health < 0f) m.Health = m.MaxHealth;
            if (m.Dead)
            {
                m.Stir = m.Eager = 0f;
                m.Latches.Clear();
                continue;
            }
            float reach = 2f + m.Size * 3f, best = reach, grab = Grab(r, m);
            Vector3 to = Vector3.Zero;
            foreach (ITarget t in w.Targets)
            {
                if (!t.Attackable) continue;
                Vector3 d = t.Position + Vector3.Up - m.Middle;
                float len = d.Length();
                if (len < best) { best = len; to = d; }
                Touch(w, m, t, null, Closest(m, t.Position), grab);
            }
            foreach (Creature c in w.Creatures)
            {
                if (c.Removed || c.Dead || c.Species.Husk) continue;
                Vector3 d = Interpenetration.Middle(c) - m.Middle;
                float len = d.Length();
                if (len < best) { best = len; to = d; }
                if (!c.Species.Mechanical && !(c.Species.Flies && !c.Landed) && c.Species.Rig != RigKind.Swarm)
                    Touch(w, m, c.AsTarget, c, Closest(m, c.Position) - c.Radius * 0.8f, grab);
            }
            // The held, hauled in and hurt; anyone gone or got away, let go.
            for (int i = m.Latches.Count - 1; i >= 0; i--)
            {
                Latch l = m.Latches[i];
                Creature c = l.Creature;
                bool gone = c != null ? c.Removed || c.Dead : !l.Target.Attackable;
                Vector3 at = c?.Position ?? l.Target.Position;
                if (gone || Closest(m, at) - (c?.Radius ?? 0f) * 0.8f > grab + r.MassLetGo)
                {
                    Release(w, m, i);
                    continue;
                }
                if (c == null) continue;
                // It walks as it will, at a fraction of its pace, and is dragged in besides.
                Vector3 moved = c.Position - l.Last;
                Vector3 pull = WildSim.Flat(m.Middle - c.Position);
                float far = pull.Length();
                Vector3 next = l.Last + moved * r.MassSlow + (far > m.Size * 0.5f + c.Radius * 0.6f ? pull / far * r.MassPull * dt : Vector3.Zero);
                // In a place, only into open room: never hauled into a wall.
                if (c.Space == null || c.Space.Density(next + Vector3.Up * MathF.Max(0.3f, c.Radius)) < -c.Radius * 0.5f) c.Position = next;
                l.Last = c.Position;
                w.Damage(c, r.MassDamage / 100f * c.Stats.MaxHealth * dt, null);
            }
            float want = best < reach ? 1f - best / reach : 0f;
            m.Stir = Mathf.MoveToward(m.Stir, want, dt * 1.5f);
            // Eager only close: nothing past twice its reach, all of it at its reach or holding.
            float near = MathF.Min(best, reach);
            float eager = m.Latches.Count > 0 ? 1f : Math.Clamp(1f - (near - grab) / (grab * 1.6f + 0.8f), 0f, 1f);
            m.Eager = Mathf.MoveToward(m.Eager, eager * eager, dt * (eager > m.Eager ? 2.5f : 1f));
            if (m.Latches.Count > 0) to = (m.Latches[0].Creature?.Position ?? m.Latches[0].Target.Position) + Vector3.Up * 0.6f - m.Middle;
            if (to != Vector3.Zero) m.Reach = m.Reach.Lerp(to.Normalized(), MathF.Min(1f, dt * (3f + 5f * m.Eager)));
        }
    }

    /// <summary>How far its worms reach to take hold, m (from its middle to the nearest of someone's legs and body).</summary>
    public static float Grab(HuskRules r, WormMass m) => r.MassGrab + r.MassGrabPerSize * m.Size;

    /// <summary>How near someone standing at a point (their feet) comes to a mass: the nearest of their legs and body, from its middle.</summary>
    static float Closest(WormMass m, Vector3 feet)
    {
        float best = float.MaxValue;
        for (float up = 0.15f; up <= 1.35f; up += 0.4f) best = MathF.Min(best, (feet + Vector3.Up * up - m.Middle).Length());
        return best;
    }

    /// <summary>Someone within reach and not just let go of: the worms take hold.</summary>
    static void Touch(WildSim w, WormMass m, ITarget t, Creature c, float near, float grab)
    {
        if (near > grab) return;
        object who = (object)c ?? t;
        foreach (Latch l in m.Latches) if (l.Who == who) return;
        if (m.Spared.TryGetValue(who, out float until) && w.Time < until) return;
        m.Latches.Add(new Latch { Target = t, Creature = c, Last = c?.Position ?? t.Position });
        m.Stir = 1f;
    }

    static void Release(WildSim w, WormMass m, int i)
    {
        m.Spared[m.Latches[i].Who] = w.Time + w.Data.Husks.MassRegrab;
        m.Latches.RemoveAt(i);
    }

    /// <summary>Lets go of everything it holds (it was hurt, or someone tore free).</summary>
    public static void LetGo(WildSim w, WormMass m)
    {
        for (int i = m.Latches.Count - 1; i >= 0; i--) Release(w, m, i);
    }

    /// <summary>
    /// Whether any mass has hold of a target the game moves (a player), and if so which way they
    /// are hauled (unit, flat, sim frame, toward the nearest mass holding them). The game slows
    /// them by <see cref="HuskRules.MassSlow"/>, hauls them at <see cref="HuskRules.MassPull"/> and
    /// hurts them <see cref="HuskRules.MassDamage"/> a second.
    /// </summary>
    public static bool Held(WildSim w, ITarget t, out Vector3 toward, out WormMass by)
    {
        toward = Vector3.Zero;
        by = null;
        float best = float.MaxValue;
        foreach (WormMass m in w.Masses)
            foreach (Latch l in m.Latches)
            {
                if (l.Target != t || l.Creature != null) continue;
                Vector3 d = WildSim.Flat(m.Middle - t.Position);
                if (d.Length() < best) { best = d.Length(); by = m; toward = d.Length() > m.Size * 0.6f ? d.Normalized() : Vector3.Zero; }
            }
        return by != null;
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
        // Hurt, its worms let go of whoever they held (a shot, a cut).
        LetGo(w, m);
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
        Color flesh = new Color(0.66f, 0.42f, 0.4f).Lerp(new Color(0.8f, 0.64f, 0.56f), rng.Float());
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
        if (m.Host?.Figure != null) Remnant(p, m, tops, rng);
        return p;
    }

    /// <summary>What kind of remnant a mass shows, for a seed: 0 a hand, 1 a foot, 2 part of a face (tests).</summary>
    public static int RemnantKind(WormMass m) => Rng.For(m.Seed, "remnant").Int(0, 2);

    /// <summary>
    /// What is left of the one it was made of, sticking out of the lobes: their own hand (a
    /// forearm and the fingers), a foot, or part of a face, as their body builds them
    /// (<see cref="HumanBody.Build"/>), sunk in so only that much shows. A big mass may hold two.
    /// </summary>
    static void Remnant(Piece p, WormMass m, List<(Vector3 at, float r)> tops, Rng rng)
    {
        RigLayout body = HumanBody.Build(m.Host);
        Figure f = m.Host.Figure;
        int n = tops.Count > 6 && rng.Chance(0.4f) ? 2 : 1;
        int first = RemnantKind(m);
        for (int k = 0; k < n; k++)
        {
            var (at, r) = tops[rng.Int(0, tops.Count - 1)];
            // Out of the lobe, leaning off upright, turned any way.
            Vector3 out_ = new Vector3(rng.Range(-0.8f, 0.8f), 1f, rng.Range(-0.8f, 0.8f)).Normalized();
            Vector3 surface = at + out_ * r * 0.55f;
            int kind = (first + k) % 3;
            string name = kind switch { 0 => $"arm{rng.Int(0, 1)}l", 1 => $"leg{rng.Int(0, 1)}f", _ => "head" };
            Piece part = body.Pieces.Find(q => q.Name == name);
            if (part == null) continue;
            Transform3D t;
            if (kind == 0)
            {
                // A forearm reaching out of it, the hand at the end: the elbow still in there.
                float fore = f.ForearmOf(name[3] - '0');
                Basis b = Geo.AlongY(out_).Rotated(out_, rng.Range(0f, MathF.Tau));
                t = new Transform3D(b, surface - out_ * fore * 0.25f);
            }
            else if (kind == 1)
            {
                // A shin kicked up out of it, the foot on the end: the knee still in there.
                Piece shin = body.Pieces.Find(q => q.Name == $"leg{name[3]}l");
                // (The shin runs knee to ankle along its +Y; the foot stands up its own +Y toward the knee.)
                float turn = rng.Range(0f, MathF.Tau);
                Basis b = Geo.AlongY(-out_).Rotated(out_, turn);
                Vector3 ankle = surface + out_ * f.LowerLeg * 0.6f;
                if (shin != null)
                {
                    var along = new Transform3D(Geo.AlongY(out_).Rotated(out_, turn), ankle - out_ * f.LowerLeg);
                    foreach (Prim q in shin.Prims)
                    {
                        Prim c = q;
                        c.Local = along * q.Local;
                        p.Prims.Add(c);
                    }
                }
                t = new Transform3D(b, ankle);
            }
            else
            {
                // A face, turned up out of it, the back of the head under the worms, tipped off true.
                float R = f.HeadRadius;
                Vector3 centre = new(0f, f.Neck + R * 0.92f, 0f);
                Basis b = Basis.LookingAt(out_, MathF.Abs(out_.Y) > 0.9f ? Vector3.Forward : Vector3.Up).Rotated(out_, rng.Range(-0.9f, 0.9f));
                t = new Transform3D(b, surface - out_ * R * 0.15f - b * centre);
            }
            foreach (Prim q in part.Prims)
            {
                Prim c = q;
                c.Local = t * q.Local;
                p.Prims.Add(c);
            }
        }
    }
}
