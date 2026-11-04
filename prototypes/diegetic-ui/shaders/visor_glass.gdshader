// The helmet visor itself: a faint vignette and reflections on the glass, frost creeping in
// from the edges in the cold, a red pulse while losing health, a flash on the edge facing
// incoming fire, and the dark helmet beyond the glass.
//
// Each effect is a layer composited "over" the one before, so a thin layer (the ragged front
// of the frost) stays thin and light instead of turning into grey smudge.
shader_type canvas_item;

uniform float frost : hint_range(0.0, 1.0) = 0.0;
uniform float harm : hint_range(0.0, 1.0) = 0.0;
uniform float fire : hint_range(0.0, 1.0) = 0.0;
uniform vec2 fire_dir = vec2(1.0, 0.0);   // screen-space direction toward the shooter, y down
uniform float aspect = 1.7778;

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
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
	float streak = smoothstep(0.09, 0.0, abs(a.x * 0.55 + a.y + 1.35)) * 0.06
	             + smoothstep(0.03, 0.0, abs(a.x * 0.55 + a.y + 1.1)) * 0.05;
	col = over(col, vec3(0.75, 0.85, 1.0), streak);

	// Frost: ice grows inward from the rim with a ragged front.
	vec2 fp = UV * vec2(aspect, 1.0);
	float n = noise(fp * 6.0) * 0.6 + noise(mat2(vec2(0.8, -0.6), vec2(0.6, 0.8)) * fp * 17.0) * 0.4;
	float reach = 1.1 - frost * 0.3;
	float ice = smoothstep(reach - 0.01, reach + 0.09, r + (n - 0.5) * 0.32) * frost;
	col = over(col, vec3(0.9, 0.96, 1.0), ice * 0.7);

	// Losing health: a dull red pulse at the edges.
	float edge = smoothstep(0.62, 1.05, r);
	col = over(col, vec3(0.85, 0.05, 0.05), harm * edge * (0.33 + 0.22 * sin(TIME * 6.0)));

	// Under fire: a hot flash on the side facing the shooter.
	vec2 dir = normalize(a + vec2(1e-5));
	float toward = pow(max(dot(dir, normalize(fire_dir)), 0.0), 5.0);
	col = over(col, vec3(1.0, 0.35, 0.1), 0.8 * fire * toward * smoothstep(0.55, 1.0, r));

	// The helmet: solid dark beyond the glass, with a warm lip of light along the seal.
	col = over(col, vec3(0.03, 0.03, 0.035), smoothstep(1.03, 1.055, r));
	col.rgb += vec3(0.22, 0.17, 0.12) * smoothstep(0.008, 0.0, abs(r - 1.045));

	COLOR = col;
}
