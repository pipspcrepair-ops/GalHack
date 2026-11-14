using System;
using Godot;

namespace Wildlife;

public static partial class Brain
{
    /// <summary>A band's distances, m: it greets you within this, warns you off within this, keeps you this far off, takes coming on past its warning this close as pushing it, and settles once you are gone this far.</summary>
    const float GreetIn = 14f, WarnIn = 24f, KeepOff = 13f, PushIn = 7f, CalmOut = 38f;
    /// <summary>A gun on them: this close it is a threat (friendly), within this reach a provocation (wary); a shot passing this close to one of them, or fired this close, is being shot at.</summary>
    const float AimClose = 12f, AimReach = 32f, ShotPast = 3f, ShotBy = 6f;
    /// <summary>How close a friendly one lets you come before stepping aside; how close its leader comes to meet you; how far off a gesture of yours is seen.</summary>
    const float Comfort = 2.6f, MeetAt = 4f, SeeGesture = 24f;
    /// <summary>How long a warning stands before anything more can come of it, s.</summary>
    const float WarnHold = 1.6f;
    static readonly float AimCone = MathF.Cos(Mathf.DegToRad(7f));

    /// <summary>
    /// One of a band of people (<see cref="Band"/>): the band decides, once a step, what it makes of
    /// whoever is about; each member takes that in its own time (the leader first, the rest from it)
    /// and does its part: carries on, steps aside, watches and backs off, warns, fights, or runs.
    /// </summary>
    static void Folk(WildSim w, Creature c, float dt)
    {
        Herd h = c.Herd ?? Alone(w, c);
        Band b = h.Band ??= Band.Roll(c.Species, Rng.Mix(c.Species.Seed, (ulong)c.Id));
        UpdateHerd(w, h, dt);
        Creature lead = Leader(h) ?? c;
        UpdateBand(w, h, b, lead);
        c.WantAim = 0f;
        if (w.Time > c.ActUntil) c.Act = BodyAct.None;
        // Each takes in what the band does in its own time: the leader first, the rest once it has.
        if (c.BandSeen != b.Events)
        {
            c.BandSeen = b.Events;
            c.ReactAt = c == lead ? b.LeaderReactsAt : float.MaxValue;
        }
        if (c != lead && c.ReactAt == float.MaxValue && lead.BandSeen == b.Events && lead.ReactedAt >= lead.ReactAt)
            c.ReactAt = lead.ReactedAt + Reaction.Delay(c, WildSim.Flat(lead.Position - c.Position).Length(), saw: c.Noticed != null);
        bool reacted = w.Time >= c.ReactAt;
        if (reacted && c.ReactedAt < c.ReactAt)
        {
            c.ReactedAt = w.Time;
            if (c != lead) { if (lead.ReactedAt <= c.ReactedAt && lead.BandSeen == b.Events) b.AfterLeader++; else b.BeforeLeader++; }
            Took(w, c, b, c == lead);
        }
        Answer(w, c, b, lead);
        if (b.Mood == BandMood.Fleeing)
        {
            // Until it takes the fright in, the head comes round to it; then it runs with the band.
            if (reacted) WithHerd(w, c, h, h.Alarmed);
            else { c.SetState(CState.Idle); c.Desired = Vector3.Zero; c.LookAt = b.ThreatAt + Vector3.Up; }
            return;
        }
        if (reacted && b.Mood == BandMood.Fighting && b.Foe != null) { Fight(w, c, b.Foe); return; }
        if (reacted && b.Mood is BandMood.Watching or BandMood.Warning && b.Outsider != null)
        {
            if (b.Leaning == Leaning.Friendly) Easy(w, c, h, b, lead);
            else Watch(w, c, b, lead);
            return;
        }
        CarryOn(w, c, h, b);
    }

    /// <summary>Someone met on their own: a band of one.</summary>
    static Herd Alone(WildSim w, Creature c)
    {
        var h = new Herd { Home = c.Position, Goal = c.Position, Center = c.Position, Spread = WildSim.HerdSpread(c.Species, 1) };
        h.Members.Add(c);
        c.Herd = h;
        c.HerdSlot = 0;
        w.Herds.Add(h);
        return h;
    }

