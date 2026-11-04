using System.Collections.Generic;
using System.Text;

namespace Wildlife;

/// <summary>
/// Makes names that sound like they belong to one planet. Each planet keeps only a handful of
/// the onsets, vowels and codas in the tables, so one world's creatures come out all hard
/// clicks ("Krakxek", "Tzakrok") and another's all soft vowels ("Leiolou", "Mailua").
/// </summary>
public sealed class NameGen
{
    readonly List<string> _onsets, _vowels, _codas;
    readonly SyllableTables _t;

    public NameGen(ulong planetSeed, SyllableTables tables)
    {
        _t = tables;
        var rng = Rng.For(planetSeed, "phonology");
        _onsets = Subset(rng, tables.Onsets, tables.OnsetsPerPlanet);
        _vowels = Subset(rng, tables.Vowels, tables.VowelsPerPlanet);
        _codas = Subset(rng, tables.Codas, tables.CodasPerPlanet);
    }

    public string Make(Rng rng)
    {
        int syllables = rng.Int(_t.Count);
        var sb = new StringBuilder();
        for (int i = 0; i < syllables; i++)
        {
            // An occasional vowel-led syllable inside the word keeps names from marching in lockstep.
            if (i == 0 || !rng.Chance(0.2f)) sb.Append(rng.Pick(_onsets));
            sb.Append(rng.Pick(_vowels));
            bool last = i == syllables - 1;
            if (rng.Chance(last ? _t.CodaChance + 0.2f : _t.CodaChance * 0.5f)) sb.Append(rng.Pick(_codas));
        }
        string name = Tidy(sb.ToString());
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>A unique name among those already taken.</summary>
    public string MakeUnique(Rng rng, HashSet<string> taken)
    {
        string name = Make(rng);
        for (int attempt = 0; attempt < 30 && taken.Contains(name); attempt++) name = Make(rng);
        if (taken.Contains(name)) name += "'" + (taken.Count + 1);
        taken.Add(name);
        return name;
    }

    static List<string> Subset(Rng rng, string[] all, int count)
    {
        var copy = new List<string>(all);
        rng.Shuffle(copy);
        return copy.GetRange(0, System.Math.Clamp(count, 1, copy.Count));
    }

    /// <summary>No triple letters: "Shaaa" becomes "Shaa".</summary>
    static string Tidy(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            int n = sb.Length;
            if (n >= 2 && sb[n - 1] == c && sb[n - 2] == c) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
