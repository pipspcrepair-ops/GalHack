#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The airport's plan (data/place-types.txt "plan airport"; an airstrip's too, with fewer of the parts), laid out as a real
/// regional airport is (reference-photos/airports: two runways crossing in one paved intersection, every end widened,
/// taxiways to a large apron hard against the terminal, rows of hangars on their own aprons, the fire station, fuel and
/// works sheds, car parks, a service road behind it all and the access road curving in): its lengths scaled to our worlds,
/// what a body or a craft uses at true size, every count, size, angle and spot rolled by the spawn.
/// </summary>
public static partial class PlaceTypes
{
    sealed partial class PortPlan
    {
        /// <summary>The landside, +z or -z: the plan works in x along the main runway and v metres out to the landside.</summary>
        int _s = 1;
        (float X, float Z) At(float x, float v) => (x, _s * v);
        SketchBox Box(float x, float v, float w, float d) => Around(x, _s * v, w, d);
        /// <summary>How far a box reaches toward the landside, m.</summary>
        float Back(SketchBox b) => _s > 0 ? b.Z1 : -b.Z0;

        /// <summary>Whether this spawn has a part: its type has it and its chance came up (always, where shots and tests ask).</summary>
        bool Has(string id, Rng r) => Part(id) is { } p && (!p.Size.TryGetValue("chance", out Span c) || Always.Contains(id) || r.Chance(c.Lo));

        /// <summary>Clear of the runways, their widened ends and their approach lights by <paramref name="clear"/>.</summary>
        bool OffRunways(Shape sh, float clear) => S.Items.Where(i => i.Part.Id is "runway" or "crosswind" or "end" || i.Part.Kind == "lights").All(i => !i.Outline.Hits(sh, clear));

        /// <summary>
        /// The main runway along x through the middle; in most spawns a second one meeting it in one paved intersection;
        /// every free end widened past its threshold, approach lights off those square to the axes. On the landside a
        /// parallel taxiway linked to the runway near its ends (or, without one, stubs from each apron); along it the apron
        /// line from the end away from the second runway: the terminal's apron with the terminal, its tower and the big
        /// hangars behind it, the GA apron and its row of small hangars, the fire station on the taxiway, in a rolled order;
        /// car parks behind the terminal, the service road behind everything, works sheds and the fuel farm off it, and the
        /// access road out from it round a bend to the gate.
        /// </summary>
        public void Airport()
        {
            Rng r = _r.Fork("airport");
            _s = r.Chance(0.5f) ? 1 : -1;
            int e = r.Chance(0.5f) ? 1 : -1;
            TypePart runway = Part("runway")!;
            var rs = Sizes(runway, r.Fork("runway"));
            float len = rs["len"], half = rs["w"] / 2f + rs.GetValueOrDefault("shoulder");
            Lay(runway, Shape.Strip(-len / 2f, 0f, len / 2f, 0f, half), rs);
            var ends = new List<(float X, float Z, float UX, float UZ, float Half, int Layer)> { (-len / 2f, 0f, -1f, 0f, half, 8), (len / 2f, 0f, 1f, 0f, half, 8) };
            if (Has("crosswind", r)) Crosswind(Part("crosswind")!, len, half, e, r.Fork("crosswind"), ends);
            foreach (var (x, z, ux, uz, h, layer) in ends)
            {
                float past = 0f;
                if (Part("end") is { } end)
                {
                    // Paved on past the threshold and wider than the runway, reaching back over its last stretch.
                    var es = Sizes(end, r.Fork($"end{x:F0},{z:F0}"));
                    past = es["len"];
                    es["layer"] = layer;
                    Lay(end, Shape.Strip(x - ux * 30f, z - uz * 30f, x + ux * past, z + uz * past, MathF.Max(es["w"] / 2f, h + 3f)), es);
                }
                if (Part("lights") is { } lights && (ux == 0f || uz == 0f))
                {
                    var ls = Sizes(lights, r.Fork($"lights{x:F0},{z:F0}"));
                    float w = MathF.Min(2f * h, 30f), a = (ux != 0f ? x : z) + (ux + uz) * past, b = a + (ux + uz) * ls["len"];
                    SketchBox box = ux != 0f ? new SketchBox(MathF.Min(a, b), z - w / 2f, MathF.Max(a, b), z + w / 2f) : new SketchBox(x - w / 2f, MathF.Min(a, b), x + w / 2f, MathF.Max(a, b));
                    Add(lights, box, 0f, (int)ux, (int)uz, ls);
                }
            }
            // The taxiway: parallel on the landside, linked to the runway near both its ends, and on along the apron line
            // where that runs on past them.
            TypePart? tp = Has("taxiway", r) ? Part("taxiway") : null;
            var ts = tp != null ? Sizes(tp, r.Fork("taxiway")) : null;
            float taxi = ts?["off"] ?? -1f, line = ts != null ? taxi + ts["w"] / 2f : r.Float(50f, 70f);
            var (lo, hi) = ApronLine(len, line, taxi, e, r.Fork("line"));
            if (tp == null || ts == null) return;
            float t0 = -len / 2f + r.Float(10f, 40f), t1 = len / 2f - r.Float(10f, 40f);
            var (ax, az) = At(MathF.Min(t0, lo + 10f), taxi);
            var (bx, bz) = At(MathF.Max(t1, hi - 10f), taxi);
            Lay(tp, Shape.Strip(ax, az, bx, bz, ts["w"] / 2f), ts);
            Stub(Part("link"), t0, taxi, 0f);
            Stub(Part("link"), t1, taxi, 0f);
        }

