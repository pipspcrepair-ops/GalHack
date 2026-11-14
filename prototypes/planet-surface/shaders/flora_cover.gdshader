// Ground cover: grass, flowers, low scrub. One multimesh per species following the player. Each
// tuft has its own reach (instance alpha) and shrinks into the ground as it comes to it, so cover is
// thick at your feet and thins with distance without popping. Vertex alpha 1 takes the tuft's tint;
// the rest (flowers, stalks) keep their own colour.
shader_type spatial;
render_mode cull_disabled, diffuse_burley;

#include "res://shaders/noise.gdshaderinc"

global uniform vec4 weather_wind;    // direction x, z, speed m/s, distance travelled (gust phase)
global uniform vec4 weather_ground;  // wetness, snow, dust, solar

varying vec3 tint;
varying vec3 wpos;

// Keep in step with CoverBuilder.ReachOf.
const float NEAR_REACH = 30.0;
const float FAR_REACH = 112.0;

// A cell that landed in sight carries when (instance alpha's whole part, tenths of a second round
// a 400 s clock; TerrainStreamer.CoverClock) and grows in over GROW seconds from then.
const float GROW = 0.6;

// What keeps its own colour (flowers, stalks, twigs) is a few centimetres across: about this, m.
const float SMALL = 0.08;

void vertex() {
	tint = INSTANCE_CUSTOM.rgb;
	float born = floor(INSTANCE_CUSTOM.a);
	float k = 1.0 - (INSTANCE_CUSTOM.a - born);
	float reach = NEAR_REACH + (FAR_REACH - NEAR_REACH) * k * k * k * k;
	vec3 root = MODEL_MATRIX[3].xyz;
	// From the eye itself, so cover far below a low flight shrinks away too.
	float away = distance(root, INV_VIEW_MATRIX[3].xyz);
	float keep = 1.0 - smoothstep(reach * 0.72, reach, away);
	if (born > 0.5) keep *= clamp(mod(TIME * 10.0 - born, 4000.0) / (GROW * 10.0), 0.0, 1.0);
	float high = max(VERTEX.y, 0.0);
	VERTEX *= keep;
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float gust = sin(dot(world.xz, weather_wind.xy) * 0.1 - weather_wind.w * 0.1) * 0.5 + 0.5;
	float strength = min(weather_wind.z, 30.0) * 0.016 * (0.4 + 0.6 * gust);
	float flutter = sin(TIME * 2.7 + world.x * 0.7 + world.z * 0.5) * 0.05;
	vec3 push = vec3(weather_wind.x, 0.0, weather_wind.y) * (strength + flutter) * high * high;
	VERTEX += (inverse(MODEL_MATRIX) * vec4(push, 0.0)).xyz;
	wpos = world + push;
	// Lit nearly as the ground is, so a drift reads as one soft mass, not a thousand facets.
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), 0.82));
}

void fragment() {
	// Both faces of a blade are lit as its top: the backs mustn't read as dark spikes.
	NORMAL = FRONT_FACING ? NORMAL : -NORMAL;
	vec3 own = srgb_to_linear(COLOR.rgb);
	// A flower a few metres off is a pixel or two, and one brighter than the leaves round it is a
	// single dot that comes and goes as the view moves (by moonlight a pale one is the brightest
	// thing in the grass: the pinholes). Under about two pixels across (its size against the
	// pixel's area on its face) it gives up its brightness over the tuft's top leaves, not its hue.
	float pixel = length(cross(dFdx(wpos), dFdy(wpos)));
	float shown = smoothstep(1.0, 4.0, SMALL * SMALL / max(pixel, 1e-9));
	const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);
	float over = dot(own, LUMA) / max(dot(srgb_to_linear(min(tint * 1.22, vec3(1.0))), LUMA), 1e-4);
	own /= mix(max(over, 1.0), 1.0, shown);
	vec3 c = mix(own, srgb_to_linear(min(tint * COLOR.rgb * 1.25, vec3(1.0))), COLOR.a);
	float snow = weather_ground.y * smoothstep(0.3, 0.8, NORMAL.y);
	c = mix(c * (1.0 - weather_ground.x * 0.2), vec3(0.85, 0.88, 0.9), snow * 0.7);
	ALBEDO = c;
	EMISSION = c * COLOR.a * 0.1 * weather_ground.w; // what comes down through the leaves: low growth in shade isn't black
	ROUGHNESS = 0.9;
}
