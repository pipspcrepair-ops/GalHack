using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Planets.Gen;
using PlanetSurface.Skies;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface;

/// <summary>
/// Renders one view of the planet surface to a PNG. Run windowed:
///   godot --path prototypes/planet-surface res://tests/Screenshot.tscn -- --view=NAME --out=FILE.png
/// Named views are in <see cref="Views"/>; "look" takes everything from arguments:
///   --archetype=0-4 --variant=0|1 --seed=N --x= --z= (metres) --up= (above ground) --heading= --pitch= (degrees)
///   --time=0..1 --weather=Kind [--hud=1]
/// </summary>
public partial class Screenshot : Node
{
    const int SettleFrames = 90;
    const double TimeoutSeconds = 120;

    Main _main;
    string _out = "screenshot.png", _view = "look";
    readonly Dictionary<string, string> _args = new();
    ViewSpec _spec;
    int _stage, _settled;
    double _elapsed;

    /// <summary>One named view: the planet, where to stand, when, and in what weather.</summary>
    public sealed record ViewSpec(PlanetSpec Planet, Func<Main, (Vector3 pos, float heading, float pitch)> Place, double Time,
        WeatherKind? Weather = null, GroundCover? Ground = null, Action<Main> Prepare = null, Action<Main> BeforeCapture = null,
        Func<Main, double> TimeFor = null, string Force = null, Func<Main, List<Shot>> Series = null);

    /// <summary>One frame of a series: a name for its file (FILE-name.png) and where the camera stands.</summary>
    public sealed record Shot(string Name, Vector3 Pos, float Heading, float Pitch);

