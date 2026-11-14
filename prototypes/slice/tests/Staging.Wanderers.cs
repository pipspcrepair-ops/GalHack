using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Slice.Surface;
using Wildlife;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

public sealed partial class Staging
{
    /// <summary>
    /// --view=wanderers [--kind=name] [--friendly=FILE]: bands of the world's people met out in the
    /// open by day, seen past the sleeper. First a friendly band on its way past you (heads turned,
    /// a wave), saved to --friendly; then a wary one stopped ahead of you, its leader holding up a
    /// warning (the view's own frame, --out). STAGING: each band is put down near you with its
    /// stance set (printed); everything else near is cleared.
    /// </summary>
    IEnumerable<int> WanderersView()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 stand = FlatSpot(_game.Survivor.WorldFeet, (float)Num("off", 120));
        _game.Survivor.PlaceOutside((stand + Vector3.Up * 0.2f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in Daylight()) yield return x;
        // Facing the openest way: the fewest plants ahead.
        Vector3 from = _game.Survivor.WorldFeet, way = Vector3.Forward;
        int fewest = int.MaxValue;
        for (int k = 0; k < 8; k++)
        {
            var dir = new Vector3(MathF.Cos(k * MathF.Tau / 8f), 0f, MathF.Sin(k * MathF.Tau / 8f));
            int n = _game.Zone.Streamer.FloraNear(from + dir * 12f, 14f).Count();
            if (n < fewest) { fewest = n; way = dir; }
        }
        foreach (int x in _drive.LookAt(from + way * 20f + Vector3.Up * 1.5f, 20)) yield return x;
        WildlifePatch patch = _game.Zone.Wildlife;
        patch.Frozen = false;
        GD.Print($"(a weapon in hand: {patch.Player.Drawn})");
        _game.Visor.Visible = false;
        foreach (int x in MeetBand(patch, Stance.Welcoming, _args.GetValueOrDefault("friendly", "wanderers-friendly.png"))) yield return x;
        foreach (int x in MeetBand(patch, Stance.Wary, null)) yield return x;
    }

