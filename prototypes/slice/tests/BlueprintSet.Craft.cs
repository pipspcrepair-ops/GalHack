using GalHack.Structures;

namespace Slice.Tests;

/// <summary>The rest of the slice's starter set: a hover rover, a light fighter, a shuttle, a hover tank, a hauler and a small capital ship.</summary>
public static partial class BlueprintSet
{
    /// <summary>The Hover Rover: a low open runner on four hover plates.</summary>
    static string Rover(Catalogue cat)
    {
        var d = new Designer(cat, "Hover Rover", CoreMode.Hover);
        ushort body = d.Paint("body", Coat(0xf4a100)), belly = d.Paint("belly", Coat(0x2b2d42, 2)), deck = d.Paint("deck", Deck);
        ushort stripe = d.Paint("stripe", Coat(0xf4a100).WithPattern(Pattern.Stripes, 0x2b2d42)), hazard = d.Paint("hazard", Hazard);

        d.Fill(P(-2, -1, -4), P(2, -1, 3), "base-block", belly).Face(P(-1, -1, -2), P(1, -1, 0), Dir.PosY, deck).Face(P(-2, -1, 0), P(-2, -1, 0), Dir.PosY, deck);
        d.Put("core", P(0, -1, 1));
        d.Fill(P(-2, 0, -4), P(2, 0, -4), "base-block", body, "slope").Fill(P(-2, 0, -3), P(2, 0, -3), "base-block", hazard);
        d.Fill(P(-1, 1, -3), P(1, 1, -3), "window-block", 0, "slope");
        d.Fill(P(-2, 0, -2), P(-2, 0, -1), "base-block", stripe).Fill(P(2, 0, -2), P(2, 0, 0), "base-block", stripe);
        d.Put("command-chair", P(0, 0, -2));
        // The engine bay behind the seat, the mains in its tail; retros either side of the dash.
        d.Fill(P(-2, 0, 1), P(2, 0, 3), "base-block", body).Face(P(-2, 0, 3), P(2, 0, 3), Dir.PosZ, hazard);
        d.Put("generator-t1", P(-1, 0, 2));
        d.Put("gyro-t1", P(0, 0, 2));
        d.Put("fuel-tank-t1", P(1, 0, 2));
        d.Put("container-t1", P(0, 0, 3));
        d.Thruster("thruster-t1", P(-1, 0, 4), Dir.NegZ);
        d.Thruster("thruster-t1", P(1, 0, 4), Dir.NegZ);
        d.Thruster("thruster-t1", P(-3, 0, -3), Dir.PosZ);
        d.Thruster("thruster-t1", P(3, 0, -3), Dir.PosZ);
        foreach (int x in new[] { -2, 2 })
            foreach (int z in new[] { -2, 2 }) d.Put("hover-t1", P(x, -1, z));
        d.Stairs(P(-3, -1, 0), Dir.PosX, body);
        return d.Text(@"Hover Rover: a low, wide ground runner on four hover plates, built of painted base blocks.
An open cockpit behind a raked windscreen, stepped into from the port step; the engine bay behind
with the generator, gyro, fuel and a locker; two mains in the tail and a retro either side of the dash.");
    }

    /// <summary>The Dart: a light fighter, a cockpit pod on a slim fuselage with swept wings and four guns.</summary>
    static string Dart(Catalogue cat)
    {
        var d = new Designer(cat, "Dart Fighter", CoreMode.Capital);
        ushort hull = d.Paint("hull", Coat(0xece3c8)), belly = d.Paint("belly", Coat(0x2b2d42, 2)), deck = d.Paint("deck", Deck);
        ushort chev = d.Paint("chev", Coat(0xece3c8).WithPattern(Pattern.Chevrons, 0xe85d04)), red = d.Paint("red", Coat(0xc1121f));

        // Fuselage floor and swept wings (y -1); their tops are walked to the cockpit.
        d.Fill(P(-2, -1, -6), P(2, -1, 5), "iron-plated", belly);
        d.Fill(P(-7, -1, 0), P(-3, -1, 3), "iron-plated", belly).Fill(P(3, -1, 0), P(7, -1, 3), "iron-plated", belly);
        d.Face(P(-7, -1, -6), P(7, -1, 5), Dir.PosY, chev).Face(P(-2, -1, -4), P(2, -1, 4), Dir.PosY, deck);
        d.Fill(P(-7, 0, 2), P(-7, 1, 3), "iron-plated", red).Fill(P(7, 0, 2), P(7, 1, 3), "iron-plated", red);
        d.Fill(P(-1, 0, -6), P(1, 0, -6), "iron-plated", red, "slope").Fill(P(-1, 0, -5), P(1, 0, -5), "iron-plated", hull);
        d.Put("cockpit-t1", P(-1, 0, -4));
        // The bay behind the cockpit: generators, gyro and fuel, three mains in its tail.
        d.Fill(P(-1, 0, 0), P(1, 1, 4), "iron-plated", hull).Fill(P(-1, 1, 0), P(1, 1, 0), "iron-plated", hull, "slope");
        d.Put("core", P(0, -1, 0));
        d.Put("generator-t1", P(-1, 0, 1));
        d.Put("generator-t1", P(1, 0, 1));
        d.Put("gyro-t1", P(0, 0, 1));
        d.Put("fuel-tank-t1", P(0, 0, 2));
        for (int x = -1; x <= 1; x++) d.Thruster("thruster-t1", P(x, 0, 5), Dir.NegZ);
        d.Thruster("thruster-t1", P(-2, 0, -5), Dir.PosZ);
        d.Thruster("thruster-t1", P(2, 0, -5), Dir.PosZ);
        d.Thruster("thruster-t1", P(0, -1, -2), Dir.PosY);
        d.Thruster("thruster-t1", P(-5, -1, 1), Dir.PosY);
        d.Thruster("thruster-t1", P(5, -1, 1), Dir.PosY);
        d.Thruster("thruster-t1", P(0, 2, 2), Dir.NegY);
        d.Thruster("thruster-t1", P(-8, -1, 1), Dir.PosX);
        d.Thruster("thruster-t1", P(8, -1, 1), Dir.NegX);
        foreach (int x in new[] { -6, -4, 4, 6 }) d.Put("gatling", P(x, -1, -1));
        d.Put("pad-t1", P(0, -1, -6));
        d.Put("pad-t1", P(-1, -1, 3));
        d.Put("pad-t1", P(1, -1, 3));
        d.Stairs(P(-5, -1, 4), Dir.NegZ, hull);
        return d.Text(@"Dart Fighter: a light fighter. A Cockpit T1 on an iron fuselage, swept wings with fins at the
tips and four gatlings on their leading edges; up the step onto the port wing and along the
fuselage to the cockpit. Generators, gyro and fuel in the bay behind it, three mains, two retros,
three lift jets, a down jet, a side jet on each wingtip, three pads.");
    }

    /// <summary>The Shuttle: a walk-in people-and-cargo runner with two seats and cargo racks.</summary>
    static string Shuttle(Catalogue cat)
    {
        var d = new Designer(cat, "Shuttle", CoreMode.Capital);
        ushort hull = d.Paint("hull", Coat(0xece3c8)), inner = d.Paint("inner", Coat(0xd8d2c2, 0)), deck = d.Paint("deck", Deck);
        ushort band = d.Paint("band", Coat(0x0a9396)), pod = d.Paint("pod", Coat(0xe85d04)), hazard = d.Paint("hazard", Hazard);

        // A cabin 4 m wide, 3 m tall and 11 m long; a windscreen, a window band down each side.
        d.Room(P(-2, 0, -5), P(1, 2, 5), "iron-plated", hull, inner, deck);
        d.Face(P(-3, 0, -6), P(2, 0, 6), Dir.PosX, band).Face(P(-3, 0, -6), P(2, 0, 6), Dir.NegX, band);
        d.Fill(P(-2, 1, -6), P(1, 2, -6), "glass-plated").Fill(P(-3, 1, -4), P(-3, 1, -1), "glass-plated").Fill(P(2, 1, -4), P(2, 1, -1), "glass-plated");
        d.Put("core", P(2, -1, -1));
        d.Put("command-chair", P(-2, 0, -5));
        d.Put("command-chair", P(1, 0, -5));
        for (int z = 0; z <= 3; z++)
        {
            d.Put("container-t1", P(-2, 0, z));
            d.Put("container-t1", P(1, 0, z));
        }
        d.Mount("light", P(-1, 2, -2), Dir.PosY);
        d.Mount("light", P(0, 2, 2), Dir.PosY);
        // The hatch at the back, up a step.
        d.Door("door-2x2x1", P(-1, 0, 6), Dir.PosZ);
        d.Face(P(-3, 0, 6), P(2, 2, 6), Dir.PosZ, hazard);
        d.Stairs(P(-1, -1, 7), Dir.NegZ, hull);
        d.Stairs(P(0, -1, 7), Dir.NegZ, hull);
        // Engine pods on the flanks with the power, gyro and fuel; mains, side jets, retros, lift and down jets.
        d.Fill(P(-5, 0, 0), P(-4, 1, 5), "iron-plated", pod).Fill(P(3, 0, 0), P(4, 1, 5), "iron-plated", pod);
        d.Put("generator-t1", P(-5, 0, 2));
        d.Put("gyro-t1", P(-4, 0, 2));
        d.Put("fuel-tank-t1", P(-5, 1, 2));
        d.Put("generator-t1", P(4, 0, 2));
        d.Put("fuel-tank-t1", P(3, 0, 2));
        foreach (int x in new[] { -5, -4, 3, 4 }) d.Thruster("thruster-t1", P(x, 0, 6), Dir.NegZ);
        d.Thruster("thruster-t1", P(-6, 0, 3), Dir.PosX);
        d.Thruster("thruster-t1", P(5, 0, 3), Dir.NegX);
        d.Thruster("thruster-t1", P(-2, 0, -7), Dir.PosZ);
        d.Thruster("thruster-t1", P(1, 0, -7), Dir.PosZ);
        foreach (int x in new[] { -3, 2 })
            foreach (int z in new[] { -2, 2 }) d.Thruster("thruster-t1", P(x, -1, z), Dir.PosY);
        d.Thruster("thruster-t1", P(-1, 4, 0), Dir.NegY);
        foreach (int x in new[] { -3, 2 })
        {
            d.Put("pad-t1", P(x, -1, -5));
            d.Put("pad-t1", P(x, -1, 4));
        }
        return d.Text(@"Shuttle: a boxy people-and-cargo runner. An iron cabin 4 m wide, 3 m floor to ceiling and
11 m long: two command chairs side by side behind the windscreen, eight lockers down the walls
either side of a 2 m aisle, a double hatch at the back up a step. An engine pod on each flank holds
the generators, gyro and fuel; four mains, two retros, two side jets, four lift jets, a down jet.");
    }

    /// <summary>The Hover Tank: a low iron hull on four big hover plates, a cockpit and a turret on its deck.</summary>
    static string Tank(Catalogue cat)
    {
        var d = new Designer(cat, "Hover Tank", CoreMode.Hover);
        ushort sand = d.Paint("sand", Coat(0xc8b48a, 2)), belly = d.Paint("belly", Coat(0x2b2d42, 2)), deck = d.Paint("deck", Deck);
        ushort flash = d.Paint("flash", Coat(0xc8b48a, 2).WithPattern(Pattern.Stripes, 0x0a9396)), hazard = d.Paint("hazard", Hazard);

        // The hull (y -1..0) with its glacis and sponsons; its top is the deck.
        d.Fill(P(-4, -1, -7), P(4, 0, 7), "iron-plated", sand).Face(P(-4, -1, -7), P(4, -1, 7), Dir.NegY, belly).Face(P(-4, 0, -7), P(4, 0, 7), Dir.PosY, deck);
        d.Fill(P(-4, 0, -8), P(4, 0, -8), "hardened-plated", sand, "slope").Fill(P(-4, -1, -8), P(4, -1, -8), "hardened-plated", sand);
        d.Fill(P(-5, -1, -6), P(-5, 0, 6), "hardened-plated", flash).Fill(P(5, -1, -6), P(5, 0, 6), "hardened-plated", flash);
        d.Face(P(-4, -1, 7), P(4, 0, 7), Dir.PosZ, hazard);
        d.Put("core", P(0, -1, 0));
        d.Put("generator-t2", P(-1, -1, 2));
        d.Put("gyro-t2", P(0, -1, 2));
        d.Put("fuel-tank-t2", P(1, -1, 2));
        d.Put("container-t1", P(2, -1, 2));
        foreach (int x in new[] { -4, 3 })
            foreach (int z in new[] { -6, 5 }) d.Put("hover-t2", P(x, -1, z));
        // On deck: the driver's cockpit at the front, the turret behind it.
        d.Put("cockpit-t1", P(-1, 1, -6));
        d.Put("turret", P(0, 1, 1));
        // Mains at the stern, retros at the sponsons' noses; a stair up the stern plate.
        d.Thruster("thruster-t2", P(-3, 0, 8), Dir.NegZ);
        d.Thruster("thruster-t2", P(3, 0, 8), Dir.NegZ);
        d.Thruster("thruster-t2", P(-5, 0, -8), Dir.PosZ);
        d.Thruster("thruster-t2", P(5, 0, -8), Dir.PosZ);
        d.Fill(P(-1, -1, 8), P(0, -1, 8), "iron-plated", sand);
        d.Stairs(P(-1, 0, 8), Dir.NegZ, sand);
        d.Stairs(P(0, 0, 8), Dir.NegZ, sand);
        d.Stairs(P(-1, -1, 9), Dir.NegZ, sand);
        d.Stairs(P(0, -1, 9), Dir.NegZ, sand);
        return d.Text(@"Hover Tank: a low iron hull on four Hover plate T2s, a hardened glacis and armoured
sponsons. Up the stair at the stern onto the deck: the driver's Cockpit T1 forward, the turret
behind it. Generator, gyro, fuel and a locker in the hull; two mains at the stern and a retro on
each sponson.");
    }

    /// <summary>The Hauler: a cab, a spine of machinery and two long cargo pods of lockers.</summary>
    static string Hauler(Catalogue cat)
    {
        var d = new Designer(cat, "Hauler", CoreMode.Capital);
        ushort cream = d.Paint("cream", Coat(0xece3c8)), inner = d.Paint("inner", Coat(0xd8d2c2, 0)), deck = d.Paint("deck", Deck);
        ushort spine = d.Paint("spine", Coat(0x2b2d42, 2)), pod = d.Paint("pod", Coat(0xe85d04).WithPattern(Pattern.Stripes, 0x2b2d42)), hazard = d.Paint("hazard", Hazard);

        // The cab: 4 m wide, 3 m clear, 5 m deep, a windscreen and a door to port up a step.
        d.Room(P(-2, 0, -13), P(1, 2, -9), "iron-plated", cream, inner, deck);
        d.Fill(P(-2, 1, -14), P(1, 2, -14), "glass-plated");
        d.Put("command-chair", P(-2, 0, -13));
        d.Put("command-chair", P(1, 0, -13));
        d.Mount("light", P(-1, 2, -10), Dir.PosY);
        d.Door("door-1x2x1", P(-3, 0, -10), Dir.NegX);
        d.Stairs(P(-4, -1, -10), Dir.PosX, cream);
        // The spine with the machinery, and a cargo pod of lockers either side.
        d.Fill(P(-3, -1, -7), P(2, 1, 8), "iron-plated", spine).Face(P(-3, -1, 8), P(2, 1, 8), Dir.PosZ, hazard);
        d.Put("core", P(-1, 0, 0));
        d.Put("generator-t2", P(-2, -1, -6));
        d.Put("generator-t2", P(1, -1, -6));
        d.Put("gyro-t2", P(-1, 0, -5));
        d.Put("gyro-t2", P(0, 0, -5));
        d.Put("fuel-tank-t2", P(-1, 0, -3));
        d.Put("fuel-tank-t2", P(0, 0, -3));
        foreach (int x0 in new[] { -7, 3 })
        {
            d.Fill(P(x0, -1, -6), P(x0 + 3, 2, 8), "iron-plated", pod).Face(P(x0, -1, 8), P(x0 + 3, 2, 8), Dir.PosZ, hazard);
            for (int y = 0; y <= 1; y++)
                for (int z = -5; z <= 7; z++) d.Put("container-t4", P(x0 + 1, y, z));
            d.Put("pad-t2", P(x0 + 1, -1, -3));
            d.Put("pad-t2", P(x0 + 1, -1, 5));
        }
        // Mains in the tail, retros on the pods' noses, lift jets at the pods' corners, down and side jets.
        foreach (int x in new[] { -6, -2, 1, 5 }) d.Thruster("thruster-t3", P(x, 0, 9), Dir.NegZ);
        d.Thruster("thruster-t3", P(-6, 0, -8), Dir.PosZ);
        d.Thruster("thruster-t3", P(5, 0, -8), Dir.PosZ);
        foreach (int x in new[] { -7, 6 })
            foreach (int z in new[] { -6, 3 }) d.Thruster("thruster-t3", P(x, -1, z), Dir.PosY);
        d.Thruster("thruster-t1", P(-1, 2, 4), Dir.NegY);
        d.Thruster("thruster-t1", P(0, 2, 4), Dir.NegY);
        d.Thruster("thruster-t1", P(-8, 0, 1), Dir.PosX);
        d.Thruster("thruster-t1", P(7, 0, 1), Dir.NegX);
        return d.Text(@"Hauler: a space truck. An iron cab 4 m wide and 3 m clear behind a windscreen, two command
chairs, a door to port up a step; a spine behind it with the generators, gyros and fuel; two long
cargo pods holding 52 Container T4s. Four Thruster T3 mains, two retros, four lift jets at the pods'
corners, two down jets, a side jet each way, four pads.");
    }

    /// <summary>The Frigate: a small capital ship, two decks forward and a hangar aft that takes the Skiff.</summary>
    static string Frigate(Catalogue cat)
    {
        var d = new Designer(cat, "Frigate", CoreMode.Capital);
        ushort hull = d.Paint("hull", Coat(0xc9ccd1)), inner = d.Paint("inner", Coat(0xd8d2c2, 0)), deck = d.Paint("deck", Deck);
        ushort keel = d.Paint("keel", Coat(0x2b2d42, 2)), red = d.Paint("red", Coat(0xc1121f)), hazard = d.Paint("hazard", Hazard);

        // The hangar aft: 10 m wide, 6 m clear and 16 m deep, open at the stern.
        d.Room(P(-5, 1, -3), P(4, 6, 12), "iron-plated", hull, inner, deck);
        d.Carve(P(-5, 1, 13), P(4, 6, 13)).Face(P(-6, 0, 13), P(5, 7, 13), Dir.PosZ, hazard);
        // Forward: the machine room below (4 m clear) and the bridge above (4 m clear), joined by a stair.
        d.Room(P(-7, 1, -19), P(6, 9, -5), "iron-plated", hull, inner, deck);
        d.Fill(P(-7, 5, -19), P(6, 5, -5), "iron-plated", inner).Face(P(-7, 5, -19), P(6, 5, -5), Dir.PosY, deck);
        d.Face(P(-8, 0, -20), P(7, 0, 13), Dir.NegY, keel);
        d.Fill(P(-6, 7, -20), P(5, 8, -20), "glass-plated");
        for (int i = 0; i <= 4; i++)
        {
            if (i > 0) d.Fill(P(-1, 1, -6 - i), P(0, i, -6 - i), "iron-plated", inner);
            d.Stairs(P(-1, 1 + i, -6 - i), Dir.NegZ, inner);
            d.Stairs(P(0, 1 + i, -6 - i), Dir.NegZ, inner);
        }
        d.Carve(P(-1, 5, -9), P(0, 5, -8));
        d.Door("door-2x2x1", P(-1, 1, -4), Dir.NegZ);
        d.Put("core", P(3, 0, -12));
        d.Put("command-chair", P(-2, 6, -19));
        d.Put("command-chair", P(1, 6, -19));
        d.Put("generator-t2", P(-7, 1, -19));
        d.Put("generator-t2", P(-7, 1, -18));
        d.Put("fuel-tank-t2", P(-7, 1, -17));
        d.Put("fuel-tank-t2", P(-7, 1, -15));
        d.Put("gyro-t2", P(6, 1, -19));
        d.Put("gyro-t2", P(6, 1, -18));
        for (int z = -17; z <= -14; z++) d.Put("container-t4", P(5, 1, z));
        foreach (int x in new[] { -4, 3 })
        {
            d.Mount("light", P(x, 4, -14), Dir.PosY);
            d.Mount("light", P(x, 9, -14), Dir.PosY);
        }
        // Engine nacelles down the hangar's flanks, the mains in their tails.
        d.Fill(P(-8, 1, -3), P(-7, 5, 10), "iron-plated", hull).Fill(P(6, 1, -3), P(7, 5, 10), "iron-plated", hull);
        d.Face(P(-8, 3, -3), P(-8, 3, 10), Dir.NegX, red).Face(P(7, 3, -3), P(7, 3, 10), Dir.PosX, red);
        foreach (int x in new[] { -8, -7, 6, 7 }) d.Thruster("thruster-t3", P(x, 3, 11), Dir.NegZ);
        foreach (int x in new[] { -8, 7 })
            foreach (int y in new[] { 1, 3 }) d.Thruster("thruster-t3", P(x, y, -22), Dir.PosZ);
        foreach (int z in new[] { -12, 4 })
        {
            d.Thruster("thruster-t3", P(-10, 3, z), Dir.PosX);
            d.Thruster("thruster-t3", P(8, 3, z), Dir.NegX);
        }
        foreach (var (x, z) in new[] { (-6, -2), (5, -2), (-6, 3), (5, 3), (-8, -15), (7, -15), (-8, -9), (7, -9) })
            d.Thruster("thruster-t3", P(x, 0, z), Dir.PosY);
        d.Thruster("thruster-t3", P(-3, 11, -12), Dir.NegY);
        d.Thruster("thruster-t3", P(2, 11, -12), Dir.NegY);
        d.Thruster("thruster-t3", P(-3, 8, 6), Dir.NegY);
        d.Thruster("thruster-t3", P(2, 8, 6), Dir.NegY);
        foreach (var (x, z) in new[] { (-8, -18), (7, -18), (-6, 10), (5, 10) }) d.Put("pad-t3", P(x, 0, z));
        d.Stairs(P(-1, 0, 14), Dir.NegZ, hull);
        d.Stairs(P(0, 0, 14), Dir.NegZ, hull);
        return d.Text(@"Frigate: a small capital ship in iron. Forward, two decks 4 m floor to ceiling joined by a
2 m stair: the machine room below (generators, fuel, gyros, lockers) and the bridge above, two
command chairs behind a glazed brow. Aft, a hangar 10 m wide, 6 m clear and 16 m deep, open at the
stern, that takes the Skiff; a double door from it into the machine room, a step up at its mouth.
Engine nacelles down both flanks; Thruster T3s throughout.");
    }
}
