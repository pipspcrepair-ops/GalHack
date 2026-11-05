using System;
using System.Numerics;

namespace Slice.Orbit;

/// <summary>A place on the planet's map (metres, the torus surface's own x and z) and a height above it.</summary>
public readonly record struct MapPlace(double X, double Z, double Altitude);

/// <summary>
/// The planet from orbit is a sphere textured with its own colour map (equirectangular, as
/// planet-generator's orbit globe draws it); the surface is the torus map, flat. This is the one
/// mapping between them, both ways, for positions, orientations and velocities:
///
/// - longitude λ = 2π·x / width, from the sphere's +Z towards +X (east); colatitude θ = π·z / height,
///   so the map's top row is the north pole, its middle row the equator and its bottom row the
///   south pole. The sphere direction is (sinθ·sinλ, cosθ, sinθ·cosλ): Godot's sphere mesh UVs.
/// - a surface frame is east = +X, up = +Y, south = +Z (the map's z grows southward). Its sphere
///   counterpart at a point is (east, up, south) of the sphere there, so a vessel arriving nose-east
///   arrives nose-east, and pitch and roll carry over as they are. East and south depend only on
///   longitude at the poles, so a pole is no special case: whatever longitude a pole is given, the
///   frames on both sides agree, and the round trip is exact.
///
/// Pure arithmetic in doubles: tests run it without the engine.
/// </summary>
public sealed class GlobeMap
{
    public readonly double Width, Height, Radius;

    /// <param name="width">The map's width, metres (one turn of longitude).</param>
    /// <param name="height">The map's height, metres (pole to pole).</param>
    /// <param name="radius">The globe's radius in the orbit zone, metres.</param>
    public GlobeMap(double width, double height, double radius)
    {
        Width = width;
        Height = height;
        Radius = radius;
    }

    // ---- Angles -------------------------------------------------------------------------------

    public double Longitude(double x) => Wrap(x, Width) / Width * Math.Tau;
    public double Colatitude(double z) => Math.Clamp(z, 0, Height) / Height * Math.PI;

    /// <summary>Latitude in degrees, +90 at the north pole (map row 0).</summary>
    public double LatitudeDegrees(double z) => 90.0 - Colatitude(z) * 180.0 / Math.PI;

    /// <summary>The unit direction from the globe's centre over a map point.</summary>
    public Vector3 Direction(double x, double z)
    {
        double lon = Longitude(x), col = Colatitude(z);
        return new Vector3((float)(Math.Sin(col) * Math.Sin(lon)), (float)Math.Cos(col), (float)(Math.Sin(col) * Math.Cos(lon)));
    }

    /// <summary>
    /// The map point under a direction from the centre. At a pole the longitude is free: it is
    /// taken from <paramref name="poleLongitude"/> (radians), so a caller can keep the one it had.
    /// </summary>
    public (double x, double z) MapPoint(Vector3 dir, double poleLongitude = 0)
    {
        Vector3 d = Vector3.Normalize(dir);
        double horizontal = Math.Sqrt((double)d.X * d.X + (double)d.Z * d.Z);
        double col = Math.Atan2(horizontal, d.Y);
        double lon = horizontal < 1e-9 ? poleLongitude : Math.Atan2(d.X, d.Z);
        if (lon < 0) lon += Math.Tau;
        return (Wrap(lon / Math.Tau * Width, Width), col / Math.PI * Height);
    }

    // ---- Frames -------------------------------------------------------------------------------

    /// <summary>
    /// The sphere's surface frame over a map point as a rotation: it takes the surface's axes
    /// (east +X, up +Y, south +Z) to the sphere's east, up and south there.
    /// </summary>
    public Quaternion Frame(double x, double z)
    {
        double lon = Longitude(x), col = Colatitude(z);
        double sl = Math.Sin(lon), cl = Math.Cos(lon), sc = Math.Sin(col), cc = Math.Cos(col);
        var east = new Vector3((float)cl, 0f, (float)-sl);
        var up = new Vector3((float)(sc * sl), (float)cc, (float)(sc * cl));
        var south = new Vector3((float)(cc * sl), (float)-sc, (float)(cc * cl));
        // Columns east, up, south (System.Numerics matrices are row-vector: rows are the images of the axes).
        var m = new Matrix4x4(
            east.X, east.Y, east.Z, 0,
            up.X, up.Y, up.Z, 0,
            south.X, south.Y, south.Z, 0,
            0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    // ---- Both ways ----------------------------------------------------------------------------

    /// <summary>A pose in the orbit zone (globe centre at the origin) from a map place and a surface-frame orientation.</summary>
    public (Vector3 position, Quaternion rotation) ToOrbit(MapPlace place, Quaternion surfaceRotation)
    {
        Vector3 dir = Direction(place.X, place.Z);
        Quaternion frame = Frame(place.X, place.Z);
        return (dir * (float)(Radius + place.Altitude), Quaternion.Normalize(frame * surfaceRotation));
    }

    /// <summary>
    /// The map place under an orbit position, and the orientation in the surface frame there. At a
    /// pole the longitude is chosen so the vessel's heading is kept (its nose points along it).
    /// </summary>
    public (MapPlace place, Quaternion rotation) ToSurface(Vector3 position, Quaternion orbitRotation)
    {
        double r = position.Length();
        Vector3 dir = r > 1e-6 ? position / (float)r : Vector3.UnitY;
        Vector3 nose = Vector3.Transform(-Vector3.UnitZ, orbitRotation);
        // At a pole: the longitude whose "south" the nose points down (any longitude is right; this one keeps it tidy).
        double poleLon = Math.Atan2(nose.X, nose.Z) + (dir.Y < 0 ? Math.PI : 0);
        var (x, z) = MapPoint(dir, poleLon);
        Quaternion frame = Frame(x, z);
        Quaternion local = Quaternion.Normalize(Quaternion.Conjugate(frame) * orbitRotation);
        return (new MapPlace(x, z, r - Radius), local);
    }

    /// <summary>A surface velocity (east, up, south) as an orbit velocity over a map point.</summary>
    public Vector3 VelocityToOrbit(double x, double z, Vector3 surfaceVelocity) => Vector3.Transform(surfaceVelocity, Frame(x, z));

    /// <summary>An orbit velocity as a surface velocity over a map point.</summary>
    public Vector3 VelocityToSurface(double x, double z, Vector3 orbitVelocity) => Vector3.Transform(orbitVelocity, Quaternion.Conjugate(Frame(x, z)));

    /// <summary>The heading of a rotation in the surface frame, degrees clockwise from north (0 north, 90 east).</summary>
    public static double HeadingDegrees(Quaternion surfaceRotation)
    {
        Vector3 nose = Vector3.Transform(-Vector3.UnitZ, surfaceRotation);
        double h = Math.Atan2(nose.X, -nose.Z) * 180.0 / Math.PI;
        return h < 0 ? h + 360 : h;
    }

    /// <summary>Great-circle distance between two map places on the globe, metres at the globe's radius.</summary>
    public double Distance(double x0, double z0, double x1, double z1)
    {
        double dot = Math.Clamp(Vector3.Dot(Direction(x0, z0), Direction(x1, z1)), -1f, 1f);
        return Math.Acos(dot) * Radius;
    }

    static double Wrap(double v, double span)
    {
        double r = v % span;
        return r < 0 ? r + span : r;
    }
}
