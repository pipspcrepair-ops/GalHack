using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Decoration surfaces in real places: inside the forced place (--force=family), each dressed room
/// asked for (--rooms=office,quarters,mess) from its corner at eye height by day, a PNG of each
/// into --dir, and with --perf=1 what a frame costs standing there first (vsync off: the frame
/// time, and the renderer's own CPU and GPU time, averaged over five seconds).
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> DecoRooms()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        PoiSite site = Named();
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        if (p?.Memory.Rooms == null) { GD.PrintErr("decorooms: the place has no dressed rooms"); yield break; }
        GD.Print($"decorooms: {site.Family.Id} {site.Instance.State}, rooms {string.Join(", ", p.Memory.Rooms.Values.GroupBy(r => r.Room.RoomId).Select(g => $"{g.Key} {g.Count()}"))}");
        string dir = _args.GetValueOrDefault("dir", ".");
        _game.Visor.Visible = false;
        bool perf = _args.GetValueOrDefault("perf", "0") == "1";
        if (perf)
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Engine.MaxFps = 0;
            RenderingServer.ViewportSetMeasureRenderTime(_game.GetViewport().GetViewportRid(), true);
        }
        foreach (string want in _args.GetValueOrDefault("rooms", "office").Split(','))
        {
            PlaceRoom room = p.Memory.Rooms.Values.Where(r => r.Room.RoomId == want).OrderByDescending(r => r.Plan.Placements.Count - MathF.Abs(r.Corner.Y) * 2f).FirstOrDefault();
            if (room == null) { GD.Print($"decorooms: no {want} room"); continue; }
            var size = new Vector3(room.Room.Width, 0f, room.Room.Depth);
            Vector3 corner = p.ToWorld(new Vector3(room.Corner.X, room.Corner.Y, room.Corner.Z));
            Vector3 eye = corner + size + new Vector3(-0.5f, 1.55f, -0.5f), middle = corner + size * 0.5f + Vector3.Up * 1.55f;
            for (int i = 0; i < 16 && BlockInfo.Collides(p.Inst.Blocks.Get(p.CellAt(eye)).Type); i++) eye = eye.MoveToward(middle, 0.5f);
            _game.Survivor.PlaceOutside((eye + Vector3.Down * 1.55f).ToNumerics());
            foreach (int x in _drive.Wait(30)) yield return x;
            _game.Torch.Visible = true;
            foreach (int x in _drive.LookAt(corner + size * 0.25f + Vector3.Up * 0.6f, 30)) yield return x;
            foreach (int x in _drive.Wait(90)) yield return x;
            GD.Print($"decorooms: {want} {room.Room.Width}x{room.Room.Depth} m, {room.Plan.Placements.Count} pieces, {room.Room.State}");
            if (perf)
            {
                Rid vp = _game.GetViewport().GetViewportRid();
                double fps = 0, cpu = 0, gpu = 0;
                const int Ticks = 300;
                for (int i = 0; i < Ticks; i++)
                {
                    fps += Engine.GetFramesPerSecond();
                    cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp);
                    gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
                    yield return 0;
                }
                long draws = _game.GetViewport().GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame);
                GD.Print(string.Create(CultureInfo.InvariantCulture, $"DECOPERF {want}: frame {1000.0 / Math.Max(1.0, fps / Ticks):F2} ms ({fps / Ticks:F0} fps), render cpu {cpu / Ticks:F2} ms, gpu {gpu / Ticks:F2} ms, draws {draws}"));
            }
            foreach (int x in Shot(System.IO.Path.Combine(dir, $"place_{want}.png"))) yield return x;
        }
    }
}
