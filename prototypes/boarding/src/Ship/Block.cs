using System.Collections.Generic;
using Godot;

namespace Boarding;

public enum BlockKind
{
    Wall, Hull, WeakHull, Armour, DuctCap,
    Door, SecurityDoor, Bulkhead, Airlock,
    Core, Station, Turret, Generator, Loot, Cover,
}

/// <summary>What a bullet hits: picks the spark colour, decal and sound of an impact.</summary>
public enum Surface { Metal, Armour, Machine, Core, Rock, Ichor }

/// <summary>
/// One block of a ship, with its own hit points. Each block is its own static body, so a
/// hit always knows exactly which block it struck.
///
/// Damage shows as it happens: a block that has lost hit points splits into pieces (2x2x2
/// for a wall), and each time it loses another share of its health the piece nearest the
/// hit breaks away as tumbling chunks, taking its collision with it. Shoot one spot long
/// enough and you make a real hole; empty it and the whole block is gone and its cell opens.
/// </summary>
public partial class Block : StaticBody3D
{
    public Ship Ship;
    public BlockKind Kind;
    public Vector2I Cell;
    public Vector3 Size = Vector3.One;
    public Material BaseMaterial;
    public Color ChunkColor = Palette.Bone;
    public Surface Surface = Surface.Metal;
    public float MaxHp = 100f, Hp = 100f;
    public bool Indestructible;
    /// <summary>How the block splits up once damaged. (1,1,1) never splits.</summary>
    public Vector3I Pieces = Vector3I.One;

    public bool Destroyed { get; private set; }
    public bool IsComponent => Kind is BlockKind.Turret or BlockKind.Generator;
    public float Health01 => MaxHp > 0f ? Hp / MaxHp : 1f;

    MeshInstance3D _whole;
    CollisionShape3D _wholeShape;
    readonly List<Node3D> _decor = new();
    List<Piece> _pieces;
    int _broken;
    int _scorch;

    sealed class Piece
    {
        public MeshInstance3D Mesh;
        public CollisionShape3D Shape;
        public Vector3 Center, Size;
        public bool Broken;
        public readonly List<Node3D> Decor = new();
    }

    public override void _Ready()
    {
        CollisionLayer = Layers.Ship;
        CollisionMask = 0;
        BuildVisual();
        BuildShape();
    }

    /// <summary>The default look: one box in the block's material. Subclasses draw their own.</summary>
    /// <summary>Builds a custom look (machines, consoles) instead of the plain box.</summary>
    public System.Action<Block> Dress;

    protected virtual void BuildVisual()
    {
        if (Dress != null)
        {
            Dress(this);
            return;
        }
        if (BaseMaterial == null) return;
        _whole = Art.Mesh(Art.Box(Vector3.Zero, Size), BaseMaterial, Vector3.Zero);
        AddChild(_whole);
    }

    protected virtual void BuildShape()
    {
        _wholeShape = new CollisionShape3D { Shape = new BoxShape3D { Size = Size } };
        AddChild(_wholeShape);
    }

    /// <summary>Paint, stripes and fittings that belong to this block and go when it goes.</summary>
    public void AddDecor(Node3D decor)
    {
        AddChild(decor);
        _decor.Add(decor);
    }

    /// <summary>Applies damage at a world-space point. Returns true if this destroyed the block.</summary>
    public virtual bool Damage(float amount, Vector3 point, Vector3 normal)
    {
        if (Destroyed || amount <= 0f || Indestructible) return false;
        float dealt = Mathf.Min(amount, Hp);
        Hp -= dealt;
        OnDamaged(dealt, point, normal);
        Ship?.NotifyBlockDamaged(this, dealt);
        if (Hp <= 0f)
        {
            Break();
            return true;
        }
        BreakPiecesFor(ToLocal(point));
        UpdateScorch();
        return false;
    }

    protected virtual void OnDamaged(float amount, Vector3 point, Vector3 normal) { }

