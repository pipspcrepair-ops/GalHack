using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AssetViewer;

/// <summary>Saving and loading where you are: the asset, its seed and options, the animation and the camera.</summary>
public partial class Viewer
{
    public Position CapturePosition(string name)
    {
        var p = new Position
        {
            Name = name,
            Saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Asset = Entry?.Key ?? "",
            Seed = Seed,
            Options = new Dictionary<string, string>(Options),
            Animation = Animation,
            FreeCamera = Cam.IsFree,
            Target = new[] { Cam.Target.X, Cam.Target.Y, Cam.Target.Z },
            Yaw = Cam.Yaw,
            Pitch = Cam.Pitch,
            Distance = Cam.Distance,
            CameraPosition = new[] { Cam.Position.X, Cam.Position.Y, Cam.Position.Z },
            Fov = Cam.Fov,
            Spin = Spin,
            Turntable = Stage.Pivot.Rotation.Y,
            Light = Stage.Light,
            Ground = Stage.GroundWanted,
        };
        if (Cam.IsFree)
        {
            // The free camera keeps its own angles.
            p.Yaw = Cam.FreeYaw;
            p.Pitch = Cam.FreePitch;
        }
        return p;
    }

    /// <summary>Brings back a saved position. Returns false if its asset no longer exists.</summary>
    public bool ApplyPosition(Position p)
    {
        AssetEntry e = Registry.Find(p.Asset);
        if (e == null) { Toast("That asset is gone", 3f); return false; }
        Select(e, p.Seed, p.Options, false);
        if (p.Animation != "" && Instance != null && Instance.Animations.Contains(p.Animation)) PlayAnimation(p.Animation);
        _frameWanted = false;
        _frameIn = -1;
        Stage.FitGround(MeasureBounds());
        Stage.Light = p.Light;
        Stage.GroundWanted = p.Ground;
        Stage.Refresh();
        Spin = p.Spin;
        Stage.Pivot.Rotation = new Vector3(0f, p.Turntable, 0f);
        Cam.ApplyFov(p.Fov);
        Cam.Target = new Vector3(p.Target[0], p.Target[1], p.Target[2]);
        if (p.FreeCamera)
        {
            Cam.PlaceFree(new Vector3(p.CameraPosition[0], p.CameraPosition[1], p.CameraPosition[2]), Vector3.Zero);
            Cam.SetFreeAngles(p.Yaw, p.Pitch);
        }
        else
        {
            Cam.SetFree(false);
            Cam.Yaw = p.Yaw;
            Cam.Pitch = p.Pitch;
            Cam.Distance = Mathf.Max(p.Distance, 0.05f);
            Cam.Refresh();
        }
        SyncCameraMode();
        RefreshPanels();
        return true;
    }

    public void SavePosition(string name)
    {
        if (Entry == null) return;
        if (string.IsNullOrWhiteSpace(name)) name = $"{Entry.Name} {DateTime.Now:HH:mm:ss}";
        Notes.AddBookmark(CapturePosition(name.Trim()));
        Notes.Last = CapturePosition("last");
        SaveNow(false);
        RefreshBookmarks();
        Toast($"Position saved: {name.Trim()}", 2f);
    }

    public void LoadLastBookmark()
    {
        if (Notes.Bookmarks.Count == 0) { Toast("No saved positions", 2f); return; }
        if (ApplyPosition(Notes.Bookmarks[^1])) Toast($"Loaded: {Notes.Bookmarks[^1].Name}", 2f);
    }
}
