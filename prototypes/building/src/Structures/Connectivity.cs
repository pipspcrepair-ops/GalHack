using System.Collections.Generic;
using System.Linq;

namespace GalHack.Structures;

/// <summary>
/// Which blocks hold together. Two blocks are joined when both touch their shared face. A
/// section that loses its path to the core breaks away as an unowned wreck: a new structure that
/// takes the blocks (with their damage and paint) and its share of the cargo. Nothing vanishes.
/// There is deliberately no stress or load simulation.
/// </summary>
public static class Connectivity
{
    public static HashSet<Int3> Reachable(Structure s, Int3 start, Int3? without = null) =>
        Reachable(s, start, without is Int3 w && s.TryGetBlock(w, out Int3 anchor, out Block b) ? new HashSet<Int3>(s.CellsOf(anchor, b)) : null);

    /// <summary>The cells joined to <paramref name="start"/>, as if the cells in <paramref name="without"/> were empty.</summary>
    public static HashSet<Int3> Reachable(Structure s, Int3 start, HashSet<Int3> without)
    {
        var seen = new HashSet<Int3>();
        if (!s.Has(start) || without != null && without.Contains(start)) return seen;
        var queue = new Queue<Int3>();
        seen.Add(start);
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Int3 p = queue.Dequeue();
            foreach (Dir d in Dirs.All)
            {
                Int3 n = p + d.Vec();
                if (without != null && without.Contains(n) || seen.Contains(n) || !s.Joined(p, d)) continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        return seen;
    }

    /// <summary>How many blocks would lose their core if the block at <paramref name="removed"/> went.</summary>
    public static int CutOffWithout(Structure s, Int3 removed)
    {
        if (!s.TryGetBlock(removed, out Int3 anchor, out Block block)) return 0;
        if (s.Core is not Int3 core || core == anchor) return s.BlockCount - 1;

        // A one-cell block joined to one neighbour or none is a leaf: removing it cuts nothing off.
        if (!s.TypeOf(block).Multi && Dirs.All.Count(d => s.Joined(anchor, d)) <= 1) return 0;
        HashSet<Int3> kept = Reachable(s, core, removed);
        return s.BlockCount - 1 - (s.MultiCellBlocks == 0 ? kept.Count : kept.Count(c => !s.Get(c).IsFiller));
    }

    /// <summary>The structure's separate pieces, largest first.</summary>
    public static List<List<Int3>> Pieces(Structure s)
    {
        var seen = new HashSet<Int3>();
        var pieces = new List<List<Int3>>();
        foreach (var (p, _) in s.Blocks())
        {
            if (seen.Contains(p)) continue;
            HashSet<Int3> piece = Reachable(s, p);
            seen.UnionWith(piece);
            pieces.Add(piece.ToList());
        }
        pieces.Sort((a, b) => b.Count.CompareTo(a.Count));
        return pieces;
    }

    /// <summary>
    /// Moves every section that is not attached to the core into a wreck of its own and returns
    /// the wrecks. Without a core (it was destroyed) the largest piece stays as this structure.
    /// </summary>
    public static List<Structure> SplitOff(Structure s)
    {
        var wrecks = new List<Structure>();
        if (s.Count == 0) return wrecks;

        List<List<Int3>> detached;
        if (s.Core is Int3 core)
        {
            HashSet<Int3> attached = Reachable(s, core);
            if (attached.Count == s.Count) return wrecks;
            var rest = new Structure(s.Catalogue);
            foreach (var (p, b) in s.Blocks())
                if (!attached.Contains(p)) rest.Set(p, b);
            detached = Pieces(rest);
        }
        else
        {
            List<List<Int3>> pieces = Pieces(s);
            if (pieces.Count <= 1) return wrecks;
            detached = pieces.Skip(1).ToList();
        }

        double capacityBefore = RawStorage(s, s.Blocks().Select(x => x.pos));
        double cargo = s.CargoSu;
        int n = 1;
        foreach (List<Int3> piece in detached)
        {
            double share = capacityBefore > 0 ? cargo * RawStorage(s, piece) / capacityBefore : 0;
            Structure wreck = Extract(s, piece, $"{s.Name} wreck {n++}");
            wreck.CargoSu = share;
            s.CargoSu -= share;
            wrecks.Add(wreck);
        }
        return wrecks;
    }

    /// <summary>Moves blocks into a new unowned structure at the same grid coordinates, keeping damage, state and paint.</summary>
    public static Structure Extract(Structure s, IEnumerable<Int3> cells, string name)
    {
        var wreck = new Structure(s.Catalogue) { Name = name, Owner = null, Mode = s.Mode, GravityOn = s.GravityOn };
        foreach (Int3 p in cells)
        {
            // A block spanning several cells moves whole with its anchor: its fillers are written with it.
            if (!s.TryGet(p, out Block b) || b.IsFiller) continue;
            foreach (Dir d in Dirs.All)
            {
                ushort slot = b.Paint[d];
                if (slot != 0) b.Paint[d] = wreck.Palette.Intern(s.Palette[slot], s.Palette.NameOf(slot));
            }
            wreck.Set(p, b);
            s.Clear(p);
        }
        return wreck;
    }

    static double RawStorage(Structure s, IEnumerable<Int3> cells)
    {
        double total = 0;
        foreach (Int3 p in cells)
            if (s.TryGet(p, out Block b) && !b.IsFiller && s.TypeOf(b).IsContainer)
                total += s.TypeOf(b).Storage;
        return total;
    }
}
