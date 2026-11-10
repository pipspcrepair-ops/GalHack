using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// Every block of the game's catalogue (building's armour materials and components, the vessel parts,
/// the production machines and guns the slice adds), one entry each, listed by what it does. An
/// armour material takes any shape; every block takes any of the 24 orientations, either grid,
/// paint, damage and power. The block is placed in a one-cell structure and drawn by the slice's own
/// mesher; its animations are the game's real damage code (hits, blasts, repairs) and its working
/// states (doors, lights, power, thrust).
/// </summary>
public sealed partial class BlocksProvider : IAssetProvider
{
    public string Category => "Blocks";

    public static readonly string[] Orients = Enumerable.Range(0, Orientations.Count).Select(Orientations.Name).ToArray();
    public static readonly string[] Damages = { "Intact", "Scuffed", "Cracked and dented", "Holed" };
    static readonly float[] DamageHp = { 1f, 0.62f, 0.4f, 0.14f };
    public static readonly string[] Colours = { "Bare material", "Hull white", "Signal red", "Safety orange", "Hazard yellow", "Deep blue", "Green", "Black" };
    static readonly uint[] ColourRgb = { 0, 0xe9dfc4, 0xc1272d, 0xe8691b, 0xf2c200, 0x1d3f7a, 0x3a7a3c, 0x23252b };
    public static readonly string[] Patterns = Enum.GetNames<Pattern>();
    public static readonly string[] Textures = Enum.GetNames<FaceTexture>();
    public static readonly string[] Stencils = { "None", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    public IEnumerable<AssetEntry> Entries()
    {
        Catalogue cat = PartsData.Blocks;
        foreach (BlockType t in cat.Types.Skip(1))
            yield return Entry(cat, t);
        foreach (AssetEntry e in Assemblies.Entries(cat)) yield return e;
    }

    static string GroupOf(BlockType t) => t.IsArmor ? "Armour" : "Devices/" + PartsData.Nice(string.IsNullOrEmpty(t.Function) ? "other" : t.Function);

    static AssetEntry Entry(Catalogue cat, BlockType t)
    {
        var options = new List<AssetOption>();
        bool both = true;
        if (PartsData.Items.Blocks.TryGet(t.Id, out BlockBill bill)) both = bill.OnLarge && bill.OnSmall;
        if (both) options.Add(new AssetOption("Grid", "Large", "Small") { Rollable = true });
        else options.Add(new AssetOption("Grid", PartsData.Items.Blocks.Get(t.Id).OnLarge ? "Large" : "Small"));
        if (t.IsArmor) options.Add(new AssetOption("Shape", cat.Shapes.Select(s => s.Name).ToArray()) { Rollable = true });
        // Devices face the viewer's camera by default (their front is -Z); thrusters keep their exhaust toward it.
        options.Add(new AssetOption("Orientation", Orients) { Default = t.IsArmor || t.IsThruster ? 0 : Math.Max(0, Array.IndexOf(Orients, "+y+z")), Rollable = true });
        options.Add(new AssetOption("Damage", Damages) { Rollable = true });
        if (t.IsDoor) options.Add(new AssetOption("Door", "Closed", "Open") { Rollable = true });
        if (t.IsLight)
        {
            options.Add(new AssetOption("Lamp", "On", "Off") { Rollable = true });
            options.Add(new AssetOption("Lamp mode", Enum.GetNames<LightAnim>()) { Rollable = true });
        }
        if (!t.IsArmor) options.Add(new AssetOption("Power", "On", "Off"));
        options.Add(new AssetOption("Paint", Colours) { Rollable = true });
        options.Add(new AssetOption("Pattern", Patterns) { Rollable = true });
        options.Add(new AssetOption("Texture", Textures) { Rollable = true });
        options.Add(new AssetOption("Grime", "0", "1", "2", "3") { Rollable = true });
        options.Add(new AssetOption("Stencil", Stencils) { Rollable = true });
        return new AssetEntry
        {
            Id = "block." + t.Id, Group = GroupOf(t), Name = t.Name, Randomisable = true,
            Options = options.ToArray(), Build = r => BuildBlock(cat, t, r),
        };
    }

    // ---- One block ------------------------------------------------------------------------

    public static Paint PaintOf(BuildRequest r, out bool any)
    {
        int colour = Array.IndexOf(Colours, r.Get("Paint", Colours[0]));
        Pattern pattern = Enum.TryParse(r.Get("Pattern", "None"), out Pattern p) ? p : Pattern.None;
        FaceTexture texture = Enum.TryParse(r.Get("Texture", "Panel"), out FaceTexture tx) ? tx : FaceTexture.Panel;
        int grime = int.TryParse(r.Get("Grime", "0"), out int g) ? g : 0;
        int stencil = Array.IndexOf(Stencils, r.Get("Stencil", "None")) - 1;
        // Choosing a pattern, texture, grime or stencil paints the face: hull white if no colour was chosen.
        any = colour > 0 || pattern != Pattern.None || texture != FaceTexture.Panel || grime > 0 || stencil >= 0;
        if (colour <= 0) colour = 1;
        uint accent = colour == 4 || colour == 1 ? 0x2b2d42u : 0xf2f2f2u;
        return new Paint(ColourRgb[colour], accent, pattern, texture, stencil < 0 ? (byte)0 : (byte)('0' + stencil), (byte)grime);
    }

    static AssetInstance BuildBlock(Catalogue cat, BlockType t, BuildRequest r)
    {
        bool small = r.Get("Grid", "Large") == "Small";
        var s = new Structure(cat, cat.Grid(small ? GridSize.Small : GridSize.Large));
        ShapeDef shape = t.IsArmor ? cat.Shapes.FirstOrDefault(x => x.Name == r.Get("Shape")) ?? cat.Cube : null;
        Block b = Building.Make(s, t, shape, Math.Max(0, Array.IndexOf(Orients, r.Get("Orientation"))));
        Paint paint = PaintOf(r, out bool painted);
        if (painted) b.Paint = FacePaint.All(s.Palette.Intern(paint, "viewer"));
        if (r.Get("Door") == "Open") b.Flags |= BlockFlags.Open;
        if (r.Get("Lamp") == "Off") b.Flags |= BlockFlags.Off;
        b.Hp = s.MaxHp(b) * DamageHp[Math.Max(0, Array.IndexOf(Damages, r.Get("Damage")))];
        s.Set(Int3.Zero, b);
        if (t.IsLight && Enum.TryParse(r.Get("Lamp mode"), out LightAnim anim) && anim != LightAnim.Steady)
            s.SetLight(Int3.Zero, LightSettings.Make(t.GlowColor, anim, 0.5f, 0, anim == LightAnim.Fade ? 0x4aa8e0u : (uint?)null));

        var asset = new StructureAsset(s) { Powered = r.Get("Power", "On") != "Off" };
        asset.FactsFn = () => Facts(cat, s, t, b, small, shape);
        Animate(asset, t, b);
        return asset;
    }

    static IEnumerable<(string, string)> Facts(Catalogue cat, Structure s, BlockType t, Block b, bool small, ShapeDef shape)
    {
        BlockStats st = s.StatsOf(b);
        yield return ("Kind", t.IsArmor ? "Armour material" : "Device: " + PartsData.Nice(t.Function));
        yield return ("Grid", small ? "Small grid, 0.5 m (small and hover vessels)" : "Large grid, 2 m (bases and capital vessels)");
        if (shape != null) yield return ("Shape", shape.Name);
        yield return ("Mass", st.Mass >= 1 ? $"{st.Mass:0.##} t" : $"{st.Mass * 1000:0.#} kg");
        yield return ("Hit points", $"{st.MaxHp:0.#}");
        yield return ("Armour", $"{st.Armor:0.#} flat, resist K {st.Resist.Kinetic:0.##} E {st.Resist.Explosive:0.##} En {st.Resist.Energy:0.##}");
        if (st.Cpu > 0) yield return ("CPU", $"{st.Cpu:0.#}");
        if (t.Power != 0) yield return ("Power", $"{t.Power * s.Grid.FunctionScale:0.###} MW");
        if (t.Thrust > 0) yield return ("Thrust", $"{t.Thrust * s.Grid.FunctionScale:0.#} kN");
        if (t.Storage > 0) yield return ("Storage", $"{t.Storage * s.Grid.StorageScale:0.#} SU");
        yield return ("Material", t.DamageLook + (t.Airtight ? ", airtight" : "") + (t.Transparent ? ", transparent" : ""));
        ItemCatalogue items = PartsData.Items;
        if (items.Blocks.TryGet(t.Id, out BlockBill bill))
        {
            yield return ("Recipe", items.Describe(bill.For(small), 6));
            Unlock u = bill.UnlockFor(small);
            yield return ("Tech tier", u == null ? "always available" : $"level {u.Level}, {u.Cost} points ({u.Tab})");
            if (bill.Ammo != null) yield return ("Ammo", items.TryGet(bill.Ammo, out ItemDef a) ? a.Name : bill.Ammo);
            if (bill.UniqueOnly) yield return ("Source", "salvage only");
        }
        MachineDef m = items.Machines.ForBlock(t.Id);
        if (m != null) yield return ("Machine", $"{m.Name}, tier {m.Tier} ({items.Machines.TierName(m.Tier)})");
    }

    static void Animate(StructureAsset a, BlockType t, Block original) => Working.Add(a);
}
