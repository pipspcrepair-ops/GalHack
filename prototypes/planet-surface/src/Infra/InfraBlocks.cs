using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using Planets.Gen;
using PlanetSurface.World;
using NV = System.Numerics.Vector3;
using NQ = System.Numerics.Quaternion;
using SInt3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;

namespace PlanetSurface.Infra;

/// <summary>
/// The network's structures built of the player's own 1 m blocks (owner, 22 Nov: "rotated blocks", "shaped blocks"): a bridge's
/// spans, piers and abutments, a tunnel's mouths, a waterfront's wall and stairs, a railway's track and stations, a maglev's
/// guideway and pylons. Each is one <see cref="Structure"/> of building's (blocks with health, shapes, turns and paint), laid at
/// its route's angle as a turned structure and drawn as one mesh with its colliders in its tile's <see cref="BlocksPart"/>. Built
/// afresh from the plan each time a tile is (the same every time); once something harms one, the game takes it into play as a
/// structure of its own (<see cref="Take"/>: a wreck with no core, damaged, salvaged and split as any other), and the tile draws
/// it no more. A span rests on its supports (<see cref="BlockPiece.Supports"/>): a support that no longer
/// <see cref="BlockPiece.Holds"/> drops what it carries.
/// </summary>
public partial class InfraLayer
{
    /// <summary>The tile part the network's block structures are drawn in.</summary>
    public const string BlocksPart = "Blocks";

    static Catalogue _ownBlocks;

