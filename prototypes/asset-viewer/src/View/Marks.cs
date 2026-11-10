using System.Collections.Generic;
using Godot;

namespace AssetViewer;

/// <summary>Small drawn marks for the browser list: green = really like, red = problem, blue = has a comment.</summary>
public static class Marks
{
    public static readonly Color LikeColor = new("5fd07a");
    public static readonly Color ProblemColor = new("e8574f");
    public static readonly Color CommentColor = new("5aa9e6");

    static readonly Dictionary<int, ImageTexture> Cache = new();

    public const int Width = 42, Height = 12;

    /// <summary>A strip of up to three squares: like, problem, comment (a missing mark leaves its slot empty).</summary>
    public static Texture2D For(bool like, bool problem, bool comment)
    {
        int key = (like ? 1 : 0) | (problem ? 2 : 0) | (comment ? 4 : 0);
        if (key == 0) return null;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));
        void Square(int slot, Color color)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Height; x++)
                {
                    bool edge = x == 0 || y == 0 || x == Height - 1 || y == Height - 1;
                    image.SetPixel(slot * 15 + x, y, edge ? color.Darkened(0.35f) : color);
                }
        }
        if (like) Square(0, LikeColor);
        if (problem) Square(1, ProblemColor);
        if (comment) Square(2, CommentColor);
        return Cache[key] = ImageTexture.CreateFromImage(image);
    }

    static readonly Dictionary<(bool, int), ImageTexture> BoxCache = new();

    /// <summary>A tick box that reads clearly on the dark panels: a bordered square, filled with the accent colour and a tick when on.</summary>
    public static Texture2D Box(bool on, Color accent, bool dim = false)
    {
        var key = (on, accent.ToRgba32().GetHashCode() ^ (dim ? 7 : 0));
        if (BoxCache.TryGetValue(key, out var cached)) return cached;
        const int n = 22;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));
        Color border = dim ? new Color(0.4f, 0.42f, 0.46f) : new Color(0.78f, 0.82f, 0.88f);
        Color fill = on ? (dim ? accent.Darkened(0.5f) : accent) : new Color(0.12f, 0.13f, 0.15f);
        for (int y = 2; y < n - 2; y++)
            for (int x = 2; x < n - 2; x++)
            {
                bool edge = x < 4 || y < 4 || x >= n - 4 || y >= n - 4;
                image.SetPixel(x, y, edge ? (on ? accent.Lightened(0.3f) : border) : fill);
            }
        if (on)
        {
            // A tick: down-right then up-right.
            void Dot(int cx, int cy) { for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) image.SetPixel(cx + dx, cy + dy, Colors.White); }
            for (int i = 0; i < 4; i++) Dot(6 + i, 11 + i);
            for (int i = 0; i < 7; i++) Dot(9 + i, 14 - i);
        }
        return BoxCache[key] = ImageTexture.CreateFromImage(image);
    }

    public static void StyleCheckBox(CheckBox box, Color accent)
    {
        box.AddThemeIconOverride("checked", Box(true, accent));
        box.AddThemeIconOverride("unchecked", Box(false, accent));
        box.AddThemeIconOverride("checked_disabled", Box(true, accent, true));
        box.AddThemeIconOverride("unchecked_disabled", Box(false, accent, true));
    }
}
