using System.Collections.Generic;
using Godot;

namespace Slice.Game;

/// <summary>
/// One tick's worth of player input: what is held, what was pressed this tick, the mouse. The
/// keyboard and mouse fill it in play; tests and screenshots fill it by hand, so a scripted run
/// goes through exactly the same game code as a player.
/// </summary>
public sealed class Controls
{
    /// <summary>Actions held and pressed this tick, each by its default key (W is forward, whatever key it is bound to: see <see cref="KeyBindings"/>).</summary>
    public readonly HashSet<Key> Held = new();
    public readonly HashSet<Key> Pressed = new();

    /// <summary>
    /// The keys themselves pressed this tick, as they are on the keyboard: the menus' own keys
    /// (Esc, /, Ctrl+A, + and -) and the escape menu's key prompt read these. A script's tap is both.
    /// </summary>
    public readonly HashSet<Key> Keys = new();

    /// <summary>Shift, Ctrl and Alt themselves held now (a menu's Shift-click and Ctrl-click).</summary>
    public readonly HashSet<Key> Modifiers = new();

    /// <summary>Mouse movement this tick, pixels.</summary>
    public Vector2 Look;

    public bool Primary, Secondary, PrimaryPressed, SecondaryPressed, PrimaryReleased, SecondaryReleased;
    public int Wheel;

    /// <summary>Scripts: the middle button is held (maps pan with it).</summary>
    public bool Middle;

    /// <summary>The mouse pointer over the Tab menu, in the menu's own 1600 x 900 canvas.</summary>
    public Vector2 Mouse;

    /// <summary>Text typed this tick (for a focused text box on the menu), and backspaces.</summary>
    public string Typed = "";
    public int Backspaces;

    /// <summary>Scripts: type text into whatever has the keyboard.</summary>
    public void Type(string text) => Typed += text;

    public bool IsHeld(Key k) => Held.Contains(k);
    public bool WasPressed(Key k) => Pressed.Contains(k);
    public float Axis(Key positive, Key negative) => (IsHeld(positive) ? 1f : 0f) - (IsHeld(negative) ? 1f : 0f);

    /// <summary>The key itself was pressed this tick (menus' own keys).</summary>
    public bool KeyWasPressed(Key k) => Keys.Contains(k);

    /// <summary>Shift, Ctrl or Alt itself is held.</summary>
    public bool ModifierHeld(Key k) => Modifiers.Contains(k);

    static bool IsModifier(Key k) => k is Key.Shift or Key.Ctrl or Key.Alt;

    /// <summary>Scripts: press a key for one tick.</summary>
    public void Tap(Key k)
    {
        Pressed.Add(k);
        Keys.Add(k);
    }

    /// <summary>Scripts: hold or release a key.</summary>
    public void Hold(Key k, bool down = true)
    {
        if (down) Held.Add(k);
        else Held.Remove(k);
        if (!IsModifier(k)) return;
        if (down) Modifiers.Add(k);
        else Modifiers.Remove(k);
    }

    public void ReleaseAll()
    {
        Held.Clear();
        Modifiers.Clear();
        Primary = Secondary = false;
    }

    /// <summary>Clears the one-tick parts after the tick has used them.</summary>
    public void EndTick()
    {
        Pressed.Clear();
        Keys.Clear();
        Look = Vector2.Zero;
        Wheel = 0;
        PrimaryPressed = SecondaryPressed = PrimaryReleased = SecondaryReleased = false;
        Typed = "";
        Backspaces = 0;
    }
}
