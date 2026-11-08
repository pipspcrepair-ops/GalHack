using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>What a room of a place is, as far as its creatures care.</summary>
public enum RoomKind { Entrance, Corridor, Room, Hall, Vault, Descent, Vent, Duct, Hold, EngineRoom, Cave }

/// <summary>A place's state, as the POI generator has it.</summary>
public enum PlaceState { Active, Abandoned, Infested, Ruined }

/// <summary>One room of a place: its open interior as a box, how deep it is, whether it is dark.</summary>
public sealed class PoiRoom
{
    public int Index;
    public RoomKind Kind;
    /// <summary>The open interior, in the sim's frame: floor at Min.Y, ceiling at Max.Y.</summary>
    public Vector3 Min, Max;
    /// <summary>Levels below the way in (0 at the way in).</summary>
    public int Depth;
    public bool Dark;
    /// <summary>Rooms it opens onto.</summary>
    public readonly List<int> Links = new();
    /// <summary>Vent grilles: their mouths on the room's walls or ceiling, and which way they face (into the room).</summary>
    public readonly List<(Vector3 at, Vector3 normal)> Vents = new();
    /// <summary>Doorways: their middles at floor height, and which way into the room.</summary>
    public readonly List<(Vector3 at, Vector3 normal)> Doors = new();

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 FloorCenter => new((Min.X + Max.X) * 0.5f, Min.Y, (Min.Z + Max.Z) * 0.5f);
    public Vector3 Size => Max - Min;

    /// <summary>The width a body can turn round in here, m.</summary>
    public float Width => MathF.Min(Size.X, Size.Z);
}

/// <summary>Everything the population function needs to know about a place.</summary>
public sealed class PoiPlan
{
    public ulong Seed;
    public PoiTheme Theme;
    public PlaceState State = PlaceState.Abandoned;
    /// <summary>A wrecked ship (crashed or drifting): its holds, ducts and engine rooms get nesters.</summary>
    public bool Derelict;
    /// <summary>How livable its world is, 0..1: a harsh world's places hold less (an overrun place holds what the Bloom brought).</summary>
    public float Livable = 1f;
    /// <summary>A Husk den (its occupancy is the Turned): Husks in its rooms, worm masses on its floors and walls. Shared: it shares the place with others (by floors, a front line, or in layers), so fewer.</summary>
    public bool Husks, Shared;
    public readonly List<PoiRoom> Rooms = new();
}

/// <summary>One creature to put in a place: its role, species, spot and how it hides.</summary>
public sealed class PoiSpawn
{
    public DeepRole Role;
    public Species Species;
    public Vector3 Position;
    /// <summary>The surface it starts on faces this way (down for a ceiling).</summary>
    public Vector3 Up = Vector3.Up;
    public int Room;
    public HideKind Hide;
    /// <summary>The crack, vent or door it hides in (index into <see cref="PoiPopulation.Cracks"/>), or -1.</summary>
    public int Hole = -1;
    /// <summary>The nest it guards (index into <see cref="PoiPopulation.Nests"/>), or -1.</summary>
    public int Nest = -1;
    /// <summary>A prowler's beat.</summary>
    public List<Vector3> Route;
}

/// <summary>A predator of the place and the subsister it eats: the food chain you can watch.</summary>
public sealed record FoodLink(Species Predator, Species Prey);

/// <summary>What a place holds: its creatures, nests, food, cracks and food chain, or nothing at all.</summary>
public sealed class PoiPopulation
{
    /// <summary>Empty and silent: nothing lives here (which is ominous in its own way).</summary>
    public bool Silent;
    public string Note = "";
    public readonly List<PoiSpawn> Spawns = new();
    public readonly List<Nest> Nests = new();
    public readonly List<FoodPatch> Food = new();
    public readonly List<Crack> Cracks = new();
    public readonly List<FoodLink> FoodChain = new();
    /// <summary>A Husk den's worm masses.</summary>
    public readonly List<WormMass> Masses = new();

    public int Count(DeepRole role) => Spawns.FindAll(s => s.Role == role).Count;
}

