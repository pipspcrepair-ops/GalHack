using System.Collections.Generic;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Fx;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using TerrainDigging.Terrain;

namespace PlanetSurface.Player;

/// <summary>
/// The survival tool, with terrain-digging's feel: hold LMB to dig a small grid-aligned ball of
/// cells under the crosshair, right click to place a 2 m building block. Aiming walks the cell
/// grid (not the collision mesh), so the target is always the real terrain even mid-rebuild.
/// Everything goes through <see cref="PlanetTerrain"/>, so digs on a seam land on both sides.
/// Ore is counted by the resource its deposit holds.
/// </summary>
public partial class SurvivalTool : Node
{
    public const float Reach = 7.5f, DigInterval = 0.14f, DigRadius = 1.42f;

    public Main Main;
    public readonly Dictionary<string, int> Gathered = new();

    public bool HasTarget { get; private set; }
    public Int3 Target { get; private set; }
    public Int3 TargetFace { get; private set; }
    public Vector3 TargetPoint { get; private set; }
    public Verdict TargetVerdict { get; private set; }
    public bool AimingAtStructure { get; private set; }

    /// <summary>Aiming at a tree the tool can fell (LMB cuts it down, for wood).</summary>
    public bool AimingAtTree { get; private set; }
    public FloraSolid TargetTree { get; private set; }
    public bool ShowCursor = true;

    float _cooldown;
    bool _rightWasDown;
    MeshInstance3D _cursor;
    StandardMaterial3D _cursorMaterial;

    PlanetTerrain Terrain => Main.World.Terrain;
    Walker Player => Main.Player;

