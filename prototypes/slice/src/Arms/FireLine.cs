using Godot;
using Slice.Places;
using Slice.Surface;
using Wildlife;

namespace Slice.Arms;

/// <summary>
/// The line of fire (owner: "they should try not to shoot each other, they need to treat other NPCs as obstacles when
/// shooting a target"). Before an NPC's round leaves (a town's fighter's, a band's, a machine's mount), the line from
/// its muzzle to what it shoots at is looked along for bodies as well as the world: whoever or whatever stands within a
/// hand's breadth of it, the shooter and its target left out (its friends, its own band, a bystander, an animal that
/// isn't its quarry, you when it isn't shooting at you). A body there and the round isn't fired: the shooter steps for
/// a clear line instead. The one look every NPC shot goes through.
/// </summary>
public static class FireLine
{
    /// <summary>How far round a body a line must pass to be clear of it, m.</summary>
    const float Margin = 0.2f;

    /// <summary>Rounds held because someone stood in the line (tests); and whether the line is looked along at all (tests: off, how it was).</summary>
    public static int Held;
    public static bool On = true;

    /// <summary>
    /// Whether a body other than the shooter and its target stands in the line from a muzzle to an aim point (zone frame):
    /// every sim's creatures, every standing town's people, and you (unless you are what it shoots at).
    /// </summary>
    public static bool Masked(WildlifePatch patch, PoiPlaces places, Vector3 from, Vector3 to, object shooter, object target, bool atYou)
    {
        if (!On || patch == null) return false;
        if (patch.Sim != null && Crosses(patch.Sim, patch.Origin, from, to, shooter, target)) return true;
        foreach (DeepSite s in patch.Sites)
            if (Crosses(s.Sim, s.GlobalPosition, from, to, shooter, target)) return true;
        if (places != null)
            foreach (Place p in places.Standing)
                if (p.Memory.Town is { } town)
                    foreach (Town.Person q in town.People)
                        if (q != shooter && q != target && !q.Dead && !q.Gone && !q.Indoors && !q.Away && Across(from, to, town.World(q.Pos), Town.RadiusOf(q), Town.HeightOf(q))) return true;
        return !atYou && patch.Player is { Attackable: true } && Across(from, to, patch.PlayerWorld(), 0.35f, 1.8f);
    }

    static bool Crosses(WildSim sim, Vector3 origin, Vector3 from, Vector3 to, object shooter, object target)
    {
        foreach (Creature c in sim.Creatures)
            if (c != shooter && c != target && !c.Dead && !c.Removed && !c.InWalls && Across(from, to, c.Position + origin, c.Radius, c.Species.BodyHeight)) return true;
        return false;
    }

    /// <summary>Whether a line passes within the margin of a body standing at its feet, this wide and tall (short of the line's end: what stands behind the target is no matter).</summary>
    static bool Across(Vector3 from, Vector3 to, Vector3 feet, float radius, float height)
    {
        Vector3 d = to - from;
        float flat = d.X * d.X + d.Z * d.Z;
        if (flat < 1e-6f) return false;
        // The nearest the line comes to the body's upright middle, seen from above; the line's height there.
        float t = ((feet.X - from.X) * d.X + (feet.Z - from.Z) * d.Z) / flat;
        if (t <= 0.02f || t >= 0.98f) return false;
        Vector3 at = from + d * t;
        float side = new Vector2(at.X - feet.X, at.Z - feet.Z).Length();
        return side < radius + Margin && at.Y > feet.Y - Margin && at.Y < feet.Y + height + Margin;
    }
}
