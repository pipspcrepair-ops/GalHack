using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using Slice.Construction;
using Slice.Game;
using Slice.Items;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// The BLUEPRINTS tab and its starter set. Pure first: every starter loads, powers on and is fitted
/// for its mode; a saved structure loads back exactly; the frigate's hangar takes the Skiff. Then in
/// the world, through the tab's own widgets: Survival's PLACE waits on locked devices and starts the
/// projector; Creative's PLACE sets each starter down whole on clear ground (nothing of it in the
/// ground, no long frame), and each powers on and moves in its mode (the hover tank excepted: Large
/// Hover isn't in the game yet); SAVE keeps a structure you sit in under a typed name, under Yours.
/// </summary>
public partial class BuildTest
{
    const string TestFolder = "user://blueprints-test";

    void BlueprintChecks()
    {
        GameData data = GameData.Shared;
        float g = data.Rules.Planet.Gravity;
        var library = new BlueprintLibrary(data, g, TestFolder);
        Check(library.Starters.Count == data.Rules.Build.Library.Count,
            $"the starter set loads: {string.Join(", ", library.Starters.Select(e => $"{e.Name} ({BlueprintsSection.ModeLabel(e)})"))}");
        foreach (LibraryEntry e in library.Starters)
        {
            Structure s = e.Plan;
            StructureStats st = StructureStats.Compute(s);
            Check(s.Core != null && st.Powered && s.Owner == Building.DefaultOwner,
                $"{e.Name}: {s.Count} blocks, {st.Mass:F1} t, powers on ({st.PowerOut:F2} MW out, {st.PowerUse:F2} MW at full load)");
            if (s.Mode == CoreMode.Base)
            {
                Check(st.Generators > 0 && st.Fuel > 0 && st.Containers > 0 && BuildingBridge.MachinesOn(data.Items, s).Any(), $"{e.Name}: a constructor, storage, a generator and fuel");
                continue;
            }
            List<string> missing = ModeFit.Missing(s, s.Mode, data.Handling, g);
            HandlingStats h = e.Handling;
            Check(missing.Count == 0 && h.CanLiftOff && h.Accel(Direction.Forward) > 2f && h.TurnRate > 5f,
                $"{e.Name}: fitted for {BlueprintsSection.ModeLabel(e)} ({h.Accel(Direction.Forward):F1} m/s², {h.TurnRate:F0} °/s, {h.TopSpeed:F0} m/s, lift margin {h.LiftMargin:+0;-0} kN){(missing.Count > 0 ? $": missing {string.Join(", ", missing)}" : "")}");
        }
        LibraryEntry fighter = library.Starters.FirstOrDefault(e => e.Plan.Blocks().Any(b => e.Plan.TypeOf(b.block).Function == "weapon"));
        LibraryEntry shuttle = library.Starters.FirstOrDefault(e => e.Plan.Blocks().Count(b => e.Plan.TypeOf(b.block).IsCommandChair) >= 2);
        Check(fighter != null && shuttle != null && shuttle.Plan.Blocks().Count(b => shuttle.Plan.TypeOf(b.block).Function == "container") >= 4,
            $"the fighter carries guns ({fighter?.Name}), the shuttle two seats and cargo ({shuttle?.Name})");

        // The frigate's hangar: an empty box open to the outside at one end, the Skiff standing on its legs inside with room round it.
        LibraryEntry skiff = library.Starters.First(e => e.File == data.Rules.Build.StarterBlueprint);
        LibraryEntry carrier = library.Starters.OrderByDescending(e => e.Plan.Count).FirstOrDefault(e => e.Plan.Mode != CoreMode.Base && e.Plan.Blocks().Count(b => e.Plan.TypeOf(b.block).IsCommandChair) >= 2);
        float legs = new StructureVessel(skiff.Plan, data.Handling).LandingGear.Select(l => l.Length).DefaultIfEmpty(0f).Max();
        var need = new Vector3(skiff.Size.X, skiff.Size.Y + legs, skiff.Size.Z) + Vector3.One;
        Check(carrier != null && Hangar(carrier.Plan, need), $"the {carrier?.Name}'s hangar takes the Skiff with a metre to spare ({need.X:F1} x {need.Y:F1} x {need.Z:F1} m), open at one end");

        RoundTrip(data, skiff.Plan);
        RoundTrip(data, library.Starters.First(e => e.Plan.Mode == CoreMode.Base).Plan);
    }

