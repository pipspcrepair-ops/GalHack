using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace GalHack.Structures;

public enum DamageType { Kinetic, Explosive, Energy }

public enum GridSize { Large, Small }

public enum BlockKind { Armor, Component }

/// <summary>How a component collides: as a whole cell, as its part boxes, or not at all (ladders, lights).</summary>
public enum Collide { Cell, Parts, None }

/// <summary>How a component part is drawn. Body faces take the block's paint; the rest are fixed.</summary>
public enum PartStyle { Body, Dark, Accent, Glow, Glass }

/// <summary>Resistance to each damage type, 0 (none) to 1 (immune).</summary>
public readonly record struct Resist(float Kinetic, float Explosive, float Energy)
{
    public float this[DamageType t] => t switch { DamageType.Kinetic => Kinetic, DamageType.Explosive => Explosive, _ => Energy };
}

/// <summary>
/// A grid size. Stats in the data files are for a large-grid cube; a grid scales them, so one
/// catalogue serves both grids and a small block is never a separate definition.
/// </summary>
public sealed class GridDef
{
    public string Id;
    public GridSize Size;
    public float CellSize;
    public float HpScale, MassScale, CpuScale, CostScale, StorageScale, FunctionScale;
    public long[] CpuTiers;
    public double StorageCap;
}

public sealed class Part
{
    public PartStyle Style;
    public List<Polygon> Polygons;
    public Vector3 Min, Max;

    /// <summary>Drawn (and solid) only while a door is closed: the door leaf.</summary>
    public bool ClosedOnly;
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

    // Stats for a full large-grid cube. Shapes scale hit points and mass by volume.
    public float Hp, Armor, Mass, Cpu;
    public Resist Resist;

    public bool Airtight, Transparent;
    public uint Color, Accent = 0xe85d04, GlowColor = 0xffc46b;
    public FaceTexture Texture = FaceTexture.Panel;
    public byte Metal;
    public string UpgradesTo;
    public BlockType Upgrade;
    public Dictionary<string, int> Cost = new();
    /// <summary>The small-grid recipe, from common materials. Null falls back to Cost × the grid's costScale.</summary>
    public Dictionary<string, int> SmallCost;

    // Components only.
    public string Function = "";
    public float Power, Thrust, Storage, Fuel;
    public int AttachFaces = 0x3f, AirtightFaces, CoverFaces;

    // A door's faces seal and hide what is behind them only while it is closed.
    public int AirtightFacesOpen, CoverFacesOpen;
    public Collide Collide = Collide.Cell;
    public List<Part> Parts = new();

    public bool IsArmor => Kind == BlockKind.Armor;
    public bool IsCore => Function == "core";
    public bool IsDoor => Function == "door";
    public bool IsLadder => Function == "ladder";
    public bool IsLight => Function == "light";
    public bool IsThruster => Function == "thruster";
    public bool IsGenerator => Function == "generator";
    public bool IsContainer => Function == "container";
    public bool IsCpuExtender => Function == "cpu-extender";
    public bool IsCockpit => Function == "cockpit";

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

    readonly List<GridDef> _grids = new();
    readonly List<ShapeDef> _shapes = new();
    readonly List<BlockType> _types = new() { null }; // type 0 is "empty"
    readonly Dictionary<string, ShapeDef> _shapeById = new();
    readonly Dictionary<string, BlockType> _typeById = new();

    public IReadOnlyList<GridDef> Grids => _grids;
    public IReadOnlyList<ShapeDef> Shapes => _shapes;
    public IReadOnlyList<BlockType> Types => _types;
    public ShapeDef Cube => _shapes[0];
    public BlockType CoreType { get; private set; }
    public int[] ExtendersPerTier { get; private set; } = { 0, 1, 2, 4 };
    public double CargoMassPerSu { get; private set; } = 0.001;

    /// <summary>Materials found on any planet; small-grid recipes use only these.</summary>
    public string[] CommonMaterials { get; private set; } = Array.Empty<string>();

    public GridDef Grid(GridSize size) => _grids.First(g => g.Size == size);
    public GridDef GridById(string id) => _grids.FirstOrDefault(g => g.Id == id);
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
            if (t.UpgradesTo != null) t.Upgrade = c.Type(t.UpgradesTo);
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

