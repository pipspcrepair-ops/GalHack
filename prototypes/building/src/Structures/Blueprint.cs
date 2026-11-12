using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GalHack.Structures;

/// <summary>One block statement from a blueprint, after range expansion. Names are kept as text until validation.</summary>
public sealed class BlueprintBlock
{
    public Int3 Pos;
    public string Type, Shape, Rot;
    public string[] Paint; // six face paint names (+x -x +y -y +z -z), "-" for unpainted; null when unpainted
    public float? Hp;
    public BlockFlags Flags;
    public LightSettings? Light;
    public int Line;
}

/// <summary>A parsed blueprint, before it is checked against the catalogue.</summary>
public sealed class BlueprintData
{
    public string Schema;
    public int Version;
    public string Name = "Unnamed";
    public string Grid = "large";
    public CoreMode Mode = CoreMode.Capital;
    public bool Gravity = true;
    public double Cargo;
    public readonly List<(string name, Paint paint, int line)> Paints = new();
    public readonly List<BlueprintBlock> Blocks = new();
    public readonly List<string> Errors = new();
}

/// <summary>
/// The blueprint text format: a human-readable block list that people and agents can write by
/// hand and the game loads and saves. One statement per line; '#' starts a comment.
///
///   schema galhack.blueprint
///   version 1
///   name "Foss Lancer"
///   grid large                       # large (2 m) or small (0.5 m)
///   mode capital                     # base or capital
///   paint hull base=e9dfc4 grime=1   # base accent pattern texture stencil grime
///   block 0 1 0 core
///   block -3..3 0 -8..8 steel paint=hull
///   block 4 1 0 hardened slope rot=+y+x paint=hull,stripe,hull,hull,hull,hull
///
/// Coordinates are cells (x right, y up, -z forward) and a..b is an inclusive range. rot gives
/// where the block's up and forward point (default +y-z). paint is one name for all faces or six
/// (+x -x +y -y +z -z, '-' for bare); "+x=name" overrides a single face. Saving always writes the
/// canonical form (runs merged along x, defaults omitted), so save → load → save is byte-identical.
/// </summary>
public static class Blueprint
{
    public const string SchemaName = "galhack.blueprint";
    public const int CurrentVersion = 1;
    const int MaxBlocks = 2_000_000;
    const int MaxCoordinate = 100_000;

    // ---- Loading ----------------------------------------------------------------------------

    /// <summary>Parses, validates and builds. Returns null (and the errors) if the blueprint is bad.</summary>
    public static Structure Load(string text, Catalogue catalogue, out List<string> errors, string owner = Building.DefaultOwner)
    {
        BlueprintData data = Parse(text);
        OldDoorTops(data, catalogue);
        errors = Validate(data, catalogue);
        return errors.Count > 0 ? null : Build(data, catalogue, owner);
    }

