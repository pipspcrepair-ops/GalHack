using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Slice.Places;
using Wildlife;
using Rng = Wildlife.Rng;
using WeatherKind = Weather.Model.WeatherKind;
using Mood = PoiGenerator.Gen.Mood;

namespace Slice.Tests;

/// <summary>
/// The people round's proof frames, in the real game on the world asked for:
/// --view=peoples   six alien folk side by side, the Vharn among them, close and at 30 m
/// --view=outfits   what people wear from ice to desert, on a hazard world and where the air is bad
/// --view=townnight a town by day, then the same at night with its people indoors
/// --view=checkpoint a guard at their post warning you off, then the gun up as you come on
/// --view=wheel     the gesture wheel held open (windowed)
/// --out=DIR is the folder.
/// </summary>
public sealed partial class Staging
{
    /// <summary>A row of people set down standing, facing the camera; their views and rigs.</summary>
    List<(HumanRig rig, PersonView view, Vector3 at)> Row(Node3D stage, IReadOnlyList<Species> line, Vector3 spot, float gap)
    {
        var made = new List<(HumanRig, PersonView, Vector3)>();
        for (int i = 0; i < line.Count; i++)
        {
            var rig = new HumanRig(line[i]);
            Vector3 at = Ground(spot + new Vector3((i - (line.Count - 1) * 0.5f) * gap, 0f, 0f));
            rig.Drive(new Gesture { Feet = at, Yaw = MathF.PI * 0.92f, Clock = i * 1.3f });
            var view = new PersonView();
            stage.AddChild(view);
            view.Build(rig);
            view.Sync();
            view.SyncFar(at, MathF.PI * 0.92f);
            made.Add((rig, view, at));
        }
        return made;
    }

