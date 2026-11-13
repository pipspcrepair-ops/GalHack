using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GalHack.Items;
using Godot;
using PlanetSurface.Infra;
using PoiGenerator.Gen;
using Slice.Items;
using Slice.Surface;

namespace Slice.Places;

/// <summary>
/// The small ruins' caches, for the game: a ruin isn't a place that stands up, it is drawn by the
/// planet's infrastructure layer, so this opens what it rolled. When you come near one, its century is
/// spawned on a worker from the ruin's own seed (the same blocks the drawing has) and each cache
/// becomes a container in the zone, once, kept for the game. The drawn crate is its look.
/// </summary>
public partial class RuinLoot : Node
{
    public SurfaceZone Zone;
    public Func<Vector3> Focus = () => Vector3.Zero;

    /// <summary>How near a ruin you come before its caches are opened up.</summary>
    public const float Reach = 70f;

    readonly HashSet<FlavourRuin> _done = new();
    Task<(FlavourRuin Ruin, PoiInstance Inst)> _spawn;
    double _scan;

    /// <summary>Containers made so far (tests).</summary>
    public int Made { get; private set; }

    public override void _Process(double delta)
    {
        if (Zone?.Planet == null) return;
        if (_spawn != null)
        {
            if (!_spawn.IsCompleted) return;
            var (ruin, inst) = _spawn.Result;
            _spawn = null;
            Make(ruin, inst);
            return;
        }
        if ((_scan -= delta) > 0) return;
        _scan = 1;
        Vector3 focus = Focus();
        foreach (FlavourRuin ruin in Zone.Planet.Infra.Ruins)
        {
            if (_done.Contains(ruin)) continue;
            var (cx, cz) = ruin.Center;
            Vector3 at = Zone.Planet.NearestCopy(new Vector3((float)cx, focus.Y, (float)cz), focus);
            if (at.DistanceTo(focus) > Reach) continue;
            _done.Add(ruin);
            WorldProfile profile = Zone.Planet.Profile;
            _spawn = Task.Run(() => (ruin, ruin.Spawn(profile)));
            return;
        }
    }

    void Make(FlavourRuin ruin, PoiInstance inst)
    {
        ItemCatalogue items = Zone.Data.Items;
        int[] size = Zone.Data.Rules.Containers.Corpse;
        Vector3 focus = Focus();
        foreach (LootCache c in inst.Caches)
        {
            var bill = new Bill();
            int k = 0;
            foreach (LootItem li in c.Items)
                if (items.TryGet(li.Id, out ItemDef def)) bill.Add(def.Id, PoiPlaces.CountOf(def, c.Tier, Hash.Cell(c.Cell, (uint)(++k * 31))));
            Slice.Arms.ArmsLoot.Add(bill, inst.Family.Tags, c.Tier, c.Secret, (int)inst.Tech, Hash.Cell(c.Cell, 0xA77u), Zone.Data.Arms, items); // weapons by place and rarity
            Vector3 at = Zone.Planet.NearestCopy(new Vector3(ruin.OriginX + c.Cell.X + 0.5f, ruin.OriginY + c.Cell.Y + 0.05f, ruin.OriginZ + c.Cell.Z + 0.5f), focus);
            Lootable loot = Zone.Loot.Create(LootKind.Cache, "Cache", at, Math.Max(4, size[0]), Math.Max(3, size[1]), bill);
            loot.Radius = 0.55f;
            loot.Node.Visible = false; // the drawn ruin has the crate
            Made++;
        }
    }
}
