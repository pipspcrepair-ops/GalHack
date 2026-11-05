using System.Linq;
using System.Threading.Tasks;
using Godot;
using Invasions.Combat;
using Invasions.Director;
using Invasions.World;

namespace Invasions.Tests;

/// <summary>
/// The options' invasions switch: off stops new invasions and calls off or pulls back any under way,
/// on resumes on the usual timing with full warnings, and the choice is saved with the world.
/// </summary>
public partial class InvasionTest
{
    /// <summary>The shortest countdown a nearby base can draw: the interval at full jitter the short way.</summary>
    double ShortestInterval => _server.InvasionIntervalMinutes * 60 * (1 - _server.IntervalJitter);

    void InvasionsOffStopsNewInvasions()
    {
        InvasionDirector d = NewDirector();
        d.SetPlayersPresent("home", 1);
        d.SetInvasionsEnabled(false);
        d.Advance(LongestInterval * 10);
        Check(!d.Log.Any(e => e.Kind == DirectorEventKind.InvasionScheduled) && !d.StartInvasionNow("home") && d.NextInvasionIn("home") == null,
            $"with invasions off, none is scheduled in ten intervals with a player home ({LongestInterval * 10 / 3600:F0} h simulated), and 'invade now' is refused");
    }

    void InvasionsOffCancelsTheWarnings()
    {
        InvasionDirector d = NewDirector();
        d.SetPlayersPresent("home", 1);
        d.StartInvasionNow("home");
        Invasion inv = d.Base("home").Active;
        d.Advance(300 + 1); // just past the five-minute warning
        int before = d.Log.Count(e => e.Kind == DirectorEventKind.Warning && e.InvasionId == inv.Id);
        d.SetInvasionsEnabled(false);
        d.Advance(900);
        int after = d.Log.Count(e => e.Kind == DirectorEventKind.Warning && e.InvasionId == inv.Id);
        Check(before == 2 && after == 2 && inv.Phase == InvasionPhase.Ended && inv.Outcome == InvasionOutcome.CalledOff &&
              !d.Log.Any(e => e.Kind == DirectorEventKind.ForceArrived) &&
              d.Log.Any(e => e.Kind == DirectorEventKind.InvasionsToggled && e.Text == _data.Message("disabled")),
            $"switched off during the five-minute warning, the invasion is called off: no more warnings ({before} → {after}), no arrival, and a notice");

        InvasionDirector s = NewDirector();
        s.SetPlayersPresent("home", 1);
        s.StartInvasionNow("home");
        Invasion sounding = s.Base("home").Active;
        s.Advance(600 - 60 + 1);
        bool wasOn = sounding.SirenOn;
        s.SetInvasionsEnabled(false);
        Check(wasOn && !sounding.SirenOn && s.Log.Last(e => e.Kind == DirectorEventKind.SirenOff).Time == s.Now,
            "switched off during the one-minute warning, the sirens stop at once");
    }

    void InvasionsOffWithdrawsAnAssault()
    {
        InvasionDirector d = NewDirector();
        d.SetPlayersPresent("home", 1);
        d.StartInvasionNow("home");
        Invasion inv = d.Base("home").Active;
        d.Advance(601);
        d.ReportInvadersRemaining("home", 12);
        d.SetInvasionsEnabled(false);
        DirectorEvent withdraw = d.Log.LastOrDefault(e => e.Kind == DirectorEventKind.Withdraw);
        Check(inv.Phase == InvasionPhase.Withdrawing && inv.Outcome == InvasionOutcome.CalledOff && inv.SirenOn && withdraw?.Text.Contains("Invasions disabled") == true,
            $"switched off mid-assault, the invaders withdraw, as when their faction stands down; the sirens sound until they're gone: \"{withdraw?.Text}\"");
        d.ReportInvadersRemaining("home", 0);
        Check(inv.Phase == InvasionPhase.Ended && !inv.SirenOn, "and once the last is gone it ends and the sirens stop");
    }

