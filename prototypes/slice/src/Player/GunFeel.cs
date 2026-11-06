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
    /// <summary>A small flare: the survival tool.</summary>
    Flare,
}

/// <summary>
/// How one thing fired feels, in data: the flash at the muzzle (style, colour, size, a short light,
/// how many frames it lasts, how much smoke) and the kick (the held tool's recoil, the camera's tilt
/// in degrees, and a shake for a pilot). Looked up by id; a vessel gun scales its flash and shake by
/// calibre.
/// </summary>
public sealed record GunFeel(
    string Id, FlashStyle Style, Color Colour, float Size, float LightEnergy, float LightRange, int Frames,
    float Smoke, float SmokeLife, float Recoil, float CameraKick, float Shake = 0f, string Shape = null)
{
    /// <summary>A shot soon after another (within this many seconds) flashes at <see cref="RepeatDim"/> of the strength, so held fire doesn't strobe.</summary>
    public const float RepeatWindow = 0.25f, RepeatDim = 0.45f;

    // Sizes are the flame's length in metres at the muzzle (a bloom's diameter): about 110-130 px for a bullet on a 1600 px screen.
    public static readonly GunFeel Tool = new("tool_shot", FlashStyle.Flare, new Color(0.4f, 0.88f, 1f), 0.1f, 0.7f, 3.5f, 2, 0f, 0f, 0.6f, 0.12f, 0f, "tool4");

    static readonly Dictionary<string, GunFeel> Table = Build(
        Tool,
        new("bullet", FlashStyle.Star, new Color(1f, 0.66f, 0.16f), 0.15f, 1.1f, 4f, 2, 0f, 0f, 0.9f, 0.25f, 0f, "cone5"),
        new("plasma", FlashStyle.Bloom, new Color(0.35f, 1f, 0.6f), 0.13f, 0.9f, 4f, 3, 0f, 0f, 0.5f, 0.1f),
        new("laser", FlashStyle.Bloom, new Color(1f, 0.25f, 0.2f), 0.09f, 0.7f, 3.5f, 2, 0f, 0f, 0.25f, 0.05f),
        new("rocket", FlashStyle.Rocket, new Color(1f, 0.7f, 0.25f), 0.24f, 1.6f, 6f, 3, 1f, 1.4f, 1.5f, 0.45f, 0f, "chunk6"),
        new("vessel_gun", FlashStyle.Star, new Color(1f, 0.7f, 0.25f), 0.6f, 3f, 12f, 3, 0f, 0f, 0f, 0f, 0.3f, "cone5"));

    /// <summary>The petal burst shapes: a long centre petal and a ring of shorter ones in a cone.</summary>
    public static readonly (string Id, BurstShape Shape)[] Shapes =
    {
        ("cone5", new BurstShape(5, 28f, 0.62f, 0.2f, 0.26f)),
        ("chunk6", new BurstShape(6, 38f, 0.5f, 0.28f, 0.34f)),
        ("narrow4", new BurstShape(4, 20f, 0.75f, 0.16f, 0.2f)),
        ("spiky8", new BurstShape(8, 45f, 0.45f, 0.14f, 0.18f)),
        ("tool4", new BurstShape(4, 30f, 0.6f, 0.22f, 0.26f)),
    };

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

/// <summary>A petal burst: how many petals round the centre, their spread from the barrel axis (degrees), their length against the centre petal's, and the widths.</summary>
public sealed record BurstShape(int Petals, float Spread, float SideLength, float Width, float CentreWidth);
