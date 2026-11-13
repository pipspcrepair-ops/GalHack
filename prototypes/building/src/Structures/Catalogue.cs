using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace GalHack.Structures;

public enum DamageType { Kinetic, Explosive, Energy }

public enum BlockKind { Armor, Component }

/// <summary>How a component collides: as a whole cell, as its part boxes, or not at all (ladders, lights).</summary>
public enum Collide { Cell, Parts, None }

/// <summary>How a component part is drawn. Body faces take the block's paint; the rest are fixed.</summary>
public enum PartStyle { Body, Dark, Accent, Glow, Glass }

/// <summary>How a block's material shows damage: metal dents and scorches, concrete chips, alien alloy bruises and bleeds light, glass cracks and shatters.</summary>
public enum DamageLook { Metal, Concrete, Alien, Glass }

/// <summary>Resistance to each damage type, 0 (none) to 1 (immune).</summary>
public readonly record struct Resist(float Kinetic, float Explosive, float Energy)
{
    public float this[DamageType t] => t switch { DamageType.Kinetic => Kinetic, DamageType.Explosive => Explosive, _ => Energy };
}

/// <summary>The one block grid: its cell size and how much a structure's containers may hold.</summary>
public sealed class GridDef
{
    public string Id;
    public float CellSize;
    public double StorageCap;
}

public sealed class Part
{
    public PartStyle Style;
    public List<Polygon> Polygons;
    public Vector3 Min, Max;

    /// <summary>Drawn (and solid) only while a door is closed: the door leaf.</summary>
    public bool ClosedOnly;

    /// <summary>How its faces depart from the style's look (colour, pattern, a screen's symbol, a pulse), or null.</summary>
    public PartFinish Finish;

    // Detail parts only (see BlockType.Detail).
    /// <summary>The face it sits on: hidden, with all of it, when a neighbour hides that face.</summary>
    public Dir? Mount;
}

/// <summary>
/// What a part changes about its style's look. Unset values keep the style's: a body part keeps
/// the block's paint, a dark part the housing grey, a glow its type's colour.
/// </summary>
public sealed class PartFinish
{
    public uint? Color, Accent;
    /// <summary>A <see cref="Structures.Pattern"/> or one of the detail patterns (<see cref="DetailPattern"/>).</summary>
    public int Pattern;
    public FaceTexture? Texture;
    /// <summary>0..3 like a material's; -1 keeps the style's.</summary>
    public int Metal = -1;
    /// <summary>A symbol shown on the part's faces: lit on a screen (a glow part), stencilled on anything else.</summary>
    public DeviceSymbol Symbol;
    /// <summary>A glow that moves: a slow pulse, a blinking lamp.</summary>
    public LightAnim Anim;
    public float Speed = 0.5f;
}

/// <summary>Patterns only parts use, beyond the paint patterns: fine hazard trim, slotted grilles, a symbol.</summary>
public static class DetailPattern
{
    public const int FineHazard = 5, Grille = 6, Symbol = 7;
}

/// <summary>
/// Anything that can be placed: an armour material (combined with any shape) or a component
/// (a functional block with a fixed shape). Both carry the same stats.
/// </summary>
public sealed class BlockType
{
    public string Id, Name;
    public int Index;
    public BlockKind Kind;

    // Stats for a full cube. Shapes scale hit points, mass and plates by volume.
    public float Hp, Armor, Mass;
    public Resist Resist;

    public bool Airtight, Transparent;
    public uint Color, Accent = 0xe85d04, GlowColor = 0xffc46b;
    public FaceTexture Texture = FaceTexture.Panel;
    public byte Metal;
    public DamageLook DamageLook;
    public string UpgradesTo;
    public BlockType Upgrade;
    public Dictionary<string, int> Cost = new();

    // Plated armour only: the block under the plating, and the plate item sprayed on per full cube.
    public BlockType Base;
    public string Plate;
    public int Plates;
    public bool IsPlated => Plate != null;

