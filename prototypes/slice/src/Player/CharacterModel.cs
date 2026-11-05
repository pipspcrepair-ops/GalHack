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
        // Arms.
        foreach (float x in new[] { -0.27f, 0.27f })
        {
            Part(new SphereMesh { Radius = 0.09f, Height = 0.18f }, plate, new Vector3(x, 1.38f, 0));
            Part(new CapsuleMesh { Radius = 0.06f, Height = 0.36f }, suit, new Vector3(x * 1.05f, 1.18f, 0), new Vector3(0, 0, x < 0 ? -8 : 8));
            Part(new CapsuleMesh { Radius = 0.055f, Height = 0.34f }, suit, new Vector3(x * 1.12f, 0.9f, -0.02f), new Vector3(-6, 0, x < 0 ? -4 : 4));
            Part(new BoxMesh { Size = new Vector3(0.08f, 0.1f, 0.08f) }, dark, new Vector3(x * 1.14f, 0.7f, -0.03f));
        }
        // Helmet and visor.
        Part(new SphereMesh { Radius = 0.16f, Height = 0.32f }, plate, new Vector3(0, 1.6f, 0));
        Part(new SphereMesh { Radius = 0.125f, Height = 0.2f }, visor, new Vector3(0, 1.6f, -0.065f));
        Part(new BoxMesh { Size = new Vector3(0.04f, 0.1f, 0.04f) }, dark, new Vector3(0.14f, 1.72f, 0.05f));
        foreach (Node n in root.GetChildren())
            if (n is MeshInstance3D m) m.Layers = 1u << 1; // its own layer: hidden from the first-person camera
        return root;
    }
}
