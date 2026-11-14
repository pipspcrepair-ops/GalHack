using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Plants;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using PoiGenerator.Gen;
using Archetype = Planets.Gen.Archetype;
using Flora = Planets.Look.Flora;
using Biome = Planets.Gen.Biome;
using Rgb = Planets.Look.Rgb;

namespace AssetViewer.Providers.Nature;

/// <summary>A world's plants as the game rolls them: its map and sky, and the flora those and its history make.</summary>
public sealed class FloraWorld
{
    public PlanetSpec Spec;
    public WorldMap Map;
    public PlanetFlora Flora;
    public string Label = "";
    public string History = "None";
    public Herbarium Book => Flora.Book;

    static readonly List<(string Key, FloraWorld World)> Cache = new();

    public static FloraMood MoodOf(WorldLayer layer, out float lean)
    {
        lean = 0f;
        if (layer == null) return FloraMood.None;
        float L(string k) => Math.Clamp(layer.Bias.GetValueOrDefault(k), 0f, 1f);
        lean = Math.Clamp(layer.Bias.GetValueOrDefault("alien"), -0.2f, 0.45f);
        return new FloraMood(L("overgrown"), L("dead"), L("swollen"), L("fungal"), L("burnt"), L("sparse"));
    }

    public static FloraWorld Get(int typeIndex, ulong seed, string history)
    {
        var (archetype, variant, label) = NatureWorlds.Types().ElementAt(typeIndex);
        PlanetSpec spec = NatureWorlds.SpecOf(archetype, variant, seed);
        string key = $"{spec}|{history}";
        for (int i = 0; i < Cache.Count; i++)
            if (Cache[i].Key == key) return Cache[i].World;
        NatureWorlds.Prepare();
        WorldLayer layer = history is "None" or "" ? null : FloraProvider.Histories.FirstOrDefault(l => l.Id == history);
        FloraMood mood = MoodOf(layer, out float lean);
        WorldMap map = PlanetGenerator.Generate(spec);
        PlanetFlora flora = PlanetFlora.For(map, SkySystem.For(spec), mood: mood, lean: lean);
        var world = new FloraWorld { Spec = spec, Map = map, Flora = flora, Label = label, History = history };
        Cache.Insert(0, (key, world));
        while (Cache.Count > 6) Cache.RemoveAt(Cache.Count - 1);
        return world;
    }
}

/// <summary>One plant, a stand of them or the scatter of a biome, drawn with the game's flora shader.</summary>
public sealed partial class FloraView : AssetInstance
{
    readonly Dictionary<Mesh, List<(Transform3D Place, Color Custom)>> _groups = new();
    static ShaderMaterial _material;
    readonly List<string> _facts = new();
    Aabb _bounds;
    readonly FloraWorld _world;
    readonly Dictionary<(int, int), ArrayMesh> _meshes = new();

    public override IEnumerable<(string Label, string Value)> Facts() => _facts.Select(f => { int i = f.IndexOf('|'); return (f[..i], f[(i + 1)..]); });
    void Fact(string label, string value) => _facts.Add(label + "|" + value);

    public FloraView(BuildRequest r, PlantFamily? family, Biome? biome)
    {
        ulong seed = r.Seed == 0 ? 1UL : r.Seed;
        string history = r.Get("History", "None");
        var rng = new RandomNumberGenerator { Seed = seed * 31 + 7 };
        Stage.Ground = true;
        Stage.Background = "sky";
        _material ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/flora.gdshader") };
        if (family is PlantFamily fam)
        {
            string[] labels = NatureWorlds.Types().Select(t => t.Label).ToArray();
            int start = Math.Max(0, Array.IndexOf(labels, r.Get("World")));
            FloraWorld world = null;
            // A world that grows none of this family hands over to the next that does.
            for (int k = 0; k < labels.Length && world == null; k++)
            {
                FloraWorld w = FloraWorld.Get((start + k) % labels.Length, seed, history);
                if (w.Book.Of(fam).Any()) world = w;
            }
            _world = world ?? FloraWorld.Get(start, seed, history);
            BuildFamily(_world, fam, r, rng);
        }
        else
        {
            _world = FindWorld(biome.Value, r.Get("World", "Any"), seed, history);
            BuildBiome(_world, biome.Value, rng);
        }
        Commit();
    }

    // ---- Worlds ----------------------------------------------------------------------------------

    static FloraWorld FindWorld(Biome biome, string label, ulong seed, string history)
    {
        string[] labels = NatureWorlds.Types().Select(t => t.Label).ToArray();
        if (label != "Any" && Array.IndexOf(labels, label) is int pick and >= 0) return FloraWorld.Get(pick, seed, history);
        // The kind of world the enum lists the biome under first, then the rest.
        int hint = biome switch { <= Biome.CloudForest => 0, <= Biome.Oasis => 2, <= Biome.Sinter => 4, <= Biome.Sulfur => 6, _ => 8 };
        for (int k = 0; k < labels.Length; k++)
        {
            FloraWorld w = FloraWorld.Get((hint + k) % labels.Length, seed, history);
            if (w.Map.Biome.Contains((byte)biome)) return w;
        }
        return FloraWorld.Get(hint, seed, history);
    }