    /// <summary>The world's people out in the open, a kind to stage: asked for by name, else (for a wary band) one that goes armed.</summary>
    Species BandKind(WorldLife life, bool armed)
    {
        var kinds = life.Signature.Kinds.Select(k => k.Species).Concat(life.RuinCast.Select(r => r.Species)).Append(life.Settlers)
            .Where(s => s is { Behaviour: Behaviour.Folk } && s.Figure.Occupancy == Occupancy.Friendly).ToList();
        string want = _args.GetValueOrDefault("kind", "");
        bool Armed(Species s) => (s.Hosts ?? new List<Species> { s }).Count(h => Weapons.Armed(h.Figure.Prop)) >= 2;
        Species pick = kinds.OrderByDescending(s => want != "" && s.CommonName.Contains(want, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(s => Armed(s) == armed ? 1 : 0).FirstOrDefault();
        if (pick != null) return pick;
        // Nobody lives here: some of the world's people anyway (STAGING).
        var hosts = Enumerable.Range(0, 5).Select(i => life.People.Person(Wildlife.Rng.Mix(life.World.Seed, 900 + (ulong)i), i % 2 == 0 ? PersonRole.Guard : PersonRole.Farmer)).ToList();
        hosts[0].Hosts = hosts;
        GD.Print("(STAGING: no people of the world's own live here; a band of its people made for the frame)");
        return hosts[0];
    }

    IEnumerable<int> MeetBand(WildlifePatch patch, Stance stance, string file)
    {
        bool wary = Temper.LeaningOf(stance) == Leaning.Wary;
        Vector3 feet = _game.Survivor.WorldFeet;
        foreach (Creature c in patch.Sim.Creatures) if (patch.WorldOf(c).DistanceTo(feet) < 90f) c.Removed = true;
        Vector3 look = _game.Survivor.WorldLook();
        Vector3 fwd = new Vector3(look.X, 0f, look.Z).Normalized(), side = fwd.Cross(Vector3.Up).Normalized();
        Species s = BandKind(patch.Life, wary);
        // STAGING: the plants in the way felled, so the frame shows the band and not a trunk.
        int felled = 0;
        for (float t = 0f; t <= 26f; t += 2f)
            foreach (var plant in _game.Zone.Streamer.FloraNear(feet + fwd * t, 7f).ToList())
                if (_game.Zone.Planet.Felled.Add(plant.Id)) { _game.Zone.Streamer.Fell(plant.Id); felled++; }
        GD.Print($"(STAGING: {felled} plants between you and where the band goes felled)");
        // Friendly: off to one side ahead, on their way across in front of you. Wary: ahead, inside their warning.
        Vector3 at = Ground(feet + fwd * (wary ? 23f : 9f) + side * (wary ? 0f : 6f));
        List<Creature> band = patch.Sim.SpawnGroup(s, patch.PatchLocal(at), 4);
        if (band.Count == 0)
        {
            // Ground a band wouldn't be put down on by itself (near the bunker's clearance): put there by hand (STAGING).
            var herd = new Herd { Home = patch.PatchLocal(at), Goal = patch.PatchLocal(at), Center = patch.PatchLocal(at), Spread = 3f, Party = Party.Roll(s, Wildlife.Rng.Mix(s.Seed, 77)) };
            patch.Sim.Herds.Add(herd);
            for (int i = 0; i < 4; i++) band.Add(patch.Sim.Spawn(s, patch.PatchLocal(Ground(at + side * (i - 1.5f) * 1.6f + fwd * (i % 2) * 1.2f)), herd));
            GD.Print("(STAGING: the band set down by hand: the ground here is inside a clearance no band is put down in)");
        }
        Herd h = band[0].Herd;
        Party party = h.Party;
        party.Temper.Stance = stance;
        // On their way across in front of you; or (wary) where they stand.
        Vector3 bound = patch.PatchLocal(wary ? at : Ground(feet + fwd * 5f - side * 14f));
        h.Home = bound;
        h.Goal = bound;
        h.GoalTimer = 60f;
        foreach (Creature c in band) c.Home = bound;
        GD.Print($"(STAGING: {band.Count} of {s.CommonName} put down {at.DistanceTo(feet):F0} m off, their stance set {stance})");
        // Your eyes on them, a little to one side: not a gun on anyone. Up to a wary band: a few steps toward them.
        foreach (int x in _drive.LookAt(at + side * 3f + Vector3.Up * 1.2f, 20)) yield return x;
        if (wary)
        {
            foreach (int x in _drive.WalkTo(feet + fwd * 8f, 0.5f, 12f, sprint: false)) yield return x;
            foreach (int x in _drive.LookAt(at + side * 3f + Vector3.Up * 1.2f, 10)) yield return x;
        }
        feet = _game.Survivor.WorldFeet;
        Creature lead = Brain.Leader(h);
        int held = 0;
        for (int i = 0; i < 60 * 16; i++)
        {
            bool moment = wary ? lead.Act == BodyAct.Warn : party.Greeted && band.Any(c => c.Act is BodyAct.Greet or BodyAct.Beckon or BodyAct.Present);
            held = moment ? held + 1 : 0;
            if (held > (wary ? 45 : 25)) break;
            yield return 0;
        }
        Vector3 mid = band.Where(c => !c.Removed).Aggregate(Vector3.Zero, (a, c) => a + patch.WorldOf(c)) / band.Count(c => !c.Removed);
        Vector3 to = new Vector3(mid.X - feet.X, 0f, mid.Z - feet.Z).Normalized(), right = to.Cross(Vector3.Up).Normalized();
        // From behind you onto a band going by; side-on to a wary one, you and them both, its leader's hand plain.
        float apart = new Vector3(mid.X - feet.X, 0f, mid.Z - feet.Z).Length();
        if (wary) Look(feet + to * apart * 0.55f + right * apart * 0.55f + Vector3.Up * 1.7f, feet.Lerp(mid, 0.6f) + Vector3.Up * 1.0f);
        else Look(feet - to * 2.2f + right * 1.3f + Vector3.Up * 1.8f, mid + Vector3.Up * 1.0f);
        foreach (int x in _drive.Wait(4)) yield return x;
        GD.Print($"(the band: {party.Mood}, {party.Stance}, {party.Warnings} warnings; {string.Join("; ", band.Select(c => $"{(c == lead ? "leader " : "")}{c.Species.Figure.Role} {c.State} {c.Act} {patch.WorldOf(c).DistanceTo(feet):F1} m{(c.Species.Figure.Prop != "" ? ", " + c.Species.Figure.Prop : "")}"))})");
        if (file != null) foreach (int x in Shot(file)) yield return x;
    }
}
