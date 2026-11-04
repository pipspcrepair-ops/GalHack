using System.Collections.Generic;
using Godot;

namespace Boarding;

/// <summary>
/// A Vharn strongbox. Hold E for a moment to crack it and take what's inside. Every
/// boarding pays out: the vault's caches are the big prize, smaller ones sit in the holds.
/// </summary>
public partial class LootCache : Block
{
    public Dictionary<string, int> Contents = new();
    public bool Looted { get; private set; }

    Node3D _lid;
    MeshInstance3D _strip;

    protected override void BuildVisual()
    {
        bool big = Kind == BlockKind.Loot && Size.Y > 1f;
        var body = Art.Flat(Palette.Oxblood, 0.6f, 0.3f);
        AddChild(Art.Mesh(Art.BoxMesh(Size - new Vector3(0, 0.15f, 0)), body, Vector3.Down * 0.075f));
        AddChild(Art.Mesh(Art.BoxMesh(new Vector3(Size.X + 0.04f, 0.12f, Size.Z + 0.04f)), Art.Flat(Palette.Yellow, 0.5f, 0.4f),
            new Vector3(0, -Size.Y * 0.5f + 0.25f, 0)));
        _lid = new Node3D { Position = new Vector3(0, Size.Y * 0.5f - 0.075f, -Size.Z * 0.5f) };
        AddChild(_lid);
        _lid.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(Size.X, 0.15f, Size.Z)), Art.Checker(Palette.Dark, Palette.Yellow, 0.2f),
            new Vector3(0, 0, Size.Z * 0.5f)));
        _strip = Art.Mesh(Art.BoxMesh(new Vector3(Size.X * 0.7f, 0.08f, 0.04f)), Art.Glow(big ? Palette.Yellow : Palette.TealGlow, 3f),
            new Vector3(0, 0.05f, Size.Z * 0.5f + 0.02f), false);
        AddChild(_strip);
    }

    /// <summary>Opens the cache and hands over its contents (once).</summary>
    public Dictionary<string, int> Open()
    {
        if (Looted) return new Dictionary<string, int>();
        Looted = true;
        _lid.RotationDegrees = new Vector3(-100f, 0, 0);
        _strip.MaterialOverride = Art.Flat(Palette.Dark);
        return Contents;
    }
}
