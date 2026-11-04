using System.Linq;
using Godot;
using TerrainDigging.Terrain;

namespace TerrainDigging;

/// <summary>
/// The survival tool in gather mode. Hold LMB to dig a small grid-aligned ball of cells under
/// the crosshair; right click to place a 2 m building block; C fires a test crater.
///
/// Aiming walks the cell grid itself (not the collision mesh), so the target is always the
/// real terrain even while a chunk's new mesh is still being built.
/// </summary>
public partial class GatherTool : Node
{
    public const float Reach = 7.5f, DigInterval = 0.14f, DigRadius = 1.42f;
    public const float CraterReach = 60f, CraterRadius = 6f, CraterLift = 1.5f;

    public Main Main;
    public readonly int[] Gathered = new int[CellMaterials.Count];

    public bool HasTarget { get; private set; }
    public Int3 Target { get; private set; }
    public Int3 TargetFace { get; private set; }
    public Vector3 TargetPoint { get; private set; }
    public Verdict TargetVerdict { get; private set; }
    public bool AimingAtStructure { get; private set; }

    /// <summary>The wireframe cell under the crosshair (hidden for screenshots).</summary>
    public bool ShowCursor = true;

    float _cooldown;
    bool _rightWasDown;
    MeshInstance3D _cursor;
    StandardMaterial3D _cursorMaterial;

    ChunkStore Store => Main.Store;
    Player Player => Main.Player;

