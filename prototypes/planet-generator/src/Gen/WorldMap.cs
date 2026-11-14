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

    /// <summary>
    /// Metres below sea level by cell as the land lies before the rivers cut their channels into it (negative on
    /// land): where the sea is. A river's channel carved below sea level near the coast is still land, not a
    /// tongue of sea running up it.
    /// </summary>
    public readonly float[] SeaDepth;

    /// <summary>
    /// The sea is drawn, and is water wherever the ground lies under sea level, where <see cref="SeaDepth"/>
    /// blended between cells is over this: past the map's own coast, onto land up to 1.5 m above the water (the
    /// fine ground strays a metre or so either way of the map's), so the sea runs in under the shore and the
    /// ground makes the water's edge, never the map's coarse cells. Rivers come down to the sea's level where
    /// they reach it (<see cref="RiverFit"/>).
    /// </summary>
    public const float SeaFrom = -1.5f;

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
        SeaDepth = new float[n];
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

    /// <summary>Whether the sea is drawn over a point in metres (<see cref="SeaFrom"/>; both axes wrap).</summary>
    public bool IsSea(double x, double y) => Sample(SeaDepth, x, y) > SeaFrom;

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
