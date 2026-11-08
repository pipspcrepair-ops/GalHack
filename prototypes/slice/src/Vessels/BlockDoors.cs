using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Places;
using Slice.Surface;
using N = System.Numerics;

namespace Slice.Vessels;

/// <summary>A door that stands open, as anything that must know reads it (a force field across it, later).</summary>
public readonly record struct OpenDoor(Vector3 Centre, Vector3 Normal, Vector2 Size, float Open);

/// <summary>
/// One door system with the places' doors: door blocks on bases and vessels open and shut with E,
/// the same two leaves sliding apart, the same prompt, and on your own structures they open for you
/// as you come up and shut behind you ("doors open for the player faction on their own"). The
/// block's own Open flag (building's) stays the truth for rooms, pathing and collision: opening
/// flips it at once so the way is clear while the leaves draw back; shutting flips it as the
/// leaves meet, so nobody is shut inside one. Locks and keycards are the places' (<see cref="Place.Door"/>).
/// </summary>
public partial class BlockDoors : Node3D
{
    public SurfaceZone Zone;
    public Func<Vector3> Feet;

    public const float OpenRange = 2.6f, ShutRange = 3.4f;

    sealed class Slide
    {
        public Vessel Vessel;
        public Int3 Cell;
        public Node3D Root;
        public MeshInstance3D A, B;
        public float Open, Target;
        public float Half, Y;
    }

    sealed class Known
    {
        public int Version = -1;
        public readonly List<Int3> Doors = new();
    }

    readonly Dictionary<(Vessel, Int3), Slide> _slides = new();
    readonly Dictionary<Vessel, Known> _known = new();
    /// <summary>Doors you shut by hand while beside them: they stay shut until you have stepped away.</summary>
    readonly HashSet<(Vessel, Int3)> _heldShut = new();
    int _tick;
    static StandardMaterial3D _body, _band;

    /// <summary>Times a door has opened or shut for you (tests).</summary>
    public int Opened { get; private set; }
    public int Shut { get; private set; }

    public static bool IsOpen(Vessel v, Int3 cell) => v.Structure.TryGet(cell, out Block b) && v.Structure.TypeOf(b).IsDoor && b.Open;

    /// <summary>
    /// A door's leaf, in its base cell's frame (cell units): across, and from foot to head. A large door is two
    /// cells, its leaf running on up into its top (3 m under the head); its leaf is the closed-only body parts
    /// of every cell of it.
    /// </summary>
    public static (float Half, float Y0, float Y1) Leaf(Structure s, Int3 root)
    {
        float half = 0.44f, y0 = float.MaxValue, y1 = float.MinValue;
        List<Int3> cells = Building.PieceCells(s, root);
        for (int i = 0; i < cells.Count; i++)
            foreach (Part part in s.TypeOf(s.Get(cells[i])).Parts)
            {
                if (!part.ClosedOnly || part.Style != PartStyle.Body || part.Grid is GridSize g && g != s.Grid.Size) continue;
                half = MathF.Max(-part.Min.X, part.Max.X);
                y0 = MathF.Min(y0, part.Min.Y + i);
                y1 = MathF.Max(y1, part.Max.Y + i);
            }
        return y0 > y1 ? (0.44f, -0.5f, 0.46f) : (half, y0, y1);
    }

    /// <summary>The middle of a door's leaf in the structure's frame (metres), and its height.</summary>
    static (N.Vector3 Centre, float Tall) Middle(Vessel v, Int3 root)
    {
        Structure s = v.Structure;
        var (_, y0, y1) = Leaf(s, root);
        N.Vector3 up = Building.UpOf(s.Get(root)).ToVector3();
        return (StructureBridge.CellCenter(s, root) + up * ((y0 + y1) * 0.5f * s.Grid.CellSize), (y1 - y0) * s.Grid.CellSize);
    }

    /// <summary>E on a door block: open or shut it. True if it was a door.</summary>
    public bool Use(Vessel v, Int3 cell)
    {
        cell = Building.Root(v.Structure, cell); // its top opens the door it belongs to
        if (!v.Structure.TryGet(cell, out Block b) || !v.Structure.TypeOf(b).IsDoor) return false;
        bool open = _slides.TryGetValue((v, cell), out Slide s) ? s.Target > 0.5f : b.Open;
        Set(v, cell, !open);
        if (open) _heldShut.Add((v, cell));
        else _heldShut.Remove((v, cell));
        return true;
    }

