#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NV3 = System.Numerics.Vector3;
using NQ = System.Numerics.Quaternion;

namespace PoiGenerator.Gen;

/// <summary>
/// Capital ships come down as hulks and drift in orbit as derelicts, composed from rules, never drawn by hand:
/// one plan for every crashed or derelict capital vessel. A role (freighter, warship, liner, miner, carrier)
/// gives the bays stem to stern; a size class gives the length, the beam and the decks (Empyrion's capital
/// wrecks run 50 x 26 x 102 m at the median, p90 98 x 48 x 174 m, its derelicts up to 276 m long:
/// docs/place-patterns.md 1.4); the world's tech era gives the plating and the faction's kit its livery. How it
/// came down (broken back, nose down, scattered, long hauler) gives its pose.
///
/// The plan is the ship's own: its envelope (a tapered, raked bow, chamfered shoulders and bilges, a bridge tower,
/// engine nacelles), decks 5 m apart (a 1 m deck and 4 m clear), its bays, the ways in the crash left and the
/// tears it made. The vessel itself, the player's own blocks room by room, is built from this plan where it is
/// set down (the slice's HullVessel); the place round it here is the ground it lies in, its ramps and its yard.
///
/// The hull's frame: u across (cells -Half..Half, the middle cell on the centre line), y up from the keel slab
/// (cell 0), v from the stem aft (cells 0..L-1, nacelles behind to L+Nac-1). A cell's middle is its coordinates.
/// </summary>
public static class CapitalHull
{
    /// <summary>What a hull is: its role, size class and how it came down, and the faction whose style it is built in (null: the site's kit's).</summary>
    public sealed record Spec(string Role, string Size, string Crash, string? Faction = null);

    /// <summary>A size class: the length and half-beam ranges (m) and the decks.</summary>
    public static (int L0, int L1, int H0, int H1, int Decks) SizeOf(string size) => size switch
    {
        "medium" => (64, 96, 12, 14, 3),
        "huge" => (150, 250, 20, 25, 5),
        _ => (96, 148, 15, 19, 4),
    };

    public static void Compose(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech, SitePart part)
    {
        Dir front = hints?.Front ?? Dir.North;
        var p = new Plot(c, rng, tech, front);
        Civic.Wear(p);
        var hulk = new Hulk(p, SpecOf(part), rng.Fork("hull"), front is Dir.North or Dir.East or Dir.South or Dir.West ? (int)front : 0);
        hulk.Build();
        Remember(c.X, hulk.Result);
        hulk.EmitPlace();
        p.Finish();
    }

    public static Spec SpecOf(SitePart part) => new(part.Arg("role", "freighter"), part.Arg("size", "large"), part.Arg("crash", "broken"), part.Arg("faction", "") is { Length: > 0 } f ? f : null);

    /// <summary>
    /// Something a faction's style adds outside the hull's body (a cargo pod, a bolted-on module, a scrap plate, a spike, a
    /// fin's run, a truss member): a box in the hull's frame, or tapered along an axis (0 +u, 1 -u, 2 +y, 3 -y, 4 +v, 5 -v)
    /// to Tip of its section at its far end; painted by name.
    /// </summary>
    public sealed record Part(string Paint, float U0, float Y0, float V0, float U1, float Y1, float V1, int Axis = -1, float Tip = 1f)
    {
        public bool Contains(float u, float y, float v)
        {
            if (u < U0 || u > U1 || y < Y0 || y > Y1 || v < V0 || v > V1) return false;
            if (Axis < 0) return true;
            // How far along it is (0 at its base, 1 at its tip), and its section there about its middle.
            float t = Axis switch { 0 => (u - U0) / (U1 - U0), 1 => (U1 - u) / (U1 - U0), 2 => (y - Y0) / (Y1 - Y0), 3 => (Y1 - y) / (Y1 - Y0), 4 => (v - V0) / (V1 - V0), _ => (V1 - v) / (V1 - V0) };
            float k = 1f + (Tip - 1f) * t;
            float cu = (U0 + U1) / 2, cy = (Y0 + Y1) / 2, cv = (V0 + V1) / 2;
            bool InU() => MathF.Abs(u - cu) <= (U1 - U0) / 2 * k;
            bool InY() => MathF.Abs(y - cy) <= (Y1 - Y0) / 2 * k;
            bool InV() => MathF.Abs(v - cv) <= (V1 - V0) / 2 * k;
            return Axis switch { 0 or 1 => InY() && InV(), 2 or 3 => InU() && InV(), _ => InU() && InY() };
        }

        public Part Mirrored => this with { U0 = -U1, U1 = -U0, Axis = Axis switch { 0 => 1, 1 => 0, _ => Axis } };
    }

    /// <summary>
    /// A capital hull with no place round it (a derelict adrift): the same rules, the vessel only. Its pieces lie
    /// as the crash left them relative to one another, in the frame the plan would have had.
    /// </summary>
    public static HullPlan Plan(Kit kit, TechLevel tech, Spec spec, ulong seed)
    {
        var canvas = new Canvas(kit);
        var rng = new Rng(seed);
        var p = new Plot(canvas, rng, tech, Dir.North);
        Civic.Wear(p);
        var hulk = new Hulk(p, spec, rng.Fork("hull"));
        hulk.Build();
        return hulk.Result;
    }

    /// <summary>One structure of a hulk: the stretch of the hull it is (cells V0..V1-1, and the nacelles when it is the last), and where it lies.</summary>
    public sealed class HullPiece
    {
        public string Name = "";
        public int V0, V1;
        /// <summary>The stern piece carries the nacelles.</summary>
        public bool Last;
        /// <summary>Where its structure's cell 0, 0, 0 (the hull's cell 0, 0, V0) lies, metres in the place's frame, and its turn.</summary>
        public NV3 Origin;
        public NQ Rotation = NQ.Identity;    }

    /// <summary>A bay of the hull, stem to stern: what it is for (bow, crew, hold, hangar, power, engine...) and its cells along v.</summary>
    public sealed record Bay(string Kind, int V0, int V1)
    {
        /// <summary>A bay whose lower two decks are one hall (holds, hangars, the engine room).</summary>
        public bool Hall => Kind is "hold" or "hangar" or "engine" or "promenade" or "ore-hold" or "refinery";
    }

    /// <summary>A way in from outside: an opening in a flank (side +1 starboard, -1 port) or an open face (side 0), from deck 0's floor up.</summary>
    public sealed record Way(int Side, int V0, int Width, int Height, string What);

    public sealed class HullPlan
    {
        public string Role = "", Size = "", Crash = "";
        public TechLevel Tech;
        public ulong Seed;
        public Faction Faction;

