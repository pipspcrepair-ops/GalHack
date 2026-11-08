using System.Collections.Generic;
using Godot;
using PlanetSurface.World;
using PoiGenerator;
using PoiGenerator.Gen;
using NV = System.Numerics.Vector3;

namespace PlanetSurface.Infra;

public partial class InfraLayer
{
    /// <summary>The flavour ruins: houses, farmsteads, sheds and wayside stops, spawned and meshed when their tile comes near.</summary>
    public static IEnumerable<Piece> RuinPieces(PlanetWorld world)
    {
        foreach (FlavourRuin ruin in world.Infra.Ruins)
        {
            var (cx, cz) = ruin.Center;
            yield return new RuinPiece { Ruin = ruin, X = cx, Z = cz };
        }
    }

    /// <summary>A ruin's blocks as they are drawn.</summary>
    public static List<ChunkMesh> RuinMesh(PoiInstance inst)
    {
        BlockGrid g = inst.Blocks;
        // No door leaves: a ruin has no doors to open, and a leaf drawn there would be a panel you walk through.
        MeshOptions options = WalkableMesh.On(g.Min);
        options.Mottle = 0.2f;
        return Mesher.Build(g, inst.Layout.Occupied, options);
    }

    /// <summary>A flavour ruin: its century of decay spawned from its seed on a worker, meshed by the POI generator's mesher like any place.</summary>
    sealed class RuinPiece : Piece
    {
        public FlavourRuin Ruin;

        public override void Emit(TileBuilder b)
        {
            PoiInstance inst = Ruin.Spawn(b.World.Profile, b.World.Source);
            Growth.Earthen(inst, b.World.RubbleRock, b.World.Weed);
            List<ChunkMesh> meshes = RuinMesh(inst);
            NV at = b.Local(Ruin.OriginX, Ruin.OriginY, Ruin.OriginZ);
            string name = Ruin.Family.Id;
            b.Blocks(name, new Vector3(at.X, at.Y, at.Z), meshes, () => Materials.BySurface);
            // What its world painted and hung on its front: marks by the door, a shop's awning and sign.
            b.DressHouse(Ruin, inst, at);
        }
    }

    static BlockMaterials _materials;
    /// <summary>The block materials ruins are drawn with (dim: nobody has kept the lights on).</summary>
    static BlockMaterials Materials => _materials ??= new BlockMaterials(1.6f);
}
