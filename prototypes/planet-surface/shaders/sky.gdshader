// The sky, from the world's own data (see SkySystem): a clear-sky gradient from its air, the
// sun and its glow in the star's colour, sunsets toward a low sun, moons lit by the sun (so they
// show phases), a neighbouring giant with bands and maybe rings, stars and a nebula at night,
// and a layer of clouds you can see the shape of: a slab of cloud raymarched against the sun,
// with flat bases, lit tops and dark undersides, computed at half resolution.
//
// The Mobile renderer keeps the reduced-resolution sky buffers in RGB10A2, so their alpha has
// only four levels: clouds' edges banded into blocks. So the alpha never goes through ALPHA: the
// half-resolution pass stores the clouds already laid over the plain sky gradient (colour only),
// and the quarter-resolution pass marches density alone for how much of what is behind them
// (stars, moons, the giant, the sun) the clouds hide.
shader_type sky;
render_mode use_half_res_pass, use_quarter_res_pass;

global uniform vec4 weather_flash;   // direction to the last strike, and its brightness

uniform vec3 zenith_day : source_color;
uniform vec3 horizon_day : source_color;
uniform vec3 zenith_night : source_color;
uniform vec3 sunset : source_color;
uniform vec3 haze : source_color;
uniform vec3 star_color : source_color;
uniform float sun_radius = 0.01;
uniform float sun_energy = 1.3;
uniform float density = 1.0;
uniform float stars_by_day = 0.0;
uniform mat3 star_turn = mat3(1.0);
uniform float overcast = 0.0;          // weather greying the whole sky
uniform vec3 overcast_color : source_color = vec3(0.5);

uniform bool nebula = false;
uniform vec3 nebula_a : source_color = vec3(0.5, 0.2, 0.6);
uniform vec3 nebula_b : source_color = vec3(0.2, 0.4, 0.7);

uniform int moon_count = 0;
uniform vec3 moon_dir[3];
uniform float moon_radius[3];
uniform vec3 moon_color[3] : source_color;

uniform bool giant = false;
uniform vec3 giant_dir = vec3(0.0, 1.0, 0.0);
uniform float giant_radius = 0.1;
uniform vec3 giant_color : source_color = vec3(0.8, 0.7, 0.5);
uniform vec3 giant_band : source_color = vec3(0.5, 0.4, 0.3);
uniform bool rings = false;
uniform vec3 ring_color : source_color = vec3(0.9, 0.85, 0.7);
uniform vec3 ring_normal = vec3(0.0, 1.0, 0.0);

uniform sampler2D cloud_noise : filter_linear_mipmap, repeat_enable;
uniform float cloud_cover = 0.4;
uniform float cloud_dark = 0.0;
uniform float cloud_base = 1600.0;
uniform float cloud_thick = 900.0;
uniform vec2 cloud_scale = vec2(8000.0, 8000.0);   // world metres per noise tile: divides the planet both ways
uniform vec2 cloud_drift = vec2(0.0);
uniform vec3 cloud_lit : source_color = vec3(1.0);
uniform vec3 cloud_shade : source_color = vec3(0.5, 0.55, 0.62);
uniform vec3 cloud_underglow : source_color = vec3(0.0);   // light from the ground: lava under ash
uniform int debug_view = 0;   // 1: cloud coverage, 2: cloud colour, 3: the night backdrop alone, brightened

float hash31(vec3 p) {
	p = fract(p * vec3(443.897, 441.423, 437.195));
	p += dot(p, p.yzx + 19.19);
	return fract((p.x + p.y) * p.z);
}

// How much of the sun's light gets through the air, by its height: all of it from about 17° up,
// under half at the horizon (SkyRig.AirMass, the same curve for the sunlight on the ground).
float sun_air(float sun_h) {
	return 0.45 + 0.55 * smoothstep(0.02, 0.3, sun_h);
}

