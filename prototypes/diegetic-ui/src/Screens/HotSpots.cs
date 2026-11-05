using System;
using System.Collections.Generic;
using Godot;

namespace DiegeticUi.Screens;

/// <summary>A press on a hot spot: which button, and whether Shift was held.</summary>
public readonly record struct SpotClick(MouseButton Button, bool Shift)
{
    public bool Right => Button == MouseButton.Right;
}

/// <summary>
/// Clickable areas for a page that paints itself in _Draw instead of using child Controls.
///
/// The page registers each clickable thing while it paints it (<see cref="Add"/>), so what can
/// be clicked is always exactly what the screen is showing, even between redraws. Feed it the
/// page's GUI input: <see cref="Move"/> says when what's under the cursor changed (redraw to
/// light it), <see cref="Press"/> runs the action of the spot under a click.
///
/// Spots added earlier sit on top: register a button before the row it sits in.
/// </summary>
public sealed class HotSpots
{
    readonly record struct Spot(Rect2 Rect, string Key, Func<SpotClick, string> Act);

    readonly List<Spot> _spots = new();
    Vector2? _pointer;
    bool _claimed;

    /// <summary>The key of the spot under the cursor as last painted, or null.</summary>
    public string Hovered { get; private set; }

    public int Count => _spots.Count;

    /// <summary>Every spot's key, in paint order.</summary>
    public IEnumerable<string> Keys
    {
        get
        {
            foreach (Spot s in _spots) yield return s.Key;
        }
    }

    /// <summary>Starts a paint: forgets the last layout. Call at the top of _Draw.</summary>
    public void Begin()
    {
        _spots.Clear();
        _claimed = false;
        Hovered = null;
    }

    /// <summary>
    /// Registers a clickable area as it is painted. Returns true if the cursor is on it, so the
    /// caller paints it lit. <paramref name="act"/> runs on a press and returns a line for the
    /// player, or null; a null act makes a spot that only lights up.
    /// </summary>
    public bool Add(Rect2 rect, string key, Func<SpotClick, string> act = null)
    {
        _spots.Add(new Spot(rect, key, act));
        if (_claimed || _pointer is not { } p || !rect.HasPoint(p)) return false;
        _claimed = true;
        Hovered = key;
        return true;
    }

    /// <summary>The key of the topmost spot at a page pixel, or null.</summary>
    public string KeyAt(Vector2 pixel)
    {
        foreach (Spot s in _spots)
            if (s.Rect.HasPoint(pixel)) return s.Key;
        return null;
    }

    /// <summary>Where a spot was last painted, for tests and screenshots that aim at it.</summary>
    public Rect2? RectOf(string key)
    {
        foreach (Spot s in _spots)
            if (s.Key == key) return s.Rect;
        return null;
    }

    /// <summary>The cursor moved to a page pixel, or off the page (null). True if the page should redraw.</summary>
    public bool Move(Vector2? pixel)
    {
        _pointer = pixel;
        string now = pixel is { } p ? KeyAt(p) : null;
        if (now == Hovered) return false;
        Hovered = now;
        return true;
    }

    /// <summary>A press at a page pixel: runs the spot's action. <paramref name="hit"/> says whether it landed on a spot.</summary>
    public string Press(Vector2 pixel, SpotClick click, out bool hit)
    {
        _pointer = pixel;
        foreach (Spot s in _spots)
        {
            if (!s.Rect.HasPoint(pixel)) continue;
            hit = true;
            return s.Act?.Invoke(click);
        }
        hit = false;
        return null;
    }
}
