#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>The ways in and down: what stands on the surface, how you get to the first level, and from level to level.</summary>
internal sealed partial class Depths
{
    string _headKind = "hood", _downKind = "straight";
    /// <summary>A ladder to build: its shaft column, which way its wall lies, and the walk levels it joins (feet at the bottom, step off at the top).</summary>
    readonly List<(int I, int K, Dir Back, int Bottom, int Top)> _ladders = new();
    /// <summary>A lift: its shaft column and the spaces it stops beside, one per stop.</summary>
    readonly List<(DSpace Shaft, List<DSpace> Stops)> _lifts = new();
    /// <summary>Straight flights and ramps inside a hall or chamber (not spaces of their own).</summary>
    readonly List<DSpace> _runs = new();
    /// <summary>Gallery decks to lay: the gallery space and the side its rail runs on.</summary>
    readonly List<DSpace> _galleries = new();
    DSpace? _span;
    string _spanKind = "";
    int _spanCi, _spanCk, _spanR;

    // ---- the head ---------------------------------------------------------------------------

    void Head()
    {
        _headKind = Def.Head.Count == 0 ? "hood" : Def.Head[Math.Max(0, R.PickWeighted(Def.Head.Select(h => h.Weight).ToList()))].Key;
        var downs = Def.Down.Where(d => d.Key != "lift" || Tech != TechLevel.Low).ToList();
        _downKind = downs.Count == 0 ? "straight" : downs[Math.Max(0, R.PickWeighted(downs.Select(d => d.Weight).ToList()))].Key;
        if (_headKind == "headframe" && _downKind is "straight" or "ramp") _downKind = "ladder";
        // Wide enough for its way down, deep enough to roof the top of a stair.
        int w = _downKind == "ramp" ? R.Between(11, 13) : _headKind is "hatch" ? R.Between(5, 6) : R.Between(7, 10);
        int d = _downKind is "straight" or "ramp" ? R.Between(8, 10) : _headKind == "hatch" ? R.Between(5, 6) : R.Between(6, 8);
        int h = _headKind is "hatch" ? 2 : 3;
        int i0 = -w / 2;
        _head = (i0, 0, i0 + w - 1, d - 1, h);
    }

    /// <summary>The way from the head down to the first level: a long stair or ramp, or a ladder.</summary>
    void Descend(Level lv)
    {
        var (hi0, hk0, hi1, hk1, _) = _head;
        int rise = -lv.J;
        if (_downKind is "straight" or "ramp")
        {
            int width = _downKind == "ramp" ? Math.Min(hi1 - hi0 - 3, R.Between(3, 4)) : 2;
            int iS = _downKind == "ramp" ? hi0 + 2 : R.Chance(0.5f) ? hi0 + 2 : hi1 - 1 - width;
            if (iS < hi0 + 1) iS = hi0 + 1;
            int kTop = hk0 + 2;
            int kS = kTop + rise;
            var run = RunSpace(_downKind == "ramp" ? "ramp" : "stair", iS, kS, Dir.North, lv.J, rise, width, lv);
            run.Surface = true;
            run.Raw = false;
            if (!Take(run, null)) { Fallback(lv); return; }
            _top = run;
            var landing = Box("landing", "landing", ModuleKind.Corridor, iS - 1, kS + 1, iS + width, kS + 2, lv.J, 3, lv, run);
            if (landing == null) { Fallback(lv); return; }
            landing.Raw = false;
            Join(run, landing);
            lv.Arrival = landing;
            lv.In = Dir.South;
            return;
        }
        // A ladder down from the head's back corner into a small landing.
        int li = R.Chance(0.5f) ? hi0 + 1 : hi1 - 1, lk = hk1 - 1;
        var land = Box("landing", "landing", ModuleKind.Corridor, li - 1, lk - 2, li + 1, lk, lv.J, 3, lv);
        if (land == null) { Fallback(lv); return; }
        land.Raw = false;
        var shaft = NewSpace("shaft", "ladderway", ModuleKind.Stair, lv.J + 3, -1 - (lv.J + 3) + 1, lv);
        shaft.Surface = true;
        shaft.Raw = false;
        shaft.Column(li, lk);
        if (shaft.H < 1 || !Take(shaft, null, land)) { Fallback(lv); return; }
        _top = shaft;
        Join(shaft, land);
        _ladders.Add((li, lk, Dir.South, lv.J, 0));
        lv.Arrival = land;
        lv.In = Dir.North;
    }