    public override void _Ready()
    {
        _cursorMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true, AlbedoColor = new Color(1f, 1f, 1f, 0.5f),
        };
        _cursor = new MeshInstance3D
        {
            Mesh = CubeLines(1.04f), MaterialOverride = _cursorMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off, Visible = false,
        };
        Main.AddChild(_cursor);
    }

    public override void _PhysicsProcess(double delta)
    {
        _cooldown -= (float)delta;
        if (!Main.IsReady || Main.Flying)
        {
            _cursor.Visible = false;
            HasTarget = false;
            return;
        }
        Aim();
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured && Player.ControlsEnabled;
        bool left = captured && Input.IsMouseButtonPressed(MouseButton.Left);
        bool right = captured && Input.IsMouseButtonPressed(MouseButton.Right);
        if (left && _cooldown <= 0f)
        {
            if (AimingAtTree) FellAtTarget();
            else DigAtTarget();
        }
        if (right && !_rightWasDown) PlaceBlock();
        _rightWasDown = right;
    }

    public void Aim()
    {
        Vector3 eye = Player.EyePosition, dir = Player.LookDirection;
        HasTarget = Terrain.Store.Raycast(ToN(eye), ToN(dir), Reach, out Int3 cell, out Int3 face, out float distance);
        AimingAtStructure = false;
        var query = PhysicsRayQueryParameters3D.Create(eye, eye + dir * Reach, Layers.Structure | Layers.Poi);
        var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0 && (!HasTarget || eye.DistanceTo((Vector3)hit["position"]) < distance))
        {
            HasTarget = false;
            AimingAtStructure = true;
        }
        // A trunk or a boulder in front of the ground: a tree can be felled, a boulder only blocks the tool.
        AimingAtTree = false;
        var flora = PhysicsRayQueryParameters3D.Create(eye, eye + dir * Reach, Layers.Flora);
        var floraHit = Player.GetWorld3D().DirectSpaceState.IntersectRay(flora);
        if (!AimingAtStructure && floraHit.Count > 0 && (!HasTarget || eye.DistanceTo((Vector3)floraHit["position"]) < distance)
            && Main.Streamer.SolidHit((Rid)floraHit["rid"], (int)floraHit["shape"], out FloraSolid solid))
        {
            HasTarget = false;
            AimingAtTree = solid.Fellable;
            TargetTree = solid;
        }
        if (HasTarget)
        {
            Target = cell;
            TargetFace = face;
            TargetPoint = eye + dir * distance;
            TargetVerdict = Terrain.Check(cell, EditKind.Tool);
        }
        Color color = AimingAtTree ? Hud.Good : AimingAtStructure ? Hud.Caution
            : !HasTarget ? new Color(1f, 1f, 1f, 0.5f)
            : TargetVerdict switch { Verdict.Bedrock => Hud.Warning, Verdict.Anchored => Hud.Caution, _ => new Color(1f, 1f, 1f, 0.9f) };
        Main.Hud.SetCrosshair(color);
        _cursor.Visible = HasTarget && ShowCursor;
        if (HasTarget)
        {
            _cursor.Position = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            _cursorMaterial.AlbedoColor = new Color(color, 0.45f);
        }
    }

    /// <summary>Fells the tree under the crosshair, for its wood. Public so tests can cut without a mouse.</summary>
    public bool FellAtTarget()
    {
        if (!AimingAtTree) return false;
        _cooldown = 0.4f;
        bool felled = Main.FellTree(TargetTree);
        if (felled)
        {
            Player.Kick(0.8f, 0.3f);
            AimingAtTree = false;
        }
        return felled;
    }

    /// <summary>One dig pulse at the current target. Public so tests can dig without a mouse.</summary>
    public PlanetEdit DigAtTarget()
    {
        if (!HasTarget) return null;
        _cooldown = DigInterval;
        PlanetEdit result = Main.Dig(Target, DigRadius);
        if (result.Removed.Count > 0)
        {
            int ore = 0;
            foreach (RemovedCell r in result.Removed)
            {
                string what = r.Material == CellMaterial.Ore ? Main.World.Source.OreKindAt(r.Cell).ToString() : r.Material.Label();
                Gathered[what] = Gathered.TryGetValue(what, out int n) ? n + 1 : 1;
                if (r.Material == CellMaterial.Ore) ore++;
            }
            Main.Debris.Burst(result.Removed, Player.EyePosition);
            Player.Kick(0.55f + 0.02f * result.Removed.Count, 0.15f);
            Main.Sfx.Play(Sfx.Sound.Dig, TargetPoint);
            if (ore > 0)
            {
                Main.Sfx.Play(Sfx.Sound.Ore, TargetPoint, 1f, -4f);
                Main.Hud.Toast($"+{ore} {Main.World.Source.OreKindAt(Target)}", Hud.Good, 0.9f);
            }
            return result;
        }
        _cooldown = 0.3f;
        Vector3 normal = new(TargetFace.X, TargetFace.Y, TargetFace.Z);
        if (result.Headline == Verdict.Bedrock)
        {
            Main.Hud.Toast($"BEDROCK · {PlanetTerrainSource.BedrockDepth:F0} m under the valley floors the tool can't cut", Hud.Warning);
            Main.Debris.Sparks(TargetPoint, normal, new Color("9d8cff"));
            Main.Sfx.Play(Sfx.Sound.Clank, TargetPoint);
            Player.Kick(0.25f, 0.7f);
        }
        else if (result.Headline == Verdict.Anchored)
        {
            Main.Hud.Toast("ANCHORED · the ground under and around structures holds", Hud.Caution);
            Main.Debris.Sparks(TargetPoint, normal, Hud.Caution);
            Main.Sfx.Play(Sfx.Sound.Clank, TargetPoint, 1.3f, -3f);
            Player.Kick(0.2f, 0.4f);
        }
        return result;
    }

    /// <summary>Places a 2 m block half-sunk where the crosshair meets the ground.</summary>
    public PlaceResult PlaceBlock()
    {
        if (!HasTarget)
        {
            if (AimingAtStructure) Main.Hud.Toast("Place blocks against the ground", Hud.Caution);
            return null;
        }
        CellBox box = BlockBoxAt(Target, TargetFace, TargetPoint);
        // Never build a block around a body: that would be terrain growing into it.
        Vector3 feet = Player.GlobalPosition;
        if (feet.X - Walker.Radius < box.Max.X && feet.X + Walker.Radius > box.Min.X && feet.Y < box.Max.Y &&
            feet.Y + Player.CurrentHeight > box.Min.Y && feet.Z - Walker.Radius < box.Max.Z && feet.Z + Walker.Radius > box.Min.Z)
        {
            Main.Hud.Toast("Too close: step back to place a block", Hud.Caution);
            return null;
        }
        PlaceResult result = Main.PlaceStructure(box);
        switch (result.Verdict)
        {
            case PlaceVerdict.Placed:
                Main.Debris.Burst(result.Cleared.Removed, Player.EyePosition, 0.6f);
                Main.Sfx.Play(Sfx.Sound.Place, new Vector3(box.CenterPosition.X, box.CenterPosition.Y, box.CenterPosition.Z));
                Player.Kick(0.8f, 0.2f);
                Main.Hud.Toast($"Block placed · cleared {result.Cleared.Removed.Count} cells · the ground around it is anchored", Hud.Good);
                break;
            case PlaceVerdict.Overlaps: Main.Hud.Toast("Another block is in the way", Hud.Caution); break;
            case PlaceVerdict.Bedrock: Main.Hud.Toast("Can't build into bedrock", Hud.Warning); break;
            case PlaceVerdict.AnchoredByPoi: Main.Hud.Toast("This place's ground is protected: build elsewhere", Hud.Caution); break;
            default: Main.Hud.Toast("Can't place a block there", Hud.Caution); break;
        }
        return result;
    }

    public static CellBox BlockBoxAt(Int3 cell, Int3 face, Vector3 point)
    {
        int Axis(int c, int f, float p) => f != 0 ? Mathf.Min(c, c + f) : c - 1 + (p - Mathf.Floor(p) >= 0.5f ? 1 : 0);
        var min = new Int3(Axis(cell.X, face.X, point.X), Axis(cell.Y, face.Y, point.Y), Axis(cell.Z, face.Z, point.Z));
        return CellBox.FromSize(min, new Int3(2, 2, 2));
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
}
