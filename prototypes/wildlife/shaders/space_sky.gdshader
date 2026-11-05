shader_type sky;

// Open space behind the space fauna: a deep wash, nebula clouds in two colours with dark dust
// lanes cut through them, a scatter of stars and the local star. No TIME, so the radiance map
// is built once.

uniform vec3 deep_color : source_color = vec3(0.02, 0.02, 0.05);
uniform vec3 cloud_a : source_color = vec3(0.45, 0.2, 0.6);
uniform vec3 cloud_b : source_color = vec3(0.1, 0.45, 0.55);
uniform float cloud_strength = 0.8;
uniform float cloud_scale = 1.6;
uniform float dust = 0.6;
uniform vec3 cloud_offset = vec3(0.0);
uniform float star_density = 1.0;
uniform float sun_size = 0.03;
uniform float sun_glow = 0.4;

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.yzx + 33.33);
	return fract((p.x + p.y) * p.z);
}

float vnoise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash13(i), b = hash13(i + vec3(1, 0, 0)), c = hash13(i + vec3(0, 1, 0)), d = hash13(i + vec3(1, 1, 0));
	float e = hash13(i + vec3(0, 0, 1)), g = hash13(i + vec3(1, 0, 1)), h = hash13(i + vec3(0, 1, 1)), k = hash13(i + vec3(1, 1, 1));
	return mix(mix(mix(a, b, f.x), mix(c, d, f.x), f.y), mix(mix(e, g, f.x), mix(h, k, f.x), f.y), f.z);
}

float fbm(vec3 p) {
	float v = 0.0, amp = 0.5;
	for (int i = 0; i < 5; i++) {
		v += amp * vnoise(p);
		p = p * 2.03 + vec3(1.7, 9.2, 4.1);
		amp *= 0.5;
	}
	return v;
}

vec3 stars(vec3 d) {
	vec3 p = d * 300.0;
	vec3 cell = floor(p);
	if (hash13(cell) < 1.0 - 0.05 * star_density) {
		return vec3(0.0);
	}
	vec3 at = vec3(hash13(cell + 3.1), hash13(cell + 7.7), hash13(cell + 1.3)) * 0.6 + 0.2;
	float r = length(fract(p) - at);
	float bright = pow(hash13(cell + 9.9), 5.0) * 8.0 + 0.4;
	vec3 tint = mix(vec3(1.0, 0.82, 0.65), vec3(0.72, 0.85, 1.0), hash13(cell + 4.4));
	return tint * bright * smoothstep(0.25, 0.0, r);
}

void sky() {
	vec3 d = normalize(EYEDIR);
	vec3 q = d * cloud_scale + cloud_offset;
	float warp = fbm(q * 1.3 + 4.0);
	float n1 = fbm(q + warp * 0.9);
	float n2 = fbm(q * 2.4 + vec3(n1 * 2.0));
	float cloud = smoothstep(0.35, 0.85, n1 * 0.7 + n2 * 0.5);
	vec3 col = deep_color + mix(cloud_a, cloud_b, smoothstep(0.3, 0.7, n2)) * cloud * cloud_strength;
	// Dust lanes: dark filaments eating through the brighter cloud.
	float lanes = smoothstep(0.5, 0.75, fbm(q * 4.2 + 9.0));
	col *= 1.0 - dust * lanes * cloud;
	if (!AT_CUBEMAP_PASS) {
		col += stars(d) * (1.0 - min(cloud * cloud_strength, 0.85));
	}
	vec3 light = LIGHT0_ENABLED ? LIGHT0_COLOR * LIGHT0_ENERGY : vec3(1.0);
	float angle = acos(clamp(dot(d, LIGHT0_DIRECTION), -1.0, 1.0));
	col += light * (smoothstep(sun_size, sun_size * 0.8, angle) * 30.0 + sun_glow * exp(-angle * 9.0) + 0.05 * exp(-angle * 2.0));
	COLOR = col;
}