    List<Shot> _series;
    int _shot;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--") && eq > 2) _args[arg[2..eq]] = arg[(eq + 1)..];
        }
        if (_args.TryGetValue("out", out string o)) _out = o;
        if (_args.TryGetValue("view", out string v)) _view = v;
        _spec = _view == "look" ? Look() : Views.Get(_view);
        if (_spec == null)
        {
            GD.PrintErr($"no view '{_view}'");
            GetTree().Quit(1);
            return;
        }
        PlanetSurface.Poi.PoiSiting.ForceFamily = _spec.Force;
        _main = new Main { Interactive = false };
        AddChild(_main);
        _main.Arrive(_spec.Planet);
    }

    ViewSpec Look()
    {
        int a = Int("archetype", 0), variant = Int("variant", 0);
        ulong seed = (ulong)Int("seed", 1);
        var planet = PlanetSpec.Roll(seed, (Archetype)a, variant);
        WeatherKind? weather = _args.TryGetValue("weather", out string w) ? Enum.Parse<WeatherKind>(w) : null;
        _args.TryGetValue("force", out string force);
        if (_args.TryGetValue("state", out string state)) PlanetSurface.Poi.PoiSiting.ForceState = Enum.Parse<PoiGenerator.Gen.PoiState>(state, true);
        if (_args.ContainsKey("forcevariant")) PlanetSurface.Poi.PoiSiting.ForceVariant = Int("forcevariant", 0);
        if (_args.ContainsKey("tiered")) PlanetSurface.Poi.PoiLayer.Tiered = true;
        _args.TryGetValue("city", out string cityView);
        if (cityView != null) PlanetSurface.Infra.CityPlan.Force = true;
        if (_args.ContainsKey("cityfall")) PlanetSurface.Infra.CityPlan.ForceFall = (float)Num("cityfall", 0.5);
        return new ViewSpec(planet, m =>
        {
            // A place's own frame: the camera and what it looks at, in the place's cells.
            if (force != null && _args.TryGetValue("cam", out string cam))
            {
                var site = m.World.Pois.First(p => p.Family.Id == force);
                GD.Print($"forced {site.Name} at {site.Origin.X},{site.Origin.Y},{site.Origin.Z}, stamp {site.Stamp.MinX}..{site.Stamp.MaxX} x {site.Stamp.MinZ}..{site.Stamp.MaxZ}, entrance {site.Entrance.X},{site.Entrance.Y},{site.Entrance.Z}");
                Vector3 Local(string text)
                {
                    var n = text.Split(',').Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray();
                    return new Vector3(site.Origin.X + n[0], site.Origin.Y + n[1], site.Origin.Z + n[2]);
                }
                Vector3 from = Local(cam), at = Local(_args["at"]), d = at - from;
                if (_args.TryGetValue("figure", out string figure)) _figure = Local(figure);
                return (from, Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), Mathf.RadToDeg(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length())));
            }
            // A region from the air: what its plan set down, listed, with the camera over its first town.
            if (_args.ContainsKey("region") && m.World.Infra.RegionPlan is { } plan)
            {
                GD.Print($"{plan.Roll}: {plan.Regions.Count} regions");
                foreach (var pp in plan.Places) GD.Print($"  {pp.Role} {pp.Family} at {pp.X:F0},{pp.Z:F0}: {pp.Why}");
                foreach (var l in plan.Links) GD.Print($"  {l.Kind}: {plan.Places[l.A].Family} to {plan.Places[l.B].Family}: {l.Why}");
                var hub = plan.Places.First(p => p.Role == "hub" && p.Seated != null);
                float cx = (float)hub.X, cz = (float)hub.Z, g0 = m.World.GroundHeight(hub.X, hub.Z);
                float back = (float)Num("back", 1400), up = (float)Num("up", 900), turn = (float)Num("heading", 0);
                var from = new Vector3(cx - Mathf.Sin(Mathf.DegToRad(turn)) * back, g0 + up, cz + Mathf.Cos(Mathf.DegToRad(turn)) * back);
                return (from, turn, -Mathf.RadToDeg(Mathf.Atan2(up, back)));
            }
            double x = Num("x", m.World.Landing.X), z = Num("z", m.World.Landing.Y);
            float ground = m.World.GroundHeight(x, z);
            return (new Vector3((float)x, ground + (float)Num("up", 20), (float)z), (float)Num("heading", 0), (float)Num("pitch", -8));
        }, Num("time", 0.42), weather, Force: force, BeforeCapture: AddFigure,
            Series: cityView == null ? null : m => m.World.Infra.City is { Sites.Count: > 0 } city ? CityShots(m, city, cityView) : null);
    }

    /// <summary>
    /// A city's frames (--city=all, or one of them by name): its skyline from 2 km and from 500 m across the water,
    /// from the air over the land toward the water, down a downtown avenue, at a house's frontage, inside a tower's lobby.
    /// </summary>
    List<Shot> CityShots(Main m, PlanetSurface.Infra.CityPlan city, string which)
    {
        float cxf = (float)city.CenterX, czf = (float)city.CenterZ, g0 = m.World.GroundHeight(city.CenterX, city.CenterZ);
        var lots = city.Lots.Where(l => l.Site != null).ToList();
        var tall = lots.Where(l => l.Kind == "tower").OrderByDescending(l => l.Lot.Storeys).FirstOrDefault() ?? lots.OrderByDescending(l => l.Lot.Storeys).First();
        GD.Print($"city at {city.CenterX:F0},{city.CenterZ:F0} radius {city.Radius:F0}, {lots.Count} buildings; tallest {tall.Lot.Storeys} storeys at {tall.Crossover.X:F0},{tall.Crossover.Z:F0}; " +
                 $"districts {string.Join(", ", city.Districts.Select(d => $"{d.Key} {d.Value}"))}; kinds {string.Join(", ", lots.GroupBy(l => l.Kind).Select(g => $"{g.Key} {g.Count()}"))}; " +
                 $"{lots.Count(l => l.Lot.Lean)} leaning, {lots.Count(l => l.Lot.Toppled)} toppled, {lots.Count(l => l.Site.Instance.State == PoiGenerator.Gen.PoiState.Active)} lit; bank {(city.Bank != null ? "yes" : "no")}, {city.Bridges.Count} bridges, station {(city.Tracks != null ? "yes" : "no")}");
        // Toward the water from the middle, or the heading asked for.
        Vector2 toWater = new((float)city.WaterX, (float)city.WaterZ);
        if (_args.ContainsKey("heading")) toWater = new Vector2(Mathf.Sin(Mathf.DegToRad((float)Num("heading", 0))), -Mathf.Cos(Mathf.DegToRad((float)Num("heading", 0))));
        if (toWater.LengthSquared() < 1e-4f) toWater = new Vector2(0, 1);
        toWater = toWater.Normalized();
        Shot Toward(string name, Vector3 from, Vector3 at)
        {
            // Never in the ground: a camera far out stands well over whatever is under it.
            from.Y = Mathf.Max(from.Y, m.World.GroundHeight(from.X, from.Z) + 25f);
            Vector3 d = at - from;
            return new Shot(name, from, Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), Mathf.RadToDeg(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length())));
        }
        Shot Level(string name, Vector3 from, Vector3 at)
        {
            Vector3 d = at - from;
            return new Shot(name, from, Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), Mathf.RadToDeg(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length())));
        }
        Vector3 Over(Vector2 dir, float dist, float up) => new(cxf + dir.X * dist, g0 + up, czf + dir.Y * dist);
        var centre = new Vector3(cxf, g0 + (float)Num("look", 60), czf);
        // From the water's side where the land leaves a clear line to the middle, else the nearest heading round from it that does.
        Vector2 Clear(float dist, float up)
        {
            for (int k = 0; k < 24; k++)
            {
                float turn = Mathf.DegToRad(15f * ((k + 1) / 2) * (k % 2 == 0 ? 1 : -1));
                Vector2 dir = toWater.Rotated(turn);
                Vector3 from = Over(dir, dist, up);
                from.Y = Mathf.Max(from.Y, m.World.GroundHeight(from.X, from.Z) + 25f);
                bool clear = true;
                for (float t = 0.05f; t < 0.85f && clear; t += 0.025f)
                {
                    Vector3 p = from.Lerp(centre, t);
                    clear = m.World.GroundHeight(p.X, p.Z) < p.Y - 4f;
                }
                if (clear) return dir;
            }
            return toWater;
        }
        float farDist = (float)Num("dist", 2000), midDist = 500 + (float)city.Radius * 0.3f;
        var shots = new List<Shot>
        {
            Toward("far", Over(Clear(farDist, 90), farDist, 90), centre),
            Toward("mid", Over(Clear(midDist, 45), midDist, 45), centre),
            Toward("aerial", Over(-toWater, 900, 650), new Vector3(cxf, g0, czf)),
        };
        // How it fell: a leaning or toppled tower from 140 m off, on the side its lean faces.
        if (lots.Where(l => l.Lot.Lean || l.Lot.Toppled).OrderByDescending(l => l.Lot.Lean).ThenBy(l => Math.Abs((l.X0 + l.X1) / 2 - city.CenterX) + Math.Abs((l.Z0 + l.Z1) / 2 - city.CenterZ)).FirstOrDefault() is { } fallen)
        {
            var at = new Vector3((float)(fallen.X0 + fallen.X1) / 2, fallen.Pad + 3f * fallen.Lot.Storeys, (float)(fallen.Z0 + fallen.Z1) / 2);
            Vector2 side = new Vector2(-toWater.Y, toWater.X);
            shots.Add(Toward("fallen", new Vector3(at.X + side.X * 140, fallen.Pad + 40, at.Z + side.Y * 140), at));
        }
        // The waterfront from the water: its wall, the road on top, the city behind.
        if (city.Waterfront.FirstOrDefault() is { } front && front.Road.Count > 8)
        {
            var r = front.Road;
            int i = r.Count / 2;
            double dx = r.X[i + 1] - r.X[i - 1], dz = r.Z[i + 1] - r.Z[i - 1], l = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
            float ox = (float)(-dz / l * r.WallSide), oz = (float)(dx / l * r.WallSide), reach = r.HalfWidth + 1.5f;
            var top = new Vector3((float)r.X[i] + ox * reach, r.Y[i], (float)r.Z[i] + oz * reach);
            // Out over the water: past the wall to the water, then on to 40 m out or the middle of a narrow river.
            float first = -1, last = -1, level = 0;
            for (float t = 2; t <= 160; t += 2)
            {
                bool wet = m.World.Source.WaterSurfaceAt(Mathx.WrapX(top.X + ox * t, m.World.Width), Mathx.Wrap(top.Z + oz * t, m.World.Height), out float lv);
                if (wet && first < 0) (first, level) = (t, lv);
                if (wet) last = t;
                else if (first >= 0) break;
            }
            float off = first < 0 ? 45f : Mathf.Min(first + 40f, (first + last) * 0.5f);
            var eye = top + new Vector3(ox, 0, oz) * off + new Vector3((float)(dx / l), 0, (float)(dz / l)) * 20f;
            eye.Y = Mathf.Max(first < 0 ? top.Y + 4f : level + 4f, m.World.GroundHeight(eye.X, eye.Z) + 3f);
            shots.Add(Level("waterfront", eye, top - new Vector3(0, 1.5f, 0)));
        }
        // Down an avenue toward the middle: from its point about 120 m out, 1.7 m over its deck.
        var avenue = city.Streets.Where(s => s.HalfWidth >= PlanetSurface.Infra.CityPlan.Avenue).OrderBy(s => Enumerable.Range(0, s.Count).Min(i => Dist(s, i, city))).FirstOrDefault();
        if (avenue != null)
        {
            int near = Enumerable.Range(0, avenue.Count).OrderBy(i => Dist(avenue, i, city)).First();
            int from = near + 30 < avenue.Count ? near + 30 : Math.Max(0, near - 30);
            var eye = new Vector3((float)avenue.X[from], avenue.Y[from] + 1.7f, (float)avenue.Z[from]);
            shots.Add(Level("street", eye, new Vector3((float)avenue.X[near], avenue.Y[near] + 22f, (float)avenue.Z[near])));
        }
        // A house's frontage from its street: lawn, drive, fence and the house set back behind them.
        if (lots.Where(l => l.Kind is "house" or "terrace").OrderBy(l => l.Kind == "house" ? 0 : 1).ThenBy(l => Math.Abs((l.X0 + l.X1) / 2 - city.CenterX) + Math.Abs((l.Z0 + l.Z1) / 2 - city.CenterZ)).FirstOrDefault() is { } home)
        {
            bool alongX = home.Front is PoiGenerator.Gen.Dir.North or PoiGenerator.Gen.Dir.South;
            float sx = (float)home.Crossover.X + (alongX ? 16f : 0f), sz = (float)home.Crossover.Z + (alongX ? 0f : 16f);
            var eye = new Vector3(sx, m.World.GroundHeight(sx, sz) + 1.7f, sz);
            shots.Add(Level("homes", eye, new Vector3((float)home.Gate.X, home.Pad + 2.5f, (float)home.Gate.Z)));
        }
        // The city's services from their streets: a fire station, a police station, a school, a campus.
        foreach (string kind in new[] { "fire", "police", "school", "university" })
            if (lots.FirstOrDefault(l => l.Kind == kind) is { } svc)
            {
                bool alongX = svc.Front is PoiGenerator.Gen.Dir.North or PoiGenerator.Gen.Dir.South;
                float back = kind is "school" or "university" ? 30f : 18f, outward = svc.Front is PoiGenerator.Gen.Dir.North or PoiGenerator.Gen.Dir.West ? -1f : 1f;
                float sx = (float)svc.Crossover.X + (alongX ? 14f : outward * 6f), sz = (float)svc.Crossover.Z + (alongX ? outward * 6f : 14f);
                var eye = new Vector3(sx, m.World.GroundHeight(sx, sz) + (kind is "school" or "university" ? 9f : 1.7f), sz);
                shots.Add(Level(kind, eye, new Vector3((float)(svc.X0 + svc.X1) / 2, svc.Pad + 4f, (float)(svc.Z0 + svc.Z1) / 2)));
            }
        // Inside the tallest tower's lobby: at eye height where nothing is nearer than 2 m, toward the lobby's far side.
        {
            var site = tall.Site;
            var inst = site.Instance;
            var lobby = inst.Layout.Modules.FirstOrDefault(md => md.Purpose == "lobby");
            if (lobby != null)
            {
                var cells = lobby.Cells.ToList();
                PoiGenerator.Gen.Int3 lo = cells.Aggregate(PoiGenerator.Gen.Int3.Min), hi = cells.Aggregate(PoiGenerator.Gen.Int3.Max);
                bool Open(PoiGenerator.Gen.Int3 c) => !PoiGenerator.Gen.BlockInfo.Collides(inst.Blocks.Get(c).Type);
                var spot = cells.Where(c => c.Y == lo.Y + 1)
                    .Where(c => Enumerable.Range(-2, 5).All(dx => Enumerable.Range(-2, 5).All(dz => Enumerable.Range(0, 3).All(dy => Open(new PoiGenerator.Gen.Int3(c.X + dx, lo.Y + dy, c.Z + dz))))))
                    .OrderBy(c => Math.Abs(c.X - (lo.X + (hi.X - lo.X) / 4)) + Math.Abs(c.Z - (lo.Z + (hi.Z - lo.Z) / 4))).FirstOrDefault();
                var eye = new Vector3(site.Origin.X + spot.X + 0.5f, site.Origin.Y + lo.Y + 1.7f, site.Origin.Z + spot.Z + 0.5f);
                var far = new Vector3(site.Origin.X + hi.X + 0.5f, site.Origin.Y + lo.Y + 1.9f, site.Origin.Z + hi.Z + 0.5f);
                GD.Print($"lobby {lo}..{hi}, eye at {spot}");
                shots.Add(Level("inside", eye, far));
            }
        }
        return which == "all" ? shots : shots.Where(s => s.Name == which).ToList();
    }

    static double Dist(PlanetSurface.Infra.InfraRoute r, int i, PlanetSurface.Infra.CityPlan city) => Math.Abs(r.X[i] - city.CenterX) + Math.Abs(r.Z[i] - city.CenterZ);

    Vector3? _figure;

    /// <summary>A person for scale: 1.8 m, in a bright coat, standing where asked.</summary>
    void AddFigure(Main main)
    {
        if (_figure is not Vector3 at) return;
        var coat = new StandardMaterial3D { AlbedoColor = new Color("d8552a") };
        var head = new StandardMaterial3D { AlbedoColor = new Color("e0c0a0") };
        var body = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.26f, Height = 1.55f, Material = coat }, Position = at + new Vector3(0, 0.78f, 0) };
        var top = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.14f, Height = 0.28f, Material = head }, Position = at + new Vector3(0, 1.67f, 0) };
        main.AddChild(body);
        main.AddChild(top);
    }

    int Int(string key, int fallback) => _args.TryGetValue(key, out string s) ? int.Parse(s, CultureInfo.InvariantCulture) : fallback;
    double Num(string key, double fallback) => _args.TryGetValue(key, out string s) ? double.Parse(s, CultureInfo.InvariantCulture) : fallback;

    public override void _Process(double delta)
    {
        if (_main == null) return;
        if ((_elapsed += delta) > TimeoutSeconds)
        {
            GD.PrintErr($"timed out (stage {_stage}, streamer pending {_main.Streamer.Pending}, settled {_main.Streamer.Settled})");
            GetTree().Quit(1);
            return;
        }
        switch (_stage)
        {
            case 0:
                if (_main.World == null) return;
                _spec.Prepare?.Invoke(_main);
                _series = _spec.Series?.Invoke(_main);
                var (pos, heading, pitch) = _series is { Count: > 0 } ? (_series[0].Pos, _series[0].Heading, _series[0].Pitch) : _spec.Place(_main);
                _main.Fly.Begin(pos, -Mathf.DegToRad(heading), Mathf.DegToRad(pitch));
                _main.Player.ProcessMode = ProcessModeEnum.Disabled;
                _main.Sky.TimeOfDay = _spec.TimeFor?.Invoke(_main) ?? _spec.Time;
                _main.Sky.Paused = true;
                if (_spec.Weather is WeatherKind kind) _main.Weather.Snap(kind, _spec.Ground);
                if (!_args.TryGetValue("hud", out string hud) || hud != "1") _main.Hud.HideAll();
                if (_args.TryGetValue("debug", out string debug)) _main.Sky.DebugView(int.Parse(debug));
                // The ground without its roads drawn, to weigh what drawing them costs.
                if (_args.ContainsKey("noroads"))
                    foreach (ShaderMaterial m in new[] { _main.Streamer.VoxelMaterial, _main.Streamer.FarMaterial }) m.SetShaderParameter("road_grid", Vector4.Zero);
                RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
                _stage = 1;
                break;
            case 1:
                _main.Streamer.SetFocus(_main.Camera.GlobalPosition);
                bool settled = _main.Streamer.Settled && _main.Pois.Built && _main.Infra.Built && (!PlanetSurface.Poi.PoiLayer.Tiered || !_main.Pois.MidPending);
                _settled = settled ? _settled + 1 : 0;
                if (_settled == SettleFrames - 12) _spec.BeforeCapture?.Invoke(_main);
                if (_settled == SettleFrames - 12 && _args.TryGetValue("hide", out string hide))
                    foreach (Node n in _main.Pois.GetChildren())
                        foreach (Node c in n.GetChildren())
                            if ((c is CpuParticles3D && hide.Contains("smoke")) || (c is MeshInstance3D && hide.Contains("mesh"))) ((Node3D)c).Visible = false;
                if (_settled < SettleFrames) return;
                _stage = 2;
                Capture();
                break;
        }
    }

    async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        string file = _series is { Count: > 0 } ? _out.Replace(".png", $"-{_series[_shot].Name}.png") : _out;
        Error result = image.SavePng(file);
        Vector3 p = _main.Camera.GlobalPosition;
        GD.Print($"{_main.World.Spec} · {_main.World.Sky.Summary} · camera {p.X:F0}, {p.Y:F0}, {p.Z:F0} · " +
                 $"{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame) / 1000} k primitives, " +
                 $"{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} draw calls, " +
                 $"{RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()):F1} ms on the GPU, " +
                 $"{RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()) + RenderingServer.GetFrameSetupTimeCpu():F1} ms rendering on the CPU, " +
                 $"{Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:F1} ms process");
        Vector3 fwd = -_main.Camera.GlobalBasis.Z;
        GD.Print($"time {_main.Sky.TimeOfDay:F3} · sun {_main.Sky.ToSun.X:F2}, {_main.Sky.ToSun.Y:F2}, {_main.Sky.ToSun.Z:F2} · facing {fwd.X:F2}, {fwd.Y:F2}, {fwd.Z:F2}");
        if (_main.Pois.DecoRooms > 0) GD.Print($"rooms dressed from the decorations library: {_main.Pois.DecoRooms}");
        GD.Print(result == Error.Ok ? $"saved {file}" : $"could not save {file}: {result}");
        // A series: on to its next frame, the world kept.
        if (result == Error.Ok && _series != null && ++_shot < _series.Count)
        {
            Shot next = _series[_shot];
            _main.Fly.Begin(next.Pos, -Mathf.DegToRad(next.Heading), Mathf.DegToRad(next.Pitch));
            _elapsed = 0;
            _settled = 0;
            _stage = 1;
            return;
        }
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}

