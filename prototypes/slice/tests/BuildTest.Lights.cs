using System;
using System.Linq;
using GalHack.Structures;
using Godot;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

public partial class BuildTest
{
    /// <summary>A light's settings and a glowing paint save with a blueprint and load back the same; old data loads steady.</summary>
    void LightChecks()
    {
        Catalogue cat = Blocks;
        var s = new Structure(cat, cat.Grid(GridSize.Large));
        s.Set(Int3.Zero, Building.Make(s, cat.Types.First(t => t != null && t.IsCore), null, 0));
        BlockType lamp = cat.Types.First(t => t != null && t.IsLight);
        var rng = new Random();
        var cells = new[] { new Int3(1, 0, -1), new Int3(2, 0, -1), new Int3(3, 0, -1) };
        for (int x = 1; x <= 3; x++) s.Set(new Int3(x, 0, 0), Building.Make(s, cat.Type("steel"), null, 0));
        foreach (Int3 c in cells) s.Set(c, Building.Make(s, lamp, null, 0));
        // Random settings every run: no pinned colour or seed.
        LightSettings Random1() => LightSettings.Make(LightSettings.Palette[rng.Next(LightSettings.Palette.Length)],
            (LightAnim)rng.Next(5), LightSettings.Speeds[rng.Next(3)], rng.Next(128), LightSettings.Palette[rng.Next(LightSettings.Palette.Length)]);
        var set = cells.Select(_ => Random1()).ToArray();
        for (int i = 0; i < cells.Length; i++) s.SetLight(cells[i], set[i]);
        Building.PaintFace(s, new Int3(0, 0, 0), Dir.PosY, Paint.Lit(LampShape.Border, Random1()));
        Paint lit = Building.PaintOf(s, Int3.Zero, Dir.PosY).Value;

        Structure back = Blueprint.Load(Blueprint.Save(s), cat, out var errors);
        bool lightsSame = back != null && cells.Select((c, i) => back.LightAt(c) == set[i] || set[i] == LightSettings.Steady(lamp.GlowColor)).All(b => b);
        Check(errors.Count == 0 && lightsSame, "a light's colour, second colour, mode, speed and seed survive a blueprint save and load");
        Check(back != null && Building.PaintOf(back, Int3.Zero, Dir.PosY) == lit && lit.IsLamp, "glowing paint (shape and settings) survives a blueprint save and load");

        const string Old = "schema galhack.blueprint\nversion 1\nname \"Old\"\ngrid large\nmode capital\ngravity on\nblock 0 0 0 core\nblock 1 0 0 steel\nblock 1 0 -1 light\n";
        Structure old = Blueprint.Load(Old.Replace("core", cat.Types.First(t => t != null && t.IsCore).Id).Replace("light", lamp.Id), cat, out var oldErrors);
        Check(old != null && oldErrors.Count == 0 && old.LightAt(new Int3(1, 0, -1)) == LightSettings.Steady(lamp.GlowColor), "old blueprints load their lights steady, in the block's own glow colour");

        var anim = LightSettings.Make(0xff3322, LightAnim.Pulse, 1f, 0);
        float lo = 9, hi = 0;
        for (double t = 0; t < 1.0; t += 0.05) { float k = anim.Intensity(t); lo = Math.Min(lo, k); hi = Math.Max(hi, k); }
        Check(hi - lo > 0.6f && LightSettings.Steady(0xffffff).Intensity(3.3) == 1f, "a pulse breathes between dim and full; steady holds");
    }
}

