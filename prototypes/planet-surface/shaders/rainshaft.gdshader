// A storm cell's rain shaft: a soft grey curtain under its cloud, seen from outside it. Its sides
// thin away as they turn edge-on, so it has no silhouette, and slow streaks fall down it.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec3 tint : source_color = vec3(0.32, 0.34, 0.38);
uniform float amount = 0.0;

float streaks(vec2 uv) {
	vec2 cell = floor(uv);
	vec2 f = fract(uv);
	float a = fract(sin(dot(cell, vec2(12.9898, 78.233))) * 43758.5453);
	float b = fract(sin(dot(cell + vec2(1.0, 0.0), vec2(12.9898, 78.233))) * 43758.5453);
	return mix(a, b, f.x * f.x * (3.0 - 2.0 * f.x));
}

void fragment() {
	float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float side = smoothstep(0.08, 0.8, facing);
	float fall = TIME * 0.45;
	float s = streaks(vec2(UV.x * 90.0, UV.y * 3.0 - fall)) * 0.6 + streaks(vec2(UV.x * 37.0 + 5.0, UV.y * 2.0 - fall * 0.7)) * 0.4;
	// Out of the cloud at the top, and thinner the lower it comes.
	float height = smoothstep(0.0, 0.08, UV.y) * (0.65 + 0.35 * UV.y);
	ALBEDO = tint;
	ALPHA = clamp(amount * side * height * (0.55 + 0.75 * s), 0.0, 1.0);
}
