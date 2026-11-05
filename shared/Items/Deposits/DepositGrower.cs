using System;
using System.Linq;

namespace GalHack.Items.Deposits;

/// <summary>
/// One deposit to grow, as the planet generator places it: a seed, where, which ore and how
/// much (its Amount, 0.6 to 1.6). The shape is picked from the ore's weights unless given.
/// </summary>
public readonly record struct DepositSeed(ulong Seed, double X, double Z, string Ore, double Amount, DepositShape? Shape = null);

/// <summary>
/// Grows ore bodies. Each shape is a few signed-distance parts (blobs, tubes, sheets) with a
/// lumpy outline, sized so the body holds about the volume the rules give for its amount, and
/// laid under the real ground so beds follow hills and veins dip into slopes. Pure C#, no engine.
/// </summary>
public static class DepositGrower
{
    const double Reach = 18; // metres around the deposit point the ground is sampled for

    public static DepositBody Grow(DepositSeed seed, Func<double, double, double> surface, DepositRules rules)
    {
        var dice = new Dice(Rng.Of(seed.Seed, 0xDE905, (long)Math.Round(seed.X * 8), (long)Math.Round(seed.Z * 8)));
        DepositShape shape = seed.Shape ?? rules.PickShape(seed.Ore, dice.Next());
        ShapeRule rule = rules.Shape(shape);
        double amount = Math.Clamp(seed.Amount, 0.2, 3);
        var ground = new HeightGrid(surface, seed.X - Reach, seed.Z - Reach, seed.X + Reach, seed.Z + Reach, 2);

        var body = new DepositBody
        {
            Ore = seed.Ore,
            Shape = shape,
            Amount = amount,
            X = seed.X,
            Z = seed.Z,
            TargetVolume = rule.Volume * amount,
            BaseGrade = rules.PickBaseGrade(dice.Next()),
        };
        body.Ground = ground;
        body.MaxDepth = rules.MaxDepth;
        body.Lumps = new Lumps(Rng.Of(seed.Seed, 0x1A3B), 2.2);
        body.SetYields(rules);

        double top = dice.Range(rule.TopDepthMin, rule.TopDepthMax);
        double fracture = double.NaN, fx = 0, fz = 0;
        switch (shape)
        {
            case DepositShape.Pocket: Pockets(body, dice, ground, seed.X, seed.Z, top); break;
            case DepositShape.Vein: Vein(body, dice, ground, seed.X, seed.Z, top, rules.MaxDepth); break;
            case DepositShape.Bed: Bed(body, dice, ground, seed.X, seed.Z, top); break;
            default: (fx, fz, fracture) = Deep(body, dice, ground, seed.X, seed.Z, top); break;
        }

        Normalise(body);
        AddTells(body, shape, fx, fz, fracture);
        return body;
    }

    // ---- Shapes -----------------------------------------------------------------------------

    /// <summary>Three to five pockets, each joined to the last by a thin mineralised passage.</summary>
    static void Pockets(DepositBody body, Dice dice, HeightGrid ground, double x, double z, double top)
    {
        int n = dice.Int(3, 5);
        Blob previous = null, largest = null;
        for (int i = 0; i < n; i++)
        {
            double a = dice.Range(0, Math.Tau), d = i == 0 ? 0 : dice.Range(2.0, 4.5);
            double bx = x + Math.Cos(a) * d, bz = z + Math.Sin(a) * d;
            var blob = new Blob
            {
                Cx = bx, Cz = bz, Cy = ground.At(bx, bz) - top - 1.4 - dice.Range(0, 1.2),
                Rx = dice.Range(1.2, 2.0), Rz = dice.Range(1.2, 2.0), Ry = dice.Range(0.8, 1.3), Turn = dice.Range(0, Math.PI),
            };
            body.Parts.Add(blob);
            if (previous != null)
                body.Parts.Add(new Capsule { Ax = previous.Cx, Ay = previous.Cy, Az = previous.Cz, Bx = blob.Cx, By = blob.Cy, Bz = blob.Cz, Ra = 0.45, Rb = 0.45 });
            if (largest == null || blob.Size > largest.Size) largest = blob;
            previous = blob;
        }
        body.Hot.Add(new HotSpot(largest.Cx, largest.Cy, largest.Cz, largest.Size * 1.3));
    }

