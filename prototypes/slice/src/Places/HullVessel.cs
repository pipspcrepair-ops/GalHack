using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using PoiGenerator.Gen;
using Block = GalHack.Structures.Block;
using BlockType = GalHack.Structures.BlockType;
using Dirs = GalHack.Structures.Dirs;
using Building = GalHack.Structures.Building;
using Dir = GalHack.Structures.Dir;
using NV3 = System.Numerics.Vector3;
using Int3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;
using Plan = PoiGenerator.Gen.CapitalHull.HullPlan;
using Piece = PoiGenerator.Gen.CapitalHull.HullPiece;

namespace Slice.Places;

/// <summary>
/// A capital hull's plan (poi-generator's CapitalHull) built as a vessel of the player's own blocks on the 1 m grid,
/// one piece of it at a time: the one hull builder for the planet's crashed hulks and orbit's derelicts.
///
/// It is built the way a player builds a capital ship (docs/place-patterns.md 1.4, 5.2; one-metre-grid.md 6): a
/// shell of plated base blocks shaped to the plan's envelope with slopes and corners (the tapered, raked bow, the
/// chamfered shoulders and bilges, the tower and nacelles), decks 5 m apart (a 1 m deck and 4 m clear), a spine
/// corridor, side passages and cross passages on every deck, the holds, hangar and engine room as halls two decks
/// tall, rooms off the corridors for what a crew needs (bridge, officers, quarters, galley, medbay, workshop, stores,
/// reactor, generators, the core room), switchback stairs, doors that open by hand. Its devices are the player's
/// real parts sized from the catalogue: thrusters in the nacelles, lift engines in the keel under the halls, a
/// reactor of generators, gyros and a warp drive by the engines, turrets on the roof, cargo containers, a cockpit
/// and command chairs on the bridge, and no core, only the seat for one. Every room is lit for its purpose and
/// dressed by the decorations library, as a house's rooms are (RoomDecor, the interiors proof). The crash's tears
/// and crushing come from the plan, the years' damage from the state, and enough always comes through to fly.
/// </summary>
public sealed partial class HullVessel
{
    /// <summary>A space of the vessel: a room, a hall or a deck's corridors, its floor cells (hull frame u, v) at its walking level.</summary>
    public sealed class Space
    {
        public string Purpose;
        public int Deck, Y, Height = 4;
        public bool Corridor, Hall;
        public readonly HashSet<(int U, int V)> Floor = new();
        /// <summary>Its doorways: the wall cell a door or opening stands in and the cell inside it.</summary>
        public readonly List<((int U, int V) Wall, (int U, int V) Inside, int Width)> Doors = new();
        /// <summary>Floor cells something stands on (a device, a cargo stack, a stair).</summary>
        public readonly HashSet<(int U, int V)> Taken = new();
        public int U0 => Floor.Min(c => c.U);
        public int U1 => Floor.Max(c => c.U);
        public int V0 => Floor.Min(c => c.V);
        public int V1 => Floor.Max(c => c.V);
        public float Area => Floor.Count;
    }

    /// <summary>A deco piece the vessel is dressed with: what, its look, the structure cells it takes, its place in the structure's frame (metres) and yaw.</summary>
    public sealed record Dressing(GalHack.Deco.DecoLook Look, Int3 Anchor, List<Int3> Cells, NV3 At, float Yaw);

    /// <summary>What a piece of the hull came out as.</summary>
    public sealed class Result
    {
        public Structure Structure;
        public readonly List<Space> Spaces = new();
        public readonly List<Dressing> Decor = new();
        /// <summary>The hull door from outside (its anchor), the empty seat for a core.</summary>
        public Int3? Door, CoreSlot;
        /// <summary>Where the plan crushed the top deck (structure cells at its walking level).</summary>
        public readonly HashSet<Int3> Crushed = new();
        public Piece Piece;
        public Plan Plan;
        public ulong Seed;
        public PoiState State;
    }

