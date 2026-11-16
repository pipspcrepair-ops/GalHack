using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Dir = GalHack.Structures.Dir;
using Dirs = GalHack.Structures.Dirs;
using Int3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// The ship forms' gate (docs/ship-forms.md, "Gates" 1): the six factions' forms and a Husk one, five fresh seeds each,
/// built by the hull builder as vessels of the player's own blocks and drawn at one scale from a three-quarter view off
/// the bow, a little above: one sheet greyed out (the livery off), one in plain faction colours, a 50 m bar in every
/// cell; a row's first three rolls show three of the faction's body plans, the Husk row five factions' ships. Prints each
/// hull's build time (its form and its blocks, off the main thread), what the builder left out and what floats.
///
/// Run (windowed: it renders): G:\GalHack\.tools\queue\godot-q.ps1 -Console --path &lt;slice&gt; res://tests/ShipFormSheet.tscn
/// -After --dir=&lt;folder&gt;[,--size=medium|large|huge]
/// </summary>
public partial class ShipFormSheet : Node
{
    const int W = 640, Hgt = 400, Cols = 5;
    /// <summary>The view's width, m: every cell at the same scale.</summary>
    const float Span = 230f;

    static readonly (string Name, string Faction)[] Rows =
    {
        ("Freeholders", "freeholders"), ("Meridian", "meridian"), ("Exchange", "exchange"), ("Reavers", "reavers"), ("Kessari", "kessari"), ("Vharn", "vharn"), ("Husk", null),
    };

    sealed record Built(string Label, Structure Grey, Structure Colour, double Ms, int Dropped, int Loose);

    readonly Dictionary<string, string> _args = OS.GetCmdlineUserArgs().Where(a => a.StartsWith("--") && a.Contains('=')).ToDictionary(a => a[2..a.IndexOf('=')], a => a[(a.IndexOf('=') + 1)..]);
    readonly List<Task<Built>> _cells = new();
    SubViewport _view;
    Camera3D _camera;
    Label _label;
    Node3D _model;
    Image _grey, _colour;
    IEnumerator<int> _run;
    static System.Threading.Timer _watchdog;