        /// <summary>Decks are 5 m apart: a 1 m deck and 4 m clear over it.</summary>
        public const int Pitch = 5;
        /// <summary>The bridge tower's decks over the hull's roof.</summary>
        public const int TowerDecks = 2;

        /// <summary>A deck's walking level: the first cell over its slab.</summary>
        public static int Walk(int d) => Pitch * d + 1;

        public int L, Half, Decks, Bz;
        /// <summary>The faction's style it is built in (data/hull-styles.txt).</summary>
        public HullStyle Style = new();
        /// <summary>What the style adds outside the body (pods, modules, plates, spikes, fins, the truss).</summary>
        public readonly List<Part> Parts = new();
        /// <summary>Where the waists between its segments fall (v).</summary>
        public readonly List<int> Waists = new();
        /// <summary>How far the hull and everything on it reaches across (|u|) and up (y), cells: the box a builder must cover.</summary>
        public int ExtentU, ExtentY;
        /// <summary>The roof's level (the slab over the top deck).</summary>
        public int Top => Pitch * Decks;
        public int TowerTop => Pitch * (Decks + TowerDecks);
        public readonly List<Bay> Bays = new();
        public int TU0, TU1, TV0, TV1;
        /// <summary>The nacelles (u spans) behind the stern, Nac cells long, NY0..NY1 high.</summary>
        public readonly List<(int U0, int U1)> Nacelles = new();
        public int Nac, NY0, NY1;
        public int Break = -1, Cut = -1;
        public readonly List<HullPiece> Pieces = new();
        public readonly List<Way> Ways = new();
        /// <summary>The hull door from outside (starboard, deck 0), at this v.</summary>
        public int DoorV = -1;
        /// <summary>A bulbous, rounded bow (the traders' and the liners') rather than a straight taper.</summary>
        public bool Round;
        /// <summary>The livery, by paint name: hull, trim, wall, deck, orange, teal, dark, machine, hazard, black.</summary>
        public readonly Dictionary<string, Rgb> Paints = new();
        /// <summary>Where the crash struck hardest (v), and how far the crushing reaches from it: the top deck there is 2 m clear.</summary>
        public int Impact = -1, Crush;

        float Taper(float v)
        {
            float t = Math.Clamp((v + 0.5f) / Bz, 0f, 1f);
            if (Round) return 0.32f + 0.68f * MathF.Sqrt(1f - (1f - t) * (1f - t));
            return Style.Bow switch
            {
                "blunt" => 0.45f + 0.55f * MathF.Pow(t, 0.6f),
                "needle" => 0.12f + 0.88f * MathF.Pow(t, 1.4f),
                _ => 0.26f + 0.74f * MathF.Pow(t, 0.8f),
            };
        }

        /// <summary>How much of the beam a segment's waist leaves at v (1 away from the waists).</summary>
        float Waisted(float v)
        {
            float k = 1f;
            foreach (int w in Waists) k = MathF.Min(k, 1f - Style.Waist * MathF.Max(0f, 1f - MathF.Abs(v - w) / 5f));
            return k;
        }

        /// <summary>Half the beam at v, m (to the outside of the shell).</summary>
        public float HalfWidth(float v) => (Half + 0.5f) * Taper(v) * Waisted(v) * (1f - 0.06f * Math.Clamp((v - (L - 5)) / 5f, 0f, 1f));

        /// <summary>How far the shoulders slope in at 45 degrees to the roof, m: the style's share of the flank (a block's is small), as a hull leans in to its back.</summary>
        public int Shoulder => Math.Max(2, (int)MathF.Round(Top * Style.Shoulder));

        /// <summary>The hull's solid as drawn (metres in its frame): the hull, the tower, the nacelles, its ribs and what its style adds.</summary>
        public bool Solid(float u, float y, float v) => InHull(u, y, v) || InTower(u, y, v) || InNacelle(u, y, v) || InRib(u, y, v) || InPart(u, y, v) != null;

        /// <summary>The body a crew lives in: the hull and the tower (rooms go nowhere else).</summary>
        public bool Body(float u, float y, float v) => InHull(u, y, v) || InTower(u, y, v);

        /// <summary>The part of the style's at a point, if any.</summary>
        public Part? InPart(float u, float y, float v)
        {
            if (_byV == null || Parts.Count != _indexed) Index();
            int k = (int)MathF.Floor(v);
            if (k < 0 || k >= _byV!.Length) return null;
            foreach (Part p in _byV[k])
                if (p.Contains(u, y, v)) return p;
            return null;
        }

        /// <summary>The parts by the metre of the hull's length they reach into (a point is only tested against those).</summary>
        List<Part>[]? _byV;
        int _indexed = -1;

        void Index()
        {
            int n = Math.Max(1, (int)MathF.Ceiling(Parts.Count == 0 ? 1 : Parts.Max(p => p.V1)) + 2);
            var by = new List<Part>[n];
            for (int i = 0; i < n; i++) by[i] = new List<Part>();
            foreach (Part p in Parts)
                for (int k = Math.Max(0, (int)MathF.Floor(p.V0)); k <= Math.Min(n - 1, (int)MathF.Floor(p.V1)); k++) by[k].Add(p);
            _byV = by;
            _indexed = Parts.Count;
        }

        /// <summary>A frame round the hull (the style's ribs): the hull grown by the rib's depth, in a 1 m slab every so many metres down the body.</summary>
        public bool InRib(float u, float y, float v)
        {
            if (Style.Ribs <= 0 || v < Bz || v > L - 3 || ((int)MathF.Floor(v + 0.5f) - Bz) % Style.Ribs != 0) return false;
            float r = Style.Rib;
            return InHull(u - r, y, v) || InHull(u + r, y, v) || InHull(u, y - r, v) || InHull(u, y + r, v);
        }

        public bool InHull(float u, float y, float v)
        {
            if (v < -0.5f || v > L - 0.5f) return false;
            float hw = HalfWidth(v), au = MathF.Abs(u);
            if (au > hw) return false;
            // A ram's prow rakes far back over its keel blade (a part of its own).
            float fore = MathF.Min(Top / 3f, Bz / 2f), rake = Style.Bow == "ram" ? MathF.Min(Top * 0.9f, Bz * 0.9f) : MathF.Min(Top * 0.5f, Bz * 0.6f);
            float bottom = -0.5f + MathF.Max(0f, fore - (v + 0.5f)) + MathF.Max(0f, au - (hw - 2f));
            float top = Top + 0.5f - MathF.Max(0f, rake - (v + 0.5f)) - MathF.Max(0f, au - (hw - Shoulder)) - MathF.Max(0f, (v + 0.5f) - (L - 2));
            // The dorsal spine down the back, from the bow to the engines, sloped either side and at its ends.
            float spine = MathF.Max(0f, Style.Spine - MathF.Max(0f, au - Style.SpineWidth)) * Math.Clamp(MathF.Min(v - Bz, L - 6 - v) / 1.5f, 0f, 1f);
            return y >= bottom && y <= top + spine;
        }

