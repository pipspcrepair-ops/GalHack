using Godot;
using N = System.Numerics;

namespace GalHack.Physics.Engine;

/// <summary>
/// Conversions between the core's System.Numerics types and Godot's. The component layout
/// and handedness are the same, so these are plain copies; only the boundary uses them.
/// </summary>
public static class GodotMath
{
    public static Vector3 ToGodot(this N.Vector3 v) => new(v.X, v.Y, v.Z);
    public static N.Vector3 ToNumerics(this Vector3 v) => new(v.X, v.Y, v.Z);

    public static Quaternion ToGodot(this N.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
    public static N.Quaternion ToNumerics(this Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    public static Basis ToBasis(this N.Quaternion q) => new(q.ToGodot());
    public static N.Quaternion ToNumerics(this Basis b) => b.GetRotationQuaternion().ToNumerics();

    /// <summary>A body's pose as a Godot transform, in whatever frame its position is in.</summary>
    public static Transform3D TransformOf(Body b) => new(b.Rotation.ToBasis(), b.Position.ToGodot());
}
