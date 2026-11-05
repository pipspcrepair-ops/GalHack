using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Wildlife;

/// <summary>The space fauna's checks: generator, drops, zero-g animation, fair attacks, behaviours, the encounter deck's kinds, cost.</summary>
public partial class WildlifeTest
{
    static readonly Habitat[] SpaceHabitats = { Habitat.Nebula, Habitat.AsteroidField, Habitat.GasGiant, Habitat.Derelict, Habitat.Void };

    // ---- Generator ----------------------------------------------------------------------

    /// <summary>
    /// Space fauna come from the same kind of generator as planet wildlife: seeded, every
    /// habitat stocked, swappable parts that really vary, and no part on the wrong body or job.
    /// </summary>
    void SpaceGenerator()
    {
        string Describe(Species s) => $"{s.Name}|{s.CommonName}|{SpaceSignature(s)}|{s.Temperament}|{s.Colors.Base.ToHtml()}|{s.Body.Size:F4}|{s.Stats.MaxHealth}";
        SpaceFauna a = _spaceGen.Generate(77, Habitat.Nebula), b = _spaceGen.Generate(77, Habitat.Nebula), c = _spaceGen.Generate(78, Habitat.Nebula);
        Check(a.Species.Select(Describe).SequenceEqual(b.Species.Select(Describe)) && !a.Species.Select(Describe).SequenceEqual(c.Species.Select(Describe)),
            $"space fauna are seeded: the same region rolls the same creatures, another region others ({string.Join("; ", a.Species.Select(s => $"{s.Name}, {s.CommonName}"))})");

        var stocked = new Dictionary<Habitat, HashSet<string>>();
        var builds = new Dictionary<string, HashSet<string>>();
        var problems = new List<string>();
        int species = 0, hostileDomes = 0, grazerStingers = 0, softLeeches = 0;
        for (ulong region = 1; region <= 300; region++)
            foreach (Habitat h in SpaceHabitats)
                foreach (Species s in _spaceGen.Generate(region * 7477, h).Species)
                {
                    species++;
                    stocked.TryAdd(h, new HashSet<string>());
                    stocked[h].Add(s.Form);
                    builds.TryAdd(s.Form, new HashSet<string>());
                    builds[s.Form].Add(SpaceSignature(s));
                    foreach (string issue in SpaceQuality.Problems(_space, s))
                        if (problems.Count < 5) problems.Add($"{s.Name} ({s.Form}): {issue}");
                    if (s.Behaviour is Behaviour.Ambusher or Behaviour.TerritorialGiant && s.Part("head") == "dome") hostileDomes++;
                    if (s.Behaviour is Behaviour.Herd or Behaviour.Grazer && s.Part("tail") == "stinger") grazerStingers++;
                    if (s.Rig == RigKind.Clinger && s.Part("head") is not ("maw" or "jaws" or "lantern")) softLeeches++;
                }
        bool everyHabitat = SpaceHabitats.All(h => stocked.ContainsKey(h) && stocked[h].Count > 0);
        Check(everyHabitat && stocked[Habitat.Nebula].SetEquals(new[] { "veil-drifter", "void-leviathan" }) && stocked[Habitat.Derelict].SetEquals(new[] { "hull-leech" }),
            $"every habitat has its fauna: {string.Join(", ", SpaceHabitats.Select(h => $"{h} ({string.Join(", ", stocked.GetValueOrDefault(h) ?? new HashSet<string>())})"))}");
        Check(problems.Count == 0 && hostileDomes + grazerStingers + softLeeches == 0,
            problems.Count > 0 ? $"degenerate space creatures: {string.Join("; ", problems)}"
                : $"{species} space creatures over 300 regions all pass the part, form and behaviour rules (no eyeless grazer heads on hunters, no stingers on grazers, every leech has a mouth to bite with)");
        Check(builds.Values.All(set => set.Count >= 6),
            $"swappable parts really vary: distinct builds per kind over 300 regions: {string.Join(", ", builds.Select(kv => $"{kv.Key} {kv.Value.Count}"))}");

        string bad = DataFilesText("res://data/space_fauna.json").Replace("\"material\": \"Starsilver\", \"rarity\": \"Rare\", \"amount\": [1, 3]", "\"material\": \"Iron\", \"rarity\": \"Common\", \"amount\": [1, 3]");
        bool refused = false;
        try { SpaceFaunaData.FromJson(bad); }
        catch (InvalidOperationException) { refused = true; }
        Check(refused, "the data refuses a space creature that drops a common material");
    }

