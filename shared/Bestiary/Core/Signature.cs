using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// One kind a world's story leaves, as data: which of the bestiary's bodies it is made from (one
/// of the world's people, one of its machines, one of its beasts) and what is done to it (how far
/// mutated, which wrong shapes, what it wears and carries, what colour its clothes are, how it
/// moves and hunts, how many go together and where they keep to). Nothing here names a world.
/// </summary>
public sealed class SignatureKind
{
    public string Name { get; set; } = "";
    /// <summary>person, machine or beast.</summary>
    public string Body { get; set; } = "person";
    /// <summary>A person's side: Friendly, Raider or Feral.</summary>
    public string Occupancy { get; set; } = "Feral";
    /// <summary>Whose bodies they were (a <see cref="PersonRole"/> each).</summary>
    public string[] Roles { get; set; } = { "Worker" };
    public float[] Mutation { get; set; } = { 0f, 0f };
    /// <summary>How many of the wrong shapes (jaw, arm, hunch, head) each body has, at least and most.</summary>
    public int[] Wrongs { get; set; } = { 0, 0 };
    /// <summary>Skin pulled toward this colour (hex) by <see cref="SkinPull"/>.</summary>
    public string Skin { get; set; } = "";
    public float SkinPull { get; set; }
    /// <summary>Their clothes: "" their own; "uniform" one colour for all of them, from the world's paint; "festive" bright, from its paint, faded; or a list of hex colours.</summary>
    public string Cloth { get; set; } = "";
    public string[] Colours { get; set; } = Array.Empty<string>();
    /// <summary>Clothes dirtied toward grime by this much.</summary>
    public float Grime { get; set; }
    public bool Torn { get; set; }
    /// <summary>Armed: they carry what the era arms people with (see <see cref="SignatureTables.Armed"/>).</summary>
    public bool Armed { get; set; }
    public RoleLook Look { get; set; } = new();
    /// <summary>Garment slots (or garments) taken off first.</summary>
    public string[] Strip { get; set; } = Array.Empty<string>();
    public string Dress { get; set; } = "";
    public string Behaviour { get; set; } = "Herd";
    public string Temper { get; set; } = "Aggressive";
    public bool Hunts { get; set; } = true;
    public bool Flees { get; set; }
    public float Walk { get; set; } = 1f;
    public float Run { get; set; } = 1f;
    public float Health { get; set; } = 1f;
    public float Damage { get; set; }
    public int[] Group { get; set; } = { 2, 4 };
    /// <summary>Where they keep to, by weight: town (round living places' edges), ruins (round fallen places), road, open.</summary>
    public Dictionary<string, float> Haunts { get; set; } = new();
    /// <summary>How many more of them by night.</summary>
    public float Night { get; set; } = 1f;
    /// <summary>They hold places too (a twisted beasts' den has them, where the fall has them).</summary>
    public bool Den { get; set; }
    public string Activity { get; set; } = "Cathemeral";
    /// <summary>A beast's body: from the world's hunters or its herds.</summary>
    public string From { get; set; } = "hunter";
}

/// <summary>
/// What each world's story leaves walking it, as weights over <see cref="Kinds"/>: by the fall (or,
/// failing a line of its own, the fall's group), the theme and the rest of the roll (creeds,
/// presences); what everyone on the world wears for it (<see cref="Folk"/>); and what each era
/// gives people and machines.
/// </summary>
public sealed class SignatureTables
{
    public Dictionary<string, SignatureKind> Kinds { get; set; } = new();
    public Dictionary<string, Dictionary<string, float>> Falls { get; set; } = new();
    public Dictionary<string, Dictionary<string, float>> Groups { get; set; } = new();
    public Dictionary<string, Dictionary<string, float>> Themes { get; set; } = new();
    /// <summary>By a rolled value as layer.id (creed.festival_keepers, presence.pilgrims).</summary>
    public Dictionary<string, Dictionary<string, float>> Story { get; set; } = new();
    /// <summary>What everyone on the world wears for its story, by fall, fall group or theme (all that apply).</summary>
    public Dictionary<string, RoleLook> Folk { get; set; } = new();
    /// <summary>What the armed carry and wear in each era (Low, Mid, High).</summary>
    public Dictionary<string, RoleLook> Armed { get; set; } = new();
    /// <summary>What everyone wears in each era, and the dress they are made in (by chance).</summary>
    public Dictionary<string, RoleLook> Eras { get; set; } = new();
    public Dictionary<string, Dictionary<string, float>> EraDress { get; set; } = new();
    /// <summary>What each era wears in place of another's (a low-tech world's head wrap for a helmet, a high one's sealed helmet).</summary>
    public Dictionary<string, Dictionary<string, string>> EraSwap { get; set; } = new();
    /// <summary>Kinds every world where people survive has, whatever its story, by weight (hunting parties).</summary>
    public Dictionary<string, float> Always { get; set; } = new();
    /// <summary>People who pick through ruins, by weight, on worlds where people survive.</summary>
    public Dictionary<string, float> Ruins { get; set; } = new();
    /// <summary>Groups of the world's own kinds kept within reach of you as you go.</summary>
    public float Want { get; set; } = 2f;
    /// <summary>The most kinds of its own a world has.</summary>
    public int Most { get; set; } = 3;

