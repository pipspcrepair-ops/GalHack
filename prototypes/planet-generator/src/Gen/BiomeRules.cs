using System;

namespace Planets.Gen;

/// <summary>
/// Picks a biome for every cell from temperature, humidity, altitude, slope and the archetype's
/// special field. Each archetype variant has its own short rule list: that, more than anything,
/// is what makes a dune world read differently from a canyon world.
/// </summary>
static class BiomeRules
{
    readonly record struct Cell(float E, float T, float H, float Slope, float Special, float Patch, bool Coast);

    public static void Run(WorldMap map)
    {
        ArchetypeDef def = map.Def;
        for (int k = 0; k < map.Count; k++)
        {
            int i = k % map.Width, j = k / map.Width;
            byte flags = map.Flags[k];
            float e = map.Elevation[k], t = map.Temperature[k];
            Biome biome;
            if ((flags & CellFlags.Cap) != 0) biome = def.Cap == CapStyle.IceCliff ? Biome.IceCap : Biome.StormCap;
            else if ((flags & CellFlags.Water) != 0) biome = Sea(def, e, t, map.Special[k]);
            else if ((flags & (CellFlags.River | CellFlags.Riparian)) != 0 && map.Slope[k] < 0.6f && t > 0.08f)
                biome = def.RiverbankBiome;
            else
            {
                float patch = map.Model.Patches(map.CellX(i), map.CellY(j));
                var cell = new Cell(e, t, map.Humidity[k], map.Slope[k], map.Special[k], patch, (flags & CellFlags.Coast) != 0);
                biome = Land(def, cell);
            }
            map.Biome[k] = (byte)biome;
        }
    }

    static Biome Sea(ArchetypeDef def, float e, float t, float special)
    {
        bool frozen = def.SeaFreezes && t < (def.Archetype == Archetype.Ice ? 0.26f : 0.06f) && special < 0.55f;
        if (frozen) return Biome.FrozenSea;
        return e < -30f ? Biome.DeepSea : Biome.ShallowSea;
    }

    static Biome Land(ArchetypeDef def, Cell c) => (def.Archetype, def.Variant) switch
    {
        (Archetype.Temperate, 0) => Meadow(c),
        (Archetype.Temperate, _) => Rainforest(c),
        (Archetype.Arid, 0) => Dunes(c),
        (Archetype.Arid, _) => Canyon(c),
        (Archetype.Ice, 0) => Glacier(c),
        (Archetype.Ice, _) => HotSpring(c),
        (Archetype.Lava, 0) => Volcanic(c),
        (Archetype.Lava, _) => LavaOcean(c),
        (Archetype.Alien, 0) => Crystal(c),
        _ => Fungal(c),
    };

    static Biome Meadow(Cell c)
    {
        if (c.T < 0.1f) return Biome.Snow;
        if (c.Slope > 0.85f) return Biome.Cliff;
        if (c.E < 3.5f && c.Coast) return Biome.Beach;
        if (c.T < 0.24f) return c.H > 0.5f ? Biome.PineForest : Biome.Tundra;
        if (c.E > 460f) return c.Slope > 0.45f ? Biome.Cliff : Biome.Tundra;
        if (c.H > 0.66f) return c.T < 0.4f ? Biome.PineForest : Biome.Forest;
        if (c.Special > 0.6f) return Biome.Meadow;
        return Biome.Grassland;
    }

    static Biome Rainforest(Cell c)
    {
        if (c.T < 0.14f) return Biome.Snow;
        if (c.Slope > 0.95f) return Biome.Cliff;
        if (c.E < 3f && c.Coast) return Biome.Beach;
        if (c.H > 0.86f && c.Slope < 0.08f && c.E < 60f) return Biome.Swamp;
        if (c.E > 400f || c.T < 0.42f) return Biome.CloudForest;
        if (c.H < 0.5f) return Biome.Grassland;
        return c.Special > 0.55f ? Biome.Jungle : Biome.Rainforest;
    }

    static Biome Dunes(Cell c)
    {
        if (c.Slope > 0.55f) return Biome.RedRock;
        if (c.E < 5f && c.Coast) return Biome.SaltFlat;
        if (c.H > 0.5f) return Biome.Scrub;
        if (c.E > 170f) return Biome.Badlands;
        return c.Special > 0.6f ? Biome.Erg : Biome.Dunes;
    }

    static Biome Canyon(Cell c)
    {
        if (c.Slope > 0.42f) return Biome.RedRock;
        if (c.E < 6f && c.Coast) return Biome.SaltFlat;
        if (c.H > 0.46f) return Biome.Scrub;
        return c.Special > 0.55f ? Biome.MesaTop : Biome.Badlands;
    }

    static Biome Glacier(Cell c)
    {
        if (c.Slope > 0.7f) return Biome.BlueIce;
        if (c.T > 0.2f && c.E < 90f) return Biome.IceTundra;
        if (c.E > 340f || c.T < 0.06f) return Biome.Glacier;
        return Biome.Snowfield;
    }

    static Biome HotSpring(Cell c)
    {
        if (c.Special > 0.74f && c.Slope < 0.3f) return Biome.HotSpring;
        if (c.Special > 0.56f) return Biome.Sinter;
        if (c.Special > 0.4f || (c.T > 0.3f && c.E < 60f)) return Biome.IceTundra;
        if (c.Slope > 0.75f) return Biome.BlueIce;
        if (c.E > 380f) return Biome.Glacier;
        return Biome.Snowfield;
    }

    static Biome Volcanic(Cell c)
    {
        if (c.Special > 0.8f) return Biome.LavaField;
        if (c.Slope > 0.6f) return Biome.Obsidian;
        if (c.Special > 0.3f) return Biome.Ash;
        if (c.Patch > 0.35f && c.E < 150f && c.Slope < 0.3f) return Biome.Sulfur;
        return Biome.Basalt;
    }

    static Biome LavaOcean(Cell c)
    {
        if (c.Coast && c.E < 6f) return Biome.Obsidian;
        if (c.Special > 0.8f) return Biome.LavaField;
        if (c.Slope > 0.6f) return Biome.Obsidian;
        if (c.Special > 0.3f) return Biome.Ash;
        if (c.Patch > 0.4f && c.Slope < 0.3f) return Biome.Sulfur;
        return Biome.Basalt;
    }

    static Biome Crystal(Cell c)
    {
        if (c.Slope > 0.65f) return Biome.CrystalSpires;
        if (c.H > 0.62f) return Biome.GlowMoss;
        if (c.E > 300f) return Biome.Chalk;
        return c.Special > 0.55f ? Biome.CrystalSpires : Biome.CrystalPlain;
    }

    static Biome Fungal(Cell c)
    {
        if (c.H > 0.88f && c.Slope < 0.12f && c.E < 50f) return Biome.SporeMarsh;
        if (c.Slope > 0.7f || c.E > 260f) return Biome.Mycelium;
        if (c.Special > 0.5f || c.H > 0.62f) return Biome.FungalForest;
        return Biome.GlowMoss;
    }
}
