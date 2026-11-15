using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Slice.Places;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Inside every place (OccupantTest --inside; owner: "an ominous tower POI, and there were no spawns inside at all"):
/// every family that holds anything, seated as the game seats it on fresh worlds (its state by its own odds, a
/// faction's only as a ruin, who holds it rolled from the world's mix, the world's livability), and filled into its
/// blocks as the game fills it (those where no open air reaches taken out). Then:
/// - whoever the roll says holds it is there (its people where it has rooms for them, or what lives in it), unless a
///   barren world left it silent;
/// - the storeys off the way in's with a dark room, below ground or up a tower, have something on them (a threat or a person), or are
///   the storeys its keepers live on (theirs and one either side); one in five may be quiet (place-patterns E3, E7);
/// - that floor, where nobody keeps it, holds at least the easiest places' density less the quiet rooms (E2's 0.3 threats a
///   100 m2, a third of the rooms quiet at most by E3: 0.2).
/// Counts by family: threats a 100 m2 of all its floor and of the floor off the way in's storey nobody keeps, people a
/// place, and the storeys held.
/// </summary>
public partial class OccupantTest
{
    sealed class InsideTally
    {
        public int Places, Silent, Missing, Storeys, Held, Bare, Thin, Threats, People, FarThreats;
        public float Floor, Far;
        public string Example = "";
        public void Note(string what) { if (Example == "") Example = $" (e.g. {what})"; }
    }

    void Inside()
    {
        GD.Print("-- inside every place --");
        var livable = Slice.Game.GameData.Shared.Rules.Wildlife.Livable;
        var families = _lib.Families.Where(f => f.Types.Count == 0 && !f.Space && f.StateWeights.Any(w => w > 0f)).ToList();
        var by = families.ToDictionary(f => f.Id, _ => new InsideTally());
        for (int w = 0; w < 4; w++)
        {
            string arch = Archetypes[_rng.Next(Archetypes.Length)];
            ulong seed = Seed();
            WorldRoll roll = Roll(seed, arch);
            WorldProfile profile = WorldProfile.Roll(seed, arch, false);
            WorldConditions cond = Conditions(roll, seed);
            Slice.Surface.WildlifePatch.Story(cond, roll, profile, WorldReading.Of(roll));
            var deep = new PlaceFauna.Deep(Bestiary.Roll(_data, cond), null, livable.GetValueOrDefault(arch, 0.5f));
            foreach (FamilyDef f in families)
                for (int i = 0; i < (f.Id == "ominous_tower" ? 3 : 1); i++) InsidePlace(f, roll, profile, deep, by[f.Id]);
        }
        foreach (var (id, t) in by.Where(kv => kv.Value.Places > 0).OrderBy(kv => kv.Key))
            GD.Print($"    {id}: {t.Places} places ({t.Silent} silent), {t.Threats * 100f / Math.Max(1f, t.Floor):F2} threats a 100 m2 ({t.FarThreats * 100f / Math.Max(1f, t.Far):F2} off the way in's storey where nobody keeps it), "
                + $"{t.People / (float)t.Places:F1} people a place; {t.Held} of {t.Storeys} storeys off the way in's held{(t.Missing + t.Bare + t.Thin > 0 ? $"; {t.Missing} without their holders, {t.Bare} bare, {t.Thin} thin{t.Example}" : "")}");
        var all = by.Values.ToList();
        int places = all.Sum(t => t.Places), silent = all.Sum(t => t.Silent);
        string Worst(Func<InsideTally, int> of) => string.Join("; ", by.Where(kv => of(kv.Value) > 0).OrderByDescending(kv => of(kv.Value)).Take(5).Select(kv => $"{kv.Key} {of(kv.Value)}{kv.Value.Example}"));
        Check(places > 250 && silent <= places * 0.1f, $"{places} places of {by.Count(kv => kv.Value.Places > 0)} families on fresh worlds: {silent} silent (only a barren world's may be)");
        Check(all.Sum(t => t.Missing) == 0, $"whoever holds a place is in it: {all.Sum(t => t.Missing)} places without their holders ({Worst(t => t.Missing)})");
        Check(all.Sum(t => t.Bare) == 0, $"the storeys off the way in's hold something, up a tower as below ground: {all.Sum(t => t.Held)} of {all.Sum(t => t.Storeys)}; {all.Sum(t => t.Bare)} places with more bare than one in five ({Worst(t => t.Bare)})");
        Check(all.Sum(t => t.Thin) == 0, $"the floor off the way in's storey that nobody keeps holds 0.2 threats a 100 m2 at least (place-patterns E2, E3): {all.Sum(t => t.Thin)} places thinner ({Worst(t => t.Thin)})");
    }

