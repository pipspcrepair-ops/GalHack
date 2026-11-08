using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;

namespace DecoShowroom.Tests;

/// <summary>
/// Headless test of the decorations catalogue and its meshes: every entry loads, has a mesh for
/// every state it claims, has collision, fits its declared grid size, and its knocked-over states
/// rest on the floor; every cable is attached. Run with:
///   godot --headless --path prototypes/deco res://tests/CatalogueTest.tscn
/// </summary>
public partial class CatalogueTest : TestBase
{
    DecoCatalogue _cat;
    DecoForge _forge;

    protected override void RunAll()
    {
        Run("Catalogue loads and keeps its rules", TestCatalogue);
        Run("Sets: breadth and counts", TestSets);
        Run("Tech levels and salvage", TestTechAndSalvage);
        Run("Every state has a mesh", TestMeshes);
        Run("Collision", TestCollision);
        Run("Fits its declared grid size", TestFit);
        Run("Knocked over rests on the floor", TestKnocked);
        Run("Every cable is attached", TestCables);
        Run("Godot meshes and shapes", TestGodot);
        Run("Every part has a surface", TestSurfaces);
        Run("Names are GalHack's own", TestNames);
    }

    void TestCatalogue()
    {
        _cat = DataPaths.Deco;
        _forge = new DecoForge(_cat);
        ItemCatalogue items = DataPaths.ItemCatalogue;
        List<string> problems = _cat.Validate(items);
        foreach (string p in problems.Take(20)) Note("rule broken: " + p);
        Check(problems.Count == 0, $"the deco catalogue keeps every rule ({problems.Count} broken)");
        Note($"{_cat.Pieces.Count} pieces in {_cat.Sets.Count} sets");
        Check(_cat.Pieces.Count >= 120, $"120 or more pieces ({_cat.Pieces.Count})");
        Check(_cat.Sets.Count >= 15, $"15 or more theme sets ({_cat.Sets.Count})");
        foreach (DecoPiece p in _cat.Pieces)
        {
            if (p.OnLarge) Check(p.UnlockLarge != null && p.UnlockLarge.Level >= 1 && p.UnlockLarge.Tab == "deco", $"{p.Id}: a large-grid tech-tree node on the deco tab");
            if (p.OnSmall) Check(p.UnlockSmall != null, $"{p.Id}: a small-grid tech-tree node");
        }
        // Roots: one per set with no parent; the rest hang under it.
        foreach (DecoSet s in _cat.Sets)
            Check(_cat.OfSet(s.Id).Count(p => p.UnlockLarge?.Parent == null && p.OnLarge) == 1, $"set '{s.Id}' has one root on the deco tab");
        // A broken file is refused at load.
        string json = File.ReadAllText(Path.Combine(DataPaths.Items, DecoCatalogue.FileName));
        bool Refused(string text) { try { DecoCatalogue.Load(text); return false; } catch (InvalidDataException) { return true; } }
        Check(Refused(json.Replace("\"set\": \"office\"", "\"set\": \"nowhere\"")), "a piece in an unknown set is refused");
        Check(Refused(json.Replace("\"size\": [3, 2, 2]", "\"size\": [3, 0, 2]")), "a zero size is refused");
        Check(Refused(json.Replace("\"bill\": { \"iron-bar\": 1 }", "\"bill\": { \"iron-bar\": 0 }")), "a zero count in a bill is refused");
        // Integration: the bills slot into the shared block bills without clashing.
        DecoCatalogue fresh = DecoCatalogue.Load(json);
        ItemCatalogue items2 = ItemCatalogue.LoadDirectory(DataPaths.Items);
        int before = items2.Blocks.Count;
        fresh.AddBillsTo(items2);
        Check(items2.Blocks.Count == before + fresh.Pieces.Count, $"every deco piece adds a block bill ({before} + {fresh.Pieces.Count})");
    }

    void TestSets()
    {
        foreach (DecoSet s in _cat.Sets)
        {
            int own = _cat.OfSet(s.Id).Count(), all = _cat.InSet(s.Id).Count();
            Note($"{s.Id,-12} {own,3} own, {all,3} with borrowed  ({string.Join(", ", _cat.OfSet(s.Id).Select(p => p.Id))})");
            Check(own >= 6, $"set '{s.Id}' has at least 6 pieces of its own ({own})");
        }
        foreach (DecoPiece p in _cat.Pieces) Check(DecoForge.HasBuilder(p.Id), $"{p.Id} has a model builder");
    }

