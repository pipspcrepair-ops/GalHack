using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

public sealed partial class Staging
{
    /// <summary>A side-on camera on a craft, the ground and its height in view.</summary>
    void Beside(Vessel v, float distance)
    {
        Vector3 at = v.Body.Position.ToGodot();
        Vector3 eye = at + new Vector3(distance, 0f, distance * 0.6f);
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 2.2f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt((at - eye).Normalized(), Vector3.Up), eye);
    }

    IEnumerable<int> ClickMode(CoreMode mode)
    {
        foreach (int x in Play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in _drive.ClickWidget($"core:mode:{mode.ToString().ToLowerInvariant()}")) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
    }

    /// <summary>
    /// The two cores, shot by shot into --dir: both tech tabs, a craft in Hover Mode and in Vessel
    /// Mode (the one craft, switched on the CORE tab), the striped switch, and the seated indicator
    /// in all four modes. The structures are set down whole (STAGING); the switch is clicked.
    /// </summary>
    IEnumerable<int> CoresView()
    {
        string dir = _args.TryGetValue("dir", out string d) ? d : "user://cores";
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;

        foreach (int x in Play.OpenMenu(MenuTab.Tech)) yield return x;
        foreach (int x in _drive.ClickWidget("tech:tab:structures")) yield return x;
        foreach (int x in _drive.Wait(4)) yield return x;
        foreach (int x in Shot($"{dir}/tech-structures.png")) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;

        Vector3 site = Play.FlatSpot(_game.Start, 40f, 0.6f, 0.4f, span: 14f);
        _game.Survivor.PlaceOutside((site + new Vector3(0f, 1f, 7f)).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;

        Structure s = CoreCraft.Skiff(_game.Zone.Data);
        CoreCraft.FitPlates(s, _game.Zone.Data.Catalogue);
        s.DirtyChunks.Clear();
        Vessel v = Vessel.Spawn(_game.Zone, s, (site + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        v.Settle();
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in _drive.Board(v)) yield return x;
        foreach (int x in ClickMode(CoreMode.Hover)) yield return x;
        _game.Controls.Hold(Key.W);
        foreach (int x in _drive.Wait(30)) yield return x;
        _game.Controls.Hold(Key.W, false);
        foreach (int x in _drive.Wait(90)) yield return x;
        foreach (int x in Shot($"{dir}/seated-hover.png")) yield return x;
        Beside(v, 7f);
        _game.Visor.Visible = false;
        foreach (int x in _drive.Wait(4)) yield return x;
        foreach (int x in Shot($"{dir}/craft-hover-mode.png")) yield return x;
        _game.Visor.Visible = true;
        _game.FixedCamera = null;

        foreach (int x in Play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in _drive.Wait(4)) yield return x;
        foreach (int x in Shot($"{dir}/core-tab-small-core.png")) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;

        foreach (int x in ClickMode(CoreMode.Vessel)) yield return x;
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(30)) yield return x;
        _game.Controls.Hold(Key.Space, false);
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in Shot($"{dir}/seated-vessel.png")) yield return x;
        Beside(v, 9f);
        _game.Visor.Visible = false;
        foreach (int x in _drive.Wait(4)) yield return x;
        foreach (int x in Shot($"{dir}/craft-vessel-mode.png")) yield return x;
        _game.Visor.Visible = true;
        _game.FixedCamera = null;

        // Set the craft down out of the way and walk to a base with everything a ship needs on it.
        _game.Survivor.StandUp();
        site += new Vector3(30f, 0f, 0f);
        Vector3 ground = _game.Zone.GroundAt(site.X, site.Z);
        _game.Survivor.PlaceOutside((ground + new Vector3(0f, 1f, 10f)).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        var tune = _game.Zone.Data.Handling;
        float g = _game.Zone.Physics.Zone.Strength;
        Structure big = CoreCraft.Ship(_game.Zone.Data.Catalogue, true, x => ModeFit.Missing(x, CoreMode.Capital, tune, g).Count == 0);
        big.DirtyChunks.Clear();
        Vessel b = Vessel.SpawnBase(_game.Zone, big, (ground + Vector3.Up * 1.4f).ToNumerics(), 0f);
        foreach (var (p, _) in big.Blocks().ToList()) b.ClearGround(p);
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in _drive.Board(b)) yield return x;
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in Shot($"{dir}/seated-base.png")) yield return x;
        foreach (int x in Play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in _drive.Wait(4)) yield return x;
        foreach (int x in Shot($"{dir}/core-tab-big-core.png")) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        foreach (int x in ClickMode(CoreMode.Capital)) yield return x;
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(24)) yield return x;
        _game.Controls.Hold(Key.Space, false);
        foreach (int x in _drive.Wait(40)) yield return x;
        // The last shot of the view is taken by the scene.
    }
}
