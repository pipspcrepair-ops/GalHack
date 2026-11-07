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
/// Riders on an outer hull live in their ship's cabin world, and the weather reaches them there:
/// the wind acts against their velocity relative to the hull, not the ship's airspeed (they ride
/// in its frame, so a ship can fly with someone on its roof), and its push is turned into the
/// ship's frame before it joins the one force path. A tornado still lifts them off a parked ship.
/// Above the atmosphere nobody feels any weather. Who is sheltered from it (riders inside an
/// interior, bases, vessels parked on a base or a capital ship) is the shared module's one rule,
/// <see cref="PhysicsWorld.IsSheltered"/>; this bridge asks it, and the world enforces it.
/// </summary>
public sealed class WeatherBridge : IForceProvider
{
    public WeatherSim Sim { get; }
    public ForceModule Forces { get; } = new();

    /// <summary>Where hazards happen: around the player.</summary>
    public System.Func<N.Vector3> Observer = static () => N.Vector3.Zero;

    /// <summary>Weather off: no wind, no tornadoes (the force module still steps, so fling floors keep working).</summary>
    public bool Calm;

    /// <summary>The game's own shelter (a hole deep enough, a place's roof): no wind or tornado reaches such a body.</summary>
    public System.Func<Body, bool> Shelter;

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

        /// <summary>
        /// No air reaches a sheltered body (the shared module's one rule: a base, a vessel parked on
        /// a base or a capital ship, a rider inside an interior), and there is none above the atmosphere.
        /// </summary>
        public float DragArea => Sheltered || InSpace ? 0f : _body.DragArea;

        bool Sheltered => (_bridge._world?.IsSheltered(_body) ?? false) || InShelter;
        bool InShelter => _bridge.Shelter?.Invoke(_body) ?? false;
        bool InSpace => _bridge._world != null && !_bridge._world.Zone.InAtmosphere(_body.WorldPosition);

        public GVector3 Position
        {
            get
            {
                // Players register at their feet; the force module wants the middle of the body.
                N.Vector3 up = _body.Frame == null ? _body.Up : MathX.Rotate(_body.Frame.Rotation, _body.Up);
                N.Vector3 p = _body.WorldPosition + up * (_body.Kind == BodyKind.Player ? 0.9f : 0f);
                return new GVector3(p.X, p.Y, p.Z);
            }
        }

        /// <summary>
        /// The velocity the air acts against. Riders on a hull: relative to the hull. Flown
        /// vessels: none, unless a tornado has hold of them. Their handling model's top speed
        /// already is their air resistance, so still air must not slow them (the four numbers stay
        /// true); they feel the wind's own push, ½ρ·CdA·|w|·w, and a tornado's full grip.
        /// </summary>
        public GVector3 Velocity
        {
            get
            {
                if (_body.Frame != null) return MathX.Rotate(_body.Frame.Rotation, _body.Velocity).ToGodot();
                if (_body.Kind == BodyKind.Vessel && _body.Motion == GalHack.Physics.Motion.Kinematic
                    && _bridge.Forces.PhaseOf(this) != VortexPhase.Caught)
                    return GVector3.Zero;
                return _body.Velocity.ToGodot();
            }
        }

        // The world itself drops weather on sheltered bodies, tornado pulls and flings included.
        public void ApplyForce(GVector3 force)
        {
            if (!InShelter) _bridge._world?.AddForce(_body, IntoFrame(force), ForceKind.Weather);
        }

        public void ApplyImpulse(GVector3 impulse)
        {
            if (!InShelter) _bridge._world?.AddImpulse(_body, IntoFrame(impulse), ForceKind.Weather);
        }

        /// <summary>A world push in the axes the body integrates in (its ship's, out on a hull).</summary>
        N.Vector3 IntoFrame(GVector3 v) => _body.Frame == null ? v.ToNumerics() : MathX.Unrotate(_body.Frame.Rotation, v.ToNumerics());
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
