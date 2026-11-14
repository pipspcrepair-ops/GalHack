using System;
using System.Collections.Generic;
using System.Linq;
using AssetViewer.Providers.Components;
using AssetViewer.Providers.Parts;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;

namespace AssetViewer.Tests;

/// <summary>
/// The Components provider: every non-armour block in the game's catalogue has an entry in a real family,
/// every function with several sizes has its row, everything builds in two states, every animation plays
/// in the tree, and the behaviours that matter happen: the sitter ends on the seat, a thruster's flame
/// leaves the end of its body, a door opens for the walker, the climber and the stair walker rise.
/// </summary>
public partial class ComponentsProviderTest : Node
{
    int _passes, _failures;

    bool Check(bool ok, string what)
    {
        if (ok) _passes++; else { _failures++; GD.PrintErr("  FAIL " + what); }
        return ok;
    }

    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception e) { _failures++; GD.PrintErr("  FAIL threw: " + e); }
        GD.Print($"\n{_passes} checks passed, {_failures} failed");
        GD.Print(_failures == 0 ? "ALL PASSED" : "SOME CHECKS FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    static BuildRequest Request(AssetEntry e, bool alternative)
    {
        var req = new BuildRequest { Seed = alternative ? 777UL : Viewer.DefaultSeed(e) };
        foreach (AssetOption o in e.Options)
        {
            if (o.Values.Length == 0) { req.Options[o.Name] = ""; continue; }
            int i = alternative ? o.Values.Length - 1 - (o.Default == o.Values.Length - 1 ? 1 : 0) : o.Default;
            req.Options[o.Name] = o.Values[Math.Clamp(i, 0, o.Values.Length - 1)];
        }
        return req;
    }

    static Node3D Person(StructureAsset a) => a.Holder.GetChildren().OfType<Node3D>().FirstOrDefault(n => n.Name == "Sleeper");

    async System.Threading.Tasks.Task Run()
    {
        var registry = AssetRegistry.Discover();
        Check(registry.Failures.Count == 0, "providers list cleanly: " + string.Join("; ", registry.Failures.Select(f => f.Provider + ": " + f.Error)));
        var entries = registry.Entries.Where(e => e.Category == "Components").ToList();
        Catalogue cat = PartsData.Blocks;

        GD.Print("== coverage of the catalogue");
        var devices = cat.Types.Skip(1).Where(t => !t.IsArmor).ToList();
        Check(devices.Count > 40, $"the catalogue has {devices.Count} components");
        foreach (BlockType t in devices)
        {
            AssetEntry e = entries.FirstOrDefault(x => x.Id == "component." + t.Id);
            Check(e != null, $"component {t.Id} has an entry");
            Check(e == null || e.Group == ComponentsProvider.FamilyOf(t) && e.Group != "Other", $"component {t.Id} (function '{t.Function}') is in a family, not 'Other'");
        }
        foreach (var f in devices.GroupBy(t => t.Function).Where(g => g.Count() > 1))
            Check(entries.Any(x => x.Id == "tiers." + f.Key && x.Group == "All sizes"), $"{f.Key} has its every-size row");
        Check(entries.Select(e => e.Id).Distinct().Count() == entries.Count, "ids are unique");
        foreach (string need in new[] { "chair", "thruster", "constructor", "generator", "gyro", "container", "turret", "weapon", "door" })
            Check(devices.Any(t => t.Function == need), $"the game has a '{need}' component");
        GD.Print($"  {entries.Count} entries");

        GD.Print("== builds in two states and every animation plays");
        int played = 0;
        foreach (AssetEntry e in entries)
        {
            foreach (bool alt in new[] { false, true })
            {
                StructureAsset a = null;
                try
                {
                    a = (StructureAsset)e.Build(Request(e, alt));
                    Check(a.Facts().Any(), $"{e.Key} has facts");
                    Check(a.FrameBounds is Aabb box && box.Size.Length() > 0.5f, $"{e.Key} has a frame");
                    if (alt) continue;
                    AddChild(a);
                    foreach (string anim in a.Animations)
                    {
                        a.PlayAnimation(anim);
                        await Frames(4);
                        played++;
                    }
                }
                catch (Exception ex) { Check(false, $"{e.Key} threw: {ex.GetBaseException().Message}"); }
                finally { if (a != null && a.GetParent() != null) { RemoveChild(a); a.Release(); a.Free(); } else a?.Free(); }
            }
        }
        GD.Print($"  {played} animations played");
        Check(played > 100, "animations were played");

        GD.Print("== the chair is sat in");
        await Behaviour("component.command-chair", null, 3, (a, t) =>
        {
            Node3D person = Person(a);
            if (!Check(person != null, "a figure sits in the chair")) return;
            var (anchor, block) = a.Structure.Blocks().First();
            System.Numerics.Vector3 seat = StructureBridge.SeatPoint(a.Structure, anchor, block);
            // Seated at the start: the figure's floor point is under the seat, within a metre of it.
            Check((person.Position - new Vector3(seat.X, seat.Y, seat.Z)).Length() < 1.2f, "the figure is at the seat");
        });

        GD.Print("== thruster flames leave the end of the body");
        foreach (BlockType t in devices.Where(t => t.IsThruster))
        {
            AssetEntry e = entries.First(x => x.Id == "component." + t.Id);
            var a = (StructureAsset)e.Build(Request(e, false));
            AddChild(a);
            a.PlayAnimation("Full burn");
            await Frames(3);
            var fx = a.Holder.GetChildren().OfType<ThrusterFx>().FirstOrDefault();
            if (Check(fx != null && fx.Count == 1, $"{t.Id} has one flame"))
            {
                MeshInstance3D flame = fx.GetChildren().OfType<MeshInstance3D>().First();
                var (anchor, block) = a.Structure.Blocks().First();
                System.Numerics.Vector3 centre = StructureBridge.BlockCenter(a.Structure, anchor, block);
                Int3 size = Footprint.WorldSize(t, block.Rot);
                // The flame's near end is at or beyond the body's far face along the exhaust.
                var n = Orientations.ToWorld(block.Rot, Dir.PosZ).Normal();
                float half = 0.5f * (Math.Abs(n.X) * size.X + Math.Abs(n.Y) * size.Y + Math.Abs(n.Z) * size.Z);
                Vector3 exhaust = new(n.X, n.Y, n.Z);
                Vector3 nearEnd = flame.Position - exhaust * (flame.Scale.Y * 0.5f);
                float out_ = (nearEnd - new Vector3(centre.X, centre.Y, centre.Z)).Dot(exhaust);
                Check(flame.Visible && out_ >= half - 0.05f, $"{t.Id} flame starts {out_:0.00} m from the middle, body ends {half:0.00} m");
            }
            RemoveChild(a);
            a.Release();
            a.Free();
        }

        GD.Print("== a door opens for the walker");
        await Behaviour("component.door-1x2x1", "Walk through", 1, null, async a =>
        {
            var (anchor, _) = a.Structure.Blocks().First();
            bool opened = false, shut = false;
            for (int i = 0; i < 400; i++)
            {
                await Frames(1);
                a.Structure.TryGet(anchor, out Block b);
                if ((b.Flags & BlockFlags.Open) != 0) opened = true; else shut = true;
            }
            Check(opened && shut, "the door opens and shuts as the figure comes and goes");
        });

        GD.Print("== the climber and the stair walker rise");
        foreach (var (id, anim, rise) in new[] { ("component.ladder", "Climb up and down", 1.5f), ("component.stairs", "Walk up and down", 1.5f) })
            await Behaviour(id, anim, 1, null, async a =>
            {
                float top = float.MinValue, bottom = float.MaxValue;
                for (int i = 0; i < 500; i++)
                {
                    await Frames(1);
                    Node3D p = Person(a);
                    if (p == null) break;
                    top = Math.Max(top, p.Position.Y);
                    bottom = Math.Min(bottom, p.Position.Y);
                }
                Check(top - bottom >= rise, $"{id}: the figure rises {top - bottom:0.0} m (wanted {rise})");
            });
    }

    /// <summary>Builds an entry's asset in the tree, plays an animation, and checks it after a few frames (or runs a coroutine on it).</summary>
    async System.Threading.Tasks.Task Behaviour(string id, string animation, int frames, Action<StructureAsset, int> check, Func<StructureAsset, System.Threading.Tasks.Task> run = null)
    {
        AssetEntry e = registryEntry(id);
        if (!Check(e != null, $"{id} exists")) return;
        var a = (StructureAsset)e.Build(Request(e, false));
        AddChild(a);
        if (animation != null) a.PlayAnimation(animation);
        await Frames(frames);
        check?.Invoke(a, frames);
        if (run != null) await run(a);
        RemoveChild(a);
        a.Release();
        a.Free();
    }

    AssetEntry registryEntry(string id) => AssetRegistry.Discover().Entries.FirstOrDefault(e => e.Category == "Components" && e.Id == id);
}
