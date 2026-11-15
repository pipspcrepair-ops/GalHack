using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// rooms:&lt;family&gt;:&lt;state&gt; spawns one fresh place of a family as a world lays it out (its own layout from the
/// spawn's seed; a house in a street's culture) and shows every room from just inside its doorway at a standing eye
/// height (1.65 m), looking in, at the game's field of view, with a soft fill at the eye for the head torch: one tile a
/// room on a sheet (--out), and each tile on its own beside it. Dressed by the decorations library as the game
/// dresses it; --undressed shows the generator's own props. --seed=N replays a spawn (the before and after of a
/// change); the room's purpose and size label each tile.
/// </summary>
public partial class Screenshot
{
    const float EyeHeight = 1.65f;

    async void RoomShots(string view, string sheetPath)
    {
        string[] a = view.Split(':');
        var lib = _main.Library;
        FamilyDef f = lib.Families.First(x => x.Id == a[1]);
        PoiState state = Enum.Parse<PoiState>(a[2], true);
        if (OS.GetCmdlineUserArgs().Contains("--undressed")) PoiView.Dressed = false;
        // --purpose=X: that room of eight places from the seed on (the same rooms before and after a change), else every room of one.
        string purpose = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--purpose="))?["--purpose=".Length..];
        var tiles = new List<(Image Img, string Label)>();
        string stem = Path.Combine(Path.GetDirectoryName(sheetPath) ?? ".", Path.GetFileNameWithoutExtension(sheetPath));
        var fill = new OmniLight3D { LightColor = new Color("fff0dc"), LightEnergy = 0.6f, OmniRange = 22f, OmniAttenuation = 1.2f, ShadowEnabled = false };
        _main.Camera.AddChild(fill);
        int stray = 0;
        string firstStray = "";
        for (ulong place = 0; place < (purpose != null ? 8UL : 1UL); place++)
        {
            var rng = new Rng(_seed + place);
            int variant = rng.Range(0, f.Variants.Count);
            TechLevel? tech = f.Variants[variant].Tech switch { "low" => TechLevel.Low, "mid" => TechLevel.Mid, "high" => TechLevel.High, _ => null };
            HomeHints home = f.Tags.Contains("house") ? new HomeHints(rng.Range(0, 1 << 16), (Climate)rng.Range(0, 4), rng.Float()) : null;
            Layout layout = PoiFactory.SpawnLayout(lib, f, variant, f.Rarity, rng.NextU64(), new LayoutHints(Dir.North, tech, Home: home), out string why);
            if (layout == null) { GD.PrintErr($"no layout: {why}"); continue; }
            PoiInstance poi = PoiFactory.Spawn(lib, layout, state, rng.NextU64());
            _main.Show(poi);
            _main.HideHud(true);
            _main.Player.Lamp.Visible = false;
            GD.Print($"rooms: {f.Id} seed {_seed + place}, {layout.Name}, {state}, tech {tech?.ToString() ?? "any"}{(layout.Extras.Homes.FirstOrDefault() is { } h ? $", {h.Family} {h.Silhouette}" : "")}");

            // How deep each module lies from the way in, by the links: a room is shown from the door you come to it by.
            var depth = new Dictionary<int, int> { [layout.EntranceModule] = 0 };
            var queue = new Queue<int>(new[] { layout.EntranceModule });
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                foreach (Link k in layout.Links)
                    if ((k.A == at ? k.B : k.B == at ? k.A : -1) is int next and >= 0 && depth.TryAdd(next, depth[at] + 1)) queue.Enqueue(next);
            }
            var rooms = layout.Modules.Where(m => m.Kind is ModuleKind.Room or ModuleKind.Hall && m.Cells.Count(c => c.Y == m.Min.Y) >= 2 && (purpose == null || m.Purpose == purpose))
                .OrderBy(m => m.Storey).ThenBy(m => depth.GetValueOrDefault(m.Index, 99)).Take(purpose != null ? 1 : int.MaxValue).ToList();
            // --rooms=N: at most N tiles, every purpose once before any twice (a tower's twenty floors on one sheet).
            if (OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--rooms=")) is { } cap)
                rooms = rooms.Select((m, k) => (m, k, n: rooms.Take(k).Count(o => o.Purpose == m.Purpose))).OrderBy(t => t.n).ThenBy(t => t.k)
                    .Take(int.Parse(cap["--rooms=".Length..])).OrderBy(t => t.k).Select(t => t.m).ToList();
            foreach (PlacedModule m in rooms)
            {
                var floor = m.Cells.Where(c => c.Y == m.Min.Y).ToList();
                // (A floor come up to by its stair has no doorway: from the corner of it nearest the stair's.)
                var door = DoorwayInto(poi, m, depth);
                if ((door ?? Corner(poi, m, floor)) is not var (centre, inward)) continue;
                // Just over the threshold, clear of the door's leaf, as a person stands coming in; looking at the room's middle (from a stair's corner, down the floor).
                Vector3 eye = centre + inward * 0.75f + Vector3.Up * EyeHeight;
                Vector3 mid = door != null ? new((float)floor.Average(c => c.X) + 0.5f, m.Min.Y + 1.1f, (float)floor.Average(c => c.Z) + 0.5f) : eye + inward * 8f + Vector3.Down * 0.8f;
                _main.Camera.GlobalTransform = new Transform3D(Basis.Identity, _main.Stage.ToGlobal(eye)).LookingAt(_main.Stage.ToGlobal(mid), Vector3.Up);
                _main.Camera.Fov = 75f;
                _main.FreezeCamera = true;
                await Frames(45);
                int w = floor.Max(c => c.X) - floor.Min(c => c.X) + 1, d = floor.Max(c => c.Z) - floor.Min(c => c.Z) + 1;
                string label = $"{m.Purpose} {Math.Min(w, d)}x{Math.Max(w, d)} m, {(m.Storey == 0 ? "ground" : $"storey {m.Storey}")}{(purpose != null ? $", seed {_seed + place}" : "")}";
                Image img = await Grab();
                img.SavePng($"{stem}-{tiles.Count:00}-{m.Purpose}.png");
                tiles.Add((img, label));
                stray += Check(ref firstStray);
            }
        }        GD.Print($"rooms: {tiles.Count} rooms shown; geometry check {stray} stray{firstStray}");
        Sheet(tiles, $"{f.Name}, {state.ToString().ToLowerInvariant()}, seed {_seed}: {(purpose != null ? $"the {purpose} of eight places" : "every room")} from its doorway at eye height{(PoiView.Dressed ? "" : " (undressed)")}");
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Error result = GetViewport().GetTexture().GetImage().SavePng(sheetPath);
        GD.Print(result == Error.Ok ? $"saved {sheetPath}" : $"could not save {sheetPath}: {result}");
        GalHack.Testing.Backstop.Quit(this, result == Error.Ok && stray == 0 ? 0 : 1);
    }

