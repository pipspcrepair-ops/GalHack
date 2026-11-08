using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using Slice.Game;
using Structure = GalHack.Structures.Structure;

namespace Slice.Construction;

/// <summary>Where a blueprint in the library comes from: the game's starter set, or one the player saved.</summary>
public enum BlueprintSource { Starter, Yours }

/// <summary>
/// One blueprint in the library: its plan (never built on: placing copies it) and the numbers that
/// never change with the game's state (size, mass, the four numbers, the blocks it takes).
/// </summary>
public sealed class LibraryEntry
{
    public string File;
    public BlueprintSource Source;
    public Structure Plan;
    public string Name => Plan.Name;
    public bool Large => Plan.Grid.Size == GridSize.Large;

    /// <summary>Its bounding box, metres (x across, y up, z long).</summary>
    public Vector3 Size;
    public double MassT;
    public int Blocks;

    /// <summary>The four numbers in its own mode on the zone's gravity; null in Base Mode, which never moves.</summary>
    public HandlingStats Handling;

    /// <summary>
    /// The mode its file asks for. The same as the plan's, except a Large blueprint in Hover Mode:
    /// Large Hover isn't in the game yet, so such a plan loads in Flight Mode.
    /// </summary>
    public CoreMode Declared;

    /// <summary>Every block family it uses, by block id, and how many.</summary>
    public IReadOnlyList<(string BlockId, int Count)> Families;
}

/// <summary>
/// The BLUEPRINTS tab's library: the starter set the game ships (data/blueprints, listed in
/// slice.json's build.library) under Large and Small, and the blueprints the player saved
/// (build.saved, user://) under Yours. Any structure the player owns can be saved: as a design, at
/// full hit points, under a name of their own, in a new file (an old one is never overwritten).
/// </summary>
public sealed class BlueprintLibrary
{
    readonly GameData _data;
    readonly float _gravity;
    readonly List<LibraryEntry> _starters = new();
    readonly List<LibraryEntry> _yours = new();

    /// <summary>The folder saved blueprints live in (a user:// path); tests point it elsewhere.</summary>
    public string Folder { get; private set; }

    /// <summary>Files under <see cref="Folder"/> that didn't load, and why (shown nowhere: tests and the log).</summary>
    public readonly List<string> Rejected = new();

    public const int MaxName = 28;

    public BlueprintLibrary(GameData data, float gravity, string folder = null)
    {
        _data = data;
        _gravity = gravity;
        foreach (string file in data.Rules.Build.Library)
            _starters.Add(Entry(data.Blueprint(file), file, BlueprintSource.Starter, System.IO.File.ReadAllText(GameData.Own("data/blueprints/" + file))));
        UseFolder(folder ?? data.Rules.Build.Saved);
    }

    public IReadOnlyList<LibraryEntry> Starters => _starters;
    public IReadOnlyList<LibraryEntry> Yours => _yours;
    public IEnumerable<LibraryEntry> All => _starters.Concat(_yours);

    /// <summary>Changes are counted, so the tab redraws when the library does.</summary>
    public int Version { get; private set; }

    /// <summary>Reads saved blueprints from another folder (tests use their own).</summary>
    public void UseFolder(string folder)
    {
        Folder = folder;
        Refresh();
    }

    /// <summary>Reads the saved blueprints again, oldest name first.</summary>
    public void Refresh()
    {
        _yours.Clear();
        Rejected.Clear();
        string dir = ProjectSettings.GlobalizePath(Folder);
        if (Directory.Exists(dir))
            foreach (string path in Directory.GetFiles(dir, "*.gbp").OrderBy(p => p, StringComparer.Ordinal))
            {
                string text = System.IO.File.ReadAllText(path);
                Structure s = Blueprint.Load(text, _data.Catalogue, out List<string> errors);
                if (s == null) { Rejected.Add($"{Path.GetFileName(path)}: {string.Join("; ", errors.Take(3))}"); continue; }
                s.DirtyChunks.Clear();
                _yours.Add(Entry(s, path, BlueprintSource.Yours, text));
            }
        Version++;
    }

