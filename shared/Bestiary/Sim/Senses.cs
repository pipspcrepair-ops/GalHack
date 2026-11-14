using System;
using Godot;

namespace Wildlife;

/// <summary>A loud sound in the world (a shot): whatever is in earshot turns toward it. A shot also says who fired it and which way.</summary>
public struct WorldNoise
{
    public Vector3 Position;
    public float Radius, Time;
    public Vector3 Dir;
    public ITarget By;
}

/// <summary>
/// What a creature can tell of a player. Two senses, kept apart:
/// - Sight needs the target inside its range and its cone, with a clear line over the ground and
///   through the structures round it. A blind creature has none.
/// - Hearing is shorter, works all round, and depends on how much noise the target makes (standing
///   still next to nothing, a sprint more). A wall all but stops it: only a short way through.
/// Each species hears and sees by its own <see cref="SenseProfile"/>: sight goes with the light on
/// the target (so shadow hides you and a torch gives you away) unless its eyes are made for the
/// dark; echolocation sees shape with no light at all, a short way, and clicks as it does; and
/// scent, outdoors, carries on the wind, far downwind and hardly at all upwind.
/// Each think that it senses something its awareness climbs, faster the closer and the plainer it
/// is, and it only notices at the top; unsensed it falls away slowly. It then holds on to where it
/// last knew the target to be for a few seconds, and goes there, not to where the target has got to.
/// Nothing is alerted by anything it can't sense: no distance check alone sets any of this.
/// </summary>
public static class Senses
{
    /// <summary>Seconds it holds on to something it can no longer sense.</summary>
    public const float Memory = 5f;
    /// <summary>Hearing as a share of sight's range, for the sighted and the blind.</summary>
    public const float Hearing = 0.45f, BlindHearing = 0.7f;
    /// <summary>The share of hearing that gets through a wall.</summary>
    public const float ThroughWall = 0.3f;
    /// <summary>Half the field of view, as a cosine: prey sees most of the way round, a hunter ahead of it.</summary>
    public static readonly float PreyCone = MathF.Cos(Mathf.DegToRad(110f)), HunterCone = MathF.Cos(Mathf.DegToRad(70f));
    /// <summary>Seconds to notice someone plainly seen: quick in the open, slower for what lives in places.</summary>
    public const float NoticeOutside = 0.45f, NoticeInside = 1.1f;
    /// <summary>Awareness at which it pricks up and looks, though it hasn't placed you yet.</summary>
    public const float Curious = 0.35f;
    /// <summary>How high above its feet a crouched target is sighted, m: low cover hides it.</summary>
    public const float CrouchedHead = 0.95f;
    /// <summary>How far a good nose reaches in still air, as a share of its perception.</summary>
    public const float ScentReach = 0.5f;

    /// <summary>A surface creature: its sight and hearing, by its perception.</summary>
    public static void Surface(WildSim w, Creature c, ITarget t, float d, float dt)
    {
        Stats st = c.Stats;
        bool resting = c.State == CState.Rest;
        float scale = t?.ThreatScale ?? 1f;
        SenseProfile p = c.Species.Senses;
        float sight = resting ? 0f : st.Perception * scale * p.Sight;
        float hearing = MathF.Max(st.Perception * Hearing, MathF.Max(st.AmbushRadius, st.AttackRange + 1f)) * scale * (resting ? 0.5f : 1f) * p.Hearing;
        Vector3 eye = c.Position + Vector3.Up * (c.Species.Flies ? 0f : c.Species.BodyHeight * 0.85f);
        // An ambusher lying hidden faces nothing in particular: all round.
        float cone = c.Species.Behaviour == Behaviour.Ambusher ? -1.01f : c.Species.IsHunter ? HunterCone : PreyCone;
        Update(w, c, t, d, dt, sight, hearing, eye, c.Forward, cone, NoticeOutside, false, st.Perception * ScentReach * p.Scent);
    }