/// <summary>
/// Where small things come back from: a subsister species keeps its numbers up in a place by
/// emerging from its cracks, so a place's food chain keeps going.
/// </summary>
public sealed class Colony
{
    public Species Species;
    public ISolidSpace Space;
    public int Target;
    public Vector3 Home;
    public readonly List<Crack> Sources = new();
    public float Timer, Wait = RefillSeconds;
    const float RefillSeconds = 90f;

    public static void StepAll(WildSim w, float dt)
    {
        foreach (Colony col in w.Colonies)
        {
            col.Timer -= dt;
            if (col.Timer > 0f) continue;
            col.Timer = 5f;
            int alive = 0;
            foreach (Creature c in w.Creatures)
                if (c.Species == col.Species && c.Alive && !c.Removed && c.Space == col.Space) alive++;
            if (alive >= col.Target || col.Sources.Count == 0)
            {
                col.Wait = RefillSeconds;
                continue;
            }
            col.Wait -= 5f;
            if (col.Wait > 0f) continue;
            // Nothing comes out under your nose: only by a crack nobody is near or looking down.
            Crack from = col.Sources[(int)(w.Frame % col.Sources.Count)];
            if (Overlooked(w, col, from)) continue;
            col.Wait = RefillSeconds;
            Creature n = w.SpawnIn(col.Species, col.Space, from.Position + from.Normal * 0.3f, from.Normal);
            n.Home = col.Home;
            // It comes out of the crack: in the walls a moment, then out.
            Brain.EnterCrack(w, n, from, park: true);
            n.CrackExit = from;
            n.WallLength = 1f;
            n.WallTime = 0f;
        }
    }

    static bool Overlooked(WildSim w, Colony col, Crack k)
    {
        SpawnRules r = w.Data.Spawning;
        Vector3 mouth = k.Position + k.Normal * 0.3f;
        foreach (ITarget t in w.Targets)
        {
            float d = (t.Position - k.Position).Length();
            if (d < r.ColonyClearance) return true;
            if (d < r.ViewDistance && SpaceMath.Clear(col.Space, t.Position + Vector3.Up * 1.4f, mouth, 0f)) return true;
        }
        return false;
    }
}

