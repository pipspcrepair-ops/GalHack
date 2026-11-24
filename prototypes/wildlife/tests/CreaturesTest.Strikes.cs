using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// Strikes look like attacks and land only on contact (the owner, 15 Nov: "massive damage and the
/// animal doesn't even look like it's touched us"). Fresh worlds every run, bodies from a small
/// hunter to megafauna: every hit is preceded by its tell, the tell is short (by size), the striking
/// part (jaws, a stomping foot, a tail, the horns of a charge) is at the victim's body the step the
/// damage lands, jaws open in the lunge and snap shut on contact, and stepping out of reach after
/// the commit is a miss.
/// </summary>
public partial class CreaturesTest
{
    /// <summary>What one run of strikes saw.</summary>
    sealed class StrikeLog
    {
        public int Hits, Misses, FarHits, Untold, OpenLunges, Bites, Snapped, Quakes, Stomps;
        public float WorstGap = float.MinValue, DeepestGap = float.MaxValue, MaxPartGap = float.MinValue, MaxDrawnGap = float.MinValue;
        /// <summary>The most any striker's chest was turned from its victim as a blow landed, degrees (not a tail swipe, which turns into it), and how many such blows by people.</summary>
        public float WorstFacing;
        public int Squared, PeopleBlows;
        public string WorstFaced = "";
        public readonly List<(float size, float windup, bool first)> Windups = new();
        public readonly Dictionary<AttackMove, int> HitsBy = new(), TriesBy = new();
        public string Worst = "", WorstDrawn = "";
    }

