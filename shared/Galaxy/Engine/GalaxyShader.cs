using Godot;

namespace GalHack.Stars;

/// <summary>
/// The sky's stars and nebulae, the one piece of shader every sky uses: space's own sky takes this
/// text directly, and planet-surface's sky.gdshader, a file, includes it from <see cref="Path"/>,
/// which <see cref="Register"/> serves from here. Nothing in it is invented: every point it draws
/// is a catalogue star listed for that part of the sky, and the rest is the catalogue's own light.
/// The cube layout is <see cref="SkyView.Face"/>, the brightness scale <see cref="SkyView.Pack"/>.
/// </summary>
public static class GalaxyShader
{
    public const string Path = "res://galaxy_sky.gdshaderinc";

    public const string Code = """
        uniform sampler2D galaxy_cells : filter_nearest, repeat_disable;
        uniform sampler2D galaxy_stars : filter_nearest, repeat_disable;
        uniform samplerCube galaxy_glow : filter_linear;
        uniform bool galaxy_on = false;
        uniform float galaxy_pixel = 0.0017;   // the angle of a pixel, radians: a star is a point at any resolution
        uniform float galaxy_gain = 2.2;
        const int GALAXY_GRID = 192;

        vec3 galaxy_points(vec3 d) {
            vec3 a = abs(d);
            int face;
            vec2 st;
            if (a.x >= a.y && a.x >= a.z) {
                face = d.x > 0.0 ? 0 : 1;
                st = vec2(d.x > 0.0 ? -d.z : d.z, -d.y) / a.x;
            } else if (a.y >= a.z) {
                face = d.y > 0.0 ? 2 : 3;
                st = vec2(d.x, d.y > 0.0 ? d.z : -d.z) / a.y;
            } else {
                face = d.z > 0.0 ? 4 : 5;
                st = vec2(d.z > 0.0 ? d.x : -d.x, -d.y) / a.z;
            }
            ivec2 cell = clamp(ivec2((st * 0.5 + 0.5) * float(GALAXY_GRID)), ivec2(0), ivec2(GALAXY_GRID - 1));
            int code = int(texelFetch(galaxy_cells, ivec2(cell.x + face * GALAXY_GRID, cell.y), 0).r);
            int count = code & 15, first = code >> 4;
            vec3 col = vec3(0.0);
            for (int i = 0; i < count; i++) {
                int k = first + i;
                vec4 s = texelFetch(galaxy_stars, ivec2(k & 1023, k >> 10), 0);
                vec3 off = s.xyz - d;
                float r2 = dot(off, off);
                int p = int(s.w);
                float shown = 0.0015 * exp(float(p & 255) * 0.03967);   // SkyView.Pack's scale: ln(40 / 0.0015) / 255
                float wide = galaxy_pixel * (0.7 + 0.8 * min(sqrt(shown) * 0.5, 1.0));
                float fall = exp(-0.5 * r2 / (wide * wide));
                if (fall < 0.004) continue;
                vec3 tint = vec3(float((p >> 19) & 31) / 31.0, float((p >> 13) & 63) / 63.0, float((p >> 8) & 31) / 31.0);
                col += tint * shown * fall;
            }
            return col * galaxy_gain;
        }

        // Everything past the system in a catalogue direction: the stars' own soft light and the
        // nebulae always; with points, the stars themselves (not for a sky's small reflection cube).
        vec3 galaxy_sky(vec3 d, bool points) {
            if (!galaxy_on) return vec3(0.0);
            vec3 col = textureLod(galaxy_glow, d, 0.0).rgb;
            if (points) col += galaxy_points(d);
            return col;
        }
        """;

    static ShaderInclude _include;

    /// <summary>Serves the include: call before loading a shader that includes it.</summary>
    public static void Register()
    {
        if (_include != null) return;
        _include = new ShaderInclude { Code = Code };
        _include.TakeOverPath(Path);
    }
}
