using Godot;
using Invasions.Director;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>A faction's colours. The tracer comes from factions.json; the rest is presentation.</summary>
public sealed record Livery(Color Main, Color Dark, Color Trim, Color Black, Color Metal, Color Glow, Color Tracer)
{
    public static Livery For(FactionData f)
    {
        var tracer = new Color(f.Tracer);
        return f.Id switch
        {
            "kessari" => new(new("56616b"), new("2b3238"), new("c9a227"), new("15191c"), new("7d8a94"), new("34e5ff"), tracer),
            "meridian" => new(new("e8eef2"), new("1d3557"), new("3a7ca5"), new("1b1f24"), new("9aa5ad"), new("7fd1ff"), tracer),
            "reavers" => new(new("8c3b12"), new("3b2416"), new("e9b949"), new("1c1410"), new("5c5248"), new("ff3b30"), tracer),
            "hollow" => new(new("6b1d55"), new("2e0b25"), new("d16ba5"), new("1a0716"), new("4a2d44"), new("e2ff5a"), tracer),
            "exchange" => new(new("d9b44a"), new("2f3e46"), new("f2e8cf"), new("141414"), new("6c757d"), new("ffd166"), tracer),
            "freeholders" => new(Palette.Cream, Palette.Dark, Palette.Orange, Palette.Black, Palette.Gunmetal, Palette.Teal, tracer),
            _ => new(Palette.Oxblood, Palette.Maroon, Palette.Bone, Palette.Black, Palette.Gunmetal, Palette.Acid, tracer),
        };
    }
}

/// <summary>
/// How each faction's people look: built from boxes, like everything else here. Every trooper and
/// every crew member is a person (Kessari are robot clans, so theirs are machines, but each one is a
/// clan member, not a remote drone). Forward is -Z; feet at y = 0.
/// </summary>
public static class Looks
{
    /// <summary>Builds a trooper's model and returns where its weapon's muzzle is, in the trooper's space.</summary>
    public static Vector3 Trooper(Node3D model, string faction, string look, Livery l)
    {
        switch (faction)
        {
            case "kessari": return look == "drone" ? KessariCutter(model, l) : KessariFrame(model, l, look == "lancer");
            case "meridian": return MeridianOfficer(model, l, look);
            case "reavers": return Reaver(model, l, look);
            case "hollow": return look == "corrupted" ? Corrupted(model, l) : Mutant(model, l, look == "burster");
            default: return Vharn(model, l, look);
        }
    }

    static Material M(Color c, float rough = 0.7f, float metal = 0f) => Palette.Mat(c, rough, metal);

    static void Box(Node3D p, float x, float y, float z, float sx, float sy, float sz, Material m) =>
        Palette.Box(p, new Vector3(x, y, z), new Vector3(sx, sy, sz), m);

    // ---- Vharn Dominion: oxblood armour, bone plate, acid visor ----------------------------------

    static Vector3 Vharn(Node3D model, Livery l, string look)
    {
        Material armour = M(l.Main, 0.6f), bone = M(l.Trim, 0.5f), dark = M(l.Black), metal = M(l.Metal, 0.4f, 0.5f);
        bool heavy = look == "heavy";
        float bulk = heavy ? 1.18f : 1f;
        Box(model, -0.13f, 0.43f, 0, 0.2f, 0.86f, 0.26f, dark);
        Box(model, 0.13f, 0.43f, 0, 0.2f, 0.86f, 0.26f, dark);
        Box(model, 0, 1.15f, 0, 0.52f * bulk, 0.62f, 0.32f * bulk, armour);
        Box(model, 0, 1.24f, -0.17f * bulk, 0.42f, 0.3f, 0.05f, bone);
        Box(model, -0.32f * bulk, 1.4f, 0, 0.18f, 0.16f, 0.34f, bone);
        Box(model, 0.32f * bulk, 1.4f, 0, 0.18f, 0.16f, 0.34f, bone);
        Box(model, 0, 1.64f, 0, 0.3f, 0.32f, 0.32f, bone);
        Box(model, 0, 1.66f, -0.165f, 0.26f, 0.07f, 0.02f, Palette.Glow(l.Glow, 3f));
        Box(model, 0, 1.82f, 0.02f, 0.06f, 0.06f, 0.3f, armour); // crest
        Box(model, 0, 1.18f, 0.24f * bulk, 0.38f, 0.42f, 0.18f, dark);
        if (look == "breacher")
        {
            Box(model, -0.1f, 1.2f, 0.36f, 0.14f, 0.3f, 0.1f, M(Palette.Orange));
            Box(model, 0.1f, 1.2f, 0.36f, 0.14f, 0.3f, 0.1f, M(Palette.Orange));
        }
        float gun = heavy ? 1.1f : 0.8f;
        Box(model, 0.22f, 1.2f, -0.3f, heavy ? 0.14f : 0.08f, heavy ? 0.16f : 0.1f, gun, metal);
        return new Vector3(0.22f, 1.25f, -0.8f);
    }

