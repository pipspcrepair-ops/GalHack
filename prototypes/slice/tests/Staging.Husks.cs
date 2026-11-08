using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Slice.Surface;
using Wildlife;
using GalHack.Physics.Engine;
using WeatherKind = Weather.Model.WeatherKind;
using BlockInfo = PoiGenerator.Gen.BlockInfo;
using Int3 = PoiGenerator.Gen.Int3;
using ModuleKind = PoiGenerator.Gen.ModuleKind;
using Occupant = PoiGenerator.Gen.Occupant;
using OccupancyRoll = PoiGenerator.Gen.OccupancyRoll;
using Place = Slice.Places.Place;
using PoiSite = PlanetSurface.Poi.PoiSite;
using Sharing = PoiGenerator.Gen.Sharing;
using Town = Slice.Places.Town;

namespace Slice.Tests;

public sealed partial class Staging
{
    /// <summary>
    /// --view=husk-grab: a Husk seizes the castaway by the head, seen through their own eyes (the
    /// game's camera, its close-up and the struggle as they are in play); then one shot from the
    /// survival tool, which goes into it and breaks the hold. Frames: grab.png, then shot.png.
    /// </summary>
    IEnumerable<int> HuskGrab()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string dir = Folder("husk");
        foreach (int x in Play.Tool(Slice.Player.ToolMode.Shoot)) yield return x;
        WildlifePatch patch = _game.Zone.Wildlife;
        patch.Frozen = false;
        Vector3 feet = _game.Survivor.WorldFeet;
        foreach (Creature other in patch.Sim.Creatures) if (patch.WorldOf(other).DistanceTo(feet) < 120f) other.Removed = true;
        foreach (DeepSite s in patch.Sites) foreach (Creature other in s.Sim.Creatures) other.Removed = true;
        _game.Suit.Sim.Player.Gear.HelmetOn = _args.GetValueOrDefault("helmet", "sealed") != "open";
        WorldLife life = patch.Life;
        // One of this world's Husks if it has them; else one of its people, taken.
        List<Species> hosts = life.Moving.FirstOrDefault(s => s.Husk)?.Hosts
            ?? Enumerable.Range(0, 6).Select(i => life.People.Person((ulong)Random.Shared.NextInt64(), (PersonRole)(i % 7), Occupancy.Friendly, 0.2f, husk: true)).ToList();
        // One tall enough to seize a head, whose face shows.
        Species husk = hosts.OrderBy(h => h.Figure.Height + Seizing.ReachAbove >= Seizing.StandingHead ? 0 : 1).ThenBy(h => h.Figure.Wear.Any(w => HumanBody.Slot(w) == "head") ? 1 : 0).First();
        Vector3 look = _game.Survivor.WorldLook();
        look = new Vector3(look.X, 0f, look.Z).Normalized();
        Creature c = patch.Sim.Spawn(husk, patch.PatchLocal(Ground(feet + look * 1.4f)));
        c.Yaw = MathF.Atan2(look.X, look.Z);
        c.Rig.Reset(patch.Sim, c);
        _game.Visor.Visible = false;
        foreach (int x in _drive.Wait(10)) yield return x;
        Seizing.Begin(patch.Sim, c, patch.Player);
        foreach (int x in _drive.Wait(55)) yield return x;
        GD.Print($"(held by a husk: {_game.HeldBy != null}, close-up {_game.CloseUp:F2}, struggle {_game.Struggle:F2}, health {_game.Suit.HealthShare * 100:F0}%)");
        foreach (int x in Shot(Path.Combine(dir, "grab.png"))) yield return x;
        // One shot: it can't miss, and it breaks the hold.
        _game.Controls.Primary = true;
        _game.Controls.PrimaryPressed = true;
        yield return 0;
        _game.Controls.Primary = false;
        foreach (int x in Shot(Path.Combine(dir, "shot.png"))) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        GD.Print($"(after the shot: held {_game.HeldBy != null}, holds shot off {_game.ShotsFree}, the husk {WildSim.Flat(patch.WorldOf(c) - _game.Survivor.WorldFeet).Length():F1} m off, its health {c.Health:F0})");
        foreach (int x in Shot(Path.Combine(dir, "after.png"))) yield return x;
    }

    /// <summary>
    /// --view=husk-den [--den=severe|light]: a Husk den by day, at the nearest abandoned place with
    /// rooms enough (STAGING: its occupancy set to the Turned before it stands, alone for a severe den,
    /// only a trace for a light one; printed). Frames: outside.png (its way in from the open: nothing
    /// to see), then inside severe.png and part.png (a mass with what is left of someone in it) or
    /// light.png, and compass.png (the visor among them, the compass off true).
    /// </summary>
    IEnumerable<int> HuskDen()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        // --dir for the frames, so --out stays the closing frame's file.
        string dir = _args.GetValueOrDefault("dir", "husk");
        Directory.CreateDirectory(dir);
        bool severe = _args.GetValueOrDefault("den", "severe") != "light";
        string tag = severe ? "" : "-light";
        Vector3 from = _game.Survivor.WorldFeet;
        PoiSite site = _game.Zone.Planet.Pois.Where(s => s.Instance != null && s != _game.Bunker.Poi && _game.Places.PlaceOf(s) == null && s.Instance.Townsfolk == null
                && s.Instance.Layout.Modules.Count(m => m.Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Corridor) >= (int)Num("rooms", 9) && !s.Family.Tags.Contains("camp")
                && s.Instance.State is PoiGenerator.Gen.PoiState.Abandoned or PoiGenerator.Gen.PoiState.Ruined)
            .OrderBy(s => _game.Places.DistanceTo(s, from)).First(s => _game.Places.DistanceTo(s, from) > 250f && !_game.Places.MemoryOf(s).Prepared);
        site.Instance.Occupancy = severe ? new OccupancyRoll(Occupant.Turned) : new OccupancyRoll(Occupant.Empty, null, Sharing.Layers, Occupant.Turned);
        GD.Print($"(STAGING: {site.Name} [{site.Family.Id}], {_game.Places.DistanceTo(site, from):F0} m off, held by the Turned {(severe ? "alone" : "as a trace")})");
        Vector3 gate = Gate(site);
        Int3 f = PoiGenerator.Gen.Dirs.Vec(site.Instance.Layout.EntranceFacing);
        Vector3 stand = gate + new Vector3(f.X, 0f, f.Z) * (float)Num("off", 16);
        stand.Y = _game.Zone.GroundAt(stand.X, stand.Z).Y + 0.2f;
        foreach (int x in GoTo(site, stand)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        foreach (int x in Daylight()) yield return x;
        for (int i = 0; i < 600 && _game.Zone.Wildlife.Sites.All(s => s.Sim != p.Memory.Fauna?.Sim); i++) yield return 0;
        DeepSite deep = _game.Zone.Wildlife.Sites.FirstOrDefault(s => s.Sim == p.Memory.Fauna?.Sim);
        if (deep == null) throw new InvalidOperationException("the den's creatures never stood");
        WildSim sim = deep.Sim;
        GD.Print($"(the den: severity {p.Memory.Fauna.Population.Severity:F2}, {sim.Creatures.Count(c => c.Species.Husk)} Husks, {sim.Masses.Count} masses ({sim.Masses.Count(m => m.Host != null)} with someone in them), {sim.Veins.Count} veins)");
        _game.Visor.Visible = false;
        foreach (int x in _drive.LookAt(gate + Vector3.Up * 2f, 30)) yield return x;
        foreach (int x in _drive.Wait(90)) yield return x;
        GD.Print($"(outside: wrongness {_game.Wrongness:F2}, shown {_game.WrongShown:F2})");
        foreach (int x in Shot(Path.Combine(dir, $"outside{tag}.png"))) yield return x;

        // Inside: what there is to see.
        _game.Torch.Visible = true;
        if (severe)
        {
            WormMass big = sim.Masses.Where(m => !m.Dead).OrderByDescending(m => sim.Veins.Count(v => v.Points[0].DistanceTo(m.Position) < 2f) + m.Size * 4f).First();
            foreach (int x in DenView(p, deep, big.Middle, 3.2f, 1.5f)) yield return x;
            foreach (int x in Shot(Path.Combine(dir, "severe.png"))) yield return x;
            // One on a wall: rooted in it, its open side out into the room.
            WormMass wall = sim.Masses.Where(m => !m.Dead && MathF.Abs(m.Normal.Y) < 0.4f).OrderByDescending(m => m.Size).FirstOrDefault();
            if (wall != null)
            {
                GD.Print($"(a mass on a wall: facing {wall.Normal.X:F2}, {wall.Normal.Y:F2}, {wall.Normal.Z:F2}; {sim.Masses.Count(m => MathF.Abs(m.Normal.Y) < 0.4f)} of {sim.Masses.Count} are on walls)");
                foreach (int x in DenView(p, deep, wall.Middle, 2.6f, 0.3f)) yield return x;
                foreach (int x in Shot(Path.Combine(dir, "wall.png"))) yield return x;
            }
            WormMass made = sim.Masses.Where(m => !m.Dead && m.Host != null && m.Normal.Y > 0.5f).OrderBy(m => WormMasses.RemnantKind(m) == 0 ? 0 : 1).ThenByDescending(m => m.Size).FirstOrDefault();
            if (made != null)
            {
                int cleared = 0;
                foreach (Creature c in sim.Creatures)
                    if (!c.Species.Husk && c.Position.DistanceTo(made.Position) < 4f) { c.Removed = true; cleared++; }
                if (cleared > 0) GD.Print($"(STAGING: {cleared} of the place's other creatures by that mass taken out of the shot)");
                GD.Print($"(the mass up close: {(WormMasses.RemnantKind(made) switch { 0 => "a hand", 1 => "a foot", _ => "a face" })} first)");
                foreach (int x in DenView(p, deep, made.Middle + made.Normal * made.Size * 0.2f, 1.3f, 0.75f)) yield return x;
                foreach (int x in Shot(Path.Combine(dir, "part.png"))) yield return x;
            }
            // The compass among them: the game's own view, the visor up.
            _game.FixedCamera = null;
            HideCastaway(false);
            _game.Visor.Visible = true;
            foreach (int x in _drive.LookAt(deep.World(big.Middle), 20)) yield return x;
            for (int i = 0; i < 60 * 30 && _game.WrongShown < 0.35f; i++) yield return 0;
            // When it has wandered well off true (it comes and goes).
            for (int i = 0; i < 60 * 40 && MathF.Abs(_game.CompassDrift) < 22f; i++) yield return 0;
            Vector3 look = _game.Survivor.WorldLook();
            GD.Print($"(the compass: wrongness {_game.Wrongness:F2}, shown {_game.WrongShown:F2}, true heading {Mathf.PosMod(Mathf.RadToDeg(MathF.Atan2(look.X, -look.Z)), 360f):F0}, drifted {_game.CompassDrift:+0;-0} degrees, {_game.WrongOnsets} onsets)");
            foreach (int x in Shot(Path.Combine(dir, "compass.png"))) yield return x;
        }
        else
        {
            Vein faint = sim.Veins.FirstOrDefault();
            Creature husk = sim.Creatures.FirstOrDefault(c => c.Species.Husk);
            Vector3 at = faint != null ? faint.Points[faint.Points.Count / 2] : husk?.Position ?? Vector3.Zero;
            foreach (int x in DenView(p, deep, at + Vector3.Up * 0.1f, 2.4f, 1.4f)) yield return x;
            GD.Print($"(light inside: {sim.Veins.Count} veins, the Husk {(husk == null ? "none" : $"{husk.Position.DistanceTo(at):F1} m from the vein")}, wrongness {_game.Wrongness:F2})");
            foreach (int x in Shot(Path.Combine(dir, "light.png"))) yield return x;
        }
    }

    /// <summary>
    /// --view=husk-worms: walking up to a worm mass in a severe den, through the castaway's eyes:
    /// calm.png from a few metres, eager.png a step out of its reach (the worms reaching for you),
    /// held.png once they have you (hauled in, stung: printed), then a shot into it, free.png.
    /// STAGING: the den is set up as husk-den's; the place's creatures stand still.
    /// </summary>
    IEnumerable<int> HuskWorms()
    {
        _args["den"] = "severe";
        string dir = _args.GetValueOrDefault("dir", "husk");
        Directory.CreateDirectory(dir);
        _args["dir"] = Path.Combine(dir, "den");
        // The den, its frames to a side folder; then the walk up.
        foreach (int x in HuskDen()) yield return x;
        _game.FixedCamera = null;
        HideCastaway(false);
        _game.Visor.Visible = false;
        DeepSite deep = _game.Zone.Wildlife.Sites.OrderBy(s => s.GlobalPosition.DistanceTo(_game.Survivor.WorldFeet)).First(s => s.Sim.Masses.Count > 0);
        HuskRules rules = _game.Zone.Wildlife.Life.Data.Husks;
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        // A floor mass with open floor before it, at a man's eye looking down on it.
        WormMass m = null;
        Vector3 back = Vector3.Zero;
        foreach (WormMass c in deep.Sim.Masses.Where(c => !c.Dead && c.Normal.Y > 0.9f).OrderByDescending(c => c.Size))
        {
            for (int a = 0; a < 12 && m == null; a++)
            {
                var d = new Vector3(MathF.Cos(a * MathF.Tau / 12f), 0f, MathF.Sin(a * MathF.Tau / 12f));
                Vector3 eye = deep.World(c.Position + d * 4.5f) + Vector3.Up * 1.6f;
                if (space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, deep.World(c.Middle), GalHack.Physics.Layers.World)).Count == 0
                    && space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye + Vector3.Down * 1.4f, deep.World(c.Position) + Vector3.Up * 0.2f, GalHack.Physics.Layers.World)).Count == 0) { m = c; back = d; }
            }
            if (m != null) break;
        }
        if (m == null) throw new InvalidOperationException("no floor mass with open floor before it");
        float grab = WormMasses.Grab(rules, m);
        foreach (Creature c in deep.Sim.Creatures) if (c.Position.DistanceTo(m.Position) < 6f) c.Removed = true;
        GD.Print($"(STAGING: the creatures by the mass taken out of the shot; it reaches {grab:F1} m)");
        IEnumerable<int> StandAt(float off, int wait)
        {
            _game.Survivor.PlaceOutside(deep.World(m.Position + back * off).ToNumerics());
            for (int i = 0; i < wait; i++)
            {
                _drive.Face(deep.World(m.Middle) + Vector3.Up * 0.3f);
                yield return 0;
            }
        }
        foreach (int x in StandAt(4.5f, 120)) yield return x;
        GD.Print($"(calm: eager {m.Eager:F2}, stir {m.Stir:F2})");
        foreach (int x in Shot(Path.Combine(dir, "calm.png"))) yield return x;
        foreach (int x in StandAt(grab + 0.7f, 75)) yield return x;
        GD.Print($"(a step out of reach: eager {m.Eager:F2}, stir {m.Stir:F2}, held {_game.InWorms})");
        foreach (int x in Shot(Path.Combine(dir, "eager.png"))) yield return x;
        double health = _game.Suit.HealthShare;
        foreach (int x in StandAt(grab - 0.25f, 2)) yield return x;
        float startAt = WildSim.Flat(_game.Survivor.WorldFeet - deep.World(m.Middle)).Length();
        for (int i = 0; i < 120; i++)
        {
            _drive.Face(deep.World(m.Middle) + Vector3.Up * 0.3f);
            yield return 0;
        }
        float nowAt = WildSim.Flat(_game.Survivor.WorldFeet - deep.World(m.Middle)).Length();
        GD.Print($"(held: {_game.InWorms}, hauled {startAt:F2} to {nowAt:F2} m in 2 s ({_game.WormHauled:F2} m all told), health {health * 100:F1}% to {_game.Suit.HealthShare * 100:F1}%)");
        if (!_game.InWorms || nowAt >= startAt - 0.3f || _game.Suit.HealthShare >= health) throw new InvalidOperationException("the mass did not hold, haul and sting the castaway");
        foreach (int x in Shot(Path.Combine(dir, "held.png"))) yield return x;
        foreach (int x in Play.Tool(Slice.Player.ToolMode.Shoot)) yield return x;
        _drive.Face(deep.World(m.Middle));
        yield return 0;
        _game.Controls.Primary = true;
        _game.Controls.PrimaryPressed = true;
        yield return 0;
        _game.Controls.Primary = false;
        for (int i = 0; i < 10; i++) yield return 0;
        GD.Print($"(a shot into it: its health {m.Health:F0} of {m.MaxHealth:F0}, held {_game.InWorms})");
        if (_game.InWorms) throw new InvalidOperationException("a shot into the mass did not make it let go");
        foreach (int x in Shot(Path.Combine(dir, "free.png"))) yield return x;
    }

    /// <summary>Full day where the castaway stands (the clock is the world's, the sun the place's): the time of day with the sun highest, printed.</summary>
    IEnumerable<int> Daylight()
    {
        double best = _game.Zone.Sky.TimeOfDay;
        float high = -2f;
        for (double t = 0; t < 1; t += 0.05)
        {
            _game.Zone.Sky.TimeOfDay = t;
            yield return 0;
            yield return 0;
            if (_game.Zone.Sky.ToSun.Y > high) { high = _game.Zone.Sky.ToSun.Y; best = t; }
        }
        _game.Zone.Sky.TimeOfDay = best - 0.05; // a little before noon, so shadows have a length
        GD.Print($"(STAGING: the clock set to {best - 0.05:F2}, the sun {Mathf.RadToDeg(MathF.Asin(Math.Clamp(high, -1f, 1f))):F0} degrees up at noon)");
        foreach (int x in _drive.Wait(10)) yield return x;
    }

    /// <summary>The castaway's own body, hidden for a staged eye that would have them in the shot.</summary>
    void HideCastaway(bool hide)
    {
        if (_game.Survivor.Node.GetNodeOrNull<Node3D>("Castaway") is { } body) body.Visible = !hide;
    }

    /// <summary>The floor under a point of a den's sim (its own solid space), or the point's height if none is found.</summary>
    static float SpaceFloor(DeepSite deep, Vector3 p) =>
        deep.Sim.Creatures.FirstOrDefault(c => c.Space != null)?.Space is { } s && SpaceMath.FloorBelow(s, p.X, p.Y + 0.3f, p.Z, 3f) is float y ? y : p.Y;

    /// <summary>A fixed eye inside a place on a point of its den's sim: the first spot round it, at a height, that is open and sees it; the castaway stands there too.</summary>
    IEnumerable<int> DenView(Place p, DeepSite deep, Vector3 target, float away, float height)
    {
        Vector3 at = deep.World(target);
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        Vector3 best = at + Vector3.Up * height + Vector3.Back * away;
        bool found = false;
        for (float r = away; r <= away * 2.6f && !found; r += 0.3f)
            for (int a = 0; a < 24 && !found; a++)
            {
                Vector3 eye = at + new Vector3(MathF.Cos(a * MathF.Tau / 24f) * r, height + (r - away) * 0.4f, MathF.Sin(a * MathF.Tau / 24f) * r);
                if (BlockInfo.Collides(p.Inst.Blocks.Get(p.CellAt(eye)).Type)) continue;
                if (space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, at, GalHack.Physics.Layers.World)).Count > 0) continue;
                best = eye;
                found = true;
            }
        GD.Print($"(eye {best.DistanceTo(at):F1} m off what it looks at{(found ? "" : ", no clear spot found")})");
        // The castaway stands just behind the eye (else beside it), out of the shot, their torch on what it sees.
        Vector3 back = new Vector3(best.X - at.X, 0f, best.Z - at.Z).Normalized();
        Vector3 feet = best with { Y = at.Y - (target.Y - SpaceFloor(deep, target)) };
        foreach (Vector3 off in new[] { back * 1.1f, new Vector3(-back.Z, 0f, back.X) * 1.1f, new Vector3(back.Z, 0f, -back.X) * 1.1f, Vector3.Zero })
            if (!BlockInfo.Collides(p.Inst.Blocks.Get(p.CellAt(feet + off + Vector3.Up * 1.2f)).Type)) { feet += off; break; }
        _game.Survivor.PlaceOutside((feet + Vector3.Up * 0.1f).ToNumerics());
        foreach (int x in _drive.Wait(20)) yield return x;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(at - best, Vector3.Up), best);
        HideCastaway(true);
        foreach (int x in _drive.Wait(40)) yield return x;
    }

    /// <summary>--view=husk-carrier: a settlement's people about by day, a silent carrier among them (STAGING: one in view made so if none was rolled; printed with where it stands in the frame).</summary>
    IEnumerable<int> HuskCarrier()
    {
        Vector3 from = _game.Survivor.WorldFeet;
        // A living town at peace: its people out about it.
        PoiSite site = _game.Zone.Planet.Pois.Where(s => s.Instance?.Townsfolk != null && s.Instance.State == PoiGenerator.Gen.PoiState.Active && s.Instance.Mood != PoiGenerator.Gen.Mood.Hostile)
            .OrderBy(s => _game.Places.DistanceTo(s, from)).First();
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        foreach (int x in Daylight()) yield return x;
        Place p = _game.Places.PlaceOf(site);
        Town town = p.Memory.Town;
        foreach (int x in _drive.Wait(600)) yield return x;
        // A street with people on it: the one with most others near, seen from a few metres off (the town view's way).
        var people = town.People.Where(q => !q.Indoors && !q.Dead && !q.Gone).ToList();
        GD.Print($"({site.Name}: {people.Count} of {town.People.Count} out)");
        Town.Person busy = people.OrderByDescending(q => people.Count(o => o.Pos.DistanceTo(q.Pos) < 12f)).First();
        Vector3 mid = p.ToWorld(busy.Pos);
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        int Seen(Vector3 eye) => people.Count(o => space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, p.ToWorld(o.Pos) + Vector3.Up * 1.2f, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull)).Count == 0 && p.ToWorld(o.Pos).DistanceTo(eye) < 30f);
        Vector3 best = mid + Vector3.Back * 6f;
        int most = -1;
        for (float r = 5f; r <= 15f; r += 2.5f)
            for (int a = 0; a < 16; a++)
            {
                Vector3 c = mid + new Vector3(MathF.Cos(a * MathF.Tau / 16f), 0f, MathF.Sin(a * MathF.Tau / 16f)) * r;
                c.Y = _game.Zone.GroundAt(c.X, c.Z).Y + 1.65f;
                int n = Seen(c);
                if (n > most) { most = n; best = c; }
            }
        _game.Visor.Visible = false;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(mid + Vector3.Up * 1.1f - best, Vector3.Up), best);
        foreach (int x in _drive.Wait(30)) yield return x;
        Camera3D cam = _game.Camera;
        Town.Person carrier = town.People.FirstOrDefault(q => q.Carrier && !q.Gone && !q.Indoors && !cam.IsPositionBehind(p.ToWorld(q.Pos)));
        if (carrier == null)
        {
            carrier = town.People.Where(q => !q.Dead && !q.Gone && !q.Indoors && q.Looks.Figure.Stage != LifeStage.Child && !cam.IsPositionBehind(p.ToWorld(q.Pos)))
                .OrderBy(q => p.ToWorld(q.Pos).DistanceTo(cam.GlobalPosition)).First();
            carrier.Carrier = true;
            GD.Print("(STAGING: none of those in view was rolled a carrier; the nearest grown one is made one)");
        }
        Vector2 onScreen = cam.UnprojectPosition(p.ToWorld(carrier.Pos) + Vector3.Up);
        GD.Print($"(the carrier: the {carrier.Role}, at {onScreen.X:F0},{onScreen.Y:F0} in the frame; {town.CarriersAtStart} were rolled here, {town.Carrying} carry now; it looks like the rest: husk {carrier.Looks.Husk}, torn {carrier.Figure.Torn})");
        foreach (int x in _drive.Wait(10)) yield return x;
    }
}