vec3 sky_color(vec3 dir, vec3 sun) {
	float up = dir.y;
	float sun_h = sun.y;
	float day = smoothstep(-0.2, 0.15, sun_h);
	float bright = 0.25 + 0.75 * smoothstep(-0.05, 0.45, sun_h);

	vec3 zen = mix(zenith_night, zenith_day * bright, day);
	vec3 hor = mix(zenith_night * 1.6 + haze * 0.02, horizon_day * bright, day);
	float a = pow(clamp(up, 0.0, 1.0), 0.42 / density);
	vec3 col = mix(hor, zen, a);

	// Sunset and sunrise: warm light piling up toward a low sun, strongest in thick air.
	vec2 flat_dir = normalize(dir.xz + vec2(1e-5));
	vec2 flat_sun = normalize(sun.xz + vec2(1e-5));
	// Clamped: straight away from the sun the dot can round past -1, and pow of a negative is NaN,
	// which drew a dotted dark line down the sky on the anti-solar azimuth at dawn and dusk.
	float toward = clamp(dot(flat_dir, flat_sun) * 0.5 + 0.5, 0.0, 1.0);
	float low = smoothstep(0.45, 0.0, sun_h) * smoothstep(-0.28, -0.02, sun_h);
	col += sunset * low * pow(toward, 2.5) * exp(-max(up, 0.0) * 5.0 / density) * (0.45 + 0.35 * density);
	col = mix(col, col * vec3(0.9, 0.85, 0.82) + sunset * 0.08, low * 0.5);

	// Aerosol glow round the sun. Near the horizon its light has come a long way through the air
	// (sun_through): the wide halo dims with it, so a low sun is a contained glow, not a white sky.
	float sd = dot(dir, sun);
	float sun_through = sun_air(sun_h);
	// Low down both halos thin with the same path and take the sunset's colour, so a setting sun
	// sits in a warm contained glow you can look at, never a white blaze.
	vec3 halo = mix(sunset, star_color, smoothstep(0.03, 0.38, sun_h));
	col += halo * (pow(max(sd, 0.0), 8.0) * 0.18 * sun_through + pow(max(sd, 0.0), 90.0) * 0.5 * sun_through * sun_through) * day * density;

	// Below the horizon: the haze, darker downward.
	if (up < 0.0) col = mix(col, haze * bright * day * 0.55 + zenith_night, clamp(-up * 6.0, 0.0, 1.0));
	return col;
}

// The night backdrop has no seams, poles or corners anywhere on the sky: everything is looked up
// along the 3D direction itself. (A longitude-latitude map pinched the nebula into a starburst at
// its pole and tore it along a meridian; stars cut from a 3D lattice by a sphere left a star-free
// hole and rings at each of the six axis directions, and thinned toward them.)
uniform sampler3D nebula_noise : filter_linear, repeat_disable;    // broad clouds of it
uniform sampler3D nebula_detail : filter_linear, repeat_disable;   // its finer structure

// A 3D noise texture looked up along a sky direction, inside one tile (no wrap, so no tiling seam),
// with the trilinear weights eased so its texel grid never shows as creases in a smooth glow.
float sky_noise(sampler3D tex, vec3 d) {
	vec3 size = vec3(textureSize(tex, 0));
	vec3 t = (d * 0.45 + 0.5) * size - 0.5;
	vec3 i = floor(t), f = t - i;
	f = f * f * (3.0 - 2.0 * f);
	return textureLod(tex, (i + f + 0.5) / size, 0.0).r;
}

// Stars: points scattered through a thin shell of a 3D lattice, STAR_SHELL cells out, each drawn
// where the view ray passes within its radius. Every star is placed and kept or dropped by itself
// (its own place, inside its cell, and whether it lies in the shell), never by where the ray cuts
// a cell, so the field is equally dense in every direction. A star that close to the ray lies
// within half a cell of the ray's own point on each axis: the two cells round it, each way, hold it.
const float STAR_SHELL = 260.0;
const float STAR_DEPTH = 0.35;   // half the shell's thickness, cells
const float STAR_RADIUS = 0.32;  // cells

vec3 stars(vec3 d) {
	vec3 p = d * STAR_SHELL;
	vec3 base = floor(p + 0.28) - 1.0;
	vec3 col = vec3(0.0);
	for (int i = 0; i < 8; i++) {
		vec3 cell = base + vec3(float(i & 1), float((i >> 1) & 1), float((i >> 2) & 1));
		float h = hash31(cell);
		if (h <= 0.985) continue;
		vec3 star = cell + 0.2 + 0.6 * vec3(hash31(cell + 7.7), hash31(cell + 13.1), hash31(cell + 19.3));
		if (abs(length(star) - STAR_SHELL) > STAR_DEPTH) continue;
		float off = length(star - d * dot(star, d));   // from the ray, in cells
		float b = smoothstep(STAR_RADIUS, 0.0, off) * pow((h - 0.985) / 0.015, 6.0);   // a few bright, most faint
		col += vec3(0.85 + 0.15 * hash31(cell + 3.1), 0.9, 1.0) * b * 4.0;
	}
	return col;
}

