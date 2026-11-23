using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlanetSurface.Skies;
using PlanetSurface.World;
using Slice.Game;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>
/// The space round a body with no ground (a gas giant, the star, a belt): the same zone as any world's orbit, its frame
/// the body's own, the body drawn by space's sky and solid at its true radius, with no globe, no crossing and no surface.
/// A giant's moons are added to it as any world's are (<see cref="AddMoon"/>).
/// </summary>
public partial class OrbitZone
{
    /// <summary>The sky its space is lit by when it has no world below of its own: its system's anchor world's (the one star).</summary>
    SkySystem _lit;

    /// <summary>The body has ground: a world, with a globe and a surface under its crossing.</summary>
    public bool HasGround => Below != null;

    /// <summary>The space round <paramref name="frame"/>, a body of <paramref name="system"/>, lit as <paramref name="light"/> (its anchor world's sky) has it.</summary>
    public OrbitZone(Orbiter frame, StarSystem system, SkySystem light, GameData data, OrbitRules rules)
    {
        Name = "Orbit";
        Data = data;
        Rules = rules;
        Spec = system.Home.Spec;
        Gap = rules.Crossing.Gap;
        _lit = light;
        StarSystem model = system.SeenFrom(frame);
        float r = (float)Math.Max(frame.Radius, 1.0);
        Home = new Celestial(frame, home: true)
        {
            Seen = model, GlobeRadius = r, AtmosphereRadius = r, ShellRadius = r, Ceiling = 0f, Reach = float.PositiveInfinity,
            Map = new GlobeMap(Math.Tau * r, Math.PI * r, r),
        };
        Bodies.Add(Home);
        Anchor = Home;
        _preparing = Task.FromResult<(GlobePaint, List<DerelictPlan>)>((null, new List<DerelictPlan>()));
    }

    /// <summary>
    /// Moves everything in the zone so a point becomes its origin: out at a star's high orbit (a thousand km from its
    /// centre) the numbers near where you fly stay small, and a hull drawn there doesn't shake. Nothing changes to the eye.
    /// </summary>
    public void Recentre(N.Vector3 point)
    {
        foreach (Celestial c in Bodies)
        {
            c.Shift -= point;
            c.ShiftWas = c.Shift;
            Placed(c);
        }
        foreach (var (_, shell) in _solids) shell.Position -= new Godot.Vector3(point.X, point.Y, point.Z);
        foreach (GalHack.Physics.Body b in Physics.Bodies)
        {
            if (b.Frame != null) continue; // riders stay where they are aboard
            b.Position -= point;
            Host.BindingOf(b)?.Teleport();
        }
        foreach (Slice.Items.Lootable l in Loot.All)
        {
            l.Position -= new Godot.Vector3(point.X, point.Y, point.Z);
            if (l.Node != null) l.Node.GlobalPosition = l.Position;
        }
    }
}