    public override void _Ready()
    {
        _cursorMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
            AlbedoColor = new Color(1f, 1f, 1f, 0.5f),
        };
        _cursor = new MeshInstance3D
        {
            Mesh = CubeLines(1.04f),
            MaterialOverride = _cursorMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off,
            Visible = false,
        };
        Main.AddChild(_cursor);
    }

    public override void _PhysicsProcess(double delta)
    {
        _cooldown -= (float)delta;
        if (!Main.IsReady || Main.View.IsVerifying)
        {
            _cursor.Visible = false;
            return;
        }
        Aim();

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured && Player.ControlsEnabled;
        bool left = captured && Input.IsMouseButtonPressed(MouseButton.Left);
        bool right = captured && Input.IsMouseButtonPressed(MouseButton.Right);
        if (left && _cooldown <= 0f) DigAtTarget();
        if (right && !_rightWasDown) PlaceBlock();
        _rightWasDown = right;
    }

    void Aim()
    {
        Vector3 eye = Player.EyePosition, dir = Player.LookDirection;
        HasTarget = Store.Raycast(ToN(eye), ToN(dir), Reach, out Int3 cell, out Int3 face, out float distance);
        AimingAtStructure = false;

        var query = PhysicsRayQueryParameters3D.Create(eye, eye + dir * Reach, Layers.Structure);
        var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0 && (!HasTarget || eye.DistanceTo((Vector3)hit["position"]) < distance))
        {
            HasTarget = false;
            AimingAtStructure = true;
        }

        if (HasTarget)
        {
            Target = cell;
            TargetFace = face;
            TargetPoint = eye + dir * distance;
            TargetVerdict = Store.Check(cell, EditKind.Tool);
        }

        Color color = AimingAtStructure ? Palette.Caution
            : !HasTarget ? new Color(1f, 1f, 1f, 0.5f)
            : TargetVerdict switch
            {
                Verdict.Bedrock => Palette.Warning,
                Verdict.Anchored => Palette.Caution,
                _ => new Color(1f, 1f, 1f, 0.9f),
            };
        Main.Hud.SetCrosshair(color);
        _cursor.Visible = HasTarget && ShowCursor;
        if (HasTarget)
        {
            _cursor.Position = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            _cursorMaterial.AlbedoColor = new Color(color, 0.45f);
        }
    }

    /// <summary>One dig pulse at the current target. Public so tests can dig without a mouse.</summary>
    public EditResult DigAtTarget()
    {
        if (!HasTarget) return null;
        _cooldown = DigInterval;
        EditResult result = Store.Dig(Target, DigRadius);
        Main.View.MarkDirty(result.DirtyChunks);

        if (result.Removed.Count > 0)
        {
            int ore = 0;
            foreach (RemovedCell r in result.Removed)
            {
                Gathered[(int)r.Material]++;
                if (r.Material == CellMaterial.Ore) ore++;
            }
            Main.Debris.Burst(result.Removed, Player.EyePosition);
            Player.Kick(0.55f + 0.02f * result.Removed.Count, 0.15f);
            int stone = result.Removed.Count(r => r.Material is CellMaterial.Rock or CellMaterial.DeepRock);
            Main.Sfx.Play(Sfx.Sound.Dig, TargetPoint, stone * 2 > result.Removed.Count ? 0.8f : 1f);
            if (ore > 0)
            {
                Main.Hud.PopOre();
                Main.Sfx.Play(Sfx.Sound.Ore, TargetPoint, 1f, -4f);
                Main.Hud.Toast($"+{ore} ore", Palette.Ore, 0.9f);
            }
            return result;
        }

        // Nothing came out: say why, loudly enough that it never feels like a bug.
        _cooldown = 0.3f;
        Vector3 normal = new(TargetFace.X, TargetFace.Y, TargetFace.Z);
        if (result.Headline == Verdict.Bedrock)
        {
            Main.Hud.Toast("BEDROCK · 30 m below the surface the tool can't cut", Palette.Warning);
            Main.Debris.Sparks(TargetPoint, normal, new Color("9d8cff"));
            Main.Sfx.Play(Sfx.Sound.Clank, TargetPoint);
            Player.Kick(0.25f, 0.7f);
        }
        else if (result.Headline == Verdict.Anchored)
        {
            Main.Hud.Toast("ANCHORED · ground under and around structures holds", Palette.Caution);
            Main.Debris.Sparks(TargetPoint, normal, Palette.Caution);
            Main.Sfx.Play(Sfx.Sound.Clank, TargetPoint, 1.3f, -3f);
            Player.Kick(0.2f, 0.4f);
            foreach (AnchorZone zone in Store.Anchors.Where(z => z.Box.Contains(Target)))
                Main.Structures.FlashAnchor(zone.Box, Palette.Caution);
        }
        return result;
    }

    /// <summary>
    /// Places a 2 m block half-sunk where the crosshair meets the ground: the cell hit and
    /// the open cell in front of it along the face, and the two nearest cells on each other axis.
    /// </summary>
    public PlaceResult PlaceBlock()
    {
        if (!HasTarget)
        {
            if (AimingAtStructure) Main.Hud.Toast("Blocks can't stack in this prototype: place against ground", Palette.Caution);
            return null;
        }
        CellBox box = BlockBoxAt(Target, TargetFace, TargetPoint);

        // Never build a block around a body: that would be "terrain" growing into it.
        Vector3 feet = Player.GlobalPosition;
        var playerMin = new Vector3(feet.X - Player.Radius, feet.Y, feet.Z - Player.Radius);
        var playerMax = new Vector3(feet.X + Player.Radius, feet.Y + Player.CurrentHeight, feet.Z + Player.Radius);
        if (playerMin.X < box.Max.X && playerMax.X > box.Min.X && playerMin.Y < box.Max.Y &&
            playerMax.Y > box.Min.Y && playerMin.Z < box.Max.Z && playerMax.Z > box.Min.Z)
        {
            Main.Hud.Toast("Too close: step back to place a block", Palette.Caution);
            return null;
        }

        PlaceResult result = Store.PlaceStructure(box);
        switch (result.Verdict)
        {
            case PlaceVerdict.Placed:
                Main.View.MarkDirty(result.Cleared.DirtyChunks);
                Main.Structures.Add(result.Structure);
                Main.Structures.FlashAnchor(ChunkStore.AnchorBoxFor(box), Palette.Good);
                Main.Debris.Burst(result.Cleared.Removed, Player.EyePosition, 0.6f);
                Main.Sfx.Play(Sfx.Sound.Place, ToGodot(box.CenterPosition));
                Player.Kick(0.8f, 0.2f);
                Main.Hud.Toast($"Block placed · cleared {result.Cleared.Removed.Count} cells · ground around it is now anchored", Palette.Good);
                break;
            case PlaceVerdict.Overlaps:
                Main.Hud.Toast("Another block is in the way", Palette.Caution);
                break;
            case PlaceVerdict.Bedrock:
                Main.Hud.Toast("Can't build into bedrock", Palette.Warning);
                break;
            case PlaceVerdict.AnchoredByPoi:
                Main.Hud.Toast("POI ground is protected: build elsewhere", Palette.Caution);
                break;
            default:
                Main.Hud.Toast("Can't place a block there", Palette.Caution);
                break;
        }
        return result;
    }

    public static CellBox BlockBoxAt(Int3 cell, Int3 face, Vector3 point)
    {
        int Axis(int c, int f, float p) => f != 0 ? Mathf.Min(c, c + f) : c - 1 + (p - Mathf.Floor(p) >= 0.5f ? 1 : 0);
        var min = new Int3(Axis(cell.X, face.X, point.X), Axis(cell.Y, face.Y, point.Y), Axis(cell.Z, face.Z, point.Z));
        return CellBox.FromSize(min, new Int3(2, 2, 2));
    }

    /// <summary>A test crash or liftoff blast where the crosshair meets the ground (rule 10).</summary>
    public EditResult Crater()
    {
        Vector3 eye = Player.EyePosition, dir = Player.LookDirection;
        if (!Store.Raycast(ToN(eye), ToN(dir), CraterReach, out _, out _, out float distance))
        {
            Main.Hud.Toast("Aim at the ground for a test crater", Palette.Caution);
            return null;
        }
        Vector3 center = eye + dir * distance;
        // The sphere's centre sits above the ground, so the blast cuts a wide, shallow cap.
        EditResult result = Store.Crater(ToN(center + Vector3.Up * CraterLift), CraterRadius);
        Main.View.MarkDirty(result.DirtyChunks);
        Main.Debris.Burst(result.Removed, center - dir * 2f + Vector3.Down, 2.4f);
        for (int i = 0; i < 6; i++) Main.Debris.Puff(center + Vector3.Up * i * 0.6f, Palette.Dust, 3.5f);
        Main.Flash(center, new Color("ffb070"));
        Main.Sfx.Play(Sfx.Sound.Boom, center, 1f, 4f);
        Player.Kick(1.2f, Mathf.Clamp(1.4f - distance / 50f, 0.4f, 1.4f));
        int held = result.Bedrock + result.Anchored;
        Main.Hud.Toast($"Crater · {result.Removed.Count} cells removed (capped at {ChunkStore.MaxCraterCells})" +
                       (held > 0 ? $" · {held} held by bedrock or anchors" : ""), Palette.Text);
        return result;
    }

    static ArrayMesh CubeLines(float size)
    {
        float h = size / 2f;
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++) corners[i] = new Vector3((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -h : h);
        int[] edges = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
        var vertices = new Vector3[edges.Length];
        for (int i = 0; i < edges.Length; i++) vertices[i] = corners[edges[i]];
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return mesh;
    }

    static System.Numerics.Vector3 ToN(Vector3 v) => new(v.X, v.Y, v.Z);
    static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
