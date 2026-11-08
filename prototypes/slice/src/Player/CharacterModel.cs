using Godot;

namespace Slice.Player;

/// <summary>
/// The castaway's body as seen from outside: a suited figure 1.8 m tall in the slice's 1970s
/// paperback livery (orange suit, cream plates, a dark visor), built from capsules and boxes. It
/// stands in the inventory's paperdoll and in third person. Procedural, no assets.
/// </summary>
public static class CharacterModel
{
    public static Node3D Build()
    {
        var root = new Node3D { Name = "Castaway" };
        var suit = new StandardMaterial3D { AlbedoColor = new Color("e85d04"), Roughness = 0.75f };
        var plate = new StandardMaterial3D { AlbedoColor = new Color("ece3c8"), Roughness = 0.45f, Metallic = 0.2f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("2b2d42"), Roughness = 0.7f };
        var visor = new StandardMaterial3D { AlbedoColor = new Color("14213d"), Roughness = 0.1f, Metallic = 0.6f, EmissionEnabled = true, Emission = new Color("2ec4b6"), EmissionEnergyMultiplier = 0.25f };
        var glow = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color("ffb703") };

        void Part(Mesh m, Material mat, Vector3 at, Vector3 deg = default) =>
            root.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = at, RotationDegrees = deg });

        // Legs and boots.
        foreach (float x in new[] { -0.11f, 0.11f })
        {
            Part(new CapsuleMesh { Radius = 0.075f, Height = 0.5f }, suit, new Vector3(x, 0.62f, 0));
            Part(new CapsuleMesh { Radius = 0.07f, Height = 0.48f }, suit, new Vector3(x, 0.3f, 0.01f));
            Part(new BoxMesh { Size = new Vector3(0.13f, 0.1f, 0.24f) }, dark, new Vector3(x, 0.05f, -0.03f));
            Part(new BoxMesh { Size = new Vector3(0.11f, 0.14f, 0.05f) }, plate, new Vector3(x, 0.47f, -0.07f));
        }
        // Torso, chest plate, belt, backpack with its jetpack nozzles.
        Part(new CapsuleMesh { Radius = 0.19f, Height = 0.62f }, suit, new Vector3(0, 1.14f, 0));
        Part(new BoxMesh { Size = new Vector3(0.32f, 0.26f, 0.06f) }, plate, new Vector3(0, 1.24f, -0.17f));
        Part(new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.02f) }, glow, new Vector3(0.09f, 1.3f, -0.205f));
        Part(new BoxMesh { Size = new Vector3(0.4f, 0.07f, 0.3f) }, dark, new Vector3(0, 0.88f, 0));
        Part(new BoxMesh { Size = new Vector3(0.34f, 0.42f, 0.18f) }, plate, new Vector3(0, 1.17f, 0.2f));
        foreach (float x in new[] { -0.09f, 0.09f })
            Part(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 0.12f }, dark, new Vector3(x, 0.92f, 0.24f));
        // Arms, each hung from a shoulder joint with an elbow joint in it, so the body can gesture.
        foreach (float x in new[] { -0.27f, 0.27f })
        {
            Part(new SphereMesh { Radius = 0.09f, Height = 0.18f }, plate, new Vector3(x, 1.38f, 0));
            var shoulder = new Node3D { Name = x < 0 ? "ShoulderL" : "ShoulderR", Position = new Vector3(x, 1.38f, 0) };
            root.AddChild(shoulder);
            shoulder.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.06f, Height = 0.36f }, MaterialOverride = suit, Position = new Vector3(x * 0.05f, -0.2f, 0), RotationDegrees = new Vector3(0, 0, x < 0 ? -8 : 8) });
            var elbow = new Node3D { Name = "Elbow", Position = new Vector3(x * 0.09f, -0.35f, 0) };
            shoulder.AddChild(elbow);
            elbow.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.055f, Height = 0.34f }, MaterialOverride = suit, Position = new Vector3(x * 0.03f, -0.13f, -0.02f), RotationDegrees = new Vector3(-6, 0, x < 0 ? -4 : 4) });
            elbow.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.1f, 0.08f) }, MaterialOverride = dark, Position = new Vector3(x * 0.05f, -0.33f, -0.03f) });
        }
        // Helmet and visor.
        Part(new SphereMesh { Radius = 0.16f, Height = 0.32f }, plate, new Vector3(0, 1.6f, 0));
        Part(new SphereMesh { Radius = 0.125f, Height = 0.2f }, visor, new Vector3(0, 1.6f, -0.065f));
        Part(new BoxMesh { Size = new Vector3(0.04f, 0.1f, 0.04f) }, dark, new Vector3(0.14f, 1.72f, 0.05f));
        OwnLayer(root);
        return root;
    }

    /// <summary>Every part on its own layer: hidden from the first-person camera.</summary>
    static void OwnLayer(Node n)
    {
        if (n is MeshInstance3D m) m.Layers = 1u << 1;
        foreach (Node c in n.GetChildren()) OwnLayer(c);
    }

    /// <summary>How long a gesture plays on the body, seconds.</summary>
    public const float GestureSeconds = 1.7f;

    /// <summary>
    /// Poses the figure's arms for a gesture <paramref name="t"/> seconds after it was made (none, or
    /// over: arms at rest). The arms rise into the gesture, make it (a wave swings, a beckon curls,
    /// a fist shakes) and come down. Angles are a shoulder's lift forward, its lift out to the side
    /// and an elbow's bend, degrees.
    /// </summary>
    public static void Pose(Node3D figure, Wildlife.PlayerGesture? gesture, float t)
    {
        var right = figure.GetNodeOrNull<Node3D>("ShoulderR");
        var left = figure.GetNodeOrNull<Node3D>("ShoulderL");
        if (right == null || left == null) return;
        float w = gesture == null || t < 0f || t > GestureSeconds ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Min(t / 0.25f, (GestureSeconds - t) / 0.35f));
        float swing = Mathf.Sin(t * Mathf.Tau * 2.2f);
        (Vector3 r, Vector3 l) = gesture switch
        {
            Wildlife.PlayerGesture.Wave => (new Vector3(10f, 150f + 18f * swing, 25f), Vector3.Zero),
            Wildlife.PlayerGesture.Beckon => (new Vector3(70f, 12f, 55f + 35f * swing), Vector3.Zero),
            Wildlife.PlayerGesture.HandsUp => (new Vector3(5f, 160f, 20f), new Vector3(5f, 160f, 20f)),
            Wildlife.PlayerGesture.Stop => (new Vector3(95f, 5f, 5f), Vector3.Zero),
            Wildlife.PlayerGesture.Point => (new Vector3(90f, 8f, 0f), Vector3.Zero),
            Wildlife.PlayerGesture.Threaten => (new Vector3(80f + 10f * Mathf.Sin(t * Mathf.Tau * 4f), 10f, 70f), Vector3.Zero),
            Wildlife.PlayerGesture.Thanks => (new Vector3(45f, -25f, 115f), Vector3.Zero),
            _ => (Vector3.Zero, Vector3.Zero),
        };
        Arm(right, r * w, 1f);
        Arm(left, l * w, -1f);
    }

    static void Arm(Node3D shoulder, Vector3 a, float side)
    {
        // A forward lift turns the hanging arm toward -Z (the way the figure faces); a side lift takes it out from the body.
        shoulder.Rotation = new Vector3(Mathf.DegToRad(a.X), 0f, Mathf.DegToRad(a.Y) * side);
        if (shoulder.GetNodeOrNull<Node3D>("Elbow") is Node3D elbow) elbow.Rotation = new Vector3(Mathf.DegToRad(a.Z), 0f, 0f);
    }

    /// <summary>The figure's right hand, in the world (tests: is it raised?).</summary>
    public static Vector3 RightHand(Node3D figure) =>
        figure.GetNodeOrNull<Node3D>("ShoulderR/Elbow") is Node3D e ? e.ToGlobal(new Vector3(0.27f * 0.05f, -0.33f, -0.03f)) : figure.GlobalPosition;
}
