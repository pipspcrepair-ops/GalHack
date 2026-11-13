using System.Collections.Generic;
using Godot;

namespace ShipHandling.Handling;

/// <summary>
/// The three prototype vessels as plain component data: thrusters with facing, thrust and mass;
/// gyros; hover engines; landing gear; hull and cargo mass; and the blocks they are drawn and
/// collided from. Part numbers come from the tunables file, so retuning a part retunes every ship.
/// In the game these come from the canonical block model instead (see IVesselStructure).
/// </summary>
public static class VesselDesigns
{
    public static VesselLoadout Create(int index, Tunables t) => index switch
    {
        0 => Rover(t),
        1 => Fighter(t),
        _ => Capital(t),
    };

    /// <summary>
    /// A light hover rover. The origin is the plane of its hover engines, which ride 1.5 m up;
    /// nothing hangs below it, so ride height is also the clearance under the skirt.
    /// </summary>
    public static VesselLoadout Rover(Tunables t)
    {
        var v = new VesselLoadout
        {
            Name = "Sand Flea", HoverMode = true, MainEngine = "small", HullMass = 4f, Cockpit = new Vector3(0, 1.75f, -0.5f),
        };
        var b = new Builder(v, t);

        foreach (float x in new[] { -1.2f, 1.2f })
            foreach (float z in new[] { -1.9f, 1.9f })
                b.Hover("small", new Vector3(x, 0, z));
        b.Thruster("small", new Vector3(-0.8f, 0.85f, 2.75f), Vector3.Forward);
        b.Thruster("small", new Vector3(0.8f, 0.85f, 2.75f), Vector3.Forward);
        b.Thruster("small", new Vector3(0, 0.45f, -2.75f), Vector3.Back);
        b.Thruster("small", new Vector3(-1.65f, 0.55f, 0.6f), Vector3.Right);
        b.Thruster("small", new Vector3(1.65f, 0.55f, 0.6f), Vector3.Left);
        b.Gyro("small", new Vector3(0, 0.6f, 0.5f));

        b.Solid(new(0, 0.25f, 0), new(3.0f, 0.5f, 5.2f), "dark");            // hover skirt
        b.Solid(new(0, 0.85f, 0.35f), new(2.8f, 0.7f, 4.5f), "hull");        // body
        b.Prism(new(0, 0.85f, -2.35f), new(0.9f, 0.7f, 2.8f), "hull", new(0, 90, 0)); // sloped nose
        b.Glass(new(0, 1.45f, -0.6f), new(1.9f, 0.55f, 1.5f));               // bubble canopy
        b.Box(new(0, 1.3f, 0.5f), new(2.5f, 0.14f, 0.3f), "dark");           // roll bar
        b.Box(new(0, 1.35f, 1.6f), new(2.0f, 0.3f, 1.4f), "teal");           // cargo rack
        foreach (float x in new[] { -1.42f, 1.42f })
        {
            b.Box(new(x, 0.85f, 0.35f), new(0.04f, 0.32f, 4.5f), "orange");  // side band
            b.Box(new(x, 1.08f, 0.35f), new(0.04f, 0.07f, 4.5f), "yellow");  // pinstripe
        }
        b.Box(new(0.95f, 1.95f, 1.9f), new(0.06f, 1.2f, 0.06f), "dark");     // whip antenna
        b.Glow(new(0.95f, 2.57f, 1.9f), new(0.14f, 0.14f, 0.14f), "red");
        b.Checker(new(0, 0.75f, 2.62f), 6, 2, 0.22f, "dark", facingZ: true); // rear flash
        return v;
    }

