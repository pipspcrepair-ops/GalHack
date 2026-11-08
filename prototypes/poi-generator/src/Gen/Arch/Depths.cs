#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Places below ground, planned from rules over shared parts rather than drawn by hand. A purpose
/// (data/depths.txt: a shelter, a command post, a cryo vault, a biolab, a mine, a silo, a prison's hold, an
/// alien warren...) names the plan parts it draws on: hub and spokes, rings, grids of cross corridors, halls with
/// galleries over them, great chambers with galleries round, long galleries with alcoves, cell blocks round a
/// guarded hub, rows of pod halls, winding drifts and stopes, round chambers on curved ways. Its builders' tech
/// and habits (a culture rolled per place, or a faction's own), the world's age and how violently it fell tilt
/// every weight, and the years leave their marks on the plan: a collapse that forces a detour, a cave breaking
/// in, a squatters' tunnel, a sealed section with a way round it.
/// <para>Each level picks its own part; levels stack on one core or step down by long stairs, ladders, lifts and
/// ramps; zones are parted by airlocks, decontamination chains and blast doors; service crawlways run beside the
/// main ways; dead ends keep vaults behind them. Everything is on the player's 2 m lattice (<see cref="Plot"/>),
/// and the layout check walks it from the way in.</para>
/// </summary>
internal sealed partial class Depths
{
    // ---- the front doors ----------------------------------------------------------------------

    /// <summary>A whole place below ground with its head on the surface: the grand plan "below".</summary>
    public static void Whole(Canvas c, PoiLibrary lib, SitePart part, FamilyDef? fam, Rarity rarity, Rng rng, LayoutHints? hints)
    {
        TechLevel tech = hints?.Tech ?? WorldProfile.Parse(part.Arg("tech", "mid"));
        var p = new Plot(c, rng.Fork("plot"), tech, hints?.Front ?? Dir.North);
        var d = new Depths(lib, p, part, fam, rarity, rng, hints, tech) { _whole = true };
        p.Approach(0, -3, Dir.North);
        d._yard = p.Yard;
        d.Plan();
        d.Build();
        c.VaultModule = d._vault >= 0 ? d._vault : d._deepest;
        p.Finish();
    }

    /// <summary>The place below ground of a composed site: its head stands at the part's cell, its door on the site's yard.</summary>
    public static (int Vault, int Deepest) InSite(Canvas c, PoiLibrary lib, SitePart part, FamilyDef fam, Rarity rarity, Rng rng, int yard, bool holdsVault, LayoutHints? hints)
    {
        TechLevel tech = hints?.Tech ?? WorldProfile.Parse(part.Arg("tech", "mid"));
        Dir front = (Dir)(((int)Dir.North + part.Rot) % 4);
        var exits = c.ExitCells;
        var p = new Plot(c, rng.Fork("plot"), tech, front, part.X, part.Z);
        c.ExitCells = exits;
        var d = new Depths(lib, p, part, fam, rarity, rng, hints, tech) { _holdsVault = holdsVault };
        d._yard = yard;
        d.Plan();
        d.Build();
        p.Finish();
        return (d._vault, d._deepest);
    }

    // ---- the brief ---------------------------------------------------------------------------

    readonly PoiLibrary Lib;
    readonly Plot P;
    readonly Rng R;
    readonly DepthDef Def;
    readonly FamilyDef? Fam;
    readonly Rarity Rarity;
    readonly TechLevel Tech;
    /// <summary>The builders' habit: orderly, radial, linear, warren or organic.</summary>
    readonly string Culture;
    /// <summary>How long ago it fell (0 yesterday, 1 ages) and how violently.</summary>
    readonly float Wear, Violence;
    readonly bool Alien;
    readonly DGrid G = new();
    readonly List<DSpace> S = new();
    readonly List<DJoint> Joints = new();
    readonly List<Level> Levels = new();
    int _yard = -1, _vault = -1, _deepest = -1;
    bool _holdsVault = true;
    /// <summary>The head on the surface: its block box and its module.</summary>
    (int I0, int K0, int I1, int K1, int H) _head;
    int _headModule = -1;
    DSpace? _top;

