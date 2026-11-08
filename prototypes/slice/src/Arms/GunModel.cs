using System.Collections.Generic;
using GalHack.Arms;
using Godot;
using Slice.Player;

namespace Slice.Arms;

/// <summary>
/// A gun's model in the engine: each part of its generated look (shared/Arms GunLook) as a box,
/// cylinder, sphere, torus or capsule with the held-item finish (HeldFinish: mottled wear, grime
/// in the creases, worn edges) or a glowing emitter lens. No assets. The same node is held in
/// first person and rendered once for the inventory icon.
/// </summary>
public static class GunModel
{
    /// <summary>The built model: its root, the muzzle node (flashes and shots start there), and its emitters by role.</summary>
    public sealed class Built
    {
        public Node3D Root;
        public Node3D Muzzle;
        public GunModelSpec Spec;
        /// <summary>Charge rings, one per stage, lit as it charges.</summary>
        public readonly List<ShaderMaterial> Rings = new();
        /// <summary>Every glowing part (dimmed while reloading, pulsing on a living gun).</summary>
        public readonly List<ShaderMaterial> Glows = new();
    }

    static readonly StringName Glow = "glow";

    public static Built Build(WeaponDef def)
    {
        GunModelSpec spec = GunLook.Build(def);
        var b = new Built { Root = new Node3D { Name = def.Id }, Spec = spec };
        foreach (LookPart p in spec.Parts)
        {
            Mesh mesh = MeshOf(p);
            Color c = ColourOf(p.Colour);
            Material mat;
            if (p.Finish == Finish.Emitter)
            {
                var e = HeldFinish.Emitter(mesh is TorusMesh ? new CylinderMesh { TopRadius = p.Size.Y, Height = 0.004f } : mesh, c);
                if (p.Role == Role.Ring) { b.Rings.Add(e); e.SetShaderParameter(Glow, 0.15f); }
                b.Glows.Add(e);
                mat = e;
            }
            else mat = HeldFinish.Make(mesh, c, p.Wear, p.Rough, p.Metal, p.Index * 1.37f + (int)p.Role * 3.1f);
            var mi = new MeshInstance3D
            {
                Mesh = mesh, MaterialOverride = mat, Position = new Vector3(p.At.X, p.At.Y, p.At.Z),
                RotationDegrees = new Vector3(p.Turn.X, p.Turn.Y, p.Turn.Z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            b.Root.AddChild(mi);
        }
        b.Muzzle = new Node3D { Name = "Muzzle", Position = new Vector3(spec.Muzzle.X, spec.Muzzle.Y, spec.Muzzle.Z) };
        b.Root.AddChild(b.Muzzle);
        return b;
    }

    /// <summary>Lights the charge rings up to a stage (0: all dim).</summary>
    public static void LightRings(Built b, int stage)
    {
        for (int i = 0; i < b.Rings.Count; i++) b.Rings[i].SetShaderParameter(Glow, i < stage ? 1.6f : 0.15f);
    }

    /// <summary>Every glowing part at a level (1 normal).</summary>
    public static void SetGlow(Built b, float level, int skipRings = 1)
    {
        foreach (ShaderMaterial m in b.Glows)
            if (skipRings == 0 || !b.Rings.Contains(m)) m.SetShaderParameter(Glow, level);
    }

    static Mesh MeshOf(LookPart p) => p.Shape switch
    {
        PartShape.Box => new BoxMesh { Size = new Vector3(p.Size.X, p.Size.Y, p.Size.Z) },
        PartShape.Cylinder => new CylinderMesh { TopRadius = p.Size.X, Height = p.Size.Y, BottomRadius = p.Size.Z > 0f ? p.Size.Z : p.Size.X, RadialSegments = 14, Rings = 1 },
        PartShape.Sphere => new SphereMesh { Radius = p.Size.X, Height = p.Size.Y, RadialSegments = 14, Rings = 7 },
        PartShape.Torus => new TorusMesh { InnerRadius = p.Size.X, OuterRadius = p.Size.Y, Rings = 14, RingSegments = 6 },
        _ => new CapsuleMesh { Radius = p.Size.X, Height = Mathf.Max(p.Size.Y, p.Size.X * 2.01f), RadialSegments = 14, Rings = 4 },
    };

    public static Color ColourOf(uint rgb) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f);
}
