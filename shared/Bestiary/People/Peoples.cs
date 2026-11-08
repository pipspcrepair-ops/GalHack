using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Rolls a folk: the human skeleton with parts at its sockets (docs/alien-peoples.md). Nothing
/// here names a people. A folk spends a budget of outline points (3 + 7 x the world's alienness)
/// on one part or none at each socket, steered by its covering and the climate it settled in, and
/// kept believable by a few rules: one mouth part, one eye plan, one crown, one back, tidy and
/// symmetrical, nothing cold-blooded native to ice, and at least two things that change its
/// outline so it reads at 30 m.
/// </summary>
public static class Peoples
{
    /// <summary>Pigment families: real animal colours (hue, saturation and value ranges).</summary>
    static readonly (string Name, float[] Hue, float[] Sat, float[] Val)[] Families =
    {
        ("browns", new[] { 0.05f, 0.09f }, new[] { 0.3f, 0.55f }, new[] { 0.25f, 0.72f }),
        ("slate", new[] { 0.55f, 0.65f }, new[] { 0.05f, 0.18f }, new[] { 0.3f, 0.62f }),
        ("ochre", new[] { 0.06f, 0.11f }, new[] { 0.4f, 0.55f }, new[] { 0.38f, 0.68f }),
        ("moss", new[] { 0.17f, 0.28f }, new[] { 0.2f, 0.45f }, new[] { 0.3f, 0.58f }),
        ("dusk", new[] { 0.62f, 0.78f }, new[] { 0.15f, 0.35f }, new[] { 0.25f, 0.52f }),
        ("bone", new[] { 0.06f, 0.12f }, new[] { 0.05f, 0.18f }, new[] { 0.68f, 0.84f }),
        ("black", new[] { 0.6f, 0.7f }, new[] { 0.05f, 0.15f }, new[] { 0.18f, 0.26f }),
    };

    /// <summary>Which families each covering comes in.</summary>
    static string[] FamiliesOf(string covering) => covering switch
    {
        "fur" => new[] { "browns", "ochre", "slate", "bone", "black" },
        "feathers" => new[] { "browns", "slate", "bone", "black", "ochre" },
        "scales" => new[] { "moss", "ochre", "slate", "dusk" },
        "chitin" => new[] { "black", "browns", "dusk", "ochre" },
        _ => new[] { "browns", "slate", "ochre", "moss", "dusk", "bone", "black" },
    };

    static float Points(string socket, string part) => (socket, part) switch
    {
        (_, "") or ("ears", "human" or "round" or "droop" or "none") or ("eyes", "two" or "three" or "four") => 0f,
        ("ears", "leaf" or "fin") or ("crown", "knobs" or "quills") or ("mouth", "muzzle") or ("eyes", "compound" or "band") or ("tail", "stub") or ("back", "ridge") => 1f,
        _ => 2f,
    };

