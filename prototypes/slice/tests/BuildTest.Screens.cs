using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Production;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Ui;
using Slice.Vessels;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// A machine's screen is clear: from where its user stands, a grid of rays over the glass meets nothing of the machine's own
/// model (its gantry and nozzle, vents, status screen, hopper) for every constructor block (all 24 turns, free on
/// every side and boxed in but for its front), every core, and the survival constructor; and up close at the survival
/// constructor no marker and no use prompt covers the glass. The "before" figures replay the old placement and drawing.
/// </summary>
public partial class BuildTest
{
    static bool SegmentHitsBox(Vector3 a, Vector3 b, Vector3 lo, Vector3 hi)
    {
        float t0 = 0f, t1 = 1f;
        for (int i = 0; i < 3; i++)
        {
            float o = a[i], d = b[i] - a[i];
            if (MathF.Abs(d) < 1e-9f)
            {
                if (o < lo[i] || o > hi[i]) return false;
                continue;
            }
            float ta = (lo[i] - o) / d, tb = (hi[i] - o) / d;
            if (ta > tb) (ta, tb) = (tb, ta);
            t0 = MathF.Max(t0, ta);
            t1 = MathF.Min(t1, tb);
            if (t0 > t1) return false;
        }
        return true;
    }

    /// <summary>Rays from a standing user over a 9 by 9 grid of the glass, against boxes: how many meet one first.</summary>
    static int RaysBlocked(Transform3D glass, Vector2 size, List<(Vector3 Lo, Vector3 Hi)> boxes, out int cast)
    {
        Vector3 normal = glass.Basis.Z.Normalized();
        Vector3 eye = glass.Origin + normal * 1.3f + Vector3.Up * 0.1f;
        int blocked = 0;
        cast = 0;
        for (int i = 0; i <= 8; i++)
            for (int j = 0; j <= 8; j++)
            {
                Vector3 at = glass * new Vector3((i / 4f - 1f) * size.X / 2f, (j / 4f - 1f) * size.Y / 2f, 0.003f);
                cast++;
                if (boxes.Any(b => SegmentHitsBox(eye, at, b.Lo, b.Hi))) blocked++;
            }
        return blocked;
    }

