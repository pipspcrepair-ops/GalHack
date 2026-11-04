using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Weather;
using Weather.Forces;
using Weather.Model;
using GVector3 = Godot.Vector3;
using N = System.Numerics;

namespace PhysicsSandbox.Bridges;

/// <summary>
/// The bridge between the weather prototype and the shared physics module. Each tick it steps
/// the weather model around the player, copies its tornadoes into weather's force module (via
/// weather's own Glue), and lets the force module push every tracked body, its pushes landing
/// on the one force path as <see cref="ForceKind.Weather"/>.
///
/// The tornado fling's health floor stays the weather code's: <see cref="FlingFloor"/> asks
/// weather's force module, and the player's health sink passes it on.
///
/// Riders on an outer hull feel the weather's wind against their velocity relative to the hull,
/// not the ship's airspeed: their boots hold them, so a ship can fly with someone on its roof,
/// but a tornado still lifts them off a parked one. Riders inside a cabin feel no weather.
/// </summary>
public sealed class WeatherBridge : IForceProvider
{
    public WeatherSim Sim { get; }
    public ForceModule Forces { get; } = new();

    /// <summary>Where hazards happen: around the player.</summary>
    public System.Func<N.Vector3> Observer = static () => N.Vector3.Zero;

    /// <summary>Weather off: no wind, no tornadoes (the force module still steps, so fling floors keep working).</summary>
    public bool Calm;

    readonly Dictionary<Body, Pushed> _tracked = new();
    PhysicsWorld _world;

    public WeatherBridge(WeatherSim sim)
    {
        Sim = sim;
        Forces.Wind = p => Calm ? GVector3.Zero : Sim.WindAt(p);
    }

    public void Track(Body body)
    {
        if (_tracked.ContainsKey(body)) return;
        var pushed = new Pushed(this, body);
        _tracked[body] = pushed;
        Forces.Register(pushed);
    }

    public void Untrack(Body body)
    {
        if (_tracked.Remove(body, out Pushed p)) Forces.Unregister(p);
    }

    public void ApplyForces(PhysicsWorld world, float dt)
    {
        _world = world;
        N.Vector3 o = Observer();
        Sim.Step(dt, new GVector3(o.X, o.Y, o.Z));
        Glue.Sync(Sim, Forces);
        if (Calm) Forces.Vortices.Clear();
        Forces.Step(dt);
    }

    /// <summary>The share of max health a fling's landing must leave this body, or 0. Weather's own rule.</summary>
    public float FlingFloor(Body body) => _tracked.TryGetValue(body, out Pushed p) ? Forces.FlingFloor(p) : 0f;

    /// <summary>The body has touched down: a fling's protection ends.</summary>
    public void Landed(Body body)
    {
        if (_tracked.TryGetValue(body, out Pushed p)) Forces.Landed(p);
    }

    public VortexPhase PhaseOf(Body body) => _tracked.TryGetValue(body, out Pushed p) ? Forces.PhaseOf(p) : VortexPhase.Free;

    /// <summary>A shared body, as weather's force module sees it.</summary>
    sealed class Pushed : IForceBody
    {
        readonly WeatherBridge _bridge;
        readonly Body _body;

        public Pushed(WeatherBridge bridge, Body body)
        {
            _bridge = bridge;
            _body = body;
        }

        public float Mass => _body.Mass;
        public float DragArea => _body.Frame != null ? 0f : _body.DragArea;

        public GVector3 Position
        {
            get
            {
                // Players register at their feet; the force module wants the middle of the body.
                N.Vector3 p = _body.WorldPosition + N.Vector3.UnitY * (_body.Kind == BodyKind.Player ? 0.9f : 0f);
                return new GVector3(p.X, p.Y, p.Z);
            }
        }

        public GVector3 Velocity => (_body.Frame != null ? _body.WorldVelocity : _body.Velocity).ToGodot();

        public void ApplyForce(GVector3 force)
        {
            if (_body.Frame == null) _bridge._world?.AddForce(_body, force.ToNumerics(), ForceKind.Weather);
        }

        public void ApplyImpulse(GVector3 impulse)
        {
            if (_body.Frame == null) _bridge._world?.AddImpulse(_body, impulse.ToNumerics(), ForceKind.Weather);
        }
    }
}

/// <summary>
/// A player's health as the physics module's damage sink: every impact hits it in full
/// (weather's <see cref="Health.Hit"/>), with the floor weather says applies: the tornado
/// fling's share, or none. Nothing else ever lowers damage.
/// </summary>
public sealed class HealthSink : IDamageSink
{
    public readonly Health Health;
    readonly WeatherBridge _weather;

    public HealthSink(Health health, WeatherBridge weather)
    {
        Health = health;
        _weather = weather;
    }

    public float Take(Body body, float raw)
    {
        float floor = _weather?.FlingFloor(body) ?? 0f;
        return Health.Hit(raw, floor * Health.Max);
    }
}
