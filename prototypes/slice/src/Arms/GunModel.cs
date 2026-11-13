using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GalHack.Arms;
using GalHack.Items;
using Godot;
using Slice.Items;
using Slice.Player;

namespace Slice.Arms;

/// <summary>
/// A gun's model in the engine: each part of its generated look (shared/Arms GunLook) as a box,
/// cylinder, sphere, torus or capsule with the held-item finish (HeldFinish: mottled wear, grime
/// in the creases, worn edges) or a glowing emitter lens. No assets. The same node is held in
/// first person and rendered once for the inventory icon: every weapon item is registered with
/// <see cref="ItemModels"/> as <see cref="Held"/>, so the hands and the icons build the same thing.
/// </summary>
public static class GunModel
{
    /// <summary>Where a gun sits in the glove: its grip's top in the hand, a little smaller than modelled so the view stays open.</summary>
    static readonly Vector3 GripAt = new(0.19f, -0.175f, -0.36f);
    const float GunScale = 0.62f;

    static Armoury _rows;
    /// <summary>The weapon rows (shared/Arms/data/weapons.json), read once.</summary>
    public static Armoury Rows => _rows ??= Armoury.LoadFile(Slice.Game.GameData.SharedPath("Arms", "data", "weapons.json"));

    static readonly ConditionalWeakTable<Node3D, Built> _byRoot = new();

    /// <summary>A weapon item's held model: the gun set in the glove, its muzzle the shots' start.</summary>
    public static ItemModel Held(ItemDef item)
    {
        WeaponDef def = Rows.OfItem(item?.Id);
        if (def == null) return null;
        Built b = Build(def.Base ?? def);
        var root = new Node3D { Name = "Gun", Position = GripAt, Scale = Vector3.One * GunScale, RotationDegrees = new Vector3(0f, 2.5f, 0f) };
        root.AddChild(b.Root);
        _byRoot.AddOrUpdate(root, b);
        return new ItemModel { Root = root, Tip = b.Muzzle };
    }

    /// <summary>The gun inside a held model's root, or null.</summary>
    public static Built Of(Node3D root) => root != null && _byRoot.TryGetValue(root, out Built b) ? b : null;

    /// <summary>A weapon's model hung on someone else's hand or mount (a node named "Weapon" under it, placed there).</summary>
    public static Built Hang(Node3D on, WeaponDef def, Transform3D at)
    {
        Built b = Build(def);
        var hold = new Node3D { Name = "Weapon", Transform = at };
        hold.AddChild(b.Root);
        on.AddChild(hold);
        _byRoot.AddOrUpdate(hold, b);
        return b;
    }

    /// <summary>A held gun's life in the hand: charge rings lit by stage, and a reload dips it out of line and back.</summary>
    public static void Animate(Node3D root, Gun gun, float dt)
    {
        Built b = Of(root);
        if (b == null) return;
        if (gun != null && gun.Base.Id != b.Id) gun = null;
        if (b.Rings.Count > 0) LightRings(b, gun != null && gun.Charging ? Math.Max(1, gun.Stage) : 0);
        b.Dip = Mathf.MoveToward(b.Dip, gun != null && gun.Reloading ? 1f : 0f, dt * 5f);
        b.Root.RotationDegrees = new Vector3(-28f * b.Dip, 0f, 12f * b.Dip);
        b.Root.Position = new Vector3(0f, -0.08f * b.Dip, 0f);
    }

    /// <summary>The built model: its root, the muzzle node (flashes and shots start there), and its emitters by role.</summary>
    public sealed class Built
    {
        public Node3D Root;
        public Node3D Muzzle;
        /// <summary>The weapon row's id, and how far a reload has dipped it (0..1).</summary>
        public string Id;
        public float Dip;
        public GunModelSpec Spec;
        /// <summary>Charge rings, one per stage, lit as it charges.</summary>
        public readonly List<ShaderMaterial> Rings = new();
        /// <summary>Every glowing part (dimmed while reloading, pulsing on a living gun).</summary>
        public readonly List<ShaderMaterial> Glows = new();
    }

    static readonly StringName Glow = "glow";

