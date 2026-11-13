using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Stars;
using Godot;
using Planets.Gen;
using PlanetSurface.Skies;
using PlanetSurface.World;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;

namespace AssetViewer.Providers.Nature;

/// <summary>
/// A sky as seen from the ground of a world, with the game's own sky rig: its star, moons, giants and rings
/// where the system has them, the galaxy behind, the clouds, and the light on the land. The camera opens
/// looking at what the asset is about.
/// </summary>
public partial class SkyView : SurfaceView
{
    public sealed record Scene(string Focus, string Time, string Cloud, float? Depth = null, bool Eclipse = false);

    readonly Scene _scene;
    float _speed = 1f;
    int _orient = 6;
    double _eclipseDays;
    double _eclipseCover;

    public SkyView(PlanetWorld world, Scene scene) : base(world, null, 0f)
    {
        _scene = scene;
        OpenTime = TimeValue(scene.Time);
        Stage.StartInside = true;
        Stage.MinDistanceShare = 0.01f;
        HalfSize = 10f;
        Height = 12f;
        SettleSeconds = 3.0;
    }

    public override Aabb? FrameBounds
    {
        get
        {
            float camZ = Spot.Z + new Vector3(20f, 12f, 20f).Length() * 0.5f * 0.25f;
            return new Aabb(new Vector3(Spot.X - 10f, World.GroundHeight(Spot.X, camZ), Spot.Z - 10f), new Vector3(20f, 12f, 20f));
        }
    }

    public override IReadOnlyList<string> Animations => new[] { "Still", "Day passing", "Hours passing" };

    public override void PlayAnimation(string name)
    {
        base.PlayAnimation(name);
        Sky.TimeScale = name switch { "Day passing" => 90f, "Hours passing" => 600f, _ => 1f };
        Sky.Paused = name == "Still";
    }

    public override void SetSpeed(float scale) { _speed = scale; }

    protected override void AfterWorld()
    {
        if (_scene.Eclipse)
        {
            // The deepest eclipse with the sun well up, within the next few weeks of the system's days.
            (_eclipseDays, _eclipseCover) = Sky.NextEclipse(40.0, 0.3);
            if (_eclipseCover > 0.0) Sky.Days = _eclipseDays - 0.0009;
        }
        else if (_scene.Focus is "rings" or "moon" or "giant")
        {
            double? t = TimeWith(b => _scene.Focus switch
            {
                "rings" => b.Body.Rings,
                "moon" => b.Body.Kind == OrbiterKind.Moon,
                _ => b.Body.Kind == OrbiterKind.GasGiant,
            });
            if (t is double day) Sky.TimeOfDay = day;
        }
    }

    /// <summary>A time of day when a kind of body stands well up in this sky, searched over the day.</summary>
    double? TimeWith(Func<(Orbiter Body, Vector3 Direction, float Angular), bool> kind)
    {
        double keep = Sky.TimeOfDay;
        double? found = null;
        for (double t = 0.0; t < 1.0 && found == null; t += 0.02)
        {
            Sky.TimeOfDay = t;
            Sky.Advance(0.0, 0f, Air(), Vector2.Zero);
            foreach (var d in Sky.Drawn)
                if (kind(d) && d.Direction.Y > 0.25f) { found = t; break; }
        }
        Sky.TimeOfDay = found ?? keep;
        Sky.Advance(0.0, 0f, Air(), Vector2.Zero);
        return found;
    }

    protected override AtmosphereState Air()
    {
        Conditions c = Atmosphere.Clear();
        switch (_scene.Cloud)
        {
            case "Scattered": c[Param.CloudCover] = 0.5f; c[Param.CloudDarkness] = 0.08f; break;
            case "Overcast": c[Param.CloudCover] = 0.95f; c[Param.CloudDarkness] = 0.35f; break;
            case "Clear": c[Param.CloudCover] = 0.02f; break;
        }
        return Atmosphere.Compute(World.Sky, c, Sky.ToSun.Y);
    }

    public override void _Process(double delta)
    {
        base._Process(delta * _speed);
        if (_orient > 0 && --_orient == 0) Orient();
    }

    /// <summary>Turns the free camera to what the sky is about: the sun, the ringed planet, a moon.</summary>
    void Orient()
    {
        if (GetViewport()?.GetCamera3D() is not ViewCamera cam || !cam.IsFree) return;
        Vector3 d = Vector3.Zero;
        switch (_scene.Focus)
        {
            case "rings": d = Pick(b => b.Body.Rings); break;
            case "moon": d = Pick(b => b.Body.Kind == OrbiterKind.Moon); break;
            case "giant": d = Pick(b => b.Body.Kind == OrbiterKind.GasGiant); break;
            case "hole":
                try
                {
                    var hole = Sky.Stars.ToHole;
                    var inStars = new Vector3(hole.X, hole.Y, hole.Z);
                    d = Sky.SkyToStars.Inverse() * (Sky.Stars.Field.Inverse() * inStars);
                }
                catch (Exception) { }
                break;
        }
        if (d == Vector3.Zero && Sky.ToSun.Y > 0.03f) d = Sky.ToSun;
        if (d == Vector3.Zero)
        {
            // The sun is down: the brightest body above the horizon, else the northern sky, high.
            d = Pick(_ => true);
            if (d == Vector3.Zero) d = new Vector3(0f, 0.55f, -1f);
        }
        d = d.Normalized();
        float yaw = Mathf.Atan2(-d.X, -d.Z), pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(d.Y, -1f, 1f)), 0.05f, 1.2f);
        cam.SetFreeAngles(yaw, pitch);
    }

    Vector3 Pick(Func<(Orbiter Body, Vector3 Direction, float Angular), bool> kind)
    {
        Vector3 best = Vector3.Zero;
        float size = 0f;
        foreach (var d in Sky.Drawn)
            if (kind(d) && d.Direction.Y > 0.05f && d.Angular > size) { size = d.Angular; best = d.Direction; }
        return best;
    }

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        SkySystem s = World.Sky;
        yield return ("World", World.Spec.Archetype + " " + Archetypes.Get(World.Spec.Archetype, World.Spec.Variant).VariantName);
        yield return ("Star", s.Star.ToString());
        yield return ("Air", s.Air.ToString());
        yield return ("Day", $"{s.DayMinutes:F0} min");
        int moons = s.Bodies.Count(b => b.Kind == BodyKind.Moon), giants = s.Bodies.Count(b => b.Kind == BodyKind.GasGiant);
        yield return ("Moons", moons.ToString());
        yield return ("Giants", giants.ToString());
        yield return ("Rings", s.HasRingedBody ? "yes" : "no");
        yield return ("Home star depth", World.Depth >= 0f ? $"{World.Depth:F2} (0 rim, 1 core)" : "rim");
        if (_scene.Eclipse) yield return ("Eclipse", _eclipseCover > 0.0 ? $"{_eclipseCover * 100f:F0}% of the sun covered at its deepest" : "none in the next weeks here");
        yield return ("Look", "free camera, look up");
    }
}
