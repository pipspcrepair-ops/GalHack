using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GalHack.Items;

/// <summary>
/// The states a decoration can be found in. Intact is the one players build; the rest dress POIs and
/// Creative. Knocked is tipped over (a vehicle on its roof or side), Broken is smashed (a vehicle
/// crushed), Rusted is long abandoned (rust, flat tyres, grass through the cracks), Open has its
/// doors flung open or gone and its glass broken, Buried is half under drifted sand or snow.
/// </summary>
public enum DecoState { Intact, Knocked, Broken, Burnt, Overgrown, Rusted, Open, Buried }

/// <summary>How advanced a place was: frontier colonies of dirt roads and diesel, ordinary worlds, high-tech ones of maglevs and skyways.</summary>
public enum DecoTech { Low, Mid, High }

/// <summary>Where a decoration goes: on the floor, on a wall, hanging from the ceiling, or on top of another piece (a monitor on a desk).</summary>
public enum DecoMount { Floor, Wall, Ceiling, Top }

/// <summary>Which way a knocked-over piece falls, in its own frame (its front faces -Z).</summary>
public enum DecoFall { None, Back, Front, Left, Right, Over }

/// <summary>
/// A deco piece's tech-tree node on one grid: the same shape as the slice's block unlocks
/// (<c>level</c>, <c>cost</c>, <c>parent</c>, <c>tab</c>), so the two merge into one tree.
/// </summary>
public sealed record DecoUnlock(int Level, int Cost, string Parent, string Tab);

/// <summary>A deco piece that gives off light: its reach in metres and its strength.</summary>
public sealed record DecoLight(float Range, float Energy);

/// <summary>A theme set (Empyrion's deco block groups): a family of pieces that furnish one kind of room.</summary>
public sealed class DecoSet
{
    public string Id { get; init; }
    public string Name { get; init; }
    /// <summary>The palette its pieces are coloured with unless a piece names its own.</summary>
    public string Palette { get; init; }
    /// <summary>The set's tech-tree defaults; each piece may raise its level or cost.</summary>
    public int Level { get; init; } = 1;
    public int Cost { get; init; }
    public string Tab { get; init; } = "deco";
    /// <summary>Glows need no power (Precursor glyphs, alien resin): they stay lit when a place loses power.</summary>
    public bool SelfLit { get; init; }
    /// <summary>Outdoor pieces (vehicles, trains, airliners): no room-height limit, and their arrangements are sites, not rooms.</summary>
    public bool Outdoor { get; init; }
    /// <summary>Its pieces' tech level unless a piece says otherwise.</summary>
    public DecoTech Tech { get; init; } = DecoTech.Mid;
    /// <summary>The damaged states its pieces have unless a piece lists its own.</summary>
    public IReadOnlyList<DecoState> States { get; init; }
    /// <summary>Broken means crushed: everything above the waist is flattened (vehicles).</summary>
    public bool Crush { get; init; }
    public string Blurb { get; init; }
    public override string ToString() => Id;
}

/// <summary>
/// One decoration: a deco block in the shared block catalogue. Its footprint is given in small-grid
/// cells (0.5 m) so it can be planned finely in a room; on the large grid (2 m) it takes
/// <see cref="LargeCells"/>, rounded up, as Empyrion's deco blocks take whole base blocks.
/// </summary>
public sealed class DecoPiece
{
    public const float SmallCell = 0.5f, LargeCell = 2f;

