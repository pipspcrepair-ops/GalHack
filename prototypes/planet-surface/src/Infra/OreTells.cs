using System;
using System.Collections.Generic;
using System.Numerics;
using GalHack.Deco;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.World;
using GTerrain = GalHack.Terrain;

namespace PlanetSurface.Infra;

public partial class InfraLayer
{
    /// <summary>Whether a deposit's surface tell is solid. The slice turns this off: its rocks would stop the tool's ray short of the ore under them.</summary>
    public static bool OreTellsCollide = true;

    /// <summary>
    /// The ground's tells of the ore under it (design doc "Deposit shapes"): every deposit of the
    /// planet shows itself in the rock over it, the way a prospector reads ground. A deposit that
    /// breaks the surface stands as an outcrop (slabs, a bedrock knuckle or a boulder cluster with
    /// the ore's colour in its veins), a shallow one as scree, a deep one as a fracture. Their veins
    /// are matte, never neon: the ore reads by its rock and texture first, its colour second. None
    /// on a place's pad, a road or in water.
    /// </summary>
    public static IEnumerable<Piece> OrePieces(PlanetWorld world)
    {
        var source = world.Source;
        foreach (Deposit d in world.Map.Deposits.InRect(0, 0, world.Width, world.Height))
        {
            double x = Mathx.WrapX(d.X, world.Width), z = Mathx.Wrap(d.Y, world.Height);
            if (source.StampWeight(x, z) > 0f || source.WaterSurfaceAt(x, z, out _)) continue;
            yield return new OreTellPiece { Deposit = d, X = x, Z = z };
        }
    }

    /// <summary>The kind of tell a deposit shows, by how deep it lies.</summary>
    public static DecoTellKind TellOf(Deposit d) => d.Depth <= 0.01f ? DecoTellKind.Outcrop : d.Depth < 3f ? DecoTellKind.Scree : DecoTellKind.Fracture;

    sealed class OreTellPiece : Piece
    {
        public Deposit Deposit;

        public override void Emit(TileBuilder b)
        {
            Deposit d = Deposit;
            Rgb ore = DepositLook.Color(d.Kind), rock = b.World.Palette.Rock;
            bool glints = d.Kind is DepositKind.Iron or DepositKind.Copper or DepositKind.Titanium or DepositKind.Cobalt or DepositKind.Crystal or DepositKind.Silicon;
            ulong seed = Hash.Of((ulong)(long)(d.X * 10), (long)(d.Y * 10), (long)d.Kind);
            float radius = GTerrain.PlanetTerrainSource.OreRadius(d) * (TellOf(d) == DecoTellKind.Fracture ? 0.7f : 1f);
            DecoMeshData mesh = DecoOreTells.Build(TellOf(d), new DecoRgb(ore.R, ore.G, ore.B), DepositLook.Glow(d.Kind) * 0.3f, glints, seed, radius, new DecoRgb(rock.R, rock.G, rock.B));
            float ground = b.Ground(d.X, d.Y);
            Vector3 at = b.Local(d.X, ground - 0.05f, d.Y);
            Matrix4x4 place = Matrix4x4.CreateRotationY((seed % 360) * MathF.PI / 180f) * Matrix4x4.CreateTranslation(at);
            // A rock's edge can reach a place's pad, where nothing solid may stand in a way in: there it stays scenery.
            bool clear = true;
            for (int a = 0; a < 8 && clear; a++)
                clear = b.World.Source.StampWeight(Mathx.WrapX(d.X + MathF.Cos(a * MathF.PI / 4f) * (radius + 2f), b.World.Width), Mathx.Wrap(d.Y + MathF.Sin(a * MathF.PI / 4f) * (radius + 2f), b.World.Height)) <= 0f;
            b.Merge(mesh, place, collide: OreTellsCollide && clear);
        }
    }
}

public sealed partial class TileBuilder
{
    /// <summary>Copies a deco mesh into the tile under a transform; its collision boxes become collision.</summary>
    public void Merge(DecoMeshData data, Matrix4x4 place, bool collide)
    {
        for (int s = 0; s < data.Surfaces.Length; s++)
        {
            DecoSurfaceData src = data.Surfaces[s], dst = Mesh.Surfaces[s];
            for (int i = 0; i < src.Positions.Count; i++)
            {
                dst.Positions.Add(Vector3.Transform(src.Positions[i], place));
                dst.Normals.Add(Vector3.Normalize(Vector3.TransformNormal(src.Normals[i], place)));
                dst.Colors.Add(src.Colors[i]);
            }
        }
        Mesh.Min = Vector3.Min(Mesh.Min, Vector3.Transform(data.Min, place));
        Mesh.Max = Vector3.Max(Mesh.Max, Vector3.Transform(data.Max, place));
        if (!collide) return;
        foreach (DecoBox box in data.Boxes)
        {
            Vector3 c = Vector3.Transform(box.Centre, place);
            Vector3 ax = Vector3.TransformNormal(box.X * box.Half.X, place), ay = Vector3.TransformNormal(box.Y * box.Half.Y, place), az = Vector3.TransformNormal(box.Z * box.Half.Z, place);
            CollisionBox(c, ax, ay, az);
        }
    }
}
