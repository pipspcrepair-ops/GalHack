using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Godot;

namespace Boarding;

/// <summary>
/// The one tiny text format every data file here uses: `key = value` lines, `[section]`
/// headers, and `#` comments. Sections named `deck N` are ASCII grids and are kept raw,
/// because `#` is a wall there, not a comment.
/// </summary>
public sealed class DataFile
{
    /// <summary>Key/value pairs per section. Keys before any header live in section "".</summary>
    public readonly Dictionary<string, Dictionary<string, string>> Sections = new();

    /// <summary>Raw grid rows per `deck N` section, in file order.</summary>
    public readonly Dictionary<string, List<string>> Grids = new();

    /// <summary>Section names in file order (handy for weapons, which keep their order).</summary>
    public readonly List<string> SectionOrder = new();

    public Dictionary<string, string> Top => Section("");

    public Dictionary<string, string> Section(string name) =>
        Sections.TryGetValue(name, out var values) ? values : new Dictionary<string, string>();

    public static DataFile Load(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) throw new InvalidOperationException($"Cannot open data file {path}: {FileAccess.GetOpenError()}");
        return Parse(file.GetAsText());
    }

    public static DataFile Parse(string text)
    {
        var data = new DataFile();
        string section = "";
        data.Sections[section] = new Dictionary<string, string>();

        foreach (string rawLine in text.Replace("\r", "").Split('\n'))
        {
            string trimmed = rawLine.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1].Trim();
                data.SectionOrder.Add(section);
                if (IsGrid(section)) data.Grids[section] = new List<string>();
                else data.Sections[section] = new Dictionary<string, string>();
                continue;
            }

            if (IsGrid(section))
            {
                // Grid rows are taken verbatim; a blank line ends the grid.
                if (trimmed.Length > 0) data.Grids[section].Add(trimmed);
                continue;
            }

            string line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) throw new FormatException($"Expected `key = value`, got: {rawLine}");
            data.Sections[section][line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return data;
    }

    static bool IsGrid(string section) => section.StartsWith("deck", StringComparison.OrdinalIgnoreCase);

    /// <summary>`#` starts a comment at the beginning of a line or after whitespace.</summary>
    static string StripComment(string line)
    {
        if (line.TrimStart().StartsWith('#')) return "";
        int at = line.IndexOf(" #", StringComparison.Ordinal);
        return at >= 0 ? line[..at] : line;
    }

    // ---- Typed helpers -------------------------------------------------------------------

    public static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);

    public static float[] Floats(string value)
    {
        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) result[i] = Float(parts[i]);
        return result;
    }

    public static string[] List(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Copies matching keys onto an object's public fields: `snake_case` keys map to
    /// `PascalCase` fields. Unknown keys are an error, so a typo in a data file is loud.
    /// </summary>
    public static void ApplyTo(object target, Dictionary<string, string> values)
    {
        Type type = target.GetType();
        foreach (var (key, value) in values)
        {
            FieldInfo field = type.GetField(Pascal(key), BindingFlags.Public | BindingFlags.Instance)
                ?? throw new FormatException($"{type.Name} has no field for data key `{key}`");
            object parsed = field.FieldType switch
            {
                var t when t == typeof(float) => Float(value),
                var t when t == typeof(int) => int.Parse(value, CultureInfo.InvariantCulture),
                var t when t == typeof(bool) => bool.Parse(value),
                var t when t == typeof(string) => value,
                var t when t == typeof(string[]) => List(value),
                var t when t == typeof(float[]) => Floats(value),
                _ => throw new FormatException($"Unsupported field type {field.FieldType} for `{key}`"),
            };
            field.SetValue(target, parsed);
        }
    }

    static string Pascal(string snake)
    {
        var sb = new StringBuilder(snake.Length);
        bool upper = true;
        foreach (char c in snake)
        {
            if (c == '_') { upper = true; continue; }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        return sb.ToString();
    }
}
