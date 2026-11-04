using Godot;

namespace Boarding;

/// <summary>
/// A hostile bolt or shell with real travel time: it moves every physics tick and sweeps a
/// ray over the distance it covered, so it can be seen coming and (at range) dodged.
/// Warning shots are harmless to the player by construction: their sweep ignores the player,
/// so a guaranteed miss stays a miss even if you step into it. A bolt that passes close
/// makes you flinch, so you know you were shot at.
/// </summary>
public partial class Projectile : Node3D
{
    public Main Game;
    public Vector3 Velocity;
    public float Damage;
    public bool Warning;
    public float Tracking;
    public Node3D Homing;
    public Rid Shooter;
    public Color Color = Palette.VharnGlow;
    public float Thickness = 0.06f;
    public float Life = 1.5f;

    /// <summary>For tests: where it ended up and whether it hit the player.</summary>
    public bool HitPlayer { get; private set; }
    public float ClosestToPlayer { get; private set; } = float.MaxValue;

    MeshInstance3D _bolt;
    bool _nearMissReported;

    public override void _Ready()
    {
        float length = Mathf.Clamp(Velocity.Length() * 0.004f, 0.4f, 2.2f);
        _bolt = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Thickness, Thickness, length) },
            MaterialOverride = Art.Additive(Color),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = Vector3.Back * length * 0.5f,   // the tail trails behind the head
        };
        AddChild(_bolt);
        Face();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Life -= dt;
        if (Life <= 0f)
        {
            QueueFree();
            return;
        }

        if (Tracking > 0f && Homing != null && IsInstanceValid(Homing))
        {
            // Rockets turn toward their target slowly.
            Vector3 want = (Homing.GlobalPosition + Vector3.Up - GlobalPosition).Normalized() * Velocity.Length();
            float angle = Velocity.AngleTo(want);
            if (angle > 1e-4f) Velocity = Velocity.Slerp(want, Mathf.Min(1f, Tracking * dt / angle));
        }

        Vector3 from = GlobalPosition;
        Vector3 to = from + Velocity * dt;
        uint mask = Layers.World | Layers.Ship | (Warning ? 0u : Layers.Player);
        var query = PhysicsRayQueryParameters3D.Create(from, to, mask, new Godot.Collections.Array<Rid> { Shooter });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        TrackNearMiss(from, hit.Count > 0 ? (Vector3)hit["position"] : to);

        if (hit.Count == 0)
        {
            GlobalPosition = to;
            Face();
            return;
        }

        var point = (Vector3)hit["position"];
        var normal = (Vector3)hit["normal"];
        Node collider = hit["collider"].As<Node>();
        switch (collider)
        {
            case Player player:
                HitPlayer = true;
                player.TakeHit(Damage, from);
                Game.Effects.Impact(point, normal, Surface.Ichor, null, 0.6f);
                break;
            case Block block:
                Game.Effects.Impact(point, normal, block.Surface, block, 0.9f);
                block.Damage(Damage * 0.5f, point, normal);
                break;
            case Node3D other:
                var surface = other.HasMeta("surface") ? (Surface)(int)other.GetMeta("surface") : Surface.Rock;
                Game.Effects.Impact(point, normal, surface, other, Mathf.Clamp(Damage / 30f, 0.8f, 2.5f));
                break;
        }
        Game.Effects.Flash(point + normal * 0.1f, Color, 0.5f);
        QueueFree();
    }

    void TrackNearMiss(Vector3 a, Vector3 b)
    {
        Player player = Game.Player;
        if (player == null) return;
        Vector3 chest = player.GlobalPosition + Vector3.Up * 1.1f;
        Vector3 ab = b - a;
        float t = ab.LengthSquared() > 1e-6f ? Mathf.Clamp((chest - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
        float d = chest.DistanceTo(a + ab * t);
        ClosestToPlayer = Mathf.Min(ClosestToPlayer, d);
        if (!_nearMissReported && d < 3f && !HitPlayer)
        {
            _nearMissReported = true;
            player.NearMiss(a + ab * t);
        }
    }

    void Face()
    {
        if (Velocity.LengthSquared() > 1e-4f) GlobalBasis = Effects.Facing(Vector3.Zero, Velocity).Basis;
    }
}
