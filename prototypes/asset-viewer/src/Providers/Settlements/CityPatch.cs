using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator;
using PoiGenerator.Gen;

namespace AssetViewer.Providers;

/// <summary>
/// A bounded patch of the game's city on flat ground: blocks of 84 m between streets, lots set along each block's two rows by the
/// city planner's own rules (CityPlan.Plan in planet-surface: tower, mid-rise, works, terrace, house widths and storeys by
/// district, a lean or a toppled top as the fall rises), each lot a place of its own built from its lot hints (CityBlocks), the
/// streets drawn from their centre lines with kerbs and footpaths out to the lot lines. What the real planner reads off a
/// world's terrain, rivers and layers is replaced by the options: the district, the size of the patch, how far it fell.
/// </summary>
public static class CityPatch
{
    public const double Block = 84, Inset = 8;
    public const float Avenue = 5.5f, Side = 4f;

    public static readonly string[] Districts = { "Mixed transect", "Downtown", "Mid-rise", "Works", "Houses and terraces", "Civic hall", "Station", "School", "Campus" };
    public static readonly string[] Sizes = { "1 block", "2 blocks", "2 x 2 blocks", "3 x 3 blocks" };
    public static readonly (string Name, float Fall)[] Falls = { ("Standing", 0f), ("Worn", 0.35f), ("Torn tops", 0.6f), ("Fallen, towers leaning", 0.8f), ("Stumps", 1f) };

    public static AssetOption[] Options(string district) => new[]
    {
        new AssetOption("District", Districts) { Default = Array.IndexOf(Districts, district) },
        new AssetOption("Size", Sizes) { Default = 2 },
        new AssetOption("Fall", Falls.Select(f => f.Name).ToArray()) { Default = 1, Rollable = true },
        new AssetOption("Lights", "Dark", "A lit district") { Rollable = true },
        new AssetOption("Tech level", "Low", "Mid", "High") { Default = 1, Rollable = true },
        new AssetOption("Biome", Enum.GetNames<Biome>()) { Default = (int)Biome.Steppe, Rollable = true },
    };

    public static PlaceGroup Build(BuildRequest r)
    {
        PoiLibrary lib = PlaceData.Library;
        string district = r.Get("District", "Downtown");
        (int cols, int rows) = r.Get("Size") switch { "1 block" => (1, 1), "2 blocks" => (2, 1), "3 x 3 blocks" => (3, 3), _ => (2, 2) };
        float fall = Falls.FirstOrDefault(f => f.Name == r.Get("Fall")).Fall;
        bool lit = r.Get("Lights") == "A lit district";
        TechLevel tech = r.Get("Tech level") switch { "Low" => TechLevel.Low, "High" => TechLevel.High, _ => TechLevel.Mid };
        Biome biome = Enum.TryParse(r.Get("Biome"), out Biome b) ? b : Biome.Steppe;
        var rng = new Rng(Hash.Combine(r.Seed, 0xC171UL));
        var families = lib.Families.Where(f => f.Tags.Contains("city") && PlaceData.SourceOf(f.Id) == "city-families").ToDictionary(f => f.Id);
        int culture = (int)(r.Seed % 100000);

        var group = new PlaceGroup(new Aabb(new Vector3(-30, 0, -30), new Vector3((float)(cols * Block) + 60, 40, (float)(rows * Block) + 60)));
        var flat = new Flat();
        double cityW = cols * Block, cityD = rows * Block;
        Color lawn = new Color("5c6b3f").Lerp(PoiGenerator.Look.ToColor(PoiGenerator.Look.Ground(biome)), 0.35f), concrete = new Color("6f706c"), asphalt = new Color("2f3033"), path = new Color("8c8a82"), line = new Color("d6d2c0");

        // Streets: an avenue along every row line, a side street down every column line, running on past the patch.
        const float Run = 60f;
        for (int row = 0; row <= rows; row++) Street(flat, -Run, (float)(row * Block), (float)cityW + Run, (float)(row * Block), true, Avenue, asphalt, path, line);
        for (int col = 0; col <= cols; col++) Street(flat, (float)(col * Block), -Run, (float)(col * Block), (float)cityD + Run, false, Side, asphalt, path, line);

        var tally = new Tally();
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                string d = DistrictOf(district, col, row, cols);
                double x0 = col * Block, z0 = row * Block;
                double rr = RadiusOf(d, col);
                bool green = d is "Civic hall" or "School" or "Campus" or "Houses and terraces";
                flat.Quad((float)(x0 + Inset), (float)(z0 + Inset), (float)(x0 + Block - Inset), (float)(z0 + Block - Inset), -0.03f, green ? lawn : concrete);

                if (d is "Civic hall" or "Station" or "School" or "Campus")
                {
                    string kind = d switch { "Civic hall" => "civic", "Station" => "station", "School" => "school", _ => "university" };
                    bool north = (row + col) % 2 == 0;
                    Lot(group, lib, families, kind, x0 + Inset, z0 + Inset, x0 + Block - Inset, z0 + Block - Inset, north ? Dir.North : Dir.South, rr, fall, lit, tech, culture, biome, rng, tally);
                    continue;
                }
                double depth = (Block - 2 * Inset) / 2;
                foreach (bool northRow in new[] { true, false })
                {
                    double lz0 = northRow ? z0 + Inset : z0 + Block - Inset - depth, lz1 = lz0 + depth;
                    double along = x0 + Inset, end = x0 + Block - Inset;
                    while (true)
                    {
                        string kind = d switch
                        {
                            "Downtown" => rng.Chance(0.22f) ? "midrise" : "tower",
                            "Mid-rise" => "midrise",
                            "Works" => "works",
                            _ => northRow ? "terrace" : "house",
                        };
                        double[] widths = kind switch
                        {
                            "tower" => new[] { 34.0, 36.0 }, "midrise" => new[] { 24.0, 26.0, 30.0 }, "works" => new[] { 34.0, 36.0, 40.0 },
                            "terrace" => new[] { 18.0, 26.0, 34.0 }, _ => new[] { 20.0, 22.0, 24.0 },
                        };
                        double lw = widths[rng.Range(0, widths.Length)];
                        if (along + lw > end + 0.01) lw = widths.Where(x => along + x <= end + 0.01).DefaultIfEmpty(0).Min();
                        if (lw <= 0) break;
                        double lx0 = along, lx1 = along + lw;
                        along += lw + (d == "Houses and terraces" ? 0 : 2);
                        Lot(group, lib, families, kind, lx0, lz0, lx1, lz1, northRow ? Dir.North : Dir.South, rr, fall, lit, tech, culture, biome, rng, tally);
                    }
                }
            }

