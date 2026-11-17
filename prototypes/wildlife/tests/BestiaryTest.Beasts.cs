using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// The owner (17 Nov 2026), of people and animals getting along in a mine: aggressive animals go for every person, predators
/// take other animals, kept beasts (collared) are on their people's side and hurting one is hurting them, and gunfire is heard
/// ("startled or trigger aggression [or] fleeing depending on their temperament"). Fresh worlds every run; part of --wanderers.
/// </summary>
public partial class BestiaryTest
{
    void PeopleAndBeasts()
    {
        var reach = Arming.Reach;
        var fire = Arming.Fire;
        WildSim sim = null;
        // Guns reach 35 m along a clear line; each shot is heard along its line, and now and then hurts whom it's at.
        Arming.Reach = (c, t) => c.Species.Figure is { } f && Weapons.Ranged(f.Prop) && WildSim.Flat(t.Position - c.Position).Length() < 35f ? 35f : 0f;
        Arming.Fire = (w, c, t) =>
        {
            Vector3 muzzle = c.Position + Vector3.Up * 1.5f;
            w.Noise(muzzle, 90f, (t.Position + Vector3.Up * Arming.Chest(t) - muzzle).Normalized(), c.AsTarget);
            if (t is CreatureTarget ct && c.Rng.Chance(0.3f)) w.Damage(ct.Creature, 4f, c.AsTarget);
            return 1f;
        };
        try
        {
            var bands = new List<Species>();
            var hunters = new List<(Species Hunter, Species Prey)>();
            Species beast = null, grazer = null;
            for (int i = 0; i < 60 && (bands.Count < 12 || hunters.Count < 6 || beast == null || grazer == null); i++)
            {
                WorldLife life = Bestiary.Roll(_data, StoryWorld());
                bands.AddRange(Wanderers(life).Select(k => k.Species).Where(s => (s.Hosts ?? new List<Species> { s }).All(h => Weapons.Ranged(h.Figure?.Prop ?? ""))));
                beast ??= life.Fauna.Species.FirstOrDefault(CrossReactions.Keepable);
                grazer ??= life.Fauna.Species.FirstOrDefault(s => s.Behaviour == Behaviour.Herd && !s.Flies && s.Temperament is not (Temperament.Aggressive or Temperament.Bold));
                foreach (Species h in life.Fauna.Species.Where(s => Brain.Preys(s) && !s.Flies))
                    if (life.Fauna.Species.FirstOrDefault(p => !p.Flies && Brain.Takes(new Creature { Species = h, Pack = h.Behaviour == Behaviour.PackHunter ? new Pack() : null }, new Creature { Species = p })) is { } prey)
                        hunters.Add((h, prey));
            }
            if (bands.Count < 2 || hunters.Count == 0 || beast == null || grazer == null) { Check(false, $"armed bands, hunters with prey, a beast to keep and a herd to try ({bands.Count}, {hunters.Count}, {beast?.CommonName}, {grazer?.CommonName})"); return; }
            WildSim Open()
            {
                var w = new WildSim(_data, new Terrain((x, z) => 0f, 150f), new NavGrid(150f, 3f));
                w.Clock.Time01 = 0.5f;
                return w;
            }
            void Run(float seconds, Func<bool> done = null)
            {
                for (float t = 0f; t < seconds && !(done?.Invoke() ?? false); t += BandDt) sim.Step(BandDt);
            }
            void Stay(List<Creature> g) { Herd h = g[0].Herd; h.Home = h.Goal = g[0].Position; h.GoalTimer = 1e9f; }

            // Predators take prey on open ground: a hunter (or its pack) and a group it would take, 25 m apart, four minutes.
            int trials = 0, fed = 0, kills = 0;
            foreach (var (h, p) in hunters.OrderBy(_ => _fresh.Float()).Take(6))
            {
                sim = Open();
                sim.SpawnGroup(h, new Vector3(-12f, 0f, 0f), h.Behaviour == Behaviour.PackHunter ? 3 : 1);
                sim.SpawnGroup(p, new Vector3(13f, 0f, 0f), 5);
                Run(240f);
                trials++;
                kills += sim.Predations;
                if (sim.Predations > 0) fed++;
            }
            Check(fed * 3 >= trials && kills > 0, $"predators take prey on open ground: {kills} kills, in {fed} of {trials} four-minute meetings ({string.Join(", ", hunters.Take(3).Select(x => $"{x.Hunter.CommonName} on {x.Prey.CommonName}"))})");

            // A predator goes for one of a band out in the open, no player anywhere.
            sim = Open();
            Species band = bands[_fresh.Int(0, bands.Count - 1)];
            List<Creature> people = sim.SpawnGroup(band, new Vector3(10f, 0f, 0f), 3);
            Stay(people);
            Creature hunter = sim.Spawn(hunters[0].Hunter, new Vector3(10f - MathF.Min(20f, hunters[0].Hunter.Stats.Perception * 0.7f), 0f, 0f)); // (within its senses: a short-sighted one never saw them at 20 m)
            Run(60f, () => hunter.Attacking || hunter.Noticed is CreatureTarget { Creature.Herd: var hh } && hh == people[0].Herd && hunter.State == CState.Chase);
            Check(hunter.Noticed is CreatureTarget { Creature.Herd: var them } && them == people[0].Herd || hunter.AttackTarget is CreatureTarget, $"a {hunter.Species.CommonName} goes for a band's people with no player about ({hunter.State}, after {hunter.Noticed?.Name ?? "nobody"})");

            // A shot fired through a herd scatters it; one only heard sends no one running but the timid.
            sim = Open();
            List<Creature> herd = sim.SpawnGroup(grazer, Vector3.Zero, 6);
            var shooter = new DummyTarget { Position = new Vector3(-30f, 0f, 0f) };
            Run(2f);
            Vector3 was = herd[0].Herd.Center;
            sim.Noise(shooter.Position + Vector3.Up * 1.5f, 90f, (herd[0].Herd.Center + Vector3.Up - shooter.Position - Vector3.Up * 1.5f).Normalized(), shooter);
            Run(4f);
            Check(herd[0].Herd.Alarmed && herd.Count(c => c.State == CState.Flee) * 2 >= herd.Count && WildSim.Flat(herd[0].Herd.Center - shooter.Position).Length() > WildSim.Flat(was - shooter.Position).Length(),
                $"a shot through a herd of {grazer.CommonName} scatters it ({herd.Count(c => c.State == CState.Flee)} of {herd.Count} running, {WildSim.Flat(herd[0].Herd.Center - was).Length():F0} m off)");

            // A shot past an armed band: the aggressive come at the shooter (one that isn't friendly to them), the rest run or watch.
            int on = 0, tried = 0;
            var notes = new List<string>();
            foreach (Species s in bands.OrderBy(_ => _fresh.Float()).Take(6))
            {
                sim = Open();
                List<Creature> g = sim.SpawnGroup(s, Vector3.Zero, 3);
                if (g.Count == 0) continue;
                Stay(g);
                Party b = g[0].Herd.Party;
                b.Temper.Stance = Stance.Hostile;
                var gun = new DummyTarget { Position = new Vector3(-25f, 0f, 4f), Drawn = true };
                Run(1f);
                sim.Noise(gun.Position + Vector3.Up * 1.5f, 90f, (g[0].Position + Vector3.Up - gun.Position - Vector3.Up * 1.5f).Normalized(), gun);
                Run(4f, () => b.Mood == PartyMood.Fighting);
                tried++;
                if (b.Mood == PartyMood.Fighting && b.Foe == gun) on++;
                notes.Add($"{s.CommonName}: {b.Mood}");
            }
            Check(tried > 0 && on == tried, $"a shot past an armed band that would fight brings it on the shooter: {on} of {tried} ({string.Join(", ", notes)})");

            // A kept beast, collared, by a band: it never goes for its own; hurt, its people turn on whoever did it.
            sim = Open();
            List<Creature> keepers = sim.SpawnGroup(bands[_fresh.Int(0, bands.Count - 1)], Vector3.Zero, 3);
            Stay(keepers);
            Herd kh = keepers[0].Herd;
            Creature kept = sim.Keep(sim.Spawn(beast, new Vector3(3f, 0f, 2f)), kh);
            Piece wild = Rig.Create(beast, null).Layout.Pieces.Find(x => x.Name == "body"), worn = kept.Rig.Layout.Pieces.Find(x => x.Name == "body");
            Check(kept.Species.Collared && kept.Species.Domestic == DomesticKind.None && worn.Prims.Count > wild.Prims.Count, $"a kept {beast.CommonName} wears a collar ({wild.Prims.Count} parts to its body wild, {worn.Prims.Count} kept)");
            var wolf = sim.Spawn(hunters[0].Hunter, new Vector3(-22f, 0f, 0f));
            bool turned = false;
            Run(30f, () => (turned |= kept.AttackTarget != null && kh.Kin(kept.AttackTarget) || kept.ProvokedBy != null && kh.Kin(kept.ProvokedBy)) && false);
            Check(!turned && WildSim.Flat(kept.Position - kh.Center).Length() < 30f, $"it keeps by its people ({WildSim.Flat(kept.Position - kh.Center).Length():F0} m from them) and never goes for one of them, a {wolf.Species.CommonName} about ({kept.State})");
            var stranger = new DummyTarget { Position = new Vector3(18f, 0f, 0f) };
            kh.Party.Temper.Stance = Stance.Welcoming;
            sim.Damage(kept, 3f, stranger);
            Run(5f, () => kh.Party.Foe == stranger || kh.Party.Mood == PartyMood.Fleeing);
            Check(kh.Party.Mood == PartyMood.Fighting && kh.Party.Foe == stranger || kh.Party.Mood == PartyMood.Fleeing, $"hurting it is hurting them: the band {kh.Party.Mood}{(kh.Party.Foe == stranger ? " the one who did it" : "")}");
        }
        finally
        {
            Arming.Reach = reach;
            Arming.Fire = fire;
        }
    }
}
