using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Layers = GalHack.Physics.Layers;

namespace Slice.Places;

/// <summary>
/// A place's moving and usable parts: doors (two leaves that slide into the walls beside the
/// doorway, E to open and shut; locked ones need a keycard of their clearance, and a lamp beside
/// them says which), lift cars (the shaft is ridden Empyrion's way, up and down with Space and
/// Ctrl, the car under your feet; a shaft door opens only with the car there, and E on it calls
/// the car), keycards lying where their owners fell, the draft that gives a hidden room away, and
/// a few real lamps for the place's lights nearest you.
/// </summary>
public partial class Place
{
    // ---- Doors ----------------------------------------------------------------------------------

    public sealed class Door
    {
        public int Index;                 // doorway index, -1 for the way in
        public Node3D Root;
        public AnimatableBody3D[] Leaves;
        public Vector3[] Shut;            // each leaf's centre when shut (local)
        public Vector3 Along;             // the doorway's width direction
        public float Width, Open, Target;
        public bool Jammed, Blast;
        public string Clearance;
        public int Lift = -1, Stop = -1;
        public Vector3 Centre;            // local, mid-height
        public MeshInstance3D Lamp;
        public List<Int3> Cells;
        public bool IsShut => Open < 0.05f;
    }

    readonly List<Door> _doors = new();
    public IReadOnlyList<Door> Doors => _doors;
    readonly Dictionary<ulong, Door> _doorOf = new();

    public Door DoorOf(GodotObject collider) => collider != null && _doorOf.TryGetValue(collider.GetInstanceId(), out Door d) ? d : null;

    static StandardMaterial3D _band, _seam;

    /// <summary>How far a door leaf's band and seam stop short of its ends, top and bottom.</summary>
    const float Inset = 0.006f;

