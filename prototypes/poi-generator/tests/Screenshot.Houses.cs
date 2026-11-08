using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Proof frames of the house rules in daylight: houses built by the rules, set side by side on open ground and
/// seen from where a person would stand. --view=houses --out=&lt;folder&gt; writes them all: a street of one
/// culture, a terrace row, three regional families, odd one-offs and garage doors close up beside a 1.8 m figure.
/// Fresh seeds every run.
/// </summary>
public partial class Screenshot
{
    readonly List<Node3D> _row = new();

    async void HouseShots(string folder)
    {
        // A wall-clock limit: the run quits itself whatever happens.
        GetTree().CreateTimer(900.0, processAlways: true, processInPhysics: false, ignoreTimeScale: true).Timeout += () =>
        {
            GD.PrintErr("house shots timed out after 15 minutes");
            GetTree().Quit(2);
        };
        Directory.CreateDirectory(folder);
        var lib = _main.Library;
        var rand = new Random();
        _main.Inspect("flavour_house", 0, Rarity.Common, PoiState.Abandoned, 7);
        _main.HideHud(true);
        Node3D root = _main.Stage.GetParent<Node3D>();
        foreach (Node n in root.GetChildren()) if (n is Node3D n3) n3.Visible = false;
        root.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(1600, 1600) }, Position = new Vector3(0, -0.02f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.43f, 0.27f), Roughness = 1f },
        });
        FamilyDef fam = lib.Families.First(f => f.Id == "flavour_house");

        // Houses in a row along +x, fronts to -z (the street), each from the rules; returns their layouts and where each stands.
        List<(Layout L, float X)> Row(Func<int, LayoutHints> hints, int n, Func<Layout, bool>? keep = null, int tries = 400)
        {
            foreach (Node3D x in _row) x.QueueFree();
            _row.Clear();
            var made = new List<(Layout, float)>();
            float at = 0;
            for (int k = 0, t = 0; k < n && t < tries; t++)
            {
                Layout l = PoiFactory.BuildLayout(lib, fam, 1, Rarity.Common, (ulong)rand.NextInt64(), hints(k));
                if (keep != null && !keep(l)) continue;
                PoiInstance poi = PoiFactory.Spawn(lib, l, PoiState.Active, (ulong)rand.NextInt64());
                var view = PoiView.Build(poi, Biome.Steppe, new PoiView.Options(Collision: false, Figures: false), (_, _) => new Color(0.36f, 0.43f, 0.27f));
                float x0 = l.Occupied.Min.X;
                view.Position = new Vector3(at - x0, 0, 0);
                root.AddChild(view);
                _row.Add(view);
                made.Add((l, at - x0));
                at += l.Occupied.Size.X - 6;
                k++;
            }
            return made;
        }

        async System.Threading.Tasks.Task Shot(string name, Vector3 eye, Vector3 look, int settle = 90)
        {
            _main.Sky.Apply(Look.Day, 260f);
            _main.Camera.GlobalTransform = new Transform3D(Basis.Identity, eye).LookingAt(look, Vector3.Up);
            _main.FreezeCamera = true;
            await Frames(settle);
            await ToSignal(RenderingServer.Singleton, "frame_post_draw");
            Image img = GetViewport().GetTexture().GetImage();
            string path = Path.Combine(folder, name + ".png");
            Error e = img.SavePng(path);
            GD.Print(e == Error.Ok ? $"saved {path}" : $"could not save {path}: {e}");
        }

        string Says(IEnumerable<(Layout L, float X)> row) => string.Join(" | ", row.Select(r => string.Join(", ", r.L.Extras.Homes.Select(h => $"{h.Family} {h.Silhouette}"))));

        // A suburban street: one culture's houses, mid tech, temperate, seen from the street's edge looking along it.
        // A culture whose main family is a suburban one.
        int culture = rand.Next();
        for (int k = 0; k < 400 && HomeCulture.Of(lib.Homes, new HomeHints(culture, Climate.Temperate, 0.6f), TechLevel.Mid).Families[0].Family.Id is not ("suburban" or "bungalow"); k++) culture = rand.Next();
        var street = Row(_ => new LayoutHints(Dir.North, TechLevel.Mid, null, new HomeHints(culture, Climate.Temperate, 0.6f)), 7);
        GD.Print($"street: {Says(street)}");
        float end = street[^1].X + 30;
        await Shot("street", new Vector3(end * 0.12f, 2.2f, -17), new Vector3(end * 0.5f, 3.5f, 6));
        await Shot("street-across", new Vector3(end * 0.3f, 8f, -34), new Vector3(end * 0.3f, 4f, 14));

        // Another culture, another climate: the same rules, a different place.
        int culture2 = rand.Next();
        var street2 = Row(_ => new LayoutHints(Dir.North, (TechLevel)rand.Next(3), null, new HomeHints(culture2, rand.Next(2) == 0 ? Climate.Hot : Climate.Wet, 0.5f)), 6);
        GD.Print($"street 2: {Says(street2)}");
        await Shot("street-other", new Vector3((street2[^1].X + 30) * 0.12f, 2.2f, -17), new Vector3((street2[^1].X + 30) * 0.5f, 3.5f, 6));

        // A row of terraces.
        var terrace = Row(_ => new LayoutHints(Dir.North, TechLevel.Mid, null, new HomeHints(rand.Next(), Climate.Temperate, 0.5f, "terrace")), 2);
        GD.Print($"terrace: {Says(terrace)}");
        float tw = terrace.Sum(t => t.L.Occupied.Size.X - 6);
        await Shot("terrace", new Vector3(tw * 0.2f, 3f, -24), new Vector3(tw * 0.45f, 5f, 10));

        // Three regional families, each in its own climate.
        foreach (var (family, climate, tech) in new[] { ("chalet", Climate.Cold, TechLevel.Mid), ("stilt", Climate.Wet, TechLevel.Low), ("courtyard", Climate.Hot, TechLevel.Mid), ("adobe", Climate.Hot, TechLevel.Low), ("canal", Climate.Temperate, TechLevel.Low), ("eaves", Climate.Temperate, TechLevel.Mid) })
        {
            var one = Row(_ => new LayoutHints(Dir.North, tech, null, new HomeHints(rand.Next(), climate, 0.6f, family)), 3);
            GD.Print($"{family}: {Says(one)}");
            float w = one.Sum(t => t.L.Occupied.Size.X - 6);
            await Shot($"family-{family}", new Vector3(w * 0.5f - 10, 4f, -26), new Vector3(w * 0.5f, 5f, 12));
        }

        // The odd one-offs.
        foreach (string odd in new[] { "watertower", "mushroom", "saucer", "wavehouse", "hillside", "glasshell", "pod" })
        {
            var one = Row(_ => new LayoutHints(Dir.North, TechLevel.High, null, new HomeHints(rand.Next(), Climate.Temperate, 0.6f, odd)), 1,
                l => l.Extras.Homes.Any(h => h.Family == odd));
            if (one.Count == 0) continue;
            GD.Print($"odd {odd}: {Says(one)}");
            var b = one[0].L.Occupied;
            float cx = one[0].X + b.Min.X + b.Size.X * 0.5f;
            await Shot($"odd-{odd}", new Vector3(cx - 14, 3f, -30), new Vector3(cx, 6f, b.Min.Z + b.Size.Z * 0.5f));
        }

        // Garage doors close up: one of each kind, a 1.8 m figure beside each.
        foreach (string kind in new[] { "panel", "ribbed", "glazed", "timber", "hinged", "carport" })
        {
            var one = Row(_ => new LayoutHints(Dir.North, kind is "timber" or "hinged" ? TechLevel.Low : TechLevel.Mid, null, new HomeHints(rand.Next(), Climate.Temperate, 0.8f, kind is "timber" or "hinged" ? "bungalow" : "suburban")), 1,
                l => l.Extras.Homes.Any(h => h.GarageDoor == kind && h.DoorWide > 0), 600);
            if (one.Count == 0) { GD.Print($"no {kind} garage came up"); continue; }
            HomeNote h = one[0].L.Extras.Homes.First(x => x.GarageDoor == kind);
            GD.Print($"garage {kind} {h.DoorWide} m: {Says(one)}");
            Vector3 door = new Vector3(one[0].X + h.DoorCell.X, 0, h.DoorCell.Z) + new Vector3(h.DoorWide * 0.5f, 0, 0);
            var person = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.25f, Height = 1.8f }, Position = door + new Vector3(-h.DoorWide * 0.5f - 0.8f, 0.9f, -1.2f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.35f, 0.2f) } };
            root.AddChild(person);
            await Shot($"garage-{kind}", door + new Vector3(-4f, 1.7f, -10f), door + new Vector3(0, 1.6f, 0));
            person.QueueFree();
        }

        // Front doors close up, a 1.8 m figure beside each: a full storey tall, framed (where at all) in the house's own trim.
        for (int n = 0; n < 2; n++)
        {
            var one = Row(_ => new LayoutHints(Dir.North, TechLevel.Mid, null, new HomeHints(rand.Next(), Climate.Temperate, 0.6f, n == 0 ? "suburban" : "cottage")), 1);
            Doorway d = one[0].L.Doorways.FirstOrDefault(x => x.ModuleB == one[0].L.EntranceModule || x.ModuleA == one[0].L.EntranceModule);
            if (d == null) continue;
            Int3 lo = d.Cells.Aggregate(Int3.Min);
            GD.Print($"door {n}: {Says(one)}, {d.Cells.Count} cells, {d.Cells.Max(c => c.Y) - lo.Y + 1} m tall");
            Vector3 door = new Vector3(one[0].X + lo.X + 1f, 0, lo.Z);
            var person = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.25f, Height = 1.8f }, Position = door + new Vector3(-2.2f, 0.9f, -1.0f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.35f, 0.2f) } };
            root.AddChild(person);
            await Shot($"door-{n}", door + new Vector3(-3f, 1.7f, -7.5f), door + new Vector3(0, 1.5f, 0));
            person.QueueFree();
        }
        GetTree().Quit(0);
    }
}
