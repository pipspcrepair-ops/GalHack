using System.Collections.Generic;
using Godot;

namespace ShipHandling;

/// <summary>
/// A 1970s paperback-cover palette after Chris Foss: cream hulls, hot orange and yellow, teal,
/// pillar-box red and near-black machinery, over an ochre desert.
/// </summary>
public static class Palette
{
    public static readonly Color Hull = new("e9dfc4");
    public static readonly Color Orange = new("e85d04");
    public static readonly Color Yellow = new("f4a100");
    public static readonly Color Red = new("c1121f");
    public static readonly Color Teal = new("0a9396");
    public static readonly Color Dark = new("2b2d42");
    public static readonly Color Glass = new(0.35f, 0.6f, 0.7f, 0.55f);
    public static readonly Color Flame = new("ffb347");
    public static readonly Color BoostFlame = new("fff1c1");
    public static readonly Color HoverGlow = new("5ee0e6");

    public static readonly Color Sand = new("d9a066");
    public static readonly Color Ochre = new("c87941");
    public static readonly Color Rust = new("8b3a1e");
    public static readonly Color Rock = new("a0522d");
    public static readonly Color Water = new(0.05f, 0.45f, 0.5f, 0.93f);

    static readonly Dictionary<string, Color> Named = new()
    {
        ["hull"] = Hull, ["orange"] = Orange, ["yellow"] = Yellow, ["red"] = Red,
        ["teal"] = Teal, ["dark"] = Dark, ["glass"] = Glass,
    };

    /// <summary>The colour a structure's block names, by key.</summary>
    public static Color Of(string name) => Named.TryGetValue(name, out var c) ? c : Hull;
}
