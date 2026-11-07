// A storm cell's rain shaft: a soft grey veil under its cloud, marched through in the fragment so it
// has no mesh shape at all. The column is wider and fainter near the ground, torn into uneven
// curtains with gaps, leans with the wind, drifts slowly, and fades up into the cloud base and down
// into the ground haze. The mesh is only a loose bound that the ray is clipped against.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec3 tint : source_color = vec3(0.32, 0.34, 0.38);
uniform float amount = 0.0;
uniform vec3 axis_base = vec3(0.0); // ground centre of the shaft
uniform float shaft_height = 1000.0;
uniform float reach = 300.0;        // mean radius at the cloud
uniform float bound = 600.0;        // radius of the clipping cylinder
uniform vec2 lean = vec2(0.0);      // sideways offset of the top, metres
uniform vec2 drift = vec2(0.0);     // metres per second the pattern slides
uniform float seed = 0.0;

varying vec3 wpos;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3) + seed);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float vnoise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float density(vec3 p) {
	float h = (p.y - axis_base.y) / shaft_height;
	if (h < 0.0 || h > 1.0) return 0.0;
	vec2 c = axis_base.xz + lean * h + drift * TIME;
	vec2 d = p.xz - (axis_base.xz + lean * h);
	float width = reach * (1.25 - 0.55 * h);      // wider low down
	float r = length(d) / width;
	vec2 q = p.xz - c;
	// Ragged edge and curtains: noise over the position round the axis, stretched along the fall.
	float edge = vnoise(vec3(q * (3.0 / reach), h * 2.5)) * 0.6 + vnoise(vec3(q * (9.0 / reach), h * 6.0 + 7.0)) * 0.4;
	float curtain = vnoise(vec3(q * (5.5 / reach), h * 1.3 + 3.0));
	curtain = smoothstep(0.28, 0.75, curtain);
	float body = 1.0 - smoothstep(0.15, 1.0, r + (edge - 0.5) * 0.95);
	float fade = smoothstep(1.0, 0.8, h) * smoothstep(0.0, 0.07, h) * (0.55 + 0.45 * h);
	return body * (0.25 + 0.75 * curtain) * fade;
}

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 cam = (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	vec3 dir = normalize(wpos - cam);
	// Clip the ray against the vertical slab and the bounding cylinder.
	float t0 = 0.0, t1 = 1e5;
	if (abs(dir.y) > 1e-5) {
		float a = (axis_base.y - cam.y) / dir.y;
		float b = (axis_base.y + shaft_height - cam.y) / dir.y;
		t0 = max(t0, min(a, b));
		t1 = min(t1, max(a, b));
	} else if (cam.y < axis_base.y || cam.y > axis_base.y + shaft_height) {
		discard;
	}
	vec2 o = cam.xz - (axis_base.xz + lean * 0.5);
	vec2 dd = dir.xz;
	float A = dot(dd, dd), B = dot(o, dd), C = dot(o, o) - bound * bound;
	if (A > 1e-9) {
		float disc = B * B - A * C;
		if (disc <= 0.0) discard;
		float s = sqrt(disc);
		t0 = max(t0, (-B - s) / A);
		t1 = min(t1, (-B + s) / A);
	} else if (C > 0.0) {
		discard;
	}
	if (t1 <= t0) discard;
	t1 = min(t1, t0 + 4000.0);
	const int N = 14;
	float dt = (t1 - t0) / float(N);
	float jitter = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
	float sum = 0.0;
	for (int i = 0; i < N; i++) {
		float t = t0 + (float(i) + jitter) * dt;
		sum += density(cam + dir * t) * dt;
	}
	float optical = sum / (reach * 1.6);
	ALBEDO = tint;
	ALPHA = clamp((1.0 - exp(-optical * 3.2)) * amount, 0.0, 1.0);
}
