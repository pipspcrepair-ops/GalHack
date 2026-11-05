using Godot;

namespace DiegeticUi.Screens;

/// <summary>
/// Where to hang a screen so a standing player looks at it, not down at it: its centre at
/// standing eye height above the floor they stand on, facing out at them. And the checks a test
/// uses to hold every screen to that.
/// </summary>
public static class ScreenMount
{
    /// <summary>Standing eye height above the feet, in metres (GalHack's first-person players).</summary>
    public const float EyeHeight = 1.62f;

    /// <summary>
    /// A screen hung on a wall or a machine's face: centre at eye height above
    /// <paramref name="floorY"/>, straight out along <paramref name="outward"/> (kept level), a
    /// hair off the surface so the bezel sits on it. <paramref name="tipBackDegrees"/> tips the top
    /// away, for a screen on a desk below eye height; <paramref name="rise"/> moves it up or down.
    /// </summary>
    public static Transform3D OnFace(Vector3 facePoint, Vector3 outward, float floorY,
        float tipBackDegrees = 0, float rise = 0, float standOff = 0.035f)
    {
        Vector3 n = new Vector3(outward.X, 0, outward.Z).Normalized();
        Basis facing = Basis.LookingAt(-n, Vector3.Up); // the glass faces +Z
        Basis tipped = facing * new Basis(Vector3.Right, -Mathf.DegToRad(tipBackDegrees));
        var centre = new Vector3(facePoint.X, floorY + EyeHeight + rise, facePoint.Z);
        return new Transform3D(tipped, centre + n * standOff);
    }

    /// <summary>How far the screen's centre sits above (+) or below (-) standing eye height over a floor.</summary>
    public static float EyeOffset(WorldScreen screen, float floorY) => screen.GlobalPosition.Y - floorY - EyeHeight;

    /// <summary>
    /// Degrees between where the glass faces and the eyes of a player standing
    /// <paramref name="distance"/> in front of it: 0 is square on.
    /// </summary>
    public static float FacingError(WorldScreen screen, float floorY, float distance = 1.5f)
    {
        Vector3 normal = screen.GlobalBasis.Z.Normalized();
        Vector3 ahead = new Vector3(normal.X, 0, normal.Z).Normalized() * distance;
        Vector3 eye = screen.GlobalPosition + ahead;
        eye.Y = floorY + EyeHeight;
        return Mathf.RadToDeg(normal.AngleTo(eye - screen.GlobalPosition));
    }

    /// <summary>Hung right: within <paramref name="band"/> metres of eye height, and facing a standing player within <paramref name="maxDegrees"/>.</summary>
    public static bool AtEyeHeight(WorldScreen screen, float floorY, float band = 0.1f, float maxDegrees = 15) =>
        Mathf.Abs(EyeOffset(screen, floorY)) <= band && FacingError(screen, floorY) <= maxDegrees;
}
