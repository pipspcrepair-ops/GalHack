using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Places;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>Screenshot views for the remote toolbar and the deco gallery.</summary>
public sealed partial class Staging
{
    /// <summary>The remote toolbar on the HUD: items left in the base's storage, a pip per core, one slot out of range.</summary>
    IEnumerable<int> RemoteBarView()
    {
        foreach (int x in BaseView(survival: true)) yield return x;
        Vessel b0 = _game.Zone.Vessels.FirstOrDefault(v => v.IsBase && !v.IsRuin);
        if (b0 == null) yield break;
        _game.FixedCamera = null;
        ItemCatalogue items = _game.Zone.Data.Items;
        var stock = new (string id, int n)[]
        {
            (Play.BlockItem("hardened"), 340), (Play.BlockItem("steel"), 120), (Play.BlockItem("glass"), 48),
            ("medkit", 6), ("pistol-rounds", 12), ("iron-bar", 80),
        };
        foreach (var (id, n) in stock) b0.Storage.Add(id, n);
        b0.StorageGrid.Reconcile();
        var far = new SharedStorage(items, "Outpost", 500);
        far.Add(Play.BlockItem("concrete"), 60);
        var pack = _game.Pack;
        for (int i = 0; i < 5; i++) pack.SetRemote(i, b0.Storage, b0.Name, stock[i].id);
        pack.SetRemote(5, far, "Outpost", Play.BlockItem("concrete"));
        pack.Bar = BarSource.Remote;
        pack.Selected = 0;
        foreach (int x in _drive.WalkTo(b0.ToWorld(new N.Vector3(0, 0, -9f)), 1f, 20f)) yield return x;
        foreach (int x in _drive.LookAt(b0.ToWorld(new N.Vector3(0, 0, -1f)), 12)) yield return x;
        foreach (int x in Play.Select(1)) yield return x;
        foreach (int x in _drive.Wait(90)) yield return x;
    }

    /// <summary>The deco gallery above the toolbar, the universal deco block in hand, a few pieces already placed.</summary>
    IEnumerable<int> DecoGalleryView()
    {
        foreach (int x in BaseView()) yield return x;
        Vessel b0 = _game.Zone.Vessels.FirstOrDefault(v => v.IsBase && !v.IsRuin);
        if (b0 == null) yield break;
        _game.FixedCamera = null;
        _game.Progress.Creative = true;
        Hand((DecoCatalogue.UniversalItem, 40));
        foreach (int x in Play.ToToolbar(DecoCatalogue.UniversalItem, 1)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        var tool = _game.Builder;
        tool.DecoTheme = _args.GetValueOrDefault("theme", "industrial");
        tool.DecoPieceId = _args.GetValueOrDefault("piece", "cargo-crate");
        var deco = Decor.Shared.Catalogue;
        // A few pieces on the grid already: crates on top, a lamp under the roof.
        var core = new Int3(0, 0, 0);
        foreach (var (piece, target, place) in new[]
                 {
                     ("cargo-crate", core, new Int3(0, 1, 0)), ("oil-barrel", new Int3(-1, 0, 0), new Int3(-1, 1, 0)),
                     ("workbench", new Int3(1, 0, 0), new Int3(1, 1, 0)),
                 })
        {
            if (!b0.Structure.Has(target)) continue;
            var plan = tool.PlanDeco(b0, deco.Piece(piece), target, place, 0);
            if (plan.Why == null) b0.AddDeco(piece, place, 0, plan.Cells, plan.Local);
        }
        foreach (int x in Play.Select(1)) yield return x;
        foreach (int x in _drive.WalkTo(b0.ToWorld(new N.Vector3(0, 0, -9f)), 1f, 20f)) yield return x;
        foreach (int x in _drive.LookAt(b0.ToWorld(new N.Vector3(0, 1f, 0f)), 12)) yield return x;
        foreach (int x in _drive.Wait(150)) yield return x;
    }
}