vec3 night_sky(vec3 dir, vec3 sun) {
	vec3 d = star_turn * dir;
	vec3 col = vec3(0.0);
	if (nebula) {
		float n = sky_noise(nebula_noise, d);
		float m = sky_noise(nebula_detail, d);
		float off_band = (d.y - 0.15 + 0.3 * (n - 0.5)) * 2.2;
		float band = exp(-off_band * off_band);   // squared, not pow(): its base goes negative
		col += mix(nebula_a, nebula_b, m) * pow(max(n * m, 0.0), 1.5) * band * 0.9;
	}
	return col + stars(d);
}

// Moons and the giant: discs lit from the sun's side, so they show phases; the giant has bands
// and, if the system gives it rings, a ring plane that passes behind it and in front of it.
// Colours are premultiplied by alpha so the layers stack: back ring, disc, front ring.
vec4 over(vec4 front, vec4 back) {
	return vec4(front.rgb + back.rgb * (1.0 - front.a), front.a + back.a * (1.0 - front.a));
}

vec4 lit_disc(vec3 dir, vec3 centre, float radius, vec3 sun, out vec3 normal) {
	float c = dot(dir, centre);
	float r = acos(clamp(c, -1.0, 1.0));
	normal = -centre;
	if (r >= radius) return vec4(0.0);
	vec3 helper = abs(centre.y) > 0.95 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
	vec3 east = normalize(cross(helper, centre));
	vec3 north = cross(centre, east);
	vec2 q = vec2(dot(dir, east), dot(dir, north)) / sin(radius);
	float z = sqrt(max(0.0, 1.0 - dot(q, q)));
	normal = normalize(east * q.x + north * q.y - centre * z);
	float edge = smoothstep(radius, radius * 0.985, r);
	return vec4(vec3(clamp(dot(normal, sun), 0.0, 1.0)), edge);
}

vec4 bodies(vec3 dir, vec3 sun) {
	vec4 result = vec4(0.0);
	if (giant) {
		vec3 axis = normalize(ring_normal);
		vec4 back = vec4(0.0), front = vec4(0.0);
		if (rings) {
			// Where the view ray crosses the ring plane through the giant's centre, in giant radii.
			float denom = dot(dir, axis);
			float t = dot(giant_dir, axis) / (abs(denom) > 1e-4 ? denom : 1e-4);
			if (t > 0.0) {
				float rr = length(dir * t - giant_dir) / sin(giant_radius);
				float ring = smoothstep(1.45, 1.5, rr) * (1.0 - smoothstep(2.35, 2.42, rr));
				ring *= 0.6 + 0.4 * sin(rr * 41.0) * sin(rr * 13.0);
				ring *= 1.0 - (smoothstep(1.93, 1.97, rr) - smoothstep(2.03, 2.07, rr)) * 0.85; // a dark gap
				float lit = 0.6 + 0.4 * clamp(abs(dot(axis, sun)) * 1.5, 0.0, 1.0);
				vec4 rc = vec4(ring_color * lit * ring * 0.85, ring * 0.85);
				if (t < 1.0) front = rc; else back = rc;
			}
		}
		vec3 n;
		vec4 disc = lit_disc(dir, giant_dir, giant_radius, sun, n);
		if (disc.a > 0.0) {
			float lat = dot(n, axis);
			float bands = sin(lat * 19.0 + sin(lat * 7.0) * 1.6) * 0.5 + 0.5;
			vec3 surface = mix(giant_color, giant_band, bands * 0.7);
			disc.rgb = surface * (0.03 + 0.97 * disc.r) * disc.a;
		}
		result = over(front, over(disc, back));
	}
	for (int i = 0; i < moon_count; i++) {
		vec3 n;
		vec4 m = lit_disc(dir, moon_dir[i], moon_radius[i], sun, n);
		if (m.a <= 0.0) continue;
		float craters = 0.82 + 0.18 * texture(cloud_noise, n.xz * 0.7 + float(i) * 0.31).r;
		m.rgb = moon_color[i] * craters * (0.025 + 1.1 * m.r) * m.a;
		result = over(m, result);
	}
	return result;
}

