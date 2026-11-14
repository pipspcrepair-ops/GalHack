using System;
using System.Linq;
using GalHack.Structures;

namespace Slice.Tests;

/// <summary>The rest of the slice's starter set: a hover rover, a light fighter, a shuttle, a hover tank, a hauler and a small capital ship.</summary>
public static partial class BlueprintSet
{
    /// <summary>The Hover Rover: a low open runner on four hover plates.</summary>
    static string Rover(Catalogue cat)
    {
        var d = new Designer(cat, "Hover Rover", CoreMode.Hover) { Seal = true };
        ushort body = d.Paint("body", Coat(0xf4a100)), belly = d.Paint("belly", Coat(0x2b2d42, 2)), deck = d.Paint("deck", Deck);
        ushort stripe = d.Paint("stripe", Coat(0xf4a100).WithPattern(Pattern.Stripes, 0x2b2d42)), hazard = d.Paint("hazard", Hazard);

        d.Fill(P(-2, -1, -4), P(2, -1, 3), "base-block", belly).Face(P(-1, -1, -2), P(1, -1, 0), Dir.PosY, deck).Face(P(-2, -1, 0), P(-2, -1, 0), Dir.PosY, deck);
        d.Put("core", P(0, -1, 1));
        d.Fill(P(-1, 0, -4), P(1, 0, -4), "base-block", body, "slope").Fill(P(-2, 0, -3), P(2, 0, -3), "base-block", hazard);
        d.Chamfer(P(2, 0, -4), P(2, 0, -4), "base-block", body, Dir.PosX, Dir.NegZ).Chamfer(P(-2, 0, -4), P(-2, 0, -4), "base-block", body, Dir.NegX, Dir.NegZ);
        d.Chamfer(P(2, -1, -4), P(2, -1, -4), "base-block", belly, Dir.PosX, Dir.NegZ).Chamfer(P(-2, -1, -4), P(-2, -1, -4), "base-block", belly, Dir.NegX, Dir.NegZ);
        d.Slope(P(-2, 1, 3), P(2, 1, 3), "base-block", stripe, Dir.PosZ); // a raked spoiler over the tail
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
        var d = new Designer(cat, "Dart Fighter", CoreMode.Flight) { Seal = true };
        ushort hull = d.Paint("hull", Coat(0xece3c8)), belly = d.Paint("belly", Coat(0x2b2d42, 2)), deck = d.Paint("deck", Deck);
        ushort chev = d.Paint("chev", Coat(0xece3c8).WithPattern(Pattern.Chevrons, 0xe85d04)), red = d.Paint("red", Coat(0xc1121f));

        // Fuselage floor (y -1) with a pointed nose, and delta wings whose leading edges are 45 degree chamfers; their tops are walked to the cockpit.
        const string Iron = "iron-plated";
        d.Fill(P(-2, -1, -5), P(2, -1, 5), Iron, belly).Fill(P(-1, -1, -6), P(1, -1, -6), Iron, belly);
        d.Chamfer(P(2, -1, -6), P(2, -1, -6), Iron, belly, Dir.PosX, Dir.NegZ).Chamfer(P(-2, -1, -6), P(-2, -1, -6), Iron, belly, Dir.NegX, Dir.NegZ);
        for (int k = 0; k <= 4; k++)
            foreach (int s in new[] { 1, -1 })
            {
                int x = s * (3 + k);
                d.Fill(P(x, -1, k), P(x, -1, 4), Iron, belly);
                d.Chamfer(P(x, -1, k - 1), P(x, -1, k - 1), Iron, red, s > 0 ? Dir.PosX : Dir.NegX, Dir.NegZ);
            }
        d.Face(P(-7, -1, -6), P(7, -1, 5), Dir.PosY, chev).Face(P(-2, -1, -4), P(2, -1, 4), Dir.PosY, deck);
        // Winglets at the tips and a raked fin on each side of the tail.
        foreach (int x in new[] { -7, 7 }) d.Fill(P(x, 0, 4), P(x, 0, 4), Iron, red).Fill(P(x - Math.Sign(x), 0, 4), P(x - Math.Sign(x), 0, 4), Iron, red);
        d.Fill(P(0, 0, -6), P(0, 0, -6), Iron, red, "slope").Fill(P(-1, 0, -5), P(1, 0, -5), Iron, hull);
        d.Chamfer(P(1, 0, -6), P(1, 0, -6), Iron, red, Dir.PosX, Dir.NegZ).Chamfer(P(-1, 0, -6), P(-1, 0, -6), Iron, red, Dir.NegX, Dir.NegZ);
        d.Put("cockpit-t1", P(-1, 0, -4));
        // The bay behind the cockpit: generators, gyro and fuel, three mains in its tail.
        d.Fill(P(-1, 0, 0), P(1, 1, 4), "iron-plated", hull).Fill(P(0, 1, 0), P(0, 1, 0), "iron-plated", hull, "slope").Fill(P(-1, 1, 0), P(-1, 1, 0), "iron-plated", hull).Fill(P(1, 1, 0), P(1, 1, 0), "iron-plated", hull);
        d.Put("core", P(0, -1, 0));
        d.Put("generator-t1", P(-1, 0, 1));
        d.Put("generator-t1", P(1, 0, 1));
        d.Put("gyro-t1", P(0, 0, 1));
        d.Put("fuel-tank-t1", P(0, 0, 2));
        for (int x = -1; x <= 1; x++) d.Thruster("thruster-t1", P(x, 0, 5), Dir.NegZ);
        d.Thruster("thruster-t1", P(-2, 0, -5), Dir.PosZ);
        d.Thruster("thruster-t1", P(2, 0, -5), Dir.PosZ);
        d.Thruster("thruster-t1", P(0, -1, -2), Dir.PosY);
        d.Thruster("thruster-t1", P(-5, -1, 2), Dir.PosY);
        d.Thruster("thruster-t1", P(5, -1, 2), Dir.PosY);
        d.Thruster("thruster-t1", P(0, 2, 2), Dir.NegY);
        d.Thruster("thruster-t1", P(-8, -1, 4), Dir.PosX);
        d.Thruster("thruster-t1", P(8, -1, 4), Dir.NegX);
        // Four gatlings on the wings' leading edges, each on a small pod on the wing behind its chamfer.
        foreach (int x in new[] { -6, -4, 4, 6 })
        {
            d.Fill(P(x, 0, Math.Abs(x) - 2), P(x, 0, Math.Abs(x) - 2), Iron, hull);
            d.Put("gatling", P(x, 0, Math.Abs(x) - 3));
        }
        foreach (int x in new[] { -1, 1 })
        {
            d.Fill(P(x, 2, 3), P(x, 2, 4), Iron, red).Fill(P(x, 3, 3), P(x, 3, 3), Iron, red);
            d.Slope(P(x, 3, 4), P(x, 3, 4), Iron, red, Dir.PosZ);
        }
        d.Put("pad-t1", P(0, -1, -6));
        d.Put("pad-t1", P(-1, -1, 3));
        d.Put("pad-t1", P(1, -1, 3));
        d.Stairs(P(-5, -1, 4), Dir.NegZ, hull);
        return d.Text(@"Dart Fighter: a light fighter. A Cockpit T1 on an iron fuselage, delta wings with winglets at the
tips, twin tail fins and four gatlings on their leading edges; up the step onto the port wing and along the
fuselage to the cockpit. Generators, gyro and fuel in the bay behind it, three mains, two retros,
three lift jets, a down jet, a side jet on each wingtip, three pads.");
    }

    /// <summary>The Shuttle: a walk-in people-and-cargo runner with two seats and cargo racks.</summary>
    static string Shuttle(Catalogue cat)
    {
        var d = new Designer(cat, "Shuttle", CoreMode.Flight) { Seal = true };
        ushort hull = d.Paint("hull", Coat(0xece3c8)), inner = d.Paint("inner", Coat(0xd8d2c2, 0)), deck = d.Paint("deck", Deck);
        ushort band = d.Paint("band", Coat(0x0a9396)), pod = d.Paint("pod", Coat(0xe85d04)), hazard = d.Paint("hazard", Hazard);

        // A cabin 4 m wide, 3 m tall and 11 m long; a windscreen, a window band down each side.
        d.Room(P(-2, 0, -5), P(1, 2, 5), "iron-plated", hull, inner, deck);
        d.Face(P(-3, 0, -6), P(2, 0, 6), Dir.PosX, band).Face(P(-3, 0, -6), P(2, 0, 6), Dir.NegX, band);
        d.Fill(P(-2, 1, -6), P(1, 2, -6), "glass-plated").Fill(P(-3, 1, -4), P(-3, 1, -1), "glass-plated").Fill(P(2, 1, -4), P(2, 1, -1), "glass-plated");
        // A blunt nose: its corners chamfered, the roof and belly raked to it and the roof's edges raked to the sides, a tail fin aft.
        for (int y = -1; y <= 2; y++)
            d.Chamfer(P(2, y, -6), P(2, y, -6), "iron-plated", hull, Dir.PosX, Dir.NegZ).Chamfer(P(-3, y, -6), P(-3, y, -6), "iron-plated", hull, Dir.NegX, Dir.NegZ);
        d.Slope(P(-2, 3, -6), P(1, 3, -6), "iron-plated", pod, Dir.NegZ).Fill(P(2, 3, -6), P(2, 3, -6), "iron-plated", pod, "corner").Fill(P(-3, 3, -6), P(-3, 3, -6), "iron-plated", pod, "corner", Designer.Rot(Dir.PosY, Dir.NegX)).Slope(P(-2, -1, -6), P(1, -1, -6), "iron-plated", hull, Dir.NegZ, under: true);
        d.Slope(P(-3, 3, -5), P(-3, 3, 5), "iron-plated", pod, Dir.NegX).Slope(P(2, 3, -5), P(2, 3, 5), "iron-plated", pod, Dir.PosX);
        d.Fill(P(-1, 4, 3), P(0, 4, 5), "iron-plated", pod).Fill(P(-1, 5, 4), P(0, 5, 5), "iron-plated", pod).Slope(P(-1, 5, 3), P(0, 5, 3), "iron-plated", pod, Dir.NegZ);
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
        // Engine pods on the flanks, each on a pylon, with the power, gyro and fuel; the mains in their tails.
        d.Fill(P(-4, 0, 2), P(-4, 1, 3), "iron-plated", hull).Fill(P(3, 0, 2), P(3, 1, 3), "iron-plated", hull);
        d.Fill(P(-6, 0, 0), P(-5, 1, 5), "iron-plated", pod).Fill(P(4, 0, 0), P(5, 1, 5), "iron-plated", pod);
        d.Slope(P(-6, 1, 0), P(-5, 1, 0), "iron-plated", pod, Dir.NegZ).Slope(P(4, 1, 0), P(5, 1, 0), "iron-plated", pod, Dir.NegZ);
        d.Slope(P(-6, 0, 0), P(-5, 0, 0), "iron-plated", hull, Dir.NegZ, under: true).Slope(P(4, 0, 0), P(5, 0, 0), "iron-plated", hull, Dir.NegZ, under: true);
        d.Put("generator-t1", P(-6, 0, 2));
        d.Put("gyro-t1", P(-5, 0, 2));
        d.Put("fuel-tank-t1", P(-6, 1, 2));
        d.Put("generator-t1", P(5, 0, 2));
        d.Put("fuel-tank-t1", P(4, 0, 2));
        foreach (int x in new[] { -6, -5, 4, 5 }) d.Thruster("thruster-t1", P(x, 0, 6), Dir.NegZ);
        d.Thruster("thruster-t1", P(-7, 0, 3), Dir.PosX);
        d.Thruster("thruster-t1", P(6, 0, 3), Dir.NegX);
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
either side of a 2 m aisle, a double hatch at the back up a step. An engine pod on a pylon on each
flank holds a generator and fuel (and the gyro to port); four mains in the pods, two retros, two
side jets, four lift jets, a down jet.");
    }

    /// <summary>The Hover Tank: a low iron hull on eight hover plates, a cockpit and a turret on its deck.</summary>
    static string Tank(Catalogue cat)
    {
        var d = new Designer(cat, "Hover Tank", CoreMode.Hover) { Seal = true };
        ushort sand = d.Paint("sand", Coat(0xc8b48a, 2)), belly = d.Paint("belly", Coat(0x2b2d42, 2)), deck = d.Paint("deck", Deck);
        ushort flash = d.Paint("flash", Coat(0xc8b48a, 2).WithPattern(Pattern.Stripes, 0x0a9396)), hazard = d.Paint("hazard", Hazard);

        // The hull (y -1..0) with its glacis and sponsons; its top is the deck.
        d.Fill(P(-4, -1, -7), P(4, 0, 7), "iron-plated", sand).Face(P(-4, -1, -7), P(4, -1, 7), Dir.NegY, belly).Face(P(-4, 0, -7), P(4, 0, 7), Dir.PosY, deck);
        d.Fill(P(-4, 0, -8), P(4, 0, -8), "hardened-plated", sand, "slope").Fill(P(-4, -1, -8), P(4, -1, -8), "hardened-plated", sand);
        d.Fill(P(-5, -1, -6), P(-5, 0, 6), "hardened-plated", flash).Fill(P(5, -1, -6), P(5, 0, 6), "hardened-plated", flash);
        d.Face(P(-4, -1, 7), P(4, 0, 7), Dir.PosZ, hazard);
        // Sponson tops raked outward, the glacis corners cut, intake stacks on the engine deck.
        d.Slope(P(-5, 0, -5), P(-5, 0, 5), "hardened-plated", flash, Dir.NegX).Slope(P(5, 0, -5), P(5, 0, 5), "hardened-plated", flash, Dir.PosX);
        d.Chamfer(P(4, -1, -8), P(4, -1, -8), "hardened-plated", sand, Dir.PosX, Dir.NegZ).Chamfer(P(-4, -1, -8), P(-4, -1, -8), "hardened-plated", sand, Dir.NegX, Dir.NegZ);
        d.Chamfer(P(4, -1, 7), P(4, 0, 7), "iron-plated", sand, Dir.PosX, Dir.PosZ).Chamfer(P(-4, -1, 7), P(-4, 0, 7), "iron-plated", sand, Dir.NegX, Dir.PosZ);
        foreach (int x in new[] { -2, 2 }) d.Fill(P(x, 1, 4), P(x, 2, 4), "iron-plated", belly, "cylinder");
        d.Put("core", P(0, -1, 0));
        d.Put("generator-t2", P(-1, -1, 2));
        d.Put("gyro-t2", P(0, -1, 2));
        d.Put("fuel-tank-t2", P(1, -1, 2));
        d.Put("container-t1", P(2, -1, 2));
        foreach (int x in new[] { -4, 4 })
            foreach (int z in new[] { -6, -2, 2, 6 }) d.Put("hover-t1", P(x, -1, z));
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
        return d.Text(@"Hover Tank: a low iron hull on eight Hover plate T1s, a hardened glacis and armoured
sponsons. Up the stair at the stern onto the deck: the driver's Cockpit T1 forward, the turret
behind it. Generator, gyro, fuel and a locker in the hull; two mains at the stern and a retro on
each sponson.");
    }

    /// <summary>The Hauler: a cab, a spine of machinery and two long cargo pods of lockers.</summary>
    static string Hauler(Catalogue cat)
    {
        var d = new Designer(cat, "Hauler", CoreMode.Flight) { Seal = true };
        ushort cream = d.Paint("cream", Coat(0xece3c8)), inner = d.Paint("inner", Coat(0xd8d2c2, 0)), deck = d.Paint("deck", Deck);
        ushort spine = d.Paint("spine", Coat(0x2b2d42, 2)), pod = d.Paint("pod", Coat(0xe85d04).WithPattern(Pattern.Stripes, 0x2b2d42)), hazard = d.Paint("hazard", Hazard);

        // The cab: 4 m wide, 3 m clear, 5 m deep, a windscreen and a door to port up a step.
        d.Room(P(-2, 0, -13), P(1, 2, -9), "iron-plated", cream, inner, deck);
        d.Fill(P(-2, 1, -14), P(1, 2, -14), "glass-plated");
        d.Put("command-chair", P(-2, 0, -13));
        d.Put("command-chair", P(1, 0, -13));
        // The cab's nose cut at its corners, the roof and belly raked to the glass, the roof's edges raked to the sides.
        for (int y = -1; y <= 2; y++)
            d.Chamfer(P(2, y, -14), P(2, y, -14), "iron-plated", cream, Dir.PosX, Dir.NegZ).Chamfer(P(-3, y, -14), P(-3, y, -14), "iron-plated", cream, Dir.NegX, Dir.NegZ);
        d.Slope(P(-2, 3, -14), P(1, 3, -14), "iron-plated", spine, Dir.NegZ).Fill(P(2, 3, -14), P(2, 3, -14), "iron-plated", spine, "corner").Fill(P(-3, 3, -14), P(-3, 3, -14), "iron-plated", spine, "corner", Designer.Rot(Dir.PosY, Dir.NegX)).Slope(P(-2, -1, -14), P(1, -1, -14), "iron-plated", spine, Dir.NegZ, under: true);
        d.Slope(P(-3, 3, -13), P(-3, 3, -9), "iron-plated", spine, Dir.NegX).Slope(P(2, 3, -13), P(2, 3, -9), "iron-plated", spine, Dir.PosX);
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
            // Each pod's nose and tail raked, its top edge rounded off outboard.
            int outer = x0 < 0 ? x0 : x0 + 3;
            d.Slope(P(x0, 2, -6), P(x0 + 3, 2, -6), "iron-plated", pod, Dir.NegZ).Slope(P(x0, 2, 8), P(x0 + 3, 2, 8), "iron-plated", pod, Dir.PosZ);
            d.Fill(P(outer, 2, -6), P(outer, 2, -6), "iron-plated", pod, "corner", x0 < 0 ? Designer.Rot(Dir.PosY, Dir.NegX) : null);
            d.Slope(P(outer, 2, -5), P(outer, 2, 7), "iron-plated", pod, x0 < 0 ? Dir.NegX : Dir.PosX);
            d.Chamfer(P(outer, -1, -6), P(outer, 1, -6), "iron-plated", pod, x0 < 0 ? Dir.NegX : Dir.PosX, Dir.NegZ);
            d.Put("pad-t2", P(x0 + 1, -1, -3));
            d.Put("pad-t2", P(x0 + 1, -1, 5));
        }
        // Mains in the tail, retros on the pods' noses, lift jets at the pods' corners, down and side jets.
        foreach (int x in new[] { -6, -2, 1, 5 }) d.Thruster("thruster-t3", P(x, 0, 9), Dir.NegZ);
        d.Thruster("thruster-t3", P(-6, 0, -8), Dir.PosZ);
        d.Thruster("thruster-t3", P(5, 0, -8), Dir.PosZ);
        foreach (int x in new[] { -7, 6 })
            foreach (int z in new[] { -5, 3 }) d.Thruster("thruster-t3", P(x, -1, z), Dir.PosY);
        d.Fill(P(-1, 2, -4), P(0, 2, 1), "iron-plated", spine).Slope(P(-1, 2, -5), P(0, 2, -5), "iron-plated", spine, Dir.NegZ);
        d.Thruster("thruster-t1", P(-1, 2, 4), Dir.NegY);
        d.Thruster("thruster-t1", P(0, 2, 4), Dir.NegY);
        d.Thruster("thruster-t1", P(-8, 0, 1), Dir.PosX);
        d.Thruster("thruster-t1", P(7, 0, 1), Dir.NegX);
        return d.Text(@"Hauler: a space truck. An iron cab 4 m wide and 3 m clear behind a windscreen, two command
chairs, a door to port up a step; a spine behind it with the generators, gyros and fuel; two long
cargo pods holding 52 Container T4s. Four Thruster T3 mains, two retros, four lift jets at the pods'
corners, two down jets, a side jet each way, four pads.");
    }

    /// <summary>The Frigate: a small capital ship, two decks forward under a tapering bow and a hangar aft that takes the Skiff.</summary>
    static string Frigate(Catalogue cat)
    {
        var d = new Designer(cat, "Frigate", CoreMode.Flight) { Seal = true };
        ushort hull = d.Paint("hull", Coat(0xc9ccd1)), inner = d.Paint("inner", Coat(0xd8d2c2, 0)), deck = d.Paint("deck", Deck);
        ushort keel = d.Paint("keel", Coat(0x2b2d42, 2)), red = d.Paint("red", Coat(0xc1121f)), hazard = d.Paint("hazard", Hazard);
        ushort roof = d.Paint("roof", Coat(0x6b7280, 2));
        const string Iron = "iron-plated";

        // The forward hull, z -20 to -4, 11 m tall: the machine room (y 1..4) under the deck (y 5) and the bridge (y 6..9).
        // Its sides run out from the nose at one clean 45 degree line to full width at z -16 (a vertical chamfer on each row's
        // outer column, no steps), and the roof's edges are raked.
        for (int z = -20; z <= -4; z++)
        {
            int hw = Math.Min(8, 4 + z + 20);
            d.Fill(P(-hw, 0, z), P(hw - 1, 10, z), Iron, hull);
            if (z > -20 && z < -4)
                d.Hollow(P(1 - hw, 1, z), P(hw - 2, 4, z)).Hollow(P(1 - hw, 6, z), P(hw - 2, 9, z));
            if (hw == 8) d.Slope(P(hw - 1, 10, z), P(hw - 1, 10, z), Iron, roof, Dir.PosX).Slope(P(-hw, 10, z), P(-hw, 10, z), Iron, roof, Dir.NegX);
            if (hw < 8)
                foreach (int y in Enumerable.Range(0, 11))
                {
                    ushort c = y == 5 ? red : hull;
                    d.Chamfer(P(hw - 1, y, z), P(hw - 1, y, z), Iron, c, Dir.PosX, Dir.NegZ).Chamfer(P(-hw, y, z), P(-hw, y, z), Iron, c, Dir.NegX, Dir.NegZ);
                }
            d.Face(P(hw - 1, 5, z), P(hw - 1, 5, z), Dir.PosX, red).Face(P(-hw, 5, z), P(-hw, 5, z), Dir.NegX, red);
            // A window band down the flanks of the bridge.
            if (hw == 8 && z < -5) d.Fill(P(7, 7, z), P(7, 8, z), "glass-plated").Fill(P(-8, 7, z), P(-8, 8, z), "glass-plated");
        }
        // The prow: a glazed front, flat roof and flat belly; ahead of it a spear of three rows narrowing at 45 degrees, flat underneath
        // (level with the second metre) and flat on top (level with the sill), its sides chamfered the whole way.
        d.Fill(P(-3, 7, -20), P(2, 8, -20), "glass-plated");
        d.Face(P(-8, 10, -20), P(7, 10, -4), Dir.PosY, roof).Face(P(-8, 0, -20), P(7, 0, 13), Dir.NegY, keel);
        for (int k = 1; k <= 3; k++)
        {
            int z = -20 - k, w = 4 - k;
            d.Fill(P(-w, 1, z), P(w - 1, 6, z), Iron, hull);
            d.Chamfer(P(w - 1, 1, z), P(w - 1, 6, z), Iron, hull, Dir.PosX, Dir.NegZ).Chamfer(P(-w, 1, z), P(-w, 6, z), Iron, hull, Dir.NegX, Dir.NegZ);
            d.Face(P(-w, 1, z), P(w - 1, 1, z), Dir.NegY, keel).Face(P(-w, 6, z), P(w - 1, 6, z), Dir.PosY, roof);
        }

        // The hangar aft: 10 m wide, 6 m clear and 16 m deep, open at the stern, its roof edges raked down to the sides.
        d.Room(P(-5, 1, -3), P(4, 6, 12), Iron, hull, inner, deck);
        d.Carve(P(-5, 1, 13), P(4, 6, 13));
        d.Slope(P(-6, 7, -3), P(-6, 7, 12), Iron, roof, Dir.NegX).Slope(P(5, 7, -3), P(5, 7, 12), Iron, roof, Dir.PosX);
        d.Slope(P(-5, 7, 13), P(4, 7, 13), Iron, roof, Dir.PosZ).Chamfer(P(-6, 0, 13), P(-6, 6, 13), Iron, hull, Dir.NegX, Dir.PosZ).Chamfer(P(5, 0, 13), P(5, 6, 13), Iron, hull, Dir.PosX, Dir.PosZ);
        d.Fill(P(-6, 7, 13), P(-6, 7, 13), Iron, roof, "corner", Designer.Rot(Dir.PosY, Dir.PosZ)).Fill(P(5, 7, 13), P(5, 7, 13), Iron, roof, "corner", Designer.Rot(Dir.PosY, Dir.PosX));
        d.Face(P(-6, 0, 13), P(5, 7, 13), Dir.PosZ, hazard);
        // The bridge tower runs down to the hangar roof in a ramp.
        d.Fill(P(-6, 8, -3), P(5, 8, -3), Iron, hull).Slope(P(-6, 9, -3), P(5, 9, -3), Iron, roof, Dir.PosZ).Slope(P(-6, 8, -2), P(5, 8, -2), Iron, roof, Dir.PosZ);

        // The stair, 3 m wide and 5 m long, rises through the deck at its far end: the deck is open over all but its first
        // metre (and a 2 m apron before it), so a body is under no edge of it until it has risen to the opening's end.
        for (int i = 0; i <= 4; i++)
        {
            if (i > 0) d.Fill(P(-2, 1, -7 - i), P(0, i, -7 - i), Iron, inner);
            for (int x = -2; x <= 0; x++) d.Stairs(P(x, 1 + i, -7 - i), Dir.NegZ, inner);
        }
        d.Carve(P(-2, 5, -10), P(0, 5, -8));
        // The flight's flanks are plain walls to the deck, so no stepped stringer shows.
        d.Fill(P(-3, 1, -11), P(-3, 4, -7), Iron, inner).Fill(P(1, 1, -11), P(1, 4, -7), Iron, inner);
        d.Door("door-2x2x1", P(-1, 1, -4), Dir.NegZ);

        d.Put("core", P(3, 0, -12));
        d.Put("command-chair", P(-1, 6, -19));
        d.Put("command-chair", P(0, 6, -19));
        // The machine room: power and fuel at the stern end, gyros opposite, lockers down the bow.
        d.Put("generator-t2", P(-7, 1, -8));
        d.Put("generator-t2", P(-7, 1, -7));
        d.Put("fuel-tank-t2", P(-7, 1, -6));
        d.Put("fuel-tank-t2", P(-7, 2, -6));
        d.Put("gyro-t2", P(6, 1, -8));
        d.Put("gyro-t2", P(6, 1, -7));
        for (int z = -16; z <= -14; z++)
        {
            d.Put("container-t4", P(-4, 1, z));
            d.Put("container-t4", P(2, 1, z));
        }
        foreach (int x in new[] { -4, 3 })
        {
            d.Mount("light", P(x, 4, -14), Dir.PosY);
            d.Mount("light", P(x, 9, -14), Dir.PosY);
        }
        // Sensor drums on the bridge roof.
        d.Fill(P(-5, 11, -7), P(-5, 11, -7), Iron, roof, "cylinder").Fill(P(4, 11, -7), P(4, 11, -7), Iron, roof, "cylinder");

        // Engine nacelles down the hangar's flanks, rounded top and bottom and raked at the tail, the mains in them.
        d.Fill(P(-8, 1, -3), P(-7, 5, 10), Iron, hull).Fill(P(6, 1, -3), P(7, 5, 10), Iron, hull);
        d.Slope(P(-8, 5, -3), P(-8, 5, 9), Iron, roof, Dir.NegX).Slope(P(7, 5, -3), P(7, 5, 9), Iron, roof, Dir.PosX);
        d.Slope(P(-8, 1, -3), P(-8, 1, 9), Iron, keel, Dir.NegX, under: true).Slope(P(7, 1, -3), P(7, 1, 9), Iron, keel, Dir.PosX, under: true);
        d.Slope(P(-8, 5, 10), P(-7, 5, 10), Iron, roof, Dir.PosZ).Slope(P(6, 5, 10), P(7, 5, 10), Iron, roof, Dir.PosZ);
        d.Slope(P(-8, 1, 10), P(-7, 1, 10), Iron, keel, Dir.PosZ, under: true).Slope(P(6, 1, 10), P(7, 1, 10), Iron, keel, Dir.PosZ, under: true);
        d.Face(P(-8, 3, -3), P(-8, 3, 10), Dir.NegX, red).Face(P(7, 3, -3), P(7, 3, 10), Dir.PosX, red);
        foreach (int x in new[] { -8, -7, 6, 7 }) d.Thruster("thruster-t3", P(x, 3, 11), Dir.NegZ);
        // Retros on the prow, jets down the flanks.
        foreach (int x in new[] { -2, 1 }) d.Thruster("thruster-t3", P(x, 7, -22), Dir.PosZ);
        d.Thruster("thruster-t3", P(-10, 3, -12), Dir.PosX);
        d.Thruster("thruster-t3", P(8, 3, -12), Dir.NegX);
        foreach (int x in new[] { -10, 8 }) d.Thruster("thruster-t3", P(x, 3, 4), x < 0 ? Dir.PosX : Dir.NegX);
        foreach (var (x, z) in new[] { (-8, -12), (7, -12), (-8, -10), (7, -10), (-6, -2), (5, -2), (-6, 3), (5, 3) })
            d.Thruster("thruster-t3", P(x, 0, z), Dir.PosY);
        d.Thruster("thruster-t3", P(-3, 11, -12), Dir.NegY);
        d.Thruster("thruster-t3", P(2, 11, -12), Dir.NegY);
        d.Thruster("thruster-t3", P(-3, 8, 6), Dir.NegY);
        d.Thruster("thruster-t3", P(2, 8, 6), Dir.NegY);
        foreach (var (x, z) in new[] { (-8, -16), (7, -16), (-6, 10), (5, 10) }) d.Put("pad-t1", P(x, 0, z));
        d.Stairs(P(-1, 0, 14), Dir.NegZ, hull);
        d.Stairs(P(0, 0, 14), Dir.NegZ, hull);
        d.Lining(inner, deck);
        return d.Text(@"Frigate: a small capital ship in iron. Forward, a bow that tapers to a glazed prow over two decks
4 m floor to ceiling joined by a 3 m stair: the machine room below (generators, fuel, gyros,
lockers) and the bridge above, two command chairs behind the brow. Aft, a hangar 10 m wide, 6 m
clear and 16 m deep, open at the stern, that takes the Skiff; a double door from it into the
machine room, a step up at its mouth. Rounded engine nacelles down both flanks; Thruster T3s throughout.");
    }
}
