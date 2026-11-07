using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Stars;

/// <summary>
/// Lays a galaxy out from its seed. Nebulae, spiral arms and voids are chosen first; the stars are
/// then scattered by how dense each place should be, so the band in the sky, the voids and the
/// crowded core are all simply where the stars are. Each region picks its star classes.
/// </summary>
static class GalaxyGen
{
    /// <summary>How the disc is shaped: kept with the regions so <see cref="RegionOf"/> can read a place back.</summary>
    sealed class Shape
    {
        public int Arms;
        public float Twist, Phase;
        public (Vector2 at, float radius)[] Voids;

        /// <summary>Across-arm distance from the nearest arm, ly.</summary>
        public float ArmOffset(float r, float angle)
        {
            float best = float.MaxValue;
            for (int a = 0; a < Arms; a++)
            {
                float along = Phase + a * MathF.Tau / Arms + MathF.Log(MathF.Max(r, 30f) / 30f) * Twist;
                float d = MathF.IEEERemainder(angle - along, MathF.Tau);
                best = MathF.Min(best, MathF.Abs(d) * r);
            }
            return best;
        }

        public float VoidDepth(Vector2 p)
        {
            float keep = 1f;
            foreach (var (at, radius) in Voids)
            {
                float d = Vector2.Distance(p, at) / radius;
                if (d < 1f) keep *= 0.03f + 0.97f * d * d * d * d;
            }
            return keep;
        }
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Galaxy, Shape> Shapes = new();

    const float CoreRadius = 85f, InnerRadius = 260f, OuterRadius = 440f;

    public static Galaxy Make(ulong seed)
    {
        var rng = new GalaxyRng(Mix.Of(seed, 0x47414C41));
        var shape = new Shape { Arms = 2 + rng.Int(3), Twist = rng.Range(1.1f, 1.9f), Phase = rng.Range(0f, MathF.Tau) };
        var voids = new (Vector2, float)[4 + rng.Int(4)];
        for (int i = 0; i < voids.Length; i++)
        {
            float r = rng.Range(150f, 345f), a = rng.Range(0f, MathF.Tau);
            voids[i] = (new Vector2(MathF.Cos(a), MathF.Sin(a)) * r, rng.Range(38f, 72f));
        }
        shape.Voids = voids;

        Nebula[] nebulae = MakeNebulae(seed, ref rng, shape);
        var stars = new StarField(seed);
        // The central black hole is the first star of every galaxy.
        stars.Add(Vector3.Zero, Region.Core, StarKind.BlackHole, 900f, ref rng);

        int total = 21000 + rng.Int(13000);
        var satellites = MakeSatellites(seed, ref rng, stars);
        Clusters(ref rng, stars);
        Born(ref rng, stars, nebulae);

        // The core's bulge, then the disc. Most of the catalogue.
        int bulge = (total - stars.Count) * 15 / 100, field = total - stars.Count - bulge;
        for (int i = 0, tries = 0; i < bulge && tries < bulge * 20; tries++)
        {
            Vector3 dir = rng.Direction();
            float r = -MathF.Log(1f - rng.Float() * 0.98f) * 27f;
            if (r > CoreRadius * 1.15f) continue;
            Vector3 p = dir * r * new Vector3(1f, 0.62f, 1f);
            if (stars.TryAdd(p, Region.Core, ref rng)) i++;
        }
        for (int i = 0, tries = 0; i < field && tries < field * 80; tries++)
        {
            float r = MathF.Sqrt(rng.Float()) * (Galaxy.DiscRadius + 25f), a = rng.Range(0f, MathF.Tau);
            if (rng.Float() > DiscDensity(shape, r, a)) continue;
            float h = 9f + r * 0.022f;
            float u = Math.Clamp(rng.Float(), 1e-4f, 0.9999f);
            float y = MathF.Log(u / (1f - u)) * h * 0.55f; // a logistic drop off the disc's plane
            var flat = new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
            if (stars.TryAdd(new Vector3(flat.X, y, flat.Y), PlaceRegion(shape, r, a, flat), ref rng)) i++;
        }

        Star[] done = stars.Finish(nebulae);
        var galaxy = new Galaxy(seed, done, nebulae, satellites);
        Shapes.Add(galaxy, shape);
        return galaxy;
    }

