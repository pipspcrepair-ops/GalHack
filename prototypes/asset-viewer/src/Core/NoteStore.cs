using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace AssetViewer;

/// <summary>The comment and two ticks for one thing: an asset, or one animation of an asset.</summary>
public sealed class Note
{
    [JsonPropertyName("comment")] public string Comment { get; set; } = "";
    [JsonPropertyName("really_like")] public bool Like { get; set; }
    [JsonPropertyName("problem")] public bool Problem { get; set; }

    [JsonIgnore] public bool Empty => string.IsNullOrWhiteSpace(Comment) && !Like && !Problem;
}

/// <summary>Everything written about one asset, plus how to get back to what was being looked at.</summary>
public sealed class AssetNote
{
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("comment")] public string Comment { get; set; } = "";
    [JsonPropertyName("really_like")] public bool Like { get; set; }
    [JsonPropertyName("problem")] public bool Problem { get; set; }
    /// <summary>The seed and options on screen when this was last edited, so the asset can be rebuilt exactly as seen.</summary>
    [JsonPropertyName("seed")] public ulong? Seed { get; set; }
    [JsonPropertyName("options")] public Dictionary<string, string> Options { get; set; } = new();
    [JsonPropertyName("animations")] public Dictionary<string, Note> Animations { get; set; } = new();
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";

    [JsonIgnore]
    public bool Empty => string.IsNullOrWhiteSpace(Comment) && !Like && !Problem && Animations.Values.All(a => a.Empty);
}

/// <summary>A saved camera and asset state.</summary>
public sealed class Position
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("saved")] public string Saved { get; set; } = "";
    [JsonPropertyName("asset")] public string Asset { get; set; } = "";
    [JsonPropertyName("seed")] public ulong Seed { get; set; }
    [JsonPropertyName("options")] public Dictionary<string, string> Options { get; set; } = new();
    [JsonPropertyName("animation")] public string Animation { get; set; } = "";
    [JsonPropertyName("free_camera")] public bool FreeCamera { get; set; }
    [JsonPropertyName("target")] public float[] Target { get; set; } = new float[3];
    [JsonPropertyName("yaw")] public float Yaw { get; set; }
    [JsonPropertyName("pitch")] public float Pitch { get; set; }
    [JsonPropertyName("distance")] public float Distance { get; set; }
    [JsonPropertyName("position")] public float[] CameraPosition { get; set; } = new float[3];
    [JsonPropertyName("fov")] public float Fov { get; set; } = 70f;
    [JsonPropertyName("spin")] public bool Spin { get; set; } = true;
    [JsonPropertyName("turntable")] public float Turntable { get; set; }
    [JsonPropertyName("light")] public float Light { get; set; } = 0.5f;
    [JsonPropertyName("ground")] public bool Ground { get; set; } = true;
}

/// <summary>
/// The notes file (what the user wrote about each asset, the file to hand to an agent) and the
/// positions file (bookmarks and where the viewer was left). Both are plain JSON, written whenever
/// something changes and again on quit.
/// </summary>
public sealed class NoteStore
{
    public const string NotesFile = "asset-notes.json";
    public const string PositionsFile = "viewer-positions.json";

