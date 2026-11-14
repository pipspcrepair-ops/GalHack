using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using Godot;
using Slice.Construction;
using Slice.Items;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The BLUEPRINTS tab, frame by frame into --dir, in daylight: each starter set down whole in
/// Creative through the tab's own PLACE and seen from outside (placed-NAME; any plants between the
/// camera and it felled for the frame), the tab with a blueprint
/// selected over the world (tab-NAME), and the save dialog with the Skiff you sit in (save-dialog).
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> BlueprintShots()
    {
        string dir = _args.TryGetValue("dir", out string d) ? d : "user://blueprints";
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        _game.Mode = GameMode.Creative;
        BlueprintsSection tab = _game.Menu.Root.Blueprints;
        var placed = new Dictionary<string, Vessel>();
        int n = 0;
        foreach (LibraryEntry e in tab.Library.Starters.ToList())
        {
            Vessel v = null;
            for (int attempt = 0; attempt < 4 && v == null; attempt++, n++)
            {
                float radius = 0.5f * new Vector2(e.Size.X, e.Size.Z).Length();
                Vector3 spot = Play.FlatSpot(_game.Start, 110f + 60f * (n % 5), n * 1.3f, 0.5f + attempt * 0.5f, span: MathF.Max(14f, radius));
                float yaw = n * 0.9f;
                Vector3 ahead = new Basis(Vector3.Up, yaw) * Vector3.Forward;
                Vector3 stand = spot - ahead * (radius + 4f);
                _game.Survivor.PlaceOutside((_game.Zone.GroundAt(stand.X, stand.Z) + Vector3.Up * 0.2f).ToNumerics());
                foreach (int x in _drive.UntilSettled(20)) yield return x;
                foreach (int x in _drive.Wait(30)) yield return x;
                _game.Survivor.Yaw = yaw;
                _game.Survivor.Pitch = 0f;
                foreach (int x in Play.OpenMenu(MenuTab.Blueprints)) yield return x;
                foreach (int x in _drive.ClickWidget($"bp:entry:{tab.Library.All.ToList().IndexOf(e)}")) yield return x;
                foreach (int x in _drive.ClickWidget("bp:place")) yield return x;
                WholePlacement job = _game.Builder.Placing;
                for (int t = 0; t < 600 && job != null && !job.Done; t++) yield return 0;
                v = job?.Vessel;
                if (v == null)
                {
                    GD.Print($"({e.Name} would not go down near {spot}: {tab.Why})");
                    foreach (int x in Play.CloseMenu()) yield return x;
                }
            }
            if (v == null) continue;
            placed[e.File] = v;
            foreach (int x in _drive.Wait(40)) yield return x;
            _game.Visor.Visible = false;
            foreach (int x in Outside(v)) yield return x;
            foreach (int x in Shot($"{dir}/placed-{Slug(e.Name)}.png")) yield return x;
            _game.Visor.Visible = true;
            _game.FixedCamera = null;
        }

        // The tab over the world, a blueprint selected and turning.
        foreach (string pick in new[] { "frigate.gbp", "dart.gbp" })
        {
            LibraryEntry e = tab.Library.Starters.FirstOrDefault(x => x.File == pick);
            if (e == null) continue;
            foreach (int x in Play.OpenMenu(MenuTab.Blueprints)) yield return x;
            foreach (int x in _drive.ClickWidget($"bp:entry:{tab.Library.All.ToList().IndexOf(e)}")) yield return x;
            _game.Controls.Mouse = new Vector2(-100, -100); // nothing lit under the pointer
            foreach (int x in _drive.Wait(50)) yield return x;
            foreach (int x in Shot($"{dir}/tab-{Slug(e.Name)}.png")) yield return x;
        }
        foreach (int x in Play.CloseMenu()) yield return x;

        // The save dialog, sitting in the Skiff set down above.
        if (placed.TryGetValue(_game.Zone.Data.Rules.Build.StarterBlueprint, out Vessel skiff))
        {
            _game.Survivor.Sit(skiff);
            foreach (int x in _drive.Wait(20)) yield return x;
            foreach (int x in Play.OpenMenu(MenuTab.Blueprints)) yield return x;
            foreach (int x in _drive.Wait(4)) yield return x;
            foreach (int x in _drive.ClickWidget("bp:save")) yield return x;
            _game.Controls.Backspaces = 40;
            _game.Controls.Type("Red Skiff");
            _game.Controls.Mouse = new Vector2(-100, -100);
            foreach (int x in _drive.Wait(20)) yield return x;
            foreach (int x in Shot($"{dir}/save-dialog.png")) yield return x;
            foreach (int x in _drive.ClickWidget("bp:save-cancel")) yield return x;
            foreach (int x in Play.CloseMenu()) yield return x;
        }
    }

    /// <summary>
    /// The stock set (<see cref="BlueprintSet"/>), frame by frame into --dir: each blueprint set down whole by Creative's
    /// PLACE and seen from outside (stock-NAME-out), then from its chair (stock-NAME-in), the seated figure in view.
    /// --only=a,b picks files by name.
    /// </summary>
    IEnumerable<int> StockShots()
    {
        string dir = _args.TryGetValue("dir", out string d) ? d : "user://stock";
        string[] only = _args.TryGetValue("only", out string o) ? o.Split(',') : null;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Mode = GameMode.Creative;
        var catalogue = _game.Zone.Data.Catalogue;
        int n = 0;
        foreach (BlueprintSet.Design design in BlueprintSet.All)
        {
            string file = System.IO.Path.GetFileNameWithoutExtension(design.File);
            if (only != null && !only.Contains(file)) continue;
            var plan = GalHack.Structures.Blueprint.Load(design.Make(catalogue), catalogue, out _);
            if (plan == null) continue;
            Vessel v = null;
            var (lo, hi) = plan.Bounds();
            float radius = 0.5f * new Vector2(hi.X - lo.X + 1, hi.Z - lo.Z + 1).Length();
            for (int attempt = 0; attempt < 4 && v == null; attempt++, n++)
            {
                Vector3 spot = Play.FlatSpot(_game.Start, 110f + 60f * (n % 5), n * 1.3f, 0.5f + attempt * 0.5f, span: MathF.Max(14f, radius));
                Vector3 stand = spot - Vector3.Forward * (radius + 4f);
                _game.Survivor.PlaceOutside((_game.Zone.GroundAt(stand.X, stand.Z) + Vector3.Up * 0.2f).ToNumerics());
                foreach (int x in _drive.UntilSettled(20)) yield return x;
                foreach (int x in _drive.Wait(30)) yield return x;
                WholePlacement job = _game.Builder.PlaceWhole(plan);
                for (int t = 0; t < 600 && !job.Done; t++) yield return 0;
                v = job.Vessel;
            }
            if (v == null) { GD.Print($"({plan.Name} would not go down)"); continue; }
            for (int t = 0; t < 300 && (v.PendingShapes > 0 || v.View.Waiting > 0); t++) yield return 0;
            foreach (int x in _drive.Wait(40)) yield return x;
            _game.Visor.Visible = false;
            foreach (int x in Outside(v, front: true)) yield return x;
            foreach (int x in Shot($"{dir}/stock-{file}-out.png")) yield return x;
            _game.FixedCamera = null;
            _game.ThirdPerson = false;
            if (file == "frigate")
            {
                foreach (int x in StairShot(v, $"{dir}/stock-{file}-stair.png")) yield return x;
                foreach (int x in Closeup(v, new N.Vector3(-8, 10, -9), new N.Vector3(-14, 15, -14), $"{dir}/stock-{file}-roofedge.png")) yield return x;
                foreach (int x in Closeup(v, new N.Vector3(-4, 10, -20), new N.Vector3(-9, 14, -27), $"{dir}/stock-{file}-cockpitcorner.png")) yield return x;
                foreach (int x in Closeup(v, new N.Vector3(-8, 3, 10), new N.Vector3(-14, 7, 16), $"{dir}/stock-{file}-nacelletail.png")) yield return x;
                foreach (int x in Closeup(v, new N.Vector3(-6, 8, -2), new N.Vector3(-12, 13, -8), $"{dir}/stock-{file}-towerramp.png")) yield return x;
            }
            if (file == "outpost")
            {
                foreach (int x in Closeup(v, new N.Vector3(19, 0, -7), new N.Vector3(25, 5, -13), $"{dir}/stock-{file}-padcorner-n.png")) yield return x;
                foreach (int x in Closeup(v, new N.Vector3(19, 0, 6), new N.Vector3(25, 5, 12), $"{dir}/stock-{file}-padcorner-s.png")) yield return x;
                foreach (int x in Closeup(v, new N.Vector3(7, 0, 6), new N.Vector3(13, 4, 12), $"{dir}/stock-{file}-padbase.png")) yield return x;
                foreach (int x in Closeup(v, new N.Vector3(-5, 5, -6), new N.Vector3(-10, 9, -12), $"{dir}/stock-{file}-roofpillar.png")) yield return x;
            }
            _game.Survivor.Sit(v);
            _game.Survivor.Pitch = -0.35f;
            foreach (int x in _drive.Wait(30)) yield return x;
            foreach (int x in Shot($"{dir}/stock-{file}-in.png")) yield return x;
            _game.Survivor.StandUp();
            _game.Visor.Visible = true;
            foreach (int x in _drive.Wait(5)) yield return x;
        }
    }

    /// <summary>The flight of stairs that rises highest, seen on foot from its foot, standing, looking up it (the way to the bridge).</summary>
    IEnumerable<int> StairShot(Vessel v, string file)
    {
        var s = v.Structure;
        var stairs = s.Blocks().Where(b => s.TypeOf(b.block).Function == "stairs").ToList();
        int top = stairs.Max(b => b.pos.Y);
        var (pos, block) = stairs.Where(b => b.pos.Y == top).First();
        GalHack.Structures.Int3 rise = GalHack.Structures.Dirs.Vec(GalHack.Structures.Orientations.ToWorld(block.Rot, GalHack.Structures.Dir.PosZ));
        // The flight: stairs in line with the top one along its rise, from the lowest.
        var flight = stairs.Where(b => b.pos.Y >= 1 && (rise.X == 0 ? b.pos.X >= pos.X - 3 && b.pos.X <= pos.X + 3 : b.pos.Z >= pos.Z - 3 && b.pos.Z <= pos.Z + 3)).ToList();
        var foot = flight.OrderBy(b => b.pos.Y).First().pos;
        float cell = s.Grid.CellSize;
        float mid = (float)flight.Where(b => b.pos.Y == foot.Y).Average(b => rise.X == 0 ? b.pos.X : b.pos.Z);
        N.Vector3 at = new N.Vector3(rise.X == 0 ? mid : foot.X - 2 * rise.X, foot.Y - 0.4f, rise.Z == 0 ? mid : foot.Z - 2 * rise.Z) * cell;
        _game.Survivor.PlaceAboard(v, at);
        _game.Survivor.Yaw = MathF.Atan2(-rise.X, -rise.Z);
        _game.Survivor.Pitch = 0.3f;
        foreach (int x in _drive.Tap(Key.L)) yield return x;
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (int x in Shot(file)) yield return x;
        foreach (int x in _drive.Tap(Key.L)) yield return x;
        _game.Survivor.PlaceOutside((v.Body.Position + N.Vector3.UnitY * 30f).ToGodot().ToNumerics());
    }

    /// <summary>A close view of a spot on a structure: the camera at a structure-local eye, looking at a structure-local target.</summary>
    IEnumerable<int> Closeup(Vessel v, N.Vector3 target, N.Vector3 eye, string file)
    {
        float cell = v.Structure.Grid.CellSize;
        Vector3 to = v.Body.ToParent(target * cell).ToGodot(), from = v.Body.ToParent(eye * cell).ToGodot();
        foreach (long id in _game.Zone.Streamer.FloraNear(from, 6f).Select(p => p.Id).ToList())
        {
            _game.Zone.Planet.Felled.Add(id);
            _game.Zone.Streamer.Fell(id);
        }
        _game.FixedCamera = new Transform3D(Basis.LookingAt((to - from).Normalized(), Vector3.Up), from);
        foreach (int x in _drive.Wait(25)) yield return x;
        foreach (int x in Shot(file)) yield return x;
        _game.FixedCamera = null;
    }

    /// <summary>
    /// A three-quarter view of a structure from outside, a little above, the ground in the frame: of
    /// eight sides the one with the fewest plants in the way, and those few felled for the frame (a
    /// photographer's clearing; the structure itself stands as it was placed).
    /// </summary>
    IEnumerable<int> Outside(Vessel v, bool front = false)
    {
        var (min, max) = v.Structure.Bounds();
        float cell = v.Structure.Grid.CellSize;
        var lo = new N.Vector3(min.X, min.Y, min.Z) * cell;
        var hi = new N.Vector3(max.X, max.Y, max.Z) * cell;
        N.Vector3 mid = (lo + hi) * 0.5f;
        float size = (hi - lo).Length() + cell;
        Vector3 centre = v.Body.ToParent(mid).ToGodot();
        var streamer = _game.Zone.Streamer;
        List<long> InTheWay(Vector3 eye)
        {
            var ids = new HashSet<long>();
            Vector3 to = centre - eye;
            float length = to.Length() - size * 0.35f;
            for (float t = 0f; t < length; t += 2f)
            {
                Vector3 p = eye + to.Normalized() * t;
                foreach (var plant in streamer.FloraNear(p, 3.5f)) ids.Add(plant.Id);
            }
            return ids.ToList();
        }
        // Framing: far enough that the whole craft fits the view, and a clearing round it.
        float fit = front ? 0.5f * size / MathF.Tan(Mathf.DegToRad(_game.Camera.Fov) * 0.5f) * 1.1f : size * 1.15f;
        if (front)
            foreach (long id in streamer.FloraNear(centre, size * 2.2f).Select(p => p.Id).ToList())
            {
                _game.Zone.Planet.Felled.Add(id);
                streamer.Fell(id);
            }
        Vector3 best = default;
        List<long> blocking = null;
        for (int k = 0; k < 8; k++)
        {
            if (front && k is > 0 and < 6) continue; // the bow's three-quarter views only
            float a = Mathf.Pi * 0.25f * k + 0.6f;
            var local = mid + new N.Vector3(MathF.Sin(a) * fit * 0.92f, fit * 0.38f, -MathF.Cos(a) * fit * 0.92f);
            Vector3 eye = v.Body.ToParent(local).ToGodot();
            eye.Y = MathF.Max(eye.Y, _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.8f);
            List<long> ids = InTheWay(eye);
            if (blocking != null && ids.Count >= blocking.Count) continue;
            best = eye;
            blocking = ids;
        }
        foreach (long id in blocking)
        {
            _game.Zone.Planet.Felled.Add(id);
            streamer.Fell(id);
        }
        _game.FixedCamera = new Transform3D(Basis.LookingAt((centre - best).Normalized(), Vector3.Up), best);
        foreach (int x in _drive.Wait(blocking.Count > 0 ? 45 : 6)) yield return x;
    }

    static string Slug(string name) => name.ToLowerInvariant().Replace(' ', '-');
}
