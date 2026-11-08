using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

public enum Sex { Female, Male }
public enum LifeStage { Child, Adult, Elder }

/// <summary>What someone does for a living, which is what they look like.</summary>
public enum PersonRole { Worker, Trader, Guard, Farmer, Medic, Scavenger, Elder, Child, Drunk, Raider, Mechanic, Lawman }

/// <summary>What the worms do to a body's shape: one or two of these on a Husk, so it reads wrong from a distance.</summary>
[Flags]
public enum Wrong { None = 0, Jaw = 1, Arm = 2, Hunch = 4, Head = 8 }

/// <summary>Who holds a place: the same people, friendly, gone to raiding, or gone wild.</summary>
public enum Occupancy { Friendly, Raider, Feral }

/// <summary>
/// One person's body and clothes: every number the rig, the meshes, the gait and the voice read.
/// A person is a <see cref="Species"/> of one (<see cref="Species.Figure"/>), so whatever works on
/// a species works on them: mutation, the rig, the sim's brains.
/// </summary>
public sealed class Figure
{
    public Sex Sex;
    public LifeStage Stage;
    public float Age;
    public PersonRole Role;
    public Occupancy Occupancy;
    /// <summary>The folk they come from (humans, or a human-like people of this world).</summary>
    public Folk Folk;

    /// <summary>Height standing, m.</summary>
    public float Height;
    /// <summary>0 slight to 1 heavy.</summary>
    public float Build;
    /// <summary>0 upright to 1 bent over a stick.</summary>
    public float Stoop;
    /// <summary>0 steady to 1 falling over (a drunk's stagger).</summary>
    public float Stagger;

    // Worked out from the above in Measure, m.
    public float LegLength, UpperLeg, LowerLeg, Ankle;
    public float UpperArm, Forearm;
    public float Torso, Neck, HeadRadius;
    /// <summary>Half widths: shoulders, waist, hips; half the depth of the chest.</summary>
    public float ShoulderW, WaistW, HipW, ChestD;
    /// <summary>How much clothes add round a limb, as a share (a fur coat on a thin arm).</summary>
    public float Padding = 1f;
    /// <summary>Weight, kg: the body's own.</summary>
    public float Mass;

    public Color Skin, Hair, Cloth, Trim, Leather, Metal;
    public string HairStyle = "short";
    public bool Beard;
    /// <summary>
    /// Their own proportions, each around 1 and rolled independently around their people's: legs,
    /// arms, neck, head, hands, feet, hips and shoulders. Height stays what it is: longer legs make a
    /// shorter body. Bounds keep every combination human.
    /// </summary>
    public float LegK = 1f, ArmK = 1f, NeckK = 1f, HeadK = 1f, HandK = 1f, FootK = 1f, HipK = 1f, ShoulderK = 1f;
    /// <summary>One saturated mark that says what they do (a band, a scarf): never the colour of the ground.</summary>
    public Color Accent = new(0.6f, 0.15f, 0.1f);

    /// <summary>The cast of their face from their people: brow and jaw offsets.</summary>
    public float Brow, Jaw;
    /// <summary>What they wear (garment ids, see <see cref="HumanBody"/>).</summary>
    public readonly HashSet<string> Wear = new();
    /// <summary>The base of their clothes: shirt, wraps, furs, hazard, suit.</summary>
    public string Dress = "shirt";
    /// <summary>The layers they wear for the weather, innermost first (base, insulation, wind, shell), and how warm all they wear keeps them.</summary>
    public readonly List<string> Layered = new();
    public float Warmth;
    /// <summary>How worn their clothes are, 0 new to 1 patched and faded.</summary>
    public float Worn;
    /// <summary>What they carry: "", rifle, club, hoe, cane, bottle.</summary>
    public string Prop = "";
    /// <summary>A Husk: taken by the worms, which come out of the mouth, the nose, the eyes and the ears. Gaunt and slack, moved like a puppet.</summary>
    public bool Husk;
    /// <summary>Their clothes are torn: rents and hanging strips, skin showing through.</summary>
    public bool Torn;
    /// <summary>What the worms have done to the body's shape (none for most people): see <see cref="Husks"/>.</summary>
    public Wrong Wrongs;
    /// <summary>The arm that is too long (0 left, 1 right), and how much longer.</summary>
    public int LongArm;
    public float ArmStretch = 1f;
    /// <summary>How far the head sits off true, radians (to the side, and forward), and how far the jaw hangs, as a share of the head.</summary>
    public float HeadTilt, HeadDroop, JawDrop;

    /// <summary>One side's upper arm and forearm (a Husk's long arm is longer).</summary>
    public float UpperArmOf(int side) => side == LongArm ? UpperArm * ArmStretch : UpperArm;
    public float ForearmOf(int side) => side == LongArm ? Forearm * ArmStretch : Forearm;

