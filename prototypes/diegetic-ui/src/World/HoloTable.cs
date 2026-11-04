using System.Collections.Generic;
using DiegeticUi.Logic;
using DiegeticUi.Pages;
using DiegeticUi.Screens;
using DiegeticUi.Theme;
using Godot;

namespace DiegeticUi.World;

/// <summary>
/// The bridge holo-table: a pedestal projecting the current star system as glowing light.
/// Discovered worlds are bright and named; unsurveyed contacts are dim violet and only a "?".
///
/// Use it like a screen (look, E). The view eases to an elevated framing; the cursor is a
/// ring of light that slides over the orbital plane and snaps around the world under it.
/// Click a world to select it: the rim screen describes it and the visor marks it.
///
/// Local space: the origin is on the floor at the pedestal's foot; the player approaches
/// from +Z, where the rim screen faces.
/// </summary>
public partial class HoloTable : Node3D, IFocusTarget
{
    const float HoloY = 1.5f;          // height of the orbital plane
    const float MapScale = 0.62f;      // metres per map unit
    const float TableRadius = 1.05f;

    public StarMap Map { get; init; }
    public string Prompt => "STAR MAP";
    public bool MovesCamera => true;
    public bool InUse { get; private set; }

    /// <summary>The world under the cursor, or null.</summary>
    public Body Hovered { get; private set; }

    public WorldScreen Info { get; private set; }

    readonly Dictionary<Body, (Node3D Node, float Radius)> _bodies = new();
    Node3D _holo, _you;
    MeshInstance3D _cursor, _selection, _dropLine;
    ShaderMaterial _cursorMaterial;

