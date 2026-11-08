using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GalHack.Structures;

/// <summary>Symbols a screen shows or a stencil carries: pictures, never text (design: no written words in the world).</summary>
public enum DeviceSymbol : byte { None, Gear, Bolt, Drop, Ring, Chevron, Grid, Crosshair, Gimbal, Bars, Crystal, Down, Warning, Arrows }

/// <summary>
/// The detail that makes a device read as a machine: armoured housings, glowing hearts, data
/// ports, vents, fins, gauges, valves, screens with symbols and hazard trim. None of it is drawn
/// per device by hand: each kind of device (its function) has one rule, and the rule reads the
/// type's own numbers (its mass, its parts) for how heavy and how busy it is, so every
/// constructor is built the same way and a heavier one comes out bigger and busier. The Big Core
/// and the Small Core are one type; the rule gives the large grid the heavier housing.
///
/// All of it is a few boxes, discs and bands per device, merged into the chunk mesh with the
/// rest, and looks only: never solid, never covering or sealing a face. Each piece names the face
/// it sits on, so it is hidden along with that face when a neighbour covers it.
/// Coordinates are cell units, -0.5 to 0.5; a device's front is -Z.
/// </summary>
public static class DeviceDetail
{
    // A small palette shared by every rule.
    const uint Gunmetal = 0x3b404a, Steel = 0x8d949c, HazardYellow = 0xf2b705, HazardBlack = 0x17181c,
        VentBody = 0x2a2e36, VentSlot = 0x07080a, Soot = 0x141417, ValveRed = 0xc1121f;

    static readonly Dir[] Sides = { Dir.PosX, Dir.NegX, Dir.PosZ, Dir.NegZ };

    public static List<Part> For(BlockType t)
    {
        var k = new Kit();
        switch (t.Function)
        {
            case "core": Core(k); break;
            case "constructor": Fabricator(k, t, DeviceSymbol.Gear); break;
            case "factory": Fabricator(k, t, DeviceSymbol.Grid); break;
            case "deconstructor": Fabricator(k, t, DeviceSymbol.Down); break;
            case "generator": Generator(k); break;
            case "fuel": FuelTank(k); break;
            case "container": Container(k); break;
            case "gyro": Gyro(k); break;
            case "thruster": Thruster(k); break;
            case "hover": Hover(k); break;
            case "chair": Chair(k); break;
            case "door": Door(k); break;
            case "light": Light(k); break;
            case "cpu-extender": CpuExtender(k, t); break;
            case "warp-drive":
            case "hyperdrive": Drive(k, t); break;
            case "warp-tank":
            case "fold-tank": CellTank(k); break;
            case "gear": Leg(k); break;
            case "weapon": Weapon(k, t); break;
            case "turret": Turret(k); break;
            case "ladder": break;
            default: Fallback(k); break;
        }
        return k.Parts;
    }

    /// <summary>0 for the lightest device of its kind to 1 for the heaviest: a constructor's 3 t is light, a factory's 16 t heavy.</summary>
    static float Heft(BlockType t, float light, float heavy) => Math.Clamp((t.Mass - light) / (heavy - light), 0f, 1f);

    // ---- Finishes -------------------------------------------------------------------------

    static PartFinish Armour(int metal = 2) => new() { Color = Gunmetal, Metal = metal, Texture = FaceTexture.Panel };
    static PartFinish Bare => new() { Color = Steel, Metal = 3, Texture = FaceTexture.Smooth };
    static PartFinish Hazard => new() { Color = HazardYellow, Accent = HazardBlack, Pattern = DetailPattern.FineHazard, Metal = 1 };
    static PartFinish Vent => new() { Color = VentBody, Accent = VentSlot, Pattern = DetailPattern.Grille, Metal = 1 };
    static PartFinish Screen(DeviceSymbol s) => new() { Symbol = s };
    static PartFinish Pulse(float speed) => new() { Anim = LightAnim.Pulse, Speed = speed };
    static PartFinish Blink(float speed) => new() { Anim = LightAnim.Strobe, Speed = speed };
    static PartFinish Grille => new() { Pattern = DetailPattern.Grille };
    static PartFinish Coat(uint c, int metal = 1) => new() { Color = c, Metal = metal, Texture = FaceTexture.Smooth };

    // ---- Shared pieces --------------------------------------------------------------------

    /// <summary>A screen in a dark bezel, showing a symbol.</summary>
    static void ScreenOn(Kit k, Dir d, float u0, float v0, float u1, float v1, DeviceSymbol symbol)
    {
        k.Face(d, u0, v0, u1, v1, 0.016f, PartStyle.Dark, Armour(1));
        const float m = 0.022f;
        k.Face(d, u0 + m, v0 + m, u1 - m, v1 - m, 0.006f, PartStyle.Glow, Screen(symbol), lift: 0.016f);
    }

