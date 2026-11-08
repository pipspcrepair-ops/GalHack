using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Builds a person out of the same pieces and primitives as any creature, so mutation, the mesh
/// builder and the rig machinery need no second path. The pieces are named as the rigs name theirs:
/// "body" (hips to neck, origin at the hips), "head" (origin at the base of the neck), legs leg0u,
/// leg0l, leg0f (left) and leg1u... (right), arms arm0u, arm0l, arm1u, arm1l, and a "prop" for
/// whatever they carry. Bones run along +Y; bodies face -Z.
/// </summary>
public static class HumanBody
{
    /// <summary>Only one thing fits on each of these. "" for what can go with anything.</summary>
    public static string Slot(string id) => id switch
    {
        "helmet" or "cap" or "wide_hat" or "straw_hat" or "hood" or "fur_hood" or "head_wrap" or "suit_helmet" or "hazard_hood" or "net_hat" => "head",
        "respirator" or "filter_mask" or "balaclava" or "face_wrap" => "mouth",
        "shades" or "goggles" or "eyepatch" => "eyes",
        "backpack" or "air_tank" or "trader_pack" => "back",
        "apron" or "vest" or "hivis" => "front",
        "satchel" or "tool_belt" or "pouches" => "side",
        "coat" or "poncho" or "shawl" or "parka" or "robe" or "rain_cape" or "oilskin" => "over",
        "mukluks" or "sandals" or "rubber_boots" => "feet",
        "coveralls" => "body",
        "thermals" or "undershirt" => "base",
        "sweater" or "fleece" => "insulation",
        "anorak" or "smock" => "wind",
        _ => "",
    };

    static float Scale(Figure f) => f.Height / 1.72f;

    /// <summary>A layer's own colour: a pale base, a knit insulation layer, a wind layer in the outer cloth; coveralls in the work colour.</summary>
    static Color LayerColour(Figure f, string id) => id switch
    {
        "thermals" or "undershirt" => f.Cloth.Lerp(new Color(0.78f, 0.76f, 0.7f), 0.65f),
        "sweater" => f.Trim.Lerp(new Color(0.48f, 0.3f, 0.24f), 0.45f),
        "fleece" => f.Cloth.Lerp(new Color(0.3f, 0.36f, 0.42f), 0.5f),
        "anorak" => f.Cloth.Lerp(new Color(0.26f, 0.34f, 0.3f), 0.35f),
        "smock" => f.Cloth.Lightened(0.08f),
        "coveralls" => f.Trim.Lerp(new Color(0.24f, 0.32f, 0.42f), 0.5f),
        _ => f.Cloth,
    };

    /// <summary>What shows on the torso and arms: the outermost layer they wear under any coat.</summary>
    static Color Outer(Figure f)
    {
        if (f.Has("coveralls")) return LayerColour(f, "coveralls");
        for (int i = f.Layered.Count - 1; i >= 0; i--)
            if (Slot(f.Layered[i]) is "wind" or "insulation") return LayerColour(f, f.Layered[i]);
        return f.Cloth;
    }

    /// <summary>Their sleeves come down to the wrist: a layer over the shirt does that.</summary>
    static bool Sleeved(Figure f) => f.Has("coveralls") || f.Layered.Exists(l => Slot(l) is "insulation" or "wind");

    /// <summary>Patches on worn clothes: squares of other cloth, more the more worn. No marks or writing on any of it.</summary>
    static void Patches(Piece p, Figure f, float y0, float y1, float r, float depth, int salt)
    {
        int n = (int)(f.Worn * 5f - 1f);
        if (n <= 0 || f.Dress == "suit") return;
        var rng = new Rng((ulong)(f.Height * 1000f) * 31UL + (ulong)salt);
        float u = Scale(f);
        for (int i = 0; i < n; i++)
        {
            float a = rng.Range(-1.2f, 1.2f) + (rng.Chance(0.4f) ? MathF.PI : 0f);
            Vector3 normal = new(MathF.Sin(a), 0f, -MathF.Cos(a));
            Vector3 at = new(MathF.Sin(a) * r, rng.Range(y0, y1), -MathF.Cos(a) * depth);
            Color c = rng.Pick(new[] { f.Leather.Lightened(0.1f), f.Trim.Lightened(0.15f), f.Cloth.Darkened(0.25f), new Color(0.42f, 0.38f, 0.3f) });
            p.Add(Shape.Box, new Vector3(rng.Range(0.07f, 0.12f), rng.Range(0.06f, 0.11f), 0.012f) * u, at + normal * 0.006f, c, 0f, 0f, Geo.AlongY(Vector3.Up, new Vector3(normal.Z, 0f, -normal.X)) * new Basis(Vector3.Forward, rng.Range(-0.3f, 0.3f)), 0);
        }
    }

    public static RigLayout Build(Species s)
    {
        Figure f = s.Figure;
        var k = new Paint(s);
        var layout = new RigLayout();
        var body = new Piece("body");
        var head = new Piece("head");
        Torso(body, f);
        Head(head, f, k);
        layout.Add(body);
        layout.Add(head);
        float u = Scale(f);
        float pad = f.Padding;
        Color sleeve = f.Dress switch { "suit" or "hazard" => f.Cloth, "furs" or "coat" => Outer(f), "wraps" => f.Cloth.Lightened(0.08f), "rags" => f.Skin, _ => Outer(f) };
        Color forearm = f.Dress is "shirt" or "rags" or "wraps" or "light" && !Sleeved(f) ? f.Skin : sleeve;
        Color hand = f.Dress is "suit" or "hazard" or "furs" ? f.Leather : FolkBody.Pointed(f, f.Skin);
        float skinMat = FolkBody.SkinMat(f);
        Color trouser = f.Has("coveralls") ? LayerColour(f, "coveralls") : f.Dress switch { "suit" or "hazard" => f.Cloth, "furs" => f.Cloth.Darkened(0.15f), "rags" => f.Cloth, "wraps" => f.Cloth.Darkened(0.12f), _ => f.Trim.Darkened(0.1f).Lerp(new Color(0.2f, 0.2f, 0.22f), 0.5f) };
        // Loose light clothes in the humid heat: shorts, the legs bare below.
        if (f.Dress == "light") trouser = f.Cloth.Lightened(0.12f);
        Color shin = f.Dress is "rags" or "light" ? f.Skin : trouser;
        float b = 0.85f + 0.5f * f.Build;
        for (int side = 0; side < 2; side++)
        {
            // A Husk's long arm: longer bones (the rig reaches with them), the sleeve torn off it.
            bool longArm = f.Wrongs.HasFlag(Wrong.Arm) && side == f.LongArm;
            float upper = f.UpperArmOf(side), fore = f.ForearmOf(side);
            var au = new Piece($"arm{side}u");
            au.Taper(Vector3.Zero, Vector3.Up * upper, 0.05f * u * b * pad, 0.04f * u * b * pad, longArm ? f.Skin : sleeve, 0f, 0f, Mat.Skin, 7);
            var al = new Piece($"arm{side}l");
            al.Taper(Vector3.Zero, Vector3.Up * fore * 0.86f, 0.04f * u * b * pad, 0.03f * u * pad, longArm ? f.Skin : forearm, 0f, 0f, longArm || forearm == f.Skin ? skinMat : Mat.Skin, 7);
            al.Add(Shape.Sphere, new Vector3(0.062f, 0.11f, 0.045f) * u * f.HandK * (longArm ? 1.25f : f.Dress is "furs" or "suit" ? 1.25f : 1f), Vector3.Up * fore * 0.95f, longArm ? f.Skin : hand, 0f, 0f, null, 7, hand == f.Leather ? Mat.Skin : skinMat);
            FolkBody.Hand(al, f, fore, hand, forearm == f.Skin);
            var lu = new Piece($"leg{side}u");
            lu.Taper(Vector3.Zero, Vector3.Up * f.UpperLeg, 0.082f * u * b * pad, 0.056f * u * b * pad, trouser, 0f, 0f, Mat.Skin, 8);
            var ll = new Piece($"leg{side}l");
            ll.Taper(Vector3.Zero, Vector3.Up * f.LowerLeg, 0.056f * u * b * pad, 0.036f * u * pad, shin, 0f, 0f, Mat.Skin, 8);
            if (f.Has("mukluks"))
            {
                // Mukluks: soft hide to below the knee, a fur cuff at the top.
                Color hide = f.Leather.Lerp(new Color(0.55f, 0.46f, 0.36f), 0.45f);
                ll.Taper(Vector3.Up * f.LowerLeg * 0.42f, Vector3.Up * f.LowerLeg, 0.06f * u * pad, 0.058f * u * pad, hide, 0f, 0f, Mat.Skin, 8);
                ll.Add(Shape.Cylinder, new Vector3(0.15f, 0.05f, 0.15f) * u * pad, Vector3.Up * f.LowerLeg * 0.42f, new Color(0.78f, 0.72f, 0.62f), 0f, 0f, null, 8);
            }
            else if (f.Has("rubber_boots"))
            {
                // Rubber boots to below the knee, dark and glossy.
                Color rubber = new Color(0.16f, 0.2f, 0.15f).Lerp(f.Trim, 0.15f);
                ll.Taper(Vector3.Up * f.LowerLeg * 0.4f, Vector3.Up * f.LowerLeg, 0.062f * u * pad, 0.058f * u * pad, rubber, 0f, 0f, Mat.Carapace, 8);
            }
            else if (f.Dress is not "rags" and not "wraps" and not "light" && !f.Has("sandals")) ll.Taper(Vector3.Up * f.LowerLeg * 0.6f, Vector3.Up * f.LowerLeg, 0.05f * u * pad, 0.046f * u * pad, f.Leather, 0f, 0f, Mat.Skin, 8); // boot tops
            if (f.Worn > 0.5f && side == 1) Patches(lu, f, f.UpperLeg * 0.3f, f.UpperLeg * 0.7f, 0.07f * u * b * pad, 0.07f * u * b * pad, 7);
            // The inner layer shows at the cuff.
            if (f.Layered.Count > 0 && Sleeved(f)) al.Add(Shape.Cylinder, new Vector3(0.065f, 0.03f, 0.065f) * u * pad, Vector3.Up * fore * 0.84f, LayerColour(f, f.Layered[0]), 0f, 0f, null, 7);
            var ft = new Piece($"leg{side}f");
            Color boot = f.Has("sandals") ? FolkBody.Pointed(f, f.Skin) : f.Has("rubber_boots") ? new Color(0.16f, 0.2f, 0.15f) : f.Dress == "rags" ? f.Skin.Darkened(0.2f) : f.Dress == "wraps" ? f.Leather.Lightened(0.15f) : f.Leather.Darkened(0.2f);
            ft.Add(Shape.Sphere, new Vector3(0.1f * f.FootK, f.Ankle * 1.5f / u, 0.27f * f.FootK) * u, new Vector3(0f, -f.Ankle * 0.45f, -0.055f * u * f.FootK), boot, 0f, 0f, null, 8);
            ft.Add(Shape.Box, new Vector3(0.095f * f.FootK, 0.02f, 0.26f * f.FootK) * u, new Vector3(0f, -f.Ankle + 0.01f, -0.055f * u * f.FootK), boot.Darkened(0.4f));
            if (f.Has("sandals")) ft.Add(Shape.Box, new Vector3(0.1f * f.FootK, 0.012f, 0.06f) * u, new Vector3(0f, -f.Ankle * 0.4f, -0.09f * u * f.FootK), f.Leather, 0f, 0f, null, 0); // the strap
            ft.Shadow = false;
            if (side == 0 && f.Has("medic_band")) Band(au, f);
            // The one saturated mark: a band on the arm, where nothing else is.
            else if (side == 0 && !f.Has("spike_pads") && f.Dress != "rags") au.Add(Shape.Cylinder, new Vector3(0.115f, 0.07f, 0.115f) * u * f.Padding, Vector3.Up * f.UpperArm * 0.52f, f.Accent, 0f, 0f, null, 8);
            if (side == 1 && f.Has("scrap_armour")) lu.Add(Shape.Box, new Vector3(0.13f, f.UpperLeg * 0.42f, 0.04f) * new Vector3(u, 1f, u), new Vector3(0.02f, f.UpperLeg * 0.55f, -0.07f * u), f.Metal.Darkened(0.2f), 0f, 0f, new Basis(Vector3.Forward, 0.12f), 0, Mat.Carapace);
            if (f.Has("spike_pads")) Pad(au, f, side);
            layout.Add(au);
            layout.Add(al);
            layout.Add(lu);
            layout.Add(ll);
            layout.Add(ft);
        }
        if (f.Prop.Length > 0)
        {
            var prop = new Piece("prop");
            Prop(prop, f);
            layout.Add(prop);
        }
        FolkBody.Tail(layout, f);
        return layout;
    }