    // Components only.
    /// <summary>
    /// Cells the component spans, in its own frame (x, y, z), from its anchor towards +x, +y and
    /// +z. One cell unless the data gives a size; its parts fill the whole of it (see <see cref="Footprint"/>).
    /// </summary>
    public Int3 Size = Footprint.One;
    /// <summary>True when <see cref="Size"/> is more than one cell.</summary>
    public bool Multi;
    public string Function = "";
    public float Power, Thrust, Storage, Fuel;
    // What the handling model reads: gyro torque, hover lift (kN) and ride height (m), leg reach (m).
    public float Torque, Lift, RideHeight, LegLength;
    /// <summary>Chairs: where a seated person's hips rest and where their eyes are, in the block's -0.5..0.5 box.</summary>
    public Vector3 Seat, Eye;

    /// <summary>
    /// A variant family placed from one item, its size picked like a shape (doors), or null. The
    /// first type listed in the family is its head, the one the item names.
    /// </summary>
    public string Group;
    public BlockType Head;
    /// <summary>How many of the family's item one block takes: its cells over the head's, rounded up.</summary>
    public int Units = 1;
    public int AttachFaces = 0x3f, AirtightFaces, CoverFaces;

    // A door's faces seal and hide what is behind them only while it is closed.
    public int AirtightFacesOpen, CoverFacesOpen;
    public Collide Collide = Collide.Cell;
    public List<Part> Parts = new();

    /// <summary>
    /// Looks only: the bolts, vents, screens, fins and trim that make a device read as a machine
    /// (<see cref="DeviceDetail"/>). Never solid, never covering a face, never sealing one.
    /// </summary>
    public List<Part> Detail = new();

    public bool IsArmor => Kind == BlockKind.Armor;
    public bool IsCore => Function == "core";
    public bool IsDoor => Function == "door";
    public bool IsLadder => Function == "ladder";
    public bool IsLight => Function == "light";
    public bool IsThruster => Function == "thruster";
    public bool IsGenerator => Function == "generator";
    public bool IsContainer => Function == "container";
    /// <summary>A seat that gives control: the command chair, or a cockpit built round one.</summary>
    public bool IsCommandChair => Function == "chair";
    public int Cells => Size.X * Size.Y * Size.Z;

    public static bool Has(int mask, Dir d) => (mask & (1 << (int)d)) != 0;
}

/// <summary>
/// Every grid, shape, material and component, loaded from the JSON files in data/. Nothing about
/// blocks is hard-coded: a new material or component is a new entry in a data file.
/// </summary>
public sealed class Catalogue
{
    public const string GridsFile = "grids.json", MaterialsFile = "materials.json",
        ShapesFile = "shapes.json", ComponentsFile = "components.json";

    readonly List<ShapeDef> _shapes = new();
    readonly List<BlockType> _types = new() { null }; // type 0 is "empty"
    readonly Dictionary<string, ShapeDef> _shapeById = new();
    readonly Dictionary<string, BlockType> _typeById = new();
    readonly List<(string old, string now)> _was = new(); // old ids, read as the types that replaced them

    public GridDef Grid { get; private set; }
    public IReadOnlyList<ShapeDef> Shapes => _shapes;
    public IReadOnlyList<BlockType> Types => _types;
    public ShapeDef Cube => _shapes[0];
    public BlockType CoreType { get; private set; }
    public double CargoMassPerSu { get; private set; } = 0.001;

    /// <summary>A variant family's types in data order, head first (just the type itself when it has no family).</summary>
    public IReadOnlyList<BlockType> VariantsOf(BlockType t) =>
        t.Group == null ? new[] { t } : _types.Skip(1).Where(v => v.Group == t.Group).ToList();

    public bool TryGetShape(string id, out ShapeDef shape) => _shapeById.TryGetValue(id, out shape);
    public bool TryGetType(string id, out BlockType type) => _typeById.TryGetValue(id, out type);
    public BlockType Type(string id) => _typeById.TryGetValue(id, out var t) ? t : throw new KeyNotFoundException($"unknown block type '{id}'");
    public ShapeDef Shape(string id) => _shapeById.TryGetValue(id, out var s) ? s : throw new KeyNotFoundException($"unknown shape '{id}'");

