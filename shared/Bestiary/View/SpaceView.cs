using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Keeps a <see cref="CreatureView"/> for every creature of a <see cref="SpaceSim"/>, the space
/// twin of <see cref="WildlifeView"/>. Distances scale with each creature's size: a leviathan
/// stays fully posed much further out than a leech.
/// </summary>
public partial class SpaceView : Node3D
{
    SpaceSim _sim;
    readonly Dictionary<Creature, CreatureView> _views = new();
    readonly List<Creature> _gone = new();

    public int ViewCount => _views.Count;

    public void Bind(SpaceSim sim)
    {
        foreach (var v in _views.Values) v.QueueFree();
        _views.Clear();
        _sim = sim;
    }

    public CreatureView ViewOf(Creature c) => _views.GetValueOrDefault(c);

    public void Sync(Vector3 camera)
    {
        if (_sim == null) return;
        foreach (Creature c in _sim.Creatures)
        {
            if (c.Removed) continue;
            if (!_views.TryGetValue(c, out CreatureView view))
            {
                view = new CreatureView();
                AddChild(view);
                view.Build(c);
                _views[c] = view;
            }
            float d = (c.Position - camera).Length(), size = MathF.Max(1f, c.Species.Body.Size);
            c.RigInterval = d < 40f + size * 8f ? 1 : d < 90f + size * 16f ? 2 : 4;
            if (c.RigInterval == 1 || c.RigPending == 0f) view.Sync();
        }
        _gone.Clear();
        foreach (var (c, v) in _views)
            if (c.Removed)
            {
                v.QueueFree();
                _gone.Add(c);
            }
        foreach (Creature c in _gone) _views.Remove(c);
    }
}
