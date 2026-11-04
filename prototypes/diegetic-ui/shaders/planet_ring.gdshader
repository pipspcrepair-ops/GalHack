// A planet's rings: concentric bands of varying density with gaps, lit flat.
shader_type spatial;
render_mode cull_disabled, unshaded, blend_mix, depth_draw_opaque, shadows_disabled;

uniform vec3 tint : source_color = vec3(0.95, 0.82, 0.62);
uniform float inner = 0.6;   // as a fraction of the outer radius
uniform float brightness = 0.9;

varying vec2 flat_pos;

void vertex() {
	flat_pos = VERTEX.xz;
}

void fragment() {
	float r = length(flat_pos);            // the mesh's outer radius is 1
	float t = (r - inner) / (1.0 - inner);
	float density = 0.55 + 0.45 * sin(t * 70.0) * sin(t * 23.0 + 1.3);
	density *= smoothstep(0.0, 0.05, t) * smoothstep(1.0, 0.92, t);
	density *= 1.0 - 0.85 * smoothstep(0.02, 0.0, abs(t - 0.62));   // the big gap
	ALBEDO = tint * brightness * (0.7 + 0.3 * t);
	ALPHA = clamp(density, 0.0, 1.0) * step(inner, r) * step(r, 1.0) * 0.85;
}