        group.AddChild(flat.ToMesh("Streets"));
        // The ground under and round the whole patch, out to the horizon.
        var ground = new Flat();
        ground.Quad(-30000, -30000, 30000, 30000, -0.06f, PoiGenerator.Look.ToColor(PoiGenerator.Look.Ground(biome)).Lerp(new Color("5a5c4c"), 0.6f));
        group.AddChild(ground.ToMesh("Land"));
        group.Fact("Patch", $"{cols * Block:0} x {rows * Block:0} m, {cols * rows} block{(cols * rows > 1 ? "s" : "")}");
        group.Fact("District", district);
        group.Fact("Lots", $"{tally.Lots}: " + string.Join(", ", tally.Kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}")));
        group.Fact("Tallest", tally.Tallest + " storeys");
        group.Fact("Fall", r.Get("Fall") + (tally.Leaning + tally.Toppled > 0 ? $" ({tally.Leaning} leaning, {tally.Toppled} toppled)" : ""));
        group.Fact("Built at tech", tech.ToString());
        return group;
    }

    sealed class Tally
    {
        public int Lots, Tallest, Toppled, Leaning;
        public readonly Dictionary<string, int> Kinds = new();
    }

    static string DistrictOf(string pick, int col, int row, int cols)
    {
        if (pick != "Mixed transect") return pick;
        string[] order = { "Downtown", "Mid-rise", "Houses and terraces", "Works" };
        return order[(col + row * cols) % order.Length];
    }

    /// <summary>The planner's distance in from the middle (0 the core, 1 the edge): downtown is the core, houses the edge.</summary>
    static double RadiusOf(string district, int col) => district switch
    {
        "Downtown" => 0.12 + 0.1 * (col % 3), "Mid-rise" => 0.45, "Works" => 0.7, "Houses and terraces" => 0.6 + 0.15 * col, _ => 0.5,
    };

