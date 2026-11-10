using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// An asset made of a model (or several) with named animations driven by closures, and short facts.
/// <see cref="Mount"/> stands a model on the ground (or on a small plinth when it is a handheld thing
/// that has no ground of its own) with its middle on the axis.
/// </summary>
public partial class ModelAsset : AssetInstance
{
    public Func<IEnumerable<(string, string)>> FactsFn;
    readonly List<StructureAsset.Anim> _anims = new();
    StructureAsset.Anim _current;
    protected float Speed = 1f;
    Aabb _frame;
    bool _spins = true;

    public override bool Spins => _spins;
    public void SetSpins(bool spins) => _spins = spins;

    public void AddAnimation(string name, Action start, Action<float> tick, Action stop = null) =>
        _anims.Add(new StructureAsset.Anim { Name = name, Start = start, Tick = tick, Stop = stop });

    public override IReadOnlyList<string> Animations => _anims.Select(a => a.Name).ToArray();

    public override void PlayAnimation(string name)
    {
        base.PlayAnimation(name);
        _current?.Stop?.Invoke();
        BeforeAnimation();
        _current = _anims.FirstOrDefault(a => a.Name == name);
        _current?.Start?.Invoke();
    }

    protected virtual void BeforeAnimation() { }

    public override void SetSpeed(float scale) => Speed = scale;

    public override void _Process(double delta) => _current?.Tick?.Invoke((float)delta * Speed);

    public override IEnumerable<(string Label, string Value)> Facts() => FactsFn?.Invoke() ?? Enumerable.Empty<(string, string)>();

    public override Aabb? FrameBounds => _frame.Size == Vector3.Zero ? null : _frame;

    /// <summary>The box round every mesh below <paramref name="root"/>, in <paramref name="root"/>'s own frame.</summary>
    public static Aabb Measure(Node3D root)
    {
        bool any = false;
        Aabb box = default;
        void Walk(Node n, Transform3D parent)
        {
            Transform3D t = n is Node3D d ? parent * d.Transform : parent;
            if (n is MeshInstance3D m && m.Mesh != null && m.Visible)
            {
                Aabb a = t * m.Mesh.GetAabb();
                box = any ? box.Merge(a) : a;
                any = true;
            }
            foreach (Node c in n.GetChildren()) Walk(c, t);
        }
        foreach (Node c in root.GetChildren()) Walk(c, Transform3D.Identity);
        if (root is MeshInstance3D rm && rm.Mesh != null) { box = rm.Mesh.GetAabb(); any = true; }
        return any ? box : new Aabb(Vector3.Zero, Vector3.One * 0.1f);
    }

    /// <summary>
    /// Adds a model with its lowest point on the ground and its middle on the axis. With
    /// <paramref name="plinth"/>, it stands on a low round plinth that is sized to it, for handheld
    /// things (a gun, a tool) that would otherwise float.
    /// </summary>
    public Node3D Mount(Node3D model, bool plinth = false, Vector3? offset = null)
    {
        var root = new Node3D { Name = "Mounted", Position = offset ?? Vector3.Zero };
        AddChild(root);
        var holder = new Node3D { Name = "Model" };
        root.AddChild(holder);
        holder.AddChild(model);
        Aabb box = Measure(holder);
        float lift = 0f;
        if (plinth)
        {
            float r = Mathf.Max(0.12f, Mathf.Sqrt(box.Size.X * box.Size.X + box.Size.Z * box.Size.Z) * 0.5f + 0.06f);
            float h = Mathf.Clamp(r * 0.18f, 0.03f, 0.12f);
            var mat = new StandardMaterial3D { AlbedoColor = new Color("3a3f4a"), Roughness = 0.7f, Metallic = 0.2f };
            root.AddChild(new MeshInstance3D { Name = "Plinth", Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r * 1.04f, Height = h, RadialSegments = 32 }, MaterialOverride = mat, Position = new Vector3(0, h * 0.5f, 0) });
            lift = h;
        }
        Vector3 c = box.GetCenter();
        holder.Position = new Vector3(-c.X, -box.Position.Y + lift, -c.Z);
        Grow(root);
        return holder;
    }

    /// <summary>Recomputes the frame box from everything mounted (call after adding more).</summary>
    public void Grow(Node3D holder)
    {
        Aabb box = Measure(this);
        _frame = box.Size == Vector3.Zero ? new Aabb(Vector3.Zero, Vector3.One * 0.2f) : box;
    }
}
