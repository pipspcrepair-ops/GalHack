using System;
using System.Collections.Generic;
using Godot;
using Planets.Gen;
using PlanetSurface.World;

namespace AssetViewer.Providers.Nature;

/// <summary>
/// A stretch of a world's ground, streamed as the game streams it (voxel terrain near, far rings, the horizon,
/// plants, the sea), round a spot where the thing looked for lies. Seen from over it, or stood on.
/// </summary>
public partial class TerrainView : SurfaceView
{
    readonly string _what;
    readonly bool _infra;
    PlanetSurface.Infra.InfraLayer _infraLayer;
    float _low, _high;
    readonly string _found;
    readonly bool _ground;

    public TerrainView(PlanetWorld world, double x, double z, string what, bool ground, bool infra = false, string found = "", double time = 0.42)
        : base(world, new Vector3((float)x, world.GroundHeight(x, z), (float)z))
    {
        _what = what;
        _infra = infra;
        _found = found;
        OpenTime = time;
        Stage.StartInside = ground;
        _ground = ground;
        Stage.MinDistanceShare = 0.005f;
        // The relief round the spot: the frame holds all of it, so the camera starts clear of the hills.
        _low = float.MaxValue;
        _high = float.MinValue;
        for (float dz = -350f; dz <= 350f; dz += 50f)
        for (float dx = -350f; dx <= 350f; dx += 50f)
        {
            float h = Mathf.Max(0f, world.GroundHeight(Spot.X + dx, Spot.Z + dz));
            _low = Mathf.Min(_low, h);
            _high = Mathf.Max(_high, h);
        }
        HalfSize = 350f;
        SettleSeconds = 6.0;
    }

    protected override void AfterWorld()
    {
        if (!_infra) return;
        _infraLayer = new PlanetSurface.Infra.InfraLayer { Name = "Infra" };
        AddChild(_infraLayer);
        _infraLayer.Build(World);
    }

    protected override void SettleStep() => _infraLayer?._Process(0.016);

    public override Aabb? FrameBounds
    {
        get
        {
            if (_ground)
            {
                float camZ = Spot.Z + new Vector3(48f, 20f, 48f).Length() * 0.5f * 0.25f;
                return new Aabb(new Vector3(Spot.X - 24f, World.GroundHeight(Spot.X, camZ), Spot.Z - 24f), new Vector3(48f, 20f, 48f));
            }
            float relief = _high - _low;
            float height = Mathf.Clamp(relief * 1.4f, 90f, 700f);
            return new Aabb(new Vector3(Spot.X - HalfSize, _low - 5f, Spot.Z - HalfSize), new Vector3(HalfSize * 2f, height, HalfSize * 2f));
        }
    }

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        yield return ("World", World.Spec.Archetype + " " + Archetypes.Get(World.Spec.Archetype, World.Spec.Variant).VariantName);
        yield return ("Size", $"{World.Spec.WidthKm} x {World.Spec.HeightKm} km world, ground streamed round a 700 m patch");
        yield return ("Landforms here", World.Map.LandformsAt(Spot.X, Spot.Z).ToString());
        yield return ("Biome", World.Palette.BiomeAt(Spot.X, Spot.Z).ToString());
        yield return ("Ground", $"{Spot.Y:F0} m above the sea");
        yield return ("Relief in the patch", $"{_high - _low:F0} m");
        yield return ("Terrain ceiling", "900 m at most");
        if (!string.IsNullOrEmpty(_found)) yield return ("Found", _found);
    }
}
