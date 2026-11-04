using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Stand-in figures for whoever waits in a place: a few boxes each, shaped and coloured so
/// a Vharn drone, a Combine guard and a Bloom thrall read differently from across a room.
/// They don't move; behaviour belongs to the encounters prototype.
/// </summary>
public static class Figures
{
    public static Node3D Make(EnemySpawn e, Faction faction, string roster)
    {
        var root = new Node3D { Name = e.Name };
        switch (e.Kind)
        {
            case EnemyKind.Turret: Turret(root, e.Shooter == ShooterClass.Capital); break;
            case EnemyKind.Hover: Hover(root); break;
            case EnemyKind.Hollow: Thrall(root); break;
            case EnemyKind.Fauna: Beast(root, e.Name.StartsWith("Alpha")); break;
            default:
                Humanoid(root, roster, e.Kind == EnemyKind.Corrupted);
                break;
        }
        if (e.Kind == EnemyKind.Boss || e.Name.StartsWith("Alpha")) root.Scale = Vector3.One * 1.7f;
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

    static void Humanoid(Node3D r, string roster, bool corrupted)
    {
        Color body, trim, eye;
        switch (roster)
        {
            case "vharn":
                // Segmented, narrow-waisted, mandibles.
                body = new Color("5a2348"); trim = new Color("d9c8a0"); eye = new Color("ffa040");
                Box(r, new Vector3(0.5f, 0.55f, 0.35f), new Vector3(0, 0.45f, 0), body);
                Box(r, new Vector3(0.3f, 0.25f, 0.25f), new Vector3(0, 0.85f, 0), trim);
                Box(r, new Vector3(0.6f, 0.45f, 0.4f), new Vector3(0, 1.2f, 0), body);
                Box(r, new Vector3(0.36f, 0.3f, 0.42f), new Vector3(0, 1.58f, -0.05f), body.Darkened(0.2f));
                Box(r, new Vector3(0.08f, 0.08f, 0.3f), new Vector3(-0.12f, 1.5f, -0.3f), trim);
                Box(r, new Vector3(0.08f, 0.08f, 0.3f), new Vector3(0.12f, 1.5f, -0.3f), trim);
                Box(r, new Vector3(0.3f, 0.06f, 0.04f), new Vector3(0, 1.64f, -0.27f), eye, true);
                break;
            case "kessari":
                body = new Color("cfe3e0"); trim = new Color("2e3a3f"); eye = new Color("3df2e0");
                Box(r, new Vector3(0.1f, 0.9f, 0.1f), new Vector3(-0.15f, 0.45f, 0), body);
                Box(r, new Vector3(0.1f, 0.9f, 0.1f), new Vector3(0.15f, 0.45f, 0), body);
                Box(r, new Vector3(0.5f, 0.6f, 0.3f), new Vector3(0, 1.2f, 0), trim);
                Box(r, new Vector3(0.56f, 0.08f, 0.36f), new Vector3(0, 1.0f, 0), body);
                Box(r, new Vector3(0.3f, 0.3f, 0.3f), new Vector3(0, 1.7f, 0), body);
                Box(r, new Vector3(0.2f, 0.07f, 0.04f), new Vector3(0, 1.72f, -0.16f), eye, true);
                break;
            case "precursor":
                body = new Color("1f2029"); eye = new Color("4fe0b8");
                Box(r, new Vector3(0.6f, 2.2f, 0.4f), new Vector3(0, 1.1f, 0), body);
                Box(r, new Vector3(0.62f, 0.06f, 0.42f), new Vector3(0, 1.5f, 0), new Color("a8783a"));
                Box(r, new Vector3(0.12f, 0.5f, 0.04f), new Vector3(0, 1.85f, -0.21f), eye, true);
                break;
            default:
            {
                bool combine = roster == "meridian";
                body = combine ? new Color("e6e1d6") : new Color("6e5a3e");
                trim = combine ? new Color("3a4250") : new Color("4f7f78");
                eye = combine ? new Color("ef6c1a") : new Color("ffcf7a");
                Box(r, new Vector3(0.18f, 0.85f, 0.2f), new Vector3(-0.13f, 0.43f, 0), trim);
                Box(r, new Vector3(0.18f, 0.85f, 0.2f), new Vector3(0.13f, 0.43f, 0), trim);
                Box(r, new Vector3(0.55f, 0.65f, 0.32f), new Vector3(0, 1.18f, 0), body);
                Box(r, new Vector3(0.3f, 0.3f, 0.3f), new Vector3(0, 1.66f, 0), body);
                Box(r, new Vector3(0.26f, 0.08f, 0.04f), new Vector3(0, 1.68f, -0.16f), eye, true);
                if (!combine) Box(r, new Vector3(0.55f, 0.05f, 0.55f), new Vector3(0, 1.83f, 0), new Color("4a3a28")); // a wide hat
                // A rifle held across the body.
                Box(r, new Vector3(0.08f, 0.08f, 0.8f), new Vector3(0.22f, 1.15f, -0.25f), new Color("2a2a2c"));
                break;
            }
        }
        if (corrupted)
        {
            // The Bloom has them: a swelling on the shoulder, glowing seams.
            foreach (Node n in r.GetChildren())
                if (n is MeshInstance3D m && m.MaterialOverride is StandardMaterial3D s && !s.EmissionEnabled)
                    s.AlbedoColor = s.AlbedoColor.Lerp(StateColor.Bruise, 0.55f);
            Box(r, new Vector3(0.45f, 0.4f, 0.4f), new Vector3(0.25f, 1.45f, 0.05f), StateColor.Flesh);
            Box(r, new Vector3(0.12f, 0.12f, 0.42f), new Vector3(0.3f, 1.55f, 0.05f), StateColor.Glow, true);
        }
    }

    static void Thrall(Node3D r)
    {
        Box(r, new Vector3(0.9f, 0.7f, 0.7f), new Vector3(0, 0.35f, 0), StateColor.Bruise);
        Box(r, new Vector3(0.7f, 0.6f, 0.6f), new Vector3(0.05f, 0.9f, -0.1f), StateColor.Flesh);
        Box(r, new Vector3(0.4f, 0.35f, 0.4f), new Vector3(0, 1.3f, -0.25f), StateColor.Flesh.Darkened(0.2f));
        Box(r, new Vector3(0.25f, 0.06f, 0.04f), new Vector3(0, 1.32f, -0.46f), StateColor.Glow, true);
        for (int i = 0; i < 3; i++)
            Box(r, new Vector3(0.08f, 0.8f, 0.08f), new Vector3(-0.35f + i * 0.35f, 0.5f, 0.35f), StateColor.Flesh.Darkened(0.3f));
    }

    static void Beast(Node3D r, bool alpha)
    {
        var c = alpha ? new Color("4a3a30") : new Color("6a5a48");
        Box(r, new Vector3(0.5f, 0.35f, 1.0f), new Vector3(0, 0.45f, 0), c);
        Box(r, new Vector3(0.35f, 0.3f, 0.35f), new Vector3(0, 0.55f, -0.6f), c.Darkened(0.2f));
        foreach (var (x, z) in new[] { (-0.2f, -0.35f), (0.2f, -0.35f), (-0.2f, 0.35f), (0.2f, 0.35f) })
            Box(r, new Vector3(0.1f, 0.35f, 0.1f), new Vector3(x, 0.17f, z), c.Darkened(0.35f));
        Box(r, new Vector3(0.2f, 0.05f, 0.03f), new Vector3(0, 0.6f, -0.78f), new Color("ffb030"), true);
    }

    static void Turret(Node3D r, bool capital)
    {
        var steel = new Color("3a3f48");
        Box(r, new Vector3(1.0f, 0.4f, 1.0f), new Vector3(0, 0.2f, 0), steel);
        Box(r, new Vector3(0.7f, 0.5f, 0.8f), new Vector3(0, 0.65f, 0), steel.Lightened(0.15f));
        float len = capital ? 2.2f : 1.2f;
        Box(r, new Vector3(capital ? 0.24f : 0.12f, capital ? 0.24f : 0.12f, len), new Vector3(0, 0.7f, -len / 2f - 0.3f), new Color("22252a"));
        Box(r, new Vector3(0.3f, 0.08f, 0.04f), new Vector3(0, 0.85f, -0.42f), new Color("ff3020"), true);
    }

    static void Hover(Node3D r)
    {
        Box(r, new Vector3(1.4f, 0.25f, 1.0f), Vector3.Zero, new Color("4a5260"));
        Box(r, new Vector3(0.9f, 0.08f, 0.6f), new Vector3(0, -0.15f, 0), new Color("6fd0ff"), true);
        Box(r, new Vector3(0.12f, 0.12f, 0.9f), new Vector3(0, -0.05f, -0.8f), new Color("22252a"));
    }
}

/// <summary>Hollow Bloom colours for figures, matching the blocks.</summary>
public static class StateColor
{
    public static readonly Color Flesh = Look.ToColor(StateTransforms.BloomFlesh);
    public static readonly Color Bruise = Look.ToColor(StateTransforms.BloomBruise);
    public static readonly Color Glow = Look.ToColor(StateTransforms.BloomGlow);
}