    // ---- Kessari: steel frames with brass plates and a cyan eye; cutters hover ----------------

    static Vector3 KessariFrame(Node3D model, Livery l, bool lancer)
    {
        Material steel = M(l.Main, 0.35f, 0.6f), dark = M(l.Dark, 0.4f, 0.5f), brass = M(l.Trim, 0.3f, 0.7f);
        Material eye = Palette.Glow(l.Glow, 4f);
        float bulk = lancer ? 1.2f : 1f;
        // Digitigrade struts: thigh back, shin forward.
        foreach (float x in new[] { -0.15f, 0.15f })
        {
            Box(model, x, 0.7f, 0.08f, 0.12f, 0.5f, 0.14f, dark);
            Box(model, x, 0.28f, -0.04f, 0.1f, 0.52f, 0.12f, steel);
            Box(model, x, 0.04f, -0.1f, 0.16f, 0.08f, 0.3f, dark);
        }
        Box(model, 0, 0.98f, 0, 0.42f, 0.16f, 0.26f, dark);                       // pelvis
        Box(model, 0, 1.28f, 0, 0.5f * bulk, 0.46f, 0.3f * bulk, steel);          // chassis
        Box(model, 0, 1.3f, -0.16f * bulk, 0.36f, 0.26f, 0.03f, brass);           // clan plate
        Box(model, -0.32f * bulk, 1.42f, 0, 0.12f, 0.36f, 0.16f, dark);           // arm
        Box(model, 0, 1.66f, -0.02f, 0.24f, 0.18f, 0.26f, steel);                 // head
        Box(model, 0, 1.68f, -0.155f, 0.2f, 0.04f, 0.02f, eye);
        Box(model, 0, 1.82f, 0.08f, 0.02f, 0.2f, 0.02f, brass);                   // antenna
        Box(model, 0, 1.3f, 0.2f * bulk, 0.3f, 0.3f, 0.12f, dark);                // power cell
        Box(model, 0, 1.3f, 0.27f * bulk, 0.18f, 0.08f, 0.02f, eye);
        if (lancer)
        {
            // A long rail lance over the right shoulder.
            Box(model, 0.3f, 1.55f, -0.35f, 0.1f, 0.12f, 1.5f, dark);
            Box(model, 0.3f, 1.55f, -1.1f, 0.06f, 0.06f, 0.1f, eye);
            return new Vector3(0.3f, 1.55f, -1.15f);
        }
        Box(model, 0.3f, 1.3f, -0.3f, 0.09f, 0.11f, 0.7f, dark);
        Box(model, 0.3f, 1.3f, -0.62f, 0.05f, 0.05f, 0.06f, eye);
        return new Vector3(0.3f, 1.3f, -0.68f);
    }