    sealed class NotesDoc
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("saved")] public string Saved { get; set; } = "";
        [JsonPropertyName("counts")] public Dictionary<string, int> Counts { get; set; } = new();
        [JsonPropertyName("assets")] public SortedDictionary<string, AssetNote> Assets { get; set; } = new(StringComparer.Ordinal);
    }

    sealed class PositionsDoc
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("last")] public Position Last { get; set; }
        [JsonPropertyName("bookmarks")] public List<Position> Bookmarks { get; set; } = new();
    }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Folder { get; }
    public string NotesPath => Path.Combine(Folder, NotesFile);
    public string PositionsPath => Path.Combine(Folder, PositionsFile);

    readonly SortedDictionary<string, AssetNote> _notes = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, AssetNote> Notes => _notes;
    public Position Last { get; set; }
    public readonly List<Position> Bookmarks = new();
    /// <summary>Set when a file could not be read or written; shown in the status line.</summary>
    public string Problem { get; private set; } = "";
    public DateTime LastSaved { get; private set; }
    public bool Dirty { get; private set; }

    public NoteStore(string folder)
    {
        Folder = folder;
        Load();
    }

    /// <summary>
    /// Where the files live: <c>--notes=&lt;folder&gt;</c> on the command line, else the GALHACK_ASSET_NOTES
    /// environment variable, else an asset-notes folder in the main checkout (so a removed worktree can't take your notes with it).
    /// </summary>
    public static string DefaultFolder()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--notes=", StringComparison.Ordinal)) return Path.GetFullPath(arg["--notes=".Length..]);
        string env = System.Environment.GetEnvironmentVariable("GALHACK_ASSET_NOTES");
        if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);
        string repo = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".."));
        return Path.Combine(MainCheckout(repo), "asset-notes");
    }

    /// <summary>A worktree's .git is a file naming its git dir; the main checkout is two folders above the shared one.</summary>
    public static string MainCheckout(string repo)
    {
        try
        {
            string git = Path.Combine(repo, ".git");
            if (File.Exists(git))
            {
                string line = File.ReadAllLines(git).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal));
                if (line != null)
                {
                    string dir = line["gitdir:".Length..].Trim(); // <main>/.git/worktrees/<name>
                    string common = Path.GetFullPath(Path.Combine(dir, "..", ".."));
                    if (Path.GetFileName(common) == ".git") return Path.GetDirectoryName(common);
                }
            }
        }
        catch (Exception) { }
        return repo;
    }

    // ---- notes ------------------------------------------------------------------------------------

    public AssetNote Get(AssetEntry e) => _notes.TryGetValue(e.Key, out var n) ? n : null;

    /// <summary>The note for this asset, created (and filed) on first use.</summary>
    public AssetNote GetOrCreate(AssetEntry e)
    {
        if (!_notes.TryGetValue(e.Key, out var n))
        {
            n = new AssetNote { Category = e.Category, Group = e.Group, Name = e.Name, Id = e.Id };
            _notes[e.Key] = n;
        }
        return n;
    }

    public Note GetAnimation(AssetEntry e, string animation) =>
        Get(e) is { } n && n.Animations.TryGetValue(animation, out var a) ? a : null;

    public Note GetOrCreateAnimation(AssetEntry e, string animation)
    {
        AssetNote n = GetOrCreate(e);
        if (!n.Animations.TryGetValue(animation, out var a)) n.Animations[animation] = a = new Note();
        return a;
    }

    /// <summary>Records what was on screen when a note was edited, and marks the store for saving.</summary>
    public void Touch(AssetEntry e, ulong seed, IReadOnlyDictionary<string, string> options)
    {
        AssetNote n = GetOrCreate(e);
        n.Name = e.Name;
        n.Group = e.Group;
        n.Seed = e.Randomisable ? seed : null;
        n.Options = new Dictionary<string, string>(options);
        n.Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Dirty = true;
    }

    public void MarkDirty() => Dirty = true;

    public (int liked, int problems, int commented) Counts()
    {
        int l = 0, p = 0, c = 0;
        foreach (AssetNote n in _notes.Values)
        {
            if (n.Like || n.Animations.Values.Any(a => a.Like)) l++;
            if (n.Problem || n.Animations.Values.Any(a => a.Problem)) p++;
            if (!string.IsNullOrWhiteSpace(n.Comment) || n.Animations.Values.Any(a => !string.IsNullOrWhiteSpace(a.Comment))) c++;
        }
        return (l, p, c);
    }

    /// <summary>A plain-text report of every problem and comment, for pasting to an agent.</summary>
    public string Report()
    {
        var sb = new StringBuilder();
        void Section(string title, Func<AssetNote, bool> pick)
        {
            var list = _notes.Values.Where(pick).OrderBy(n => n.Category).ThenBy(n => n.Group).ThenBy(n => n.Name).ToList();
            if (list.Count == 0) return;
            sb.AppendLine($"## {title} ({list.Count})");
            foreach (AssetNote n in list)
            {
                string seed = n.Seed.HasValue ? $", seed {n.Seed}" : "";
                string opts = n.Options.Count > 0 ? ", " + string.Join(", ", n.Options.Select(kv => $"{kv.Key}={kv.Value}")) : "";
                sb.AppendLine($"- {n.Category} / {(n.Group.Length > 0 ? n.Group + " / " : "")}{n.Name} [{n.Id}{seed}{opts}]");
                if (!string.IsNullOrWhiteSpace(n.Comment)) sb.AppendLine("    " + n.Comment.Trim().Replace("\n", "\n    "));
                foreach (var (anim, note) in n.Animations.Where(a => !a.Value.Empty))
                {
                    string tag = note.Problem ? " (problem)" : note.Like ? " (really like)" : "";
                    sb.AppendLine($"    animation {anim}{tag}: {note.Comment.Trim().Replace("\n", " ")}");
                }
            }
            sb.AppendLine();
        }
        Section("Problems", n => n.Problem || n.Animations.Values.Any(a => a.Problem));
        Section("Really like", n => n.Like || n.Animations.Values.Any(a => a.Like));
        Section("Comments only", n => !n.Problem && !n.Like && n.Animations.Values.All(a => !a.Problem && !a.Like) && !n.Empty);
        return sb.ToString();
    }

    // ---- positions --------------------------------------------------------------------------------

    public void AddBookmark(Position p) { Bookmarks.Add(p); Dirty = true; }
    public void RemoveBookmark(Position p) { Bookmarks.Remove(p); Dirty = true; }

    // ---- files ------------------------------------------------------------------------------------

    void Load()
    {
        try
        {
            if (File.Exists(NotesPath))
            {
                var doc = JsonSerializer.Deserialize<NotesDoc>(File.ReadAllText(NotesPath, Encoding.UTF8), Json);
                if (doc?.Assets != null) foreach (var (k, v) in doc.Assets) _notes[k] = v;
            }
            if (File.Exists(PositionsPath))
            {
                var doc = JsonSerializer.Deserialize<PositionsDoc>(File.ReadAllText(PositionsPath, Encoding.UTF8), Json);
                Last = doc?.Last;
                if (doc?.Bookmarks != null) Bookmarks.AddRange(doc.Bookmarks);
            }
        }
        catch (Exception ex)
        {
            Problem = "could not read notes: " + ex.Message;
            GD.PrintErr(Problem);
            // Keep the unreadable file rather than overwrite it.
            try { if (File.Exists(NotesPath)) File.Copy(NotesPath, NotesPath + ".unreadable", true); } catch (Exception) { }
        }
    }

    /// <summary>Writes both files. Empty notes are left out. Returns false (and sets <see cref="Problem"/>) on failure.</summary>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var doc = new NotesDoc { Saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
            foreach (var (k, v) in _notes)
            {
                foreach (string dead in v.Animations.Where(a => a.Value.Empty).Select(a => a.Key).ToList()) v.Animations.Remove(dead);
                if (!v.Empty) doc.Assets[k] = v;
            }
            var (l, p, c) = Counts();
            doc.Counts = new Dictionary<string, int> { ["assets_with_notes"] = doc.Assets.Count, ["really_like"] = l, ["problem"] = p, ["commented"] = c };
            WriteAtomic(NotesPath, JsonSerializer.Serialize(doc, Json));
            WriteAtomic(PositionsPath, JsonSerializer.Serialize(new PositionsDoc { Last = Last, Bookmarks = Bookmarks }, Json));
            Problem = "";
            LastSaved = DateTime.Now;
            Dirty = false;
            return true;
        }
        catch (Exception ex)
        {
            Problem = "could not save: " + ex.Message;
            GD.PrintErr(Problem);
            return false;
        }
    }

    static void WriteAtomic(string path, string text)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}
