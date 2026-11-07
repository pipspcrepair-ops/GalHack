using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using Godot;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// Map labels that never overlap. Dots are added with their text and a priority (low first); on
/// <see cref="Flush"/> each label takes the first of several spots round its dot that is clear of
/// every label already placed, every dot, and the panel's edge. One that fits nowhere is left off
/// (the dot stays), so a crowd thins out instead of piling up.
/// </summary>
public sealed class MapLabels
{
    static Palette C => UiTheme.Colors;
    readonly List<(Vector2 At, float R, string Text, Color Colour, int Priority, int Seq)> _items = new();
    public const int FontSize = 13;
    const float Pad = 2f;

    /// <summary>A dot, with a label (or none) in a priority: lower is placed first and so keeps its spot.</summary>
    public void Add(Vector2 at, float radius, string text, Color colour, int priority = 5) => _items.Add((at, radius, text, colour, priority, _items.Count));

    /// <summary>Where each label would go, in the order they are placed: for the draw and for tests.</summary>
    public List<(string Text, Rect2 Box, Color Colour)> Place(Rect2 bounds, Func<string, float> width)
    {
        var placed = new List<(string, Rect2, Color)>();
        var dots = _items.Select(i => new Rect2(i.At - new Vector2(i.R, i.R), new Vector2(i.R * 2, i.R * 2))).ToList();
        var taken = new List<Rect2>();
        foreach (var item in _items.OrderBy(i => i.Priority).ThenBy(i => i.Seq))
        {
            if (string.IsNullOrEmpty(item.Text)) continue;
            float w = width(item.Text) + 6, h = FontSize + 5;
            int own = _items.IndexOf(item);
            Vector2 a = item.At;
            float r = item.R + 3;
            // Right, left, above, below, then the four corners, then the same a little further out.
            var spots = new List<Vector2>();
            for (int ring = 0; ring < 2; ring++)
            {
                float far = ring * 16f;
                spots.Add(new Vector2(a.X + r + far, a.Y - h / 2));
                spots.Add(new Vector2(a.X - r - far - w, a.Y - h / 2));
                spots.Add(new Vector2(a.X - w / 2, a.Y - r - far - h));
                spots.Add(new Vector2(a.X - w / 2, a.Y + r + far));
                spots.Add(new Vector2(a.X + r + far, a.Y - r - far - h * 0.7f));
                spots.Add(new Vector2(a.X + r + far, a.Y + r + far - h * 0.3f));
                spots.Add(new Vector2(a.X - r - far - w, a.Y - r - far - h * 0.7f));
                spots.Add(new Vector2(a.X - r - far - w, a.Y + r + far - h * 0.3f));
            }
            foreach (Vector2 s in spots)
            {
                var box = new Rect2(s, new Vector2(w, h));
                if (!bounds.Encloses(box)) continue;
                Rect2 padded = box.Grow(Pad);
                if (taken.Any(t => t.Intersects(padded))) continue;
                bool onDot = false;
                for (int i = 0; i < dots.Count && !onDot; i++)
                    if (i != own && dots[i].Intersects(box)) onDot = true;
                if (onDot) continue;
                taken.Add(box);
                placed.Add((item.Text, box, item.Colour));
                break;
            }
        }
        return placed;
    }

    /// <summary>Draws the labels (draw pass only).</summary>
    public void Flush(Gui g, Rect2 bounds)
    {
        if (!g.Drawing) return;
        foreach (var (text, box, colour) in Place(bounds, t => Paint.TextWidth(UiTheme.Label, FontSize, t)))
        {
            g.Fill(box, new Color(C.Glass, 0.6f));
            g.Text(text, box.Position + new Vector2(3, 1), colour, FontSize);
        }
    }
}
