using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Terrain;
using PlanetSurface.Terrain;
using TerrainDigging.Terrain;
using N = System.Numerics;

namespace Slice.Surface;

/// <summary>
/// The one way the slice changes the ground: the survival tool's digs and the physics module's
/// craters, both through <see cref="PlanetTerrain"/> (terrain-digging's rules, on every copy of a
/// cell across a seam). Each edit rebuilds the streamed chunks it touched, clears the flora that
/// stood in it, and wakes and stuck-checks the bodies nearby.
/// </summary>
public sealed class PlanetEdits : ITerrainEditor
{
    public readonly PlanetTerrain Terrain;
    readonly TerrainStreamer _streamer;
    readonly PhysicsWorld _world;

    /// <summary>Every edit that removed something, as it happened.</summary>
    public event Action<PlanetEdit> Edited;

    public PlanetEdits(PlanetTerrain terrain, TerrainStreamer streamer, PhysicsWorld world)
    {
        Terrain = terrain;
        _streamer = streamer;
        _world = world;
    }

    /// <summary>A crash: the physics world notifies for its own craters.</summary>
    public CraterResult Crater(N.Vector3 center, float radius)
    {
        PlanetEdit edit = Terrain.Crater(center, radius);
        Region changed = Commit(edit, notify: false);
        return new CraterResult(edit.Removed.Count, edit.Bedrock + edit.Outside, changed);
    }

    /// <summary>The survival tool's dig: a small ball of cells around one cell.</summary>
    public PlanetEdit Dig(Int3 center, float radius)
    {
        PlanetEdit edit = Terrain.Dig(center, radius);
        Commit(edit, notify: true);
        return edit;
    }

    /// <summary>
    /// A block of a base reaching into the ground (terrain-digging's rule 4): the cells whose
    /// centres lie inside it are dug like any others (every copy across a seam, logged, flora
    /// cleared, bodies woken), and its volume is registered, with its copies across the seams, so
    /// the ground is drawn and collides flush against its faces. Harmless to repeat for a standing block.
    /// </summary>
    public PlanetEdit ClearForStructure(VolumeKey key, StructureVolume volume)
    {
        var dirty = new HashSet<Int3>();
        int copy = 0;
        for (int a = -1; a <= 1; a++)
        for (int b = -1; b <= 1; b++)
        {
            bool self = a == 0 && b == 0;
            StructureVolume v = self ? volume : volume.Offset(new N.Vector3(a * Terrain.Width, 0f, b * Terrain.Height));
            CellBox box = v.CellBounds;
            if (!self && !Terrain.InFrame(box.Min) && !Terrain.InFrame(box.Max - Int3.One)) continue;
            dirty.UnionWith(Terrain.Store.AddVolume(key.AsCopy(self ? 0 : ++copy), v));
        }
        PlanetEdit edit = Terrain.DigCells(volume.CellsInside());
        Commit(edit, notify: true);
        _streamer.MarkDirty(dirty); // the volume changes how the ground near it is drawn, dug or not
        return edit;
    }

    /// <summary>A block taken off: the ground it cleared stays cleared, drawn as the dug hole it is.</summary>
    public void ReleaseStructure(VolumeKey key) => _streamer.MarkDirty(Terrain.Store.RemoveVolume(key));

    /// <summary>Every block of one structure let go at once (the base taken down).</summary>
    public void ReleaseStructures(int owner) => _streamer.MarkDirty(Terrain.Store.RemoveVolumes(owner));

    /// <summary>Particular cells under the tool's rules (tests, a cut plant's roots).</summary>
    public PlanetEdit DigCells(IEnumerable<Int3> cells)
    {
        PlanetEdit edit = Terrain.DigCells(cells);
        Commit(edit, notify: true);
        return edit;
    }

    Region Commit(PlanetEdit edit, bool notify)
    {
        if (edit.Removed.Count == 0) return default;
        _streamer.MarkDirty(edit.DirtyChunks);
        _streamer.MarkDug(edit.Removed);
        N.Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (RemovedCell r in edit.Removed)
        {
            N.Vector3 c = r.Cell.ToVector();
            min = N.Vector3.Min(min, c);
            max = N.Vector3.Max(max, c + N.Vector3.One);
        }
        var region = new Region(min - N.Vector3.One, max + N.Vector3.One);
        Edited?.Invoke(edit);
        if (notify) _world.NotifyChanged(region);
        return region;
    }
}
