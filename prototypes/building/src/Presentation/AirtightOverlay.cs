using System.Collections.Generic;
using System.Linq;
using Godot;
using GalHack.Structures;

namespace GalHack.Structures.Presentation;

/// <summary>
/// The visor's air overlay, drawn through walls: sealed rooms as a faint teal volume, leaking
/// rooms in amber, and every leaking face as a pulsing red panel with a short beam pointing out
/// of it, so a hole is easy to find from across the ship.
/// </summary>
public partial class AirtightOverlay : MeshInstance3D
{
    static readonly Color SealedColor = new(0.2f, 1f, 0.85f, 0.16f);
    static readonly Color LeakingColor = new(1f, 0.45f, 0.08f, 0.2f);

    ShaderMaterial _roomMaterial, _leakMaterial;

    public RoomReport Report { get; private set; }

    public override void _Ready()
    {
        CastShadow = ShadowCastingSetting.Off;
        _roomMaterial = new ShaderMaterial { Shader = new Shader { Code = RoomCode }, RenderPriority = 1 };
        _leakMaterial = new ShaderMaterial { Shader = new Shader { Code = LeakCode }, RenderPriority = 2 };
    }

    // Rooms are drawn as a hologram: a tinted volume with a line along every cell edge, so the
    // shape of the air is readable through the hull.
    const string RoomCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_test_disabled, shadows_disabled;
void fragment() {
    vec2 f = fract(UV);
    float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
    float line = 1.0 - smoothstep(0.015, 0.045, edge);
    ALBEDO = COLOR.rgb;
    ALPHA = COLOR.a + line * 0.45;
}
";

    public void Refresh(Structure s, RoomReport report)
    {
        Report = report;
        float size = s.Grid.CellSize;
        var rooms = new MeshBuffers();
        var leaks = new MeshBuffers();
        var look = new FaceLook(Colors.White, 0, 0);

        foreach (Room room in report.Rooms)
        {
            var cells = new HashSet<Int3>(room.Cells);
            Color color = room.Sealed ? SealedColor : LeakingColor;
            var roomLook = look with { Base = color };
            foreach (Int3 c in room.Cells)
                foreach (Dir d in Dirs.All)
                    if (!cells.Contains(c + d.Vec()))
                        AddFace(rooms, c, d, size, inset: 0.06f, grow: 0f, roomLook);
        }
        foreach (Face f in report.LeakFaces)
        {
            AddFace(leaks, f.Cell, f.Dir, size, inset: -0.03f, grow: 0.04f, look);
            AddBeam(leaks, f, size, look);
        }

        var mesh = new ArrayMesh();
        if (rooms.VertexCount > 0)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, rooms.ToArrays());
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, _roomMaterial);
        }
        if (leaks.VertexCount > 0)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, leaks.ToArrays());
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, _leakMaterial);
        }
        Mesh = mesh.GetSurfaceCount() > 0 ? mesh : null;
    }

    /// <summary>A quad on one face of a cell; inset pulls it towards the cell centre, grow enlarges it.</summary>
    static void AddFace(MeshBuffers buffers, Int3 cell, Dir d, float size, float inset, float grow, FaceLook look)
    {
        int a = d.Axis(), u = (a + 1) % 3, v = (a + 2) % 3;
        var c = new Vector3(cell.X, cell.Y, cell.Z);
        float plane = c[a] + d.Sign() * (0.5f - inset), h = 0.5f + grow;
        System.Span<Vector3> q = stackalloc Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            var p = new Vector3();
            p[a] = plane;
            p[u] = c[u] + (i == 1 || i == 2 ? h : -h);
            p[v] = c[v] + (i >= 2 ? h : -h);
            q[i] = p * size;
        }
        buffers.AddPolygon(q, MeshBuffers.ToGodot(d.Normal()), d, size, look);
    }

    /// <summary>A thin bar sticking out of a leaking face, so the leak is visible edge-on and from afar.</summary>
    static void AddBeam(MeshBuffers buffers, Face f, float size, FaceLook look)
    {
        int a = f.Dir.Axis();
        var c = new Vector3(f.Cell.X, f.Cell.Y, f.Cell.Z);
        Vector3 n = MeshBuffers.ToGodot(f.Dir.Normal());
        Vector3 start = c + n * 0.5f, end = c + n * 2.2f;
        Vector3 min = start.Min(end), max = start.Max(end);
        for (int axis = 0; axis < 3; axis++)
        {
            if (axis == a) continue;
            min[axis] -= 0.09f;
            max[axis] += 0.09f;
        }
        System.Span<Vector3> q = stackalloc Vector3[4];
        foreach (Dir d in Dirs.All)
        {
            int da = d.Axis(), u = (da + 1) % 3, v = (da + 2) % 3;
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3();
                p[da] = d.Sign() > 0 ? max[da] : min[da];
                p[u] = i == 1 || i == 2 ? max[u] : min[u];
                p[v] = i >= 2 ? max[v] : min[v];
                q[i] = p * size;
            }
            buffers.AddPolygon(q, MeshBuffers.ToGodot(d.Normal()), d, size, look);
        }
    }

    const string LeakCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_test_disabled, shadows_disabled;
void fragment() {
    float pulse = 0.5 + 0.5 * sin(TIME * 7.0);
    float hatch = step(0.5, fract((UV.x + UV.y) * 3.0 - TIME * 1.5));
    ALBEDO = mix(vec3(1.0, 0.04, 0.02), vec3(1.0, 0.95, 0.9), hatch * 0.55);
    ALPHA = 0.75 + 0.25 * pulse;
}
";
}
