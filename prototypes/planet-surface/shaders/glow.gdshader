// Additive, unshaded light: lightning bolts, the charge glow before a strike, embers and
// warning rings. Values above 1 feed the glow (bloom) pass.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec3 tint : source_color = vec3(0.85, 0.8, 1.0);
uniform float energy = 4.0;
uniform float softness = 0.0;   // > 0 fades toward the quad's edges (UV.x across it)

void fragment() {
	float edge = softness > 0.0 ? smoothstep(0.0, softness, 0.5 - abs(UV.x - 0.5)) : 1.0;
	ALBEDO = tint * energy * COLOR.rgb * COLOR.a * edge;
}
