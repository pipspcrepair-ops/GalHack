using System;
using System.Collections.Generic;
using GalHack.Physics.Engine;
using Godot;
using Slice.Items;
using Slice.Surface;
using Slice.Ui;
using Slice.Vessels;
using Wildlife;
using Layers = GalHack.Physics.Layers;

namespace Slice.Game;

public partial class Game
{
    int _progressTick;

    /// <summary>Experience from playing: digging ore, cutting plants, building (crafting and kills are counted where they happen).</summary>
    void WireProgress()
    {
        ProgressRules.XpRules xp = Zone.Data.Progress.Xp;
        Tool.OreDug += cells => Progress.Earn(cells * xp.OreCell, "ore");
        Tool.PlantCut += () => Progress.Earn(xp.Plant, "plant cut");
        Builder.BlockPlaced += (v, earned) => { Stats.Placed++; Progress.Earn(earned, "building"); };
        // Experience counts quietly (the Tab menu shows it); a new level, an unlock and a recipe learnt are news, in the notices' slot.
        Progress.LevelledUp += (level, points) => Visor.LevelUp(level, points);
        Progress.Unlocked += n => Notices.Post(NoticeKind.Unlock, "UNLOCKED", n.Name);
        Progress.Learnt += n => Notices.Post(NoticeKind.Learnt, "LEARNT", n.Name);
        Builder.Claimed += name => Notices.Post(NoticeKind.Claimed, "CLAIMED", name);
    }

    /// <summary>Exploring experience, a few times a second: deposits you come near, species you meet.</summary>
    void TrackProgress()
    {
        if (++_progressTick % 15 != 0) return;
        ProgressRules.XpRules xp = Zone.Data.Progress.Xp;
        Vector3 feet = Survivor.WorldFeet;
        foreach (var (kind, at) in Visor.Deposits.Near(Zone, feet))
        {
            if (new Vector2(at.X - feet.X, at.Z - feet.Z).Length() > xp.DepositRadius) continue;
            string key = $"deposit:{Mathf.RoundToInt((float)Planets.Gen.Mathx.WrapX(at.X, Zone.Planet.Width))},{Mathf.RoundToInt((float)Planets.Gen.Mathx.Wrap(at.Z, Zone.Planet.Height))}";
            if (Progress.Discover(key, xp.Deposit, $"{kind} deposit found")) Stats.DepositsFound++;
        }
        if (Zone.Wildlife.Sim == null) return;
        Vector3 local = Zone.Wildlife.PatchLocal(feet);
        foreach (Creature c in Zone.Wildlife.Sim.Creatures)
        {
            if (c.Removed || c.Dead || (c.Position - local).Length() > 25f) continue;
            if (Progress.Discover($"species:{c.Species.Id}", xp.Species, $"met a {c.Species.CommonName}")) Stats.SpeciesMet++;
        }
        // And what lives in places and caves, met where it lives.
        foreach (DeepSite site in Zone.Wildlife.Sites)
        {
            Vector3 there = site.Local(feet);
            foreach (Creature c in site.Sim.Creatures)
            {
                if (c.Removed || c.Dead || (c.Position - there).Length() > 15f) continue;
                if (Progress.Discover($"species:{c.Species.Id}", xp.Species, $"met a {c.Species.CommonName}")) Stats.SpeciesMet++;
            }
        }
    }
}

/// <summary>The game's own numbers for the Stats tab: what you have done since the crash.</summary>
public sealed class GameStats
{
    public double Seconds;
    public float Walked, Flown, TopSpeed;
    public int Placed, Crafted, Kills, Deaths, Used, DepositsFound, SpeciesMet;
    Vector3? _last;

    public void Tick(Game game, float dt)
    {
        Seconds += dt;
        Vector3 at = game.Survivor.WorldFeet;
        if (_last is Vector3 last)
        {
            float d = new Vector2(at.X - last.X, at.Z - last.Z).Length();
            if (d < 50f)
            {
                if (game.Survivor.Seated != null) Flown += d;
                else Walked += d;
            }
        }
        _last = at;
        if (game.Survivor.Seated is Vessel v) TopSpeed = MathF.Max(TopSpeed, v.Body.Velocity.Length());
    }
}
