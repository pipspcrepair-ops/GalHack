using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Plants;
using Godot;
using PlanetSurface.Terrain;


namespace Slice.Tests;

public partial class SliceTest
{
    /// <summary>Rides the bike at a trunk and then at a canopy from 14 m off, and checks it stops short of each.</summary>
    IEnumerable<int> BikeIntoTrees(Slice.Vehicles.HoverBike bike)
    {
        TerrainStreamer streamer = _game.Zone.Streamer;
        Vector3 feet = _game.Survivor.WorldFeet;
        var near = streamer.SolidsNear(feet, 120f, canopies: true).ToList();
        Plant PlantOf(FloraSolid s) => streamer.World.Flora.Book.Plants[s.Plant];
        // A trunk, and the lowest edge of a canopy the bike's body reaches (0.9 to 1.9 m over the ground).
        var trunk = near.Where(n => n.solid.IsTrunk && n.solid.Fellable && n.solid.Extents.X > 0.25f).OrderBy(n => (n.solid.Center + n.offset).DistanceTo(feet)).Select(n => ((FloraSolid, Vector3)?)n).FirstOrDefault();
        Vector3? edge = null, canopyRoot = null;
        foreach (var (s, off) in near.Where(n => n.solid.IsCanopy))
            foreach (System.Numerics.Vector3 p in PlantOf(s).Canopy[s.Variant][s.Part])
            {
                Vector3 w = s.Center + off + s.Basis * new Vector3(p.X, p.Y, p.Z) * s.Scale;
                float up = w.Y - _game.Zone.GroundAt(w.X, w.Z).Y;
                if (up < 1.0f || up > 1.6f || edge != null && w.DistanceTo(feet) > edge.Value.DistanceTo(feet)) continue;
                edge = w;
                canopyRoot = s.Center + off;
            }
        Check(trunk != null && edge != null, $"trees to ride into: a trunk {(trunk != null ? "found" : "missing")}, a canopy edge {(edge != null ? "found" : "missing")}");
        if (trunk == null || edge == null) yield break;
        Vector3 hit = trunk.Value.Item1.Center + trunk.Value.Item2;
        float radius = trunk.Value.Item1.Extents.X;
        foreach (var (name, target, stop) in new[] { ("trunk", hit, radius), ("canopy", edge.Value, 0.3f) })
        {
            // From 14 m out, on the side away from the tree it belongs to.
            Vector3 root = name == "trunk" ? hit : canopyRoot.Value;
            Vector3 out_ = new Vector3(target.X - root.X, 0f, target.Z - root.Z);
            out_ = out_.LengthSquared() < 0.01f ? Vector3.Right : out_.Normalized();
            Vector3 start = target + out_ * 14f;
            bike.Position = new Vector3(start.X, _game.Zone.GroundAt(start.X, start.Z).Y + Slice.Vehicles.HoverBike.Hover, start.Z);
            bike.Yaw = MathF.Atan2(out_.X, out_.Z);
            bike.Velocity = Vector3.Zero;
            bike.SetUpright();
            float nearest = float.MaxValue;
            _game.Controls.Hold(Key.W);
            for (int i = 0; i < 240; i++)
            {
                yield return 0;
                nearest = MathF.Min(nearest, new Vector2(bike.Position.X - target.X, bike.Position.Z - target.Z).Length());
            }
            _game.Controls.Hold(Key.W, false);
            Check(nearest > stop, $"a bike ridden at a {name} stops short of it: closest {nearest:F2} m to its {(name == "trunk" ? "axis" : "edge")} (solid within {stop:F2} m)");
        }
    }
}
