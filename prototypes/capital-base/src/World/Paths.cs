using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Godot;

namespace CapitalBase.World;

/// <summary>Where things live: this prototype's own files, and the other prototypes' files read in place.</summary>
public static class Paths
{
    public static string Own(params string[] parts) => Path.Combine(ProjectSettings.GlobalizePath("res://"), Path.Combine(parts));

    public static string RepoRoot => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".."));

    public static string Repo(params string[] parts) => Path.Combine(RepoRoot, Path.Combine(parts));
}

/// <summary>
/// Another prototype's shader, read from where it lives (never copied): its own
/// <c>#include "res://..."</c> lines are resolved against that prototype's folder, so planet-surface's
/// terrain shader draws this prototype's ground exactly as it draws its own.
/// </summary>
public static class ForeignShader
{
    static readonly Dictionary<string, Shader> Cache = new();
    static readonly Regex Include = new("#include \"res://([^\"]+)\"");

    public static Shader Load(string prototype, string file)
    {
        string key = prototype + "/" + file;
        if (Cache.TryGetValue(key, out Shader cached)) return cached;
        string root = Paths.Repo("prototypes", prototype);
        var shader = new Shader { Code = Resolve(root, File.ReadAllText(Path.Combine(root, "shaders", file)), 0) };
        Cache[key] = shader;
        return shader;
    }

    static string Resolve(string root, string code, int depth) =>
        depth > 4 ? code : Include.Replace(code, m => Resolve(root, File.ReadAllText(Path.Combine(root, m.Groups[1].Value)), depth + 1));
}