    /// <summary>The torso, in two lofts that meet at the belt: what's under it and what's over it.</summary>
    static void Torso(Piece p, Figure f)
    {
        float T = f.Torso, pad = f.Padding, d = f.ChestD * pad;
        var upright = new Basis(Vector3.Right, -MathF.PI * 0.5f);
        float fem = f.Sex == Sex.Female && f.Stage != LifeStage.Child ? 1f : 0f;
        Color top = f.Dress == "rags" ? f.Skin.Darkened(0.05f) : f.Dress is "suit" or "hazard" ? f.Cloth : Outer(f);
        Color lower = f.Has("coveralls") ? LayerColour(f, "coveralls") : f.Dress is "suit" or "hazard" ? f.Cloth : f.Dress == "furs" ? f.Cloth.Darkened(0.15f) : f.Trim.Darkened(0.1f).Lerp(new Color(0.2f, 0.2f, 0.22f), 0.5f);
        var hips = LoftProfile.Smooth(-0.1f * T, 0.45f * T, new[]
        {
            (0f, f.HipW * 0.62f * pad, d * 0.72f, 0f),
            (0.3f, f.HipW * pad, d * 1.02f, 0f),
            (0.72f, (f.HipW + f.WaistW) * 0.5f * pad, d * 0.9f, 0f),
            (1f, f.WaistW * 1.02f * pad, d * 0.82f, 0f),
        }, 9, 0.2f);
        var chest = LoftProfile.Smooth(0.34f * T, 1.02f * T, new[]
        {
            (0f, f.WaistW * pad, d * 0.82f, 0f),
            (0.2f, f.WaistW * 1.02f * pad, d * 0.82f, 0f),
            (0.6f, f.ShoulderW * (0.86f - 0.05f * fem) * pad, d * (1.02f + 0.1f * fem), d * 0.08f * fem),
            (0.84f, f.ShoulderW * pad, d * 0.84f, 0f),
            (1f, f.ShoulderW * 0.42f, d * 0.46f, 0f),
        }, 11, 0.2f);
        p.Loft(chest, top, top.Darkened(0.12f), 0f, 0f, Mat.Skin, null, upright, 11);
        p.Loft(hips, lower, lower.Darkened(0.1f), 0f, 0f, Mat.Skin, null, upright, 11);
        float belt = 0.4f * T;
        if (f.Dress != "rags")
            p.Add(Shape.Cylinder, new Vector3(f.WaistW * 2.1f * pad, 0.05f * Scale(f), f.WaistW * 2.1f * pad), new Vector3(0f, belt, 0f), f.Dress == "wraps" ? f.Trim : f.Leather, 0f, 0f, new Basis(Vector3.Up, 0f) * Basis.FromScale(new Vector3(1f, 1f, d / MathF.Max(0.01f, f.WaistW * pad))), 10);
        if (f.Dress == "furs") FurTrim(p, f, T * 0.92f, f.ShoulderW * 0.8f, f.Cloth.Lightened(0.25f));
        if (f.Dress == "suit") SuitLines(p, f);
        if (f.Dress == "rags") Tatters(p, f);
        // Layers show at the collar, innermost highest: a pale base, a knit roll over it.
        for (int i = 0; i < f.Layered.Count && f.Dress is not ("suit" or "hazard"); i++)
        {
            int k = f.Layered.Count - i;
            p.Add(Shape.Sphere, new Vector3(0.17f + 0.035f * k, 0.05f, 0.15f + 0.03f * k) * Scale(f) * pad, new Vector3(0f, T * (1.0f - 0.025f * (k - 1)), 0f), LayerColour(f, f.Layered[i]), 0f, 0f, null, 9);
        }
        if (f.Has("coveralls")) p.Add(Shape.Box, new Vector3(0.012f, T * 0.62f, 0.01f) * new Vector3(Scale(f), 1f, Scale(f)), new Vector3(0f, T * 0.62f, -d * 1.01f), LayerColour(f, "coveralls").Darkened(0.35f));
        Patches(p, f, T * 0.15f, T * 0.85f, f.WaistW * pad * 1.02f, d * 1.0f, 3);
        foreach (string w in f.Wear) Garment(p, f, w);
        if (f.Torn) Rents(p, f);
        if (f.Wrongs.HasFlag(Wrong.Hunch)) Hump(p, f);
        FolkBody.Back(p, f);
    }

    static void FurTrim(Piece p, Figure f, float y, float r, Color c)
    {
        float u = Scale(f);
        for (int i = 0; i < 9; i++)
        {
            float a = i / 9f * MathF.Tau;
            p.Add(Shape.Sphere, new Vector3(0.09f, 0.07f, 0.09f) * u, new Vector3(MathF.Cos(a) * r, y + (i % 2) * 0.012f, MathF.Sin(a) * r * 0.7f), c, 0f, 0f, null, 6);
        }
    }

    static void SuitLines(Piece p, Figure f)
    {
        float T = f.Torso, u = Scale(f), d = f.ChestD * f.Padding;
        p.Add(Shape.Box, new Vector3(0.2f, 0.09f, 0.03f) * u, new Vector3(0f, T * 0.74f, -d * 1.0f), f.Trim);
        p.Add(Shape.Box, new Vector3(0.05f, 0.03f, 0.02f) * u, new Vector3(0.05f * u, T * 0.75f, -d * 1.08f), new Color(0.75f, 0.85f, 0.5f), 0f, 0.7f);
    }

