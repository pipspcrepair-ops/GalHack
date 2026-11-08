using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GalHack.Plants;
using Godot;
using Planets.Look;

namespace PlanetSurface.Terrain;

/// <summary>
/// Which scattered flora is solid, from data/flora-solids.json: trees, cacti and crystals get an
/// upright (or leaning) trunk cylinder, rocks and boulders a box, each sized from the instance's
/// own scale and turned with it. Rocks shorter than <see cref="MinRockHeight"/> stay pebbles you
/// walk over. Shrubs, mushrooms and grasses are never solid.
/// </summary>
public sealed class FloraSolidRules
{
    public sealed class TrunkRule
    {
        public float Radius { get; set; }
        public float Height { get; set; }
        public float[] Axis { get; set; }
    }

    public sealed class BoxRule
    {
        public float[] Half { get; set; }
        public float Cy { get; set; }
    }

    public sealed class KindRule
    {
        public TrunkRule Trunk { get; set; }
        public BoxRule Box { get; set; }
        public int Wood { get; set; }
    }

    /// <summary>Rocks no taller than this above the ground (m) are pebbles: never solid.</summary>
    public float MinRockHeight { get; set; } = 0.6f;
    public Dictionary<string, KindRule> Kinds { get; set; } = new();

    readonly KindRule[] _byKind = new KindRule[Enum.GetValues<Flora>().Length];

    /// <summary>How far a scatter mesh's foot is sunk into the ground (FarBuilder places it so).</summary>
    public const float Sink = 0.25f;

    public static FloraSolidRules Load(string path)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var rules = JsonSerializer.Deserialize<FloraSolidRules>(File.ReadAllText(path), options) ?? new FloraSolidRules();
        foreach (var (name, rule) in rules.Kinds)
        {
            if (!Enum.TryParse(name, out Flora kind)) throw new InvalidDataException($"flora-solids.json: unknown flora kind '{name}'");
            rules._byKind[(int)kind] = rule;
        }
        return rules;
    }

    public KindRule Of(Flora kind) => _byKind[(int)kind];

    /// <summary>
    /// The collider a rolled species' instance gets: its own trunk, read from its body, and wood by
    /// how much trunk it has against its kind's base in data/flora-solids.json. False if you walk through it.
    /// </summary>
    public bool TryMake(Flora kind, Plant plant, long id, Vector3 foot, float scale, float angle, out FloraSolid solid)
    {
        if (plant == null) return TryMake(kind, id, foot, scale, angle, out solid);
        solid = default;
        KindRule rule = _byKind[(int)kind];
        if (!plant.Solid || rule?.Trunk == null) return false;
        var turn = new Basis(Vector3.Up, angle);
        Vector3 axis = turn * new Vector3(plant.TrunkAxis.X, plant.TrunkAxis.Y, plant.TrunkAxis.Z).Normalized();
        float height = plant.TrunkHeight * scale, radius = plant.TrunkRadius * scale;
        Basis lean = new Basis(new Quaternion(Vector3.Up, axis)) * turn;
        float mass = plant.TrunkHeight / rule.Trunk.Height * Mathf.Pow(plant.TrunkRadius / rule.Trunk.Radius, 2f);
        int wood = rule.Wood > 0 ? Math.Max(1, Mathf.RoundToInt(rule.Wood * Math.Clamp(mass, 0.5f, 3f))) : 0;
        solid = new FloraSolid(id, kind, true, foot + axis * (height * 0.5f), lean.Orthonormalized(), new Vector3(radius, height * 0.5f, radius), wood, plant.Index);
        return true;
    }

    /// <summary>
    /// The colliders a rolled species' instance gets: one round each solid part of the near mesh drawn
    /// there (<paramref name="variant"/>): a tree's trunk and its thick limbs, every pipe of a cluster of
    /// tubes, a trumpet's stalk and bell, a cap, each shard of a crystal; so you meet the plant where you see
    /// it, whatever its form. The first (the main stem) carries the wood. False if you walk through it.
    /// </summary>
    public bool MakeAll(Flora kind, Plant plant, int variant, long id, Vector3 foot, float scale, float angle, List<FloraSolid> into)
    {
        if (!TryMake(kind, plant, id, foot, scale, angle, out FloraSolid main)) return false;
        PlantColumn[] columns = plant?.Columns is { Length: > 0 } all ? all[Math.Clamp(variant, 0, all.Length - 1)] : null;
        if (columns == null || columns.Length == 0)
        {
            into.Add(main);
            return true;
        }
        var turn = new Basis(Vector3.Up, angle);
        bool first = true;
        foreach (PlantColumn c in columns)
        {
            Vector3 a = foot + turn * new Vector3(c.A.X, c.A.Y, c.A.Z) * scale, b = foot + turn * new Vector3(c.B.X, c.B.Y, c.B.Z) * scale;
            Vector3 along = b - a;
            float length = along.Length();
            if (length < 0.02f) continue;
            along /= length;
            Basis basis = along.Y < -0.999f ? new Basis(Vector3.Right, Mathf.Pi) : new Basis(new Quaternion(Vector3.Up, along));
            into.Add(new FloraSolid(id, kind, true, (a + b) * 0.5f, basis.Orthonormalized(), new Vector3(c.Radius * scale, length * 0.5f, c.Radius * scale), first ? main.Wood : 0, plant.Index));
            first = false;
        }
        if (first) into.Add(main);
        return true;
    }

    /// <summary>
    /// The collider one scatter instance gets (its mesh's foot at <paramref name="foot"/>, scaled and
    /// turned), or false if this kind is never solid or this one is too small (a pebble).
    /// </summary>
    public bool TryMake(Flora kind, long id, Vector3 foot, float scale, float angle, out FloraSolid solid)
    {
        solid = default;
        KindRule rule = _byKind[(int)kind];
        if (rule == null) return false;
        var turn = new Basis(Vector3.Up, angle);
        if (rule.Trunk is TrunkRule t)
        {
            Vector3 axis = t.Axis is { Length: 3 } a ? new Vector3(a[0], a[1], a[2]).Normalized() : Vector3.Up;
            axis = turn * axis;
            float height = t.Height * scale, radius = t.Radius * scale;
            // A basis whose Y is the trunk's axis (the cylinder's own axis), keeping the turn.
            Basis lean = new Basis(new Quaternion(Vector3.Up, axis)) * turn;
            solid = new FloraSolid(id, kind, true, foot + axis * (height * 0.5f), lean.Orthonormalized(), new Vector3(radius, height * 0.5f, radius), rule.Wood);
            return true;
        }
        if (rule.Box is BoxRule b && b.Half is { Length: 3 } h)
        {
            var half = new Vector3(h[0], h[1], h[2]) * scale;
            float top = (b.Cy + h[1]) * scale - Sink; // how far it stands above the ground
            if (top <= MinRockHeight) return false;   // a pebble: walked over
            solid = new FloraSolid(id, kind, false, foot + Vector3.Up * (b.Cy * scale), turn, half, rule.Wood);
            return true;
        }
        return false;
    }
}