    /// <summary>World faces the block touches at all (full or partial): it can attach through these.</summary>
    public byte TouchMask(in Block b) => _touch[FaceIndex(b)];
    /// <summary>World faces the block covers completely.</summary>
    public byte FullMask(in Block b) => _full[FaceIndex(b)];
    /// <summary>World faces fully and opaquely covered: the neighbour's face behind is hidden.</summary>
    public byte OccludeMask(in Block b) => _occlude[FaceIndex(b)];
    /// <summary>World faces air cannot pass.</summary>
    public byte SealMask(in Block b) => _seal[FaceIndex(b)];

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
                                : BlockType.Has(open == 1 ? t.CoverFacesOpen : t.CoverFaces, local);
                            if (occludes) _occlude[i] |= bit;
                            bool seals = t.IsArmor
                                ? t.Airtight && cover == Coverage.Full
                                : BlockType.Has(open == 1 ? t.AirtightFacesOpen : t.AirtightFaces, local);
                            if (seals) _seal[i] |= bit;
                        }
                    }
        }
    }

    /// <summary>Full hit points of a placed block: the type's, scaled by the grid and the shape's volume.</summary>
    public float MaxHp(in Block b, GridDef grid)
    {
        BlockType t = _types[b.Type];
        return t.Hp * grid.HpScale * (t.IsArmor ? _shapes[b.Shape].Volume : 1f);
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
        foreach (JsonElement g in root.GetProperty("grids").EnumerateArray())
        {
            string id = Str(g, "id");
            _grids.Add(new GridDef
            {
                Id = id,
                Size = id == "small" ? GridSize.Small : GridSize.Large,
                CellSize = Num(g, "cellSize"),
                HpScale = Num(g, "hpScale", 1),
                MassScale = Num(g, "massScale", 1),
                CpuScale = Num(g, "cpuScale", 1),
                CostScale = Num(g, "costScale", 1),
                StorageScale = Num(g, "storageScale", 1),
                FunctionScale = Num(g, "functionScale", 1),
                CpuTiers = g.GetProperty("cpuTiers").EnumerateArray().Select(e => e.GetInt64()).ToArray(),
                StorageCap = Num(g, "storageCap"),
            });
        }
        if (root.TryGetProperty("extendersPerTier", out JsonElement ext))
            ExtendersPerTier = ext.EnumerateArray().Select(e => e.GetInt32()).ToArray();
        CargoMassPerSu = Num(root, "cargoMassPerSu", 0.001f);
        if (root.TryGetProperty("commonMaterials", out JsonElement common))
            CommonMaterials = common.EnumerateArray().Select(e => e.GetString()).ToArray();
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
            t.AttachFaces = Faces(c, "attach", 0x3f);
            t.AirtightFaces = Faces(c, "airtight", 0);
            t.CoverFaces = Faces(c, "covers", 0);
            t.AirtightFacesOpen = Faces(c, "airtightOpen", t.AirtightFaces);
            t.CoverFacesOpen = Faces(c, "coversOpen", t.CoverFaces);
            t.Airtight = t.AirtightFaces != 0;
            t.Collide = Str(c, "collide", "cell") switch { "none" => Collide.None, "parts" => Collide.Parts, _ => Collide.Cell };
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
        };
        part.Polygons = p.TryGetProperty("cylinder", out JsonElement axis)
            ? Geometry.Cylinder(AxisIndex(axis.GetString()), min, max, Int(p, "segments", 12))
            : Geometry.ConvexHull(Geometry.BoxPoints(min, max).ToList());
        return part;
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
            Cpu = Num(e, "cpu", 0),
            Metal = (byte)Int(e, "metal", 0),
        };
        t.Name = Str(e, "name", t.Id);
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
        if (e.TryGetProperty("smallCost", out JsonElement smallCost))
        {
            t.SmallCost = new Dictionary<string, int>();
            foreach (JsonProperty item in smallCost.EnumerateObject())
                t.SmallCost[item.Name] = item.Value.GetInt32();
        }
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
    public BlockStats StatsFor(in Block b, GridDef grid)
    {
        BlockType t = _types[b.Type];
        float volume = t.IsArmor ? _shapes[b.Shape].Volume : 1f;
        return new BlockStats(
            t.Hp * grid.HpScale * volume,
            t.Armor,
            t.Resist,
            t.Mass * grid.MassScale * volume,
            t.Cpu * grid.CpuScale);
    }

    /// <summary>
    /// The materials a block is built from, returned when it is taken down. Small-grid blocks use
    /// their own cheap recipe of common materials, so a first small vessel comes quickly.
    /// The core's mode never changes a recipe.
    /// </summary>
    public Dictionary<string, int> CostOf(in Block b, GridDef grid)
    {
        BlockType t = _types[b.Type];
        float volume = t.IsArmor ? _shapes[b.Shape].Volume : 1f;
        bool small = grid.Size == GridSize.Small && t.SmallCost != null;
        float scale = small ? 1f : grid.CostScale;
        var cost = new Dictionary<string, int>();
        foreach (var (item, n) in small ? t.SmallCost : t.Cost)
            cost[item] = Math.Max(1, (int)MathF.Round(n * volume * scale));
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
        string.Create(CultureInfo.InvariantCulture, $"{_types.Count - 1} block types, {_shapes.Count} shapes, {_grids.Count} grids");
}

/// <summary>A placed block's numbers, after grid and shape scaling.</summary>
public readonly record struct BlockStats(float MaxHp, float Armor, Resist Resist, float Mass, float Cpu);
