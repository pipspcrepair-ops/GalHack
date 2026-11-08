using System;
using Godot;
using Slice.Surface;
using SurvivalVitals.Model;
using Wildlife;
using N = System.Numerics;

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
    bool _openHelmet, _crouchedWhenSeized;

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
        // Your stance holds while it has you: crouched you stay crouched, standing you stay standing.
        _crouchedWhenSeized = Survivor.Crouched;
        Survivor.Dismount();
        Zone.Wildlife.Audio.PlayUi("hurt", -4f);
        Visor.Hurt(Zone.Wildlife.WorldOf(e.Husk));
        Alerts.Attack("husk_hazard", Zone.Wildlife.WorldOf(e.Husk)); // the key to struggle free shows while it holds you
    }

    void OnLetGo(SeizeEvent e)
    {
        if (e.Husk == HeldBy) EndHold();
    }

    void EndHold()
    {
        // However it ended, a moment's grace: the next Husk in the ring can't take you straight away.
        if (HeldBy != null) Zone.Wildlife.SparePlayer(Zone.Wildlife.Life.Data.Husks.SeizeCooldown);
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
        // Nothing moves you: you stay where you stand, in the stance you were taken in (only the Husk comes to you).
        Survivor.WantCrouch = _crouchedWhenSeized;
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
        Turn(Survivor, c);
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
            EndHold();
        }
        return true;
    }

    /// <summary>A worm mass's worms have you by the legs and body (in whichever sim).</summary>
    public bool InWorms { get; private set; }
    Vector3 _wormsPull;
    WormMass _wormsBy;
    /// <summary>Seconds spent in a mass's grip, and how far it has hauled you all told, m (tests).</summary>
    public float WormSeconds { get; private set; }
    public float WormHauled { get; private set; }

    /// <summary>
    /// A worm mass has hold of you (wildlife's <see cref="WormMasses.Held"/>): your legs keep only a
    /// share of their pace, you are hauled toward it a step at a time, and it stings, steadily and
    /// no worse. Walk out of it against the pull, or shoot or cut the mass, and it lets go. It
    /// never infects you: only a Husk's face could, and it can't get to you.
    /// </summary>
    void Tangled(Slice.Player.Survivor s)
    {
        bool was = InWorms;
        InWorms = false;
        WildlifePatch patch = Zone.Wildlife;
        if (patch?.Sim == null || s.Seated != null || Suit.Dead) return;
        foreach (DeepSite site in patch.Sites)
            if (site.Player != null && WormMasses.Held(site.Sim, site.Player, out Vector3 toward, out WormMass by))
            {
                InWorms = true;
                _wormsPull = toward;
                if (by != _wormsBy || !was) Alerts.Attack("husk_hazard", site.World(by.Middle));
                _wormsBy = by;
                break;
            }
        if (!InWorms)
        {
            _wormsBy = null;
            return;
        }
        HuskRules rules = patch.Life.Data.Husks;
        float dt = (float)GetPhysicsProcessDeltaTime();
        s.Legs.Move *= rules.MassSlow;
        s.Legs.Sprint = false;
        s.Legs.Jump = s.Legs.Jetpack = false;
        // Hauled in: the legs dragged its way, as if walking there at the pull's pace (never a jump);
        // walking away, what's left of your pace fights it.
        var pull = new N.Vector3(_wormsPull.X, 0f, _wormsPull.Z) * (rules.MassPull / s.Legs.WalkSpeed);
        N.Vector3 local = N.Vector3.Transform(pull, N.Quaternion.Inverse(N.Quaternion.Normalize(s.Body.Rotation * N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, s.Legs.Yaw))));
        s.Legs.Move += new N.Vector2(local.X, local.Z);
        WormHauled += rules.MassPull * dt;
        WormSeconds += dt;
        Suit.Hit(rules.MassDamage / 100.0 * Suit.Sim.Data.Vitals.Health.Max * dt, DamageKind.Hit);
    }

    /// <summary>The camera pulled in to its face: a narrower view, and a tremor.</summary>
    Transform3D CloseUpView(Transform3D eye)
    {
        Camera.Fov = Settings.Fov * (1f - 0.4f * CloseUp); // 75 degrees comes in to 45
        if (CloseUp <= 0.01f) return eye;
        float t = (float)Time.GetTicksMsec() / 1000f;
        var shake = new Vector3(MathF.Sin(t * 37f), MathF.Sin(t * 29f + 1.3f), 0f) * 0.006f * CloseUp;
        return eye.TranslatedLocal(shake + Vector3.Forward * 0.12f * CloseUp);
    }
}