    /// <summary>
    /// A small-vessel fighter, and the first ship a player flies: strong main engines and side
    /// jets so it can jink out of a capital gun's line, gear on three legs. Origin mid-fuselage.
    /// </summary>
    public static VesselLoadout Fighter(Tunables t)
    {
        var v = new VesselLoadout
        {
            Name = "Kestrel", MainEngine = "large", HullMass = 14f, Cockpit = new Vector3(0, 1.3f, -2.6f),
        };
        var b = new Builder(v, t);

        b.Thruster("large", new Vector3(-1.6f, 0.2f, 6.1f), Vector3.Forward);  // main engines
        b.Thruster("large", new Vector3(1.6f, 0.2f, 6.1f), Vector3.Forward);
        b.Thruster("large", new Vector3(-0.75f, -0.45f, -5.9f), Vector3.Back); // two retros in the chin: a
        b.Thruster("large", new Vector3(0.75f, -0.45f, -5.9f), Vector3.Back);  // first ship stops quickly
        b.Thruster("large", new Vector3(-1.6f, -0.75f, 3.4f), Vector3.Up);     // belly lift jets
        b.Thruster("large", new Vector3(1.6f, -0.75f, 3.4f), Vector3.Up);
        b.Thruster("small", new Vector3(-0.9f, 1.05f, 2.0f), Vector3.Down);
        b.Thruster("small", new Vector3(0.9f, 1.05f, 2.0f), Vector3.Down);
        b.Thruster("large", new Vector3(-5.6f, -0.3f, 1.5f), Vector3.Right);   // wingtip jink jets
        b.Thruster("large", new Vector3(5.6f, -0.3f, 1.5f), Vector3.Left);
        for (int i = 0; i < 3; i++) b.Gyro("small", new Vector3(0, 0, 0.5f + i));
        b.Gear("small", new Vector3(0, -1f, -3.4f));
        b.Gear("small", new Vector3(-2.1f, -1f, 2.6f));
        b.Gear("small", new Vector3(2.1f, -1f, 2.6f));

        b.Solid(new(0, 0, 0.5f), new(2.4f, 2f, 10f), "hull");                  // fuselage
        b.Prism(new(0, 0, -5.6f), new(2.2f, 2f, 2.4f), "hull", new(0, 90, 0)); // wedge nose
        b.Solid(new(0, -0.35f, 1.6f), new(10.8f, 0.4f, 4f), "orange");         // wings, one slab
        foreach (float x in new[] { -1f, 1f })
        {
            b.Solid(new(x * 1.6f, 0.2f, 3.6f), new(1.4f, 1.4f, 5f), "dark");    // engine nacelles
            b.Box(new(x * 1.6f, 0.2f, 1.05f), new(1.5f, 1.5f, 0.2f), "orange"); // intake rings
            b.Box(new(x * 5.45f, -0.35f, 1.6f), new(0.5f, 0.7f, 4.6f), "dark"); // wingtip pods
            b.Glow(new(x * 5.45f, -0.35f, -0.75f), new(0.4f, 0.4f, 0.1f), x < 0 ? "red" : "teal"); // nav lights
            b.Box(new(x * 1.22f, -0.2f, 0.5f), new(0.04f, 0.45f, 10f), "red");  // fuselage band
            b.Box(new(x * 1.22f, 0.15f, 0.5f), new(0.04f, 0.1f, 10f), "yellow");
        }
        b.Glass(new(0, 1.15f, -2.4f), new(1.5f, 0.6f, 2.6f));                  // canopy
        b.Prism(new(0, 2.1f, 4.4f), new(2.6f, 2.2f, 0.25f), "orange", new(0, 90, 0)); // swept tail fin
        b.Checker(new(0.14f, 1.75f, 4.6f), 4, 3, 0.3f, "dark", facingZ: false);
        b.Checker(new(-0.14f, 1.75f, 4.6f), 4, 3, 0.3f, "dark", facingZ: false);
        return v;
    }

    /// <summary>
    /// A capital ship: 970 t, so its two big gyros alone would turn it at 10°/s and the floor
    /// takes over. Origin at the middle of the main hull.
    /// </summary>
    public static VesselLoadout Capital(Tunables t)
    {
        var v = new VesselLoadout
        {
            Name = "Atlas", MainEngine = "capital", HullMass = 800f, Cockpit = new Vector3(0, 11.2f, 11.4f),
        };
        var b = new Builder(v, t);

        foreach (float x in new[] { -6f, 6f })
            foreach (float y in new[] { -3.5f, 3.5f })
                b.Thruster("capital", new Vector3(x, y, 43.5f), Vector3.Forward); // main cluster
        foreach (var p in new Vector3[] { new(-7, -7.2f, -20), new(7, -7.2f, -20), new(0, -7.2f, 0), new(-7, -7.2f, 20), new(7, -7.2f, 20) })
            b.Thruster("capital", p, Vector3.Up);                                // belly lifters
        b.Thruster("capital", new Vector3(-7f, -4f, -36f), Vector3.Back);       // retros in the prow
        b.Thruster("capital", new Vector3(7f, -4f, -36f), Vector3.Back);
        b.Thruster("large", new Vector3(-5f, 7.2f, -2f), Vector3.Down);
        b.Thruster("large", new Vector3(5f, 7.2f, -2f), Vector3.Down);
        foreach (float z in new[] { -15f, 15f })
        {
            b.Thruster("large", new Vector3(-17.2f, -2f, z), Vector3.Right);
            b.Thruster("large", new Vector3(17.2f, -2f, z), Vector3.Left);
        }
        b.Gyro("large", new Vector3(0, 0, 10f));
        b.Gyro("large", new Vector3(0, 0, -10f));
        foreach (float x in new[] { -8f, 8f })
            foreach (float z in new[] { -25f, 0f, 25f })
                b.Gear("large", new Vector3(x, -7f, z));

        b.Solid(new(0, 0, 0), new(22, 14, 70), "hull");                          // main hull
        b.Prism(new(0, -1, -41), new(12, 12, 22), "hull", new(0, 90, 0));        // prow
        b.Solid(new(0, 0, 39), new(26, 16, 8), "dark");                          // engine block
        b.Solid(new(0, 10, 18), new(10, 6, 12), "hull");                         // bridge tower
        b.Glass(new(0, 11.2f, 11.95f), new(9, 1.6f, 0.2f));                      // bridge windows
        b.Box(new(0, 8.5f, -6), new(6, 3, 40), "orange");                        // dorsal spine
        b.Box(new(0, 10.1f, -6), new(6.1f, 0.2f, 40), "yellow");
        foreach (float x in new[] { -1f, 1f })
        {
            b.Solid(new(x * 14f, -2f, 4f), new(6, 8, 40), "dark");               // sponsons
            b.Box(new(x * 17.02f, -0.5f, 4f), new(0.05f, 1.2f, 40), "teal");
            b.Box(new(x * 11.02f, -3.5f, -3f), new(0.05f, 2.4f, 64), "red");     // hull band
            b.Box(new(x * 11.02f, -1.8f, -3f), new(0.05f, 0.4f, 64), "yellow");
            b.Checker(new(x * 11.03f, 3.2f, 26f), 10, 3, 1.1f, "dark", facingZ: false);
            b.Box(new(x * 5.02f, 10f, 18f), new(0.05f, 1f, 12f), "orange");     // bridge band
            b.Glow(new(x * 13f, 6.9f, -33f), new(1f, 0.4f, 1f), x < 0 ? "red" : "teal"); // nav lights
        }
        for (int i = 0; i < 5; i++)                                              // prow chevrons
            b.Box(new(-6f + i * 3f, -5.2f, -35.2f), new(1.4f, 3.2f, 0.1f), i % 2 == 0 ? "yellow" : "dark");
        foreach (float x in new[] { -1f, 1f })
        {
            b.Box(new(x * 11.03f, 3.6f, -14f), new(0.05f, 4.2f, 24f), "orange");  // big livery panels
            b.Box(new(x * 11.03f, 1.2f, -14f), new(0.05f, 0.5f, 24f), "yellow");
            b.Box(new(x * 6.5f, 8.6f, 27f), new(5f, 3.2f, 9f), x < 0 ? "teal" : "yellow"); // deck cargo pods
            b.Box(new(x * 5.1f, 13.3f, 18f), new(0.4f, 0.4f, 8f), "dark");       // bridge rails
        }
        b.Box(new(0, -7.05f, -2f), new(18f, 0.1f, 62f), "dark");                 // belly plating
        b.Box(new(0, 2.6f, -46.2f), new(5f, 1.2f, 1.4f), "dark");                // prow visor
        b.Box(new(0, 14.2f, 15f), new(4f, 0.3f, 3f), "teal");                    // radar plate
        b.Box(new(0, 13.4f, 22f), new(0.4f, 3f, 0.4f), "dark");                  // mast
        b.Glow(new(0, 15f, 22f), new(0.8f, 0.8f, 0.8f), "red");
        return v;
    }