        /// <summary>
        /// The second runway, meeting the main toward its end e in one paved intersection: an X (both run on through it, each
        /// crossed a rolled way along) or a T (its end across the main's whole laid width, near square to it); its landside
        /// arm leans toward that end, away from the apron line. Its free ends join the ends to widen.
        /// </summary>
        void Crosswind(TypePart p, float main, float mainHalf, int e, Rng r, List<(float X, float Z, float UX, float UZ, float Half, int Layer)> ends)
        {
            var xs = Sizes(p, r);
            float len = xs["len"], half = xs["w"] / 2f + xs.GetValueOrDefault("shoulder");
            bool tee = r.Chance(0.4f), square = r.Chance(0.3f), stemLand = r.Chance(0.4f);
            float a = (square ? 90f : tee ? r.Float(78f, 89f) : r.Float(50f, 85f)) * MathF.PI / 180f;
            // The way its landside arm runs in (x, v): exactly square where it is (its lights stand square to the axes).
            float ux = square ? 0f : e * MathF.Cos(a), uv = square ? 1f : MathF.Sin(a);
            // The junction well clear of the main's widened end.
            float xj = e * MathF.Min(r.Float(0.25f, 0.38f) * main, main / 2f - 40f - (half + 3f) / uv);
            // Its reach either side of the junction: a T's end on the main's far edge.
            float through = mainHalf / uv, land = tee ? (stemLand ? len - through : through) : len * r.Float(0.3f, 0.5f), air = len - land;
            var (ax, az) = At(xj - ux * air, -uv * air);
            var (bx, bz) = At(xj + ux * land, uv * land);
            xs["tee"] = tee ? 1f : 0f;
            Lay(p, Shape.Strip(ax, az, bx, bz, half), xs);
            if (!tee || stemLand) ends.Add((bx, bz, ux, _s * uv, half, 7));
            if (!tee || !stemLand) ends.Add((ax, az, -ux, -_s * uv, half, 7));
        }

        /// <summary>A link from v out on the landside at x to the runway, slanting by <paramref name="slant"/> along it; left out where it would cross the second runway.</summary>
        void Stub(TypePart? p, float x, float v, float slant)
        {
            if (p == null) return;
            var (ax, az) = At(x, v);
            var (bx, bz) = At(x + slant, 0f);
            Shape sh = Shape.Strip(ax, az, bx, bz, p.Get("w", 15f).Lo / 2f);
            if (S.Items.Any(i => (i.Part.Id == "crosswind" || i.Part.Kind == "lights") && i.Outline.Hits(sh, 4f))) return;
            Lay(p, sh, Sizes(p, _r.Fork("link" + S.Items.Count)));
        }

