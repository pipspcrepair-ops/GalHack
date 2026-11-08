using System;
using System.Collections.Generic;
using Godot;
using PlanetSurface.Infra;
using PoiGenerator.Gen;
using Wildlife;
using Rng = Wildlife.Rng;

namespace Slice.Surface;

/// <summary>
/// The houses and farmsteads along the roads and out in the country, as the creatures and the
/// world's people have them. Each is solid to everything walking (nothing wanders through a wall
/// or stands in a room it can't reach); and on a world where people survive, many are lived in:
/// how many by how many survive (a few survivors, scattered outposts, settled country), an intact
/// one more often than a broken one. A lived-in home's people (the world's own, dressed for its
/// story and era) are about it by day, put there before you are near enough to see.
/// </summary>
public partial class WildlifePatch
{
    sealed class Home
    {
        public Vector3 At;
        public float HalfX, HalfZ, Top;
        public bool Lived;
    }

    List<Home> _homes;
    Dictionary<(int, int), List<Home>> _homeCells;
    const float HomeCell = 48f;

    /// <summary>Ruins given their scavengers so far (tests).</summary>
    public int RuinsPicked { get; private set; }

    /// <summary>Lived-in homes on this world, and the times one was given its people (tests).</summary>
    public int HomesLived { get; private set; }
    public int HomesFilled { get; private set; }

    /// <summary>The share of a world's homes lived in, by how many survive on it: none, a few survivors, scattered outposts, settled country.</summary>
    public static float LivedShare(string people) => people switch { "none" => 0f, "survivors" => 0.3f, "outposts" => 0.45f, "settled" => 0.7f, _ => 0.4f };

    void MakeHomes()
    {
        if (_homes != null) return;
        _homes = new List<Home>();
        _homeCells = new();
        if (_planet?.Infra is not { } net) return;
        string people = _planet.Roll?.Fact("people") ?? (_planet.Profile is { Inhabited: true } ? "" : "none");
        float share = Life?.World.Peopled == false ? 0f : LivedShare(people);
        foreach (FlavourRuin r in net.Ruins)
        {
            if (r.Stamp == null) continue;
            var h = new Home
            {
                At = new Vector3((r.Stamp.MinX + r.Stamp.MaxX + 1) * 0.5f, 0f, (r.Stamp.MinZ + r.Stamp.MaxZ + 1) * 0.5f),
                HalfX = (r.Stamp.MaxX - r.Stamp.MinX + 1) * 0.5f,
                HalfZ = (r.Stamp.MaxZ - r.Stamp.MinZ + 1) * 0.5f,
                Top = r.Stamp.PadY + 9f,
            };
            // A home to someone: a house or a farmstead, more often standing whole than broken.
            bool dwelling = r.Family.Tags.Contains("house") || r.Family.Tags.Contains("farmstead");
            float chance = share * (r.State == PoiState.Abandoned ? 1f : 0.55f) * (r.Blast > 0.2f ? 0.2f : 1f);
            h.Lived = dwelling && Rng.For(r.SpawnSeed, "lived-in").Chance(chance);
            if (h.Lived) HomesLived++;
            _homes.Add(h);
            for (int x = (int)MathF.Floor((h.At.X - h.HalfX) / HomeCell); x <= (int)MathF.Floor((h.At.X + h.HalfX) / HomeCell); x++)
            for (int z = (int)MathF.Floor((h.At.Z - h.HalfZ) / HomeCell); z <= (int)MathF.Floor((h.At.Z + h.HalfZ) / HomeCell); z++)
            {
                if (!_homeCells.TryGetValue((x, z), out var list)) _homeCells[(x, z)] = list = new List<Home>();
                list.Add(h);
            }
        }
    }

    /// <summary>Whether a zone point is inside a house or farmstead's walls (its footprint, up to its roof): solid to anything walking.</summary>
    public bool InHome(Vector3 zone, float margin = 0f)
    {
        MakeHomes();
        if (_homeCells.Count == 0) return false;
        Vector3 w = _planet.Canonical(zone);
        if (!_homeCells.TryGetValue(((int)MathF.Floor(w.X / HomeCell), (int)MathF.Floor(w.Z / HomeCell)), out var near)) return false;
        foreach (Home h in near)
            if (MathF.Abs(w.X - h.At.X) <= h.HalfX + margin && MathF.Abs(w.Z - h.At.Z) <= h.HalfZ + margin && w.Y < h.Top) return true;
        return false;
    }

