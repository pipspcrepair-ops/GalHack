using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Stars;
using Godot;
using Planets.Gen;
using PlanetSurface.Skies;
using PlanetSurface.World;

namespace PlanetSurface;

/// <summary>
/// barren:regolith, barren:icy, barren:metal: a barren body of a fresh start world's system (owner, 22 Nov: an airless
/// moon is a grey cratered moonscape), its world made as the game makes a moon's (<see cref="PlanetWorld.ShareSystem"/>:
/// the start world's galaxy, star and system, seen from the body), in daylight with the sun low enough to model the
/// ground. Frames: "field", across a crater field from a person's height, the sun to the side; "up", at the body it
/// circles, when it is a moon and that stands in its sky; "rim", across its biggest fresh crater from the rim, the sun
/// behind. A regolith body is a small moon of the start world. Fresh seeds every run, printed.
/// </summary>
public static class BarrenViews
{
    public static Screenshot.ViewSpec Get(string name)
    {
        int variant = name.Split(':')[1] switch { "icy" => Archetypes.Icy, "metal" => Archetypes.Metal, _ => Archetypes.Regolith };
        var (home, body) = Find(variant);
        GD.Print($"barren view: {body.Name} of a fresh start world (seed {home.Seed}): {body.Spec}, a {body.Kind} of {body.Parent?.Name}, zone {body.Zone}");
        double time = 0.4;
        return new Screenshot.ViewSpec(body.Spec, _ => default, time, TimeFor: _ => time,
            World: () =>
            {
                PlanetWorld of = PlanetWorld.Build(home, Main.Catalog);
                Orbiter mine = of.StarSystem.Bodies.First(b => b.Seed == body.Seed);
                PlanetWorld w = PlanetWorld.Build(mine.Spec, null);
                w.ShareSystem(of, mine);
                return w;
            },
            Series: m => Shots(m, body, out time));
    }

    /// <summary>A fresh start world whose system holds a barren body of the variant: a small moon of the start world for regolith, else any, a moon first.</summary>
    static (PlanetSpec Home, Orbiter Body) Find(int variant)
    {
        var rng = new Random();
        for (int tries = 0; tries < 400; tries++)
        {
            PlanetSpec home = PlanetSpec.Roll((ulong)rng.NextInt64(1, long.MaxValue), Archetype.Temperate, 0, PlanetSpec.StartWidthKm);
            // The system the start world's own world will build: its star found in the galaxy as it finds it.
            SkySystem sky = SkySystem.For(home);
            // Out of the nebulae: inside one its glow washes even a vacuum's black sky (the system's truth, not this rule's).
            if (sky.Nebula) continue;
            StarKind kind = SkyRig.KindOf(sky.Star);
            Galaxy galaxy = Galaxy.Make(home.Seed);
            ulong seed = galaxy.SystemSeed(galaxy.Home(kind, Galaxy.HomeKey(home.Seed, kind, sky.Nebula), sky.Nebula));
            StarSystem system = StarSystem.For(home, sky, home.WidthMeters, seed);
            var barren = system.Bodies.Where(b => b != system.Home && b.Spec is { Archetype: Archetype.Barren } s && s.Variant == variant).ToList();
            Orbiter pick = variant == Archetypes.Regolith
                ? barren.Where(b => b.Parent == system.Home).OrderBy(b => b.Spec.WidthKm).FirstOrDefault()
                : barren.OrderBy(b => b.Kind == OrbiterKind.Moon ? 0 : 1).FirstOrDefault();
            if (pick != null) return (home, pick);
        }
        throw new InvalidOperationException($"no barren body of variant {variant} in 400 fresh systems");
    }

