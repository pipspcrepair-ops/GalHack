#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The shared plan parts a level grows from: each starts at the level's way in and grows its own shape. They
/// share the same few tools (a straight way off a face, rooms along a way, a room at a way's end), so a spine,
/// a hub and its spokes, a ring, a grid, a hall with its gallery, a chamber with a gallery round it, a gallery
/// of alcoves, cell blocks round a hub, pod halls off a ring, winding drifts and stopes, and round chambers on
/// curved ways all come out of the same rules, at the sizes the purpose and its builders give them.
/// </summary>
internal sealed partial class Depths
{
    /// <summary>A straight way: its first column, its direction, its length and width (lanes run +i or +k, as a flight's do).</summary>
    sealed class Way
    {
        public DSpace S = null!;
        public int I, K, Len, Wid;
        public Dir D;
        public (int I, int K) At(int t, int w)
        {
            var (di, dk) = V(D);
            var (li, lk) = Lat(D);
            return (I + t * di + w * li, K + t * dk + w * lk);
        }
    }

    static (int I, int K) Lat(Dir d) => d is Dir.North or Dir.South ? (1, 0) : (0, 1);
    static Dir DirOf(int di, int dk) => di > 0 ? Dir.East : di < 0 ? Dir.West : dk > 0 ? Dir.South : Dir.North;
    static int Along(Dir d, int i, int k) { var (di, dk) = V(d); return i * di + k * dk; }
    static int Across(Dir d, int i, int k) { var (li, lk) = Lat(d); return i * li + k * lk; }

    // ---- growing a level --------------------------------------------------------------------

    void Grow(Level lv)
    {
        var a = lv.Arrival!;
        if (_span != null && _spanKind == "chamber")
        {
            // A silo's levels wrap round its shaft first (the deeper ones already did, to be reached), then grow on from the ring.
            var ring = _rings.TryGetValue(lv.Index, out var built) ? built : RingAround(lv, a);
            if (ring != null)
            {
                lv.Part = "ring";
                foreach (var side in ring) RoomsAlong(lv, side, neg: true, pos: false, density: 0.7f);
                // And one more part off the ring's outside, where there's room.
                if (R.Chance(0.6f))
                    foreach (var side in ring.OrderBy(_ => R.Float()))
                    {
                        Dir outward = side.D == Dir.East ? Dir.North : Dir.West;
                        if (From(lv, side.S, outward, R.Between(6, 12), 2, Math.Min(lv.H, 3), minLen: 4) is { } spur)
                        {
                            RoomsAlong(lv, spur, density: 0.7f);
                            EndRoom(lv, spur, R.Between(4, 6), 5);
                            break;
                        }
                    }
                return;
            }
        }
        // The operations hall: off any face, and smaller if the full one won't go.
        if (lv.Index == _opsLevel && (Faces(lv.In, f => Hall(lv, a, f, ops: true)) || Faces(lv.In, f => Hall(lv, a, f, ops: true, small: true)))) { lv.Part = "hall"; return; }
        bool ok = Part(lv.Part, lv, a, lv.In);
        if (!ok)
            foreach (string fallback in new[] { "spine", "hub", "gallery" })
                if (fallback != lv.Part && Part(fallback, lv, a, lv.In)) { lv.Part = fallback; ok = true; break; }
        if (!ok) lv.Part = "landing";
    }

    bool Part(string part, Level lv, DSpace a, Dir d) => part switch
    {
        "spine" => Faces(d, f => Spine(lv, a, f)),
        "hub" => Faces(d, f => Hub(lv, a, f)),
        "ring" => Faces(d, f => Ring(lv, a, f, pods: false)),
        "pods" => Faces(d, f => Ring(lv, a, f, pods: true)),
        "grid" => Faces(d, f => Grid(lv, a, f)),
        "hall" => Faces(d, f => Hall(lv, a, f, ops: false)),
        "chamber" => Faces(d, f => Chamber(lv, a, f)),
        "gallery" => Faces(d, f => Gallery(lv, a, f)),
        "cells" => lv.H >= 5 && Faces(d, f => Hall(lv, a, f, ops: false, cells: true)) || Faces(d, f => Cells(lv, a, f)),
        "drifts" => Faces(d, f => Drifts(lv, a, f)),
        "organic" => Faces(d, f => Organic(lv, a, f)),
        _ => Faces(d, f => Spine(lv, a, f)),
    };

    /// <summary>Tries a part off the way-in face first, then off its sides.</summary>
    bool Faces(Dir d, Func<Dir, bool> attempt) => attempt(d) || attempt(Left(d)) || attempt(Right(d));

    // ---- tools ---------------------------------------------------------------------------------

    /// <summary>The columns along a space's face in a direction: its far edge that way, at the floor it walks on.</summary>
    static List<(int I, int K)> Face(DSpace s, Dir d, int floor)
    {
        var (di, dk) = V(d);
        var edge = s.Foot.Where(c => !s.Has(c.I + di, c.K + dk) && s.FloorAt(c.I, c.K) == floor).ToList();
        if (edge.Count == 0) return edge;
        int far = edge.Max(c => Along(d, c.I, c.K));
        return edge.Where(c => Along(d, c.I, c.K) == far).OrderBy(c => Across(d, c.I, c.K)).ToList();
    }

    /// <summary>A way of columns: len along d from (i, k), wid lanes.</summary>
    Way? Corr(Level lv, string kind, string purpose, ModuleKind mk, int i, int k, Dir d, int len, int wid, int j, int h, params DSpace?[] touch)
    {
        var w = new Way { I = i, K = k, D = d, Len = len, Wid = wid };
        var s = NewSpace(kind, purpose, mk, j, h, lv);
        for (int t = 0; t < len; t++)
            for (int x = 0; x < wid; x++) { var (ci, ck) = w.At(t, x); s.Column(ci, ck); }
        if (!Take(s, lv, touch)) return null;
        w.S = s;
        return w;
    }

    /// <summary>
    /// A way straight off a space's face, centred on it, as long as will fit (down to minLen) and nudged sideways
    /// if it must; open into the space.
    /// </summary>
    Way? From(Level lv, DSpace host, Dir d, int len, int wid, int h, string kind = "corridor", string purpose = "corridor", int minLen = 3, ModuleKind mk = ModuleKind.Corridor, float raw = -1f)
    {
        var face = Face(host, d, lv.J);
        if (face.Count == 0) return null;
        var (di, dk) = V(d);
        int along = Along(d, face[0].I, face[0].K) + 1;
        int lo = Across(d, face[0].I, face[0].K), hi = Across(d, face[^1].I, face[^1].K);
        int mid = (lo + hi) / 2 - (wid - 1) / 2;
        foreach (int shift in new[] { 0, -1, 1, -2, 2, -3, 3 })
        {
            int a0 = mid + shift;
            // At least two lanes must meet the face, so it opens properly.
            if (Math.Min(a0 + wid - 1, hi) - Math.Max(a0, lo) + 1 < Math.Min(2, Math.Min(wid, hi - lo + 1))) continue;
            for (int l = len; l >= minLen; l -= 2)
            {
                // Back from (along, across) to (i, k): along is i or k times the direction's sign, across the other.
                int i = di != 0 ? along * di : a0, k = di != 0 ? a0 : along * dk;
                var w = Corr(lv, kind, purpose, mk, i, k, d, l, wid, lv.J, h, host);
                if (w == null) continue;
                if (raw >= 0f) w.S.Raw = raw > 0.5f;
                Join(host, w.S);
                return w;
            }
        }
        return null;
    }

