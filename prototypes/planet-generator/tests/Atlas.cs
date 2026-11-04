using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Planets;

/// <summary>
/// The human blind test: flat maps of 5 archetypes x 4 seeds in shuffled order, with no labels,
/// plus a separate answer key. Runs headless (it only needs Image):
///   godot --headless --path prototypes/planet-generator res://tests/Atlas.tscn -- --out=atlas
/// Can you sort the 20 maps into five archetypes before opening the key?
/// </summary>
public partial class Atlas : Node
{
    const int Columns = 4, Rows = 5, TileWidth = 420, Gap = 12;

    public override void _Ready()
    {
        string outDir = ProjectSettings.GlobalizePath("res://atlas");
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--out=")) outDir = Path.GetFullPath(arg["--out=".Length..]);
        Directory.CreateDirectory(outDir);

        var specs = new List<PlanetSpec>();
        var rng = new Rng(0xA71A5);
        for (int a = 0; a < 5; a++)
        for (int s = 0; s < 4; s++)
        {
            ulong seed = 3000 + rng.NextULong() % 100000;
            specs.Add(PlanetSpec.Roll(seed, (Archetype)a, s % PlanetSpec.VariantCount, 16));
        }
        for (int i = specs.Count - 1; i > 0; i--) // shuffle so position gives nothing away
        {
            int j = rng.Next(i + 1);
            (specs[i], specs[j]) = (specs[j], specs[i]);
        }

        int tileHeight = TileWidth / 2;
        int width = Columns * TileWidth + (Columns + 1) * Gap, height = Rows * tileHeight + (Rows + 1) * Gap;
        var pixels = new byte[width * height * 3];
        for (int p = 0; p < width * height; p++)
        {
            pixels[p * 3] = 18;
            pixels[p * 3 + 1] = 16;
            pixels[p * 3 + 2] = 24;
        }

        var key = new StringBuilder("Blind-test key for atlas.png. Tiles are numbered left to right, top to bottom.\n\n");
        for (int t = 0; t < specs.Count; t++)
        {
            var map = PlanetGenerator.Generate(specs[t]);
            byte[] tile = ColorMap.Build(map, ArchetypeLook.For(specs[t]), TileWidth).Composite();
            int ox = Gap + t % Columns * (TileWidth + Gap), oy = Gap + t / Columns * (tileHeight + Gap);
            for (int y = 0; y < tileHeight; y++)
                Array.Copy(tile, y * TileWidth * 3, pixels, ((oy + y) * width + ox) * 3, TileWidth * 3);
            key.AppendLine($"{t + 1,2}  (row {t / Columns + 1}, column {t % Columns + 1})  {specs[t]}");
            GD.Print($"tile {t + 1} of {specs.Count} ({map.GenerationMs:F0} ms)");
        }

        var image = Image.CreateFromData(width, height, false, Image.Format.Rgb8, pixels);
        string png = Path.Combine(outDir, "atlas.png"), txt = Path.Combine(outDir, "atlas_key.txt");
        Error result = image.SavePng(png);
        File.WriteAllText(txt, key.ToString());
        GD.Print(result == Error.Ok ? $"saved {png} and {txt}" : $"could not save {png}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