    public static string Era(int tech) => tech <= 0 ? "Low" : tech == 1 ? "Mid" : "High";
}

/// <summary>A world's own kinds: what its story left walking it, each with its weight and its rules.</summary>
public sealed class WorldSignature
{
    public readonly List<(Species Species, SignatureKind Kind, float Weight)> Kinds = new();
    readonly HashSet<Species> _all = new();

    public void Add(Species s, SignatureKind k, float w)
    {
        Kinds.Add((s, k, w));
        _all.Add(s);
        if (s.Mate != null) _all.Add(s.Mate);
        if (s.Hosts != null) foreach (Species h in s.Hosts) _all.Add(h);
    }

    /// <summary>One of the world's own kinds (its young and its mate too).</summary>
    public bool Is(Species s) => s != null && (_all.Contains(s) || s.Adult != null && _all.Contains(s.Adult));

    /// <summary>Those that hold places as a twisted beasts' den does.</summary>
    public List<Species> Denning => Kinds.FindAll(k => k.Kind.Den).ConvertAll(k => k.Species);
    /// <summary>Its machines.</summary>
    public List<Species> Machines => Kinds.FindAll(k => k.Kind.Body == "machine").ConvertAll(k => k.Species);
    public bool Empty => Kinds.Count == 0;
}

public sealed partial class WorldLife
{
    WorldSignature _signature;
    List<(Species Species, float Weight)> _ruinCast;

    /// <summary>
    /// Who picks through the world's ruins (every ruin has someone): its scavengers and drifters where
    /// people survive, and its small pack animals and scavenging beasts, in small groups, anywhere.
    /// </summary>
    public IReadOnlyList<(Species Species, float Weight)> RuinCast => _ruinCast ??= MakeRuinCast();

    List<(Species, float)> MakeRuinCast()
    {
        var cast = new List<(Species, float)>();
        SignatureTables t = Data.Signature;
        if (World.Peopled)
        {
            int n = 30;
            foreach (var (id, w) in t.Ruins)
                if (w > 0f && t.Kinds.TryGetValue(id, out SignatureKind k) && MakeKind(id, k, n++) is { } s) cast.Add((s, w));
        }
        // Small beasts that go about in packs or live on leavings: the world's own, a ruin's roamers.
        var small = Fauna.Species.FindAll(x => Walks(x) && !x.Corrupted && x.Body.Size <= 1.3f && x.Behaviour is Behaviour.PackHunter or Behaviour.Scavenger or Behaviour.Predator or Behaviour.Grazer or Behaviour.Herd);
        small.Sort((a, b) => Roamer(b).CompareTo(Roamer(a)));
        // A world with nothing small of its own: the smallest of its walkers, as a smaller kind of it.
        if (small.Count == 0 && Fauna.Species.FindAll(x => Walks(x) && !x.Corrupted) is { Count: > 0 } any)
        {
            any.Sort((a, b) => a.Body.Size.CompareTo(b.Body.Size));
            Species least = any[0].Copy();
            float k = 1f / MathF.Max(1f, least.Body.Size);
            Proportions b = least.Body;
            b.Size *= k; b.Width *= k; b.Height *= k; b.LegLength *= k; b.LegThickness *= k; b.NeckLength *= k; b.HeadSize *= k; b.TailLength *= k;
            least.Stats.Radius *= k;
            least.Stats.MaxHealth = MathF.Round(least.Stats.MaxHealth * k * k);
            small.Add(least);
        }
        float beasts = World.Peopled ? 1.4f : 3f;
        for (int i = 0; i < small.Count && i < 3; i++)
        {
            Species r = small[i].Copy();
            r.Mate = null;
            r.Young = null;
            r.Id = small[i].Id + "-ruins";
            r.Index = 560 + i;
            r.Behaviour = Behaviour.Scavenger;
            r.Temperament = small[i].Behaviour is Behaviour.PackHunter or Behaviour.Predator ? Temperament.Bold : Temperament.Skittish;
            r.GroupMin = 3;
            r.GroupMax = 5;
            r.Stats.FleeRadius = MathF.Max(r.Stats.FleeRadius, 10f);
            cast.Add((r, beasts / Math.Min(3, small.Count)));
        }
        return cast;
    }

