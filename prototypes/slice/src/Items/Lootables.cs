using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using Godot;
using Wildlife;
using Layers = GalHack.Physics.Layers;

namespace Slice.Items;

/// <summary>What a ground container is.</summary>
public enum LootKind
{
    /// <summary>
    /// A body that lies where it fell, holding what it had: an animal's meat, hide and parts (a machine's
    /// salvage), a person's weapon, ammo and goods. Once the body is gone, by any means, what's left on it
    /// is in a bag where it lay.
    /// </summary>
    Corpse,
    /// <summary>
    /// A bag of someone's things: the one bag, the same everywhere. The sleeper's own (left on death with
    /// everything but the toolbar and the tool, or set down out of the inventory), what's left of a body
    /// once it's gone, and the bundle a frightened one tosses down to make you leave.
    /// </summary>
    Bag,
    /// <summary>
    /// A place's cache (a locker, a crate, a vault's strongbox): part of the place, drawn by it, and
    /// never taken away, empty or not (Empyrion's POI containers stay where they stand).
    /// </summary>
    Cache,
}

/// <summary>A container on the ground: a grid of slots over its own storage, at a place in the zone.</summary>
public sealed class Lootable
{
    public int Id { get; init; }
    public LootKind Kind { get; init; }
    public string Name { get; init; }
    /// <summary>Where it is: a bag's or a body's bottom, a cache's floor.</summary>
    public Vector3 Position;
    public float Radius = 0.6f;
    public ItemGrid Grid { get; init; }
    public SharedStorage Storage => Grid.Storage;
    /// <summary>The creature this is the body of, while the wildlife still draws it.</summary>
    public Creature Creature;
    /// <summary>A body's: true once the body is no more (eaten, cleared away, blown apart, gathered).</summary>
    public Func<bool> Gone;
    /// <summary>The colour of the bag what's left on a body ends up in.</summary>
    public Color Colour = Lootables.HideColour;
    public Node3D Node;
    public bool IsEmpty => Storage.IsEmpty;

    /// <summary>A bag of the sleeper's own: marked on the compass and the map.</summary>
    public bool Yours;
    /// <summary>Whose bag or body it was (a creature, a townsperson; null: the sleeper's).</summary>
    public object Owner;
    /// <summary>A bag in the air (tossed, or falling onto what is under it): how fast it goes; null once it rests.</summary>
    public Vector3? Flying;
    /// <summary>The up of the face it rests on.</summary>
    public Vector3 Up = Vector3.Up;
    /// <summary>The Husk its owner's body rose as, to be met about it (the game keeps one walking near while the bag lies), and the one walking now.</summary>
    public Species Risen;
    public Creature RisenNow;
}

/// <summary>
/// Every container lying in the world: creature bodies and bags. Each is a grid like any storage
/// (Empyrion's loot window), opened with E on it. Nothing here is ever deleted while it holds anything:
/// a container goes only once it is empty (and closed).
///
/// A bag is a sack drawn by code (its profile turned about its axis, tied at the neck, in its owner's
/// colour), with a collision shape of the same hull. It comes down on a real arc and rests on the first
/// face it meets under it, its bottom flat on that face: never in the air, never in the ground.
/// </summary>
public partial class Lootables : Node3D
{
    public ItemCatalogue Items;
    public readonly List<Lootable> All = new();
    int _nextId = 1;

    /// <summary>The container open in the inventory view right now (not removed while open).</summary>
    public Lootable Open;

    /// <summary>A container was emptied and taken away.</summary>
    public event Action<Lootable> Removed;

    /// <summary>A bag came to rest.</summary>
    public event Action<Lootable> Landed;

    /// <summary>A bag's slots across and down, and the colour of the sleeper's own (set by the game).</summary>
    public int[] BagSize = { 8, 8 };
    public Color YourColour = new("a8481a");
    /// <summary>A bag of hide: what's left of an animal once its body is gone.</summary>
    public static readonly Color HideColour = new("7f5539");

