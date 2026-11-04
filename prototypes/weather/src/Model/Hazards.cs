using System;
using Godot;

namespace Weather.Model;

/// <summary>
/// Something lightning can strike: a spire, a tree, a ship, a player. The world keeps
/// <see cref="Position"/> (the top of the thing) up to date; the model only reads it.
/// </summary>
public sealed class StrikePoint
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public Vector3 Position { get; set; }
}

public enum HazardKind { Lightning, Debris }

/// <summary>
/// A strike that is coming: lightning charging up, or burning debris falling. Every hazard
/// is visible for its whole build-up before it hits, so a danger is never silent.
/// </summary>
public sealed class Hazard
{
    public int Id { get; init; }
    public HazardKind Kind { get; init; }

    /// <summary>Where the strike was aimed. Lightning then picks the highest point within <see cref="Radius"/>.</summary>
    public Vector3 Center { get; init; }
    public float Radius { get; init; }
    public float BuildUp { get; init; }
    public float Age { get; internal set; }

    /// <summary>Where it will hit, re-picked every tick while lightning charges.</summary>
    public Vector3 Target { get; internal set; }

    /// <summary>What it will hit, or null for bare ground.</summary>
    public StrikePoint TargetPoint { get; internal set; }

    public float Progress => Math.Clamp(Age / BuildUp, 0f, 1f);
    public float Remaining => MathF.Max(0f, BuildUp - Age);
}

public enum WeatherEventKind { LightningStrike, DebrisImpact, TornadoForming, TornadoTouchdown, TornadoGone }

/// <summary>Something that happened during the last tick, for the world to show, sound and apply.</summary>
public readonly record struct WeatherEvent(
    WeatherEventKind Kind, int Id, Vector3 Position, StrikePoint Target, float Damage, float DamageRadius);

/// <summary>
/// A tornado: a vortex that forms under the cloud base, touches down, wanders across the
/// plain (steering toward whoever it is homing on) and dissipates. Its lift profile comes
/// from <see cref="TornadoRules"/>; the force module turns it into pushes and lifts.
/// </summary>
public sealed class Tornado
{
    public int Id { get; }
    public TornadoRules Rules { get; }

    /// <summary>Base of the funnel, on the ground.</summary>
    public Vector3 Position { get; private set; }

    /// <summary>Direction of travel, radians in the XZ plane (0 is +X, π/2 is +Z).</summary>
    public float Heading { get; private set; }

    public float Age { get; private set; }

    /// <summary>Steers toward the observer until it has passed them, then keeps going.</summary>
    public bool Homing { get; set; }

    float _lifetime;
    readonly float _wobblePhase;

    public Tornado(int id, TornadoRules rules, Vector3 position, float heading, bool homing, float wobblePhase)
    {
        Id = id;
        Rules = rules;
        Position = position;
        Heading = heading;
        Homing = homing;
        _lifetime = rules.Lifetime;
        _wobblePhase = wobblePhase;
    }

    public bool Touchdown => Age >= Rules.FormSeconds;

    /// <summary>0..1 as the funnel lowers from the cloud base: the visible build-up before touchdown.</summary>
    public float Formation => Math.Clamp(Age / Rules.FormSeconds, 0f, 1f);

    /// <summary>How hard it pulls, 0..1: nothing until touchdown, fading as it dissipates.</summary>
    public float Strength => Touchdown ? 1f - SmoothStep(_lifetime - Rules.DissipateSeconds, _lifetime, Age) : 0f;

    public bool Dissipating => Age > _lifetime - Rules.DissipateSeconds;
    public bool Done => Age >= _lifetime;

    public Vector3 Velocity
    {
        get
        {
            if (!Touchdown) return Vector3.Zero;
            float h = MoveHeading;
            return new Vector3(MathF.Cos(h), 0f, MathF.Sin(h)) * Rules.Speed;
        }
    }

    /// <summary>A slow weave either side of its heading, so the path never looks ruled.</summary>
    float MoveHeading => Heading + 0.35f * MathF.Sin(Age * 0.23f + _wobblePhase);

    /// <summary>
    /// How loud its roar is for a listener, 0..1. It starts while the funnel is still forming
    /// and carries a long way (audible from about 700 m), so a tornado is heard before it can
    /// be felt, even in a whiteout or a dust storm where it can't be seen.
    /// </summary>
    public float Roar(Vector3 listener)
    {
        float distance = new Vector2(listener.X - Position.X, listener.Z - Position.Z).Length();
        float source = Formation * (0.35f + 0.65f * (Touchdown ? Strength : 0f));
        return source / (1f + distance * distance / (250f * 250f));
    }

    /// <summary>Tests and screenshots: skip the forming build-up and touch down now.</summary>
    public void SkipFormation() => Age = MathF.Max(Age, Rules.FormSeconds);

    /// <summary>Start dissipating now if it isn't already, e.g. when the storm that fed it has gone.</summary>
    public void Dissipate()
    {
        if (!Dissipating) _lifetime = MathF.Max(Age + Rules.DissipateSeconds, Rules.FormSeconds + Rules.DissipateSeconds);
    }

    public void Step(float dt, Vector3 observer, Func<float, float, float> groundHeight)
    {
        Age += dt;
        if (Touchdown)
        {
            var toObserver = new Vector2(observer.X - Position.X, observer.Z - Position.Z);
            if (Homing && toObserver.Length() < Rules.CaptureRadius * 1.5f) Homing = false; // passed over: carry on
            if (Homing)
            {
                float want = MathF.Atan2(toObserver.Y, toObserver.X);
                float turn = Mathf.Wrap(want - Heading, -MathF.PI, MathF.PI);
                Heading += Math.Clamp(turn, -Rules.TurnRate * dt, Rules.TurnRate * dt);
            }
            Position += Velocity * dt;
        }
        Position = new Vector3(Position.X, groundHeight(Position.X, Position.Z), Position.Z);
    }

    static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
