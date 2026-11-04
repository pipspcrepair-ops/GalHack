using System.Threading.Tasks;
using DiegeticUi.Screens;
using DiegeticUi.World;
using Godot;

namespace DiegeticUi.Tests;

/// <summary>
/// Drives the bridge the way a player would, for tests and screenshots: aim at things, press
/// keys, move the mouse to a point on a screen and click. Everything goes through the real
/// input path (Input.ParseInputEvent), so the interactor, the rays and the pushed SubViewport
/// events are all exercised.
/// </summary>
public sealed class Drive
{
    readonly Node _host;
    public Main Main { get; }

    public Drive(Node host, Main main)
    {
        _host = host;
        Main = main;
    }

    public async Task Frames(int count = 1)
    {
        for (int i = 0; i < count; i++) await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    public async Task PhysicsFrames(int count = 1)
    {
        for (int i = 0; i < count; i++) await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    public async Task Seconds(double seconds) => await _host.ToSignal(_host.GetTree().CreateTimer(seconds), Timer.SignalName.Timeout);

    /// <summary>Stands the player at a spot on the deck, looking at a world point.</summary>
    public void StandAndLook(Vector3 feet, Vector3 lookAt)
    {
        Main.Player.Place(feet, 0);
        Aim(lookAt);
    }

    public void Aim(Vector3 lookAt)
    {
        Vector3 eye = Main.Player.GlobalPosition + Vector3.Up * 1.62f;
        Vector3 d = lookAt - eye;
        Main.Player.SetLook(Mathf.Atan2(-d.X, -d.Z), Mathf.Asin(d.Y / d.Length()));
    }

    public static void Key(Key key, bool pressed = true) =>
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed });

    public static void Tap(Key key)
    {
        Key(key, true);
        Key(key, false);
    }

    /// <summary>Walks the player up to a screen and uses it with E, waiting until it is in use.</summary>
    public async Task<bool> Use(IFocusTarget target, Vector3 standAt, Vector3 lookAt)
    {
        StandAndLook(standAt, lookAt);
        await PhysicsFrames(3);
        await Frames(2);
        if (Main.Interactor.LookTarget != target) return false;
        Tap(Godot.Key.E);
        for (int i = 0; i < 120 && Main.Interactor.Focus.State != Logic.FocusState.Using; i++) await Frames();
        return Main.Interactor.Focus.State == Logic.FocusState.Using;
    }

    /// <summary>The window position of a pixel on a world screen, through the current camera.</summary>
    public Vector2 WindowPoint(WorldScreen screen, Vector2 pixel) => Main.Camera.UnprojectPosition(screen.PixelToWorld(pixel));

    /// <summary>The page-pixel centre of a control on a screen.</summary>
    public static Vector2 Centre(Control control) => control.GetGlobalRect().GetCenter();

    /// <summary>
    /// Moves the virtual cursor (by relative mouse motion, as a real mouse would) so it sits
    /// over a window position.
    /// </summary>
    public void MoveMouseTo(Vector2 window, bool leftHeld = false)
    {
        Vector2 relative = window - Main.Interactor.Cursor;
        Input.ParseInputEvent(new InputEventMouseMotion { Relative = relative, ButtonMask = leftHeld ? MouseButtonMask.Left : 0 });
    }

    public static void Mouse(bool pressed) =>
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, ButtonMask = pressed ? MouseButtonMask.Left : 0 });

    public async Task Click(Vector2 window)
    {
        MoveMouseTo(window);
        await Frames();
        Mouse(true);
        await Frames();
        Mouse(false);
        await Frames();
    }
}
