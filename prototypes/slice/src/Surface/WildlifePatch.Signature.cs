using System;
using System.Collections.Generic;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Wildlife;
using Rng = Wildlife.Rng;

namespace Slice.Surface;

/// <summary>
/// The world's own kinds (what its fall, theme and era left walking it: <see cref="WorldSignature"/>)
/// at their haunts as you go: the Changed clumped round town edges and ruins, soldiers and machines
/// patrolling the roads, pilgrims walking them, scavengers picking at ruins, frenzied packs out in the
/// open. A few groups are kept within reach of you, out of your sight, more by night for the kinds
/// that come out then; each keeps to where it was put (a patrol walks its road), so something of the
/// world's own turns up every few minutes of walking, wherever the land is.
/// </summary>
public partial class WildlifePatch
{
    /// <summary>
    /// A world's story for the bestiary, from its roll: how it fell (and the fall's group), its
    /// theme, its creeds and presences, whether anyone lives on it now, its eras and its paint.
    /// </summary>
    public static void Story(WorldConditions w, WorldRoll roll, WorldProfile profile, WorldReading reading)
    {
        if (roll != null)
        {
            if (roll.One(LayerKind.Fall) is { } fall)
            {
                w.Fall = fall.Id;
                w.FallGroup = fall.Group ?? "";
            }
            w.Theme = roll.Theme?.Id ?? "";
            foreach (WorldLayer l in roll.Rolled)
                if (l.Kind is LayerKind.Creed or LayerKind.Presence) w.Story.Add($"{l.Kind.ToString().ToLowerInvariant()}.{l.Id}");
            w.Peopled = roll.Fact("people") != "none";
        }
        if (profile != null)
        {
            w.Before = (int)profile.Before;
            w.Now = (int)profile.Now;
            if (!profile.Inhabited) w.Peopled = false;
        }
        if (reading?.Paint is { } paint)
            foreach (Rgb c in new[] { paint.Main, paint.Accent, paint.Roof, paint.Cloth, paint.Light })
                w.Paint.Add(new Color(c.R / 255f, c.G / 255f, c.B / 255f).Lerp(new Color(0.55f, 0.53f, 0.5f), Math.Clamp(paint.Fade * 0.5f, 0f, 0.5f)));
    }

    /// <summary>Where the world's own kinds keep to, by kind of ground: round living places, round ruins, along its roads.</summary>
    sealed class Anchors
    {
        public readonly List<(Vector3 At, float Radius)> Town = new(), Ruins = new();
        /// <summary>Each road as its points, 4 m apart (zone frame, canonical).</summary>
        public readonly List<List<Vector3>> Roads = new();
    }

    Anchors _anchors;

    /// <summary>A patrol: a herd walking a road, its home moved on along it as it gets there.</summary>
    sealed class Patrol
    {
        public Herd Herd;
        public List<Vector3> Road;
        public int At, Step;
    }

    readonly List<Patrol> _patrols = new();
    float _ownClock;
    long _ownRolls;
    /// <summary>Where you were at the last look, and the way you are going (patch frame, flat).</summary>
    Vector3 _ownLast, _heading;

    /// <summary>What the world's own haunts and homes put down: kept apart from the wild's numbers, never let go to make room for animals.</summary>
    readonly HashSet<Creature> _kept = new();

    bool Kept(Creature c) => _kept.Contains(c);

    void Keep(List<Creature> group)
    {
        if (group == null) return;
        foreach (Creature c in group) _kept.Add(c);
        if (_kept.Count > 400) _kept.RemoveWhere(c => c.Removed || c.Dead);
    }

    /// <summary>How far the nearest haunt of each kind is from you, m (tests).</summary>
    public string NearestHaunts()
    {
        _anchors ??= MakeAnchors();
        Vector3 you = FromPatch(Player.Position);
        float Near(IEnumerable<Vector3> at) { float best = float.MaxValue; foreach (Vector3 a in at) best = MathF.Min(best, WildSim.Flat(_planet.NearestCopy(a, you) - you).Length()); return best; }
        var roads = new List<Vector3>();
        foreach (List<Vector3> r in _anchors.Roads) roads.AddRange(r);
        return $"town {Near(_anchors.Town.ConvertAll(t => t.At)):F0} m, ruins {Near(_anchors.Ruins.ConvertAll(t => t.At)):F0} m, road {Near(roads):F0} m";
    }

