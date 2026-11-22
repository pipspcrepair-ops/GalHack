using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GalHack.Physics.Engine;
using PoiGenerator.Gen;
using WeatherKind = Weather.Model.WeatherKind;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Tests;

/// <summary>
/// Blocks open to the sky are drawn like the land round them (--view=exposed). Every under-town block face with open air
/// over the drawn ground beside it is found; the spots with most of them are framed near (14 m) and far (90 m), in clear
/// daylight and again in fog, and each frame is measured: drawn with those blocks and again without them (their
/// shadows off for both, so only the blocks differ), the pixels that change are the blocks and the same pixels without them
/// the land behind. The run fails where exposed blocks draw near-black in daylight, or stand out of the haze the land
/// takes. Then a sewer junction at noon with no lamp, which must stay dark.
/// </summary>
public sealed partial class Staging
{
    /// <summary>
    /// Exposed blocks under this share of the land's brightness behind them read as black: a face lit by the sky alone, in the
    /// shade of a wall with the land behind it in full sun, reads about a tenth; a face that takes no sky reads nothing.
    /// </summary>
    const float NearBlack = 0.05f;
    /// <summary>In haze, exposed blocks' colour may differ from the land's behind them by this share of its brightness.</summary>
    const float HazeOff = 0.35f;

    IEnumerable<int> ExposedView()
    {
        var world = _game.Surface.Planet;
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        _game.Zone.Sky.Paused = true;
        GD.Print($"world: seed {_game.World.Seed}, {_game.World}");
        if (world.StartTown?.Under?.Built is not { } built) { GD.Print("(exposed: no underground on this world)"); yield break; }

        // Faces toward open air over the ground beside them: what the sky and the eye reach.
        var faces = new List<(PInt3 Cell, Dir Dir, bool Dark)>();
        foreach (PInt3 c in built.Blocks.Keys)
        {
            if (built.Open.Contains(c)) continue;
            bool dark = !Dirs.All.Any(d => built.Daylight.Contains(c + d.Vec()));
            foreach (Dir d in Dirs.All)
            {
                PInt3 n = c + d.Vec();
                if (built.Blocks.ContainsKey(n) || built.Open.Contains(n)) continue;
                if (n.Y + 0.5f > world.GroundHeight(Planets.Gen.Mathx.WrapX(n.X + 0.5, world.Width), Planets.Gen.Mathx.Wrap(n.Z + 0.5, world.Height))) faces.Add((c, d, dark));
            }
        }
        GD.Print($"(exposed: {faces.Count} block faces open to the sky, {faces.Count(f => f.Dark)} of them on blocks with no daylight beside them)");
        // Which spaces they line: the ways in are meant to show; anything else poking out of the ground is a fault of its own.
        string Lines(PInt3 c) => built.Spaces.Where(s => c.X >= s.Min.X - 1 && c.X <= s.Max.X + 1 && c.Y >= s.Min.Y - 1 && c.Y <= s.Max.Y + 1 && c.Z >= s.Min.Z - 1 && c.Z <= s.Max.Z + 1)
            .Select(s => $"{s.Network} {s.Kind}").DefaultIfEmpty("none").First();
        foreach (var k in faces.GroupBy(f => Lines(f.Cell)).OrderByDescending(k => k.Count()))
            GD.Print($"    {k.Key}: {k.Count()} faces ({k.Count(f => f.Dark)} dark), e.g. {k.First().Cell}");

        // The spots with most of them, 16 m apart at least.
        var spots = faces.GroupBy(f => (X: (int)Math.Floor(f.Cell.X / 16.0), Z: (int)Math.Floor(f.Cell.Z / 16.0)))
            .OrderByDescending(g => g.Count(f => f.Dark) * 4 + g.Count()).Take((int)Num("spots", 3)).ToList();
        int measured = 0, shot = 0;
        foreach (var g in spots)
        {
            shot++;
            Vector3 sum = Vector3.Zero, out_ = Vector3.Zero;
            foreach (var f in g)
            {
                PInt3 v = f.Dir.Vec();
                sum += new Vector3(f.Cell.X + 0.5f + v.X * 0.5f, f.Cell.Y + 0.5f + v.Y * 0.5f, f.Cell.Z + 0.5f + v.Z * 0.5f);
                out_ += new Vector3(v.X, 0f, v.Z);
            }
            Vector3 centre = world.NearestCopy(sum / g.Count(), _game.Survivor.WorldFeet);
            Vector3 dir = out_.LengthSquared() > 1f ? out_.Normalized() : new Vector3(1f, 0f, 0.4f).Normalized();
            GD.Print($"  spot {shot}: {g.Count()} faces ({g.Count(f => f.Dark)} dark) round {centre.Round()}");
            foreach (var (weather, haze) in new[] { (WeatherKind.Clear, false), (WeatherKind.Fog, true) })
            // Fog thick enough to fade land 40 m off hides it altogether at 90: there it is looked at from 40.
            foreach (var (name, back) in new[] { ("near", 14f), ("far", haze ? 40f : 90f) })
            {
                _game.Zone.Weather.Sim.Snap(weather);
                Vector3 eye = centre + dir * back;
                eye.Y = Math.Max(centre.Y + back * 0.2f + 1.5f, _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f);
                _game.Survivor.PlaceOutside((eye - Vector3.Up * 1.7f).ToNumerics());
                double day = _game.Zone.Sky.TimeOfDay;
                for (int i = 0; i < 900; i++)
                {
                    _game.FixedCamera = new Transform3D(Basis.LookingAt(centre - eye, Vector3.Up), eye);
                    if (i % 120 == 2) day = LocalDay();
                    _game.Zone.Sky.TimeOfDay = day;
                    yield return 0;
                    if (i > 240 && _game.Zone.Streamer.Settled && !_game.Zone.Pois.MidPending) break;
                }
                string tag = $"spot{shot}-{(haze ? "haze" : "day")}-{name}";
                Snap(tag);
                // Measured with and without the blocks, their shadows off for both.
                var blocks = UnderNodes();
                var casts = blocks.Select(m => m.CastShadow).ToList();
                foreach (MeshInstance3D m in blocks) m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                foreach (int x in _drive.Wait(4)) yield return x;
                Image with = Grab();
                foreach (MeshInstance3D m in blocks) m.Visible = false;
                foreach (int x in _drive.Wait(4)) yield return x;
                Image without = Grab();
                for (int i = 0; i < blocks.Count; i++) { blocks[i].Visible = true; blocks[i].CastShadow = casts[i]; }
                // Each exposed face in sight, by the pixels round its middle: never black; and far off in haze, as faded
                // into it as the land behind (near, their own colours still differ).
                bool hazed = haze && name == "far";
                int seen = 0, black = 0, stands = 0;
                float worstLit = float.MaxValue, worstOff = 0f;
                var bad = new List<string>();
                byte[] da = with.GetData(), db = without.GetData();
                int iw = with.GetWidth(), ih = with.GetHeight();
                foreach (var f in faces)
                {
                    PInt3 v = f.Dir.Vec();
                    Vector3 p = world.NearestCopy(new Vector3(f.Cell.X + 0.5f + v.X * 0.5f, f.Cell.Y + 0.5f + v.Y * 0.5f, f.Cell.Z + 0.5f + v.Z * 0.5f), eye);
                    if (new Vector3(v.X, v.Y, v.Z).Dot(eye - p) <= 0f || Sample(da, db, iw, ih, p) is not var (lit, land, off)) continue;
                    seen++;
                    worstLit = Math.Min(worstLit, lit / Math.Max(land, 1e-3f));
                    bool dark = lit < NearBlack * land, hazy = hazed && off > HazeOff * Math.Max(land, 0.05f);
                    if (hazed) worstOff = Math.Max(worstOff, off / Math.Max(land, 0.05f));
                    if (dark) black++;
                    if (hazy) stands++;
                    if ((dark || hazy) && bad.Count < 4) bad.Add($"{f.Cell} {f.Dir} ({lit:F3} on {land:F3}, off {off:F3})");
                }
                if (seen == 0) { GD.Print($"    {tag}: no exposed face in sight"); continue; }
                measured++;
                bool ok = black == 0 && stands == 0;
                if (!ok) Faults++;
                GD.Print($"  {(ok ? "ok   " : "FAIL ")} {tag}: {seen} exposed faces in sight, {black} near-black (darkest {worstLit:F2} of the land behind, at least {NearBlack:F2})"
                    + (hazed ? $", {stands} out of the haze (worst off by {worstOff:F2} of the land's brightness, at most {HazeOff:F2})" : "") + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));
            }
        }
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        GD.Print($"  {(measured > 0 || faces.Count == 0 ? "ok   " : "FAIL ")} exposed blocks measured in {measured} frames");
        if (measured == 0 && faces.Count > 0) Faults++;

