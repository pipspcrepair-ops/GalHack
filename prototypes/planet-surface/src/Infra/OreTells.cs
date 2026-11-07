using System;
using System.Collections.Generic;
using System.Numerics;
using GalHack.Deco;
using TerrainDigging.Terrain;
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

    /// <summary>A tell's id among <see cref="PlanetWorld.Felled"/> (negative, so it never meets a tree's): broken tells stay gone, as felled trees do.</summary>
    public static long TellId(Deposit d) => long.MinValue | (long)(Hash.Of((ulong)(long)(d.X * 10), (long)(d.Y * 10), (long)d.Kind) >> 2);

    /// <summary>
    /// A tell rock is broken: its rock and its collider go together. The tile's tells are one mesh and one
    /// body, so they are built again without it, on a worker, and swapped in the moment that's done.
    /// </summary>
    public void BreakTell(Deposit d)
    {
        if (_world == null || !_world.Felled.Add(TellId(d))) return;
        long key = Key((int)(Mathx.WrapX(d.X, _world.Width) / TileSize), (int)(Mathx.Wrap(d.Y, _world.Height) / TileSize));
        if (_tiles.TryGetValue(key, out Tile tile)) tile.TellsStale = true;
    }

    /// <summary>The ground under a tell was dug away: nothing holds it up, so it goes too.</summary>
    public void GroundDug(IReadOnlyList<RemovedCell> cells)
    {
        if (_world == null || cells.Count == 0) return;
        double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
        foreach (RemovedCell c in cells)
        {
            x0 = Math.Min(x0, c.Cell.X);
            x1 = Math.Max(x1, c.Cell.X + 1);
            z0 = Math.Min(z0, c.Cell.Z);
            z1 = Math.Max(z1, c.Cell.Z + 1);
        }
        const double Reach = 4;
        double wx = Mathx.WrapX(x0 - Reach, _world.Width), wz = Mathx.Wrap(z0 - Reach, _world.Height);
        foreach (Deposit d in _world.Map.Deposits.InRect(wx, wz, wx + x1 - x0 + 2 * Reach, wz + z1 - z0 + 2 * Reach))
        {
            if (_world.Felled.Contains(TellId(d))) continue;
            float radius = GTerrain.PlanetTerrainSource.OreRadius(d) * 0.6f, ground = _world.GroundHeight(d.X, d.Y);
            foreach (RemovedCell c in cells)
            {
                double dx = Mathx.WrapDelta(c.Cell.X + 0.5 - d.X, _world.Width), dz = Mathx.WrapDelta(c.Cell.Z + 0.5 - d.Y, _world.Height);
                if (dx * dx + dz * dz > radius * radius || c.Cell.Y < ground - 1.5f) continue;
                BreakTell(d);
                break;
            }
        }
    }

    /// <summary>The tile's tells as they stand, on their own builder (nothing if none are left).</summary>
    TileBuilder TellsOf(Tile tile, List<Piece> pieces)
    {
        var b = new TileBuilder(_world, tile.Ix * TileSize, tile.Iz * TileSize);
        foreach (Piece p in pieces)
            if (p is OreTellPiece t) t.Rock(b);
        return b;
    }

    /// <summary>The kind of tell a deposit shows, by how deep it lies.</summary>
    public static DecoTellKind TellOf(Deposit d) => d.Depth <= 0.01f ? DecoTellKind.Outcrop : d.Depth < 3f ? DecoTellKind.Scree : DecoTellKind.Fracture;

    sealed class OreTellPiece : Piece
    {
        public Deposit Deposit;

        public override void Emit(TileBuilder b) => Rock(b.Tells ??= new TileBuilder(b.World, b.OriginX, b.OriginZ));

        public void Rock(TileBuilder b)
        {
            Deposit d = Deposit;
            if (b.World.Felled.Contains(TellId(d))) return;
            Rgb ore = DepositLook.Color(d.Kind), rock = b.World.Palette.Rock;
            bool glints = d.Kind is DepositKind.Iron or DepositKind.Copper or DepositKind.Magnesium or DepositKind.Cobalt or DepositKind.Neodymium or DepositKind.Gold or DepositKind.Gravium or DepositKind.Crystal or DepositKind.Silicon;
            ulong seed = Hash.Of((ulong)(long)(d.X * 10), (long)(d.Y * 10), (long)d.Kind);
            float radius = GTerrain.PlanetTerrainSource.OreRadius(d) * (TellOf(d) == DecoTellKind.Fracture ? 0.7f : 1f);
            DecoMeshData mesh = DecoOreTells.Build(TellOf(d), new DecoRgb(ore.R, ore.G, ore.B), DepositLook.Glow(d.Kind) * 0.3f, glints, seed, radius, new DecoRgb(rock.R, rock.G, rock.B));
            // Laid along the slope and sunk in by whatever the ground still falls away under its rim.
            Matrix4x4 place = Matrix4x4.CreateRotationY((seed % 360) * MathF.PI / 180f) * b.Seat(d.X, d.Y, radius);
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
    /// <summary>
    /// The transform that seats something <paramref name="radius"/> round on the ground at a point: tilted
    /// with the slope (<paramref name="steepest"/> rise per metre at most), and down by the most the ground
    /// falls short of its base anywhere round its rim, so no edge hangs over the ground.
    /// </summary>
    public Matrix4x4 Seat(double x, double z, float radius, float steepest = 0.6f)
    {
        Vector3 Up(double px, double pz, out float y, out float sx, out float sz)
        {
            y = Ground(px, pz);
            sx = (Ground(px + radius, pz) - Ground(px - radius, pz)) / (2f * radius);
            sz = (Ground(px, pz + radius) - Ground(px, pz - radius)) / (2f * radius);
            float slope = MathF.Sqrt(sx * sx + sz * sz);
            if (slope > steepest)
            {
                sx *= steepest / slope;
                sz *= steepest / slope;
            }
            return Vector3.Normalize(new Vector3(-sx, 1f, -sz));
        }
        Vector3 n = Up(x, z, out float y, out float gx, out float gz);
        float low = 0f;
        for (int k = 0; k < 24; k++)
        {
            // The rim and a little past it: the slope carries a tilted rim out over the ground beyond it.
            float a = k * MathF.PI / 12f, r = radius * (k % 2 == 0 ? 0.8f : 1.2f), dx = MathF.Cos(a) * r, dz = MathF.Sin(a) * r;
            low = MathF.Min(low, Ground(x + dx, z + dz) - (y + gx * dx + gz * dz));
        }
        Vector3 axis = Vector3.Cross(Vector3.UnitY, n);
        Matrix4x4 tilt = axis.LengthSquared() < 1e-8f ? Matrix4x4.Identity : Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(n.Y, -1f, 1f)));
        Vector3 at = Local(x, y + low - 0.05f, z);
        // The rim as it ends up, tilted: down by whatever of it still stands over the ground.
        float over = 0f;
        for (int k = 0; k < 24; k++)
        {
            float a = k * MathF.PI / 12f;
            Vector3 rim = Vector3.TransformNormal(new Vector3(MathF.Cos(a) * radius, 0f, MathF.Sin(a) * radius), tilt) + at;
            over = MathF.Max(over, rim.Y - Ground(OriginX + rim.X, OriginZ + rim.Z));
        }
        return tilt * Matrix4x4.CreateTranslation(at - Vector3.UnitY * over);
    }

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
