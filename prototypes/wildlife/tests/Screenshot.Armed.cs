using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// Armed people, fresh each run, five of different folk with different weapons (run windowed):
/// - armed-front / armed-side: from the front or the side; --pick=aim raises every weapon, otherwise calm; --pick=crouch (or aim+crouch) crouches them.
/// </summary>
public partial class Screenshot
{
    static readonly string[][] ArmedSets =
    {
        new[] { "rifle", "bow", "spear", "pistol", "laser_rifle" },
        new[] { "shotgun", "crossbow", "machete", "energy_pistol", "musket" },
    };

    bool ArmedSetup()
    {
        if (_view is not ("armed-front" or "armed-side")) return false;
        Main m = _main;
        Stage(0.42f);
        m.Sim.Frozen = true;
        bool aim = _pick.Contains("aim"), crouch = _pick.Contains("crouch");
        var rng = new Rng((ulong)DateTime.UtcNow.Ticks);
        string[] props = ArmedSets[rng.Int(0, ArmedSets.Length - 1)];
        // Five people of different folk: fresh worlds until five folk are found.
        var people = new List<Species>();
        var folks = new HashSet<string>();
        for (int i = 0; i < 200 && people.Count < props.Length; i++)
        {
            var w = new WorldConditions { Seed = rng.NextU64(), Alienness = rng.Range(0f, 1f), TempC = rng.Range(-10f, 30f) };
            PeopleGenerator g = Bestiary.Roll(m.Data, w).People;
            Species s = g.Person(rng.NextU64(), rng.Chance(0.5f) ? PersonRole.Guard : PersonRole.Worker, founders: g.FoundersOf(rng.NextU64()));
            if (s.Mechanical || s.Figure.Husk || !folks.Add(s.Figure.Folk?.Id ?? "human") && i < 150) continue;
            s.Figure.Prop = props[people.Count];
            people.Add(s);
        }
        Vector3 fwd = Turn(SunFlat(), 0.35f), right = fwd.Cross(Vector3.Up).Normalized();
        Vector3 origin = Ground(new Vector3(-20f, 0f, 30f));
        var crowd = new List<Creature>();
        for (int i = 0; i < people.Count; i++)
        {
            Creature c = m.Sim.Spawn(people[i], origin + right * (i - 2) * 1.6f);
            c.Yaw = MathF.Atan2(fwd.X, fwd.Z);
            c.Rig.Reset(m.Sim, c);
            crowd.Add(c);
        }
        // The pose each frame, driven straight over the sim's own and drawn again.
        _each = () =>
        {
            foreach (Creature c in crowd)
                ((HumanRig)c.Rig).Drive(new Gesture { Feet = c.Position, Yaw = c.Yaw, Aim = aim ? 1f : 0f, Crouch = crouch ? 1f : 0f, Clock = _tick / 60f });
            m.View.Sync(m.Camera.GlobalPosition); // (Main synced the sim's own pose already this tick)
        };
        bool side = _view == "armed-side";
        Vector3 eye = side ? Ground(origin + right * 5.5f - fwd * 2.2f) + Vector3.Up * 1.4f : Ground(origin - fwd * 7.5f) + Vector3.Up * 1.5f;
        m.Camera.Fov = side ? 62f : 52f;
        _aim = () => (eye, origin + Vector3.Up * 1.0f - (side ? right * 1.2f : Vector3.Zero));
        _captureTick = 200;
        return true;
    }
}