    Depths(PoiLibrary lib, Plot p, SitePart part, FamilyDef? fam, Rarity rarity, Rng rng, LayoutHints? hints, TechLevel tech)
    {
        Lib = lib;
        P = p;
        R = rng;
        Fam = fam;
        Rarity = rarity;
        Tech = tech;
        string purpose = part.Arg("purpose", "shelter");
        Def = lib.Depths.TryGetValue(purpose, out var def) ? def : throw new ArgumentException($"no depth purpose '{purpose}' in depths.txt");
        RuinAge age = hints?.Ruin ?? RuinAge.Fallback;
        Wear = age.Wear;
        Violence = age.Violence;
        Alien = Def.Alien || (fam?.Style ?? "").StartsWith("alien_", StringComparison.Ordinal);
        Culture = CultureOf(fam, tech, rng.Fork("culture"), Alien, hints?.Culture);
    }

    /// <summary>Who built it and how they built: a faction keeps its own habit; the old world's builders roll one per place, by their tech.</summary>
    static string CultureOf(FamilyDef? fam, TechLevel tech, Rng rng, bool alien, int? culture)
    {
        if (alien) return "organic";
        if (fam?.Owner == "gang") return "warren";
        switch (fam?.Faction ?? Faction.None)
        {
            case Faction.Meridian or Faction.Exchange: return "orderly";
            case Faction.Kessari: return "orderly";
            case Faction.Vharn: return "radial";
            case Faction.Hollow: return "organic";
        }
        string[] names = { "orderly", "radial", "linear", "warren" };
        float[] w = tech switch
        {
            TechLevel.Low => new[] { 0.6f, 0.6f, 1.6f, 1.4f },
            TechLevel.High => new[] { 1.5f, 1.5f, 0.8f, 0.4f },
            _ => new[] { 1.2f, 1f, 1f, 0.8f },
        };
        if (culture is int c) rng = new Rng(Hash.Combine((ulong)c, 0xC417UL));
        return names[Math.Max(0, rng.PickWeighted(w.ToList()))];
    }

    /// <summary>How much the builders' habit and tech favour a plan part.</summary>
    float Lean(string part)
    {
        float k = (Culture, part) switch
        {
            ("orderly", "grid") => 2.4f, ("orderly", "ring") => 1.5f, ("orderly", "hall") => 1.2f, ("orderly", "spine") => 0.7f, ("orderly", "hub") => 0.8f,
            ("radial", "hub") => 2.4f, ("radial", "ring") => 1.7f, ("radial", "chamber") => 1.6f, ("radial", "grid") => 0.5f, ("radial", "spine") => 0.6f,
            ("linear", "spine") => 2.2f, ("linear", "gallery") => 2f, ("linear", "hub") => 0.6f, ("linear", "grid") => 0.6f, ("linear", "ring") => 0.7f,
            ("warren", "hub") => 1.4f, ("warren", "spine") => 1.4f, ("warren", "grid") => 0.5f, ("warren", "ring") => 0.5f, ("warren", "hall") => 0.7f,
            _ => 1f,
        };
        k *= (Tech, part) switch
        {
            (TechLevel.Low, "ring" or "grid" or "chamber") => 0.6f,
            (TechLevel.Low, "spine" or "drifts" or "gallery") => 1.4f,
            (TechLevel.High, "ring" or "hub" or "chamber" or "hall") => 1.3f,
            _ => 1f,
        };
        return k;
    }

    // ---- levels --------------------------------------------------------------------------------

    sealed class Level
    {
        public int Index, J, H;
        public string Part = "";
        public string Zone = "";
        public DSpace? Arrival;
        public Dir In = Dir.South;
        public readonly List<DSpace> Spaces = new();
        public readonly List<string> Bands = new();
        public bool Last;
        public override string ToString() => $"level {Index} j{J} {Part} ({Zone})";
    }

