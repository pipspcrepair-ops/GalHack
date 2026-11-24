using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Slice.Ui;

/// <summary>
/// The owner's rule for every string on screen: labels, values and icons only, never a sentence. A line
/// reads as a sentence when it runs more than <see cref="MostLowerWords"/> lowercase words in a row, or
/// a full stop ends a phrase in it. Proper names that would trip it are on <see cref="Allowed"/> (tests).
/// </summary>
public static class UiText
{
    /// <summary>The most lowercase words a line may run in a row ("in your bag" is three).</summary>
    public const int MostLowerWords = 3;

    /// <summary>Proper names that may hold a run of lowercase words or a full stop: taken out of a line before it is judged.</summary>
    public static readonly string[] Allowed =
    {
        "Juan Linietsky, Ariel Manzur and the Godot contributors",
    };

    static readonly Regex Word = new(@"^[a-z][a-z'’\-]*[,;]?$", RegexOptions.Compiled);
    static readonly Regex Stop = new(@"[a-z]{2}\.(\s|$)", RegexOptions.Compiled);

    /// <summary>Why a line reads as a sentence ("5 lowercase words", "a full stop"), or null when it is a label or a value.</summary>
    public static string Problem(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (string name in Allowed) text = text.Replace(name, " | ", StringComparison.Ordinal);
        if (Stop.IsMatch(text)) return "a full stop";
        int run = 0, most = 0;
        foreach (string token in text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            run = Word.IsMatch(token) ? run + 1 : 0;
            most = Math.Max(most, run);
        }
        return most > MostLowerWords ? $"{most} lowercase words in a row" : null;
    }

    /// <summary>The lines among these that read as sentences, each with why, once each.</summary>
    public static List<string> Sentences(IEnumerable<string> lines)
    {
        var found = new List<string>();
        var seen = new HashSet<string>();
        foreach (string line in lines)
            if (line != null && seen.Add(line) && Problem(line) is string why) found.Add($"\"{line}\" ({why})");
        return found;
    }
}