    /// <summary>
    /// The block catalogue they are built from: building's own, unless a game sets its own (the slice's, with its vessel parts:
    /// a structure taken into play is then one of its own).
    /// </summary>
    public static Func<Catalogue> BlockCatalogue = () => _ownBlocks ??=
        Catalogue.LoadDirectory(Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "building", "data")));

    /// <summary>A structure of the network's built of blocks: its template lays them (<see cref="Lay"/>), where and turned as it says.</summary>
    public abstract class BlockPiece : Piece
    {
        public override string Part => BlocksPart;

        /// <summary>Where its cell (0, 0, 0) is centred (a world point, any copy) and how it is turned: its route's way, and its slope.</summary>
        public NV Origin;
        public NQ Turn = NQ.Identity;

        /// <summary>What it rests on (a span its piers or abutments; null for the ground), and what rests on it.</summary>
        public BlockPiece[] Supports = Array.Empty<BlockPiece>();
        public readonly List<BlockPiece> Carries = new();

        /// <summary>The cells it carries with (a pier's cap, an abutment's seat): it holds while any of them stands.</summary>
        public HashSet<SInt3> Cap;

        /// <summary>Taken into play by the game (a structure of its own, the one it holds): its tile draws none of it.</summary>
        public bool Taken;
        public Structure Held;

        /// <summary>Its blocks no longer carry what rests on it (taken into play and broken there).</summary>
        public bool Holds => Held == null || Cap == null || Cap.Count == 0 || Cap.Any(Held.Has);

        /// <summary>Held up by its supports, not the ground: no cell of it is a footing.</summary>
        protected virtual bool Carried => false;

        /// <summary>Lying on the ground (a fallen span): every cell of it is a footing.</summary>
        protected virtual bool Lying => false;

        /// <summary>Lays its blocks: sets <see cref="Origin"/>, <see cref="Turn"/> and <see cref="Cap"/> where its plan didn't.</summary>
        protected abstract void Lay(Laying l);

        /// <summary>
        /// The row whose cell in column (x, z) lies <paramref name="sink"/> metres into the ground under that column: where a
        /// level structure's foot goes, so it stands in the ground wherever the ground falls away under it (nothing floats).
        /// </summary>
        protected int Row(Laying l, int x, int z, float sink)
        {
            NV at = Origin + NV.Transform(new NV(x, 0, z), Turn);
            return (int)MathF.Floor(l.Ground(at.X, at.Z) - sink - at.Y + 0.5f);
        }

        /// <summary>Its structure as built, afresh (the same every time): the cells it stands in the ground by are its footing.</summary>
        public Structure Build(PlanetWorld world)
        {
            var l = new Laying(BlockCatalogue(), world, GetType().Name);
            Lay(l);
            Structure s = l.S;
            if (Carried || s.Count == 0) return s;
            s.Footing = new HashSet<SInt3>();
            foreach (var (c, _) in s.Cells())
            {
                NV at = Origin + NV.Transform(c.ToVector3(), Turn);
                if (Lying || at.Y < l.Ground(at.X, at.Z)) s.Footing.Add(c);
            }
            return s;
        }

        public override void Emit(TileBuilder b)
        {
            if (Taken) return;
            Structure s = Build(b.World);
            if (s.Count > 0) b.Structure(this, s, b.Local(Origin.X, Origin.Y, Origin.Z), Turn);
        }
    }

    /// <summary>A structure's blocks as its template lays them, in its own frame (cell (0, 0, 0) on its origin, -Z its way, +Y up).</summary>
    public sealed class Laying
    {
        public readonly Structure S;
        public readonly PlanetWorld World;
        readonly Catalogue _c;

        public Laying(Catalogue c, PlanetWorld world, string name)
        {
            _c = c;
            World = world;
            S = new Structure(c) { Name = name, Owner = null };
        }

        /// <summary>A block type in a colour (and its grime, 0 to 3).</summary>
        public readonly record struct Stuff(BlockType Type, ushort Paint);

        public Stuff Of(string type, GalHack.Deco.DecoRgb color, int grime = 1) =>
            new(_c.Type(type), S.Palette.Intern(Paint.Solid(Byte(color.R) << 16 | Byte(color.G) << 8 | Byte(color.B)) with { Grime = (byte)Math.Clamp(grime, 0, 3) }));

        static uint Byte(float c) => (uint)Math.Clamp((int)MathF.Round(c * 255f), 0, 255);

        public void Put(int x, int y, int z, Stuff m, string shape = null, int rot = Orientations.Identity)
        {
            Block b = Building.Make(S, m.Type, shape == null ? null : _c.Shape(shape), rot);
            b.Paint = FacePaint.All(m.Paint);
            S.Set(new SInt3(x, y, z), b);
        }

        public void Fill(int x0, int x1, int y0, int y1, int z0, int z1, Stuff m)
        {
            for (int z = Math.Min(z0, z1); z <= Math.Max(z0, z1); z++)
                for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                    for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                        Put(x, y, z, m);
        }

        /// <summary>The ground's height at a world point (stamps applied).</summary>
        public float Ground(double x, double z) => World.Source.SurfaceHeight(Mathx.WrapX(x, World.Width), Mathx.Wrap(z, World.Height));

        /// <summary>A shape's turn: its own +Y to <paramref name="up"/>, its own -Z (its front) to <paramref name="front"/>.</summary>
        public static int Rot(SInt3 up, SInt3 front) => Orientations.Find(new Mat3(SInt3.Cross(up, -front), up, -front));
    }

    /// <summary>A turn with its -Z along <paramref name="forward"/> and its +X level, across it.</summary>
    public static NQ Frame(NV forward)
    {
        forward = NV.Normalize(forward);
        NV right = NV.Normalize(NV.Cross(forward, NV.UnitY)), up = NV.Cross(right, forward);
        return NQ.Normalize(NQ.CreateFromRotationMatrix(new System.Numerics.Matrix4x4(
            right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, -forward.X, -forward.Y, -forward.Z, 0, 0, 0, 0, 1)));
    }

    // ---- in play ------------------------------------------------------------------------------------------

    /// <summary>Every block structure drawn now, by its body: what a hit on a collider struck.</summary>
    static readonly Dictionary<ulong, (BlockPiece Piece, Node3D Node)> Drawn = new();
    static readonly Dictionary<BlockPiece, Node3D> DrawnNodes = new();

    internal static void Drew(StaticBody3D body, BlockPiece piece, Node3D node)
    {
        ulong id = body.GetInstanceId();
        Drawn[id] = (piece, node);
        DrawnNodes[piece] = node;
        body.TreeExiting += () =>
        {
            Drawn.Remove(id);
            if (DrawnNodes.TryGetValue(piece, out Node3D n) && n == node) DrawnNodes.Remove(piece);
        };
    }

    /// <summary>The block structure a collider is part of (and where it stands), or null.</summary>
    public BlockPiece BlockAt(GodotObject collider, out Transform3D at)
    {
        at = default;
        if (collider == null || !Drawn.TryGetValue(collider.GetInstanceId(), out var held) || held.Piece.Taken) return null;
        at = held.Node.GlobalTransform;
        return held.Piece;
    }

    /// <summary>The block structures drawn within a reach of a point, and where each stands.</summary>
    public List<(BlockPiece Piece, Transform3D At)> BlocksNear(Vector3 at, float reach)
    {
        var near = new List<(BlockPiece, Transform3D)>();
        foreach (var (piece, node) in DrawnNodes)
        {
            if (piece.Taken || !IsInstanceValid(node) || !node.IsInsideTree()) continue;
            var box = node.GetChild<MeshInstance3D>(0).GetAabb();
            Transform3D t = node.GlobalTransform;
            Vector3 local = t.AffineInverse() * at;
            if ((local.Clamp(box.Position, box.End) - local).Length() <= reach) near.Add((piece, t));
        }
        return near;
    }

    /// <summary>Every block structure planned on the world.</summary>
    public IEnumerable<BlockPiece> BlockPieces => _pieces?.Values.SelectMany(l => l).OfType<BlockPiece>() ?? Enumerable.Empty<BlockPiece>();

    /// <summary>The box a structure drawn now fills in the scene, or null while it isn't drawn.</summary>
    public Aabb? BoxOf(BlockPiece p) =>
        DrawnNodes.TryGetValue(p, out Node3D node) && IsInstanceValid(node) && node.IsInsideTree() ? node.GlobalTransform * node.GetChild<MeshInstance3D>(0).GetAabb() : null;

    /// <summary>Where a structure stands in the scene: as drawn, or (not drawn) its plan's place on the copy of the planet nearest the camera.</summary>
    public Transform3D Where(BlockPiece p)
    {
        if (DrawnNodes.TryGetValue(p, out Node3D node) && IsInstanceValid(node) && node.IsInsideTree()) return node.GlobalTransform;
        Vector3 camera = GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        Vector3 at = _world.NearestCopy(new Vector3(p.Origin.X, p.Origin.Y, p.Origin.Z), camera);
        return new Transform3D(new Basis(new Quaternion(p.Turn.X, p.Turn.Y, p.Turn.Z, p.Turn.W)), at);
    }

    /// <summary>
    /// The game has taken a structure into play (its blocks held by it now): its tiles draw it no more. Its colliders go at
    /// once; its mesh a moment later, once the game's own drawing of it is up.
    /// </summary>
    public void Take(BlockPiece p, Structure held)
    {
        p.Taken = true;
        p.Held = held;
        if (!DrawnNodes.Remove(p, out Node3D node) || !IsInstanceValid(node)) return;
        if (node.GetNodeOrNull<StaticBody3D>("Collision") is { } body)
        {
            body.CollisionLayer = 0;
            body.QueueFree();
        }
        GetTree().CreateTimer(0.3).Timeout += () => { if (IsInstanceValid(node)) node.QueueFree(); };
    }

    /// <summary>Block structures taken into play so far (tests).</summary>
    public int BlocksTaken => _pieces?.Values.Sum(l => l.Count(p => p is BlockPiece { Taken: true })) ?? 0;
}

