using System;
using System.Collections.Generic;
using Godot;

namespace GalHack.Net;

/// <summary>
/// Process-local clocks. Network state is stamped and buffered in <b>simulation</b> time: the
/// physics tick a state belongs to. A stamp then always matches its state exactly, however late,
/// bunched or skipped the ticks run when the machine is busy (a wall-clock stamp taken during a
/// catch-up burst would give three different states nearly the same time). Real time is only
/// for people waiting: timeouts, pings, reconnect delays.
/// </summary>
public static class NetClock
{
    /// <summary>The physics tick this process is on. Stamps travel as these (an int is small on the wire).</summary>
    public static int Tick => (int)Engine.GetPhysicsFrames();

    /// <summary>Seconds of simulation so far: ticks × the tick length.</summary>
    public static double Now => Engine.GetPhysicsFrames() / (double)Engine.PhysicsTicksPerSecond;

    /// <summary>A tick stamp in seconds of simulation.</summary>
    public static double Seconds(int tick) => tick / (double)Engine.PhysicsTicksPerSecond;

    /// <summary>Seconds since the process started, in real time. For timeouts and pings only.</summary>
    public static double RealNow => Time.GetTicksUsec() / 1_000_000.0;
}

/// <summary>
/// An interpolation buffer: remote state is drawn a little in the past (<see cref="Delay"/>,
/// about 100 ms) so there are always two snapshots to blend between, even with jitter and the
/// odd lost packet.
///
/// Snapshots carry the sender's own clock. The buffer learns the offset between that clock and
/// ours from the fastest recent arrivals (a windowed minimum, which ignores jitter), and drifts
/// toward a new offset slowly, by at most 5% time-warp, so the picture never jumps.
///
/// When snapshots stop coming (a lost burst, or a sender that stalled on a busy machine), the
/// buffer glides on along the last motion for <see cref="MaxExtrapolation"/>, and then its clock
/// waits for the data rather than running on into nothing (<see cref="HoldWhenStarved"/>). When
/// the data comes, drawing carries on from where it was, with no jump, and the time-warp
/// catches back up.
/// </summary>
public sealed class SnapshotBuffer<T>
{
    /// <summary>How far behind the newest possible snapshot we draw, in seconds.</summary>
    public double Delay = 0.1;

    const int MaxItems = 64;
    const int OffsetWindow = 48;
    const double MaxWarp = 0.05;
    const double SnapOffset = 1.0; // further behind than this, catching up by time-warp takes too long: jump

    /// <summary>
    /// Stop the clock when starved (continuous streams: players, ships). Off for streams that
    /// legitimately go quiet, such as cargo at rest (once a second), which should just hold.
    /// </summary>
    public bool HoldWhenStarved = true;

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
    /// the render time and how far between them it is. Past the newest snapshot, A and B are
    /// the last two, <c>Alpha</c> is above 1 (so an unclamped lerp extrapolates, for at most
    /// <see cref="MaxExtrapolation"/>), and <c>Extra</c> says how far beyond the newest it is.
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
        double limit = _items[^1].time + MaxExtrapolation;
        if (HoldWhenStarved && renderTime > limit)
        {
            _offset += renderTime - limit; // wait here for the data
            renderTime = limit;
        }
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
        // Past the newest snapshot (late packets): carry on along the last two for a moment
        // (Alpha above 1), so a short gap is a glide rather than a stop and a jump.
        var (tn, newest) = _items[^1];
        sample.Extra = renderTime - tn;
        sample.B = newest;
        sample.TimeB = tn;
        if (_items.Count >= 2 && tn > _items[^2].time)
        {
            var (tp, previous) = _items[^2];
            sample.A = previous;
            sample.TimeA = tp;
            sample.Alpha = (float)(1.0 + Math.Min(sample.Extra, MaxExtrapolation) / (tn - tp));
        }
        else
        {
            sample.A = newest;
            sample.TimeA = tn;
            sample.Alpha = 1f;
        }
        return true;
    }

    /// <summary>How far past the newest snapshot <see cref="Sample.Alpha"/> may run, in seconds.</summary>
    public const double MaxExtrapolation = 0.1;

    public struct Sample
    {
        public T A, B;
        public float Alpha;
        /// <summary>Seconds past the newest snapshot (0 while interpolating).</summary>
        public double Extra;
        /// <summary>The sender-clock time being drawn.</summary>
        public double RenderTime;
        public double TimeA, TimeB;

        /// <summary>
        /// The sender-clock time a blend at <paramref name="alpha"/> actually shows. It is the
        /// render time while interpolating, and lags it when the buffer runs dry and holds.
        /// </summary>
        public readonly double TimeAt(float alpha) => TimeB > TimeA ? TimeA + alpha * (TimeB - TimeA) : TimeB;
    }
}
