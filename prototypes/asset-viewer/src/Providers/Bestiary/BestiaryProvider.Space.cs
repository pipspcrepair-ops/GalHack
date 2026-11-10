using System;
using System.Collections.Generic;
using System.Linq;
using Wildlife;

namespace AssetViewer.Providers;

public sealed partial class BestiaryProvider
{
    /// <summary>The space fauna forms of the data, by habitat: swimmers in nebulae, belts and a giant's skies, leeches on a wreck, the leviathan in the void.</summary>
    static IEnumerable<AssetEntry> SpaceFauna()
    {
        SpaceFaunaData table = Zoo.Space;
        foreach (SpaceFormDef f in table.Forms)
        {
            string id = f.Id;
            string[] habitats = f.Habitats.Select(h => h.ToString()).ToArray();
            yield return new AssetEntry
            {
                Id = $"space/{id}",
                Group = $"Space/{(f.Rig == RigKind.Clinger ? "Hull clingers" : f.Swim switch { SwimStyle.Pulse => "Pulsing drifters", SwimStyle.Flap => "Flapping swimmers", _ => "Undulating giants" })}",
                Name = Zoo.Title(f.Noun),
                Randomisable = true,
                Options = new[] { Opt("Habitat", habitats, true) },
                Build = r =>
                {
                    Enum.TryParse(r.Get("Habitat", habitats[0]), out Habitat habitat);
                    var gen = new SpaceFaunaGenerator(Zoo.Space, Zoo.Data.Combat);
                    Species s = gen.Roll(r.Seed, id, habitat);
                    return new SpaceStage(s);
                },
            };
        }
    }
}
