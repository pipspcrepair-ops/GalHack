using System.IO;
using GalHack.Deco;
using GalHack.Items;
using Godot;

namespace DecoShowroom;

/// <summary>Where the shared data lives, from this project's folder, and the loaded catalogues.</summary>
public static class DataPaths
{
    public static string Project => ProjectSettings.GlobalizePath("res://");
    public static string Repo => Path.GetFullPath(Path.Combine(Project, "..", ".."));
    public static string Items => Path.Combine(Repo, "shared", "Items", "data");

    static DecoCatalogue _deco;
    static ItemCatalogue _items;
    static DecoForge _forge;
    static DecoArrangements _arrangements;

    public static DecoCatalogue Deco => _deco ??= DecoCatalogue.LoadDirectory(Items);
    public static ItemCatalogue ItemCatalogue => _items ??= GalHack.Items.ItemCatalogue.LoadDirectory(Items);
    public static DecoForge Forge => _forge ??= new DecoForge(Deco);
    public static DecoArrangements Arrangements => _arrangements ??= DecoArrangements.LoadDirectory(Items, Deco);
}