        public bool InTower(float u, float y, float v)
        {
            float df = v - (TV0 - 0.5f), db = TV1 + 0.5f - v, ds = MathF.Min(u - (TU0 - 0.5f), TU1 + 0.5f - u);
            if (df < 0f || db < 0f || ds < 0f || y < Top * 0.5f || y > TowerTop + 0.5f) return false; // (its walls run down the shoulders into the hull)
            if (df + ds < 2f) return false; // its forward corners cut off
            return y <= TowerTop + 0.5f - MathF.Max(0f, 2f - MathF.Min(MathF.Min(df, db), ds));
        }

        public bool InNacelle(float u, float y, float v)
        {
            if (v < L - 1.5f || v > L + Nac - 0.5f || y < NY0 - 0.5f || y > NY1 + 0.5f) return false;
            foreach (var (u0, u1) in Nacelles)
            {
                float du = MathF.Min(u - (u0 - 0.5f), u1 + 0.5f - u);
                if (du < 0f) continue;
                float dy = MathF.Min(y - (NY0 - 0.5f), NY1 + 0.5f - y);
                if (du + dy >= 2f) return true;
            }
            return false;
        }

        /// <summary>The crash tore this cell away: the ragged faces where the hull broke or parted.</summary>
        public bool Torn(int u, int y, int v)
        {
            if (Break >= 0 && y >= 1)
            {
                int reach = 3 + (int)(Hash.Cell(new Int3(u >> 1, y >> 1, 0), 37) % 3) + (y > Top - 4 ? 1 : 0);
                if (MathF.Abs(v - Break + 0.5f) < reach) return true;
            }
            if (Cut >= 0 && y >= Walk(1) && v >= Cut - 4 && v < Cut + 6)
            {
                int edge = v < Cut ? Cut - 1 - v : v - Cut - 2;
                if (edge < (int)(Hash.Cell(new Int3(u >> 1, y >> 1, 1), 53) % 4)) return true;
            }
            return Cut >= 0 && v >= Cut && v < Cut + 2;
        }

        /// <summary>How far the roof is crushed down into the top deck at v (cells): 2 near where it struck, nothing elsewhere.</summary>
        public int Crushed(int v) => Impact >= 0 && Math.Abs(v - Impact) < Crush ? 2 : 0;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LayoutExtras, HullPlan> Plans = new();

    internal static void Remember(LayoutExtras x, HullPlan plan) => Plans.AddOrUpdate(x, plan);

    /// <summary>The vessel a crash site's place was laid out round, or null when the place is no hulk.</summary>
    public static HullPlan? Of(Layout layout) => Plans.TryGetValue(layout.Extras, out HullPlan? plan) ? plan : null;
}

/// <summary>One hulk's plan, and the place it lies in (the plan's frame: u across, v from its front back, y up from the ground).</summary>
sealed class Hulk
{
    readonly Plot P;
    readonly Rng R;
    readonly CapitalHull.Spec S;
    readonly int Quarter;
    public readonly CapitalHull.HullPlan Result = new();
    CapitalHull.HullPlan H => Result;

    /// <summary>A stretch of the hull as it lies: pitched about its keel point Pivot, which sits at At in the plan's frame.</summary>
    sealed class Pose
    {
        public int V0, V1, OffU, OffV;
        public float Pitch;
        public NV3 Pivot, At;
    }

    readonly List<Pose> Poses = new();

    public Hulk(Plot p, CapitalHull.Spec s, Rng r, int q = 0)
    {
        P = p;
        S = s;
        R = r;
        Quarter = q;
    }

    static Rgb K(Plot p, char symbol, Rgb fallback) => p.C.Kit.BySymbol(symbol) is { } e ? e.Colors[0] : fallback;

    public void Build()
    {
        H.Role = S.Role;
        H.Size = S.Size;
        H.Crash = S.Crash;
        H.Tech = P.Tech;
        H.Seed = R.NextU64();
        H.Faction = S.Faction is { } f && Enum.TryParse(f, true, out Faction named) ? named : P.C.Kit.Faction;
        H.Style = HullStyle.For(HullStyle.Shared, H.Faction, H.Seed);
        Livery();
        Bays();
        Shape();
        Poses_();
        Ways();
        Parts();
        Pieces();
    }

    // ---- what it is made of ---------------------------------------------------------------------------

    void Livery()
    {
        Fab f = P.F;
        Rgb plate = K(P, 'a', f.Wall.C);
        plate = P.Tech switch
        {
            TechLevel.Low => plate.Lerp(new Rgb(0x7a, 0x6a, 0x5a), 0.35f),
            TechLevel.High => plate.Scale(1.04f),
            _ => plate,
        };
        // Outside, the faction's livery (its style); inside, the kit's own walls and floors.
        HullStyle st = H.Style;
        H.Paints["hull"] = P.Tech == TechLevel.Low ? st.Hull.Lerp(new Rgb(0x7a, 0x6a, 0x5a), 0.25f) : st.Hull;
        H.Paints["trim"] = st.Trim;
        H.Paints["wall"] = K(P, '%', f.Panel.C);
        H.Paints["deck"] = K(P, '_', f.Floor.C);
        H.Paints["orange"] = st.Band;
        H.Paints["teal"] = st.Band2;
        H.Paints["machine"] = K(P, 'm', f.Steel.C);
        H.Paints["dark"] = st.Dark;
        H.Paints["hazard"] = f.Hazard;
        H.Paints["black"] = f.Black;
        for (int i = 0; i < st.Cargo.Count; i++) H.Paints["cargo" + i] = st.Cargo[i];
        for (int i = 0; i < st.Scrap.Count; i++) H.Paints["scrap" + i] = st.Scrap[i];
        H.Round = st.Bow == "round" || S.Role == "liner";
    }

    // ---- the bays, stem to stern -----------------------------------------------------------------------

