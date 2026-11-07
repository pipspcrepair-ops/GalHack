using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Slice.Game;

/// <summary>One line of the credits.</summary>
public sealed record CreditRow(string Section, string What, string Author, string Source, string Licence);

/// <summary>Everyone whose work is in the game, as the Stats tab's credits show it: the rows of data/credits.json.</summary>
public sealed class Credits
{
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

    public static Credits Shared => _shared ??= Load(GameData.Own("data/credits.json"));

    public IReadOnlyList<CreditRow> Rows { get; }

    Credits(IReadOnlyList<CreditRow> rows) => Rows = rows;

    public static Credits Load(string creditsJson)
    {
        var data = JsonSerializer.Deserialize<CreditsFile>(File.ReadAllText(creditsJson), Options);
        return new Credits(data.Sections.SelectMany(s => s.Rows.Select(r => new CreditRow(s.Name, r.What, r.Author, r.Source, r.Licence))).ToList());
    }
}
