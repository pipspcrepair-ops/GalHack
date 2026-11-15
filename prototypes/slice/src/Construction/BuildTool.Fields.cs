using System;
using System.Collections.Generic;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Construction;

/// <summary>
/// A force field component in hand (design section 12). Pressing on a block's face sets the first
/// corner, in the cell before that face; the field's plane is the grid plane through it that faces
/// you most squarely. Dragging, the far corner is the cell where your look meets that plane, so the
/// rectangle follows where you stand and look. Releasing places it if it can go: every cell clear,
/// the far corner touching a block of the structure, each corner's emitter attaching as any block
/// must, and the component in your stack (or the structure's storage on its toolbar). The
/// silhouette is blue while it can, red while it can't. Placed, it uses that one component whatever
/// its size and an L-shaped emitter block appears in each corner. Taking an emitter down takes the
/// field down whole, the component coming back.
/// </summary>
public sealed partial class BuildTool
{
    sealed class FieldDrag
    {
        public Vessel Vessel;
        public Int3 First, Far;
        public int Axis;
    }

    FieldDrag _drag;
    MeshInstance3D _fieldGhost;
    ShaderMaterial _fieldCan, _fieldCant;

    /// <summary>Force fields placed and taken down since the start (tests).</summary>
    public int FieldsPlaced { get; private set; }
    public int FieldsTakenDown { get; private set; }

    /// <summary>A field is being drawn (the button held since it was pressed on a block).</summary>
    public bool Drawing => _drag != null;

    SliceRules.FieldRules FieldRules => _zone.Data.Rules.ForceFields;

    void TickField(ItemDef item, int slotIndex, bool held, bool pressed, bool cancel)
    {
        _planGhost.Visible = false;
        _deviceGhost.Visible = false;
        if (_decoGhost != null) _decoGhost.Visible = false;
        if (_drag != null && (cancel || _drag.Vessel.IsWreck || !_zone.Vessels.Contains(_drag.Vessel))) EndDrag();
        if (_drag == null)
        {
            Aim();
            if (!HasTarget || TargetVessel == null)
            {
                Hide();
                Verdict = HasTarget ? "NO STRUCTURE · CORE FIRST" : "";
                return;
            }
            _ghosts.HideGhosts();
            _ghosts.ShowOutline(TargetVessel.DrawTransform, TargetVessel.Structure, TargetCell, new Color(1, 1, 1, 0.8f));
            Verdict = $"{item.Name} · {Available(TargetVessel, item, slotIndex)}";
            if (!pressed) return;
            _drag = new FieldDrag { Vessel = TargetVessel, First = PlaceCell, Far = PlaceCell, Axis = FacingAxis(TargetVessel) };
        }
        Vessel v = _drag.Vessel;
        Structure s = v.Structure;
        _drag.Far = DragTo(v, _drag) ?? _drag.Far;
        var drawn = new ForceField(_drag.First, _drag.Far, _drag.Axis, item.Id);
        string why = FieldProblem(v, drawn, _drag.Far) ?? SourceProblem(v, item, slotIndex);
        ShowField(v, drawn, why == null);
        Verdict = why ?? $"{item.Name} · {drawn.Width} × {drawn.Height} · {drawn.Tiles * FieldRules.DrawPerTile:0.###} MW · {Available(v, item, slotIndex)}";
        if (held) return;
        EndDrag();
        if (why != null) Said?.Invoke(why, Tones.Caution);
        else if (Take(v, item, slotIndex, 1)) PlaceField(v, drawn);
    }

    void EndDrag()
    {
        _drag = null;
        if (_fieldGhost != null) _fieldGhost.Visible = false;
        _ghosts.HideGhosts();
    }

    /// <summary>The axis a structure's grid faces you along most squarely: the plane a field is drawn in is normal to it.</summary>
    int FacingAxis(Vessel v)
    {
        N.Vector3 look = N.Vector3.Abs(MathX.Unrotate(v.Body.Rotation, _survivor.WorldLook().ToNumerics()));
        return look.X >= look.Y && look.X >= look.Z ? 0 : look.Y >= look.Z ? 1 : 2;
    }

    /// <summary>The cell where your look meets the field's plane, at most the largest field away; null looking along it or away from it.</summary>
    Int3? DragTo(Vessel v, FieldDrag d)
    {
        float cell = v.Structure.Grid.CellSize;
        N.Vector3 eye = v.Body.ToLocal(_survivor.WorldEye().ToNumerics()) / cell, look = MathX.Unrotate(v.Body.Rotation, _survivor.WorldLook().ToNumerics());
        float along = Geometry.Get(look, d.Axis), t = (d.First[d.Axis] - Geometry.Get(eye, d.Axis)) / along;
        if (MathF.Abs(along) < 1e-3f || t <= 0f) return null;
        Int3 at = Int3.Round(eye + look * t).With(d.Axis, d.First[d.Axis]);
        int most = FieldRules.MaxSide - 1;
        for (int a = 0; a < 3; a++) at = at.With(a, Math.Clamp(at[a], d.First[a] - most, d.First[a] + most));
        return at;
    }

