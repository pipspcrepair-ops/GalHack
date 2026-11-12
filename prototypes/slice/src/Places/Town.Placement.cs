using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Places;

/// <summary>
/// People where they'd be: each to the rooms their job is done in (a cook in the galley, a medic on
/// the ward, a mechanic in the workshop or the plant room, a guard in the guardroom), guards on the
/// lookouts as well as the way in, and in the evening those off duty gathered at the fire or in the
/// common rooms before bed. The day itself (out at their hours, home to bed at night) is the town's.
/// </summary>
public sealed partial class Town
{
    /// <summary>The rooms (by the generator's purposes) each job is done in, best first.</summary>
    public static readonly IReadOnlyDictionary<PersonRole, string[]> RoomsFor = new Dictionary<PersonRole, string[]>
    {
        [PersonRole.Medic] = new[] { "ward", "nurses", "theatre", "pharmacy", "quarantine", "decon" },
        [PersonRole.Worker] = new[] { "galley", "farm-kitchen", "mess", "storage", "warehouse", "loading-dock", "parts-store", "workshop", "office", "archive", "lab", "cold-store" },
        [PersonRole.Mechanic] = new[] { "workshop", "plant", "generator", "reactor", "boiler-house", "pump-hall", "parts-store", "control" },
        [PersonRole.Farmer] = new[] { "hydroponics", "grow-room", "farm-kitchen", "cellar" },
        [PersonRole.Trader] = new[] { "shop", "market-hall" },
        [PersonRole.Guard] = new[] { "security", "guardroom", "armoury", "monitor", "airlock", "lobby" },
        [PersonRole.Lawman] = new[] { "security", "guardroom", "cellblock", "evidence", "lobby" },
        [PersonRole.Elder] = new[] { "chapel", "mess", "lobby" },
        [PersonRole.Drunk] = new[] { "mess", "galley", "lobby" },
        [PersonRole.Scavenger] = new[] { "storage", "warehouse", "loading-dock" },
    };

    /// <summary>Rooms people gather in of an evening.</summary>
    static readonly string[] Common = { "mess", "chapel", "lobby", "market-hall", "farm-kitchen", "galley" };

    /// <summary>Each person's own place of work (a spot in a room their job is done in), or -1.</summary>
    readonly Dictionary<Person, int> _station = new();

    /// <summary>Guards sent up to a lookout (tests).</summary>
    public int Lookouts { get; private set; }

    string PurposeOf(int spot)
    {
        if (spot < 0 || spot >= Spots.Count) return "";
        int m = Spots[spot].Module;
        return m >= 0 && m < Memory.Inst.Layout.Modules.Count ? Memory.Inst.Layout.Modules[m].Purpose ?? "" : "";
    }

    /// <summary>Whether a spot is in a room this person's job is done in.</summary>
    public bool ForJob(Person p, int spot) => RoomsFor.TryGetValue(p.Job, out string[] rooms) && Array.IndexOf(rooms, PurposeOf(spot)) >= 0;

