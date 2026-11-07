using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Machines still walking their rounds. They come from the same generation as everything else:
/// a species with no blood and no lungs, on the same rigs (two legs on the people's rig, four or
/// six on the walkers'), under the same brains and spawning rules; only their bodies are hard. So
/// they go where nothing that breathes can, and sense the world with what they were built with.
/// They patrol. They never come in waves.
/// </summary>
public static class Robots
{
    /// <summary>One kind of machine for this world; <paramref name="n"/> tells a world's kinds apart.</summary>
    public static Species Roll(WorldConditions w, int n)
    {
        ulong seed = Rng.Mix(w.Seed, 0xB07 + (ulong)n);
        var rng = Rng.For(seed, "robot");
        int legs = n % 3 == 0 ? 4 : n % 3 == 1 ? 6 : 2;
        var s = new Species
        {
            Id = $"robot-{seed:x}",
            Seed = seed,
            Index = 300 + n,
            Plan = BodyPlan.Walker,
            Form = legs == 2 ? "automaton" : legs == 4 ? "hound-frame" : "crab-frame",
            Rig = legs == 2 ? RigKind.Humanoid : RigKind.Legged,
            Mechanical = true,
            Coat = Coat.Plating,
            Name = "machine",
            CommonName = legs == 2 ? "walking machine" : legs == 4 ? "four-legged machine" : "six-legged machine",
            Behaviour = Behaviour.Predator,
            Temperament = Temperament.Bold,
            Activity = Activity.Cathemeral,
            RestKind = RestKind.Den,
            GroupMin = 1,
            GroupMax = 2,
            Senses = new SenseProfile { Sight = 1.1f, DarkVision = 1f, Hearing = 0.8f, Scent = 0f },
        };
        foreach (string slot in FaunaTables.Slots) s.Parts[slot] = "none";
        s.Parts["head"] = "dome";
        Color hull = new Color(0.34f, 0.35f, 0.33f).Lerp(Color.FromHsv(rng.Range(0f, 1f), 0.35f, 0.42f), rng.Range(0f, 0.5f));
        s.Colors.Base = hull;
        s.Colors.Accent = hull.Darkened(0.35f);
        s.Colors.Belly = hull.Darkened(0.2f);
        s.Colors.Glow = Color.FromHsv(rng.Pick(new[] { 0f, 0.08f, 0.5f, 0.56f }), 0.85f, 1f);
        s.Colors.GlowStrength = 0.9f;
        s.Colors.Pattern = Pattern.Plain;
        Proportions b = s.Body;
        float k = rng.Range(0.9f, 1.25f);
        if (legs == 2)
        {
            s.Figure = Automaton(rng, k, hull);
            Figure f = s.Figure;
            b.Size = f.Torso; b.Width = f.ShoulderW * 2f; b.Height = f.Torso; b.LegLength = f.LegLength; b.LegThickness = 0.1f; b.HeadSize = f.HeadRadius / 0.6f; b.NeckLength = f.Neck; b.Legs = 2;
        }
        else
        {
            b.Size = 1.3f * k; b.Width = 0.72f * k; b.Height = 0.46f * k; b.LegLength = legs == 4 ? 0.9f * k : 0.75f * k; b.LegThickness = 0.14f * k; b.HeadSize = 0.32f * k; b.NeckLength = 0.1f; b.Legs = legs;
            b.LegStyle = legs == 6 ? LegStyle.Splayed : LegStyle.Upright;
        }
        Stats st = s.Stats;
        st.MaxHealth = 180f * k;
        st.WalkSpeed = 1.4f;
        st.RunSpeed = legs == 4 ? 5f : 3.2f;
        st.Damage = 16f * k;
        st.AttackRange = 1.6f;
        st.Perception = 45f;
        st.FleeRadius = 0f;
        st.Radius = legs == 2 ? 0.4f : 0.7f * k;
        st.Retaliates = true;
        st.Hunts = true;
        s.Attack = new AttackDef { Name = "Strike", Style = AttackStyle.Lunge, Telegraph = 1.2f, Sound = "servo" };
        s.Drops.Meat = 0;
        return s;
    }

