using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Planets;

/// <summary>
/// The landform atlas: for each world, its landform map (each landform's key colour, shaded by
/// relief) beside its ordinary colour map, with the mix and the landforms really present
/// written to a key. Runs headless:
///   godot --headless --path prototypes/planet-generator res://tests/LandformAtlas.tscn -- --out=dir [--worlds=0:0:77,1:0:5] [--width=16]
/// Without --worlds it draws one world per archetype variant.
/// </summary>
public partial class LandformAtlas : Node
{
    const int Tile = 560, Gap = 10;

    public override void _Ready()
    {
        string outDir = ProjectSettings.GlobalizePath("res://atlas");
        string name = "landforms";
        int widthKm = 16;
        var specs = new List<PlanetSpec>();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--out=")) outDir = Path.GetFullPath(arg["--out=".Length..]);
            else if (arg.StartsWith("--name=")) name = arg["--name=".Length..];
            else if (arg.StartsWith("--width=")) widthKm = int.Parse(arg["--width=".Length..], CultureInfo.InvariantCulture);
        }
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--worlds=")) continue;
            foreach (string w in arg["--worlds=".Length..].Split(','))
            {
                string[] p = w.Split(':');
                specs.Add(PlanetSpec.Roll(ulong.Parse(p[2]), (Archetype)int.Parse(p[0]), int.Parse(p[1]), widthKm));
            }
        }
        if (specs.Count == 0)
            for (int a = 0; a < 5; a++)
            for (int v = 0; v < PlanetSpec.VariantCount; v++)
                specs.Add(PlanetSpec.Roll((ulong)(4100 + a * 17 + v * 5), (Archetype)a, v, widthKm));
        Directory.CreateDirectory(outDir);

        int tileH = Tile / 2;
        int width = 2 * Tile + 3 * Gap, height = specs.Count * (tileH + Gap) + Gap;
        var pixels = new byte[width * height * 3];
        for (int p = 0; p < width * height; p++) { pixels[p * 3] = 18; pixels[p * 3 + 1] = 16; pixels[p * 3 + 2] = 24; }
        var key = new StringBuilder("Landform atlas: each row is one world, its landform map on the left and its colour map on the right.\n\n");
        for (int t = 0; t < specs.Count; t++)
        {
            WorldMap map = PlanetGenerator.Generate(specs[t]);
            byte[] left = LandformLook.Map(map, Tile);
            byte[] right = ColorMap.Build(map, ArchetypeLook.For(specs[t]), Tile).Composite();
            int oy = Gap + t * (tileH + Gap);
            for (int y = 0; y < tileH; y++)
            {
                Array.Copy(left, y * Tile * 3, pixels, ((oy + y) * width + Gap) * 3, Tile * 3);
                Array.Copy(right, y * Tile * 3, pixels, ((oy + y) * width + 2 * Gap + Tile) * 3, Tile * 3);
            }
            var present = new StringBuilder();
            foreach (var (l, s) in map.Landforms.Present()) present.Append($"{LandformInfo.Name(l)} {s:P0}, ");
            string line = $"{t + 1,2}  {specs[t]}\n    mix: {map.Model.Mix}\n    present: {present.ToString().TrimEnd(',', ' ')}\n    land {map.LandFraction:P0}, {map.Rivers.Count} rivers, {map.Landforms.Deltas.Count} deltas, {map.GenerationMs:F0} ms";
            key.AppendLine(line);
            GD.Print(line);
        }
        key.AppendLine().AppendLine("Key colours:");
        foreach (Landform l in LandformInfo.All)
        {
            Rgb c = LandformLook.Color(l);
            key.AppendLine($"  {LandformInfo.Name(l),-15} #{Rgb.Byte(c.R):x2}{Rgb.Byte(c.G):x2}{Rgb.Byte(c.B):x2}");
        }
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgb8, pixels);
        string png = Path.Combine(outDir, name + ".png"), txt = Path.Combine(outDir, name + ".txt");
        Error result = image.SavePng(png);
        File.WriteAllText(txt, key.ToString());
        GD.Print(result == Error.Ok ? $"saved {png}" : $"could not save {png}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
