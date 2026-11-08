using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// The underground showroom's views (run windowed, like the rest):
/// - deep-ceiling: a cave crawler on the ceiling of a generator cave, from the floor under it
/// - deep-tell: an ambush's tell, grit falling from a corridor ceiling and a shadow gathering
///   on the floor, as someone walks under the creeper there
/// - deep-food: a predator taking a subsister: a lurker springing at a grazing grub in the dark
/// - deep-nest: the deep nest, its pale, blind guards rearing at an intruder
/// - deep-hold: wreck nesters and their scrap nests in a derelict freighter's hold
/// - mutation: one species at mutation 0, 0.5 and 1, side by side
/// </summary>
public partial class Screenshot
{
    Showroom _room;

    bool IsDeep(string view) => view.StartsWith("deep-") || view is "mutation" or "domestic" or "husk-den";

    /// <summary>A point in the open near <paramref name="around"/> with a clear view of <paramref name="at"/>.</summary>
    static Vector3 OpenSpot(ISolidSpace s, Vector3 around, Vector3 at, float radius, float height, bool sameFloor = false)
    {
        Vector3 best = around;
        float home = SpaceMath.FloorBelow(s, around.X, around.Y + 0.5f, around.Z, 8f) ?? around.Y;
        float bestScore = float.MinValue;
        for (int i = 0; i < 192; i++)
        {
            float a = i * MathF.Tau / 16f, r = radius * (0.6f + 0.2f * (i / 16 % 3));
            float lift = new[] { 1.2f, 0f, -1.2f, -2.4f }[i / 48];
            var p = around + new Vector3(MathF.Cos(a) * r, lift, MathF.Sin(a) * r);
            if (s.Density(p) > -0.2f || SpaceMath.FloorBelow(s, p.X, p.Y, p.Z, 8f) is not float y) continue;
            if (sameFloor && MathF.Abs(y - home) > 0.4f) continue; // not up on a crate
            p.Y = y + height;
            if (s.Density(p) > -0.4f || !SpaceMath.Clear(s, p, at, 0.1f)) continue;
            float score = -MathF.Abs((p - at).Length() - radius) - MathF.Max(0f, s.Density(p) + 0.8f) * 4f;
            if (score > bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    void DeepSetup()
    {
        Showroom r = _room;
        r.Hud.StatusVisible = _hud;
        r.Hud.ShowCards = false;
        r.Hud.ClearCards();
        r.Player.Muzzle.Visible = false;
        if (DenSetup()) return;
        switch (_view)
        {
            case "deep-ceiling":
            {
                r.GoTo(0);
                WildSim sim = r.Current.Sim;
                sim.Targets.Clear();
                // The crawler with the most ceiling to itself, nearest the chamber.
                var up = sim.Creatures.Where(x => x.Species.Role == DeepRole.Crawler && x.Facing.Y.Y < -0.6f && !x.InWalls).ToList();
                Creature c = up.OrderByDescending(x => up.Where(o => o != x).Select(o => (o.Position - x.Position).Length()).DefaultIfEmpty(99f).Min() > 3f ? 1 : 0)
                    .ThenBy(x => (x.Position - r.Chamber).Length()).First();
                foreach (Creature x in sim.Creatures) x.Hunger = 0f;
                Vector3 top = Crawl.Center(c);
                Vector3 eye = OpenSpot(r.Current.Space, top, top, 2.6f, 1.6f);
                r.Camera.Fov = 50f;
                _aim = () => (eye, Crawl.Center(c));
                _captureTick = 170;
                break;
            }
            case "deep-tell":
            {
                r.GoTo(2);
                WildSim sim = r.Current.Sim;
                sim.Targets.Clear();
                foreach (Creature x in sim.Creatures)
                    if (x.Position.X > 9f && x.Position.X < 29f && x.Position.Z > 3f && x.Position.Z < 8f && x.Position.Y > 8f) x.Removed = true;
                Species dropper = Enumerable.Range(0, 60).Select(i => new DeepFauna(r.Data, (ulong)(2024 + i), 0.5f).For(DeepRole.Ambusher, PoiTheme.Industrial, 0)).First(s => s.Form == "creeper");
                Creature c = sim.SpawnIn(dropper, r.Current.Space, new Vector3(16.5f, 11.8f, 6f), Vector3.Down);
                c.Ambush = new AmbushPoint { Kind = HideKind.Ceiling, Position = Crawl.Center(c), Normal = c.Facing.Y };
                var walker = new DummyTarget { Name = "Walker", Position = new Vector3(11f, 9f, 6f), Health = new Health(1e6f) };
                int toldAt = -1;
                sim.Told += e => { if (e.Creature == c && toldAt < 0) toldAt = _tick; };
                int startAt = _tick + 60;
                _each = () =>
                {
                    if (_tick == startAt) sim.Targets.Add(walker);
                    if (_tick > startAt && toldAt < 0) walker.Position += Vector3.Right * 1.2f / 60f;
                    if (toldAt > 0 && _captureTick > toldAt + 55) _captureTick = toldAt + 55;
                };
                _aim = () => (new Vector3(21.5f, 10.6f, 6.4f), Crawl.Center(c) + new Vector3(0.5f, -1.2f, 0f));
                _captureTick = 900;
                break;
            }
            case "deep-food":
            {
                r.GoTo(2);
                WildSim sim = r.Current.Sim;
                sim.Targets.Clear();
                foreach (Creature x in sim.Creatures)
                    if (x.Position.X > 27f && x.Position.Z < 13f && x.Position.Y > 8f) x.Removed = true;
                DeepFauna f = r.Fauna;
                Species lurker = f.For(DeepRole.Lurker, PoiTheme.Industrial, 0), grub = f.For(DeepRole.Subsister, PoiTheme.Industrial, 0);
                var moss = new FoodPatch { Position = new Vector3(32.5f, 9f, 7.5f), Kind = grub.Diet, Radius = 1f };
                sim.Food.Add(moss);
                r.Current.Lairs.Build(sim);
                Creature hunter = sim.SpawnIn(lurker, r.Current.Space, new Vector3(34.6f, 9.2f, 9.6f));
                hunter.Hunger = 1f;
                var grubs = new List<Creature>();
                for (int i = 0; i < 3; i++)
                {
                    Creature g = sim.SpawnIn(grub, r.Current.Space, moss.Position + new Vector3(-0.6f + i * 0.7f, 0.3f, -0.3f + 0.3f * i));
                    g.Home = moss.Position;
                    grubs.Add(g);
                }
                int lungeAt = -1;
                _each = () =>
                {
                    if (lungeAt < 0 && hunter.Phase == AttackPhase.Lunge && hunter.AttackTarget is CreatureTarget) lungeAt = _tick;
                    if (lungeAt > 0 && _captureTick > lungeAt + 9) _captureTick = lungeAt + 9;
                };
                _aim = () =>
                {
                    Creature prey = hunter.Prey ?? grubs[0];
                    Vector3 mid = (Crawl.Center(hunter) + Crawl.Center(prey)) * 0.5f;
                    return (new Vector3(31.2f, 10.2f, 5.9f), mid + Vector3.Up * 0.1f);
                };
                _captureTick = 3000;
                break;
            }
            case "deep-nest":
            {
                r.GoTo(2);
                WildSim sim = r.Current.Sim;
                sim.Targets.Clear();
                Nest nest = sim.Nests.OrderBy(n => n.Position.Y).First();
                var eye = new Vector3(nest.Position.X + 4.2f, nest.Position.Y + 1.7f, nest.Position.Z - 3.6f);
                var intruder = new DummyTarget { Name = "Intruder", Position = new Vector3(eye.X, nest.Position.Y, eye.Z), Health = new Health(1e6f) };
                int startAt = _tick + 90;
                _each = () => { if (_tick == startAt) sim.Targets.Add(intruder); };
                _aim = () => (eye, nest.Position + new Vector3(0f, 0.6f, 0f));
                _captureTick = startAt + 100;
                break;
            }
            case "deep-hold":
            {
                r.GoTo(3);
                WildSim sim = r.Current.Sim;
                sim.Targets.Clear();
                Nest nest = sim.Nests.OrderBy(n => n.Position.X).First();
                Vector3 look = nest.Position + new Vector3(0f, 0.5f, 0f);
                Vector3 eye = OpenSpot(r.Current.Space, nest.Position, look, 3.6f, 1.55f, sameFloor: true);
                _aim = () => (eye, look);
                _captureTick = 240;
                break;
            }
            case "domestic":
            {
                r.GoTo(5);
                r.Current.Sim.Targets.Clear();
                r.Camera.Fov = 66f;
                _aim = () => (new Vector3(15f, 4.0f, 14.8f), new Vector3(15f, 1.2f, 7f));
                _captureTick = 420;
                break;
            }
            case "mutation":
            {
                r.GoTo(4);
                r.Current.Sim.Targets.Clear();
                r.Camera.Fov = 66f;
                _aim = () => (new Vector3(15f, 4.4f, 14.8f), new Vector3(15f, 1.3f, 6.6f));
                _captureTick = 120;
                break;
            }
        }
    }
}
