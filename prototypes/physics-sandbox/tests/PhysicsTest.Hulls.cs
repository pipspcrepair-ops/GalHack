using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using PhysicsSandbox.World;
using N = System.Numerics;
using SInt3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;

namespace PhysicsSandbox;

/// <summary>
/// Hulls set into the ground (the grid agent's cases): a small, a middling and a large hand-built hull, each set down
/// with its keel 0.2 to 1.5 m into the ground on level ground, a gentle slope and a steep one, comes up clear of it
/// (nothing more than 10 cm under the ground's surface) and comes to rest there with a contact (a still hull on a slope
/// still has what holds it). No landing gear: the hulls lie on their keels. (-- --hulls runs only this.)
/// </summary>
public partial class PhysicsTest
{
    IEnumerable<int> HullsInGround()
    {
        Catalogue cat = _w.Catalogue;
        var sizes = new[] { ("small", new SInt3(3, 1, 4)), ("middling", new SInt3(6, 2, 10)), ("large", new SInt3(12, 3, 24)) };
        // Spots on the sandbox ground by slope: level, gentle, steep; clear of the parked craft, the lane and the bank.
        var spots = new List<(string, N.Vector3)>();
        foreach (var (name, lo, hi) in new[] { ("level", 0f, 5f), ("a 15° slope", 12f, 18f), ("a 30° slope", 26f, 34f) })
        {
            for (float x = -100f; x <= 40f && !spots.Exists(s => s.Item1 == name); x += 4f)
                for (float z = -100f; z <= 100f; z += 4f)
                {
                    if (MathF.Abs(x) < 40f && MathF.Abs(z) < 40f || MathF.Abs(z - SandboxTerrain.LaneZ) < 20f) continue;
                    float dx = _w.Ground(x + 2f, z).Y - _w.Ground(x - 2f, z).Y, dz = _w.Ground(x, z + 2f).Y - _w.Ground(x, z - 2f).Y;
                    float slope = MathF.Atan(MathF.Sqrt(dx * dx + dz * dz) / 4f) * 180f / MathF.PI;
                    if (slope < lo || slope > hi) continue;
                    spots.Add((name, _w.Ground(x, z)));
                    break;
                }
        }
        int placed = 0, clear = 0, rested = 0;
        var bad = new List<string>();
        var down = new PhysicsRayQueryParameters3D { CollisionMask = Layers.World };
        foreach (var (where, ground) in spots)
            foreach (var (size, dims) in sizes)
                foreach (float depth in new[] { 0.2f, 0.6f, 1.0f, 1.5f })
                {
                    var s = new Structure(cat) { Name = $"Hull {size}", Mode = CoreMode.Base, Owner = Building.DefaultOwner, GravityOn = false };
                    BlockType steel = cat.Type("iron-plated");
                    for (int x = 0; x < dims.X; x++)
                        for (int y = 0; y < dims.Y; y++)
                            for (int z = 0; z < dims.Z; z++)
                                s.Set(new SInt3(x, y, z), Building.Make(s, x == 0 && y == 0 && z == 0 ? cat.CoreType : steel));
                    s.DirtyChunks.Clear();
                    // Keel (the bottom face of row 0, half a cell under the origin) this deep under the ground at its middle.
                    var middle = new N.Vector3((dims.X - 1) * 0.5f, 0f, (dims.Z - 1) * 0.5f);
                    float surface = _w.Ground(ground.X + middle.X, ground.Z + middle.Z).Y;
                    var at = new N.Vector3(ground.X, surface + 0.5f - depth, ground.Z);
                    Ship ship = Ship.SpawnVessel(_w, s, at, 0f, landed: false);
                    ship.Binding.Teleport();
                    for (int t = 0; t < 180; t++) yield return 0;
                    placed++;
                    // How far the ground's surface stands over the keel, under every bottom cell.
                    float sunk = 0f;
                    PhysicsDirectSpaceState3D space = ship.Node.GetWorld3D().DirectSpaceState;
                    for (int x = 0; x < dims.X; x++)
                        for (int z = 0; z < dims.Z; z++)
                        {
                            N.Vector3 keel = ship.Body.ToParent(new N.Vector3(x, -0.5f, z));
                            down.From = (keel + N.Vector3.UnitY * 30f).ToGodot();
                            down.To = (keel - N.Vector3.UnitY * 3f).ToGodot();
                            Godot.Collections.Dictionary hit = space.IntersectRay(down);
                            if (hit.Count > 0) sunk = MathF.Max(sunk, hit["position"].AsVector3().Y - keel.Y);
                        }
                    bool isClear = sunk <= 0.1f, still = ship.Body.Velocity.Length() < 0.3f || ship.Body.Asleep,
                        held = ship.Body.Asleep || ship.Body.OnFloor || (ship.Binding as HullBinding)?.Contacts > 0;
                    if (isClear) clear++;
                    if (still && held) rested++;
                    if (!isClear || !still || !held)
                        bad.Add($"{size} hull on {where}, keel {depth:F1} m in: {sunk * 100f:F0} cm under the ground, speed {ship.Body.Velocity.Length():F2} m/s, asleep {ship.Body.Asleep}, contacts {(ship.Binding as HullBinding)?.Contacts}");
                    _w.Remove(ship);
                    yield return 0;
                }
        foreach (string b in bad) GD.Print($"    {b}");
        Check(spots.Count == 3 && clear == placed, $"hulls of three sizes set 0.2 to 1.5 m into level, gentle and steep ground come up clear of it ({clear} of {placed})");
        Check(rested == placed, $"and come to rest there, still with what holds them ({rested} of {placed})");
    }
}