    /// <summary>The chance a place on the disc keeps a star: arms stand out, voids are almost empty, and it thins to the rim.</summary>
    static float DiscDensity(Shape s, float r, float angle)
    {
        if (r < CoreRadius * 0.6f) return 0.08f;
        float width = 16f + r * 0.085f, off = s.ArmOffset(r, angle) / width;
        float arm = 0.1f + 0.9f * MathF.Exp(-off * off);
        float fall = MathF.Exp(-r / 330f);
        float rim = r > Galaxy.DiscRadius ? MathF.Max(0f, 1f - (r - Galaxy.DiscRadius) / 25f) : 1f;
        return arm * fall * rim * s.VoidDepth(new Vector2(MathF.Cos(angle) * r, MathF.Sin(angle) * r)) * 1.6f;
    }

    static Region PlaceRegion(Shape s, float r, float angle, Vector2 flat)
    {
        if (r <= CoreRadius) return Region.Core;
        if (r > OuterRadius) return Region.Periphery;
        if (s.VoidDepth(flat) < 0.5f) return Region.Void;
        return r <= InnerRadius ? Region.InnerArm : Region.OuterArm;
    }

    /// <summary>The region a place in the galaxy lies in.</summary>
    public static Region RegionOf(Galaxy g, Vector3 p)
    {
        Shapes.TryGetValue(g, out Shape s);
        var flat = new Vector2(p.X, p.Z);
        float r = flat.Length();
        return r > Galaxy.DiscRadius + 80f ? Region.Satellite : PlaceRegion(s, r, MathF.Atan2(p.Z, p.X), flat);
    }

    // ---- Nebulae --------------------------------------------------------------------------------

