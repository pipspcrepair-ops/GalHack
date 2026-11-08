using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Construction;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The block detail proof, frame by frame into --dir, every name led by --tag (before, after), in
/// daylight: every device close up on each grid (device-ID-large, device-ID-small), a wall of each
/// armour material at 2 m and 20 m (wall-ID-2m, wall-ID-20m), the Outpost and Frigate set down
/// through the BLUEPRINTS tab and seen from outside and from inside (bp-NAME-outside, bp-NAME-inside),
/// a PERF line of frame time and triangles at the Frigate, the first devices powered, unpowered and
/// damaged (status-*), and an icon sheet of every block item (icons). --part=devices|walls|blueprints|icons runs one part; the default is all of them.
/// The showrooms are set down whole (STAGING): one large-grid base and one small-grid plate.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> BlockDetailView()
    {
        string dir = _args.TryGetValue("dir", out string d) ? d : "user://blockdetail";
        string tag = _args.TryGetValue("tag", out string tg) ? tg + "-" : "";
        string part = _args.TryGetValue("part", out string p) ? p : "all";
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(dir));
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        _game.Mode = GameMode.Creative;
        _game.Visor.Visible = false;
        if (part is "all" or "devices") foreach (int x in DeviceShots(dir, tag)) yield return x;
        if (part is "all" or "walls") foreach (int x in WallShots(dir, tag)) yield return x;
        if (part is "all" or "blueprints") foreach (int x in DetailBlueprints(dir, tag)) yield return x;
        if (part is "all" or "icons") foreach (int x in IconSheet(dir, tag)) yield return x;
        _game.Visor.Visible = true;
    }

    /// <summary>Every component on a showroom floor, one grid at a time, each shot from its front quarter.</summary>
    IEnumerable<int> DeviceShots(string dir, string tag)
    {
        Catalogue cat = _game.Zone.Data.Catalogue;
        List<BlockType> devices = cat.Types.Skip(1).Where(t => !t.IsArmor).ToList();
        foreach (GridSize size in new[] { GridSize.Large, GridSize.Small })
        {
            if (size == GridSize.Small && _args.TryGetValue("grid", out string g) && g == "large") continue;
            if (size == GridSize.Large && _args.TryGetValue("grid", out string g2) && g2 == "small") continue;
            GridDef grid = cat.Grid(size);
            var s = new Structure(cat, grid) { Name = $"Showroom {size}" };
            const int perRow = 8, gap = 3;
            int rows = (devices.Count + perRow - 1) / perRow;
            BlockType steel = cat.Type("steel");
            for (int x = -1; x <= perRow * gap; x++)
                for (int z = -2; z <= rows * gap; z++)
                    s.Set(new Int3(x, 0, z), Building.Make(s, steel));
            var at = new Dictionary<BlockType, Int3>();
            for (int i = 0; i < devices.Count; i++)
            {
                BlockType t = devices[i];
                var c = new Int3(1 + (i % perRow) * gap, 1, (i / perRow) * gap);
                // Devices that hang from something get it: a light and a ladder on a wall behind, a hover plate and a leg under a block.
                if (t.IsLight || t.IsLadder) s.Set(c + new Int3(0, 0, 1), Building.Make(s, steel));
                if (t.Function is "hover" or "gear")
                {
                    c += new Int3(0, 1, 0);
                    s.Set(c + new Int3(0, 1, 0), Building.Make(s, steel));
                }
                s.Set(c, Building.Make(s, t));
                at[t] = c;
            }
            s.DirtyChunks.Clear();
            float cell = grid.CellSize;
            float span = (perRow * gap + 2) * cell;
            Vector3 site = Play.FlatSpot(_game.Start, 60f + (size == GridSize.Small ? 40f : 0f), 0.4f, 0.5f, span: MathF.Max(10f, span * 0.6f));
            _game.Survivor.PlaceOutside((site + new Vector3(-span * 0.5f - 6f, 1f, -8f)).ToNumerics());
            foreach (int x in _drive.Wait(30)) yield return x;
            Vector3 corner = site - new Vector3(span * 0.5f, 0f, rows * gap * cell * 0.5f);
            Fell(site, span * 0.75f + 4f);
            Vessel v = Vessel.SpawnBase(_game.Zone, s, (corner + Vector3.Up * (cell * 0.5f + 0.05f)).ToNumerics(), 0f);
            foreach (var (c, _) in s.Blocks().ToList()) v.ClearGround(c);
            foreach (int x in _drive.Wait(60)) yield return x;
            string grid0 = size == GridSize.Large ? "large" : "small";
            foreach (BlockType t in devices)
            {
                Int3 c = at[t];
                var centre = new N.Vector3(c.X, c.Y, c.Z) * cell;
                // Front quarter (the front is -Z), a little above; a thruster from behind, its nozzle; a hover plate from below its edge.
                float up = t.Function is "hover" ? -0.35f : 0.75f, side = t.IsThruster ? 1f : -1f;
                var eyeLocal = centre + new N.Vector3(t.IsThruster ? 1.7f : 1.15f, up, (t.IsThruster ? 1.5f : 2.05f) * side) * cell;
                Vector3 eye = v.ToWorld(eyeLocal), look = v.ToWorld(centre + new N.Vector3(0f, t.Function == "hover" ? -0.1f : 0f, 0f));
                _game.FixedCamera = new Transform3D(Basis.LookingAt((look - eye).Normalized(), Vector3.Up), eye);
                foreach (int x in _drive.Wait(4)) yield return x;
                foreach (int x in Shot($"{dir}/{tag}device-{t.Id}-{grid0}.png")) yield return x;
            }
            // The whole showroom at a glance.
            var mid = new N.Vector3(perRow * gap * 0.5f, 1f, rows * gap * 0.5f) * cell;
            Vector3 over = v.ToWorld(mid + new N.Vector3(0f, rows * gap * 0.9f, -rows * gap * 1.2f) * cell);
            _game.FixedCamera = new Transform3D(Basis.LookingAt((v.ToWorld(mid) - over).Normalized(), Vector3.Up), over);
            foreach (int x in _drive.Wait(6)) yield return x;
            foreach (int x in Shot($"{dir}/{tag}showroom-{grid0}.png")) yield return x;
            if (size == GridSize.Large) foreach (int x in StatusShots(v, devices.Take(4).Select(t => at[t]).ToList(), dir, tag)) yield return x;
            _game.FixedCamera = null;
        }
    }

    /// <summary>The first devices of the showroom's front row as their status shows: powered, with the power off, then damaged (one bucket apiece, the glows flickering).</summary>
    IEnumerable<int> StatusShots(Vessel v, List<Int3> cells, string dir, string tag)
    {
        Structure s = v.Structure;
        float cell = s.Grid.CellSize;
        var lo = new N.Vector3(cells.Min(c => c.X), 1f, cells.Min(c => c.Z)) * cell;
        var hi = new N.Vector3(cells.Max(c => c.X), 1f, cells.Max(c => c.Z)) * cell;
        N.Vector3 mid = (lo + hi) * 0.5f;
        Vector3 eye = v.ToWorld(mid + new N.Vector3(2.5f, 1.6f, -5.5f) * cell);
        _game.FixedCamera = new Transform3D(Basis.LookingAt((v.ToWorld(mid) - eye).Normalized(), Vector3.Up), eye);
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Shot($"{dir}/{tag}status-powered.png")) yield return x;
        v.PowerOn = false;
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (int x in Shot($"{dir}/{tag}status-unpowered.png")) yield return x;
        v.PowerOn = true;
        float[] left = { 0.7f, 0.45f, 0.4f, 0.6f };
        for (int i = 0; i < cells.Count; i++) s.SetHp(cells[i], s.MaxHp(s.Get(cells[i])) * left[i % left.Length]);
        v.View.Rebuild(s.DirtyChunks.ToList());
        s.DirtyChunks.Clear();
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (int x in Shot($"{dir}/{tag}status-damaged.png")) yield return x;
        foreach (Int3 c in cells) s.SetHp(c, s.MaxHp(s.Get(c)));
        v.View.Rebuild(s.DirtyChunks.ToList());
        s.DirtyChunks.Clear();
    }

    /// <summary>A free-standing wall of each armour material, 6 by 4 blocks, at 2 m and at 20 m.</summary>
    IEnumerable<int> WallShots(string dir, string tag)
    {
        Catalogue cat = _game.Zone.Data.Catalogue;
        GridDef grid = cat.Grid(GridSize.Large);
        float cell = grid.CellSize;
        int n = 0;
        foreach (BlockType t in cat.Types.Skip(1).Where(t => t.IsArmor))
        {
            var s = new Structure(cat, grid) { Name = $"Wall {t.Id}" };
            for (int x = 0; x < 6; x++)
                for (int y = 0; y < 4; y++)
                    s.Set(new Int3(x, y, 0), Building.Make(s, t));
            s.DirtyChunks.Clear();
            Vector3 site = Play.FlatSpot(_game.Start, 140f + 25f * n, 1.1f + n * 0.7f, 0.6f, span: 10f);
            n++;
            _game.Survivor.PlaceOutside((site + new Vector3(0f, 1f, -24f)).ToNumerics());
            foreach (int x in _drive.Wait(30)) yield return x;
            Vector3 corner = site - new Vector3(3f * cell, 0f, 0f);
            Fell(site, 9f);
            Fell(site + new Vector3(0f, 0f, -12f), 9f);
            Fell(site + new Vector3(0f, 0f, -22f), 8f);
            Vessel v = Vessel.SpawnBase(_game.Zone, s, (corner + Vector3.Up * (cell * 0.5f - 0.2f)).ToNumerics(), 0f);
            foreach (var (c, _) in s.Blocks().ToList()) v.ClearGround(c);
            foreach (int x in _drive.Wait(40)) yield return x;
            // The face towards -Z, centred on the seam between two blocks.
            var face = new N.Vector3(2.5f * cell, 1.5f * cell, -0.5f * cell);
            foreach (float range in new[] { 2f, 20f })
            {
                var eyeLocal = face + new N.Vector3(range * 0.35f, 0f, -range);
                Vector3 eye = v.ToWorld(eyeLocal);
                eye.Y = MathF.Max(eye.Y, _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f);
                Vector3 look = v.ToWorld(face + new N.Vector3(range < 5f ? 0.4f : 0f, 0f, 0f));
                _game.FixedCamera = new Transform3D(Basis.LookingAt((look - eye).Normalized(), Vector3.Up), eye);
                foreach (int x in _drive.Wait(6)) yield return x;
                foreach (int x in Shot($"{dir}/{tag}wall-{t.Id}-{range:0}m.png")) yield return x;
            }
            _game.FixedCamera = null;
        }
    }

    /// <summary>The Outpost and the Frigate, set down through the tab's own PLACE, seen from outside and inside; a PERF line at the Frigate.</summary>
    IEnumerable<int> DetailBlueprints(string dir, string tag)
    {
        BlueprintsSection tab = _game.Menu.Root.Blueprints;
        int n = 0;
        foreach (string file in new[] { "outpost.gbp", "frigate.gbp" })
        {
            LibraryEntry e = tab.Library.Starters.FirstOrDefault(x => x.File == file);
            if (e == null) { GD.Print($"(no starter {file})"); continue; }
            Vessel v = null;
            for (int attempt = 0; attempt < 4 && v == null; attempt++, n++)
            {
                float radius = 0.5f * new Vector2(e.Size.X, e.Size.Z).Length();
                Vector3 spot = Play.FlatSpot(_game.Start, 260f + 70f * n, 2.2f + n * 1.3f, 0.5f + attempt * 0.5f, span: MathF.Max(14f, radius));
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
                foreach (int x in Play.CloseMenu()) yield return x;
                if (v == null) GD.Print($"({e.Name} would not go down near {spot}: {tab.Why})");
            }
            if (v == null) continue;
            foreach (int x in _drive.Wait(60)) yield return x;
            string name = Slug(e.Name);
            foreach (int x in Outside(v)) yield return x;
            foreach (int x in Shot($"{dir}/{tag}bp-{name}-outside.png")) yield return x;
            if (file == "frigate.gbp") foreach (int x in MeasureFrames(v, $"{tag}frigate outside")) yield return x;
            if (Inside(v) is Transform3D inside)
            {
                // Sealed rooms are dark by day: the head torch, as a player would.
                _game.FixedCamera = inside;
                _game.Survivor.PlaceOutside((inside.Origin + Vector3.Down * 1.55f).ToNumerics());
                _game.Torch.Visible = true;
                foreach (int x in _drive.Wait(20)) yield return x;
                _game.FixedCamera = inside;
                foreach (int x in _drive.Wait(4)) yield return x;
                foreach (int x in Shot($"{dir}/{tag}bp-{name}-inside.png")) yield return x;
                if (file == "frigate.gbp") foreach (int x in MeasureFrames(v, $"{tag}frigate inside")) yield return x;
                _game.Torch.Visible = false;
            }
            _game.FixedCamera = null;
        }
    }

    /// <summary>Fells every plant within a radius of a point (a photographer's clearing for a showroom).</summary>
    void Fell(Vector3 centre, float radius)
    {
        var streamer = _game.Zone.Streamer;
        var ids = new HashSet<long>();
        for (float x = -radius; x <= radius; x += 3f)
            for (float z = -radius; z <= radius; z += 3f)
                if (x * x + z * z <= radius * radius)
                    foreach (var plant in streamer.FloraNear(centre + new Vector3(x, 0f, z), 3.5f)) ids.Add(plant.Id);
        foreach (long id in ids)
        {
            _game.Zone.Planet.Felled.Add(id);
            streamer.Fell(id);
        }
    }

    /// <summary>
    /// A camera inside a structure: the empty cell most closed in on every side (a room), looking
    /// along its longest open way, level, at a person's eye height.
    /// </summary>
    static Transform3D? Inside(Vessel v)
    {
        Structure s = v.Structure;
        var (min, max) = s.Bounds();
        float cell = s.Grid.CellSize;
        int Reach(Int3 c, Int3 step)
        {
            for (int k = 1; k < 40; k++)
            {
                Int3 q = c + new Int3(step.X * k, step.Y * k, step.Z * k);
                if (q.X < min.X || q.Y < min.Y || q.Z < min.Z || q.X > max.X || q.Y > max.Y || q.Z > max.Z) return -1;
                if (!s.Get(q).IsEmpty) return k;
            }
            return -1;
        }
        Int3[] sides = { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };
        Int3 best = default;
        Int3 way = default;
        int bestScore = -1;
        for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
                for (int z = min.Z; z <= max.Z; z++)
                {
                    var c = new Int3(x, y, z);
                    if (!s.Get(c).IsEmpty) continue;
                    int floor = Reach(c, new Int3(0, -1, 0)), roof = Reach(c, new Int3(0, 1, 0));
                    if (floor != 1 || roof < 1) continue;
                    int score = 0, longest = 0;
                    Int3 dir0 = sides[0];
                    bool closed = true;
                    foreach (Int3 side in sides)
                    {
                        int r = Reach(c, side);
                        if (r < 0) { closed = false; break; }
                        if (r > longest) { longest = r; dir0 = side; }
                        score += r;
                    }
                    if (!closed || longest < 3) continue;
                    if (score > bestScore) { bestScore = score; best = c; way = dir0; }
                }
        if (bestScore < 0) return null;
        // From the back of the room, a little off the wall, at eye height.
        var back = new N.Vector3(best.X - way.X * 0.4f, best.Y - 0.5f, best.Z - way.Z * 0.4f) * cell + new N.Vector3(0f, MathF.Min(1.6f, cell * 0.8f), 0f);
        var ahead = back + new N.Vector3(way.X, -0.12f, way.Z) * cell * 4f;
        Vector3 eye = v.ToWorld(back), look = v.ToWorld(ahead);
        return new Transform3D(Basis.LookingAt((look - eye).Normalized(), Vector3.Up), eye);
    }

    /// <summary>Two seconds of frames as they really are (vsync off, no cap): frame time, the renderer's CPU and GPU time, triangles drawn.</summary>
    IEnumerable<int> MeasureFrames(Vessel v, string label)
    {
        var meter = new FrameMeter();
        _game.AddChild(meter);
        DisplayServer.VSyncMode was = DisplayServer.WindowGetVsyncMode();
        int maxFps = Engine.MaxFps;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        foreach (int x in _drive.Wait(30)) yield return x;
        meter.Start();
        foreach (int x in _drive.Wait(150)) yield return x;
        meter.Stop();
        DisplayServer.WindowSetVsyncMode(was);
        Engine.MaxFps = maxFps;
        long tris = 0;
        foreach (Node node in v.View.GetChildren())
            if (node is MeshInstance3D m && m.Mesh is ArrayMesh am)
                for (int i = 0; i < am.GetSurfaceCount(); i++)
                    tris += am.SurfaceGetArrayIndexLen(i) / 3;
        GD.Print($"PERF {label}: {v.Structure.Count} blocks, {tris} structure triangles; {meter.Report()}");
        meter.QueueFree();
    }

    sealed partial class FrameMeter : Node
    {
        readonly List<double> _frame = new(), _cpu = new(), _gpu = new();
        readonly List<long> _prims = new();
        readonly Stopwatch _clock = new();
        bool _on;

        public void Start()
        {
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
            _on = true;
            _clock.Restart();
        }

        public void Stop() => _on = false;

        public override void _Process(double delta)
        {
            if (!_on) return;
            _frame.Add(_clock.Elapsed.TotalMilliseconds);
            _clock.Restart();
            Rid vp = GetViewport().GetViewportRid();
            _cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp));
            _gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
            _prims.Add((long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame));
        }

        static double P(List<double> l, double q)
        {
            if (l.Count == 0) return 0;
            var s = l.OrderBy(x => x).ToList();
            return s[Math.Min(s.Count - 1, (int)(q * s.Count))];
        }

        public string Report() => _frame.Count < 3 ? "no frames"
            : $"{_frame.Count - 1} frames, frame avg {_frame.Skip(1).Average():0.00} ms p95 {P(_frame.Skip(1).ToList(), 0.95):0.00} ms, " +
              $"render cpu {_cpu.Average():0.00} ms, gpu avg {_gpu.Average():0.00} ms p95 {P(_gpu, 0.95):0.00} ms, primitives {_prims.Average():0}";
    }

    /// <summary>Every block item's icon on one sheet, large grid then small, ten to a row.</summary>
    IEnumerable<int> IconSheet(string dir, string tag)
    {
        ItemIcons icons = _game.Icons;
        List<ItemDef> blocks = _game.Zone.Data.Items.Items.Where(i => i.IsBlock && _game.Zone.Data.Catalogue.TryGetType(i.BlockId, out _))
            .OrderBy(i => i.SmallGrid).ThenBy(i => _game.Zone.Data.Catalogue.Type(i.BlockId).IsArmor ? 0 : 1).ThenBy(i => i.BlockId, StringComparer.Ordinal).ToList();
        foreach (ItemDef i in blocks) icons.Get(i);
        for (int t = 0; t < 900 && blocks.Any(i => icons.Get(i) == null); t++) yield return 0;
        const int cols = 10, size = ItemIcons.Size, pad = 6;
        int rows = (blocks.Count + cols - 1) / cols;
        Image sheet = Image.CreateEmpty(cols * (size + pad) + pad, rows * (size + pad) + pad, false, Image.Format.Rgba8);
        sheet.Fill(new Color(0.16f, 0.17f, 0.2f));
        for (int k = 0; k < blocks.Count; k++)
        {
            Texture2D tex = icons.Get(blocks[k]);
            if (tex == null) continue;
            Image img = tex.GetImage();
            img.Convert(Image.Format.Rgba8);
            sheet.BlendRect(img, new Rect2I(0, 0, size, size), new Vector2I(pad + (k % cols) * (size + pad), pad + (k / cols) * (size + pad)));
        }
        string file = ProjectSettings.GlobalizePath($"{dir}/{tag}icons.png");
        GD.Print(sheet.SavePng(file) == Error.Ok ? $"saved {file} ({blocks.Count} icons)" : $"could not save {file}");
    }
}
