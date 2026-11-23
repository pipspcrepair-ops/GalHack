using GalHack.Stars;
using Planets.Gen;
using PlanetSurface.Skies;

namespace PlanetSurface.World;

/// <summary>
/// This world's place in the galaxy. The game's seed is the galaxy's, so a world finds its own star
/// in the catalogue: a real star of the class its sky already has (out at an arm's tip on the rim,
/// or near the depth a new game asked for), and that star's system seed is what its
/// <see cref="StarSystem"/> is built from, so the two agree on what is out there. The star's depth
/// is the world's, and the world rolls from it.
/// </summary>
public sealed partial class PlanetWorld
{
    /// <summary>The galaxy this world is in.</summary>
    public Galaxy Catalogue { get; private set; }

    /// <summary>This world's star in the catalogue.</summary>
    public int HomeStar { get; private set; } = -1;

    static partial void Chart(PlanetSpec spec, float? wanted, ref object chart, ref float depth)
    {
        SkySystem sky = SkySystem.For(spec);
        StarKind kind = SkyRig.KindOf(sky.Star);
        Galaxy galaxy = Galaxy.Make(spec.Seed);
        ulong key = Galaxy.HomeKey(spec.Seed, kind, sky.Nebula);
        int home = wanted is float d ? galaxy.Home(kind, key, sky.Nebula, d) : galaxy.Home(kind, key, sky.Nebula);
        chart = (galaxy, home);
        depth = galaxy.DepthOf(home);
    }

    partial void TakeChart(object chart)
    {
        if (chart is not (Galaxy galaxy, int home)) return;
        Catalogue = galaxy;
        HomeStar = home;
    }

    partial void FindHomeStar()
    {
        if (Catalogue != null) return;
        StarKind kind = SkyRig.KindOf(Sky.Star);
        Galaxy galaxy = Galaxy.Make(Spec.Seed);
        HomeStar = galaxy.Home(kind, Galaxy.HomeKey(Spec.Seed, kind, Sky.Nebula), Sky.Nebula);
        Catalogue = galaxy;
        Depth = galaxy.DepthOf(HomeStar);
    }

    /// <summary>What this world's system gives every other body of it: the galaxy, the star, the depth, the system and its light.</summary>
    public SystemShare Share()
    {
        FindHomeStar();
        return new SystemShare(Catalogue, HomeStar, Depth, StarSystem, Sky);
    }

    /// <summary>
    /// This world is another body of a system (a moon of a world, any world of a star's): the same galaxy, the same star
    /// and the same system on the same clock, seen from here. Set before the surface is laid out.
    /// </summary>
    public void ShareSystem(SystemShare of, Orbiter body)
    {
        Catalogue = of.Catalogue;
        HomeStar = of.Star;
        Depth = of.Depth;
        _system = of.System.SeenFrom(body);
        // Its sky's star is the system's; with no air to scatter it, the sky is black space by day too.
        Sky = Spec.Airless ? SkySystem.Vacuum(of.Light) : Sky with { Star = of.Light.Star, StarColor = of.Light.StarColor, SunEnergy = of.Light.SunEnergy, Bodies = of.Light.Bodies, DayMinutes = of.Light.DayMinutes };
    }

    partial void SystemSeedOf(ref ulong seed)
    {
        FindHomeStar();
        seed = Catalogue.SystemSeed(HomeStar);
    }
}

/// <summary>
/// What a system gives each body of it that is made as a world: the galaxy's catalogue, its star there and that star's
/// depth, the system's model (<see cref="StarSystem"/>), and the light and sky its anchor world has under that star.
/// </summary>
public sealed record SystemShare(Galaxy Catalogue, int Star, float Depth, StarSystem System, SkySystem Light);
