using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>
/// Leaves a dressed room as the collapse left it, by the POI's state and age: a few frozen moments
/// first (a chair pushed back from a set table, a tray dropped mid-aisle, a gurney left out, a
/// door left open), then pieces gone, knocked over, scattered, broken, burnt or overgrown. Every
/// change is kept only if nothing overlaps, nothing goes through a wall, the doorways stay clear
/// and a walker still gets from door to door. Deterministic from the room's seed.
/// </summary>
public sealed class DecoDisorder
{
    readonly DecoForge _forge;
    readonly DecoArrangements _arr;
    DecoPlan _plan;
    DecoRoom _room;
    DecoRaster _raster;
    DecoRng _rng;
    List<DecoRect> _aprons;

    public DecoDisorder(DecoForge forge, DecoArrangements arr)
    {
        _forge = forge;
        _arr = arr;
    }

    public void Apply(DecoPlan plan)
    {
        _plan = plan;
        _room = plan.Room;
        _rng = new DecoRng(DecoHash.Combine(_room.Seed, 0xD150DE7UL));
        DecoDisorderRules rules = _arr.Disorder.TryGetValue(_room.State, out var r) ? r : _arr.Disorder["abandoned"];
        DecoAge age = _room.AgeOrDefault;
        // Nothing is pushed, dropped or left out onto a doorway's apron or a patch of floor that can't take it.
        _aprons = _room.Doors.Select(d => _room.Apron(d, 1.0f, 0.1f)).Concat(_room.Blocked).ToList();
        _raster = new DecoRaster(_room.Width, _room.Depth, DecoPlanner.Cell);
        foreach (DecoPlacement pl in _plan.Placements.Where(p => DecoLayout.Blocks(_forge, p))) _raster.Add(DecoLayout.Footprint(_forge, pl), 1);

        // Power, age and weather first: they don't move anything.
        bool stillRunning = false;
        foreach (DecoPlacement pl in _plan.Placements)
        {
            DecoPiece p = _forge.Catalogue.Piece(pl.Piece);
            pl.Age = age;
            pl.Exposed = _room.Exposed;
            pl.Powered = _rng.Chance(rules.Powered);
            if (!pl.Powered && !stillRunning && (p.Light != null || p.Anim != null) && _rng.Chance(rules.StillPowered))
            {
                pl.Powered = true;
                stillRunning = true;
                pl.Note = "still running on a long-life cell";
            }
            if (_room.KitBody != null || _room.KitTrim != null)
            {
                string id = $"{pl.Palette}+kit{(_room.KitBody?.ToHex() ?? "")}{(_room.KitTrim?.ToHex() ?? "")}";
                _forge.AddPalette(_forge.Palette(pl.Palette).Tinted(_room.KitBody, _room.KitTrim, id));
                pl.Palette = id;
            }
        }

        int touches = _rng.Between(rules.TouchesMin, rules.TouchesMax);
        var moments = new List<Func<bool>> { PushBack, DropTray, LeaveOut, LeaveOpen };
        for (int i = 0, tries = 0; i < touches && tries < 12; tries++)
            if (moments[_rng.Int(moments.Count)]()) i++;

        // Gone: carried off or destroyed.
        for (int i = _plan.Placements.Count - 1; i >= 0; i--)
            if (_plan.Placements[i].Support < 0 && _rng.Chance(rules.Missing)) Remove(i, "gone");

        // Fire spreads from one or two points; growth creeps in from one side.
        var fires = Enumerable.Range(0, rules.Burn > 0 ? 1 + _rng.Int(2) : 0).Select(_ => new Vector2(_rng.Range(0, _room.Width), _rng.Range(0, _room.Depth))).ToList();
        Vector2 growthFrom = new(_rng.Range(0, _room.Width), _rng.Chance(0.5f) ? 0 : _room.Depth);
        float reach = MathF.Max(_room.Width, _room.Depth);

        for (int i = 0; i < _plan.Placements.Count; i++)
        {
            DecoPlacement pl = _plan.Placements[i];
            DecoPiece p = _forge.Catalogue.Piece(pl.Piece);
            if (pl.Support >= 0) continue;
            if (p.Has(DecoState.Knocked) && p.Falls.Count > 0 && _rng.Chance(rules.Knock)) Knock(i);
            else if (!p.Fixed && p.Width * p.Depth < 1.3f && pl.Fall == DecoFall.None && _rng.Chance(rules.Scatter)) Scatter(i);

            var pos = new Vector2(pl.X, pl.Z);
            float burn = fires.Count == 0 ? 0 : rules.Burn * 2.2f * MathF.Max(0, 1 - fires.Min(f => Vector2.Distance(f, pos)) / (reach * 0.45f));
            float grow = rules.Overgrow * (_room.Exposed ? 1.6f : 1f) * (1.4f - Vector2.Distance(growthFrom, pos) / reach);
            DecoState s = DecoState.Intact;
            if (p.Has(DecoState.Burnt) && _rng.Chance(burn)) s = DecoState.Burnt;
            else if (p.Has(DecoState.Broken) && _rng.Chance(rules.Break)) s = DecoState.Broken;
            else if (p.Has(DecoState.Overgrown) && _rng.Chance(grow)) s = DecoState.Overgrown;
            else if (p.Has(DecoState.Rusted) && age == DecoAge.OldWorld && (_room.Exposed || p.Outdoor) && _rng.Chance(0.6f)) s = DecoState.Rusted;
            if (s != DecoState.Intact) Restate(i, s, rules.Growth);
        }
        foreach (DecoPlacement pl in _plan.Placements.Where(p => p.Support >= 0))
        {
            DecoPlacement under = _plan.Placements[pl.Support];
            DecoPiece p = _forge.Catalogue.Piece(pl.Piece);
            if (under.State != DecoState.Intact && p.Has(under.State)) { pl.State = under.State; pl.Cover = under.Cover; }
        }
        _plan.Notes.Add($"disorder: {_room.State}, {age}{(_room.Exposed ? ", exposed" : "")}, {touches} touches");
    }