/// <summary>
/// The named views committed as screenshots. Each finds its own spot from the terrain (a peak to
/// look at, the deepest canyon, a ridge over low ground, a POI on a slope, a hillside to dig into),
/// so they don't depend on hand-picked coordinates.
/// </summary>
public static class Views
{
    public static Screenshot.ViewSpec Get(string name) => name switch
    {
        // A temperate world whose system has a ringed giant: a mountain range under it.
        "mountains" => new(new PlanetSpec(21, Archetype.Temperate, 0, Modifier.RingedSky | Modifier.Highlands),
            TowardGiant, 0.36, TimeFor: SunBehindGiantView,
            Prepare: m => m.Weather.SnapPartway(WeatherKind.Clear, WeatherKind.Clouds, 0.4f)),
        "canyon" => new(PlanetSpec.Roll(7, Archetype.Arid, 1), CanyonView, 0.33, WeatherKind.Clear),
        // A humid rainforest world just after sunrise: fog has pooled in the valleys.
        "foggy_valley" => new(PlanetSpec.Roll(12, Archetype.Temperate, 1), RidgeOverValley, 0.262, WeatherKind.Clear),
        "thunderstorm" => new(new PlanetSpec(33, Archetype.Temperate, 0, Modifier.Mesas), m => LookAtPeak(m, 900, 25, 2), 0.47,
            WeatherKind.Thunderstorm, new GroundCover { Wetness = 0.8f }, BeforeCapture: Strike),
        // Caught as the blizzard closes in: the wall of snow is still a few hundred metres off.
        "blizzard" => new(PlanetSpec.Roll(5, Archetype.Ice, 0), m => LookAtPeak(m, 140, 3, 6), 0.45,
            Prepare: m => m.Weather.SnapPartway(WeatherKind.Snow, WeatherKind.Blizzard, 0.45f, new GroundCover { Snow = 1f })),
        // An exotic-air crystal world: a POI fitted to a slope.
        "poi_hillside" => new(PlanetSpec.Roll(41, Archetype.Alien, 0), PoiView, 0.4, WeatherKind.Clouds),
        "cave" => new(PlanetSpec.Roll(9, Archetype.Temperate, 0), CaveView, CaveTime, WeatherKind.Clouds, Prepare: DigCave),
        "volcanic_dusk" => new(PlanetSpec.Roll(3, Archetype.Lava, 0), m => LookAtPeak(m, 1400, 120, 6), 0.735, WeatherKind.Clouds),
        _ when name.StartsWith("far:") => FarView(name),
        _ when name.StartsWith("infra:") => InfraViews.Get(name),
        _ => null,
    };

