using Encounters.Director;
using Godot;

namespace Encounters;

/// <summary>
/// Reads the director's data files through Godot, so res:// paths work in the editor and in an
/// export alike. The director itself only ever sees the text.
/// </summary>
public static class GameData
{
    public const string ConfigPath = "res://data/director.json";
    public const string ZonesPath = "res://data/zones.json";
    public const string DeckPath = "res://data/deck.json";

    public static string Read(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
            throw new System.IO.FileNotFoundException($"could not read {path}: {FileAccess.GetOpenError()}");
        return text;
    }

    public static DirectorConfig Config() => DirectorConfig.Parse(Read(ConfigPath));
    public static ZoneCatalog Zones() => ZoneCatalog.Parse(Read(ZonesPath));
    public static EventDeck Deck() => EventDeck.Parse(Read(DeckPath));
}
