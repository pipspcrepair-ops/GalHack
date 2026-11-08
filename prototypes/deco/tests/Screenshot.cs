using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Deco;
using GalHack.Items;
using Godot;

namespace DecoShowroom.Tests;

/// <summary>
/// Renders one view of the showroom to a PNG and quits. Run windowed (not headless), off-screen:
///   godot --path prototypes/deco res://tests/Screenshot.tscn -- --view=set:office --out=shot.png
/// Views: set:&lt;id&gt;[:&lt;part&gt;/&lt;parts&gt;] (a set's strip: every piece in every state),
/// front:&lt;id&gt;:&lt;part&gt;/&lt;parts&gt; (intact pieces at eye level), piece:&lt;id&gt; (one piece in all its
/// states), near:&lt;piece&gt;:&lt;row&gt;:&lt;metres&gt;[:&lt;up&gt;[:&lt;degrees&gt;[:&lt;aim&gt;[:&lt;x&gt;/&lt;z&gt;]]]] (one piece in one row of its
/// strip, close: from the front, raised, swung round, aimed at a share of its height),
/// room:&lt;demo&gt; (a demo room or site as used and as found), close:&lt;demo&gt;:before|after,
/// inside:&lt;demo&gt;:before|after (from a corner at eye height), ores (the ore surface tells).
/// Add --state=ruined (or active, infested) for the rooms, --sun=pitch,yaw for a light of the view's own,
/// --perf=1 for what a frame costs on each view. Several shots in one run:
/// --shots="view&gt;file;view&gt;file", or --shotfile=a file of view&gt;file lines (no piece: views among them). The rooms roll a fresh seed each run.
/// </summary>
public partial class Screenshot : Node
{
    string _view = "set:office", _out = "shot.png", _state = "abandoned";
    readonly List<(string View, string File)> _shots = new();
    Showroom _room;
    Vector2? _sun;
    bool _perf;