uniform sampler3D cloud_detail : filter_linear_mipmap, repeat_enable;

// Cloud density at a point: a coverage map from 2D noise (where clouds are, and how tall), a
// height profile with flat bases and rounded tops that rise higher where the cloud is denser,
// and 3D noise eroding the edges into billows so nothing reads as an extruded pillar.
float cloud_density(vec3 p, bool cheap) {
	vec2 uv = (p.xz + cloud_drift) / cloud_scale;
	float n = texture(cloud_noise, uv).r * 0.65 + texture(cloud_noise, uv * 2.0 + 0.31).r * 0.35;
	float cover = mix(0.12, 1.05, cloud_cover);
	float coverage = n - (1.0 - cover);
	if (coverage <= 0.0) return 0.0;
	float h = (p.y - cloud_base) / cloud_thick;
	float top = clamp(0.2 + coverage * 2.6, 0.2, 1.0);
	float profile = smoothstep(0.0, 0.07, h) * (1.0 - smoothstep(top * 0.55, top, h));
	float d = coverage * 3.2 * profile;
	if (!cheap) {
		vec3 q = (p + vec3(cloud_drift.x, 0.0, cloud_drift.y) * 1.3) / (cloud_scale.x * 0.125);
		float detail = texture(cloud_detail, q).r * 0.65 + texture(cloud_detail, q * 3.0).r * 0.35;
		d -= detail * (0.55 - 0.25 * h) * (1.0 - cloud_cover * 0.5);
	}
	return clamp(d * 1.8, 0.0, 1.0);
}

// Where along its first step each pixel's march starts, 0..1: interleaved gradient noise on the
// pixel grid, so neighbouring pixels sample the slab at well-spread depths. (A hash of the view
// direction, over only a third of a step, left the steps showing as combs and streaks along
// cloud edges.)
float march_jitter(vec2 pixel) {
	return fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715))));
}

// The clouds along a view ray: their light over the given sky colour, and their opacity. With
// lit false only the opacity is marched (no light samples): the cheap pass for occlusion.
vec4 march_clouds(vec3 ro, vec3 rd, vec3 sun, vec3 sky, bool lit, float jitter) {
	if (cloud_cover < 0.02) return vec4(sky, 0.0);
	float base = cloud_base, top = cloud_base + cloud_thick;
	float t0, t1;
	if (abs(rd.y) < 1e-3) {
		if (ro.y < base || ro.y > top) return vec4(sky, 0.0);
		t0 = 0.0;
		t1 = 2400.0;
	} else {
		float ta = (base - ro.y) / rd.y, tb = (top - ro.y) / rd.y;
		t0 = max(min(ta, tb), 0.0);
		t1 = max(ta, tb);
	}
	// The layer sits low, so its far reach is scaled to its height: about 16 km and 2 km of path.
	if (t1 <= 0.0 || t0 > 16000.0) return vec4(sky, 0.0);
	t1 = min(t1, t0 + 2000.0);
	// Steps by the length of the path through the slab: a few dozen straight up, more toward the
	// horizon, where the path is long and too few would slice the clouds into layers.
	int steps = int(clamp((t1 - t0) / 28.0, 24.0, 64.0));
	float dt = (t1 - t0) / float(steps);
	float t = t0 + dt * jitter;
	float trans = 1.0;
	vec3 light = vec3(0.0);
	float day = smoothstep(-0.15, 0.1, sun.y);
	vec3 sun_tint = mix(vec3(1.0), star_color, 0.4);
	vec3 sun_light = mix(sunset, sun_tint, smoothstep(0.0, 0.3, sun.y)) * day * sun_energy * 1.1;
	vec3 ambient = mix(cloud_shade, sky, 0.35) * (0.12 + 0.88 * day);
	float flash = weather_flash.w;
	for (int i = 0; i < steps; i++) {
		vec3 p = ro + rd * t;
		float d = cloud_density(p, false);
		if (d > 0.01) {
			// A thinner slab packs the same cloud into less path: as solid, seen through.
			float ext = d * dt * 0.016 * clamp(700.0 / cloud_thick, 1.0, 2.0);
			if (lit) {
				// Light reaching this point from the sun, through two cheaper samples of cloud.
				float reach = clamp(cloud_thick / 900.0, 0.3, 1.0);
				float toward = cloud_density(p + sun * 80.0 * reach, true) + cloud_density(p + sun * 240.0 * reach, true) * 0.8;
				float beer = exp(-toward * 1.6);
				float powder = 1.0 - exp(-d * 3.0);
				float hgt = clamp((p.y - cloud_base) / cloud_thick, 0.0, 1.0);
				vec3 lum = ambient * (0.4 + 0.6 * hgt) + cloud_lit * sun_light * beer * mix(0.55, 1.0, powder);
				lum *= 1.0 - cloud_dark * 0.78 * (1.0 - hgt * 0.4);
				lum += cloud_underglow * (1.0 - hgt) * (1.0 - hgt);
				lum += vec3(0.75, 0.72, 1.0) * flash * 2.5 * (1.0 - hgt * 0.3);
				light += trans * lum * (1.0 - exp(-ext));
			}
			trans *= exp(-ext);
			if (trans < 0.03) break;
		}
		t += dt;
	}
	float alpha = 1.0 - trans;
	// Distant clouds thin into the haze along the horizon: they fade in opacity, so whatever is
	// behind them (a moon, a giant) shows through properly; their colour leans toward the sky's.
	float fade = exp(-t0 / (6500.0 * density)) * (1.0 - smoothstep(9000.0, 16000.0, t0));
	// The low layer's far clouds would close to thin streaks along the horizon: they thin out first.
	if (ro.y < base) fade *= smoothstep(0.015, 0.09, rd.y);
	else fade *= 0.3 + 0.7 * smoothstep(0.0, 0.07, abs(rd.y)); // in or over the layer, its edge on the horizon stays soft
	vec3 col = alpha > 0.001 ? light / alpha : sky;
	col = mix(mix(sky, col, 0.5), col, fade);
	alpha *= fade;
	return vec4(mix(sky, col, alpha), alpha);
}

