using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Stars;
using PlanetSurface.Skies;
using Planets.Gen;

namespace Slice.Orbit;

/// <summary>
/// What any star of the catalogue holds, worked out on demand and kept: its system (<see cref="Of"/>) and a short
/// summary of it. Only the orbits are built: no world's terrain is ever generated for a summary.
/// </summary>
public sealed class StarProfile
{
    readonly Galaxy _galaxy;
    readonly int _home;
    readonly StarSystem _atHome;
    readonly Dictionary<int, StarSystem> _systems = new();
    readonly Dictionary<int, string[]> _lines = new();

    /// <param name="home">The start's star, whose system is the start world's own (<paramref name="atHome"/>), or -1.</param>
    public StarProfile(Galaxy galaxy, int home = -1, StarSystem atHome = null) => (_galaxy, _home, _atHome) = (galaxy, home, atHome);

    /// <summary>
    /// The system of a catalogue star that isn't the start's: built from its own seed (<see cref="Galaxy.SystemSeed"/>)
    /// and class by the start system's recipe (<see cref="StarSystem.ForStar"/>). The one way every star gets its system:
    /// the map's summaries, a fold's destination and the sky from there all read it.
    /// </summary>
    public static StarSystem Of(Galaxy galaxy, int id) => StarSystem.ForStar(galaxy.SystemSeed(id), SkyRig.ClassOf(galaxy.Stars[id].Kind));

    /// <summary>The system of a star: the start world's own at the start's star.</summary>
    public StarSystem System(int id)
    {
        if (id == _home && _atHome != null) return _atHome;
        if (_systems.TryGetValue(id, out StarSystem known)) return known;
        return _systems[id] = Of(_galaxy, id);
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
