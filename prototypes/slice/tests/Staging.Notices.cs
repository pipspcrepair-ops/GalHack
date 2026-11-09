using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Ui;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The notices' slot in play: <c>--view=notices --dir=folder</c> writes notices-hud (out on the surface, a level-up,
/// an unlock and a recipe learnt in the slot at the top right beside the compass) and notices-menu (more of them with
/// the Tab menu up, in the corner of its header).
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> NoticesShots()
    {
        string dir = _args.GetValueOrDefault("dir", "user://");
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        foreach (int x in _drive.Wait(30)) yield return x;
        Vector3 eye = _game.Survivor.WorldEye(), look = _game.Survivor.WorldLook();
        foreach (int x in _drive.LookAt(eye + new Vector3(look.X, 0f, look.Z).Normalized() * 40f + Vector3.Down * 1.5f, 12)) yield return x;
        foreach (int x in _drive.Wait(240)) yield return x; // anything from the climb out has gone
        Progress p = _game.Progress;
        void Burst()
        {
            var (into, span) = p.ToNext;
            p.Earn((int)(span - into) + 1, "staging");
            TechNode n = p.Nodes.FirstOrDefault(n => p.StateOf(n) == UnlockState.Affordable);
            if (n != null) p.Unlock(n.Id);
        }
        Burst();
        _game.Controls.Mouse = new Vector2(-100, -100);
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in Shot($"{dir}/notices-hud.png")) yield return x;
        foreach (int x in _drive.Wait(300)) yield return x;
        foreach (int x in Play.OpenMenu(MenuTab.You)) yield return x;
        Burst();
        _game.Notices.Post(NoticeKind.Made, "MADE", "2 Hull plate");
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in Shot($"{dir}/notices-menu.png")) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
    }
}