    /// <summary>A winding tube that swells and pinches as it dips into the ground, sometimes splaying a branch.</summary>
    static void Vein(DepositBody body, Dice dice, HeightGrid ground, double x, double z, double top, double maxDepth)
    {
        int n = dice.Int(6, 8);
        double seg = dice.Range(2.0, 2.8);
        double azimuth = dice.Range(0, Math.Tau), dip = dice.Range(0.25, 0.6);
        double half = n * seg * 0.5;
        double px = x - Math.Cos(azimuth) * half * 0.8, pz = z - Math.Sin(azimuth) * half * 0.8;
        double py = ground.At(px, pz) - top - 0.6, r = dice.Range(0.8, 1.0);
        var nodes = new (double X, double Y, double Z, double R)[n + 1];
        nodes[0] = (px, py, pz, r);
        for (int i = 1; i <= n; i++)
        {
            azimuth += (dice.Next() - 0.5) * 0.9;
            dip = Math.Clamp(dip + (dice.Next() - 0.5) * 0.5, -0.15, 0.9);
            px += Math.Cos(azimuth) * seg * Math.Cos(dip);
            pz += Math.Sin(azimuth) * seg * Math.Cos(dip);
            py -= seg * Math.Sin(dip);
            // Keep it within reach of the sampled ground and inside the dig band.
            px = Math.Clamp(px, x - Reach + 3, x + Reach - 3);
            pz = Math.Clamp(pz, z - Reach + 3, z + Reach - 3);
            double g = ground.At(px, pz);
            if (g - py > maxDepth - 2) { py = g - (maxDepth - 2); dip = -0.2; }
            if (g - py < 0.6) { py = g - 0.6; dip = 0.4; }
            r = Math.Clamp(r * dice.Range(0.72, 1.32), 0.55, 1.5);
            nodes[i] = (px, py, pz, r);
        }
        for (int i = 0; i < n; i++)
            body.Parts.Add(new Capsule
            {
                Ax = nodes[i].X, Ay = nodes[i].Y, Az = nodes[i].Z, Ra = nodes[i].R,
                Bx = nodes[i + 1].X, By = nodes[i + 1].Y, Bz = nodes[i + 1].Z, Rb = nodes[i + 1].R,
            });

        // A splay: a thinner branch leaving from somewhere along the middle.
        if (dice.Next() < 0.6)
        {
            int k = dice.Int(2, n - 2);
            var (bx, by, bz, br) = nodes[k];
            double ba = azimuth + (dice.Next() < 0.5 ? -1 : 1) * dice.Range(0.6, 1.1);
            for (int s = 0, m = dice.Int(2, 3); s < m; s++)
            {
                double nx = Math.Clamp(bx + Math.Cos(ba) * seg, x - Reach + 3, x + Reach - 3), nz = Math.Clamp(bz + Math.Sin(ba) * seg, z - Reach + 3, z + Reach - 3);
                double ny = Math.Clamp(by - seg * dice.Range(-0.2, 0.5), ground.At(nx, nz) - (maxDepth - 2), ground.At(nx, nz) - 0.8);
                double nr = Math.Max(0.5, br * 0.75);
                body.Parts.Add(new Capsule { Ax = bx, Ay = by, Az = bz, Ra = br * 0.8, Bx = nx, By = ny, Bz = nz, Rb = nr });
                (bx, by, bz, br) = (nx, ny, nz, nr);
                ba += (dice.Next() - 0.5) * 0.6;
            }
        }

        foreach (var node in nodes.OrderByDescending(v => v.R).Take(dice.Int(1, 2)))
            body.Hot.Add(new HotSpot(node.X, node.Y, node.Z, node.R * 2.6));
    }

    /// <summary>A layered bed lying a set depth under the ground, now and then split into a second sheet below.</summary>
    static void Bed(DepositBody body, Dice dice, HeightGrid ground, double x, double z, double top)
    {
        double turn = dice.Range(0, Math.PI), half = dice.Range(0.45, 0.6);
        var warp = new Lumps(Rng.Of(Rng.OfText(body.Ore), (long)(x * 4), (long)(z * 4)), 9);
        var sheet = new Sheet
        {
            Cx = x, Cz = z, Rx = dice.Range(4.5, 6.5), Rz = dice.Range(3.0, 4.5), Turn = turn,
            Depth = top + half, Half = half, Warp = 0.8, WarpNoise = warp,
        };
        AddSheet(body, ground, sheet);
        if (dice.Next() < 0.35)
            AddSheet(body, ground, new Sheet
            {
                Cx = x + dice.Range(-1.5, 1.5), Cz = z + dice.Range(-1.5, 1.5), Rx = sheet.Rx * dice.Range(0.55, 0.8), Rz = sheet.Rz * dice.Range(0.55, 0.8),
                Turn = turn + dice.Range(-0.3, 0.3), Depth = sheet.Depth + dice.Range(2.2, 3.2), Half = half * 0.8, Warp = 0.6, WarpNoise = warp,
            });
        for (int i = 0, n = dice.Int(1, 2); i < n; i++)
        {
            double a = dice.Range(0, Math.Tau), d = dice.Range(0, 0.6);
            double hx = x + Math.Cos(a) * sheet.Rx * d, hz = z + Math.Sin(a) * sheet.Rz * d;
            body.Hot.Add(new HotSpot(hx, ground.At(hx, hz) - sheet.Depth, hz, 3.2));
        }
    }