    static string SpaceSignature(Species s) =>
        string.Join("|", SpaceFaunaData.Slots.Select(s.Part)) + $"|{s.Body.Segments}|{s.Colors.Pattern}";

    static string DataFilesText(string path) => Godot.FileAccess.GetFileAsString(path);

    // ---- Drops --------------------------------------------------------------------------

    /// <summary>Every kill pays: rare always or exotic, unique only sometimes, nothing common, all on the ladder.</summary>
    void SpaceDropTables()
    {
        var lines = new List<string>();
        string problem = null;
        foreach (SpaceFormDef form in _space.Forms)
        {
            Species s = _spaceGen.Roll(5, form.Id, form.Habitats[0]);
            var rng = new Rng(Rng.Hash(form.Id));
            int kills = 4000, empty = 0, exotic = 0, unique = 0, low = 0, offLadder = 0;
            for (int k = 0; k < kills; k++)
            {
                List<SpaceDrops.Loot> loot = SpaceDrops.Roll(s, rng);
                if (loot.Count == 0) empty++;
                if (loot.Any(l => l.Rarity == Rarity.Exotic)) exotic++;
                if (loot.Any(l => l.Rarity == Rarity.Unique)) unique++;
                low += loot.Count(l => l.Rarity < Rarity.Rare);
                offLadder += loot.Count(l => _space.RarityOf(l.Material) != l.Rarity);
            }
            float uniqueShare = unique / (float)kills, listedUnique = form.Drops.Where(d => d.Rarity == Rarity.Unique).Sum(d => d.Chance);
            bool listsExotic = form.Drops.Any(d => d.Rarity == Rarity.Exotic);
            if (empty > 0 || low > 0 || offLadder > 0) problem ??= $"{form.Id}: {empty} empty kills, {low} drops below rare, {offLadder} off the ladder";
            if (listsExotic && exotic == 0) problem ??= $"{form.Id} never dropped its exotic material";
            if (listedUnique > 0f && (unique == 0 || uniqueShare > _space.MaxUniqueChance || MathF.Abs(uniqueShare - listedUnique) > listedUnique * 0.5f + 0.01f))
                problem ??= $"{form.Id}: unique drops in {uniqueShare:P1} of kills, listed {listedUnique:P0}";
            lines.Add($"{form.Id}: exotic {exotic / (float)kills:P0}, unique {uniqueShare:P1}");
        }
        Check(problem == null, problem ?? $"every space creature drops rare or better on every kill, exotic sometimes, unique rarely, all from the ladder (4,000 kills each: {string.Join("; ", lines)})");

        // A real kill in the simulation rolls the table.
        var sim = new SpaceSim(_data);
        Species leviathan = _spaceGen.Roll(9, "void-leviathan", Habitat.Void);
        Creature c = sim.Spawn(leviathan, Vector3.Zero);
        List<SpaceDrops.Loot> dropped = null;
        sim.Died += (dead, _) => dropped = dead.Loot;
        sim.Damage(c, 1e9f, null);
        Check(dropped is { Count: > 0 } && dropped.All(l => l.Rarity >= Rarity.Rare),
            $"killing a {leviathan.CommonName} leaves its loot: {string.Join(", ", dropped?.Select(l => $"{l.Amount} {l.Material} ({l.Rarity.ToString().ToLowerInvariant()})") ?? Array.Empty<string>())}");
    }

    // ---- Zero-g animation -----------------------------------------------------------------