    /// <summary>A hovering Kessari cutter: a clan member built to burn through walls, not a remote drone.</summary>
    static Vector3 KessariCutter(Node3D model, Livery l)
    {
        Material steel = M(l.Main, 0.35f, 0.6f), dark = M(l.Dark, 0.4f, 0.5f), brass = M(l.Trim, 0.3f, 0.7f);
        Material eye = Palette.Glow(l.Glow, 4f);
        var hull = new Node3D { Name = "Hover", Position = new Vector3(0, 1.25f, 0) };
        model.AddChild(hull);
        Box(hull, 0, 0, 0, 0.62f, 0.3f, 0.62f, steel);
        var ring = Palette.Box(hull, Vector3.Zero, new Vector3(0.62f, 0.22f, 0.62f), brass);
        ring.Rotation = new Vector3(0, Mathf.Pi / 4, 0);
        Box(hull, 0, 0.22f, 0, 0.34f, 0.16f, 0.34f, dark);
        Box(hull, 0, 0.02f, -0.32f, 0.36f, 0.06f, 0.02f, eye);                    // eye band
        Box(hull, 0, -0.08f, -0.5f, 0.1f, 0.1f, 0.42f, dark);                     // cutter arm
        Box(hull, 0, -0.08f, -0.74f, 0.07f, 0.07f, 0.07f, Palette.Glow(l.Glow, 8f));
        Box(hull, 0, -0.24f, 0, 0.42f, 0.04f, 0.42f, Palette.Glow(l.Glow, 1.6f)); // lift glow
        return new Vector3(0, 1.17f, -0.8f);
    }

    // ---- Meridian Combine: white armour, navy suit, blue light; shield emitters ----------------

    static Vector3 MeridianOfficer(Node3D model, Livery l, string look)
    {
        Material white = M(l.Main, 0.35f), navy = M(l.Dark, 0.6f), blue = M(l.Trim, 0.5f), metal = M(l.Metal, 0.3f, 0.6f);
        bool warden = look == "warden";
        float bulk = warden ? 1.22f : 1f;
        Box(model, -0.13f, 0.43f, 0, 0.19f, 0.86f, 0.24f, navy);
        Box(model, 0.13f, 0.43f, 0, 0.19f, 0.86f, 0.24f, navy);
        Box(model, -0.13f, 0.5f, -0.13f, 0.16f, 0.22f, 0.04f, white);              // knee guards
        Box(model, 0.13f, 0.5f, -0.13f, 0.16f, 0.22f, 0.04f, white);
        Box(model, 0, 1.15f, 0, 0.5f * bulk, 0.6f, 0.3f * bulk, white);
        Box(model, 0, 1.1f, -0.16f * bulk, 0.5f * bulk, 0.08f, 0.02f, blue);        // corporate stripe
        Box(model, -0.31f * bulk, 1.38f, 0, 0.16f, 0.2f, 0.32f, white);
        Box(model, 0.31f * bulk, 1.38f, 0, 0.16f, 0.2f, 0.32f, white);
        Box(model, 0, 1.64f, 0, 0.3f, 0.3f, 0.32f, white);
        Box(model, 0, 1.64f, -0.165f, 0.26f, 0.12f, 0.02f, M(l.Black, 0.1f, 0.4f)); // visor
        Box(model, 0, 1.71f, -0.17f, 0.2f, 0.025f, 0.02f, Palette.Glow(l.Glow, 3f));
        Box(model, 0, 1.2f, 0.22f * bulk, 0.3f, 0.34f, 0.14f, navy);                // shield pack
        Box(model, 0, 1.36f, 0.3f * bulk, 0.18f, 0.06f, 0.03f, Palette.Glow(l.Glow, 4f));
        if (look == "breacher")
        {
            Palette.Box(model, new Vector3(0, 1.0f, -0.18f), new Vector3(0.34f, 0.16f, 0.08f), Palette.Textured(Palette.Hazard, new Vector3(2f, 1f, 1f)));
        }
        float gun = warden ? 1.05f : 0.75f;
        Box(model, 0.22f, 1.2f, -0.3f, warden ? 0.14f : 0.08f, warden ? 0.15f : 0.1f, gun, metal);
        Box(model, 0.22f, 1.27f, -0.25f, 0.05f, 0.04f, 0.2f, blue);
        return new Vector3(0.22f, 1.25f, -0.25f - gun);
    }

    // ---- Carrion Reavers: rust scrap armour, hazard paint, red goggles -------------------------

