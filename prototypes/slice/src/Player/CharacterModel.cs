using System;
using System.Runtime.CompilerServices;
using Godot;
using Wildlife;

namespace Slice.Player;

/// <summary>What the castaway's body is doing, for its figure: the walker's own state, read each frame.</summary>
public struct CastawayMotion
{
    /// <summary>Over the ground, m/s, and up (positive) or down, m/s.</summary>
    public float Speed, Climb;
    public bool Grounded, Crouched, Jetting, Swimming, Floating, Ladder;
    /// <summary>On a hover bike's seat; in a vessel's chair.</summary>
    public bool Riding, Seated;
    /// <summary>In a chair: how far its seat stands above the floor the feet rest on, m (the figure's origin is on that floor).</summary>
    public float SeatDrop;
    /// <summary>The view's pitch, radians (pointing).</summary>
    public float Pitch;
}

/// <summary>
/// The castaway's body as seen from outside: a suited figure 1.8 m tall in the slice's 1970s
/// paperback livery (orange suit, cream plates, a dark visor, a backpack with its jetpack nozzles).
/// It runs on the people's own rig (<see cref="HumanRig"/>), so it walks, runs, crouches and sits
/// as the townsfolk do; only the suit's pieces are its own. It stands in the inventory's paperdoll,
/// and in third person it turns with the view and moves as the walker does. Procedural, no assets.
/// </summary>
public static class CharacterModel
{
    /// <summary>The castaway's proportions, as a person's: a grown man 1.8 m tall in a suit.</summary>
    static readonly Lazy<Species> Castaway = new(() =>
    {
        var f = new Figure
        {
            Sex = Sex.Male, Stage = LifeStage.Adult, Age = 35f, Role = PersonRole.Worker, Folk = new Folk(),
            Height = 1.8f, Build = 0.5f, Padding = 1.15f, Dress = "suit",
            Skin = new Color("c99a74"), Hair = new Color("3a2a1e"), Cloth = new Color("e85d04"), Trim = new Color("ece3c8"),
            Leather = new Color("2b2d42"), Metal = new Color("ece3c8"),
        };
        f.Measure();
        var s = new Species { Id = "castaway", Seed = 1, Plan = BodyPlan.Walker, Form = "person", Rig = RigKind.Humanoid, Figure = f, Name = "human", CommonName = "castaway" };
        foreach (string slot in FaunaTables.Slots) s.Parts[slot] = "none";
        s.Body.Size = f.Torso;
        s.Body.Width = f.ShoulderW * 2f;
        s.Body.Height = f.Torso;
        s.Body.LegLength = f.LegLength;
        s.Body.HeadSize = f.HeadRadius / 0.6f;
        s.Body.NeckLength = f.Neck;
        s.Body.Legs = 2;
        s.Colors.Base = f.Skin;
        s.Colors.Accent = f.Cloth;
        return s;
    });

    /// <summary>One built figure: its rig and the suit's piece for each of the rig's pieces it draws.</summary>
    sealed class Body
    {
        public HumanRig Rig;
        public Node3D[] Pieces;
        public int Torso, ArmU0, ArmU1, ArmL0, ArmL1, LegU0, LegU1, LegL0, LegL1, Foot0, Foot1;
        public float Walked, Crouch, Seat, Air, Flown, Clock;
    }

    static readonly ConditionalWeakTable<Node3D, Body> Bodies = new();

    public static Node3D Build()
    {
        var root = new Node3D { Name = "Castaway" };
        Species sp = Castaway.Value;
        Figure f = sp.Figure;
        var rig = new HumanRig(sp);
        var b = new Body { Rig = rig, Pieces = new Node3D[rig.Layout.Pieces.Count] };
        var suit = new StandardMaterial3D { AlbedoColor = new Color("e85d04"), Roughness = 0.75f };
        var plate = new StandardMaterial3D { AlbedoColor = new Color("ece3c8"), Roughness = 0.45f, Metallic = 0.2f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("2b2d42"), Roughness = 0.7f };
        var visor = new StandardMaterial3D { AlbedoColor = new Color("14213d"), Roughness = 0.1f, Metallic = 0.6f, EmissionEnabled = true, Emission = new Color("2ec4b6"), EmissionEnergyMultiplier = 0.25f };
        var glow = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color("ffb703") };

