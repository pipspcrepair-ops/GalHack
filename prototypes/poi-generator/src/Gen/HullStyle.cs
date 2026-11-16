using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A faction's livery (data/hull-styles.txt): the colours, the band's pattern, grime, glowing strips and stencilled hull
/// numbers, the cargo pods' and scrap plates' colours; and the numbers a broken station is built by (its proportions,
/// segments, ribs, pods, bolted-on modules, plates, spikes, fins, symmetry and engine block). A capital ship's shape is
/// its faction's body plan (ShipForm), never these.
/// </summary>
public sealed class HullStyle
{
    public string Id = "";
    /// <summary>A station's proportions.</summary>
    public float Length = 1f, Beam = 1f;
    /// <summary>A station's segments.</summary>
    public int Segments;
    /// <summary>A station's frames round its hub (0: none).</summary>
    public int Ribs;
    /// <summary>Per 100 m: cargo pod stacks, bolted-on modules, spikes; the share of the shell in mismatched plates; swept fins (count).</summary>
    public float Pods, Bolted, Spikes, Plates;
    public int Fins;
    /// <summary>Everything added mirrored port and starboard.</summary>
    public bool Symmetric;
    /// <summary>A station's engine block, its length against the default.</summary>
    public float EngineLength = 1f;
    /// <summary>Livery: hull, trim, the band and a second colour, the dark of the vents; the band's pattern; grime 0 to 3.</summary>
    public Rgb Hull = new(0x9a, 0x9a, 0x94), Trim = new(0x6a, 0x6a, 0x66), Band = new(0xd0, 0x70, 0x30), Band2 = new(0x30, 0x8a, 0x8a), Dark = new(0x2a, 0x2e, 0x34);
    public string Pattern = "stripes";
    public int Grime = 2;
    /// <summary>Glowing strips' colour (null: none), and stencilled hull numbers on the bow.</summary>
    public Rgb? Glow;
    public bool Numbers;
    /// <summary>The cargo pods' colours (colour-coded), and the mismatched plates' and bolted modules'.</summary>
    public List<Rgb> Cargo = new(), Scrap = new();

    /// <summary>Every faction's style, as the library last read them (data/hull-styles.txt).</summary>
    public static Dictionary<string, HullStyle> Shared = new();

    /// <summary>The style for a faction, from the library's (none, or a faction with no hulls of its own, takes a human faction's by the seed).</summary>
    public static HullStyle For(IReadOnlyDictionary<string, HullStyle> styles, Faction faction, ulong seed)
    {
        if (styles.TryGetValue(faction.Id(), out HullStyle s)) return s;
        string[] human = { "freeholders", "meridian", "exchange", "reavers" };
        return styles.TryGetValue(human[(int)(seed % (ulong)human.Length)], out s) ? s : new HullStyle { Id = "default" };
    }

    public static Dictionary<string, HullStyle> Parse(string text, string source)
    {
        var all = new Dictionary<string, HullStyle>();
        HullStyle cur = null;
        int n = 0;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            n++;
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                all[line[1..^1]] = cur = new HullStyle { Id = line[1..^1] };
                continue;
            }
            if (cur == null) throw new FormatException($"{source}:{n}: a rule before any [faction]");
            string[] w = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            float F(int i) => float.Parse(w[i], CultureInfo.InvariantCulture);
            int I(int i) => int.Parse(w[i], CultureInfo.InvariantCulture);
            Rgb C(int i) => Rgb.Parse(w[i]);
            bool B(int i) => w[i] is "yes" or "true";
            switch (w[0])
            {
                case "length": cur.Length = F(1); break;
                case "beam": cur.Beam = F(1); break;
                case "segments": cur.Segments = I(1); break;
                case "ribs": cur.Ribs = I(1); break;
                case "pods": cur.Pods = F(1); break;
                case "bolted": cur.Bolted = F(1); break;
                case "spikes": cur.Spikes = F(1); break;
                case "plates": cur.Plates = F(1); break;
                case "fins": cur.Fins = I(1); break;
                case "symmetric": cur.Symmetric = B(1); break;
                case "engines": cur.EngineLength = F(1); break;
                case "hull": cur.Hull = C(1); break;
                case "trim": cur.Trim = C(1); break;
                case "band": cur.Band = C(1); cur.Band2 = w.Length > 2 ? C(2) : cur.Band; break;
                case "dark": cur.Dark = C(1); break;
                case "pattern": cur.Pattern = w[1]; break;
                case "grime": cur.Grime = I(1); break;
                case "glow": cur.Glow = w[1] == "none" ? null : C(1); break;
                case "numbers": cur.Numbers = B(1); break;
                case "cargo": cur.Cargo = w.Skip(1).Select(Rgb.Parse).ToList(); break;
                case "scrap": cur.Scrap = w.Skip(1).Select(Rgb.Parse).ToList(); break;
                default: throw new FormatException($"{source}:{n}: unknown hull rule '{w[0]}'");
            }
        }
        return all;
    }
}