    /// <summary>
    /// One folk for a world: its covering, colours, frame and parts. <paramref name="lean"/>
    /// biases parts towards a kind (the Vharn: carapace, mandibles, antennae, chitin), and is
    /// still only a bias on the same rolls.
    /// </summary>
    public static Folk Roll(Rng rng, WorldConditions w, string id, Dictionary<string, float> lean = null, string covering = null)
    {
        lean ??= new Dictionary<string, float>();
        float Lean(string what) => lean.TryGetValue(what, out float b) ? b : 1f;
        ClimateBand band = w.Climate;
        bool cold = band is ClimateBand.Frozen or ClimateBand.Cold, hot = band is ClimateBand.Hot or ClimateBand.Scorching;
        var f = new Folk { Id = id, Human = false };
        float budget = 3f + 7f * w.Alienness + rng.Range(-0.5f, 0.8f);

        // Covering first: it steers everything else. Nothing scaled or plated is native to ice.
        var cover = new Dictionary<string, float>
        {
            ["skin"] = 2.5f, ["fur"] = cold ? 4f : hot ? 0.3f : 1.5f, ["feathers"] = band == ClimateBand.Frozen ? 0.4f : hot ? 0.8f : 1.2f,
            ["scales"] = cold ? 0f : hot ? 3f : 1f, ["chitin"] = cold ? 0f : hot ? 1.6f : 0.7f,
        };
        f.Covering = covering ?? Pick(rng, cover, Lean);
        string c = f.Covering;
        f.Hairless = c != "skin" || rng.Chance(0.4f);

        // Colour: a family the covering comes in; the world's light shifts it, as for anyone.
        string family = lean.ContainsKey("dark") ? rng.Pick(new[] { "black", "dusk", "browns" }) : rng.Pick(FamiliesOf(c));
        var fam = Array.Find(Families, x => x.Name == family);
        f.SkinHue = Narrow(rng, fam.Hue, 0.5f);
        f.SkinSat = fam.Sat;
        f.SkinVal = Narrow(rng, fam.Val, 0.7f);
        float markHue = Wrap(rng.Range(fam.Hue) + rng.Range(0.3f, 0.7f));
        f.Mark = Color.FromHsv(markHue, rng.Range(0.3f, 0.55f), rng.Range(0.45f, 0.8f));
        f.Points = cold && f.SkinVal[1] > 0.6f ? rng.Range(0.5f, 0.9f) : rng.Chance(0.4f) ? rng.Range(0.2f, 0.7f) : 0f;
        f.DarkVision = rng.Chance(cold ? 0.7f : 0.3f) ? rng.Range(0.5f, 1f) : rng.Range(0f, 0.3f);
        f.EyeShine = Color.FromHsv(rng.Range(0.05f, 0.55f), 0.6f, 0.9f);
        f.EyeColour = rng.Pick(new[] { new Color(0.55f, 0.36f, 0.08f), new Color(0.62f, 0.5f, 0.12f), new Color(0.2f, 0.36f, 0.14f), new Color(0.42f, 0.08f, 0.06f), new Color(0.05f, 0.04f, 0.04f), new Color(0.16f, 0.1f, 0.03f) });

        // The frame: cold thickens and shortens, heat draws out (the wildlife fit rule's numbers).
        float limbs = band switch { ClimateBand.Frozen => 0.82f, ClimateBand.Cold => 0.92f, ClimateBand.Hot => 1.08f, ClimateBand.Scorching => 1.14f, _ => 1f };
        f.HeightScale = rng.Range(0.9f, 1.12f);
        f.LimbScale = 1f;
        f.LegScale = Math.Clamp(rng.Range(0.92f, 1.1f) * MathF.Sqrt(limbs), 0.86f, 1.16f);
        f.ArmScale = Math.Clamp(rng.Range(0.9f, 1.2f) * MathF.Sqrt(limbs), 0.8f, 1.28f);
        f.NeckScale = rng.Chance(0.25f) ? rng.Range(1.4f, 2.4f) : rng.Range(0.6f, 1.3f);
        f.HeadScale = rng.Range(0.88f, 1.2f);
        f.ShoulderScale = Math.Clamp(rng.Range(0.88f, 1.22f) / MathF.Sqrt(limbs), 0.84f, 1.28f);
        f.StoopBias = rng.Chance(0.3f) ? rng.Range(0.15f, 0.4f) : rng.Range(-0.1f, 0.08f);
        f.BuildBias = band switch { ClimateBand.Frozen => 0.14f, ClimateBand.Cold => 0.08f, ClimateBand.Hot => -0.06f, ClimateBand.Scorching => -0.1f, _ => 0f } + rng.Range(-0.1f, 0.15f);
        f.BrowRidge = rng.Range(0.2f, 1f);
        f.Dimorphism = rng.Range(0.2f, 1.7f);

        // Parts, socket by socket, each weighted by the covering and the climate.
        string Socket(string socket, Dictionary<string, float> options)
        {
            var can = new Dictionary<string, float>();
            foreach (var (part, wt) in options)
                if (Points(socket, part) <= budget) can[part] = wt;
            string got = Pick(rng, can, Lean);
            budget -= Points(socket, got);
            f.Spent += (int)Points(socket, got);
            return got;
        }
        bool fur = c == "fur", feathers = c == "feathers", scales = c == "scales", chitin = c == "chitin";
        f.Mouth = Socket("mouth", new()
        {
            [""] = c == "skin" ? 5f : 1f, ["muzzle"] = fur ? 3f : c == "skin" ? 1f : 0.3f, ["snout"] = fur ? 2f : scales ? 3f : 0.3f,
            ["beak"] = feathers ? 6f : 0.1f, ["mandibles"] = chitin ? 6f : 0f,
        });
        f.Tusks = f.Mouth is "" or "muzzle" or "snout" && budget >= 1f && rng.Chance(fur || c == "skin" ? 0.22f * Lean("tusks") : 0.08f);
        if (f.Tusks) { budget -= 1f; f.Spent++; }
        f.Eyes = Socket("eyes", new() { ["two"] = chitin ? 1f : 8f, ["three"] = 0.5f, ["four"] = 0.4f, ["compound"] = chitin ? 6f : 0.1f, ["band"] = chitin ? 1f : 0.3f });
        f.EyeSize = f.DarkVision > 0.5f ? rng.Range(1.2f, 1.6f) : rng.Range(0.8f, 1.2f);
        f.Crown = Socket("crown", new()
        {
            [""] = 2.5f, ["pair"] = fur || c == "skin" ? 1.5f : 0.6f, ["ram"] = fur ? 1.2f : 0.3f, ["brow"] = 0.4f, ["knobs"] = 1f,
            ["antlers"] = fur && !hot ? 2f : 0.2f, ["crown"] = scales ? 1f : 0.3f, ["antennae"] = chitin ? 5f : 0.3f,
            ["crest"] = feathers || scales ? 2f : 0.3f, ["quills"] = fur ? 1.2f : 0.4f, ["plume"] = feathers ? 2.5f : 0.1f, ["keel"] = scales || c == "skin" ? 1f : 0.2f,
        });
        f.CrownSize = rng.Range(0.85f, 1.25f);
        f.Ears = f.Crown == "antennae" || chitin ? "none" : Socket("ears", new()
        {
            ["human"] = c == "skin" ? 3f : 0.5f, ["round"] = fur ? 2f : 0.6f, ["leaf"] = 1.5f, ["tall"] = fur ? 1.2f * (hot ? 1.6f : 1f) : 0.3f,
            ["fan"] = hot ? 2f : 0.3f, ["droop"] = fur ? 0.8f : 0.2f, ["fin"] = scales ? 2f : 0.2f, ["none"] = scales || feathers ? 2f : 0.2f,
        });
        f.EarSize = f.Ears is "tall" or "fan" ? rng.Range(0.9f, 1.3f) : band == ClimateBand.Frozen ? rng.Range(0.6f, 0.85f) : rng.Range(0.8f, 1.3f);
        f.Tail = Socket("tail", new()
        {
            [""] = c == "skin" ? 2.5f : 1.2f, ["stub"] = cold ? 1.5f : 0.6f, ["whip"] = hot ? 1.4f : 0.6f, ["tufted"] = fur ? 1.2f : 0.4f,
            ["thick"] = scales ? 3f : 0.3f, ["bushy"] = fur && !hot ? 2.5f : 0f, ["fan"] = feathers ? 2.5f : 0f,
        });
        f.TailLength = f.Tail switch { "" => 0f, "stub" => rng.Range(0.12f, 0.2f), "fan" => rng.Range(0.3f, 0.45f), "thick" => rng.Range(0.6f, 0.95f), _ => rng.Range(0.55f, 0.9f) };
        f.Back = Socket("back", new() { [""] = 5f, ["spines"] = scales || fur ? 1f : 0.2f, ["plates"] = chitin ? 4f : scales ? 0.8f : 0.1f, ["ridge"] = scales ? 1.5f : 0.3f });
        f.Claws = budget >= 1f && rng.Chance(fur || scales || chitin ? 0.45f * Lean("claws") : 0.1f);
        if (f.Claws) f.Spent++;
        f.Glow = (f.DarkVision > 0.6f || lean.ContainsKey("glow")) && rng.Chance(0.35f * Lean("glow"));
        f.MouthSize = rng.Range(0.85f, 1.2f);

        // At least two things that change the outline (docs/alien-peoples.md 1.6): add a tail, then a crown, if short.
        Fit(f);
        // (After the doorway fit, which can take height away: only parts count here.)
        if (Outline(f) < 2 && f.Tail is "" or "stub") { f.Tail = scales ? "thick" : feathers ? "fan" : fur && !hot ? "bushy" : "whip"; f.TailLength = rng.Range(0.55f, 0.85f); f.Spent += 2; }
        if (Outline(f) < 2 && f.Crown is "" or "knobs") { f.Crown = chitin ? "antennae" : feathers ? "plume" : scales ? "crest" : "pair"; f.Spent += 2; Fit(f); }
        if (Outline(f) < 2 && f.Back == "") { f.Back = chitin ? "plates" : "spines"; f.Spent += 2; }
        return f;
    }