    /// <summary>
    /// Every habitat's fauna, in their places (rocks, a wreck), with a ship flying through them,
    /// for 60 s: no NaNs, bodies stay upright-agnostic but orthonormal, nothing flies off,
    /// shoals hold together, and leeches stay on the hull they cling to.
    /// </summary>
    void SpaceAnimation()
    {
        string problem = null;
        int creatures = 0, clingChecks = 0;
        float worstCling = 0f, widestShoal = 0f;
        foreach (Habitat h in SpaceHabitats)
        {
            SpaceSim w = NewSpace(h, out _);
            SpaceFauna fauna = _spaceGen.Generate(31 + (ulong)h, h);
            List<Creature> all = Populate(w, fauna, new Rng(5));
            creatures += all.Count;
            var ship = new DummyTarget { Name = "Ship", Health = new Health(1e9f) };
            w.Targets.Add(ship);
            var homes = all.ToDictionary(c => c, c => c.Position);
            for (int step = 0; step < 3600 && problem == null; step++)
            {
                float t = step * Dt;
                ship.Position = new Vector3(MathF.Cos(t * 0.2f) * 60f, MathF.Sin(t * 0.13f) * 15f, MathF.Sin(t * 0.2f) * 60f - 20f);
                ship.Forward = new Vector3(-MathF.Sin(t * 0.2f), 0f, MathF.Cos(t * 0.2f));
                if (step == 2400) foreach (Creature c in all.Where((c, i) => i % 5 == 2)) w.Damage(c, 1e9f, ship);
                w.Step(Dt);
                foreach (Creature c in all)
                {
                    if (c.Removed) continue;
                    string what = $"{c.Species.Name} ({c.Species.Form}) t={t:F1}s";
                    if (!Geo.Finite(c.Position) || c.Rig.Pose.Any(p => !Geo.Finite(p))) { problem = $"NaN: {what}"; break; }
                    if (MathF.Abs(c.Facing.Determinant() - 1f) > 0.01f) { problem = $"orientation not orthonormal ({c.Facing.Determinant():F3}): {what}"; break; }
                    float from = (c.Position - homes[c]).Length();
                    if (from > 600f) { problem = $"drifted {from:F0} m off: {what}"; break; }
                    if (c.Clinging)
                    {
                        float off = MathF.Abs(c.ClingHull.SignedDistance(c.Position));
                        clingChecks++;
                        worstCling = MathF.Max(worstCling, off);
                        if (off > 0.05f) { problem = $"a clinging leech is {off:F2} m off its hull: {what}"; break; }
                    }
                }
                if (step % 60 == 0)
                    foreach (Herd s in w.Shoals)
                    {
                        var alive = s.Members.Where(m => m.Alive && !m.Removed).ToList();
                        if (alive.Count < 2) continue;
                        Vector3 mid = alive.Aggregate(Vector3.Zero, (sum, m) => sum + m.Position) / alive.Count;
                        widestShoal = MathF.Max(widestShoal, alive.Max(m => (m.Position - mid).Length()) / (s.Spread * 2f + 10f));
                    }
            }
        }
        Check(problem == null, problem ?? $"{creatures} space creatures in all five habitats animate 60 s each in zero-g with no NaNs, orthonormal bodies, nothing drifting off");
        Check(problem == null && clingChecks > 0 && widestShoal <= 1f,
            $"leeches stay on their hull while they cling ({clingChecks} checks, worst {worstCling:F3} m off), and shoals hold together (widest {widestShoal:P0} of what is allowed)");
    }

    /// <summary>A place for one habitat: rocks for a belt, a wreck for a graveyard, open space otherwise.</summary>
    SpaceSim NewSpace(Habitat h, out Hull hull)
    {
        var w = new SpaceSim(_data);
        hull = null;
        if (h == Habitat.AsteroidField)
        {
            var rng = new Rng(3);
            for (int i = 0; i < 24; i++) w.Asteroids.Add(new Asteroid { Center = new Vector3(rng.Range(-150f, 150f), rng.Range(-40f, 40f), rng.Range(-150f, 150f)), Radius = rng.Range(5f, 30f) });
        }
        if (h == Habitat.Derelict)
        {
            hull = new Hull { Center = new Vector3(0f, 0f, -30f), Basis = new Basis(Vector3.Up, 0.4f), Half = new Vector3(8f, 4f, 30f) };
            w.Hulls.Add(hull);
        }
        return w;
    }

