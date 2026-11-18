using System;
using System.Collections.Generic;
using GalHack.Arms;
using Godot;

namespace Slice.Tests;

/// <summary>
/// --fire=1: at each stop inside, a gun fired as the owner fires it, the eye turning slowly as it goes: the auto rifle
/// held into the walls and furniture, a pistol shot as fast as a finger pulls, and the rifle up into the ceiling or sky.
/// Each is a stage of its own ("inside fire ..."), so the inside budget check covers it.
/// </summary>
public partial class PlacePerf
{
    static readonly (string Name, string Weapon, bool Up)[] Bursts =
    {
        ("auto walls", "line-rifle", false),
        ("single walls", "tack-pistol", false),
        ("auto up", "line-rifle", true),
    };

    /// <summary>A finger pulls a semi-automatic's trigger at most this often, s.</summary>
    const float FingerPull = 0.25f;

    IEnumerable<int> Fire(string stop)
    {
        if (!_args.ContainsKey("fire")) yield break;
        foreach (var (name, weapon, up) in Bursts)
        {
            WeaponDef d = _game.Arms.Table.Get(weapon);
            float every = d.Trigger == Trigger.Auto ? d.SecondsPerShot : MathF.Max(d.SecondsPerShot, FingerPull);
            float dt = 1f / Engine.PhysicsTicksPerSecond, wait = 0f;
            int shots = 0, ticks = 0;
            // The last stage's stir (and its report's own frame) gone first.
            foreach (int x in _drive.Wait(120)) yield return x;
            Begin();
            while (_times.Count < Frames)
            {
                // A full turn every 8 s, level into the walls or steeply up.
                float a = ticks++ * dt * MathF.Tau / 8f;
                Vector3 eye = _game.Survivor.WorldEye(), dir = new(MathF.Sin(a), 0f, MathF.Cos(a));
                _drive.Face(eye + (up ? dir * 3f + Vector3.Up * 10f : dir * 10f - Vector3.Up * 0.6f));
                if ((wait -= dt) <= 0f)
                {
                    _game.Arms.Fire(d, 1);
                    shots++;
                    wait += every;
                }
                yield return 0;
            }
            End($"{stop} fire {name}");
            GD.Print($"PERF {stop} fire {name}: {shots} shots of the {d.Id}");
        }
    }
}