    static Nebula[] MakeNebulae(ulong seed, ref GalaxyRng rng, Shape shape)
    {
        int count = 38 + rng.Int(24);
        var list = new Nebula[count];
        // A share of them lie out on the rim, where every game begins: the rest along the arms.
        int rim = 8 + rng.Int(5);
        for (int i = 0; i < count; i++)
        {
            NebulaKind kind = rng.Float() switch { < 0.4f => NebulaKind.Emission, < 0.68f => NebulaKind.Dark, < 0.86f => NebulaKind.Reflection, _ => NebulaKind.Remnant };
            if (i < rim && kind == NebulaKind.Dark) kind = NebulaKind.Emission;
            Vector2 flat = default;
            float r = 0f, angle = 0f;
            for (int tries = 0; tries < 60; tries++)
            {
                r = i < rim ? rng.Range(Galaxy.RimFrom + 5f, 485f) : rng.Range(CoreRadius + 15f, 420f);
                angle = rng.Range(0f, MathF.Tau);
                flat = new Vector2(MathF.Cos(angle) * r, MathF.Sin(angle) * r);
                if (shape.ArmOffset(r, angle) < 14f + r * 0.08f && shape.VoidDepth(flat) > 0.8f) break;
            }
            float radius = kind switch
            {
                NebulaKind.Emission => rng.Range(20f, 58f),
                NebulaKind.Dark => rng.Range(16f, 46f),
                NebulaKind.Reflection => rng.Range(11f, 30f),
                _ => rng.Range(9f, 24f),
            };
            var lobes = new (Vector3, float)[3 + rng.Int(4)];
            for (int l = 0; l < lobes.Length; l++)
            {
                float size = rng.Range(0.4f, 0.75f);
                lobes[l] = (rng.InBall() * (1f - size) * 0.95f, size);
            }
            Vector3 hue = kind switch
            {
                // Glowing gas: hydrogen red and rose, ionised teal in the hottest; each its own mix.
                NebulaKind.Emission => Vector3.Lerp(new Vector3(0.95f, 0.22f, 0.3f), rng.Float() < 0.5f ? new Vector3(0.95f, 0.4f, 0.55f) : new Vector3(0.75f, 0.2f, 0.65f), rng.Float()),
                NebulaKind.Reflection => Vector3.Lerp(new Vector3(0.32f, 0.5f, 1f), new Vector3(0.45f, 0.72f, 1f), rng.Float()),
                NebulaKind.Remnant => Vector3.Lerp(new Vector3(0.3f, 0.85f, 0.8f), new Vector3(0.9f, 0.55f, 0.35f), rng.Float()),
                _ => new Vector3(0.1f, 0.075f, 0.06f),
            };
            Vector3 rimHue = kind switch
            {
                NebulaKind.Emission => Vector3.Lerp(new Vector3(0.2f, 0.6f, 0.7f), new Vector3(0.65f, 0.3f, 0.2f), rng.Float()),
                NebulaKind.Reflection => new Vector3(0.2f, 0.28f, 0.6f),
                NebulaKind.Remnant => Vector3.Lerp(new Vector3(0.75f, 0.3f, 0.5f), new Vector3(0.25f, 0.5f, 0.9f), rng.Float()),
                _ => new Vector3(0.07f, 0.05f, 0.05f),
            };
            list[i] = new Nebula
            {
                Id = i, Name = GalaxyNames.Nebula(seed, i, kind), Kind = kind,
                Centre = new Vector3(flat.X, rng.Range(-14f, 14f), flat.Y),
                Radius = radius,
                Axes = new Vector3(rng.Range(0.6f, 1f), rng.Range(0.45f, 0.85f), rng.Range(0.6f, 1f)),
                Turn = rng.Turn(), Colour = hue, Rim = rimHue,
                Glow = kind switch { NebulaKind.Emission => 0.045f, NebulaKind.Reflection => 0.036f, NebulaKind.Remnant => 0.07f, _ => 0.002f } * rng.Range(0.7f, 1.25f),
                Dust = kind switch { NebulaKind.Dark => 0.32f * 16f / radius + 0.1f, NebulaKind.Emission => 0.006f, NebulaKind.Reflection => 0.012f, _ => 0f },
                Offset = new Vector3(rng.Range(0f, 32f), rng.Range(0f, 32f), rng.Range(0f, 32f)), Lobes = lobes,
            };
        }
        return list;
    }

    /// <summary>The stars born in each nebula (with a pulsar or magnetar at a remnant's heart): a nebula always has systems to go to.</summary>
    static void Born(ref GalaxyRng rng, StarField stars, Nebula[] nebulae)
    {
        foreach (Nebula n in nebulae)
        {
            int want = n.Kind switch
            {
                NebulaKind.Emission => 40 + (int)(n.Radius * 2.4f),
                NebulaKind.Dark => 8 + (int)(n.Radius * 0.5f),
                NebulaKind.Reflection => 16 + (int)n.Radius,
                _ => 8 + (int)(n.Radius * 0.6f),
            };
            if (n.Kind == NebulaKind.Remnant)
            {
                StarKind heart = rng.Float() < 0.7f ? StarKind.Pulsar : StarKind.Magnetar;
                stars.Add(n.Centre, RegionAtFlat(n.Centre), heart, rng.Log(0.05f, 1f), ref rng);
            }
            for (int i = 0, tries = 0; i < want && tries < want * 200; tries++)
            {
                Vector3 p = n.Centre + rng.InBall() * n.Radius;
                if (n.Density(p) <= Nebula.Edge * 1.3f) continue;
                StarKind kind = n.Kind switch
                {
                    NebulaKind.Emission => rng.Float() switch { < 0.32f => StarKind.Protostar, < 0.45f => StarKind.BlueGiant, < 0.62f => StarKind.WhiteStar, < 0.78f => StarKind.YellowDwarf, < 0.9f => StarKind.OrangeDwarf, _ => StarKind.RedDwarf },
                    NebulaKind.Reflection => rng.Float() switch { < 0.3f => StarKind.BlueGiant, < 0.6f => StarKind.WhiteStar, < 0.8f => StarKind.YellowDwarf, _ => StarKind.OrangeDwarf },
                    NebulaKind.Dark => rng.Float() switch { < 0.3f => StarKind.Protostar, < 0.65f => StarKind.RedDwarf, _ => StarKind.BrownDwarf },
                    _ => rng.Float() switch { < 0.25f => StarKind.WhiteDwarf, < 0.5f => StarKind.RedDwarf, < 0.75f => StarKind.OrangeDwarf, _ => StarKind.YellowDwarf },
                };
                if (stars.TryAdd(p, RegionAtFlat(p), kind, LightOf(kind, ref rng), ref rng)) i++;
            }
        }
    }

