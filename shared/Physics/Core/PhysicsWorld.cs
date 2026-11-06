using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// The shared physics module's heart: every body registers here once, every force goes
/// through <see cref="AddForce"/>, every contact through the same rules, and the rest of the
/// game hears about physics only through the events below.
///
/// One tick, in a fixed order (the engine host calls these in turn):
/// <list type="number">
/// <item><see cref="BeginTick"/>; free bodies sync from the engine and report their impacts.</item>
/// <item><see cref="ApplyForces"/>: each body's own drive (thrust), then gravity from its zone or
///   ship core, then outside forces (weather), then reactors (legs gripping against the wind).</item>
/// <item><see cref="Integrate"/>: kinematic bodies get velocity += ΣF/m·dt + ΣJ/m.</item>
/// <item>The host moves kinematic bodies with collisions (hulls, then creatures, then
///   characters), reporting contacts to <see cref="ResolveContacts"/>.</item>
/// <item><see cref="EndTick"/>: controllers react, craters are cut, stuck bodies are rescued.</item>
/// <item>The host hands free bodies their summed force and impulse; the engine integrates them
///   in its own step. <see cref="FinishTick"/> clears the sums (forces added between ticks land in the next).</item>
/// </list>
/// Bodies always go in registration order, so the same inputs give the same trajectories.
/// </summary>
public sealed class PhysicsWorld
{
    public PhysicsRules Rules { get; }
    public ZoneGravity Zone { get; } = new();
    public IWorldQuery Query { get; set; } = NoQuery.Instance;

    /// <summary>The terrain's edit API, for craters. Null: impacts make no holes.</summary>
    public ITerrainEditor Terrain { get; set; }

    /// <summary>Where the water is (seas, rivers, lakes): a volume bodies go into. Null: a dry zone.</summary>
    public IWaterField Water { get; set; }

    public List<IForceProvider> ForceProviders { get; } = new();
    public List<IStuckField> StuckFields { get; } = new();
    public StuckGuard Stuck { get; }

    public IReadOnlyList<Body> Bodies => _bodies;
    public long Tick { get; private set; }

    /// <summary>Something collided (ground, another body). Damage already applied.</summary>
    public event Action<ImpactInfo> Impact;

    /// <summary>A vessel settled on the ground and stopped simulating.</summary>
    public event Action<Body> Landed;

    /// <summary>A landed vessel woke up because its pilot applied thrust.</summary>
    public event Action<Body> LiftedOff;

    /// <summary>A ground impact cut a real crater into the terrain.</summary>
    public event Action<CraterInfo> Cratered;

    /// <summary>A stuck body was moved to open space.</summary>
    public event Action<RescueInfo> Rescued;

    /// <summary>A section broke off a structure and became a wreck body.</summary>
    public event Action<SeverInfo> Severed;

    /// <summary>A body changed how it is moved (a vessel out of hull points became a free wreck).</summary>
    public event Action<Body> MotionChanged;

    /// <summary>Terrain or a structure changed here: sleeping bodies nearby must wake (engines don't wake them by themselves).</summary>
    public event Action<Region> RegionChanged;

    /// <summary>A vessel parked on a host (landed on it, resting on it, docked to it), and when it leaves again.</summary>
    public event Action<Body, Body> Parked, Unparked;

    public event Action<Body> Registered, Unregistered;

    readonly List<Body> _bodies = new();
    readonly List<(Body cause, Vector3 center, float radius)> _craters = new();
    readonly HashSet<(int, int)> _pairs = new();
    int _nextId = 1;

    public PhysicsWorld(PhysicsRules rules)
    {
        Rules = rules;
        Stuck = new StuckGuard(this);
    }

    // ---- Registration -----------------------------------------------------------------

