using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Slice.Game;

/// <summary>One thing a key does: its id (in the settings file), its default key, its name and where it is used.</summary>
public sealed record KeyAction(string Id, Key Default, string Name, string Uses, string Group);

/// <summary>
/// Which key does what. The game reads every action by its default key (<see cref="Controls"/>
/// holds "W" for forward whatever key you press for it), so a rebinding changes nothing but this
/// table: a key pressed is turned into the actions bound to it before the game sees it. Keys no
/// action has, such as the menus' own (Esc, /, + and -), pass through as themselves; a default key
/// whose action has moved elsewhere does nothing until something is bound to it again.
/// </summary>
public sealed class KeyBindings
{
    public const string Move = "MOVE", Hands = "HANDS", Build = "BUILD AND FLY", Menus = "MENUS", Toolbar = "TOOLBAR";

    /// <summary>Every rebindable action, in the order the Controls page lists them.</summary>
    public static readonly KeyAction[] Actions = Make();

    /// <summary>Keys that are never bound: Esc is the escape menu's (and cancels a rebinding).</summary>
    public static readonly Key[] Fixed = { Key.Escape };

    static KeyAction[] Make()
    {
        var list = new List<KeyAction>
        {
            new("forward", Key.W, "Forward", "walk, throttle, fly", Move),
            new("back", Key.S, "Back", "walk back, brake, fly back", Move),
            new("left", Key.A, "Left", "strafe, steer, slide left", Move),
            new("right", Key.D, "Right", "strafe, steer, slide right", Move),
            new("up", Key.Space, "Jump / up", "jetpack, swim and fly up, hop, struggle", Move),
            new("down", Key.Ctrl, "Crouch / down", "climb, swim and fly down", Move),
            new("sprint", Key.Shift, "Sprint / boost", "on foot, bike and vessel", Move),
            new("use", Key.E, "Use", "open, take, pick up; roll right", Hands),
            new("seat", Key.F, "Sit / ride", "seats, bikes, the pod; get up", Hands),
            new("tool-mode", Key.Q, "Tool mode", "gather, shoot, take down; fire mode; roll left", Hands),
            new("helmet", Key.H, "Helmet", "open or seal", Hands),
            new("replicator", Key.B, "Replicator", "set down or pack up", Hands),
            new("torch", Key.L, "Head torch", "on or off", Hands),
            new("view", Key.V, "Third person", "camera behind you", Hands),
            new("rotate", Key.R, "Rotate block", "turn deco; reload; respawn", Build),
            new("tilt", Key.T, "Tilt block", "the other axis", Build),
            new("mirror", Key.X, "Mirror", "build left and right", Build),
            new("style", Key.G, "Deco style / gear", "next style; landing gear", Build),
            new("cruise", Key.C, "Cruise", "hold speed in flight", Build),
            new("switch-bar", Key.Y, "Switch toolbar", "yours or the structure's", Build),
            new("terminal", Key.Tab, "Suit terminal", "the Tab menu", Menus),
            new("map", Key.M, "Map", "open or close", Menus),
            new("debug", Key.F3, "Debug readout", "numbers for testing", Menus),
        };
        for (int i = 0; i < 9; i++) list.Add(new($"slot-{i + 1}", Key.Key1 + i, $"Slot {i + 1}", "", Toolbar));
        return list.ToArray();
    }

    public static KeyAction Action(string id) => Actions.FirstOrDefault(a => a.Id == id);

    readonly Dictionary<string, Key> _bound = new();
    /// <summary>A key pressed: the default keys of the actions bound to it.</summary>
    readonly Dictionary<Key, List<Key>> _on = new();
    /// <summary>Default keys whose action is bound somewhere else.</summary>
    readonly HashSet<Key> _moved = new();

    public KeyBindings()
    {
        foreach (KeyAction a in Actions) _bound[a.Id] = a.Default;
        Rebuild();
    }

    /// <summary>Goes up with every change (the menu redraws, the settings save).</summary>
    public int Version { get; private set; }

    public Key this[string id] => _bound.TryGetValue(id, out Key k) ? k : Key.None;

    /// <summary>Binds an action to a key. Esc can't be bound; false if refused.</summary>
    public bool Bind(string id, Key key)
    {
        if (!_bound.ContainsKey(id) || key == Key.None || Array.IndexOf(Fixed, key) >= 0) return false;
        _bound[id] = key;
        Rebuild();
        return true;
    }

    public void Reset()
    {
        foreach (KeyAction a in Actions) _bound[a.Id] = a.Default;
        Rebuild();
    }

    public bool IsDefault => Actions.All(a => _bound[a.Id] == a.Default);

    /// <summary>The key an action is read by in the game (its default) as it is held now: the key bound to it.</summary>
    public Key BoundFor(Key action)
    {
        foreach (KeyAction a in Actions)
            if (a.Default == action) return _bound[a.Id];
        return action;
    }

    /// <summary>What a key pressed means to the game: the actions bound to it (by their default keys), or itself if no action has it.</summary>
    public IReadOnlyList<Key> ActionsOn(Key key)
    {
        if (_on.TryGetValue(key, out List<Key> actions)) return actions;
        return _moved.Contains(key) ? Array.Empty<Key>() : new[] { key };
    }

    /// <summary>The other actions on the same key as this one (a conflict), by name.</summary>
    public IEnumerable<KeyAction> Clashes(string id)
    {
        Key key = this[id];
        return Actions.Where(a => a.Id != id && _bound[a.Id] == key);
    }

    public bool HasClashes => Actions.Any(a => Clashes(a.Id).Any());

    void Rebuild()
    {
        _on.Clear();
        _moved.Clear();
        foreach (KeyAction a in Actions)
        {
            Key key = _bound[a.Id];
            if (!_on.TryGetValue(key, out List<Key> list)) _on[key] = list = new List<Key>();
            list.Add(a.Default);
            if (key != a.Default) _moved.Add(a.Default);
        }
        Version++;
    }

    /// <summary>A key's name as the menu shows it: "W", "SPACE", "F3", "1".</summary>
    public static string Name(Key key) => key == Key.None ? "-" : OS.GetKeycodeString(key).ToUpperInvariant();

    /// <summary>A key from its saved name (<see cref="OS.GetKeycodeString"/>), or None.</summary>
    public static Key Parse(string name) => string.IsNullOrWhiteSpace(name) ? Key.None : OS.FindKeycodeFromString(name);

    /// <summary>Every binding by action id, as the settings file keeps them (key names).</summary>
    public IEnumerable<(string Id, string Key)> Saved => Actions.Select(a => (a.Id, OS.GetKeycodeString(_bound[a.Id])));

    /// <summary>Takes a binding from the settings file: an unknown action or an unreadable key is ignored (its default stands).</summary>
    public void Restore(string id, string keyName)
    {
        if (!_bound.ContainsKey(id)) return;
        Key key = Parse(keyName);
        if (key == Key.None || Array.IndexOf(Fixed, key) >= 0) return;
        _bound[id] = key;
        Rebuild();
    }
}
