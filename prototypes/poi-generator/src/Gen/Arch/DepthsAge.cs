#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What the years did to the plan, for every state the place is found in: a corridor's roof come down, with a
/// way round dug by whoever came after if there wasn't one; a natural cave the diggers broke into; a squatters'
/// tunnel through the rock between two rooms; a door welded shut, with the section behind it reached another way.
/// How much of it there is goes by the purpose, the world's age and how violently it fell.
/// </summary>
internal sealed partial class Depths
{
    /// <summary>Corridor columns filled by a fall of rock.</summary>
    readonly List<(DSpace S, List<(int I, int K)> Cols)> _collapses = new();
    readonly HashSet<(int Id, int I, int K)> _blocked = new();

    float AgeChance(string kind) => Def.Age.GetValueOrDefault(kind) * kind switch
    {
        "collapse" => (0.35f + Wear) * (1f + Violence),
        "cave" => 0.4f + Wear,
        "squat" => 0.2f + Wear,
        "sealed" => 0.5f + Violence,
        "breach" => (0.4f + Wear) * (1f + 0.5f * Violence),
        _ => 1f,
    };

    void History()
    {
        if (Levels.Count == 0) return;
        int collapses = (R.Chance(AgeChance("collapse")) ? 1 : 0) + (R.Chance(AgeChance("collapse") * 0.4f) ? 1 : 0);
        for (int n = 0; n < collapses; n++) Collapse();
        if (R.Chance(AgeChance("cave"))) Cave();
        if (R.Chance(AgeChance("squat"))) Squat(R.Pick(Levels));
        if (R.Chance(AgeChance("sealed"))) Seal();
        if (_whole && R.Chance(AgeChance("breach"))) SurfaceBreach();
    }

    bool _whole;
    /// <summary>A breach from the surface: the room it falls into, the rubble slope, where you stand at the lip.</summary>
    (DSpace Room, DSpace Slope, int I, int K)? _breach;

    /// <summary>
    /// The ground and a roof gave way over the top level: a slope of rubble from the surface down into a room or
    /// a corridor, daylight and weather falling in, another way in for whoever finds it.
    /// </summary>
    void SurfaceBreach()
    {
        var lv = Levels[0];
        int rise = -lv.J;
        var (hi0, hk0, hi1, hk1, _) = _head;
        var hosts = lv.Spaces.Where(s => !s.Dead && !s.Raw && !s.Secret && s != _vaultSpace && s.Kind is "room" or "hall" or "corridor" or "gallery" or "hub" or "ring").OrderBy(_ => R.Float()).Take(12).ToList();
        foreach (var host in hosts)
            foreach (Dir d in Dirs.Horizontal.OrderBy(_ => R.Float()))
            {
                var face = Face(host, d, lv.J);
                if (face.Count < 2) continue;
                var c = face[face.Count / 2 - 1];
                var (di, dk) = V(d);
                int i0 = c.I + di, k0 = c.K + dk;
                // Its lip on open ground, well clear of the head and the way up to it.
                int ti = i0 + rise * di, tk = k0 + rise * dk;
                if (ti >= hi0 - 4 && ti <= hi1 + 4 && tk >= hk0 - 8 && tk <= hk1 + 4) continue;
                var run = RunSpace("ramp", i0, k0, d, lv.J, rise, 2, lv);
                run.Surface = true;
                run.Raw = true;
                run.Kind = "breach";
                run.Purpose = "breach";
                if (!Take(run, lv, host)) continue;
                Join(host, run);
                _breach = (host, run, ti, tk);
                return;
            }
    }

    // ---- can it still be walked? ------------------------------------------------------------------

