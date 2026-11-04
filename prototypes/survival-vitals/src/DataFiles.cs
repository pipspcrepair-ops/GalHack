using Godot;
using SurvivalVitals.Model;

namespace SurvivalVitals;

/// <summary>
/// Reads the data folder through Godot's file system, so it works from the editor, from tests
/// and from an export alike. Every call returns fresh objects, so a test can tweak its copy.
/// </summary>
public static class DataFiles
{
    public static SurvivalData Load() =>
        SurvivalData.Parse(Read("vitals"), Read("armor"), Read("food"), Read("planet"));

    static string Read(string name)
    {
        string path = $"res://data/{name}.json";
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text)) throw new System.IO.FileNotFoundException($"Missing data file {path}");
        return text;
    }
}
