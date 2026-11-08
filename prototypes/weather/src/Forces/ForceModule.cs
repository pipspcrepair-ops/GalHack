using System;
using System.Collections.Generic;
using Godot;

namespace Weather.Forces;

/// <summary>
/// Anything the wind can push: a player, a hover vessel, a rover. Implemented by the body
/// itself, so the force module never needs to know whether it is a RigidBody3D, a
/// CharacterBody3D or a test dummy.
/// </summary>
public interface IForceBody
{
    float Mass { get; }

    /// <summary>Drag coefficient × frontal area, m². Crouching shrinks it.</summary>
    float DragArea { get; }

    Vector3 Position { get; }
    Vector3 Velocity { get; }

    /// <summary>A force for this physics step only, N. The body still applies its own gravity.</summary>
    void ApplyForce(Vector3 force);

    /// <summary>An instant change of momentum, N·s.</summary>
    void ApplyImpulse(Vector3 impulse);
}

/// <summary>
/// A tornado as a force volume: an upright cylinder of rotating, rising air that moves.
/// The weather model owns the tornado; whoever wires the two together copies it in here.
/// </summary>
public struct Vortex
{
    public int Id;
    public Vector3 Base;
    public Vector3 Velocity;

    /// <summary>0..1: forming and dissipating tornadoes pull less.</summary>
    public float Strength;

    /// <summary>Inside this radius a body is caught: lifted and spun.</summary>
    public float CaptureRadius;

    /// <summary>Out to this radius the inflow and swirl push bodies around.</summary>
    public float InfluenceRadius;

    public float OrbitRadius, OrbitSpeed, InflowSpeed, LiftSpeed;

    /// <summary>Lift fades out approaching this height, and a body is let go once it gets there.</summary>
    public float ReleaseHeight;
    public float MaxCarrySeconds;

    /// <summary>The outward kick on release, m/s.</summary>
    public float FlingSpeed;

    /// <summary>After release a body can't be caught again for this long, so it is thrown clear.</summary>
    public float ReleaseImmunity;

    /// <summary>Bodies heavier than this feel proportionally less of the vortex.</summary>
    public float LiftMassLimit;

    /// <summary>How quickly a caught body matches the vortex's flow, 1/s.</summary>
    public float Response;
}

public enum VortexPhase { Free, Caught, Released }

/// <summary>
/// The wind and tornado forces, in one small module with a simple interface: register bodies,
/// set <see cref="Wind"/> and <see cref="Vortices"/>, call <see cref="Step"/> every physics tick.
///
/// Wind is plain aerodynamic drag, ½ρ·CdA·|v|·v on the wind relative to the body, so the same
/// gust that sets a 280 kg hover vessel drifting barely nudges a 6.5 t rover: acceleration is
/// force over mass. A tornado is a target flow (round, in and up) that caught bodies are steered
/// toward; it carries them up and around, then lets them go with a fling. There is no fluid
/// simulation anywhere, just these forces.
/// </summary>
public sealed class ForceModule
{
    public const float AirDensity = 1.2f;
    public const float Gravity = 9.8f;

    /// <summary>The wind at a point, m/s.</summary>
    public Func<Vector3, Vector3> Wind { get; set; } = static _ => Vector3.Zero;

    /// <summary>The tornadoes this tick. Replace the contents every tick.</summary>
    public List<Vortex> Vortices { get; } = new();

    /// <summary>Raised when a body is caught by a vortex or let go, so the HUD can warn.</summary>
    public event Action<IForceBody, VortexPhase> PhaseChanged;

    sealed class Tracked
    {
        public IForceBody Body;
        public VortexPhase Phase;
        public int VortexId;
        public float Carry, Immunity, Orbit;
        public Vector2 LastRadial;
    }

    readonly List<Tracked> _bodies = new();

    public void Register(IForceBody body)
    {
        if (Find(body) == null) _bodies.Add(new Tracked { Body = body });
    }

    public void Unregister(IForceBody body) => _bodies.RemoveAll(t => t.Body == body);

    public int Count => _bodies.Count;

    public VortexPhase PhaseOf(IForceBody body) => Find(body)?.Phase ?? VortexPhase.Free;

    /// <summary>Radians a caught body has travelled around the vortex since it was caught.</summary>
    public float OrbitOf(IForceBody body) => Find(body)?.Orbit ?? 0f;

    /// <summary>Applies this tick's wind and vortex forces to every registered body.</summary>
    public void Step(float dt)
    {
        foreach (Tracked t in _bodies) StepBody(t, dt);
    }

    /// <summary>Drag from a wind velocity on a body: ½ρ·CdA·|v_rel|·v_rel.</summary>
    public static Vector3 Drag(IForceBody body, Vector3 wind)
    {
        Vector3 relative = wind - body.Velocity;
        return 0.5f * AirDensity * body.DragArea * relative.Length() * relative;
    }

    /// <summary>The air moving at a point: the wind plus every tornado's inflow and swirl. What a body outside a funnel feels.</summary>
    public Vector3 AirAt(Vector3 position)
    {
        Vector3 air = Wind(position);
        foreach (Vortex v in Vortices)
        {
            if (v.Strength <= 0.01f) continue;
            Vector3 offset = position - v.Base;
            var flat = new Vector2(offset.X, offset.Z);
            float r = flat.Length();
            Vector2 radial = r > 0.01f ? flat / r : Vector2.Right;
            air += Inflow(v, r, offset.Y, new Vector3(radial.X, 0f, radial.Y), new Vector3(-radial.Y, 0f, radial.X));
        }
        return air;
    }

