using GalHack.Items.Bridge;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Surface;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Vessels;

/// <summary>Vessels in orbit: wrecks adrift, and claiming one by placing a core in it.</summary>
public sealed partial class Vessel
{
    /// <summary>The wreck this was set adrift as in orbit (its plan), or null.</summary>
    public Slice.Orbit.DerelictPlan Derelict { get; internal set; }

    /// <summary>A wreck nobody has claimed: no core, nobody's.</summary>
    public bool IsDerelict => Derelict != null && Structure.Core == null;

    /// <summary>
    /// A wreck adrift in space: one still body with no core (nothing flies it, nothing holds you to
    /// its floor), its hull and cabin world like any structure's, so it is boarded, walked or floated
    /// through, looted and taken apart the same way.
    /// </summary>
    public static Vessel SpawnDerelict(SurfaceZone zone, Structure s, N.Vector3 at, N.Quaternion rotation)
    {
        MassProperties mass = MassOf(s);
        Body body = zone.Physics.Register(new BodySpec
        {
            Kind = BodyKind.Wreck,
            Motion = Motion.Kinematic,
            Label = s.Name,
            Mass = mass.Mass,
            CenterOfMass = mass.CenterOfMass,
            DragArea = StructureBridge.DragArea(s),
            Shape = StructureBridge.ShapeOf(s),
            Position = at,
            Rotation = rotation,
        });
        var vessel = new Vessel(zone, s, body);
        var hull = new CharacterBody3D { Name = s.Name.Replace(" ", "").Replace("(", "").Replace(")", "").Replace(":", "") };
        zone.AddChild(hull);
        vessel.Binding = zone.Host.Bind(body, new HullBinding(hull));
        vessel.HullShapes = new StructureShapes(hull);
        vessel.Furnish(hull);
        vessel.Binding.Teleport();
        vessel.Register();
        return vessel;
    }

    /// <summary>
    /// A core placed in a wreck claims it (design: any wreck with no core can be claimed): it is
    /// yours, and its core's gravity holds whoever is aboard to its floor. A Big Core makes it a
    /// station in Base Mode, holding still in space; a Small Core a craft in Vessel Mode, ready to fly.
    /// </summary>
    public void Claimed(float gravity)
    {
        Structure.GravityOn = true;
        Body.Core = new CoreGravity { On = true, Strength = gravity };
        Cabin.SetGravity(Body.Core);
        if (BuildingBridge.IsSmall(Structure)) TakeFlight();
        else
        {
            Structure.Mode = CoreMode.Base;
            Body.Kind = BodyKind.Base;
        }
    }
}
