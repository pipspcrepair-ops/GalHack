using System;
using System.Collections.Generic;
using GalHack.Arms;
using PoiGenerator.Gen;
using Godot;
using Slice.Arms;
using Wildlife;

namespace Slice.Places;

/// <summary>
/// How the town's people fight you (one weapon truth): each carries a real weapon item from
/// shared/Arms (<see cref="Weapons.ItemOf"/>), drawn as the player's own model in the hand, fired
/// from its muzzle with that row's damage, rate, spread, range and shot, using up the rounds they
/// carry, and dropped with what's left of their ammo where they fall. A hand weapon strikes within
/// its reach; the unarmed punch (a raider with nerve) or flee.
///
/// They fight as a group: whoever sees you (or hears a shot, or is hit) raises the rest, who take it
/// in a beat apart. The first to see you pins you from cover, the others flank wide of them; the
/// hurt and the unnerved fall back to cover further off when hurt or when the one who led them drops;
/// the out-of-rounds close in with their fists. Every shot is aimed first, where you can see it.
/// Something dangerous at hand (a predator, raiders, whoever hurt one of them: Town.Reactions) is
/// fought the same way, by those with the nerve for it, whatever the town makes of you.
/// </summary>
public sealed partial class Town
{
    public sealed partial class Person
    {
        /// <summary>The weapon in their hands (a hand weapon or fists included) with its rounds; a gun they emptied and carry still (it drops with them).</summary>
        public Clip Kit, Emptied;
        /// <summary>The model of it in their hand (the player's own), and the node it hangs on.</summary>
        public GunModel.Built Model;
        public Node3D Holder;
        /// <summary>Their part in the fight now, and whether they are in it (from when).</summary>
        public FightPart Part;
        public bool Engaged;
        public float EngageAt = -1f;
        /// <summary>Where they're making for (a spot), how long until they think it over again, and how long since they could see you.</summary>
        public int Post = -1;
        public float Rethink, Unseen, LookIn;
        public bool Sees;
        /// <summary>Rounds still to go in a burst, time to the next, a breath between bursts, a reload under way.</summary>
        public int Pending;
        public float NextRound, Pause, Reloading;
        /// <summary>A blow under way: its wind-up (0..1, the body's hook) and the time left in it.</summary>
        public float Strike, Winding = -1f, SwingIn;
        /// <summary>Down behind cover (0..1, the body's crouch).</summary>
        public float Crouched;
        /// <summary>What last hurt them (a damage class: what their death looks like is the body's).</summary>
        public DamageClass LastHarm;
        public bool FellBack, Dropped;

        /// <summary>A real weapon, not bare hands.</summary>
        public bool Armed => Kit != null && Kit.Def.Id != Weapons.Fists;
    }

    public enum FightPart { None, Pin, Flank, Rush, FallBack }

    /// <summary>What the town has done in a fight (tests): shots and blows its people dealt, rounds used, parts taken.</summary>
    public int Shots { get; private set; }
    public int Blows { get; private set; }
    /// <summary>The most anyone's chest was turned from you as their blow landed, degrees (tests).</summary>
    public float WorstBlowFacing { get; private set; }
    public int Engagements { get; private set; }
    /// <summary>Bursts not fired because the weapon never came onto its mark, and how far off the last held round was, with its raise and gesture (tests).</summary>
    public int Dropped;
    public string LastHeld = "";
    public int Flanks { get; private set; }
    public int FallBacks { get; private set; }

    /// <summary>One of them fires (or swings) their weapon: who, the row, from where and which way (world), and how unsteady (degrees).</summary>
    public event Action<Person, WeaponDef, Vector3, Vector3, float, object> ShotFired;
    /// <summary>One of them is down: what they carried is left where they lie.</summary>
    public event Action<Person> Fell;

    bool _alert;
    Vector3 _seenAt;
    float _seenClock = -99f;
    Person _leader;

    const float NoticeRange = 42f, LookEvery = 0.45f, ThinkEvery = 2.6f, Spotted = 1.1f;

