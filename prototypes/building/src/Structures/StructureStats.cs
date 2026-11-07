using System;
using System.Linq;

namespace GalHack.Structures;

/// <summary>
/// Everything the build HUD shows, summed over a structure's blocks: mass, CPU and its tier,
/// power, thrust and the one shared storage.
///
/// CPU is a soft limit. Over the limit, every component runs at limit ÷ used efficiency, so
/// 100% over gives 50%: thrusters push and generators produce that much less. Nothing switches off.
/// </summary>
public sealed class StructureStats
{
    public const double G = 9.81;

    public int Blocks, Components;
    public double BlockMass, CargoMass, Mass; // tonnes

    public double CpuUsed;
    public long CpuLimit;
    public int Tier, Extenders;
    /// <summary>Extenders aboard by their own tier (index 2 to 4).</summary>
    public readonly int[] ExtendersOfTier = new int[5];
    public double Efficiency;

    public double PowerOut, PowerUse; // MW, generation after efficiency and full-load use
    public bool Powered => PowerOut > 0;

    /// <summary>kN of thrust pushing the structure towards each direction, after efficiency.</summary>
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
        GridDef grid = s.Grid;
        double powerOut = 0;
        var thrust = new double[6];

        foreach (var (_, b) in s.Blocks())
        {
            BlockType t = s.TypeOf(b);
            BlockStats bs = s.StatsOf(b);
            st.Blocks++;
            st.BlockMass += bs.Mass;
            st.CpuUsed += bs.Cpu;
            if (t.IsArmor) continue;

            st.Components++;
            if (t.IsCpuExtender)
            {
                st.Extenders++;
                st.ExtendersOfTier[t.ExtenderTier]++;
            }
            if (t.IsContainer)
            {
                st.Containers++;
                st.StorageRaw += t.Storage * grid.StorageScale;
            }
            st.Fuel += t.Fuel * grid.FunctionScale;
            if (t.Power > 0) { powerOut += t.Power * grid.FunctionScale; st.Generators++; }
            else if (!(t.IsLight && b.Off)) st.PowerUse += -t.Power * grid.FunctionScale;
            if (t.IsLight) st.Lights++;
            if (t.IsThruster)
            {
                st.Thrusters++;
                // A thruster's exhaust leaves through its back (+Z), pushing the structure forward (-Z).
                thrust[(int)Orientations.ToWorld(b.Rot, Dir.NegZ)] += t.Thrust * grid.FunctionScale;
            }
        }

        (st.Tier, st.CpuLimit) = TierFor(s.Catalogue, grid, st.ExtendersOfTier, st.HasCore);
        st.Efficiency = EfficiencyFor(st.CpuUsed, st.CpuLimit);
        st.PowerOut = powerOut * st.Efficiency;

        st.CargoMass = s.CargoSu * s.Catalogue.CargoMassPerSu;
        st.Mass = st.BlockMass + st.CargoMass;
        st.StorageCapacity = Math.Min(st.StorageRaw, grid.StorageCap);

        // Only Base Mode doesn't fly: its thrusters sit idle until the core is switched.
        st.Flies = st.HasCore && s.Mode != CoreMode.Base && st.Powered;
        for (int i = 0; i < 6; i++) st.Thrust[i] = st.Flies ? thrust[i] * st.Efficiency : 0;
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

    /// <summary>
    /// The core gives tier 1; CPU extenders unlock the rest (data: extendersPerTier, e.g. 0/1/2/4).
    /// A tier counts only extenders of that tier or better, so tiers 3 and 4 need the lattice
    /// extenders (design 883). A wreck has no core, so no CPU at all.
    /// </summary>
    public static (int tier, long limit) TierFor(Catalogue c, GridDef grid, int[] extendersOfTier, bool hasCore)
    {
        if (!hasCore) return (0, 0);
        int tier = 1;
        for (int i = 0; i < c.ExtendersPerTier.Length && i < grid.CpuTiers.Length; i++)
            if (extendersOfTier.Skip(i + 1).Sum() >= c.ExtendersPerTier[i]) tier = i + 1;
        return (tier, grid.CpuTiers[tier - 1]);
    }

    public static double EfficiencyFor(double used, long limit) =>
        limit <= 0 ? 0 : used <= limit ? 1 : limit / used;
}
