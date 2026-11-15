#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A house's rooms finished by what each is for (the reference bar: period and modern house plans, docs/floorplan-refs
/// 02-housing-commerce): every room's floor laid in its own colour (the finish book gives the pattern by purpose: a
/// kitchen's tiles, a bedroom's carpet, a hall's boards), its walls in their own paint and every ceiling in the house's
/// one; a light fitting's lamp in the middle of every ceiling; and the ceiling by role and storey (<see cref="Hut.Ceiling"/>).
/// Rooms of one purpose take the next colour of their purpose, so no two read alike. The furniture is the decorations
/// library's (Gen/RoomDecor); the doors in its inner doorways are every place's (<see cref="Canvas"/>).
/// </summary>
public static partial class Homes
{
    /// <summary>Each purpose's floor colours, a room taking the next of its purpose's: tiles, boards, carpet, concrete.</summary>
    static readonly Dictionary<string, Rgb[]> FloorColours = new()
    {
        ["kitchen"] = new[] { new Rgb(0xd2, 0xc8, 0xb0), new Rgb(0x96, 0x5c, 0x44), new Rgb(0x70, 0x74, 0x74), new Rgb(0xb4, 0xb4, 0xac) },
        ["bathroom"] = new[] { new Rgb(0xd4, 0xd8, 0xd4), new Rgb(0xa4, 0xbc, 0xc4), new Rgb(0xb0, 0xc2, 0xae) },
        ["hall"] = new[] { new Rgb(0x8a, 0x4c, 0x3a), new Rgb(0x5c, 0x44, 0x34), new Rgb(0x86, 0x80, 0x76), new Rgb(0x6e, 0x60, 0x50), new Rgb(0x9a, 0x84, 0x64), new Rgb(0x4e, 0x52, 0x50) },
        ["living-room"] = new[] { new Rgb(0x7a, 0x58, 0x3a), new Rgb(0x8c, 0x6a, 0x48), new Rgb(0x6a, 0x4e, 0x36) },
        ["bedroom"] = new[] { new Rgb(0x80, 0x70, 0x5e), new Rgb(0x6e, 0x5e, 0x74), new Rgb(0x5c, 0x6c, 0x7c), new Rgb(0x7c, 0x5c, 0x54), new Rgb(0x66, 0x72, 0x5a) },
        ["study"] = new[] { new Rgb(0x6a, 0x50, 0x38), new Rgb(0x5e, 0x48, 0x34) },
        ["store"] = new[] { new Rgb(0x8a, 0x86, 0x80), new Rgb(0x7a, 0x62, 0x48) },
        ["garage"] = new[] { new Rgb(0x7a, 0x7a, 0x76), new Rgb(0x6c, 0x6a, 0x64) },
    };

    /// <summary>Wall paints a house's rooms are done in, each room the next it hasn't used.</summary>
    static readonly Rgb[] WallPaints =
    {
        new(0xe6, 0xdc, 0xc6), new(0xe8, 0xe6, 0xde), new(0xc8, 0xd4, 0xbc), new(0xc4, 0xd0, 0xd8), new(0xe4, 0xd8, 0xa8),
        new(0xdc, 0xc4, 0xbc), new(0xcd, 0xca, 0xc2), new(0xd6, 0xc0, 0x98), new(0xb8, 0xc8, 0xc4), new(0xd0, 0xb8, 0xc8),
        new(0xe0, 0xd0, 0xb8), new(0xbc, 0xc0, 0xa8), new(0xc8, 0xbc, 0xa4),
    };

    /// <summary>The lamp's colour by the age that hung it: an oil flame, a filament, a cold panel.</summary>
    static Rgb LampColour(TechLevel tech) => tech switch { TechLevel.Low => new Rgb(0xff, 0xb0, 0x60), TechLevel.High => new Rgb(0xe8, 0xf0, 0xff), _ => new Rgb(0xff, 0xd9, 0xa0) };