    void InvasionsBackOnResumeWithFullWarnings()
    {
        InvasionDirector d = NewDirector();
        d.SetPlayersPresent("home", 1);
        d.SetInvasionsEnabled(false);
        d.Advance(LongestInterval * 2);
        d.SetInvasionsEnabled(true);
        double on = d.Now;
        bool notAtOnce = d.Base("home").Active == null && d.NextInvasionIn("home") >= ShortestInterval;
        bool came = RunUntil(d, () => d.Base("home").Active?.Phase == InvasionPhase.Assault, LongestInterval * 2);
        Invasion inv = d.Base("home").History.LastOrDefault();
        var warnings = d.Log.Where(e => e.Kind == DirectorEventKind.Warning && e.InvasionId == inv?.Id).ToList();
        Check(notAtOnce && came && inv.ScheduledAt - on >= ShortestInterval && warnings.Count == 3 &&
              warnings[0].Time == inv.ArrivalAt - 600 && warnings[1].Time == inv.ArrivalAt - 300 && warnings[2].Time == inv.ArrivalAt - 60,
            $"switched back on, the next invasion comes on the usual timing ({(inv?.ScheduledAt - on ?? 0) / 60:F0} min later, not at once) with all three warnings");
    }

    async Task OptionsSwitchInvasionsOffAndSaveIt()
    {
        const string file = "user://test-world.json";
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(file));
        Main.WorldFile = file;
        try
        {
            Main m = await NewWorld();
            m.Combat.TurretsEnabled = false;
            m.Player.Invulnerable = true;
            int total = m.Model.Loot.TotalItems;
            m.Controller.InvadeNow();
            m.Controller.SkipToArrival();
            m.Controller.SpawnPendingNow();
            await RunUntil(() => m.Combat.Troops.Count(t => t.IsAlive) >= 4, 60);
            var landed = m.Combat.Troops.Where(t => t.IsAlive && !t.IsGone).ToList();
            m.Options.Open();
            bool shownOn = m.Options.IsOpen && m.Options.InvasionsSwitch.ButtonPressed;
            m.Options.InvasionsSwitch.ButtonPressed = false; // the player flips the switch
            await Run(0.5);
            Invasion inv = m.Director.Base(InvasionController.HomeId).History.Last();
            int aboard = landed.Count(t => t.IsGone);
            Check(shownOn && !m.Director.InvasionsEnabled && inv.Phase == InvasionPhase.Withdrawing && landed.Count >= 4 &&
                  landed.All(t => t.IsGone || t.State == TroopState.Withdrawing) &&
                  m.Controller.Heard.Any(e => e.Kind == DirectorEventKind.InvasionsToggled) && m.Hud.BannerText.Contains("Invasions disabled"),
                $"Esc, Invasions: Off mid-assault: all {landed.Count} troops on foot withdraw ({aboard} already back aboard), with a notice on the visor");
            bool ended = await RunUntil(() => m.Controller.Current == null, 240);
            Check(ended && !m.Base.Sirens.On && m.Model.Loot.TotalItems == total,
                $"they leave, the sirens stop, and nothing is undone or taken ({m.Model.Loot.TotalItems} of {total} items)");

            // The choice is saved with the world: a fresh start of the same world has invasions off.
            Main again = await NewWorld();
            again.Options.Open();
            bool off = !again.Director.InvasionsEnabled && !again.Options.InvasionsSwitch.ButtonPressed && again.Controller.InvadeNow() != "";
            again.Options.InvasionsSwitch.ButtonPressed = true;
            await Run(0.25);
            Check(off && again.Director.InvasionsEnabled && again.Controller.Current == null &&
                  again.Director.NextInvasionIn(InvasionController.HomeId) >= ShortestInterval - 1,
                "the setting is saved with the world: reloaded, invasions are still off; switched on, the next comes on the usual timing, not at once");
        }
        finally
        {
            Main.WorldFile = null;
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(file));
        }
    }
}