    /// <summary>A main way's width in blocks: the purpose's, a block wider at high tech and narrower at low (big facilities 8 m, the grandest 10 m).</summary>
    int MainW => Math.Clamp(R.Between(Def.MainW0, Def.MainW1) + (Tech == TechLevel.High ? 1 : Tech == TechLevel.Low ? -1 : 0), Math.Min(2, Def.MainW0), 5);
    /// <summary>A main way's height: the purpose's, a block more at high tech where the level has the room (6 m, the grandest 8 m).</summary>
    int MainH => R.Between(Def.MainH0, Def.MainH1) + (Tech == TechLevel.High && Def.MainH1 >= 3 ? 1 : 0);
    int SideW => Math.Clamp(R.Between(Def.SideW0, Def.SideW1), 1, 4);
    int SideH => R.Between(Def.SideH0, Def.SideH1);
    int RoomH => R.Between(Def.RoomH0, Def.RoomH1);

    static IEnumerable<string> BandsOf(int index, int count)
    {
        yield return "any";
        if (index == 0) yield return "top";
        if (index > 0 && index < count - 1) yield return "upper";
        if (index >= count / 2 && count > 1) yield return "deep";
        if (index == count - 1) yield return "deepest";
    }

    /// <summary>The whole plan: how deep, which part on each level, the core or the ways down, the history.</summary>
    void Plan()
    {
        int n = R.Between(Def.LevelsMin, Def.LevelsMax);
        if (Rarity >= Rarity.Rare && n < Def.LevelsMax + 1) n++;
        var used = new Dictionary<string, int>();
        bool ops = Def.Feature == "ops";
        int opsLevel = ops ? R.Between(Math.Min(1, n - 1), Math.Max(0, n - 2)) : -1;
        for (int i = 0; i < n; i++)
        {
            var lv = new Level { Index = i, Last = i == n - 1 };
            lv.Bands.AddRange(BandsOf(i, n));
            var options = new List<(string Key, float Weight)>();
            foreach (string band in lv.Bands)
                if (Def.Plans.TryGetValue(band, out var list))
                    foreach (var (k, w) in list) options.Add((k, w * Lean(k) * MathF.Pow(0.4f, used.GetValueOrDefault(k))));
            if (options.Count == 0) options.Add(("spine", 1f));
            lv.Part = i == opsLevel ? "hall" : options[Math.Max(0, R.PickWeighted(options.Select(o => o.Weight).ToList()))].Key;
            used[lv.Part] = used.GetValueOrDefault(lv.Part) + 1;
            lv.H = HeightOf(lv.Part);
            // Zones in order down the levels, the last always the deepest's (a biolab's isolation at the bottom).
            lv.Zone = Def.Zones.Count == 0 ? "" : Def.Zones[i == n - 1 ? Def.Zones.Count - 1 : Math.Min(Def.Zones.Count - 1, (int)MathF.Floor(i * Def.Zones.Count / (float)n))];
            Levels.Add(lv);
        }
        _opsLevel = opsLevel;

        // How the levels hang together: one core straight down, or each level stepping off the one above.
        _stacked = n > 1 && (Def.Span == "shaft" || !Alien && R.Chance(Def.Stacked * (Culture == "orderly" ? 1.5f : Culture == "warren" ? 0.5f : 1f)));
        int pitchRise = 0;
        if (_stacked)
        {
            int need = Levels.Max(l => l.H) + 1;
            _flight = Math.Clamp((need + 1) / 2, 3, 4);
            pitchRise = 2 * _flight;
            foreach (var lv in Levels) lv.H = Math.Min(lv.H, pitchRise - 1);
        }
        int first = R.Between(Def.FirstMin, Def.FirstMax);
        for (int i = 0; i < n; i++)
        {
            var lv = Levels[i];
            if (i == 0) lv.J = _stacked ? -RoundUp(Math.Max(first, lv.H + 3), pitchRise) : -Math.Max(first, lv.H + 3);
            else lv.J = Levels[i - 1].J - (_stacked ? pitchRise : lv.H + 1 + R.Between(Def.RockMin, Def.RockMax));
        }

        Head();
        if (Def.Span is { Length: > 0 } span && R.Chance(Def.SpanChance)) Span(span);
        Descend(Levels[0]);
        if (_stacked) Core();
        foreach (var lv in Levels)
        {
            if (lv.Index > 0 && lv.Arrival == null)
            {
                if (_span != null) SiloDown(Levels[lv.Index - 1], lv);
                else if (!_stacked) Between(Levels[lv.Index - 1], lv);
            }
            if (lv.Arrival == null)
            {
                // A level nothing could reach is left out, and every level under it.
                foreach (var gone in Levels.Skip(lv.Index).ToList()) foreach (var s in gone.Spaces.ToList()) Drop(s, gone);
                Levels.RemoveRange(lv.Index, Levels.Count - lv.Index);
                if (Levels.Count > 0) Levels[^1].Last = true;
                break;
            }
            if (_span == null) ZoneLock(lv);
            Grow(lv);
        }
        // A mine's ore went down chutes from its stopes: ways round the shaft between levels.
        if (Levels.Any(l => l.Part == "drifts"))
            for (int g = 0; g + 1 < Levels.Count; g++)
                if (R.Chance(0.75f)) Chute(Levels[g], Levels[g + 1]);
        // Lesser structure over the main plan: crawlways, dead ends with what they keep, hidden rooms.
        foreach (var lv in Levels.Where(l => l.Arrival != null))
        {
            if (R.Chance(Def.Crawl * (Culture == "warren" ? 1.6f : 1f))) Crawlway(lv);
            int ends = R.Between(Def.EndsMin, Def.EndsMax) + (Culture == "warren" ? 1 : 0);
            for (int e = 0; e < ends; e++) DeadEnd(lv, e == 0 && lv.Last);
        }
        Purposes();
        Secrets();
        History();
    }

