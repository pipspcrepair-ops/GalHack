using System;
using System.Collections.Generic;
using System.Linq;

namespace DiegeticUi.Logic;

/// <summary>Something coming: an invasion fleet, a storm front.</summary>
public sealed class Threat
{
    public string Id { get; init; }
    public string Title { get; init; }
    public double ArrivesAt { get; init; }

    /// <summary>The minute marks already announced, so each fires exactly once.</summary>
    internal readonly HashSet<int> Announced = new();
}

/// <summary>One announcement: "10 minutes", "5 minutes", "1 minute", or 0 for "arrived".</summary>
public readonly record struct Announcement(Threat Threat, int MinutesLeft, double At);

/// <summary>
/// Counts down to threats and announces them at 10, 5 and 1 minutes, then on arrival. Runs
/// on its own clock (seconds), so tests and the debug key can run it in fast time.
///
/// A big step that crosses several marks announces each one, in order, and never twice.
/// A threat scheduled closer than a mark skips that mark rather than announcing a stale one.
/// </summary>
public sealed class WarningTimeline
{
    public static readonly int[] MarksMinutes = { 10, 5, 1 };

    readonly List<Threat> _threats = new();

    public double Now { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<Threat> Active => _threats;

    public Threat Schedule(string id, string title, double secondsFromNow)
    {
        var threat = new Threat { Id = id, Title = title, ArrivesAt = Now + secondsFromNow };
        // Marks already well behind us at scheduling time are stale: don't announce them.
        foreach (int mark in MarksMinutes)
            if (secondsFromNow < mark * 60 - 1) threat.Announced.Add(mark);
        _threats.Add(threat);
        Version++;
        return threat;
    }

    public double Remaining(Threat threat) => Math.Max(0, threat.ArrivesAt - Now);

    /// <summary>The most urgent threat still counting down, or null.</summary>
    public Threat Next => _threats.Where(t => t.ArrivesAt > Now).OrderBy(t => t.ArrivesAt).FirstOrDefault();

    public List<Announcement> Advance(double dt) => AdvanceTo(Now + dt);

    public List<Announcement> AdvanceTo(double now)
    {
        var fired = new List<Announcement>();
        if (now < Now) return fired;
        double before = Now;
        Now = now;

        foreach (Threat threat in _threats)
        {
            foreach (int mark in MarksMinutes)
            {
                double at = threat.ArrivesAt - mark * 60;
                if (at <= now && !threat.Announced.Contains(mark))
                {
                    threat.Announced.Add(mark);
                    fired.Add(new Announcement(threat, mark, Math.Max(at, before)));
                }
            }
            if (threat.ArrivesAt <= now && threat.Announced.Add(0))
                fired.Add(new Announcement(threat, 0, threat.ArrivesAt));
        }

        // Whole seconds of countdown changed: screens showing it need one redraw.
        if (_threats.Count > 0 && (int)before != (int)now) Version++;
        if (fired.Count > 0) Version++;
        _threats.RemoveAll(t => t.ArrivesAt < now - 30); // arrived a while ago: done
        fired.Sort((a, b) => a.At.CompareTo(b.At));
        return fired;
    }
}
