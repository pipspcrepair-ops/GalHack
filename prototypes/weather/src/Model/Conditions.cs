using System;
using System.Runtime.CompilerServices;
using Godot;

namespace Weather.Model;

/// <summary>
/// Every number the weather drives. Kept as one flat list so blending, rate limits and the
/// smoothness test treat them all the same way, with no per-parameter special cases.
/// </summary>
public enum Param
{
    CloudCover, CloudDarkness, Rain, Snow, Dust, Embers, GroundFog,
    /// <summary>log10 of visibility in metres: fog thickening from 10 km to 1 km should take as long as from 100 m to 10 m.</summary>
    VisibilityLog,
    /// <summary>Mean wind, m/s, blended as a vector so a change of direction never spins through zero suddenly.</summary>
    WindX, WindZ,
    Gustiness, Temperature, Solar, CreatureActivity, Lightning, Tornadoes, Debris, MoveSpeed, Exposure,
    /// <summary>How much of the rain falls from storm cells, 0 (everywhere alike) to 1 (only under them).</summary>
    Cellular,
}

[InlineArray(Conditions.Count)]
public struct ParamValues
{
    float _first;
}

/// <summary>
/// The weather at one moment: what the world, the HUD and the creature AI read. A value type
/// with an inline array, so ticking the model allocates nothing.
/// </summary>
public struct Conditions
{
    public const int Count = 20;

    ParamValues _values;

    public float this[Param p]
    {
        readonly get => _values[(int)p];
        set => _values[(int)p] = value;
    }

    public float this[int i]
    {
        readonly get => _values[i];
        set => _values[i] = value;
    }

    public readonly float CloudCover => this[Param.CloudCover];
    public readonly float CloudDarkness => this[Param.CloudDarkness];
    public readonly float Rain => this[Param.Rain];
    public readonly float Snow => this[Param.Snow];
    public readonly float Dust => this[Param.Dust];
    public readonly float Embers => this[Param.Embers];
    public readonly float GroundFog => this[Param.GroundFog];
    public readonly float Visibility => MathF.Pow(10f, this[Param.VisibilityLog]);
    public readonly Vector2 Wind => new(this[Param.WindX], this[Param.WindZ]);
    public readonly float WindSpeed => Wind.Length();
    public readonly float Gustiness => this[Param.Gustiness];
    public readonly float Temperature => this[Param.Temperature];
    public readonly float Solar => this[Param.Solar];
    public readonly float CreatureActivity => this[Param.CreatureActivity];
    public readonly float LightningPerMinute => this[Param.Lightning];
    public readonly float TornadoesPerMinute => this[Param.Tornadoes];
    public readonly float DebrisPerMinute => this[Param.Debris];
    public readonly float MoveSpeed => this[Param.MoveSpeed];
    public readonly float Exposure => this[Param.Exposure];
    public readonly float Cellular => this[Param.Cellular];

    /// <summary>The settled conditions of a weather state on a planet, with the wind blowing toward <paramref name="windHeading"/>.</summary>
    public static Conditions FromState(WeatherState s, float baseTemperature, float windHeading)
    {
        var c = new Conditions();
        c[Param.CloudCover] = s.CloudCover;
        c[Param.CloudDarkness] = s.CloudDarkness;
        c[Param.Rain] = s.Rain;
        c[Param.Snow] = s.Snow;
        c[Param.Dust] = s.Dust;
        c[Param.Embers] = s.Embers;
        c[Param.GroundFog] = s.GroundFog;
        c[Param.VisibilityLog] = MathF.Log10(MathF.Max(s.Visibility, 1f));
        c[Param.WindX] = MathF.Cos(windHeading) * s.WindSpeed;
        c[Param.WindZ] = MathF.Sin(windHeading) * s.WindSpeed;
        c[Param.Gustiness] = s.Gustiness;
        c[Param.Temperature] = baseTemperature + s.Temperature;
        c[Param.Solar] = s.Solar;
        c[Param.CreatureActivity] = s.CreatureActivity;
        c[Param.Lightning] = s.Lightning;
        c[Param.Tornadoes] = s.Tornadoes;
        c[Param.Debris] = s.Debris;
        c[Param.MoveSpeed] = s.MoveSpeed;
        c[Param.Exposure] = s.Exposure;
        c[Param.Cellular] = s.Cellular;
        return c;
    }

    public static Conditions Lerp(in Conditions a, in Conditions b, float t)
    {
        var c = new Conditions();
        for (int i = 0; i < Count; i++) c[i] = a[i] + (b[i] - a[i]) * t;
        return c;
    }

    /// <summary>
    /// How long a smoothstep blend from a to b must take so no parameter ever changes faster
    /// than its max rate. Smoothstep's steepest slope is 1.5× the average, at the midpoint.
    /// </summary>
    public static float RequiredSeconds(in Conditions a, in Conditions b, float[] maxRates)
    {
        float seconds = 0f;
        for (int i = 0; i < Count; i++)
            seconds = MathF.Max(seconds, 1.5f * MathF.Abs(b[i] - a[i]) / maxRates[i]);
        return seconds;
    }
}
