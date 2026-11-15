using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>
/// The hand scanner's place marks, on a fresh world: a scan marks every place within the scanner's scan range (the
/// item's data) and none beyond, each on the visor as an icon and a distance with no name, at the start and stood
/// by a place that was out of range (now marked); the marks are still there at 29 s and gone by 30.3 s; a second
/// scan brings them back.
///
/// Run: test-q.ps1 -Project prototypes/slice -Scene res://tests/PlaceScanTest.tscn
/// </summary>
public partial class PlaceScanTest : Node
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

    static readonly string[] Icons = { "place", "ruin", "wreck" };

    Game.Game _game;

    IEnumerable<int> Scenario()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _game = new Game.Game { Interactive = false, World = WorldChoice.FromArgs(args) ?? WorldChoice.Tested, SkipIntro = true };
        AddChild(_game);
        var drive = new Driver(_game);
        var play = new Playthrough(_game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        ItemDef item = _game.Zone.Data.Items.Item("scanner");
        float range = item.Scan;
        GD.Print($"-- the hand scanner's place marks, {_game.World}, {_game.Zone.Planet.Pois.Count} places --");
        Check(range >= 900f && range <= 1100f, $"data: the hand scanner marks places within {range} m (design: about 1 km)");

        _game.Pack.Add("scanner", 1);
        foreach (int x in CoreScanStage.Hold(play, "scanner", _ => { })) yield return x;
        Check(_game.Pack.Hand == HandKind.Scanner && _game.PlaceScanRange == range, "the scanner in hand");
        Check(_game.Visor.Markers().All(m => !Icons.Contains(m.Kind) || m.Tag != ""), "before a scan, no place marks");

        // A scan at the start: every place within range (there may be none), none beyond.
        foreach (int x in ScanHere(drive, range, "at the start")) yield return x;

        // Stood elsewhere, by a place that was out of range: it is marked now.
        Vector3 feet = _game.Survivor.WorldFeet;
        var beyond = Expected(feet, float.MaxValue).Where(e => Flat(e.At, feet) > range * 1.2f).OrderBy(e => Flat(e.At, feet)).FirstOrDefault();
        if (beyond.Key == null) { Check(false, "a place beyond the range to walk to"); yield break; }
        Vector3 dir = new Vector3(beyond.At.X - feet.X, 0f, beyond.At.Z - feet.Z).Normalized();
        Vector3 to = beyond.At - dir * range * 0.6f;
        _game.Survivor.PlaceOutside(new System.Numerics.Vector3(to.X, _game.Zone.GroundAt(to.X, to.Z).Y + 1f, to.Z));
        foreach (int x in drive.UntilSettled()) yield return x;
        foreach (int x in play.WaitFor(() => _game.ScanWait <= 0, 5)) yield return x;
        foreach (int x in ScanHere(drive, range, $"{Flat(beyond.At, feet):F0} m away")) yield return x;
        int marked = PlaceMarks().Count;
        Check(PlaceMarks().Any(p => p.Key == beyond.Key), $"there, the place that was {Flat(beyond.At, feet):F0} m off ({beyond.Key}) is marked, of {marked}");

        // The marks time out 30 s after the scan.
        double t0 = _game.Stats.Seconds;
        foreach (int x in play.WaitFor(() => _game.Stats.Seconds - t0 >= 29.0, 40)) yield return x;
        var late = _game.Visor.Markers().Where(m => Icons.Contains(m.Kind) && m.Tag == "").ToList();
        Check(PlaceMarks().Count == marked && late.Count == VisorExpected(), $"at 29 s: still {marked} marks, {late.Count} on the visor, fading (strength {(late.Count > 0 ? late.Max(m => m.Alpha) : 0):F2})");
        foreach (int x in play.WaitFor(() => _game.Stats.Seconds - t0 >= 30.3, 5)) yield return x;
        Check(PlaceMarks().Count == 0 && !_game.Visor.Markers().Any(m => Icons.Contains(m.Kind) && m.Tag == ""), $"at 30.3 s: the marks are gone ({PlaceMarks().Count} left)");

        // A second scan brings them back.
        foreach (int x in ScanHere(drive, range, "a second scan")) yield return x;
        Check(PlaceMarks().Count == marked, $"a second scan: the same {marked} marks back ({PlaceMarks().Count})");
    }

    List<Game.Game.Ping> PlaceMarks() => _game.Pings.Where(p => p.Place != null).ToList();

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>The places the planet has (a city as one at its middle, a typed place as one on its anchor), each at its copy nearest <paramref name="feet"/>, within <paramref name="range"/> across the ground.</summary>
    List<(string Key, Vector3 At)> Expected(Vector3 feet, float range)
    {
        var planet = _game.Zone.Planet;
        var city = planet.Infra.City;
        var all = new List<(string, Vector3)>();
        for (int i = 0; i < planet.Pois.Count; i++)
            if (!planet.Pois[i].Family.Space && city?.Sites.Contains(planet.Pois[i]) != true 
                && (planet.Pois[i].Whole is not { } whole || (whole.Anchor ?? whole.Parts[0]) == planet.Pois[i]))
                all.Add(($"poi{i}", planet.NearestCopy(new Vector3(planet.Pois[i].Origin.X, feet.Y, planet.Pois[i].Origin.Z), feet)));
        if (city != null) all.Add(("city", planet.NearestCopy(new Vector3((float)city.CenterX, feet.Y, (float)city.CenterZ), feet)));
        return all.Where(e => Flat(e.Item2, feet) <= range).ToList();
    }

    /// <summary>How many place marks the visor should draw as their own: those not already standing under a found place's named marker.</summary>
    int VisorExpected()
    {
        var named = _game.Visor.Markers().Where(m => m.Kind == "place" && m.Tag != "").ToList();
        return PlaceMarks().Count(p => !named.Any(n => Flat(n.At, p.At) < 1f));
    }

    IEnumerable<int> ScanHere(Driver drive, float range, string where)
    {
        Vector3 feet = _game.Survivor.WorldFeet;
        Check(_game.Scan(), $"{where}: the scan goes off");
        foreach (int x in drive.Wait(2)) yield return x;
        var want = Expected(feet, range).Select(e => e.Key).ToHashSet();
        var got = PlaceMarks();
        var keys = got.Select(p => p.Key).ToHashSet();
        int beyond = Expected(feet, float.MaxValue).Count - want.Count;
        Check(keys.SetEquals(want) && got.Count == keys.Count,
            $"{where}: {got.Count} places marked, every one of the {want.Count} within {range:F0} m and none of the {beyond} beyond (missing {string.Join(",", want.Except(keys))}; extra {string.Join(",", keys.Except(want))})");
        Check(got.All(p => Flat(p.At, feet) <= range + 0.01f && p.Until - _game.Stats.Seconds is > 29.0 and <= 30.0), $"{where}: each within range, timing out 30 s after the scan");
        var shown = _game.Visor.Markers().Where(m => Icons.Contains(m.Kind) && m.Tag == "").ToList();
        Check(shown.Count == VisorExpected() && shown.All(m => m.Alpha > 0.99f), $"{where}: {shown.Count} on the visor as an icon and a distance ({string.Join(" ", shown.GroupBy(m => m.Kind).Select(g => $"{g.Key} {g.Count()}"))})");
        var placed = _game.Visor.LayoutMarkers(new Vector2(1920, 1080), 1f).Where(p => Icons.Contains(p.Mark.Kind) && p.Mark.Tag == "").ToList();
        Check(placed.Count > 0 || shown.Count == 0, $"{where}: {placed.Count} laid out on a 1920x1080 visor");
        GD.Print($"  (marks: {string.Join(", ", got.OrderBy(p => Flat(p.At, feet)).Select(p => $"{p.Key} {p.Place} {Visor.Distance(Flat(p.At, feet))}"))})");
    }
}
