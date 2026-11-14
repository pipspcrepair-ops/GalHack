using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using GalHack.Items.Deposits;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// Ore deposits as the game makes them: the surface tell the planet shows over a deposit (an outcrop,
/// a fracture or scree, from shared/Deco's ore tells, coloured by the planet generator's deposit look),
/// and the body under the ground (shared/Items' deposit grower), drawn as its dug cells by grade.
/// One entry per ore in deposits.json.
/// </summary>
public static class Deposits
{
    public const string ViewTell = "Surface tell", ViewBody = "Underground body";
    static readonly string[] Tells = Enum.GetNames<DecoTellKind>();
    static readonly string[] Shapes = Enum.GetNames<DepositShape>();
    static readonly string[] Amounts = { "Small 0.6", "Normal 1.0", "Large 1.6" };
    static readonly float[] AmountValue = { 0.6f, 1f, 1.6f };
    static readonly string[] Radii = { "Small 1.5 m", "Medium 2.5 m", "Large 4 m" };
    static readonly float[] RadiusValue = { 1.5f, 2.5f, 4f };

    public static IEnumerable<AssetEntry> Entries(ItemCatalogue items)
    {
        DepositRules rules = DepositRules.LoadDirectory(PartsData.Shared("Items", "data"));
        foreach (string ore in rules.Ores)
        {
            if (!items.TryGet(ore, out ItemDef item)) continue;
            string captured = ore;
            yield return new AssetEntry
            {
                Id = "deposit." + ore, Group = "Deposits", Name = item.Name + " deposit", Randomisable = true,
                Options = new[]
                {
                    new AssetOption("View", ViewTell, ViewBody),
                    new AssetOption("Tell", Tells) { Rollable = true },
                    new AssetOption("Size", Radii) { Default = 1, Rollable = true },
                    new AssetOption("Body", rules.ShapesFor(ore).Select(s => s.Shape.ToString()).Distinct().ToArray()) { Rollable = true },
                    new AssetOption("Amount", Amounts) { Default = 1, Rollable = true },
                },
                Build = r => Build(items, rules, captured, r),
            };
        }
    }

    static AssetInstance Build(ItemCatalogue items, DepositRules rules, string ore, BuildRequest r)
    {
        ItemDef item = items.Item(ore);
        bool known = Enum.TryParse(item.DepositKind, out DepositKind kind);
        var asset = new ModelAsset { Name = ore };
        Rgb c = DepositLook.Color(kind);
        var oreColour = new DecoRgb(c.R, c.G, c.B);
        string view = r.Get("View", ViewTell);
        float radius = RadiusValue[r.IndexOf("Size", Radii)];
        DecoTellKind tell = Enum.TryParse(r.Get("Tell"), out DecoTellKind t) ? t : DecoTellKind.Outcrop;
        bool glints = kind is DepositKind.Iron or DepositKind.Copper or DepositKind.Magnesium or DepositKind.Cobalt or DepositKind.Neodymium or DepositKind.Gold or DepositKind.Gravium or DepositKind.Crystal or DepositKind.Silicon;
        var rock = new DecoRgb(0.43f, 0.4f, 0.36f);

        if (view == ViewTell)
        {
            DecoMeshData mesh = DecoOreTells.Build(tell, oreColour, DepositLook.Glow(kind) * 0.3f, glints, r.Seed, radius * (tell == DecoTellKind.Fracture ? 0.5f : 0.7f), rock);
            asset.AddChild(new MeshInstance3D { Name = "Tell", Mesh = DecoGodot.ToMesh(mesh) });
            asset.Stage = new StageSpec { Ground = true };
            asset.FactsFn = () => new[]
            {
                ("Ore", item.Name), ("Tell", tell.ToString()), ("Deposit kind", known ? kind.ToString() : item.DepositKind ?? "none"),
                ("Seen on", tell switch { DecoTellKind.Outcrop => "deposits at the surface", DecoTellKind.Scree => "deposits under 3 m", _ => "deeper deposits" }),
            };
            asset.Grow(asset);
            return asset;
        }

        DepositShape shape = Enum.TryParse(r.Get("Body"), out DepositShape sh) ? sh : DepositShape.Pocket;
        float amount = AmountValue[r.IndexOf("Amount", Amounts)];
        DepositBody body = DepositGrower.Grow(new DepositSeed(r.Seed, 0, 0, ore, amount, shape), (_, _) => 0.0, rules);
        var cells = new List<(int x, int y, int z, Grade g)>();
        int count = body.ForEachCell((x, y, z, g) => cells.Add((x, y, z, g)));
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = Vector3.One * 0.96f }, InstanceCount = cells.Count };
        for (int i = 0; i < cells.Count; i++)
        {
            var (x, y, z, g) = cells[i];
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f)));
            Color shade = new Color(c.R, c.G, c.B).Lerp(Colors.White, g switch { Grade.Poor => 0f, Grade.Medium => 0.1f, Grade.Rich => 0.25f, _ => 0.45f });
            mm.SetInstanceColor(i, shade);
        }
        asset.AddChild(new MultiMeshInstance3D { Name = "Body", Multimesh = mm, MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.6f, Metallic = 0.25f } });
        // The ground it lies in: a translucent block of soil from the surface down past the deepest cell.
        float deep = Mathf.Max(3f, -body.MinY + 1), half = Mathf.Max(6f, Mathf.Max(body.MaxX - body.MinX, body.MaxZ - body.MinZ) * 0.5f + 3f);
        float cx = (body.MinX + body.MaxX) * 0.5f, cz = (body.MinZ + body.MaxZ) * 0.5f;
        asset.AddChild(new MeshInstance3D
        {
            Name = "Soil", Mesh = new BoxMesh { Size = new Vector3(half * 2, deep, half * 2) }, Position = new Vector3(cx, -deep * 0.5f - 0.02f, cz),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.33f, 0.24f, 0.16f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Front, Roughness = 1f },
        });
        asset.AddChild(new MeshInstance3D
        {
            Name = "Surface", Mesh = new BoxMesh { Size = new Vector3(half * 2, 0.04f, half * 2) }, Position = new Vector3(cx, -0.02f, cz),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.36f, 0.22f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 1f },
        });
        asset.Stage = new StageSpec { Ground = false };
        asset.FactsFn = () => new[]
        {
            ("Ore", item.Name), ("Shape", body.Shape.ToString()), ("Base grade", body.BaseGrade.ToString()), ("Cells", count.ToString()),
            ("Depth", $"{-body.MaxY:0.#} to {-body.MinY:0.#} m"), ("Ore if dug", $"{body.TotalOre(12):0} units at 12 per cell"),
        };
        asset.Grow(asset);
        return asset;
    }
}