    static Figure Automaton(Rng rng, float k, Color hull)
    {
        var f = new Figure { Sex = Sex.Male, Stage = LifeStage.Adult, Role = PersonRole.Guard, Occupancy = Occupancy.Feral, Height = 1.9f * k, Build = 0.3f, Dress = "frame" };
        f.Skin = hull;
        f.Cloth = hull;
        f.Trim = hull.Darkened(0.4f);
        f.Leather = hull.Darkened(0.5f);
        f.Metal = hull.Lightened(0.15f);
        f.Hair = hull;
        f.HairStyle = "bald";
        f.Measure();
        return f;
    }

    // ---- Bodies ---------------------------------------------------------------------------------

    /// <summary>
    /// A walker's hull, for the legged rig: every piece named as the rig names them, but boxes and
    /// struts, seams and a sensor where a face would be.
    /// </summary>
    public static RigLayout BuildLegged(Species s)
    {
        var layout = new RigLayout();
        Proportions b = s.Body;
        float L = b.Size, W = b.Width, H = b.Height, hs = b.HeadSize;
        Color hull = s.Colors.Base, dark = s.Colors.Accent, under = s.Colors.Belly;
        var body = new Piece("body");
        Hard(body, new Vector3(W * 0.92f, H * 0.8f, L * 0.82f), Vector3.Zero, hull);
        Hard(body, new Vector3(W * 0.7f, H * 0.32f, L * 0.56f), new Vector3(0f, H * 0.52f, L * 0.06f), under);
        Hard(body, new Vector3(W * 1.02f, H * 0.14f, L * 0.9f), new Vector3(0f, -H * 0.1f, 0f), dark);
        for (int side = -1; side <= 1; side += 2)
            body.Add(Shape.Box, new Vector3(0.02f, H * 0.08f, L * 0.5f), new Vector3(side * W * 0.47f, H * 0.16f, 0f), s.Colors.Glow, 0f, 0.8f, null, 0, Mat.Carapace);
        layout.Add(body);
        var head = new Piece("head");
        Hard(head, new Vector3(hs * 0.9f, hs * 0.6f, hs * 0.9f), new Vector3(0f, 0f, -hs * 0.2f), hull);
        head.Add(Shape.Sphere, new Vector3(hs * 0.4f, hs * 0.26f, hs * 0.2f), new Vector3(0f, hs * 0.04f, -hs * 0.66f), s.Colors.Glow, 0f, 1f, null, 8, Mat.Eye);
        layout.Add(head);
        float upper = b.LegLength * 0.52f, lower = b.LegLength * 0.56f, t = b.LegThickness;
        for (int i = 0; i < b.Legs; i++)
        {
            var up = new Piece($"leg{i}u");
            Hard(up, new Vector3(t, upper, t * 1.3f), new Vector3(0f, upper * 0.5f, 0f), dark);
            up.Add(Shape.Cylinder, new Vector3(t * 1.4f, t * 1.5f, t * 1.4f), Vector3.Zero, hull, 0f, 0f, new Basis(Vector3.Back, MathF.PI * 0.5f), 8, Mat.Carapace);
            layout.Add(up);
            var lo = new Piece($"leg{i}l");
            Hard(lo, new Vector3(t * 0.7f, lower, t * 0.8f), new Vector3(0f, lower * 0.5f, 0f), hull);
            lo.Add(Shape.Cylinder, new Vector3(t * 1.2f, t * 1.2f, t * 1.2f), Vector3.Zero, dark, 0f, 0f, new Basis(Vector3.Back, MathF.PI * 0.5f), 8, Mat.Carapace);
            layout.Add(lo);
            var foot = new Piece($"leg{i}f") { Shadow = false };
            Hard(foot, new Vector3(t * 1.1f, t * 0.4f, t * 1.8f), new Vector3(0f, t * 0.2f, -t * 0.3f), dark);
            layout.Add(foot);
        }
        return layout;
    }

