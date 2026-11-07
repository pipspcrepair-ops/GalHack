// A felled tree: its own mesh, its own colours, fading away where it lies.
shader_type spatial;
render_mode cull_disabled, diffuse_burley;

#include "res://shaders/noise.gdshaderinc"

uniform vec3 tint = vec3(0.3, 0.5, 0.2);
uniform float fade = 1.0;

void fragment() {
	vec3 own = srgb_to_linear(COLOR.rgb);
	ALBEDO = mix(own, srgb_to_linear(min(tint * COLOR.rgb * 1.25, vec3(1.0))), COLOR.a);
	ROUGHNESS = 0.85;
	ALPHA = fade;
}