    /// <summary>Whoever leads a band: the first of it still standing.</summary>
    public static Creature Leader(Herd h)
    {
        foreach (Creature m in h.Members) if (m.Alive && !m.Removed) return m;
        return null;
    }

    static void Hold(WildSim w, Creature c, BodyAct act, float seconds)
    {
        c.Act = act;
        c.ActUntil = w.Time + seconds;
    }

    /// <summary>Whether someone has a weapon on this one: drawn, within reach, pointed at its chest.</summary>
    static bool Aimed(ITarget t, Creature m, float reach)
    {
        if (!t.Drawn) return false;
        Vector3 to = m.Position + Vector3.Up * (m.Species.Figure?.Height ?? 1.7f) * 0.6f - (t.Position + Vector3.Up * 1.6f);
        float len = to.Length();
        return len > 0.1f && len < reach && t.Forward.Dot(to / len) > AimCone;
    }

    static float Nearest(Herd h, Vector3 p)
    {
        float best = float.MaxValue;
        foreach (Creature m in h.Members) if (m.Alive && !m.Removed) best = MathF.Min(best, WildSim.Flat(m.Position - p).Length());
        return best;
    }

    // ---- The band's mind, once a step --------------------------------------------------------

    static void UpdateBand(WildSim w, Herd h, Band b, Creature lead)
    {
        if (w.Time - b.UpdatedAt < 0.15f) return;
        b.UpdatedAt = w.Time;
        // Their stance with you now: the roll, moved by your standing with their people; hostile while turned.
        bool keeper = lead.Species.Figure?.Role is PersonRole.Guard or PersonRole.Lawman;
        b.Stance = b.Turned ? Stance.Hostile : Temper.ByStanding(b.Temper.Stance, w.StandingAt?.Invoke(h.Center) ?? 0f, keeper);

        // Whom the leader has noticed (or, failing it, any of them); who hurt one of them (or a friend of theirs near); how many are down.
        ITarget seen = lead.Noticed, harm = null;
        int n = 0, lost = 0;
        bool armed = false;
        foreach (Creature m in h.Members)
        {
            n++;
            bool gone = m.Dead || m.Removed;
            if (gone || m.Health < m.Stats.MaxHealth * 0.3f) lost++;
            if (gone) continue;
            seen ??= m.Noticed;
            armed |= Weapons.Armed(m.Species.Figure?.Prop ?? "");
            if (m.ProvokedBy is { Attackable: true } by && m.ProvokedAt <= w.Time && m.ProvokedAt > b.HarmSeen) { harm = by; b.HarmSeen = m.ProvokedAt; }
        }
        if (seen != null) { b.Outsider = seen; b.OutsiderSeen = w.Time; }
        ITarget t = b.Outsider;
        float d = t != null ? Nearest(h, t.Position) : float.MaxValue;

        // Hurt: they turn on whoever did it (a band with no weapons runs from it), and never warm to them again.
        if (harm != null)
        {
            b.Turned = true;
            b.Stance = Stance.Hostile;
            if (Temper.LeaningOf(b.Temper.Stance) == Leaning.Friendly) b.Temper.Stance = Stance.Wary;
            if (armed) ToFight(w, h, b, lead, harm);
            else ToFlight(w, h, b, lead, harm.Position);
        }
        // Losing the fight: enough of them down or badly hurt, and the rest break and run.
        if (b.Mood == BandMood.Fighting && lost >= Math.Max(1, (int)MathF.Ceiling(n * 0.5f)))
        {
            b.BrokenUntil = w.Time + 45f;
            ToFlight(w, h, b, lead, b.Foe?.Position ?? h.Center);
        }
        // Something real: shot at, a gun held on them up close, a Husk or a killer animal near.
        if (Danger(w, h, b, t, d, out Vector3 from, out ITarget shooter, out Creature beast))
        {
            if (beast != null && CrossReactions.Respond(beast.Species, lead.Species.Figure, b.Temper, lead.Nerve, WildSim.Flat(beast.Position - h.Center).Length()) == Response.Fight) ToFight(w, h, b, lead, beast.AsTarget);
            else if (shooter != null && armed && b.Leaning != Leaning.Friendly) ToFight(w, h, b, lead, shooter);
            else
            {
                if (shooter != null && b.Leaning == Leaning.Friendly) b.Temper.Stance = Stance.Wary; // shot at: they won't trust you again
                ToFlight(w, h, b, lead, from);
            }
        }

        switch (b.Mood)
        {
            case BandMood.Fleeing:
                // Until the fright has passed (nothing new for a while), then they settle where they are.
                if (!h.Alarmed) Settle(w, h, b, lead, t != null && w.Time - b.OutsiderSeen < 2f ? BandMood.Watching : BandMood.Easy);
                return;
            case BandMood.Fighting:
            {
                ITarget foe = b.Foe;
                bool known = false;
                foreach (Creature m in h.Members)
                    if (m.Alive && !m.Removed && (m.Noticed == foe || m.ProvokedBy == foe && w.Time - m.ProvokedAt < 4f || m.Attacking)) { known = true; break; }
                if (known) b.FoeSeen = w.Time;
                if (foe == null || !foe.Attackable || (foe.Health?.Downed ?? false) || w.Time - b.FoeSeen > 8f || WildSim.Flat(foe.Position - h.Center).Length() > 70f)
                {
                    b.Foe = null;
                    Settle(w, h, b, lead, BandMood.Easy);
                }
                return;
            }
        }
        // They have lost you, or you have gone: they settle back to what they were doing. A band you hurt forgets the fight in a minute, never the harm.
        if (b.Turned && w.Time - b.FoeSeen > 60f) b.Turned = false;
        if (t == null || w.Time - b.OutsiderSeen > 4f || d > CalmOut)
        {
            if (b.Mood != BandMood.Easy) Settle(w, h, b, lead, BandMood.Easy);
            b.Outsider = null;
            return;
        }
        switch (b.Leaning)
        {
            case Leaning.Hostile:
                if (t.Attackable && w.Time > b.BrokenUntil && w.Time - b.OutsiderSeen < 1f) ToFight(w, h, b, lead, t);
                break;
            case Leaning.Wary:
            {
                // A gun drawn near them or on them, your threats, or coming on once warned: each is answered by the warning first, then by a fight.
                bool provoked = t.Drawn && d < 15f || AimedAtAny(h, t, AimReach) || w.Time - b.Threatened < 3f || b.Warnings > 0 && d < PushIn;
                if (b.Mood != BandMood.Warning && (d < WarnIn || provoked)) ToMood(w, h, b, lead, BandMood.Warning, BandEvent.Warn);
                else if (b.Mood == BandMood.Warning && provoked && b.WarnedAt >= b.MoodSince && w.Time - b.WarnedAt > WarnHold)
                {
                    b.Fights++;
                    ToFight(w, h, b, lead, t);
                }
                else if (b.Mood == BandMood.Warning && d > WarnIn + 6f && !provoked) ToMood(w, h, b, lead, BandMood.Watching, BandEvent.Calm);
                else if (b.Mood == BandMood.Easy) ToMood(w, h, b, lead, BandMood.Watching, BandEvent.Seen);
                break;
            }
            default:
                if (b.Mood == BandMood.Easy) ToMood(w, h, b, lead, BandMood.Watching, BandEvent.Seen);
                // Close enough to greet: they do, and a warm leader comes over to meet you.
                if (!b.Greeted && d < GreetIn)
                {
                    b.Greeted = true;
                    Event(w, b, lead, BandEvent.Greet);
                    if (b.Stance is Stance.Welcoming or Stance.Bargaining && lead.Nerve > 0.45f) b.MeetUntil = w.Time + 12f;
                }
                if (d > 20f) b.MeetUntil = -999f; // (they don't follow you off)
                break;
        }
    }

