using System;
using System.Collections.Generic;
using GalHack.Structures;

namespace Slice.Tests;

/// <summary>
/// The stock blueprints, each a short description written through <see cref="Designer"/> on the
/// 1 m grid (one-metre-grid.md section 6): rooms 3 to 4 m clear, people doors 1x2 or 2x2, proper
/// chairs, full-block floors with a painted finish. Cells: x starboard (east), y up, -z forward
/// (north). The blueprint test writes these files (--write) and checks them; nothing else does.
/// </summary>
public static partial class BlueprintSet
{
    /// <summary>A stock blueprint. A fixture is a test prop, not a structure anyone uses (a wreck, a bare pad): it is only checked to load whole.</summary>
    public sealed record Design(string File, Func<Catalogue, string> Make, bool Fixture = false);

    /// <summary>Every stock blueprint, by its file under the repository root.</summary>
    public static readonly Design[] All =
    {
        new("prototypes/slice/data/blueprints/starter.gbp", Skiff),
        new("prototypes/slice/data/blueprints/outpost.gbp", Outpost),
        new("prototypes/slice/data/blueprints/pod.gbp", Pod),
        new("prototypes/slice/data/blueprints/rover.gbp", Rover),
        new("prototypes/slice/data/blueprints/dart.gbp", Dart),
        new("prototypes/slice/data/blueprints/shuttle.gbp", Shuttle),
        new("prototypes/slice/data/blueprints/tank.gbp", Tank),
        new("prototypes/slice/data/blueprints/hauler.gbp", Hauler),
        new("prototypes/slice/data/blueprints/frigate.gbp", Frigate),
        new("prototypes/capital-base/data/blueprints/bastion.gbp", Bastion),
        new("prototypes/capital-base/data/blueprints/prospector.gbp", Prospector),
        new("prototypes/manufacturing/data/blueprints/workshop.gbp", Workshop),
        new("prototypes/manufacturing/data/blueprints/wreck.gbp", Wreck, Fixture: true),
        new("prototypes/physics-sandbox/data/blueprints/barge.gbp", Barge),
        new("prototypes/physics-sandbox/data/blueprints/tender.gbp", Tender),
        new("prototypes/physics-sandbox/data/blueprints/pad.gbp", FieldPad, Fixture: true),
        new("prototypes/building/data/blueprints/lancer.gbp", Lancer),
    };

    static Int3 P(int x, int y, int z) => new(x, y, z);

    // ---- Shared liveries ---------------------------------------------------------------------------

    static Paint Coat(uint color, int grime = 1) => GalHack.Structures.Paint.Solid(color) with { Grime = (byte)grime };

    /// <summary>The deck finish every walked floor takes on its top face.</summary>
    static Paint Deck => Coat(0x4a4e57, 1);

    static Paint Hazard => Coat(0xf4a100, 2).WithPattern(Pattern.Hazard, 0x2b2d42);

    // ---- Small craft -------------------------------------------------------------------------------