    static void Tatters(Piece p, Figure f)
    {
        float T = f.Torso, u = Scale(f);
        var rng = new Rng((ulong)(f.Height * 1000f) ^ 0x5151UL);
        for (int i = 0; i < 5; i++)
        {
            float a = rng.Range(0f, MathF.Tau);
            p.Add(Shape.Box, new Vector3(0.06f, 0.2f, 0.012f) * u, new Vector3(MathF.Cos(a) * f.HipW, T * rng.Range(0.05f, 0.25f), MathF.Sin(a) * f.ChestD), f.Cloth, 0f, 0f, new Basis(Vector3.Up, -a) * new Basis(Vector3.Forward, rng.Range(-0.2f, 0.2f)));
        }
    }

    /// <summary>What goes on the body piece; hats and masks go on in <see cref="HeadWear"/>.</summary>
    static void Garment(Piece p, Figure f, string id)
    {
        float T = f.Torso, u = Scale(f), pad = f.Padding, d = f.ChestD * pad, W = f.ShoulderW * pad;
        var upright = new Basis(Vector3.Right, -MathF.PI * 0.5f);
        switch (id)
        {
            case "apron":
                p.Add(Shape.Box, new Vector3(W * 1.5f, T * 0.92f, 0.02f), new Vector3(0f, T * 0.35f, -d * 1.03f), f.Trim.Lightened(0.15f));
                break;
            case "vest":
                var vest = LoftProfile.Smooth(0.42f * T, 0.92f * T, new[] { (0f, f.WaistW * 1.12f * pad, d * 0.98f, 0f), (0.6f, W * 0.98f, d * 1.16f, 0f), (1f, W * 0.8f, d * 0.98f, 0f) }, 7, 0.1f);
                p.Loft(vest, f.Trim, f.Trim.Darkened(0.15f), 0f, 0f, Mat.Carapace, null, upright, 11);
                p.Add(Shape.Box, new Vector3(W * 0.9f, T * 0.14f, 0.03f), new Vector3(0f, T * 0.68f, -d * 1.16f), f.Trim.Lightened(0.12f), 0f, 0f, null, 0, Mat.Carapace);
                break;
            case "coat":
            case "poncho":
                var coat = LoftProfile.Smooth(-0.32f * T, 0.96f * T, new[]
                {
                    (0f, f.HipW * 1.32f * pad, d * 1.2f, 0f), (0.4f, (f.HipW + f.WaistW) * 0.62f * pad, d * 1.05f, 0f), (0.8f, W * 1.02f, d * 1.1f, 0f), (1f, W * 0.72f, d * 0.8f, 0f),
                }, 9, 0.04f);
                p.Loft(coat, id == "coat" ? f.Trim : f.Trim.Darkened(0.1f), f.Trim.Darkened(0.25f), 0f, 0f, Mat.Skin, null, upright, 11);
                break;
            case "shawl":
                var shawl = LoftProfile.Smooth(0.5f * T, 0.98f * T, new[] { (0f, W * 0.82f, d * 1.08f, 0f), (0.7f, W * 1.08f, d * 1.14f, 0f), (1f, W * 0.62f, d * 0.72f, 0f) }, 6, 0.06f);
                p.Loft(shawl, f.Trim, f.Trim.Darkened(0.2f), 0f, 0f, Mat.Skin, null, upright, 11);
                break;
            case "scarf":
                p.Add(Shape.Cylinder, new Vector3(0.21f, 0.09f, 0.18f) * u, new Vector3(0f, T * 1.0f, 0f), f.Accent, 0f, 0f, null, 9);
                p.Add(Shape.Box, new Vector3(0.09f, 0.3f, 0.02f) * u, new Vector3(0.05f * u, T * 0.78f, -d * 1.03f), f.Accent.Darkened(0.1f));
                break;
            case "satchel":
                p.Rod(new Vector3(-W * 0.8f, T * 0.92f, -d * 0.9f), new Vector3(W * 0.95f, T * 0.28f, -d * 0.4f), 0.03f * u, f.Leather);
                p.Rod(new Vector3(-W * 0.8f, T * 0.92f, d * 0.9f), new Vector3(W * 0.95f, T * 0.28f, d * 0.2f), 0.03f * u, f.Leather);
                p.Add(Shape.Box, new Vector3(0.09f, 0.22f, 0.26f) * u, new Vector3(W * 1.02f, T * 0.2f, -d * 0.1f), f.Leather.Lightened(0.12f));
                break;
            case "tool_belt":
                for (int i = 0; i < 3; i++)
                {
                    float a = 0.7f + i * 0.8f;
                    p.Add(Shape.Box, new Vector3(0.08f, 0.11f, 0.05f) * u, new Vector3(MathF.Sin(a) * f.WaistW * 1.15f * pad, T * 0.32f, -MathF.Cos(a) * d), f.Leather.Lightened(0.08f * i));
                }
                break;
            case "backpack":
                p.Add(Shape.Box, new Vector3(0.32f, 0.44f, 0.18f) * u, new Vector3(0f, T * 0.66f, d + 0.09f * u), f.Trim.Darkened(0.2f));
                p.Add(Shape.Cylinder, new Vector3(0.12f, 0.38f, 0.12f) * u, new Vector3(0f, T * 1.04f, d + 0.08f * u), f.Cloth.Darkened(0.25f), 0f, 0f, new Basis(Vector3.Back, MathF.PI * 0.5f), 8);
                p.Add(Shape.Box, new Vector3(0.12f, 0.1f, 0.1f) * u, new Vector3(0.16f * u, T * 0.38f, d + 0.1f * u), f.Metal);
                p.Rod(new Vector3(-W * 0.6f, T * 0.92f, -d * 0.7f), new Vector3(-W * 0.6f, T * 0.45f, -d), 0.03f * u, f.Leather);
                p.Rod(new Vector3(W * 0.6f, T * 0.92f, -d * 0.7f), new Vector3(W * 0.6f, T * 0.45f, -d), 0.03f * u, f.Leather);
                break;
            case "air_tank":
                for (int s = -1; s <= 1; s += 2)
                    p.Add(Shape.Capsule, new Vector3(0.13f, 0.46f, 0.13f) * u, new Vector3(s * 0.08f * u, T * 0.68f, d + 0.07f * u), f.Metal.Lightened(0.15f), 0f, 0f, null, 8, Mat.Carapace);
                break;
            case "bandoleer":
                Bandoleer(p, f, 1f);
                if (f.Height > 1.7f) Bandoleer(p, f, -1f);
                break;
            case "trophies":
                // Bones and a skull hung from the belt: a raider's, at a glance.
                p.Add(Shape.Sphere, new Vector3(0.11f, 0.12f, 0.12f) * u, new Vector3(-f.WaistW * 1.05f * pad, T * 0.24f, -d * 0.4f), new Color(0.82f, 0.78f, 0.66f), 0f, 0f, null, 8, Mat.Horn);
                p.Add(Shape.Sphere, new Vector3(0.03f, 0.03f, 0.02f) * u, new Vector3(-f.WaistW * 1.05f * pad - 0.02f * u, T * 0.25f, -d * 0.4f - 0.05f * u), new Color(0.1f, 0.08f, 0.07f));
                for (int i = 0; i < 3; i++) p.Rod(new Vector3(f.WaistW * (0.2f + i * 0.3f) * pad, T * 0.38f, -d * 1.02f), new Vector3(f.WaistW * (0.22f + i * 0.3f) * pad, T * 0.14f, -d * 1.05f), 0.025f * u, new Color(0.8f, 0.76f, 0.64f), 0f, 0f, false, Mat.Horn);
                break;
            case "scrap_armour":
                p.Add(Shape.Box, new Vector3(W * 0.9f, T * 0.24f, 0.03f), new Vector3(-W * 0.15f, T * 0.62f, -d * 1.07f), f.Metal.Darkened(0.15f), 0f, 0f, new Basis(Vector3.Back, 0.18f), 0, Mat.Carapace);
                p.Add(Shape.Box, new Vector3(W * 0.6f, T * 0.18f, 0.03f), new Vector3(W * 0.3f, T * 0.4f, -d * 1.05f), f.Leather.Darkened(0.2f), 0f, 0f, new Basis(Vector3.Back, -0.25f), 0, Mat.Carapace);
                break;
            case "spike_pads":
                p.Add(Shape.Cone, new Vector3(0.06f, 0.16f, 0f) * u, new Vector3(0f, T * 0.9f, d + 0.05f * u), f.Metal.Lightened(0.2f), 0f, 0f, new Basis(Vector3.Right, MathF.PI * 0.5f), 6, Mat.Horn);
                for (int s = -1; s <= 1; s += 2)
                    p.Add(Shape.Cone, new Vector3(0.05f, 0.14f, 0f) * u, new Vector3(s * W * 0.5f, T * 0.8f, d + 0.04f * u), f.Metal.Lightened(0.2f), 0f, 0f, new Basis(Vector3.Right, MathF.PI * 0.5f), 6, Mat.Horn);
                break;
            case "parka":
            {
                // A shell to the thigh, quilted bulk under it, a fur ruff at the collar.
                Color shell = f.Cloth.Lerp(new Color(0.36f, 0.4f, 0.3f), 0.4f);
                var parka = LoftProfile.Smooth(-0.22f * T, 1.0f * T, new[]
                {
                    (0f, f.HipW * 1.4f * pad, d * 1.3f, 0f), (0.35f, (f.HipW + f.WaistW) * 0.7f * pad, d * 1.25f, 0f), (0.8f, W * 1.1f, d * 1.25f, 0f), (1f, W * 0.78f, d * 0.92f, 0f),
                }, 9, 0.05f);
                p.Loft(parka, shell, shell.Darkened(0.2f), 0f, 0f, Mat.Skin, null, upright, 11);
                for (int i = 0; i < 4; i++) p.Add(Shape.Sphere, new Vector3(Mathf.Lerp(f.HipW * 1.4f * pad, W * 1.1f, 0.25f + 0.2f * i) * 2.04f, 0.02f * u, d * 2.56f), new Vector3(0f, T * (0.1f + 0.2f * i), 0f), shell.Darkened(0.12f), 0f, 0f, null, 10);
                FurTrim(p, f, T * 1.0f, f.ShoulderW * 0.62f, new Color(0.72f, 0.66f, 0.56f));
                break;
            }
            case "robe":
            {
                // A desert robe: long, loose and light, to the shins, a sash at the waist.
                Color robe = f.Cloth.Lightened(0.2f);
                var gown = LoftProfile.Smooth(-0.95f * T, 0.98f * T, new[]
                {
                    (0f, f.HipW * 1.5f * pad, d * 1.4f, 0f), (0.45f, f.HipW * 1.15f * pad, d * 1.12f, 0f), (0.8f, W * 1.04f, d * 1.1f, 0f), (1f, W * 0.7f, d * 0.8f, 0f),
                }, 9, 0.03f);
                p.Loft(gown, robe, robe.Darkened(0.12f), 0f, 0f, Mat.Skin, null, upright, 11);
                p.Add(Shape.Sphere, new Vector3(f.WaistW * 2.5f * pad, 0.05f * u, d * 2.35f), new Vector3(0f, T * 0.4f, 0f), f.Trim, 0f, 0f, null, 10);
                break;
            }
            case "hivis":
            {
                // High-visibility: a bright vest with two pale bands round it. Nothing written on it.
                Color vis = new Color(0.95f, 0.62f, 0.08f).Lerp(new Color(0.85f, 0.88f, 0.12f), f.Height % 0.02f * 50f);
                var bright = LoftProfile.Smooth(0.38f * T, 0.93f * T, new[] { (0f, f.WaistW * 1.12f * pad, d * 1.02f, 0f), (0.6f, W * 0.99f, d * 1.14f, 0f), (1f, W * 0.82f, d * 1.0f, 0f) }, 7, 0.1f);
                p.Loft(bright, vis, vis.Darkened(0.1f), 0f, 0.06f, Mat.Skin, null, upright, 11);
                for (int i = 0; i < 2; i++) p.Add(Shape.Sphere, new Vector3((f.WaistW * f.Padding + W * 0.9f) * 1.12f, 0.035f * u, d * 2.24f), new Vector3(0f, T * (0.55f + 0.14f * i), 0f), new Color(0.8f, 0.82f, 0.82f), 0f, 0.1f, null, 10);
                break;
            }
            case "trader_pack":
                // A frame pack hung with bundles of goods: the trader's kit, read from behind at range.
                p.Add(Shape.Box, new Vector3(0.36f, 0.6f, 0.06f) * u, new Vector3(0f, T * 0.62f, d + 0.06f * u), f.Leather.Darkened(0.2f));
                p.Add(Shape.Box, new Vector3(0.34f, 0.24f, 0.22f) * u, new Vector3(0f, T * 0.82f, d + 0.18f * u), f.Trim.Lightened(0.1f));
                p.Add(Shape.Cylinder, new Vector3(0.15f, 0.42f, 0.15f) * u, new Vector3(0f, T * 1.08f, d + 0.16f * u), f.Accent.Darkened(0.2f), 0f, 0f, new Basis(Vector3.Back, MathF.PI * 0.5f), 8);
                p.Add(Shape.Box, new Vector3(0.28f, 0.2f, 0.2f) * u, new Vector3(0f, T * 0.46f, d + 0.16f * u), f.Cloth.Darkened(0.15f));
                for (int s = -1; s <= 1; s += 2) p.Add(Shape.Sphere, new Vector3(0.1f, 0.14f, 0.1f) * u, new Vector3(s * 0.2f * u, T * 0.62f, d + 0.12f * u), f.Leather.Lightened(0.15f), 0f, 0f, null, 7);
                p.Rod(new Vector3(-W * 0.6f, T * 0.92f, -d * 0.7f), new Vector3(-W * 0.6f, T * 0.45f, -d), 0.03f * u, f.Leather);
                p.Rod(new Vector3(W * 0.6f, T * 0.92f, -d * 0.7f), new Vector3(W * 0.6f, T * 0.45f, -d), 0.03f * u, f.Leather);
                break;
            case "pouches":
                for (int i = 0; i < 4; i++)
                {
                    float a = -1.2f + i * 0.8f;
                    p.Add(Shape.Box, new Vector3(0.07f, 0.09f, 0.05f) * u, new Vector3(MathF.Sin(a) * f.WaistW * 1.15f * pad, T * 0.34f, -MathF.Cos(a) * d * 1.02f), f.Leather.Lightened(0.05f * i));
                }
                break;
            case "rain_cape":
            case "oilskin":
            {
                // A rain cape to the hips, or a waxed shell to the knee: both shed water, both shine.
                bool cape = id == "rain_cape";
                Color wax = cape ? f.Cloth.Lerp(new Color(0.36f, 0.42f, 0.3f), 0.55f) : new Color(0.42f, 0.36f, 0.2f).Lerp(f.Trim, 0.25f);
                var shell = LoftProfile.Smooth((cape ? 0.1f : -0.5f) * T, 1.0f * T, new[] { (0f, f.HipW * (cape ? 1.6f : 1.38f) * pad, d * 1.3f, 0f), (0.5f, W * (cape ? 1.3f : 1.08f), d * 1.22f, 0f), (1f, W * 0.75f, d * 0.9f, 0f) }, 8, 0.04f);
                p.Loft(shell, wax, wax.Darkened(0.2f), 0f, 0f, Mat.Carapace, null, upright, 11);
                break;
            }
            case "badge":
                // The law's mark: a bright disc on the chest, plain metal, nothing written on it.
                p.Add(Shape.Cylinder, new Vector3(0.07f, 0.012f, 0.07f) * u, new Vector3(-W * 0.45f, T * 0.74f, -d * 1.18f), new Color(0.82f, 0.68f, 0.3f), 0f, 0.05f, new Basis(Vector3.Right, MathF.PI * 0.5f), 8, Mat.Carapace);
                break;
            case "medic_band":
                // A white shoulder bag: the medic's sign is the kit, and the cross on the armband.
                p.Rod(new Vector3(W * 0.8f, T * 0.92f, -d * 0.9f), new Vector3(-W * 0.95f, T * 0.28f, -d * 0.4f), 0.03f * u, new Color(0.8f, 0.8f, 0.76f));
                p.Add(Shape.Box, new Vector3(0.09f, 0.2f, 0.24f) * u, new Vector3(-W * 1.02f, T * 0.2f, -d * 0.1f), new Color(0.82f, 0.82f, 0.78f));
                p.Add(Shape.Box, new Vector3(0.1f, 0.13f, 0.04f) * u, new Vector3(-W * 1.05f, T * 0.2f, -d * 0.1f), new Color(0.65f, 0.08f, 0.07f));
                p.Add(Shape.Box, new Vector3(0.1f, 0.04f, 0.13f) * u, new Vector3(-W * 1.05f, T * 0.2f, -d * 0.1f), new Color(0.65f, 0.08f, 0.07f));
                break;
        }
    }

