using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// The Husks and the Hollows, in daylight (run windowed, like the rest):
/// - husk-near / husk-far: one Husk at 5 m and at 30 m on open ground
/// - husk-lineup: five Husks of one world side by side, no two alike
/// - husk-grab: the seize from the eyes of the one seized, face to face
/// - husk-walk: frames of a Husk's puppet walk (--frames=N writes out-000.png onward)
/// - hollow-pair: a Hollow (fungal) animal beside a healthy one of its kind
/// - husk-den: worm masses on a den's floor and wall, a Husk among them (the showroom outpost)
/// </summary>
public partial class Screenshot
{
    int _frames, _frameEvery = 3, _framesTaken, _framesAsked;

    /// <summary>A core world's Husks: a fresh world each run.</summary>
    List<Species> HuskHosts()
    {
        var rng = new Rng((ulong)DateTime.UtcNow.Ticks);
        for (int i = 0; i < 50; i++)
        {
            var w = new WorldConditions { Seed = rng.NextU64(), Alienness = 0.2f, TempC = 14f, Coreward = 1f };
            Species lead = Bestiary.Roll(_main?.Data ?? _room.Data, w).Moving.FirstOrDefault(s => s.Husk);
            if (lead?.Hosts is { Count: >= 5 } hosts) return hosts;
        }
        throw new InvalidOperationException("no world with Husks");
    }

    /// <summary>A bare, sunlit stage: nothing else on it.</summary>
    void Stage(float time)
    {
        Main m = _main;
        World(WorldKind.Temperate, time);
        m.Grass.Visible = true;
        m.Vessel.Visible = false;
        m.Outpost.Visible = false;
        m.Player.Teleport(Ground(new Vector3(200f, 0f, 200f)));
        m.Sim.Clear();
        m.View.Bind(m.Sim, null);
        m.Sim.Targets.Clear();
        m.Hud.Visible = false;
    }

