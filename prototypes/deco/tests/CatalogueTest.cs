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
        Run("No two faces fight", TestCoplanar);
        Run("Nothing leans on air", TestLeaning);
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
            Check(p.Unlock != null && p.Unlock.Level >= 1 && p.Unlock.Tab == "deco", $"{p.Id}: a tech-tree node on the deco tab");
        }
        // Roots: one per set with no parent; the rest hang under it.
        foreach (DecoSet s in _cat.Sets)
            Check(_cat.OfSet(s.Id).Count(p => p.Unlock.Parent == null) == 1, $"set '{s.Id}' has one root on the deco tab");
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

    /// <summary>
    /// No model draws two faces of different looks in one plane over each other (they z-fight): every piece,
    /// every state and fall, every cover. A detail laid on a face stands off it (<see cref="DecoEmit.Standoff"/>).
    /// </summary>
    void TestCoplanar()
    {

        int models = 0, bad = 0;
        var by = new Dictionary<string, int>();
        foreach (DecoPiece p in _cat.Pieces)
            foreach (DecoState s in p.States)
                foreach (DecoFall f in FallsOf(p, s))
                    foreach (DecoCover c in s == DecoState.Overgrown ? new[] { DecoCover.Vines, DecoCover.Bloom } : s == DecoState.Buried ? new[] { DecoCover.Snow } : new[] { DecoCover.Vines })
                    {
                        models++;
                        int n = Fights(_forge.Mesh(new DecoLook(p.Id, s, f, true, c, p.Palette)));
                        if (n == 0) continue;
                        bad++;
                        by[$"{p.Id} {s}"] = n;
                    }
        foreach (var g in by.GroupBy(k => k.Key.Split(' ')[1]).OrderByDescending(g => g.Sum(k => k.Value))) Note($"  {g.Sum(k => k.Value),6} pairs in {g.Count()} {g.Key} models");
        foreach (var kv in by.Where(k => !k.Key.EndsWith(" Buried")).OrderByDescending(k => k.Value).Take(12)) Note($"  {kv.Value,4} pairs in {kv.Key}");
        foreach (var kv in Kinds.OrderByDescending(k => k.Value).Take(12)) Note($"  {kv.Value,6} pairs between {kv.Key}");
        // Zero is the aim; parts tangled several deep (vines over trim over panels) still leave a few. This is a
        // ceiling from 105,419 pairs before the standoff rule, to be lowered, never raised.
        Check(by.Values.Sum() <= 1200, $"models hardly draw two faces of different looks in one plane: {by.Values.Sum()} pairs in {bad} of {models} models, at most 1,200");
    }

    /// <summary>How many pairs of a model's faces fight (look: its surface and colour).</summary>
    static readonly Dictionary<string, int> Kinds = new();

    public static int Fights(DecoMeshData m)
    {
        var faces = new List<GalHack.Geometry.Coplanar.Face>();
        for (int si = 0; si < m.Surfaces.Length; si++)
        {
            DecoSurfaceData s = m.Surfaces[si];
            for (int t = 0; t + 2 < s.Positions.Count; t += 3)
                faces.Add(new(s.Positions[t], s.Positions[t + 1], s.Positions[t + 2], s.Normals[t] + s.Normals[t + 1] + s.Normals[t + 2], GalHack.Geometry.Coplanar.Look(si, s.Colors[t]), si));
        }
        var pairs = GalHack.Geometry.Coplanar.Find(faces);
        foreach (var p in pairs) { string k = $"{(DecoSurface)faces[p.P].Source}/{(DecoSurface)faces[p.Q].Source}"; Kinds[k] = Kinds.GetValueOrDefault(k) + 1; }
        return pairs.Count;
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
        // A detail flush with the footprint's face stands one standoff proud of it (DecoStandoff).
        const float tol = 0.002f + GalHack.Geometry.Coplanar.Standoff;
        foreach (DecoPiece p in _cat.Pieces)
        {
            var lo = new Vector3(-p.Width / 2 - tol, -tol, -p.Depth / 2 - tol);
            var hi = new Vector3(p.Width / 2 + tol, p.Height + tol, p.Depth / 2 + tol);
            foreach (DecoState s in p.States.Where(s => s is not (DecoState.Knocked or DecoState.Open or DecoState.Buried)))
            {
                DecoMeshData m = _forge.Mesh(p.Id, s);
                bool fits = m.Min.X >= lo.X && m.Min.Y >= lo.Y && m.Min.Z >= lo.Z && m.Max.X <= hi.X && m.Max.Y <= hi.Y && m.Max.Z <= hi.Z;
                Check(fits, $"{p.Id} {s}: fits {p.SizeX}x{p.SizeY}x{p.SizeZ} half metres ({p.Width}x{p.Height}x{p.Depth} m): mesh {m.Min} .. {m.Max}");
                if (s == DecoState.Intact && p.Mount is DecoMount.Floor or DecoMount.Top)
                    Check(MathF.Abs(m.Min.Y) < 0.002f, $"{p.Id}: stands on the floor (lowest point {m.Min.Y:0.000} m)");
            }
            var (cx, cy, cz) = p.Cells;
            Check(cx >= p.Width && cy >= p.Height && cz >= p.Depth && cx < p.Width + 1 && cy < p.Height + 1 && cz < p.Depth + 1, $"{p.Id}: fits {cx}x{cy}x{cz} 1 m cells, rounded up");
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
                if (p.Mount == DecoMount.Wall)
                {
                    // Off its wall: on the floor and against the wall's face (local +z), touching both, cutting neither.
                    float wall = p.Depth / 2;
                    Check(MathF.Abs(shape.Max.Z - wall) < 0.002f && m.Max.Z < wall + 0.002f + GalHack.Geometry.Coplanar.Standoff,
                        $"{p.Id} fallen {f}: lies or leans against its wall's face and not into it (back {shape.Max.Z:0.000}, mesh {m.Max.Z:0.000}, wall {wall:0.000})");
                    continue;
                }
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

    /// <summary>
    /// Nothing leans on air: every part of a model that stands tilted (10 to 80 degrees off upright) on
    /// a foot touches a support well away from that foot: the next book, the case's side, a wall, a
    /// bench, a chunk it is propped on. A hard part set into another (a handle in its socket, horns in
    /// a skull) is fixed there; a soft one (a book, cloth) never is. The owner's case, a book turned
    /// over on a shelf with nothing beside it, must be caught.
    /// </summary>
    void TestLeaning()
    {
        // The owner's case: books on a shelf, the last tipped over on its foot with nothing beside it.
        var air = new DecoBuilder(1, 1, 0.4f, 1);
        air.Box(-0.5f, 0, -0.2f, 0.5f, 0.03f, 0.2f, DecoSlot.Wood);
        air.Box(-0.5f, 0.03f, -0.18f, -0.46f, 0.3f, 0.18f, DecoSlot.Fabric).Soft();
        using (air.Turn(new Vector3(-0.3f, 0.03f, 0), 0, 0, -35))
            air.Box(-0.3f, 0.03f, -0.18f, -0.26f, 0.3f, 0.18f, DecoSlot.Fabric).Soft();
        var caught = LeansOnAir(air.Model.Parts);
        Check(caught.Count == 1, $"a book tipped over on its foot with nothing beside it is caught ({caught.Count}: {string.Join("; ", caught)})");

        int tilted = 0, failed = 0;
        // Every model as built, then shelves of books filled afresh many times over (a fresh roll each run).
        var models = _cat.Pieces.Select(p => (p.Id, _forge.Model(p.Id))).ToList();
        DecoPiece shelf = _cat.Piece("bookshelf");
        for (int i = 0; i < 300; i++)
        {
            var builder = new DecoBuilder(shelf.Width, shelf.Height, shelf.Depth, (ulong)Random.Shared.NextInt64());
            DecoModels.All[shelf.Id](builder);
            models.Add(($"{shelf.Id} (filled afresh)", builder.Model));
        }
        foreach (var (id, model) in models)
            foreach (string problem in LeansOnAir(model.Parts, n => tilted += n))
                if (failed++ < 20) Check(false, $"{id}: {problem}");
        Check(failed == 0, $"nothing leans on air ({failed} parts do)");
        Note($"{tilted} tilted parts checked in {models.Count} models");
    }

    /// <summary>The tilted parts among <paramref name="parts"/> that stand on a foot with nothing holding them up.</summary>
    static List<string> LeansOnAir(IList<DecoPart> parts, Action<int> counted = null)
    {
        var problems = new List<string>();
        int tilted = 0;
        foreach (DecoPart part in parts)
        {
            if (part.Kind is DecoPrim.Sphere or DecoPrim.Dome) continue;
            Vector3 up = Vector3.Normalize(new Vector3(part.M.M21, part.M.M22, part.M.M23));
            float tilt = MathF.Acos(Math.Clamp(MathF.Abs(up.Y), 0f, 1f)) / DecoGeo.Deg;
            if (!float.IsFinite(tilt) || tilt < 10 || tilt > 80) continue;
            List<Vector3> pts = SurfacePoints(part);
            var others = parts.Where(o => o != part).Select(o => Matrix4x4.Invert(o.M, out Matrix4x4 inv) ? (inv, Scale(o)) : default).Where(o => o.Item2 != default).ToList();
            bool Within(Vector3 v, float tol) => others.Any(o => InBox(v, o.Item1, o.Item2, tol));
            // A hard part set into another is fixed there, not leaning; a soft one (a book, cloth) is never fixed.
            if (!part.Is(PartFlags.Soft) && pts.Any(v => Within(v, -0.004f))) continue;
            tilted++;
            float lo = pts.Min(v => v.Y);
            bool Touches(Vector3 v) => v.Y < 0.01f || Within(v, 0.008f);
            bool foot = pts.Any(v => v.Y < lo + 0.01f && Touches(v));
            if (!foot) continue;
            // Held: touching something well away from its foot, not beside it. Distances are taken across the
            // line it tips about (along that line a support is only beside the foot).
            Vector3 low = pts.OrderBy(v => v.Y).First(), hinge = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, up));
            float Off(Vector3 v) { Vector3 d = v - low; return (d - Vector3.Dot(d, hinge) * hinge).Length(); }
            float reach = pts.Max(Off);
            bool Far(Vector3 v) => Off(v) > reach * 0.4f;
            bool held = pts.Any(v => Far(v) && Touches(v));
            // Or another part's edge against this one's face (a panel resting on a chunk's top edge).
            if (!held && Matrix4x4.Invert(part.M, out Matrix4x4 pinv))
            {
                Vector3 ps = Scale(part);
                held = parts.Where(o => o != part && o.Kind == DecoPrim.Hex).SelectMany(SurfacePoints).Any(v => Far(v) && InBox(v, pinv, ps, 0.008f));
            }
            if (!held) problems.Add($"a {part.Slot} part tilted {tilt:F0} degrees stands on its foot with nothing holding it");
        }
        counted?.Invoke(tilted);
        return problems;
    }

    static Vector3 Scale(DecoPart p) => new(new Vector3(p.M.M11, p.M.M12, p.M.M13).Length(), new Vector3(p.M.M21, p.M.M22, p.M.M23).Length(), new Vector3(p.M.M31, p.M.M32, p.M.M33).Length());

    /// <summary>Whether a point is inside a part's box (given its inverse and scale), grown by <paramref name="tol"/> metres (shrunk if negative).</summary>
    static bool InBox(Vector3 v, Matrix4x4 inv, Vector3 s, float tol)
    {
        Vector3 q = Vector3.Transform(v, inv);
        return MathF.Abs(q.X) <= 0.5f + tol / MathF.Max(s.X, 1e-4f) && MathF.Abs(q.Y) <= 0.5f + tol / MathF.Max(s.Y, 1e-4f) && MathF.Abs(q.Z) <= 0.5f + tol / MathF.Max(s.Z, 1e-4f);
    }

    /// <summary>A part's corners and points along its edges (a contact along a face shows at its edges).</summary>
    static List<Vector3> SurfacePoints(DecoPart p)
    {
        var pts = new List<Vector3>(DecoEmit.Vertices(p));
        if (p.Kind != DecoPrim.Hex) return pts;
        for (int i = 0; i < 8; i++)
            for (int axis = 0; axis < 3; axis++)
            {
                if ((i & (1 << axis)) != 0) continue;
                Vector3 a = p.Corners[i], c = p.Corners[i | (1 << axis)];
                // Every centimetre or so: a support may touch a leaning edge at a single point.
                Vector3 wa = Vector3.Transform(a, p.M), wc = Vector3.Transform(c, p.M);
                int n = Math.Clamp((int)(Vector3.Distance(wa, wc) / 0.008f), 4, 400);
                for (int k = 1; k < n; k++) pts.Add(Vector3.Lerp(wa, wc, k / (float)n));
            }
        // And a grid over each face of a box: a support may touch the middle of a face (a panel propped on a chunk).
        if (!ReferenceEquals(p.Corners, DecoPart.UnitCube)) return pts;
        for (int axis = 0; axis < 3; axis++)
            foreach (float side in new[] { -0.5f, 0.5f })
                for (int i = 1; i < 8; i++)
                    for (int j = 1; j < 8; j++)
                    {
                        float u = i / 8f - 0.5f, v = j / 8f - 0.5f;
                        var q = axis == 0 ? new Vector3(side, u, v) : axis == 1 ? new Vector3(u, side, v) : new Vector3(u, v, side);
                        pts.Add(Vector3.Transform(q, p.M));
                    }
        return pts;
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