    // ---- Families --------------------------------------------------------------------------------

    ArrayMesh MeshOf(Plant p, int variant)
    {
        if (_meshes.TryGetValue((p.Index, variant), out ArrayMesh m)) return m;
        return _meshes[(p.Index, variant)] = PlantMeshes.ToGodot(p.Near[variant % p.Near.Length]);
    }

    static Color Paint(Plant p, FloraWorld w, float shade, double x, double z)
    {
        Rgb c = p.Mineral ? PlanetFlora.ToRgb(p.Leaf) : w.Flora.Tint(p, shade, x, z);
        return new Color(c.R, c.G, c.B, 0f);
    }

    void Place(Plant p, int variant, Vector3 at, float scale, float turn, FloraWorld w, float shade)
    {
        Mesh mesh = MeshOf(p, variant);
        var basis = new Basis(Vector3.Up, turn).Scaled(Vector3.One * scale);
        if (!_groups.TryGetValue(mesh, out var list)) _groups[mesh] = list = new();
        list.Add((new Transform3D(basis, at), Paint(p, w, shade, at.X + 4000, at.Z + 2000)));
    }

    void BuildFamily(FloraWorld w, PlantFamily family, BuildRequest r, RandomNumberGenerator rng)
    {
        List<Plant> pool = w.Book.Of(family).ToList();
        string layout = r.Get("Layout", "Single");
        float size = r.Get("Size", "Typical") switch { "Smallest" => 0f, "Largest" => 1f, _ => 0.5f };
        float Scale(bool vary) => Mathf.Lerp(0.78f, 1.24f, vary ? Mathf.Clamp(size + rng.RandfRange(-0.35f, 0.35f), 0f, 1f) : size);
        Fact("World", w.Label);
        if (w.History != "None") Fact("History", w.History);
        if (pool.Count == 0)
        {
            Fact("Plants", "none grow on this world");
            return;
        }
        int pick = Math.Clamp(int.Parse(r.Get("Species", "1")) - 1, 0, pool.Count - 1);
        Plant head = pool[pick];
        Fact("Species", $"{pick + 1} of {pool.Count}");
        Fact("Form", head.Form.ToString());
        float s0 = Scale(false);
        Fact("Height", $"{head.Height * s0:F1} m");
        Fact("Crown radius", $"{head.CrownRadius * s0:F1} m");
        if (head.Solid) Fact("Trunk", $"{head.TrunkRadius * s0 * 2f:F2} m across, {head.TrunkHeight * s0:F1} m tall");
        PlantMesh mesh0 = head.Near[0];
        Fact("Triangles", mesh0.Triangles.ToString());
        Fact("Parts", mesh0.PartKind != null ? (mesh0.PartKind.Length - 1).ToString() : "1");
        Fact("Variants", head.Near.Length.ToString());
        Fact("Flowers", head.Flowers ? "yes" : "no");
        Fact("Kind", head.Cover ? "ground cover" : head.Tree ? "tree" : head.Mineral ? "mineral growth" : "growth");

        if (layout == "Single")
        {
            Place(head, 0, Vector3.Zero, s0, rng.RandfRange(0f, Mathf.Tau), w, 0.6f);
            return;
        }
        bool small = head.Cover || head.Height < 1.2f;
        int count = layout == "Stand" ? (small ? 36 : head.Large ? 9 : 16) : (small ? 220 : 24);
        float spread = layout == "Stand" ? (small ? 3f : Mathf.Max(6f, head.CrownRadius * 3.2f)) : (small ? 6f : Mathf.Max(9f, head.CrownRadius * 4.5f));
        var placed = new List<(Vector3 At, float R)>();
        int guard = 0;
        while (placed.Count < count && guard++ < count * 40)
        {
            float a = rng.RandfRange(0f, Mathf.Tau), d = Mathf.Sqrt(rng.Randf()) * spread;
            var at = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
            // Several species of the family stand together, the chosen one most often.
            Plant p = rng.Randf() < 0.55f ? head : pool[rng.RandiRange(0, pool.Count - 1)];
            float sc = Scale(true);
            float room = small ? p.CrownRadius * sc * 0.5f : p.CrownRadius * sc * 0.7f;
            bool clear = true;
            foreach (var (o, or) in placed)
                if ((o - at).LengthSquared() < (room + or) * (room + or)) { clear = false; break; }
            if (!clear) continue;
            placed.Add((at, room));
            Place(p, rng.RandiRange(0, p.Near.Length - 1), at, sc, rng.RandfRange(0f, Mathf.Tau), w, rng.Randf());
        }
        Fact("In the picture", placed.Count.ToString());
    }

    // ---- A biome's scatter -----------------------------------------------------------------------

