using System;
using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Physics.Engine;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Player;
using Slice.Vessels;
using N = System.Numerics;
using S = GalHack.Structures;

namespace Slice.Tests;

/// <summary>
/// The plating gun (the survival tool's PLATE mode), on a fresh random world, through the game's
/// own keys: a base block on a base, aimed at. A locked plating is refused; the left button sprays
/// iron plates on; a damaged iron block re-plated with hardened plates gives back its iron by its
/// hit-point share, rounded down; a damaged hardened block sprayed again is repaired with plates
/// for the damage; the right button strips it back to its base block for the same refund. Each
/// step changes the block, and the bag, by exactly that.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/PlatingTest.tscn
/// </summary>
public partial class PlatingTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        var play = new Playthrough(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        foreach (int x in play.MakeTool()) yield return x;

        S.Catalogue cat = _game.Zone.Data.Catalogue;
        var s = new S.Structure(cat) { Name = "Plating test" };
        S.BlockType bare = cat.Type("base-block"), iron = cat.Type("iron-plated"), hard = cat.Type("hardened-plated");
        s.Set(S.Int3.Zero, S.Building.Make(s, cat.CoreType));
        for (int x = 1; x <= 3; x++) s.Set(new S.Int3(x, 0, 0), S.Building.Make(s, bare));
        s.DirtyChunks.Clear();
        Vector3 site = play.FlatSpot(_game.Start, 30f, span: 8f);
        Vector3 ground = _game.Zone.GroundAt(site.X, site.Z);
        Vessel v = Vessel.SpawnBase(_game.Zone, s, (ground + Vector3.Up * (s.Grid.CellSize * 0.5f + 0.4f)).ToNumerics(), 0f);
        foreach (var (p, _) in s.Blocks()) v.ClearGround(p);
        var cell = new S.Int3(2, 0, 0);
        Vector3 face = v.ToWorld(StructureBridge.CellCenter(s, cell) + new N.Vector3(0f, 0f, s.Grid.CellSize * 0.5f));
        _game.Survivor.PlaceOutside((face + new Vector3(0f, -0.4f, 3f)).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in play.Tool(ToolMode.Plate)) yield return x;
        Check(_game.Tool.Mode == ToolMode.Plate, "Q steps the survival tool into PLATE mode");
        var builder = _game.Builder;
        var pack = _game.Pack;
        pack.Add("iron-plate", 10);
        pack.Add("hardened-plate", 10);
        S.Block B() => s.Get(cell);
        float Max() => s.StatsOf(B()).MaxHp;
        int Iron() => pack["iron-plate"];
        int Hard() => pack["hardened-plate"];
        void Hurt(float share) { s.SetHp(cell, Max() * share); v.ApplyEdits(); }

        IEnumerable<int> Pick(S.BlockType plating)
        {
            for (int i = 0; i < builder.Platings.Count && builder.Plating != plating; i++)
            {
                _game.Controls.Wheel = 1;
                yield return 0;
            }
        }
        IEnumerable<int> Aim(bool right = false)
        {
            foreach (int x in _drive.LookAt(face, 6)) yield return x;
            if (right)
            {
                _game.Controls.SecondaryPressed = _game.Controls.Secondary = true;
                yield return 0;
                _game.Controls.Secondary = false;
                yield return 0;
            }
            else foreach (int x in _drive.Click()) yield return x;
            foreach (int x in _drive.Wait(2)) yield return x;
        }

        // Locked: hardened plating is a later unlock.
        foreach (int x in Pick(hard)) yield return x;
        foreach (int x in _drive.LookAt(face, 6)) yield return x;
        Check(builder.Plating == hard && builder.Verdict.Contains("LOCKED"), $"the wheel picks the plating; a locked one says so: \"{builder.Verdict}\"");
        foreach (int x in Aim()) yield return x;
        Check(s.TypeOf(B()) == bare && Hard() == 10, "and a click sprays nothing");

        // Spray: a bare base block takes iron plates.
        foreach (int x in Pick(iron)) yield return x;
        int plates = cat.PlatesOf(S.Building.Make(s, iron));
        foreach (int x in _drive.LookAt(face, 6)) yield return x;
        string said = builder.Verdict;
        foreach (int x in Aim()) yield return x;
        Check(s.TypeOf(B()) == iron && Mathf.IsEqualApprox(B().Hp, Max()) && Iron() == 10 - plates,
            $"sprayed: the base block is iron plated, whole, for {plates} iron plates (bag {Iron()}); the line said \"{said}\"");

        // Re-plate a damaged iron block with hardened: its iron back by its hit-point share, rounded down.
        _game.Progress.Creative = true; // the tech opens; the bag still pays
        Hurt(0.6f);
        int refund = (int)MathF.Floor(plates * 0.6f + 1e-4f);
        foreach (int x in Pick(hard)) yield return x;
        foreach (int x in Aim()) yield return x;
        Check(s.TypeOf(B()) == hard && Mathf.IsEqualApprox(B().Hp, Max()) && Hard() == 10 - plates && Iron() == 10 - plates + refund,
            $"re-plated at 60%: hardened, whole, for {plates} hardened plates, {refund} iron back (bag: {Iron()} iron, {Hard()} hardened)");

        // Repair: the same plating on a damaged block tops it up, plates for the damage.
        Hurt(0.3f);
        int cost = (int)MathF.Ceiling(plates * 0.7f - 1e-4f), before = Hard();
        foreach (int x in _drive.LookAt(face, 6)) yield return x;
        said = builder.Verdict;
        foreach (int x in Aim()) yield return x;
        Check(s.TypeOf(B()) == hard && Mathf.IsEqualApprox(B().Hp, Max()) && Hard() == before - cost,
            $"repaired from 30%: whole again for {cost} hardened plates (bag {Hard()}); the line said \"{said}\"");
        foreach (int x in Aim()) yield return x;
        Check(Hard() == before - cost && builder.Verdict.Contains("WHOLE"), $"a whole block takes nothing more: \"{builder.Verdict}\"");

        // Strip: back to the base block, keeping its share; the plates back, rounded down.
        Hurt(0.55f);
        refund = (int)MathF.Floor(plates * 0.55f + 1e-4f);
        before = Hard();
        foreach (int x in Aim(right: true)) yield return x;
        Check(s.TypeOf(B()) == bare && Mathf.IsEqualApprox(B().Hp, Max() * 0.55f) && Hard() == before + refund,
            $"stripped at 55%: a base block at {B().Hp / Max():P0}, {refund} hardened plates back (bag {Hard()})");
        _game.Progress.Creative = false;
    }
}
