#nullable enable
using System;

namespace PoiGenerator.Gen;

/// <summary>
/// The front door of the large places: each kind has a layout logic of its own, none of them "a
/// building with rooms". A site plan names which, and the tech level it was built at.
/// </summary>
public static class Grand
{
    public static void Compose(Canvas c, PoiLibrary lib, SiteDef site, Rng rng, LayoutHints? hints)
    {
        SitePart part = site.Parts[0];
        TechLevel tech = WorldProfile.Parse(part.Arg("tech", "mid"));
        switch (part.Arg("what"))
        {
            case "sewage": SewageWorks.Compose(c, rng, hints, tech); break;
            case "farm": FarmEstate.Compose(c, rng, hints, tech); break;
            case "complex": BrutalComplex.Compose(c, rng, hints, tech); break;
            case "market": Civic.Market(c, rng, hints, tech); break;
            case "hospital": Civic.Hospital(c, rng, hints, tech); break;
            case "water": Civic.WaterWorks(c, rng, hints, tech); break;
            case "bunker": Civic.Bunker(c, rng, hints, tech); break;
            case "lab" or "hull" or "hive" or "relay" or "homestead" or "minehead": FactionPlaces.Compose(c, lib, rng, hints, tech, part.Arg("what")); break;
            case "city": CityBlocks.Compose(c, rng, hints, tech, part); break;
            case "capital": CapitalHull.Compose(c, rng, hints, hints?.Tech ?? tech, part); break;
            default: throw new ArgumentException($"site {site.Id}: no large plan '{part.Arg("what")}'");
        }
    }
}