    public static BlueprintData Parse(string text)
    {
        var data = new BlueprintData();
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        int total = 0;
        for (int li = 0; li < lines.Length; li++)
        {
            int line = li + 1;
            List<string> t;
            try { t = Tokenize(lines[li]); }
            catch (FormatException e) { data.Errors.Add($"line {line}: {e.Message}"); continue; }
            if (t.Count == 0) continue;

            void Error(string what) => data.Errors.Add($"line {line}: {what}");
            string Arg(int i) => i < t.Count ? t[i] : null;

            switch (t[0])
            {
                case "schema": data.Schema = Arg(1); break;
                case "version":
                    if (!int.TryParse(Arg(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out data.Version)) Error("bad version");
                    break;
                case "name": data.Name = Arg(1) ?? ""; break;
                case "grid": data.Grid = Arg(1); break;
                case "mode":
                    if (Enum.TryParse(Arg(1), true, out CoreMode mode) && !int.TryParse(Arg(1), out _)) data.Mode = mode;
                    else Error($"mode must be base, capital, vessel or hover, not '{Arg(1)}'");
                    break;
                case "gravity":
                    if (Arg(1) is "on" or "off") data.Gravity = Arg(1) == "on";
                    else Error("gravity must be on or off");
                    break;
                case "cargo":
                    if (!double.TryParse(Arg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out data.Cargo) || data.Cargo < 0)
                        Error("bad cargo amount");
                    break;
                case "paint": ParsePaint(t, line, data, Error); break;
                case "block": total += ParseBlock(t, line, data, Error, total); break;
                default: Error($"unknown statement '{t[0]}'"); break;
            }
        }
        return data;
    }

    static void ParsePaint(List<string> t, int line, BlueprintData data, Action<string> error)
    {
        if (t.Count < 2 || !IsName(t[1])) { error("paint needs a name (letters, digits, _ - .)"); return; }
        uint? baseColor = null;
        var p = Paint.Solid(0);
        foreach (string kv in t.Skip(2))
        {
            string[] parts = kv.Split('=', 2);
            if (parts.Length != 2) { error($"expected key=value, got '{kv}'"); continue; }
            string v = parts[1];
            switch (parts[0])
            {
                case "base":
                    if (Paint.TryParseHex(v, out uint b)) baseColor = b; else error($"bad colour '{v}' (use rrggbb)");
                    break;
                case "accent":
                    if (Paint.TryParseHex(v, out uint a)) p = p with { Accent = a }; else error($"bad colour '{v}' (use rrggbb)");
                    break;
                case "pattern":
                    if (Enum.TryParse(v, true, out Pattern pat) && !int.TryParse(v, out _)) p = p with { Pattern = pat };
                    else error($"unknown pattern '{v}'");
                    break;
                case "texture":
                    if (Enum.TryParse(v, true, out FaceTexture tex) && !int.TryParse(v, out _)) p = p with { Texture = tex };
                    else error($"unknown texture '{v}'");
                    break;
                case "stencil":
                    if (v.Length == 1 && char.IsAsciiDigit(v[0])) p = p with { Stencil = (byte)v[0] };
                    else error($"stencil must be one digit, not '{v}'");
                    break;
                case "lamp":
                    if (Enum.TryParse(v, true, out LampShape shape) && shape != LampShape.None && !int.TryParse(v, out _)) p = p with { Shape = shape };
                    else error($"unknown lamp shape '{v}' (full strip panel border)");
                    break;
                case "light":
                    if (LightSettings.TryParse(v, out LightSettings ls)) p = p with { Lamp = ls };
                    else error($"bad light '{v}'");
                    break;
                case "grime":
                    if (byte.TryParse(v, out byte g) && g <= 3) p = p with { Grime = g };
                    else error("grime must be 0 to 3");
                    break;
                default: error($"unknown paint key '{parts[0]}'"); break;
            }
        }
        if (baseColor == null) { error("paint needs base=rrggbb"); return; }
        if (data.Paints.Any(x => x.name == t[1])) { error($"paint '{t[1]}' defined twice"); return; }
        data.Paints.Add((t[1], p with { Base = baseColor.Value }, line));
    }

    static int ParseBlock(List<string> t, int line, BlueprintData data, Action<string> error, int total)
    {
        if (t.Count < 5) { error("block needs x y z and a type"); return 0; }
        if (!TryRange(t[1], out int x0, out int x1) || !TryRange(t[2], out int y0, out int y1) || !TryRange(t[3], out int z0, out int z1))
        {
            error("bad coordinate (use an integer or a..b)");
            return 0;
        }

        var proto = new BlueprintBlock { Type = t[4], Line = line };
        string[] paint = null;
        foreach (string token in t.Skip(5))
        {
            string[] kv = token.Split('=', 2);
            if (kv.Length == 1)
            {
                if (token == "open") proto.Flags |= BlockFlags.Open;
                else if (token == "off") proto.Flags |= BlockFlags.Off;
                else if (proto.Shape == null) proto.Shape = token;
                else error($"unexpected '{token}'");
                continue;
            }
            string key = kv[0], v = kv[1];
            if (key == "rot") proto.Rot = v;
            else if (key == "hp")
            {
                if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float hp)) proto.Hp = hp;
                else error($"bad hp '{v}'");
            }
            else if (key == "light")
            {
                if (LightSettings.TryParse(v, out LightSettings ls)) proto.Light = ls;
                else error($"bad light '{v}' (use rrggbb:mode:speed:seed, modes steady pulse flicker strobe fade)");
            }
            else if (key == "paint")
            {
                string[] names = v.Split(',');
                if (names.Length == 1) paint = Enumerable.Repeat(names[0], 6).ToArray();
                else if (names.Length == 6) paint = names;
                else error("paint takes one name or six (+x,-x,+y,-y,+z,-z)");
            }
            else if (Dirs.TryParse(key, out Dir face))
            {
                paint ??= Enumerable.Repeat("-", 6).ToArray();
                paint[(int)face] = v;
            }
            else error($"unknown block key '{key}'");
        }
        proto.Paint = paint;

