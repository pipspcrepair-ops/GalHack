using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Structures;
using Godot;
using SigilDef = PoiGenerator.Gen.SigilDef;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// The signs and markers. What the game draws today: the faction sigils its places paint on their walls
/// (poi-generator's sigils.txt, painted cell by cell onto block faces, glowing where the faction's marks
/// glow), and the hazard sign set the visor's alert row shows (slice/data/signs). Nothing in this code
/// base draws a warning, direction, group, Husk or Hollow sign plate in the world; the draft art in
/// docs/signs (81 signs, the traveller marks, the sigils, the zone bands) is listed as it is, under
/// "Design art", and every entry says it is not in the game yet.
/// </summary>
public sealed partial class SignsProvider : IAssetProvider
{
    public string Category => "Signs";

    public static string DocsSigns => System.IO.Path.Combine(PartsData.Repo, "docs", "signs");

    public IEnumerable<AssetEntry> Entries()
    {
        foreach (var (id, def) in Sigils())
        {
            string faction = id;
            yield return new AssetEntry
            {
                Id = "sigil." + faction, Group = "In the game/Faction sigils on walls", Name = PartsData.Nice(faction),
                Options = new[]
                {
                    new AssetOption("Grid", "Large", "Small") { Rollable = true },
                    new AssetOption("Scale", "Full", "Half") { Rollable = true },
                    new AssetOption("Glow", "Where the faction glows", "Painted only", "All lit") { Rollable = true },
                    new AssetOption("Wall", "Concrete", "Steel", "Combat steel") { Rollable = true },
                },
                Build = r => BuildSigil(faction, r),
            };
        }
        string hud = PartsData.Proto("slice", "data", "signs");
        foreach (string file in Pngs(hud))
            yield return PlateEntry("hud." + Stem(file), "In the game/Hazard signs (visor alerts)", Name(file), file, "Slice HUD alert row (slice/data/signs); the game draws it on the visor, not as a plate in the world");
        foreach (var (folder, label) in new[] { ("png", "Signs"), ("marks", "Traveller marks"), ("sigils/png", "Faction sigils"), ("zones/png", "Deck and zone bands"), ("restyle/png", "Restyle proposal") })
            foreach (string file in Pngs(System.IO.Path.Combine(DocsSigns, folder.Replace('/', System.IO.Path.DirectorySeparatorChar))))
                yield return PlateEntry("docs." + PartsData.Slug(folder) + "." + Stem(file), "Design art (docs/signs, not in the game yet)/" + label, Name(file), file,
                    "Draft art in docs/signs/" + folder + ", not in the game yet");
    }

    static string Stem(string file) => System.IO.Path.GetFileNameWithoutExtension(file);

    static string Name(string file) => PartsData.Nice(System.IO.Path.GetFileNameWithoutExtension(file));

    static IEnumerable<string> Pngs(string dir) => Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.png").OrderBy(f => f, StringComparer.Ordinal) : Enumerable.Empty<string>();

    static Dictionary<string, SigilDef> _sigils;
    static IEnumerable<KeyValuePair<string, SigilDef>> Sigils()
    {
        string path = PartsData.Proto("poi-generator", "data", "sigils.txt");
        _sigils ??= File.Exists(path) ? SigilDef.Parse(File.ReadAllText(path), path) : new Dictionary<string, SigilDef>();
        return _sigils;
    }

    // ---- Plates (a picture of a sign) -------------------------------------------------------------

    static AssetEntry PlateEntry(string id, string group, string name, string file, string source) => new()
    {
        Id = id, Group = group, Name = name,
        Options = new[] { new AssetOption("Mounting", "On a post", "On a wall") },
        Build = r => BuildPlate(name, file, source, r.Get("Mounting", "On a post") == "On a wall"),
    };

    static readonly Dictionary<string, ImageTexture> Textures = new();

    static ImageTexture Load(string file)
    {
        if (Textures.TryGetValue(file, out ImageTexture t)) return t;
        Image image = Image.LoadFromFile(file);
        if (image == null || image.IsEmpty()) return null;
        image.GenerateMipmaps();
        return Textures[file] = ImageTexture.CreateFromImage(image);
    }