    void Bays()
    {
        var (l0, l1, h0, h1, decks) = CapitalHull.SizeOf(S.Size);
        H.Decks = decks;
        HullStyle st = H.Style;
        H.Half = Math.Max(11, (int)MathF.Round(R.Between(h0, h1) * st.Beam));
        int target = (int)MathF.Round(R.Between(l0, l1) * st.Length);
        // Now and then a huge hull runs on toward the longest Empyrion's derelicts reach.
        if (S.Size == "huge" && R.Chance(0.15f)) target = R.Between(250, 270);
        // Each bay its own proportions about the hull's length; the crew's and the power bays hold a stair core and a full
        // set of rooms beside it on every deck.
        int Len(int share, int lo, int hi) => Math.Clamp(Math.Max(target / share, lo) + R.Range(-3, 6), lo, hi);
        int bow = Len(6, 14, 34), engine = Len(8, 12, 24), power = Len(7, 22, 32), crew = Len(6, 22, 32);
        string[] middle = S.Role switch
        {
            "warship" => new[] { "armoury", "hangar", "barracks", "magazine" },
            "liner" => new[] { "cabins", "promenade", "med" },
            "miner" => new[] { "ore-hold", "refinery" },
            "carrier" => new[] { "hangar", "barracks" },
            _ => new[] { "hold", "hangar" },
        };
        int left = target - bow - crew - power - engine;
        int n = Math.Max(middle.Length, (int)MathF.Round(left / 24f));
        // The first of the role's bays repeats as the hull grows (more holds, more cabins), the rest follow it once each.
        var kinds = Enumerable.Repeat(middle[0], n - middle.Length + 1).Concat(middle.Skip(1)).ToList();
        int v = 0;
        void Add(string kind, int len) { H.Bays.Add(new CapitalHull.Bay(kind, v, v + len)); v += len; }
        Add("bow", bow);
        Add("crew", crew);
        // The role's bays share what is left of the length, each a little longer or shorter than its share.
        for (int i = 0; i < n; i++)
        {
            int share = i == n - 1 ? left - (v - bow - crew) : Math.Max(left / n, 14) + R.Range(-3, 6);
            Add(kinds[i], Math.Clamp(share, 14, 34));
        }
        Add("power", power);
        Add("engine", engine);
        H.L = v;
        H.Bz = H.Bays[0].V1;
        // Segment waists at the heads of the role's bays (never by the crew's or the power bay's stair core), and at their
        // middles when there are too few heads.
        var mids = H.Bays.Where(b => b.Kind is not ("bow" or "crew" or "power" or "engine")).ToList();
        var at = mids.Skip(1).Select(b => b.V0).Concat(mids.Where(b => b.V1 - b.V0 >= 24).Select(b => (b.V0 + b.V1) / 2)).Distinct().OrderBy(x => x).ToList();
        for (int i = 0; i < Math.Min(st.Segments, at.Count); i++) H.Waists.Add(at[(int)((i + 0.5f) * at.Count / Math.Min(st.Segments, at.Count))]);
    }

    void Shape()
    {
        // The tower: forward over the crew for a warship, a liner or a carrier, aft over the power for a hauler.
        bool aft = S.Role is "freighter" or "miner";
        CapitalHull.Bay bay = H.Bays.First(b => b.Kind == (aft ? "power" : "crew"));
        // Wide enough for the stair core coming up through it beside the spine (u 3 to 7, walled), and from the bay's head,
        // so the core's landing (4 m in) is clear of the crown's rounding over the tower's front.
        // The style's lopsided crown shifts it to starboard (its port edge stays over the spine, for the stair core's landing).
        int tw = Math.Clamp((int)(H.Half * 0.6f), 9, Math.Max(9, H.Half - H.Shoulder));
        int off = Math.Min(tw - 1, (int)MathF.Round(H.Style.TowerOffset * tw));
        H.TU0 = -tw + off;
        H.TU1 = tw + off;
        H.TV0 = bay.V0;
        H.TV1 = Math.Min(bay.V1 - 2, H.TV0 + Math.Clamp(H.Half + R.Range(-2, 3), 14, 22));
        // The engine bank off the stern: the style's count across the beam, its length and its height.
        HullStyle st = H.Style;
        int n = R.Between(st.EnginesMin, st.EnginesMax), span = (2 * H.Half + 1 - 6) / n, w = Math.Max(3, Math.Min(span - 1, 3 + span * 2 / 3));
        for (int i = 0; i < n; i++)
        {
            int c = -H.Half + 3 + span * i + span / 2;
            H.Nacelles.Add((c - w / 2, c - w / 2 + w - 1));
        }
        H.Nac = Math.Clamp((int)MathF.Round((H.L / 12 + R.Range(-1, 3)) * st.EngineLength), 8, 34);
        H.NY0 = 2;
        H.NY1 = Math.Clamp((int)MathF.Round(H.Top * st.EngineHeight), H.NY0 + 4, H.Top - 2);
    }

    // ---- how it came down ------------------------------------------------------------------------------

    /// <summary>
    /// How it came down, as a real pose: each piece of the hull a rigid body pitched about its keel and sunk into
    /// the ground, the way the vessel structure is set down. Rise is the keel's climb a metre aft; the pivot (a
    /// keel point) is set at a depth under the ground.
    /// </summary>
    void Poses_()
    {
        int u0 = 0, v0 = 24, L = H.L;
        Pose Make(int a, int b, int pivotV, float rise, float depth, int du = 0, int dv = 0) => new()
        {
            V0 = a, V1 = b, OffU = u0 + du, OffV = v0 + dv, Pitch = -MathF.Atan(rise),
            Pivot = new NV3(0, -0.5f, pivotV), At = new NV3(u0 + du + 0.5f, depth, v0 + dv + pivotV + 0.5f),
        };
        switch (S.Crash)
        {
            case "nose":
                // Nose down: the bow ploughed in deep, the stern up on it at about four degrees, the bow crushed.
                Poses.Add(Make(0, L, 0, 0.075f, -9f - R.Range(0, 2)));
                H.Impact = H.Bz / 2;
                H.Crush = H.Bz;
                break;
            case "scattered":
            {
                // Broken apart: the fore hull lies nearly level where it stopped, the stern section with the hangar,
                // the power and the engines tumbled further on down the furrow, the cargo strewn between.
                CapitalHull.Bay bay = H.Bays.FirstOrDefault(b => b.Kind == "hangar") ?? H.Bays.Last(b => b.Hall && b.Kind != "engine");
                H.Cut = bay.V0;
                int gap = 34 + 2 * R.Range(0, 5), side = R.Chance(0.5f) ? 1 : -1;
                Poses.Add(Make(0, H.Cut, H.Cut, 0.03f, -2f));
                Poses.Add(Make(H.Cut + 2, L, H.Cut + 2, -0.05f, -2f, side * (6 + 2 * R.Range(0, 3)), gap));
                H.Impact = H.Cut;
                H.Crush = 10;
                break;
            }
            case "long":
                // A long hauler that came down flat on its belly and slid: level, buried along its whole length at the
                // end of a long furrow (level is also what lets it lift off without levelling through the ground).
                Poses.Add(Make(0, L, L, 0f, -7f));
                break;
            default:
            {
                // Broken back: it came down on its belly and its keel gave amidships, both halves sunk at the break
                // with the hull torn open over it, bow and stern up either side.
                int mid = H.Bays.Where(b => b.Hall && b.Kind != "engine").Select(b => (b.V0 + b.V1) / 2).OrderBy(m => Math.Abs(m - L / 2)).FirstOrDefault(L / 2);
                H.Break = mid;
                Poses.Add(Make(0, mid, mid, -0.07f, -6f));
                Poses.Add(Make(mid, L, mid, 0.07f, -6f));
                H.Impact = mid;
                H.Crush = 12;
                break;
            }
        }
    }

