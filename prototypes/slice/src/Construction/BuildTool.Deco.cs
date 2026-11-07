using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics.Engine;
using PhysicsSandbox.Bridges;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Places;
using Slice.Vessels;
using Int3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;

namespace Slice.Construction;

public sealed partial class BuildTool
{
    // The universal deco block in hand: the piece is picked from a gallery (the strip above the
    // toolbar) before placing. Each placement uses one block whatever the piece; pieces go on the
    // grid, floor pieces on a floor face, wall pieces on a wall face, ceiling pieces under a ceiling.

    /// <summary>The piece the gallery has chosen, and the theme (deco set) it is filtered to (null: every theme).</summary>
    public string DecoPieceId;
    public string DecoTheme;

    /// <summary>Quarter turns of the piece about the vertical (R turns it).</summary>
    public int DecoYaw;

    MeshInstance3D _decoGhost;
    string _decoGhostPiece;

    /// <summary>The pieces the gallery offers: those unlocked (every one in Creative) for a grid, in the chosen theme.</summary>
    public List<DecoPiece> DecoChoices(bool smallGrid)
    {
        Decor decor = Decor.Shared;
        if (decor == null) return new List<DecoPiece>();
        return decor.Catalogue.Pieces
            .Where(p => (smallGrid ? p.OnSmall : p.OnLarge) && (DecoTheme == null || p.InSet(DecoTheme))
                        && (Creative || Progress == null || (Items.BlockItem(p.Id, smallGrid) is ItemDef i && Progress.ItemAllowed(i))))
            .OrderBy(p => p.Set != DecoTheme).ThenBy(p => p.Set, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The themes that have any piece to offer on a grid.</summary>
    public List<DecoSet> DecoThemes(bool smallGrid)
    {
        Decor decor = Decor.Shared;
        if (decor == null) return new List<DecoSet>();
        string was = DecoTheme;
        DecoTheme = null;
        var offered = DecoChoices(smallGrid).Select(p => p.Set).ToHashSet();
        DecoTheme = was;
        return decor.Catalogue.Sets.Where(s => offered.Contains(s.Id)).ToList();
    }

    /// <summary>The gallery's current piece (the first on offer if none is chosen yet).</summary>
    public DecoPiece CurrentDeco(bool smallGrid)
    {
        List<DecoPiece> choices = DecoChoices(smallGrid);
        if (choices.Count == 0) return null;
        return choices.FirstOrDefault(p => p.Id == DecoPieceId) ?? choices[0];
    }

    /// <summary>Wheel: the next or previous piece in the gallery.</summary>
    public void NextPiece(int step, bool smallGrid)
    {
        List<DecoPiece> choices = DecoChoices(smallGrid);
        if (choices.Count == 0) return;
        int at = Math.Max(0, choices.FindIndex(p => p.Id == DecoPieceId));
        DecoPieceId = choices[(at + (step > 0 ? 1 : -1) + choices.Count) % choices.Count].Id;
    }

    /// <summary>G: the next theme (every theme, then each in turn).</summary>
    public void NextTheme(bool smallGrid)
    {
        List<DecoSet> themes = DecoThemes(smallGrid);
        if (themes.Count == 0) return;
        int at = DecoTheme == null ? -1 : themes.FindIndex(t => t.Id == DecoTheme);
        DecoTheme = at + 1 >= themes.Count ? null : themes[at + 1].Id;
        DecoPieceId = DecoChoices(smallGrid).FirstOrDefault()?.Id;
    }

    public void TurnDeco() => DecoYaw = (DecoYaw + 1) & 3;

    /// <summary>
    /// Where a piece would go: the cells it takes, its place in the structure's frame, or why it can't
    /// go there. The aimed block is <paramref name="target"/> and <paramref name="place"/> the empty cell
    /// against its face.
    /// </summary>
    public (List<Int3> Cells, Transform3D Local, int Yaw, string Why) PlanDeco(Vessel v, DecoPiece piece, Int3 target, Int3 place, int turns)
    {
        Structure s = v.Structure;
        float cell = s.Grid.CellSize;
        Int3 n = place - target;
        bool wall = n.Y == 0;
        bool floor = n.Y > 0, ceiling = n.Y < 0;
        var none = new List<Int3>();
        string fail(string why) => why;
        if (piece.Mount == DecoMount.Ceiling && !ceiling) return (none, default, 0, $"{piece.Name} hangs from a ceiling: aim at the underside of a block");
        if (piece.Mount == DecoMount.Wall && !wall) return (none, default, 0, $"{piece.Name} mounts on a wall: aim at the side of a block");
        if (piece.Mount is DecoMount.Floor or DecoMount.Top && !floor) return (none, default, 0, $"{piece.Name} stands on a floor: aim at the top of a block");

        // Footprint in cells (the piece's size is in half-metres).
        int W = Math.Max(1, (int)MathF.Ceiling(piece.Width / cell - 1e-4f)), D = Math.Max(1, (int)MathF.Ceiling(piece.Depth / cell - 1e-4f)), H = Math.Max(1, (int)MathF.Ceiling(piece.Height / cell - 1e-4f));
        var cells = new List<Int3>();
        Vector3 centre;
        float yawDeg;
        int yawOut = turns;
        if (wall)
        {
            // Front faces away from the wall; the width runs along the wall, the depth stands out of it.
            Int3 t = new(-n.Z, 0, n.X);
            for (int w = 0; w < W; w++)
                for (int h = 0; h < H; h++)
                    for (int d = 0; d < D; d++)
                        cells.Add(place + t * w + new Int3(0, h, 0) + n * d);
            Vector3 anchorCentre = StructureBridge.CellCenter(s, place).ToGodot();
            Vector3 nv = new(n.X, 0, n.Z), tv = new(t.X, 0, t.Z);
            Vector3 wallPoint = anchorCentre - nv * (cell * 0.5f) + tv * ((W - 1) * 0.5f * cell);
            float y0 = anchorCentre.Y - cell * 0.5f + Math.Clamp(piece.MountY, 0f, Math.Max(0f, H * cell - piece.Height));
            centre = wallPoint + nv * (piece.Depth * 0.5f);
            centre.Y = y0;
            yawDeg = Mathf.RadToDeg(MathF.Atan2(-nv.X, -nv.Z));
            yawOut = -1;
        }
        else
        {
            int fx = turns % 2 == 0 ? W : D, fz = turns % 2 == 0 ? D : W;
            int y0cell = ceiling ? place.Y - (H - 1) : place.Y;
            for (int x = 0; x < fx; x++)
                for (int z = 0; z < fz; z++)
                    for (int h = 0; h < H; h++)
                        cells.Add(new Int3(place.X + x, y0cell + h, place.Z + z));
            Vector3 a = StructureBridge.CellCenter(s, place).ToGodot();
            float y = ceiling ? a.Y + cell * 0.5f - piece.Height : a.Y - cell * 0.5f;
            centre = new Vector3(a.X + (fx - 1) * 0.5f * cell, y, a.Z + (fz - 1) * 0.5f * cell);
            yawDeg = turns * 90f;
        }
        var local = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), centre);
        foreach (Int3 c in cells)
        {
            if (s.Has(c)) return (cells, local, yawOut, "Something is already there");
            if (v.DecoAt(c, out _)) return (cells, local, yawOut, "Another deco piece is there");
        }
        return (cells, local, yawOut, null);
    }