    public override void _Ready()
    {
        _watchdog = new System.Threading.Timer(_ => System.Environment.Exit(3), null, TimeSpan.FromMinutes(15), System.Threading.Timeout.InfiniteTimeSpan);
        string size = _args.GetValueOrDefault("size", "large");
        PoiLibrary lib = WorldChoice.Library;
        Catalogue cat = GameData.Shared.Catalogue;
        SiteDef site = lib.Sites.Values.First(s => s.Parts.Count > 0 && s.Parts[0].Arg("what") == "capital");
        Kit kit = lib.Kits[site.Style];
        TechLevel tech = WorldProfile.Parse(site.Parts[0].Arg("tech", "mid"));
        string[] roles = { "freighter", "warship", "liner", "miner", "carrier" }, six = Rows.Take(6).Select(r => r.Faction).ToArray();
        // Every cell a fresh seed, built a few at a time off the main thread; a row's first three show three of its faction's
        // body plans (fresh seeds drawn until one of a plan not yet shown comes up), the Husk row five factions' ships.
        var gate = new System.Threading.SemaphoreSlim(Math.Max(2, System.Environment.ProcessorCount / 2));
        foreach (var (name, faction) in Rows)
        {
            var shown = new HashSet<string>();
            string[] husks = six.OrderBy(_ => Random.Shared.Next()).ToArray();
            for (int c = 0; c < Cols; c++)
            {
                string role = "", who = faction ?? husks[c];
                bool husk = faction == null;
                ulong seed = 0;
                for (int tries = 0; tries < 40; tries++)
                {
                    (seed, role) = ((ulong)Random.Shared.NextInt64(), roles[Random.Shared.Next(roles.Length)]);
                    if (husk || c >= 3 || shown.Add(CapitalHull.Plan(kit, tech, new CapitalHull.Spec(role, size, "long", who), seed).Form.Plan)) break;
                }
                _cells.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        var clock = Stopwatch.StartNew();
                        CapitalHull.HullPlan plan = CapitalHull.Plan(kit, tech, new CapitalHull.Spec(role, size, "long", who, husk), seed);
                        PoiState state = husk ? PoiState.Infested : PoiState.Abandoned;
                        HullVessel.Result built = HullVessel.Build(plan, plan.Pieces[0], cat, state, seed, intact: true);
                        Structure colour = built.Structure;
                        double ms = clock.Elapsed.TotalMilliseconds;
                        Structure grey = HullVessel.Build(plan, plan.Pieces[0], cat, state, seed, intact: true, plain: true).Structure;
                        return new Built($"{name}{(husk ? " (" + who + ")" : "")}  {plan.Form.Plan}  {role}  {plan.L} m", grey, colour, ms, built.Dropped, Loose(colour));
                    }
                    finally { gate.Release(); }
                }));
            }
        }
        Stage();
        _run = Run().GetEnumerator();
    }

    public override void _Process(double delta)
    {
        if (!_run.MoveNext()) SetProcess(false);
    }

    /// <summary>The cell's own small world: space behind, a key light off the bow quarter and a cool fill, an orthographic camera, the label and the 50 m bar.</summary>
    void Stage()
    {
        _view = new SubViewport { Size = new Vector2I(W, Hgt), OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(_view);
        _view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.19f, 0.21f, 0.25f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.63f, 0.7f), AmbientLightEnergy = 0.65f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.35f, ShadowEnabled = true, DirectionalShadowMaxDistance = 1500f };
        _view.AddChild(sun);
        sun.LookAtFromPosition(new Vector3(0.9f, 1.4f, -0.6f), Vector3.Zero);
        var fill = new DirectionalLight3D { LightEnergy = 0.3f, LightColor = new Color(0.65f, 0.75f, 1f) };
        _view.AddChild(fill);
        fill.LookAtFromPosition(new Vector3(-1f, -0.3f, 0.8f), Vector3.Zero);
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = Span * Hgt / W, Near = 1f, Far = 3000f, Current = true };
        _view.AddChild(_camera);
        _label = new Label { Position = new Vector2(14, 10) };
        _label.AddThemeFontSizeOverride("font_size", 18);
        _label.AddThemeColorOverride("font_color", new Color(0.85f, 0.86f, 0.88f));
        _view.AddChild(_label);
        float bar = 50f * W / Span;
        _view.AddChild(new ColorRect { Position = new Vector2(16, Hgt - 26), Size = new Vector2(bar, 6), Color = new Color(0.85f, 0.86f, 0.88f) });
        var unit = new Label { Position = new Vector2(24 + bar, Hgt - 38), Text = "50 m" };
        unit.AddThemeFontSizeOverride("font_size", 16);
        unit.AddThemeColorOverride("font_color", new Color(0.85f, 0.86f, 0.88f));
        _view.AddChild(unit);
    }

    /// <summary>A structure drawn with the game's own chunk mesher and materials, the camera on it from off the bow, a little above.</summary>
    void Show(Structure s)
    {
        _model?.QueueFree();
        _model = new Node3D();
        var data = new ChunkMeshData();
        foreach (Int3 chunk in s.Chunks.Keys)
        {
            ChunkMesher.Build(s, chunk, data, colliders: false);
            _model.AddChild(new MeshInstance3D { Mesh = ChunkMesher.ToMesh(data) });
        }
        _view.AddChild(_model);
        var (lo, hi) = s.Bounds();
        float cell = s.Grid.CellSize;
        Vector3 centre = new Vector3(lo.X + hi.X, lo.Y + hi.Y, lo.Z + hi.Z) * 0.5f * cell;
        _camera.LookAtFromPosition(centre + new Vector3(0.8f, 0.42f, -0.55f).Normalized() * 1200f, centre, Vector3.Up);
    }

    /// <summary>Blocks not joined face to face to the hull's largest piece: what floats.</summary>
    static int Loose(Structure s)
    {
        var left = s.Blocks().Select(b => b.pos).ToHashSet();
        int total = left.Count, largest = 0;
        var stack = new Stack<Int3>();
        while (left.Count > 0)
        {
            Int3 start = left.First();
            left.Remove(start);
            stack.Push(start);
            int n = 0;
            while (stack.Count > 0)
            {
                Int3 c = stack.Pop();
                n++;
                foreach (Dir d in Dirs.All)
                    if (left.Remove(c + d.Vec())) stack.Push(c + d.Vec());
            }
            largest = Math.Max(largest, n);
        }
        return total - largest;
    }

    IEnumerable<int> Run()
    {
        var times = new List<double>();
        int loose = 0;
        for (int i = 0; i < _cells.Count; i++)
        {
            while (!_cells[i].IsCompleted) yield return 0;
            if (_cells[i].IsFaulted)
            {
                GD.PrintErr($"cell {i}: {_cells[i].Exception?.InnerException}");
                continue;
            }
            Built b = _cells[i].Result;
            times.Add(b.Ms);
            GD.Print($"  {b.Label}: {b.Colour.Count} blocks, built in {b.Ms / 1000:F1} s, {b.Dropped} left out, {b.Loose} floating");
            loose += b.Loose;
            _label.Text = b.Label;
            var at = new Vector2I(i % Cols * W, i / Cols * Hgt);
            foreach (bool grey in new[] { true, false })
            {
                Show(grey ? b.Grey : b.Colour);
                for (int f = 0; f < 4; f++) yield return 0;
                Image shot = _view.GetTexture().GetImage();
                if (grey) (_grey ??= Image.CreateEmpty(W * Cols, Hgt * Rows.Length, false, shot.GetFormat())).BlitRect(shot, new Rect2I(0, 0, W, Hgt), at);
                else (_colour ??= Image.CreateEmpty(W * Cols, Hgt * Rows.Length, false, shot.GetFormat())).BlitRect(shot, new Rect2I(0, 0, W, Hgt), at);
            }
            _cells[i] = Task.FromResult<Built>(null);
        }
        string dir = _args.GetValueOrDefault("dir", ".");
        System.IO.Directory.CreateDirectory(dir);
        _grey?.SavePng($"{dir}/ship-forms-grey.png");
        _colour?.SavePng($"{dir}/ship-forms-colour.png");
        GD.Print($"sheets: {dir}/ship-forms-grey.png, {dir}/ship-forms-colour.png; {times.Count} hulls, build {times.DefaultIfEmpty(0).Average() / 1000:F1} s mean, {times.DefaultIfEmpty(0).Max() / 1000:F1} s slowest; {loose} blocks floating in all");
        Wildlife.Shutdown.Quit(this, times.Count == _cells.Count ? 0 : 1);
    }
}
