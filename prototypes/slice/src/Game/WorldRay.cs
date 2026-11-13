using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;

namespace Slice.Game;

/// <summary>What a look ray hit, in the zone's world (frame coordinates).</summary>
public readonly record struct LookHit(Vector3 Point, Vector3 Normal, float Distance, Vessel Vessel, Int3 Cell, GodotObject Collider, int Shape = -1)
{
    /// <summary>A structure's block (its cell is <see cref="Cell"/>).</summary>
    public bool OnBlock => Vessel != null;
}

/// <summary>
/// Every interaction and tool ray asks the world the survivor is really in. Aboard a structure
/// that is its cabin world (a still copy of its blocks, structure-local) as well as the zone's
/// world beyond it; on foot, the zone's world. So E, the tool and the build tool all work the
/// same inside a pod as out on the ground, and nothing reaches through a wall you are behind.
/// </summary>
public static class WorldRay
{
    /// <summary>Colliders that only keep bodies apart and are never aimed at (the creatures' blockers: a shot or a dig meets the creature itself).</summary>
    public static readonly Godot.Collections.Array<Rid> Ignored = new();

    /// <summary>The nearest thing along the survivor's look within reach, on the given zone layers. Blocks of the structure you are in count from the inside.</summary>
    public static bool Cast(SurfaceZone zone, Survivor survivor, float reach, uint mask, out LookHit hit) =>
        Cast(zone, survivor.Aboard, survivor.WorldEye(), survivor.WorldLook(), reach, mask, out hit);

    public static bool Cast(SurfaceZone zone, Vessel aboard, Vector3 eye, Vector3 dir, float reach, uint mask, out LookHit hit)
    {
        hit = default;
        float best = reach;
        bool found = false;

        // Inside: the cabin world, structure-local.
        if (aboard?.Cabin?.FindWorld3D() is World3D cabin)
        {
            Vector3 from = aboard.Body.ToLocal(eye.ToNumerics()).ToGodot();
            Vector3 to = aboard.Body.ToLocal((eye + dir * reach).ToNumerics()).ToGodot();
            var q = PhysicsRayQueryParameters3D.Create(from, to, Layers.Interior);
            Godot.Collections.Dictionary r = cabin.DirectSpaceState.IntersectRay(q);
            if (r.Count > 0)
            {
                Vector3 local = (Vector3)r["position"], localNormal = (Vector3)r["normal"];
                float d = from.DistanceTo(local);
                if (d < best)
                {
                    best = d;
                    Vector3 point = aboard.Body.ToParent(local.ToNumerics()).ToGodot();
                    Vector3 normal = MathX.Rotate(aboard.Body.Rotation, localNormal.ToNumerics()).ToGodot();
                    hit = new LookHit(point, normal, d, aboard, CellOf(aboard, local, localNormal), (GodotObject)r["collider"]);
                    found = true;
                }
            }
        }

        // The zone's world, never the structure you are in (its cabin answered for it).
        var zq = PhysicsRayQueryParameters3D.Create(eye, eye + dir * best, mask);
        var exclude = new Godot.Collections.Array<Rid>(Ignored);
        if (aboard?.Binding?.Collider is CollisionObject3D own) exclude.Add(own.GetRid());
        zq.Exclude = exclude;
        Godot.Collections.Dictionary z = zone.GetWorld3D().DirectSpaceState.IntersectRay(zq);
        if (z.Count > 0)
        {
            Vector3 point = (Vector3)z["position"], normal = (Vector3)z["normal"];
            float d = eye.DistanceTo(point);
            if (d <= best)
            {
                var collider = (GodotObject)z["collider"];
                Vessel v = zone.VesselOf(zone.Host.BodyOf(collider));
                Int3 cell = v == null ? default : CellOf(v, v.Body.ToLocal(point.ToNumerics()).ToGodot(), MathX.Unrotate(v.Body.Rotation, normal.ToNumerics()).ToGodot());
                hit = new LookHit(point, normal, d, v, cell, collider, (int)z["shape"]);
                found = true;
            }
        }
        return found;
    }

    /// <summary>The block a local hit is on: just inside the face it hit.</summary>
    static Int3 CellOf(Vessel v, Vector3 local, Vector3 localNormal) =>
        StructureBridge.CellAt(v.Structure, (local - localNormal * 0.02f).ToNumerics());
}
