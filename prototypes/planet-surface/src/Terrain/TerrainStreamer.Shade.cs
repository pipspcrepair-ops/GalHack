using System;
using Godot;

namespace PlanetSurface.Terrain;

/// <summary>
/// The far land's shade past the sun's real-time shadows (<see cref="FarShade"/>, shade.gdshaderinc): a square round the
/// eye, its corner on its own cells, marched on the graphics card again whenever the sun has turned a hair, the square
/// has moved a cell or the ground under it has come in. Nothing waits on the CPU and nothing is blended: each march is
/// whole the frame it is asked for.
/// </summary>
public partial class TerrainStreamer
{
    /// <summary>Toward the sun (unit, frame), the game's sky sets it; and how far the sun's real-time shadows reach, m (the shadow distance setting).</summary>
    public Func<Vector3> SunToward;
    public float ShadowReach = 320f;

    /// <summary>The square marched last: its corner (frame), its cells' width, the sun's flat way, and the coarsest ring's tiles it saw.</summary>
    Vector2 _shadeCorner = new(float.NaN, float.NaN), _shadeToward;
    float _shadeCell;
    int _shadeTops = -1;

    /// <summary>How far the sun turns before the shade is marched again, radians: a small share of its soft edge.</summary>
    const float ShadeTurn = 0.002f;

    void UpdateShade()
    {
        if (SunToward == null || World == null) return;
        Vector3 sun = SunToward();
        var flat = new Vector2(sun.X, sun.Z);
        float rise = flat.Length() > 1e-4f ? sun.Y / flat.Length() : 1000f;
        if (flat.LengthSquared() > 1e-8f) flat = flat.Normalized();
        float half = MathF.Min(DetailReach.Distance + 2000f, Clip.ShadeReach), cell = 2f * half / FarShade.N;
        Vector3 eye = Eye();
        var corner = new Vector2(MathF.Floor((eye.X - half) / cell) * cell, MathF.Floor((eye.Z - half) / cell) * cell);
        if (Clip.Complete && Clip.ShadeHolds(eye, half + FarShade.Look) && sun.Y > -0.05f && (corner != _shadeCorner || cell != _shadeCell || _shadeTops != Clip.TopVersion || _shadeToward.Dot(flat) < MathF.Cos(ShadeTurn)))
        {
            Clip.March(corner, cell, flat);
            (_shadeCorner, _shadeCell, _shadeToward, _shadeTops) = (corner, cell, flat, Clip.TopVersion);
        }
        // A material parameter set costs the render thread, so the materials take new values only when something changed:
        // the sun a tenth of a degree on, the square moved, a material made since.
        var now = new Vector4(sun.X, sun.Y, sun.Z, rise);
        var rect = float.IsNaN(_shadeCorner.X) ? Vector4.Zero : new Vector4(_shadeCorner.X, _shadeCorner.Y, _shadeCell * FarShade.N, 1f);
        int live = (VoxelMaterial != null ? 1 : 0) + (FarMaterial != null ? 1 : 0) + (_horizonMaterial != null ? 1 : 0) + (_floraMaterial != null ? 1 : 0);
        if (live == _pushedMaterials && now.DistanceSquaredTo(_pushedSun) < 4e-6f && _pushedReach == ShadowReach && rect == _pushedRect && _pushedMap == Clip.ShadeMap) return;
        (_pushedSun, _pushedReach, _pushedRect, _pushedMap, _pushedMaterials) = (now, ShadowReach, rect, Clip.ShadeMap, live);
        foreach (ShaderMaterial m in new[] { VoxelMaterial, FarMaterial, _horizonMaterial, _floraMaterial })
        {
            if (m == null) continue;
            m.SetShaderParameter("shade_sun", now);
            m.SetShaderParameter("shade_reach", ShadowReach);
            m.SetShaderParameter("shade_join", new Vector2(FarShade.JoinFrom, FarShade.JoinFull));
            m.SetShaderParameter("shade_rect", Clip.ShadeMap == null ? Vector4.Zero : rect);
            m.SetShaderParameter("shade_map", Clip.ShadeMap);
        }
    }

    /// <summary>The far shade's square as last marched (tests): its corner (frame), cells' width and the sun's flat way.</summary>
    public (Vector2 Corner, float Cell, Vector2 Toward) ShadeSquare => (_shadeCorner, _shadeCell, _shadeToward);

    Vector4 _pushedSun, _pushedRect;
    float _pushedReach;
    Texture2Drd _pushedMap;
    int _pushedMaterials;

    /// <summary>How much of the sun the far shade lets reach a point (frame), as the march the graphics card makes has it: 1 lit, 0 shaded; ignoring the real-time shadows' reach.</summary>
    public float FarLit(Vector3 p)
    {
        if (SunToward == null || float.IsNaN(_shadeCorner.X) || !Clip.Complete) return 1f;
        Vector3 sun = SunToward();
        float flat = new Vector2(sun.X, sun.Z).Length();
        int i = (int)MathF.Floor((p.X - _shadeCorner.X) / _shadeCell), j = (int)MathF.Floor((p.Z - _shadeCorner.Y) / _shadeCell);
        if (i < 0 || j < 0 || i >= FarShade.N || j >= FarShade.N) return 1f;
        float rise = FarShade.Rise(Clip.Tops, _shadeCorner.X, _shadeCorner.Y, _shadeCell, _shadeToward, i, j);
        return FarShade.Lit(rise, flat > 1e-4f ? sun.Y / flat : 1000f);
    }
}