    /// <summary>Somewhere within a few kilometres of the landing site, the highest ground; the camera stands back from it.</summary>
    static (Vector3, float, float) LookAtPeak(Main m, float back, float up, float pitch)
    {
        var w = m.World;
        double bx = w.Landing.X, bz = w.Landing.Y, best = double.MinValue;
        for (double dz = -3000; dz <= 3000; dz += 120)
        for (double dx = -3000; dx <= 3000; dx += 120)
        {
            double x = w.Landing.X + dx, z = w.Landing.Y + dz;
            double h = w.Source.SurfaceHeight(x, z, 16) - Math.Sqrt(dx * dx + dz * dz) * 0.04;
            if (h > best) { best = h; bx = x; bz = z; }
        }
        // Stand back from the peak toward the landing site, on whatever ground is there.
        double ax = Mathx.WrapDelta(w.Landing.X - bx, w.Width), az = Mathx.WrapDelta(w.Landing.Y - bz, w.Height);
        double len = Math.Max(1, Math.Sqrt(ax * ax + az * az));
        double cx = bx + ax / len * back, cz = bz + az / len * back;
        float ground = Math.Max(w.GroundHeight(cx, cz), 0f);
        float heading = Mathf.RadToDeg(MathF.Atan2((float)(bx - cx), -(float)(bz - cz)));
        return (new Vector3((float)cx, ground + up, (float)cz), heading, pitch);
    }