    /// <summary>Registers a body once, with its mass and drag area. From now on it is simulated in id order.</summary>
    public Body Register(BodySpec spec)
    {
        if (!(spec.Mass > 0f)) throw new ArgumentException($"{spec.Label}: a body needs a positive mass");
        var body = new Body
        {
            Id = _nextId++,
            Kind = spec.Kind,
            Motion = spec.Motion,
            Label = spec.Label,
            Mass = spec.Mass,
            DragArea = spec.DragArea,
            Class = spec.Class,
            CenterOfMass = spec.CenterOfMass,
            Position = spec.Position,
            Rotation = spec.Rotation,
            Velocity = spec.Velocity,
            AngularVelocity = spec.AngularVelocity,
            Shape = spec.Shape,
            Tag = spec.Tag,
        };
        body.Integrity = float.IsNaN(spec.Integrity) ? Impacts.Integrity(spec.Kind, spec.Mass, Rules.Integrity) : spec.Integrity;
        body.Density = float.IsNaN(spec.Density) || spec.Density <= 0f ? Rules.Water.DensityOf(spec.Kind) : spec.Density;
        _bodies.Add(body);
        Registered?.Invoke(body);
        return body;
    }

    public void Unregister(Body body)
    {
        if (!_bodies.Remove(body)) return;
        Stuck.Forget(body);
        Unpark(body);
        foreach (Body b in _bodies)
        {
            if (b.Frame == body) Riding.LeaveCabin(b);
            if (b.ParkedOn == body) Unpark(b);
        }
        Unregistered?.Invoke(body);
    }

    /// <summary>
    /// A zone handover, leaving: takes a body out of this world exactly as it is, with everything
    /// riding in its cabin (riders keep their place aboard; nothing is let go), for another zone's
    /// world to <see cref="Adopt"/> in the same tick. Anything parked on it is unparked and stays.
    /// Returns what left: the body first, then its riders.
    /// </summary>
    public List<Body> Release(Body body)
    {
        var leaving = new List<Body>();
        if (!_bodies.Contains(body)) return leaving;
        leaving.Add(body);
        foreach (Body b in _bodies)
            if (b.Frame == body) leaving.Add(b);
        foreach (Body b in leaving)
        {
            _bodies.Remove(b);
            Stuck.Forget(b);
            Unpark(b);
        }
        foreach (Body b in _bodies)
            if (b.ParkedOn != null && leaving.Contains(b.ParkedOn)) Unpark(b);
        foreach (Body b in leaving) Unregistered?.Invoke(b);
        return leaving;
    }

    /// <summary>
    /// A zone handover, arriving: takes in bodies another world released, as they are (riders stay
    /// aboard). Each gets this world's next id, so the world still steps in id order.
    /// </summary>
    public void Adopt(IEnumerable<Body> bodies)
    {
        foreach (Body b in bodies)
        {
            if (_bodies.Contains(b)) continue;
            b.Id = _nextId++;
            b.Underfoot = null;
            b.Water = WaterState.Dry;
            _bodies.Add(b);
            Registered?.Invoke(b);
        }
    }

    public Body Find(int id)
    {
        foreach (Body b in _bodies) if (b.Id == id) return b;
        return null;
    }

    // ---- The one force path ---------------------------------------------------------

    /// <summary>
    /// Adds a force for this tick, N. The only way anything pushes a body. Weather never reaches a
    /// sheltered body (<see cref="IsSheltered"/>), whichever provider sends it.
    /// </summary>
    public void AddForce(Body body, Vector3 force, ForceKind kind)
    {
        if (kind == ForceKind.Weather && IsSheltered(body)) return;
        body.Accumulate(force, kind);
    }

    /// <summary>Adds an instant change of momentum for this tick, N·s. Weather's never reaches a sheltered body.</summary>
    public void AddImpulse(Body body, Vector3 impulse, ForceKind kind)
    {
        if (kind == ForceKind.Weather && IsSheltered(body)) return;
        body.PendingImpulse += impulse;
        if (body.Asleep && body.Motion == Motion.Free) body.Asleep = false;
    }

