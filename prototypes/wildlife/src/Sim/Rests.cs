using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Rest sites on the open surface, chosen where each kind would really bed down:
/// - dens on a slope or against a rock, out of the open;
/// - burrows in flat, soft ground near cover, never under a rock;
/// - roosts on high ground, ideally by a rock to perch on;
/// - bedding grounds (and hives) on flat ground at the edge of a grove.
/// Never in rock or a tree, a base's zone or (where the nav grid marks it) the sea. A herd or a
/// pack shares one site.
/// </summary>
public static class Rests
{
    public static void Assign(WildSim w, List<Creature> group)
    {
        if (group.Count == 0) return;
        Species s = group[0].Species;
        if (s.Activity == Activity.Cathemeral || s.RestKind == RestKind.None || s.InSpace || s.IsDeep) return;
        Vector3 home = Vector3.Zero;
        foreach (Creature c in group) home += c.Home;
        home /= group.Count;
        RestSite site;
        // Walking plants root where they stand; ambushers hide where they wait.
        if (s.Rooted || s.Behaviour == Behaviour.Ambusher) site = new RestSite { Kind = s.RestKind, Position = new Vector3(home.X, w.Terrain.Height(home.X, home.Z), home.Z) };
        else site = Find(w, s, home, Rng.For(s.Seed, "rest", group[0].Id));
        site.Radius = group.Count > 1 ? MathF.Max(1.5f, WildSim.HerdSpread(s, group.Count) * 0.55f) : MathF.Max(0.5f, s.Stats.Radius);
        site.Users = group.Count;
        w.RestSites.Add(site);
        foreach (Creature c in group) c.Rest = site;
    }

    static RestSite Find(WildSim w, Species s, Vector3 home, Rng rng)
    {
        ActivityRules r = w.Data.Activity;
        float homeY = w.Terrain.Height(home.X, home.Z);
        float best = float.MinValue;
        Vector3 bestP = home;
        for (int i = 0; i < 32; i++)
        {
            float a = rng.Range(0f, MathF.Tau), d = rng.Range(r.RestRadius[0], r.RestRadius[1]);
            Vector3 p = w.Terrain.Clamp(home + new Vector3(MathF.Cos(a) * d, 0f, MathF.Sin(a) * d), 12f);
            // Never in rock, a tree, a base's zone or (where the nav grid marks it) the sea.
            if (w.Nav.Blocked(p) || w.InBaseZone(p)) continue;
            float h = w.Terrain.Height(p.X, p.Z);
            float score = Score(w, s.RestKind, p, h, homeY) + rng.Range(0f, 0.15f);
            if (score > best)
            {
                best = score;
                bestP = p;
            }
        }
        return new RestSite { Kind = s.RestKind, Position = new Vector3(bestP.X, w.Terrain.Height(bestP.X, bestP.Z), bestP.Z) };
    }

    /// <summary>How well a spot suits a kind of rest site (higher is better).</summary>
    public static float Score(WildSim w, RestKind kind, Vector3 p, float h, float homeY)
    {
        Vector3 n = w.Terrain.Normal(p.X, p.Z);
        float slope = 1f - n.Y; // 0 on the flat
        float rock = 99f;
        if (w.Nav.Near(p) is { } near)
            foreach (Obstacle o in near)
                if (o.Active) rock = MathF.Min(rock, WildSim.Flat(o.Position - p).Length() - o.Radius);
        float shelter = rock < 8f ? 1f - MathF.Max(0f, rock) / 8f : 0f;
        float grove = 99f;
        foreach (Vector3 g in w.Groves) grove = MathF.Min(grove, WildSim.Flat(g - p).Length());
        float cover = 1f - Math.Clamp((grove - 8f) / 40f, 0f, 1f);
        return kind switch
        {
            RestKind.Den => slope * 8f + shelter * 3f,
            RestKind.Burrow => 1f - slope * 10f + cover * 1.5f - shelter,
            RestKind.Roost => (h - homeY) * 0.3f + shelter * 1.5f,
            _ => 1f - slope * 8f + cover * 2f,
        };
    }

