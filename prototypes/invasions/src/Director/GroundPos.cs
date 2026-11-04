using System;
using System.Globalization;

namespace Invasions.Director;

// Everything under src/Director and src/Model is plain C#: no Godot types, no scene nodes.
// The director decides; the scene only presents what it decided and reports back what happened.

/// <summary>
/// A point on the ground plane in metres, on Godot's axes: X east and Z south, so north is -Z.
/// Doubles rather than floats so the simulated clock and positions replay bit-for-bit.
/// </summary>
public readonly record struct GroundPos(double X, double Z)
{
    public static GroundPos operator +(GroundPos a, GroundPos b) => new(a.X + b.X, a.Z + b.Z);
    public static GroundPos operator -(GroundPos a, GroundPos b) => new(a.X - b.X, a.Z - b.Z);
    public static GroundPos operator *(GroundPos a, double k) => new(a.X * k, a.Z * k);

    public double Length => Math.Sqrt(X * X + Z * Z);
    public double DistanceTo(GroundPos other) => (other - this).Length;
    public GroundPos Lerp(GroundPos to, double t) => this + (to - this) * t;

    public GroundPos Normalized()
    {
        double length = Length;
        return length > 1e-9 ? new GroundPos(X / length, Z / length) : new GroundPos(0, -1);
    }

    /// <summary>The direction 90° clockwise (seen from above): east when this points north.</summary>
    public GroundPos Right => new(-Z, X);

    /// <summary>Compass bearing in degrees from here to another point: 0 north, 90 east.</summary>
    public double BearingTo(GroundPos other)
    {
        GroundPos d = other - this;
        double degrees = Math.Atan2(d.X, -d.Z) * 180.0 / Math.PI;
        return (degrees + 360.0) % 360.0;
    }

    /// <summary>The offset that lies <paramref name="distance"/> metres away on a compass bearing.</summary>
    public static GroundPos FromBearing(double degrees, double distance)
    {
        double radians = degrees * Math.PI / 180.0;
        return new GroundPos(Math.Sin(radians) * distance, -Math.Cos(radians) * distance);
    }

    static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    /// <summary>The nearest of the eight compass points to a bearing.</summary>
    public static string Compass(double bearing) => Points[(int)Math.Round(((bearing % 360) + 360) % 360 / 45.0) % 8];

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:F1}, {Z:F1})");
}
