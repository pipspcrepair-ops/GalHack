using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Boarding;

public enum Faction { Vharn, Player, None }

public enum LightMode { Normal, Alarm, Blackout, Player }

/// <summary>
/// A ship built from a <see cref="ShipLayout"/>: a deck and roof, one block per wall, door
/// or component cell, room lights, and the exterior superstructure.
///
/// Everything aboard is placed and queried in ship-local space (the layout's space) and
/// converted with ToLocal/ToGlobal at the edges, so nothing here assumes the ship stands
/// still. It is landed in this prototype; flying it is the moving-cabin prototype's job.
/// </summary>
public partial class Ship : Node3D
{
    public Main Game;
    public ShipLayout Layout;

    public Faction OwnerFaction { get; private set; } = Faction.Vharn;
    public bool Derelict => OwnerFaction == Faction.None;
    public bool Hostile => OwnerFaction == Faction.Vharn;
    public bool CoreAlive => Core != null && !Core.Destroyed;
    public float RebootRemaining { get; private set; }
    public bool Rebooting => RebootRemaining > 0f;
    public LightMode Lights { get; private set; } = LightMode.Normal;

    public CoreBlock Core { get; private set; }
    public Turret Turret { get; private set; }
    public Block GunnerStation { get; private set; }
    public Block Generator { get; private set; }

    /// <summary>The block filling each cell (walls, doors, components). Removed when destroyed.</summary>
    public readonly Dictionary<Vector2I, Block> Blocks = new();
    public readonly List<Door> Doors = new();
    public readonly List<LootCache> Caches = new();

    /// <summary>Cells whose block was destroyed: open floor now, or a hole in the hull.</summary>
    readonly HashSet<Vector2I> _opened = new();

    readonly List<(OmniLight3D light, MeshInstance3D lamp, bool emergency)> _lights = new();
    readonly List<Vector3> _actors = new();
    float _hullMax, _hullNow, _time;

    public event Action<Block> BlockDestroyed;
    public event Action CoreDestroyed;

    Tunables T => Game.Tunables;
    float CellSize => Layout.Cell;
    float Deck => Layout.DeckHeight;

    public float HullIntegrity => _hullMax > 0f ? _hullNow / _hullMax : 1f;
    public IEnumerable<Door> Bulkheads => Doors.Where(d => d.Kind == BlockKind.Bulkhead);

    public override void _Ready()
    {
        ProcessPhysicsPriority = -10;
        BuildDeckAndRoof();
        BuildBlocks();
        BuildGunnerStation();
        BuildTurret();
        BuildLights();
        BuildExterior();
        BuildInteriorAmbience();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _actors.Clear();
        Game.CollectActorFeet(_actors);
        foreach (Door door in Doors)
            if (!door.Destroyed) door.Tick(dt, _actors, T.DoorOpenRadius);

        if (RebootRemaining > 0f)
        {
            RebootRemaining = Mathf.Max(0f, RebootRemaining - dt);
            if (RebootRemaining == 0f && OwnerFaction == Faction.Player) SetLights(LightMode.Player);
        }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        UpdateLights();
        if (CoreAlive)
            Core.ShowHijack(OwnerFaction == Faction.Player ? 1f : Game.Hijack.Progress / Game.Hijack.Total, Game.Hijack.Channelling);
    }

    // ---- Space conversion and queries --------------------------------------------------

    public Vector3 CellToGlobal(Vector2I cell) => ToGlobal(Layout.CellCenter(cell));
    public Vector2I GlobalToCell(Vector3 global) => Layout.CellAt(ToLocal(global));

    /// <summary>True if a world-space point is inside the hull (under the roof, in an interior cell).</summary>
    public bool IsInside(Vector3 global)
    {
        Vector3 local = ToLocal(global);
        // Under the roof and within the footprint. Nobody can stand inside an intact wall, so
        // this is "in a room, a doorway, a duct or a hole blown in the hull".
        return local.Y > -1f && local.Y < Deck && Layout.IsShip(Layout.CellAt(local));
    }

