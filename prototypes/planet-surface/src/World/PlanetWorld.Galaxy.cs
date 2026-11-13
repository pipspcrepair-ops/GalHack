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

    /// <summary>
    /// This world is another body of a world's system (its moon): the same galaxy, the same star and
    /// the same system on the same clock, seen from here. Set before the surface is laid out.
    /// </summary>
    public void ShareSystem(PlanetWorld of, Orbiter body)
    {
        of.FindHomeStar();
        Catalogue = of.Catalogue;
        HomeStar = of.HomeStar;
        Depth = of.Depth;
        _system = of.StarSystem.SeenFrom(body);
        // Its sky's star is the system's; with no air to scatter it, the sky is black space by day too.
        Sky = Spec.Airless ? SkySystem.Vacuum(of.Sky) : Sky with { Star = of.Sky.Star, StarColor = of.Sky.StarColor, SunEnergy = of.Sky.SunEnergy, Bodies = of.Sky.Bodies, DayMinutes = of.Sky.DayMinutes };
    }

    partial void SystemSeedOf(ref ulong seed)
    {
        FindHomeStar();
        seed = Catalogue.SystemSeed(HomeStar);
    }
}
