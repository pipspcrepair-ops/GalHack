using System.IO;
using Godot;

namespace Saves;

/// <summary>
/// Where the prototype and its tests keep saves: prototypes/saves/.saves, inside the project
/// (git-ignored, and hidden from Godot's importer by a .gdignore). Never user://, which would
/// land on the nearly-full C: drive.
/// </summary>
public static class DataPaths
{
    public static string Root
    {
        get
        {
            string root = Path.GetFullPath(ProjectSettings.GlobalizePath("res://.saves"));
            Directory.CreateDirectory(root);
            string ignore = Path.Combine(root, ".gdignore");
            if (!File.Exists(ignore)) File.WriteAllText(ignore, "");
            return root;
        }
    }

    /// <summary>The prototype's own world, unless --save-dir says otherwise.</summary>
    public static string World => Path.Combine(Root, "world");

    /// <summary>An empty folder under the data root. Anything already in it is deleted.</summary>
    public static string Fresh(string name)
    {
        string path = Path.Combine(Root, name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }
}
