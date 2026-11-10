using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using Godot;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// The working states of a structure's blocks, found by scanning it (never by name): every door opening and
/// shutting, lamps cycling the light modes or switched off, the whole structure's power cut and restored,
/// thrusters firing, and the game's damage playing over all of it.
/// </summary>
public static class Working
{
    public static void Add(StructureAsset a)
    {
        Structure s = a.Structure;
        var doors = s.Blocks().Where(x => s.TypeOf(x.block).IsDoor).Select(x => x.pos).ToList();
        var lamps = s.Blocks().Where(x => s.TypeOf(x.block).IsLight).Select(x => x.pos).ToList();
        bool devices = s.Blocks().Any(x => !s.TypeOf(x.block).IsArmor);

        Damaging.Add(a);

        if (doors.Count > 0)
        {
            float clock = 0f;
            a.AddAnimation("Doors open and close", () => clock = 0f, dt =>
            {
                clock += dt;
                bool open = (int)(clock / 1.6f) % 2 == 0;
                foreach (Int3 d in doors) SetFlag(s, d, BlockFlags.Open, open);
            });
        }
        if (lamps.Count > 0)
        {
            float clock = 0f;
            var modes = Enum.GetValues<LightAnim>();
            a.AddAnimation("Lamps cycle modes", () => clock = 0f, dt =>
            {
                clock += dt;
                var mode = modes[(int)(clock / 2.5f) % modes.Length];
                foreach (Int3 l in lamps)
                {
                    if (!s.TryGet(l, out Block cur)) continue;
                    LightSettings want = LightSettings.Make(s.TypeOf(cur).GlowColor, mode, 0.5f, 0, mode == LightAnim.Fade ? 0x4aa8e0u : (uint?)null);
                    SetFlag(s, l, BlockFlags.Off, false);
                    if (s.LightAt(l) != want) s.SetLight(l, want);
                }
            });
            a.AddAnimation("Lamps off and on", () => clock = 0f, dt =>
            {
                clock += dt;
                bool off = (int)(clock / 1.4f) % 2 == 1;
                foreach (Int3 l in lamps) SetFlag(s, l, BlockFlags.Off, off);
            });
        }
        if (devices)
        {
            float clock = 0f;
            a.AddAnimation("Power cut and restored", () => clock = 0f, dt =>
            {
                clock += dt;
                a.Powered = (int)(clock / 2f) % 2 == 0;
            }, () => a.Powered = true);
        }
        ThrusterFx.Add(a);
    }

    static void SetFlag(Structure s, Int3 cell, BlockFlags flag, bool on)
    {
        if (!s.TryGet(cell, out Block b) || ((b.Flags & flag) != 0) == on) return;
        b.Flags = on ? b.Flags | flag : b.Flags & ~flag;
        s.Set(cell, b);
    }
}