        // Down in a junction by day with no lamp: still dark.
        if (built.Plan.Rooms.FirstOrDefault(r => r.Kind == UnderSpaceKind.Junction && r.Network == UnderKind.Sewer) is UnderRoom room)
        {
            Vector3 mid = world.NearestCopy(new Vector3((room.Min.X + room.Max.X + 1) * 0.5f, room.Min.Y + 1.7f, (room.Min.Z + room.Max.Z + 1) * 0.5f), _game.Survivor.WorldFeet);
            _game.Survivor.PlaceOutside((mid - Vector3.Up * 1.7f).ToNumerics());
            for (int i = 0; i < 400; i++)
            {
                _game.FixedCamera = new Transform3D(Basis.LookingAt(new Vector3(1f, -0.25f, 0.3f), Vector3.Up), mid);
                _game.Zone.Sky.TimeOfDay = LocalDay();
                yield return 0;
                if (i > 200 && _game.Zone.Streamer.Settled) break;
            }
            // A sky field made round the eye down here, not the one it had on the surface.
            int floods = _game.Zone.Light.Floods;
            for (int i = 0; i < 240 && _game.Zone.Light.Floods < floods + 2; i++) { _game.Zone.Light.Touch(); yield return 0; }
            foreach (int x in _drive.Wait(4)) yield return x;
            Snap("junction-day");
            float dark = Brightness(Grab());
            bool ok = dark < 0.04f;
            if (!ok) Faults++;
            GD.Print($"  {(ok ? "ok   " : "FAIL ")} a sewer junction by day with no lamp reads {dark:F3} (under 0.040; the sky field says {_game.Zone.Light.LightAt(mid):F3} there)");
        }