    /// <summary>Can someone stand in this cell right now? Doors, sealed bulkheads and holes all count.</summary>
    public bool Passable(Vector2I cell, bool canCrouch = false)
    {
        if (!Layout.InBounds(cell)) return false;
        if (_opened.Contains(cell)) return Layout.IsShip(cell);
        CellKind kind = Layout[cell];
        if (Blocks.TryGetValue(cell, out Block block)) return block is Door door && door.Passable;
        if (kind == CellKind.Duct) return canCrouch;
        // Obstacles are inflated to whole cells: a cell is either free for its full 2 m width
        // or it's blocked. The gunner's console only fills part of its cell, so the cell is
        // refused rather than letting a path skim the console's edge.
        if (kind == CellKind.GunnerStation) return false;
        return ShipLayout.IsWalkable(kind);
    }

    /// <summary>
    /// A* from one ship-local point to another. Guards can't crawl through ducts. extraCost lets a
    /// guard steer around crowded cells (other guards) without treating them as walls.
    /// </summary>
    public List<Vector2I> FindPath(Vector3 fromLocal, Vector3 toLocal, bool canCrouch = false, Func<Vector2I, float> extraCost = null)
    {
        Vector2I start = Layout.CellAt(fromLocal);
        Vector2I goal = NearestPassable(Layout.CellAt(toLocal), canCrouch);
        if (goal.X < 0) return null;
        return Pathfinder.FindPath(start, goal, c => Passable(c, canCrouch), extraCost);
    }

