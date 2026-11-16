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
/// shell of plated base blocks shaped to the plan's outer form (its faction's body plan, ShipForm) with slopes and
/// corners wherever a face leans, its pipes and struts of cylinder blocks, thrusters packed in its bells' throats and
/// turrets on its faces. Inside (switched off until rooms are fitted into the form, docs/ship-forms.md phase B):
/// decks 5 m apart (a 1 m deck and 4 m clear), a spine
/// corridor, side passages and cross passages on every deck, crawlways through the machinery decks, the holds, hangar and engine room as halls two decks
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
        /// <summary>Where the crash crushed the top deck or tore the decks (structure cells at their walking levels).</summary>
        public readonly HashSet<Int3> Crushed = new();
        public Piece Piece;
        public Plan Plan;
        public ulong Seed;
        public PoiState State;
        /// <summary>Its collision, worked out where it was built (off the main thread), or null.</summary>
        public List<GalHack.Physics.ChunkColliders> Colliders;
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
    readonly bool Plain;
    readonly ShapeFit Fit;
    readonly BlockType Plating, Glass;
    readonly Dictionary<string, ushort> Paint = new();

    int VEnd => Pc.Last ? H.L : Pc.V1;
    Int3 At(int u, int y, int v) => new(u, y, v - Pc.V0);

    HullVessel(Plan plan, Piece piece, Catalogue cat, PoiState state, ulong seed, bool intact, bool plain)
    {
        H = plan;
        Pc = piece;
        Cat = cat;
        State = state;
        Intact = intact;
        Plain = plain;
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
    /// library (null: undressed). <paramref name="plain"/> paints it all one grey: its form with the livery off.
    /// For now it is the outer form alone: its rooms, the crash's and the years' damage and the dressing wait until rooms
    /// are fitted inside the form (docs/ship-forms.md phase B).
    /// </summary>
    public static Result Build(Plan plan, Piece piece, Catalogue cat, PoiState state, ulong seed, GalHack.Deco.DecoPoi dress = null, bool intact = false, bool plain = false)
    {
        var b = new HullVessel(plan, piece, cat, state, seed, intact, plain);
        b.Paints();
        b.Corners();
        b.Shell();
        b.Pipes();
        b.Engines();
        b.Turrets();
        b.Hollow();
        b.S.Name = plan.Role switch { "warship" => "Warship", "liner" => "Liner", "miner" => "Mining Ship", "carrier" => "Carrier", _ => "Freighter" } + piece.Name;
        b.S.Owner = null;
        b.S.DirtyChunks.Clear();
        return b.Out;
    }

    // ---- paint ----------------------------------------------------------------------------------------

    static uint Hex(Rgb c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    /// <summary>The one grey of a form with its livery off.</summary>
    const uint Grey = 0x9a9c9e;

    void Paints()
    {
        GalHack.Structures.Paint Coat(uint c, int grime) => GalHack.Structures.Paint.Solid(c) with { Grime = (byte)grime };
        HullStyle st = H.Style;
        // (A Husk ship keeps its faction's livery, fouled.)
        int outside = Plain ? 1 : State == PoiState.Infested && !Intact ? 3 : Math.Clamp(st.Grime, 0, 3);
        foreach (var (name, c) in H.Paints) Paint[name] = D.Paint(name, Coat(Plain ? Grey : Hex(c), name is "deck" ? 3 : outside));
        // The form's own: tanks and radiators in the hull's and the dark's shades.
        Paint["tank"] = D.Paint("tank", Coat(Plain ? Grey : Hex(H.Paints["hull"].Lerp(H.Paints["trim"], 0.4f)), outside));
        Paint["radiator"] = D.Paint("radiator", Coat(Plain ? Grey : Hex(H.Paints["dark"].Lerp(H.Paints["machine"], 0.3f)), outside));
        Paint["flesh"] = D.Paint("flesh", Coat(Plain ? Grey : 0x8a3a50u, 3));
        if (Plain)
        {
            Paint["glow"] = Paint["hull"];
            return;
        }
        // The band in the style's pattern over its second colour.
        if (st.Pattern is "stripes" or "chevrons" or "checker" or "hazard")
            Paint["orange"] = D.Paint("band", Coat(Hex(st.Band), outside).WithPattern(Enum.Parse<Pattern>(st.Pattern, true), Hex(st.Band2)));
        // Glowing strips (markings, status lights) in the style's colour; hull numerals stencilled over the trim.
        if (st.Glow is { } glow)
            Paint["glow"] = D.Paint("glow", GalHack.Structures.Paint.Lit(LampShape.Strip, LightSettings.Make(Hex(glow), LightAnim.Steady, 0.6f, 0)));
        for (char c = '0'; c <= '9'; c++) Paint["num" + c] = D.Paint("num" + c, Coat(Hex(H.Paints["trim"]), outside).WithStencil(c, Hex(st.Band)));
        // The veins through a Husk ship's walls.
        Paint["vein"] = D.Paint("vein", Coat(0x5a2a3a, 3).WithPattern(Pattern.Stripes, 0xb8486e));
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
        _u0 = -H.ExtentU - 1;
        _nu = 2 * H.ExtentU + 3;
        _ny = H.ExtentY + 1;
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

    /// <summary>
    /// The shell: every cell the envelope passes through, shaped to it (slopes on the chamfers and the rake, corners
    /// where two meet), and the full cells just inside those, plated and in the livery's paint. What the crash tore
    /// away is left out of a wreck.
    /// </summary>
    void Shell()
    {
        for (int v = Pc.V0; v < VEnd; v++)
            for (int y = 0; y < _ny; y++)
                for (int u = _u0; u < _u0 + _nu; u++)
                {
                    int m = Mask(u, y, v);
                    if (m == 0 || Interior(u, y, v)) continue;
                    if (Fit.For(m) is not var (shape, rot)) continue;
                    Put(u, y, v, Armour(Plating, shape, rot, Livery(u, y, v)));
                }
    }

    /// <summary>The paint on the outside: each part of the form its own (the livery's patterns come with the rooms, phase B).</summary>
    ushort Livery(int u, int y, int v) => H.Form.PaintOf(u, y, v) is { } name && Paint.TryGetValue(name, out ushort p) ? p : Paint["hull"];

    static int Mod(int a, int m) => ((a % m) + m) % m;

    /// <summary>The form's pipes and struts along an axis: runs of cylinder blocks, plated, wherever the cells are free.</summary>
    void Pipes()
    {
        ShapeDef tube = Cat.Shape("cylinder");
        foreach (var (from, axis, n, paint) in H.Form.Pipes)
        {
            int rot = axis switch { 0 => Turn(Dir.PosX, Dir.NegZ), 1 => Orientations.Identity, _ => Turn(Dir.PosZ, Dir.PosY) };
            for (int k = 0; k < n; k++)
            {
                int u = from.X + (axis == 0 ? k : 0), y = from.Y + (axis == 1 ? k : 0), v = from.Z + (axis == 2 ? k : 0);
                if (InPiece(v) && y >= 0 && !Has(u, y, v)) Put(u, y, v, Armour(Plating, tube, rot, Paint.TryGetValue(paint, out ushort p) ? p : Paint["machine"]));
            }
        }
    }

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

    /// <summary>A column of a deck all inside the shell from its floor to its ceiling.</summary>
    bool Open(int u, int d, int v)
    {
        int y0 = Plan.Walk(d);
        for (int y = y0; y < y0 + 4; y++)
            if (!Interior(u, y, v)) return false;
        return true;
    }
}