    // ---- keeping every change legal --------------------------------------------------------------

    /// <summary>Applies a change to one placement (and the things standing on it), keeping it only if the room stays legal.</summary>
    bool Try(int i, Action<DecoPlacement> change, string note = null)
    {
        DecoPlacement pl = _plan.Placements[i];
        DecoPlacement before = pl.Clone();
        bool blocked = DecoLayout.Blocks(_forge, pl);
        if (blocked) _raster.Add(DecoLayout.Footprint(_forge, pl), -1);
        change(pl);
        bool ok = Legal(i);
        bool nowBlocks = DecoLayout.Blocks(_forge, pl);
        if (ok && nowBlocks)
        {
            _raster.Add(DecoLayout.Footprint(_forge, pl), 1);
            if (!_raster.DoorsJoined(_room)) { _raster.Add(DecoLayout.Footprint(_forge, pl), -1); ok = false; }
        }
        if (!ok)
        {
            _plan.Placements[i] = before;
            if (blocked) _raster.Add(DecoLayout.Footprint(_forge, before), 1);
            return false;
        }
        if (note != null) pl.Note = note;
        // Whatever stood on it moves with it.
        float dx = pl.X - before.X, dz = pl.Z - before.Z, dyaw = pl.Yaw - before.Yaw;
        if (dx != 0 || dz != 0 || dyaw != 0)
            foreach (DecoPlacement top in _plan.Placements.Where(t => t.Support == i))
            {
                Vector2 rel = DecoGeo.Yaw(new Vector2(top.X - before.X, top.Z - before.Z), dyaw);
                top.X = pl.X + rel.X; top.Z = pl.Z + rel.Y; top.Yaw += dyaw;
            }
        return true;
    }

    bool Legal(int i)
    {
        DecoPlacement pl = _plan.Placements[i];
        DecoRect r = DecoLayout.Footprint(_forge, pl);
        if (!DecoLayout.Inside(_room, r)) return false;
        if (pl.Support < 0 && _aprons.Any(a => a.Overlaps(r.Shrink(0.01f)))) return false;
        for (int j = 0; j < _plan.Placements.Count; j++)
        {
            if (j == i) continue;
            DecoPlacement o = _plan.Placements[j];
            if (o.Support == i || pl.Support == j) continue;
            if (DecoLayout.Clash(_forge, o, pl)) return false;
            // Litter can't lie under anything else that stands on the floor.
            if (pl.Support < 0 && o.Support < 0 && (_forge.Catalogue.Piece(pl.Piece).WalkOver || _forge.Catalogue.Piece(o.Piece).WalkOver)
                && DecoLayout.Footprint(_forge, o).Shrink(0.01f).Overlaps(r.Shrink(0.01f)) && !(_forge.Catalogue.Piece(pl.Piece).WalkOver && _forge.Catalogue.Piece(o.Piece).WalkOver && o.Assembly >= 0)) return false;
        }
        return true;
    }

    void Remove(int i, string why)
    {
        DecoPlacement pl = _plan.Placements[i];
        if (DecoLayout.Blocks(_forge, pl)) _raster.Add(DecoLayout.Footprint(_forge, pl), -1);
        // Things on it go too.
        for (int j = _plan.Placements.Count - 1; j >= 0; j--)
            if (_plan.Placements[j].Support == i) { RemoveAt(j); if (j < i) i--; }
        RemoveAt(i);
        _plan.Notes.Add($"{pl.Piece} {why}");
    }

    void RemoveAt(int i)
    {
        _plan.Placements.RemoveAt(i);
        foreach (DecoPlacement p in _plan.Placements)
        {
            if (p.Support == i) p.Support = -1;
            else if (p.Support > i) p.Support--;
        }
        foreach (DecoPlanCable c in _plan.Cables)
        {
            if (c.From > i) c.From--;
            if (c.To > i) c.To--;
        }
    }

    // ---- changes ----------------------------------------------------------------------------------