    static void Lot(PlaceGroup group, PoiLibrary lib, Dictionary<string, FamilyDef> families, string kind, double x0, double z0, double x1, double z1, Dir front, double r, float fall, bool lit,
        TechLevel tech, int culture, Biome biome, Rng rng, Tally tally)
    {
        if (!families.TryGetValue("city_" + kind, out FamilyDef f)) return;
        int wBlocks = (int)Math.Round((x1 - x0) / 2), dBlocks = (int)Math.Round((z1 - z0) / 2);
        int storeys = kind switch
        {
            "tower" => (int)Math.Round(8 + 22 * Math.Pow(Math.Clamp(1 - r / 0.38, 0, 1), 0.8) * (0.4 + 0.6 * rng.Float())),
            "midrise" => rng.Range(4, 9), "terrace" => rng.Range(2, 4), "house" => rng.Range(1, 3), "police" or "fire" or "school" or "university" => 2, _ => 1,
        };
        if (kind == "tower" && r < 0.22 && rng.Chance(0.25f)) storeys = Math.Min(30, storeys + rng.Range(6, 11));
        float lotFall = Math.Clamp(fall * (0.5f + rng.Float()), 0f, 1f);
        bool toppled = storeys > 6 && lotFall > 0.75f && rng.Chance(0.35f), lean = !toppled && storeys > 6 && lotFall > 0.5f && rng.Chance(0.3f);
        if (toppled) storeys = Math.Max(2, storeys / 4);
        else if (storeys > 3 && lotFall > 0.6f && !lean) storeys = Math.Max(2, (int)(storeys * (1f - (lotFall - 0.6f))));
        HomeHints home = kind is "house" or "terrace" ? new HomeHints(culture, Climate.Temperate, (float)Math.Clamp(0.2 + 0.7 * r, 0, 1)) : null;
        var lot = new CityLot(kind, wBlocks, dBlocks, Math.Max(1, storeys), rng.Range(0, 1 << 12), lotFall, lean, toppled, 0, 0, 0, false, home);
        ulong s = Hash.Combine(rng.NextU64(), 0xC17UL);
        Layout layout = PoiFactory.TryLayout(lib, f, (int)(s % (ulong)f.Variants.Count), f.Rarity, s, new LayoutHints(front, tech, lot), out string why);
        if (layout == null) { GD.PushWarning($"city lot left empty: {why}"); return; }
        PoiState state = lit && kind is "tower" or "midrise" or "police" or "fire" ? PoiState.Active : lotFall > 0.55f ? PoiState.Ruined : PoiState.Abandoned;
        PoiInstance inst = PoiFactory.Spawn(lib, layout, state, s + 1);
        bool green = kind is "house" or "terrace" or "civic" or "school" or "university";
        Color ground = green ? new Color("4f5f38") : new Color("6a6b67");
        var view = PoiView.Build(inst, biome, new PoiView.Options(Collision: false, Detail: true, Lights: lit, Figures: false), (_, _) => ground);
        var (ox, oz) = front switch { Dir.North => (x0, z0), Dir.South => (x1, z1), Dir.East => (x1, z0), _ => (x0, z1) };
        group.Add(view, new Vector3((float)Math.Round(ox / 2) * 2, 0, (float)Math.Round(oz / 2) * 2));
        tally.Lots++;
        tally.Kinds[kind] = tally.Kinds.GetValueOrDefault(kind) + 1;
        tally.Tallest = Math.Max(tally.Tallest, lot.Storeys);
        if (toppled) tally.Toppled++;
        if (lean) tally.Leaning++;
    }

    /// <summary>One street along a centre line: carriageway, a painted centre line, kerbs and the footpath out to the lot line.</summary>
    static void Street(Flat flat, float ax, float az, float bx, float bz, bool alongX, float half, Color asphalt, Color path, Color line)
    {
        float len = alongX ? bx - ax : bz - az;
        float dx = alongX ? 1 : 0, dz = alongX ? 0 : 1;
        const float kerb = 0.14f;
        float edge = (float)Inset;
        if (alongX)
        {
            flat.Box(ax, 0f, az - edge, bx, kerb, az - half, path);
            flat.Box(ax, 0f, az + half, bx, kerb, az + edge, path);
        }
        else
        {
            flat.Box(ax - edge, 0f, az, ax - half, kerb, bz, path);
            flat.Box(ax + half, 0f, az, ax + edge, kerb, bz, path);
        }
        flat.Strip(ax, az, dx, dz, len, half * 2, 0.02f, asphalt);
        for (float t = 4f; t < len - 4f; t += 8f) flat.Strip(ax + dx * t, az + dz * t, dx, dz, 3.5f, 0.25f, 0.04f, line);
    }
}