        // A lining block breaks as a place's does: an exposed one shot from in front until it goes, seen before and after.
        if (faces.Where(f => f.Dir is not (Dir.Up or Dir.Down)).OrderBy(f => world.NearestCopy(new Vector3(f.Cell.X, f.Cell.Y, f.Cell.Z), _game.Survivor.WorldFeet).DistanceTo(_game.Survivor.WorldFeet))
            .Cast<(PInt3 Cell, Dir Dir, bool Dark)?>().FirstOrDefault() is { } target)
        {
            PInt3 v = target.Dir.Vec();
            Vector3 block = world.NearestCopy(new Vector3(target.Cell.X + 0.5f, target.Cell.Y + 0.5f, target.Cell.Z + 0.5f), _game.Survivor.WorldFeet), out_ = new Vector3(v.X, 0f, v.Z);
            Vector3 eye = block + out_ * 7f + out_.Cross(Vector3.Up) * 2f;
            eye.Y = Mathf.Max(eye.Y, _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.6f);
            IEnumerable<int> Look(string tag)
            {
                _game.Survivor.PlaceOutside((eye - Vector3.Up * 1.6f).ToNumerics());
                for (int i = 0; i < 600; i++)
                {
                    _game.FixedCamera = new Transform3D(Basis.LookingAt(block - eye, Vector3.Up), eye);
                    _game.Zone.Sky.TimeOfDay = LocalDay();
                    yield return 0;
                    if (i > 200 && _game.Zone.Streamer.Settled && !_game.Zone.Infra.PartsPending) break;
                }
                Snap(tag);
            }
            foreach (int x in Look("lining-before")) yield return x;
            int broken = _game.Zone.Infra.UnderBroken, shots = 0;
            GalHack.Arms.WeaponDef gun = _game.Zone.Data.Arms.OfItem(Wildlife.Weapons.ItemOf("rifle"));
            for (; shots < 200 && _game.Zone.Infra.UnderBroken == broken; shots++)
            {
                WorldFire.Fire(_game, gun, block + out_ * 4f, block);
                foreach (int x in _drive.Wait(5)) yield return x;
            }
            foreach (int x in Look("lining-after")) yield return x;
            bool gone = _game.Zone.Infra.UnderBroken > broken && !built.Blocks.ContainsKey(target.Cell);
            if (!gone) Faults++;
            GD.Print($"  {(gone ? "ok   " : "FAIL ")} an exposed lining block ({Lines(target.Cell)}) at {target.Cell} broke after {shots} rifle shots, as a place's block does ({_game.Zone.Infra.UnderBroken - broken} broken, its patch built again)");
        }
        _game.FixedCamera = null;
    }

    /// <summary>The under-town's drawn blocks now in the tree.</summary>
    List<MeshInstance3D> UnderNodes()
    {
        var found = new List<MeshInstance3D>();
        void Walk(Node n)
        {
            if (n is MeshInstance3D m && n.GetParent()?.Name.ToString().StartsWith("Under") == true) found.Add(m);
            foreach (Node c in n.GetChildren()) Walk(c);
        }
        Walk(_game.Zone);
        return found;
    }

    Image Grab()
    {
        Image i = _game.GetViewport().GetTexture().GetImage();
        i.Convert(Image.Format.Rgb8);
        return i;
    }

    static float Luma(byte[] d, int k) => (0.2126f * d[k] + 0.7152f * d[k + 1] + 0.0722f * d[k + 2]) / 255f;

    /// <summary>Mean brightness of a frame's middle half.</summary>
    static float Brightness(Image i)
    {
        byte[] d = i.GetData();
        int w = i.GetWidth(), h = i.GetHeight(), n = 0;
        double sum = 0;
        for (int y = h / 4; y < h * 3 / 4; y++)
            for (int x = w / 4; x < w * 3 / 4; x++) { sum += Luma(d, (y * w + x) * 3); n++; }
        return (float)(sum / Math.Max(1, n));
    }

    /// <summary>
    /// A point's 3x3 pixels in the frames with the blocks (a) and without (b): their brightness in each and how far apart their
    /// colours are (0-1 a channel). Null off the screen, or where any of them is the same in both (the blocks don't show there).
    /// </summary>
    (float With, float Without, float Off)? Sample(byte[] da, byte[] db, int w, int h, Vector3 p)
    {
        Camera3D cam = _game.GetViewport().GetCamera3D();
        if (cam == null || cam.IsPositionBehind(p)) return null;
        Vector2 s = cam.UnprojectPosition(p) * new Vector2(w, h) / _game.GetViewport().GetVisibleRect().Size;
        int cx = (int)s.X, cy = (int)s.Y;
        if (cx < 1 || cy < 1 || cx > w - 2 || cy > h - 2) return null;
        float la = 0, lb = 0, r = 0, g = 0, bl = 0;
        for (int y = cy - 1; y <= cy + 1; y++)
            for (int x = cx - 1; x <= cx + 1; x++)
            {
                int k = (y * w + x) * 3;
                if (Math.Abs(da[k] - db[k]) + Math.Abs(da[k + 1] - db[k + 1]) + Math.Abs(da[k + 2] - db[k + 2]) < 12) return null;
                la += Luma(da, k); lb += Luma(db, k);
                r += da[k] - db[k]; g += da[k + 1] - db[k + 1]; bl += da[k + 2] - db[k + 2];
            }
        return (la / 9f, lb / 9f, MathF.Sqrt(r * r + g * g + bl * bl) / 9f / 255f / MathF.Sqrt(3f));
    }
}
