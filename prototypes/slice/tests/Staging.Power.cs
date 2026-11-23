using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Infra;
using Slice.Player;
using NV = System.Numerics.Vector3;

namespace Slice.Tests;

/// <summary>
/// Power poles and pylons felled as trees are (--view=power; a world with a grid: --era-old=mid or high): for a pole and then a
/// pylon, the nearest standing one shot through at its foot until it goes over, then the next nearest cut down with the Sleeper
/// tool; each seen standing, going over and lying, from one eye side-on to the way it falls (its whole fall in frame), the
/// sleeper stood behind that eye. The run fails unless each went over (falling as a tree does, and lying flat),
/// its drawing went from its tiles, and every wire it held now hangs from the pole at its other end, the wire's top at that
/// pole's insulator and its tail on the ground: nothing strung to it, nothing in the air. Then the dressing's nearest post or
/// mast (a lamp post, a sign post) the same way, shot and then cut.
/// </summary>
public sealed partial class Staging
{
    /// <summary>How many shots or pulls of the tool the last felling took.</summary>
    int _blows;

    IEnumerable<int> PowerView()
    {
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        GD.Print($"world: seed {_game.World.Seed}, {_game.World}");
        PowerLines lines = _game.Zone.Planet.Infra.Power;
        GD.Print($"(power: {lines.Poles.Count} poles, {lines.Poles.Count(q => q.Pylon)} pylons, {lines.Poles.Count(q => q.Fallen)} down, {lines.Wires.Count} wires, {lines.Drops.Count} house drops; roles {string.Join(", ", lines.Poles.GroupBy(q => q.Role).Select(g => $"{g.Key} {g.Count()}"))})");
        if (!lines.Poles.Any(q => q.Role == PowerRole.Line && !q.Fallen))
        {
            GD.Print("  FAIL  no standing pole or pylon on this world (run it on one with a grid: --era-old=mid)");
            Faults++;
            yield break;
        }
        Vector3 from = _game.Survivor.WorldFeet;
        Vector3 At(int i) => _game.Zone.Planet.NearestCopy(new Vector3((float)lines.Poles[i].X, lines.Poles[i].Ground, (float)lines.Poles[i].Z), from);
        float Ground(double x, double z) => _game.Zone.GroundAt((float)x, (float)z).Y;
        foreach (bool pylon in new[] { false, true })
        foreach (bool cut in new[] { false, true })
        {
            string what = pylon ? "pylon" : "pole", how = cut ? "cut" : "shot";
            int pole = Enumerable.Range(0, lines.Poles.Count).Where(i => lines.Poles[i].Role == PowerRole.Line && !lines.Poles[i].Fallen && lines.Poles[i].Pylon == pylon)
                .OrderBy(i => At(i).DistanceTo(from)).DefaultIfEmpty(-1).First();
            if (pole < 0) { GD.Print($"(power: no standing {what} left on this world)"); continue; }
            PowerPole p = lines.Poles[pole];
            Vector3 foot = At(pole), side = new(p.SideX, 0f, p.SideZ), along = new(-p.SideZ, 0f, p.SideX);
            // It goes over away from the blow: cut from a step off along the line (a pylon across it), shot from across it.
            Vector3 fall = cut && !pylon ? -along : -side;
            var (eye, look) = SideOn(foot, fall, p.Height);
            foreach (int x in Shot($"{what}-{how}-before", eye, look)) yield return x;
            int felled = _game.Zone.PolesFelled, rebuilt = _game.Zone.Infra.PartsRebuilt, blows = 0;
            var held = Enumerable.Range(0, lines.Wires.Count).Where(i => lines.Wires[i].Spanned && (lines.Wires[i].A == pole || lines.Wires[i].B == pole)).ToList();
            var drops = lines.Drops.Where(d => d.Pole == pole).ToList();
            // A pylon is cut at one of its legs and shot with rockets.
            foreach (int x in Fell(cut, pylon, pylon ? foot + (side + along) * (p.Height * 0.13f) : foot, pylon ? side : along, side, () => _game.Zone.PolesFelled > felled, eye, look, $"{what}-{how}")) yield return x;
            blows = _blows;
            float lean = (_game.Zone.Trees.GetChildren().OfType<FallingTree>().LastOrDefault()?.Basis.Y.AngleTo(Vector3.Up) ?? 0f);
            bool drawn = !p.Felled || _game.Zone.Infra.PartsRebuilt == rebuilt || _game.Zone.Infra.PartsPending;
            // Each wire it held: hanging from the pole at its other end, its top on that pole's insulator, its tail on the ground.
            int strung = 0, hanging = 0, loose = 0;
            float worstTop = 0f, worstTail = 0f;
            foreach (int i in held)
            {
                PowerWire w = lines.Wires[i];
                if (w.Spanned) { strung++; continue; }
                if (w.A == pole) { loose++; continue; }
                hanging++;
                List<NV> path = lines.Path(w, Ground);
                PowerPole a = lines.Poles[w.A];
                NV anchor = a.Anchor(w.K, lines.WiresOn(a));
                worstTop = Mathf.Max(worstTop, NV.Distance(path[0], anchor));
                worstTail = Mathf.Max(worstTail, Mathf.Abs(path[^1].Y - Ground(path[^1].X, path[^1].Z)));
            }
            int dropsUp = drops.Count(d => d.State is DropState.Strung or DropState.FromPole);
            bool ok = p.Fallen && lean > 1.3f && !drawn && strung == 0 && loose == 0 && hanging == held.Count && worstTop < 0.1f && worstTail < 0.3f && dropsUp == 0;
            if (!ok) Faults++;
            GD.Print($"  {(ok ? "ok   " : "FAIL ")} the {what} ({pole}, {p.Height:F0} m) {how} through: over after {blows} {(cut ? "pulls of the tool" : "shots")} (fallen {p.Fallen}, lying {Mathf.RadToDeg(lean):F0} degrees over, " +
                     $"its drawing {(drawn ? "still in its tiles" : "gone from its tiles")}); of its {held.Count} wires {hanging} hang from the pole at their other end (top off its insulator at most {worstTop:F2} m, " +
                     $"tail off the ground at most {worstTail:F2} m), {strung} still strung to it, {loose} hanging from it; of its {drops.Count} house drops {dropsUp} still held up by it");
            foreach (int x in Shot($"{what}-{how}-lying", eye, look)) yield return x;
            from = _game.Survivor.WorldFeet;
        }
        // The dressing's posts and masts stand the same way (lamp posts, sign posts, beacons, flag and banner poles): the
        // nearest shot through, the next cut down.
        var items = _game.Zone.Planet.Dress.Items;
        foreach (bool cut in new[] { false, true })
        {
            string how = cut ? "cut" : "shot";
            int k = Enumerable.Range(0, items.Count).Where(i => !items[i].Felled && TileBuilder.Stands(items[i]))
                .OrderBy(i => new Vector2((float)items[i].X - from.X, (float)items[i].Z - from.Z).Length()).DefaultIfEmpty(-1).First();
            if (k < 0) { GD.Print("(power: no standing post or mast on this world)"); break; }
            DressItem it = items[k];
            Vector3 foot = _game.Zone.Planet.NearestCopy(new Vector3((float)it.X, _game.Zone.GroundAt((float)it.X, (float)it.Z).Y, (float)it.Z), from);
            Vector3 side = new(Mathf.Cos(it.Yaw), 0f, -Mathf.Sin(it.Yaw)), along = side.Cross(Vector3.Up);
            var (eye, look) = SideOn(foot, cut ? -along : -side, 6f);
            foreach (int x in Shot($"post-{how}-before", eye, look)) yield return x;
            int felled = _game.Zone.PostsFelled, rebuilt = _game.Zone.Infra.PartsRebuilt;
            foreach (int x in Fell(cut, false, foot, along, side, () => _game.Zone.PostsFelled > felled, eye, look, $"post-{how}")) yield return x;
            float lean = (_game.Zone.Trees.GetChildren().OfType<FallingTree>().LastOrDefault()?.Basis.Y.AngleTo(Vector3.Up) ?? 0f);
            bool drawn = _game.Zone.Infra.PartsRebuilt == rebuilt || _game.Zone.Infra.PartsPending;
            bool ok = it.Felled && lean > 1.3f && !drawn;
            if (!ok) Faults++;
            GD.Print($"  {(ok ? "ok   " : "FAIL ")} a {it.Model} {how} through: over after {_blows} {(cut ? "pulls of the tool" : "shots")} (felled {it.Felled}, lying {Mathf.RadToDeg(lean):F0} degrees over, its drawing {(drawn ? "still in its tiles" : "gone from its tiles")})");
            foreach (int x in Shot($"post-{how}-lying", eye, look)) yield return x;
            from = _game.Survivor.WorldFeet;
        }
        _game.FixedCamera = null;

        // An eye side-on to a fall: across the line it falls along, back by more than its length, its whole fall in frame.
        (Vector3 Eye, Vector3 Look) SideOn(Vector3 foot, Vector3 fall, float height)
        {
            Vector3 mid = foot + fall * (height * 0.5f), eye = mid + fall.Cross(Vector3.Up) * (height * 1.3f + 6f);
            eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f;
            return (eye, mid + Vector3.Up * (height * 0.3f));
        }

        // Cut at waist height with the tool in hand (as the start gives it), stood a step off along <paramref name="off"/>; or shot
        // at its foot from 8 m off along <paramref name="side"/>, with rockets for a pylon; then its fall seen from the eye (the
        // sleeper behind it), halfway down, and its tiles built again.
        IEnumerable<int> Fell(bool cut, bool rockets, Vector3 at, Vector3 off, Vector3 side, System.Func<bool> down, Vector3 eye, Vector3 look, string tag)
        {
            _blows = 0;
            Vector3 waist = at with { Y = _game.Zone.GroundAt(at.X, at.Z).Y + 1f };
            if (cut)
            {
                Vector3 stand = at + off * 1.3f;
                _game.FixedCamera = null;
                _game.Survivor.PlaceOutside((stand with { Y = _game.Zone.GroundAt(stand.X, stand.Z).Y + 0.05f }).ToNumerics());
                foreach (int x in _drive.Wait(30)) yield return x;
                for (; _blows < 40 && !down(); _blows++)
                    foreach (int x in _drive.HoldPrimary(waist, 30)) yield return x;
            }
            else
            {
                GalHack.Arms.WeaponDef gun = rockets ? WorldFire.Rocket(_game) : _game.Zone.Data.Arms.OfItem(Wildlife.Weapons.ItemOf("rifle"));
                for (; _blows < 120 && !down(); _blows++)
                {
                    WorldFire.Fire(_game, gun, waist + side * 8f + Vector3.Up * 0.5f, waist);
                    foreach (int x in _drive.Wait(rockets ? 20 : 5)) yield return x;
                }
            }
            // Its fall (the newest falling tree is it), a tree's, slower under a light world's gravity: seen from the eye as it
            // passes halfway down, till it lies, or 15 s.
            _game.Survivor.PlaceOutside((eye + (eye - look).Normalized() * 3f - Vector3.Up * 1.6f).ToNumerics());
            _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
            bool seen = false;
            for (int i = 0; i < 900; i++)
            {
                float lean = _game.Zone.Trees.GetChildren().OfType<FallingTree>().LastOrDefault()?.Basis.Y.AngleTo(Vector3.Up) ?? 0f;
                if (!seen && lean > 0.6f)
                {
                    Snap($"{tag}-falling");
                    GD.Print($"  (falling: {Mathf.RadToDeg(lean):F0} degrees over)");
                    seen = true;
                }
                if (i >= 30 && lean >= 1.5f) break;
                yield return 0;
            }
            for (int i = 0; i < 300 && _game.Zone.Infra.PartsPending; i++) yield return 0;
        }

        IEnumerable<int> Shot(string tag, Vector3 eye, Vector3 look)
        {
            _game.Survivor.PlaceOutside((eye + (eye - look).Normalized() * 3f - Vector3.Up * 1.6f).ToNumerics()); // behind the eye: its own body out of the frame
            for (int i = 0; i < 900; i++)
            {
                _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
                _game.Zone.Sky.TimeOfDay = HighSun(_game);
                yield return 0;
                if (i > 240 && _game.Zone.Streamer.Settled) break;
            }
            Snap(tag);
        }
    }
}
