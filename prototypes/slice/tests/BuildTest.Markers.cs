using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>
/// Markers on the things themselves: for the bunker, the place in view from its way out, a map mark and the
/// scanner's pings, looked at from several headings and two spots, each marker sits on its target's own projection
/// (the engine's, to within a few pixels) unless it was nudged off a box of the visor; none is on a box, no two
/// labels overlap, and a target behind the camera is pinned to the screen's edge. Looking at the place puts its
/// marker on the place itself.
/// </summary>
public partial class BuildTest
{
    IEnumerable<int> MarkerChecks()
    {
        GD.Print("-- markers on the things themselves --");
        Camera3D cam = _game.Camera;
        Visor visor = _game.Visor;
        // A 1080p view (a headless run's own window is tiny), its targets projected by the engine's own perspective matrix.
        var size = new Vector2(1920, 1080);
        Vector2 Projected(Vector3 p)
        {
            Vector3 l = cam.GlobalTransform.AffineInverse() * p;
            Vector4 clip = Projection.CreatePerspective(cam.Fov, size.X / size.Y, cam.Near, cam.Far, false) * new Vector4(l.X, l.Y, l.Z, 1f);
            return new Vector2((clip.X / clip.W + 1f) / 2f * size.X, (1f - clip.Y / clip.W) / 2f * size.Y);
        }
        var s = _game.Survivor;
        Vector3 start = s.WorldFeet;
        // A map mark 70 m off, and a ping, beside the bunker and the place.
        Vector3 markAt = start + new Vector3(50f, 0f, -50f);
        _game.Spaceflight.Charts.Mark(markAt.X, markAt.Z);
        _game.Scan();
        foreach (int x in _drive.Wait(2)) yield return x;
        var kinds = visor.Markers().Select(m => m.Kind).Distinct().ToList();

        int views = 0, compared = 0, worstAt = 0;
        float worst = 0f;
        var boxProblems = new List<string>();
        var labelProblems = new List<string>();
        void Measure(string where)
        {
            foreach (Vector2 sz in new[] { size, new Vector2(2560, 1440), new Vector2(1280, 720) })
            {
                List<Visor.PlacedMarker> placed = visor.LayoutMarkers(sz, 1f);
                HudLayout l = HudLayout.Compute(sz, 1f);
                for (int i = 0; i < placed.Count; i++)
                {
                    Visor.PlacedMarker p = placed[i];
                    foreach (var (name, box) in l.Boxes)
                        if (box.Intersects(p.Icon) || p.Labelled && box.Intersects(p.Label)) boxProblems.Add($"{where} {sz}: {p.Mark.Tag} on the {name}");
                    for (int k = i + 1; k < placed.Count; k++)
                        if (p.Labelled && placed[k].Labelled && p.Label.Intersects(placed[k].Label)) labelProblems.Add($"{where} {sz}: {p.Mark.Tag} and {placed[k].Mark.Tag}");
                }
                if (sz != size) continue;
                // Against the engine's own projection of each target in front of the camera.
                foreach (Visor.PlacedMarker p in placed.Where(p => !p.Behind && !p.Pinned))
                {
                    float off = p.Anchor.DistanceTo(Projected(p.Mark.At));
                    if (!p.Nudged) off = MathF.Max(off, p.At.DistanceTo(p.Anchor));
                    compared++;
                    if (off > worst) { worst = off; worstAt = views; }
                }
            }
            views++;
        }
        Vector3 eye = s.WorldEye();
        foreach (Vector3 spot in new[] { start, start + new Vector3(12f, 0f, 9f) })
        {
            if (spot != start) { s.PlaceOutside((_game.Zone.GroundAt(spot.X, spot.Z) + Vector3.Up * 0.3f).ToNumerics()); foreach (int x in _drive.Wait(20)) yield return x; }
            eye = s.WorldEye();
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.Tau / 6f;
                foreach (int x in _drive.LookAt(eye + new Vector3(MathF.Cos(a), -0.08f, MathF.Sin(a)) * 30f, 8)) yield return x;
                Measure($"heading {k * 60}");
            }
        }
        Check(compared > 0 && worst < 3f, $"{compared} markers ({string.Join(", ", kinds)}) over {views} views from two spots sit on their targets' own projection in the {size.X:F0}x{size.Y:F0} view: worst {worst:F2} px");
        Check(boxProblems.Count == 0, $"no marker on a box of the visor at 1080p, 1440p or 720p{(boxProblems.Count == 0 ? "" : ": " + string.Join("; ", boxProblems.Take(5)))}");
        Check(labelProblems.Count == 0, $"no two labels on one another{(labelProblems.Count == 0 ? "" : ": " + string.Join("; ", labelProblems.Take(5)))}");

