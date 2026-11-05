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

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
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
	} else {
		vec2 wind = weather_wind.xy * (0.3 + min(weather_wind.z, 25.0) * 0.04);
		vec2 g = vec2(0.0);
		g += vec2(0.6, 0.8) * cos(dot(p, vec2(0.6, 0.8)) * 0.12 + t * 0.9) * 0.05;
		g += vec2(-0.9, 0.3) * cos(dot(p, vec2(-0.9, 0.3)) * 0.21 + t * 1.3) * 0.035;
		g += wind * cos(dot(p, normalize(weather_wind.xy + 0.001)) * 0.5 - t * 2.1) * 0.03;
		g += (vec2(pnoise(vec3(p.x * 4.0, t * 6.0, p.y * 4.0), planet_size * 4.0), pnoise(vec3(p.y * 4.0, t * 6.0 + 3.0, p.x * 4.0), planet_size.yx * 4.0)) - 0.5) * weather_ground.x * 0.15;
		vec3 n = normalize(vec3(-g.x, 1.0, -g.y));
		NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
		ALBEDO = mix(shallow, deep, smoothstep(0.0, 0.6, depth));
		ROUGHNESS = 0.06;
		METALLIC = 0.0;
		SPECULAR = 0.5;
		if (glow > 0.0) EMISSION = shallow * glow * 0.3;
	}
}
