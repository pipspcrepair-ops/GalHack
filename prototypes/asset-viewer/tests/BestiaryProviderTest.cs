using System;
using System.Collections.Generic;
using System.Linq;
using AssetViewer.Providers;
using Godot;
using Wildlife;
using Landform = Planets.Gen.Landform;

namespace AssetViewer.Tests;

/// <summary>
/// The Bestiary and People providers: every id in the game's data has an entry, every entry builds
/// (two seeds each, and rolled options), every animation it lists runs on the real rig without a bad
/// pose, and different animations of one creature look different.
/// </summary>
public partial class BestiaryProviderTest : Node
{
    int _passes, _failures;
    readonly List<string> _failed = new();

    bool Check(bool ok, string what)
    {
        if (ok) _passes++; else { _failures++; _failed.Add(what); GD.PrintErr("  FAIL " + what); }
        return ok;
    }

    public override async void _Ready()
    {
        try { Run(); }
        catch (Exception e) { _failures++; GD.PrintErr("  FAIL threw: " + e); }
        GD.Print($"\n{_passes} checks passed, {_failures} failed");
        foreach (string f in _failed.Take(40)) GD.Print("  failed: " + f);
        GD.Print(_failures == 0 ? "ALL PASSED" : "SOME CHECKS FAILED");
        await System.Threading.Tasks.Task.Yield();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    static BuildRequest Request(AssetEntry e, ulong seed, Func<AssetOption, string> pick = null)
    {
        var r = new BuildRequest { Seed = seed };
        foreach (AssetOption o in e.Options)
            r.Options[o.Name] = o.Values.Length == 0 ? "" : pick != null ? pick(o) : o.Values[Math.Clamp(o.Default, 0, o.Values.Length - 1)];
        return r;
    }

    void Run()
    {
        WildlifeData data = DataFiles.LoadWildlife();
        SpaceFaunaData space = DataFiles.LoadSpace();
        var registry = AssetRegistry.Discover();
        Check(registry.Failures.Count == 0, "providers list cleanly: " + string.Join("; ", registry.Failures.Select(f => f.Provider + ": " + f.Error)));
        var mine = registry.Entries.Where(e => e.Category is "Bestiary" or "People").ToList();
        GD.Print($"  {mine.Count(e => e.Category == "Bestiary")} Bestiary and {mine.Count(e => e.Category == "People")} People entries");
        Check(mine.Count(e => e.Category == "Bestiary") > 50, "the Bestiary has entries");

        GD.Print("== every id in the data has an entry");
        var ids = new HashSet<string>(mine.Select(e => e.Key));
        bool Has(string key) => ids.Contains(key);
        foreach (FormDef f in data.Fauna.Forms) Check(Has($"Bestiary|form/{f.Id}"), $"form {f.Id} has an entry");
        foreach (DomesticKind k in Enum.GetValues<DomesticKind>()) if (k != DomesticKind.None) Check(Has($"Bestiary|domestic/{k.ToString().ToLowerInvariant()}"), $"domestic {k}");
        foreach (string role in data.Deep.Roles.Keys) Check(Has($"Bestiary|place/{role.ToLowerInvariant()}"), $"role {role}");
        foreach (Landform l in Enum.GetValues<Landform>()) Check(Has($"Bestiary|landform/{l.ToString().ToLowerInvariant()}"), $"landform {l}");
        foreach (SpaceFormDef f in space.Forms) Check(Has($"Bestiary|space/{f.Id}"), $"space form {f.Id}");
        foreach (BreakerKind k in Enum.GetValues<BreakerKind>()) if (k != BreakerKind.None) Check(Has($"Bestiary|breaker/{k.ToString().ToLowerInvariant()}"), $"breaker {k}");
        foreach (var (id, kind) in data.Signature.Kinds)
            Check(kind.Body == "person" ? Has($"People|kind/{id}") : Has($"Bestiary|kind/{id}"), $"signature kind {id}");
        foreach (PersonRole role in Enum.GetValues<PersonRole>()) { Check(Has($"People|role/{role.ToString().ToLowerInvariant()}"), $"person role {role}"); if (role != PersonRole.Child) Check(Has($"People|husk/{role.ToString().ToLowerInvariant()}"), $"husk of {role}"); }
        foreach (string prop in ZooPeople.Props(data)) Check(Has($"People|held/{prop}"), $"held {prop}");
        Check(Has("People|castaway"), "the castaway");
        foreach (ClimateBand b in Enum.GetValues<ClimateBand>()) Check(Has($"People|outfits/{b.ToString().ToLowerInvariant()}"), $"outfits for {b}");

        GD.Print("== every entry builds, two seeds and rolled options");
        var rng = new RandomNumberGenerator { Seed = 99 };
        var stages = new List<(AssetEntry Entry, AssetInstance Instance)>();
        foreach (AssetEntry e in mine)
        {
            try
            {
                AssetInstance a = e.Build(Request(e, Viewer.DefaultSeed(e)));
                if (Check(a != null, $"{e.Key} builds"))
                {
                    AddChild(a);
                    stages.Add((e, a));
                    Check(a.Facts().Any(), $"{e.Key} has facts");
                }
                if (e.Randomisable)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        AssetInstance b = e.Build(Request(e, (ulong)(1000 + i * 37), o => o.Rollable ? o.Values[rng.RandiRange(0, o.Values.Length - 1)] : o.Values[Math.Clamp(o.Default, 0, o.Values.Length - 1)]));
                        Check(b != null, $"{e.Key} rerolls");
                        if (b != null) AddChild(b);
                        if (b is IStageProbe pb && b.Animations.Count > 0)
                        {
                            b.PlayAnimation(b.Animations[(i + 1) % b.Animations.Count]);
                            pb.Advance(1.0f);
                            string p = pb.Problems();
                            Check(p == "", $"{e.Key} reroll {i}: {p}");
                        }
                        b?.Free();
                    }
                }
            }
            catch (Exception ex) { Check(false, $"{e.Key} threw: {ex.GetBaseException().Message} {ex.GetBaseException().StackTrace?.Split('\n').FirstOrDefault()}"); }
        }

        GD.Print("== every animation runs, and they differ");
        int anims = 0, differ = 0, same = 0;
        foreach (var (e, a) in stages)
        {
            if (a is not IStageProbe probe || a.Animations.Count == 0) continue;
            var signatures = new HashSet<long>();
            foreach (string anim in a.Animations)
            {
                try
                {
                    a.PlayAnimation(anim);
                    probe.Advance(2.4f);
                    anims++;
                    string p = probe.Problems();
                    Check(p == "", $"{e.Key} / {anim}: {p}");
                    signatures.Add(probe.PoseSignature());
                }
                catch (Exception ex) { Check(false, $"{e.Key} / {anim} threw: {ex.GetBaseException().Message}"); }
            }
            if (a.Animations.Count >= 3)
            {
                bool ok = signatures.Count >= Math.Max(2, a.Animations.Count * 6 / 10);
                if (ok) differ++; else same++;
                Check(ok, $"{e.Key}: {a.Animations.Count} animations give only {signatures.Count} different poses");
            }
        }
        GD.Print($"  {anims} animations run on {stages.Count} entries; {differ} creatures with clearly different animations, {same} without");
        foreach (var (_, a) in stages) a.Free();
    }
}
