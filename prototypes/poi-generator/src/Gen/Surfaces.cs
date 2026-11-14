#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What a face of a place is finished in: the surface the place shader draws over its colour. The
/// order is the shader's: new ones are only ever appended. A block's <see cref="Block.Variant"/> on a
/// wall, floor or roof cube or slope is one of these plus one (zero: worked out by the rules), so a
/// builder that knows better (a house that chose its cladding) can say so.
/// </summary>
public enum Finish : byte
{
    Plain, Brick, Render, Plaster, Weatherboard, Logs, Stone, Rubble, Poured, Precast, Block,
    Corrugated, Seam, Plate, Planks, Parquet, Tile, Lino, Carpet, RoofTile, Slate, Shingle, Thatch, Tar,
    WallTile, Deck, Hull, Paving, Chitin, Resin, Bone, Fused, Basalt, Bronze, RockFace, Soil, CeilingTile,
    Armour, Patched, Cloth, Ash, Glass,
}

/// <summary>Which way a face looks, as finishes are chosen: a wall's outside or inside, a floor, a ceiling, a roof (or paving: anything facing the sky), an eave's underside.</summary>
public enum Side : byte { Out, In, Floor, Ceiling, Roof, Soffit }

/// <summary>A finish and the small choice that goes with it (a brick bond, a tile layout): 0 to 3, or -1 to let the place pick.</summary>
public readonly record struct FinishPick(Finish Finish, int Param = -1);

/// <summary>
/// One side's finish in data: a list of alternatives, each with a condition on the block, the last one
/// unconditional. "painted=render/poured" is render on a painted (tinted) block and poured concrete on a
/// grey one. Conditions: painted, grey, wood, straw, dark, light, slope, flat, low, mid, high, alien.
/// </summary>
public sealed class FinishSpec
{
    public readonly List<(string? When, FinishPick Pick)> Choices = new();

    public static FinishSpec Parse(string text, Func<string, Exception> error)
    {
        var spec = new FinishSpec();
        foreach (string part in text.Split('/'))
        {
            string when = part.Contains('=') ? part[..part.IndexOf('=')] : "";
            string what = part.Contains('=') ? part[(part.IndexOf('=') + 1)..] : part;
            if (when.Length > 0 && !Conditions.Contains(when)) throw error($"unknown finish condition '{when}'");
            spec.Choices.Add((when.Length > 0 ? when : null, Surfaces.ParsePick(what, error)));
        }
        return spec;
    }

    static readonly HashSet<string> Conditions = new() { "painted", "grey", "wood", "straw", "dark", "light", "slope", "flat", "low", "mid", "high", "alien" };
}

/// <summary>Finishes by material, role and side: the defaults in data/finishes.txt and a kit's own lines (kit lines win).</summary>
public sealed class FinishTable
{
    readonly Dictionary<(Material, string, Side), FinishSpec> _rules = new();

    public bool IsEmpty => _rules.Count == 0;

    public void Set(Material m, string role, Side side, FinishSpec spec) => _rules[(m, role, side)] = spec;

    public FinishSpec? Find(Material m, string role, Side side)
    {
        if (_rules.TryGetValue((m, role, side), out var s)) return s;
        if (role == "decor" && _rules.TryGetValue((m, "wall", side), out s)) return s;
        return _rules.TryGetValue((m, "any", side), out s) ? s : null;
    }

    /// <summary>
    /// "finish material role side:spec side:spec ...": material is a block material, role one of wall,
    /// floor, roof, decor, rock or any, each side out, in, floor, ceiling, roof, soffit or all.
    /// </summary>
    public void ParseLine(string[] parts, Func<string, Exception> error)
    {
        if (parts.Length < 4) throw error("finish lines are: finish <material> <role> <side>:<finish> ...");
        if (!Enum.TryParse(parts[1], true, out Material m)) throw error($"'{parts[1]}' is not a material");
        string role = parts[2].ToLowerInvariant();
        if (role is not ("wall" or "floor" or "roof" or "decor" or "rock" or "any")) throw error($"'{role}' is not a finish role (wall, floor, roof, decor, rock, any)");
        foreach (string p in parts.Skip(3))
        {
            int colon = p.IndexOf(':');
            if (colon < 0) throw error($"'{p}' is not side:finish");
            string side = p[..colon];
            FinishSpec spec = FinishSpec.Parse(p[(colon + 1)..], error);
            if (side == "all") foreach (Side s in Enum.GetValues<Side>()) Set(m, role, s, spec);
            else if (Enum.TryParse(side, true, out Side s)) Set(m, role, s, spec);
            else throw error($"'{side}' is not a side (out, in, floor, ceiling, roof, soffit, all)");
        }
    }
}