    /// <summary>A point of the hull's frame where it lies in the plan's frame.</summary>
    static NV3 ToPlot(Pose p, NV3 ship) => p.At + NV3.Transform(ship - p.Pivot, NQ.CreateFromAxisAngle(NV3.UnitX, p.Pitch));

    static NV3 FromPlot(Pose p, NV3 plot) => p.Pivot + NV3.Transform(plot - p.At, NQ.CreateFromAxisAngle(NV3.UnitX, -p.Pitch));

    Pose? PoseOf(int v)
    {
        v = Math.Clamp(v, 0, H.L - 1);
        return Poses.FirstOrDefault(p => v >= p.V0 && v < p.V1);
    }

    // ---- the ways in -----------------------------------------------------------------------------------

    bool NearTear(int v, int margin) => H.Break >= 0 && Math.Abs(v - H.Break) < 6 + margin || H.Cut >= 0 && v >= H.Cut - 4 - margin && v < H.Cut + 6 + margin;

    void Ways()
    {
        // The hull door: through the starboard flank into the crew bay's side passage, the way a boarding party comes in.
        CapitalHull.Bay crew = H.Bays.First(b => b.Kind == "crew");
        H.DoorV = crew.V0 + 4;
        // The cargo ramp: down from a hangar or a hold onto the ground, its bay door open.
        CapitalHull.Bay cargo = H.Bays.FirstOrDefault(b => b.Kind == "hangar" && (H.Cut < 0 || b.V0 >= H.Cut))
                                ?? H.Bays.Last(b => b.Hall && b.Kind != "engine");
        int side = R.Chance(0.5f) ? 1 : -1;
        int ov = Enumerable.Range(cargo.V0 + 3, Math.Max(1, cargo.V1 - cargo.V0 - 10)).FirstOrDefault(v => !NearTear(v, 2), cargo.V0 + 3);
        H.Ways.Add(new CapitalHull.Way(side, ov, 6, 6, "cargo"));
        switch (S.Crash)
        {
            case "nose":
            {
                // Where the hull meets the ground, the crash tore its flank open.
                CapitalHull.Bay b = H.Bays.First(x => x.Hall && x.Kind != "engine");
                H.Ways.Add(new CapitalHull.Way(-side, b.V0 + 3, 4, 5, "breach"));
                break;
            }
            case "long":
            {
                CapitalHull.Bay b = H.Bays.First(x => x.Hall && x.Kind != "engine");
                H.Ways.Add(new CapitalHull.Way(-side, b.V0 + 4, 6, 4, "breach"));
                break;
            }
            case "broken":
                foreach (int s in new[] { 1, -1 }) H.Ways.Add(new CapitalHull.Way(s, H.Break - 2, 4, 5, "break"));
                break;
            case "scattered":
                H.Ways.Add(new CapitalHull.Way(0, H.Cut - 1, 0, 0, "fore-face"));
                H.Ways.Add(new CapitalHull.Way(0, H.Cut + 2, 0, 0, "aft-face"));
                break;
        }
    }

    // ---- what the faction's style adds outside ---------------------------------------------------------

