// Value noise that repeats exactly every planet width in x and every planet height in z, so
// anything textured with it joins across both seams of the torus, wherever the frame is.
// `period` is the lattice period in cells: planet size times frequency (whole numbers here,
// because planet sizes are whole kilometres and frequencies are 1/2^n).

float hash3(vec3 p) {
	uvec3 q = uvec3(ivec3(floor(p))) * uvec3(1597334673u, 3812015801u, 2798796415u);
	return float((q.x ^ q.y ^ q.z) * 1597334673u) * 2.3283064e-10;
}

float pnoise(vec3 p, vec2 period) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float x0 = mod(i.x, period.x), x1 = mod(i.x + 1.0, period.x);
	float z0 = mod(i.z, period.y), z1 = mod(i.z + 1.0, period.y);
	float a = mix(hash3(vec3(x0, i.y, z0)), hash3(vec3(x1, i.y, z0)), u.x);
	float b = mix(hash3(vec3(x0, i.y + 1.0, z0)), hash3(vec3(x1, i.y + 1.0, z0)), u.x);
	float c = mix(hash3(vec3(x0, i.y, z1)), hash3(vec3(x1, i.y, z1)), u.x);
	float d = mix(hash3(vec3(x0, i.y + 1.0, z1)), hash3(vec3(x1, i.y + 1.0, z1)), u.x);
	return mix(mix(a, b, u.y), mix(c, d, u.y), u.z);
}

// Four octaves starting at `freq` cycles per metre (a power of two).
float pfbm(vec3 p, float freq, vec2 size) {
	float s = 0.0, a = 0.5;
	for (int k = 0; k < 4; k++) {
		s += a * pnoise(p * freq, size * freq);
		freq *= 2.0;
		a *= 0.5;
	}
	return s / 0.9375;
}

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

// Whether a fragment is gone past the rim (x, z of the centre, half the square's width, m; no half
// width: no rim). The last 600 m thin away a pixel at a time, by a pattern shared by land and sea, so
// at that distance they fade into what stands behind them with no line.
bool past_rim(vec2 p, vec3 rim, vec2 pixel) {
	if (rim.z <= 0.0) return false;
	float out_by = (max(abs(p.x - rim.x), abs(p.y - rim.y)) - rim.z + 600.0) / 600.0;
	return out_by > fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715))));
}
