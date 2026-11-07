using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

public enum Sex { Female, Male }
public enum LifeStage { Child, Adult, Elder }

/// <summary>What someone does for a living, which is what they look like.</summary>
public enum PersonRole { Worker, Trader, Guard, Farmer, Medic, Scavenger, Elder, Child, Drunk, Raider }

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
    /// <summary>What they wear (garment ids, see <see cref="HumanBody"/>).</summary>
    public readonly HashSet<string> Wear = new();
    /// <summary>The base of their clothes: shirt, wraps, furs, hazard, suit.</summary>
    public string Dress = "shirt";
    /// <summary>What they carry: "", rifle, club, hoe, cane, bottle.</summary>
    public string Prop = "";
    /// <summary>A Hollow husk: gaunt, slack, and what lives in it shows at the mouth and the eyes.</summary>
    public bool Husk;

    /// <summary>How they sound.</summary>
    public Voice Voice;

    public bool Has(string garment) => Wear.Contains(garment);

    /// <summary>The body's lengths and widths from its height, build, sex and age. Only numbers: the rig and the view read these.</summary>
    public void Measure()
    {
        float H = Height;
        bool child = Stage == LifeStage.Child;
        float shape = Sex == Sex.Male ? 1f : 0f;
        float limb = Folk?.LimbScale ?? 1f;
        LegLength = H * (child ? 0.47f : 0.525f - 0.008f * Build) * Math.Clamp(limb, 0.9f, 1.08f);
        Ankle = H * 0.04f;
        UpperLeg = (LegLength - Ankle) * 0.51f;
        LowerLeg = (LegLength - Ankle) * 0.51f;
        HeadRadius = H * (child ? 0.083f : 0.066f);
        Neck = H * 0.042f;
        Torso = H - LegLength - Neck - HeadRadius * 1.75f;
        UpperArm = H * 0.176f * limb;
        Forearm = H * 0.165f * limb;
        float width = H / 1.72f * (child ? 0.86f : 1f);
        // Men broader across the shoulder, women across the hip; heavy people wider everywhere, most at the waist.
        ShoulderW = width * (0.175f + 0.035f * shape + 0.03f * Build);
        HipW = width * (0.165f - 0.022f * shape + 0.035f * Build);
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
}
