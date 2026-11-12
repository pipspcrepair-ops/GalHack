using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Vessels;
using Structure = GalHack.Structures.Structure;

namespace Slice.Construction;

/// <summary>
/// The survival tool's plating mode, the plating gun (docs/one-metre-grid.md section 3): aimed at
/// an armour block, the left button sprays the picked plating on (a bare base block takes it, a
/// plated one swaps it, the old plates coming back by its hit-point share, rounded down), or, on
/// a damaged block already wearing it, tops it back up with plates for the damage. The right
/// button strips the plating back to the base block, for the same refund. The wheel picks the
/// plating. Plates come from where the build tool takes blocks from (your bag, or the structure's
/// storage on its toolbar) and refunds go back the way a taken-down block does. Building's rules
/// do the plating (<see cref="Building.Plate"/>, <see cref="Building.Strip"/>, <see cref="Building.Repair(Structure, Int3)"/>);
/// this pays for it.
/// </summary>
public sealed partial class BuildTool
{
    List<BlockType> _platings;

    /// <summary>Every plating the gun sprays, in the wheel's order (a salvage-only plating can't be sprayed).</summary>
    public IReadOnlyList<BlockType> Platings => _platings ??= _catalogue.Types
        .Where(t => t != null && t.IsPlated && !(Items.Blocks.TryGet(t.Id, out BlockBill b) && b.UniqueOnly)).ToList();

    public int PlatingIndex;
    public BlockType Plating => Platings[PlatingIndex];
    public void NextPlating(int step) => PlatingIndex = (PlatingIndex + Math.Sign(step) + Platings.Count) % Platings.Count;

    /// <summary>The plate item a plating sprays on.</summary>
    public ItemDef PlateOf(BlockType plated) => Items.Item(plated.Plate);

    /// <summary>One tick of the plating gun: the verdict for what is aimed at, and a press sprays or strips it.</summary>
    public void TickPlate(bool spray, bool strip)
    {
        _planGhost.Visible = false;
        _deviceGhost.Visible = false;
        _ghosts.HideGhosts();
        Aim();
        if (!HasTarget || TargetVessel == null || !TargetVessel.Structure.TryGetBlock(TargetCell, out Int3 anchor, out Block b))
        {
            _ghosts.HideOutline();
            Verdict = "PLATE · NO BLOCK";
            return;
        }
        Vessel v = TargetVessel;
        Structure s = v.Structure;
        BlockType t = s.TypeOf(b), want = Plating;
        _ghosts.ShowOutline(v.DrawTransform, s, TargetCell, new Color(0.18f, 0.77f, 0.71f, 0.9f));
        ItemDef plate = PlateOf(want);
        string why = !t.IsArmor ? $"{t.Name} · NOT ARMOUR"
            : (t.Base ?? t) != want.Base ? $"{plate.Name} · NOT FOR {t.Name}"
            : !Creative && Progress?.BlockAllowed(want.Id) == false ? $"{want.Name} · LOCKED"
            : null;
        Bill need = new(), back = new();
        if (why == null && t == want)
        {
            if (Building.IsDamaged(s, anchor)) foreach (var (id, n) in Building.RepairCost(s, anchor)) need.Add(id, n);
            else why = $"{t.Name} · WHOLE";
        }
        else if (why == null)
        {
            Block next = b;
            next.Type = (ushort)want.Index;
            need.Add(want.Plate, _catalogue.PlatesOf(next));
            foreach (var (id, n) in Building.PlatingWorth(s, b)) back.Add(id, n);
        }
        if (Creative) need = back = new Bill();
        string have = Creative ? "free" : FromStructure ? $"{_pack.HandSource.Count(want.Plate)} in {_pack.HandRemote?.Origin}" : $"{_pack.HandSource.Count(want.Plate)} in your bag";
        string refund = back.IsEmpty ? "" : $" · +{Items.Describe(back, 2)}";
        if (why == null && !_pack.HandUsable) why = $"{_pack.HandRemote?.Origin.ToUpperInvariant()} STORAGE · OUT OF RANGE";
        else if (why == null && need.Any(kv => _pack.HandSource.Count(kv.Key) < kv.Value)) why = $"{plate.Name} · {have} · {need[want.Plate]} NEEDED";
        Verdict = why ?? (t == want ? $"REPAIR · {Items.Describe(need, 2)} · {have}" : $"{want.Name} · {(need.IsEmpty ? "free" : Items.Describe(need, 2))}{refund} · {have}");
        if (strip) Strip(v, anchor, b);
        else if (spray && why != null) Said?.Invoke(why, Tones.Caution);
        else if (spray)
        {
            if (!need.IsEmpty && !_pack.HandSource.TryTake(need)) return;
            if (!FromStructure) _pack.Grid.Reconcile();
            Count(need);
            if (t == want) Building.Repair(s, anchor);
            else Building.Plate(s, anchor, want);
            Changed(v, anchor, b, back, PlanetSurface.Fx.Sfx.Sound.Place);
        }
    }

    /// <summary>Strips the plating aimed at back to its base block; its plates come back by the block's hit-point share.</summary>
    void Strip(Vessel v, Int3 anchor, in Block b)
    {
        Structure s = v.Structure;
        BlockType t = s.TypeOf(b);
        if (!t.IsArmor || !t.IsPlated)
        {
            Said?.Invoke(t.IsArmor ? $"{t.Name} · BARE" : $"{t.Name} · NOT ARMOUR", Tones.Caution);
            return;
        }
        var back = new Bill();
        if (!Creative) foreach (var (id, n) in Building.PlatingWorth(s, b)) back.Add(id, n);
        Building.Strip(s, anchor);
        Changed(v, anchor, b, back, PlanetSurface.Fx.Sfx.Sound.Clank);
    }

    /// <summary>After a block's plating changed: its mass and look, the refund home, a sound and a line.</summary>
    void Changed(Vessel v, Int3 anchor, in Block was, Bill back, PlanetSurface.Fx.Sfx.Sound sound)
    {
        Vector3 at = v.ToWorld(StructureBridge.BlockCenter(v.Structure, anchor, was));
        v.ApplyEdits();
        GiveBack(back, v, at);
        _zone.Sfx?.Play(sound, at, 1.1f, -6f);
        if (!back.IsEmpty) Said?.Invoke($"+{Items.Describe(back, 3)}", Tones.Text);
    }
}
