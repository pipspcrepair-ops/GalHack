using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Slice.Surface;
using Wildlife;
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
        string dir = Folder("husk");
        bool severe = _args.GetValueOrDefault("den", "severe") != "light";
        string tag = severe ? "" : "-light";
        Vector3 from = _game.Survivor.WorldFeet;
        PoiSite site = _game.Zone.Planet.Pois.Where(s => s.Instance != null && s != _game.Bunker.Poi && _game.Places.PlaceOf(s) == null && s.Instance.Townsfolk == null
                && s.Instance.Layout.Modules.Count(m => m.Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Corridor) >= 5)
            .OrderBy(s => _game.Places.DistanceTo(s, from)).First();
        site.Instance.Occupancy = severe ? new OccupancyRoll(Occupant.Turned) : new OccupancyRoll(Occupant.Empty, null, Sharing.Layers, Occupant.Turned);
        GD.Print($"(STAGING: {site.Name} [{site.Family.Id}], {_game.Places.DistanceTo(site, from):F0} m off, held by the Turned {(severe ? "alone" : "as a trace")})");
        Vector3 gate = Gate(site);
        Int3 f = site.Instance.Layout.EntranceFacing.Vec();
        Vector3 stand = gate + new Vector3(f.X, 0f, f.Z) * Num("off", 16);
        stand.Y = _game.Zone.GroundAt(stand.X, stand.Z).Y + 0.2f;
        foreach (int x in GoTo(site, stand)) yield return x;
        Place p = _game.Places.PlaceOf(site);
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
            WormMass made = sim.Masses.Where(m => !m.Dead && m.Host != null && m.Normal.Y > 0.5f).OrderByDescending(m => m.Size).FirstOrDefault();
            if (made != null)
            {
                GD.Print($"(the mass up close: {(WormMasses.RemnantKind(made) switch { 0 => "a hand", 1 => "a foot", _ => "a face" })} first)");
                foreach (int x in DenView(p, deep, made.Middle + made.Normal * made.Size * 0.2f, 1.3f, 0.75f)) yield return x;
                foreach (int x in Shot(Path.Combine(dir, "part.png"))) yield return x;
            }
            // The compass among them: the game's own view, the visor up.
            _game.FixedCamera = null;
            _game.Visor.Visible = true;
            for (int i = 0; i < 60 * 30 && _game.WrongShown < 0.35f; i++) yield return 0;
            for (int i = 0; i < 90; i++) yield return 0;
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

    /// <summary>A fixed eye inside a place on a point of its den's sim: the first spot round it, at a height, that is open and sees it; the castaway stands there too.</summary>
    IEnumerable<int> DenView(Place p, DeepSite deep, Vector3 target, float away, float height)
    {
        Vector3 at = deep.World(target);
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        Vector3 best = at + Vector3.Up * height + Vector3.Back * away;
        bool found = false;
        for (float r = away; r <= away * 1.8f && !found; r += 0.4f)
            for (int a = 0; a < 24 && !found; a++)
            {
                Vector3 eye = at + new Vector3(MathF.Cos(a * MathF.Tau / 24f) * r, height, MathF.Sin(a * MathF.Tau / 24f) * r);
                if (BlockInfo.Collides(p.Inst.Blocks.Get(p.CellAt(eye)).Type) || BlockInfo.Collides(p.Inst.Blocks.Get(p.CellAt(eye + Vector3.Down * 1.4f)).Type)) continue;
                if (space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, at, GalHack.Physics.Layers.World)).Count > 0) continue;
                best = eye;
                found = true;
            }
        GD.Print($"(eye {best.DistanceTo(at):F1} m off what it looks at{(found ? "" : ", no clear spot found")})");
        _game.Survivor.PlaceOutside((best + Vector3.Down * 1.5f).ToNumerics());
        foreach (int x in _drive.Wait(20)) yield return x;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(at - best, Vector3.Up), best);
        foreach (int x in _drive.Wait(40)) yield return x;
    }

    /// <summary>--view=husk-carrier: a settlement's people about by day, a silent carrier among them (STAGING: one in view made so if none was rolled; printed with where it stands in the frame).</summary>
    IEnumerable<int> HuskCarrier()
    {
        foreach (int x in TownView()) yield return x;
        Vector3 from = _game.Survivor.WorldFeet;
        PoiSite site = _game.Zone.Planet.Pois.Where(s => s.Instance?.Townsfolk != null).OrderBy(s => _game.Places.DistanceTo(s, from)).First();
        Place p = _game.Places.PlaceOf(site);
        Town town = p.Memory.Town;
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
