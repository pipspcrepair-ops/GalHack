// Scatter: trees, shrubs, rocks, crystals. Vertex alpha marks the parts the instance tint colours
// (canopies, caps), and their vertex colour shades it: 0.8 is the tint as it is, more is sunlit, less
// is shade. Trunks and flowers keep their own colour. Tall plants sway in the weather's wind, more
// toward the top and more in gusty weather; rocks and crystals don't move.
shader_type spatial;
render_mode cull_disabled, diffuse_burley;

#include "res://shaders/noise.gdshaderinc"

global uniform vec4 weather_wind;    // direction x, z, speed m/s, distance travelled (gust phase)
global uniform vec4 weather_ground;  // wetness, snow, dust, solar

varying vec3 tint;
varying float glow;

void vertex() {
	tint = INSTANCE_CUSTOM.rgb;
	glow = INSTANCE_CUSTOM.a;
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float sway = clamp(VERTEX.y / 12.0, 0.0, 1.0);
	sway *= sway;
	float gust = sin(dot(world.xz, weather_wind.xy) * 0.08 - weather_wind.w * 0.08) * 0.5 + 0.5;
	float strength = min(weather_wind.z, 30.0) * 0.012 * (0.4 + 0.6 * gust);
	float flutter = sin(TIME * 2.3 + world.x * 0.3 + world.z * 0.2) * 0.04;
	vec3 push = vec3(weather_wind.x, 0.0, weather_wind.y) * (strength + flutter) * sway * max(COLOR.a, 0.8) * 3.0;
	VERTEX += (inverse(MODEL_MATRIX) * vec4(push, 0.0)).xyz;
}

void fragment() {
	vec3 own = srgb_to_linear(COLOR.rgb);
	vec3 c = mix(own, srgb_to_linear(min(tint * COLOR.rgb * 1.25, vec3(1.0))), COLOR.a);
	// A dusting of snow on top faces; wet bark goes dark.
	float snow = smoothstep(0.4, 0.8, NORMAL.y) * weather_ground.y;
	c = mix(c * (1.0 - weather_ground.x * 0.25), vec3(0.85, 0.88, 0.9), snow * 0.8);
	ALBEDO = c;
	ROUGHNESS = 0.85;
	if (glow > 0.0) EMISSION = srgb_to_linear(tint) * glow * 1.5;
}