    static readonly Dictionary<Flora, Mesh> KindMeshes = new();

    static Mesh KindMesh(Flora kind)
    {
        if (KindMeshes.Count == 0)
            foreach (var kv in PlanetGenerator_Meshes()) KindMeshes[kv.Key] = kv.Value;
        return KindMeshes[kind];
    }

    static Dictionary<Flora, Mesh> PlanetGenerator_Meshes() => Planets.Surface.FloraMeshes.BuildAll();

    void BuildBiome(FloraWorld w, Biome biome, RandomNumberGenerator rng)
    {
        Fact("World", w.Label);
        if (w.History != "None") Fact("History", w.History);
        const float radius = 22f, area = Mathf.Pi * radius * radius;
        FloraRule[] rules = w.Flora.RulesOf(biome);
        var placed = new List<(Vector3 At, float R)>();
        int trees = 0, low = 0, rocks = 0;
        Vector3 Random(float reach)
        {
            float a = rng.RandfRange(0f, Mathf.Tau), d = Mathf.Sqrt(rng.Randf()) * reach;
            return new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
        }
        foreach (FloraRule rule in rules)
        {
            int n = Math.Clamp((int)MathF.Round(rule.Density * area / 1000f), 0, 90);
            for (int i = 0; i < n; i++)
            {
                Vector3 at = Random(radius);
                float shade = rng.Randf(), size = rng.Randf();
                Plant p = w.Flora.Pick(rule.Kind, biome, at.X + 4000, at.Z + 2000, rng.Randf());
                if (p != null)
                {
                    float sc = Mathf.Lerp(0.78f, 1.24f, size), room = p.CrownRadius * sc * (p.Large ? 0.55f : 0.4f);
                    if (placed.Any(o => (o.At - at).LengthSquared() < (room + o.R) * (room + o.R))) continue;
                    placed.Add((at, room));
                    Place(p, rng.RandiRange(0, p.Near.Length - 1), at, sc, rng.RandfRange(0f, Mathf.Tau), w, shade);
                    if (p.Large) trees++; else low++;
                }
                else if (!w.Flora.Rolled(rule.Kind))
                {
                    Mesh mesh = KindMesh(rule.Kind);
                    float sc = Mathf.Lerp(0.7f, 1.35f, size) * Planets.Surface.FloraMeshes.BaseScale(rule.Kind);
                    Rgb tint = FarBuilder.Tint(rule, shade);
                    if (!_groups.TryGetValue(mesh, out var list)) _groups[mesh] = list = new();
                    list.Add((new Transform3D(new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)).Scaled(Vector3.One * sc), at), new Color(tint.R, tint.G, tint.B, 0f)));
                    rocks++;
                }
            }
        }
        // Ground cover, a share of the biome's own mix, in the middle of the stand.
        CoverMix mix = PlanetFlora.CoverOf(biome);
        foreach ((PlantFamily fam, float chance) in new[] { (PlantFamily.Grass, mix.Grass), (PlantFamily.Forb, mix.Forb), (PlantFamily.Twig, mix.Twig), (PlantFamily.Reed, mix.Reed), (PlantFamily.Moss, mix.Moss), (PlantFamily.Pins, mix.Pins) })
        {
            if (chance <= 0f || w.Book.Barren) continue;
            int n = (int)(chance * 140f * w.Flora.CoverDensity);
            for (int i = 0; i < n; i++)
            {
                Vector3 at = Random(9f);
                Plant p = w.Book.Pick((int)biome, fam, w.Flora.RegionAt(at.X + 4000, at.Z + 2000), rng.Randf());
                if (p == null) break;
                Place(p, rng.RandiRange(0, p.Near.Length - 1), at, Mathf.Lerp(0.8f, 1.2f, rng.Randf()), rng.RandfRange(0f, Mathf.Tau), w, rng.Randf());
                low++;
            }
        }
        Fact("Trees and large growth", trees.ToString());
        Fact("Low growth", low.ToString());
        Fact("Rocks", rocks.ToString());
        Fact("Scatter", $"{radius:F0} m round");
        if (trees + low + rocks == 0) Fact("Plants", "none grow here");
    }

    // ---- Drawing ---------------------------------------------------------------------------------

    void Commit()
    {
        Aabb? total = null;
        foreach (var (mesh, list) in _groups)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = list.Count };
            Aabb box = mesh.GetAabb();
            for (int i = 0; i < list.Count; i++)
            {
                mm.SetInstanceTransform(i, list[i].Place);
                mm.SetInstanceCustomData(i, list[i].Custom);
                Aabb world = list[i].Place * box;
                total = total.HasValue ? total.Value.Merge(world) : world;
            }
            AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.On });
        }
        _bounds = total ?? new Aabb(new Vector3(-1, 0, -1), new Vector3(2, 2, 2));
        if (_bounds.Size.Y < 0.5f) _bounds = _bounds.Grow(0.3f);
    }

    public override Aabb? FrameBounds => _bounds;
}
