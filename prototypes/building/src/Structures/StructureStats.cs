using System;

namespace GalHack.Structures;

/// <summary>
/// Everything the build HUD shows, summed over a structure's blocks: mass, power, thrust and the
/// one shared storage. There is no CPU: what a structure can run is limited by its power alone.
/// </summary>
public sealed class StructureStats
{
    public const double G = 9.81;

    public int Blocks, Components;
    public double BlockMass, CargoMass, Mass; // tonnes

    public double PowerOut, PowerUse; // MW, generation and full-load use
    public bool Powered => PowerOut > 0;

    /// <summary>kN of thrust pushing the structure towards each direction.</summary>
    public readonly double[] Thrust = new double[6];
    public double ThrustToWeightForward, ThrustToWeightMax; // in g
    public bool Flies;

    public double StorageRaw, StorageCapacity, CargoSu; // storage units
    public int Containers, Thrusters, Generators, Lights;
    public double Fuel;

    public bool HasCore, GravityActive, LifeSupport;

    public static StructureStats Compute(Structure s)
    {
        var st = new StructureStats { HasCore = s.Core != null, CargoSu = s.CargoSu };
        var thrust = new double[6];

        foreach (var (_, b) in s.Blocks())
        {
            BlockType t = s.TypeOf(b);
            st.Blocks++;
            st.BlockMass += s.StatsOf(b).Mass;
            if (t.IsArmor) continue;

            st.Components++;
            if (t.IsContainer)
            {
                st.Containers++;
                st.StorageRaw += t.Storage;
            }
            st.Fuel += t.Fuel;
            if (t.Power > 0) { st.PowerOut += t.Power; st.Generators++; }
            else if (!(t.IsLight && b.Off)) st.PowerUse += -t.Power;
            if (t.IsLight) st.Lights++;
            if (t.IsThruster)
            {
                st.Thrusters++;
                // A thruster's exhaust leaves through its back (+Z), pushing the structure forward (-Z).
                thrust[(int)Orientations.ToWorld(b.Rot, Dir.NegZ)] += t.Thrust;
            }
        }

        st.CargoMass = s.CargoSu * s.Catalogue.CargoMassPerSu;
        st.Mass = st.BlockMass + st.CargoMass;
        st.StorageCapacity = Math.Min(st.StorageRaw, s.Grid.StorageCap);

        // Only Base Mode doesn't fly: its thrusters sit idle until the core is switched.
        st.Flies = st.HasCore && s.Mode != CoreMode.Base && st.Powered;
        for (int i = 0; i < 6; i++) st.Thrust[i] = st.Flies ? thrust[i] : 0;
        double weight = st.Mass * G; // kN
        if (weight > 0)
        {
            st.ThrustToWeightForward = st.Thrust[(int)Dir.NegZ] / weight;
            foreach (double t in st.Thrust) st.ThrustToWeightMax = Math.Max(st.ThrustToWeightMax, t / weight);
        }

        // Core gravity needs power but draws none; life support comes with the core.
        st.GravityActive = st.HasCore && s.GravityOn && st.Powered;
        st.LifeSupport = st.HasCore;
        return st;
    }
}