    /// <summary>Along the deepest canyon near the landing site, from above its floor.</summary>
    static (Vector3, float, float) CanyonView(Main m)
    {
        var w = m.World;
        double bestDepth = 0, bx = w.Landing.X, bz = w.Landing.Y;
        float bestAxis = 0;
        for (double dz = -2500; dz <= 2500; dz += 60)
        for (double dx = -2500; dx <= 2500; dx += 60)
        {
            double x = w.Landing.X + dx, z = w.Landing.Y + dz;
            float h = w.Source.SurfaceHeight(x, z, 8);
            if (h < 3f) continue;
            for (int d = 0; d < 4; d++)
            {
                double a = d * Math.PI / 4;
                float left = w.Source.SurfaceHeight(x + Math.Cos(a) * 110, z + Math.Sin(a) * 110, 8);
                float right = w.Source.SurfaceHeight(x - Math.Cos(a) * 110, z - Math.Sin(a) * 110, 8);
                double depth = Math.Min(left, right) - h;
                if (depth > bestDepth) { bestDepth = depth; bx = x; bz = z; bestAxis = (float)(a + Math.PI / 2); }
            }
        }
        // Back off along the canyon so it runs away from the camera.
        double cx = bx - Math.Cos(bestAxis) * 220, cz = bz - Math.Sin(bestAxis) * 220;
        float floor = w.GroundHeight(cx, cz);
        float heading = Mathf.RadToDeg(MathF.Atan2(MathF.Cos(bestAxis), -MathF.Sin(bestAxis)));
        return (new Vector3((float)cx, Math.Max(floor, 0f) + (float)bestDepth * 0.55f, (float)cz), heading, -9);
    }

