using System;
using Godot;

namespace Slice.Surface;

/// <summary>
/// What the weather's sound should hear of where you stand: how much roof is over the eye, how walled
/// in it is, whether the ground is a ridge or a hollow, and whether the wind's upwind side is blocked.
/// A few level and upward rays from the eye and a ring of ground heights, five times a second, eased.
/// <see cref="RainLevel"/> and <see cref="WindLevel"/> turn those and the weather into loudness.
/// </summary>
public partial class AmbientExposure : Node3D
{
    public SurfaceZone Zone;
    public Func<Vector3> Eye;

    /// <summary>Roof over the eye, 0 (open sky) to 1 (solid above).</summary>
    public float Cover { get; private set; }

    /// <summary>Walls around the eye within a few metres, 0 to 1.</summary>
    public float Walled { get; private set; }

    /// <summary>How much higher the ground here is than the ring of ground 60 m round it, m: ridges positive, hollows negative.</summary>
    public float Relief { get; private set; }

    /// <summary>The upwind side is blocked (terrain or buildings within 40 m), 0 to 1.</summary>
    public float Lee { get; private set; }

    /// <summary>Tests: holds the measurements still at a staged value instead of surveying.</summary>
    public bool Held;

    const float Poll = 0.2f;
    float _timer;
    const int Round = 8;

    public void Set(float cover, float walled, float relief, float lee)
    {
        Held = true;
        (Cover, Walled, Relief, Lee) = (cover, walled, relief, lee);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Held || Zone == null || Eye == null) return;
        if ((_timer -= (float)delta) > 0f) return;
        _timer = Poll;
        Survey();
    }

    /// <summary>Measures now (from a physics step).</summary>
    public void Survey()
    {
        Vector3 eye = Eye();
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        const uint Solid = GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull;
        bool Blocked(Vector3 to) => space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, to, Solid)).Count > 0;

        // Roof: straight up counts most, four slanted rays catch an overhang or a doorway's lintel.
        float cover = Blocked(eye + Vector3.Up * 25f) ? 0.5f : 0f;
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.Tau / 4f;
            if (Blocked(eye + new Vector3(MathF.Cos(a) * 0.64f, 0.77f, MathF.Sin(a) * 0.64f) * 14f)) cover += 0.125f;
        }
        float walled = 0f;
        for (int i = 0; i < Round; i++)
        {
            float a = i * MathF.Tau / Round;
            if (Blocked(eye + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * 7f)) walled += 1f / Round;
        }
        float lee = 0f;
        Vector3 wind = Zone.Weather.Sim?.WindAt(eye) ?? Vector3.Zero;
        if (wind.Length() > 0.5f)
        {
            Vector3 upwind = -wind.Normalized();
            foreach (float h in new[] { 0f, 3f, 8f })
                if (Blocked(eye + upwind * 40f + Vector3.Up * h)) lee += 1f / 3f;
        }
        float ground = Zone.GroundAt(eye.X, eye.Z).Y, ring = 0f;
        for (int i = 0; i < Round; i++)
        {
            float a = i * MathF.Tau / Round;
            ring += Zone.GroundAt(eye.X + MathF.Cos(a) * 60f, eye.Z + MathF.Sin(a) * 60f).Y / Round;
        }
        float relief = Mathf.Clamp(ground - ring, -40f, 40f);
        const float K = 0.5f; // eased: a step through a door is not a click
        Cover += (cover - Cover) * K;
        Walled += (walled - Walled) * K;
        Lee += (lee - Lee) * K;
        Relief += (relief - Relief) * K;
    }

    /// <summary>Rain's loudness, 0 to 1: the intensity falling here, full in the open, muffled under a roof, none in a shut room.</summary>
    public static float RainLevel(float intensity, float cover, float walled, bool shut, bool cockpit)
    {
        if (shut) return 0f;
        float r = Mathf.Clamp(intensity, 0f, 1f) * (1f - cover * (0.7f + 0.15f * walled));
        return cockpit ? r * 0.6f : r;
    }

    /// <summary>Wind's loudness, 0 to 1.3: the local speed, up on a ridge, down in a hollow or a lee, muffled by roof and walls, none in a shut room.</summary>
    public static float WindLevel(float speed, float cover, float walled, float relief, float lee, bool shut, bool cockpit)
    {
        if (shut) return 0f;
        float w = Mathf.Clamp(speed / 30f, 0f, 1.3f);
        w *= Mathf.Clamp(1f + relief / 40f, 0.45f, 1.35f);
        w *= 1f - 0.6f * lee;
        w *= 1f - Mathf.Min(0.92f, 0.55f * cover + 0.4f * walled);
        return cockpit ? w * 0.6f : w;
    }
}
