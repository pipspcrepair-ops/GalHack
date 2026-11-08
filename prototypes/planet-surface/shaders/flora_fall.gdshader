// A felled or thrown plant: its own mesh, exactly as the scatter draws it standing (flora.gdshader:
// the instance's own tint and glow, wet and snow, light through the leaves), but still (no sway: it is
// down), its normals turning with it. It fades out by dithering, so it stays in the opaque pass and in
// the same light as the standing ones until the moment it goes.
shader_type spatial;
render_mode cull_disabled, diffuse_burley;

#include "res://shaders/noise.gdshaderinc"

global uniform vec4 weather_ground;  // wetness, snow, dust, solar

uniform vec3 tint = vec3(0.3, 0.5, 0.2); // the standing instance's own tint (its INSTANCE_CUSTOM.rgb)
uniform float glow = 0.0;                // and its glow (INSTANCE_CUSTOM.a)
uniform float fade = 1.0;

void fragment() {
	if (fade < 1.0 && fract(sin(dot(floor(FRAGCOORD.xy), vec2(12.9898, 78.233))) * 43758.5453) > fade) discard;
	vec3 own = srgb_to_linear(COLOR.rgb);
	vec3 c = mix(own, srgb_to_linear(min(tint * COLOR.rgb * 1.25, vec3(1.0))), COLOR.a);
	float snow = smoothstep(0.4, 0.8, NORMAL.y) * weather_ground.y;
	c = mix(c * (1.0 - weather_ground.x * 0.25), vec3(0.85, 0.88, 0.9), snow * 0.8);
	ALBEDO = c;
	ROUGHNESS = 0.85;
	vec3 through = c * COLOR.a * 0.16 * weather_ground.w;
	EMISSION = through + (glow > 0.0 ? srgb_to_linear(tint) * glow * 1.5 : vec3(0.0));
}