    sealed partial class Hut
    {
        /// <summary>
        /// Its rooms finished by purpose (see <see cref="Homes"/>), from the house's own stream (a fork: the plan's draws stay
        /// as they were). <paramref name="used"/> carries the colours its other building took, so a garage's differ.
        /// </summary>
        public void Interior(Rng r, TechLevel tech, Dictionary<string, int> used)
        {
            int paint = r.Range(0, WallPaints.Length);
            Rgb ceiling = tech == TechLevel.Low ? new Rgb(0xd8, 0xcc, 0xb4) : new Rgb(0xe8, 0xe4, 0xda);
            C.X.Ceiling ??= ceiling;
            foreach (Room room in Rooms.Where(x => x.Module >= 0 && x.Kind != ModuleKind.Stair))
            {
                // The floor: its purpose's next colour, laid on every storey (the ground floor's plates recoloured, an upper
                // floor's laid over the slab), never over a stairwell.
                Rgb[] floors = FloorColours.GetValueOrDefault(room.Purpose) ?? new[] { S.Floor };
                int k = used.GetValueOrDefault(room.Purpose);
                used[room.Purpose] = k + 1;
                Rgb floor = floors[(k + r.Range(0, 2)) % floors.Length];
                foreach (var (u, v) in room.Cells())
                {
                    if (_stair.Contains((u, v, room.Storey))) continue;
                    Int3 at = P(u, Walk(room.Storey), v);
                    if (C.Blocks.TryGetValue(at, out Block laid) && laid.Type == BlockType.Plate && laid.Role == Role.Floor) { laid.Color = floor; C.Blocks[at] = laid; }
                    else if (!C.Solid(at) && C.Get(at + Int3.Down) is { } under && BlockInfo.IsFullCube(under.Type)) C.Blocks.Pave(at, new Block(BlockType.Plate, Material.Concrete, floor, Role.Floor));
                }
                // The walls: the house's next paint; the ceilings all the house's one.
                C.X.Paints[room.Module] = WallPaints[paint++ % WallPaints.Length];
                Ceiling(room, tech, C.X.Ceiling.Value);
                // The lamp in the middle of the ceiling, as high as the ceiling is there (the library hangs the fitting of the house's age there).
                int cu = (room.U0 + room.U1) / 2, cv = (room.V0 + room.V1) / 2, wide = Math.Max(room.U1 - room.U0, room.V1 - room.V0) + 1;
                int top = Walk(room.Storey) + 1;
                while (top < Walk(room.Storey) + 12 && !C.Solid(P(cu, top + 1, cv))) top++;
                if (C.Get(P(cu, top + 1, cv)).Type is BlockType.Half or BlockType.Beam) top++; // (hung at a dropped panel's face, or between the beams)
                C.X.Lights.Add(new LightSpot { Cell = P(cu, top, cv), Color = LampColour(tech), Module = room.Module, Storey = room.Storey, Energy = 1.1f, Range = wide * 0.9f + 2f, Mode = LightMode.Steady });
            }
        }

        /// <summary>
        /// The ceiling by role and storey (period and modern house sections, docs/floorplan-refs 02): the top storey under a pitched
        /// roof open to its slope over the bedrooms, study and living room (a vaulted ceiling); an old house's joists over its
        /// ground-floor kitchen, living room and hall, a newer living room coffered; the upper storeys, the bathrooms and stores
        /// half a metre lower under a dropped ceiling; a garage its pipe run.
        /// </summary>
        void Ceiling(Room room, TechLevel tech, Rgb paint)
        {
            if (room.Purpose == "garage" && room.Shape == null) { PipeRun(room, tech); return; }
            if (room.Storey == Storeys - 1 && room.Purpose is "bedroom" or "study" or "living-room" && Vault(room, paint)) return;
            if (room.Purpose is "bathroom" or "store" || room.Storey > 0 && room.Purpose != "hall") { Drop(room, paint); return; }
            if (room.Shape != null) return; // (a round room's ceiling spans it whole)
            if (tech == TechLevel.Low && room.Purpose is "kitchen" or "living-room" or "hall") Beams(room, new Block(BlockType.Beam, Material.Timber, new Rgb(0x5a, 0x44, 0x30), Role.Decor), false);
            else if (room.Purpose == "living-room") Beams(room, new Block(BlockType.Beam, Material.Concrete, paint, Role.Decor), true);
        }

