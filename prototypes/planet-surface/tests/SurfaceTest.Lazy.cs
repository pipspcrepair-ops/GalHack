using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using PlanetSurface.World;

namespace PlanetSurface;

public partial class SurfaceTest
{
    /// <summary>
    /// A world loads with its places laid out and seated, not built: each is built when first wanted (PoiBuilds), on
    /// whatever thread, and must come out the same whenever that is. The same fresh world twice, its places built
    /// one by one in order on one thread and all at once in reverse order on many, block for block; and no place
    /// was built while the world loaded.
    /// </summary>
    async Task LazyChecks()
    {
        var spec = PlanetSpec.Roll((ulong)new Random().NextInt64(), Archetype.Temperate, 0);
        PlanetWorld a = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
        int builtAtLoad = a.Pois.Count(p => p.Built != null);
        GD.Print($"      {spec}: {a.Pois.Count} places; {PlanetWorld.LastTimes}");
        PlanetWorld b = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
        await Task.Run(() => { foreach (var p in a.Pois) p.Build(); });
        await Task.Run(() => Parallel.ForEach(b.Pois.Reverse(), p => p.Build()));
        int differ = 0;
        string first = "";
        for (int i = 0; i < a.Pois.Count; i++)
        {
            var x = a.Pois[i].Built.Blocks.Blocks().ToList();
            var y = b.Pois[i].Built.Blocks.Blocks().ToList();
            if (x.Count == y.Count && !x.Zip(y).Any(t => t.First.Cell != t.Second.Cell || !t.First.Block.Equals(t.Second.Block))) continue;
            differ++;
            if (first.Length == 0) first = $" (first: {a.Pois[i].Family.Id}, {x.Count} blocks against {y.Count})";
        }
        Check(builtAtLoad == 0, $"a world loads without building its places: {builtAtLoad} of {a.Pois.Count} built");
        Check(differ == 0, $"a place is the same built in order on one thread or out of order on many: {differ} of {a.Pois.Count} differ{first}");
    }
}