    void TestTechAndSalvage()
    {
        foreach (DecoTech t in Enum.GetValues<DecoTech>())
            Note($"{t}: {_cat.Pieces.Count(p => p.Tech == t)} pieces, vehicles {string.Join(", ", _cat.Pieces.Where(p => p.Tech == t && p.Has("vehicle")).Select(p => p.Id))}");
        foreach (DecoTech t in Enum.GetValues<DecoTech>())
            Check(_cat.Pieces.Count(p => p.Tech == t && p.Has("vehicle")) >= 3, $"tech level {t} has its own vehicles");
        foreach (DecoPiece p in _cat.Pieces.Where(p => p.Has("vehicle")))
            Check(p.Salvage != null, $"{p.Id} is a vehicle marked salvageable");
        foreach (string c in DecoCatalogue.SalvageClasses)
            Check(_cat.SalvageYield(c).Total > 0, $"salvage class '{c}' yields scrap");
    }

    static IEnumerable<DecoFall> FallsOf(DecoPiece p, DecoState s) => s == DecoState.Knocked ? p.Falls : new[] { DecoFall.None };

    void TestMeshes()
    {
        int meshes = 0, tris = 0;
        foreach (DecoPiece p in _cat.Pieces)
        {
            DecoMeshData intact = _forge.Mesh(p.Id);
            foreach (DecoState s in p.States)
                foreach (DecoFall f in FallsOf(p, s))
                {
                    DecoMeshData m = _forge.Mesh(p.Id, s, f);
                    meshes++;
                    tris += m.Triangles;
                    Check(m.Triangles > 0, $"{p.Id} {s} {f}: has triangles");
                    if (s != DecoState.Intact)
                        Check(Math.Abs(Signature(m) - Signature(intact)) > 1e-3, $"{p.Id} {s} {f}: looks different from intact");
                }
            foreach (DecoCover c in new[] { DecoCover.Bloom })
                if (p.Has(DecoState.Overgrown)) Check(_forge.Mesh(p.Id, DecoState.Overgrown, cover: c).Triangles > 0, $"{p.Id}: Bloom growth");
            if (p.Has(DecoState.Buried)) Check(_forge.Mesh(p.Id, DecoState.Buried, cover: DecoCover.Snow).Triangles > 0, $"{p.Id}: buried in snow");
        }
        Note($"{meshes} meshes, {tris:N0} triangles ({tris / Math.Max(1, meshes)} per mesh)");
    }

    /// <summary>A number that changes when any vertex moves or any colour changes.</summary>
    static double Signature(DecoMeshData m)
    {
        double sum = 0;
        foreach (DecoSurfaceData s in m.Surfaces)
            for (int i = 0; i < s.Positions.Count; i++)
                sum += s.Positions[i].X * 1.3 + s.Positions[i].Y * 2.7 + s.Positions[i].Z * 3.1 + s.Colors[i].X * 0.7 + s.Colors[i].Y * 0.9;
        return sum;
    }

    void TestCollision()
    {
        foreach (DecoPiece p in _cat.Pieces)
            foreach (DecoState s in p.States)
                foreach (DecoFall f in FallsOf(p, s))
                {
                    DecoMeshData m = _forge.Mesh(p.Id, s, f);
                    Check(m.Boxes.Count >= 1, $"{p.Id} {s} {f}: has collision");
                    if (s != DecoState.Intact) continue;
                    Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
                    foreach (DecoBox b in m.Boxes) foreach (Vector3 c in b.Corners()) { lo = Vector3.Min(lo, c); hi = Vector3.Max(hi, c); }
                    Vector3 size = m.Max - m.Min, cover = Vector3.Min(hi, m.Max) - Vector3.Max(lo, m.Min);
                    // The two biggest dimensions must be mostly covered (thin details may go without).
                    float[] share = { cover.X / size.X, cover.Y / size.Y, cover.Z / size.Z };
                    float[] dims = { size.X, size.Y, size.Z };
                    int small = Array.IndexOf(dims, dims.Min());
                    bool covers = share[0] >= 0.6f && share[2] >= 0.6f && share[1] >= 0.4f || Enumerable.Range(0, 3).Where(i => i != small).All(i => share[i] >= 0.6f);
                    float tol = MathF.Max(0.1f, 0.04f * MathF.Max(size.X, MathF.Max(size.Y, size.Z)));
                    bool inside = lo.X >= m.Min.X - tol && lo.Y >= m.Min.Y - tol && lo.Z >= m.Min.Z - tol && hi.X <= m.Max.X + tol && hi.Y <= m.Max.Y + tol && hi.Z <= m.Max.Z + tol;
                    Check(covers && inside, $"{p.Id}: collision boxes cover the piece (covers {cover}, of {size}; inside {inside})");
                }
    }

