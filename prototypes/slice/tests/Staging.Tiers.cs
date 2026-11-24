using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>--view=tiershots: each device ladder's rungs side by side on a showroom floor, one PNG a ladder in --dir.</summary>
public sealed partial class Staging
{
    /// <summary>Tier ladders on a showroom floor: --ladder=thruster,generator.</summary>
    IEnumerable<int> TierShots()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Sky.Paused = true;
        _game.Visor.Visible = false;
        string dir = _args.GetValueOrDefault("dir", ".");
        Catalogue cat = _game.Zone.Data.Catalogue;
        foreach (string ladder in _args.GetValueOrDefault("ladder", "thruster,generator").Split(','))
        {
            var types = cat.Types.Where(t => t != null && t.Id.StartsWith(ladder + "-t")).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
            var s = new Structure(cat) { Name = "Tiers" };
            BlockType steel = cat.Type("iron-plated");
            int total = types.Sum(t => t.Size.X + 1) + 2;
            int depth = types.Max(t => Math.Max(t.Size.Z, t.Size.X)) + 3;
            for (int x = -1; x <= total; x++)
                for (int z = -2; z <= depth; z++) s.Set(new Int3(x, 0, z), Building.Make(s, steel));
            int cx = 1;
            float sideDir = (float)Num("side", ladder == "thruster" ? 1 : -1);
            if (sideDir < 0) types.Reverse(); // small to large left to right from the front
            foreach (BlockType t in types)
            {
                var c = new Int3(cx, 1, 0);
                s.Set(c, Building.Make(s, t));
                GD.Print($"{t.Id}: size {t.Size.X}x{t.Size.Y}x{t.Size.Z} at {c}");
                cx += t.Size.X + 1;
            }
            s.DirtyChunks.Clear();
            float cell = s.Grid.CellSize;
            float span = (total + 2) * cell;
            Vector3 site = Play.FlatSpot(_game.Start, 60f, 0.4f, 0.5f, span: MathF.Max(10f, span * 0.6f));
            _game.Survivor.PlaceOutside((site + new Vector3(-span * 0.5f - 6f, 1f, -8f)).ToNumerics());
            foreach (int f in _drive.Wait(30)) yield return f;
            Fell(site, span * 0.75f + 10f);
            Vector3 corner = site - new Vector3(span * 0.5f, 0f, depth * cell * 0.5f);
            Vessel v = Vessel.SpawnBase(_game.Zone, s, (corner + Vector3.Up * (cell * 0.5f + 0.05f)).ToNumerics(), 0f);
            foreach (var (c, _) in s.Blocks().ToList()) v.ClearGround(c);
            foreach (int f in _drive.Wait(60)) yield return f;
            var midL = new N.Vector3(total * 0.5f, 1.5f, 1f) * cell;
            float side = sideDir;
            var eyeL = midL + new N.Vector3((float)Num("ex", 0.0), (float)Num("ey", 3.2f), side * (float)Num("ez", 10.5f)) * cell;
            Vector3 eye = v.ToWorld(eyeL), look = v.ToWorld(midL);
            _game.FixedCamera = new Transform3D(Basis.LookingAt((look - eye).Normalized(), Vector3.Up), eye);
            foreach (int f in _drive.Wait(8)) yield return f;
            foreach (int f in Shot($"{dir}/tiers-{ladder}.png")) yield return f;
            _game.FixedCamera = null;
        }
        _game.Visor.Visible = true;
    }
}
