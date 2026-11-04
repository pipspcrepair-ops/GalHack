using GalHack.Structures;
using Godot;

namespace GalHack.Structures.Presentation;

/// <summary>
/// Reads the structure data through Godot's file system, so res:// works in the editor and in
/// exported builds alike. The core itself only ever sees text.
/// </summary>
public static class GameData
{
    public const string DataDir = "res://data";
    public const string ExampleBlueprint = "res://data/blueprints/foss-lancer.gbp";

    static Catalogue _catalogue;

    public static Catalogue Catalogue => _catalogue ??= Catalogue.Load(name => ReadText($"{DataDir}/{name}"));

    public static string ReadText(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) throw new System.IO.FileNotFoundException($"cannot read {path}: {FileAccess.GetOpenError()}");
        return file.GetAsText();
    }
}
