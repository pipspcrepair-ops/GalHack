using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using Godot;
using GalHack.Physics.Engine;
using PoiGenerator.Gen;
using Slice.Places;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// A Husk ship setting its heading to ram (owner, 16 Nov): a hijacked hull of any faction adrift in orbit ahead of the
/// Skiff, another faction's ship further off its bow, the worms in its meat writhing while it idles; then it sets the
/// heading (<see cref="HuskWorms.ReachToward"/>) and they stretch and strain toward the other ship; then it relaxes.
/// Frames into --dir as husk-ram-idle, husk-ram-idle2 (the writhing a few seconds on), husk-ram-reach and husk-ram-wide.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> HuskRamView()
    {
        string dir = _args.GetValueOrDefault("dir", ".");
        System.IO.Directory.CreateDirectory(dir);
        foreach (int x in ToOrbit()) yield return x;
        for (int i = 0; i < 1200 && _game.Orbit is { Ready: false }; i++) yield return 0;
        _game.Visor.Visible = false;
        var lib = Slice.Game.WorldChoice.Library;
        var cat = _game.Zone.Data.Catalogue;
        SiteDef site = lib.Sites.Values.First(x => x.Parts.Count > 0 && x.Parts[0].Arg("what") == "capital");
        string[] styled = { "freeholders", "meridian", "exchange", "reavers", "kessari", "vharn" };
        {
            Vector3 from = Skiff.Body.Position.ToGodot(), world0 = _game.Orbit.ToWorld(N.Vector3.Zero).ToGodot();
            Hurry(3);
            foreach (int x in FlyRound(Skiff, from + (from - world0).Normalized() * 1500f, 60f, 150f)) yield return x;
            Hurry(1);
        }
        N.Vector3 skiff = Skiff.Body.Position, world = _game.Orbit.ToWorld(N.Vector3.Zero), upN = N.Vector3.Normalize(skiff - world);
        N.Vector3 ahead = N.Vector3.Normalize(N.Vector3.Cross(upN, N.Vector3.UnitX) is var c && c.LengthSquared() > 0.01f ? c : N.Vector3.Cross(upN, N.Vector3.UnitZ));
        N.Vector3 along = N.Vector3.Normalize(N.Vector3.Cross(ahead, upN));
        N.Quaternion turn = N.Quaternion.Normalize(N.Quaternion.CreateFromRotationMatrix(N.Matrix4x4.CreateWorld(N.Vector3.Zero, along, upN)));
        // The Husk ship broadside on ahead of the Skiff; its prey off its bow (the hull's -z), its own faction's look.
        string husk = styled[Random.Shared.Next(styled.Length)], prey = styled.Where(f => f != husk).ElementAt(Random.Shared.Next(5));
        Vessel Spawn(string faction, bool hijacked, N.Vector3 middle, string id, out HullVessel.Result r)
        {
            ulong seed = (ulong)Random.Shared.NextInt64();
            var plan = CapitalHull.Plan(lib.Kits[site.Style], WorldProfile.Parse(site.Parts[0].Arg("tech", "mid")), new CapitalHull.Spec("warship", "large", "long", faction, hijacked), seed);
            r = HullVessel.Build(plan, plan.Pieces[0], cat, hijacked ? PoiState.Infested : PoiState.Active, seed, intact: true);
            var (lo, hi) = r.Structure.Bounds();
            N.Vector3 at = middle - N.Vector3.Transform(new N.Vector3((lo.X + hi.X) * 0.5f, (lo.Y + hi.Y) * 0.5f, (lo.Z + hi.Z) * 0.5f), turn);
            Vessel v = Vessel.SpawnDerelict(_game.Orbit, r.Structure, at, turn);
            v.Derelict = new Slice.Orbit.DerelictPlan { Id = id, Name = r.Structure.Name, Structure = r.Structure, Position = at, Rotation = turn, Hull = r, State = hijacked ? PoiState.Infested : PoiState.Active };
            GD.Print($"husk-ram: {(hijacked ? "Husk " : "")}{faction} {plan.Form.Plan}, {plan.L} m, {r.Worms.Count} worms");
            return v;
        }
        N.Vector3 mine = skiff + ahead * 260f;
        Vessel ship = Spawn(husk, true, mine, "husk-ram", out HullVessel.Result hull);
        Vessel target = Spawn(prey, false, mine - along * 420f + upN * 60f, "husk-prey", out _);
        Slice.Vessels.HuskWorms worms = Slice.Vessels.HuskWorms.Grow(ship.Node, hull.Worms, 7);
        if (worms == null)
        {
            GD.PrintErr("husk-ram: no worms on the Husk ship");
            yield break;
        }
        foreach (int x in _drive.Wait(60)) yield return x;
        // Close on the lump whose worms face the prey best, from its side, the prey beyond them.
        Vector3 prey3 = target.Node.ToGlobal(Vector3.Zero), centre = ship.Node.ToGlobal(Vector3.Zero);
        var root = hull.Worms.OrderByDescending(w => (ship.Node.ToGlobal(new Vector3(w.At.X, w.At.Y, w.At.Z)) - centre).Normalized().Dot((prey3 - centre).Normalized()) * 2f + w.Length * 0.05f).First();
        Vector3 at3 = ship.Node.ToGlobal(new Vector3(root.At.X, root.At.Y, root.At.Z)), toPrey = (prey3 - at3).Normalized(), up = upN.ToGodot();
        Vector3 side = toPrey.Cross(up).Normalized();
        Vector3 eye = at3 + side * 34f + up * 10f - toPrey * 18f, look = at3 + toPrey * 12f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, up), eye);
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in Shot($"{dir}/husk-ram-idle.png")) yield return x;
        foreach (int x in _drive.Wait(240)) yield return x;
        foreach (int x in Shot($"{dir}/husk-ram-idle2.png")) yield return x;
        worms.ReachToward(prey3);
        foreach (int x in _drive.Wait(240)) yield return x;
        GD.Print($"husk-ram: reaching {worms.Reaching:F2} toward the {prey} ship {at3.DistanceTo(prey3):F0} m off");
        foreach (int x in Shot($"{dir}/husk-ram-reach.png")) yield return x;
        Vector3 wide = centre + side * 260f + up * 90f - toPrey * 120f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt((centre + prey3) * 0.5f - wide, up), wide);
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in Shot($"{dir}/husk-ram-wide.png")) yield return x;
        worms.Relax();
        foreach (int x in _drive.Wait(240)) yield return x;
        GD.Print($"husk-ram: relaxed to {worms.Reaching:F2}");
        _game.FixedCamera = null;
    }
}
