using System;
using System.Collections.Generic;
using Godot;
using Planets.Gen;
using PlanetSurface.World;
using PoiGenerator.Gen;
using Wildlife;
using Rng = Wildlife.Rng;

namespace Slice.Surface;

/// <summary>
/// Fish: schools of small passive swimmers in the lakes, rivers and seas round you, drifting along
/// in the water between its bed and its surface, darting off when you come at them; none where the
/// water is tainted (a lava sea, fallout, poison, slag). Each world's fish are its own (their size,
/// build and colour from its seed). They are the water's life, not its threat, and nothing hunts
/// them, so they live outside the wild sim: drawn in one batch, cheap however many.
/// </summary>
public partial class Schools : Node3D
{
    public PlanetWorld Planet;
    public Func<Vector3> Eye;
    public ulong Seed;

    /// <summary>Most schools about you at once, how far round you they are kept, and how near you they start, m.</summary>
    const int MostSchools = 6;
    const float Keep = 70f, Drop = 95f, Near = 12f;

    sealed class School
    {
        public Vector3 Center, Drift;
        public float Bed, Surface;
        public int Count;
        public float Radius, Phase;
        public float Startled;
    }

    readonly List<School> _schools = new();
    MultiMeshInstance3D _view;
    float _clock;
    long _rolls;
    float _size;
    Color _colour;

    /// <summary>Schools about you now, and fish in them (tests).</summary>
    public int Count => _schools.Count;
    public int Fish { get { int n = 0; foreach (School s in _schools) n += s.Count; return n; } }

    /// <summary>
    /// Whether a world's water holds fish: water, not lava; not fallout, poison or slag; not what a
    /// mutagen or a chemical war left in it.
    /// </summary>
    public static bool Clean(Archetype arch, WorldConditions w, WorldRoll roll)
    {
        if (arch == Archetype.Lava || w.Vacuum) return false;
        if (w.Radiation >= 2f || w.Mutagen >= 0.5f) return false;
        if (roll != null)
        {
            foreach (string t in roll.Tags) if (t is "slag_rivers" or "stained_streets") return false;
            if (roll.One(LayerKind.Fall)?.Id is "nuclear_war" or "chemical_war" or "poison_air" or "grey_goo" or "reactor_failures" or "industrial_collapse") return false;
        }
        return true;
    }