    /// <summary>Destroys the block: everything left flies off as chunks and its cell opens up.</summary>
    public virtual void Break()
    {
        if (Destroyed) return;
        Destroyed = true;
        Hp = 0f;
        var effects = Ship?.Game?.Effects;
        if (_pieces == null) effects?.Chunks(GlobalTransform * Vector3.Zero, Size, ChunkColor, 10);
        else
            foreach (Piece piece in _pieces)
                if (!piece.Broken) effects?.Chunks(ToGlobal(piece.Center), piece.Size, ChunkColor, 3);
        Ship?.NotifyBlockDestroyed(this);
        QueueFree();
    }

    /// <summary>Taken off whole (salvaged): no chunks, it just leaves the ship.</summary>
    public void Detach()
    {
        if (Destroyed) return;
        Destroyed = true;
        Ship?.NotifyBlockDestroyed(this);
        QueueFree();
    }

    void BreakPiecesFor(Vector3 localHit)
    {
        int total = Pieces.X * Pieces.Y * Pieces.Z;
        if (total <= 1) return;
        int target = Mathf.Min(total - 1, Mathf.FloorToInt((1f - Health01) * total));
        if (target <= _broken) return;
        if (_pieces == null) Subdivide();
        while (_broken < target) BreakNearest(localHit);
    }

    void Subdivide()
    {
        _pieces = new List<Piece>();
        Vector3 pieceSize = Size / new Vector3(Pieces.X, Pieces.Y, Pieces.Z);
        for (int x = 0; x < Pieces.X; x++)
        for (int y = 0; y < Pieces.Y; y++)
        for (int z = 0; z < Pieces.Z; z++)
        {
            Vector3 center = -Size * 0.5f + pieceSize * new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
            var piece = new Piece { Center = center, Size = pieceSize };
            // The piece mesh's vertices are already offset, so banded paint stays aligned.
            piece.Mesh = Art.Mesh(Art.Box(center, pieceSize * 0.995f), BaseMaterial, Vector3.Zero);
            piece.Shape = new CollisionShape3D { Shape = new BoxShape3D { Size = pieceSize }, Position = center };
            AddChild(piece.Mesh);
            AddChild(piece.Shape);
            _pieces.Add(piece);
        }

        // Decor goes with whichever piece it sits on.
        foreach (Node3D decor in _decor)
        {
            Piece owner = _pieces[0];
            float best = float.MaxValue;
            foreach (Piece piece in _pieces)
            {
                float d = (decor.Position - piece.Center).LengthSquared();
                if (d < best) { best = d; owner = piece; }
            }
            owner.Decor.Add(decor);
        }

        _whole?.QueueFree();
        _whole = null;
        _wholeShape?.QueueFree();
        _wholeShape = null;
    }

    void BreakNearest(Vector3 localHit)
    {
        Piece nearest = null;
        float best = float.MaxValue;
        foreach (Piece piece in _pieces)
        {
            if (piece.Broken) continue;
            float d = (piece.Center - localHit).LengthSquared();
            if (d < best) { best = d; nearest = piece; }
        }
        if (nearest == null) return;
        nearest.Broken = true;
        _broken++;
        Ship?.Game?.Effects?.Chunks(ToGlobal(nearest.Center), nearest.Size, ChunkColor, 4);
        nearest.Mesh.QueueFree();
        nearest.Shape.QueueFree();
        foreach (Node3D decor in nearest.Decor) decor.QueueFree();
    }

    /// <summary>Darkens what's left as it takes damage: three scorch levels, shared materials.</summary>
    void UpdateScorch()
    {
        int level = Health01 < 0.3f ? 3 : Health01 < 0.6f ? 2 : Health01 < 0.9f ? 1 : 0;
        if (level == _scorch) return;
        _scorch = level;
        Material overlay = level == 0 ? null : ScorchOverlay(level);
        foreach (Node child in GetChildren())
            if (child is GeometryInstance3D geometry && child is not Light3D) geometry.MaterialOverlay = overlay;
    }

    static readonly Material[] Scorches = new Material[4];

    static Material ScorchOverlay(int level) => Scorches[level] ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.05f, 0.03f, 0.02f, 0.18f * level),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 1f,
    };
}
