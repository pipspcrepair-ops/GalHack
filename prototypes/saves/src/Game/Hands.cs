using System;
using System.Linq;
using GalHack.Persistence;
using GalHack.Records;
using Godot;
using Saves.Zones;

namespace Saves;

/// <summary>
/// What the player can do to saved things by looking at them: pick up loose stacks, carry crates,
/// drop items, build and remove blocks, switch a core's mode. Every action is an edit to the
/// record table (through WorldOps, the same code the tests hammer), after which the affected
/// views are simply made again from their records.
/// </summary>
public sealed class Hands
{
    const float Reach = 6f;
    const uint LookMask = Layers.World | Layers.Interior | Layers.Pickup;

    readonly Func<RecordTable> _table;
    readonly Func<Zone> _zone;
    readonly Places _places;
    readonly Player _player;
    int _nextId;

    /// <summary>The crate being carried, or null. While carried, its record sits at the player's feet, in the player's zone.</summary>
    public string Carrying { get; private set; }

    public event Action<string> Said;

    public Hands(Func<RecordTable> table, Func<Zone> zone, Places places, Player player)
    {
        _table = table;
        _zone = zone;
        _places = places;
        _player = player;
        _nextId = (int)(DateTime.UtcNow.Ticks % 1_000_000);
    }

    RecordTable Table => _table();

    /// <summary>The zone and ship of the frame the player's body is in, and positions in it are local to it.</summary>
    (string Zone, string Ship) Here() =>
        _player.CurrentMode == Player.Mode.Outside
            ? (_zone().ZoneId, null)
            : (ZoneIds.Cabin(_player.Ship.RecordId), _player.Ship.RecordId);

    (Vector3 Point, Vector3 Normal, string RecordId, int Shape)? Look()
    {
        var (space, from, direction, _) = _player.LookRay();
        var query = PhysicsRayQueryParameters3D.Create(from, from + direction * Reach, LookMask, new Godot.Collections.Array<Rid> { _player.GetRid() });
        var hit = space.IntersectRay(query);
        if (hit.Count == 0) return null;
        var collider = hit["collider"].As<GodotObject>();
        string id = collider is Node node && node.HasMeta(RecordViews.RecordMeta) ? node.GetMeta(RecordViews.RecordMeta).AsString() : null;
        return (hit["position"].AsVector3(), hit["normal"].AsVector3(), id, hit["shape"].AsInt32());
    }

    Vector3 InFront(float distance)
    {
        var (_, from, direction, _) = _player.LookRay();
        Vector3 flat = new Vector3(direction.X, 0, direction.Z).Normalized();
        Vector3 feet = from - Vector3.Up * 1.6f;
        return feet + flat * distance;
    }

    public void Handle(Key key)
    {
        if (_player.CurrentMode == Player.Mode.Piloting) return;
        try
        {
            switch (key)
            {
                case Key.E: Use(); break;
                case Key.Z: DropItems(); break;
                case Key.B: Build(); break;
                case Key.X: RemoveBlock(); break;
                case Key.M: SwitchMode(); break;
            }
        }
        catch (SaveRefusedException e)
        {
            Said?.Invoke(e.Message);
        }
    }

    void Use()
    {
        var look = Look();
        if (Carrying != null)
        {
            // On whatever floor you look at, or on your own floor just in front of you.
            SetDown(look is { Normal.Y: > 0.5f } floor ? floor.Point : InFront(2f));
            return;
        }
        if (look?.RecordId is not string id || !Table.TryGet(id, out SaveRecord record)) return;
        if (record.Type == RecordTypes.Drop)
        {
            int count = WorldOps.PickUp(Table, id, _player.RecordId);
            Places.Despawn(_zone(), id);
            Said?.Invoke($"picked up {count} items");
        }
        else if (record.Type == RecordTypes.Container)
        {
            var (zone, ship) = Here();
            WorldOps.Place(Table, id, zone, ship, Places.Round(_player.GlobalPosition));
            Places.Despawn(_zone(), id);
            Carrying = id;
            Said?.Invoke($"carrying {id}: E to set it down");
        }
    }