    /// <summary>Their weapon from the world's rows: what they carry (bare hands if nothing), loaded, with an item or three of ammo.</summary>
    void Arm(Person p)
    {
        if (p.Kit != null || p.Looks == null || _places?.Zone?.Data?.Arms is not Armoury table) return;
        WeaponDef def = table.OfItem(Weapons.ItemOf(p.Figure.Prop)) ?? table.OfItem(Weapons.Fists);
        if (def == null) return;
        var rng = Wildlife.Rng.For(Memory.Inst.SpawnSeed, "arms", (long)People.IndexOf(p));
        p.Kit = Clip.Of(def, _places.Zone.Data.Items, def.Delivery == Delivery.Strike ? 0 : rng.Int(1, 3));
    }

    /// <summary>Who stands and fights: grown-ups with a weapon (the guards, the law, raiders), and an unarmed raider with the nerve to use their fists.</summary>
    bool Fighter(Person p)
    {
        if (p.Looks == null || p.Kit == null) return false;
        if (p.Figure.Stage is LifeStage.Child or LifeStage.Elder || p.Job == PersonRole.Drunk) return false;
        if (p.Armed) return !(p.Kit.Spent && p.Emptied == null && p.Nerve < 0.35f);
        return p.Job == PersonRole.Raider && p.Nerve > 0.55f || p.Emptied != null && p.Nerve > 0.4f;
    }

    /// <summary>The town knows where you are: everyone who fights takes it in, the nearest and boldest first.</summary>
    void Alert(Vector3 at)
    {
        _seenAt = at;
        _seenClock = _clock;
        if (_alert) return;
        _alert = true;
        foreach (Person q in People)
        {
            if (q.Dead || q.Gone || q.Engaged) continue;
            float d = (q.Pos - at).Length();
            q.EngageAt = _clock + 0.25f + d / 45f + (1f - q.Nerve) * 0.5f + (float)_rng.NextDouble() * 0.3f;
        }
    }

    /// <summary>One of them is hit (by the player, or anything else): hurt, remembered, the rest told; down if it killed them. A creature that did it (<paramref name="by"/>) is met as a danger.</summary>
    public void Struck(Person p, float damage, DamageClass harm, bool byPlayer, object by = null)
    {
        if (by is Creature from) Harmed(from);
        if (p.Dead) return;
        p.Health.Current -= damage;
        // They cry out (once for a shot's pellets, always as they go down).
        if (p.Looks != null && (p.Dead || p.Hurt <= 0.4f - Slice.Game.Game.CryEvery))
        {
            Vector3 at = World(p.Pos + Vector3.Up * Height(p) * 0.9f);
            _places.Zone.Wildlife.Sound(Vector3.Zero, a => a.Cry(p.Looks, at));
        }
        p.Hurt = 0.4f;
        p.LastHarm = harm;
        if (byPlayer)
        {
            Did(p.Dead ? Deed.Kill : Deed.Harm);
            Provoked();
            if (_place != null) Alert(_place.FromWorld(_places.Zone.Wildlife.PlayerWorld()));
            // Hit, they know it: no aiming about it, they're in.
            if (!p.Dead && Fighter(p)) p.EngageAt = MathF.Min(p.EngageAt < 0f ? _clock : p.EngageAt, _clock + 0.2f);
            // Hit themselves, it frightens them more (their mood sends them running: Town.Mood).
            if (!p.Dead) { Bind(p); p.Feel.Hit(MoodEvent.Harm); }
        }
        if (p.Dead) Fall(p);
    }

