using Godot;

namespace GalHack.Stars;

/// <summary>
/// The sky's stars and nebulae, the one piece of shader every sky uses: space's own sky takes this
/// text directly, and planet-surface's sky.gdshader, a file, includes it from <see cref="Path"/>,
/// which <see cref="Register"/> serves from here. Nothing in it is invented: every point it draws
/// is a catalogue star listed for that part of the sky, the rest is the catalogue's own light, and
/// the central black hole is the catalogue's (<see cref="Galaxy.Hole"/>), where it stands from here.
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

        // The heart: the central black hole where it really is (Galaxy.Hole), from here: its shadow
        // and its accretion disc, the disc's far side seen over and under the shadow, its light bent
        // round it. A point in the bulge from far off, a disc with a dark centre from deep in the core.
        uniform vec3 galaxy_hole = vec3(0.0, 0.0, -1.0);        // from here to the hole, ly, the catalogue's frame
        uniform vec3 galaxy_hole_axis = vec3(0.0, 1.0, 0.0);    // the disc's axis
        uniform vec3 galaxy_hole_zero = vec3(1.0, 0.0, 0.0);    // in the disc's plane: its pattern is measured round from here
        uniform vec4 galaxy_hole_size = vec4(0.0);              // the shadow's radius, the disc's inner and outer radius (ly); the share of the bulge's light toward it from this side of it

        float galaxy_hash(vec2 p) {
            p = fract(p * vec2(0.1031, 0.1030));
            p += dot(p, p.yx + 33.33);
            return fract((p.x + p.y) * p.x);
        }

        // Value noise wrapping every `period` along x: round the disc, no seam.
        float galaxy_wrap_noise(vec2 p, float period) {
            vec2 i = floor(p), f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            float x0 = mod(i.x, period), x1 = mod(i.x + 1.0, period);
            return mix(mix(galaxy_hash(vec2(x0, i.y)), galaxy_hash(vec2(x1, i.y)), f.x),
                       mix(galaxy_hash(vec2(x0, i.y + 1.0)), galaxy_hash(vec2(x1, i.y + 1.0)), f.x), f.y);
        }

        // Hot gas: white-blue at its hottest, through white and gold to a deep red.
        vec3 galaxy_heat(float t) {
            vec3 c = mix(vec3(0.5, 0.1, 0.03), vec3(1.0, 0.42, 0.12), smoothstep(0.12, 0.38, t));
            c = mix(c, vec3(1.0, 0.78, 0.5), smoothstep(0.38, 0.68, t));
            c = mix(c, vec3(1.0, 0.96, 0.9), smoothstep(0.68, 1.0, t));
            return mix(c, vec3(0.72, 0.84, 1.0), smoothstep(1.0, 1.6, t));
        }

        // The disc's light at a point of it (from the hole, ly) seen along `ray`, and how much it hides.
        // `px` is a pixel's width there, ly; `detail` how much of its fine pattern a pixel can show.
        vec4 galaxy_disc(vec3 x, vec3 ray, float px, float detail) {
            vec4 s = galaxy_hole_size;
            float rho = length(x);
            float soft = max(px, s.y * 0.04);
            float body = smoothstep(s.y - soft, s.y + soft, rho) * (1.0 - smoothstep(s.z * 0.72, s.z + soft, rho));
            if (body <= 0.0) return vec4(0.0);
            float u = s.y / rho;   // 1 at the inner edge
            // Its orbits: fastest within, and the side coming toward you brighter and bluer.
            vec3 along = normalize(cross(galaxy_hole_axis, x));
            float beta = 0.42 * sqrt(u);
            float boost = sqrt(1.0 - beta * beta) / (1.0 - beta * dot(along, -ray));
            float heat = pow(u, 0.75) * boost;
            // Streaks wound round with the orbits, clumps and lanes across them.
            float turn = atan(dot(x, cross(galaxy_hole_axis, galaxy_hole_zero)), dot(x, galaxy_hole_zero)) / 6.2831853 + 0.5;
            float lr = log(rho / s.y);
            float streak = galaxy_wrap_noise(vec2(turn * 48.0 + lr * 9.0, lr * 22.0), 48.0);
            float clump = galaxy_wrap_noise(vec2(turn * 12.0 - lr * 4.0, lr * 6.0 + 7.0), 12.0);
            float pattern = mix(1.0, (0.45 + 1.1 * streak) * (0.6 + 0.8 * clump), detail);
            float bright = 4.5 * pow(u, 1.25) * boost * boost * boost * pattern * body;
            float hides = clamp(body * (0.35 + 0.65 * pow(u, 0.5)) * (0.7 + 0.3 * pattern), 0.0, 0.97);
            return vec4(galaxy_heat(heat) * bright, hides);
        }

        // The hole along `d`: its light in front of the sky, and in `a` how much of the sky behind it hides.
        // `touched`: whether `d` passes near enough to it to be its at all.
        vec4 galaxy_heart(vec3 d, out bool touched) {
            touched = false;
            vec4 s = galaxy_hole_size;
            float dist = length(galaxy_hole);
            if (s.z <= 0.0 || dist <= s.z) return vec4(0.0);
            float c = dot(d, galaxy_hole) / dist;
            if (c <= 0.0) return vec4(0.0);
            float px = galaxy_pixel * dist;          // a pixel's width at the hole, ly
            float wide = 2.5 * s.z;                  // the disc's light bent round it, out to here
            float reach = max(wide, s.z + 6.0 * px);
            vec3 closest = d * (dist * c) - galaxy_hole; // the ray's nearest approach, from the hole
            float b = length(closest);
            if (b > reach) return vec4(0.0);
            touched = true;
            float shown = s.z / px;                 // the disc's radius in pixels
            // Too small to make out: a point of the disc's light, as a star is drawn.
            float tiny = 1.0 - smoothstep(1.5, 3.5, shown);
            vec3 light = vec3(0.0);
            float hidden = 0.0;
            if (tiny > 0.0) {
                float sigma = max(px, s.z * 0.6);
                light += galaxy_heat(0.75) * (2.2 * s.z * s.z / (sigma * sigma)) * exp(-0.5 * b * b / (sigma * sigma)) * tiny;
            }
            if (tiny < 1.0) {
                float detail = smoothstep(4.0, 40.0, shown);
                // Bent toward the hole by its pull, all of it at its nearest approach: more than
                // the weak field's 2 r / b close in, so the disc's far side shows over the shadow.
                float horizon = s.x / 2.6;
                float bend = 2.0 * horizon / max(b, 1e-4) * (1.0 + 1.5 * pow(s.x / max(b, s.x), 2.0));
                bend *= 1.0 - smoothstep(wide * 0.6, wide, b);
                bend = min(bend, 2.6);
                vec3 inward = -closest / max(b, 1e-6);
                vec3 out_dir = normalize(d * cos(bend) + inward * sin(bend));
                float shadow = 1.0 - smoothstep(s.x - px * 0.7, s.x + px * 0.7, b);
                vec3 n = galaxy_hole_axis;
                // In along the first leg, to the nearest approach.
                float dn = dot(d, n);
                if (abs(dn) > 1e-5) {
                    float t = dot(galaxy_hole, n) / dn;
                    if (t > 0.0 && t < dist * c) {
                        vec4 e = galaxy_disc(d * t - galaxy_hole, d, galaxy_pixel * t, detail);
                        light += e.rgb * (1.0 - hidden);
                        hidden += e.a * (1.0 - hidden);
                    }
                }
                // Out along the second, bent: the far side, unless the hole took it.
                float on = dot(out_dir, n);
                if (shadow < 1.0 && abs(on) > 1e-5) {
                    float t = -dot(closest, n) / on;
                    if (t > 0.0) {
                        vec4 e = galaxy_disc(closest + out_dir * t, out_dir, galaxy_pixel * (dist + t), detail) * (1.0 - shadow);
                        light += e.rgb * (1.0 - hidden);
                        hidden += e.a * (1.0 - hidden);
                    }
                }
                // The thin ring of light that has circled the hole, at the shadow's edge.
                float ring = exp(-pow((b - s.x * 1.04) / max(px * 0.8, s.x * 0.035), 2.0));
                light += galaxy_heat(0.9) * ring * 1.6 * (1.0 - hidden);
                hidden += shadow * (1.0 - hidden);
                light *= 1.0 - tiny;
                hidden *= 1.0 - tiny;
            }
            return vec4(light, hidden);
        }

        // The system's own star: its disc as big as it really looks (StarSystem.SunAngular, its real
        // radius over the world's distance from it: the one size every sky draws it at, on the
        // ground, through the climb and in orbit), white-hot within and the star's own colour at
        // its rim, churning slowly, with a corona round it as big and fierce as the star is close.
        // The air's glow and the screen's bloom go on top: they never change the disc.
        uniform float sun_radius = 0.0;                          // radians
        uniform vec3 sun_disc_light = vec3(30.0, 27.6, 24.0);    // the disc's light at its centre with no air in the way
        uniform vec3 sun_rim = vec3(1.0, 0.6, 0.28);             // the colour at its limb, by star type
        uniform float sun_heat = 0.0;                            // 0 a far pale sun (a thin halo), 1 a scorching one (a wide corona)
        uniform float sun_time = 0.0;                            // seconds, for its slow churn
        uniform bool sun_measure = false;                        // a flat disc and no corona: what the sky test measures

        float sun_disc(float facing) {
            return sun_radius > 0.0 ? smoothstep(cos(sun_radius * 1.15), cos(sun_radius), facing) : 0.0;
        }

        // The sun along `d`, as a share of its light at the centre of its disc: the disc and its corona.
        vec3 sun_body(vec3 d, vec3 to_sun) {
            if (sun_radius <= 0.0) return vec3(0.0);
            float c = dot(d, to_sun);
            float disc = sun_disc(c);
            if (sun_measure) return vec3(disc);
            float rho = atan(length(cross(d, to_sun)), c) / sun_radius;   // 0 at its centre, 1 at its limb
            vec3 light = vec3(0.0);
            if (disc > 0.0) {
                // Granules welling up and sinking back, slowly.
                vec3 helper = abs(to_sun.y) > 0.9 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
                vec3 e1 = normalize(cross(helper, to_sun)), e2 = cross(to_sun, e1);
                vec2 uv = vec2(dot(d, e1), dot(d, e2)) / sun_radius;
                float churn = galaxy_wrap_noise(uv * 9.0 + vec2(sun_time * 0.021, -sun_time * 0.013) + 40.0, 4096.0) * 0.6
                            + galaxy_wrap_noise(uv * 23.0 - vec2(sun_time * 0.034, sun_time * 0.027) + 90.0, 4096.0) * 0.4;
                float limb = smoothstep(0.25, 0.82, rho);
                light = mix(vec3(1.0), sun_rim * 0.075, limb) * (0.86 + 0.28 * churn) * disc;
            }
            float past = max(rho - 1.0, 0.0);
            light += sun_rim * mix(0.035, 0.11, sun_heat) * exp(-past / mix(0.3, 1.1, sun_heat)) * (1.0 - disc);
            return light;
        }

        vec3 sun_in_space(vec3 d, vec3 to_sun) {
            float s = dot(d, to_sun);
            return sun_disc_light * sun_body(d, to_sun) + (sun_measure ? vec3(0.0) : vec3(1.0, 0.92, 0.8) * pow(max(s, 0.0), 40.0) * 0.25);
        }

        // Everything past the system in a catalogue direction: the stars' own soft light and the
        // nebulae always, and the hole; with points, the stars themselves (not for a sky's small
        // reflection cube). The hole's shadow and disc hide what is behind them: only the bulge's
        // light from this side of the hole shows in its shadow.
        vec3 galaxy_sky(vec3 d, bool points) {
            if (!galaxy_on) return vec3(0.0);
            bool touched;
            vec4 heart = galaxy_heart(d, touched);
            vec3 glow = textureLod(galaxy_glow, d, 0.0).rgb;
            vec3 stars = points ? galaxy_points(d) : vec3(0.0);
            if (!touched) return glow + stars;
            float front = galaxy_hole_size.w;
            return glow * front + (glow * (1.0 - front) + stars) * (1.0 - heart.a) + heart.rgb;
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
