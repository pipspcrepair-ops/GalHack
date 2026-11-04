using System;
using Godot;

namespace Boarding;

/// <summary>
/// The Hijack Tool's channel on a staged core, as rules only.
///
/// Progress is measured in seconds of channelling. It only grows while someone holds the
/// tool on the core, stalls for a moment whenever they take damage, and slowly bleeds away
/// while nobody is channelling. A finished stage is locked in: decay never undoes it.
/// Each finished stage fires the core's next defensive response (from data).
/// </summary>
public sealed class HijackSystem
{
    readonly Tunables _t;

    public float Progress { get; private set; }
    public int CompletedStages { get; private set; }
    public bool Started { get; private set; }
    public bool Complete { get; private set; }
    public bool Channelling { get; private set; }
    public float PauseRemaining { get; private set; }

    public int Stages => _t.HijackStages;
    public float StageSeconds => _t.StageSeconds;
    public float Total => _t.HijackTotalSeconds;
    public bool Paused => PauseRemaining > 0f;

    public event Action Begun;
    /// <summary>(stage number from 1, response name from data)</summary>
    public event Action<int, string> StageCompleted;
    public event Action Finished;

    public HijackSystem(Tunables tunables) => _t = tunables;

    /// <summary>0..1 progress through one stage, for the HUD.</summary>
    public float StageFill(int stage) =>
        Mathf.Clamp((Progress - stage * StageSeconds) / StageSeconds, 0f, 1f);

    /// <summary>Taking damage while channelling stalls the tool.</summary>
    public void NotifyDamage()
    {
        if (Started && !Complete) PauseRemaining = _t.HijackDamagePause;
    }

    public void Tick(float dt, bool channelling)
    {
        Channelling = channelling && !Complete;
        if (Complete) return;
        PauseRemaining = Mathf.Max(0f, PauseRemaining - dt);

        if (Channelling)
        {
            if (!Started)
            {
                Started = true;
                Begun?.Invoke();
            }
            if (!Paused) Progress = Mathf.Min(Total, Progress + dt);
        }
        else
        {
            float floor = CompletedStages * StageSeconds;
            Progress = Mathf.Max(floor, Progress - _t.HijackDecayPerSecond * dt);
        }

        // A long tick can finish more than one stage; fire each in order.
        while (CompletedStages < Stages && Progress >= (CompletedStages + 1) * StageSeconds - 1e-4f)
        {
            CompletedStages++;
            string[] responses = _t.HijackResponses;
            string response = CompletedStages - 1 < responses.Length ? responses[CompletedStages - 1] : "";
            StageCompleted?.Invoke(CompletedStages, response);
        }

        if (CompletedStages >= Stages && !Complete)
        {
            Complete = true;
            Channelling = false;
            Finished?.Invoke();
        }
    }
}
