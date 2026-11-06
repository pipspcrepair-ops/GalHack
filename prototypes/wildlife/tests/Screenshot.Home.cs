using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Archetype = Planets.Gen.Archetype;
using Landform = Planets.Gen.Landform;
using LandformInfo = Planets.Gen.LandformInfo;
using PlanetGenerator = Planets.Gen.PlanetGenerator;
using PlanetSpec = Planets.Gen.PlanetSpec;
using SurfaceSampler = Planets.Gen.SurfaceSampler;
using WorldMap = Planets.Gen.WorldMap;

namespace Wildlife;

/// <summary>
/// The home view: creatures in their home landform on a real generated planet, built for its
/// ground (design doc rev 423, end of "Landforms"):
///   -- --view=home --landform=Plains [--archetype=0 --variant=0 --seed=22]
/// The patch of planet under the camera becomes the prototype's ground (in the planet's own
/// biome colours), the fauna is rolled for the planet's landform mix and spawned by home ground,
/// and the landform's signature creature stands in front of the camera: a plains herd, a mountain
/// climber, a dune burrower, a delta wader.
/// </summary>
public partial class Screenshot
{
    Landform _homeLandform = Landform.Plains;
    WorldMap _homeMap;
    double _homeX, _homeZ;
    float _homeHeading;