    /// <summary>
    /// What the style adds outside the body, all by its numbers (per 100 m of hull): cargo pod stacks on the flanks and
    /// the back, bolted-on modules, scrap plates welded over the shell, spikes, a ram's keel blade, swept fins built in
    /// short straight runs (the 1 m grid's curve), a truss down the spine; each mirrored for a symmetric style. Never over
    /// a way in, the flank jets or the tower; the bow's taper is the bow's own (the silhouette's nose).
    /// </summary>
    void Parts()
    {
        HullStyle st = H.Style;
        float per = H.L / 100f;
        int Count(float rate) => (int)MathF.Round(rate * per * R.Float(0.8f, 1.2f));
        int v0 = H.Bz + 2, v1 = H.L - 4;
        bool Clear(float a, float b) => !(H.DoorV >= 0 && b >= H.DoorV - 3 && a <= H.DoorV + 5)
            && !H.Ways.Any(w => w.Side != 0 && b >= w.V0 - 2 && a <= w.V0 + w.Width + 2)
            && !(b >= H.Bz && a <= H.Bz + 8);
        bool OffTower(float a, float b) => b < H.TV0 - 1 || a > H.TV1 + 1;
        void Add(CapitalHull.Part part)
        {
            H.Parts.Add(part);
            if (st.Symmetric && MathF.Abs(part.U0 + part.U1) > 0.01f) H.Parts.Add(part.Mirrored);
        }
        // The flank's face over a stretch (the narrowest of it), and where the roof is flat across.
        float Face(float a, float b) => Enumerable.Range((int)a, Math.Max(1, (int)(b - a) + 1)).Min(v => H.HalfWidth(v));
        float Flat(float a, float b) => Face(a, b) - H.Shoulder - 1f;
        int Side() => st.Symmetric ? 1 : R.Chance(0.5f) ? 1 : -1;
        string Scrap() => st.Scrap.Count > 0 ? "scrap" + R.Range(0, st.Scrap.Count) : "trim";
        CapitalHull.Part Flank(string paint, int side, float a, float b, float y0, float y1, float depth, int axis = -1, float tip = 1f)
        {
            float f = Face(a, b) - 1f;
            return side > 0 ? new(paint, f, y0, a, f + depth, y1, b, axis, tip) : new(paint, -f - depth, y0, a, -f, y1, b, axis < 0 ? axis : axis ^ 1, tip);
        }

        // Cargo pods, stacked two or three high on a flank or a row across the back, each its own colour (colour-coded).
        for (int i = 0, n = Count(st.Pods); i < n; i++)
        {
            int a = v0 + 9 * R.Range(0, Math.Max(1, (v1 - v0 - 8) / 9)), b = a + 8;
            if (!Clear(a, b)) continue;
            if (R.Chance(0.35f) && OffTower(a, b))
            {
                float w = Flat(a, b);
                for (float u = -w + 1; u + 5 <= w - 1; u += 6)
                    if (MathF.Abs(u + 2.5f) > H.Style.SpineWidth + 2) Add(new("cargo" + R.Range(0, Math.Max(1, st.Cargo.Count)), u, H.Top, a, u + 5, H.Top + 5 + 5 * R.Range(0, 2), b));
                continue;
            }
            int side = Side();
            for (float y0 = 1; y0 + 5 <= H.Top + 2; y0 += 5.5f)
                Add(Flank("cargo" + R.Range(0, Math.Max(1, st.Cargo.Count)), side, a, b, y0, y0 + 5, R.Between(6, 9)));
        }
        // Bolted-on modules: a box of whatever there was, on a flank or the back, never two alike.
        for (int i = 0, n = Count(st.Bolted); i < n; i++)
        {
            int len = R.Between(6, 18), a = R.Range(v0, Math.Max(v0 + 1, v1 - len)), b = a + len;
            if (!Clear(a, b)) continue;
            if (R.Chance(0.35f) && OffTower(a, b))
            {
                float w = Flat(a, b), du = R.Between(4, 10);
                float u = R.Float(-w + 1, Math.Max(-w + 1.5f, w - 1 - du));
                Add(new(Scrap(), u, H.Top, a, u + du, H.Top + R.Between(3, 8), b));
                continue;
            }
            int dy = R.Between(5, 11);
            float y0 = R.Between(1, Math.Max(1, H.Top - dy));
            Add(Flank(Scrap(), Side(), a, b, y0, y0 + dy, R.Between(4, 9)));
        }
        // Scrap plates welded over the shell, a metre proud, mismatched.
        for (int i = 0, n = Count(st.Plates * 40f); i < n; i++)
        {
            int len = R.Between(3, 8), a = R.Range(v0, Math.Max(v0 + 1, v1 - len)), b = a + len;
            if (!Clear(a, b)) continue;
            int dy = R.Between(2, 5);
            float y0 = R.Between(2, Math.Max(2, H.Top - H.Shoulder - dy));
            Add(Flank(Scrap(), Side(), a, b, y0, y0 + dy, 1.5f));
        }
        // Spikes out of the flanks and the back, tapering to a point.
        for (int i = 0, n = Count(st.Spikes); i < n; i++)
        {
            float v = R.Range(v0, v1), r = R.Float(1.4f, 2.4f), len = R.Between(6, 13);
            if (!Clear(v - r, v + r)) continue;
            if (R.Chance(0.35f) && OffTower(v - r, v + r))
            {
                float u = R.Float(-Flat(v - r, v + r), Flat(v - r, v + r));
                Add(new("dark", u - r, H.Top, v - r, u + r, H.Top + len, v + r, 2, 0.12f));
                continue;
            }
            float y = R.Float(3f, H.Top - H.Shoulder - 2f);
            Add(Flank("dark", Side(), v - r, v + r, y - r, y + r, len, 0, 0.12f));
        }
        // A ram's keel blade, out ahead under the raked prow.
        if (st.Bow == "ram") Add(new("dark", -3f, 1f, 0f, 3f, H.Top * 0.55f, H.Bz, 5, 0.1f));
        // Swept fins: a blade stepping out, back and up or down in short straight runs, each its own length and side.
        for (int i = 0; i < st.Fins; i++)
        {
            int side = i % 2 == 0 ? 1 : -1, steps = R.Between(6, 10);
            float v = R.Range(v0, Math.Max(v0 + 1, v1 - 4 * steps)), y = R.Float(H.Top * 0.25f, H.Top * 0.7f), u = Face(v, v + 4) - 1f, dy = R.Chance(0.5f) ? 1f : -1f;
            if (!Clear(v, v + 4 * steps)) continue;
            for (int k = 0; k < steps && y > 1 && y < H.TowerTop; k++)
            {
                Add(side > 0 ? new("trim", u, y, v, u + 3, y + 3, v + 4) : new("trim", -u - 3, y, v, -u, y + 3, v + 4));
                u += 2;
                v += 3;
                y += dy * (1 + k / 3);
            }
        }
        // A superstructure down the back, a long block either side of the tower (a big spine's: the corporate hull's).
        if (st.Spine >= 3f)
        {
            float w = st.SpineWidth + 2, top = H.Top + st.Spine + R.Between(3, 5);
            if (H.TV0 - 2 > v0 + 6) Add(new("trim", -w, H.Top, v0 + 2, w, top, H.TV0 - 2));
            if (H.TV1 + 2 < v1 - 8) Add(new("trim", -w, H.Top, H.TV1 + 2, w, top - 1, v1 - 6));
        }
        // A truss down the spine: two rails, posts every six metres and the bars across between them.
        if (st.Lattice)
        {
            float w = H.Style.SpineWidth + 2, top = H.Top + H.Style.Spine + 4;
            for (int k = 0; k < 2; k++)
            {
                float u = k == 0 ? -w : w;
                Add(new("machine", u - 0.5f, top, v0 + 2, u + 0.5f, top + 1, v1 - 2));
            }
            for (int v = v0 + 2; v < v1 - 2; v += 4)
            {
                if (!OffTower(v - 1, v + 1)) continue;
                Add(new("machine", -w - 0.5f, H.Top, v - 0.5f, -w + 0.5f, top, v + 0.5f));
                Add(new("machine", -w, top, v - 0.5f, w, top + 1, v + 0.5f));
            }
        }
        H.ExtentU = H.Half + 2 + (st.Ribs > 0 ? st.Rib : 0);
        H.ExtentY = H.TowerTop + 2;
        foreach (CapitalHull.Part q in H.Parts)
        {
            H.ExtentU = Math.Max(H.ExtentU, (int)MathF.Ceiling(MathF.Max(MathF.Abs(q.U0), MathF.Abs(q.U1))) + 2);
            H.ExtentY = Math.Max(H.ExtentY, (int)MathF.Ceiling(q.Y1) + 2);
        }
    }

    // ---- the vessel's pieces ---------------------------------------------------------------------------

    void Pieces()
    {
        foreach (Pose p in Poses)
        {
            var piece = new CapitalHull.HullPiece
            {
                Name = Poses.Count == 1 ? "" : p == Poses[0] ? " (fore)" : " (aft)",
                V0 = p.V0, V1 = p.V1, Last = p == Poses[^1],
            };
            // Where it lies: its structure's origin (the middle of the hull's cell 0, 0, V0) and its turn, in the place's frame.
            NV3 plot = ToPlot(p, new NV3(0, 0, p.V0));
            var turn = NQ.CreateFromAxisAngle(NV3.UnitY, -Quarter * MathF.PI / 2f);
            piece.Origin = Turn(plot, Quarter);
            piece.Rotation = NQ.Normalize(turn * NQ.CreateFromAxisAngle(NV3.UnitX, p.Pitch));
            H.Pieces.Add(piece);
        }
    }

