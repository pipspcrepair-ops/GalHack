using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Planets.Gen;
using Biome = Planets.Gen.Biome;
using Archetype = Planets.Gen.Archetype;

namespace AssetViewer.Providers.Nature;

/// <summary>
/// Ground as the game generates and streams it: every landform and every biome of the generator, and the
/// features that stand out of them (a coast, a river, a cliff, a crater, an ore outcrop). The spot where each
/// lies is found on a generated world, the world is streamed round it, and nothing is forced onto it.
/// </summary>
public sealed partial class TerrainProvider : IAssetProvider
{
    public string Category => "Terrain";
    public IEnumerable<(string Prototype, string Folder)> Mounts => new[] { ("planet-surface", "shaders") };

    static AssetOption[] Options(string home = "Any", bool ground = false) => new[]
    {
        new AssetOption("World", new[] { "Any" }.Concat(NatureWorlds.Types().Select(t => t.Label)).ToArray()),
        new AssetOption("View", "Over", "Ground") { Default = ground ? 1 : 0 },
        new AssetOption("Time", SurfaceView.Times) { Default = 1 },
    };

    // What a search found, kept so that changing the view or the time does not search again.
    static readonly Dictionary<string, Spots.Found?> Found = new();

    static Spots.Found? Search(string key, Func<Spots.Found?> find)
    {
        if (Found.TryGetValue(key, out var hit)) return hit;
        var result = find();
        if (Found.Count > 64) Found.Clear();
        return Found[key] = result;
    }

    static AssetInstance Show(BuildRequest r, Spots.Found? found, string what, string note, bool infra = false)
    {
        found ??= new Spots.Found(NatureWorlds.SpecOf(Archetype.Temperate, 0, r.Seed), 4000, 2000, 0f);
        var world = NatureWorlds.Get(found.Value.Spec);
        return new TerrainView(world, found.Value.X, found.Value.Z, what, r.Get("View", "Over") == "Ground", infra, note, SurfaceView.TimeValue(r.Get("Time", "Morning")));
    }

    static ulong SeedOf(BuildRequest r) => r.Seed == 0 ? 1UL : r.Seed;

    public IEnumerable<AssetEntry> Entries()
    {
        foreach (Landform landform in LandformInfo.All)
        {
            Landform l = landform;
            yield return new AssetEntry
            {
                Id = "landform-" + l, Group = "Landforms/" + LandformInfo.Openness(l), Name = char.ToUpperInvariant(LandformInfo.Name(l)[0]) + LandformInfo.Name(l)[1..],
                Randomisable = true, Options = Options(), Build = r => Landform(r, l),
            };
        }
        foreach (Biome biome in Enum.GetValues<Biome>())
        {
            Biome b = biome;
            yield return new AssetEntry
            {
                Id = "biome-" + b, Group = "Biomes/" + BiomeGroup(b), Name = b.ToString(), Randomisable = true, Options = Options(), Build = r => BiomeSpot(r, b),
            };
        }
        yield return new AssetEntry { Id = "feature-coast", Group = "Features/Water", Name = "Sea coast", Randomisable = true, Options = Options(), Build = Coast };
        yield return new AssetEntry { Id = "feature-river", Group = "Features/Water", Name = "River", Randomisable = true, Options = Options(), Build = River };
        yield return new AssetEntry { Id = "feature-cliff", Group = "Features/Rock", Name = "Cliff face", Randomisable = true, Options = Options(), Build = Cliff };
        yield return new AssetEntry { Id = "feature-crater", Group = "Features/Craters", Name = "Impact crater", Randomisable = true, Options = Options(), Build = Crater };
        yield return new AssetEntry { Id = "feature-volcano", Group = "Features/Craters", Name = "Volcano crater", Randomisable = true, Options = Options(), Build = Volcano };
        foreach (DepositKind kind in Enum.GetValues<DepositKind>())
        {
            DepositKind k = kind;
            yield return new AssetEntry
            {
                Id = "ore-" + k, Group = "Features/Ore outcrops", Name = k + " outcrop", Randomisable = true, Options = Options(ground: true), Build = r => Ore(r, k),
            };
        }
    }

    static string BiomeGroup(Biome b) => b switch
    {
        <= Biome.FrozenSea => "Seas",
        <= Biome.CloudForest => "Temperate",
        <= Biome.Oasis => "Arid",
        <= Biome.Sinter => "Ice",
        <= Biome.Sulfur => "Lava",
        _ => "Alien",
    };

