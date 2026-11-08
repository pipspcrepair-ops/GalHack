using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Slice.Surface;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// The world's own kinds: each fall, theme and era makes its own from the bestiary's parts, on fresh
/// worlds every run. The frame worlds' kinds differ from each other; the Changed and ghouls read wrong
/// at a distance (bodies mutated, wrong shapes, bare heads); soldiers wear one uniform; the same fall
/// at low tech and high tech gives different gear, weapons and machines; and everyone on a world
/// wears its story (masks on a plague world).
/// </summary>
public partial class OccupantTest
{
    WorldLife Life(string fall = null, string theme = null, TechLevel? now = null, TechLevel? before = null)
    {
        ulong seed = Seed();
        var given = new WorldRoll.Given();
        if (fall != null) given.Set(LayerKind.Fall, fall);
        if (theme != null) given.Set(LayerKind.Theme, theme);
        WorldProfile profile = WorldProfile.Roll(seed, "Temperate", false);
        if (!profile.Inhabited) profile = WorldProfile.Roll(seed ^ 0x5EED, "Temperate", true);
        profile = profile with { Inhabited = true, Now = now ?? profile.Now, Before = before ?? profile.Before };
        WorldRoll roll = WorldRoll.Roll(_lib, seed, "Temperate", profile, false, given, (float)_rng.NextDouble());
        WorldConditions w = Conditions(roll, seed);
        WildlifePatch.Story(w, roll, profile, WorldReading.Of(roll));
        w.Peopled = true; // the test asks what the story leaves; a world nobody lives on is its own check
        return Bestiary.Roll(_data, w);
    }

    static string Kinds(WorldLife life) => string.Join(", ", life.Signature.Kinds.Select(k => k.Species.CommonName));