    /// <summary>
    /// Whether every live, unhidden space can still be walked to from the way in: column by column within a space
    /// (a step up or down of one block at most, a stair's), across the joints that are walked (open, doors,
    /// hatches, breaks), up and down shafts; the blocked columns and the joints left out are not.
    /// </summary>
    bool AllReached(HashSet<(int Id, int I, int K)> blocked, DJoint? without = null)
    {
        var start = _top ?? Levels[0].Arrival;
        if (start == null) return false;
        var byId = Live.ToDictionary(s => s.Id);
        var seen = new HashSet<(int, int, int)>();
        var q = new Queue<(int Id, int I, int K)>();
        foreach (var (i, k) in start.Foot)
            if (!blocked.Contains((start.Id, i, k)) && seen.Add((start.Id, i, k))) q.Enqueue((start.Id, i, k));
        // Joints by space.
        var joints = new Dictionary<int, List<DJoint>>();
        foreach (var j in Joints)
        {
            if (j == without || j.Kind is "secret" or "sealed") continue;
            (joints.TryGetValue(j.A.Id, out var la) ? la : joints[j.A.Id] = new List<DJoint>()).Add(j);
            (joints.TryGetValue(j.B.Id, out var lb) ? lb : joints[j.B.Id] = new List<DJoint>()).Add(j);
        }
        void Visit(DSpace s, int i, int k)
        {
            if (!s.Has(i, k) || blocked.Contains((s.Id, i, k)) || !seen.Add((s.Id, i, k))) return;
            q.Enqueue((s.Id, i, k));
        }
        while (q.Count > 0)
        {
            var (id, i, k) = q.Dequeue();
            var s = byId[id];
            int f = s.FloorAt(i, k);
            foreach (Dir d in Dirs.Horizontal)
            {
                var (di, dk) = V(d);
                if (s.Has(i + di, k + dk) && Math.Abs(s.FloorAt(i + di, k + dk) - f) <= 1) Visit(s, i + di, k + dk);
            }
            if (!joints.TryGetValue(id, out var js)) continue;
            foreach (var j in js)
            {
                var o = j.A == s ? j.B : j.A;
                if (o.Dead) continue;
                if (j.Kind == "open")
                {
                    // Up or down a shaft: the same column over or under; otherwise a step across at about the same floor.
                    if ((s.Kind is "shaft" or "lift" || o.Kind is "shaft" or "lift") && o.Has(i, k)) Visit(o, i, k);
                    foreach (Dir d in Dirs.Horizontal)
                    {
                        var (di, dk) = V(d);
                        if (o.Has(i + di, k + dk) && Math.Abs(o.FloorAt(i + di, k + dk) - f) <= 1) Visit(o, i + di, k + dk);
                    }
                }
                else
                {
                    // Through the wall block: from the column on this side to the one on the other.
                    foreach (Dir d in Dirs.Horizontal)
                    {
                        var (di, dk) = V(d);
                        if (i + di == j.I && k + dk == j.K && f == j.J) Visit(o, j.I + di, j.K + dk);
                    }
                }
            }
        }
        var reachedIds = new HashSet<int>(seen.Select(x => x.Item1));
        // The silo's shaft is reached from its galleries: a door onto it counts.
        return Live.Where(s => !s.Secret).All(s => reachedIds.Contains(s.Id) || s == _span && Joints.Any(j => j.B == _span && reachedIds.Contains(j.A.Id)));
    }

    // ---- a fall of rock ---------------------------------------------------------------------------

    /// <summary>A corridor's roof come down across it: if no way round remains, whoever came after dug one through the rock beside it.</summary>
    void Collapse()
    {
        var candidates = Live.Where(s => s.Kind is "corridor" or "gallery" or "ring" or "tunnel" or "drift" && !s.Secret && s.Columns >= 10 && s != _top).OrderBy(_ => R.Float()).ToList();
        foreach (var c in candidates)
        {
            bool alongI = c.W >= c.D;
            int lo = alongI ? c.I0 : c.K0, hi = alongI ? c.I1 : c.K1;
            if (hi - lo < 7) continue;
            int p = R.Between(lo + (hi - lo) / 3, hi - (hi - lo) / 3 - 1);
            var cut = c.Foot.Where(x => (alongI ? x.I : x.K) is var a && (a == p || a == p + 1)).ToList();
            if (cut.Count == 0 || cut.Any(x => c.FloorAt(x.I, x.K) != c.J)) continue;
            // No door may open onto the fall.
            if (Joints.Any(j => (j.A == c || j.B == c) && j.Kind != "open" && cut.Any(x => Math.Abs(x.I - j.I) + Math.Abs(x.K - j.K) <= 2))) continue;
            var blocked = new HashSet<(int, int, int)>(_blocked);
            foreach (var (i, k) in cut) blocked.Add((c.Id, i, k));
            if (!AllReached(blocked) && !Bypass(c, alongI, p, blocked)) continue;
            foreach (var x in blocked) _blocked.Add(x);
            _collapses.Add((c, cut));
            return;
        }
    }