    /// <summary>A point of the plan's frame (u along its front, v back from it) in the place's own frame, its front turned q quarters.</summary>
    static NV3 Turn(NV3 p, int q) => q switch
    {
        1 => new(-p.Z, p.Y, p.X),
        2 => new(-p.X, p.Y, -p.Z),
        3 => new(p.Z, p.Y, -p.X),
        _ => p,
    };

    // ---- setting it down -------------------------------------------------------------------------------

    /// <summary>
    /// The place round the vessel: the ground cut away where the hull lies in it (open to the sky over it, as it came
    /// down, and wide enough for it to level as it lifts: the lift-off's margins are its lever arm times its tilt),
    /// the ramps and cuttings up to its ways in, the yard you arrive at, a heap of wreckage with the best of what was
    /// thrown clear, and the debris down the furrow. The vessel itself is set down by the world. Nobody waits inside:
    /// a wreck's danger is outside it (place-patterns 2.3).
    /// </summary>
    public void EmitPlace()
    {
        var plot = P;
        foreach (Pose p in Poses)
        {
            bool last = p == Poses[^1];
            int v1 = last ? H.L + H.Nac : p.V1;
            float tilt = MathF.Abs(p.Pitch), arm = (v1 - p.V0) * 0.5f;
            NV3 middle = ToPlot(p, new NV3(0, H.Top * 0.5f, (p.V0 + v1) * 0.5f));
            var corners = new List<NV3>();
            foreach (float u in new[] { -H.ExtentU - 0.5f, H.ExtentU + 0.5f })
                foreach (float y in new[] { -0.5f, H.ExtentY + 0.5f })
                    foreach (float v in new[] { p.V0 - 0.5f, v1 - 0.5f }) corners.Add(ToPlot(p, new NV3(u, y, v)));
            float grow = arm * MathF.Sin(tilt) + 1f;
            int x0 = (int)MathF.Floor(corners.Min(c => c.X) - grow), x1 = (int)MathF.Ceiling(corners.Max(c => c.X) + grow);
            int y0 = (int)MathF.Floor(corners.Min(c => c.Y) - grow), z0 = (int)MathF.Floor(corners.Min(c => c.Z) - grow), z1 = (int)MathF.Ceiling(corners.Max(c => c.Z) + grow);
            if (y0 >= 0) continue;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    // Up the column from the deepest the hull reaches: once the hull (or the room it needs to level) is met,
                    // every cell over it is open to the sky.
                    bool open = false;
                    for (int y = y0; y < 0; y++)
                    {
                        if (!open)
                        {
                            var c = new NV3(x + 0.5f, y + 0.5f, z + 0.5f);
                            float margin = tilt > 0.01f ? (c - middle).Length() * MathF.Sin(tilt) : 0f;
                            open = Near(p, c, margin + 0.45f, last);
                        }
                        if (open) plot.Air(x, y, z);
                    }
                }
        }
        // The ways in from the ground: ramps up, cuts down.
        var clear = new HashSet<(int, int)>();
        var outside = new List<int>();
        int yardU = int.MinValue, yardV = 0, walk0 = CapitalHull.HullPlan.Walk(0);
        foreach (CapitalHull.Way w in H.Ways)
        {
            if (PoseOf(w.V0) is not { } p) continue;
            if (w.Side == 0)
            {
                NV3 face = ToPlot(p, new NV3(0, walk0, w.V0));
                int fv = (int)MathF.Round(face.Z), fu = (int)MathF.Round(face.X), dir = w.What == "fore-face" ? 1 : -1;
                for (int k = -2; k < 10; k++)
                    for (int u = fu - H.Half - 2; u < fu + H.Half + 2; u++) clear.Add((u, fv + dir * k));
                if (yardU == int.MinValue) { yardU = fu + H.Half + 6; yardV = fv + dir * 6; }
                continue;
            }
            NV3 sill = ToPlot(p, new NV3(w.Side * (H.HalfWidth(w.V0) + 0.5f), walk0 - 0.5f, w.V0 + w.Width / 2f - 0.5f));
            int walk = (int)MathF.Round(sill.Y);
            int s = w.Side, uo = (int)MathF.Floor(sill.X) + (s > 0 ? 0 : -1), vs = (int)MathF.Round(sill.Z) - w.Width / 2;
            var cells = new List<(int, int, int)>();
            int n = Math.Abs(walk);
            Rgb trim = H.Paints["trim"];
            for (int i = 0; i < n; i++)
            {
                int u = uo + s * i;
                for (int v = vs; v < vs + w.Width; v++)
                {
                    if (walk > 0)
                    {
                        int y = walk - 1 - i;
                        plot.Put(u, y, v, new Block(BlockType.Ramp, Material.Steel, trim.Scale(1.1f), Role.Floor, s > 0 ? Dir.West : Dir.East));
                        for (int fy = 0; fy < y; fy++) plot.Put(u, fy, v, new Block(BlockType.Cube, Material.Steel, trim.Scale(0.8f), Role.Wall));
                        cells.Add((u, y, v));
                        for (int up = 1; up <= 3; up++) cells.Add((u, y + up, v));
                    }
                    else
                    {
                        int y = walk + i;
                        plot.Put(u, y, v, new Block(BlockType.Ramp, Material.Rock, new Rgb(0x5a, 0x52, 0x48), Role.Floor, s > 0 ? Dir.East : Dir.West));
                        // A bank of rock under it, down through the room the hull was given to level in.
                        for (int fy = y - 1; fy >= y - 12; fy--) plot.Put(u, fy, v, new Block(BlockType.Cube, Material.Rock, new Rgb(0x5a, 0x52, 0x48), Role.Wall));
                        cells.Add((u, y, v));
                        for (int up = y + 1; up < Math.Max(0, y + 4); up++) { if (up < 0) plot.Air(u, up, v); cells.Add((u, up, v)); }
                    }
                }
            }
            for (int k = -2; k < n + 6; k++)
                for (int v = vs - 3; v < vs + w.Width + 3; v++) clear.Add((uo + s * k, v));
            if (cells.Count > 0) outside.Add(plot.RoomOf(ModuleKind.Outdoor, walk > 0 ? "cargo-ramp" : "cutting", 0, cells.Distinct()));
            if (w.What == "cargo" || yardU == int.MinValue) { yardU = uo + s * (n + 6); yardV = vs + w.Width / 2; }
        }
        plot.Approach(Div2(yardU), Div2(yardV), Dir.North);
        for (int du = -4; du <= 4; du++) for (int dv = -4; dv <= 4; dv++) clear.Add((yardU + du, yardV + dv));
        // A heap of what was thrown clear, by the yard: the crates that came out of the holds, the best of them.
        int hu = yardU + (yardU > 0 ? 6 : -6), hv = yardV + 6;
        var heap = new List<(int, int, int)>();
        for (int dv = -3; dv <= 3; dv++)
            for (int du = -3; du <= 3; du++)
                for (int y = 0; y < 3; y++) heap.Add((hu + du, y, hv + dv));
        int wreckage = plot.RoomOf(ModuleKind.Vault, "wreckage", 0, heap);
        plot.Yards(outside.Append(wreckage).ToArray());
        Rgb[] crates = { H.Paints["orange"], H.Paints["teal"], H.Paints["hazard"] };
        foreach (var (du, dv) in new[] { (-3, -3), (3, -3), (-3, 3), (3, 3), (0, 3) })
            plot.Prop(hu + du, 0, hv + dv, BlockType.Crate, Material.Scrap, crates[(du + dv + 9) % 3].Scale(0.8f));
        plot.Cache(hu, 0, hv, wreckage);
        plot.Cache(hu + 2, 0, hv - 2, wreckage);
        plot.Spawn(hu - 2, 0, hv, wreckage);
        plot.Spawn(yardU, 0, yardV + 2, plot.Yard);
        for (int du = -5; du <= 5; du++) for (int dv = -5; dv <= 5; dv++) clear.Add((hu + du, hv + dv));
        plot.C.VaultModule = wreckage;
        Debris(clear);
    }