    static Region RegionAtFlat(Vector3 p)
    {
        float r = new Vector2(p.X, p.Z).Length();
        return r <= CoreRadius ? Region.Core : r > OuterRadius ? Region.Periphery : r <= InnerRadius ? Region.InnerArm : Region.OuterArm;
    }

    // ---- Dense places ---------------------------------------------------------------------------

    /// <summary>Globular clusters: dense balls of ancient stars near the core.</summary>
    static void Clusters(ref GalaxyRng rng, StarField stars)
    {
        int n = 3 + rng.Int(4);
        for (int c = 0; c < n; c++)
        {
            float a = rng.Range(0f, MathF.Tau), r = rng.Range(95f, 210f);
            var centre = new Vector3(MathF.Cos(a) * r, rng.Range(-70f, 70f), MathF.Sin(a) * r);
            float radius = rng.Range(7f, 13f);
            int want = 110 + rng.Int(140);
            for (int i = 0, tries = 0; i < want && tries < want * 30; tries++)
            {
                Vector3 p = centre + rng.Direction() * (radius * MathF.Pow(rng.Float(), 0.8f) * 1.5f);
                StarKind kind = rng.Float() switch { < 0.5f => StarKind.RedDwarf, < 0.72f => StarKind.OrangeDwarf, < 0.88f => StarKind.RedGiant, < 0.98f => StarKind.WhiteDwarf, _ => StarKind.Pulsar };
                if (stars.TryAdd(p, Region.Core, kind, LightOf(kind, ref rng), ref rng, spacing: 0.7f)) i++;
            }
        }
    }

    /// <summary>A few small satellite galaxies, off past the disc.</summary>
    static Satellite[] MakeSatellites(ulong seed, ref GalaxyRng rng, StarField stars)
    {
        var list = new Satellite[3 + rng.Int(3)];
        for (int s = 0; s < list.Length; s++)
        {
            float a = rng.Range(0f, MathF.Tau), r = rng.Range(640f, 900f);
            var centre = new Vector3(MathF.Cos(a) * r, rng.Range(-220f, 220f), MathF.Sin(a) * r);
            float radius = rng.Range(32f, 62f);
            int want = 170 + rng.Int(300), first = stars.Count;
            for (int i = 0, tries = 0; i < want && tries < want * 30; tries++)
            {
                Vector3 p = centre + rng.Direction() * (-MathF.Log(1f - rng.Float() * 0.95f) * radius * 0.4f) * new Vector3(1f, 0.7f, 1f);
                if (stars.TryAdd(p, Region.Satellite, ref rng)) i++;
            }
            list[s] = new Satellite(GalaxyNames.Satellite(seed, s), centre, radius, first, stars.Count - first);
        }
        return list;
    }

    // ---- What each place holds --------------------------------------------------------------------

    /// <summary>How likely each kind of star is in a region (the design: region picks star classes).</summary>
    static readonly (StarKind kind, float weight)[][] Kinds = MakeKinds();