    /// <summary>A structure saved and read back is the same structure: blocks, shapes, turns, paint, doors, lights; every block whole.</summary>
    void RoundTrip(GameData data, Structure plan)
    {
        Structure s = BlueprintLibrary.Design(plan, "Round Trip");
        // Wear one block, open a door, restyle a light: the design keeps all but the wear.
        var rng = new Random();
        var cells = s.Blocks().Select(b => b.pos).ToList();
        Int3 worn = cells[rng.Next(cells.Count)];
        Block w = s.Get(worn);
        w.Hp *= 0.5f;
        s.Set(worn, w);
        var lamps = cells.Where(c => s.TypeOf(s.Get(c)).IsLight).ToList();
        if (lamps.Count > 0) s.SetLight(lamps[rng.Next(lamps.Count)], LightSettings.Make(LightSettings.Palette[rng.Next(LightSettings.Palette.Length)], LightAnim.Pulse, LightSettings.Speeds[1], rng.Next(64)));
        string text = BlueprintLibrary.Text(s, "Round Trip");
        Structure back = Blueprint.Load(text, data.Catalogue, out List<string> errors);
        bool same = back != null && back.Count == s.Count && s.Blocks().All(b =>
            back.TryGet(b.pos, out Block o) && o.Type == b.block.Type && o.Shape == b.block.Shape && o.Rot == b.block.Rot && o.Flags == b.block.Flags
            && Dirs.All.All(d => Building.PaintOf(back, b.pos, d) == Building.PaintOf(s, b.pos, d))
            && Math.Abs(o.Hp - s.StatsOf(b.block).MaxHp) < 0.06f);
        bool lights = back != null && s.LightStyles.All(kv => back.LightAt(kv.Key) == kv.Value) && back.LightStyles.Count == s.LightStyles.Count;
        Check(errors.Count == 0 && same && lights && back.Mode == s.Mode && back.GravityOn == s.GravityOn && back.Name == "Round Trip" && Blueprint.Save(back) == text,
            $"{plan.Name} saved and read back exactly: {s.Count} blocks, their paint and lights, its mode; every block whole; saved again, the same text {string.Join("; ", errors.Take(3))}");
    }

    /// <summary>Is there an empty box of at least this size (m) in the structure, floored, roofed, and open to the outside at one end along its length?</summary>
    static bool Hangar(Structure s, Vector3 need)
    {
        float cell = s.Grid.CellSize;
        int w = (int)MathF.Ceiling(need.X / cell), h = (int)MathF.Ceiling(need.Y / cell), l = (int)MathF.Ceiling(need.Z / cell);
        var (min, max) = s.Bounds();
        for (int x = min.X; x + w - 1 <= max.X; x++)
            for (int y = min.Y + 1; y + h - 1 <= max.Y; y++)
                for (int z = min.Z; z + l - 1 <= max.Z; z++)
                {
                    bool fits = true;
                    for (int i = 0; i < w && fits; i++)
                        for (int k = 0; k < l && fits; k++)
                        {
                            fits = s.Has(new Int3(x + i, y - 1, z + k)) && s.Has(new Int3(x + i, y + h, z + k));
                            for (int j = 0; j < h && fits; j++) fits = !s.Has(new Int3(x + i, y + j, z + k));
                        }
                    if (!fits) continue;
                    // Open behind it: nothing between its back end and the outside.
                    bool open = true;
                    for (int i = 0; i < w && open; i++)
                        for (int j = 0; j < h && open; j++)
                            for (int k = z + l; k <= max.Z && open; k++) open = !s.Has(new Int3(x + i, y + j, k));
                    if (open) return true;
                }
        return false;
    }

    // ---- In the world ----------------------------------------------------------------------------

    BlueprintsSection Tab => _game.Menu.Root.Blueprints;