    static Vector3 Reaver(Node3D model, Livery l, string look)
    {
        Material rust = M(l.Main, 0.85f), dark = M(l.Dark, 0.9f), hazard = M(l.Trim, 0.7f), metal = M(l.Metal, 0.5f, 0.4f);
        Material eye = Palette.Glow(l.Glow, 4f);
        bool brute = look == "brute";
        float bulk = brute ? 1.25f : 1f;
        Box(model, -0.13f, 0.43f, 0, 0.2f, 0.86f, 0.25f, dark);
        Box(model, 0.13f, 0.43f, 0.02f, 0.2f, 0.86f, 0.25f, metal);                // mismatched legs
        Box(model, 0, 1.13f, 0, 0.5f * bulk, 0.58f, 0.3f * bulk, rust);
        Box(model, -0.08f, 1.2f, -0.16f * bulk, 0.3f, 0.07f, 0.02f, hazard);        // hazard slash
        Box(model, 0.06f, 1.08f, -0.16f * bulk, 0.3f, 0.07f, 0.02f, hazard);
        Box(model, -0.36f * bulk, 1.42f, 0, 0.24f, 0.24f, 0.4f, metal);             // one big pauldron
        Box(model, 0.3f * bulk, 1.36f, 0, 0.12f, 0.12f, 0.26f, dark);
        Box(model, 0, 1.62f, 0, 0.28f, 0.3f, 0.3f, dark);                           // scrap mask
        Box(model, -0.07f, 1.65f, -0.155f, 0.08f, 0.06f, 0.02f, eye);
        Box(model, 0.07f, 1.65f, -0.155f, 0.08f, 0.06f, 0.02f, eye);
        for (int i = 0; i < 4; i++)                                                 // spiked crest
            Box(model, 0, 1.8f + (i % 2) * 0.04f, -0.1f + i * 0.08f, 0.04f, 0.14f, 0.04f, hazard);
        Box(model, 0, 1.14f, 0.22f * bulk, 0.34f, 0.38f, 0.14f, dark);
        if (look == "wrecker")
            for (int i = -1; i <= 1; i++) Box(model, i * 0.14f, 0.86f, -0.17f, 0.1f, 0.14f, 0.08f, M(Palette.Orange));
        float gun = brute ? 1.1f : 0.65f;
        Box(model, 0.22f, 1.18f, -0.3f, brute ? 0.15f : 0.12f, brute ? 0.16f : 0.12f, gun, metal);
        return new Vector3(0.22f, 1.22f, -0.3f - gun);
    }

    // ---- Hollow Bloom: hunched swarm flesh, bile glow; corrupted crews overgrown ---------------

    static Vector3 Mutant(Node3D model, Livery l, bool burster)
    {
        Material flesh = M(l.Main, 0.55f), dark = M(l.Dark, 0.7f), pink = M(l.Trim, 0.45f);
        Material bile = Palette.Glow(l.Glow, 3.5f);
        Box(model, -0.16f, 0.32f, 0.05f, 0.16f, 0.64f, 0.2f, dark);
        Box(model, 0.16f, 0.32f, 0.05f, 0.16f, 0.64f, 0.2f, dark);
        var torso = new Node3D { Name = "Torso", Position = new Vector3(0, 0.95f, 0), Rotation = new Vector3(-0.5f, 0, 0) };
        model.AddChild(torso);
        float bulk = burster ? 1.3f : 1f;
        Box(torso, 0, 0.12f, 0, 0.5f * bulk, 0.55f, 0.36f * bulk, flesh);
        Box(torso, -0.2f, 0.3f, 0.12f, 0.12f, 0.12f, 0.12f, bile);                // pustules
        Box(torso, 0.18f, 0.12f, 0.18f, 0.1f, 0.1f, 0.1f, bile);
        Box(torso, 0, 0.5f, -0.12f, 0.28f, 0.26f, 0.3f, pink);                    // head, low and forward
        Box(torso, 0, 0.44f, -0.28f, 0.18f, 0.06f, 0.02f, bile);                  // maw
        if (burster)
        {
            var sac = Palette.Box(torso, new Vector3(0, 0.3f, 0.32f), new Vector3(0.62f, 0.6f, 0.5f), Palette.Glow(l.Glow, 2.2f));
            sac.Name = "Sac";
        }
        // Long arms that nearly reach the ground.
        Box(model, -0.36f, 0.75f, -0.15f, 0.12f, 0.75f, 0.14f, flesh);
        Box(model, 0.36f, 0.75f, -0.15f, 0.12f, 0.75f, 0.14f, flesh);
        Box(model, -0.36f, 0.35f, -0.24f, 0.16f, 0.1f, 0.24f, pink);
        Box(model, 0.36f, 0.35f, -0.24f, 0.16f, 0.1f, 0.24f, pink);
        return new Vector3(0, 1.3f, -0.55f);
    }