    /// <summary>The world's anchors for its own kinds, counted (tests).</summary>
    public string AnchorCounts
    {
        get
        {
            _anchors ??= MakeAnchors();
            return $"{_anchors.Town.Count} living places, {_anchors.Ruins.Count} ruins, {_anchors.Roads.Count} roads";
        }
    }

    /// <summary>Groups of the world's own kinds brought to their haunts so far, by kind of haunt (tests).</summary>
    public readonly Dictionary<string, int> OwnBrought = new();

    Anchors MakeAnchors()
    {
        var a = new Anchors();
        if (_planet == null) return a;
        foreach (PoiSite site in _planet.Pois)
        {
            if (site.Instance == null) continue;
            var lo = site.Footprint.Min;
            var hi = site.Footprint.Max;
            float hx = (hi.X - lo.X + 1) * 0.5f, hz = (hi.Z - lo.Z + 1) * 0.5f;
            var at = new Vector3((lo.X + hi.X + 1) * 0.5f, 0f, (lo.Z + hi.Z + 1) * 0.5f);
            float r = MathF.Sqrt(hx * hx + hz * hz);
            bool living = site.Instance.State == PoiState.Active || site.Family.Tags.Contains("settlement");
            (living ? a.Town : a.Ruins).Add((at, r));
        }
        if (_planet.Infra is { } net)
        {
            foreach (var ruin in net.Ruins)
            {
                var (cx, cz) = ruin.Center;
                a.Ruins.Add((new Vector3((float)cx, 0f, (float)cz), 10f));
            }
            // A wreck is picked over like a ruin: the smallest circle round its footprint.
            foreach (var w in net.Wrecks)
            {
                var (wx, wz) = w.World(w.Width * 0.5f, w.Length * 0.5f);
                a.Ruins.Add((new Vector3((float)wx, 0f, (float)wz), MathF.Sqrt(w.Width * w.Width + w.Length * w.Length) * 0.5f));
            }
            foreach (var lot in net.Lots)
                a.Ruins.Add((new Vector3((float)((lot.X0 + lot.X1) * 0.5), 0f, (float)((lot.Z0 + lot.Z1) * 0.5)), 14f));
            foreach (var route in net.Routes)
            {
                if (route.Kind == PlanetSurface.Infra.RouteKind.Rail || route.X.Length < 4) continue;
                var road = new List<Vector3>(route.X.Length / 5 + 1);
                for (int i = 0; i < route.X.Length; i += 5) road.Add(new Vector3((float)route.X[i], 0f, (float)route.Z[i]));
                a.Roads.Add(road);
            }
        }
        return a;
    }

    /// <summary>The world's own kinds alive near you (patch frame).</summary>
    int OwnNear(Vector3 you, WorldSignature sig)
    {
        int n = 0;
        foreach (Creature c in Sim.Creatures)
            if (!c.Removed && !c.Dead && sig.Is(c.Species) && WildSim.Flat(c.Position - you).Length() < AroundYou) n++;
        return n;
    }

