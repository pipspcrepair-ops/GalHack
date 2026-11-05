using System;
using System.Collections.Generic;
using GalHack.Structures;
using Godot;
using Structure = GalHack.Structures.Structure;

namespace Slice.Construction;

/// <summary>
/// The build supports a blueprint stands on while it goes up: hazard-striped struts from the
/// ground to the underside of every second column of the plan, so the ghost and the blocks
/// already placed rest on a cradle instead of hanging at landing-gear height. They go when the
/// vessel is finished and stands on its own legs. Presentation only; one multimesh.
/// </summary>
public partial class BuildCradle : MultiMeshInstance3D
{
    const float StrutWidth = 0.14f;
    readonly List<Transform3D> _struts = new();

    public override void _Ready()
    {
        var mesh = new BoxMesh { Size = new Vector3(StrutWidth, 1f, StrutWidth) };
        mesh.Material = new StandardMaterial3D
        {
            AlbedoColor = Colors.White, Roughness = 0.6f, Metallic = 0.2f,
            AlbedoTexture = Stripes(),
            Uv1Scale = new Vector3(1f, 4f, 1f),
        };
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh };
        TopLevel = true;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        CastShadow = ShadowCastingSetting.On;
        Visible = false;
    }

    /// <summary>
    /// Stands the cradle under a plan placed at a frame: one strut under every second column,
    /// from the ground (as <paramref name="groundAt"/> gives it) up to the column's lowest block.
    /// </summary>
    public void Show(Structure plan, Transform3D frame, Func<float, float, float> groundAt)
    {
        var lowest = new Dictionary<(int, int), int>();
        foreach (var (p, _) in plan.Blocks())
            if (!lowest.TryGetValue((p.X, p.Z), out int y) || p.Y < y) lowest[(p.X, p.Z)] = p.Y;
        float cell = plan.Grid.CellSize;
        _struts.Clear();
        foreach (var ((x, z), y) in lowest)
        {
            if ((x & 1) != 0 || (z & 1) != 0) continue;
            Vector3 top = frame * (new Vector3(x, y - 0.5f, z) * cell);
            float ground = groundAt(top.X, top.Z) - 0.3f; // sunk a little into the soil
            float length = top.Y - ground;
            if (length < 0.05f) continue;
            _struts.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(1f, length, 1f)), new Vector3(top.X, ground + length * 0.5f, top.Z)));
        }
        Multimesh.InstanceCount = _struts.Count;
        for (int i = 0; i < _struts.Count; i++) Multimesh.SetInstanceTransform(i, _struts[i]);
        Visible = _struts.Count > 0;
    }

    public void Clear() => Visible = false;

    /// <summary>Hazard bands: yellow and charcoal, a few to a metre of strut.</summary>
    static ImageTexture Stripes()
    {
        var image = Image.CreateEmpty(4, 16, false, Image.Format.Rgb8);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 4; x++)
                image.SetPixel(x, y, (y / 4) % 2 == 0 ? new Color("f4a100") : new Color("2b2d42"));
        return ImageTexture.CreateFromImage(image);
    }
}
