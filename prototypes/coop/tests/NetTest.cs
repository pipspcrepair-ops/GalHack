using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Net;
using Godot;

namespace Coop;

/// <summary>
/// The networking tests. Run headless:
///   godot --headless --path prototypes/coop res://tests/NetTest.tscn
///
/// With no arguments this is the runner. It first runs the in-process unit tests (the Steam
/// peer against a fake socket layer, Godot's high-level multiplayer over that peer, the
/// network simulator, the interpolation buffer). Then it launches real Godot processes talking
/// ENet over loopback, with the clients on a simulated bad network:
///   1. a listen host and 3 clients (4 players)
///   2. a headless dedicated server and 2 clients
/// The host or server directs each scenario and checks the results; see TestAgent.
///
/// With --role=host|client|dedicated it is one of those processes.
/// Exits 0 if everything passed, 1 if anything failed. Steam is never touched.
/// </summary>
public partial class NetTest : Node
{
    const string Sim = "80,10,1.5"; // 80 ms round trip, 10 ms jitter, 1.5% loss
    const double ScenarioTimeout = 480; // real seconds: only for a stuck scenario (the agents' watchdog is 420)

    int _failures;
    readonly ConcurrentQueue<string> _output = new();

    public override void _Ready()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        SteamworksApi.Forbidden = true; // automated tests never touch Steam
        Engine.MaxFps = 60;

        Args args = Args.FromCommandLine();
        if (args.Has("role")) StartRole(args);
        else _ = RunAll();
    }

    // ---- One process in a scenario ----------------------------------------------------------------

    void StartRole(Args args)
    {
        string role = args.Get("role");
        string name = args.Get("name", role);
        int port = args.GetInt("port", EnetTransport.DefaultPort);
        var options = new CoopGame.Options
        {
            Name = name,
            Host = role == "host",
            Dedicated = role == "dedicated",
            Transport = new EnetTransport { Port = port, BindAddress = "127.0.0.1" },
            Address = $"127.0.0.1:{port}",
            Conditions = args.Has("sim") ? NetConditions.Parse(args.Get("sim")) : null,
        };
        CoopGame game = CoopGame.Create(options);
        game.AddChild(new TestAgent
        {
            Name = "TestAgent",
            Game = game,
            TestName = name,
            Scenario = args.Get("test", ""),
            SimDescription = NetConditions.Parse(Sim).ToString(),
        });
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, game);
    }

    // ---- The runner ----------------------------------------------------------------------------------

    async Task RunAll()
    {
        try
        {
            await Frames(1); // the scene tree is still being set up during _Ready
            GD.Print("== Unit tests (in process, no Steam) ==");
            _failures += UnitTests.Run();
            _failures += ConditionerTest.Run();
            var fake = new FakeSteamNetwork();
            _failures += await HighLevelSteamTest.Run(this, "fake socket layer",
                () => SteamMultiplayerPeer.CreateHost(fake.AddEndpoint(500)),
                () => SteamMultiplayerPeer.CreateClient(fake.AddEndpoint(501), 500));

            if (Args.FromCommandLine().Has("unit-only")) throw new OperationCanceledException("unit tests only");
            int port = 30000 + (int)(Process.GetCurrentProcess().Id % 9000);
            GD.Print($"\n== Scenario 1: listen host + 3 clients over ENet loopback, clients on {NetConditions.Parse(Sim)} ==");
            await RunScenario(new[]
            {
                ("host", new[] { "--role=host", "--name=Host", "--test=listen", $"--port={port}" }),
                ("A", new[] { "--role=client", "--name=A", $"--port={port}", $"--sim={Sim}" }),
                ("B", new[] { "--role=client", "--name=B", $"--port={port}", $"--sim={Sim}" }),
                ("C", new[] { "--role=client", "--name=C", $"--port={port}", $"--sim={Sim}" }),
            });

            GD.Print($"\n== Scenario 2: headless dedicated server + 2 clients, clients on {NetConditions.Parse(Sim)} ==");
            await RunScenario(new[]
            {
                ("server", new[] { "--role=dedicated", "--name=server", "--test=dedicated", $"--port={port + 1}" }),
                ("D", new[] { "--role=client", "--name=D", $"--port={port + 1}", $"--sim={Sim}" }),
                ("E", new[] { "--role=client", "--name=E", $"--port={port + 1}", $"--sim={Sim}" }),
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            _failures++;
            GD.PrintErr($"FAIL  runner: {e}");
        }
        GD.Print(_failures == 0 ? "\nALL PASSED" : $"\n{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    async Task RunScenario((string name, string[] args)[] processes)
    {
        string exe = OS.GetExecutablePath();
        string project = ProjectSettings.GlobalizePath("res://");
        var running = new List<(string name, Process process)>();
        var lines = new ConcurrentDictionary<string, ConcurrentQueue<string>>();
        foreach (var (name, extra) in processes)
        {
            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string a in new[] { "--headless", "--path", project, "res://tests/NetTest.tscn", "--" }.Concat(extra))
                info.ArgumentList.Add(a);
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            var log = lines.GetOrAdd(name, _ => new ConcurrentQueue<string>());
            process.OutputDataReceived += (_, e) => { if (e.Data != null) { log.Enqueue(e.Data); _output.Enqueue($"[{name}] {e.Data}"); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) { log.Enqueue(e.Data); _output.Enqueue($"[{name}] {e.Data}"); } };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            running.Add((name, process));
            await Seconds(name == processes[0].name ? 2.0 : 0.3); // the host first
        }

        double end = NetClock.RealNow + ScenarioTimeout;
        while (running.Any(r => !r.process.HasExited) && NetClock.RealNow < end)
        {
            Flush();
            await Seconds(0.1);
        }
        foreach (var (name, process) in running.Where(r => !r.process.HasExited))
        {
            _failures++;
            GD.PrintErr($"FAIL  {name} still running after {ScenarioTimeout} s; killed");
            process.Kill(true);
        }
        foreach (var (_, process) in running) process.WaitForExit(); // drains the output
        Flush();

        foreach (var (name, process) in running)
        {
            int code = process.ExitCode;
            bool director = name == processes[0].name;
            bool graceful = director || lines[name].Any(l => l.Contains("ENDED graceful=True"));
            bool errors = lines[name].Any(l => l.Contains("ERROR:") || l.Contains("SCRIPT ERROR") || l.Contains("Unhandled exception"));
            if (code == 0 && graceful && !errors)
                GD.Print($"PASS  {name} exited cleanly{(director ? "" : " after the session ended gracefully")}");
            else
            {
                _failures++;
                GD.PrintErr($"FAIL  {name}: exit code {code}, graceful end {graceful}, engine errors {errors}");
            }
        }
    }

    void Flush()
    {
        while (_output.TryDequeue(out string line))
        {
            if (line.Contains("Godot Engine v")) continue;
            if (line.Contains("FAIL") || line.Contains("ERROR")) GD.PrintErr(line);
            else GD.Print(line);
        }
    }

    public async Task Seconds(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    public async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