    /// <summary>Rooms side by side along a way, a block of wall between each and the way, each with its door.</summary>
    List<DSpace> RoomsAlong(Level lv, Way w, bool neg = true, bool pos = true, float density = 0.85f, int from = 0, int? to = null,
        int minW = 3, int maxW = 6, int minD = 3, int maxD = 6, string? purpose = null, ModuleKind mk = ModuleKind.Room, int? height = null, string door = "door")
    {
        var made = new List<DSpace>();
        int end = to ?? w.Len - 1;
        foreach (bool side in new[] { false, true })
        {
            if (side ? !pos : !neg) continue;
            int p = from + R.Between(0, 1);
            while (p <= end - 1)
            {
                int rw = R.Between(minW, maxW), rd = R.Between(minD, maxD);
                if (!R.Chance(density)) { p += rw + 1; continue; }
                rw = Math.Min(rw, end - p + 1);
                if (rw < 2) break;
                DSpace? room = null;
                for (int dd = rd; dd >= Math.Min(minD, 2) && room == null; dd--)
                    for (int ww = rw; ww >= Math.Min(minW, 2) && room == null; ww--)
                    {
                        var s = NewSpace("room", purpose ?? "room", mk, w.S.J, height ?? RoomH, lv);
                        for (int t = p; t < p + ww; t++)
                            for (int x = 0; x < dd; x++)
                            {
                                int lane = side ? w.Wid + 1 + x : -2 - x;
                                var (ci, ck) = w.At(t, lane);
                                s.Column(ci, ck);
                            }
                        if (!Take(s, lv)) continue;
                        var mid = w.At(p + ww / 2, side ? w.Wid : -1);
                        if (Door(w.S, s, door, mid) == null) { Drop(s, lv); continue; }
                        s.Open = purpose == null;
                        room = s;
                        rw = ww;
                    }
                if (room != null) made.Add(room);
                p += rw + 1;
            }
        }
        return made;
    }

    /// <summary>A room across a way's far end, a wall between, its door in the middle.</summary>
    DSpace? EndRoom(Level lv, Way w, int depth, int wide, string? purpose = null, ModuleKind mk = ModuleKind.Room, int? height = null, string door = "door")
    {
        for (int dd = depth; dd >= 3; dd--)
            for (int ww = wide; ww >= Math.Max(3, w.Wid); ww--)
            {
                var s = NewSpace("room", purpose ?? "room", mk, w.S.J, height ?? RoomH, lv);
                int a0 = (w.Wid - ww) / 2;
                for (int t = w.Len + 1; t <= w.Len + dd; t++)
                    for (int x = a0; x < a0 + ww; x++) { var (ci, ck) = w.At(t, x); s.Column(ci, ck); }
                if (!Take(s, lv)) continue;
                if (Door(w.S, s, door, w.At(w.Len, w.Wid / 2)) == null) { Drop(s, lv); continue; }
                s.Open = purpose == null;
                return s;
            }
        return null;
    }

    /// <summary>A way that turns off another's end, left or right.</summary>
    Way? Turn(Level lv, Way w, bool left, int len, int wid, int h)
    {
        Dir d2 = left ? Left(w.D) : Right(w.D);
        var (li, lk) = Lat(w.D);
        var (d2i, d2k) = V(d2);
        bool positive = d2i == li && d2k == lk;
        wid = Math.Min(wid, w.Len);
        // Its first column sits just past the end square's side; its lanes run along the old way's axis, from the lower end.
        var (di, dk) = V(w.D);
        int t0 = di + dk > 0 ? w.Len - wid : w.Len - 1;
        var start = w.At(t0, positive ? w.Wid : -1);
        for (int l = len; l >= 3; l -= 2)
        {
            var nw = Corr(lv, "corridor", "corridor", ModuleKind.Corridor, start.I, start.K, d2, l, wid, w.S.J, h, w.S);
            if (nw == null) continue;
            Join(w.S, nw.S);
            return nw;
        }
        return null;
    }

    // ---- spine ---------------------------------------------------------------------------------

    /// <summary>One main way with rooms down both sides; it may turn once; a room or a hall across its end.</summary>
    bool Spine(Level lv, DSpace a, Dir d)
    {
        int wid = MainW, h = Math.Min(lv.H, MainH);
        var w = From(lv, a, d, R.Between(10, 22), wid, h, minLen: 6);
        if (w == null) return false;
        RoomsAlong(lv, w, density: Culture == "linear" ? 0.95f : 0.8f);
        var last = w;
        if (R.Chance(Culture == "warren" ? 0.8f : 0.5f))
            if (Turn(lv, w, R.Chance(0.5f), R.Between(6, 14), SideW + 1, Math.Min(h, SideH + 1)) is { } t)
            {
                RoomsAlong(lv, t, from: 0, density: 0.75f);
                last = t;
            }
        EndRoom(lv, last, R.Between(4, 7), last.Wid + R.Between(2, 5));
        return true;
    }

    // ---- hub and spokes ------------------------------------------------------------------------

    /// <summary>A short way in to a hub, round or square, with spokes off its other sides, rooms along each and a room at each end.</summary>
    bool Hub(Level lv, DSpace a, Dir d)
    {
        int wid = MainW, h = Math.Min(lv.H, MainH);
        var entry = From(lv, a, d, R.Between(2, 5), wid, h, minLen: 2);
        if (entry == null) return false;
        bool round = Culture is "radial" || Tech == TechLevel.High && R.Chance(0.5f) || R.Chance(0.3f);
        int size = R.Between(7, 11), r = R.Between(4, 6);
        var hub = NewSpace("hub", "hub", ModuleKind.Corridor, lv.J, Math.Min(lv.H, h + 1), lv);
        var (ei, ek) = entry.At(entry.Len, entry.Wid / 2);
        var (di, dk) = V(d);
        var (li, lk) = Lat(d);
        if (round)
        {
            int ci = ei + r * di, ck = ek + r * dk;
            for (int k = ck - r; k <= ck + r; k++)
                for (int i = ci - r; i <= ci + r; i++)
                    if (Plot.In(ci + 0.5, ck + 0.5, r + 0.45, i, k)) hub.Column(i, k);
        }
        else
            for (int t = 0; t < size; t++)
                for (int x = -size / 2; x < size - size / 2; x++) hub.Column(ei + t * di + x * li, ek + t * dk + x * lk);
        if (!Take(hub, lv, entry.S))
        {
            Drop(entry.S, lv);
            return false;
        }
        Join(entry.S, hub);
        int spokes = 0;
        foreach (Dir sd in new[] { d, Left(d), Right(d) }.OrderBy(_ => R.Float()))
        {
            if (spokes >= 2 && !R.Chance(Culture == "radial" ? 0.9f : 0.6f)) continue;
            var sp = From(lv, hub, sd, R.Between(6, 14), Math.Max(2, SideW + (R.Chance(0.4f) ? 1 : 0)), Math.Min(lv.H, SideH + 1), minLen: 4);
            if (sp == null) continue;
            spokes++;
            RoomsAlong(lv, sp, density: 0.75f);
            EndRoom(lv, sp, R.Between(4, 7), sp.Wid + R.Between(2, 4), mk: R.Chance(0.3f) ? ModuleKind.Hall : ModuleKind.Room);
        }
        // Rooms round a big square hub's corners, between the spokes.
        if (!round && spokes < 3) RoomsAlong(lv, new Way { S = hub, I = ei - size / 2 * li, K = ek - size / 2 * lk, D = d, Len = size, Wid = size }, density: 0.5f, from: 1, to: size - 2);
        return spokes > 0;
    }

    // ---- ring ------------------------------------------------------------------------------------

