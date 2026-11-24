using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// Stuck-safety for every registered body (terrain-digging's rule 7, made general): anything
/// found inside terrain or inside a structure's blocks for two ticks in a row is moved to the
/// nearest open space, preferring up and preferring somewhere to stand. Checked every tick
/// (a few density or block samples per body), and straight away after terrain edits,
/// structure edits and zone loads, so a rescue always lands within two physics frames.
///
/// Nothing a player owns or drops can fall through the world: terrain only shrinks, so the
/// only ways in are teleports, loads, network corrections, a tunnelling physics step, or a
/// block placed where something stands. All of them end here.
///
/// Riders of a structure are checked in the world along their own up: against their own ship's
/// blocks, and, out on the hull, against the terrain (a ship flown low can scrape a rider through
/// a hill, since its cabin world holds no terrain). Structure bodies (vessels and wrecks) are
/// checked at their centre of mass against terrain only; hulls keep out of each other through
/// the movers and the engine.
/// </summary>
public sealed class StuckGuard
{
    readonly PhysicsWorld _world;
    readonly Dictionary<Body, int> _ticks = new();

    public int Rescues { get; private set; }

    /// <summary>Candidate offsets, 1 m apart, nearest first; going down costs more than going up.</summary>
    readonly Vector3[] Offsets;

    public StuckGuard(PhysicsWorld world)
    {
        _world = world;
        Offsets = BuildOffsets(Math.Max(1, (int)MathF.Ceiling(world.Rules.Stuck.SearchRadius)));
    }