    /// <summary>Where in its site a creature lies: its own spot round the middle, so a herd beds down apart.</summary>
    public static Vector3 Spot(Creature c)
    {
        RestSite site = c.Rest;
        if (site.Users <= 1) return site.Position;
        int slot = c.HerdSlot;
        float a = slot * 2.39996f, r = site.Radius * MathF.Sqrt((slot + 0.5f) / site.Users);
        return site.Position + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
    }
}

public static partial class Brain
{
    /// <summary>
    /// Outside its hours a creature goes to its rest site and rests there: lying down in a den
    /// or on a bedding ground, dug half in in a burrow, landed and folded on a roost. It wakes
    /// for a while if a player comes too close (crouching lets you creep nearer), if it is
    /// hurt, or if its herd takes fright; a pack mid-hunt or a herd in flight doesn't go to bed.
    /// Returns true while it is resting or on its way to rest.
    /// </summary>
    static bool Resting(WildSim w, Creature c, ITarget near, float dist, float dt)
    {
        Species s = c.Species;
        // Ambushers hide through the day and walking plants root: that is their rest already.
        if (s.Behaviour == Behaviour.Ambusher || s.Rooted || s.Activity == Activity.Cathemeral) return false;
        ActivityRules r = w.Data.Activity;
        int key = c.Herd != null && c.Herd.Members.Count > 0 ? c.Herd.Members[0].Id : c.Pack?.Leader?.Id ?? c.Id;
        bool active = ActivityCycle.Active(r, s.Activity, w.Clock.Time01, ActivityCycle.Offset(r, key));
        if (active || w.Time < c.AwakeUntil)
        {
            if (c.State == CState.Rest) c.SetState(CState.Wander);
            return false;
        }
        if (c.ProvokedBy != null && w.Time - c.ProvokedAt < 12f)
        {
            Wake(w, c);
            return false;
        }
        if ((c.Herd != null && c.Herd.Alarmed) || (c.Pack != null && c.Pack.Phase != PackPhase.Roam)) return false;
        if (near != null && dist < r.WakeRadius * near.ThreatScale)
        {
            Wake(w, c);
            return false;
        }

        Vector3 spot = Rests.Spot(c);
        float d = WildSim.Flat(spot - c.Position).Length();
        bool flier = s.Rig == RigKind.Winged;
        if (c.State != CState.Rest && d > (flier ? 2.5f : 1.2f))
        {
            c.SetState(CState.ReturnHome);
            Steer(w, c, spot, d > 20f ? s.Stats.WalkSpeed * 1.3f : s.Stats.WalkSpeed);
            if (flier) c.Altitude = d < 8f ? 0f : 6f;
            else if (s.Rig == RigKind.GasBag) c.Altitude = d < 8f ? 1.4f : 6f;
            else if (s.Rig == RigKind.Swarm && s.Flies) c.Altitude = 1.8f;
            return true;
        }
        c.SetState(CState.Rest);
        c.Desired = Vector3.Zero;
        c.Alert = 0f;
        c.LookAt = null;
        c.WantCrouch = 1f;
        c.WantHidden = c.Rest.Kind switch
        {
            RestKind.Burrow => s.Burrows ? 1f : 0.6f,
            RestKind.Den => 0.25f,
            _ => 0f,
        };
        if (flier) c.Altitude = 0f;
        else if (s.Rig == RigKind.GasBag) c.Altitude = 1.4f;
        else if (s.Rig == RigKind.Swarm && s.Flies) c.Altitude = 0.5f;
        return true;
    }

    /// <summary>Woken: up for a while, and the whole herd or pack with it.</summary>
    static void Wake(WildSim w, Creature c)
    {
        float until = w.Time + w.Data.Activity.WakeSeconds;
        c.AwakeUntil = until;
        if (c.State == CState.Rest) c.SetState(CState.Wander);
        IEnumerable<Creature> group = c.Herd?.Members ?? (IEnumerable<Creature>)c.Pack?.Members;
        if (group == null) return;
        foreach (Creature m in group)
        {
            m.AwakeUntil = until;
            if (m.State == CState.Rest) m.SetState(CState.Wander);
        }
    }
}