    static void Bandoleer(Piece p, Figure f, float s)
    {
        float T = f.Torso, u = Scale(f), d = f.ChestD * f.Padding, W = f.ShoulderW * f.Padding;
        Vector3 a = new(-s * W * 0.72f, T * 0.92f, -d * 0.92f), b = new(s * W * 0.82f, T * 0.36f, -d * 0.96f);
        p.Rod(a, b, 0.045f * u, f.Leather.Darkened(0.15f));
        p.Rod(new Vector3(a.X, a.Y, d * 0.9f), new Vector3(b.X, b.Y, d * 0.9f), 0.045f * u, f.Leather.Darkened(0.15f));
        for (int i = 1; i < 7; i++)
        {
            Vector3 at = a.Lerp(b, i / 7f) + new Vector3(0f, 0f, -0.02f * u);
            p.Add(Shape.Box, new Vector3(0.02f, 0.06f, 0.02f) * u, at, new Color(0.62f, 0.5f, 0.24f), 0f, 0f, null, 0, Mat.Horn);
        }
    }

    static void Band(Piece arm, Figure f)
    {
        float u = Scale(f);
        arm.Add(Shape.Cylinder, new Vector3(0.11f, 0.06f, 0.11f) * u * f.Padding, Vector3.Up * f.UpperArm * 0.5f, new Color(0.82f, 0.82f, 0.78f), 0f, 0f, null, 8);
        arm.Add(Shape.Box, new Vector3(0.08f, 0.024f, 0.116f) * u * f.Padding, Vector3.Up * f.UpperArm * 0.5f, new Color(0.65f, 0.08f, 0.07f));
        arm.Add(Shape.Box, new Vector3(0.026f, 0.05f, 0.118f) * u * f.Padding, Vector3.Up * f.UpperArm * 0.5f, new Color(0.65f, 0.08f, 0.07f));
    }

