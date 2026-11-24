// Seas and rivers: water, lava or an exotic liquid. The sea plane follows the camera and is
// masked by the world map's depth texture, so a pit dug below sea level inland stays dry (water
// is static, as in terrain-digging). Ripples move with the weather's wind and rain roughens them.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/noise.gdshaderinc"

global uniform vec2 planet_size;
global uniform vec4 weather_wind;
global uniform vec4 weather_ground;

uniform sampler2D sea_map : filter_linear, repeat_enable;  // r: metres below sea level by world-map cell (negative on land)
// The sea is where the map is deeper than this, as the terrain and physics have it (WorldMap.SeaFrom): out in under
// the shore, so the ground makes the water's edge. Rivers read it too: where one reaches the sea it is the sea.
uniform float sea_mask = -1.5;
uniform bool sea = false;
// Ground the map calls sea but that holds none (a place's pad, its rooms dug below sea level): the sea
// draws nothing there, or it shows through their openings. r 1 dry, over dry_area (x, z of its corner, size m; w 1 on).
uniform sampler2D dry_map : filter_nearest, repeat_disable;
uniform vec4 dry_area = vec4(0.0);
uniform vec3 shallow : source_color = vec3(0.1, 0.45, 0.5);
uniform vec3 deep : source_color = vec3(0.02, 0.12, 0.2);
uniform bool lava = false;
uniform float glow = 0.0;
uniform float daylight = 1.0;   // how lit the underside is from above
uniform float rain = 0.0;       // what's falling where the camera is
uniform vec3 rim = vec3(0.0, 0.0, -1.0);  // the sea ends with the land's own mesh: see past_rim

varying vec3 wpos;
varying float level;   // the surface's own height, before it sinks over the horizon

// Rain rings: a drop lands in each small cell of a grid at its own moment. Its ripple starts tight
// at the point of impact, runs out quickly while fading, and is a short packet of two or three thin
// rings (a gradient that swings both ways, so it tilts the surface softly and never raises an edge).
// Many small short-lived drops overlap and add. xy: the surface slope; z: a tiny splash at the landing.
vec3 rain_rings(vec2 p, float t, float amount) {
	const float CELL = 0.55;
	vec2 cell = floor(p / CELL);
	vec3 g = vec3(0.0);
	for (int j = -1; j <= 1; j++)
	for (int i = -1; i <= 1; i++) {
		vec2 c = cell + vec2(float(i), float(j));
		vec2 m = mod(c, 257.0);
		float h = fract(sin(dot(m, vec2(127.1, 311.7))) * 43758.5453);
		float h2 = fract(h * 91.7 + 0.37);
		if (h > 0.25 + 0.75 * amount) continue; // lighter rain, fewer drops
		vec2 centre = (c + 0.5 + 0.4 * vec2(sin(h * 57.0), cos(h * 91.0))) * CELL;
		float life = 0.55 + 0.5 * h2;           // a drop's ripple lasts about half a second
		float age = fract(t / life + h * 7.0);
		vec2 d = p - centre;
		float r = length(d);
		// A few drops are big and slow, most are small.
		float size = h2 > 0.9 ? 1.0 : 0.55;
		float R = size * 0.42 * sqrt(age) * (0.8 + 0.4 * h);
		float x = r - R;
		float w = 0.045 + 0.05 * age;           // the packet loosens as it spreads
		float f = 3.14159 / (w * 0.8);
		float env = exp(-x * x / (w * w * 2.0));
		float slope = env * (f * cos(f * x) - x / (w * w) * sin(f * x)) * w;
		float fade = (1.0 - age) * (1.0 - age) * smoothstep(0.0, 0.04, age) * size;
		g.xy += d / max(r, 0.001) * slope * fade * 0.7;
		// the splash: a bright speck at the landing, gone in a moment
		g.z += exp(-r * r / 0.0009) * (1.0 - smoothstep(0.0, 0.12, age)) * step(0.02, age);
	}
	return g;
}
void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	level = wpos.y;
	// The sea sinks over the horizon with the land (round_sink): the plane is subdivided for it.
	float sink = round_sink(wpos.xz - CAMERA_POSITION_WORLD.xz);
	VERTEX.y -= sink;
	wpos.y -= sink;
}