    void TestFit()
    {
        const float tol = 0.002f;
        foreach (DecoPiece p in _cat.Pieces)
        {
            var lo = new Vector3(-p.Width / 2 - tol, -tol, -p.Depth / 2 - tol);
            var hi = new Vector3(p.Width / 2 + tol, p.Height + tol, p.Depth / 2 + tol);
            foreach (DecoState s in p.States.Where(s => s is not (DecoState.Knocked or DecoState.Open or DecoState.Buried)))
            {
                DecoMeshData m = _forge.Mesh(p.Id, s);
                bool fits = m.Min.X >= lo.X && m.Min.Y >= lo.Y && m.Min.Z >= lo.Z && m.Max.X <= hi.X && m.Max.Y <= hi.Y && m.Max.Z <= hi.Z;
                Check(fits, $"{p.Id} {s}: fits {p.SizeX}x{p.SizeY}x{p.SizeZ} small cells ({p.Width}x{p.Height}x{p.Depth} m): mesh {m.Min} .. {m.Max}");
                if (s == DecoState.Intact && p.Mount is DecoMount.Floor or DecoMount.Top)
                    Check(MathF.Abs(m.Min.Y) < 0.002f, $"{p.Id}: stands on the floor (lowest point {m.Min.Y:0.000} m)");
            }
            var (lx, ly, lz) = p.LargeCells;
            Check(lx * DecoPiece.LargeCell >= p.Width && ly * DecoPiece.LargeCell >= p.Height && lz * DecoPiece.LargeCell >= p.Depth, $"{p.Id}: fits {lx}x{ly}x{lz} large cells");
        }
    }

    void TestKnocked()
    {
        int n = 0;
        foreach (DecoPiece p in _cat.Pieces.Where(p => p.Has(DecoState.Knocked)))
            foreach (DecoFall f in p.Falls)
            {
                DecoForge.Shape shape = _forge.ShapeOf(p.Id, DecoState.Knocked, f);
                DecoMeshData m = _forge.Mesh(p.Id, DecoState.Knocked, f);
                n++;
                Check(MathF.Abs(shape.Min.Y) < 0.001f, $"{p.Id} fallen {f}: touches the floor (lowest {shape.Min.Y:0.0000})");
                Check(m.Min.Y > -0.001f, $"{p.Id} fallen {f}: doesn't sink into it (mesh lowest {m.Min.Y:0.0000})");
                Check(shape.Stable, $"{p.Id} fallen {f}: rests stably (its centre of mass over what touches the floor)");
                // Independently: the centre of mass lies over the hull of the corners on the floor.
                var contacts = shape.Model.Parts.SelectMany(DecoEmit.Vertices).Where(v => v.Y < 0.003f).Select(v => new Vector2(v.X, v.Z)).ToList();
                var hull = DecoGeo.Hull(contacts);
                DecoGeo.ClosestOnHull(hull, new Vector2(shape.CentreOfMass.X, shape.CentreOfMass.Z), out bool inside, -0.005f);
                Check(hull.Count >= 3 && inside, $"{p.Id} fallen {f}: {contacts.Count} floor contacts hold its centre of mass");
            }
        Note($"{n} knocked-over poses checked");
    }

