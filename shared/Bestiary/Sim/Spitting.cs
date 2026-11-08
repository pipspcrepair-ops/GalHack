using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>Something thrown from a throat, in the air.</summary>
public sealed class Gob
{
    public Vector3 Position, Velocity;
    public Creature From;
    public ITarget Target;
    public Color Colour;
    public float Age;
    public bool Landed;
}

/// <summary>
/// The spit attack, for anything with a throat that can do it: an animal's acid, or what comes up
/// a mutated person. It runs through the same wind-up and commit as a bite (the wind-up is the
/// retch), then a gob flies in an arc at where you stood when it committed, so stepping aside
/// dodges it. Blood comes up the colour of their own blood; bile is green-yellow.
/// </summary>
public static class Spitting
{
    /// <summary>How far it reaches, m, and how close it would rather bite.</summary>
    public const float Reach = 11f, Close = 2.6f;
    /// <summary>How fast a gob leaves the mouth, m/s, and what pulls it down.</summary>
    const float Speed = 13f, Gravity = 9f;
    /// <summary>How close to you it has to land, m.</summary>
    public const float HitRadius = 1.05f;

    public static readonly Color BileColour = new(0.62f, 0.68f, 0.14f);
    /// <summary>A Husk's maggots: pale, the colour of grubs.</summary>
    public static readonly Color MaggotColour = new(0.9f, 0.86f, 0.74f);

    public static Color ColourOf(Species s) => s.Spit switch { SpitKind.Blood => s.Blood, SpitKind.Maggots => MaggotColour, _ => BileColour };

    /// <summary>How far a throat throws: maggots come up at short range only.</summary>
    public static float ReachOf(Species s) => s.Spit == SpitKind.Maggots ? 6f : Reach;

    public static bool Can(Creature c) => c.Species.Spit != SpitKind.None && c.Space == null;

    /// <summary>Whether this attack is a spit: it can, and it isn't close enough to bite.</summary>
    public static bool Wants(Creature c) => Can(c) && WildSim.Flat(c.AimPoint - c.Position).Length() > Close;

    /// <summary>Where the mouth is, near enough: at the front of its head.</summary>
    static Vector3 Mouth(Creature c)
    {
        Species s = c.Species;
        float up = s.Figure != null ? s.Figure.Height * 0.86f : s.BodyHeight * 0.8f;
        return c.Position + Vector3.Up * up + c.Forward * Attacks.Jaws(c);
    }

    /// <summary>Lets go: a gob on an arc that comes down on the spot it committed to.</summary>
    public static void Launch(WildSim w, Creature c)
    {
        Vector3 from = Mouth(c), to = c.AimPoint + Vector3.Up * 1.1f;
        Vector3 flat = WildSim.Flat(to - from);
        float d = flat.Length();
        float time = Math.Clamp(d / Speed, 0.12f, 1.2f);
        Vector3 v = flat / time;
        v.Y = (to.Y - from.Y) / time + 0.5f * Gravity * time;
        c.Yaw = MathF.Atan2(-flat.X, -flat.Z);
        var gob = new Gob { Position = from, Velocity = v, From = c, Target = c.AttackTarget, Colour = ColourOf(c.Species) };
        w.Gobs.Add(gob);
        w.RaiseSpat(gob);
    }

    public static void Step(WildSim w, float dt)
    {
        List<Gob> gobs = w.Gobs;
        for (int i = gobs.Count - 1; i >= 0; i--)
        {
            Gob g = gobs[i];
            g.Age += dt;
            g.Velocity += Vector3.Down * Gravity * dt;
            g.Position += g.Velocity * dt;
            ITarget t = g.Target;
            bool hit = t != null && t.Attackable && (t.Position + Vector3.Up * 1f - g.Position).Length() < HitRadius;
            float ground = w.Terrain.Height(g.Position.X, g.Position.Z);
            if (!hit && g.Position.Y > ground && g.Age < 3f) continue;
            g.Landed = true;
            gobs.RemoveAt(i);
            w.RaiseSplash(g, hit);
            if (t != null && !g.From.Removed) Attacks.Deliver(w, g.From, t, hit);
        }
    }
}
