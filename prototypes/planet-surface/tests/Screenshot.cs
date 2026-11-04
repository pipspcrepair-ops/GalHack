using System;
using System.Collections.Generic;
using System.Globalization;
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
        WeatherKind? Weather = null, GroundCover? Ground = null, Action<Main> Prepare = null);

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
        return new ViewSpec(planet, m =>
        {
            double x = Num("x", m.World.Landing.X), z = Num("z", m.World.Landing.Y);
            float ground = m.World.GroundHeight(x, z);
            return (new Vector3((float)x, ground + (float)Num("up", 20), (float)z), (float)Num("heading", 0), (float)Num("pitch", -8));
        }, Num("time", 0.42), weather);
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
                var (pos, heading, pitch) = _spec.Place(_main);
                _main.Fly.Begin(pos, -Mathf.DegToRad(heading), Mathf.DegToRad(pitch));
                _main.Player.ProcessMode = ProcessModeEnum.Disabled;
                _main.Sky.TimeOfDay = _spec.Time;
                _main.Sky.Paused = true;
                if (_spec.Weather is WeatherKind kind) _main.Weather.Snap(kind, _spec.Ground);
                if (!_args.TryGetValue("hud", out string hud) || hud != "1") _main.Hud.HideAll();
                _stage = 1;
                break;
            case 1:
                _main.Streamer.SetFocus(_main.Camera.GlobalPosition);
                bool settled = _main.Streamer.Settled && _main.Pois.Built;
                _settled = settled ? _settled + 1 : 0;
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
        Error result = image.SavePng(_out);
        Vector3 p = _main.Camera.GlobalPosition;
        GD.Print($"{_main.World.Spec} · {_main.World.Sky.Summary} · camera {p.X:F0}, {p.Y:F0}, {p.Z:F0} · " +
                 $"{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame) / 1000} k primitives, " +
                 $"{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} draw calls");
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}

/// <summary>The named views committed as screenshots.</summary>
public static class Views
{
    public static Screenshot.ViewSpec Get(string name) => name switch
    {
        _ => null,
    };
}
