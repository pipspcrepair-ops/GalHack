using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Material = PoiGenerator.Gen.Material;

namespace PoiGenerator;

public partial class PoiTest
{
    static readonly HashSet<Material> Modern = new() { Material.Concrete, Material.Steel, Material.Alloy, Material.Resin, Material.Armour, Material.Scrap };
    static readonly HashSet<Material> Stellar = new() { Material.Alloy, Material.Resin, Material.Glass, Material.Lumen };

    /// <summary>A layout's shape and stuff in one number: two that match are the same building.</summary>
    static ulong Print(Layout l)
    {
        ulong h = 1469598103934665603UL;
        foreach (var (p, b) in l.Blocks.Blocks())
            h = Hash.Combine(h, Hash.Combine((ulong)(uint)Hash.Cell(p), (ulong)b.Type << 8 | (ulong)b.Material));
        return h;
    }

    /// <summary>
    /// Places by era, on fresh seeds: every spawn lays out afresh (two spawns of a family are different
    /// buildings, and the time it takes is no worse than the authored seeds'); a low-tech world's old places
    /// are timber, stone and thatch with pitched roofs and few windows, a stellar one's alloy and glass with
    /// light strips; nothing the era pass reshapes breaks a place's walk; a world asks only for what its tech
    /// had, and a fall that brings a launch field to a low-tech world gets its age's own camp instead; no
    /// culture mixes strange and regular houses.
    /// </summary>
    void Eras()
    {
        var rand = new Random();
        var families = _lib.Families.Where(f => !f.Space).ToList();

        // ---- every spawn its own layout
        int differ = 0, same = 0, failed = 0;
        double authoredMs = 0, freshMs = 0;
        var sameIds = new List<string>();
        // (A city's buildings are laid lot by lot by its planner, each from its own lot: size, storeys, style.)
        foreach (FamilyDef f in families.Where(x => !x.Tags.Contains("city")))
        {
            TechLevel tech = f.Techs.Count > 0 ? WorldProfile.Parse(f.Techs[rand.Next(f.Techs.Count)]) : (TechLevel)rand.Next(3);
            int v = rand.Next(f.Variants.Count);
            var hints = new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech);
            var sw = Stopwatch.StartNew();
            Layout authored = PoiFactory.TryLayout(_lib, f, v, f.Rarity, null, hints, out _);
            authoredMs += sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            Layout a = PoiFactory.SpawnLayout(_lib, f, v, f.Rarity, (ulong)rand.NextInt64(), hints, out _);
            Layout b = PoiFactory.SpawnLayout(_lib, f, v, f.Rarity, (ulong)rand.NextInt64(), hints, out _);
            freshMs += sw.Elapsed.TotalMilliseconds / 2;
            if (a == null || b == null || authored == null) { failed++; continue; }
            if (Print(a) != Print(b)) differ++;
            else { same++; sameIds.Add(f.Id); }
        }
        Check(same * 20 <= differ + same && failed <= families.Count / 20,
            $"two spawns of a family are different buildings: {differ} of {differ + same} families differ{(sameIds.Count > 0 ? $" (alike: {string.Join(", ", sameIds)})" : "")}, {failed} would not lay out");
        Check(freshMs <= authoredMs * 1.6 + 200,
            $"a fresh layout takes no longer than an authored one: {freshMs / families.Count:0.0} ms against {authoredMs / families.Count:0.0} ms a family");

