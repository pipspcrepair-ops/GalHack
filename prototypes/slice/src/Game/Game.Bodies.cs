using System;
using System.Collections.Generic;
using GalHack.Items;
using Godot;
using Slice.Items;
using Slice.Places;
using Wildlife;
using Layers = GalHack.Physics.Layers;

namespace Slice.Game;

/// <summary>
/// How bodies end. One blown apart comes down as pieces of itself (a machine with sparks and smoke
/// instead of blood); one burned away leaves ash on a scorch. Whatever it carried is never lost: it
/// is left where the body was, to be searched there.
/// </summary>
public partial class Game
{
    /// <summary>The pieces and the ash, in the zone.</summary>
    public Gibs Gibs { get; private set; }

    /// <summary>What a weapon's kill does to a body.</summary>
    public static BodyEnd EndOf(GalHack.Arms.KillEffect k) => k switch
    {
        GalHack.Arms.KillEffect.Gib => BodyEnd.Gib,
        GalHack.Arms.KillEffect.Vaporise => BodyEnd.Vaporise,
        _ => BodyEnd.Fall,
    };

    void SetUpBodies()
    {
        Gibs = new Gibs { Name = "Gibs", Layer = Layers.Props, Mask = Layers.World | Layers.Hull | Layers.Interior | Layers.Props };
        Zone.AddChild(Gibs);
    }

    /// <summary>A town person's body as it lies (it ragdolls), as balls in the world.</summary>
    static IEnumerable<Vector4> PersonBalls(Town t, Town.Person p)
    {
        if (p.Rig == null || p.Gone) yield break;
        p.Rig.UpdateHits();
        for (int i = 0; i < p.Rig.HitCount; i++)
        {
            Vector4 h = p.Rig.Hits[i];
            Vector3 w = t.World(new Vector3(h.X, h.Y, h.Z));
            yield return new Vector4(w.X, w.Y, w.Z, h.W);
        }
    }

    /// <summary>What a body that is no more had, left on the floor where it was.</summary>
    void Remains(Species s, Vector3 at, Bill drops)
    {
        int[] size = Zone.Data.Rules.Containers.Corpse;
        Lootable left = Zone.Loot.Create(LootKind.Corpse, $"Remains of {s.CommonName}", at, size[0], size[1], drops);
        left.Radius = 0.6f;
    }

    /// <summary>
    /// A body comes apart or burns away, from its pieces where they are (world, from a rig's own space
    /// moved by <paramref name="shift"/>). Returns the floor where its remains lie.
    /// </summary>
    Vector3 EndBody(Species s, Rig rig, Vector3 shift, BodyEnd end, Vector3 hitDir)
    {
        Vector3 centre = rig.BoundsCenter + shift;
        Vector3 floor = Zone.Loot.Settle != null ? Zone.Loot.Settle(centre) : centre;
        if (end == BodyEnd.Gib)
        {
            var pose = new Transform3D[rig.Pose.Length];
            for (int i = 0; i < pose.Length; i++) pose[i] = rig.Pose[i].Translated(shift);
            Vector3 from = centre - (hitDir.LengthSquared() > 1e-4f ? hitDir.Normalized() : Vector3.Down) * 1.2f + Vector3.Down * 0.4f;
            Gibs.Burst(s, rig.Layout, pose, from, 7f);
            if (s.Mechanical)
            {
                // A machine: no blood, sparks and smoke.
                for (int k = 0; k < 4; k++) Zone.Dust?.Sparks(centre + new Vector3((k - 1.5f) * 0.15f, 0.1f * k, 0f), Vector3.Up, new Color(1f, 0.7f, 0.3f));
                Zone.Dust?.Puff(centre, new Color(0.18f, 0.18f, 0.19f, 0.9f), 2.2f);
            }
            else
            {
                Zone.Blood.Pop(centre, s.Blood, 60);
                Zone.Blood.Hit(centre, hitDir, s.Blood, s.Figure != null ? 1.2f : MathF.Max(0.6f, s.Body.Size));
            }
        }
        else
        {
            Gibs.Ash(floor, Vector3.Up, s.Figure != null ? 0.55f : Math.Clamp(s.Body.Size * 0.6f, 0.3f, 3f), s.Seed);
            Zone.Dust?.Puff(centre, new Color(0.3f, 0.29f, 0.28f, 0.85f), 1.8f);
        }
        return floor;
    }

    /// <summary>A creature blown apart or burned away: its pieces or its ash, and the floor its drops are left on.</summary>
    bool EndCreature(Creature c, out Vector3 at)
    {
        at = default;
        if (c.End == BodyEnd.Fall || c.Rig == null || c.Species.Rig == RigKind.Swarm) return false;
        Zone.Wildlife.SimOf(c, out Vector3 origin, out _);
        at = EndBody(c.Species, c.Rig, origin, c.End, c.HitDir);
        c.Removed = true;
        return true;
    }

    /// <summary>The same for one of a town's people.</summary>
    bool EndPerson(Town t, Town.Person p, out Vector3 at)
    {
        at = default;
        if (p.End == BodyEnd.Fall || p.Rig == null) return false;
        p.Rig.UpdateHits();
        at = EndBody(p.Looks, p.Rig, t.World(Vector3.Zero), p.End, p.Rig.HitDir);
        t.Unbody(p);
        return true;
    }
}
