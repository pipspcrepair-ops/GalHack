using Godot;

namespace Wildlife;

/// <summary>Reads the data files through Godot, so they also load from an exported .pck.</summary>
public static class DataFiles
{
    public const string WildlifePath = "res://data/wildlife.json";
    public const string SpacePath = "res://data/space_fauna.json";

    public static WildlifeData LoadWildlife() => WildlifeData.FromJson(Read(WildlifePath));

    public static SpaceFaunaData LoadSpace() => SpaceFaunaData.FromJson(Read(SpacePath));

    static string Read(string path)
    {
        string json = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(json)) throw new System.IO.FileNotFoundException($"could not read {path}");
        return json;
    }
}
