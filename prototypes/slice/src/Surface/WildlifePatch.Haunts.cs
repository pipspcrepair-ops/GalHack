using System;
using System.Collections.Generic;
using Godot;
using PlanetSurface.Poi;
using Wildlife;
using Rng = Wildlife.Rng;

namespace Slice.Surface;

/// <summary>
/// Ground that some of the world's own kinds keep to: the places' holders about their places
/// (the growth's Hollows round a place it took, twisted beasts round their den, machines round the
/// place they keep), so what holds a place shows on the ground outside it before you go in. As you
/// come within reach of one, a few of its kinds are kept about it, out of your sight, each with
/// the haunt as its home, so they keep to it as a herd keeps to its range.
/// </summary>
public partial class WildlifePatch
{
    /// <summary>One haunt: its middle (the zone's frame, canonical), how far round it they keep, its kinds and how many of them.</summary>
    public sealed class Haunt
    {
        public Vector3 At;
        public float Radius;
        public readonly List<Species> Kinds = new();
        public int Want;
        public string Why = "";
    }

    readonly List<Haunt> _haunts = new();
    bool _hauntsMade;
    float _hauntClock;

    /// <summary>How far from a haunt its kinds are brought in at most, beyond its own radius, m.</summary>
    const float HauntReach = 30f;

    /// <summary>Kinds brought to haunts so far (tests).</summary>
    public int Haunted { get; private set; }

    /// <summary>Every haunt of this world (made once, on first asking).</summary>
    public IReadOnlyList<Haunt> Haunts
    {
        get
        {
            MakeHaunts();
            return _haunts;
        }
    }

    void MakeHaunts()
    {
        if (_hauntsMade || _planet == null || Life == null) return;
        _hauntsMade = true;
        _haunts.AddRange(HauntsOf(_planet.Pois, Life));
    }

    /// <summary>The haunts of a world's places: round each place the growth took, each beasts' den and each place machines keep, its holders (pure; tests).</summary>
    public static List<Haunt> HauntsOf(IEnumerable<PoiSite> sites, WorldLife life)
    {
        var haunts = new List<Haunt>();
        foreach (PoiSite site in sites)
        {
            if (site.Instance == null) continue;
            var plan = new PoiPlan();
            Places.PlaceFauna.HoldersOf(site.Instance, life, plan);
            int want = plan.Holder switch { PlaceHolder.Hollows => 5, PlaceHolder.Beasts => 3, PlaceHolder.Machines => 2, _ => 0 };
            if (want == 0 || plan.Holders.Count == 0) continue;
            var lo = site.Footprint.Min;
            var hi = site.Footprint.Max;
            float hx = (hi.X - lo.X + 1) * 0.5f, hz = (hi.Z - lo.Z + 1) * 0.5f;
            var h = new Haunt
            {
                At = new Vector3((lo.X + hi.X + 1) * 0.5f, 0f, (lo.Z + hi.Z + 1) * 0.5f),
                Radius = MathF.Sqrt(hx * hx + hz * hz) + 6f,
                Want = want,
                Why = $"{plan.Holder} of {site.Name}",
            };
            // Only what walks the open: anything that can't is kept indoors.
            foreach (Species s in plan.Holders) if (!s.Flies) h.Kinds.Add(s);
            if (h.Kinds.Count > 0) haunts.Add(h);
        }
        return haunts;
    }

