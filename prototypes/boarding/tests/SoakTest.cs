using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Boarding;

/// <summary>
/// Navigation soak test: 50 seeded runs of the crew moving around the ship under pressure
/// (alarms converging on random cells every few seconds, a reinforcement wave, random
/// security doors opened, and in every other run a firefight with the player, who keeps
/// relocating). Fails if any guard stays stuck for more than 3 s; prints the worst stuck time.
///
/// Run it with a fixed frame rate so it runs faster than real time:
///   godot --headless --fixed-fps 60 --path prototypes/boarding res://tests/SoakTest.tscn
/// </summary>
public partial class SoakTest : Node
{
    const int Runs = 50;
    const float RunSeconds = 8f;
    const float StuckLimit = 3f;

    public override async void _Ready()
    {
        float worst = 0f;
        string worstWhere = "none";
        var escalations = new int[4];
        int failedRuns = 0;
        try
        {
            for (int run = 0; run < Runs; run++)
            {
                int seed = 7000 + run;
                var (runWorst, who, steps) = await Soak(seed, combat: run % 2 == 0);
                for (int i = 0; i < 4; i++) escalations[i] += steps[i];
                if (runWorst > StuckLimit) failedRuns++;
                if (runWorst > worst)
                {
                    worst = runWorst;
                    worstWhere = $"seed {seed}, {who}";
                }
                GD.Print($"run {run + 1,2}/{Runs} seed {seed} {(run % 2 == 0 ? "combat " : "alarms ")} worst stuck {runWorst:F2} s");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"FAIL  exception: {e}");
            failedRuns = Runs;
        }
        GD.Print($"Worst stuck time over {Runs} runs: {worst:F2} s ({worstWhere}); limit {StuckLimit:F1} s");
        GD.Print($"Unstick escalations: {escalations[0]} re-paths, {escalations[1]} side-steps, {escalations[2]} walks to a free cell, {escalations[3]} snaps");
        GD.Print(failedRuns == 0 ? "SOAK PASSED" : $"SOAK FAILED: {failedRuns} run(s) had a guard stuck over {StuckLimit} s");
        GetTree().Quit(failedRuns == 0 ? 0 : 1);
    }

    async Task<(float worst, string who, int[] steps)> Soak(int seed, bool combat)
    {
        GD.Seed((ulong)seed);
        var rng = new Random(seed);
        var main = new Main { Name = "Main" };
        AddChild(main);
        await Ticks(2);

        Ship ship = main.Ship;
        main.Player.TestInvulnerable = true;
        main.PlayerHidden = !combat;
        foreach (Door door in ship.Doors.ToList())
            if (door.Locked && rng.NextDouble() < 0.5) door.Unlock();

        var open = new List<Vector2I>();
        for (int r = 0; r < main.Layout.Height; r++)
        for (int c = 0; c < main.Layout.Width; c++)
            if (ship.Passable(new Vector2I(c, r)) && main.Layout[new Vector2I(c, r)] == CellKind.Floor) open.Add(new Vector2I(c, r));
        Vector2I RandomCell() => open[rng.Next(open.Count)];

        if (combat) main.Player.DebugPlace(ship.ToGlobal(main.Layout.CellCenter(RandomCell()) + Vector3.Up * 0.05f));
        main.SpawnWave();

        float t = 0f, nextAlarm = 0.5f, nextMove = 4f;
        while (t < RunSeconds)
        {
            if (t >= nextAlarm)
            {
                main.Alarm.ReportLoudNoise(main.Layout.CellCenter(RandomCell()));
                nextAlarm += 2.5f;
            }
            if (combat && t >= nextMove)
            {
                main.Player.DebugPlace(ship.ToGlobal(main.Layout.CellCenter(RandomCell()) + Vector3.Up * 0.05f));
                nextMove += 4f;
            }
            await Ticks(1);
            t += 1f / Engine.PhysicsTicksPerSecond;
        }

        Guard stuckest = main.Guards.OrderByDescending(g => g.WorstStuck).First();
        var steps = new int[4];
        foreach (Guard g in main.Guards)
            for (int i = 0; i < 4; i++) steps[i] += g.Escalations[i];
        var result = (stuckest.WorstStuck, $"{stuckest.Name} at cell {main.Layout.CellAt(stuckest.Position)}", steps);

        main.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Ticks(1);
        return result;
    }

    async Task Ticks(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
