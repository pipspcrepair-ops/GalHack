// Every bolt of lightning (a storm's, the Sleeper tool's, an arc gun's): one camera-facing strip per
// channel, UV.x across it, UV.y the hot core's half-width as a share of the strip's. A thin bright
// core whitens toward its middle; round it a glow falls smoothly to nothing at the strip's edge, so
// no edge shows. Additive, so a strip only ever adds light. (Bolts.Profile is this same curve.)
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform vec3 tint : source_color = vec3(0.85, 0.8, 1.0);
uniform float energy = 4.0;
uniform float glow = 0.35;   // the glow's strength at the middle, against the core's 1

void fragment() {
	float d = abs(UV.x - 0.5) * 2.0;
	float core = exp(-(d * d) / max(UV.y * UV.y, 1e-4));
	float halo = glow * (1.0 - d) * (1.0 - d) * (1.0 - d) * (1.0 - d);
	vec3 base = tint * COLOR.rgb;
	ALBEDO = mix(base, vec3(1.0), core * 0.85) * energy * COLOR.a * (core + halo);
}
