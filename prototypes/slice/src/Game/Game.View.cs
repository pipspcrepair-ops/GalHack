using System;
using Godot;
using GalHack.Physics;
using GalHack.Physics.Engine;
using PhysicsSandbox.Bridges;
using Slice.Surface;
using Slice.Vessels;

namespace Slice.Game;

/// <summary>
/// The one camera rig. First person it is the eye. Third person it orbits what you are in or on:
/// the sleeper on foot (in the vessel's frame when aboard one), the sleeper on the bike, or the
/// vessel you sit in, in that vessel's own frame. The right mouse button held lets go of that frame:
/// the camera's turn stays fixed in the world (the ship rolls and loops under it) while it keeps
/// its distance round the middle, and the mouse swings it round without turning you or steering;
/// let go and it eases back behind, into the frame again. The wheel zooms it between just outside
/// the body or hull and several of its lengths, remembered per mode for the session. It never ends
/// up inside the ground, a wall or the hull.
/// </summary>
public partial class Game
{
    public enum OrbitMode { Foot, Bike, Vessel }

    /// <summary>The camera's turn in the world while the right button holds it, and how much of the view is held (1 held, easing to 0 after).</summary>
    Quaternion _hold = Quaternion.Identity, _holdWas = Quaternion.Identity;
    float _held, _heldWas;

    /// <summary>The right button is holding the camera this tick.</summary>
    public bool FreeLook { get; private set; }

    /// <summary>Zoom per mode: metres on foot and on the bike, hull sizes for a vessel (so it suits any craft).</summary>
    readonly float[] _zoom = { 4.5f, 4.5f, 1.68f };
    float _zoomShown = float.NaN;
    OrbitMode _zoomShownFor;

    /// <summary>A vessel's chase view stands this far above behind, radians.</summary>
    public const float VesselRise = 0.3f;

    public OrbitMode Following => Survivor?.Seated is Vessel ? OrbitMode.Vessel : Survivor?.OnBike != null ? OrbitMode.Bike : OrbitMode.Foot;

    /// <summary>The zoom limits for a mode, in its units (metres, or hull sizes).</summary>
    public static (float Near, float Far) ZoomLimits(OrbitMode mode) => mode switch
    {
        OrbitMode.Vessel => (0.62f, 6f), // just outside the hull's bounding sphere (half its size), out to six sizes
        OrbitMode.Bike => (1.6f, 14f),
        _ => (1.1f, 12f),
    };

    public float Zoom(OrbitMode mode) => _zoom[(int)mode];

    /// <summary>Each tick, before the controls take the mouse: in third person the right button and the wheel are the camera's.</summary>
    void StepOrbit(Controls c, float dt)
    {
        _holdWas = _hold;
        _heldWas = _held;
        bool was = FreeLook;
        FreeLook = ThirdPerson && !Menu.IsOpen && c.Secondary;
        if (FreeLook)
        {
            // Taken as it stands on screen, then swung by the mouse: round the body's up on foot and on the bike,
            // round its own up in a vessel (no world's up out there).
            if (!was) _holdWas = _hold = Camera.GlobalTransform.Basis.GetRotationQuaternion();
            _held = _heldWas = 1f;
            Vector2 turn = LookTurn(c);
            Vector3 up = Following == OrbitMode.Vessel ? new Basis(_hold).Y : Survivor.WorldUp();
            _hold = (new Quaternion(up.Normalized(), -turn.X) * _hold * new Quaternion(Vector3.Right, -turn.Y)).Normalized();
            c.Look = Vector2.Zero; // turns the camera, not you, the bike or the ship
        }
        else
        {
            _held *= MathF.Exp(-6f * dt);
            if (_held < 1e-3f) _held = 0f;
        }
        if (!ThirdPerson || Menu.IsOpen) return;
        c.SecondaryPressed = false; // the button is the camera's, never a block taken off
        if (c.Wheel != 0)
        {
            OrbitMode m = Following;
            var (near, far) = ZoomLimits(m);
            _zoom[(int)m] = Mathf.Clamp(_zoom[(int)m] * MathF.Pow(1.15f, c.Wheel), near, far);
            c.Wheel = 0; // zoom, not the toolbar
        }
    }