    IEnumerable<int> BuildDoors()
    {
        Layout layout = Inst.Layout;
        var locks = layout.Extras.Locks.ToDictionary(l => l.Doorway);
        var liftDoor = new Dictionary<int, (int lift, int stop)>();
        for (int li = 0; li < layout.Extras.Lifts.Count; li++)
            for (int s = 0; s < layout.Extras.Lifts[li].StopDoors.Count; s++) liftDoor[layout.Extras.Lifts[li].StopDoors[s]] = (li, s);
        var groups = layout.Doorways.Select((d, i) => (d.Cells, d.Facing, i)).ToList();
        groups.Add((layout.ExitCells, layout.EntranceFacing, -1));
        _band ??= new StandardMaterial3D { AlbedoColor = new Color("e0a800"), Roughness = 0.6f };
        _seam ??= new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.09f), Roughness = 0.8f };
        foreach (var (cells, facing, index) in groups)
        {
            if (cells.Count == 0) continue;
            var doorCells = cells.Where(c => Inst.Blocks.Get(c).Type == BlockType.Door).ToList();
            if (doorCells.Count == 0) continue; // blown out of a ruin: an open doorway
            Block b0 = Inst.Blocks.Get(doorCells[0]);
            bool lift = index >= 0 && liftDoor.ContainsKey(index);
            // A jammed door's gap has just the room for a body, straight on: with a block crowding either side
            // of it there is no getting through, so what the years left in a doorway like that is a door forced wide.
            bool pinched = b0.Variant == DoorState.Jammed && !lift && cells.Any(c => c.Y <= cells.Min(o => o.Y) + 1
                && (BlockInfo.Collides(Inst.Blocks.Get(c + facing.Vec()).Type) || BlockInfo.Collides(Inst.Blocks.Get(c - facing.Vec()).Type)));
            if (pinched) foreach (Int3 c in doorCells) Inst.Blocks.At(c).Variant = DoorState.Open;
            if ((b0.Variant == DoorState.Open || pinched) && !lift) continue; // standing open: nothing to draw
            Int3 min = cells.Aggregate(Int3.Min), max = cells.Aggregate(Int3.Max);
            var lo = new Vector3(min.X, min.Y, min.Z);
            var hi = new Vector3(max.X + 1, max.Y + 1, max.Z + 1);
            bool alongX = facing is Dir.North or Dir.South;
            // The leaves run in the wall either side: where decay took that wall, the door fell with it (a way in, not a leaf hanging in the air).
            Int3 sideA = alongX ? min + new Int3(-1, 0, 0) : min + new Int3(0, 0, -1), sideB = alongX ? max with { Y = min.Y } + new Int3(1, 0, 0) : max with { Y = min.Y } + new Int3(0, 0, 1);
            if (!lift && (Inst.Blocks.Get(sideA).IsAir || Inst.Blocks.Get(sideB).IsAir))
            {
                foreach (Int3 c in doorCells) Inst.Blocks.Set(c, Block.Air);
                continue;
            }
            bool blast = b0.Variant == DoorState.Blast;
            float thick = blast ? 0.4f : 0.18f;
            float width = alongX ? hi.X - lo.X : hi.Z - lo.Z, height = hi.Y - lo.Y;
            Vector3 along = alongX ? Vector3.Right : Vector3.Back;
            Vector3 centre = (lo + hi) / 2f;
            var color = new Color(b0.Color.R / 255f, b0.Color.G / 255f, b0.Color.B / 255f);
            var paint = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.55f, Metallic = 0.4f };
            var root = new Node3D { Name = index < 0 ? "Door way-in" : $"Door {index}" };
            AddChild(root);
            var door = new Door
            {
                Index = index, Root = root, Along = along, Width = width, Centre = centre, Cells = cells,
                Jammed = b0.Variant == DoorState.Jammed, Blast = blast,
                Clearance = index >= 0 && locks.TryGetValue(index, out DoorLock lk) ? lk.Clearance : null,
                Leaves = new AnimatableBody3D[2], Shut = new Vector3[2],
            };
            if (lift) (door.Lift, door.Stop) = liftDoor[index];
            for (int side = 0; side < 2; side++)
            {
                float half = width / 2f;
                var size = alongX ? new Vector3(half, height, thick) : new Vector3(thick, height, half);
                var leaf = new AnimatableBody3D { Name = side == 0 ? "Leaf A" : "Leaf B", CollisionLayer = Layers.World, CollisionMask = 0, SyncToPhysics = true };
                leaf.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
                leaf.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = paint });
                // A hazard band across the middle, and the dark edge where the leaves meet. Each stands proud of
                // the leaf's faces (the seam prouder than the band it crosses) and stops short of its ends, top
                // and bottom: no face of one lies in a face of another, where the two would flicker.
                float band = half - 2f * Inset;
                leaf.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = alongX ? new Vector3(band, blast ? 0.4f : 0.2f, thick + 0.02f) : new Vector3(thick + 0.02f, blast ? 0.4f : 0.2f, band) },
                    MaterialOverride = _band, Position = new Vector3(0f, -0.4f, 0f),
                });
                float edge = (side == 0 ? 1f : -1f) * (half / 2f - 0.02f - 2f * Inset);
                leaf.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = alongX ? new Vector3(0.04f, height - 2f * Inset, thick + 0.04f) : new Vector3(thick + 0.04f, height - 2f * Inset, 0.04f) },
                    MaterialOverride = _seam, Position = along * edge,
                });
                door.Shut[side] = centre + along * ((side == 0 ? -1f : 1f) * half / 2f);
                leaf.Position = door.Shut[side];
                root.AddChild(leaf);
                door.Leaves[side] = leaf;
                _doorOf[leaf.GetInstanceId()] = door;
            }
            if (door.Clearance != null)
            {
                // The lock's lamp beside the doorway, in its clearance's colour (green once you can open it).
                Vector3 side = along * (width / 2f + 0.3f);
                Vector3 outward = new Vector3(facing.Vec().X, 0, facing.Vec().Z) * (thick / 2f + 0.52f);
                door.Lamp = new MeshInstance3D
                {
                    Name = "Lock lamp", Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.18f, 0.12f) },
                    Position = centre + side + outward + Vector3.Down * 0.2f,
                    MaterialOverride = LampMaterial(ClearanceColor(door.Clearance)),
                };
                root.AddChild(door.Lamp);
            }
            door.Open = door.Jammed ? 0.5f : Memory.DoorsOpen.TryGetValue(index, out bool open) && open ? 1f : 0f;
            door.Target = door.Open;
            PlaceLeaves(door);
            _doors.Add(door);
            // Its leaves are drawn only while its cube is (from further off the merged drawing has it, shut).
            Group home = GroupAt(GroupOf(ChunkOf(new Int3(Mathf.FloorToInt(centre.X), Mathf.FloorToInt(centre.Y), Mathf.FloorToInt(centre.Z)))));
            home.Doors.Add(door);
            root.Visible = home.Near;
            yield return 0;
        }
    }

    static StandardMaterial3D LampMaterial(Color c) => new()
    {
        AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 1.6f, Roughness = 0.4f,
    };

    public static Color ClearanceColor(string clearance)
    {
        Rgb c = Fixtures.ClearanceColor(clearance);
        return new Color(c.R / 255f, c.G / 255f, c.B / 255f);
    }

    void PlaceLeaves(Door d)
    {
        for (int side = 0; side < 2; side++)
            d.Leaves[side].Position = d.Shut[side] + d.Along * ((side == 0 ? -1f : 1f) * d.Open * (d.Width / 2f - 0.08f));
    }

    void TickDoors(float dt)
    {
        foreach (Door d in _doors)
        {
            if (Memory.Town is { GatesShut: true } && d.Target > 0.5f) { d.Target = 0f; Memory.DoorsOpen[d.Index] = false; }
            float target = d.Jammed ? 0.5f : d.Target;
            // A shaft door stays shut unless the car is at its stop.
            if (d.Lift >= 0 && !CarAt(d.Lift, d.Stop)) target = 0f;
            if (d.Open == target) continue;
            d.Open = Mathf.MoveToward(d.Open, target, dt * 1.6f);
            PlaceLeaves(d);
        }
        if (_lampTimer-- <= 0)
        {
            _lampTimer = 20;
            foreach (Door d in _doors.Where(d => d.Lamp != null))
            {
                bool may = Places.HasClearance(d.Clearance);
                d.Lamp.MaterialOverride = LampMaterial(may ? new Color("3fcf5e") : ClearanceColor(d.Clearance));
            }
        }
    }

    int _lampTimer;

    /// <summary>E on a door: open or shut it, if you may. Returns what happened, for the visor.</summary>
    public (bool Ok, string Message) UseDoor(Door d)
    {
        if (d.Jammed) return (false, "Jammed: it won't move further, but there's room to squeeze through");
        // A settlement that won't have you keeps its doors shut.
        if (Memory.Town is { GatesShut: true } && d.Target < 0.5f) return (false, "BARRED");
        if (d.Clearance != null && !Places.HasClearance(d.Clearance))
            return (false, $"LOCKED: needs a {d.Clearance.ToUpperInvariant()} keycard");
        if (d.Lift >= 0 && !CarAt(d.Lift, d.Stop))
        {
            CallLift(d.Lift, d.Stop);
            return (true, "The lift is coming");
        }
        d.Target = d.Target > 0.5f ? 0f : 1f;
        Memory.DoorsOpen[d.Index] = d.Target > 0.5f;
        Places.Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Clank, ToWorld(d.Centre), d.Blast ? 0.6f : 0.9f, -8f);
        return (true, d.Target > 0.5f ? (d.Clearance != null ? $"{d.Clearance.ToUpperInvariant()} card accepted" : "") : "");
    }

    // ---- Lifts ----------------------------------------------------------------------------------

    sealed class Car
    {
        public LiftShaft Shaft;
        public AnimatableBody3D Body;
        public CollisionShape3D Shape;
        public Vector3 Centre;            // local x, z middle of the shaft
        public float Y, Target;            // the car floor's walk height now, and where it is going
        public bool Ridden, Climbing;
        public float Bottom => Shaft.StopWalkY[0];
        public float Top => Shaft.StopWalkY[^1];
    }

    readonly List<Car> _cars = new();

    void BuildLifts()
    {
        var shafts = Inst.Extras.Lifts;
        Memory.LiftY ??= shafts.Select(s => (float)s.StopWalkY[^1]).ToArray(); // cars wait at the top stop
        for (int i = 0; i < shafts.Count; i++)
        {
            LiftShaft shaft = shafts[i];
            if (shaft.StopWalkY.Count == 0 || shaft.Footprint.Count == 0) continue;
            Int3 min = shaft.Footprint.Aggregate(Int3.Min), max = shaft.Footprint.Aggregate(Int3.Max);
            var size = new Vector3(max.X - min.X + 1 - 0.1f, 0.2f, max.Z - min.Z + 1 - 0.1f);
            var body = new AnimatableBody3D { Name = $"Lift {i}", CollisionLayer = Layers.World, CollisionMask = 0, SyncToPhysics = true };
            var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = size } };
            body.AddChild(shape);
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("5a5e64"), Metallic = 0.5f, Roughness = 0.5f } });
            // A hazard rim round the car's floor.
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(size.X, 0.05f, 0.08f) }, Position = new Vector3(0, 0.13f, size.Z / 2f - 0.05f), MaterialOverride = _band ??= new StandardMaterial3D { AlbedoColor = new Color("e0a800") } });
            AddChild(body);
            var car = new Car { Shaft = shaft, Body = body, Shape = shape, Centre = new Vector3((min.X + max.X + 1) / 2f, 0, (min.Z + max.Z + 1) / 2f), Y = Memory.LiftY[i] };
            car.Target = car.Y;
            body.Position = car.Centre + Vector3.Up * (car.Y - 0.1f);
            _cars.Add(car);
        }
    }

    /// <summary>The car stands level with a stop: empty, or carrying someone who has stopped there (and may want out), not someone riding past.</summary>
    bool CarAt(int lift, int stop) => lift < _cars.Count && stop >= 0 && stop < _cars[lift].Shaft.StopWalkY.Count
                                      && !_cars[lift].Climbing && Mathf.Abs(_cars[lift].Y - _cars[lift].Shaft.StopWalkY[stop]) < 0.25f;

    void CallLift(int lift, int stop)
    {
        if (lift < _cars.Count) _cars[lift].Target = _cars[lift].Shaft.StopWalkY[stop];
    }

    /// <summary>The shaft a local point stands in (its footprint, between its lowest stop and the top one's headroom), or -1.</summary>
    public int ShaftAt(Vector3 local)
    {
        for (int i = 0; i < _cars.Count; i++)
        {
            Car c = _cars[i];
            if (local.Y < c.Bottom - 0.5f || local.Y > c.Top + 2.5f) continue;
            int x = Mathf.FloorToInt(local.X), z = Mathf.FloorToInt(local.Z);
            foreach (Int3 f in c.Shaft.Footprint)
                if (f.X == x && f.Z == z) return i;
        }
        return -1;
    }

    /// <summary>The stop height nearest a walk height in a shaft (for easing a rider level with a doorway).</summary>
    public float NearestStop(int lift, float y)
    {
        float best = _cars[lift].Shaft.StopWalkY[0];
        foreach (int s in _cars[lift].Shaft.StopWalkY)
            if (MathF.Abs(s - y) < MathF.Abs(best - y)) best = s;
        return best;
    }

    /// <summary>Someone rides lift <paramref name="lift"/> with their feet at this local height (or nobody: -1).</summary>
    public void Ride(int lift, float feetY)
    {
        for (int i = 0; i < _cars.Count; i++)
        {
            Car c = _cars[i];
            bool riding = i == lift;
            if (riding)
            {
                float y = Mathf.Clamp(feetY, c.Bottom, c.Top);
                c.Climbing = c.Ridden && MathF.Abs(y - c.Y) > 0.004f;
                c.Y = y;
                c.Target = c.Y;
            }
            else if (c.Ridden)
            {
                // Got off: the car settles at the nearest stop.
                c.Target = NearestStop(i, c.Y);
                c.Climbing = false;
            }
            c.Ridden = riding;
            c.Shape.Disabled = riding; // the rider floats in the shaft; the car only shows under them
        }
    }

    void TickLifts(float dt)
    {
        for (int i = 0; i < _cars.Count; i++)
        {
            Car c = _cars[i];
            if (!c.Ridden) c.Y = Mathf.MoveToward(c.Y, c.Target, dt * 2.5f);
            Memory.LiftY[i] = c.Y;
            c.Body.Position = c.Centre + Vector3.Up * (c.Y - 0.1f);
        }
    }

    // ---- Keycards -------------------------------------------------------------------------------

    readonly List<(KeyCard Key, Node3D Node)> _keys = new();

    void BuildKeys()
    {
        var holder = new Node3D { Name = "Keys" };
        AddChild(holder);
        foreach (KeyCard k in Inst.Extras.Keys)
        {
            if (Memory.KeysTaken.Contains(k)) continue;
            Color c = ClearanceColor(k.Clearance);
            var card = new MeshInstance3D
            {
                Name = $"Keycard {k.Clearance}", Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.008f, 0.056f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.5f },
                Position = new Vector3(k.Cell.X + 0.55f, k.Cell.Y + 0.08f, k.Cell.Z + 0.42f), RotationDegrees = new Vector3(0, 23, 0),
            };
            holder.AddChild(card);
            _keys.Add((k, card));
        }
    }

    /// <summary>The keycard nearest a look ray within reach (local space), or null.</summary>
    public KeyCard KeyAlong(Vector3 eyeWorld, Vector3 dir, float reach)
    {
        Vector3 eye = FromWorld(eyeWorld);
        KeyCard best = null;
        float bestAlong = reach;
        foreach (var (k, node) in _keys)
        {
            Vector3 c = node.Position;
            float along = (c - eye).Dot(dir);
            if (along < 0f || along > bestAlong) continue;
            if ((eye + dir * along - c).Length() > 0.35f) continue;
            bestAlong = along;
            best = k;
        }
        return best;
    }

    public void TakeKey(KeyCard k)
    {
        Memory.KeysTaken.Add(k);
        int i = _keys.FindIndex(e => e.Key == k);
        if (i < 0) return;
        _keys[i].Node.QueueFree();
        _keys.RemoveAt(i);
    }

    // ---- Hidden rooms' tells ----------------------------------------------------------------------

    readonly Dictionary<SecretRoom, Node3D> _drafts = new();

    /// <summary>The draft through a hidden room's cracked wall: a faint drift of dust out of the cracks, into the room it is found from.</summary>
    void BuildTells()
    {
        foreach (SecretRoom s in Inst.Extras.Secrets)
        {
            if (Memory.Broken.Contains(s) || s.WallCells.Count == 0) continue;
            Int3 min = s.WallCells.Aggregate(Int3.Min), max = s.WallCells.Aggregate(Int3.Max);
            Int3 f = FoundFrom(s);
            var outward = new Vector3(f.X, f.Y, f.Z);
            var centre = new Vector3((min.X + max.X + 1) / 2f, min.Y + 1.2f, (min.Z + max.Z + 1) / 2f);
            var size = new Vector3(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1);
            var draft = new CpuParticles3D
            {
                Name = "Draft", Amount = 14, Lifetime = 5f, Preprocess = 5f, LocalCoords = false,
                Position = centre + outward * 0.56f,
                EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(MathF.Max(0.05f, (size.X - 1) / 2f + 0.3f) * (f.X != 0 ? 0.05f : 1f), 0.9f, MathF.Max(0.05f, (size.Z - 1) / 2f + 0.3f) * (f.Z != 0 ? 0.05f : 1f)),
                Direction = outward, Spread = 25f, InitialVelocityMin = 0.08f, InitialVelocityMax = 0.22f,
                Gravity = new Vector3(0f, -0.015f, 0f),
                Mesh = new QuadMesh
                {
                    Size = new Vector2(0.018f, 0.018f),
                    Material = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(0.85f, 0.8f, 0.7f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                    },
                },
            };
            AddChild(draft);
            _drafts[s] = draft;
        }
    }

    /// <summary>The way out of a hidden room's wall onto the side it is found from (the side that isn't the hidden room).</summary>
    public Int3 FoundFrom(SecretRoom s) => FoundFrom(Inst, s);

    public static Int3 FoundFrom(PoiInstance inst, SecretRoom s)
    {
        Int3 f = s.Facing.Vec(), wall = s.WallCells[0];
        PlacedModule hidden = inst.Layout.Modules[s.Module];
        Int3 a = wall + f;
        bool intoHidden = a.X >= hidden.Min.X && a.X <= hidden.Max.X && a.Z >= hidden.Min.Z && a.Z <= hidden.Max.Z;
        return intoHidden ? f * -1 : f;
    }

    /// <summary>The hidden room whose breakable wall holds this cell, if any.</summary>
    public SecretRoom SecretAt(Int3 cell)
    {
        foreach (SecretRoom s in Inst.Extras.Secrets)
            if (!Memory.Broken.Contains(s) && s.WallCells.Contains(cell)) return s;
        return null;
    }

    /// <summary>Breaks a hidden room's wall: its blocks go, the chunks round it are meshed again, the room behind opens.</summary>
    public void Break(SecretRoom s)
    {
        if (!Memory.Broken.Add(s)) return;
        foreach (Int3 c in s.WallCells) Inst.Blocks.Set(c, Block.Air);
        Remesh(s.WallCells);
        if (_drafts.Remove(s, out Node3D draft)) draft.QueueFree();
    }

    // ---- Lights -------------------------------------------------------------------------------

    /// <summary>
    /// The planet's far drawing keeps its own lamps (the 20 that carry from a distance); inside, a
    /// small pool of real lamps goes to the place's other lights nearest the survivor, flickering,
    /// pulsing or failing as their mode says.
    /// </summary>
    readonly List<(LightSpot Spot, float Phase)> _spots = new();
    readonly List<OmniLight3D> _pool = new();
    readonly Dictionary<OmniLight3D, int> _poolSpot = new();
    int _poolTimer;
    float _time;

    void BuildLights()
    {
        // The far drawing's choice (planet-surface's PoiLayer): beacons first, then lights above ground, highest first; 20 of them.
        var far = Inst.Lights.OrderByDescending(l => l.Mode == LightMode.Beacon).ThenByDescending(l => l.Cell.Y >= 0).ThenByDescending(l => l.Cell.Y).Take(20).ToHashSet();
        var rng = new RandomNumberGenerator { Seed = Inst.SpawnSeed };
        foreach (LightSpot s in Inst.Lights)
            if (!far.Contains(s) && s.Mode != LightMode.Dead) _spots.Add((s, rng.RandfRange(0f, 100f)));
        if (_spots.Count == 0) return;
        for (int i = 0; i < 10; i++)
        {
            var l = new OmniLight3D { Name = "Lamp", ShadowEnabled = false, OmniAttenuation = 1.4f, Visible = false };
            AddChild(l);
            _pool.Add(l);
        }
    }

    void TickLights(float dt)
    {
        _time += dt;
        if (_pool.Count == 0) return;
        if (--_poolTimer <= 0)
        {
            _poolTimer = 15;
            Vector3 at = FromWorld(Places.Focus());
            var nearest = _spots.Select((s, i) => (s, i)).OrderBy(t => SpotPos(t.s.Spot).DistanceSquaredTo(at)).Take(_pool.Count).ToList();
            for (int i = 0; i < _pool.Count; i++)
            {
                OmniLight3D l = _pool[i];
                if (i >= nearest.Count || SpotPos(nearest[i].s.Spot).DistanceTo(at) > 60f) { l.Visible = false; _poolSpot[l] = -1; continue; }
                var (s, index) = nearest[i];
                l.Visible = true;
                l.Position = SpotPos(s.Spot);
                l.LightColor = new Color(s.Spot.Color.R / 255f, s.Spot.Color.G / 255f, s.Spot.Color.B / 255f);
                l.OmniRange = s.Spot.Range;
                _poolSpot[l] = index;
            }
        }
        foreach (OmniLight3D l in _pool)
        {
            if (!l.Visible || !_poolSpot.TryGetValue(l, out int i) || i < 0) continue;
            var (spot, phase) = _spots[i];
            float t = _time + phase;
            if (spot.Style is { } style)
            {
                (float energy, Color colour) = PlanetSurface.Poi.PoiLitFaces.At(style, spot.Energy, _time);
                l.LightEnergy = energy;
                l.LightColor = colour;
                continue;
            }
            float wave = spot.Mode switch
            {
                LightMode.Flicker => Mathf.Sin(t * 3.1f) + Mathf.Sin(t * 11.7f) * 0.6f > 0.2f ? 0.9f : 0.04f,
                LightMode.Emergency => 0.65f + 0.2f * Mathf.Sin(t * 0.8f),
                LightMode.Bloom or LightMode.Pulse => 0.6f + 0.4f * Mathf.Sin(t * 1.3f),
                LightMode.Beacon => Mathf.Sin(t * 2.2f) > 0.2f ? 1f : 0.05f,
                _ => 0.92f + 0.08f * Mathf.Sin(t * 7.1f) * Mathf.Sin(t * 2.3f),
            };
            l.LightEnergy = spot.Energy * Mathf.Max(0f, wave);
        }
    }

    static Vector3 SpotPos(LightSpot s) => new(s.Cell.X + 0.5f, s.Cell.Y + 0.4f, s.Cell.Z + 0.5f);
}