    static void AddSheet(DepositBody body, HeightGrid ground, Sheet sheet)
    {
        double lo = double.MaxValue, hi = double.MinValue, r = Math.Max(sheet.Rx, sheet.Rz);
        for (double dz = -r; dz <= r; dz += 1.5)
            for (double dx = -r; dx <= r; dx += 1.5)
            {
                double g = ground.At(sheet.Cx + dx, sheet.Cz + dz);
                lo = Math.Min(lo, g);
                hi = Math.Max(hi, g);
            }
        sheet.TopY = hi;
        sheet.BottomY = lo;
        body.Parts.Add(sheet);
    }

    /// <summary>
    /// A rich core near the bottom of the dig band with satellite pockets, and a thin stringer
    /// rising from it towards the surface, ending in cracked rock a player can follow down.
    /// Returns where the stringer tops out.
    /// </summary>
    static (double X, double Z, double Y) Deep(DepositBody body, Dice dice, HeightGrid ground, double x, double z, double top)
    {
        double cx = x + dice.Range(-1, 1), cz = z + dice.Range(-1, 1);
        var core = new Blob
        {
            Cx = cx, Cz = cz, Cy = ground.At(cx, cz) - top - 1.8,
            Rx = dice.Range(1.7, 2.2), Rz = dice.Range(1.7, 2.2), Ry = dice.Range(1.2, 1.6), Turn = dice.Range(0, Math.PI),
        };
        body.Parts.Add(core);
        for (int i = 0, n = dice.Int(2, 3); i < n; i++)
        {
            double a = dice.Range(0, Math.Tau), d = dice.Range(2.6, 4.0);
            var sat = new Blob
            {
                Cx = cx + Math.Cos(a) * d, Cz = cz + Math.Sin(a) * d, Cy = core.Cy + dice.Range(-0.8, 1.2),
                Rx = dice.Range(0.9, 1.3), Rz = dice.Range(0.9, 1.3), Ry = dice.Range(0.7, 1.0), Turn = dice.Range(0, Math.PI),
            };
            body.Parts.Add(sat);
            body.Parts.Add(new Capsule { Ax = core.Cx, Ay = core.Cy, Az = core.Cz, Bx = sat.Cx, By = sat.Cy, Bz = sat.Cz, Ra = 0.45, Rb = 0.4 });
        }
        double sx = cx + dice.Range(-1.5, 1.5), sz = cz + dice.Range(-1.5, 1.5), sy = ground.At(sx, sz) - 1.2;
        body.Parts.Add(new Capsule { Ax = core.Cx, Ay = core.Cy + core.Ry * 0.5, Az = core.Cz, Ra = 0.4, Bx = sx, By = sy, Bz = sz, Rb = 0.3 });
        body.Hot.Add(new HotSpot(core.Cx, core.Cy, core.Cz, core.Size * 1.15));
        return (sx, sz, sy);
    }

    // ---- Sizing and tells -----------------------------------------------------------------------

    /// <summary>Thickens or thins every part until the body holds about the target volume of whole cells.</summary>
    static void Normalise(DepositBody body)
    {
        const int Passes = 6;
        double bestError = double.MaxValue, bestScale = 1, applied = 1;
        for (int pass = 0; pass < Passes; pass++)
        {
            body.ComputeBounds();
            int cells = body.ForEachCell();
            double ratio = body.TargetVolume / Math.Max(cells, 1);
            double error = cells > 0 ? Math.Abs(ratio - 1) : double.MaxValue;
            if (error < bestError) { bestError = error; bestScale = applied; }
            if (error < 0.08 || pass == Passes - 1) break;
            double s = Math.Clamp(Math.Pow(ratio, 0.45), 0.6, 1.8);
            foreach (Part p in body.Parts) p.ScaleRadii(s);
            applied *= s;
        }
        // Whole cells make the volume jump in steps: keep the closest pass, not just the last.
        if (bestScale != applied)
            foreach (Part p in body.Parts) p.ScaleRadii(bestScale / applied);
        body.ComputeBounds();
    }

    static void AddTells(DepositBody body, DepositShape shape, double fx, double fz, double fy)
    {
        double rx = (body.MaxX - body.MinX) * 0.5, rz = (body.MaxZ - body.MinZ) * 0.5;
        body.Tells.Add(new Tell(TellKind.Stain, (body.MinX + body.MaxX + 1) * 0.5, (body.MinZ + body.MaxZ + 1) * 0.5, Math.Max(rx, rz) + 1.5));
        int outcrops = 0;
        for (int z = body.MinZ; z <= body.MaxZ && outcrops < 48; z++)
            for (int x = body.MinX; x <= body.MaxX && outcrops < 48; x++)
            {
                double cx = x + 0.5, cz = z + 0.5, g = body.SurfaceAt(cx, cz);
                if (!body.Contains(cx, g - 0.5, cz) && !body.Contains(cx, g - 1.5, cz)) continue;
                body.Tells.Add(new Tell(TellKind.Outcrop, cx, cz, 0.5));
                outcrops++;
            }
        if (shape == DepositShape.Deep && !double.IsNaN(fy)) body.Tells.Add(new Tell(TellKind.Fracture, fx, fz, 1.2));
    }
}