    /// <summary>A spot for a school in the water near a point (zone frame): its bed and surface, deep enough to swim in; null if none was found.</summary>
    public static (Vector3 At, float Bed, float Surface)? Place(GalHack.Terrain.PlanetTerrainSource source, double width, double height, Vector3 around, float near, float far, Rng rng)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float a = rng.Range(0f, MathF.Tau), r = rng.Range(near, far);
            float x = around.X + MathF.Cos(a) * r, z = around.Z + MathF.Sin(a) * r;
            double wx = Mathx.WrapX(x, width), wz = Mathx.Wrap(z, height);
            if (!source.WaterSurfaceAt(wx, wz, out float surface)) continue;
            float bed = source.SurfaceHeight(wx, wz);
            if (surface - bed < 0.9f) continue;
            return (new Vector3(x, Mathf.Lerp(bed + 0.35f, surface - 0.35f, rng.Range(0.3f, 0.7f)), z), bed, surface);
        }
        return null;
    }

    public override void _Ready()
    {
        var rng = Rng.For(Seed, "fish");
        _size = rng.Range(0.16f, 0.45f);
        _colour = Color.FromHsv(rng.Range(0f, 1f), rng.Range(0.1f, 0.45f), rng.Range(0.45f, 0.8f));
        var mesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = Body(rng), InstanceCount = MostSchools * 26, VisibleInstanceCount = 0 };
        _view = new MultiMeshInstance3D
        {
            Name = "Fish",
            Multimesh = mesh,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = 0.35f, Roughness = 0.35f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_view);
    }

    /// <summary>One fish: a spindle of a body, a forked tail, a dorsal fin; long or deep by the world's own.</summary>
    static Mesh Body(Rng rng)
    {
        float len = 1f, deep = rng.Range(0.22f, 0.4f), wide = deep * rng.Range(0.45f, 0.7f), tail = rng.Range(0.25f, 0.4f);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 nose = new(0f, 0f, -len * 0.5f), back = new(0f, 0f, len * 0.3f);
        var ring = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.Tau / 6f;
            ring[i] = new Vector3(MathF.Cos(a) * wide * 0.5f, MathF.Sin(a) * deep * 0.5f, -len * 0.05f);
        }
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = (b - a).Cross(c - a).Normalized();
            st.SetNormal(n); st.AddVertex(a);
            st.SetNormal(n); st.AddVertex(b);
            st.SetNormal(n); st.AddVertex(c);
        }
        for (int i = 0; i < 6; i++)
        {
            Vector3 a = ring[i], b = ring[(i + 1) % 6];
            Tri(nose, b, a);
            Tri(back, a, b);
        }
        // The tail, forked; the fin on the back.
        Vector3 root = back, up = new(0f, deep * 0.55f, len * 0.3f + tail), down = new(0f, -deep * 0.55f, len * 0.3f + tail), notch = new(0f, 0f, len * 0.3f + tail * 0.55f);
        Tri(root, up, notch); Tri(root, notch, up);
        Tri(root, notch, down); Tri(root, down, notch);
        Vector3 f0 = new(0f, deep * 0.45f, -len * 0.1f), f1 = new(0f, deep * 0.85f, len * 0.05f), f2 = new(0f, deep * 0.4f, len * 0.18f);
        Tri(f0, f1, f2); Tri(f0, f2, f1);
        return st.Commit();
    }

    public override void _Process(double delta)
    {
        if (Planet == null || Eye == null || _view == null) return;
        float dt = (float)delta;
        Vector3 eye = Eye();
        if ((_clock -= dt) <= 0f)
        {
            _clock = 1f;
            for (int i = _schools.Count - 1; i >= 0; i--)
                if (WildSim.Flat(Planet.NearestCopy(_schools[i].Center, eye) - eye).Length() > Drop) _schools.RemoveAt(i);
            var rng = Rng.For(Seed, "schools", ++_rolls);
            if (_schools.Count < MostSchools && Place(Planet.Source, Planet.Width, Planet.Height, eye, Near, Keep, rng) is { } spot)
                _schools.Add(new School
                {
                    Center = spot.At, Bed = spot.Bed, Surface = spot.Surface, Count = rng.Int(8, 24), Radius = rng.Range(0.8f, 2.2f), Phase = rng.Range(0f, 10f),
                    Drift = new Vector3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)).Normalized() * rng.Range(0.3f, 0.8f),
                });
        }
        MultiMesh mm = _view.Multimesh;
        int k = 0;
        foreach (School s in _schools)
        {
            s.Phase += dt;
            // Something big comes at them: they dart off, then settle again.
            Vector3 at = Planet.NearestCopy(s.Center, eye);
            Vector3 away = WildSim.Flat(at - eye);
            if (away.Length() < 5f && s.Startled <= 0f) { s.Startled = 2.5f; s.Drift = away.Normalized() * 2.5f; }
            s.Startled = MathF.Max(0f, s.Startled - dt);
            if (s.Startled <= 0f && s.Drift.Length() > 0.9f) s.Drift *= 0.97f;
            // Along through the water, turning back where it runs shallow.
            Vector3 next = s.Center + s.Drift * dt;
            double wx = Mathx.WrapX(next.X, Planet.Width), wz = Mathx.Wrap(next.Z, Planet.Height);
            if (Planet.Source.WaterSurfaceAt(wx, wz, out float surface) && surface - Planet.GroundHeight(wx, wz) > 0.8f)
            {
                s.Center = next;
                s.Surface = surface;
                s.Bed = Planet.GroundHeight(wx, wz);
            }
            else s.Drift = s.Drift.Rotated(Vector3.Up, 2.2f);
            s.Center.Y = Math.Clamp(s.Center.Y, s.Bed + 0.3f, s.Surface - 0.3f);
            Vector3 heading = s.Drift.LengthSquared() > 1e-4f ? s.Drift.Normalized() : Vector3.Forward;
            for (int i = 0; i < s.Count && k < mm.InstanceCount; i++, k++)
            {
                float a = s.Phase * (0.6f + 0.05f * (i % 5)) + i * 2.399f, r = s.Radius * MathF.Sqrt((i + 0.5f) / s.Count);
                Vector3 p = at + new Vector3(MathF.Cos(a) * r, MathF.Sin(a * 0.7f + i) * 0.25f, MathF.Sin(a) * r);
                p.Y = Math.Clamp(p.Y, s.Bed + 0.2f, s.Surface - 0.15f);
                Vector3 swim = (heading + new Vector3(-MathF.Sin(a), 0f, MathF.Cos(a)) * 0.35f).Normalized();
                float size = _size * (0.8f + 0.4f * ((i * 37 % 11) / 11f));
                var basis = Basis.LookingAt(swim, Vector3.Up).Scaled(Vector3.One * size);
                mm.SetInstanceTransform(k, new Transform3D(basis, p - GlobalPosition));
                mm.SetInstanceColor(k, _colour.Lerp(Colors.White, 0.1f * (i % 3)));
            }
        }
        mm.VisibleInstanceCount = k;
    }
}
