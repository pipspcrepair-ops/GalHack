using System;

namespace GalHack.Arms;

/// <summary>
/// One hit as it reaches a living target: how much, its class and tags, and who it came from.
/// This is the seam a personal shield sits in (it is not built yet): the shield takes a hit first
/// unless the hit goes past it. Flame, gas and radiation always do (owner's decision 11), so when
/// the shield lands it reads <see cref="PassesShield"/> and nothing else changes.
/// </summary>
public readonly record struct HitInfo(float Amount, DamageClass Class, HarmTags Tags, int Source)
{
    public bool PassesShield => (Tags & (HarmTags.Fire | HarmTags.Gas | HarmTags.Rad)) != 0;
}

/// <summary>Whatever a timed effect lands on: alive or not, and how harm reaches it.</summary>
public interface IAfflicted
{
    bool Alive(object target);
    void Harm(object target, in HitInfo hit);
}

/// <summary>
/// Timed harm on targets: a burn ticking down after a flame, so many points a second for so many
/// seconds. A fixed table, refreshed rather than stacked when the same harm lands again, and
/// cleared the moment its target dies. On a player every effect is capped at a few seconds
/// (<see cref="ArmsRules.PlayerEffectSeconds"/>): effects on players last seconds and always end.
/// </summary>
public sealed class Afflictions
{
    struct Entry
    {
        public object Target;
        public float PerSecond, Left, NextTick;
        public HarmTags Tags;
        public DamageClass Class;
        public int Source;
    }

    readonly Entry[] _entries;
    float _clock;

    public Afflictions(int capacity = 64) => _entries = new Entry[capacity];

    /// <summary>Effects running now (tests).</summary>
    public int Active
    {
        get
        {
            int n = 0;
            foreach (Entry e in _entries) if (e.Target != null) n++;
            return n;
        }
    }

    /// <summary>Seconds left on a target's effect with these tags, 0 if none.</summary>
    public float Left(object target, HarmTags tags)
    {
        foreach (Entry e in _entries)
            if (e.Target == target && e.Tags == tags) return e.Left;
        return 0f;
    }

    /// <summary>Starts or refreshes an effect. <paramref name="cap"/> limits it (a player's few seconds).</summary>
    public void Add(object target, float perSecond, float seconds, DamageClass cls, HarmTags tags, int source, float cap = float.MaxValue)
    {
        if (target == null || perSecond <= 0f || seconds <= 0f) return;
        seconds = MathF.Min(seconds, cap);
        int free = -1;
        for (int i = 0; i < _entries.Length; i++)
        {
            ref Entry e = ref _entries[i];
            if (e.Target == target && e.Tags == tags)
            {
                e.Left = MathF.Max(e.Left, seconds);
                e.PerSecond = MathF.Max(e.PerSecond, perSecond);
                return;
            }
            if (e.Target == null && free < 0) free = i;
        }
        if (free < 0) return; // full: the newest waits its turn
        _entries[free] = new Entry { Target = target, PerSecond = perSecond, Left = seconds, NextTick = _clock + 0.5f, Tags = tags, Class = cls, Source = source };
    }

    /// <summary>Ticks every effect: harm lands twice a second.</summary>
    public void Tick(float dt, IAfflicted sink)
    {
        _clock += dt;
        for (int i = 0; i < _entries.Length; i++)
        {
            ref Entry e = ref _entries[i];
            if (e.Target == null) continue;
            if (!sink.Alive(e.Target)) { e = default; continue; }
            e.Left -= dt;
            if (_clock >= e.NextTick)
            {
                e.NextTick += 0.5f;
                sink.Harm(e.Target, new HitInfo(e.PerSecond * 0.5f, e.Class, e.Tags, e.Source));
            }
            if (e.Left <= 0f) e = default;
        }
    }

    public void Clear()
    {
        for (int i = 0; i < _entries.Length; i++) _entries[i] = default;
    }
}
