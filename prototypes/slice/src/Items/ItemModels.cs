using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using Godot;
using Slice.Player;
using Slice.Vehicles;
using Slice.Vessels;

namespace Slice.Items;

/// <summary>Where an item's real model comes from: a hand-held item's, a vehicle's, or a device set down in the world.</summary>
public enum ModelSource { Held, Vehicle, Placed }

/// <summary>
/// An item's real model, built fresh each time. Its front is -Z and its up is +Y, as everything
/// held or driven in the game is. A held model may also carry a muzzle (<see cref="Tip"/>, where
/// shots leave) and its lit lenses (<see cref="Lenses"/>, tinted by mode); the icons ignore both.
/// </summary>
public sealed class ItemModel
{
    public Node3D Root { get; init; }
    public Node3D Tip { get; init; }
    public IReadOnlyList<ShaderMaterial> Lenses { get; init; } = Array.Empty<ShaderMaterial>();
    public ModelSource Source { get; internal set; }
}

/// <summary>
/// The one place that says which items have a real model of their own, and builds it. The hands
/// show an item's model from here and the inventory icons render it from here, so the two are the
/// same model built by the same code and a new item can never be in your hand and in the bag as
/// two different things. Register a model once, for one item or for a rule ("every weapon") and
/// both the hands and the icon follow; an item nobody registers has only a stand-in icon.
/// </summary>
public static class ItemModels
{
    sealed record Entry(Func<ItemDef, bool> Matches, Func<ItemDef, ItemModel> Build, ModelSource Source);

    static readonly List<Entry> Entries = new();

    static ItemModels()
    {
        // The hand tools are one model in three liveries (by rarity); the survival tool is the plainest.
        Register(i => Array.IndexOf(PlayerInventory.ToolItems, i.Id) >= 0, HeldTool.Build, ModelSource.Held);
        Register("scanner", HeldScanner.Build, ModelSource.Held);
        Register("hover-bike", _ => new ItemModel { Root = BikeModel.Build() }, ModelSource.Vehicle);
        // Devices are the model they have once set down (the survival constructor stands for every carried machine).
        Register("survival-constructor", _ => Device.Model(climate: false), ModelSource.Placed);
        Register(Device.ClimateItem, _ => Device.Model(climate: true), ModelSource.Placed);
    }

    /// <summary>Gives every item the rule matches this model (first registration to match wins).</summary>
    public static void Register(Func<ItemDef, bool> matches, Func<ItemDef, ItemModel> build, ModelSource source) =>
        Entries.Add(new Entry(matches, build, source));

    /// <summary>Gives one item this model.</summary>
    public static void Register(string itemId, Func<ItemDef, ItemModel> build, ModelSource source) =>
        Register(i => i.Id == itemId, build, source);

    /// <summary>Does this item have a real model of its own, and from where?</summary>
    public static bool Has(ItemDef item, out ModelSource source)
    {
        Entry e = item == null ? null : Entries.FirstOrDefault(x => x.Matches(item));
        source = e?.Source ?? default;
        return e != null;
    }

    /// <summary>Does the item have a model for the hands to hold?</summary>
    public static bool IsHeld(ItemDef item) => Has(item, out ModelSource s) && s == ModelSource.Held;

    /// <summary>A fresh copy of the item's model, or null when it has none.</summary>
    public static ItemModel Build(ItemDef item)
    {
        Entry e = item == null ? null : Entries.FirstOrDefault(x => x.Matches(item));
        if (e == null) return null;
        ItemModel m = e.Build(item);
        if (m != null) m.Source = e.Source;
        return m;
    }

    /// <summary>
    /// What a model is made of, as text: each mesh's kind, shape and place, in order. Two models with
    /// the same signature are the same model (tests compare the one in the hand with the one in the bag).
    /// </summary>
    public static string Signature(Node3D root)
    {
        var parts = new List<string>();
        void Walk(Node n, Transform3D parent)
        {
            Transform3D t = n is Node3D d ? parent * d.Transform : parent;
            if (n is MeshInstance3D m && m.Mesh != null)
            {
                Aabb a = m.Mesh.GetAabb();
                Vector3 at = t.Origin;
                parts.Add($"{m.Mesh.GetClass()} {a.Size.X:0.####},{a.Size.Y:0.####},{a.Size.Z:0.####} @{at.X:0.####},{at.Y:0.####},{at.Z:0.####} r{t.Basis.GetEuler().X:0.###},{t.Basis.GetEuler().Y:0.###},{t.Basis.GetEuler().Z:0.###} {(m.MaterialOverride as ShaderMaterial)?.Shader != null}");
            }
            foreach (Node c in n.GetChildren()) Walk(c, t);
        }
        Walk(root, Transform3D.Identity);
        return string.Join("\n", parts);
    }
}
