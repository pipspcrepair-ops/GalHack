using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Items;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Places;
using Structure = GalHack.Structures.Structure;

namespace Slice.Ui;

/// <summary>
/// Small 3D icons for items: each object's own model (a block's real mesh from building's mesher, a deco
/// piece's from the decorations library, and whatever <see cref="ItemModels"/> has a model for: the
/// tools and the scanner as they are held, the hover bike, the placed devices) rendered once in its
/// own tiny viewport, then kept as a plain texture; only ore, bars, parts, food and ammo, which
/// have no model of their own, get a simple stand-in by kind. A held, driven or placed model is
/// shown at one fixed angle: its front toward the right, a little toward you, from above, filling
/// the icon. Slots draw the texture, so a full bag costs nothing per frame. A headless run, which
/// cannot render, has no icons and the slots keep their text labels.
/// </summary>
public partial class ItemIcons : Node
{
    public const int Size = 96;

    /// <summary>The game's icon cache (set when the game builds).</summary>
    public static ItemIcons Shared;

    readonly Dictionary<string, Texture2D> _done = new(StringComparer.Ordinal);
    readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    readonly Queue<ItemDef> _waiting = new();
    readonly HashSet<string> _queued = new(StringComparer.Ordinal);
    readonly List<(ItemDef item, SubViewport view, int frames)> _active = new();
    Catalogue _blocks;
    ItemCatalogue _items;

    public static bool CanRender => DisplayServer.GetName() != "headless";

    /// <summary>Icons made so far (tests, screenshots).</summary>
    public int Count => _done.Count;

    /// <summary>Where an item's icon model comes from.</summary>
    public enum Source { Block, Piece, Held, Vehicle, Placed, StandIn }

    /// <summary>Where an item's icon model comes from, without rendering it (tests, the icon sheet).</summary>
    public Source SourceOf(ItemDef item)
    {
        if (item.Id.StartsWith("piece:", StringComparison.Ordinal)) return Source.Piece;
        if (item.Id == "deco-block") return Source.StandIn;
        if (item.IsBlock && _blocks != null && _blocks.TryGetType(item.BlockId, out _)) return Source.Block;
        if (ItemModels.Has(item, out ModelSource origin)) return origin switch { ModelSource.Held => Source.Held, ModelSource.Vehicle => Source.Vehicle, _ => Source.Placed };
        return Source.StandIn;
    }

    /// <summary>An icon that has been rendered, or null (tests and the icon sheet).</summary>
    public Texture2D Rendered(string itemId) => itemId != null && _done.TryGetValue(itemId, out Texture2D t) ? t : null;

    /// <summary>Did making this item's icon fail (tests)?</summary>
    public bool Failed(string itemId) => _failed.Contains(itemId);

    public void Setup(ItemCatalogue items, Catalogue blocks)
    {
        _items = items;
        _blocks = blocks;
    }

    /// <summary>The icon of an item once it is rendered, else null (and it is queued to be).</summary>
    public Texture2D Get(ItemDef item)
    {
        if (item == null) return null;
        if (_done.TryGetValue(item.Id, out Texture2D t)) return t;
        if (!CanRender || _failed.Contains(item.Id) || !_queued.Add(item.Id)) return null;
        _waiting.Enqueue(item);
        return null;
    }

    public Texture2D Get(string itemId) => _items != null && itemId != null && _items.TryGet(itemId, out ItemDef item) ? Get(item) : null;

    /// <summary>The icon of a deco piece (its own mesh), once rendered.</summary>
    public Texture2D GetPiece(string pieceId)
    {
        if (_done.TryGetValue("piece:" + pieceId, out Texture2D t)) return t;
        if (!CanRender || _failed.Contains("piece:" + pieceId) || !_queued.Add("piece:" + pieceId)) return null;
        _waiting.Enqueue(new ItemDef { Id = "piece:" + pieceId, Name = pieceId });
        return null;
    }