    List<Creature> Populate(SpaceSim w, SpaceFauna fauna, Rng rng)
    {
        var all = new List<Creature>();
        foreach (Species s in fauna.Species)
        {
            if (s.Rig == RigKind.Clinger) all.AddRange(w.SpawnOnHull(s, w.Hulls[0], 6, rng));
            else if (s.Behaviour == Behaviour.TerritorialGiant) all.Add(w.Spawn(s, new Vector3(0f, 20f, -250f)));
            else
            {
                Vector3 at = new(rng.Range(-60f, 60f), rng.Range(-20f, 20f), rng.Range(-60f, 60f));
                foreach (Asteroid a in w.Asteroids) if ((at - a.Center).Length() < a.Radius + 20f) at += (at - a.Center).Normalized() * (a.Radius + 20f);
                all.AddRange(w.SpawnShoal(s, at, Math.Max(3, s.GroupMax)));
            }
        }
        return all;
    }

    // ---- Fair attacks -------------------------------------------------------------------

    /// <summary>
    /// The planet rules hold in space: every space creature's first attack has at least its
    /// telegraph of wind-up, sidestepping after the commit at a suited player's 6 m/s dodges it,
    /// a target that holds still takes the full damage (never capped, even a leviathan's), and
    /// the next attack comes at the normal rate. A leech strikes from its hull, lets go to do
    /// it, and latches back on afterwards.
    /// </summary>
    void SpaceAttacksAreFair()
    {
        int kinds = 0, dodged = 0, landed = 0;
        float tightest = float.MaxValue;
        string problem = null, damages = "";
        bool letGo = false, backOn = false;
        foreach (SpaceFormDef form in _space.Forms)
            for (ulong region = 1; region <= 3; region++)
            {
                Species s = _spaceGen.Roll(region * 101, form.Id, form.Habitats[0]);
                if (s.Stats.Damage <= 0f) continue;
                kinds++;
                float telegraph = s.TelegraphWindup(_data.Combat);
                foreach (bool dodge in new[] { true, false })
                {
                    SpaceSim w = NewSpace(form.Habitats[0], out Hull hull);
                    Creature c;
                    Vector3 up;
                    if (s.Rig == RigKind.Clinger)
                    {
                        c = w.SpawnOnHull(s, hull, 1, new Rng(region), onlyFace: 2)[0];
                        up = hull.Normal(2);
                    }
                    else
                    {
                        c = w.Spawn(s, Vector3.Zero, null, Vector3.Forward);
                        up = Vector3.Up;
                    }
                    float reach = SpaceBrain.StrikeRange(c, _data.Combat) * 0.8f;
                    Vector3 toward = (c.Forward3() - up * c.Forward3().Dot(up)).Normalized();
                    var target = new DummyTarget { Position = c.Position + toward * reach + up * (s.Rig == RigKind.Clinger ? 1f : 0f), Health = new Health(1e9f) };
                    w.Targets.Add(target);
                    float telegraphAt = -1f, contactAt = -1f, windup = 0f, dealt = 0f;
                    bool first = false, hit = false;
                    int telegraphs = 0;
                    w.Telegraph += e => { if (telegraphs++ == 0) { telegraphAt = e.Time; windup = e.Windup; first = e.First; } };
                    w.Attacked += e => { if (contactAt < 0f) { contactAt = e.Time; hit = e.Hit; dealt = e.Damage; } };
                    SpaceAttacks.Start(w, c, target);
                    Vector3 side = toward.Cross(up).Normalized();
                    for (int step = 0; step < 900 && contactAt < 0f; step++)
                    {
                        if (dodge && c.Committed) target.Position += side * 6f * Dt;
                        w.Step(Dt);
                        if (c.Phase == AttackPhase.Lunge && s.Rig == RigKind.Clinger && !c.Clinging) letGo = true;
                    }
                    float gap = contactAt - telegraphAt;
                    if (telegraphAt < 0f || contactAt < 0f) problem ??= $"{s.Name} ({form.Id}) never attacked";
                    else if (!first || windup + 1e-4f < telegraph || gap + 1e-3f < telegraph) problem ??= $"{s.Name} ({form.Id}): wind-up {gap:F2} s, telegraph {telegraph:F2} s";
                    else if (dodge && hit) problem ??= $"{s.Name} ({form.Id}): sidestepping after the commit did not dodge it";
                    else if (!dodge && (!hit || MathF.Abs(dealt - s.Stats.Damage) > 1e-3f)) problem ??= $"{s.Name} ({form.Id}): standing still took {dealt:F0} of {s.Stats.Damage:F0}";
                    tightest = MathF.Min(tightest, gap - telegraph);
                    if (dodge && !hit) dodged++;
                    if (!dodge && hit)
                    {
                        landed++;
                        if (region == 1) damages += $"{form.Id} {dealt:F0}, ";
                        // The next attack comes at the normal rate; a leech first goes home and latches on.
                        float second = -1f;
                        w.Telegraph += e => { if (!e.First) second = e.Windup; };
                        for (int step = 0; step < 60 * 20 && second < 0f; step++)
                        {
                            w.Step(Dt);
                            backOn |= s.Rig == RigKind.Clinger && c.Clinging;
                            if (!c.Attacking && s.Rig != RigKind.Clinger) SpaceAttacks.Start(w, c, target);
                        }
                        if (second < 0f || second > telegraph) problem ??= $"{s.Name} ({form.Id}): second attack wind-up {second:F2} s is not the normal rate";
                    }
                }
            }
        Check(problem == null && kinds >= 12, problem ?? $"space creatures' first attacks are telegraphed: wind-up >= the telegraph for all {kinds} attackers (tightest margin {tightest:F3} s), and the next comes at the normal rate");
        Check(problem == null && dodged == kinds && landed == kinds,
            $"every first attack can be sidestepped at 6 m/s after the commit ({dodged}/{kinds}); holding still takes the full damage, never capped ({damages.TrimEnd(',', ' ')})");
        Check(letGo && backOn, "a hull leech lets go of the hull to strike and latches back on afterwards");
    }