    /// <summary>
    /// A loop: a way in, then four sides of a ring round a central block. The block is a hall with doors off the
    /// ring; rooms (or, for a cryo vault, rows of pod halls) line the ring's outer sides.
    /// </summary>
    bool Ring(Level lv, DSpace a, Dir d, bool pods)
    {
        int wid = Math.Max(2, Math.Min(MainW, 4)), h = Math.Min(lv.H, MainH);
        var entry = From(lv, a, d, R.Between(2, 5), Math.Max(2, MainW), h, minLen: 2);
        if (entry == null) return false;
        int W = R.Between(16, 26), D = R.Between(14, 24);
        var (ei, ek) = entry.At(entry.Len, entry.Wid / 2);
        var sides = RingSides(lv, entry.S, ei, ek, d, W, D, wid, h);
        if (sides == null) { Drop(entry.S, lv); return false; }
        var (nr, lf, rt, fr) = sides.Value;
        // The block inside: a hall on the loop.
        var (di, dk) = V(d);
        var (li, lk) = Lat(d);
        var inner = NewSpace("room", "room", ModuleKind.Hall, lv.J, Math.Min(lv.H, h + 1), lv);
        for (int t = wid + 1; t <= D - wid - 2; t++)
            for (int x = -W / 2 + wid + 1; x <= W - W / 2 - wid - 2; x++) inner.Column(ei + t * di + x * li, ek + t * dk + x * lk);
        if (inner.Columns >= 16 && Take(inner, lv))
        {
            inner.Open = true;
            int doors = 0;
            foreach (var side in new[] { lf, rt, fr, nr }.OrderBy(_ => R.Float()))
                if (doors < 2 && Door(side.S, inner) != null) doors++;
            if (doors == 0) Drop(inner, lv);
        }
        // Outward: left is the low lane side, right the high; the far side's outward is d, the near side's against it.
        bool dPos = di + dk > 0;
        foreach (var (side, outPos) in new[] { (lf, false), (rt, true), (fr, dPos) })
        {
            if (pods)
                RoomsAlong(lv, side, neg: !outPos, pos: outPos, density: 0.9f, minW: 5, maxW: 8, minD: 7, maxD: 11, purpose: "cryo-bay", mk: ModuleKind.Hall, height: Math.Min(lv.H, 3));
            else RoomsAlong(lv, side, neg: !outPos, pos: outPos, density: 0.8f);
        }
        if (!pods) RoomsAlong(lv, nr, neg: dPos, pos: !dPos, density: 0.5f);
        return true;
    }

    /// <summary>The four sides of a ring whose near side crosses (ei, ek), W across and D deep; sides are ways so rooms can line them.</summary>
    (Way Near, Way Left, Way Right, Way Far)? RingSides(Level lv, DSpace from, int ei, int ek, Dir d, int W, int D, int wid, int h, DSpace? alsoTouch = null)
    {
        var (di, dk) = V(d);
        var (li, lk) = Lat(d);
        Dir lat = DirOf(li, lk);
        int x0 = -W / 2, x1 = W - W / 2 - 1;
        // Near and far sides run across d, as ways along the lateral axis; the left and right sides run along d between them.
        (int I, int K) P(int t, int x) => (ei + t * di + x * li, ek + t * dk + x * lk);
        Way? Side(int t0, int t1, int xa, int xb, Dir run, params DSpace?[] touch)
        {
            var s = NewSpace("ring", "corridor", ModuleKind.Corridor, lv.J, h, lv);
            for (int t = t0; t <= t1; t++)
                for (int x = xa; x <= xb; x++) { var (ci, ck) = P(t, x); s.Column(ci, ck); }
            if (!Take(s, lv, touch)) return null;
            // As a way: first column at its low corner, lanes positive.
            int along0 = run == lat ? xa : t0;
            var (si, sk) = run == lat ? P(t0, xa) : P(t0, xa);
            var w = new Way { S = s, D = run, Len = run == lat ? xb - xa + 1 : t1 - t0 + 1, Wid = run == lat ? t1 - t0 + 1 : xb - xa + 1 };
            // Recover the corner whose lanes run positive.
            var cols = s.Foot.ToList();
            var (rdi, rdk) = V(run);
            var (rli, rlk) = Lat(run);
            int minAlong = cols.Min(c => c.I * rdi + c.K * rdk), minAcross = cols.Min(c => c.I * rli + c.K * rlk);
            var corner = cols.First(c => c.I * rdi + c.K * rdk == minAlong && c.I * rli + c.K * rlk == minAcross);
            w.I = corner.I;
            w.K = corner.K;
            return w;
        }
        // Run directions with positive steps, so the Way arithmetic holds whichever way the ring faces.
        Dir along = di + dk > 0 ? d : d.Opposite();
        Dir across = DirOf(li, lk);
        var near = Side(0, wid - 1, x0, x1, across, from, alsoTouch);
        if (near == null) return null;
        Join(from, near.S);
        var left = Side(wid, D - wid - 1, x0, x0 + wid - 1, along, near.S);
        var right = Side(wid, D - wid - 1, x1 - wid + 1, x1, along, near.S);
        if (left == null || right == null)
        {
            if (left != null) Drop(left.S, lv);
            if (right != null) Drop(right.S, lv);
            Drop(near.S, lv);
            return null;
        }
        var far = Side(D - wid, D - 1, x0, x1, across, left.S, right.S);
        if (far == null)
        {
            Drop(left.S, lv);
            Drop(right.S, lv);
            Drop(near.S, lv);
            return null;
        }
        Join(near.S, left.S);
        Join(near.S, right.S);
        Join(left.S, far.S);
        Join(right.S, far.S);
        // Which side of each is outward: the Way's lanes run positive, so "pos" is +lateral of its run.
        return (near, left, right, far);
    }

    /// <summary>A ring round the silo's shaft at this level, a door through to its gallery at each of the four points; its way in from the arrival.</summary>
    List<Way>? RingAround(Level lv, DSpace? a)
    {
        if (_spanWid == 0) _spanWid = R.Between(2, 3);
        int r = _spanR, wid = _spanWid, h = Math.Min(lv.H, 3);
        int half = r + 2;
        // The ring's box: its inside clears the shaft by a wall all round.
        int i0 = _spanCi - half - wid + 1, i1 = _spanCi + half + wid - 1, k0 = _spanCk - half - wid + 1, k1 = _spanCk + half + wid - 1;
        var sides = new List<DSpace>();
        DSpace? MakeSide(int a0, int b0, int a1, int b1, params DSpace?[] touch)
        {
            var s = NewSpace("ring", "corridor", ModuleKind.Corridor, lv.J, h, lv);
            s.Raw = false;
            for (int k = b0; k <= b1; k++) for (int i = a0; i <= a1; i++) s.Column(i, k);
            return Take(s, lv, touch) ? s : null;
        }
        var north = MakeSide(i0, k0, i1, k0 + wid - 1);
        var south = north == null ? null : MakeSide(i0, k1 - wid + 1, i1, k1);
        var west = south == null ? null : MakeSide(i0, k0 + wid, i0 + wid - 1, k1 - wid, north, south);
        var east = west == null ? null : MakeSide(i1 - wid + 1, k0 + wid, i1, k1 - wid, north, south);
        if (east == null)
        {
            foreach (var s in new[] { north, south, west }) if (s != null) Drop(s, lv);
            return null;
        }
        Join(north!, west!); Join(north!, east); Join(south!, west!); Join(south!, east);
        // Doors through to the shaft's gallery at the four points.
        foreach (var (wi, wk, alongI) in new[] { (_spanCi, _spanCk - r - 1, true), (_spanCi, _spanCk + r + 1, true), (_spanCi - r - 1, _spanCk, false), (_spanCi + r + 1, _spanCk, false) })
        {
            if (G.Interior(wi, lv.J, wk) || G.Interior(wi, lv.J + 1, wk)) continue;
            var side = new[] { north!, south!, west!, east }.First(s => s.Has(wi + (alongI ? 0 : Math.Sign(wi - _spanCi)), wk + (alongI ? Math.Sign(wk - _spanCk) : 0)));
            Joints.Add(new DJoint { A = side, B = _span!, Kind = "door", I = wi, J = lv.J, K = wk, AlongI = alongI });
            G.Keep(wi, lv.J, wk);
            G.Keep(wi, lv.J + 1, wk);
        }
        _spanGalleries.Add(lv.J);
        // The way from the arrival to the ring.
        if (a != null && !Link(lv, a, new[] { north!, south!, west!, east }))
        {
            foreach (var s in new[] { north!, south!, west!, east }) Drop(s, lv);
            Joints.RemoveAll(j => j.B == _span && j.J == lv.J);
            _spanGalleries.Remove(lv.J);
            return null;
        }
        _ringSides[lv.Index] = (north!, south!, west!, east);
        Way AsWay(DSpace s, Dir run)
        {
            var (rdi, rdk) = V(run);
            var (rli, rlk) = Lat(run);
            var cols = s.Foot.ToList();
            int minAlong = cols.Min(c => c.I * rdi + c.K * rdk), minAcross = cols.Min(c => c.I * rli + c.K * rlk);
            var c0 = cols.First(c => c.I * rdi + c.K * rdk == minAlong && c.I * rli + c.K * rlk == minAcross);
            return new Way { S = s, I = c0.I, K = c0.K, D = run, Len = run is Dir.East ? s.W : s.D, Wid = run is Dir.East ? s.D : s.W };
        }
        // Outward sides: north's outward is -k (neg lanes when run East), south's +k (pos), west's -i (neg when run South), east's +i (pos).
        var ways = new List<Way> { AsWay(north!, Dir.East), AsWay(west!, Dir.South) };
        RoomsAlong(lv, AsWay(south!, Dir.East), neg: false, pos: true, density: 0.75f);
        RoomsAlong(lv, AsWay(east, Dir.South), neg: false, pos: true, density: 0.75f);
        _rings[lv.Index] = ways;
        return ways;
    }

