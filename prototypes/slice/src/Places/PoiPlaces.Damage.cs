using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using Godot;
using PoiGenerator.Gen;

namespace Slice.Places;

/// <summary>
/// Weapons on places (owner, 17 Nov 2026: "So they can damage anything, really"): a shot chips the block it meets and it
/// breaks away once spent, a door or a piece of dressing is worn as the Sleeper tool's blast wears it (<see cref="Wear"/>),
/// and a blast does all of it over its reach, falling off with distance. Whatever is left joined to nothing goes with what
/// broke (nothing floats), and the cubes are rebuilt a few a frame (<see cref="Place.Rebuild"/>).
/// </summary>
public partial class PoiPlaces
{
    /// <summary>Blocks broken by shots, blasts and the tool, and those that went with them for want of anything holding them (tests).</summary>
    public int BlocksBroken { get; private set; }
    public int BlocksFallen { get; private set; }

    /// <summary>A weapon's hit on a place: a hazard, a hidden wall's panel or a core as the tool's bolt has them (<see cref="Shot"/>), a door or a piece worn, any other block chipped.</summary>
    public void Struck(Hit h, float damage)
    {
        if (damage <= 0f) return;
        if (h.Piece?.Piece.Blast != null || h.Piece == null && h.Door == null && (h.Place.Inst.Blocks.Get(h.Cell).Type == BlockType.Core || h.Place.SecretAt(h.Cell) != null))
            Shot(h, damage);
        else if (h.Piece != null || h.Door != null) Wear(h, damage);
        else
        {
            var gone = new List<Int3>(1);
            Hurt(h.Place, h.Cell, damage, gone);
            Removed(h.Place, gone);
        }
    }

