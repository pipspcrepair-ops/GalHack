using Godot;

namespace Wildlife;

/// <summary>Reads the data files through Godot, so they also load from an exported .pck.</summary>
public static class DataFiles
{
    public const string WildlifePath = "res://data/wildlife.json";

    public static WildlifeData LoadWildlife()
    {
        string json = FileAccess.GetFileAsString(WildlifePath);
        if (string.IsNullOrEmpty(json)) throw new System.IO.FileNotFoundException($"could not read {WildlifePath}");
        return WildlifeData.FromJson(json);
    }
}