/// <summary>A room purpose's own finishes inside (data/finishes.txt "room" lines): its floor, walls and ceiling.</summary>
public sealed record RoomFinish(FinishSpec? Floor, FinishSpec? Wall, FinishSpec? Ceiling);

/// <summary>Every finish rule in data/finishes.txt: the defaults by material, and what each room purpose is finished in inside.</summary>
public sealed class FinishBook
{
    public readonly FinishTable Defaults = new();
    public readonly Dictionary<string, RoomFinish> Rooms = new();
    /// <summary>Materials a room's own finish may go over (an office's carpet over a concrete slab, never over chitin).</summary>
    public readonly HashSet<Material> Built = new();

    /// <summary>The book the library last loaded, for places spawned without one at hand.</summary>
    public static FinishBook Current = new();

    public static FinishBook Parse(string text, string source)
    {
        var book = new FinishBook();
        int n = 0;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            n++;
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            string[] p = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            Exception Error(string why) => new FormatException($"{source}:{n}: {why}");
            switch (p[0])
            {
                case "default": book.Defaults.ParseLine(p, Error); break;
                case "built":
                    foreach (string m in p.Skip(1)) book.Built.Add(Enum.TryParse(m, true, out Material mm) ? mm : throw Error($"'{m}' is not a material"));
                    break;
                case "room":
                {
                    FinishSpec? floor = null, wall = null, ceiling = null;
                    var purposes = new List<string>();
                    foreach (string q in p.Skip(1))
                    {
                        int colon = q.IndexOf(':');
                        if (colon < 0) { purposes.Add(q); continue; }
                        FinishSpec spec = FinishSpec.Parse(q[(colon + 1)..], Error);
                        switch (q[..colon])
                        {
                            case "floor": floor = spec; break;
                            case "wall": wall = spec; break;
                            case "ceiling": ceiling = spec; break;
                            default: throw Error($"'{q[..colon]}' is not floor, wall or ceiling");
                        }
                    }
                    foreach (string purpose in purposes) book.Rooms[purpose] = new RoomFinish(floor, wall, ceiling);
                    break;
                }
                default: throw Error($"unknown keyword '{p[0]}'");
            }
        }
        Current = book;
        return book;
    }
}

/// <summary>
/// How one place's faces are finished and how far the years have taken them: the rules its kit and the
/// finish book give, its rooms' purposes, its tech and the decay's state (wear, fire, water). It rides on
/// the place's block grid (<see cref="BlockGrid.Look"/>), so every drawing of the place, near or far,
/// remeshed after damage or not, asks the same questions and gets the same answers. Read-only once
/// made: meshing runs on workers.
/// </summary>
public sealed class PlaceLook
{
    public FinishBook Book = FinishBook.Current;
    public FinishTable? Kit;
    /// <summary>Its rooms keep the kit's finishes whatever they are for (alien and faction kits).</summary>
    public bool KitRooms;
    public TechLevel Tech = TechLevel.Mid;
    public bool Alien;
    /// <summary>0 as built and kept to 1 two centuries of weather, as the decay pass reads it.</summary>
    public float Wear;
    /// <summary>How much of the fall was fire and blast (0 to 1): scorch.</summary>
    public float Fire;
    /// <summary>How wet the ground is (0 dry to 1 drowned): damp climbs higher.</summary>
    public float Water;
    /// <summary>Picks the place's own small choices (its brick bond, which way its boards run).</summary>
    public int Seed;
    /// <summary>The purpose of the room standing on each floor cell (its walk-level cells), by index into <see cref="Purposes"/>.</summary>
    readonly Dictionary<Int3, byte> _rooms = new();
    public readonly List<string> Purposes = new() { "" };
    /// <summary>Each room slot's own wall paint inside (null: its blocks' colour): a slot of its own for every room its builder painted.</summary>
    public readonly List<Rgb?> Paints = new() { null };
    /// <summary>The colour the place's ceilings are painted inside, where its builder said (a house's).</summary>
    public Rgb? Ceiling;
    /// <summary>A far lump's roofs: the finish of the roofs in each of its cells (Mesher.Coarse), by cell index.</summary>
    public byte[]? CoarseRoofs;
    public Int3 CoarseSize;

