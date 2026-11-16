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
    /// <summary>Seconds a run to cover may take before it's given up.</summary>
    const float CoverRun = 4f;
    /// <summary>How far a band sees others in the open by day (half by night), m; how often it looks round, s; below this share of health a body runs on its own; within this the brave stand against what the band runs from, m.</summary>
    const float SeeFar = 60f, ScanEvery = 0.5f, HurtRun = 0.3f, StandIn = 25f;
    /// <summary>How far round a danger a band's way steps at a time, radians.</summary>
    const float RoundStep = 0.8f;
    static readonly float AimCone = MathF.Cos(Mathf.DegToRad(7f));

    /// <summary>
    /// One of a band of people (<see cref="Party"/>): the band decides, once a step, what it makes of
    /// whoever is about; each member takes that in its own time (the leader first, the rest from it)
    /// and does its part: carries on, steps aside, watches and backs off, warns, fights, or runs.
    /// </summary>
    static void Folk(WildSim w, Creature c, float dt)
    {
        Herd h = c.Herd ?? Alone(w, c);
        Party b = h.Party ??= Party.Roll(c.Species, Rng.Mix(c.Species.Seed, (ulong)c.Id));
        UpdateHerd(w, h, dt);
        Creature lead = Leader(h) ?? c;
        UpdateBand(w, h, b, lead);
        c.WantAim = 0f;
        if (w.Time > c.ActUntil) c.Act = BodyAct.None;
        // Each takes in what the band does in its own time: the leader first, the rest once it has.
        if (c.PartySeen != b.Events)
        {
            c.PartySeen = b.Events;
            c.ReactAt = c == lead ? b.LeaderReactsAt : float.MaxValue;
        }
        if (c != lead && c.ReactAt == float.MaxValue && lead.PartySeen == b.Events && lead.ReactedAt >= lead.ReactAt)
            c.ReactAt = lead.ReactedAt + Reaction.Delay(c, WildSim.Flat(lead.Position - c.Position).Length(), saw: c.Noticed != null);
        bool reacted = w.Time >= c.ReactAt;
        if (reacted && c.ReactedAt < c.ReactAt)
        {
            c.ReactedAt = w.Time;
            if (c != lead) { if (lead.ReactedAt <= c.ReactedAt && lead.PartySeen == b.Events) b.AfterLeader++; else b.BeforeLeader++; }
            Took(w, c, b, c == lead);
        }
        Answer(w, c, b, lead);
        // The band's call is each one's default, and each answers it by their own nerve. Badly hurt, in a fight or a fright, they break off and run on their own.
        ITarget danger = b.Mood == PartyMood.Fighting ? b.Foe : b.Threat;
        if (reacted && danger != null && b.Mood is PartyMood.Fighting or PartyMood.Fleeing && c.Health < c.Stats.MaxHealth * HurtRun)
        {
            Flee(w, c, danger.Position);
            if (c.Act == BodyAct.None) Hold(w, c, BodyAct.Cower, 1f);
            return;
        }
        if (b.Mood == PartyMood.Fleeing)
        {
            // The brave with a weapon stand against what the band runs from while it's in reach; until it takes the fright in, the head comes round to it; then it runs with the band.
            if (reacted && b.Threat is { Attackable: true } th && WildSim.Flat(th.Position - c.Position).Length() < StandIn && Stands(c, b, th)) Fight(w, c, b, th);
            else if (reacted) WithHerd(w, c, h, h.Alarmed);
            else { c.SetState(CState.Idle); c.Desired = Vector3.Zero; c.LookAt = b.ThreatAt + Vector3.Up; }
            return;
        }
        if (reacted && b.Mood == PartyMood.Fighting && b.Foe != null) { Fight(w, c, b, b.Foe); return; }
        if (reacted && b.Mood is PartyMood.Watching or PartyMood.Warning && b.Outsider != null)
        {
            // Other people they only keep an eye on as they go, till it comes to a warning.
            if (b.Leaning == Leaning.Friendly || b.Mood == PartyMood.Watching && b.Outsider is CreatureTarget) Easy(w, c, h, b, lead);
            else Watch(w, c, b, lead);
            return;
        }
        CarryOn(w, c, h, b);
    }

    /// <summary>Whether this one stands and fights a foe by their own nerve (<see cref="CrossReactions.Respond"/>), rather than run: what's coming at them as near as it is in the fight.</summary>
    static bool Stands(Creature c, Party b, ITarget foe)
    {
        if (c.Species.Figure == null) return true;
        if (foe is not CreatureTarget { Creature: var o }) return CrossReactions.Stands(c.Species, b.Temper, c.Nerve, animal: false);
        return CrossReactions.Respond(o.Species, c.Species, b.Temper, c.Nerve, MathF.Min(WildSim.Flat(o.Position - c.Position).Length(), 15f)) == Response.Fight;
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

    /// <summary>Whoever leads a band: the first grown one of it still standing (a child only when no one else is).</summary>
    public static Creature Leader(Herd h)
    {
        Creature child = null;
        foreach (Creature m in h.Members)
        {
            if (!m.Alive || m.Removed) continue;
            if (m.Species.Figure?.Stage != LifeStage.Child) return m;
            child ??= m;
        }
        return child;
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

    /// <summary>The one of a band nearest a point, and how far (flat).</summary>
    static Creature Closest(Herd h, Vector3 p, out float best)
    {
        best = float.MaxValue;
        Creature near = null;
        foreach (Creature m in h.Members)
        {
            if (!m.Alive || m.Removed) continue;
            float d = WildSim.Flat(m.Position - p).Length();
            if (d < best) { best = d; near = m; }
        }
        return near;
    }

    // ---- The band's mind, once a step --------------------------------------------------------

    static void UpdateBand(WildSim w, Herd h, Party b, Creature lead)
    {
        if (w.Time - b.UpdatedAt < 0.15f) return;
        b.UpdatedAt = w.Time;
        // What's about in the open round them: a look twice a second.
        if (w.Time >= b.ScanAt) Scan(w, h, b, lead);

        // Whom the leader has noticed (or, failing it, any of them); who hurt one of them (or a friend of theirs near); how many are down.
        ITarget seen = lead.Noticed, harm = null;
        int n = 0, lost = 0;
        bool armed = false;
        foreach (Creature m in h.Members)
        {
            n++;
            bool gone = m.Dead || m.Removed;
            if (gone || m.Health < m.Stats.MaxHealth * HurtRun) lost++;
            if (gone) continue;
            seen ??= m.Noticed;
            armed |= Weapons.Armed(m.Species.Figure?.Prop ?? "");
            if (m.ProvokedBy is { Attackable: true } by && m.ProvokedAt <= w.Time && m.ProvokedAt > b.HarmSeen) { harm = by; b.HarmSeen = m.ProvokedAt; }
        }
        // Another band is whom they deal with when there's nobody else, or when it's nearer and one of them would prey on the other.
        if (b.Stranger is { Attackable: true } other && (seen == null || Toward(b, other) != Leaning.Friendly && Flat(other, h) < Flat(seen, h))) seen = other;
        if (seen != null) { b.Outsider = seen; b.OutsiderSeen = w.Time; }
        ITarget t = b.Outsider;
        float d = float.MaxValue;
        if (t != null) Closest(h, t.Position, out d);
        // Their stance with whoever it is: another band by what each is; you by the roll, moved by your standing with their people, hostile while turned.
        bool keeper = lead.Species.Figure?.Role is PersonRole.Guard or PersonRole.Lawman;
        b.Stance = t is CreatureTarget { Creature.Herd.Party: { } them } ? StanceFor(b.Toward(them), b.Temper.Stance)
            : b.Turned ? Stance.Hostile : Temper.ByStanding(b.Temper.Stance, w.StandingAt?.Invoke(h.Center) ?? 0f, keeper);

        // Hurt: they turn on whoever did it (a band with no weapons runs from it); hurt by you, they never warm to you again.
        if (harm != null)
        {
            if (harm is not (CreatureTarget or IHost))
            {
                b.Turned = true;
                b.Stance = Stance.Hostile;
                if (Temper.LeaningOf(b.Temper.Stance) == Leaning.Friendly) b.Temper.Stance = Stance.Wary;
            }
            if (armed) ToFight(w, h, b, lead, harm);
            else ToFlight(w, h, b, lead, harm.Position);
        }
        // Losing the fight: enough of them down or badly hurt, and the rest break and run.
        if (b.Mood == PartyMood.Fighting && lost >= Math.Max(1, (int)MathF.Ceiling(n * 0.5f)))
        {
            b.BrokenUntil = w.Time + 45f;
            ToFlight(w, h, b, lead, b.Foe?.Position ?? h.Center);
        }
        // Something real: shot at, a gun held on them up close, a danger seen near enough to fight or run from.
        Response danger = Danger(w, h, b, lead, t, d, out Vector3 from, out ITarget shooter);
        if (shooter != null)
        {
            // Shot at: an armed band shoots back (at you, unless it's friendly to you: then it runs, and won't trust you again); one with no weapons runs.
            bool you = shooter is not (CreatureTarget or IHost);
            if (armed && (!you || b.Leaning != Leaning.Friendly)) ToFight(w, h, b, lead, shooter);
            else
            {
                if (you && b.Leaning == Leaning.Friendly) b.Temper.Stance = Stance.Wary;
                ToFlight(w, h, b, lead, from);
            }
        }
        else if (danger == Response.Fight) ToFight(w, h, b, lead, b.Threat);
        else if (danger == Response.Flee) ToFlight(w, h, b, lead, from);

        switch (b.Mood)
        {
            case PartyMood.Fleeing:
                // Until the fright has passed (nothing new for a while), then they settle where they are.
                if (!h.Alarmed) Settle(w, h, b, lead, t != null && w.Time - b.OutsiderSeen < 2f ? PartyMood.Watching : PartyMood.Easy);
                return;
            case PartyMood.Fighting:
            {
                ITarget foe = b.Foe;
                bool known = foe != null && (foe == b.Threat || foe == b.Stranger || foe is CreatureTarget or IHost && Flat(foe, h) < SeeFar * 0.6f);
                foreach (Creature m in h.Members)
                    if (m.Alive && !m.Removed && (m.Noticed == foe || m.ProvokedBy == foe && w.Time - m.ProvokedAt < 4f || m.Attacking)) { known = true; break; }
                if (known) b.FoeSeen = w.Time;
                if (foe == null || !foe.Attackable || (foe.Health?.Downed ?? false) || w.Time - b.FoeSeen > 8f || Flat(foe, h) > 70f)
                {
                    // One of a band down or gone: on to the next of them still standing near; else it's over.
                    if (foe is CreatureTarget { Creature.Herd: { } theirs } && Closest(theirs, h.Center, out float next) is { } one && next < SeeFar * 0.8f) ToFight(w, h, b, lead, one.AsTarget);
                    else
                    {
                        b.Foe = null;
                        Settle(w, h, b, lead, PartyMood.Easy);
                    }
                }
                // A band that was only defending itself lets it go once you back off with your gun off them.
                else if (b.Leaning != Leaning.Hostile && foe == t && d > WarnIn && !AimedAtAny(h, foe, AimReach))
                {
                    b.Foe = null;
                    ToMood(w, h, b, lead, PartyMood.Watching, PartyEvent.Calm);
                }
                return;
            }
        }
        // Keeping away: a danger they've seen bends their way round it before it's close, by the leader's nerve and their arms.
        if (b.Threat != null && danger != Response.Fight) Bend(w, h, b, b.Threat.Position, CrossReactions.Berth(lead.Nerve, armed));
        else if (b.Bending && h.Goal == b.BentTo) { h.Goal = b.Bound; b.Bending = false; }
        // They have lost you, or you have gone: they settle back to what they were doing. A band you hurt forgets the fight in a minute, never the harm.
        if (b.Turned && w.Time - b.FoeSeen > 60f) b.Turned = false;
        if (t == null || w.Time - b.OutsiderSeen > 4f || d > CalmOut)
        {
            if (b.Mood != PartyMood.Easy) Settle(w, h, b, lead, PartyMood.Easy);
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
                // A gun drawn near them or on them, your threats, or coming on once warned (you, not one of them wandering your way): each is answered by the warning first, then by a fight.
                bool coming = b.Warnings > 0 && d < PushIn && WildSim.Flat(b.WarnedFrom - h.Center).Length() - WildSim.Flat(t.Position - h.Center).Length() > 2f;
                bool provoked = t.Drawn && d < 15f || AimedAtAny(h, t, AimReach) || w.Time - b.Threatened < 3f || coming;
                if (b.Mood != PartyMood.Warning && (d < WarnIn || provoked)) ToMood(w, h, b, lead, PartyMood.Warning, PartyEvent.Warn);
                else if (b.Mood == PartyMood.Warning && provoked && b.WarnedAt >= b.MoodSince && w.Time - b.WarnedAt > WarnHold) ToFight(w, h, b, lead, t);
                else if (b.Mood == PartyMood.Warning && d > WarnIn + 6f && !provoked) ToMood(w, h, b, lead, PartyMood.Watching, PartyEvent.Calm);
                else if (b.Mood == PartyMood.Easy) ToMood(w, h, b, lead, PartyMood.Watching, PartyEvent.Seen);
                break;
            }
            default:
                if (b.Mood == PartyMood.Easy) ToMood(w, h, b, lead, PartyMood.Watching, PartyEvent.Seen);
                // Close enough to greet: they do, and a warm leader comes over to meet you.
                if (!b.Greeted && d < GreetIn)
                {
                    b.Greeted = true;
                    Event(w, b, lead, PartyEvent.Greet);
                    if (b.Stance is Stance.Welcoming or Stance.Bargaining && lead.Nerve > 0.45f && t is not CreatureTarget) b.MeetUntil = w.Time + 12f; // (never another band)
                }
                if (d > 20f) b.MeetUntil = -999f; // (they don't follow you off)
                break;
        }
    }

    static float Flat(ITarget t, Herd h) => WildSim.Flat(t.Position - h.Center).Length();

    /// <summary>How the band takes whoever it deals with: another band by what each is; anyone else by its stance now.</summary>
    static Leaning Toward(Party b, ITarget t) => t is CreatureTarget { Creature.Herd.Party: { } them } ? b.Toward(them) : b.Leaning;

    /// <summary>The stance a leaning toward another band comes to: their own where it agrees, else the plainest of its kind.</summary>
    static Stance StanceFor(Leaning l, Stance own) => Temper.LeaningOf(own) == l ? own : l switch { Leaning.Hostile => Stance.Hostile, Leaning.Wary => Stance.Wary, _ => Stance.Indifferent };

    /// <summary>
    /// A band's look round (twice a second, from the cells round it, <see cref="Sightings"/>): the nearest
    /// danger it can see (a predator, a Husk, a feral, or people who'd do it harm: who'd prey on it, or are
    /// warning it off) and the nearest people of another band, their own kind left out. By night it sees
    /// half as far.
    /// </summary>
    static void Scan(WildSim w, Herd h, Party b, Creature lead)
    {
        b.ScanAt = w.Time + ScanEvery;
        b.Threat = b.Stranger = null;
        float see = SeeFar * (w.Clock.IsNight ? 0.5f : 1f), near = see, nearest = see;
        Creature danger = null, other = null;
        foreach (Creature o in w.Sightings.Near(h.Center, see))
        {
            if (o.Herd == h) continue;
            float d = WildSim.Flat(o.Position - h.Center).Length();
            if (o.Herd?.Party is { } them && CrossReactions.People(o.Species))
            {
                if (them.Kind == b.Kind) continue;
                if (d < nearest) { nearest = d; other = o; }
                bool warned = them.Mood == PartyMood.Warning && them.Outsider is CreatureTarget { Creature.Herd: var at } && at == h;
                if (them.Toward(b) != Leaning.Hostile && !warned) continue;
            }
            if (d < near) { near = d; danger = o; }
        }
        if (danger != null && Seen(w, lead, danger)) b.Threat = danger.AsTarget;
        if (other != null && Seen(w, lead, other)) b.Stranger = other.AsTarget;
    }

    /// <summary>A clear line from the leader's eyes to its middle.</summary>
    static bool Seen(WildSim w, Creature lead, Creature o) =>
        Senses.LineClear(w, lead, o.AsTarget, lead.Position + Vector3.Up * (lead.Species.Figure?.Height ?? 1.7f) * 0.92f, o.Position + Vector3.Up * MathF.Max(0.4f, o.Species.BodyHeight * 0.6f));

    /// <summary>
    /// Keeping away from a danger they've seen: while the way they mean to go would take them within their
    /// berth of it, they go round it instead, a step at a time round a ring a little wider than the berth,
    /// on the side they're already on (inside it, outward and round at once). Where they were going is too
    /// near it: they go only as near as the berth. Clear of it, they go on where they were going.
    /// </summary>
    static void Bend(WildSim w, Herd h, Party b, Vector3 danger, float berth)
    {
        Vector3 bound = b.Bending && h.Goal == b.BentTo ? b.Bound : h.Goal; // (the herd picks a new goal now and then)
        Vector3 off = WildSim.Flat(h.Center - danger), to = WildSim.Flat(bound - danger), way = to - off;
        float d = off.Length(), len = way.Length();
        // The nearest the way comes to it.
        float along = len > 0.5f ? Math.Clamp(-off.Dot(way) / len, 0f, len) : 0f;
        if ((off + (len > 0.5f ? way / len * along : Vector3.Zero)).Length() >= berth)
        {
            if (b.Bending) h.Goal = bound;
            b.Bending = false;
            return;
        }
        float ring = berth + 4f;
        Vector3 at;
        if (to.Length() < berth) at = (to.LengthSquared() > 0.01f ? to.Normalized() : off / MathF.Max(d, 0.1f)) * ring;
        else
        {
            // A step round from where they are, toward where they're going, whichever way round is shorter.
            Vector3 radial = d > 0.1f ? off / d : Vector3.Right, toward = to.Normalized();
            float step = MathF.Min(RoundStep, MathF.Acos(Math.Clamp(radial.Dot(toward), -1f, 1f)));
            Vector3 left = radial.Rotated(Vector3.Up, step), right = radial.Rotated(Vector3.Up, -step);
            at = (left.Dot(toward) >= right.Dot(toward) ? left : right) * ring;
        }
        b.Bound = bound;
        b.Bending = true;
        h.Goal = b.BentTo = w.Terrain.Clamp(danger + at, 20f);
    }

    static bool AimedAtAny(Herd h, ITarget t, float reach)
    {
        if (!t.Drawn) return false;
        foreach (Creature m in h.Members) if (m.Alive && !m.Removed && Aimed(t, m, reach)) return true;
        return false;
    }

    /// <summary>
    /// What they can't stand and face: a shot from someone passing close by one of them (or fired close
    /// to them: not by one of their own); a gun held on them up close (a friendly band, held a moment or
    /// come close); the danger they last saw, as the leader answers it (<see cref="CrossReactions.Respond"/>):
    /// fought, run from, kept wide of, or watched. People who'd do them harm aren't fought on sight: the
    /// warning comes first. Where it is, and who shot.
    /// </summary>
    static Response Danger(WildSim w, Herd h, Party b, Creature lead, ITarget t, float d, out Vector3 from, out ITarget shooter)
    {
        from = default;
        shooter = null;
        foreach (WorldNoise n in w.Noises)
        {
            if (n.By == null || n.Time <= b.ShotHeard) continue;
            b.ShotHeard = n.Time;
            if (n.By is CreatureTarget { Creature.Herd: var by } && by == h) continue;
            foreach (Creature m in h.Members)
            {
                if (!m.Alive || m.Removed) continue;
                Vector3 v = m.Position + Vector3.Up * 1.2f - n.Position;
                float along = n.Dir.LengthSquared() > 0.5f ? v.Dot(n.Dir) : -1f;
                if (v.Length() < ShotBy || along > 0f && along < n.Radius && (v - n.Dir * along).Length() < ShotPast)
                {
                    from = n.Position;
                    shooter = n.By;
                    return Response.Flee;
                }
            }
        }
        if (t != null && b.Leaning == Leaning.Friendly && b.Mood != PartyMood.Fleeing && AimedAtAny(h, t, AimClose))
        {
            if (b.AimedSince < 0f) b.AimedSince = w.Time;
            if (w.Time - b.AimedSince > 2.5f || d < 6f) { from = t.Position; return Response.Flee; }
        }
        else b.AimedSince = -1f;
        if (b.Threat is not CreatureTarget { Attackable: true, Creature: var o } || lead.Species.Figure == null) return Response.None;
        from = o.Position;
        Response r = CrossReactions.Respond(o.Species, lead.Species, b.Temper, lead.Nerve, WildSim.Flat(o.Position - h.Center).Length());
        if (r == Response.Fight && CrossReactions.People(o.Species)) return Response.Watch;
        return r == Response.Barricade ? Response.Flee : r;
    }

    static void Event(WildSim w, Party b, Creature lead, PartyEvent e)
    {
        b.Events++;
        b.Event = e;
        float d = b.Outsider != null ? WildSim.Flat(b.Outsider.Position - lead.Position).Length() : 10f;
        b.LeaderReactsAt = w.Time + Reaction.Delay(lead, d, saw: true) * 0.5f;
        lead.ThinkEvery = 0f;
    }

    static void ToMood(WildSim w, Herd h, Party b, Creature lead, PartyMood mood, PartyEvent e)
    {
        b.Mood = mood;
        b.MoodSince = w.Time;
        Event(w, b, lead, e);
    }

    static void ToFight(WildSim w, Herd h, Party b, Creature lead, ITarget foe)
    {
        b.Foe = foe;
        b.FoeSeen = w.Time;
        h.Alarmed = false;
        if (b.Mood != PartyMood.Fighting) ToMood(w, h, b, lead, PartyMood.Fighting, PartyEvent.Fight);
    }

    static void ToFlight(WildSim w, Herd h, Party b, Creature lead, Vector3 from)
    {
        b.ThreatAt = from;
        h.Alarm(from, w.Time);
        if (b.Mood != PartyMood.Fleeing) ToMood(w, h, b, lead, PartyMood.Fleeing, PartyEvent.Flee);
    }

    static void Settle(WildSim w, Herd h, Party b, Creature lead, PartyMood mood)
    {
        b.Greeted = false;
        b.MeetUntil = -999f;
        b.AimedSince = -1f;
        ToMood(w, h, b, lead, mood, PartyEvent.Calm);
    }

    // ---- Each member's part ------------------------------------------------------------------

    /// <summary>What a body does as it takes the band's news in: the leader points out who is there to the rest, greets, warns or calls the fight; the frightened panic.</summary>
    static void Took(WildSim w, Creature c, Party b, bool leader)
    {
        Figure f = c.Species.Figure;
        if (f == null) return;
        switch (b.Event)
        {
            case PartyEvent.Seen:
                if (leader && b.Leaning != Leaning.Friendly) Hold(w, c, BodyAct.Point, 1.2f);
                break;
            case PartyEvent.Greet:
                if (leader || c.Nerve > 0.4f) Hold(w, c, Temper.ActFor(leader ? b.Stance : b.Temper.PersonStance(c.Rng), f.Approach), 2.6f);
                break;
            case PartyEvent.Warn:
                if (leader)
                {
                    // The warning, with a shout: it comes before anything else.
                    Hold(w, c, BodyAct.Warn, 2.4f);
                    b.WarnedAt = w.Time;
                    b.WarnedFrom = b.Outsider?.Position ?? c.Position;
                    b.Warnings++;
                    w.RaiseWarned(c);
                }
                else if (c.Nerve > 0.5f) Hold(w, c, BodyAct.WaveOff, 1.8f);
                break;
            case PartyEvent.Fight:
                if (leader)
                {
                    Hold(w, c, f.Approach == BodyAct.Taunt ? BodyAct.Taunt : BodyAct.Point, 1f);
                    w.RaiseWarned(c);
                }
                break;
            case PartyEvent.Flee:
                if (c.Nerve < 0.4f || f.Stage == LifeStage.Child) Hold(w, c, BodyAct.Panic, 2f);
                else c.Act = BodyAct.None;
                break;
            case PartyEvent.Calm:
                c.Act = BodyAct.None;
                break;
        }
    }

    /// <summary>Going about what they were: with the band to its goal, standing about between; stopped when you asked them to; looking where you pointed.</summary>
    static void CarryOn(WildSim w, Creature c, Herd h, Party b)
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
    static void Easy(WildSim w, Creature c, Herd h, Party b, Creature lead)
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
    static void Watch(WildSim w, Creature c, Party b, Creature lead)
    {
        ITarget t = b.Outsider;
        Vector3 to = WildSim.Flat(t.Position - c.Position);
        float d = to.Length();
        bool warning = b.Mood == PartyMood.Warning;
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
    /// In a fight: one they picked, the young and the faint-hearted with nothing to fight with keep out
    /// of it; one that came to them, each stands or runs by their own nerve (never a child against an
    /// animal; never with nothing in their hands); a gun
    /// is raised and fired from cover where there is some close by (beside a rock or a trunk, round
    /// from straight behind it so the shot clears its edge); the rest close in. A band that isn't
    /// hostile (a wary one you pushed) only defends itself: it holds its ground and fights you where
    /// you stand, and never comes after you. Every attack still opens with its telegraph.
    /// </summary>
    static void Fight(WildSim w, Creature c, Party b, ITarget foe)
    {
        Figure f = c.Species.Figure;
        float d = WildSim.Flat(foe.Position - c.Position).Length();
        bool animal = foe is CreatureTarget { Creature.Species: var s } && !CrossReactions.People(s);
        bool picked = b.Nature == Leaning.Hostile && foe == b.Foe;
        if (f != null && (f.Stage == LifeStage.Child && animal || (picked ? !Weapons.Armed(f.Prop) && (f.Stage == LifeStage.Child || c.Nerve < 0.4f) : !Stands(c, b, foe))))
        {
            Flee(w, c, foe.Position);
            Hold(w, c, f.Stage == LifeStage.Child ? BodyAct.Panic : BodyAct.Cower, 1f);
            return;
        }
        c.LookAt = foe.Position + Vector3.Up * 1.4f;
        float reach = f != null && Weapons.Ranged(f.Prop) ? Arming.ShootReach(c, foe) : 0f;
        if (reach > 0f)
        {
            c.WantAim = 1f;
            if (Cover(w, c, foe, reach)) return;
            if (!c.Attacking && c.Phase == AttackPhase.None && c.HasGoal) c.WantCrouch = 0.5f; // low at its cover between shots
        }
        else if (b.Leaning != Leaning.Hostile && foe == b.Outsider && d > PushIn + 1f)
        {
            c.SetState(CState.Guard);
            c.Desired = Vector3.Zero;
            c.FacePoint = foe.Position;
            if (f != null && Weapons.Armed(f.Prop)) c.WantAim = 0.8f;
            return;
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
            Vector3 was = c.Goal;
            bool had = c.HasGoal;
            c.HasGoal = CoverNear(w, c, foe.Position, reach, out c.Goal);
            // A new spot gets a few seconds to reach; the same spot picked again doesn't get more.
            if (c.HasGoal && (!had || WildSim.Flat(c.Goal - was).Length() > 1.5f)) c.CoverUntil = w.Time + CoverRun;
        }
        if (!c.HasGoal || WildSim.Flat(c.Goal - c.Position).Length() < 0.8f) return false;
        if (w.Time > c.CoverUntil)
        {
            // It couldn't get there (round a rock it can't pass): it stands and fights, and looks again later.
            c.HasGoal = false;
            c.CoverIn = w.Time + 6f;
            return false;
        }
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
    static void Answer(WildSim w, Creature c, Party b, Creature lead)
    {
        if (c.Answered == b.Gestures) return;
        float d = WildSim.Flat(b.HeardFrom - c.Position).Length();
        if (w.Time < b.HeardAt + 0.25f + (1f - c.Nerve) * 0.5f + d * 0.02f) return;
        c.Answered = b.Gestures;
        if (d > SeeGesture || c.Species.Figure is not { } f) return;
        var (reply, effect) = Gestures.Answer(b.Heard, b.Stance, f.Role, w.StandingAt?.Invoke(c.Position) ?? 0f, b.Mood == PartyMood.Fighting || b.Leaning == Leaning.Hostile, c.Nerve);
        if (reply != BodyAct.None) Hold(w, c, reply, 2.6f);
        if (c != lead) return;
        switch (effect)
        {
            case GestureEffect.Calm:
                // Hands up: guns come down; a band you only crossed lets it go (one you hurt stays wary of you).
                if (b.Mood == PartyMood.Fighting && b.Temper.Stance != Stance.Hostile && b.Foe == b.Outsider) { b.Turned = false; b.Foe = null; ToMood(w, c.Herd, b, lead, PartyMood.Watching, PartyEvent.Calm); }
                else if (b.Mood == PartyMood.Warning) b.Mood = PartyMood.Watching;
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
            if (h.Party is not { } b) continue;
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
