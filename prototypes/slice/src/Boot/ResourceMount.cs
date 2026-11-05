using System;
using System.IO;
using System.IO.Compression;
using Godot;

namespace Slice.Boot;

/// <summary>
/// The engine pieces the slice compiles in from other prototypes load their shaders and fonts
/// from their own project's res:// paths (res://shaders/sky.gdshader, res://fonts/...). Rather
/// than copy those files, this packs them, straight from where they live, into a resource pack
/// and mounts it at res:// before anything loads. Their code then runs here unchanged.
///
/// The names never collide: each folder below holds different files.
/// </summary>
public static class ResourceMount
{
    /// <summary>(prototype folder, folder inside it): mounted at res://(folder inside it).</summary>
    static readonly (string Prototype, string Folder)[] Mounts =
    {
        ("planet-surface", "shaders"),
        ("diegetic-ui", "shaders"),
        ("diegetic-ui", "fonts"),
        ("wildlife", "shaders"),
    };

    static bool _mounted;

    public static string PrototypesRoot => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));

    /// <summary>Packs and mounts everything, once per run. Call before anything loads a shader.</summary>
    public static void Mount()
    {
        if (_mounted) return;
        _mounted = true;
        string pack = Path.Combine(ProjectSettings.GlobalizePath("user://"), "mounted.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(pack));
        // A fresh pack each run, so an edited shader in another prototype shows up here at once.
        string temp = pack + "." + System.Environment.ProcessId + ".tmp";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var (prototype, folder) in Mounts)
            {
                string root = Path.Combine(PrototypesRoot, prototype, folder);
                if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"cannot mount {root}");
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(Path.Combine(PrototypesRoot, prototype), file).Replace('\\', '/');
                    if (relative.EndsWith(".import", StringComparison.Ordinal) || relative.EndsWith(".uid", StringComparison.Ordinal)) continue;
                    zip.CreateEntryFromFile(file, relative, CompressionLevel.NoCompression);
                }
            }
        }
        // Several test runs may start at once: each mounts its own copy. Copies left by earlier
        // runs are cleared (one still open by a running game refuses, and stays).
        foreach (string old in Directory.EnumerateFiles(Path.GetDirectoryName(pack), "mounted-*.zip")) TryDelete(old);
        string mine = Path.Combine(Path.GetDirectoryName(pack), $"mounted-{System.Environment.ProcessId}.zip");
        File.Move(temp, mine, overwrite: true);
        if (!ProjectSettings.LoadResourcePack(mine, replaceFiles: false))
            throw new InvalidOperationException($"could not mount {mine}");
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