        long count = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
        if (total + count > MaxBlocks) { error($"more than {MaxBlocks} blocks"); return 0; }
        for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    data.Blocks.Add(new BlueprintBlock
                    {
                        Pos = new Int3(x, y, z), Type = proto.Type, Shape = proto.Shape, Rot = proto.Rot,
                        Paint = proto.Paint, Hp = proto.Hp, Flags = proto.Flags, Light = proto.Light, Line = line,
                    });
        return (int)count;
    }

    /// <summary>
    /// Rejects bad data: syntax errors, a missing or second core, overlapping blocks, unknown
    /// ids, and blocks that claim to belong to the structure but are not attached to its core.
    /// </summary>
    public static List<string> Validate(BlueprintData data, Catalogue catalogue)
    {
        var errors = new List<string>(data.Errors);
        if (data.Schema != SchemaName) errors.Add($"schema must be '{SchemaName}' (found '{data.Schema ?? "nothing"}')");
        if (data.Version != CurrentVersion) errors.Add($"unsupported version {data.Version} (expected {CurrentVersion})");
        GridDef grid = catalogue.GridById(data.Grid ?? "");
        if (grid == null) { errors.Add($"unknown grid '{data.Grid}'"); return errors; }

        var paintNames = new HashSet<string>(data.Paints.Select(p => p.name));
        var scratch = new Structure(catalogue, grid);
        var lineOf = new Dictionary<Int3, int>();
        var cellLine = new Dictionary<Int3, int>();
        int cores = 0;
        foreach (BlueprintBlock b in data.Blocks)
        {
            void Error(string what) => errors.Add($"line {b.Line}: {what}");
            if (!catalogue.TryGetType(b.Type, out BlockType type)) { Error($"unknown block id '{b.Type}'"); continue; }
            ShapeDef shape = catalogue.Cube;
            if (b.Shape != null)
            {
                if (!type.IsArmor) { Error($"'{b.Type}' is a component and takes no shape"); continue; }
                if (!catalogue.TryGetShape(b.Shape, out shape)) { Error($"unknown shape '{b.Shape}'"); continue; }
            }
            int rot = Orientations.Identity;
            if (b.Rot != null && !Orientations.TryParse(b.Rot, out rot)) { Error($"bad rot '{b.Rot}' (up then forward, e.g. +y-z)"); continue; }
            if (b.Paint != null)
                foreach (string name in b.Paint.Where(n => n != "-" && !paintNames.Contains(n)).Distinct())
                    Error($"unknown paint '{name}'");
            if ((b.Flags & BlockFlags.Open) != 0 && !type.IsDoor) Error($"only doors can be open, not '{b.Type}'");
            if ((b.Flags & BlockFlags.Off) != 0 && !type.IsLight) Error($"only lights can be off, not '{b.Type}'");
            if (Math.Abs(b.Pos.X) > MaxCoordinate || Math.Abs(b.Pos.Y) > MaxCoordinate || Math.Abs(b.Pos.Z) > MaxCoordinate)
                Error("coordinate out of range");

            Block block = Building.Make(scratch, type, shape, rot);
            if (b.Hp is float hp && (hp <= 0 || hp > block.Hp + 0.05f)) Error($"hp {hp} is outside 0..{block.Hp:0.#}");
            // A block spanning several cells is saved at its anchor; its other cells are rebuilt here,
            // and any of them landing on another block's cell is an error, never a silent overwrite.
            int other = -1;
            foreach (Int3 cell in scratch.CellsOf(b.Pos, block))
                if (cellLine.TryGetValue(cell, out other)) break;
            if (other >= 0)
            {
                Error(Footprint.IsMulti(type) ? $"block at {b.Pos} ({type.Size.X}x{type.Size.Y}x{type.Size.Z} cells) overlaps the one from line {other}"
                    : $"block at {b.Pos} overlaps the one from line {other}");
                continue;
            }
            foreach (Int3 cell in scratch.CellsOf(b.Pos, block)) cellLine[cell] = b.Line;
            lineOf[b.Pos] = b.Line;
            if (type.IsCore) cores++;
            scratch.Set(b.Pos, block);
        }

        if (cores == 0) errors.Add("no core: every structure needs exactly one core block");
        if (cores > 1) errors.Add($"{cores} cores: a structure has exactly one");
        if (cores == 1 && scratch.Core is Int3 core)
        {
            HashSet<Int3> attached = Connectivity.Reachable(scratch, core);
            var detached = lineOf.Keys.Where(p => !attached.Contains(p)).OrderBy(p => lineOf[p]).ToList();
            foreach (Int3 p in detached.Take(8))
                errors.Add($"line {lineOf[p]}: block at {p} is not attached to the core");
            if (detached.Count > 8) errors.Add($"... and {detached.Count - 8} more detached blocks");
        }
        return errors;
    }

    /// <summary>Builds a structure from validated data.</summary>
    public static Structure Build(BlueprintData data, Catalogue catalogue, string owner = Building.DefaultOwner)
    {
        var s = new Structure(catalogue, catalogue.GridById(data.Grid))
        {
            Name = data.Name,
            Mode = data.Mode,
            GravityOn = data.Gravity,
            CargoSu = data.Cargo,
            Owner = owner,
        };
        var slots = new Dictionary<string, ushort> { ["-"] = 0 };
        foreach (var (name, paint, _) in data.Paints) slots[name] = s.Palette.Intern(paint, name);

        foreach (BlueprintBlock b in data.Blocks)
        {
            BlockType type = catalogue.Type(b.Type);
            ShapeDef shape = b.Shape != null ? catalogue.Shape(b.Shape) : catalogue.Cube;
            Orientations.TryParse(b.Rot ?? "+y-z", out int rot);
            Block block = Building.Make(s, type, shape, rot);
            block.Flags = b.Flags;
            if (b.Hp is float hp) block.Hp = MathF.Min(hp, block.Hp);
            if (b.Paint != null)
                foreach (Dir d in Dirs.All) block.Paint[d] = slots[b.Paint[(int)d]];
            s.Set(b.Pos, block);
            if (b.Light is LightSettings light) s.SetLight(b.Pos, light);
        }
        if (data.Blocks.Any(b => b.Type == "door")) WallOverDoors(s);
        if (s.Core == null) s.Owner = null;
        // Old blueprints said "capital" on every small grid, and a hover pad made the craft a hover
        // vessel: they load in Hover Mode. A blueprint saved since says "vessel" or "hover" itself.
        if (data.Mode == CoreMode.Capital && s.Grid.Size == GridSize.Small && s.Blocks().Any(x => s.TypeOf(x.block).Function == "hover"))
            s.Mode = CoreMode.Hover;
        return s;
    }

    // ---- Doors two cells tall, for a while -----------------------------------------------------
    // For a short while (design 702 to 708) a large door was two cells, a 3 m leaf under a top cell ("door-head")
    // that took the place of the wall over it. The door is one block again; a blueprint saved then still loads,
    // its door with the wall block over it once more.

    /// <summary>A door's top named in a blueprint becomes the wall block it was: steel, its paint kept, whole.</summary>
    static void OldDoorTops(BlueprintData data, Catalogue catalogue)
    {
        if (catalogue.TryGetType("door-head", out _)) return;
        foreach (BlueprintBlock b in data.Blocks.Where(b => b.Type == "door-head"))
        {
            b.Type = "steel";
            b.Hp = null;
            b.Flags &= ~BlockFlags.Open;
        }
    }

    /// <summary>
    /// A top saved unnamed (it wore its door's paint) left a gap over the door, between the wall's blocks either
    /// side of it: the wall closes over it again, a block like its neighbour's.
    /// </summary>
    static void WallOverDoors(Structure s)
    {
        if (s.Grid.Size != GridSize.Large) return;
        foreach (var (p, b) in s.Blocks().Where(x => s.TypeOf(x.block).IsDoor && !s.TypeOf(x.block).Multi).ToList())
        {
            Int3 over = p + Orientations.ToWorld(b.Rot, Dir.PosY).Vec(), side = Orientations.ToWorld(b.Rot, Dir.PosX).Vec();
            if (s.Has(over) || !s.TryGet(over + side, out Block left) || !s.TryGet(over - side, out Block right)) continue;
            if (!s.TypeOf(left).IsArmor || !s.TypeOf(right).IsArmor) continue;
            Block wall = left;
            wall.Hp = s.StatsOf(wall).MaxHp;
            wall.Flags = BlockFlags.None;
            s.Set(over, wall);
        }
    }

    // ---- Saving -----------------------------------------------------------------------------

    /// <summary>Writes the canonical text: header, the paints in use, then blocks sorted by y, z, x with runs along x merged.</summary>
    public static string Save(Structure s)
    {
        var sb = new StringBuilder();
        void Line(string text) => sb.Append(text).Append('\n');

        Line("# GalHack blueprint. One statement per line; '#' starts a comment.");
        Line("# block <x> <y> <z> <type> [shape] [rot=<up><forward>] [paint=<name> | paint=<+x>,<-x>,<+y>,<-y>,<+z>,<-z>] [hp=<n>] [open] [off] [light=<rrggbb[+rrggbb]>:<mode>:<hz>:<seed>]");
        Line("# Cells: x right, y up, -z forward; a..b is an inclusive range. rot defaults to +y-z; '-' is bare metal.");
        Line($"schema {SchemaName}");
        Line($"version {CurrentVersion}");
        Line($"name {Quote(s.Name)}");
        Line($"grid {s.Grid.Id}");
        Line($"mode {s.Mode.ToString().ToLowerInvariant()}");
        Line($"gravity {(s.GravityOn ? "on" : "off")}");
        if (s.CargoSu > 0) Line($"cargo {Num(s.CargoSu)}");

        var blocks = s.Blocks().OrderBy(x => x.pos.Y).ThenBy(x => x.pos.Z).ThenBy(x => x.pos.X).ToList();
        var used = new SortedSet<ushort>();
        foreach (var (_, b) in blocks)
            foreach (Dir d in Dirs.All)
                if (b.Paint[d] != 0) used.Add(b.Paint[d]);
        if (used.Count > 0) Line("");
        foreach (ushort slot in used) Line(PaintLine(s.Palette.NameOf(slot), s.Palette[slot]));

        // Runs merge on the written text, so blocks that print the same always merge the same way
        // (two damaged blocks rounding to the same hp merge on the first save, not only the second).
        Line("");
        string[] tails = blocks.Select(x => BlockTail(s, x.block, x.pos)).ToArray();
        for (int i = 0; i < blocks.Count;)
        {
            Int3 start = blocks[i].pos;
            int j = i + 1;
            while (j < blocks.Count && blocks[j].pos == start + new Int3(j - i, 0, 0) && tails[j] == tails[i]) j++;
            int end = start.X + (j - i - 1);
            string x = end == start.X ? I(start.X) : $"{I(start.X)}..{I(end)}";
            Line($"block {x} {I(start.Y)} {I(start.Z)} {tails[i]}");
            i = j;
        }
        return sb.ToString();
    }

    static string PaintLine(string name, Paint p)
    {
        var sb = new StringBuilder($"paint {name} base={Paint.Hex(p.Base)}");
        if (p.Accent != Paint.DefaultAccent) sb.Append($" accent={Paint.Hex(p.Accent)}");
        if (p.Pattern != Pattern.None) sb.Append($" pattern={p.Pattern.ToString().ToLowerInvariant()}");
        if (p.Texture != FaceTexture.Panel) sb.Append($" texture={p.Texture.ToString().ToLowerInvariant()}");
        if (p.Stencil != 0) sb.Append($" stencil={(char)p.Stencil}");
        if (p.Grime != 0) sb.Append($" grime={p.Grime}");
        if (p.IsLamp) sb.Append($" lamp={p.Shape.ToString().ToLowerInvariant()} light={p.Lamp.Value.ToText()}");
        return sb.ToString();
    }

    /// <summary>Everything on a block line after its coordinates.</summary>
    static string BlockTail(Structure s, Block b, Int3 pos)
    {
        BlockType t = s.TypeOf(b);
        var sb = new StringBuilder(t.Id);
        if (t.IsArmor && b.Shape != 0) sb.Append(' ').Append(s.ShapeOf(b).Id);
        if (b.Rot != Orientations.Identity) sb.Append(" rot=").Append(Orientations.Name(b.Rot));
        if (b.Paint.Uniform)
        {
            if (b.Paint.PX != 0) sb.Append(" paint=").Append(s.Palette.NameOf(b.Paint.PX));
        }
        else sb.Append(" paint=").Append(string.Join(",", Dirs.All.Select(d => s.Palette.NameOf(b.Paint[d]))));
        if (b.Hp < s.StatsOf(b).MaxHp - 0.05f) sb.Append(" hp=").Append(Num(Math.Round(b.Hp, 1)));
        if (b.Open) sb.Append(" open");
        if (b.Off) sb.Append(" off");
        if (s.LightStyles.TryGetValue(pos, out LightSettings light)) sb.Append(" light=").Append(light.ToText());
        return sb.ToString();
    }

    // ---- Text helpers -----------------------------------------------------------------------

    static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static bool IsName(string s) => s.Length > 0 && s != "-" && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    static bool TryRange(string s, out int a, out int b)
    {
        int dots = s.IndexOf("..", StringComparison.Ordinal);
        if (dots < 0)
        {
            bool ok = int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out a);
            b = a;
            return ok;
        }
        b = 0;
        return int.TryParse(s[..dots], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out a)
            && int.TryParse(s[(dots + 2)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out b)
            && a <= b;
    }

    /// <summary>Splits a line into tokens; "quoted strings" may hold spaces. '#' outside quotes ends the line.</summary>
    static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false, inToken = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '\\' && i + 1 < line.Length) sb.Append(line[++i]);
                else if (c == '"') quoted = false;
                else sb.Append(c);
                continue;
            }
            if (c == '#') break;
            if (c == '"') { quoted = true; inToken = true; continue; }
            if (char.IsWhiteSpace(c))
            {
                if (inToken) tokens.Add(sb.ToString());
                sb.Clear();
                inToken = false;
                continue;
            }
            sb.Append(c);
            inToken = true;
        }
        if (quoted) throw new FormatException("unclosed quote");
        if (inToken) tokens.Add(sb.ToString());
        return tokens;
    }
}
