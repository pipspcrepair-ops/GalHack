using System;

namespace GalHack.Music;

/// <summary>
/// The long shape of a piece, over tens of minutes: thin stretches, slow rises, full passages, the
/// falls after them, and now and then near silence, because quiet is part of it. Each section lasts
/// minutes, at the world's own pace, and everything eases, so no one moment is ever a change.
/// </summary>
internal sealed class Arc
{
    enum Section { Thin, Rise, Full, Fall, Rest }

    /// <summary>What a rest keeps of the music: 26 dB down, the room still faintly there. Reached at a steady rate in decibels, as a slow fade should be, so no stretch of it is steeper than another.</summary>
    const float RestDb = -26f, DbPerSecond = 0.42f;
    const float DensitySeconds = 70f;

    readonly float _pace, _rest;
    Section _section;
    float _left, _db;

    /// <summary>How much is going on, 0 to 1: how far the stack opens, how often anything sounds.</summary>
    public float Density { get; private set; }

    /// <summary>Level, 1 unless the piece has fallen quiet.</summary>
    public float Presence { get; private set; } = 1f;

    public Arc(float pace, float rest, DroneRng rng)
    {
        _pace = pace;
        _rest = rest;
        // A piece can start anywhere but in silence.
        _section = rng.Pick(new[] { Section.Thin, Section.Rise, Section.Rise, Section.Full });
        _left = Length(_section, rng) * rng.Range(0.5f, 1f);
        Density = Aim(_section);
    }

    float Length(Section s, DroneRng rng) => _pace * s switch
    {
        Section.Thin => rng.Range(120f, 240f),
        Section.Rise => rng.Range(100f, 200f),
        Section.Full => rng.Range(150f, 320f),
        Section.Fall => rng.Range(90f, 180f),
        _ => rng.Range(100f, 180f),
    };

    static float Aim(Section s) => s switch
    {
        Section.Thin => 0.28f,
        Section.Rise => 0.62f,
        Section.Full => 1f,
        Section.Fall => 0.45f,
        _ => 0.1f,
    };

    public void Control(float dt, DroneRng rng)
    {
        _left -= dt;
        if (_left <= 0f)
        {
            _section = _section switch
            {
                Section.Thin => Section.Rise,
                Section.Rise => Section.Full,
                Section.Full => Section.Fall,
                Section.Fall => rng.Chance(_rest) ? Section.Rest : Section.Thin,
                _ => Section.Thin,
            };
            _left = Length(_section, rng);
        }
        Density += (Aim(_section) - Density) * dt / DensitySeconds;
        float aim = _section == Section.Rest ? RestDb : 0f, move = DbPerSecond * dt;
        _db += Math.Clamp(aim - _db, -move, move);
        Presence = MathF.Pow(10f, _db / 20f);
    }
}