    /// <summary>
    /// An impulse at a point (an explosion pushing a severed section away). Free bodies also
    /// spin from it; kinematic bodies take only the push, since hits never spin a flown ship.
    /// </summary>
    public void AddImpulseAt(Body body, Vector3 impulse, Vector3 point, ForceKind kind)
    {
        if (kind == ForceKind.Weather && IsSheltered(body)) return;
        AddImpulse(body, impulse, kind);
        if (body.Motion == Motion.Free)
            body.PendingAngularImpulse += Vector3.Cross(point - body.CenterOfMassPosition, impulse);
    }

    /// <summary>
    /// The acceleration gravity gives this body. In the zone's world, the zone's. Riding a structure:
    /// on its outer hull in mag boots, toward the face underfoot; otherwise its core's (ship-down),
    /// or the zone's turned into its frame when the core is off.
    /// </summary>
    public Vector3 GravityAt(Body body)
    {
        if (body.Frame == null) return Zone.At(body.Position);
        // Out on a hull in mag boots, down is the face under the feet, whatever the core does.
        if (body.OnOuterHull && Rules.Riding.MagBoots) return -body.Up * Rules.Riding.MagBootGravity;
        if (body.Frame.Core is { On: true } core) return core.Local;
        // Core gravity off: the zone's gravity, turned into the structure's frame (none in space).
        return MathX.Unrotate(body.Frame.Rotation, Zone.At(body.WorldPosition));
    }

    // ---- Water ------------------------------------------------------------------------

    /// <summary>
    /// How a body sits in water right now, from its pose: the share of its height (feet to top,
    /// upright) under the surface over it. Only bodies in the zone's world are in water; riders
    /// live in their ship's cabin world, and a base is fixed whatever laps at it.
    /// </summary>
    public WaterState WaterStateOf(Body b)
    {
        if (Water == null || b.Frame != null || b.Motion == Motion.Fixed) return WaterState.Dry;
        Vector3 p = b.CenterOfMassPosition;
        if (!Water.SurfaceAt(p, out float surface)) return WaterState.Dry;
        float feet = b.Position.Y - b.Shape.FeetBelowOrigin;
        return new WaterState(surface, WaterForces.SubmergedFraction(feet, b.Shape.Height, surface));
    }

    /// <summary>
    /// The water's buoyancy on a body right now, N. A vessel holding itself up counts it into the
    /// load its engines hold, so it flies underwater as in air (only slower, through the drag).
    /// </summary>
    public Vector3 BuoyancyOn(Body b)
    {
        WaterState w = WaterStateOf(b);
        return WaterForces.Buoyancy(b.Mass, b.Density, w.Fraction, GravityAt(b), Rules.Water);
    }

    /// <summary>Everything the water does to a body this tick: buoyancy and drag (still water), N.</summary>
    Vector3 WaterForce(Body b, float dt)
    {
        float fraction = b.Water.Fraction;
        Vector3 v = b.Motion == Motion.Free ? b.PointVelocity(b.CenterOfMassPosition) : b.Velocity;
        return WaterForces.Buoyancy(b.Mass, b.Density, fraction, GravityAt(b), Rules.Water)
               + WaterForces.Drag(b.Mass, b.DragArea, v, fraction, Rules.Water, dt);
    }

    // ---- Shelter and parking ----------------------------------------------------------

    /// <summary>
    /// Whether weather reaches a body at all: the one shelter rule, which every weather provider
    /// goes through (<see cref="AddForce"/> drops weather pushes on a sheltered body, and providers
    /// may ask first to skip the work). Sheltered are:
    /// - a structure in base mode: nothing ever moves a base;
    /// - a vessel parked on a base or a capital ship (landed on it, resting on it, docked to it): it
    ///   rides its host and feels no wind of its own (the host's own push is the host's);
    /// - a rider inside a structure's interior.
    /// Everything else is pushed by its own mass and drag: players, creatures, crates, wrecks and
    /// hover, small and capital vessels, a capital ship included.
    /// </summary>
    public bool IsSheltered(Body body) =>
        body.Kind == BodyKind.Base || body.ParkedOn != null || body.Frame != null && !body.OnOuterHull;

    /// <summary>A body other vessels park on and ride: a base, or a capital ship.</summary>
    public static bool IsHost(Body body) =>
        body != null && (body.Kind == BodyKind.Base || body.Kind == BodyKind.Vessel && body.Class == StructureClass.Capital);