    static Vector3 Corrupted(Node3D model, Livery l)
    {
        Material suit = M(new Color("4a4e57"), 0.8f), dark = M(l.Black, 0.8f), growth = M(l.Main, 0.5f), pink = M(l.Trim, 0.45f);
        Material metal = M(Palette.Gunmetal, 0.4f, 0.5f);
        Box(model, -0.13f, 0.43f, 0, 0.19f, 0.86f, 0.24f, suit);
        Box(model, 0.13f, 0.43f, 0, 0.19f, 0.86f, 0.24f, dark);
        Box(model, 0, 1.13f, 0, 0.48f, 0.58f, 0.3f, suit);
        Box(model, 0, 1.2f, -0.16f, 0.2f, 0.12f, 0.02f, M(Palette.Orange)); // what's left of a ship patch
        Box(model, 0.14f, 1.3f, 0.06f, 0.34f, 0.36f, 0.34f, growth);        // the bloom bursting out of the shoulder
        Box(model, 0.24f, 1.5f, 0.0f, 0.18f, 0.18f, 0.18f, pink);
        Box(model, -0.12f, 0.75f, -0.12f, 0.16f, 0.2f, 0.06f, growth);
        Box(model, 0, 1.62f, 0, 0.3f, 0.3f, 0.32f, suit);                   // flight helmet
        Box(model, 0, 1.63f, -0.165f, 0.24f, 0.11f, 0.02f, Palette.Glow(l.Glow, 2.5f)); // cracked, glowing visor
        Box(model, 0.06f, 1.78f, 0.04f, 0.2f, 0.12f, 0.2f, growth);
        Box(model, 0.22f, 1.2f, -0.3f, 0.08f, 0.1f, 0.8f, metal);
        return new Vector3(0.22f, 1.25f, -0.8f);
    }

    // ---- Vehicle crew ----------------------------------------------------------------------------

    /// <summary>A seated crew member (driver, pilot or gunner) in the faction's colours, about 0.85 m tall.</summary>
    public static void Crew(Node3D figure, string faction, Livery l)
    {
        Material body, plate, head;
        switch (faction)
        {
            case "kessari":
                body = M(l.Main, 0.35f, 0.6f); plate = M(l.Trim, 0.3f, 0.7f); head = M(l.Dark, 0.4f, 0.5f); break;
            case "meridian":
                body = M(l.Main, 0.35f); plate = M(l.Trim, 0.5f); head = M(l.Main, 0.35f); break;
            case "reavers":
                body = M(l.Main, 0.85f); plate = M(l.Trim, 0.7f); head = M(l.Dark, 0.9f); break;
            case "hollow":
                body = M(new Color("4a4e57"), 0.8f); plate = M(l.Main, 0.5f); head = M(new Color("4a4e57"), 0.8f); break;
            default:
                body = M(l.Main, 0.6f); plate = M(l.Trim, 0.5f); head = M(l.Trim, 0.5f); break;
        }
        Box(figure, 0, 0.25f, 0, 0.52f, 0.5f, 0.34f, body);
        Box(figure, 0, 0.36f, -0.18f, 0.42f, 0.26f, 0.04f, plate);
        Box(figure, 0, 0.66f, 0, 0.3f, 0.32f, 0.32f, head);
        Box(figure, 0, 0.68f, -0.165f, 0.26f, 0.07f, 0.02f, Palette.Glow(l.Glow, 3f));
    }
}
