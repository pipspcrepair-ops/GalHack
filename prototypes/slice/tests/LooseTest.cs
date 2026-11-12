using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Surface;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Loose props on the ground in wind. Fittings (lights) cut off a base or a hull become free bodies; this tracks every one near
/// the player, in the weather's normal wind then a gale: how deep any corner of it sinks into the ground (limit 2 cm), how much
/// it jitters vertically while resting (limit 1 cm), how many move in a normal wind (none, for lamp-sized props), and that in a
/// gale they may slide and tumble but never sink. Fresh random world every run.
/// Run: --headless --fixed-fps 60 --path prototypes/slice res://tests/LooseTest.tscn
/// </summary>
public partial class LooseTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures;

    SurfaceZone Zone => _game.Zone;
    Catalogue Cat => Zone.Data.Catalogue;

    public override void _Ready() => _run = Scenarios().GetEnumerator();

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e) { GD.Print($"FAIL  threw: {e}"); _failures++; more = false; }
        if (more) return;
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (!ok) _failures++;
    }

    /// <summary>A short spar of steel with a fitting on its end, set down as a base; shooting the middle cuts the fitting loose.</summary>
    Vessel Spar(Vector3 at, GridSize grid, string fitting)
    {
        var s = new Structure(Cat, Cat.Grid(grid)) { Name = $"Spar{_n++}" };
        s.Set(new Int3(0, 0, 0), Building.Make(s, Cat.Type("steel")));
        s.Set(new Int3(1, 0, 0), Building.Make(s, Cat.Type("steel")));
        s.Set(new Int3(2, 0, 0), Building.Make(s, Cat.Type(fitting)));
        s.DirtyChunks.Clear();
        return Vessel.SpawnBase(Zone, s, at.ToNumerics(), 0f);
    }

    int _n;
    static readonly int NormalSeconds = OS.GetCmdlineUserArgs().Contains("--short") ? 15 : 100, GaleSeconds = OS.GetCmdlineUserArgs().Contains("--short") ? 15 : 60;

    sealed class Track
    {
        public Vessel V;
        public float Deepest, Jitter;
        public Vector3 Rested;
        public readonly List<float> Heights = new();
    }

    /// <summary>How far the lowest corner of the piece's actual collision shapes (what the engine collides) is below the collider under it, m (positive: sunk).</summary>
    float Sinking(Vessel v)
    {
        float deepest = float.MinValue;
        foreach (CollisionShape3D cs in ((FreeBinding)v.Binding).Rigid.GetChildren().OfType<CollisionShape3D>())
        {
            Vector3[] points = cs.Shape switch
            {
                BoxShape3D b => Enumerable.Range(0, 8).Select(k => new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1) * b.Size * 0.5f).ToArray(),
                ConvexPolygonShape3D c => c.Points,
                _ => Array.Empty<Vector3>(),
            };
            foreach (Vector3 p in points)
            {
                Vector3 w = cs.GlobalTransform * p;
                float top = ColliderTop(w);
                deepest = MathF.Max(deepest, (float.IsFinite(top) ? top : Zone.GroundAt(w.X, w.Z).Y) - w.Y);
            }
        }
        return deepest;
    }

    /// <summary>The top of the solid ground by a ray from above, or NaN where nothing collides.</summary>
    float ColliderTop(Vector3 w)
    {
        var q = PhysicsRayQueryParameters3D.Create(w + Vector3.Up * 0.6f, w + Vector3.Down * 8f, GalHack.Physics.Layers.World);
        var hit = Zone.GetWorld3D().DirectSpaceState.IntersectRay(q);
        return hit.Count == 0 ? float.NaN : ((Vector3)hit["position"]).Y;
    }

    /// <summary>Dry, level ground near a spot east and south of the player (no water, no slope), so the props are judged on ground.</summary>
    Vector3 DrySpot(float east, float south)
    {
        Vector3 from = _game.Survivor.WorldFeet + new Vector3(east, 0f, south);
        for (int k = 0; k < 400; k++)
        {
            float a = k * 2.399f, r = MathF.Sqrt(k) * 2.5f;
            Vector3 p = from + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            Vector3 g = Zone.GroundAt(p.X, p.Z);
            bool level = true;
            foreach (Vector2 o in new[] { new Vector2(2.5f, 0f), new Vector2(-2.5f, 0f), new Vector2(0f, 2.5f), new Vector2(0f, -2.5f) })
                level &= MathF.Abs(Zone.GroundAt(p.X + o.X, p.Z + o.Y).Y - g.Y) < 0.5f;
            if (level && !Zone.Water.LiquidAt(p.X, p.Z, out _) && !_used.Any(u => u.DistanceTo(g) < 4f))
            {
                _used.Add(g);
                return g;
            }
        }
        return Zone.GroundAt(from.X, from.Z);
    }

    readonly List<Vector3> _used = new();

    IEnumerable<int> Scenarios()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        WeatherSim sim = Zone.Weather.Sim;
        sim.FrontsOff = true;
        Zone.Wildlife.Frozen = true;
        Zone.Weather.Bridge.Calm = true;
        sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;

        int rescues = 0;
        Zone.Physics.Rescued += r => { rescues++; if (rescues <= 12) GD.Print($"  rescue {r.Body.Label}: {r.From.Y:F2} -> {r.To.Y:F2} inside {r.Inside}"); };
        var spars = new List<Vessel>();
        int k = 0;
        foreach (GridSize grid in new[] { GridSize.Small, GridSize.Large })
            for (int i = 0; i < 3; i++, k++)
            {
                Vector3 g = DrySpot(-8f + k * 3.5f, 8f + (k % 2) * 4f);
                spars.Add(Spar(g + Vector3.Up * 0.2f, grid, "light"));
            }
        foreach (int x in _drive.Wait(3)) yield return x;
        foreach (Vessel v in spars)
        {
            N.Vector3 local = StructureBridge.CellCenter(v.Structure, new Int3(1, 0, 0)) - N.Vector3.UnitZ * v.Structure.Grid.CellSize * 0.5f;
            v.Hits.Shot(new Int3(1, 0, 0), v.Body.ToParent(local), MathX.Rotate(v.Body.Rotation, -N.Vector3.UnitZ), 1e6f, DamageType.Kinetic);
        }
        foreach (int x in _drive.Wait(3)) yield return x;
        foreach (Vessel v in spars) // and the stump of the spar goes too, so nothing it leaves props the fittings up
        {
            if (v.Structure.Count == 0) continue;
            N.Vector3 local = StructureBridge.CellCenter(v.Structure, new Int3(0, 0, 0)) - N.Vector3.UnitZ * v.Structure.Grid.CellSize * 0.5f;
            v.Hits.Shot(new Int3(0, 0, 0), v.Body.ToParent(local), MathX.Rotate(v.Body.Rotation, -N.Vector3.UnitZ), 1e6f, DamageType.Kinetic);
        }
        foreach (int x in _drive.Wait(3)) yield return x;
        var tracks = Zone.Vessels.Where(v => v.IsWreck && v.Structure.Count == 1).Select(v => new Track { V = v }).ToList();
        GD.Print($"{tracks.Count} loose fittings; {string.Join(", ", tracks.Select(t => $"{t.V.Body.Mass:F1} kg / {t.V.Body.DragArea:F2} m2"))}");
        Check(tracks.Count == spars.Count, "every fitting cut off became its own loose piece");

        for (int i = 0; i < 60 * 12; i++) // they fall and settle
        {
            yield return 0;
            if (i % 90 != 0) continue;
            foreach (Track t in new[] { tracks[0], tracks[3] })
            {
                Vector3 w = t.V.ToWorld(N.Vector3.Zero);
                GD.Print($"  t{i / 60f:F1}s {t.V.Name}: y {w.Y:F2} ground {Zone.GroundAt(w.X, w.Z).Y:F2} v {t.V.Body.Velocity.Length():F2} asleep {t.V.Body.Asleep} sink {Sinking(t.V) * 100f:F0} cm, collider top {ColliderTop(w):F2}, {t.V.HullShapes.ShapeCount} shapes, f grav {t.V.Body.LastForce(ForceKind.Gravity).Y:F0} weather {t.V.Body.LastForce(ForceKind.Weather).Length():F1} contact {t.V.Body.LastForce(ForceKind.Contact).Length():F0} other {t.V.Body.LastForce(ForceKind.Other).Length():F0} water {t.V.Body.LastForce(ForceKind.Water).Length():F0} rot {t.V.Body.Rotation}");
            }
        }

        if (OS.GetCmdlineUserArgs().Contains("--trace"))
            for (int i = 0; i < 180; i++)
            {
                yield return 0;
                if (i % 6 != 0) continue;
                Track t = tracks[0];
                Vector3 com = t.V.Body.CenterOfMassPosition.ToGodot();
                var rg = ((FreeBinding)t.V.Binding).Rigid;
                GD.Print($"  tr{i}: comY-ground {com.Y - Zone.GroundAt(com.X, com.Z).Y:F2} lowest {-Sinking(t.V) * 100f:F1} cm  vRig {rg.LinearVelocity.Length():F2} wRig {rg.AngularVelocity.Length():F2} sleepRig {rg.Sleeping} asleep {t.V.Body.Asleep} rescues {rescues}");
            }

        // Normal wind.
        Zone.Weather.Bridge.Calm = false;
        sim.Snap(WeatherKind.Clouds);
        float windNormal = sim.Current.WindSpeed;
        foreach (Track t in tracks) { t.Rested = t.V.ToWorld(N.Vector3.Zero); t.Deepest = float.MinValue; t.Heights.Clear(); }
        int asleep = 0, ticks = 0;
        for (int i = 0; i < 60 * NormalSeconds; i++)
        {
            yield return 0;
            foreach (Track t in tracks)
            {
                t.Deepest = MathF.Max(t.Deepest, Sinking(t.V));
                t.Heights.Add(t.V.Body.CenterOfMassPosition.Y);
                if (t.V.Body.Asleep) asleep++;
            }
            ticks += tracks.Count;
        }
        int moved = 0;
        foreach (Track t in tracks)
        {
            Vector3 now = t.V.ToWorld(N.Vector3.Zero);
            if (new Vector2(now.X - t.Rested.X, now.Z - t.Rested.Z).Length() > 0.5f) moved++;
            List<float> late = t.Heights.Skip(t.Heights.Count / 2).ToList();
            t.Jitter = late.Max() - late.Min();
        }
        float sink1 = tracks.Max(t => t.Deepest), jit1 = tracks.Max(t => t.Jitter);
        GD.Print($"{rescues} rescues so far");
        GD.Print($"NORMAL WIND {windNormal:F1} m/s for {NormalSeconds} s: deepest overlap {sink1 * 100f:F2} cm, worst resting jitter {jit1 * 100f:F1} cm, {moved}/{tracks.Count} moved over 0.5 m, asleep {100f * asleep / ticks:F0}% of the time");
        Check(sink1 <= 0.02f, "in a normal wind no loose prop sinks more than 2 cm into the ground");
        Check(jit1 <= 0.01f, "and none jitters more than 1 cm at rest");
        Check(moved == 0, "and none of them moves");

        // A gale.
        sim.Snap(WeatherKind.Hurricane);
        float gale = sim.Current.WindSpeed;
        foreach (Track t in tracks) { t.Rested = t.V.ToWorld(N.Vector3.Zero); t.Deepest = float.MinValue; }
        float spin = 0f, slid = 0f;
        int bad = 0;
        for (int i = 0; i < 60 * GaleSeconds; i++)
        {
            yield return 0;
            foreach (Track t in tracks)
            {
                float depth = Sinking(t.V);
                if (depth > 0.02f && depth > t.Deepest && bad++ < 14)
                {
                    Vector3 at = t.V.Body.WorldPosition.ToGodot();
                    GD.Print($"  deep {depth * 100f:F1} cm: {t.V.Name} held {Zone.Held.Contains(t.V.Body)} collides {Zone.Streamer.CollidesAt(at)} above {at.Y - Zone.GroundAt(at.X, at.Z).Y:F2} m v {t.V.Body.Velocity.Length():F1} asleep {t.V.Body.Asleep}");
                }
                t.Deepest = MathF.Max(t.Deepest, depth);
                spin = MathF.Max(spin, t.V.Body.AngularVelocity.Length());
            }
        }
        foreach (Track t in tracks)
        {
            Vector3 now = t.V.ToWorld(N.Vector3.Zero);
            slid = MathF.Max(slid, new Vector2(now.X - t.Rested.X, now.Z - t.Rested.Z).Length());
        }
        float sink2 = tracks.Max(t => t.Deepest);
        GD.Print($"GALE {gale:F1} m/s for {GaleSeconds} s: deepest overlap {sink2 * 100f:F2} cm, furthest slide {slid:F1} m, fastest tumble {spin:F1} rad/s");
        Check(sink2 <= 0.02f, "in a gale nothing sinks more than 2 cm into the ground");
        Zone.Weather.Bridge.Calm = true;
    }
}
