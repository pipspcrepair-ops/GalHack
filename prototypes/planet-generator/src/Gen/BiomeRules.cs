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
            if ((flags & CellFlags.Water) != 0) biome = Sea(def, e, t, map.Special[k]);
            else if ((flags & (CellFlags.River | CellFlags.Riparian)) != 0 && map.Slope[k] < 0.6f && t > 0.08f)
                biome = def.RiverbankBiome;
            else
            {
                float patch = map.Model.Patches(map.CellX(i), map.CellY(j));
                var cell = new Cell(e, t, map.Humidity[k], map.Slope[k], map.Special[k], patch, (flags & CellFlags.Coast) != 0);
                biome = Hint(def, (Landform)map.Landform[k], cell) ?? Land(def, cell);
            }
            map.Biome[k] = (byte)biome;
        }
    }

    /// <summary>
    /// The landform's biome hint, where it has one: a dune sea is sand on any world, wetlands are
    /// swamp (or spore marsh, or glow moss), sea cliffs and fjord walls are bare rock or ice.
    /// Only existing biomes are used, in the archetype's own palette where it has one, so every
    /// world keeps its colours. Null leaves the cell to the variant's own rules.
    /// </summary>
    static Biome? Hint(ArchetypeDef def, Landform landform, Cell c)
    {
        Archetype a = def.Archetype;
        bool crystal = a == Archetype.Alien && def.Variant == 0;
        Biome Rock() => a switch
        {
            Archetype.Arid => Biome.RedRock,
            Archetype.Ice => Biome.BlueIce,
            Archetype.Lava => Biome.Obsidian,
            Archetype.Alien => crystal ? Biome.CrystalSpires : Biome.Mycelium,
            _ => Biome.Cliff,
        };
        switch (landform)
        {
            case Landform.DuneSea:
                if (c.Slope > 0.9f) return Rock();
                return a switch
                {
                    Archetype.Ice => Biome.Snowfield,
                    Archetype.Lava => Biome.Ash,
                    Archetype.Alien => crystal ? Biome.Chalk : Biome.Mycelium,
                    _ => c.T < 0.2f ? Biome.Snow : c.Special > 0.55f ? Biome.Erg : Biome.Dunes,
                };
            case Landform.RockyDesert:
                if (c.Slope > 0.5f) return Rock();
                return a switch
                {
                    Archetype.Ice => Biome.IceTundra,
                    Archetype.Lava => c.Patch > 0.35f ? Biome.Sulfur : Biome.Ash,
                    Archetype.Alien => crystal ? Biome.CrystalPlain : Biome.Mycelium,
                    Archetype.Arid => c.Patch > 0.42f ? Biome.SaltFlat : Biome.MesaTop,
                    _ => c.Patch > 0.42f ? Biome.SaltFlat : Biome.Scrub,
                };
            case Landform.Badlands:
                if (c.Slope > 0.6f) return Rock();
                return a switch
                {
                    Archetype.Ice => Biome.IceTundra,
                    Archetype.Lava => Biome.Ash,
                    Archetype.Alien => crystal ? Biome.Chalk : Biome.Mycelium,
                    _ => Biome.Badlands,
                };
            case Landform.Steppe:
                return a switch
                {
                    // Golden open grass, few trees; scrub where it is driest, tundra where it is cold.
                    Archetype.Temperate => c.T < 0.25f ? Biome.Tundra : c.H < 0.3f ? Biome.Scrub : Biome.Meadow,
                    Archetype.Arid => c.Slope > 0.5f ? Rock() : Biome.Scrub,
                    Archetype.Ice => Biome.IceTundra,
                    _ => null,
                };
            case Landform.Wetlands or Landform.Delta:
                if (c.T < 0.1f && a != Archetype.Lava) return a == Archetype.Ice ? Biome.IceTundra : Biome.Tundra;
                return a switch
                {
                    Archetype.Temperate => landform == Landform.Delta && c.Patch > 0.1f ? Biome.Riverbank : Biome.Swamp,
                    Archetype.Arid => Biome.Oasis,
                    Archetype.Ice => def.Variant == 1 && c.Special > 0.6f ? Biome.HotSpring : Biome.IceTundra,
                    Archetype.Lava => Biome.Basalt,
                    _ => crystal ? Biome.GlowMoss : Biome.SporeMarsh,
                };
            case Landform.IcePlain:
                if (a == Archetype.Ice) return c.E > 340f ? Biome.Glacier : Biome.Snowfield;
                return a == Archetype.Temperate ? Biome.Snow : null;
            case Landform.Mesas or Landform.Canyonlands:
                if (c.Slope > (a == Archetype.Arid ? 0.42f : 0.6f)) return Rock();
                return a switch
                {
                    Archetype.Arid => c.H > 0.46f ? Biome.Scrub : c.Special > 0.55f ? Biome.MesaTop : Biome.Badlands,
                    Archetype.Lava => Biome.Basalt,
                    Archetype.Alien => crystal ? Biome.Chalk : null,
                    _ => null,
                };
            case Landform.VolcanicField:
                if (a == Archetype.Lava) return null;
                if (c.Slope > 0.6f) return Biome.Obsidian;
                return a == Archetype.Ice && def.Variant == 1 && c.Special > 0.56f ? Biome.Sinter : Biome.Ash;
            case Landform.BasaltPlains:
                return a == Archetype.Lava ? null : Biome.Basalt;
            case Landform.Fjords or Landform.CoastalCliffs or Landform.Karst:
                return c.Slope > 0.65f ? Rock() : null;
            default:
                return null;
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
