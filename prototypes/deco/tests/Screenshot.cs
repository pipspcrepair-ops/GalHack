using System;
using System.Threading.Tasks;
using Godot;

namespace DecoShowroom.Tests;

/// <summary>
/// Renders one view of the showroom to a PNG and quits. Run windowed (not headless), off-screen:
///   godot --position 20000,20000 --path prototypes/deco res://tests/Screenshot.tscn -- --view=set:office --out=shot.png
/// Views: set:&lt;id&gt;[:&lt;part&gt;/&lt;parts&gt;] (a set's strip: every piece in every state),
/// front:&lt;id&gt;:&lt;part&gt;/&lt;parts&gt; (intact pieces at eye level), piece:&lt;id&gt; (one piece in all its
/// states), room:&lt;demo&gt; (a demo room or site as used and as found), close:&lt;demo&gt;:before|after,
/// ores (the ore surface tells). Add --state=ruined (or active, infested) for the rooms.
/// </summary>
public partial class Screenshot : Node
{
    string _view = "set:office", _out = "shot.png", _state = "abandoned";
    Showroom _room;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
            else if (arg.StartsWith("--state=")) _state = arg["--state=".Length..];
        }
        _room = new Showroom();
        string[] v = _view.Split(':');
        // Only build what the view needs, so a shot is quick.
        _room.BuildGallery = v[0] is "set" or "front" or "piece";
        _room.BuildRooms = v[0] is "room" or "close";
        _room.BuildOres = v[0] == "ores";
        if (v[0] is "set" or "front") _room.OnlySet = v[1];
        if (v[0] == "piece") { _room.OnlySet = DataPaths.Deco.Piece(v[1]).Set; _room.OnlyPiece = v[1]; }
        if (v[0] is "room" or "close") _room.OnlyRoom = v[1];
        AddChild(_room);
        if (_room.Rooms != null && _room.Rooms.State != _state) { _room.Rooms.State = _state; _room.Rooms.Build(); }
        Run(v);
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async void Run(string[] v)
    {
        try { await Shoot(v); }
        catch (Exception e)
        {
            GD.PrintErr($"FAIL screenshot {_view}: {e}");
            GetTree().Quit(1);
        }
    }

    async Task Shoot(string[] v)
    {
        await Frames(4);
        _room.Hud.Visible = false;
        _room.Camera.Enabled = false;
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
            case "ores":
            {
                var (from, to) = _room.OreView();
                _room.Camera.Place(from, to);
                break;
            }
        }
        await Frames(45);
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
