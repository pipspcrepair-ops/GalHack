using System;
using System.Linq;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>Walls change colour in sheets, never from cell to cell, and every kit dresses its buildings.</summary>
    void Facades()
    {
        Check(_lib.Kits.Values.All(k => k.BaseCourse != null && k.Coping != null && k.DoorFrame != null), $"all {_lib.Kits.Count} kits say how their buildings meet the ground, the sky and a door");
        Check(_lib.Kits.Values.All(k => k.PanelWidth >= 4 && k.PanelHeight >= 3 && k.PanelShare < 0.5f), "a wall's other colours come in sheets of at least 4 by 3, and never outnumber its main colour");
        Check(_lib.Kits.Values.Select(k => (k.Palette.First(p => p.Symbol == '#').Colors[0], k.Coping, k.DoorFrame)).Distinct().Count() == _lib.Kits.Count, "no two kits dress alike");

        var rand = new Random();
        int walls = 0, lonely = 0;
        string worst = "";
        float worstShare = 0f;
        foreach (FamilyDef f in _lib.Families)
        {
            Layout layout = PoiFactory.BuildLayout(_lib, f, rand.Next(f.Variants.Count), Rarity.Common, (ulong)rand.NextInt64());
            int w = 0, alone = 0;
            foreach (var (p, b) in layout.Blocks.Blocks())
            {
                if (b.Role != Role.Wall || b.Type != BlockType.Cube || p.Y < 0) continue;
                var near = Dirs.All.Select(d => layout.Blocks.Get(p + d.Vec())).Where(n => n.Role == Role.Wall && n.Type == BlockType.Cube).ToList();
                if (near.Count < 2 || !Dirs.Horizontal.Any(d => layout.Blocks.Get(p + d.Vec()).IsAir)) continue;
                w++;
                // A cell like none of its neighbours is confetti; a pilaster or a band has its own kind beside it. The Vharn segment their walls by hand, hence the margin.
                if (!near.Any(n => Math.Abs(n.Color.R - b.Color.R) + Math.Abs(n.Color.G - b.Color.G) + Math.Abs(n.Color.B - b.Color.B) <= 24)) alone++;
            }
            walls += w;
            lonely += alone;
            if (w > 100 && (float)alone / w > worstShare) { worstShare = (float)alone / w; worst = $"{f.Id} ({layout.Kit.Id})"; }
        }
        Check(worstShare < 0.08f, $"of {walls} wall cells in {_lib.Families.Count} fresh families, {100f * lonely / Math.Max(1, walls):F2}% match none of their neighbours, at worst {100f * worstShare:F1}% in {worst}");
    }
}