        /// <summary>
        /// The apron line along the landside from the main runway's end away from the second runway, ending before the second
        /// runway (the GA apron shortened where it would not, then the whole line begun further out); behind it the car parks
        /// and the service road, off which the works sheds, the fuel farm and the access road. Returns how far along x it runs.
        /// </summary>
        (float Lo, float Hi) ApronLine(float len, float line, float taxi, int e, Rng r)
        {
            var blocks = new List<string> { "apron" };
            foreach (string b in new[] { "ga", "fire" }) if (Part(b) != null) blocks.Add(b);
            r.Shuffle(blocks);
            var sizes = blocks.ToDictionary(b => b, b => Sizes(Part(b)!, r.Fork(b)));
            // How deep each block reaches back from the line: an apron and what stands behind it, or the fire station itself.
            float Deep(string b) => b == "fire" ? sizes[b]["depth"] + 4f : sizes[b]["depth"] + 3f + (b == "apron" ? new[] { "terminal", "hangar", "tower" } : new[] { "gahangar" }).Max(q => Part(q)?.Get("depth", 0f).Hi ?? 0f);
            float deepest = blocks.Max(Deep), gap = r.Float(10f, 22f);
            float start = -e * (len / 2f + r.Float(0f, 30f)), room = 0f;
            // The room before the second runway (or the main's other end): the first probe along the line that isn't clear.
            for (; room < len + 80f; room += 8f)
                if (!OffRunways(Shape.Of(Box(start + e * (room + 4f), line + 1f + deepest / 2f, 8f, deepest)), 12f)) break;
            // Too long for it: the GA apron shortened (to its least), then all of it begun further out past the runway's end.
            float Total() => blocks.Sum(b => sizes[b]["len"]) + gap * (blocks.Count - 1);
            if (blocks.Contains("ga")) sizes["ga"]["len"] = MathF.Max(Part("ga")!.Get("len", 60f).Lo, sizes["ga"]["len"] - MathF.Max(0f, Total() - room));
            float edge = start - e * MathF.Max(0f, Total() - room);
            SketchItem? terminal = null;
            foreach (string b in blocks)
            {
                float w = sizes[b]["len"], cx = edge + e * w / 2f;
                edge += e * (w + gap);
                var br = r.Fork("at" + b);
                switch (b)
                {
                    case "apron": terminal = MainApron(cx, line, taxi, sizes[b], br); break;
                    case "ga": GaApron(cx, line, taxi, sizes[b], br); break;
                    default:
                        if (Building(Part(b)!, cx, _s * (line + 5f + sizes[b]["depth"] / 2f), 0, -_s, br, 2f, sizes[b]) != null && taxi < 0f) Stub(Part("link"), cx, line, 0f);
                        break;
                }
            }
            Landside(terminal, line, e, r.Fork("landside"));
            var aprons = S.Items.Where(i => i.Part.Id is "apron" or "ga" or "fire").ToList();
            return (aprons.Min(i => i.Box.X0), aprons.Max(i => i.Box.X1));
        }

