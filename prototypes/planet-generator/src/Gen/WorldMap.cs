using System;
using System.Collections.Generic;

namespace Planets.Gen;

/// <summary>
/// One river as a smoothed polyline from source to mouth, in metres. X and Y are unwrapped (they
/// can run past a map edge) so the line stays continuous across both seams.
/// </summary>
public sealed class River
{
    public readonly List<float> X = new(), Y = new(), Level = new(), Width = new();
    public int Count => X.Count;
}

/// <summary>
/// The coarse world map: one sample per cell (32 m by default) of everything the generator knows.
/// Row 0 is the north edge. The map is a torus: x wraps east-west and y wraps north-south, so
/// walking off the north edge brings you in at the south edge at the same x.
/// </summary>
public sealed class WorldMap
{
    public readonly PlanetSpec Spec;
    public readonly ArchetypeDef Def;
    public readonly TerrainModel Model;
    public readonly int Width, Height;
    public readonly float CellSize;

    public readonly float[] Elevation;    // metres above sea level, river channels carved in
    public readonly float[] Temperature;  // 0 frozen .. 1 scorching
    public readonly float[] Humidity;     // 0 bone dry .. 1 saturated
    public readonly float[] Special;      // the archetype's special field: heat, hotspots, spores...
    public readonly float[] Slope;        // rise over run
    public readonly float[] RiverFlow;    // catchment in km² where a river runs, else 0
    public readonly float[] RiverLevel;   // river surface height where a river runs
    public readonly int[] Downstream;     // index of the steepest-descent neighbour, -1 for sinks
    public readonly byte[] Biome;
    public readonly byte[] Flags;
    /// <summary>The dominant landform of each cell (a <see cref="Gen.Landform"/>); the model's landform layer has the blend.</summary>
    public readonly byte[] Landform;
    public readonly List<River> Rivers = new();
    public DepositField Deposits { get; internal set; }

    public float LandTarget { get; internal set; }
    public float LandFraction { get; internal set; }
    public double GenerationMs { get; internal set; }

    public WorldMap(PlanetSpec spec, ArchetypeDef def, TerrainModel model, float cellSize)
    {
        Spec = spec;
        Def = def;
        Model = model;
        CellSize = cellSize;
        Width = (int)Math.Round(spec.WidthMeters / cellSize);
        Height = (int)Math.Round(spec.HeightMeters / cellSize);
        int n = Width * Height;
        Elevation = new float[n];
        Temperature = new float[n];
        Humidity = new float[n];
        Special = new float[n];
        Slope = new float[n];
        RiverFlow = new float[n];
        RiverLevel = new float[n];
        Downstream = new int[n];
        Biome = new byte[n];
        Flags = new byte[n];
        Landform = new byte[n];
    }

    public int Count => Width * Height;
    public double WidthMeters => Width * (double)CellSize;
    public double HeightMeters => Height * (double)CellSize;

    public int WrapI(int i) => (i % Width + Width) % Width;
    public int WrapJ(int j) => (j % Height + Height) % Height;

    /// <summary>The cell at column i, row j, both wrapped onto the torus.</summary>
    public int Index(int i, int j) => WrapJ(j) * Width + WrapI(i);
    public bool Has(int index, byte flag) => (Flags[index] & flag) != 0;

    /// <summary>This world's landform layer: the landforms and their blend at any point.</summary>
    public LandformLayer Landforms => Model.Landforms;

    /// <summary>The landforms at a point in metres (both axes wrap), strongest first, with their weights.</summary>
    public LandformBlend LandformsAt(double x, double y) => Model.Landforms.At(Mathx.WrapX(x, WidthMeters), Mathx.Wrap(y, HeightMeters));

    /// <summary>Centre of a cell in metres.</summary>
    public double CellX(int i) => (i + 0.5) * CellSize;
    public double CellY(int j) => (j + 0.5) * CellSize;

    /// <summary>
    /// Bilinear sample of a per-cell field at a point in metres, wrapping on both axes. Used by
    /// the far terrain ring and by everything that blends between cells.
    /// </summary>
    public float Sample(float[] field, double x, double y)
    {
        double gx = x / CellSize - 0.5, gy = y / CellSize - 0.5;
        int i0 = (int)Math.Floor(gx), j0 = (int)Math.Floor(gy);
        float tx = (float)(gx - i0), ty = (float)(gy - j0);
        int j1 = j0 + 1;
        float a = field[Index(i0, j0)], b = field[Index(i0 + 1, j0)];
        float c = field[Index(i0, j1)], d = field[Index(i0 + 1, j1)];
        return Mathx.Lerp(Mathx.Lerp(a, b, tx), Mathx.Lerp(c, d, tx), ty);
    }

    /// <summary>The cell containing a point in metres.</summary>
    public int CellAt(double x, double y)
    {
        int i = (int)Math.Floor(x / CellSize);
        int j = (int)Math.Floor(y / CellSize);
        return Index(i, j);
    }

    /// <summary>FNV-1a over every generated field, for determinism checks.</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Add(uint v) { h = (h ^ v) * 1099511628211UL; }
        for (int k = 0; k < Count; k++)
        {
            Add((uint)BitConverter.SingleToInt32Bits(Elevation[k]));
            Add((uint)BitConverter.SingleToInt32Bits(Humidity[k]));
            Add((uint)BitConverter.SingleToInt32Bits(RiverFlow[k]));
            Add(Biome[k]);
            Add(Flags[k]);
            Add(Landform[k]);
        }
        foreach (River r in Rivers)
            for (int k = 0; k < r.Count; k++) Add((uint)BitConverter.SingleToInt32Bits(r.X[k] + r.Y[k]));
        if (Deposits != null)
            foreach (Deposit d in Deposits.All()) Add((uint)BitConverter.SingleToInt32Bits((float)(d.X * 3 + d.Y)) ^ (uint)d.Kind);
        return h;
    }
}
