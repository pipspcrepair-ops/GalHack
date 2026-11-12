using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>What the room was for (the generator's purpose: galley, ward, storage...), or empty.</summary>
    public string Purpose = "";
    /// <summary>A working light keeps it lit: the worst of the dark keep out of it.</summary>
    public bool Lit;
    /// <summary>Caches in it, and the best of their tiers (loot worth having is guarded).</summary>
    public int Loot, LootTier;
    /// <summary>Open ground of the place (a yard or a camp's pitch), not a room: only its holders walk it.</summary>
    public bool Yard;
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

/// <summary>
/// Who holds a place, as far as its creatures care: the game takes it from the place's rolled
/// occupant (who the world's theme and fall put there). Wild is a place nobody's roll speaks for
/// (a living place, a wreck): its state alone decides.
/// </summary>
public enum PlaceHolder
{
    Wild,
    /// <summary>Nobody: vermin at most.</summary>
    Empty,
    /// <summary>Nesters: nests room after room, and the bugs that keep them.</summary>
    Nesters,
    /// <summary>Twisted beasts denned in it (and the fall's own changed kinds, where it has them).</summary>
    Beasts,
    /// <summary>The Hollow growth's: its beasts, fungus-taken, through the rooms.</summary>
    Hollows,
    /// <summary>The Husks' den (with <see cref="PoiPlan.Husks"/>).</summary>
    Husks,
    /// <summary>People hold it (the game's townsfolk): they keep it clear, vermin at most down in the dark.</summary>
    People,
    /// <summary>Machines on their rounds: no wildlife lives where they walk, vermin at most.</summary>
    Machines,
}

/// <summary>Everything the population function needs to know about a place.</summary>
public sealed class PoiPlan
{
    public ulong Seed;
    public PoiTheme Theme;
    public PlaceState State = PlaceState.Abandoned;
    /// <summary>Who holds it, from its occupant; <see cref="PlaceHolder.Wild"/> where nothing was rolled.</summary>
    public PlaceHolder Holder = PlaceHolder.Wild;
    /// <summary>The holder's own kinds, as the world rolled them: its machines, its twisted beasts, its Hollows, its changed people. Those walk the place.</summary>
    public readonly List<Species> Holders = new();
    /// <summary>What the world's dark holds beside the deep's own kinds (its hunters, twisted, changed, machines, Hollows): the dark rooms get most of these.</summary>
    public readonly List<Species> Dark = new();
    /// <summary>An armed group that now and then holds out down in the dark (the world's armed people), or none.</summary>
    public Species Armed;
    /// <summary>A wrecked ship (crashed or drifting): its holds, ducts and engine rooms get nesters.</summary>
    public bool Derelict;
    /// <summary>How livable its world is, 0..1: a harsh world's places hold less (an overrun place holds what the Bloom brought).</summary>
    public float Livable = 1f;
    /// <summary>A Husk den (its occupancy is the Turned): Husks in its rooms, worm masses on its floors and walls. Shared: it shares the place with others (by floors, a front line, or in layers), so fewer.</summary>
    public bool Husks, Shared;
    /// <summary>
    /// How far gone a den is, 0..1 (the game takes it from the place's occupancy roll): light, and
    /// almost nothing shows (a few Husks in the back rooms, a faint vein in a corner); severe, and
    /// it is worm masses and veins room after room. Either way nothing shows from outside.
    /// </summary>
    public float Severity = 1f;
    public readonly List<PoiRoom> Rooms = new();
    /// <summary>The place's open ground (yards, a camp's pitches): kept apart from its rooms, for those that hold it.</summary>
    public readonly List<PoiRoom> Yards = new();
    /// <summary>
    /// The growth's beasts, where the growth took the place (its state is infested, whoever else
    /// holds it): a place the Hollow took ALWAYS has them, in it and round it (the owner).
    /// </summary>
    public readonly List<Species> Hollows = new();
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
    /// <summary>Where a den's veins start without a mass (the faint one in a light den's corner): a point on a surface, its normal, how far it spreads (0..1).</summary>
    public readonly List<(Vector3 At, Vector3 Normal, float Spread)> VeinRoots = new();
    /// <summary>A den's severity (<see cref="PoiPlan.Severity"/>): how thick and far its veins spread.</summary>
    public float Severity;
    /// <summary>The place's seed (its veins grow from it).</summary>
    public ulong Seed;
    /// <summary>A mixed encounter set up (an armed group's room, the nest's room), or none.</summary>
    public (int Armed, int Nest)? Clash;
    /// <summary>Spawns waiting to be put down out of sight (fair spawning), and put down when they can be.</summary>
    public readonly List<PoiSpawn> Deferred = new();

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
public static partial class PoiFauna
{
    public static PoiPopulation Populate(PoiPlan poi, DeepFauna fauna, DeepTables rules)
    {
        // Each place draws its own of the world's kinds for each job.
        fauna.Variant = (int)(Rng.Mix(poi.Seed, 0x7A41) % 3);
        try { return PopulateOne(poi, fauna, rules); }
        finally { fauna.Variant = 0; }
    }

    static PoiPopulation PopulateOne(PoiPlan poi, DeepFauna fauna, DeepTables rules)
    {
        var pop = new PoiPopulation { Seed = poi.Seed };
        var rng = Rng.For(poi.Seed, "poi-fauna");
        PopulationRules pr = rules.Population;
        PlaceHolder holder = poi.Holder;
        // Whoever holds a place is there: only a place nobody holds (or nobody but people) may be empty and silent.
        bool held = holder is PlaceHolder.Nesters or PlaceHolder.Beasts or PlaceHolder.Hollows or PlaceHolder.Machines;
        string state = holder is PlaceHolder.People or PlaceHolder.Machines ? nameof(PlaceState.Active) : poi.State.ToString();
        // Aliveness follows the world: a living world's places are lived in by something; a barren one's may be silent.
        if (!poi.Husks && !held && poi.Hollows.Count == 0 && rng.Chance(pr.Silent.GetValueOrDefault(state, 0.25f) * Mathf.Lerp(0.5f, 0.1f, Math.Clamp(poi.Livable, 0f, 1f))))
        {
            pop.Silent = true;
            pop.Note = "empty and silent";
            return pop;
        }
        // People and machines keep their place clear as a garrison does; the growth's places are overrun.
        bool infested = poi.State == PlaceState.Infested || holder == PlaceHolder.Hollows;
        bool active = poi.State == PlaceState.Active || holder is PlaceHolder.People or PlaceHolder.Machines;
        bool nesters = holder == PlaceHolder.Nesters, empty = holder == PlaceHolder.Empty;
        // Twisted beasts and the growth's take the hunting in their place: its own lurkers and prowlers are theirs.
        bool owned = holder is PlaceHolder.Beasts or PlaceHolder.Hollows && poi.Holders.Count > 0;
        float density = pr.Density.GetValueOrDefault(state, 1f) * (infested || nesters ? 1f : Mathf.Lerp(0.4f, 1f, poi.Livable));
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
        if (empty)
        {
            // Nobody holds it: only what lives in any place's walls, and nothing that hunts.
            foreach (PoiRoom room in poi.Rooms)
            {
                if (room.Kind is RoomKind.Vent or RoomKind.Duct) continue;
                if (room.Dark || room.Depth >= 1 || rng.Chance(0.35f)) Subsisters(pop, fauna, theme, room, rng, false, density * 0.7f, pr);
                // Nobody holds the floors above; below ground, the dark has its own all the same.
                if (room.Depth >= 1 && room.Kind != RoomKind.Corridor && rng.Chance(0.45f) && Dark(poi, fauna, theme, DeepRole.Lurker, room.Width, room.Size.Y, rng) is { } d) Lurker(pop, fauna, theme, room, rng, false, d);
            }
        }
        else
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
                    bool roomy = room.Kind is RoomKind.Room or RoomKind.Hall or RoomKind.Vault or RoomKind.Descent;
                    // Nests are one thing the deep holds, never the whole of it: deepest down, now and then.
                    if (nests && deep && roomy && (nesters || infested || poi.Derelict || rng.Chance(0.45f)))
                        NestIn(pop, fauna, theme, room, rng, infested, density, rules, poi.Derelict ? DeepRole.DerelictNester : DeepRole.Nester, poi.Derelict);
                    else if (nests && infested && d >= 1 && rng.Chance(0.3f))
                        NestIn(pop, fauna, theme, room, rng, true, density, rules, DeepRole.Nester, false);
                    // A nesters' place has its nests room after room, in the dark and in the big rooms, not only deepest down.
                    else if (nesters && roomy && room.Kind != RoomKind.Entrance && (dark || room.Kind != RoomKind.Room) && rng.Chance(0.55f))
                        NestIn(pop, fauna, theme, room, rng, infested, density, rules, DeepRole.Nester, false);
                    // The dark's hunters: the most dangerous and the most varied of the world's, more the deeper.
                    if (dark && room.Kind != RoomKind.Entrance && rng.Chance(MathF.Min(0.95f, pr.Lurker * MathF.Min(1.5f, density) * (owned ? 1.5f : 1f) * (1f + 0.25f * d))))
                        Lurker(pop, fauna, theme, room, rng, infested, owned ? Own(poi, fauna, theme, DeepRole.Lurker, room.Width, room.Size.Y, rng)
                            : rng.Chance(0.65f) ? Dark(poi, fauna, theme, DeepRole.Lurker, room.Width, room.Size.Y, rng) : null);
                    // A pack lies up in a big dark room deep down.
                    if (dark && d >= 1 && room.Kind is RoomKind.Hall or RoomKind.Room or RoomKind.Vault && room.Width >= 4f && rng.Chance(0.3f * MathF.Min(1.3f, density + 0.3f)))
                        Pack(poi, pop, fauna, theme, room, rng);
                    Subsisters(pop, fauna, theme, room, rng, infested, density, pr);
                    foreach (var (at, n) in room.Vents)
                        if (rng.Chance(pr.Ambusher * density)) Hidden(pop, fauna, theme, room, rng, infested, HideKind.Vent, at, n);
                    foreach (var (at, n) in room.Doors)
                        if (room.Kind != RoomKind.Entrance && rng.Chance(pr.Ambusher * density * 0.4f)) Hidden(pop, fauna, theme, room, rng, infested, HideKind.Door, at, n);
                    break;
            }
        }
        // However small, a nesters' place has its nest: in its biggest room.
        if (nesters && pop.Nests.Count == 0)
        {
            PoiRoom biggest = null;
            foreach (PoiRoom r in poi.Rooms)
                if (r.Kind is RoomKind.Room or RoomKind.Hall or RoomKind.Vault or RoomKind.Descent or RoomKind.Cave && (biggest == null || r.Size.X * r.Size.Z > biggest.Size.X * biggest.Size.Z)) biggest = r;
            if (biggest != null) NestIn(pop, fauna, theme, biggest, rng, infested, density, rules, DeepRole.Nester, false);
        }

        // Prowlers walk beats through the corridors, between the rooms at either end.
        if (!active && corridors.Count > 0)
        {
            int prowlers = Math.Max(corridors.Count >= 2 ? 1 : 0, (int)MathF.Round(corridors.Count * pr.Prowler * density));
            for (int i = 0; i < prowlers; i++)
            {
                PoiRoom start = corridors[rng.Int(0, corridors.Count - 1)];
                List<Vector3> route = Beat(poi, start, rng, out float width, out float height);
                Species sp = (owned ? Own(poi, fauna, theme, DeepRole.Prowler, width, height, rng) : rng.Chance(0.5f) ? Dark(poi, fauna, theme, DeepRole.Prowler, width, height, rng) : null)
                    ?? Foe(fauna, fauna.For(DeepRole.Prowler, theme, start.Depth, infested), width, height);
                if (sp == null) continue; // the way is too tight for its kind
                pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Prowler, Species = sp, Position = route[0], Room = start.Index, Route = route });
            }
        }

        Cap(poi, pop, rng, pr, state);
        // Now and then an armed group holds out down in the dark: the deepest big room, theirs.
        if (!active && poi.Armed != null && !poi.Husks && rng.Chance(0.16f)) ArmedGroup(poi, pop, fauna, theme, rng, below: empty);
        // A Husk den: theirs come after the cap, as a den is theirs before it is anything's.
        if (poi.Husks) Den(poi, pop, fauna, theme, rng);
        // Whoever holds it, after the cap too: the place is theirs before it is anything else's.
        if (poi.Holders.Count > 0 && holder is PlaceHolder.Beasts or PlaceHolder.Hollows or PlaceHolder.Machines) Held(poi, pop, fauna, theme, rng, holder);
        // Then by purpose: the growth's always, a floor by state, loot guarded, mixed encounters, the way in kept quiet.
        Purposes(poi, pop, fauna, theme, rng, rules, active, infested, empty);

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
        // Insects are the place's life, not its threat: they never count against its caps (the owner: plenty of them).
        foreach (PoiRoom room in poi.Rooms)
        {
            bool nest = pop.Spawns.Exists(s => s.Room == room.Index && s.Nest >= 0);
            int cap = nest ? pr.NestCap : pr.RoomCap + (int)(room.Size.X * room.Size.Z / MathF.Max(1f, pr.RoomArea));
            List<PoiSpawn> here = pop.Spawns.FindAll(s => s.Room == room.Index && !s.Species.Passive);
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
        while (pop.Spawns.Count(s => !s.Species.Passive) > most)
        {
            var roles = new Dictionary<DeepRole, int>();
            foreach (PoiSpawn s in pop.Spawns) if (Spare(s) && !s.Species.Passive) roles[s.Role] = roles.GetValueOrDefault(s.Role) + 1;
            if (roles.Count == 0) break;
            DeepRole busiest = default;
            int count = -1;
            foreach (var (role, n) in roles)
                if (n > count || n == count && role < busiest) { busiest = role; count = n; }
            List<PoiSpawn> from = pop.Spawns.FindAll(s => s.Role == busiest && Spare(s) && !s.Species.Passive);
            int shallowest = int.MaxValue;
            foreach (PoiSpawn s in from) shallowest = Math.Min(shallowest, Depth(s));
            from = from.FindAll(s => Depth(s) == shallowest);
            pop.Spawns.Remove(from[rng.Int(0, from.Count - 1)]);
        }
    }

    /// <summary>
    /// A Husk den. The worms spread by stealth, so a den is never plain from outside: the way in,
    /// and every room that opens straight off it, look like any other abandoned place's. Deeper in
    /// (further from the way in, behind more doors, down more levels) are the world's Husks
    /// (prowling the corridors, lurking in the rooms) and the worms gathered into masses on the
    /// floors and walls, more and bigger in the halls and deeper down, with veins spreading round
    /// them (<see cref="Veins"/>). How far in it starts and how much there is go by its severity:
    /// a light den has a few Husks in its furthest rooms and a faint vein in a corner; a severe one
    /// is masses and veins room after room once you are past the way in. Fewer where it shares the place.
    /// </summary>
    static void Den(PoiPlan poi, PoiPopulation pop, DeepFauna fauna, PoiTheme theme, Rng rng)
    {
        float share = poi.Shared ? 0.5f : 1f, sev = Math.Clamp(poi.Severity, 0f, 1f);
        pop.Severity = sev;
        Dictionary<int, int> hops = Hops(poi);
        Dictionary<int, float> far = Remoteness(poi, hops);
        Species lurker = fauna.HuskFor(theme, DeepRole.Lurker);
        PoiRoom furthest = null;
        foreach (PoiRoom room in poi.Rooms)
        {
            if (room.Kind is RoomKind.Vent or RoomKind.Duct || room.Size.Y < 2f) continue;
            // A room another occupant nests in is theirs: no Husks or worms among the eggs.
            if (pop.Spawns.Exists(s => s.Room == room.Index && s.Nest >= 0)) continue;
            // Nothing in sight of the way in: not in it, not in what opens off it.
            if (!hops.TryGetValue(room.Index, out int hop) || hop <= 0) continue;
            if (furthest == null || far[room.Index] > far[furthest.Index]) furthest = room;
            if (hop <= 1 || far[room.Index] < 1f - sev * 1.05f) continue;
            Fill(room, MathF.Max(0.35f, sev), sev);
        }
        // A place too small to hide one deeper in still has its Husk: in its furthest room, whatever that is.
        if (furthest == null)
            foreach (PoiRoom room in poi.Rooms)
                if (room.Kind is not (RoomKind.Vent or RoomKind.Duct) && room.Size.Y >= 1.8f && (furthest == null || far.GetValueOrDefault(room.Index) > far.GetValueOrDefault(furthest.Index) || room.Size.X * room.Size.Z > furthest.Size.X * furthest.Size.Z && far.GetValueOrDefault(room.Index) == far.GetValueOrDefault(furthest.Index)))
                    furthest = room;
        if (furthest == null) return;
        // However light, the furthest room has its Husk and the faintest sign of them: a vein in a corner.
        if (!pop.Spawns.Exists(s => s.Room == furthest.Index && s.Species.Husk) && lurker != null)
            pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Lurker, Species = lurker, Position = FloorPoint(furthest, rng, 0.8f), Room = furthest.Index, Hide = HideKind.Dark });
        if (!pop.Masses.Exists(m => m.Room == furthest.Index))
        {
            bool xLow = rng.Chance(0.5f), zLow = rng.Chance(0.5f);
            var corner = new Vector3(xLow ? furthest.Min.X + 0.25f : furthest.Max.X - 0.25f, furthest.Min.Y, zLow ? furthest.Min.Z + 0.25f : furthest.Max.Z - 0.25f);
            pop.VeinRoots.Add((corner, Vector3.Up, 0.15f + 0.4f * sev));
        }

        void Fill(PoiRoom room, float husksAt, float massesAt)
        {
            bool hall = room.Kind is RoomKind.Hall or RoomKind.Hold or RoomKind.Vault;
            int husks = (int)MathF.Round((rng.Int(0, 2) + (room.Depth >= 1 ? 1 : 0) + (hall ? 1 : 0)) * share * husksAt);
            for (int i = 0; i < husks; i++)
            {
                bool corridor = room.Kind == RoomKind.Corridor;
                Species sp = corridor ? fauna.HuskFor(theme, DeepRole.Prowler) : lurker;
                if (sp == null) break;
                if (corridor)
                {
                    List<Vector3> route = Beat(poi, room, rng, out _, out _);
                    pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Prowler, Species = sp, Position = route[0], Room = room.Index, Route = route });
                }
                else pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Lurker, Species = sp, Position = FloorPoint(room, rng, 0.8f), Room = room.Index, Hide = HideKind.Dark });
            }
            // Light, a mass is rare and small; severe, every room has its share.
            int masses = (int)MathF.Round((rng.Int(1, 2) + (room.Depth >= 1 ? 1 : 0) + (hall ? 1 : 0)) * MathF.Max(share, 0.6f) * massesAt * massesAt);
            for (int i = 0; i < masses; i++)
            {
                float size = MathF.Min(rng.Range(0.6f, 1.25f) * (hall ? 1.3f : 1f) * (1f + 0.15f * room.Depth) * (0.6f + 0.4f * massesAt), room.Width * 0.45f);
                var m = new WormMass { Size = size, Seed = Rng.Mix(poi.Seed, (ulong)(pop.Masses.Count * 7919 + room.Index)), Room = room.Index };
                // Who it was made of: one of the world's Husks' bodies, a hand or a foot or a face still showing.
                if (lurker?.Hosts is { Count: > 0 } hosts && rng.Chance(0.75f)) m.Host = hosts[rng.Int(0, hosts.Count - 1)];
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

    /// <summary>How many doorways from the way in each room is (0 for the way in itself); rooms cut off from it are left out.</summary>
    public static Dictionary<int, int> Hops(PoiPlan poi)
    {
        var hops = new Dictionary<int, int>();
        var byIndex = new Dictionary<int, PoiRoom>();
        foreach (PoiRoom r in poi.Rooms) byIndex[r.Index] = r;
        var open = new Queue<PoiRoom>();
        foreach (PoiRoom r in poi.Rooms)
            if (r.Kind == RoomKind.Entrance) { hops[r.Index] = 0; open.Enqueue(r); }
        if (open.Count == 0 && poi.Rooms.Count > 0)
        {
            // No marked way in (a wreck, a cave): the shallowest room is where you come in.
            PoiRoom first = poi.Rooms[0];
            foreach (PoiRoom r in poi.Rooms) if (r.Depth < first.Depth) first = r;
            hops[first.Index] = 0;
            open.Enqueue(first);
        }
        while (open.Count > 0)
        {
            PoiRoom r = open.Dequeue();
            foreach (int l in r.Links)
                if (!hops.ContainsKey(l) && byIndex.TryGetValue(l, out PoiRoom next))
                {
                    hops[l] = hops[r.Index] + 1;
                    open.Enqueue(next);
                }
        }
        return hops;
    }

    /// <summary>How deep into a place each room is, 0 at the way in to 1 for the furthest: doorways from it, and levels down counting for more.</summary>
    static Dictionary<int, float> Remoteness(PoiPlan poi, Dictionary<int, int> hops)
    {
        var far = new Dictionary<int, float>();
        float most = 0.01f;
        foreach (PoiRoom r in poi.Rooms)
        {
            if (!hops.TryGetValue(r.Index, out int h)) continue;
            far[r.Index] = h + r.Depth * 1.5f;
            most = MathF.Max(most, far[r.Index]);
        }
        foreach (int k in new List<int>(far.Keys)) far[k] /= most;
        return far;
    }

    // ---- Roles into rooms -----------------------------------------------------------------

    /// <summary>What fills a role in a room, as it fits there; nothing if that kind can't turn round in it.</summary>
    static Species Fits(DeepFauna fauna, DeepRole role, PoiTheme theme, PoiRoom room, bool infested) =>
        Foe(fauna, fauna.For(role, theme, room.Depth, infested), room.Width, room.Size.Y);

    /// <summary>
    /// A kind as it fits a space here: if it comes at you, between a child's height and a person's
    /// (<see cref="DeepFauna.Indoor"/>) and still so once fitted, else nothing; if it keeps to itself, as it fits.
    /// </summary>
    static Species Foe(DeepFauna fauna, Species s, float width, float height)
    {
        if (s == null || fauna.Indoor(s) is not { } sized || fauna.Fitted(sized, width, height) is not { } fit) return null;
        return fit.Figure != null || fit.Passive || fit.Role == DeepRole.Subsister || fit.BodyHeight >= DeepFauna.IndoorMin - 0.05f ? fit : null;
    }

    static void Ceiling(PoiPopulation pop, DeepFauna fauna, PoiTheme theme, PoiRoom room, Rng rng, bool infested, DeepRole role)
    {
        if (Fits(fauna, role, theme, room, infested) is not { } sp) return;
        Vector3 p = FloorPoint(room, rng, 0.9f);
        p.Y = room.Max.Y - 0.05f;
        // Insects are just there, on the ceiling; only what comes at you lies in wait.
        pop.Spawns.Add(new PoiSpawn { Role = role, Species = sp, Position = p, Up = Vector3.Down, Room = room.Index, Hide = sp.Passive ? HideKind.None : HideKind.Ceiling });
    }

    static void Lurker(PoiPopulation pop, DeepFauna fauna, PoiTheme theme, PoiRoom room, Rng rng, bool infested, Species own = null)
    {
        if ((own ?? Fits(fauna, DeepRole.Lurker, theme, room, infested)) is not { } sp) return;
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
        // An insect by a vent or a door is only crawling by it.
        if (sp.Passive)
        {
            pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Ambusher, Species = sp, Position = p + n * 0.3f, Up = n, Room = room.Index });
            return;
        }
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

    /// <summary>One of the dark's kinds (the world's hunters, twisted, changed, machines, Hollows) given a job here, as it fits; nothing if none can turn round in it.</summary>
    static Species Dark(PoiPlan poi, DeepFauna fauna, PoiTheme theme, DeepRole role, float width, float height, Rng rng)
    {
        if (poi.Dark.Count == 0) return null;
        int first = rng.Int(0, poi.Dark.Count - 1);
        for (int i = 0; i < poi.Dark.Count; i++)
            if (Foe(fauna, fauna.Holding(poi.Dark[(first + i) % poi.Dark.Count], role, theme), width, height) is { } fit) return fit;
        return null;
    }

    /// <summary>A pack of the dark's hunters lying up in a room: two to four of one kind, together at one end.</summary>
    static void Pack(PoiPlan poi, PoiPopulation pop, DeepFauna fauna, PoiTheme theme, PoiRoom room, Rng rng)
    {
        var packs = poi.Dark.FindAll(s => s.Behaviour is Behaviour.PackHunter or Behaviour.Herd || s.GroupMax >= 3);
        if (packs.Count == 0) return;
        Species kind = packs[rng.Int(0, packs.Count - 1)];
        if (Foe(fauna, fauna.Holding(kind, DeepRole.Lurker, theme), room.Width, room.Size.Y) is not { } sp) return;
        Vector3 at = FloorPoint(room, rng, MathF.Max(1.2f, sp.TurnCircle * 0.5f));
        for (int i = 0, n = rng.Int(2, 4); i < n; i++)
            pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Lurker, Species = sp, Position = Clamp(room, at + new Vector3(rng.Range(-1.5f, 1.5f), 0f, rng.Range(-1.5f, 1.5f)), 0.5f), Room = room.Index, Hide = HideKind.Dark });
    }

    /// <summary>An armed group holding out in the deepest big room: two to four of the world's armed people.</summary>
    static void ArmedGroup(PoiPlan poi, PoiPopulation pop, DeepFauna fauna, PoiTheme theme, Rng rng, bool below)
    {
        PoiRoom best = null;
        foreach (PoiRoom r in poi.Rooms)
            if (r.Kind is RoomKind.Room or RoomKind.Hall or RoomKind.Vault && (r.Dark && !below || r.Depth >= 1) && r.Width >= 3f && r.Size.Y >= 2.2f
                && (best == null || r.Depth > best.Depth || r.Depth == best.Depth && r.Size.X * r.Size.Z > best.Size.X * best.Size.Z)) best = r;
        if (best == null || Foe(fauna, fauna.Holding(poi.Armed, DeepRole.Lurker, theme), best.Width, best.Size.Y) is not { } sp) return;
        for (int i = 0, n = rng.Int(2, 4); i < n; i++)
            pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Lurker, Species = sp, Position = FloorPoint(best, rng, 1f), Room = best.Index, Hide = HideKind.Dark });
    }

    /// <summary>One of the holder's own kinds given a job here, as it fits a space this wide and high; nothing if none of them can turn round in it.</summary>
    static Species Own(PoiPlan poi, DeepFauna fauna, PoiTheme theme, DeepRole role, float width, float height, Rng rng)
    {
        if (poi.Holders.Count == 0) return null;
        int first = rng.Int(0, poi.Holders.Count - 1);
        for (int i = 0; i < poi.Holders.Count; i++)
            if (Foe(fauna, fauna.Holding(poi.Holders[(first + i) % poi.Holders.Count], role, theme), width, height) is { } fit) return fit;
        return null;
    }

    /// <summary>
    /// Whoever holds the place, in it: machines walking their rounds through its corridors and
    /// rooms; twisted beasts denned in its biggest room deepest in, more of them about; the growth's
    /// beasts through room after room. More in a bigger place.
    /// </summary>
    static void Held(PoiPlan poi, PoiPopulation pop, DeepFauna fauna, PoiTheme theme, Rng rng, PlaceHolder holder)
    {
        var walkable = poi.Rooms.FindAll(r => r.Kind is not (RoomKind.Vent or RoomKind.Duct) && r.Size.Y >= 2f);
        // A place with no rooms (a clearing, a camp): its holders keep its open ground.
        if (walkable.Count == 0) walkable = new List<PoiRoom>(poi.Yards);
        if (walkable.Count == 0) return;
        int n = Math.Clamp(walkable.Count / 3 + 1, 2, 6);
        var from = walkable.FindAll(r => r.Kind == RoomKind.Corridor);
        if (from.Count == 0) from = walkable;
        if (holder == PlaceHolder.Machines)
        {
            // On their rounds: each its own beat, through the rooms either side (where a frame can turn round).
            for (int i = 0, placed = 0; i < n * 4 && placed < n; i++)
            {
                PoiRoom start = i % 2 == 0 ? from[rng.Int(0, from.Count - 1)] : walkable[rng.Int(0, walkable.Count - 1)];
                List<Vector3> route = Beat(poi, start, rng, out float width, out float height);
                if (Own(poi, fauna, theme, DeepRole.Prowler, width, height, rng) is not { } sp) continue;
                pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Prowler, Species = sp, Position = route[0], Room = start.Index, Route = route });
                placed++;
            }
            return;
        }
        if (holder == PlaceHolder.Beasts)
        {
            // Their den: the biggest room deepest in, with its bones, and some of them always there.
            PoiRoom den = null;
            foreach (PoiRoom r in walkable)
                if (r.Kind is not (RoomKind.Entrance or RoomKind.Corridor) && (den == null || r.Depth > den.Depth || r.Depth == den.Depth && r.Size.X * r.Size.Z > den.Size.X * den.Size.Z)) den = r;
            if (den != null && Own(poi, fauna, theme, DeepRole.Lurker, den.Width, den.Size.Y, rng) is { } keeper)
            {
                Vector3 lair = FloorPoint(den, rng, MathF.Min(1.6f, den.Width * 0.3f));
                for (int i = 0, guards = rng.Int(2, 3); i < guards; i++)
                {
                    float a = i * MathF.Tau / guards + rng.Range(-0.3f, 0.3f);
                    pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Lurker, Species = keeper, Position = Clamp(den, lair + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * 1.6f, 0.5f), Room = den.Index, Hide = HideKind.Dark });
                }
            }
            n = Math.Max(1, n / 2);
        }
        // About the place: lying up in the dark rooms and walking between them.
        for (int i = 0; i < n; i++)
        {
            PoiRoom room = walkable[rng.Int(0, walkable.Count - 1)];
            if (i % 2 == 1 && from.Count > 0)
            {
                PoiRoom start = from[rng.Int(0, from.Count - 1)];
                List<Vector3> route = Beat(poi, start, rng, out float width, out float height);
                if (Own(poi, fauna, theme, DeepRole.Prowler, width, height, rng) is { } walker)
                    pop.Spawns.Add(new PoiSpawn { Role = DeepRole.Prowler, Species = walker, Position = route[0], Room = start.Index, Route = route });
                continue;
            }
            if (room.Kind == RoomKind.Entrance && walkable.Count > 1) continue;
            if (Own(poi, fauna, theme, DeepRole.Lurker, room.Width, room.Size.Y, rng) is { } lurk) Lurker(pop, fauna, theme, room, rng, true, lurk);
        }
    }

    /// <summary>
    /// What would have been a nest: the insects that keep the room, more the deeper it is and in an
    /// overrun place, crawling its walls, floor and ceiling (no nest is built: they keep to themselves).
    /// </summary>
    static void NestIn(PoiPopulation pop, DeepFauna fauna, PoiTheme theme, PoiRoom room, Rng rng, bool infested, float density, DeepTables rules, DeepRole role, bool derelict)
    {
        DepthRules dr = rules.Depth;
        int depth = Math.Max(room.Depth, derelict ? 1 : 0);
        if (Foe(fauna, fauna.For(role, derelict ? PoiTheme.Derelict : theme, depth, infested), room.Width, room.Size.Y) is not { } sp) return;
        int n = (int)MathF.Round((dr.Nesters[0] + dr.Nesters[1] * depth) * MathF.Min(1.6f, MathF.Max(0.6f, density)));
        n = Math.Clamp(n, 1, rules.Population.NestCap);
        for (int i = 0; i < n; i++)
        {
            // A third each on the walls, the ceiling and the floor.
            if (sp.Passive && i % 3 == 0)
            {
                Crack w = WallCrack(room, rng, 0, -1);
                pop.Spawns.Add(new PoiSpawn { Role = role, Species = sp, Position = w.Position + Vector3.Up * rng.Range(0.4f, MathF.Max(0.6f, room.Size.Y - 0.6f)), Up = w.Normal, Room = room.Index });
            }
            else if (sp.Passive && i % 3 == 1)
            {
                Vector3 c = FloorPoint(room, rng, 0.6f);
                c.Y = room.Max.Y - 0.05f;
                pop.Spawns.Add(new PoiSpawn { Role = role, Species = sp, Position = c, Up = Vector3.Down, Room = room.Index });
            }
            else pop.Spawns.Add(new PoiSpawn { Role = role, Species = sp, Position = FloorPoint(room, rng, 0.6f), Room = room.Index });
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

    /// <summary>
    /// Roots a worm mass in the surface it grows on: down onto the floor, or back against the wall,
    /// onto the surface itself, and turned to face out of it as the surface does (its base and
    /// lobes on the solid, its open, wormy side out into the room). False where there is no such
    /// surface within reach (a room's box can run past its real walls, and a mass left there
    /// hung in the air, its back to whoever saw it from beyond), or no open air before it.
    /// </summary>
    public static bool Root(ISolidSpace space, WormMass m)
    {
        Vector3 n = m.Normal.Normalized();
        if (n.Y > 0.5f)
        {
            if (SpaceMath.FloorBelow(space, m.Position.X, m.Position.Y + 0.5f, m.Position.Z, 2f) is not float y) return false;
            m.Position = new Vector3(m.Position.X, y, m.Position.Z);
        }
        else
        {
            const float Reach = 2f;
            float t = SpaceMath.Ray(space, m.Position + n * 0.5f, -n, Reach);
            if (t >= Reach) return false;
            m.Position = m.Position + n * 0.5f - n * t;
        }
        Vector3 at = m.Position + n * 0.05f;
        if (!SpaceMath.Project(space, ref at, 0f, out Vector3 surface) || surface.Dot(n) < 0.6f) return false;
        m.Position = at;
        m.Normal = surface;
        return Faces(space, m);
    }

    /// <summary>Whether a mass sits on a surface with its open side away from it: solid just behind its base, open air just before its middle (tests too).</summary>
    public static bool Faces(ISolidSpace space, WormMass m) =>
        space.Density(m.Position - m.Normal * 0.15f) > 0f && space.Density(m.Middle + m.Normal * 0.2f) < 0f
        && SpaceMath.Normal(space, m.Position + m.Normal * 0.02f).Dot(m.Normal) > 0.6f;

    // ---- Into a sim -------------------------------------------------------------------

    /// <summary>
    /// Puts a place's population into a sim, in the place's solid space: cracks, food and nests,
    /// then every creature on its surface (ceiling ambushers on the ceiling, vent and door
    /// ambushers hidden inside their holes, guards round their nests, prowlers on their beats),
    /// and a colony per subsister species so the small things keep coming back.
    /// </summary>
    public static List<Creature> Spawn(WildSim sim, PoiPopulation pop, ISolidSpace space, Func<Vector3, bool> allow = null)
    {
        // Fair spawning: nothing is put down where you'd see it appear, or right beside you. A spot that
        // fails is moved a little within reach (still in the open), else it waits (pop.Deferred) until it can be.
        if (allow != null && !pop.Silent)
        {
            var fair = Rng.For(pop.Seed, "fair");
            for (int i = pop.Spawns.Count - 1; i >= 0; i--)
            {
                PoiSpawn s = pop.Spawns[i];
                if (s.Hole >= 0 || allow(s.Position)) continue; // hidden in its hole: nothing shows
                bool moved = false;
                for (int k = 0; k < 10 && !moved; k++)
                {
                    float a = fair.Range(0f, MathF.Tau), r = fair.Range(2f, 7f);
                    Vector3 q = s.Position + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
                    if (space.Density(q + Vector3.Up * 0.5f) > 0f || !allow(q)) continue;
                    if (s.Route != null && s.Route.Count > 0) s.Route[0] = q;
                    s.Position = q;
                    moved = true;
                }
                if (!moved)
                {
                    pop.Deferred.Add(pop.Spawns[i]);
                    pop.Spawns.RemoveAt(i);
                }
            }
        }
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
        var placed = new List<WormMass>();
        foreach (WormMass m in pop.Masses)
        {
            if (!Root(space, m)) continue; // nothing there to grow on, or no room before it: it isn't there
            if (space.Density(m.Middle) > Interpenetration.Tolerance) continue; // inside something: it isn't there
            sim.Masses.Add(m);
            placed.Add(m);
        }
        sim.DenSeverity = MathF.Max(sim.DenSeverity, pop.Severity);
        // The veins round them, over the floors, up the walls and onto the ceilings.
        if (placed.Count > 0 || pop.VeinRoots.Count > 0) Veins.Grow(sim.Veins, space, placed, pop.VeinRoots, pop.Severity, pop.Seed);

        var colonies = new Dictionary<Species, Colony>();
        foreach (PoiSpawn s in pop.Spawns)
        {
            Creature c = Put(sim, pop, space, s);
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

    /// <summary>
    /// Puts down the spawns that had to wait (they'd have appeared in view, or beside you) where they
    /// can be now, out of sight; those still in view wait on. The number put down.
    /// </summary>
    public static int PutDeferred(WildSim sim, PoiPopulation pop, ISolidSpace space, Func<Vector3, bool> allow)
    {
        int n = 0;
        for (int i = pop.Deferred.Count - 1; i >= 0; i--)
        {
            PoiSpawn s = pop.Deferred[i];
            if (allow != null && !allow(s.Position)) continue;
            pop.Deferred.RemoveAt(i);
            pop.Spawns.Add(s);
            Put(sim, pop, space, s);
            n++;
        }
        return n;
    }

    /// <summary>One spawn into the sim: on its surface, by its nest, on its beat, in its hole.</summary>
    static Creature Put(WildSim sim, PoiPopulation pop, ISolidSpace space, PoiSpawn s)
    {
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
            return c;
        }
    }
}
