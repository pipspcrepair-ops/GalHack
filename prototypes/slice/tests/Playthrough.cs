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

    /// <summary>Digs ore of a kind out of the nearest deposits until the bag holds enough.</summary>
    public IEnumerable<int> Gather(DepositKind kind, int enough)
    {
        ItemDef ore = Game.Tool.OreOf(kind);
        foreach (int x in Tool(Slice.Player.ToolMode.Gather)) yield return x;
        for (int deposit = 0; deposit < 6 && Pack[ore.Id] < enough; deposit++)
        {
            Vector3? found = DepositFinder.Nearest(Game.Zone, kind, Game.Survivor.WorldFeet, 700f);
            if (found is not Vector3 at)
            {
                Log($"no {kind} deposit within 700 m");
                yield break;
            }
            foreach (int x in Drive.WalkTo(at, 3.5f, 120f)) yield return x;
            int idle = 0;
            for (int pulse = 0; pulse < 220 && idle < 14 && Pack[ore.Id] < enough; pulse++)
            {
                Vector3 eye = Game.Survivor.WorldEye();
                Vector3? target = NearestOre(at, eye, kind);
                if (target is not Vector3 t) break;
                if (eye.DistanceTo(t) > 6.5f)
                {
                    foreach (int x in Drive.WalkTo(t, 1.2f, 6f)) yield return x;
                    continue;
                }
                int before = Pack[ore.Id];
                foreach (int x in Drive.HoldPrimary(t, 2)) yield return x;
                for (int i = 0; i < 10; i++) yield return 0;
                idle = Pack[ore.Id] > before ? 0 : idle + 1;
            }
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
        Vector3 feet = Game.Survivor.WorldFeet;
        var plant = Game.Zone.Streamer.FloraNear(feet, 150f).Where(p => p.Root.Y > 0.6f).OrderBy(p => p.Root.DistanceTo(feet)).FirstOrDefault();
        if (plant.Id == 0) yield break;
        foreach (int x in Drive.WalkTo(plant.Root, 1.8f, 40f)) yield return x;
        int cut = Game.Tool.PlantsCut;
        for (int i = 0; i < 6 && Game.Tool.PlantsCut == cut; i++)
        {
            Drive.Face(plant.Root + Vector3.Up * 0.9f);
            Controls.Primary = true;
            yield return 0;
        }
        Controls.Primary = false;
        foreach (int x in Drive.Wait(20)) yield return x;
    }

    // ---- Placing -----------------------------------------------------------------------------

    /// <summary>Puts what is on a toolbar slot down on the ground at a point (a core or a device).</summary>
    public IEnumerable<int> PlaceOnGround(int slot, Vector3 at)
    {
        foreach (int x in Select(slot)) yield return x;
        foreach (int x in Drive.LookAt(at, 8)) yield return x;
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
            if (Orientations.ToWorld(o, Dir.NegZ) == push && Orientations.ToWorld(o, Dir.PosY) is Dir.PosZ or Dir.NegZ or Dir.PosY) return o;
        return Orientations.Identity;
    }

    /// <summary>
    /// The small vessel the real-player test and the screenshots build on a core: a seat at the
    /// front, a generator and a fuel tank to the sides, a gyro behind, a main engine at the back,
    /// two lift jets under the sides, two struts, and three legs. Every block goes on a side or
    /// bottom face, which a player standing by the core on its build stand can see.
    /// </summary>
    public static readonly (string block, Int3 on, Dir face, bool lift)[] VesselLayout =
    {
        ("cockpit", Int3.Zero, Dir.NegZ, false), ("generator", Int3.Zero, Dir.PosX, false), ("fueltank", Int3.Zero, Dir.NegX, false), ("gyro", Int3.Zero, Dir.PosZ, false),
        ("thruster", new Int3(0, 0, 1), Dir.PosZ, false), ("thruster", new Int3(1, 0, 0), Dir.NegY, true), ("thruster", new Int3(-1, 0, 0), Dir.NegY, true),
        ("steel", new Int3(0, 0, 1), Dir.PosX, false), ("steel", new Int3(0, 0, 1), Dir.NegX, false),
        ("gear", new Int3(0, 0, -1), Dir.NegY, false), ("gear", new Int3(1, 0, 1), Dir.NegY, false), ("gear", new Int3(-1, 0, 1), Dir.NegY, false),
    };

    /// <summary>Builds <see cref="VesselLayout"/> on a vessel's core, from toolbar slots by block, walking round it to see each face. Counts what went down.</summary>
    public IEnumerable<int> BuildVessel(Vessel v, IReadOnlyDictionary<string, int> slots, Action<int> placed)
    {
        int ok = 0;
        Vector3 centre = v.Body.Position.ToGodot();
        foreach (var (block, on, face, lift) in VesselLayout)
        {
            N.Vector3 cell = StructureBridge.CellCenter(v.Structure, on);
            Vector3 at = v.ToWorld(cell);
            // Out from the face for a side face, out from the block for a bottom one (looking up under it).
            Vector3 outward = face == Dir.NegY ? v.ToWorld(new N.Vector3(on.X, 0, on.Z) * 2f + new N.Vector3(0, 0, on.X == 0 && on.Z == 0 ? -1 : 0)) - centre
                : v.ToWorld(face.Vec().ToVector3()) - centre;
            outward.Y = 0f;
            outward = outward.LengthSquared() < 1e-4f ? Vector3.Back : outward.Normalized();
            Vector3 stand = at + outward * (face == Dir.NegY ? 1.4f : 2.4f);
            foreach (int x in Drive.WalkTo(new Vector3(stand.X, 0, stand.Z), 0.5f, 25f)) yield return x;
            foreach (int x in Select(slots[block])) yield return x;
            foreach (int x in TurnTo(lift ? ThrustingAlong(Dir.PosY) : Orientations.Identity)) yield return x;
            bool done = false;
            foreach (int x in PlaceBlock(slots[block], v, on, face, p => done = p)) yield return x;
            if (done) ok++;
        }
        placed(ok);
    }

    /// <summary>Walks to stand a few metres from a point on the side facing away from another point.</summary>
    public IEnumerable<int> StandOff(Vector3 at, Vector3 from, float distance)
    {
        Vector3 dir = at - from;
        dir.Y = 0f;
        dir = dir.LengthSquared() < 1e-3f ? Vector3.Forward : dir.Normalized();
        Vector3 stand = at - dir * distance;
        foreach (int x in Drive.WalkTo(stand, 0.8f, 40f)) yield return x;
    }

    /// <summary>A flat, dry spot this far from a point (for a base or a vessel).</summary>
    public Vector3 FlatSpot(Vector3 near, float distance, float startAngle = 0f, float spread = 0.35f)
    {
        SurfaceZone zone = Game.Zone;
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
            if (h0 > 1f && worst < spread) return zone.GroundAt(p.X, p.Z);
        }
        return zone.GroundAt(near.X + distance, near.Z);
    }
}