    void TestCables()
    {
        int cables = 0;
        foreach (DecoPiece p in _cat.Pieces)
        {
            Check(_forge.ShapeOf(p.Id, DecoState.Intact, DecoFall.None).Repaired == 0, $"{p.Id}: every cable its builder strung is anchored at both ends (or hangs to the floor)");
            foreach (DecoState s in p.States)
                foreach (DecoFall f in FallsOf(p, s))
                {
                    DecoForge.Shape shape = _forge.ShapeOf(p.Id, s, f);
                    cables += shape.Model.Cables.Count;
                    List<string> loose = DecoForge.LooseCables(shape);
                    foreach (string l in loose.Take(2)) Note($"{p.Id} {s} {f}: {l}");
                    Check(loose.Count == 0, $"{p.Id} {s} {f}: no cable ends in mid-air");
                }
        }
        // The helper itself: a span sags below its ends and a drop reaches the ground.
        var span = DecoCables.Span(new Vector3(0, 5, 0), new Vector3(10, 4, 0), 0.03f);
        Check(span[0] == new Vector3(0, 5, 0) && Vector3.Distance(span[^1], new Vector3(10, 4, 0)) < 1e-4f && span.Min(v => v.Y) < 3.9f, "a span joins its anchors and sags between them");
        var drop = DecoCables.Drop(new Vector3(1, 3, 1), 0, new Vector3(1, 0, 0), 0.5f, 0.01f);
        Check(MathF.Abs(drop[^1].Y - 0.01f) < 1e-4f && drop[1].X == 1 && drop[1].Z == 1, "a drop falls straight down and lies on the ground");
        Note($"{cables} cables checked");
    }

    void TestGodot()
    {
        var cache = new DecoMeshCache(_forge);
        int surfaces = 0, shapes = 0;
        foreach (DecoPiece p in _cat.Pieces)
            foreach (DecoState s in p.States)
            {
                var look = new DecoLook(p.Id, s, s == DecoState.Knocked ? p.Falls[0] : DecoFall.None, true, DecoCover.Vines, p.Palette);
                var mesh = cache.Mesh(look);
                surfaces += mesh.GetSurfaceCount();
                shapes += cache.Shapes(look).Count;
                Check(mesh.GetSurfaceCount() > 0, $"{p.Id} {s}: an ArrayMesh with surfaces");
            }
        Note($"{cache.Count} Godot meshes, {surfaces} surfaces, {shapes} collision boxes");
    }

