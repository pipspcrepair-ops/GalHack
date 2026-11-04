// Deep space for the bridge window: a navy-to-violet field, a warm nebula band, stars, and
// the sun's disc. Cheap: a few hash and value-noise lookups per pixel.
shader_type sky;

uniform vec3 deep : source_color = vec3(0.012, 0.02, 0.055);
uniform vec3 haze : source_color = vec3(0.09, 0.05, 0.16);
uniform vec3 nebula_a : source_color = vec3(0.95, 0.38, 0.12);
uniform vec3 nebula_b : source_color = vec3(0.05, 0.55, 0.6);

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 x) {
	vec3 i = floor(x), f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
	           mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm(vec3 p) {
	float v = 0.0, a = 0.5;
	for (int i = 0; i < 4; i++) { v += a * noise(p); p *= 2.03; a *= 0.5; }
	return v;
}

void sky() {
	vec3 d = EYEDIR;
	vec3 col = mix(deep, haze, smoothstep(-0.2, 0.9, d.y * 0.5 + 0.35 - d.x * 0.2));

	// A tilted band of glowing gas.
	float band = exp(-pow((d.y - 0.35 * d.x + 0.12) * 3.2, 2.0));
	float n = fbm(d * 3.5);
	col += mix(nebula_b, nebula_a, smoothstep(0.35, 0.75, fbm(d * 2.0 + 7.0))) * band * smoothstep(0.35, 0.85, n) * 0.55;

	// Stars: sparse cells, a few bright, most faint.
	vec3 cell = floor(d * 260.0);
	float h = hash(cell);
	float star = step(0.9965, h) * smoothstep(0.42, 0.0, length(fract(d * 260.0) - 0.5));
	col += vec3(0.85, 0.9, 1.0) * star * (0.6 + 2.5 * step(0.9992, h));

	// The sun, where the directional light comes from.
	if (LIGHT0_ENABLED) {
		float sun = dot(d, LIGHT0_DIRECTION);
		col += vec3(1.0, 0.85, 0.6) * (smoothstep(0.9993, 0.9997, sun) * 14.0 + pow(max(sun, 0.0), 220.0) * 1.4);
	}
	COLOR = col;
}
