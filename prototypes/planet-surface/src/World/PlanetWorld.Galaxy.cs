using GalHack.Stars;
using PlanetSurface.Skies;

namespace PlanetSurface.World;

/// <summary>
/// This world's place in the galaxy. The game's seed is the galaxy's, so a world finds its own star
/// in the catalogue: a real rim star of the class its sky already has, and that star's system seed
/// is what its <see cref="StarSystem"/> is built from, so the two agree on what is out there.
/// </summary>
public sealed partial class PlanetWorld
{
    /// <summary>The galaxy this world is in.</summary>
    public Galaxy Catalogue { get; private set; }

    /// <summary>This world's star in the catalogue.</summary>
    public int HomeStar { get; private set; } = -1;

    partial void FindHomeStar()
    {
        if (Catalogue != null) return;
        StarKind kind = SkyRig.KindOf(Sky.Star);
        Galaxy galaxy = Galaxy.Make(Spec.Seed);
        HomeStar = galaxy.Home(kind, Galaxy.HomeKey(Spec.Seed, kind, Sky.Nebula), Sky.Nebula);
        Catalogue = galaxy;
    }

    partial void SystemSeedOf(ref ulong seed)
    {
        FindHomeStar();
        seed = Catalogue.SystemSeed(HomeStar);
    }
}