    /// <summary>A creature of a cave or a place: by its notice radius, hidden ones listening at their holes.</summary>
    public static void Inside(WildSim w, Creature c, ITarget t, float d, float dt)
    {
        Species s = c.Species;
        float noticed = t != null && c.Noticed == t ? 1.6f : 1f; // once it has you, it keeps you further off
        float radius = s.NoticeRadius * (t?.ThreatScale ?? 1f) * noticed;
        Vector3 eye = c.InWalls ? c.InCrack.Position + c.InCrack.Normal * 0.4f : Crawl.Center(c) + c.Facing.Y * 0.2f;
        // Waiting in the dark or in a hole it faces nothing in particular: all round.
        bool lying = c.InWalls || s.Role is DeepRole.Ambusher or DeepRole.Crawler or DeepRole.Lurker;
        float cone = lying ? -1.01f : s.HuntsPrey ? HunterCone : PreyCone;
        Update(w, c, t, d, dt, s.Blind ? 0f : radius * s.Senses.Sight, radius * (s.Blind ? BlindHearing : Hearing) * s.Senses.Hearing, eye, -c.Facing.Z, cone, NoticeInside, true, 0f);
    }

    static void Update(WildSim w, Creature c, ITarget t, float d, float dt, float sight, float hearing, Vector3 eye, Vector3 facing, float cone, float notice, bool inside, float scent)
    {
        foreach (WorldNoise n in w.Noises)
        {
            if (n.Time <= c.HeardAt || (n.Position - c.Position).Length() > n.Radius) continue;
            c.HeardAt = n.Time;
            c.LastSeen = n.Position;
            c.Awareness = MathF.Max(c.Awareness, 0.6f);
        }
        float strength = t == null ? 0f : Sensed(w, c, t, d, sight, hearing, eye, facing, cone, inside, scent);
        c.Sensing = strength > 0f;
        if (c.Sensing)
        {
            c.LastSeen = t.Position;
            float near = sight > 0f ? Math.Clamp(1f - d / MathF.Max(sight, 0.1f), 0f, 1f) : 0.5f;
            c.Awareness = MathF.Min(1f, c.Awareness + dt / notice * strength * (0.6f + 1.4f * near));
            if (c.Awareness >= 1f || c.Noticed == t)
            {
                c.Noticed = t;
                c.NoticedAt = w.Time;
            }
        }
        else c.Awareness = MathF.Max(0f, c.Awareness - dt / (notice * 2.5f));

        // Shot by someone: it knows where they are.
        if (c.ProvokedBy != null && w.Time - c.ProvokedAt < 1f && c.ProvokedBy.Attackable)
        {
            c.Noticed = c.ProvokedBy;
            c.NoticedAt = w.Time;
            c.LastSeen = c.ProvokedBy.Position;
            c.Awareness = 1f;
        }
        if (c.Noticed != null)
        {
            bool gone = w.Time - c.NoticedAt > Memory || (!c.Sensing && (c.LastSeen - c.Position).Length() < c.Radius + 1.5f);
            if (gone || (inside && !c.Noticed.Attackable))
            {
                c.Noticed = null;
                c.Awareness = MathF.Min(c.Awareness, 0.5f);
            }
        }
        c.Alert = c.Noticed != null ? 1f : c.Awareness >= Curious ? 0.5f : 0f;
        // Hunting by its voice, it clicks; that's how you know.
        if (c.Species.Senses.Echo && c.Alert > 0f && w.Time >= c.NextClick)
        {
            c.NextClick = w.Time + (c.Noticed != null ? 0.7f : 1.6f);
            w.RaiseClick(c);
        }
        c.LookAt = c.Alert > 0f ? c.LastSeen + Vector3.Up * 1.2f : null;
    }

    /// <summary>How well a sight range holds up in this light: with good eyes for the dark it hardly matters.</summary>
    public static float Seeing(SenseProfile p, float light)
    {
        float day = Mathf.SmoothStep(0.01f, 0.28f, light);
        return MathF.Max(0.05f, Mathf.Lerp(day, 1f, p.DarkVision));
    }

