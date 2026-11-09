using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Stars;
using PlanetSurface.Skies;
using Planets.Gen;

namespace Slice.Orbit;

/// <summary>
/// What any star of the catalogue holds, worked out on demand and kept: its system from the star's
/// own seed (<see cref="Galaxy.SystemSeed"/>) through the <see cref="StarSystem"/> model, and a short
/// summary of it. Only the orbits are built: no world's terrain is ever generated for a summary.
/// </summary>
public sealed class StarProfile
{
    readonly Galaxy _galaxy;
    readonly Dictionary<int, StarSystem> _systems = new();
    readonly Dictionary<int, string[]> _lines = new();

    public StarProfile(Galaxy galaxy) => _galaxy = galaxy;

    /// <summary>The star class the system model builds for a catalogue kind.</summary>
    public static StarClass ClassOf(StarKind k) => k switch
    {
        StarKind.BrownDwarf or StarKind.RedDwarf or StarKind.Protostar => StarClass.RedDwarf,
        StarKind.OrangeDwarf or StarKind.RedGiant => StarClass.Orange,
        StarKind.YellowDwarf => StarClass.Yellow,
        StarKind.WhiteStar or StarKind.WhiteDwarf => StarClass.White,
        _ => StarClass.BlueWhite,
    };

    /// <summary>The system of a star, or null if the model can't make one.</summary>
    public StarSystem System(int id)
    {
        if (_systems.TryGetValue(id, out StarSystem known)) return known;
        StarSystem made = null;
        try
        {
            ulong seed = _galaxy.SystemSeed(id);
            var spec = new PlanetSpec(seed, (Archetype)(int)((seed >> 7) % 5), 0, 0);
            SkySystem sky = SkySystem.For(spec) with { Star = ClassOf(_galaxy.Stars[id].Kind) };
            made = StarSystem.For(spec, sky, spec.WidthMeters, seed);
        }
        catch (Exception) { }
        return _systems[id] = made;
    }

    /// <summary>Words for a body's temperature.</summary>
    static string Warmth(double kelvin) => kelvin switch { < 120 => "frozen", < 220 => "cold", < 320 => "temperate", < 500 => "hot", _ => "scorched" };

    /// <summary>What is in the system, a line each: its worlds, moons and belts and their types, nearest the star first.</summary>
    public string[] Lines(int id)
    {
        if (_lines.TryGetValue(id, out string[] known)) return known;
        StarSystem s = System(id);
        var lines = new List<string>();
        if (s == null) lines.Add("NO SURVEY");
        else
        {
            int moons = s.Bodies.Count(b => b.Kind == OrbiterKind.Moon), belts = s.Bodies.Count(b => b.Kind == OrbiterKind.Belt);
            int giants = s.Bodies.Count(b => b.Kind == OrbiterKind.GasGiant), rocky = s.Bodies.Count(b => b.Kind == OrbiterKind.Planet);
            lines.Add($"{rocky} rocky, {giants} giant{(giants == 1 ? "" : "s")}, {moons} moon{(moons == 1 ? "" : "s")}, {belts} belt{(belts == 1 ? "" : "s")}");
            foreach (Orbiter b in s.Bodies.Where(b => b.Parent == s.Star && b.Kind != OrbiterKind.Star).OrderBy(b => b.Orbit).Take(8))
            {
                int own = s.Bodies.Count(m => m.Parent == b);
                string kind = b.Kind switch { OrbiterKind.GasGiant => "gas giant", OrbiterKind.Belt => "asteroid belt", _ => $"{Warmth(b.Temperature)} world" };
                lines.Add($"{b.Orbit / StarSystem.AuMetres:0.0} AU  {kind}{(own > 0 ? $", {own} moon{(own == 1 ? "" : "s")}" : "")}");
            }
        }
        return _lines[id] = lines.ToArray();
    }
}
