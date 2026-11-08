// Rock faces: what a cliff is made of, drawn from the world's own minerals and soil (RockLook.cs sets
// it all from the world). Included by terrain.gdshader after its uniforms; one call, rock_face, gives the
// colour of bare rock at a point and the normal its relief shades with, and lays scree at a cliff's foot.
//
// Everything is a function of the point in the world (no texture is projected, so nothing stretches down
// a face): beds of rock in horizontal strata, some of them hard enough to stand out as ledges with a
// shadowed undercut and dark streaks weathered down from them; vertical flutes worn by water; joints that
// break the rock into blocks; dark varnish streaking down from a cliff's rim; a hard caprock at its top;
// a wet, salt-stained foot by the sea; lichen where anything grows. How much of each a face shows is the
// region's landform (rock_style, rock_beds), so mesas, canyon walls, sea cliffs and mountain crags differ by
// rule. Fine detail eases out by the size of a pixel on the ground, so nothing shimmers far off.
//
// Frequencies are multiples of 1/32 m, so the noise wraps with the planet (sizes are multiples of 4 km).

// The world's rock and the beds in it, in sRGB as the palette has them.
uniform vec3 rock_base = vec3(0.45, 0.42, 0.38);
uniform vec3 rock_pale = vec3(0.62, 0.57, 0.5);
uniform vec3 rock_dark = vec3(0.3, 0.28, 0.26);
uniform vec3 rock_mineral = vec3(0.5, 0.38, 0.3);
uniform vec3 rock_mineral2 = vec3(0.45, 0.42, 0.36);
uniform vec3 rock_soil = vec3(0.5, 0.44, 0.36);
// What weathering paints down a face (desert varnish on a dry world, damp and algae on a wet one), and
// what grows on rock and how much (none on a world without life).
uniform vec3 rock_varnish = vec3(0.16, 0.13, 0.11);
uniform vec3 rock_life = vec3(0.36, 0.4, 0.26);
uniform float rock_life_amount = 0.0;
// By region (a texel every planet/256): strata, flutes, joints and caprock (rgba); and the beds' height
// offset (r, a slow dip across the land, 0.5 none, +-24 m) and thickness (g, 0.5-4 m).
uniform sampler2D rock_style : filter_linear, repeat_enable, hint_default_black;
uniform sampler2D rock_beds : filter_linear, repeat_enable, hint_default_black;
uniform float rock_style_on = 0.0;
uniform vec4 rock_style_default = vec4(0.45, 0.3, 0.4, 0.0);
// Whether UV2 is the cliff the mesh knows: how far the ground rises above a vertex and falls below it within
// about 16 m (to the cliff's top and its foot). The horizon's UV2 is its own; there it is unknown.
uniform float rock_uv2 = 0.0;

// How much of a pixel fw wide a band |x| < w covers.
float rk_band(float x, float w, float fw) {
	fw = max(fw, 1e-3);
	return clamp((min(x + fw * 0.5, w) - max(x - fw * 0.5, -w)) / fw, 0.0, 1.0);
}

float rk_hash(float k, float salt) {
	return hash3(vec3(k, salt, 7.0));
}

// Smooth noise along one axis (beds' thickness, a streak's length).
float rk_noise1(float x, float salt) {
	float i = floor(x), f = x - i;
	f = f * f * (3.0 - 2.0 * f);
	return mix(rk_hash(i, salt), rk_hash(i + 1.0, salt), f);
}

// 3D noise at its own frequency along x and z (f.x) and y (f.y): stretched upright for flutes and streaks.
float rk_noise(vec3 p, vec2 f, float salt) {
	return pnoise(vec3(p.x * f.x, p.y * f.y + salt, p.z * f.x), planet_size * f.x);
}

// Two octaves of it, the second at twice the frequency: thresholded, one octave of value noise shows its
// lattice as squarish blocks.
float rk_noise2(vec3 p, vec2 f, float salt) {
	return rk_noise(p, f, salt) * 0.62 + rk_noise(p, f * 2.0, salt + 17.0) * 0.38;
}

vec3 rk_lin(vec3 c) {
	return srgb_to_linear(c);
}

// The bed of rock at a height: its index, how far up through it (0-1), its thickness.
vec3 rk_bed(float sy, float thick) {
	float u = sy / thick;
	u += 0.55 * (rk_noise1(u * 0.37, 3.0) - 0.5) + 0.25 * (rk_noise1(u * 1.3, 5.0) - 0.5);
	float k = floor(u);
	return vec3(k, u - k, thick * (0.7 + 0.6 * rk_hash(k, 9.0)));
}