    void SetDown(Vector3 at)
    {
        var (zone, ship) = Here();
        WorldOps.Place(Table, Carrying, zone, ship, Places.Round(at), yaw: Math.Round(_player.Yaw, 3));
        _places.Respawn(_zone(), Carrying);
        Said?.Invoke($"set {Carrying} down{(ship != null ? " in the ship's hold" : "")}");
        Carrying = null;
    }

    /// <summary>The player stepped on or off a ship: a carried crate goes with them, record and all.</summary>
    public void FollowPlayer()
    {
        if (Carrying == null) return;
        var (zone, ship) = Here();
        WorldOps.Place(Table, Carrying, zone, ship, Places.Round(_player.GlobalPosition));
    }

    void DropItems()
    {
        SaveRecord me = Table.Get(_player.RecordId);
        ItemStack stack = WorldOps.StacksOf(me).FirstOrDefault();
        if (stack == null)
        {
            Said?.Invoke("nothing to drop");
            return;
        }
        var look = Look();
        Vector3 at = look is { Normal.Y: > 0.5f } l ? l.Point : InFront(1.5f);
        var (zone, ship) = Here();
        string id;
        do id = $"drop-{_nextId++}"; while (Table.Contains(id));
        int dropped = WorldOps.Drop(Table, me.Id, stack.Item, 5, id, zone, ship, Places.Round(at));
        _places.Respawn(_zone(), id);
        Said?.Invoke($"dropped {dropped} {stack.Item}");
    }

    void Build()
    {
        var look = Look();
        if (look is not { } hit) return;
        var (zone, ship) = Here();
        Vector3 target = hit.Point + hit.Normal * 0.5f;

        // Build onto the structure you look at, else the nearest one here, else start a new one.
        SaveRecord structure = hit.RecordId != null && Table.TryGet(hit.RecordId, out SaveRecord s) && s.Type == RecordTypes.Structure ? s : null;
        structure ??= Table.InZone(zone).Where(r => r.Type == RecordTypes.Structure)
            .OrderBy(r => Origin(r).DistanceTo(target)).FirstOrDefault(r => Origin(r).DistanceTo(target) < 12f);
        if (structure == null)
        {
            string id;
            do id = $"build-{_nextId++}"; while (Table.Contains(id));
            Vector3 origin = target.Floor();
            Table.Put(GameRecords.Make(id, RecordTypes.Structure, zone, ship, StarterWorld.Owner,
                new StructureData { Name = "Build", Origin = new double[] { origin.X, origin.Y, origin.Z } }, touched: true));
            structure = Table.Get(id);
        }
        Vector3 cell = (target - Origin(structure)).Floor();
        if (WorldOps.PlaceBlock(Table, structure.Id, new[] { (int)cell.X, (int)cell.Y, (int)cell.Z }, StarterWorld.Orange, _player.RecordId))
        {
            _places.Respawn(_zone(), structure.Id);
            Said?.Invoke($"built a block on {structure.Id} (one plate used)");
        }
        else Said?.Invoke(Stacks.CountOf(WorldOps.StacksOf(Table.Get(_player.RecordId)), ItemKinds.Plate) == 0 ? "no hull plates left" : "that cell is taken");
    }

    void RemoveBlock()
    {
        if (Look() is not { RecordId: string id } hit || !Table.TryGet(id, out SaveRecord r) || r.Type != RecordTypes.Structure) return;
        if (WorldOps.RemoveBlock(Table, id, hit.Shape, _player.RecordId))
        {
            _places.Respawn(_zone(), id);
            Said?.Invoke($"took a block off {id}: a plate back in your pack");
        }
    }

    void SwitchMode()
    {
        if (Look() is not { RecordId: string id } || !Table.TryGet(id, out SaveRecord r) || r.Type != RecordTypes.Structure) return;
        string mode = GameRecords.Read<StructureData>(r).Mode == CoreModes.Base ? CoreModes.CapitalShip : CoreModes.Base;
        WorldOps.SwitchCoreMode(Table, id, mode);
        _places.Respawn(_zone(), id);
        Said?.Invoke($"{id}'s core is now in {mode} mode: same blocks, same crates");
    }

    static Vector3 Origin(SaveRecord structure)
    {
        double[] o = GameRecords.Read<StructureData>(structure).Origin;
        return new Vector3((float)o[0], (float)o[1], (float)o[2]);
    }
}