    /// <summary>
    /// Every few seconds, once you have moved on: if fewer of the world's own kinds than it keeps are
    /// within reach of you, a group of one (by its weight) comes to one of its haunts near you (by the
    /// kind's own leaning, among those there are), out of your sight, and keeps to it.
    /// </summary>
    void Own(float dt)
    {
        if (_frozen || Player == null || Life == null || Sim == null) return;
        if ((_ownClock += dt) < 3f) return;
        _ownClock = 0f;
        StepPatrols();
        Homes();
        Ruins();
        WorldSignature sig = Life.Signature;
        if (sig.Empty) return;
        SignatureTables t = Life.Data.Signature;
        Vector3 you = Player.Position;
        // The way you are going: the world's own are met ahead of you, coming into sight as you go.
        Vector3 moved = WildSim.Flat(you - _ownLast);
        if (moved.Length() > 2f) _heading = moved.Normalized();
        else if (_heading == Vector3.Zero) _heading = WildSim.Flat(Player.Forward).Normalized();
        _ownLast = you;
        bool night = Sim.Clock.IsNight;
        float total = 0f, groupSum = 0f, nightSum = 0f;
        foreach (var k in sig.Kinds)
        {
            total += k.Weight;
            groupSum += k.Weight * (k.Species.GroupMin + k.Species.GroupMax) * 0.5f;
            nightSum += k.Weight * k.Kind.Night;
        }
        if (total <= 0f) return;
        float want = t.Want * groupSum / total * (night ? nightSum / total : 1f);
        if (OwnNear(you, sig) >= want) return;
        var rng = Rng.For(Life.Seed, "own", ++_ownRolls);
        float pick = rng.Range(0f, total);
        var kind = sig.Kinds[^1];
        foreach (var k in sig.Kinds)
            if ((pick -= k.Weight) <= 0f) { kind = k; break; }
        _anchors ??= MakeAnchors();
        Species s = kind.Species;
        int count = rng.Int(s.GroupMin, s.GroupMax);
        // Its haunts near you, by the kind's own leaning; the open always is.
        Vector3 youZone = FromPatch(you);
        var choices = new List<(string Haunt, float W)>();
        foreach (var (haunt, w) in kind.Kind.Haunts)
            if (w > 0f && (haunt == "open" || NearestAnchor(haunt, youZone, rng, true) != null)) choices.Add((haunt, w));
        if (choices.Count == 0) choices.Add(("open", 1f));
        float sum = 0f;
        foreach (var c in choices) sum += c.W;
        float roll = rng.Range(0f, sum);
        string where = choices[^1].Haunt;
        foreach (var c in choices)
            if ((roll -= c.W) <= 0f) { where = c.Haunt; break; }
        List<Creature> group = null;
        if (where == "open")
        {
            // Out in the open, ahead of you beyond what you can make out, and on their way somewhere across
            // the way you are going: travellers, a pack out ranging, a clump drifting. You meet on the way.
            // Off to one side ahead, out of your eye's reach, heading for a point on your way: you cross paths.
            if (_heading != Vector3.Zero)
                for (int attempt = 0; attempt < 10 && group == null; attempt++)
                {
                    float side = rng.Chance(0.5f) ? 1f : -1f;
                    Vector3 d = attempt < 6 ? _heading.Rotated(Vector3.Up, side * rng.Range(1.25f, 1.55f)) : _heading.Rotated(Vector3.Up, rng.Range(-0.7f, 0.7f));
                    float r = attempt < 6 ? rng.Range(110f, 170f) * AroundYou / SpawnReach.Old : rng.Range(Sim.Data.Spawning.ViewDistance + 6f, AroundYou - 4f);
                    group = PutAt(s, count, you + d * r, 0f, 4f, rng);
                }
            if (group is { Count: > 0 } && _heading != Vector3.Zero)
            {
                Vector3 bound = you + _heading.Rotated(Vector3.Up, rng.Range(-0.3f, 0.3f)) * rng.Range(60f, 100f);
                if (Open(s, bound, 0f)) Toward(group, bound);
            }
            group ??= BringBackGroup(s, count, you, rng);
        }
        else if (NearestAnchor(where, youZone, rng, false) is { } anchor)
        {
            Vector3 at = PatchLocal(_planet.NearestCopy(anchor.At, youZone));
            // Round a living place, outside its streets (the wild keeps out of a town's ground); round a ruin, close in.
            group = where == "road" ? PutAt(s, count, at, 0f, 8f, rng)
                : where == "town" ? PutAt(s, count, at, anchor.Radius + 40f, anchor.Radius + 75f, rng)
                : PutAt(s, count, at, anchor.Radius + 4f, anchor.Radius + 28f, rng);
            if (group != null && where == "road" && anchor.Road != null && group.Count > 0 && group[0].Herd is { } herd)
                _patrols.Add(new Patrol { Herd = herd, Road = anchor.Road, At = anchor.Index, Step = rng.Chance(0.5f) ? 3 : -3 });
        }
        if (group == null || group.Count == 0) return;
        // Out and about now; only those with hours of their own go to rest at theirs.
        if (s.Activity != Activity.Cathemeral) Sim.AssignRest(group);
        Keep(group);
        OwnBrought[where] = OwnBrought.GetValueOrDefault(where) + 1;
    }

    sealed class Anchor
    {
        public Vector3 At;
        public float Radius;
        public List<Vector3> Road;
        public int Index;
    }