    /// <summary>A port: a dark socket with a lit pin that blinks.</summary>
    static void Port(Kit k, Dir d, float u, float v, float speed)
    {
        k.Face(d, u - 0.05f, v - 0.034f, u + 0.05f, v + 0.034f, 0.018f, PartStyle.Dark, Armour(1));
        k.Face(d, u - 0.026f, v - 0.01f, u + 0.026f, v + 0.01f, 0.006f, PartStyle.Glow, Blink(speed), lift: 0.018f);
    }

    /// <summary>Square studs at the four corners of a face.</summary>
    static void Bolts(Kit k, Dir d, float inset, float size)
    {
        foreach (float u in new[] { -0.5f + inset, 0.5f - inset })
            foreach (float v in new[] { -0.5f + inset, 0.5f - inset })
                k.Face(d, u - size, v - size, u + size, v + size, size * 0.8f, PartStyle.Dark, Bare);
    }

    /// <summary>A hazard-striped band wrapped round the cell between two heights along an axis (sides only, ends open).</summary>
    static void Band(Kit k, int axis, float a0, float a1, float out_, PartFinish finish, PartStyle style = PartStyle.Accent)
    {
        float r = 0.5f + out_;
        Vector3 min = Geometry.Set(new Vector3(-r), axis, a0), max = Geometry.Set(new Vector3(r), axis, a1);
        k.Box(min, max, style, finish);
    }

    // ---- Rules ----------------------------------------------------------------------------