    bool HuskSetup()
    {
        Main m = _main;
        switch (_view)
        {
            case "husk-near":
            case "husk-far":
            {
                Stage(0.44f);
                m.Sim.Frozen = true;
                // One whose face shows (no brim over it), the most wrong of those.
                Species s = HuskHosts().OrderBy(h => h.Figure.Wear.Any(w => HumanBody.Slot(w) == "head") ? 1 : 0)
                    .ThenByDescending(h => System.Numerics.BitOperations.PopCount((uint)h.Figure.Wrongs)).First();
                Vector3 toward = Turn(SunFlat(), 0.4f);
                Vector3 at = Ground(new Vector3(20f, 0f, -30f));
                Creature c = m.Sim.Spawn(s, at);
                c.Yaw = MathF.Atan2(toward.X, toward.Z);
                c.Rig.Reset(m.Sim, c);
                float d = _view == "husk-near" ? 5f : 30f;
                Vector3 eye = Ground(at - toward * d) + Vector3.Up * 1.65f;
                // A narrow view, as when you look hard at something: the distance is real, the frame shows the shape.
                m.Camera.Fov = d < 10f ? 40f : 16f;
                _each = () => c.Stir = 0.6f + 0.4f * MathF.Sin(_tick * 0.05f);
                _aim = () => (eye, c.Position + Vector3.Up * (d < 10f ? 1.25f : 0.95f));
                _captureTick = 200;
                return true;
            }
            case "husk-lineup":
            {
                Stage(0.44f);
                m.Sim.Frozen = true;
                List<Species> hosts = HuskHosts().Take(5).ToList();
                Vector3 fwd = Turn(SunFlat(), 0.3f), right = fwd.Cross(Vector3.Up).Normalized();
                Vector3 origin = Ground(new Vector3(-20f, 0f, 30f));
                for (int i = 0; i < hosts.Count; i++)
                {
                    Creature c = m.Sim.Spawn(hosts[i], origin + right * (i - 2) * 1.5f);
                    c.Yaw = MathF.Atan2(fwd.X, fwd.Z) + (i - 2) * 0.12f;
                    c.Rig.Reset(m.Sim, c);
                }
                Vector3 eye = Ground(origin - fwd * 6.2f) + Vector3.Up * 1.7f;
                m.Camera.Fov = 55f;
                _aim = () => (eye, origin + Vector3.Up * 1.0f);
                _captureTick = 200;
                return true;
            }
            case "husk-grab":
            {
                Stage(0.44f);
                Species s = HuskHosts().OrderBy(h => h.Figure.Wear.Any(w => HumanBody.Slot(w) == "head") ? 1 : 0).ThenByDescending(h => h.Figure.JawDrop).First();
                Vector3 toward = Turn(SunFlat(), 0.5f);
                Vector3 feet = Ground(new Vector3(10f, 0f, 20f));
                var you = new DummyTarget { Name = "You", Position = feet, Health = new Health(1e9f), Forward = -toward };
                m.Sim.Targets.Add(you);
                Creature c = m.Sim.Spawn(s, feet + toward * 1.2f);
                c.Yaw = MathF.Atan2(toward.X, toward.Z);
                int seizeAt = _tick + 20;
                _each = () =>
                {
                    if (_tick == seizeAt) Seizing.Begin(m.Sim, c, you);
                    if (c.Phase == AttackPhase.Hold) c.HeldFor = 0f; // held for the shot
                };
                m.Camera.Fov = 62f;
                Vector3 eye = feet + Vector3.Up * 1.65f;
                _aim = () => (eye, c.Position + Vector3.Up * s.Figure.Height * 0.9f);
                _captureTick = seizeAt + 70;
                return true;
            }
            case "husk-walk":
            {
                Stage(0.44f);
                Species s = HuskHosts().OrderByDescending(h => h.Figure.Wrongs.HasFlag(Wrong.Arm) || h.Figure.Wrongs.HasFlag(Wrong.Head) ? 1 : 0).First();
                Vector3 fwd = Turn(SunFlat(), 1.35f), side = Turn(fwd, -1.2f);
                Vector3 start = Ground(new Vector3(-10f, 0f, 0f));
                var lure = new DummyTarget { Name = "Lure", Position = Ground(start + fwd * 60f), Health = new Health(1e9f) };
                m.Sim.Targets.Add(lure);
                Creature c = m.Sim.Spawn(s, start);
                c.Yaw = MathF.Atan2(-fwd.X, -fwd.Z);
                c.Noticed = lure;
                m.Camera.Fov = 50f;
                _aim = () => (Ground(c.Position + side * 4.2f - fwd * 1.2f) + Vector3.Up * 1.45f, c.Position + Vector3.Up * 0.95f);
                _captureTick = 120;
                if (_frames <= 0) _frames = 48;
                return true;
            }
            case "hollow-pair":
            {
                Stage(0.44f);
                m.Sim.Frozen = true;
                Species healthy = m.Fauna.Species.Where(x => x.Rig == RigKind.Legged && !x.Corrupted && x.Plan != BodyPlan.Giant && x.Body.Size is > 0.8f and < 3f)
                    .OrderByDescending(x => x.Body.Size).First();
                Species hollow = healthy.Copy();
                hollow.Mate = null;
                hollow.Young = null;
                m.Generator.CorruptSpecies(hollow, Rng.Mix(healthy.Seed, 0x4011));
                Vector3 fwd = Turn(SunFlat(), 0.3f), right = fwd.Cross(Vector3.Up).Normalized();
                Vector3 origin = Ground(new Vector3(-30f, 0f, -20f));
                float gap = healthy.Body.Size * 0.75f + healthy.Body.Width + 0.8f;
                Vector3 face = (-right * 0.7f - fwd * 0.7f).Normalized();
                foreach ((Species sp, float x) in new[] { (healthy, -gap), (hollow, gap) })
                {
                    Creature c = m.Sim.Spawn(sp, origin + right * x);
                    c.Yaw = MathF.Atan2(-face.X, -face.Z);
                    c.Rig.Reset(m.Sim, c);
                }
                float r = MathF.Max(healthy.Body.Size, healthy.Body.LegLength + healthy.Body.Height);
                Vector3 eye = Ground(origin - fwd * (gap * 1.3f + r * 1.5f)) + Vector3.Up * (1.2f + r * 0.5f);
                m.Camera.Fov = 50f;
                _aim = () => (eye, origin + Vector3.Up * (healthy.Body.LegLength + healthy.Body.Height) * 0.5f);
                _captureTick = 180;
                return true;
            }
        }
        return false;
    }

