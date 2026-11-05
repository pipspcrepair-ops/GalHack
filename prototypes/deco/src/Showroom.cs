using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using Godot;

namespace DecoShowroom;

/// <summary>
/// The decorations showroom: one strip per theme set, each piece standing intact at the front with
/// its other states in the rows behind (old-world and preserved ages, knocked over, broken, burnt,
/// overgrown, and for vehicles rusted, doors open and half buried); the ore tells, a row per ore;
/// then the arrangement templates, each room and site shown as used and again as found. A
/// free-flying camera; [ and ] jump between sets, T to the rooms, O to the ore tells, 1-4 pick the
/// rooms' state, R reseeds them.
/// </summary>
public partial class Showroom : Node3D
{
    public const float Gap = 1.1f;

    /// <summary>A strip row: a state (and fall), or an age of the intact piece.</summary>
    public record Row(string Label, DecoState State, DecoAge Age = DecoAge.Fresh, bool Snow = false);

    public static readonly Row[] AllRows =
    {
        new("INTACT", DecoState.Intact), new("OLD WORLD", DecoState.Intact, DecoAge.OldWorld), new("PRESERVED", DecoState.Intact, DecoAge.Preserved),
        new("KNOCKED", DecoState.Knocked), new("BROKEN", DecoState.Broken), new("BURNT", DecoState.Burnt), new("OVERGROWN", DecoState.Overgrown),
        new("RUSTED", DecoState.Rusted), new("OPEN", DecoState.Open), new("BURIED", DecoState.Buried),
    };

    public FreeFly Camera { get; private set; }
    public DecoMeshCache Meshes { get; private set; }
    public Label Hud { get; private set; }
    /// <summary>Each set's strip: where it starts, how wide and deep it is, and its row pitch.</summary>
    public readonly Dictionary<string, (Vector3 Origin, float Width, float Depth, float Pitch, Row[] Rows)> Strips = new();
    /// <summary>Where each piece's column stands (its intact spot).</summary>
    public readonly Dictionary<string, Vector3> Columns = new();
    public Node3D Gallery { get; private set; }
    public RoomGallery Rooms { get; private set; }
    public Vector3 OreOrigin { get; private set; }
    public bool BuildGallery = true, BuildRooms = true, BuildOres = true, Lights = true;
    public string OnlySet, OnlyRoom;
    /// <summary>Only this piece, its states laid out side by side in lines of five (a close card for screenshots).</summary>
    public string OnlyPiece;
    int _setIndex;

    public override void _Ready()
    {
        Meshes = new DecoMeshCache(DataPaths.Forge);
        SetupWorld();
        Camera = new FreeFly { Name = "Camera" };
        AddChild(Camera);
        float z = 0;
        if (BuildGallery) z = BuildAllStrips();
        if (BuildOres) { OreOrigin = new Vector3(0, 0, z + 6); BuildOreTells(OreOrigin); }
        if (BuildRooms)
        {
            Rooms = new RoomGallery { Name = "Rooms", Meshes = Meshes, Poi = new DecoPoi(DataPaths.Forge, DataPaths.Arrangements), Position = new Vector3(0, 0, -60), Only = OnlyRoom };
            AddChild(Rooms);
        }
        Hud = new Label { Position = new Vector2(16, 12), Text = HelpText() };
        Hud.AddThemeColorOverride("font_color", new Color(0.95f, 0.92f, 0.85f));
        Hud.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        Hud.AddThemeConstantOverride("outline_size", 6);
        var layer = new CanvasLayer();
        layer.AddChild(Hud);
        AddChild(layer);
        if (Strips.Count > 0) GoToSet(0);
    }

    string HelpText() =>
        "DECORATIONS SHOWROOM   WASD fly, mouse look, Space/Ctrl up/down, Shift fast\n" +
        "[ ] previous / next set   T rooms and sites (as used / as found)   O ore tells   1-4 room state   R reseed rooms   F1 hide this\n" +
        $"{DataPaths.Deco.Pieces.Count} pieces in {DataPaths.Deco.Sets.Count} sets";