    /// <summary>A plan that can't seat its way down is thrown away whole: the architect rolls it again.</summary>
    static void Fallback(Level lv) => throw new InvalidOperationException($"the way down to {lv} would not fit");

    /// <summary>A straight run of steps (or a ramp) as a space: its foot column (i, k) at level j, rising toward d by rise, width lanes beside it.</summary>
    DSpace RunSpace(string kind, int i, int k, Dir d, int j, int rise, int width, Level? lv)
    {
        var s = NewSpace(kind, kind == "ramp" ? "ramp" : "stair", ModuleKind.Stair, j, 3, lv);
        var (di, dk) = V(d);
        int li = d is Dir.North or Dir.South ? 1 : 0, lk = 1 - li;
        for (int t = 0; t < rise; t++)
            for (int w = 0; w < width; w++) s.Column(i + t * di + w * li, k + t * dk + w * lk, j + t, 3);
        s.Run = (i, k, d, rise, width);
        s.Ramp = kind == "ramp";
        return s;
    }

    // ---- between levels ----------------------------------------------------------------------

    /// <summary>
    /// The way from one level down to the next when they don't share a core: a long stair or ramp stepping off
    /// a level's corridor or hall, or a ladder down through its floor, to a landing the next level grows from.
    /// </summary>
    void Between(Level up, Level down)
    {
        var ways = Def.Between.Where(b => b.Key != "lift" && (b.Key != "ramp" || Alien || Def.Raw > 0.5f || R.Chance(0.4f))).ToList();
        if (ways.Count == 0) ways.Add(("straight", 1f));
        int rise = up.J - down.J;
        var hosts = up.Spaces.Where(s => s.Kind is "corridor" or "hub" or "hall" or "landing" or "gallery" or "drift" or "chamber" or "tunnel" or "ring" && s.Columns > 0 && !s.Secret)
            .OrderBy(_ => R.Float()).ToList();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string way = ways[Math.Max(0, R.PickWeighted(ways.Select(w => w.Weight).ToList()))].Key;
            if (Alien && way == "straight") way = "ramp";
            foreach (var host in hosts)
            {
                if (way == "ladder" ? LadderDown(up, down, host) : StairDown(up, down, host, way == "ramp", rise)) return;
            }
        }
        // Last resort: any host, any way that fits.
        foreach (var host in hosts)
            if (StairDown(up, down, host, Alien, rise) || LadderDown(up, down, host)) return;
    }

    /// <summary>A straight flight (or ramp) from a host's edge down to a landing on the level below.</summary>
    bool StairDown(Level up, Level down, DSpace host, bool ramp, int rise)
    {
        int width = ramp ? 3 : 2;
        var edges = new List<(int I, int K, Dir D)>();
        foreach (var (i, k) in host.Foot)
        {
            if (host.FloorAt(i, k) != up.J || host.HeightAt(i, k) < 2) continue;
            foreach (Dir d in Dirs.Horizontal)
            {
                var (di, dk) = V(d);
                if (host.Has(i + di, k + dk)) continue;
                int li = d is Dir.North or Dir.South ? 1 : 0, lk = 1 - li;
                bool all = true;
                for (int w = 1; w < width && all; w++)
                    all = host.Has(i + w * li, k + w * lk) && host.FloorAt(i + w * li, k + w * lk) == up.J && !host.Has(i + w * li + di, k + w * lk + dk);
                if (all) edges.Add((i, k, d));
            }
        }
        foreach (var (i, k, d) in edges.OrderBy(_ => R.Float()).Take(14))
        {
            var (di, dk) = V(d);
            int i0 = i + rise * di, k0 = k + rise * dk;
            var run = RunSpace(ramp ? "ramp" : "stair", i0, k0, d.Opposite(), down.J, rise, width, down);
            run.Raw = host.Raw && Def.Raw > 0.5f;
            if (!Take(run, null, host)) continue;
            int li = d is Dir.North or Dir.South ? 1 : 0, lk = 1 - li;
            int fi = i0 + di, fk = k0 + dk;
            var landing = NewSpace("landing", "landing", ModuleKind.Corridor, down.J, 3, down);
            landing.Raw = run.Raw;
            for (int t = 0; t < 2; t++)
                for (int w = -1; w <= width; w++) landing.Column(fi + t * di + w * li, fk + t * dk + w * lk);
            if (!Take(landing, down, run))
            {
                Drop(run, down);
                continue;
            }
            Join(host, run);
            Join(run, landing);
            down.Arrival = landing;
            down.In = d;
            return true;
        }
        return false;
    }

    /// <summary>A ladder down through a host's floor, against its wall, into a small landing below.</summary>
    bool LadderDown(Level up, Level down, DSpace host)
    {
        var spots = new List<(int I, int K, Dir D)>();
        foreach (var (i, k) in host.Foot)
        {
            if (host.FloorAt(i, k) != up.J) continue;
            foreach (Dir d in Dirs.Horizontal)
            {
                var (di, dk) = V(d);
                // Against a wall, with floor either side along it to step off onto.
                if (host.Has(i + di, k + dk)) continue;
                int pi = Math.Abs(dk), pk = Math.Abs(di);
                if (!host.Has(i + pi, k + pk) && !host.Has(i - pi, k - pk)) continue;
                if (!host.Has(i - di, k - dk)) continue;
                spots.Add((i, k, d));
            }
        }
        foreach (var (i, k, d) in spots.OrderBy(_ => R.Float()).Take(12))
        {
            var (di, dk) = V(d);
            int pi = Math.Abs(dk), pk = Math.Abs(di);
            var landing = NewSpace("landing", "landing", ModuleKind.Corridor, down.J, 3, down);
            landing.Raw = host.Raw && Def.Raw > 0.5f;
            for (int t = 0; t < 3; t++)
                for (int w = -1; w <= 1; w++) landing.Column(i - t * di + w * pi, k - t * dk + w * pk);
            if (!Take(landing, down)) continue;
            var shaft = NewSpace("shaft", "ladderway", ModuleKind.Stair, down.J + 3, up.J - (down.J + 3), down);
            shaft.Raw = landing.Raw;
            shaft.Column(i, k);
            if (shaft.H < 1 || !Take(shaft, null, landing, host))
            {
                Drop(landing, down);
                continue;
            }
            Join(host, shaft);
            Join(shaft, landing);
            _ladders.Add((i, k, d, down.J, up.J));
            down.Arrival = landing;
            down.In = d.Opposite();
            return true;
        }
        return false;
    }

    // ---- one core through every level -------------------------------------------------------

    /// <summary>
    /// Levels stacked on one core: a switchback stairwell (two lanes, a landing at each turn) or a ladderway,
    /// with a lift beside it at higher tech, the same footprint level after level. Each level's arrival is the
    /// core's landing on it. The core hangs off the first level's landing.
    /// </summary>
    void Core()
    {
        var first = Levels[0];
        var a = first.Arrival!;
        bool shaft = Def.Span == "shaft" || Tech == TechLevel.Low && R.Chance(0.5f);
        bool lift = Tech != TechLevel.Low && R.Chance(Def.Lift * (Tech == TechLevel.High ? 1.5f : 1f)) || Def.Span == "shaft" && Tech != TechLevel.Low;
        int r = _flight;
        // The core's start landing is the first level's arrival, widened to five lanes; its run heads on from it.
        Dir run = first.In;
        var (di, dk) = V(run);
        int li = run is Dir.North or Dir.South ? 1 : 0, lk = 1 - li;
        // The landing's columns along its far edge.
        var far = a.Foot.Where(c => !a.Has(c.I + di, c.K + dk)).OrderBy(c => c.I * li + c.K * lk).ToList();
        if (far.Count == 0) return;
        var origin = far[0];
        int ci = origin.I, ck = origin.K;
        Dir grow = DirOf(-li, -lk);
        // A landing per level, all on one footprint: two deep, five lanes.
        DSpace Landing(Level lv, int j)
        {
            var s = NewSpace("landing", "landing", ModuleKind.Corridor, j, 3, lv);
            s.Raw = false;
            for (int t = 1; t <= 2; t++)
                for (int w = 0; w < 5; w++) s.Column(ci + t * di + w * li, ck + t * dk + w * lk);
            return s;
        }
        var landings = new List<DSpace?>();
        var top = Landing(first, first.J);
        if (!Take(top, first, a)) return;
        Join(a, top);
        landings.Add(top);
        for (int n = 1; n < Levels.Count; n++)
        {
            var lv = Levels[n];
            var s = Landing(lv, lv.J);
            landings.Add(Take(s, lv) ? s : null);
            if (landings[^1] == null) break;
        }
        // The ways between landings.
        for (int n = 1; n < landings.Count; n++)
        {
            var lower = landings[n];
            var upper = landings[n - 1];
            if (lower == null || upper == null) break;
            var lv = Levels[n];
            bool ok;
            if (shaft)
            {
                // A ladder per gap, on alternate lanes so each one ends at its landing.
                int lane = n % 2 == 0 ? 0 : 4;
                int i = ci + 2 * di + lane * li, k = ck + 2 * dk + lane * lk;
                var sh = NewSpace("shaft", "ladderway", ModuleKind.Stair, lv.J + 3, upper.J - (lv.J + 3), lv);
                sh.Raw = false;
                sh.Column(i, k);
                ok = sh.H >= 1 && Take(sh, null, lower, upper);
                if (ok)
                {
                    Join(upper, sh);
                    Join(sh, lower);
                    _ladders.Add((i, k, run, lv.J, upper.J));
                }
            }
            else ok = Switchback(lv, lower, upper, ci, ck, run, r);
            if (!ok)
            {
                for (int m = n; m < landings.Count; m++)
                    if (landings[m] is { } dead) Drop(dead, Levels[m]);
                landings.RemoveRange(n, landings.Count - n);
                break;
            }
            lv.Arrival = lower;
            lv.In = grow;
        }
        _coreLandings = landings.Where(l => l != null).Cast<DSpace>().ToList();
        // The first level grows from its core landing too, not the bare foot of the way down: away from the lift's side.
        first.Arrival = top;
        first.In = grow;
        // A lift beside the core: a shaft beyond the fifth lane with a door onto every landing.
        if (lift && _coreLandings.Count >= 2)
        {
            int si = ci + 1 * di + 6 * li, sk = ck + 1 * dk + 6 * lk;
            int bottom = _coreLandings[^1].J, topJ = _coreLandings[0].J;
            var sh = NewSpace("lift", "lift", ModuleKind.Lift, bottom, topJ - bottom + 3, null);
            sh.Raw = false;
            sh.Column(si, sk);
            if (Take(sh, null)) _lifts.Add((sh, _coreLandings.ToList()));
        }
    }

    List<DSpace> _coreLandings = new();

    /// <summary>Two flights between two landings stacked one over the other: up a lane to a turning landing, back down the other lane.</summary>
    bool Switchback(Level lv, DSpace lower, DSpace upper, int ci, int ck, Dir run, int r)
    {
        var (di, dk) = V(run);
        int li = run is Dir.North or Dir.South ? 1 : 0, lk = 1 - li;
        int jl = lower.J;
        // Lane A rises away from the landings, lane B back toward them; the turn is r + 3 blocks out.
        int ai = ci + 3 * di, ak = ck + 3 * dk;
        var f1 = RunSpace("stair", ai, ak, run, jl, r, 2, lv);
        f1.Raw = false;
        var turn = NewSpace("landing", "turn", ModuleKind.Corridor, jl + r, 3, lv);
        turn.Raw = false;
        for (int t = 0; t < 2; t++)
            for (int w = 0; w < 5; w++) turn.Column(ci + (3 + r + t) * di + w * li, ck + (3 + r + t) * dk + w * lk);
        int bi = ci + (2 + r) * di + 3 * li, bk = ck + (2 + r) * dk + 3 * lk;
        var f2 = RunSpace("stair", bi, bk, run.Opposite(), jl + r, r, 2, lv);
        f2.Raw = false;
        var mine = new List<DSpace>();
        bool Try(DSpace s, params DSpace?[] t) { if (!Take(s, null, t)) return false; mine.Add(s); return true; }
        if (Try(f1, lower, turn, f2) && Try(turn, f1, f2) && Try(f2, turn, upper, f1, lower))
        {
            Join(lower, f1);
            Join(f1, turn);
            Join(turn, f2);
            Join(f2, upper);
            return true;
        }
        foreach (var s in mine) Drop(s);
        return false;
    }

    // ---- through every level ---------------------------------------------------------------

    /// <summary>
    /// A structure through every level, placed before the levels grow round it: a silo's round shaft from the
    /// lowest floor to just under the surface, a gallery ring inside it at every level.
    /// </summary>
    void Span(string kind)
    {
        if (kind != "chamber") return;
        int bottom = Levels[^1].J, topJ = DGrid.Cover;
        int r = R.Between(4, 5);
        // Away from the head and its way down, so the levels can wrap round it.
        int ci = R.Between(-4, 4), ck = _head.K1 + Math.Max(8, -Levels[0].J) + r + R.Between(4, 8);
        var s = NewSpace("chamber", "silo", ModuleKind.Hall, bottom, topJ - bottom + 1, null);
        s.Raw = false;
        for (int k = ck - r; k <= ck + r; k++)
            for (int i = ci - r; i <= ci + r; i++)
                if (Plot.In(ci + 0.5, ck + 0.5, r + 0.5, i, k)) s.Column(i, k, bottom, topJ - bottom + 1);
        if (!Take(s, null)) return;
        _span = s;
        _spanKind = kind;
        _spanCi = ci;
        _spanCk = ck;
        _spanR = r;
    }

    // ---- zones -----------------------------------------------------------------------------

    /// <summary>
    /// Where a level starts a new zone, its way in passes a lock first: an airlock (one chamber, a door each end),
    /// a decontamination chain (two or three chambers in a row), or a blast vestibule. The level grows past it.
    /// </summary>
    void ZoneLock(Level lv)
    {
        if (lv.Index == 0 && !(Def.Zones.Count > 0 && Def.Lock is "decon" or "airlock" && R.Chance(0.6f))) return;
        if (lv.Index > 0 && Levels[lv.Index - 1].Zone == lv.Zone && !R.Chance(Def.Blast * 0.6f)) return;
        string kind = lv.Index > 0 && Levels[lv.Index - 1].Zone == lv.Zone ? "blast" : Def.Lock;
        if (kind == "door") return;
        int chambers = kind == "decon" ? R.Between(2, 3) : 1;
        var a = lv.Arrival!;
        Dir d = lv.In;
        var (di, dk) = V(d);
        int li = Math.Abs(dk), lk = Math.Abs(di);
        // From the middle of the arrival's far face.
        var face = a.Foot.Where(c => !a.Has(c.I + di, c.K + dk) && a.FloorAt(c.I, c.K) == lv.J).OrderBy(c => c.I * li + c.K * lk).ToList();
        if (face.Count == 0) return;
        var mid = face[face.Count / 2];
        DSpace prev = a;
        var chain = new List<DSpace>();
        int at = 2;
        for (int n = 0; n < chambers; n++)
        {
            int depth = 2;
            var c = NewSpace("airlock", kind == "decon" ? (n == chambers - 1 ? "airlock" : "decon") : kind == "blast" ? "airlock" : "airlock", ModuleKind.Room, lv.J, 2, lv);
            c.Raw = false;
            for (int t = 0; t < depth; t++)
                for (int w = -1; w <= 1; w++) c.Column(mid.I + (at + t) * di + w * li, mid.K + (at + t) * dk + w * lk);
            if (!Take(c, lv)) break;
            var door = Door(prev, c, kind == "blast" ? "blast" : "hazard", (mid.I + (at - 1) * di, mid.K + (at - 1) * dk));
            if (door == null) { Drop(c, lv); break; }
            chain.Add(c);
            prev = c;
            at += depth + 1;
        }
        if (chain.Count == 0) return;
        // The far side of the lock: a short vestibule the level grows from.
        var v = NewSpace("landing", "vestibule", ModuleKind.Corridor, lv.J, 3, lv);
        v.Raw = false;
        for (int t = 0; t < 2; t++)
            for (int w = -1; w <= 1; w++) v.Column(mid.I + (at + t) * di + w * li, mid.K + (at + t) * dk + w * lk);
        if (!Take(v, lv) || Door(prev, v, kind == "blast" ? "blast" : "hazard", (mid.I + (at - 1) * di, mid.K + (at - 1) * dk)) == null)
        {
            // A lock with nothing past it isn't a lock: take the chain back out.
            if (!v.Dead && S.Contains(v)) Drop(v, lv);
            foreach (var c in chain) Drop(c, lv);
            return;
        }
        lv.Arrival = v;
        _locks.Add((lv.Index, kind));
    }

    readonly List<(int Level, string Kind)> _locks = new();
}