    /// <summary>
    /// A person's fight this tick: with something dangerous at hand they've taken on (Town.Reactions),
    /// else, in a hostile place, with you. True while they're in it (moving, aiming, firing, striking);
    /// false leaves them to the routine (and those who don't fight to their doors).
    /// </summary>
    bool Combat(Person p, Vector3 player, Mood mood, float dt)
    {
        if (p.Looks == null || _place == null) return false;
        Arm(p);
        p.Facing = MathF.Max(0f, p.Facing - dt);
        p.Masked = MathF.Max(0f, p.Masked - dt);
        Creature foe = p.Facing > 0f && p.Threat is { Dead: false, Removed: false } c ? c : null;
        // Unseen a long while, they stand down (the place stays hostile).
        if (_alert && _clock - _seenClock > 40f) _alert = false;
        // (Frightened enough to run, they run: Feeling.Flees. You down, they fight only what else is about.)
        if (foe == null && (mood != Mood.Hostile || p.Engaged && !_alert || YouDown) || !Fighter(p) || p.ScaredFor > 0f)
        {
            if (p.Engaged) Disengage(p);
            return false;
        }
        Vector3 at = foe != null ? _place.FromWorld(_places.Zone.Wildlife.WorldOf(foe)) : player;
        float chest = foe != null ? Arming.Chest(foe) : 1.15f;
        if (!p.Engaged)
        {
            if (foe == null && !Notices(p, player, dt)) return false;
            Engage(p, foe == null);
        }
        if (p.Indoors || p.Asleep) { p.Indoors = false; Wake(p); }
        p.Halt = 0f;
        // What they can see of it, a few times a second.
        if ((p.LookIn -= dt) <= 0f)
        {
            p.LookIn = 0.3f;
            p.Sees = (at - p.Pos).Length() < NoticeRange * 1.4f && Clear(Eye(p), at + Vector3.Up * chest);
            if (p.Sees && foe == null) { _seenAt = player; _seenClock = _clock; }
        }
        p.Unseen = p.Sees ? 0f : p.Unseen + dt;
        p.Pause = MathF.Max(0f, p.Pause - dt);
        p.SwingIn = MathF.Max(0f, p.SwingIn - dt);
        p.Rethink -= dt;
        Choose(p, foe == null && _clock - _seenClock < 8f ? _seenAt : at);
        if (p.Kit.Strike) Close(p, at, chest, dt);
        else Shoot(p, at, chest, foe == null, dt);
        return true;
    }

    /// <summary>Whether they've noticed you: the town's word reached them, or they saw you themselves.</summary>
    bool Notices(Person p, Vector3 player, float dt)
    {
        if (_alert && p.EngageAt >= 0f && _clock >= p.EngageAt) return true;
        if (p.Indoors || p.Asleep) return false;
        if ((p.LookIn -= dt) > 0f) return false;
        p.LookIn = LookEvery + (float)_rng.NextDouble() * 0.2f;
        Vector3 to = player - p.Pos;
        float d = to.Length();
        if (d > NoticeRange) return false;
        // In front of them, or close enough to hear: and nothing in the way.
        if (d > 7f && p.Forward.Dot(to / d) < -0.1f) return false;
        if (!Clear(Eye(p), player + Vector3.Up * 1.3f)) return false;
        Alert(player);
        _leader ??= p;
        return true;
    }

    /// <summary>Into the fight: with you, the first in leads and pins you down and the bold of the rest go wide; with anything else, each pins it from where they are.</summary>
    void Engage(Person p, bool you)
    {
        p.Engaged = true;
        Engagements++;
        p.Rethink = 0f;
        p.LookIn = 0f;
        p.Path = null;
        p.Step = 0;
        p.Partner = null;
        p.Sitting = false;
        p.Greeted = true;
        if (you && (_leader == null || _leader.Dead)) _leader = p;
        p.Part = !p.Armed ? FightPart.Rush : p.Kit.Strike ? FightPart.Rush : !you || p == _leader ? FightPart.Pin : (Flanking() < 2 && p.Nerve > 0.3f ? FightPart.Flank : FightPart.Pin);
        if (p.Part == FightPart.Flank) Flanks++;
        Speak(p, VoiceMood.Shout);
    }

    int Flanking()
    {
        int n = 0;
        foreach (Person q in People) if (q.Engaged && !q.Dead && q.Part == FightPart.Flank) n++;
        return n;
    }

    void Disengage(Person p)
    {
        p.Engaged = false;
        p.Part = FightPart.None;
        p.Pending = 0;
        p.Winding = -1f;
        p.Strike = 0f;
        p.Crouched = 0f;
        p.Aim = -1f;
        p.Holding = false;
        p.Post = -1;
    }

