// Additive hologram light: a fresnel rim, horizontal scan bands that crawl upward, and a
// faint flicker. No lighting, no depth writes: it is light hanging in the air.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec4 tint : source_color = vec4(0.37, 0.91, 1.0, 1.0);
uniform float intensity = 1.0;
uniform float body = 0.25;      // light from the face itself
uniform float rim = 1.6;        // light at grazing angles
uniform float scan = 0.45;      // strength of the scan bands
uniform float band_density = 70.0;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float fresnel = pow(1.0 - facing, 2.5);
	float bands = 1.0 - scan * step(0.55, fract(world_pos.y * band_density - TIME * 0.9));
	float flicker = 0.93 + 0.07 * sin(TIME * 29.0 + world_pos.y * 31.0) * sin(TIME * 3.1);
	ALBEDO = tint.rgb * (body + fresnel * rim) * bands * flicker * intensity;
}
