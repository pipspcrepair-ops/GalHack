using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// The one set of units and axes every body in GalHack is simulated in.
///
/// Metres, kilograms, seconds and newtons, always. Angles are radians in code and degrees
/// only in data and on screens. +Y is up, -Z is forward and +X is right (Godot's axes), and
/// a body's angular velocity is in the same frame as its velocity.
///
/// Some cores keep friendlier units of their own: the handling model and the building
/// catalogue talk in tonnes and kN, so thrust / mass reads straight as m/s². The ratio is the
/// same in both systems; convert at the boundary with these helpers and never inside a rule.
/// </summary>
public static class Units
{
    public const float KilogramsPerTonne = 1000f;
    public const float NewtonsPerKilonewton = 1000f;

    /// <summary>The planet gravity every prototype so far uses, m/s². Zones set their own.</summary>
    public const float StandardGravity = 9.8f;

    public static float Tonnes(float kilograms) => kilograms / KilogramsPerTonne;
    public static float Kilograms(float tonnes) => tonnes * KilogramsPerTonne;
    public static float Newtons(float kilonewtons) => kilonewtons * NewtonsPerKilonewton;
    public static float Kilonewtons(float newtons) => newtons / NewtonsPerKilonewton;

    public static float Radians(float degrees) => degrees * (MathF.PI / 180f);
    public static float Degrees(float radians) => radians * (180f / MathF.PI);

    public static readonly Vector3 Up = Vector3.UnitY;
    public static readonly Vector3 Down = -Vector3.UnitY;
    public static readonly Vector3 Forward = -Vector3.UnitZ;
    public static readonly Vector3 Right = Vector3.UnitX;
}
