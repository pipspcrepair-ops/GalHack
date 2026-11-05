using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Planets.Gen;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Items;
using Slice.Surface;
using Slice.Ui;
using Slice.Vessels;
using Wildlife;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// A player's hands on the loop, step by step: everything goes through the game's own inputs
/// (look, walk, keys, the mouse on the Tab menu), never a teleport or a grant. Each routine is an
/// iterator yielding once per physics tick. Shared by the real-player test and the screenshots.
/// </summary>
public sealed class Playthrough
{
    public readonly Game.Game Game;
    public readonly Driver Drive;
    public Action<string> Log = s => GD.Print("  " + s);

    public Playthrough(Game.Game game)
    {
        Game = game;
        Drive = new Driver(game);
    }

    Controls Controls => Game.Controls;
    PlayerInventory Pack => Game.Pack;

    // ---- Waiting -----------------------------------------------------------------------------

    public IEnumerable<int> WaitFor(Func<bool> done, float seconds)
    {
        int ticks = (int)(seconds * 60);
        for (int i = 0; i < ticks && !done(); i++) yield return 0;
    }

    // ---- The menu ----------------------------------------------------------------------------

    public IEnumerable<int> OpenMenu(MenuTab tab)
    {
        if (!Game.Menu.IsOpen) foreach (int x in Drive.Tap(Key.Tab)) yield return x;
        foreach (int x in Drive.Wait(1)) yield return x;
        if (Game.Menu.Tab != tab) foreach (int x in Drive.ClickWidget($"tab:{tab.ToString().ToLowerInvariant()}")) yield return x;
    }

    public IEnumerable<int> CloseMenu()
    {
        if (Game.Menu.IsOpen) foreach (int x in Drive.Tap(Key.Tab)) yield return x;
        foreach (int x in Drive.Wait(1)) yield return x;
    }

    /// <summary>Clicks a widget that may be on a later page of a list, paging with a button until it shows.</summary>
    public IEnumerable<int> ClickPaged(string id, string next, int pages = 12)
    {
        foreach (int x in Drive.Wait(1)) yield return x;
        for (int p = 0; p < pages && Game.Menu.RectOf(id) == null; p++)
        {
            if (Game.Menu.RectOf(next) == null) break;
            foreach (int x in Drive.ClickWidget(next)) yield return x;
        }
        foreach (int x in Drive.ClickWidget(id)) yield return x;
    }

    /// <summary>Pages a list back to its start.</summary>
    public IEnumerable<int> PageToStart(string up, int pages = 12)
    {
        for (int p = 0; p < pages; p++)
        {
            foreach (int x in Drive.Wait(1)) yield return x;
            if (Game.Menu.RectOf(up) is not Rect2) yield break;
            int before = Game.Menu.Root.Gui.Rects.Count;
            foreach (int x in Drive.ClickWidget(up)) yield return x;
        }
    }

