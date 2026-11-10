using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Godot;

namespace AssetViewer;

/// <summary>
/// The game code the viewer compiles in loads its shaders and fonts from its own prototype's res://
/// paths (res://shaders/..., res://fonts/...). This packs the folders the providers name, straight
/// from where they live, into a resource pack and mounts it at res://, exactly as the slice does.
/// Folders from different prototypes must not hold files of the same name; a repeat is skipped and logged.
/// </summary>
public static class ResourceMount
{
    static bool _mounted;

    public static string PrototypesRoot => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));

    public static void Mount(IEnumerable<(string Prototype, string Folder)> mounts)
    {
        if (_mounted) return;
        _mounted = true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var wanted = new List<(string Prototype, string Folder)>();
        foreach (var m in mounts) if (seen.Add(m.Prototype + "|" + m.Folder)) wanted.Add(m);
        if (wanted.Count == 0) return;

        string dir = ProjectSettings.GlobalizePath("user://");
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, "mounted." + System.Environment.ProcessId + ".tmp");
        var names = new HashSet<string>(StringComparer.Ordinal);
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var (prototype, folder) in wanted)
            {
                string protoDir = Path.GetFullPath(Path.Combine(PrototypesRoot, prototype));
                string root = Path.Combine(protoDir, folder);
                if (!Directory.Exists(root)) { GD.PrintErr($"cannot mount {root}"); continue; }
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(protoDir, file).Replace('\\', '/');
                    if (relative.EndsWith(".import", StringComparison.Ordinal) || relative.EndsWith(".uid", StringComparison.Ordinal)) continue;
                    if (!names.Add(relative)) { GD.Print($"mount: {relative} from {prototype} already mounted, skipped"); continue; }
                    zip.CreateEntryFromFile(file, relative, CompressionLevel.NoCompression);
                }
            }
        }
        foreach (string old in Directory.EnumerateFiles(dir, "mounted-*.zip"))
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(old) > TimeSpan.FromHours(1))
            {
                try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        string mine = Path.Combine(dir, $"mounted-{System.Environment.ProcessId}.zip");
        File.Move(temp, mine, overwrite: true);
        if (!ProjectSettings.LoadResourcePack(mine, replaceFiles: false))
            GD.PrintErr($"could not mount {mine}");
    }
}