    /// <summary>
    /// A core: a heavy armoured housing with corner posts, a glowing heart on every side that beats
    /// over the status ring, data ports, and hazard trim round the foot. On the large grid (the Big
    /// Core) the posts are thicker, an armour rim runs round the top, the ports are doubled and a
    /// crown of fins stands on it; the Small Core is the same machine, compact.
    /// </summary>
    static void Core(Kit k)
    {
        foreach (GridSize g in new[] { GridSize.Large, GridSize.Small })
        {
            k.Grid = g;
            bool big = g == GridSize.Large;
            float w = big ? 0.15f : 0.1f, o = 0.016f, r = big ? 0.2f : 0.15f;
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    // Corner posts; the Big Core's stand proud of its top as capped towers.
                    float top = big ? 0.56f : 0.5f;
                    k.Box(new Vector3(sx * (0.5f + o), -0.5f, sz * (0.5f + o)), new Vector3(sx * (0.5f - w), top, sz * (0.5f - w)), PartStyle.Dark, Armour());
                    if (big)
                        k.Box(new Vector3(sx * (0.5f + o - 0.025f), top, sz * (0.5f + o - 0.025f)), new Vector3(sx * (0.5f - w + 0.025f), top + 0.025f, sz * (0.5f - w + 0.025f)), PartStyle.Accent, Hazard);
                }
            foreach (Dir d in Sides)
            {
                float u0 = -0.5f + w, u1 = 0.5f - w;
                k.Face(d, u0, -0.47f, u1, -0.37f, 0.012f, PartStyle.Accent, Hazard);
                k.Face(d, u0, 0.11f, u1, 0.145f, 0.012f, PartStyle.Dark, Armour());
                k.Face(d, u0, -0.145f, u1, -0.11f, 0.012f, PartStyle.Dark, Armour());
                k.Disc(d, 0f, 0f, r, 0.03f, PartStyle.Dark, Armour());
                k.Disc(d, 0f, 0f, r * 0.62f, 0.014f, PartStyle.Glow, Pulse(0.35f), lift: 0.03f);
                if (big)
                {
                    k.Face(d, u0, 0.34f, u1, 0.45f, 0.02f, PartStyle.Dark, Armour(3));
                    Port(k, d, -0.22f, -0.26f, 0.7f);
                    Port(k, d, 0.22f, -0.26f, 0.45f);
                }
                else Port(k, d, 0.2f, -0.26f, 0.6f);
            }
            if (big)
            {
                k.Face(Dir.PosY, -0.3f, -0.3f, 0.3f, 0.3f, 0.035f, PartStyle.Dark, Armour(3));
                for (int i = 0; i < 5; i++)
                {
                    float u = -0.24f + i * 0.12f;
                    k.Face(Dir.PosY, u - 0.022f, -0.24f, u + 0.022f, 0.24f, 0.09f, PartStyle.Dark, Armour(), lift: 0.035f);
                }
            }
            else
            {
                k.Face(Dir.PosY, -0.19f, -0.19f, 0.19f, 0.19f, 0.022f, PartStyle.Dark, Armour());
                k.Disc(Dir.PosY, 0f, 0f, 0.1f, 0.012f, PartStyle.Glow, Pulse(0.35f), lift: 0.022f);
            }
        }
        k.Grid = null;
    }

    /// <summary>
    /// A constructor, deconstructor or factory: a framed work bay on its front (the type's own dark
    /// aperture) with a gantry and nozzle across it, a status screen showing what it does, vents on
    /// its flanks, an input hopper on top and hazard trim along the foot. Heavier machines get a
    /// bigger hopper, a second nozzle, more vents and a stack.
    /// </summary>
    static void Fabricator(Kit k, BlockType t, DeviceSymbol symbol)
    {
        float heft = Heft(t, 3f, 16f);
        // The bay: the type's largest dark part flush on its front.
        Part bayPart = t.Parts.Where(p => p.Style == PartStyle.Dark && p.Min.Z <= -0.5f && p.Max.Z <= -0.49f)
            .OrderByDescending(p => (p.Max.X - p.Min.X) * (p.Max.Y - p.Min.Y)).FirstOrDefault();
        float bu0 = -0.34f, bu1 = 0.34f, bv0 = -0.12f, bv1 = 0.32f;
        if (bayPart != null)
        {
            // On the front face, u runs along -X (the viewer's right) and v along +Y.
            bu0 = -bayPart.Max.X;
            bu1 = -bayPart.Min.X;
            bv0 = bayPart.Min.Y;
            bv1 = bayPart.Max.Y;
        }
        Dir front = Dir.NegZ;
        const float bar = 0.035f;
        k.Face(front, bu0 - bar, bv1, bu1 + bar, bv1 + bar, 0.03f, PartStyle.Dark, Armour());
        k.Face(front, bu0 - bar, bv0 - bar, bu1 + bar, bv0, 0.03f, PartStyle.Dark, Armour());
        k.Face(front, bu0 - bar, bv0, bu0, bv1, 0.03f, PartStyle.Dark, Armour());
        k.Face(front, bu1, bv0, bu1 + bar, bv1, 0.03f, PartStyle.Dark, Armour());
        // The gantry rail under the bay's lintel, and a carriage with a nozzle for each.
        float rail = bv1 - 0.06f;
        k.Face(front, bu0, rail - 0.025f, bu1, rail + 0.025f, 0.045f, PartStyle.Dark, Bare);
        int nozzles = heft > 0.45f ? 2 : 1;
        for (int i = 0; i < nozzles; i++)
        {
            float u = nozzles == 1 ? (bu0 + bu1) * 0.3f : bu0 + (bu1 - bu0) * (0.28f + 0.44f * i);
            k.Face(front, u - 0.045f, rail - 0.05f, u + 0.045f, rail + 0.04f, 0.07f, PartStyle.Accent, Coat(HazardYellow));
            Vector3 tip = Kit.OnFace(front, u, rail - 0.05f, 0.5f + 0.035f);
            k.Tube(1, tip, 0.012f, 0.026f, tip.Y - 0.1f, tip.Y, PartStyle.Dark, Bare, segments: 8);
            k.Box(tip + new Vector3(-0.012f, -0.13f, -0.012f), tip + new Vector3(0.012f, -0.1f, 0.012f), PartStyle.Glow, Pulse(2.5f));
        }
        // Status screen, high on the right flank, readable from a front quarter.
        ScreenOn(k, Dir.PosX, 0.12f, 0.12f, 0.42f, 0.4f, symbol);
        // Vents: on both flanks, low down; on the heaviest across the back too.
        foreach (Dir d in new[] { Dir.PosX, Dir.NegX })
            k.Face(d, -0.4f, -0.42f, 0.4f, -0.12f + 0.08f * heft, 0.01f, PartStyle.Dark, Vent);
        if (heft > 0.6f) k.Face(Dir.PosZ, -0.36f, -0.36f, 0.36f, 0.2f, 0.01f, PartStyle.Dark, Vent);
        // The hopper on top, towards the back.
        float h = 0.17f + 0.12f * heft, wall = 0.03f, tall = 0.07f + 0.06f * heft, cv = -0.1f;
        k.Face(Dir.PosY, -h, cv - h, h, cv + h, 0.006f, PartStyle.Dark, Coat(Soot, 0));
        k.Face(Dir.PosY, -h, cv - h, h, cv - h + wall, tall, PartStyle.Body, null);
        k.Face(Dir.PosY, -h, cv + h - wall, h, cv + h, tall, PartStyle.Body, null);
        k.Face(Dir.PosY, -h, cv - h + wall, -h + wall, cv + h - wall, tall, PartStyle.Body, null);
        k.Face(Dir.PosY, h - wall, cv - h + wall, h, cv + h - wall, tall, PartStyle.Body, null);
        k.Face(Dir.PosY, -h - 0.01f, cv - h - 0.01f, h + 0.01f, cv - h + wall + 0.01f, 0.012f, PartStyle.Accent, Hazard, lift: tall);
        if (heft > 0.5f)
        {
            Vector3 stack = new(-0.36f, 0.5f, 0.32f);
            k.Tube(1, stack, 0.06f, 0.06f, 0.5f, 0.66f, PartStyle.Dark, Armour(1), segments: 10);
            k.Tube(1, stack, 0.045f, 0.045f, 0.66f, 0.665f, PartStyle.Dark, Coat(Soot, 0), segments: 10);
        }
        k.Face(front, -0.5f, -0.5f, 0.5f, -0.43f, 0.012f, PartStyle.Accent, Hazard);
        Bolts(k, front, 0.05f, 0.022f);
    }

    /// <summary>A generator: cooling fins down both flanks, a pulsing core light behind a bezel on its front, exhaust stacks on top and warning stripes.</summary>
    static void Generator(Kit k)
    {
        foreach (Dir d in new[] { Dir.PosX, Dir.NegX })
            foreach (float u in new[] { -0.3f, -0.21f, -0.12f, 0.12f, 0.21f, 0.3f })
                k.Face(d, u - 0.014f, -0.38f, u + 0.014f, 0.38f, 0.075f, PartStyle.Dark, Armour(2));
        Dir front = Dir.NegZ;
        k.Disc(front, 0f, 0.06f, 0.2f, 0.03f, PartStyle.Dark, Armour());
        k.Disc(front, 0f, 0.06f, 0.13f, 0.012f, PartStyle.Glow, Pulse(0.6f), lift: 0.03f);
        k.Face(front, -0.24f, -0.3f, 0.24f, -0.18f, 0.01f, PartStyle.Dark, Vent);
        k.Face(front, -0.5f, 0.38f, 0.5f, 0.46f, 0.012f, PartStyle.Accent, Hazard);
        k.Face(front, -0.5f, -0.46f, 0.5f, -0.38f, 0.012f, PartStyle.Accent, Hazard);
        k.Face(Dir.PosZ, -0.36f, -0.36f, 0.36f, 0.3f, 0.01f, PartStyle.Dark, Vent);
        foreach (float x in new[] { -0.24f, 0.24f })
        {
            var at = new Vector3(x, 0.5f, 0.26f);
            k.Tube(1, at, 0.075f, 0.075f, 0.5f, 0.64f, PartStyle.Dark, Armour(1), segments: 10);
            k.Tube(1, at, 0.055f, 0.055f, 0.64f, 0.645f, PartStyle.Dark, Coat(Soot, 0), segments: 10);
        }
    }

    /// <summary>A fuel tank: bands round the drum, a gauge on its front, a filler cap and a valve with its wheel on top.</summary>
    static void FuelTank(Kit k)
    {
        foreach (float y in new[] { -0.28f, 0.28f })
            k.Tube(1, Vector3.Zero, 0.472f, 0.472f, y - 0.025f, y + 0.025f, PartStyle.Dark, Armour(1), segments: 16);
        var gauge = new Vector3(0f, 0.15f, -0.455f);
        k.Tube(2, gauge, 0.09f, 0.09f, -0.49f, -0.45f, PartStyle.Dark, Armour(1), segments: 12);
        k.Tube(2, gauge, 0.066f, 0.066f, -0.497f, -0.49f, PartStyle.Glow, Screen(DeviceSymbol.Drop), segments: 12);
        var filler = new Vector3(0.2f, 0.5f, -0.14f);
        k.Tube(1, filler, 0.085f, 0.085f, 0.5f, 0.55f, PartStyle.Dark, Bare, segments: 12);
        k.Tube(1, filler, 0.06f, 0.06f, 0.55f, 0.575f, PartStyle.Accent, Coat(HazardYellow), segments: 12);
        var valve = new Vector3(-0.2f, 0.5f, 0.12f);
        k.Tube(1, valve, 0.028f, 0.028f, 0.5f, 0.62f, PartStyle.Dark, Bare, segments: 8);
        k.Tube(1, valve, 0.095f, 0.095f, 0.6f, 0.62f, PartStyle.Accent, Coat(ValveRed), segments: 12);
        k.Face(Dir.NegZ, -0.5f, -0.5f, 0.5f, -0.42f, 0.012f, PartStyle.Accent, Hazard);
    }

    /// <summary>A container: rails round its top and foot, two latches, a handle, and a stencilled grid.</summary>
    static void Container(Kit k)
    {
        foreach (Dir d in Sides)
        {
            k.Face(d, -0.4f, 0.37f, 0.4f, 0.44f, 0.022f, PartStyle.Body, null);
            k.Face(d, -0.4f, -0.44f, 0.4f, -0.37f, 0.022f, PartStyle.Body, null);
        }
        Dir front = Dir.NegZ;
        foreach (float u in new[] { -0.29f, 0.29f })
        {
            k.Face(front, u - 0.045f, -0.06f, u + 0.045f, 0.07f, 0.03f, PartStyle.Dark, Armour(2));
            k.Face(front, u - 0.025f, 0.07f, u + 0.025f, 0.12f, 0.018f, PartStyle.Dark, Bare);
        }
        k.Face(front, -0.15f, -0.27f, -0.11f, -0.2f, 0.055f, PartStyle.Dark, Bare);
        k.Face(front, 0.11f, -0.27f, 0.15f, -0.2f, 0.055f, PartStyle.Dark, Bare);
        k.Face(front, -0.15f, -0.27f, 0.15f, -0.235f, 0.02f, PartStyle.Dark, Bare, lift: 0.055f);
        k.Face(front, -0.13f, 0.1f, 0.13f, 0.33f, 0.003f, PartStyle.Body, new PartFinish { Symbol = DeviceSymbol.Grid, Texture = FaceTexture.Smooth });
    }

    /// <summary>A gyro: its ring housing shows, a band each side of the glowing rotor and a gimbal ring through it the other way, with a status screen.</summary>
    static void Gyro(Kit k)
    {
        // On each flank the ring housing shows through a round port: a bright outer race, a dark gap,
        // the glowing rotor ring turning inside, and the hub.
        foreach (Dir d in Sides)
        {
            k.Disc(d, 0f, 0f, 0.42f, 0.025f, PartStyle.Dark, Bare, segments: 14);
            k.Disc(d, 0f, 0f, 0.35f, 0.012f, PartStyle.Dark, Coat(Soot, 0), lift: 0.025f, segments: 14);
            k.Disc(d, 0f, 0f, 0.29f, 0.004f, PartStyle.Glow, Pulse(1.2f), lift: 0.037f, segments: 14);
            k.Disc(d, 0f, 0f, 0.21f, 0.006f, PartStyle.Dark, Coat(Soot, 0), lift: 0.037f, segments: 12);
            k.Disc(d, 0f, 0f, 0.08f, 0.03f, PartStyle.Dark, Bare, lift: 0.037f, segments: 8);
        }
        ScreenOn(k, Dir.PosY, -0.16f, -0.16f, 0.16f, 0.16f, DeviceSymbol.Gimbal);
    }

    /// <summary>A thruster: a mounting flange, a nozzle bell in three heat-tinted bands, its sooty mouth with the glow inside, vents and a hazard band on the body.</summary>
    static void Thruster(Kit k)
    {
        // Straw, violet, blue: metal that has been hot.
        (float r0, float r1, float z0, float z1, uint c)[] bands =
        {
            (0.3f, 0.35f, 0.26f, 0.34f, 0xd9b25a), (0.35f, 0.405f, 0.34f, 0.42f, 0x8a64b8), (0.405f, 0.46f, 0.42f, 0.5f, 0x5577aa),
        };
        // The bell and its mouth open on the nozzle's face (+Z): with that face against a block they are hidden with it.
        foreach (var b in bands)
            k.Tube(2, Vector3.Zero, b.r0, b.r1, b.z0, b.z1, PartStyle.Dark, new PartFinish { Color = b.c, Metal = 1, Texture = FaceTexture.Smooth }, segments: 16, caps: false).Mount = Dir.PosZ;
        k.Tube(2, Vector3.Zero, 0.455f, 0.455f, 0.48f, 0.49f, PartStyle.Dark, Coat(Soot, 0), segments: 16).Mount = Dir.PosZ;
        Band(k, 2, 0.05f, 0.16f, 0.014f, Hazard);
        // Vents along the body from its nose to the band (on the flanks u runs along Z, on top and bottom v does).
        k.Face(Dir.PosX, 0.0f, -0.34f, 0.4f, 0.34f, 0.01f, PartStyle.Dark, Vent);
        k.Face(Dir.NegX, -0.4f, -0.34f, 0.0f, 0.34f, 0.01f, PartStyle.Dark, Vent);
        k.Face(Dir.PosY, -0.34f, 0.0f, 0.34f, 0.4f, 0.01f, PartStyle.Dark, Vent);
        k.Face(Dir.NegY, -0.34f, 0.0f, 0.34f, 0.4f, 0.01f, PartStyle.Dark, Vent);
    }

    /// <summary>A hover plate: its emitter is a slotted grille with a faint glow, four small emitter pods at the corners, and a hazard band.</summary>
    static void Hover(Kit k)
    {
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                var at = new Vector3(sx * 0.39f, 0f, sz * 0.39f);
                k.Tube(1, at, 0.075f, 0.075f, 0.02f, 0.1f, PartStyle.Dark, Armour(1), segments: 10);
                k.Tube(1, at, 0.05f, 0.05f, 0.005f, 0.02f, PartStyle.Glow, Grille, segments: 10);
            }
        k.Box(new Vector3(-0.514f, 0.1f, -0.514f), new Vector3(0.514f, 0.19f, 0.514f), PartStyle.Accent, Hazard);
        k.Tube(1, Vector3.Zero, 0.4f, 0.4f, -0.415f, -0.4f, PartStyle.Dark, Bare, segments: 16);
    }

    /// <summary>The command chair: a headrest, armrests with sticks, buttons on the console and a hazard ring at its foot.</summary>
    static void Chair(Kit k)
    {
        k.Box(new Vector3(-0.17f, 0.3f, 0.15f), new Vector3(0.17f, 0.48f, 0.2f), PartStyle.Dark, Armour(1));
        foreach (int s in new[] { -1, 1 })
        {
            k.Box(new Vector3(s * 0.3f, -0.08f, -0.12f), new Vector3(s * 0.37f, -0.02f, 0.26f), PartStyle.Dark, Armour(1));
            var stick = new Vector3(s * 0.335f, 0f, -0.06f);
            k.Tube(1, stick, 0.018f, 0.022f, -0.02f, 0.09f, PartStyle.Dark, Bare, segments: 8);
            k.Box(stick + new Vector3(-0.022f, 0.09f, -0.022f), stick + new Vector3(0.022f, 0.115f, 0.022f), PartStyle.Accent, Coat(ValveRed));
            k.Box(new Vector3(s * 0.26f - 0.03f, -0.08f, -0.38f), new Vector3(s * 0.26f + 0.03f, -0.065f, -0.34f), PartStyle.Glow, Blink(s > 0 ? 0.6f : 0.4f));
        }
        k.Box(new Vector3(-0.19f, -0.5f, -0.08f), new Vector3(0.19f, -0.45f, 0.25f), PartStyle.Accent, Hazard);
    }

    /// <summary>A door: chunky plain jambs in the frame (hazard stripes belong on a dangerous opening, never round a person's door), a status lamp over it each side, and seams across the leaf.</summary>
    static void Door(Kit k)
    {
        foreach (int s in new[] { -1, 1 })
            k.Box(new Vector3(s * 0.44f, -0.5f, -0.15f), new Vector3(s * 0.39f, 0.46f, 0.15f), PartStyle.Accent, Armour(1));
        foreach (int s in new[] { -1, 1 })
            k.Box(new Vector3(-0.07f, 0.4f, s * 0.5f), new Vector3(0.07f, 0.455f, s * 0.515f), PartStyle.Glow, Pulse(0.25f));
        k.ClosedOnly = true;
        k.Box(new Vector3(-0.007f, -0.5f, -0.104f), new Vector3(0.007f, 0.46f, 0.104f), PartStyle.Dark, Armour(1));
        foreach (float y in new[] { -0.32f, 0.24f })
            k.Box(new Vector3(-0.39f, y - 0.01f, -0.104f), new Vector3(0.39f, y + 0.01f, 0.104f), PartStyle.Dark, Armour(1));
        k.ClosedOnly = false;
    }

    /// <summary>A light: a bezel round the lamp, a guard of two bars across it, and screws in its plate.</summary>
    static void Light(Kit k)
    {
        const float z0 = 0.38f, z1 = 0.44f, e = 0.26f, b = 0.04f;
        k.Box(new Vector3(-e, e - b, z0), new Vector3(e, e, z1), PartStyle.Dark, Armour(1));
        k.Box(new Vector3(-e, -e, z0), new Vector3(e, -e + b, z1), PartStyle.Dark, Armour(1));
        k.Box(new Vector3(-e, -e + b, z0), new Vector3(-e + b, e - b, z1), PartStyle.Dark, Armour(1));
        k.Box(new Vector3(e - b, -e + b, z0), new Vector3(e, e - b, z1), PartStyle.Dark, Armour(1));
        foreach (float x in new[] { -0.075f, 0.075f })
            k.Box(new Vector3(x - 0.012f, -e + b, z0 + 0.004f), new Vector3(x + 0.012f, e - b, z0 + 0.02f), PartStyle.Dark, Bare);
    }

    /// <summary>A CPU extender: rack slots between its bands and a row of blinking lamps.</summary>
    static void CpuExtender(Kit k, BlockType t)
    {
        foreach (Dir d in Sides)
            k.Face(d, -0.38f, -0.17f, 0.38f, 0.17f, 0.008f, PartStyle.Dark, Vent);
        for (int i = 0; i < 2 + t.ExtenderTier; i++)
        {
            float u = -0.3f + i * 0.6f / (1 + t.ExtenderTier);
            k.Face(Dir.NegZ, u - 0.025f, 0.34f, u + 0.025f, 0.39f, 0.012f, PartStyle.Glow, Blink(0.3f + 0.17f * i));
        }
        Bolts(k, Dir.NegZ, 0.05f, 0.02f);
    }

    /// <summary>A warp drive or hyperdrive: field coils round it, a framed emitter on its front, a status screen; heavier drives carry more coils, lit between.</summary>
    static void Drive(Kit k, BlockType t)
    {
        float heft = Heft(t, 8f, 20f);
        int coils = 2 + (int)MathF.Round(heft * 2f);
        for (int i = 0; i < coils; i++)
        {
            float z = -0.3f + 0.6f * i / (coils - 1);
            Band(k, 2, z - 0.045f, z + 0.045f, 0.02f, Armour(3), PartStyle.Dark);
            if (heft > 0.4f && i + 1 < coils) Band(k, 2, z + 0.12f, z + 0.14f, 0.01f, Pulse(0.8f), PartStyle.Glow);
        }
        Dir front = Dir.NegZ;
        k.Face(front, -0.34f, 0.3f, 0.34f, 0.34f, 0.03f, PartStyle.Dark, Bare);
        k.Face(front, -0.34f, -0.34f, 0.34f, -0.3f, 0.03f, PartStyle.Dark, Bare);
        k.Face(front, -0.34f, -0.3f, -0.3f, 0.3f, 0.03f, PartStyle.Dark, Bare);
        k.Face(front, 0.3f, -0.3f, 0.34f, 0.3f, 0.03f, PartStyle.Dark, Bare);
        ScreenOn(k, Dir.PosX, 0.14f, 0.14f, 0.4f, 0.4f, DeviceSymbol.Crystal);
        k.Face(front, -0.5f, -0.5f, 0.5f, -0.42f, 0.012f, PartStyle.Accent, Hazard);
    }

    /// <summary>A warp cell or fold crystal tank: posts at its corners, a band round it, a gauge screen and a valve on top.</summary>
    static void CellTank(Kit k)
    {
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                k.Box(new Vector3(sx * 0.508f, -0.5f, sz * 0.508f), new Vector3(sx * 0.41f, 0.5f, sz * 0.41f), PartStyle.Dark, Armour(2));
        Band(k, 1, 0.26f, 0.32f, 0.014f, Armour(1), PartStyle.Dark);
        Band(k, 1, -0.32f, -0.26f, 0.014f, Armour(1), PartStyle.Dark);
        ScreenOn(k, Dir.PosX, -0.14f, -0.12f, 0.14f, 0.16f, DeviceSymbol.Crystal);
        var valve = new Vector3(0.2f, 0.5f, 0.2f);
        k.Tube(1, valve, 0.028f, 0.028f, 0.5f, 0.6f, PartStyle.Dark, Bare, segments: 8);
        k.Tube(1, valve, 0.085f, 0.085f, 0.58f, 0.6f, PartStyle.Accent, Coat(ValveRed), segments: 12);
        k.Face(Dir.PosY, -0.32f, -0.32f, -0.12f, -0.12f, 0.02f, PartStyle.Accent, Hazard);
    }

    /// <summary>A landing leg: a broad foot pad and a hazard band under its housing.</summary>
    static void Leg(Kit k)
    {
        k.Tube(1, Vector3.Zero, 0.47f, 0.47f, -0.5f, -0.44f, PartStyle.Dark, Bare, segments: 16);
        k.Box(new Vector3(-0.414f, 0.3f, -0.414f), new Vector3(0.414f, 0.38f, 0.414f), PartStyle.Accent, Hazard);
    }

    /// <summary>A gun: a gatling gets its barrel cluster and an ammo box, a rocket pod a hazard band and a vent.</summary>
    static void Weapon(Kit k, BlockType t)
    {
        Part barrel = t.Parts.FirstOrDefault(p => p.Style == PartStyle.Dark && p.Min.Z <= -0.45f);
        if (barrel != null && barrel.Max.X - barrel.Min.X < 0.5f)
        {
            for (int i = 0; i < 3; i++)
            {
                float a = MathF.Tau * i / 3f + 0.5f;
                var at = new Vector3(MathF.Cos(a) * 0.1f, MathF.Sin(a) * 0.1f, 0f);
                k.Tube(2, at, 0.032f, 0.032f, -0.56f, -0.1f, PartStyle.Dark, Bare, segments: 8);
            }
            k.Box(new Vector3(0.3f, -0.26f, 0.0f), new Vector3(0.44f, 0.04f, 0.36f), PartStyle.Dark, Armour(1));
            k.Box(new Vector3(0.3f, 0.04f, 0.0f), new Vector3(0.44f, 0.08f, 0.36f), PartStyle.Accent, Hazard);
        }
        else
        {
            k.Box(new Vector3(-0.434f, -0.374f, 0.1f), new Vector3(0.434f, 0.374f, 0.2f), PartStyle.Accent, Hazard);
            k.Box(new Vector3(-0.3f, 0.36f, 0.24f), new Vector3(0.3f, 0.372f, 0.46f), PartStyle.Dark, Vent);
        }
    }

    /// <summary>The placeholder turret: a hazard ring round its turntable.</summary>
    static void Turret(Kit k) =>
        k.Tube(1, Vector3.Zero, 0.435f, 0.435f, -0.3f, -0.26f, PartStyle.Accent, Hazard, segments: 16);

    /// <summary>Any other device: hazard trim along its front foot and a status lamp.</summary>
    static void Fallback(Kit k)
    {
        k.Face(Dir.NegZ, -0.5f, -0.5f, 0.5f, -0.42f, 0.012f, PartStyle.Accent, Hazard);
        k.Face(Dir.NegZ, 0.36f, 0.38f, 0.44f, 0.44f, 0.012f, PartStyle.Glow, Pulse(0.4f));
    }

    // ---- Building -------------------------------------------------------------------------

    /// <summary>Builds detail parts in cell units.</summary>
    sealed class Kit
    {
        public readonly List<Part> Parts = new();
        /// <summary>Parts added while set are drawn on this grid only.</summary>
        public GridSize? Grid;
        /// <summary>Parts added while set belong to a door's leaf.</summary>
        public bool ClosedOnly;

        /// <summary>
        /// A point on a face: across it (u to the viewer's right, v up the face; on a top or bottom
        /// face up is towards -Z) and <paramref name="n"/> out from the cell's centre along its normal.
        /// The same frame as the mesher's face UVs.
        /// </summary>
        public static Vector3 OnFace(Dir d, float u, float v, float n)
        {
            Vector3 normal = d.Normal();
            Vector3 up = d.Axis() == 1 ? -Vector3.UnitZ : Vector3.UnitY;
            Vector3 right = Vector3.Cross(up, normal);
            return normal * n + right * u + up * v;
        }

        Part Add(PartStyle style, PartFinish finish, List<Polygon> polygons, Vector3 min, Vector3 max, Dir? mount)
        {
            var p = new Part { Style = style, Finish = finish, Polygons = polygons, Min = min, Max = max, Grid = Grid, Mount = mount, ClosedOnly = ClosedOnly };
            Parts.Add(p);
            return p;
        }

        public Part Box(Vector3 a, Vector3 b, PartStyle style, PartFinish finish, Dir? mount = null)
        {
            Vector3 min = Vector3.Min(a, b), max = Vector3.Max(a, b);
            return Add(style, finish, Geometry.ConvexHull(Geometry.BoxPoints(min, max).ToList()), min, max, mount);
        }

        /// <summary>A plate on a face, across u0..u1 and v0..v1, standing <paramref name="height"/> proud of the face (or of <paramref name="lift"/> above it). Its back, against the face, is left out.</summary>
        public Part Face(Dir d, float u0, float v0, float u1, float v1, float height, PartStyle style, PartFinish finish, float lift = 0f)
        {
            Vector3 a = OnFace(d, u0, v0, 0.5f + lift), b = OnFace(d, u1, v1, 0.5f + lift + height);
            Vector3 min = Vector3.Min(a, b), max = Vector3.Max(a, b);
            List<Polygon> polygons = Geometry.ConvexHull(Geometry.BoxPoints(min, max).ToList());
            polygons.RemoveAll(p => Vector3.Dot(p.Normal, d.Normal()) < -0.99f);
            return Add(style, finish, polygons, min, max, d);
        }

        /// <summary>A disc on a face, centred at (u, v), standing proud of it.</summary>
        public Part Disc(Dir d, float u, float v, float r, float height, PartStyle style, PartFinish finish, float lift = 0f, int segments = 12)
        {
            Vector3 c = OnFace(d, u, v, 0f);
            int axis = d.Axis();
            float n0 = d.Sign() * (0.5f + lift), n1 = d.Sign() * (0.5f + lift + height);
            Part p = Tube(axis, c, r, r, MathF.Min(n0, n1), MathF.Max(n0, n1), style, finish, segments);
            p.Polygons.RemoveAll(q => Vector3.Dot(q.Normal, d.Normal()) < -0.99f);
            p.Mount = d;
            return p;
        }

        /// <summary>
        /// A cylinder or cone along an axis through <paramref name="centre"/>, from <paramref name="a0"/>
        /// (radius r0) to <paramref name="a1"/> (radius r1) along that axis; its sides are smooth-shaded.
        /// Without caps it is an open band (a nozzle bell).
        /// </summary>
        public Part Tube(int axis, Vector3 centre, float r0, float r1, float a0, float a1, PartStyle style, PartFinish finish, int segments = 12, bool caps = true)
        {
            int ua = (axis + 1) % 3, va = (axis + 2) % 3;
            var points = new List<Vector3>();
            for (int i = 0; i < segments; i++)
            {
                float t = MathF.Tau * i / segments;
                foreach (var (end, r) in new[] { (a0, r0), (a1, r1) })
                {
                    Vector3 p = Geometry.Set(centre, axis, end);
                    p = Geometry.Set(p, ua, Geometry.Get(centre, ua) + MathF.Cos(t) * r);
                    p = Geometry.Set(p, va, Geometry.Get(centre, va) + MathF.Sin(t) * r);
                    points.Add(p);
                }
            }
            List<Polygon> polygons = Geometry.ConvexHull(points);
            foreach (Polygon p in polygons)
                if (MathF.Abs(Geometry.Get(p.Normal, axis)) < 0.9f) p.SmoothAxis = axis;
            if (!caps) polygons.RemoveAll(p => p.SmoothAxis < 0);
            float rMax = MathF.Max(r0, r1);
            Vector3 min = Geometry.Set(centre - new Vector3(rMax), axis, a0), max = Geometry.Set(centre + new Vector3(rMax), axis, a1);
            return Add(style, finish, polygons, min, max, null);
        }
    }
}
