using System;
using Godot;

namespace Wildlife;

/// <summary>
/// What people put on their animals, so a settlement's beasts read as kept, not wild: a working
/// beast carries a blanket, a girth strap and two packs; a pet wears a collar. Stock go bare.
/// Added to the body piece after it is built, like <see cref="Mutation"/>; wild species get nothing.
/// </summary>
public static class Tack
{
    public static RigLayout Decorate(RigLayout layout, Species s)
    {
        if (s.Domestic is DomesticKind.None or DomesticKind.Stock) return layout;
        Piece body = layout.Pieces.Find(p => p.Name == "body");
        if (body == null) return layout;
        Proportions b = s.Body;
        float W = b.Width, H = b.Height, L = b.Size;
        var leather = new Color(0.32f, 0.2f, 0.12f);
        var cloth = new Color(0.46f, 0.18f, 0.14f).Lerp(new Color(0.2f, 0.26f, 0.38f), (s.Seed % 3) * 0.5f);
        if (s.Domestic == DomesticKind.Working)
        {
            // A woven blanket over the back, a girth strap round the barrel, and a pack each side.
            body.Add(Shape.Sphere, new Vector3(W * 1.12f, H * 0.32f, L * 0.42f), new Vector3(0f, H * 0.42f, L * 0.02f), cloth, 0.15f, 0f, null, 14);
            body.Add(Shape.Cylinder, new Vector3(W * 1.06f, L * 0.05f, H * 1.06f), new Vector3(0f, 0f, -L * 0.02f), leather, 0f, 0f, new Basis(Vector3.Right, MathF.PI * 0.5f), 16);
            for (int side = -1; side <= 1; side += 2)
            {
                body.Add(Shape.Box, new Vector3(W * 0.28f, H * 0.5f, L * 0.26f), new Vector3(side * W * 0.62f, H * 0.02f, L * 0.03f), leather.Lightened(0.15f), 0f, 0f, null, 0);
                body.Add(Shape.Box, new Vector3(W * 0.3f, H * 0.08f, L * 0.27f), new Vector3(side * W * 0.62f, H * 0.28f, L * 0.03f), leather, 0f, 0f, null, 0);
            }
        }
        else
        {
            // A collar where the neck leaves the body.
            body.Add(Shape.Cylinder, new Vector3(W * 0.72f, L * 0.06f, H * 0.72f), new Vector3(0f, H * 0.12f, -L * 0.43f), cloth, 0f, 0f, new Basis(Vector3.Right, MathF.PI * 0.5f), 14);
            body.Add(Shape.Sphere, Vector3.One * W * 0.12f, new Vector3(0f, -H * 0.18f, -L * 0.47f), new Color(0.75f, 0.62f, 0.3f), 0f, 0f, null, 8, Mat.Horn);
        }
        return layout;
    }
}
