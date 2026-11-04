using System;
using System.Collections.Generic;
using Godot;

namespace GalHack.Net;

/// <summary>Process-local clocks used to stamp and buffer network state.</summary>
public static class NetClock
{
    /// <summary>Milliseconds since this process started. Stamps travel as these (an int is small on the wire).</summary>
    public static int Ms => (int)Time.GetTicksMsec();

    /// <summary>Seconds since this process started, for buffering.</summary>
    public static double Now => Time.GetTicksUsec() / 1_000_000.0;
}

/// <summary>
/// An interpolation buffer: remote state is drawn a little in the past (<see cref="Delay"/>,
/// about 100 ms) so there are always two snapshots to blend between, even with jitter and the
/// odd lost packet.
///
/// Snapshots carry the sender's own clock. The buffer learns the offset between that clock and
/// ours from the fastest recent arrivals (a windowed minimum, which ignores jitter), and drifts
/// toward a new offset slowly, by at most 5% time-warp, so the picture never jumps.
/// </summary>
public sealed class SnapshotBuffer<T>
{
    /// <summary>How far behind the newest possible snapshot we draw, in seconds.</summary>
    public double Delay = 0.1;

    const int MaxItems = 64;
    const int OffsetWindow = 48;
    const double MaxWarp = 0.05;
    const double SnapOffset = 0.25;

    readonly List<(double time, T value)> _items = new();
    readonly double[] _offsets = new double[OffsetWindow];
    int _offsetCount, _offsetNext;
    double _targetOffset, _offset, _lastSampleAt;
    bool _started;

    public int Count => _items.Count;
    public bool HasData => _items.Count > 0;

    /// <summary>The newest snapshot, whatever the render time.</summary>
    public T Newest => _items.Count > 0 ? _items[^1].value : default;

    public void Clear()
    {
        _items.Clear();
        _offsetCount = _offsetNext = 0;
        _started = false;
        _lastSampleAt = 0;
    }

    /// <summary>Adds a snapshot stamped with the sender's clock (seconds), received at local time <paramref name="now"/>.</summary>
    public void Push(double senderTime, T value, double now)
    {
        _offsets[_offsetNext] = now - senderTime;
        _offsetNext = (_offsetNext + 1) % OffsetWindow;
        _offsetCount = Math.Min(_offsetCount + 1, OffsetWindow);
        double min = double.MaxValue;
        for (int i = 0; i < _offsetCount; i++) min = Math.Min(min, _offsets[i]);
        _targetOffset = min;
        if (!_started)
        {
            _offset = _targetOffset;
            _started = true;
        }

        // Usually the newest; insert in order if the network reordered it, drop duplicates.
        int at = _items.Count;
        while (at > 0 && _items[at - 1].time > senderTime) at--;
        if (at > 0 && _items[at - 1].time == senderTime) return;
        _items.Insert(at, (senderTime, value));
        if (_items.Count > MaxItems) _items.RemoveAt(0);
    }

    /// <summary>
    /// Samples the buffer at local time <paramref name="now"/>. Gives the two snapshots around
    /// the render time and how far between them it is. Past the newest snapshot, both are the
    /// newest and <c>Extra</c> says how far beyond it (for extrapolation).
    /// </summary>
    public bool TrySample(double now, out Sample sample)
    {
        sample = default;
        if (_items.Count == 0) return false;

        double dt = _lastSampleAt > 0 ? Math.Max(0, now - _lastSampleAt) : 0;
        _lastSampleAt = now;
        double drift = _targetOffset - _offset;
        if (Math.Abs(drift) > SnapOffset) _offset = _targetOffset;
        else _offset += Math.Clamp(drift, -MaxWarp * dt, MaxWarp * dt);

        double renderTime = now - _offset - Delay;
        sample.RenderTime = renderTime;

        // Forget snapshots we have moved well past, keeping one before the render time.
        while (_items.Count > 2 && _items[1].time <= renderTime) _items.RemoveAt(0);

        if (renderTime <= _items[0].time)
        {
            sample.A = sample.B = _items[0].value;
            sample.TimeA = sample.TimeB = _items[0].time;
            return true;
        }
        for (int i = 0; i < _items.Count - 1; i++)
        {
            var (ta, a) = _items[i];
            var (tb, b) = _items[i + 1];
            if (renderTime >= ta && renderTime < tb)
            {
                sample.A = a;
                sample.B = b;
                sample.TimeA = ta;
                sample.TimeB = tb;
                sample.Alpha = (float)((renderTime - ta) / (tb - ta));
                return true;
            }
        }
        var (tn, newest) = _items[^1];
        sample.A = sample.B = newest;
        sample.TimeA = sample.TimeB = tn;
        sample.Extra = renderTime - tn;
        return true;
    }

    public struct Sample
    {
        public T A, B;
        public float Alpha;
        /// <summary>Seconds past the newest snapshot (0 while interpolating).</summary>
        public double Extra;
        /// <summary>The sender-clock time being drawn.</summary>
        public double RenderTime;
        public double TimeA, TimeB;
    }
}
