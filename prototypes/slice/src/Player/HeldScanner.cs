using System.Collections.Generic;
using GalHack.Items;
using Godot;
using Slice.Items;

namespace Slice.Player;

/// <summary>
/// The handheld scanner's model: a slim housing with a lit screen on top, a grip the glove holds
/// (where the tool's is), and a neck out front ending in a round sensor coil whose lens glows. The same
/// worn finish and translucent lenses as the tool, in a cooler grey; it points down -Z. The hands
/// hold it and the inventory icon renders it, both through <see cref="ItemModels"/>.
/// </summary>
public static class HeldScanner
{
    public static ItemModel Build(ItemDef item) => Build(item, false);

    /// <summary>Forked: two prongs and a ring of lit beads round the coil, for the core scanner.</summary>
    public static ItemModel Build(ItemDef item, bool forked)
    {
        Color hull = new("b9c6c4"), plate = new("9aaaa8"), grip = new("2a3550");
        var root = new Node3D { Name = "Scanner" };
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

        // Housing, the grip in the glove, and a rear cap.
        Worn(new BoxMesh { Size = new Vector3(0.08f, 0.06f, 0.2f) }, hull, new Vector3(0.17f, -0.15f, -0.4f), Vector3.Zero, 0.5f, 0.3f, 21f);
        Worn(new BoxMesh { Size = new Vector3(0.05f, 0.11f, 0.05f) }, grip, new Vector3(0.17f, -0.21f, -0.33f), new Vector3(15, 0, 0), 0.7f, 0.05f, 22f);
        Worn(new BoxMesh { Size = new Vector3(0.084f, 0.07f, 0.02f) }, plate, new Vector3(0.17f, -0.15f, -0.305f), Vector3.Zero, 0.55f, 0.3f, 23f);
        foreach (float side in new[] { -1f, 1f })
            Worn(new BoxMesh { Size = new Vector3(0.006f, 0.04f, 0.12f) }, plate, new Vector3(0.17f + side * 0.0430f, -0.15f, -0.41f), Vector3.Zero, 0.55f, 0.3f, 24f + side);
        // The screen on top: a bezel frame round a recessed lens, a bright core beneath.
        var screen = new BoxMesh { Size = new Vector3(0.044f, 0.004f, 0.1f) };
        Add(screen, Lens(screen), new Vector3(0.17f, -0.1185f, -0.4f), Vector3.Zero);
        Add(new BoxMesh { Size = new Vector3(0.03f, 0.004f, 0.076f) }, Lens(new BoxMesh { Size = new Vector3(0.03f, 0.004f, 0.076f) }), new Vector3(0.17f, -0.1235f, -0.4f), Vector3.Zero);
        foreach (float side in new[] { -1f, 1f })
        {
            Worn(new BoxMesh { Size = new Vector3(0.007f, 0.012f, 0.11f) }, grip, new Vector3(0.17f + side * 0.0255f, -0.1155f, -0.4f), Vector3.Zero, 0.6f, 0.1f, 30f + side);
            Worn(new BoxMesh { Size = new Vector3(0.058f, 0.012f, 0.007f) }, grip, new Vector3(0.17f, -0.1155f, -0.4f + side * 0.0585f), Vector3.Zero, 0.6f, 0.1f, 32f + side);
        }
        // The neck and the sensor coil: a dark worn ring round a round translucent lens, a core behind it.
        Worn(new CylinderMesh { TopRadius = 0.014f, BottomRadius = 0.016f, Height = 0.12f, RadialSegments = 8 }, grip, new Vector3(0.17f, -0.15f, -0.56f), new Vector3(90, 0, 0), 0.5f, 0.3f, 25f);
        Worn(new TorusMesh { InnerRadius = 0.034f, OuterRadius = 0.05f, Rings = 14, RingSegments = 6 }, grip, new Vector3(0.17f, -0.15f, -0.625f), new Vector3(90, 0, 0), 0.5f, 0.3f, 26f);
        var coil = new CylinderMesh { TopRadius = 0.036f, BottomRadius = 0.036f, Height = 0.004f, RadialSegments = 14 };
        Add(coil, Lens(coil), new Vector3(0.17f, -0.15f, -0.6275f), new Vector3(90, 0, 0));
        Add(new CylinderMesh { TopRadius = 0.016f, BottomRadius = 0.016f, Height = 0.004f, RadialSegments = 10 }, Lens(new CylinderMesh { TopRadius = 0.016f, Height = 0.004f }), new Vector3(0.17f, -0.15f, -0.6215f), new Vector3(90, 0, 0));
        // A short mast on the back, its tip lit.
        Worn(new CylinderMesh { TopRadius = 0.005f, BottomRadius = 0.006f, Height = 0.075f, RadialSegments = 6 }, grip, new Vector3(0.145f, -0.093f, -0.312f), new Vector3(0, 0, 8), 0.5f, 0.4f, 27f);
        var bead = new BoxMesh { Size = new Vector3(0.014f, 0.014f, 0.014f) };
        Add(bead, Lens(bead), new Vector3(0.1385f, -0.0545f, -0.312f), new Vector3(0, 0, 8));
        if (forked)
            foreach (float side in new[] { -1f, 1f })
            {
                Worn(new BoxMesh { Size = new Vector3(0.012f, 0.012f, 0.16f) }, grip, new Vector3(0.17f + side * 0.06f, -0.15f, -0.56f), new Vector3(0, side * 8f, 0), 0.5f, 0.3f, 40f + side);
                var tipBead = new BoxMesh { Size = new Vector3(0.016f, 0.016f, 0.016f) };
                Add(tipBead, Lens(tipBead), new Vector3(0.17f + side * 0.0675f, -0.15f, -0.64f), Vector3.Zero);
            }
        // Where its ping would leave: the middle of the coil.
        var tip = new Node3D { Name = "SensorTip", Position = new Vector3(0.17f, -0.15f, -0.64f) };
        root.AddChild(tip);
        return new ItemModel { Root = root, Tip = tip, Lenses = lenses };
    }
}