    /// <summary>Where they're going and why: hurt or leaderless and unnerved, back to cover further off; out of rounds, in with their fists; otherwise their part (against what they fight, where they know it to be).</summary>
    void Choose(Person p, Vector3 you)
    {
        float health = p.Health.Current / MathF.Max(1f, p.Health.Max);
        bool leaderDown = _leader != null && _leader != p && _leader.Dead;
        if (!p.FellBack && (health < 0.35f || leaderDown && p.Nerve < 0.5f))
        {
            p.FellBack = true;
            p.Part = FightPart.FallBack;
            p.Rethink = 0f;
            FallBacks++;
            Speak(p, VoiceMood.Cry);
        }
        if (p.Armed && p.Kit.Spent && p.Pending == 0)
        {
            // Out of rounds: the gun stays on them (it drops with them); fists now.
            p.Emptied = p.Kit;
            WeaponDef fists = _places.Zone.Data.Arms.OfItem(Weapons.Fists);
            p.Kit = Clip.Of(fists, null, 0);
            p.Part = p.Nerve > 0.4f ? FightPart.Rush : FightPart.FallBack;
            p.Rethink = 0f;
        }
        if (p.Rethink > 0f) return;
        p.Rethink = ThinkEvery * (0.8f + 0.4f * (float)_rng.NextDouble());
        if (p.Part == FightPart.Rush) return;
        int spot = Position(p, you);
        // Nowhere in reach with a line, and none from here a while (walls between, it beyond the edge): up to the spot nearest it.
        if (spot < 0 && p.Unseen > 1.5f) spot = NearestSpot(you);
        if (spot >= 0 && spot != p.Post)
        {
            p.Post = spot;
            GoTo(p, spot);
        }
    }