    /// <summary>Loads the four data files from a folder on disk (tools, servers, tests outside the engine).</summary>
    public static Catalogue LoadDirectory(string dir) =>
        Load(name => File.ReadAllText(Path.Combine(dir, name)));

    /// <summary>Loads the catalogue through a file reader, so the engine can supply res:// files.</summary>
    public static Catalogue Load(Func<string, string> readFile)
    {
        var c = new Catalogue();
        c.LoadGrids(Parse(readFile(GridsFile), "galhack.grids"));
        c.LoadShapes(Parse(readFile(ShapesFile), "galhack.shapes"));
        c.LoadMaterials(Parse(readFile(MaterialsFile), "galhack.materials"));
        c.LoadComponents(Parse(readFile(ComponentsFile), "galhack.components"));
        foreach (BlockType t in c._types.Skip(1))
        {
            if (t.UpgradesTo != null) t.Upgrade = c.Type(t.UpgradesTo);
            if (!t.IsArmor) t.Detail = DeviceDetail.For(t);
            if (t.Group == null) continue;
            t.Head = c._types.Skip(1).First(v => v.Group == t.Group);
            t.Units = (t.Cells + t.Head.Cells - 1) / t.Head.Cells;
        }
        foreach (var (old, now) in c._was)
            if (!c._typeById.TryAdd(old, c.Type(now))) throw new InvalidDataException($"'{old}' is listed as replaced but is a block id");
        c.CoreType = c._types.Skip(1).FirstOrDefault(t => t.IsCore) ?? throw new InvalidDataException("no core component in components.json");
        c.BuildFaceTables();
        return c;
    }

    // ---- Face tables -------------------------------------------------------------------------
    // What each face of a block does depends only on its type, shape, orientation and whether it
    // is open, so it is worked out once for every combination. The mesher, the room flood fill
    // and connectivity then read one byte per block instead of re-deriving faces.

    byte[] _touch, _full, _occlude, _seal; // one bit per world Dir

    int FaceIndex(in Block b) => (((b.Type * _shapes.Count + b.Shape) * Orientations.Count + b.Rot) << 1) | (b.Open ? 1 : 0);

    // A cell of a block that spans several cells has only the faces on the outside of the
    // block: the faces between two of its cells don't exist. An outer face does what the
    // component's face list says for that side, so "+z" means the whole +z side of the block.

    /// <summary>World faces the block touches at all (full or partial): it can attach through these.</summary>
    public byte TouchMask(in Block b) => Outer(b, _touch[FaceIndex(b)]);
    /// <summary>World faces the block covers completely.</summary>
    public byte FullMask(in Block b) => Outer(b, _full[FaceIndex(b)]);
    /// <summary>World faces fully and opaquely covered: the neighbour's face behind is hidden.</summary>
    public byte OccludeMask(in Block b) => Outer(b, _occlude[FaceIndex(b)]);

    byte Outer(in Block b, byte mask) => _types[b.Type].Multi ? (byte)(mask & Footprint.OuterMask(_types[b.Type], b)) : mask;

    /// <summary>True if the whole block, all round, hides what's behind it (a block spanning several cells: every outer face).</summary>
    public bool OpaqueAllRound(in Block b) => _occlude[FaceIndex(b)] == 0x3f;

    /// <summary>
    /// World faces air cannot pass. Inside a block that spans several cells, air passes between
    /// its cells unless the block seals all round (then all of it is solid to air, like a cube).
    /// </summary>
    public byte SealMask(in Block b)
    {
        byte seal = _seal[FaceIndex(b)];
        BlockType t = _types[b.Type];
        if (!t.Multi) return seal;
        byte outer = Footprint.OuterMask(t, b);
        bool allRound = (b.Open ? t.AirtightFacesOpen : t.AirtightFaces) == 0x3f;
        return (byte)((seal & outer) | (allRound ? ~outer & 0x3f : 0));
    }