    /// <summary>
    /// The start, as a player plays it: wake in the cryopod, F opens it, step out, E on the
    /// locker and TAKE ALL, (a shot at the wall and the tool on the floor, harmless,) up the
    /// stairwell to the surface, 50 m away, back down into the bunker, and out again.
    /// </summary>
    public IEnumerable<int> WakeAndLeave(Action<bool, string> check, string label = "", bool shoot = false)
    {
        Slice.Start.Bunker b = Game.Bunker;
        Vector3 feet = Game.Survivor.WorldFeet;
        float under = Game.Zone.GroundAt(feet.X, feet.Z).Y - feet.Y;
        check(Game.Phase == Phase.Playing && !b.Mine.Open && feet.DistanceTo(b.CryoStand) < 0.6f,
            $"{label}wakes in the cryopod, its lid shut, {under:F1} m under the ground");
        foreach (int x in Drive.Tap(Key.F)) yield return x;
        foreach (int x in Drive.Wait(40)) yield return x;
        check(b.Mine.Open, $"{label}F opens the cryopod");
        foreach (int x in Drive.WalkTo(b.CryoFront, 0.3f, 10f)) yield return x;
        check(Game.Survivor.WorldFeet.DistanceTo(b.CryoStand) > 0.7f && Game.Survivor.Body.OnFloor, $"{label}and the castaway steps out into the chamber");

        foreach (int x in Drive.WalkTo(b.LockerFront, 0.4f, 15f)) yield return x;
        foreach (int x in Drive.LookAt(b.Locker, 8)) yield return x;
        foreach (int x in Drive.Tap(Key.E)) yield return x;
        foreach (int x in Drive.Wait(2)) yield return x;
        check(Game.Opened?.Vessel == b.Ruin && Game.Menu.IsOpen && Game.Menu.Tab == MenuTab.You, $"{label}E on the locker opens it beside the bag");
        foreach (int x in Drive.ClickWidget("box:take-all")) yield return x;
        var kit = Game.Zone.Data.Rules.Materials.StartKit;
        check(b.Ruin.Storage.IsEmpty && kit.All(kv => Pack[kv.Key] >= kv.Value),
            $"{label}TAKE ALL moves the kit into the bag: {string.Join(", ", kit.Select(kv => $"{Pack[kv.Key]} {kv.Key}"))}");
        foreach (int x in CloseMenu()) yield return x;

        if (shoot)
        {
            int blocks = b.Ruin.Structure.Count, bolts = Game.Shots.Bolts, impacts = Game.Shots.Impacts;
            Vector3 wall = b.At(new Vector3(3.25f, 1.4f, -1.0f));
            foreach (int x in ShootAt(() => wall)) yield return x;
            foreach (int x in Drive.Wait(20)) yield return x;
            check(Game.Shots.Bolts > bolts && Game.Shots.Impacts > impacts && Game.Shots.LastImpact.Kind == Slice.Player.ImpactKind.Block
                  && Game.Shots.LastImpact.At.DistanceTo(wall) < 1.2f && b.Ruin.Structure.Count == blocks && b.Ruin.Body.Damage == 0f,
                $"{label}a shot inside the bunker: a bolt from the muzzle and an impact on its wall {Game.Shots.LastImpact.At.DistanceTo(wall):F2} m from the aim, no harm done");
            int removed = Game.Zone.Planet.Terrain.Store.RemovedCount;
            foreach (int x in Tool(Slice.Player.ToolMode.Gather)) yield return x;
            foreach (int x in Drive.HoldPrimary(b.At(new Vector3(0f, 0.25f, -1.5f)), 30)) yield return x;
            check(Game.Zone.Planet.Terrain.Store.RemovedCount == removed && Game.Tool.OnBlock, $"{label}the tool inside digs nothing through its floor");
        }

        double start = Game.Stats.Seconds;
        foreach (int x in Drive.ClimbOut(b)) yield return x;
        GD.Print($"  (out: feet {Game.Survivor.WorldFeet}, the way out ends at {b.Outside}, ground there {Game.Zone.GroundAt(b.Outside.X, b.Outside.Z).Y:F2}, top step {b.StairTop})");
        check(Drive.OnSurface && Game.Survivor.WorldFeet.DistanceTo(b.Outside) < 2f && Game.Survivor.Body.OnFloor,
            $"{label}up the stairwell's {b.Steps} steps to the surface on foot ({Game.Stats.Seconds - start:F0} s)");
        // Away a step at a time over ground that is really there (caves, crevasses and lava the
        // map doesn't show are probed for), never far above or below the way out, as far from it
        // as each step allows; breadcrumbs on the way, to come back the way you went.
        var crumbs = new List<Vector3>();
        Vector3 away = b.Outside;
        // First along a way the site was chosen for (walkable 60 m), toward the place in view if one goes that way.
        if (b.Ways.Count > 0)
        {
            Vector3 toward = b.Landmark is Vector3 lm ? lm - b.Outside : b.Outside - b.StairTop;
            toward.Y = 0f;
            Vector3 way = b.Ways.OrderByDescending(w => w.Dot(toward.Normalized())).First();
            foreach (int x in Drive.WalkTo(b.Outside + way * 56f, 2f, 60f, sprint: false, dig: false))
            {
                if (crumbs.Count == 0 || crumbs[^1].DistanceTo(Game.Survivor.WorldFeet) > 3f) crumbs.Add(Game.Survivor.WorldFeet);
                yield return x;
            }
        }
        for (int step = 0; step < 40; step++)
        {
            Vector3 here = Game.Survivor.WorldFeet;
            if (new Vector2(here.X - b.Outside.X, here.Z - b.Outside.Z).Length() >= 53f) break;
            Vector3? next = null;
            float bestScore = float.MinValue;
            for (int k = 0; k < 24; k++)
            {
                float ang = k * MathF.Tau / 24f;
                Vector3 p = here + new Vector3(MathF.Cos(ang), 0f, MathF.Sin(ang)) * 4f;
                var probe = PhysicsRayQueryParameters3D.Create(new Vector3(p.X, here.Y + 20f, p.Z), new Vector3(p.X, here.Y - 20f, p.Z), GalHack.Physics.Layers.World);
                var hit = Game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(probe);
                if (hit.Count == 0) continue;
                float h = ((Vector3)hit["position"]).Y;
                if (h < 1.5f || MathF.Abs(h - b.Outside.Y) > 14f || MathF.Abs(h - here.Y) > 2.4f || b.Over(p, 3f)) continue;
                // Not at the lip of a drop: the ground a few metres on is there and not far below.
                Vector3 on = p + new Vector3(MathF.Cos(ang), 0f, MathF.Sin(ang)) * 3f;
                var lip = Game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(on.X, h + 20f, on.Z), new Vector3(on.X, h - 20f, on.Z), GalHack.Physics.Layers.World));
                if (lip.Count == 0 || ((Vector3)lip["position"]).Y < h - 2.5f) continue;
                float score = new Vector2(p.X - b.Outside.X, p.Z - b.Outside.Z).Length();
                if (score > bestScore) { bestScore = score; next = new Vector3(p.X, h, p.Z); }
            }
            if (next is not Vector3 n) break;
            crumbs.Add(here);
            away = n;
            foreach (int x in Drive.WalkTo(n, 0.8f, 10f, sprint: false, dig: false)) yield return x;
        }
        // Boxed in by the ground's shape short of 50 m: on the gentlest straight line out from there.
        Vector3 there = Game.Survivor.WorldFeet;
        if (new Vector2(there.X - b.Outside.X, there.Z - b.Outside.Z).Length() < 52f)
        {
            Vector3 dir = there - b.Outside;
            dir.Y = 0f;
            Vector3 far = AwayFrom(there, dir.LengthSquared() > 1f ? dir : b.Outside - b.StairTop, 56f - new Vector2(there.X - b.Outside.X, there.Z - b.Outside.Z).Length());
            foreach (int x in Drive.WalkTo(far, 2f, 60f, dig: false))
            {
                if (crumbs.Count == 0 || crumbs[^1].DistanceTo(Game.Survivor.WorldFeet) > 3f) crumbs.Add(Game.Survivor.WorldFeet);
                if (new Vector2(Game.Survivor.WorldFeet.X - b.Outside.X, Game.Survivor.WorldFeet.Z - b.Outside.Z).Length() >= 52f) break;
                yield return x;
            }
            Controls.ReleaseAll();
        }
        Vector3 f = Game.Survivor.WorldFeet;
        float gone = new Vector2(f.X - b.Outside.X, f.Z - b.Outside.Z).Length();
        if (gone < 50f) GD.Print($"  (walking away: from {b.Outside} toward {away}, stopped at {f}, on floor {Game.Survivor.Body.OnFloor}, the ground there {Game.Zone.GroundAt(f.X, f.Z).Y:F1}, inside the bunker {b.Inside(f, Game.Zone)})");
        check(gone >= 50f, $"{label}and walks {gone:F0} m away from it (feet {f.Y - Game.Zone.GroundAt(f.X, f.Z).Y:+0.0;-0.0} m from the ground)");
        bool told = false;
        for (int i = crumbs.Count - 1; i >= 0; i--)
            foreach (int x in Drive.WalkTo(crumbs[i], 1.0f, 20f, sprint: false, dig: false))
            {
                if (!told && Game.Survivor.WorldFeet.Y < crumbs[i].Y - 4f)
                {
                    told = true;
                    GD.Print($"  (coming back: dropped below the way out at crumb {i} of {crumbs.Count} ({crumbs[i]}): feet {Game.Survivor.WorldFeet}; the way out's last crumbs {string.Join(" ", crumbs.Take(4))})");
                }
                yield return x;
            }
        foreach (int x in Drive.WalkTo(b.Outside, 0.8f, 90f, dig: false)) yield return x;
        foreach (int x in Drive.ClimbIn(b)) yield return x;
        check(Game.Survivor.WorldFeet.DistanceTo(b.CryoFront) < 1.0f, $"{label}back down the stairs into the chamber");
        foreach (int x in Drive.ClimbOut(b)) yield return x;
        check(Drive.OnSurface && Game.Survivor.WorldFeet.DistanceTo(b.Outside) < 2f, $"{label}and out again, with no snag");
    }

    /// <summary>A point a walk away over dry, gentle ground: the way you face first, then round from it.</summary>
    public Vector3 AwayFrom(Vector3 from, Vector3 facing, float distance)
    {
        facing.Y = 0f;
        float a0 = MathF.Atan2(facing.Z, facing.X), bestScore = float.MaxValue;
        Vector3 best = from + facing.Normalized() * distance;
        for (int i = 0; i < 32; i++)
        {
            float a = a0 + (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * MathF.Tau / 32f;
            var dir = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            // The real ground (the terrain's own collision: caves, crevasses and ice the map
            // doesn't show), probed from well above: gentle all the way, never far below or above
            // where you start, so the way back is the way you went.
            float start = from.Y, worst = 0f, last = start;
            bool wet = false;
            for (float d = 2f; d <= distance + 4f; d += 2f)
            {
                Vector3 p = from + dir * d;
                var probe = PhysicsRayQueryParameters3D.Create(new Vector3(p.X, start + 40f, p.Z), new Vector3(p.X, start - 40f, p.Z), GalHack.Physics.Layers.World);
                var hit = Game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(probe);
                float h = hit.Count > 0 ? ((Vector3)hit["position"]).Y : start - 40f;
                wet |= h < 1.5f || hit.Count == 0 || MathF.Abs(h - start) > 4f;
                // Not back over the bunker: its stairwell is a trench.
                if (Game.Bunker != null && Game.Bunker.Over(p, 3f)) wet = true;
                worst = MathF.Max(worst, MathF.Abs(h - last) / 2f);
                last = h;
            }
            float score = (wet ? 100f : 0f) + (worst > 0.45f ? 50f : 0f) + worst + i * 0.01f;
            if (score < bestScore) { bestScore = score; best = from + dir * distance; }
        }
        return best;
    }

    /// <summary>
    /// Clicks a widget on an in-world screen as a player does: look at the screen (a cursor
    /// appears where you aim), page with its arrow until the widget shows, aim at it and click.
    /// </summary>
    public IEnumerable<int> ScreenClick(MachineScreens.Mounted m, string id, string next = null, int pages = 12)
    {
        for (int i = 0; i < 8 && m.Panel.RectOf(id) == null && (next == null || m.Panel.RectOf(next) == null); i++)
        {
            Drive.Face(m.Screen.GlobalPosition + Vector3.Up * (i % 2 == 0 ? 0.01f : -0.01f));
            yield return 0;
        }
        for (int p = 0; p < pages && m.Panel.RectOf(id) == null && next != null && m.Panel.RectOf(next) != null; p++)
            foreach (int x in ScreenPress(m, next)) yield return x;
        foreach (int x in ScreenPress(m, id)) yield return x;
    }

    IEnumerable<int> ScreenPress(MachineScreens.Mounted m, string id)
    {
        if (MachineScreens.WidgetPoint(m, id) is not Vector3 at)
        {
            Log($"(no '{id}' on the screen: {string.Join(", ", m.Panel.WidgetIds.Take(12))})");
            yield break;
        }
        foreach (int x in Drive.LookAt(at, 5)) yield return x;
        if (Game.MachineScreens.Pointer.Target != m.Screen) Log($"(aiming at '{id}' on the screen, the cursor is on {Game.MachineScreens.Pointer.Target?.Name ?? "nothing"}; {Game.Survivor.WorldEye().DistanceTo(at):F1} m away)");
        Controls.PrimaryPressed = true;
        Controls.Primary = true;
        yield return 0;
        Controls.Primary = false;
        Controls.PrimaryReleased = true;
        yield return 0;
        yield return 0;
    }

    /// <summary>The bag slot holding an item, or -1.</summary>
    public int BagSlotOf(string itemId)
    {
        Pack.Grid.Reconcile();
        for (int i = PlayerInventory.ToolbarSlots; i < Pack.Grid.Count; i++)
            if (Pack.Grid[i].Item == itemId) return i;
        return -1;
    }

    /// <summary>Drags an item from the bag onto a toolbar slot (YOU tab).</summary>
    public IEnumerable<int> ToToolbar(string itemId, int slot)
    {
        foreach (int x in OpenMenu(MenuTab.You)) yield return x;
        int from = BagSlotOf(itemId);
        if (from < 0) throw new InvalidOperationException($"no {itemId} in the bag to put on the toolbar");
        foreach (int x in Drive.DragWidget($"bag:{from}", $"bar:{slot}")) yield return x;
    }

    /// <summary>Drags a block from the build palette onto a toolbar slot (YOU tab, build palette).</summary>
    public IEnumerable<int> BlockToToolbar(string blockId, bool small, int slot)
    {
        foreach (int x in OpenMenu(MenuTab.You)) yield return x;
        if (Game.Menu.Root.You.ShowBlocks == false) foreach (int x in Drive.ClickWidget("you:blocks")) yield return x;
        foreach (int x in PageToStart("palette:up")) yield return x;
        string id = $"palette:{blockId}@{(small ? "small" : "large")}";
        for (int p = 0; p < 12 && Game.Menu.RectOf(id) == null; p++)
            foreach (int x in Drive.ClickWidget("palette:down")) yield return x;
        foreach (int x in Drive.DragWidget(id, $"bar:{slot}")) yield return x;
    }

    /// <summary>Orders items at a constructor's panel (the fabricator on YOU, or the CORE tab's), paging to the item's row.</summary>
    public IEnumerable<int> Order(string key, string itemId, int count)
    {
        string id = $"order:{key}:{itemId}:{count}";
        foreach (int x in PageToStart($"{key}:up")) yield return x;
        foreach (int x in ClickPaged(id, $"{key}:down")) yield return x;
    }

    /// <summary>Orders any number by clicking +10, +5 and +1 as a player would.</summary>
    public IEnumerable<int> OrderMany(string key, string itemId, int count)
    {
        foreach (int step in new[] { 10, 5, 1 })
            for (; count >= step; count -= step)
                foreach (int x in Order(key, itemId, step)) yield return x;
    }

    /// <summary>
    /// Makes sure a bill's worth is on hand (in a storage and the bag together): orders what is
    /// short at a constructor's panel and waits for it, again if something was used up on the way,
    /// as a player re-orders what is still missing.
    /// </summary>
    public IEnumerable<int> MakeAll(string key, GalHack.Items.Production.Constructor c, SharedStorage at, Bill bill, float seconds)
    {
        for (int round = 0; round < 4; round++)
        {
            var shortfall = new Bill();
            foreach (var (id, n) in bill)
            {
                int have = at.Count(id) + Pack[id];
                if (have < n) shortfall.Add(id, n - have);
            }
            if (shortfall.IsEmpty) yield break;
            foreach (var (id, n) in shortfall)
                foreach (int x in OrderMany(key, id, n)) yield return x;
            foreach (int x in WaitFor(() => !c.Busy, seconds)) yield return x;
        }
    }

    /// <summary>Unlocks a tech-tree node on the TECH tab by clicking it.</summary>
    public IEnumerable<int> Unlock(string nodeId)
    {
        TechNode node = Game.Progress.Node(nodeId);
        foreach (int x in OpenMenu(MenuTab.Tech)) yield return x;
        foreach (int x in Drive.ClickWidget($"tech:tab:{node.Tab}")) yield return x;
        foreach (int x in Drive.ClickWidget($"tech:{nodeId}")) yield return x;
    }

    // ---- Hands ---------------------------------------------------------------------------------

    public IEnumerable<int> Select(int slot)
    {
        if (Pack.Selected != slot) foreach (int x in Drive.Tap(Key.Key1 + slot)) yield return x;
        foreach (int x in Drive.Wait(1)) yield return x;
    }

    /// <summary>The survival tool in hand, in a mode (Q switches).</summary>
    public IEnumerable<int> Tool(Slice.Player.ToolMode mode)
    {
        int slot = Enumerable.Range(0, PlayerInventory.ToolbarSlots).First(i => Pack.Grid[i].Item == "survival-tool");
        foreach (int x in Select(slot)) yield return x;
        if (Game.Tool.Mode != mode) foreach (int x in Drive.Tap(Key.Q)) yield return x;
        foreach (int x in Drive.Wait(1)) yield return x;
    }

    /// <summary>Fires the tool at a point: look, click, let the bolt fly.</summary>
    public IEnumerable<int> ShootAt(Func<Vector3> at, int shots = 1)
    {
        foreach (int x in Tool(Slice.Player.ToolMode.Shoot)) yield return x;
        for (int s = 0; s < shots; s++)
        {
            foreach (int x in Drive.LookAt(at(), 4)) yield return x;
            Controls.Primary = true;
            Controls.PrimaryPressed = true;
            Drive.Face(at());
            yield return 0;
            Controls.Primary = false;
            foreach (int x in Drive.Wait(14)) yield return x;
        }
    }

    // ---- Gathering ---------------------------------------------------------------------------

    readonly HashSet<(int, int)> _triedDeposits = new();

    /// <summary>The nearest deposit of a kind not dug at yet, on dry ground (its ore body's centre), or null.</summary>
    public Vector3? NextDeposit(DepositKind kind, float within = 700f, bool retry = false)
    {
        SurfaceZone zone = Game.Zone;
        Vector3 from = Game.Survivor.WorldFeet;
        Vector3? best = null;
        float bestD = within;
        foreach (Deposit d in zone.Planet.Map.Deposits.InRect(from.X - within, from.Z - within, from.X + within, from.Z + within))
        {
            if (d.Kind != kind) continue;
            zone.Planet.Source.OreCentre(d, out double ox, out double oy, out double oz);
            Vector3 at = zone.Planet.NearestCopy(new Vector3((float)ox, (float)oy, (float)oz), from);
            // Dry, and near enough the surface for the survival tool (it can't cut deep rock).
            if ((!retry && _triedDeposits.Contains(((int)at.X, (int)at.Z))) || zone.GroundAt(at.X, at.Z).Y < 0.5f || zone.GroundAt(at.X, at.Z).Y - at.Y > 5f) continue;
            if (retry && NearestOre(at, at, kind) == null) continue; // dug out
            // Not in or beside a place (its rooms are dug into the ground under it).
            if (zone.Planet.Pois.Any(poi => new Vector2(poi.Origin.X - at.X, poi.Origin.Z - at.Z).Length() < 60f)) continue;
            if (Game.Bunker != null && Game.Bunker.WayOut.Append(Game.Bunker.RoomCentre).Any(w => w.DistanceTo(at) < 10f)) continue; // dug through by the bunker
            float dist = new Vector2(at.X - from.X, at.Z - from.Z).Length();
            if (dist < bestD) { bestD = dist; best = at; }
        }
        return best;
    }

    /// <summary>Digs ore of a kind out of the nearest deposits until the bag holds enough.</summary>
    public IEnumerable<int> Gather(DepositKind kind, int enough, float within = 700f, bool retry = false)
    {
        ItemDef ore = Game.Tool.OreOf(kind);
        foreach (int x in Tool(Slice.Player.ToolMode.Gather)) yield return x;
        for (int deposit = 0; deposit < 8 && Pack[ore.Id] < enough; deposit++)
        {
            if (NextDeposit(kind, within, retry) is not Vector3 at)
            {
                Log($"no more {kind} deposits within 700 m");
                yield break;
            }
            _triedDeposits.Add(((int)at.X, (int)at.Z));
            foreach (int x in Drive.WalkTo(at, 3.5f, 90f)) yield return x;
            int idle = 0, got0 = Pack[ore.Id];
            var store = Game.Zone.Planet.Terrain.Store;
            for (int pulse = 0; pulse < 240 && idle < 12 && Pack[ore.Id] < enough; pulse++)
            {
                Vector3 eye = Game.Survivor.WorldEye();
                Vector3? target = NearestOre(at, eye, kind);
                if (target is not Vector3 t) break;
                // Digging down through soil and rock to a buried bed is progress too.
                int before = Pack[ore.Id] + store.RemovedCount;
                // Walk over a deposit; one buried under the feet is dug down to, a ball at a time.
                if (new Vector2(t.X - eye.X, t.Z - eye.Z).Length() > 4f && eye.DistanceTo(t) > 6.5f)
                    foreach (int x in Drive.WalkTo(t, 1.2f, 5f)) yield return x;
                else
                {
                    foreach (int x in Drive.HoldPrimary(t, 2)) yield return x;
                    for (int i = 0; i < 10; i++) yield return 0;
                }
                idle = Pack[ore.Id] + store.RemovedCount > before ? 0 : idle + 1;
                if (idle == 3)
                    Log($"(digging at {t}: eye {eye} {eye.DistanceTo(t):F1} m off; the tool's target {(Game.Tool.HasTarget ? Game.Tool.Target.ToString() : "none")}, {Game.Tool.TargetVerdict}, on a block {Game.Tool.OnBlock}, hand {Game.Pack.Hand}, mode {Game.Tool.Mode}, dead {Game.Suit.Dead}, menu {Game.Menu.IsOpen}, screen {Game.MachineScreens.Pointer.Target?.Name})");
            }
            Log($"({kind} deposit {deposit + 1}: +{Pack[ore.Id] - got0}, {Pack[ore.Id]}/{enough})");
        }
    }
    /// <summary>The nearest cell of a kind of ore still in the ground round a deposit's centre.</summary>
    public Vector3? NearestOre(Vector3 centre, Vector3 eye, DepositKind kind)
    {
        var store = Game.Zone.Planet.Terrain.Store;
        Vector3? best = null;
        float bestD = float.MaxValue;
        int cx = (int)MathF.Floor(centre.X), cy = (int)MathF.Floor(centre.Y), cz = (int)MathF.Floor(centre.Z);
        for (int y = -6; y <= 6; y++)
        for (int z = -6; z <= 6; z++)
        for (int x = -6; x <= 6; x++)
        {
            var cell = new TerrainDigging.Terrain.Int3(cx + x, cy + y, cz + z);
            if (store.MaterialAt(cell) != TerrainDigging.Terrain.CellMaterial.Ore || Game.Zone.Planet.Source.OreKindAt(cell) != kind) continue;
            var p = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            float d = p.DistanceTo(eye);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    /// <summary>Walks to the nearest plant and cuts it.</summary>
    public IEnumerable<int> CutPlant()
    {
        foreach (int x in Tool(Slice.Player.ToolMode.Gather)) yield return x;
        var tried = new HashSet<long>();
        int cut = Game.Tool.PlantsCut;
        // The nearest plant you can walk up to; the next one if a bank or a cliff is in the way.
        for (int attempt = 0; attempt < 4 && Game.Tool.PlantsCut == cut; attempt++)
        {
            Vector3 feet = Game.Survivor.WorldFeet;
            var plant = Game.Zone.Streamer.FloraNear(feet, 150f)
                .Where(p => p.Root.Y > 0.6f && !tried.Contains(p.Id) && MathF.Abs(p.Root.Y - feet.Y) < 4f)
                .OrderBy(p => p.Root.DistanceTo(feet)).FirstOrDefault();
            if (plant.Id == 0) yield break;
            tried.Add(plant.Id);
            foreach (int x in Drive.WalkTo(plant.Root, 1.8f, 40f)) yield return x;
            for (int i = 0; i < 6 && Game.Tool.PlantsCut == cut; i++)
            {
                Drive.Face(plant.Root + Vector3.Up * 0.9f);
                Controls.Primary = true;
                yield return 0;
            }
            Controls.Primary = false;
        }
        foreach (int x in Drive.Wait(20)) yield return x;
    }

    // ---- Placing -----------------------------------------------------------------------------

    /// <summary>Puts what is on a toolbar slot down on the ground at a point (a core or a device).</summary>
    public IEnumerable<int> PlaceOnGround(int slot, Vector3 at)
    {
        // Within reach of the spot first (the build tool reaches 9 m).
        if (Game.Survivor.WorldEye().DistanceTo(at) > 7f) foreach (int x in Drive.WalkTo(at, 4.5f, 30f)) yield return x;
        foreach (int x in Select(slot)) yield return x;
        foreach (int x in Drive.LookAt(at, 8)) yield return x;
        bool seen = WorldRay.Cast(Game.Zone, Game.Survivor, 20f, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull, out LookHit look);
        if (!seen || look.Point.DistanceTo(at) > 1.5f)
            Log($"(placing on the ground at {at}: the look from {Game.Survivor.WorldEye()} {(seen ? $"meets {look.Collider} on {look.Vessel?.Name} at {look.Point}, {look.Distance:F1} m" : "meets nothing")}; aboard {Game.Survivor.Aboard?.Name}, menu {Game.Menu.IsOpen})");
        foreach (int x in Drive.Click()) yield return x;
        foreach (int x in Drive.Wait(3)) yield return x;
    }

    /// <summary>
    /// Places the block on a toolbar slot against one face of a structure's block, by looking at
    /// that face and clicking, from where the castaway stands. True if a block went down there.
    /// </summary>
    public IEnumerable<int> PlaceBlock(int slot, Vessel v, Int3 on, Dir face, Action<bool> placed = null)
    {
        foreach (int x in Select(slot)) yield return x;
        Int3 target = on + face.Vec();
        float cell = v.Structure.Grid.CellSize;
        N.Vector3 local = StructureBridge.CellCenter(v.Structure, on) + face.Vec().ToVector3() * (cell * 0.49f);
        int before = v.Structure.Count;
        foreach (int x in Drive.LookAt(v.ToWorld(local), 6)) yield return x;
        foreach (int x in Drive.Click()) yield return x;
        foreach (int x in Drive.Wait(2)) yield return x;
        bool ok = v.Structure.Has(target) && v.Structure.Count > before;
        if (!ok) Log($"(placing at {target} on {v.Name}: {Game.Builder.Verdict})");
        placed?.Invoke(ok);
    }

    /// <summary>Turns the block in hand with R and T until it faces a way (tapped like a player: the fewest taps that get there).</summary>
    public IEnumerable<int> TurnTo(int rotation)
    {
        var seen = new Dictionary<int, List<Key>> { [Game.Builder.Rot] = new() };
        var queue = new Queue<int>();
        queue.Enqueue(Game.Builder.Rot);
        while (queue.Count > 0 && !seen.ContainsKey(rotation))
        {
            int r = queue.Dequeue();
            foreach (var (key, axis) in new[] { (Key.R, 1), (Key.T, 0) })
            {
                int next = Orientations.Rotate(r, axis, 1);
                if (seen.ContainsKey(next)) continue;
                seen[next] = new List<Key>(seen[r]) { key };
                queue.Enqueue(next);
            }
        }
        foreach (Key k in seen.TryGetValue(rotation, out var keys) ? keys : new List<Key>())
            foreach (int x in Drive.Tap(k)) yield return x;
    }

    /// <summary>The block turn whose thrust pushes along a direction (a thruster's push is its -z).</summary>
    public static int ThrustingAlong(Dir push)
    {
        for (int o = 0; o < Orientations.Count; o++)
            if (Orientations.ToWorld(o, Dir.NegZ) == push && (push is Dir.PosY or Dir.NegY ? Orientations.ToWorld(o, Dir.PosY) is Dir.PosZ or Dir.NegZ : Orientations.ToWorld(o, Dir.PosY) == Dir.PosY)) return o;
        return Orientations.Identity;
    }

    /// <summary>
    /// The small vessel the real-player test and the screenshots build on a core: a deck of a
    /// generator, struts and a gyro round the core, a main engine at the back, a seat on top
    /// (reached with a hop on the jetpack), and under the outer blocks two lift jets, a fuel tank
    /// and three legs, all reachable from outside the vessel's footprint. Each
    /// block goes on a face its mounting allows (a seat by its base, a fuel tank top or bottom, a
    /// thruster not by its nozzle, a leg by its top).
    /// </summary>
    public static readonly (string block, Int3 on, Dir face, Dir? push)[] VesselLayout =
    {
        // The deck round the core, from its sides.
        ("generator", Int3.Zero, Dir.PosX, null), ("steel", Int3.Zero, Dir.NegX, null), ("steel", Int3.Zero, Dir.NegZ, null),
        ("steel", new Int3(0, 0, -1), Dir.NegZ, null), ("gyro", Int3.Zero, Dir.PosZ, null), ("thruster", new Int3(0, 0, 1), Dir.PosZ, Dir.NegZ),
        ("steel", new Int3(0, 0, 1), Dir.PosX, null), ("steel", new Int3(0, 0, 1), Dir.NegX, null),
        // A retro at the nose, to brake.
        ("thruster", new Int3(0, 0, -2), Dir.NegZ, Dir.PosZ),
        // On top, from hops: the seat, and a jet pushing down, to come down under control.
        ("cockpit", Int3.Zero, Dir.PosY, null), ("thruster", new Int3(1, 0, 0), Dir.PosY, Dir.NegY),
        // Under the outer blocks, from outside: lift jets, the fuel tank, three legs.
        ("thruster", new Int3(1, 0, 0), Dir.NegY, Dir.PosY), ("thruster", new Int3(-1, 0, 0), Dir.NegY, Dir.PosY),
        ("fueltank", new Int3(-1, 0, 1), Dir.NegY, null), ("gear", new Int3(1, 0, 1), Dir.NegY, null),
        ("gear", new Int3(0, 0, -2), Dir.NegY, null), ("gear", new Int3(-1, -1, 1), Dir.NegY, null),
    };

    /// <summary>Builds <see cref="VesselLayout"/> on a vessel's core, from toolbar slots by block, walking round it to see each face. Counts what went down.</summary>
    public IEnumerable<int> BuildVessel(Vessel v, IReadOnlyDictionary<string, int> slots, Action<int> placed)
    {
        int ok = 0;
        Vector3 centre = v.Body.Position.ToGodot();
        foreach (var (block, on, face, push) in VesselLayout)
        {
            N.Vector3 cell = StructureBridge.CellCenter(v.Structure, on);
            Vector3 at = v.ToWorld(cell);
            // A side face from out in front of it; a bottom face from just beside the block,
            // looking up under it; a top face from beside it, on a hop of the jetpack.
            Vector3 outward = face is Dir.NegY or Dir.PosY ? v.ToWorld(new N.Vector3(on.X, 0, on.Z) * 2f + new N.Vector3(on.X == 0 && on.Z == 0 ? -1 : 0, 0, 0)) - centre
                : v.ToWorld(face.Vec().ToVector3()) - centre;
            outward.Y = 0f;
            outward = outward.LengthSquared() < 1e-4f ? Vector3.Back : outward.Normalized();
            Int3 target = on + face.Vec();
            bool done = false;
            // Where to stand: tried nearer and further until the crosshair is on that very face,
            // as a player shuffles until the ghost sits where they want it.
            float[] distances = face == Dir.NegY ? new[] { 0.1f, 0.75f, 1.1f } : face == Dir.PosY ? new[] { 1.6f, 1.2f, 2.2f } : new[] { 2.4f, 1.8f, 3.2f };
            foreach (float d in distances)
            {
                if (done) break;
                Vector3 stand = at + outward * d;
                foreach (int x in WalkAround(centre, new Vector3(stand.X, 0, stand.Z), 4.5f)) yield return x;
                foreach (int x in Select(slots[block])) yield return x;
                foreach (int x in TurnTo(push is Dir along ? ThrustingAlong(along) : Orientations.Identity)) yield return x;
                if (face == Dir.PosY) foreach (int x in HopAndPlace(slots[block], v, on, face, p => done = p)) yield return x;
                else
                {
                    N.Vector3 aim = cell + face.Vec().ToVector3() * (v.Structure.Grid.CellSize * 0.49f);
                    foreach (int x in Drive.LookAt(v.ToWorld(aim), 6)) yield return x;
                    if (Game.Builder.TargetVessel != v || Game.Builder.PlaceCell != target) continue;
                    foreach (int x in PlaceBlock(slots[block], v, on, face, p => done = p)) yield return x;
                }
            }
            if (done) ok++;
            else Log($"(could not place the {block} at {target}: the crosshair was on {Game.Builder.TargetCell}, placing at {Game.Builder.PlaceCell}, \"{Game.Builder.Verdict}\")");
        }
        placed(ok);
    }

    /// <summary>A top face out of sight from the ground: up on the jetpack, looking down at it, and a click at the top of the hop.</summary>
    public IEnumerable<int> HopAndPlace(int slot, Vessel v, Int3 on, Dir face, Action<bool> placed)
    {
        float cell = v.Structure.Grid.CellSize;
        N.Vector3 local = StructureBridge.CellCenter(v.Structure, on) + face.Vec().ToVector3() * (cell * 0.49f);
        Int3 target = on + face.Vec();
        bool done = false;
        for (int attempt = 0; attempt < 3 && !done; attempt++)
        {
            int before = v.Structure.Count;
            bool clicked = false;
            // Up on the jetpack, watching the ghost: click only once it sits on that face, as a
            // player would (a click on anything else puts the block somewhere unwanted).
            Controls.Hold(Key.Space);
            for (int i = 0; i < 90 && !clicked; i++)
            {
                Drive.Face(v.ToWorld(local));
                yield return 0;
                bool high = Game.Survivor.WorldEye().Y > v.ToWorld(local).Y + 0.4f;
                if (high && Game.Builder.TargetVessel == v && Game.Builder.PlaceCell == target)
                {
                    Drive.Face(v.ToWorld(local));
                    Controls.PrimaryPressed = true;
                    Controls.Primary = true;
                    clicked = true;
                    yield return 0;
                    Controls.Primary = false;
                }
            }
            if (!clicked) Log($"(the hop's crosshair: on {Game.Builder.TargetCell} of {Game.Builder.TargetVessel?.Name}, placing at {Game.Builder.PlaceCell}, eye {Game.Survivor.WorldEye().Y - v.ToWorld(local).Y:F2} m above the face)");
            Controls.Hold(Key.Space, false);
            foreach (int x in WaitFor(() => Game.Survivor.Body.OnFloor, 5)) yield return x;
            done = v.Structure.Has(target) && v.Structure.Count > before;
        }
        if (!done) Log($"(placing at {target} on {v.Name} from a hop: {Game.Builder.Verdict})");
        placed(done);
    }
    /// <summary>Walks round something (a structure) at a radius to reach a point on its far side, the way a person walks round a building rather than into it.</summary>
    public IEnumerable<int> WalkAround(Vector3 centre, Vector3 to, float radius)
    {
        Vector3 from = Game.Survivor.WorldFeet;
        float a0 = MathF.Atan2(from.Z - centre.Z, from.X - centre.X), a1 = MathF.Atan2(to.Z - centre.Z, to.X - centre.X);
        float turn = Mathf.Wrap(a1 - a0, -MathF.PI, MathF.PI);
        int steps = (int)MathF.Ceiling(MathF.Abs(turn) / (MathF.PI / 4f));
        for (int i = 1; i <= steps; i++)
        {
            float a = a0 + turn * i / steps;
            var p = new Vector3(centre.X + MathF.Cos(a) * radius, 0f, centre.Z + MathF.Sin(a) * radius);
            foreach (int x in Drive.WalkTo(p, 1.2f, 15f)) yield return x;
        }
        foreach (int x in Drive.WalkTo(to, 0.6f, 15f)) yield return x;
    }

    /// <summary>Walks to stand a few metres from a point on the side facing away from another point.</summary>
    public IEnumerable<int> StandOff(Vector3 at, Vector3 from, float distance)
    {
        Vector3 dir = at - from;
        dir.Y = 0f;
        dir = dir.LengthSquared() < 1e-3f ? Vector3.Forward : dir.Normalized();
        Vector3 stand = at + dir * distance;
        foreach (int x in Drive.WalkTo(stand, 0.8f, 40f)) yield return x;
    }

/// <summary>A flat, dry spot about this far from a point, clear of structures and devices (for a base or a vessel). Flatter first, then less flat.</summary>
    public Vector3 FlatSpot(Vector3 near, float distance, float startAngle = 0f, float spread = 0.35f)
    {
        SurfaceZone zone = Game.Zone;
        foreach (float allow in new[] { spread, spread * 2f, spread * 4f, 4f })
            for (int i = 0; i < 96; i++)
            {
                float a = startAngle + i * 0.61f, r = distance + (i / 16) * 6f;
                var p = new Vector3(near.X + MathF.Cos(a) * r, 0f, near.Z + MathF.Sin(a) * r);
                float h0 = zone.GroundAt(p.X, p.Z).Y, worst = 0f;
                for (int k = 0; k < 8; k++)
                {
                    float b = k * MathF.Tau / 8f;
                    worst = MathF.Max(worst, MathF.Abs(zone.GroundAt(p.X + MathF.Cos(b) * 5f, p.Z + MathF.Sin(b) * 5f).Y - h0));
                }
                Vector3 g = zone.GroundAt(p.X, p.Z);
                bool clear = zone.Vessels.All(v => v.IsWreck || v.IsRuin || v.Body.Position.ToGodot().DistanceTo(g) > 14f) && zone.Devices.All(d => d.GlobalPosition.DistanceTo(g) > 8f)
                             && (Game.Bunker == null || !Game.Bunker.Over(g, 8f));
                if (h0 > 1f && worst < allow && clear) return g;
            }
        return zone.GroundAt(near.X + distance, near.Z);
    }
}