    /// <summary>How far downwind it can smell this target, m: the wind carries it a long way one way and hardly at all the other.</summary>
    public static float ScentRange(WildSim w, Creature c, ITarget t, float reach)
    {
        if (reach <= 0f) return 0f;
        Vector3 away = WildSim.Flat(c.Position - t.Position);
        float wind = w.Wind.Length();
        if (wind < 0.2f || away.LengthSquared() < 0.01f) return reach;
        float along = away.Normalized().Dot(w.Wind / wind);
        return reach * Math.Clamp(1f + Math.Clamp(wind / 6f, 0f, 1f) * (along > 0f ? along * 1.2f : along * 0.8f), 0.15f, 2.2f);
    }

    /// <summary>How plainly it senses the target now: 1 seen, 0.8 clicked out, 0.6 heard, 0.4 smelled, 0 not at all.</summary>
    static float Sensed(WildSim w, Creature c, ITarget t, float d, float sight, float hearing, Vector3 eye, Vector3 facing, float cone, bool inside, float scent)
    {
        if (t.Health != null && t.Health.Downed) return 0f;
        if (inside && !t.Attackable) return 0f;
        SenseProfile p = c.Species.Senses;
        float noise = w.NoiseOf(t);
        float ear = hearing * noise;
        float echo = p.Echo ? hearing * 0.7f : 0f;
        float nose = ScentRange(w, c, t, scent);
        if (d > sight && d > ear && d > echo && d > nose) return 0f; // nothing to work out: what keeps an unaware creature cheap
        Vector3 head = t.Position + Vector3.Up * (t.Crouched ? CrouchedHead : 1.4f); // crouched, there is less of you to see
        bool clear = false, looked = false;
        if (d <= sight)
        {
            Vector3 to = head - eye;
            float len = to.Length();
            if ((len < 0.01f || facing.Dot(to / len) >= cone) && d <= sight * Seeing(p, w.LightOn(t)))
            {
                clear = LineClear(w, c, t, eye, head);
                looked = true;
                if (clear) return 1f;
            }
        }
        if (d <= echo)
        {
            if (!looked) { clear = LineClear(w, c, t, eye, head); looked = true; }
            if (clear) return 0.8f;
        }
        if (d <= ear)
        {
            if (d <= ear * ThroughWall) return 0.6f;
            if (!looked) { clear = LineClear(w, c, t, eye, head); looked = true; }
            if (clear) return 0.6f;
        }
        return d <= nose ? 0.4f : 0f;
    }

    /// <summary>Whether a line reaches the target, through what it can tell of the world: the ground or a place's solid cells, then the world's own collision where the game has it.</summary>
    public static bool LineClear(WildSim w, Creature c, ITarget t, Vector3 eye, Vector3 head)
    {
        if (c.Space != null ? !SpaceMath.Clear(c.Space, eye, head, 0.05f) : !OverGround(w, eye, head)) return false;
        // A vessel's own hull isn't cover from what is looking at the vessel.
        if (t.ThreatScale > 1.01f) return true;
        return w.SightLine != null && !c.NoRays ? w.SightLine(eye, head) : c.Space != null || w.Nav.LineClear(eye, head);
    }

    static bool OverGround(WildSim w, Vector3 a, Vector3 b)
    {
        float len = (b - a).Length();
        int steps = Math.Clamp((int)(len / 4f), 1, 24);
        for (int i = 1; i < steps; i++)
        {
            Vector3 p = a.Lerp(b, i / (float)steps);
            if (w.Terrain.Height(p.X, p.Z) > p.Y + 0.2f) return false;
        }
        return true;
    }

    /// <summary>Where it thinks a target is: where it is while sensed, else where it was last known to be.</summary>
    public static Vector3 Aim(Creature c, ITarget t) => c.Sensing || c.Noticed != t ? t.Position : c.LastSeen;
}