    /// <summary>A leather shoulder pad with spikes standing off it.</summary>
    static void Pad(Piece arm, Figure f, int side)
    {
        float u = Scale(f), s = side == 0 ? -1f : 1f;
        arm.Add(Shape.Sphere, new Vector3(0.15f, 0.12f, 0.15f) * u, Vector3.Up * 0.015f * u, f.Leather.Darkened(0.1f), 0f, 0f, null, 8, Mat.Carapace);
        for (int i = 0; i < 3; i++)
        {
            float a = -0.6f + i * 0.6f;
            var dir = new Vector3(s * MathF.Cos(a) * 0.5f, -0.75f, MathF.Sin(a)).Normalized();
            arm.Add(Shape.Cone, new Vector3(0.055f, 0.22f, 0f) * u, dir * 0.13f * u, f.Metal.Lightened(0.25f), 0f, 0f, Geo.AlongY(dir), 6, Mat.Horn);
        }
    }

    /// <summary>The head: a skull with a jaw, a nose, brows, ears, then hair, then whatever is on it.</summary>
    static void Head(Piece p, Figure f, Paint k)
    {
        float R = f.HeadRadius, N = f.Neck, u = Scale(f);
        var c = new Vector3(0f, N + R * 0.92f, 0f);
        Color skin = f.Skin;
        bool child = f.Stage == LifeStage.Child;
        Folk folk = f.Folk ?? new Folk();
        float skinMat = FolkBody.SkinMat(f);
        p.Taper(new Vector3(0f, -0.03f, 0.005f), new Vector3(0f, N + R * 0.2f, 0f), 0.052f * u * (0.8f + 0.5f * f.Build), 0.046f * u, skin, 0f, 0f, skinMat, 8);
        p.Add(Shape.Sphere, new Vector3(R * 1.68f, R * 1.95f, R * 2f), c + new Vector3(0f, R * 0.08f, 0f), skin, 0f, 0f, null, 10, skinMat);
        // Jaw: heavier in men, narrow in women and the young; small under a beak or mandibles.
        float jaw = ((f.Sex == Sex.Male && !child ? 1.06f : 0.92f) + f.Jaw) * (folk.Mouth is "beak" or "mandibles" ? 0.82f : 1f);
        // (A Husk's jaw may hang far too low: stretched down, the mouth with it.)
        float drop = folk.Mouth == "" ? f.JawDrop : f.JawDrop * 0.4f;
        p.Add(Shape.Sphere, new Vector3(R * 1.34f * jaw * (1f - 0.15f * drop), R * 1.15f * (1f + 1.2f * drop), R * 1.42f), c + new Vector3(0f, -R * (0.56f + 0.5f * drop), -R * 0.2f), skin.Darkened(0.03f), 0f, 0f, null, 9, skinMat);
        // The face (a nose and mouth, or a muzzle, snout, beak or mandibles) gives the openings the worms use.
        FolkBody.Openings open = FolkBody.Face(p, f, c, skin);
        // Brows: a folk's ridge sets how far they stand out.
        float ridge = MathF.Max(0.08f, 0.18f + 0.22f * folk.BrowRidge + f.Brow);
        var husk = new Rng((ulong)(f.Height * 1000f) * 977UL + (ulong)(f.Skin.R * 1000f));
        int worms = 0;
        foreach (var (eye, size, s) in FolkBody.EyeSpots(f, c))
        {
            bool plain = folk.Eyes is "two" or "three" or "four";
            if (plain) p.Add(Shape.Sphere, new Vector3(size * 2.94f, size * 1.65f, size * 1.3f), eye + new Vector3(0f, -R * 0.03f, R * 0.07f), skin.Darkened(0.22f), 0f, 0f, null, 6);
            if (f.Husk && plain)
            {
                // The eye is gone: a dark socket, and what took its place coming out of it.
                p.Add(Shape.Sphere, Vector3.One * size * 1.76f, eye, new Color(0.05f, 0.03f, 0.03f), 0f, 0f, null, 6);
                int n = 1 + (s > 0 ? 1 : 0);
                for (int i = 0; i < n && worms < 4; i++, worms++)
                    Worm(p, f, eye + new Vector3(s * R * 0.04f * i, -R * 0.03f * i, -R * 0.08f), new Vector3(s * (0.2f + 0.3f * i), -0.25f - 0.3f * i, -0.9f), 0.7f + 0.25f * i, 20 + (s > 0 ? 2 : 0) + i);
            }
            else if (f.Husk)
            {
                // A compound eye or a band goes dark; a worm works out between the facets, never through the whole eye.
                FolkBody.Eye(p, f, eye, size, s);
                if (worms < 3) { Worm(p, f, eye + new Vector3(s * size * 0.3f, -size * 0.4f, -size * 0.3f), new Vector3(s * 0.4f, -0.6f, -0.6f), 0.6f, 20 + worms); worms++; }
            }
            else if (folk.Human) Parts.Eye(p, k, eye + new Vector3(0f, 0f, -R * 0.05f), size);
            else FolkBody.Eye(p, f, eye + new Vector3(0f, 0f, -R * 0.05f), size, s);
            if (s != 0 && folk.Covering != "chitin") p.Add(Shape.Sphere, new Vector3(R * 0.62f, R * ridge, R * 0.3f), c + new Vector3(s * R * 0.36f, R * 0.34f, -R * 0.79f), skin.Darkened(0.08f), 0f, 0f, null, 6);
        }
        for (int s = -1; s <= 1; s += 2) FolkBody.Ear(p, f, c, s, skin);
        FolkBody.Covering(p, f, c, skin);
        if (f.Husk)
        {
            // What's in them comes out of every opening their face has: the mouth (hanging open, too
            // far if the jaw has gone; along a snout's or a beak's whole gape; between mandibles),
            // the nostrils and the ear holes, and moves on its own. At most sixteen.
            Vector3 mouth = open.Mouth;
            bool along = open.MouthAlong != Vector3.Right;
            p.Add(Shape.Sphere, along ? new Vector3(R * 0.3f, R * (0.3f + drop * 0.6f), open.Gape * 1.1f) : new Vector3(open.Gape * 1.3f, R * (0.34f + drop * 0.6f), R * 0.24f), mouth, new Color(0.07f, 0.03f, 0.03f), 0f, 0f, null, 7);
            // (A face with no ear holes puts more out of the mouth: never fewer than the human count.)
            int many = husk.Int(5, 8) + (open.EarHoles ? 0 : 3);
            for (int i = 0; i < many; i++)
            {
                float spread = many == 1 ? 0f : i / (many - 1f) - 0.5f;
                Vector3 at = mouth + open.MouthAlong * spread * open.Gape + new Vector3(0f, husk.Range(-0.1f, 0.1f) * R, along ? 0f : -R * 0.06f);
                Vector3 dir = along ? new Vector3((i % 2 == 0 ? 1f : -1f) * 0.7f + husk.Range(-0.2f, 0.2f), husk.Range(-0.9f, -0.2f), -0.4f) : new Vector3(spread * 0.9f + husk.Range(-0.2f, 0.2f), husk.Range(-0.6f, 0.15f), -1f);
                Worm(p, f, at, dir, husk.Range(0.8f, 1.35f), 2 + i);
            }
            worms += many;
            for (int s = -1; s <= 1; s += 2)
            {
                Worm(p, f, open.Nose + new Vector3(s * R * 0.08f, 0f, 0f), new Vector3(s * 0.25f, -0.6f, -0.7f), husk.Range(0.5f, 0.75f), 12 + s);
                worms++;
                if (!open.EarHoles) continue;
                int ear = husk.Int(1, 2);
                for (int i = 0; i < ear && worms < 16; i++, worms++)
                    Worm(p, f, c + new Vector3(s * R * 0.94f, R * 0.04f * i, R * 0.08f), new Vector3(s * 0.9f, -0.2f + 0.35f * i, 0.15f - 0.3f * i), husk.Range(0.55f, 0.9f), 15 + s * 3 + i);
            }
        }
        else if (folk.Mouth == "") p.Add(Shape.Box, new Vector3(R * 0.5f, R * 0.05f, R * 0.04f), c + new Vector3(0f, -R * 0.6f, -R * 0.84f), skin.Darkened(0.4f));
        FolkBody.Crown(p, f, c, skin);
        if (!f.Has("suit_helmet") && !f.Has("fur_hood") && !f.Has("hood") && !f.Has("head_wrap") && !f.Has("hazard_hood") && !(f.Has("balaclava") && !f.Husk)) Hair(p, f, c);
        if (f.Beard) p.Add(Shape.Sphere, new Vector3(R * 1.22f, R * 0.9f, R * 1.2f), c + new Vector3(0f, -R * 0.82f, -R * 0.33f), f.Hair, 0f, 0f, null, 8);
        if (f.Has("face_paint"))
            p.Add(Shape.Box, new Vector3(R * 1.56f, R * 0.22f, R * 0.1f), c + new Vector3(0f, R * 0.14f, -R * 0.93f), new Color(0.1f, 0.09f, 0.09f));
        foreach (string w in f.Wear) HeadWear(p, f, w, c);
    }

