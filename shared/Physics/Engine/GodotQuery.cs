using System.Collections.Generic;
using Godot;
using N = System.Numerics;

namespace GalHack.Physics.Engine;

/// <summary>
/// Answers the core's ray questions with the main world's physics space, and tells the core
/// what a collider is: a registered body, the ground, water or something static.
/// </summary>
public sealed class GodotQuery : IWorldQuery
{
    readonly PhysicsHost _host;
    readonly PhysicsRayQueryParameters3D _ray = new() { HitFromInside = false };
    readonly Dictionary<Body, Godot.Collections.Array<Rid>> _excludes = new();
    static readonly Godot.Collections.Array<Rid> NoExclude = new();

    public PhysicsDirectSpaceState3D Space;

    public GodotQuery(PhysicsHost host) => _host = host;

    public bool Ray(N.Vector3 from, N.Vector3 to, uint mask, Body exclude, out RayHit hit)
    {
        if (Space == null)
        {
            hit = default;
            return false;
        }
        _ray.From = from.ToGodot();
        _ray.To = to.ToGodot();
        _ray.CollisionMask = mask;
        _ray.Exclude = exclude == null ? NoExclude : ExcludeFor(exclude);
        Godot.Collections.Dictionary result = Space.IntersectRay(_ray);
        if (result.Count == 0)
        {
            hit = default;
            return false;
        }
        GodotObject collider = result["collider"].AsGodotObject();
        Body body = _host.BodyOf(collider);
        Surface surface = body != null ? Surface.Body : SurfaceOf(collider);
        object part = null;
        if (collider == null)
        {
            // A body made on the physics server alone (the flora's): only then are its body and shape read (rays are many).
            Rid rid = result["rid"].AsRid();
            surface = SurfaceOf(rid);
            part = PartOf(surface, rid, result["shape"].AsInt32());
        }
        hit = new RayHit(result["position"].AsVector3().ToNumerics(), result["normal"].AsVector3().ToNumerics(), body, surface, part);
        return true;
    }

    /// <summary>The handle a flora hit carries for the game's flora field: its body and shape. Nothing for the rest.</summary>
    public static object PartOf(Surface surface, Rid body, int shape) => surface == Surface.Flora ? (body, shape) : null;

    /// <summary>What a body made on the physics server alone is (no node: the flora's), by its layer.</summary>
    public static Surface SurfaceOf(Rid body) => SurfaceOfLayer(PhysicsServer3D.BodyGetCollisionLayer(body));

    Godot.Collections.Array<Rid> ExcludeFor(Body body)
    {
        if (_excludes.TryGetValue(body, out var list)) return list;
        list = new Godot.Collections.Array<Rid>();
        if (_host.BindingOf(body)?.Collider is CollisionObject3D o && GodotObject.IsInstanceValid(o)) list.Add(o.GetRid());
        _excludes[body] = list;
        return list;
    }

    /// <summary>Forget a body's cached exclusion (its collider node changed).</summary>
    public void Forget(Body body) => _excludes.Remove(body);

    /// <summary>What an unregistered collider is, by its layer.</summary>
    public static Surface SurfaceOf(GodotObject collider) =>
        collider is CollisionObject3D o ? SurfaceOfLayer(o.CollisionLayer) : Surface.Static;

    static Surface SurfaceOfLayer(uint layer) =>
        (layer & Layers.World) != 0 ? Surface.Ground : (layer & Layers.Water) != 0 ? Surface.Water : (layer & Layers.Flora) != 0 ? Surface.Flora : Surface.Static;
}
