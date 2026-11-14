using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// The owner (14 Nov 2026, and twice before): "Wandering people encountered still act like animals -
/// running away or attack with no in between." Bands of people met out in the world, on fresh worlds
/// every run, take to you by their stance as people do. Run alone with --wanderers.
/// </summary>
public partial class BestiaryTest
{
    const float BandDt = 1f / 30f;

    void BandsOfPeople()
    {
        BandsRollTheirStance();
        BandsMeetYouAsPeople();
        BandsAnswerWhatYouDid();
    }

    /// <summary>The people a world has out in the open: its own kinds (by its story), its hunting parties, its ruin roamers and its settlers.</summary>
    static IEnumerable<(string Kind, Species Species)> Wanderers(WorldLife life)
    {
        foreach (var k in life.Signature.Kinds) if (k.Species.Behaviour == Behaviour.Folk) yield return (k.Species.CommonName, k.Species);
        foreach (var (s, _) in life.RuinCast) if (s.Behaviour == Behaviour.Folk) yield return (s.CommonName, s);
        if (life.Settlers is { Behaviour: Behaviour.Folk } settlers) yield return (settlers.CommonName, settlers);
    }

    /// <summary>A world with a story: any fall in the data, any era, now and then mutagenic or hot with radiation.</summary>
    WorldConditions StoryWorld()
    {
        var w = World(_fresh.Range(-15f, 35f), mutagen: _fresh.Chance(0.3f) ? _fresh.Range(0.2f, 0.9f) : 0f);
        List<string> falls = _data.Signature.Falls.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        w.Fall = falls[_fresh.Int(0, falls.Count - 1)];
        w.Now = _fresh.Int(0, 2);
        w.Before = Math.Max(w.Now, _fresh.Int(0, 2));
        w.Radiation = _fresh.Chance(0.2f) ? _fresh.Range(1f, 4f) : 0f;
        return w;
    }

    static string Shares(int[] n) => n.Sum() == 0 ? "none" : $"{100 * n[0] / n.Sum()}% friendly, {100 * n[1] / n.Sum()}% wary, {100 * n[2] / n.Sum()}% hostile of {n.Sum()}";

    /// <summary>
    /// Every band rolls friendly, wary or hostile from who they are and their world: all three happen,
    /// raiders and soldiers come for you far more often than settlers and pilgrims, a band with no
    /// weapons never picks a fight, a world falling apart is colder, and a low-tech one warier.
    /// </summary>
    void BandsRollTheirStance()
    {
        int[] all = new int[3], raiders = new int[3], others = new int[3], unarmed = new int[3], stable = new int[3], fallen = new int[3], low = new int[3], high = new int[3];
        var kinds = new Dictionary<string, int[]>();
        bool animalsKeepTheirs = true;
        for (int i = 0; i < 80; i++)
        {
            WorldConditions w = StoryWorld();
            WorldLife life = Bestiary.Roll(_data, w);
            animalsKeepTheirs &= life.Fauna.Species.All(s => s.Behaviour != Behaviour.Folk || s.Figure != null);
            foreach (var (kind, s) in Wanderers(life))
            {
                bool raider = (s.Hosts ?? new List<Species> { s }).Any(h => h.Figure?.Occupancy == Occupancy.Raider);
                bool arms = (s.Hosts ?? new List<Species> { s }).Any(h => Weapons.Armed(h.Figure?.Prop ?? ""));
                for (int j = 0; j < 12; j++)
                {
                    int l = (int)Party.Roll(s, Seed()).Leaning;
                    all[l]++;
                    (raider ? raiders : others)[l]++;
                    if (!arms) unarmed[l]++;
                    if (!raider) (s.Collapse < 0.15f ? stable : s.Collapse > 0.35f ? fallen : new int[3])[l]++;
                    if (!raider) (s.Tech == "low" ? low : s.Tech == "high" ? high : new int[3])[l]++;
                    if (!kinds.TryGetValue(kind, out int[] k)) kinds[kind] = k = new int[3];
                    k[l]++;
                }
            }
        }
        float Share(int[] n, int l) => n.Sum() == 0 ? 0f : n[l] / (float)n.Sum();
        GD.Print($"    bands by kind: {string.Join("; ", kinds.OrderByDescending(k => k.Value.Sum()).Select(k => $"{k.Key} {Shares(k.Value)}"))}");
        Check(all.Sum() > 800 && all.All(n => n > all.Sum() * 0.08f), $"bands roll every stance: {Shares(all)}");
        Check(Share(raiders, 2) > Share(others, 2) + 0.35f && Share(others, 0) > Share(raiders, 0) + 0.3f, $"raiders and soldiers come for you ({Shares(raiders)}); settlers, pilgrims and hunters mostly let you be ({Shares(others)})");
        Check(unarmed.Sum() > 50 && unarmed[2] == 0, $"a band with no weapons never picks a fight: {Shares(unarmed)}");
        Check(Share(fallen, 0) < Share(stable, 0) - 0.05f, $"a world falling apart is colder ({Shares(fallen)}) than a stable one ({Shares(stable)})");
        Check(Share(low, 1) > Share(high, 1) + 0.05f, $"a low-tech world is warier ({Shares(low)}) than a high-tech one ({Shares(high)})");
        Check(animalsKeepTheirs, "no animal goes about as a band of people");
    }

