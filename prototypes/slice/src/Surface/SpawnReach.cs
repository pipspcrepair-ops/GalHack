using System;
using Wildlife;

namespace Slice.Surface;

/// <summary>
/// How far round you life is kept and drawn: one distance (the spawn distance setting), from
/// <see cref="Default"/> out toward the detail distance (<see cref="Most"/>). The wild patch's lively
/// ring (<see cref="WildlifePatch.AroundYou"/>), its ground, how many it holds, how far its creatures
/// are drawn and where none may be put in sight all take their own from it, and so do people's
/// bodies (<see cref="PersonView.FarRange"/>). Far creatures are the same ones that close in: they
/// only think and re-pose less often. It can change in play: <see cref="Version"/> goes up and the
/// patch lays itself out again.
/// </summary>
public static class SpawnReach
{
    /// <summary>The lively ring as it was before the setting, m.</summary>
    public const float Old = 250f;

    /// <summary>The spawn distance a game starts at, m.</summary>
    public const float Default = 400f;

    /// <summary>The most it can be, m: toward the detail distance's default reach.</summary>
    public const float Most = 1200f;

    public static float Distance { get; private set; }
    public static int Version { get; private set; }

    /// <summary>The patch's half size, m: the ring and a margin, so nothing in it is dropped when the patch moves on (never less than the data's).</summary>
    public static float PatchHalf(float data) => MathF.Max(data, Distance + 50f);

    /// <summary>Nothing new is put down in anyone's sight nearer than this, m: just short of where bodies are drawn.</summary>
    public static float ViewDistance => Distance - 30f;

    /// <summary>How many more a patch holds than the data's: as its ground grows, at most six times (far life thins, it doesn't cost what near life does).</summary>
    public static float HoldScale(float half, float data) => MathF.Min(half * half / (data * data), 6f);

    static SpawnReach() => Set(Default);

    /// <summary>Sets the spawn distance, m (kept between the default and the most).</summary>
    public static void Set(float metres)
    {
        float d = Math.Clamp(float.IsFinite(metres) ? metres : Default, Default, Most);
        if (d == Distance) return;
        Distance = d;
        PersonView.FarRange = MathF.Max(340f, d);
        Version++;
    }
}
