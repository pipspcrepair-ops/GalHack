using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Placement by purpose: after the roles are filled, each place is read for what its rooms were for
/// and how deep and dark they lie. The growth's beasts are always where the growth took a place;
/// every place has something by its state (vermin and scavengers in an abandoned one, monsters in
/// its dark below ground); scavengers gather where the food was; the worst of the dark keep out of
/// lit rooms; loot worth having is guarded; now and then an armed group has run into a nest; and
/// the way in is kept quiet, so encounters build as you go in and down.
/// </summary>
public static partial class PoiFauna
{
    /// <summary>Rooms where food was kept, cooked or grown: vermin and scavengers gather there.</summary>
    public static bool Food(string purpose) => purpose is "galley" or "mess" or "farm-kitchen" or "cold-store" or "storage" or "cellar"
        or "market-hall" or "shop" or "warehouse" or "hydroponics" or "grow-room" or "mortuary";

    /// <summary>Rooms a nest is made in: out of the way, cluttered, warm or damp.</summary>
    public static bool Nesting(string purpose) => purpose is "storage" or "warehouse" or "cellar" or "boiler-house" or "pump-hall" or "plant"
        or "reactor" or "ore-store" or "stope" or "cryo-bay" or "archive" or "loading-dock" or "generator";

    /// <summary>Whether a spawn is a threat (not vermin, not an insect).</summary>
    public static bool Threat(PoiSpawn s) => !s.Species.Passive && s.Species.Role != DeepRole.Subsister && s.Role != DeepRole.Subsister;

    static void Purposes(PoiPlan poi, PoiPopulation pop, DeepFauna fauna, PoiTheme theme, Rng rng, DeepTables rules, bool active, bool infested, bool empty)
    {
        PopulationRules pr = rules.Population;
        var walkable = poi.Rooms.FindAll(r => r.Kind is not (RoomKind.Vent or RoomKind.Duct) && r.Size.Y >= 2f);
        // Scavengers where the food was: more vermin, and what lives on them, in the galleys, stores and fields.
        if (!active)
            foreach (PoiRoom room in walkable)
            {
                if (!Food(room.Purpose)) continue;
                if (!pop.Spawns.Exists(s => s.Room == room.Index && s.Role == DeepRole.Subsister)) Subsisters(pop, fauna, theme, room, rng, infested, 1.4f, pr);
            }
        // The worst of the dark keep out of the light: a lit room loses its lurkers to the nearest dark room.
        foreach (PoiSpawn s in pop.Spawns)
        {
            if (s.Role != DeepRole.Lurker || s.Species.Husk) continue;
            PoiRoom room = poi.Rooms.Find(r => r.Index == s.Room);
            if (room == null || !room.Lit) continue;
            PoiRoom dark = null;
            foreach (int l in room.Links)
                if (poi.Rooms.Find(r => r.Index == l) is { Lit: false, Kind: not (RoomKind.Vent or RoomKind.Duct) } d && d.Width >= s.Species.Stats.Radius * 3f) { dark = d; break; }
            if (dark == null) continue;
            s.Room = dark.Index;
            s.Position = FloorPoint(dark, rng, MathF.Max(0.8f, s.Species.TurnCircle * 0.5f));
        }
        if (!active) Floor(poi, pop, fauna, theme, rng, infested, walkable, pr);
        if (!active && !empty) Guarded(poi, pop, fauna, theme, rng, infested);
        if (!active && !empty && poi.Armed != null && !poi.Husks) Clash(poi, pop, fauna, theme, rng);
        Quiet(poi, pop, rng);
        // Last: the growth's beasts, whatever else holds it (never thinned by the quiet or the caps).
        if (poi.Hollows.Count > 0) Hollow(poi, pop, fauna, theme, rng, walkable);
    }

    /// <summary>
    /// Every place has something by its state: a place with rooms has vermin at least; below ground,
    /// the dark has its monsters (one per level down, the deepest first, never in a lit room).
    /// </summary>
    static void Floor(PoiPlan poi, PoiPopulation pop, DeepFauna fauna, PoiTheme theme, Rng rng, bool infested, List<PoiRoom> walkable, PopulationRules pr)
    {
        // Vermin at least: where the food was, else any room, else the open ground of a place with none.
        var any = walkable.Count > 0 ? walkable : poi.Yards;
        for (int i = 0; i < 4 && any.Count > 0 && !pop.Spawns.Exists(s => !s.Species.Passive); i++)
        {
            PoiRoom vermin = i == 0 ? any.Find(r => Food(r.Purpose)) ?? any[rng.Int(0, any.Count - 1)] : any[rng.Int(0, any.Count - 1)];
            Subsisters(pop, fauna, theme, vermin, rng, infested, 4f, pr);
        }
        if (walkable.Count == 0) return;
        int deepest = 0;
        foreach (PoiRoom r in walkable) deepest = Math.Max(deepest, r.Depth);
        for (int d = 1; d <= deepest; d++)
        {
            int level = d;
            if (pop.Spawns.Exists(s => Threat(s) && poi.Rooms.Find(r => r.Index == s.Room)?.Depth == level)) continue;
            var dark = walkable.FindAll(r => r.Depth == level && !r.Lit && r.Kind is not (RoomKind.Entrance or RoomKind.Corridor));
            if (dark.Count == 0) dark = walkable.FindAll(r => r.Depth == level && !r.Lit);
            if (dark.Count == 0) continue;
            dark.Sort((a, b) => (b.Size.X * b.Size.Z).CompareTo(a.Size.X * a.Size.Z));
            PoiRoom room = dark[0];
            Species sp = Dark(poi, fauna, theme, DeepRole.Lurker, room.Width, room.Size.Y, rng) ?? Fits(fauna, DeepRole.Lurker, theme, room, infested);
            if (sp != null) Lurker(pop, fauna, theme, room, rng, infested, sp);
        }
    }

