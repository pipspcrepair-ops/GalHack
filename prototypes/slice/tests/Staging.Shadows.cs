using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Plants;
using Godot;
using Planets.Look;
using PlanetSurface.Terrain;
using Planets.Gen;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Shadows at a low sun: from the highest open ground near the start, across the land at right angles
/// to the sun (so ridges' shadows lie across valleys), with the GPU's and CPU's frame time.
/// --elev=DEGREES the sun's height (8), --shadow=METRES the shadow distance. Frames go beside --out.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> ShadowView()
    {
        var planet = _game.Zone.Planet;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        _game.Zone.Wildlife.Frozen = true;
        if (_args.TryGetValue("shadow", out string reach))
        {
            _game.Settings.ShadowDistance = float.Parse(reach, System.Globalization.CultureInfo.InvariantCulture);
            _game.Settings.Changed();
        }
        Vector3 start = _game.Survivor.WorldFeet;
        // Open high ground within 5 km over the deepest land round it: a ridge over valleys.
        Vector3 hill = start;
        float top = float.MinValue, score = float.MinValue;
        float H(double x, double z) => planet.GroundHeight(Mathx.WrapX(x, planet.Width), Mathx.Wrap(z, planet.Height));
        for (int a = 0; a < 36; a++)
        for (float d = 0f; d <= 5000f; d += 250f)
        {
            double x = Mathx.WrapX(start.X + MathF.Cos(a * Mathf.Tau / 36f) * d, planet.Width), z = Mathx.Wrap(start.Z + MathF.Sin(a * Mathf.Tau / 36f) * d, planet.Height);
            float h = H(x, z);
            if (h < 5f || Woodedness(x, z) >= 1f || !Standable(x, z)) continue;
            // (The land round it, not the sea: a valley, not a coast.)
            float low = float.MaxValue;
            int land = 0;
            for (int k = 0; k < 12; k++)
            {
                float g = H(x + MathF.Cos(k * Mathf.Tau / 12f) * 1500f, z + MathF.Sin(k * Mathf.Tau / 12f) * 1500f);
                if (g < 3f) continue;
                land++;
                low = MathF.Min(low, g);
            }
            if (land < 10) continue;
            if (h - low > score)
            {
                score = h - low;
                top = h;
                hill = new Vector3((float)x, 0f, (float)z);
            }
        }
        foreach (int x in Arrive(hill)) yield return x;
        foreach (int x in SunAt((float)Num("elev", 8))) yield return x;
        double best = _sunTime;
        var sky = _game.Zone.Sky;
        var s = _game.Zone.Streamer;
        for (int i = 0; i < 60 * 40 && !(s.FarComplete && s.FarBehind == 0); i++) yield return 0;
        sky.TimeOfDay = best;
        Vector3 sun = sky.Sun.GlobalBasis.Z.Normalized();
        Vector3 across = new Vector3(-sun.Z, 0f, sun.X).Normalized();
        Vector3 feet = _game.Survivor.WorldFeet;
        // The side with the lower land: the valleys.
        float Low(Vector3 way) => Enumerable.Range(1, 6).Sum(k => { float g = planet.GroundHeight(Mathx.WrapX(feet.X + way.X * k * 400f, planet.Width), Mathx.Wrap(feet.Z + way.Z * k * 400f, planet.Height)); return g < 3f ? 1000f : g; });
        if (Low(-across) < Low(across)) across = -across;
        Vector3 eye = Feet(feet) + Vector3.Up * (float)Num("up", 40); // over the hilltop's own crest
        GD.Print($"SHADOW world {_game.World}; hill {top:F0} m ({score:F0} m over the land round it) at {hill.X:F0}, {hill.Z:F0}; sun {Num("elev", 8):F0} degrees up at time {best:F3}; looking across it");
        for (int i = 0; i < 180; i++)
        {
            sky.TimeOfDay = best;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(across + Vector3.Down * 0.12f, Vector3.Up), eye);
            yield return 0;
        }
        GD.Print($"SHADOW the sun stands {MathF.Asin(Math.Clamp(sky.Sun.GlobalBasis.Z.Normalized().Y, -1f, 1f)) * 180f / MathF.PI:F1} degrees up, its light {sky.Sun.LightEnergy:F2}");
        foreach (int x in Measure("valley")) yield return x;
        _game.FixedCamera = null;
    }

    double _sunTime;

    /// <summary>Sets the morning hour the sun stands at the height asked (degrees); the hour is left in _sunTime.</summary>
    IEnumerable<int> SunAt(float degrees)
    {
        float want = degrees * Mathf.Pi / 180f;
        var sky = _game.Zone.Sky;
        double err = double.MaxValue;
        _sunTime = 0.3;
        for (double t = 0.0; t < 1.0; t += 0.005)
        {
            sky.TimeOfDay = t;
            yield return 0;
            yield return 0;
            float e = MathF.Asin(Math.Clamp(sky.Sun.GlobalBasis.Z.Normalized().Y, -1f, 1f));
            if (MathF.Abs(e - want) < err) { err = MathF.Abs(e - want); _sunTime = t; }
        }
        sky.TimeOfDay = _sunTime;
    }

    /// <summary>
    /// Each plant's and rock's shadow at a low sun, by distance: on open ground among large growth, the sun
    /// low and the camera zoomed on a plant at each of a few distances, looking across the sun, so a shadow
    /// lies sideways to the eye and can be compared with its plant. Frames are --stem=PATH-NAME.png.
    /// --elev=DEGREES (6), --dist=A,B,C the distances, m. --rock looks at a rock.
    /// </summary>
    IEnumerable<int> PlantShadows()
    {
        var planet = _game.Zone.Planet;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        _game.Zone.Wildlife.Frozen = true;
        Vector3? spot = Best(_game.Survivor.WorldFeet, (x, z) => Woodedness(x, z) is float w && w > 0f ? 1f / (0.1f + w) : 0f); // sparse growth: plants stand apart
        if (spot == null) { GD.Print("PLANTSHADOW no large growth on this world"); yield break; }
        foreach (int x in Arrive(spot.Value)) yield return x;
        foreach (int x in SunAt((float)Num("elev", 6))) yield return x;
        var s = _game.Zone.Streamer;
        for (int i = 0; i < 60 * 40 && !(s.FarComplete && s.FarBehind == 0); i++) yield return 0;
        var sky = _game.Zone.Sky;
        sky.TimeOfDay = _sunTime;
        yield return 0;
        yield return 0;
        Vector3 sun = sky.Sun.GlobalBasis.Z.Normalized();
        Vector3 across = new Vector3(-sun.Z, 0f, sun.X).Normalized();
        Vector3 feet = _game.Survivor.WorldFeet;
        _game.Settings.Fov = 30f;
        _game.Settings.Changed();
        // One plant standing apart (nothing within 12 m of it) on the fine ground near the survivor, seen from each distance along the way across the sun.
        var near = s.FloraNear(feet, 120f).ToList();
        float Ground(Vector3 p, float dx, float dz) => planet.GroundHeight(Mathx.WrapX(p.X + dx, planet.Width), Mathx.Wrap(p.Z + dz, planet.Height));
        // (Level ground all round it, out to 25 m on the sun's side and the far side, so no dune's own shade falls on it.)
        bool Level(Vector3 p) => new[] { -25f, -12f, 12f, 25f }.All(k => MathF.Abs(Ground(p, sun.X * k, sun.Z * k) - p.Y) < 1.2f);
        var pick = near.Where(p => p.Root.DistanceTo(feet) > 8f && Level(p.Root))
            .OrderByDescending(p => Math.Min(30f, near.Where(q => q.Id != p.Id).Select(q => q.Root.DistanceTo(p.Root)).DefaultIfEmpty(30f).Min()) - (_args.ContainsKey("rock") != (p.Kind is Flora.Rock) ? 100f : 0f))
            .Cast<TerrainStreamer.PlantSighting?>().FirstOrDefault();
        if (pick is not TerrainStreamer.PlantSighting plant) { GD.Print($"PLANTSHADOW no level plant among {near.Count} near, {near.Count(p => Level(p.Root))} level"); yield break; }
        GD.Print($"PLANTSHADOW target {plant.Kind} plant {plant.Plant} scale {plant.Scale:F2}, sun {MathF.Asin(sun.Y) * 180f / MathF.PI:F1} degrees up");
        foreach (string d in _args.GetValueOrDefault("dist", "15,40,100,200,300").Split(','))
        {
            float want = float.Parse(d, System.Globalization.CultureInfo.InvariantCulture);
            Vector3 from = plant.Root + across * want;
            Vector3 eye = Feet(from) + Vector3.Up * 2.5f, look = plant.Root + Vector3.Up * (plant.Scale + 0.5f);
            for (int i = 0; i < 90; i++)
            {
                sky.TimeOfDay = _sunTime;
                _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
                yield return 0;
            }
            FloraShot($"shadow-{want:F0}");
            if (_args.ContainsKey("atlas"))
            {
                _game.GetViewport().DebugDraw = Viewport.DebugDrawEnum.DirectionalShadowAtlas;
                for (int i = 0; i < 4; i++) yield return 0;
                FloraShot($"atlas-{want:F0}");
                _game.GetViewport().DebugDraw = Viewport.DebugDrawEnum.Disabled;
            }
        }
        _game.FixedCamera = null;
    }

    /// <summary>
    /// A plant's drawing by distance casts the shadow of its whole shape: on the ground by the survivor, a plant (and a
    /// rock) is drawn as the layer its distance picks (near, far, distant) and then whole (layer 2, the same shape at the
    /// same place), from several distances, and the two frames are compared: where the shadows differ, a layer's shadow
    /// is not the plant's own. Prints the share of the frame that differs; frames are --stem=PATH-ab-NAME-DISTANCE.png.
    /// </summary>
    IEnumerable<int> PlantShadowsAb()
    {
        var planet = _game.Zone.Planet;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        _game.Zone.Wildlife.Frozen = true;
        foreach (int x in SunAt((float)Num("elev", 7))) yield return x;
        var s = _game.Zone.Streamer;
        for (int i = 0; i < 60 * 40 && !(s.FarComplete && s.FarBehind == 0); i++) yield return 0;
        var sky = _game.Zone.Sky;
        sky.TimeOfDay = _sunTime;
        yield return 0;
        yield return 0;
        Vector3 sun = sky.Sun.GlobalBasis.Z.Normalized();
        Vector3 across = new Vector3(-sun.Z, 0f, sun.X).Normalized();
        Vector3 feet = _game.Survivor.WorldFeet;
        Vector3 at = Feet(feet + across * 6f);
        _game.Settings.Fov = 30f;
        _game.Settings.Changed();
        var drawn = new List<(string Name, Mesh Mesh, float Size)>();
        foreach (Plant p in planet.Flora.Book.Plants.Where(p => p.Large).Take(3)) drawn.Add(($"{p.Family}-{p.Form}", s.PlantMesh(p.Index, 0), p.Height));
        if (s.KindMesh(Flora.Rock) is Mesh rock) drawn.Add(("rock", rock, 1.5f));
        if (_args.ContainsKey("bias")) sky.Sun.ShadowBias = (float)Num("bias", 0.1);
        if (_args.ContainsKey("nbias")) sky.Sun.ShadowNormalBias = (float)Num("nbias", 2);
        if (_args.ContainsKey("blur")) sky.Sun.ShadowBlur = (float)Num("blur", 1);
        if (_args.ContainsKey("atlas")) RenderingServer.DirectionalShadowAtlasSetSize((int)Num("atlas", 4096), true);
        if (_args.ContainsKey("splits")) { sky.Sun.DirectionalShadowSplit1 = (float)Num("splits", 0.02); sky.Sun.DirectionalShadowSplit2 = sky.Sun.DirectionalShadowSplit1 * 3f; sky.Sun.DirectionalShadowSplit3 = sky.Sun.DirectionalShadowSplit1 * 12f; }
        GD.Print($"PLANTSHADOW-AB sun bias {sky.Sun.ShadowBias}, normal bias {sky.Sun.ShadowNormalBias}, blur {sky.Sun.ShadowBlur}, opacity {sky.Sun.ShadowOpacity:F2}, energy {sky.Sun.LightEnergy:F2}, max distance {sky.Sun.DirectionalShadowMaxDistance}, splits {sky.Sun.DirectionalShadowSplit1:F3}/{sky.Sun.DirectionalShadowSplit2:F3}/{sky.Sun.DirectionalShadowSplit3:F3}");
        ShaderMaterial mat = s.FloraMaterial;
        var tint = new Color(0.5f, 0.45f, 0.4f, 0f);
        MultiMeshInstance3D Draw(Mesh mesh, int layer)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = 1 };
            mm.SetInstanceTransform(0, new Transform3D(Basis.Identity, at));
            mm.SetInstanceCustomData(0, tint);
            var m = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
            m.SetInstanceShaderParameter("flora_layer", (float)layer);
            _game.AddChild(m);
            return m;
        }
        foreach (var (name, mesh, size) in drawn)
        foreach (float d in _args.GetValueOrDefault("dist", "30,80,112,150,250,300").Split(',').Select(float.Parse))
        {
            int layer = d < DetailReach.Near * 0.95f ? 0 : d < DetailReach.Far * 0.95f ? 1 : 3;
            Vector3 eye = Feet(at + across * d), look = at + Vector3.Up * size * 0.5f;
            if (eye.Y < 3f) { GD.Print($"PLANTSHADOW-AB {name} at {d} m: the camera would stand in the sea, skipped"); continue; }
            eye.Y = MathF.Max(eye.Y, at.Y) + 5f + d * 0.05f; // over the land between
            var frames = new List<Image>();
            foreach (int l in new[] { 2, layer })
            {
                MultiMeshInstance3D m = Draw(mesh, l);
                for (int i = 0; i < 20; i++)
                {
                    sky.TimeOfDay = _sunTime;
                    _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
                    yield return 0;
                }
                frames.Add(_game.GetViewport().GetTexture().GetImage());
                m.QueueFree();
                yield return 0;
            }
            int differ = 0, total = 0;
            for (int y = 0; y < frames[0].GetHeight(); y += 2)
            for (int x = 0; x < frames[0].GetWidth(); x += 2)
            {
                Color a = frames[0].GetPixel(x, y), b = frames[1].GetPixel(x, y);
                total++;
                if (MathF.Abs(a.R - b.R) + MathF.Abs(a.G - b.G) + MathF.Abs(a.B - b.B) > 0.06f) differ++;
            }
            GD.Print($"PLANTSHADOW-AB {name} at {d} m (layer {layer}): {100f * differ / total:F2}% of the frame differs from the whole drawing's");
            string stem = _args.GetValueOrDefault("stem", "flora");
            frames[0].SavePng($"{stem}-ab-{name}-{d:F0}-whole.png");
            frames[1].GetRegion(new Rect2I(frames[1].GetWidth() / 2 - 200, frames[1].GetHeight() / 2 - 100, 400, 200)).SavePng($"{stem}-ab-{name}-{d:F0}-crop.png");
            frames[1].SavePng($"{stem}-ab-{name}-{d:F0}-layer.png");
        }
        _game.FixedCamera = null;
    }
}