    static List<(Vector3 Lo, Vector3 Hi)> ModelBoxes(Structure s, Int3 cell)
    {
        Block b = s.Get(cell);
        float size = s.Grid.CellSize;
        Vector3 centre = new Vector3(cell.X, cell.Y, cell.Z) * size;
        Vector3 Axis(Dir d) { Int3 v = d.Vec(); return new Vector3(v.X, v.Y, v.Z); }
        Vector3 ax = Axis(Orientations.ToWorld(b.Rot, Dir.PosX)), ay = Axis(Orientations.ToWorld(b.Rot, Dir.PosY)), az = Axis(Orientations.ToWorld(b.Rot, Dir.PosZ));
        var boxes = new List<(Vector3, Vector3)>();
        foreach (Part p in s.TypeOf(b).Detail)
        {
            Vector3 lo = new(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            for (int k = 0; k < 8; k++)
            {
                var c = new Vector3((k & 1) == 0 ? p.Min.X : p.Max.X, (k & 2) == 0 ? p.Min.Y : p.Max.Y, (k & 4) == 0 ? p.Min.Z : p.Max.Z);
                Vector3 w = centre + (ax * c.X + ay * c.Y + az * c.Z) * size;
                lo = lo.Min(w);
                hi = hi.Max(w);
            }
            boxes.Add((lo, hi));
        }
        return boxes;
    }

    IEnumerable<int> ScreenClearChecks()
    {
        GD.Print("-- machine screens: nothing of the machine in front of the glass --");
        Catalogue cat = _base.Structure.Catalogue;
        BlockType armour = cat.Types.First(t => t != null && t.IsArmor);
        var kinds = cat.Types.Where(t => t != null && !t.IsArmor && (t.Function == "core" || Items.Machines.ForBlock(t.Id)?.Kind == MachineKind.Constructor)).ToList();
        int cast = 0, before = 0, after = 0, cases = 0, fronts = 0;
        var worst = "";
        var size = new Vector2(0.52f, 0.74f);
        foreach (BlockType t in kinds)
            for (int rot = 0; rot < Orientations.Count; rot++)
                for (int boxed = 0; boxed < 2; boxed++)
                {
                    var st = new Structure(cat);
                    st.Set(Int3.Zero, Building.Make(st, t, rot: rot));
                    Dir front = Orientations.ToWorld(rot, Dir.NegZ);
                    if (boxed == 1)
                        foreach (Dir d in new[] { Dir.NegZ, Dir.PosX, Dir.NegX, Dir.PosZ })
                            if (d != front) st.Set(d.Vec(), Building.Make(st, armour));
                    var boxes = ModelBoxes(st, Int3.Zero);
                    // Before: the first free side from NegZ, 3.5 cm off the face.
                    Dir? old = null;
                    foreach (Dir d in new[] { Dir.NegZ, Dir.PosX, Dir.NegX, Dir.PosZ })
                        if (!st.Has(d.Vec())) { old = d; break; }
                    if (old is Dir od) before += RaysBlocked(MachineScreens.BlockScreenLocal(st, Int3.Zero, od, 0.035f), size, boxes, out _);
                    if (MachineScreens.BlockScreenFace(st, Int3.Zero) is not var (face, off)) continue;
                    int hit = RaysBlocked(MachineScreens.BlockScreenLocal(st, Int3.Zero, face, off), size, boxes, out int n);
                    cast += n;
                    after += hit;
                    cases++;
                    if (face == front) fronts++;
                    if (hit > 0 && worst == "") worst = $"{t.Id} turn {rot} boxed {boxed} on {face}";
                }
        Check(cases > 0 && after == 0, $"every machine block's screen (constructors and cores, {cases} cases in 24 turns, {fronts} boxed in so on the front) meets none of its own model: {after} of {cast} rays blocked ({before} blocked by the old placement){(worst == "" ? "" : "; first " + worst)}");

        // The survival constructor and a screen near it, from where its user stands.
        Vector3 spot = Ground(_site + new Vector3(-9f, 0f, 6f));
        Device dev = Device.Place(_game.Zone, Items.Machines.Get("survival"), "survival-constructor", spot, 0.6f);
        _game.MachineScreens.Sync();
        foreach (int x in _drive.Wait(3)) yield return x;
        MachineScreens.Mounted m = _game.MachineScreens.Screens.TryGetValue((dev, Int3.Zero), out var mm) ? mm : null;
        if (m == null) { Check(false, "the survival constructor has a screen"); yield break; }
        var meshes = new List<(Vector3 Lo, Vector3 Hi)>();
        foreach (Node nd in dev.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (m.Holder.IsAncestorOf(nd)) continue;
            var mi = (MeshInstance3D)nd;
            Aabb a = mi.Mesh.GetAabb();
            Vector3 lo = new(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            for (int k = 0; k < 8; k++)
            {
                Vector3 w = mi.GlobalTransform * (a.Position + new Vector3((k & 1) * a.Size.X, ((k >> 1) & 1) * a.Size.Y, ((k >> 2) & 1) * a.Size.Z));
                lo = lo.Min(w);
                hi = hi.Max(w);
            }
            meshes.Add((lo, hi));
        }
        Vector2 gsize = m.Screen.SizeMeters;
        Transform3D oldLocal = DiegeticUi.Screens.ScreenMount.OnFace(new Vector3(0f, 0f, -0.3f), Vector3.Forward, 0f, tipBackDegrees: 8f, rise: -0.02f);
        int deviceBefore = RaysBlocked(dev.GlobalTransform * oldLocal, gsize, meshes, out int deviceCast);
        int deviceAfter = RaysBlocked(m.Screen.GlobalTransform, gsize, meshes, out _);
        Check(deviceAfter == 0, $"the survival constructor's screen meets none of its cabinet: {deviceAfter} of {deviceCast} rays blocked ({deviceBefore} by the old height)");

        // Up close at it: markers laid over its glass, and the use prompt.
        var s = _game.Survivor;
        Vector3 normal = m.Screen.GlobalBasis.Z.Normalized();
        Vector3 stand = Ground(m.Screen.GlobalPosition + normal * 1.3f);
        s.PlaceOutside(((stand + Vector3.Up * 0.3f)).ToNumerics());
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in _drive.LookAt(m.Screen.GlobalPosition, 10)) yield return x;
        Camera3D cam = _game.Camera;
        Vector3 eye = cam.GlobalPosition;
        Vector3 fwd = (m.Screen.GlobalPosition - eye).Normalized();
        Vector3 flat = new Vector3(fwd.X, 0f, fwd.Z).Normalized();
        for (int k = -3; k <= 3; k++)
        {
            Vector3 w = eye + flat.Rotated(Vector3.Up, k * 0.08f) * (30f + 6f * MathF.Abs(k));
            _game.Spaceflight.Charts.Mark(w.X, w.Z);
        }
        foreach (int x in _drive.Wait(3)) yield return x;
        var win = new Vector2(1920, 1080);
        Vector2 Proj(Vector3 p)
        {
            Vector3 l = cam.GlobalTransform.AffineInverse() * p;
            Vector4 clip = Projection.CreatePerspective(cam.Fov, win.X / win.Y, cam.Near, cam.Far, false) * new Vector4(l.X, l.Y, l.Z, 1f);
            return new Vector2((clip.X / clip.W + 1f) / 2f * win.X, (1f - clip.Y / clip.W) / 2f * win.Y);
        }
        Rect2 glass = default;
        bool first = true;
        foreach (var (cx, cy) in new[] { (-1f, 1f), (1f, 1f), (1f, -1f), (-1f, -1f) })
        {
            Vector2 pp = Proj(m.Screen.GlobalTransform * new Vector3(cx * gsize.X / 2f, cy * gsize.Y / 2f, 0f));
            glass = first ? new Rect2(pp, Vector2.Zero) : glass.Expand(pp);
            first = false;
        }
        string label = "E  Open survival constructor";
        int Overlaps(out int markers)
        {
            _game.Visor.LayoutMarkers(win, 1f); // settles the hysteresis
            var placed = _game.Visor.LayoutMarkers(win, 1f);
            markers = placed.Count;
            int n = placed.Count(p => glass.Intersects(p.Icon) || p.Labelled && glass.Intersects(p.Label));
            if (glass.Intersects(_game.Visor.PromptBox(win, 1f, label))) n++;
            return n;
        }
        Visor.HoldScreensClear = false;
        int overBefore = Overlaps(out int shownBefore);
        Visor.HoldScreensClear = true;
        int overAfter = Overlaps(out int shownAfter);
        Check(overAfter == 0 && overBefore > 0, $"up close at the survival constructor no marker and no use prompt covers its glass ({glass.Size.X:F0} by {glass.Size.Y:F0} px): {overAfter} overlaps with {shownAfter} markers up, {overBefore} before ({shownBefore} up)");

        _game.Zone.Devices.Remove(dev);
        dev.QueueFree();
        _game.MachineScreens.Sync();
    }
}
