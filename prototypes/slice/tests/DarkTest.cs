using GalHack.Physics.Engine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using GalHack.Structures;
using Godot;
using Slice.Game;
using Slice.Surface;
using Slice.Vessels;
using WeatherKind = Weather.Model.WeatherKind;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace Slice.Tests;

/// <summary>
/// Dark is dark, on a fresh world: with no light source a sealed room and the depths of a cave read
/// next to nothing at noon and at midnight alike, a lamp lifts them, light rises toward a cave mouth,
/// the open sky still lights the open ground by day and by night, and digging lets it in.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/DarkTest.tscn
/// </summary>
public partial class DarkTest : Node
{
    const int WatchdogMinutes = 10;
    const float NearZero = 0.01f;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Game.Game _game;
    Driver _drive;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    SkyLight Light => _game.Zone.Light;

    /// <summary>Stands the survivor at a spot and waits for the field to be built round it.</summary>
    IEnumerable<int> StandAt(Vector3 feet)
    {
        _game.Survivor.PlaceOutside(feet.ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in Refreshed()) yield return x;
    }

    /// <summary>Waits for a field started after now.</summary>
    IEnumerable<int> Refreshed()
    {
        int floods = Light.Floods;
        for (int i = 0; i < 240 && Light.Floods < floods + 2; i++)
        {
            Light.Touch();
            yield return 0;
        }
    }

    float At(Vector3 p, double time)
    {
        _game.Zone.Sky.TimeOfDay = time;
        _game.Zone.Sky.Apply();
        return Light.LightAt(p);
    }

    /// <summary>A sealed box of iron, a core inside: 10 m across inside, and nothing in it that lights.</summary>
    static string SealedRoom()
    {
        // The room it was on the 2 m grid, each of its cells two 1 m blocks a side: 10 m inside, 6 m floor to ceiling.
        var b = new StringBuilder("schema galhack.blueprint\nversion 1\nname \"Sealed\"\ngrid 1m\nmode base\ngravity off\n");
        for (int y = 0; y <= 4; y++)
        for (int z = -3; z <= 3; z++)
        for (int x = -3; x <= 3; x++)
            if (y == 0 || y == 4 || Math.Abs(x) == 3 || Math.Abs(z) == 3)
                b.Append($"block {2 * x}..{2 * x + 1} {2 * y}..{2 * y + 1} {2 * z}..{2 * z + 1} iron-plated\n");
        b.Append("block 4 2 4 core\n");
        return b.ToString();
    }

    IEnumerable<int> Scenario()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _game = new Game.Game { Interactive = false, World = WorldChoice.FromArgs(args) ?? WorldChoice.Tested, SkipIntro = true };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        GD.Print($"world {_game.World}");
        SurfaceZone zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        zone.Sky.Paused = true;

        // The open ground, by day and by night.
        Vector3 outside = _game.Bunker.Ahead(8f);
        outside = zone.GroundAt(outside.X, outside.Z) + Vector3.Up * 0.3f;
        foreach (int x in StandAt(outside)) yield return x;
        Check(Light.Field != null, $"a field was built ({Light.Floods} floods, last {Light.LastFloodMs:F1} ms)");
        double noon = Staging.HighSun(_game), midnight = Staging.HighSun(_game, highest: false);
        Vector3 air = outside + Vector3.Up * 2f;
        float dayOut = At(air, noon), nightOut = At(air, midnight);
        Check(Light.Openness(air) > 0.95f, $"open air is open to the sky ({Light.Openness(air):F2})");
        Check(dayOut > 0.5f, $"open ground is well lit at noon ({dayOut:F2})");
        Check(nightOut > 0.03f && nightOut < dayOut * 0.5f, $"and dim but not black at midnight ({nightOut:F3})");

        // A sealed room, at either hour, then with a lamp, then breached.
        Structure s = Blueprint.Load(SealedRoom(), zone.Data.Catalogue, out List<string> errors);
        Check(s != null, $"the sealed room's blueprint loads {string.Join("; ", errors)}");
        Vector3 site = zone.GroundAt(outside.X + 30f, outside.Z);
        Vessel room = Vessel.SpawnBase(zone, s, (site + Vector3.Up * 4f).ToNumerics(), 0f);
        foreach (int x in Refreshed()) yield return x;
        Vector3 centre = room.ToWorld(new System.Numerics.Vector3(0f, 2f * 2f, 0f));
        Vector3 wall = room.ToWorld(new System.Numerics.Vector3(5f, 2f * 2f, 0f));
        Check(Light.Field.Holds(Mathf.FloorToInt(centre.X), Mathf.FloorToInt(centre.Y), Mathf.FloorToInt(centre.Z)), "the room is inside the field");
        float noonIn = At(centre, noon), nightIn = At(centre, midnight);
        Check(noonIn < NearZero, $"a sealed room is black at noon ({noonIn:F4})");
        Check(nightIn < NearZero, $"and at midnight ({nightIn:F4})");
        Check(Mathf.Abs(noonIn - nightIn) < 0.001f, "the same at both hours");