// A bed's colour, as a share of the world's rock: plain rock, a pale bed, a dark one, the world's minerals,
// or one stained by its soil; each a little lighter or darker than the last.
vec3 rk_bed_tint(float k, vec3 rock, float layered) {
	float pick = rk_hash(k, 1.0), tone = rk_hash(k, 2.0);
	vec3 c = rock;
	if (pick < 0.16) c = rk_lin(rock_pale);
	else if (pick < 0.26) c = rk_lin(rock_dark);
	else if (pick < 0.36) c = rk_lin(rock_mineral);
	else if (pick < 0.43) c = rk_lin(rock_mineral2);
	else if (pick < 0.53) c = rk_lin(rock_soil);
	vec3 ratio = clamp(c / max(rock, vec3(0.01)), vec3(0.55), vec3(1.6));
	return mix(vec3(1.0), ratio * (0.92 + 0.16 * tone), layered * 0.6);
}

// Bare rock at p: its colour from the vertex's own (base: the world's rock, an ore, a dug wall) and the
// relief its normal takes (bump, world space). n is the shading normal in world space; cliff the UV2
// rise and fall; fine the shader's fine noise; grain its close-up grain (0 far off); bare how bare.
// soil is laid with scree at the foot of a cliff. eye is the camera.
vec3 rock_face(vec3 base, vec3 p, vec3 view_normal, mat4 inv_view, vec2 cliff, float fine, float grain, float bare, inout vec3 soil, out vec3 bump) {
	bump = vec3(0.0);
	bool known = rock_uv2 > 0.5;
	float rise = known ? cliff.x : 1e3, fall = known ? cliff.y : 1e3;
	// The size of a pixel on the ground, m, and up the face: detail finer than a few of them gives way to
	// its mean. (Taken before anything branches, so neighbouring pixels agree.)
	float fw = max(length(fwidth(p)), 1e-3);
	float fy = max(fwidth(p.y), 1e-3);
	vec3 rock = rk_lin(rock_base);

	// Scree and talus at a cliff's foot: broken rock fallen from the face, lighter where it broke fresh,
	// thinning out away from it in a ragged edge, pebbles only up close.
	float under = known && bare < 0.999 ? smoothstep(5.0, 16.0, rise) * (1.0 - smoothstep(0.6, 3.5, fall)) : 0.0;
	if (under > 0.0) {
		float ragged = smoothstep(0.3, 0.7, rk_noise(p, vec2(0.125, 0.125), 11.0) * 0.7 + fine * 0.3 + under * 0.45 - 0.2);
		float pebbles = rk_noise(p, vec2(1.5, 1.5), 13.0) - 0.5;
		vec3 fallen = mix(rock, rk_lin(rock_pale), 0.3) * (0.86 + 0.3 * fine + pebbles * 0.5 * (1.0 - smoothstep(0.02, 0.1, fw)));
		soil = mix(soil, fallen, under * ragged * 0.8);
	}
	if (bare <= 0.0) return base;

	vec3 n = normalize((inv_view * vec4(view_normal, 0.0)).xyz);
	vec4 style = rock_style_on > 0.5 ? textureLod(rock_style, p.xz / planet_size, 0.0) : rock_style_default;
	vec2 beds = rock_style_on > 0.5 ? textureLod(rock_beds, p.xz / planet_size, 0.0).rg : vec2(0.5, 0.35);
	float layered = style.r, flutes = style.g, joints = style.b, cap = style.a;
	float steep = 1.0 - smoothstep(0.35, 0.62, n.y);   // a face, not a slope

	// The beds: a slow dip across the land, a wander along the face, thickness by the region.
	float sy = p.y + (beds.r - 0.5) * 48.0 + (rk_noise(p, vec2(0.0625, 0.0), 21.0) - 0.5) * 1.4;
	float thick = mix(0.5, 4.0, beds.g);
	vec3 bed = rk_bed(sy, thick);
	float beds_seen = 1.0 - smoothstep(thick * 0.25, thick * 0.9, fy);  // far off, beds blur to their mean
	vec3 tint = mix(vec3(1.0), rk_bed_tint(bed.x, rock, layered), beds_seen);
	// Graded bedding: each bed a shade lighter toward its top.
	tint *= 1.0 + (bed.y - 0.5) * 0.12 * layered * beds_seen;
	// Bedding planes: hairlines between beds.
	float to_plane = min(bed.y, 1.0 - bed.y) * bed.z;
	tint *= 1.0 - 0.22 * layered * step(0.4, rk_hash(bed.x + step(0.5, bed.y), 4.0)) * rk_band(to_plane, 0.03 + 0.03 * layered, fy) * (1.0 - smoothstep(0.04, 0.25, fy));
	vec3 c = base * tint;

	// Ledges: a hard bed stands out of the face. Its top catches the light; under it, the soft bed is cut
	// back into a shadowed undercut, and the water off the ledge streaks the face below it dark.
	float hard = step(rk_hash(bed.x, 3.0), 0.12 + 0.3 * layered);
	float hard_above = step(rk_hash(bed.x + 1.0, 3.0), 0.12 + 0.3 * layered);
	float ledge = hard * smoothstep(0.75, 0.95, bed.y) * steep * layered;
	float undercut = hard_above * smoothstep(0.62, 0.92, bed.y) * steep * layered;
	float relief_seen = 1.0 - smoothstep(0.15, 0.6, fy / max(bed.z, 0.1));
	c *= 1.0 + 0.16 * ledge * relief_seen - 0.38 * undercut * beds_seen;
	bump += vec3(0.0, 0.55 * ledge - 0.45 * undercut, 0.0) * relief_seen;
	// (How far below the nearest hard bed above, over up to three beds, so a streak runs on past the
	// soft beds under it instead of stopping square at the next bedding plane.)
	float below_ledge = 1e3;
	for (int j = 3; j >= 1; j--)
		if (rk_hash(bed.x + float(j), 3.0) < 0.12 + 0.3 * layered) below_ledge = (float(j) - bed.y) * bed.z;
	// (Not under every stretch of every ledge: where the water off it ran, in patches along the face.)
	float runs = smoothstep(0.4, 0.7, rk_noise(p, vec2(0.0625, 0.0625), 33.0));
	float drip = smoothstep(0.58, 0.86, rk_noise2(p, vec2(0.75, 0.03125), 31.0)) * exp(-below_ledge / 6.0) * steep * layered * runs * 0.55;

	// Varnish down from the rim: dark streaks of differing length hanging from the top of the face.
	float streak_len = 5.0 + 22.0 * rk_noise(p, vec2(0.0625, 0.0), 41.0);
	float from_rim = known ? smoothstep(0.5, 2.5, rise) * (1.0 - smoothstep(streak_len, streak_len + 8.0, rise)) : 0.5;
	float streaks = smoothstep(0.5, 0.8, rk_noise2(p, vec2(0.25, 0.03125), 43.0) * 0.8 + fine * 0.2) * from_rim * steep;
	float varnish = clamp(max(streaks * (0.35 + 0.65 * flutes), drip), 0.0, 1.0);
	c = mix(c, c * clamp(rk_lin(rock_varnish) / max(rock, vec3(0.01)), vec3(0.15), vec3(1.2)), varnish * 0.75);

	// Flutes: upright grooves worn by running water, a metre or two apart, wandering.
	float flute_seen = 1.0 - smoothstep(0.08, 0.35, fw);
	if (flutes > 0.02 && steep > 0.0 && flute_seen > 0.0) {
		vec3 t = normalize(vec3(-n.z, 0.0, n.x) + vec3(1e-4, 0.0, 0.0));
		float e = 0.12;
		float g0 = rk_noise(p - t * e, vec2(0.75, 0.0625), 51.0), g1 = rk_noise(p + t * e, vec2(0.75, 0.0625), 51.0);
		float gm = (g0 + g1) * 0.5;
		float depth = flutes * steep * flute_seen * smoothstep(0.25, 0.6, rk_noise(p, vec2(0.0625, 0.0625), 53.0));
		bump += t * ((g1 - g0) / (2.0 * e)) * 1.1 * depth;
		c *= 1.0 - 0.22 * depth * (1.0 - gm);
	}

	// Joints: the rock broken into blocks a bed high and a few metres long, each its own shade and tilt;
	// the cracks between them dark. Crags are all joints, a mesa's beds only a few.
	// (Whole blocks across the planet each way, so they wrap with it.) Only some joints are open cracks,
	// a different few in each bed, so a face never reads as masonry.
	vec2 count = floor(planet_size / mix(9.0, 2.4, joints));
	vec2 jw = planet_size / count;
	// (Each joint wanders a little up the face and along it, so none runs ruler-straight.)
	vec2 wander = vec2(rk_noise(p, vec2(0.25, 0.125), 63.0), rk_noise(p, vec2(0.25, 0.125), 67.0)) - 0.5;
	vec2 q = (p.xz + wander * jw * 0.35 + rk_hash(bed.x, 6.0) * jw) / jw;
	vec2 cell = mod(floor(q), count);
	float block = hash3(vec3(cell, bed.x));
	float crack_seen = 1.0 - smoothstep(0.02, 0.12, fw);
	vec2 inb = fract(q) * jw;
	vec2 joint_at = mod(floor(q) + step(jw * 0.5, inb), count);   // the nearer joint each way
	float open_x = step(hash3(vec3(joint_at.x, bed.x, 3.0)), 0.15 + 0.5 * joints);
	float open_z = step(hash3(vec3(joint_at.y, bed.x, 5.0)), 0.15 + 0.5 * joints);
	float to_joint = min(mix(1e3, min(inb.x, jw.x - inb.x), open_x), mix(1e3, min(inb.y, jw.y - inb.y), open_z));
	float joint = rk_band(to_joint, 0.012 + 0.03 * joints, fw) * crack_seen * (0.25 + 0.75 * joints) * steep * steep;
	// And fractures that wander across the beds: thin, dark, mostly upright.
	float frac = abs(rk_noise(p, vec2(0.1875, 0.0625), 61.0) - 0.5);
	float fracture = rk_band(frac, 0.006 + 0.01 * joints, fw * 0.3) * crack_seen * joints * steep;
	c *= (1.0 + (block - 0.5) * 0.12 * joints * beds_seen) * (1.0 - 0.4 * max(joint, fracture));
	vec3 tilt = vec3(hash3(vec3(cell, bed.x + 17.0)) - 0.5, 0.0, hash3(vec3(cell, bed.x + 29.0)) - 0.5);
	bump += tilt * 0.3 * joints * joints * steep * relief_seen;

	// The caprock: a hard, paler bed along the top of the face, rounded at the lip, with a shadowed
	// line under it where the softer rock below has weathered back.
	if (known && cap > 0.01) {
		float thick_cap = 1.8 + 3.0 * rk_noise(p, vec2(0.03125, 0.0), 71.0);
		float in_cap = (1.0 - smoothstep(thick_cap - 0.4, thick_cap + 0.2, rise)) * cap;
		float under_cap = rk_band(rise - thick_cap - 0.6, 0.6, fy) * cap * steep;
		vec3 capstone = base * clamp(rk_lin(rock_pale) / max(rock, vec3(0.01)), vec3(0.5), vec3(2.0)) * (0.92 + 0.16 * fine);
		c = mix(c, capstone, in_cap * 0.85);
		c *= 1.0 - 0.45 * under_cap;
		float lip = in_cap * (1.0 - smoothstep(0.3, 1.4, rise)) * smoothstep(0.25, 0.5, n.y) * (1.0 - smoothstep(0.75, 0.95, n.y));
		c *= 1.0 + 0.12 * lip;
		bump += vec3(0.0, 0.4 * in_cap * steep - 0.35 * under_cap, 0.0);
	}

	// By the sea: a dark, wet band where the waves reach, and salt crusting pale above it.
	if (p.y < 7.0) {
		float wash = (rk_noise(p, vec2(0.25, 0.25), 81.0) - 0.5) * 0.8;
		float wet = (1.0 - smoothstep(1.2, 2.6, p.y + wash)) * steep;
		float salt = rk_band(p.y + wash - 3.4, 1.0, fy) * steep * smoothstep(0.42, 0.68, rk_noise2(p, vec2(0.75, 0.375), 83.0));
		c = mix(c, c * 0.5, wet);
		c = mix(c, c * 1.25 + 0.03, salt * 0.6);
	}

	// Lichen and moss where anything grows: blotches on the face, more on ledges and in damp hollows.
	if (rock_life_amount > 0.0) {
		float blot = smoothstep(0.56, 0.78, rk_noise2(p, vec2(0.375, 0.375), 91.0) * 0.75 + fine * 0.25);
		float where = rock_life_amount * (0.35 + 0.65 * max(ledge, undercut)) * (0.5 + 0.5 * smoothstep(0.2, 0.7, n.y + 0.4));
		c = mix(c, rk_lin(rock_life) * (0.75 + 0.4 * fine), blot * where * 0.55);
	}

	// Grain up close.
	c *= 1.0 + grain * 0.35;
	return c;
}

// The shading normal (view space) with the rock's relief worked in, never turned from the eye.
vec3 rock_normal(vec3 normal, vec3 bump, float bare, mat4 view, mat4 inv_view, vec3 p, vec3 eye) {
	if (bare <= 0.0 || dot(bump, bump) < 1e-8) return normal;
	vec3 n = normalize((inv_view * vec4(normal, 0.0)).xyz);
	vec3 m = normalize(n + bump * bare);
	vec3 to_eye = normalize(eye - p);
	float seen = dot(m, to_eye), was = dot(n, to_eye);
	float least = min(0.05, was);
	if (seen < least) m = normalize(m + to_eye * (least - seen));
	return normalize((view * vec4(m, 0.0)).xyz);
}