    /// <summary>Why a field drawn here can't go, or null: its size, its cells, its far corner, then each emitter as any block.</summary>
    string FieldProblem(Vessel v, ForceField f, Int3 far)
    {
        var rules = FieldRules;
        FieldError e = f.Check(v.Structure, far, rules.MinSide, rules.MaxSide);
        if (e != FieldError.None) return e switch
        {
            FieldError.TooSmall => $"FIELD · {rules.MinSide} × {rules.MinSide} AT LEAST",
            FieldError.TooBig => $"FIELD · {rules.MaxSide} × {rules.MaxSide} AT MOST",
            FieldError.Occupied => "FIELD · OCCUPIED",
            _ => "FAR CORNER · NOTHING TO TOUCH",
        };
        for (int i = 0; i < 4; i++)
            if (Problem(v, f.Corner(i), EmitterAt(v.Structure, f, i)) is string why) return $"CORNER · {why}";
        return null;
    }

    Block EmitterAt(Structure s, ForceField f, int corner) => Building.Make(s, ForceFields.Emitter(_catalogue), null, f.CornerRot(corner));

    /// <summary>The silhouette: the sheet and its four emitters, blue when it can go, red when it can't.</summary>
    void ShowField(Vessel v, ForceField f, bool can)
    {
        if (_fieldGhost == null)
        {
            (_fieldCan, _fieldCant) = (ForceFields.Material(ForceFields.Blue), ForceFields.Material(GhostPreview.Invalid));
            _fieldGhost = new MeshInstance3D { Name = "FieldGhost", Mesh = new QuadMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off };
            _zone.AddChild(_fieldGhost);
        }
        Structure s = v.Structure;
        Transform3D frame = ForceFields.Frame(s.Grid.CellSize, f.Min, f.Max, f.Axis, ForceFields.Inset, out Vector2 size);
        ShaderMaterial m = can ? _fieldCan : _fieldCant;
        m.SetShaderParameter("size", size);
        ((QuadMesh)_fieldGhost.Mesh).Size = size;
        _fieldGhost.MaterialOverride = m;
        _fieldGhost.GlobalTransform = v.DrawTransform * frame;
        _fieldGhost.Visible = true;
        var corners = new List<(Int3, Block, Color)>();
        for (int i = 0; i < 4; i++) corners.Add((f.Corner(i), EmitterAt(s, f, i), can ? ForceFields.Blue : GhostPreview.Invalid));
        _ghosts.ShowGhosts(v.DrawTransform, s, corners);
        _ghosts.HideOutline();
    }

    /// <summary>The field goes down: an emitter in each corner, the field at its tier's full strength, one component used (already taken).</summary>
    void PlaceField(Vessel v, ForceField f)
    {
        Structure s = v.Structure;
        for (int i = 0; i < 4; i++)
        {
            Building.Place(s, f.Corner(i), EmitterAt(s, f, i));
            if (v.IsBase) v.ClearGround(f.Corner(i));
        }
        f.Strength = FieldRules.TierOf(f.Tier)?.Strength ?? 0f;
        s.Fields.Add(f);
        v.ApplyEdits();
        Count(Bill.Of((f.Tier, 1)));
        FieldsPlaced++;
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, v.ToWorld(Middle(s, f)), 0.8f, -4f);
        BlockPlaced?.Invoke(v, XpOf(s, EmitterAt(s, f, 0)) * 4);
    }

    static N.Vector3 Middle(Structure s, ForceField f) => (f.Min.ToVector3() + f.Max.ToVector3()) * 0.5f * s.Grid.CellSize;

    /// <summary>The placed field an emitter at this cell belongs to, or null.</summary>
    static ForceField FieldOf(Structure s, Int3 cell)
    {
        foreach (ForceField f in s.Fields)
            for (int i = 0; i < 4; i++)
                if (f.Corner(i) == cell) return f;
        return null;
    }

    /// <summary>Takes a field down whole, as a block is: its emitters (any plating back by their share) and its one component, home the way a block's item goes.</summary>
    void TakeDown(Vessel v, ForceField f)
    {
        Structure s = v.Structure;
        Bill back = Creative ? new Bill() : Bill.Of((f.Tier, 1));
        for (int i = 0; i < 4; i++)
        {
            Int3 c = f.Corner(i);
            if (!s.TryGet(c, out Block b) || !s.TypeOf(b).IsFieldEmitter) continue;
            if (!Creative) foreach (var (id, n) in Building.PlatingWorth(s, b)) back.Add(id, n);
            RemoveResult r = Building.Remove(s, c, null);
            if (v.IsBase) _zone.Edits.ReleaseStructure(v.GroundKey(c));
            foreach (Structure piece in r.Wrecks) v.Sever(piece);
        }
        s.Fields.Remove(f);
        if (f.Holding) s.AirChanged();
        v.ApplyEdits();
        FieldsTakenDown++;
        TakenDown++;
        Vector3 at = v.ToWorld(Middle(s, f));
        GiveBack(back, v, at);
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Clank, at, 0.9f, -6f);
        if (!back.IsEmpty) Said?.Invoke($"+{Items.Describe(back, 3)}", Tones.Text);
    }
}
