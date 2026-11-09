using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Places;
using Slice.Surface;
using Wildlife;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// A furnished room walked through (--view=furnished, with --family or --force-family for the place): its most
/// crowded dressed room, a person walked across it by the people's walking grid and an animal let loose in it
/// on the place's own creatures' sim, both going round the furniture as it really stands. The camera stands in a
/// corner at eye height; the frame is taken once both are under way (--out). Prints how far each walked and how
/// deep either body ever was in a piece (it must be 0).
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> FurnishedView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        PoiSite site = Named();
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        PartSet parts = PlaceParts.Of(p.Memory);
        // The most crowded dressed room on the way-in's level or near it.
        PlaceRoom room = p.Memory.Rooms.Values.OrderByDescending(r => (r.Plan.Placements.Count - p.Memory.DecoTaken.GetValueOrDefault(r.Module)?.Count ?? 0) / (float)(r.Room.Width * r.Room.Depth) - MathF.Abs(r.Corner.Y) * 0.05f).First();
        float floor = room.Corner.Y + room.Floor;
        Town town = Town.Walking(p.Memory);
        Species looks = _game.Zone.Wildlife.Life.People.Person(Wildlife.Rng.Mix(site.Instance.SpawnSeed, 9), PersonRole.Worker, Occupancy.Friendly);
        // Two points far across the room a person fits at, with a way between them.
        var rng = new Random((int)(site.Instance.SpawnSeed & 0x7fffffff));
        Town.Person person = null;
        Vector3 from = default, to = default;
        for (int i = 0; i < 400 && person == null; i++)
        {
            Vector3 a = new(room.Corner.X + (float)rng.NextDouble() * room.Room.Width, floor, room.Corner.Z + (float)rng.NextDouble() * room.Room.Depth);
            Vector3 b = new(room.Corner.X + (float)rng.NextDouble() * room.Room.Width, floor, room.Corner.Z + (float)rng.NextDouble() * room.Room.Depth);
            if (new Vector2(a.X - b.X, a.Z - b.Z).Length() < MathF.Max(room.Room.Width, room.Room.Depth) * 0.6f) continue;
            if (!parts.Fits(a, PlaceParts.PersonRadius, PlaceParts.PersonHeight) || !parts.Fits(b, PlaceParts.PersonRadius, PlaceParts.PersonHeight) || !town.CanStand(a) || !town.CanStand(b)) continue;
            if (parts.Sweep(a, b, PlaceParts.PersonRadius, PlaceParts.PersonHeight)) continue; // furniture across the straight way
            person = town.Walker(looks, a, b);
            (from, to) = (a, b);
        }
        GD.Print($"FURNISHED {site.Family.Id}: room {room.Room.RoomId} {room.Room.Width}x{room.Room.Depth}, {room.Plan.Placements.Count} pieces, {parts.Count} parts in the place; person {(person != null ? $"from {from} to {to}, {person.Path.Count} points" : "none")}");
        var stage = new Node3D { Name = "Furnished" };
        p.AddChild(stage);
        HumanRig rig = null;
        PersonView view = null;
        if (person != null)
        {
            rig = new HumanRig(looks);
            view = new PersonView { Name = "Walker" };
            stage.AddChild(view);
            view.Build(rig);
        }
        // The animal: the place's own creatures' sim with only it in it, small enough to go under a table.
        Creature beast = null;
        DeepSite deep = p.Memory.Fauna?.Sim is { } fsim ? _game.Zone.Wildlife.Sites.FirstOrDefault(s => s.Sim == fsim) : null;
        if (deep != null)
        {
            foreach (Creature c in deep.Sim.Creatures.ToList()) deep.Sim.Remove(c);
            Species pet = Enumerable.Range(0, 30).Select(n => _game.Zone.Wildlife.Life.Domestic(DomesticKind.Pet, n)).Where(s => s != null && !s.Flies).OrderBy(s => s.BodyHeight).First();
            BlockSpace space = PlaceFauna.SpaceOf(p.Memory);
            for (int i = 0; i < 200 && beast == null; i++)
            {
                Vector3 a = new(room.Corner.X + (float)rng.NextDouble() * room.Room.Width, floor + 0.05f, room.Corner.Z + (float)rng.NextDouble() * room.Room.Depth);
                if (!parts.Fits(a, SpaceNav.PartRadius(pet), SpaceNav.PartHeight(pet))) continue;
                beast = deep.Sim.SpawnIn(pet, space, a);
            }
            GD.Print($"FURNISHED animal: {(beast != null ? $"{pet.CommonName} {pet.BodyHeight:F2} m tall at {beast.Position}" : "none")}");
        }
        else GD.Print("FURNISHED: the place has no creatures' sim; no animal");
        _game.Zone.Wildlife.Frozen = false;
        // The eye in the corner of the room the walk starts nearest, looking across it.
        var size = new Vector3(room.Room.Width, 0f, room.Room.Depth);
        Vector3 corner = new(room.Corner.X, floor, room.Corner.Z), middle = corner + size * 0.5f;
        Vector3 eyeLocal = new[] { corner + new Vector3(0.4f, 0f, 0.4f), corner + new Vector3(size.X - 0.4f, 0f, 0.4f), corner + new Vector3(0.4f, 0f, size.Z - 0.4f), corner + new Vector3(size.X - 0.4f, 0f, size.Z - 0.4f) }
            .OrderByDescending(c => (c - middle).Length() - (c - to).Length() * 0.3f).First() + Vector3.Up * 1.65f;
        _game.Survivor.PlaceOutside(p.ToWorld(eyeLocal + Vector3.Down * 1.65f + (middle - eyeLocal).Normalized() * -0.2f).ToNumerics());
        _game.Visor.Visible = false;
        _game.Torch.Visible = true;
        float walked = 0f, worstPerson = 0f, worstBeast = 0f, beastWalked = 0f, clock = 0f;
        Vector3 beastAt = beast?.Position ?? default;
        bool shot = false;
        for (int i = 0; i < 2400 && !shot; i++)
        {
            float dt = 1f / 60f;
            clock += dt;
            _game.FixedCamera = new Transform3D(Basis.LookingAt((p.ToWorld(middle + Vector3.Up * 0.7f) - p.ToWorld(eyeLocal)).Normalized(), Vector3.Up), p.ToWorld(eyeLocal));
            if (person != null && i > 60)
            {
                Vector3 was = person.Pos;
                bool there = town.StepWalker(person, dt);
                walked += new Vector2(person.Pos.X - was.X, person.Pos.Z - was.Z).Length();
                worstPerson = MathF.Max(worstPerson, -parts.Gap(person.Pos, Town.RadiusOf(person), Town.HeightOf(person)));
                rig.Drive(new Gesture { Feet = person.Pos, Yaw = person.Yaw, Speed = person.Now, Walked = person.Gait, Clock = clock });
                view.Sync();
                if (there) person.Path = town.Walker(looks, person.Pos, person.Pos.DistanceTo(from) < 0.5f ? to : from)?.Path ?? person.Path;
            }
            if (beast != null)
            {
                beastWalked += new Vector2(beast.Position.X - beastAt.X, beast.Position.Z - beastAt.Z).Length();
                beastAt = beast.Position;
                if (!beast.Falling) worstBeast = MathF.Max(worstBeast, -parts.Gap(beast.Position, SpaceNav.PartRadius(beast.Species), SpaceNav.PartHeight(beast.Species)));
            }
            yield return 0;
            // Both under way and the person well into the room: the frame.
            if (i > 420 && walked > 2.5f && person != null && person.Now > 0.5f && (beast == null || beastWalked > 1f))
            {
                GD.Print($"FURNISHED person walked {walked:F1} m, deepest in a piece {MathF.Max(0f, worstPerson):F3} m; animal walked {beastWalked:F1} m, deepest in a piece {MathF.Max(0f, worstBeast):F3} m; person at {person.Pos}, animal at {beast?.Position}");
                foreach (int x in Shot(System.IO.Path.Combine(_args.GetValueOrDefault("dir", "."), "furnished-walk.png"))) yield return x;
                shot = true;
            }
        }
        if (!shot) GD.Print($"FURNISHED no frame: person walked {walked:F1} m, animal {beastWalked:F1} m");
        _game.Zone.Wildlife.Frozen = true;
    }
}
