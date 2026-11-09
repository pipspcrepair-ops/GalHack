using System;
using System.Collections.Generic;
using GalHack.Deco;
using GalHack.Items;
using Godot;
using PoiGenerator.Gen;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Places;

/// <summary>
/// A place's <see cref="PartSet"/>: everything in it that fills only part of its cell, as it really collides.
/// The generator's furniture, fittings, crates and rubble give their blocks' collision boxes
/// (<see cref="Shapes.CollisionBoxes"/>); a dressed room's pieces give the boxes their bodies are built
/// of, turned and tilted as they stand: furniture, the things set on it, the clutter on the floor (all but
/// the flat litter walked over). Its creatures and its people go by the one set, made once a place and
/// kept as blocks are broken and pieces taken.
/// </summary>
public static class PlaceParts
{
    /// <summary>
    /// Tests only: the whole-cell rule as it was before the parts (a piece of furniture took its whole cell
    /// from people and creatures; creatures never saw the dressing), so a run can measure one against the other.
    /// </summary>
    public static bool Legacy;

    /// <summary>The body the people's walking grid is worked out for (each walks it at their own width, held off the parts as they go).</summary>
    public const float PersonRadius = 0.3f, PersonHeight = 1.75f;

    /// <summary>A block's parts belong to its cell.</summary>
    public static long OwnerOf(PInt3 c) => 1 + (((long)(c.X + 0x80000) << 40) | ((long)(c.Y + 0x80000) << 20) | (long)(c.Z + 0x80000));

    /// <summary>A dressed piece's parts belong to it.</summary>
    public static long OwnerOf(int module, int index) => (1L << 62) | ((long)module << 24) | (long)index;

    /// <summary>The place's parts (made the first time they're asked for; worker-safe). Null when <see cref="Legacy"/>.</summary>
    public static PartSet Of(PlaceMemory m)
    {
        if (Legacy) return null;
        lock (m)
        {
            if (m.Parts != null) return m.Parts;
            var parts = new PartSet();
            foreach (var (c, b) in m.Inst.Blocks.Blocks()) AddBlock(parts, c, b);
            if (m.Rooms != null && Decor.Shared is { } decor)
                foreach (PlaceRoom room in m.Rooms.Values)
                {
                    HashSet<int> taken = m.DecoTaken.GetValueOrDefault(room.Module);
                    for (int i = 0; i < room.Plan.Placements.Count; i++)
                        if (taken == null || !taken.Contains(i)) AddPiece(parts, decor, room, i);
                }
            return m.Parts = parts;
        }
    }

    static void AddBlock(PartSet parts, PInt3 c, Block b)
    {
        if (!BlockInfo.IsPart(b.Type)) return;
        long owner = OwnerOf(c);
        foreach (var (lo, hi) in Shapes.CollisionBoxes(b, c))
            parts.AddBox(new Vector3(c.X + lo.X, c.Y + lo.Y, c.Z + lo.Z), new Vector3(c.X + hi.X, c.Y + hi.Y, c.Z + hi.Z), owner);
    }

    /// <summary>Whether a placed piece is something to go round: all but the flat litter walked over (as the dressing's own rule has it).</summary>
    public static bool Counts(Decor decor, DecoPlacement pl)
    {
        DecoPiece p;
        try { p = decor.Catalogue.Piece(pl.Piece); }
        catch (KeyNotFoundException) { return false; }
        return !(p.WalkOver && (pl.Fall == DecoFall.None || p.Height <= 0.3f));
    }

    static void AddPiece(PartSet parts, Decor decor, PlaceRoom room, int index)
    {
        DecoPlacement pl = room.Plan.Placements[index];
        if (!Counts(decor, pl)) return;
        long owner = OwnerOf(room.Module, index);
        System.Numerics.Matrix4x4 at = pl.Transform;
        var o = new Vector3(room.Corner.X, room.Corner.Y + room.Floor, room.Corner.Z);
        foreach (DecoBox box in decor.Forge.Mesh(pl.Look).Boxes)
        {
            System.Numerics.Vector3 c = System.Numerics.Vector3.Transform(box.Centre, at);
            System.Numerics.Vector3 x = System.Numerics.Vector3.TransformNormal(box.X * box.Half.X, at);
            System.Numerics.Vector3 y = System.Numerics.Vector3.TransformNormal(box.Y * box.Half.Y, at);
            System.Numerics.Vector3 z = System.Numerics.Vector3.TransformNormal(box.Z * box.Half.Z, at);
            float hx = x.Length(), hy = y.Length(), hz = z.Length();
            if (hx < 1e-5f || hy < 1e-5f || hz < 1e-5f) continue;
            parts.Add(o + new Vector3(c.X, c.Y, c.Z), new Vector3(x.X, x.Y, x.Z) / hx, new Vector3(y.X, y.Y, y.Z) / hy, new Vector3(z.X, z.Y, z.Z) / hz, new Vector3(hx, hy, hz), owner);
        }
    }

    /// <summary>Blocks of the place changed (broken, built, salvaged): their parts afresh.</summary>
    public static void Refresh(PlaceMemory m, IEnumerable<PInt3> cells)
    {
        PartSet parts = m.Parts;
        if (parts == null) return;
        lock (m)
            foreach (PInt3 c in cells)
            {
                parts.Remove(OwnerOf(c));
                AddBlock(parts, c, m.Inst.Blocks.Get(c));
            }
    }

    /// <summary>A dressed piece taken from its room: its parts go.</summary>
    public static void Taken(PlaceMemory m, int module, int index)
    {
        if (m.Parts == null) return;
        lock (m) m.Parts.Remove(OwnerOf(module, index));
    }
}
