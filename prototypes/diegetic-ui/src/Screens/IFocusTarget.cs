using Godot;

namespace DiegeticUi.Screens;

/// <summary>
/// Anything in the world the player can step up to and use: a screen, a holo-table, the
/// wrist computer. The <see cref="Interactor"/> eases the view in, routes the mouse to it as
/// rays, and eases back out on Esc.
/// </summary>
public interface IFocusTarget
{
    /// <summary>Short name the visor shows when you look at it: "SHIP SYSTEMS".</summary>
    string Prompt { get; }

    /// <summary>False for things that come to you (the wrist): the view stays put.</summary>
    bool MovesCamera { get; }

    /// <summary>Where the camera eases to while using it, in world space.</summary>
    Transform3D FocusView(Camera3D camera);

    /// <summary>The window-space rectangle it covers, so the cursor can be kept on it.</summary>
    Rect2 ScreenRect(Camera3D camera);

    void SetInUse(bool inUse);

    /// <summary>A mouse event while in use, as a world-space ray from the camera through the cursor.</summary>
    void Point(Vector3 origin, Vector3 direction, InputEvent e);
}

/// <summary>A screen's content. Pulls from its view-models and says whether anything visible changed.</summary>
public interface IScreenPage
{
    bool Refresh();
}