    static (StarKind, float)[][] MakeKinds()
    {
        var t = new (StarKind, float)[6][];
        t[(int)Region.Core] = new[]
        {
            (StarKind.RedDwarf, 0.25f), (StarKind.OrangeDwarf, 0.2f), (StarKind.YellowDwarf, 0.1f), (StarKind.WhiteStar, 0.04f), (StarKind.BlueGiant, 0.004f), (StarKind.BrownDwarf, 0.05f),
            (StarKind.RedGiant, 0.2f), (StarKind.WhiteDwarf, 0.11f), (StarKind.Pulsar, 0.016f), (StarKind.Magnetar, 0.004f), (StarKind.BlackHole, 0.002f),
        };
        t[(int)Region.InnerArm] = new[]
        {
            (StarKind.RedDwarf, 0.39f), (StarKind.OrangeDwarf, 0.19f), (StarKind.YellowDwarf, 0.14f), (StarKind.WhiteStar, 0.09f), (StarKind.BlueGiant, 0.03f), (StarKind.BrownDwarf, 0.07f),
            (StarKind.Protostar, 0.02f), (StarKind.RedGiant, 0.04f), (StarKind.WhiteDwarf, 0.03f), (StarKind.Pulsar, 0.004f), (StarKind.Magnetar, 0.001f), (StarKind.BlackHole, 0.0005f),
        };
        t[(int)Region.OuterArm] = new[]
        {
            (StarKind.RedDwarf, 0.4f), (StarKind.OrangeDwarf, 0.2f), (StarKind.YellowDwarf, 0.16f), (StarKind.WhiteStar, 0.08f), (StarKind.BlueGiant, 0.02f), (StarKind.BrownDwarf, 0.09f),
            (StarKind.Protostar, 0.012f), (StarKind.RedGiant, 0.02f), (StarKind.WhiteDwarf, 0.02f), (StarKind.Pulsar, 0.002f), (StarKind.BlackHole, 0.0003f),
        };
        t[(int)Region.Void] = new[]
        {
            (StarKind.RedDwarf, 0.5f), (StarKind.BrownDwarf, 0.24f), (StarKind.OrangeDwarf, 0.1f), (StarKind.YellowDwarf, 0.05f), (StarKind.WhiteDwarf, 0.08f), (StarKind.RedGiant, 0.02f), (StarKind.Pulsar, 0.01f),
        };
        t[(int)Region.Periphery] = new[]
        {
            (StarKind.RedDwarf, 0.29f), (StarKind.OrangeDwarf, 0.17f), (StarKind.YellowDwarf, 0.14f), (StarKind.WhiteStar, 0.08f), (StarKind.BlueGiant, 0.04f), (StarKind.BrownDwarf, 0.13f),
            (StarKind.RedGiant, 0.05f), (StarKind.WhiteDwarf, 0.06f), (StarKind.Pulsar, 0.025f), (StarKind.Magnetar, 0.01f), (StarKind.BlackHole, 0.005f),
        };
        t[(int)Region.Satellite] = new[]
        {
            (StarKind.RedDwarf, 0.32f), (StarKind.OrangeDwarf, 0.2f), (StarKind.YellowDwarf, 0.1f), (StarKind.WhiteStar, 0.06f), (StarKind.BlueGiant, 0.05f), (StarKind.BrownDwarf, 0.08f),
            (StarKind.RedGiant, 0.08f), (StarKind.WhiteDwarf, 0.07f), (StarKind.Pulsar, 0.02f), (StarKind.Magnetar, 0.012f), (StarKind.BlackHole, 0.008f),
        };
        return t;
    }

    static StarKind KindIn(Region r, ref GalaxyRng rng)
    {
        (StarKind kind, float weight)[] table = Kinds[(int)r];
        float total = 0f;
        foreach (var e in table) total += e.weight;
        float pick = rng.Float() * total;
        foreach (var e in table)
            if ((pick -= e.weight) <= 0f) return e.kind;
        return table[0].kind;
    }