    static int RoundUp(int v, int step) => step <= 0 ? v : (v + step - 1) / step * step;

    bool _stacked;
    int _flight = 2, _opsLevel = -1;

    /// <summary>The headroom a level's plan wants: its tallest main space.</summary>
    int HeightOf(string part) => part switch
    {
        "hall" => R.Between(5, 6),
        "chamber" => R.Between(6, 7),
        "cells" => R.Chance(0.5f) ? 5 : Math.Max(3, Def.MainH1),
        "drifts" => 4,
        "organic" => 5,
        _ => Math.Max(3, Def.MainH1) + (Tech == TechLevel.High && Def.MainH1 >= 3 ? 1 : 0),
    };

    // ---- the parts' bookkeeping --------------------------------------------------------------

    DSpace NewSpace(string kind, string purpose, ModuleKind module, int j, int h, Level? lv)
    {
        var s = new DSpace
        {
            Id = S.Count, Kind = kind, Purpose = purpose, Module = module, J = j, H = h, Level = lv?.Index ?? -1, Zone = lv?.Zone ?? "",
            Raw = R.Chance(Def.Raw), Lining = Def.Lining, Part = lv?.Part ?? "",
        };
        return s;
    }

    /// <summary>Takes a drawn space into the plan if it fits, touching only the spaces given.</summary>
    bool Take(DSpace s, Level? lv, params DSpace?[] touch)
    {
        var ids = touch.Where(t => t != null && !t.Dead).Select(t => t!.Id).ToHashSet();
        s.Id = S.Count;
        if (s.Columns == 0 || !G.Fits(s, ids)) return false;
        S.Add(s);
        G.Mark(s);
        lv?.Spaces.Add(s);
        return true;
    }

    /// <summary>Takes a space back out: its blocks free again, its joints gone.</summary>
    void Drop(DSpace s, Level? lv = null)
    {
        if (s.Dead) return;
        s.Dead = true;
        G.Unmark(s);
        lv?.Spaces.Remove(s);
        foreach (var l in Levels) l.Spaces.Remove(s);
        Joints.RemoveAll(j => j.A == s || j.B == s);
    }

    IEnumerable<DSpace> Live => S.Where(s => !s.Dead);

    /// <summary>A box of a space: columns i0..i1 by k0..k1 at floor j, h high.</summary>
    DSpace? Box(string kind, string purpose, ModuleKind module, int i0, int k0, int i1, int k1, int j, int h, Level? lv, params DSpace?[] touch)
    {
        var s = NewSpace(kind, purpose, module, j, h, lv);
        for (int k = Math.Min(k0, k1); k <= Math.Max(k0, k1); k++)
            for (int i = Math.Min(i0, i1); i <= Math.Max(i0, i1); i++) s.Column(i, k);
        return Take(s, lv, touch) ? s : null;
    }