    /// <summary>A tall folk is scaled to the doorway, not cut off at it: its tallest (crown and all) stand just under the cap.</summary>
    static void Fit(Folk f)
    {
        float tallest = (1.695f + 0.065f * f.Dimorphism + 0.15f) * (1f + 0.066f * f.HeadScale * f.CrownRise);
        f.HeightScale = MathF.Min(f.HeightScale, Figure.HeightCap / tallest);
    }

    /// <summary>The Vharn: the same rolls with a strong lean to carapace, mandibles, antennae and chitin, dark with glowing markings.</summary>
    public static Folk RollVharn(Rng rng, WorldConditions w)
    {
        // They rode out the collapse on their own worlds: their bodies keep that climate, not this one's.
        var home = new WorldConditions { Seed = w.Seed, Alienness = 0.75f, TempC = 32f, Oxygen = w.Oxygen };
        var lean = new Dictionary<string, float>
        {
            ["chitin"] = 60f, ["mandibles"] = 40f, ["antennae"] = 40f, ["compound"] = 12f, ["band"] = 4f, ["plates"] = 8f, ["claws"] = 2.2f, ["glow"] = 3f, ["dark"] = 1f,
            [""] = 0.6f,
        };
        Folk f = Roll(rng, home, "vharn", lean, "chitin");
        // Thin and long in the arm, a little hunched: the insect line, on the human shape so their gestures read.
        f.ShoulderScale = MathF.Min(f.ShoulderScale, rng.Range(0.88f, 0.96f));
        f.ArmScale = MathF.Max(f.ArmScale, rng.Range(1.08f, 1.18f));
        f.StoopBias = MathF.Max(f.StoopBias, rng.Range(0.18f, 0.3f));
        f.HeightScale = rng.Range(1.02f, 1.1f);
        f.Glow = true;
        f.EyeShine = new Color(1f, 0.24f, 0.43f);
        f.Mark = new Color(0.62f, 0.12f, 0.2f);
        Fit(f);
        return f;
    }