    /// <summary>
    /// The spot to fight from, by their part: in reach of you, with a line to you standing and,
    /// best of all, cover crouching (something between them and you below the chest); a flanker as
    /// far round you from the one pinning you as it can get; falling back, cover further from you.
    /// </summary>
    int Position(Person p, Vector3 you)
    {
        float range = p.Kit.Range;
        Vector3 pin = _leader != null && !_leader.Dead && _leader != p ? _leader.Pos : p.Pos;
        Vector3 pinDir = (pin - you) with { Y = 0f };
        pinDir = pinDir.LengthSquared() > 0.01f ? pinDir.Normalized() : -p.Forward;
        int best = -1;
        float bestScore = float.MinValue;
        int looked = 0;
        for (int i = 0; i < Spots.Count && looked < 10; i++)
        {
            if (Spots[i].Tag == "bed") continue;
            Vector3 at = At(i);
            float fromMe = (at - p.Pos).Length(), fromYou = (at - you).Length();
            if (fromMe > 28f) continue;
            if (p.Part != FightPart.FallBack && (fromYou > range * 0.9f || fromYou < 4f)) continue;
            if (p.Part == FightPart.FallBack && fromYou < (p.Pos - you).Length() + 4f) continue;
            if (Taken(p, i)) continue;
            looked++;
            // (Their line, once someone stood in it: clear of bodies too, theirs and anyone's.)
            bool line = Clear(at + Vector3.Up * 1.5f, you + Vector3.Up * 1.2f) && (p.Masked <= 0f || !Slice.Arms.FireLine.Masked(_places.Zone.Wildlife, _places, World(at + Vector3.Up * 1.5f), World(you + Vector3.Up * 1.2f), p, p.Threat ?? Slice.Arms.ArmsHost.You, p.Threat == null));
            bool cover = !Clear(at + Vector3.Up * 0.8f, you + Vector3.Up * 1.0f);
            float score = -fromMe * 0.15f;
            switch (p.Part)
            {
                case FightPart.Pin:
                    score += (line ? 6f : -4f) + (cover ? 4f : 0f) - MathF.Abs(fromYou - range * 0.55f) * 0.1f;
                    break;
                case FightPart.Flank:
                    Vector3 side = (at - you) with { Y = 0f };
                    float round = side.LengthSquared() > 0.01f ? 1f - side.Normalized().Dot(pinDir) : 0f; // 0 beside the pinner, 2 opposite
                    score += (line ? 4f : -4f) + round * 5f + (cover ? 1.5f : 0f);
                    break;
                case FightPart.FallBack:
                    score += (cover ? 6f : 0f) + (line ? 1f : 0f) + fromYou * 0.1f;
                    break;
            }
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    bool Taken(Person p, int spot)
    {
        foreach (Person q in People) if (q != p && q.Engaged && !q.Dead && q.Post == spot) return true;
        return false;
    }

    /// <summary>A gun's fight: to their spot (aiming as they go once there's a line), then aim, bursts, breaths between, reloads behind cover. The foe's feet are local, its chest so high up them; you, or something else.</summary>
    void Shoot(Person p, Vector3 foe, float chest, bool you, float dt)
    {
        WeaponDef d = p.Kit.Def;
        bool there = Move(p, dt);
        p.Holding = true;
        Vector3 to = foe - p.Pos;
        float dist = to.Length();
        // No line for a while: they move up to where they last saw you.
        // (Once, as the three seconds pass: every tick after it made each unseen fighter look round every spot again, 5,000 rays a frame
        // in a hostile town's hall.)
        if (p.Unseen > 3f && p.Unseen - dt <= 3f && p.Rethink > 0.5f && p.Part != FightPart.FallBack) p.Rethink = 0f;
        if (p.Reloading > 0f)
        {
            p.Reloading -= dt;
            p.Crouched = Mathf.MoveToward(p.Crouched, there ? 1f : 0f, dt * 3f);
            if (p.Reloading <= 0f && !p.Kit.Reload()) p.Reloading = 0f;
            return;
        }
        p.Crouched = Mathf.MoveToward(p.Crouched, 0f, dt * 3f);
        if (!p.Sees || dist > p.Kit.Range) { p.Aim = -1f; p.Pending = 0; return; }
        // Going wide or falling back: they get there first (unless you're on top of them).
        if (p.Part is FightPart.Flank or FightPart.FallBack && !there && p.Pending == 0 && dist > 8f) { p.Aim = -1f; return; }
        // Facing you to shoot: they stop where they are to take it.
        p.Yaw = Mathf.LerpAngle(p.Yaw, MathF.Atan2(-to.X, -to.Z), MathF.Min(1f, dt * 7f));
        if (p.Kit.Loaded <= 0)
        {
            // Empty: the burst is over; a reload from what they carry (the rounds go in at the end of it), or with none, fists (Choose).
            p.Pending = 0;
            if (!p.Kit.Spent) p.Reloading = d.Reload;
            return;
        }
        if (p.Pending == 0)
        {
            if (p.Pause > 0f) return;
            if (p.Aim < 0f)
            {
                // The aim: where you can see it, a beat before the first round of each burst.
                p.Aim = Spotted * (p.Part == FightPart.Pin ? 0.7f : 1f) * (0.8f + 0.4f * (float)_rng.NextDouble());
                if (you && p.Role == "guard") GuardAims++;
                if (you) Aimed?.Invoke(World(p.Pos + Vector3.Up * Height(p) * 0.85f));
                return;
            }
            p.Aim -= dt;
            if (p.Aim > 0f) return;
            p.Pending = Math.Min(p.Kit.Burst(Wildlife.Rng.For(Memory.Inst.SpawnSeed, "burst", (long)(_clock * 10f) + People.IndexOf(p))), p.Kit.Loaded);
            p.NextRound = 0f;
            if (you && p.Role == "guard") GuardShots++;
        }
        p.Now = 0f;
        if (p.Path != null && p.Step < p.Path.Count) { p.Path = null; p.Step = 0; } // they stand to fire
        if ((p.NextRound -= dt) > 0f) return;
        // Not until the weapon is on you: the round leaves along the barrel (or the arrow's line).
        float off = p.View is { Visible: true } ? p.Rig?.AimError(foe + Vector3.Up * chest) ?? 0f : 0f;
        if (off > HumanRig.OnMark)
        {
            // Still coming round: they wait; never on: they take aim again.
            LastHeld = $"{off:F1} deg off, raised {p.Raised:F2}, act {p.Act}, crouched {p.Crouched:F2}, threat {(p.Threat != null ? "a creature" : "none")}";
            if ((p.AimHeld += dt) >= HumanRig.AimWait) { p.AimHeld = 0f; p.Pending = 0; p.Aim = -1f; Dropped++; }
            return;
        }
        p.AimHeld = 0f;
        p.LastOff = off;
        Vector3 muzzle = Muzzle(p);
        Vector3 aim = World(foe + Vector3.Up * chest);
        // Someone in the line (one of their own, a bystander, an animal that isn't the one they shoot at): no round goes, and they
        // look again where to fight from, a spot with a line clear of bodies too (Position).
        object meant = you ? Slice.Arms.ArmsHost.You : p.Threat;
        if (Slice.Arms.FireLine.Masked(_places.Zone.Wildlife, _places, muzzle, aim, p, meant, you))
        {
            p.Pending = 0;
            p.Aim = -1f;
            p.Masked = 3f;
            p.Rethink = 0f;
            Slice.Arms.FireLine.Held++;
            return;
        }
        p.NextRound = d.SecondsPerShot;
        if (!p.Kit.Shot()) { p.Pending = 0; return; }
        p.Pending--;
        Shots++;
        float unsteady = 1.2f + (1f - p.Nerve) * 1.5f + (p.Hurt > 0f ? 1.5f : 0f) + (p.Part == FightPart.Pin ? 0.6f : 0f);
        ShotFired?.Invoke(p, d, muzzle, aim - muzzle, unsteady, meant);
        if (p.Pending == 0)
        {
            p.Aim = -1f;
            // The one pinning you keeps at it; the rest take a breath.
            p.Pause = (p.Part == FightPart.Pin ? 0.35f : 0.8f) + (float)_rng.NextDouble() * 0.7f + d.Pump;
            if (p.Kit.Loaded <= 0 && !p.Kit.Spent) p.Reloading = d.Reload;
        }
    }

    /// <summary>How far off what they strike, radians, someone may still be turned as their blow lands.</summary>
    public static readonly float SquaredUp = Mathf.DegToRad(15f);

    /// <summary>Turns them, whole body, to face along <paramref name="to"/> as fast as a strike turns; returns how far off they still are, radians.</summary>
    float SquareUp(Person p, Vector3 to, float dt)
    {
        float want = MathF.Atan2(-to.X, -to.Z);
        p.Yaw = Mathf.RotateToward(p.Yaw, want, _places.Zone.Data.Wildlife.Combat.StrikeTurn * dt);
        return MathF.Abs(Geo.WrapAngle(want - p.Yaw));
    }

    /// <summary>A hand weapon's (or bare hands') fight: straight at the foe (local feet) by the walking grid, then close; a wind-up you can see, then the blow, at its chest.</summary>
    void Close(Person p, Vector3 foe, float chest, float dt)
    {
        WeaponDef d = p.Kit.Def;
        p.Holding = d.Id != Weapons.Fists;
        Vector3 to = foe - p.Pos;
        float flat = new Vector2(to.X, to.Z).Length();
        float reach = MathF.Max(0.8f, d.Reach * 0.85f);
        if (p.Winding >= 0f)
        {
            // Committed: the blow lands when the wind-up is done, whoever is still there.
            float wind = 0.42f, facing = SquareUp(p, to, dt);
            // Nobody lands a blow looking past you: it waits at the top of the swing until squared up.
            if (p.Winding + dt < wind || facing <= SquaredUp) p.Winding += dt;
            p.Strike = Math.Clamp(p.Winding / wind, 0f, 1f);
            p.Now = 0f;
            if (p.Winding < wind) return;
            p.Winding = -1f;
            p.SwingIn = d.SecondsPerShot;
            Blows++;
            if (p.Rig != null)
            {
                Vector3 body = WildSim.Flat(p.Rig.Facing).Normalized(), want = WildSim.Flat(to).Normalized();
                WorstBlowFacing = MathF.Max(WorstBlowFacing, Mathf.RadToDeg(MathF.Acos(Math.Clamp(body.Dot(want), -1f, 1f))));
            }
            Vector3 from = World(p.Pos + Vector3.Up * Height(p) * 0.78f);
            ShotFired?.Invoke(p, d, from, World(foe + Vector3.Up * MathF.Min(1.1f, chest)) - from, 0f, null);
            return;
        }
        p.Strike = Mathf.MoveToward(p.Strike, 0f, dt * 4f);
        if (flat <= reach + 0.25f && MathF.Abs(to.Y) < 1.4f)
        {
            p.Now = 0f;
            p.Path = null;
            if (SquareUp(p, to, dt) <= SquaredUp * 2f && p.SwingIn <= 0f) p.Winding = 0f;
            return;
        }
        // On their way in: at a run, straight once there's a clear line and the walking grid has floor all the way, else to the spot nearest you.
        p.Speed = 3.6f;
        Vector3 stop = foe - new Vector3(to.X, 0f, to.Z) / MathF.Max(flat, 0.01f) * reach * 0.8f;
        if (p.Sees && flat < 8f && Clear(p.Pos + Vector3.Up * 0.5f, foe + Vector3.Up * 0.5f) && Straight(p, stop))
        {
            if (p.Path == null || p.Path.Count != 1 || (p.Path[0] - stop).LengthSquared() > 0.25f) { p.Path = new List<Vector3> { stop with { Y = p.Pos.Y } }; p.Step = 0; }
        }
        else if (p.Rethink <= 0f || p.Path == null || p.Step >= p.Path.Count)
        {
            p.Rethink = 1.2f;
            if (NearestSpot(foe) is var near and >= 0) GoTo(p, near);
        }
        Move(p, dt);
    }

    /// <summary>The town's spot nearest a point (local), -1 if it has none.</summary>
    int NearestSpot(Vector3 to)
    {
        int near = -1;
        float best = float.MaxValue;
        for (int i = 0; i < Spots.Count; i++)
        {
            float d2 = (At(i) - to).LengthSquared();
            if (d2 < best) { best = d2; near = i; }
        }
        return near;
    }

    /// <summary>Where they look from (local).</summary>
    Vector3 Eye(Person p) => p.Pos + Vector3.Up * Height(p) * 0.9f;

    /// <summary>Nothing solid between two local points.</summary>
    bool Clear(Vector3 a, Vector3 b)
    {
        var space = _place.GetWorld3D().DirectSpaceState;
        var q = PhysicsRayQueryParameters3D.Create(_place.ToWorld(a), _place.ToWorld(b), GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull);
        return space.IntersectRay(q).Count == 0;
    }

    /// <summary>Where their shots leave (world): the muzzle of the gun in their hands as drawn (undrawn: about where it would be).</summary>
    public Vector3 Muzzle(Person p)
    {
        if (p.Model?.Muzzle != null && GodotObject.IsInstanceValid(p.Model.Muzzle) && p.Model.Muzzle.IsInsideTree() && p.View is { Visible: true }) return p.Model.Muzzle.GlobalPosition;
        // A bow (the body's own), or a gun not drawn: from the hand, along the weapon's line.
        if (p.Rig is { } r && r.WeaponLine.LengthSquared() > 0.5f) return World(r.WeaponGrip + r.WeaponLine * 0.1f);
        return World(p.Pos + Vector3.Up * Height(p) * 0.78f + p.Forward * 0.55f);
    }

    static int PropIndex(Person p) => p.Rig.Layout.Pieces.FindIndex(x => x.Name == "prop");

    /// <summary>Whether their weapon is drawn in their hand (a body built, armed with something the game draws).</summary>
    bool Wields(Person p) => p.View != null && p.Kit != null && p.Kit.Def.Id != Weapons.Fists && Weapons.DrawnByGame(p.Figure.Prop);

    /// <summary>The weapon's model in their hand: the player's own, hung where the rig puts their carried thing (the frame after their body).</summary>
    void HoldWeapon(Person p)
    {
        Arm(p);
        if (p.Holder != null || !Wields(p)) return;
        p.Model = GunModel.Hang(p.View, p.Kit.Def, Transform3D.Identity);
        p.Holder = p.View.GetNode<Node3D>("Weapon");
    }

    /// <summary>Keeps the weapon's model in their hand as the rig poses it.</summary>
    void PoseWeapon(Person p)
    {
        if (p.Holder == null || p.Rig == null || PropIndex(p) is not (int i and >= 0)) return;
        p.Holder.Transform = p.Rig.Pose[i] * new Transform3D(Clip.GunInProp.Scaled(Vector3.One * HumanBody.ArmScale(p.Figure)), Vector3.Zero);
    }

    /// <summary>What a fallen person leaves: their weapon (the one they emptied too) and the ammo they had left for it.</summary>
    public GalHack.Items.Bill Leaves(Person p)
    {
        var bill = new GalHack.Items.Bill();
        var items = _places?.Zone?.Data?.Items;
        if (items == null) return bill;
        foreach (Clip c in new[] { p.Kit, p.Emptied })
        {
            if (c == null || c.Def.Id == Weapons.Fists) continue;
            if (items.Has(c.Def.Item)) bill.Add(c.Def.Item, 1);
            if (c.ItemsLeft > 0 && items.Has(c.Def.Ammo)) bill.Add(c.Def.Ammo, c.ItemsLeft);
        }
        return bill;
    }
}
