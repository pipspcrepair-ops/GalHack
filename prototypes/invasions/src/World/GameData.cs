using Godot;
using Invasions.Director;
using Invasions.Model;

namespace Invasions.World;

/// <summary>
/// Reads the JSON data files from res://data, and reads and writes the world's own settings. The pure
/// C# side only ever sees the parsed objects.
/// </summary>
public static class GameData
{
    /// <summary>invasions.json with factions.json attached: who invades, and how.</summary>
    public static InvasionData Invasions() => InvasionData.Load(Read("res://data/invasions.json"), Read("res://data/factions.json"));
    public static ServerSettings Server() => ServerSettings.FromJson(Read("res://data/server.json"));
    public static BaseData Base() => BaseData.FromJson(Read("res://data/base.json"));
    public static FireControlData FireControl() => FireControlData.FromJson(Read("res://data/fire_control.json"));

    /// <summary>
    /// The world's own settings (the in-game options), saved with the world. This prototype has one
    /// world, so it is one small file in user data; nothing set yet reads as empty.
    /// </summary>
    public static WorldSettings LoadWorld(string path) =>
        path != null && FileAccess.FileExists(path) ? WorldSettings.FromJson(FileAccess.GetFileAsString(path)) : new WorldSettings();

    public static void SaveWorld(string path, WorldSettings settings)
    {
        if (path == null) return;
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushError($"could not save {path}: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(settings.ToJson());
    }

    static string Read(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text)) throw new System.IO.FileNotFoundException($"could not read {path}: {FileAccess.GetOpenError()}");
        return text;
    }
}