        // ---- the era kits read their era
        var kitMix = new Dictionary<TechLevel, (int Fabric, int Modern, int Stellar, int Places, int Pitched, int Strips, int Panes, int Walls)>();
        int walkBroken = 0, walked = 0;
        (int L, int H) glassBefore = (0, 0), glassAfter = (0, 0);
        string first = "";
        foreach (TechLevel tech in new[] { TechLevel.Low, TechLevel.Mid, TechLevel.High })
        {
            var m = (Fabric: 0, Modern: 0, Stellar: 0, Places: 0, Pitched: 0, Strips: 0, Panes: 0, Walls: 0);
            EraKit era = EraBook.Current.Of(tech)!;
            foreach (FamilyDef f in families.Where(f => !WorldProfile.Unbuilt(f) && (f.Techs.Count == 0 || f.Techs.Contains(WorldProfile.Tag(tech)))))
            {
                var fit = f.Variants.Where(x => x.Tech.Length == 0 || x.Tech == WorldProfile.Tag(tech)).ToList();
                if (fit.Count == 0) continue;
                ulong seed = (ulong)rand.NextInt64();
                Dir hintsFront = Dirs.Horizontal[rand.Next(4)];
                Layout l = PoiFactory.SpawnLayout(_lib, f, fit[rand.Next(fit.Count)].Index, f.Rarity, seed, new LayoutHints(hintsFront, tech), out _);
                if (l == null || !EraPass.Applies(l)) continue;
                m.Places++;
                bool pitched = false, strips = false;
                foreach (var (p, b) in l.Blocks.Blocks())
                {
                    if (b.Role is Role.Wall or Role.Roof && b.Type is BlockType.Cube or BlockType.Ramp or BlockType.Corner && !era.Keep.Contains(b.Type))
                    {
                        m.Fabric++;
                        if (Modern.Contains(b.Material)) m.Modern++;
                        if (Stellar.Contains(b.Material)) m.Stellar++;
                    }
                    if (b.Role == Role.Wall && b.Type == BlockType.Cube) m.Walls++;
                    if (b.Role == Role.Window && b.Material == Material.Glass) m.Panes++;
                    if (b.Role == Role.Roof && b.Type == BlockType.Ramp) pitched = true;
                    if (b.Role == Role.Decor && b.Material == Material.Lumen) strips = true;
                }
                if (pitched) m.Pitched++;
                if (strips) m.Strips++;
                // The era pass never cuts a place's way through: walked as built, on the same seed, and in its age.
                if (tech != TechLevel.Mid && walked < 160)
                {
                    walked++;
                    EraPass.Enabled = false;
                    var asBuilt = PoiFactory.SpawnLayout(_lib, f, l.Variant?.Index ?? 0, f.Rarity, seed, new LayoutHints(hintsFront, tech), out _);
                    EraPass.Enabled = true;
                    var inst = PoiFactory.Spawn(_lib, l, PoiState.Active, seed);
                    int before = asBuilt == null ? 0 : Validator.Check(PoiFactory.Spawn(_lib, asBuilt, PoiState.Active, seed)).Count;
                    int Glass(Layout x) => x.Blocks.Blocks().Count(c => c.Block.Role == Role.Window && c.Block.Material == Material.Glass);
                    if (asBuilt != null) { if (tech == TechLevel.Low) { glassBefore.L += Glass(asBuilt); glassAfter.L += Glass(l); } else { glassBefore.H += Glass(asBuilt); glassAfter.H += Glass(l); } }
                    var problems = Validator.Check(inst);
                    if (problems.Count > before) { walkBroken++; if (first.Length == 0) first = $" (first: {f.Id} in {WorldProfile.Tag(tech)}: {problems[0]})"; }
                }
            }
            kitMix[tech] = m;
        }
        var (lo, mid, hi) = (kitMix[TechLevel.Low], kitMix[TechLevel.Mid], kitMix[TechLevel.High]);
        float Share(int a, int b) => b == 0 ? 0f : a / (float)b;
        Check(Share(lo.Modern, lo.Fabric) < 0.08f && lo.Places > 10,
            $"low-tech kits read low: {Share(lo.Modern, lo.Fabric):P1} of {lo.Fabric} wall and roof blocks in concrete, steel or alloy over {lo.Places} places (mid {Share(mid.Modern, mid.Fabric):P0})");
        Check(Share(lo.Pitched, lo.Places) > 0.3f && glassAfter.L < glassBefore.L * 0.75f,
            $"low-tech places have pitched roofs and few windows: {lo.Pitched} of {lo.Places} pitched, {glassAfter.L} panes kept of {glassBefore.L} the kits drew");
        Check(Share(hi.Stellar, hi.Fabric) > 0.55f && Share(hi.Strips, hi.Places) > 0.5f && glassAfter.H > glassBefore.H,
            $"high-tech kits read high: {Share(hi.Stellar, hi.Fabric):P0} alloy, resin, glass and light (mid {Share(mid.Stellar, mid.Fabric):P0}), {hi.Strips} of {hi.Places} with light strips, {glassAfter.H} panes against {glassBefore.H} the kits drew");
        Check(walkBroken == 0, $"the era pass breaks no place's walk: {walkBroken} of {walked} places gained a problem{first}");