    static (Archetype, int)[] WorldsOf(Biome b) => b switch
    {
        <= Biome.CloudForest => new[] { (Archetype.Temperate, 0), (Archetype.Temperate, 1) },
        <= Biome.Oasis => new[] { (Archetype.Arid, 0), (Archetype.Arid, 1) },
        <= Biome.Sinter => new[] { (Archetype.Ice, 0), (Archetype.Ice, 1) },
        <= Biome.Sulfur => new[] { (Archetype.Lava, 0), (Archetype.Lava, 1) },
        _ => new[] { (Archetype.Alien, 0), (Archetype.Alien, 1) },
    };

    // ---- Landforms and biomes ---------------------------------------------------------------------

    static AssetInstance Landform(BuildRequest r, Landform l)
    {
        string world = r.Get("World", "Any");
        bool coastal = LandformInfo.IsCoastal(l);
        var found = Search($"landform|{l}|{r.Seed}|{world}", () => Spots.Find(
            Spots.Candidates(SeedOf(r), Spots.WorldsWith(l), world),
            (m, i, j) =>
            {
                if (m.Has(m.Index(i, j), CellFlags.Water) && !(coastal && l != Planets.Gen.Landform.Archipelago)) return -1f;
                return Spots.Share(m, i, j, (mm, k) => mm.Landform[k] == (byte)l && (coastal || !mm.Has(k, CellFlags.Water)));
            }, 0.85f));
        return Show(r, found, LandformInfo.Name(l), $"{LandformInfo.Name(l)}, {found?.Score * 100f:F0}% of the window");
    }

    static AssetInstance BiomeSpot(BuildRequest r, Biome b)
    {
        string world = r.Get("World", "Any");
        bool sea = b <= Biome.FrozenSea;
        var found = Search($"biome|{b}|{r.Seed}|{world}", () => Spots.Find(
            Spots.Candidates(SeedOf(r), WorldsOf(b), world),
            (m, i, j) => (sea || !m.Has(m.Index(i, j), CellFlags.Water)) ? Spots.Share(m, i, j, (mm, k) => mm.Biome[k] == (byte)b) : -1f, 0.85f));
        return Show(r, found, b.ToString(), $"{b}, {found?.Score * 100f:F0}% of the window");
    }

    // ---- Features ----------------------------------------------------------------------------------

    static IEnumerable<PlanetSpec> Any(BuildRequest r, params (Archetype, int)[] preferred) => Spots.Candidates(SeedOf(r), preferred, r.Get("World", "Any"));

    static AssetInstance Coast(BuildRequest r)
    {
        var found = Search($"coast|{r.Seed}|{r.Get("World")}", () => Spots.Find(Any(r, (Archetype.Temperate, 0)),
            (m, i, j) =>
            {
                int c = m.Index(i, j);
                if (m.Has(c, CellFlags.Water) || !m.Has(c, CellFlags.Coast) || m.Elevation[c] < 3f) return -1f;
                float water = Spots.Share(m, i, j, (mm, k) => mm.Has(k, CellFlags.Water));
                return 1f - Mathf.Abs(water - 0.45f) * 2f;
            }, 0.9f));
        return Show(r, found, "coast", "sea coast");
    }

    static AssetInstance River(BuildRequest r)
    {
        var found = Search($"river|{r.Seed}|{r.Get("World")}", () => Spots.Find(Any(r, (Archetype.Temperate, 0), (Archetype.Temperate, 1)),
            (m, i, j) =>
            {
                int c = m.Index(i, j);
                if (m.Has(c, CellFlags.Water) || !m.Has(c, CellFlags.River) || m.Elevation[c] < 4f) return -1f;
                return Mathf.Min(1f, m.RiverFlow[c] / 60f) + 0.4f * Spots.Share(m, i, j, (mm, k) => mm.Has(k, CellFlags.River));
            }, 1.2f));
        return Show(r, found, "river", "a river");
    }

    static AssetInstance Cliff(BuildRequest r)
    {
        var found = Search($"cliff|{r.Seed}|{r.Get("World")}", () => Spots.Find(Any(r, (Archetype.Arid, 1), (Archetype.Temperate, 0)),
            (m, i, j) =>
            {
                int c = m.Index(i, j);
                if (m.Has(c, CellFlags.Water) || m.Elevation[c] < 20f) return -1f;
                return Spots.Share(m, i, j, (mm, k) => mm.Slope[k] > 0.9f && !mm.Has(k, CellFlags.Water));
            }, 0.4f));
        return Show(r, found, "cliff", "steep ground");
    }

