using System.Collections.Generic;
using Godot;
using PoiGenerator;

namespace AssetViewer.Providers;

/// <summary>Several places and the ground between them (a city block, a street of houses, a farm's lots): one asset, a bounded patch.</summary>
public sealed partial class PlaceGroup : ProviderAsset
{
    public readonly List<PoiView> Views = new();
    public readonly List<(string, string)> FactList = new();
    Aabb _flat;

    /// <param name="flat">The ground the patch covers (its height is ignored: the buildings' own heights are added).</param>
    public PlaceGroup(Aabb flat, float groundRadius = 0f)
    {
        _flat = flat;
        Stage.Ground = false;
        Stage.GroundRadius = groundRadius;
    }

    public PoiView Add(PoiView view, Vector3 at)
    {
        view.Position = at;
        AddChild(view);
        Views.Add(view);
        return view;
    }

    public PlaceGroup Fact(string label, string value) { FactList.Add((label, value)); return this; }
    public PlaceGroup WithBounds(Aabb flat) { _flat = flat; return this; }

    public override Aabb? FrameBounds
    {
        get
        {
            Aabb box = new(new Vector3(_flat.Position.X, 0, _flat.Position.Z), new Vector3(_flat.Size.X, 1, _flat.Size.Z));
            foreach (PoiView v in Views)
                if (IsInstanceValid(v)) box = box.Merge(v.Transform * v.SurfaceBounds());
            return box;
        }
    }

    public override IEnumerable<(string Label, string Value)> Facts() => FactList;

    public override void _Process(double delta)
    {
        Camera3D cam = GetViewport()?.GetCamera3D();
        foreach (PoiView v in Views) if (IsInstanceValid(v)) v.Watcher = cam;
    }
}
