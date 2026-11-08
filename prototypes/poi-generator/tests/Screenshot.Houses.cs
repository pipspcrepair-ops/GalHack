using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Proof frames of the house rules in daylight: houses built by the rules, their lots set side by side lot line to
/// lot line on open ground and seen from where a person would stand. --view=houses --out=&lt;folder&gt; writes them all:
/// a street of one culture, two streets' fences, fences close up, a terrace row, regional families, odd one-offs,
/// garage doors and front doors close up beside a 1.8 m figure, the service fixing. --shots=a,b only writes the
/// frames whose names start with one of those. Fresh seeds every run.
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
        string[] only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--shots="))?["--shots=".Length..].Split(',');
        bool Want(string name) => only == null || only.Any(o => name.StartsWith(o, StringComparison.Ordinal));
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

        // Lots in a row along +x, fronts to -z (the street), each lot's line against the last's; returns their layouts and where each stands.
        List<(Layout L, float X)> Row(Func<int, LayoutHints> hints, int n, Func<Layout, bool> keep = null, int tries = 400, float start = 0, bool clear = true)
        {
            if (clear)
            {
                foreach (Node3D x in _row) x.QueueFree();
                _row.Clear();
            }
            var made = new List<(Layout, float)>();
            float at = start;
            for (int k = 0, t = 0; k < n && t < tries; t++)
            {
                Layout l = PoiFactory.BuildLayout(lib, fam, 1, Rarity.Common, (ulong)rand.NextInt64(), hints(k));
                if (keep != null && !keep(l)) continue;
                PoiInstance poi = PoiFactory.Spawn(lib, l, PoiState.Active, (ulong)rand.NextInt64());
                var view = PoiView.Build(poi, Biome.Steppe, new PoiView.Options(Collision: false, Figures: false), (_, _) => new Color(0.36f, 0.43f, 0.27f));
                HomeNote h = l.Extras.Homes.FirstOrDefault();
                bool lot = h != null && h.LotMax.X > h.LotMin.X;
                float x0 = lot ? h.LotMin.X : l.Occupied.Min.X, w = lot ? h.LotMax.X - h.LotMin.X + 1 : l.Occupied.Size.X - 6;
                view.Position = new Vector3(at - x0, 0, 0);
                root.AddChild(view);
                _row.Add(view);
                made.Add((l, at - x0));
                at += w;
                k++;
            }
            return made;
        }

        MeshInstance3D Figure(Vector3 feet)
        {
            var person = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.25f, Height = 1.8f }, Position = feet + new Vector3(0, 0.9f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.35f, 0.2f) } };
            root.AddChild(person);
            return person;
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

        string Says(IEnumerable<(Layout L, float X)> row) => string.Join(" | ", row.Select(r => string.Join(", ", r.L.Extras.Homes.Select(h => $"{h.Family} {h.Silhouette} fence {h.FenceKind}"))));

        // The front door of a lot as built: its middle at the floor, the way out, the way along it.
        (Vector3 Mid, Vector3 Out, Vector3 Along)? Door((Layout L, float X) one)
        {
            HomeNote h = one.L.Extras.Homes.FirstOrDefault();
            if (h == null || h.Exits.Count == 0) return null;
            var (a, b, o) = h.Exits[0];
            var mid = new Vector3(one.X + (a.X + b.X) / 2f + 0.5f, a.Y, (a.Z + b.Z) / 2f + 0.5f);
            Int3 ov = o.Vec(), av = b - a;
            return (mid, new Vector3(ov.X, 0, ov.Z), new Vector3(av.X, 0, av.Z));
        }

        // A front door close up, the figure standing at it.
        async System.Threading.Tasks.Task DoorShot(string name, (Layout L, float X) one)
        {
            if (Door(one) is not var (mid, o, along)) return;
            var person = Figure(mid + o * 1.1f + along * 0.9f);
            await Shot(name, mid + o * 8.5f - along * 3.5f + new Vector3(0, 1.8f, 0), mid + new Vector3(0, 2.2f, 0) + o * 1f);
            person.QueueFree();
        }

        // A suburban street: one culture's houses, mid tech, temperate, seen from the street's edge looking along it.
        int culture = rand.Next();
        for (int k = 0; k < 400 && HomeCulture.Of(lib.Homes, new HomeHints(culture, Climate.Temperate, 0.6f), TechLevel.Mid).Families[0].Family.Id is not ("suburban" or "bungalow"); k++) culture = rand.Next();
        if (Want("street"))
        {
            var street = Row(_ => new LayoutHints(Dir.North, TechLevel.Mid, null, new HomeHints(culture, Climate.Temperate, 0.6f)), 6);
            GD.Print($"street: {Says(street)}");
            float end = street[^1].X + 40;
            await Shot("street", new Vector3(end * 0.12f, 2.2f, -17), new Vector3(end * 0.5f, 3.5f, 6));
            await Shot("street-across", new Vector3(end * 0.3f, 8f, -34), new Vector3(end * 0.3f, 4f, 14));
        }

        // Two streets of one town side by side, each with its own fences.
        if (Want("streets-fences"))
        {
            int c1 = rand.Next(), c2 = rand.Next();
            HomeCulture C(int seed) => HomeCulture.Of(lib.Homes, new HomeHints(seed, Climate.Temperate, 0.55f), TechLevel.Mid);
            for (int k = 0; k < 400 && (C(c1).Fences.Count == 0 || C(c1).Bare > 0.2f || C(c1).Fences[0].Kind.Body == "edging"); k++) c1 = rand.Next();
            for (int k = 0; k < 400 && (C(c2).Fences.Count == 0 || C(c2).Bare > 0.2f || C(c2).Fences[0].Kind.Body is "edging" || C(c2).Fences[0].Kind.Body == C(c1).Fences[0].Kind.Body); k++) c2 = rand.Next();
            var a = Row(_ => new LayoutHints(Dir.North, TechLevel.Mid, null, new HomeHints(c1, Climate.Temperate, 0.55f)), 3);
            float gap = a.Count > 0 ? a[^1].X + a[^1].L.Extras.Homes[0].LotMax.X + 14 : 60;
            var b = Row(_ => new LayoutHints(Dir.North, TechLevel.Mid, null, new HomeHints(c2, Climate.Temperate, 0.55f)), 3, start: gap, clear: false);
            GD.Print($"street A ({C(c1).Fences[0].Kind.Id}): {Says(a)}");
            GD.Print($"street B ({C(c2).Fences[0].Kind.Id}): {Says(b)}");
            float end = b.Count > 0 ? b[^1].X + b[^1].L.Extras.Homes[0].LotMax.X : gap * 2;
            await Shot("streets-fences", new Vector3(end * 0.5f, 14f, -46), new Vector3(end * 0.5f, 1f, 8));
        }

        // Fences close up: a corner post, the side run going back, the front run to the gate; a panel fence and a wall or railings.
        for (int n = 0; n < 2; n++)
        {
            string name = $"fence-close-{n}";
            if (!Want(name)) continue;
            int nLocal = n;
            var one = Row(_ => new LayoutHints(Dir.North, nLocal == 0 ? TechLevel.Low : TechLevel.Mid, null, new HomeHints(rand.Next(), nLocal == 0 ? Climate.Wet : Climate.Temperate, 0.6f)), 1,
                l => l.Extras.Homes.FirstOrDefault()?.Fence is { } fn && fn.Gates.Count > 0 && (nLocal == 0 ? fn.Body == "panel" : fn.Body is "railing" or "wall" or "hedge"), 800);
            if (one.Count == 0) { GD.Print($"no fence came up for {name}"); continue; }
            HomeNote h = one[0].L.Extras.Homes[0];
            GD.Print($"{name}: {Says(one)}");
            var corner = new Vector3(one[0].X + h.LotMin.X, 0, h.LotMin.Z);
            var gate = h.Fence!.Gates[0].A;
            var g = new Vector3(one[0].X + gate.X + 0.5f, 0, gate.Z + 0.5f);
            var person = Figure(corner + new Vector3(2.5f, 0, -1.3f));
            await Shot(name, corner + new Vector3(-3f, 1.9f, -5f), corner + new Vector3(4.5f, 0.6f, 3.5f));
            var person2 = Figure(g + new Vector3(0, 0, -1.3f));
            await Shot(name + "-gate", g + new Vector3(-4f, 2f, -6.5f), g + new Vector3(0.5f, 0.8f, 1f));
            person.QueueFree();
            person2.QueueFree();
        }

        // Front doors close up with the figure at the door: a one-storey eaves house under its hipped or pagoda roof, and a cross-gabled house with a carport.
        if (Want("door-eaves"))
        {
            var one = Row(_ => new LayoutHints(Dir.North, TechLevel.Mid, null, new HomeHints(rand.Next(), Climate.Temperate, 0.6f, "eaves")), 1,
                l => l.Extras.Homes.FirstOrDefault() is { Family: "eaves", Storeys: 1, Roof: "hip" or "pagoda" }, 600);
            if (one.Count > 0) { GD.Print($"door-eaves: {Says(one)}"); await DoorShot("door-eaves", one[0]); }
        }
        if (Want("door-carport"))
        {
            var one = Row(_ => new LayoutHints(Dir.North, TechLevel.Mid, null, new HomeHints(rand.Next(), Climate.Temperate, 0.4f, rand.Next(2) == 0 ? "suburban" : "bungalow")), 1,
                l => l.Extras.Homes.FirstOrDefault() is { Roof: "cross", Garage: "carport" }, 1500);
            if (one.Count > 0 && Door(one[0]) is var (mid, o, along))
            {
                // Back far enough to see the carport beside the house as well as the figure at the door.
                GD.Print($"door-carport: {Says(one)}");
                HomeNote h = one[0].L.Extras.Homes[0];
                var port = new Vector3(one[0].X + h.DoorCell.X + 0.5f, 0, h.DoorCell.Z + 0.5f);
                var person = Figure(mid + o * 1.1f + along * 0.9f);
                Vector3 between = (mid + port) * 0.5f;
                await Shot("door-carport", between + o * 15f + new Vector3(0, 2.4f, 0) - (port - mid).Normalized() * 2f, between + new Vector3(0, 2.2f, 0));
                person.QueueFree();
            }
            else GD.Print("no cross-gabled house with a carport came up");
        }

        // The service fitting at the roof's corner on the street side, close up from the street off that corner, the figure
        // standing at the corner and the meter box on the side wall: a pitched roof (under the eave's corner) and a flat roof
        // (on the parapet's corner).
        foreach (var (name, flat) in new[] { ("corner-pitched", false), ("corner-flat", true) })
        {
            if (!Want(name)) continue;
            bool flatLocal = flat;
            ServiceMount want = flat ? ServiceMount.Parapet : ServiceMount.Eave;
            var one = Row(_ => new LayoutHints(Dir.North, TechLevel.Mid, null, new HomeHints(rand.Next(), flatLocal ? Climate.Hot : Climate.Temperate, 0.6f, flatLocal ? (rand.Next(2) == 0 ? "modern" : "courtyard") : rand.Next(2) == 0 ? "suburban" : "bungalow")), 1,
                l => l.Extras.Homes.FirstOrDefault() is { } h && (flatLocal ? h.Roof == "flat" : h.Roof is "gable" or "hip" or "cross") && l.Extras.Service.Any(p => p.Mount == want), 1200);
            if (one.Count == 0) { GD.Print($"no {name} came up"); continue; }
            ServicePoint p = one[0].L.Extras.Service.First(x => x.Mount == want);
            GD.Print($"{name}: fitting at {p.Point} ({p.Mount}), held by {one[0].L.Blocks.Get(p.Anchor)}: {Says(one)}");
            PlainBox box = one[0].L.Extras.Plain.OrderByDescending(x => (x.Max.X - x.Min.X) * (x.Max.Z - x.Min.Z)).First();
            var at = new Vector3(one[0].X + p.Point.X, p.Point.Y, p.Point.Z);
            float sx = Math.Sign(at.X - one[0].X - (box.Min.X + box.Max.X + 1) * 0.5f);
            if (sx == 0) sx = 1;
            // The figure by the front wall just in from the corner, clear of the fitting and the box; the eye a little lower than the fitting, five or six metres out.
            var person = Figure(new Vector3(at.X - sx * 1.6f, 0, at.Z - 0.9f));
            float up = Math.Max(1.6f, at.Y - 1.2f);
            await Shot(name, new Vector3(at.X + sx * 4.5f, up, at.Z - 5.5f), new Vector3(at.X - sx * 0.6f, at.Y * 0.55f, at.Z + 1.2f));
            person.QueueFree();
        }

        // A row of terraces.
        if (Want("terrace"))
        {
            var terrace = Row(_ => new LayoutHints(Dir.North, TechLevel.Mid, null, new HomeHints(rand.Next(), Climate.Temperate, 0.5f, "terrace")), 2);
            GD.Print($"terrace: {Says(terrace)}");
            float tw = terrace[^1].X + terrace[^1].L.Extras.Homes[0].LotMax.X;
            await Shot("terrace", new Vector3(tw * 0.2f, 3f, -24), new Vector3(tw * 0.45f, 5f, 10));
        }

        // Regional families, each in its own climate.
        foreach (var (family, climate, tech) in new[] { ("chalet", Climate.Cold, TechLevel.Mid), ("stilt", Climate.Wet, TechLevel.Low), ("courtyard", Climate.Hot, TechLevel.Mid), ("adobe", Climate.Hot, TechLevel.Low), ("canal", Climate.Temperate, TechLevel.Low), ("eaves", Climate.Temperate, TechLevel.Mid) })
        {
            if (!Want($"family-{family}")) continue;
            var one = Row(_ => new LayoutHints(Dir.North, tech, null, new HomeHints(rand.Next(), climate, 0.6f, family)), 3);
            GD.Print($"{family}: {Says(one)}");
            float w = one[^1].X + one[^1].L.Extras.Homes[0].LotMax.X;
            await Shot($"family-{family}", new Vector3(w * 0.5f - 10, 4f, -30), new Vector3(w * 0.5f, 4f, 12));
        }

        // The odd one-offs.
        foreach (string odd in new[] { "watertower", "mushroom", "saucer", "wavehouse", "hillside", "glasshell", "pod" })
        {
            if (!Want($"odd-{odd}")) continue;
            var one = Row(_ => new LayoutHints(Dir.North, TechLevel.High, null, new HomeHints(rand.Next(), Climate.Temperate, 0.6f, odd)), 1,
                l => l.Extras.Homes.Any(h => h.Family == odd));
            if (one.Count == 0) continue;
            GD.Print($"odd {odd}: {Says(one)}");
            // On the house itself, from a little above any garden wall.
            PlainBox b = one[0].L.Extras.Plain.OrderByDescending(x => (x.Max.X - x.Min.X) * (x.Max.Z - x.Min.Z)).First();
            float cx = one[0].X + (b.Min.X + b.Max.X + 1) * 0.5f, cz = (b.Min.Z + b.Max.Z + 1) * 0.5f, size = Math.Max(b.Max.X - b.Min.X, b.Max.Z - b.Min.Z);
            Vector3 look = new(cx, b.Eave * 0.8f, cz);
            await Shot($"odd-{odd}", new Vector3(cx - size * 0.55f, b.Eave + 5f, cz - size * 1.25f), look);
        }

        // Garage doors close up: one of each kind, a 1.8 m figure beside each.
        foreach (string kind in new[] { "panel", "ribbed", "glazed", "timber", "hinged", "carport" })
        {
            if (!Want($"garage-{kind}")) continue;
            var one = Row(_ => new LayoutHints(Dir.North, kind is "timber" or "hinged" ? TechLevel.Low : TechLevel.Mid, null, new HomeHints(rand.Next(), Climate.Temperate, 0.8f, kind is "timber" or "hinged" ? "bungalow" : "suburban")), 1,
                l => l.Extras.Homes.Any(h => h.GarageDoor == kind && h.DoorWide > 0), 600);
            if (one.Count == 0) { GD.Print($"no {kind} garage came up"); continue; }
            HomeNote h = one[0].L.Extras.Homes.First(x => x.GarageDoor == kind);
            GD.Print($"garage {kind} {h.DoorWide} m: {Says(one)}");
            Vector3 door = new Vector3(one[0].X + h.DoorCell.X, 0, h.DoorCell.Z) + new Vector3(h.DoorWide * 0.5f, 0, 0);
            var person = Figure(door + new Vector3(-h.DoorWide * 0.5f - 0.8f, 0, -1.2f));
            await Shot($"garage-{kind}", door + new Vector3(-4f, 1.7f, -10f), door + new Vector3(0, 1.6f, 0));
            person.QueueFree();
        }

        // Front doors close up, a 1.8 m figure at each: the standard door, framed (where at all) in the house's own trim.
        for (int n = 0; n < 2; n++)
        {
            if (!Want($"door-{n}")) continue;
            int nLocal = n;
            var one = Row(_ => new LayoutHints(Dir.North, TechLevel.Mid, null, new HomeHints(rand.Next(), Climate.Temperate, 0.6f, nLocal == 0 ? "suburban" : "cottage")), 1);
            GD.Print($"door {n}: {Says(one)}");
            await DoorShot($"door-{n}", one[0]);
        }
        GetTree().Quit(0);
    }
}
