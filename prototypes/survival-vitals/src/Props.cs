using System;
using Godot;
using SurvivalVitals.Model;

namespace SurvivalVitals;

/// <summary>
/// Builds every prop in code from primitive meshes, in the chunky, colourful style of a 1970s
/// paperback cover. Props are placed in world coordinates; each builder returns its root node.
/// </summary>
public static class Props
{
    // ---- Building blocks -----------------------------------------------------------------

    static MeshInstance3D Mesh(Node3D parent, Mesh mesh, Material material, Vector3 at, Vector3 rotationDegrees = default)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at, RotationDegrees = rotationDegrees };
        parent.AddChild(m);
        return m;
    }

    static MeshInstance3D Box(Node3D parent, Vector3 size, Material material, Vector3 at, Vector3 rotationDegrees = default) =>
        Mesh(parent, new BoxMesh { Size = size }, material, at, rotationDegrees);

    static MeshInstance3D Cylinder(Node3D parent, float radius, float height, Material material, Vector3 at, int segments = 24) =>
        Mesh(parent, new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = segments }, material, at);

    /// <summary>A box you can't walk through.</summary>
    static void Wall(Node3D parent, Vector3 size, Material material, Vector3 at)
    {
        var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = at };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
        parent.AddChild(body);
    }

    static void Collider(Node3D parent, Shape3D shape, Vector3 at)
    {
        var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = at };
        body.AddChild(new CollisionShape3D { Shape = shape });
        parent.AddChild(body);
    }

    public static Label3D Label(Node3D parent, string text, Vector3 at, int size = 48, Color? color = null, bool fixedSize = false)
    {
        var label = new Label3D
        {
            Text = text,
            Position = at,
            FontSize = size,
            PixelSize = 0.0075f,
            Modulate = color ?? Palette.Cream,
            OutlineModulate = new Color(0, 0, 0, 0.85f),
            OutlineSize = 10,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            FixedSize = fixedSize,
            NoDepthTest = fixedSize,
        };
        if (fixedSize) label.PixelSize = 0.0007f;
        parent.AddChild(label);
        return label;
    }

    static OmniLight3D Light(Node3D parent, Color color, float energy, float range, Vector3 at)
    {
        var light = new OmniLight3D { LightColor = color, LightEnergy = energy, OmniRange = range, Position = at };
        parent.AddChild(light);
        return light;
    }

    static Node3D Root(string name, Vector3 at, float yawDegrees = 0)
    {
        var root = new Node3D { Name = name, Position = at, RotationDegrees = new Vector3(0, yawDegrees, 0) };
        return root;
    }

    // ---- The shelter ---------------------------------------------------------------------

    /// <summary>
    /// A breathable prefab room on a concrete pad: door in the south wall, livery outside, warm
    /// light inside. The walls sit just outside the breathable volume from planet.json.
    /// </summary>
    public static Node3D Shelter(Volume room)
    {
        var root = new Node3D { Name = "Shelter" };
        const float t = 0.3f, doorWidth = 2.4f, doorHeight = 3f;
        float x0 = (float)room.Min.X, x1 = (float)room.Max.X, z0 = (float)room.Min.Z, z1 = (float)room.Max.Z, top = (float)room.Max.Y;
        float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, w = x1 - x0 + 2 * t, d = z1 - z0 + 2 * t;
        var hull = Palette.Solid(Palette.Hull);
        var inner = Palette.Solid(new Color("c9cdd2"));

        Wall(root, new Vector3(w, 0.42f, d), Palette.Solid(Palette.Deck), new Vector3(cx, -0.19f, cz));
        Wall(root, new Vector3(w + 0.6f, 0.35f, d + 0.6f), hull, new Vector3(cx, top + 0.175f, cz));
        Wall(root, new Vector3(w, top, t), hull, new Vector3(cx, top / 2, z0 - t / 2));
        Wall(root, new Vector3(t, top, d), hull, new Vector3(x0 - t / 2, top / 2, cz));
        Wall(root, new Vector3(t, top, d), hull, new Vector3(x1 + t / 2, top / 2, cz));
        float left = cx - doorWidth / 2 - (x0 - t), right = (x1 + t) - (cx + doorWidth / 2);
        Wall(root, new Vector3(left, top, t), hull, new Vector3(x0 - t + left / 2, top / 2, z1 + t / 2));
        Wall(root, new Vector3(right, top, t), hull, new Vector3(x1 + t - right / 2, top / 2, z1 + t / 2));
        Wall(root, new Vector3(doorWidth, top - doorHeight, t), hull, new Vector3(cx, (top + doorHeight) / 2, z1 + t / 2));

        // Inside: pale liners, a dark skirting band and ceiling light panels.
        Box(root, new Vector3(x1 - x0, 0.04f, z1 - z0), inner, new Vector3(cx, top - 0.03f, cz));
        Box(root, new Vector3(0.04f, 0.6f, z1 - z0), Palette.Solid(Palette.Dark), new Vector3(x0 + 0.02f, 0.3f, cz));
        Box(root, new Vector3(0.04f, 0.6f, z1 - z0), Palette.Solid(Palette.Dark), new Vector3(x1 - 0.02f, 0.3f, cz));
        foreach (float px in new[] { cx - 4f, cx + 4f })
        {
            Box(root, new Vector3(2.4f, 0.04f, 1.2f), Palette.Glow(new Color("fff1d6"), 1.5f), new Vector3(px, top - 0.07f, cz));
            Light(root, new Color("ffe2b0"), 1.4f, 11f, new Vector3(px, top - 0.6f, cz));
        }

        // Outside: an orange band, a yellow pinstripe, a window strip, and a framed door.
        var orange = Palette.Solid(Palette.Orange);
        var yellow = Palette.Solid(Palette.Yellow);
        var window = Palette.Solid(new Color("1b263b"), 0.15f, 0.6f);
        Box(root, new Vector3(w + 0.04f, 0.8f, 0.04f), orange, new Vector3(cx, 1.3f, z0 - t - 0.02f));
        Box(root, new Vector3(w + 0.04f, 0.14f, 0.04f), yellow, new Vector3(cx, 1.85f, z0 - t - 0.02f));
        foreach (float sx in new[] { x0 - t - 0.02f, x1 + t + 0.02f })
        {
            Box(root, new Vector3(0.04f, 0.8f, d + 0.04f), orange, new Vector3(sx, 1.3f, cz));
            Box(root, new Vector3(0.04f, 0.14f, d + 0.04f), yellow, new Vector3(sx, 1.85f, cz));
            Box(root, new Vector3(0.04f, 0.7f, d - 2f), window, new Vector3(sx, 3.0f, cz));
        }
        foreach (float sx in new[] { x0 - t + left / 2, x1 + t - right / 2 })
        {
            float span = sx < cx ? left : right;
            Box(root, new Vector3(span, 0.8f, 0.04f), orange, new Vector3(sx, 1.3f, z1 + t + 0.02f));
            Box(root, new Vector3(span, 0.14f, 0.04f), yellow, new Vector3(sx, 1.85f, z1 + t + 0.02f));
        }
        foreach (float fx in new[] { cx - doorWidth / 2 - 0.1f, cx + doorWidth / 2 + 0.1f })
            Box(root, new Vector3(0.2f, doorHeight + 0.1f, 0.06f), yellow, new Vector3(fx, doorHeight / 2, z1 + t + 0.04f));
        Box(root, new Vector3(doorWidth + 0.4f, 0.2f, 0.06f), yellow, new Vector3(cx, doorHeight + 0.05f, z1 + t + 0.04f));
        for (int i = 0; i < 6; i++)
            Box(root, new Vector3(0.4f, 0.02f, 0.5f), Palette.Solid(i % 2 == 0 ? Palette.Yellow : Palette.Dark),
                new Vector3(cx - 1f + i * 0.4f, 0.035f, z1 + t / 2));
        Box(root, new Vector3(1.2f, 0.6f, 0.1f), Palette.Solid(Palette.Teal), new Vector3(cx + 3.2f, 4.0f, z1 + t + 0.06f));
        Label(root, "SHELTER 7", new Vector3(cx, 3.75f, z1 + t + 0.12f), 64).Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;
        Label(root, "BREATHABLE  ·  CLIMATE CONTROLLED", new Vector3(cx, 3.42f, z1 + t + 0.12f), 28, Palette.Yellow)
            .Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;

        // Antenna and a roof unit for silhouette.
        Box(root, new Vector3(3f, 1.1f, 2f), Palette.Solid(Palette.Dark), new Vector3(cx + 4.5f, top + 0.9f, cz - 2f));
        Box(root, new Vector3(3.05f, 0.12f, 2.05f), Palette.Solid(Palette.Teal), new Vector3(cx + 4.5f, top + 1.1f, cz - 2f));
        Cylinder(root, 0.06f, 3.5f, Palette.Solid(Palette.Hull), new Vector3(cx - 5f, top + 2f, cz - 3f), 8);
        Mesh(root, new SphereMesh { Radius = 0.15f, Height = 0.3f }, Palette.Glow(Palette.Red, 3f), new Vector3(cx - 5f, top + 3.8f, cz - 3f));
        return root;
    }

    /// <summary>Restores every health need in one place. Faces +X (out of the west wall).</summary>
    public static Node3D HealingBooth(Vector3 at)
    {
        Node3D root = Root("HealingBooth", at);
        Cylinder(root, 0.8f, 0.25f, Palette.Solid(Palette.Dark), new Vector3(0, 0.125f, 0));
        Cylinder(root, 0.68f, 2f, Palette.Clear(new Color(0.5f, 0.95f, 0.9f, 0.25f)), new Vector3(0, 1.25f, 0));
        Cylinder(root, 0.8f, 0.36f, Palette.Solid(Palette.Hull), new Vector3(0, 2.43f, 0));
        Cylinder(root, 0.71f, 0.06f, Palette.Glow(new Color("5ff5e0"), 3f), new Vector3(0, 2.2f, 0));
        Cylinder(root, 0.71f, 0.06f, Palette.Glow(new Color("5ff5e0"), 3f), new Vector3(0, 0.3f, 0));
        Box(root, new Vector3(0.3f, 2.8f, 1.8f), Palette.Solid(Palette.Hull), new Vector3(-0.75f, 1.4f, 0));
        Box(root, new Vector3(0.04f, 0.26f, 0.08f), Palette.Glow(Palette.Red, 1.5f), new Vector3(0.81f, 2.43f, 0));
        Box(root, new Vector3(0.04f, 0.08f, 0.26f), Palette.Glow(Palette.Red, 1.5f), new Vector3(0.81f, 2.43f, 0));
        Light(root, new Color("5ff5e0"), 0.8f, 4f, new Vector3(0.2f, 1.4f, 0));
        Collider(root, new CylinderShape3D { Radius = 0.8f, Height = 2.6f }, new Vector3(0, 1.3f, 0));
        Label(root, "HEALING BOOTH", new Vector3(0.4f, 3.0f, 0), 40);
        return root;
    }

    /// <summary>A clone chamber. The returned lamp turns green when it is flagged as home.</summary>
    public static Node3D CloneChamber(Vector3 at, out MeshInstance3D homeLamp)
    {
        Node3D root = Root("CloneChamber", at);
        Cylinder(root, 0.8f, 0.3f, Palette.Solid(Palette.Dark), new Vector3(0, 0.15f, 0));
        Cylinder(root, 0.66f, 2.2f, Palette.Clear(new Color(1f, 0.75f, 0.35f, 0.25f)), new Vector3(0, 1.4f, 0));
        Mesh(root, new CapsuleMesh { Radius = 0.24f, Height = 1.6f }, Palette.Glow(new Color("ffb347"), 0.5f), new Vector3(0, 1.3f, 0));
        Cylinder(root, 0.8f, 0.4f, Palette.Solid(Palette.Orange), new Vector3(0, 2.7f, 0));
        Box(root, new Vector3(0.3f, 3f, 1.8f), Palette.Solid(Palette.Hull), new Vector3(-0.75f, 1.5f, 0));
        homeLamp = Mesh(root, new SphereMesh { Radius = 0.16f, Height = 0.32f }, Palette.Solid(new Color("555555")), new Vector3(0.5f, 3.05f, 0));
        Light(root, new Color("ffb347"), 0.7f, 4f, new Vector3(0.2f, 1.5f, 0));
        Collider(root, new CylinderShape3D { Radius = 0.8f, Height = 2.9f }, new Vector3(0, 1.45f, 0));
        Label(root, "CLONE CHAMBER", new Vector3(0.4f, 3.4f, 0), 40);
        return root;
    }

    public static void SetHomeLamp(MeshInstance3D lamp, bool home) =>
        lamp.MaterialOverride = home ? Palette.Glow(new Color("57ff7a"), 3f) : Palette.Solid(new Color("555555"));

    /// <summary>A replicator of the given tier, facing +Z.</summary>
    public static Node3D Replicator(ReplicatorTier tier, Vector3 at, float yawDegrees = 0)
    {
        Node3D root = Root($"Replicator_{tier.Id}", at, yawDegrees);
        switch (tier.Tier)
        {
            case 1: // portable: a squat orange box on three legs, with a whip antenna
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.Tau / 3f;
                    Mesh(root, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 0.6f, RadialSegments = 6 },
                        Palette.Solid(Palette.Dark), new Vector3(Mathf.Cos(a) * 0.35f, 0.28f, Mathf.Sin(a) * 0.35f),
                        new Vector3(Mathf.Sin(a) * 15f, 0, -Mathf.Cos(a) * 15f));
                }
                Box(root, new Vector3(0.95f, 0.7f, 0.75f), Palette.Solid(Palette.Orange), new Vector3(0, 0.85f, 0));
                Box(root, new Vector3(1.0f, 0.1f, 0.8f), Palette.Solid(Palette.Dark), new Vector3(0, 1.24f, 0));
                Box(root, new Vector3(0.96f, 0.08f, 0.76f), Palette.Solid(Palette.Yellow), new Vector3(0, 0.62f, 0));
                Box(root, new Vector3(0.5f, 0.38f, 0.03f), Palette.Solid(Palette.Dark), new Vector3(0, 0.88f, 0.376f));
                Box(root, new Vector3(0.42f, 0.3f, 0.04f), Palette.Glow(new Color("ffb347"), 1.4f), new Vector3(0, 0.88f, 0.38f));
                Cylinder(root, 0.015f, 0.9f, Palette.Solid(Palette.Dark), new Vector3(0.38f, 1.7f, -0.25f), 6);
                Mesh(root, new SphereMesh { Radius = 0.05f, Height = 0.1f }, Palette.Glow(Palette.Red, 4f), new Vector3(0.38f, 2.15f, -0.25f));
                Light(root, Palette.Yellow, 0.6f, 3f, new Vector3(0, 0.9f, 0.7f));
                Collider(root, new BoxShape3D { Size = new Vector3(0.95f, 1.25f, 0.75f) }, new Vector3(0, 0.62f, 0));
                Label(root, "SURVIVAL REPLICATOR", new Vector3(0, 1.75f, 0), 24);
                break;
            case 2: // galley: a kitchen counter with a glowing hatch
                Box(root, new Vector3(1.7f, 1.1f, 0.8f), Palette.Solid(Palette.Hull), new Vector3(0, 0.55f, 0));
                Box(root, new Vector3(1.8f, 0.08f, 0.9f), Palette.Solid(Palette.Dark), new Vector3(0, 1.14f, 0.02f));
                Box(root, new Vector3(1.7f, 0.12f, 0.02f), Palette.Solid(Palette.Teal), new Vector3(0, 0.2f, 0.41f));
                Box(root, new Vector3(0.7f, 0.42f, 0.03f), Palette.Glow(Palette.Orange, 2f), new Vector3(-0.3f, 0.7f, 0.41f));
                Box(root, new Vector3(0.3f, 0.2f, 0.03f), Palette.Glow(new Color("5ff5e0"), 1.5f), new Vector3(0.5f, 0.82f, 0.41f));
                Box(root, new Vector3(1.6f, 1.4f, 0.3f), Palette.Solid(Palette.Hull), new Vector3(0, 1.9f, -0.25f));
                Light(root, Palette.Orange, 0.6f, 3f, new Vector3(-0.3f, 0.8f, 0.8f));
                Collider(root, new BoxShape3D { Size = new Vector3(1.8f, 2.6f, 0.9f) }, new Vector3(0, 1.3f, 0));
                Label(root, "GALLEY REPLICATOR", new Vector3(0, 2.85f, 0.2f), 34);
                break;
            default: // advanced: a tall yellow unit with twin hatches
                Box(root, new Vector3(2.0f, 2.2f, 0.9f), Palette.Solid(Palette.Yellow), new Vector3(0, 1.1f, 0));
                Box(root, new Vector3(2.04f, 0.16f, 0.94f), Palette.Solid(Palette.Teal), new Vector3(0, 1.6f, 0));
                Box(root, new Vector3(2.04f, 0.16f, 0.94f), Palette.Solid(Palette.Dark), new Vector3(0, 0.1f, 0));
                foreach (float x in new[] { -0.5f, 0.5f })
                    Box(root, new Vector3(0.6f, 0.5f, 0.03f), Palette.Glow(new Color("5ff5e0"), 2f), new Vector3(x, 1.0f, 0.46f));
                Light(root, new Color("5ff5e0"), 0.6f, 3f, new Vector3(0, 1f, 0.9f));
                Collider(root, new BoxShape3D { Size = new Vector3(2f, 2.2f, 0.9f) }, new Vector3(0, 1.1f, 0));
                Label(root, "ADVANCED REPLICATOR", new Vector3(0, 2.6f, 0.2f), 34);
                break;
        }
        return root;
    }

    public static Node3D StorageCrate(Vector3 at)
    {
        Node3D root = Root("Storage", at);
        Box(root, new Vector3(1.1f, 1.1f, 1.8f), Palette.Solid(Palette.Dark), new Vector3(0, 0.55f, 0));
        Box(root, new Vector3(1.14f, 0.18f, 1.84f), Palette.Solid(Palette.Teal), new Vector3(0, 0.8f, 0));
        Box(root, new Vector3(0.02f, 0.3f, 0.6f), Palette.Glow(new Color("57ff7a"), 1f), new Vector3(-0.56f, 0.45f, 0));
        Collider(root, new BoxShape3D { Size = new Vector3(1.1f, 1.1f, 1.8f) }, new Vector3(0, 0.55f, 0));
        Label(root, "ORGANICS STORAGE", new Vector3(0, 1.5f, 0), 32);
        return root;
    }

    // ---- The crash site ------------------------------------------------------------------

    /// <summary>An escape pod: a wreck half dug into the ground, or a fresh one standing upright.</summary>
    public static Node3D EscapePod(Vector3 at, bool wrecked)
    {
        Node3D root = Root("EscapePod", at, wrecked ? 35f : 0f);
        var body = new Node3D
        {
            Position = wrecked ? new Vector3(0, 0.75f, 0) : new Vector3(0, 2.2f, 0),
            RotationDegrees = wrecked ? new Vector3(0, 0, 76f) : Vector3.Zero,
        };
        root.AddChild(body);
        Mesh(body, new CapsuleMesh { Radius = 1.25f, Height = 4.4f, RadialSegments = 20 }, Palette.Solid(Palette.Hull), Vector3.Zero);
        Cylinder(body, 1.28f, 0.5f, Palette.Solid(Palette.Orange), new Vector3(0, 0.6f, 0));
        Cylinder(body, 1.28f, 0.12f, Palette.Solid(Palette.Yellow), new Vector3(0, 1.0f, 0));
        Mesh(body, new CylinderMesh { TopRadius = 0.5f, BottomRadius = 1.1f, Height = 0.6f }, Palette.Solid(Palette.Dark), new Vector3(0, -2.2f, 0));
        Box(body, new Vector3(0.1f, 1.3f, 0.9f), Palette.Solid(new Color("1b263b"), 0.2f, 0.5f), new Vector3(1.22f, -0.3f, 0));
        Box(body, new Vector3(0.12f, 1.45f, 0.08f), Palette.Solid(Palette.Yellow), new Vector3(1.23f, -0.3f, 0.5f));
        Box(body, new Vector3(0.12f, 1.45f, 0.08f), Palette.Solid(Palette.Yellow), new Vector3(1.23f, -0.3f, -0.5f));
        if (wrecked)
        {
            // Scorch marks, a flung hatch and a thin trail of smoke.
            Box(body, new Vector3(0.05f, 1.6f, 1.6f), Palette.Solid(new Color("1d1a18")), new Vector3(-1.2f, -0.8f, 0.2f), new Vector3(0, 0, 8));
            Box(root, new Vector3(1.0f, 0.08f, 1.4f), Palette.Solid(Palette.Hull), new Vector3(2.6f, 0.15f, 1.2f), new Vector3(8, 30, 5));
            root.AddChild(Smoke(new Vector3(0, 1.5f, 0), 0.6f));
        }
        Collider(root, new CapsuleShape3D { Radius = 1.25f, Height = 4.4f }, body.Position);
        root.GetChild<StaticBody3D>(root.GetChildCount() - 1).RotationDegrees = body.RotationDegrees;
        Label(root, wrecked ? "ESCAPE POD" : "ESCAPE POD  ·  FRESH START", new Vector3(0, wrecked ? 2.6f : 5.2f, 0), 40);
        return root;
    }

    public static Node3D Tent(Vector3 at)
    {
        Node3D root = Root("Tent", at, 180f);
        Mesh(root, new PrismMesh { Size = new Vector3(2.6f, 1.7f, 3f) }, Palette.Solid(Palette.Orange, 0.95f), new Vector3(0, 0.85f, 0));
        Mesh(root, new PrismMesh { Size = new Vector3(1.0f, 1.1f, 0.05f) }, Palette.Solid(Palette.Dark), new Vector3(0, 0.55f, -1.51f));
        Box(root, new Vector3(0.06f, 0.06f, 3.3f), Palette.Solid(Palette.Yellow), new Vector3(0, 1.72f, 0));
        Collider(root, new BoxShape3D { Size = new Vector3(2.2f, 1.2f, 2.8f) }, new Vector3(0, 0.6f, 0));
        Label(root, "SURVIVAL TENT", new Vector3(0, 2.3f, 0), 36);
        return root;
    }

    /// <summary>An alien fruit bush. The returned node holds the fruit, hidden while it regrows.</summary>
    public static Node3D Bush(Vector3 at, int seed, out Node3D fruit)
    {
        Node3D root = Root("Bush", at, seed * 47 % 360);
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed + 11) };
        var leaves = Palette.Solid(Palette.Foliage, 0.9f);
        var leaves2 = Palette.Solid(new Color("2f6f73"), 0.9f);
        for (int i = 0; i < 4; i++)
        {
            float r = rng.RandfRange(0.45f, 0.75f);
            Mesh(root, new SphereMesh { Radius = r, Height = r * 1.6f, RadialSegments = 10, Rings = 6 }, i % 2 == 0 ? leaves : leaves2,
                new Vector3(rng.RandfRange(-0.5f, 0.5f), r * 0.75f + rng.RandfRange(0, 0.3f), rng.RandfRange(-0.5f, 0.5f)));
        }
        fruit = new Node3D { Name = "Fruit" };
        root.AddChild(fruit);
        var fruitMaterial = Palette.Glow(Palette.Fruit, 0.6f);
        for (int i = 0; i < 7; i++)
        {
            float a = rng.RandfRange(0, Mathf.Tau), h = rng.RandfRange(0.4f, 1.2f);
            Mesh(fruit, new SphereMesh { Radius = 0.12f, Height = 0.24f, RadialSegments = 8, Rings = 4 }, fruitMaterial,
                new Vector3(Mathf.Cos(a) * 0.72f, h, Mathf.Sin(a) * 0.72f));
        }
        return root;
    }

    /// <summary>A faceted boulder streaked with ore, for mining.</summary>
    public static StaticBody3D Rock(Vector3 at, float size, int seed)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919 + 3) };
        var rock = new StaticBody3D { Name = "Rock", CollisionLayer = Layers.World, CollisionMask = 0, Position = at };
        rock.AddToGroup("rock");
        rock.RotationDegrees = new Vector3(0, rng.RandfRange(0, 360), 0);
        var scale = new Vector3(size, size * rng.RandfRange(0.6f, 0.85f), size * rng.RandfRange(0.8f, 1.1f));
        var mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 7, Rings = 4 },
            MaterialOverride = Palette.Solid(new[] { Palette.Rust, new Color("8b3a1e"), new Color("b5651d") }[seed % 3], 1f),
            Scale = scale,
            Position = new Vector3(0, scale.Y * 0.55f, 0),
        };
        rock.AddChild(mesh);
        var ore = Palette.Glow(Palette.Yellow, 0.4f);
        ore.Metallic = 0.8f;
        for (int i = 0; i < 5; i++)
        {
            float a = rng.RandfRange(0, Mathf.Tau);
            Box(rock, new Vector3(0.18f, 0.08f, 0.3f) * size, ore,
                new Vector3(Mathf.Cos(a) * scale.X * 0.92f, scale.Y * rng.RandfRange(0.4f, 1.0f), Mathf.Sin(a) * scale.Z * 0.92f),
                new Vector3(rng.RandfRange(-30, 30), Mathf.RadToDeg(-a), 0));
        }
        rock.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = size * 0.85f }, Position = new Vector3(0, scale.Y * 0.55f, 0) });
        return rock;
    }

    // ---- Hazards and markings ------------------------------------------------------------

    /// <summary>A yellow-and-black warning sign on a post: environmental dangers are never silent.</summary>
    public static Node3D HazardSign(string text, Vector3 at, float yawDegrees)
    {
        Node3D root = Root("Sign", at, yawDegrees);
        Cylinder(root, 0.06f, 2.2f, Palette.Solid(Palette.Dark), new Vector3(0, 1.1f, 0), 8);
        Box(root, new Vector3(1.9f, 0.7f, 0.06f), Palette.Solid(Palette.Yellow), new Vector3(0, 2.1f, 0));
        for (int i = 0; i < 5; i++)
            Box(root, new Vector3(0.18f, 0.7f, 0.07f), Palette.Solid(Palette.Dark), new Vector3(-0.8f + i * 0.4f, 1.62f, 0), new Vector3(0, 0, 35));
        Label3D label = Label(root, text, new Vector3(0, 2.1f, 0.05f), 26, Palette.Dark);
        label.Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;
        label.OutlineSize = 0;
        Label3D back = Label(root, text, new Vector3(0, 2.1f, -0.05f), 26, Palette.Dark);
        back.Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;
        back.OutlineSize = 0;
        back.RotationDegrees = new Vector3(0, 180, 0);
        return root;
    }

    /// <summary>Faction land: red-and-white posts and painted dashes round the edge, a pylon in the middle.</summary>
    public static Node3D TerritoryMarker(Territory land, Func<float, float, float> height)
    {
        var root = new Node3D { Name = "Territory" };
        float cx = (float)land.Center.X, cz = (float)land.Center.Z, r = (float)land.Radius;
        int posts = (int)(Mathf.Tau * r / 7f);
        var red = Palette.Solid(Palette.Red);
        var white = Palette.Solid(new Color("f2efe6"));
        for (int i = 0; i < posts; i++)
        {
            float a = i * Mathf.Tau / posts;
            float x = cx + Mathf.Cos(a) * r, z = cz + Mathf.Sin(a) * r;
            float y = height(x, z);
            Cylinder(root, 0.1f, 0.8f, red, new Vector3(x, y + 0.4f, z), 8);
            Cylinder(root, 0.1f, 0.8f, white, new Vector3(x, y + 1.2f, z), 8);
            Mesh(root, new SphereMesh { Radius = 0.12f, Height = 0.24f }, Palette.Glow(Palette.Red, 2.5f), new Vector3(x, y + 1.7f, z));
            // Painted dashes between posts.
            for (int j = 1; j < 3; j++)
            {
                float b = a + j * Mathf.Tau / posts / 3f;
                float dx = cx + Mathf.Cos(b) * r, dz = cz + Mathf.Sin(b) * r;
                Box(root, new Vector3(0.45f, 0.06f, 1.3f), red, new Vector3(dx, height(dx, dz) + 0.04f, dz), new Vector3(0, -Mathf.RadToDeg(b), 0));
            }
        }
        float py = height(cx, cz);
        Box(root, new Vector3(1.4f, 7f, 1.4f), Palette.Solid(Palette.Dark), new Vector3(cx, py + 3.5f, cz));
        Box(root, new Vector3(1.45f, 4f, 0.2f), Palette.Glow(Palette.Red, 1.2f), new Vector3(cx, py + 4f, cz + 0.65f));
        Box(root, new Vector3(1.45f, 4f, 0.2f), Palette.Glow(Palette.Red, 1.2f), new Vector3(cx, py + 4f, cz - 0.65f));
        Mesh(root, new SphereMesh { Radius = 0.45f, Height = 0.9f }, Palette.Glow(Palette.Red, 4f), new Vector3(cx, py + 7.4f, cz));
        Collider(root, new BoxShape3D { Size = new Vector3(1.4f, 7f, 1.4f) }, new Vector3(cx, py + 3.5f, cz));
        Label(root, "KESTREL COMBINE TERRITORY\nno survival devices", new Vector3(cx, py + 8.6f, cz), 64, new Color("ff9a8a"));
        return root;
    }

    /// <summary>Dressing that makes each hazard read from a distance.</summary>
    public static Node3D ZoneDressing(Zone zone, Terrain terrain)
    {
        var root = new Node3D { Name = zone.Id };
        float cx = (float)zone.Center.X, cz = (float)zone.Center.Z, r = (float)zone.Radius;
        var rng = new RandomNumberGenerator { Seed = (ulong)zone.Id.GetHashCode() & 0xffff };
        Vector3 Ground(float x, float z) => new(x, terrain.Height(x, z), z);

        switch (zone.Id)
        {
            case "frozen-valley":
                var ice = Palette.Glow(Palette.Ice, 0.4f);
                ice.Roughness = 0.2f;
                for (int i = 0; i < 18; i++)
                {
                    float a = rng.RandfRange(0, Mathf.Tau), d = rng.RandfRange(4, r + 6), h = rng.RandfRange(2.5f, 7f);
                    float x = cx + Mathf.Cos(a) * d, z = cz + Mathf.Sin(a) * d;
                    Mesh(root, new PrismMesh { Size = new Vector3(h * 0.35f, h, h * 0.3f) }, ice, Ground(x, z) + new Vector3(0, h / 2 - 0.3f, 0),
                        new Vector3(rng.RandfRange(-12, 12), rng.RandfRange(0, 180), rng.RandfRange(-12, 12)));
                }
                root.AddChild(Snow(Ground(cx, cz) + new Vector3(0, 16, 0), r + 8));
                // A cold fill light, so the hollow reads icy even under a warm desert sun.
                Light(root, new Color("a8d4ff"), 1.2f, 48f, Ground(cx, cz) + new Vector3(0, 14, 0));
                break;

            case "volcanic-vent":
                float lava = terrain.LavaLevel(zone);
                Cylinder(root, (float)zone.LavaRadius + 0.6f, 0.3f, Palette.Glow(Palette.Lava, 3.5f), new Vector3(cx, lava, cz), 32);
                Light(root, Palette.Lava, 4f, 26f, new Vector3(cx, lava + 3f, cz));
                root.AddChild(Embers(new Vector3(cx, lava + 0.3f, cz), (float)zone.LavaRadius));
                root.AddChild(Smoke(new Vector3(cx, lava + 2f, cz), 2.5f));
                break;

            case "radiation-crater":
                var crystal = Palette.Glow(Palette.RadGlow, 1.6f);
                for (int i = 0; i < 16; i++)
                {
                    float a = rng.RandfRange(0, Mathf.Tau), d = rng.RandfRange(0, r), h = rng.RandfRange(0.8f, 2.6f);
                    float x = cx + Mathf.Cos(a) * d, z = cz + Mathf.Sin(a) * d;
                    Mesh(root, new PrismMesh { Size = new Vector3(h * 0.4f, h, h * 0.4f) }, crystal, Ground(x, z) + new Vector3(0, h / 2 - 0.2f, 0),
                        new Vector3(rng.RandfRange(-25, 25), rng.RandfRange(0, 180), rng.RandfRange(-25, 25)));
                }
                Light(root, Palette.RadGlow, 2.5f, 30f, Ground(cx, cz) + new Vector3(0, 4, 0));
                break;

            case "airless-sinkhole":
                float width = (float)zone.Terrain.Width, depth = (float)-zone.Terrain.Depth;
                for (int i = 0; i < 4; i++)
                {
                    float y = -depth + 0.6f + i * 1.6f;
                    float radius = 3f + (width - 3f) * (y + depth) / depth;
                    Cylinder(root, radius, 0.05f, Palette.Beam(new Color(Palette.Gas, 0.16f)), new Vector3(cx, y + terrain.Height(cx + width + 4, cz), cz), 32);
                }
                break;
        }
        return root;
    }

    // ---- The backpack ----------------------------------------------------------------------

    /// <summary>A dropped backpack under a tall beam: it stays, marked, until it is picked up.</summary>
    public static Node3D BackpackMarker(Vector3 at)
    {
        Node3D root = Root("Backpack", at);
        Box(root, new Vector3(0.55f, 0.65f, 0.35f), Palette.Solid(Palette.Orange), new Vector3(0, 0.33f, 0));
        Box(root, new Vector3(0.45f, 0.25f, 0.1f), Palette.Solid(Palette.Dark), new Vector3(0, 0.3f, 0.21f));
        Box(root, new Vector3(0.57f, 0.08f, 0.37f), Palette.Solid(Palette.Yellow), new Vector3(0, 0.55f, 0));
        Cylinder(root, 0.22f, 220f, Palette.Beam(new Color(1f, 0.75f, 0.2f, 0.4f)), new Vector3(0, 110f, 0), 12);
        Cylinder(root, 0.6f, 220f, Palette.Beam(new Color(1f, 0.5f, 0.1f, 0.12f)), new Vector3(0, 110f, 0), 12);
        Cylinder(root, 1.4f, 0.03f, Palette.Beam(new Color(1f, 0.7f, 0.2f, 0.5f)), new Vector3(0, 0.03f, 0), 32);
        Light(root, Palette.Yellow, 1.2f, 6f, new Vector3(0, 1.2f, 0));
        return root;
    }

    // ---- Particles ---------------------------------------------------------------------------

    static CpuParticles3D Particles(int amount, float lifetime, Color color, float size, bool glow)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            // A soft round dot, so a flake or puff right in front of the visor never shows as a square.
            AlbedoTexture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = new[] { Colors.White, new Color(1, 1, 1, 0) } },
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(0.5f, 0f),
                Width = 32,
                Height = 32,
            },
        };
        if (glow) material.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
        return new CpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            Mesh = new QuadMesh { Size = new Vector2(size, size), Material = material },
            Color = color,
            Preprocess = lifetime,
        };
    }

    static CpuParticles3D Snow(Vector3 at, float radius)
    {
        CpuParticles3D p = Particles(900, 8f, new Color(1, 1, 1, 0.9f), 0.11f, false);
        p.Position = at;
        p.EmissionShape = CpuParticles3D.EmissionShapeEnum.Box;
        p.EmissionBoxExtents = new Vector3(radius, 1, radius);
        p.Direction = new Vector3(0.3f, -1, 0);
        p.Spread = 15;
        p.Gravity = new Vector3(0, -1.2f, 0);
        p.InitialVelocityMin = 1f;
        p.InitialVelocityMax = 2.5f;
        return p;
    }

    static CpuParticles3D Embers(Vector3 at, float radius)
    {
        CpuParticles3D p = Particles(80, 3f, new Color(1f, 0.55f, 0.15f, 1f), 0.12f, true);
        p.Position = at;
        p.EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere;
        p.EmissionSphereRadius = radius;
        p.Direction = Vector3.Up;
        p.Spread = 20;
        p.Gravity = new Vector3(0, 1.5f, 0);
        p.InitialVelocityMin = 1f;
        p.InitialVelocityMax = 3f;
        return p;
    }

    static CpuParticles3D Smoke(Vector3 at, float radius)
    {
        CpuParticles3D p = Particles(40, 7f, new Color(0.25f, 0.22f, 0.22f, 0.35f), 2.2f, false);
        p.Position = at;
        p.EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere;
        p.EmissionSphereRadius = radius;
        p.Direction = Vector3.Up;
        p.Spread = 10;
        p.Gravity = new Vector3(0.4f, 0.6f, 0);
        p.InitialVelocityMin = 0.8f;
        p.InitialVelocityMax = 1.6f;
        p.ScaleAmountMin = 1f;
        p.ScaleAmountMax = 2.5f;
        return p;
    }
}
