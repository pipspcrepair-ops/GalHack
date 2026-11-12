using System;
using Godot;
using Wildlife;

namespace Slice.Surface;

/// <summary>
/// The spawn lead grows with your speed: on a bike or flying low, something put down just past the
/// least spawn distance would come into view a second later, nearer than that. So the faster you go,
/// the further out (and the more so ahead of you) anything is put down, so it is in place before you
/// could see it.
/// </summary>
public partial class WildlifePatch
{
    /// <summary>How many seconds of your travel the lead covers.</summary>
    const float LeadSeconds = 2.5f;

    Vector3 _velocity, _lastWorld;
    bool _tracked;

    void TrackTravel(float dt)
    {
        if (Player == null || dt <= 0f) return;
        Vector3 at = FromPatch(Player.Position);
        if (_tracked)
        {
            Vector3 v = WildSim.Flat(at - _lastWorld) / dt;
            // A teleport or a patch shift is no speed.
            if (v.Length() < 400f) _velocity = _velocity.Lerp(v, MathF.Min(1f, dt * 4f));
        }
        _lastWorld = at;
        _tracked = true;
    }

    /// <summary>The extra distance a spawn this way from you (flat, normalised) must be put down at, m.</summary>
    public float Lead(Vector3 dir)
    {
        float speed = _velocity.Length();
        if (speed < 2f) return 0f;
        float ahead = _velocity.Dot(dir) / speed;
        return speed * LeadSeconds * MathF.Max(0.3f, ahead);
    }
}
