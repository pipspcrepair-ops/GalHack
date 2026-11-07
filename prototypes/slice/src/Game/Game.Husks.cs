using System;
using Godot;
using SurvivalVitals.Model;
using Wildlife;

namespace Slice.Game;

/// <summary>
/// A Husk has you by the head (wildlife's <see cref="Seizing"/>), pulled face to face for the worms
/// to find a way in. What happens is the suit's business:
/// - helmet open: the view is dragged onto its face and in, and you are dead in a moment or two
///   (a quick, horrible death; nothing infects you, and nothing lingers);
/// - helmet sealed: a struggle. The worms can't get in, but it hurts; mash Space or wrench your
///   view away from it to break the hold, and it is thrown back and staggers.
/// You are never infected: it is death or nothing.
/// </summary>
public partial class Game
{
    /// <summary>The Husk holding you, if one is, and the sim it lives in.</summary>
    public Creature HeldBy { get; private set; }
    WildSim _heldIn;
    float _heldFor;
    bool _openHelmet;

    /// <summary>How far through breaking a sealed-helmet hold you are, 0 to 1.</summary>
    public float Struggle { get; private set; }
    /// <summary>How far the view has been pulled in to its face, 0 to 1 (the camera reads it).</summary>
    public float CloseUp { get; private set; }

    /// <summary>Holds broken and deaths to a Husk with the helmet open (tests).</summary>
    public int HoldsBroken { get; private set; }
    public int HuskDeaths { get; private set; }

    void SetUpHusks()
    {
        Zone.Wildlife.Seized += OnSeized;
        Zone.Wildlife.LetGo += OnLetGo;
        // Its face is against yours: every shot and blow goes into it, and any one breaks contact.
        Tool.Seizer = () => HeldBy;
        Tool.SeizerHit += husk =>
        {
            if (husk != HeldBy) return;
            Seizing.Break(_heldIn, husk);
            HoldsBroken++;
            ShotsFree++;
            Say("Shot it off", Tones.Good);
            EndHold();
        };
    }

    /// <summary>Holds broken by a shot or a blow (tests).</summary>
    public int ShotsFree { get; private set; }

    void OnSeized(SeizeEvent e, WildSim sim)
    {
        if (HeldBy != null || Suit.Dead)
        {
            Seizing.Finish(sim, e.Husk);
            return;
        }
        HeldBy = e.Husk;
        _heldIn = sim;
        _heldFor = 0f;
        Struggle = 0f;
        _openHelmet = !Suit.Sim.Player.Gear.HelmetOn;
        Survivor.Dismount();
        Zone.Wildlife.Audio.PlayUi("hurt", -4f);
        Visor.Hurt(Zone.Wildlife.WorldOf(e.Husk));
        if (!_openHelmet) Say("SEIZED: MASH SPACE OR WRENCH AWAY", Tones.Warning);
    }

    void OnLetGo(SeizeEvent e)
    {
        if (e.Husk == HeldBy) EndHold();
    }

    void EndHold()
    {
        HeldBy = null;
        _heldIn = null;
        Struggle = 0f;
    }

    /// <summary>Each tick while held: the hold has the body (true), and the struggle or the end plays out.</summary>
    bool Held(Controls c, float dt)
    {
        CloseUp = Mathf.MoveToward(CloseUp, 0f, dt * 2f);
        if (HeldBy == null) return false;
        Creature h = HeldBy;
        if (h.Removed || h.Dead || h.Phase != AttackPhase.Hold || Suit.Dead)
        {
            if (h.Phase == AttackPhase.Hold) Seizing.Finish(_heldIn, h);
            EndHold();
            return false;
        }
        HuskRules rules = Zone.Wildlife.Life.Data.Husks;
        _heldFor += dt;
        Survivor.Legs.Move = default;
        Survivor.Legs.Jump = Survivor.Legs.Jetpack = false;
        Survivor.Legs.Sprint = false;
        // Its face, a hand's width from yours.
        // (Its head, where its rig has it: a hunched one's is lower than its height.)
        Vector3 face = h.Rig != null && h.Rig.HitCount > 1
            ? Zone.Wildlife.WorldOf(h) + new Vector3(h.Rig.Hits[1].X, h.Rig.Hits[1].Y, h.Rig.Hits[1].Z) - h.Position
            : Zone.Wildlife.WorldOf(h) + Vector3.Up * h.Species.Figure.Height * 0.9f;
        Vector3 to = face - Survivor.WorldEye();
        float yawTo = MathF.Atan2(-to.X, -to.Z);
        float pitchTo = MathF.Atan2(to.Y, new Vector2(to.X, to.Z).Length());
        if (_openHelmet)
        {
            // Nothing between you and it: the view is dragged onto its face and in, and then it's over.
            float pull = 1f - MathF.Exp(-9f * dt);
            Survivor.Yaw = Mathf.LerpAngle(Survivor.Yaw, yawTo, pull);
            Survivor.Pitch = Mathf.Lerp(Survivor.Pitch, pitchTo, pull);
            CloseUp = Math.Clamp(_heldFor / rules.OpenHelmetSeconds, 0f, 1f);
            if (_heldFor >= rules.OpenHelmetSeconds)
            {
                Suit.Hit(Suit.Sim.Data.Vitals.Health.Max * 2.0, DamageKind.Hit);
                HuskDeaths++;
                Seizing.Finish(_heldIn, h);
                EndHold();
            }
            return true;
        }
        // Sealed: it can't get in, but it has you. Fight the pull of its hands to wrench the view away, or mash Space.
        Survivor.Yaw -= c.Look.X * 0.0025f;
        Survivor.Pitch = Mathf.Clamp(Survivor.Pitch - c.Look.Y * 0.0025f, -1.5f, 1.5f);
        float drag = 1f - MathF.Exp(-2.2f * dt);
        Survivor.Yaw = Mathf.LerpAngle(Survivor.Yaw, yawTo, drag);
        Survivor.Pitch = Mathf.Lerp(Survivor.Pitch, pitchTo, drag);
        CloseUp = MathF.Max(CloseUp, 0.35f);
        if (MathF.Abs(Mathf.Wrap(Survivor.Yaw - yawTo, -MathF.PI, MathF.PI)) > 0.9f) Struggle += rules.StrugglePerSecondAway * dt;
        if (c.WasPressed(Key.Space)) Struggle += rules.StrugglePerPress;
        Suit.Hit(rules.HoldDamage / 100.0 * Suit.Sim.Data.Vitals.Health.Max * dt, DamageKind.Hit);
        if (Struggle >= 1f)
        {
            Seizing.Break(_heldIn, h);
            HoldsBroken++;
            Say("Broke free", Tones.Good);
            EndHold();
        }
        return true;
    }

    /// <summary>The camera pulled in to its face: a narrower view, and a tremor.</summary>
    Transform3D CloseUpView(Transform3D eye)
    {
        Camera.Fov = 75f - 30f * CloseUp;
        if (CloseUp <= 0.01f) return eye;
        float t = (float)Time.GetTicksMsec() / 1000f;
        var shake = new Vector3(MathF.Sin(t * 37f), MathF.Sin(t * 29f + 1.3f), 0f) * 0.006f * CloseUp;
        return eye.TranslatedLocal(shake + Vector3.Forward * 0.12f * CloseUp);
    }
}
