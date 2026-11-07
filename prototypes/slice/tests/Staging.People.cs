using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Slice.Places;
using Slice.Surface;
using Wildlife;
using Rng = Wildlife.Rng;
using WeatherKind = Weather.Model.WeatherKind;
using Mood = PoiGenerator.Gen.Mood;

namespace Slice.Tests;

/// <summary>
/// The bestiary's proof frames, all in the real game on the world asked for (--planet, --seed):
/// --view=folk   everyone this world dresses, one of each role, then a mutated one and a raider, at two distances
/// --view=crowd  a real town's people at several distances, and a run of frames of them walking
/// --view=fauna  this world's wild species standing on its own ground, and what vacuum leaves
/// --out=DIR is the folder (one PNG a moment).
/// </summary>
public sealed partial class Staging
{
    string Folder(string fallback)
    {
        string dir = _args.GetValueOrDefault("out", fallback);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A fixed camera, never under the ground: a staged eye in rising ground is lifted clear of it.</summary>
    void Look(Vector3 eye, Vector3 at)
    {
        float ground = _game.Zone.GroundAt(eye.X, eye.Z).Y;
        if (eye.Y < ground + 1.2f) { GD.Print($"(camera lifted {ground + 1.2f - eye.Y:F1} m out of the ground)"); eye.Y = ground + 1.2f; }
        _game.FixedCamera = new Transform3D(Basis.LookingAt((at - eye).Normalized(), Vector3.Up), eye);
    }

    /// <summary>What in view stands well clear of the ground under it: a list to check nothing floats.</summary>
    void ReportAloft(Vector3 eye, Vector3 at)
    {
        Vector3 look = (at - eye).Normalized();
        foreach (Node n in _game.Zone.FindChildren("*", "GeometryInstance3D", true, false))
        {
            if (n is not GeometryInstance3D g || !g.IsVisibleInTree()) continue;
            Aabb box = g.GlobalTransform * g.GetAabb();
            Vector3 c = box.GetCenter();
            Vector3 to = c - eye;
            float d = to.Length();
            if (d < 2f || d > 200f || to.Normalized().Dot(look) < 0.75f) continue;
            float under = _game.Zone.GroundAt(c.X, c.Z).Y;
            if (box.Position.Y - under > 2.5f) GD.Print($"ALOFT {n.GetPath()} ({g.GetType().Name}) {box.Position.Y - under:F1} m over the ground, {d:F0} m away, size {box.Size}");
        }
    }

    Vector3 Ground(Vector3 p)
    {
        p.Y = _game.Zone.GroundAt(p.X, p.Z).Y;
        return p;
    }

    IEnumerable<int> FolkView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string dir = Folder("folk");
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        WorldLife life = _game.Zone.Wildlife.Life;
        WorldConditions w = life.World;
        GD.Print($"(world: {w.TempC:F0} C {w.Climate}, air {w.Oxygen:F0}%, vacuum {w.Vacuum}, rad {w.Radiation:F1}, mutagen {w.Mutagen:F2}, folk {life.People.Folk.Id})");
        Vector3 spot = Ground(Play.FlatSpot(_game.Survivor.WorldFeet, 30f, span: 14f));
        var stage = new Node3D { Name = "Lineup" };
        _game.Zone.AddChild(stage);
        ulong seed = (ulong)Num("seed2", 41);
        var line = new List<Species>();
        foreach (PersonRole r in new[] { PersonRole.Trader, PersonRole.Guard, PersonRole.Farmer, PersonRole.Medic, PersonRole.Scavenger, PersonRole.Elder, PersonRole.Child, PersonRole.Drunk, PersonRole.Worker })
            line.Add(life.People.Person(Rng.Mix(seed, (ulong)r), r));
        line.Add(life.People.Person(Rng.Mix(seed, 100), PersonRole.Raider, Occupancy.Raider));
        line.Add(life.People.Person(Rng.Mix(seed, 101), PersonRole.Raider, Occupancy.Raider));
        var people = new List<(HumanRig rig, Vector3 at, PersonView view)>();
        for (int i = 0; i < line.Count; i++)
        {
            var rig = new HumanRig(line[i]);
            Vector3 at = Ground(spot + new Vector3((i - (line.Count - 1) * 0.5f) * 1.25f, 0f, (i % 2) * 0.5f));
            rig.Drive(new Gesture { Feet = at, Yaw = MathF.PI, Clock = i });
            var view = new PersonView();
            stage.AddChild(view);
            view.Build(rig);
            view.Sync();
            view.SyncFar(at, MathF.PI);
            people.Add((rig, at, view));
            GD.Print($"  {line[i].CommonName} {line[i].Figure.Sex} {line[i].Figure.Age:F0} y {line[i].Figure.Height:F2} m build {line[i].Figure.Build:F2} {line[i].Figure.Dress} [{string.Join(", ", line[i].Figure.Wear)}] {line[i].Figure.Prop}");
        }
        _game.Visor.Visible = false;
        Look(spot + new Vector3(0f, 1.3f, 10f), spot + Vector3.Up * 0.9f);
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "lineup.png"))) yield return x;
        Look(spot + new Vector3(-3.6f, 1.4f, 3.6f), spot + new Vector3(-3.6f, 1f, 0f));
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "close-left.png"))) yield return x;
        Look(spot + new Vector3(3.6f, 1.4f, 3.6f), spot + new Vector3(3.6f, 1f, 0f));
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "close-right.png"))) yield return x;

        // At range: 30, 70 and 140 m, as the eye meets them across open ground.
        foreach (float range in new[] { 30f, 70f, 140f })
        {
            Look(spot + new Vector3(0f, 1.7f, range), spot + Vector3.Up * 0.9f);
            foreach (int x in _drive.Wait(5)) yield return x;
            foreach (int x in Shot(Path.Combine(dir, $"at-{range:F0}m.png"))) yield return x;
        }
        // What bodies say: hands up at a gun, a wave, a guard's warning, a trader's wares, a start, a raider's taunt; close and from 25 m.
        var acts = new[] { BodyAct.HandsUp, BodyAct.Greet, BodyAct.Warn, BodyAct.Present, BodyAct.Startle, BodyAct.Panic, BodyAct.Cower, BodyAct.Shrug, BodyAct.Beckon, BodyAct.Taunt, BodyAct.Taunt };
        for (int i = 0; i < people.Count; i++)
            people[i].rig.Drive(new Gesture { Feet = people[i].at, Yaw = MathF.PI, Clock = 2f + i, Act = acts[i % acts.Length], ActAmount = 1f, ActAt = people[i].at + new Vector3(1.5f, 1f, 2f) });
        foreach (var q in people) q.view.Sync();
        Look(spot + new Vector3(0f, 1.3f, 10f), spot + Vector3.Up * 0.9f);
        foreach (int x in _drive.Wait(5)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "gestures.png"))) yield return x;
        foreach (float range in new[] { 30f, 70f })
        {
            Look(spot + new Vector3(0f, 1.7f, range), spot + Vector3.Up * 0.9f);
            foreach (int x in _drive.Wait(5)) yield return x;
            foreach (int x in Shot(Path.Combine(dir, $"gestures-{range:F0}m.png"))) yield return x;
            if (range == 30f) ReportAloft(spot + new Vector3(0f, 1.7f, range), spot + Vector3.Up * 0.9f);
        }
        // Walking: everyone of the line-up crosses in front of the camera at their own pace, a child running, the drunk weaving.
        string walk = Path.Combine(dir, "walk");
        Directory.CreateDirectory(walk);
        var paces = new float[people.Count];
        var walked = new float[people.Count];
        for (int i = 0; i < people.Count; i++)
        {
            Figure f = line[i].Figure;
            paces[i] = f.Stage == LifeStage.Child && f.Playful ? 3.2f : f.Stage == LifeStage.Elder ? 0.9f : f.Role == PersonRole.Drunk ? 0.9f : f.Role == PersonRole.Scavenger ? 1.8f : 1.35f;
        }
        Look(spot + new Vector3(0f, 1.4f, 9f), spot + Vector3.Up * 0.9f);
        for (int frame = 0; frame < 72; frame++)
        {
            for (int i = 0; i < people.Count; i++)
            {
                float dt = 1f / 30f;
                walked[i] += paces[i] * dt;
                float x = -8f + ((walked[i] + i * 1.7f) % 16f);
                Vector3 at = Ground(spot + new Vector3(x, 0f, -1.5f + (i % 4) * 1.1f));
                people[i].rig.Drive(new Gesture { Feet = at, Yaw = -MathF.PI * 0.5f, Speed = paces[i], Walked = walked[i], Clock = frame / 30f + i });
                people[i].view.Sync();
                people[i].view.SyncFar(at, -MathF.PI * 0.5f);
            }
            foreach (int x in _drive.Wait(1)) yield return x;
            foreach (int x in Shot(Path.Combine(walk, $"f{frame:000}.png"))) yield return x;
        }
        // A dozen people as a place would have them: one people, every job, every build.
        foreach (var q in people) q.view.QueueFree();
        people.Clear();
        Founders founders = life.People.FoundersOf((ulong)Num("seed2", 41) + 7);
        var jobs = Enum.GetValues<PersonRole>();
        for (int i = 0; i < 12; i++)
        {
            Species one = life.People.Person(Rng.Mix(seed, 500 + (ulong)i), jobs[(i * 7 + 3) % jobs.Length], founders: founders);
            var rig = new HumanRig(one);
            Vector3 at = Ground(spot + new Vector3((i - 5.5f) * 1.05f, 0f, (i % 3) * 0.4f));
            rig.Drive(new Gesture { Feet = at, Yaw = MathF.PI, Clock = i * 1.3f });
            var view = new PersonView();
            stage.AddChild(view);
            view.Build(rig);
            view.Sync();
            view.SyncFar(at, MathF.PI);
            people.Add((rig, at, view));
        }
        Look(spot + new Vector3(0f, 1.25f, 9.5f), spot + Vector3.Up * 0.9f);
        foreach (int x in _drive.Wait(5)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "dozen.png"))) yield return x;
        // The same world's mutation: mild, strong, monstrous, and a husk beside them.
        foreach (Node n in stage.GetChildren()) n.QueueFree();
        var ones = new[]
        {
            life.People.Person(Rng.Mix(seed, 200), PersonRole.Worker, Occupancy.Friendly, 0.25f),
            life.People.Person(Rng.Mix(seed, 201), PersonRole.Scavenger, Occupancy.Friendly, 0.6f),
            life.People.Person(Rng.Mix(seed, 202), PersonRole.Raider, Occupancy.Raider, 0.95f),
            life.People.Person(Rng.Mix(seed, 203), PersonRole.Worker, Occupancy.Feral, 1f),
            life.People.Person(Rng.Mix(seed, 204), PersonRole.Worker, Occupancy.Feral, 0.3f, husk: true),
        };
        for (int i = 0; i < ones.Length; i++)
        {
            var rig = new HumanRig(ones[i]);
            Vector3 at = Ground(spot + new Vector3((i - 2f) * 1.5f, 0f, 0f));
            rig.Drive(new Gesture { Feet = at, Yaw = MathF.PI, Clock = i });
            var view = new PersonView();
            stage.AddChild(view);
            view.Build(rig);
            view.Sync();
            view.SyncFar(at, MathF.PI);
            Mutation.Report m = Mutation.Of(ones[i]);
            GD.Print($"  {ones[i].CommonName}: {m.Growths} growths, asymmetry {m.Asymmetry:F2} extra '{m.Extra}', monstrous {ones[i].Monstrous}, spit {ones[i].Spit}");
        }
        Look(spot + new Vector3(0f, 1.4f, 5.4f), spot + Vector3.Up * 0.95f);
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "mutated.png"))) yield return x;
        Look(spot + new Vector3(3f, 1.55f, 1.3f), spot + new Vector3(3f, 1.55f, 0f));
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "husk-close.png"))) yield return x;
    }

    IEnumerable<int> CrowdView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string dir = Folder("crowd");
        Vector3 from = _game.Survivor.WorldFeet;
        foreach (int x in _drive.UntilSettled()) yield return x;
        PlanetSurface.Poi.PoiSite site = _game.Zone.Planet.Pois.Where(s => s.Instance?.Townsfolk != null).OrderBy(s => s.Instance.Mood == Mood.Hostile ? 1 : 0).ThenBy(s => _game.Places.DistanceTo(s, from)).FirstOrDefault();
        if (site == null) { GD.Print($"(no town on this world: {_game.Zone.Planet.Pois.Count} places, {_game.Zone.Planet.Pois.Count(s => s.Instance != null)} planned, settlements {_game.Zone.Planet.Pois.Count(s => s.Family.Tags.Contains("settlement"))})"); yield break; }
        GD.Print($"(town: {site.Name}, {site.Instance.Mood}, {site.Instance.Townsfolk.People.Count} people)");
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        // Local noon here: the hour of the brightest sky at this place.
        double noon = 0.5, brightest = -1;
        for (int k = 0; k < 24; k++)
        {
            _game.Zone.Sky.TimeOfDay = k / 24.0;
            yield return 0;
            yield return 0;
            if (_game.Zone.Sky.ToSun.Y > brightest) { brightest = _game.Zone.Sky.ToSun.Y; noon = k / 24.0; }
        }
        _game.Zone.Sky.TimeOfDay = noon - 0.06;
        foreach (int x in _drive.Wait(300)) yield return x;
        Town town = p.Memory.Town;
        var people = town.People.Where(q => !q.Indoors && !q.Dead && !p.Roofed(p.ToWorld(q.Pos) + Vector3.Up * 0.1f)).ToList();
        if (people.Count == 0) people = town.People.Where(q => !q.Dead).ToList();
        Town.Person busy = people.OrderByDescending(q => people.Count(o => o.Pos.DistanceTo(q.Pos) < 12f)).First();
        Vector3 at = p.ToWorld(busy.Pos);
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        Vector3 EyeAt(float r)
        {
            Vector3 best = at + Vector3.Back * r;
            int most = -1;
            for (int a = 0; a < 16; a++)
            {
                Vector3 c = at + new Vector3(MathF.Cos(a * MathF.Tau / 16f), 0f, MathF.Sin(a * MathF.Tau / 16f)) * r;
                c.Y = _game.Zone.GroundAt(c.X, c.Z).Y + 1.7f;
                int n = people.Count(o => space.IntersectRay(PhysicsRayQueryParameters3D.Create(c, p.ToWorld(o.Pos) + Vector3.Up * 1.2f, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull)).Count == 0);
                if (n > most) { most = n; best = c; }
            }
            return best;
        }
        _game.Visor.Visible = false;
        // The survivor stands where the camera is, as in play: what's near them is what's posed.
        void Stand(Vector3 eye)
        {
            // A step behind the camera, so their own body stays out of the picture.
            Vector3 back = eye + (eye - at).Normalized() * 2.5f;
            _game.Survivor.PlaceOutside(Ground(back).ToNumerics());
            Look(eye, at + Vector3.Up * 1f);
        }
        float worst = 0f;
        foreach (float r in new[] { 5f, 14f, 32f, 70f, 140f })
        {
            Stand(EyeAt(r));
            for (int i = 0; i < 20; i++) { yield return 0; worst = MathF.Max(worst, town.LastTickMs); }
            foreach (int x in Shot(Path.Combine(dir, $"at-{r:F0}m.png"))) yield return x;
        }
        Town.Person walker = town.People.Where(q => !q.Dead && !q.Indoors && q.Now > 0.4f).OrderBy(q => q.Pos.DistanceTo(busy.Pos)).FirstOrDefault() ?? busy;
        string frames = Path.Combine(dir, "walk");
        Directory.CreateDirectory(frames);
        Vector3 eye = p.ToWorld(walker.Pos) + new Vector3(4.5f, 0f, 4.5f);
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.6f;
        _game.Survivor.PlaceOutside(Ground(eye + new Vector3(2.5f, 0f, 2.5f)).ToNumerics());
        for (int i = 0; i < 48; i++)
        {
            Vector3 w = p.ToWorld(walker.Pos);
            Look(eye, w + Vector3.Up * 0.9f);
            foreach (int x in _drive.Wait(2)) yield return x;
            foreach (int x in Shot(Path.Combine(frames, $"f{i:000}.png"))) yield return x;
        }
        GD.Print($"(people cost: worst tick {worst:F2} ms for {town.People.Count} while shooting, {people.Count} out; walker {walker.Role} at {walker.Now:F1} m/s)");
    }

    IEnumerable<int> FaunaView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string dir = Folder("fauna");
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        WorldLife life = _game.Zone.Wildlife.Life;
        WildlifePatch patch = _game.Zone.Wildlife;
        patch.Frozen = true;
        Vector3 spot = Ground(Play.FlatSpot(_game.Survivor.WorldFeet, 30f, span: 16f));
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42); // the climb took the day
        var everyone = life.Moving.Where(s => !s.Flies && s.Plan != BodyPlan.Giant).Take(7).ToList();
        Species giant = life.Moving.FirstOrDefault(s => s.Plan == BodyPlan.Giant);
        GD.Print($"({life.World.Climate} {life.World.TempC:F0} C, vacuum {life.World.Vacuum}: {life.Moving.Count()} kinds)");
        foreach (Species s in life.Moving) GD.Print($"  {s.CommonName} ({s.Form}, {s.Rig}): {s.Coat} size {s.Body.Size:F1} legs {s.Body.LegLength:F2} ears {s.EarSize:F2} {s.Activity} senses {s.Senses} mate {s.Mate != null} scarcity {s.Scarcity:F2}");
        float x0 = 0f;
        var stood = new List<Vector3>();
        foreach (Species s in everyone)
        {
            float gap = MathF.Max(2.2f, s.BodyLength + 1.4f);
            Vector3 at = Ground(spot + new Vector3(x0 + gap * 0.5f, 0f, 0f));
            x0 += gap;
            Creature c = patch.Sim.Spawn(s, patch.PatchLocal(at));
            c.Yaw = MathF.PI * 0.5f;
            c.Rig.Reset(patch.Sim, c);
            stood.Add(at);
        }
        if (giant != null)
        {
            Creature c = patch.Sim.Spawn(giant, patch.PatchLocal(Ground(spot + new Vector3(x0 * 0.5f, 0f, -16f))));
            c.Yaw = MathF.PI * 0.5f;
            c.Rig.Reset(patch.Sim, c);
        }
        foreach (int x in _drive.Wait(4)) yield return x;
        _game.Visor.Visible = false;
        Look(spot + new Vector3(x0 * 0.5f, 2.4f, MathF.Max(8f, x0 * 0.72f)), spot + new Vector3(x0 * 0.5f, 1f, -3f));
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "lineup.png"))) yield return x;
        for (int i = 0; i < stood.Count; i += 2)
        {
            Look(stood[i] + new Vector3(0.7f, 1.3f, 3.6f), stood[i] + Vector3.Up * 0.6f);
            foreach (int x in _drive.Wait(10)) yield return x;
            foreach (int x in Shot(Path.Combine(dir, $"close-{i}.png"))) yield return x;
        }
    }

    /// <summary>
    /// --view=peopleperf: what people cost to draw and pose. Nobody, 6 and 20 people walking close to
    /// the camera, drawn as a node per piece and as one skinned body each: frame time on the CPU and
    /// GPU, draw calls, posing time and allocation, over 4 s each.
    /// </summary>
    IEnumerable<int> PeoplePerf()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        WorldLife life = _game.Zone.Wildlife.Life;
        Vector3 spot = Ground(Play.FlatSpot(_game.Survivor.WorldFeet, 30f, span: 14f));
        _game.Visor.Visible = false;
        Look(spot + new Vector3(0f, 1.6f, 12f), spot + Vector3.Up * 0.9f);
        Rid vp = _game.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(vp, true);
        var stage = new Node3D { Name = "PerfStage" };
        _game.Zone.AddChild(stage);
        var lines = new List<string>();
        foreach (bool merged in new[] { false, true })
        foreach (int count in new[] { 0, 6, 20 })
        {
            if (count == 0 && merged) continue;
            PersonView.Merged = merged;
            foreach (Node n in stage.GetChildren()) n.QueueFree();
            var crowd = new List<(HumanRig rig, PersonView view, float phase)>();
            // What the game does on a worker (who they are, their meshes) is left out: the main thread's share is timed.
            var rigs = Enumerable.Range(0, count).Select(i => new HumanRig(life.People.Person(Rng.Mix(77, (ulong)i), (PersonRole)(i % 10)))).ToList();
            var ready = rigs.Select(rg => merged ? PersonView.Prepare(rg) : null).ToList();
            long built = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                var rig = rigs[i];
                var view = new PersonView();
                stage.AddChild(view);
                view.Build(rig, ready[i]);
                crowd.Add((rig, view, i * 0.7f));
            }
            double buildMs = System.Diagnostics.Stopwatch.GetElapsedTime(built).TotalMilliseconds / Math.Max(1, count);
            foreach (int x in _drive.Wait(30)) yield return x;
            double cpu = 0, gpu = 0, pose = 0;
            long draws = 0, bytes = 0;
            int frames = 240;
            for (int f = 0; f < frames; f++)
            {
                long from = System.Diagnostics.Stopwatch.GetTimestamp();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < crowd.Count; i++)
                {
                    float t = f / 60f + crowd[i].phase;
                    float a = t * 0.25f;
                    Vector3 at = spot + new Vector3(MathF.Cos(a) * (3f + i % 5), 0f, MathF.Sin(a) * (3f + i % 5) - 2f);
                    crowd[i].rig.Drive(new Gesture { Feet = at, Yaw = -a, Speed = 1.3f, Walked = t * 1.3f, Clock = t });
                    crowd[i].view.Sync();
                    crowd[i].view.SyncFar(at, -a);
                }
                bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
                pose += System.Diagnostics.Stopwatch.GetElapsedTime(from).TotalMilliseconds;
                yield return 0;
                cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + RenderingServer.GetFrameSetupTimeCpu();
                gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
                draws += (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            }
            string line = $"PERF {(merged ? "one body" : "per piece")} x{count}: render cpu {cpu / frames:F2} ms, gpu {gpu / frames:F2} ms, draw calls {draws / frames}, posing {pose / frames:F3} ms, alloc {bytes / frames} B a frame, building {buildMs:F2} ms a person, nodes near {(crowd.Count > 0 ? crowd[0].view.DrawnNear : 0)} each";
            GD.Print(line);
            lines.Add(line);
            if (count == 20) foreach (int x in Shot(Path.Combine(Folder("perf"), $"/perf-{(merged ? "merged" : "pieces")}.png"))) yield return x;
        }
        PersonView.Merged = true;
        File.WriteAllLines(Path.Combine(Folder("perf"), "people-perf.txt"), lines);
    }
}