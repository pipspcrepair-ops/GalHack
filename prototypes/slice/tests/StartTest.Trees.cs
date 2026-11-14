using System.Collections.Generic;
using Godot;
using PlanetSurface.Terrain;
using Slice.Game;

namespace Slice.Tests;

public partial class StartTest
{
    /// <summary>The tree distance: it sets flora's far reach live, never goes past the detail distance, follows it down, and keeps in the settings file.</summary>
    IEnumerable<int> TreeDistance(Game.Game game)
    {
        float detail = game.Settings.DetailDistance;
        game.Settings.TreeDistance = 600f;
        game.Settings.Changed();
        yield return 0;
        yield return 0;
        Check(DetailReach.Large == 600f && TerrainStreamer.FloraLayerReach(TerrainStreamer.LargeLayer) == 600f && DetailReach.LargeBand == 90f && DetailReach.Distance == detail,
            $"tree distance 600 m: trees' reach {DetailReach.Large:F0} m (fade {DetailReach.LargeBand:F0} m), detail still {DetailReach.Distance:F0} m");
        DetailReach.Set(detail, 99999f);
        Check(DetailReach.Large == detail, $"tree distance is clamped to the detail distance ({DetailReach.Large:F0} of {detail:F0} m)");
        game.Settings.TreeDistance = 1900f;
        game.Settings.DetailDistance = 2400f;
        game.Settings.Changed();
        for (int k = 0; k < 6; k++) yield return 0;
        bool high = DetailReach.Large == 1900f;
        GD.Print($"  (detail {DetailReach.Distance:F0}, trees {DetailReach.Large:F0}, asked {game.Settings.DetailDistance:F0}/{game.Settings.TreeDistance:F0})");
        game.Settings.DetailDistance = 1500f;
        game.Settings.Changed();
        yield return 0;
        yield return 0;
        Check(high && DetailReach.Large == 1500f, $"when the detail distance drops below it, trees follow ({DetailReach.Large:F0} m)");
        const string path = "user://tree-test-settings.cfg";
        new GameSettings { TreeDistance = 777f }.Save(path);
        float back = GameSettings.Load(path).TreeDistance;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        Check(back == 777f, $"tree distance keeps in the settings file ({back:F0} m read back)");
        Check(new GameSettings().TreeDistance >= DetailReach.Most, "the default follows the detail distance");
        game.Settings.TreeDistance = DetailReach.Most;
        game.Settings.DetailDistance = detail;
        game.Settings.Changed();
        yield return 0;
    }
}
