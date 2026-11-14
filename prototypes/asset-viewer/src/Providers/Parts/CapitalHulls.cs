using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// The capital hulls the POI generator composes (CapitalHull): a role gives the bays, a size the beam and holds,
/// the crash the pose and the pieces, the tech era the plating, the kit its livery. One entry for each capital
/// site in the generator's data (sites.txt: the crashed freighter, warship, liner...), every part of it an option.
/// Its pieces are laid out as the plan has them, adrift, built of the player's own large-grid blocks.
/// </summary>
public static class CapitalHulls
{
    // The generator's own vocabulary (CapitalHull's switches); the data's values are added to these.
    static readonly string[] BaseRoles = { "freighter", "warship", "liner", "miner", "carrier" };
    static readonly string[] BaseSizes = { "large", "medium", "huge" };
    static readonly string[] BaseCrashes = { "broken", "nose", "scattered", "long" };
    static readonly string[] Techs = { "mid", "low", "high" };
    public static readonly string[] States = { "As built", "Crash only", "Active", "Abandoned", "Infested", "Ruined" };

    static PoiLibrary _library;
    static PoiLibrary Library => _library ??= PoiCatalog.Load(PartsData.Proto("poi-generator", "data")).Library;

    static string[] Merge(string[] baseValues, IEnumerable<string> data, string first) =>
        new[] { first }.Concat(baseValues).Concat(data).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();

    public static IEnumerable<AssetEntry> Entries()
    {
        PoiLibrary lib = Library;
        var capital = lib.Sites.Values.Where(s => s.Parts.Any(p => p.Arg("what") == "capital")).ToList();
        string[] kits = lib.Kits.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var all = capital.SelectMany(c => c.Parts).Where(p => p.Arg("what") == "capital").ToList();
        foreach (SiteDef site in capital)
        {
            SitePart part = site.Parts.First(p => p.Arg("what") == "capital");
            CapitalHull.Spec spec = CapitalHull.SpecOf(part);
            SiteDef captured = site;
            yield return new AssetEntry
            {
                Id = "hull." + site.Id, Group = "Capital hulls", Name = site.Name + " (" + site.Id + ")", Randomisable = true,
                Options = new[]
                {
                    new AssetOption("Role", Merge(BaseRoles, all.Select(p => p.Arg("role")), spec.Role)) { Rollable = true },
                    new AssetOption("Size", Merge(BaseSizes, all.Select(p => p.Arg("size")), spec.Size)) { Rollable = true },
                    new AssetOption("Crash", Merge(BaseCrashes, all.Select(p => p.Arg("crash")), spec.Crash)) { Rollable = true },
                    new AssetOption("Tech", Merge(Techs, all.Select(p => p.Arg("tech")), part.Arg("tech", "mid"))) { Rollable = true },
                    new AssetOption("Kit", new[] { site.Style }.Concat(kits).Distinct().ToArray()) { Rollable = true },
                    new AssetOption("State", States) { Default = 3, Rollable = true },
                },
                Build = r => Build(lib, captured, r),
            };
        }
    }

    static AssetInstance Build(PoiLibrary lib, SiteDef site, BuildRequest r)
    {
        Catalogue cat = PartsData.Blocks;
        string kit = r.Get("Kit", site.Style);
        var spec = new CapitalHull.Spec(r.Get("Role", "freighter"), r.Get("Size", "large"), r.Get("Crash", "broken"));
        CapitalHull.HullPlan plan = CapitalHull.Plan(lib.Kits[kit], WorldProfile.Parse(r.Get("Tech", "mid")), spec, r.Seed);
        string state = r.Get("State", States[3]);
        var group = new AssetGroup { Name = "Hull" };
        System.Numerics.Vector3 first = plan.Pieces[0].Origin;
        for (int i = 0; i < plan.Pieces.Count; i++)
        {
            CapitalHull.HullPiece piece = plan.Pieces[i];
            HashSet<PoiGenerator.Gen.Int3> gone = state switch
            {
                "As built" => new HashSet<PoiGenerator.Gen.Int3>(),
                "Crash only" => new HashSet<PoiGenerator.Gen.Int3>(piece.Torn),
                _ => piece.Damage(Enum.Parse<PoiState>(state), r.Seed + (ulong)i * 7919UL),
            };
            Structure s = Slice.Places.Hulks.Build(piece.Text(site.Name, gone), cat, site.Name + piece.Name);
            if (s == null) continue;
            var member = new StructureAsset(s, ground: false, centre: false) { Name = "Piece" + i };
            Working.Add(member);
            System.Numerics.Vector3 o = piece.Origin - first;
            System.Numerics.Quaternion q = piece.Rotation;
            group.Add(member, new Transform3D(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)), new Vector3(o.X, o.Y, o.Z)));
        }
        group.Centre(onGround: false);
        group.Stage = new StageSpec { Ground = false, Background = "space", MinDistanceShare = 0.02f };
        group.FactsFn = () => Facts(plan, group, spec, state);
        return group;
    }

    static IEnumerable<(string, string)> Facts(CapitalHull.HullPlan plan, AssetGroup g, CapitalHull.Spec spec, string state)
    {
        yield return ("Role", $"{spec.Role}, {spec.Size}, crash: {spec.Crash}");
        yield return ("Pieces", plan.Pieces.Count.ToString());
        int blocks = g.Members.Sum(m => m.Structure.Count);
        double mass = g.Members.Sum(m => StructureStats.Compute(m.Structure).Mass);
        yield return ("Blocks", blocks.ToString("N0"));
        yield return ("Mass", $"{mass:0} t");
        int thrusters = g.Members.Sum(m => m.Structure.Blocks().Count(b => m.Structure.TypeOf(b.block).IsThruster));
        yield return ("Thrusters", thrusters.ToString());
        yield return ("State", state);
        yield return ("Class", "Capital vessel hull, large grid (no core: a wreck until one is set into it)");
    }
}
