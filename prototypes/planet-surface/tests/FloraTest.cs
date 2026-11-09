using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Plants;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Poi;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Rng = Planets.Gen.Rng;

namespace PlanetSurface.Tests;

/// <summary>
/// The flora module on fresh worlds rolled for this run, never a pinned seed: two worlds of one type
/// grow measurably different plants; one world's stay the same for its seed; every species works as
/// gameplay data; regions differ and blend; nothing grows on a vacuum world; and real chunks and
/// cover cells keep their roots in the ground and off what's built.
/// </summary>
public partial class FloraTest : Node
{
    int _failures;
    static readonly ulong RunSeed = (ulong)System.Random.Shared.NextInt64(1, long.MaxValue);
    ulong _seeds = RunSeed;

    /// <summary>The body-size limits every species keeps to, so forests stay cheap.</summary>
    const int NearBudget = 400, FarBudget = 160, CoverBudget = 75, CropBudget = 240;

    void Check(bool ok, string text)
    {
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {text}");
        if (!ok) _failures++;
    }

    ulong Fresh() => _seeds = Planets.Gen.Hash.Mix(_seeds + 0x9E37);

    public override void _Ready()
    {
        // A hung run quits on its own after 20 minutes rather than wait for someone to kill it.
        _ = System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(20)).ContinueWith(_ =>
        {
            GD.PrintErr("TIMED OUT after 20 minutes: the run quits itself");
            System.Environment.Exit(2);
        });
        CallDeferred(nameof(RunAll));
    }

    async void RunAll()
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"flora test run seed {RunSeed}");
            FarBuilder.Solids ??= FloraSolidRules.Load(ProjectSettings.GlobalizePath("res://data/flora-solids.json"));
            if (OS.GetCmdlineUserArgs().Contains("--openings")) NothingOverOpenings(); // that check alone
            else
            {
            Spread();
            Differ();
            Stable();
            Working();
            Regions();
            Vacuum();
            Scatter();
            NothingOverOpenings();
            }
        }
        catch (Exception e)
        {
            Check(false, $"test crashed: {e}");
        }
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    static (WorldMap map, SkySystem sky, PlanetFlora flora) Roll(ulong seed, Archetype a, int v)
    {
        PlanetSpec spec = PlanetSpec.Roll(seed, a, v, 8) with { Modifiers = Modifier.None };
        WorldMap map = PlanetGenerator.Generate(spec);
        SkySystem sky = SkySystem.For(spec);
        return (map, sky, PlanetFlora.For(map, sky));
    }

    /// <summary>How far apart two worlds' plants are: for each of A's stand species, the gap to the nearest of its family on B, in form, numbers and colour.</summary>
    static float Gap(Herbarium a, Herbarium b)
    {
        float sum = 0f;
        int n = 0;
        foreach (Plant p in a.Plants.Where(p => p.Family < PlantFamily.Grass && !p.Mineral))
        {
            float best = float.MaxValue;
            foreach (Plant q in b.Plants.Where(q => q.Family == p.Family))
            {
                float[] x = p.Genome.Vector(), y = q.Genome.Vector();
                float numbers = x.Zip(y, (u, v) => Math.Abs(u - v)).Sum() / x.Length;
                best = Math.Min(best, (p.Form == q.Form ? 0f : 0.6f) + numbers + p.Leaf.Distance(q.Leaf));
            }
            if (best == float.MaxValue) continue;
            sum += best;
            n++;
        }
        return n == 0 ? 0f : sum / n;
    }

    void Differ()
    {
        foreach (var (name, a, v) in new[] { ("temperate", Archetype.Temperate, 0), ("rainforest", Archetype.Temperate, 1), ("dunes", Archetype.Arid, 0), ("ice", Archetype.Ice, 0), ("crystal", Archetype.Alien, 0), ("fungal", Archetype.Alien, 1) })
        {
            float least = float.MaxValue;
            int species = 0;
            var pairs = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                Herbarium x = Roll(Fresh(), a, v).flora.Book, y = Roll(Fresh(), a, v).flora.Book;
                species = Math.Max(species, x.Plants.Count);
                if (i == 0) GD.Print($"      {name}: {string.Join(", ", x.Plants.GroupBy(p => p.Family).Select(g => $"{g.Key} {string.Join("/", g.Select(p => p.Form))}"))}");
                float gap = Math.Min(Gap(x, y), Gap(y, x));
                least = Math.Min(least, gap);
                pairs.Add($"{gap:F2}");
            }
            Check(least > 0.2f && species >= 8, $"two fresh {name} worlds grow different plants: gaps {string.Join(", ", pairs)}, {species} species");
        }
        // Forests on two temperate worlds differ in what a player sees first: colour or form.
        int differ = 0, tries = 12;
        for (int i = 0; i < tries; i++)
        {
            Herbarium x = Roll(Fresh(), Archetype.Temperate, 0).flora.Book, y = Roll(Fresh(), Archetype.Temperate, 0).flora.Book;
            var fx = x.Of(PlantFamily.Broadleaf).Select(p => p.Form).ToHashSet();
            var fy = y.Of(PlantFamily.Broadleaf).Select(p => p.Form).ToHashSet();
            if (!fx.SetEquals(fy) || x.Style.Leaves[0].Distance(y.Style.Leaves[0]) > 0.08f) differ++;
        }
        Check(differ >= tries - 1, $"pairs of temperate worlds differ in broadleaf forms or leaf colour: {differ} of {tries}");
    }

    void Stable()
    {
        ulong seed = Fresh();
        var (m, s, f) = Roll(seed, Archetype.Temperate, 0);
        var (_, _, again) = Roll(seed, Archetype.Temperate, 0);
        Check(f.Book.Fingerprint() == again.Book.Fingerprint(), "a world's species are stable for its seed, mesh for mesh");
        var (_, _, other) = Roll(Fresh(), Archetype.Temperate, 0);
        Check(f.Book.Fingerprint() != other.Book.Fingerprint(), "another seed rolls another book");
        double x = m.WidthMeters * 0.31, z = m.HeightMeters * 0.62;
        Check(f.RegionAt(x, z) == again.RegionAt(x, z) && f.RegionAt(x, z) == f.RegionAt(x + m.WidthMeters, z), "the region field is stable, and tiles the planet");
    }

    void Working()
    {
        int plants = 0, large = 0, solid = 0, noStem = 0, overNear = 0, overFar = 0, overCover = 0, noReach = 0, broken = 0, noWood = 0, overMeshes = 0, adrift = 0, petalled = 0, audited = 0, afloat = 0, unreached = 0, trunks = 0, stemGap = 0, covered = 0, uncovered = 0, solidPartsSeen = 0;
        var outsideBy = new Dictionary<PlantForm, int>();
        var forms = new HashSet<PlantForm>();
        foreach (var (a, v) in new[] { (Archetype.Temperate, 0), (Archetype.Temperate, 1), (Archetype.Arid, 0), (Archetype.Arid, 1), (Archetype.Ice, 0), (Archetype.Ice, 1), (Archetype.Alien, 0), (Archetype.Alien, 1), (Archetype.Lava, 0) })
        for (int i = 0; i < 2; i++)
        {
            Herbarium book = Roll(Fresh(), a, v).flora.Book;
            foreach (Plant p in book.Plants)
            {
                plants++;
                forms.Add(p.Form);
                if (p.Near.Length > Herbarium.NearMeshes) overMeshes++;
                bool small = p.Cover || p.Family == PlantFamily.Crop;
                int budget = p.Cover ? CoverBudget : small ? CropBudget : NearBudget;
                foreach (PlantMesh m in p.Near)
                {
                    if (m.Triangles > budget) { (small ? ref overCover : ref overNear)++; GD.PrintErr($"{p.Form} {m.Triangles} triangles"); }
                    if (m.Triangles == 0 || m.Positions.Any(q => !float.IsFinite(q.X + q.Y + q.Z))) broken++;
                }
                if (p.Large)
                {
                    large++;
                    if (p.Far.Triangles > FarBudget) { overFar++; GD.PrintErr($"{p.Form} far {p.Far.Triangles} triangles"); }
                }
                if (!small && p.Near.Any(m => m.Positions.Min(q => q.Y) > -0.2f)) { noStem++; GD.PrintErr($"{p.Form} stands on the ground instead of in it"); }
                // Every flower sits on the plant: the middle of each touches a leaf or a stem, and nothing bright floats.
                if (p.Flowers && p.Family == PlantFamily.Shrub)
                {
                    Tone heart = Tone.Lerp(p.Bloom, new Tone(1f, 0.9f, 0.5f), 0.7f);
                    foreach (PlantMesh m in p.Near)
                    {
                        var body = new List<int>();
                        var hearts = new List<System.Numerics.Vector3>();
                        for (int k = 0; k < m.Positions.Length; k += 3)
                        {
                            bool flower = false;
                            for (int corner = k; corner < k + 3; corner++)
                            {
                                System.Numerics.Vector4 col = m.Colors[corner];
                                if (col.W != 0f) continue;
                                if (Math.Abs(col.X - heart.R) < 0.002f && Math.Abs(col.Y - heart.G) < 0.002f) { hearts.Add(m.Positions[corner]); flower = true; }
                                else if (Math.Abs(col.X - p.Bloom.R) < 0.002f && Math.Abs(col.Y - p.Bloom.G) < 0.002f) flower = true;
                            }
                            if (!flower) body.Add(k);
                        }
                        foreach (System.Numerics.Vector3 h in hearts.Distinct())
                        {
                            petalled++;
                            if (body.Min(k => ToTriangle(h, m.Positions[k], m.Positions[k + 1], m.Positions[k + 2])) > 0.03f) adrift++;
                        }
                    }
                }
                // Nothing floats: every part of every mesh is held by a chain of touching parts down to the ground, every
                // lobe of leaves is reached by wood, and a trunk's top meets what grows from it (limbs, crown or fronds).
                if (PlantAudit.Joined(p.Form) && !p.Cover && p.Family != PlantFamily.Crop)
                {
                    var meshes = p.Near.ToList();
                    if (p.Far != p.Near[0]) meshes.Add(p.Far);
                    foreach (PlantMesh m in meshes)
                    {
                        audited++;
                        PlantAudit.Report report = PlantAudit.Analyse(m);
                        int away = report.Adrift().Count(), loose = report.Loose(m).Count();
                        if (away > 0 || loose > 0)
                        {
                            (away > 0 ? ref afloat : ref unreached)++;
                            GD.PrintErr($"{p.Form} ({(m == p.Far ? "far" : "near")}): {away} parts adrift, {loose} lobes no wood reaches");
                        }
                    }
                    if (PlantAudit.Crowned(p.Form))
                    {
                        trunks++;
                        if (!PlantAudit.TrunkMeets(p.Near[0], new System.Numerics.Vector3(p.StemTop.X, p.StemTop.Y, p.StemTop.Z)))
                        {
                            stemGap++;
                            GD.PrintErr($"{p.Form}: the trunk stops short of what should grow from it");
                        }
                    }
                }
                if (p.Reach == null || p.Reach.Length == 0 || p.Reach.Any(r => r.Radius <= 0f || !float.IsFinite(r.Radius))) noReach++;
                if (!p.Solid) continue;
                solid++;
                Flora kind = KindOf(p.Family);
                bool made = FarBuilder.Solids.TryMake(kind, p, 7, Vector3.Zero, 1f, 1f, out FloraSolid s);
                if (!made || s.Extents.X <= 0f || !s.IsTrunk) { broken++; GD.PrintErr($"{p.Form} has no trunk"); continue; }
                if (p.Tree && !s.Fellable) noWood++;
                // The collider stands where the stem does: its point just above the foot is inside it.
                Vector3 near = s.Center - s.Basis.Y * (s.Extents.Y - 0.05f);
                if (near.Length() > 1f) broken++;
                // What you see stops you: every solid part of every near mesh (each pipe of a cluster, a stalk,
                // a cap, a shard, a trunk and its thick limbs) lies inside the colliders that variant gets.
                for (int nv = 0; nv < p.Near.Length; nv++)
                {
                    var colliders = new List<FloraSolid>();
                    FarBuilder.Solids.MakeAll(kind, p, nv, 7, Vector3.Zero, 1f, 0f, colliders);
                    PlantMesh m = p.Near[nv];
                    var solidParts = p.Columns[nv].Select(c => c.Part).ToHashSet();
                    solidPartsSeen += solidParts.Count;
                    for (int t = 0; t < m.Triangles; t++)
                    {
                        if (!solidParts.Contains(m.PartOf[t])) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            System.Numerics.Vector3 q = m.Positions[t * 3 + k];
                            if (q.Y < 0.05f || q.Y > 2.2f) continue;
                            covered++;
                            var at = new Vector3(q.X, q.Y, q.Z);
                            if (colliders.Any(c => Within(c, at, 0.03f))) continue;
                            uncovered++;
                            outsideBy[p.Form] = outsideBy.GetValueOrDefault(p.Form) + 1;
                            if (uncovered <= 3) GD.PrintErr($"{p.Form} variant {nv}: {at} of part {m.PartOf[t]} lies outside its colliders ({string.Join("; ", colliders.Select(c => $"{c.Center} r {c.Extents.X:F2} h {c.Extents.Y:F2}"))}; columns {string.Join("; ", p.Columns[nv].Select(c => $"{c.A}-{c.B} r {c.Radius:F2} p {c.Part}"))})");
                        }
                    }
                }
            }
        }
        Check(forms.Count >= 28, $"the module's forms all come up: {forms.Count} of {Enum.GetValues<PlantForm>().Length} seen in {plants} species");
        Check(broken == 0, $"every species builds sound meshes and a working trunk ({solid} solid, {broken} broken)");
        foreach (var kv in outsideBy.OrderByDescending(k => k.Value)) GD.Print($"  outside its colliders: {kv.Key} {kv.Value}");
        Check(uncovered == 0 && covered > 0, $"every solid part of every solid species' meshes lies inside its colliders, whatever the form: {solidPartsSeen} parts, {covered} points, {uncovered} outside");
        Check(noWood == 0, $"every tree species gives wood when felled ({noWood} without)");
        Check(noStem == 0, $"every stem starts under its foot ({noStem} on top of the ground)");
        Check(adrift == 0 && petalled > 0, $"every flower sits on a leaf or stem, within 3 cm: {petalled} flowers, {adrift} adrift");
        Check(afloat == 0 && unreached == 0 && audited > 0, $"nothing floats: in {audited} meshes every part is joined to the ground through touching parts and every crown lobe is reached by wood ({afloat} meshes with parts adrift, {unreached} with lobes unreached)");
        Check(stemGap == 0 && trunks > 0, $"every trunk's top meets its limbs, crown or fronds, within {PlantAudit.Touch * 100f:F0} cm: {trunks} trunks, {stemGap} stopping short");
        Check(noReach == 0, $"every species knows the room it takes up, for ruin growth ({noReach} without)");
        Check(overNear == 0 && overCover == 0 && overFar == 0 && overMeshes == 0, $"mesh variety is bounded: at most {Herbarium.NearMeshes} near meshes a species, {NearBudget} triangles near, {FarBudget} far, {CoverBudget} cover ({overNear}, {overFar}, {overCover} over)");
    }

    /// <summary>Whether a point lies inside a collider grown by a margin (a trunk's radius and length, or a box).</summary>
    static bool Within(FloraSolid c, Vector3 p, float margin)
    {
        Vector3 l = c.Basis.Transposed() * (p - c.Center);
        if (Mathf.Abs(l.Y) > c.Extents.Y + margin) return false;
        return c.IsTrunk ? Mathf.Sqrt(l.X * l.X + l.Z * l.Z) < c.Extents.X + margin : Mathf.Abs(l.X) < c.Extents.X + margin && Mathf.Abs(l.Z) < c.Extents.Z + margin;
    }

    /// <summary>How far a point is from a triangle.</summary>
    static float ToTriangle(System.Numerics.Vector3 p, System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector3 c)
    {
        System.Numerics.Vector3 n = System.Numerics.Vector3.Cross(b - a, c - a);
        if (n.LengthSquared() < 1e-12f) return float.MaxValue;
        n = System.Numerics.Vector3.Normalize(n);
        float d = System.Numerics.Vector3.Dot(p - a, n);
        System.Numerics.Vector3 q = p - n * d;
        bool Inside(System.Numerics.Vector3 u, System.Numerics.Vector3 v) => System.Numerics.Vector3.Dot(System.Numerics.Vector3.Cross(v - u, q - u), n) >= 0f;
        if (Inside(a, b) && Inside(b, c) && Inside(c, a)) return Math.Abs(d);
        float Edge(System.Numerics.Vector3 u, System.Numerics.Vector3 v)
        {
            float t = Math.Clamp(System.Numerics.Vector3.Dot(p - u, v - u) / (v - u).LengthSquared(), 0f, 1f);
            return System.Numerics.Vector3.Distance(p, u + (v - u) * t);
        }
        return Math.Min(Edge(a, b), Math.Min(Edge(b, c), Edge(c, a)));
    }

    static float HueOf(Tone t)
    {
        float max = Math.Max(t.R, Math.Max(t.G, t.B)), min = Math.Min(t.R, Math.Min(t.G, t.B)), c = max - min;
        if (c < 1e-4f) return 0f;
        float h = max == t.R ? (t.G - t.B) / c : max == t.G ? 2f + (t.B - t.R) / c : 4f + (t.R - t.G) / c;
        return (h * 60f + 360f) % 360f;
    }

    /// <summary>What colour a world's leaves come out, over 20 fresh seeds of each world type.</summary>
    void Spread()
    {
        string Band(float h) => h < 45f || h >= 330f ? "russet" : h < 72f ? "olive" : h < 178f ? "green" : h < 255f ? "blue" : "violet";
        foreach (var (name, a, v) in new[] { ("temperate", Archetype.Temperate, 0), ("rainforest", Archetype.Temperate, 1), ("dunes", Archetype.Arid, 0), ("canyon", Archetype.Arid, 1), ("glacier", Archetype.Ice, 0), ("hotspring", Archetype.Ice, 1), ("volcanic", Archetype.Lava, 0), ("crystal", Archetype.Alien, 0), ("fungal", Archetype.Alien, 1) })
        {
            var bands = new Dictionary<string, int>();
            int species = 0, green = 0;
            for (int i = 0; i < 20; i++)
            {
                Herbarium book = Roll(Fresh(), a, v).flora.Book;
                string band = Band(HueOf(book.Style.Leaves[0]));
                bands[band] = bands.GetValueOrDefault(band) + 1;
                foreach (Plant p in book.Plants.Where(p => p.Family is PlantFamily.Conifer or PlantFamily.Broadleaf or PlantFamily.Jungle or PlantFamily.Grass))
                {
                    species++;
                    if (Band(HueOf(p.Leaf)) == "green") green++;
                }
            }
            GD.Print($"SPREAD {name}: {string.Join(", ", bands.OrderByDescending(b => b.Value).Select(b => $"{b.Key} {b.Value}"))} of 20 worlds; {(species == 0 ? 0 : green * 100 / species)}% of tree and grass species green");
            if (a == Archetype.Temperate) Check(bands.GetValueOrDefault("green") >= 14 && bands.GetValueOrDefault("russet") <= 4, $"{name} worlds are mostly green: {bands.GetValueOrDefault("green")} of 20 green, {bands.GetValueOrDefault("russet")} russet");
        }
    }

    static Flora KindOf(PlantFamily f) => f switch
    {
        PlantFamily.Conifer => Flora.Conifer, PlantFamily.Broadleaf => Flora.Broadleaf, PlantFamily.Jungle => Flora.JungleTree, PlantFamily.Palm => Flora.Palm,
        PlantFamily.Cactus => Flora.Cactus, PlantFamily.Crystal => Flora.Crystal, PlantFamily.GiantFungus => Flora.GiantMushroom, PlantFamily.Ice => Flora.IceShard, _ => Flora.Shrub,
    };

    void Regions()
    {
        var (m, _, f) = Roll(Fresh(), Archetype.Temperate, 0);
        Biome biome = Biome.Forest;
        float At(float region, Plant p)
        {
            int n = 0;
            for (int i = 0; i < 400; i++) if (f.Book.Pick((int)biome, PlantFamily.Broadleaf, region, (i + 0.5f) / 400f) == p) n++;
            return n / 400f;
        }
        Plant first = f.Book.Pick((int)biome, PlantFamily.Broadleaf, 0f, 0.5f);
        if (first == null)
        {
            biome = Biome.Grassland;
            first = f.Book.Pick((int)biome, PlantFamily.Broadleaf, 0f, 0.5f);
        }
        var dominant = new HashSet<int>();
        float worstStep = 0f;
        for (int r = 0; r < 100; r++)
        {
            dominant.Add(f.Book.Pick((int)biome, PlantFamily.Broadleaf, r / 100f, 0.5f).Index);
            worstStep = Math.Max(worstStep, Math.Abs(At(r / 100f, first) - At((r + 1) / 100f, first)));
        }
        Check(dominant.Count >= 2, $"one biome holds different trees in different regions: {dominant.Count} leading species along the region axis");
        Check(worstStep < 0.2f, $"regions blend, with no hard line: a species' share never jumps more than {worstStep:P0} in a hundredth of the axis");
        int across = 0, spots = 200;
        float low = 1f, high = 0f;
        var rng = new Rng(Fresh());
        for (int i = 0; i < spots; i++)
        {
            double x = rng.NextFloat() * m.WidthMeters, z = rng.NextFloat() * m.HeightMeters;
            float a = f.RegionAt(x, z), b = f.RegionAt(x + 40, z);
            low = Math.Min(low, a);
            high = Math.Max(high, a);
            if (Math.Min(Math.Abs(a - b), 1f - Math.Abs(a - b)) < 0.05f) across++;
        }
        Check(across > spots * 0.88f && high - low > 0.5f, $"the region field is slow on foot and spans the axis: {across} of {spots} spots barely change over 40 m, range {low:F2} to {high:F2}");
    }

    void Vacuum()
    {
        foreach (var (a, v) in new[] { (Archetype.Temperate, 0), (Archetype.Arid, 0), (Archetype.Alien, 1) })
        {
            ulong seed = Fresh();
            var (m, s, _) = Roll(seed, a, v);
            PlanetFlora none = PlanetFlora.For(m, s, vacuum: true);
            Check(none.Book.Barren && none.Book.Verdure == null, $"a vacuum {a} world rolls no species");
            var pal = new GroundPalette(m, none);
            var source = new GalHack.Terrain.PlanetTerrainSource(m);
            int plants = 0, cover = 0;
            var rng = new Rng(Fresh());
            for (int i = 0; i < 8; i++)
            {
                int fx = (int)(rng.NextFloat() * (m.WidthMeters / FarBuilder.Size)), fz = (int)(rng.NextFloat() * (m.HeightMeters / FarBuilder.Size));
                var job = new FarJob { Fx = fx, Fz = fz, Lod = 0 };
                FarBuilder.Build(job, source, pal, new HashSet<long>());
                if (job.Error != null) throw job.Error;
                plants += job.Flora.Count(b => b.Plant >= 0 || b.Kind is not (Flora.Rock or Flora.LavaRock));
                cover += CoverBuilder.Build(fx * 8, fz * 8, source, pal, new HashSet<long>()).Plants.Count;
            }
            Check(plants == 0 && cover == 0, $"no plant and no cover scattered on it ({plants}, {cover})");
        }
        // Lifeless by nature: a world with no air by the generator's rule grows nothing either.
        PlanetSpec cratered = PlanetSpec.Roll(Fresh(), Archetype.Arid, 0, 8) with { Modifiers = Modifier.Cratered };
        WorldMap map = PlanetGenerator.Generate(cratered);
        Check(PlanetFlora.For(map, SkySystem.For(cratered)).Book.Barren, "an airless cratered world is barren");
        var lava = Roll(Fresh(), Archetype.Lava, 0).flora.Book;
        var green = Roll(Fresh(), Archetype.Temperate, 0).flora.Book;
        Check(lava.Plants.Count < green.Plants.Count, $"a lava world holds little life against a temperate one ({lava.Plants.Count} species to {green.Plants.Count})");
    }

    /// <summary>
    /// Round places dug into the ground (a stairwell, rooms below the pad), on fresh worlds: no tuft of
    /// ground cover lies out over the opening (its leaves reach over ground dug from under them), and no
    /// river sheet runs over it. Nothing floats over a hole.
    /// </summary>
    void NothingOverOpenings()
    {
        int places = 0, tufts = 0, over = 0, sheets = 0, wet = 0;
        var e = new List<string>();
        foreach (var (a, v) in new[] { (Archetype.Temperate, 0), (Archetype.Temperate, 1), (Archetype.Alien, 0) })
        {
            PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll(Fresh(), a, v, 8), Main.Catalog);
            var dug = w.Source.Stamps.Where(st => st.Shape == GalHack.Terrain.StampShape.Pad && st.CarveBottom < st.PadY).Take(14).ToList();
            foreach (GalHack.Terrain.TerrainStamp st in dug)
            {
                places++;
                // An opening: the ground under the surface is dug out by a place (its rooms, its stairwell).
                bool Open(double x, double z)
                {
                    int gy = (int)Math.Floor(w.Source.SurfaceHeight(x, z) - 0.6f), gx = (int)Math.Floor(x), gz = (int)Math.Floor(z);
                    foreach (int i in w.Source.StampsAt(x, z)) if (w.Source.Stamps[i].IsCarved(gx, gy, gz)) return true;
                    return false;
                }
                // Ground cover over the footprint and a cell round it.
                int c0x = (int)Math.Floor((st.MinX - 4.0) / CoverBuilder.CellSize), c1x = (int)Math.Floor((st.MaxX + 4.0) / CoverBuilder.CellSize);
                int c0z = (int)Math.Floor((st.MinZ - 4.0) / CoverBuilder.CellSize), c1z = (int)Math.Floor((st.MaxZ + 4.0) / CoverBuilder.CellSize);
                for (int cz = c0z; cz <= c1z; cz++)
                for (int cx = c0x; cx <= c1x; cx++)
                {
                    CoverCell cell = CoverBuilder.Build(cx, cz, w.Source, w.Palette, new HashSet<long>());
                    foreach (var (plant, buffer, count) in cell.Plants)
                        for (int k = 0; k < count; k++)
                        {
                            int o = k * 16;
                            double x = cx * (double)CoverBuilder.CellSize + buffer[o + 3], z = cz * (double)CoverBuilder.CellSize + buffer[o + 11];
                            tufts++;
                            float r = CoverBuilder.Reach * 0.9f;
                            if (Open(x, z) || Open(x + r, z) || Open(x - r, z) || Open(x, z + r) || Open(x, z - r))
                            {
                                over++;
                                if (e.Count < 3) e.Add($"a tuft at ({x:F1}, {z:F1}) by {st.Name}, over its opening");
                            }
                        }
                }
                // The far chunk the place stands in, at full detail: its river sheet.
                int fx = (int)Math.Floor((st.MinX + st.MaxX) * 0.5 / FarBuilder.Size), fz = (int)Math.Floor((st.MinZ + st.MaxZ) * 0.5 / FarBuilder.Size);
                var job = new FarJob { Fx = fx, Fz = fz, Lod = 0, SpanX = FarBuilder.Span(fx, w.Terrain.Width), SpanZ = FarBuilder.Span(fz, w.Terrain.Height) };
                FarBuilder.Build(job, w.Source, w.Palette, new HashSet<long>());
                if (job.Error != null) throw job.Error;
                if (job.WaterIndices == null) continue;
                for (int t = 0; t + 2 < job.WaterIndices.Length; t += 3)
                {
                    Vector3 m = (job.WaterVertices[job.WaterIndices[t]] + job.WaterVertices[job.WaterIndices[t + 1]] + job.WaterVertices[job.WaterIndices[t + 2]]) / 3f;
                    double x = fx * (double)FarBuilder.Size + m.X, z = fz * (double)FarBuilder.Size + m.Z;
                    if (x < st.MinX - 2 || x > st.MaxX + 2 || z < st.MinZ - 2 || z > st.MaxZ + 2) continue;
                    wet++;
                    if (Open(x, z))
                    {
                        sheets++;
                        if (e.Count < 3) e.Add($"river water at ({x:F1}, {z:F1}) over {st.Name}'s opening");
                    }
                }
            }
        }
        foreach (string s in e) GD.Print("  e.g. " + s);
        Check(places >= 6 && over == 0 && sheets == 0,
            $"round {places} places dug into the ground, no ground cover lies out over an opening ({over} of {tufts} tufts) and no river sheet runs over one ({sheets} of {wet} bits of water by them)");
    }

    /// <summary>Real chunks and cells on a fresh built world (roads, ruins and all).</summary>
    void Scatter()
    {
        foreach (var (a, v) in new[] { (Archetype.Temperate, 0), (Archetype.Alien, 1) })
        {
            ulong seed = Fresh();
            PlanetSpec spec = PlanetSpec.Roll(seed, a, v, 8);
            PlanetWorld w = PlanetWorld.Build(spec, null);
            var rng = new Rng(Fresh());
            int stood = 0, proud = 0, onWork = 0, solids = 0, unset = 0, different = 0, felled = 0, unfelled = 0;
            var species = new HashSet<int>();
            int covered = 0, coverOnBuilt = 0, coverProud = 0;
            for (int i = 0; i < 40; i++)
            {
                int fx = (int)(rng.NextFloat() * FarBuilder.Count(w.Terrain.Width)), fz = (int)(rng.NextFloat() * FarBuilder.Count(w.Terrain.Height));
                var job = new FarJob { Fx = fx, Fz = fz, Lod = 0, SpanX = FarBuilder.Span(fx, w.Terrain.Width), SpanZ = FarBuilder.Span(fz, w.Terrain.Height) };
                FarBuilder.Build(job, w.Source, w.Palette, new HashSet<long>());
                if (job.Error != null) throw job.Error;
                var twin = new FarJob { Fx = fx, Fz = fz, Lod = 0, SpanX = job.SpanX, SpanZ = job.SpanZ };
                FarBuilder.Build(twin, w.Source, w.Palette, new HashSet<long>());
                if (job.Flora.Count != twin.Flora.Count || job.Flora.Zip(twin.Flora).Any(p => !p.First.Buffer.SequenceEqual(p.Second.Buffer))) different++;
                foreach (FloraBatch b in job.Flora)
                {
                    if (b.Plant < 0) continue;
                    species.Add(b.Plant);
                    Plant p = w.Flora.Book.Plants[b.Plant];
                    for (int k = 0; k < b.Count; k++)
                    {
                        int o = k * 16;
                        double x = fx * (double)FarBuilder.Size + b.Buffer[o + 3], z = fz * (double)FarBuilder.Size + b.Buffer[o + 11];
                        if (w.Palette.Planted != null && w.Palette.Planted.ContainsKey(FarBuilder.ScatterIdAt(x, z))) continue;
                        stood++;
                        if (b.Buffer[o + 7] > w.Source.SurfaceHeight(x, z) - FloraSolidRules.Sink + 0.01f) proud++;
                        // Trees come in only over the outer blend round a place: none on its apron or footprint, nor on a road.
                        if (p.Large && (w.Source.Built(x, z, out bool road) >= 0.55f || road) && !w.Source.OnWild(x, z, out _)) onWork++;
                    }
                }
                solids += job.Solids.Count;
                // Felling a tree takes its trunk and its instance out of the chunk, for whatever species it is.
                if (job.Solids.FirstOrDefault(s => s.Fellable) is { Wood: > 0 } tree)
                {
                    var cut = new FarJob { Fx = fx, Fz = fz, Lod = 0, SpanX = job.SpanX, SpanZ = job.SpanZ };
                    FarBuilder.Build(cut, w.Source, w.Palette, new HashSet<long>(), new HashSet<long> { tree.Id });
                    // Level 0 carries a near and a far drawing of each large plant (they dissolve into each other): one of each goes.
                    int Drawn(FarJob j, bool far) => j.Flora.Where(b => b.Plant == tree.Plant && b.Far == far).Sum(b => b.Count);
                    felled++;
                    if (Drawn(cut, false) != Drawn(job, false) - 1 || Drawn(cut, true) != Drawn(job, true) - 1 || cut.Solids.Any(s => s.Id == tree.Id)) unfelled++;
                }
                unset += job.Solids.Count(s => s.Fellable && s.Plant < 0);
                int cx = fx * 8 + (int)(rng.NextFloat() * 8), cz = fz * 8 + (int)(rng.NextFloat() * 8);
                CoverCell cell = CoverBuilder.Build(cx, cz, w.Source, w.Palette, new HashSet<long>());
                foreach (var (plant, buffer, count) in cell.Plants)
                {
                    species.Add(plant);
                    for (int k = 0; k < count; k++)
                    {
                        int o = k * 16;
                        double x = cx * (double)CoverBuilder.CellSize + buffer[o + 3], z = cz * (double)CoverBuilder.CellSize + buffer[o + 11];
                        covered++;
                        // Ground cover thins out over a place's blend, but never stands on its apron or footprint.
                        if (w.Source.Built(x, z, out _) >= 1f && !w.Source.OnWild(x, z, out _)) coverOnBuilt++;
                        if (buffer[o + 7] > w.Source.SurfaceHeight(x, z) + 0.02f) coverProud++;
                    }
                }
            }
            Check(stood > 40 && proud == 0, $"{a}: every stem meets the ground: {stood} plants, {proud} above it");
            Check(onWork == 0, $"{a}: no tree stands on anything built ({onWork})");
            Check(unset == 0 && solids > 0, $"{a}: every fellable trunk is a species ({solids} solids, {unset} not)");
            Check(felled > 0 && unfelled == 0, $"{a}: felled trees are gone, trunk and all: {felled} felled, {unfelled} still standing");
            Check(different == 0, $"{a}: chunks scatter the same plants each time ({different} differ)");
            Check(covered > 200 && coverOnBuilt == 0 && coverProud == 0, $"{a}: ground cover is rooted and off aprons and footprints: {covered} tufts, {coverOnBuilt} on them, {coverProud} proud of the ground; {species.Count} species seen");
        }
    }
}
