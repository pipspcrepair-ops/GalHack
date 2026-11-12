using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Nothing alive is ever inside a wall, a floor or a stair. A creature's middle is checked against
/// whatever is solid where it lives: the cells of its cave or place, or, out on the surface, the
/// places standing there (<see cref="WildSim.SolidAt"/>). A spawn that lands inside is moved to the
/// nearest open floor or not made at all; the sim scans everything now and then and frees anything
/// that has ended up inside all the same (that count, <see cref="WildSim.Freed"/>, should stay at
/// zero); and the tests scan every spawned creature with <see cref="Scan"/>.
/// </summary>
public static class Interpenetration
{
    /// <summary>How deep a body's middle may sit in solid before it counts as inside, m (the corners are filleted).</summary>
    public const float Tolerance = 0.08f;

    /// <summary>The middle of a creature's body: what must be in the open.</summary>
    public static Vector3 Middle(Creature c) => c.Space != null ? Crawl.Center(c) : c.Position + Vector3.Up * Lift(c.Species);

    static float Lift(Species s) => Math.Clamp(s.BodyHeight * 0.5f, 0.15f, 0.6f);

    /// <summary>Whether a creature's body is inside something solid now.</summary>
    public static bool Inside(WildSim w, Creature c)
    {
        if (c.Removed || c.InWalls || c.Species.Rig == RigKind.Swarm) return false;
        if (c.Space != null) return !c.Falling && InSpace(c);
        if (w.SolidAt == null || c.Species.Flies && !c.Landed || c.Thrown) return false;
        return w.SolidAt(Middle(c));
    }

    /// <summary>
    /// In a cave or a place: a climber's middle in its rock or walls or on the wrong side of a part's face;
    /// a walker's middle in the walls, or its body in a piece of furniture (<see cref="PartSet"/>).
    /// </summary>
    static bool InSpace(Creature c)
    {
        Vector3 mid = Crawl.Center(c);
        if (c.Species.Climbs || c.Space.Parts is not { } parts) return c.Space.Density(mid) > Tolerance;
        return c.Space.Bare.Density(mid) > Tolerance || parts.Gap(c.Position, SpaceNav.PartRadius(c.Species), SpaceNav.PartHeight(c.Species)) < -Tolerance;
    }

    /// <summary>Whether a body of this species standing on the surface at a point would be inside a place's blocks.</summary>
    public static bool SolidSurface(WildSim w, Vector3 at, Species s) =>
        w.SolidAt != null && w.SolidAt(new Vector3(at.X, w.Terrain.Height(at.X, at.Z) + Lift(s), at.Z));

    /// <summary>Every living creature of a sim that is inside something solid.</summary>
    public static List<Creature> Scan(WildSim w)
    {
        var inside = new List<Creature>();
        foreach (Creature c in w.Creatures)
            if (!c.Dead && Inside(w, c)) inside.Add(c);
        return inside;
    }

    /// <summary>Puts a creature on the nearest open ground round a point (its own position if none given). False if there is none within a few metres.</summary>
    public static bool Free(WildSim w, Creature c, Vector3? near = null)
    {
        Vector3 from = near ?? (c.Space != null ? c.LastFirm : c.Position);
        bool freed = c.Space != null ? FreeInSpace(w, c, from) : FreeOnSurface(w, c, from);
        if (!freed) return false;
        c.Velocity = Vector3.Zero;
        c.Path.Clear();
        c.PathIndex = 0;
        c.SpacePath.Clear();
        c.SpacePathIndex = 0;
        c.Rig?.Reset(w, c);
        return true;
    }

    static bool FreeOnSurface(WildSim w, Creature c, Vector3 from)
    {
        float lift = Lift(c.Species);
        for (float r = 1f; r <= 14f; r += 1f)
            for (int k = 0; k < 12; k++)
            {
                float a = k * MathF.Tau / 12f + r * 0.37f;
                var q = new Vector3(from.X + MathF.Cos(a) * r, 0f, from.Z + MathF.Sin(a) * r);
                if (!w.Terrain.Inside(q, 6f)) continue;
                q.Y = w.Terrain.Height(q.X, q.Z);
                if (w.SolidAt(q + Vector3.Up * lift)) continue;
                c.Position = q;
                return true;
            }
        return false;
    }

    /// <summary>The nearest open cell with a floor under it and headroom over it, shell by shell out to four metres.</summary>
    static bool FreeInSpace(WildSim w, Creature c, Vector3 from)
    {
        ISolidSpace s = c.Space;
        Vector3I o = SpaceMath.Cell(from);
        int headroom = Math.Max(1, (int)MathF.Ceiling(c.Species.BodyHeight * 0.8f));
        for (int r = 0; r <= 4; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) != r) continue;
                int x = o.X + dx, y = o.Y + dy, z = o.Z + dz;
                if (!s.SolidCell(x, y - 1, z)) continue;
                bool open = true;
                for (int h = 0; h < headroom && open; h++) open = !s.SolidCell(x, y + h, z);
                if (!open) continue;
                Crawl.Place(w, c, new Vector3(x + 0.5f, y + 0.05f, z + 0.5f), null);
                if (!InSpace(c)) return true;
            }
        return false;
    }
}