    /// <summary>
    /// A scripted sleeper meets band after band on fresh worlds: walks up to 10 m, stands, walks off
    /// out of their sight, comes back to 16 m, aims at the leader, fires past them, and leaves. By the
    /// stance each rolled: nobody friendly or wary runs with no threat; the friendly keep a comfortable
    /// distance, the wary back off and warn before anything else, every time; the hostile attack; the
    /// friendly and the wary settle once you have gone. The leader takes everything in first.
    /// </summary>
    void BandsMeetYouAsPeople()
    {
        var reach = Arming.Reach;
        var fire = Arming.Fire;
        var blow = Arming.BlowDamage;
        WildSim sim = null;
        int shots = 0;
        // Guns that reach 35 m along a clear line, and fire without hurting anyone: enough to see who shoots, and from where.
        Arming.Reach = (c, t) => c.Species.Figure is { } f && Weapons.Ranged(f.Prop) && WildSim.Flat(t.Position - c.Position).Length() < 35f && sim.Nav.LineClear(c.Position, t.Position) ? 35f : 0f;
        Arming.Fire = (w, c, t) => { shots++; return 1f; };
        Arming.BlowDamage = null;
        try
        {
            var bands = new int[3];
            var fledNoThreat = new int[3];
            var foughtNoThreat = new int[3];
            var kept = new int[3];
            var calmLeft = new int[3];
            var calmEnd = new int[3];
            var fledShot = new int[3];
            int warnedOnApproach = 0, waryNoticed = 0, waryFights = 0, waryFightsWarned = 0, hostileAttacked = 0, before = 0, after = 0, rangedFrames = 0, coverFrames = 0, anyFrames = 0, anyNear = 0;
            var notes = new List<string>();
            for (int tries = 0; tries < 400 && (bands.Sum() < 36 || bands.Min() < 8) && bands.Sum() < 60; tries++)
            {
                WorldLife life = Bestiary.Roll(_data, StoryWorld());
                var kinds = Wanderers(life).ToList();
                if (kinds.Count == 0) continue;
                var (kind, s) = kinds[_fresh.Int(0, kinds.Count - 1)];
                sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
                for (int k = 0; k < 18; k++) sim.Nav.AddObstacle(new Vector3(_fresh.Range(-45f, 45f), 0f, _fresh.Range(-45f, 45f)), _fresh.Range(0.5f, 1.3f));
                // How much of the ground is beside cover anyway: what standing anywhere would give.
                for (int k = 0; k < 200; k++)
                {
                    var p = new Vector3(_fresh.Range(-40f, 40f), 0f, _fresh.Range(-40f, 40f));
                    anyFrames++;
                    if (sim.Nav.Obstacles.Any(o => WildSim.Flat(o.Position - p).Length() - o.Radius < 1.5f)) anyNear++;
                }
                List<Creature> band = sim.SpawnGroup(s, Vector3.Zero, _fresh.Int(Math.Max(2, s.GroupMin), Math.Max(2, s.GroupMax)));
                if (band.Count == 0) continue;
                Herd h = band[0].Herd;
                Party b = h.Party;
                int l = (int)b.Leaning;
                if (bands[l] >= 20) continue; // (enough of this kind of band: room for the others)
                bands[l]++;
                foreach (Creature c in band) c.RigInterval = 3;
                float a = _fresh.Range(0f, MathF.Tau);
                var you = new DummyTarget { Position = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * 52f, Health = new Health(1e9f) };
                sim.Targets.Add(you);
                int telegraphs = 0;
                sim.Telegraph += e => { if (e.Target == you) telegraphs++; };
                string phase = "";
                var fled = new HashSet<string>();
                var fought = new HashSet<string>();
                int warningsAtReturn = 0;
                float returnStart = 0f;
                bool fightSeen = false, seen = false;
                IEnumerable<Creature> Alive() => band.Where(c => c.Alive && !c.Removed);
                Vector3 Mid() => Alive().Aggregate(Vector3.Zero, (v, c) => v + c.Position) / Math.Max(1, Alive().Count());
                Creature NearestOne() => Alive().OrderBy(c => WildSim.Flat(c.Position - you.Position).Length()).First();
                float Near() => Alive().Min(c => WildSim.Flat(c.Position - you.Position).Length());
                Vector3 At(Creature c) => (c.Position + Vector3.Up * 1.1f - (you.Position + Vector3.Up * 1.6f)).Normalized();
                void Step(Vector3 face)
                {
                    you.Forward = face;
                    you.Health.Restore();
                    sim.Step(BandDt);
                    if (b.Outsider == you && phase is "approach" or "stand") seen = true;
                    if (b.Mood == PartyMood.Fleeing || Alive().Any(c => c.State == CState.Flee)) fled.Add(phase);
                    if (b.Mood == PartyMood.Fighting)
                    {
                        fought.Add(phase);
                        // The first turn to a fight over what you did (a gun on them), for a wary band: was it warned first?
                        if (!fightSeen && l == (int)Leaning.Wary && phase == "aim")
                        {
                            fightSeen = true;
                            waryFights++;
                            if (b.Warnings > warningsAtReturn && b.WarnedAt >= returnStart) waryFightsWarned++;
                        }
                        foreach (Creature c in Alive())
                        {
                            if (!Weapons.Ranged(c.Species.Figure.Prop) || c.WantAim < 0.5f || WildSim.Flat(c.Velocity).Length() > 0.3f) continue;
                            rangedFrames++;
                            if (sim.Nav.Near(c.Position) is { } near && near.Any(o => WildSim.Flat(o.Position - c.Position).Length() - o.Radius < 1.5f)) coverFrames++;
                        }
                    }
                }
                void WalkTo(Func<Vector3> goal, float stopAt, float speed, float most, bool facing)
                {
                    for (float t = 0f; t < most; t += BandDt)
                    {
                        Vector3 to = WildSim.Flat(goal() - you.Position);
                        if (to.Length() <= stopAt) return;
                        you.Position += to.Normalized() * speed * BandDt;
                        Step(facing ? At(Brain.Leader(h) ?? band[0]) : to.Normalized());
                    }
                }
                void Wait(float seconds, Func<Vector3> face) { for (float t = 0f; t < seconds; t += BandDt) Step(face()); }

                phase = "approach";
                WalkTo(() => NearestOne().Position, 10f, 1.6f, 45f, true);
                phase = "stand";
                float startNear = Near(), least = float.MaxValue;
                for (float t = 0f; t < 8f; t += BandDt) { Step(At(Brain.Leader(h) ?? band[0])); least = MathF.Min(least, Near()); }
                float endNear = Near();
                if (l == (int)Leaning.Wary && seen) { waryNoticed++; if (b.Warnings > 0) warnedOnApproach++; }
                if (l == (int)Leaning.Friendly ? least >= 1.5f : l == (int)Leaning.Wary ? endNear > startNear + 1f || endNear >= 12f : true) kept[l]++;
                phase = "leave";
                Vector3 away = WildSim.Flat(you.Position - Mid()).Normalized();
                WalkTo(() => Mid() + away * 55f, 1f, 1.8f, 50f, false);
                phase = "gone";
                Wait(12f, () => away);
                if (b.Mood == PartyMood.Easy) calmLeft[l]++;
                phase = "return";
                warningsAtReturn = b.Warnings;
                returnStart = sim.Time;
                WalkTo(() => NearestOne().Position, 16f, 1.6f, 45f, true);
                phase = "aim";
                you.Drawn = true;
                Wait(4f, () => At(Brain.Leader(h) ?? band[0]));
                phase = "fire";
                Creature lead = Brain.Leader(h) ?? band[0];
                Vector3 muzzle = you.Position + Vector3.Up * 1.5f, side = WildSim.Flat(lead.Position - you.Position).Normalized().Cross(Vector3.Up);
                sim.Noise(muzzle, 120f, (lead.Position + Vector3.Up * 1.2f + side * 1.5f - muzzle).Normalized(), you);
                Wait(6f, () => At(lead));
                if (fled.Contains("fire")) fledShot[l]++;
                phase = "away";
                you.Drawn = false;
                away = WildSim.Flat(you.Position - Mid()).Normalized();
                WalkTo(() => Mid() + away * 60f, 1f, 1.8f, 60f, false);
                Wait(20f, () => away);
                if (b.Mood == PartyMood.Easy) calmEnd[l]++;
                string[] calm = { "approach", "stand", "leave", "gone", "return" };
                if (calm.Any(fled.Contains)) fledNoThreat[l]++;
                if (calm.Any(fought.Contains)) foughtNoThreat[l]++;
                if (l == (int)Leaning.Hostile && (telegraphs > 0 || fought.Contains("approach") || fought.Contains("stand"))) hostileAttacked++;
                before += b.BeforeLeader;
                after += b.AfterLeader;
                if (notes.Count < 10 && (l != (int)Leaning.Hostile && (calm.Any(fled.Contains) || calm.Any(fought.Contains)) || l == (int)Leaning.Hostile && telegraphs == 0 && !fought.Contains("approach")))
                    notes.Add($"{kind} ({(Leaning)l}, {b.Stance}): fled in {string.Join("/", fled)}, fought in {string.Join("/", fought)}, warned {b.Warnings}, mood {b.Mood}");
            }
            string By(int[] n) => $"friendly {n[0]}/{bands[0]}, wary {n[1]}/{bands[1]}, hostile {n[2]}/{bands[2]}";
            foreach (string n in notes) GD.Print($"    ({n})");
            Check(bands.Sum() >= 30 && bands.All(n => n >= 6), $"{bands.Sum()} bands met on fresh worlds: {By(bands)}");
            Check(fledNoThreat[0] == 0 && fledNoThreat[1] == 0, $"nobody friendly or wary runs with no threat (walked up to, stood by, walked off from, come back to): fled {By(fledNoThreat)}");
            Check(foughtNoThreat[0] == 0 && foughtNoThreat[1] == 0, $"nor turns on you for it: fought {By(foughtNoThreat)}");
            Check(kept[0] == bands[0] && kept[1] >= bands[1] * 0.9f, $"the friendly keep a comfortable distance and the wary back off as you stand by them: {By(kept)}");
            Check(waryNoticed >= bands[1] * 0.8f && warnedOnApproach == waryNoticed, $"every wary band that sees you coming warns you off ({warnedOnApproach} of the {waryNoticed} of {bands[1]} that saw you: a rock between can hide you)");
            Check(waryFights > 0 && waryFightsWarned == waryFights, $"a gun on a wary band: it turns only after its warning, every time ({waryFightsWarned} of {waryFights} warned first)");
            Check(hostileAttacked >= bands[2] * 0.9f, $"hostile bands come for you ({hostileAttacked} of {bands[2]} attacked)");
            Check(calmLeft[0] == bands[0] && calmLeft[1] >= bands[1] * 0.9f && calmEnd[0] >= bands[0] * 0.9f && calmEnd[1] >= bands[1] * 0.9f,
                $"once you have gone they settle: after you walk off {By(calmLeft)}; after the gun and the shot {By(calmEnd)}");
            Check(fledShot[0] >= bands[0] * 0.8f, $"a shot past a friendly band is a real threat: it runs ({By(fledShot)} ran when shot past)");
            Check(before == 0 && after > bands.Sum(), $"the leader takes everything in first: {after} reactions after their leader's, {before} before");
            float covered = coverFrames / (float)Math.Max(1, rangedFrames), anywhere = anyNear / (float)Math.Max(1, anyFrames);
            Check(rangedFrames > 0 && covered > 3f * anywhere, $"in a fight, those with guns shoot from cover beside a rock or a trunk: {covered * 100f:F0}% of their shooting time, against {anywhere * 100f:F0}% of the ground being beside any ({shots} shots)");
        }
        finally
        {
            Arming.Reach = reach;
            Arming.Fire = fire;
            Arming.BlowDamage = blow;
        }
    }