/// <summary>
/// One solid scatter instance: a trunk (a cylinder along its basis's Y) or a box, in canonical
/// planet coordinates. Built on the far-ring workers with the flora it belongs to; the streamer
/// turns the near ones into colliders.
/// </summary>
public readonly struct FloraSolid
{
    /// <summary>Its scatter cell, canonical: stable across levels of detail, streaming and reloads.</summary>
    public readonly long Id;
    public readonly Flora Kind;
    public readonly bool IsTrunk;
    public readonly Vector3 Center;
    public readonly Basis Basis;

    /// <summary>Trunks: (radius, half height, radius). Boxes: half extents.</summary>
    public readonly Vector3 Extents;
    public readonly int Wood;

    /// <summary>The species it is in the world's herbarium, or -1 for a rock.</summary>
    public readonly int Plant;

    public FloraSolid(long id, Flora kind, bool trunk, Vector3 center, Basis basis, Vector3 extents, int wood, int plant = -1)
    {
        Id = id;
        Kind = kind;
        IsTrunk = trunk;
        Center = center;
        Basis = basis;
        Extents = extents;
        Wood = wood;
        Plant = plant;
    }

    public bool Fellable => Wood > 0;
    public Transform3D Transform => new(Basis, Center);

    /// <summary>A point genuinely inside it: within a trunk's radius of its axis (and along it), or inside a box.</summary>
    public bool Contains(Vector3 p)
    {
        Vector3 l = Basis.Transposed() * (p - Center);
        if (Mathf.Abs(l.Y) > Extents.Y) return false;
        return IsTrunk ? l.X * l.X + l.Z * l.Z < Extents.X * Extents.X : Mathf.Abs(l.X) < Extents.X && Mathf.Abs(l.Z) < Extents.Z;
    }

    /// <summary>The nearest point beside it, sideways (never over the top), <paramref name="clearance"/> clear of it.</summary>
    public Vector3 Beside(Vector3 p, float clearance)
    {
        Vector3 l = Basis.Transposed() * (p - Center);
        if (IsTrunk)
        {
            var flat = new Vector2(l.X, l.Z);
            flat = flat.LengthSquared() > 1e-8f ? flat.Normalized() : Vector2.Right;
            l = new Vector3(flat.X * (Extents.X + clearance), l.Y, flat.Y * (Extents.Z + clearance));
        }
        else if (Extents.X - Mathf.Abs(l.X) < Extents.Z - Mathf.Abs(l.Z)) l.X = Mathf.Sign(l.X == 0f ? 1f : l.X) * (Extents.X + clearance);
        else l.Z = Mathf.Sign(l.Z == 0f ? 1f : l.Z) * (Extents.Z + clearance);
        return Center + Basis * l;
    }
}
