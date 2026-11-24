using System.IO;
using Godot;

namespace Wildlife;

/// <summary>
/// Finds the bestiary's data and shaders: mounted in the project (res://data, res://shaders) where
/// a game has packed them, else read straight from shared/Bestiary beside the prototypes.
/// </summary>
public static class DataFiles
{
    public static string Root => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "shared", "Bestiary"));

    public static WildlifeData LoadWildlife() => WildlifeData.FromJson(Text("data", "wildlife.json"));

    public static SpaceFaunaData LoadSpace() => SpaceFaunaData.FromJson(Text("data", "space_fauna.json"));

    /// <summary>One of the module's text files, wherever it is.</summary>
    public static string Text(string folder, string file)
    {
        string res = $"res://{folder}/{file}";
        if (Godot.FileAccess.FileExists(res)) return Godot.FileAccess.GetFileAsString(res);
        return File.ReadAllText(Path.Combine(Root, folder, file));
    }

    /// <summary>One of the module's shaders: the mounted resource if there is one, else compiled from its source.</summary>
    public static Shader Shader(string file)
    {
        string res = $"res://shaders/{file}";
        return ResourceLoader.Exists(res) ? GD.Load<Shader>(res) : new Shader { Code = File.ReadAllText(Path.Combine(Root, "shaders", file)) };
    }
}