    public override void _Ready()
    {
        BuildPedestal();
        BuildHologram();

        var pick = new StaticBody3D { Name = "Pick", CollisionLayer = Layers.Pick, CollisionMask = 0 };
        pick.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = 0.85f, Height = 0.7f },
            Position = new Vector3(0, HoloY, 0),
        });
        pick.SetMeta("focus_target", this);
        AddChild(pick);

        Info = new WorldScreen
        {
            Name = "InfoScreen",
            Pixels = new Vector2I(HoloInfoPage.Width, HoloInfoPage.Height),
            SizeMeters = new Vector2(0.64f, 0.36f),
            Pickable = false,
            Bezel = 0.03f,
            Prompt = "STAR MAP",
            Position = new Vector3(0, 1.12f, TableRadius + 0.12f),
            RotationDegrees = new Vector3(-52, 0, 0),
        };
        AddChild(Info);
        Info.SetContent(new HoloInfoPage(Map));
    }

    void BuildPedestal()
    {
        var cream = new StandardMaterial3D { AlbedoColor = new Color("e9dfc4"), Roughness = 0.7f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("23252e"), Roughness = 0.5f, Metallic = 0.3f };
        var orange = new StandardMaterial3D { AlbedoColor = new Color("e85d04"), Roughness = 0.6f };
        Add(new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.9f, Height = 0.8f }, cream, new Vector3(0, 0.4f, 0));
        Add(new CylinderMesh { TopRadius = TableRadius, BottomRadius = 0.8f, Height = 0.16f }, cream, new Vector3(0, 0.88f, 0));
        Add(new CylinderMesh { TopRadius = TableRadius + 0.04f, BottomRadius = TableRadius + 0.04f, Height = 0.07f }, orange, new Vector3(0, 0.99f, 0));
        Add(new CylinderMesh { TopRadius = 0.92f, BottomRadius = 0.92f, Height = 0.02f }, dark, new Vector3(0, 1.025f, 0));
        Add(new CylinderMesh { TopRadius = 0.92f, BottomRadius = 0.92f, Height = 0.08f }, dark, new Vector3(0, 0.04f, 0));

        // The emitter: rings of light on the glass, and the faintest column up to the map.
        foreach (float r in new[] { 0.22f, 0.45f, 0.68f, 0.88f })
            Add(Ring(r, 0.006f), Holo(UiTheme.Colors.Holo, 0.7f, body: 1, rim: 0, scan: 0), new Vector3(0, 1.038f, 0));
        Add(new CylinderMesh { TopRadius = 0.88f, BottomRadius = 0.88f, Height = 0.42f, RadialSegments = 48, CapTop = false, CapBottom = false },
            Holo(UiTheme.Colors.Holo, 0.05f, body: 0.05f, rim: 0.5f, scan: 0.7f), new Vector3(0, 1.25f, 0));
        AddChild(new OmniLight3D { Position = new Vector3(0, 1.4f, 0), LightColor = UiTheme.Colors.Holo, LightEnergy = 0.9f, OmniRange = 3.5f });
    }

    void BuildHologram()
    {
        _holo = new Node3D { Name = "Hologram", Position = new Vector3(0, HoloY, 0) };
        AddChild(_holo);
        Color holo = UiTheme.Colors.Holo, dark = new("8b6fb3");

        foreach (Body body in Map.Bodies)
        {
            bool known = body.Discovered;
            Color colour = known ? Paint.Rgb(body.Color) : dark;
            if (body.Orbit > 0)
                _holo.AddChild(new MeshInstance3D
                {
                    Mesh = Ring((float)body.Orbit * MapScale, 0.0035f),
                    MaterialOverride = Holo(known ? holo : dark, known ? 0.55f : 0.3f, body: 1, rim: 0, scan: 0),
                });

            float radius = body.Kind == BodyKind.Star ? 0.09f : 0.034f + (float)body.Size * 0.7f; // worlds exaggerated: they are what you click
            var node = new Node3D { Name = body.Name };
            _holo.AddChild(node);
            Mesh mesh = body.Kind == BodyKind.Station
                ? new BoxMesh { Size = Vector3.One * radius * 1.2f }
                : new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 24, Rings = 12 };
            var view = new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = Holo(colour, known ? (body.Kind == BodyKind.Star ? 2.2f : 1.3f) : 0.6f,
                    body: body.Kind == BodyKind.Star ? 0.9f : 0.3f, rim: 1.8f),
            };
            if (body.Kind == BodyKind.Station) view.RotationDegrees = new Vector3(45, 0, 35);
            node.AddChild(view);
            if (body.Kind == BodyKind.GasGiant)
                node.AddChild(new MeshInstance3D
                {
                    Mesh = Ring(radius * 1.75f, radius * 0.35f),
                    MaterialOverride = Holo(colour, 0.22f, body: 1, rim: 0, scan: 0.3f),
                    RotationDegrees = new Vector3(18, 0, 10),
                });

            node.AddChild(new Label3D
            {
                Text = known ? body.Name : "?",
                Font = UiTheme.Label3D,
                FontSize = 44,
                PixelSize = 0.0009f,
                OutlineSize = 6,
                OutlineModulate = new Color(0.02f, 0.04f, 0.08f, 0.6f),
                Modulate = known ? colour.Lightened(0.6f) : dark.Lightened(0.3f),
                RenderPriority = 2,         // after the additive glow of the worlds, so names stay crisp
                OutlineRenderPriority = 1,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Shaded = false,
                Position = new Vector3(0, radius + 0.045f, 0),
            });
            _bodies[body] = (node, radius);
        }

        // You are here: a small yellow arrowhead riding your orbit.
        _you = new Node3D { Name = "You" };
        _holo.AddChild(_you);
        _you.AddChild(new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(0.05f, 0.06f, 0.02f) },
            MaterialOverride = Holo(UiTheme.Colors.Accent2, 1.6f, body: 0.8f, rim: 1),
            RotationDegrees = new Vector3(-90, 0, 0),
        });
        _you.AddChild(new Label3D
        {
            Text = "YOU", Font = UiTheme.Label3D, FontSize = 36, PixelSize = 0.0009f, OutlineSize = 10,
            OutlineModulate = new Color(0.02f, 0.04f, 0.08f, 0.85f),
            Modulate = UiTheme.Colors.Accent2, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Shaded = false,
            Position = new Vector3(0, -0.06f, 0),
        });

        _selection = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1f, Rings = 48, RingSegments = 6 },
            MaterialOverride = Holo(UiTheme.Colors.Accent2, 1.4f, body: 1, rim: 0, scan: 0),
            Visible = false,
        };
        _holo.AddChild(_selection);
        _dropLine = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.003f, BottomRadius = 0.003f, Height = 1 },
            MaterialOverride = Holo(UiTheme.Colors.Accent2, 0.8f, body: 1, rim: 0),
            Visible = false,
        };
        _holo.AddChild(_dropLine);

        _cursorMaterial = Holo(UiTheme.Colors.Text, 1.6f, body: 1, rim: 0, scan: 0);
        _cursor = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1f, Rings = 40, RingSegments = 6 },
            MaterialOverride = _cursorMaterial,
            Visible = false,
        };
        _holo.AddChild(_cursor);
    }

    public override void _Process(double delta)
    {
        double t = Map.Clock;
        foreach (var (body, (node, _)) in _bodies) node.Position = ToHolo(body.PositionAt(t));
        _you.Position = ToHolo(Map.Ship.PositionAt(t)) + Vector3.Up * 0.03f;
        _you.Rotation = new Vector3(0, (float)Map.Ship.AngleAt(t), 0);

        Body selected = Map.Selected;
        _selection.Visible = _dropLine.Visible = selected != null;
        if (selected != null)
        {
            var (node, radius) = _bodies[selected];
            float r = radius + 0.035f;
            _selection.Position = node.Position;
            _selection.Scale = Vector3.One * r;
            _selection.RotateY((float)delta * 1.5f);
            _dropLine.Position = node.Position + Vector3.Down * (HoloY - 1.04f) / 2;
            _dropLine.Scale = new Vector3(1, HoloY - 1.04f, 1);
        }
    }

    Vector3 ToHolo((double X, double Y) p) => new((float)p.X * MapScale, 0, -(float)p.Y * MapScale);

    // ---- IFocusTarget -------------------------------------------------------------------

    public Transform3D FocusView(Camera3D camera)
    {
        Vector3 centre = _holo.GlobalPosition;
        // Up and back from the near rim, tipped down enough to keep the rim screen in the frame.
        Vector3 eye = ToGlobal(new Vector3(0, HoloY + 0.75f, 1.75f));
        return new Transform3D(Basis.Identity, eye).LookingAt(centre + GlobalBasis * new Vector3(0, -0.32f, 0.45f), Vector3.Up);
    }

    public Rect2 ScreenRect(Camera3D camera)
    {
        var rect = new Rect2();
        int i = 0;
        foreach (float x in new[] { -0.85f, 0.85f })
            foreach (float y in new[] { -0.1f, 0.2f })
                foreach (float z in new[] { -0.85f, 0.85f })
                {
                    Vector3 p = _holo.ToGlobal(new Vector3(x, y, z));
                    if (camera.IsPositionBehind(p)) return camera.GetViewport().GetVisibleRect();
                    Vector2 s = camera.UnprojectPosition(p);
                    rect = i++ == 0 ? new Rect2(s, Vector2.Zero) : rect.Expand(s);
                }
        return rect;
    }

    public void SetInUse(bool inUse)
    {
        InUse = inUse;
        _cursor.Visible = inUse;
        if (!inUse) Hovered = null;
    }

    public void Point(Vector3 origin, Vector3 direction, InputEvent e)
    {
        Hovered = Pick(origin, direction);
        if (Hovered != null)
        {
            var (node, radius) = _bodies[Hovered];
            _cursor.Position = node.Position;
            _cursor.Scale = Vector3.One * (radius + 0.06f);
        }
        else
        {
            // Off any world: the ring rides the orbital plane under the cursor.
            Vector3 o = _holo.ToLocal(origin), d = _holo.GlobalBasis.Inverse() * direction;
            if (Mathf.Abs(d.Y) > 1e-4f)
            {
                Vector3 p = o + d * (-o.Y / d.Y);
                p.Y = 0;
                _cursor.Position = p.LimitLength(0.85f);
            }
            _cursor.Scale = Vector3.One * 0.035f;
        }
        _cursorMaterial.SetShaderParameter("tint", Hovered != null ? UiTheme.Colors.Accent2 : UiTheme.Colors.Text);

        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && Hovered != null)
            Map.Select(Hovered);
    }

    /// <summary>The nearest world whose pick sphere the ray passes through, or null.</summary>
    public Body Pick(Vector3 origin, Vector3 direction)
    {
        Body best = null;
        float bestT = float.MaxValue;
        Vector3 dir = direction.Normalized();
        foreach (var (body, (node, radius)) in _bodies)
        {
            float r = radius + 0.04f; // a little generous: small worlds are fiddly targets
            Vector3 oc = origin - node.GlobalPosition;
            float b = oc.Dot(dir), c = oc.LengthSquared() - r * r, disc = b * b - c;
            if (disc < 0) continue;
            float t = -b - Mathf.Sqrt(disc);
            if (t > 0 && t < bestT)
            {
                best = body;
                bestT = t;
            }
        }
        return best;
    }

    /// <summary>A world's position in the hologram. For tests.</summary>
    public Vector3 BodyPosition(Body body) => _bodies[body].Node.GlobalPosition;

    // ---- Helpers ---------------------------------------------------------------------

    void Add(Mesh mesh, Material material, Vector3 position) =>
        AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position });

    static ShaderMaterial Holo(Color tint, float intensity, float body = 0.25f, float rim = 1.6f, float scan = 0.45f)
    {
        var m = new ShaderMaterial { Shader = HoloShader };
        m.SetShaderParameter("tint", tint);
        m.SetShaderParameter("intensity", intensity);
        m.SetShaderParameter("body", body);
        m.SetShaderParameter("rim", rim);
        m.SetShaderParameter("scan", scan);
        return m;
    }

    static Shader _holoShader;
    static Shader HoloShader => _holoShader ??= GD.Load<Shader>("res://shaders/hologram.gdshader");

    /// <summary>A flat annulus in the XZ plane: an orbit line, or a planet's rings.</summary>
    static ArrayMesh Ring(float radius, float halfWidth, int segments = 128)
    {
        var verts = new Vector3[(segments + 1) * 2];
        var normals = new Vector3[verts.Length];
        var indices = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.Tau / segments;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            verts[i * 2] = dir * (radius - halfWidth);
            verts[i * 2 + 1] = dir * (radius + halfWidth);
            normals[i * 2] = normals[i * 2 + 1] = Vector3.Up;
            if (i == segments) break;
            int k = i * 6, v = i * 2;
            (indices[k], indices[k + 1], indices[k + 2]) = (v, v + 1, v + 2);
            (indices[k + 3], indices[k + 4], indices[k + 5]) = (v + 1, v + 3, v + 2);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
