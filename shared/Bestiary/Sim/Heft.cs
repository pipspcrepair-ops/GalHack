using System;
using Godot;

namespace Wildlife;

/// <summary>
/// How a body's size weighs on the way it moves: a small thing darts and pivots; something the
/// size of a strider takes a second or two to get going, more to stop, and swings wide at speed.
/// </summary>
public static class Heft
{
    /// <summary>Seconds from standing to top speed.</summary>
    public static float TopSpeedIn(Species s) => Math.Clamp(0.15f + 0.22f * s.Body.Size, 0.2f, 3.5f);

    public static float Accel(Creature c) => MathF.Max(0.5f, c.Stats.RunSpeed) / TopSpeedIn(c.Species);

    /// <summary>Braking: a big body gives up its speed slower than it gained it.</summary>
    public static float Brake(Creature c) => Accel(c) * Mathf.Lerp(1f, 0.6f, Bulk(c.Species));

    /// <summary>Radians a second: small things turn on the spot; a big one turns slowly, and more slowly the faster it is going.</summary>
    public static float TurnRate(Creature c, float speed)
    {
        float standing = Math.Clamp(5f / MathF.Pow(MathF.Max(0.6f, c.Species.Body.Size), 0.8f), 0.6f, 7f);
        float running = Math.Clamp(speed / MathF.Max(0.5f, c.Stats.RunSpeed), 0f, 1f);
        return standing * Mathf.Lerp(1f, 0.45f, running * Bulk(c.Species));
    }

    /// <summary>How far a body going this fast needs to stop, m.</summary>
    public static float StoppingDistance(Creature c, float speed) => speed * speed / (2f * Brake(c));

    /// <summary>A rough bulk, m3: what it has to hold up in the air.</summary>
    public static float Weight(Species s) => s.Body.Size * s.Body.Width * s.Body.Height;

    /// <summary>A glider's stall: it can't fly slower than this, m/s, however it wants to. It grows with the root of its size, as wing loading does.</summary>
    public static float MinAirspeed(Species s) => Math.Clamp(3f * MathF.Sqrt(s.Body.Size), 2.5f, 7f);

    /// <summary>Its easy speed on a long glide, m/s.</summary>
    public static float CruiseAirspeed(Creature c) => MathF.Max(MinAirspeed(c.Species) * 1.35f, c.Stats.WalkSpeed);

    /// <summary>The fastest it flies on the level, m/s.</summary>
    public static float MostAirspeed(Creature c) => MathF.Max(CruiseAirspeed(c) * 1.3f, c.Stats.RunSpeed);

    /// <summary>How fast it comes down a swoop, m/s: a big body comes slower.</summary>
    public static float SwoopSpeed(Creature c) => MathF.Max(CruiseAirspeed(c) * 1.3f, 13f - 2.2f * c.Species.Body.Size);

    /// <summary>The radius of a glider's tightest turn, m: wider with size and weight, the same at any speed (so a fast one takes longer to come round).</summary>
    public static float TurnRadius(Species s) => 1.5f + 2.6f * s.Body.Size + 2.5f * MathF.Cbrt(Weight(s));

    /// <summary>How fast flapping lifts it, m/s.</summary>
    public static float ClimbRate(Species s) => Math.Clamp(3.8f - 0.9f * s.Body.Size, 1.5f, 3.4f);

    /// <summary>How fast it sinks on a glide with its wings held out, m/s.</summary>
    public static float SinkRate(Species s) => Math.Clamp(0.35f + 0.12f * s.Body.Size, 0.35f, 0.9f);

    /// <summary>How high its middle is above the ground, standing (the rig holds it there).</summary>
    public static float StandHeight(Species s) => s.Body.Height * 0.5f + s.Body.Width * 0.25f;

    /// <summary>What it weighs, kg: the one number big things sound and move by. A person's is their own; anything else, its bulk at about the density of flesh.</summary>
    public static float Mass(Species s) => s.Figure != null ? s.Figure.Mass * (s.Mechanical ? 3f : 1f) : MathF.Max(0.1f, Weight(s) * (s.Coat == Coat.Plating ? 700f : 420f));

    /// <summary>How low its sounds are pitched: the heavier, the deeper, by the same number that slows its turns.</summary>
    public static float Pitch(Species s) => Math.Clamp(1.3f * MathF.Pow(Mass(s) / 60f, -0.17f), 0.4f, 1.9f);

    /// <summary>How loud it is, dB against a person: a few dB for each doubling of weight.</summary>
    public static float Loudness(Species s) => Math.Clamp(2f + 1.25f * MathF.Log2(MathF.Max(1f, Mass(s)) / 60f), -5f, 11f);

    /// <summary>0 for anything up to about a metre, 1 from five metres.</summary>
    public static float Bulk(Species s) => Math.Clamp((s.Body.Size - 1f) / 4f, 0f, 1f);
}
