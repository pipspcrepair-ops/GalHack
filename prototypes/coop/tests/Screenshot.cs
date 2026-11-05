using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using GalHack.Net;
using Godot;

namespace Coop;

/// <summary>
/// Renders the README screenshots. Run windowed (not headless):
///   godot --path prototypes/coop res://tests/Screenshot.tscn -- --view=coop --out=DIR
///   godot --path prototypes/coop res://tests/Screenshot.tscn -- --view=title --out=DIR
///
/// "coop" is a real session: this window is a client ("Kit") that joins a headless host
/// ("Pip") and a second headless client ("Ana") over ENet loopback. The host stages the shots
/// and Kit captures what it really sees: aboard.png and moon.png. "title" saves title.png.
/// </summary>
public partial class Screenshot : Node
{
    string _view = "coop";
    string _out = ".";
    readonly List<Process> _children = new();
    double _started;

    public override void _Ready()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        SteamworksApi.Forbidden = true;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        _out = System.IO.Path.GetFullPath(_out);
        _started = NetClock.RealNow;

        if (_view == "title")
        {
            AddChild(new TitleScreen { Name = "Title" });
            CaptureTitle();
            return;
        }

        int port = 33000 + Process.GetCurrentProcess().Id % 5000;
        Launch("--role=host", "--name=Pip", "--test=shot", $"--port={port}");
        var timer = GetTree().CreateTimer(2.5);
        timer.Timeout += () =>
        {
            Launch("--role=client", "--name=Ana", $"--port={port}", "--sim=60,5,0");
            JoinAsKit(port);
        };
    }

    void Launch(params string[] args)
    {
        var info = new ProcessStartInfo(OS.GetExecutablePath()) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string a in new[] { "--headless", "--path", ProjectSettings.GlobalizePath("res://"), "res://tests/NetTest.tscn", "--" })
            info.ArgumentList.Add(a);
        foreach (string a in args) info.ArgumentList.Add(a);
        _children.Add(Process.Start(info));
    }

    void JoinAsKit(int port)
    {
        var game = CoopGame.Create(new CoopGame.Options
        {
            Name = "Kit",
            Transport = new EnetTransport { Port = port },
            Address = $"127.0.0.1:{port}",
            Conditions = NetConditions.Parse("60,5,0"),
        });
        game.AddChild(new TestAgent { Name = "TestAgent", Game = game, TestName = "Kit", CaptureDir = _out });
        game.Ended += (_, _) => Done(0);
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, game);
    }

    async void CaptureTitle()
    {
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = System.IO.Path.Combine(_out, "title.png");
        Error result = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(result == Error.Ok ? $"saved {path}" : $"could not save {path}: {result}");
        Done(result == Error.Ok ? 0 : 1);
    }

    public override void _Process(double delta)
    {
        if (NetClock.RealNow - _started > 120) Done(1); // never hang
    }

    void Done(int code)
    {
        foreach (Process child in _children)
            if (!child.HasExited) child.Kill(true);
        GetTree().Quit(code);
    }
}
