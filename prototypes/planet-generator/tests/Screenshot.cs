using System;
using System.Globalization;
using Godot;
using Planets.Gen;

namespace Planets;

/// <summary>
/// Renders one view of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/planet-generator res://tests/Screenshot.tscn -- --view=orbit --archetype=2 --variant=1 --seed=7 --out=shot.png
/// Views: orbit, flat, surface (at the landing site), pole (on the north-south seam, looking north across it).
/// Optional: --mods=Mesas,Cratered (default: rolled from the seed), --heading=deg --pitch=deg --up=metres
/// to aim the surface camera, --x=metres --z=metres to put it somewhere else, --hud=0 to hide the
/// orbit text. Prints the average frame time with vsync off, and primitives and draw calls.
/// </summary>
public partial class Screenshot : Node
{
    const int SettleFrames = 90;
    const double TimeoutSeconds = 120;

    Main _main;
    string _view = "orbit", _out = "shot.png";
    int _archetype, _variant, _frame, _settled, _stage;
    ulong _seed = 1977;
    Modifier? _mods;
    float? _heading, _pitch, _up;
    double? _x, _z;
    double _frameTime, _worstFrame, _elapsed;
    bool _hud = true, _landforms;
    Landform? _at, _face;
    float _from = -1f;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            if (kv.Length != 2) continue;
            string v = kv[1];
            switch (kv[0])
            {
                case "view": _view = v; break;
                case "out": _out = v; break;
                case "archetype": _archetype = int.Parse(v); break;
                case "variant": _variant = int.Parse(v); break;
                case "seed": _seed = ulong.Parse(v); break;
                case "mods": _mods = v == "none" ? Modifier.None : Enum.Parse<Modifier>(v); break;
                case "heading": _heading = float.Parse(v, CultureInfo.InvariantCulture); break;
                case "pitch": _pitch = float.Parse(v, CultureInfo.InvariantCulture); break;
                case "up": _up = float.Parse(v, CultureInfo.InvariantCulture); break;
                case "x": _x = double.Parse(v, CultureInfo.InvariantCulture); break;
                case "z": _z = double.Parse(v, CultureInfo.InvariantCulture); break;
                case "hud": _hud = v != "0"; break;
                case "landforms": _landforms = v != "0"; break;
                case "landform": _at = Enum.Parse<Landform>(v); break;
                case "face": _face = Enum.Parse<Landform>(v); break;
                case "from": _from = float.Parse(v, CultureInfo.InvariantCulture); break;
            }
        }
        // Measure the scene, not the monitor: no vsync while timing frames.
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        _main = new Main();
        AddChild(_main);
        if (!_hud) _main.HideLabel();
        var spec = PlanetSpec.Roll(_seed, (Archetype)_archetype, _variant);
        if (_mods is Modifier mods) spec = spec with { Modifiers = mods };
        _main.SetSpec(spec);
    }

    public override void _Process(double delta)
    {
        _frame++;
        if ((_elapsed += delta) > TimeoutSeconds)
        {
            GD.PrintErr("timed out waiting for the view to settle");
            GetTree().Quit(1);
            return;
        }
        if (_main.Busy) return;

        switch (_stage)
        {
            case 0: // planet ready
                if (_landforms) _main.ShowLandforms = true;
                if (_view is "orbit") _main.Orbit.Face(_main.Map, _x ?? LandiestLongitude(_main.Map));
                if (_view == "flat") Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Tab, Pressed = true });
                if (_view is "surface" or "pole") _main.Land();
                _stage = 1;
                break;
            case 1: // aim the camera
                if (_main.Surface != null)
                {
                    if (!_hud) _main.Surface.HudVisible = false;
                    var site = _main.Surface.Landing;
                    double x = _x ?? site.X, z = _z ?? site.Y;
                    float heading = _heading.HasValue ? Mathf.DegToRad(_heading.Value) : _main.Surface.Camera.Heading;
                    if (_at is Landform at)
                    {
                        (x, z, float h) = Spots.Find(_main.Map, at, _face, _from);
                        if (!_heading.HasValue) heading = h;
                        GD.Print($"camera in {LandformInfo.Name(at)} at {x:F0}, {z:F0}, heading {Mathf.RadToDeg(heading):F0} deg ({_main.Map.LandformsAt(x, z)})");
                    }
                    if (_view == "pole")
                    {
                        z = _z ?? 40; // just south of the north edge, looking north across the seam
                        heading = _heading.HasValue ? heading : 0f;
                    }
                    bool aimed = _view == "pole" || _heading.HasValue || _x.HasValue || _up.HasValue || _pitch.HasValue || _at.HasValue;
                    if (aimed)
                        _main.Surface.PlaceCamera(x, z, _up ?? (_view == "pole" ? 60f : 16f), heading,
                            Mathf.DegToRad(_pitch ?? (_view == "pole" ? -4f : -9f)));
                }
                _stage = 2;
                break;
            case 2: // wait for terrain to stream in, then a few more frames for temporal effects
                bool settled = _main.Surface == null || _main.Surface.Streamer.Settled;
                _settled = settled ? _settled + 1 : 0;
                if (_settled > SettleFrames / 3)
                {
                    _frameTime += delta;
                    _worstFrame = Math.Max(_worstFrame, delta);
                }
                if (_settled >= SettleFrames)
                {
                    double frames = SettleFrames - SettleFrames / 3;
                    GD.Print($"frame time {_frameTime / frames * 1000:F2} ms average ({frames / _frameTime:F0} fps), worst {_worstFrame * 1000:F1} ms, vsync off; " +
                        $"{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame) / 1000} k primitives, " +
                        $"{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} draw calls");
                    _stage = 3;
                    Capture();
                }
                break;
        }
    }

    /// <summary>The map x with the most land within a quarter of the planet either side: where a globe shot looks.</summary>
    static double LandiestLongitude(WorldMap map)
    {
        var land = new int[map.Width];
        for (int k = 0; k < map.Count; k++)
        {
            int j = k / map.Width;
            if (!map.Has(k, CellFlags.Water) && j > map.Height / 6 && j < map.Height * 5 / 6) land[k % map.Width]++;
        }
        int best = 0, bestSum = -1, half = map.Width / 5;
        for (int i = 0; i < map.Width; i += 4)
        {
            int sum = 0;
            for (int d = -half; d <= half; d++) sum += land[map.WrapI(i + d)];
            if (sum > bestSum) { bestSum = sum; best = i; }
        }
        return map.CellX(best);
    }

    async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
