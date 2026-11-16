using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// The owner (16 Nov 2026): "Are people reactive to dangerous people and animals? Like, will a person
/// stay away/or shoot something that would attack them, possibly running away when attacked?", and of
/// people: "yeah, they should attack people as well." Bands met on fresh worlds every run, with no
/// sleeper anywhere: raiders and other hostile bands go for other bands, who shoot back; bands that
/// mean each other no harm pass; a band's way bends round a predator it has seen, wider the more timid
/// its leader; an armed nobody with nerve shoots a predator and a timid one runs; the badly hurt run
/// on their own; no child stands against an animal. Run with --wanderers.
/// </summary>
public partial class BestiaryTest
{
    void BandsMeetEachOther()
    {
        var reach = Arming.Reach;
        var fire = Arming.Fire;
        var blow = Arming.BlowDamage;
        WildSim sim = null;
        var shots = new Dictionary<Herd, int>();
        var still = new HashSet<Creature>();
        // Guns reach 35 m along a clear line; each shot is heard along its line, and now and then hurts whoever it's at.
        Arming.Reach = (c, t) => c.Species.Figure is { } f && Weapons.Ranged(f.Prop) && WildSim.Flat(t.Position - c.Position).Length() < 35f && sim.Nav.LineClear(c.Position, t.Position) ? 35f : 0f;
        Arming.Fire = (w, c, t) =>
        {
            if (c.Herd != null) shots[c.Herd] = shots.GetValueOrDefault(c.Herd) + 1;
            Vector3 muzzle = c.Position + Vector3.Up * 1.5f;
            w.Noise(muzzle, 90f, (t.Position + Vector3.Up * Arming.Chest(t) - muzzle).Normalized(), c.AsTarget);
            if (t is CreatureTarget ct && !still.Contains(ct.Creature) && c.Rng.Chance(0.4f)) w.Damage(ct.Creature, 5f, c.AsTarget);
            return 1f;
        };
        Arming.BlowDamage = null;
        try
        {
            var kinds = new List<(string Kind, Species S)>();
            Species beast = null;
            for (int i = 0; i < 80 && (kinds.Count < 40 || beast == null); i++)
            {
                WorldLife life = Bestiary.Roll(_data, StoryWorld());
                kinds.AddRange(Wanderers(life));
                beast ??= life.Fauna.Species.FirstOrDefault(s => CrossReactions.Danger(s) && !s.Flies && s.Rig != RigKind.Swarm && s.Body.Size < 3f);
            }
            if (kinds.Count < 6 || beast == null) { Check(false, $"bands and a predator to try ({kinds.Count} kinds, predator {beast?.CommonName})"); return; }
            List<Species> Bodies(Species s) => s.Hosts ?? new List<Species> { s };
            bool Guns(Species s) => Bodies(s).All(h => Weapons.Ranged(h.Figure?.Prop ?? ""));
            bool Raid(Species s) => Bodies(s).Any(h => h.Figure?.Occupancy == Occupancy.Raider);
            var armed = kinds.Where(k => Guns(k.S)).ToList();
            var peaceful = kinds.Where(k => !Raid(k.S)).ToList();
            if (armed.Count < 2) { Check(false, $"armed kinds of band to try ({armed.Count})"); return; }
            WildSim Open(int rocks)
            {
                var w = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
                w.Clock.Time01 = 0.5f;
                for (int k = 0; k < rocks; k++) w.Nav.AddObstacle(new Vector3(_fresh.Range(-45f, 45f), 0f, _fresh.Range(-25f, 25f)), _fresh.Range(0.5f, 1.2f));
                shots.Clear();
                return w;
            }
            void Head(List<Creature> g, Vector3 to) { Herd h = g[0].Herd; h.Home = h.Goal = to; h.GoalTimer = 999f; }
            Creature Still(Species s, Vector3 at)
            {
                Creature c = sim.Spawn(s, at);
                c.ThinkEvery = 1e9f; // it stands where it was put, and shots don't hurt it
                still.Add(c);
                return c;
            }

            // Hostile bands that meet: a band that preys walks into an armed band of another kind, and each side fires or strikes.
            int pairs = 0, both = 0, hostileFired = 0, theyFired = 0;
            var notes = new List<string>();
            for (int tries = 0; tries < 300 && pairs < 8; tries++)
            {
                var (ka, sa) = armed[_fresh.Int(0, armed.Count - 1)];
                var (kb, sb) = armed[_fresh.Int(0, armed.Count - 1)];
                if (sa == sb || Raid(sa) && Raid(sb)) continue;
                sim = Open(14);
                List<Creature> a = sim.SpawnGroup(sa, new Vector3(-32f, 0f, 0f), _fresh.Int(3, 5)), b = sim.SpawnGroup(sb, new Vector3(32f, 0f, 0f), _fresh.Int(3, 5));
                if (a.Count < 2 || b.Count < 2) continue;
                Party pa = a[0].Herd.Party, pb = b[0].Herd.Party;
                pa.Temper.Stance = Stance.Hostile;
                pb.Temper.Stance = _fresh.Pick(new[] { Stance.Welcoming, Stance.Bargaining, Stance.Guarded, Stance.Wary, Stance.Hostile });
                if (!b.Any(m => CrossReactions.Stands(m.Species, pb.Temper, m.Nerve, animal: false))) continue; // (someone among them with the nerve to answer)
                pairs++;
                Head(a, new Vector3(32f, 0f, 0f));
                Head(b, new Vector3(-32f, 0f, 0f));
                int blowsA = 0, blowsB = 0;
                sim.Attacked += e => { if (e.Hit && e.Target is CreatureTarget t && t.Creature.Herd != e.Creature.Herd) { if (e.Creature.Herd == a[0].Herd) blowsA++; else if (e.Creature.Herd == b[0].Herd) blowsB++; } };
                for (float t = 0f; t < 60f && (shots.GetValueOrDefault(a[0].Herd) + blowsA == 0 || shots.GetValueOrDefault(b[0].Herd) + blowsB == 0); t += BandDt) sim.Step(BandDt);
                int fa = shots.GetValueOrDefault(a[0].Herd) + blowsA, fb = shots.GetValueOrDefault(b[0].Herd) + blowsB;
                if (fa > 0) hostileFired++;
                if (fb > 0) theyFired++;
                if (fa > 0 && fb > 0) both++;
                else if (notes.Count < 6) notes.Add($"{ka} (hostile) on {kb} ({pb.Temper.Stance}): {fa} from them, {fb} back; moods {pa.Mood}/{pb.Mood}, {WildSim.Flat(a[0].Herd.Center - b[0].Herd.Center).Length():F0} m apart");
            }
            foreach (string n in notes) GD.Print($"    ({n})");
            Check(pairs >= 6 && both >= pairs * 0.85f, $"hostile bands that meet fight it out with no sleeper near: {both} of {pairs} pairs both fired or struck (the band that preys in {hostileFired}, the other back in {theyFired})");

            // Bands that mean each other no harm cross paths and pass.
            int passes = 0, fought = 0;
            for (int tries = 0; tries < 200 && passes < 8 && peaceful.Count >= 2; tries++)
            {
                var (_, sa) = peaceful[_fresh.Int(0, peaceful.Count - 1)];
                var (_, sb) = peaceful[_fresh.Int(0, peaceful.Count - 1)];
                if (sa == sb) continue;
                sim = Open(6);
                List<Creature> a = sim.SpawnGroup(sa, new Vector3(-35f, 0f, -3f), _fresh.Int(2, 5)), b = sim.SpawnGroup(sb, new Vector3(35f, 0f, 3f), _fresh.Int(2, 5));
                if (a.Count == 0 || b.Count == 0) continue;
                a[0].Herd.Party.Temper.Stance = _fresh.Pick(new[] { Stance.Welcoming, Stance.Bargaining, Stance.Indifferent });
                b[0].Herd.Party.Temper.Stance = _fresh.Pick(new[] { Stance.Welcoming, Stance.Bargaining, Stance.Indifferent });
                passes++;
                Head(a, new Vector3(35f, 0f, -3f));
                Head(b, new Vector3(-35f, 0f, 3f));
                bool bad = false;
                for (float t = 0f; t < 50f; t += BandDt)
                {
                    sim.Step(BandDt);
                    bad |= a[0].Herd.Party.Mood is PartyMood.Fighting or PartyMood.Fleeing || b[0].Herd.Party.Mood is PartyMood.Fighting or PartyMood.Fleeing;
                }
                if (bad || shots.Values.Sum() > 0) fought++;
            }
            Check(passes >= 6 && fought == 0, $"bands that mean each other no harm cross paths and pass: {fought} of {passes} fought or ran");

            // A predator in the way: the band's way bends round it, as wide as the leader's nerve and their arms make it.
            var berths = new List<(float Berth, float Least)>();
            int bent = 0, through = 0;
            var stuck = new List<string>();
            for (int tries = 0; tries < 300 && berths.Count < 10; tries++)
            {
                var (_, s) = kinds[_fresh.Int(0, kinds.Count - 1)];
                sim = Open(0);
                float side = _fresh.Range(-3f, 3f);
                List<Creature> g = sim.SpawnGroup(s, new Vector3(-50f, 0f, side), _fresh.Int(2, 5));
                if (g.Count == 0) continue;
                Herd h = g[0].Herd;
                h.Party.Temper.Stance = Stance.Welcoming;
                Creature lead = Brain.Leader(h);
                if (CrossReactions.Stands(lead.Species, h.Party.Temper, lead.Nerve, animal: true)) continue; // (one who'd go for it instead: below)
                float berth = CrossReactions.Berth(lead.Nerve, g.Any(m => Weapons.Armed(m.Species.Figure?.Prop ?? "")));
                Creature beastAt = Still(beast, Vector3.Zero);
                Head(g, new Vector3(50f, 0f, side));
                float least = float.MaxValue;
                for (float t = 0f; t < 200f && h.Center.X < 38f; t += BandDt)
                {
                    sim.Step(BandDt);
                    foreach (Creature m in g) if (m.Alive) least = MathF.Min(least, WildSim.Flat(m.Position - beastAt.Position).Length());
                }
                if (h.Center.X < 38f) { through++; if (stuck.Count < 8) stuck.Add($"berth {berth:F0} at x {h.Center.X:F0} {h.Party.Mood}"); continue; } // (never got past: it ran, or stood off)
                berths.Add((berth, least));
                if (least >= berth * 0.6f) bent++;
            }
            float Corr(List<(float X, float Y)> v)
            {
                float mx = v.Average(p => p.X), my = v.Average(p => p.Y);
                float sxy = v.Sum(p => (p.X - mx) * (p.Y - my)), sx = MathF.Sqrt(v.Sum(p => (p.X - mx) * (p.X - mx))), sy = MathF.Sqrt(v.Sum(p => (p.Y - my) * (p.Y - my)));
                return sx * sy > 1e-4f ? sxy / (sx * sy) : 0f;
            }
            float corr = berths.Count > 2 ? Corr(berths) : 0f;
            GD.Print($"    (berth and nearest pass, m: {string.Join(", ", berths.OrderBy(p => p.Berth).Select(p => $"{p.Berth:F0}/{p.Least:F0}"))}; {through} never got past: {string.Join("; ", stuck)})");
            Check(berths.Count >= 8 && bent >= berths.Count * 0.9f && corr > 0.4f, $"a band's way bends round a {beast.CommonName} it has seen ({bent} of {berths.Count} kept to 60% of their berth or more, walking straight it was in their way), wider for the timid (berth to nearest pass {corr:F2})");

            // One armed nobody (no guard, no law) by a predator: with nerve, they shoot it; timid, they run.
            int bold = 0, boldShot = 0, timid = 0, timidRan = 0;
            PersonRole[] nobodies = { PersonRole.Worker, PersonRole.Trader, PersonRole.Farmer, PersonRole.Mechanic, PersonRole.Medic };
            for (int i = 0; i < 12; i++)
            {
                WorldLife life = Bestiary.Roll(_data, StoryWorld());
                Species who = life.People.Person(Seed(), nobodies[_fresh.Int(0, nobodies.Length - 1)]);
                if (who.Figure.Stage == LifeStage.Child) continue;
                who.Figure.Prop = _fresh.Pick(new[] { "rifle", "shotgun", "bow", "pistol" });
                bool brave = i % 2 == 0;
                sim = Open(0);
                Creature c = sim.Spawn(who, Vector3.Zero);
                c.Nerve = brave ? 0.95f : 0.05f;
                Creature it = Still(beast, new Vector3(14f, 0f, 0f));
                float far = 0f;
                for (float t = 0f; t < 10f; t += BandDt)
                {
                    sim.Step(BandDt);
                    far = MathF.Max(far, WildSim.Flat(c.Position - it.Position).Length());
                }
                int fired = c.Herd != null ? shots.GetValueOrDefault(c.Herd) : 0;
                if (brave) { bold++; if (fired > 0) boldShot++; }
                else { timid++; if (fired == 0 && far > 20f) timidRan++; }
            }
            Check(bold >= 5 && boldShot == bold && timid >= 5 && timidRan == timid, $"an armed nobody with nerve shoots a predator 14 m off ({boldShot} of {bold}); a timid one runs from it ({timidRan} of {timid})");

            // A brave band fights a predator: one of them badly hurt breaks off and runs on their own, while the rest fight on; no child among them stands against it.
            int hurtTries = 0, hurtRan = 0, children = 0, childFought = 0, childRan = 0;
            notes.Clear();
            for (int tries = 0; tries < 100 && hurtTries < 6; tries++)
            {
                var (_, s) = armed[_fresh.Int(0, armed.Count - 1)];
                sim = Open(0);
                List<Creature> g = sim.SpawnGroup(s, new Vector3(-16f, 0f, 0f), 4);
                if (g.Count < 3) continue;
                Herd h = g[0].Herd;
                foreach (Creature m in g) m.Nerve = 0.95f;
                // Children among them, one with a weapon of their own (a culture may arm them).
                WorldLife life = Bestiary.Roll(_data, StoryWorld());
                var young = new List<Creature>();
                for (int k = 0; k < 2; k++)
                {
                    Species child = life.People.Person(Seed(), PersonRole.Child);
                    if (k == 0) child.Figure.Prop = "spear";
                    Creature kid = sim.Spawn(child, new Vector3(-18f, 0f, k * 2f - 1f), h);
                    kid.Nerve = 0.95f;
                    young.Add(kid);
                }
                Creature it = Still(beast, Vector3.Zero);
                for (float t = 0f; t < 8f && h.Party.Mood != PartyMood.Fighting; t += BandDt) sim.Step(BandDt);
                if (h.Party.Mood != PartyMood.Fighting) continue;
                hurtTries++;
                children += young.Count;
                Creature hurt = g.First(m => m.Alive && m != Brain.Leader(h));
                hurt.Health = hurt.Stats.MaxHealth * 0.2f;
                Vector3 from = hurt.Position, off = WildSim.Flat(hurt.Position - it.Position).Normalized();
                bool fled = false, fightOn = true;
                var kidsRan = new HashSet<Creature>();
                for (float t = 0f; t < 4f; t += BandDt)
                {
                    sim.Step(BandDt);
                    fled |= hurt.State == CState.Flee && WildSim.Flat(hurt.Position - from).Dot(off) > 3f;
                    foreach (Creature kid in young)
                    {
                        if (kid.State == CState.Chase || kid.Attacking && kid.AttackTarget == it.AsTarget) childFought++;
                        if (kid.State == CState.Flee) kidsRan.Add(kid);
                    }
                }
                fightOn = h.Party.Mood == PartyMood.Fighting || it.Dead;
                if (fled && fightOn) hurtRan++;
                else if (notes.Count < 6) notes.Add($"the hurt one {hurt.State} {(hurt.Dead ? "dead " : "")}{WildSim.Flat(hurt.Position - from).Dot(off):F1} m off, attacking {hurt.Phase}; the band {h.Party.Mood}, its foe {(h.Party.Foe == it.AsTarget ? "the beast" : h.Party.Foe?.Name)}");
                childRan += kidsRan.Count;
            }
            foreach (string n in notes) GD.Print($"    ({n})");
            Check(hurtTries >= 4 && hurtRan == hurtTries, $"badly hurt in a fight, one breaks off and runs on their own while the band fights on ({hurtRan} of {hurtTries})");
            Check(children >= 8 && childFought == 0 && childRan == children, $"no child stands against an animal, armed or not: {childFought} frames of a child at it, {childRan} of {children} ran");
        }
        finally
        {
            Arming.Reach = reach;
            Arming.Fire = fire;
            Arming.BlowDamage = blow;
        }
    }
}
