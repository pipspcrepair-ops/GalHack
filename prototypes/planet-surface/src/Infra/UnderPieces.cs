using System;
using System.Collections.Generic;
using Godot;
using PlanetSurface.World;
using PoiGenerator;
using PoiGenerator.Gen;
using Material = Godot.Material;
using NV = System.Numerics.Vector3;
using PInt3 = PoiGenerator.Gen.Int3;

namespace PlanetSurface.Infra;

public partial class InfraLayer
{
    /// <summary>What runs under a town: a piece for each patch of it, drawn with the tiles over it, and the dark it stands in.</summary>
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

        public override void Emit(TileBuilder b)
        {
            UnderBuilt built = Under.Built;
            int shift = Under.Top + 3;
            PInt3 lo = new(TileX * UnderTown.MeshTile - 1, Under.Bottom - shift - 1, TileZ * UnderTown.MeshTile - 1);
            PInt3 size = new(UnderTown.MeshTile + 2, Under.Top - Under.Bottom + 3, UnderTown.MeshTile + 2);
            var grid = new BlockGrid(lo, size);
            var mask = new CellMask(lo, size);
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
            // Underground it's dark, whatever the sky is doing; what the daylight reaches (a culvert's
            // headwall, the head of the stairs) is drawn like anything else out in the open.
            bool Outdoors(PInt3 c)
            {
                PInt3 world = new(c.X, c.Y + shift, c.Z);
                foreach (Dir d in Dirs.All)
                {
                    PInt3 n = world + d.Vec();
                    if (built.Daylight.Contains(n)) return true;
                }
                return false;
            }
            MeshOptions Options(Func<PInt3, bool> only)
            {
                MeshOptions o = MeshOptions.OnPlanet(lo);
                o.Only = only;
                return o;
            }
            List<ChunkMesh> dark = Mesher.Build(grid, mask, Options(c => !Outdoors(c)));
            List<ChunkMesh> lit = Mesher.Build(grid, mask, Options(Outdoors));
            // The node puts the mesh's world cells in the tile's frame, the short way round the seams.
            NV at = b.Local(lo.X, shift, lo.Z) - new NV(lo.X, 0, lo.Z);
            string name = $"Under{TileX}_{TileZ}";
            b.Extra.Add(() =>
            {
                var node = new Node3D { Name = name, Position = new Vector3(at.X, at.Y, at.Z) };
                var body = new StaticBody3D { Name = "Collision", CollisionLayer = PlanetSurface.Poi.PoiLayer.PlaceLayer, CollisionMask = 0 };
                node.AddChild(body);
                void Add(List<ChunkMesh> meshes, Material[] materials)
                {
                    foreach (ChunkMesh chunk in meshes)
                    {
                        node.AddChild(new MeshInstance3D { Mesh = MeshBuild.ToMesh(chunk, materials) });
                        if (chunk.Collision.Count > 0) body.AddChild(new CollisionShape3D { Shape = MeshBuild.ToShape(chunk) });
                    }
                }
                Add(dark, DarkMaterials);
                Add(lit, Materials.BySurface);
                return node;
            });
        }
    }

    const string DarkShader = @"
shader_type spatial;
render_mode ambient_light_disabled, fog_disabled, cull_back;
uniform float rough = 0.85;
uniform float wet = 0.0;
void fragment() {
    vec3 c = COLOR.rgb;
    ALBEDO = c * c;
    ROUGHNESS = mix(rough, 0.12, wet);
    SPECULAR = mix(0.3, 0.7, wet);
    EMISSION = ALBEDO * 0.01;
}
void light() {
    if (!LIGHT_IS_DIRECTIONAL) {
        DIFFUSE_LIGHT += clamp(dot(NORMAL, LIGHT), 0.0, 1.0) * ATTENUATION * LIGHT_COLOR / 3.14159;
        vec3 h = normalize(VIEW + LIGHT);
        SPECULAR_LIGHT += pow(clamp(dot(NORMAL, h), 0.0, 1.0), mix(8.0, 90.0, wet)) * ATTENUATION * LIGHT_COLOR * (0.04 + 0.5 * wet) / 3.14159;
    }
}";

    static Material[] _dark;
    /// <summary>
    /// Underground blocks answer only to lamps and torches: no sky and no sun reach down here (shadows
    /// from the terrain wouldn't reach far enough to stop them), so with your torch off it's black.
    /// </summary>
    static Material[] DarkMaterials
    {
        get
        {
            if (_dark != null) return _dark;
            var shader = new Shader { Code = DarkShader };
            ShaderMaterial Make(float rough, float wet)
            {
                var m = new ShaderMaterial { Shader = shader };
                m.SetShaderParameter("rough", rough);
                m.SetShaderParameter("wet", wet);
                return m;
            }
            Material matte = Make(0.9f, 0f), metal = Make(0.5f, 0f), water = Make(0.3f, 1f);
            return _dark = new Material[] { matte, metal, Materials.BySurface[2], water, matte };
        }
    }
}
