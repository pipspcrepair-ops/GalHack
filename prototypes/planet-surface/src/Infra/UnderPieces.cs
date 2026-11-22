using System;
using System.Collections.Generic;
using Godot;
using PlanetSurface.World;
using PoiGenerator;
using PoiGenerator.Gen;
using NV = System.Numerics.Vector3;
using PInt3 = PoiGenerator.Gen.Int3;

namespace PlanetSurface.Infra;

public partial class InfraLayer
{
    /// <summary>What runs under a town: a piece for each patch of it, drawn with the tiles over it.</summary>
    public static IEnumerable<Piece> UnderPieces(PlanetWorld world)
    {
        UnderTown under = world.Infra.Town?.Under;
        if (under == null) yield break;
        foreach (var (key, cells) in under.Tiles)
            yield return new UnderPiece
            {
                Under = under, Cells = cells, TileX = key.X, TileZ = key.Z,
                X = (key.X + 0.5) * UnderTown.MeshTile, Z = (key.Z + 0.5) * UnderTown.MeshTile,
            };
    }

    /// <summary>
    /// A patch of the underground: its blocks meshed by the POI generator's mesher, with collision.
    /// The patch's cells sit below the mesher's ground level in its own frame; every face is drawn,
    /// so digging in from the rock round it meets a wall you can see.
    /// </summary>
    sealed class UnderPiece : Piece
    {
        public UnderTown Under;
        public List<PInt3> Cells;
        public int TileX, TileZ;
        /// <summary>A patch is a part of its tile: a block broken, the patch is built again alone (<see cref="WearUnder"/>).</summary>
        public override string Part => UnderPart;

        public override void Emit(TileBuilder b)
        {
            UnderBuilt built = Under.Built;
            int shift = Under.Top + 3;
            PInt3 lo = new(TileX * UnderTown.MeshTile - 1, Under.Bottom - shift - 1, TileZ * UnderTown.MeshTile - 1);
            PInt3 size = new(UnderTown.MeshTile + 2, Under.Top - Under.Bottom + 3, UnderTown.MeshTile + 2);
            var grid = new BlockGrid(lo, size);
            var mask = new CellMask(lo, size);
            lock (built)
            {
                foreach (PInt3 c in Cells) grid.Set(new PInt3(c.X, c.Y - shift, c.Z), built.Blocks[c]);
                // The open cells in the patch and a cell round it: faces are only drawn toward these.
                for (int y = 0; y < size.Y; y++)
                    for (int z = 0; z < size.Z; z++)
                        for (int x = 0; x < size.X; x++)
                        {
                            PInt3 world = new(lo.X + x, lo.Y + y + shift, lo.Z + z);
                            if (built.Open.Contains(world) || built.Daylight.Contains(world) || built.Blocks.ContainsKey(world))
                                mask[new PInt3(lo.X + x, lo.Y + y, lo.Z + z)] = true;
                        }
            }
            // Drawn as a place's blocks are, lit and hazed like anything in the open: how dark it is down here is the
            // sky field's to say (the game's SkyLit), so a block that comes up out of the ground is lit like the ground
            // round it. (A material of its own that took no sun, sky or fog once drew such blocks black in daylight.)
            List<ChunkMesh> meshes = Mesher.Build(grid, mask, MeshOptions.OnPlanet(lo));
            // The node puts the mesh's world cells in the tile's frame, the short way round the seams.
            NV at = b.Local(lo.X, shift, lo.Z) - new NV(lo.X, 0, lo.Z);
            string name = $"Under{TileX}_{TileZ}";
            var place = new Vector3(at.X, at.Y, at.Z);
            b.Blocks(name, place, meshes, () => Materials.BySurface);
            // The water standing in its runs: a surface over the floor, never a block, the still water a place has.
            var wet = new List<PInt3>();
            foreach (PInt3 c in built.Water)
                if (UnderTown.TileOf(c) == (TileX, TileZ)) wet.Add(new PInt3(c.X, c.Y - shift, c.Z));
            if (wet.Count > 0)
                b.Extra.Add(() =>
                {
                    MeshInstance3D water = MeshBuild.Water(wet, MeshBuild.StillWater());
                    water.Name = $"{name} water";
                    water.Position = place;
                    return water;
                });
        }
    }

    /// <summary>The tile part the under-town's patches are drawn in.</summary>
    public const string UnderPart = "Under";

    /// <summary>Under-town lining blocks broken so far (tests).</summary>
    public int UnderBroken { get; private set; }

    /// <summary>
    /// The under-town lining block (its world cell) a hit at a point on a collider struck, or null: its drains, sewers, culverts
    /// and metro break block by block, as a place's blocks do (<see cref="WearUnder"/>).
    /// </summary>
    public PInt3? UnderAt(GodotObject collider, Vector3 point, Vector3 normal)
    {
        UnderTown under = _world?.Infra.Town?.Under;
        if (under == null || collider is not Node body || body.GetParent() is not Node patch || !patch.Name.ToString().StartsWith("Under", StringComparison.Ordinal)) return null;
        return UnderCell(under, point - normal * 0.5f);
    }

    /// <summary>The lining block a point is in, on whichever copy of the planet the under-town's cells are (they may run past a seam), or null.</summary>
    PInt3? UnderCell(UnderTown under, Vector3 p)
    {
        int x = (int)Math.Floor(Planets.Gen.Mathx.WrapX(p.X, _world.Width)), y = (int)Math.Floor(p.Y), z = (int)Math.Floor(Planets.Gen.Mathx.Wrap(p.Z, _world.Height));
        int w = (int)Math.Round(_world.Width), h = (int)Math.Round(_world.Height);
        lock (under.Built)
            foreach (int dx in new[] { 0, -w, w })
            foreach (int dz in new[] { 0, -h, h })
                if (under.Built.Blocks.ContainsKey(new PInt3(x + dx, y, z + dz))) return new PInt3(x + dx, y, z + dz);
        return null;
    }

    /// <summary>Wears an under-town lining block (a shot's, a blast's, the tool's damage); spent, it is gone and the patches round it are built again.</summary>
    public bool WearUnder(PInt3 c, float damage)
    {
        UnderTown under = _world?.Infra.Town?.Under;
        if (under == null || !under.Wear(c, damage)) return false;
        var near = new HashSet<(int, int)>();
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                near.Add(UnderTown.TileOf(new PInt3(c.X + dx, c.Y, c.Z + dz)));
        Restale(UnderPart, p => p is UnderPiece u && near.Contains((u.TileX, u.TileZ)));
        UnderBroken++;
        return true;
    }

    /// <summary>The lining blocks in a blast's reach, worn as a place's are: in full at its middle, a quarter at its edge.</summary>
    public void BlastUnder(Vector3 at, float radius, float damage)
    {
        UnderTown under = _world?.Infra.Town?.Under;
        if (under == null || radius <= 0f) return;
        var hits = new List<(PInt3 Cell, float Damage)>();
        int r = (int)Math.Ceiling(radius);
        for (int y = -r; y <= r; y++)
            for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    float d = new Vector3(x, y, z).Length();
                    if (d < radius && UnderCell(under, at + new Vector3(x, y, z)) is PInt3 c) hits.Add((c, damage * (1f - d / radius * 0.75f)));
                }
        foreach (var (c, d) in hits) WearUnder(c, d);
    }

    static BlockMaterials _materials;
    /// <summary>The block materials the under-town is drawn with (dim: nobody has kept the lights on).</summary>
    static BlockMaterials Materials => _materials ??= new BlockMaterials(1.6f);
}
