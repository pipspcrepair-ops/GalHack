using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using Godot;
using Slice.Places;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// The decorations library: every piece of every theme set (state, age, fall, cover, power, doors,
/// palette), and the arrangement templates that furnish rooms and outdoor sites, each shown as it
/// was used and as it was found a century on after the disorder pass. Walks the deco catalogue and
/// the arrangements file; nothing here lists a piece or a room.
/// </summary>
public sealed partial class DecoProvider : IAssetProvider
{
    public string Category => "Deco";

    public static readonly string[] Ages = { "Fresh", "OldWorld", "Preserved" };
    public static readonly string[] Covers = Enum.GetNames<DecoCover>();
    public static readonly string[] Views = { "As found", "As used", "Used and found side by side" };
    public static readonly string[] FoundStates = { "abandoned", "infested", "ruined", "active" };
    public static readonly string[] Techs = { "Mid", "Low", "High" };
    public static readonly string[] Sizes = { "Medium 10 x 8 m", "Small 7 x 6 m", "Large 14 x 10 m" };

    public IEnumerable<AssetEntry> Entries()
    {
        Decor decor = PartsData.Deco;
        if (decor == null) yield break;
        foreach (DecoPiece p in decor.Catalogue.Pieces)
            yield return PieceEntry(decor, p);
        foreach (AssetEntry e in Rooms.Entries(decor)) yield return e;
    }

    public static AssetEntry PieceEntry(Decor decor, DecoPiece p)
    {
        DecoSet set = decor.Catalogue.Set(p.Set);
        var states = new List<string> { nameof(DecoState.Intact) };
        states.AddRange(p.States.Where(s => s != DecoState.Intact).Select(s => s.ToString()));
        var options = new List<AssetOption>
        {
            new AssetOption("State", states.ToArray()) { Rollable = true },
            new AssetOption("Age", Ages) { Rollable = true },
        };
        if (p.Falls.Count > 0) options.Add(new AssetOption("Falls", p.Falls.Select(f => f.ToString()).ToArray()) { Rollable = true });
        options.Add(new AssetOption("Cover", Covers) { Rollable = true });
        options.Add(new AssetOption("Power", "On", "Off") { Rollable = true });
        if (p.Outdoor) options.Add(new AssetOption("Doors", "As state says", "Flung open"));
        options.Add(new AssetOption("Exposed to weather", "No", "Yes"));
        options.Add(new AssetOption("Palette", new[] { "Piece default" }.Concat(decor.Catalogue.Palettes.Keys.Where(k => k != "base")).ToArray()) { Rollable = true });
        return new AssetEntry
        {
            Id = "piece." + p.Id, Group = "Pieces/" + set.Name, Name = p.Name, Randomisable = true,
            Options = options.ToArray(), Build = r => BuildPiece(decor, p, r),
        };
    }

    // ---- One piece -----------------------------------------------------------------------------

    public static DecoLook LookOf(DecoPiece p, BuildRequest r)
    {
        DecoState state = Enum.TryParse(r.Get("State"), out DecoState s) ? s : DecoState.Intact;
        DecoFall fall = state == DecoState.Knocked && Enum.TryParse(r.Get("Falls"), out DecoFall f) ? f : state == DecoState.Knocked && p.Falls.Count > 0 ? p.Falls[0] : DecoFall.None;
        DecoAge age = Enum.TryParse(r.Get("Age"), out DecoAge a) ? a : DecoAge.Fresh;
        DecoCover cover = Enum.TryParse(r.Get("Cover"), out DecoCover c) ? c : DecoCover.Vines;
        string palette = r.Get("Palette", "Piece default");
        return new DecoLook(p.Id, state, fall, r.Get("Power", "On") == "On", cover, palette == "Piece default" ? p.Palette : palette,
            r.Get("Doors") == "Flung open", age, r.Get("Exposed to weather") == "Yes");
    }

    static AssetInstance BuildPiece(Decor decor, DecoPiece p, BuildRequest r)
    {
        DecoLook look = LookOf(p, r);
        var asset = new ModelAsset { Name = p.Id };
        bool lifted = look.State != DecoState.Knocked && p.Mount is DecoMount.Wall or DecoMount.Ceiling;
        float y = !lifted ? 0 : p.Mount == DecoMount.Wall ? p.MountY : 2.6f - p.Height;
        bool onTop = p.Mount == DecoMount.Top && look.State != DecoState.Knocked;
        if (lifted && p.Mount == DecoMount.Wall) Wall(asset, p);
        if (lifted && p.Mount == DecoMount.Ceiling) Gantry(asset, p);
        if (onTop) Table(asset, p);
        var at = new Transform3D(Basis.Identity, new Vector3(0, y + (onTop ? 0.75f : 0f), 0));
        DecoNode node = DecoNode.Create(decor.Meshes, look, at, collide: false, lights: true);
        asset.AddChild(node);
        asset.Stage = new StageSpec { Ground = true, MinDistanceShare = 0.02f };
        asset.FactsFn = () => Facts(decor, p, look);
        asset.Grow(asset);
        return asset;
    }

