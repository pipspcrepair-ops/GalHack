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
    /// A three-quarter view of a structure from outside, a little above, the ground in the frame: of
    /// eight sides the one with the fewest plants in the way, and those few felled for the frame (a
    /// photographer's clearing; the structure itself stands as it was placed).
    /// </summary>
    IEnumerable<int> Outside(Vessel v)
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
        Vector3 best = default;
        List<long> blocking = null;
        for (int k = 0; k < 8; k++)
        {
            float a = Mathf.Pi * 0.25f * k + 0.6f;
            var local = mid + new N.Vector3(MathF.Sin(a) * size * 1.15f, size * 0.3f, -MathF.Cos(a) * size * 1.15f);
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