    static void Hard(Piece p, Vector3 size, Vector3 at, Color c) => p.Add(Shape.Box, size, at, c, 0f, 0f, null, 0, Mat.Carapace);

    /// <summary>A machine on two legs: the people's pieces, in plate and strut.</summary>
    public static RigLayout BuildAutomaton(Species s)
    {
        Figure f = s.Figure;
        var layout = new RigLayout();
        Color hull = f.Cloth, dark = f.Trim, glow = s.Colors.Glow;
        float T = f.Torso, d = f.ChestD;
        var body = new Piece("body");
        Hard(body, new Vector3(f.ShoulderW * 1.9f, T * 0.5f, d * 2.1f), new Vector3(0f, T * 0.72f, 0f), hull);
        Hard(body, new Vector3(f.WaistW * 1.2f, T * 0.32f, d * 1.1f), new Vector3(0f, T * 0.34f, 0f), dark);
        Hard(body, new Vector3(f.HipW * 1.9f, T * 0.2f, d * 1.5f), new Vector3(0f, T * 0.08f, 0f), hull);
        body.Add(Shape.Sphere, new Vector3(0.1f, 0.1f, 0.05f), new Vector3(0f, T * 0.76f, -d * 1.06f), glow, 0f, 1f, null, 8, Mat.Eye);
        layout.Add(body);
        var head = new Piece("head");
        float R = f.HeadRadius;
        Hard(head, new Vector3(0.05f, f.Neck * 1.4f, 0.05f), new Vector3(0f, f.Neck * 0.4f, 0f), dark);
        Hard(head, new Vector3(R * 1.8f, R * 1.5f, R * 1.9f), new Vector3(0f, f.Neck + R * 0.8f, 0f), hull);
        head.Add(Shape.Box, new Vector3(R * 1.4f, R * 0.22f, 0.02f), new Vector3(0f, f.Neck + R * 0.9f, -R * 0.96f), glow, 0f, 1f, null, 0, Mat.Eye);
        layout.Add(head);
        for (int side = 0; side < 2; side++)
        {
            var au = new Piece($"arm{side}u");
            Hard(au, new Vector3(0.08f, f.UpperArm, 0.09f), Vector3.Up * f.UpperArm * 0.5f, dark);
            au.Add(Shape.Sphere, Vector3.One * 0.15f, Vector3.Zero, hull, 0f, 0f, null, 8, Mat.Carapace);
            var al = new Piece($"arm{side}l");
            Hard(al, new Vector3(0.07f, f.Forearm, 0.08f), Vector3.Up * f.Forearm * 0.5f, hull);
            Hard(al, new Vector3(0.1f, 0.1f, 0.05f), Vector3.Up * f.Forearm, dark);
            var lu = new Piece($"leg{side}u");
            Hard(lu, new Vector3(0.11f, f.UpperLeg, 0.13f), Vector3.Up * f.UpperLeg * 0.5f, dark);
            var ll = new Piece($"leg{side}l");
            Hard(ll, new Vector3(0.1f, f.LowerLeg, 0.12f), Vector3.Up * f.LowerLeg * 0.5f, hull);
            ll.Add(Shape.Sphere, Vector3.One * 0.14f, Vector3.Zero, dark, 0f, 0f, null, 8, Mat.Carapace);
            var ft = new Piece($"leg{side}f") { Shadow = false };
            Hard(ft, new Vector3(0.12f, f.Ankle, 0.3f), new Vector3(0f, -f.Ankle * 0.5f, -0.06f), dark);
            layout.Add(au);
            layout.Add(al);
            layout.Add(lu);
            layout.Add(ll);
            layout.Add(ft);
        }
        return layout;
    }
}