        /// <summary>
        /// The terminal's apron hard against the taxiway (or stubs from it to the runway, fanning out), the terminal behind it
        /// with its front on it, the tower at one of its ends, the big hangars along the rest of the apron's back.
        /// </summary>
        SketchItem? MainApron(float cx, float line, float taxi, Dictionary<string, float> sizes, Rng r)
        {
            float w = sizes["len"], d = sizes["depth"];
            SketchItem apron = Lay(Part("apron")!, Shape.Of(Box(cx, line + d / 2f, w, d)), sizes);
            SketchBox ab = apron.Box;
            if (taxi >= 0f) Stub(Part("link"), cx + r.Float(-0.25f, 0.25f) * w, taxi, 0f);
            else foreach (int side in new[] { -1, 1 }) Stub(Part("link"), cx + side * (w / 2f - 20f), line, side * r.Float(0f, 0.7f) * line);
            // The terminal somewhere along the apron's back.
            TypePart tp = Part("terminal")!;
            var ts = Sizes(tp, r.Fork("terminal"));
            float back = Back(ab) + 3f, tx = cx + r.Float(-0.6f, 0.6f) * MathF.Max(0f, w - ts["len"] - 40f) / 2f;
            SketchItem? terminal = Building(tp, tx, _s * (back + ts["depth"] / 2f), 0, -_s, r, 0.5f, ts);
            if (terminal == null) return null;
            // The tower beside it, its foot on the apron's edge too; the big hangars outward from both, as far as the apron goes.
            float lo = terminal.Box.X0, hi = terminal.Box.X1;
            if (Part("tower") is { } tower)
            {
                var ws = Sizes(tower, r.Fork("tower"));
                int side = r.Chance(0.5f) ? 1 : -1;
                foreach (int s in new[] { side, -side })
                    if (Building(tower, s > 0 ? hi + 8f + ws["len"] / 2f : lo - 8f - ws["len"] / 2f, _s * (back + ws["depth"] / 2f), 0, -_s, r, 0.5f, ws) is { } t)
                    {
                        (lo, hi) = (MathF.Min(lo, t.Box.X0), MathF.Max(hi, t.Box.X1));
                        break;
                    }
            }
            if (Part("hangar") is { } hangar)
            {
                int count = Count(hangar, r.Fork("hangars"));
                for (int k = 0; k < count; k++)
                {
                    var hs = Sizes(hangar, r.Fork("hangar" + k));
                    int s = (k % 2 == 0) == r.Chance(0.5f) ? 1 : -1;
                    foreach (int way in new[] { s, -s })
                    {
                        float hx = way > 0 ? hi + r.Float(6f, 14f) + hs["len"] / 2f : lo - r.Float(6f, 14f) - hs["len"] / 2f;
                        // (Its door on the apron: no further past its end than a quarter of its own width.)
                        if (hx - hs["len"] / 4f > ab.X1 || hx + hs["len"] / 4f < ab.X0) continue;
                        if (Building(hangar, hx, _s * (back + hs["depth"] / 2f), 0, -_s, r, 0.5f, hs) is not { } h) continue;
                        (lo, hi) = (MathF.Min(lo, h.Box.X0), MathF.Max(hi, h.Box.X1));
                        break;
                    }
                }
            }
            return terminal;
        }

        /// <summary>The GA apron and the row of small hangars along its back, their doors on it.</summary>
        void GaApron(float cx, float line, float taxi, Dictionary<string, float> sizes, Rng r)
        {
            float w = sizes["len"], d = sizes["depth"];
            SketchItem apron = Lay(Part("ga")!, Shape.Of(Box(cx, line + d / 2f, w, d)), sizes);
            if (taxi < 0f) Stub(Part("link"), cx, line, r.Float(-0.5f, 0.5f) * line);
            if (Part("gahangar") is not { } hangar) return;
            int most = Count(hangar, r.Fork("count"));
            float x = apron.Box.X0 + r.Float(0f, 8f);
            for (int k = 0; k < most; k++)
            {
                var hs = Sizes(hangar, r.Fork("ga" + k));
                if (x + hs["len"] > apron.Box.X1) break;
                Building(hangar, x + hs["len"] / 2f, _s * (Back(apron.Box) + 3f + hs["depth"] / 2f), 0, -_s, r, 0.5f, hs);
                x += hs["len"] + r.Float(4f, 10f);
            }
        }