    static int Roamer(Species s) => s.Behaviour is Behaviour.PackHunter or Behaviour.Scavenger ? 2 : s.Behaviour == Behaviour.Predator ? 1 : 0;
    Species _settlers;
    bool _settlersMade;

    /// <summary>The world's people at home in its houses and farmsteads (several of them, each spawned one), dressed for its story and era; null where nobody survives.</summary>
    public Species Settlers
    {
        get
        {
            if (_settlersMade) return _settlers;
            _settlersMade = true;
            if (World.Peopled && Data.Signature.Kinds.TryGetValue("settlers", out SignatureKind k)) _settlers = MakeKind("settlers", k, 9);
            return _settlers;
        }
    }

    /// <summary>What this world's story left walking it: its own kinds, made from its own people, machines and beasts.</summary>
    public WorldSignature Signature => _signature ??= MakeSignature();

    WorldSignature MakeSignature()
    {
        var sig = new WorldSignature();
        SignatureTables t = Data.Signature;
        var weights = new Dictionary<string, float>();
        void Add(Dictionary<string, float> d, float k)
        {
            foreach (var (id, v) in d) weights[id] = weights.GetValueOrDefault(id) + v * k;
        }
        if (t.Falls.TryGetValue(World.Fall, out var fall)) Add(fall, 1f);
        else if (t.Groups.TryGetValue(World.FallGroup, out var group)) Add(group, 1f);
        if (t.Themes.TryGetValue(World.Theme, out var theme)) Add(theme, 1f);
        foreach (string s in World.Story)
            if (t.Story.TryGetValue(s, out var told)) Add(told, 0.8f);
        var order = new List<(string Id, float W)>();
        foreach (var (id, w) in weights)
            if (w > 0f && t.Kinds.ContainsKey(id)) order.Add((id, w));
        order.Sort((a, b) => b.W != a.W ? b.W.CompareTo(a.W) : string.CompareOrdinal(a.Id, b.Id));
        for (int i = 0; i < order.Count && sig.Kinds.Count < t.Most; i++)
        {
            SignatureKind k = t.Kinds[order[i].Id];
            // Nobody left alive: no living people of its own, only what has gone wild, its machines and its beasts.
            if (!World.Peopled && k.Body == "person" && k.Occupancy != "Feral") continue;
            if (MakeKind(order[i].Id, k, i) is { } s) sig.Add(s, k, order[i].W);
        }
        // And what every world where people survive has: hunting parties out in the wild.
        if (World.Peopled)
        {
            int n = 20;
            foreach (var (id, w) in t.Always)
                if (w > 0f && t.Kinds.TryGetValue(id, out SignatureKind k) && !sig.Kinds.Exists(x => x.Kind == k) && MakeKind(id, k, n++) is { } s) sig.Add(s, k, w);
        }
        return sig;
    }

    static Color Hex(string h) => Color.FromHtml(h.StartsWith('#') ? h : "#" + h);

    /// <summary>One of the world's own kinds, from its parts: the same world always makes the same.</summary>
    public Species MakeKind(string id, SignatureKind k, int n)
    {
        SignatureTables t = Data.Signature;
        ulong seed = Rng.Mix(Seed, Rng.Hash("signature/" + id));
        var rng = Rng.For(seed, "signature");
        int era = k.Body == "machine" ? Math.Max(World.Before, World.Now) : World.Now;
        Species s;
        switch (k.Body)
        {
            case "machine":
                // No machines on a world that never had more than low tech.
                if (!Robots.Allowed(World)) return null;
                s = Robots.Roll(World, n, era);
                break;
            case "beast":
            {
                var hosts = Fauna.Species.FindAll(x => Walks(x) && !x.Corrupted && (k.From == "herd" ? x.Behaviour is Behaviour.Herd or Behaviour.Grazer : Hunter(x) > 0));
                if (hosts.Count == 0) hosts = Fauna.Species.FindAll(x => Walks(x) && !x.Corrupted);
                if (hosts.Count == 0) return null;
                Species host = hosts[rng.Int(0, hosts.Count - 1)];
                s = _generator.Mutate(host, rng.Range(k.Mutation[0], MathF.Max(k.Mutation[0], k.Mutation[1])));
                if (ReferenceEquals(s, host)) s = host.Copy();
                s.Mate = null;
                s.Young = null;
                break;
            }
            default:
            {
                // Several of the world's people, each spawned one of them (as a world's Husks are): one look for a uniform, never one face.
                var hosts = new List<Species>();
                for (int i = 0; i < 5; i++)
                {
                    ulong one = Rng.Mix(seed, (ulong)i + 1);
                    Species h = Person(id, k, one, Rng.For(one, "signature"), era, t);
                    Finish(h, k, n, one, $"{Seed:x}-own-{id}-{i}");
                    hosts.Add(h);
                }
                hosts[0].Hosts = hosts;
                return hosts[0];
            }
        }
        Finish(s, k, n, seed, $"{Seed:x}-own-{id}");
        return s;
    }

