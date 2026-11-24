using System.Collections.Generic;
using System.IO;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Items.Deposits;
using GalHack.Structures;
using Godot;

namespace Manufacturing.Game;

/// <summary>
/// Where the shared data lives, seen from this prototype when it runs from source. Nothing is
/// copied: the item data is read from shared/Items/data, building's catalogue from
/// prototypes/building/data (with the production machines, guns and precursor armour appended),
/// the vessel parts from the physics sandbox, and the screen look from diegetic-ui.
/// </summary>
public static class DataPaths
{
    public static string Project => ProjectSettings.GlobalizePath("res://");
    public static string Repo => Path.GetFullPath(Path.Combine(Project, "..", ".."));
    public static string Items => Path.Combine(Repo, "shared", "Items", "data");
    public static string BuildingData => Path.Combine(Repo, "prototypes", "building", "data");
    public static string VesselParts => Path.Combine(Repo, "prototypes", "physics-sandbox", "data", "vessel-parts.json");
    public static string DiegeticUi => Path.Combine(Repo, "prototypes", "diegetic-ui");
    public static string Blueprints => Path.Combine(Project, "data", "blueprints");

    /// <summary>A blueprint of the slice's starter set (the starter vessel is starter.gbp), for <see cref="LoadBlueprint"/>.</summary>
    public static string Slice(string file) => Path.Combine(Repo, "prototypes", "slice", "data", "blueprints", file);

    static ItemCatalogue _items;
    static Catalogue _blocks;
    static DepositRules _deposits;

    public static ItemCatalogue ItemCatalogue => _items ??= GalHack.Items.ItemCatalogue.LoadDirectory(Items);
    public static DepositRules DepositRules => _deposits ??= GalHack.Items.Deposits.DepositRules.LoadDirectory(Items);

    /// <summary>Building's catalogue plus the vessel parts, production machines, guns and precursor armour.</summary>
    public static Catalogue BlockCatalogue => _blocks ??= BuildingBridge.LoadCatalogue(BuildingData,
        new List<string> { VesselParts, Path.Combine(Items, "building-parts.json") },
        new List<string> { Path.Combine(Items, "building-materials.json") });

    /// <summary>A blueprint by its file name here, or by a full path (<see cref="Slice"/>).</summary>
    public static Structure LoadBlueprint(string fileName)
    {
        string path = Path.Combine(Blueprints, fileName);
        Structure s = Blueprint.Load(File.ReadAllText(path), BlockCatalogue, out List<string> errors);
        if (s == null || errors.Count > 0) throw new InvalidDataException($"{fileName}: {string.Join("; ", errors)}");
        s.DirtyChunks.Clear();
        return s;
    }
}