    public string Id { get; init; }
    public string Name { get; init; }
    public string Set { get; init; }
    /// <summary>Other sets it also furnishes (an office chair in a lab).</summary>
    public IReadOnlyList<string> Also { get; init; } = Array.Empty<string>();
    /// <summary>What it is in a room (desk, chair, bed, rack...): arrangement templates and POI props ask for roles.</summary>
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    /// <summary>Footprint in small-grid cells: x is width, y height, z depth (its front faces -Z).</summary>
    public int SizeX { get; init; }
    public int SizeY { get; init; }
    public int SizeZ { get; init; }
    public float Width => SizeX * SmallCell;
    public float Height => SizeY * SmallCell;
    public float Depth => SizeZ * SmallCell;
    /// <summary>Cells it takes on the large grid: its small-grid size rounded up to whole 2 m cells.</summary>
    public (int X, int Y, int Z) LargeCells => ((SizeX + 3) / 4, (SizeY + 3) / 4, (SizeZ + 3) / 4);
    /// <summary>Which building grids it can be placed on. Most deco is large-grid (bases and capital ships), as in Empyrion.</summary>
    public bool OnLarge { get; init; } = true;
    public bool OnSmall { get; init; }
    public DecoMount Mount { get; init; }
    /// <summary>Wall pieces: height of the piece's bottom above the floor.</summary>
    public float MountY { get; init; }
    /// <summary>Tables, desks, benches and counters: the height of the top other pieces stand on (0 if none).</summary>
    public float Surface { get; init; }
    /// <summary>Flat enough to step over (a dropped tray, a rail): it never blocks a walkway.</summary>
    public bool WalkOver { get; init; }
    /// <summary>The states it can be found in (always including <see cref="DecoState.Intact"/>).</summary>
    public IReadOnlyList<DecoState> States { get; init; }
    /// <summary>Directions it may fall when knocked over (empty if it is bolted down).</summary>
    public IReadOnlyList<DecoFall> Falls { get; init; } = Array.Empty<DecoFall>();
    public Bill Bill { get; init; } = new();
    /// <summary>Needs a unique find (Precursor pieces): salvage only, no path from ore.</summary>
    public bool UniqueOnly { get; init; }
    public DecoUnlock UnlockLarge { get; init; }
    public DecoUnlock UnlockSmall { get; init; }
    /// <summary>Light it gives off while powered, or null.</summary>
    public DecoLight Light { get; init; }
    /// <summary>Animation: flicker (screens, lamps), blink (status lights), pulse (alien and Precursor glows), spark (when broken), or null.</summary>
    public string Anim { get; init; }
    /// <summary>How advanced: frontier (low), ordinary (mid) or high tech.</summary>
    public DecoTech Tech { get; init; } = DecoTech.Mid;
    /// <summary>Vehicles and wrecks: salvageable for scrap, with a rough yield class (light, medium, heavy, huge), or null.</summary>
    public string Salvage { get; init; }
    public bool Outdoor { get; init; }
    /// <summary>Its palette, if not its set's (the alien looks).</summary>
    public string Palette { get; init; }
    public int Hp { get; init; }
    public float MassKg { get; init; }
    public int Index { get; internal set; }

    public bool Has(DecoState s) => States.Contains(s);
    public bool Has(string role) => Roles.Contains(role);
    public bool InSet(string set) => Set == set || Also.Contains(set);
    public bool Fixed => Falls.Count == 0;
    public DecoUnlock UnlockFor(bool smallGrid) => smallGrid ? UnlockSmall : UnlockLarge;

    /// <summary>A piece's tech-tree node id on a grid, as the slice writes block nodes: <c>office-desk@large</c>.</summary>
    public string NodeId(bool smallGrid) => $"{Id}@{(smallGrid ? "small" : "large")}";

    /// <summary>The block id of a damaged or knocked-over state (Creative and POIs only): <c>office-desk.broken</c>.</summary>
    public string BlockId(DecoState s) => s == DecoState.Intact ? Id : $"{Id}.{s.ToString().ToLowerInvariant()}";

    public override string ToString() => Id;
}

/// <summary>
/// The decorations catalogue (shared/Items/data/deco.json): theme sets, their palettes and every
/// deco piece with its size, bill, unlock, states and roles. Load throws on anything malformed;
/// <see cref="Validate"/> lists the softer rules the tests hold it to.
/// </summary>
public sealed class DecoCatalogue
{
    public const string FileName = "deco.json";
    public const string Schema = "galhack.deco";

    readonly List<DecoPiece> _pieces = new();
    readonly Dictionary<string, DecoPiece> _byId = new(StringComparer.Ordinal);
    readonly List<DecoSet> _sets = new();
    readonly Dictionary<string, DecoSet> _setById = new(StringComparer.Ordinal);
    readonly Dictionary<string, IReadOnlyDictionary<string, string>> _palettes = new(StringComparer.Ordinal);

