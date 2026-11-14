#nullable enable
using System.Linq;
using GalHack.Structures;

namespace PoiGenerator.Gen;

/// <summary>
/// A place's lights set to its feel, in one call at the end of a spawn, through the lights API
/// (<see cref="PoiLights"/>): lived-in places glow warm, hostile ones pulse red, dying ones keep a few
/// lamps that flicker or have failed, and high-tech places carry some of their light as glowing strips
/// painted on the walls beside their lamps. The feel comes from the place's state, who is in it and its
/// kit's own mood; nothing is placed by hand.
/// </summary>
public static class PlaceLights
{
    static readonly Rgb Warm = new(0xff, 0xc8, 0x88);

    public enum Feel : byte { AsBuilt, Dying, Hostile, LivedIn }

    /// <summary>How a place feels, from its state, its mood and who is in it.</summary>
    public static Feel Of(PoiInstance inst) =>
        inst.Mood == Mood.Hostile || inst.State == PoiState.Infested ? Feel.Hostile
        : inst.Townsfolk is { People.Count: > 0 } || inst.Occupancy is { Main: Occupant.Squatters or Occupant.Keepers or Occupant.Cult } ? Feel.LivedIn
        : inst.State is PoiState.Abandoned or PoiState.Ruined ? Feel.Dying
        : Feel.AsBuilt;

    public static void Tune(PoiInstance inst)
    {
        Feel feel = Of(inst);
        Rgb kit = inst.Layout.Lamp ?? inst.Kit.Light;
        bool named = inst.Kit.Mood.Length > 0;
        bool high = (inst.Tech ?? inst.Layout.Family.Techs.Select(WorldProfile.Parse).DefaultIfEmpty(TechLevel.Mid).Max()) == TechLevel.High;
        var rng = new Rng(Hash.Combine(inst.SpawnSeed, 0x11647UL));
        int strips = 0;
        // A plain building's lamps hang from its ceilings: where a ceiling came down, its lamp went with it.
        if (inst.Layout.Extras.Plain.Count > 0)
            foreach (LightSpot l in inst.Lights.Where(l => !BlockInfo.IsFullCube(inst.Blocks.Get(l.Cell + Int3.Up).Type)))
                l.Mode = LightMode.Dead;
        foreach (LightSpot l in inst.Lights.Where(l => l.Mode is not (LightMode.Bloom or LightMode.Beacon or LightMode.Dead)).ToList())
        {
            LightMood mood = named ? PoiLights.MoodFor(inst) : feel switch
            {
                Feel.Hostile => LightMood.Hostile,
                Feel.Dying => LightMood.Dying,
                _ => high ? LightMood.Cold : LightMood.Warm,
            };
            if (feel == Feel.Dying && !named)
            {
                // A dying place: most of what's left still burns, a few flicker, a few have failed.
                float r = rng.Float();
                // (Never the only light a room has: a room with no window keeps its lamp, flickering.)
                if (r < 0.15f && Daylit(inst, l.Module)) { l.Mode = LightMode.Dead; l.Style = null; continue; }
                if (r > 0.4f) mood = high ? LightMood.Cold : LightMood.Warm;
            }
            Rgb colour = feel == Feel.LivedIn ? kit.Lerp(Warm, 0.6f) : l.Mode == LightMode.Emergency ? l.Color : kit;
            PoiLights.Set(l, PoiLights.Style(mood, colour, l.Cell));
            // High tech carries some of its light as paint: a strip on the wall beside every few lamps.
            if (high && feel != Feel.Hostile && rng.Chance(0.3f) && Strip(inst, l, PoiLights.Style(mood, colour, l.Cell))) strips++;
        }
    }

    /// <summary>Whether a room has a window in its walls (a lamp's room; a lamp with none is in the open, lit by day).</summary>
    static bool Daylit(PoiInstance inst, int module) =>
        module < 0 || module >= inst.Layout.Modules.Count || inst.Layout.Modules[module].Cells.Any(c => Dirs.Horizontal.Any(d => inst.Blocks.Get(c + d.Vec()) is { Type: BlockType.Pane, Role: Role.Window }));

    /// <summary>A glowing strip three cells long on the nearest wall beside a lamp, facing into the room. False if no wall stands beside it.</summary>
    static bool Strip(PoiInstance inst, LightSpot l, LightSettings style)
    {
        BlockGrid g = inst.Blocks;
        foreach (Dir d in Dirs.Horizontal)
        {
            Int3 wall = l.Cell + d.Vec();
            if (!g.Get(l.Cell).IsAir || g.Get(wall) is not { Type: BlockType.Cube, Role: Role.Wall }) continue;
            Int3 along = (d is Dir.East or Dir.West ? Dir.South : Dir.East).Vec();
            if (g.Get(wall - along) is not { Type: BlockType.Cube } || g.Get(wall + along) is not { Type: BlockType.Cube }) continue;
            PoiLights.PaintStrip(inst, wall - along, wall + along, d.Opposite(), style);
            return true;
        }
        return false;
    }
}