    /// <summary>This frame's view, from exactly the transforms everything is drawn with this frame.</summary>
    Transform3D View(float dt)
    {
        if (!ThirdPerson) return CloseUpView(Survivor.Eye());
        OrbitMode mode = Following;
        float zoom = _zoom[(int)mode];
        if (float.IsNaN(_zoomShown) || _zoomShownFor != mode) _zoomShown = zoom;
        _zoomShown = Mathf.Lerp(_zoomShown, zoom, 1f - MathF.Exp(-12f * dt));
        _zoomShownFor = mode;
        float f = (float)Engine.GetPhysicsInterpolationFraction();
        float held = Mathf.Lerp(_heldWas, _held, f);
        Basis Turn(Basis frame) => held <= 0f ? frame : new Basis(frame.GetRotationQuaternion().Slerp(_holdWas.Slerp(_hold, f), held));
        Camera.Fov = Settings.Fov;
        if (Survivor.Seated is Vessel v) return Chase(Zone, v, _zoomShown, Turn);
        // On foot or on the bike: round the eye, behind it along its look (in the vessel's frame aboard one).
        Transform3D eye = Survivor.Eye();
        Basis b = Turn(eye.Basis.Orthonormalized());
        Vector3 back = b * new Vector3(0f, 0.13f, 1f);
        float d = Clear(Zone, Survivor.Aboard, null, eye.Origin, back, _zoomShown * back.Length(), 0.3f) / back.Length();
        return new Transform3D(b, eye.Origin + back * d);
    }

    /// <summary>
    /// A chase view round a vessel's middle in its own frame (never the world's up): behind and a little
    /// above, <paramref name="sizes"/> hull sizes out, its turn given by <paramref name="turn"/> (the free
    /// look's), kept out of the ground and other structures but never inside its own bounds.
    /// </summary>
    public static Transform3D Chase(SurfaceZone zone, Vessel vessel, float sizes, Func<Basis, Basis> turn = null)
    {
        Transform3D t = vessel.DrawTransform;
        var (min, max) = vessel.Structure.Bounds();
        float cell = vessel.Structure.Grid.CellSize;
        float size = MathF.Max(3f, ((max - min).ToVector3() + System.Numerics.Vector3.One).Length() * cell);
        Vector3 middle = t * ((StructureBridge.CellCenter(vessel.Structure, min) + StructureBridge.CellCenter(vessel.Structure, max)).ToGodot() * 0.5f);
        Basis b = t.Basis.Orthonormalized() * new Basis(Vector3.Right, -VesselRise);
        if (turn != null) b = turn(b);
        var (near, _) = ZoomLimits(OrbitMode.Vessel);
        CollisionObject3D own = vessel.Binding?.Collider;
        float clear = Clear(zone, null, own, middle, b.Z, sizes * size, 0.5f);
        Vector3 eye = middle + b.Z * MathF.Max(near * size, clear);
        // Blocked closer than its own bounds (the ground under a landed craft): out of it, upward.
        if (clear < near * size && zone != null && WorldRay.Cast(zone, null, eye + Vector3.Up * size, Vector3.Down, size, Layers.World | Layers.Hull, out LookHit ground, own))
            eye = ground.Point + Vector3.Up * 0.5f;
        return new Transform3D(b, eye);
    }

    /// <summary>How far out along <paramref name="dir"/> the camera can stand, up to <paramref name="want"/>: short of the first ground or structure met by <paramref name="margin"/>.</summary>
    static float Clear(SurfaceZone zone, Vessel aboard, CollisionObject3D skip, Vector3 from, Vector3 dir, float want, float margin)
    {
        if (zone == null || want <= margin) return want;
        dir = dir.Normalized();
        if (!WorldRay.Cast(zone, aboard, from, dir, want + margin, Layers.World | Layers.Hull, out LookHit hit, skip)) return want;
        return MathF.Max(0f, MathF.Min(want, hit.Distance - margin));
    }
}