    public override void _Ready()
    {
        // A wall-clock limit: a hung shot quits itself.
        var watchdog = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(240));
            Console.Error.WriteLine($"screenshot '{_view}' took over 4 minutes of wall-clock time: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        watchdog.Start();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
            else if (arg.StartsWith("--state=")) _state = arg["--state=".Length..];
            else if (arg.StartsWith("--sun=")) { string[] s = arg["--sun=".Length..].Split(','); _sun = new Vector2(float.Parse(s[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(s[1], System.Globalization.CultureInfo.InvariantCulture)); }
            else if (arg == "--perf=1") _perf = true;
            else if (arg.StartsWith("--shots=") || arg.StartsWith("--shotfile="))
                foreach (string s in arg.StartsWith("--shots=") ? arg["--shots=".Length..].Split(';', StringSplitOptions.RemoveEmptyEntries) : System.IO.File.ReadAllLines(arg["--shotfile=".Length..]).Where(l => l.Contains('>')))
                {
                    int k = s.IndexOf('>');
                    _shots.Add((s[..k], s[(k + 1)..]));
                }
        }
        if (_shots.Count == 0) _shots.Add((_view, _out));
        _room = new Showroom { RoomSeed = (ulong)Random.Shared.NextInt64(1, long.MaxValue), Sun = _sun };
        var views = _shots.Select(s => s.View.Split(':')).ToList();
        // Only build what the views need, so a shot is quick.
        _room.BuildGallery = views.Any(v => v[0] is "set" or "front" or "piece" or "near");
        _room.BuildRooms = views.Any(v => v[0] is "room" or "close" or "inside");
        _room.BuildOres = views.Any(v => v[0] == "ores");
        _room.OnlySets = views.Where(v => v[0] is "set" or "front" or "near" or "piece").Select(v => v[0] is "near" or "piece" ? DataPaths.Deco.Piece(v[1]).Set : v[1]).ToHashSet();
        if (views.Count == 1 && views[0][0] == "piece") _room.OnlyPiece = views[0][1];
        var rooms = views.Where(v => v[0] is "room" or "close" or "inside").Select(v => v[1]).Distinct().ToList();
        if (rooms.Count == 1) _room.OnlyRoom = rooms[0];
        AddChild(_room);
        GD.Print($"rooms seed {_room.RoomSeed}");
        if (_room.Rooms != null && _room.Rooms.State != _state) { _room.Rooms.State = _state; _room.Rooms.Build(); }
        Run();
    }

    /// <summary>What a frame costs on this view, vsync off: frame time, the renderer's CPU and GPU time, averaged over 240 frames.</summary>
    async Task Measure(string view)
    {
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        Rid vp = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(vp, true);
        await Frames(30);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double cpu = 0, gpu = 0;
        const int N = 240;
        for (int i = 0; i < N; i++)
        {
            await Frames(1);
            cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp);
            gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
        }
        GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"DECOPERF {view}: frame {clock.Elapsed.TotalMilliseconds / N:F2} ms, render cpu {cpu / N:F2} ms, gpu {gpu / N:F2} ms, draws {GetViewport().GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame)}"));
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async void Run()
    {
        try
        {
            await Frames(4);
            _room.Hud.Visible = false;
            _room.Camera.Enabled = false;
            bool ok = true;
            for (int i = 0; i < _shots.Count; i++)
            {
                _view = _shots[i].View;
                Place(_view.Split(':'));
                await Frames(i == 0 ? 45 : 20);
                if (_perf) await Measure(_view);
                Image image = GetViewport().GetTexture().GetImage();
                Error result = image.SavePng(_shots[i].File);
                GD.Print(result == Error.Ok ? $"saved {_shots[i].File}" : $"could not save {_shots[i].File}: {result}");
                ok &= result == Error.Ok;
            }
            GetTree().Quit(ok ? 0 : 1);
        }
        catch (Exception e)
        {
            GD.PrintErr($"FAIL screenshot {_view}: {e}");
            GetTree().Quit(1);
        }
    }

    void Place(string[] v)
    {
        switch (v[0])
        {
            case "set":
            {
                int part = 0, parts = 1;
                if (v.Length > 2) { string[] pp = v[2].Split('/'); part = int.Parse(pp[0]); parts = int.Parse(pp[1]); }
                var (from, to) = _room.SetView(v[1], part, parts);
                _room.Camera.Place(from, to);
                break;
            }
            case "front":
            {
                var strip = _room.Strips[v[1]];
                int part = 0, parts = 1;
                if (v.Length > 2) { string[] pp = v[2].Split('/'); part = int.Parse(pp[0]); parts = int.Parse(pp[1]); }
                float span = (strip.Width - 2.2f) / parts, cx = -(2.2f + span * (part + 0.5f));
                bool big = strip.Pitch > 6;
                var target = strip.Origin + new Vector3(cx, big ? 1.5f : 0.7f, big ? 2f : 0.6f);
                float d = Math.Max(3.2f, span * 0.5f);
                _room.Camera.Place(target + new Vector3(0, 1.0f + d * 0.6f, -d * 0.9f), target);
                break;
            }
            case "piece":
            {
                Vector3 c = _room.Columns[v[1]];
                var strip = _room.Strips[DataPaths.Deco.Piece(v[1]).Set];
                var p = DataPaths.Deco.Piece(v[1]);
                var target = c + new Vector3(0, p.Height * 0.25f, strip.Depth / 2 - strip.Pitch / 2);
                float d = MathF.Max(strip.Width * 0.36f, strip.Depth * 0.9f) + 0.5f;
                _room.Camera.Place(target + new Vector3(0, d * 0.55f, -d), target);
                break;
            }
            case "near":
            {
                DecoPiece p = DataPaths.Deco.Piece(v[1]);
                var strip = _room.Strips[p.Set];
                int row = v.Length > 2 ? int.Parse(v[2]) : 0;
                float dist = v.Length > 3 ? float.Parse(v[3], System.Globalization.CultureInfo.InvariantCulture) : 1.5f;
                float up = v.Length > 4 ? float.Parse(v[4], System.Globalization.CultureInfo.InvariantCulture) : 0.4f;
                float yaw = v.Length > 5 ? float.Parse(v[5], System.Globalization.CultureInfo.InvariantCulture) : 25f;
                float aim = v.Length > 6 ? float.Parse(v[6], System.Globalization.CultureInfo.InvariantCulture) : 0.5f;
                bool lifted = p.Mount is DecoMount.Wall or DecoMount.Ceiling;
                float y0 = !lifted ? 0 : p.Mount == DecoMount.Wall ? p.MountY : 2.6f - p.Height;
                if (p.Mount == DecoMount.Top) y0 = 0.75f;
                // An offset across and along the piece (a wheel, a corner), as x/z.
                Vector3 off = Vector3.Zero;
                if (v.Length > 7) { string[] o = v[7].Split('/'); off = new Vector3(float.Parse(o[0], System.Globalization.CultureInfo.InvariantCulture), 0, float.Parse(o[1], System.Globalization.CultureInfo.InvariantCulture)); }
                var target = _room.Columns[v[1]] + off + new Vector3(0, y0 + p.Height * aim, row * strip.Pitch);
                float a = Mathf.DegToRad(yaw);
                var from = target + new Vector3(MathF.Sin(a) * dist, up, -MathF.Cos(a) * dist);
                _room.Camera.Place(from, target);
                break;
            }
            case "room":
            {
                var (from, to) = _room.Rooms.View(v[1]);
                _room.Camera.Place(from, to);
                break;
            }
            case "close":
            {
                var (from, to) = _room.Rooms.Close(v[1], v.Length > 2 && v[2] == "after");
                _room.Camera.Place(from, to);
                break;
            }
            case "inside":
            {
                var (from, to) = _room.Rooms.Inside(v[1], v.Length > 2 && v[2] == "after");
                _room.Camera.Place(from, to);
                break;
            }
            case "ores":
            {
                var (from, to) = _room.OreView();
                _room.Camera.Place(from, to);
                break;
            }
        }
    }
}
