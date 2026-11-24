using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Where someone waits in a place, as the viewer shows it: a spawn marker standing on the floor (a ring on the floor,
/// a slim post and a cap), its cap coloured by who waits there. Not a body: the game spawns the real creature or
/// person from the world's bestiary and peoples, which this prototype doesn't have, so the viewer marks the spot
/// rather than drawing a stand-in that could be taken for the real thing (the old box figures were). A boss's
/// marker is taller. Behaviour belongs to the game.
/// </summary>
public static class Figures
{
    public static Node3D Make(EnemySpawn e, Faction faction, string roster)
    {
        var root = new Node3D { Name = e.Name };
        bool big = e.Kind == EnemyKind.Boss || e.Name.StartsWith("Alpha");
        Color cap = e.Kind switch
        {
            EnemyKind.Turret or EnemyKind.Hover => new Color("8a9099"), // machines
            EnemyKind.Hollow or EnemyKind.Corrupted => new Color("7a8a5a"), // the infested
            EnemyKind.Fauna => new Color("b08850"), // creatures
            _ => new Color("c8c0b0"), // people
        };
        float tall = big ? 2.2f : 1.6f;
        var post = new Color("3a3e44");
        Box(root, new Vector3(0.8f, 0.03f, 0.8f), new Vector3(0, 0.015f, 0), post);
        Box(root, new Vector3(0.06f, tall, 0.06f), new Vector3(0, tall / 2f, 0), post);
        Box(root, new Vector3(0.3f, 0.12f, 0.3f), new Vector3(0, tall + 0.06f, 0), cap);
        return root;
    }

    static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 at, Color color, bool glow = false)
    {
        var mat = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.75f };
        if (glow)
        {
            mat.EmissionEnabled = true;
            mat.Emission = color;
            mat.EmissionEnergyMultiplier = 3f;
        }
        var m = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, Position = at };
        parent.AddChild(m);
        return m;
    }
}