    public static Built Build(WeaponDef def)
    {
        if (def.Delivery == Delivery.Strike) return Hand(def);
        GunModelSpec spec = GunLook.Build(def);
        var b = new Built { Root = new Node3D { Name = def.Id }, Spec = spec, Id = def.Id };
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

    /// <summary>
    /// A hand weapon's model, in the gun's frame (the grip at the origin, the business end along -Z,
    /// up +Y): its look's stock names the head (club, blade, spear; a fist has none), its body the
    /// haft. The "muzzle" is the striking end.
    /// </summary>
    static Built Hand(WeaponDef def)
    {
        LookSpec l = def.Look;
        var b = new Built { Root = new Node3D { Name = def.Id }, Id = def.Id };
        Color wood = ColourOf(l.Accent), steel = new(0.55f, 0.56f, 0.58f), dark = new(0.2f, 0.2f, 0.21f);
        float len = MathF.Max(0.05f, l.Body);
        int n = 0;
        void Part(Mesh mesh, Vector3 at, Vector3 turn, Color c, float metal)
        {
            var mi = new MeshInstance3D
            {
                Mesh = mesh, MaterialOverride = HeldFinish.Make(mesh, c, 0.5f, 0.7f, metal, n++ * 1.37f),
                Position = at, RotationDegrees = turn, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            b.Root.AddChild(mi);
        }
        Vector3 tip = Vector3.Zero;
        switch (l.Stock)
        {
            case "club":
                Part(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.05f, Height = len + 0.14f, RadialSegments = 10, Rings = 1 }, new Vector3(0f, 0f, 0.07f - (len + 0.14f) * 0.5f), new Vector3(-90f, 0f, 0f), wood, 0f);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * MathF.PI * 0.5f + 0.4f;
                    var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
                    Part(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.014f, Height = 0.07f, RadialSegments = 5, Rings = 1 }, new Vector3(0f, 0f, -len * (0.7f + i * 0.06f)) + dir * 0.06f, new Vector3(0f, 0f, Mathf.RadToDeg(a) - 90f), dark, 0.6f);
                }
                tip = new Vector3(0f, 0f, -len);
                break;
            case "blade":
                Part(new CylinderMesh { TopRadius = 0.017f, BottomRadius = 0.017f, Height = 0.13f, RadialSegments = 8, Rings = 1 }, new Vector3(0f, 0f, 0.04f), new Vector3(-90f, 0f, 0f), wood, 0f);
                Part(new BoxMesh { Size = new Vector3(0.03f, 0.075f, 0.012f) }, new Vector3(0f, 0.004f, -0.025f), Vector3.Zero, dark, 0.6f); // the guard between grip and blade
                Part(new BoxMesh { Size = new Vector3(0.008f, 0.05f, len * 0.8f) }, new Vector3(0f, 0.004f, -0.025f - len * 0.4f), Vector3.Zero, steel, 0.8f);
                Part(new BoxMesh { Size = new Vector3(0.008f, 0.065f, len * 0.16f) }, new Vector3(0f, 0.008f, -0.025f - len * 0.88f), Vector3.Zero, steel, 0.8f);
                tip = new Vector3(0f, 0f, -0.025f - len);
                break;
            case "spear":
                Part(new CylinderMesh { TopRadius = 0.016f, BottomRadius = 0.017f, Height = len, RadialSegments = 8, Rings = 1 }, new Vector3(0f, 0f, 0.15f - len * 0.5f), new Vector3(-90f, 0f, 0f), wood, 0f);
                Part(new CylinderMesh { TopRadius = 0.034f, BottomRadius = 0.034f, Height = 0.07f, RadialSegments = 8, Rings = 1 }, new Vector3(0f, 0f, 0.15f - len), new Vector3(-90f, 0f, 0f), dark, 0.2f);
                Part(new BoxMesh { Size = new Vector3(0.012f, 0.05f, 0.22f) }, new Vector3(0f, 0f, 0.15f - len - 0.13f), Vector3.Zero, steel, 0.8f);
                Part(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 0.04f, RadialSegments = 8, Rings = 1 }, new Vector3(0f, 0f, 0.15f), new Vector3(-90f, 0f, 0f), dark, 0.6f); // the butt's cap
                tip = new Vector3(0f, 0f, 0.15f - len - 0.24f);
                break;
        }
        b.Muzzle = new Node3D { Name = "Muzzle", Position = tip };
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