        /// <summary>Beams under the ceiling across a room's short span, one every two metres wall to wall (an old house's timber joists); <paramref name="coffered"/>, along its length too (a plastered coffer). None over a stair.</summary>
        void Beams(Room room, Block beam, bool coffered)
        {
            int y = Ring(room.Storey) - 1;
            bool shortU = room.U1 - room.U0 <= room.V1 - room.V0;
            foreach (bool acrossU in coffered ? new[] { shortU, !shortU } : new[] { shortU })
                for (int a = (acrossU ? room.V0 : room.U0) + 1; a < (acrossU ? room.V1 : room.U1); a += 2)
                {
                    var line = Enumerable.Range(acrossU ? room.U0 : room.V0, (acrossU ? room.U1 - room.U0 : room.V1 - room.V0) + 1).Select(b => acrossU ? (b, a) : (a, b)).ToList();
                    if (line.Any(c => _stair.Contains((c.Item1, c.Item2, room.Storey)) || !C.Solid(P(c.Item1, y + 1, c.Item2)))) continue;
                    foreach (var (u, v) in line.Where(c => C.Get(P(c.Item1, y, c.Item2)).IsAir)) C.Set(P(u, y, v), beam with { Facing = _f.Turn(acrossU ? Dir.East : Dir.South) });
                }
        }

        /// <summary>A ceiling dropped half a metre (a panel under the slab), stepping back up round the windows so none is cut across.</summary>
        void Drop(Room room, Rgb paint)
        {
            int y = Ring(room.Storey) - 1;
            var panel = new Block(BlockType.Half, Material.Concrete, paint, Role.Floor, Dir.Up);
            foreach (var (u, v) in room.Cells())
                if (!_stair.Contains((u, v, room.Storey)) && C.Get(P(u, y, v)).IsAir && C.Solid(P(u, y + 1, v)) && !Dirs.Horizontal.Any(h => C.Get(P(u, y, v) + h.Vec()).Role == Role.Window))
                    C.Set(P(u, y, v), panel);
        }

        /// <summary>
        /// The room open to the pitched roof over it: its ceiling slab out, the roof's underside laid in slopes under the deck so it
        /// reads as one plane from eaves to ridge, and the walls round it carried up to the roof. Only under a plain slope of this
        /// house's roof (a slope or the ridge over every cell); else nothing changes.
        /// </summary>
        bool Vault(Room room, Rgb paint)
        {
            int ring = Ring(room.Storey);
            var cells = room.Cells().ToList();
            if (cells.Any(c => _stair.Contains((c.U, c.V, room.Storey)) || !_top.TryGetValue(c, out int t) || t < ring + 1 || C.Get(P(c.U, t, c.V)).Type is not (BlockType.Ramp or BlockType.Cube))) return false;
            var deck = new Block(BlockType.Cube, S.RoofM, paint, Role.Roof);
            foreach (var (u, v) in cells)
            {
                int t = _top[(u, v)];
                Block roof = C.Get(P(u, t, v));
                for (int y = ring; y < t - 1; y++) C.Clear(P(u, y, v));
                C.Set(P(u, t - 1, v), deck);
                C.Set(P(u, t - 2, v), roof.Type == BlockType.Ramp ? new Block(BlockType.Chamfer, S.RoofM, paint, Role.Roof, roof.Facing.Opposite()) : deck);
            }
            // The walls round it up to the roof: a bedroom under the slope is shut off from the next one's.
            var own = cells.ToHashSet();
            foreach (var (u, v) in cells)
                foreach (var w in new[] { (u - 1, v), (u + 1, v), (u, v - 1), (u, v + 1) })
                {
                    if (own.Contains(w) || !Foot.Contains(w) || Edge(w.Item1, w.Item2) || !_top.TryGetValue(w, out int t)) continue;
                    for (int y = ring + 1; y < t; y++)
                        if (C.Get(P(w.Item1, y, w.Item2)).IsAir) C.Set(P(w.Item1, y, w.Item2), Cube(S.WallM, S.Wall, Role.Wall));
                }
            return true;
        }

        /// <summary>A service pipe along a room's longest wall under its ceiling, its whole length.</summary>
        void PipeRun(Room room, TechLevel tech)
        {
            bool alongU = room.U1 - room.U0 >= room.V1 - room.V0;
            int y = Ring(room.Storey) - 1;
            var pipe = new Block(BlockType.Pipe, tech == TechLevel.Low ? Material.Rust : Material.Steel, new Rgb(0x6a, 0x6e, 0x70), Role.Decor, _f.Turn(alongU ? Dir.East : Dir.South));
            for (int a = alongU ? room.U0 : room.V0; a <= (alongU ? room.U1 : room.V1); a++)
            {
                Int3 at = alongU ? P(a, y, room.V1) : P(room.U1, y, a);
                if (!C.Solid(at)) C.Set(at, pipe);
            }
        }
    }
}