    static bool AimedAtAny(Herd h, ITarget t, float reach)
    {
        if (!t.Drawn) return false;
        foreach (Creature m in h.Members) if (m.Alive && !m.Removed && Aimed(t, m, reach)) return true;
        return false;
    }

    /// <summary>
    /// What they can't stand and face: a shot from someone passing close by one of them (or fired close
    /// to them); a gun held on them up close (a friendly band, held a moment or come close); a Husk, or
    /// an animal that could kill them, near. Where it is, and who or what.
    /// </summary>
    static bool Danger(WildSim w, Herd h, Band b, ITarget t, float d, out Vector3 from, out ITarget shooter, out Creature beast)
    {
        from = default;
        shooter = null;
        beast = null;
        foreach (WorldNoise n in w.Noises)
        {
            if (n.By == null || n.Time <= b.ShotHeard) continue;
            b.ShotHeard = n.Time;
            foreach (Creature m in h.Members)
            {
                if (!m.Alive || m.Removed) continue;
                Vector3 v = m.Position + Vector3.Up * 1.2f - n.Position;
                float along = n.Dir.LengthSquared() > 0.5f ? v.Dot(n.Dir) : -1f;
                if (v.Length() < ShotBy || along > 0f && along < n.Radius && (v - n.Dir * along).Length() < ShotPast)
                {
                    from = n.Position;
                    shooter = n.By;
                    return true;
                }
            }
        }
        if (t != null && b.Leaning == Leaning.Friendly && b.Mood != BandMood.Fleeing && AimedAtAny(h, t, AimClose))
        {
            if (b.AimedSince < 0f) b.AimedSince = w.Time;
            if (w.Time - b.AimedSince > 2.5f || d < 6f) { from = t.Position; return true; }
        }
        else b.AimedSince = -1f;
        float best = 25f;
        foreach (Creature o in w.Creatures)
        {
            if (o.Dead || o.Removed || o.Herd == h || o.Species.Figure != null && !o.Species.Husk || !(o.Species.Husk || CrossReactions.Danger(o.Species))) continue;
            float k = WildSim.Flat(o.Position - h.Center).Length();
            if (k < best) { best = k; beast = o; }
        }
        if (beast == null) return false;
        from = beast.Position;
        return CrossReactions.Respond(beast.Species, Leader(h).Species.Figure, b.Temper, Leader(h).Nerve, best) is Response.Fight or Response.Flee or Response.Barricade;
    }

