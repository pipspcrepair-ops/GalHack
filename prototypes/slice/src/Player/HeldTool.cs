using System.Collections.Generic;
using GalHack.Items;
using Godot;
using Slice.Items;

namespace Slice.Player;

/// <summary>
/// The hand tool's model: body, grip, barrel, emitter ring, the top light, plates and a muzzle
/// shroud, each a worn, mottled finish (<see cref="HeldFinish"/>), with translucent lit lenses.
/// The first-person hands hold exactly this and the inventory icons render exactly this, because
/// both ask <see cref="ItemModels"/>, which asks here. Its parts sit where the glove holds them
/// (the grip at about 0.17, -0.21, -0.33 in the hands' frame) and the barrel points down -Z.
///
/// The survival tool, the mining drill and the plasma drill are the one tool at three levels, so they
/// are one model in three liveries: the plainest is cream, and each step up takes more of its
/// rarity's colour (the one the bag draws round the slot) into its hull and plates.
/// </summary>
public static class HeldTool
{
    /// <summary>The model for a tool item (null: the plainest, as a block or a core in the hands shows).</summary>
    public static ItemModel Build(ItemDef item)
    {
        Color hull = new("d8cfb6"), plate = new("c4baa0"), grip = new("2a3550");
        if (item != null && item.Rarity != Rarity.Common)
        {
            var mark = new Color(Rarities.Color(item.Rarity) << 8 | 0xff);
            hull = hull.Lerp(mark, 0.18f);
            plate = plate.Lerp(mark, 0.5f);
            grip = grip.Lerp(mark.Darkened(0.6f), 0.35f);
        }
        var root = new Node3D { Name = "Tool" };
        var lenses = new List<ShaderMaterial>();
        void Add(Mesh mesh, Material material, Vector3 at, Vector3 degrees) =>
            root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at, RotationDegrees = degrees });
        void Worn(Mesh mesh, Color c, Vector3 at, Vector3 deg, float rough, float metal, float seed) =>
            Add(mesh, HeldFinish.Make(mesh, c, 0.65f, rough, metal, seed), at, deg);
        ShaderMaterial Lens(Mesh mesh)
        {
            ShaderMaterial m = HeldFinish.Emitter(mesh, new Color("2ec4b6"));
            lenses.Add(m);
            return m;
        }

        // The tool: body, grip, barrel, emitter ring; each a worn, mottled finish (HeldFinish), with
        // a few proud plates and a muzzle shroud for panel lines. The glowing panel stays clean.
        Worn(new BoxMesh { Size = new Vector3(0.08f, 0.09f, 0.26f) }, hull, new Vector3(0.17f, -0.15f, -0.38f), Vector3.Zero, 0.5f, 0.3f, 1f);
        Worn(new BoxMesh { Size = new Vector3(0.05f, 0.11f, 0.05f) }, grip, new Vector3(0.17f, -0.21f, -0.33f), new Vector3(15, 0, 0), 0.7f, 0.05f, 2f);
        Worn(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.026f, Height = 0.14f, RadialSegments = 8 }, grip, new Vector3(0.17f, -0.145f, -0.55f + 0.07f), new Vector3(90, 0, 0), 0.5f, 0.3f, 3f);
        // The muzzle emitter: a dark worn bezel ring, a round translucent lens set back inside it, and a glowing core behind.
        var tipLens = new CylinderMesh { TopRadius = 0.021f, BottomRadius = 0.021f, Height = 0.004f, RadialSegments = 12 };
        Worn(new TorusMesh { InnerRadius = 0.02f, OuterRadius = 0.034f, Rings = 12, RingSegments = 6 }, grip, new Vector3(0.17f, -0.145f, -0.545f), new Vector3(90, 0, 0), 0.5f, 0.3f, 9f);
        Add(tipLens, Lens(tipLens), new Vector3(0.17f, -0.145f, -0.5485f), new Vector3(90, 0, 0));
        Add(new CylinderMesh { TopRadius = 0.011f, BottomRadius = 0.011f, Height = 0.004f, RadialSegments = 8 }, Lens(new CylinderMesh { TopRadius = 0.011f, Height = 0.004f }), new Vector3(0.17f, -0.145f, -0.5435f), new Vector3(90, 0, 0));
        // The top light: a bezel frame round a recessed translucent lens, with a bright core beneath.
        var topLens = new BoxMesh { Size = new Vector3(0.028f, 0.004f, 0.098f) };
        Add(topLens, Lens(topLens), new Vector3(0.17f, -0.1035f, -0.38f), Vector3.Zero);
        Add(new BoxMesh { Size = new Vector3(0.016f, 0.004f, 0.07f) }, Lens(new BoxMesh { Size = new Vector3(0.016f, 0.004f, 0.07f) }), new Vector3(0.17f, -0.1085f, -0.38f), Vector3.Zero);
        foreach (float side in new[] { -1f, 1f })
        {
            Worn(new BoxMesh { Size = new Vector3(0.007f, 0.012f, 0.118f) }, grip, new Vector3(0.17f + side * 0.0185f, -0.1005f, -0.38f), Vector3.Zero, 0.6f, 0.1f, 10f + side);
            Worn(new BoxMesh { Size = new Vector3(0.044f, 0.012f, 0.007f) }, grip, new Vector3(0.17f, -0.1005f, -0.38f + side * 0.0555f), Vector3.Zero, 0.6f, 0.1f, 12f + side);
        }
        // Side plates, a rear cap, and a shroud round the barrel root.
        foreach (float side in new[] { -1f, 1f })
            Worn(new BoxMesh { Size = new Vector3(0.006f, 0.062f, 0.15f) }, plate, new Vector3(0.17f + side * 0.0405f, -0.15f, -0.4f), Vector3.Zero, 0.55f, 0.3f, 5f + side);
        Worn(new BoxMesh { Size = new Vector3(0.084f, 0.094f, 0.02f) }, plate, new Vector3(0.17f, -0.15f, -0.255f), Vector3.Zero, 0.55f, 0.3f, 7f);
        Worn(new BoxMesh { Size = new Vector3(0.058f, 0.058f, 0.05f) }, plate, new Vector3(0.17f, -0.145f, -0.52f), Vector3.Zero, 0.5f, 0.35f, 8f);
        // The barrel's tip (the barrel is 0.14 long, centred at z -0.48): where flashes and bolts begin.
        var tip = new Node3D { Name = "BarrelTip", Position = new Vector3(0.17f, -0.145f, -0.56f) };
        root.AddChild(tip);
        return new ItemModel { Root = root, Tip = tip, Lenses = lenses };
    }
}
