using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Slice.Places;
using SurvivalVitals.Model;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// --air: the town's people need air (owner, 24 Nov: "if an NPC found itself naked in vacuum, would it die?"). On a fresh airless
/// world with a settlement (another, up to --worlds, till one has a closed room to step back into), out in the open by the sleeper,
/// held where they stand: one unsuited dies when the sleeper would (the hold, then the sleeper's share of health a second); one in a
/// sealed suit outlives that, its tank run down by the sleeper's drain; one unsuited stops losing health once set in a closed room;
/// a machine loses nothing; and one unsuited left free runs for air once the hold is out.
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/FallTest.tscn -Extra --air
/// </summary>
public partial class FallTest
{
    bool _airShown;

    IEnumerable<int> AirScenario(int worlds)
    {
        for (int i = 0; i < worlds && !_airShown; i++)
            foreach (int x in World(i == 0, i == 0 ? FirstSeed : null, air: true)) yield return x;
        Check(_airShown, $"the air shown on {(worlds > 1 ? $"one of {worlds} airless worlds" : "an airless world")} (a settlement with a closed room in it)");
    }

    IEnumerable<int> Air(Town town, Place place)
    {
        OxygenRules o = _game.Suit.Sim.Data.Vitals.Oxygen;
        // A closed room to step back into: a spot of the place's whose air the place holds.
        // (Looked for again when it's wanted: doors open and shut as people go through.)
        Vector3? Room()
        {
            foreach (var s in town.Spots)
                if (new Vector3(s.Cell.X + 0.5f, s.Cell.Y, s.Cell.Z + 0.5f) is var local && place.Closed(town.World(local))) return local;
            return null;
        }
        GD.Print($"  ({town.Spots.Count(s => place.Closed(town.World(new Vector3(s.Cell.X + 0.5f, s.Cell.Y, s.Cell.Z + 0.5f))))} of {town.Spots.Count} of its spots stand in closed rooms)");
        if (Room() == null) { GD.Print("  (no closed room here: another world)"); yield break; }
        Check(_game.Suit.InVacuum, "the sleeper, out of doors by the settlement, is in vacuum");

        // Five of them, out by the sleeper in the open, each in a body of their own (a place of copies shares one).
        var who = town.People.Where(p => !p.Dead && !p.Gone && p.Looks != null && p.View != null && p.Run == null).Take(5).ToList();
        if (who.Count < 5) { Check(false, $"five of its people to try ({who.Count})"); yield break; }
        Vector3 here = town.Local(_game.Survivor.WorldFeet);
        for (int i = 0; i < who.Count; i++)
        {
            Town.Person p = who[i];
            Species body = p.Looks.Copy();
            body.Figure = p.Looks.Figure.Clone();
            p.Looks = body;
            float a = i * MathF.Tau / who.Count;
            p.Pos = here + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * 2.5f;
            (p.Path, p.Step, p.Health.Current, p.Breath, p.Tank, p.AirIn) = (null, 0, p.Health.Max, -1f, -1f, 0f);
        }
        Town.Person bare = who[0], suited = who[1], back = who[2], machine = who[3], free = who[4];
        foreach (Town.Person p in who) if (p != suited) p.Figure.Wear.Remove("suit_helmet");
        suited.Figure.Wear.Add("suit_helmet");
        machine.Looks.Mechanical = true;
        foreach (Town.Person p in who) if (p != free) p.StunFor = 1e4f; // (held where they stand)

        // The sleeper's numbers: the hold, then their share of health a second.
        float dies = (float)(o.HoldBreathSeconds + _game.Suit.Sim.Data.Vitals.Health.Max / o.SuffocationDamagePerSecond);
        ulong from = Engine.GetPhysicsFrames();
        float Seconds() => Frame(from) * Tick;
        float diedAt = -1f, backHealth = -1f, panicAt = -1f, movedAt = -1f, movedHealth = -1f;
        bool backIn = false, wasOut = false;
        while (Seconds() < dies + 15f)
        {
            TopUp();
            yield return 0;
            float t = Seconds();
            if (t > 1f && !wasOut) { wasOut = true; Check(who.All(p => p == machine || !p.InAir), $"all four that breathe are out of the air ({string.Join(", ", who.Where(p => p != machine).Select(p => p.InAir ? "air" : "none"))})"); }
            if (bare.Dead && diedAt < 0f) diedAt = t;
            if (backIn && backHealth < 0f && back.InAir) (backHealth, movedAt) = (back.Health.Current, t - movedAt); // (its next look at the air)
            if (panicAt < 0f && free.Fleeing && t > o.HoldBreathSeconds - 1f) panicAt = t;
            // Into a closed room once it has started to suffocate: no more harm from there.
            if (!backIn && t > o.HoldBreathSeconds + 8f && Room() is { } room)
            {
                backIn = true;
                back.Pos = room;
                (movedAt, movedHealth) = (t, back.Health.Current);
                Check(movedHealth < back.Health.Max, $"out of the air past the hold, it suffocates ({movedHealth:F1} of {back.Health.Max:F0})");
            }
        }
        Check(diedAt > 0f && MathF.Abs(diedAt - dies) < 3f, $"unsuited, it dies of it in {diedAt:F1} s (the sleeper's {dies:F1} s: a {o.HoldBreathSeconds:F0} s hold, then {o.SuffocationDamagePerSecond / _game.Suit.Sim.Data.Vitals.Health.Max:P1} of health a second)");
        float drained = (float)o.Max - suited.Tank;
        Check(!suited.Dead && suited.Health.Current >= suited.Health.Max && MathF.Abs(drained - Seconds() * (float)o.DrainPerSecond) < 3f,
            $"in a sealed suit it lives on past that, unharmed, on its tank ({suited.Tank:F0} of {o.Max:F0} left after {Seconds():F0} s)");
        Check(backHealth > 0f && movedAt <= 0.5f && back.InAir && !back.Dead && back.Health.Current >= backHealth - 0.01f,
            $"back in a closed room it breathes ({movedAt:F2} s to its next look at the air, {movedHealth - backHealth:F2} lost meanwhile) and loses no more ({back.Health.Current:F1} from {backHealth:F1})");
        Check(!machine.Dead && machine.Health.Current >= machine.Health.Max, $"a machine doesn't breathe: unharmed ({machine.Health.Current:F0} of {machine.Health.Max:F0})");
        Check(panicAt > 0f, $"one left free runs for air once its breath is out ({(panicAt > 0f ? $"at {panicAt:F1} s" : "never")}; {(free.Dead ? "dead" : free.InAir ? "in the air" : "still out")} at the end, refuge spot {free.Refuge})");
        _airShown = true;
    }
}