        // A vessel carries its dark with it, field or no field (flying, in orbit).
        var own = new StructureSky();
        own.Flood(s);
        Check(own.DarkAt(new Vector3(0f, 4f, 0f)) > 0.99f, $"the room's own flood is dark inside ({own.DarkAt(new Vector3(0f, 4f, 0f)):F2})");
        Check(own.DarkAt(new Vector3(0f, 12f, 0f)) < 0.01f, "and open over its roof");
        Check(own.DarkAt(new Vector3(9f, 4f, 0f)) < 0.01f, "and outside its walls");

        var lamp = new OmniLight3D { LightEnergy = 1.5f, OmniRange = 8f };
        AddChild(lamp);
        lamp.GlobalPosition = centre + Vector3.Up * 2f;
        foreach (int x in _drive.Wait(2)) yield return x;
        float lit = At(centre, noon), farCorner = At(room.ToWorld(new System.Numerics.Vector3(4f, 3f, 4f)), noon);
        Check(lit > 0.2f, $"a lamp lights it ({lit:F2})");
        Check(farCorner < lit, $"and falls off with distance ({lit:F2} by it, {farCorner:F2} in the corner)");
        lamp.Free();
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(At(centre, noon) < NearZero, "with the lamp gone it's black again");

        // A hole in the wall: daylight comes in, the most near the hole.
        for (int y = 2; y <= 5; y++) for (int x = 6; x <= 7; x++) for (int z = 0; z <= 1; z++) room.Structure.Clear(new Int3(x, y, z)); // the old 2 m cells (3, 1..2, 0)
        foreach (int x in Refreshed()) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        float byHole = At(wall, noon), farSide = At(room.ToWorld(new System.Numerics.Vector3(-4.4f, 4f, 0f)), noon);
        Check(byHole > 0.15f, $"a breach lets daylight in ({byHole:F2} beside it)");
        Check(farSide < byHole, $"and it fades across the room ({farSide:F3} on the far side)");

        // A dug hole updates as you dig.
        Vector3 ground = zone.GroundAt(outside.X - 16f, outside.Z - 16f);
        var bottom = new Vector3(ground.X, ground.Y - 10f, ground.Z);
        foreach (int x in StandAt(ground + Vector3.Up * 0.5f)) yield return x;
        bool wasSolid = Light.Field.IsSolid(Mathf.FloorToInt(bottom.X), Mathf.FloorToInt(bottom.Y), Mathf.FloorToInt(bottom.Z));
        Check(wasSolid || Light.Openness(bottom) < NearZero, "ten metres down starts as dark rock");
        for (int d = 0; d <= 12; d += 2)
            zone.Edits.Dig(new TInt3(Mathf.FloorToInt(ground.X), Mathf.FloorToInt(ground.Y) - d, Mathf.FloorToInt(ground.Z)), 2f);
        for (int i = 0; i < 40; i++) yield return 0;
        Check(!Light.Field.IsSolid(Mathf.FloorToInt(bottom.X), Mathf.FloorToInt(bottom.Y), Mathf.FloorToInt(bottom.Z)), "a shaft dug down opens the field within 40 ticks");
        Check(Light.Openness(bottom) > 0.3f, $"and daylight falls down it ({Light.Openness(bottom):F2})");

        // A cave: rising toward its mouth, black in its depths at both hours.
        if (Staging.NearestCave(_game) is { } cave)
        {
            Vector3[] line = cave.Line;
            Vector3 deep = line[^1] + Vector3.Up * 0.5f;
            Vector3 feet = line[line.Length / 2];
            foreach (int x in StandAt(feet)) yield return x;
            float mouth = line.Take(3).Max(p => At(p + Vector3.Up * 1.5f, noon));
            float mid = At(line[line.Length / 3] + Vector3.Up * 0.5f, noon);
            float deepNoon = At(deep, noon), deepNight = At(deep, midnight);
            GD.Print($"  cave {line.Length} points, mouth {mouth:F3}, a third in {mid:F3}, deep {deepNoon:F4}");
            Check(mouth > 0.2f, $"daylight stands in a cave's mouth ({mouth:F2})");
            Check(mouth > mid, $"and falls off inward ({mouth:F2} to {mid:F3})");
            Check(deepNoon < NearZero, $"deep in the cave it's black at noon ({deepNoon:F4})");
            Check(deepNight < NearZero, $"and at midnight ({deepNight:F4})");
            _game.Torch.Visible = true;
            float torch = Light.LightAt(_game.Torch.GlobalPosition - _game.Torch.GlobalTransform.Basis.Z * 1.5f);
            _game.Torch.Visible = false;
            Check(torch > 0.2f, $"the head torch lights what's ahead ({torch:F2})");
        }
        else GD.Print("  (no cave mouth on this world: the cave checks are skipped)");

        GD.Print($"  flood worst {Light.WorstFloodMs:F1} ms on its worker; upload worst {Light.WorstUploadMs:F2} ms on the main thread; {Light.Floods} floods");
        // Other runs share the machine: a slow worker costs nothing, but the upload is on the frame.
        Check(Light.WorstUploadMs < 16f, $"uploading the field stays inside a frame ({Light.WorstUploadMs:F2} ms)");
    }
}