    /// <summary>A worm coming out of an opening: a wet, ringed curve of a few joints, tapering to nothing, that moves on its own (<see cref="Piece.Worm"/>).</summary>
    static void Worm(Piece p, Figure f, Vector3 from, Vector3 dir, float length, int salt)
    {
        var rng = new Rng((ulong)(f.Height * 1000f) * 131UL + (ulong)salt);
        float R = f.HeadRadius;
        // Pale flesh to the dark red of something that lives inside.
        Color flesh = new Color(0.78f, 0.66f, 0.58f).Lerp(new Color(0.42f, 0.14f, 0.16f), rng.Range(0.1f, 0.9f));
        p.Worm(from, dir, R * 1.75f * length, R * rng.Range(0.075f, 0.1f), flesh, salt + (int)(f.Height * 100f), rng, 4, 0.28f, 0.4f);
    }

    /// <summary>
    /// Their clothes torn: rents in the cloth with skin showing through, each with a dark frayed
    /// edge, at the chest, the back and the belly. Lies on the body's own surface.
    /// </summary>
    static void Rents(Piece p, Figure f)
    {
        var rng = new Rng((ulong)(f.Height * 1000f) ^ 0x7E47UL ^ (ulong)(f.Cloth.R * 997f));
        float T = f.Torso, u = Scale(f), pad = f.Padding, d = f.ChestD * pad;
        int n = rng.Int(4, 7);
        for (int i = 0; i < n; i++)
        {
            float t = rng.Range(0.42f, 0.9f);
            float half = Mathf.Lerp(f.WaistW, f.ShoulderW * 0.9f, Math.Clamp((t - 0.34f) / 0.5f, 0f, 1f)) * pad;
            float a = rng.Range(0f, MathF.Tau);
            Vector3 normal = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            Vector3 at = new(MathF.Cos(a) * half, T * t, MathF.Sin(a) * d);
            Basis b = Geo.AlongY(normal) * new Basis(Vector3.Up, rng.Range(0f, MathF.Tau));
            float w = rng.Range(0.09f, 0.17f) * u, h = rng.Range(0.07f, 0.14f) * u;
            p.Add(Shape.Sphere, new Vector3(w * 1.25f, 0.014f, h * 1.25f), at + normal * 0.004f, f.Cloth.Darkened(0.55f), 0f, 0f, b, 8);
            p.Add(Shape.Sphere, new Vector3(w, 0.016f, h), at + normal * 0.008f, f.Skin.Darkened(rng.Range(0.05f, 0.2f)), 0f, 0f, b, 8);
        }
        // Strips hanging from the hem and the shoulders.
        for (int i = 0; i < 6; i++)
        {
            float a = rng.Range(0f, MathF.Tau);
            bool hem = i < 4;
            float y = hem ? T * rng.Range(0.3f, 0.36f) : T * 0.88f;
            float r = hem ? f.WaistW * 1.05f * pad : f.ShoulderW * 0.95f * pad;
            p.Add(Shape.Box, new Vector3(0.05f, rng.Range(0.12f, 0.24f), 0.01f) * u, new Vector3(MathF.Cos(a) * r, y - 0.08f * u, MathF.Sin(a) * d * 1.02f), f.Cloth.Darkened(0.15f),
                0f, 0f, new Basis(Vector3.Up, -a) * new Basis(Vector3.Forward, rng.Range(-0.25f, 0.25f)));
        }
    }

    /// <summary>A back bent and swollen at the top: the hump a hunched Husk carries, under its clothes.</summary>
    static void Hump(Piece p, Figure f)
    {
        float T = f.Torso, d = f.ChestD * f.Padding, W = f.ShoulderW * f.Padding;
        p.Add(Shape.Sphere, new Vector3(W * 1.5f, T * 0.38f, d * 1.5f), new Vector3(W * 0.12f, T * 0.84f, d * 0.75f), f.Cloth.Darkened(0.08f), 0f, 0f, new Basis(Vector3.Right, 0.4f), 10);
        p.Add(Shape.Sphere, new Vector3(W * 0.7f, T * 0.2f, d * 0.9f), new Vector3(-W * 0.3f, T * 0.92f, d * 0.9f), f.Skin.Darkened(0.1f), 0f, 0f, null, 9, Mat.Membrane);
    }

    static void Hair(Piece p, Figure f, Vector3 c)
    {
        float R = f.HeadRadius;
        Color h = f.Hair;
        switch (f.HairStyle)
        {
            case "short":
            case "long":
            case "tied":
                p.Add(Shape.Sphere, new Vector3(R * 1.8f, R * 1.72f, R * 2.08f), c + new Vector3(0f, R * 0.34f, R * 0.08f), h, 0f, 0f, null, 9);
                break;
            case "crop":
                p.Add(Shape.Sphere, new Vector3(R * 1.73f, R * 1.7f, R * 2.02f), c + new Vector3(0f, R * 0.26f, R * 0.04f), h.Lerp(f.Skin, 0.3f), 0f, 0f, null, 9);
                break;
            case "mohawk":
                Color dye = h.Lerp(new Color(0.75f, 0.12f, 0.1f), 0.8f);
                for (int i = 0; i < 7; i++)
                {
                    float a = -0.6f + i * 0.42f;
                    Vector3 dir = new(0f, MathF.Cos(a), MathF.Sin(a));
                    p.Add(Shape.Cone, new Vector3(R * 0.34f, R * 0.95f, 0f), c + new Vector3(0f, R * 0.08f, 0f) + dir * R * 0.86f, dye, 0f, 0f, Geo.AlongY(dir), 5);
                }
                break;
        }
        if (f.HairStyle == "long")
        {
            p.Taper(c + new Vector3(0f, R * 0.3f, R * 0.74f), c + new Vector3(0f, -R * 1.8f, R * 0.86f), R * 0.82f, R * 0.5f, h, 0f, 0f, Mat.Skin, 7);
            for (int s = -1; s <= 1; s += 2) p.Taper(c + new Vector3(s * R * 0.74f, R * 0.2f, R * 0.2f), c + new Vector3(s * R * 0.8f, -R * 1.2f, R * 0.3f), R * 0.32f, R * 0.18f, h, 0f, 0f, Mat.Skin, 6);
        }
        if (f.HairStyle == "tied") p.Add(Shape.Sphere, Vector3.One * R * 0.72f, c + new Vector3(0f, R * 0.5f, R * 1.1f), h.Darkened(0.1f), 0f, 0f, null, 7);
    }

