using System;
using System.Collections.Generic;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>
/// The colours a set (or a POI's style kit) paints its pieces with, by slot. Built from the
/// palettes in deco.json: the set's own over the base palette. A POI can retint the body and trim
/// with its kit's colours so the furniture matches the walls.
/// </summary>
public sealed class DecoPalette
{
    readonly DecoRgb[] _colors = new DecoRgb[Enum.GetValues<DecoSlot>().Length];
    public string Id { get; }

    DecoPalette(string id) => Id = id;

    public DecoRgb this[DecoSlot s] => _colors[(int)s];

    /// <summary>The palette <paramref name="id"/> from the catalogue, over the base palette.</summary>
    public static DecoPalette From(DecoCatalogue cat, string id)
    {
        var p = new DecoPalette(id);
        foreach (DecoSlot s in Enum.GetValues<DecoSlot>()) p._colors[(int)s] = Fixed(s);
        Apply(p, cat.Palettes["base"]);
        // Paint palettes only change the body: they sit on the vehicles palette.
        if ((id.StartsWith("paint-") || id == "ambulance") && cat.Palettes.TryGetValue("vehicles", out var veh)) Apply(p, veh);
        if (id != "base" && cat.Palettes.TryGetValue(id, out var own)) Apply(p, own);
        return p;
    }

    static void Apply(DecoPalette p, IReadOnlyDictionary<string, string> slots)
    {
        foreach (var (name, hex) in slots)
            if (Enum.TryParse(name, true, out DecoSlot s)) p._colors[(int)s] = DecoRgb.Hex(hex);
    }

    /// <summary>A copy with the style kit's colours on the body and trim (null keeps the set's own).</summary>
    public DecoPalette Tinted(DecoRgb? body, DecoRgb? trim, string id)
    {
        var p = new DecoPalette(id);
        Array.Copy(_colors, p._colors, _colors.Length);
        if (body is DecoRgb b) p._colors[(int)DecoSlot.Body] = _colors[(int)DecoSlot.Body].Lerp(b, 0.7f);
        if (trim is DecoRgb t) p._colors[(int)DecoSlot.Trim] = t;
        return p;
    }

    /// <summary>Colours the variants paint with, the same in every palette.</summary>
    static DecoRgb Fixed(DecoSlot s) => s switch
    {
        DecoSlot.Dead => DecoRgb.Hex("15191d"),
        DecoSlot.Crack => DecoRgb.Hex("aab4bc"),
        DecoSlot.Char => DecoRgb.Hex("1c1714"),
        DecoSlot.Ash => DecoRgb.Hex("6a6560"),
        DecoSlot.Moss => DecoRgb.Hex("4f7a2e"),
        DecoSlot.Vine => DecoRgb.Hex("33502a"),
        DecoSlot.Leaf => DecoRgb.Hex("5f9a3a"),
        DecoSlot.Bloom => DecoRgb.Hex("b8507e"),
        DecoSlot.BloomVein => DecoRgb.Hex("6e2a5a"),
        DecoSlot.Debris => DecoRgb.Hex("5a5650"),
        DecoSlot.Sand => DecoRgb.Hex("c9a46a"),
        DecoSlot.Snow => DecoRgb.Hex("e6ecf0"),
        DecoSlot.Grime => DecoRgb.Hex("4a4238"),
        _ => DecoRgb.Hex("ff00ff"),
    };

    /// <summary>The surface a slot is drawn with.</summary>
    public static DecoSurface SurfaceOf(DecoSlot s) => s switch
    {
        DecoSlot.Metal or DecoSlot.Chrome or DecoSlot.Bronze or DecoSlot.Gold => DecoSurface.Metal,
        DecoSlot.Screen or DecoSlot.Lamp or DecoSlot.Signal or DecoSlot.Resin or DecoSlot.Node or DecoSlot.Node2 or DecoSlot.Liquid => DecoSurface.Glow,
        DecoSlot.Glass => DecoSurface.Glass,
        DecoSlot.Flesh or DecoSlot.Bloom or DecoSlot.BloomVein => DecoSurface.Flesh,
        DecoSlot.Vine or DecoSlot.Leaf => DecoSurface.Leaf,
        _ => DecoSurface.Matte,
    };

    public static bool Glows(DecoSlot s) => SurfaceOf(s) == DecoSurface.Glow;
}