    /// <summary>What a kind's rules do to any body made for it: its name, behaviour, temper, hours, numbers and stats.</summary>
    static void Finish(Species s, SignatureKind k, int n, ulong seed, string id)
    {
        s.Id = id;
        s.Seed = seed;
        s.Index = 600 + n;
        // A machine keeps its era's name (a clockwork machine, a sleek one); the rest are what the world calls them.
        if (k.Body != "machine") s.CommonName = k.Name;
        if (Enum.TryParse(k.Behaviour, out Behaviour b)) s.Behaviour = b;
        if (Enum.TryParse(k.Temper, out Temperament tm)) s.Temperament = tm;
        if (Enum.TryParse(k.Activity, out Activity act)) s.Activity = act;
        Stats st = s.Stats;
        st.Hunts = k.Hunts;
        st.Retaliates = k.Hunts || s.Temperament != Temperament.Skittish;
        st.FleeRadius = k.Flees ? MathF.Max(st.FleeRadius, 14f) : 0f;
        st.WalkSpeed *= k.Walk;
        st.RunSpeed *= k.Run;
        st.MaxHealth = MathF.Round(st.MaxHealth * k.Health);
        if (k.Damage > 0f) st.Damage = k.Damage;
        if (!k.Hunts && s.Figure != null && s.Figure.Occupancy == Occupancy.Friendly) st.Damage = 0f;
        s.GroupMin = Math.Max(1, k.Group[0]);
        s.GroupMax = Math.Max(s.GroupMin, k.Group.Length > 1 ? k.Group[1] : k.Group[0]);
        s.Scarcity = 1f;
    }

