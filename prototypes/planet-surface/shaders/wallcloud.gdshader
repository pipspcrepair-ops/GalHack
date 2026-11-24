// The lowered dark base a funnel hangs from, a wall cloud: a squashed dome of storm cloud whose
// underside bulges down round the funnel's top and melts into the tower's own base above it.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 dark : source_color = vec3(0.11, 0.12, 0.145);
uniform float light = 1.0;
uniform float amount = 1.0;

varying vec3 local;

void vertex() {
	local = VERTEX;
}

void fragment() {
	vec2 uv = local.xz * 0.9 + vec2(TIME * 0.02, -TIME * 0.015);
	float n = texture(noise_tex, uv).r * 0.6 + texture(noise_tex, uv * 2.7 + 0.4).r * 0.4;
	float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float under = clamp(0.5 - local.y, 0.0, 1.0);          // the dome's lower half
	float rim = smoothstep(0.0, 0.55, facing);
	vec3 col = dark * (0.7 + 0.7 * n) * (0.35 + 0.65 * light);
	ALBEDO = col;
	ALPHA = clamp(amount * rim * (0.55 + 0.45 * n) * (0.35 + 0.65 * smoothstep(0.55, -0.4, local.y)), 0.0, 0.92);
}