// ---- The way out ---------------------------------------------------------------------------
// As the air thins with height the sky fades into space's own backdrop: the same stars, nebula
// and glare the orbit's sky draws (planet-generator's space shader, copied here because a sky
// shader can't include another's), so when the orbit's sky takes over nothing changes.

uniform float space_fade = 0.0;        // 0 the planet's sky, 1 space
uniform mat3 space_turn = mat3(1.0);   // the world's directions into the backdrop's
uniform vec3 space_sun = vec3(0.0, 1.0, 0.0);
uniform vec3 space_nebula : source_color = vec3(0.16, 0.1, 0.35);
uniform vec3 space_nebula_b : source_color = vec3(0.1, 0.2, 0.3);

float hash3(vec3 p) {
	uvec3 q = uvec3(ivec3(floor(p))) * uvec3(1597334673u, 3812015801u, 2798796415u);
	return float((q.x ^ q.y ^ q.z) * 1597334673u) * 2.3283064e-10;
}

float vnoise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float a = mix(hash3(i), hash3(i + vec3(1, 0, 0)), u.x);
	float b = mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), u.x);
	float c = mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), u.x);
	float d = mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), u.x);
	return mix(mix(a, b, u.y), mix(c, d, u.y), u.z);
}

float fbm3(vec3 p) {
	float s = 0.0, a = 0.5;
	for (int i = 0; i < 4; i++) { s += a * vnoise3(p); p *= 2.03; a *= 0.5; }
	return s / 0.9375;
}

vec3 star_shell(vec3 d, float shell, float chance, float radius) {
	vec3 p = d * shell;
	vec3 base = floor(p + 0.28) - 1.0;
	vec3 col = vec3(0.0);
	for (int i = 0; i < 8; i++) {
		vec3 cell = base + vec3(float(i & 1), float((i >> 1) & 1), float((i >> 2) & 1));
		if (hash3(cell) < 1.0 - chance) continue;
		vec3 star = cell + 0.2 + 0.6 * vec3(hash3(cell + 3.0), hash3(cell + 7.0), hash3(cell + 11.0));
		if (abs(length(star) - shell) > 0.35) continue;
		float off = length(star - d * dot(star, d));
		float lit = smoothstep(radius, 0.0, off);
		col += lit * (0.4 + 0.6 * hash3(cell + 9.0)) * mix(vec3(1.0, 0.9, 0.8), vec3(0.8, 0.9, 1.0), hash3(cell + 2.0));
	}
	return col;
}

