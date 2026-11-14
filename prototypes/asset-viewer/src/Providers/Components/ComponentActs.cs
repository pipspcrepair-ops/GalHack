using System;
using System.Linq;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Player;
using Slice.Vessels;
using AssetViewer.Providers.Parts;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Components;

/// <summary>
/// What a component does, as the game does it, played with the game's own sleeper figure: a chair is
/// walked up to and sat in, a door opens for whoever comes through it, a ladder is climbed, a stair
/// walked up and down, landing legs fold and unfold. Anything else has the figure standing at its
/// side for scale. Found by function, never by name. Everything is placed in the block's own frame
/// (its floor, its front at -Z), so every one of the 24 orientations plays the same.
/// </summary>
public static class ComponentActs
{
    const float WalkSpeed = 1.4f;

    /// <summary>A figure in the structure's frame, helmet up so the face shows.</summary>
    sealed class Person
    {
        readonly Node3D _node = CharacterModel.Build();

        public Person(StructureAsset a)
        {
            a.Holder.AddChild(_node);
            CharacterModel.SetHelmet(_node, true, 1f);
        }

        public void Pose(Transform3D at, in SleeperMotion m, float dt)
        {
            _node.Transform = at;
            CharacterModel.Drive(_node, m, null, 0f, dt);
        }
    }

    /// <summary>The block's floor frame: its bottom middle, turned as the block is (local -Z is its front).</summary>
    static Transform3D Frame(StructureAsset a, Int3 anchor, in Block b, BlockType t)
    {
        System.Numerics.Vector3 c = StructureBridge.BlockCenter(a.Structure, anchor, b);
        Basis basis = VesselRoles.BasisOf(a.Structure, anchor);
        return new Transform3D(basis, new Vector3(c.X, c.Y, c.Z) - basis.Y * (0.5f * t.Size.Y * a.Structure.Grid.CellSize));
    }

    static Transform3D At(Transform3D frame, Vector3 local, float yaw) => frame * new Transform3D(new Basis(Vector3.Up, yaw), local);

    public static void Add(StructureAsset a, bool person)
    {
        Structure s = a.Structure;
        var (anchor, b) = s.Blocks().First();
        BlockType t = s.TypeOf(b);
        Transform3D frame = Frame(a, anchor, b, t);
        switch (t.Function)
        {
            case "gear": Gear(a, t, frame); break;
            case "door" when person: Door(a, anchor, t, frame); break;
            case "ladder" when person: Ladder(a, frame); break;
            case "stairs" when person: Stairs(a, frame, s.Count); break;
            case "chair" when person: Chair(a, anchor, b, t); break;
            default: Standing(a, person); break;
        }
        // The viewer opens on what the component does, not on it standing still.
        a.Lead(t.Function switch
        {
            "gear" => "Legs fold and unfold", "door" => "Walk through", "ladder" => "Climb up and down", "stairs" => "Walk up and down",
            "chair" => "Sit down and get up", "thruster" => "Thrust", "light" => "Lamps cycle modes", _ => "Person standing by",
        });
    }

    /// <summary>The figure at the structure's side, facing it (a person for scale), breathing.</summary>
    public static void Standing(StructureAsset a, bool person)
    {
        if (!person) return;
        var (min, max) = a.Structure.Bounds();
        float cell = a.Structure.Grid.CellSize;
        var low = new Vector3(min.X - 0.5f, min.Y - 0.5f, min.Z - 0.5f) * cell;
        var high = new Vector3(max.X + 0.5f, max.Y + 0.5f, max.Z + 0.5f) * cell;
        var at = new Transform3D(new Basis(Vector3.Up, -Mathf.Pi * 0.5f), new Vector3(low.X - 0.9f, low.Y, (low.Z + high.Z) * 0.5f));
        var figure = new Person(a);
        var still = new SleeperMotion { Grounded = true };
        figure.Pose(at, still, 0f);
        a.AddAnimation("Person standing by", null, dt => figure.Pose(at, still, dt));
        a.Include(at.Origin + Vector3.Up * 0.9f);
    }

