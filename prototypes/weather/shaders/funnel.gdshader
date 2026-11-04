// A tornado funnel: a lathed trumpet mesh whose surface is rotating, rising streaks of
// condensation and dust. Denser at the silhouette (like a real funnel seen side-on), it
// lowers from the cloud base while forming and leans and snakes as it moves.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 color_low : source_color = vec3(0.32, 0.27, 0.22);
uniform vec3 color_high : source_color = vec3(0.5, 0.52, 0.5);
uniform vec3 lit_color : source_color = vec3(0.9, 0.85, 0.7);
uniform vec3 to_sun = vec3(-0.8, 0.45, -0.3);
uniform float spin = 1.0;
uniform float opacity = 0.9;
uniform float formation = 1.0;    // 0..1: how far down from the cloud base the funnel reaches
uniform vec2 lean = vec2(0.0);    // metres the top sits behind the base
uniform float height = 170.0;

varying float h;
varying vec3 local;

void vertex() {
	h = UV.y; // 0 at the ground, 1 at the cloud base
	local = VERTEX;
	float snake = sin(TIME * 0.7 + h * 5.0) * 3.5 * (1.0 - h) + sin(TIME * 0.31 + h * 2.0) * 6.0 * h;
	VERTEX.xz += lean * h * h + vec2(snake, snake * 0.6);
}

void fragment() {
	// Hide what hasn't come down yet; the tip is a ragged rope.
	float reach = 1.0 - formation;
	float a = UV.x;
	float rope = texture(noise_tex, vec2(a * 2.0, TIME * 0.2)).r * 0.08;
	if (h < reach - rope) discard;

	vec2 uv = vec2(a * 3.0 + TIME * spin / (0.35 + h), h * 2.2 - TIME * 0.35);
	float n = texture(noise_tex, uv).r;
	float n2 = texture(noise_tex, uv * 2.3 + vec2(0.3, TIME * 0.1)).r;
	float streaks = smoothstep(0.25, 0.75, n * 0.65 + n2 * 0.35);

	float edge = 1.0 - abs(dot(NORMAL, VIEW));
	float alpha = (0.6 + 0.4 * pow(edge, 1.2)) * (0.6 + 0.4 * streaks);
	alpha *= smoothstep(reach - rope, reach - rope + 0.05, h);        // soft rope tip
	alpha *= 1.0 - smoothstep(0.82, 1.0, h) * 0.85;                    // melts into the cloud base
	alpha *= smoothstep(0.0, 0.04, h + 0.02);

	vec3 side = normalize(vec3(local.x, 0.0, local.z) + vec3(0.0001));
	float lit = clamp(dot(side, normalize(vec3(to_sun.x, 0.0, to_sun.z))) * 0.5 + 0.5, 0.0, 1.0);
	vec3 col = mix(color_low, color_high, smoothstep(0.0, 0.5, h));
	col = mix(col * 0.6, mix(col, lit_color, 0.2), lit) * (0.75 + 0.35 * streaks);

	ALBEDO = col;
	ALPHA = clamp(alpha * opacity, 0.0, 1.0);
}