    /// <summary>The place's look, from what it is and what has happened to it.</summary>
    public static PlaceLook For(PoiInstance inst)
    {
        Kit kit = inst.Kit;
        var look = new PlaceLook
        {
            Kit = kit.Finishes.IsEmpty ? null : kit.Finishes,
            KitRooms = kit.KeepRooms,
            Tech = inst.Tech ?? TechLevel.Mid,
            Alien = kit.Alien || kit.Id.StartsWith("alien") || kit.Faction is Faction.Vharn or Faction.Kessari || kit.Id == "precursor",
            Seed = (int)(Hash.Combine(inst.Layout.Seed, 0x5EAFUL) & 0xFFF),
        };
        RuinAge age = inst.Ruin;
        bool ruined = inst.State == PoiState.Ruined;
        float wear = inst.State == PoiState.Active ? 0.12f : age.Wear * (ruined ? 1f : 0.75f) * (inst.Age != Age.OldWorld ? 0.45f : 1f);
        if (inst.Layout.Extras.InSpace) wear *= 0.5f;
        look.Wear = Math.Clamp(wear + (inst.State == PoiState.Infested ? 0.15f : 0f), 0f, 1f);
        look.Fire = ruined || inst.Blasted > 0f ? Math.Clamp(Math.Max(age.Violence, inst.Blasted), 0f, 1f) : inst.State == PoiState.Abandoned ? age.Violence * 0.3f : 0f;
        look.Water = Math.Clamp(age.Water, 0f, 1f);
        look.Ceiling = inst.Layout.Extras.Ceiling;
        foreach (PlacedModule m in inst.Layout.Modules)
        {
            string purpose = m.Purpose;
            if (purpose.Length == 0 && m.Kind == ModuleKind.Corridor) purpose = "corridor";
            bool painted = inst.Layout.Extras.Paints.TryGetValue(m.Index, out Rgb paint);
            if (purpose.Length == 0 || !painted && !look.Book.Rooms.ContainsKey(purpose)) continue;
            int i = painted ? -1 : look.Purposes.IndexOf(purpose);
            if (i < 0) { i = look.Purposes.Count; look.Purposes.Add(purpose); look.Paints.Add(painted ? paint : null); }
            if (i > 255) continue;
            foreach (Int3 c in m.Cells)
                if (c.Y == m.Min.Y) look._rooms[c] = (byte)i;
        }
        return look;
    }

    /// <summary>
    /// Gives a spawned place its look, and fixes each wall's and roof's outer finish on the block (its
    /// Variant) while its colour is still its own: the world's paint, which comes after, may tint a log
    /// wall or a thatch, and what it is made of must not change with it. A builder's own choice stays.
    /// </summary>
    public static void Stamp(PoiInstance inst)
    {
        PlaceLook look = For(inst);
        inst.Blocks.Look = look;
        BlockGrid g = inst.Blocks;
        // Only what stands: walking every cell of a big works' box would make every empty chunk of it.
        foreach (var (c, found) in g.Blocks().ToList())
        {
            if (found.Variant != 0 || found.Type is not (BlockType.Cube or BlockType.Ramp or BlockType.Corner or BlockType.InvCorner) && !BlockInfo.IsShaped(found.Type)) continue;
            ref Block b = ref g.At(c);
            if (b.Role is not (Role.Wall or Role.Roof or Role.Decor) || b.Material is Material.Glass or Material.Lumen or Material.Glyph or Material.Crystal) continue;
            bool slope = b.Type is BlockType.Ramp or BlockType.Corner or BlockType.InvCorner || BlockInfo.IsHung(b.Type);
            Finish f = look.Pick(b, b.Role == Role.Roof || slope ? Side.Roof : Side.Out, slope, 0).Finish;
            if (f != Finish.Plain) b.Variant = (byte)(1 + (int)f);
        }
    }

