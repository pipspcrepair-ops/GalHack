using System.Collections.Generic;
using Godot;

namespace Slice.Player;

/// <summary>What a muzzle flash looks like.</summary>
public enum FlashStyle
{
    /// <summary>A sharp star: kinetic bullets.</summary>
    Star,
    /// <summary>A soft coloured bloom: energy, plasma, laser.</summary>
    Bloom,
    /// <summary>A backblast cone and a lingering smoke trail: rockets.</summary>
    Rocket,
    /// <summary>An organic burst with no flash to speak of: living weapons.</summary>
    Wet,
    /// <summary>A soft puff: a flame's breath, a thrown thing.</summary>
    Mist,
}

/// <summary>
/// How one thing fired feels, in data: the flash at the muzzle (style, colour, size, a short light,
/// how many frames it lasts, how much smoke) and the kick (the held tool's recoil, the camera's tilt
/// in degrees, and a shake for a pilot). Looked up by id; a vessel gun scales its flash and shake by
/// calibre.
/// </summary>
public sealed record GunFeel(
    string Id, FlashStyle Style, Color Colour, float Size, float LightEnergy, float LightRange, int Frames,
    float Smoke, float SmokeLife, float Recoil, float CameraKick, float Shake = 0f)
{
    /// <summary>A shot soon after another (within this many seconds) flashes at <see cref="RepeatDim"/> of the strength, so held fire doesn't strobe.</summary>
    public const float RepeatWindow = 0.25f, RepeatDim = 0.45f;

    /// <summary>One multiplier for every flash's size (for size samples; the sizes below are the ones the owner chose).</summary>
    public static float FlashScale = 1f;

    public static readonly GunFeel Tool = new("tool_shot", FlashStyle.Star, new Color(1f, 0.25f, 0.2f), 0.084f, 0.7f, 3.5f, 2, 0f, 0f, 0.6f, 0.12f);

    static readonly Dictionary<string, GunFeel> Table = Build(
        Tool,
        new("bullet", FlashStyle.Star, new Color(1f, 0.93f, 0.62f), 0.084f, 1.1f, 4f, 2, 0.4f, 0.45f, 0.9f, 0.25f),
        new("plasma", FlashStyle.Bloom, new Color(0.35f, 1f, 0.6f), 0.09f, 0.9f, 4f, 3, 0f, 0f, 0.5f, 0.1f),
        new("laser", FlashStyle.Bloom, new Color(1f, 0.25f, 0.2f), 0.06f, 0.7f, 3.5f, 2, 0f, 0f, 0.25f, 0.05f),
        new("rocket", FlashStyle.Rocket, new Color(1f, 0.72f, 0.3f), 0.15f, 1.6f, 6f, 3, 1f, 1.4f, 1.5f, 0.45f),
        new("vessel_gun", FlashStyle.Star, new Color(1f, 0.9f, 0.6f), 0.2f, 3f, 12f, 3, 0.7f, 0.8f, 0f, 0f, 0.3f),
        // Hand weapons (shared/Arms/data/weapons.json names these by id).
        new("pistol", FlashStyle.Star, new Color(1f, 0.9f, 0.6f), 0.07f, 0.9f, 3.5f, 2, 0.3f, 0.4f, 0.8f, 0.22f),
        new("rifle", FlashStyle.Star, new Color(1f, 0.92f, 0.62f), 0.09f, 1.2f, 4.5f, 2, 0.45f, 0.5f, 0.9f, 0.3f),
        new("shotgun", FlashStyle.Star, new Color(1f, 0.85f, 0.5f), 0.13f, 1.6f, 5f, 3, 0.9f, 0.8f, 1.5f, 0.9f),
        new("launcher", FlashStyle.Rocket, new Color(1f, 0.78f, 0.45f), 0.1f, 1.2f, 4.5f, 3, 0.8f, 1.0f, 1.3f, 0.6f),
        new("rail", FlashStyle.Bloom, new Color(0.55f, 0.9f, 1f), 0.08f, 1.2f, 4f, 3, 0f, 0f, 1.0f, 0.4f),
        new("arc", FlashStyle.Bloom, new Color(0.7f, 0.82f, 1f), 0.09f, 1.6f, 5f, 2, 0f, 0f, 0.6f, 0.15f),
        new("smart", FlashStyle.Star, new Color(0.8f, 1f, 0.7f), 0.06f, 0.7f, 3.5f, 2, 0.2f, 0.3f, 0.6f, 0.15f),
        new("hive", FlashStyle.Wet, new Color(0.9f, 0.7f, 0.3f), 0.05f, 0f, 1f, 2, 0.2f, 0.5f, 0.15f, 0.02f),
        new("worm", FlashStyle.Wet, new Color(0.8f, 0.5f, 0.45f), 0.07f, 0f, 1f, 2, 0.35f, 0.6f, 0.9f, 0.25f),
        new("flame", FlashStyle.Mist, new Color(1f, 0.55f, 0.2f), 0.06f, 1.4f, 5f, 2, 0.3f, 0.5f, 0.05f, 0.01f),
        new("throw", FlashStyle.Mist, new Color(0.7f, 0.7f, 0.7f), 0.02f, 0f, 1f, 2, 0f, 0f, 0.4f, 0.05f));

    /// <summary>The feel for an id; the survival tool's if unknown.</summary>
    public static GunFeel Get(string id) => id != null && Table.TryGetValue(id, out GunFeel f) ? f : Tool;

    public static IEnumerable<GunFeel> All => Table.Values;

    static Dictionary<string, GunFeel> Build(params GunFeel[] feels)
    {
        var d = new Dictionary<string, GunFeel>();
        foreach (GunFeel f in feels) d[f.Id] = f;
        return d;
    }
}
