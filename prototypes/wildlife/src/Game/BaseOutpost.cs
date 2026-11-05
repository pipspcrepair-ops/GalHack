using System;
using Godot;

namespace Wildlife;

/// <summary>
/// The players' small outpost on the playable planet: a deck on short legs, a hab module, a
/// few crates and a core pylon glowing in base mode. It is here so the no-spawn rule has
/// something to keep clear: no wildlife spawns or respawns within the clearance of its deck.
/// Placeholder blocks; real bases come from the building prototype.
/// </summary>
public partial class BaseOutpost : Node3D
{
    public Structure Structure { get; private set; }

    static readonly Vector2 Half = new(9f, 7f);

    /// <summary>Places the outpost on the first of a few spots that is clear of rocks and groves.</summary>
    public void Build(Terrain terrain, NavGrid nav, Forest forest)
    {
        Vector3[] spots = { new(58f, 0f, 88f), new(-62f, 0f, 92f), new(92f, 0f, 38f), new(-96f, 0f, 30f) };
        Vector3 at = spots[0];
        foreach (Vector3 spot in spots)
        {
            if (Clear(spot, nav, forest)) { at = spot; break; }
        }
        float yaw = -0.35f;
        Structure = new Structure { Name = "Outpost", Mode = CoreMode.Base, Center = at, HalfExtents = Half, Yaw = yaw };

        // Sit the deck on the highest corner so it never sinks into the slope; legs reach down.
        Basis turn = new(Vector3.Up, yaw);
        float top = float.MinValue, low = float.MaxValue;
        for (int c = 0; c < 4; c++)
        {
            Vector3 corner = at + turn * new Vector3((c % 2 == 0 ? -1f : 1f) * Half.X, 0f, (c < 2 ? -1f : 1f) * Half.Y);
            float h = terrain.Height(corner.X, corner.Z);
            top = MathF.Max(top, h);
            low = MathF.Min(low, h);
        }
        Position = new Vector3(at.X, top + 0.6f, at.Z);
        Rotation = new Vector3(0f, yaw, 0f);

        var steel = new StandardMaterial3D { AlbedoColor = new Color("8b9297"), Roughness = 0.55f, Metallic = 0.4f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("2c3238"), Roughness = 0.6f, Metallic = 0.3f };
        var cream = new StandardMaterial3D { AlbedoColor = new Color("e9dcc0"), Roughness = 0.6f };
        var stripe = new StandardMaterial3D { AlbedoColor = new Color("e8641e"), Roughness = 0.5f };
        var core = new StandardMaterial3D { AlbedoColor = new Color("7cf9ff"), EmissionEnabled = true, Emission = new Color("7cf9ff"), EmissionEnergyMultiplier = 3f };

        var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
        AddChild(body);
        void Block(Vector3 size, Vector3 pos, Material m, bool solid = true)
        {
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = pos });
            if (solid) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = pos });
        }

        Block(new Vector3(Half.X * 2f, 0.4f, Half.Y * 2f), new Vector3(0f, -0.2f, 0f), steel);
        Block(new Vector3(Half.X * 2f + 0.1f, 0.12f, 0.5f), new Vector3(0f, 0.02f, Half.Y - 0.25f), stripe, solid: false);
        float legDrop = top - low + 1.2f;
        for (int c = 0; c < 4; c++)
        {
            var leg = new Vector3((c % 2 == 0 ? -1f : 1f) * (Half.X - 0.6f), -legDrop * 0.5f - 0.3f, (c < 2 ? -1f : 1f) * (Half.Y - 0.6f));
            Block(new Vector3(0.6f, legDrop, 0.6f), leg, dark, solid: false);
        }
        // The hab module, with a cream band and a dark door.
        Block(new Vector3(7f, 3.2f, 5f), new Vector3(-3.5f, 1.6f, -2f), cream);
        Block(new Vector3(7.05f, 0.5f, 5.05f), new Vector3(-3.5f, 2.6f, -2f), stripe, solid: false);
        Block(new Vector3(1.4f, 2.2f, 0.1f), new Vector3(-2.5f, 1.1f, 0.52f), dark, solid: false);
        // Crates by the edge.
        Block(new Vector3(1.4f, 1.2f, 1.4f), new Vector3(5.5f, 0.6f, 4.5f), stripe);
        Block(new Vector3(1.2f, 1.0f, 1.2f), new Vector3(7f, 0.5f, 4.6f), steel);
        Block(new Vector3(1.2f, 1.0f, 1.2f), new Vector3(5.6f, 1.7f, 4.4f), cream);
        // The core pylon: cyan for base mode.
        Block(new Vector3(1.6f, 0.8f, 1.6f), new Vector3(4f, 0.4f, -3f), dark);
        Block(new Vector3(0.7f, 3.4f, 0.7f), new Vector3(4f, 2.5f, -3f), core, solid: false);
        Block(new Vector3(1.1f, 0.3f, 1.1f), new Vector3(4f, 4.3f, -3f), dark, solid: false);
        AddChild(new OmniLight3D { LightColor = new Color("7cf9ff"), OmniRange = 9f, LightEnergy = 1.2f, Position = new Vector3(4f, 3f, -3f) });

        // Creatures walk round the solid parts, not through them.
        foreach (Vector3 local in new[] { new Vector3(-3.5f, 0f, -2f), new Vector3(4f, 0f, -3f), new Vector3(6f, 0f, 4.5f) })
            nav.AddObstacle(at + turn * local, local.X < 0f ? 4.2f : 1.5f);
    }

    static bool Clear(Vector3 spot, NavGrid nav, Forest forest)
    {
        for (float x = -Half.X - 4f; x <= Half.X + 4f; x += 2f)
            for (float z = -Half.Y - 4f; z <= Half.Y + 4f; z += 2f)
                if (nav.Blocked(spot + new Vector3(x, 0f, z))) return false;
        foreach (Vector3 g in forest.Groves)
            if (WildSim.Flat(g - spot).Length() < 40f) return false;
        return true;
    }
}