    static void Event(WildSim w, Band b, Creature lead, BandEvent e)
    {
        b.Events++;
        b.Event = e;
        float d = b.Outsider != null ? WildSim.Flat(b.Outsider.Position - lead.Position).Length() : 10f;
        b.LeaderReactsAt = w.Time + Reaction.Delay(lead, d, saw: true) * 0.5f;
        lead.ThinkEvery = 0f;
    }

    static void ToMood(WildSim w, Herd h, Band b, Creature lead, BandMood mood, BandEvent e)
    {
        b.Mood = mood;
        b.MoodSince = w.Time;
        Event(w, b, lead, e);
    }

    static void ToFight(WildSim w, Herd h, Band b, Creature lead, ITarget foe)
    {
        b.Foe = foe;
        b.FoeSeen = w.Time;
        h.Alarmed = false;
        if (b.Mood != BandMood.Fighting) ToMood(w, h, b, lead, BandMood.Fighting, BandEvent.Fight);
    }

    static void ToFlight(WildSim w, Herd h, Band b, Creature lead, Vector3 from)
    {
        b.ThreatAt = from;
        h.Alarm(from, w.Time);
        if (b.Mood != BandMood.Fleeing) ToMood(w, h, b, lead, BandMood.Fleeing, BandEvent.Flee);
    }

    static void Settle(WildSim w, Herd h, Band b, Creature lead, BandMood mood)
    {
        b.Greeted = false;
        b.MeetUntil = -999f;
        b.AimedSince = -1f;
        ToMood(w, h, b, lead, mood, BandEvent.Calm);
    }

    // ---- Each member's part ------------------------------------------------------------------