    /// <summary>
    /// Every few seconds: a lived-in home within reach of you with nobody about it gets its people, out
    /// of your sight (a home ahead is filled while it is still too far to make out), by its door or in
    /// its yard, with the home as theirs.
    /// </summary>
    /// <summary>
    /// Every ruin has someone: each one within reach of you with nobody about it gets a small group of
    /// the world's ruin roamers (its scavengers or drifters, or its small pack animals and leavings-eaters),
    /// out of your sight, picking through it. A few at a time, so a ruined city is roamed, not crowded.
    /// </summary>
    void Ruins()
    {
        _anchors ??= MakeAnchors();
        var cast = Life.RuinCast;
        if (cast.Count == 0 || _anchors.Ruins.Count == 0) return;
        Vector3 you = Player.Position, youZone = FromPatch(you);
        float edge = Sim.Terrain.HalfSize - 20f, total = 0f;
        foreach (var c in cast) total += c.Weight;
        int near = 0, made = 0;
        foreach (Creature c in Sim.Creatures)
            if (!c.Removed && !c.Dead && Kept(c) && WildSim.Flat(c.Position - you).Length() < AroundYou) near++;
        var rng = Rng.For(Life.Seed, "ruins", ++_ownRolls);
        foreach (var (ruin, radius) in _anchors.Ruins)
        {
            if (near >= 40 || made >= 2) break;
            Vector3 at = PatchLocal(_planet.NearestCopy(ruin, youZone));
            if (WildSim.Flat(at - you).Length() > AroundYou || MathF.Abs(at.X) > edge || MathF.Abs(at.Z) > edge) continue;
            bool someone = false;
            foreach (Creature c in Sim.Creatures)
                if (!c.Removed && !c.Dead && WildSim.Flat(c.Position - at).Length() < radius + 20f && (Kept(c) || c.Species.Figure != null)) { someone = true; break; }
            if (someone) continue;
            float pick = Rng.For(Rng.Mix(Life.Seed, (ulong)(ruin.X * 31f + ruin.Z * 7f)), "who").Range(0f, total);
            Species s = cast[^1].Species;
            foreach (var (k, w) in cast)
                if ((pick -= w) <= 0f) { s = k; break; }
            if (PutAt(s, rng.Int(s.GroupMin, s.GroupMax), at, radius + 2f, radius + 14f, rng) is not { Count: > 0 } group) continue;
            foreach (Creature c in group) c.Home = at;
            Sim.AssignRest(group);
            Keep(group);
            near += group.Count;
            made++;
            RuinsPicked++;
        }
    }

    void Homes()
    {
        MakeHomes();
        if (_homes.Count == 0 || HomesLived == 0 || Life.Settlers is not { } settlers) return;
        Vector3 you = Player.Position, youZone = FromPatch(you);
        float edge = Sim.Terrain.HalfSize - 20f;
        var rng = Rng.For(Life.Seed, "homes", ++_ownRolls);
        int filled = 0;
        foreach (Home h in _homes)
        {
            if (!h.Lived) continue;
            Vector3 at = PatchLocal(_planet.NearestCopy(h.At, youZone));
            float d = WildSim.Flat(at - you).Length();
            if (d > AroundYou + 40f || MathF.Abs(at.X) > edge || MathF.Abs(at.Z) > edge) continue;
            float ring = MathF.Max(h.HalfX, h.HalfZ);
            // Someone is home already: its people, or anyone of the world's living kept about it.
            bool home = false;
            foreach (Creature c in Sim.Creatures)
                if (!c.Removed && !c.Dead && c.Species.Figure != null && WildSim.Flat(c.Position - at).Length() < ring + 24f) { home = true; break; }
            if (home) continue;
            int count = rng.Int(settlers.GroupMin, settlers.GroupMax);
            if (PutAt(settlers, count, at, ring + 2f, ring + 9f, rng) is not { Count: > 0 } group) continue;
            foreach (Creature c in group) c.Home = c.Position;
            Sim.AssignRest(group);
            Keep(group);
            HomesFilled++;
            if (++filled >= 2) break;
        }
    }
}