    /// <summary>
    /// Every few seconds, the haunts near you short of their kinds get one group more: put down round
    /// the haunt (outside its place, on open ground), away from you and out of your sight, their home
    /// the haunt. Nothing appears where you are looking, as with every refill.
    /// </summary>
    void Haunting(float dt)
    {
        if (_frozen || Player == null || Life == null || Sim == null) return;
        if ((_hauntClock += dt) < 4f) return;
        _hauntClock = 0f;
        MakeHaunts();
        if (_haunts.Count == 0) return;
        Vector3 you = Player.Position, youZone = FromPatch(you);
        float edge = Sim.Terrain.HalfSize - 20f;
        foreach (Haunt h in _haunts)
        {
            Vector3 at = PatchLocal(_planet.NearestCopy(h.At, youZone));
            float away = WildSim.Flat(at - you).Length();
            if (away > AroundYou + h.Radius || MathF.Abs(at.X) > edge || MathF.Abs(at.Z) > edge) continue;
            int there = 0;
            foreach (Creature c in Sim.Creatures)
                if (!c.Removed && !c.Dead && h.Kinds.Contains(c.Species.Adult ?? c.Species) && WildSim.Flat(c.Position - at).Length() < h.Radius + HauntReach + 20f) there++;
            if (there >= h.Want) continue;
            var rng = Rng.For(Life.Seed, "haunt", ++_refills);
            Species s = h.Kinds[rng.Int(0, h.Kinds.Count - 1)];
            int count = Math.Min(h.Want - there, Math.Max(1, rng.Int(s.GroupMin, Math.Min(3, s.GroupMax))));
            if (PutAt(s, count, at, h.Radius, h.Radius + HauntReach, rng) is { Count: > 0 } group)
            {
                foreach (Creature c in group) c.Home = at;
                Keep(group);
                Haunted += group.Count;
            }
        }
    }

    Dictionary<(int, int), List<(Vector3 Mid, float Hx, float Hz)>> _footprints;
    const float FootCell = 64f;

    /// <summary>
    /// Whether a point (patch frame) is on a place's ground: inside its footprint or within a margin
    /// of it. Nothing that walks is put down there, standing or not: it would start inside its walls.
    /// </summary>
    public bool OnPlace(Vector3 at, float margin)
    {
        if (_planet == null) return false;
        if (_footprints == null)
        {
            _footprints = new();
            foreach (PoiSite site in _planet.Pois)
            {
                var lo = site.Footprint.Min;
                var hi = site.Footprint.Max;
                var mid = new Vector3((lo.X + hi.X + 1) * 0.5f, 0f, (lo.Z + hi.Z + 1) * 0.5f);
                float hx = (hi.X - lo.X + 1) * 0.5f, hz = (hi.Z - lo.Z + 1) * 0.5f;
                for (int x = (int)MathF.Floor((mid.X - hx - 8f) / FootCell); x <= (int)MathF.Floor((mid.X + hx + 8f) / FootCell); x++)
                for (int z = (int)MathF.Floor((mid.Z - hz - 8f) / FootCell); z <= (int)MathF.Floor((mid.Z + hz + 8f) / FootCell); z++)
                {
                    if (!_footprints.TryGetValue((x, z), out var list)) _footprints[(x, z)] = list = new();
                    list.Add((mid, hx, hz));
                }
            }
        }
        Vector3 w = _planet.Canonical(FromPatch(at));
        if (!_footprints.TryGetValue(((int)MathF.Floor(w.X / FootCell), (int)MathF.Floor(w.Z / FootCell)), out var near)) return false;
        foreach (var (mid, hx, hz) in near)
            if (MathF.Abs(w.X - mid.X) <= hx + margin && MathF.Abs(w.Z - mid.Z) <= hz + margin) return true;
        return false;
    }

    /// <summary>A group put down in a ring round a point (patch frame), on open ground, out of your way and sight; the group, or null.</summary>
    List<Creature> PutAt(Species s, int count, Vector3 center, float inner, float outer, Rng rng)
    {
        float spread = Sim.GroupRadius(s, count), edge = Sim.Terrain.HalfSize - 20f;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float a = rng.Range(0f, MathF.Tau), r = rng.Range(inner, MathF.Max(inner + 1f, outer));
            var at = new Vector3(center.X + MathF.Cos(a) * r, 0f, center.Z + MathF.Sin(a) * r);
            if (MathF.Abs(at.X) > edge || MathF.Abs(at.Z) > edge || !Open(s, at, spread) || !Sim.ClearOfPlayers(at, spread)) continue;
            Vector3 spot = Sim.FreeSpot(at, rng);
            if (!Open(s, spot, 0f)) continue;
            List<Creature> group = Sim.SpawnGroup(s, spot, count, p => Sim.ClearOfPlayers(p, 0f) && Open(s, p, 0f));
            if (group.Count > 0) return group;
        }
        return null;
    }
}
