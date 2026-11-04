using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Groves of trees and bushes, drawn from the HarvestField. The layout is fixed per planet;
/// the look changes with the world: lollipop oaks on the temperate world, cap-stalks, pagodas
/// and glowing bulb trees on the alien one, black fleshy weeping things where the Bloom has
/// taken hold. Each plant shakes when chipped, drops to a stump when felled, and grows back
/// when the field says so.
/// </summary>
public partial class Forest : Node3D
{
    public HarvestField Field { get; private set; }
    public readonly List<Vector3> Groves = new();

    sealed class PlantView
    {
        public Plant Plant;
        public MeshInstance3D Mesh, Stump;
        public CollisionShape3D Shape;
        public float Yaw, Scale;
        public bool ShownAlive = true;
        public float ShownGrow = 1f;
    }

    readonly List<PlantView> _views = new();
    ShaderMaterial _material;
    ArrayMesh[] _treeMeshes = Array.Empty<ArrayMesh>(), _bushMeshes = Array.Empty<ArrayMesh>();
    ArrayMesh _stump;

    public void Build(Terrain terrain, NavGrid nav, HarvestRules rules)
    {
        Field = new HarvestField(rules, nav);
        _material = CreatureMaterials.Plain(1.6f);
        var rng = Rng.For(Planet.LandSeed, "forest");

        // One grove just ahead of where you start, so the harvester has something to chew.
        Groves.Add(new Vector3(-30f, 0f, -40f));
        for (int i = 0; i < 9; i++)
        {
            float a = rng.Range(0f, MathF.Tau), r = rng.Range(55f, 200f);
            Groves.Add(new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r));
        }
        var placed = new List<Vector3>();
        foreach (Vector3 g in Groves)
        {
            float radius = rng.Range(12f, 22f);
            int trees = rng.Int(14, 24), bushes = rng.Int(6, 12);
            for (int i = 0; i < trees + bushes; i++)
            {
                bool tree = i < trees;
                float a = rng.Range(0f, MathF.Tau), r = radius * MathF.Sqrt(rng.Float());
                var p = g + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
                if (!Fits(p, placed, tree ? 3f : 1.5f, nav)) continue;
                AddPlant(terrain, rng, p, tree);
                placed.Add(p);
            }
        }
        for (int i = 0; i < 45; i++)
        {
            var p = new Vector3(rng.Range(-220f, 220f), 0f, rng.Range(-220f, 220f));
            if (Fits(p, placed, 4f, nav)) { AddPlant(terrain, rng, p, rng.Chance(0.7f)); placed.Add(p); }
        }
    }

    static bool Fits(Vector3 p, List<Vector3> placed, float spacing, NavGrid nav)
    {
        if (new Vector2(p.X, p.Z - 15f).Length() < 14f || new Vector2(p.X - 7f, p.Z - 10f).Length() < 8f) return false; // start, hovercraft
        if (MathF.Abs(p.X) > 225f || MathF.Abs(p.Z) > 225f || nav.Blocked(p)) return false;
        foreach (Vector3 q in placed) if (WildSim.Flat(p - q).Length() < spacing) return false;
        return true;
    }

    void AddPlant(Terrain terrain, Rng rng, Vector3 p, bool tree)
    {
        p.Y = terrain.Height(p.X, p.Z);
        Plant plant = tree
            ? Field.Add(PlantKind.Tree, p, rng.Range(0.28f, 0.45f), rng.Range(5.5f, 10f), rng.Range(2f, 3.5f), rng.Int(0, 4))
            : Field.Add(PlantKind.Bush, p, 0.2f, rng.Range(0.8f, 1.6f), rng.Range(0.8f, 1.4f), rng.Int(0, 2));
        var view = new PlantView { Plant = plant, Yaw = rng.Range(0f, MathF.Tau), Scale = tree ? plant.Height / 8f : plant.Height };
        view.Mesh = new MeshInstance3D { MaterialOverride = _material, VisibilityRangeEnd = tree ? 320f : 120f };
        AddChild(view.Mesh);
        if (tree)
        {
            view.Stump = new MeshInstance3D { MaterialOverride = _material, Visible = false, VisibilityRangeEnd = 120f };
            AddChild(view.Stump);
            var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = p + Vector3.Up * 2f };
            view.Shape = new CollisionShape3D { Shape = new CylinderShape3D { Radius = plant.TrunkRadius, Height = 4f } };
            body.AddChild(view.Shape);
            AddChild(body);
        }
        _views.Add(view);
    }

    public void ApplyStyle(WorldStyle style)
    {
        _treeMeshes = new ArrayMesh[5];
        for (int v = 0; v < 5; v++) _treeMeshes[v] = TreeMesh(style.Trees, v);
        _bushMeshes = new ArrayMesh[3];
        for (int v = 0; v < 3; v++) _bushMeshes[v] = BushMesh(style.Trees, v);
        _stump = StumpMesh(style.Trees);
        Field.Update(1e6f, Array.Empty<Vector3>()); // a new world starts with its forest standing
        foreach (PlantView v in _views)
        {
            Plant p = v.Plant;
            v.Mesh.Mesh = p.Kind == PlantKind.Tree ? _treeMeshes[p.Variant % 5] : _bushMeshes[p.Variant % 3];
            if (v.Stump != null) v.Stump.Mesh = _stump;
            p.Grow = 1f;
            v.ShownGrow = -1f;
            v.ShownAlive = !p.Alive;
        }
        Sync();
    }

    /// <summary>Updates shaking, felled and regrowing plants. Untouched plants cost nothing.</summary>
    public void Sync()
    {
        foreach (PlantView v in _views)
        {
            Plant p = v.Plant;
            bool changed = v.ShownAlive != p.Alive || MathF.Abs(v.ShownGrow - p.Grow) > 0.001f || p.Shake > 0f;
            if (!changed) continue;
            v.ShownAlive = p.Alive;
            v.ShownGrow = p.Grow;
            v.Mesh.Visible = p.Alive;
            if (v.Stump != null)
            {
                v.Stump.Visible = !p.Alive || p.Grow < 0.3f;
                v.Stump.Transform = new Transform3D(new Basis(Vector3.Up, v.Yaw).Scaled(Vector3.One * (p.TrunkRadius / 0.35f)), p.Position);
            }
            if (v.Shape != null) v.Shape.Disabled = !p.Alive;
            float grow = p.Grow * p.Grow * (3f - 2f * p.Grow);
            float wobble = p.Shake * 0.06f;
            var shake = new Basis(Vector3.Right, MathF.Sin(p.Shake * 40f) * wobble) * new Basis(Vector3.Back, MathF.Cos(p.Shake * 33f) * wobble);
            v.Mesh.Transform = new Transform3D((shake * new Basis(Vector3.Up, v.Yaw)).Scaled(Vector3.One * MathF.Max(0.02f, v.Scale * grow)), p.Position);
        }
    }

    // ---- Meshes -----------------------------------------------------------------------
    // Trees are built at a nominal 8 m and scaled per plant. Glow lives in vertex alpha.

    static ArrayMesh Bake(Piece p)
    {
        var b = new MeshBuilder();
        foreach (Prim prim in p.Prims) b.Add(prim);
        return b.Commit();
    }

    static ArrayMesh TreeMesh(TreeStyle style, int v)
    {
        var p = new Piece("tree");
        var rng = Rng.For(Planet.LandSeed + (ulong)style * 31, "tree", v);
        switch (style)
        {
            case TreeStyle.Temperate:
            {
                Color bark = new Color("6b4a2e").Lerp(new Color("8a6a48"), rng.Float());
                p.Add(Shape.Cylinder, new Vector3(0.8f, 4.6f, 0.45f), new Vector3(0, 2.3f, 0), bark, 0f, 0f, null, 8);
                p.Add(Shape.Cone, new Vector3(1.4f, 0.9f, 0f), new Vector3(0, 0.45f, 0), bark.Darkened(0.15f), 0f, 0f, null, 8);
                Color[] leaves = v == 3
                    ? new[] { new Color("c8752a"), new Color("d9a23a"), new Color("b5542a") }
                    : new[] { new Color("4f7a2a"), new Color("3d6b24"), new Color("6b8f2e") };
                int blobs = rng.Int(4, 6);
                for (int i = 0; i < blobs; i++)
                {
                    float a = i * MathF.Tau / blobs + rng.Range(-0.3f, 0.3f), r = i == 0 ? 0f : rng.Range(0.8f, 1.6f);
                    float d = rng.Range(2.2f, 3.2f);
                    p.Add(Shape.Sphere, new Vector3(d, d * 0.85f, d), new Vector3(MathF.Cos(a) * r, rng.Range(5.2f, 6.8f), MathF.Sin(a) * r), leaves[i % leaves.Length]);
                }
                break;
            }
            case TreeStyle.Alien:
                if (v % 3 == 0)
                {
                    // Cap-stalk: a bending stem under a wide frilled cap, with glowing pods beneath.
                    Color stem = new Color("e8d9b0"), cap = rng.Chance(0.5f) ? new Color("e0457b") : new Color("f08a24");
                    Vector3 a0 = Vector3.Zero, a1 = new(rng.Range(-0.5f, 0.5f), 3.5f, rng.Range(-0.5f, 0.5f)), a2 = a1 + new Vector3(rng.Range(-0.6f, 0.6f), 3.2f, rng.Range(-0.6f, 0.6f));
                    p.Rod(a0, a1, 0.6f, stem);
                    p.Rod(a1, a2, 0.45f, stem);
                    p.Add(Shape.Sphere, new Vector3(5.2f, 1.3f, 5.2f), a2 + Vector3.Up * 0.3f, cap);
                    p.Add(Shape.Cylinder, new Vector3(4.6f, 0.15f, 4.6f), a2 - Vector3.Up * 0.25f, cap.Darkened(0.45f), 0f, 0f, null, 16);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * MathF.Tau / 6f;
                        var at = a2 + new Vector3(MathF.Cos(a) * 1.8f, -0.9f - (i % 2) * 0.5f, MathF.Sin(a) * 1.8f);
                        p.Rod(at + Vector3.Up * 0.8f, at, 0.05f, stem);
                        p.Add(Shape.Sphere, Vector3.One * 0.35f, at, new Color("7cf9ff"), 0f, 1f);
                    }
                }
                else if (v % 3 == 1)
                {
                    // Pagoda: stacked discs on a thin spine, teal and ochre.
                    p.Rod(Vector3.Zero, new Vector3(0, 8.5f, 0), 0.35f, new Color("3a2a4a"));
                    for (int i = 0; i < 5; i++)
                    {
                        float d = 4.2f - i * 0.7f;
                        p.Add(Shape.Sphere, new Vector3(d, 0.45f, d), new Vector3(0, 3.2f + i * 1.25f, 0), i % 2 == 0 ? new Color("2fb5a5") : new Color("e8b440"));
                    }
                    p.Add(Shape.Sphere, Vector3.One * 0.5f, new Vector3(0, 9.0f, 0), new Color("ff6fd8"), 0f, 1f);
                }
                else
                {
                    // Bulb tree: a swollen trunk bristling with glowing-tipped fronds.
                    Color skin = new Color("6a3fa0");
                    p.Add(Shape.Sphere, new Vector3(2.2f, 4.2f, 2.2f), new Vector3(0, 2.4f, 0), skin);
                    p.Rod(new Vector3(0, 4f, 0), new Vector3(0, 6.2f, 0), 0.5f, skin.Lightened(0.15f));
                    for (int i = 0; i < 9; i++)
                    {
                        float a = i * MathF.Tau / 9f;
                        var tip = new Vector3(MathF.Cos(a) * 2.6f, 7.2f + rng.Range(-0.6f, 0.8f), MathF.Sin(a) * 2.6f);
                        p.Rod(new Vector3(0, 6f, 0), tip, 0.18f, new Color("b6ff4a").Darkened(0.35f));
                        p.Add(Shape.Sphere, Vector3.One * 0.4f, tip, new Color("b6ff4a"), 0f, 1f);
                    }
                }
                break;
            default: // Bloom: black weeping flesh with pink glowing pustules
            {
                Color flesh = new Color("2a1426"), vein = new Color("ff3cac");
                Vector3 a0 = Vector3.Zero, a1 = new(rng.Range(-0.7f, 0.7f), 3.8f, rng.Range(-0.7f, 0.7f)), a2 = a1 + new Vector3(rng.Range(-0.8f, 0.8f), 3.2f, rng.Range(-0.8f, 0.8f));
                p.Rod(a0, a1, 0.9f, flesh);
                p.Rod(a1, a2, 0.6f, flesh.Lightened(0.06f));
                p.Add(Shape.Sphere, new Vector3(2.6f, 1.6f, 2.6f), a2, flesh.Lightened(0.1f));
                for (int i = 0; i < 10; i++)
                {
                    float a = i * MathF.Tau / 10f;
                    var root = a2 + new Vector3(MathF.Cos(a) * 1.0f, 0.2f, MathF.Sin(a) * 1.0f);
                    var mid = root + new Vector3(MathF.Cos(a) * 1.4f, -0.6f, MathF.Sin(a) * 1.4f);
                    var tip = mid + new Vector3(MathF.Cos(a) * 0.3f, -rng.Range(2f, 3.5f), MathF.Sin(a) * 0.3f);
                    p.Rod(root, mid, 0.18f, flesh);
                    p.Rod(mid, tip, 0.12f, flesh.Lightened(0.08f));
                    p.Add(Shape.Sphere, Vector3.One * 0.22f, tip, vein, 0f, 1f);
                }
                for (int i = 0; i < 7; i++)
                {
                    float h = rng.Range(0.8f, 6.5f);
                    float a = rng.Range(0f, MathF.Tau);
                    p.Add(Shape.Sphere, Vector3.One * rng.Range(0.25f, 0.5f), new Vector3(MathF.Cos(a) * 0.45f, h, MathF.Sin(a) * 0.45f), vein, 0f, 1f);
                }
                break;
            }
        }
        return Bake(p);
    }

    static ArrayMesh BushMesh(TreeStyle style, int v)
    {
        var p = new Piece("bush");
        var rng = Rng.For(Planet.LandSeed + (ulong)style * 17, "bush", v);
        switch (style)
        {
            case TreeStyle.Temperate:
                for (int i = 0; i < 4; i++)
                    p.Add(Shape.Sphere, Vector3.One * rng.Range(0.7f, 1.0f), new Vector3(rng.Range(-0.35f, 0.35f), 0.4f, rng.Range(-0.35f, 0.35f)), new Color("4a7a2c").Lerp(new Color("6e8f30"), rng.Float()));
                for (int i = 0; i < 6; i++)
                    p.Add(Shape.Sphere, Vector3.One * 0.12f, new Vector3(rng.Range(-0.5f, 0.5f), rng.Range(0.4f, 0.8f), rng.Range(-0.5f, 0.5f)), new Color("c0392b"));
                break;
            case TreeStyle.Alien:
                for (int i = 0; i < 7; i++)
                {
                    var tip = new Vector3(rng.Range(-0.5f, 0.5f), rng.Range(0.7f, 1.3f), rng.Range(-0.5f, 0.5f));
                    p.Rod(new Vector3(tip.X * 0.3f, 0f, tip.Z * 0.3f), tip, 0.14f, new Color("e05a9a"));
                    p.Add(Shape.Sphere, Vector3.One * 0.2f, tip, new Color("7cf9ff"), 0f, 1f);
                }
                break;
            default:
                for (int i = 0; i < 6; i++)
                {
                    var tip = new Vector3(rng.Range(-0.7f, 0.7f), rng.Range(0.5f, 1.1f), rng.Range(-0.7f, 0.7f));
                    p.Rod(Vector3.Zero, tip, 0.12f, new Color("1f0e1c"), 0, 0, cone: true);
                }
                p.Add(Shape.Sphere, Vector3.One * 0.3f, new Vector3(0, 0.3f, 0), new Color("ff3cac"), 0f, 1f);
                break;
        }
        return Bake(p);
    }

    static ArrayMesh StumpMesh(TreeStyle style)
    {
        var p = new Piece("stump");
        Color bark = style switch { TreeStyle.Alien => new Color("e8d9b0"), TreeStyle.Bloom => new Color("2a1426"), _ => new Color("6b4a2e") };
        Color cut = style switch { TreeStyle.Alien => new Color("ff9fd0"), TreeStyle.Bloom => new Color("ff3cac"), _ => new Color("e3c48a") };
        p.Add(Shape.Cylinder, new Vector3(0.85f, 0.5f, 0.75f), new Vector3(0, 0.25f, 0), bark, 0f, 0f, null, 8);
        p.Add(Shape.Cylinder, new Vector3(0.72f, 0.04f, 0.72f), new Vector3(0, 0.51f, 0), cut, 0f, style == TreeStyle.Bloom ? 0.8f : 0f, null, 8);
        return Bake(p);
    }
}
