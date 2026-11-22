using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Godot;
using PlanetSurface;

namespace Slice.Tests;

/// <summary>
/// What each kind of node costs the main thread, timed where it is spent: every node whose class has its own
/// _Process or _PhysicsProcess and is processing is taken over for a stretch of frames, and its callbacks are
/// made from here, in the engine's own order (priority, then the tree's), each one timed. So every kind is read
/// on the same frames, with nothing switched off: per kind, its mean a frame, the most it took in any one frame
/// (what a spike is made of), and how many of its nodes run under a parked branch (a disabled ancestor), which
/// should be none. Used by the flight checks with --profile, since it reads the whole game at work.
/// </summary>
public sealed partial class ProcessProfiler
{
    sealed class Group
    {
        public string Name;
        public int Nodes, Parked;
        public double Ms, Worst, ThisFrame;
    }

    sealed record Taken(Node Node, Group Group, bool Process, bool Physics);

    readonly FrameProbe _probe;
    readonly int _frames;
    readonly List<Taken> _process = new(), _physics = new();
    readonly Dictionary<Type, Group> _groups = new();
    readonly Pass _pass;
    int _lastFrame = -1, _read;
    double _probeCpu;

    public bool Done { get; private set; }

    /// <summary>A short look (a second or two) is about a hitch: its kinds go by their worst frame, not their mean.</summary>
    bool ByWorst => _frames < 120;

    /// <summary>Takes over every processing node under <paramref name="root"/> but <paramref name="leave"/> for <paramref name="frames"/> frames.</summary>
    public ProcessProfiler(FrameProbe probe, Node root, int frames, params Node[] leave)
    {
        _probe = probe;
        _frames = frames;
        var skip = new HashSet<Node>(leave);
        var found = new List<Node>();
        void Walk(Node n)
        {
            if (!skip.Contains(n) && n is not FrameProbeEdge && DeclaresProcess(n.GetType())) found.Add(n);
            foreach (Node child in n.GetChildren()) Walk(child);
        }
        Walk(root);
        foreach (Node n in found)
        {
            if (!_groups.TryGetValue(n.GetType(), out Group g)) _groups[n.GetType()] = g = new Group { Name = n.GetType().Name };
            g.Nodes++;
            if (n.CanProcess() && UnderParked(n)) g.Parked++;
            bool process = n.IsProcessing(), physics = n.IsPhysicsProcessing();
            if (process) _process.Add(new Taken(n, g, true, false));
            if (physics) _physics.Add(new Taken(n, g, false, true));
            n.SetProcess(false);
            n.SetPhysicsProcess(false);
        }
        // The engine's order: priority first, the tree's order among equals (a stable sort keeps the walk's).
        Sort(_process, n => n.ProcessPriority);
        Sort(_physics, n => n.ProcessPhysicsPriority);
        _pass = new Pass { Profiler = this };
        (leave.FirstOrDefault() ?? root).AddChild(_pass);
    }

    static void Sort(List<Taken> list, Func<Node, int> priority)
    {
        var sorted = list.Select((t, i) => (t, i)).OrderBy(p => priority(p.t.Node)).ThenBy(p => p.i).Select(p => p.t).ToList();
        list.Clear();
        list.AddRange(sorted);
    }

    /// <summary>Running though a branch over it is disabled: something a parked zone holds that still processes.</summary>
    static bool UnderParked(Node n)
    {
        for (Node p = n.GetParent(); p != null; p = p.GetParent())
            if (p.ProcessMode == Node.ProcessModeEnum.Disabled) return true;
        return false;
    }

    static bool DeclaresProcess(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type t = type; t != null && t != typeof(Node) && t != typeof(GodotObject); t = t.BaseType)
            if (t.GetMethod("_Process", flags, null, new[] { typeof(double) }, null) != null || t.GetMethod("_PhysicsProcess", flags, null, new[] { typeof(double) }, null) != null)
                return true;
        return false;
    }

    void Call(List<Taken> list, double delta)
    {
        if (Done) return;
        foreach (Taken t in list)
        {
            if (!GodotObject.IsInstanceValid(t.Node) || !t.Node.CanProcess()) continue;
            long started = Stopwatch.GetTimestamp();
            try
            {
                if (t.Process) t.Node._Process(delta);
                else t.Node._PhysicsProcess(delta);
            }
            catch (Exception e) { GD.PushError($"{t.Group.Name}: {e}"); }
            t.Group.ThisFrame += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
    }

    /// <summary>Call once a physics tick; reads each new frame the probe has seen, and hands the nodes back when done.</summary>
    public void Step()
    {
        if (Done || _probe.Frames == _lastFrame) return;
        _lastFrame = _probe.Frames;
        foreach (Group g in _groups.Values)
        {
            g.Ms += g.ThisFrame;
            g.Worst = Math.Max(g.Worst, g.ThisFrame);
            g.ThisFrame = 0;
        }
        _probeCpu += _probe.LastCpuMs;
        if (++_read >= _frames) Restore();
    }

    /// <summary>Hands every node back to the engine, processing as it was.</summary>
    public void Restore()
    {
        if (Done) return;
        Done = true;
        foreach (Taken t in _process) if (GodotObject.IsInstanceValid(t.Node)) t.Node.SetProcess(true);
        foreach (Taken t in _physics) if (GodotObject.IsInstanceValid(t.Node)) t.Node.SetPhysicsProcess(true);
        if (GodotObject.IsInstanceValid(_pass)) _pass.QueueFree();
    }

    /// <summary>Each kind's cost to the main thread, biggest first: ms a frame, its worst frame, how many nodes (and how many under a parked branch).</summary>
    public IEnumerable<string> Report()
    {
        int frames = Math.Max(_read, 1);
        yield return $"{_read} frames, the main thread's CPU {_probeCpu / frames:F2} ms a frame, of it timed here {_groups.Values.Sum(g => g.Ms) / frames:F2} ms";
        foreach (Group g in _groups.Values.Where(g => g.Ms > 0).OrderByDescending(g => ByWorst ? g.Worst : g.Ms))
            yield return $"{g.Name} x{g.Nodes}{(g.Parked > 0 ? $" ({g.Parked} under a parked branch)" : "")}: {g.Ms / frames:F2} ms a frame, worst {g.Worst:F1}";
    }

    /// <summary>Makes the taken-over callbacks in the process and physics passes.</summary>
    sealed partial class Pass : Node
    {
        public ProcessProfiler Profiler;
        public override void _Process(double delta) => Profiler.Call(Profiler._process, delta);
        public override void _PhysicsProcess(double delta) => Profiler.Call(Profiler._physics, delta);
    }
}
