using System;
using Godot;
using PoiGenerator.Gen;
using Wildlife;

namespace Slice.Places;

/// <summary>
/// The town reading your gestures by the same rules its people use among themselves
/// (<see cref="Gestures"/>): whoever is near enough and can see you answers in their own time,
/// with their body (a wave back, wares shown, a warning, hands up), and does something about it:
/// comes over, keeps back, lowers a gun, looks where you point.
/// </summary>
public sealed partial class Town
{
    /// <summary>Gestures read and answered by somebody here (tests).</summary>
    public int Answered { get; private set; }
    public PlayerGesture? LastHeard { get; private set; }

    const float SeeGesture = 24f;

    public void Heard(PlayerGesture g, Vector3 eyeWorld, Vector3 lookWorld)
    {
        if (_place == null || !Rolled) return;
        Vector3 eye = _place.FromWorld(eyeWorld);
        Vector3 pointAt = _place.FromWorld(eyeWorld + lookWorld * 12f);
        bool hostile = Memory.Mood == Mood.Hostile;
        Stance stance = _temper?.Stance ?? Stance.Welcoming;
        LastHeard = g;
        int heard = 0;
        foreach (Person p in People)
        {
            if (p.Dead || p.Gone || p.Indoors || p.Away || p.Looks == null) continue;
            Vector3 d = eye - p.Pos;
            float away = new Vector2(d.X, d.Z).Length();
            if (away > SeeGesture) continue;
            // They see it if you're close, or in front of them.
            Vector3 facing = new(-MathF.Sin(p.Yaw), 0f, -MathF.Cos(p.Yaw));
            if (away > 6f && facing.Dot(new Vector3(d.X, 0f, d.Z) / away) < -0.2f) continue;
            var (reply, effect) = Gestures.Answer(g, stance, p.Job, Standing, hostile, p.Nerve);
            p.Answer = reply;
            p.AnswerAt = _clock + 0.25f + (1f - p.Nerve) * 0.5f + away * 0.02f;
            p.AnswerFor = 2.6f;
            heard++;
            switch (effect)
            {
                case GestureEffect.Calm:
                    // Hands up: guns come down; the frightened settle.
                    if (p.Aim >= 0f) { p.Aim = -1f; p.Cooldown = 4f; }
                    p.Holding = false;
                    p.ScaredFor = 0f;
                    break;
                case GestureEffect.Alarm:
                    if (Weapons.Ranged(p.Figure.Prop)) p.Holding = true;
                    break;
                case GestureEffect.Back:
                    p.ScaredFor = MathF.Max(p.ScaredFor, p.Figure.Stage == LifeStage.Child ? 6f : 2f);
                    break;
                case GestureEffect.Keep:
                    p.Path = null;
                    p.Step = 0;
                    p.Wait = MathF.Max(p.Wait, 4f);
                    break;
                case GestureEffect.Come:
                    // Over to the spot nearest you.
                    int near = -1;
                    float best = float.MaxValue;
                    for (int i = 0; i < Spots.Count; i++)
                    {
                        float k = (At(i) - eye).LengthSquared();
                        if (k < best) { best = k; near = i; }
                    }
                    if (near >= 0) { GoTo(p, near); p.Wait = 6f; }
                    break;
                case GestureEffect.Look:
                    p.LookAt = pointAt;
                    p.LookFor = 3f;
                    break;
            }
        }
        if (heard == 0) return;
        Answered++;
        // A threat costs the place's goodwill; thanks after a trade earns a little.
        if (g == PlayerGesture.Threaten) Shift(-4f, Deed.Threat);
        if (g == PlayerGesture.Thanks) Shift(1f, Deed.Thanks);
    }
}