    readonly List<int> _spanGalleries = new();
    readonly Dictionary<int, List<Way>> _rings = new();
    readonly Dictionary<int, (DSpace N, DSpace S, DSpace W, DSpace E)> _ringSides = new();
    int _spanWid;

    /// <summary>
    /// The way down between two of a silo's levels: the lower ring is laid first, the same footprint as the one
    /// over it, and a ladder drops from a corner of the upper ring to the same corner of the lower (the corners
    /// alternate, so each ladder ends at its own level).
    /// </summary>
    void SiloDown(Level up, Level down)
    {
        if (!_ringSides.TryGetValue(up.Index, out var upper)) return;
        if (RingAround(down, null) == null || !_ringSides.TryGetValue(down.Index, out var lower)) return;
        // Any corner of the ring but the one the ladder from above came down at.
        var corners = new[]
        {
            (upper.N.I0, upper.N.K0, Dir.North, upper.N, lower.N), (upper.N.I1, upper.N.K0, Dir.North, upper.N, lower.N),
            (upper.S.I0, upper.S.K1, Dir.South, upper.S, lower.S), (upper.S.I1, upper.S.K1, Dir.South, upper.S, lower.S),
        };
        foreach (var (ci, ck, back, from, to) in corners.Where(c => !_siloCorners.Contains((up.Index, c.Item1, c.Item2))).OrderBy(_ => R.Float()))
        {
            if (!to.Has(ci, ck)) continue;
            var sh = NewSpace("shaft", "ladderway", ModuleKind.Stair, down.J + to.H, up.J - (down.J + to.H), down);
            sh.Raw = false;
            sh.Column(ci, ck);
            if (sh.H < 1 || !Take(sh, null, from, to)) continue;
            Join(from, sh);
            Join(sh, to);
            _ladders.Add((ci, ck, back, down.J, up.J));
            _siloCorners.Add((down.Index, ci, ck));
            down.Arrival = to;
            down.In = back;
            return;
        }
        foreach (var s in new[] { lower.N, lower.S, lower.W, lower.E }) Drop(s, down);
        _rings.Remove(down.Index);
        _ringSides.Remove(down.Index);
        Joints.RemoveAll(j => j.B == _span && j.J == down.J);
        _spanGalleries.Remove(down.J);
    }

    /// <summary>The ring corners a ladder from above came down at, by level: the next one down starts elsewhere.</summary>
    readonly HashSet<(int Level, int I, int K)> _siloCorners = new();

    /// <summary>Joins a level's arrival to one of a set of spaces by the shortest way through free rock (see <see cref="Route"/>).</summary>
    bool Link(Level lv, DSpace a, IReadOnlyList<DSpace> targets) => Route(lv, a, targets, 2, Math.Min(3, lv.H), "corridor", raw: false) != null;

    /// <summary>
    /// A way of two-block squares from a space to the nearest of some others through free rock, found by a
    /// breadth-first search over the level's columns: it keeps a block of rock from everything else, may bend
    /// as it must, and opens into both ends.
    /// </summary>
    DSpace? Route(Level lv, DSpace from, IReadOnlyList<DSpace> targets, int wid, int h, string kind, bool raw, int maxSteps = 5000)
    {
        int J = lv.J;
        var allowed = new HashSet<int>(targets.Select(t => t.Id)) { from.Id };
        var memo = new Dictionary<(int, int), bool>();
        // A column that no other space's interior comes near at these levels.
        bool Clear(int i, int k)
        {
            if (memo.TryGetValue((i, k), out bool ok)) return ok;
            ok = true;
            for (int j = J - 1; j <= J + h && ok; j++)
            {
                int o = G.Owner(i, j, k);
                if (o == -2 || o >= 0 && !allowed.Contains(o)) ok = false;
            }
            return memo[(i, k)] = ok;
        }
        bool Free(int i, int k)
        {
            for (int j = J; j < J + h; j++) if (G.Owner(i, j, k) != -1) return false;
            for (int dk = -1; dk <= 1; dk++) for (int di = -1; di <= 1; di++) if (!Clear(i + di, k + dk)) return false;
            return true;
        }
        bool Square(int i, int k)
        {
            for (int z = 0; z < wid; z++) for (int x = 0; x < wid; x++) if (!Free(i + x, k + z)) return false;
            return true;
        }
        DSpace? Touches(int i, int k)
        {
            for (int z = 0; z < wid; z++)
                for (int x = 0; x < wid; x++)
                    foreach (Dir d in Dirs.Horizontal)
                    {
                        var (di, dk) = V(d);
                        int ni = i + x + di, nk = k + z + dk;
                        foreach (var t in targets) if (t.Has(ni, nk) && t.FloorAt(ni, nk) == J) return t;
                    }
            return null;
        }
        var prev = new Dictionary<(int, int), (int, int)>();
        var q = new Queue<(int I, int K)>();
        foreach (var (i, k) in from.Foot)
        {
            if (from.FloorAt(i, k) != J) continue;
            foreach (Dir d in Dirs.Horizontal)
            {
                var (di, dk) = V(d);
                if (from.Has(i + di, k + dk)) continue;
                for (int s = 0; s < wid; s++)
                {
                    // The squares just outside this face column.
                    int ci = di > 0 ? i + 1 : di < 0 ? i - wid : i - s, ck = dk > 0 ? k + 1 : dk < 0 ? k - wid : k - s;
                    if (prev.ContainsKey((ci, ck)) || !Square(ci, ck)) continue;
                    prev[(ci, ck)] = (int.MinValue, 0);
                    q.Enqueue((ci, ck));
                }
            }
        }
        (int, int)? goal = null;
        DSpace? hit = null;
        int steps = 0;
        while (q.Count > 0 && steps++ < maxSteps)
        {
            var (i, k) = q.Dequeue();
            if (Touches(i, k) is { } t) { goal = (i, k); hit = t; break; }
            foreach (Dir d in Dirs.Horizontal)
            {
                var (di, dk) = V(d);
                var n = (i + di, k + dk);
                if (prev.ContainsKey(n) || !Square(n.Item1, n.Item2)) continue;
                prev[n] = (i, k);
                q.Enqueue(n);
            }
        }
        if (goal == null || hit == null) return null;
        var s2 = NewSpace(kind, kind == "tunnel" ? "tunnel" : "corridor", ModuleKind.Corridor, J, h, lv);
        s2.Raw = raw;
        for (var at = goal.Value; at.Item1 != int.MinValue; at = prev[at])
            for (int z = 0; z < wid; z++) for (int x = 0; x < wid; x++) s2.Column(at.Item1 + x, at.Item2 + z);
        if (!Take(s2, lv, from, hit)) return null;
        Join(from, s2);
        Join(s2, hit);
        return s2;
    }