    /// <summary>The Skiff: the cheapest flyer, an open-cockpit hopper on base blocks.</summary>
    static string Skiff(Catalogue cat)
    {
        var d = new Designer(cat, "Skiff", CoreMode.Flight) { Seal = true };
        ushort hull = d.Paint("hull", Coat(0xece3c8)), belly = d.Paint("belly", Coat(0x2b2d42, 2)), deck = d.Paint("deck", Deck);
        ushort stripe = d.Paint("stripe", Coat(0xece3c8).WithPattern(Pattern.Stripes, 0xe85d04));
        ushort chev = d.Paint("chev", Coat(0x0a9396).WithPattern(Pattern.Chevrons, 0xf4a100)), hazard = d.Paint("hazard", Hazard);

        // Belly (y -1), the core in it; its top is the cockpit floor.
        d.Fill(P(-2, -1, -5), P(2, -1, 3), "base-block", belly).Face(P(-1, -1, -3), P(1, -1, 0), Dir.PosY, deck);
        d.Face(P(-2, -1, 0), P(-2, -1, 0), Dir.PosY, deck);
        d.Chamfer(P(2, -1, -5), P(2, -1, -5), "base-block", belly, Dir.PosX, Dir.NegZ).Chamfer(P(-2, -1, -5), P(-2, -1, -5), "base-block", belly, Dir.NegX, Dir.NegZ);
        d.Put("core", P(1, -1, 1));
        // Nose: a ramp, the dash, a clear windscreen at eye height.
        d.Fill(P(-1, 0, -5), P(1, 0, -5), "base-block", hull, "slope");
        d.Chamfer(P(2, 0, -5), P(2, 0, -5), "base-block", hull, Dir.PosX, Dir.NegZ).Chamfer(P(-2, 0, -5), P(-2, 0, -5), "base-block", hull, Dir.NegX, Dir.NegZ);
        d.Fill(P(-2, 0, -4), P(2, 0, -4), "base-block", hull);
        d.Fill(P(-1, 1, -4), P(1, 1, -4), "window-block", 0, "slope");
        // Rails round the cockpit tub, a gap to port to step in by.
        d.Fill(P(-2, 0, -3), P(-2, 0, -3), "base-block", stripe).Fill(P(2, 0, -3), P(2, 0, -3), "base-block", stripe);
        d.Put("command-chair", P(0, 0, -3));
        // Engine hump behind the seat: generator, gyro and fuel in it, the mains in its tail.
        d.Fill(P(-2, 0, 1), P(2, 0, 3), "base-block", chev).Fill(P(-2, 1, 1), P(2, 1, 1), "base-block", chev, "slope");
        d.Fill(P(-2, 0, 2), P(-2, 0, 2), "base-block", chev, "half").Fill(P(2, 0, 2), P(2, 0, 2), "base-block", chev, "half");
        d.Chamfer(P(2, 0, 3), P(2, 0, 3), "base-block", chev, Dir.PosX, Dir.PosZ).Chamfer(P(-2, 0, 3), P(-2, 0, 3), "base-block", chev, Dir.NegX, Dir.PosZ);
        d.Put("generator-t1", P(-1, 0, 2));
        d.Put("gyro-t1", P(0, 0, 2));
        d.Put("fuel-tank-t1", P(1, 0, 2));
        d.Face(P(-2, 0, 3), P(2, 0, 3), Dir.PosZ, hazard);
        d.Thruster("thruster-t1", P(-1, 0, 4), Dir.NegZ);
        d.Thruster("thruster-t1", P(1, 0, 4), Dir.NegZ);
        // Retro under the nose, a side jet each way, a lift jet in the belly under the hump and a down jet on it.
        d.Thruster("thruster-t1", P(0, -1, -6), Dir.PosZ);
        d.Thruster("thruster-t1", P(3, 0, 2), Dir.NegX);
        d.Thruster("thruster-t1", P(-3, 0, 2), Dir.PosX);
        d.Thruster("thruster-t1", P(0, -1, 1), Dir.PosY);
        d.Thruster("thruster-t1", P(0, 1, 2), Dir.NegY);
        // Three pads, flush with the belly; a step up to port.
        d.Put("pad-t1", P(0, -1, -4));
        d.Put("pad-t1", P(-2, -1, 2));
        d.Put("pad-t1", P(2, -1, 2));
        d.Stairs(P(-3, -1, 0), Dir.PosX, hull);
        return d.Text(@"Skiff: the slice's starter vessel and the cheapest flyer in the game: an open-cockpit
hopper of plain base blocks, painted. A command chair behind a clear windscreen, a cockpit tub you
step into from the port step, an engine hump behind with the generator, gyro and fuel,
two mains, a retro, a side jet each way, a lift jet, a down jet and three pads.");
    }

    // ---- Bases -------------------------------------------------------------------------------------

    /// <summary>The Outpost: a first base, a hall with a raised clerestory, an ops wing to the west, a porch to the south and a landing pad off its east door.</summary>
    static string Outpost(Catalogue cat)
    {
        var d = new Designer(cat, "Outpost", CoreMode.Base) { Seal = true };
        ushort wall = d.Paint("wall", Coat(0xe9dfc4)), inner = d.Paint("inner", Coat(0xd8d2c2, 0)), deck = d.Paint("deck", Deck);
        ushort band = d.Paint("band", Coat(0x0a9396)), roof = d.Paint("roof", Coat(0x3a3f47, 2)), hazard = d.Paint("hazard", Hazard);
        ushort pad = d.Paint("pad", Coat(0x5a5e66, 2)), frame = d.Paint("frame", Coat(0x2b2d42, 1));
        const string Concrete = "concrete-plated";

        // The hall: 10 x 9 m inside, 4 m floor to ceiling. Its roof edge is raked, and a clerestory 6 x 5 m rises out of the middle
        // of it, 2 m higher and glazed all round.
        d.Fill(P(-5, 0, -5), P(6, 5, 5), Concrete, wall).Hollow(P(-4, 1, -4), P(5, 4, 4));
        d.Slope(P(-4, 5, -5), P(5, 5, -5), Concrete, roof, Dir.NegZ).Slope(P(-4, 5, 5), P(5, 5, 5), Concrete, roof, Dir.PosZ);
        d.Slope(P(-5, 5, -4), P(-5, 5, 4), Concrete, roof, Dir.NegX).Slope(P(6, 5, -4), P(6, 5, 4), Concrete, roof, Dir.PosX);
        d.Fill(P(-3, 5, -3), P(4, 7, 3), Concrete, wall).Hollow(P(-2, 5, -2), P(3, 6, 2));
        d.Face(P(-3, 7, -3), P(4, 7, 3), Dir.PosY, roof);
        d.Fill(P(-3, 6, -2), P(-3, 6, 2), "glass-plated").Fill(P(4, 6, -2), P(4, 6, 2), "glass-plated");
        d.Fill(P(-2, 6, -3), P(3, 6, -3), "glass-plated").Fill(P(-2, 6, 3), P(3, 6, 3), "glass-plated");
        d.Face(P(-5, 3, -5), P(6, 3, 5), Dir.PosX, band).Face(P(-5, 3, -5), P(6, 3, 5), Dir.NegX, band);
        d.Face(P(-5, 3, -5), P(6, 3, 5), Dir.PosZ, band).Face(P(-5, 3, -5), P(6, 3, 5), Dir.NegZ, band);
        for (int i = 8; i <= 11; i++) d.Fill(P(0, i, 0), P(0, i, 0), Concrete, frame, "cylinder"); // the mast
        d.Put("core", P(0, 0, -2));
        // The ops wing to the west: 5 x 5 m and 3 m clear, glazed on three sides, through a door in the hall's west wall.
        d.Fill(P(-11, 0, -3), P(-6, 4, 3), Concrete, wall).Hollow(P(-10, 1, -2), P(-6, 3, 2));
        d.Slope(P(-10, 4, -3), P(-6, 4, -3), Concrete, roof, Dir.NegZ).Slope(P(-10, 4, 3), P(-6, 4, 3), Concrete, roof, Dir.PosZ).Slope(P(-11, 4, -2), P(-11, 4, 2), Concrete, roof, Dir.NegX);
        d.Face(P(-10, 4, -2), P(-6, 4, 2), Dir.PosY, roof).Face(P(-11, 2, -3), P(-6, 2, 3), Dir.NegX, band).Face(P(-11, 2, -3), P(-6, 2, 3), Dir.NegZ, band).Face(P(-11, 2, -3), P(-6, 2, 3), Dir.PosZ, band);
        d.Fill(P(-11, 2, -1), P(-11, 3, 1), "glass-plated").Fill(P(-9, 2, -3), P(-7, 3, -3), "glass-plated").Fill(P(-9, 2, 3), P(-7, 3, 3), "glass-plated");
        d.Door("door-1x2x1", P(-5, 1, 0), Dir.NegX);
        d.Put("command-chair", P(-10, 1, 0), Designer.Rot(Dir.PosY, Dir.NegX));
        d.Mount("light", P(-8, 3, 0), Dir.PosY);
        // Framing: a pilaster at each corner of the hall and along its north wall, dark against the cream.
        foreach (int x in new[] { -5, -1, 3, 6 }) d.Fill(P(x, 0, -6), P(x, 4, -6), Concrete, frame).Slope(P(x, 5, -6), P(x, 5, -6), Concrete, frame, Dir.NegZ);
        foreach (int x in new[] { -5, 6 }) d.Fill(P(x, 0, 6), P(x, 4, 6), Concrete, frame).Slope(P(x, 5, 6), P(x, 5, 6), Concrete, frame, Dir.PosZ);
        // The roof's four hips.
        d.Fill(P(6, 5, -5), P(6, 5, -5), Concrete, roof, "corner").Fill(P(-5, 5, -5), P(-5, 5, -5), Concrete, roof, "corner", Designer.Rot(Dir.PosY, Dir.NegX));
        d.Fill(P(-5, 5, 5), P(-5, 5, 5), Concrete, roof, "corner", Designer.Rot(Dir.PosY, Dir.PosZ)).Fill(P(6, 5, 5), P(6, 5, 5), Concrete, roof, "corner", Designer.Rot(Dir.PosY, Dir.PosX));
        d.Fill(P(-4, 2, 5), P(-2, 3, 5), "glass-plated").Fill(P(3, 2, 5), P(5, 3, 5), "glass-plated");
        // Doors: a double one south under a porch, a single one east onto the pad.
        d.Door("door-2x2x1", P(0, 1, 5), Dir.PosZ);
        d.Fill(P(-1, 0, 6), P(2, 0, 8), Concrete, wall).Face(P(-1, 0, 6), P(2, 0, 8), Dir.PosY, deck);
        d.Stairs(P(0, 0, 9), Dir.NegZ, wall);
        d.Stairs(P(1, 0, 9), Dir.NegZ, wall);
        foreach (int x in new[] { -1, 2 }) d.Fill(P(x, 1, 8), P(x, 3, 8), Concrete, frame, "cylinder");
        d.Fill(P(-1, 4, 6), P(2, 4, 7), Concrete, roof).Slope(P(-1, 4, 8), P(2, 4, 8), Concrete, roof, Dir.PosZ);
        d.Door("door-1x2x1", P(6, 1, 0), Dir.PosX);
        // Along the north wall: power, fuel, the fabricator and the lockers; lights on the ceilings, two under the clerestory.
        d.Put("generator-t1", P(-4, 1, -4));
        d.Put("fuel-tank-t1", P(-3, 1, -4));
        d.Put("fabricator-t1", P(-2, 1, -4));
        for (int x = 2; x <= 5; x++) d.Put("container-t1", P(x, 1, -4));
        foreach (int x in new[] { -3, 4 })
            foreach (int z in new[] { -3, 3 }) d.Mount("light", P(x, 4, z), Dir.PosY);
        d.Mount("light", P(-1, 6, 0), Dir.PosY);
        d.Mount("light", P(2, 6, 0), Dir.PosY);
        // The pad: 12 m square off the east door, level with the floor, a hazard rim and a ramp down to the ground on three sides.
        d.Fill(P(7, 0, -6), P(18, 0, 5), "base-block", pad);
        d.Face(P(8, 0, -5), P(17, 0, 4), Dir.PosY, hazard).Face(P(9, 0, -4), P(16, 0, 3), Dir.PosY, pad).Face(P(11, 0, -1), P(14, 0, 0), Dir.PosY, hazard);
        // Ramps down to the ground on three sides carry the pad's plain finish, and corners run them into each other.
        d.Slope(P(19, 0, -6), P(19, 0, 5), "base-block", pad, Dir.PosX).Slope(P(8, 0, -7), P(18, 0, -7), "base-block", pad, Dir.NegZ).Slope(P(8, 0, 6), P(18, 0, 6), "base-block", pad, Dir.PosZ);
        d.Fill(P(19, 0, -7), P(19, 0, -7), "base-block", pad, "corner").Fill(P(19, 0, 6), P(19, 0, 6), "base-block", pad, "corner", Designer.Rot(Dir.PosY, Dir.PosX));
        d.Fill(P(7, 0, -7), P(7, 0, -7), "base-block", pad, "corner", Designer.Rot(Dir.PosY, Dir.NegX)).Fill(P(7, 0, 6), P(7, 0, 6), "base-block", pad, "corner", Designer.Rot(Dir.PosY, Dir.PosZ));
        d.Lining(inner, deck);
        return d.Text(@"Outpost: a first base on the ground. A concrete hall 10 x 9 m and 4 m floor to ceiling under a raked
roof, a glazed clerestory rising out of its middle; power, fuel, a fabricator and four lockers along
the north wall. A door west into a 3 m ops wing with the command chair at its window; a double door
south under a porch, up two steps; a door east onto a landing pad 12 m square level with the floor,
its rim hazard-marked and ramped down to the ground.");
    }

    /// <summary>The escape pod: a shelter with a seat, a locker and its own power.</summary>
    static string Pod(Catalogue cat)
    {
        var d = new Designer(cat, "Escape Pod", CoreMode.Base) { Seal = true };
        ushort hull = d.Paint("hull", Coat(0xe85d04, 2)), inner = d.Paint("inner", Coat(0xd8d2c2, 0)), deck = d.Paint("deck", Deck);
        ushort shield = d.Paint("shield", Coat(0x2b2d42, 3));

        // A shell 3 x 4 m inside and 3 m tall, a scorched heat shield under it.
        ushort band = d.Paint("band", Coat(0xece3c8));
        d.Room(P(-1, 1, -1), P(1, 3, 2), "iron-plated", hull, inner, deck);
        // A capsule: its four corners cut away and a cream belt round it.
        foreach (int z in new[] { -2, 3 })
            for (int y = 0; y <= 4; y++)
            {
                Dir sz = z < 0 ? Dir.NegZ : Dir.PosZ;
                d.Chamfer(P(2, y, z), P(2, y, z), "iron-plated", y == 2 ? band : hull, Dir.PosX, sz).Chamfer(P(-2, y, z), P(-2, y, z), "iron-plated", y == 2 ? band : hull, Dir.NegX, sz);
            }
        d.Face(P(-2, 2, -2), P(2, 2, 3), Dir.PosX, band).Face(P(-2, 2, -2), P(2, 2, 3), Dir.NegX, band).Face(P(-2, 2, -2), P(2, 2, 3), Dir.PosZ, band).Face(P(-2, 2, -2), P(2, 2, 3), Dir.NegZ, band);
        d.Face(P(-2, 0, -2), P(2, 0, 3), Dir.NegY, shield);
        d.Fill(P(-1, 4, -2), P(1, 4, -2), "iron-plated", hull, "slope").Fill(P(-1, 4, 3), P(1, 4, 3), "iron-plated", hull, "slope", "+y+z");
        d.Put("core", P(0, 0, 0));
        d.Door("door-1x2x1", P(0, 1, -2), Dir.NegZ);
        d.Stairs(P(0, 0, -3), Dir.PosZ, hull);
        d.Put("command-chair", P(1, 1, -1));
        d.Put("container-t1", P(-1, 1, 2));
        d.Put("generator-t1", P(0, 1, 2));
        d.Put("fuel-tank-t1", P(1, 1, 2));
        d.Mount("light", P(0, 3, 0), Dir.PosY);
        return d.Text(@"Escape pod: a crash-landed capsule kept as a shelter. An iron shell 3 x 4 m inside and
3 m tall, a door at the front up one step, a command chair facing it, a locker, a generator and its
fuel along the back wall, a light, and a scorched heat shield underneath.");
    }
}
