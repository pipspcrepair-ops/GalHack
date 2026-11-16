using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// What out in the open matters to people at a glance, bucketed by ground cell and refreshed a few
/// times a second (<see cref="WildSim.Step"/>): the dangerous (a predator, a Husk, a feral) and the
/// people of every band. A band or a town looks only at the cells round it, never at every creature,
/// and nothing is allocated once the cells are made.
/// </summary>
public sealed class Sightings
{
    const float Cell = 32f;
    readonly Dictionary<long, List<Creature>> _cells = new();
    readonly Stack<List<Creature>> _spare = new();
    readonly List<Creature> _near = new();

    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    /// <summary>Off, nobody sees anyone: the cells stay empty (measurements of what looking round costs).</summary>
    public static bool On = true;

    /// <summary>Whether people take note of it: one of a band, or something that could kill them.</summary>
    public static bool Matters(Creature c) => c.Herd?.Party != null || c.Species.Husk || CrossReactions.Danger(c.Species);

    public void Refresh(WildSim w)
    {
        foreach (List<Creature> list in _cells.Values) { list.Clear(); _spare.Push(list); }
        _cells.Clear();
        if (!On) return;
        foreach (Creature c in w.Creatures)
        {
            if (c.Dead || c.Removed || c.InWalls || c.Space != null || !Matters(c)) continue;
            long k = Key((int)MathF.Floor(c.Position.X / Cell), (int)MathF.Floor(c.Position.Z / Cell));
            if (!_cells.TryGetValue(k, out List<Creature> list)) _cells[k] = list = _spare.Count > 0 ? _spare.Pop() : new List<Creature>();
            list.Add(c);
        }
    }

    /// <summary>Those within so far of a point (flat), as of the last refresh: a list of its own, good until the next call.</summary>
    public List<Creature> Near(Vector3 at, float reach)
    {
        _near.Clear();
        int x0 = (int)MathF.Floor((at.X - reach) / Cell), x1 = (int)MathF.Floor((at.X + reach) / Cell);
        int z0 = (int)MathF.Floor((at.Z - reach) / Cell), z1 = (int)MathF.Floor((at.Z + reach) / Cell);
        float r2 = reach * reach;
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!_cells.TryGetValue(Key(x, z), out List<Creature> list)) continue;
                foreach (Creature c in list)
                    if (!c.Dead && !c.Removed && WildSim.Flat(c.Position - at).LengthSquared() < r2) _near.Add(c);
            }
        return _near;
    }
}
