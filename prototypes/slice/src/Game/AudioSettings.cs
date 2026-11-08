using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Slice.Game;

/// <summary>
/// The four volumes, each 0 to 1, remembered between runs in a small file of their own. A missing
/// or broken file just means the defaults: the master starts at 50%, the others under it at full.
/// </summary>
public sealed class AudioSettings
{
    public const string Master = "master", Music = "music", Effects = "effects", Ambience = "ambience";
    /// <summary>Not a volume: the option to mark at the screen edge where a significant sound came from (off by default).</summary>
    public const string Direction = "direction";
    public static readonly string[] Volumes = { Master, Music, Effects, Ambience };
    const float DefaultMaster = 0.5f;

    readonly Dictionary<string, float> _values = new();

    /// <summary>Goes up with every change, so the menu knows to redraw.</summary>
    public int Version { get; private set; }

    public AudioSettings()
    {
        foreach (string key in Volumes) _values[key] = key == Master ? DefaultMaster : 1f;
        _values[Direction] = 0f;
    }

    /// <summary>The audio bus a volume sets (the master is the engine's own).</summary>
    public static string BusName(string key) => key switch { Music => "Music", Effects => "Effects", Ambience => "Ambience", _ => "Master" };

    public float Get(string key) => _values[key];

    /// <summary>Where a significant sound came from shows as a marker at the screen edge.</summary>
    public bool SoundDirection
    {
        get => _values[Direction] > 0.5f;
        set { _values[Direction] = value ? 1f : 0f; Version++; }
    }

    public void Set(string key, float value)
    {
        _values[key] = Math.Clamp(value, 0f, 1f);
        Version++;
    }

    public static AudioSettings Load(string path)
    {
        var settings = new AudioSettings();
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, float>>(File.ReadAllText(path));
            foreach (string key in Volumes)
                if (saved != null && saved.TryGetValue(key, out float value)) settings._values[key] = Math.Clamp(value, 0f, 1f);
            if (saved != null && saved.TryGetValue(Direction, out float shown)) settings._values[Direction] = shown > 0.5f ? 1f : 0f;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return settings;
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(_values));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Godot.GD.PushWarning($"Volumes not saved: {e.Message}"); }
    }
}