    /// <summary>What a bag lands and rests on: the ground and rock, hulls, a place's floors and furniture (its scenery layer), boulders and logs.</summary>
    public const uint RestOn = Layers.World | Layers.Hull | Layers.Flora | (1u << 10);

    /// <summary>How fast things fall, m/s².</summary>
    const float Fall = 9.8f;

    public Lootable Create(LootKind kind, string name, Vector3 at, int columns, int rows, Bill contents = null)
    {
        var storage = SharedStorage.Unbounded(Items, name);
        var loot = new Lootable
        {
            Id = _nextId++, Kind = kind, Name = name, Position = at,
            Grid = new ItemGrid(storage, columns, rows, growable: true),
        };
        if (contents != null) storage.AddAll(contents);
        loot.Grid.Reconcile();
        loot.Node = new Node3D { Name = $"Loot{kind}" };
        AddChild(loot.Node);
        loot.Node.GlobalPosition = at;
        All.Add(loot);
        return loot;
    }

    /// <summary>A bag set down on what is under a point, holding the items: its bottom flat on that face, upright to it.</summary>
    public Lootable Bag(Bill items, Vector3 at, string name, Color colour, bool yours = false, object owner = null)
    {
        Lootable bag = NewBag(items, at, name, colour, yours, owner);
        Rest(bag, at);
        return bag;
    }

    /// <summary>A bag thrown from a point (where it is held) to come down at another, on a real arc: a lob, up and down in well under a second; it rests where it first meets something under it.</summary>
    public Lootable Toss(Bill items, Vector3 from, Vector3 to, string name, Color colour, object owner = null)
    {
        Vector3 start = from + Vector3.Down * BagHeight * 0.5f; // (its bottom: held by its middle)
        Lootable bag = NewBag(items, start, name, colour, false, owner);
        Vector3 flat = new(to.X - start.X, 0f, to.Z - start.Z);
        float time = Math.Clamp(0.35f + flat.Length() * 0.06f, 0.4f, 1f);
        bag.Flying = new Vector3(flat.X / time, (to.Y - start.Y) / time + 0.5f * Fall * time, flat.Z / time);
        return bag;
    }

    /// <summary>What's left of a body once it's gone, in one bag set down where it lay (owner): nothing left, no bag.</summary>
    public Lootable Leftovers(Bill items, Vector3 at, Color colour, object owner) =>
        items.IsEmpty ? null : Bag(items, Settle != null ? Settle(at) : at, "Bag", colour, owner: owner);

    /// <summary>Puts the sleeper's things into their bag lying within 2.5 m of a spot, or a new one set down there.</summary>
    public Lootable Spill(Bill items, Vector3 at)
    {
        if (items == null || items.IsEmpty) return null;
        Lootable near = All.Where(l => l.Kind == LootKind.Bag && l.Yours && l.Flying == null && l.Position.DistanceTo(at) < 2.5f).OrderBy(l => l.Position.DistanceTo(at)).FirstOrDefault();
        if (near == null) return Bag(items, at, "Your bag", YourColour, yours: true);
        near.Storage.AddAll(items);
        near.Grid.Reconcile();
        return near;
    }

    Lootable NewBag(Bill items, Vector3 at, string name, Color colour, bool yours, object owner)
    {
        Lootable bag = Create(LootKind.Bag, name, at, BagSize[0], BagSize[1], items);
        bag.Yours = yours;
        bag.Owner = owner;
        bag.Radius = BagRadius;
        bag.Node.GlobalBasis = new Basis(Vector3.Up, bag.Id * 2.4f); // (each lies its own way round)
        BagLook(bag.Node, colour);
        return bag;
    }

