using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GalHack.Deco.Engine;
using PoiGenerator;

namespace Slice.Tests;

/// <summary>
/// What a frame draws without paint near a point, read from the scene tree: a surface with no material (the engine's
/// default white), a plain box (a stand-in), a flat-coloured standard material, faces of the place or deco shaders
/// that carry no finish (drawn flat), unlit glow faces (flat black), and a mesh standing clear of the ground with
/// nothing drawn under it. Staging prints them; the start and place checks count them.
/// </summary>
public static class Unpainted
{
    /// <summary>One fault: what kind, the node (its path says which code made it), its mesh and material, where the faulty vertices lie, how far its foot stands over the ground, how many vertices.</summary>
    public sealed record Fault(string Kind, string Path, string Mesh, string Material, Aabb Box, float Over, int Vertices)
    {
        public override string ToString() => $"{Kind}: {Path} [{Mesh}, {Material}] {Vertices} vertices at {Box.Position.Round()}+{Box.Size.Round()}, foot {Over:0.0} m over the ground";
    }

    /// <summary>A mesh's foot this far over the ground, with nothing drawn under it, is floating, m.</summary>
    const float Clear = 3f;

    /// <summary>Every fault within <paramref name="range"/> of <paramref name="at"/> (horizontally) under <paramref name="root"/>; <paramref name="ground"/> gives the drawn ground's height.</summary>
    public static List<Fault> Near(Node root, Vector3 at, float range, Func<float, float, float> ground)
    {
        var faults = new List<Fault>();
        var drawn = new List<(GeometryInstance3D Node, Aabb Box)>();
        Walk(root, at, range, drawn);
        foreach (var (g, box) in drawn)
        {
            string path = Short(g);
            Mesh mesh = g switch { MeshInstance3D m => m.Mesh, MultiMeshInstance3D mm => mm.Multimesh?.Mesh, _ => null };
            if (mesh == null) continue;
            Vector3 lo = box.Position, hi = box.End;
            float under = MathF.Max(MathF.Max(ground(lo.X, lo.Z), ground(hi.X, lo.Z)), MathF.Max(ground(lo.X, hi.Z), MathF.Max(ground(hi.X, hi.Z), ground((lo.X + hi.X) * 0.5f, (lo.Z + hi.Z) * 0.5f))));
            float over = lo.Y - under;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                Material mat = g.MaterialOverride ?? (g as MeshInstance3D)?.GetSurfaceOverrideMaterial(s) ?? (mesh is PrimitiveMesh pm ? pm.Material : mesh.SurfaceGetMaterial(s));
                string what = Describe(mat);
                if (mat == null) faults.Add(new Fault("no material", path, mesh.GetClass(), what, box, over, 0));
                else if (mesh is PrimitiveMesh) faults.Add(new Fault("plain primitive", path, mesh.GetClass(), what, box, over, 0));
                else if (mat is StandardMaterial3D { AlbedoTexture: null, VertexColorUseAsAlbedo: false, Transparency: BaseMaterial3D.TransparencyEnum.Disabled }) faults.Add(new Fault("flat standard", path, mesh.GetClass(), what, box, over, 0));
                else if (mat is ShaderMaterial { Shader: { } sh } && mesh is ArrayMesh am && g is MeshInstance3D && Flat(sh.Code) is { } flat)
                {
                    var arrays = am.SurfaceGetArrays(s);
                    var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
                    var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                    int n = 0;
                    Aabb where = default;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        if (!flat(colors.Length > i ? colors[i] : Colors.White, uvs.Length > i ? uvs[i] : Vector2.Zero)) continue;
                        Vector3 p = g.GlobalTransform * verts[i];
                        where = n++ == 0 ? new Aabb(p, Vector3.Zero) : where.Expand(p);
                    }
                    if (n > 0) faults.Add(new Fault(sh.Code == DecoShaders.Surface ? "flat deco faces" : sh.Code == PlaceShaders.Surface ? "flat place faces" : "dark glow", path, mesh.GetClass(), what, where, over, n));
                }
            }
            // Floating: its foot well over the ground everywhere under it, and nothing else drawn there to stand on.
            if (over > Clear && !Airborne(path) && !drawn.Any(o => o.Node != g && o.Box.Position.Y < lo.Y - 0.25f && o.Box.End.Y >= lo.Y - 0.5f
                                                                  && o.Box.Position.X < hi.X && o.Box.End.X > lo.X && o.Box.Position.Z < hi.Z && o.Box.End.Z > lo.Z))
                faults.Add(new Fault("floating", path, mesh.GetClass(), Describe(g.MaterialOverride ?? mesh.SurfaceGetMaterial(0)), box, over, 0));
        }
        return faults;
    }

    /// <summary>Prints the faults, a line each, alike ones (same kind, same node path without its numbers) counted together.</summary>
    public static void Print(string title, List<Fault> faults)
    {
        GD.Print($"{title}: {faults.Count} unpainted or floating ({string.Join(", ", faults.GroupBy(f => f.Kind).Select(k => $"{k.Count()} {k.Key}"))})");
        foreach (var k in faults.GroupBy(f => (f.Kind, Pattern(f.Path))).OrderByDescending(k => k.Count()).Take(40))
            GD.Print($"    {k.Count()} x {k.First()}");
    }

    /// <summary>A shader's test for a vertex drawn flat, or null for a shader with no such case.</summary>
    static Func<Color, Vector2, bool> Flat(string code)
    {
        // The place shader: the face's finish code in UV.x; Plain is drawn as its bare colour.
        if (code == PlaceShaders.Surface) return (_, uv) => PoiGenerator.Gen.Surfaces.FinishOf(uv.X) == PoiGenerator.Gen.Finish.Plain;
        // The deco shader: what the part is made of rides in the colour's alpha; 128 and over, or Plain (15), is bare colour.
        if (code == DecoShaders.Surface) return (c, _) => (int)(c.A * 255f + 0.5f) is var k && (k >= 128 || (k & 15) == 15);
        // A glow drawn unlit: a dark colour shows as flat black, whatever the light.
        if (code.Contains("unshaded")) return (c, _) => c.R + c.G + c.B < 0.25f;
        return null;
    }

    /// <summary>Things meant to be off the ground: the sky, weather, creatures, vessels, people, the camera's own.</summary>
    static bool Airborne(string path) => path.Contains("Sky") || path.Contains("Weather") || path.Contains("Cloud") || path.Contains("Wildlife") || path.Contains("Vessel")
                                         || path.Contains("Orbit") || path.Contains("Survivor") || path.Contains("Visor") || path.Contains("Rain") || path.Contains("Bird");

    static string Describe(Material m) => m switch
    {
        null => "null",
        ShaderMaterial { Shader: { } sh } => sh.Code == PlaceShaders.Surface ? "place surface" : sh.Code == DecoShaders.Surface ? "deco surface" : sh.Code.Contains("unshaded") ? "unshaded shader" : $"shader {sh.ResourcePath}",
        StandardMaterial3D s => $"standard {s.AlbedoColor.ToHtml(false)}{(s.AlbedoTexture != null ? " textured" : "")}{(s.VertexColorUseAsAlbedo ? " vertex colour" : "")}",
        _ => m.GetClass(),
    };

    static void Walk(Node n, Vector3 at, float range, List<(GeometryInstance3D, Aabb)> into)
    {
        if (n is Node3D { Visible: false }) return;
        if (n is GeometryInstance3D g && g is MeshInstance3D or MultiMeshInstance3D)
        {
            Aabb box = g.GlobalTransform * g.GetAabb();
            Vector3 c = box.GetCenter();
            float dx = MathF.Max(0f, MathF.Abs(c.X - at.X) - box.Size.X * 0.5f), dz = MathF.Max(0f, MathF.Abs(c.Z - at.Z) - box.Size.Z * 0.5f);
            if (dx * dx + dz * dz < range * range && box.Size.LengthSquared() > 0f) into.Add((g, box));
        }
        foreach (Node child in n.GetChildren()) Walk(child, at, range, into);
    }

    /// <summary>A node's path from the game down (the root's own steps left off).</summary>
    static string Short(Node n)
    {
        string p = n.GetPath().ToString();
        int game = p.IndexOf("/Game/", StringComparison.Ordinal);
        return game >= 0 ? p[(game + 6)..] : p;
    }

    static string Pattern(string path) => new string(path.Select(ch => char.IsDigit(ch) ? '#' : ch).ToArray());
}