    public IReadOnlyList<DecoPiece> Pieces => _pieces;
    public IReadOnlyList<DecoSet> Sets => _sets;
    /// <summary>Palettes by id: slot name to an rrggbb colour. "base" holds every slot; the others override some.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Palettes => _palettes;

    public DecoPiece Piece(string id) => _byId.TryGetValue(id, out DecoPiece p) ? p : throw new KeyNotFoundException($"unknown deco piece '{id}'");
    public bool TryGet(string id, out DecoPiece p) => _byId.TryGetValue(id, out p);
    public DecoSet Set(string id) => _setById.TryGetValue(id, out DecoSet s) ? s : throw new KeyNotFoundException($"unknown deco set '{id}'");
    public bool HasSet(string id) => _setById.ContainsKey(id);

    /// <summary>Pieces whose own set is <paramref name="set"/>.</summary>
    public IEnumerable<DecoPiece> OfSet(string set) => _pieces.Where(p => p.Set == set);
    /// <summary>Pieces that furnish <paramref name="set"/>, its own first, then those it borrows.</summary>
    public IEnumerable<DecoPiece> InSet(string set) => _pieces.Where(p => p.Set == set).Concat(_pieces.Where(p => p.Set != set && p.Also.Contains(set)));
    /// <summary>Pieces that furnish a set and play a role in it.</summary>
    public IEnumerable<DecoPiece> WithRole(string set, string role) => InSet(set).Where(p => p.Has(role));

    public static DecoCatalogue LoadDirectory(string dir) => Load(File.ReadAllText(Path.Combine(dir, FileName)));

    public static DecoCatalogue Load(string json)
    {
        var c = new DecoCatalogue();
        JsonElement root = DataJson.Parse(json, Schema);
        c.LoadPalettes(root.GetProperty("palettes"));
        c.LoadSets(root.GetProperty("sets"));
        if (root.TryGetProperty("salvage", out JsonElement salvage)) c.LoadSalvage(salvage);
        c.LoadPieces(root.GetProperty("pieces"));
        return c;
    }