    /// <summary>How many things change the folk's outline at 30 m.</summary>
    public static int Outline(Folk f)
    {
        int n = 0;
        if (f.Crown is not ("" or "knobs")) n++;
        if (f.Tail is not ("" or "stub")) n++;
        if (f.Ears is "tall" or "fan") n++;
        if (f.Back != "") n++;
        if (MathF.Abs(f.HeightScale - 1f) >= 0.08f) n++;
        if (MathF.Abs(f.ShoulderScale - 1f) >= 0.1f) n++;
        if (f.NeckScale >= 1.5f || f.NeckScale <= 0.6f) n++;
        if (f.StoopBias >= 0.25f) n++;
        return n;
    }

    /// <summary>How many of the five silhouette codes two folk share.</summary>
    public static int Shared(Folk a, Folk b)
    {
        string[] x = a.Silhouette(), y = b.Silhouette();
        int n = 0;
        for (int i = 0; i < x.Length; i++) if (x[i] == y[i]) n++;
        return n;
    }

    static string Pick(Rng rng, Dictionary<string, float> options, Func<string, float> lean)
    {
        float total = 0f;
        foreach (var (k, v) in options) total += v * lean(k);
        float r = rng.Float() * total;
        foreach (var (k, v) in options)
        {
            r -= v * lean(k);
            if (r <= 0f) return k;
        }
        foreach (var (k, _) in options) return k;
        return "";
    }

    /// <summary>A narrower range inside a family's: one folk is one shade of it, not all.</summary>
    static float[] Narrow(Rng rng, float[] range, float keep)
    {
        float span = (range[1] - range[0]) * keep, at = rng.Range(range[0], range[1] - span);
        return new[] { at, at + span };
    }

    static float Wrap(float h) => h - MathF.Floor(h);
}