    /// <summary>The room whose floor is this cell, as an index into <see cref="Purposes"/> (0: none).</summary>
    public int RoomAt(Int3 floorCell) => _rooms.TryGetValue(floorCell, out byte i) ? i : 0;

    /// <summary>A room slot's own wall paint (null: its blocks' colour).</summary>
    public Rgb? PaintOf(int room) => room > 0 && room < Paints.Count ? Paints[room] : null;

    /// <summary>
    /// The colour a face is drawn: a wall's inside or a ceiling coated (plaster, render, tiles) in the paint its room was
    /// done in and the place's ceiling colour, else its block's own.
    /// </summary>
    public Rgb Colour(Block b, Side side, Finish f, int room) =>
        f is Finish.Plaster or Finish.Render or Finish.WallTile or Finish.CeilingTile or Finish.Precast
        && (side == Side.In ? PaintOf(room) : side == Side.Ceiling && room > 0 ? Ceiling : null) is Rgb paint ? paint : b.Color;

    /// <summary>A copy for a far lump of the place: its age and rules, none of its rooms.</summary>
    public PlaceLook Far(byte[] roofs, Int3 size) => new()
    {
        Book = Book, Kit = Kit, KitRooms = KitRooms, Tech = Tech, Alien = Alien, Wear = Wear, Fire = Fire, Water = Water, Seed = Seed,
        CoarseRoofs = roofs, CoarseSize = size,
    };

    /// <summary>A face's finish: the block's own say, then its room's, then its kit's, then the book's defaults.</summary>
    public FinishPick Pick(Block b, Side side, bool slope, int roomIndex)
    {
        string room = roomIndex > 0 && roomIndex < Purposes.Count ? Purposes[roomIndex] : "";
        Finish own = Surfaces.Own(b);
        // A roof's own finish is its covering, on top; its edges and underside go by the rules.
        if (own != Finish.Plain && (b.Role == Role.Roof ? side == Side.Roof : side is Side.Out or Side.Roof or Side.Soffit)) return new FinishPick(own);
        string role = Surfaces.RoleName(b.Role);
        if (room.Length > 0 && !KitRooms && Book.Built.Contains(b.Material) && Book.Rooms.TryGetValue(room, out RoomFinish? rf))
        {
            FinishSpec? r = side switch { Side.Floor => rf.Floor, Side.Ceiling => rf.Ceiling, Side.In => rf.Wall, _ => null };
            if (r != null) return Choose(r, b, slope);
        }
        if (own != Finish.Plain && b.Role != Role.Roof && side == Side.In)
        {
            // Inside, a wall of logs, boards, plate or poured concrete shows what it is made of; brick, stone,
            // render and weatherboard are plastered over.
            Finish inside = own switch
            {
                Finish.Weatherboard or Finish.Render or Finish.Brick or Finish.Stone or Finish.Rubble => Finish.Plaster,
                Finish.Paving or Finish.Tar or Finish.RoofTile or Finish.Slate or Finish.Shingle or Finish.Thatch or Finish.Seam => Finish.Plain,
                _ => own,
            };
            if (inside != Finish.Plain) return new FinishPick(inside);
        }
        FinishSpec? spec = Kit?.Find(b.Material, role, side) ?? Book.Defaults.Find(b.Material, role, side);
        return spec == null ? new FinishPick(Finish.Plain) : Choose(spec, b, slope);
    }

    FinishPick Choose(FinishSpec spec, Block b, bool slope)
    {
        foreach (var (when, pick) in spec.Choices)
            if (when == null || Holds(when, b, slope)) return pick;
        return new FinishPick(Finish.Plain);
    }

