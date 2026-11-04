using Godot;
using NVector3 = System.Numerics.Vector3;

namespace Encounters;

/// <summary>
/// A 1970s sci-fi paperback palette, after Chris Foss: cream hulls, big blocks of orange, yellow,
/// red and teal, hazard stripes and checkerboards, against deep painted space.
/// </summary>
public static class Palette
{
    public static readonly Color Cream = new("e9dfc4");
    public static readonly Color Orange = new("e85d04");
    public static readonly Color Yellow = new("f4a100");
    public static readonly Color Red = new("c1121f");
    public static readonly Color Teal = new("0a9396");
    public static readonly Color Dark = new("2b2d42");
    public static readonly Color Navy = new("1d3557");
    public static readonly Color Rust = new("8b3a1e");
    public static readonly Color Ochre = new("e9b44c");
    public static readonly Color Steel = new("8d99ae");
    public static readonly Color Bone = new("cbbba6");
    public static readonly Color Violet = new("7b2cbf");
    public static readonly Color Magenta = new("d4358c");
    public static readonly Color Cyan = new("6fe3d6");
    public static readonly Color PaleBlue = new("a8dadc");
    public static readonly Color Warm = new("ffd9a0");
    public static readonly Color Black = new("16161d");

    // HUD
    public static readonly Color Text = new("ffe8c2");
    public static readonly Color TextDim = new(1f, 0.91f, 0.76f, 0.6f);
    public static readonly Color Distress = new("ff5d3a");
    public static readonly Color Transmission = new("6fe3d6");
    public static readonly Color Lasting = new("9bf26b");

    /// <summary>The colour a faction reads as on the HUD and the map.</summary>
    public static Color Faction(string faction) => faction switch
    {
        "concord" => new Color("8ecae6"),
        "reavers" => new Color("ff5a36"),
        "kessari" => new Color("f2c14e"),
        "hollow" => new Color("e055b8"),
        "guild" => new Color("f4a261"),
        _ => new Color("cfcfcf"),
    };

    public static Color Lead(string kind) => kind == "distress" ? Distress : Transmission;
}

/// <summary>The director speaks System.Numerics; Godot has its own vectors.</summary>
public static class Conv
{
    public static Vector3 ToGodot(this NVector3 v) => new(v.X, v.Y, v.Z);

    public static NVector3 ToNum(this Vector3 v) => new(v.X, v.Y, v.Z);
}
