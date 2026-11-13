using GalHack.Structures;

namespace Slice.Tests;

/// <summary>
/// The other prototypes' fixtures, each on the parts its own prototype loads: capital-base (Bastion, Prospector) and the
/// physics sandbox (Barge, Tender, Pad) have building's parts and the vessel parts; manufacturing (Workshop, Wreck) has
/// shared/Items' machines too; building (Lancer) has building's parts alone. Where a fixture was the same design as a
/// slice starter (the Gadfly, the Corsair, the Sand Flea, capital-base's Outpost) the tests load the slice's file.
/// </summary>
public static partial class BlueprintSet
{
    /// <summary>Bastion: a capital ship that lands, becomes a base and lifts off again; its hall roof is a landing pad.</summary>
    static string Bastion(Catalogue cat)
    {
        var d = new Designer(cat, "Bastion 7", CoreMode.Capital);
        ushort hull = d.Paint("hull", Coat(0xe9dfc4)), inner = d.Paint("inner", Coat(0xd8d2c2, 0)), deck = d.Paint("deck", Deck);
        ushort saffron = d.Paint("saffron", Coat(0xf4a100)), red = d.Paint("red", Coat(0xc1121f)), hazard = d.Paint("hazard", Hazard);
        ushort chev = d.Paint("chev", Coat(0x0a9396).WithPattern(Pattern.Chevrons, 0xe9dfc4)), pad = d.Paint("pad", Coat(0x5a5e66, 2));

        // The bridge forward and the engineering hall aft, both 4 m clear, a double door between them.
        d.Room(P(-5, 1, -12), P(4, 4, -6), "iron-plated", hull, inner, deck);
        d.Room(P(-5, 1, -4), P(4, 4, 8), "iron-plated", hull, inner, deck);
        d.Face(P(-6, 3, -13), P(5, 3, 9), Dir.PosX, saffron).Face(P(-6, 3, -13), P(5, 3, 9), Dir.NegX, saffron);
        d.Fill(P(-5, 2, -13), P(4, 3, -13), "glass-plated").Face(P(-6, 0, -13), P(5, 1, -13), Dir.NegZ, red);
        d.Door("door-2x2x1", P(-1, 1, -5), Dir.NegZ);
        d.Door("door-2x2x1", P(-1, 1, 9), Dir.PosZ);
        d.Stairs(P(-1, 0, 10), Dir.NegZ, hull);
        d.Stairs(P(0, 0, 10), Dir.NegZ, hull);
        // The hall's roof is the pad: hazard round its rim.
        d.Face(P(-6, 5, -4), P(5, 5, 9), Dir.PosY, hazard).Face(P(-5, 5, -3), P(4, 5, 8), Dir.PosY, pad);
        d.Put("core", P(0, 0, -9));
        d.Put("command-chair", P(-2, 1, -12));
        d.Put("command-chair", P(1, 1, -12));
        d.Mount("light", P(-1, 4, -8), Dir.PosY);
        foreach (int z in new[] { -1, 5 })
        {
            d.Mount("light", P(-3, 4, z), Dir.PosY);
            d.Mount("light", P(2, 4, z), Dir.PosY);
        }
        // Engineering: lockers to port, power, fuel and gyros to starboard.
        for (int z = -4; z <= 2; z++) d.Put("container-t4", P(-5, 1, z));
        d.Put("generator-t2", P(4, 1, -4));
        d.Put("generator-t2", P(4, 1, -3));
        d.Put("fuel-tank-t2", P(4, 1, -2));
        d.Put("fuel-tank-t2", P(4, 1, 0));
        d.Put("gyro-t2", P(4, 1, 2));
        d.Put("gyro-t2", P(4, 1, 3));
        // Nacelles either side of the hall, the mains in their tails; lift jets and pads in the keel.
        d.Fill(P(-8, 1, -4), P(-7, 3, 8), "iron-plated", chev).Fill(P(6, 1, -4), P(7, 3, 8), "iron-plated", chev);
        foreach (int x in new[] { -8, -7, 6, 7 }) d.Thruster("thruster-t3", P(x, 2, 9), Dir.NegZ);
        d.Thruster("thruster-t3", P(-6, 1, -15), Dir.PosZ);
        d.Thruster("thruster-t3", P(5, 1, -15), Dir.PosZ);
        d.Thruster("thruster-t3", P(-8, 2, -10), Dir.PosX);
        d.Thruster("thruster-t3", P(6, 2, -10), Dir.NegX);
        d.Thruster("thruster-t3", P(-10, 2, 2), Dir.PosX);
        d.Thruster("thruster-t3", P(8, 2, 2), Dir.NegX);
        foreach (var (x, z) in new[] { (-6, -10), (5, -10), (-6, -1), (5, -1), (-6, 6), (5, 6) }) d.Thruster("thruster-t3", P(x, 0, z), Dir.PosY);
        d.Thruster("thruster-t3", P(-3, 6, -9), Dir.NegY);
        d.Thruster("thruster-t3", P(2, 6, -9), Dir.NegY);
        foreach (var (x, z) in new[] { (-6, -12), (5, -12), (-6, 2), (5, 2), (-6, 7), (5, 7) }) d.Put("pad-t3", P(x, 0, z));
        return d.Text(@"Bastion 7: a capital ship that lands on a planet, becomes a base and lifts off again. Two
iron rooms 10 m wide and 4 m floor to ceiling: the bridge forward, two command chairs behind the
glazed nose, and the engineering hall aft with seven Container T4s, two generators, their fuel and
two gyros; a double door between them and one at the stern up a step. The hall's roof is a landing
pad 12 x 14 m for a small vessel. Nacelles on the flanks; Thruster T3s; six pads.");
    }

    /// <summary>Prospector: a small harvester that lives on Bastion's pad (capital-base digs under its keel).</summary>
    static string Prospector(Catalogue cat)
    {
        var d = new Designer(cat, "Prospector 3", CoreMode.Capital);
        ushort saffron = d.Paint("saffron", Coat(0xf4a100)), belly = d.Paint("belly", Coat(0x2b2d42, 2)), deck = d.Paint("deck", Deck);
        ushort teal = d.Paint("teal", Coat(0x0a9396).WithPattern(Pattern.Chevrons, 0xf4a100)), red = d.Paint("red", Coat(0xc1121f)), hazard = d.Paint("hazard", Hazard);

        d.Fill(P(-2, -1, -5), P(2, -1, 4), "iron-plated", belly).Face(P(-1, -1, -3), P(1, -1, 0), Dir.PosY, deck).Face(P(-2, -1, 0), P(-2, -1, 0), Dir.PosY, deck);
        d.Put("core", P(0, -1, 1));
        d.Fill(P(-2, 0, -5), P(2, 0, -5), "iron-plated", red, "slope").Fill(P(-2, 0, -4), P(2, 0, -4), "iron-plated", red);
        d.Fill(P(-2, 1, -4), P(2, 1, -4), "glass-plated", 0, "slope");
        d.Fill(P(-2, 0, -3), P(-2, 0, -1), "iron-plated", saffron).Fill(P(2, 0, -3), P(2, 0, 0), "iron-plated", saffron);
        d.Put("command-chair", P(0, 0, -3));
        // The cargo body: eight lockers round the generator, gyro and fuel; the mains in its tail.
        d.Fill(P(-2, 0, 1), P(2, 1, 4), "iron-plated", teal).Face(P(-2, 0, 4), P(2, 1, 4), Dir.PosZ, hazard);
        foreach (int x in new[] { -1, 1 })
            for (int z = 1; z <= 4; z++) d.Put("container-t1", P(x, 1, z));
        d.Put("generator-t1", P(-1, 0, 2));
        d.Put("gyro-t1", P(0, 0, 2));
        d.Put("fuel-tank-t1", P(1, 0, 2));
        d.Thruster("thruster-t1", P(-1, 0, 5), Dir.NegZ);
        d.Thruster("thruster-t1", P(1, 0, 5), Dir.NegZ);
        d.Thruster("thruster-t1", P(-3, 0, -4), Dir.PosZ);
        d.Thruster("thruster-t1", P(3, 0, -4), Dir.PosZ);
        d.Thruster("thruster-t1", P(3, 0, 3), Dir.NegX);
        d.Thruster("thruster-t1", P(-3, 0, 3), Dir.PosX);
        d.Thruster("thruster-t1", P(-1, -1, 1), Dir.PosY);
        d.Thruster("thruster-t1", P(1, -1, 1), Dir.PosY);
        d.Thruster("thruster-t1", P(0, 2, 2), Dir.NegY);
        d.Put("pad-t1", P(0, -1, -5));
        d.Put("pad-t1", P(-2, -1, 3));
        d.Put("pad-t1", P(2, -1, 3));
        d.Stairs(P(-3, -1, 0), Dir.PosX, saffron);
        return d.Text(@"Prospector 3: a small harvesting vessel that lives on Bastion's pad and digs the ground
under its keel. An open cab behind a red nose and a glass brow, stepped into from the port step; a
cargo body of eight lockers round the generator, gyro and fuel; two mains, two retros, side jets,
two lift jets, a down jet and three pads.");
    }

    /// <summary>Workshop: manufacturing's base, a hall with its machines along the back wall and guns to keep in ammo.</summary>
    static string Workshop(Catalogue cat)
    {
        var d = new Designer(cat, "Workshop", CoreMode.Base);
        ushort wall = d.Paint("wall", Coat(0xe9dfc4)), inner = d.Paint("inner", Coat(0xd8d2c2, 0)), deck = d.Paint("deck", Deck);
        ushort band = d.Paint("band", Coat(0x0a9396)), roof = d.Paint("roof", Coat(0x3a3f47, 2));

        // A hall 14 x 8 m inside and 4 m floor to ceiling, the core in the floor.
        d.Room(P(-7, 1, -4), P(6, 4, 3), "concrete-plated", wall, inner, deck);
        d.Face(P(-8, 5, -5), P(7, 5, 4), Dir.PosY, roof).Face(P(-8, 3, -5), P(7, 3, 4), Dir.PosZ, band).Face(P(-8, 3, -5), P(7, 3, 4), Dir.NegZ, band);
        d.Put("core", P(0, 0, 1));
        d.Door("door-2x2x1", P(-1, 1, 4), Dir.PosZ);
        d.Stairs(P(-1, 0, 5), Dir.NegZ, wall);
        d.Stairs(P(0, 0, 5), Dir.NegZ, wall);
        d.Put("command-chair", P(-7, 1, 1));
        // Along the back wall, port to starboard: power, the fabricator, lockers, the deconstructor, the factory, fuel.
        d.Put("generator-t1", P(-7, 1, -4));
        d.Put("fabricator-t1", P(-6, 1, -4));
        d.Put("container-t1", P(-5, 1, -4));
        d.Put("container-t1", P(-4, 1, -4));
        d.Put("decon", P(-3, 1, -4));
        d.Put("container-t1", P(-1, 1, -4));
        d.Put("container-t1", P(0, 1, -4));
        d.Put("factory", P(1, 1, -4));
        d.Put("fuel-tank-t1", P(2, 1, -4));
        d.Put("fuel-tank-t1", P(3, 1, -4));
        foreach (int x in new[] { -4, 4 })
            foreach (int z in new[] { -2, 2 }) d.Mount("light", P(x, 4, z), Dir.PosY);
        // Two turrets on the roof, a gatling and a rocket pod out of the back wall.
        d.Put("turret", P(-4, 6, -1));
        d.Put("turret", P(3, 6, -1));
        d.Put("gatling", P(-2, 3, -6));
        d.Put("rocketpod", P(1, 3, -6));
        return d.Text(@"Workshop: the manufacturing prototype's base. A concrete hall 14 x 8 m and 4 m floor to
ceiling, a double door up a step; along the back wall a generator, the fabricator, four lockers,
the deconstructor, the blueprint factory and fuel; a command chair by the west wall. Two turrets on
the roof, a gatling and a rocket pod out of the back wall, so the fabricator has three kinds of
ammo to keep stocked.");
    }

    /// <summary>A wrecked engine pod for the salvage pad: written with a core so it loads; the scene takes it out.</summary>
    static string Wreck(Catalogue cat)
    {
        var d = new Designer(cat, "Wrecked pod", CoreMode.Capital);
        ushort burnt = d.Paint("burnt", Coat(0x3a3f47, 3)), rust = d.Paint("rust", Coat(0x8a4b2a, 3));
        d.Fill(P(-1, 0, -2), P(1, 1, 2), "iron-plated", burnt).Face(P(-1, 1, -2), P(1, 1, 2), Dir.PosY, rust);
        d.Fill(P(-1, 1, -2), P(-1, 1, -1), "iron-plated", burnt, "slope");
        d.Put("core", P(0, 0, 0));
        d.Put("generator-t1", P(0, 1, 0));
        d.Put("gyro-t1", P(0, 0, -1));
        d.Thruster("thruster-t1", P(-1, 0, 3), Dir.NegZ);
        d.Thruster("thruster-t1", P(1, 0, 3), Dir.NegZ);
        return d.Text(@"A wrecked engine pod for the salvage pad: a burnt iron shell with a generator, a gyro and two
thrusters. Written with a core so it loads; the scene takes the core out, leaving an unowned wreck
for the deconstructor to break down.");
    }

    /// <summary>Deck Barge: a flat work barge for walking on a hull: a wall, a square edge, a lip, a stair, a deckhouse.</summary>
    static string Barge(Catalogue cat)
    {
        var d = new Designer(cat, "Deck Barge", CoreMode.Capital);
        ushort hull = d.Paint("hull", Coat(0xc9ccd1)), deck = d.Paint("deck", Deck), inner = d.Paint("inner", Coat(0xd8d2c2, 0));
        ushort hazard = d.Paint("hazard", Hazard), yellow = d.Paint("yellow", Coat(0xf4a100));

        // The deck: 8 m wide, 16 m long, two blocks thick with square edges; its top is at 0.5 m.
        d.Fill(P(-4, -1, -6), P(3, 0, 9), "iron-plated", hull).Face(P(-4, 0, -6), P(3, 0, 9), Dir.PosY, deck);
        // Amidships a superstructure 2 m square and 2 m tall; a lip 0.5 m tall down the starboard side;
        // a stair of three 0.5 m steps to port.
        d.Fill(P(-1, 1, -1), P(0, 2, 0), "iron-plated", yellow);
        d.Fill(P(2, 1, -6), P(2, 1, 2), "iron-plated", hazard, "half");
        d.Fill(P(-4, 1, 2), P(-3, 1, 2), "iron-plated", hazard, "half");
        d.Fill(P(-4, 1, 1), P(-3, 1, 1), "iron-plated", yellow);
        d.Fill(P(-4, 1, 0), P(-3, 1, 0), "iron-plated", yellow).Fill(P(-4, 2, 0), P(-3, 2, 0), "iron-plated", hazard, "half");
        // Aft the deckhouse, 4 m clear inside, a door at its front, a lip 0.5 m tall and 2 m deep across its floor.
        d.Room(P(-2, 1, 4), P(1, 4, 8), "iron-plated", hull, inner, deck);
        d.Fill(P(-2, 1, 6), P(1, 1, 7), "iron-plated", hazard, "half");
        d.Door("door-1x2x1", P(-1, 1, 3), Dir.NegZ);
        d.Mount("light", P(-1, 4, 5), Dir.PosY);
        // A seat at the bow; the machinery in the deck; engines and jets all round.
        d.Put("command-chair", P(-1, 1, -6));
        d.Put("core", P(0, -1, 2));
        d.Put("generator-t1", P(-1, -1, 2));
        d.Put("gyro-t1", P(1, -1, 2));
        d.Put("fuel-tank-t1", P(-2, -1, 2));
        d.Put("fuel-tank-t1", P(-2, -1, 1));
        // A stair up the port side from the ground to the deck, 2 m.
        d.Stairs(P(-6, -1, -1), Dir.PosX, hull);
        d.Fill(P(-5, -1, -1), P(-5, -1, -1), "iron-plated", hull);
        d.Stairs(P(-5, 0, -1), Dir.PosX, hull);
        foreach (int x in new[] { -3, 2 })
        {
            d.Thruster("thruster-t1", P(x, -1, 10), Dir.NegZ);
            d.Thruster("thruster-t1", P(x, -1, -7), Dir.PosZ);
        }
        foreach (int z in new[] { -4, 6 })
        {
            d.Thruster("thruster-t1", P(-5, -1, z), Dir.PosX);
            d.Thruster("thruster-t1", P(4, -1, z), Dir.NegX);
        }
        foreach (int x in new[] { -3, 2 })
            foreach (int z in new[] { -3, 6 }) d.Thruster("thruster-t1", P(x, -1, z), Dir.PosY);
        d.Thruster("thruster-t1", P(-1, 3, -1), Dir.NegY);
        d.Thruster("thruster-t1", P(0, 3, -1), Dir.NegY);
        foreach (var (x, z) in new[] { (-4, -6), (3, -6), (-4, 7), (3, 7) }) d.Put("pad-t1", P(x, -1, z));
        return d.Text(@"Deck Barge: a flat iron work barge for walking on a hull. A deck 8 m wide and 16 m long, two
blocks thick with square edges; amidships a superstructure 2 m square and 2 m tall, a lip 0.5 m
tall down the starboard side and a stair of three 0.5 m steps to port; aft a deckhouse 4 m clear
inside with a door, and a lip 0.5 m tall across its floor. A seat at the bow, the machinery in the
deck, thrusters all round it, four pads.");
    }

    /// <summary>Deck Tender: a capital ship with a flat deck big enough for the Shuttle and the Rover to park on.</summary>
    static string Tender(Catalogue cat)
    {
        var d = new Designer(cat, "Deck Tender", CoreMode.Capital);
        ushort hull = d.Paint("hull", Coat(0xc9ccd1)), keel = d.Paint("keel", Coat(0x2b2d42, 2)), deck = d.Paint("deck", Coat(0x5a5e66, 2)), hazard = d.Paint("hazard", Hazard);

        // The deck (y 0), 20 m wide and 32 m long, hazard fore and aft; the keel under it holds the machinery.
        d.Fill(P(-10, 0, -16), P(9, 0, 15), "iron-plated", hull).Face(P(-10, 0, -16), P(9, 0, 15), Dir.PosY, deck);
        d.Face(P(-10, 0, -16), P(9, 0, -16), Dir.PosY, hazard).Face(P(-10, 0, 15), P(9, 0, 15), Dir.PosY, hazard);
        d.Fill(P(-4, -1, -14), P(3, -1, 13), "iron-plated", keel);
        // The pilot's seat at the bow in an open booth: a windscreen in front, rails at the sides.
        d.Put("command-chair", P(0, 1, -15));
        d.Fill(P(-1, 1, -16), P(1, 1, -16), "iron-plated", hull).Fill(P(-1, 2, -16), P(1, 2, -16), "glass-plated", 0, "slope");
        d.Put("core", P(0, -1, 0));
        d.Put("generator-t2", P(-2, -1, -2));
        d.Put("fuel-tank-t2", P(1, -1, -2));
        d.Put("gyro-t2", P(-1, -1, 2));
        d.Put("gyro-t2", P(0, -1, 2));
        foreach (int x in new[] { -4, 3 })
        {
            d.Thruster("thruster-t3", P(x, -1, 14), Dir.NegZ);
            d.Thruster("thruster-t3", P(x, -1, -16), Dir.PosZ);
        }
        d.Thruster("thruster-t3", P(-6, -1, 0), Dir.PosX);
        d.Thruster("thruster-t3", P(4, -1, 0), Dir.NegX);
        foreach (int x in new[] { -4, 3 })
            foreach (int z in new[] { -12, -4, 4, 12 }) d.Thruster("thruster-t3", P(x, -2, z), Dir.PosY);
        foreach (int x in new[] { -9, 8 })
            foreach (int z in new[] { -8, 8 }) d.Thruster("thruster-t3", P(x, 1, z), Dir.NegY);
        foreach (int x in new[] { -3, 2 })
            foreach (int z in new[] { -10, 10 }) d.Put("pad-t4", P(x, -2, z));
        // A stair up the stern from the ground to the deck, 3 m.
        d.Stairs(P(-1, -2, 18), Dir.NegZ, hull);
        d.Stairs(P(0, -2, 18), Dir.NegZ, hull);
        d.Fill(P(-1, -2, 16), P(0, -2, 17), "iron-plated", keel).Fill(P(-1, -1, 16), P(0, -1, 16), "iron-plated", keel);
        d.Stairs(P(-1, -1, 17), Dir.NegZ, hull);
        d.Stairs(P(0, -1, 17), Dir.NegZ, hull);
        d.Stairs(P(-1, 0, 16), Dir.NegZ, hull);
        d.Stairs(P(0, 0, 16), Dir.NegZ, hull);
        return d.Text(@"Deck Tender: a capital ship with a flat iron deck 20 m wide and 32 m long for small vessels to
land and park on; the pilot's command chair at the bow behind a windscreen; under the deck a keel
with the generator, fuel, two gyros, eight lift jets and four pads; mains aft, retros forward, a
side jet each way and down jets at the deck's corners. A vessel landed on its deck parks there and
rides it.");
    }

    /// <summary>Field Pad: a landing pad in Base Mode for the wind and parking tests.</summary>
    static string FieldPad(Catalogue cat)
    {
        var d = new Designer(cat, "Field Pad", CoreMode.Base);
        ushort pad = d.Paint("pad", Coat(0x5a5e66, 2)), hazard = d.Paint("hazard", Hazard);
        d.Fill(P(-10, 0, -10), P(9, 0, 9), "concrete-plated", pad).Face(P(-10, 0, -10), P(9, 0, 9), Dir.PosY, hazard).Face(P(-9, 0, -9), P(8, 0, 8), Dir.PosY, pad);
        d.Put("core", P(0, 0, 0));
        return d.Text(@"Field Pad: a concrete landing pad 20 m square in Base Mode, a hazard-striped rim and the core
in its middle. A base never flies and nothing moves it; a vessel landed on it parks there, out of
the wind.");
    }

    /// <summary>Lancer: building's example ship, on building's own parts (no gyros, no pads): a sealed cabin, cargo, power, guns.</summary>
    static string Lancer(Catalogue cat)
    {
        var d = new Designer(cat, "Lancer 07", CoreMode.Capital);
        ushort hull = d.Paint("hull", Coat(0xece3c8)), inner = d.Paint("inner", Coat(0xd8d2c2, 0)), deck = d.Paint("deck", Deck);
        ushort belly = d.Paint("belly", Coat(0x2b2d42, 2)), stripe = d.Paint("stripe", Coat(0xece3c8).WithPattern(Pattern.Stripes, 0xe85d04));
        ushort chev = d.Paint("chev", Coat(0x0a9396).WithPattern(Pattern.Chevrons, 0xf4a100)), hazard = d.Paint("hazard", Hazard);

        // A sealed cabin 6 m wide, 3 m clear and 8 m long; a glazed nose; a door each side up a step.
        d.Room(P(-3, 0, -6), P(2, 2, 1), "iron-plated", hull, inner, deck);
        d.Face(P(-4, -1, -7), P(3, -1, 2), Dir.NegY, belly).Face(P(-4, 1, -7), P(3, 1, 2), Dir.PosX, stripe).Face(P(-4, 1, -7), P(3, 1, 2), Dir.NegX, stripe);
        d.Fill(P(-3, 1, -7), P(2, 2, -7), "glass-plated");
        d.Put("command-chair", P(-1, 0, -6));
        d.Put("command-chair", P(0, 0, -6));
        d.Door("door-1x2x1", P(-4, 0, -1), Dir.NegX);
        d.Door("door-1x2x1", P(3, 0, -1), Dir.PosX);
        d.Stairs(P(-5, -1, -1), Dir.PosX, hull);
        d.Stairs(P(4, -1, -1), Dir.NegX, hull);
        d.Mount("light", P(-1, 2, -3), Dir.PosY);
        d.Put("core", P(0, -1, -3));
        // Behind the cabin's back wall: cargo, power and the engine room, three mains in the tail.
        d.Fill(P(-3, -1, 3), P(2, 2, 6), "iron-plated", chev).Face(P(-3, -1, 6), P(2, 2, 6), Dir.PosZ, hazard);
        for (int x = -2; x <= 1; x++) d.Put("container-t1", P(x, 0, 3));
        d.Put("generator-t1", P(-2, 0, 4));
        d.Put("generator-t1", P(1, 0, 4));
        d.Put("fuel-tank-t1", P(-1, 0, 4));
        d.Put("fuel-tank-t1", P(0, 0, 4));
        foreach (int x in new[] { -2, 0, 1 }) d.Thruster("thruster-t1", P(x, 1, 7), Dir.NegZ);
        d.Thruster("thruster-t1", P(-5, 1, -6), Dir.PosZ);
        d.Thruster("thruster-t1", P(4, 1, -6), Dir.PosZ);
        d.Thruster("thruster-t1", P(-4, 1, 4), Dir.PosX);
        d.Thruster("thruster-t1", P(3, 1, 4), Dir.NegX);
        foreach (int x in new[] { -4, 3 })
            foreach (int z in new[] { -4, 4 }) d.Thruster("thruster-t1", P(x, -1, z), Dir.PosY);
        d.Thruster("thruster-t1", P(-1, 3, 5), Dir.NegY);
        d.Thruster("thruster-t1", P(0, 3, 5), Dir.NegY);
        d.Put("turret", P(-1, 4, -3));
        return d.Text(@"Lancer 07: building's example ship, on building's own parts. A sealed iron cabin 6 m wide,
3 m floor to ceiling and 8 m long, two command chairs behind the glazed nose, a door each side up
a step; behind its back wall four lockers, two generators and their fuel; three mains, two retros,
side jets, four lift jets, two down jets and a turret on the roof. It turns at the game's floor
rate and rests on its keel: building has no gyros and no pads.");
    }
}