        /// <summary>
        /// Behind the apron line: car parks behind the terminal, the service road along the back of it all (short of the
        /// second runway), the works sheds across it facing it, the fuel farm off the line's end well away from the
        /// terminal, and the access road out from the road by the terminal round a bend toward the gate.
        /// </summary>
        void Landside(SketchItem? terminal, float line, int e, Rng r)
        {
            var built = S.Items.Where(i => i.Part.Id is not ("runway" or "crosswind" or "end" or "taxiway" or "link") && i.Part.Kind != "lights").ToList();
            if (terminal != null && Part("carpark") is { } carpark)
            {
                int count = Count(carpark, r.Fork("carparks"));
                float along = 0f;
                for (int k = 0; k < count; k++)
                {
                    var cs = Sizes(carpark, r.Fork("carpark" + k));
                    float x = terminal.Box.Cx + (k % 2 == 0 ? 1 : -1) * along;
                    SketchBox b = Box(x, Back(terminal.Box) + r.Float(6f, 12f) + cs["depth"] / 2f, cs["len"], cs["depth"]);
                    if (Free(Shape.Of(b), false, 3f)) built.Add(Lay(carpark, Shape.Of(b), cs));
                    if (k % 2 == 0) along += cs["len"] / 2f + 6f + carpark.Get("len", 60f).Hi / 2f;
                }
            }
            if (built.Count == 0) return;
            // The service road along the back of it all, flush behind it, ended short of the second runway.
            TypePart? road = Part("road");
            float rw = road?.Get("w", 7f).Hi ?? 7f, rv = built.Max(i => Back(i.Box)) + rw / 2f + 1f;
            float x0 = built.Min(i => i.Box.X0) - 10f, x1 = built.Max(i => i.Box.X1) + 10f;
            Shape RoadAt(float a, float b) => Shape.Run(a, _s * rv, b, _s * rv, rw / 2f);
            while (x1 - x0 > 40f && !OffRunways(RoadAt(x0, x1), 8f)) { if (e > 0) x1 -= 8f; else x0 += 8f; }
            if (road != null) Lay(road, RoadAt(x0, x1), Sizes(road, r.Fork("road")));
            // Across it: the works sheds, their yards on it.
            if (Part("works") is { } works)
            {
                int count = Count(works, r.Fork("works"));
                float x = e > 0 ? x0 + 10f : x1 - 10f;
                for (int k = 0; k < count; k++)
                {
                    var ws = Sizes(works, r.Fork("works" + k));
                    for (; e > 0 ? x + ws["len"] < x1 : x - ws["len"] > x0; x += e * 8f)
                        if (Building(works, x + e * ws["len"] / 2f, _s * (rv + rw / 2f + 6f + ws["depth"] / 2f), 0, -_s, r, 3f, ws) is { } done)
                        {
                            x = e > 0 ? done.Box.X1 + r.Float(10f, 30f) : done.Box.X0 - r.Float(10f, 30f);
                            break;
                        }
                }
            }
            // The fuel farm off the end of the line away from the terminal, a hundred metres from it at least, by the road.
            if (Part("fuel") is { } fuel)
            {
                float from = terminal?.Box.Cx ?? 0f;
                bool lowEnd = MathF.Abs(x0 - from) > MathF.Abs(x1 - from);
                for (float t = 0f; t < 200f; t += 8f)
                {
                    float fx = lowEnd ? x0 - t : x1 + t;
                    if (terminal != null && MathF.Abs(fx - terminal.Box.Cx) < terminal.Box.W / 2f + 130f) continue;
                    if (Tanks(fuel, fx, _s * (rv - 30f), r) != null) break;
                }
            }
            // The access road: out from the service road near the terminal, round a bend, then straight on to the gate.
            if (Part("access") is not { } access) return;
            float aw = access.Get("w", 8f).Hi / 2f, ax = Math.Clamp(terminal?.Box.Cx ?? (x0 + x1) / 2f, x0 + 20f, x1 - 20f), bend = r.Float(25f, 45f) * MathF.PI / 180f;
            for (int k = 0; k < 9; k++)
            {
                float x = ax + (k % 2 == 0 ? 1 : -1) * (k + 1) / 2 * 16f, v1 = rv + r.Float(10f, 20f), run = r.Float(30f, 60f);
                Shape first = Shape.Run(x, _s * rv, x, _s * v1, aw);
                if (x < x0 || x > x1 || !Free(first, true, 2f)) continue;
                foreach (int side in new[] { -e, e, 0 })
                {
                    float bx = x + side * MathF.Sin(bend) * run, bv = v1 + (side == 0 ? run : MathF.Cos(bend) * run);
                    Shape second = Shape.Run(x, _s * v1, bx, _s * bv, aw);
                    if (!Free(second, true, 2f) || !OffRunways(second, 10f)) continue;
                    Lay(access, first, Sizes(access, r.Fork("run0")));
                    Lay(access, second, Sizes(access, r.Fork("run1")));
                    Road(bx, _s * bv, 0f, _s);
                    return;
                }
            }
            Road(ax, _s * rv, 0f, _s);
        }
    }
}
