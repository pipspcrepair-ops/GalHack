using System.Collections.Generic;
using System.Linq;
using Godot;
using PlanetSurface;

namespace Slice.Tests;

/// <summary>
/// The main thread's frame split by system, as the game runs (a route, a scene), with nothing switched off: a
/// marker node goes in just before each child of the given parents, at that child's own priority, and the main
/// thread's CPU time and allocations from one marker to the next (in the same process or physics pass) are that
/// child's: itself and everything under it. Children are named by their script class (by node name for the
/// engine's own), so a hundred places count as one. A parent's own work is the stretch from its marker to its
/// first child's. A node under a child with a priority of its own runs out of tree order and is counted to the
/// marker before it. Children added later are marked at the next <see cref="Reset"/>.
/// </summary>
public sealed partial class SystemSplit
{
    sealed partial class Mark : Node
    {
        public SystemSplit Split;
        public int Index;
        public bool End;

        public override void _Process(double delta)
        {
            Split.Hit(Index);
            if (End) Split.EndFrame();
        }

        public override void _PhysicsProcess(double delta) => Split.Hit(Index);
    }

    const int EndIndex = 0;
    readonly Node[] _parents;
    /// <summary>Children split one level further too, by class (a place into its creatures, people, dressing and itself), however many come and go.</summary>
    public System.Func<Node, bool> Deeper = _ => false;
    readonly HashSet<Node> _split, _marked = new();
    readonly List<string> _names = new() { "(end)" };
    readonly List<Mark> _marks = new();
    double[] _frame = new double[1], _sum = new double[1], _worst = new double[1], _last = new double[1];
    long[] _bytes = new long[1];
    int _open = -1, _frames;
    ulong _at;
    long _allocAt;

    /// <summary>Splits each parent into itself and each of its children (a child that is itself split is split, not counted whole).</summary>
    public SystemSplit(params Node[] parents)
    {
        _parents = parents;
        _split = new HashSet<Node>(parents);
        // The end of every pass: nothing after it is the game's.
        var end = new Mark { Split = this, End = true, Index = EndIndex, Name = "Split end", ProcessPriority = 2_000_000, ProcessPhysicsPriority = 2_000_000 };
        parents[0].GetTree().Root.AddChild(end);
        _marks.Add(end);
        Refresh();
    }

    void Refresh()
    {
        foreach (Node p in _parents)
        {
            if (!GodotObject.IsInstanceValid(p) || !p.IsInsideTree()) continue;
            if (!_split.Contains(p.GetParent())) Before(p, true);
            foreach (Node kid in p.GetChildren())
            {
                if (kid is Mark) continue;
                bool deeper = Deeper(kid);
                Before(kid, _split.Contains(kid) || deeper);
                if (!deeper) continue;
                foreach (Node grandchild in kid.GetChildren())
                    if (grandchild is not Mark) Before(grandchild, false, NameOf(kid, false) + "/" + grandchild.GetType().Name);
            }
        }
        int n = _names.Count;
        if (_sum.Length == n) return;
        System.Array.Resize(ref _frame, n);
        System.Array.Resize(ref _sum, n);
        System.Array.Resize(ref _worst, n);
        System.Array.Resize(ref _last, n);
        System.Array.Resize(ref _bytes, n);
    }

    static string NameOf(Node n, bool itself)
    {
        string type = n.GetType().Name;
        string name = type == n.GetClass() ? n.Name.ToString() : type;
        return itself ? name + " (itself)" : name;
    }

    int Add(string name)
    {
        int i = _names.IndexOf(name);
        if (i >= 0) return i;
        _names.Add(name);
        return _names.Count - 1;
    }

    void Before(Node n, bool itself, string name = null)
    {
        if (!_marked.Add(n)) return;
        var m = new Mark { Split = this, Index = Add(name ?? NameOf(n, itself)), Name = "Split mark", ProcessPriority = n.ProcessPriority, ProcessPhysicsPriority = n.ProcessPhysicsPriority };
        n.AddSibling(m);
        n.GetParent().MoveChild(m, n.GetIndex());
        _marks.Add(m);
    }

    void Hit(int index)
    {
        ulong now = FrameProbe.Cycles();
        long alloc = System.GC.GetAllocatedBytesForCurrentThread();
        if (_open >= 0 && _open < _frame.Length)
        {
            if (now > _at) _frame[_open] += (now - _at) / FrameProbe.CyclesPerMs;
            _bytes[_open] += alloc - _allocAt;
        }
        _at = now;
        _allocAt = alloc;
        // The end marker closes the pass: nothing is open until the next pass's first marker.
        _open = index == EndIndex ? -1 : index;
    }

    /// <summary>The frame's split goes into the sums (the end marker, once a frame).</summary>
    void EndFrame()
    {
        for (int i = 0; i < _frame.Length; i++)
        {
            _sum[i] += _frame[i];
            if (_frame[i] > _worst[i]) _worst[i] = _frame[i];
            _last[i] = _frame[i];
            _frame[i] = 0;
        }
        _frames++;
    }

    /// <summary>Marks any children added since, keeping the sums.</summary>
    public void MarkNew() => Refresh();

    /// <summary>Starts the sums over, marking any children added since.</summary>
    public void Reset()
    {
        Refresh();
        System.Array.Clear(_sum);
        System.Array.Clear(_worst);
        System.Array.Clear(_bytes);
        _frames = 0;
    }

    /// <summary>The systems that took the most of the last whole frame, CPU ms.</summary>
    public string LastFrame(int most = 5) => string.Join(", ", Enumerable.Range(1, _names.Count - 1).Where(i => _last[i] > 0.5).OrderByDescending(i => _last[i]).Take(most).Select(i => $"{_names[i]} {_last[i]:F1}"));

    /// <summary>Each system's mean and worst CPU ms a frame and what it allocates a frame, the costliest first, those over 0.05 ms.</summary>
    public string Report(int most = 14) => _frames == 0 ? "(no frames)" : string.Join(", ",
        Enumerable.Range(1, _names.Count - 1).Where(i => _sum[i] / _frames > 0.05).OrderByDescending(i => _sum[i])
            .Take(most).Select(i => $"{_names[i]} {_sum[i] / _frames:F2} (worst {_worst[i]:F1}, {_bytes[i] / 1024.0 / _frames:F0} KB)"));
}