    void BuildFaceTables()
    {
        int n = _types.Count * _shapes.Count * Orientations.Count * 2;
        _touch = new byte[n];
        _full = new byte[n];
        _occlude = new byte[n];
        _seal = new byte[n];
        for (int type = 1; type < _types.Count; type++)
        {
            BlockType t = _types[type];
            for (int shape = 0; shape < (t.IsArmor ? _shapes.Count : 1); shape++)
                for (int rot = 0; rot < Orientations.Count; rot++)
                    for (int open = 0; open < 2; open++)
                    {
                        var b = new Block { Type = (ushort)type, Shape = (byte)shape, Rot = (byte)rot, Flags = open == 1 ? BlockFlags.Open : BlockFlags.None };
                        int i = FaceIndex(b);
                        foreach (Dir world in Dirs.All)
                        {
                            Dir local = Orientations.ToLocal(rot, world);
                            byte bit = (byte)(1 << (int)world);
                            Coverage cover = t.IsArmor ? _shapes[shape].Face(local)
                                : BlockType.Has(t.AttachFaces, local) ? Coverage.Full : Coverage.None;
                            if (cover != Coverage.None) _touch[i] |= bit;
                            if (cover == Coverage.Full) _full[i] |= bit;
                            bool occludes = t.IsArmor
                                ? !t.Transparent && cover == Coverage.Full
                                : BlockType.Has(open == 1 ? t.CoverFacesOpen : t.CoverFaces, local) && PartsCover(t, local, open == 1);
                            if (occludes) _occlude[i] |= bit;
                            bool seals = t.IsArmor
                                ? t.Airtight && cover == Coverage.Full
                                : BlockType.Has(open == 1 ? t.AirtightFacesOpen : t.AirtightFaces, local);
                            if (seals) _seal[i] |= bit;
                        }
                    }
        }
    }

    /// <summary>
    /// True if an opaque part really lays a whole cell face here, measured as armour's shapes
    /// are: <c>covers</c> in the data can only narrow this. A thruster's body stops short of its
    /// nozzle end, so its sides cover 70% of the cell, and hiding the face behind them would leave
    /// a hole through the construct.
    /// </summary>
    static bool PartsCover(BlockType t, Dir local, bool open)
    {
        foreach (Part part in t.Parts)
        {
            if (part.Style == PartStyle.Glass || (open && part.ClosedOnly)) continue;
            foreach (Polygon poly in part.Polygons)
                if (poly.Boundary == local && poly.Area() > 0.999f) return true;
        }
        return false;
    }

    /// <summary>Full hit points of a placed block: the type's, scaled by the shape's volume.</summary>
    public float MaxHp(in Block b) => _types[b.Type].Hp * VolumeOf(b);

    float VolumeOf(in Block b) => _types[b.Type].IsArmor ? _shapes[b.Shape].Volume : 1f;

    /// <summary>Plates a plated block of this shape carries: the cube's share by volume, never fewer than one.</summary>
    public int PlatesOf(in Block b)
    {
        BlockType t = _types[b.Type];
        return t.IsPlated ? Math.Max(1, (int)MathF.Round(t.Plates * VolumeOf(b))) : 0;
    }