    static void HeadWear(Piece p, Figure f, string id, Vector3 c)
    {
        float R = f.HeadRadius, u = Scale(f);
        switch (id)
        {
            case "helmet":
                p.Add(Shape.Sphere, new Vector3(R * 2.14f, R * 1.72f, R * 2.4f), c + new Vector3(0f, R * 0.42f, R * 0.02f), f.Trim, 0f, 0f, null, 10, Mat.Carapace);
                p.Add(Shape.Box, new Vector3(R * 2.18f, R * 0.12f, R * 2.46f), c + new Vector3(0f, R * 0.18f, 0f), f.Trim.Darkened(0.25f), 0f, 0f, null, 0, Mat.Carapace);
                break;
            case "cap":
                p.Add(Shape.Sphere, new Vector3(R * 1.92f, R * 1.3f, R * 2.14f), c + new Vector3(0f, R * 0.52f, 0.0f), f.Trim, 0f, 0f, null, 9);
                p.Add(Shape.Box, new Vector3(R * 1.2f, R * 0.08f, R * 0.8f), c + new Vector3(0f, R * 0.34f, -R * 1.2f), f.Trim.Darkened(0.2f));
                break;
            case "wide_hat":
            case "straw_hat":
                Color straw = id == "straw_hat" ? new Color(0.72f, 0.62f, 0.36f) : f.Leather.Lightened(0.08f);
                p.Add(Shape.Cylinder, new Vector3(R * 4.2f, R * 0.09f, R * 4.0f), c + new Vector3(0f, R * 0.46f, 0f), straw, 0f, 0f, null, 12);
                p.Add(Shape.Cylinder, new Vector3(R * 2.02f, R * 0.84f, R * 1.72f), c + new Vector3(0f, R * 0.86f, 0f), straw.Darkened(0.1f), 0f, 0f, null, 10);
                break;
            case "hood":
                p.Add(Shape.Sphere, new Vector3(R * 2.24f, R * 2.4f, R * 2.46f), c + new Vector3(0f, R * 0.1f, R * 0.22f), f.Trim, 0f, 0f, null, 10);
                break;
            case "fur_hood":
                p.Add(Shape.Sphere, new Vector3(R * 2.34f, R * 2.5f, R * 2.5f), c + new Vector3(0f, R * 0.1f, R * 0.26f), f.Cloth, 0f, 0f, null, 10);
                for (int i = 0; i < 10; i++)
                {
                    float a = i / 10f * MathF.Tau;
                    p.Add(Shape.Sphere, Vector3.One * R * 0.6f, c + new Vector3(MathF.Cos(a) * R * 1.0f, MathF.Sin(a) * R * 1.12f - R * 0.05f, -R * 0.82f), f.Cloth.Lightened(0.3f), 0f, 0f, null, 6);
                }
                break;
            case "head_wrap":
                p.Add(Shape.Sphere, new Vector3(R * 2.06f, R * 2.16f, R * 2.26f), c + new Vector3(0f, R * 0.22f, R * 0.08f), f.Cloth.Lightened(0.15f), 0f, 0f, null, 10);
                p.Add(Shape.Sphere, new Vector3(R * 1.66f, R * 0.9f, R * 1.3f), c + new Vector3(0f, -R * 0.6f, -R * 0.36f), f.Cloth.Lightened(0.1f), 0f, 0f, null, 9);
                p.Taper(c + new Vector3(0f, R * 0.1f, R * 1.0f), c + new Vector3(R * 0.2f, -R * 1.7f, R * 1.3f), R * 0.3f, R * 0.14f, f.Cloth, 0f, 0f, Mat.Skin, 6);
                break;
            case "shades":
            case "goggles":
                for (int s = -1; s <= 1; s += 2)
                    p.Add(Shape.Sphere, new Vector3(R * 0.62f, R * 0.46f, R * 0.22f), c + new Vector3(s * R * 0.36f, R * 0.14f, -R * 0.93f), new Color(0.04f, 0.05f, 0.06f), 0f, 0f, null, 8, Mat.Eye);
                p.Add(Shape.Box, new Vector3(R * 1.76f, R * 0.08f, R * 1.1f), c + new Vector3(0f, R * 0.14f, -R * 0.4f), f.Leather.Darkened(0.2f));
                break;
            case "eyepatch":
                p.Add(Shape.Sphere, new Vector3(R * 0.54f, R * 0.5f, R * 0.14f), c + new Vector3(R * 0.36f, R * 0.14f, -R * 0.92f), new Color(0.07f, 0.06f, 0.06f), 0f, 0f, null, 7);
                p.Add(Shape.Box, new Vector3(R * 1.74f, R * 0.06f, R * 1.1f), c + new Vector3(0f, R * 0.3f, -R * 0.36f), new Color(0.07f, 0.06f, 0.06f));
                break;
            case "net_hat":
                // A wide hat with insect netting hanging from the brim (lifted off the face of a Husk).
                p.Add(Shape.Cylinder, new Vector3(R * 4.0f, R * 0.09f, R * 3.8f), c + new Vector3(0f, R * 0.5f, 0f), new Color(0.7f, 0.62f, 0.4f), 0f, 0f, null, 12);
                p.Add(Shape.Cylinder, new Vector3(R * 2.0f, R * 0.8f, R * 1.7f), c + new Vector3(0f, R * 0.88f, 0f), new Color(0.64f, 0.56f, 0.36f), 0f, 0f, null, 10);
                if (!f.Husk) p.Add(Shape.Cylinder, new Vector3(R * 3.7f, R * 1.7f, R * 3.6f), c + new Vector3(0f, -R * 0.38f, 0f), new Color(0.12f, 0.13f, 0.12f), 0f, 0f, null, 12, Mat.Membrane);
                break;
            case "balaclava":
                // Knit over the head and face, the eyes left open.
                if (f.Husk) break;
                p.Add(Shape.Sphere, new Vector3(R * 1.82f, R * 1.4f, R * 1.95f), c + new Vector3(0f, -R * 0.38f, -R * 0.05f), f.Trim.Darkened(0.3f), 0f, 0f, null, 9);
                p.Add(Shape.Sphere, new Vector3(R * 1.84f, R * 1.2f, R * 2.06f), c + new Vector3(0f, R * 0.55f, R * 0.06f), f.Trim.Darkened(0.3f), 0f, 0f, null, 9);
                break;
            case "face_wrap":
                // Cloth over the nose and mouth against sand, its end hanging.
                if (f.Husk) break;
                {
                    float face = MathF.Max(0f, FolkBody.FaceLength(f) - 1.05f);
                    p.Add(Shape.Sphere, new Vector3(R * 1.74f, R * 0.95f, R * (1.7f + face)), c + new Vector3(0f, -R * 0.42f, -R * (0.12f + face * 0.5f)), f.Cloth.Lightened(0.12f), 0f, 0f, null, 9);
                    p.Taper(c + new Vector3(R * 0.6f, -R * 0.5f, R * 0.4f), c + new Vector3(R * 0.75f, -R * 1.9f, R * 0.7f), R * 0.22f, R * 0.12f, f.Cloth.Lightened(0.08f), 0f, 0f, Mat.Skin, 6);
                }
                break;
            case "filter_mask":
            {
                if (f.Husk) break;
                // A full mask: a visor over the eyes, twin filter canisters at the jaw.
                float face = MathF.Max(0f, FolkBody.FaceLength(f) - 1.05f);
                p.Add(Shape.Sphere, new Vector3(R * 1.5f, R * 1.5f, R * (0.9f + face)), c + new Vector3(0f, -R * 0.1f, -R * (0.62f + face * 0.55f)), new Color(0.16f, 0.17f, 0.16f), 0f, 0f, null, 9, Mat.Carapace);
                p.Add(Shape.Sphere, new Vector3(R * 1.2f, R * 0.5f, R * 0.4f), c + new Vector3(0f, R * 0.16f, -R * (1.0f + face * 0.6f)), new Color(0.12f, 0.16f, 0.14f), 0f, 0f, null, 9, Mat.Eye);
                for (int s = -1; s <= 1; s += 2)
                    p.Add(Shape.Cylinder, new Vector3(R * 0.62f, R * 0.7f, R * 0.62f), c + new Vector3(s * R * 0.7f, -R * 0.66f, -R * (1.0f + face * 0.5f)), new Color(0.62f, 0.56f, 0.2f), 0f, 0f, new Basis(Vector3.Right, MathF.PI * 0.4f) * new Basis(Vector3.Forward, s * 0.6f), 8, Mat.Carapace);
                break;
            }
            case "hazard_hood":
                if (f.Husk) break;
                p.Add(Shape.Sphere, new Vector3(R * 2.36f, R * 2.5f, R * 2.5f), c + new Vector3(0f, R * 0.12f, R * 0.18f), f.Cloth.Lightened(0.05f), 0f, 0f, null, 10);
                p.Add(Shape.Taper, new Vector3(R * 2.4f, f.Neck * 1.2f, R * 2.2f), new Vector3(0f, 0f, 0f), f.Cloth, 0f, 0f, null, 9);
                break;
            case "respirator":
            {
                // A muzzle mask sized to the face: a snout or a beak gets a longer cover.
                float face = MathF.Max(0f, FolkBody.FaceLength(f) - 1.05f);
                p.Add(Shape.Sphere, new Vector3(R * 1.16f, R * 0.86f, R * (0.74f + face)), c + new Vector3(0f, -R * 0.42f, -R * (0.82f + face * 0.55f)), new Color(0.13f, 0.14f, 0.14f), 0f, 0f, null, 9, Mat.Carapace);
                for (int s = -1; s <= 1; s += 2)
                    p.Add(Shape.Cylinder, new Vector3(R * 0.5f, R * 0.5f, R * 0.5f), c + new Vector3(s * R * 0.56f, -R * 0.56f, -R * (1.06f + face * 0.5f)), f.Metal, 0f, 0f, new Basis(Vector3.Right, MathF.PI * 0.5f) * new Basis(Vector3.Forward, s * 0.5f), 8, Mat.Carapace);
                break;
            }
            case "suit_helmet":
            {
                // The bubble goes round the whole head, snout and crown: on a vacuum world its shape is the folk's.
                float face = MathF.Max(0f, FolkBody.FaceLength(f) - 1.05f), rise = MathF.Min(1.2f, f.Folk?.CrownRise ?? 0f);
                p.Add(Shape.Sphere, new Vector3(R * 2.86f, R * (2.86f + rise * 0.8f), R * (2.86f + face)), c + new Vector3(0f, R * (0.04f + rise * 0.4f), -R * face * 0.5f), f.Cloth.Lightened(0.08f), 0f, 0f, null, 12, Mat.Carapace);
                p.Add(Shape.Sphere, new Vector3(R * 2.16f, R * 1.56f, R * 1.2f), c + new Vector3(0f, R * 0.12f, -R * (0.9f + face)), new Color(0.05f, 0.08f, 0.1f), 0f, 0f, null, 12, Mat.Eye);
            }
                p.Add(Shape.Cylinder, new Vector3(0.21f, 0.05f, 0.21f) * u, new Vector3(0f, f.Neck * 0.3f, 0f), f.Trim, 0f, 0f, null, 10, Mat.Carapace);
                break;
        }
    }

