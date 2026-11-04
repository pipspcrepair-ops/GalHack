// Creatures (and plants): colour comes from the vertex colours the mesh builder bakes per
// primitive, the skin pattern is drawn procedurally in each piece's own space (so stripes
// ride along with a moving leg), and the vertex alpha marks what glows. One material per
// species: pattern, accent and glow colour are uniforms.
shader_type spatial;
render_mode diffuse_lambert, specular_schlick_ggx, cull_back;

uniform vec3 accent : source_color = vec3(0.2);
uniform vec3 glow_color : source_color = vec3(0.5, 1.0, 1.0);
uniform float glow_strength = 0.0;
// 1: glowing parts shine in the species' glow colour; 0: in their own vertex colour (plants).
uniform float glow_tint = 1.0;
// 0 plain, 1 countershade, 2 spots, 3 stripes, 4 bands, 5 rings, 6 checker, 7 veins
uniform int pattern = 0;
uniform float pattern_freq = 2.0;
// The Hollow Bloom: veins that pulse with light.
uniform float veins_glow = 0.0;
uniform float roughness_value = 0.72;
// Flashes when hit. Per instance, so one shared material serves the whole species.
instance uniform float hurt = 0.0;

varying vec3 lp;

void vertex() {
	lp = VERTEX;
}

float hash3(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float tri(float v) {
	return abs(fract(v) - 0.5) * 2.0;
}

float pattern_mask(vec3 p) {
	if (pattern == 1) {
		return smoothstep(-0.1, 0.35, p.y);
	} else if (pattern == 2) {
		vec3 cell = floor(p);
		vec3 f = fract(p) - 0.5 - (vec3(hash3(cell), hash3(cell + 3.1), hash3(cell + 7.7)) - 0.5) * 0.4;
		float r = 0.2 + 0.12 * hash3(cell + 1.7);
		return 1.0 - smoothstep(r - 0.04, r + 0.02, length(f));
	} else if (pattern == 3) {
		return smoothstep(0.42, 0.58, tri(p.z + 0.4 * p.y + 0.18 * sin(p.x * 3.0)));
	} else if (pattern == 4) {
		return smoothstep(0.62, 0.72, tri(p.z * 0.45));
	} else if (pattern == 5) {
		return smoothstep(0.5, 0.62, tri(length(p.xz) * 1.1 + p.y * 0.3));
	} else if (pattern == 6) {
		return mod(floor(p.x) + floor(p.y) + floor(p.z), 2.0);
	} else if (pattern == 7) {
		float v = abs(sin(p.x * 2.1 + sin(p.z * 1.7) * 1.5 + sin(p.y * 2.3)) * sin(p.z * 1.9 + sin(p.x * 1.3) * 1.8));
		return 1.0 - smoothstep(0.0, 0.2, v);
	}
	return 0.0;
}

void fragment() {
	vec3 base = COLOR.rgb;
	float m = pattern == 0 ? 0.0 : pattern_mask(lp * pattern_freq) * UV.x;
	ALBEDO = mix(base, accent, m);
	ROUGHNESS = roughness_value;
	SPECULAR = 0.3;
	RIM = 0.3;
	RIM_TINT = 0.5;
	float g = COLOR.a * glow_strength;
	vec3 em = mix(base, glow_color, glow_tint) * g * 2.6;
	em += accent * m * veins_glow * (0.55 + 0.45 * sin(TIME * 2.7 + lp.x * 3.0 + lp.z * 2.0)) * 1.8;
	em += vec3(1.0, 0.35, 0.2) * hurt * 0.9;
	EMISSION = em;
}
