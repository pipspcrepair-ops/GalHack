using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Infra;
using Slice.Vessels;

namespace Slice.Tests;

/// <summary>
/// The network's block structures breaking (--view=structures): for each kind on the world (a bridge's span, pier and abutment,
/// a tunnel mouth, a seawall stretch and its stairs, a railway's track and station, a guideway and its pylon), the nearest one
/// seen side-on, struck with rockets until blocks of it are gone, and seen again from the same eye. A pier is struck at its
/// columns until it lets go of its spans: they are seen falling and lying. The run fails for a kind that didn't break (and a
/// pier whose spans didn't drop), and lists the kinds the world has none of.
/// </summary>
public sealed partial class Staging
{
    static readonly string[] StructureKinds = { "BridgePier", "BridgeSpan", "Abutment", "Portal", "SeawallStretch", "SeawallStair", "Track", "Station", "Guideway", "GuidewayPylon" };

    IEnumerable<int> StructuresView()
    {
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        GD.Print($"world: seed {_game.World.Seed}, {_game.World}");
        InfraLayer infra = _game.Zone.Infra;
        var all = infra.BlockPieces.ToList();
        GD.Print($"(structures: {all.Count} planned: {string.Join(", ", all.GroupBy(p => p.GetType().Name).Select(g => $"{g.Count()} {g.Key}"))})");
        GalHack.Arms.WeaponDef rocket = WorldFire.Rocket(_game);
        string only = _args.GetValueOrDefault("kinds");
        var missing = new List<string>();
        foreach (string kind in StructureKinds)
        {
            if (only != null && !only.Split(',').Contains(kind)) continue;
            Vector3 from = _game.Survivor.WorldFeet;
            // The nearest of the kind with blocks to it (a span that stands; a support at least 8 m out of the ground that carries one).
            bool carrier = kind is "BridgePier" or "GuidewayPylon";
            InfraLayer.BlockPiece piece = all.Where(p => p.GetType().Name == kind && !p.Taken && (!carrier || p.Carries.Count > 0) && (kind != "BridgeSpan" || p.Supports.Length > 0))
                .OrderBy(p => new Vector2((float)p.X - from.X, (float)p.Z - from.Z).Length())
                .FirstOrDefault(p => p.Build(_game.Zone.Planet) is { Count: > 0 } b && (!carrier || Standing(b).Max(c => c.Y) - Standing(b).Min(c => c.Y) >= 8));
            if (piece == null) { missing.Add(kind); continue; }
            // Stand by it till its tile is drawn.
            Vector3 near = _game.Zone.Planet.NearestCopy(new Vector3((float)piece.X, 0f, (float)piece.Z), from);
            Aabb? drawn = null;
            _game.FixedCamera = null; // the tiles load round the eye
            for (int i = 0; i < 1200 && (drawn = infra.BoxOf(piece)) == null; i++)
            {
                if (i % 60 == 0) _game.Survivor.PlaceOutside((near with { Y = _game.Zone.GroundAt(near.X, near.Z).Y + 30f }).ToNumerics());
                yield return 0;
            }
            if (drawn is not Aabb box) { GD.Print($"  FAIL  a {kind} near {near}: never drawn"); Faults++; continue; }
            Transform3D at = infra.Where(piece);
            Vector3 centre = box.GetCenter(), across = kind == "Portal" ? at.Basis.Z : at.Basis.X;
            across = (across with { Y = 0f }).Normalized();
            bool pier = carrier;
            float back = Mathf.Max(box.Size.Length() * 0.9f, pier ? 45f : 14f);
            Vector3 eye = centre + across * back;
            // From the ground or water's level, or the structure's own when it stands high (a pier: from the deck's height off to its side).
            eye.Y = Mathf.Max(_game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f, (_game.Zone.Planet.WaterSurfaceAt(eye) ?? float.MinValue) + 1.7f);
            eye.Y = Mathf.Max(eye.Y, centre.Y);
            // Aimed at its block nearest the eye out of the ground (a support's, low on its columns).
            var built = piece.Build(_game.Zone.Planet);
            var cells = (Standing(built).Any() ? Standing(built) : built.Cells().Select(c => c.pos)).Select(c => at * new Vector3(c.X, c.Y, c.Z)).ToList();
            float lowest = cells.Min(c => c.Y), highest = cells.Max(c => c.Y);
            Vector3 look = centre, aim = cells.Where(c => !pier || c.Y < lowest + (highest - lowest) * 0.4f).OrderBy(c => c.DistanceTo(eye)).First();
            foreach (int x in Look($"{kind}-before", eye, look)) yield return x;
            int dropped = _game.Zone.InfraDropped, rockets = 0;
            Vessel v = null;
            bool Broke() => (v = _game.Zone.InfraVessel(piece)) != null && (pier ? _game.Zone.InfraDropped > dropped : v.Hits.BlocksLost > 0);
            for (; rockets < 60 && !Broke(); rockets++)
            {
                Vector3 muzzle = aim + (eye - aim).Normalized() * 8f; // from close by: nothing between
                WorldFire.Fire(_game, rocket, muzzle, aim);
                foreach (int x in _drive.Wait(20)) yield return x;
            }
            int lost = v?.Hits.BlocksLost ?? 0;
            bool ok = Broke();
            if (pier && ok)
            {
                // Its spans fall: seen on their way down, and lying where they came to rest.
                Vessel span = piece.Carries.Select(_game.Zone.InfraVessel).FirstOrDefault(s => s != null);
                float y0 = span?.Body.Position.Y ?? 0f;
                for (int i = 0; i < 180 && span != null && GodotObject.IsInstanceValid(span.Node) && span.Body.Position.Y > y0 - 3f; i++) yield return 0;
                foreach (int x in Look($"{kind}-falling", eye, look, settle: false)) yield return x;
                for (int i = 0; i < 900 && span != null && GodotObject.IsInstanceValid(span.Node) && (i < 120 || span.Body.Velocity.Length() > 0.2f); i++) yield return 0;
                ok &= span != null && GodotObject.IsInstanceValid(span.Node) && span.Body.Position.Y < box.End.Y - 2f;
                GD.Print($"  (its first span came to rest at {span?.Body.Position.Y:F1} m, the pier's top was at {box.End.Y:F1} m)");
            }
            if (!ok) Faults++;
            GD.Print($"  {(ok ? "ok   " : "FAIL ")} a {kind} ({box.Size.X:F0} x {box.Size.Y:F0} x {box.Size.Z:F0} m, {v?.Structure.BlockCount ?? 0} blocks left): after {rockets} rockets {lost} blocks gone" +
                     (pier ? $", {_game.Zone.InfraDropped - dropped} spans it carried dropped" : ""));
            foreach (int x in Look($"{kind}-after", eye, look)) yield return x;
        }
        if (missing.Count > 0) GD.Print($"(structures: none on this world of {string.Join(", ", missing)})");
        _game.FixedCamera = null;

        // A structure's cells out of the ground.
        static IEnumerable<GalHack.Structures.Int3> Standing(GalHack.Structures.Structure b) => b.Cells().Select(c => c.pos).Where(c => b.Footing?.Contains(c) != true);

        IEnumerable<int> Look(string tag, Vector3 eye, Vector3 look, bool settle = true)
        {
            // Stood behind the eye: its own body out of the frame.
            _game.Survivor.PlaceOutside((eye + (eye - look).Normalized() * 3f - Vector3.Up * 1.6f).ToNumerics());
            for (int i = 0; i < (settle ? 900 : 1); i++)
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