    /// <summary>A copy to change (a person turned): every number and colour, their own set of clothes.</summary>
    public Figure Clone()
    {
        var f = (Figure)MemberwiseClone();
        typeof(Figure).GetField(nameof(Wear))!.SetValue(f, new HashSet<string>(Wear));
        typeof(Figure).GetField(nameof(Layered))!.SetValue(f, new List<string>(Layered));
        return f;
    }
    /// <summary>A child doing a grown-up's job out of necessity: walks, stands and dresses like one.</summary>
    public bool Hardened;
    /// <summary>A child at play: quick, darting, never still for long.</summary>
    public bool Playful => Stage == LifeStage.Child && !Hardened;

    /// <summary>What their body says to you coming up peacefully, to you aiming at them, to a fright (from their role's data).</summary>
    public BodyAct Approach = BodyAct.Greet, Aimed = BodyAct.HandsUp, Startled = BodyAct.Startle;

    /// <summary>How they sound.</summary>
    public Voice Voice;
    /// <summary>Their job's day (when they're out, where they keep to, how they get about): <see cref="RoleLook"/>.</summary>
    public RoleLook Routine;

    static readonly float[] DayOut = { 0.26f, 0.78f };

    /// <summary>Whether they are out and about at this time of day (0 to 1).</summary>
    public bool OutAt(float time)
    {
        float[] o = Routine?.Out ?? DayOut;
        if (o[1] - o[0] >= 0.999f) return true;
        return o[0] <= o[1] ? time >= o[0] && time < o[1] : time >= o[0] || time < o[1];
    }

    public bool Has(string garment) => Wear.Contains(garment);

    /// <summary>
    /// The tallest anyone stands, crown and all, m: under a doorway (one 2 m cell, less a hand's
    /// clearance). The owner's rule (8 Nov 2026): every people fits through every generated door.
    /// </summary>
    public const float HeightCap = 1.92f;
    /// <summary>The top of them standing, crown and all, m.</summary>
    public float Top => Height + HeadRadius * (Folk?.CrownRise ?? 0f) * (Stage == LifeStage.Child ? 0.5f : 1f);
    static readonly Folk Default = new();

    /// <summary>The body's lengths and widths from its height, build, sex and age. Only numbers: the rig and the view read these.</summary>
    public void Measure()
    {
        bool child = Stage == LifeStage.Child;
        float shape = Sex == Sex.Male ? 1f : 0f;
        float limb = Folk?.LimbScale ?? 1f;
        // A folk's frame round the human one (wider bounds than one person's own, docs/alien-peoples.md 4); a child's parts are their people's, smaller.
        Folk k = Folk ?? Default;
        float grow = child ? 0.5f : 1f;
        float legs = 1f + (k.LegScale - 1f) * grow, arms0 = 1f + (k.ArmScale - 1f) * grow, neck = 1f + (k.NeckScale - 1f) * grow, head = 1f + (k.HeadScale - 1f) * grow;
        // Nobody is taller than a doorway lets through: crown and all, under the cap.
        float rise = k.CrownRise * (child ? 0.5f : 1f);
        float headR = (child ? 0.083f : 0.066f) * Math.Clamp(HeadK, 0.92f, 1.08f) * head;
        Height = MathF.Min(Height, HeightCap / (1f + headR * rise));
        float H = Height;
        LegLength = H * (child ? 0.47f : 0.525f - 0.008f * Build) * Math.Clamp(limb * LegK, 0.88f, 1.1f) * legs;
        Ankle = H * 0.04f;
        UpperLeg = (LegLength - Ankle) * 0.51f;
        LowerLeg = (LegLength - Ankle) * 0.51f;
        HeadRadius = H * headR;
        Neck = H * 0.042f * Math.Clamp(NeckK, 0.75f, 1.25f) * neck;
        Torso = H - LegLength - Neck - HeadRadius * 1.75f;
        float arms = limb * Math.Clamp(ArmK, 0.93f, 1.07f) * arms0;
        UpperArm = H * 0.176f * arms;
        Forearm = H * 0.165f * arms;
        float width = H / 1.72f * (child ? 0.86f : 1f);
        // Men broader across the shoulder, women across the hip; heavy people wider everywhere, most at the waist.
        ShoulderW = width * (0.175f + 0.035f * shape + 0.03f * Build) * Math.Clamp(ShoulderK, 0.88f, 1.12f) * (1f + (k.ShoulderScale - 1f) * grow);
        HipW = width * (0.165f - 0.022f * shape + 0.035f * Build) * Math.Clamp(HipK, 0.86f, 1.14f);
        WaistW = width * (0.122f + 0.022f * shape + 0.075f * Build);
        ChestD = width * (0.105f + 0.012f * shape + 0.045f * Build);
        Mass = (18f + 15f * Build) * H * H;
    }
}

