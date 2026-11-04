using System.Collections.Generic;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Reads every data file under res://data and hands the text to the pure generator. Godot's
/// FileAccess works in the editor and in exports alike (exports need *.kit and *.txt in the
/// export filter).
/// </summary>
public static class GameData
{
    static PoiLibrary _library;

    public static PoiLibrary Library => _library ??= Load();

    public static PoiLibrary Load()
    {
        var files = new List<(string, string)>();
        Collect("res://data", files);
        return PoiLibrary.Load(files);
    }

    static void Collect(string dir, List<(string, string)> files)
    {
        using var d = DirAccess.Open(dir);
        if (d == null) throw new System.IO.DirectoryNotFoundException(dir);
        foreach (string f in d.GetFiles())
            if (f.EndsWith(".kit") || f.EndsWith(".txt"))
                files.Add((f, FileAccess.GetFileAsString($"{dir}/{f}")));
        foreach (string sub in d.GetDirectories()) Collect($"{dir}/{sub}", files);
    }
}