    static JsonElement Parse(string json, string schema)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        JsonElement root = JsonDocument.Parse(json, options).RootElement.Clone();
        string found = Str(root, "schema", "");
        if (found != schema) throw new InvalidDataException($"expected schema '{schema}', found '{found}'");
        if (Int(root, "version", 0) != 1) throw new InvalidDataException($"{schema}: unsupported version");
        return root;
    }

    void LoadGrids(JsonElement root)
    {
        JsonElement g = root.GetProperty("grids").EnumerateArray().Single();
        Grid = new GridDef { Id = Str(g, "id"), CellSize = Num(g, "cellSize"), StorageCap = Num(g, "storageCap") };
        CargoMassPerSu = Num(root, "cargoMassPerSu", 0.001f);
    }

    void LoadShapes(JsonElement root)
    {
        foreach (JsonElement s in root.GetProperty("shapes").EnumerateArray())
        {
            string id = Str(s, "id"), name = Str(s, "name", id);
            ShapeDef shape;
            if (s.TryGetProperty("box", out JsonElement box))
            {
                var (min, max) = Box(box);
                shape = ShapeDef.FromHull(id, name, Geometry.BoxPoints(min, max));
            }
            else if (s.TryGetProperty("cylinder", out JsonElement cyl))
            {
                int axis = AxisIndex(Str(cyl, "axis", "y"));
                float r = Num(cyl, "radius", 0.5f);
                int segments = Int(cyl, "segments", 16);
                Vector3 min = Geometry.Set(new Vector3(-r), axis, -0.5f), max = Geometry.Set(new Vector3(r), axis, 0.5f);
                shape = ShapeDef.FromHull(id, name, Geometry.CylinderPoints(axis, min, max, segments));
                foreach (Polygon p in shape.Polygons)
                    if (MathF.Abs(Geometry.Get(p.Normal, axis)) < 0.5f) p.SmoothAxis = axis;
            }
            else
            {
                shape = ShapeDef.FromHull(id, name, s.GetProperty("hull").EnumerateArray().Select(Vec));
            }
            shape.Index = _shapes.Count;
            _shapes.Add(shape);
            _shapeById[id] = shape;
        }
        if (_shapes.Count == 0 || !_shapes[0].IsCube) throw new InvalidDataException("the first shape must be the cube");
    }

    void LoadMaterials(JsonElement root)
    {
        foreach (JsonElement m in root.GetProperty("materials").EnumerateArray())
        {
            BlockType t = Common(m, BlockKind.Armor);
            t.Airtight = Bool(m, "airtight", true);
            t.Transparent = Bool(m, "transparent", false);
            t.UpgradesTo = m.TryGetProperty("upgradesTo", out JsonElement up) ? up.GetString() : null;
            Add(t);
            if (!m.TryGetProperty("base", out JsonElement under)) continue;
            // A plated block is made of its base block and its plates.
            t.Base = Type(under.GetString());
            t.Plate = Str(m, "plate");
            t.Plates = Int(m, "plates", 1);
            t.Cost = new Dictionary<string, int>(t.Base.Cost) { [t.Plate] = t.Plates };
        }
    }

    void LoadComponents(JsonElement root)
    {
        foreach (JsonElement c in root.GetProperty("components").EnumerateArray())
        {
            BlockType t = Common(c, BlockKind.Component);
            t.Function = Str(c, "function", "");
            t.Power = Num(c, "power", 0);
            t.Thrust = Num(c, "thrust", 0);
            t.Storage = Num(c, "storage", 0);
            t.Fuel = Num(c, "fuel", 0);
            t.Torque = Num(c, "torque", 0);
            t.Lift = Num(c, "lift", 0);
            t.RideHeight = Num(c, "rideHeight", 0);
            t.LegLength = Num(c, "legLength", 0);
            if (c.TryGetProperty("group", out JsonElement group)) t.Group = group.GetString();
            if (c.TryGetProperty("seat", out JsonElement seat)) t.Seat = Vec(seat);
            if (c.TryGetProperty("eye", out JsonElement eye)) t.Eye = Vec(eye);
            t.AttachFaces = Faces(c, "attach", 0x3f);
            t.AirtightFaces = Faces(c, "airtight", 0);
            t.CoverFaces = Faces(c, "covers", 0);
            t.AirtightFacesOpen = Faces(c, "airtightOpen", t.AirtightFaces);
            t.CoverFacesOpen = Faces(c, "coversOpen", t.CoverFaces);
            t.Airtight = t.AirtightFaces != 0;
            t.Collide = Str(c, "collide", "cell") switch { "none" => Collide.None, "parts" => Collide.Parts, _ => Collide.Cell };
            if (c.TryGetProperty("size", out JsonElement size))
            {
                int[] n = size.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                if (n.Length != 3 || n.Any(v => v < 1 || v > Footprint.MaxSize))
                    throw new InvalidDataException($"{t.Id}: size must be three whole cell counts from 1 to {Footprint.MaxSize}");
                t.Size = new Int3(n[0], n[1], n[2]);
                t.Multi = t.Size != Footprint.One;
            }
            if (c.TryGetProperty("parts", out JsonElement parts))
                foreach (JsonElement p in parts.EnumerateArray())
                    t.Parts.Add(LoadPart(p));
            Add(t);
        }
    }

    static Part LoadPart(JsonElement p)
    {
        var (min, max) = Box(p.GetProperty("box"));
        var part = new Part
        {
            Min = min,
            Max = max,
            Style = Enum.Parse<PartStyle>(Str(p, "style", "body"), ignoreCase: true),
            ClosedOnly = Bool(p, "closedOnly", false),
            Finish = LoadFinish(p),
        };
        part.Polygons = p.TryGetProperty("cylinder", out JsonElement axis)
            ? Geometry.Cylinder(AxisIndex(axis.GetString()), min, max, Int(p, "segments", 12))
            : Geometry.ConvexHull(Geometry.BoxPoints(min, max).ToList());
        return part;
    }

    /// <summary>A part's finish from its optional fields: color, accent, pattern, texture, metal, symbol, anim, speed.</summary>
    static PartFinish LoadFinish(JsonElement p)
    {
        string[] keys = { "color", "accent", "pattern", "texture", "metal", "symbol", "anim" };
        if (!keys.Any(k => p.TryGetProperty(k, out _))) return null;
        var f = new PartFinish
        {
            Metal = Int(p, "metal", -1),
            Speed = Num(p, "speed", 0.5f),
        };
        if (p.TryGetProperty("color", out JsonElement c) && Paint.TryParseHex(c.GetString(), out uint cv)) f.Color = cv;
        if (p.TryGetProperty("accent", out JsonElement a) && Paint.TryParseHex(a.GetString(), out uint av)) f.Accent = av;
        if (p.TryGetProperty("pattern", out JsonElement pat))
            f.Pattern = pat.GetString() switch
            {
                "fine-hazard" => DetailPattern.FineHazard,
                "grille" => DetailPattern.Grille,
                var name => (int)Enum.Parse<Pattern>(name, ignoreCase: true),
            };
        if (p.TryGetProperty("texture", out JsonElement tex)) f.Texture = Enum.Parse<FaceTexture>(tex.GetString(), ignoreCase: true);
        if (p.TryGetProperty("symbol", out JsonElement sym)) f.Symbol = Enum.Parse<DeviceSymbol>(sym.GetString(), ignoreCase: true);
        if (p.TryGetProperty("anim", out JsonElement anim)) f.Anim = Enum.Parse<LightAnim>(anim.GetString(), ignoreCase: true);
        return f;
    }

    BlockType Common(JsonElement e, BlockKind kind)
    {
        var t = new BlockType
        {
            Id = Str(e, "id"),
            Kind = kind,
            Hp = Num(e, "hp"),
            Armor = Num(e, "armor", 0),
            Mass = Num(e, "mass"),
            Metal = (byte)Int(e, "metal", 0),
        };
        t.Name = Str(e, "name", t.Id);
        t.DamageLook = e.TryGetProperty("damageLook", out JsonElement look) ? Enum.Parse<DamageLook>(look.GetString(), ignoreCase: true) : DamageLook.Metal;
        if (e.TryGetProperty("resist", out JsonElement r))
            t.Resist = new Resist(Num(r, "kinetic", 0), Num(r, "explosive", 0), Num(r, "energy", 0));
        t.Color = Color(e, "color", 0xc9c3b4);
        t.Accent = Color(e, "accent", t.Accent);
        t.GlowColor = Color(e, "glow", t.GlowColor);
        if (e.TryGetProperty("texture", out JsonElement tex))
            t.Texture = Enum.Parse<FaceTexture>(tex.GetString(), ignoreCase: true);
        if (e.TryGetProperty("cost", out JsonElement cost))
            foreach (JsonProperty item in cost.EnumerateObject())
                t.Cost[item.Name] = item.Value.GetInt32();
        if (e.TryGetProperty("was", out JsonElement was))
            foreach (JsonElement old in was.EnumerateArray()) _was.Add((old.GetString(), t.Id));
        return t;
    }

    void Add(BlockType t)
    {
        if (_typeById.ContainsKey(t.Id))
            throw new InvalidDataException($"duplicate block id '{t.Id}'");
        t.Index = _types.Count;
        _types.Add(t);
        _typeById[t.Id] = t;
    }

    // ---- Per-block numbers --------------------------------------------------------------

    /// <summary>A placed block's stats: hit points and mass scale with shape volume, armour and resistances don't.</summary>
    public BlockStats StatsFor(in Block b)
    {
        BlockType t = _types[b.Type];
        float volume = VolumeOf(b);
        return new BlockStats(t.Hp * volume, t.Armor, t.Resist, t.Mass * volume);
    }

    /// <summary>
    /// The materials a block is built from, returned when it is taken down: each by the shape's
    /// volume, never fewer than one. The core's mode never changes a recipe.
    /// </summary>
    public Dictionary<string, int> CostOf(in Block b)
    {
        float volume = VolumeOf(b);
        var cost = new Dictionary<string, int>();
        foreach (var (item, n) in _types[b.Type].Cost)
            cost[item] = Math.Max(1, (int)MathF.Round(n * volume));
        return cost;
    }

    // ---- JSON helpers ---------------------------------------------------------------------

    static string Str(JsonElement e, string name, string fallback = null) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetString() : fallback ?? throw new InvalidDataException($"missing '{name}'");

    static float Num(JsonElement e, string name, float? fallback = null) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetSingle() : fallback ?? throw new InvalidDataException($"missing '{name}'");

    static int Int(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetInt32() : fallback;

    static bool Bool(JsonElement e, string name, bool fallback) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetBoolean() : fallback;

    static uint Color(JsonElement e, string name, uint fallback) =>
        e.TryGetProperty(name, out JsonElement v) && Paint.TryParseHex(v.GetString(), out uint c) ? c : fallback;

    static Vector3 Vec(JsonElement e)
    {
        float[] f = e.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        return new Vector3(f[0], f[1], f[2]);
    }

    static (Vector3 min, Vector3 max) Box(JsonElement e)
    {
        float[] f = e.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        return (new Vector3(f[0], f[1], f[2]), new Vector3(f[3], f[4], f[5]));
    }

    static int AxisIndex(string axis) => axis switch { "x" => 0, "y" => 1, "z" => 2, _ => throw new InvalidDataException($"bad axis '{axis}'") };

    /// <summary>A face list: "all", "none" or local directions such as ["-y", "+z"].</summary>
    static int Faces(JsonElement e, string name, int fallback)
    {
        if (!e.TryGetProperty(name, out JsonElement v)) return fallback;
        if (v.ValueKind == JsonValueKind.String)
            return v.GetString() switch { "all" => 0x3f, "none" => 0, _ => throw new InvalidDataException($"bad face list '{v.GetString()}'") };
        int mask = 0;
        foreach (JsonElement f in v.EnumerateArray())
        {
            if (!Dirs.TryParse(f.GetString(), out Dir d)) throw new InvalidDataException($"bad face '{f.GetString()}'");
            mask |= 1 << (int)d;
        }
        return mask;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{_types.Count - 1} block types, {_shapes.Count} shapes");
}

/// <summary>A placed block's numbers, after shape scaling.</summary>
public readonly record struct BlockStats(float MaxHp, float Armor, Resist Resist, float Mass);