    /// <summary>A rough tunnel dug round a fall: out of the corridor before it, along beside it through the rock, back in after it.</summary>
    bool Bypass(DSpace c, bool alongI, int p, HashSet<(int, int, int)> blocked)
    {
        var lv = Levels.FirstOrDefault(l => l.Index == c.Level);
        foreach (int side in new[] { 1, -1 }.OrderBy(_ => R.Float()))
        {
            int edge = side > 0 ? (alongI ? c.K1 : c.I1) : (alongI ? c.K0 : c.I0);
            var s = NewSpace("tunnel", "tunnel", ModuleKind.Corridor, c.J, 2, lv);
            s.Raw = true;
            int a0 = p - 3, a1 = p + 4;
            // The two stubs out of the corridor, then the leg along beside it.
            if (!c.Has(alongI ? a0 : edge, alongI ? edge : a0) || !c.Has(alongI ? a1 : edge, alongI ? edge : a1)) continue;
            foreach (int a in new[] { a0, a1 }) s.Column(alongI ? a : edge + side, alongI ? edge + side : a);
            for (int a = a0; a <= a1; a++) s.Column(alongI ? a : edge + 2 * side, alongI ? edge + 2 * side : a);
            if (!Take(s, lv, c)) continue;
            Join(c, s);
            if (AllReached(blocked)) { _stories.Add(("dug-round", s)); return true; }
            Drop(s, lv);
        }
        return false;
    }

    readonly List<(string Kind, DSpace S)> _stories = new();

    // ---- a cave broken into -------------------------------------------------------------------------

    /// <summary>The diggers broke into a natural cave: a ragged chamber of raw rock through a smashed wall, sometimes a second beyond it.</summary>
    void Cave()
    {
        var hosts = Live.Where(s => !s.Raw && !s.Secret && s.Kind is "room" or "hall" or "corridor" or "gallery" && s.Level >= 0)
            .OrderByDescending(s => s.Level).ThenBy(_ => R.Float()).Take(8).ToList();
        foreach (var host in hosts)
        {
            var lv = Levels.FirstOrDefault(l => l.Index == host.Level);
            if (lv == null) continue;
            foreach (Dir d in Dirs.Horizontal.OrderBy(_ => R.Float()))
            {
                var face = Face(host, d, host.J);
                if (face.Count < 2) continue;
                var mid = face[face.Count / 2];
                var (di, dk) = V(d);
                int r = R.Between(4, 6);
                var cave = NewSpace("cave", "cavern", ModuleKind.Cave, host.J, 4, lv);
                cave.Raw = true;
                float ci = mid.I + (r + 2) * di + 0.5f, ck = mid.K + (r + 2) * dk + 0.5f, phase = R.Float(0f, 6.28f);
                for (int k = (int)ck - r - 2; k <= (int)ck + r + 2; k++)
                    for (int i = (int)ci - r - 2; i <= (int)ci + r + 2; i++)
                    {
                        float x = i + 0.5f - ci, z = k + 0.5f - ck, ang = MathF.Atan2(z, x), dist = MathF.Sqrt(x * x + z * z);
                        float edge = r + 0.8f * MathF.Sin(ang * 2f + phase) + 0.6f * MathF.Sin(ang * 5f - phase * 2f);
                        if (dist > edge) continue;
                        int h = Math.Clamp((int)MathF.Round(2 + 3 * (1 - dist / edge)) + (int)(Hash.Cell(new Int3(i, 1, k), 31) % 2), 2, 5);
                        cave.Column(i, k, host.J, h);
                    }
                if (!cave.Has(mid.I + 2 * di, mid.K + 2 * dk) || !Take(cave, lv)) continue;
                if (Door(host, cave, "broken", (mid.I + di, mid.K + dk)) == null) { Drop(cave, lv); continue; }
                _stories.Add(("cave", cave));
                // Sometimes the cave runs on: a second chamber down a natural passage.
                if (R.Chance(0.5f))
                {
                    float ang = R.Float(0f, 6.28f);
                    float c2i = ci + MathF.Cos(ang) * (r + 8), c2k = ck + MathF.Sin(ang) * (r + 8);
                    var two = Blob(lv, c2i, c2k, R.Float(3f, 5f), R.Float(3f, 5f), R.Float(0f, 3f));
                    if (two != null)
                    {
                        two.Raw = true;
                        two.Kind = "cave";
                        two.Purpose = "cavern";
                        two.Module = ModuleKind.Cave;
                        if (!CurvedWay(lv, cave, (ci, ck), two, (c2i, c2k))) Drop(two, lv);
                        else foreach (var j in Joints.Where(j => j.B == two || j.A == two)) { if (j.A.Kind == "tunnel") j.A.Raw = true; if (j.B.Kind == "tunnel") j.B.Raw = true; }
                    }
                }
                return;
            }
        }
    }