    /// <summary>A den: worm masses on the outpost's deepest floor and its wall, and a Husk standing among them.</summary>
    bool DenSetup()
    {
        if (_view != "husk-den") return false;
        Showroom r = _room;
        r.GoTo(2);
        WildSim sim = r.Current.Sim;
        sim.Targets.Clear();
        Nest deepest = sim.Nests.OrderBy(n => n.Position.Y).First();
        foreach (Creature x in sim.Creatures) if ((x.Position - deepest.Position).Length() < 8f) x.Removed = true;
        ISolidSpace space = r.Current.Space;
        // Off to one side of the nest there, on the same floor.
        Vector3 spot = OpenSpot(space, deepest.Position, deepest.Position + Vector3.Up * 0.5f, 2.6f, 0f, sameFloor: true);
        // A den has no nests: this station's (another occupant's) are taken out, eggs and all.
        sim.Nests.Clear();
        if (r.Current.Lairs != null) r.Current.Lairs.Visible = false;
        var nest = new { Position = spot };
        var rng = new Rng((ulong)DateTime.UtcNow.Ticks);
        var masses = new List<WormMass>();
        // Two on the floor round where the nest was, one on the nearest wall.
        for (int i = 0; i < 2; i++)
        {
            Vector3 p = nest.Position + new Vector3(i == 0 ? -0.6f : 1.3f, 0.5f, i == 0 ? 0.4f : -0.9f);
            float y = SpaceMath.FloorBelow(space, p.X, p.Y, p.Z, 3f) ?? nest.Position.Y;
            masses.Add(new WormMass { Position = new Vector3(p.X, y, p.Z), Normal = Vector3.Up, Size = i == 0 ? 1.25f : 0.8f, Seed = rng.NextU64() });
        }
        Vector3 bestDir = Vector3.Right;
        float bestT = 99f;
        foreach (Vector3 d in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back })
        {
            float t = SpaceMath.Ray(space, nest.Position + Vector3.Up * 1.1f, d, 6f);
            if (t < bestT) { bestT = t; bestDir = d; }
        }
        masses.Add(new WormMass { Position = nest.Position + Vector3.Up * 1.1f + bestDir * bestT, Normal = -bestDir, Size = 0.9f, Seed = rng.NextU64() });
        var views = new List<WormMassView>();
        foreach (WormMass m in masses)
        {
            sim.Masses.Add(m);
            var v = new WormMassView();
            r.Current.Root.AddChild(v);
            v.Build(m);
            views.Add(v);
        }
        Species husk = HuskHosts().First();
        Creature c = sim.SpawnIn(husk, space, nest.Position + new Vector3(0.6f, 0.3f, 1.4f));
        Vector3 look = nest.Position + new Vector3(0.3f, 0.6f, 0.2f);
        Vector3 eye = OpenSpot(space, nest.Position, look, 3.4f, 1.6f, sameFloor: true);
        var you = new DummyTarget { Name = "You", Position = new Vector3(eye.X, nest.Position.Y, eye.Z), Health = new Health(1e9f) };
        sim.Targets.Add(you);
        c.Yaw = MathF.Atan2(-(eye.X - c.Position.X), -(eye.Z - c.Position.Z));
        sim.Frozen = true;
        _each = () => { foreach (WormMassView v in views) v.Sync(1f / 60f); c.Stir = 1f; };
        r.Camera.Fov = 62f;
        _aim = () => (eye, look);
        _captureTick = 200;
        return true;
    }

    /// <summary>Frames for a GIF: from the capture tick, one every few ticks, numbered after --out.</summary>
    async void CaptureFrame()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        string path = _out.EndsWith(".png") ? _out[..^4] + $"-{_framesTaken:000}.png" : $"{_out}-{_framesTaken:000}.png";
        image.SavePng(path);
        _framesTaken++;
        if (_framesTaken >= _frames)
        {
            GD.Print($"saved {_framesTaken} frames");
            Shutdown.Quit(this, 0);
        }
    }
}
