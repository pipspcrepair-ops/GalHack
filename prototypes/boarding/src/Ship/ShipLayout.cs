using System;
using System.Collections.Generic;
using Godot;

namespace Boarding;

/// <summary>What one grid cell of a ship deck holds, as written in the layout file.</summary>
public enum CellKind
{
    Outside, Floor, Wall, Hull, WeakHull, Armour,
    Door, SecurityDoor, Bulkhead, Airlock,
    Core, GunnerStation, TurretMount, Generator, VaultLoot, Loot, Cover, Duct,
}

/// <summary>
/// A ship deck parsed from an ASCII grid (see data/vharn_picket.txt for the legend).
/// Pure data: no nodes, no physics. The <see cref="Ship"/> builds blocks from it, and the
/// pathfinder walks it.
///
/// Ship-local space: the grid is centred on the origin, the deck floor is at y = 0, +X is
/// starboard and +Z is aft. Everything aboard works in this space, so the ship can later
/// move or fly without anything here changing.
/// </summary>
public sealed class ShipLayout
{
    public const string DefaultPath = "res://data/vharn_picket.txt";

    public string Name = "ship";
    public float Cell = 2f;
    public float DeckHeight = 3.4f;
    public float DuctHeight = 1.25f;
    public int Width, Height;
    public CellKind[,] Cells;

    public Vector2 PlayerSpawn;   // in cells; may be outside the grid
    public Vector2 PlayerFace;
    public readonly List<Vector2I> PatrolLoop = new();
    public readonly List<Vector2I> GuardSpawns = new();
    public readonly List<Vector2I> OfficerSpawns = new();
    public readonly List<Vector2I> ReinforcementSpawns = new();
    public readonly Dictionary<string, int> LootVault = new();
    public readonly Dictionary<string, int> LootCache = new();
    public Vector2I Core = new(-1, -1);
    public Vector2I GunnerStation = new(-1, -1);
    public Vector2I TurretMount = new(-1, -1);

    public static ShipLayout Load(string path = DefaultPath) => Parse(DataFile.Load(path));

    public static ShipLayout Parse(DataFile data)
    {
        var layout = new ShipLayout();
        var top = data.Top;
        if (top.TryGetValue("name", out var name)) layout.Name = name;
        if (top.TryGetValue("cell", out var cell)) layout.Cell = DataFile.Float(cell);
        if (top.TryGetValue("deck_height", out var deck)) layout.DeckHeight = DataFile.Float(deck);
        if (top.TryGetValue("duct_height", out var duct)) layout.DuctHeight = DataFile.Float(duct);
        layout.PlayerSpawn = Pair(top, "player_spawn");
        layout.PlayerFace = Pair(top, "player_face");
        ReadLoot(top, "loot_vault", layout.LootVault);
        ReadLoot(top, "loot_cache", layout.LootCache);

        if (!data.Grids.TryGetValue("deck 0", out var rows) || rows.Count == 0)
            throw new FormatException("Layout has no [deck 0] grid");

        layout.Height = rows.Count;
        layout.Width = rows[0].Length;
        layout.Cells = new CellKind[layout.Width, layout.Height];
        var patrol = new SortedDictionary<int, Vector2I>();

        for (int r = 0; r < layout.Height; r++)
        {
            if (rows[r].Length != layout.Width)
                throw new FormatException($"Layout row {r} is {rows[r].Length} wide, expected {layout.Width}");
            for (int c = 0; c < layout.Width; c++)
            {
                var at = new Vector2I(c, r);
                char ch = rows[r][c];
                layout.Cells[c, r] = ch switch
                {
                    '.' => CellKind.Outside,
                    '_' or 'g' or 'O' or 'R' or (>= '1' and <= '9') => CellKind.Floor,
                    '#' => CellKind.Wall,
                    'H' => CellKind.Hull,
                    'W' => CellKind.WeakHull,
                    'X' => CellKind.Armour,
                    'd' => CellKind.Door,
                    'S' => CellKind.SecurityDoor,
                    'B' => CellKind.Bulkhead,
                    'A' => CellKind.Airlock,
                    'C' => CellKind.Core,
                    'G' => CellKind.GunnerStation,
                    'T' => CellKind.TurretMount,
                    'E' => CellKind.Generator,
                    'L' => CellKind.VaultLoot,
                    'l' => CellKind.Loot,
                    'x' => CellKind.Cover,
                    'v' => CellKind.Duct,
                    _ => throw new FormatException($"Unknown layout character '{ch}' at column {c}, row {r}"),
                };
                switch (ch)
                {
                    case 'g': layout.GuardSpawns.Add(at); break;
                    case 'O': layout.OfficerSpawns.Add(at); break;
                    case 'R': layout.ReinforcementSpawns.Add(at); break;
                    case 'C': layout.Core = at; break;
                    case 'G': layout.GunnerStation = at; break;
                    case 'T': layout.TurretMount = at; break;
                    case >= '1' and <= '9': patrol[ch - '0'] = at; break;
                }
            }
        }
        layout.PatrolLoop.AddRange(patrol.Values);
        return layout;
    }

