using System;
using Godot;

namespace Slice.Surface;

/// <summary>
/// Wind howling at openings: where you are closed in and a gap lets the outside through (a cave
/// mouth, a doorway, a hole in a wall), a windy day moans in that gap. A fan of level rays from the
/// eye finds it, twice a second: a long clear line with solid close by on both sides of it. The
/// sound sits in the opening and comes up with the wind, synthesised, no audio files.
/// </summary>
public partial class WindHowl : Node3D
{
    const int Rays = 16;
    const float Reach = 28f, FarEnough = 14f, NearWall = 11f, Survey = 0.5f, LeastWind = 9f, FullWind = 34f;

    /// <summary>Directions of the fan that must be walled for this to be somewhere closed in: not just two trees.</summary>
    const int WalledAtLeast = 7;

    public SurfaceZone Zone;
    public Func<Vector3> Eye;

    /// <summary>Nothing to hear (in space, out of the air).</summary>
    public Func<bool> Quiet = () => false;

    /// <summary>The opening heard now, if any.</summary>
    public Vector3? Opening { get; private set; }

    /// <summary>How loud it is, 0 to 1.</summary>
    public float Level { get; private set; }

    AudioStreamPlayer3D _sound;
    readonly float[] _hits = new float[Rays];
    float _timer, _target;

    public override void _Ready()
    {
        _sound = new AudioStreamPlayer3D { Name = "Howl", Stream = Moan(), UnitSize = 9f, MaxDistance = 45f, VolumeDb = -60f };
        AddChild(_sound);
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if ((_timer -= dt) <= 0f)
        {
            _timer = Survey;
            Look();
        }
        Level = Mathf.MoveToward(Level, _target, dt * 0.8f);
        if (Level < 0.01f)
        {
            if (_sound.Playing) _sound.Stop();
            return;
        }
        if (Opening is Vector3 at) _sound.GlobalPosition = _sound.GlobalPosition.Lerp(at, Mathf.Min(1f, dt * 2f));
        _sound.VolumeDb = Mathf.LinearToDb(Level) - 7f;
        _sound.PitchScale = 0.85f + 0.4f * Level;
        if (!_sound.Playing) _sound.Play();
    }

    void Look()
    {
        float wind = Zone.Weather.Wind2D.Length();
        if (wind < LeastWind || Quiet() || Zone.Underwater.CameraUnder)
        {
            _target = 0f;
            return;
        }
        Vector3 eye = Eye();
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        for (int i = 0; i < Rays; i++)
        {
            float a = i * MathF.Tau / Rays;
            var ray = PhysicsRayQueryParameters3D.Create(eye, eye + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * Reach, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull);
            var hit = space.IntersectRay(ray);
            _hits[i] = hit.Count == 0 ? Reach : eye.DistanceTo((Vector3)hit["position"]);
        }
        int gap = FindGap(_hits, out float across);
        if (gap < 0)
        {
            Opening = null;
            _target = 0f;
            return;
        }
        float heading = gap * MathF.Tau / Rays;
        Vector3 at = eye + new Vector3(MathF.Cos(heading), 0f, MathF.Sin(heading)) * across;
        if (Opening == null) _sound.GlobalPosition = at;
        Opening = at;
        _target = Mathf.Clamp((wind - LeastWind) / (FullWind - LeastWind), 0.2f, 1f);
    }

    /// <summary>
    /// The direction of the widest-reaching opening in a ring of ray lengths, or -1: enough of the ring
    /// walled in, and a clear line with a wall close by on each side. Gives how far off the walls
    /// are: that's where the opening stands.
    /// </summary>
    public static int FindGap(float[] hits, out float across)
    {
        across = 0f;
        int n = hits.Length, walled = 0;
        foreach (float d in hits)
            if (d < NearWall) walled++;
        if (walled < WalledAtLeast) return -1;
        int best = -1;
        float reach = 0f;
        for (int i = 0; i < n; i++)
        {
            if (hits[i] < FarEnough || hits[i] <= reach) continue;
            // Each way round to the first wall, no further than a quarter turn: a gap has sides.
            float left = Side(hits, i, -1), right = Side(hits, i, 1);
            if (left < 0f || right < 0f) continue;
            best = i;
            reach = hits[i];
            across = MathF.Max(left, right);
        }
        return best;
    }

    static float Side(float[] hits, int from, int step)
    {
        int n = hits.Length;
        for (int k = 1; k <= n / 4; k++)
        {
            float d = hits[((from + k * step) % n + n) % n];
            if (d < NearWall) return d;
        }
        return -1f;
    }

    /// <summary>A hollow moan: noise through a narrow resonance that wanders, in slow swells. Loops.</summary>
    static AudioStreamWav Moan()
    {
        const int Rate = 22050;
        const float Seconds = 4f;
        int n = (int)(Seconds * Rate);
        var data = new byte[n * 2];
        var rng = new Random(23);
        float y1 = 0f, y2 = 0f, z1 = 0f, z2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float x = (float)rng.NextDouble() * 2f - 1f;
            float f = 330f + 90f * MathF.Sin(MathF.Tau * 0.5f * t) + 45f * MathF.Sin(MathF.Tau * 1.25f * t);
            float r = 0.996f, w = MathF.Tau * f / Rate;
            float y = x * 0.02f + 2f * r * MathF.Cos(w) * y1 - r * r * y2;
            y2 = y1;
            y1 = y;
            float w2 = w * 1.5f; // a thinner fifth over it
            float z = x * 0.012f + 2f * r * MathF.Cos(w2) * z1 - r * r * z2;
            z2 = z1;
            z1 = z;
            float swell = 0.7f + 0.3f * MathF.Sin(MathF.Tau * 0.25f * t);
            float fade = i > n * 0.96f ? (n - i) / (n * 0.04f) : i < n * 0.04f ? i / (n * 0.04f) : 1f;
            short s = (short)(Math.Clamp((y + z * 0.6f) * swell * fade * 0.5f, -1f, 1f) * 32000);
            data[2 * i] = (byte)s;
            data[2 * i + 1] = (byte)(s >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Data = data, LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopEnd = n };
    }
}
