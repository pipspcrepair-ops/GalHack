using System;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Items;
using Slice.Player;
using Slice.Vessels;
using N = System.Numerics;
using Survivor = Slice.Player.Survivor;

namespace Slice.Orbit;

/// <summary>What your hands and E do in space: salvage, claiming a wreck, its doors.</summary>
public partial class Spaceflight
{
    /// <summary>Blocks salvaged from wrecks, and what they gave back (tests, stats).</summary>
    public int Salvaged { get; private set; }
    public readonly Bill SalvageTaken = new();

    /// <summary>A wreck was claimed by setting a core in it.</summary>
    public event Action<Vessel> Claimed;

    /// <summary>A wreck's door was forced open or shut.</summary>
    public event Action<Vessel, Int3, bool> DoorCycled;

    Vessel _salvaging;
    Int3 _salvageCell;
    float _salvageTime, _sparkTime, _shotCooldown;
    string _lastRefusal;

    /// <summary>The fraction of the current block's salvage done, 0 to 1 (the visor's progress).</summary>
    public float SalvageProgress { get; private set; }

    /// <summary>
    /// The hands in space (the game asks this first, before the surface's tools): the survival
    /// tool salvages wrecks in gather mode and fires in shoot mode, a core item claims a wreck,
    /// building waits for a later phase. False for what works the same anywhere (food, kits).
    /// </summary>
    public bool TakeHands(float dt, HandKind hand, bool primary, bool pressed)
    {
        _shotCooldown -= dt;
        if (!Game.InOrbit) return false;
        switch (hand)
        {
            case HandKind.Tool:
                if (Game.Tool.Mode == ToolMode.Gather) Salvage(dt, primary);
                else
                {
                    Halt();
                    if (primary && _shotCooldown <= 0f) Shoot();
                }
                return true;
            case HandKind.Core:
                Halt();
                if (pressed) Claim();
                return true;
            case HandKind.Block or HandKind.Device or HandKind.Blueprint:
                Halt();
                if (pressed) Game.Say("NO BUILDING IN ORBIT", Tones.Caution);
                return true;
            default:
                Halt();
                return false;
        }
    }

    void Halt()
    {
        _salvaging = null;
        _salvageTime = 0f;
        SalvageProgress = 0f;
    }

    void Refuse(string why)
    {
        if (why != _lastRefusal) Game.Say(why, Tones.Caution);
        _lastRefusal = why;
    }

    // ---- Salvage -------------------------------------------------------------------------------

    /// <summary>
    /// Gather mode on a wreck's block: held on one block for a moment (its mass decides how long),
    /// it comes free and about a third of its bill goes in your bag (design: the survival tool
    /// salvages wrecks at about 33 %). Only wrecks: your own blocks come down whole with the build tool.
    /// </summary>
    void Salvage(float dt, bool primary)
    {
        if (!primary)
        {
            Halt();
            _lastRefusal = null;
            return;
        }
        if (!WorldRay.Cast(Game.Zone, Game.Survivor, Rules.Wrecks.SalvageReach, Layers.Hull, out LookHit look) || look.Vessel == null
            || !look.Vessel.Structure.TryGet(look.Cell, out Block block))
        {
            Halt();
            return;
        }
        Vessel v = look.Vessel;
        if (!v.IsDerelict)
        {
            Halt();
            Refuse(v.Derelict != null ? $"{v.Name} · YOURS · USE BUILD TOOL" : "SALVAGE · WRECKS ONLY");
            return;
        }
        Structure s = v.Structure;
        BlockType type = s.TypeOf(block);
        if (type.IsContainer && !v.Storage.IsEmpty)
        {
            Halt();
            Refuse("HOLD NOT EMPTY · [E] CARGO BOX");
            return;
        }
        if (v != _salvaging || look.Cell != _salvageCell)
        {
            _salvaging = v;
            _salvageCell = look.Cell;
            _salvageTime = 0f;
        }
        float need = MathF.Max(Rules.Wrecks.SalvageMinSeconds, s.StatsOf(block).Mass * Rules.Wrecks.SalvageSecondsPerTonne);
        _salvageTime += dt;
        SalvageProgress = Math.Clamp(_salvageTime / need, 0f, 1f);
        Game.Suit.Worked();
        _sparkTime -= dt;
        if (_sparkTime <= 0f)
        {
            _sparkTime = 0.18f;
            Game.Shots?.Impact(look.Point, look.Normal, ImpactKind.Block);
            Game.Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Dig, look.Point, 1.6f, -10f);
        }
        if (_salvageTime < need) return;

