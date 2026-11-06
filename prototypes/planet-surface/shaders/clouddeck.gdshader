// The cloud layer seen from above. The sky draws its clouds behind everything, so from over them
// the ground would hide them: this sheet in the layer draws what is under you, from the same
// coverage as the sky's clouds (sky.gdshader's cloud_density), so the two line up as you climb
// out through them. It only shows once the eye is on its way up through the layer.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, fog_disabled;

uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
uniform vec2 scale = vec2(8000.0, 8000.0);
uniform vec2 drift = vec2(0.0);
uniform float cover = 0.4;
uniform float dark = 0.0;
uniform float seen = 0.0;
uniform float light = 1.0;

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 uv = (wpos.xz + drift) / scale;
	float n = texture(noise, uv).r * 0.65 + texture(noise, uv * 2.0 + 0.31).r * 0.35;
	float coverage = n - (1.0 - mix(0.12, 1.05, cover));
	float body = smoothstep(0.0, 0.16, coverage);
	float tops = smoothstep(0.05, 0.4, coverage + texture(noise, uv * 9.0).r * 0.12);
	vec3 col = mix(vec3(0.62, 0.66, 0.72), vec3(1.0, 0.98, 0.95), tops) * (1.0 - dark * 0.6);
	ALBEDO = col * (0.2 + 0.8 * light);
	ALPHA = body * seen;
}
