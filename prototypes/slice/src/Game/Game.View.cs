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
/// vessel you sit in, in that vessel's own frame. The right mouse button held turns it round
/// without turning you or steering; let go and it eases back behind. The wheel zooms it between
/// just outside the body or hull and several of its lengths, remembered per mode for the session.
/// It never ends up inside the ground, a wall or the hull.
/// </summary>
public partial class Game
{
    public enum OrbitMode { Foot, Bike, Vessel }

    /// <summary>The free look's turn round the target (yaw, pitch), radians: 0 is behind.</summary>
    public Vector2 Swing;
    Vector2 _swingWas;

    /// <summary>The right button is holding the camera this tick.</summary>
    public bool FreeLook { get; private set; }

    /// <summary>Zoom per mode: metres on foot and on the bike, hull sizes for a vessel (so it suits any craft).</summary>
    readonly float[] _zoom = { 4.5f, 4.5f, 1.68f };
    float _zoomShown = float.NaN;
    OrbitMode _zoomShownFor;

    /// <summary>A vessel's chase view stands this far above behind, radians.</summary>
    const float VesselRise = 0.3f;

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
        _swingWas = Swing;
        FreeLook = ThirdPerson && !Menu.IsOpen && c.Secondary;
        if (ThirdPerson && !Menu.IsOpen)
        {
            if (FreeLook)
            {
                Vector2 turn = LookTurn(c);
                Swing = new Vector2(Mathf.Wrap(Swing.X - turn.X, -MathF.PI, MathF.PI), Mathf.Clamp(Swing.Y - turn.Y, -1.4f, 1.4f));
                c.Look = Vector2.Zero; // turns the camera, not you, the bike or the ship
            }
            c.SecondaryPressed = false; // the button is the camera's, never a block taken off
            if (c.Wheel != 0)
            {
                OrbitMode m = Following;
                var (near, far) = ZoomLimits(m);
                _zoom[(int)m] = Mathf.Clamp(_zoom[(int)m] * MathF.Pow(1.15f, c.Wheel), near, far);
                c.Wheel = 0; // zoom, not the toolbar
            }
        }
        if (!FreeLook)
        {
            float back = MathF.Exp(-6f * dt);
            Swing = new Vector2(Mathf.Wrap(Swing.X, -MathF.PI, MathF.PI) * back, Swing.Y * back);
            if (Swing.LengthSquared() < 1e-8f) Swing = Vector2.Zero;
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
        var orbit = new Vector2(Mathf.LerpAngle(_swingWas.X, Swing.X, f), Mathf.Lerp(_swingWas.Y, Swing.Y, f));
        Camera.Fov = Settings.Fov;
        if (Survivor.Seated is Vessel v) return Chase(Zone, v, orbit, _zoomShown);
        // On foot or on the bike: round the eye, about the body's own up (the vessel's, aboard one).
        Transform3D eye = Survivor.Eye();
        Basis heading = eye.Basis * new Basis(Vector3.Right, -Survivor.Pitch);
        Basis b = heading * new Basis(Vector3.Up, orbit.X) * new Basis(Vector3.Right, Mathf.Clamp(Survivor.Pitch + orbit.Y, -1.45f, 1.45f));
        Vector3 back = b * new Vector3(0f, 0.13f, 1f);
        float d = Clear(Zone, Survivor.Aboard, null, eye.Origin, back, _zoomShown * back.Length(), 0.3f) / back.Length();
        return new Transform3D(b, eye.Origin + back * d);
    }

    /// <summary>
    /// A chase view round a vessel in its own frame (never the world's up): behind and a little above
    /// its middle, turned by <paramref name="orbit"/>, <paramref name="sizes"/> hull sizes out, kept out
    /// of the ground and other structures but never inside its own bounds.
    /// </summary>
    public static Transform3D Chase(SurfaceZone zone, Vessel vessel, Vector2 orbit, float sizes)
    {
        Transform3D t = vessel.DrawTransform;
        var (min, max) = vessel.Structure.Bounds();
        float cell = vessel.Structure.Grid.CellSize;
        float size = MathF.Max(3f, ((max - min).ToVector3() + System.Numerics.Vector3.One).Length() * cell);
        Vector3 middle = t * ((StructureBridge.CellCenter(vessel.Structure, min) + StructureBridge.CellCenter(vessel.Structure, max)).ToGodot() * 0.5f);
        Basis b = t.Basis.Orthonormalized() * new Basis(Vector3.Up, orbit.X) * new Basis(Vector3.Right, Mathf.Clamp(orbit.Y - VesselRise, -1.5f, 1.5f));
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