    static List<Screenshot.Shot> Shots(Main m, Orbiter body, out double time)
    {
        PlanetWorld w = m.World;
        SkyRig sky = m.Sky;
        // Where it stands on its world and when: the body it circles well up in its sky (a moon keeps one face to it,
        // so that is a place, not a time), and the sun low but clear of the horizon, so the ground's relief shows.
        double bestScore = double.MinValue, lon = StarSystem.NoonLongitude, colat = Math.PI * 0.5;
        time = 0.4;
        for (int i = 0; i < 24; i++)
            for (int j = 0; j < 5; j++)
            {
                double lo = i * Math.Tau / 24, co = Math.PI * (0.3 + 0.1 * j);
                sky.Stand(lo, co, 0);
                for (double t = 0; t < 1; t += 0.01)
                {
                    sky.TimeOfDay = t;
                    sky.Track();
                    float sun = Mathf.RadToDeg(MathF.Asin(sky.ToSun.Y));
                    var (_, toParent, _) = sky.Drawn.FirstOrDefault(d => d.Body.Seed == body.Parent.Seed);
                    float over = toParent == Vector3.Zero ? -90f : Mathf.RadToDeg(MathF.Asin(toParent.Y));
                    // And its face to us lit: the sun well round from it.
                    double score = -Math.Abs(sun - 24f) - (body.Kind == OrbiterKind.Moon ? Math.Abs(over - 40f) + 20f * Math.Max(0f, sky.ToSun.Dot(toParent)) : 0f);
                    if (score > bestScore) (bestScore, lon, colat, time) = (score, lo, co, t);
                }
            }
        sky.Stand(lon, colat, 0);
        sky.TimeOfDay = time;
        sky.Track();
        Vector3 toSun = sky.ToSun;
        var sunFlat = new Vector2(toSun.X, toSun.Z).Normalized();
        double z = Math.Clamp((colat / Math.PI), 0.05, 0.95) * w.Height; // the latitude the sky was stood at, so the map's row there
        TerrainModel model = w.Map.Model;
        bool OnPlain((double X, double Y, float Radius, float Fresh) c) => model.Mix.Count == 1 || (Landform)w.Map.Landform[w.Map.CellAt(c.X, c.Y)] is Landform.Plains or Landform.IcePlain;
        double Near((double X, double Y, float Radius, float Fresh) c) => Math.Abs(Mathx.WrapDelta(c.Y - z, w.Height));
        var field = model.CratersOver(70f).Where(c => c.Radius < 200f && c.Fresh > 0.75f && OnPlain(c)).OrderBy(Near).FirstOrDefault();
        if (field.Radius <= 0f) field = model.CratersOver(50f).Where(c => c.Radius < 200f).OrderBy(Near).First();
        var big = model.CratersOver(300f).OrderByDescending(c => c.Fresh > 0.85f).ThenByDescending(c => c.Fresh * c.Radius).FirstOrDefault();
        float Ground(double x, double zz) => w.GroundHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(zz, w.Height));
        Screenshot.Shot Toward(string name, Vector3 from, Vector3 dir) =>
            new(name, from, Mathf.RadToDeg(MathF.Atan2(dir.X, -dir.Z)), Mathf.RadToDeg(MathF.Asin(Math.Clamp(dir.Normalized().Y, -1f, 1f))));
        var shots = new List<Screenshot.Shot>();

        // Across a crater field, the sun to one side: stood just off a fresh crater's rim, looking over its bowl and on.
        var side = new Vector2(-sunFlat.Y, sunFlat.X);
        var eye = new Vector3((float)(field.X + side.X * field.Radius * 1.4f), 0f, (float)(field.Y + side.Y * field.Radius * 1.4f));
        eye.Y = Ground(eye.X, eye.Z) + 1.8f;
        var at = new Vector3((float)field.X, Ground(field.X, field.Y), (float)field.Y);
        shots.Add(Toward("field", eye, new Vector3(at.X - eye.X, 0f, at.Z - eye.Z).Normalized() with { Y = -0.1f }));
        GD.Print($"barren view field: a {field.Radius:F0} m crater at {field.X:F0},{field.Y:F0}, the eye at {eye}; sun {Mathf.RadToDeg(MathF.Asin(toSun.Y)):F0}° up at {time:F3}");

        // Up at the body it circles, from the same place.
        if (body.Kind == OrbiterKind.Moon && sky.Drawn.FirstOrDefault(d => d.Body.Seed == body.Parent.Seed) is { Body: not null } parent && parent.Direction.Y > 0.1f)
        {
            Vector3 d = parent.Direction;
            float up = MathF.Asin(d.Y);
            // Low enough that the ground shows under it.
            Vector3 aim = new Vector3(d.X, 0f, d.Z).Normalized() * MathF.Cos(up * 0.7f) + Vector3.Up * MathF.Sin(up * 0.7f);
            shots.Add(Toward("up", eye, aim));
            GD.Print($"barren view up: {parent.Body.Name} {Mathf.RadToDeg(up):F0}° up, {Mathf.RadToDeg(parent.Angular) * 2f:F1}° across");
        }

        // Across its biggest fresh crater from the rim, the sun at the back.
        if (big.Radius > 0f)
        {
            // On its crest (the highest ground out from its middle toward the sun), looking down at the far wall.
            Vector3 Out(float t) => new((float)(big.X + sunFlat.X * big.Radius * t), 0f, (float)(big.Y + sunFlat.Y * big.Radius * t));
            float crest = Enumerable.Range(0, 31).Select(k => 0.85f + 0.01f * k).MaxBy(t => Ground(Out(t).X, Out(t).Z));
            Vector3 rim = Out(crest - 0.03f);
            rim.Y = Ground(rim.X, rim.Z) + 2.5f;
            var middle = new Vector3((float)big.X, Ground(big.X, big.Y), (float)big.Y);
            shots.Add(Toward("rim", rim, (middle - rim).Normalized()));
            GD.Print($"barren view rim: a {big.Radius:F0} m crater at {big.X:F0},{big.Y:F0}, its floor {rim.Y - middle.Y:F0} m under the rim");
        }
        return shots;
    }
}