    LibraryEntry Entry(Structure plan, string file, BlueprintSource source, string text)
    {
        bool largeHover = plan.Grid.Size == GridSize.Large && Blueprint.Parse(text).Mode == CoreMode.Hover;
        var (min, max) = plan.Bounds();
        float cell = plan.Grid.CellSize;
        StructureStats st = StructureStats.Compute(plan);
        HandlingStats h = plan.Mode == CoreMode.Base ? null
            : HandlingModel.Compute(new StructureVessel(plan, _data.Handling), new FlightConditions(_gravity, true), _data.Handling);
        return new LibraryEntry
        {
            File = file, Source = source, Plan = plan, Declared = largeHover ? CoreMode.Hover : plan.Mode,
            Size = new Vector3(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1) * cell,
            MassT = st.Mass, Blocks = plan.Count, Handling = h,
            Families = plan.Blocks().Where(b => !plan.TypeOf(b.block).IsPieceTop).GroupBy(b => plan.TypeOf(b.block).Id).Select(g => (g.Key, g.Count()))
                .OrderByDescending(f => f.Item2).ThenBy(f => f.Key, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>Why a name can't be saved under, or null.</summary>
    public static string NameProblem(string name)
    {
        string n = (name ?? "").Trim();
        if (n.Length == 0) return "NAME IT";
        if (n.Length > MaxName) return $"{MaxName} LETTERS AT MOST";
        if (n.Any(c => char.IsControl(c))) return "LETTERS AND DIGITS";
        return null;
    }

    /// <summary>
    /// Saves a structure as a new blueprint under a name: a design, so every block at full hit
    /// points, in its mode, with its paint and lights. Null (and why) if the name won't do.
    /// </summary>
    public LibraryEntry Save(Structure s, string name, out string why)
    {
        why = NameProblem(name);
        if (why != null) return null;
        if (s.Core == null) { why = "NO CORE"; return null; }
        string dir = ProjectSettings.GlobalizePath(Folder);
        Directory.CreateDirectory(dir);
        string slug = Slug(name.Trim()), path = Path.Combine(dir, slug + ".gbp");
        for (int i = 2; System.IO.File.Exists(path); i++) path = Path.Combine(dir, $"{slug}-{i}.gbp");
        System.IO.File.WriteAllText(path, Text(s, name.Trim()), new UTF8Encoding(false));
        Refresh();
        return _yours.FirstOrDefault(e => e.File == path);
    }

    /// <summary>A structure as blueprint text: a copy at full hit points, named, so the live structure is untouched.</summary>
    public static string Text(Structure s, string name) => Blueprint.Save(Design(s, name));

    /// <summary>A copy of a structure as a design: its blocks, shapes, turns, paint, doors and lights, every block whole.</summary>
    public static Structure Design(Structure s, string name)
    {
        var copy = new Structure(s.Catalogue, s.Grid) { Name = name, Mode = s.Mode, GravityOn = s.GravityOn, Owner = Building.DefaultOwner };
        for (int i = 1; i < s.Palette.Count; i++) copy.Palette.Intern(s.Palette[(ushort)i], s.Palette.NameOf((ushort)i));
        foreach (var (p, b) in s.Blocks())
        {
            Block whole = b;
            whole.Hp = s.StatsOf(b).MaxHp;
            copy.Set(p, whole);
        }
        foreach (var (p, light) in s.LightStyles) copy.SetLight(p, light);
        copy.DirtyChunks.Clear();
        return copy;
    }

    static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        string slug = sb.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Length == 0 ? "blueprint" : slug;
    }

    // ---- What it takes ---------------------------------------------------------------------------

    /// <summary>The block items a blueprint uses, one per block (what the projector draws from your bag).</summary>
    public static Bill Cost(ItemCatalogue items, Structure plan)
    {
        var bill = new Bill();
        bool small = BuildingBridge.IsSmall(plan);
        foreach (var (_, b) in plan.Blocks())
            if (items.BlockItem(plan.TypeOf(b).Id, small) is ItemDef item) bill.Add(item.Id, 1);
        return bill;
    }
}