    /// <summary>
    /// Parks a vessel on a host (landed on it, resting on it, or docked): from now on no wind reaches
    /// it, and while it is landed (not simulating) it moves with its host, exactly. Vessel controllers
    /// park and unpark themselves as they land, rest and lift off; a game docks a vessel by calling this.
    /// </summary>
    public void Park(Body body, Body host)
    {
        if (body.ParkedOn == host || !IsHost(host) || host == body) return;
        Unpark(body);
        body.ParkedOn = host;
        body.HostStartPosition = host.Position;
        body.HostStartRotation = host.Rotation;
        Parked?.Invoke(body, host);
    }

    /// <summary>The vessel leaves its host (it lifted off, or was knocked off it): the wind reaches it again.</summary>
    public void Unpark(Body body)
    {
        Body host = body.ParkedOn;
        if (host == null) return;
        body.ParkedOn = null;
        Unparked?.Invoke(body, host);
    }

    /// <summary>
    /// After every body has moved: each landed vessel parked on a host moves by exactly what its
    /// host did this tick, turning included, and takes on the host's velocity there, so it rides it
    /// whatever order the two step in. A parked vessel that is simulating (resting on its gear,
    /// about to land or lift off) moves itself.
    /// </summary>
    public void CarryParked()
    {
        foreach (Body b in _bodies)
        {
            Body host = b.ParkedOn;
            if (host == null || !b.Asleep) continue;
            if (host.Position != b.HostStartPosition || host.Rotation != b.HostStartRotation)
            {
                Quaternion turn = Quaternion.Normalize(host.Rotation * Quaternion.Conjugate(b.HostStartRotation));
                b.Position = host.Position + MathX.Rotate(turn, b.Position - b.HostStartPosition);
                b.Rotation = Quaternion.Normalize(turn * b.Rotation);
            }
            b.Velocity = host.PointVelocity(b.Position);
            b.AngularVelocity = host.AngularVelocity;
            b.HostStartPosition = host.Position;
            b.HostStartRotation = host.Rotation;
        }
    }

    // ---- Stepping ---------------------------------------------------------------------

    public void BeginTick()
    {
        Tick++;
        _pairs.Clear();
        // Where each parked body's host starts this tick: carrying moves it by the host's whole motion.
        foreach (Body b in _bodies)
        {
            if (b.ParkedOn == null) continue;
            b.HostStartPosition = b.ParkedOn.Position;
            b.HostStartRotation = b.ParkedOn.Rotation;
        }
    }

    /// <summary>
    /// After the engine has been handed this tick's sums: start the next tick's accumulators. Forces
    /// added between ticks (a blast's push from game code) land in the next tick, never lost.
    /// </summary>
    public void FinishTick()
    {
        foreach (Body b in _bodies) b.ClearForces();
    }

    public void ApplyForces(float dt)
    {
        // Bodies' own drives first: a landed vessel decides here whether it wakes.
        foreach (Body b in _bodies) b.Controller?.Drive(this, b, dt);

        // Nothing pulls on a base: nothing could move it. Water buoys and slows everything in it.
        foreach (Body b in _bodies)
        {
            b.Water = WaterStateOf(b);
            if (b.Asleep || b.Motion == Motion.Fixed) continue;
            AddForce(b, b.Mass * GravityAt(b), ForceKind.Gravity);
            if (b.Water.InWater) AddForce(b, WaterForce(b, dt), ForceKind.Water);
        }

        foreach (IForceProvider p in ForceProviders) p.ApplyForces(this, dt);

        // A sleeping rigid body that something now pushes (a gust, a tornado) wakes, and from
        // then on carries its own weight (and its buoyancy) like everything else.
        foreach (Body b in _bodies)
        {
            if (!b.Asleep || b.Motion != Motion.Free) continue;
            if (b.PendingForce.LengthSquared() < 1e-4f * b.Mass * b.Mass && b.PendingImpulse == Vector3.Zero) continue;
            b.Asleep = false;
            AddForce(b, b.Mass * GravityAt(b), ForceKind.Gravity);
            if (b.Water.InWater) AddForce(b, WaterForce(b, dt), ForceKind.Water);
        }

        // Controllers that react to what is already pushing (legs gripping against the wind).
        foreach (Body b in _bodies) (b.Controller as IForceReactor)?.React(this, b, dt);
    }