    // ---- Chair: walk up, turn, sit, get up, walk away -----------------------------------------

    static void Chair(StructureAsset a, Int3 anchor, in Block b, BlockType t)
    {
        System.Numerics.Vector3 seat = StructureBridge.SeatPoint(a.Structure, anchor, b);
        float drop = Vessel.FloorUnder(t, a.Structure.Grid.CellSize);
        Basis basis = VesselRoles.BasisOf(a.Structure, anchor);
        // The figure's floor under the seat, in the chair's own frame from there.
        var seatFrame = new Transform3D(basis, new Vector3(seat.X, seat.Y, seat.Z) - basis.Y * drop);
        var figure = new Person(a);
        float reach = Mathf.Max(1.4f, t.Size.Z * a.Structure.Grid.CellSize * 0.5f + 1f);
        const float Walk = 1.1f, Turn = 0.7f, Sit = 3.2f, Stand = 1.2f;
        float total = Walk + Turn + Sit + Stand + Walk, clock = Walk + Turn + 1f;

        void Step(float dt)
        {
            clock = (clock + dt) % total;
            // In front of the chair (local -Z) facing it, then round to face the way the chair does.
            var m = new SleeperMotion { Grounded = true, SeatDrop = drop };
            float z = 0f, yaw = 0f;
            if (clock < Walk) { z = -reach * (1f - clock / Walk); yaw = Mathf.Pi; m.Speed = WalkSpeed; }
            else if (clock < Walk + Turn) yaw = Mathf.Pi * (1f - Mathf.SmoothStep(0f, 1f, (clock - Walk) / Turn));
            else if (clock < Walk + Turn + Sit) m.Seated = clock > Walk + Turn + 0.2f;
            else if (clock >= Walk + Turn + Sit + Stand) { z = -reach * ((clock - Walk - Turn - Sit - Stand) / Walk); m.Speed = WalkSpeed; }
            figure.Pose(At(seatFrame, new Vector3(0f, 0f, z), yaw), m, dt);
        }

        // It starts seated, as the game shows a chair in use.
        Step(0f);
        a.AddAnimation("Sit down and get up", () => clock = 0f, Step);
        a.Include(seatFrame * new Vector3(0f, 0.9f, -reach));
    }

    // ---- Door: opens for whoever walks through ------------------------------------------------

    static void Door(StructureAsset a, Int3 anchor, BlockType t, Transform3D frame)
    {
        Structure s = a.Structure;
        var figure = new Person(a);
        float reach = 2.2f + t.Size.X * 0.25f, pause = 0.8f, leg = 2f * reach / WalkSpeed, clock = 0f;
        a.AddAnimation("Walk through", () => clock = 0f, dt =>
        {
            clock = (clock + dt) % (2f * (leg + pause));
            // There through the door, a pause, and back.
            bool back = clock >= leg + pause;
            float run = Mathf.Clamp((back ? clock - leg - pause : clock) / leg, 0f, 1f);
            float z = back ? Mathf.Lerp(reach, -reach, run) : Mathf.Lerp(-reach, reach, run);
            Working.SetFlag(s, anchor, BlockFlags.Open, Mathf.Abs(z) < 1.2f);
            figure.Pose(At(frame, new Vector3(0f, 0f, z), back ? 0f : Mathf.Pi), new SleeperMotion { Grounded = true, Speed = run is > 0f and < 1f ? WalkSpeed : 0f }, dt);
        }, () => Working.SetFlag(s, anchor, BlockFlags.Open, false));
        a.Include(frame * new Vector3(0f, 0.9f, -reach));
        a.Include(frame * new Vector3(0f, 0.9f, reach));
    }

    // ---- Ladder and stairs --------------------------------------------------------------------

