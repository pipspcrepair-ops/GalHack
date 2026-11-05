using Godot;
using Invasions.Director;
using Invasions.Model;

namespace Invasions.World;

/// <summary>Reads the JSON data files from res://data. The pure C# side only ever sees the parsed objects.</summary>
public static class GameData
{
    /// <summary>invasions.json with factions.json attached: who invades, and how.</summary>
    public static InvasionData Invasions() => InvasionData.Load(Read("res://data/invasions.json"), Read("res://data/factions.json"));
    public static ServerSettings Server() => ServerSettings.FromJson(Read("res://data/server.json"));
    public static BaseData Base() => BaseData.FromJson(Read("res://data/base.json"));
    public static FireControlData FireControl() => FireControlData.FromJson(Read("res://data/fire_control.json"));

    static string Read(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text)) throw new System.IO.FileNotFoundException($"could not read {path}: {FileAccess.GetOpenError()}");
        return text;
    }
}
