using Godot;
using TD = TerrainDigging.Terrain;

namespace Wildlife;

/// <summary>
/// The voxel terrain as a space creatures live inside: terrain-digging's <see cref="TD.ChunkStore"/>
/// (and so a planet's <c>PlanetTerrain.Store</c>), dug tunnels and generator caves alike. Its
/// density is the store's own trilinear density, the same field the Surface Nets mesh is drawn
/// from, so a crawler holds exactly the rock the player sees, and a hole dug under one drops it.
///
/// The store works in world cells; the sim may work in a frame of its own (a wildlife patch
/// round the player): <see cref="Offset"/> is where the sim's origin sits in the store's frame,
/// whole metres so cells line up.
/// </summary>
public sealed class VoxelSpace : ISolidSpace
{
    public readonly TD.ChunkStore Store;
    public readonly Vector3I Offset;

    public VoxelSpace(TD.ChunkStore store, Vector3I offset = default)
    {
        Store = store;
        Offset = offset;
    }

    public float Density(Vector3 p) =>
        Store.SampleDensity(new System.Numerics.Vector3(p.X + Offset.X, p.Y + Offset.Y, p.Z + Offset.Z));

    public bool SolidCell(int x, int y, int z) => Store.IsSolid(new TD.Int3(x + Offset.X, y + Offset.Y, z + Offset.Z));
}
