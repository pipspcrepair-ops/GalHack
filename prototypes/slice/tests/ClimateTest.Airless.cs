using System;
using System.Collections.Generic;
using Godot;
using Slice.Game;
using Slice.Surface;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>
/// --airless: a fresh airless world started on (reached from the surface, never through orbit) reads as vacuum,
/// as one landed on from orbit does (MoonTest): a black sky with no weather, no oxygen out of doors, the suit's
/// helmet shut and the visor's vacuum icon lit.
/// </summary>
public partial class ClimateTest
{
    /// <summary>A fresh world of any type that rolled airless (a cratered barren body).</summary>
    static WorldChoice AirlessWorld()
    {
        for (int i = 0; i < 4096; i++)
        {
            WorldChoice w = WorldChoice.Fresh("random");
            if (w.Spec().Airless) return w;
        }
        throw new InvalidOperationException("no airless world in 4,096 rolls");
    }

    IEnumerable<int> Airless()
    {
        GD.Print($"-- an airless world, started on: {Zone.Planet.Spec} --");
        Check(Zone.Planet.Spec.Airless && Zone.Planet.Sky.Density < SurfaceWeather.NoAir && Zone.Weather.Sim == null && Zone.Wildlife.Sim == null,
            $"its sky has no air (density {Zone.Planet.Sky.Density:F2}), no weather and no life, as its moons' do");
        Check(_game.Underworld != null && _game.Tool.Places != null && _game.Places.Fauna == null, "the start finished setting up all the same: places, caves and the underworld, with no creatures in them");
        foreach (int x in OnOpenGround(Me.WorldFeet)) yield return x;
        if (!_game.Suit.Sim.Player.Gear.HelmetOn) _game.Suit.Sim.ToggleHelmet();
        foreach (int x in _drive.Wait(10)) yield return x;
        var e = _game.Suit.Last.Exposure;
        IconState vacuum = _game.Visor.SleeperIcons()[1];
        Check(e.OxygenPercent == 0 && e.Volume == null && _game.Suit.InVacuum && vacuum.Icon == HudIcon.Vacuum && vacuum.On,
            $"out of doors the suit reads no air ({e.OxygenPercent:F0}% at {e.Temperature:F0} °C, {e.Place}) and the visor's vacuum icon is lit");
        Check(!_game.Suit.Dead && _game.Suit.OxygenShare > 0, $"the helmet shut, the sleeper breathes the suit's own air ({_game.Suit.OxygenShare:P0})");
    }
}