    readonly Plan H;
    readonly Piece Pc;
    readonly Catalogue Cat;
    readonly Designer D;
    readonly Structure S;
    readonly Result Out = new();
    readonly Random Rng;
    readonly bool Intact;
    readonly PoiState State;
    readonly ShapeFit Fit;
    readonly BlockType Plating, Glass;
    readonly Dictionary<string, ushort> Paint = new();

    int VEnd => Pc.Last ? H.L + H.Nac : Pc.V1;
    Int3 At(int u, int y, int v) => new(u, y, v - Pc.V0);

    HullVessel(Plan plan, Piece piece, Catalogue cat, PoiState state, ulong seed, bool intact)
    {
        H = plan;
        Pc = piece;
        Cat = cat;
        State = state;
        Intact = intact;
        Rng = new Random(unchecked((int)(seed ^ (seed >> 29))));
        D = new Designer(cat, "hulk", CoreMode.Flight);
        S = D.S;
        Out.Structure = S;
        Out.Piece = piece;
        Out.Plan = plan;
        Out.Seed = seed;
        Out.State = state;
        Fit = new ShapeFit(cat, "cube", "slope", "corner", "invcorner");
        Plating = cat.Type(plan.Tech == TechLevel.High || plan.Role == "warship" ? "hardened-plated" : "iron-plated");
        Glass = cat.Type("glass-plated");
    }

    /// <summary>
    /// One piece of a capital hull as a coreless vessel. <paramref name="intact"/> builds it as its builder made it,
    /// before the crash and the years (what a full repair puts back). <paramref name="dress"/> is the decorations
    /// library (null: undressed).
    /// </summary>
    public static Result Build(Plan plan, Piece piece, Catalogue cat, PoiState state, ulong seed, GalHack.Deco.DecoPoi dress = null, bool intact = false)
    {
        var b = new HullVessel(plan, piece, cat, state, seed, intact);
        b.Paints();
        b.Corners();
        b.Shell();
        b.Decks();
        b.Spaces();
        b.Walls();
        b.Stairs();
        b.Doors();
        b.Ways();
        b.Windows();
        b.Engines();
        b.Fittings();
        b.Lights();
        b.Finish();
        if (!intact) b.Damage();
        b.Hollow();
        if (dress != null) b.Dress(dress);
        b.S.Name = plan.Role switch { "warship" => "Warship", "liner" => "Liner", "miner" => "Mining Ship", "carrier" => "Carrier", _ => "Freighter" } + piece.Name;
        b.S.Owner = null;
        b.S.DirtyChunks.Clear();
        return b.Out;
    }

    // ---- paint ----------------------------------------------------------------------------------------