    // ---- grid --------------------------------------------------------------------------------------

    /// <summary>Cross corridors: lines across and along the way in, a room in every cell between them, rooms round the outside.</summary>
    bool Grid(Level lv, DSpace a, Dir d)
    {
        int wid = Math.Max(2, Math.Min(MainW, 4)), h = Math.Min(lv.H, MainH);
        var entry = From(lv, a, d, R.Between(2, 4), Math.Max(2, MainW), h, minLen: 2);
        if (entry == null) return false;
        int spacing = R.Between(8, 11) + wid - 2, nCross = R.Between(2, 3), nAlong = R.Between(2, 3);
        var (ei, ek) = entry.At(entry.Len, entry.Wid / 2);
        var (di, dk) = V(d);
        var (li, lk) = Lat(d);
        (int I, int K) P(int t, int x) => (ei + t * di + x * li, ek + t * dk + x * lk);
        int x0 = -((nAlong - 1) * spacing) / 2 - wid / 2;
        var cross = new List<DSpace>();
        for (int m = 0; m < nCross; m++)
        {
            var s = NewSpace("corridor", "corridor", ModuleKind.Corridor, lv.J, h, lv);
            for (int t = m * spacing; t < m * spacing + wid; t++)
                for (int x = x0; x < x0 + (nAlong - 1) * spacing + wid; x++) { var (ci, ck) = P(t, x); s.Column(ci, ck); }
            if (!Take(s, lv, m == 0 ? entry.S : null)) break;
            cross.Add(s);
        }
        if (cross.Count == 0) { Drop(entry.S, lv); return false; }
        Join(entry.S, cross[0]);
        var along = new Dictionary<(int, int), DSpace>();
        for (int m = 0; m + 1 < cross.Count; m++)
            for (int q = 0; q < nAlong; q++)
            {
                var s = NewSpace("corridor", "corridor", ModuleKind.Corridor, lv.J, h, lv);
                for (int t = m * spacing + wid; t < (m + 1) * spacing; t++)
                    for (int x = x0 + q * spacing; x < x0 + q * spacing + wid; x++) { var (ci, ck) = P(t, x); s.Column(ci, ck); }
                if (!Take(s, lv, cross[m], cross[m + 1])) continue;
                Join(cross[m], s);
                Join(s, cross[m + 1]);
                along[(m, q)] = s;
            }
        // A room in each cell, its door on whichever line will take it.
        for (int m = 0; m + 1 < cross.Count; m++)
            for (int q = 0; q + 1 < nAlong; q++)
            {
                var s = NewSpace("room", "room", ModuleKind.Room, lv.J, Math.Min(lv.H, RoomH + (R.Chance(0.25f) ? 1 : 0)), lv);
                for (int t = m * spacing + wid + 1; t <= (m + 1) * spacing - 2; t++)
                    for (int x = x0 + q * spacing + wid + 1; x <= x0 + (q + 1) * spacing - 2; x++) { var (ci, ck) = P(t, x); s.Column(ci, ck); }
                if (s.Columns < 9 || !Take(s, lv)) continue;
                s.Open = true;
                var lines = new List<DSpace?> { cross[m], cross[m + 1], along.GetValueOrDefault((m, q)), along.GetValueOrDefault((m, q + 1)) };
                bool doored = false;
                foreach (var line in lines.Where(l => l != null).OrderBy(_ => R.Float()))
                    if (Door(line!, s) != null) { doored = true; if (!R.Chance(0.25f)) break; }
                if (!doored) Drop(s, lv);
            }
        // Rooms round the outside of the grid: off the last line across, and down the outer lines along.
        var lastLine = cross[^1];
        var farWay = new Way { S = lastLine, D = DirOf(li, lk), Len = (nAlong - 1) * spacing + wid, Wid = wid };
        var (fi, fk) = P((cross.Count - 1) * spacing + (di + dk > 0 ? 0 : wid - 1), x0);
        farWay.I = fi; farWay.K = fk;
        if (di + dk > 0) RoomsAlong(lv, farWay, neg: false, pos: true, density: 0.7f);
        else RoomsAlong(lv, farWay, neg: true, pos: false, density: 0.7f);
        return true;
    }

    // ---- hall with a gallery --------------------------------------------------------------------

