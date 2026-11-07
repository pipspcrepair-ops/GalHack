using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalHack.Music;

namespace Slice.Game;

/// <summary>One line of the credits.</summary>
public sealed record CreditRow(string Section, string What, string Author, string Source, string Licence);

/// <summary>
/// Everyone whose work is in the game, as the Stats tab's credits show it: the rows of
/// data/credits.json, then one per chosen track in data/music.json. A track is a file under
/// assets/music/&lt;mood&gt;/ and a row with its credit; a row whose file has gone is ignored, by the
/// player and by the credits alike, so removing a track is deleting its file and its row.
/// </summary>
public sealed class Credits
{
    /// <summary>The audio the engine can load at run time.</summary>
    public static readonly string[] Formats = { ".ogg", ".mp3", ".wav" };

    sealed class TrackRow
    {
        public string File { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Source { get; set; }
        public string Licence { get; set; }
        public string Attribution { get; set; }
    }

    sealed class MusicFile { public List<TrackRow> Tracks { get; set; } = new(); }
    sealed class CreditSection
    {
        public string Name { get; set; }
        public List<CreditLine> Rows { get; set; } = new();
    }

    sealed class CreditLine
    {
        public string What { get; set; }
        public string Author { get; set; }
        public string Source { get; set; }
        public string Licence { get; set; }
    }

    sealed class CreditsFile { public List<CreditSection> Sections { get; set; } = new(); }

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    static Credits _shared;

    public static Credits Shared => _shared ??= Load(GameData.Own("data/music.json"), GameData.Own("data/credits.json"), GameData.Own("assets/music"));

    /// <summary>The tracks that exist, with the folders that give them their moods.</summary>
    public IReadOnlyList<MusicTrack> Tracks { get; }

    public IReadOnlyList<CreditRow> Rows { get; }

    /// <summary>The folder the tracks' <see cref="MusicTrack.File"/> paths are under.</summary>
    public string MusicFolder { get; }

    Credits(string folder, IReadOnlyList<MusicTrack> tracks, IReadOnlyList<CreditRow> rows)
    {
        MusicFolder = folder;
        Tracks = tracks;
        Rows = rows;
    }

    public static Credits Load(string musicJson, string creditsJson, string folder)
    {
        var rows = new List<CreditRow>();
        var data = JsonSerializer.Deserialize<CreditsFile>(File.ReadAllText(creditsJson), Options);
        foreach (CreditSection section in data.Sections)
            rows.AddRange(section.Rows.Select(r => new CreditRow(section.Name, r.What, r.Author, r.Source, r.Licence)));

        var tracks = new List<MusicTrack>();
        var music = JsonSerializer.Deserialize<MusicFile>(File.ReadAllText(musicJson), Options);
        foreach (TrackRow t in music.Tracks)
        {
            string mood = t.File.Split('/')[0];
            if (!File.Exists(Path.Combine(folder, t.File)) || !Formats.Contains(Path.GetExtension(t.File).ToLowerInvariant())) continue;
            tracks.Add(new MusicTrack(mood, t.File, t.Title, t.Author, t.Source, t.Licence, t.Attribution));
            rows.Add(new CreditRow($"MUSIC ({mood})", string.IsNullOrEmpty(t.Attribution) ? t.Title : t.Attribution, t.Author, t.Source, t.Licence));
        }
        return new Credits(folder, tracks, rows);
    }
}
