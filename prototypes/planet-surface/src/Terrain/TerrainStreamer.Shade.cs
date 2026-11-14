using System;
using System.Threading.Tasks;
using Godot;

namespace PlanetSurface.Terrain;

/// <summary>
/// The far land's shade past the sun's real-time shadows (<see cref="FarShade"/>, shade.gdshaderinc):
/// worked out on a worker round the focus for the sun's way, again when the sun has turned a degree or
/// the focus has gone an eighth of the square, and blended in over a moment.
/// </summary>
public partial class TerrainStreamer
{
    /// <summary>Toward the sun (unit, frame), the game's sky sets it; and how far the sun's real-time shadows reach, m (the shadow distance setting).</summary>
    public Func<Vector3> SunToward;
    public float ShadowReach = 320f;

    /// <summary>The square in use and the last one, and the one being worked out.</summary>
    public FarShade Shade { get; private set; }
    FarShade _shadeWas;
    Task<FarShade> _shading;
    float _shadeBlend = 1f;
    ImageTexture _shadeTex, _shadeWasTex;

    /// <summary>How long a new square takes to blend in, s; how far the sun turns before a new one, radians.</summary>
    const float ShadeBlendSeconds = 1.5f, ShadeTurn = 0.017f;

    void UpdateShade()
    {
        if (SunToward == null || World == null) return;
        Vector3 sun = SunToward();
        var flat = new Vector2(sun.X, sun.Z);
        float rise = flat.Length() > 1e-4f ? sun.Y / flat.Length() : 1000f;
        if (flat.LengthSquared() > 1e-8f) flat = flat.Normalized();
        float half = DetailReach.Distance + 2000f;
        if (_shading != null && _shading.IsCompleted)
        {
            FarShade made = _shading.Result;
            _shading = null;
            _shadeWas = Shade;
            _shadeWasTex = _shadeTex;
            Shade = made;
            _shadeTex = made.Texture();
            _shadeBlend = _shadeWas == null ? 1f : 0f;
        }
        bool turned = Shade == null || Shade.Toward.Dot(flat) < MathF.Cos(ShadeTurn);
        bool moved = Shade == null || MathF.Abs(Focus.X - (Shade.X0 + Shade.Side * 0.5f)) > Shade.Side / 8f || MathF.Abs(Focus.Z - (Shade.Z0 + Shade.Side * 0.5f)) > Shade.Side / 8f
            || MathF.Abs(Shade.Side * 0.5f - half) > 1f;
        if (_shading == null && (turned || moved) && sun.Y > -0.05f)
        {
            var world = World;
            float cx = Focus.X, cz = Focus.Z;
            _shading = Task.Run(() => FarShade.Build(world, cx, cz, half, flat));
        }
        _shadeBlend = MathF.Min(1f, _shadeBlend + (float)GetProcessDeltaTime() / ShadeBlendSeconds);
        // A material parameter set costs the render thread, so the materials take new values only when something changed:
        // the sun a tenth of a degree on (its shade is built to a degree), a blend, a new square, a material made since.
        var now = new Vector4(sun.X, sun.Y, sun.Z, rise);
        int live = (VoxelMaterial != null ? 1 : 0) + (FarMaterial != null ? 1 : 0) + (_horizonMaterial != null ? 1 : 0) + (_floraMaterial != null ? 1 : 0) + (ClipMaterial != null ? 1 : 0);
        bool squares = live != _pushedMaterials || _pushedShade != Shade || _pushedWas != _shadeWas;
        if (!squares && now.DistanceSquaredTo(_pushedSun) < 4e-6f && _pushedReach == ShadowReach && _pushedBlend == _shadeBlend) return;
        (_pushedSun, _pushedReach, _pushedBlend, _pushedShade, _pushedWas, _pushedMaterials) = (now, ShadowReach, _shadeBlend, Shade, _shadeWas, live);
        foreach (ShaderMaterial m in new[] { VoxelMaterial, FarMaterial, _horizonMaterial, _floraMaterial, ClipMaterial == FarMaterial ? null : ClipMaterial })
        {
            if (m == null) continue;
            m.SetShaderParameter("shade_sun", now);
            m.SetShaderParameter("shade_reach", ShadowReach);
            m.SetShaderParameter("shade_blend", _shadeBlend);
            if (!squares) continue;
            m.SetShaderParameter("shade_join", new Vector2(FarShade.JoinFrom, FarShade.JoinFull));
            if (Shade != null)
            {
                m.SetShaderParameter("shade_map", _shadeTex);
                m.SetShaderParameter("shade_rect", new Vector4(Shade.X0, Shade.Z0, Shade.Side, 1f));
            }
            if (_shadeWas != null)
            {
                m.SetShaderParameter("shade_was", _shadeWasTex);
                m.SetShaderParameter("shade_was_rect", new Vector4(_shadeWas.X0, _shadeWas.Z0, _shadeWas.Side, 1f));
            }
        }
    }

    Vector4 _pushedSun;
    float _pushedReach, _pushedBlend = -1f;
    FarShade _pushedShade, _pushedWas;
    int _pushedMaterials;
    /// <summary>How much of the sun the far shade lets reach a point (frame), for a test: 1 lit, 0 shaded; ignoring the real-time shadows' reach.</summary>
    public float FarLit(Vector3 p)
    {
        if (Shade == null || SunToward == null) return 1f;
        Vector3 sun = SunToward();
        float flat = new Vector2(sun.X, sun.Z).Length();
        return Shade.Lit(p.X, p.Z, flat > 1e-4f ? sun.Y / flat : 1000f);
    }
}
