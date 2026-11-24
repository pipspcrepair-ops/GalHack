using System;

namespace Wildlife;

/// <summary>What the player can say with their body, from the gesture wheel. The same meaning everywhere.</summary>
public enum PlayerGesture { Wave, Beckon, HandsUp, Stop, Point, Threaten, Thanks }

/// <summary>How someone answers a gesture: what their body says back, and what they do about it.</summary>
public enum GestureEffect { None, Come, Keep, Back, Look, Calm, Alarm, Warm }

/// <summary>
/// The gesture rules, the same for people and the player (docs: "gestures carry the same meaning
/// everywhere; only the friendly ones vary from world to world"). A gesture is one of the body
/// acts the rig already shows; a person reads it by their group's stance, their job, the place's
/// standing with you and whether the place has turned on you, and answers with a body act and a
/// small effect. No words.
/// </summary>
public static class Gestures
{
    /// <summary>The body act the player's gesture is, as anyone else would make it.</summary>
    public static BodyAct Act(PlayerGesture g) => g switch
    {
        PlayerGesture.Wave => BodyAct.Greet,
        PlayerGesture.Beckon => BodyAct.Beckon,
        PlayerGesture.HandsUp => BodyAct.HandsUp,
        PlayerGesture.Stop => BodyAct.Warn,
        PlayerGesture.Point => BodyAct.Point,
        PlayerGesture.Threaten => BodyAct.Taunt,
        _ => BodyAct.Thanks,
    };

    /// <summary>
    /// The friendly gestures' local form, from the world (0 a hand waved high, 1 an open palm held
    /// up, 2 a hand to the chest): how a world's people greet and thank. The rest never change.
    /// </summary>
    public static int FriendlyStyle(ulong worldSeed) => (int)(Rng.For(worldSeed, "greeting").Float() * 3f) % 3;

    /// <summary>How a person answers a gesture from you.</summary>
    public static (BodyAct Reply, GestureEffect Effect) Answer(PlayerGesture g, Stance stance, PersonRole job, float standing, bool hostile, float nerve)
    {
        bool keeper = job is PersonRole.Guard or PersonRole.Lawman;
        bool raider = job == PersonRole.Raider || hostile;
        bool cold = stance is Stance.Wary or Stance.Hostile or Stance.Superstitious || standing < -20f;
        bool warm = stance is Stance.Welcoming or Stance.Bargaining && standing > -10f || standing > 30f;
        switch (g)
        {
            case PlayerGesture.Wave:
            case PlayerGesture.Thanks:
                if (raider) return (BodyAct.Taunt, GestureEffect.None);
                if (keeper) return (cold ? BodyAct.Warn : BodyAct.None, GestureEffect.None);
                if (cold) return (BodyAct.WaveOff, GestureEffect.None);
                return (BodyAct.Greet, g == PlayerGesture.Thanks ? GestureEffect.Warm : GestureEffect.None);
            case PlayerGesture.Beckon:
                if (raider) return (BodyAct.Taunt, GestureEffect.None);
                if (keeper) return (BodyAct.Warn, GestureEffect.Keep);
                if (job == PersonRole.Trader) return (BodyAct.Present, warm ? GestureEffect.Come : GestureEffect.None);
                return warm && job != PersonRole.Child ? (BodyAct.Greet, GestureEffect.Come) : (BodyAct.WaveOff, GestureEffect.Back);
            case PlayerGesture.HandsUp:
                // Surrender: guns come down, the frightened settle.
                return (raider ? BodyAct.Taunt : keeper ? BodyAct.Warn : BodyAct.Shrug, GestureEffect.Calm);
            case PlayerGesture.Stop:
                if (raider) return (BodyAct.Taunt, GestureEffect.None);
                return (keeper ? BodyAct.Warn : nerve < 0.35f ? BodyAct.HandsUp : BodyAct.Shrug, GestureEffect.Keep);
            case PlayerGesture.Point:
                return (keeper || nerve > 0.5f ? BodyAct.Point : BodyAct.None, GestureEffect.Look);
            default: // threaten
                if (raider || keeper) return (BodyAct.Warn, GestureEffect.Alarm);
                return (nerve < 0.4f ? BodyAct.Cower : nerve < 0.75f ? BodyAct.HandsUp : BodyAct.WaveOff, GestureEffect.Back);
        }
    }
}
