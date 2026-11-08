namespace GalHack.Terrain;

/// <summary>What a patch of made ground is laid in: the terrain shader draws each its own way.</summary>
public enum MadeCover : byte
{
    /// <summary>Poured concrete: pale, in slabs.</summary>
    Concrete = 1,
    /// <summary>Asphalt: dark, crazing and holed as it ages.</summary>
    Tarmac = 2,
    /// <summary>Loose stone: grey-brown, rutted.</summary>
    Gravel = 3,
    /// <summary>The land's own ground trodden hard.</summary>
    Dirt = 4,
    /// <summary>Paving flags: small square slabs, a footpath's or a forecourt's.</summary>
    Flags = 5,
    /// <summary>Kept grass: nothing laid, and nothing under it painted (a verge, a lawn over a paved block).</summary>
    Lawn = 6,
}

/// <summary>What is marked out on a patch.</summary>
public enum GroundPattern : byte
{
    Plain = 0,
    /// <summary>Joints every <see cref="GroundPatch.Slab"/> metres along both of its axes.</summary>
    Slabs = 1,
    /// <summary>A car park: rows of bays 2.5 m wide and 5 m deep along its long axis, either side of a 6 m aisle.</summary>
    Bays = 2,
    /// <summary>A loading apron: hazard stripes along its far long edge (+V), where the doors are.</summary>
    Loading = 3,
}

/// <summary>
/// A patch of made ground the terrain shader paints from its exact outline: a yard, an apron round a
/// building, a car park, a forecourt, a drive or a path. A rounded rectangle on the ground, square to its own
/// axis (U along it, V across), its corners rounded by <see cref="Round"/> (as much as its half width makes it
/// a capsule: a path). Patches overlap; the highest <see cref="Layer"/> wins where they do. Planned from the
/// places' stamps and layouts with the world (PlanetSurface.Infra.MadeGrounds), held by the terrain source.
/// World metres, canonical centre.
/// </summary>
public sealed class GroundPatch
{
    public double X, Z;
    /// <summary>The way its U axis runs (unit).</summary>
    public float UX = 1f, UZ;
    public float HalfU, HalfV, Round;
    /// <summary>Its ground's height: ground well off it (a pit dug in it, a wall) isn't painted.</summary>
    public float Y;
    public MadeCover Cover;
    public GroundPattern Pattern;
    /// <summary>0 kept up, 1 a century of weather: cracks, broken slabs and weeds in the joints, the land creeping in.</summary>
    public float Wear;
    public int Layer;
    /// <summary>Edged with a kerb (a crisp line), or laid straight onto the land (a ragged, crumbling edge).</summary>
    public bool Kerb;
    /// <summary>Slabs and flags: their size, metres.</summary>
    public float Slab = 5f;
    /// <summary>How far off <see cref="Y"/> the ground may lie and still be painted, at least (a footpath beside a street's banked deck); 0 leaves it to the renderer.</summary>
    public float Give;

    /// <summary>An axis-aligned patch over a box of world metres.</summary>
    public static GroundPatch Box(double x0, double z0, double x1, double z1, float y, MadeCover cover, GroundPattern pattern, float wear, int layer, float round = 1f)
    {
        bool alongX = x1 - x0 >= z1 - z0;
        return new GroundPatch
        {
            X = (x0 + x1) * 0.5, Z = (z0 + z1) * 0.5, UX = alongX ? 1f : 0f, UZ = alongX ? 0f : 1f,
            HalfU = (float)(alongX ? x1 - x0 : z1 - z0) * 0.5f, HalfV = (float)(alongX ? z1 - z0 : x1 - x0) * 0.5f,
            Round = round, Y = y, Cover = cover, Pattern = pattern, Wear = wear, Layer = layer,
        };
    }

    /// <summary>A strip from one point to another, its ends rounded: a path or a drive.</summary>
    public static GroundPatch Strip(double ax, double az, double bx, double bz, float halfWidth, float y, MadeCover cover, float wear, int layer)
    {
        double dx = bx - ax, dz = bz - az, len = System.Math.Sqrt(dx * dx + dz * dz);
        float ux = len > 1e-6 ? (float)(dx / len) : 1f, uz = len > 1e-6 ? (float)(dz / len) : 0f;
        return new GroundPatch
        {
            X = (ax + bx) * 0.5, Z = (az + bz) * 0.5, UX = ux, UZ = uz, HalfU = (float)len * 0.5f + halfWidth, HalfV = halfWidth,
            Round = halfWidth, Y = y, Cover = cover, Pattern = GroundPattern.Plain, Wear = wear, Layer = layer, Slab = 0.9f,
        };
    }
}
