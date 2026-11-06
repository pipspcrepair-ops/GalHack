using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using Godot;

namespace GalHack.Deco.Engine;

/// <summary>
/// A dressed room or site in the scene: one <see cref="DecoNode"/> per placement (meshes shared
/// through the cache) and the wires strung between placements, hung by the cable helper.
/// </summary>
public partial class DecoPlanView : Node3D
{
    public DecoPlan Plan { get; private set; }
    public readonly List<DecoNode> Nodes = new();

    public static DecoPlanView Create(DecoMeshCache cache, DecoPlan plan, bool collide = true, bool lights = true)
    {
        var view = new DecoPlanView { Plan = plan, Name = plan.Room?.RoomId ?? plan.Room?.Site ?? "Room" };
        foreach (DecoPlacement pl in plan.Placements)
        {
            var at = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(pl.Yaw)), new Vector3(pl.X, pl.Y, pl.Z));
            DecoNode n = DecoNode.Create(cache, pl.Look, at, collide, lights);
            n.Placement = pl;
            view.Nodes.Add(n);
            view.AddChild(n);
        }
        if (plan.Cables.Count > 0)
        {
            var mesh = new DecoMeshData();
            foreach (DecoPlanCable c in plan.Cables)
            {
                DecoCable cable = c.AsCable();
                foreach (DecoPart t in DecoCables.Tubes(DecoCables.Path(cable), cable.Radius, cable.Slot))
                    DecoEmit.Part(mesh, t, (_, _, _) => DecoRgb.Hex("1e2024"), _ => DecoSurface.Matte);
            }
            view.AddChild(new MeshInstance3D { Name = "Wires", Mesh = DecoGodot.ToMesh(mesh, cache.Materials) });
        }
        return view;
    }
}