    void LoadPalettes(JsonElement e)
    {
        foreach (JsonProperty p in e.EnumerateObject())
        {
            var slots = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty s in p.Value.EnumerateObject())
            {
                string hex = s.Value.GetString();
                if (hex == null || hex.Length != 6 || !hex.All(Uri.IsHexDigit)) throw new InvalidDataException($"palette '{p.Name}': '{s.Name}' is not an rrggbb colour");
                slots[s.Name] = hex;
            }
            _palettes[p.Name] = slots;
        }
        if (!_palettes.ContainsKey("base")) throw new InvalidDataException("deco.json: the 'base' palette is missing");
    }

    void LoadSets(JsonElement e)
    {
        foreach (JsonProperty p in e.EnumerateObject())
        {
            JsonElement s = p.Value;
            var set = new DecoSet
            {
                Id = p.Name,
                Name = DataJson.Str(s, "name"),
                Palette = DataJson.Str(s, "palette", p.Name),
                Level = DataJson.Int(s, "level", 1),
                Cost = DataJson.Int(s, "cost", 0),
                Tab = DataJson.Str(s, "tab", "deco"),
                SelfLit = DataJson.Bool(s, "selfLit", false),
                Outdoor = DataJson.Bool(s, "outdoor", false),
                Crush = DataJson.Bool(s, "crush", false),
                Tech = ParseTech(DataJson.Str(s, "tech", "mid"), $"set '{p.Name}'"),
                States = s.TryGetProperty("states", out JsonElement st) ? ParseStates(st, $"set '{p.Name}'") : AllDamage,
                Blurb = DataJson.Str(s, "blurb", ""),
            };
            if (!_palettes.ContainsKey(set.Palette)) throw new InvalidDataException($"set '{set.Id}': unknown palette '{set.Palette}'");
            if (set.Level < 1 || set.Cost < 0) throw new InvalidDataException($"set '{set.Id}': level must be at least 1 and cost not negative");
            _sets.Add(set);
            _setById[set.Id] = set;
        }
    }

    static readonly DecoState[] AllDamage = { DecoState.Knocked, DecoState.Broken, DecoState.Burnt, DecoState.Overgrown };

    static DecoTech ParseTech(string s, string where) =>
        System.Enum.TryParse(s, true, out DecoTech t) ? t : throw new InvalidDataException($"{where}: tech is low, mid or high, not '{s}'");

    static List<DecoState> ParseStates(JsonElement st, string where)
    {
        var list = new List<DecoState>();
        foreach (JsonElement s in st.EnumerateArray())
            list.Add(System.Enum.TryParse(s.GetString(), true, out DecoState ds) && ds != DecoState.Intact ? ds : throw new InvalidDataException($"{where}: bad state '{s.GetString()}'"));
        return list;
    }

    public static readonly string[] SalvageClasses = { "light", "medium", "heavy", "huge" };
    readonly Dictionary<string, Bill> _salvage = new(StringComparer.Ordinal);

    /// <summary>The rough scrap a salvage class yields (deco.json "salvage"), before the salvaging tool's rate.</summary>
    public Bill SalvageYield(string cls) => _salvage.TryGetValue(cls, out Bill b) ? b.Clone() : throw new KeyNotFoundException($"unknown salvage class '{cls}'");

    void LoadSalvage(JsonElement e)
    {
        foreach (JsonProperty p in e.EnumerateObject())
        {
            if (!SalvageClasses.Contains(p.Name)) throw new InvalidDataException($"salvage class '{p.Name}' is not one of {string.Join(", ", SalvageClasses)}");
            var bill = new Bill();
            foreach (JsonProperty kv in p.Value.EnumerateObject()) bill.Add(kv.Name, kv.Value.GetInt32());
            _salvage[p.Name] = bill;
        }
    }

    void LoadPieces(JsonElement e)
    {
        // The first piece of each set is its root on the tech tree; the rest hang under it.
        var roots = new Dictionary<string, DecoPiece>(StringComparer.Ordinal);
        foreach (JsonElement j in e.EnumerateArray())
        {
            string id = DataJson.Str(j, "id");
            string where = $"deco piece '{id}'";
            if (_byId.ContainsKey(id)) throw new InvalidDataException($"duplicate deco piece id '{id}'");
            string setId = DataJson.Str(j, "set");
            if (!_setById.TryGetValue(setId, out DecoSet set)) throw new InvalidDataException($"{where}: unknown set '{setId}'");

            int[] size = j.GetProperty("size").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            if (size.Length != 3 || size.Any(v => v < 1 || v > 96)) throw new InvalidDataException($"{where}: size must be three small-grid cell counts from 1 to 96");

            DecoMount mount = System.Enum.TryParse(DataJson.Str(j, "mount", "floor"), true, out DecoMount m) ? m : throw new InvalidDataException($"{where}: bad mount");
            bool isFixed = DataJson.Bool(j, "fixed", false) || mount == DecoMount.Ceiling;
            var states = new List<DecoState> { DecoState.Intact };
            if (j.TryGetProperty("states", out JsonElement st)) states.AddRange(ParseStates(st, where));
            else states.AddRange(set.States.Where(s => !(isFixed && s == DecoState.Knocked)));
            if (isFixed && states.Contains(DecoState.Knocked)) throw new InvalidDataException($"{where}: a fixed piece can't be knocked over");

            var falls = new List<DecoFall>();
            if (states.Contains(DecoState.Knocked))
            {
                if (j.TryGetProperty("fall", out JsonElement f))
                    foreach (JsonElement x in f.EnumerateArray())
                    {
                        string name = x.GetString();
                        if (name == "side") { falls.Add(DecoFall.Left); falls.Add(DecoFall.Right); }
                        else falls.Add(System.Enum.TryParse(name, true, out DecoFall df) && df != DecoFall.None ? df : throw new InvalidDataException($"{where}: bad fall '{name}'"));
                    }
                else falls.AddRange(new[] { DecoFall.Back, DecoFall.Front, DecoFall.Left, DecoFall.Right });
            }

            Bill bill = new();
            if (j.TryGetProperty("bill", out JsonElement b))
                foreach (JsonProperty kv in b.EnumerateObject())
                {
                    int n = kv.Value.GetInt32();
                    if (n <= 0) throw new InvalidDataException($"{where}: bill counts must be positive");
                    bill.Add(kv.Name, n);
                }
            if (bill.IsEmpty) throw new InvalidDataException($"{where}: it needs a bill");

            string[] grids = j.TryGetProperty("grids", out JsonElement g) ? g.EnumerateArray().Select(x => x.GetString()).ToArray() : new[] { "large" };
            if (grids.Length == 0 || grids.Any(x => x != "large" && x != "small")) throw new InvalidDataException($"{where}: grids are 'large' and/or 'small'");

            DecoLight light = null;
            if (j.TryGetProperty("light", out JsonElement l))
                light = new DecoLight((float)DataJson.Num(l, "range", 3), (float)DataJson.Num(l, "energy", 1));

            roots.TryGetValue(setId, out DecoPiece root);
            int level = DataJson.Int(j, "level", set.Level), cost = DataJson.Int(j, "cost", set.Cost);
            if (level < 1 || cost < 0) throw new InvalidDataException($"{where}: unlock level must be at least 1 and cost not negative");
            bool onLarge = grids.Contains("large"), onSmall = grids.Contains("small");
            DecoUnlock Node(bool small) =>
                new(level, cost, root == null ? null : (small ? (root.OnSmall ? root.NodeId(true) : null) : (root.OnLarge ? root.NodeId(false) : null)), set.Tab);

            var piece = new DecoPiece
            {
                Id = id,
                Name = DataJson.Str(j, "name"),
                Set = setId,
                Also = j.TryGetProperty("also", out JsonElement a) ? a.EnumerateArray().Select(x => x.GetString()).ToArray() : Array.Empty<string>(),
                Roles = j.TryGetProperty("roles", out JsonElement r) ? r.EnumerateArray().Select(x => x.GetString()).ToArray() : Array.Empty<string>(),
                SizeX = size[0], SizeY = size[1], SizeZ = size[2],
                OnLarge = onLarge, OnSmall = onSmall,
                Mount = mount,
                MountY = (float)DataJson.Num(j, "mountY", mount == DecoMount.Wall ? 1.0 : 0),
                Surface = (float)DataJson.Num(j, "surface", 0),
                WalkOver = DataJson.Bool(j, "walk", false),
                States = states,
                Falls = falls,
                Bill = bill,
                UniqueOnly = DataJson.Bool(j, "uniqueOnly", false),
                UnlockLarge = onLarge ? Node(false) : null,
                UnlockSmall = onSmall ? Node(true) : null,
                Light = light,
                Anim = DataJson.Str(j, "anim", null),
                Tech = ParseTech(DataJson.Str(j, "tech", set.Tech.ToString()), where),
                Salvage = DataJson.Str(j, "salvage", null),
                Outdoor = set.Outdoor,
                Palette = DataJson.Str(j, "palette", set.Palette),
                Hp = DataJson.Int(j, "hp", 80 + 10 * size[0] * size[1] * size[2] / 4),
                MassKg = (float)DataJson.Num(j, "mass", 6 * size[0] * size[1] * size[2]),
                Index = _pieces.Count,
            };
            if (!_palettes.ContainsKey(piece.Palette)) throw new InvalidDataException($"{where}: unknown palette '{piece.Palette}'");
            if (piece.Roles.Count == 0) throw new InvalidDataException($"{where}: it needs at least one role");
            foreach (string s in piece.Also)
                if (!_setById.ContainsKey(s)) throw new InvalidDataException($"{where}: unknown set '{s}' in also");
            if (piece.Anim != null && !Anims.Contains(piece.Anim)) throw new InvalidDataException($"{where}: unknown anim '{piece.Anim}'");
            if (piece.Surface < 0 || piece.Surface > piece.Height) throw new InvalidDataException($"{where}: surface must be within its height");
            if (piece.Salvage != null && !SalvageClasses.Contains(piece.Salvage)) throw new InvalidDataException($"{where}: salvage class must be one of {string.Join(", ", SalvageClasses)}");
            _pieces.Add(piece);
            _byId[id] = piece;
            if (root == null) roots[setId] = piece;
        }
    }

    public static readonly string[] Anims = { "flicker", "blink", "pulse", "spark" };

    /// <summary>
    /// Softer rules: bills name real items, unlock parents exist, every set has six pieces of its
    /// own, sizes fit a POI room (2.5 m tall at most), names are unique. With no item catalogue,
    /// bills aren't checked against items.
    /// </summary>
    public List<string> Validate(ItemCatalogue items = null)
    {
        var problems = new List<string>();
        var nodes = new HashSet<string>(_pieces.SelectMany(p => new[] { p.OnLarge ? p.NodeId(false) : null, p.OnSmall ? p.NodeId(true) : null }).Where(n => n != null));
        foreach (DecoPiece p in _pieces)
        {
            if (items != null)
                foreach (string id in p.Bill.Ids)
                    if (!items.Has(id)) problems.Add($"{p.Id}: bill names unknown item '{id}'");
            if (items != null && items.Blocks.TryGet(p.Id, out _)) problems.Add($"{p.Id}: the id is already a block in blocks.json");
            if (p.Bill.Ids.Contains("precursor-alloy") != p.UniqueOnly) problems.Add($"{p.Id}: uniqueOnly must match needing precursor alloy");
            foreach (DecoUnlock u in new[] { p.UnlockLarge, p.UnlockSmall })
                if (u?.Parent != null && !nodes.Contains(u.Parent)) problems.Add($"{p.Id}: unlock parent '{u.Parent}' is not a node");
            if (!p.OnLarge && !p.OnSmall) problems.Add($"{p.Id}: it can be placed on no grid");
            if (!p.Outdoor && p.Height > 2.5f + 1e-4f && p.Mount != DecoMount.Ceiling) problems.Add($"{p.Id}: taller than a 3 m POI room allows (2.5 m)");
            if (p.Mount == DecoMount.Top && p.Height > 1f) problems.Add($"{p.Id}: a piece that sits on others must be small");
            if (p.Light == null && p.Anim is "flicker" or "pulse") problems.Add($"{p.Id}: it {p.Anim}s but gives no light");
        }
        foreach (DecoSet s in _sets)
        {
            int own = _pieces.Count(p => p.Set == s.Id);
            if (own < 6) problems.Add($"set '{s.Id}' has only {own} pieces of its own (6 or more wanted)");
        }
        foreach (var dup in _pieces.GroupBy(p => p.Name).Where(g => g.Count() > 1))
            problems.Add($"two pieces are called '{dup.Key}'");
        return problems;
    }

    /// <summary>
    /// Integration: adds every intact deco piece to the shared catalogue as a block (its bill, on the
    /// grids it goes on) and as craftable block items, one per grid, with the piece's tech-tree node
    /// on the deco tab (mapped to the shared <see cref="Unlock"/>) and its theme set as the item's
    /// group. Damaged and knocked-over states are Creative and POI blocks: they get no bill and no item.
    /// </summary>
    public void AddBillsTo(ItemCatalogue items)
    {
        foreach (DecoPiece p in _pieces)
        {
            if (items.Blocks.TryGet(p.Id, out _)) throw new InvalidDataException($"deco piece '{p.Id}' clashes with a block id");
            var bill = new BlockBill
            {
                BlockId = p.Id, Small = p.Bill.Clone(), Large = p.Bill.Clone(), UniqueOnly = p.UniqueOnly,
                OnSmall = p.OnSmall, OnLarge = p.OnLarge, Family = BlockFamily.Deco,
                UnlockSmall = ToUnlock(p.UnlockSmall), UnlockLarge = ToUnlock(p.UnlockLarge),
            };
            items.AddBlock(bill, small => new ItemCatalogue.BlockItemSpec(Name: $"{p.Name}{(p.OnSmall && p.OnLarge ? (small ? " (small)" : " (large)") : "")}", Group: p.Set));
        }
        items.Rebuild();
    }

    static Unlock ToUnlock(DecoUnlock u) => u == null ? null : ItemCatalogue.ToUnlock(u.Level, u.Cost, u.Parent, u.Tab);
}