/// <summary>
/// A people: humans, or a human-like folk of this world, all of them shaped by where they live.
/// Everyone of a world's folk shares these; how far men and women differ is theirs too.
/// </summary>
public sealed class Folk
{
    public string Id = "human";
    /// <summary>False for a human-like people: their own proportions and colours.</summary>
    public bool Human = true;
    public float HeightScale = 1f, LimbScale = 1f, BuildBias, BrowRidge;
    /// <summary>How far the sexes differ, as a share of the human gap in height and shoulder.</summary>
    public float Dimorphism = 1f;
    /// <summary>Hue, saturation and value ranges of their skin.</summary>
    public float[] SkinHue = { 0.05f, 0.09f }, SkinSat = { 0.3f, 0.55f }, SkinVal = { 0.3f, 0.86f };
    /// <summary>0 to 1: they see in the dark better than we do, and their eyes catch the light.</summary>
    public float DarkVision;
    public Color EyeShine = new(0.9f, 0.7f, 0.3f);
    /// <summary>No hair at all.</summary>
    public bool Hairless;

    // ---- An alien folk's parts (docs/alien-peoples.md): one or none at each socket, the same on everyone of the folk ----

    /// <summary>What their skin is: skin, fur, feathers, scales or chitin.</summary>
    public string Covering = "skin";
    /// <summary>The ears: human, round, leaf, tall, fan, droop, fin, or none (holes only); and how big.</summary>
    public string Ears = "human";
    public float EarSize = 1f;
    /// <summary>What stands on the head: "" or horns (pair, ram, brow, knobs), antlers, crown, antennae, crest, quills, plume, keel; and how big.</summary>
    public string Crown = "";
    public float CrownSize = 1f;
    /// <summary>The face: "" (a human nose), muzzle, snout, beak or mandibles; tusks from the jaw.</summary>
    public string Mouth = "";
    public float MouthSize = 1f;
    public bool Tusks;
    /// <summary>How they see: two, three, four, compound or band; how big; their colour, solid without a white.</summary>
    public string Eyes = "two";
    public float EyeSize = 1f;
    public Color EyeColour = new(0.16f, 0.1f, 0.03f);
    /// <summary>A tail: "" or stub, whip, tufted, thick, bushy, fan; its length at 1.72 m tall, m.</summary>
    public string Tail = "";
    public float TailLength;
    /// <summary>On the back: "" or spines, plates, ridge.</summary>
    public string Back = "";
    /// <summary>Claws on the fingers.</summary>
    public bool Claws;
    /// <summary>Their own second colour (crest, frill, tail tip), and darker points at the hands, ears and tail tip (0 none to 1).</summary>
    public Color Mark = new(0.5f, 0.4f, 0.3f);
    public float Points;
    /// <summary>Glowing dots on cheeks and forearms, in the eye shine's colour.</summary>
    public bool Glow;
    /// <summary>Their frame, round the human one: legs, arms, neck, head and shoulders, and a lean forward.</summary>
    public float LegScale = 1f, ArmScale = 1f, NeckScale = 1f, HeadScale = 1f, ShoulderScale = 1f, StoopBias;
    /// <summary>Points spent on outline (docs/alien-peoples.md 1.5).</summary>
    public int Spent;

    /// <summary>Above the skull this folk's crown reaches, as a share of the head radius (horns, antlers, antennae and crests stand up).</summary>
    public float CrownRise => Crown switch
    {
        "" => 0f, "knobs" => 0.35f, "quills" => 0.6f, "keel" or "crest" => 0.8f, "pair" or "ram" or "brow" => 0.75f, "crown" => 0.95f, "plume" => 1.2f, "antennae" => 1.5f, "antlers" => 2.1f, _ => 0.6f,
    } * CrownSize;

    /// <summary>
    /// How the folk reads at 30 m (docs/alien-peoples.md 1.6): what is over the head, on the back
    /// and behind, and how tall and wide. Two folk of a world must differ in at least three.
    /// </summary>
    public string[] Silhouette() => new[]
    {
        Crown is "" ? Ears is "tall" or "fan" ? "ears" : "" : Crown is "pair" or "ram" or "brow" or "knobs" ? "horns" : Crown,
        Back,
        Tail is "stub" or "" ? Tail : Tail is "thick" or "bushy" ? "heavy-tail" : "tail",
        HeightScale < 0.9f ? "short" : HeightScale > 1.06f ? "tall" : "mid",
        ShoulderScale > 1.1f ? "wide" : ShoulderScale < 0.92f ? "thin" : "mid",
    };
}
