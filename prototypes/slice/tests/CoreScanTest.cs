using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Ui;
using Slice.Vessels;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// The hand scanner's core sight, on a fresh world: it is free from the start, and held near a place with a core behind
/// walls, a marker sits on the core's projected place though the view of it is blocked, at the right distance; it fades
/// at the range's edge and shows nothing past it, with another tool held; a structure's core shows too.
///
/// Run: test-q.ps1 -Project prototypes/slice -Scene res://tests/CoreScanTest.tscn
/// </summary>
public partial class CoreScanTest : Node
{
    IEnumerator<int> _run;
    int _failures, _passes;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(8));
            Console.Error.WriteLine("FAIL  the run took over 8 minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e) { Check(false, $"scenario threw: {e}"); more = false; }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++; else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    static readonly Vector2 Size = new(1920, 1080);

    IEnumerable<int> Scenario()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string family = CoreScanStage.ForceCoreFamily();
        var game = new Game.Game { Interactive = false, World = WorldChoice.FromArgs(args) ?? WorldChoice.Tested, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        var play = new Playthrough(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        PlanetSurface.Poi.PoiSiting.ForceState = null;
        game.Zone.Weather.Bridge.Calm = true;
        game.Zone.Wildlife.Frozen = true;
        GD.Print($"-- the hand scanner, a {family} near the start, {game.World} --");
        ItemDef item = game.Zone.Data.Items.Item("scanner");
        Visor visor = game.Visor;
        Camera3D cam = game.Camera;

        // The one scanner: free from the start, with a range for the core sight, and no core scanner item left.
        Check(item.Recipe != null && item.Unlock is { Cost: 0 } && item.Range > 0f, $"data: the hand scanner is free (tier {item.Recipe?.Tier}), with a range of {item.Range} m");
        Check(!game.Zone.Data.Items.TryGet("core-scanner", out _), "there is no separate core scanner");

        // Held, outside a place whose core is behind walls.
        game.Pack.Add("scanner", 1);
        int scanSlot = -1;
        foreach (int x in CoreScanStage.Hold(play, "scanner", s => scanSlot = s)) yield return x;
        var stage = new CoreScanStage();
        foreach (int x in stage.Go(game, drive)) yield return x;
        if (stage.Place == null) { Check(false, "a place with a core whose view is blocked from outside, among the nearest eight"); yield break; }
        foreach (int x in play.CloseMenu()) yield return x;
        foreach (int x in drive.LookAt(stage.Core, 12)) yield return x;
        Vector3 eye = game.Survivor.WorldEye();
        var ray = game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, stage.Core, GalHack.Physics.Layers.World));
        Check(scanSlot >= 0 && game.Pack.Hand == HandKind.Scanner, "the hand scanner is on the toolbar and in hand");
        Check(ray.Count > 0, $"the view of the core of {stage.Place.Site.Name} ({stage.Core.DistanceTo(eye):F0} m) is blocked");
        MarkOn(visor, cam, stage.Core, "a place's core");

        // It follows the core as the view turns.
        foreach (int x in drive.LookAt(stage.Core + cam.GlobalTransform.Basis.X * 12f, 12)) yield return x;
        MarkOn(visor, cam, stage.Core, "a place's core, the view turned away");

        // The range's edge: full strength inside, fading out across the last metres, gone past it.
        float range = item.Range;
        Vector3 away = new(1f, 0f, 0f);
        Check(game.CoresInRange(stage.Core + away * (range - Game.Game.CoreFade - 1f)).Any(c => c.At.DistanceTo(stage.Core) < 0.1f && c.Alpha > 0.99f), $"{range - Game.Game.CoreFade - 1f:F0} m off: full strength");
        var edge = game.CoresInRange(stage.Core + away * (range - Game.Game.CoreFade / 2f)).Where(c => c.At.DistanceTo(stage.Core) < 0.1f).ToList();
        Check(edge.Count == 1 && edge[0].Alpha is > 0.3f and < 0.7f, $"{range - Game.Game.CoreFade / 2f:F0} m off: half faded ({(edge.Count == 1 ? edge[0].Alpha : -1f):F2})");
        Check(!game.CoresInRange(stage.Core + away * (range + 1f)).Any(c => c.At.DistanceTo(stage.Core) < 0.1f), $"{range + 1f:F0} m off: no marker");

        // Beyond the range for real: stood well away, nothing on the visor at that core.
        Vector3 far = stage.Core + away * (range + 60f);
        game.Survivor.PlaceOutside((far with { Y = game.Zone.GroundAt(far.X, far.Z).Y + 0.3f }).ToNumerics());
        foreach (int x in drive.Wait(30)) yield return x;
        Check(!visor.Markers().Any(m => m.Kind == "core" && m.At.DistanceTo(stage.Core) < 0.1f), "beyond the range, no marker on that core");

        // Back in range with the scanner, then with the survival tool in hand: nothing.
        foreach (int x in stage.Go(game, drive)) yield return x;
        Check(visor.Markers().Any(m => m.Kind == "core"), "back in range with the scanner: markers again");
        game.Pack.Add("survival-tool", 1);
        foreach (int x in CoreScanStage.Hold(play, "survival-tool", _ => { })) yield return x;
        Check(game.Pack.Hand == HandKind.Tool && !visor.Markers().Any(m => m.Kind == "core"), "with the survival tool in hand: no marker");
        Check(!visor.LayoutMarkers(Size, 1f).Any(p => p.Mark.Kind == "core"), "nor on the drawn layout");
        foreach (int x in play.Select(scanSlot)) yield return x;

        // A structure's core.
        Vector3 site = play.FlatSpot(game.Survivor.WorldFeet, 30f, 0.6f, 0.4f, span: 14f);
        Structure s = CoreCraft.Skiff(game.Zone.Data);
        CoreCraft.FitPlates(s, game.Zone.Data.Catalogue);
        s.DirtyChunks.Clear();
        Vessel v = Vessel.Spawn(game.Zone, s, (site + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        v.Settle();
        foreach (int x in drive.Wait(30)) yield return x;
        Vector3 coreAt = v.ToWorld(PhysicsSandbox.Bridges.StructureBridge.CellCenter(v.Structure, v.Structure.Core.Value));
        foreach (int x in drive.LookAt(coreAt, 12)) yield return x;
        Check(game.Pack.Hand == HandKind.Scanner, "the scanner in hand again");
        MarkOn(visor, cam, coreAt, "a vessel's core");
    }

    /// <summary>A core marker is on screen at the engine's projection of <paramref name="at"/>, reading the distance from the eye.</summary>
    void MarkOn(Visor visor, Camera3D cam, Vector3 at, string what)
    {
        Vector3 l = cam.GlobalTransform.AffineInverse() * at;
        Vector4 clip = Projection.CreatePerspective(cam.Fov, Size.X / Size.Y, cam.Near, cam.Far, false) * new Vector4(l.X, l.Y, l.Z, 1f);
        Vector2 want = new((clip.X / clip.W + 1f) / 2f * Size.X, (1f - clip.Y / clip.W) / 2f * Size.Y);
        Visor.PlacedMarker? m = visor.LayoutMarkers(Size, 1f).Where(p => p.Mark.Kind == "core" && p.Mark.At.DistanceTo(at) < 0.2f).Select(p => (Visor.PlacedMarker?)p).FirstOrDefault();
        float d = cam.GlobalPosition.DistanceTo(at);
        Check(m is { Pinned: false, Behind: false } && m.Value.Anchor.DistanceTo(want) < 4f && m.Value.Icon.GetCenter().DistanceTo(want) < 4f && MathF.Abs(m.Value.Distance - d) < 0.5f && m.Value.Alpha > 0.9f,
            $"{what}: marker {(m == null ? "missing" : $"at {m.Value.Icon.GetCenter()} for {want}, {(m.Value.Pinned ? "pinned, " : "")}reads {Visor.Distance(m.Value.Distance)} for {d:F1} m, strength {m.Value.Alpha:F2}")}");
    }
}