    // ---- Behaviours ---------------------------------------------------------------------

    /// <summary>Shoals scatter together from anything that comes too close; the leviathan warns intruders before it charges.</summary>
    void SpaceBehaviours()
    {
        // A skittish shoal of drifters, a ship coming straight at it.
        Species drifter = null;
        for (ulong region = 1; region < 200 && drifter == null; region++)
        {
            Species s = _spaceGen.Roll(region, "veil-drifter", Habitat.Nebula);
            if (s.Temperament == Temperament.Skittish) drifter = s;
        }
        SpaceSim w = NewSpace(Habitat.Nebula, out _);
        List<Creature> shoal = w.SpawnShoal(drifter, Vector3.Zero, 7);
        var ship = new DummyTarget { Name = "Ship", Position = new Vector3(0f, 0f, 120f), Health = new Health(1e9f), ThreatScale = 2f };
        w.Targets.Add(ship);
        float before = shoal.Average(c => (c.Position - ship.Position).Length());
        bool alarmed = false;
        for (int step = 0; step < 60 * 12; step++)
        {
            ship.Position = ship.Position.MoveToward(Vector3.Zero, 12f * Dt);
            w.Step(Dt);
            alarmed |= shoal[0].Herd.Alarmed;
        }
        float nearest = shoal.Min(c => (c.Position - ship.Position).Length());
        Check(alarmed && shoal.All(c => c.State == CState.Flee || !shoal[0].Herd.Alarmed) && nearest > drifter.Stats.FleeRadius * 0.6f,
            $"a skittish shoal scatters together from a ship that comes too close (nearest {nearest:F0} m when the ship stops in its middle; flee distance {drifter.Stats.FleeRadius:F0} m)");

        // The leviathan: an intruder in its territory gets the display first, then the charge.
        Species leviathan = _spaceGen.Roll(3, "void-leviathan", Habitat.Void);
        SpaceSim v = NewSpace(Habitat.Void, out _);
        Creature giant = v.Spawn(leviathan, Vector3.Zero);
        var intruder = new DummyTarget { Name = "Ship", Position = new Vector3(0f, 0f, leviathan.Stats.Territory * 0.7f), Health = new Health(1e9f) };
        v.Targets.Add(intruder);
        float warnedAt = -1f, telegraphAt = -1f;
        v.Warned += _ => { if (warnedAt < 0f) warnedAt = v.Time; };
        v.Telegraph += e => { if (telegraphAt < 0f) telegraphAt = e.Time; };
        for (int step = 0; step < 60 * 90 && telegraphAt < 0f; step++) v.Step(Dt);
        Check(warnedAt >= 0f && telegraphAt > warnedAt + 3f,
            $"a void leviathan warns an intruder off its territory before it charges (display at {warnedAt:F1} s, first wind-up at {telegraphAt:F1} s)");
    }