    DJoint Join(DSpace a, DSpace b, string kind = "open")
    {
        var j = new DJoint { A = a, B = b, Kind = kind };
        Joints.Add(j);
        return j;
    }

    static (int dI, int dK) V(Dir d) => d switch { Dir.East => (1, 0), Dir.West => (-1, 0), Dir.South => (0, 1), _ => (0, -1) };
    static Dir Left(Dir d) => d switch { Dir.North => Dir.West, Dir.West => Dir.South, Dir.South => Dir.East, _ => Dir.North };
    static Dir Right(Dir d) => Left(d).Opposite();

    /// <summary>
    /// A door through the wall between two spaces at a floor level they share: a block of wall with interior of
    /// one on each side, walled jambs either side along the wall and a solid head over it. The middle of the
    /// candidates, or the one nearest a wanted spot.
    /// </summary>
    DJoint? Door(DSpace a, DSpace b, string kind = "door", (int I, int K)? near = null, bool reserve = true)
    {
        var spots = DoorSpots(a, b);
        if (spots.Count == 0) return null;
        var spot = near is { } n ? spots.OrderBy(s => Math.Abs(s.I - n.I) + Math.Abs(s.K - n.K)).First() : spots[spots.Count / 2];
        var j = new DJoint { A = a, B = b, Kind = kind, I = spot.I, J = spot.J, K = spot.K, AlongI = spot.AlongI };
        Joints.Add(j);
        if (reserve)
        {
            G.Keep(spot.I, spot.J, spot.K);
            G.Keep(spot.I, spot.J + 1, spot.K);
        }
        return j;
    }

    List<(int I, int J, int K, bool AlongI)> DoorSpots(DSpace a, DSpace b)
    {
        var spots = new List<(int, int, int, bool)>();
        foreach (var (i, k) in a.Foot)
        {
            int fj = a.FloorAt(i, k);
            if (a.HeightAt(i, k) < 1) continue;
            foreach (Dir d in Dirs.Horizontal)
            {
                var (di, dk) = V(d);
                int wi = i + di, wk = k + dk, bi = i + 2 * di, bk = k + 2 * dk;
                if (!b.Has(bi, bk) || b.FloorAt(bi, bk) != fj || a.FloorAt(i, k) != fj) continue;
                if (G.Owner(wi, fj, wk) != -1) continue;
                // Never onto the top of a ladder's shaft (a hole in the floor) either side.
                if (ShaftUnder(i, fj, k) || ShaftUnder(bi, fj, bk)) continue;
                // The head over it, and the jambs either side along the wall: wall or rock, no one's interior.
                if (G.Interior(wi, fj + 1, wk) || G.Owner(wi, fj + 1, wk) == -2) continue;
                int pi = Math.Abs(dk), pk = Math.Abs(di);
                if (G.Interior(wi + pi, fj, wk + pk) || G.Interior(wi - pi, fj, wk - pk)) continue;
                if (G.Owner(wi + pi, fj, wk + pk) == -3 || G.Owner(wi - pi, fj, wk - pk) == -3) continue;
                // Room to stand both sides: not the foot of a flight, not a drop.
                if (a.HeightAt(i, k) < 1 || b.HeightAt(bi, bk) < 1) continue;
                spots.Add((wi, fj, wk, d is Dir.North or Dir.South));
            }
        }
        return spots.Distinct().OrderBy(s => s.Item1).ThenBy(s => s.Item3).ToList();
    }

    /// <summary>Whether a column's floor at j is the top of a shaft or a lift (a hole you can't stand at).</summary>
    bool ShaftUnder(int i, int j, int k) => G.Owner(i, j - 1, k) is int o && o >= 0 && S[o].Kind is "shaft" or "lift";

    /// <summary>Whether a door's block (kept clear) stands beside a column at j.</summary>
    bool BesideDoor(int i, int j, int k) => Dirs.Horizontal.Any(d => { var (di, dk) = V(d); return G.Owner(i + di, j, k + dk) == -3; });
}
