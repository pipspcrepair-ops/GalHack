using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// How a faction builds a hull (data/hull-styles.txt): shape rules first, since a faction must read by its silhouette at
/// long range before colour or detail (DESIGN, "Ships and structures by faction"), then its livery. One rule set for every
/// generated vessel and station, the factions only its numbers: proportions, the bow, the shoulders' chamfer, the spine,
/// segment waists, ribs and a lattice spine, cargo pods, bolted-on modules, mismatched plates, spikes, swept fins, mirror
/// symmetry, the tower's offset and the engine bank; then the colours, the band's pattern, grime, glowing strips and
/// stencilled hull numbers. Counts are per 100 m of hull, lengths and beams shares of the size class's.
/// </summary>
public sealed class HullStyle
{
    public string Id = "";
    public float Length = 1f, Beam = 1f;
    /// <summary>The bow: taper, round, blunt, needle or ram (a keel blade under a raked prow).</summary>
    public string Bow = "taper";
    /// <summary>How far the shoulders slope in, as a share of the hull's height (the chamfer; small is a block).</summary>
    public float Shoulder = 0.33f;
    /// <summary>The dorsal spine's height and half width, m.</summary>
    public float Spine = 1.5f, SpineWidth = 1f;
    /// <summary>Waists between the hull's segments: how many, and how deep (a share of the beam taken in).</summary>
    public int Segments;
    public float Waist;
    /// <summary>Frames round the hull every so many metres, standing out this far (0: none); a lattice truss down the spine.</summary>
    public int Ribs, Rib = 1;
    public bool Lattice;
    /// <summary>Per 100 m: cargo pod stacks, bolted-on modules, spikes; the share of the shell in mismatched plates; swept fins (count).</summary>
    public float Pods, Bolted, Spikes, Plates;
    public int Fins;
    /// <summary>Everything added mirrored port and starboard.</summary>
    public bool Symmetric;
    /// <summary>The tower's shift to starboard, a share of its half width (the alien hulls' lopsided crown).</summary>
    public float TowerOffset;
    /// <summary>The engine bank: nacelles (fewest, most), their length against the hull's and their height (a share of the hull's).</summary>
    public int EnginesMin = 2, EnginesMax = 3;
    public float EngineLength = 1f, EngineHeight = 0.55f;
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
                case "bow": cur.Bow = w[1]; break;
                case "shoulder": cur.Shoulder = F(1); break;
                case "spine": cur.Spine = F(1); cur.SpineWidth = w.Length > 2 ? F(2) : 1f; break;
                case "segments": cur.Segments = I(1); cur.Waist = F(2); break;
                case "ribs": cur.Ribs = I(1); cur.Rib = w.Length > 2 ? I(2) : 1; break;
                case "lattice": cur.Lattice = B(1); break;
                case "pods": cur.Pods = F(1); break;
                case "bolted": cur.Bolted = F(1); break;
                case "spikes": cur.Spikes = F(1); break;
                case "plates": cur.Plates = F(1); break;
                case "fins": cur.Fins = I(1); break;
                case "symmetric": cur.Symmetric = B(1); break;
                case "tower-offset": cur.TowerOffset = F(1); break;
                case "engines": cur.EnginesMin = I(1); cur.EnginesMax = I(2); cur.EngineLength = F(3); cur.EngineHeight = F(4); break;
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