    /// <summary>From high ground near the landing site toward the system's giant, with mountains under it.</summary>
    static (Vector3, float, float) TowardGiant(Main m)
    {
        var w = m.World;
        PlanetSurface.Skies.SkyBody giant = w.Sky.Bodies.FirstOrDefault(b => b.Kind == PlanetSurface.Skies.BodyKind.GasGiant);
        float azimuth = giant?.Azimuth ?? 0f;
        var toward = new Vector2(MathF.Sin(azimuth), -MathF.Cos(azimuth));
        // Stand on the ground that gives the deepest view toward the giant: high here, mountains ahead.
        double best = double.MinValue, bx = w.Landing.X, bz = w.Landing.Y;
        for (double dz = -2500; dz <= 2500; dz += 100)
        for (double dx = -2500; dx <= 2500; dx += 100)
        {
            double x = w.Landing.X + dx, z = w.Landing.Y + dz;
            float h = w.Source.SurfaceHeight(x, z, 16);
            if (h < 5f) continue;
            float ahead = 0f, near = 0f;
            for (double r = 1200; r <= 3600; r += 400) ahead = Math.Max(ahead, w.Source.SurfaceHeight(x + toward.X * r, z + toward.Y * r, 32));
            for (double r = 60; r <= 600; r += 90) near = Math.Max(near, w.Source.SurfaceHeight(x + toward.X * r, z + toward.Y * r, 16));
            double score = ahead - Math.Max(0, near - h) * 3;
            if (score > best) { best = score; bx = x; bz = z; }
        }
        float ground = w.GroundHeight(bx, bz);
        float elevation = Mathf.RadToDeg(giant?.Inclination ?? 0.3f);
        return (new Vector3((float)bx, ground + 30f, (float)bz), Mathf.RadToDeg(azimuth), Math.Clamp(elevation * 0.55f, 0f, 22f));
    }

    /// <summary>
    /// The time of day that puts the sun farthest from the giant while it is still well up, so the
    /// camera facing the giant has the sun behind it: the giant shows its phase from the sun, and
    /// with the sun beyond it it is only a dark disc.
    /// </summary>
    static double SunBehindGiantView(Main m)
    {
        PlanetSurface.Skies.SkyBody giant = m.World.Sky.Bodies.FirstOrDefault(b => b.Kind == PlanetSurface.Skies.BodyKind.GasGiant);
        if (giant == null) return 0.36;
        var toGiant = new Vector3(MathF.Sin(giant.Azimuth) * MathF.Cos(giant.Inclination), MathF.Sin(giant.Inclination),
            -MathF.Cos(giant.Azimuth) * MathF.Cos(giant.Inclination));
        double best = 0.36, bestScore = double.MaxValue;
        for (double t = 0.27; t <= 0.73; t += 0.01)
        {
            Vector3 sun = m.Sky.SunAt(t);
            if (sun.Y < 0.3f) continue;
            float score = sun.Dot(toGiant);
            if (score < bestScore) { bestScore = score; best = t; }
        }
        return best;
    }

    /// <summary>High ground looking out over the lowest ground around it.</summary>
    static (Vector3, float, float) RidgeOverValley(Main m)
    {
        var w = m.World;
        double best = double.MinValue, bx = w.Landing.X, bz = w.Landing.Y;
        float bestHeading = 0;
        for (double dz = -2000; dz <= 2000; dz += 100)
        for (double dx = -2000; dx <= 2000; dx += 100)
        {
            double x = w.Landing.X + dx, z = w.Landing.Y + dz;
            float h = w.Source.SurfaceHeight(x, z, 16);
            if (h < 20f) continue;
            for (int d = 0; d < 8; d++)
            {
                double a = d * Math.PI / 4;
                float low = float.MaxValue;
                for (double r = 200; r <= 900; r += 175) low = Math.Min(low, Math.Max(0f, w.Source.SurfaceHeight(x + Math.Sin(a) * r, z - Math.Cos(a) * r, 16)));
                double score = (h - low) - Math.Abs(h - 160) * 0.3;
                if (score > best) { best = score; bx = x; bz = z; bestHeading = (float)a; }
            }
        }
        float ground = w.GroundHeight(bx, bz);
        return (new Vector3((float)bx, ground + 14f, (float)bz), Mathf.RadToDeg(bestHeading), -9);
    }