    void SetupWorld()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("0e1830"), SkyHorizonColor = new Color("b9764a"),
            GroundBottomColor = new Color("120e0c"), GroundHorizonColor = new Color("4a3428"), SunAngleMax = 20,
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.7f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            GlowEnabled = true, GlowIntensity = 0.5f, GlowBloom = 0.06f,
            FogEnabled = true, FogLightColor = new Color("4a3e3c"), FogDensity = 0.0025f, FogSkyAffect = 0.12f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        var sun = new DirectionalLight3D
        {
            LightColor = new Color("ffe2c0"), LightEnergy = 1.35f, ShadowEnabled = true,
            RotationDegrees = new Vector3(-52, -35, 0), DirectionalShadowMaxDistance = 120,
        };
        AddChild(sun);
    }

    // ---- the set strips -------------------------------------------------------------------------

    float BuildAllStrips()
    {
        Gallery = new Node3D { Name = "Gallery" };
        AddChild(Gallery);
        DecoCatalogue cat = DataPaths.Deco;
        float z = 0;
        foreach (DecoSet set in cat.Sets)
        {
            if (OnlySet != null && OnlySet != set.Id) continue;
            z += BuildStrip(set, new Vector3(0, 0, z)) + 8;
        }
        return z;
    }

    public static float ColumnWidth(DecoPiece p) => Math.Max(Math.Max(p.Width, p.Depth), Math.Max(1.4f, p.Height * 0.7f)) + Gap * (p.Outdoor ? 2.5f : 1f);

    float BuildStrip(DecoSet set, Vector3 origin)
    {
        DecoCatalogue cat = DataPaths.Deco;
        var pieces = cat.OfSet(set.Id).Where(p => DecoForge.HasBuilder(p.Id) && (OnlyPiece == null || p.Id == OnlyPiece)).ToList();
        var strip = new Node3D { Name = set.Id, Position = origin };
        Gallery.AddChild(strip);
        Row[] rows = AllRows.Where(r => r.Age != DecoAge.Fresh || pieces.Any(p => p.Has(r.State))).ToArray();
        float pitch = pieces.Max(p => MathF.Max(p.Depth, MathF.Max(p.Height, p.Width * (p.Outdoor ? 0.35f : 0.6f)))) + (set.Outdoor ? 3.5f : 1.6f);
        if (OnlyPiece != null) return BuildCard(strip, origin, pieces[0], rows, set);
        float x = 2.2f;
        foreach (DecoPiece p in pieces)
        {
            float w = ColumnWidth(p);
            float cx = -(x + w / 2);
            Columns[p.Id] = origin + new Vector3(cx, 0, 0);
            for (int r = 0; r < rows.Length; r++)
            {
                Row row = rows[r];
                DecoState state = row.State;
                if (!p.Has(state)) continue;
                DecoFall fall = state == DecoState.Knocked ? p.Falls[0] : DecoFall.None;
                bool lifted = state is not (DecoState.Knocked) && p.Mount is DecoMount.Wall or DecoMount.Ceiling;
                float y = !lifted ? 0 : p.Mount == DecoMount.Wall ? p.MountY : 2.6f - p.Height;
                var at = new Vector3(cx, y, r * pitch);
                if (lifted && p.Mount == DecoMount.Wall) Stand(strip, at, p, wall: true);
                if (lifted && p.Mount == DecoMount.Ceiling) Stand(strip, at, p, wall: false);
                if (p.Mount == DecoMount.Top && state != DecoState.Knocked) Plinth(strip, at, p);
                DecoCover cover = state == DecoState.Buried ? (r % 2 == 0 ? DecoCover.Snow : DecoCover.Sand) : DecoCover.Vines;
                bool powered = row.Age == DecoAge.Fresh && state is DecoState.Intact or DecoState.Knocked;
                var look = new DecoLook(p.Id, state, fall, powered, cover, p.Palette, false, row.Age, row.Age == DecoAge.OldWorld && p.Outdoor);
                Vector3 pos = at + (p.Mount == DecoMount.Top && state != DecoState.Knocked ? new Vector3(0, 0.75f, 0) : Vector3.Zero);
                strip.AddChild(DecoNode.Create(Meshes, look, new Transform3D(Basis.Identity, pos), collide: true, lights: Lights && r == 0));
            }
            Text(strip, p.Name, new Vector3(cx, 0.02f, -MathF.Max(1.1f, p.Depth / 2 + 0.6f)), p.Outdoor ? 0.7f : 0.32f, flat: true);
            x += w;
        }
        float width = x + 1f, depth = rows.Length * pitch;
        Strips[set.Id] = (origin, width, depth, pitch, rows);
        for (int r = 0; r < rows.Length; r++)
            Text(strip, rows[r].Label, new Vector3(-1.0f, 0.02f, r * pitch), set.Outdoor ? 0.9f : 0.42f, flat: true);
        Text(strip, $"{set.Name.ToUpperInvariant()}   {pieces.Count} pieces", new Vector3(-1.2f, 0.02f, -2.2f - (set.Outdoor ? 3 : 0)), set.Outdoor ? 1.6f : 0.7f, flat: true, left: true);
        Floor(strip, new Vector3(-width / 2, 0, (rows.Length - 1) * pitch / 2 - 1), new Vector2(width + 2, depth + 5));
        return depth + 5;
    }

    /// <summary>One piece in every state it has, in lines of five, each labelled; Strips and Columns describe the card.</summary>
    float BuildCard(Node3D strip, Vector3 origin, DecoPiece p, Row[] rows, DecoSet set)
    {
        rows = rows.Where(r => p.Has(r.State)).ToArray();
        float w = Math.Max(p.Width, p.Height) + (p.Outdoor ? 1.6f : 0.8f), pitch = Math.Max(p.Depth, p.Height) + (p.Outdoor ? 2.2f : 1.2f);
        int perLine = Math.Min(5, rows.Length), lines = (rows.Length + perLine - 1) / perLine;
        for (int r = 0; r < rows.Length; r++)
        {
            Row row = rows[r];
            float cx = -(2.2f + w * (r % perLine + 0.5f)), z = (r / perLine) * pitch;
            DecoState state = row.State;
            DecoFall fall = state == DecoState.Knocked ? p.Falls[0] : DecoFall.None;
            bool lifted = state is not DecoState.Knocked && p.Mount is DecoMount.Wall or DecoMount.Ceiling;
            float y = !lifted ? 0 : p.Mount == DecoMount.Wall ? p.MountY : 2.6f - p.Height;
            var at = new Vector3(cx, y, z);
            if (lifted) Stand(strip, at, p, wall: p.Mount == DecoMount.Wall);
            if (p.Mount == DecoMount.Top && state != DecoState.Knocked) Plinth(strip, at, p);
            DecoCover cover = state == DecoState.Buried ? (r % 2 == 0 ? DecoCover.Snow : DecoCover.Sand) : DecoCover.Vines;
            bool powered = row.Age == DecoAge.Fresh && state is DecoState.Intact or DecoState.Knocked;
            var look = new DecoLook(p.Id, state, fall, powered, cover, p.Palette, false, row.Age, row.Age == DecoAge.OldWorld && p.Outdoor);
            Vector3 pos = at + (p.Mount == DecoMount.Top && state != DecoState.Knocked ? new Vector3(0, 0.75f, 0) : Vector3.Zero);
            strip.AddChild(DecoNode.Create(Meshes, look, new Transform3D(Basis.Identity, pos), collide: true, lights: Lights));
            Text(strip, row.Label, new Vector3(cx, 0.02f, z - MathF.Max(0.9f, p.Depth / 2 + 0.5f)), p.Outdoor ? 0.6f : 0.3f, flat: true);
        }
        float width = 2.2f + w * perLine + 1f, depth = lines * pitch;
        Columns[p.Id] = origin + new Vector3(-(2.2f + w * perLine / 2), 0, 0);
        Strips[set.Id] = (origin, width, depth, pitch, rows);
        Text(strip, p.Name.ToUpperInvariant(), new Vector3(-1.2f, 0.02f, -2.0f - (p.Outdoor ? 2 : 0)), p.Outdoor ? 1.2f : 0.5f, flat: true, left: true);
        Floor(strip, new Vector3(-width / 2, 0, depth / 2 - pitch / 2), new Vector2(width + 6, depth + 10));
        return depth + 5;
    }

    // ---- ore tells --------------------------------------------------------------------------------

    /// <summary>The planet generator's ore colours and glows (DepositLook), as the game will pass them in.</summary>
    public static readonly (string Name, string Hex, float Glow, bool Glints)[] Ores =
    {
        ("Iron", "d0582c", 0.25f, true), ("Copper", "2cc8a0", 0.25f, true), ("Silicon", "f4f2ff", 0.25f, true), ("Carbon", "34303c", 0.05f, false),
        ("Water ice", "7cd4ff", 0.25f, false), ("Titanium", "c8ccdc", 0.4f, true), ("Cobalt", "2a58f0", 0.4f, true), ("Sulfur", "f4d81c", 0.4f, false),
        ("Thorium", "6aff5a", 0.6f, false), ("Crystal", "e070ff", 0.6f, true), ("Resin", "ffa020", 0.4f, false),
    };

    public static readonly (string Label, DecoTellKind Kind, int Shape, float Radius)[] TellRows =
    {
        ("OUTCROP: SLABS", DecoTellKind.Outcrop, 0, 1.6f), ("OUTCROP: BEDROCK", DecoTellKind.Outcrop, 1, 1.6f), ("OUTCROP: BOULDERS", DecoTellKind.Outcrop, 2, 1.8f),
        ("FRACTURE", DecoTellKind.Fracture, 0, 1.4f), ("SCREE", DecoTellKind.Scree, 0, 1.4f),
    };

    void BuildOreTells(Vector3 origin)
    {
        var holder = new Node3D { Name = "OreTells", Position = origin };
        AddChild(holder);
        const float pitchX = 5f, pitchZ = 5.5f;
        for (int o = 0; o < Ores.Length; o++)
        {
            var (name, hex, glow, glints) = Ores[o];
            float x = -(2.5f + o * pitchX + pitchX / 2);
            for (int r = 0; r < TellRows.Length; r++)
            {
                var (_, kind, shape, radius) = TellRows[r];
                DecoMeshData mesh = DecoOreTells.Build(kind, DecoRgb.Hex(hex), glow, glints, (ulong)(o * 31 + r * 7 + 1), radius, null, shape);
                var mi = new MeshInstance3D { Mesh = DecoGodot.ToMesh(mesh), Position = new Vector3(x, 0, r * pitchZ) };
                holder.AddChild(mi);
                if (mesh.Boxes.Count > 0)
                {
                    var body = new StaticBody3D { Position = mi.Position };
                    foreach (var (shapeRes, xf) in DecoGodot.ToShapes(mesh)) body.AddChild(new CollisionShape3D { Shape = shapeRes, Transform = xf });
                    holder.AddChild(body);
                }
            }
            Text(holder, name, new Vector3(x, 0.02f, -2.6f), 0.45f, flat: true);
        }
        for (int r = 0; r < TellRows.Length; r++) Text(holder, TellRows[r].Label, new Vector3(-1f, 0.02f, r * pitchZ), 0.4f, flat: true);
        Text(holder, "ORE SURFACE TELLS", new Vector3(-1.2f, 0.02f, -4.2f), 0.8f, flat: true, left: true);
        float width = 2.5f + Ores.Length * pitchX + 1;
        Floor(holder, new Vector3(-width / 2, 0, (TellRows.Length - 1) * pitchZ / 2 - 1), new Vector2(width + 2, TellRows.Length * pitchZ + 6), RoomGallery.GroundMaterial);
    }

    /// <summary>A low plinth for pieces that stand on desks and tables, so they read at their real height.</summary>
    void Plinth(Node3D parent, Vector3 at, DecoPiece p)
    {
        var mesh = new BoxMesh { Size = new Vector3(Math.Max(0.6f, p.Width + 0.1f), 0.75f, Math.Max(0.6f, p.Depth + 0.1f)) };
        parent.AddChild(new MeshInstance3D { Mesh = mesh, Position = at + new Vector3(0, 0.375f, 0), MaterialOverride = StandMaterial });
    }

    /// <summary>A display wall (for wall pieces) or gantry (for ceiling pieces).</summary>
    void Stand(Node3D parent, Vector3 at, DecoPiece p, bool wall)
    {
        if (wall)
        {
            var mesh = new BoxMesh { Size = new Vector3(p.Width + 0.3f, MathF.Max(2.6f, p.MountY + p.Height + 0.2f), 0.12f) };
            parent.AddChild(new MeshInstance3D { Mesh = mesh, Position = new Vector3(at.X, mesh.Size.Y / 2, at.Z + p.Depth / 2 + 0.06f), MaterialOverride = StandMaterial });
        }
        else
        {
            var beam = new BoxMesh { Size = new Vector3(p.Width + 0.6f, 0.12f, 0.2f) };
            parent.AddChild(new MeshInstance3D { Mesh = beam, Position = new Vector3(at.X, 2.66f, at.Z), MaterialOverride = StandMaterial });
            foreach (float dx in new[] { -1, 1 })
            {
                var post = new BoxMesh { Size = new Vector3(0.1f, 2.7f, 0.1f) };
                parent.AddChild(new MeshInstance3D { Mesh = post, Position = new Vector3(at.X + dx * (p.Width / 2 + 0.25f), 1.35f, at.Z), MaterialOverride = StandMaterial });
            }
        }
    }

    static StandardMaterial3D _stand;
    public static StandardMaterial3D StandMaterial => _stand ??= new StandardMaterial3D { AlbedoColor = new Color("3a3632"), Roughness = 0.95f };

    const string FloorShader = @"
