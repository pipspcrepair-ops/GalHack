// Grass and trees that sway with the wind. UV.y runs from 0 at the root to 1 at the tip, so
// the bend grows toward the top; gust fronts sweep across the plain at the wind's own speed,
// and a tornado drags everything near it round and inward.
shader_type spatial;
render_mode world_vertex_coords, cull_disabled, diffuse_lambert;

#include "res://shaders/weather.gdshaderinc"

uniform sampler2D cloud_noise : filter_linear_mipmap, repeat_enable;
uniform vec3 to_sun = vec3(-0.8, 0.45, -0.3);
uniform float stiffness = 1.0;     // trees are stiffer than grass
uniform float flutter = 1.0;       // fast leaf/blade shiver
uniform float height_scale = 1.0;  // metres from root to tip, for bend size
uniform vec3 snow_color : source_color = vec3(0.92, 0.95, 1.0);

varying vec3 world_pos;
varying float tip;

void vertex() {
	tip = UV.y;
	vec3 root = MODEL_MATRIX[3].xyz;
	float speed = weather_wind.z;
	float gust = gust_at(root.xz);
	// Bend grows with the square of wind speed, saturating so a hurricane lays grass flat but no further.
	float push = (1.0 - exp(-speed * speed * gust * 0.0016 / stiffness)) * 1.15;
	float wobble = sin(TIME * (2.0 + speed * 0.25) + root.x * 0.7 + root.z * 0.53) * (0.08 + 0.02 * speed) * flutter / stiffness;
	vec2 bend = weather_wind.xy * push + vec2(weather_wind.y, -weather_wind.x) * wobble;

	// Near a tornado: pulled in and round.
	vec2 to_core = weather_tornado.xy - root.xz;
	float d = length(to_core);
	if (weather_tornado.z > 0.01 && d < 90.0) {
		vec2 inward = to_core / max(d, 0.1);
		vec2 around = vec2(-inward.y, inward.x);
		float near = (1.0 - smoothstep(10.0, 90.0, d)) * weather_tornado.z;
		bend = mix(bend, (inward * 0.6 + around) * 1.1, near);
	}

	float k = tip * tip * height_scale;
	VERTEX.xz += bend * k;
	VERTEX.y -= length(bend) * k * 0.35; // bending shortens it, so it doesn't look stretched
	world_pos = VERTEX;
}

void fragment() {
	if (!FRONT_FACING) NORMAL = -NORMAL; // a blade is lit the same from both sides
	vec3 base = COLOR.rgb;
	float snow = smoothstep(0.1, 0.6, weather_ground.y) * smoothstep(0.2, 1.0, tip);
	base = mix(base, snow_color, snow * 0.85);
	base *= mix(1.0, 0.7, weather_ground.x);
	base *= 1.0 - cloud_shadow(cloud_noise, world_pos, normalize(to_sun)) * 0.5;
	ALBEDO = base;
	ROUGHNESS = 0.85;
	BACKLIGHT = vec3(0.25) * base;
}