    /// <summary>Is a point of the plan's frame within a margin of the hull as this piece lies?</summary>
    bool Near(Pose p, NV3 plot, float margin, bool last)
    {
        NV3 s = FromPlot(p, plot);
        if (s.Z < p.V0 - 0.5f - margin || s.Z > (last ? H.L + H.Nac : p.V1) - 0.5f + margin) return false;
        if (H.Solid(s.X, s.Y, s.Z)) return true;
        if (margin <= 0f) return false;
        for (int k = 0; k < 6; k++)
        {
            NV3 d = k switch { 0 => NV3.UnitX, 1 => -NV3.UnitX, 2 => NV3.UnitY, 3 => -NV3.UnitY, 4 => NV3.UnitZ, _ => -NV3.UnitZ };
            NV3 q = s + d * margin;
            if (q.Z >= p.V0 - 0.5f && q.Z <= (last ? H.L + H.Nac : p.V1) - 0.5f && H.Solid(q.X, q.Y, q.Z)) return true;
        }
        return false;
    }

    static int Div2(int a) => a >= 0 ? a / 2 : (a - 1) / 2;

    /// <summary>Is the plan's ground cell (u, v) under the hull, or within two metres of it (nothing is laid there: the vessel stands there)?</summary>
    bool UnderHull(int u, int v)
    {
        foreach (Pose p in Poses)
        {
            bool last = p == Poses[^1];
            for (float y = -16f; y <= H.TowerTop; y += 2f)
            {
                NV3 s = FromPlot(p, new NV3(u + 0.5f, y, v + 0.5f));
                if (s.Z < p.V0 - 2.5f || s.Z > (last ? H.L + H.Nac + 3 : p.V1 + 1)) continue;
                float vz = Math.Clamp(s.Z, 0f, H.L - 1f);
                if (MathF.Abs(s.X) <= H.ExtentU + 4 && (s.Z >= H.L || MathF.Abs(s.X) <= H.HalfWidth(vz) + 2f || s.Z >= H.Bz && MathF.Abs(s.X) <= H.ExtentU + 2)) return true;
            }
        }
        return false;
    }

    /// <summary>What the crash threw about: torn plates, a spill of cargo, scrap down the furrow behind it.</summary>
    void Debris(HashSet<(int, int)> clear)
    {
        var rng = R.Fork("debris");
        Pose last = Poses[^1], first = Poses[0];
        int stern = H.L + H.Nac + last.OffV, half = H.Half;
        Rgb plate = H.Paints["hull"];
        Rgb[] cargo = { H.Paints["orange"], H.Paints["teal"], H.Paints["hazard"] };
        var spots = new List<(int U, int V, int R)>();
        // Down the furrow behind it.
        for (int i = 0; i < 26; i++) spots.Add((last.OffU + rng.Range(-half, half), stern + 6 + rng.Range(0, 60), 2));
        // Along both flanks.
        for (int i = 0; i < 24; i++)
        {
            int v = rng.Range(0, H.L);
            if (PoseOf(v) is not { } p) continue;
            int s = rng.Chance(0.5f) ? 1 : -1;
            spots.Add((p.OffU + s * (half + 3 + rng.Range(0, 18)), v + p.OffV, 1));
        }
        // Where it broke: the spill between the pieces, or out of the broken back.
        if (H.Cut >= 0)
            for (int i = 0; i < 30; i++) spots.Add((first.OffU + rng.Range(-half, half), H.Cut + first.OffV + rng.Range(2, last.OffV - first.OffV), 3));
        if (H.Break >= 0)
            for (int i = 0; i < 20; i++) spots.Add((first.OffU + (rng.Chance(0.5f) ? 1 : -1) * (half + 2 + rng.Range(0, 14)), H.Break + first.OffV + rng.Range(-12, 12), 3));
        foreach (var (u, v, kind) in spots)
        {
            int n = 1 + rng.Range(0, kind + 1);
            for (int k = 0; k < n; k++)
            {
                int cu = u + rng.Range(-2, 3), cv = v + rng.Range(-2, 3);
                if (clear.Contains((cu, cv)) || P.C.Occ.Contains(P.Cell(cu, 0, cv)) || P.C.Blocks.ContainsKey(P.Cell(cu, 0, cv)) || UnderHull(cu, cv)) continue;
                if (P.C.Occ.Contains(P.Cell(cu, -1, cv))) continue;
                int pick = rng.Range(0, 10);
                Block b = pick switch
                {
                    < 3 => new Block(BlockType.Ramp, Material.Steel, plate.Scale(rng.Float(0.6f, 0.9f)), Role.Debris, Dirs.Horizontal[rng.Range(0, 4)]),
                    < 5 => new Block(BlockType.Corner, Material.Steel, plate.Scale(rng.Float(0.5f, 0.85f)), Role.Debris, Dirs.Horizontal[rng.Range(0, 4)]),
                    < 7 => new Block(BlockType.Rubble, Material.Scrap, new Rgb(0x5a, 0x50, 0x48), Role.Debris, Dir.North, (byte)rng.Range(0, 4)),
                    < 9 when kind == 3 => new Block(BlockType.Cube, Material.Steel, cargo[rng.Range(0, 3)].Scale(0.8f), Role.Debris),
                    _ => new Block(BlockType.Crate, Material.Scrap, new Rgb(0x6a, 0x5a, 0x48), Role.Debris, Dirs.Horizontal[rng.Range(0, 4)], 1),
                };
                P.Lay(cu, 0, cv, b);
            }
        }
    }
}