    /// <summary>Picks the planet and the spot, and bakes the patch the prototype will stand on (before Main is built).</summary>
    Terrain HomeTerrain()
    {
        int archetype = 0, variant = 0;
        ulong seed = 22;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            if (kv.Length != 2) continue;
            switch (kv[0])
            {
                case "landform": _homeLandform = Enum.Parse<Landform>(kv[1]); break;
                case "archetype": archetype = int.Parse(kv[1]); break;
                case "variant": variant = int.Parse(kv[1]); break;
                case "seed": seed = ulong.Parse(kv[1]); break;
            }
        }
        _homeMap = PlanetGenerator.Generate(PlanetSpec.Roll(seed, (Archetype)archetype, variant, 16));
        var sampler = new SurfaceSampler(_homeMap);
        (_homeX, _homeZ, _homeHeading) = HomeSpot(_homeMap, sampler, _homeLandform);
        GD.Print($"home: {LandformInfo.Name(_homeLandform)} at {_homeX:F0}, {_homeZ:F0} on {_homeMap.Spec} ({_homeMap.LandformsAt(_homeX, _homeZ)})");
        double cx = _homeX, cz = _homeZ;
        return new Terrain((x, z) => sampler.HeightAt(cx + x, cz + z, 1), 200f);
    }

    /// <summary>Somewhere deep in the landform, on dry ground gentle enough to stand a group on; deltas want water close by.</summary>
    static (double x, double z, float heading) HomeSpot(WorldMap map, SurfaceSampler sampler, Landform l)
    {
        double best = double.MinValue, bx = 0, bz = 0;
        float heading = 0f;
        var rng = new Rng(Rng.Hash(LandformInfo.Name(l)));
        for (int t = 0; t < 6000; t++)
        {
            double x = rng.Float() * map.WidthMeters, z = rng.Float() * map.HeightMeters;
            bool range = l == Landform.Mountains;
            if (map.LandformsAt(x, z).Weight(l) < (range ? 0.85f : 0.95f)) continue;
            float h = sampler.HeightAt(x, z, 1);
            if (h < 1.5f) continue;
            bool deep = true;
            for (int d = 0; d < 8 && deep; d++)
                deep = map.LandformsAt(x + Math.Cos(d * 0.785) * (range ? 100 : 160), z + Math.Sin(d * 0.785) * (range ? 100 : 160)).Weight(l) >= (range ? 0.55f : 0.85f);
            if (!deep) continue;
            // Gentle underfoot: the group must be able to stand there.
            float rough = 0f, wet = 0f;
            for (int d = 0; d < 8; d++)
            {
                double a = d * 0.785;
                rough = Math.Max(rough, Math.Abs(sampler.HeightAt(x + Math.Cos(a) * 12, z + Math.Sin(a) * 12, 1) - h));
                if (sampler.HeightAt(x + Math.Cos(a) * 45, z + Math.Sin(a) * 45, 2) < 0f) wet++;
            }
            if (rough > (l == Landform.Mountains ? 9f : 2.5f)) continue;
            double score = -rough + (l == Landform.Delta ? Math.Min(wet, 3) * 2 : 0);
            if (l == Landform.DuneSea)
            {
                // A flat between the dunes, with crests close round it.
                float lo = h, hi = h;
                for (int d = 0; d < 16; d++)
                for (double r = 30; r <= 90; r += 30)
                {
                    float q = sampler.HeightAt(x + Math.Cos(d * 0.39) * r, z + Math.Sin(d * 0.39) * r, 2);
                    lo = Math.Min(lo, q);
                    hi = Math.Max(hi, q);
                }
                score += Math.Min(hi - lo, 25f) * 0.3;
            }
            if (l == Landform.Mountains)
            {
                // Climbers on a mountain shoulder with the range rising behind them.
                float up = 0f;
                for (int d = 0; d < 8; d++) up = Math.Max(up, sampler.HeightAt(x + Math.Cos(d * 0.785) * 220, z + Math.Sin(d * 0.785) * 220, 4) - h);
                score += Math.Min(up, 150f) * 0.05;
            }
            if (score <= best) continue;
            best = score;
            bx = x;
            bz = z;
        }
        // Look the way the ground rises (mountains), at the water (deltas), across the dunes, or anywhere open.
        float bestRise = float.MinValue;
        for (int d = 0; d < 16; d++)
        {
            double a = d * Math.Tau / 16;
            float rise = sampler.HeightAt(bx + Math.Sin(a) * 150, bz - Math.Cos(a) * 150, 4);
            float wetAhead = 0f;
            for (double r = 20; r <= 120; r += 10) if (sampler.HeightAt(bx + Math.Sin(a) * r, bz - Math.Cos(a) * r, 2) < 0f) wetAhead += 1f;
            float score = l switch
            {
                Landform.Mountains => rise,
                Landform.Delta => wetAhead,
                _ => -Math.Abs(rise - sampler.HeightAt(bx, bz, 4)),
            };
            if (score > bestRise) { bestRise = score; heading = (float)a; }
        }
        return (bx, bz, heading);
    }

    void HomeSetup()
    {
        Main m = _main;
        World(WorldKind.Temperate, 0.38f);
        // The ground in the planet's own colours round the spot.
        Color sum = new(0, 0, 0);
        int n = 0;
        for (int dz = -4; dz <= 4; dz++)
        for (int dx = -4; dx <= 4; dx++)
        {
            int cell = _homeMap.CellAt(Planets.Gen.Mathx.WrapX(_homeX + dx * 40, _homeMap.WidthMeters), Planets.Gen.Mathx.Wrap(_homeZ + dz * 40, _homeMap.HeightMeters));
            if (_homeMap.Has(cell, Planets.Gen.CellFlags.Water)) continue;
            Planets.Look.Rgb c = Planets.Look.BiomeLook.Color((Planets.Gen.Biome)_homeMap.Biome[cell]);
            sum += new Color(c.R, c.G, c.B);
            n++;
        }
        Color ground = n > 0 ? sum / n : new Color("808060");
        ground.A = 1f;
        WorldStyle style = m.Planet.Style;
        style.GroundA = ground.Darkened(0.12f);
        style.GroundB = ground.Lightened(0.08f);
        m.Planet.ApplyStyle(style);
        m.Forest.Visible = false;
        m.Grass.Visible = _homeLandform is Landform.Plains or Landform.Steppe or Landform.Delta;
        m.Outpost.Visible = false;
        m.Vessel.Visible = false;
        m.Planet.ShowRocks(false);
        // Water, where the patch dips under sea level.
        var water = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(500, 500) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.16f, 0.4f, 0.5f, 0.85f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.15f,
            },
            Position = new Vector3(0f, 0.05f, 0f),
        };
        m.AddChild(water);

        // The planet's fauna, built for its landforms, spawned by home ground.
        var landforms = _homeMap.Model.Mix.Landforms;
        PlanetFauna fauna = m.Generator.Generate(_homeMap.Spec.Seed, 0.3f, false, landforms);
        double cx = _homeX, cz = _homeZ;
        m.UseFauna(fauna, p => _homeMap.LandformsAt(cx + p.X, cz + p.Z));
        foreach (Species s in fauna.Species) GD.Print($"  {s.Name,-12} {s.CommonName,-36} {s.Behaviour,-16} home {string.Join(", ", s.Homes.Select(LandformInfo.Name))}");

        // The landform's signature creature, in front of the camera.
        Adaptation want = _homeLandform switch
        {
            Landform.Mountains => Adaptation.Climber,
            Landform.DuneSea => Adaptation.SandSwimmer,
            Landform.Delta => Adaptation.Wader,
            _ => Adaptation.Runner,
        };
        Species star = fauna.Species.Where(s => s.Homes.Contains(_homeLandform))
            .OrderByDescending(s => s.Adaptation == want ? 2 : 0)
            .ThenByDescending(s => s.Behaviour == Behaviour.Herd ? 1 : 0).First();
        Vector3 toward = new(MathF.Sin(_homeHeading), 0f, -MathF.Cos(_homeHeading));
        float ahead = 6f + star.Body.Size * 3f;
        if (_homeLandform == Landform.Delta)
        {
            // Waders at the water's edge.
            for (float r = 4f; r < 120f; r += 2f)
                if (m.Planet.Terrain.Height(toward.X * r, toward.Z * r) < 0.3f) { ahead = Math.Max(4f, r - 3f); break; }
        }
        Vector3 centre = Ground(toward * ahead);
        foreach (Creature other in m.Sim.Creatures.ToList()) if (WildSim.Flat(other.Position - centre).Length() < 30f) m.Sim.Remove(other);
        int count = star.Behaviour == Behaviour.Herd ? 6 : star.Plan == BodyPlan.Giant ? 1 : 3;
        List<Creature> group = m.Sim.SpawnGroup(star, centre, count);
        m.Player.Teleport(Ground(-toward * 30f));
        // Keep them out and up (an ambusher would otherwise dig in for the day).
        _each = () => { foreach (Creature c in group) if (c.Alive && c.State is CState.Hidden or CState.Lurk or CState.Rest) c.SetState(CState.Graze); };
        Vector3 side = Turn(toward, 0.5f);
        float back = 7f + star.Body.Size * 3.2f;
        _aim = () =>
        {
            Vector3 c = Centroid(group, centre);
            return (Ground(c - side * back) + Vector3.Up * (1.6f + star.Body.Size * 0.6f), c + Vector3.Up * star.Body.Size * 0.5f);
        };
        _captureTick = 200;
        GD.Print($"home: {star.Name} ({star.CommonName}), {star.Behaviour}, {group.Count} of them");
    }

    static Vector3 Centroid(List<Creature> group, Vector3 fallback)
    {
        var alive = group.Where(c => c.Alive).ToList();
        if (alive.Count == 0) return fallback;
        Vector3 sum = Vector3.Zero;
        foreach (Creature c in alive) sum += c.Position;
        return sum / alive.Count;
    }
}
