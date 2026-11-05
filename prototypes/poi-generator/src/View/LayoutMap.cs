using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// One storey of a layout drawn from above as an image: corridors pale, rooms tinted by purpose,
/// stairs yellow, lifts cyan, the vault gold, hidden rooms violet, walls dark, doors orange. For the
/// contact sheets that show layouts side by side.
/// </summary>
public static class LayoutMap
{
    public static Image Storey(Layout layout, int walkY, int px = 4)
    {
        var g = layout.Blocks;
        int w = g.Size.X, d = g.Size.Z;
        var module = new Dictionary<(int, int), PlacedModule>();
        foreach (var m in layout.Modules)
            foreach (Int3 c in m.Cells)
                if (c.Y == walkY) module[(c.X, c.Z)] = m;
        var secrets = new HashSet<int>(layout.Extras.Secrets.Select(s => s.Module));
        var img = Image.CreateEmpty(w * px, d * px, false, Image.Format.Rgba8);
        img.Fill(new Color(0.05f, 0.055f, 0.065f));
        for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                var c = new Int3(g.Min.X + x, walkY, g.Min.Z + z);
                var b = g.Get(c);
                Color col;
                if (layout.Extras.CorridorCells.Contains(c)) col = new Color(0.8f, 0.78f, 0.72f);
                else if (b.Type == BlockType.Door) col = new Color(1f, 0.55f, 0.15f);
                else if (b.Type == BlockType.Breakable) col = new Color(0.75f, 0.45f, 1f);
                else if (module.TryGetValue((c.X, c.Z), out var m))
                {
                    col = m.Kind switch
                    {
                        ModuleKind.Stair => new Color(0.95f, 0.8f, 0.2f),
                        ModuleKind.Lift => new Color(0.3f, 0.85f, 0.95f),
                        ModuleKind.Vault => new Color(0.95f, 0.68f, 0.2f),
                        ModuleKind.Cave => new Color(0.5f, 0.42f, 0.3f),
                        ModuleKind.Outdoor => new Color(0.32f, 0.36f, 0.3f),
                        _ => secrets.Contains(m.Index) ? new Color(0.6f, 0.25f, 0.95f) : Tint(m.Purpose),
                    };
                    if (!b.IsAir && BlockInfo.Collides(b.Type)) col = col.Darkened(0.35f);
                }
                else if (!b.IsAir && BlockInfo.Collides(b.Type)) col = new Color(0.22f, 0.24f, 0.28f);
                else if (!g.Get(c + Int3.Down).IsAir) col = new Color(0.36f, 0.36f, 0.34f);
                else continue;
                img.FillRect(new Rect2I(x * px, z * px, px, px), col);
            }
        return img;
    }

    /// <summary>A pastel per purpose, so the same kind of room reads the same on every sheet.</summary>
    static Color Tint(string purpose)
    {
        uint h = (uint)Hash.Of(purpose);
        return Color.FromHsv((h % 360) / 360f, 0.38f, 0.62f);
    }

    /// <summary>The storey's shape and circulation, from the plan signature.</summary>
    public static string PlanName(Layout layout, int storey)
    {
        var p = layout.Extras.Plans.FirstOrDefault(p => p.Storey == storey);
        if (p.Signature == null) return "";
        var parts = p.Signature.Split('/');
        return parts.Length >= 2 ? $"{parts[0]} · {parts[1]}" : p.Signature;
    }
}