void fragment() {
	if (past_rim(wpos.xz, rim, FRAGCOORD.xy)) discard;
	float below = texture(sea_map, wpos.xz / planet_size).r;
	float depth;
	if (sea) {
		if (below < sea_mask) discard;
		if (dry_area.w > 0.5) {
			vec2 uv = (wpos.xz - dry_area.xy) / dry_area.z;
			if (uv.x >= 0.0 && uv.y >= 0.0 && uv.x < 1.0 && uv.y < 1.0 && texture(dry_map, uv).r > 0.5) discard;
		}
		depth = clamp(below / 120.0, 0.0, 1.0);
	} else {
		// A river that has come down to the sea where the sea is drawn is the sea: one surface, drawn once.
		if (below >= sea_mask && level < 0.05) discard;
		// Shallow, and over its last ten metres of fall to the sea (on the map) the colour of the sea at its shore,
		// so the two meet without a line.
		depth = 0.25 * (1.0 - smoothstep(sea_mask - 10.0, sea_mask, below));
	}
	vec2 p = wpos.xz;
	float t = TIME;
	if (lava) {
		float n = pnoise(vec3(p.x * 0.03 + t * 0.015, 0.0, p.y * 0.03), planet_size * 0.03) * 0.7
			+ pnoise(vec3(p.x * 0.09, t * 0.05, p.y * 0.09), planet_size * 0.09) * 0.3;
		float seam = 1.0 - smoothstep(0.0, 0.022, abs(n - 0.48));
		float pool = smoothstep(0.6, 0.625, n);
		float hot = max(seam, pool);
		vec3 c = mix(shallow * 0.22, deep, hot);
		ALBEDO = c * 0.4;
		EMISSION = c * glow * (0.12 + hot);
		ROUGHNESS = 0.8;
	} else if (!FRONT_FACING) {
		// The surface from below: a bright rippling window straight overhead (the sky through the
		// water, Snell's window) closing to the water's own dark mirror further off.
		vec3 view = normalize(wpos - CAMERA_POSITION_WORLD);
		float window = smoothstep(0.62, 0.86, view.y);
		float rip = pnoise(vec3(p.x * 0.7, t * 0.6, p.y * 0.7), planet_size * 0.7) * 0.6
			+ pnoise(vec3(p.x * 2.1, t * 1.1, p.y * 2.1), planet_size * 2.1) * 0.4;
		float shimmer = smoothstep(0.35, 0.72, rip);
		vec3 sky = mix(shallow, vec3(0.78, 0.92, 0.96), 0.6);
		ALBEDO = vec3(0.0);
		SPECULAR = 0.0;
		ROUGHNESS = 1.0;
		EMISSION = mix(mix(deep, shallow, 0.5 + 0.4 * shimmer) * 0.45, sky * (0.8 + 1.1 * shimmer), window) * daylight;
	} else {
		vec2 wind = weather_wind.xy * (0.3 + min(weather_wind.z, 25.0) * 0.04);
		vec2 g = vec2(0.0);
		float splash = 0.0;
		g += vec2(0.6, 0.8) * cos(dot(p, vec2(0.6, 0.8)) * 0.12 + t * 0.9) * 0.05;
		g += vec2(-0.9, 0.3) * cos(dot(p, vec2(-0.9, 0.3)) * 0.21 + t * 1.3) * 0.035;
		g += wind * cos(dot(p, normalize(weather_wind.xy + 0.001)) * 0.5 - t * 2.1) * 0.03;
		g += (vec2(pnoise(vec3(p.x * 4.0, t * 6.0, p.y * 4.0), planet_size * 4.0), pnoise(vec3(p.y * 4.0, t * 6.0 + 3.0, p.x * 4.0), planet_size.yx * 4.0)) - 0.5) * weather_ground.x * 0.15;
		if (rain > 0.01) {
			float near = 1.0 - smoothstep(10.0, 26.0, length(p - CAMERA_POSITION_WORLD.xz));
			if (near > 0.0) { vec3 rr = rain_rings(p, t, rain); g += rr.xy * near * 0.2; splash = rr.z * near; }
		}
		// Ripples a pixel can't hold beat against the screen as rings across the whole sea: they lie down with distance.
		g *= 1.0 - smoothstep(120.0, 700.0, length(wpos - CAMERA_POSITION_WORLD));
		vec3 n = normalize(vec3(-g.x, 1.0, -g.y));
		NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
		ALBEDO = mix(shallow, deep, smoothstep(0.0, 0.6, depth));
		ROUGHNESS = 0.06;
		METALLIC = 0.0;
		SPECULAR = 0.5;
		if (glow > 0.0) EMISSION = shallow * glow * 0.3;
		EMISSION += vec3(0.8, 0.88, 0.95) * splash * 0.5 * daylight;
	}
}