    static uint Hex(Rgb c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    void Paints()
    {
        GalHack.Structures.Paint Coat(uint c, int grime) => GalHack.Structures.Paint.Solid(c) with { Grime = (byte)grime };
        foreach (var (name, c) in H.Paints) Paint[name] = D.Paint(name, Coat(Hex(c), name is "deck" or "hull" or "trim" ? 3 : 2));
        uint hazard = Hex(H.Paints["hazard"]), black = Hex(H.Paints["black"]);
        Paint["hazard"] = D.Paint("hazard", Coat(hazard, 2).WithPattern(Pattern.Hazard, black));
        Paint["scorch"] = D.Paint("scorch", Coat(Hex(H.Paints["hull"].Lerp(new Rgb(0x20, 0x1c, 0x1a), 0.7f)), 3));
        Paint["blue"] = D.Paint("blue", Coat(0x3a5a9a, 2));
        Paint["red"] = D.Paint("red", Coat(0x8a2a22, 2));
        // Each kind of room its own walls and floor: warm in the crew's rooms, white in the medbay, dark round the machines.
        Paint["lived"] = D.Paint("lived", Coat(Hex(H.Paints["wall"].Lerp(new Rgb(0xc8, 0xb8, 0x96), 0.35f)), 1));
        Paint["clinic"] = D.Paint("clinic", Coat(0xd8dcd6, 1));
        Paint["works"] = D.Paint("works", Coat(Hex(H.Paints["machine"].Scale(0.8f)), 3));
        Paint["floor-lived"] = D.Paint("floor-lived", Coat(Hex(H.Paints["deck"].Lerp(new Rgb(0x5a, 0x48, 0x38), 0.4f)), 1));
        Paint["floor-clinic"] = D.Paint("floor-clinic", Coat(0x9aa4a0, 1));
    }

    /// <summary>The walls' and floor's paints of a space, by what it is for.</summary>
    (ushort Wall, ushort Floor) FinishOf(Space s) => s.Purpose switch
    {
        "crew-quarters" or "cabin" or "barracks" or "officers" or "galley" or "promenade" => (Paint["lived"], Paint["floor-lived"]),
        "medbay" => (Paint["clinic"], Paint["floor-clinic"]),
        "engine-room" or "reactor" or "generator-room" or "workshop" or "refinery" or "core-room" => (Paint["works"], Paint["deck"]),
        "bridge" or "sensor-room" => (Paint["dark"], Paint["deck"]),
        _ => (Paint["wall"], Paint["deck"]),
    };

    Block Armour(BlockType t, ShapeDef shape, int rot, ushort paint)
    {
        Block b = Building.Make(S, t, shape, rot);
        b.Paint = FacePaint.All(paint);
        return b;
    }

    void Put(int u, int y, int v, Block b) => S.Set(At(u, y, v), b);
    void Solid(int u, int y, int v, ushort paint) => Put(u, y, v, Armour(Plating, Cat.Cube, Orientations.Identity, paint));
    bool Has(int u, int y, int v) => S.Has(At(u, y, v));
    void Clear(int u, int y, int v) => S.Clear(At(u, y, v));

    // ---- the envelope ---------------------------------------------------------------------------------

    int _u0, _nu, _ny, _nv;
    byte[] _mask;
    bool[] _inner;

    /// <summary>
    /// Each cell's corner mask (which of its corners lie inside the plan's solid: bit 0 +u, bit 1 +y, bit 2 +v) over the
    /// piece's box, and which cells are inside the shell (the cell and every one round it wholly the hull's).
    /// </summary>
    void Corners()
    {
        _u0 = -H.Half - 2;
        _nu = 2 * H.Half + 5;
        _ny = H.TowerTop + 2;
        _nv = VEnd - Pc.V0;
        int cu = _nu + 1, cy = _ny + 1;
        var corner = new bool[cu * cy * (_nv + 1)];
        for (int k = 0; k <= _nv; k++)
            for (int j = 0; j < cy; j++)
                for (int i = 0; i < cu; i++)
                    corner[i + cu * (j + cy * k)] = H.Solid(_u0 + i - 0.5f, j - 0.5f, Pc.V0 + k - 0.5f);
        _mask = new byte[_nu * _ny * _nv];
        for (int k = 0; k < _nv; k++)
            for (int j = 0; j < _ny; j++)
                for (int i = 0; i < _nu; i++)
                {
                    int m = 0;
                    for (int c = 0; c < 8; c++)
                        if (corner[i + (c & 1) + cu * (j + (c >> 1 & 1) + cy * (k + (c >> 2 & 1)))]) m |= 1 << c;
                    _mask[i + _nu * (j + _ny * k)] = (byte)m;
                }
        _inner = new bool[_mask.Length];
        for (int k = 1; k < _nv - 1; k++)
            for (int j = 1; j < _ny - 1; j++)
                for (int i = 1; i < _nu - 1; i++)
                {
                    bool all = true;
                    for (int dk = -1; dk <= 1 && all; dk++)
                        for (int dj = -1; dj <= 1 && all; dj++)
                            for (int di = -1; di <= 1 && all; di++)
                                all = _mask[i + di + _nu * (j + dj + _ny * (k + dk))] == 255;
                    _inner[i + _nu * (j + _ny * k)] = all;
                }
    }

    int Index(int u, int y, int v)
    {
        int i = u - _u0, k = v - Pc.V0;
        return i < 0 || y < 0 || k < 0 || i >= _nu || y >= _ny || k >= _nv ? -1 : i + _nu * (y + _ny * k);
    }

    /// <summary>The cell's corner mask.</summary>
    int Mask(int u, int y, int v) => Index(u, y, v) is int i && i >= 0 ? _mask[i] : 0;

    bool InPiece(int v) => v >= Pc.V0 && v < VEnd;

    /// <summary>Inside the shell: the cell and everything round it is the hull's.</summary>
    public bool Interior(int u, int y, int v) => Index(u, y, v) is int i && i >= 0 && _inner[i];

    bool InTower(int u, int v) => u >= H.TU0 && u <= H.TU1 && v >= H.TV0 && v <= H.TV1;

    /// <summary>
    /// The shell: every cell the envelope passes through, shaped to it (slopes on the chamfers and the rake, corners
    /// where two meet), and the full cells just inside those, plated and in the livery's paint. What the crash tore
    /// away is left out of a wreck.
    /// </summary>
    void Shell()
    {
        for (int v = Pc.V0; v < VEnd; v++)
            for (int y = 0; y <= H.TowerTop; y++)
                for (int u = -H.Half - 1; u <= H.Half + 1; u++)
                {
                    int m = Mask(u, y, v);
                    if (m == 0 || Interior(u, y, v)) continue;
                    if (Fit.For(m) is not var (shape, rot)) continue;
                    Put(u, y, v, Armour(Plating, shape, rot, Livery(u, y, v)));
                }
    }

    /// <summary>The livery on the outside: bands down the flanks, chevrons on the bow, hazard checks on the stern and the tower's crown, dark nacelles.</summary>
    ushort Livery(int u, int y, int v)
    {
        int band = H.Top * 3 / 5;
        bool flank = MathF.Abs(u) >= H.HalfWidth(v) - 1.5f && y > 2 && y < H.Top - 2 && v < H.L;
        if (v >= H.L) return (u + y) % 5 == 0 ? Paint["dark"] : Paint["trim"];
        if (y > H.Top + 1) return y >= H.TowerTop - 1 ? ((u / 2 + v / 2 + y / 2) & 1) == 0 ? Paint["hazard"] : Paint["black"] : y <= H.Top + 2 ? Paint["orange"] : Paint["hull"];
        if (flank && (y == band || y == band + 1)) return Paint["orange"];
        if (flank && y == band + 2) return Paint["teal"];
        if (flank && v < H.Bz && y > 2 && y < band - 1 && Mod(v - y, 6) < 3) return Mod(v - y, 12) < 6 ? Paint["orange"] : Paint["black"];
        if (v >= H.L - 3 && y < band && ((v / 2 + y / 2) & 1) == 0) return Paint["hazard"];
        if (y == 0 || y == 1 && MathF.Abs(u) > H.HalfWidth(v) - 3) return Paint["dark"];
        return Paint["hull"];
    }

    static int Mod(int a, int m) => ((a % m) + m) % m;

    // ---- decks ------------------------------------------------------------------------------------------

    /// <summary>The decks: a slab every 5 m inside the shell, in the main hull and up through the tower.</summary>
    void Decks()
    {
        for (int v = Pc.V0; v < Math.Min(VEnd, H.L); v++)
            for (int u = -H.Half; u <= H.Half; u++)
                for (int d = 1; d <= H.Decks + 1; d++)
                {
                    int y = Plan.Pitch * d;
                    if (Interior(u, y, v)) Solid(u, y, v, Paint["deck"]);
                }
    }

    /// <summary>The walking levels the hull has at (u, v): the main decks, and the tower's over the roof.</summary>
    int DeckCount(int u, int v) => InTower(u, v) ? H.Decks + Plan.TowerDecks : H.Decks;

    /// <summary>A column of a deck all inside the shell from its floor to its ceiling.</summary>
    bool Open(int u, int d, int v)
    {
        int y0 = Plan.Walk(d);
        for (int y = y0; y < y0 + 4; y++)
            if (!Interior(u, y, v)) return false;
        return true;
    }
}