    /// <summary>
    /// A great hall with a gallery round three sides over it (the operations hall of a command bunker, a cell
    /// hall, a machine hall): a flight up along its end wall to the gallery, rooms off the floor and off the
    /// gallery through its long walls, and a way on out of its far end.
    /// </summary>
    bool Hall(Level lv, DSpace a, Dir d, bool ops, bool cells = false, bool small = false)
    {
        int H = Math.Max(5, lv.H);
        if (lv.H < 5) return false;
        int wid = small ? 2 : MainW;
        var entry = From(lv, a, d, R.Between(2, 4), wid, Math.Min(3, MainH), minLen: 2);
        if (entry == null) return false;
        int Hw = small ? wid + 10 : R.Between(Math.Max(14, wid + 10), 18), Hl = small ? R.Between(12, 14) : R.Between(16, 24);
        var (ei, ek) = entry.At(entry.Len, 0);
        var (di, dk) = V(d);
        var (li, lk) = Lat(d);
        (int I, int K) P(int t, int x) => (ei + t * di + x * li, ek + t * dk + x * lk);
        int x0 = -(Hw - wid) / 2, x1 = x0 + Hw - 1;
        var hall = NewSpace("hall", ops ? "command" : cells ? "cellblock" : "room", ModuleKind.Hall, lv.J, H, lv);
        var left = NewSpace("gallery", "gallery", ModuleKind.Corridor, lv.J + 3, H - 3, lv);
        var right = NewSpace("gallery", "gallery", ModuleKind.Corridor, lv.J + 3, H - 3, lv);
        var far = NewSpace("gallery", "gallery", ModuleKind.Corridor, lv.J + 3, H - 3, lv);
        var flight = NewSpace("stair", "stair", ModuleKind.Stair, lv.J, 3, lv);
        foreach (var s in new[] { hall, left, right, far, flight }) s.Raw = false;
        // The flight: two lanes against the near wall, rising toward the left wall onto the gallery's corner.
        var flightCols = new HashSet<(int, int)>();
        for (int t = 0; t < 2; t++)
            for (int x = 0; x < 3; x++) flightCols.Add(P(t, x0 + 2 + x));
        for (int t = 0; t < Hl; t++)
            for (int x = x0; x <= x1; x++)
            {
                var c = P(t, x);
                bool galleryCol = x <= x0 + 1 || x >= x1 - 1 || t >= Hl - 2;
                if (flightCols.Contains(c))
                {
                    int step = 2 - (x - x0 - 2); // x0+4 is the foot (step 0), x0+2 the top (step 2)
                    flight.Column(c.I, c.K, lv.J + step, H - step);
                }
                else if (galleryCol)
                {
                    hall.Column(c.I, c.K, lv.J, 3);
                    var g = x <= x0 + 1 ? left : x >= x1 - 1 ? right : far;
                    g.Column(c.I, c.K);
                }
                else hall.Column(c.I, c.K);
            }
        // Rails along each gallery's open edge.
        foreach (var g in new[] { left, right, far })
            foreach (var (i, k) in g.Foot)
                foreach (Dir od in Dirs.Horizontal)
                {
                    var (oi, ok) = V(od);
                    if (hall.Has(i + oi, k + ok) && hall.HeightAt(i + oi, k + ok) == H) g.Rail.Add((i, k, od));
                }
        var taken = new List<DSpace>();
        bool Try(DSpace s, params DSpace?[] t) { if (!Take(s, lv, t)) return false; taken.Add(s); return true; }
        if (!Try(hall, entry.S) || !Try(flight, hall, left) || !Try(left, hall, flight, far) || !Try(far, hall, left, right) || !Try(right, hall, far))
        {
            foreach (var s in taken) Drop(s, lv);
            Drop(entry.S, lv);
            return false;
        }
        // The flight's foot is the lane column nearest the middle; it rises toward the left wall. Its lanes start at the lower row.
        var lane0 = di + dk > 0 ? P(0, x0 + 4) : P(1, x0 + 4);
        flight.Run = (lane0.I, lane0.K, DirOf(-li, -lk), 3, 2);
        flight.Ramp = Alien;
        _runs.Add(flight);
        Join(entry.S, hall);
        Join(hall, flight);
        Join(flight, left);
        Join(left, far);
        Join(far, right);
        foreach (var g in new[] { left, far, right }) { Join(hall, g); _galleries.Add(g); }
        hall.Open = !ops && !cells;
        // Rooms off the floor and off the gallery, through the long walls.
        foreach (bool pos in new[] { false, true })
        {
            var w = new Way { S = hall, D = d, Len = Hl, Wid = Hw };
            var (si, sk) = P(0, x0);
            w.I = si; w.K = sk;
            // Floor rooms: under the gallery, doors through the long wall.
            string? purpose = cells ? "cellblock" : null;
            RoomsAlong(lv, w, neg: !pos, pos: pos, density: 0.8f, from: 2, to: Hl - 3, minW: cells ? 2 : 3, maxW: cells ? 2 : 6, minD: 3, maxD: cells ? 3 : 6, purpose: purpose, height: 2);
            // Gallery rooms: a storey up, their doors onto the gallery.
            var gw = new Way { S = pos ? right : left, D = d, Len = Hl, Wid = Hw, I = si, K = sk };
            RoomsAlong(lv, gw, neg: !pos, pos: pos, density: 0.75f, from: 2, to: Hl - 3, minW: cells ? 2 : 3, maxW: cells ? 2 : 5, minD: 3, maxD: cells ? 3 : 5,
                purpose: cells ? "cellblock" : ops ? (R.Chance(0.5f) ? "control" : "monitor") : null, height: 2);
        }
        // On out of the far end at floor level.
        var exitFace = Face(hall, d, lv.J);
        if (exitFace.Count > 0)
            if (From(lv, hall, d, R.Between(4, 10), Math.Max(2, Math.Min(wid, 3)), 3, minLen: 3) is { } exit)
            {
                RoomsAlong(lv, exit, density: 0.6f);
                if (R.Chance(0.6f)) EndRoom(lv, exit, R.Between(4, 6), exit.Wid + 3);
            }
        return true;
    }

    // ---- a great chamber with a gallery round it ------------------------------------------------

    /// <summary>A round chamber two storeys tall (a reactor hall, a test chamber, a reading room), a gallery ring round its wall halfway up reached by a flight, rooms off its floor and its gallery.</summary>
    bool Chamber(Level lv, DSpace a, Dir d)
    {
        int H = Math.Max(6, lv.H);
        if (lv.H < 6) return false;
        var entry = From(lv, a, d, R.Between(2, 4), Math.Max(2, MainW), Math.Min(3, MainH), minLen: 2);
        if (entry == null) return false;
        int r = R.Between(6, 7);
        var (ei, ek) = entry.At(entry.Len, entry.Wid / 2);
        var (di, dk) = V(d);
        var (li, lk) = Lat(d);
        int ci = ei + r * di, ck = ek + r * dk;
        int gj = lv.J + 4;
        var chamber = NewSpace("chamber", "room", ModuleKind.Hall, lv.J, H, lv);
        var gallery = NewSpace("gallery", "gallery", ModuleKind.Corridor, gj, H - 4, lv);
        var flight = NewSpace("stair", "stair", ModuleKind.Stair, lv.J, 3, lv);
        foreach (var s in new[] { chamber, gallery, flight }) s.Raw = false;
        // The flight rises toward the chamber's left point onto the gallery.
        Dir up = DirOf(-li, -lk);
        var (ui, uk) = V(up);
        var flightCols = new Dictionary<(int, int), int>();
        for (int t = 0; t < 4; t++)
            for (int w = 0; w < 2; w++)
            {
                // From r-5 out to r-2 out toward the left point, two lanes along d.
                int dist = r - 5 + t;
                flightCols[(ci + dist * ui + w * di, ck + dist * uk + w * dk)] = t;
            }
        for (int k = ck - r; k <= ck + r; k++)
            for (int i = ci - r; i <= ci + r; i++)
            {
                if (!Plot.In(ci + 0.5, ck + 0.5, r + 0.45, i, k)) continue;
                if (flightCols.TryGetValue((i, k), out int step)) { flight.Column(i, k, lv.J + step, H - step); continue; }
                bool rim = !Plot.In(ci + 0.5, ck + 0.5, r - 1.55, i, k);
                if (rim) { chamber.Column(i, k, lv.J, 4); gallery.Column(i, k); }
                else chamber.Column(i, k);
            }
        foreach (var (i, k) in gallery.Foot)
            foreach (Dir od in Dirs.Horizontal)
            {
                var (oi, ok) = V(od);
                if (chamber.Has(i + oi, k + ok) && chamber.HeightAt(i + oi, k + ok) == H) gallery.Rail.Add((i, k, od));
            }
        var taken = new List<DSpace>();
        bool Try(DSpace s, params DSpace?[] t) { if (!Take(s, lv, t)) return false; taken.Add(s); return true; }
        if (!Try(chamber, entry.S) || !Try(flight, chamber, gallery) || !Try(gallery, chamber, flight))
        {
            foreach (var s in taken) Drop(s, lv);
            Drop(entry.S, lv);
            return false;
        }
        // The flight's lanes run along d; its foot is the column nearest the middle, its lanes from the lower one.
        int laneI = ci + (r - 5) * ui + (di < 0 ? -1 : 0), laneK = ck + (r - 5) * uk + (dk < 0 ? -1 : 0);
        flight.Run = (laneI, laneK, up, 4, 2);
        flight.Ramp = Alien;
        _runs.Add(flight);
        _galleries.Add(gallery);
        Join(entry.S, chamber);
        Join(chamber, flight);
        Join(flight, gallery);
        Join(chamber, gallery);
        chamber.Open = true;
        // Rooms round it: at its four points, off the floor and the gallery.
        foreach (Dir od in Dirs.Horizontal)
        {
            if (od == d.Opposite()) continue;
            var (oi, ok) = V(od);
            var (ol, olk) = Lat(od);
            int pi = ci + r * oi, pk = ck + r * ok;
            foreach (var (host, j) in new[] { (chamber, lv.J), (gallery, gj) })
            {
                if (host == chamber && od == d && R.Chance(0.5f)) continue;
                var s = NewSpace("room", "room", ModuleKind.Room, j, j == gj ? Math.Min(2, H - 4) : 3, lv);
                int depth = R.Between(3, 5), half = R.Between(2, 3);
                for (int t = 2; t < 2 + depth; t++)
                    for (int x = -half; x <= half; x++) s.Column(pi + t * oi + x * ol, pk + t * ok + x * olk);
                if (!Take(s, lv)) continue;
                if (Door(host, s, near: (pi + oi, pk + ok)) == null) { Drop(s, lv); continue; }
                s.Open = true;
            }
        }
        // On out of the far point.
        if (From(lv, chamber, d, R.Between(4, 9), 2, 3, minLen: 3) is { } exit) RoomsAlong(lv, exit, density: 0.6f);
        return true;
    }