    static Vector3[] BuildOffsets(int r)
    {
        var list = new List<(int cost, int x, int y, int z)>();
        for (int y = -r; y <= r; y++)
            for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    int cost = x * x + z * z + (y < 0 ? 3 * y * y : y * y);
                    if (cost <= r * r) list.Add((cost, x, y, z));
                }
        list.Sort((a, b) => a.cost != b.cost ? a.cost.CompareTo(b.cost)
            : a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z));
        var offsets = new Vector3[list.Count];
        for (int i = 0; i < offsets.Length; i++) offsets[i] = new Vector3(list[i].x, list[i].y, list[i].z);
        return offsets;
    }

    public void Forget(Body body) => _ticks.Remove(body);

    /// <summary>Checks every body once (call once per tick). Bodies stuck long enough are rescued.</summary>
    public void CheckAll()
    {
        IReadOnlyList<Body> bodies = _world.Bodies;
        for (int i = 0; i < bodies.Count; i++) Check(bodies[i]);
    }

    /// <summary>Checks one body: counts a tick inside, rescues it once it has been inside long enough.</summary>
    public void Check(Body body)
    {
        IStuckField inside = Inside(body);
        if (inside == null)
        {
            _ticks[body] = 0;
            return;
        }
        _ticks.TryGetValue(body, out int ticks);
        if (++ticks < _world.Rules.Stuck.TicksBeforeRescue)
        {
            _ticks[body] = ticks;
            return;
        }
        _ticks[body] = 0;
        Rescue(body, inside);
    }

    static bool IsStructure(Body b) => b.Kind is BodyKind.Vessel or BodyKind.Wreck;

    public static Vector3 Feet(Body b) => b.Position - Vector3.UnitY * b.Shape.FeetBelowOrigin;

    /// <summary>The body's feet in the world, wherever it is simulated.</summary>
    public static Vector3 WorldFeet(Body b) => b.Frame == null ? Feet(b) : b.Frame.ToParent(Feet(b));

    public bool IsStuck(Body body, out string inside)
    {
        IStuckField field = Inside(body);
        inside = field?.Name;
        return field != null;
    }

    /// <summary>The field a body is stuck inside right now, or null.</summary>
    public IStuckField Inside(Body body)
    {
        // A base is fixed where it was built, into a hillside if need be: nothing ever moves it.
        if (body.Mass <= 0f || body.Motion == Motion.Fixed) return null;
        // A player held asleep (in a seat, on a bike) is set where it is every tick, inside a chair's cells if need be: never stuck.
        if (body.Asleep && body.Kind == BodyKind.Player) return null;
        foreach (IStuckField field in _world.StuckFields)
        {
            if (!field.Applies(body)) continue;
            // A loose structure's centre of mass may sit in a thin fitting lying on the ground, a few centimetres off the density's
            // surface: it counts as buried only when a little above its centre is solid too.
            bool stuck = IsStructure(body) ? field.IsSolid(body, body.CenterOfMassPosition)
                    && (body.Motion != Motion.Free || field.IsSolid(body, body.CenterOfMassPosition + Vector3.UnitY * 0.15f))
                : body.Frame == null && body.Up.Y > 0.999f ? field.IsInside(body, Feet(body), body.Shape)
                : InsideAlongUp(body, field);
            if (stuck) return field;
        }
        return null;
    }

    /// <summary>
    /// A body's core, sampled along its own up in the world: a rider in mag boots on a ship's side
    /// or underside points sideways or down (and so may someone drifting in space after letting
    /// go), and must not be mistaken for being inside the hull.
    /// </summary>
    static bool InsideAlongUp(Body body, IStuckField field)
    {
        Vector3 feet = WorldFeet(body);
        Vector3 up = Vector3.Normalize(body.Frame == null ? body.Up : MathX.Rotate(body.Frame.Rotation, body.Up));
        float bottom = body.Shape.Radius, top = MathF.Max(body.Shape.Height - body.Shape.Radius, body.Shape.Radius);
        int steps = Math.Max(1, (int)MathF.Ceiling((top - bottom) / 0.25f));
        for (int i = 0; i <= steps; i++)
            if (field.IsSolid(body, feet + up * (bottom + (top - bottom) * i / steps))) return true;
        return false;
    }

    /// <summary>
    /// Moves a body to the nearest open space, at rest. A rider found inside its own ship stays
    /// aboard; one found inside anything else (terrain, another ship) goes back to the zone's world.
    /// A hull that came down into the ground where it has no collider yet (colliders stand only near
    /// the player) has still crashed: it takes the impact it would have taken on the surface.
    /// </summary>
    public void Rescue(Body body, IStuckField inside)
    {
        Vector3 from = body.WorldPosition;
        float closing = IsStructure(body) ? -body.Velocity.Y : 0f;
        Vector3 feet = IsStructure(body) ? LiftStructure(body) : FindOpen(body, WorldFeet(body), body.Shape);
        if (body.Frame != null && inside?.Owner != body.Frame) Riding.LeaveCabin(body);
        body.Position = body.Frame != null
            ? body.Frame.ToLocal(feet) + Vector3.UnitY * body.Shape.FeetBelowOrigin
            : feet + Vector3.UnitY * body.Shape.FeetBelowOrigin;
        body.Velocity = Vector3.Zero;
        body.AngularVelocity = Vector3.Zero;
        body.Asleep = false;
        if (body.Controller is VesselController vessel) vessel.Wake(body);
        Rescues++;
        _world.RaiseRescued(new RescueInfo(body, from, body.WorldPosition, inside?.Name));
        if (closing > 0f) _world.ResolveImpact(body, inside?.Owner, feet, Vector3.UnitY, closing, inside?.Owner != null ? Surface.Body : Surface.Ground);
    }

    /// <summary>A whole hull goes straight up until its keel clears the highest ground under its footprint.</summary>
    Vector3 LiftStructure(Body body)
    {
        Vector3 feet = Feet(body);
        float r = MathF.Max(0.5f, body.Shape.Radius), top = float.NegativeInfinity;
        Vector3 c = body.CenterOfMassPosition;
        Span<Vector2> probes = stackalloc Vector2[] { new(0, 0), new(r, 0), new(-r, 0), new(0, r), new(0, -r) };
        foreach (IStuckField field in _world.StuckFields)
        {
            if (!field.Applies(body)) continue;
            foreach (Vector2 p in probes) top = MathF.Max(top, field.TopAt(body, c.X + p.X, c.Z + p.Y));
        }
        if (float.IsFinite(top)) return new Vector3(feet.X, MathF.Max(feet.Y, top + 0.2f), feet.Z);

        // Nothing under it at all (it fell off the world): a field knows where to bring it back.
        foreach (IStuckField field in _world.StuckFields)
            if (field.Applies(body) && field.TryFindOpen(body, feet, body.Shape, out Vector3 open))
                return open + Vector3.UnitY * 0.5f;
        return feet;
    }

    /// <summary>
    /// The nearest spot where the body fits in every field: each field's own suggestion first
    /// (terrain knows its density field best), then a shared search on a 1 m grid, first for a
    /// spot with something to stand on, then any open spot. If nothing fits nearby, it goes on
    /// top of its column.
    /// </summary>
    public Vector3 FindOpen(Body body, Vector3 feet, StuckShape shape)
    {
        List<IStuckField> fields = Applicable(body);
        // A rider aboard is rescued within its own structure, near where it is: never to a spot the ground offers far
        // below the ship (in orbit, the terrain's open space is hundreds of metres down).
        bool Ours(IStuckField f) => body.Frame == null || f.Owner == body.Frame;
        foreach (IStuckField field in fields)
            if (Ours(field) && field.TryFindOpen(body, feet, shape, out Vector3 open) && FitsAll(body, fields, open, shape))
                return open;

        var start = new Vector3(MathF.Floor(feet.X), MathF.Floor(feet.Y + shape.Height * 0.5f), MathF.Floor(feet.Z));
        for (int pass = 0; pass < 2; pass++)
        {
            bool needGround = pass == 0;
            foreach (Vector3 offset in Offsets)
            {
                Vector3 cell = start + offset;
                Vector3 centre = cell + new Vector3(0.5f);
                if (SolidInAny(body, fields, centre)) continue;
                var candidate = new Vector3(centre.X, cell.Y + 0.05f, centre.Z);
                if (needGround && !SupportedInAny(body, fields, candidate)) continue;
                if (FitsAll(body, fields, candidate, shape)) return candidate;
            }
        }

        float top = float.NegativeInfinity;
        foreach (IStuckField field in fields) if (Ours(field)) top = MathF.Max(top, field.TopAt(body, feet.X, feet.Z));
        return float.IsFinite(top) ? new Vector3(feet.X, top + 0.1f, feet.Z) : feet + Vector3.UnitY * 2f;
    }

    List<IStuckField> Applicable(Body body)
    {
        var list = new List<IStuckField>();
        foreach (IStuckField f in _world.StuckFields) if (f.Applies(body)) list.Add(f);
        return list;
    }

    static bool FitsAll(Body b, List<IStuckField> fields, Vector3 feet, StuckShape shape)
    {
        foreach (IStuckField f in fields) if (!f.Fits(b, feet, shape)) return false;
        return true;
    }

    static bool SolidInAny(Body b, List<IStuckField> fields, Vector3 p)
    {
        foreach (IStuckField f in fields) if (f.IsSolid(b, p)) return true;
        return false;
    }

    static bool SupportedInAny(Body b, List<IStuckField> fields, Vector3 feet)
    {
        foreach (IStuckField f in fields) if (f.Supports(b, feet)) return true;
        return false;
    }
}