    static AssetInstance BuildPlate(string name, string file, string source, bool wall)
    {
        var asset = new ModelAsset { Name = name };
        ImageTexture tex = Load(file);
        Vector2 size = tex == null ? Vector2.One : new Vector2(tex.GetWidth(), tex.GetHeight());
        float h = 1.0f, w = h * size.X / Math.Max(1f, size.Y);
        float bottom = wall ? 0.9f : 1.6f;
        var metal = new StandardMaterial3D { AlbedoColor = new Color("4a4f57"), Roughness = 0.6f, Metallic = 0.4f };
        // The plate: a slab with its picture on the front, set a hair proud so the two never share a face.
        asset.AddChild(new MeshInstance3D { Name = "Plate", Mesh = new BoxMesh { Size = new Vector3(w + 0.06f, h + 0.06f, 0.04f) }, Position = new Vector3(0, bottom + h * 0.5f, 0), MaterialOverride = metal });
        var picture = new StandardMaterial3D { AlbedoTexture = tex, Roughness = 0.7f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic, Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor };
        asset.AddChild(new MeshInstance3D { Name = "Picture", Mesh = new QuadMesh { Size = new Vector2(w, h) }, Position = new Vector3(0, bottom + h * 0.5f, 0.0215f), MaterialOverride = picture });
        if (wall)
            asset.AddChild(new MeshInstance3D { Name = "Wall", Mesh = new BoxMesh { Size = new Vector3(w + 1.2f, h + 1.6f, 0.2f) }, Position = new Vector3(0, (h + 1.6f) * 0.5f, -0.12f), MaterialOverride = DecoProvider.StandMaterial });
        else
        {
            asset.AddChild(new MeshInstance3D { Name = "Post", Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = bottom + h }, Position = new Vector3(0, (bottom + h) * 0.5f, -0.06f), MaterialOverride = metal });
            asset.AddChild(new MeshInstance3D { Name = "Foot", Mesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.2f, Height = 0.08f }, Position = new Vector3(0, 0.04f, -0.06f), MaterialOverride = metal });
        }
        asset.Stage = new StageSpec { Ground = true };
        asset.FactsFn = () => new[] { ("Source", source), ("Picture", tex == null ? "missing" : $"{(int)size.X} x {(int)size.Y} px"), ("File", System.IO.Path.GetRelativePath(PartsData.Repo, file)) };
        asset.Grow(asset);
        return asset;
    }

    // ---- Sigils painted on a wall ------------------------------------------------------------------

    static AssetInstance BuildSigil(string faction, BuildRequest r)
    {
        SigilDef sigil = _sigils[faction];
        Catalogue cat = PartsData.Blocks;
        bool small = r.Get("Grid", "Large") == "Small";
        int scale = r.Get("Scale", "Full") == "Half" ? 2 : 1;
        string glow = r.Get("Glow", "Where the faction glows");
        string wallName = r.Get("Wall", "Concrete");
        BlockType wall = cat.Types.Skip(1).FirstOrDefault(t => t.IsArmor && t.Name.StartsWith(wallName, StringComparison.OrdinalIgnoreCase)) ?? cat.Types.Skip(1).First(t => t.IsArmor);
        var s = new Structure(cat, cat.Grid(small ? GridSize.Small : GridSize.Large)) { Name = faction };
        int size = sigil.Size / scale, border = 1;
        var slots = new Dictionary<(uint, bool), ushort>();
        ushort Slot(char ch)
        {
            var c = sigil.Colours[ch];
            uint rgb = ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
            bool lit = glow == "All lit" || (glow != "Painted only" && ch != 'a');
            if (!slots.TryGetValue((rgb, lit), out ushort slot))
                slots[(rgb, lit)] = slot = s.Palette.Intern(lit ? Paint.Lit(LampShape.Full, LightSettings.Make(rgb)) : Paint.Solid(rgb), "sigil");
            return slot;
        }
        for (int row = -border; row < size + border; row++)
            for (int col = -border; col < size + border; col++)
            {
                Block b = Building.Make(s, wall);
                if (row >= 0 && row < size && col >= 0 && col < size)
                {
                    // Seen from +Z the sigil reads left to right along +X; seen from the back it is the same picture, mirrored.
                    char front = sigil.Rows[row * scale][col * scale], back = sigil.Rows[row * scale][(size - 1 - col) * scale];
                    if (sigil.Colours.ContainsKey(front)) b.Paint.PZ = Slot(front);
                    if (sigil.Colours.ContainsKey(back)) b.Paint.NZ = Slot(back);
                }
                s.Set(new Int3(col, size - 1 - row + border, 0), b);
            }
        s.DirtyChunks.Clear();
        var asset = new StructureAsset(s);
        asset.FactsFn = () => new[]
        {
            ("Source", "poi-generator data/sigils.txt, painted onto block faces as the architect does"),
            ("Painted cells", $"{sigil.Rows.Sum(rw => rw.Count(sigil.Colours.ContainsKey))} of {sigil.Size * sigil.Size}"),
            ("Colours", string.Join(", ", sigil.Colours.Select(kv => $"{kv.Key}={kv.Value.R:x2}{kv.Value.G:x2}{kv.Value.B:x2}"))),
            ("Wall", wall.Name + (small ? ", small grid" : ", large grid")),
        };
        return asset;
    }
}
