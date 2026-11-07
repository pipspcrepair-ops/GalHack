// Seas and rivers: water, lava or an exotic liquid. The sea plane follows the camera and is
// masked by the world map's depth texture, so a pit dug below sea level inland stays dry (water
// is static, as in terrain-digging). Ripples move with the weather's wind and rain roughens them.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/noise.gdshaderinc"

global uniform vec2 planet_size;
global uniform vec4 weather_wind;
global uniform vec4 weather_ground;

uniform sampler2D sea_map : filter_linear, repeat_enable;  // r: depth below sea level (0 = land)
uniform bool sea = false;
uniform vec3 shallow : source_color = vec3(0.1, 0.45, 0.5);
uniform vec3 deep : source_color = vec3(0.02, 0.12, 0.2);
uniform bool lava = false;
uniform float glow = 0.0;
uniform float daylight = 1.0;   // how lit the underside is from above
uniform float rain = 0.0;       // what's falling where the camera is
uniform vec3 rim = vec3(0.0, 0.0, -1.0);  // the sea ends with the land's own mesh: see past_rim

varying vec3 wpos;

// Rain rings: a drop lands in each cell of a grid at its own moment, and a ring runs out from it.
// Only close to the camera (the fine rain roughening takes over further off).
vec2 rain_rings(vec2 p, float t, float amount) {
	const float CELL = 1.5;
	vec2 cell = floor(p / CELL);
	vec2 g = vec2(0.0);
	for (int j = -1; j <= 1; j++)
	for (int i = -1; i <= 1; i++) {
		vec2 c = cell + vec2(float(i), float(j));
		vec2 m = mod(c, 257.0);
		float h = fract(sin(dot(m, vec2(127.1, 311.7))) * 43758.5453);
		if (h > 0.25 + 0.75 * amount) continue; // lighter rain, fewer drops
		vec2 centre = (c + 0.5 + 0.38 * vec2(sin(h * 57.0), cos(h * 91.0))) * CELL;
		float age = fract(t * (0.8 + 0.5 * h) + h * 7.0);
		vec2 d = p - centre;
		float r = length(d);
		float off = (r - age * 1.5) * 10.0;
		g += d / max(r, 0.001) * exp(-off * off) * (1.0 - age) * (1.0 - age);
	}
	return g;
}

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (past_rim(wpos.xz, rim, FRAGCOORD.xy)) discard;
	float depth = 0.25; // rivers: shallow
	if (sea) {
		depth = texture(sea_map, wpos.xz / planet_size).r;
		if (depth < 0.004) discard;
	} else {
		// River quads run one grid square past the channel (vertex alpha 0 there). Where the banks
		// are too low to hide that edge it would show as a staircase of squares: fray it with noise
		// partway across instead, so the shore wanders like a real one. Still opaque: no sorting.
		float shore = COLOR.a + (pnoise(wpos * 0.35, planet_size * 0.35) - 0.5) * 0.7;
		if (shore < 0.45) discard;
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
		g += vec2(0.6, 0.8) * cos(dot(p, vec2(0.6, 0.8)) * 0.12 + t * 0.9) * 0.05;
		g += vec2(-0.9, 0.3) * cos(dot(p, vec2(-0.9, 0.3)) * 0.21 + t * 1.3) * 0.035;
		g += wind * cos(dot(p, normalize(weather_wind.xy + 0.001)) * 0.5 - t * 2.1) * 0.03;
		g += (vec2(pnoise(vec3(p.x * 4.0, t * 6.0, p.y * 4.0), planet_size * 4.0), pnoise(vec3(p.y * 4.0, t * 6.0 + 3.0, p.x * 4.0), planet_size.yx * 4.0)) - 0.5) * weather_ground.x * 0.15;
		if (rain > 0.01) {
			float near = 1.0 - smoothstep(14.0, 34.0, length(p - CAMERA_POSITION_WORLD.xz));
			if (near > 0.0) g += rain_rings(p, t, rain) * near * 0.18;
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
	}
}