    /// <summary>Kinematic bodies: velocity += ΣF/m·dt + ΣJ/m. Free bodies are integrated by the engine from the same sums.</summary>
    public void Integrate(float dt)
    {
        foreach (Body b in _bodies)
        {
            if (b.Motion != Motion.Kinematic || b.Asleep) continue;
            b.Velocity += b.PendingForce / b.Mass * dt + b.PendingImpulse / b.Mass;
        }
    }

    /// <summary>After every body has moved: controllers react, craters are cut, stuck bodies are rescued.</summary>
    public void EndTick(float dt)
    {
        for (int i = 0; i < _bodies.Count; i++) _bodies[i].Controller?.AfterMove(this, _bodies[i], dt);

        // Craters are cut after everything has moved, in the order they happened, so a hole
        // never opens under a body halfway through the tick.
        for (int i = 0; i < _craters.Count; i++)
        {
            var (cause, center, radius) = _craters[i];
            if (Terrain == null) break;
            CraterResult result = Terrain.Crater(center, radius);
            Cratered?.Invoke(new CraterInfo(cause, center, radius, result.CellsRemoved, result.CellsRefused));
            if (result.CellsRemoved > 0) NotifyChanged(result.Changed);
        }
        _craters.Clear();

        Stuck.CheckAll();
        foreach (Body b in _bodies) b.Publish();
    }

    /// <summary>
    /// Terrain or a structure changed in this region, or a zone was loaded into it: wake
    /// everything nearby (a landed ship whose ground was dug out must fall, not hover), and
    /// check it for being stuck now rather than next tick.
    /// </summary>
    public void NotifyChanged(Region region)
    {
        Region near = region.Grow(2f);
        foreach (Body b in _bodies)
        {
            if (b.Frame != null || !near.Contains(b.Position) && !near.Contains(b.CenterOfMassPosition)) continue;
            if (b.Controller is VesselController v && (v.Frozen || v.Settling)) v.Wake(b);
            else b.Asleep = false;
            Stuck.Check(b);
        }
        RegionChanged?.Invoke(near);
    }

    /// <summary>A whole zone was (re)loaded: check every body at once.</summary>
    public void NotifyZoneLoaded()
    {
        foreach (Body b in _bodies)
        {
            b.Asleep = false;
            Stuck.Check(b);
        }
    }

    // ---- Contacts -----------------------------------------------------------------------

    /// <summary>
    /// A kinematic body's contacts from one move, each resolved once with the velocity from
    /// before the move, so the impact speed is the real closing speed. Returns the velocity
    /// after: slid, glanced, rammed.
    /// </summary>
    public Vector3 ResolveContacts(Body body, IReadOnlyList<Contact> contacts, Vector3 before, Vector3 velocity)
    {
        // Fixed things (terrain is many collision blocks) count as one impact per move: the
        // hardest one. Touching two terrain blocks at once is still one crash, not two.
        int hardest = -1;
        float most = 0f;
        body.Underfoot = null;
        for (int i = 0; i < contacts.Count; i++)
        {
            Contact c = contacts[i];
            if (c.Other != null && c.Other != body)
            {
                if (c.Normal.Y > 0.6f) body.Underfoot = c.Other; // resting on it (a vessel on a deck)
                velocity = Ram(body, c.Other, c, before, velocity);
                continue;
            }
            if (c.Normal.Y > 0.6f) body.OnFloor = true;
            float closing = -Vector3.Dot(before, c.Normal);
            if (closing > most)
            {
                most = closing;
                hardest = i;
            }
        }
        if (hardest >= 0) velocity = HitFixed(body, contacts[hardest], before, velocity);
        return velocity;
    }