    /// <summary>Small helpers so a design reads like a parts list.</summary>
    sealed class Builder
    {
        readonly VesselLoadout _v;
        readonly Tunables.PartsData _parts;

        public Builder(VesselLoadout v, Tunables t)
        {
            _v = v;
            _parts = t.Parts;
        }

        public void Thruster(string size, Vector3 at, Vector3 facing)
        {
            var part = _parts.Thrusters[size];
            _v.ThrusterList.Add(new ThrusterSpec(size, at, facing, part.Thrust, part.Mass));
        }

        public void Gyro(string size, Vector3 at)
        {
            var part = _parts.Gyros[size];
            _v.GyroList.Add(new GyroSpec(size, at, part.Torque, part.Mass));
        }

        public void Hover(string size, Vector3 at)
        {
            var part = _parts.HoverEngines[size];
            _v.HoverList.Add(new HoverEngineSpec(size, at, part.Lift, part.RideHeight, part.Mass));
        }

        public void Gear(string size, Vector3 at)
        {
            var part = _parts.LandingGear[size];
            _v.GearList.Add(new GearSpec(size, at, part.Length, part.Mass));
        }

        public void Solid(Vector3 c, Vector3 s, string color) => _v.BlockList.Add(new HullBlock(c, s, color, true));
        public void Box(Vector3 c, Vector3 s, string color) => _v.BlockList.Add(new HullBlock(c, s, color, false));
        public void Glass(Vector3 c, Vector3 s) => _v.BlockList.Add(new HullBlock(c, s, "glass", false, BlockKind.Glass));
        public void Glow(Vector3 c, Vector3 s, string color) => _v.BlockList.Add(new HullBlock(c, s, color, false, BlockKind.Glow));

        public void Prism(Vector3 c, Vector3 s, string color, Vector3 rotationDegrees) =>
            _v.BlockList.Add(new HullBlock(c, s, color, false, BlockKind.Prism, rotationDegrees));

        /// <summary>A Foss-style checkerboard flash on a flank (facingZ false) or a rear face (true).</summary>
        public void Checker(Vector3 center, int columns, int rows, float cell, string color, bool facingZ)
        {
            for (int i = 0; i < columns; i++)
                for (int j = 0; j < rows; j++)
                {
                    if ((i + j) % 2 != 0) continue;
                    float u = (i - (columns - 1) / 2f) * cell, w = (j - (rows - 1) / 2f) * cell;
                    Vector3 c = facingZ ? center + new Vector3(u, w, 0) : center + new Vector3(0, w, u);
                    Vector3 s = facingZ ? new Vector3(cell, cell, 0.04f) : new Vector3(0.04f, cell, cell);
                    _v.BlockList.Add(new HullBlock(c, s, color, false));
                }
        }
    }
}
