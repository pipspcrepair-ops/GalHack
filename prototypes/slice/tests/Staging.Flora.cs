using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Plants;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Terrain;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The flora proof, in the game itself, on one fresh world: its thickest forest from eye height and
/// from above, the same woods in another region, the ground close up, a flowering bush close to and
/// from a distance, and frame time and memory with and without flora. --stem=PATH names the files.
/// </summary>
public sealed partial class Staging
{
    void FloraShot(string name)
    {
        string path = $"{_args.GetValueOrDefault("stem", "flora")}-{name}.png";
        Error e = _game.GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"FLORA {path}: {e}");
    }

    /// <summary>The tree density of the rules at a point, on this world.</summary>
    float Woodedness(double x, double z)
    {
        var planet = _game.Zone.Planet;
        return planet.Flora.RulesOf(planet.Palette.BiomeAt(x, z)).Where(r => r.Kind is Flora.Conifer or Flora.Broadleaf or Flora.JungleTree or Flora.Palm or Flora.Cactus or Flora.GiantMushroom).Sum(r => r.Density);
    }

    /// <summary>Open ground of a test: land, no slope to speak of, away from anything built.</summary>
    bool Standable(double x, double z)
    {
        var planet = _game.Zone.Planet;
        float h = planet.GroundHeight(x, z);
        if (h < 2f || planet.Source.StampWeight(x, z) > 0f) return false;
        float a = planet.GroundHeight(Mathx.WrapX(x + 12, planet.Width), z), b = planet.GroundHeight(x, Mathx.Wrap(z + 12, planet.Height));
        return MathF.Abs(a - h) < 3.5f && MathF.Abs(b - h) < 3.5f;
    }

    /// <summary>The spot nearest a point that scores best, searched outward on a 64 m grid.</summary>
    Vector3? Best(Vector3 near, Func<double, double, float> score, double minDistance = 0)
    {
        var planet = _game.Zone.Planet;
        float best = 0f;
        Vector3? at = null;
        for (double z = 32; z < planet.Height; z += 64)
        for (double x = 32; x < planet.Width; x += 64)
        {
            float s = score(x, z);
            if (s <= 0f) continue;
            float dx = (float)Mathx.WrapDelta(x - Mathx.WrapX(near.X, planet.Width), planet.Width), dz = (float)Mathx.WrapDelta(z - Mathx.Wrap(near.Z, planet.Height), planet.Height);
            float d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < minDistance) continue;
            s /= 1f + d / 6000f;
            if (s > best && Standable(x, z))
            {
                best = s;
                at = new Vector3((float)x, 0f, (float)z);
            }
        }
        return at;
    }

    IEnumerable<int> Arrive(Vector3 at)
    {
        var planet = _game.Zone.Planet;
        at = planet.NearestCopy(at, _game.Survivor.WorldFeet);
        _game.Survivor.PlaceOutside(Feet(at).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(420)) yield return x;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.43); // a long wait on a world with short days runs on into dusk
    }

    /// <summary>A camera at eye height, turned to the way with most standing in it and no trunk in its face.</summary>
    Transform3D EyeLook(Vector3 feet, float height = 1.7f, float upward = 0.06f)
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.43);
        var plants = _game.Zone.Streamer.FloraNear(feet, 70f).Where(p => FloraMeshes_Large(p.Kind)).ToList();
        Vector3 best = Vector3.Forward;
        float top = float.MinValue;
        for (int a = 0; a < 16; a++)
        {
            var dir = new Vector3(Mathf.Cos(a * Mathf.Pi / 8f), 0f, Mathf.Sin(a * Mathf.Pi / 8f));
            float score = 0f;
            foreach (var p in plants)
            {
                Vector3 to = (p.Root - feet - dir * 3f) with { Y = 0f }; // from where the camera will stand
                float d = to.Length(), along = to.Dot(dir), off = (to - dir * along).Length();
                if (along < 0f) continue;
                if (off < 1.6f + along * 0.12f && d < 12f) score -= 25f;
                else if (along > 8f && off < along * 0.55f) score += 1f;
            }
            if (score > top)
            {
                top = score;
                best = dir;
            }
        }
        // Out in front of the survivor, or the camera sits inside them.
        Vector3 eye = Feet(feet + best * 3f) + Vector3.Up * height;
        return new Transform3D(Basis.LookingAt(best + Vector3.Up * upward, Vector3.Up), eye);
    }

    static bool FloraMeshes_Large(Flora k) => Planets.Surface.FloraMeshes.IsLarge(k);

    void ListWorld()
    {
        var planet = _game.Zone.Planet;
        Herbarium book = planet.Flora.Book;
        GD.Print($"FLORA world {_game.World}, {planet.Spec}, star {planet.Sky.Star}, air {planet.Sky.Air} {planet.Sky.Density:F2}; {book.Plants.Count} species, life {book.Climate.Life:F2}, warm {book.Climate.Warmth:F2}, wet {book.Climate.Wetness:F2}");
        foreach (var family in book.Plants.GroupBy(p => p.Family))
            GD.Print($"FLORA   {family.Key}: {string.Join(", ", family.Select(p => $"{p.Form} {p.Height:F1} m #{Rgb.Byte(p.Leaf.R):x2}{Rgb.Byte(p.Leaf.G):x2}{Rgb.Byte(p.Leaf.B):x2} {p.Near[0].Triangles} tris"))}");
    }

    IEnumerable<int> FloraProof()
    {
        var planet = _game.Zone.Planet;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.43);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        _game.Zone.Wildlife.Frozen = true;
        ListWorld();
        Vector3 start = _game.Survivor.WorldFeet;

        Vector3? wood = Best(start, Woodedness);
        if (wood == null)
        {
            GD.Print("FLORA no woods on this world");
            yield break;
        }
        Biome biome = planet.Palette.BiomeAt(wood.Value.X, wood.Value.Z);
        GD.Print($"FLORA woods in {biome} at {wood.Value.X:F0}, {wood.Value.Z:F0}, density {Woodedness(wood.Value.X, wood.Value.Z):F1}, region {planet.Flora.RegionAt(wood.Value.X, wood.Value.Z):F2}");
        // Numbers only: stand in the woods and measure, no frames.
        if (_args.ContainsKey("costonly"))
        {
            foreach (int x in FloraCost(wood.Value)) yield return x;
            yield break;
        }
        foreach (int x in Arrive(wood.Value)) yield return x;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.43);
        Vector3 feet = _game.Survivor.WorldFeet;
        foreach (var kin in _game.Zone.Streamer.FloraNear(feet, 90f).Where(p => p.Plant >= 0).GroupBy(p => p.Plant).OrderByDescending(g => g.Count()).Take(6))
            GD.Print($"FLORA   here: {planet.Flora.Book.Plants[kin.Key].Family} {planet.Flora.Book.Plants[kin.Key].Form} x{kin.Count()}");
        _game.FixedCamera = EyeLook(feet);
        foreach (int x in _drive.Wait(20)) yield return x;
        FloraShot("forest");
        Transform3D eye = _game.FixedCamera.Value;
        Vector3 ahead = eye.Origin - eye.Basis.Z * 46f;
        Vector3 high = eye.Origin + eye.Basis.Z * 34f + Vector3.Up * 24f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(ahead - high, Vector3.Up), high);
        foreach (int x in _drive.Wait(30)) yield return x;
        FloraShot("forest-high");
        _game.FixedCamera = null;

        if (!_args.ContainsKey("short"))
        {
            // The same biome in another region, as far off as it takes for other trees to lead.
            Plant Lead(double x, double z)
            {
                Biome b = planet.Palette.BiomeAt(x, z);
                foreach (FloraRule r in planet.Flora.RulesOf(b).Where(r => PlanetFlora.FamilyOf(r.Kind) != null && FloraMeshes_Large(r.Kind)).OrderByDescending(r => r.Density))
                    return planet.Flora.Book.Pick((int)b, PlanetFlora.FamilyOf(r.Kind).Value, planet.Flora.RegionAt(x, z), 0.5f);
                return null;
            }
            Plant here = Lead(wood.Value.X, wood.Value.Z);
            // Far enough for the region to have turned; the same biome if it runs that far, else any wooded land.
            Vector3? far = Best(wood.Value, (x, z) => planet.Palette.BiomeAt(x, z) == biome && Lead(x, z) != here ? Woodedness(x, z) : 0f, 1800)
                ?? Best(wood.Value, (x, z) => Lead(x, z) != here ? Woodedness(x, z) : 0f, 1200);
            if (far is Vector3 other)
            {
                GD.Print($"FLORA second region {other.X:F0}, {other.Z:F0}, region {planet.Flora.RegionAt(other.X, other.Z):F2}, leading {Lead(other.X, other.Z)?.Form}, first {here?.Form}");
                foreach (int x in Arrive(other)) yield return x;
                Vector3 there = _game.Survivor.WorldFeet;
                foreach (var kin in _game.Zone.Streamer.FloraNear(there, 90f).Where(p => p.Plant >= 0).GroupBy(p => p.Plant).OrderByDescending(g => g.Count()).Take(6))
                    GD.Print($"FLORA   there: {planet.Flora.Book.Plants[kin.Key].Family} {planet.Flora.Book.Plants[kin.Key].Form} x{kin.Count()}");
                _game.FixedCamera = EyeLook(there);
                foreach (int x in _drive.Wait(20)) yield return x;
                FloraShot("region2");
                _game.FixedCamera = null;
            }
            else GD.Print("FLORA no second region found");

            // Open country: the ground up close, then flowering bushes close to and far off.
            Vector3? open = Best(start, (x, z) =>
            {
                CoverMix m = PlanetFlora.CoverOf(planet.Palette.BiomeAt(x, z));
                return m.Grass + 2f * m.Forb + m.Pins + m.Moss;
            });
            if (open is Vector3 field)
            {
                GD.Print($"FLORA ground in {planet.Palette.BiomeAt(field.X, field.Z)}");
                foreach (int x in Arrive(field)) yield return x;
                Vector3 g = _game.Survivor.WorldFeet;
                GD.Print($"FLORA cover {_game.Zone.Streamer.CoverInstances} instances in {_game.Zone.Streamer.CoverDrawCalls} draw calls");
                Transform3D look = EyeLook(g, 0.8f, -0.1f);
                _game.FixedCamera = look;
                foreach (int x in _drive.Wait(20)) yield return x;
                FloraShot("ground");
                _game.FixedCamera = EyeLook(g, 1.7f, -0.03f);
                foreach (int x in _drive.Wait(20)) yield return x;
                FloraShot("ground-eye");
                _game.FixedCamera = null;
            }
            Vector3? blooms = Best(start, (x, z) => planet.Flora.RulesOf(planet.Palette.BiomeAt(x, z)).Where(r => r.Kind == Flora.Shrub).Sum(r => r.Density) * (planet.Palette.BiomeAt(x, z) == Biome.Meadow ? 3f : 1f));
            if (blooms is Vector3 meadow)
            {
                foreach (int x in Arrive(meadow)) yield return x;
                Vector3 g = _game.Survivor.WorldFeet;
                var bushes = _game.Zone.Streamer.FloraNear(g, 110f).Where(p => p.Kind == Flora.Shrub).OrderBy(p => (planet.Flora.Book.Plants[p.Plant].Form == PlantForm.Bush ? 0f : 400f) + (planet.Flora.Book.Plants[p.Plant].Flowers ? 0f : 200f) + p.Root.DistanceTo(g)).ToList();
                GD.Print($"FLORA {bushes.Count} shrubs in {planet.Palette.BiomeAt(meadow.X, meadow.Z)}, {bushes.Count(p => planet.Flora.Book.Plants[p.Plant].Flowers)} in flower");
                if (bushes.Count > 0)
                {
                    var bush = bushes[0];
                    Plant p = planet.Flora.Book.Plants[bush.Plant];
                    GD.Print($"FLORA bush {p.Form} {p.Height * bush.Scale:F1} m, {p.Near[bush.Variant].Triangles} tris, leaf {p.Leaf}, bloom {p.Bloom}");
                    Vector3 side = new Vector3(0.8f, 0f, 0.6f);
                    Vector3 eyeAt = Feet(bush.Root + side * 2.6f) + Vector3.Up * 1.05f;
                    _game.FixedCamera = new Transform3D(Basis.LookingAt(bush.Root + Vector3.Up * 0.55f - eyeAt, Vector3.Up), eyeAt);
                    foreach (int x in _drive.Wait(20)) yield return x;
                    FloraShot("bush-close");
                    Vector3 backAt = Feet(bush.Root + side * 26f) + Vector3.Up * 1.9f;
                    _game.FixedCamera = new Transform3D(Basis.LookingAt(bush.Root + Vector3.Up * 0.6f - backAt, Vector3.Up), backAt);
                    foreach (int x in _drive.Wait(20)) yield return x;
                    FloraShot("bush-far");
                    _game.FixedCamera = null;
                }
            }
        }
        if (_args.ContainsKey("cost")) foreach (int x in FloraCost(wood.Value)) yield return x;
    }

    /// <summary>Frame time and memory in the thick of the woods: with no flora drawn, with stands only, with cover as well.</summary>
    IEnumerable<int> FloraCost(Vector3 at)
    {
        foreach (int x in Arrive(at)) yield return x;
        var streamer = _game.Zone.Streamer;
        _game.FixedCamera = EyeLook(_game.Survivor.WorldFeet);
        RenderingServer.ViewportSetMeasureRenderTime(_game.GetViewport().GetViewportRid(), true);
        foreach (int x in _drive.Wait(60)) yield return x;
        long meshBytes = 0;
        foreach (Plant p in _game.Zone.Planet.Flora.Book.Plants)
        {
            foreach (PlantMesh m in p.Near) meshBytes += m.Bytes;
            if (p.Far != p.Near[0]) meshBytes += p.Far.Bytes;
        }
        GD.Print($"FLORACOST species meshes {meshBytes / 1048576f:F2} MB, cover {streamer.CoverInstances} instances ({streamer.CoverInstances * 64 / 1048576f:F2} MB) in {streamer.CoverDrawCalls} calls, worst far node {streamer.WorstFarNodeMs:F2} ms ({streamer.WorstFarNode}), worst streaming frame {streamer.WorstFrameMs:F2} ms");

        void Show(Node n, bool on)
        {
            foreach (Node c in n.GetChildren())
            {
                if (c is MultiMeshInstance3D m) m.Visible = on;
                Show(c, on);
            }
        }
        for (int repeat = 0; repeat < 2; repeat++)
        foreach (string mode in new[] { "none", "stands", "all" })
        {
            Show(streamer, mode != "none");
            streamer.CoverVisible = mode == "all";
            if (mode == "stands") foreach (Node c in streamer.GetChildren()) if (c.Name == "Cover") Show(c, true);
            foreach (int x in _drive.Wait(90)) yield return x;
            var cpu = new List<double>();
            var gpu = new List<double>();
            for (int i = 0; i < 180; i++)
            {
                cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(_game.GetViewport().GetViewportRid()));
                gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(_game.GetViewport().GetViewportRid()));
                yield return 0;
            }
            cpu.Sort();
            gpu.Sort();
            GD.Print($"FLORACOST {mode}: render cpu median {cpu[cpu.Count / 2]:F2} ms, gpu median {gpu[gpu.Count / 2]:F2} ms, p95 {gpu[gpu.Count * 19 / 20]:F2}; fps {Performance.GetMonitor(Performance.Monitor.TimeFps):F0}, process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0:F2} ms; calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):F0}, primitives {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame):F0}; video {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:F0} MB, static {Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0:F0} MB, managed {GC.GetTotalMemory(false) / 1048576.0:F0} MB");
        }
        Show(streamer, true);
        streamer.CoverVisible = true;
        _game.FixedCamera = null;
    }
}
