using System;
using Godot;
using GalHack.Physics;

namespace Slice.Tests;

/// <summary>
/// A bulkhead with a low passage through it, 1.2 m high, on a level concrete pad: standing is
/// 1.8 m, so it takes a crouch. The player walks into it toward -Z. Built in the zone's world as
/// real collision (the walls, the lintel over the passage), visible for the screenshots.
/// </summary>
public sealed class CrouchRig
{
    /// <summary>The headroom in the passage, m: more than a crouched body (1.1 m), less than a standing one (1.8 m).</summary>
    public const float Gap = 1.2f;

    /// <summary>The passage's width, m, and its length along the walk.</summary>
    public const float Width = 2.4f, Length = 4f;

    /// <summary>The pad's top, m, and where the player starts (on the pad, 4 m from the passage's mouth).</summary>
    public readonly float Top;
    public readonly Vector3 Start;

    /// <summary>Z of the passage's near mouth and its far end (walking toward -Z).</summary>
    public readonly float Near, Far;

    CrouchRig(float top, Vector3 start, float near, float far)
    {
        Top = top;
        Start = start;
        Near = near;
        Far = far;
    }

    /// <summary>Builds the rig on level ground near <paramref name="ground"/>, a pad over the highest ground under it.</summary>
    public static CrouchRig Build(Game.Game game, Vector3 ground)
    {
        float top = float.MinValue;
        for (float dx = -6f; dx <= 6f; dx += 2f)
        for (float dz = -22f; dz <= 6f; dz += 2f)
            top = MathF.Max(top, game.Zone.GroundAt(ground.X + dx, ground.Z + dz).Y);
        top += 0.05f;
        var rig = new CrouchRig(top, new Vector3(ground.X, top + 0.05f, ground.Z + 2f), ground.Z - 4f, ground.Z - 4f - Length);

        var concrete = new StandardMaterial3D { AlbedoColor = new Color("8d8a82"), Roughness = 0.9f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("55534e"), Roughness = 0.85f };
        var stripe = new StandardMaterial3D { AlbedoColor = new Color("e85d04"), EmissionEnabled = true, Emission = new Color("e85d04"), EmissionEnergyMultiplier = 0.6f };

        void Block(string name, Vector3 centre, Vector3 size, Material look)
        {
            var body = new StaticBody3D { Name = name, CollisionLayer = Layers.World, CollisionMask = 0, Position = centre };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = look });
            game.Zone.AddChild(body);
        }

        float x = ground.X, z = (rig.Near + rig.Far) / 2f, half = Width / 2f;
        // The pad, then the bulkhead: a block either side of the passage and the lintel over it.
        Block("CrouchPad", new Vector3(x, top - 1f, ground.Z - 6f), new Vector3(14f, 2f, 28f), dark);
        float wall = 3.4f, side = 5f;
        Block("CrouchWallL", new Vector3(x - half - side / 2f, top + wall / 2f, z), new Vector3(side, wall, Length), concrete);
        Block("CrouchWallR", new Vector3(x + half + side / 2f, top + wall / 2f, z), new Vector3(side, wall, Length), concrete);
        Block("CrouchLintel", new Vector3(x, top + Gap + (wall - Gap) / 2f, z), new Vector3(Width, wall - Gap, Length), concrete);
        // A stripe along the lintel's lower edge, so the gap reads from a distance.
        Block("CrouchStripe", new Vector3(x, top + Gap + 0.1f, rig.Near + 0.02f), new Vector3(Width, 0.2f, 0.05f), stripe);
        return rig;
    }
}
