using Godot;

namespace GalHack.Physics.Engine;

/// <summary>
/// Stepping up onto anything up to a step tall (<see cref="PhysicsRules.WalkingData.StepHeight"/>),
/// for any character controller, whichever way up it stands: the shared module's walkers on
/// terrain, cabin floors and outer hulls, and any game's own walker. Blocked by a wall while
/// walking, the move is tried again lifted by the step height (or what headroom there is), on by
/// this tick's walk, and back down; if all three are clear and it comes down on a floor, the
/// character is up the step, still walking at the same speed. Anything taller stays a wall.
/// </summary>
public static class Steps
{
    /// <summary>
    /// Call right after <see cref="CharacterBody3D.MoveAndSlide"/>, with the velocity the move
    /// wanted. If it was stopped by something no taller than <paramref name="height"/>, moves the
    /// character up onto it, sets its velocity to the walk along it and returns true.
    /// </summary>
    /// <param name="up">the character's up</param>
    /// <param name="radius">its capsule's radius: it always moves on at least a share of it</param>
    public static bool Up(CharacterBody3D character, Vector3 up, Vector3 wanted, float height, float radius, float dt)
    {
        Vector3 along = wanted - up * wanted.Dot(up);
        float speed = along.Length();
        if (speed < 0.3f || height <= 0f) return false;
        Vector3 dir = along / speed;
        bool blocked = false;
        for (int i = 0; i < character.GetSlideCollisionCount() && !blocked; i++)
        {
            Vector3 n = character.GetSlideCollision(i).GetNormal();
            blocked = n.Dot(up) < 0.3f && dir.Dot(-n) > 0.3f;
        }
        if (!blocked) return false;

        Transform3D from = character.GlobalTransform;
        var hit = new KinematicCollision3D();
        Vector3 lift = up * height;
        if (character.TestMove(from, lift, hit)) lift = hit.GetTravel();
        Transform3D raised = from;
        raised.Origin += lift;
        Vector3 on = dir * Mathf.Max(speed * dt, radius * 0.4f);
        if (character.TestMove(raised, on)) return false; // taller than a step: a wall
        raised.Origin += on;
        if (!character.TestMove(raised, -lift - up * 0.05f, hit)) return false; // nothing to stand on
        if (hit.GetNormal().Dot(up) < Mathf.Cos(character.FloorMaxAngle)) return false;
        Vector3 landed = raised.Origin + hit.GetTravel();
        if ((landed - from.Origin).Dot(up) < 0.02f) return false; // nothing to climb
        character.GlobalPosition = landed;
        character.Velocity = along; // on at the walking speed, nothing into the step
        return true;
    }
}