    /// <summary>One place of a family as the game seats and fills it, counted into its family's tally.</summary>
    void InsidePlace(FamilyDef f, WorldRoll roll, WorldProfile profile, PlaceFauna.Deep deep, InsideTally t)
    {
        // Its state by its own odds; a faction's place only as a ruin, as on a start world.
        float pick = (float)_rng.NextDouble() * f.StateWeights.Sum();
        int si = 0;
        while (si < f.StateWeights.Length - 1 && (pick -= f.StateWeights[si]) >= 0f) si++;
        var state = PoiGenerator.Gen.ThemeDef.FactionOwned(f) ? PoiState.Ruined : (PoiState)si;
        ulong seed = Seed();
        OccupancyRoll occ = OccupancyLayer.Applies(f, state) ? OccupancyLayer.Roll(roll.Occupancy, f, seed, null, OccupancyLayer.Peopled(roll, profile) && OccupancyLayer.Livable(f, state)) with { Tags = roll.Tags } : null;
        PoiInstance inst;
        try { inst = PoiFactory.Spawn(_lib, PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, Seed()), state, seed, null, occ); }
        catch (InvalidOperationException) { return; } // the generator gave up on a seed: the game skips such a place
        PlaceMemory m = Memory(inst);
        PlaceFauna fauna = PlaceFauna.Plan(m, deep);
        PoiPlan plan = fauna.Rooms;
        var walkable = plan.Rooms.Where(r => r.Kind is not (RoomKind.Vent or RoomKind.Duct) && r.Size.Y >= 2f).ToList();
        if (walkable.Count == 0) return; // (a sign or a camp on open ground: no inside to it)
        t.Places++;
        PoiPopulation pop = fauna.Population;
        Townsfolk folk = inst.Townsfolk;
        int people = folk?.People.Count ?? 0;
        t.People += people;
        string who = $"{state} {occ?.Main.ToString() ?? "unheld"} ({plan.Holder}), {pop.Note}";
        // Only a barren world's places may be silent.
        bool barren = deep.Livable < 0.35f;
        if (pop.Silent && people == 0)
        {
            if (barren) t.Silent++;
            else
            {
                t.Missing++;
                t.Note($"{who}: silent");
            }
            return;
        }
        // Into its blocks as the game puts them, each by the room it stands in (the highest floor under it).
        var terrain = new Wildlife.Terrain((x, z) => -50f, 64f);
        var sim = new WildSim(_data, terrain, new Wildlife.NavGrid(terrain.HalfSize, 3f));
        PoiFauna.Spawn(sim, pop, PlaceFauna.SpaceOf(m));
        PlaceFauna.CullUnreached(sim, PlaceFauna.Reach(inst, plan));
        PoiRoom RoomOf(Vector3 p) => walkable.Where(r => p.X >= r.Min.X - 0.6f && p.X <= r.Max.X + 0.6f && p.Z >= r.Min.Z - 0.6f && p.Z <= r.Max.Z + 0.6f && r.Min.Y <= p.Y + 0.6f && p.Y < r.Max.Y + 0.6f)
            .OrderByDescending(r => r.Min.Y).FirstOrDefault();
        var threats = sim.Creatures.Where(c => !c.Removed && !c.Species.Passive && c.Species.Role != DeepRole.Subsister).Select(c => RoomOf(c.Species.Climbs ? Crawl.Center(c) : c.Position)).ToList();
        t.Threats += threats.Count;
        t.Floor += walkable.Sum(r => r.Size.X * r.Size.Z);
        // Whoever the roll says holds it: people where it has rooms they could settle in, else what lives in it (the nesters' bugs too).
        Occupant? main = occ?.Main;
        bool peopleHold = main is Occupant.Scavengers or Occupant.Squatters or Occupant.Gang or Occupant.Faction or Occupant.Cult or Occupant.Keepers or Occupant.Mimics && occ.Second == null;
        // (People who found no room to settle, or machines no beat or post they fit, leave it nobody's: then it holds what any place does.)
        bool nobody = plan.Holder == PlaceHolder.Empty || main is null or Occupant.Nesters || peopleHold && people == 0;
        bool there = peopleHold && people >= 2 || threats.Count > 0 || pop.Masses.Count > 0 || !peopleHold && people > 0 || nobody && sim.Creatures.Any(c => !c.Removed);
        if (!there)
        {
            t.Missing++;
            t.Note($"{who}: {people} people, {sim.Creatures.Count(c => !c.Removed)} creatures");
        }
        // The storeys off the way in's (each its rooms at one storey of the layout), and those its keepers live on: their people's spots, their machines' posts, one either side.
        List<PlacedModule> modules = inst.Layout.Modules;
        int entry = inst.Layout.EntranceModule >= 0 && inst.Layout.EntranceModule < modules.Count ? modules[inst.Layout.EntranceModule].Storey : 0;
        IEnumerable<int> posts = plan.Holder == PlaceHolder.Machines ? inst.Enemies.Select(e => e.Module) : folk?.Spots.Select(s => s.Module) ?? Enumerable.Empty<int>();
        var theirs = posts.Where(i => i >= 0 && i < modules.Count).Select(i => modules[i].Storey).ToHashSet();
        bool keepers = plan.Holder is PlaceHolder.People or PlaceHolder.Machines || inst.State == PoiState.Active;
        int storeys = 0, held = 0, farThreats = 0;
        float far = 0f;
        // (A storey whose rooms all have a working light may be quiet: the worst of the dark keep out of it.)
        foreach (var storey in walkable.GroupBy(r => modules[r.Index].Storey).Where(g => g.Key != entry && g.Any(r => !r.Lit && r.Kind is not (RoomKind.Corridor or RoomKind.Entrance or RoomKind.Descent))))
        {
            storeys++;
            bool kept = keepers && (theirs.Count == 0 || theirs.Any(s => Math.Abs(s - storey.Key) <= 1));
            int here = threats.Count(r => r != null && storey.Contains(r));
            if (kept || here > 0) held++;
            if (kept) continue;
            far += storey.Sum(r => r.Size.X * r.Size.Z);
            farThreats += here;
        }
        (t.Storeys, t.Held, t.Far, t.FarThreats) = (t.Storeys + storeys, t.Held + held, t.Far + far, t.FarThreats + farThreats);
        if (barren && farThreats == 0) return; // (the floor nobody keeps left silent, as a barren world's may be)
        if (storeys - held > (int)MathF.Ceiling(storeys * 0.2f))
        {
            t.Bare++;
            t.Note($"{who}: {held} of {storeys} storeys off the way in's hold something; {threats.Count} threats, {people} people");
        }
        if (far >= 300f && farThreats * 100f / far < 0.2f)
        {
            t.Thin++;
            t.Note($"{who}: {farThreats} threats on {far:F0} m2 off the way in's storey nobody keeps");
        }
    }
}