    /// <summary>A display wall behind a wall piece, so it is hung on something.</summary>
    static void Wall(ModelAsset a, DecoPiece p)
    {
        var mesh = new BoxMesh { Size = new Vector3(p.Width + 0.4f, Mathf.Max(2.6f, p.MountY + p.Height + 0.2f), 0.12f) };
        a.AddChild(new MeshInstance3D { Name = "Wall", Mesh = mesh, Position = new Vector3(0, mesh.Size.Y / 2, p.Depth / 2 + 0.06f), MaterialOverride = StandMaterial });
    }

    /// <summary>A gantry over a ceiling piece, so it hangs from something.</summary>
    static void Gantry(ModelAsset a, DecoPiece p)
    {
        a.AddChild(new MeshInstance3D { Name = "Beam", Mesh = new BoxMesh { Size = new Vector3(p.Width + 0.6f, 0.12f, 0.2f) }, Position = new Vector3(0, 2.66f, 0), MaterialOverride = StandMaterial });
        foreach (float dx in new[] { -1f, 1f })
            a.AddChild(new MeshInstance3D { Name = "Post", Mesh = new BoxMesh { Size = new Vector3(0.1f, 2.7f, 0.1f) }, Position = new Vector3(dx * (p.Width / 2 + 0.25f), 1.35f, 0), MaterialOverride = StandMaterial });
    }

    /// <summary>A plain table under a piece that stands on tables, at the height those pieces are made for.</summary>
    static void Table(ModelAsset a, DecoPiece p) =>
        a.AddChild(new MeshInstance3D { Name = "Table", Mesh = new BoxMesh { Size = new Vector3(Mathf.Max(0.6f, p.Width + 0.1f), 0.75f, Mathf.Max(0.6f, p.Depth + 0.1f)) }, Position = new Vector3(0, 0.375f, 0), MaterialOverride = StandMaterial });

    static StandardMaterial3D _stand;
    public static StandardMaterial3D StandMaterial => _stand ??= new StandardMaterial3D { AlbedoColor = new Color("3a3632"), Roughness = 0.95f };

    static IEnumerable<(string, string)> Facts(Decor decor, DecoPiece p, DecoLook look)
    {
        DecoSet set = decor.Catalogue.Set(p.Set);
        ItemCatalogue items = PartsData.Items;
        yield return ("Set", $"{set.Name} ({set.Tech.ToString().ToLowerInvariant()} tech)");
        yield return ("Roles", string.Join(", ", p.Roles));
        yield return ("Size", $"{p.Width:0.#} x {p.Height:0.#} x {p.Depth:0.#} m, {p.LargeCells.X}x{p.LargeCells.Y}x{p.LargeCells.Z} large cells");
        yield return ("Mount", p.Mount + (p.Mount == DecoMount.Wall ? $" at {p.MountY:0.#} m" : "") + (p.Surface > 0 ? $", top at {p.Surface:0.##} m" : ""));
        yield return ("States", string.Join(", ", p.States));
        if (p.Falls.Count > 0) yield return ("Falls", string.Join(", ", p.Falls));
        yield return ("Mass", $"{p.MassKg:0.#} kg, {p.Hp} hit points");
        yield return ("Recipe", items.Describe(p.Bill, 6));
        DecoUnlock u = p.UnlockLarge;
        if (u != null) yield return ("Tech tier", $"level {u.Level}, {u.Cost} points");
        if (p.Light != null) yield return ("Light", $"{p.Light.Range:0.#} m, energy {p.Light.Energy:0.##}");
        if (p.Anim != null) yield return ("Animation", p.Anim + " (plays by itself while lit)");
        if (p.Blast != null) yield return ("Blast", $"{p.Blast.Radius:0.#} m, damage {p.Blast.Damage:0}");
        if (p.Salvage != null) yield return ("Salvage", p.Salvage);
        yield return ("Shown", $"{look.State}, {look.Age}" + (look.Powered ? ", lit" : ", dark"));
    }
}
