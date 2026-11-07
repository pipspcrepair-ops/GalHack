using System;
using System.Collections.Generic;
using Godot;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>The settings and credits pages, driven through the real menu: <c>--view=settings --dir=folder</c>.</summary>
public sealed partial class Staging
{
    IEnumerable<int> SettingsView()
    {
        string dir = _args.GetValueOrDefault("dir", "user://");
        foreach (int x in Play.OpenMenu(MenuTab.Stats)) yield return x;
        foreach (int x in _drive.ClickWidget("options:settings")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        // A click halfway along a slider's track puts that volume at 50%, on the bus as well as in the settings.
        foreach (int x in _drive.ClickWidget("options:volume-music")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        float music = _game.Audio.Settings.Get(Game.AudioSettings.Music);
        float bus = Mathf.DbToLinear(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music")));
        GD.Print($"(music volume {music:F2}, bus {bus:F2}, master {_game.Audio.Settings.Get(Game.AudioSettings.Master):F2}, drones running {_game.Audio.Music.Running}, frames pushed {_game.Audio.Music.Pushed})");
        if (MathF.Abs(music - 0.5f) > 0.03f || MathF.Abs(bus - music) > 0.03f) throw new InvalidOperationException("the music slider did not set its bus");
        foreach (int x in Shot($"{dir}/settings.png")) yield return x;
        foreach (int x in _drive.ClickWidget("options:credits")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        foreach (int x in Shot($"{dir}/credits.png")) yield return x;
    }
}