    /// <summary>
    /// Level ground with a clear view of a row this wide from this far back: of a few flat spots
    /// round you, the one whose sight lines to the row hit the fewest things (trees, rocks).
    /// </summary>
    Vector3 OpenSpot(float width, float back)
    {
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        Vector3 best = Ground(Play.FlatSpot(_game.Survivor.WorldFeet, 30f, span: width));
        int fewest = int.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            float a = i * 2.39996f, r = 25f + (i % 4) * 15f;
            Vector3 from = _game.Survivor.WorldFeet + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * r;
            Vector3 s = Ground(Play.FlatSpot(from, 12f, span: width));
            Vector3 eye = s + new Vector3(0f, 1.45f, back);
            int hits = 0;
            for (float x = -width * 0.5f; x <= width * 0.5f; x += 1f)
            {
                Vector3 to = Ground(s + new Vector3(x, 0f, 0f)) + Vector3.Up * 1.2f;
                var q = PhysicsRayQueryParameters3D.Create(eye, to);
                if (space.IntersectRay(q).Count > 0) hits++;
                float rise = _game.Zone.GroundAt(s.X + x, s.Z).Y - s.Y;
                if (MathF.Abs(rise) > 0.5f) hits++;
            }
            if (hits < fewest) { fewest = hits; best = s; }
        }
        GD.Print($"(stage: {fewest} sight lines blocked)");
        return best;
    }

    IEnumerable<int> NoonLight()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        double noon = 0.5, brightest = -1;
        for (int k = 0; k < 24; k++)
        {
            _game.Zone.Sky.TimeOfDay = k / 24.0;
            yield return 0;
            if (_game.Zone.Sky.ToSun.Y > brightest) { brightest = _game.Zone.Sky.ToSun.Y; noon = k / 24.0; }
        }
        _game.Zone.Sky.TimeOfDay = noon - 0.04;
    }

    IEnumerable<int> PeoplesView()
    {
        string dir = Folder("peoples");
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in NoonLight()) yield return x;
        WorldLife life = _game.Zone.Wildlife.Life;
        PeopleGenerator g = life.People;
        // Five folk rolled from this world's conditions at their strangest (fresh each run), and the Vharn.
        var rng = new Rng((ulong)DateTime.UtcNow.Ticks);
        var folks = new List<Folk>();
        var w = new WorldConditions { Seed = life.World.Seed, Alienness = 0.95f, TempC = 18f, Oxygen = 21f, Backdrop = life.World.Backdrop };
        for (int tries = 0; folks.Count < 5 && tries < 80; tries++)
        {
            w.TempC = rng.Range(-20f, 45f);
            Folk f = Peoples.Roll(rng, w, $"shown{folks.Count}");
            if (folks.All(o => Peoples.Shared(o, f) <= 2 && o.Covering + o.Mouth != f.Covering + f.Mouth)) folks.Add(f);
        }
        folks.Add(g.Vharn);
        var line = new List<Species>();
        foreach (Folk f in folks)
        {
            Founders founders = g.FoundersOf(rng.NextU64());
            founders.Folk = f;
            founders.Second = null;
            founders.Outsiders = 0f;
            founders.SkinHue = rng.Range(f.SkinHue);
            founders.SkinSat = rng.Range(f.SkinSat);
            founders.SkinValue = rng.Range(f.SkinVal);
            Species s = g.Person(rng.NextU64(), f.Id == "vharn" ? PersonRole.Guard : PersonRole.Worker, founders: founders);
            line.Add(s);
            GD.Print($"  {f.Id}: {f.Covering}, crown {f.Crown}, mouth {f.Mouth}{(f.Tusks ? "+tusks" : "")}, eyes {f.Eyes}, ears {f.Ears}, tail {f.Tail}, back {f.Back}; {s.Figure.Height:F2} m (top {s.Figure.Top:F2}); [{string.Join(", ", s.Figure.Wear)}]");
        }
        Vector3 spot = OpenSpot(12f, 6.5f);
        var stage = new Node3D { Name = "Peoples" };
        _game.Zone.AddChild(stage);
        Row(stage, line, spot, 1.6f);
        _game.Visor.Visible = false;
        Look(spot + new Vector3(0.6f, 1.4f, 6.2f), spot + Vector3.Up * 1.1f);
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "lineup.png"))) yield return x;
        Look(spot + new Vector3(-2.4f, 1.6f, 3.2f), spot + new Vector3(-2.4f, 1.45f, 0f));
        foreach (int x in _drive.Wait(5)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "close-left.png"))) yield return x;
        Look(spot + new Vector3(2.4f, 1.6f, 3.2f), spot + new Vector3(2.4f, 1.45f, 0f));
        foreach (int x in _drive.Wait(5)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "close-right.png"))) yield return x;
        Look(spot + new Vector3(-7f, 1.4f, -3.5f), spot + new Vector3(0f, 0.9f, 0f));
        foreach (int x in _drive.Wait(5)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "side.png"))) yield return x;
        Look(spot + new Vector3(0f, 1.7f, 30f), spot + Vector3.Up * 0.9f);
        foreach (int x in _drive.Wait(5)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "at-30m.png"))) yield return x;
        // The same folk taken by the worms: the worms read on every head.
        foreach (Node n in stage.GetChildren()) n.QueueFree();
        var husks = line.Select(s => Husks.Turn(s, rng.NextU64(), g.HuskRules)).ToList();
        Row(stage, husks, spot, 1.6f);
        Look(spot + new Vector3(1.2f, 1.45f, 6.4f), spot + Vector3.Up * 1.1f);
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "husks.png"))) yield return x;
    }

    IEnumerable<int> OutfitsView()
    {
        string dir = Folder("outfits");
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in NoonLight()) yield return x;
        WorldLife life = _game.Zone.Wildlife.Life;
        var rng = new Rng((ulong)DateTime.UtcNow.Ticks);
        // One world's people in each climate (and on a hazard world, and where the air is bad): the same job, dressed for where they are.
        var climates = new (string Name, WorldConditions W)[]
        {
            ("frozen", new WorldConditions { TempC = -30f }), ("cold", new WorldConditions { TempC = 0f }), ("mild", new WorldConditions { TempC = 16f }),
            ("hot", new WorldConditions { TempC = 34f }), ("scorching", new WorldConditions { TempC = 50f }), ("hazard", new WorldConditions { TempC = 16f, Radiation = 3f }),
            ("bad air", new WorldConditions { TempC = 16f, Oxygen = 9f }),
        };
        var line = new List<Species>();
        foreach (var (name, cw) in climates)
        {
            cw.Seed = rng.NextU64();
            cw.Alienness = 0f;
            cw.Backdrop = life.World.Backdrop;
            var g = new PeopleGenerator(life.Data.People, cw);
            foreach (PersonRole r in new[] { PersonRole.Worker, PersonRole.Trader })
            {
                Species s = g.Person(rng.NextU64(), r);
                if (s.Figure.Folk != null && !s.Figure.Folk.Human) s = g.Person(rng.NextU64(), r);
                line.Add(s);
                GD.Print($"  {name} {r}: {s.Figure.Dress}, layers [{string.Join(", ", s.Figure.Layered)}], wear [{string.Join(", ", s.Figure.Wear)}], warmth {s.Figure.Warmth:F1}, worn {s.Figure.Worn:F2}");
            }
        }
        Vector3 spot = OpenSpot(16f, 10.5f);
        var stage = new Node3D { Name = "Outfits" };
        _game.Zone.AddChild(stage);
        Row(stage, line, spot, 1.05f);
        _game.Visor.Visible = false;
        Look(spot + new Vector3(0f, 1.4f, 10.5f), spot + Vector3.Up * 0.95f);
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "range.png"))) yield return x;
        Look(spot + new Vector3(-4.2f, 1.45f, 4f), spot + new Vector3(-4.2f, 1.1f, 0f));
        foreach (int x in _drive.Wait(5)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "cold-close.png"))) yield return x;
        Look(spot + new Vector3(2.6f, 1.45f, 4f), spot + new Vector3(2.6f, 1.1f, 0f));
        foreach (int x in _drive.Wait(5)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "hot-close.png"))) yield return x;
        // Humidity is regional: two regions of one world side by side, in the same heat, then the same cold.
        var one = new PeopleGenerator(life.Data.People, new WorldConditions { Seed = rng.NextU64(), Alienness = 0f, TempC = 15f, Backdrop = life.World.Backdrop });
        foreach (var (name, temp) in new[] { ("hot", 34f), ("cold", 0f) })
        {
            foreach (Node n in stage.GetChildren()) n.QueueFree();
            var pair = new List<Species>();
            foreach (float wet in new[] { 0.72f, 0.12f })
            {
                Founders f = one.FoundersOf(rng.NextU64());
                f.Folk = one.Folk;
                f.Second = null;
                f.Outsiders = 0f;
                f.Locale = new Locale { TempC = temp, Humidity = wet };
                foreach (PersonRole r in new[] { PersonRole.Worker, PersonRole.Farmer, PersonRole.Trader })
                {
                    Species s = one.Person(rng.NextU64(), r, founders: f);
                    pair.Add(s);
                    GD.Print($"  {name}, humidity {wet:F2}, {r}: {s.Figure.Dress} [{string.Join(", ", s.Figure.Wear)}] layers [{string.Join(", ", s.Figure.Layered)}] {s.Figure.Prop}");
                }
            }
            Row(stage, pair, spot, 1.15f);
            Look(spot + new Vector3(0f, 1.4f, 6.4f), spot + Vector3.Up * 0.95f);
            foreach (int x in _drive.Wait(10)) yield return x;
            foreach (int x in Shot(Path.Combine(dir, $"regions-{name}.png"))) yield return x;
        }
    }

    /// <summary>The nearest town with people, walked to; its place once it stands.</summary>
    IEnumerable<int> ToTown(Action<Place> found)
    {
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 from = _game.Survivor.WorldFeet;
        PlanetSurface.Poi.PoiSite site = _game.Zone.Planet.Pois.Where(s => s.Instance?.Townsfolk != null && s.Instance.Mood != Mood.Hostile).OrderBy(s => s.Family.Tags.Contains("settlement") ? 0 : 1).ThenByDescending(s => Math.Min(s.Instance.Townsfolk.People.Count, 12)).ThenBy(s => _game.Places.DistanceTo(s, from)).FirstOrDefault();
        if (site == null) { GD.Print("(no town on this world)"); yield break; }
        GD.Print($"(town: {site.Name}, {site.Instance.Mood}, {site.Instance.Townsfolk.People.Count} people)");
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        foreach (int x in _drive.Wait(240)) yield return x;
        found(_game.Places.PlaceOf(site));
    }

    IEnumerable<int> TownNightView()
    {
        string dir = Folder("townnight");
        Place p = null;
        foreach (int x in ToTown(q => p = q)) yield return x;
        if (p?.Memory.Town == null) yield break;
        Town town = p.Memory.Town;
        foreach (int x in NoonLight()) yield return x;
        foreach (int x in _drive.Wait(300)) yield return x;
        Vector3 c = p.ToWorld(new Vector3(town.Data.AreaCenter.X, 0f, town.Data.AreaCenter.Z));
        c.Y = _game.Zone.GroundAt(c.X, c.Z).Y;
        Vector3 eye = c + new Vector3(town.Data.AreaRadius * 0.7f, 6f, town.Data.AreaRadius * 0.7f);
        eye.Y = MathF.Max(eye.Y, _game.Zone.GroundAt(eye.X, eye.Z).Y + 6f);
        _game.Survivor.PlaceOutside(Ground(eye + (eye - c).Normalized() * 3f).ToNumerics());
        _game.Visor.Visible = false;
        Look(eye, c + Vector3.Up * 1f);
        foreach (int x in _drive.Wait(30)) yield return x;
        GD.Print($"  by day: {town.People.Count(q => !q.Indoors)} of {town.People.Count} out");
        foreach (int x in Shot(Path.Combine(dir, "day.png"))) yield return x;
        _game.Zone.Sky.TimeOfDay = Num("night", 0.86);
        foreach (int x in _drive.Wait(1500)) yield return x;
        Look(eye, c + Vector3.Up * 1f);
        GD.Print($"  at night: {town.People.Count(q => q.Indoors)} of {town.People.Count} indoors, {town.People.Count(q => !q.Indoors && q.Role == "guard")} guards out");
        foreach (int x in Shot(Path.Combine(dir, "night.png"))) yield return x;
    }

    IEnumerable<int> CheckpointView()
    {
        string dir = Folder("checkpoint");
        Place p = null;
        foreach (int x in ToTown(q => p = q)) yield return x;
        if (p?.Memory.Town == null) yield break;
        Town town = p.Memory.Town;
        foreach (int x in NoonLight()) yield return x;
        foreach (int x in _drive.Wait(200)) yield return x;
        p.Memory.Mood = Mood.Wary;
        GD.Print("(the place made wary of you, to meet its checkpoint)");
        Town.Person post = town.People.Where(q => q.Role == "guard" && !q.Dead && q.Figure.Prop == "rifle").OrderBy(q => q.Pos.DistanceTo(p.FromWorld(_game.Survivor.WorldFeet))).FirstOrDefault();
        if (post == null) { GD.Print("(no armed guard)"); yield break; }
        for (int i = 0; i < 900 && (post.Path != null && post.Step < post.Path.Count || post.Spot != post.Spawn.Spot); i++) yield return 0;
        Vector3 at = p.ToWorld(post.Pos);
        Vector3 toward = (at - _game.Survivor.WorldFeet) with { Y = 0f };
        toward = toward.LengthSquared() > 0.01f ? toward.Normalized() : Vector3.Forward;
        _game.Visor.Visible = false;
        foreach (float d in new[] { 9f, 3.5f })
        {
            Vector3 stand = Ground(at - toward * d);
            _game.Survivor.PlaceOutside((stand + Vector3.Up * 0.1f).ToNumerics());
            foreach (int x in _drive.Wait(d > 5f ? 120 : 160)) yield return x;
            Vector3 eye = stand + Vector3.Up * 1.65f - toward * 0.6f + new Vector3(-toward.Z, 0f, toward.X) * 0.8f;
            Look(eye, p.ToWorld(post.Pos) + Vector3.Up * 1.2f);
            foreach (int x in _drive.Wait(4)) yield return x;
            GD.Print($"  at {d:F1} m: {post.Act} {post.ActAmount:F2}, gun {(post.Holding ? "up" : "down")}, {town.Halts} warnings, {town.HoldAims} held");
            foreach (int x in Shot(Path.Combine(dir, d > 5f ? "warned.png" : "held.png"))) yield return x;
        }
    }

    IEnumerable<int> WheelView()
    {
        string dir = Folder("wheel");
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in NoonLight()) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        for (int i = 0; i < 14; i++)
        {
            _game.Controls.Hold(Key.Z);
            _game.Controls.Look = new Vector2(24f, -14f);
            yield return 0;
        }
        _game.Controls.Hold(Key.Z);
        GD.Print($"(wheel open {_game.WheelOpen}, on {(_game.WheelPick >= 0 ? Game.Game.Wheel[_game.WheelPick] : "none")})");
        foreach (int x in Shot(Path.Combine(dir, "wheel.png"))) yield return x;
        _game.Controls.Hold(Key.Z, false);
    }
}