    bool Holds(string when, Block b, bool slope) => when switch
    {
        "painted" => Surfaces.Painted(b.Color, b.Material),
        "grey" => !Surfaces.Painted(b.Color, b.Material),
        "wood" => Surfaces.Woody(b.Color),
        "straw" => Surfaces.Straw(b.Color),
        "dark" => Surfaces.Luma(b.Color) < 0.3f,
        "light" => Surfaces.Luma(b.Color) > 0.62f,
        "slope" => slope,
        "flat" => !slope,
        "low" => Tech == TechLevel.Low,
        "mid" => Tech == TechLevel.Mid,
        "high" => Tech == TechLevel.High,
        "alien" => Alien,
        _ => false,
    };

    /// <summary>The place's own pick of a finish's small choice (its bond, its board direction), the same all over it.</summary>
    public int ParamOf(FinishPick p)
    {
        if (p.Param >= 0) return p.Param;
        uint h = Hash.Cell(new Int3((int)p.Finish, Seed, 7), 0xF1);
        return p.Finish switch
        {
            // Stretcher bond most of all; Flemish and English on older streets.
            Finish.Brick => (h % 10) switch { < 5 => 0, < 7 => 1, < 9 => 2, _ => 3 },
            Finish.Stone => Tech == TechLevel.High ? 1 : (int)(h % 3),
            Finish.Tile => (int)(h % 4),
            Finish.RoofTile => (int)(h % 3),
            Finish.Shingle => Tech == TechLevel.Low ? 0 : (int)(h % 2),
            Finish.Weatherboard => (int)(h % 3),
            Finish.Planks or Finish.Parquet or Finish.Carpet or Finish.Paving => (int)(h % 2),
            Finish.Poured => (int)(h % 2),
            _ => (int)(h % 4),
        };
    }
}

/// <summary>The rules' small helpers and the face code the place shader reads.</summary>
public static class Surfaces
{
    /// <summary>A block's own finish (its Variant on a wall, floor or roof cube or slope), or Plain.</summary>
    public static Finish Own(Block b) =>
        b.Variant != 0 && (b.Type is BlockType.Cube or BlockType.Ramp or BlockType.Corner or BlockType.InvCorner || BlockInfo.IsShaped(b.Type)) && b.Variant <= (byte)Finish.Glass + 1
            ? (Finish)(b.Variant - 1) : Finish.Plain;

    /// <summary>What a house's cladding is drawn as (HouseStyle.Wall and Cover words), as a block's Variant: set it on the block and the house draws its real surface.</summary>
    public static byte HouseVariant(string word) => (byte)(1 + (int)(word switch
    {
        "brick" => Finish.Brick, "board" => Finish.Weatherboard, "timber" or "logs" => Finish.Logs, "stone" => Finish.Stone,
        "rubble" => Finish.Rubble, "render" or "adobe" => Finish.Render, "panel" => Finish.Precast, "halftimber" => Finish.Render,
        "tile" => Finish.RoofTile, "glazed" => Finish.RoofTile, "slate" => Finish.Slate, "metal" => Finish.Seam, "thatch" => Finish.Thatch,
        "shingle" => Finish.Shingle, "membrane" => Finish.Tar, "cap" => Finish.Resin, "frame" or "beam" => Finish.Planks,
        _ => Finish.Plain,
    }) & 0xFF);

    public static string RoleName(Role r) => r switch
    {
        Role.Wall or Role.Secret or Role.Window or Role.Door => "wall",
        Role.Floor or Role.Traverse => "floor",
        Role.Roof => "roof",
        Role.Decor or Role.Light => "decor",
        Role.Rock => "rock",
        _ => "any",
    };

    public static FinishPick ParsePick(string text, Func<string, Exception> error)
    {
        string name = text, param = "";
        int dot = text.IndexOf('.');
        if (dot >= 0) { name = text[..dot]; param = text[(dot + 1)..]; }
        if (!Enum.TryParse(name, true, out Finish f)) throw error($"'{name}' is not a finish");
        int p = param.Length == 0 ? -1 : ParamNames.TryGetValue(param, out int v) ? v : int.TryParse(param, out int n) && n is >= 0 and <= 3 ? n : throw error($"'{param}' is not a choice of {name}");
        return new FinishPick(f, p);
    }