    /// <summary>
    /// The container whose body a ray meets first within reach, or null. Each is a ball round its middle a
    /// little bigger than what's drawn (a cache with its lid, a bag, a body's bulk): wherever the look lands on it,
    /// from any side or from above, it's that container, as long as nothing solid stands nearer. The ball is
    /// met where the ray enters it, not at its middle: a look that stops on a cache's own lid (a fifth of a
    /// metre short of its middle and more) is a look at the cache. <paramref name="reach"/> is how far the
    /// look goes before something solid stops it; distance is where it meets the container.
    /// </summary>
    public Lootable Pick(Vector3 eye, Vector3 dir, float reach, out float distance)
    {
        Lootable best = null;
        distance = reach;
        foreach (Lootable l in All)
        {
            // A body is searched wherever the look lands on it as it lies (fallen, tumbled): its drawn parts.
            if (l.Kind == LootKind.Corpse && l.Creature != null && DrawnOf?.Invoke(l.Creature) is Node3D drawn && drawn.Visible && Along(drawn, eye, dir, distance) is float at)
            {
                distance = at;
                best = l;
                continue;
            }
            Vector3 c = l.Position + l.Up * (l.Kind switch { LootKind.Corpse => l.Radius * 0.6f, LootKind.Bag => BagHeight * 0.5f, _ => 0.25f });
            float r = l.Radius + 0.25f, along = (c - eye).Dot(dir), off = (eye + dir * along - c).Length();
            if (off > r) continue;
            float enter = along - MathF.Sqrt(r * r - off * off);
            if (along < 0f || enter > distance) continue;
            distance = MathF.Max(0f, enter);
            best = l;
        }
        return best;
    }

    /// <summary>A creature's drawn body (its view), while the wildlife still draws it (set by the game): what a look at its body meets.</summary>
    public Func<Creature, Node3D> DrawnOf;

    /// <summary>Where a look first meets any drawn part of a body (each part's box as posed, a little grown), within reach; null if it misses.</summary>
    static float? Along(Node3D body, Vector3 eye, Vector3 dir, float reach)
    {
        float? best = null;
        foreach (Node n in body.GetChildren())
        {
            if (n is not MeshInstance3D mi || !mi.Visible || mi.Mesh == null) continue;
            Aabb box = mi.Mesh.GetAabb().Grow(0.06f);
            if (box.Size.LengthSquared() < 1e-6f) continue;
            Transform3D inv = mi.GlobalTransform.AffineInverse();
            Vector3 o = inv * eye, d = inv.Basis * dir;
            float near = 0f, far = reach;
            bool hit = true;
            for (int k = 0; k < 3 && hit; k++)
            {
                float lo = box.Position[k], hi = box.End[k];
                if (MathF.Abs(d[k]) < 1e-6f) { if (o[k] < lo || o[k] > hi) hit = false; continue; }
                float t0 = (lo - o[k]) / d[k], t1 = (hi - o[k]) / d[k];
                if (t0 > t1) (t0, t1) = (t1, t0);
                near = MathF.Max(near, t0);
                far = MathF.Min(far, t1);
                if (near > far) hit = false;
            }
            if (hit && (best == null || near < best)) best = near;
        }
        return best;
    }

    /// <summary>Where a creature's body is in the zone, while the wildlife still draws it (set by the game).</summary>
    public Func<Creature, Vector3> BodyAt;

    /// <summary>The floor under a point that someone on foot can reach (set by the game): a body that came down on a ledge or a ceiling ends on it.</summary>
    public Func<Vector3, Vector3> Settle;

    /// <summary>Empty ones that aren't open go; bags in the air come down; a body goes where its creature's body goes (it falls off a ledge, it lets go of a ceiling), and once the body is gone what's left on it is in a bag where it lay.</summary>
    public void Tick()
    {
        float dt = (float)GetPhysicsProcessDeltaTime();
        for (int i = All.Count - 1; i >= 0; i--)
        {
            Lootable l = All[i];
            if (l.IsEmpty && l != Open && l.Kind != LootKind.Cache)
            {
                l.Node.QueueFree();
                All.RemoveAt(i);
                if (l.Creature != null) l.Creature.MeatLeft = 0f; // the wildlife clears the body away
                Removed?.Invoke(l);
                continue;
            }
            if (l.Flying is Vector3 v) { Fly(l, v, dt); continue; }
            if (l.Gone?.Invoke() == true && l != Open)
            {
                Leftovers(l.Storage.Contents, l.Position, l.Colour, l.Owner);
                l.Node.QueueFree();
                All.RemoveAt(i);
                continue;
            }
            if (l.Creature != null && !l.Creature.Removed && BodyAt != null && BodyAt(l.Creature) is var at && at.DistanceSquaredTo(l.Position) > 1e-4f)
            {
                l.Position = at;
                l.Node.GlobalPosition = at;
            }
        }
    }