    /// <summary>
    /// Loot worth having is guarded (place-patterns E9): the fight is on the way to it, in the room
    /// before it on the route in (the cache's room itself stays the reward), or in it where nothing leads there.
    /// </summary>
    static void Guarded(PoiPlan poi, PoiPopulation pop, DeepFauna fauna, PoiTheme theme, Rng rng, bool infested)
    {
        Dictionary<int, int> hops = Hops(poi);
        foreach (PoiRoom room in poi.Rooms)
        {
            if (room.Loot == 0 || room.LootTier < 2 && room.Kind != RoomKind.Vault || room.Size.Y < 2f) continue;
            PoiRoom before = null;
            if (hops.TryGetValue(room.Index, out int h))
                foreach (int l in room.Links)
                    if (hops.TryGetValue(l, out int lh) && lh < h && poi.Rooms.Find(r => r.Index == l) is { } r && r.Kind is not (RoomKind.Vent or RoomKind.Duct or RoomKind.Entrance) && r.Size.Y >= 2f) { before = r; break; }
            PoiRoom at = before ?? room;
            if (pop.Spawns.Exists(s => Threat(s) && (s.Room == room.Index || s.Room == at.Index))) continue;
            Species sp = (at.Lit ? null : Dark(poi, fauna, theme, DeepRole.Lurker, at.Width, at.Size.Y, rng)) ?? Fits(fauna, DeepRole.Lurker, theme, at, infested);
            if (sp != null) Lurker(pop, fauna, theme, at, rng, infested, sp);
        }
    }