        // Each piece's frame is the rig's: the body's origin at the hips (Y up the spine, -Z ahead),
        // the head's at the neck, a limb's at its upper joint with Y down the bone and +Z ahead, a
        // foot's at the ankle (-Z ahead).
        Node3D Piece(string name)
        {
            int i = rig.Layout.Pieces.FindIndex(p => p.Name == name);
            if (i < 0) return null;
            var n = new Node3D { Name = name };
            root.AddChild(n);
            b.Pieces[i] = n;
            return n;
        }
        static void Part(Node3D on, Mesh m, Material mat, Vector3 at, Vector3 deg = default) =>
            on?.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = at, RotationDegrees = deg });

        float chest = f.Torso * 0.9f, sh = f.ShoulderW * 0.94f;
        Node3D body = Piece("body");
        Part(body, new BoxMesh { Size = new Vector3(f.HipW * 2.1f, 0.2f, 0.24f) }, suit, new Vector3(0f, 0.02f, 0f));
        Part(body, new CapsuleMesh { Radius = 0.19f, Height = f.Torso + 0.1f }, suit, new Vector3(0f, f.Torso * 0.5f, 0f));
        Part(body, new BoxMesh { Size = new Vector3(0.32f, 0.26f, 0.06f) }, plate, new Vector3(0f, chest - 0.2f, -0.17f));
        Part(body, new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.02f) }, glow, new Vector3(0.09f, chest - 0.14f, -0.205f));
        Part(body, new BoxMesh { Size = new Vector3(0.4f, 0.07f, 0.3f) }, dark, new Vector3(0f, 0.05f, 0f));
        Part(body, new BoxMesh { Size = new Vector3(0.34f, 0.42f, 0.18f) }, plate, new Vector3(0f, chest - 0.27f, 0.2f));
        foreach (float x in new[] { -0.09f, 0.09f })
            Part(body, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 0.12f }, dark, new Vector3(x, chest - 0.52f, 0.24f));
        foreach (float x in new[] { -sh, sh })
            Part(body, new SphereMesh { Radius = 0.09f, Height = 0.18f }, plate, new Vector3(x, chest, 0f));

        Node3D head = Piece("head");
        float crown = f.Neck + f.HeadRadius * 0.7f;
        Part(head, new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.11f, Height = 0.08f }, dark, new Vector3(0f, 0.02f, 0f));
        Part(head, new SphereMesh { Radius = 0.16f, Height = 0.32f }, plate, new Vector3(0f, crown, 0f));
        Part(head, new SphereMesh { Radius = 0.125f, Height = 0.2f }, visor, new Vector3(0f, crown, -0.065f));
        Part(head, new BoxMesh { Size = new Vector3(0.04f, 0.1f, 0.04f) }, dark, new Vector3(0.14f, crown + 0.12f, 0.05f));

        for (int side = 0; side < 2; side++)
        {
            Node3D au = Piece($"arm{side}u"), al = Piece($"arm{side}l");
            Part(au, new CapsuleMesh { Radius = 0.06f, Height = f.UpperArm + 0.1f }, suit, new Vector3(0f, f.UpperArm * 0.5f, 0f));
            Part(al, new CapsuleMesh { Radius = 0.055f, Height = f.Forearm + 0.06f }, suit, new Vector3(0f, f.Forearm * 0.5f, 0f));
            Part(al, new BoxMesh { Size = new Vector3(0.08f, 0.1f, 0.08f) }, dark, new Vector3(0f, f.Forearm + 0.04f, 0f));
            if (side == 1 && al != null) al.AddChild(new Node3D { Name = "HandR", Position = new Vector3(0f, f.Forearm + 0.04f, 0f) });
            Node3D lu = Piece($"leg{side}u"), ll = Piece($"leg{side}l"), foot = Piece($"leg{side}f");
            Part(lu, new CapsuleMesh { Radius = 0.08f, Height = f.UpperLeg + 0.1f }, suit, new Vector3(0f, f.UpperLeg * 0.5f, 0f));
            Part(ll, new CapsuleMesh { Radius = 0.07f, Height = f.LowerLeg + 0.06f }, suit, new Vector3(0f, f.LowerLeg * 0.5f, 0f));
            Part(ll, new BoxMesh { Size = new Vector3(0.11f, 0.14f, 0.05f) }, plate, new Vector3(0f, 0.04f, 0.07f));
            Part(foot, new BoxMesh { Size = new Vector3(0.13f, 0.11f, 0.26f) }, dark, new Vector3(0f, -f.Ankle + 0.055f, -0.04f));
        }
        var names = rig.Layout.Pieces;
        int Find(string n) => names.FindIndex(p => p.Name == n);
        b.Torso = Find("body");
        b.ArmU0 = Find("arm0u"); b.ArmU1 = Find("arm1u"); b.ArmL0 = Find("arm0l"); b.ArmL1 = Find("arm1l");
        b.LegU0 = Find("leg0u"); b.LegU1 = Find("leg1u"); b.LegL0 = Find("leg0l"); b.LegL1 = Find("leg1l");
        b.Foot0 = Find("leg0f"); b.Foot1 = Find("leg1f");
        Bodies.AddOrUpdate(root, b);
        OwnLayer(root, BodyLayer);
        OwnLayer(head, HeadLayer);
        // Standing at rest until it is driven.
        Drive(root, new CastawayMotion { Grounded = true }, null, 99f, 0f);
        return root;
    }

    /// <summary>
    /// The render layers (1-based, as a camera's cull mask counts them) the figure is drawn on: a
    /// first-person camera leaves out the body on foot, where the hands stand in for it, and only
    /// the head when seated, where the eye is inside the helmet and the view down shows the lap.
    /// </summary>
    public const int BodyLayer = 2, HeadLayer = 3;

    static void OwnLayer(Node n, int layer)
    {
        if (n is MeshInstance3D m) m.Layers = 1u << (layer - 1);
        foreach (Node c in n.GetChildren()) OwnLayer(c, layer);
    }

    /// <summary>
    /// How far the hips sit above the seat, m: the suit's pelvis reaches 0.08 m under them and the
    /// thighs' 0.08 m round sits under a hip joint a little lower, so the seat meets both about here.
    /// </summary>
    public const float SitBones = 0.09f;

    /// <summary>How long a gesture plays on the body, seconds.</summary>
    public const float GestureSeconds = 1.7f;

    /// <summary>The castaway's gestures as the people's body language: the same rig makes both.</summary>
    static BodyAct Act(PlayerGesture? g) => g switch
    {
        PlayerGesture.Wave => BodyAct.Greet,
        PlayerGesture.Beckon => BodyAct.Beckon,
        PlayerGesture.HandsUp => BodyAct.HandsUp,
        PlayerGesture.Stop => BodyAct.Warn,
        PlayerGesture.Point => BodyAct.Point,
        PlayerGesture.Threaten => BodyAct.Taunt,
        PlayerGesture.Thanks => BodyAct.Thanks,
        _ => BodyAct.None,
    };

    /// <summary>A standing figure making a gesture <paramref name="t"/> seconds after it was made.</summary>
    public static void Pose(Node3D figure, PlayerGesture? gesture, float t) => Drive(figure, new CastawayMotion { Grounded = true }, gesture, t, 0f);

    /// <summary>
    /// Poses the figure for what the walker is doing: walking and running by its speed, crouched,
    /// tucked in a jump and straighter in a fall, hanging under the jetpack, laid out swimming, hand
    /// over hand on a ladder, astride the hover bike with its hands on the bars, in a chair. A
    /// gesture <paramref name="t"/> seconds old plays on its arms while it lasts. The figure's own
    /// frame is the one it stands in (feet at the origin, -Z the way it faces): the caller places it.
    /// </summary>
    public static void Drive(Node3D figure, in CastawayMotion m, PlayerGesture? gesture, float t, float dt)
    {
        if (figure == null || !Bodies.TryGetValue(figure, out Body b)) return;
        Figure f = b.Rig.Species.Figure;
        b.Clock += dt;
        bool air = !m.Grounded && !m.Ladder && !m.Swimming && !m.Riding && !m.Seated;
        float speed = m.Riding || m.Seated ? 0f : m.Ladder ? MathF.Abs(m.Climb) * 1.6f : m.Swimming ? MathF.Max(m.Speed, 1.1f) : air ? 0f : m.Speed;
        b.Walked += speed * dt;
        bool eased = dt > 0f;
        float k = eased ? 1f - MathF.Exp(-10f * dt) : 1f;
        // The legs come up under a jump and hang under the jetpack; they straighten as it falls.
        float tuck = !air ? 0f : m.Jetting || m.Floating ? 0.3f : m.Climb > 0.5f ? 0.55f : m.Climb < -3f ? 0.12f : 0.3f;
        b.Air = Mathf.Lerp(b.Air, tuck, k);
        b.Flown = Mathf.Lerp(b.Flown, air ? 1f : 0f, k);
        b.Crouch = Mathf.Lerp(b.Crouch, m.Crouched && m.Grounded ? 1f : m.Riding ? 0.6f : 0f, k);
        float sit = m.Riding || m.Seated ? 1f : 0f;
        b.Seat = eased ? Mathf.MoveToward(b.Seat, sit, 4f * dt) : sit;
        float w = gesture == null || t < 0f || t > GestureSeconds ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Min(t / 0.25f, (GestureSeconds - t) / 0.35f));
        BodyAct act = w > 0f ? Act(gesture) : BodyAct.None;
        // In the air the hips stay where they were and the feet come up to them.
        float lift = f.LegLength * 0.42f * b.Air;
        // In a chair the hips go onto its seat (the rig sits them at the lower leg's height over the feet), the feet on its floor.
        if (m.Seated) lift += (m.SeatDrop + SitBones - (f.LowerLeg + 0.05f)) * b.Seat;
        Vector3 aim = new Vector3(0f, 1.45f, 0f) + new Vector3(0f, MathF.Sin(m.Pitch), -MathF.Cos(m.Pitch)) * 5f;
        b.Rig.Drive(new Gesture
        {
            Feet = Vector3.Up * lift,
            Speed = speed,
            Walked = b.Walked,
            Crouch = MathF.Max(b.Crouch, b.Air),
            Sit = b.Seat,
            Reach = m.Ladder ? 0.85f : 0f,
            Act = act,
            ActAmount = w,
            ActAt = aim,
            Clock = b.Clock,
            Style = 0,
        });
        // The hands and feet that hold on to something: the bike's bars and its footrests, a ladder's rungs.
        if (m.Riding && b.Seat > 0.5f)
        {
            Grip(b, f, new Vector3(-0.36f, 0.91f, -0.62f), new Vector3(0.36f, 0.91f, -0.62f));
            Stand(b, f, new Vector3(-0.34f, 0.27f, -0.4f), new Vector3(0.34f, 0.27f, -0.4f));
        }
        else if (m.Seated && b.Seat > 0.5f && b.Foot0 >= 0 && b.Foot1 >= 0)
        {
            Vector3 l = b.Rig.Pose[b.Foot0].Origin, r = b.Rig.Pose[b.Foot1].Origin;
            Stand(b, f, l with { Y = 0f }, r with { Y = 0f });
        }
        else if (m.Ladder)
        {
            float c = MathF.Tau * b.Walked / (f.LegLength * 1.36f);
            Grip(b, f, new Vector3(-0.2f, 1.62f + 0.16f * MathF.Sin(c), -0.38f), new Vector3(0.2f, 1.62f - 0.16f * MathF.Sin(c), -0.38f));
        }
        else if (b.Flown > 0.01f && b.Foot0 >= 0 && b.Foot1 >= 0)
        {
            // Off the ground the legs stop walking: one knee up through a jump, both trailing under the
            // jetpack, loose and apart in a fall.
            (Vector3 l, Vector3 r) = m.Jetting || m.Floating ? (new Vector3(-0.13f, 0.24f, 0.2f), new Vector3(0.14f, 0.34f, 0.12f))
                : m.Climb > -3f ? (new Vector3(-0.12f, 0.42f, -0.22f), new Vector3(0.13f, 0.16f, 0.22f))
                : (new Vector3(-0.2f, 0.08f, -0.06f), new Vector3(0.2f, 0.14f, 0.1f));
            Vector3 down = Vector3.Up * f.Ankle;
            Stand(b, f, (b.Rig.Pose[b.Foot0].Origin - down).Lerp(l, b.Flown), (b.Rig.Pose[b.Foot1].Origin - down).Lerp(r, b.Flown));
        }
        for (int i = 0; i < b.Pieces.Length; i++)
            if (b.Pieces[i] != null) b.Pieces[i].Transform = b.Rig.Pose[i];
    }

    /// <summary>Re-reaches both arms from their shoulders to two hand holds, in the figure's frame.</summary>
    static void Grip(Body b, Figure f, Vector3 left, Vector3 right)
    {
        Transform3D[] p = b.Rig.Pose;
        Basis torso = p[b.Torso].Basis;
        for (int i = 0; i < 2; i++)
        {
            int u = i == 0 ? b.ArmU0 : b.ArmU1, l = i == 0 ? b.ArmL0 : b.ArmL1;
            if (u < 0 || l < 0) continue;
            float side = i == 0 ? -1f : 1f;
            Vector3 shoulder = p[u].Origin;
            Vector3 pole = (torso.Z * 0.4f + Vector3.Down * 0.6f + torso.X * side * 0.5f).Normalized();
            (Vector3 elbow, Vector3 wrist) = LeggedRig.SolveIK(shoulder, i == 0 ? left : right, f.UpperArmOf(i), f.ForearmOf(i), pole);
            p[u] = Geo.Segment(shoulder, elbow, torso.X);
            p[l] = Geo.Segment(elbow, wrist, torso.X);
        }
    }

    /// <summary>Re-reaches both legs from their hips to two footholds, in the figure's frame.</summary>
    static void Stand(Body b, Figure f, Vector3 left, Vector3 right)
    {
        Transform3D[] p = b.Rig.Pose;
        for (int i = 0; i < 2; i++)
        {
            int u = i == 0 ? b.LegU0 : b.LegU1, l = i == 0 ? b.LegL0 : b.LegL1, ft = i == 0 ? b.Foot0 : b.Foot1;
            if (u < 0 || l < 0) continue;
            float side = i == 0 ? -1f : 1f;
            Vector3 hip = p[u].Origin, foot = (i == 0 ? left : right) + Vector3.Up * f.Ankle;
            (Vector3 knee, Vector3 drawn) = LeggedRig.SolveIK(hip, foot, f.UpperLeg, f.LowerLeg, (Vector3.Forward + Vector3.Right * side * 0.5f).Normalized());
            p[u] = Geo.Segment(hip, knee, Vector3.Right);
            p[l] = Geo.Segment(knee, drawn, Vector3.Right);
            if (ft >= 0) p[ft] = new Transform3D(Basis.Identity, drawn);
        }
    }

    /// <summary>The figure's right hand, in the world (tests: is it raised?).</summary>
    public static Vector3 RightHand(Node3D figure) =>
        figure.FindChild("HandR", true, false) is Node3D h ? h.GlobalPosition : figure.GlobalPosition;

    /// <summary>The figure's two feet, in its own frame (tests: do they move?).</summary>
    public static (Vector3 left, Vector3 right) Feet(Node3D figure) =>
        figure != null && Bodies.TryGetValue(figure, out Body b) && b.Foot0 >= 0 && b.Foot1 >= 0 ? (b.Rig.Pose[b.Foot0].Origin, b.Rig.Pose[b.Foot1].Origin) : default;
}