    /// <summary>
    /// What moves a band besides your coming: your standing with their people (low, they keep you off;
    /// far down, they come for you; among friends the wary warm); hurting one of them turns the band and
    /// friends of theirs nearby; a Husk close by is a real threat.
    /// </summary>
    void BandsAnswerWhatYouDid()
    {
        Species Kind(Func<Party, Species, bool> want)
        {
            for (int i = 0; i < 200; i++)
                foreach (var (_, s) in Wanderers(Bestiary.Roll(_data, StoryWorld())).ToList())
                    if (want(Party.Roll(s, Seed()), s)) return s;
            return null;
        }
        Species armed = Kind((b, s) => (s.Hosts ?? new List<Species> { s }).All(h => Weapons.Armed(h.Figure.Prop)) && s.Figure.Occupancy == Occupancy.Friendly);
        if (armed == null) { Check(false, "an armed friendly kind of band to try"); return; }
        (WildSim, List<Creature>, DummyTarget) Meet(float standing, int bands = 1, Stance? stance = null)
        {
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f)) { StandingAt = _ => standing };
            var you = new DummyTarget { Position = new Vector3(0f, 0f, 20f), Health = new Health(1e9f), Forward = Vector3.Back };
            sim.Targets.Add(you);
            var all = new List<Creature>();
            for (int i = 0; i < bands; i++)
            {
                List<Creature> g = sim.SpawnGroup(armed, new Vector3(i * 14f, 0f, 0f), 3);
                if (stance is { } st) g[0].Herd.Party.Temper.Stance = st;
                all.AddRange(g);
            }
            for (int i = 0; i < 90; i++) sim.Step(BandDt);
            return (sim, all, you);
        }
        var (_, cold, _) = Meet(-50f, stance: Stance.Welcoming);
        var (_, foes, _) = Meet(-90f, stance: Stance.Welcoming);
        var (_, warm, _) = Meet(60f, stance: Stance.Wary);
        Check(cold[0].Herd.Party.Leaning == Leaning.Wary && foes[0].Herd.Party.Leaning == Leaning.Hostile && warm[0].Herd.Party.Leaning == Leaning.Friendly,
            $"your standing with their people moves a band: a welcoming one at -50 is {cold[0].Herd.Party.Stance}, at -90 {foes[0].Herd.Party.Stance}; a wary one among friends (+60) is {warm[0].Herd.Party.Stance}");

        var (sim2, two, you2) = Meet(0f, bands: 2, stance: Stance.Welcoming);
        sim2.Damage(two[0], 1f, you2);
        for (int i = 0; i < 60; i++) sim2.Step(BandDt);
        Party hurt = two[0].Herd.Party, friends = two[^1].Herd.Party;
        Check(hurt != friends && hurt.Mood == PartyMood.Fighting && hurt.Foe == you2 && friends.Turned && friends.Foe == you2,
            $"one of a band hurt: it turns on you ({hurt.Mood}), and so does a friendly band 14 m off ({friends.Mood}, turned {friends.Turned})");

        WorldLife life = Bestiary.Roll(_data, World(15f));
        Species husk = life.People.Person(Seed(), PersonRole.Worker, Occupancy.Friendly, 0.2f, husk: true);
        Species calm = Kind((b, s) => b.Leaning == Leaning.Friendly && !(s.Hosts ?? new List<Species> { s }).Any(h => Weapons.Armed(h.Figure.Prop)));
        if (calm == null) { Check(false, "an unarmed friendly kind of band to try"); return; }
        var sim3 = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
        List<Creature> folk = sim3.SpawnGroup(calm, Vector3.Zero, 3);
        folk[0].Herd.Party.Temper.Stance = Stance.Welcoming;
        sim3.Spawn(husk, new Vector3(14f, 0f, 0f));
        bool ran = false;
        for (int i = 0; i < 150 && !ran; i++) { sim3.Step(BandDt); ran = folk[0].Herd.Party.Mood == PartyMood.Fleeing; }
        Check(ran, $"a Husk 14 m off is a real threat: an unarmed friendly band runs from it ({folk[0].Herd.Party.Mood})");
    }
}