    static Vector2 Pair(Dictionary<string, string> top, string key)
    {
        if (!top.TryGetValue(key, out var value)) return Vector2.Zero;
        float[] v = DataFile.Floats(value);
        return new Vector2(v[0], v[1]);
    }

    static void ReadLoot(Dictionary<string, string> top, string key, Dictionary<string, int> into)
    {
        if (!top.TryGetValue(key, out var value)) return;
        foreach (string entry in DataFile.List(value))
        {
            string[] parts = entry.Split(':');
            into[parts[0].Trim()] = parts.Length > 1 ? int.Parse(parts[1].Trim()) : 1;
        }
    }

    // ---- Queries -------------------------------------------------------------------------

    public bool InBounds(Vector2I c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    public CellKind this[Vector2I c] => InBounds(c) ? Cells[c.X, c.Y] : CellKind.Outside;

    /// <summary>The middle of a cell on the deck floor, in ship-local metres.</summary>
    public Vector3 CellCenter(Vector2I c) => CellCenter(new Vector2(c.X, c.Y));

    public Vector3 CellCenter(Vector2 c) =>
        new((c.X + 0.5f) * Cell - Width * Cell * 0.5f, 0f, (c.Y + 0.5f) * Cell - Height * Cell * 0.5f);

    public Vector2I CellAt(Vector3 local) => new(
        Mathf.FloorToInt((local.X + Width * Cell * 0.5f) / Cell),
        Mathf.FloorToInt((local.Z + Height * Cell * 0.5f) / Cell));

    /// <summary>The ship's footprint in ship-local metres (deck to roof).</summary>
    public Aabb Bounds => new(new Vector3(-Width * Cell * 0.5f, 0f, -Height * Cell * 0.5f),
        new Vector3(Width * Cell, DeckHeight, Height * Cell));

    public static bool IsDoor(CellKind k) =>
        k is CellKind.Door or CellKind.SecurityDoor or CellKind.Bulkhead or CellKind.Airlock;

    /// <summary>Cells you stand in when nothing blocks them: floor, the gunner's seat, ducts.</summary>
    public static bool IsWalkable(CellKind k) => k is CellKind.Floor or CellKind.GunnerStation or CellKind.Duct;

    /// <summary>Part of the ship (gets a deck and a roof).</summary>
    public bool IsShip(Vector2I c) => this[c] != CellKind.Outside;

    /// <summary>True if this cell is a walkable interior space with the ship around it.</summary>
    public bool IsInterior(Vector2I c) => IsShip(c) && (IsWalkable(this[c]) || IsDoor(this[c]));

    public static readonly Vector2I[] Neighbours4 = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
}