    IEnumerable<int> Blueprints()
    {
        GD.Print("-- the BLUEPRINTS tab: the library, PLACE, SAVE --");
        string folder = ProjectSettings.GlobalizePath(TestFolder);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Tab.Library.UseFolder(TestFolder);
        _game.Mode = GameMode.Survival;
        foreach (int x in _play.OpenMenu(MenuTab.Blueprints)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        int count = Tab.Library.Starters.Count;
        Check(_game.Menu.Tab == MenuTab.Blueprints && Enumerable.Range(0, count).All(i => _game.Menu.RectOf($"bp:entry:{i}") != null)
              && _game.Menu.RectOf("bp:place") != null && _game.Menu.RectOf("bp:save") != null && _game.Menu.RectOf("core:blueprint") == null,
            $"the BLUEPRINTS tab lists all {count} starters, with PLACE and SAVE; blueprints are nowhere else");

        // Survival: the projector, once the devices are unlocked.
        LibraryEntry skiff = Tab.Library.Starters.First(e => e.File == _game.Zone.Data.Rules.Build.StarterBlueprint);
        foreach (int x in _drive.ClickWidget($"bp:entry:{Tab.Library.All.ToList().IndexOf(skiff)}")) yield return x;
        List<string> locked = Tab.Locked(skiff);
        if (locked.Count > 0)
        {
            foreach (int x in _drive.ClickWidget("bp:place")) yield return x;
            Check(!_game.Builder.Projecting && _game.Menu.IsOpen, $"in Survival PLACE waits for the Skiff's locked devices ({string.Join(", ", locked)})");
        }
        _game.Progress.Creative = true; // a shortcut: the whole tree open, still in Survival
        foreach (int x in _drive.Wait(1)) yield return x;
        foreach (int x in _drive.ClickWidget("bp:place")) yield return x;
        Check(_game.Builder.Projecting && _game.Builder.Plan == skiff.Plan && _game.Builder.Underway == null && !_game.Menu.IsOpen,
            "with them unlocked, Survival's PLACE starts the projector on the Skiff and closes the menu");
        _game.Builder.StopProjecting();
        _game.Progress.Creative = false;

        // Creative: each starter set down whole, then powered and moved in its mode.
        _game.Mode = GameMode.Creative;
        var placed = new Dictionary<string, Vessel>();
        int n = 0;
        foreach (LibraryEntry e in Tab.Library.Starters.ToList())
        {
            Vessel v = null;
            string why = null;
            var probe = new PlanetSurface.FrameProbe(this);
            double worst = 0, before = 0;
            WholePlacement job = null;
            for (int attempt = 0; attempt < 6 && v == null; attempt++, n++)
            {
                // Level ground as wide as it is, and you a few metres short of it, facing it: PLACE puts it just ahead.
                float radius = 0.5f * new Vector2(e.Size.X, e.Size.Z).Length();
                Vector3 spot = _play.FlatSpot(_game.Start, 140f + 70f * (n % 5), n * 1.3f, 0.5f + attempt * 0.5f, span: MathF.Max(14f, radius));
                float yaw = n * 0.9f;
                Vector3 ahead = new Basis(Vector3.Up, yaw) * Vector3.Forward;
                foreach (int x in StandAt(spot - ahead * (radius + 4f))) yield return x;
                foreach (int x in _drive.UntilSettled(20)) yield return x;
                _game.Survivor.Yaw = yaw;
                _game.Survivor.Pitch = 0f;
                foreach (int x in _drive.Wait(30)) yield return x;
                foreach (int x in _play.OpenMenu(MenuTab.Blueprints)) yield return x;
                foreach (int x in _drive.ClickWidget($"bp:entry:{Tab.Library.All.ToList().IndexOf(e)}")) yield return x;
                // The frames before, for comparison: the world's own work (streaming round a new spot) is in every frame.
                before = 0;
                for (int t = 0; t < 60; t++)
                {
                    yield return 0;
                    before = Math.Max(before, probe.LastMs);
                }
                worst = 0;
                foreach (int x in _drive.ClickWidget("bp:place"))
                {
                    yield return x;
                    worst = Math.Max(worst, probe.LastMs);
                }
                job = _game.Builder.Placing;
                for (int t = 0; t < 600 && job != null && !job.Done; t++)
                {
                    worst = Math.Max(worst, probe.LastMs);
                    yield return 0;
                }
                v = job?.Vessel;
                for (int t = 0; t < 20; t++)
                {
                    yield return 0;
                    worst = Math.Max(worst, probe.LastMs);
                }
                if (v == null)
                {
                    why = Tab.Why;
                    GD.Print($"  ({e.Name} would not go down near {spot}: {_game.Builder.Verdict}; the tab says {why})");
                    foreach (int x in _play.CloseMenu()) yield return x;
                }
            }
            probe.Remove();
            if (!Check(v != null && v.Name == e.Name && v.Structure.Count == e.Plan.Count && !_game.Menu.IsOpen,
                    $"Creative's PLACE sets the {e.Name} down whole: {v?.Structure.Count} of {e.Plan.Count} blocks, the menu closed")) continue;
            placed[e.File] = v;
            // Its own work is what it answers for; a headless frame also carries the world's own (streaming, the wildlife, the collector).
            Check(job.WorstTickMs < 33.4,
                $"{e.Name}: placed within the frame budget: the spot found in {job.SiteMs:F0} ms over several ticks, the body built in {job.SpawnMs:F0} ms, plants and ground cleared in {job.GroundMs:F0} ms; the most it took of any tick {job.WorstTickMs:F0} ms (the worst whole frame round it {worst:F0} ms, before it {before:F0} ms)");
            int buried = Buried(v), plants = PlantsUnder(v);
            Check(buried == 0 && plants == 0, $"{e.Name}: nothing of it in the ground ({buried} blocks in, {(v.IsBase ? "a base: the ground under it cleared flush" : "standing on its lowest point")}), and no plant left under it ({job.Felled} felled)");
            foreach (int x in _drive.Wait(20)) yield return x;
            Check(v.Powered && v.StructureStats.Powered && v.PendingShapes == 0 && v.View.Waiting == 0, $"{e.Name}: powers on ({v.StructureStats.PowerOut:F2} MW), its collision and look all built");
            foreach (int x in Moves(e, v)) yield return x;
        }

        // SAVE: sitting in the Skiff placed above, typed a new name, kept under Yours, read back the same.
        if (placed.TryGetValue(skiff.File, out Vessel own))
        {
            _game.Survivor.Sit(own);
            foreach (int x in _drive.Wait(10)) yield return x;
            foreach (int x in _play.OpenMenu(MenuTab.Blueprints)) yield return x;
            foreach (int x in _drive.Wait(2)) yield return x;
            Check(Tab.SaveTarget() == own, $"SAVE's target is the structure you sit in ({Tab.SaveTarget()?.Name})");
            foreach (int x in _drive.ClickWidget("bp:save")) yield return x;
            Check(Tab.Saving && ItemViews.Focused == BlueprintsSection.NameBox && ItemViews.Get(BlueprintsSection.NameBox).Text == own.Name,
                "SAVE asks for a name, the structure's own in the box");
            string name = $"Test Skiff {new Random().Next(100, 999)}";
            _game.Controls.Backspaces = 40;
            _game.Controls.Type(name);
            foreach (int x in _drive.Wait(2)) yield return x;
            foreach (int x in _drive.ClickWidget("bp:save-confirm")) yield return x;
            LibraryEntry saved = Tab.Library.Yours.FirstOrDefault(y => y.Name == name);
            Check(!Tab.Saving && saved != null && File.Exists(saved.File) && Tab.Selected == saved && _game.Menu.RectOf($"bp:entry:{Tab.Library.All.ToList().IndexOf(saved)}") != null,
                $"saved as \"{name}\" in {TestFolder}, listed under Yours and selected");
            bool same = saved != null && saved.Plan.Count == own.Structure.Count && own.Structure.Blocks().All(b =>
                saved.Plan.TryGet(b.pos, out Block o) && o.Type == b.block.Type && o.Shape == b.block.Shape && o.Rot == b.block.Rot);
            Check(same && saved.Plan.Mode == own.Structure.Mode, "and it reads back as the structure it was saved from");
            foreach (int x in _play.CloseMenu()) yield return x;
            _game.Survivor.StandUp();
            foreach (int x in _drive.Wait(10)) yield return x;
        }
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        _game.Mode = GameMode.Survival;
    }

    /// <summary>Plants still standing with their roots under a structure's footprint.</summary>
    int PlantsUnder(Vessel v)
    {
        var (min, max) = v.Structure.Bounds();
        float cell = v.Structure.Grid.CellSize;
        Vector3 lo = new Vector3(min.X, min.Y, min.Z) * cell - Vector3.One * cell * 0.5f, hi = new Vector3(max.X, max.Y, max.Z) * cell + Vector3.One * cell * 0.5f;
        Transform3D toLocal = v.DrawTransform.AffineInverse();
        Vector3 mid = v.ToWorld(((lo + hi) * 0.5f).ToNumerics());
        return _game.Zone.Streamer.FloraNear(mid, new Vector2(hi.X - lo.X, hi.Z - lo.Z).Length() * 0.5f)
            .Count(p => toLocal * p.Root is Vector3 at && at.X > lo.X && at.X < hi.X && at.Z > lo.Z && at.Z < hi.Z);
    }

    /// <summary>Blocks whose middle is in the ground (natural or dug), and for a vessel the middle of its lowest face too.</summary>
    int Buried(Vessel v)
    {
        var store = _game.Zone.Planet.Terrain.Store;
        float cell = v.Structure.Grid.CellSize;
        int n = 0;
        foreach (var (p, b) in v.Structure.Blocks())
        {
            if (!v.Structure.IsSolid(b)) continue;
            N.Vector3 c = StructureBridge.CellCenter(v.Structure, p);
            bool inside = store.SampleDensity(v.Body.ToParent(c)) > 0.25f;
            if (!v.IsBase && !inside) inside = store.SampleDensity(v.Body.ToParent(c - N.Vector3.UnitY * cell * 0.45f)) > 0.25f;
            if (inside) n++;
        }
        return n;
    }

    /// <summary>Into its seat, and does it move as its mode says: Flight climbs on its thrusters, Hover runs along the ground on its plates.</summary>
    IEnumerable<int> Moves(LibraryEntry e, Vessel v)
    {
        if (v.IsBase || e.Plan.Mode == CoreMode.Base)
        {
            Check(v.IsBase && v.Machines.Count > 0 && v.StructureStats.StorageCapacity > 0, $"{e.Name}: anchored as a base, its constructor ({v.Machines.Values.First().Def.Tag}) and storage ready");
            yield break;
        }
        Check(_game.Survivor.Sit(v), $"{e.Name}: in its seat");
        foreach (int x in _drive.Wait(10)) yield return x;
        Vector3 from = v.Body.Position.ToGodot();
        float high = v.AboveGround;
        if (v.Structure.Mode == CoreMode.Hover)
        {
            _game.Controls.Hold(Key.W);
            foreach (int x in _drive.Wait(60)) yield return x;
            _game.Controls.Hold(Key.W, false);
            foreach (int x in _drive.Wait(15)) yield return x;
            Vector3 to = v.Body.Position.ToGodot();
            // And brakes, before getting out.
            _game.Controls.Hold(Key.S);
            foreach (int x in _drive.Wait(45)) yield return x;
            _game.Controls.Hold(Key.S, false);
            foreach (int x in _drive.Wait(30)) yield return x;
            float run = new Vector2(to.X - from.X, to.Z - from.Z).Length();
            Check(v.Controller.Hover != null && run > 4f && v.AboveGround < 8f, $"{e.Name}: Hover Mode runs along the ground on its plates ({run:F0} m, {v.AboveGround:F1} m up)");
        }
        else
        {
            _game.Controls.Hold(Key.Space);
            for (int t = 0; t < 60 * 6 && v.AboveGround < high + 6f; t++) yield return 0;
            _game.Controls.Hold(Key.Space, false);
            Check(v.AboveGround > high + 4f && !v.Controller.Frozen, $"{e.Name}: Flight Mode lifts it off on its thrusters ({high:F1} m up, then {v.AboveGround:F1} m)");
            // And down again before getting out.
            _game.Controls.Hold(Key.Ctrl);
            for (int t = 0; t < 60 * 10 && !v.Controller.Frozen && v.AboveGround > high + 0.6f; t++) yield return 0;
            _game.Controls.Hold(Key.Ctrl, false);
            foreach (int x in _drive.Wait(60)) yield return x;
        }
        _game.Survivor.StandUp();
        foreach (int x in _drive.Wait(10)) yield return x;
    }
}
