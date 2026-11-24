using System;
using Godot;
using Slice.Places;
using Slice.Surface;
using Wildlife;

namespace Slice.Arms;

/// <summary>
/// The line of fire (owner: "they should try not to shoot each other, they need to treat other NPCs as obstacles when
/// shooting a target"). Before an NPC's round leaves (a town's fighter's, a band's, a machine's mount), the line from
/// its muzzle to what it shoots at is looked along for bodies as well as the world: an ally or a bystander within a
/// hand's breadth of it (its friends, its own band, an animal that isn't its quarry, you when it isn't shooting at you).
/// A body there and the round isn't fired: the shooter steps for a clear line instead. Its target's own side (the rest
/// of the band, the town it fights) it fires through: a round into one of them is no stray. The one look every NPC shot
/// goes through.
/// </summary>
public static class FireLine
{
    /// <summary>How far round a body a line must pass to be clear of it, m, and more for every metre out from the muzzle (a round strays a little from its line: a gun's spread, unsteady hands).</summary>
    const float Margin = 0.2f, Spread = 0.04f;

    /// <summary>Rounds held because someone stood in the line (tests); and whether the line is looked along at all (tests: off, how it was).</summary>
    public static int Held;
    public static bool On = true;

    /// <summary>
    /// Whether an ally or a bystander stands in the line from a muzzle to an aim point (zone frame): every sim's creatures,
    /// every standing town's people, and you (unless you are what it shoots at); never the shooter, nor its target's side.
    /// </summary>
    public static bool Masked(WildlifePatch patch, PoiPlaces places, Vector3 from, Vector3 to, object shooter, object target, bool atYou)
    {
        if (!On || patch == null) return false;
        object foe = Side(target);
        if (patch.Sim != null && Crosses(patch.Sim, patch.Origin, from, to, shooter, foe)) return true;
        foreach (DeepSite s in patch.Sites)
            if (Crosses(s.Sim, s.GlobalPosition, from, to, shooter, foe)) return true;
        if (places != null)
            foreach (Town town in places.Towns)
                if (town != foe)
                    foreach (Town.Person q in town.People)
                        if (q != shooter && !q.Dead && !q.Gone && Across(from, to, town.World(q.Pos), Town.RadiusOf(q), Town.HeightOf(q))) return true;
        return !atYou && patch.Player is { Attackable: true } && Across(from, to, patch.PlayerWorld(), 0.35f, 1.8f);
    }

    /// <summary>Whose side a body is on, as a shot sees it: a creature's band, pack or itself; a town's person's town; you.</summary>
    public static object Side(object o) => o switch { Creature c => (object)c.Herd ?? (object)c.Pack ?? c, Town.Person p => p.Town, _ => o };

    /// <summary>Whether a hit is on the side a shot was meant for: its target, or one of the target's own.</summary>
    public static bool SameSide(object meant, object hit) => meant != null && Side(meant) == Side(hit);

    static bool Crosses(WildSim sim, Vector3 origin, Vector3 from, Vector3 to, object shooter, object foe)
    {
        foreach (Creature c in sim.Creatures)
            if (c != shooter && !c.Dead && !c.Removed && !c.InWalls && Side(c) != foe && Across(from, to, c.Position + origin, c.Radius, c.Species.BodyHeight)) return true;
        return false;
    }

    /// <summary>
    /// Whether a line passes within the margin of a body standing at its feet, this wide and tall, short of the line's end (what
    /// stands behind the target is no matter). One beside or just behind the muzzle counts where the line starts in its body.
    /// </summary>
    static bool Across(Vector3 from, Vector3 to, Vector3 feet, float radius, float height)
    {
        Vector3 d = to - from;
        float flat = d.X * d.X + d.Z * d.Z;
        if (flat < 1e-6f) return false;
        // The nearest the line comes to the body's upright middle, seen from above (from the muzzle on); the line's height there.
        float t = ((feet.X - from.X) * d.X + (feet.Z - from.Z) * d.Z) / flat;
        if (t >= 1f) return false;
        t = MathF.Max(t, 0f);
        Vector3 at = from + d * t;
        float side = new Vector2(at.X - feet.X, at.Z - feet.Z).Length();
        float margin = t > 0f ? Margin + Spread * t * d.Length() : 0f;
        return side < radius + margin && at.Y > feet.Y - margin && at.Y < feet.Y + height + margin;
    }
}
