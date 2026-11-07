// Creatures (and plants): colour comes from the vertex colours the mesh builder bakes per
// primitive, the skin pattern is drawn procedurally in each piece's own space (so stripes
// ride along with a moving leg), and the vertex alpha marks what glows. One material per
// species: pattern, accent and glow colour are uniforms.
//
// UV.x is how much of the pattern shows; UV.y says what the surface is made of, so one
// material still gives dull pebbled hide, glossy horn and shell, thin wing membrane that
// lights up from behind, and wet eyes that catch the light.
shader_type spatial;
render_mode diffuse_lambert_wrap, specular_schlick_ggx, cull_back;

uniform vec3 accent : source_color = vec3(0.2);
uniform vec3 glow_color : source_color = vec3(0.5, 1.0, 1.0);
uniform float glow_strength = 0.0;
// 1: glowing parts shine in the species' glow colour; 0: in their own vertex colour (plants).
uniform float glow_tint = 1.0;
// 0 plain, 1 countershade, 2 spots, 3 stripes, 4 bands, 5 rings, 6 mottle, 7 veins
uniform int pattern = 0;
uniform float pattern_freq = 2.0;
// The Hollow Bloom: veins that pulse with light.
uniform float veins_glow = 0.0;
uniform float roughness_value = 0.72;
// The outline's rim of light and the lift against haze at range (people a little more than creatures).
uniform float rim_strength = 0.3;
uniform float far_lift = 0.18;
// Fine surface detail (pebbled hide, growth rings on horn) per metre, and how strong.
uniform float detail_freq = 14.0;
uniform float detail_strength = 0.0;
// Flashes when hit. Per instance, so one shared material serves the whole species.
instance uniform float hurt = 0.0;
// 0..1: a lurker waking in the dark: its glow brightens and its eyes catch the light.
instance uniform float wake = 0.0;

varying vec3 lp;

void vertex() {
	lp = VERTEX;
}

float hash3(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float vnoise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = mix(mix(hash3(i), hash3(i + vec3(1, 0, 0)), f.x), mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), f.x), f.y);
	float b = mix(mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), f.x), mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), f.x), f.y);
	return mix(a, b, f.z);
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
		// Stripes broken up a little by noise, like a real coat rather than a barber's pole.
		float n = vnoise(p * 1.7) * 0.35;
		return smoothstep(0.42, 0.58, tri(p.z + 0.4 * p.y + 0.18 * sin(p.x * 3.0) + n));
	} else if (pattern == 4) {
		return smoothstep(0.62, 0.72, tri(p.z * 0.45));
	} else if (pattern == 5) {
		return smoothstep(0.5, 0.62, tri(length(p.xz) * 1.1 + p.y * 0.3));
	} else if (pattern == 6) {
		// Mottle: soft blotches at two scales, camouflage rather than a print.
		float n = vnoise(p * 0.9) * 0.65 + vnoise(p * 2.3) * 0.35;
		return smoothstep(0.5, 0.62, n);
	} else if (pattern == 7) {
		float v = abs(sin(p.x * 2.1 + sin(p.z * 1.7) * 1.5 + sin(p.y * 2.3)) * sin(p.z * 1.9 + sin(p.x * 1.3) * 1.8));
		return 1.0 - smoothstep(0.0, 0.2, v);
	}
	return 0.0;
}

void fragment() {
	vec3 base = COLOR.rgb;
	float mat = UV.y;
	bool horn = mat > 0.5 && mat < 1.5;
	bool shell = mat > 1.5 && mat < 2.5;
	bool membrane = mat > 2.5 && mat < 3.5;
	bool eye = mat > 3.5;

	float m = pattern == 0 ? 0.0 : pattern_mask(lp * pattern_freq) * UV.x;
	vec3 albedo = mix(base, accent, m);

	// Surface detail: a height field in the piece's own space, turned into a bump with screen
	// derivatives (no tangents needed). Hide is pebbled, horn has growth rings, shell is
	// smooth with shallow pits, membrane is veined, eyes are glass.
	float h = 0.0;
	float strength = detail_strength;
	if (eye) {
		strength = 0.0;
	} else if (horn) {
		h = tri(lp.y * detail_freq * 0.5 + vnoise(lp * detail_freq * 0.5) * 0.8);
		strength *= 0.5;
	} else if (shell) {
		h = vnoise(lp * detail_freq * 0.45);
		strength *= 0.35;
	} else if (membrane) {
		h = 1.0 - smoothstep(0.0, 0.1, abs(vnoise(lp * detail_freq * 0.3) - 0.5));
		strength *= 0.3;
	} else {
		h = vnoise(lp * detail_freq) * 0.7 + vnoise(lp * detail_freq * 2.7) * 0.3;
	}
	if (strength > 0.0) {
		float bump = strength / detail_freq;
		vec3 dpdx = dFdx(VERTEX);
		vec3 dpdy = dFdy(VERTEX);
		vec3 r1 = cross(dpdy, NORMAL);
		vec3 r2 = cross(NORMAL, dpdx);
		float det = dot(dpdx, r1);
		vec3 grad = sign(det) * (dFdx(h) * r1 + dFdy(h) * r2);
		NORMAL = normalize(abs(det) * NORMAL - bump * grad);
		albedo *= 0.9 + 0.2 * h;
	}

	// Undersides sit in the shadow of the creature and the ground: darken whatever faces down.
	vec3 wn = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
	float occlusion = mix(0.55, 1.0, smoothstep(-0.85, 0.35, wn.y));

	// Read at any distance: a little brighter the further off, so a figure doesn't sink into the haze.
	float far = smoothstep(25.0, 150.0, length(VERTEX));
	ALBEDO = albedo * occlusion * (1.0 + far_lift * far);
	ROUGHNESS = roughness_value;
	SPECULAR = 0.3;
	// A rim of the sun's light round the outline, stronger with distance: the silhouette holds.
	RIM = rim_strength * (0.45 + 0.55 * far);
	RIM_TINT = 0.6;
	if (horn) {
		ROUGHNESS = 0.4;
		SPECULAR = 0.5;
	} else if (shell) {
		ROUGHNESS = 0.35;
		SPECULAR = 0.5;
		CLEARCOAT = 0.5;
		CLEARCOAT_ROUGHNESS = 0.3;
	} else if (membrane) {
		ROUGHNESS = 0.8;
		BACKLIGHT = albedo * 0.6;
	} else if (eye) {
		ROUGHNESS = 0.08;
		SPECULAR = 0.9;
		RIM = 0.0;
	}

	float g = COLOR.a * glow_strength;
	vec3 em = mix(base, glow_color, glow_tint) * g * 2.6;
	// Eyes catch a little light even in shadow: eyeshine, the thing you notice at dusk.
	if (eye) em += base * 0.6;
	em += accent * m * veins_glow * (0.55 + 0.45 * sin(TIME * 2.7 + lp.x * 3.0 + lp.z * 2.0)) * 1.8;
	em += vec3(1.0, 0.35, 0.2) * hurt * 0.9;
	em *= 1.0 + wake * 2.5;
	if (eye) em += mix(vec3(0.9, 0.85, 0.6), glow_color, glow_tint * step(0.01, glow_strength)) * wake * 2.2;
	EMISSION = em;
}
