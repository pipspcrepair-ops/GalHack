using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetViewer.Providers.Parts;
using GalHack.Items;
using GalHack.Structures;
using Godot;

namespace AssetViewer.Tests;

/// <summary>
/// The Blocks, Items, Deco, Signs and Vehicles providers: every entry builds with its defaults and again in a
/// second state or orientation, every block, item, deco piece, vehicle design and sign file in the game's data has
/// an entry, and a sample of the animations plays in the tree for a few frames.
/// </summary>
public partial class PartsProviderTest : Node
{
    int _passes, _failures;
    static readonly string[] Mine = { "Blocks", "Items", "Deco", "Signs", "Vehicles" };

    bool Check(bool ok, string what)
    {
        if (ok) _passes++; else { _failures++; GD.PrintErr("  FAIL " + what); }
        return ok;
    }

    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception e) { _failures++; GD.PrintErr("  FAIL threw: " + e); }
        GD.Print($"\n{_passes} checks passed, {_failures} failed");
        GD.Print(_failures == 0 ? "ALL PASSED" : "SOME CHECKS FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    static BuildRequest Request(AssetEntry e, bool alternative)
    {
        var req = new BuildRequest { Seed = alternative ? 777UL : Viewer.DefaultSeed(e) };
        foreach (AssetOption o in e.Options)
        {
            if (o.Values.Length == 0) { req.Options[o.Name] = ""; continue; }
            int i = alternative ? o.Values.Length - 1 - (o.Default == o.Values.Length - 1 ? 1 : 0) : o.Default;
            req.Options[o.Name] = o.Values[Math.Clamp(i, 0, o.Values.Length - 1)];
        }
        return req;
    }

    async System.Threading.Tasks.Task Run()
    {
        GD.Print("== registry");
        var registry = AssetRegistry.Discover();
        Check(registry.Failures.Count == 0, "providers list cleanly: " + string.Join("; ", registry.Failures.Select(f => f.Provider + ": " + f.Error)));
        var entries = registry.Entries.Where(e => Mine.Contains(e.Category) && e.Build != null).ToList();
        foreach (string c in Mine) Check(entries.Any(e => e.Category == c), $"{c} has assets");
        foreach (var g in entries.GroupBy(e => e.Category)) GD.Print($"  {g.Key}: {g.Count()} entries");

        GD.Print("== every entry builds with its defaults");
        int built = 0;
        foreach (AssetEntry e in entries)
        {
            try
            {
                AssetInstance a = e.Build(Request(e, false));
                if (Check(a != null, $"{e.Key} builds")) { built++; Check(a.Facts().Any() || e.Category == "Signs", $"{e.Key} has facts"); a.Free(); }
            }
            catch (Exception ex) { Check(false, $"{e.Key} threw: {ex.GetBaseException().Message}"); }
        }
        GD.Print($"  {built} of {entries.Count} built");

        GD.Print("== a second state or orientation of each family");
        int second = 0;
        foreach (var family in entries.Where(e => e.Options.Length > 0).GroupBy(e => e.Category + "|" + e.Group.Split('/')[0] + "|" + e.Id.Split('.')[0]))
        {
            foreach (AssetEntry e in family.Take(4))
            {
                try
                {
                    AssetInstance a = e.Build(Request(e, true));
                    if (Check(a != null, $"{e.Key} builds in its last state")) { second++; a.Free(); }
                }
                catch (Exception ex) { Check(false, $"{e.Key} (last state) threw: {ex.GetBaseException().Message}"); }
            }
        }
        GD.Print($"  {second} second builds");

        GD.Print("== every shape, orientation and damage of one armour block and one device builds");
        var steel = entries.First(e => e.Id == "block.steel");
        foreach (string shape in steel.Options.First(o => o.Name == "Shape").Values)
            foreach (string orient in new[] { "+y-z", "+x+y", "-y+z", "+z-x" })
            {
                var r = Request(steel, false);
                r.Options["Shape"] = shape;
                r.Options["Orientation"] = orient;
                AssetInstance a = steel.Build(r);
                Check(a != null, $"steel {shape} {orient}");
                a?.Free();
            }
        var orients = BlocksProvider.Orients;
        Check(orients.Length == 24 && orients.Distinct().Count() == 24, "24 distinct orientations");
        foreach (string d in BlocksProvider.Damages)
        {
            var r = Request(steel, false);
            r.Options["Damage"] = d;
            steel.Build(r).Free();
        }

        GD.Print("== coverage of the game's data");
        Catalogue cat = PartsData.Blocks;
        var blockIds = new HashSet<string>(entries.Where(e => e.Category == "Blocks").Select(e => e.Id));
        foreach (BlockType t in cat.Types.Skip(1)) Check(blockIds.Contains("block." + t.Id), $"block {t.Id} has an entry");
        foreach (BlockBill b in PartsData.Items.Blocks.All.Where(b => b.Family != BlockFamily.Deco))
            Check(cat.TryGetType(b.BlockId, out _) && blockIds.Contains("block." + b.BlockId), $"blocks.json id {b.BlockId} has an entry");
        var itemIds = new HashSet<string>(entries.Where(e => e.Category == "Items").Select(e => e.Id));
        foreach (ItemDef i in PartsData.Items.Items) Check(itemIds.Contains("item." + i.Id), $"item {i.Id} has an entry");
        var decoIds = new HashSet<string>(entries.Where(e => e.Category == "Deco").Select(e => e.Id));
        foreach (var p in PartsData.Deco.Catalogue.Pieces) Check(decoIds.Contains("piece." + p.Id), $"deco piece {p.Id} has an entry");
        foreach (string room in PartsData.Deco.Poi.Arrangements.Sets.Keys) Check(decoIds.Contains("room.set." + room), $"room plan {room} has an entry");
        foreach (string room in PartsData.Deco.Poi.Arrangements.Recipes.Keys) Check(decoIds.Contains("room.recipe." + room), $"room recipe {room} has an entry");
        foreach (string site in PartsData.Deco.Poi.Arrangements.Sites.Keys) Check(decoIds.Contains("site." + site), $"site {site} has an entry");
        var vehicleIds = new HashSet<string>(entries.Where(e => e.Category == "Vehicles").Select(e => e.Id));
        Check(vehicleIds.Contains("hover-bike"), "the hover bike has an entry");
        foreach (var p in PartsData.Deco.Catalogue.Pieces)
            if (PartsData.Deco.Catalogue.Set(p.Set).Outdoor && p.Salvage != null) Check(vehicleIds.Contains("deco." + p.Id), $"deco vehicle {p.Id} has an entry");
        foreach (string dir in Directory.EnumerateDirectories(PartsData.Proto()))
        {
            string bp = Path.Combine(dir, "data", "blueprints");
            if (!Directory.Exists(bp)) continue;
            foreach (string f in Directory.EnumerateFiles(bp, "*.gbp"))
                Check(vehicleIds.Contains($"vessel.{Path.GetFileName(dir)}.{Path.GetFileNameWithoutExtension(f)}"), $"blueprint {Path.GetFileName(dir)}/{Path.GetFileName(f)} has an entry");
        }
        Check(vehicleIds.Any(i => i.StartsWith("hull.")), "capital hulls are listed");
        var signIds = new HashSet<string>(entries.Where(e => e.Category == "Signs").Select(e => e.Id));
        foreach (string f in Directory.EnumerateFiles(PartsData.Proto("slice", "data", "signs"), "*.png"))
            Check(signIds.Contains("hud." + Path.GetFileNameWithoutExtension(f)), $"hud sign {Path.GetFileName(f)} has an entry");
        foreach (string line in File.ReadLines(PartsData.Proto("poi-generator", "data", "sigils.txt")).Where(l => l.StartsWith("sigil ")))
            Check(signIds.Contains("sigil." + line[6..].Trim()), $"sigil {line[6..].Trim()} has an entry");
        foreach (string f in Directory.EnumerateFiles(Path.Combine(PartsData.Repo, "docs", "signs", "png"), "*.png"))
            Check(signIds.Contains("docs.png." + Path.GetFileNameWithoutExtension(f)), $"docs sign {Path.GetFileName(f)} has an entry");
        Check(entries.Select(e => e.Key).Distinct().Count() == entries.Count, "keys are unique");

        GD.Print("== animations play in the tree");
        var sample = new List<AssetEntry>();
        foreach (var family in entries.GroupBy(e => e.Category + "|" + e.Group))
            sample.Add(family.First());
        sample.AddRange(entries.Where(e => e.Id is "block.door" or "block.light" or "block.thruster" or "block.largefab" or "item.stage-rifle" or "item.tack-pistol" or "item.hornet-hive" or "vessel.slice.starter" or "vessel.slice.shuttle" or "hover-bike" or "assembly.room" or "piece.office-lamp"));
        int played = 0;
        foreach (AssetEntry e in sample.Distinct())
        {
            AssetInstance a = null;
            try
            {
                a = e.Build(Request(e, false));
                AddChild(a);
                foreach (string anim in a.Animations)
                {
                    a.PlayAnimation(anim);
                    await Frames(3);
                    played++;
                }
                await Frames(1);
            }
            catch (Exception ex) { Check(false, $"{e.Key} animation threw: {ex.GetBaseException().Message}"); }
            finally { if (a != null) { RemoveChild(a); a.Release(); a.Free(); } }
        }
        GD.Print($"  {played} animations played");
        Check(played > 40, "animations were played");
    }
}
