using System;
using Wildlife;

namespace Slice.Places;

/// <summary>What happened near them that moves a mood.</summary>
public enum MoodEvent { Shot, SawDeath, LostKin, Threat, Harm, Gift, Fire, Robbed }

/// <summary>What the mood would have them do about danger.</summary>
public enum MoodIntent { Stay, Flee, Fight }

/// <summary>
/// One person's mood, worked out from four feelings (fear, anger, grief, cheer) that events push up
/// and time lets settle again. Nerve scales how much fear and anger a thing stirs; a drunk is drunk
/// whatever else. The mood drives their pace, posture, which idle they pick, whether they run or
/// stand, and (see the voices) how they sound. Pure: no scene, so a test can fire events at one.
/// </summary>
public sealed class Feeling
{
    public float Fear, Anger, Grief, Cheer;

    /// <summary>Set once by whoever owns the person: their nerve (0 timid to 1 bold), whether they are drunk, whether they carry a gun.</summary>
    public float Nerve = 0.5f;
    public bool Drunk, Armed, Bound, WasDead;

    /// <summary>Seconds for each feeling to fall to a third of itself.</summary>
    const float FearTime = 15f, AngerTime = 30f, GrieveTime = 90f, CheerTime = 45f;

    /// <summary>Settles back over time; a gathering fire lifts the cheer a little.</summary>
    public void Step(float dt, bool atFire)
    {
        Fear = Settle(Fear, dt, FearTime);
        Anger = Settle(Anger, dt, AngerTime);
        Grief = Settle(Grief, dt, GrieveTime);
        Cheer = Settle(Cheer, dt, CheerTime);
        if (atFire) Cheer = MathF.Min(0.7f, Cheer + dt * 0.05f * (1f - Fear));
    }

    static float Settle(float v, float dt, float time) => v <= 0.001f ? 0f : v * MathF.Exp(-dt / time);

    /// <summary>Something happened, <paramref name="near"/> from 1 (right beside them) to 0 (out of earshot).</summary>
    public void Hit(MoodEvent e, float near = 1f)
    {
        near = Math.Clamp(near, 0f, 1f);
        float timid = 1.3f - Nerve, bold = 0.5f + Nerve;
        switch (e)
        {
            case MoodEvent.Shot: Fear += 0.5f * near * timid; if (Armed) Anger += 0.15f * near * bold; break;
            case MoodEvent.SawDeath: Fear += 0.4f * near * timid; Grief += 0.45f * near; break;
            case MoodEvent.LostKin: Grief += 0.9f * near; Fear += 0.2f * near; break;
            case MoodEvent.Threat: Fear += 0.45f * near * timid; Anger += 0.2f * near * bold; break;
            case MoodEvent.Harm: Fear += 0.6f * near * timid; Anger += 0.4f * near * bold; break;
            case MoodEvent.Robbed: Fear += 0.6f * timid; Anger += 0.5f * bold; Cheer = 0f; break;
            case MoodEvent.Gift: Cheer += 0.5f * near; Fear *= 0.6f; Anger *= 0.6f; break;
            case MoodEvent.Fire: Cheer += 0.1f * near; break;
        }
        Fear = Math.Clamp(Fear, 0f, 1f);
        Anger = Math.Clamp(Anger, 0f, 1f);
        Grief = Math.Clamp(Grief, 0f, 1f);
        Cheer = Math.Clamp(Cheer, 0f, 1f);
    }

    public PersonMood Mood
    {
        get
        {
            if (Fear >= 0.55f) return PersonMood.Scared;
            if (Anger >= 0.45f && Anger >= Fear) return PersonMood.Angry;
            if (Drunk) return PersonMood.Drunk;
            if (Grief >= 0.4f) return PersonMood.Grieving;
            if (Fear >= 0.2f) return PersonMood.Uneasy;
            if (Cheer >= 0.35f) return PersonMood.Content;
            return PersonMood.Calm;
        }
    }

    /// <summary>How their walking pace scales.</summary>
    public float Pace => Mood switch
    {
        PersonMood.Scared => 1.5f, PersonMood.Uneasy => 1.15f, PersonMood.Angry => 1.1f,
        PersonMood.Grieving => 0.75f, PersonMood.Drunk => 0.8f, PersonMood.Content => 0.95f, _ => 1f,
    };

    /// <summary>How hunched they stand, 0 upright to 1 bowed or shrunk in.</summary>
    public float Hunch => Mood switch
    {
        PersonMood.Scared => 0.6f, PersonMood.Grieving => 0.5f, PersonMood.Uneasy => 0.25f, PersonMood.Drunk => 0.2f, PersonMood.Angry => 0.1f, _ => 0f,
    };

    /// <summary>Whether they would run, stand or fight if danger were on them.</summary>
    public MoodIntent Intent => Mood switch
    {
        PersonMood.Scared => Nerve > 0.75f && Armed ? MoodIntent.Fight : MoodIntent.Flee,
        PersonMood.Angry => Armed || Nerve > 0.6f ? MoodIntent.Fight : MoodIntent.Stay,
        PersonMood.Uneasy => Nerve < 0.3f ? MoodIntent.Flee : MoodIntent.Stay,
        _ => MoodIntent.Stay,
    };

    /// <summary>The idle gesture this mood picks when nothing else is asked of them (<see cref="BodyAct.None"/> if none).</summary>
    public BodyAct Idle(float clock, int salt)
    {
        // On and off, each person on their own beat, so a crowd doesn't fidget together.
        bool on = MathF.Sin(clock * 0.31f + salt * 1.7f) > 0f;
        return Mood switch
        {
            PersonMood.Scared => BodyAct.Cower,
            PersonMood.Uneasy => on ? BodyAct.Fidget : BodyAct.None,
            PersonMood.Angry => on ? BodyAct.Warn : BodyAct.None,
            PersonMood.Grieving => BodyAct.Grieve,
            PersonMood.Drunk => on ? BodyAct.Shrug : BodyAct.None,
            PersonMood.Content => Cheer > 0.6f && on ? BodyAct.Celebrate : BodyAct.None,
            _ => BodyAct.None,
        };
    }

    /// <summary>The kind of voice this mood gives (the wordless speech's four).</summary>
    public VoiceMood Voice => Mood switch
    {
        PersonMood.Scared => Fear > 0.8f ? VoiceMood.Cry : VoiceMood.Whisper,
        PersonMood.Uneasy => VoiceMood.Whisper,
        PersonMood.Angry => VoiceMood.Shout,
        PersonMood.Grieving => VoiceMood.Cry,
        _ => VoiceMood.Murmur,
    };
}
