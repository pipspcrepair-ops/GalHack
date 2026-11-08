using System.Collections.Generic;

namespace PlanetSurface.Fx;

/// <summary>
/// The pop-in log: everything drawn that came into view, went, or changed how it is drawn, how far
/// off, how big it is and how much of its look changed in one frame. Off unless a test or a
/// measurement turns it on. Each drawing system notes its own changes where it makes them (far
/// flora, ground cover, places' far and merged blocks, places' full detail, props, creatures), so
/// the log sees what the screen would without rendering, and a run can go headless.
/// </summary>
public static class PopLog
{
    /// <summary>Whether changes are noted at all.</summary>
    public static bool On;

    /// <summary>
    /// One change: what kind of thing (flora, cover, place, city, detail, props, creature), which, how
    /// far off it was (m, to its nearest part), how big (m, its height or its side), and how much of
    /// it changed in the one frame (0: nothing, as a fade a frame at a time; 1: a hard swap, or all of
    /// it appearing or going at once).
    /// </summary>
    public readonly record struct Pop(string Kind, string What, float Distance, float Size, float Jump);

    static readonly List<Pop> _pops = new();

    /// <summary>Notes a change. A change of under a thousandth of the look is no change.</summary>
    public static void Note(string kind, string what, float distance, float size, float jump)
    {
        if (!On || jump < 0.001f) return;
        lock (_pops) _pops.Add(new Pop(kind, what, distance, size, jump));
    }

    /// <summary>Everything noted since the last take, and starts over.</summary>
    public static List<Pop> Take()
    {
        lock (_pops)
        {
            var all = new List<Pop>(_pops);
            _pops.Clear();
            return all;
        }
    }
}