        // ---- a world asks only for what its tech had, and its falls bring their age's own places
        int plans = 0, lowPlaces = 0, overTech = 0, launch = 0, camps = 0, lowExodus = 0;
        string over = "";
        ulong baseSeed = (ulong)rand.NextInt64();
        for (ulong i = 0; i < 60; i++)
        {
            ulong seed = Hash.Combine(baseSeed, i);
            TechLevel before = (TechLevel)(i % 3);
            WorldProfile profile = WorldProfile.Roll(seed, "Temperate");
            if (!profile.Inhabited) continue;
            profile = profile with { Before = before, Now = (TechLevel)Math.Min((int)before, (int)profile.Now) };
            if (before == TechLevel.Low && profile.Starport > Starport.D) profile = profile with { Starport = Starport.D };
            var given = new WorldRoll.Given();
            // Every other low world takes in refugees or queues to leave: falls that bring launch fields.
            if (before == TechLevel.Low && i % 2 == 0) given.Set(LayerKind.Fall, i % 4 == 0 ? "tide_of_refugees" : "exodus");
            WorldRoll roll = WorldRoll.Roll(_lib, seed, "Temperate", profile, false, given);
            if (roll == null) continue;
            RegionPlan plan = RegionPlan.Make(_lib, new TestLand(seed), roll, profile, seed, (8000, 4000), false);
            plans++;
            if (before == TechLevel.Low && i % 2 == 0) lowExodus++;
            foreach (PlannedPlace p in plan.Places)
            {
                FamilyDef f = _lib.Family(p.Family);
                if (!profile.TechFits(f)) { overTech++; if (over.Length == 0) over = $" (first: {f.Id} on a {WorldProfile.Tag(before)} world)"; }
                if (before != TechLevel.Low) continue;
                lowPlaces++;
                if (f.Tags.Contains("scrappy")) launch++;
                if (f.Tags.Contains("exodus")) camps++;
            }
        }
        Check(overTech == 0 && plans > 30, $"no place needs more tech than its world had: {overTech} over in {plans} plans{over}");
        Check(launch == 0 && camps > 0, $"low-tech worlds' falls bring their age's camps, not launch fields: {launch} launch fields, {camps} caravan and road camps over {lowExodus} refugee and exodus worlds ({lowPlaces} places)");

        // ---- one people to a culture: strange and regular houses never share one
        int cultures = 0, strange = 0, mixed = 0;
        for (int i = 0; i < 400; i++)
        {
            var hints = new HomeHints(rand.Next(), (Climate)rand.Next(4), (float)rand.NextDouble());
            var c = HomeCulture.Of(_lib.Homes, hints, (TechLevel)rand.Next(3));
            cultures++;
            if (c.Strange) strange++;
            if (c.Odd.Count > 0 || c.Families.Any(x => x.Family.Odd != c.Strange)) mixed++;
        }
        Check(mixed == 0 && strange > 0 && strange < cultures / 3, $"no culture mixes strange and regular houses: {mixed} mixed of {cultures}, {strange} strange");
    }
}