    /// <summary>One of the world's people as the story left them: mutated, wrong-shaped, dressed and armed for it.</summary>
    Species Person(string id, SignatureKind k, ulong seed, Rng rng, int era, SignatureTables t)
    {
        PersonRole role = Enum.TryParse(k.Roles[rng.Int(0, k.Roles.Length - 1)], out PersonRole r) ? r : PersonRole.Worker;
        Occupancy occ = Enum.TryParse(k.Occupancy, out Occupancy o) ? o : Occupancy.Feral;
        float mutation = rng.Range(k.Mutation[0], MathF.Max(k.Mutation[0], k.Mutation[1]));
        Species s = People.Person(seed, role, occ, mutation);
        Figure f = s.Figure;
        if (k.Strip.Length > 0) f.Wear.RemoveWhere(w => Array.IndexOf(k.Strip, HumanBody.Slot(w)) >= 0 || Array.IndexOf(k.Strip, w) >= 0);
        if (k.Strip.Length > 0 && Array.IndexOf(k.Strip, "prop") >= 0) f.Prop = "";
        if (k.Dress != "") f.Dress = k.Dress;
        // The era's arms first (a high-tech world's sealed helmet before a cap), then the kind's own look.
        if (k.Armed)
        {
            People.Arm(f, rng, clear: true);
            if (People.Armed is { Trim.Length: > 0 } armed && k.Cloth != "uniform") f.Trim = Hex(rng.Pick(armed.Trim));
        }
        Put(f, rng, k.Look.Wear);
        if (k.Look.Prop != "" && !k.Armed && rng.Chance(k.Look.PropChance)) f.Prop = k.Look.Prop;
        if (People.EraSwap != null && occ != Occupancy.Feral)
            foreach (var (from, to) in People.EraSwap)
                if (f.Wear.Remove(from) && to != "") f.Wear.Add(to);
        if (k.Look.Hair != "") f.HairStyle = k.Look.Hair;
        f.Stoop = MathF.Max(f.Stoop, k.Look.Stoop);
        f.Stagger = MathF.Max(f.Stagger, k.Look.Stagger);
        f.Build = Math.Clamp(f.Build + k.Look.Build, 0f, 1f);
        // Their clothes: one colour for a uniform, bright for a costume (faded with the years), or their own.
        var cloth = Rng.For(Rng.Mix(Seed, Rng.Hash("cloth/" + id)), "cloth");
        switch (k.Cloth)
        {
            case "uniform":
            {
                Color c = World.Paint.Count > 3 ? World.Paint[3] : Color.FromHsv(cloth.Range(0f, 1f), cloth.Range(0.25f, 0.45f), cloth.Range(0.28f, 0.4f));
                Color u = c.Lerp(new Color(0.3f, 0.32f, 0.26f), 0.35f);
                f.Cloth = u;
                f.Trim = u.Darkened(0.35f);
                break;
            }
            case "festive":
            {
                float hue = World.Paint.Count > 1 ? World.Paint[1].H : cloth.Range(0f, 1f);
                f.Cloth = Color.FromHsv(Mathf.PosMod(hue + rng.Range(-0.08f, 0.08f) + (rng.Chance(0.4f) ? 0.5f : 0f), 1f), rng.Range(0.6f, 0.85f), rng.Range(0.65f, 0.85f));
                f.Trim = Color.FromHsv(Mathf.PosMod(hue + 0.33f + rng.Range(-0.05f, 0.05f), 1f), 0.7f, 0.8f);
                f.Accent = Color.FromHsv(Mathf.PosMod(hue + 0.16f, 1f), 0.9f, 0.9f);
                // Years in the sun: faded, never new.
                f.Cloth = f.Cloth.Lerp(new Color(0.7f, 0.68f, 0.62f), 0.3f);
                f.Trim = f.Trim.Lerp(new Color(0.7f, 0.68f, 0.62f), 0.3f);
                break;
            }
            case "list" when k.Colours.Length > 0:
                f.Cloth = Hex(rng.Pick(k.Colours));
                f.Trim = f.Cloth.Darkened(0.35f);
                break;
        }
        if (k.Grime > 0f)
        {
            var grime = new Color(0.24f, 0.2f, 0.16f);
            f.Cloth = f.Cloth.Lerp(grime, k.Grime);
            f.Trim = f.Trim.Lerp(grime, k.Grime * 0.8f);
        }
        if (k.Skin != "")
        {
            f.Skin = f.Skin.Lerp(Hex(k.Skin), k.SkinPull);
            s.Colors.Base = f.Skin;
        }
        if (k.Torn) f.Torn = true;
        // The wrong shapes, from the same parts the worms use: a jaw hanging, an arm too long, a hunch, a head on one side.
        int wrongs = rng.Int(k.Wrongs[0], Math.Max(k.Wrongs[0], k.Wrongs.Length > 1 ? k.Wrongs[1] : k.Wrongs[0]));
        if (wrongs > 0)
        {
            var pool = new List<Wrong> { Wrong.Jaw, Wrong.Arm, Wrong.Hunch, Wrong.Head };
            rng.Shuffle(pool);
            for (int i = 0; i < Math.Min(wrongs, pool.Count); i++) f.Wrongs |= pool[i];
            if (f.Wrongs.HasFlag(Wrong.Jaw)) f.JawDrop = rng.Range(0.3f, 0.6f);
            if (f.Wrongs.HasFlag(Wrong.Arm)) { f.LongArm = rng.Int(0, 1); f.ArmStretch = rng.Range(1.18f, 1.34f); }
            if (f.Wrongs.HasFlag(Wrong.Hunch)) f.Stoop = MathF.Max(f.Stoop, rng.Range(0.5f, 0.75f));
            if (f.Wrongs.HasFlag(Wrong.Head)) { f.HeadTilt = rng.Range(0.3f, 0.55f) * (rng.Chance(0.5f) ? 1f : -1f); f.HeadDroop = rng.Range(0.04f, 0.12f); }
        }
        f.Measure();
        return s;
    }

    /// <summary>Garments on by chance, one to a slot: whatever filled a slot stays.</summary>
    internal static void Put(Figure f, Rng rng, Dictionary<string, float> table)
    {
        if (table == null) return;
        var ids = new List<string>(table.Keys);
        ids.Sort(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (!rng.Chance(table[id])) continue;
            string slot = HumanBody.Slot(id);
            bool taken = false;
            if (slot != "")
                foreach (string w in f.Wear) if (HumanBody.Slot(w) == slot) { taken = true; break; }
            if (!taken) f.Wear.Add(id);
        }
    }
}
