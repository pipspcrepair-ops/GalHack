using System;
using System.Collections.Generic;
using System.Globalization;

namespace GalHack.Structures;

public enum Pattern : byte { None, Stripes, Chevrons, Checker, Hazard }

/// <summary>The surface finish under the paint: panel seams, plain, riveted plates or ribs.</summary>
public enum FaceTexture : byte { Panel, Smooth, Plates, Ribbed }

/// <summary>
/// What one face looks like: a base colour, an accent colour for the pattern and stencil, a
/// surface texture and a grime level (0 clean to 3 filthy). Colours are 0xRRGGBB. A stencil is
/// a character ('0'..'9') or 0 for none. Paint is a value: identical paints share one palette slot.
/// </summary>
public readonly record struct Paint(uint Base, uint Accent, Pattern Pattern, FaceTexture Texture, byte Stencil, byte Grime)
{
    /// <summary>Set for glowing paint: the colour, second colour, mode, speed and offset it lights with (the paint's Base is ignored for the glow). A lit paint has no pattern or stencil.</summary>
    public LightSettings? Lamp { get; init; }
    public LampShape Shape { get; init; }
    public bool IsLamp => Lamp != null && Shape != LampShape.None;

    /// <summary>Glowing paint: a face, strip, panel or border lit with these settings.</summary>
    public static Paint Lit(LampShape shape, LightSettings settings) =>
        new(settings.Color, DefaultAccent, Pattern.None, FaceTexture.Smooth, 0, 0) { Lamp = settings, Shape = shape };

    public const uint DefaultAccent = 0x2b2d42;

    public static Paint Solid(uint color) => new(color, DefaultAccent, Pattern.None, FaceTexture.Panel, 0, 0);

    public Paint WithPattern(Pattern p, uint accent) => this with { Pattern = p, Accent = accent };
    public Paint WithStencil(char c, uint accent) => this with { Stencil = (byte)c, Accent = accent };

    public static string Hex(uint c) => c.ToString("x6", CultureInfo.InvariantCulture);

    public static bool TryParseHex(string s, out uint c)
    {
        c = 0;
        s = s.TrimStart('#');
        return s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out c);
    }
}

/// <summary>
/// A structure's paints. Faces store a slot number, so a block costs 12 bytes of paint however
/// fancy its livery. Slot 0 means unpainted: the face shows its material's own colour, which is
/// why upgrading steel to hardened changes the look of bare faces but never of painted ones.
/// </summary>
public sealed class PaintPalette
{
    readonly List<Paint> _paints = new() { default };
    readonly List<string> _names = new() { "-" };
    readonly Dictionary<Paint, ushort> _bySlot = new();
    readonly Dictionary<string, ushort> _byName = new(StringComparer.Ordinal);

    public int Count => _paints.Count;
    public Paint this[ushort slot] => _paints[slot];
    public string NameOf(ushort slot) => _names[slot];
    public bool TryFind(string name, out ushort slot) => _byName.TryGetValue(name, out slot);

    /// <summary>The slot for a paint, adding it if new. A name is only used for a new slot.</summary>
    public ushort Intern(Paint paint, string name = null)
    {
        if (_bySlot.TryGetValue(paint, out ushort slot)) return slot;
        if (_paints.Count >= ushort.MaxValue) throw new InvalidOperationException("too many paints in one structure");
        slot = (ushort)_paints.Count;
        name = UniqueName(string.IsNullOrEmpty(name) ? $"p{slot}" : name);
        _paints.Add(paint);
        _names.Add(name);
        _bySlot[paint] = slot;
        _byName[name] = slot;
        return slot;
    }

    string UniqueName(string name)
    {
        if (!_byName.ContainsKey(name) && name != "-") return name;
        for (int i = 2; ; i++)
            if (!_byName.ContainsKey($"{name}{i}")) return $"{name}{i}";
    }
}

/// <summary>A palette slot for each of a block's six faces, indexed by <see cref="Dir"/>.</summary>
public struct FacePaint : IEquatable<FacePaint>
{
    public ushort PX, NX, PY, NY, PZ, NZ;

    public static FacePaint All(ushort slot) => new() { PX = slot, NX = slot, PY = slot, NY = slot, PZ = slot, NZ = slot };

    public ushort this[Dir d]
    {
        readonly get => d switch { Dir.PosX => PX, Dir.NegX => NX, Dir.PosY => PY, Dir.NegY => NY, Dir.PosZ => PZ, _ => NZ };
        set
        {
            switch (d)
            {
                case Dir.PosX: PX = value; break;
                case Dir.NegX: NX = value; break;
                case Dir.PosY: PY = value; break;
                case Dir.NegY: NY = value; break;
                case Dir.PosZ: PZ = value; break;
                default: NZ = value; break;
            }
        }
    }

    public readonly bool Uniform => PX == NX && PX == PY && PX == NY && PX == PZ && PX == NZ;

    public readonly bool Equals(FacePaint o) =>
        PX == o.PX && NX == o.NX && PY == o.PY && NY == o.NY && PZ == o.PZ && NZ == o.NZ;
    public override readonly bool Equals(object obj) => obj is FacePaint o && Equals(o);
    public override readonly int GetHashCode() => HashCode.Combine(PX, NX, PY, NY, PZ, NZ);
}