    /// <summary>
    /// True if an agent of this radius can walk straight between two ship-local points: every
    /// sample along the line, and the points a radius to either side and ahead of it, lies in
    /// an open cell. Path smoothing only takes shortcuts that pass this check.
    /// </summary>
    public bool ClearLine(Vector3 from, Vector3 to, float radius)
    {
        Vector3 d = to - from;
        d.Y = 0f;
        float length = d.Length();
        if (length < 1e-3f) return true;
        Vector3 dir = d / length;
        Vector3 side = new Vector3(-dir.Z, 0f, dir.X) * radius;
        int steps = Mathf.CeilToInt(length / 0.25f);
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = from + dir * (length * i / steps);
            if (!Passable(Layout.CellAt(p)) || !Passable(Layout.CellAt(p + side)) || !Passable(Layout.CellAt(p - side)))
                return false;
        }
        return Passable(Layout.CellAt(to + dir * radius));
    }

    /// <summary>The goal cell itself, or the closest passable cell within a few cells of it.</summary>
    public Vector2I NearestPassable(Vector2I cell, bool canCrouch = false, int radius = 3)
    {
        if (Passable(cell, canCrouch)) return cell;
        Vector2I best = new(-1, -1);
        float bestDistance = float.MaxValue;
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            var c = new Vector2I(cell.X + dx, cell.Y + dy);
            float d = dx * dx + dy * dy;
            if (d < bestDistance && Passable(c, canCrouch))
            {
                bestDistance = d;
                best = c;
            }
        }
        return best;
    }

    public Door DoorAt(Vector2I cell) => Blocks.TryGetValue(cell, out var b) ? b as Door : null;

    public bool IsCellOpened(Vector2I cell) => _opened.Contains(cell);

    // ---- State changes -----------------------------------------------------------------

    public void NotifyBlockDamaged(Block block, float amount)
    {
        if (block.Kind is BlockKind.Hull or BlockKind.WeakHull)
        {
            _hullNow = Mathf.Max(0f, _hullNow - amount);
            Game.Alarm.ReportHull(HullIntegrity);
        }
    }

    public void NotifyBlockDestroyed(Block block)
    {
        if (Blocks.TryGetValue(block.Cell, out Block there) && there == block)
        {
            Blocks.Remove(block.Cell);
            _opened.Add(block.Cell);
        }
        if (block is Door door) Doors.Remove(door);
        if (block == Core) OnCoreDestroyed();
        BlockDestroyed?.Invoke(block);
    }

    /// <summary>The hijack finished: the ship is the player's, but its weapons and warp reboot first.</summary>
    public void CaptureForPlayer()
    {
        if (OwnerFaction != Faction.Vharn) return;
        OwnerFaction = Faction.Player;
        RebootRemaining = T.RebootSeconds;
    }

    void OnCoreDestroyed()
    {
        if (OwnerFaction == Faction.None) return;
        OwnerFaction = Faction.None;
        RebootRemaining = 0f;
        SetLights(LightMode.Blackout);
        Game.Effects.Explosion(Core.GlobalPosition, 2.5f, Palette.VharnGlow);
        CoreDestroyed?.Invoke();
    }

    /// <summary>Slams every bulkhead shut and locks it.</summary>
    public void SealBulkheads()
    {
        foreach (Door door in Bulkheads.ToList()) door.Seal();
    }

    public void SetLights(LightMode mode)
    {
        // A derelict ship stays dark whatever else happens.
        if (Derelict && mode != LightMode.Blackout) return;
        Lights = mode;
    }

    /// <summary>The self-destruct went off. The hull is scorched and everything aboard dies.</summary>
    public void Wreck()
    {
        OwnerFaction = Faction.None;
        SetLights(LightMode.Blackout);
        foreach (var (light, lamp, _) in _lights)
        {
            light.Visible = false;
            lamp.MaterialOverride = Art.Flat(Palette.Dark);
        }
    }

    // ---- Lights ------------------------------------------------------------------------

    void UpdateLights()
    {
        bool alarm = Lights == LightMode.Alarm;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 5f);
        foreach (var (light, lamp, emergency) in _lights)
        {
            switch (Lights)
            {
                case LightMode.Normal:
                    light.LightColor = Palette.Lamp;
                    light.LightEnergy = 1.5f;
                    break;
                case LightMode.Alarm:
                    light.LightColor = Palette.Lamp.Lerp(Palette.AlarmRed, 0.55f + 0.35f * pulse);
                    light.LightEnergy = 1.1f + 0.6f * pulse;
                    break;
                case LightMode.Blackout:
                    // Only the emergency lamps, dim and slow.
                    light.LightColor = Palette.AlarmRed;
                    light.LightEnergy = emergency ? 0.35f + 0.35f * (0.5f + 0.5f * Mathf.Sin(_time * 2.2f)) : 0f;
                    break;
                case LightMode.Player:
                    light.LightColor = new Color("c8fff6");
                    light.LightEnergy = 1.4f;
                    break;
            }
            light.Visible = light.LightEnergy > 0.01f;
            lamp.MaterialOverride = Lights switch
            {
                LightMode.Blackout => emergency ? Art.Glow(Palette.AlarmRed, 1.2f) : Art.Flat(Palette.Dark),
                LightMode.Player => Art.Glow(new Color("c8fff6"), 2f),
                _ when alarm => Art.Glow(Palette.AlarmRed, 2f),
                _ => Art.Glow(Palette.Lamp, 2.5f),
            };
        }
    }

    // ---- Building ----------------------------------------------------------------------

    void BuildDeckAndRoof()
    {
        var deck = new StaticBody3D { Name = "Deck", CollisionLayer = Layers.Ship, CollisionMask = 0 };
        deck.SetMeta("surface", (int)Surface.Metal);
        AddChild(deck);

        var underMaterial = Art.Flat(new Color("1c1d26"), 0.9f);
        var roofMaterial = Art.Banded("roof", 0.6f, (0f, Palette.Dark), (0.3f, Palette.Bone));
        var tile = Art.BoxMesh(new Vector3(CellSize - 0.08f, 0.06f, CellSize - 0.08f));
        var tileMaterial = Art.Flat(Palette.Deck, 0.75f, 0.2f);
        var vaultTile = Art.Checker(Palette.Dark, Palette.Armour, 0.5f);
        var ceiling = Art.BoxMesh(new Vector3(CellSize - 0.06f, 0.04f, CellSize - 0.06f));
        var ceilingMaterial = Art.Flat(new Color("8d8478"), 0.9f);

        for (int r = 0; r < Layout.Height; r++)
        {
            // Merge each row's ship cells into long slabs: few shapes, no seams to trip on.
            int c = 0;
            while (c < Layout.Width)
            {
                if (!Layout.IsShip(new Vector2I(c, r))) { c++; continue; }
                int start = c;
                while (c < Layout.Width && Layout.IsShip(new Vector2I(c, r))) c++;
                float x0 = Layout.CellCenter(new Vector2I(start, r)).X - CellSize * 0.5f;
                float x1 = Layout.CellCenter(new Vector2I(c - 1, r)).X + CellSize * 0.5f;
                float z = Layout.CellCenter(new Vector2I(start, r)).Z;
                var slab = new Vector3(x1 - x0, 0.5f, CellSize);
                var mid = new Vector3((x0 + x1) * 0.5f, 0f, z);

                deck.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = slab }, Position = mid + Vector3.Down * 0.25f });
                AddChild(Art.Mesh(Art.BoxMesh(slab), underMaterial, mid + Vector3.Down * 0.27f));

                var roofSlab = new Vector3(slab.X, 0.6f, CellSize);
                deck.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = roofSlab }, Position = mid + Vector3.Up * (Deck + 0.3f) });
                AddChild(Art.Mesh(Art.Box(Vector3.Zero, roofSlab), roofMaterial, mid + Vector3.Up * (Deck + 0.3f)));
            }

            for (c = 0; c < Layout.Width; c++)
            {
                var cell = new Vector2I(c, r);
                CellKind kind = Layout[cell];
                if (kind == CellKind.Outside) continue;
                Vector3 at = Layout.CellCenter(cell);
                bool vault = IsVault(cell);
                // Tiles go everywhere a wall might one day be blown out of.
                AddChild(Art.Mesh(tile, vault ? vaultTile : tileMaterial, at + Vector3.Down * 0.03f, false));
                if (kind != CellKind.Duct)
                    AddChild(Art.Mesh(ceiling, ceilingMaterial, at + Vector3.Up * (Deck - 0.02f), false));
            }
        }
    }

    bool IsVault(Vector2I cell)
    {
        // Vault cells are the ones boxed in by armour along their row.
        bool left = false, right = false;
        for (int c = cell.X - 1; c >= 0 && !left; c--)
        {
            CellKind k = Layout[new Vector2I(c, cell.Y)];
            if (k == CellKind.Armour || k == CellKind.SecurityDoor && Layout[new Vector2I(c, cell.Y - 1)] == CellKind.Armour) left = true;
            else if (k is CellKind.Wall or CellKind.Hull) break;
        }
        for (int c = cell.X + 1; c < Layout.Width && !right; c++)
        {
            CellKind k = Layout[new Vector2I(c, cell.Y)];
            if (k == CellKind.Armour) right = true;
            else if (k is CellKind.Wall or CellKind.Hull) break;
        }
        return left && right && Layout[cell] != CellKind.Armour;
    }

    void BuildBlocks()
    {
        var wall = Art.Banded("wall", Deck, (0f, Palette.Dark), (0.05f, Palette.WallInside), (0.48f, Palette.WallTrim),
            (0.55f, Palette.WallInside), (0.92f, new Color("8d8478")));
        var hull = Art.Banded("hull", Deck, (0f, Palette.Dark), (0.05f, Palette.Oxblood), (0.36f, Palette.Yellow),
            (0.40f, Palette.Bone), (0.9f, new Color("d6c9a8")));
        var armour = Art.Banded("armour", Deck, (0f, Palette.Dark), (0.06f, Palette.Armour), (0.62f, Palette.Yellow),
            (0.66f, Palette.Armour));
        var weak = Art.Stripes(Palette.Yellow, Palette.Dark, 0.45f);
        var crate = Art.Banded("crate", 1.2f, (0f, Palette.Dark), (0.1f, Palette.Orange), (0.72f, Palette.Dark), (0.8f, Palette.Orange));
        var ductCap = Art.Flat(Palette.Dark, 0.6f, 0.4f);

        Vector3 full = new(CellSize, Deck, CellSize);
        for (int r = 0; r < Layout.Height; r++)
        for (int c = 0; c < Layout.Width; c++)
        {
            var cell = new Vector2I(c, r);
            Vector3 floor = Layout.CellCenter(cell);
            Vector3 mid = floor + Vector3.Up * Deck * 0.5f;
            switch (Layout[cell])
            {
                case CellKind.Wall:
                    Add(new Block { Kind = BlockKind.Wall, BaseMaterial = wall, ChunkColor = Palette.WallInside, Hp = T.HpWall, Pieces = new(2, 2, 2) }, cell, mid, full);
                    break;
                case CellKind.Hull:
                case CellKind.TurretMount:
                    Add(new Block { Kind = BlockKind.Hull, BaseMaterial = hull, ChunkColor = Palette.Bone, Hp = T.HpHull, Pieces = new(2, 2, 2) }, cell, mid, full);
                    break;
                case CellKind.WeakHull:
                    Add(new Block { Kind = BlockKind.WeakHull, BaseMaterial = weak, ChunkColor = Palette.Yellow, Hp = T.HpWeakHull, Pieces = new(2, 2, 2) }, cell, mid, full);
                    break;
                case CellKind.Armour:
                    Add(new Block { Kind = BlockKind.Armour, BaseMaterial = armour, ChunkColor = Palette.Armour, Surface = Surface.Armour, Hp = T.HpArmor }, cell, mid, full);
                    break;
                case CellKind.Door:
                case CellKind.SecurityDoor:
                case CellKind.Bulkhead:
                case CellKind.Airlock:
                    AddDoor(cell, mid, full);
                    break;
                case CellKind.Core:
                    Core = new CoreBlock { Kind = BlockKind.Core, Surface = Surface.Core, ChunkColor = Palette.VharnGlow, Hp = T.HpCore };
                    Add(Core, cell, mid, full);
                    break;
                case CellKind.Generator:
                    Generator = new Block
                    {
                        Kind = BlockKind.Generator, Surface = Surface.Machine, ChunkColor = Palette.Oxblood, Hp = T.HpGenerator,
                        Dress = DressGenerator,
                    };
                    Add(Generator, cell, floor + Vector3.Up * 1.4f, new Vector3(1.6f, 2.8f, 1.6f));
                    break;
                case CellKind.VaultLoot:
                case CellKind.Loot:
                    bool vault = Layout[cell] == CellKind.VaultLoot;
                    var size = vault ? new Vector3(1.5f, 1.1f, 1.1f) : new Vector3(1.1f, 0.8f, 0.8f);
                    var cache = new LootCache
                    {
                        Kind = BlockKind.Loot, Surface = Surface.Armour, Indestructible = true, Hp = T.HpLoot,
                        Contents = new Dictionary<string, int>(vault ? Layout.LootVault : Layout.LootCache),
                    };
                    Add(cache, cell, floor + Vector3.Up * size.Y * 0.5f, size);
                    Caches.Add(cache);
                    break;
                case CellKind.Cover:
                    Add(new Block { Kind = BlockKind.Cover, BaseMaterial = crate, ChunkColor = Palette.Orange, Hp = T.HpCover, Pieces = new(2, 1, 2) },
                        cell, floor + Vector3.Up * 0.6f, new Vector3(1.5f, 1.2f, 1.5f));
                    break;
                case CellKind.Duct:
                    // A low cap over the crawlway: only a crouching player fits under it.
                    float capHeight = Deck - Layout.DuctHeight;
                    var cap = new Block
                    {
                        Kind = BlockKind.DuctCap, BaseMaterial = ductCap, Indestructible = true, Cell = cell,
                        Size = new Vector3(CellSize, capHeight, CellSize), Position = floor + Vector3.Up * (Layout.DuctHeight + capHeight * 0.5f),
                        Ship = this, Name = $"DuctCap_{c}_{r}",
                    };
                    AddChild(cap);
                    cap.AddDecor(Art.Mesh(Art.BoxMesh(new Vector3(CellSize, 0.08f, 0.12f)), Art.Glow(Palette.Yellow, 1.5f),
                        new Vector3(0, -capHeight * 0.5f + 0.04f, 0), false));
                    break;
            }
        }
        _hullNow = _hullMax;
    }

    void Add(Block block, Vector2I cell, Vector3 position, Vector3 size)
    {
        block.Ship = this;
        block.Cell = cell;
        block.Size = size;
        block.MaxHp = block.Hp;
        block.Position = position;
        block.Name = $"{block.Kind}_{cell.X}_{cell.Y}";
        AddChild(block);
        Blocks[cell] = block;
        if (block.Kind is BlockKind.Hull or BlockKind.WeakHull) _hullMax += block.MaxHp;
    }

    void AddDoor(Vector2I cell, Vector3 mid, Vector3 full)
    {
        CellKind kind = Layout[cell];
        var door = new Door
        {
            Kind = kind switch
            {
                CellKind.SecurityDoor => BlockKind.SecurityDoor,
                CellKind.Bulkhead => BlockKind.Bulkhead,
                CellKind.Airlock => BlockKind.Airlock,
                _ => BlockKind.Door,
            },
            Surface = kind == CellKind.Door ? Surface.Metal : Surface.Armour,
            ChunkColor = kind == CellKind.Door ? Palette.Teal : Palette.Yellow,
            Hp = kind == CellKind.Door ? T.HpDoor : T.HpSecurityDoor,
        };
        // The passage runs along Z when the cells in front and behind are open.
        bool Open(Vector2I c) => Layout[c] == CellKind.Outside || ShipLayout.IsWalkable(Layout[c]);
        door.SpansX = Open(cell + new Vector2I(0, -1)) || Open(cell + new Vector2I(0, 1));
        door.Setup(locked: kind is CellKind.SecurityDoor or CellKind.Airlock, Deck);
        Add(door, cell, mid, full);
        Doors.Add(door);
    }

    static void DressGenerator(Block b)
    {
        var dark = Art.Flat(Palette.Dark, 0.5f, 0.5f);
        b.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(1.6f, 0.4f, 1.6f)), dark, new Vector3(0, -1.2f, 0)));
        b.AddChild(Art.Mesh(new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.62f, Height = 2.1f },
            Art.Flat(Palette.Oxblood, 0.5f, 0.3f), new Vector3(0, -0.05f, 0)));
        foreach (float y in new[] { -0.6f, 0f, 0.6f })
            b.AddChild(Art.Mesh(new CylinderMesh { TopRadius = 0.68f, BottomRadius = 0.68f, Height = 0.12f },
                Art.Glow(Palette.Orange, 2.5f), new Vector3(0, y, 0), false));
        b.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(1.6f, 0.3f, 1.6f)), Art.Checker(Palette.Yellow, Palette.Dark, 0.4f), new Vector3(0, 1.2f, 0)));
    }

    void BuildGunnerStation()
    {
        if (!Layout.InBounds(Layout.GunnerStation)) return;
        Vector3 floor = Layout.CellCenter(Layout.GunnerStation);
        Vector3 toMount = (Layout.CellCenter(Layout.TurretMount) - floor).Normalized();
        GunnerStation = new Block
        {
            Kind = BlockKind.Station, Surface = Surface.Machine, ChunkColor = Palette.Dark, Hp = T.HpStation,
            Ship = this, Cell = Layout.GunnerStation, Size = new Vector3(0.6f, 1.3f, 1.6f), Name = "GunnerStation",
            Position = floor + toMount * 0.65f + Vector3.Up * 0.65f,
            Dress = b =>
            {
                b.AddChild(Art.Mesh(Art.BoxMesh(b.Size), Art.Flat(Palette.Dark, 0.5f, 0.4f), Vector3.Zero));
                b.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.05f, 0.5f, 1.2f)), Art.Glow(Palette.AlarmRed, 1.8f), new Vector3(0.31f, 0.3f, 0), false));
                b.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.7f, 0.1f, 1.7f)), Art.Flat(Palette.Yellow, 0.5f), new Vector3(0, 0.7f, 0)));
            },
        };
        GunnerStation.MaxHp = GunnerStation.Hp;
        AddChild(GunnerStation);
        if (Mathf.Abs(toMount.Z) > Mathf.Abs(toMount.X)) GunnerStation.RotationDegrees = new Vector3(0, 90, 0);

        // The gunner's seat: decoration only, he sits on it.
        AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.7f, 0.5f, 0.7f)), Art.Flat(Palette.Red, 0.6f), floor - toMount * 0.25f + Vector3.Up * 0.25f));
    }

    void BuildTurret()
    {
        if (!Layout.InBounds(Layout.TurretMount)) return;
        Vector2I mount = Layout.TurretMount;
        // The turret hangs off the outside face of its hull block, toward open ground.
        Vector3 outward = Vector3.Zero;
        foreach (Vector2I n in ShipLayout.Neighbours4)
            if (Layout[mount + n] == CellKind.Outside) outward = new Vector3(n.X, 0, n.Y);
        Turret = new Turret
        {
            Ship = this, Kind = BlockKind.Turret, Surface = Surface.Machine, ChunkColor = Palette.Dark, Hp = T.HpTurret,
            Cell = mount, Size = new Vector3(2.1f, 2.1f, 2.1f), Name = "Turret", Outward = outward,
            Position = Layout.CellCenter(mount) + outward * (CellSize * 0.5f + 1.1f) + Vector3.Up * 2.2f,
        };
        Turret.MaxHp = Turret.Hp;
        AddChild(Turret);
    }

    void BuildLights()
    {
        // Rooms are flood-filled regions of open floor; doors separate them.
        bool InRoom(Vector2I c) => Layout[c] is CellKind.Floor or CellKind.GunnerStation or CellKind.Core
            or CellKind.Generator or CellKind.VaultLoot or CellKind.Loot or CellKind.Cover;
        var seen = new HashSet<Vector2I>();
        int lightIndex = 0;
        for (int r = 0; r < Layout.Height; r++)
        for (int c = 0; c < Layout.Width; c++)
        {
            var start = new Vector2I(c, r);
            if (seen.Contains(start) || !InRoom(start)) continue;
            var room = new List<Vector2I>();
            var queue = new Queue<Vector2I>();
            queue.Enqueue(start);
            seen.Add(start);
            while (queue.Count > 0)
            {
                Vector2I cell = queue.Dequeue();
                room.Add(cell);
                foreach (Vector2I n in ShipLayout.Neighbours4)
                    if (!seen.Contains(cell + n) && InRoom(cell + n)) { seen.Add(cell + n); queue.Enqueue(cell + n); }
            }

            // Farthest-point placement: first light near the middle, then fill the gaps.
            var centroid = new Vector2((float)room.Average(p => p.X), (float)room.Average(p => p.Y));
            var placed = new List<Vector2I> { room.OrderBy(p => (new Vector2(p.X, p.Y) - centroid).LengthSquared()).First() };
            while (true)
            {
                Vector2I best = default;
                float bestDistance = 0f;
                foreach (Vector2I p in room)
                {
                    float d = placed.Min(q => Mathf.Max(Mathf.Abs(p.X - q.X), Mathf.Abs(p.Y - q.Y)));
                    if (d > bestDistance) { bestDistance = d; best = p; }
                }
                if (bestDistance < 3f) break;
                placed.Add(best);
            }

            foreach (Vector2I p in placed)
            {
                Vector3 at = Layout.CellCenter(p) + Vector3.Up * (Deck - 0.35f);
                var light = new OmniLight3D { Position = at, OmniRange = 7.5f, LightEnergy = 1.5f, LightColor = Palette.Lamp, OmniAttenuation = 1.2f };
                AddChild(light);
                var lamp = Art.Mesh(Art.BoxMesh(new Vector3(1.1f, 0.05f, 1.1f)), Art.Glow(Palette.Lamp, 2.5f), at + Vector3.Up * 0.31f, false);
                AddChild(lamp);
                _lights.Add((light, lamp, lightIndex++ % 3 == 0));
            }
        }
    }

    void BuildInteriorAmbience()
    {
        // The sky's ambient light would flood the interior (no GI on the mobile renderer), so an
        // interior reflection probe swaps it for a dim warm ambient: room lights do the work,
        // and a blackout is actually dark.
        Aabb bounds = Layout.Bounds;
        AddChild(new ReflectionProbe
        {
            Name = "InteriorAmbience",
            Size = new Vector3(bounds.Size.X, Deck + 0.4f, bounds.Size.Z),
            Position = bounds.GetCenter(),
            Interior = true,
            AmbientMode = ReflectionProbe.AmbientModeEnum.Color,
            AmbientColor = new Color(0.16f, 0.12f, 0.11f),
            AmbientColorEnergy = 1f,
            BlendDistance = 0.5f,
            UpdateMode = ReflectionProbe.UpdateModeEnum.Once,
        });
    }
}