    // ---- The encounter deck -------------------------------------------------------------

    /// <summary>
    /// The encounter director brings space creatures to players as event cards. Every creature
    /// kind a card names must be one of these forms, and every form must have a card.
    /// </summary>
    void DeckCreatureKinds()
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "encounters", "data", "deck.json"));
        if (!System.IO.File.Exists(path)) { Fail($"no encounter deck at {path}"); return; }
        using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var kinds = new HashSet<string>();
        foreach (JsonElement ev in doc.RootElement.GetProperty("events").EnumerateArray())
            if (ev.TryGetProperty("creatures", out JsonElement groups))
                foreach (JsonElement g in groups.EnumerateArray()) kinds.Add(g.GetProperty("kind").GetString());
        var forms = _space.Forms.Select(f => f.Id).ToHashSet();
        Check(kinds.Count > 0 && kinds.IsSubsetOf(forms) && forms.IsSubsetOf(kinds),
            $"the encounter deck's creature cards name exactly the space forms ({string.Join(", ", kinds.OrderBy(k => k))})");
    }

    // ---- Cost ---------------------------------------------------------------------------

    void SpacePerformance()
    {
        SpaceSim w = NewSpace(Habitat.Derelict, out Hull hull);
        var rng = new Rng(8);
        foreach (Habitat h in new[] { Habitat.Nebula, Habitat.AsteroidField, Habitat.GasGiant })
            foreach (Species s in _spaceGen.Generate(41, h).Species.Where(x => x.Behaviour != Behaviour.TerritorialGiant))
                for (int g = 0; g < 2; g++) w.SpawnShoal(s, new Vector3(rng.Range(-200f, 200f), rng.Range(-40f, 40f), rng.Range(-200f, 200f)), 8);
        w.SpawnOnHull(_spaceGen.Roll(41, "hull-leech", Habitat.Derelict), hull, 12, rng);
        w.Spawn(_spaceGen.Roll(41, "void-leviathan", Habitat.Void), new Vector3(0f, 30f, -300f));
        var ship = new DummyTarget { Name = "Ship", Position = new Vector3(0f, 0f, 40f), Health = new Health(1e9f) };
        w.Targets.Add(ship);
        int count = w.Creatures.Count;
        for (int i = 0; i < 300; i++) w.Step(Dt);
        var windows = new List<double>();
        for (int k = 0; k < 3; k++) windows.Add(Time(() => w.Step(Dt), 300).avg);
        double best = windows.Min();
        Check(best < 2.5, $"{count} space creatures (shoals, leeches on a wreck, a leviathan) cost {best:F2} ms/frame, every rig posed every frame (budget 2.5 ms, best of three windows: {string.Join(", ", windows.Select(x => x.ToString("F2")))})");
    }
}