    /// <summary>Something fixed: bounce a little, scrape only if it was a real impact.</summary>
    Vector3 HitFixed(Body body, in Contact c, Vector3 before, Vector3 velocity)
    {
        float closing = -Vector3.Dot(before, c.Normal);
        if (closing <= 0f) return velocity;

        PhysicsRules.ContactData rules = Rules.Contacts;
        if (closing > 0.5f) velocity += c.Normal * closing * rules.Restitution; // glance off
        if (Impacts.Damage(body.Mass, closing, rules, body.Kind) > 0f)
            velocity -= MathX.Slide(velocity, c.Normal) * rules.Scrape;
        (body.Controller as IContactListener)?.OnContact(this, body, c, closing);
        ResolveImpact(body, null, c.Point, c.Normal, closing, c.Surface);
        return velocity;
    }

    /// <summary>
    /// Another body: both take the same damage (from the reduced mass, so ramming always hurts
    /// the rammer too) and trade momentum along the contact normal.
    /// </summary>
    Vector3 Ram(Body body, Body other, in Contact c, Vector3 before, Vector3 velocity)
    {
        Vector3 otherVelocity = other.Frame == null ? other.PointVelocity(c.Point) : other.WorldVelocity;
        float closing = -Vector3.Dot(before - otherVelocity, c.Normal);
        if (closing <= 0f) return velocity;
        if (!_pairs.Add(Pair(body, other))) return velocity; // already resolved from the other side this tick

        float m = body.Mass, mo = MassOf(other);
        float mu = Impacts.ReducedMass(m, mo);
        float impulse = (1f + Rules.Contacts.Restitution) * mu * closing;

        // The slide already took out this body's normal speed; put back the exchange instead.
        velocity += c.Normal * (Vector3.Dot(before, c.Normal) + impulse / m);
        if (other.Motion == Motion.Kinematic) other.Velocity -= c.Normal * (impulse / mo);
        else if (other.Motion == Motion.Free) AddImpulse(other, -c.Normal * impulse, ForceKind.Contact);
        if (other.Controller is VesselController v && (v.Frozen || v.Settling)) v.Wake(other);
        other.Asleep = false;

        float damage = Impacts.Damage(mu, closing, Rules.Contacts, body.Kind);
        Hurt(body, damage);
        Hurt(other, Impacts.Damage(mu, closing, Rules.Contacts, other.Kind));
        (body.Controller as IContactListener)?.OnContact(this, body, c, closing);
        if (closing > Rules.Contacts.EventSpeed)
            Impact?.Invoke(new ImpactInfo(body, other, c.Point, c.Normal, closing, damage, Surface.Body, 0f));
        return velocity;
    }

    /// <summary>
    /// A hit on something fixed (the ground, a rock, a landing foot slamming down): damage
    /// from the body's own mass, and a fast hit on the ground queues a real, capped crater.
    /// </summary>
    public void ResolveImpact(Body body, Body other, Vector3 point, Vector3 normal, float closing, Surface surface)
    {
        if (other != null)
        {
            if (!_pairs.Add(Pair(body, other))) return;
            float mu = Impacts.ReducedMass(body.Mass, MassOf(other));
            float both = Impacts.Damage(mu, closing, Rules.Contacts, body.Kind);
            Hurt(body, both);
            Hurt(other, Impacts.Damage(mu, closing, Rules.Contacts, other.Kind));
            if (closing > Rules.Contacts.EventSpeed)
                Impact?.Invoke(new ImpactInfo(body, other, point, normal, closing, both, Surface.Body, 0f));
            return;
        }

        float damage = Impacts.Damage(body.Mass, closing, Rules.Contacts, body.Kind);
        Hurt(body, damage);
        float crater = 0f;
        if (surface == Surface.Ground && normal.Y > 0.5f && Terrain != null)
        {
            crater = Impacts.CraterRadius(body.Mass, closing, Rules.Craters);
            if (crater > 0f) _craters.Add((body, point + normal * crater * Rules.Craters.Lift, crater));
        }
        if (closing > Rules.Contacts.EventSpeed)
            Impact?.Invoke(new ImpactInfo(body, null, point, normal, closing, damage, surface, crater));
    }