    public override void _Process(double delta)
    {
        // Harvest viewports that have had their frames.
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var (item, view, frames) = _active[i];
            if (frames < 3) { _active[i] = (item, view, frames + 1); continue; }
            Image img = view.GetTexture()?.GetImage();
            if (img != null && !img.IsEmpty() && !IsBlank(img)) _done[item.Id] = ImageTexture.CreateFromImage(img);
            else _failed.Add(item.Id);
            view.QueueFree();
            _active.RemoveAt(i);
        }
        while (_waiting.Count > 0 && _active.Count < 4)
        {
            ItemDef item = _waiting.Dequeue();
            Node3D model = ModelOf(item, out ModelSource? origin);
            if (model == null) { _failed.Add(item.Id); continue; }
            _active.Add((item, Render(model, origin), 0));
        }
    }

    static bool IsBlank(Image img)
    {
        for (int y = 8; y < img.GetHeight(); y += 16)
            for (int x = 8; x < img.GetWidth(); x += 16)
                if (img.GetPixel(x, y).A > 0.05f) return false;
        return true;
    }

    // ---- Rendering ---------------------------------------------------------------------------

    /// <summary>Where the icon camera sits, looking at the origin: above, to the right and in front of the origin.</summary>
    static readonly Vector3 CameraAt = new(1.55f, 1.25f, -2.2f);

    /// <summary>How wide the longest side of a model should look: 1.3 of the 1.48 the camera sees across the origin.</summary>
    const float Fill = 1.3f;

    /// <summary>
    /// How far a model's front turns from the icon's right toward the viewer, degrees: a gun, a tool or a
    /// bike shows its side with its muzzle or nose a little toward you; a device, whose face is its front,
    /// shows more of that face.
    /// </summary>
    static float TurnToward(ModelSource source) => source == ModelSource.Placed ? 40f : 22f;

    SubViewport Render(Node3D model, ModelSource? source = null)
    {
        var view = new SubViewport
        {
            Size = new Vector2I(Size, Size), TransparentBg = true, OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once, Msaa3D = Viewport.Msaa.Msaa4X,
        };
        Node3D holder = source is ModelSource shown ? FaceRight(model, shown) : Boxed(model);
        view.AddChild(holder);
        view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0, 0, 0, 0),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.78f, 0.8f, 0.86f), AmbientLightEnergy = 0.9f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.15f, ShadowEnabled = false };
        view.AddChild(sun);
        sun.LookAtFromPosition(new Vector3(-2, 3, -3), Vector3.Zero);
        var cam = new Camera3D { Fov = 28, Current = true };
        view.AddChild(cam);
        cam.LookAtFromPosition(CameraAt, Vector3.Zero);
        AddChild(view);
        return view;
    }

    /// <summary>A block or stand-in: fitted into a unit box at the origin, as it comes.</summary>
    static Node3D Boxed(Node3D model)
    {
        Aabb box = BoundsOf(model);
        float longest = Math.Max(0.001f, Math.Max(box.Size.X, Math.Max(box.Size.Y, box.Size.Z)));
        var holder = new Node3D { Scale = Vector3.One * (1.3f / longest) };
        holder.Position = -(box.Position + box.Size * 0.5f) * holder.Scale.X;
        holder.AddChild(model);
        return holder;
    }

    /// <summary>
    /// A held, driven or placed model (front -Z, up +Y): turned so its front points to the right of the
    /// icon and a little toward the viewer, then scaled and centred by how it looks from the camera, so
    /// every one fills the icon the same way whatever its shape.
    /// </summary>
    static Node3D FaceRight(Node3D model, ModelSource source)
    {
        Basis look = Basis.LookingAt(-CameraAt, Vector3.Up);
        Vector3 right = look.X, up = look.Y, back = look.Z;
        Vector3 toward = new Vector3(CameraAt.X, 0f, CameraAt.Z).Normalized();
        float turn = Mathf.DegToRad(TurnToward(source));
        Vector3 front = right * Mathf.Cos(turn) + toward * Mathf.Sin(turn);
        float yaw = Mathf.Atan2(-front.X, -front.Z);
        var spin = new Basis(Vector3.Up, yaw);
        Vector3 lo = new(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
        foreach (Vector3 corner in Corners(model, Transform3D.Identity))
        {
            Vector3 p = spin * corner;
            var seen = new Vector3(p.Dot(right), p.Dot(up), p.Dot(back));
            lo = new Vector3(Math.Min(lo.X, seen.X), Math.Min(lo.Y, seen.Y), Math.Min(lo.Z, seen.Z));
            hi = new Vector3(Math.Max(hi.X, seen.X), Math.Max(hi.Y, seen.Y), Math.Max(hi.Z, seen.Z));
        }
        if (lo.X > hi.X) return Boxed(model);
        float scale = Fill / Math.Max(0.001f, Math.Max(hi.X - lo.X, hi.Y - lo.Y));
        Vector3 mid = (lo + hi) * 0.5f;
        var holder = new Node3D { Rotation = new Vector3(0f, yaw, 0f), Scale = Vector3.One * scale };
        holder.Position = -(right * mid.X + up * mid.Y + back * mid.Z) * scale;
        holder.AddChild(model);
        return holder;
    }

    /// <summary>Every corner of every mesh's bounds in a model, in the model's own frame.</summary>
    static IEnumerable<Vector3> Corners(Node node, Transform3D parent)
    {
        Transform3D t = node is Node3D d ? parent * d.Transform : parent;
        if (node is MeshInstance3D m && m.Mesh != null)
        {
            Aabb a = m.Mesh.GetAabb();
            for (int i = 0; i < 8; i++)
                yield return t * (a.Position + a.Size * new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1));
        }
        foreach (Node c in node.GetChildren())
            foreach (Vector3 v in Corners(c, t)) yield return v;
    }

    static Aabb BoundsOf(Node3D root)
    {
        Aabb? total = null;
        foreach (Node n in Walk(root))
            if (n is MeshInstance3D m && m.Mesh != null)
            {
                Aabb a = m.Transform * m.Mesh.GetAabb();
                total = total == null ? a : total.Value.Merge(a);
            }
        return total ?? new Aabb(Vector3.Zero, Vector3.One);
    }

    static IEnumerable<Node> Walk(Node n)
    {
        yield return n;
        foreach (Node c in n.GetChildren())
            foreach (Node d in Walk(c)) yield return d;
    }

    // ---- Models ------------------------------------------------------------------------------

    /// <summary>The model an item's icon shows; <paramref name="origin"/> is set when it is a held, driven or placed model.</summary>
    Node3D ModelOf(ItemDef item, out ModelSource? origin)
    {
        origin = null;
        try
        {
            if (item.Id.StartsWith("piece:", StringComparison.Ordinal)) return PieceModel(item.Id[6..]);
            if (item.Id == "deco-block") return DecoBlockModel();
            if (item.IsBlock && _blocks != null && _blocks.TryGetType(item.BlockId, out BlockType type)) return BlockModel(item, type);
            // Anything with a real model of its own (the one in the hands, on the road or on the ground) is drawn from that.
            if (ItemModels.Build(item) is ItemModel own)
            {
                origin = own.Source;
                // The icon has its own light: a model's lamps would only wash it out.
                foreach (Node n in Walk(own.Root).Where(n => n is Light3D).ToList())
                {
                    n.GetParent().RemoveChild(n);
                    n.Free();
                }
                return own.Root;
            }
            return Simple(item);
        }
        catch (Exception e)
        {
            GD.PushWarning($"icon for {item.Id}: {e.Message}");
            return null;
        }
    }

    Node3D BlockModel(ItemDef item, BlockType type)
    {
        var s = new Structure(_blocks);
        ShapeDef shape = type.IsArmor ? (_blocks.Shapes.FirstOrDefault(x => x.Name.Contains("Wedge", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Slope", StringComparison.OrdinalIgnoreCase)) ?? _blocks.Shapes[0]) : null;
        Block b = Building.Make(s, type, shape, 0);
        s.Set(Int3.Zero, b);
        var data = new ChunkMeshData();
        var root = new Node3D();
        foreach (Int3 chunk in s.Chunks.Keys)
        {
            ChunkMesher.Build(s, chunk, data);
            root.AddChild(new MeshInstance3D { Mesh = ChunkMesher.ToMesh(data) });
        }
        return root;
    }

    static Node3D PieceModel(string pieceId)
    {
        Decor decor = Decor.Shared;
        if (decor == null || !decor.Catalogue.TryGet(pieceId, out DecoPiece piece)) return null;
        var look = new DecoLook(pieceId, DecoState.Intact, DecoFall.None, true, DecoCover.Vines, piece.Palette);
        var root = new Node3D();
        root.AddChild(new MeshInstance3D { Mesh = decor.Meshes.Mesh(look) });
        return root;
    }

    static Node3D DecoBlockModel()
    {
        var root = new Node3D();
        root.AddChild(Part(new BoxMesh { Size = new Vector3(1, 1, 1) }, new Color("c9b27a"), Vector3.Zero));
        // A lamp, a chair's leg and a plant on the faces say "any piece".
        root.AddChild(Part(new BoxMesh { Size = new Vector3(1.04f, 0.18f, 1.04f) }, new Color("3a3f4a"), new Vector3(0, 0.3f, 0)));
        root.AddChild(Part(new SphereMesh { Radius = 0.2f, Height = 0.4f, RadialSegments = 10, Rings = 5 }, new Color("ffd36a"), new Vector3(0, 0.62f, 0), emissive: true));
        return root;
    }

    static MeshInstance3D Part(Mesh mesh, Color colour, Vector3 at, float roughness = 0.7f, bool emissive = false, Vector3? rotate = null)
    {
        var m = new StandardMaterial3D { AlbedoColor = colour, Roughness = roughness, Metallic = 0.1f };
        if (emissive)
        {
            m.EmissionEnabled = true;
            m.Emission = colour;
            m.EmissionEnergyMultiplier = 1.4f;
        }
        var n = new MeshInstance3D { Mesh = mesh, MaterialOverride = m, Position = at };
        if (rotate is Vector3 r) n.RotationDegrees = r;
        return n;
    }

    /// <summary>A recognisable stand-in for everything without a block or deco model: shape by kind, colour by item.</summary>
    Node3D Simple(ItemDef item)
    {
        Color tone = Tone(item);
        Color dark = tone.Darkened(0.35f), light = tone.Lightened(0.3f);
        var root = new Node3D();
        switch (item.Category)
        {
            case ItemCategory.Raw:
                root.AddChild(Part(new SphereMesh { Radius = 0.5f, Height = 0.8f, RadialSegments = 7, Rings = 4 }, tone, Vector3.Zero, 0.95f));
                root.AddChild(Part(new SphereMesh { Radius = 0.3f, Height = 0.5f, RadialSegments = 6, Rings = 3 }, light, new Vector3(0.38f, -0.12f, 0.2f), 0.95f));
                root.AddChild(Part(new SphereMesh { Radius = 0.22f, Height = 0.36f, RadialSegments = 6, Rings = 3 }, dark, new Vector3(-0.4f, -0.2f, -0.1f), 0.95f));
                break;
            case ItemCategory.Refined:
                root.AddChild(Part(new BoxMesh { Size = new Vector3(1f, 0.28f, 0.45f) }, tone, new Vector3(0, -0.16f, 0), 0.35f));
                root.AddChild(Part(new BoxMesh { Size = new Vector3(1f, 0.28f, 0.45f) }, light, new Vector3(0.1f, 0.16f, 0.05f), 0.35f));
                break;
            case ItemCategory.Part:
            case ItemCategory.Component:
                root.AddChild(Part(new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 0.22f, RadialSegments = 12 }, tone, Vector3.Zero, 0.4f, rotate: new Vector3(90, 0, 0)));
                root.AddChild(Part(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.18f, Height = 0.4f, RadialSegments = 8 }, dark, Vector3.Zero, 0.3f, rotate: new Vector3(90, 0, 0)));
                root.AddChild(Part(new BoxMesh { Size = new Vector3(0.22f, 0.22f, 0.22f) }, light, new Vector3(0.5f, 0.1f, 0), 0.4f));
                break;
            case ItemCategory.Consumable:
                root.AddChild(Part(new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.4f, Height = 0.8f, RadialSegments = 12 }, tone, Vector3.Zero, 0.5f));
                root.AddChild(Part(new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 0.16f, RadialSegments = 12 }, light, new Vector3(0, 0.45f, 0), 0.4f));
                break;
            case ItemCategory.Ammo:
                for (int i = 0; i < 3; i++)
                {
                    root.AddChild(Part(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.7f, RadialSegments = 8 }, light, new Vector3(-0.3f + i * 0.3f, 0, 0), 0.3f));
                    root.AddChild(Part(new CylinderMesh { TopRadius = 0.001f, BottomRadius = 0.12f, Height = 0.22f, RadialSegments = 8 }, tone, new Vector3(-0.3f + i * 0.3f, 0.46f, 0), 0.3f));
                }
                break;
            case ItemCategory.Tool:
                root.AddChild(Part(new BoxMesh { Size = new Vector3(0.18f, 1f, 0.18f) }, dark, Vector3.Zero, 0.6f, rotate: new Vector3(0, 0, 35)));
                root.AddChild(Part(new BoxMesh { Size = new Vector3(0.7f, 0.3f, 0.3f) }, tone, new Vector3(0.28f, 0.34f, 0), 0.4f));
                break;
            default: // unique finds and anything else: a crystal
                root.AddChild(Part(new PrismMesh { Size = new Vector3(0.7f, 0.9f, 0.7f) }, tone, new Vector3(0, 0.45f, 0), 0.15f, emissive: true));
                root.AddChild(Part(new PrismMesh { Size = new Vector3(0.7f, 0.9f, 0.7f) }, tone, new Vector3(0, -0.45f, 0), 0.15f, emissive: true, rotate: new Vector3(180, 0, 0)));
                break;
        }
        return root;
    }

    /// <summary>A stable colour for an item: a hue from its id, brightened by rarity.</summary>
    static Color Tone(ItemDef item)
    {
        uint h = 2166136261;
        foreach (char c in item.Id) h = (h ^ c) * 16777619;
        float hue = (h % 360) / 360f;
        return Color.FromHsv(hue, 0.45f + 0.1f * (int)item.Rarity, 0.62f + 0.07f * (int)item.Rarity);
    }
}
