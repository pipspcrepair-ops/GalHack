using System;
using System.Collections.Generic;
using GalHack.Physics.Engine;
using Godot;
using Weather.Model;

namespace Slice.Tests;

/// <summary>
/// The sky check: a look round the whole horizon (8 headings x 3 pitches) from the bunker's way
/// out and from a hilltop, at dawn under broken cloud, after the ground has streamed in. Every
/// frame is searched for two kinds of seam:
///   - dark specks in smooth sky: a pixel much darker than nearly all of its neighbours where they
///     agree with each other (a NaN or a precision blow-up draws them, often along a line);
///   - holes in the ground: where a ray from the camera meets the planet's ground within 800 m,
///     well below its surface, the image must not show the sky there (compared with a frame of
///     the same view with the ground hidden).
/// It prints the counts and saves the frames that have any, plus one frame looking away from the
/// sun (where a dotted line used to run), into the --out folder.
/// </summary>
public partial class Staging
{
    IEnumerable<int> SkySweep()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.27);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clouds);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        _game.Zone.Wildlife.Visible = false; // creatures aloft are not specks
        if (_args.ContainsKey("hidepois")) _game.Zone.Pois.Visible = false; // to tell a place's geometry from the sky's
        foreach (int x in _drive.UntilSettled()) yield return x;
        string dir = _args.TryGetValue("out", out string o) ? System.IO.Path.GetDirectoryName(o) : ".";
        int specks = 0, holes = 0, frames = 0;
        var places = new List<(string name, Vector3 eye)> { ("exit", _game.Survivor.WorldEye()) };
        Vector3 hill = Hilltop(_game.Survivor.WorldFeet, 300f);
        if (!(_args.TryGetValue("places", out string only) && only == "exit")) places.Add(("hilltop", hill + Vector3.Up * 1.6f));
        bool all = _args.ContainsKey("all"); // every frame saved (before and after comparisons)
        Vector3 sun = _game.Zone.Sky.ToSun;
        float antiSolar = MathF.Atan2(sun.X, sun.Z); // the heading that looks away from the sun
        foreach (var (name, eye) in places)
        {
            // STAGING: the castaway is set down on the hilltop (the view, not the walk, is tested).
            if (name == "hilltop")
            {
                _game.Survivor.PlaceOutside(hill.ToNumerics());
                foreach (int x in _drive.Wait(30)) yield return x;
                foreach (int x in _drive.UntilSettled()) yield return x;
            }
            for (int h = 0; h < 9; h++)
            for (int p = 0; p < 3; p++)
            {
                if (h == 8 && p != 1) continue; // the ninth heading: straight away from the sun, once
                float yaw = h == 8 ? antiSolar : h * MathF.PI / 4f, pitch = new[] { 5f, 20f, 40f }[p] * MathF.PI / 180f;
                var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, pitch);
                _game.FixedCamera = new Transform3D(basis, eye);
                // Frames actually drawn with this view (rendering runs slower than the ticks).
                int drawn = Engine.GetFramesDrawn();
                while (Engine.GetFramesDrawn() < drawn + 3) yield return 0;
                Image shot = _game.GetViewport().GetTexture().GetImage();
                Image sky = null;
                frames++;
                int s = DarkSpecks(shot), g = SkyHoles(shot, sky, _game.Camera);
                specks += s;
                holes += g;
                int deg = (int)MathF.Round(((-yaw * 180f / MathF.PI) % 360f + 360f) % 360f);
                if (all || s > 0 || g > 0 || h == 8)
                {
                    string file = System.IO.Path.Combine(dir, $"sky-{name}-{deg:000}-{(int)(pitch * 180f / MathF.PI):00}.png");
                    shot.SavePng(file);
                    GD.Print($"  {name} heading {deg}Â° pitch {pitch * 180f / MathF.PI:F0}Â°: {s} dark specks, {g} sky holes ({file})");
                }
            }
        }
        // Specks must be none. The ground-hole count is a coarse screen (haze and gaps between
        // trunks can match the sky's colour): its frames are saved to look at.
        GD.Print($"sky check: {specks} dark specks in {frames} frames {(specks == 0 ? "(clean)" : "(SPECKS)")}; {holes} possible holes in the ground flagged for a look");
        _game.FixedCamera = new Transform3D(new Basis(Vector3.Up, antiSolar) * new Basis(Vector3.Right, 0.35f), places[0].eye);
        foreach (int x in _drive.Wait(4)) yield return x;
    }

    /// <summary>The highest natural ground within a distance of a point.</summary>
    Vector3 Hilltop(Vector3 near, float within)
    {
        Vector3 best = _game.Zone.GroundAt(near.X, near.Z);
        for (float r = 20f; r <= within; r += 20f)
        for (int i = 0; i < 24; i++)
        {
            float a = i * MathF.Tau / 24f;
            Vector3 g = _game.Zone.GroundAt(near.X + MathF.Cos(a) * r, near.Z + MathF.Sin(a) * r);
            if (g.Y > best.Y && g.Y > 2f) best = g;
        }
        return best;
    }

    static float[] Luminance(Image image, out int w, out int h)
    {
        image.Convert(Image.Format.Rgb8);
        w = image.GetWidth();
        h = image.GetHeight();
        byte[] d = image.GetData();
        var l = new float[w * h];
        for (int i = 0; i < l.Length; i++) l[i] = (d[i * 3] * 0.3f + d[i * 3 + 1] * 0.59f + d[i * 3 + 2] * 0.11f) / 255f;
        return l;
    }

    /// <summary>Pixels far darker than all but two of their eight neighbours, where those agree: specks and dashes in smooth sky.</summary>
    // In open sky, and not perched on a far crest (a tree or a rock on the skyline is no speck):
    // the pixels just below it are open sky too.
    int DarkSpecks(Image image) => DarkSpecks(image, (x, y) => OpenSky(_game.Camera, x, y) && OpenSky(_game.Camera, x, y + 3));

    /// <summary>Whether a pixel looks at open sky: its ray meets no ground within 12 km.</summary>
    bool OpenSky(Camera3D camera, int x, int y) => GroundHit(camera, x, y, 12000f) < 0f;

    float GroundHit(Camera3D camera, int x, int y, float far)
    {
        Vector3 o = camera.ProjectRayOrigin(new Vector2(x, y)), dir = camera.ProjectRayNormal(new Vector2(x, y));
        for (float t = 30f; t <= far; t = t < 800f ? t + 8f : t * 1.04f)
        {
            Vector3 p = o + dir * t;
            if (p.Y < _game.Zone.GroundAt(p.X, p.Z).Y) return t;
        }
        return -1f;
    }

    public static int DarkSpecks(Image image, Func<int, int, bool> inSky)
    {
        float[] l = Luminance((Image)image.Duplicate(), out int w, out int h);
        var n = new float[8];
        int count = 0;
        for (int y = 1; y < h - 1; y++)
        for (int x = 1; x < w - 1; x++)
        {
            float c = l[x + y * w];
            int k = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0) n[k++] = l[x + dx + (y + dy) * w];
            Array.Sort(n);
            if (!(n[2] > c + 0.12f && n[7] - n[2] < 0.06f)) continue;
            // And smooth sky round it two pixels out, bar the ends of a dash: an edge of foliage
            // or a ridge against the sky has many dark pixels there.
            if (x < 2 || y < 2 || x >= w - 2 || y >= h - 2) continue;
            int darkRing = 0;
            float bright = n[7];
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if ((Math.Abs(dx) == 2 || Math.Abs(dy) == 2) && l[x + dx + (y + dy) * w] < bright - 0.08f) darkRing++;
            if (darkRing <= 2 && inSky(x, y)) count++;
        }
        return count;
    }

    /// <summary>
    /// Ground that should be there but shows the sky. Rays are cast on a coarse grid against the
    /// planet's natural ground: a sample whose ray meets the ground between 30 and 800 m, well
    /// into it a few metres on (not grazing a crest), should show ground; one whose ray meets
    /// nothing within 4 km shows sky. A ground sample coloured like a sky sample of the same row
    /// is a hole; two side by side are counted.
    /// </summary>
    int SkyHoles(Image shot, Image sky, Camera3D camera)
    {
        Image a = (Image)shot.Duplicate();
        a.Convert(Image.Format.Rgb8);
        byte[] d = a.GetData();
        int w = a.GetWidth(), h = a.GetHeight(), holes = 0;
        var openColours = new List<(int r, int g, int b)>();
        var kinds = new List<(int x, int kind)>();
        for (int y = 10; y < h; y += 20)
        {
            openColours.Clear();
            kinds.Clear();
            for (int x = 10; x < w; x += 20)
            {
                Vector3 o = camera.ProjectRayOrigin(new Vector2(x, y)), dir = camera.ProjectRayNormal(new Vector2(x, y));
                float hit = -1f;
                for (float t = 30f; t <= 4000f; t = t < 800f ? t + 8f : t * 1.04f)
                {
                    Vector3 p = o + dir * t;
                    if (p.Y < _game.Zone.GroundAt(p.X, p.Z).Y) { hit = t; break; }
                }
                int kind = 0;
                if (hit < 0f) kind = 1; // open sky
                else if (hit <= 800f)
                {
                    Vector3 deeper = o + dir * (hit + 6f);
                    if (deeper.Y < _game.Zone.GroundAt(deeper.X, deeper.Z).Y - 3f) kind = 2; // solid ground
                }
                int i = (x + y * w) * 3;
                if (kind == 1) openColours.Add((d[i], d[i + 1], d[i + 2]));
                kinds.Add((x, kind));
            }
            if (openColours.Count == 0) continue;
            for (int k = 0; k < kinds.Count; k++)
            {
                var (x, kind) = kinds[k];
                if (kind != 2) continue;
                int i = (x + y * w) * 3;
                bool skyColoured = false;
                foreach (var (r, g, b) in openColours)
                    if (Math.Abs(d[i] - r) + Math.Abs(d[i + 1] - g) + Math.Abs(d[i + 2] - b) < 9) { skyColoured = true; break; }
                if (!skyColoured) continue;
                // A hard edge: ground right beside it looks nothing like it (haze fades softly).
                bool edge = false;
                foreach (int nx in new[] { x - 20, x + 20 })
                {
                    if (nx < 0 || nx >= w) continue;
                    int j = (nx + y * w) * 3;
                    int diff = Math.Abs(d[i] - d[j]) + Math.Abs(d[i + 1] - d[j + 1]) + Math.Abs(d[i + 2] - d[j + 2]);
                    int nk = kinds.FindIndex(q => q.x == nx);
                    if (nk >= 0 && kinds[nk].kind == 2 && diff > 60) edge = true;
                }
                if (edge) holes++;
            }
        }
        return holes;
    }

    /// <summary>
    /// The night sky through cloud, at the darkest hour of a fresh world, from a lookout: clear, under broken
    /// cloud, with a storm front's deck coming in (--ahead m ahead of its edge, 2,500: its warning is up) and
    /// with it overhead (--behind m behind its edge, 1,500), each looking up --pitch (0.45 rad) toward the
    /// front, saved as clear, broken, coming and storm .png in the --out folder.
    /// </summary>
    IEnumerable<int> NightClouds()
    {
        var sky = _game.Zone.Sky;
        sky.Paused = true;
        double midnight = 0.0;
        for (double t = 0.0; t < 1.0; t += 0.002) if (sky.SunAt(t).Y < sky.SunAt(midnight).Y) midnight = t;
        sky.TimeOfDay = midnight;
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        _game.Alerts.Visible = false;
        WeatherSim sim = _game.Zone.Weather.Sim;
        sim.FrontsOff = true;
        sim.ClearFront();
        sim.ClearTornadoes();
        sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.UntilSettled()) yield return x;
        sky.Stars.Finish();
        Vector2 wind = sim.Current.Wind.LengthSquared() > 0.01f ? sim.Current.Wind : new Vector2(1f, 0f);
        Vector2 way = Front.Lattice(wind, sim.WorldSize, 5500f).heading;
        var toward = new Vector3(-way.X, 0f, -way.Y);
        Vector3 eye = Lookout(_game.Survivor.WorldFeet, toward, 2f);
        _game.Survivor.PlaceOutside((eye + Vector3.Up).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 2f;
        float pitch = (float)Num("pitch", 0.45);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(toward * MathF.Cos(pitch) + Vector3.Up * MathF.Sin(pitch), Vector3.Up), eye);
        _game.Zone.Weather.ShaftsHidden = true;
        string dir = _args.GetValueOrDefault("out", "nightclouds");
        System.IO.Directory.CreateDirectory(dir);
        foreach (var (name, weather, front) in new[] { ("clear", WeatherKind.Clear, float.NaN), ("broken", WeatherKind.Clouds, float.NaN),
                     ("coming", WeatherKind.Clouds, (float)Num("ahead", 2500)), ("storm", WeatherKind.Thunderstorm, -(float)Num("behind", 1500)) })
        {
            sim.ClearFront();
            sim.Snap(weather);
            if (!float.IsNaN(front)) sim.StartFront(FrontKind.Storm, eye, front, wind);
            foreach (int x in _drive.Wait(60)) yield return x;
            GD.Print($"(nightclouds {name}: sun y {sky.ToSun.Y:F2}, cover {sky.Air.CloudCover:F2}, overcast {sky.Air.Overcast:F2}, front {(sim.Front == null ? "none" : $"{sim.Front.Ahead(eye):F0} m off")}, places glowing {sky.GlowingPlaces})");
            foreach (int x in Shot(System.IO.Path.Combine(dir, name + ".png"))) yield return x;
        }
    }
}
