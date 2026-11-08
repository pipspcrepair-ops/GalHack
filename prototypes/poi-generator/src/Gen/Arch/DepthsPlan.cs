#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// One space of a plan below ground, in whole 2 m blocks of the place's frame (i along its front, k back from it,
/// j a block level): a corridor, a room, a hall, a shaft, a drift. Its interior is a set of columns, each with
/// its own floor and clear height, so a stair climbs and a cave's roof is ragged. Walls are what lies between.
/// </summary>
internal sealed class DSpace
{
    public int Id;
    /// <summary>What it is in the plan: corridor, room, hall, hub, ring, gallery, alcove, cell, chamber, landing, stair, ramp, shaft, lift, crawl, airlock, drift, stope, chute, cave, tunnel.</summary>
    public string Kind = "room";
    public string Purpose = "room";
    public ModuleKind Module = ModuleKind.Room;
    public int J, H;
    readonly Dictionary<(int I, int K), (int Floor, int Height)> _cols = new();
    public int I0 = int.MaxValue, K0 = int.MaxValue, I1 = int.MinValue, K1 = int.MinValue;
    /// <summary>Cut in raw rock: no lining, the planet's own stone for walls (drifts, stopes, caves, squatters' tunnels).</summary>
    public bool Raw;
    /// <summary>Its interior may rise above the ground's cover (a stair to the surface).</summary>
    public bool Surface;
    /// <summary>Which paint lines it: wall, plinth, steel, timber, alien...</summary>
    public string Lining = "wall";
    public string Zone = "";
    public int Level;
    public int Storey;
    public int Mod = -1;
    public RoomDef? Def;
    /// <summary>The part of the plan that placed it, for the signature and the tests.</summary>
    public string Part = "";
    /// <summary>A room whose purpose the level's demands decide.</summary>
    public bool Open;
    public bool Secret;
    /// <summary>Straight run of a stair or ramp: its foot column, direction and rise, for building it.</summary>
    public (int I, int K, Dir D, int Rise, int Width)? Run;
    public bool Ramp;
    /// <summary>Taken back out of the plan (ids stay put, so the grid's marks stay true).</summary>
    public bool Dead;
    /// <summary>A gallery deck's open side: the columns its rail runs along, and which way the drop lies.</summary>
    public List<(int I, int K, Dir Drop)> Rail = new();

    public IEnumerable<(int I, int K)> Foot => _cols.Keys;
    public int Columns => _cols.Count;
    public int W => I1 - I0 + 1;
    public int D => K1 - K0 + 1;
    public bool IsBox => _cols.Count == W * D && _cols.Values.All(c => c.Floor == J && c.Height == H);
    public (float I, float K) Mid => ((I0 + I1) / 2f, (K0 + K1) / 2f);

    public void Column(int i, int k, int floor, int height)
    {
        _cols[(i, k)] = (floor, height);
        I0 = Math.Min(I0, i); I1 = Math.Max(I1, i);
        K0 = Math.Min(K0, k); K1 = Math.Max(K1, k);
    }

    public void Column(int i, int k) => Column(i, k, J, H);
    public bool Has(int i, int k) => _cols.ContainsKey((i, k));
    public int FloorAt(int i, int k) => _cols.TryGetValue((i, k), out var c) ? c.Floor : int.MinValue;
    public int HeightAt(int i, int k) => _cols.TryGetValue((i, k), out var c) ? c.Height : 0;
    public bool Holds(int i, int j, int k) => _cols.TryGetValue((i, k), out var c) && j >= c.Floor && j < c.Floor + c.Height;

    public IEnumerable<(int I, int J, int K)> Blocks()
    {
        foreach (var ((i, k), (f, h)) in _cols)
            for (int j = f; j < f + h; j++) yield return (i, j, k);
    }

    public int Top => _cols.Count == 0 ? J : _cols.Values.Max(c => c.Floor + c.Height - 1);
    public int Bottom => _cols.Count == 0 ? J : _cols.Values.Min(c => c.Floor);
    public int Area => _cols.Count;
    public override string ToString() => $"{Kind}:{Purpose}#{Id} [{I0}..{I1}]x[{K0}..{K1}] j{J}+{H}";
}

/// <summary>How two spaces meet: open (no wall), a door, a blast door, a locked door, a hidden wall, a breach, a hatch.</summary>
internal sealed class DJoint
{
    public DSpace A = null!, B = null!;
    public string Kind = "open";
    /// <summary>For a door: its block in the wall between them, and whether that wall runs along i.</summary>
    public int I, J, K;
    public bool AlongI;
    public string? Clearance;
    public override string ToString() => $"{Kind} {A.Id}-{B.Id} at {I},{J},{K}";
}

/// <summary>
/// The plan's blocks: which space's interior holds each block. A new space must leave a block of wall or rock
/// round it against every other space, except the ones it opens straight into, so walls never thin out and two
/// rooms never touch at a corner.
/// </summary>
internal sealed class DGrid
{
    public const int IMin = -50, IMax = 50, KMin = -40, KMax = 80, JMin = -46, JMax = 2;
    /// <summary>The highest block an interior may reach and keep a block of lining and one of rock over it.</summary>
    public const int Cover = -3;
    const int NI = IMax - IMin + 1, NK = KMax - KMin + 1, NJ = JMax - JMin + 1;
    const short Reserved = short.MaxValue;
    readonly short[] _own = new short[NI * NK * NJ];

    static int Index(int i, int j, int k) => ((j - JMin) * NK + (k - KMin)) * NI + (i - IMin);
    public static bool In(int i, int j, int k) => i >= IMin && i <= IMax && k >= KMin && k <= KMax && j >= JMin && j <= JMax;

    /// <summary>The space whose interior holds a block: -1 none, -2 off the plan, -3 kept clear (a door's block).</summary>
    public int Owner(int i, int j, int k)
    {
        if (!In(i, j, k)) return -2;
        short v = _own[Index(i, j, k)];
        return v == Reserved ? -3 : v - 1;
    }

    public bool Interior(int i, int j, int k) => Owner(i, j, k) >= 0;

    public void Mark(DSpace s)
    {
        foreach (var (i, j, k) in s.Blocks()) _own[Index(i, j, k)] = (short)(s.Id + 1);
    }

    public void Unmark(DSpace s)
    {
        foreach (var (i, j, k) in s.Blocks())
            if (_own[Index(i, j, k)] == s.Id + 1) _own[Index(i, j, k)] = 0;
    }

    public void Keep(int i, int j, int k)
    {
        if (In(i, j, k) && _own[Index(i, j, k)] == 0) _own[Index(i, j, k)] = Reserved;
    }

    /// <summary>Whether a space can go where it is drawn: its interior free, a block of wall round it against all but the spaces it may touch.</summary>
    public bool Fits(DSpace s, ICollection<int>? touch = null)
    {
        foreach (var (i, j, k) in s.Blocks())
        {
            if (!In(i, j, k)) return false;
            if (!s.Surface && j > Cover) return false;
            if (_own[Index(i, j, k)] != 0) return false;
        }
        foreach (var (i, j, k) in s.Blocks())
            for (int dj = -1; dj <= 1; dj++)
                for (int dk = -1; dk <= 1; dk++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int ni = i + di, nj = j + dj, nk = k + dk;
                        if (!In(ni, nj, nk)) { if (nj <= JMax) return false; continue; }
                        short v = _own[Index(ni, nj, nk)];
                        if (v == 0 || v == Reserved || v == s.Id + 1) continue;
                        if (touch == null || !touch.Contains(v - 1)) return false;
                    }
        return true;
    }
}
