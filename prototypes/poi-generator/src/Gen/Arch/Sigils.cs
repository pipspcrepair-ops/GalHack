#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>A faction's sigil as painted cells (data/sigils.txt, from the art in docs/signs/sigils): rows top to bottom, a colour per letter.</summary>
public sealed class SigilDef
{
    public string Faction = "";
    public readonly Dictionary<char, Rgb> Colours = new();
    public readonly List<string> Rows = new();
    public int Size => Rows.Count;

    public static Dictionary<string, SigilDef> Parse(string text, string source)
    {
        var all = new Dictionary<string, SigilDef>();
        SigilDef? s = null;
        int n = 0;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            n++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("sigil "))
            {
                s = new SigilDef { Faction = line[6..].Trim() };
                all[s.Faction] = s;
            }
            else if (s == null) throw new FormatException($"{source}:{n}: needs a sigil first");
            else if (line.StartsWith("colours "))
                foreach (string pair in line[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    s.Colours[pair[0]] = Rgb.Parse(pair[2..]);
            else if (line.StartsWith('|') && line.EndsWith('|')) s.Rows.Add(line[1..^1]);
            else throw new FormatException($"{source}:{n}: '{line}'");
        }
        return all;
    }
}

public static class SigilPaint
{
    /// <summary>
    /// Paints a sigil onto the face of a wall that runs along u (or v) at cell row <paramref name="v"/>, its lower
    /// left corner (as seen from outside) at cell (<paramref name="u"/>, <paramref name="y"/>). The colours are the
    /// player's paint on the wall's own blocks; a faction whose marks glow paints its accent in lumen.
    /// <paramref name="scale"/> 2 halves it (a pair of rows and columns to a cell). Only solid wall blocks take paint.
    /// </summary>
    public static int Paint(Plot p, SigilDef sigil, int u, int y, int v, bool alongU, bool fromMinusSide, bool glowAccent, int scale = 1)
    {
        int size = sigil.Size / scale, painted = 0;
        for (int row = 0; row < size; row++)
            for (int col = 0; col < size; col++)
            {
                char ch = sigil.Rows[row * scale][col * scale];
                if (!sigil.Colours.TryGetValue(ch, out Rgb colour)) continue;
                // So it reads the right way round from outside: facing +v the viewer's right is -u, facing +u it is +v.
                int along = alongU == fromMinusSide ? size - 1 - col : col, up = size - 1 - row;
                int cu = alongU ? u + along : v, cv = alongU ? v : u + along;
                Block b = p.Get(alongU ? cu : v, y + up, alongU ? v : cv);
                if (b.Type != BlockType.Cube) continue;
                bool glow = glowAccent && ch != 'a';
                p.Put(alongU ? cu : v, y + up, alongU ? v : cv, new Block(BlockType.Cube, glow ? Material.Lumen : b.Material, colour, glow ? Role.Light : Role.Wall));
                painted++;
            }
        return painted;
    }
}