    /// <summary>
    /// Once everyone is rolled (their jobs known): each to their place of work where the place has a
    /// room for it, starting there; and where there are two guards or more, one up on the lookout (the
    /// highest spot a person can stand on, well above the way in) or in the guardroom.
    /// </summary>
    public void Stations()
    {
        _station.Clear();
        var taken = new HashSet<int>();
        foreach (Person p in People)
        {
            if (p.Job is PersonRole.Guard or PersonRole.Child) continue;
            int best = -1, bestD = int.MaxValue;
            for (int i = 0; i < Spots.Count; i++)
            {
                if (!ForJob(p, i) || Spots[i].Tag is "bed" or "home" || taken.Contains(i) && Spots[i].Tag == "sit") continue;
                int d = Spots[i].Cell.Manhattan(p.Spawn.Cell) + (taken.Contains(i) ? 6 : 0);
                if (d < bestD) (best, bestD) = (i, d);
            }
            if (best < 0) continue;
            _station[p] = best;
            taken.Add(best);
            // They start at their work (nobody is drawn yet: nothing appears in view).
            if (PathBetween(p.Spot, best) != null || p.Spot == best)
            {
                p.Spot = best;
                p.Pos = At(best);
            }
        }
        var guards = People.Where(q => q.Job == PersonRole.Guard || q.Role == "guard").ToList();
        if (guards.Count < 2) return;
        int gateY = Memory.Inst.Layout.EntranceCell.Y;
        int lookout = -1;
        for (int i = 0; i < Spots.Count; i++)
        {
            bool up = Spots[i].Cell.Y >= gateY + 3, room = Array.IndexOf(RoomsFor[PersonRole.Guard], PurposeOf(i)) is >= 0 and < 4;
            if (!up && !room || Spots[i].Tag is "bed" or "home") continue;
            if (lookout < 0 || Spots[i].Cell.Y > Spots[lookout].Cell.Y) lookout = i;
        }
        if (lookout < 0 || PathBetween(guards[^1].Spot, lookout) == null) return;
        Person g = guards[^1];
        g.Spawn = g.Spawn with { Spot = lookout, Cell = Spots[lookout].Cell };
        g.Spot = lookout;
        g.Pos = At(lookout);
        Lookouts++;
    }

    /// <summary>A spot of these tags in a room this person's job is done in, most of the time (null: go by the tags alone).</summary>
    int? Fitting(Person p, string[] tags)
    {
        if (_rng.NextDouble() > 0.7) return null;
        if (_station.TryGetValue(p, out int own) && _rng.NextDouble() < 0.5) return own;
        var fit = Spots.Select((s, i) => (s, i)).Where(t => (tags == null || Array.IndexOf(tags, t.s.Tag) >= 0) && ForJob(p, t.i)).Select(t => t.i).ToList();
        return fit.Count == 0 ? null : fit[_rng.Next(fit.Count)];
    }

    /// <summary>Evening: a while before bed (each job's own day permitting).</summary>
    bool Evening()
    {
        if (_places == null) return false;
        float t = (float)_places.Zone.Sky.TimeOfDay;
        return t > 0.7f && t < 0.8f;
    }

    /// <summary>
    /// Of an evening those not on duty gather: at the fire where the place has one, else in its
    /// common rooms (a seat there), else by whoever else is out; and stay a good while.
    /// </summary>
    bool Gather(Person p)
    {
        if (!Evening() || p.Job is PersonRole.Guard or PersonRole.Lawman || p.Role == "guard" || _rng.NextDouble() > 0.6) return false;
        int? at = null;
        var fires = Memory.Inst.Markers.Where(m => m.Kind == "campfire").ToList();
        if (fires.Count > 0)
        {
            PInt3 fire = fires[0].Cell;
            var near = Spots.Select((s, i) => (s, i)).Where(t => t.s.Tag is not ("bed" or "home") && t.s.Cell.Manhattan(fire) <= 6).OrderBy(_ => _rng.Next()).FirstOrDefault();
            if (near.s != null) at = near.i;
        }
        if (at == null)
        {
            var common = Spots.Select((s, i) => (s, i)).Where(t => t.s.Tag == "sit" && Array.IndexOf(Common, PurposeOf(t.i)) >= 0).Select(t => t.i).ToList();
            if (common.Count > 0) at = common[_rng.Next(common.Count)];
        }
        if (at == null) return false;
        GoTo(p, at.Value);
        p.Wait = 20f + (float)_rng.NextDouble() * 25f;
        // Someone else gathered here already: they talk.
        p.Partner = People.FirstOrDefault(o => o != p && !o.Dead && !o.Indoors && o.Spot == at && o.Partner == null);
        if (p.Partner != null) { p.Partner.Partner = p; p.Partner.Wait = MathF.Max(p.Partner.Wait, p.Wait); }
        return true;
    }
}