    static void Prop(Piece p, Figure f)
    {
        float u = Scale(f);
        Color steel = new(0.17f, 0.17f, 0.18f), wood = new(0.33f, 0.22f, 0.13f);
        // Prop space: origin in the hand that holds it, +Y along the thing and out through the far end.
        switch (f.Prop)
        {
            case "rifle":
                p.Add(Shape.Box, new Vector3(0.05f, 0.92f, 0.05f) * u, new Vector3(0f, 0.2f * u, 0f), steel);
                p.Add(Shape.Box, new Vector3(0.055f, 0.3f, 0.09f) * u, new Vector3(0f, -0.3f * u, 0.015f * u), wood);
                p.Add(Shape.Box, new Vector3(0.05f, 0.12f, 0.1f) * u, new Vector3(0f, 0.02f * u, 0.07f * u), steel);
                break;
            case "club":
                p.Taper(new Vector3(0f, -0.14f, 0f) * u, new Vector3(0f, 0.55f, 0f) * u, 0.022f * u, 0.05f * u, wood);
                for (int i = 0; i < 4; i++)
                {
                    var dir = new Vector3(i % 2 == 0 ? 1f : -1f, 0.2f, i < 2 ? 0.7f : -0.7f).Normalized();
                    p.Add(Shape.Cone, new Vector3(0.03f, 0.1f, 0f) * u, new Vector3(0f, (0.38f + i * 0.05f) * u, 0f) + dir * 0.06f * u, f.Metal, 0f, 0f, Geo.AlongY(dir), 5, Mat.Horn);
                }
                break;
            case "hoe":
                p.Taper(new Vector3(0f, -0.7f, 0f) * u, new Vector3(0f, 0.7f, 0f) * u, 0.016f * u, 0.016f * u, wood);
                p.Add(Shape.Box, new Vector3(0.14f, 0.06f, 0.22f) * u, new Vector3(0f, 0.72f * u, -0.08f * u), f.Metal);
                break;
            case "cane":
                p.Taper(new Vector3(0f, 0f, 0f), new Vector3(0f, 0.9f, 0f) * u, 0.016f * u, 0.012f * u, wood);
                p.Add(Shape.Sphere, Vector3.One * 0.05f * u, new Vector3(0f, -0.01f, 0f), wood.Darkened(0.2f), 0f, 0f, null, 6);
                break;
            case "wrench":
                p.Taper(new Vector3(0f, -0.04f, 0f) * u, new Vector3(0f, 0.3f, 0f) * u, 0.014f * u, 0.012f * u, steel.Lightened(0.25f), 0f, 0f, Mat.Carapace, 6);
                p.Add(Shape.Box, new Vector3(0.07f, 0.05f, 0.025f) * u, new Vector3(0.015f * u, 0.32f * u, 0f), steel.Lightened(0.3f), 0f, 0f, null, 0, Mat.Carapace);
                break;
            case "umbrella":
                // Held up: the shaft through the hand, the canopy over the head.
                p.Taper(new Vector3(0f, -0.1f, 0f) * u, new Vector3(0f, 0.95f, 0f) * u, 0.01f * u, 0.008f * u, steel, 0f, 0f, Mat.Carapace, 5);
                p.Add(Shape.Cone, new Vector3(1.0f, 0.24f, 0f) * u, new Vector3(0f, 0.86f, 0f) * u, f.Accent.Lerp(f.Cloth, 0.5f).Darkened(0.2f), 0f, 0f, null, 9, Mat.Membrane);
                break;
            case "bottle":
                p.Add(Shape.Cylinder, new Vector3(0.07f, 0.17f, 0.07f) * u, new Vector3(0f, 0.04f * u, 0f), new Color(0.2f, 0.32f, 0.16f), 0f, 0f, null, 7, Mat.Eye);
                p.Add(Shape.Cylinder, new Vector3(0.03f, 0.08f, 0.026f) * u, new Vector3(0f, 0.17f * u, 0f), new Color(0.2f, 0.32f, 0.16f), 0f, 0f, null, 6, Mat.Eye);
                break;
        }
    }

    /// <summary>
    /// The whole person as one cheap piece: what a crowd is seen as from a distance. A dozen
    /// primitives, standing, in their main colours: one draw call, nothing to pose.
    /// </summary>
    public static Piece Far(Species s)
    {
        Figure f = s.Figure;
        float u = Scale(f), pad = f.Padding;
        var p = new Piece("far");
        Color top = f.Dress == "rags" ? f.Skin : f.Has("hivis") ? new Color(0.95f, 0.62f, 0.08f) : f.Has("robe") ? f.Cloth.Lightened(0.2f) : f.Has("parka") ? f.Cloth.Lerp(new Color(0.36f, 0.4f, 0.3f), 0.4f)
            : f.Has("coat") || f.Has("poncho") || f.Has("shawl") ? f.Trim : f.Dress is "suit" or "hazard" ? f.Cloth : Outer(f);
        Color leg = f.Dress is "suit" or "hazard" ? f.Cloth : f.Trim.Darkened(0.1f).Lerp(new Color(0.2f, 0.2f, 0.22f), 0.5f);
        float legH = f.LegLength, chest = f.Torso;
        for (int side = -1; side <= 1; side += 2)
        {
            p.Add(Shape.Box, new Vector3(0.14f * pad, legH, 0.17f * pad) * new Vector3(u, 1f, u), new Vector3(side * f.HipW * 0.5f, legH * 0.5f, 0f), leg);
            p.Add(Shape.Box, new Vector3(0.09f * pad * u, f.UpperArm + f.Forearm, 0.1f * u), new Vector3(side * (f.ShoulderW * pad + 0.04f * u), legH + chest - (f.UpperArm + f.Forearm) * 0.5f - 0.03f, 0f), f.Dress is "shirt" or "rags" ? f.Skin : top);
        }
        p.Add(Shape.Box, new Vector3(f.ShoulderW * 2f * pad, chest, f.ChestD * 2f * pad), new Vector3(0f, legH + chest * 0.5f, 0f), top);
        Color head = f.Has("suit_helmet") ? f.Cloth : f.Has("helmet") ? f.Trim : f.Has("fur_hood") ? f.Cloth : f.Has("head_wrap") ? f.Cloth.Lightened(0.15f) : f.Has("straw_hat") ? new Color(0.72f, 0.62f, 0.36f) : f.HairStyle == "bald" ? f.Skin : f.Skin.Lerp(f.Hair, 0.4f);
        p.Add(Shape.Sphere, Vector3.One * f.HeadRadius * (f.Has("suit_helmet") || f.Has("fur_hood") ? 2.8f : 2f), new Vector3(0f, f.Height - f.HeadRadius, 0f), head, 0f, 0f, null, 6);
        if (f.Has("wide_hat") || f.Has("straw_hat")) p.Add(Shape.Box, new Vector3(f.HeadRadius * 4f, 0.03f, f.HeadRadius * 4f), new Vector3(0f, f.Height - f.HeadRadius * 0.7f, 0f), head);
        if (f.Has("backpack") || f.Has("air_tank")) p.Add(Shape.Box, new Vector3(0.32f, 0.44f, 0.18f) * u, new Vector3(0f, legH + chest * 0.66f, f.ChestD * pad + 0.09f * u), f.Trim.Darkened(0.2f));
        // A raider's outline carries from far off: spikes on the shoulders and the crest.
        if (f.Has("spike_pads"))
            for (int side = -1; side <= 1; side += 2)
                p.Add(Shape.Cone, new Vector3(0.08f, 0.3f, 0f) * u, new Vector3(side * (f.ShoulderW * pad + 0.06f * u), legH + chest + 0.08f * u, 0f), f.Metal.Lightened(0.2f), 0f, 0f, new Basis(Vector3.Back, -side * 0.6f), 5);
        if (f.HairStyle == "mohawk" && !f.Has("suit_helmet")) p.Add(Shape.Box, new Vector3(0.05f, f.HeadRadius * 1.2f, f.HeadRadius * 2.2f), new Vector3(0f, f.Height + f.HeadRadius * 0.3f, 0f), new Color(0.7f, 0.12f, 0.1f));
        p.Add(Shape.Box, new Vector3(0.1f, 0.06f, 0.1f) * u, new Vector3(-(f.ShoulderW * pad + 0.04f * u), legH + chest - f.UpperArm * 0.5f, 0f), f.Accent);
        FolkBody.Far(p, f, head);
        return p;
    }
}
