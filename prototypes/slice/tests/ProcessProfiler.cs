using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using PlanetSurface;

namespace Slice.Tests;

/// <summary>
/// What each kind of node costs the main thread, measured by taking it away: every node whose class
/// has its own _Process or _PhysicsProcess is grouped by class, and each group is switched off for a
/// stretch of frames and on again for the same, while a <see cref="FrameProbe"/> reads the frames. The
/// difference is the group's share of the frame, in wall-clock ms and in the main thread's own CPU
/// ms (the two part when the machine is busy). Used by the flight check with --profile, since it
/// reads the whole game at work, not one part of it.
/// </summary>
public sealed class ProcessProfiler
{
    sealed class Group
    {
        public string Name;
        public readonly List<Node> Nodes = new();
        public readonly List<(bool process, bool physics)> Was = new();
        public double OnWall, OnCpu, OffWall, OffCpu;
        public int OnFrames, OffFrames;
    }

    readonly FrameProbe _probe;
    readonly List<Group> _groups = new();
    readonly int _frames;
    int _index, _lastFrame = -1, _inPhase, _skip;
    bool _off;
    bool _done;

    public bool Done => _done;

    /// <summary>Frames each group is read on and off for, after a few settling frames at each switch.</summary>
    public ProcessProfiler(FrameProbe probe, Node root, int frames, params Node[] leave)
    {
        _probe = probe;
        _frames = frames;
        var skip = new HashSet<Node>(leave);
        var byType = new Dictionary<Type, Group>();
        void Walk(Node n)
        {
            if (!skip.Contains(n) && n is not FrameProbeEdge && DeclaresProcess(n.GetType()))
            {
                if (!byType.TryGetValue(n.GetType(), out Group g)) byType[n.GetType()] = g = new Group { Name = n.GetType().Name };
                g.Nodes.Add(n);
            }
            foreach (Node child in n.GetChildren()) Walk(child);
        }
        Walk(root);
        _groups.AddRange(byType.Values.OrderBy(g => g.Name));
    }

    static bool DeclaresProcess(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type t = type; t != null && t != typeof(Node) && t != typeof(GodotObject); t = t.BaseType)
            if (t.GetMethod("_Process", flags, null, new[] { typeof(double) }, null) != null || t.GetMethod("_PhysicsProcess", flags, null, new[] { typeof(double) }, null) != null)
                return true;
        return false;
    }

    void Switch(Group g, bool off)
    {
        if (off)
        {
            g.Was.Clear();
            foreach (Node n in g.Nodes)
            {
                bool alive = GodotObject.IsInstanceValid(n);
                g.Was.Add(alive ? (n.IsProcessing(), n.IsPhysicsProcessing()) : (false, false));
                if (!alive) continue;
                n.SetProcess(false);
                n.SetPhysicsProcess(false);
            }
        }
        else
        {
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                if (!GodotObject.IsInstanceValid(g.Nodes[i])) continue;
                g.Nodes[i].SetProcess(g.Was[i].process);
                g.Nodes[i].SetPhysicsProcess(g.Was[i].physics);
            }
        }
    }

    /// <summary>Call once a physics tick; reads each new frame the probe has seen.</summary>
    public void Step()
    {
        if (_done || _probe.Frames == _lastFrame) return;
        _lastFrame = _probe.Frames;
        if (_index >= _groups.Count)
        {
            _done = true;
            return;
        }
        Group g = _groups[_index];
        if (_skip > 0)
        {
            _skip--;
            return;
        }
        if (!_off)
        {
            g.OnWall += _probe.LastMs;
            g.OnCpu += _probe.LastCpuMs;
            g.OnFrames++;
        }
        else
        {
            g.OffWall += _probe.LastMs;
            g.OffCpu += _probe.LastCpuMs;
            g.OffFrames++;
        }
        if (++_inPhase < _frames) return;
        _inPhase = 0;
        _skip = 4;
        if (!_off)
        {
            _off = true;
            Switch(g, true);
        }
        else
        {
            Switch(g, false);
            _off = false;
            _index++;
        }
    }

    /// <summary>Puts every node back as it was (a run that ends part-way through a group).</summary>
    public void Restore()
    {
        if (_off && _index < _groups.Count) Switch(_groups[_index], false);
        _off = false;
    }

    /// <summary>Each group's cost to the main thread, biggest first: wall ms and CPU ms a frame, and how many nodes.</summary>
    public IEnumerable<string> Report()
    {
        foreach (Group g in _groups.Where(g => g.OnFrames > 0 && g.OffFrames > 0)
                     .OrderByDescending(g => g.OnCpu / g.OnFrames - g.OffCpu / g.OffFrames))
        {
            double wall = g.OnWall / g.OnFrames - g.OffWall / g.OffFrames, cpu = g.OnCpu / g.OnFrames - g.OffCpu / g.OffFrames;
            yield return $"{g.Name} x{g.Nodes.Count}: {cpu:F2} ms cpu, {wall:F2} ms wall (on {g.OnWall / g.OnFrames:F2}, off {g.OffWall / g.OffFrames:F2})";
        }
    }
}
