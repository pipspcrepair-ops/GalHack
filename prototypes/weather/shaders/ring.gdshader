// A pulsing ring on the ground where burning debris will land: the warning before the hit.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec3 tint : source_color = vec3(1.0, 0.3, 0.1);
uniform float progress = 0.0;   // 0..1 of the fall

void fragment() {
	float r = length(UV - vec2(0.5)) * 2.0;
	float pulse = 0.6 + 0.4 * sin(TIME * mix(6.0, 22.0, progress));
	float ring = smoothstep(0.08, 0.0, abs(r - mix(1.0, 0.35, progress))) + smoothstep(0.06, 0.0, abs(r - 0.92)) * 0.6;
	float core = smoothstep(0.35, 0.0, r) * progress * 0.6;
	ALBEDO = tint * (ring + core) * pulse * (0.6 + 2.4 * progress);
}