    void Signatures()
    {
        GD.Print("-- the world's own kinds --");
        (string Fall, string Theme)[] frames = { ("biohazard", null), ("nuclear_war", null), ("machine_uprising", null), ("civil_war", null), ("plague", null), ("slow_fade", "parade_ground") };
        var sets = new List<string>();
        foreach (var (fall, theme) in frames)
        {
            WorldLife life = Life(fall, theme);
            string kinds = Kinds(life);
            sets.Add(string.Join("|", life.Signature.Kinds.Select(k => k.Species.CommonName).OrderBy(n => n)));
            Check(!life.Signature.Empty, $"{theme ?? fall}: its own kinds are {kinds}");
        }
        Check(sets.Distinct().Count() == sets.Count, "the six frame worlds' own kinds all differ");

        // The Changed read wrong from a distance: mutated hard, wrong-shaped, bareheaded, torn; in clumps that hunt.
        WorldLife bio = Life("biohazard");
        Species changed = bio.Signature.Kinds.FirstOrDefault(k => k.Species.CommonName == "the Changed").Species;
        Check(changed != null && changed.Mutation >= 0.75f && changed.Figure.Wrongs != Wrong.None && changed.Figure.Torn
              && !changed.Figure.Wear.Any(g => HumanBody.Slot(g) == "head") && changed.Behaviour == Behaviour.Herd && changed.Stats.Hunts && changed.GroupMax >= 6,
            $"the Changed: mutation {changed?.Mutation:F2}, wrong {changed?.Figure.Wrongs}, torn, bare-headed, clumps of {changed?.GroupMin}-{changed?.GroupMax} that hunt");
        WorldLife nuke = Life("nuclear_war");
        Species ghoul = nuke.Signature.Kinds.FirstOrDefault(k => k.Species.CommonName == "ghouls").Species;
        Species mraider = nuke.Signature.Kinds.FirstOrDefault(k => k.Species.CommonName == "mutant raiders").Species;
        Check(ghoul != null && ghoul.Figure.HairStyle == "bald" && ghoul.Mutation > 0.5f && mraider != null && mraider.Behaviour == Behaviour.PackHunter && mraider.Figure.Has("scrap_armour") | mraider.Figure.Has("spike_pads"),
            $"a nuclear world: bald burnt ghouls (mutation {ghoul?.Mutation:F2}) and mutant raiders in packs ({string.Join(" ", mraider?.Figure.Wear ?? new HashSet<string>())}, {mraider?.Figure.Prop})");

        // Soldiers wear one uniform.
        WorldLife war = Life("invasion");
        Species soldier = war.Signature.Kinds.FirstOrDefault(k => k.Species.CommonName == "soldiers").Species;
        var squad = soldier?.Hosts ?? new List<Species>();
        Check(squad.Count >= 3 && squad.All(q => q.Figure.Cloth.IsEqualApprox(squad[0].Figure.Cloth)) && squad.Select(q => q.Figure.Skin.ToHtml(false) + q.Figure.Height.ToString("F2")).Distinct().Count() >= 3 && soldier.Behaviour == Behaviour.Herd,
            $"soldiers: {squad.Count} different people in one uniform ({soldier?.Figure.Cloth.ToHtml(false)}), patrolling together");

        // The era: the same fall at low tech and high tech gives different gear, weapons and machines.
        int clubs = 0, rifles = 0, sealed_ = 0, n = 6;
        string lowHulls = "", highHulls = "";
        int lowLegs2 = 0, highSlim = 0;
        for (int i = 0; i < n; i++)
        {
            WorldLife low = Life("civil_war", now: TechLevel.Low), high = Life("civil_war", now: TechLevel.High);
            Species lm = low.Signature.Kinds.First(k => k.Species.CommonName == "militia").Species, hm = high.Signature.Kinds.First(k => k.Species.CommonName == "militia").Species;
            if (lm.Figure.Prop == "club") clubs++;
            if (hm.Figure.Prop == "rifle") rifles++;
            if (hm.Figure.Has("suit_helmet") || hm.Figure.Dress == "suit") sealed_++;
            WorldLife lowM = Life("machine_uprising", now: TechLevel.Low, before: TechLevel.Low), highM = Life("machine_uprising", now: TechLevel.High, before: TechLevel.High);
            Species lr = lowM.Signature.Machines.First(), hr = highM.Signature.Machines.First();
            lowHulls += lr.Colors.Base.ToHtml(false) + " ";
            highHulls += hr.Colors.Base.ToHtml(false) + " ";
            if (lr.Body.Legs == 2 && lr.CommonName.Contains("clockwork")) lowLegs2++;
            if (hr.Body.Legs != 4 && (hr.Figure?.Build ?? 0f) < 0.2f && hr.Body.Width < 0.75f * 1.2f) highSlim++;
        }
        Check(clubs >= n - 1 && rifles >= n - 1 && sealed_ >= n / 2, $"a civil war at low tech arms its militia with clubs ({clubs} of {n}), at high tech with rifles ({rifles}) and sealed helmets or suits ({sealed_})");
        Check(lowLegs2 == n && highSlim == n, $"machines: low-tech worlds' clockwork, two-legged, brass ({lowHulls.Trim()}); high-tech worlds' sleek in white or graphite ({highHulls.Trim()})");

        // Everyone on a world wears its story: masks among a plague world's townsfolk.
        float Masked(WorldLife life)
        {
            int m = 0;
            for (int i = 0; i < 80; i++)
            {
                Species p = life.People.Person(Seed(), PersonRole.Worker, Occupancy.Friendly);
                if (p.Figure.Wear.Any(g => HumanBody.Slot(g) == "mouth")) m++;
            }
            return m / 80f;
        }
        WorldLife plagued = Life("plague"), faded = Life("slow_fade");
        float plague = Masked(plagued);
        Check(plague >= 0.45f && plagued.People.Flavour?.Wear.ContainsKey("filter_mask") == true && faded.People.Flavour == null,
            $"a plague world's people go masked: {plague:P0} of them (its story's look on everyone; after a slow fade, none)");

        // A world nobody lives on keeps only what has gone wild, its machines and its beasts.
        WorldLife emptied = Life("plague");
        emptied.World.Peopled = false;
        WorldLife quiet = Bestiary.Roll(_data, emptied.World);
        Check(quiet.Signature.Kinds.All(k => k.Species.Figure == null || k.Species.Figure.Occupancy == Occupancy.Feral), $"a plague world nobody lives on has no living people of its own ({Kinds(quiet)})");
    }
}