    /// <summary>What a body does as it takes the band's news in: the leader points out who is there to the rest, greets, warns or calls the fight; the frightened panic.</summary>
    static void Took(WildSim w, Creature c, Band b, bool leader)
    {
        Figure f = c.Species.Figure;
        if (f == null) return;
        switch (b.Event)
        {
            case BandEvent.Seen:
                if (leader && b.Leaning != Leaning.Friendly) Hold(w, c, BodyAct.Point, 1.2f);
                break;
            case BandEvent.Greet:
                if (leader || c.Nerve > 0.4f) Hold(w, c, Temper.ActFor(leader ? b.Stance : b.Temper.PersonStance(c.Rng), f.Approach), 2.6f);
                break;
            case BandEvent.Warn:
                if (leader)
                {
                    // The warning, with a shout: it comes before anything else.
                    Hold(w, c, BodyAct.Warn, 2.4f);
                    b.WarnedAt = w.Time;
                    b.Warnings++;
                    w.RaiseWarned(c);
                }
                else if (c.Nerve > 0.5f) Hold(w, c, BodyAct.WaveOff, 1.8f);
                break;
            case BandEvent.Fight:
                if (leader)
                {
                    Hold(w, c, f.Approach == BodyAct.Taunt ? BodyAct.Taunt : BodyAct.Point, 1f);
                    w.RaiseWarned(c);
                }
                break;
            case BandEvent.Flee:
                if (c.Nerve < 0.4f || f.Stage == LifeStage.Child) Hold(w, c, BodyAct.Panic, 2f);
                else c.Act = BodyAct.None;
                break;
            case BandEvent.Calm:
                c.Act = BodyAct.None;
                break;
        }
    }

    /// <summary>Going about what they were: with the band to its goal, standing about between; stopped when you asked them to; looking where you pointed.</summary>
    static void CarryOn(WildSim w, Creature c, Herd h, Band b)
    {
        if (w.Time < b.HoldUntil)
        {
            c.SetState(CState.Idle);
            c.Desired = Vector3.Zero;
            c.FacePoint = b.HeardFrom;
        }
        else WithHerd(w, c, h, false);
        c.WantGraze = 0f;
        if (w.Time < b.LookUntil) c.LookAt = b.LookAt;
        // Standing about, now and then shifting from foot to foot.
        if (c.State == CState.Graze && c.Act == BodyAct.None && Mathf.PosMod(c.Age * 0.07f + c.Id * 0.37f, 1f) < 0.3f) Hold(w, c, BodyAct.Fidget, 3f);
    }

    /// <summary>
    /// A friendly band with you about: they carry on, heads turned to you, facing you when they stand;
    /// one with a gun held on them puts its hands up and keeps still; you come too close and they step
    /// aside, out of your way; their leader may come over to meet you.
    /// </summary>
    static void Easy(WildSim w, Creature c, Herd h, Band b, Creature lead)
    {
        ITarget t = b.Outsider;
        Vector3 to = WildSim.Flat(t.Position - c.Position);
        float d = to.Length();
        Figure f = c.Species.Figure;
        if (Aimed(t, c, AimClose * 1.5f))
        {
            c.SetState(CState.Freeze);
            c.Desired = Vector3.Zero;
            c.FacePoint = t.Position;
            c.LookAt = t.Position + Vector3.Up * 1.5f;
            Hold(w, c, f?.Aimed ?? BodyAct.HandsUp, 1f);
            return;
        }
        if (w.Time < b.BackUntil || d < Comfort)
        {
            Aside(c, t, to, d);
            return;
        }
        if (c == lead && w.Time < b.MeetUntil)
        {
            c.LookAt = t.Position + Vector3.Up * 1.5f;
            if (d > MeetAt)
            {
                c.SetState(CState.Follow);
                Steer(w, c, t.Position, c.Stats.WalkSpeed);
                return;
            }
            c.SetState(CState.Idle);
            c.Desired = Vector3.Zero;
            c.FacePoint = t.Position;
            if (c.Act == BodyAct.None && f != null) Hold(w, c, Temper.ActFor(b.Stance, f.Approach), 2.6f);
            return;
        }
        CarryOn(w, c, h, b);
        if (c.Desired.LengthSquared() < 0.04f) c.FacePoint = t.Position;
        if (w.Time >= b.LookUntil) c.LookAt = t.Position + Vector3.Up * 1.5f;
    }

