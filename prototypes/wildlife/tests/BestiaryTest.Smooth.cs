using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>The owner (17 Nov 2026): "lerp between animations and positions of NPCs". What is drawn never steps further than its speed allows.</summary>
public partial class BestiaryTest
{
    /// <summary>The worst of a run of frames: how far a body's drawn root jumped, how far it turned, how far a piece of it changed.</summary>
    sealed class Worst
    {
        public float Step, Turn, Pose; // m beyond what its speed allows, rad, piece shift / body size
        public string StepWho = "", PoseWho = "";
        public int Steps, Poses, Frames, TurnCount;
    }

    const float Frame = 1f / 240f, Tick = 1f / 60f, TurnLimit = 0.2f, PoseLimit = 0.5f;

    /// <summary>
    /// Creatures of every kind in a sim, near, mid and far from the eye, drawn at 240 frames a second off a 60 tick sim (a clock of
    /// its own, so a run is the same however fast the machine): drawn with the easing and without it, side by side. Neither the root
    /// nor a piece of the body may step further in a frame than what its speed allows, the heading turns smoothly, and a pose that
    /// changes with what the body is doing blends. People are drawn the same, with a set-back and a snapped heading.
    /// </summary>
    void DrawnBodiesGlide()
    {
        double clock = 0;
        DrawSmoother.Clock = () => clock;
        try
        {
            var terrain = new Terrain((x, z) => 1.5f * MathF.Sin(x * 0.05f) * MathF.Cos(z * 0.04f), 400f);
            var sim = new WildSim(_data, terrain, new NavGrid(400f, 3f));
            var you = new DummyTarget { Position = new Vector3(0f, 0f, 0f), Health = new Health(1e9f), Forward = Vector3.Forward };
            sim.Targets.Add(you);
            var kinds = new List<Species>();
            foreach (WorldConditions w in new[] { World(-20f), World(25f), World(45f), World(15f, mutagen: 0.9f) })
            {
                w.Coreward = w.Mutagen > 0f ? 1f : 0f;
                WorldLife life = Bestiary.Roll(_data, w);
                kinds.AddRange(life.Moving);
                kinds.Add(Robots.Roll(w, 0));
            }
            // Hoppers and the rest, people included, none of them a swarm or a thing that never moves.
            kinds = kinds.Where(s => s.Rig != RigKind.Swarm && (s.Figure != null || s.Mechanical || s.Rig is RigKind.Legged or RigKind.Hopper or RigKind.Chain or RigKind.Winged or RigKind.Finned)).ToList();
            float[] rings = { 14f, 40f, 90f, 150f };
            for (int i = 0; i < kinds.Count && i < 40; i++)
            {
                float a = i * 2.4f, r = rings[i % rings.Length] + _fresh.Range(0f, 8f);
                sim.Spawn(kinds[i], new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r));
            }
            var eased = new WildlifeView { Name = "Eased" };
            var raw = new WildlifeView { Name = "Raw" };
            AddChild(eased);
            AddChild(raw);
            eased.Bind(sim, null);
            raw.Bind(sim, null);
            var camera = Vector3.Zero;
            // Let every body be built (their meshes are made on a worker).
            for (int i = 0; i < 400 && (eased.ViewCount < sim.Creatures.Count(c => !c.Removed) || raw.ViewCount < eased.ViewCount); i++)
            {
                eased.Sync(camera);
                raw.Sync(camera);
                System.Threading.Thread.Sleep(5);
            }

            var worst = new Dictionary<bool, Worst> { [true] = new Worst(), [false] = new Worst() };
            var last = new Dictionary<(Creature, bool), (Vector3 At, float Yaw, Transform3D[] Local, Vector3 Sim, Vector4 Eased)>();
            for (int f = 0; f < 240 * 40; f++)
            {
                clock += Frame;
                if (f % 4 == 0)
                {
                    you.Position = new Vector3(MathF.Sin(f * 0.0007f) * 12f, 0f, MathF.Cos(f * 0.0005f) * 12f);
                    sim.Step(Tick);
                }
                DrawSmoother.Off = false;
                eased.Sync(camera);
                DrawSmoother.Off = true;
                raw.Sync(camera);
                DrawSmoother.Off = false;
                foreach (Creature c in sim.Creatures.ToList())
                {
                    if (c.Removed || c.Rig is SwarmRig) continue;
                    foreach (bool ease in new[] { true, false })
                    {
                        CreatureView v = (ease ? eased : raw).ViewOf(c);
                        if (v == null || !v.Visible || v.Shows < 1f) { last.Remove((c, ease)); continue; }
                        Vector3 at = v.DrawnRoot;
                        float yaw = v.DrawnYaw;
                        Transform3D back = v.PoseRoot.AffineInverse();
                        int n = c.Rig.Pose.Length;
                        var local = new Transform3D[n];
                        for (int i = 0; i < n; i++) local[i] = back * v.DrawnBone(i);
                        if (last.TryGetValue((c, ease), out var before))
                        {
                            Worst w = worst[ease];
                            w.Frames++;
                            // A real teleport (a respawn, over 8 m between ticks) is a cut, whatever is drawn.
                            bool teleported = (c.Position - before.Sim).Length() > 8f;
                            float step = (at - before.At).Length();
                            float allowed = 2f * MathF.Max(c.Velocity.Length(), (c.Position - before.Sim).Length() / Tick) * Frame + 0.03f;
                            float turn = MathF.Abs(Geo.WrapAngle(yaw - before.Yaw));
                            float shift = 0f;
                            int shiftBone = 0;
                            for (int i = 0; i < n && !teleported; i++) { float d = (local[i].Origin - before.Local[i].Origin).Length(); if (d > shift) { shift = d; shiftBone = i; } }
                            shift /= MathF.Max(0.3f, c.Species.Body.Size);
                            if (!teleported)
                            {
                                if (step - allowed > 0.08f) w.Steps++;
                                if (step - allowed > w.Step) { w.Step = step - allowed; w.StepWho = $"{c.Species.Name} {c.State} interval {c.RigInterval}"; }
                                if (turn > TurnLimit) { w.Turn = MathF.Max(w.Turn, turn); w.TurnCount++; }
                                if (shift > PoseLimit) w.Poses++;
                                if (shift > w.Pose) { w.Pose = shift; w.PoseWho = $"{c.Species.Name} {c.State} {c.Phase} dead {c.Dead} piece {c.Rig.Layout.Pieces[shiftBone].Name} interval {c.RigInterval}"; }
                            }
                        }
                        last[(c, ease)] = (at, yaw, local, c.Position, v.Eased);
                    }
                }
            }
            foreach (bool ease in new[] { false, true })
            {
                Worst w = worst[ease];
                GD.Print($"GLIDE {(ease ? "eased" : "raw  ")}: {w.Frames} body-frames; worst root step {w.Step * 100f:F1} cm over its speed ({w.StepWho}), {w.Steps} over; worst turn {w.Turn:F2} rad ({w.TurnCount} over); worst piece shift {w.Pose:F2} of its size ({w.PoseWho}), {w.Poses} over {PoseLimit}");
            }
            Worst e = worst[true];
            Check(e.Frames > 2000, $"{e.Frames} body-frames measured");
            Check(e.Steps == 0, $"no drawn root steps over twice its speed in a frame ({e.Steps}; worst {e.Step * 100f:F1} cm: {e.StepWho})");
            Check(e.TurnCount == 0, $"no drawn heading turns over {TurnLimit} rad in a frame ({e.TurnCount}; worst {e.Turn:F2})");
            Check(e.Poses == 0, $"no piece of a body moves over {PoseLimit} of its size in a frame ({e.Poses}; worst {e.Pose:F2}: {e.PoseWho})");
            eased.Clear();
            raw.Clear();
            eased.QueueFree();
            raw.QueueFree();
            PeopleGlide(ref clock);
        }
        finally
        {
            DrawSmoother.Off = false;
            DrawSmoother.Clock = static () => Time.GetTicksMsec() / 1000.0;
        }
    }

    /// <summary>A person walking, turning short, set back a step and stopping, drawn with and without the easing.</summary>
    void PeopleGlide(ref double clock)
    {
        WorldLife life = Bestiary.Roll(_data, World(15f));
        Species who = life.People.Person(Seed(), PersonRole.Farmer, founders: life.People.FoundersOf(Seed()));
        var rig = new HumanRig(who);
        var views = new Dictionary<bool, PersonView>();
        foreach (bool ease in new[] { true, false })
        {
            var v = new PersonView { PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off };
            AddChild(v);
            v.Build(rig, PersonView.Prepare(rig));
            views[ease] = v;
        }
        var lastAt = new Dictionary<bool, Vector3>();
        var lastYaw = new Dictionary<bool, float>();
        var lastBones = new Dictionary<bool, Transform3D[]>();
        var worstStep = new Dictionary<bool, float> { [true] = 0f, [false] = 0f };
        var worstTurn = new Dictionary<bool, float> { [true] = 0f, [false] = 0f };
        var worstPose = new Dictionary<bool, float> { [true] = 0f, [false] = 0f };
        Vector3 at = Vector3.Zero;
        float yaw = 0f, walked = 0f;
        for (int f = 0; f < 240 * 12; f++)
        {
            clock += Frame;
            double t = f * (double)Frame;
            bool moving = t < 6.0 || t > 8.0;
            if (f % 4 == 0)
            {
                // Walks, turns 90 degrees in one tick at 3 s, is set back 1.2 m at 5 s, stops at 6 s, goes again at 8 s, and is moved 0.5 m at 10 s.
                float speed = moving ? 1.4f : 0f;
                if (Math.Abs(t - 3.0) < 0.005) yaw += MathF.PI / 2f;
                if (Math.Abs(t - 5.0) < 0.005) at -= new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw)) * 1.2f;
                if (Math.Abs(t - 10.0) < 0.005) at += new Vector3(0.5f, 0f, 0f);
                at += new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw)) * speed * Tick;
                walked += speed * Tick;
                rig.Drive(new Gesture { Feet = at, Yaw = yaw, Speed = speed, Walked = walked, Clock = (float)t });
            }
            foreach (bool ease in new[] { true, false })
            {
                DrawSmoother.Off = !ease;
                PersonView v = views[ease];
                v.Sync(at, yaw, moving ? 1.4f : 0f, moving ? 1 : 0);
                v.Glide();
                DrawSmoother.Off = false;
                Vector3 drawn = v.Transform * at;
                float drawnYaw = yaw + v.Easing().Turn;
                Transform3D back = new Transform3D(new Basis(Vector3.Up, yaw), at).AffineInverse();
                var bones = new Transform3D[rig.Pose.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = back * v.Drawn[i];
                if (lastAt.TryGetValue(ease, out Vector3 was))
                {
                    float allowed = 2f * 1.4f * Frame + 0.03f;
                    worstStep[ease] = MathF.Max(worstStep[ease], (drawn - was).Length() - allowed);
                    worstTurn[ease] = MathF.Max(worstTurn[ease], MathF.Abs(Geo.WrapAngle(drawnYaw - lastYaw[ease])));
                    float shift = 0f;
                    for (int i = 0; i < bones.Length; i++) shift = MathF.Max(shift, (bones[i].Origin - lastBones[ease][i].Origin).Length());
                    worstPose[ease] = MathF.Max(worstPose[ease], shift / MathF.Max(0.3f, who.Figure.Height));
                }
                lastAt[ease] = drawn;
                lastYaw[ease] = drawnYaw;
                lastBones[ease] = bones;
            }
        }
        foreach (bool ease in new[] { false, true })
            GD.Print($"GLIDE person {(ease ? "eased" : "raw  ")}: worst root step {worstStep[ease] * 100f:F1} cm over its speed, worst turn {worstTurn[ease]:F2} rad, worst piece shift {worstPose[ease]:F2} of their height");
        Check(worstStep[true] < 0.05f, $"a person's drawn root never steps over twice their speed through a set-back and a move ({worstStep[true] * 100f:F1} cm over)");
        Check(worstTurn[true] < TurnLimit, $"nor snaps to a new heading ({worstTurn[true]:F2} rad in a frame)");
        Check(worstPose[true] < PoseLimit, $"and stopping and starting blends the pose ({worstPose[true]:F2} of their height in a frame)");
        foreach (PersonView v in views.Values) v.QueueFree();
    }
}
