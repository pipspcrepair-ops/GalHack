using System.Collections.Generic;
using Godot;

namespace PlanetSurface.Terrain;

/// <summary>
/// A far chunk's new drawing (a finer or coarser level, or flora redone) takes over its ground at
/// once, but its flora dissolves in over the old drawing's for <see cref="FloraSwapSeconds"/>
/// (flora.gdshader's flora_swap): a plant both draw alike looks the same throughout, one only the
/// new draws fades in, one only the old drew fades out. A chunk that arrives late, with plants in
/// sight, fades them in the same way. The old node stays for the fade with its ground hidden.
/// </summary>
public partial class TerrainStreamer
{
    public const float FloraSwapSeconds = 0.5f;

    sealed class Swap
    {
        public Node3D Old, New;
        public float T;
    }

    readonly Dictionary<(int, int), Swap> _swaps = new();
    readonly List<(int, int)> _swapsDone = new();

    /// <summary>A far chunk's drawing was replaced (old may be null: its first): the flora dissolves across.</summary>
    void BeginSwap(int fx, int fz, Node3D old, Node3D neu)
    {
        if (_swaps.Remove((fx, fz), out Swap was))
        {
            // Replaced again mid-fade: the one before goes now, and the fade starts over from the last.
            was.Old?.QueueFree();
            SwapBands(was.New, 0f, 1f);
        }
        if (old != null)
        {
            bool any = false;
            foreach (Node n in old.GetChildren())
                if (n is MultiMeshInstance3D) any = true;
                else if (n is Node3D n3) n3.Visible = false;
            if (!any)
            {
                old.QueueFree();
                old = null;
            }
        }
        var swap = new Swap { Old = old, New = neu };
        _swaps[(fx, fz)] = swap;
        Bands(swap);
    }

    static void SwapBands(Node3D node, float from, float to)
    {
        if (node == null) return;
        foreach (Node n in node.GetChildren())
            if (n is MultiMeshInstance3D m) m.SetInstanceShaderParameter(PlanetSurface.Fx.Names.Of("flora_swap"), new Vector2(from, to));
    }

    static void Bands(Swap s)
    {
        SwapBands(s.New, 0f, s.T);
        SwapBands(s.Old, s.T, 1f);
    }

    void StepSwaps(float dt)
    {
        if (_swaps.Count == 0) return;
        _swapsDone.Clear();
        foreach (var (key, s) in _swaps)
        {
            s.T = Mathf.Min(1f, s.T + dt / FloraSwapSeconds);
            if (s.Old != null)
            {
                // The old one stands where the new one does (a seam crossing moves both).
                s.Old.Position = s.New.Position;
            }
            Bands(s);
            if (s.T < 1f) continue;
            s.Old?.QueueFree();
            _swapsDone.Add(key);
        }
        foreach (var key in _swapsDone) _swaps.Remove(key);
    }

    /// <summary>A chunk went from the drawing (out of range, or the planet changed): any fade of it is over.</summary>
    void EndSwap((int, int) key)
    {
        if (_swaps.Remove(key, out Swap s)) s.Old?.QueueFree();
    }

    void ClearSwaps()
    {
        foreach (Swap s in _swaps.Values) s.Old?.QueueFree();
        _swaps.Clear();
    }
}