    /// <summary>The door block a look points at within reach (an open one has no collision for a ray to meet), nearest first.</summary>
    public (Vessel Vessel, Int3 Cell, bool Open)? Along(Vector3 eye, Vector3 dir, float reach)
    {
        (Vessel, Int3, bool)? best = null;
        float nearest = reach;
        foreach (var (v, k) in _known)
        {
            if (v.IsWreck) continue;
            float size = v.Structure.Grid.CellSize;
            foreach (Int3 cell in k.Doors)
            {
                var (mid, tall) = Middle(v, cell);
                Vector3 c = v.ToWorld(mid), up = (v.ToWorld(mid + Building.UpOf(v.Structure.Get(cell)).ToVector3()) - c).Normalized();
                float along = (c - eye).Dot(dir);
                Vector3 off = eye + dir * along - c;
                float rise = off.Dot(up);
                if (along < 0f || along > nearest || Mathf.Abs(rise) > tall * 0.5f || (off - up * rise).Length() > size * 0.5f) continue;
                nearest = along;
                best = (v, cell, _slides.TryGetValue((v, cell), out Slide s) ? s.Target > 0.5f : IsOpen(v, cell));
            }
        }
        return best;
    }

    void Set(Vessel v, Int3 cell, bool open)
    {
        if (!_slides.TryGetValue((v, cell), out Slide s))
        {
            if (IsOpen(v, cell) == open) return;
            s = Begin(v, cell, open);
        }
        s.Target = open ? 1f : 0f;
        if (open && !IsOpen(v, cell)) Flip(v, cell); // the way is clear at once
        Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Clank, v.ToWorld(StructureBridge.CellCenter(v.Structure, cell)), 1.1f, -14f);
        if (open) Opened++;
        else Shut++;
    }

    static void Flip(Vessel v, Int3 cell)
    {
        Building.Toggle(v.Structure, cell);
        v.ApplyEdits();
    }

    /// <summary>The two leaves, drawn while they move: each half of the block's leaf, drawing back into the frame.</summary>
    Slide Begin(Vessel v, Int3 cell, bool opening)
    {
        v.Structure.TryGet(cell, out Block b);
        float size = v.Structure.Grid.CellSize;
        _body ??= new StandardMaterial3D { AlbedoColor = new Color("f4a100"), Roughness = 0.6f, Metallic = 0.3f };
        _band ??= new StandardMaterial3D { AlbedoColor = new Color("2b2d42"), Roughness = 0.7f };
        Mat3 m = Orientations.Matrix(b.Rot);
        var basis = new Basis(m.X.ToVector3().ToGodot(), m.Y.ToVector3().ToGodot(), m.Z.ToVector3().ToGodot());
        var root = new Node3D { Name = "Door leaves", Transform = new Transform3D(basis, StructureBridge.CellCenter(v.Structure, cell).ToGodot()) };
        v.Node.AddChild(root);
        // The block's leaf: x -0.44..0.44 and z -0.1..0.1 of a cell, from its foot to its head (into a large door's top).
        var (across, y0, y1) = Leaf(v.Structure, cell);
        float half = across * size;
        var leaf = new BoxMesh { Size = new Vector3(half, (y1 - y0) * size, 0.2f * size) };
        var a = new MeshInstance3D { Mesh = leaf, MaterialOverride = _body };
        var c = new MeshInstance3D { Mesh = leaf, MaterialOverride = _body };
        root.AddChild(a);
        root.AddChild(c);
        var s = new Slide { Vessel = v, Cell = cell, Root = root, A = a, B = c, Half = half, Y = (y0 + y1) * 0.5f * size, Open = opening ? 0f : 1f };
        _slides[(v, cell)] = s;
        Pose(s, size);
        return s;
    }

    static void Pose(Slide s, float size)
    {
        // Sliding apart into the frame: each leaf narrows toward its side as it goes.
        float w = MathF.Max(0.02f, 1f - s.Open), y = s.Y;
        s.A.Scale = new Vector3(w, 1f, 1f);
        s.B.Scale = new Vector3(w, 1f, 1f);
        s.A.Position = new Vector3(-s.Half * (0.5f + s.Open * 0.5f), y, 0f);
        s.B.Position = new Vector3(s.Half * (0.5f + s.Open * 0.5f), y, 0f);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Zone?.Planet == null || Feet == null) return;
        float dt = (float)delta;
        foreach (var (key, s) in _slides.ToList())
        {
            if (!GodotObject.IsInstanceValid(s.Root) || !s.Vessel.Structure.TryGet(s.Cell, out _))
            {
                if (GodotObject.IsInstanceValid(s.Root)) s.Root.QueueFree();
                _slides.Remove(key);
                continue;
            }
            s.Open = Mathf.MoveToward(s.Open, s.Target, dt * 2.5f);
            Pose(s, s.Vessel.Structure.Grid.CellSize);
            if (s.Open != s.Target) continue;
            if (s.Target < 0.5f && IsOpen(s.Vessel, s.Cell)) Flip(s.Vessel, s.Cell); // the leaves have met: solid again
            s.Root.QueueFree();
            _slides.Remove(key);
        }
        if (++_tick % 6 != 0) return;
        Vector3 feet = Feet();
        foreach (Vessel v in Zone.Vessels)
        {
            if (v.IsWreck || v.IsRuin || v.Structure.Count == 0) continue; // your own bases and vessels
            var (min, max) = v.Structure.Bounds();
            float reach = (max - min).ToVector3().Length() * v.Structure.Grid.CellSize * 0.6f + ShutRange + 2f;
            if (v.Body.WorldPosition.ToGodot().DistanceTo(feet) > reach) continue;
            if (!_known.TryGetValue(v, out Known k)) _known[v] = k = new Known();
            if (k.Version != v.Structure.Version)
            {
                k.Version = v.Structure.Version;
                k.Doors.Clear();
                foreach (var (cell, block) in v.Structure.Blocks())
                    if (v.Structure.TypeOf(block).IsDoor && !v.Structure.TypeOf(block).IsPieceTop) k.Doors.Add(cell);
            }
            foreach (Int3 cell in k.Doors)
            {
                float d = v.ToWorld(StructureBridge.CellCenter(v.Structure, cell)).DistanceTo(feet + Vector3.Up * 0.9f);
                bool open = _slides.TryGetValue((v, cell), out Slide s) ? s.Target > 0.5f : IsOpen(v, cell);
                if (d > ShutRange) _heldShut.Remove((v, cell));
                if (d < OpenRange && !open && !_heldShut.Contains((v, cell))) Set(v, cell, true);
                else if (d > ShutRange && open) Set(v, cell, false);
            }
        }
    }

    /// <summary>Every door standing open or opening, on structures and in the places near (a force field across an open door reads this).</summary>
    public IEnumerable<OpenDoor> OpenDoors(PoiPlaces places)
    {
        foreach (Vessel v in Zone.Vessels)
        {
            if (v.IsWreck || !_known.TryGetValue(v, out Known k)) continue;
            float size = v.Structure.Grid.CellSize;
            foreach (Int3 cell in k.Doors)
            {
                if (!IsOpen(v, cell) || !v.Structure.TryGet(cell, out Block b)) continue;
                Vector3 z = Orientations.Matrix(b.Rot).Z.ToVector3().ToGodot();
                float open = _slides.TryGetValue((v, cell), out Slide s) ? s.Open : 1f;
                var (mid, tall) = Middle(v, cell);
                yield return new OpenDoor(v.ToWorld(mid), z, new Vector2(size, tall), open);
            }
        }
        if (places == null) yield break;
        foreach (Place p in places.Standing)
            foreach (Place.Door d in p.Doors)
                if (d.Open > 0.02f)
                    yield return new OpenDoor(p.ToWorld(d.Centre), d.Along.Cross(Vector3.Up).Normalized(), new Vector2(d.Width, d.Cells.Max(c => c.Y) - d.Cells.Min(c => c.Y) + 1), d.Open);
    }
}