public sealed partial class TileBuilder
{
    /// <summary>
    /// A block structure as a node of its own, turned and placed as it stands: one mesh (every chunk of it merged, building's
    /// own block look), and its colliders (the mesher's boxes and slopes' hulls) when something comes near.
    /// </summary>
    public void Structure(InfraLayer.BlockPiece piece, Structure s, NV at, NQ turn)
    {
        var opaque = new MeshBuffers();
        var glass = new MeshBuffers();
        var boxes = new List<(Vector3 Centre, Vector3 Size)>();
        var hulls = new List<Vector3[]>();
        var data = new ChunkMeshData();
        foreach (SInt3 chunk in s.Chunks.Keys)
        {
            ChunkMesher.Build(s, chunk, data);
            opaque.Append(data.Opaque);
            glass.Append(data.Glass);
            boxes.AddRange(data.Boxes);
            hulls.AddRange(data.Hulls);
        }
        StaticBody3D body = null;
        var place = new Transform3D(new Basis(new Quaternion(turn.X, turn.Y, turn.Z, turn.W)), new Vector3(at.X, at.Y, at.Z));
        Extra.Add(() =>
        {
            if (piece.Taken) return null; // taken into play while its tile was being built
            var node = new Node3D { Name = piece.GetType().Name, Transform = place };
            var mesh = new ArrayMesh();
            if (opaque.VertexCount > 0)
            {
                mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, opaque.ToArrays(), null, null, opaque.Format);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, BlockMaterials.Opaque);
            }
            if (glass.VertexCount > 0)
            {
                mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, glass.ToArrays());
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, BlockMaterials.Glass);
            }
            node.AddChild(new MeshInstance3D { Name = "Blocks", Mesh = mesh });
            body = new StaticBody3D { Name = "Collision", CollisionLayer = PlanetSurface.Poi.PoiLayer.PlaceLayer, CollisionMask = 0 };
            node.AddChild(body);
            InfraLayer.Drew(body, piece, node);
            return node;
        });
        Solid.Add(() =>
        {
            if (!GodotObject.IsInstanceValid(body) || !body.IsInsideTree()) return;
            foreach (var (centre, size) in boxes) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = centre });
            foreach (Vector3[] hull in hulls) body.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = hull } });
        });
    }
}