    /// <summary>A star's brightness, suns.</summary>
    static float LightOf(StarKind k, ref GalaxyRng rng) => k switch
    {
        StarKind.BrownDwarf => rng.Log(0.0002f, 0.004f),
        StarKind.RedDwarf => rng.Log(0.003f, 0.08f),
        StarKind.OrangeDwarf => rng.Log(0.1f, 0.6f),
        StarKind.YellowDwarf => rng.Log(0.6f, 1.7f),
        StarKind.WhiteStar => rng.Log(2f, 40f),
        StarKind.BlueGiant => rng.Log(150f, 6000f),
        StarKind.Protostar => rng.Log(1f, 90f),
        StarKind.RedGiant => rng.Log(60f, 1500f),
        StarKind.WhiteDwarf => rng.Log(0.003f, 0.08f),
        StarKind.Pulsar => rng.Log(0.02f, 0.7f),
        StarKind.Magnetar => rng.Log(0.05f, 1.2f),
        _ => rng.Log(0.4f, 6f),
    };

    /// <summary>The stars as they are placed: no two closer than a set spacing, each given its place's class.</summary>
    sealed class StarField
    {
        readonly List<Star> _stars = new(36000);
        readonly Dictionary<long, int> _cells = new(40000);
        readonly List<int> _sameCell = new(36000);
        readonly ulong _seed;
        const float Spacing = 1f;

        public StarField(ulong seed) => _seed = seed;

        public int Count => _stars.Count;

        static long Key(int x, int y, int z) => ((long)(x + 4096) << 40) | ((long)(y + 4096) << 20) | (uint)(z + 4096);

        bool Free(Vector3 p, float spacing)
        {
            int cx = (int)MathF.Floor(p.X), cy = (int)MathF.Floor(p.Y), cz = (int)MathF.Floor(p.Z);
            for (int x = cx - 1; x <= cx + 1; x++)
            for (int y = cy - 1; y <= cy + 1; y++)
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                if (!_cells.TryGetValue(Key(x, y, z), out int id)) continue;
                for (; id >= 0; id = _sameCell[id])
                    if (Vector3.DistanceSquared(_stars[id].Position, p) < spacing * spacing) return false;
            }
            return true;
        }

        public bool TryAdd(Vector3 p, Region region, ref GalaxyRng rng)
        {
            StarKind kind = KindIn(region, ref rng);
            return TryAdd(p, region, kind, LightOf(kind, ref rng), ref rng);
        }

        public bool TryAdd(Vector3 p, Region region, StarKind kind, float light, ref GalaxyRng rng, float spacing = Spacing)
        {
            if (!Free(p, spacing)) return false;
            Add(p, region, kind, light, ref rng);
            return true;
        }

        public void Add(Vector3 p, Region region, StarKind kind, float light, ref GalaxyRng rng)
        {
            // Double stars, and now and then a third: one catalogue entry, one system, their light together.
            byte companions = 0;
            StarKind companion = kind;
            if (kind is not (StarKind.BlackHole or StarKind.Pulsar or StarKind.Magnetar or StarKind.BrownDwarf))
            {
                float roll = rng.Float();
                if (roll < 0.17f)
                {
                    companions = (byte)(roll < 0.018f ? 2 : 1);
                    companion = rng.Float() switch { < 0.45f => StarKind.RedDwarf, < 0.7f => StarKind.OrangeDwarf, < 0.85f => StarKind.YellowDwarf, < 0.95f => StarKind.WhiteDwarf, _ => StarKind.WhiteStar };
                    light += LightOf(companion, ref rng) * companions;
                }
            }
            int id = _stars.Count;
            _stars.Add(new Star(id, p, light, kind, region, companions, companion, -1));
            long cell = Key((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
            _sameCell.Add(_cells.TryGetValue(cell, out int first) ? first : -1);
            _cells[cell] = id;
        }

        /// <summary>The catalogue, each star told which nebula it lies in.</summary>
        public Star[] Finish(Nebula[] nebulae)
        {
            Star[] done = _stars.ToArray();
            for (int i = 0; i < done.Length; i++)
            {
                short best = -1;
                float most = Nebula.Edge;
                foreach (Nebula n in nebulae)
                {
                    if (Vector3.DistanceSquared(done[i].Position, n.Centre) >= n.Radius * n.Radius) continue;
                    float d = n.Density(done[i].Position);
                    if (d > most) (most, best) = (d, (short)n.Id);
                }
                if (best >= 0) done[i] = done[i] with { Nebula = best };
            }
            return done;
        }
    }
}
