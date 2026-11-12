using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Player;
using Slice.Vessels;
using S = GalHack.Structures;

namespace Slice.Tests;

/// <summary>
/// The seated body (owner, 12 Nov: the character vanished in a chair). On a fresh random world,
/// every chair and cockpit in the catalogue goes on a small floor of its own, turned a random way,
/// and the castaway sits in it. Then: the figure is drawn for the player's own first-person camera
/// with only its head left out, the lap in view and the eye clear of everything drawn; its hips
/// are on the chair's seat and its feet on the chair's floor; the camera is at the chair's eye
/// point; its head is inside the part and under its top, and nothing of it pokes out of the part's
/// cells. From outside (third person) the head is drawn too. The hands stay down: the figure's
/// own arms rest on its lap.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/SeatedTest.tscn
/// </summary>
public partial class SeatedTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>How near is near enough for a point on the body, m.</summary>
    const float Near = 0.03f;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        S.Catalogue cat = _game.Zone.Data.Catalogue;
        var chairs = cat.Types.Where(t => t != null && Vessel.IsChair(t)).ToList();
        Check(chairs.Count >= 3, $"chairs in the catalogue: {string.Join(", ", chairs.Select(t => t.Id))}");
        Vector3 site = new Playthrough(_game).FlatSpot(_game.Start, 40f, span: 14f);
        var rng = new Random();
        foreach (S.BlockType chair in chairs)
        {
            site += new Vector3(18f, 0f, 0f);
            foreach (int x in Sit(chair, site, rng.Next(4))) yield return x;
        }
    }

    /// <summary>A floor with the chair on it, turned this many quarter turns, stood clear of the ground; seated in it, the checks.</summary>
    IEnumerable<int> Sit(S.BlockType type, Vector3 site, int turns)
    {
        S.Catalogue cat = _game.Zone.Data.Catalogue;
        var s = new S.Structure(cat) { Name = $"Seat {type.Id}" };
        S.BlockType floor = cat.Type("base-block");
        for (int x = -6; x <= 6; x++)
            for (int z = -6; z <= 6; z++) s.Set(new S.Int3(x, 0, z), S.Building.Make(s, x == -6 && z == -6 ? cat.CoreType : floor));
        var anchor = new S.Int3(0, 1, 0);
        S.Block block = S.Building.Make(s, type, null, S.Orientations.Rotate(S.Orientations.Identity, 1, turns));
        s.Set(anchor, block);
        s.DirtyChunks.Clear();
        float cell = s.Grid.CellSize, highest = float.MinValue;
        for (int i = -8; i <= 8; i += 2)
            for (int k = -8; k <= 8; k += 2) highest = MathF.Max(highest, _game.Zone.GroundAt(site.X + i, site.Z + k).Y);
        site.Y = highest + 2f;
        _game.Survivor.PlaceOutside((site + new Vector3(0f, 1f, -10f)).ToNumerics());
        foreach (int x in _drive.Wait(20)) yield return x;
        Vessel v = Vessel.SpawnBase(_game.Zone, s, (site + Vector3.Up * (cell * 0.5f + 0.05f)).ToNumerics(), 0f);
        foreach (int x in _drive.Wait(20)) yield return x;
        v.TakeChair(anchor);
        _game.ThirdPerson = false;
        Check(_game.Survivor.Sit(v), $"-- {type.Name}, {type.Size.X}x{type.Size.Y}x{type.Size.Z}, turned {turns * 90} degrees: seated");
        foreach (int x in _drive.Wait(40)) yield return x;

        // The part's box and frame, structure-local metres; points of the figure brought into it.
        Vector3 centre = s.CentreOf(anchor, block).ToGodot() * cell, half = S.Footprint.WorldSize(type, block.Rot).ToVector3().ToGodot() * cell * 0.5f;
        Basis chair = v.ChairBasis;
        Transform3D toLocal = v.DrawTransform.AffineInverse();
        Vector3 seat = v.Seat.Value.ToGodot();
        Node3D figure = _game.Figure;
        var meshes = Meshes(figure).ToList();
        Node3D body = (Node3D)figure.FindChild("body", true, false), head = (Node3D)figure.FindChild("head", true, false);
        var headMeshes = Meshes(head).ToList();
        IEnumerable<Vector3> Corners(MeshInstance3D m)
        {
            Aabb a = m.GetAabb();
            for (int i = 0; i < 8; i++) yield return toLocal * (m.GlobalTransform * a.GetEndpoint(i));
        }
        float Height(Vector3 local) => (local - seat).Dot(chair.Y); // up the chair from its seat point

        Camera3D cam = _game.Camera;
        Check(figure.Visible && cam.GetCullMaskValue(CharacterModel.BodyLayer) && !cam.GetCullMaskValue(CharacterModel.HeadLayer)
              && meshes.Except(headMeshes).All(m => (m.Layers & cam.CullMask) != 0) && headMeshes.All(m => (m.Layers & cam.CullMask) == 0),
            "first person: the figure is drawn for the player's camera, all but its head");
        // Looking down from the seat (50 degrees), the view takes in the lap and the legs.
        float pitch = _game.Survivor.Pitch;
        _game.Survivor.Pitch = -0.9f;
        foreach (int x in _drive.Wait(2)) yield return x;
        var legs = meshes.Where(m => m.GetParent()?.Name.ToString().StartsWith("leg") == true).ToList();
        int seen = legs.Count(m => cam.IsPositionInFrustum(m.GlobalTransform * m.GetAabb().GetCenter()));
        Check(seen == legs.Count, $"looking down from the seat, the view takes in its legs ({seen} of {legs.Count} leg pieces in view)");
        _game.Survivor.Pitch = pitch;
        foreach (int x in _drive.Wait(2)) yield return x;
        float closest = meshes.Except(headMeshes).Min(m => Gap(m.GlobalTransform * m.GetAabb(), cam.GlobalPosition));
        Check(closest > cam.Near, $"the eye is in nothing drawn: the nearest drawn piece is {closest:F2} m off (near plane {cam.Near:F2})");

        Vector3 hips = toLocal * body.GlobalPosition;
        float pelvis = Meshes(body).SelectMany(Corners).Min(Height);
        Vector3 across = hips - seat - chair.Y * Height(hips);
        Check(MathF.Abs(pelvis) < Near && across.Length() < Near, $"its hips are on the seat: the pelvis {pelvis * 100f:F1} cm off it, {across.Length() * 100f:F0} cm to the side of its point");
        var feet = meshes.Where(m => m.GetParent()?.Name.ToString().EndsWith("f") == true && m.GetParent()?.Name.ToString().StartsWith("leg") == true).ToList();
        float sole = feet.SelectMany(Corners).Min(Height) + v.SeatDrop;
        Check(feet.Count == 2 && MathF.Abs(sole) < Near, $"its feet are on the chair's floor {v.SeatDrop:F2} m under the seat ({sole * 100f:F1} cm)");
        Vector3 eye = v.DrawTransform * v.Eye.ToGodot();
        Check(cam.GlobalPosition.DistanceTo(eye) < Near, $"the camera is at the chair's eye point ({cam.GlobalPosition.DistanceTo(eye) * 100f:F1} cm off)");

        bool Inside(Vector3 p, float margin) => Mathf.Abs(p.X - centre.X) <= half.X + margin && Mathf.Abs(p.Y - centre.Y) <= half.Y + margin && Mathf.Abs(p.Z - centre.Z) <= half.Z + margin;
        var headCorners = headMeshes.SelectMany(Corners).ToList();
        float top = chair.Y.Dot(centre - seat) + MathF.Abs(chair.Y.Dot(half)) - headCorners.Max(Height);
        Check(headCorners.All(p => Inside(p, 0f)) && top > 0.05f, $"its head is inside the part, {top:F2} m under its top");
        var out_ = meshes.Where(m => Corners(m).Any(p => !Inside(p, 0.01f))).Select(m => m.GetParent().Name.ToString()).Distinct().ToList();
        Check(out_.Count == 0, $"nothing of the figure pokes out of the part's {type.Size.X}x{type.Size.Y}x{type.Size.Z} cells{(out_.Count > 0 ? ": " + string.Join(", ", out_) : "")}");
        Check(!_game.Hands.Visible, "the first-person hands stay down: the figure's own arms rest on its lap");

        _game.ThirdPerson = true;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(cam.GetCullMaskValue(CharacterModel.BodyLayer) && cam.GetCullMaskValue(CharacterModel.HeadLayer), "third person: the whole figure, head and all");
        _game.ThirdPerson = false;
        _game.Survivor.StandUp();
        foreach (int x in _drive.Wait(5)) yield return x;
        Check(!cam.GetCullMaskValue(CharacterModel.BodyLayer), "stood up, on foot in first person, the body is left out again");
    }

    static IEnumerable<MeshInstance3D> Meshes(Node n)
    {
        if (n is MeshInstance3D m) yield return m;
        foreach (Node c in n.GetChildren())
            foreach (MeshInstance3D d in Meshes(c)) yield return d;
    }

    /// <summary>How far a point is from a box (0 inside it).</summary>
    static float Gap(Aabb box, Vector3 p) => (p - p.Clamp(box.Position, box.End)).Length();
}
