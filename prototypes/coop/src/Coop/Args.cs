using System.Collections.Generic;
using Godot;

namespace Coop;

/// <summary>Command-line options after "--": --flag or --key=value.</summary>
public sealed class Args
{
    readonly Dictionary<string, string> _values = new();

    public static Args FromCommandLine()
    {
        var args = new Args();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--")) continue;
            int eq = arg.IndexOf('=');
            if (eq < 0) args._values[arg[2..]] = "";
            else args._values[arg[2..eq]] = arg[(eq + 1)..];
        }
        // Steam passes "+connect_lobby <id>" when a friend launches the game from an invite.
        string[] all = OS.GetCmdlineArgs();
        for (int i = 0; i < all.Length - 1; i++)
            if (all[i] == "+connect_lobby") args._values["steam-join"] = all[i + 1];
        return args;
    }

    public bool Has(string key) => _values.ContainsKey(key);
    public string Get(string key, string fallback = null) => _values.TryGetValue(key, out string v) && v.Length > 0 ? v : fallback;
    public int GetInt(string key, int fallback) => int.TryParse(Get(key), out int v) ? v : fallback;
}