    /// <summary>Out of your way: a step to the side you aren't going, and back, eyes on you.</summary>
    static void Aside(Creature c, ITarget t, Vector3 to, float d)
    {
        Vector3 away = d > 0.01f ? -to / d : c.Forward;
        Vector3 side = new(-away.Z, 0f, away.X);
        if (side.Dot(WildSim.Flat(t.Forward)) > 0f) side = -side;
        c.SetState(CState.Wander);
        c.Backing = true;
        c.Path.Clear();
        c.FacePoint = t.Position;
        c.LookAt = t.Position + Vector3.Up * 1.5f;
        c.Desired = (away * 0.6f + side * 0.8f).Normalized() * c.Stats.WalkSpeed * 0.7f;
    }

    /// <summary>A wary band with you about: stopped, facing you, hands to their weapons (up, once warned); too close and they back off with their eyes on you; the leader holds its warning up.</summary>
    static void Watch(WildSim w, Creature c, Band b, Creature lead)
    {
        ITarget t = b.Outsider;
        Vector3 to = WildSim.Flat(t.Position - c.Position);
        float d = to.Length();
        bool warning = b.Mood == BandMood.Warning;
        c.SetState(warning && c == lead ? CState.Warn : CState.Guard);
        c.FacePoint = t.Position;
        c.LookAt = t.Position + Vector3.Up * 1.5f;
        if (Weapons.Armed(c.Species.Figure?.Prop ?? "")) c.WantAim = warning ? 0.55f : 0.3f;
        if (d < KeepOff && d > 0.01f)
        {
            c.Backing = true;
            c.Path.Clear();
            c.Desired = -to / d * c.Stats.WalkSpeed * 0.7f;
        }
        else c.Desired = Vector3.Zero;
        if (warning && c == lead && c.Act == BodyAct.None && w.Time - b.WarnedAt < 5f) Hold(w, c, BodyAct.Warn, 1.2f);
    }

    /// <summary>
    /// In a fight: the young and the faint-hearted with nothing to fight with keep out of it; a gun
    /// is raised and fired from cover where there is some close by (beside a rock or a trunk, round
    /// from straight behind it so the shot clears its edge); the rest close in. Every attack still
    /// opens with its telegraph.
    /// </summary>
    static void Fight(WildSim w, Creature c, ITarget foe)
    {
        Figure f = c.Species.Figure;
        float d = WildSim.Flat(foe.Position - c.Position).Length();
        if (f != null && !Weapons.Armed(f.Prop) && (f.Stage == LifeStage.Child || c.Nerve < 0.4f))
        {
            Flee(w, c, foe.Position);
            Hold(w, c, f.Stage == LifeStage.Child ? BodyAct.Panic : BodyAct.Cower, 1f);
            return;
        }
        c.LookAt = foe.Position + Vector3.Up * 1.4f;
        if (f != null && Weapons.Ranged(f.Prop) && Arming.ShootReach(c, foe) is var reach && reach > 0f)
        {
            c.WantAim = 1f;
            if (Cover(w, c, foe, reach)) return;
            if (!c.Attacking && c.Phase == AttackPhase.None && c.HasGoal) c.WantCrouch = 0.5f; // low at its cover between shots
        }
        Hunt(w, c, foe, d);
    }

    /// <summary>To cover, if there is some within a few strides on this side of the foe and in reach of it: true while getting there.</summary>
    static bool Cover(WildSim w, Creature c, ITarget foe, float reach)
    {
        if (c.State != CState.Chase) c.SetState(CState.Chase);
        if (w.Time >= c.CoverIn)
        {
            c.CoverIn = w.Time + 3f;
            c.HasGoal = CoverNear(w, c, foe.Position, reach, out c.Goal);
        }
        if (!c.HasGoal || WildSim.Flat(c.Goal - c.Position).Length() < 0.8f) return false;
        Steer(w, c, c.Goal, c.Stats.RunSpeed);
        c.FacePoint = foe.Position;
        return true;
    }

