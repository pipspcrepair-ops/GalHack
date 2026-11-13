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
public static class BlueprintSet
{
    public sealed record Design(string File, Func<Catalogue, string> Make);

    /// <summary>Every stock blueprint, by its file under the repository root.</summary>
    public static readonly Design[] All =
    {
        new("prototypes/slice/data/blueprints/starter.gbp", Skiff),
        new("prototypes/slice/data/blueprints/outpost.gbp", Outpost),
        new("prototypes/slice/data/blueprints/pod.gbp", Pod),
    };

    static Int3 P(int x, int y, int z) => new(x, y, z);

    // ---- Shared liveries ---------------------------------------------------------------------------

    static Paint Coat(uint color, int grime = 1, FaceTexture texture = FaceTexture.Panel) =>
        GalHack.Structures.Paint.Solid(color) with { Grime = (byte)grime, Texture = texture };

    /// <summary>The deck finish every walked floor takes on its top face.</summary>
    static Paint Deck => Coat(0x4a4e57, 1, FaceTexture.Plates);

    static Paint Hazard => Coat(0xf4a100, 2).WithPattern(Pattern.Hazard, 0x2b2d42);

    // ---- Small craft -------------------------------------------------------------------------------

    /// <summary>The Skiff: the cheapest flyer, an open-cockpit hopper on base blocks.</summary>
    static string Skiff(Catalogue cat)
    {
        var d = new Designer(cat, "Skiff", CoreMode.Capital);
        ushort hull = d.Paint("hull", Coat(0xece3c8)), belly = d.Paint("belly", Coat(0x2b2d42, 2)), deck = d.Paint("deck", Deck);
        ushort stripe = d.Paint("stripe", Coat(0xece3c8).WithPattern(Pattern.Stripes, 0xe85d04));
        ushort chev = d.Paint("chev", Coat(0x0a9396).WithPattern(Pattern.Chevrons, 0xf4a100)), hazard = d.Paint("hazard", Hazard);

        // Belly (y -1), the core in it; its top is the cockpit floor.
        d.Fill(P(-2, -1, -5), P(2, -1, 3), "base-block", belly).Face(P(-1, -1, -3), P(1, -1, 0), Dir.PosY, deck);
        d.Face(P(-2, -1, 0), P(-2, -1, 0), Dir.PosY, deck);
        d.Put("core", P(0, -1, 1));
        // Nose: a ramp, the dash, a clear windscreen at eye height.
        d.Fill(P(-2, 0, -5), P(2, 0, -5), "base-block", hull, "slope");
        d.Fill(P(-2, 0, -4), P(2, 0, -4), "base-block", hull);
        d.Fill(P(-1, 1, -4), P(1, 1, -4), "window-block", 0, "slope");
        // Rails round the cockpit tub, a gap to port to step in by.
        d.Fill(P(-2, 0, -3), P(-2, 0, -1), "base-block", stripe).Fill(P(2, 0, -3), P(2, 0, 0), "base-block", stripe);
        d.Put("command-chair", P(0, 0, -3));
        // Engine hump behind the seat: generator, gyro and fuel in it, the mains in its tail.
        d.Fill(P(-2, 0, 1), P(2, 0, 3), "base-block", chev).Fill(P(-2, 1, 1), P(2, 1, 1), "base-block", chev, "slope");
        d.Put("generator-t1", P(-1, 0, 2));
        d.Put("gyro-t1", P(0, 0, 2));
        d.Put("fuel-tank-t1", P(1, 0, 2));
        d.Face(P(-2, 0, 3), P(2, 0, 3), Dir.PosZ, hazard);
        d.Thruster("thruster-t1", P(-1, 0, 4), Dir.NegZ);
        d.Thruster("thruster-t1", P(1, 0, 4), Dir.NegZ);
        // Retro under the nose, a side jet each way, two lift jets in the belly under the hump.
        d.Thruster("thruster-t1", P(0, -1, -6), Dir.PosZ);
        d.Thruster("thruster-t1", P(3, 0, 2), Dir.NegX);
        d.Thruster("thruster-t1", P(-3, 0, 2), Dir.PosX);
        d.Thruster("thruster-t1", P(-1, -1, 1), Dir.PosY);
        d.Thruster("thruster-t1", P(1, -1, 1), Dir.PosY);
        // Three pads, flush with the belly; a step up to port.
        d.Put("pad-t1", P(0, -1, -4));
        d.Put("pad-t1", P(-2, -1, 2));
        d.Put("pad-t1", P(2, -1, 2));
        d.Stairs(P(-3, -1, 0), Dir.PosX, hull);
        return d.Text(@"Skiff: the slice's starter vessel and the cheapest flyer in the game: an open-cockpit
hopper of plain base blocks, painted. A command chair behind a clear windscreen, a cockpit tub you
step into from the port step, an engine hump behind with the generator, gyro and fuel,
two mains, a retro, a side jet each way, two lift jets and three pads.");
    }

    // ---- Bases -------------------------------------------------------------------------------------

    /// <summary>The Outpost: a first base with a hall 4 m clear and a landing pad off its east door.</summary>
    static string Outpost(Catalogue cat)
    {
        var d = new Designer(cat, "Outpost", CoreMode.Base);
        ushort wall = d.Paint("wall", Coat(0xe9dfc4)), inner = d.Paint("inner", Coat(0xd8d2c2, 0, FaceTexture.Smooth)), deck = d.Paint("deck", Deck);
        ushort band = d.Paint("band", Coat(0x0a9396)), roof = d.Paint("roof", Coat(0x3a3f47, 2)), hazard = d.Paint("hazard", Hazard);
        ushort pad = d.Paint("pad", Coat(0x5a5e66, 2, FaceTexture.Plates));

        // The hall: 10 x 9 m inside, 4 m floor to ceiling, concrete plated.
        d.Room(P(-4, 1, -4), P(5, 4, 4), "concrete-plated", wall, inner, deck);
        d.Face(P(-5, 5, -5), P(6, 5, 5), Dir.PosY, roof);
        d.Face(P(-5, 3, -5), P(6, 3, 5), Dir.PosX, band).Face(P(-5, 3, -5), P(6, 3, 5), Dir.NegX, band);
        d.Face(P(-5, 3, -5), P(6, 3, 5), Dir.PosZ, band).Face(P(-5, 3, -5), P(6, 3, 5), Dir.NegZ, band);
        d.Fill(P(-5, 2, -2), P(-5, 3, 2), "glass-plated");
        d.Put("core", P(0, 0, -2));
        // Doors: a double one south with steps up to it, a single one east onto the pad.
        d.Door("door-2x2x1", P(0, 1, 5), Dir.PosZ);
        d.Stairs(P(0, 0, 6), Dir.NegZ, wall);
        d.Stairs(P(1, 0, 6), Dir.NegZ, wall);
        d.Door("door-1x2x1", P(6, 1, 0), Dir.PosX);
        // Along the north wall: power, fuel, the fabricator and the lockers.
        d.Put("generator-t1", P(-4, 1, -4));
        d.Put("fuel-tank-t1", P(-3, 1, -4));
        d.Put("fabricator-t1", P(-2, 1, -4));
        for (int x = 2; x <= 5; x++) d.Put("container-t1", P(x, 1, -4));
        // The command chair by the west window; lights on the ceiling.
        d.Put("command-chair", P(-4, 1, -1));
        foreach (int x in new[] { -2, 3 })
            foreach (int z in new[] { -2, 2 }) d.Mount("light", P(x, 4, z), Dir.PosY);
        // The pad: 12 m square off the east door, level with the floor, a hazard rim.
        d.Fill(P(7, 0, -6), P(18, 0, 5), "base-block", pad).Face(P(7, 0, -6), P(18, 0, 5), Dir.PosY, hazard);
        d.Face(P(8, 0, -5), P(17, 0, 4), Dir.PosY, pad);
        return d.Text(@"Outpost: a first base on the ground. A concrete hall 10 x 9 m and 4 m floor to ceiling,
with power, fuel, a fabricator and four lockers along the north wall and the command chair by the
west window; a double door south up two steps, a door east onto a landing pad 12 m square level
with the floor.");
    }

    /// <summary>The escape pod: a shelter with a seat, a locker and its own power.</summary>
    static string Pod(Catalogue cat)
    {
        var d = new Designer(cat, "Escape Pod", CoreMode.Base);
        ushort hull = d.Paint("hull", Coat(0xe85d04, 2)), inner = d.Paint("inner", Coat(0xd8d2c2, 0, FaceTexture.Smooth)), deck = d.Paint("deck", Deck);
        ushort shield = d.Paint("shield", Coat(0x2b2d42, 3, FaceTexture.Ribbed));

        // A shell 3 x 4 m inside and 3 m tall, a scorched heat shield under it.
        d.Room(P(-1, 1, -1), P(1, 3, 2), "iron-plated", hull, inner, deck);
        d.Face(P(-2, 0, -2), P(2, 0, 3), Dir.NegY, shield);
        d.Fill(P(-2, 4, -2), P(2, 4, -2), "iron-plated", hull, "slope").Fill(P(-2, 4, 3), P(2, 4, 3), "iron-plated", hull, "slope", "+y+z");
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