    // ---- a long gallery with alcoves --------------------------------------------------------------

    /// <summary>A long gallery, alcoves opening off both sides between piers (stacks, niches, cells), a room across its far end.</summary>
    bool Gallery(Level lv, DSpace a, Dir d)
    {
        int wid = R.Between(3, 4), h = Math.Min(lv.H, Math.Max(3, MainH));
        var w = From(lv, a, d, R.Between(16, 30), wid, h, kind: "gallery", purpose: "gallery", minLen: 8);
        if (w == null) return false;
        int aw = R.Between(2, 3), ad = R.Between(2, 3);
        string alcove = Def.Id switch { "library" => "archive", "prison" => "solitary", "cryo" => "cryo-bay", "fallout" => "quarters", _ => "storage" };
        bool doors = Def.Id is "prison" || R.Chance(0.2f);
        foreach (bool pos in new[] { false, true })
            for (int p = 1; p + aw <= w.Len - 1; p += aw + 1)
            {
                var s = NewSpace("alcove", alcove, ModuleKind.Room, lv.J, Math.Min(h, 2 + (R.Chance(0.3f) ? 1 : 0)), lv);
                for (int t = p; t < p + aw; t++)
                    for (int x = 0; x < ad; x++) { var (ci, ck) = w.At(t, pos ? wid + (doors ? 1 : 0) + x : -1 - (doors ? 1 : 0) - x); s.Column(ci, ck); }
                if (doors)
                {
                    if (!Take(s, lv)) continue;
                    if (Door(w.S, s) == null) { Drop(s, lv); continue; }
                }
                else
                {
                    if (!Take(s, lv, w.S)) continue;
                    Join(w.S, s);
                }
            }
        EndRoom(lv, w, R.Between(5, 8), wid + R.Between(3, 6), mk: ModuleKind.Hall, height: Math.Min(lv.H, h + 1));
        return true;
    }

    // ---- cell blocks round a guarded hub ------------------------------------------------------------

    /// <summary>A guard hub, and cell galleries off its sides: cells either side, a solitary cell at the end; tall levels get a tiered cell hall.</summary>
    bool Cells(Level lv, DSpace a, Dir d)
    {
        var entry = From(lv, a, d, R.Between(2, 4), Math.Max(2, MainW), Math.Min(3, lv.H), minLen: 2);
        if (entry == null) return false;
        int size = R.Between(7, 9);
        var (ei, ek) = entry.At(entry.Len, entry.Wid / 2);
        var (di, dk) = V(d);
        var (li, lk) = Lat(d);
        var hub = NewSpace("hub", "guardroom", ModuleKind.Hall, lv.J, Math.Min(lv.H, 3), lv);
        int r = size / 2;
        int ci = ei + (r + 0) * di, ck = ek + r * dk;
        for (int k = ck - r; k <= ck + r; k++)
            for (int i = ci - r; i <= ci + r; i++)
                if (Plot.In(ci + 0.5, ck + 0.5, r + 0.45, i, k)) hub.Column(i, k);
        if (!Take(hub, lv, entry.S)) { Drop(entry.S, lv); return false; }
        Join(entry.S, hub);
        int blocks = 0;
        foreach (Dir sd in new[] { d, Left(d), Right(d) }.OrderBy(_ => R.Float()))
        {
            var g = From(lv, hub, sd, R.Between(10, 16), R.Between(2, 3), Math.Min(lv.H, 3), kind: "gallery", purpose: "corridor", minLen: 6);
            if (g == null) continue;
            blocks++;
            RoomsAlong(lv, g, density: 0.95f, from: 1, minW: 2, maxW: 2, minD: 3, maxD: 3, purpose: "cellblock", height: 2);
            EndRoom(lv, g, 3, 3, purpose: "solitary", height: 2);
        }
        return blocks > 0;
    }

    // ---- winding drifts and stopes ------------------------------------------------------------------

    /// <summary>
    /// A mine level: a haulage drift winding off the station, branch drifts off it, each to a stope (a ragged
    /// worked-out chamber), stores cut into the rock off the drifts. All raw rock, roofs uneven.
    /// </summary>
    bool Drifts(Level lv, DSpace a, Dir d)
    {
        var main = Wiggle(lv, a, d, R.Between(14, 24), R.Between(2, 3));
        if (main == null) return false;
        var drifts = new List<DSpace> { main };
        int branches = R.Between(3, 5);
        for (int n = 0; n < branches * 3 && drifts.Count < branches + 1; n++)
        {
            var host = R.Pick(drifts);
            Dir bd = R.Pick(Dirs.Horizontal);
            var b = Wiggle(lv, host, bd, R.Between(6, 12), 2, fromAnywhere: true);
            if (b == null) continue;
            drifts.Add(b);
            if (R.Chance(0.75f)) Stope(lv, b, bd);
        }
        // Stores cut into the rock: an ore store, the powder magazine.
        foreach (string room in new[] { "ore-store", "magazine" })
        {
            var host = R.Pick(drifts);
            foreach (Dir sd in Dirs.Horizontal.OrderBy(_ => R.Float()))
            {
                var face = Face(host, sd, lv.J);
                if (face.Count < 2) continue;
                var c = face[face.Count / 2];
                var (di, dk) = V(sd);
                var (li, lk) = Lat(sd);
                var s = NewSpace("room", room, ModuleKind.Room, lv.J, 2, lv);
                s.Raw = true;
                for (int t = 2; t <= 4; t++) for (int x = -1; x <= 1; x++) s.Column(c.I + t * di + x * li, c.K + t * dk + x * lk);
                if (!Take(s, lv)) continue;
                if (Door(host, s, near: (c.I + di, c.K + dk)) == null) { Drop(s, lv); continue; }
                break;
            }
        }
        return true;
    }

    /// <summary>A drift: a raw tunnel winding a block aside now and then, its roof ragged; off a face of its host (or anywhere along it).</summary>
    DSpace? Wiggle(Level lv, DSpace host, Dir d, int len, int wid, bool fromAnywhere = false)
    {
        var face = Face(host, d, lv.J);
        if (face.Count == 0) return null;
        var start = fromAnywhere ? face[R.Range(0, face.Count)] : face[face.Count / 2];
        var (di, dk) = V(d);
        var (li, lk) = Lat(d);
        for (int l = len; l >= 4; l -= 3)
        {
            var s = NewSpace("drift", "drift", ModuleKind.Corridor, lv.J, 2, lv);
            s.Raw = true;
            int off = 0;
            for (int t = 1; t <= l; t++)
            {
                if (t > 2 && t % 3 == 0 && R.Chance(0.55f)) off = Math.Clamp(off + (R.Chance(0.5f) ? 1 : -1), -2, 2);
                for (int x = 0; x < wid; x++)
                {
                    int h = Math.Min(lv.H, 2 + (R.Chance(0.35f) ? 1 : 0));
                    s.Column(start.I + t * di + (x + off - wid / 2) * li, start.K + t * dk + (x + off - wid / 2) * lk, lv.J, h);
                }
            }
            if (!Take(s, lv, host)) continue;
            Join(host, s);
            return s;
        }
        return null;
    }