        // The MARKERS option: kept in settings.cfg, read back after a restart, and applied live (COMPASS draws none in the world).
        string cfg = "user://markers_test.cfg";
        var kept = new GameSettings { MarkersInWorld = false };
        kept.Save(cfg);
        Check(!GameSettings.Load(cfg).MarkersInWorld && new GameSettings().MarkersInWorld, "MARKERS: COMPASS is saved and read back after a restart; the default is WORLD");
        kept.MarkersInWorld = true;
        kept.Save(cfg);
        Check(GameSettings.Load(cfg).MarkersInWorld, "MARKERS: WORLD is saved and read back");
        DirAccess.RemoveAbsolute(cfg);
        bool was = _game.Settings.MarkersInWorld;
        _game.Settings.MarkersInWorld = false;
        _game.Settings.Changed();
        Check(!visor.MarkersInWorld && HudLayout.Compute(size, 1f, true).Compass.Size.Y > HudLayout.Compute(size, 1f).Compass.Size.Y, "switching to COMPASS applies at once: the compass gets its label rows back");
        _game.Settings.MarkersInWorld = was;
        _game.Settings.Changed();

        // Behind: look straight away from the mark.
        eye = s.WorldEye();
        Vector3 markTarget = visor.Markers().First(m => m.Kind == "mark").At;
        Vector3 away = eye - (markTarget - eye).Normalized() * 30f;
        foreach (int x in _drive.LookAt(new Vector3(away.X, eye.Y, away.Z), 10)) yield return x;
        Visor.PlacedMarker back = visor.LayoutMarkers(size, 1f).FirstOrDefault(p => p.Mark.Kind == "mark");
        float sc = HudLayout.Compute(size, 1f).Scale, reach = (Visor.MarkerMargin + Visor.MarkerIcon) * sc + 4f;
        float fromEdge = MathF.Min(MathF.Min(back.At.X, back.At.Y), MathF.Min(size.X - back.At.X, size.Y - back.At.Y));
        Check(back.Behind && back.Pinned && !back.Labelled && fromEdge <= reach && Vector2.Zero.DistanceTo(back.Direction) > 0.9f,
            $"a mark behind the camera is pinned to the screen's edge with its arrow, not drawn in the middle ({fromEdge:F0} px from the edge at {back.At})");

        // The owner's own example: look at the place, and its marker is on the place.
        Visor.Marker place = visor.Markers().FirstOrDefault(m => m.Kind == "place");
        if (place.Kind == "place")
        {
            foreach (int x in _drive.LookAt(place.At, 12)) yield return x;
            Visor.PlacedMarker on = visor.LayoutMarkers(size, 1f).FirstOrDefault(p => p.Mark.Kind == "place");
            Vector2 seen = Projected(place.At);
            Check(!on.Pinned && on.At.DistanceTo(seen) < 3f && on.At.DistanceTo(size / 2f) < 40f,
                $"looking at {place.Tag}, its marker sits on the place itself, {Visor.Distance(on.Distance)} off, {on.At.DistanceTo(seen):F1} px from its projection and at the middle of the view (not on the compass)");
        }
        else GD.Print("      (no place found from the bunker on this world: the place check is skipped)");
        _game.Spaceflight.Charts.Unmark(_game.Spaceflight.Charts.OfKind("mark").First().Key);
        s.PlaceOutside((_game.Zone.GroundAt(start.X, start.Z) + Vector3.Up * 0.3f).ToNumerics());
        foreach (int x in _drive.Wait(10)) yield return x;
    }
}
