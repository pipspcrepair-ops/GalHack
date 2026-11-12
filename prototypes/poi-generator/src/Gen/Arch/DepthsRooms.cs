#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>What the rooms are for, and the lesser structure over the main plan: dead ends with what they keep, service crawlways, hidden rooms.</summary>
internal sealed partial class Depths
{
    DSpace? _vaultSpace;

    static bool Walkway(DSpace s) => s.Kind is "corridor" or "gallery" or "hub" or "ring" or "drift" or "tunnel" or "landing" or "hall" or "chamber" or "stope";

    // ---- purposes ------------------------------------------------------------------------------

    /// <summary>
    /// The rooms each level asks for (its bands' demands, musts first) go to the rooms its part made, by size and
    /// by where they sit: the vault as far from the way in as the deepest level goes, a security post by the way
    /// in; the rest are filled from the purpose's fill list.
    /// </summary>
    void Purposes()
    {
        var dist = Distances();
        foreach (var lv in Levels)
        {
            var open = lv.Spaces.Where(s => !s.Dead && s.Open && !s.Secret).ToList();
            var wants = new List<(string Room, bool Must)>();
            foreach (string band in lv.Bands)
                if (Def.Rooms.TryGetValue(band, out var demands))
                    foreach (var d in demands)
                    {
                        int n = d.Must ? Math.Max(1, d.Min) : d.Min + (d.Max > d.Min ? R.Between(0, d.Max - d.Min) : 0);
                        for (int c = 0; c < n; c++) wants.Add((d.Room, d.Must));
                    }
            // Musts first, the vault before all.
            wants = wants.OrderByDescending(w => w.Room == "vault").ThenByDescending(w => w.Must).ToList();
            foreach (var (room, must) in wants)
            {
                if (room == "vault" && _vaultSpace != null) continue;
                if (!Lib.Rooms.TryGetValue(room, out var def)) continue;
                var pick = Best(open, def, dist, lv);
                if (pick == null)
                {
                    if (room == "vault" && lv.Last) pick = DeadEnd(lv, vault: true);
                    if (pick == null) continue;
                }
                Assign(pick, def);
                open.Remove(pick);
                if (room == "vault") _vaultSpace = pick;
            }
            foreach (var s in open)
            {
                string id = Def.Fill.Count > 0 ? R.Pick(Def.Fill) : "storage";
                Assign(s, Lib.Room(id));
            }
        }
        // Rooms named by the part that made them (pod halls, cells, the ops hall) take their library entries.
        foreach (var s in Live.Where(s => s.Def == null && !s.Secret && Lib.Rooms.ContainsKey(s.Purpose)))
            s.Def = Lib.Room(s.Purpose);
        // Every place keeps a vault: the farthest room from the way in, on the deepest level it reaches.
        if (_vaultSpace == null && _holdsVault)
        {
            var deepest = Levels.LastOrDefault();
            var pick = deepest == null ? null : DeadEnd(deepest, vault: true)
                ?? Live.Where(s => s.Kind == "room" && !s.Secret).OrderBy(s => s.J).ThenByDescending(s => dist.GetValueOrDefault(s, 0)).FirstOrDefault();
            if (pick != null)
            {
                Assign(pick, Lib.Room("vault"));
                _vaultSpace = pick;
            }
        }
    }

    void Assign(DSpace s, RoomDef def)
    {
        s.Def = def;
        s.Purpose = def.Id;
        s.Open = false;
        if (def.Kind == ModuleKind.Vault || def.Id == "vault") s.Module = ModuleKind.Vault;
        else if (s.Module != ModuleKind.Hall) s.Module = def.Kind == ModuleKind.Hall && s.Area >= 30 ? ModuleKind.Hall : ModuleKind.Room;
    }