    void Knock(int i)
    {
        DecoPlacement pl = _plan.Placements[i];
        DecoPiece p = _forge.Catalogue.Piece(pl.Piece);
        var falls = p.Falls.ToList();
        _rng.Shuffle(falls);
        foreach (DecoFall f in falls)
        {
            if (Try(i, x => { x.Fall = f; x.Y = 0; }, "knocked over"))
            {
                // Whatever stood on it fell too; what finds no clear floor beside it smashed and is swept into the debris.
                foreach (int t in Enumerable.Range(0, _plan.Placements.Count).Where(t => _plan.Placements[t].Support == i).OrderByDescending(t => t).ToList())
                    if (!Drop(t)) Remove(t, "fell and smashed");
                return;
            }
        }
    }

    void Scatter(int i)
    {
        float dx = _rng.Range(-0.4f, 0.4f), dz = _rng.Range(-0.4f, 0.4f), dyaw = _rng.Range(-35, 35);
        Try(i, x => { x.X += dx; x.Z += dz; x.Yaw += dyaw; }, "scattered");
    }

    void Restate(int i, DecoState s, DecoCover growth)
    {
        Try(i, x => { x.State = s; x.Cover = s == DecoState.Overgrown ? growth : x.Cover; });
    }

    /// <summary>Something on a table or desk ends up on the floor beside it.</summary>
    bool Drop(int t)
    {
        DecoPlacement top = _plan.Placements[t];
        DecoPlacement under = _plan.Placements[top.Support];
        DecoPiece p = _forge.Catalogue.Piece(top.Piece);
        DecoRect ur = DecoLayout.Footprint(_forge, under);
        for (int k = 0; k < 10; k++)
        {
            float a = _rng.Range(0, MathF.Tau), d = ur.U.Length() + ur.V.Length() * 0.5f + _rng.Range(0.2f, 0.9f);
            var at = ur.Centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
            DecoFall fall = p.Has(DecoState.Knocked) ? p.Falls[_rng.Int(p.Falls.Count)] : DecoFall.None;
            int support = top.Support;
            if (Try(t, x => { x.X = at.X; x.Z = at.Y; x.Y = 0; x.Support = -1; x.Fall = fall; x.Yaw = _rng.Range(0, 360); }, "dropped mid-aisle"))
                return true;
            top.Support = support;
        }
        _plan.Placements[t].Note = "left where it stood";
        return false;
    }

    // ---- frozen moments ---------------------------------------------------------------------------

    /// <summary>A chair pushed back from a set table, turned as whoever sat there got up.</summary>
    bool PushBack()
    {
        var chairs = Enumerable.Range(0, _plan.Placements.Count).Where(i => _plan.Placements[i].Role is "chair" or "stool" or "bench-seat" && _plan.Placements[i].Group >= 0 && _plan.Placements[i].Fall == DecoFall.None).ToList();
        if (chairs.Count == 0) return false;
        int c = chairs[_rng.Int(chairs.Count)];
        DecoPlacement pl = _plan.Placements[c];
        Vector2 back = DecoGeo.Yaw(new Vector2(0, 1), pl.Yaw);
        float d = _rng.Range(0.3f, 0.55f), turn = _rng.Range(-25, 25);
        return Try(c, x => { x.X += back.X * d; x.Z += back.Y * d; x.Yaw += turn; }, "pushed back from the table");
    }

    /// <summary>A tray dropped mid-aisle.</summary>
    bool DropTray()
    {
        var trays = Enumerable.Range(0, _plan.Placements.Count).Where(i => _plan.Placements[i].Support >= 0).ToList();
        return trays.Count > 0 && Drop(trays[_rng.Int(trays.Count)]);
    }

    /// <summary>A gurney, cart or wheelchair left out in the room at an angle, never across the way through.</summary>
    bool LeaveOut()
    {
        var movers = Enumerable.Range(0, _plan.Placements.Count).Where(i =>
        {
            DecoPiece p = _forge.Catalogue.Piece(_plan.Placements[i].Piece);
            return _plan.Placements[i].Support < 0 && (p.Has("gurney") || p.Has("cart") || p.Has("wheelchair") || p.Has("luggage") || p.Has("trailer"));
        }).ToList();
        if (movers.Count == 0) return false;
        int m = movers[_rng.Int(movers.Count)];
        for (int k = 0; k < 8; k++)
        {
            float x = _rng.Range(1, _room.Width - 1), z = _rng.Range(1, _room.Depth - 1), yaw = _rng.Range(0, 360);
            if (Try(m, p => { p.X = x; p.Z = z; p.Yaw = yaw; }, "left out where it was last used")) return true;
        }
        return false;
    }

    /// <summary>A cupboard, locker or vehicle door left standing open.</summary>
    bool LeaveOpen()
    {
        var doors = Enumerable.Range(0, _plan.Placements.Count).Where(i => !_plan.Placements[i].Doors && _forge.Model(_plan.Placements[i].Piece).Parts.Any(p => p.Is(PartFlags.Door))).ToList();
        if (doors.Count == 0) return false;
        int d = doors[_rng.Int(doors.Count)];
        return Try(d, x => x.Doors = true, "door left open");
    }
}
