using System.Numerics;

namespace GalHack.Physics;

/// <summary>A ray hit, as the world query found it (<see cref="Contact.Part"/>: the engine's handle on what it hit, for flora).</summary>
public readonly record struct RayHit(Vector3 Point, Vector3 Normal, Body Body, Surface Surface, object Part = null);

/// <summary>
/// The few spatial questions the rules ask (hover engines and landing gear probe the ground).
/// The engine adapter answers them; tests can answer them with a flat plane.
/// </summary>
public interface IWorldQuery
{
    /// <summary>First hit between two points on the given layers, never <paramref name="exclude"/> itself.</summary>
    bool Ray(Vector3 from, Vector3 to, uint mask, Body exclude, out RayHit hit);
}

/// <summary>
/// Adds forces from outside the body: weather wind and tornadoes, a tractor beam, a
/// current. Called once per tick in registration order, between gravity and the bodies' own
/// drives. It must add forces only through <see cref="PhysicsWorld.AddForce"/> or
/// <see cref="PhysicsWorld.AddImpulse"/>.
/// </summary>
public interface IForceProvider
{
    void ApplyForces(PhysicsWorld world, float dt);
}

/// <summary>What a crater did to the terrain.</summary>
public readonly record struct CraterResult(int CellsRemoved, int CellsRefused, Region Changed);

/// <summary>
/// The terrain's edit API as the physics rules need it: blast a capped, remove-only crater
/// and log it. The terrain owns every rule about what may be removed (bedrock, anchors, caps);
/// physics only decides when a hit is hard enough and how big the blast is.
/// </summary>
public interface ITerrainEditor
{
    CraterResult Crater(Vector3 center, float radius);
}

/// <summary>
/// A planet's standing trees as the rules need them: which one a structure meets, how thick its trunk is,
/// and felling it. Whether it gives way is the rules' (<see cref="PhysicsRules.FloraData"/>); the game owns
/// what felling does (the felled list, the colliders, what is left standing).
/// </summary>
public interface IFloraField
{
    /// <summary>
    /// The tree a flora collider belongs to (a structure's contact's or its gear's ray hit's <see cref="Contact.Part"/>),
    /// and its trunk's radius, m: 0 for one already felled whose collider has yet to go. False for rock (boulders,
    /// crystals), which never gives way.
    /// </summary>
    bool TreeAt(object part, out long tree, out float trunkRadius);

    /// <summary>Fells it for good, as harvesting does: its colliders gone at once, a stump left, its trunk and crown falling away along <paramref name="away"/>.</summary>
    void Fell(long tree, Vector3 point, Vector3 away);
}

/// <summary>
/// Something a body can be stuck inside: terrain, or a structure's blocks. Points are in the
/// world frame. Fields answer with their own data (a density field, a block grid), never the
/// collision mesh, so they stay right while colliders are being rebuilt.
/// </summary>
public interface IStuckField
{
    string Name { get; }

    /// <summary>The structure these blocks belong to, or null (terrain). A rider found inside its own ship stays aboard.</summary>
    Body Owner { get; }

    /// <summary>Whether this field should keep this body out at all (a structure never rescues its own riders or itself).</summary>
    bool Applies(Body body);

    /// <summary>Genuinely inside: the body's core is in solid matter, not merely touching it.</summary>
    bool IsInside(Body body, Vector3 feet, StuckShape shape);

    /// <summary>The body would fit here with a little room on every side.</summary>
    bool Fits(Body body, Vector3 feet, StuckShape shape);

    /// <summary>Solid matter at this point (a quick reject while searching).</summary>
    bool IsSolid(Body body, Vector3 point);

    /// <summary>Something to stand on just under these feet.</summary>
    bool Supports(Body body, Vector3 feet);

    /// <summary>The top of the highest solid matter in this column, or negative infinity.</summary>
    float TopAt(Body body, float x, float z);

    /// <summary>
    /// The field's own best idea of the nearest open space (terrain knows its density field
    /// best). False to leave it to the shared search. The world checks the answer against every
    /// other field before using it.
    /// </summary>
    bool TryFindOpen(Body body, Vector3 feet, StuckShape shape, out Vector3 openFeet);
}

/// <summary>
/// Optional: a controller that reacts to the forces already on its body this tick, after
/// gravity and the outside forces (legs gripping the ground against the wind, a landed ship
/// woken by a tornado). It may add forces of its own.
/// </summary>
public interface IForceReactor
{
    void React(PhysicsWorld world, Body body, float dt);
}

/// <summary>Optional: a controller that wants to hear about its body's contacts (hover vessels tip a little on a bump).</summary>
public interface IContactListener
{
    void OnContact(PhysicsWorld world, Body body, in Contact contact, float closingSpeed);
}
