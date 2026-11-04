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
        hit = new RayHit(result["position"].AsVector3().ToNumerics(), result["normal"].AsVector3().ToNumerics(),
            body, body != null ? Surface.Body : SurfaceOf(collider));
        return true;
    }

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
    public static Surface SurfaceOf(GodotObject collider)
    {
        if (collider is not CollisionObject3D o) return Surface.Static;
        uint layer = o.CollisionLayer;
        if ((layer & Layers.World) != 0) return Surface.Ground;
        if ((layer & Layers.Water) != 0) return Surface.Water;
        return Surface.Static;
    }
}
