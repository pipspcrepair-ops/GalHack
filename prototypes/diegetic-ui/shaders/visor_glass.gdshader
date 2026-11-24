// The helmet visor itself: a faint vignette and reflections on the glass, frost creeping in
// from the edges in the cold, a red pulse while losing health, a flash on the edge facing
// incoming fire, and the dark helmet beyond the glass.
//
// Each effect is a layer composited "over" the one before, so a thin layer (the ragged front
// of the frost) stays thin and light instead of turning into grey smudge.
shader_type canvas_item;

uniform float frost : hint_range(0.0, 1.0) = 0.0;
// The two straight reflection streaks across the glass: 1 shows them, 0 hides them (a game whose
// sky must never carry a straight line, the slice, turns them off).
uniform float streaks = 1.0;
uniform float harm : hint_range(0.0, 1.0) = 0.0;
uniform float fire : hint_range(0.0, 1.0) = 0.0;
uniform vec2 fire_dir = vec2(1.0, 0.0);   // screen-space direction toward the shooter, y down
uniform float aspect = 1.7778;

// A random unit gradient per lattice point.
vec2 grad(vec2 i) {
	float h = fract(sin(dot(i, vec2(127.1, 311.7))) * 43758.5453);
	float t = h * 6.2831853;
	return vec2(cos(t), sin(t));
}

// Gradient noise, 0..1. Value noise was used before: its contours follow the lattice and, cut by a
// narrow threshold, read as hard faceted polygons on the glass.
float noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float a = dot(grad(i), f);
	float b = dot(grad(i + vec2(1.0, 0.0)), f - vec2(1.0, 0.0));
	float c = dot(grad(i + vec2(0.0, 1.0)), f - vec2(0.0, 1.0));
	float d = dot(grad(i + vec2(1.0, 1.0)), f - vec2(1.0, 1.0));
	return 0.5 + 0.7 * mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float fbm(vec2 p) {
	mat2 rot = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8));
	float v = 0.0, w = 0.5;
	for (int k = 0; k < 4; k++) { v += noise(p) * w; p = rot * p * 2.03 + 17.0; w *= 0.5; }
	return v / 0.9375;
}

// "Over": a layer (rgb, a) on top of what is already there.
vec4 over(vec4 base, vec3 rgb, float a) {
	float out_a = a + base.a * (1.0 - a);
	vec3 out_rgb = (rgb * a + base.rgb * base.a * (1.0 - a)) / max(out_a, 1e-4);
	return vec4(out_rgb, out_a);
}

void fragment() {
	vec2 e = UV * 2.0 - 1.0;                       // -1..1 across the window
	// A rounded rectangle (superellipse): where the curved glass ends at the helmet.
	float r = pow(pow(abs(e.x) * 0.975, 7.0) + pow(abs(e.y), 7.0), 1.0 / 7.0);
	vec2 a = vec2(e.x * aspect, e.y);

	// Glass: the slightest darkening toward the edge, and two soft reflection streaks.
	vec4 col = vec4(0.01, 0.015, 0.03, smoothstep(0.55, 1.05, r) * 0.32);
	float streak = (smoothstep(0.09, 0.0, abs(a.x * 0.55 + a.y + 1.35)) * 0.06
	             + smoothstep(0.03, 0.0, abs(a.x * 0.55 + a.y + 1.1)) * 0.05) * streaks;
	col = over(col, vec3(0.75, 0.85, 1.0), streak);

	// The edge effects (the red pulse, frost) share one budget of cover, so several at once blend
	// into a soft rim instead of stacking into opaque blobs.
	float budget = 0.5 * smoothstep(0.4, 1.0, r);

	// Losing health: a dull red pulse, darkest at the very rim, under the frost.
	float edge = smoothstep(0.55, 1.05, r);
	edge *= edge;
	float pulse = min(harm * edge * (0.26 + 0.1 * sin(TIME * 6.0)), budget);
	col = over(col, vec3(0.45, 0.03, 0.03), pulse);

	// Frost: fine-grained rime creeping in from the corners first, then along the rim. The front is
	// a wide soft falloff broken by fbm; the grain is a pixel-fine speckle inside it.
	vec2 fp = UV * vec2(aspect, 1.0);
	float corner = sqrt(abs(e.x * e.y));                 // 1 in the corners, 0 along the axes
	float reach = 1.05 - frost * 0.45;                    // how far in the creep has come
	float front = mix(r, corner * 1.2, 0.6) + (fbm(fp * 4.0) - 0.5) * 0.2;
	float creep = smoothstep(reach, reach + 0.3, front);
	float grain = fbm(fp * 260.0) * 0.6 + fbm(fp * 40.0) * 0.4;
	float ice = creep * (0.35 + 0.65 * smoothstep(0.4, 0.7, grain)) * min(frost * 1.6, 1.0);
	// The frost takes what cover the pulse left, plus a little: it whitens the red rather than pinking it.
	col = over(col, vec3(0.86, 0.93, 1.0), min(ice * 0.6, budget - pulse * 0.6));
	// Under fire: a hot flash on the side facing the shooter.
	vec2 dir = normalize(a + vec2(1e-5));
	float toward = pow(max(dot(dir, normalize(fire_dir)), 0.0), 5.0);
	col = over(col, vec3(1.0, 0.35, 0.1), 0.8 * fire * toward * smoothstep(0.55, 1.0, r));

	// The helmet: solid dark beyond the glass, with a warm lip of light along the seal.
	col = over(col, vec3(0.03, 0.03, 0.035), smoothstep(1.03, 1.055, r));
	col.rgb += vec3(0.22, 0.17, 0.12) * smoothstep(0.008, 0.0, abs(r - 1.045));

	COLOR = col;
}