    /// <summary>
    /// A free body's velocity changed by more than its forces explain: something stopped it.
    /// The closing speed is that change along the contact normal (free bodies don't bounce,
    /// so it is the full closing speed). Hits between two registered bodies count once.
    /// </summary>
    public void FreeBodyImpact(Body body, Vector3 unexplained, IReadOnlyList<Contact> contacts)
    {
        if (contacts.Count == 0) return;
        Vector3 n = Vector3.Zero;
        Contact main = contacts[0];
        foreach (Contact c in contacts)
        {
            n += c.Normal;
            if (c.Other != null && main.Other == null) main = c;
        }
        n = MathX.SafeNormalize(n, main.Normal);
        float closing = Vector3.Dot(unexplained, n);
        if (closing <= Rules.Contacts.EventSpeed) return;
        if (main.Other == null && main.Surface == Surface.Ground && n.Y > 0.6f) body.OnFloor = true;
        ResolveImpact(body, main.Other, main.Point, n, closing, main.Other != null ? Surface.Body : main.Surface);
    }

    /// <summary>Applies damage in full: to the body's own count, or through its sink (a player's health).</summary>
    public float Hurt(Body body, float raw)
    {
        if (raw <= 0f) return 0f;
        float dealt = body.DamageSink?.Take(body, raw) ?? raw;
        body.Damage += dealt;
        return dealt;
    }

    static (int, int) Pair(Body a, Body b) => a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id);

    /// <summary>The mass a contact meets: a base is fixed to the planet, so hitting one is like hitting the ground.</summary>
    static float MassOf(Body b) => b.Motion == Motion.Fixed ? float.PositiveInfinity : b.Mass;

    // ---- Structures -------------------------------------------------------------------

    /// <summary>
    /// A section has broken off a structure. The wreck is the same rigid motion as the
    /// structure it came from (same origin, same velocity and spin), so every block keeps the
    /// velocity it had: total mass and momentum are unchanged at the moment of severing.
    /// </summary>
    public Body Sever(Body parent, float parentMass, Vector3 parentCenterOfMass, BodySpec wreck)
    {
        wreck.Kind = BodyKind.Wreck;
        wreck.Motion = Motion.Free;
        wreck.Position = parent.Position;
        wreck.Rotation = parent.Rotation;
        wreck.Velocity = parent.Velocity;
        wreck.AngularVelocity = parent.AngularVelocity;
        Body body = Register(wreck);
        parent.Mass = parentMass;
        parent.CenterOfMass = parentCenterOfMass;
        Severed?.Invoke(new SeverInfo(parent, body));
        return body;
    }

    /// <summary>A vessel with no hull points left loses power: from now on it is a free wreck the engine tumbles.</summary>
    public void MakeWreck(Body body)
    {
        if (body.Motion == Motion.Free) return;
        body.Kind = BodyKind.Wreck;
        body.Motion = Motion.Free;
        body.Controller = null;
        body.Asleep = false;
        // A wreck is nobody's host and parks nowhere: vessels landed on it are knocked loose.
        Unpark(body);
        foreach (Body b in _bodies)
        {
            if (b.ParkedOn != body) continue;
            Unpark(b);
            if (b.Controller is VesselController v) v.Wake(b);
        }
        MotionChanged?.Invoke(body);
    }

    // ---- Events the controllers raise -------------------------------------------------

    internal void RaiseLanded(Body b) => Landed?.Invoke(b);
    internal void RaiseLiftedOff(Body b) => LiftedOff?.Invoke(b);
    internal void RaiseRescued(RescueInfo r) => Rescued?.Invoke(r);
}

/// <summary>A query that finds nothing: for pure tests with no ground.</summary>
public sealed class NoQuery : IWorldQuery
{
    public static readonly NoQuery Instance = new();

    public bool Ray(Vector3 from, Vector3 to, uint mask, Body exclude, out RayHit hit)
    {
        hit = default;
        return false;
    }
}
