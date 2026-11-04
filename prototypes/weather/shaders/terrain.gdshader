// The plains: vertex-coloured ground that darkens and shines when wet, collects snow on flat
// ground, takes a coat of dust after a storm and lies under drifting cloud shadows.
shader_type spatial;
render_mode diffuse_lambert, specular_schlick_ggx;

#include "res://shaders/weather.gdshaderinc"

uniform sampler2D cloud_noise : filter_linear_mipmap, repeat_enable;
uniform sampler2D detail_noise : filter_linear_mipmap, repeat_enable;
uniform vec3 to_sun = vec3(-0.8, 0.45, -0.3);
uniform vec3 snow_color : source_color = vec3(0.92, 0.95, 1.0);
uniform vec3 dust_color : source_color = vec3(0.78, 0.5, 0.26);
uniform vec3 tint : source_color = vec3(1.0);   // for meshes without vertex colours (the landing pad)

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	float detail = texture(detail_noise, world_pos.xz * 0.045).r;
	float fine = texture(detail_noise, world_pos.xz * 0.31).r;
	vec3 base = COLOR.rgb * tint * (0.82 + 0.36 * detail) * (0.92 + 0.16 * fine);

	float wet = weather_ground.x;
	base *= mix(1.0, 0.55, wet);
	float rough = mix(0.95, 0.5, wet * smoothstep(0.35, 0.75, detail));

	float dust = weather_ground.z * smoothstep(0.2, 0.8, detail + 0.3);
	base = mix(base, dust_color, dust * 0.7);

	float flat_ground = smoothstep(0.65, 0.9, world_normal.y);
	float snow = smoothstep(0.0, 0.35, weather_ground.y * 1.3 - (1.0 - detail) * 0.45) * flat_ground;
	base = mix(base, snow_color, snow);
	rough = mix(rough, 0.6, snow);

	base *= 1.0 - cloud_shadow(cloud_noise, world_pos, normalize(to_sun)) * 0.5;
	ALBEDO = base;
	ROUGHNESS = rough;
	SPECULAR = mix(0.3, 0.45, wet);
}