    /// <summary>
    /// far:FAMILY:ARCHETYPE[:VARIANT]:WHEN (day, dusk or night): one family seated near the landing
    /// site of a world of that kind, seen from about 400 m off at eye height, the way a traveller
    /// first sees it. The side is picked for a clear line of sight with the place against the sky.
    /// </summary>
    static Screenshot.ViewSpec FarView(string name)
    {
        string[] a = name.Split(':');
        if (a.Length < 4) return null;
        var arch = Enum.Parse<Archetype>(a[2], true);
        int variant = a.Length >= 5 ? int.Parse(a[3]) : 0;
        string when = a[^1];
        double time = when switch { "dusk" => 0.738, "night" => 0.9, _ => 0.4 };
        string family = a[1];
        return new(PlanetSpec.Roll(61, arch, variant), m => FarPlace(m, family), time, WeatherKind.Clear, Force: family);
    }

    static (Vector3, float, float) FarPlace(Main m, string family)
    {
        var w = m.World;
        var site = w.Pois.FirstOrDefault(p => p.Family.Id == family) ?? w.Pois.FirstOrDefault();
        if (site == null) return LookAtPeak(m, 600, 20, 0);
        var (cx, cz) = site.Center;
        float baseY = site.Origin.Y, top = baseY + Math.Max(4f, site.Family.Rise * 0.7f);
        bool Wooded(double x, double z)
        {
            var b = (Biome)w.Map.Biome[w.Map.CellAt(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height))];
            return b is Biome.Forest or Biome.PineForest or Biome.Jungle or Biome.Rainforest or Biome.CloudForest or Biome.Swamp or Biome.FungalForest;
        }
        double bestScore = double.MinValue, bestDist = 400;
        Vector3 bestPos = default;
        foreach (double dist in new[] { 280.0, 330.0, 390.0 })
        for (int k = 0; k < 24; k++)
        {
            double ang = k * Math.PI / 12;
            double x = cx + Math.Cos(ang) * dist, z = cz + Math.Sin(ang) * dist;
            float ground = w.GroundHeight(x, z);
            if (ground < 1f) continue; // not from the water
            float eye = ground + 2.5f;
            // Not from a peak looking down on it: a traveller sees it across the land.
            if (eye > baseY + Math.Max(30f, site.Family.Rise)) continue;
            // The whole of it in sight, not just its top over a ridge: nothing in the way of its lower walls.
            double blocked = 0;
            float low = baseY + 3f;
            for (int s = 1; s < 60; s++)
            {
                double f = s / 60.0 * 0.88, sx = x + (cx - x) * f, sz = z + (cz - z) * f;
                blocked = Math.Max(blocked, w.GroundHeight(sx, sz) - (eye + (top - eye) * f));
                blocked = Math.Max(blocked, 0.5 * (w.GroundHeight(sx, sz) - (eye + (low - eye) * f)));
            }
            // Trees close to the eye hide everything: keep the near half of the line out of woods.
            int woods = 0;
            for (int s = 0; s < 20; s++) if (Wooded(x + (cx - x) * s / 40.0, z + (cz - z) * s / 40.0)) woods++;
            // Eye level a little under the landmark's middle, so it stands against the sky.
            double score = -Math.Max(0, blocked) * 10 - woods * 3 - Math.Abs(dist - 320) * 0.02 - Math.Abs(eye - (baseY + 6f)) * 0.08;
            if (score > bestScore) { bestScore = score; bestPos = new Vector3((float)x, eye, (float)z); bestDist = dist; }
        }
        if (bestScore == double.MinValue) return LookAtPeak(m, 600, 20, 0);
        float heading = Mathf.RadToDeg(MathF.Atan2((float)(cx - bestPos.X), -(float)(cz - bestPos.Z)));
        float pitch = Mathf.RadToDeg(MathF.Atan2(baseY + site.Family.Rise * 0.4f - bestPos.Y, (float)bestDist));
        GD.Print($"far view of {site.Name} ({site.Siting}) from {bestPos.X:F0}, {bestPos.Y:F0}, {bestPos.Z:F0} ({bestDist:F0} m); place at {cx:F0}, {site.Origin.Y}, {cz:F0}");
        return (bestPos, heading, Mathf.Clamp(pitch, -6f, 8f));
    }

    /// <summary>The first POI fitted to a slope (or any POI), from a little way off and above.</summary>
    static (Vector3, float, float) PoiView(Main m)
    {
        var w = m.World;
        var pois = w.Pois.OrderBy(p => p.Siting is "hillside" or "ridge" ? 0 : 1).ThenByDescending(p => p.CutFill).ToList();
        if (pois.Count == 0) return LookAtPeak(m, 600, 20, 0);
        var site = pois[0];
        double cx = (site.Stamp.MinX + site.Stamp.MaxX) * 0.5, cz = (site.Stamp.MinZ + site.Stamp.MaxZ) * 0.5;
        double size = Math.Max(site.Stamp.MaxX - site.Stamp.MinX, site.Stamp.MaxZ - site.Stamp.MinZ);
        // Pick the side with the lowest ground, so the cut into the slope faces the camera.
        double bestX = cx, bestZ = cz;
        float low = float.MaxValue;
        for (int a = 0; a < 8; a++)
        {
            double x = cx + Math.Cos(a * Math.PI / 4) * (size + 40), z = cz + Math.Sin(a * Math.PI / 4) * (size + 40);
            float h = w.GroundHeight(x, z);
            if (h > 1f && h < low) { low = h; bestX = x; bestZ = z; }
        }
        float eye = Math.Max(low + 6f, site.Origin.Y + 10f);
        float heading = Mathf.RadToDeg(MathF.Atan2((float)(cx - bestX), -(float)(cz - bestZ)));
        float pitch = -Mathf.RadToDeg(MathF.Atan2(eye - site.Origin.Y - 4f, (float)(size + 40)));
        return (new Vector3((float)bestX, eye, (float)bestZ), heading, pitch);
    }

    const double CaveTime = 0.4;
    static Vector3 _caveMouth;
    static float _caveHeading;

    /// <summary>
    /// Digs a tunnel into the foot of a steep rock face near the landing site, with the survival
    /// tool's own digs: flat ground in front to stand on, the face rising over the mouth.
    /// </summary>
    static void DigCave(Main m)
    {
        var w = m.World;
        float Slope(double x, double z, out float gx, out float gz)
        {
            gx = (w.GroundHeight(x + 2, z) - w.GroundHeight(x - 2, z)) / 4f;
            gz = (w.GroundHeight(x, z + 2) - w.GroundHeight(x, z - 2)) / 4f;
            return MathF.Sqrt(gx * gx + gz * gz);
        }

        double bestX = w.Landing.X, bestZ = w.Landing.Y;
        float best = float.MinValue, bestUx = 1, bestUz = 0;
        // A face the sun lights at the picture's time, so the mouth is a dark hole in bright rock.
        Vector3 sun = m.Sky.SunAt(CaveTime);
        var sunFlat = new Vector2(sun.X, sun.Z).Normalized();
        for (double dz = -500; dz <= 500; dz += 8)
        for (double dx = -500; dx <= 500; dx += 8)
        {
            double x = w.Landing.X + dx, z = w.Landing.Y + dz;
            if (w.GroundHeight(x, z) < 3f || w.Source.StampWeight(x, z) > 0f) continue;
            float slope = Slope(x, z, out float gx, out float gz);
            if (slope < 1.3f) continue; // not a face
            float ux = gx / slope, uz = gz / slope;
            // Walk downhill to the face's foot.
            double fx = x, fz = z;
            for (int k = 0; k < 30 && Slope(fx, fz, out _, out _) >= 0.45f; k++)
            {
                fx -= ux;
                fz -= uz;
            }
            float foot = w.GroundHeight(fx, fz);
            if (foot < 2f || w.Source.StampWeight(fx, fz) > 0f || Slope(fx, fz, out _, out _) >= 0.45f) continue;
            float rise = w.GroundHeight(fx + ux * 14, fz + uz * 14) - foot;              // rock over the tunnel
            float apron = MathF.Abs(w.GroundHeight(fx - ux * 12, fz - uz * 12) - foot);  // standing room in front
            if (apron > 3f) continue;
            float lit = -(ux * sunFlat.X + uz * sunFlat.Y); // the face looks back down the slope, toward -uphill
            float score = MathF.Min(rise, 18f) - apron * 2f + lit * 8f;
            if (score <= best) continue;
            best = score;
            bestX = fx;
            bestZ = fz;
            bestUx = ux;
            bestUz = uz;
        }

        var into = new Vector3(bestUx, 0, bestUz).Normalized();
        var side = new Vector3(-into.Z, 0, into.X);
        var start = new Vector3((float)bestX, w.GroundHeight(bestX, bestZ), (float)bestZ);
        // A tunnel about 6 m wide and 5 m high, 18 m into the face, dug a tool-sized ball at a time.
        for (float d = -1f; d <= 18f; d += 1.2f)
        for (int across = -2; across <= 2; across++)
        for (int up = 0; up < 4; up++)
        {
            if (up == 3 && Math.Abs(across) == 2) continue; // round the top corners into an arch
            Vector3 p = start + into * d + side * (across * 1.2f) + Vector3.Up * (0.6f + up * 1.2f);
            m.Dig(new TerrainDigging.Terrain.Int3((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z)), 1.42f);
        }
        _caveMouth = start + Vector3.Up * 1.8f;
        _caveHeading = Mathf.RadToDeg(MathF.Atan2(into.X, -into.Z));
    }

    static (Vector3, float, float) CaveView(Main m)
    {
        // Standing on the ground in front of the face, a little to one side, at eye height.
        float h = Mathf.DegToRad(_caveHeading);
        var into = new Vector3(MathF.Sin(h), 0, -MathF.Cos(h));
        var side = new Vector3(-into.Z, 0, into.X);
        Vector3 back = _caveMouth - into * 19f + side * 4f;
        back.Y = Math.Max(m.World.GroundHeight(back.X, back.Z) + 1.7f, _caveMouth.Y - 0.3f);
        Vector3 look = _caveMouth + Vector3.Up * 3f - back;
        float heading = Mathf.RadToDeg(MathF.Atan2(look.X, -look.Z));
        float pitch = Mathf.RadToDeg(MathF.Atan2(look.Y, new Vector2(look.X, look.Z).Length()));
        return (back, heading, pitch);
    }

    /// <summary>A lightning bolt into the scene just before the picture.</summary>
    static void Strike(Main m)
    {
        Vector3 cam = m.Camera.GlobalPosition, fwd = -m.Camera.GlobalBasis.Z;
        var target = cam + new Vector3(fwd.X, 0, fwd.Z).Normalized() * 420f + new Vector3(80, 0, 0);
        target.Y = m.World.GroundHeight(target.X, target.Z);
        m.Weather.DebugStrike(target);
    }
}