    void StrikesLand()
    {
        var log = new StrikeLog();
        var rng = new Random();
        CombatRules r = _data.Combat;
        int species = 0;
        foreach (float size in new[] { 0.6f, 1.4f, 3f, 6f, 9f })
        {
            PlanetFauna planet = _gen.Generate((ulong)rng.NextInt64(), 0.3f, false);
            var walkers = planet.Species.Where(s => s.Rig == RigKind.Legged && !s.Flies && !s.Mechanical).ToList();
            foreach (Species model in walkers.OrderBy(_ => rng.Next()).Take(2))
            {
                species++;
                Species s = Hunter(model, size);
                // Face on, a bite (or a charge for horned giants from far off).
                StrikeCase(log, s, new Vector3(0f, 0f, -(s.Body.Size + 6f)), dodge: false);
                StrikeCase(log, s, new Vector3(0f, 0f, -(s.Body.Size + 6f)), dodge: true);
            }
        }
        // People: bare fists, clubs and blades, a few of each kind of folk on fresh worlds.
        var folk = new PeopleGenerator(_data.People, new WorldConditions { Seed = (ulong)rng.NextInt64(), Alienness = (float)rng.NextDouble() * 0.6f, TempC = 15f }) { HuskRules = _data.Husks };
        for (int i = 0; i < 6; i++)
        {
            Species person = folk.Person((ulong)rng.NextInt64(), i % 2 == 0 ? PersonRole.Guard : PersonRole.Worker, Occupancy.Raider);
            person.Stats.Damage = 1f;
            species++;
            StrikeCase(log, person, new Vector3(0.6f * (i % 3 - 1), 0f, -2.4f), dodge: false);
        }
        // Megafauna's own strikes, from their parts: a stomp on pillar legs, a charge with horns, a swipe with a heavy tail.
        PlanetFauna world = _gen.Generate((ulong)rng.NextInt64(), 0.3f, false);
        Species giant = Hunter(world.Species.First(s => s.Rig == RigKind.Legged && !s.Flies && !s.Mechanical), 7f);
        giant.Body.LegStyle = LegStyle.Upright;
        giant.Body.Legs = Math.Max(4, giant.Body.Legs / 2 * 2);
        giant.Body.LegThickness = MathF.Max(giant.Body.Width * 0.25f, giant.Body.LegLength * 0.15f); // pillars
        giant.Body.TailLength = giant.Body.Size * 0.8f;
        giant.Parts["tail"] = "club";
        giant.Parts["horns"] = "pair";
        Proportions b = giant.Body;
        bool stomps = Strikes.CanStomp(giant, r.Strikes), charges = Strikes.CanCharge(giant, r.Strikes), swipes = Strikes.CanSwipe(giant, r.Strikes);
        StrikeCase(log, giant, new Vector3(0.3f, 0f, -(b.Size * 0.5f + b.LegLength * 0.4f)), dodge: false);
        StrikeCase(log, giant, new Vector3(0.3f, 0f, -(b.Size * 0.5f + b.LegLength * 0.4f)), dodge: true);
        for (int i = 0; i < 3; i++) StrikeCase(log, giant, new Vector3(b.Size * (0.2f + 0.15f * i), 0f, b.Size * 0.5f + b.TailLength * 0.5f), dodge: false);
        StrikeCase(log, giant, new Vector3(b.Size * 0.3f, 0f, b.Size * 0.5f + b.TailLength * 0.5f), dodge: true);
        StrikeCase(log, giant, new Vector3(0f, 0f, -(b.Size * 0.5f + r.Strikes.ChargeFrom + 3f)), dodge: false);
        StrikeCase(log, giant, new Vector3(0f, 0f, -(b.Size * 0.5f + r.Strikes.ChargeFrom + 3f)), dodge: true);

        int Tries(AttackMove m) => log.TriesBy.GetValueOrDefault(m);
        int HitsOf(AttackMove m) => log.HitsBy.GetValueOrDefault(m);
        Check(log.Hits >= species && log.FarHits == 0,
            $"blows land only on contact: {log.Hits} hits over {species} species from 0.6 to 9 m, none with the striking part further than {r.ContactSlack:F2} m from the body (worst gap {log.WorstGap:F3} m, deepest {log.DeepestGap:F2} m; {log.Worst})");
        Check(log.MaxPartGap <= r.ContactSlack + 1e-3f && log.MaxDrawnGap <= 0.3f,
            $"the striking part the rig draws is at the body the step damage applies: its sweep over the last step at most {log.MaxPartGap:F3} m off, the drawn frame at most {log.MaxDrawnGap:F2} m ({log.WorstDrawn})");
        Check(log.Squared > 0 && log.PeopleBlows > 0 && log.WorstFacing <= 20f,
            $"everything squares up to what it hits: {log.Squared} blows (people's {log.PeopleBlows}), the chest at worst {log.WorstFacing:F1} degrees off the victim as each landed ({log.WorstFaced}; at most 20)");
        Check(log.Untold == 0, $"every hit is preceded by its tell ({log.Hits} hits, {log.Untold} without one)");
        var firsts = log.Windups.Where(x => x.first).ToList();
        float least = firsts.Min(x => x.windup), most = firsts.Max(x => x.windup);
        float small = firsts.Where(x => x.size < 1f).Select(x => x.windup).DefaultIfEmpty().Max(), big = firsts.Where(x => x.size > 5f).Select(x => x.windup).DefaultIfEmpty().Min();
        float later = log.Windups.Where(x => !x.first).Select(x => x.windup).DefaultIfEmpty(r.TellLeast).Min();
        Check(least >= r.TellLeast - 1e-3f && most <= r.TellMost + 1e-3f && big > small && later >= r.TellLeast * r.LaterShare - 1e-3f,
            $"tells are short and by size: first wind-ups {least:F2} to {most:F2} s (small bodies up to {small:F2}, megafauna from {big:F2}), later ones from {later:F2} s");
        Check(log.Misses > 0 && log.Bites > 0 && log.OpenLunges == log.Bites && log.Snapped == log.Bites,
            $"jaws open wide in every lunge ({log.OpenLunges} of {log.Bites}) and snap shut at contact or the lunge's end ({log.Snapped} of {log.Bites})");
        Check(stomps && charges && swipes && HitsOf(AttackMove.Stomp) > 0 && HitsOf(AttackMove.Charge) > 0 && HitsOf(AttackMove.Swipe) > 0,
            $"a pillar-legged, horned, club-tailed giant ({b.Size:F1} m, {b.Legs} legs {b.LegThickness:F2} thick for {b.LegLength:F2}: stomps {stomps}, charges {charges}, swipes {swipes}) stomps ({HitsOf(AttackMove.Stomp)} of {Tries(AttackMove.Stomp)} landed), charges ({HitsOf(AttackMove.Charge)} of {Tries(AttackMove.Charge)}) and swipes its tail ({HitsOf(AttackMove.Swipe)} of {Tries(AttackMove.Swipe)}), by contact");
        Check(log.Stomps > 0 && log.Quakes == log.Stomps, $"every stomp shakes the ground and thuds, hit or miss ({log.Quakes} quakes for {log.Stomps} stomps)");
    }

    /// <summary>A copy made a hunter of this size.</summary>
    internal static Species Hunter(Species model, float size)
    {
        Species s = model.Resized(size);
        s.Stats.Damage = 1f;
        s.Stats.Hunts = true;
        s.Behaviour = Behaviour.Predator;
        s.Temperament = Temperament.Aggressive;
        return s;
    }

