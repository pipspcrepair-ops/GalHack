using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Slice.Game;
using Slice.Surface;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Frames of winged animals flying, from the player's own eyes, on a fresh world. Run windowed:
///   godot --fixed-fps 60 --path prototypes/slice res://tests/FlightShots.tscn -- --out=FOLDER
/// The animals are dropped in near the survivor and left to it: only where they start is staged, the
/// flight, the landing, the swoop and the sting are the sim's own. If the world has no glider or
/// hoverer of its own, one is rolled from the same world's seed. (Not a *Test scene, so the gate
/// doesn't run it.)
/// </summary>
public partial class FlightShots : Node
{
    const int Every = 4; // a frame kept every 4 ticks: 15 a second

    Game.Game _game;
    Playthrough _play;
    IEnumerator<int> _run;
    string _out = "flightshots";
    string _segment = "";
    int _shot;
    bool _grab, _finishing;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--out=")) _out = arg[6..];
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_finishing) return;
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            GD.PrintErr($"flight shots failed: {e}");
            _finishing = true;
            Shutdown.Quit(this, 1);
            return;
        }
        if (_grab)
        {
            Directory.CreateDirectory(Path.Combine(_out, _segment));
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_out, _segment, $"{_shot++:D4}.png"));
            _grab = false;
        }
        if (!more)
        {
            _finishing = true;
            Shutdown.Quit(this, 0);
        }
    }

    WildlifePatch W => _game.Zone.Wildlife;

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _play = new Playthrough(_game);
        Driver drive = _play.Drive;
        foreach (int x in drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.36;
        W.Frozen = true;
        foreach (int x in _play.WakeAndLeave((_, _) => { })) yield return x;
        foreach (int x in drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in drive.UntilSettled()) yield return x;
        GD.Print($"world: seed {_game.World.Seed}, {_game.World.Name}");
        W.Frozen = false;

        Species glider = Pick(FlightKind.Glide, "glider");
        Species hoverer = Pick(FlightKind.Hover, "darter");
        GD.Print($"glider: {glider.CommonName}, {glider.Body.Size:F2} m; hoverer: {hoverer.CommonName}, {hoverer.Body.Size:F2} m");

        foreach (int x in Flying(drive, glider)) yield return x;
        foreach (int x in Landing(drive, glider)) yield return x;
        foreach (int x in Swooping(drive, glider)) yield return x;
        foreach (int x in Hovering(drive, hoverer)) yield return x;
        foreach (int x in Stinging(drive, hoverer)) yield return x;
    }

    /// <summary>The world's own, or else one rolled from its seed; a copy, since it gets a job for the camera.</summary>
    Species Pick(FlightKind kind, string form)
    {
        Species own = W.Fauna.Species.Where(s => s.Flight == kind).OrderByDescending(s => s.Body.Size).FirstOrDefault();
        if (own != null) GD.Print($"the world's own {own.CommonName} ({own.Behaviour})");
        return own ?? new FaunaGenerator(_game.Zone.Data.Wildlife.Fauna).RollOne(_game.World.Seed, form, Behaviour.Predator, 0.3f);
    }

    static Species As(Species s, Behaviour b, bool hunts)
    {
        Species c = s.Copy();
        c.Behaviour = b;
        c.Activity = Activity.Cathemeral;
        c.Stats.Hunts = hunts;
        c.Stats.Retaliates = hunts;
        c.Stats.Damage = hunts ? MathF.Max(5f, MathF.Min(12f, c.Stats.Damage)) : 0f;
        c.Stats.FleeRadius = 0f;
        c.Stats.Perception = 70f;
        return c;
    }

    void ClearSky()
    {
        foreach (Creature c in W.Sim.Creatures) W.Sim.Remove(c);
    }

    /// <summary>Flat, ahead of where the survivor faces, in the patch's frame; with an optional step to the right.</summary>
    Vector3 Ahead(float metres, float right = 0f)
    {
        Vector3 f = _game.Survivor.WorldLook();
        f.Y = 0f;
        f = f.Normalized();
        return W.PatchLocal(_game.Survivor.WorldFeet) + f * metres + f.Cross(Vector3.Up) * right;
    }

    /// <summary>Keeps the survivor's eyes on it and the film running, until it is told to stop.</summary>
    IEnumerable<int> Film(string segment, Driver drive, Creature c, float seconds, Func<float, bool> done = null)
    {
        _segment = segment;
        _shot = 0;
        for (int tick = 0; tick < seconds * 60f; tick++)
        {
            if (c.Removed) yield break;
            drive.Face(W.FromPatch(c.Rig.BoundsCenter));
            if (tick % Every == 0)
            {
                _grab = true;
                GD.Print($"{segment} {_shot:D4}: t {tick / 60f:F1}, {c.State}/{c.Phase}, up {c.Position.Y - W.Sim.Terrain.Height(c.Position.X, c.Position.Z):F1} m, off {WildSim.Flat(c.Position - W.Player.Position).Length():F1} m, speed {WildSim.Flat(c.Velocity).Length():F1}, flap {c.Flapping}, landed {c.Landed}");
            }
            yield return 0;
            if (done != null && done(tick / 60f)) yield break;
        }
    }

    IEnumerable<int> Flying(Driver drive, Species s)
    {
        ClearSky();
        Species job = As(s, Behaviour.HarmlessOddity, false);
        job.Stats.Perception = 1f; // about its own business: it wanders round on the wing
        Creature c = W.Sim.Spawn(job, Ahead(45f, -30f));
        c.Home = Ahead(30f);
        c.Altitude = 11f;
        c.Position = new Vector3(c.Position.X, W.Sim.Terrain.Height(c.Position.X, c.Position.Z) + 12f, c.Position.Z);
        Vector3 to = c.Home - c.Position;
        c.Yaw = MathF.Atan2(-to.X, -to.Z);
        c.Velocity = c.Forward * Heft.CruiseAirspeed(c);
        foreach (int x in Film("flight", drive, c, 12f)) yield return x;
    }

    IEnumerable<int> Landing(Driver drive, Species s)
    {
        ClearSky();
        Species job = As(s, Behaviour.Grazer, false);
        Vector3 ground = Ahead(14f, 3f);
        Vector3 start = Ahead(46f, -22f);
        Creature c = W.Sim.Spawn(job, start);
        c.Home = ground;
        c.Position = new Vector3(c.Position.X, W.Sim.Terrain.Height(c.Position.X, c.Position.Z) + 9f, c.Position.Z);
        Vector3 to = ground - c.Position;
        c.Yaw = MathF.Atan2(-to.X, -to.Z);
        c.Velocity = c.Forward * Heft.CruiseAirspeed(c);
        c.SetState(CState.Graze);
        c.Altitude = 0f;
        c.IdleFor = 3f;
        bool down = false;
        float leftAt = -1f;
        foreach (int x in Film("landing", drive, c, 34f, t =>
        {
            down |= c.Landed;
            if (down && !c.Landed && leftAt < 0f) leftAt = t;
            return leftAt >= 0f && t > leftAt + 3f;
        })) yield return x;
    }

    IEnumerable<int> Swooping(Driver drive, Species s)
    {
        ClearSky();
        Species job = As(s, Behaviour.Predator, true);
        Creature c = W.Sim.Spawn(job, Ahead(38f, 26f));
        c.Position = new Vector3(c.Position.X, W.Player.Position.Y + 11f, c.Position.Z);
        c.Altitude = 11f;
        c.Noticed = W.Player;
        c.NoticedAt = W.Sim.Time;
        c.Awareness = 1f;
        c.State = CState.Chase;
        int strikes = 0;
        void Count(AttackEvent e)
        {
            if (e.Creature == c)
            {
                strikes++;
                GD.Print($"strike {strikes}: hit {e.Hit}, {e.Damage:F0} damage, at frame {_shot}");
            }
        }
        WildSim sim = W.Sim;
        sim.Attacked += Count;
        float after = -1f;
        foreach (int x in Film("swoop", drive, c, 40f, t =>
        {
            if (strikes >= 2 && after < 0f) after = t;
            return after >= 0f && t > after + 3f;
        })) yield return x;
        sim.Attacked -= Count;
    }

    IEnumerable<int> Hovering(Driver drive, Species s)
    {
        ClearSky();
        Species job = As(s, Behaviour.HarmlessOddity, false);
        job.Stats.Perception = 1f; // about its own business: dart, hold, dart
        Creature c = W.Sim.Spawn(job, Ahead(5f, -1f));
        c.Home = Ahead(6f);
        foreach (int x in Film("hover", drive, c, 12f)) yield return x;
    }

    IEnumerable<int> Stinging(Driver drive, Species s)
    {
        ClearSky();
        Species job = As(s, Behaviour.Predator, true);
        Creature c = W.Sim.Spawn(job, Ahead(9f, 3f));
        c.Noticed = W.Player;
        c.NoticedAt = W.Sim.Time;
        c.Awareness = 1f;
        c.State = CState.Chase;
        int stings = 0;
        void Count(AttackEvent e)
        {
            if (e.Creature == c)
            {
                stings++;
                GD.Print($"sting {stings}: hit {e.Hit}, at frame {_shot}");
            }
        }
        WildSim sim = W.Sim;
        sim.Attacked += Count;
        float after = -1f;
        foreach (int x in Film("sting", drive, c, 12f, t =>
        {
            if (stings >= 1 && after < 0f) after = t;
            return after >= 0f && t > after + 2.5f;
        })) yield return x;
        sim.Attacked -= Count;
    }
}