    /// <summary>The small choices by name: brick bonds, stone coursing, tile layouts, board runs.</summary>
    static readonly Dictionary<string, int> ParamNames = new()
    {
        ["stretcher"] = 0, ["flemish"] = 1, ["english"] = 2, ["stack"] = 3,
        ["coursed"] = 0, ["ashlar"] = 1, ["big"] = 2,
        ["square"] = 0, ["checker"] = 1, ["mosaic"] = 2, ["large"] = 3,
        ["pantile"] = 0, ["plain"] = 1, ["scale"] = 2,
        ["wood"] = 0, ["asphalt"] = 1,
        ["lap"] = 0, ["shiplap"] = 1, ["batten"] = 2,
        ["x"] = 0, ["z"] = 1, ["board"] = 0, ["panel"] = 1, ["tiles"] = 1, ["slabs"] = 0, ["flags"] = 1, ["rusty"] = 1, ["clean"] = 0,
    };

    // ---- Colour tests ---------------------------------------------------------------------------

    public static float Luma(Rgb c) => (0.3f * c.R + 0.59f * c.G + 0.11f * c.B) / 255f;

    static (float H, float S, float L) Hsl(Rgb c)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b)), l = (max + min) / 2f, d = max - min;
        if (d < 1e-4f) return (0f, 0f, l);
        float s = d / (1f - MathF.Abs(2f * l - 1f));
        float h = max == r ? (g - b) / d % 6f : max == g ? (b - r) / d + 2f : (r - g) / d + 4f;
        h *= 60f;
        if (h < 0f) h += 360f;
        return (h, s, l);
    }

    /// <summary>A colour that reads as wood: a warm brown, not too bright, not grey.</summary>
    public static bool Woody(Rgb c)
    {
        var (h, s, l) = Hsl(c);
        return h is >= 12f and <= 48f && s is > 0.12f and < 0.65f && l is > 0.1f and < 0.55f;
    }

    /// <summary>A colour that reads as straw: a golden yellow-brown.</summary>
    public static bool Straw(Rgb c)
    {
        var (h, s, l) = Hsl(c);
        return h is >= 32f and <= 62f && s > 0.22f && l > 0.3f;
    }

    /// <summary>A coat of paint rather than the bare stuff: tinted or bright where the bare material would be grey (concrete) or brown (timber).</summary>
    public static bool Painted(Rgb c, Material m)
    {
        var (_, s, l) = Hsl(c);
        return m switch
        {
            Material.Timber => !Woody(c) && !Straw(c),
            Material.Steel or Material.Alloy => s > 0.2f || l > 0.7f,
            _ => s > 0.1f || l > 0.66f,
        };
    }

    // ---- The face code ----------------------------------------------------------------------------

    /// <summary>What lies under a coat, for when it falls away: render over brick, plaster over block or lath.</summary>
    public enum Under : byte { None, Brick, Block, Stone, Lath, Poured }

    /// <summary>
    /// A face's code for the place shader, an integer a float carries exactly: the finish (6 bits), its
    /// choice (2), wear (2), height above the floor in cells (2), indoors (1), damp (1), scorch (2),
    /// painted (1), what is under the coat (3), and four edge flags (an opening above, an opening below,
    /// a corner, or a pane's neighbours).
    /// </summary>
    public static float Code(Finish f, int param, int wear, int height, bool indoors, bool damp, int scorch, bool painted, Under under, int edges) =>
        (int)f | (param & 3) << 6 | (wear & 3) << 8 | (Math.Min(height, 3) & 3) << 10 | (indoors ? 1 : 0) << 12 | (damp ? 1 : 0) << 13
        | (scorch & 3) << 14 | (painted ? 1 : 0) << 16 | ((int)under & 7) << 17 | (edges & 15) << 20;

    public static Finish FinishOf(float code) => (Finish)((int)code & 63);

    /// <summary>Whether a finish is a coat over something else (it can peel and show what is under it).</summary>
    public static bool Coated(Finish f) => f is Finish.Render or Finish.Plaster or Finish.Block or Finish.Weatherboard or Finish.Precast or Finish.Plate or Finish.Corrugated or Finish.Hull;
}