    // ---- squatters --------------------------------------------------------------------------------

    /// <summary>A rough tunnel squatters dug through the rock between two rooms that never met, its ends smashed through the walls.</summary>
    void Squat(Level lv)
    {
        var spaces = lv.Spaces.Where(s => !s.Dead && !s.Secret && s.Kind is "room" or "corridor" or "hall" or "gallery" or "hub" or "ring").ToList();
        for (int tries = 0; tries < 12 && spaces.Count >= 2; tries++)
        {
            var a = R.Pick(spaces);
            var b = spaces.Where(s => s != a && !Joints.Any(j => j.A == a && j.B == s || j.A == s && j.B == a))
                .OrderBy(s => Math.Abs(s.Mid.I - a.Mid.I) + Math.Abs(s.Mid.K - a.Mid.K)).FirstOrDefault();
            if (b == null) continue;
            float dx = b.Mid.I - a.Mid.I, dz = b.Mid.K - a.Mid.K;
            Dir first = MathF.Abs(dx) > MathF.Abs(dz) ? (dx > 0 ? Dir.East : Dir.West) : (dz > 0 ? Dir.South : Dir.North);
            var face = Face(a, first, lv.J);
            if (face.Count == 0) continue;
            var start = face[face.Count / 2];
            var (di, dk) = V(first);
            var s = NewSpace("tunnel", "tunnel", ModuleKind.Corridor, lv.J, 2, lv);
            s.Raw = true;
            int i = start.I + 2 * di, k = start.K + 2 * dk;
            bool ok = false;
            for (int n = 0; n < 30; n++)
            {
                s.Column(i, k);
                if (n >= 1 && Dirs.Horizontal.Any(dd => { var (ei, ek) = V(dd); return b.Has(i + 2 * ei, k + 2 * ek) && b.FloorAt(i + 2 * ei, k + 2 * ek) == lv.J; })) { ok = true; break; }
                int ti = (int)MathF.Round(b.Mid.I), tk = (int)MathF.Round(b.Mid.K);
                bool along = di != 0 ? Math.Abs(i - ti) > b.W / 2 + 1 : Math.Abs(k - tk) > b.D / 2 + 1;
                if (along) { i += di; k += dk; }
                else if (di != 0) k += Math.Sign(tk - k);
                else i += Math.Sign(ti - i);
                if (s.Has(i, k)) break;
            }
            if (!ok || s.Columns < 2 || !Take(s, lv)) continue;
            if (Door(a, s, "broken") == null || Door(s, b, "broken") == null) { Drop(s, lv); continue; }
            _stories.Add(("squat", s));
            return;
        }
    }

    // ---- a section sealed --------------------------------------------------------------------------

    /// <summary>A door welded shut, so long as what's behind it is still reached another way; if nothing reaches it, squatters dug one first.</summary>
    void Seal()
    {
        var doors = Joints.Where(j => j.Kind == "door" && !j.A.Secret && !j.B.Secret && j.B != _vaultSpace && j.A != _vaultSpace).OrderBy(_ => R.Float()).ToList();
        foreach (var j in doors.Take(10))
        {
            if (!AllReached(_blocked, without: j))
            {
                // A way round, then try again.
                var lv = Levels.FirstOrDefault(l => l.Index == j.B.Level);
                if (lv == null) continue;
                int before = Joints.Count;
                Squat(lv);
                if (Joints.Count == before || !AllReached(_blocked, without: j)) continue;
            }
            j.Kind = "sealed";
            return;
        }
    }
}