        ItemCatalogue items = Game.Zone.Data.Items;
        Bill bill = BuildingBridge.Priced(items, s, block) ? BuildingBridge.BillOf(items, s, block) : new Bill();
        Bill back = Share(bill, Rules.Wrecks.SalvageReturn);
        s.Clear(look.Cell);
        if (type.IsContainer) new BlockPlacer(items, Game.Zone.WorldRules, new CrateYard(items)).Sync(s, v.Storage, 0, 0, 0, spill: false);
        if (s.Count == 0) TakeAway(v);
        else v.ApplyEdits();
        // Into the bag through the inventory's own way in; what has no room is set down, never lost.
        var left = new Bill();
        foreach (var (id, n) in back)
            foreach (var (over, k) in Game.Pack.Add(id, n)) left.Add(over, k);
        if (!left.IsEmpty) Game.SetDown(left, look.Point);
        foreach (var (id, n) in back) SalvageTaken.Add(id, n);
        Salvaged++;
        Game.Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Clank, look.Point, 0.8f, -4f);
        Game.Progress.Earn(3, "salvage");
        if (!back.IsEmpty) Game.Say($"+{items.Describe(back, 3)}", Tones.Good);
        Halt();
    }

    /// <summary>A share of a bill, rounded down, but never nothing for something (its biggest line keeps one).</summary>
    static Bill Share(Bill bill, double share)
    {
        var back = new Bill();
        string biggest = null;
        int most = 0;
        foreach (var (id, n) in bill)
        {
            int k = (int)Math.Floor(n * share);
            if (k > 0) back.Add(id, k);
            if (n > most) { most = n; biggest = id; }
        }
        if (back.IsEmpty && biggest != null) back.Add(biggest, 1);
        return back;
    }

    /// <summary>The last block of a wreck salvaged: nothing is left to float there.</summary>
    void TakeAway(Vessel v)
    {
        v.Zone.Physics.Unregister(v.Body);
        v.Zone.Physics.StuckFields.Remove(v.Field);
        v.Zone.Untrack(v);
        Orbit?.Wrecks.Remove(v);
        v.Node.QueueFree();
    }

    // ---- Shooting ------------------------------------------------------------------------------

    /// <summary>Shoot mode in space: a bolt and a scorch where it lands (nothing out here to hunt).</summary>
    void Shoot()
    {
        _shotCooldown = 0.35f;
        Survivor s = Game.Survivor;
        Vector3 eye = s.WorldEye(), dir = s.WorldLook();
        bool hit = WorldRay.Cast(Game.Zone, s, 120f, Layers.Hull | Layers.Props, out LookHit look);
        Vector3 end = hit ? look.Point : eye + dir * 120f;
        Vector3 muzzle = eye + dir * 0.6f + s.WorldUp() * -0.25f;
        Game.Shots?.Fire(muzzle, end, new Color("6ff2e0"), () => { if (hit) Game.Shots.Impact(look.Point, look.Normal, ImpactKind.Block); });
        Game.Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Ping, eye, 2.6f, -14f);
    }

    // ---- Claiming ------------------------------------------------------------------------------

    /// <summary>
    /// A core item set into a wreck with no core claims it (design: any wreck with no core can be
    /// claimed by placing a core in it): any wreck takes the core. It becomes yours, a craft in
    /// Flight Mode, and its core's gravity holds you to its floor.
    /// </summary>
    void Claim()
    {
        string item = Game.Pack.InHand.Item;
        if (!WorldRay.Cast(Game.Zone, Game.Survivor, Slice.Game.Game.UseReach + 1f, Layers.Hull, out LookHit look) || look.Vessel == null)
        {
            Game.Say("NO WRECK BLOCK", Tones.Caution);
            return;
        }
        Vessel v = look.Vessel;
        Structure s = v.Structure;
        if (s.Core != null)
        {
            Game.Say($"{v.Name} · HAS A CORE", Tones.Caution);
            return;
        }
        N.Vector3 n = MathX.Unrotate(v.Body.Rotation, look.Normal.ToNumerics());
        N.Vector3 a = N.Vector3.Abs(n);
        Int3 face = a.X >= a.Y && a.X >= a.Z ? new Int3(Math.Sign(n.X), 0, 0) : a.Y >= a.Z ? new Int3(0, Math.Sign(n.Y), 0) : new Int3(0, 0, Math.Sign(n.Z));
        Int3 cell = new(look.Cell.X + face.X, look.Cell.Y + face.Y, look.Cell.Z + face.Z);
        Block core = Building.Make(s, Game.Zone.Data.Catalogue.Type("core"));
        PlaceError e = Building.Place(s, cell, core, null, Building.DefaultOwner);
        if (e != PlaceError.None)
        {
            Game.Say($"CORE WON'T SEAT · {e}", Tones.Caution);
            return;
        }
        Game.Pack.Storage.Remove(item, 1);
        Game.Pack.Grid.Reconcile();
        v.ApplyEdits();
        v.Claimed(Game.Surface.Physics.Zone.Strength, inOrbit: true);
        Game.Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, look.Point, 0.7f, -2f);
        Game.Progress.Earn(40, "wreck claimed");
        Game.Notices.Post(Slice.Ui.NoticeKind.Claimed, "CLAIMED", v.Name);
        Claimed?.Invoke(v);
    }

    // ---- E: doors ------------------------------------------------------------------------------

    /// <summary>
    /// E in space on a wreck's door: with no power it is forced open (or shut) by hand.
    /// The game asks this before its own E. False if there is no door under the crosshair.
    /// </summary>
    public bool Use()
    {
        if (!Game.InOrbit || !DoorInReach(out Vessel v, out Int3 cell)) return false;
        Building.Toggle(v.Structure, cell);
        v.ApplyEdits();
        bool open = v.Structure.Get(cell).Open;
        Game.Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Clank, v.ToWorld(StructureBridge.CellCenter(v.Structure, cell)), open ? 0.6f : 0.5f, -3f);
        DoorCycled?.Invoke(v, cell, open);
        return true;
    }

    /// <summary>The prompt by the crosshair for a door in reach, or null.</summary>
    public string UsePrompt() => Game.InOrbit && DoorInReach(out _, out _) ? "E  Force the door" : null;

    bool DoorInReach(out Vessel v, out Int3 cell)
    {
        v = null;
        cell = default;
        if (!WorldRay.Cast(Game.Zone, Game.Survivor, Slice.Game.Game.UseReach, Layers.World | Layers.Hull | Layers.Props, out LookHit look) || look.Vessel == null) return false;
        if (!look.Vessel.Structure.TryGet(look.Cell, out Block b) || !look.Vessel.Structure.TypeOf(b).IsDoor) return false;
        v = look.Vessel;
        cell = look.Cell;
        return true;
    }
}