    /// <summary>
    /// A blast at a world point: in every standing place within its reach, the blocks hurt by how near they are (the nearest go
    /// first), the doors and the dressing worn the same, a hidden wall's panel struck, and every hazard in it set to follow after
    /// <see cref="ChainDelay"/>; the wrecks within it worn too.
    /// </summary>
    public void Blast(Vector3 at, float radius, float damage)
    {
        if (damage <= 0f || radius <= 0f) return;
        float Falloff(float d) => damage * (1f - d / radius * 0.75f);
        foreach (Place p in Standing.ToList())
        {
            if (!p.Ready) continue;
            Vector3 local = p.FromWorld(at);
            BlockGrid g = p.Inst.Blocks;
            if (local.X < g.Min.X - radius || local.Y < g.Min.Y - radius || local.Z < g.Min.Z - radius
                || local.X > g.Min.X + g.Size.X + radius || local.Y > g.Min.Y + g.Size.Y + radius || local.Z > g.Min.Z + g.Size.Z + radius) continue;
            var gone = new List<Int3>();
            var secrets = new HashSet<SecretRoom>();
            int r = (int)MathF.Ceiling(radius);
            for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                    for (int x = -r; x <= r; x++)
                    {
                        var c = new Int3((int)MathF.Floor(local.X) + x, (int)MathF.Floor(local.Y) + y, (int)MathF.Floor(local.Z) + z);
                        float d = new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f).DistanceTo(local);
                        if (d > radius) continue;
                        if (p.SecretAt(c) is SecretRoom s) { if (secrets.Add(s)) Hurt(p, s, Falloff(d)); }
                        else Hurt(p, c, Falloff(d), gone);
                    }
            foreach (DecoHit piece in p.PiecesNear(local, radius).ToList())
            {
                float d = piece.Node.GlobalPosition.DistanceTo(at);
                if (d > radius) continue;
                if (piece.Piece.Blast != null) Fuse(p, piece);
                else Wear(new Hit(p, default, null, piece), Falloff(d));
            }
            foreach (Place.Door door in p.Doors.ToList())
                if (door.Lift < 0 && door.Root.GlobalPosition.DistanceTo(at) is var d && d < radius) Wear(new Hit(p, door.Cells[0], door, null), Falloff(d));
            Removed(p, gone);
        }
        foreach (SalvageSite s in _salvage.Values.ToList())
            if (GodotObject.IsInstanceValid(s.Body) && s.Body.GlobalPosition.DistanceTo(at) is var d && d < radius) Work(s, Falloff(d));
    }

    /// <summary>A hazard set to go up after one near it (once).</summary>
    void Fuse(Place q, DecoHit h)
    {
        if (_fuses.Any(f => f.Place == q && f.Module == h.Room.Module && f.Index == h.Index)) return;
        _fuses.Add((q, h.Room.Module, h.Index, _now + ChainDelay * (1f + 0.3f * (_fuses.Count % 3))));
    }

    /// <summary>
    /// Damage to one block; spent, it is air and listed in <paramref name="gone"/>. Every block takes it, a cache's, a terminal's,
    /// and a core's too (a core is only ever destroyed, never taken: <see cref="CoreDestroyed"/>); a door leaf is
    /// worn as a whole (<see cref="Wear"/>) and a hidden wall's panel breaks as one (<see cref="Hurt(Place, SecretRoom, float)"/>).
    /// </summary>
    void Hurt(Place p, Int3 c, float damage, List<Int3> gone)
    {
        Block b = p.Inst.Blocks.Get(c);
        if (b.IsAir || b.Type == BlockType.Door) return;
        PlaceMemory m = p.Memory;
        float done = m.BlockDamage.GetValueOrDefault(c) + damage;
        if (done < BlockInfo.HitPoints(b)) { m.BlockDamage[c] = done; return; }
        m.BlockDamage.Remove(c);
        p.Inst.Blocks.Set(c, Block.Air);
        gone.Add(c);
        if (b.Type == BlockType.Core) CoreDestroyed?.Invoke(p);
    }

    /// <summary>
    /// Blocks gone from a place (broken, shot, salvaged): whatever they held that no longer reaches the ground breaks away with them,
    /// a door left in no wall and a piece of dressing left on nothing come away (a hazard goes up), and the cubes are rebuilt.
    /// </summary>
    void Removed(Place p, List<Int3> gone)
    {
        if (gone.Count == 0) return;
        BlocksBroken += gone.Count;
        List<Int3> loose = Loose(p, gone);
        foreach (Int3 c in loose)
        {
            p.Inst.Blocks.Set(c, Block.Air);
            p.Memory.BlockDamage.Remove(c);
        }
        BlocksFallen += loose.Count;
        gone.AddRange(loose);
        if (loose.Count > 0)
        {
            Vector3 mid = p.WorldOf(loose[loose.Count / 2]);
            Zone.Dust?.Puff(mid, new Color(0.45f, 0.42f, 0.38f, 0.8f), 1f + MathF.Min(3f, loose.Count * 0.05f));
        }
        foreach (Place.Door door in p.Doors.ToList())
            if (door.Lift < 0 && door.Cells.Any(c => p.Inst.Blocks.Get(c).IsAir)) p.TakeDoor(door);
        Unheld(p, gone);
        p.Rebuild(gone);
    }

    /// <summary>Dressing by the gaps with nothing left under it (hung up: nothing beside or over it) comes away; a hazard goes up.</summary>
    void Unheld(Place p, List<Int3> gone)
    {
        var near = new HashSet<Int3>(gone);
        BlockGrid g = p.Inst.Blocks;
        Int3 lo = gone.Aggregate(Int3.Min), hi = gone.Aggregate(Int3.Max);
        var mid = new Vector3((lo.X + hi.X + 1) * 0.5f, (lo.Y + hi.Y + 1) * 0.5f, (lo.Z + hi.Z + 1) * 0.5f);
        foreach (DecoHit h in p.PiecesNear(mid, new Vector3(hi.X - lo.X + 1, hi.Y - lo.Y + 1, hi.Z - lo.Z + 1).Length() * 0.5f + 2f).ToList())
        {
            if (h.Room.Plan.Placements[h.Index] is not { Support: < 0 } at) continue; // it goes with what it stands on
            Int3 c = p.CellAt(h.Node.GlobalPosition + Vector3.Up * 0.05f);
            if (!near.Contains(c) && !Dirs.All.Any(d => near.Contains(c + d.Vec()))) continue;
            bool held = at.Y < 0.3f ? !g.Get(c + Int3.Down).IsAir || !g.Get(c).IsAir : Dirs.All.Any(d => !g.Get(c + d.Vec()).IsAir);
            if (held) continue;
            if (h.Piece.Blast != null) Fuse(p, h);
            else
            {
                p.Memory.DecoDamage.Remove((h.Room.Module, h.Index));
                p.TakePiece(h);
            }
        }
    }

    /// <summary>
    /// What no longer reaches the ground once some blocks went: every part joined (face to face, through other blocks) to the
    /// gaps' sides, walked downward first, that meets neither the lowest storey nor the rock round the place (a station's anchors
    /// in space) — the rule a ruin's collapse has (<see cref="Structure.Floating"/>), or the terrain under it, from the change only, so a chip costs a few cells' walk.
    /// </summary>
    List<Int3> Loose(Place p, List<Int3> gone)
    {
        PoiInstance inst = p.Inst;
        BlockGrid g = inst.Blocks;
        var store = Zone.Planet.Terrain.Store;
        CellMask occupied = inst.Layout.Occupied;
        bool space = inst.Layout.Extras.InSpace;
        HashSet<Int3> anchors = space ? new HashSet<Int3>(LayoutCheck.StationAnchors(inst.Layout)) : null;
        bool Rock(Int3 q) => !space && q.Y <= Assembler.GroundY && !occupied[q];
        // (and what stands on the real ground under it: a place on a slope stands on the terrain above its own rock line)
        bool Grounded(Int3 q) => space ? anchors.Contains(q) : q.Y == g.Min.Y || Dirs.All.Any(d => Rock(q + d.Vec())) || OnGround(q);
        bool OnGround(Int3 q)
        {
            Vector3 under = p.WorldOf(q, -0.5f);
            return store.MaterialAt(new TerrainDigging.Terrain.Int3((int)MathF.Floor(under.X), (int)MathF.Floor(under.Y), (int)MathF.Floor(under.Z))) != TerrainDigging.Terrain.CellMaterial.Air;
        }
        bool Solid(Int3 q) => g.InBounds(q) && !g.Get(q).IsAir;
        var held = new HashSet<Int3>();
        var seen = new HashSet<Int3>();
        var loose = new List<Int3>();
        var part = new List<Int3>();
        var stack = new Stack<Int3>();
        foreach (Int3 c in gone)
            foreach (Dir side in Dirs.All)
            {
                Int3 start = c + side.Vec();
                if (!Solid(start) || held.Contains(start) || !seen.Add(start)) continue;
                part.Clear();
                stack.Clear();
                stack.Push(start);
                bool grounded = false;
                while (stack.Count > 0 && !grounded)
                {
                    Int3 q = stack.Pop();
                    part.Add(q);
                    if (Grounded(q)) { grounded = true; break; }
                    // Down is pushed last, so taken first: a wall's walk goes straight to its foot.
                    for (int i = Walk.Length - 1; i >= 0; i--)
                    {
                        Int3 n = q + Walk[i];
                        if (held.Contains(n)) { grounded = true; break; }
                        if (Solid(n) && seen.Add(n)) stack.Push(n);
                    }
                }
                if (grounded)
                {
                    held.UnionWith(part);
                    held.UnionWith(stack);
                }
                else loose.AddRange(part);
            }
        return loose;
    }

    static readonly Int3[] Walk = { Int3.Down, new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1), Int3.Up };
}
