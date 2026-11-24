using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>One of a settlement's people as the worms see them: where they are, whether they carry them, whether they are about.</summary>
public sealed class Carried
{
    public Vector3 At;
    public bool Carrier;
    /// <summary>About (not indoors, not dead, not already gone).</summary>
    public bool Out;
    /// <summary>How long it has been alone with the one it is with, s.</summary>
    public float With;
}

/// <summary>
/// Silent carriers: Husks who still pass for people. They are the settlement's own people, rolled
/// as anyone is (their look, their walk, their gestures, their voice), with nothing showing; the
/// town's routine moves them like everyone else. The worms only show when one is alone with
/// someone: with you, it drops the act there and then (it rises a Husk, <see cref="Seizing.Rise"/>,
/// and goes for your head); with one of its own people for long enough, the worms pass to them
/// and there is one more carrier. Once carriers are a set share of the place
/// (<see cref="HuskRules.CarrierOpenShare"/>) they all drop the act. Kept as simple as that.
/// </summary>
public static class Carriers
{
    /// <summary>How many of a settlement's people carry the worms: none on a world without Husks; more coreward.</summary>
    public static int Roll(HuskRules r, bool husks, float coreward, int people, Rng rng)
    {
        if (!husks || people < 3) return 0;
        if (!rng.Chance(r.CarrierTownChance[0] + (r.CarrierTownChance[1] - r.CarrierTownChance[0]) * Math.Clamp(coreward, 0f, 1f))) return 0;
        return Math.Max(1, (int)MathF.Round(people * rng.Range(r.CarrierShare)));
    }

    /// <summary>Whether the act is over: carriers are past the share of the settlement's living people.</summary>
    public static bool Open(HuskRules r, IReadOnlyList<Carried> people)
    {
        int carriers = 0, living = 0;
        foreach (Carried p in people)
        {
            if (!p.Out && !p.Carrier) continue;
            living++;
            if (p.Carrier) carriers++;
        }
        return carriers > 0 && carriers >= r.CarrierOpenShare * living;
    }

    /// <summary>
    /// A moment of the settlement: which carriers rise (alone with you, or all of them once the
    /// act is over) into <paramref name="rise"/>, and the worms passed quietly to whoever a carrier
    /// has been alone with long enough. <paramref name="you"/> is the player, if near.
    /// </summary>
    public static void Step(HuskRules r, IReadOnlyList<Carried> people, Vector3? you, float dt, List<int> rise)
    {
        bool open = Open(r, people);
        for (int i = 0; i < people.Count; i++)
        {
            Carried c = people[i];
            if (!c.Carrier || !c.Out) continue;
            if (open) { rise.Add(i); continue; }
            // With you, and nobody of theirs near enough to see.
            if (you is Vector3 y && WildSim.Flat(y - c.At).Length() <= r.CarrierReach && Witnesses(r, people, i, -1) == 0)
            {
                rise.Add(i);
                continue;
            }
            // With one of their own, the two of them alone (you nowhere near): the worms pass, given time.
            int with = -1;
            float nearest = r.CarrierReach;
            for (int j = 0; j < people.Count; j++)
            {
                Carried o = people[j];
                if (j == i || !o.Out || o.Carrier) continue;
                float d = WildSim.Flat(o.At - c.At).Length();
                if (d <= nearest) { nearest = d; with = j; }
            }
            bool alone = with >= 0 && Witnesses(r, people, i, with) == 0 && (you is not Vector3 v || WildSim.Flat(v - c.At).Length() > r.CarrierAlone);
            c.With = alone ? c.With + dt : 0f;
            if (c.With >= r.CarrierPass)
            {
                people[with].Carrier = true;
                c.With = 0f;
            }
        }
    }

    /// <summary>How many of the settlement's own people (carriers aside, and the one it is with) are near enough to see a carrier.</summary>
    static int Witnesses(HuskRules r, IReadOnlyList<Carried> people, int carrier, int with)
    {
        int n = 0;
        Vector3 at = people[carrier].At;
        for (int j = 0; j < people.Count; j++)
        {
            Carried o = people[j];
            if (j == carrier || j == with || !o.Out || o.Carrier) continue;
            if (WildSim.Flat(o.At - at).Length() < r.CarrierAlone) n++;
        }
        return n;
    }
}