    /// <summary>A floor cell of a room nearest the stair it is come up to (else its first), and the way from there toward its middle.</summary>
    static (Vector3 Centre, Vector3 Inward)? Corner(PoiInstance poi, PlacedModule m, List<Int3> floor)
    {
        if (floor.Count == 0) return null;
        var stairs = poi.Layout.Links.Select(k => k.A == m.Index ? k.B : k.B == m.Index ? k.A : -1).Where(i => i >= 0 && poi.Layout.Modules[i].Kind == ModuleKind.Stair)
            .SelectMany(i => poi.Layout.Modules[i].Cells).ToList();
        Int3 at = stairs.Count > 0 ? floor.MinBy(c => stairs.Min(s => Math.Abs(s.X - c.X) + Math.Abs(s.Z - c.Z))) : floor[0];
        // Looking down the longest clear run of its floor from there (a floor round a core is seen along it, not into the core).
        var own = floor.ToHashSet();
        bool Seen(Int3 c) => Enumerable.Range(1, 20).All(k => own.Contains(new Int3((int)MathF.Round(at.X + (c.X - at.X) * k / 20f), at.Y, (int)MathF.Round(at.Z + (c.Z - at.Z) * k / 20f))));
        if (floor.OrderByDescending(c => Math.Abs(c.X - at.X) + Math.Abs(c.Z - at.Z)).Where(Seen).Select(c => (Int3?)c).FirstOrDefault() is not Int3 far || far == at) return null;
        Vector3 mid = new(far.X + 0.5f, at.Y, far.Z + 0.5f), from = new(at.X + 0.5f, at.Y, at.Z + 0.5f);
        return (from - (mid - from).Normalized() * 0.75f, (mid - from).Normalized());
    }

    /// <summary>The middle of the way a room is come into (from the shallowest room beside it) at its floor, and the way into the room; null if it has none.</summary>
    static (Vector3 Centre, Vector3 Inward)? DoorwayInto(PoiInstance poi, PlacedModule m, Dictionary<int, int> depth)
    {
        var own = new HashSet<Int3>(m.Cells);
        foreach (var (cells, _) in RoomDecor.Ways(poi, m, m.Min.Y).OrderBy(w => depth.GetValueOrDefault(w.From, w.From < 0 ? 0 : 99)))
            foreach (Dir h in Dirs.Horizontal)
                if (cells.Any(c => own.Contains(c + h.Vec())))
                {
                    Int3 v = h.Vec();
                    return (new Vector3((float)cells.Average(c => c.X) + 0.5f, m.Min.Y, (float)cells.Average(c => c.Z) + 0.5f), new Vector3(v.X, 0, v.Z));
                }
        return null;
    }
}
