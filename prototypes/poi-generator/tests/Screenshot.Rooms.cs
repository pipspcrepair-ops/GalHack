using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// rooms:&lt;family&gt;:&lt;state&gt; spawns one fresh place of a family as a world lays it out (its own layout from the
/// spawn's seed; a house in a street's culture) and shows every room from its doorway at a standing eye height
/// (1.65 m), looking in, at the game's field of view, with a soft fill at the eye for the head torch: one tile a
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
        var rng = new Rng(_seed);
        int variant = rng.Range(0, f.Variants.Count);
        TechLevel? tech = f.Variants[variant].Tech switch { "low" => TechLevel.Low, "mid" => TechLevel.Mid, "high" => TechLevel.High, _ => null };
        HomeHints home = f.Tags.Contains("house") ? new HomeHints(rng.Range(0, 1 << 16), (Climate)rng.Range(0, 4), rng.Float()) : null;
        Layout layout = PoiFactory.SpawnLayout(lib, f, variant, f.Rarity, rng.NextU64(), new LayoutHints(Dir.North, tech, Home: home), out string why);
        if (layout == null) { GD.PrintErr($"no layout: {why}"); GalHack.Testing.Backstop.Quit(this, 1); return; }
        PoiInstance poi = PoiFactory.Spawn(lib, layout, state, rng.NextU64());
        _main.Show(poi);
        _main.HideHud(true);
        _main.Player.Lamp.Visible = false;
        GD.Print($"rooms: {f.Id} seed {_seed}, {layout.Name}, {state}, tech {tech?.ToString() ?? "any"}{(layout.Extras.Homes.FirstOrDefault() is { } h ? $", {h.Family} {h.Silhouette}" : "")}");

        // How deep each module lies from the way in, by the links: a room is shown from the door you come to it by.
        var depth = new Dictionary<int, int> { [layout.EntranceModule] = 0 };
        var queue = new Queue<int>(new[] { layout.EntranceModule });
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            foreach (Link k in layout.Links)
                if ((k.A == at ? k.B : k.B == at ? k.A : -1) is int next and >= 0 && depth.TryAdd(next, depth[at] + 1)) queue.Enqueue(next);
        }
        var rooms = layout.Modules.Where(m => m.Kind is ModuleKind.Room or ModuleKind.Hall && m.Cells.Count(c => c.Y == m.Min.Y) >= 2)
            .OrderBy(m => m.Storey).ThenBy(m => depth.GetValueOrDefault(m.Index, 99)).ToList();
        var tiles = new List<(Image Img, string Label)>();
        string stem = Path.Combine(Path.GetDirectoryName(sheetPath) ?? ".", Path.GetFileNameWithoutExtension(sheetPath));
        var fill = new OmniLight3D { LightColor = new Color("fff0dc"), LightEnergy = 0.6f, OmniRange = 22f, OmniAttenuation = 1.2f, ShadowEnabled = false };
        _main.Camera.AddChild(fill);
        int stray = 0;
        string firstStray = "";
        foreach (PlacedModule m in rooms)
        {
            if (DoorwayInto(poi, m, depth) is not var (centre, inward)) continue;
            var floor = m.Cells.Where(c => c.Y == m.Min.Y).ToList();
            Vector3 mid = new((float)floor.Average(c => c.X) + 0.5f, m.Min.Y + 1.1f, (float)floor.Average(c => c.Z) + 0.5f);
            Vector3 eye = centre - inward * 0.3f + Vector3.Up * EyeHeight;
            _main.Camera.GlobalTransform = new Transform3D(Basis.Identity, _main.Stage.ToGlobal(eye)).LookingAt(_main.Stage.ToGlobal(mid), Vector3.Up);
            _main.Camera.Fov = 75f;
            _main.FreezeCamera = true;
            await Frames(45);
            int w = floor.Max(c => c.X) - floor.Min(c => c.X) + 1, d = floor.Max(c => c.Z) - floor.Min(c => c.Z) + 1;
            string label = $"{m.Purpose} {Math.Min(w, d)}x{Math.Max(w, d)} m, {(m.Storey == 0 ? "ground" : $"storey {m.Storey}")}";
            Image img = await Grab();
            img.SavePng($"{stem}-{tiles.Count:00}-{m.Purpose}.png");
            tiles.Add((img, label));
            stray += Check(ref firstStray);
        }
        GD.Print($"rooms: {tiles.Count} rooms shown; geometry check {stray} stray{firstStray}");
        Sheet(tiles, $"{f.Name}, {state.ToString().ToLowerInvariant()}, seed {_seed}: every room from its doorway at eye height{(PoiView.Dressed ? "" : " (undressed)")}");
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Error result = GetViewport().GetTexture().GetImage().SavePng(sheetPath);
        GD.Print(result == Error.Ok ? $"saved {sheetPath}" : $"could not save {sheetPath}: {result}");
        GalHack.Testing.Backstop.Quit(this, result == Error.Ok && stray == 0 ? 0 : 1);
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