shader_type spatial;
uniform vec3 base : source_color = vec3(0.17, 0.16, 0.15);
uniform vec3 major : source_color = vec3(0.42, 0.36, 0.28);
varying vec3 wp;
void vertex() { wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
float lines(vec2 p, float step, float width) {
    vec2 d = abs(fract(p / step + 0.5) - 0.5) * step;
    vec2 fw = fwidth(p);
    vec2 l = 1.0 - smoothstep(vec2(width) - fw, vec2(width) + fw, d);
    return max(l.x, l.y);
}
void fragment() {
    vec3 c = base;
    c = mix(c, c * 1.45, lines(wp.xz, 0.5, 0.006) * 0.6);
    c = mix(c, major, lines(wp.xz, 2.0, 0.012) * 0.55);
    ALBEDO = c;
    ROUGHNESS = 0.95;
}";

    static ShaderMaterial _floor;
    public static ShaderMaterial FloorMaterial => _floor ??= new ShaderMaterial { Shader = new Shader { Code = FloorShader } };

    /// <summary>A floor slab with the building grid drawn on it (faint 0.5 m small-grid lines and 2 m large-grid lines), or plain ground.</summary>
    public static void Floor(Node3D parent, Vector3 centre, Vector2 size, Material material = null)
    {
        var mesh = new BoxMesh { Size = new Vector3(size.X, 0.2f, size.Y) };
        parent.AddChild(new MeshInstance3D { Mesh = mesh, Position = centre + new Vector3(0, -0.1f, 0), MaterialOverride = material ?? FloorMaterial });
        var body = new StaticBody3D();
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = mesh.Size }, Position = centre + new Vector3(0, -0.1f, 0) });
        parent.AddChild(body);
    }

    public static Label3D Text(Node3D parent, string text, Vector3 at, float size, bool flat = false, bool left = false)
    {
        var l = new Label3D
        {
            Text = text, Position = at, PixelSize = size / 64f, FontSize = 64, OutlineSize = 10,
            Modulate = new Color(0.95f, 0.9f, 0.8f), OutlineModulate = new Color(0.08f, 0.07f, 0.06f),
            HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            RotationDegrees = flat ? new Vector3(-70, 180, 0) : new Vector3(0, 180, 0),
        };
        parent.AddChild(l);
        return l;
    }

    // ---- camera presets --------------------------------------------------------------------------

    public void GoToSet(int index)
    {
        var ids = Strips.Keys.ToList();
        if (ids.Count == 0) return;
        _setIndex = (index % ids.Count + ids.Count) % ids.Count;
        var (from, to) = SetView(ids[_setIndex], 0, 1);
        Camera.Place(from, to);
    }

    /// <summary>A camera view of part of a set's strip: <paramref name="part"/> of <paramref name="parts"/> along its length.</summary>
    public (Vector3 From, Vector3 To) SetView(string setId, int part, int parts)
    {
        var (origin, width, depth, _, _) = Strips[setId];
        float span = (width - 2.2f) / parts;
        float cx = -(2.2f + span * (part + 0.5f));
        float dist = Math.Max(span / 2.3f, depth * 0.62f) + 1.5f;
        var target = origin + new Vector3(cx, 0.2f, depth * 0.38f);
        var from = target + new Vector3(0, dist * 0.82f, -dist * 0.78f);
        return (from, target);
    }

    public (Vector3 From, Vector3 To) OreView()
    {
        var target = OreOrigin + new Vector3(-(2.5f + Ores.Length * 5f / 2), 0.3f, 10f);
        return (target + new Vector3(0, 22f, -24f), target);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo) return;
        switch (k.Keycode)
        {
            case Key.Bracketright: GoToSet(_setIndex + 1); break;
            case Key.Bracketleft: GoToSet(_setIndex - 1); break;
            case Key.F1: Hud.Visible = !Hud.Visible; break;
            case Key.T when Rooms != null: var (f, t) = Rooms.Overview(); Camera.Place(f, t); break;
            case Key.O when BuildOres: var (of, ot) = OreView(); Camera.Place(of, ot); break;
            case Key.R when Rooms != null: Rooms.Reseed(); break;
            case >= Key.Key1 and <= Key.Key4 when Rooms != null: Rooms.SetState((int)(k.Keycode - Key.Key1)); break;
        }
    }
}