    /// <summary>A haunt of a kind within reach of you but not on top of you (zone frame), or null: one ahead of you if there is one, by chance.</summary>
    Anchor NearestAnchor(string haunt, Vector3 youZone, Rng rng, bool any)
    {
        float min = Sim.Data.Spawning.MinPlayerDistance, max = AroundYou - 5f;
        var found = new List<Anchor>();
        if (haunt is "town" or "ruins")
        {
            foreach (var (at, r) in haunt == "town" ? _anchors.Town : _anchors.Ruins)
            {
                float d = WildSim.Flat(_planet.NearestCopy(at, youZone) - youZone).Length() - r;
                if (d > min * 0.5f && d < max) found.Add(new Anchor { At = at, Radius = r });
            }
        }
        else if (haunt == "road")
        {
            foreach (List<Vector3> road in _anchors.Roads)
                for (int i = 0; i < road.Count; i += 4)
                {
                    float d = WildSim.Flat(_planet.NearestCopy(road[i], youZone) - youZone).Length();
                    if (d > min && d < max) { found.Add(new Anchor { At = road[i], Radius = 0f, Road = road, Index = i }); i += 20; }
                }
        }
        if (found.Count == 0) return null;
        if (any) return found[0];
        // Ahead of you first: what you will walk up to.
        var ahead = found.FindAll(a => _heading != Vector3.Zero && _heading.Dot(WildSim.Flat(_planet.NearestCopy(a.At, youZone) - youZone).Normalized()) > 0.35f);
        var from = ahead.Count > 0 ? ahead : found;
        return from[rng.Int(0, from.Count - 1)];
    }

    /// <summary>A group put down round you, out of your way and sight, on ground that suits it (the refill's way); the group, or null.</summary>
    List<Creature> BringBackGroup(Species s, int count, Vector3 you, Rng rng)
    {
        SpawnRules spawning = Sim.Data.Spawning;
        float spread = Sim.GroupRadius(s, count), edge = Sim.Terrain.HalfSize - 30f;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float a = rng.Range(0f, MathF.Tau), r = rng.Range(spawning.MinPlayerDistance + spread, MathF.Max(spawning.MinPlayerDistance + spread + 20f, AroundYou - spread));
            var at = new Vector3(you.X + MathF.Cos(a) * r, 0f, you.Z + MathF.Sin(a) * r);
            if (MathF.Abs(at.X) > edge || MathF.Abs(at.Z) > edge || !Open(s, at, spread) || !Clear(at, spread)) continue;
            Vector3 spot = Sim.FreeSpot(at, rng);
            if (!s.Flies && Sim.Nav.Blocked(spot)) continue;
            List<Creature> group = Sim.SpawnGroup(s, spot, count, p => Clear(p, 0f) && Open(s, p, 0f));
            if (group.Count > 0) return group;
        }
        return null;
    }

    /// <summary>A group sent on its way to a point (patch frame): a herd's or a pack's home moved there, or each one's own.</summary>
    static void Toward(List<Creature> group, Vector3 at)
    {
        if (group[0].Herd is { } herd) { herd.Home = at; herd.Goal = at; herd.GoalTimer = 30f; }
        if (group[0].Pack is { } pack) pack.Home = at;
        foreach (Creature c in group) c.Home = at;
    }

    /// <summary>Each patrol's herd, once at its waypoint, is sent on to the next along its road (back the way it came at an end).</summary>
    void StepPatrols()
    {
        Vector3 youZone = FromPatch(Player.Position);
        for (int i = _patrols.Count - 1; i >= 0; i--)
        {
            Patrol p = _patrols[i];
            if (p.Herd.Members.TrueForAll(m => m.Dead || m.Removed)) { _patrols.RemoveAt(i); continue; }
            Vector3 home = PatchLocal(_planet.NearestCopy(p.Road[p.At], youZone));
            if (WildSim.Flat(p.Herd.Center - home).Length() > 9f)
            {
                // On the way (or turned aside by a fright): its waypoint is still where it is going.
                if (WildSim.Flat(p.Herd.Home - home).Length() > 1f && !p.Herd.Alarmed && p.Herd.Hunting == null)
                {
                    p.Herd.Home = home;
                    p.Herd.Goal = home;
                    p.Herd.GoalTimer = 20f;
                }
                continue;
            }
            int next = p.At + p.Step;
            if (next < 0 || next >= p.Road.Count) { p.Step = -p.Step; next = Math.Clamp(p.At + p.Step, 0, p.Road.Count - 1); }
            p.At = next;
            p.Herd.Home = PatchLocal(_planet.NearestCopy(p.Road[p.At], youZone));
            p.Herd.Goal = p.Herd.Home;
            p.Herd.GoalTimer = 20f;
        }
    }

    /// <summary>A new patch took over: its patrols' herds are the new ones.</summary>
    void RemapPatrols(Dictionary<Herd, Herd> herds)
    {
        for (int i = _patrols.Count - 1; i >= 0; i--)
        {
            if (herds.TryGetValue(_patrols[i].Herd, out Herd now)) _patrols[i].Herd = now;
            else _patrols.RemoveAt(i);
        }
    }
}
