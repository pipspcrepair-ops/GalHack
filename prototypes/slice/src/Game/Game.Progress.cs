using System;
using System.Collections.Generic;
using GalHack.Physics.Engine;
using Godot;
using Slice.Items;
using Slice.Surface;
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
        Builder.BlockPlaced += (v, small) => { Stats.Placed++; Progress.Earn(small ? xp.BlockSmall : xp.BlockLarge, "building"); };
        Progress.Earned += (n, why) => Visor.Xp(n, why);
        Progress.LevelledUp += (level, points) =>
        {
            Visor.LevelUp(level, points);
            Say($"LEVEL {level}: +{points} unlock point{(points == 1 ? "" : "s")} (Tab, Tech)", Tones.Good);
        };
        Progress.Unlocked += n => Say($"Unlocked: {n.Name}", Tones.Good);
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

/// <summary>
/// Creatures are solid to the castaway: each creature near them gets a capsule on the props layer
/// the character controller walks into, and a creature that ends up inside the castaway is eased
/// back out (the wildlife sim moves on its own ground and knows nothing of the player's body).
/// </summary>
public partial class CreatureBlockers : Node3D
{
    public Game Game;
    readonly List<StaticBody3D> _pool = new();
    const float Range = 14f;

    /// <summary>Keeps creatures and the castaway apart, and moves the blockers onto the creatures near them.</summary>
    public void KeepOff()
    {
        WildlifePatch patch = Game.Zone.Wildlife;
        if (patch.Sim == null) return;
        Vector3 feet = Game.Survivor.WorldFeet;
        Vector3 local = patch.PatchLocal(feet);
        bool onFoot = Game.Survivor.Body.Frame == null && Game.Survivor.Seated == null;
        int used = 0;
        foreach (Creature c in patch.Sim.Creatures)
        {
            if (c.Removed || c.Dead || c.Species.Flies) continue;
            Vector3 flat = new(c.Position.X - local.X, 0f, c.Position.Z - local.Z);
            float d = flat.Length();
            if (d > Range) continue;
            float radius = Mathf.Clamp(c.Species.Stats.Radius, 0.2f, 2.5f);
            float clear = radius + Slice.Player.Survivor.Radius;
            if (onFoot && d < clear && d > 1e-3f && MathF.Abs(c.Position.Y - local.Y) < 2.5f)
                c.Position += flat / d * (clear - d); // nudged out of the castaway
            if (used == _pool.Count)
            {
                var body = new StaticBody3D { CollisionLayer = Layers.Props, CollisionMask = 0 };
                body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.5f, Height = 1.6f } });
                AddChild(body);
                _pool.Add(body);
                WorldRay.Ignored.Add(body.GetRid());
            }
            StaticBody3D b = _pool[used++];
            var shape = (CylinderShape3D)((CollisionShape3D)b.GetChild(0)).Shape;
            float height = Mathf.Clamp(c.Species.Body.Size * 0.8f, 0.5f, 4f);
            if (MathF.Abs(shape.Radius - radius * 0.8f) > 0.05f) shape.Radius = radius * 0.8f;
            if (MathF.Abs(shape.Height - height) > 0.1f) shape.Height = height;
            b.Visible = true;
            b.ProcessMode = ProcessModeEnum.Inherit;
            b.CollisionLayer = Layers.Props;
            b.GlobalPosition = patch.FromPatch(c.Position) + Vector3.Up * height * 0.5f;
        }
        for (int i = used; i < _pool.Count; i++) _pool[i].CollisionLayer = 0;
    }
}
