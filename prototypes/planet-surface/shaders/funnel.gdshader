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
uniform float shape = 0.5;        // 0 thin rope .. 0.3 stovepipe .. 0.55 classic cone .. 1 wide wedge
uniform float seed = 0.0;
uniform float clean = 0.0;        // 1: a smooth pale cone, lit from the side
uniform float decay = 0.0;        // 0..1: stretching into a contorted rope that breaks up
uniform vec2 bow = vec2(0.0);     // metres the middle of the funnel stands off the straight line
uniform float s_bend = 0.4;       // how far its centre line snakes in an S on top of the bow (a waterspout's rope, a dust devil)
uniform float top_fade = 0.0;     // 1: it thins out into clear air toward its top instead of entering a cloud (a dust devil)

varying float h;
varying vec3 local;

void vertex() {
	h = UV.y; // 0 at the ground, 1 at the cloud base
	// Not a perfect tunnel: the width along the funnel is rolled from its shape, with bulges and pinches,
	// and its centre line bows and sways instead of standing straight.
	float hh = max(h, 0.001);
	float base = 3.2 + 38.0 * pow(hh, 2.3);
	float pipe = 3.2 + 14.0 * pow(hh, 0.6);
	float prof = mix(pipe, base, smoothstep(0.2, 0.55, shape));
	prof *= mix(0.3, 1.0, smoothstep(0.0, 0.35, shape)) * mix(1.0, 2.0, smoothstep(0.7, 1.0, shape));
	prof *= 1.0 + (0.18 * sin(h * 11.0 + seed) + 0.1 * sin(h * 23.0 + seed * 2.0)) * smoothstep(0.1, 0.4, shape + 0.15);
	VERTEX.xz *= prof / base;
	local = VERTEX;
	float bowed = sin(3.14159 * h) + s_bend * sin(6.2832 * h + TIME * 0.2 + seed);
	VERTEX.xz += bow * bowed * (1.0 + 3.0 * decay);
	VERTEX.xz += vec2(cos(seed), sin(seed)) * decay * 50.0 * sin(h * 14.0 + TIME * 0.8) * (1.0 - h);
	float snake = sin(TIME * 0.7 + h * 5.0) * 3.5 * (1.0 - h) + sin(TIME * 0.31 + h * 2.0) * 6.0 * h;
	VERTEX.xz += lean * h * h + vec2(snake, snake * 0.6);
}

void fragment() {
	// Hide what hasn't come down yet; the tip is a ragged rope.
	float reach = 1.0 - formation;
	float a = UV.x;
	float rope = texture(noise_tex, vec2(a * 2.0, TIME * 0.2)).r * 0.08;
	if (h < reach - rope) discard;

	// A twist, not rings: the streaks wind up the funnel as it turns. (They turn as one: a turn that grew with height
	// times a running clock sheared the pattern into stacked rings.)
	vec2 uv = vec2(a * 3.0 + h * 2.5 + TIME * spin * 0.4, h * 1.1 - TIME * 0.25);
	float n = texture(noise_tex, uv).r;
	float n2 = texture(noise_tex, uv * 2.3 + vec2(0.3, TIME * 0.1)).r;
	float streaks = smoothstep(0.25, 0.75, n * 0.65 + n2 * 0.35);

	float edge = 1.0 - abs(dot(NORMAL, VIEW));
	float alpha = (0.6 + 0.4 * pow(edge, 1.2)) * (0.6 + 0.4 * streaks);
	alpha *= smoothstep(reach - rope, reach - rope + 0.05, h);        // soft rope tip
	alpha *= mix(1.0 - smoothstep(0.82, 1.0, h) * 0.85, 1.0 - smoothstep(0.1, 0.95, h), top_fade); // melts into the cloud base, or into clear air
	alpha *= smoothstep(0.0, 0.04, h + 0.02);

	vec3 side = normalize(vec3(local.x, 0.0, local.z) + vec3(0.0001));
	float lit = clamp(dot(side, normalize(vec3(to_sun.x, 0.0, to_sun.z))) * 0.5 + 0.5, 0.0, 1.0);
	vec3 col = mix(color_low, color_high, smoothstep(0.0, 0.5, h));
	col = mix(col * 0.6, mix(col, lit_color, 0.2), lit) * (0.75 + 0.35 * streaks);

	// Ragged and torn at the edge, partly translucent; breaking up as it dies; a wedge may stop short of the ground.
	alpha *= mix(1.0, smoothstep(0.3, 0.7, n2), edge * 0.85 * (1.0 - clean));
	alpha *= mix(1.0, smoothstep(decay * 0.8, decay * 0.8 + 0.15, n2), step(0.001, decay));
	alpha *= smoothstep(0.12 * smoothstep(0.5, 1.0, shape), 0.12 * smoothstep(0.5, 1.0, shape) + 0.1, h);
	col = mix(col, mix(vec3(0.78, 0.8, 0.82), lit_color, 0.35) * (0.6 + 0.6 * lit), clean * 0.8); // a pale cone, brighter on the sunward side
	ALBEDO = col;
	ALPHA = clamp(alpha * opacity, 0.0, 1.0);
}