    /// <summary>
    /// Mixed encounters: now and then an armed group has pushed into a nest's rooms and is holding the
    /// next room against it, so you come on the two at it (or on what is left).
    /// </summary>
    static void Clash(PoiPlan poi, PoiPopulation pop, DeepFauna fauna, PoiTheme theme, Rng rng)
    {
        PoiSpawn nester = pop.Spawns.Find(s => s.Role is DeepRole.Nester or DeepRole.DerelictNester && !s.Species.Passive)
            ?? pop.Spawns.Find(s => s.Role == DeepRole.Lurker && s.Species.Figure == null && Threat(s) && !s.Species.Husk);
        if (nester == null || !rng.Chance(0.35f)) return;
        if (pop.Spawns.Exists(s => s.Species == poi.Armed || s.Species.Figure != null && s.Species.Id == poi.Armed.Id)) return;
        PoiRoom nest = poi.Rooms.Find(r => r.Index == nester.Room);
        if (nest == null) return;
        PoiRoom best = null;
        foreach (int l in nest.Links)
            if (poi.Rooms.Find(r => r.Index == l) is { } r && r.Kind is not (RoomKind.Vent or RoomKind.Duct) && r.Width >= 2.5f && r.Size.Y >= 2.2f
                && (best == null || r.Size.X * r.Size.Z > best.Size.X * best.Size.Z)) best = r;
        if (best == null || Foe(fauna, fauna.Holding(poi.Armed, DeepRole.Lurker, theme), best.Width, best.Size.Y) is not { } sp) return;
        // Facing the nest's doorway, at the far side of their room.
        for (int i = 0, n = rng.Int(2, 3); i < n; i++)
            pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Lurker, Species = sp, Position = FloorPoint(best, rng, 1f), Room = best.Index });
        pop.Clash = (best.Index, nest.Index);
    }

    /// <summary>
    /// Quiet stretches (place-patterns E3): the way in is quiet (nothing that comes at you in it); the
    /// rooms just off it hold one group at most; at least one room in five holds nothing; and never
    /// three rooms in a row on the way in with something in each, so encounters build as you go in and down.
    /// (A prowler's beat may pass through; a nest keeps its guards; the Husks' and the growth's are theirs.)
    /// </summary>
    static void Quiet(PoiPlan poi, PoiPopulation pop, Rng rng)
    {
        if (poi.Holder is PlaceHolder.Machines or PlaceHolder.People) return;
        Dictionary<int, int> hops = Hops(poi);
        bool Movable(PoiSpawn s) => Threat(s) && s.Route == null && s.Nest < 0 && !s.Species.Husk && !s.Species.Corrupted;
        bool Near(PoiSpawn s, int most) => hops.TryGetValue(s.Room, out int h) && h <= most && poi.Rooms.Find(r => r.Index == s.Room)?.Depth == 0;
        pop.Spawns.RemoveAll(s => Movable(s) && Near(s, 0) && poi.Rooms.Count > 2);
        var close = pop.Spawns.FindAll(s => Movable(s) && Near(s, 1));
        if (close.Count > 1)
        {
            int keep = close[rng.Int(0, close.Count - 1)].Room;
            pop.Spawns.RemoveAll(s => close.Contains(s) && s.Room != keep);
        }
        var rooms = poi.Rooms.FindAll(r => r.Kind is not (RoomKind.Vent or RoomKind.Duct));
        bool Occupied(int room) => pop.Spawns.Exists(s => Threat(s) && s.Route == null && s.Room == room);
        // Never three in a row: walking out from the way in, the middle one of three occupied rooms goes quiet.
        var order = new List<PoiRoom>(rooms);
        order.Sort((a, b) => hops.GetValueOrDefault(a.Index, 99).CompareTo(hops.GetValueOrDefault(b.Index, 99)));
        foreach (PoiRoom r in order)
        {
            if (!hops.TryGetValue(r.Index, out int h) || !Occupied(r.Index)) continue;
            PoiRoom p = Before(r, h);
            if (p == null || !Occupied(p.Index)) continue;
            PoiRoom q = Before(p, h - 1);
            if (q == null || !Occupied(q.Index)) continue;
            pop.Spawns.RemoveAll(s => Movable(s) && s.Room == p.Index);
        }
        // One room in five quiet at least: the shallowest occupied ones go first.
        int most = rooms.Count - (int)MathF.Ceiling(rooms.Count * 0.2f);
        var held = rooms.FindAll(r => Occupied(r.Index));
        held.Sort((a, b) => a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : hops.GetValueOrDefault(a.Index, 99).CompareTo(hops.GetValueOrDefault(b.Index, 99)));
        for (int i = 0; held.Count - i > most && i < held.Count; i++)
            pop.Spawns.RemoveAll(s => Movable(s) && s.Room == held[i].Index);

        PoiRoom Before(PoiRoom r, int h)
        {
            foreach (int l in r.Links)
                if (hops.TryGetValue(l, out int lh) && lh == h - 1 && poi.Rooms.Find(x => x.Index == l) is { } x && x.Kind is not (RoomKind.Vent or RoomKind.Duct)) return x;
            return null;
        }
    }

    /// <summary>
    /// The growth's: however the place is held, its Hollow beasts are in it, more the bigger it is,
    /// in its dark rooms first and the deepest, walking its corridors, and on its yards where it has
    /// no rooms (a camp). The ground round it is theirs too (the surface's haunts).
    /// </summary>
    static void Hollow(PoiPlan poi, PoiPopulation pop, DeepFauna fauna, PoiTheme theme, Rng rng, List<PoiRoom> walkable)
    {
        bool Of(PoiSpawn s) => s.Species.Corrupted && poi.Hollows.Exists(h => s.Species.Id.StartsWith(h.Id, StringComparison.Ordinal));
        int want = Math.Clamp((walkable.Count + poi.Yards.Count) / 3 + 2, 2, 6);
        int have = pop.Spawns.FindAll(Of).Count;
        if (have >= want) return;
        var where = new List<PoiRoom>(walkable);
        where.Sort((a, b) => a.Lit != b.Lit ? a.Lit.CompareTo(b.Lit) : b.Depth != a.Depth ? b.Depth.CompareTo(a.Depth) : (b.Size.X * b.Size.Z).CompareTo(a.Size.X * a.Size.Z));
        where.AddRange(poi.Yards);
        if (where.Count == 0) return;
        Species Hollowed(float width, float height)
        {
            int first = rng.Int(0, poi.Hollows.Count - 1);
            for (int i = 0; i < poi.Hollows.Count; i++)
            {
                Species h = fauna.Holding(poi.Hollows[(first + i) % poi.Hollows.Count], DeepRole.Lurker, theme);
                if (Foe(fauna, h, width, height) is { } fit) return fit;
            }
            return null;
        }
        for (int i = 0, tries = 0; have < want && tries < want * 4; tries++, i++)
        {
            PoiRoom room = where[i % where.Count];
            // A corridor's are walking it; a room's lie up in it; a yard's wander it.
            if (room.Kind == RoomKind.Corridor && !room.Yard)
            {
                List<Vector3> route = Beat(poi, room, rng, out float w, out float h);
                if (Hollowed(w, h) is not { } walker) continue;
                pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Prowler, Species = walker, Position = route[0], Room = room.Index, Route = route });
            }
            else
            {
                if (Hollowed(room.Width, room.Yard ? 6f : room.Size.Y) is not { } sp) continue;
                pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Lurker, Species = sp, Position = FloorPoint(room, rng, MathF.Max(0.8f, sp.TurnCircle * 0.5f)), Room = room.Index, Hide = room.Yard ? HideKind.None : HideKind.Dark });
            }
            have++;
        }
        pop.Silent = false;
    }
}