    /// <summary>
    /// One hunter and one sleeper standing at an offset from it (its front is -Z), for a few
    /// strikes: each told, each judged on contact. Dodging, the sleeper steps well out of reach
    /// the moment it commits, so every strike must miss.
    /// </summary>
    void StrikeCase(StrikeLog log, Species s, Vector3 offset, bool dodge)
    {
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 200f), new NavGrid(200f, 3f));
        var p = new DummyTarget { Name = "Sleeper", Health = new Health(1e7f), Position = offset, Forward = Vector3.Back };
        sim.Targets.Add(p);
        Creature c = sim.Spawn(s, Vector3.Zero);
        c.Yaw = 0f;
        // It knows where they are, even behind it (by sight it couldn't, and would turn to face them first).
        c.Noticed = p;
        c.NoticedAt = 0f;
        var told = new HashSet<Creature>();
        Vector3 partWas = default, partBefore = default;
        bool lunging = false, opened = false;
        float snapClock = -1f;
        int strikes = 0;
        float nearest = float.MaxValue;
        sim.Telegraph += e =>
        {
            if (e.Creature != c) return;
            told.Add(c);
            log.Windups.Add((s.Body.Size, e.Windup, e.First));
            log.TriesBy[c.Move] = log.TriesBy.GetValueOrDefault(c.Move) + 1;
        };
        sim.Committed += e =>
        {
            if (e.Creature != c || !dodge) return;
            Vector3 away = WildSim.Flat(p.Position - c.Position).Normalized();
            p.Position += c.Move == AttackMove.Charge ? away.Cross(Vector3.Up) * (s.Body.Width + 4f) : away * (s.Body.Size + Strikes.SwipeReach(s) + 4f);
        };
        sim.Quaked += e => { if (e.Creature == c && e.Stomp) log.Quakes++; };
        sim.Attacked += e =>
        {
            if (e.Creature != c) return;
            strikes++;
            Trace($"{s.Body.Size:F1} m {s.CommonName} {e.Move} hit {e.Hit} gap {e.Gap:F2} nearest {nearest:F2} dodge {dodge} windup {e.Windup:F2}");
            nearest = float.MaxValue;
            if (e.Move == AttackMove.Stomp) log.Stomps++;
            if (!told.Contains(c) && e.Hit) log.Untold++;
            told.Remove(c);
            if (lunging && e.Move == AttackMove.Bite && c.Species.Figure == null && c.Species.Part("head") != "none")
            {
                log.Bites++;
                if (opened) log.OpenLunges++;
                snapClock = 0f;
            }
            if (!e.Hit) { log.Misses++; return; }
            log.Hits++;
            if (e.Move != AttackMove.Swipe)
            {
                // Faced squarely: its chest, as drawn, on whoever it hit.
                Vector3 chest = c.Rig.Chest(c), to = WildSim.Flat(p.Position - c.Position).Normalized();
                float off = Mathf.RadToDeg(MathF.Acos(Math.Clamp(chest.Dot(to), -1f, 1f)));
                log.Squared++;
                if (c.Species.Figure != null) log.PeopleBlows++;
                if (off > log.WorstFacing)
                {
                    log.WorstFacing = off;
                    float yawOff = Mathf.RadToDeg(MathF.Abs(Geo.WrapAngle(MathF.Atan2(-to.X, -to.Z) - c.Yaw)));
                    log.WorstFaced = $"{s.Body.Size:F1} m {s.CommonName} {e.Move} from {WildSim.Flat(p.Position - c.Position).Length():F2} m, its yaw {yawOff:F0} degrees off";
                }
            }
            log.HitsBy[e.Move] = log.HitsBy.GetValueOrDefault(e.Move) + 1;
            if (dodge) log.FarHits++;
            // Judged again from the rig's own drawn part, swept from last step to this one.
            Vector4 part = c.Rig.Striker(c);
            var drawn = new Vector3(part.X, part.Y, part.Z);
            log.MaxPartGap = MathF.Max(log.MaxPartGap, Strikes.Gap(partBefore, drawn, part.W, p));
            float still = Strikes.Gap(drawn, drawn, part.W, p);
            if (still > log.MaxDrawnGap) { log.MaxDrawnGap = still; log.WorstDrawn = $"{s.Body.Size:F1} m {e.Move}"; }
            if (e.Gap > _data.Combat.ContactSlack) log.FarHits++;
            if (e.Gap > log.WorstGap) { log.WorstGap = e.Gap; log.Worst = $"{s.Body.Size:F1} m {s.CommonName} {e.Move}"; }
            log.DeepestGap = MathF.Min(log.DeepestGap, e.Gap);
        };
        // The first strike starts where the case put them; the rest come as its brain sees fit.
        sim.Step(Dt);
        c.Yaw = 0f;
        Attacks.Start(sim, c, p);
        for (float t = 0f; t < 30f && strikes < 3; t += Dt)
        {
            Vector4 part = c.Rig.Striker(c);
            partBefore = partWas;
            partWas = new Vector3(part.X, part.Y, part.Z);
            if (c.Phase == AttackPhase.Windup) { opened = false; lunging = false; }
            sim.Step(Dt);
            if (c.Phase == AttackPhase.Lunge)
            {
                Vector4 sp = c.Rig.Striker(c);
                nearest = MathF.Min(nearest, Strikes.Gap(new Vector3(sp.X, sp.Y, sp.Z), new Vector3(sp.X, sp.Y, sp.Z), sp.W, p));
                lunging = true;
                if (c.Rig.Gaping > 0.7f) opened = true;
            }
            if (snapClock >= 0f)
            {
                snapClock += Dt;
                if (snapClock <= 0.12f && c.Rig.Gaping < 0f) { log.Snapped++; snapClock = -1f; }
                else if (snapClock > 0.12f) snapClock = -1f;
            }
            if (c.Phase is AttackPhase.None or AttackPhase.Recover) lunging = false;
            // The sleeper keeps where the case put them (a dodge moved them once).
            if (dodge && c.Phase == AttackPhase.Recover) p.Position = c.Position + (offset.LengthSquared() > 0.01f ? new Basis(Vector3.Up, c.Yaw) * offset : Vector3.Forward * 3f);
        }
    }
}