    /// <summary>Outside the core, out to the influence radius: air spiralling in toward the funnel.</summary>
    static Vector3 Inflow(in Vortex v, float r, float height, Vector3 outward, Vector3 around)
    {
        if (r >= v.InfluenceRadius || height >= v.ReleaseHeight * 2f) return Vector3.Zero;
        float near = 1f - SmoothStep(v.CaptureRadius, v.InfluenceRadius, r);
        float swirl = v.OrbitSpeed * MathF.Min(1f, v.CaptureRadius / MathF.Max(r, 0.1f));
        return (around * swirl - outward * v.InflowSpeed) * near * v.Strength;
    }

    /// <summary>Lighter bodies get the vortex's full pull, heavier ones a share inversely proportional to mass.</summary>
    public static float MassShare(float mass, float limit) => MathF.Min(1f, limit / MathF.Max(mass, 1f));

    void StepBody(Tracked t, float dt)
    {
        IForceBody body = t.Body;
        Vector3 wind = Wind(body.Position);
        Vector3 force = Vector3.Zero;
        bool caught = false;

        if (t.Phase == VortexPhase.Released)
        {
            t.Immunity -= dt;
            if (t.Immunity <= 0f) SetPhase(t, VortexPhase.Free);
        }

        foreach (Vortex v in Vortices)
        {
            if (v.Strength <= 0.01f) continue;
            Vector3 offset = body.Position - v.Base;
            var flat = new Vector2(offset.X, offset.Z);
            float r = flat.Length();
            float height = offset.Y;
            Vector2 radial = r > 0.01f ? flat / r : Vector2.Right;
            var outward = new Vector3(radial.X, 0f, radial.Y);
            var around = new Vector3(-radial.Y, 0f, radial.X); // counter-clockwise seen from above

            if (t.Phase == VortexPhase.Caught && t.VortexId == v.Id)
            {
                caught = true;
                force += Carry(t, v, body, r, height, outward, around, radial, dt);
            }
            else if (t.Phase == VortexPhase.Free && r < v.CaptureRadius * v.Strength && height < v.ReleaseHeight * 0.5f)
            {
                t.VortexId = v.Id;
                t.Carry = 0f;
                t.Orbit = 0f;
                t.LastRadial = radial;
                SetPhase(t, VortexPhase.Caught);
                caught = true;
            }
            else
            {
                wind += Inflow(v, r, height, outward, around); // you feel the pull coming before you're caught
            }
        }

        if (!caught) force += Drag(body, wind);
        body.ApplyForce(force);
    }

    /// <summary>Inside a vortex: steered toward a flow that orbits, holds you near the core and lifts, until let go.</summary>
    Vector3 Carry(Tracked t, Vortex v, IForceBody body, float r, float height,
        Vector3 outward, Vector3 around, Vector2 radial, float dt)
    {
        t.Carry += dt;
        t.Orbit += MathF.Abs(t.LastRadial.AngleTo(radial));
        t.LastRadial = radial;

        // Its grip fades past the core (wider the stronger it is): a body pushing hard enough outward
        // there (a jetpack) can break free of a weak one; a strong core holds it.
        float holdRadius = HoldRadius(v);
        float share = MassShare(body.Mass, v.LiftMassLimit) * v.Strength * (1f - SmoothStep(holdRadius, holdRadius * 1.6f, r));
        if (r > holdRadius * 1.6f)
        {
            t.Immunity = v.ReleaseImmunity;
            SetPhase(t, VortexPhase.Released); // broke free: no fling
            return Vector3.Zero;
        }
        bool letGo = height >= v.ReleaseHeight * 0.95f || t.Carry >= v.MaxCarrySeconds
            || r > v.InfluenceRadius || v.Strength < 0.3f;
        if (letGo)
        {
            Vector3 fling = (outward * v.FlingSpeed + around * v.FlingSpeed * 0.6f + Vector3.Up * 2f) * share;
            body.ApplyImpulse(fling * body.Mass);
            t.Immunity = v.ReleaseImmunity;
            SetPhase(t, VortexPhase.Released);
            return Vector3.Zero;
        }

        float lift = v.LiftSpeed * (1f - SmoothStep(v.ReleaseHeight * 0.6f, v.ReleaseHeight, height));
        Vector3 flow = v.Velocity + around * v.OrbitSpeed + outward * ((v.OrbitRadius - r) * 1.5f) + Vector3.Up * lift;
        Vector3 accel = v.Response * (flow - body.Velocity) + Vector3.Up * Gravity; // the updraft carries your weight
        return accel * share * body.Mass;
    }

    /// <summary>Out to here a caught body is held at full grip, metres; it slips free by 1.6 times as far.</summary>
    public static float HoldRadius(in Vortex v) => v.OrbitRadius + v.CaptureRadius * v.Strength;

    void SetPhase(Tracked t, VortexPhase phase)
    {
        if (t.Phase == phase) return;
        t.Phase = phase;
        PhaseChanged?.Invoke(t.Body, phase);
    }

    Tracked Find(IForceBody body)
    {
        foreach (Tracked t in _bodies) if (t.Body == body) return t;
        return null;
    }

    static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