    /// <summary>
    /// What each part is made of reaches the shader: every part of every model resolves to a finish,
    /// every triangle of a piece carries it and its wear, every vertex lies on its face, and no colour is
    /// missing from a palette. Notes the finishes in use and the pieces still mostly plain paint or plastic
    /// in a grey or white (the audit's list).
    /// </summary>
    void TestSurfaces()
    {
        var census = new Dictionary<DecoFinish, int>();
        var greyish = new List<string>();
        int checkedTris = 0;
        foreach (DecoPiece p in _cat.Pieces)
        {
            foreach (DecoPart part in _forge.Model(p.Id).Parts)
            {
                DecoFinish f = DecoFinishes.Of(part);
                census[f] = census.GetValueOrDefault(f) + 1;
                Check(f != DecoFinish.Auto, $"{p.Id}: a {part.Slot} part resolves to a finish");
                if (!DecoPalette.Glows(part.Slot)) Check(f != DecoFinish.Plain, $"{p.Id}: a {part.Slot} part has a surface, not plain");
            }
            foreach (var (state, age, exposed) in new[] { (DecoState.Intact, DecoAge.Fresh, false), (DecoState.Intact, DecoAge.OldWorld, p.Outdoor), (p.Has(DecoState.Rusted) ? DecoState.Rusted : DecoState.Broken, DecoAge.Fresh, false) })
            {
                if (!p.Has(state)) continue;
                DecoMeshData m = _forge.Mesh(new DecoLook(p.Id, state, DecoFall.None, true, DecoCover.Vines, p.Palette, false, age, exposed));
                int wear = DecoFinishes.WearOf(state, age, exposed, p.Outdoor);
                Check(m.Wear == wear, $"{p.Id} {state} {age}: worn {wear}");
                int uncoded = 0, offFace = 0, wrongWear = 0, magenta = 0;
                for (int si = 0; si < m.Surfaces.Length; si++)
                {
                    DecoSurfaceData s = m.Surfaces[si];
                    Check(s.Faces.Count == s.Positions.Count, $"{p.Id} {state} {(DecoSurface)si}: a face for every vertex");
                    for (int i = 0; i < s.Positions.Count; i++)
                    {
                        Vector4 c = s.Colors[i];
                        if (MathF.Abs(c.X - 1f) < 0.01f && c.Y < 0.01f && MathF.Abs(c.Z - 1f) < 0.01f) magenta++;
                        Vector4 face = s.FaceAt(i);
                        if (face.Z > 0 && (face.X < -1e-3f || face.X > face.Z + 1e-3f)) offFace++;
                        if (face.W > 0 && (face.Y < -1e-3f || face.Y > face.W + 1e-3f)) offFace++;
                        if (face.Z < 0 && face.W == 0 && new Vector2(face.X, face.Y).Length() > -face.Z + 1e-3f) offFace++;
                        if ((DecoSurface)si is not (DecoSurface.Matte or DecoSurface.Metal) || i % 3 != 0) continue;
                        checkedTris++;
                        var code = DecoFinishes.Decode(c.W);
                        if (code == null) uncoded++;
                        else if (code.Value.Wear != wear) wrongWear++;
                    }
                }
                // Marks laid over a piece (a cracked screen's cracks, stains, shards) are drawn plain: none on a piece in use.
                if (state == DecoState.Intact && age == DecoAge.Fresh) Check(uncoded == 0, $"{p.Id} {age}: every triangle carries its finish ({uncoded} do not)");
                Check(offFace == 0, $"{p.Id} {state} {age}: every vertex lies on its face ({offFace} do not)");
                Check(wrongWear == 0, $"{p.Id} {state} {age}: every triangle carries the piece's wear ({wrongWear} do not)");
                Check(magenta == 0, $"{p.Id} {state} {age}: no colour missing from its palette");
            }
            // The audit: a piece whose opaque area is mostly plain paint or plastic in a grey or white.
            DecoMeshData intact = _forge.Mesh(p.Id);
            double plain = 0, all = 0;
            for (int si = 0; si < 2; si++)
            {
                DecoSurfaceData s = intact.Surfaces[si];
                for (int i = 0; i + 2 < s.Positions.Count; i += 3)
                {
                    double area = Vector3.Cross(s.Positions[i + 1] - s.Positions[i], s.Positions[i + 2] - s.Positions[i]).Length() * 0.5;
                    all += area;
                    Vector4 c = s.Colors[i];
                    float hi = MathF.Max(c.X, MathF.Max(c.Y, c.Z)), lo = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
                    var code = DecoFinishes.Decode(c.W);
                    if (code is { Finish: DecoFinish.Paint or DecoFinish.Plastic } && hi - lo < 0.08f && hi > 0.55f) plain += area;
                }
            }
            if (all > 0 && plain / all > 0.6) greyish.Add(p.Id);
        }
        Note($"{checkedTris:N0} triangles checked; finishes in use: {string.Join(", ", census.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
        Note($"mostly plain grey or white paint or plastic ({greyish.Count}): {string.Join(", ", greyish)}");
        var bare = _cat.Pieces.Where(p => _forge.Model(p.Id).Parts.Count < 4).Select(p => p.Id).ToList();
        Note($"pieces of under four parts ({bare.Count}): {string.Join(", ", bare)}");
        // Glass alpha and finish codes survive the trip into Godot's 8-bit vertex colours.
        foreach (DecoFinish f in Enum.GetValues<DecoFinish>())
            for (int w = 0; w < 4; w++)
                foreach (bool flag in new[] { false, true })
                {
                    float a = MathF.Round(DecoFinishes.Code(f, w, flag) * 255f) / 255f;
                    Check(DecoFinishes.Decode(a) == (f, w, flag), $"{f} worn {w} flag {flag} survives 8-bit colour");
                }
        Check(DecoFinishes.Decode(1f) == null, "an alpha of 1 (other code's triangles) reads as no finish");
    }

    void TestNames()
    {
        // Ideas from Empyrion only: none of its deco block names or faction names.
        string[] borrowed = { "scifi", "zirax", "talon", "polaris", "kriel", "pentaxid", "epsilon", "homesweethome", "esd_", "plb" };
        foreach (DecoPiece p in _cat.Pieces)
            Check(!borrowed.Any(w => p.Id.Contains(w) || p.Name.ToLowerInvariant().Replace(" ", "").Contains(w)), $"{p.Id} '{p.Name}' is GalHack's own name");
        Check(_cat.Pieces.Select(p => p.Name).Distinct().Count() == _cat.Pieces.Count, "every piece has its own name");
    }
}