/// <summary>
/// The population function a place calls with its rooms: which creatures live there, where,
/// how they hide, what they eat and what eats them. Deterministic from the place's seed.
///
/// - Not every place is alive: some (by state) are empty and silent.
/// - Active places are kept clear by their garrison: at most vermin in the deep.
/// - Abandoned and ruined places have moved-in wildlife: lurkers in dark rooms, prowlers on the
///   corridors, ambushers in vents, on ceilings and behind doors, subsisters grazing moss and
///   fungus or picking at scraps (and the place's hunters eating them), and nests in the
///   deepest levels that get worse with depth.
/// - Infested places are overrun: more of everything, Bloom-taken, nests on every lower level.
/// - Derelict ships have their own nesters in holds, ducts and engine rooms.
/// - Caves (rooms of kind Cave) get crawlers on their walls and ceilings.
/// </summary>
public static class PoiFauna
{
    public static PoiPopulation Populate(PoiPlan poi, DeepFauna fauna, DeepTables rules)
    {
        var pop = new PoiPopulation();
        var rng = Rng.For(poi.Seed, "poi-fauna");
        PopulationRules pr = rules.Population;
        string state = poi.State.ToString();
        if (!poi.Husks && rng.Chance(pr.Silent.GetValueOrDefault(state, 0.25f)))
        {
            pop.Silent = true;
            pop.Note = "empty and silent";
            return pop;
        }
        bool infested = poi.State == PlaceState.Infested, active = poi.State == PlaceState.Active;
        float density = pr.Density.GetValueOrDefault(state, 1f) * (infested ? 1f : Mathf.Lerp(0.4f, 1f, poi.Livable));
        PoiTheme theme = poi.Derelict ? PoiTheme.Derelict : poi.Theme;
        int maxDepth = 0;
        foreach (PoiRoom r in poi.Rooms) maxDepth = Math.Max(maxDepth, r.Depth);

        // Cracks and food first: every room has somewhere for small things to go and something to eat.
        foreach (PoiRoom room in poi.Rooms)
        {
            int cracks = rng.Int(pr.Cracks);
            for (int i = 0; i < cracks; i++) pop.Cracks.Add(WallCrack(room, rng, network: 1, pop.Cracks.Count));
            foreach (var (at, n) in room.Vents)
                pop.Cracks.Add(new Crack { Id = pop.Cracks.Count, Kind = CrackKind.Vent, Position = at, Normal = n, Network = 2, Size = 0.45f });
            if (room.Kind is RoomKind.Vent or RoomKind.Duct) continue;
            int food = rng.Int(pr.Food);
            Diet diet = fauna.For(DeepRole.Subsister, theme, room.Depth, infested).Diet;
            for (int i = 0; i < food; i++)
                pop.Food.Add(new FoodPatch { Position = FloorPoint(room, rng, 0.8f), Kind = diet, Amount = rng.Range(0.6f, 1f), Radius = rng.Range(0.6f, 1.1f) });
        }

        var corridors = new List<PoiRoom>();
        foreach (PoiRoom room in poi.Rooms)
        {
            int d = room.Depth;
            bool deep = d >= Math.Max(1, maxDepth) && maxDepth >= 1;
            switch (room.Kind)
            {
                case RoomKind.Corridor:
                    corridors.Add(room);
                    if (!active && rng.Chance(pr.Ambusher * density)) Ceiling(pop, fauna, theme, room, rng, infested, DeepRole.Ambusher);
                    break;
                case RoomKind.Cave:
                    if (!active)
                    {
                        int crawlers = Math.Max(1, (int)MathF.Round(rng.Range(1f, 2.4f) * density));
                        for (int i = 0; i < crawlers; i++) Ceiling(pop, fauna, theme, room, rng, infested, DeepRole.Crawler);
                    }
                    Subsisters(pop, fauna, theme, room, rng, infested, density, pr);
                    break;
                case RoomKind.Vent:
                case RoomKind.Duct:
                    if (active) break;
                    if (poi.Derelict && (!poi.Husks || poi.Shared) && rng.Chance(0.5f * density)) NestIn(pop, fauna, theme, room, rng, infested, density, rules, DeepRole.DerelictNester, true);
                    else if (rng.Chance(0.6f * density)) Hidden(pop, fauna, theme, room, rng, infested, HideKind.Vent);
                    break;
                case RoomKind.Hold:
                case RoomKind.EngineRoom:
                    if (poi.Derelict && !active && (!poi.Husks || poi.Shared)) NestIn(pop, fauna, theme, room, rng, infested, density, rules, DeepRole.DerelictNester, true);
                    Subsisters(pop, fauna, theme, room, rng, infested, density * 0.6f, pr);
                    break;
                default:
                    if (active)
                    {
                        // A garrison keeps its place clear: at most vermin, and only down in the dark.
                        if (d >= 1 && rng.Chance(0.5f)) Subsisters(pop, fauna, theme, room, rng, false, density, pr);
                        break;
                    }
                    bool dark = room.Dark || d >= 1;
                    // Husks don't breed in nests: a den the Turned hold alone has no eggs anywhere (it spreads only by the worms getting into someone).
                    bool nests = !poi.Husks || poi.Shared;
                    if (nests && deep && room.Kind is RoomKind.Room or RoomKind.Hall or RoomKind.Vault or RoomKind.Descent)
                        NestIn(pop, fauna, theme, room, rng, infested, density, rules, poi.Derelict ? DeepRole.DerelictNester : DeepRole.Nester, poi.Derelict);
                    else if (nests && infested && d >= 1 && rng.Chance(0.3f))
                        NestIn(pop, fauna, theme, room, rng, true, density, rules, DeepRole.Nester, false);
                    if (dark && room.Kind != RoomKind.Entrance && rng.Chance(pr.Lurker * MathF.Min(1.5f, density))) Lurker(pop, fauna, theme, room, rng, infested);
                    Subsisters(pop, fauna, theme, room, rng, infested, density, pr);
                    foreach (var (at, n) in room.Vents)
                        if (rng.Chance(pr.Ambusher * density)) Hidden(pop, fauna, theme, room, rng, infested, HideKind.Vent, at, n);
                    foreach (var (at, n) in room.Doors)
                        if (room.Kind != RoomKind.Entrance && rng.Chance(pr.Ambusher * density * 0.4f)) Hidden(pop, fauna, theme, room, rng, infested, HideKind.Door, at, n);
                    break;
            }
        }

        // Prowlers walk beats through the corridors, between the rooms at either end.
        if (!active && corridors.Count > 0)
        {
            int prowlers = Math.Max(corridors.Count >= 2 ? 1 : 0, (int)MathF.Round(corridors.Count * pr.Prowler * density));
            for (int i = 0; i < prowlers; i++)
            {
                PoiRoom start = corridors[rng.Int(0, corridors.Count - 1)];
                List<Vector3> route = Beat(poi, start, rng, out float width, out float height);
                Species sp = fauna.Fitted(fauna.For(DeepRole.Prowler, theme, start.Depth, infested), width, height);
                if (sp == null) continue; // the way is too tight for its kind
                pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Prowler, Species = sp, Position = route[0], Room = start.Index, Route = route });
            }
        }

        Cap(poi, pop, rng, pr, state);
        // A Husk den: theirs come after the cap, as a den is theirs before it is anything's.
        if (poi.Husks) Den(poi, pop, fauna, theme, rng);

        // The food chain: every hunter of the place eats every subsister in it.
        var preds = new HashSet<Species>();
        var prey = new HashSet<Species>();
        foreach (PoiSpawn s in pop.Spawns)
        {
            if (s.Species.Role == DeepRole.Subsister) prey.Add(s.Species);
            else if (s.Species.HuntsPrey) preds.Add(s.Species);
        }
        foreach (Species p in preds)
        foreach (Species q in prey) pop.FoodChain.Add(new FoodLink(p, q));
        if (pop.Spawns.Count == 0 && pop.Masses.Count == 0)
        {
            pop.Silent = true;
            pop.Note = active ? "kept clear by its garrison" : "empty and silent";
        }
        else pop.Note = $"{pop.Spawns.Count} creatures, {pop.Nests.Count} nests, {pop.FoodChain.Count} links in its food chain";
        return pop;
    }

    /// <summary>
    /// Keeps a place to a few creatures: first each room to its cap, then the place to its own,
    /// taking from whichever role has most and from the shallowest rooms, so what is left is a
    /// mix of things, further in. A nest always keeps a guard.
    /// </summary>
    static void Cap(PoiPlan poi, PoiPopulation pop, Rng rng, PopulationRules pr, string state)
    {
        bool Spare(PoiSpawn s) => s.Nest < 0 || pop.Spawns.Exists(o => o != s && o.Nest == s.Nest);
        foreach (PoiRoom room in poi.Rooms)
        {
            bool nest = pop.Spawns.Exists(s => s.Room == room.Index && s.Nest >= 0);
            int cap = nest ? pr.NestCap : pr.RoomCap + (int)(room.Size.X * room.Size.Z / MathF.Max(1f, pr.RoomArea));
            List<PoiSpawn> here = pop.Spawns.FindAll(s => s.Room == room.Index);
            while (here.Count > cap)
            {
                // Grazers go first: the hunters are what a room is about.
                List<PoiSpawn> from = here.FindAll(s => s.Nest < 0 && s.Role == DeepRole.Subsister);
                if (from.Count == 0) from = here.FindAll(Spare);
                if (from.Count == 0) break;
                PoiSpawn drop = from[rng.Int(0, from.Count - 1)];
                here.Remove(drop);
                pop.Spawns.Remove(drop);
            }
        }
        int baseCap = pr.PlaceCap.GetValueOrDefault(state, 5);
        int most = Math.Min(baseCap * 2, baseCap + poi.Rooms.Count / Math.Max(1, pr.CapRooms));
        int Depth(PoiSpawn s) => poi.Rooms.Find(r => r.Index == s.Room)?.Depth ?? 0;
        while (pop.Spawns.Count > most)
        {
            var roles = new Dictionary<DeepRole, int>();
            foreach (PoiSpawn s in pop.Spawns) if (Spare(s)) roles[s.Role] = roles.GetValueOrDefault(s.Role) + 1;
            if (roles.Count == 0) break;
            DeepRole busiest = default;
            int count = -1;
            foreach (var (role, n) in roles)
                if (n > count || n == count && role < busiest) { busiest = role; count = n; }
            List<PoiSpawn> from = pop.Spawns.FindAll(s => s.Role == busiest && Spare(s));
            int shallowest = int.MaxValue;
            foreach (PoiSpawn s in from) shallowest = Math.Min(shallowest, Depth(s));
            from = from.FindAll(s => Depth(s) == shallowest);
            pop.Spawns.Remove(from[rng.Int(0, from.Count - 1)]);
        }
    }

    /// <summary>
    /// A Husk den: in every room a few of the world's Husks (prowling the corridors, lurking in the
    /// rooms, more of them deeper down), and the worms gathered into masses on the floors and the
    /// walls, more and bigger in the halls and deeper down. Fewer where the den shares the place.
    /// </summary>
    static void Den(PoiPlan poi, PoiPopulation pop, DeepFauna fauna, PoiTheme theme, Rng rng)
    {
        float share = poi.Shared ? 0.5f : 1f;
        foreach (PoiRoom room in poi.Rooms)
        {
            if (room.Kind is RoomKind.Vent or RoomKind.Duct || room.Size.Y < 2f) continue;
            // A room another occupant nests in is theirs: no Husks or worms among the eggs.
            if (pop.Spawns.Exists(s => s.Room == room.Index && s.Nest >= 0)) continue;
            bool hall = room.Kind is RoomKind.Hall or RoomKind.Hold or RoomKind.Vault;
            int husks = (int)MathF.Round((rng.Int(0, 2) + (room.Depth >= 1 ? 1 : 0) + (hall ? 1 : 0)) * share);
            for (int i = 0; i < husks; i++)
            {
                bool corridor = room.Kind == RoomKind.Corridor;
                Species sp = fauna.HuskFor(theme, corridor ? DeepRole.Prowler : DeepRole.Lurker);
                if (sp == null) break;
                if (corridor)
                {
                    List<Vector3> route = Beat(poi, room, rng, out _, out _);
                    pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Prowler, Species = sp, Position = route[0], Room = room.Index, Route = route });
                }
                else pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Lurker, Species = sp, Position = FloorPoint(room, rng, 0.8f), Room = room.Index, Hide = HideKind.Dark });
            }
            int masses = (int)MathF.Round((rng.Int(1, 2) + (room.Depth >= 1 ? 1 : 0) + (hall ? 1 : 0)) * MathF.Max(share, 0.6f));
            for (int i = 0; i < masses; i++)
            {
                float size = MathF.Min(rng.Range(0.6f, 1.25f) * (hall ? 1.3f : 1f) * (1f + 0.15f * room.Depth), room.Width * 0.45f);
                var m = new WormMass { Size = size, Seed = Rng.Mix(poi.Seed, (ulong)(pop.Masses.Count * 7919 + room.Index)), Room = room.Index };
                if (rng.Chance(0.6f) || room.Size.Y < 2.5f)
                {
                    m.Position = FloorPoint(room, rng, MathF.Max(0.5f, size * 0.6f));
                    m.Normal = Vector3.Up;
                }
                else
                {
                    Crack wall = WallCrack(room, rng, 0, -1);
                    m.Position = wall.Position + Vector3.Up * rng.Range(0.3f, MathF.Min(1.6f, room.Size.Y - 0.6f));
                    m.Normal = wall.Normal;
                }
                pop.Masses.Add(m);
            }
        }
    }

    // ---- Roles into rooms -----------------------------------------------------------------

    /// <summary>What fills a role in a room, as it fits there; nothing if that kind can't turn round in it.</summary>
    static Species Fits(DeepFauna fauna, DeepRole role, PoiTheme theme, PoiRoom room, bool infested) =>
        fauna.Fitted(fauna.For(role, theme, room.Depth, infested), room.Width, room.Size.Y);

    static void Ceiling(PoiPopulation pop, DeepFauna fauna, PoiTheme theme, PoiRoom room, Rng rng, bool infested, DeepRole role)
    {
        if (Fits(fauna, role, theme, room, infested) is not { } sp) return;
        Vector3 p = FloorPoint(room, rng, 0.9f);
        p.Y = room.Max.Y - 0.05f;
        pop.Spawns.Add(new PoiSpawn { Role = role, Species = sp, Position = p, Up = Vector3.Down, Room = room.Index, Hide = HideKind.Ceiling });
    }

    static void Lurker(PoiPopulation pop, DeepFauna fauna, PoiTheme theme, PoiRoom room, Rng rng, bool infested)
    {
        if (Fits(fauna, DeepRole.Lurker, theme, room, infested) is not { } sp) return;
        // The far corner from the way in, where the light doesn't reach.
        Vector3 door = room.Doors.Count > 0 ? room.Doors[0].at : room.Min;
        Vector3 best = room.FloorCenter;
        float bestD = -1f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 q = FloorPoint(room, rng, MathF.Max(1.2f, sp.TurnCircle * 0.5f));
            float d = (q - door).Length();
            if (d > bestD) { bestD = d; best = q; }
        }
        pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Lurker, Species = sp, Position = best, Room = room.Index, Hide = HideKind.Dark });
    }

    static void Subsisters(PoiPopulation pop, DeepFauna fauna, PoiTheme theme, PoiRoom room, Rng rng, bool infested, float density, PopulationRules pr)
    {
        if (!rng.Chance(MathF.Min(0.95f, pr.Subsister * density))) return;
        if (Fits(fauna, DeepRole.Subsister, theme, room, infested) is not { } sp) return;
        int n = Math.Max(1, (int)MathF.Round(rng.Int(sp.GroupMin, sp.GroupMax) * MathF.Min(1.5f, density)));
        FoodPatch patch = pop.Food.Find(f => InRoom(room, f.Position));
        Vector3 around = patch?.Position ?? room.FloorCenter;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = around + new Vector3(rng.Range(-1.2f, 1.2f), 0f, rng.Range(-1.2f, 1.2f));
            p = Clamp(room, p, 0.5f);
            pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Subsister, Species = sp, Position = p, Room = room.Index });
        }
    }

    static void Hidden(PoiPopulation pop, DeepFauna fauna, PoiTheme theme, PoiRoom room, Rng rng, bool infested, HideKind hide, Vector3? at = null, Vector3? normal = null)
    {
        if (Fits(fauna, DeepRole.Ambusher, theme, room, infested) is not { } sp) return;
        Vector3 p = at ?? new Vector3((room.Min.X + room.Max.X) * 0.5f, room.Max.Y - 0.3f, (room.Min.Z + room.Max.Z) * 0.5f);
        Vector3 n = normal ?? Vector3.Down;
        var hole = new Crack
        {
            Id = pop.Cracks.Count,
            Kind = hide == HideKind.Door ? CrackKind.Door : CrackKind.Vent,
            Position = p,
            Normal = n,
            Network = hide == HideKind.Door ? 3 : 2,
            Size = hide == HideKind.Door ? 1f : 0.45f,
        };
        // A vent already listed for the room is the same hole.
        int existing = pop.Cracks.FindIndex(k => k.Kind == hole.Kind && (k.Position - p).Length() < 0.2f);
        if (existing < 0)
        {
            pop.Cracks.Add(hole);
            existing = hole.Id;
        }
        pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Ambusher, Species = sp, Position = p + n * 0.3f, Up = n, Room = room.Index, Hide = hide, Hole = existing });
    }

    /// <summary>A nest and its guards: more and bigger the deeper it is; overrun places have more.</summary>
    static void NestIn(PoiPopulation pop, DeepFauna fauna, PoiTheme theme, PoiRoom room, Rng rng, bool infested, float density, DeepTables rules, DeepRole role, bool derelict)
    {
        DepthRules dr = rules.Depth;
        int depth = Math.Max(room.Depth, derelict ? 1 : 0);
        if (fauna.Fitted(fauna.For(role, derelict ? PoiTheme.Derelict : theme, depth, infested), room.Width, room.Size.Y) is not { } sp) return;
        var nest = new Nest
        {
            Position = FloorPoint(room, rng, MathF.Min(1.6f, MathF.Min(room.Size.X, room.Size.Z) * 0.3f)),
            Depth = depth,
            Derelict = derelict,
            Radius = 1.2f + 0.25f * depth,
            Size = derelict ? 0.55f : 1f,
        };
        pop.Nests.Add(nest);
        int guards = (int)MathF.Round((dr.Nesters[0] + dr.Nesters[1] * depth) * MathF.Min(1.6f, MathF.Max(0.6f, density)));
        guards = Math.Clamp(guards, 1, rules.Population.NestCap);
        for (int i = 0; i < guards; i++)
        {
            float a = i * MathF.Tau / guards + rng.Range(-0.3f, 0.3f);
            Vector3 p = Clamp(room, nest.Position + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * (nest.Radius + 0.6f), 0.5f);
            pop.Spawns.Add(new PoiSpawn { Role = role, Species = sp, Position = p, Room = room.Index, Hide = HideKind.None, Nest = pop.Nests.Count - 1 });
        }
    }

    /// <summary>A prowler's beat: from a corridor through linked corridors and rooms, a few stops long.</summary>
    static List<Vector3> Beat(PoiPlan poi, PoiRoom start, Rng rng, out float width, out float height)
    {
        width = start.Width;
        height = start.Size.Y;
        var route = new List<Vector3> { start.FloorCenter };
        var seen = new HashSet<int> { start.Index };
        PoiRoom at = start;
        for (int i = 0; i < 6; i++)
        {
            var next = new List<PoiRoom>();
            foreach (int l in at.Links)
            {
                PoiRoom r = poi.Rooms.Find(x => x.Index == l);
                if (r != null && !seen.Contains(r.Index) && r.Depth == start.Depth && r.Kind is not (RoomKind.Vent or RoomKind.Duct)) next.Add(r);
            }
            if (next.Count == 0) break;
            at = next[rng.Int(0, next.Count - 1)];
            seen.Add(at.Index);
            route.Add(at.FloorCenter);
            width = MathF.Min(width, at.Width);
            height = MathF.Min(height, at.Size.Y);
        }
        if (route.Count == 1)
        {
            // A dead-end corridor: up and down it.
            Vector3 a = start.FloorCenter, size = start.Size;
            Vector3 along = size.X > size.Z ? new Vector3(size.X * 0.35f, 0f, 0f) : new Vector3(0f, 0f, size.Z * 0.35f);
            route.Clear();
            route.Add(a - along);
            route.Add(a + along);
        }
        return route;
    }

    // ---- Geometry in a room -----------------------------------------------------------

    static Vector3 FloorPoint(PoiRoom room, Rng rng, float margin)
    {
        float mx = MathF.Min(margin, room.Size.X * 0.45f), mz = MathF.Min(margin, room.Size.Z * 0.45f);
        return new Vector3(rng.Range(room.Min.X + mx, room.Max.X - mx), room.Min.Y, rng.Range(room.Min.Z + mz, room.Max.Z - mz));
    }

    static Vector3 Clamp(PoiRoom room, Vector3 p, float margin) => new(
        Math.Clamp(p.X, room.Min.X + margin, room.Max.X - margin), room.Min.Y, Math.Clamp(p.Z, room.Min.Z + margin, room.Max.Z - margin));

    static bool InRoom(PoiRoom room, Vector3 p) =>
        p.X >= room.Min.X - 0.1f && p.X <= room.Max.X + 0.1f && p.Z >= room.Min.Z - 0.1f && p.Z <= room.Max.Z + 0.1f && MathF.Abs(p.Y - room.Min.Y) < 1f;

    /// <summary>A crack low in one of a room's walls, facing into the room.</summary>
    static Crack WallCrack(PoiRoom room, Rng rng, int network, int id)
    {
        int side = rng.Int(0, 3);
        float y = room.Min.Y + rng.Range(0.1f, 0.5f);
        Vector3 p, n;
        switch (side)
        {
            case 0: p = new Vector3(room.Min.X, y, rng.Range(room.Min.Z + 0.4f, room.Max.Z - 0.4f)); n = Vector3.Right; break;
            case 1: p = new Vector3(room.Max.X, y, rng.Range(room.Min.Z + 0.4f, room.Max.Z - 0.4f)); n = Vector3.Left; break;
            case 2: p = new Vector3(rng.Range(room.Min.X + 0.4f, room.Max.X - 0.4f), y, room.Min.Z); n = Vector3.Back; break;
            default: p = new Vector3(rng.Range(room.Min.X + 0.4f, room.Max.X - 0.4f), y, room.Max.Z); n = Vector3.Forward; break;
        }
        return new Crack { Id = id, Kind = CrackKind.Crack, Position = p, Normal = n, Network = network, Size = rng.Range(0.18f, 0.32f) };
    }

    // ---- Into a sim -------------------------------------------------------------------

    /// <summary>
    /// Puts a place's population into a sim, in the place's solid space: cracks, food and nests,
    /// then every creature on its surface (ceiling ambushers on the ceiling, vent and door
    /// ambushers hidden inside their holes, guards round their nests, prowlers on their beats),
    /// and a colony per subsister species so the small things keep coming back.
    /// </summary>
    public static List<Creature> Spawn(WildSim sim, PoiPopulation pop, ISolidSpace space)
    {
        var made = new List<Creature>();
        if (pop.Silent) return made;
        int crackBase = sim.Cracks.Count;
        foreach (Crack k in pop.Cracks)
        {
            k.Id = sim.Cracks.Count;
            sim.Cracks.Add(k);
        }
        foreach (Nest n in pop.Nests) sim.Nests.Add(n);
        foreach (FoodPatch f in pop.Food)
        {
            if (SpaceMath.FloorBelow(space, f.Position.X, f.Position.Y + 0.5f, f.Position.Z, 2f) is float y) f.Position = new Vector3(f.Position.X, y, f.Position.Z);
            sim.Food.Add(f);
        }
        foreach (Nest n in pop.Nests)
            if (SpaceMath.FloorBelow(space, n.Position.X, n.Position.Y + 0.5f, n.Position.Z, 2f) is float y) n.Position = new Vector3(n.Position.X, y, n.Position.Z);
        foreach (WormMass m in pop.Masses)
        {
            // Onto the surface it grows on: down onto the floor, or back against the wall.
            if (m.Normal.Y > 0.5f)
            {
                if (SpaceMath.FloorBelow(space, m.Position.X, m.Position.Y + 0.5f, m.Position.Z, 2f) is float y) m.Position = new Vector3(m.Position.X, y, m.Position.Z);
            }
            else
            {
                Vector3 into = -m.Normal;
                float t = SpaceMath.Ray(space, m.Position + m.Normal * 0.5f, into, 2f);
                m.Position = m.Position + m.Normal * 0.5f + into * t;
            }
            if (space.Density(m.Middle) > Interpenetration.Tolerance) continue; // inside something: it isn't there
            sim.Masses.Add(m);
        }

        var colonies = new Dictionary<Species, Colony>();
        foreach (PoiSpawn s in pop.Spawns)
        {
            Nest nest = s.Nest >= 0 ? pop.Nests[s.Nest] : null;
            Creature c = sim.SpawnIn(s.Species, space, s.Position, s.Up, pack: nest?.Turns);
            c.Hunger = c.Rng.Range(0.2f, 0.7f);
            if (nest != null)
            {
                c.Nest = nest;
                nest.Guards.Add(c);
            }
            if (s.Route != null)
            {
                c.Route = s.Route;
                c.RouteIndex = 0;
            }
            if (s.Hide != HideKind.None && s.Hide != HideKind.Dark)
            {
                Crack hole = s.Hole >= 0 ? pop.Cracks[s.Hole] : null;
                c.Ambush = new AmbushPoint { Kind = s.Hide, Position = Crawl.Center(c), Normal = c.Facing.Y, Hole = hole };
                if (hole != null) Brain.EnterCrack(sim, c, hole, park: true);
            }
            made.Add(c);
            if (s.Species.Role == DeepRole.Subsister)
            {
                if (!colonies.TryGetValue(s.Species, out Colony col))
                {
                    col = new Colony { Species = s.Species, Space = space, Home = c.Home };
                    colonies[s.Species] = col;
                    foreach (Crack k in pop.Cracks) if (k.Kind == CrackKind.Crack) col.Sources.Add(k);
                    sim.Colonies.Add(col);
                }
                col.Target++;
            }
        }
        return made;
    }
}