vec3 space_backdrop(vec3 d) {
	vec3 col = vec3(0.012, 0.012, 0.03);
	float n = fbm3(d * 2.2 + vec3(fbm3(d * 3.0) * 1.2));
	float neb = smoothstep(0.42, 0.85, n);
	col += space_nebula * neb * 0.55 + space_nebula_b * smoothstep(0.6, 0.95, fbm3(d * 4.5 + 3.0)) * neb * 0.35;
	col += star_shell(d, 150.0, 0.16, 0.27) * 1.6;
	float s = max(dot(d, space_sun), 0.0);
	col += vec3(1.0, 0.92, 0.8) * (pow(s, 900.0) * 30.0 + pow(s, 40.0) * 0.25);
	return col;
}

void sky() {
	vec3 dir = EYEDIR;
	vec3 sun = LIGHT0_ENABLED ? LIGHT0_DIRECTION : normalize(vec3(0.3, 0.6, -0.4));
	vec3 col = sky_color(dir, sun);

	if (AT_QUARTER_RES_PASS) {
		// How much the clouds hide, marched without light: cheap, and in a channel with ten bits.
		COLOR = vec3(march_clouds(POSITION, dir, sun, col, false, march_jitter(FRAGCOORD.xy)).a);
	} else if (AT_HALF_RES_PASS) {
		// The clouds laid over the plain sky, at half resolution: the costly part. Stored on a
		// square-root curve, scaled down for headroom: the buffer clamps at one and dark storm or
		// ash clouds would band without the extra precision near black.
		vec4 c = march_clouds(POSITION, dir, sun, col, true, march_jitter(FRAGCOORD.xy));
		COLOR = sqrt(max(c.rgb, vec3(0.0)) * 0.25);
	} else {
		float day = smoothstep(-0.2, 0.15, sun.y);
		vec3 plain = col;
		// Faded out just below the horizon, not cut: seen from high up there is no hard line.
		float starlight = max(1.0 - day, stars_by_day) * (1.0 - overcast) * smoothstep(-0.08, 0.0, dir.y);
		if (starlight > 0.0) col += night_sky(dir, sun) * starlight;
		// Bodies show through a clear sky by day as well as by night, faded by the daylight's glare.
		vec4 b = bodies(dir, sun) * (1.0 - overcast * 0.9);
		col = col * (1.0 - b.a * mix(0.88, 1.0, 1.0 - day)) + b.rgb;

		// The sun itself, in front of everything but cloud: dimmer and warmer low down, behind more air.
		float sd = dot(dir, sun);
		float disc = smoothstep(cos(sun_radius * 1.15), cos(sun_radius), sd);
		float through = sun_air(sun.y);
		vec3 disc_color = mix(sunset, star_color, smoothstep(0.0, 0.3, sun.y)) * through * through;
		// Well under the glow threshold near the horizon: there it's a coloured disc, it doesn't bloom.
		float disc_energy = mix(1.5, 18.0, smoothstep(0.02, 0.4, sun.y));
		col += disc_color * disc * sun_energy * disc_energy * (1.0 - overcast) * smoothstep(-0.03, 0.0, dir.y);

		float cover;
		if (AT_CUBEMAP_PASS) {
			// Reflections, ambient light and the fog's aerial perspective: just the overall cover, no
			// march, eased in over the horizon (a hard step there showed in fogged distance and water).
			cover = cloud_cover * 0.6 * smoothstep(-0.02, 0.12, dir.y);
			col = mix(col, mix(cloud_shade, cloud_lit, 0.5) * (0.2 + 0.8 * day), cover);
		} else {
			// The clouds over the plain sky, plus whatever else is up there where they let it through.
			vec3 clouded = HALF_RES_COLOR.rgb;
			clouded = clouded * clouded * 4.0;
			cover = clamp(QUARTER_RES_COLOR.r, 0.0, 1.0);
			col = clouded + (col - plain) * (1.0 - cover);
		}
		if (debug_view == 1) col = vec3(cover);
		if (debug_view == 3) col = night_sky(dir, sun) * 6.0;
		if (debug_view == 2) col = HALF_RES_COLOR.rgb * HALF_RES_COLOR.rgb * 4.0;
		col = mix(col, overcast_color * (0.15 + 0.85 * day), overcast * smoothstep(-0.1, 0.25, dir.y) * 0.85);
		if (space_fade > 0.0) col = mix(col, space_backdrop(space_turn * dir), space_fade);
		COLOR = col;
	}
}