    static void Ladder(StructureAsset a, Transform3D frame)
    {
        var figure = new Person(a);
        float rungs = a.Structure.Count * a.Structure.Grid.CellSize - 0.3f, pause = 0.8f, climb = 0.9f, span = rungs / climb, clock = 0f;
        a.AddAnimation("Climb up and down", () => clock = 0f, dt =>
        {
            clock = (clock + dt) % (2f * (span + pause));
            // Up, a breath at the top, down, a breath at the bottom; the rungs are on the +Z side, so it faces +Z.
            bool down = clock >= span + pause;
            float run = Mathf.Clamp((down ? clock - span - pause : clock) / span, 0f, 1f);
            float height = rungs * (down ? 1f - run : run), going = run is > 0f and < 1f ? (down ? -1f : 1f) : 0f;
            figure.Pose(At(frame, new Vector3(0f, height, 0.08f), Mathf.Pi), new SleeperMotion { Ladder = true, Climb = climb * going }, dt);
        });
        a.Include(frame * new Vector3(0f, rungs + 1.8f, 0f));
    }

    static void Stairs(StructureAsset a, Transform3D frame, int flights)
    {
        var figure = new Person(a);
        // Each flight rises a metre over a metre in two half-metre treads; the walk goes up the flights and down again.
        float lead = 1.6f, length = flights + lead + 0.6f, leg = length / WalkSpeed, clock = 0f;
        a.AddAnimation("Walk up and down", () => clock = 0f, dt =>
        {
            clock = (clock + dt) % (2f * leg);
            bool down = clock >= leg;
            float along = Mathf.Clamp((down ? clock - leg : clock) / leg, 0f, 1f) * length;
            float z = (down ? length - along : along) - lead - 0.5f;
            figure.Pose(At(frame, new Vector3(0f, TreadHeight(z, flights), z), down ? 0f : Mathf.Pi), new SleeperMotion { Grounded = true, Speed = WalkSpeed }, dt);
        });
        a.Include(frame * new Vector3(0f, flights + 1.8f, flights));
        a.Include(frame * new Vector3(0f, 0.9f, -lead));
    }

    /// <summary>
    /// The floor under a walker at z along a flight (the frame's origin is the bottom middle of the
    /// first block): each block has a tread half a metre up from its front face and another at its middle.
    /// </summary>
    static float TreadHeight(float z, int flights)
    {
        float h = 0f;
        for (int i = 0; i < flights; i++)
            foreach (float lip in new[] { i - 0.5f, i })
                h += 0.5f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp((z - lip + 0.15f) / 0.3f, 0f, 1f));
        return h;
    }

    // ---- Landing gear: the leg telescopes down to the ground ------------------------------------

    static void Gear(StructureAsset a, BlockType t, Transform3D frame)
    {
        // The pad hangs on its leg: raise the structure by the leg's reach so the leg meets the ground when down.
        a.Lift(t.LegLength);
        var strut = new MeshInstance3D { Name = "Strut", Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 1f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("2b2d42"), Roughness = 0.5f, Metallic = 0.4f } };
        var foot = new MeshInstance3D { Name = "Foot", Mesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.22f, Height = 0.08f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("f4a100"), Roughness = 0.7f } };
        a.Holder.AddChild(strut);
        a.Holder.AddChild(foot);
        Vector3 hip = frame.Origin;
        float extension = 1f, clock = 0f;
        void Draw()
        {
            float reach = Mathf.Max(0.05f, t.LegLength * extension);
            strut.Position = hip + Vector3.Down * (reach * 0.5f);
            strut.Scale = new Vector3(1f, reach, 1f);
            foot.Position = hip + Vector3.Down * reach;
            strut.Visible = foot.Visible = extension > 0.05f;
        }
        Draw();
        a.AddAnimation("Legs fold and unfold", () => clock = 0f, dt =>
        {
            clock += dt;
            extension = 0.5f + 0.5f * Mathf.Cos(clock * 1.2f);
            Draw();
        }, () => { extension = 1f; Draw(); });
        a.Include(hip + Vector3.Down * t.LegLength);
    }
}