    static float Bowl(WorldMap m, int i, int j, int radius)
    {
        int c = m.Index(i, j);
        if (m.Has(c, CellFlags.Water) || m.Elevation[c] < 6f) return -1f;
        float ring = 0f;
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.Tau / 8f;
            ring += m.Elevation[m.Index(i + (int)MathF.Round(MathF.Cos(a) * radius), j + (int)MathF.Round(MathF.Sin(a) * radius))];
        }
        return Mathf.Clamp((ring / 8f - m.Elevation[c]) / 30f, 0f, 2f);
    }

    static AssetInstance Crater(BuildRequest r)
    {
        // Natural craters stay only on barren worlds (the generator's rule): the cratered ones.
        IEnumerable<PlanetSpec> Candidates()
        {
            string only = r.Get("World", "Any");
            foreach (PlanetSpec s in Spots.Candidates(SeedOf(r), new[] { (Archetype.Arid, 0), (Archetype.Ice, 0), (Archetype.Lava, 0) }, only))
                if (PlanetSpec.Craters(s.Archetype)) yield return s with { Modifiers = s.Modifiers | Modifier.Cratered };
        }
        var found = Search($"crater|{r.Seed}|{r.Get("World")}", () => Spots.Find(Candidates(), (m, i, j) => MathF.Max(Bowl(m, i, j, 7), Bowl(m, i, j, 10)), 1f));
        return Show(r, found, "crater", "an impact crater");
    }

    static AssetInstance Volcano(BuildRequest r)
    {
        var found = Search($"volcano|{r.Seed}|{r.Get("World")}", () => Spots.Find(Any(r, (Archetype.Lava, 0), (Archetype.Lava, 1), (Archetype.Ice, 1)),
            (m, i, j) => m.Landform[m.Index(i, j)] == (byte)Planets.Gen.Landform.VolcanicField ? MathF.Max(Bowl(m, i, j, 3), Bowl(m, i, j, 5)) : -1f, 1f));
        return Show(r, found, "volcano", "a volcanic cone and its crater");
    }

    static AssetInstance Ore(BuildRequest r, DepositKind kind)
    {
        string key = $"ore|{kind}|{r.Seed}|{r.Get("World")}";
        string note = "";
        Spots.Found? found;
        if (Found.TryGetValue(key, out var hit)) found = hit;
        else
        {
            found = null;
            int tried = 0;
            foreach (PlanetSpec spec in Spots.Candidates(SeedOf(r), Array.Empty<(Archetype, int)>(), r.Get("World", "Any")))
            {
                if (tried++ >= 8) break;
                var world = NatureWorlds.Get(spec);
                // Rock that shows: a deposit at the surface that the game draws a tell for.
                Deposit best = default;
                bool any = false;
                foreach (Deposit d in world.Map.Deposits.All())
                    if (d.Kind == kind && PlanetSurface.Infra.InfraLayer.HasTell(world, d) && (!any || d.Depth < best.Depth || d.Depth == best.Depth && d.Amount > best.Amount)) { best = d; any = true; }
                if (!any) continue;
                found = new Spots.Found(spec, Planets.Gen.Mathx.WrapX(best.X, world.Width), Planets.Gen.Mathx.Wrap(best.Y, world.Height), best.Amount);
                Found[key] = found;
                break;
            }
            Found[key] = found;
        }
        if (found is { } f)
        {
            var world = NatureWorlds.Get(f.Spec);
            foreach (Deposit d in world.Map.Deposits.All())
                if (d.Kind == kind && Math.Abs(Planets.Gen.Mathx.WrapX(d.X, world.Width) - f.X) < 1 && Math.Abs(Planets.Gen.Mathx.Wrap(d.Y, world.Height) - f.Z) < 1)
                    note = $"{kind}, {d.Amount:F0} units, {(d.Depth <= 0.01f ? "at the surface" : d.Depth < 3f ? $"{d.Depth:F1} m down" : $"{d.Depth:F0} m down")}";
        }
        return Show(r, found, "ore", note, infra: true);
    }
}