    void TickDeco(ItemDef item, int slotIndex, bool place, bool remove)
    {
        _planGhost.Visible = false;
        _deviceGhost.Visible = false;
        _ghosts.HideGhosts();
        Aim();
        bool small = TargetVessel != null && BuildingBridge.IsSmall(TargetVessel.Structure);
        DecoPiece piece = CurrentDeco(small);
        if (Decor.Shared == null || piece == null)
        {
            HideDeco();
            Verdict = "No deco piece unlocked yet: the tech tree's deco tab has them";
            return;
        }
        if (!HasTarget || TargetVessel == null)
        {
            HideDeco();
            Verdict = HasTarget ? "Deco goes onto a structure: set a core down first" : "";
            return;
        }
        Vessel v = TargetVessel;
        if (remove)
        {
            RemoveDecoNear(v);
            return;
        }
        var (cells, local, _, why) = PlanDeco(v, piece, TargetCell, PlaceCell, DecoYaw);
        why ??= (small ? piece.OnSmall : piece.OnLarge) ? null : $"{piece.Name} is a {(small ? "large" : "small")}-grid piece";
        why ??= InSurvivorAny(v, cells) ? "Step out of the way to build there" : null;
        why ??= SourceProblem(v, item, slotIndex);
        if (cells.Count > 0) ShowDecoGhost(v, piece, local, why == null);
        else HideDeco();
        Verdict = why ?? $"{piece.Name} · {Available(v, item, slotIndex)}";
        _ghosts.ShowOutline(v.DrawTransform, v.Structure, TargetCell, new Color(1, 1, 1, 0.8f));
        if (!place) return;
        if (why != null)
        {
            Said?.Invoke(why, Tones.Caution);
            return;
        }
        if (!TakeOne(v, item, slotIndex))
        {
            Said?.Invoke(SourceProblem(v, item, slotIndex) ?? $"No {item.Name} left", Tones.Caution);
            return;
        }
        v.AddDeco(piece.Id, PlaceCell, DecoYaw, cells, local);
        Count(Bill.Of((item.Id, 1)));
        Placed++;
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, v.ToWorld(StructureBridge.CellCenter(v.Structure, PlaceCell)), 1.3f, -6f);
    }

    /// <summary>Does a survivor stand in one of these cells?</summary>
    bool InSurvivorAny(Vessel v, List<Int3> cells) => cells.Any(c => InSurvivor(v, c));

    /// <summary>
    /// A piece standing on the face under the crosshair (or in the cell against it) comes down: its
    /// deco block returns to your bag (the core's storage when you build from the remote bar).
    /// </summary>
    public bool RemoveDecoNear(Vessel v)
    {
        if (!(v.DecoAt(PlaceCell, out DecoPlaced d) || v.DecoAt(TargetCell, out d))) return false;
        v.RemoveDeco(d);
        TakenDown++;
        if (!Creative && Items.TryGet(DecoCatalogue.UniversalItem, out ItemDef block))
        {
            Bill back = Bill.Of((block.Id, 1));
            Vector3 at = v.ToWorld(StructureBridge.CellCenter(v.Structure, d.Anchor));
            GiveBack(back, v, at);
            Said?.Invoke($"Taken down: {Decor.Shared?.Catalogue.Piece(d.Piece).Name}, +1 {block.Name}", Tones.Text);
        }
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Clank, v.ToWorld(StructureBridge.CellCenter(v.Structure, d.Anchor)), 0.9f, -6f);
        return true;
    }

    /// <summary>Deconstruct mode on a piece: aiming at the face under it takes it down.</summary>
    public bool DecoToDeconstruct(out string verdict)
    {
        verdict = null;
        if (!HasTarget || TargetVessel == null) return false;
        if (!(TargetVessel.DecoAt(PlaceCell, out DecoPlaced d) || TargetVessel.DecoAt(TargetCell, out d))) return false;
        verdict = $"Deconstruct {Decor.Shared?.Catalogue.Piece(d.Piece).Name}{(Creative ? "" : ": +1 Deco block")}";
        return true;
    }

    // ---- The ghost -------------------------------------------------------------------------------------

    void ShowDecoGhost(Vessel v, DecoPiece piece, Transform3D local, bool valid)
    {
        Decor decor = Decor.Shared;
        if (_decoGhost == null)
        {
            _decoGhost = new MeshInstance3D { Name = "DecoGhost", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off };
            _zone.AddChild(_decoGhost);
        }
        if (_decoGhostPiece != piece.Id)
        {
            var look = new DecoLook(piece.Id, DecoState.Intact, DecoFall.None, true, DecoCover.Vines, piece.Palette);
            _decoGhost.Mesh = decor.Meshes.Mesh(look);
            _decoGhostPiece = piece.Id;
        }
        _decoGhost.MaterialOverride = BlockMaterials.Ghost(valid ? GhostPreview.Valid : GhostPreview.Invalid, 0.35f);
        _decoGhost.GlobalTransform = v.DrawTransform * local;
        _decoGhost.Visible = true;
    }

    void HideDeco()
    {
        if (_decoGhost != null) _decoGhost.Visible = false;
        _ghosts.HideOutline();
    }
}
