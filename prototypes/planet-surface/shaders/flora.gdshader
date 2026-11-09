// Scatter: trees, shrubs, rocks, crystals. Vertex alpha marks the parts the instance tint colours
// (canopies, caps), and their vertex colour shades it: 0.8 is the tint as it is, more is sunlit, less
// is shade. Trunks and flowers keep their own colour. Tall plants sway in the weather's wind, more
// toward the top and more in gusty weather; rocks and crystals don't move.
//
// By distance (the far rings' scatter, TerrainStreamer): each plant has a near drawing (its own mesh)
// and, a large one, a far drawing too (its far mesh), and they dissolve one into the other over a
// band of distance through a fixed screen pattern, the near drawing keeping the pixels under its
// share and the far one the rest, so nothing swaps or doubles. Past the far reach a share of the
// large has a distant drawing (its far mesh a little bigger) that takes over the same way, out to the
// detail distance. Small growth dissolves away at its own reach. A thinning share of each (instance
// alpha past 1) goes sooner, so a wood thins into the distance rather than ending in a line. Each
// plant's reach is a little its own. The reaches come from TerrainStreamer (DetailReach), and past
// 2 km plants sink over the round world's horizon with the land. Keep in step with
// TerrainStreamer.FloraShows.
shader_type spatial;
render_mode cull_disabled, diffuse_burley;

#include "res://shaders/noise.gdshaderinc"

global uniform vec4 weather_wind;    // direction x, z, speed m/s, distance travelled (gust phase)
global uniform vec4 weather_ground;  // wetness, snow, dust, solar

// 0: large growth's near drawing; 1: its far; 3: its distant; 4: small growth; 2 (anything else
// that draws with this): whole at every distance.
instance uniform float flora_layer = 2.0;
// A chunk's new drawing dissolving in over its old one (TerrainStreamer.StepSwaps): the part of a
// second screen pattern, independent of the first, each draws, the new [0, t) and the old [t, 1).
// What both draw alike looks the same throughout; what is new or gone fades.
instance uniform vec2 flora_swap = vec2(0.0, 1.0);

// Reaches, m: large growth's near drawing, small growth, the far drawing, the distant; the bands
// they dissolve over; the far drawing's thinning share's reach and band.
uniform vec4 flora_reach = vec4(120.0, 200.0, 370.0, 1280.0);
uniform vec4 flora_bands = vec4(26.0, 60.0, 60.0, 192.0);
uniform vec2 flora_thin = vec2(200.0, 40.0);

varying vec3 tint;
varying float glow;
varying flat vec2 band;

float shown(float reach, float width, float away) {
	return 1.0 - smoothstep(reach - width, reach, away);
}

void vertex() {
	tint = INSTANCE_CUSTOM.rgb;
	bool thin = INSTANCE_CUSTOM.a >= 1.5;
	glow = INSTANCE_CUSTOM.a - (thin ? 2.0 : 0.0);
	band = vec2(0.0, 1.0);
	int layer = int(flora_layer + 0.5);
	vec3 root = MODEL_MATRIX[3].xyz;
	vec2 to_eye = root.xz - MAIN_CAM_INV_VIEW_MATRIX[3].xz;
	if (layer != 2) {
		float away = length(to_eye);
		float r = fract(sin(dot(root.xz, vec2(12.9898, 78.233))) * 43758.5453);
		float own = 0.9 + 0.1 * r;
		float near = shown(flora_reach.x * own, flora_bands.x, away);
		if (layer == 0) {
			band = vec2(0.0, near);
		} else if (layer == 4) {
			band = vec2(0.0, shown(flora_reach.y * (thin ? 0.6 : 1.0) * own, flora_bands.y * (thin ? 0.6 : 1.0), away));
		} else if (layer == 1) {
			float far = thin ? shown(flora_thin.x * own, flora_thin.y, away) : shown(flora_reach.z * own, flora_bands.z, away);
			band = vec2(near, max(far, near));
		} else {
			float far = shown(flora_reach.z * own, flora_bands.z, away);
			band = vec2(far, max(shown(flora_reach.w * (0.95 + 0.05 * r), flora_bands.w, away), far));
		}
	}
	if (band.y <= band.x) {
		VERTEX = vec3(0.0); // none of it shows: nothing to draw
	} else {
		vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
		float sway = clamp(VERTEX.y / 12.0, 0.0, 1.0);
		sway *= sway;
		float gust = sin(dot(world.xz, weather_wind.xy) * 0.08 - weather_wind.w * 0.08) * 0.5 + 0.5;
		float strength = min(weather_wind.z, 30.0) * 0.012 * (0.4 + 0.6 * gust);
		float flutter = sin(TIME * 2.3 + world.x * 0.3 + world.z * 0.2) * 0.04;
		vec3 push = vec3(weather_wind.x, 0.0, weather_wind.y) * (strength + flutter) * sway * max(COLOR.a, 0.8) * 3.0;
		push.y -= round_sink(to_eye); // over the round world's horizon, with the far land
		VERTEX += (inverse(MODEL_MATRIX) * vec4(push, 0.0)).xyz;
	}
}

void fragment() {
	if (band.x > 0.0 || band.y < 1.0) {
		float n = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
		if (n < band.x || n >= band.y) discard;
	}
	if (flora_swap.x > 0.0 || flora_swap.y < 1.0) {
		float m = fract(sin(dot(floor(FRAGCOORD.xy), vec2(12.9898, 78.233))) * 43758.5453);
		if (m < flora_swap.x || m >= flora_swap.y) discard;
	}
	vec3 own = srgb_to_linear(COLOR.rgb);
	vec3 c = mix(own, srgb_to_linear(min(tint * COLOR.rgb * 1.25, vec3(1.0))), COLOR.a);
	// A dusting of snow on top faces; wet bark goes dark.
	float snow = smoothstep(0.4, 0.8, NORMAL.y) * weather_ground.y;
	c = mix(c * (1.0 - weather_ground.x * 0.25), vec3(0.85, 0.88, 0.9), snow * 0.8);
	ALBEDO = c;
	ROUGHNESS = 0.85;
	// Light through the leaves: a canopy's underside glows a little by day, so a wood is shade, not black.
	vec3 through = c * COLOR.a * 0.16 * weather_ground.w;
	EMISSION = through + (glow > 0.0 ? srgb_to_linear(tint) * glow * 1.5 : vec3(0.0));
}
