using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// A person the game's own code keeps (a townsperson), as a Husk sees them: their body, and a way
/// to say they are gone once they have turned. Wildlife's own people are creatures and need none.
/// </summary>
public interface IHost : ITarget
{
    /// <summary>Who they are: the body a Husk made of them wears.</summary>
    Species Body { get; }
    /// <summary>They have turned: the game takes them out of its own keeping.</summary>
    void Taken();
}

/// <summary>
/// The Husks: people (human, or a human-like folk) taken over by worm-like parasites. Nothing
/// here is drawn by hand; a Husk is a person rolled the usual way (their job, their clothes,
/// their world) and then taken:
/// - their clothes are their own, torn (rents with skin showing, hanging strips, grime);
/// - worms come out of the mouth, the nostrils, the eye sockets and the ears, and move on their
///   own (<see cref="HumanBody"/>, the creature shader);
/// - one or two off-putting changes from the mutation layer's set (<see cref="Wrong"/>): a jaw
///   hanging too far, an arm too long, a hunched back, a head held at a wrong angle, so the shape
///   reads slightly wrong at 30 m while still a person;
/// - gaunt, grey, moved like a puppet (<see cref="HumanRig"/>), and quiet: what voice is left
///   murmurs through the worms.
/// A world's Husks are drawn from several hosts (<see cref="Species.Hosts"/>), so no two match;
/// and anyone they seize long enough becomes one more (<see cref="Turn"/>).
/// </summary>
public static class Husks
{
    static readonly Wrong[] Wrongs = { Wrong.Jaw, Wrong.Arm, Wrong.Hunch, Wrong.Head };

    /// <summary>What a Husk can't wear: anything over the face the worms come out through.</summary>
    static readonly string[] OverFace = { "suit_helmet", "respirator", "goggles", "shades", "eyepatch", "fur_hood", "head_wrap" };

    /// <summary>Takes a person: the same body, now a Husk. Changes the species in place (it should be the person's own copy).</summary>
    public static void Take(Species s, Rng rng, Dialect dialect, HuskRules rules)
    {
        Figure f = s.Figure;
        f.Husk = true;
        f.Torn = true;
        f.Prop = "";
        foreach (string w in OverFace) f.Wear.Remove(w);
        f.Build = MathF.Min(f.Build, rng.Range(0.05f, 0.3f));
        f.Stoop = MathF.Max(f.Stoop, rng.Range(0.12f, 0.3f));
        f.Stagger = rng.Range(0.12f, 0.28f);
        // Bloodless: the skin goes grey, the clothes go dark with grime.
        var grey = new Color(0.58f, 0.56f, 0.52f);
        var grime = new Color(0.24f, 0.2f, 0.16f);
        f.Skin = f.Skin.Lerp(grey, rng.Range(0.3f, 0.45f));
        f.Cloth = f.Cloth.Lerp(grime, rng.Range(0.25f, 0.45f));
        f.Trim = f.Trim.Lerp(grime, rng.Range(0.2f, 0.4f));
        f.Accent = f.Accent.Lerp(grime, 0.5f);

        // One or two things the worms have done to the shape, never the same pair twice running.
        var pool = new List<Wrong>(Wrongs);
        rng.Shuffle(pool);
        int n = rng.Chance(0.55f) ? 1 : 2;
        f.Wrongs = Wrong.None;
        for (int i = 0; i < n; i++) f.Wrongs |= pool[i];
        if (f.Wrongs.HasFlag(Wrong.Jaw)) f.JawDrop = rng.Range(0.4f, 0.7f);
        if (f.Wrongs.HasFlag(Wrong.Arm)) { f.LongArm = rng.Int(0, 1); f.ArmStretch = rng.Range(1.22f, 1.38f); }
        if (f.Wrongs.HasFlag(Wrong.Hunch)) f.Stoop = rng.Range(0.55f, 0.8f);
        if (f.Wrongs.HasFlag(Wrong.Head)) { f.HeadTilt = rng.Range(0.38f, 0.62f) * (rng.Chance(0.5f) ? 1f : -1f); f.HeadDroop = rng.Range(0.1f, 0.3f); }
        f.Measure();

        // What voice is left: low, wet and slow, through the worms.
        Voice v = Voice.For(f, dialect, s.Seed);
        v.Pitch *= rng.Range(0.7f, 0.85f);
        v.Breath = rng.Range(0.6f, 0.85f);
        v.Rasp = rng.Range(0.7f, 0.95f);
        v.Pace *= 0.55f;
        f.Voice = v;

        s.Id += "-husk";
        s.CommonName = "husk";
        s.Colors.Base = f.Skin;
        s.Colors.Accent = f.Cloth;
        s.Colors.Belly = f.Skin.Lightened(0.05f);
        s.Colors.Glow = Colors.Black;
        s.Colors.GlowStrength = 0f;
        s.Behaviour = Behaviour.Predator;
        s.Temperament = Temperament.Aggressive;
        s.Activity = Activity.Cathemeral;
        s.GroupMin = 2;
        s.GroupMax = 5;
        s.Monstrous = false;
        s.Mutation = MathF.Max(s.Mutation, rng.Range(rules.Mutation));
        s.Hosts = null;
        s.Mate = null;
        s.Drops = new Drops();
        s.Senses = new SenseProfile { Sight = 0.8f, DarkVision = 0.7f, Hearing = 1.5f, Scent = 0.6f };
        var st = s.Stats = new Stats
        {
            MaxHealth = MathF.Round(55f * Math.Clamp(f.Mass / 75f, 0.6f, 1.3f)),
            WalkSpeed = 0.9f,
            RunSpeed = 3.4f,
            Damage = 13f,
            AttackRange = 1.4f,
            Perception = 44f,
            FleeRadius = 0f,
            Radius = 0.35f,
            Telegraph = rules.Telegraph,
            Retaliates = true,
            Hunts = true,
        };
        st.Xp = (int)MathF.Round(st.MaxHealth * 0.4f + st.Damage);
        s.Attack = new AttackDef { Name = "Seize", Style = AttackStyle.Seize, Telegraph = rules.Telegraph, Sound = "growl" };
        s.Spit = SpitKind.Maggots;
    }

    /// <summary>
    /// Someone seized long enough: the same person (their face, their build, their clothes) as a
    /// Husk. A new species of one, as every person is; the host's own is left as it was.
    /// </summary>
    public static Species Turn(Species host, ulong seed, HuskRules rules)
    {
        Species s = host.Copy();
        s.Figure = host.Figure.Clone();
        s.Seed = seed;
        s.Young = s.Adult = null;
        s.Domestic = DomesticKind.None;
        s.Role = DeepRole.None;
        Take(s, Rng.For(seed, "turned"), host.Figure.Voice?.Dialect ?? Dialect.For(seed), rules);
        return s;
    }

    /// <summary>Whether something can be turned: a person, not a machine, not one already.</summary>
    public static bool CanTurn(Species s) => s.Figure != null && !s.Mechanical && !s.Husk;
}