    public void Shift(Vector3 by)
    {
        foreach (Lootable l in All)
        {
            l.Position += by;
            l.Node.GlobalPosition = l.Position;
        }
    }

    // ---- A bag in the air, and at rest ------------------------------------------------------------

    /// <summary>A step of a bag's flight, its bottom along the arc: a floor it meets takes it; a wall or a trunk turns it straight down.</summary>
    void Fly(Lootable l, Vector3 v, float dt)
    {
        Vector3 from = l.Position, to = from + v * dt;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from + Vector3.Up * 0.05f, to, RestOn));
        if (hit.Count > 0)
        {
            Vector3 p = (Vector3)hit["position"], n = (Vector3)hit["normal"];
            if (n.Y > 0.5f) { Rest(l, p); return; }
            // (A wall or a trunk: off it. A ceiling: back down.)
            Vector3 side = new(n.X, 0f, n.Z);
            to = side.LengthSquared() > 1e-4f ? p + side.Normalized() * (BagRadius + 0.05f) : p + Vector3.Down * 0.05f;
            v = new Vector3(0f, MathF.Min(v.Y, 0f), 0f);
        }
        v += Vector3.Down * Fall * dt;
        l.Flying = v;
        l.Position = to;
        l.Node.GlobalPosition = to;
        // Falling a long way with nothing met (no collision there yet): down on what's there.
        if (v.Y < -30f) Rest(l, to);
    }

    /// <summary>
    /// Sets a bag down on what is under a point: the face's height at its middle and round the edge of its
    /// bottom, and the slope those make; its bottom flat on that slope, never under any of it. With nothing
    /// under it at all (in space) it stays where it was let go.
    /// </summary>
    void Rest(Lootable l, Vector3 at)
    {
        l.Flying = null;
        Vector3? mid = Under(at);
        if (mid == null && Settle?.Invoke(at) is Vector3 s && float.IsFinite(s.Y)) mid = s;
        Vector3 p = mid ?? at, up = Vector3.Up;
        if (mid != null)
        {
            float e = BagFoot;
            float x0 = Under(p + new Vector3(-e, 0f, 0f))?.Y ?? p.Y, x1 = Under(p + new Vector3(e, 0f, 0f))?.Y ?? p.Y;
            float z0 = Under(p + new Vector3(0f, 0f, -e))?.Y ?? p.Y, z1 = Under(p + new Vector3(0f, 0f, e))?.Y ?? p.Y;
            up = new Vector3(-(x1 - x0) / (2f * e), 1f, -(z1 - z0) / (2f * e)).Normalized();
            // Lifted clear of any of the edge that stands proud of the slope through its middle; too steep to lie on, it sits level on the highest.
            if (up.Y < 0.7f) { up = Vector3.Up; p.Y = MathF.Max(p.Y, MathF.Max(MathF.Max(x0, x1), MathF.Max(z0, z1))); }
            else p.Y = MathF.Max(p.Y, MathF.Max((x0 + x1) * 0.5f, (z0 + z1) * 0.5f));
        }
        l.Position = p;
        l.Up = up;
        Vector3 back = l.Node.GlobalBasis.Z;
        Vector3 x = up.Cross(back).LengthSquared() > 1e-4f ? up.Cross(back).Normalized() : up.Cross(Vector3.Back).Normalized();
        l.Node.GlobalTransform = new Transform3D(new Basis(x, up, x.Cross(up)), p);
        if (l.Node.GetNodeOrNull<StaticBody3D>("Body") is { } body) body.CollisionLayer = Layers.Props;
        Landed?.Invoke(l);
    }

    /// <summary>The first face a bag would rest on straight under a point (from a little above it), or null.</summary>
    Vector3? Under(Vector3 at)
    {
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 0.35f, at + Vector3.Down * 60f, RestOn));
        return hit.Count > 0 ? (Vector3)hit["position"] : null;
    }

    // ---- Looks ----------------------------------------------------------------------------------

    /// <summary>A bag's size: the sack's widest from its middle, its height, and how far out from its middle its flat bottom reaches, m.</summary>
    public const float BagRadius = 0.27f, BagHeight = 0.47f, BagFoot = 0.15f;

    /// <summary>The sack's profile from its flat bottom up, (out, up), m: a bulging body narrowing to a tied neck, the cloth flaring above the tie.</summary>
    static readonly Vector2[] Profile =
    {
        new(0f, 0f), new(0.17f, 0f), new(0.215f, 0.04f), new(0.235f, 0.13f), new(0.225f, 0.24f), new(0.185f, 0.32f),
        new(0.12f, 0.37f), new(0.055f, 0.395f), new(0.05f, 0.41f), new(0.085f, 0.45f), new(0.06f, 0.47f), new(0f, 0.465f),
    };

    static ArrayMesh _sack;
    static Shape3D _hull;
    static readonly Dictionary<Color, StandardMaterial3D> Cloths = new();

    /// <summary>The sack: its profile turned about its axis, wider one way than the other (a bag, not a pot).</summary>
    static ArrayMesh Sack()
    {
        if (_sack != null) return _sack;
        const int around = 14;
        static Vector3 At(int ring, int step)
        {
            float t = step * MathF.Tau / around;
            Vector2 r = Profile[ring];
            return new Vector3(MathF.Cos(t) * r.X * 1.15f, r.Y, MathF.Sin(t) * r.X * 0.88f);
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int k = 0; k < Profile.Length - 1; k++)
            for (int a = 0; a < around; a++)
            {
                Vector3 p0 = At(k, a), p1 = At(k, a + 1), p2 = At(k + 1, a + 1), p3 = At(k + 1, a);
                st.AddVertex(p0); st.AddVertex(p1); st.AddVertex(p2);
                st.AddVertex(p0); st.AddVertex(p2); st.AddVertex(p3);
            }
        st.GenerateNormals();
        return _sack = st.Commit();
    }

    /// <summary>The sack's own hull, as its collision: what stops you is what's drawn.</summary>
    static Shape3D Hull() => _hull ??= Sack().CreateConvexShape(true, false);

    static StandardMaterial3D Cloth(Color c)
    {
        if (Cloths.TryGetValue(c, out var m)) return m;
        if (Cloths.Count > 64) Cloths.Clear();
        return Cloths[c] = new StandardMaterial3D { AlbedoColor = c, Roughness = 0.9f };
    }

    /// <summary>A bag's look and its body (off the collision layer until it rests): the sack in its owner's colour, and the cord round its neck.</summary>
    static void BagLook(Node3D root, Color colour)
    {
        root.AddChild(new MeshInstance3D { Name = "Sack", Mesh = Sack(), MaterialOverride = Cloth(colour) });
        root.AddChild(new MeshInstance3D
        {
            Name = "Tie", Mesh = new TorusMesh { InnerRadius = 0.045f, OuterRadius = 0.068f, Rings = 12, RingSegments = 6 },
            MaterialOverride = Cloth(colour.Darkened(0.5f)), Position = Vector3.Up * 0.405f, Scale = new Vector3(1.1f, 1f, 0.9f),
        });
        var body = new StaticBody3D { Name = "Body", CollisionLayer = 0, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = Hull() });
        root.AddChild(body);
    }
}
