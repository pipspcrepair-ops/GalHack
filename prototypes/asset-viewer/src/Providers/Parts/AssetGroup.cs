using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// Several structures that belong together (the pieces of a broken hull), each at the place and turn the plan
/// gives it. Every member keeps its own animations; the group plays the one that is named on all of them.
/// </summary>
public partial class AssetGroup : ModelAsset
{
    readonly Node3D _root = new() { Name = "Pieces" };
    readonly List<StructureAsset> _members = new();

    public AssetGroup() => AddChild(_root);

    public IReadOnlyList<StructureAsset> Members => _members;

    public void Add(StructureAsset member, Transform3D at)
    {
        _root.AddChild(member);
        member.Transform = at;
        _members.Add(member);
    }

    /// <summary>Puts the middle of everything on the axis (and, with <paramref name="onGround"/>, the lowest point on the ground).</summary>
    public void Centre(bool onGround)
    {
        Aabb box = Measure(_root);
        Vector3 c = box.GetCenter();
        _root.Position = new Vector3(-c.X, onGround ? -box.Position.Y : -c.Y, -c.Z);
        Grow(this);
    }

    public override IReadOnlyList<string> Animations =>
        _members.SelectMany(m => m.Animations).Distinct().ToArray();

    public override void PlayAnimation(string name)
    {
        base.PlayAnimation(name);
        foreach (StructureAsset m in _members) m.PlayAnimation(m.Animations.Contains(name) ? name : "Still");
    }

    public override void SetSpeed(float scale)
    {
        base.SetSpeed(scale);
        foreach (StructureAsset m in _members) m.SetSpeed(scale);
    }
}
