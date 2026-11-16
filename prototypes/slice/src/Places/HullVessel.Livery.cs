using System;
using PoiGenerator.Gen;

namespace Slice.Places;

/// <summary>
/// The livery in large panels, as DESIGN's faction table has it ("Colour and markings"), laid over the form's own
/// paints a few metres at a time, never a coat per block: Freeholders faded, their hull numbers hand-painted big on
/// both flanks; Meridian corporate bands round the hull in bright primaries and chevrons over the prow; the Exchange's
/// merchant banners down the flanks and a great checker on top; Reavers rust with warning stripes slashed over the ram
/// and rows of kill marks; the Kessari bare metal pricked with glowing status lights; the Vharn's dark carapace with a
/// glowing line down its back and glowing arcs across it. Only the plain hull plating takes it: trim, machinery, tanks,
/// cargo and flesh keep their own.
/// </summary>
public sealed partial class HullVessel
{
    /// <summary>The paint on the outside: the form's part's own, or the livery's panel over the hull plating there.</summary>
    ushort Livery(int u, int y, int v)
    {
        string name = H.Form.PaintOf(u, y, v) ?? "hull";
        if (!Plain && (name == "hull" || H.Faction == Faction.Kessari && name == "machine") && Panel(u, y, v) is { } panel && Paint.TryGetValue(panel, out ushort p)) return p;
        return Paint.TryGetValue(name, out ushort q) ? q : Paint["hull"];
    }

    /// <summary>Which way a shell cell faces out: 0 +u, 1 -u, 2 +y, 3 -y, 4 +v, 5 -v (the first open side), or -1.</summary>
    int Facing(int u, int y, int v) =>
        Mask(u + 1, y, v) == 0 ? 0 : Mask(u - 1, y, v) == 0 ? 1 : Mask(u, y + 1, v) == 0 ? 2 : Mask(u, y - 1, v) == 0 ? 3 : Mask(u, y, v + 1) == 0 ? 4 : Mask(u, y, v - 1) == 0 ? 5 : -1;

    /// <summary>The livery's panel at a shell cell, or null for the hull's own colour.</summary>
    string Panel(int u, int y, int v)
    {
        int face = Facing(u, y, v), L = H.L, mid = H.ExtentY / 2;
        ulong roll = H.Seed;
        int Pick(int lo, int hi, int salt) => lo + (int)(Hash.Combine(roll, (ulong)salt) % (ulong)(hi - lo + 1));
        bool flank = face is 0 or 1, top = face is 2 or 3;
        switch (H.Faction)
        {
            case Faction.Freeholders:
            {
                // A faded band round the hull, and the hull number painted big on both flanks behind the nose.
                int band = L * Pick(18, 30, 1) / 100;
                if (Math.Abs(v - band) <= 1) return "faded";
                if (!flank) return null;
                int number = Pick(10, 99, 2), dx = face == 0 ? v - L * 6 / 100 : L * 6 / 100 + 13 - v, dy = mid + 5 - y;
                if (dx < 0 || dy < 0 || dx >= 14 || dy >= 10 || dx / 2 == 3) return null;
                int x = dx / 2, row = dy / 2;
                int digit = x < 3 ? number / 10 : number % 10;
                return Glyph(digit, x < 3 ? x : x - 4, row) ? "band" : null;
            }
            case Faction.Meridian:
            {
                // Corporate bands round the hull, the brand's two primaries side by side, and chevrons over the prow.
                foreach (int at in new[] { L * Pick(24, 34, 3) / 100, L * Pick(62, 72, 4) / 100 })
                {
                    if (v >= at && v < at + 3) return "band";
                    if (v >= at + 4 && v < at + 6) return "band2";
                }
                if (top && v < L * 15 / 100 && Mod(v + Math.Abs(u), 9) < 3) return "band";
                return null;
            }
            case Faction.Exchange:
            {
                // Merchant banners hung down the flanks, and a great checker over the top.
                if (flank && Mod(v - Pick(0, 13, 5), 14) is int b && b < 5) return b < 3 ? "band" : "band2";
                if (top && Mod(v / 4 + u / 4 - (u < 0 ? 1 : 0), 2) == 0 && Mod(v, 30) < 12) return "band2";
                return null;
            }
            case Faction.Reavers:
            {
                // Warning stripes slashed over the ram, kill marks in a row down a flank.
                if (v < L * 22 / 100) return Mod(v + y + Math.Abs(u), 6) < 3 ? "band" : "band2";
                int kills = Pick(5, 16, 6), from = L * Pick(30, 40, 7) / 100;
                if (flank && v >= from && v < from + kills * 2 && Mod(v - from, 2) == 0 && y >= mid - 1 && y <= mid + 2) return "mark";
                return null;
            }
            case Faction.Kessari:
                // Status lights in even rows round the machinery.
                return Mod(v, 6) == 0 && Mod(u + y, 4) == 0 ? "glow" : null;
            case Faction.Vharn:
                // A glowing line down its back, glowing arcs across it.
                if (face == 2 && u == 0) return "glow";
                if (top && Mod(v, 11) == 0 && Math.Abs(u) < H.ExtentU / 3) return "glow";
                return null;
        }
        return null;
    }

    /// <summary>A 3 by 5 numeral, row 0 at its top: is its cell (x, row) painted?</summary>
    static bool Glyph(int digit, int x, int row)
    {
        string[] font = { "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001", "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111" };
        return font[digit][row * 3 + x] == '1';
    }
}
