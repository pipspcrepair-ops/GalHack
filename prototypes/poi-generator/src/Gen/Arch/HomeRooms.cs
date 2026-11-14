#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A house's rooms finished by what each is for (the reference bar: period and modern house plans, docs/floorplan-refs
/// 02-housing-commerce): every room's floor laid in its own colour (the finish book gives the pattern by purpose: a
/// kitchen's tiles, a bedroom's carpet, a hall's boards), its walls in their own paint and every ceiling in the house's
/// one; a light fitting's lamp in the middle of every ceiling; and the structure by role: an old house's joists left
/// showing over its kitchen, living room and hall, a garage's pipe run along its ceiling. Rooms of one purpose take the
/// next colour of their purpose, so no two read alike. The furniture is the decorations library's (Gen/RoomDecor).
/// </summary>
public static partial class Homes
{
    /// <summary>Each purpose's floor colours, a room taking the next of its purpose's: tiles, boards, carpet, concrete.</summary>
    static readonly Dictionary<string, Rgb[]> FloorColours = new()
    {
        ["kitchen"] = new[] { new Rgb(0xd2, 0xc8, 0xb0), new Rgb(0x96, 0x5c, 0x44), new Rgb(0x70, 0x74, 0x74), new Rgb(0xb4, 0xb4, 0xac) },
        ["bathroom"] = new[] { new Rgb(0xd4, 0xd8, 0xd4), new Rgb(0xa4, 0xbc, 0xc4), new Rgb(0xb0, 0xc2, 0xae) },
        ["hall"] = new[] { new Rgb(0x8a, 0x4c, 0x3a), new Rgb(0x5c, 0x44, 0x34), new Rgb(0x86, 0x80, 0x76) },
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
        new(0xdc, 0xc4, 0xbc), new(0xcd, 0xca, 0xc2), new(0xd6, 0xc0, 0x98), new(0xb8, 0xc8, 0xc4),
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
                // The lamp in the middle of the ceiling (the library hangs the fitting of the house's age there).
                int cu = (room.U0 + room.U1) / 2, cv = (room.V0 + room.V1) / 2, wide = Math.Max(room.U1 - room.U0, room.V1 - room.V0) + 1;
                C.X.Lights.Add(new LightSpot { Cell = P(cu, Ring(room.Storey) - 1, cv), Color = LampColour(tech), Module = room.Module, Storey = room.Storey, Energy = 1.1f, Range = wide * 0.9f + 2f, Mode = LightMode.Steady });
                // The structure by role: an old house's joists over its kitchen, living room and hall; a garage's pipe run.
                if (tech == TechLevel.Low && room.Purpose is "kitchen" or "living-room" or "hall" && room.Shape == null) Joists(room);
                if (room.Purpose == "garage" && room.Shape == null) PipeRun(room, tech);
            }
        }

        /// <summary>Timber joists under the ceiling across a room's short span, one every two metres, wall to wall; none over a stair.</summary>
        void Joists(Room room)
        {
            bool acrossU = room.U1 - room.U0 <= room.V1 - room.V0;
            int y = Ring(room.Storey) - 1;
            var timber = new Block(BlockType.Beam, Material.Timber, new Rgb(0x5a, 0x44, 0x30), Role.Decor, _f.Turn(acrossU ? Dir.East : Dir.South));
            for (int a = (acrossU ? room.V0 : room.U0) + 1; a < (acrossU ? room.V1 : room.U1); a += 2)
            {
                var line = Enumerable.Range(acrossU ? room.U0 : room.V0, (acrossU ? room.U1 - room.U0 : room.V1 - room.V0) + 1).Select(b => acrossU ? (b, a) : (a, b)).ToList();
                if (line.Any(c => _stair.Contains((c.Item1, c.Item2, room.Storey)) || !C.Solid(P(c.Item1, y + 1, c.Item2)))) continue;
                foreach (var (u, v) in line) C.Set(P(u, y, v), timber);
            }
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