    /// <summary>The room that suits a purpose best: its size against the library's widths, where it sits against the purpose's preference.</summary>
    DSpace? Best(List<DSpace> open, RoomDef def, Dictionary<DSpace, int> dist, Level lv)
    {
        if (open.Count == 0) return null;
        int far = open.Max(s => dist.GetValueOrDefault(s, 0));
        DSpace? best = null;
        float bestScore = float.MaxValue;
        foreach (var s in open)
        {
            int shortSide = 2 * Math.Min(s.W, s.D), longSide = 2 * Math.Max(s.W, s.D);
            float score = 0f;
            if (shortSide < def.WMin) score += (def.WMin - shortSide) * 2f;
            if (shortSide > def.WMax + 6) score += (shortSide - def.WMax - 6) * 0.5f;
            // Its floor area as a room of its purpose has (a space's columns are 2 m square).
            if (def.AMax > 0)
            {
                float a = s.Area * 4f;
                if (a < def.AMin * 0.75f) score += (def.AMin - a) / def.AMin * 6f;
                else if (a > def.AMax * 1.5f) score += (a - def.AMax) / def.AMax * 6f;
            }
            if (def.Kind == ModuleKind.Hall && s.Area < 30) score += 8f;
            if (def.Kind != ModuleKind.Hall && s.Module == ModuleKind.Hall) score += 4f;
            int d = dist.GetValueOrDefault(s, 0);
            if (def.Id == "vault" || def.Prefer == "end") score += (far - d) * 0.6f;
            if (def.Prefer == "entrance") score += d * 0.6f;
            if (def.Id == "vault" && Joints.Count(j => j.A == s || j.B == s) == 1) score -= 3f;
            score += R.Float(0f, 1.5f);
            if (score < bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    /// <summary>Steps from the way in to each space, through the plan's joints.</summary>
    Dictionary<DSpace, int> Distances()
    {
        var adj = new Dictionary<DSpace, List<DSpace>>();
        foreach (var j in Joints)
        {
            (adj.TryGetValue(j.A, out var la) ? la : adj[j.A] = new List<DSpace>()).Add(j.B);
            (adj.TryGetValue(j.B, out var lb) ? lb : adj[j.B] = new List<DSpace>()).Add(j.A);
        }
        var dist = new Dictionary<DSpace, int>();
        var start = _top ?? Levels.FirstOrDefault()?.Arrival;
        if (start == null) return dist;
        var q = new Queue<DSpace>();
        dist[start] = 0;
        q.Enqueue(start);
        while (q.Count > 0)
        {
            var s = q.Dequeue();
            if (!adj.TryGetValue(s, out var next)) continue;
            foreach (var n in next)
                if (!dist.ContainsKey(n)) { dist[n] = dist[s] + 1; q.Enqueue(n); }
        }
        return dist;
    }

    // ---- dead ends ---------------------------------------------------------------------------

    /// <summary>
    /// A short way off a corridor that goes nowhere: at its end a vault behind a locked door, a hidden room behind
    /// a wall that sounds hollow, or nothing but a wall (and what someone left there).
    /// </summary>
    DSpace? DeadEnd(Level lv, bool vault)
    {
        var hosts = lv.Spaces.Where(s => !s.Dead && Walkway(s) && s.Kind is not "landing").OrderBy(_ => R.Float()).ToList();
        if (hosts.Count == 0) hosts = lv.Spaces.Where(s => !s.Dead && s.Kind == "landing").ToList();
        foreach (var host in hosts)
            foreach (Dir d in Dirs.Horizontal.OrderBy(_ => R.Float()))
            {
                var stub = From(lv, host, d, R.Between(3, 6), 2, Math.Min(3, lv.H), kind: host.Raw ? "drift" : "corridor", minLen: 3, raw: host.Raw ? 1f : 0f);
                if (stub == null) continue;
                if (vault || R.Chance(0.5f))
                {
                    var room = EndRoom(lv, stub, R.Between(4, 6), R.Between(4, 6), purpose: vault ? "vault" : null, mk: vault ? ModuleKind.Vault : ModuleKind.Room, door: vault ? "locked" : "door");
                    if (room == null) { Drop(stub.S, lv); continue; }
                    room.Raw = false;
                    if (vault)
                    {
                        Assign(room, Lib.Room("vault"));
                        _vaultSpace ??= room;
                    }
                    return room;
                }
                // A hidden room behind the stub's end wall.
                var hidden = EndRoom(lv, stub, R.Between(3, 4), R.Between(3, 4), purpose: "hidden stash", door: "secret");
                if (hidden != null) { hidden.Secret = true; hidden.Raw = false; }
                return null;
            }
        return null;
    }

    // ---- service crawlways -------------------------------------------------------------------

    /// <summary>
    /// A narrow service way (one block wide and high: 2 m) through the rock beside the main ways, from one room or
    /// corridor to another it doesn't already open on, with a hatch door at each end: a way round, a short cut.
    /// </summary>
    void Crawlway(Level lv)
    {
        var spaces = lv.Spaces.Where(s => !s.Dead && !s.Secret && s.Kind is "corridor" or "room" or "hall" or "gallery" or "hub" or "ring").ToList();
        for (int tries = 0; tries < 16; tries++)
        {
            if (spaces.Count < 2) return;
            var a = R.Pick(spaces);
            var b = spaces.Where(s => s != a && !Joints.Any(j => j.A == a && j.B == s || j.A == s && j.B == a))
                .OrderBy(s => Math.Abs(s.Mid.I - a.Mid.I) + Math.Abs(s.Mid.K - a.Mid.K)).Skip(R.Between(0, 2)).FirstOrDefault();
            if (b == null) continue;
            // Out of a's side toward b, then along to b's face.
            float dx = b.Mid.I - a.Mid.I, dz = b.Mid.K - a.Mid.K;
            Dir first = MathF.Abs(dx) > MathF.Abs(dz) ? (dx > 0 ? Dir.East : Dir.West) : (dz > 0 ? Dir.South : Dir.North);
            var face = Face(a, first, lv.J);
            if (face.Count == 0) continue;
            var start = R.Pick(face);
            var (di, dk) = V(first);
            var s = NewSpace("crawl", "crawlway", ModuleKind.Corridor, lv.J, 1, lv);
            s.Raw = false;
            int i = start.I + 2 * di, k = start.K + 2 * dk;
            // Out until level with b across the first axis, then along the second until next to b's wall.
            bool ok = false;
            for (int n = 0; n < 40; n++)
            {
                s.Column(i, k);
                bool besideB = Dirs.Horizontal.Any(dd => { var (ei, ek) = V(dd); return b.Has(i + 2 * ei, k + 2 * ek) && b.FloorAt(i + 2 * ei, k + 2 * ek) == lv.J; });
                if (besideB && n >= 1) { ok = true; break; }
                int ti = (int)MathF.Round(b.Mid.I), tk = (int)MathF.Round(b.Mid.K);
                bool alongFirst = di != 0 ? Math.Abs(i - ti) > b.W / 2 + 1 : Math.Abs(k - tk) > b.D / 2 + 1;
                if (alongFirst) { i += di; k += dk; }
                else if (di != 0) k += Math.Sign(tk - k);
                else i += Math.Sign(ti - i);
                if (s.Has(i, k)) break;
            }
            if (!ok || s.Columns < 2 || s.Columns > 30 || !Take(s, lv)) continue;
            var d1 = Door(a, s, "hatch");
            var d2 = d1 == null ? null : Door(s, b, "hatch");
            if (d1 == null || d2 == null) { Drop(s, lv); continue; }
            return;
        }
    }

    // ---- hidden rooms -------------------------------------------------------------------------

    /// <summary>Rooms walled off with a weak wall: off a room or a corridor, a block of wall between, its tells on the near side.</summary>
    void Secrets()
    {
        int want = R.Between(Def.SecretsMin, Def.SecretsMax);
        var hosts = Live.Where(s => !s.Secret && s.Kind is "room" or "corridor" or "gallery" or "drift" or "hall" or "landing" && s.Level >= 0).OrderBy(_ => R.Float()).ToList();
        int made = Live.Count(s => s.Secret);
        foreach (var host in hosts)
        {
            if (made >= want) break;
            var lv = Levels.FirstOrDefault(l => l.Index == host.Level);
            if (lv == null) continue;
            foreach (Dir d in Dirs.Horizontal.OrderBy(_ => R.Float()))
            {
                var face = Face(host, d, host.J);
                if (face.Count < 3) continue;
                var mid = face[face.Count / 2];
                var (di, dk) = V(d);
                var (li, lk) = Lat(d);
                var s = NewSpace("room", "hidden stash", ModuleKind.Room, host.J, 2, lv);
                s.Raw = host.Raw;
                int deep = R.Between(2, 3), half = 1;
                for (int t = 2; t < 2 + deep; t++)
                    for (int x = -half; x <= half; x++) s.Column(mid.I + t * di + x * li, mid.K + t * dk + x * lk);
                if (!Take(s, lv)) continue;
                if (Door(host, s, "secret", (mid.I + di, mid.K + dk)) == null) { Drop(s, lv); continue; }
                s.Secret = true;
                made++;
                break;
            }
        }
    }
}