    /// <summary>A stope at a drift's end: a worked-out chamber, ragged in plan and roof.</summary>
    DSpace? Stope(Level lv, DSpace drift, Dir d)
    {
        var face = Face(drift, d, lv.J);
        if (face.Count == 0) return null;
        var c = face[face.Count / 2];
        var (di, dk) = V(d);
        for (int r = R.Between(3, 5); r >= 2; r--)
        {
            var s = NewSpace("stope", "stope", ModuleKind.Hall, lv.J, 3, lv);
            s.Raw = true;
            int ci = c.I + (r + 1) * di, ck = c.K + (r + 1) * dk;
            float phase = R.Float(0f, 6.28f);
            for (int k = ck - r - 1; k <= ck + r + 1; k++)
                for (int i = ci - r - 1; i <= ci + r + 1; i++)
                {
                    float ang = MathF.Atan2(k - ck, i - ci), dist = MathF.Sqrt((i - ci) * (i - ci) + (k - ck) * (k - ck));
                    float edge = r + 0.6f * MathF.Sin(ang * 3f + phase) + 0.4f * MathF.Sin(ang * 5f - phase);
                    if (dist > edge) continue;
                    int h = Math.Min(lv.H, Math.Max(2, (int)MathF.Round(3 + 2 * (1 - dist / Math.Max(1f, edge))) - (Hash.Cell(new Int3(i, 0, k), 77) % 3 == 0 ? 1 : 0)));
                    s.Column(i, k, lv.J, h);
                }
            if (!s.Has(c.I + di, c.K + dk)) continue;
            if (!Take(s, lv, drift)) continue;
            Join(drift, s);
            return s;
        }
        return null;
    }

    // ---- round chambers on curved ways --------------------------------------------------------------

    /// <summary>
    /// An alien level: round and oval chambers of different sizes, domed, set at no grid's angles, joined by
    /// curved ways (a tree, and a loop or two); the first one opens off the way in.
    /// </summary>
    bool Organic(Level lv, DSpace a, Dir d)
    {
        var face = Face(a, d, lv.J);
        if (face.Count == 0) return false;
        var (di, dk) = V(d);
        var c0 = face[face.Count / 2];
        var chambers = new List<(DSpace S, float I, float K)>();
        int want = R.Between(4, 6);
        // The first chamber just off the way in, joined by a short way.
        for (int tries = 0; tries < want * 10 && chambers.Count < want; tries++)
        {
            float ci, ck;
            if (chambers.Count == 0) { ci = c0.I + di * 7; ck = c0.K + dk * 7; }
            else
            {
                var (_, pi, pk) = R.Pick(chambers);
                float ang = R.Float(0f, 6.28f), dist = R.Float(11f, 17f);
                ci = pi + MathF.Cos(ang) * dist;
                ck = pk + MathF.Sin(ang) * dist;
            }
            var s = Blob(lv, ci, ck, R.Float(3f, 5.5f), R.Float(2.5f, 4.5f), R.Float(0f, 3.14f));
            if (s == null) continue;
            chambers.Add((s, ci, ck));
        }
        if (chambers.Count == 0) return false;
        // The first one joins the way in; the rest join the nearest one already joined, by a curved way.
        bool linked = false;
        foreach (var (s, ci, ck) in chambers.Take(1))
            linked = CurvedWay(lv, a, (c0.I + 0.5f, c0.K + 0.5f), s, (ci, ck));
        if (!linked) { foreach (var (s, _, _) in chambers) Drop(s, lv); return false; }
        var joined = new List<(DSpace S, float I, float K)> { chambers[0] };
        foreach (var ch in chambers.Skip(1))
        {
            bool done = false;
            foreach (var other in joined.OrderBy(o => (o.I - ch.I) * (o.I - ch.I) + (o.K - ch.K) * (o.K - ch.K)))
                if (CurvedWay(lv, other.S, (other.I, other.K), ch.S, (ch.I, ch.K))) { done = true; break; }
            if (done) joined.Add(ch);
            else Drop(ch.S, lv);
        }
        // A loop or two: a second way between two joined chambers.
        for (int n = 0; n < 2 && joined.Count >= 3; n++)
        {
            var x = R.Pick(joined);
            var y = joined.Where(o => o.S != x.S).OrderBy(o => (o.I - x.I) * (o.I - x.I) + (o.K - x.K) * (o.K - x.K)).Skip(1).FirstOrDefault();
            if (y.S != null) CurvedWay(lv, x.S, (x.I, x.K), y.S, (y.I, y.K));
        }
        foreach (var (s, _, _) in joined) s.Open = true;
        return true;
    }

    /// <summary>A domed chamber, an ellipse turned to any angle: its roof highest in the middle.</summary>
    DSpace? Blob(Level lv, float ci, float ck, float rx, float rk, float angle)
    {
        var s = NewSpace("chamber", "room", ModuleKind.Hall, lv.J, lv.H, lv);
        s.Raw = Def.Raw > 0.5f;
        float ca = MathF.Cos(angle), sa = MathF.Sin(angle);
        int reach = (int)MathF.Ceiling(MathF.Max(rx, rk)) + 1;
        for (int k = (int)ck - reach; k <= (int)ck + reach; k++)
            for (int i = (int)ci - reach; i <= (int)ci + reach; i++)
            {
                float x = i + 0.5f - ci, z = k + 0.5f - ck;
                float u = (x * ca + z * sa) / rx, v = (-x * sa + z * ca) / rk;
                float q = u * u + v * v;
                if (q > 1f) continue;
                int h = Math.Clamp((int)MathF.Round(2 + (lv.H - 2) * MathF.Sqrt(1f - q)), 2, lv.H);
                s.Column(i, k, lv.J, h);
            }
        if (s.Columns < 12 || !Take(s, lv)) return null;
        return s;
    }

    /// <summary>A curved way two blocks wide from one chamber to another, bowed to one side; flat-floored, low-roofed.</summary>
    bool CurvedWay(Level lv, DSpace from, (float I, float K) a, DSpace to, (float I, float K) b)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            float bow = R.Float(-0.45f, 0.45f);
            float mx = (a.I + b.I) / 2f + (b.K - a.K) * bow, mk = (a.K + b.K) / 2f - (b.I - a.I) * bow;
            var s = NewSpace("tunnel", "corridor", ModuleKind.Corridor, lv.J, Math.Min(lv.H, 3), lv);
            s.Raw = Def.Raw > 0.5f;
            int steps = (int)(MathF.Sqrt((b.I - a.I) * (b.I - a.I) + (b.K - a.K) * (b.K - a.K)) * 3) + 4;
            for (int n = 0; n <= steps; n++)
            {
                float t = n / (float)steps;
                float x = (1 - t) * (1 - t) * a.I + 2 * (1 - t) * t * mx + t * t * b.I;
                float z = (1 - t) * (1 - t) * a.K + 2 * (1 - t) * t * mk + t * t * b.K;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int i = (int)MathF.Floor(x) + dx, k = (int)MathF.Floor(z) + dz;
                        if ((i + 0.5f - x) * (i + 0.5f - x) + (k + 0.5f - z) * (k + 0.5f - z) > 1.1f) continue;
                        if (from.Has(i, k) || to.Has(i, k) || s.Has(i, k)) continue;
                        if (G.Interior(i, lv.J, k)) goto blocked;
                        s.Column(i, k);
                    }
            }
            if (s.Columns > 0 && Take(s, lv, from, to))
            {
                Join(from, s);
                Join(s, to);
                return true;
            }
            blocked:;
        }
        return false;
    }
}
