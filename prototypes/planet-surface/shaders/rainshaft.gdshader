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
uniform sampler2D scene_depth : hint_depth_texture, repeat_disable, filter_nearest;
uniform float soft_metres = 12.0;   // how far in front of a surface the veil thins out

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
	float up = p.y - axis_base.y;
	vec2 wdir = normalize(lean + vec2(0.0001, 0.0));
	// The pattern slides with the wind and slants with it: the lower rain trails the upper.
	vec2 slant = wdir * (0.12 + 0.2 * length(lean) / max(shaft_height, 1.0)) * 3.0;
	vec2 c = axis_base.xz + lean * h;
	vec2 q = p.xz - c + drift * TIME - slant * up * 0.25;
	vec2 d = p.xz - c;
	float width = reach * (1.35 - 0.6 * h);       // wider low down, narrowing into the cloud
	float r = length(d) / width;
	// The footprint is noise, not a radius: lobes, bays and gaps at several sizes. The radius only
	// keeps the whole thing from wandering off, and never makes an edge.
	float field = vnoise(vec3(q * (1.6 / reach), up * 0.0012)) * 0.5
		+ vnoise(vec3(q * (4.0 / reach) + 5.0, up * 0.0025 + 3.0)) * 0.3
		+ vnoise(vec3(q * (11.0 / reach) + 9.0, up * 0.004 + 7.0)) * 0.2;
	float env = 1.0 - smoothstep(0.3, 1.2, r);
	float cover = env * 1.3 + (field - 0.5) * 0.95 - 0.15;
	float body = smoothstep(0.3, 0.85, cover);
	// Fine slanted streaks, uneven: stretched along the fall, never an even comb.
	vec2 sq = q + slant * up;
	float s1 = vnoise(vec3(sq * (38.0 / reach), up * 0.0016 + TIME * 0.08));
	float s2 = vnoise(vec3(sq * (95.0 / reach) + 4.0, up * 0.0022 - TIME * 0.12 + 2.0));
	float streak = 0.5 + 0.9 * (s1 * 0.6 + s2 * 0.4 - 0.5);
	// Densest just under the cloud base, thinning toward the ground into a haze with no end line.
	float vert = (0.1 + 0.9 * smoothstep(0.0, 0.8, h)) * (1.0 - smoothstep(0.93, 1.0, h));
	return body * clamp(streak, 0.15, 1.3) * vert;
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
	// The veil only fills the space in front of the first surface seen through this pixel.
	float scene_t = 1e6;
	float raw = texture(scene_depth, SCREEN_UV).x;
	if (raw > 1e-7) {
		vec4 v = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
		scene_t = length(v.xyz / v.w);
	}
	t1 = min(t1, scene_t);
	if (t1 <= t0) discard;
	t1 = min(t1, t0 + 4000.0);
	const int N = 14;
	float dt = (t1 - t0) / float(N);
	float jitter = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
	float sum = 0.0;
	for (int i = 0; i < N; i++) {
		float t = t0 + (float(i) + jitter) * dt;
		sum += density(cam + dir * t) * dt * smoothstep(0.0, soft_metres, scene_t - t);
	}
	float optical = sum / (reach * 1.6);
	ALBEDO = tint;
	ALPHA = clamp((1.0 - exp(-optical * 4.0)) * amount, 0.0, 1.0);
}