    /// <summary>The nearest spot beside a rock or trunk, within about ten metres, covered from a threat yet within reach of it.</summary>
    static bool CoverNear(WildSim w, Creature c, Vector3 threat, float reach, out Vector3 spot)
    {
        spot = default;
        float best = 12f, cell = w.Nav.CellSize;
        int r = (int)MathF.Ceiling(10f / cell);
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (w.Nav.Near(c.Position + new Vector3(dx * cell, 0f, dz * cell)) is not { } near) continue;
            foreach (Obstacle o in near)
            {
                if (!o.Active || o.Radius < 0.25f) continue;
                Vector3 away = WildSim.Flat(o.Position - threat);
                float len = away.Length();
                if (len < 6f) continue;
                away /= len;
                // Round from straight behind it, on the side nearer this one: covered, with the edge to shoot past.
                Vector3 side = new(-away.Z, 0f, away.X);
                if (side.Dot(WildSim.Flat(c.Position - o.Position)) < 0f) side = -side;
                Vector3 at = o.Position + (away * 0.65f + side * 0.76f).Normalized() * (o.Radius + c.Radius + 0.3f);
                float k = WildSim.Flat(at - c.Position).Length(), fromThreat = WildSim.Flat(at - threat).Length();
                if (k >= best || fromThreat > reach * 0.9f || fromThreat < 6f) continue;
                best = k;
                spot = new Vector3(at.X, c.Position.Y, at.Z);
            }
        }
        return best < 12f;
    }

    /// <summary>Answering a gesture of yours in its own time, with its body; the leader's answer is what the band does about it.</summary>
    static void Answer(WildSim w, Creature c, Band b, Creature lead)
    {
        if (c.Answered == b.Gestures) return;
        float d = WildSim.Flat(b.HeardFrom - c.Position).Length();
        if (w.Time < b.HeardAt + 0.25f + (1f - c.Nerve) * 0.5f + d * 0.02f) return;
        c.Answered = b.Gestures;
        if (d > SeeGesture || c.Species.Figure is not { } f) return;
        var (reply, effect) = Gestures.Answer(b.Heard, b.Stance, f.Role, w.StandingAt?.Invoke(c.Position) ?? 0f, b.Mood == BandMood.Fighting || b.Leaning == Leaning.Hostile, c.Nerve);
        if (reply != BodyAct.None) Hold(w, c, reply, 2.6f);
        if (c != lead) return;
        switch (effect)
        {
            case GestureEffect.Calm:
                // Hands up: guns come down; a band you only crossed lets it go (one you hurt stays wary of you).
                if (b.Mood == BandMood.Fighting && b.Temper.Stance != Stance.Hostile && b.Foe == b.Outsider) { b.Turned = false; b.Foe = null; ToMood(w, c.Herd, b, lead, BandMood.Watching, BandEvent.Calm); }
                else if (b.Mood == BandMood.Warning) b.Mood = BandMood.Watching;
                b.Threatened = -999f;
                break;
            case GestureEffect.Keep: b.HoldUntil = w.Time + 4f; break;
            case GestureEffect.Back: b.BackUntil = w.Time + 3f; break;
            case GestureEffect.Come: if (b.Leaning == Leaning.Friendly) b.MeetUntil = w.Time + 12f; break;
            case GestureEffect.Look: b.LookUntil = w.Time + 3f; break;
            case GestureEffect.Alarm: b.Threatened = w.Time; break;
        }
    }

    /// <summary>You made a gesture (eye and look in this sim's frame): every band with one of them near enough to see it reads it.</summary>
    public static void Gestured(WildSim w, PlayerGesture g, Vector3 eye, Vector3 look)
    {
        foreach (Herd h in w.Herds)
        {
            if (h.Band is not { } b) continue;
            foreach (Creature m in h.Members)
            {
                if (m.Dead || m.Removed) continue;
                Vector3 to = WildSim.Flat(eye - m.Position);
                float d = to.Length();
                if (d > SeeGesture || d > 6f && m.Forward.Dot(to / d) < -0.2f) continue;
                b.Heard = g;
                b.Gestures++;
                b.HeardAt = w.Time;
                b.HeardFrom = eye;
                b.LookAt = eye + look * 12f;
                break;
            }
        }
    }
}
